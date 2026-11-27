// Application 360's writes, each in the shared side sheet (components/actions.tsx); the engine checks every one as it
// checks an AI client's write (D-36):
//   ProposeDecisionSheet   save_decision_proposal about the application, of any type the person's decision rights
//                          allow, or one type preset (the gate decision, a data use, retirement). A tier decision names
//                          the tier it sets, an autonomy level decision the level, an exception its standard and date.
//   TierAnswersSheet       answer_tier_questions for the application: answer, or answer again.
//   ArchitectureReviewSheet  request_architecture_review: please review this design.
//   DocumentCheckedSheet   confirm_document_checked: the document is still true.
//   DocumentCurrentSheet   record_document_link with status current: a person confirms a draft current.
//   HandOverSheet          propose_owner_change with a new holder and deputy of one owner role, in one decision.
//   DependencySheet        save_dependency: add a dependency, change its criticality, or end it.
//   ApplicationSearchField  an application found by name (find_applications), for the dependency sheet.
// Wave 4: a decision proposal and a hand-over open the submit follow-up right after they are
// saved ("Send it for approval now?", with the engine's answer), as the data sheets do; Cancel leaves it proposed and
// the page's banner still offers the submit.
import { Search, Workflow, X } from 'lucide-react';
import { useMemo, useState, type ReactElement, type ReactNode } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { PersonSearchField, useSettled } from '../../components/people';
import { ExternalLink } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Skeleton } from '../../components/ui/skeleton';
import { Textarea } from '../../components/ui/textarea';
import type { ApplicationSummary, DocumentationStatus, OwnerAssignment, PrincipalRef } from '../../data/contracts';
import { fmtDate, todayInZone } from '../../data/format';
import { useLabels, useListValues, useOutcomes } from '../../data/labels';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { readEngineAnswer, type WriteInput } from '../../data/writes';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';
import { ReviewDueHint } from '../../components/reviewDue';
import { cn } from '../../lib/utils';
import { TierHintView, TierQuestionsForm, useTierHint } from '../intake/shared';
import { answerList } from '../intake/logic';
import { buildHandOver, buildNewDependency, buildTierProposal, isProblem, sameId, tierOutcome, withArticle, type Problem, type TierProposalInput } from './logic';

const NONE = '__none__';

// A label inside a sentence: only its first letter lowered, so "Calls API of" reads "calls API of".
const lowerFirst = (label: string) => label.charAt(0).toLowerCase() + label.slice(1);
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

function triggerProps(p: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: true }) {
  return { id: p.id, 'aria-describedby': p['aria-describedby'], 'aria-invalid': p['aria-invalid'] };
}

function SelectBox(props: { value: string; onChange: (v: string) => void; choices: { value: string; label: string }[]; placeholder: string; control: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: true }; noneLabel?: string; className?: string }) {
  return (
    <Select value={props.value || (props.noneLabel ? NONE : '')} onValueChange={v => props.onChange(v === NONE ? '' : v)}>
      <SelectTrigger {...triggerProps(props.control)} className={cn('w-full', props.className)}><SelectValue placeholder={props.placeholder} /></SelectTrigger>
      <SelectContent className="max-h-[320px]">
        {props.noneLabel && <SelectItem value={NONE}>{props.noneLabel}</SelectItem>}
        {props.choices.map(c => <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>)}
      </SelectContent>
    </Select>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// A decision about the application
// ---------------------------------------------------------------------------------------------------------------------

export interface ProposeDecisionProps {
  applicationId: string;
  applicationName: string;
  // The type the sheet proposes; leave out to choose one of types.
  typeKey?: string;
  types?: string[];
  // An outcome to start from, such as retire on a portfolio decision.
  outcomeKey?: string;
  // The tier the latest answers computed and the tier the application holds, for a tier decision.
  computedTierKey?: string | null;
  appliedTierKey?: string | null;
  statement?: string;
  title?: string;
  description?: ReactNode;
  trigger: ReactElement;
  onProposed?: (decisionId: string, subject: string) => void;
}

export function ProposeDecisionSheet(props: ProposeDecisionProps) {
  const typeLabels = useLabels('decision-type');
  const tiers = useLabels('tier');
  const autonomy = useListValues('autonomy-level');
  const [chosenType, setChosenType] = useState('');
  const typeKey = props.typeKey ?? chosenType;
  const outcomes = useOutcomes(typeKey || 'none');
  const [statement, setStatement] = useState(props.statement ?? '');
  const [context, setContext] = useState('');
  const [outcome, setOutcome] = useState(props.outcomeKey ?? '');
  const [reason, setReason] = useState('');
  const [option, setOption] = useState('');
  const [whyNot, setWhyNot] = useState('');
  const [standard, setStandard] = useState('');
  const [reviewOn, setReviewOn] = useState('');
  const [tierKey, setTierKey] = useState(props.computedTierKey ?? props.appliedTierKey ?? '');
  const [level, setLevel] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  // The subject as it was proposed: the form resets once the follow-up opens.
  const [followSubject, setFollowSubject] = useState('');
  const reset = () => {
    setChosenType('');
    setStatement(props.statement ?? '');
    setContext('');
    setOutcome(props.outcomeKey ?? '');
    setReason('');
    setOption('');
    setWhyNot('');
    setStandard('');
    setReviewOn('');
    setTierKey(props.computedTierKey ?? props.appliedTierKey ?? '');
    setLevel('');
  };
  const isTier = typeKey === 'tier';
  const base = props.computedTierKey ?? props.appliedTierKey ?? null;
  const derived = isTier ? tierOutcome(tierKey, base, k => tiers.rank(k)) : null;
  const typeLabel = typeKey ? typeLabels.label(typeKey) : 'a';
  const outcomeLabel = (key: string | null) => outcomes.options.find(o => o.key === key)?.label ?? key ?? '';

  const build = (): WriteInput<'save_decision_proposal'> | Problem => {
    if (!typeKey) return { problem: 'Choose the kind of decision.', field: 'decisionTypeKey' };
    if (isTier) {
      const built = buildTierProposal({
        applicationId: props.applicationId,
        tierKey,
        baseTierKey: base,
        computedTierKey: props.computedTierKey ?? null,
        outcomeKeys: outcomes.options.map(o => o.key),
        statement,
        context,
        reason,
        rank: k => tiers.rank(k),
      });
      if (isProblem(built)) return built;
      const input: TierProposalInput = built;
      return input;
    }
    const s = statement.trim();
    const c = context.trim();
    const r = reason.trim();
    const o = option.trim();
    const w = whyNot.trim();
    if (!s) return { problem: 'Say what is decided, in one sentence.', field: 'statement' };
    if (!c) return { problem: 'Give the context.', field: 'context' };
    if (!outcome) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
    if (!r) return { problem: 'Say why.', field: 'reason' };
    if ((o && !w) || (!o && w)) return { problem: 'An option weighed needs the option and why it was not chosen.', field: 'optionsConsidered' };
    if (outcomes.needsOptions && !o) return { problem: 'This kind of decision needs at least one option weighed.', field: 'optionsConsidered' };
    const extra: Partial<TierProposalInput> = {};
    if (typeKey === 'exception') {
      if (!standard.trim()) return { problem: 'Name the standard the exception departs from.', field: 'exception.standard' };
      if (!ISO_DATE.test(reviewOn)) return { problem: 'Give the date the exception is reviewed again.', field: 'exception.reviewOn' };
      extra.exception = { standard: standard.trim(), reviewOn };
    }
    if (typeKey === 'autonomy-level' && outcome === 'agree') {
      if (!level) return { problem: 'Choose the autonomy level the decision agrees.', field: 'autonomyLevelKey' };
      extra.autonomyLevelKey = level;
    }
    const input: TierProposalInput = {
      ...extra,
      decisionTypeKey: typeKey,
      subjectKind: 'business-application',
      subjectId: props.applicationId,
      statement: s,
      context: c,
      proposedOutcomeKey: outcome,
      reason: r,
      ...(o ? { optionsConsidered: [{ option: o, whyNot: w }] } : {}),
    };
    return input;
  };

  const subject = `${typeKeyLabel(typeLabels.label(typeKey))} decision about ${props.applicationName}`;
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/app360/sheets#ProposeDecisionSheet"
      title={props.title ?? (props.typeKey ? `Propose ${withArticle(`${typeLabel.toLowerCase()} decision`)}` : 'Propose a decision')}
      description={props.description ?? <>About {props.applicationName}. It is saved as your proposal; nobody is asked until you submit it.</>}
      size="wide"
      trigger={props.trigger}
      open={open}
      input={build}
      submitLabel="Propose"
      preview
      // The form starts from the props each time it opens: a sheet kept mounted while the tier was still being read
      // opened with an empty statement (fix round 2, seen on S-23.12 at 390 px).
      onOpenChange={next => { if (next) reset(); setOpen(next); if (!next) reset(); }}
      onDone={(data, envelope) => {
        if (!data.recordId) return;
        props.onProposed?.(data.recordId, subject);
        setFollowSubject(subject);
        follow.start({ decisionId: data.recordId, answer: readEngineAnswer('save_decision_proposal', envelope) });
        setOpen(false);
        reset();
      }}
    >
      {!props.typeKey && (
        <Field label="Kind of decision" name="decisionTypeKey" required hint="The kinds your decision rights let you propose about this application.">
          {p => (
            <SelectBox
              control={p}
              value={chosenType}
              onChange={v => { setChosenType(v); setOutcome(''); }}
              choices={(props.types ?? []).map(k => ({ value: k, label: typeLabels.label(k) }))}
              placeholder="Choose a kind of decision"
            />
          )}
        </Field>
      )}
      <Field label="Decision" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="Context" name="context" required hint="The situation and what you weighed.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      {isTier ? (
        <>
          <Field label="The tier it sets" name="tierKey" required hint={base ? `Against ${tiers.label(base)}, ${props.computedTierKey ? 'the tier the latest answers compute' : 'the tier the application holds'}.` : 'Answer the tier questions first: a tier decision confirms or changes the computed tier.'}>
            {p => <SelectBox control={p} value={tierKey} onChange={setTierKey} choices={tiers.keys().map(k => ({ value: k, label: tiers.label(k) }))} placeholder="Choose the tier" />}
          </Field>
          <p className="m-0 text-sm text-navy-2" aria-live="polite">
            {derived ? <>Outcome: <span className="font-semibold text-navy">{outcomeLabel(derived)}</span>. The tier takes effect only when the approvers accept the decision.</> : 'The outcome follows from the tier you choose.'}
          </p>
        </>
      ) : (
        <Field label="Proposed outcome" name="proposedOutcomeKey" required>
          {p => outcomes.options.length ? (
            <SelectBox control={p} value={outcome} onChange={setOutcome} choices={outcomes.options.map(o => ({ value: o.key, label: o.label }))} placeholder="Choose an outcome" />
          ) : <Muted>{!typeKey ? 'Choose the kind of decision first.' : outcomes.loading ? 'Reading the outcomes this kind of decision allows.' : 'The outcomes could not be read. Close the sheet and try again.'}</Muted>}
        </Field>
      )}
      {typeKey === 'autonomy-level' && outcome === 'agree' && (
        <Field label="Autonomy level" name="autonomyLevelKey" required hint="The level the application is agreed to act at, once the decision is accepted.">
          {p => <SelectBox control={p} value={level} onChange={setLevel} choices={autonomy.choices} placeholder="Choose the level" />}
        </Field>
      )}
      {typeKey === 'exception' && (
        <>
          <Field label="Standard it departs from" name="exception.standard" required hint="The standard, guideline or rule the exception sets aside.">
            {p => <Input {...p} maxLength={200} value={standard} onChange={e => setStandard(e.target.value)} />}
          </Field>
          <Field label="Review on" name="exception.reviewOn" required hint="The date the exception is looked at again: today or later.">
            {p => <Input {...p} type="date" min={todayInZone()} value={reviewOn} onChange={e => setReviewOn(e.target.value)} className="w-[200px]" />}
          </Field>
        </>
      )}
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      {!isTier && (
        <div className="grid grid-cols-2 gap-2 narrow:grid-cols-1">
          <Field label="An option weighed" name="optionsConsidered" hint={outcomes.needsOptions ? 'Required for this kind of decision.' : 'Optional.'}>
            {p => <Input {...p} required={false} maxLength={500} value={option} onChange={e => setOption(e.target.value)} />}
          </Field>
          <Field label="Why not">
            {p => <Input {...p} maxLength={1000} value={whyNot} onChange={e => setWhyNot(e.target.value)} />}
          </Field>
        </div>
      )}
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={followSubject} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

const typeKeyLabel = (label: string) => label.charAt(0).toUpperCase() + label.slice(1);

// ---------------------------------------------------------------------------------------------------------------------
// Tier answers
// ---------------------------------------------------------------------------------------------------------------------

export function TierAnswersSheet(props: { applicationId: string; kindKey: string; kindLabel: string; current: Record<string, string>; trigger: ReactElement }) {
  const [answers, setAnswers] = useState<Record<string, string>>(props.current);
  return (
    <ActionSheet
      tool="answer_tier_questions" site="pages/app360/sheets#TierAnswersSheet"
      title="Answer the tier questions"
      description="The engine works out the tier from the answers and keeps them as the application's latest assessment. Only an accepted tier decision changes the tier the application holds."
      trigger={props.trigger}
      size="wide"
      input={() => {
        const list = Object.entries(answers).filter(([, a]) => !!a).map(([questionKey, answerKey]) => ({ questionKey, answerKey }));
        return list.length ? { businessApplicationId: props.applicationId, answers: list } : { problem: 'Answer at least one question.', field: 'answers' };
      }}
      submitLabel="Save the answers"
      preview="Check with the engine"
      onOpenChange={open => setAnswers(open ? props.current : {})}
    >
      <TierSheetBody applicationId={props.applicationId} kindKey={props.kindKey} kindLabel={props.kindLabel} answers={answers} setAnswers={setAnswers} />
    </ActionSheet>
  );
}

function TierSheetBody(props: { applicationId: string; kindKey: string; kindLabel: string; answers: Record<string, string>; setAnswers: (a: Record<string, string>) => void }) {
  // The application named, so the answer its declared data implies comes back as a suggestion (impliedAnswer, B24-22).
  const state = useRead('get_tier_questions', { applicationKindKey: props.kindKey, businessApplicationId: props.applicationId });
  const questions = useMemo(() => dataOf(state)?.questions ?? [], [state]);
  // Only the answers to the questions that apply are sent.
  const shown = useMemo(() => Object.fromEntries(answerList(questions, props.answers).map(a => [a.questionKey, a.answerKey])), [questions, props.answers]);
  const hint = useTierHint(questions, shown);
  if (state.status === 'loading') {
    return <div role="status" aria-label="Loading the tier questions" className="flex flex-col gap-2"><Skeleton className="h-24 w-full" /><Skeleton className="h-24 w-full" /></div>;
  }
  if (!state.envelope.ok) return <Muted>{`The tier questions could not be read: ${state.envelope.error.message}`}</Muted>;
  if (!questions.length) return <Muted>No tier question applies to {props.kindLabel.toLowerCase()}.</Muted>;
  return (
    <div className="flex flex-col gap-4">
      <TierHintView hint={hint} questions={questions} />
      <Field label={`The questions for ${props.kindLabel}`} name="answers">
        {p => (
          <div id={p.id} aria-describedby={p['aria-describedby']}>
            <TierQuestionsForm questions={questions} answers={shown} onChange={next => props.setAnswers(next)} idPrefix="app360-tier" implied={dataOf(state)?.impliedAnswer ?? null} />
          </div>
        )}
      </Field>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Architecture review
// ---------------------------------------------------------------------------------------------------------------------

export function ArchitectureReviewSheet(props: { applicationId: string; applicationName: string; changes: { changeRequestId: string; title: string }[]; trigger: ReactElement }) {
  const kinds = useLabels('link-kind');
  const evidenceKinds = kinds.keys().filter(k => kinds.field<string>(k, 'linkCategory') === 'evidence');
  const [reason, setReason] = useState('');
  const [dueOn, setDueOn] = useState('');
  const [change, setChange] = useState('');
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const reset = () => { setReason(''); setDueOn(''); setChange(''); setUrl(''); setLabel(''); };
  return (
    <ActionSheet
      tool="request_architecture_review" site="pages/app360/sheets#ArchitectureReviewSheet"
      title="Request an architecture review"
      description={<>For {props.applicationName}. The architect is asked to review the design. When a review of this type is already open, the engine returns it instead of opening a second one.</>}
      trigger={props.trigger}
      input={() => {
        const r = reason.trim();
        if (!r) return { problem: 'Say what the architect should look at, and why now.', field: 'reason' };
        if (dueOn && !ISO_DATE.test(dueOn)) return { problem: 'The due date is not a date.', field: 'dueOn' };
        const u = url.trim();
        const l = label.trim();
        if ((u && !l) || (!u && l)) return { problem: 'Evidence needs both the link and its label.', field: 'evidence' };
        if (u && !/^https:\/\//i.test(u)) return { problem: 'Give the full https address of the evidence.', field: 'evidence' };
        return {
          businessApplicationId: props.applicationId,
          reason: r,
          ...(dueOn ? { dueOn } : {}),
          ...(change ? { changeRequestId: change } : {}),
          ...(u ? { evidence: [{ linkKindKey: evidenceKinds[0] ?? 'evidence-url', url: u, label: l }] } : {}),
        };
      }}
      submitLabel="Request the review"
      preview
      onOpenChange={open => { if (!open) reset(); }}
    >
      <Field label="What to review" name="reason" required hint="The architect reads this first.">
        {p => <Textarea {...p} rows={4} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      <Field label="Due on" name="dueOn" hint={<ReviewDueHint />}>
        {p => <Input {...p} type="date" value={dueOn} onChange={e => setDueOn(e.target.value)} className="w-[200px]" />}
      </Field>
      {props.changes.length > 0 && (
        <Field label="For a change" name="changeRequestId" hint="Optional. The change the design is for.">
          {p => <SelectBox control={p} value={change} onChange={setChange} choices={props.changes.map(c => ({ value: c.changeRequestId, label: c.title }))} placeholder="No change" noneLabel="No change" />}
        </Field>
      )}
      <fieldset className="m-0 flex min-w-0 flex-col gap-3 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">Evidence <span className="text-xs font-normal text-navy-3">Optional, such as the design or an architecture decision record</span></legend>
        <Field label="Link" name="evidence" hint="A full https address.">
          {p => <Input {...p} type="url" inputMode="url" value={url} maxLength={2000} onChange={e => setUrl(e.target.value)} placeholder="https://" />}
        </Field>
        <Field label="Label">
          {p => <Input {...p} value={label} maxLength={200} onChange={e => setLabel(e.target.value)} />}
        </Field>
      </fieldset>
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------------------------------------------------

export type DocRow = DocumentationStatus['applications'][number]['documents'][number];

// confirm_document_checked with stillTrue: the person read the document and it still describes the application.
// The accessible name of a "Confirm it is still true" button: it names the document and the application, so two on one
// page each say which document they confirm (QA walk, wave 4b). The visible words stay the same.
export function checkedLabel(typeLabel: string, applicationName: string): string {
  return `Confirm the ${typeLabel.toLowerCase()} of ${applicationName} is still true`;
}

export function DocumentCheckedSheet(props: { doc: DocRow; typeLabel: string; trigger: ReactElement }) {
  const [note, setNote] = useState('');
  return (
    <ActionSheet
      tool="confirm_document_checked" site="pages/app360/sheets#DocumentCheckedSheet"
      title={`Confirm the ${props.typeLabel.toLowerCase()} is still true`}
      description="You read the document and it still describes the application. Last checked becomes today, your own calendar day, and the stale date moves on."
      trigger={props.trigger}
      input={() => ({ documentLinkId: props.doc.documentLinkId, stillTrue: true, ...(note.trim() ? { note: note.trim() } : {}) })}
      submitLabel="Confirm"
      preview
      onOpenChange={open => { if (!open) setNote(''); }}
    >
      <p className="m-0 text-sm text-navy-2">The document: <ExternalLink href={props.doc.url}>{props.typeLabel}</ExternalLink>{props.doc.staleOn ? `. Stale on ${fmtDate(props.doc.staleOn)}.` : '.'}</p>
      <Field label="Note" name="note" hint="Optional. What you checked.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// record_document_link with status current: a person confirms a draft current. An AI client can only store a draft.
export function DocumentCurrentSheet(props: { applicationId: string; doc: DocRow; typeLabel: string; trigger: ReactElement }) {
  const d = props.doc;
  return (
    <ActionSheet
      tool="record_document_link" site="pages/app360/sheets#DocumentCurrentSheet"
      title={`Confirm the ${props.typeLabel.toLowerCase()} current`}
      description="You read the document and it is ready to rely on. The engine records it as current under your name and sets its stale date."
      trigger={props.trigger}
      input={() => ({
        documentLinkId: d.documentLinkId,
        businessApplicationId: props.applicationId,
        documentTypeKey: d.documentTypeKey,
        url: d.url,
        statusMeaning: 'current' as const,
        ...(d.lastUpdatedOn ? { lastUpdatedOn: d.lastUpdatedOn } : {}),
        ...(d.owner ? { documentOwnerObjectId: d.owner.objectId } : {}),
        // The kind the link was recorded with, unchanged: the engine checks the address against its pattern.
        ...(d.linkKindKey ? { linkKindKey: d.linkKindKey } : {}),
      })}
      submitLabel="Confirm current"
      preview
    >
      <p className="m-0 text-sm text-navy-2">
        The link stays <ExternalLink href={d.url}>{d.url}</ExternalLink>
        {d.owner ? `, and ${d.owner.displayLabel} stays its owner.` : '.'}
      </p>
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Hand over
// ---------------------------------------------------------------------------------------------------------------------

export function HandOverSheet(props: {
  applicationId: string;
  applicationName: string;
  ownerRoleKey: string;
  holder: OwnerAssignment | null;
  deputy: OwnerAssignment | null;
  trigger: ReactElement;
  onProposed?: (decisionId: string, subject: string) => void;
}) {
  const roles = useLabels('owner-role');
  const roleName = roles.label(props.ownerRoleKey);
  const [holder, setHolder] = useState<PrincipalRef | null>(null);
  const [deputy, setDeputy] = useState<PrincipalRef | null>(null);
  const [startOn, setStartOn] = useState('');
  const [reason, setReason] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  const reset = () => { setHolder(null); setDeputy(null); setStartOn(''); setReason(''); };
  const subject = `Hand-over of the ${roleName.toLowerCase()} role of ${props.applicationName}`;
  return (
    <>
    <ActionSheet
      tool="propose_owner_change" site="pages/app360/sheets#HandOverSheet"
      title={`Hand over: ${roleName.toLowerCase()}`}
      description={<>For {props.applicationName}. One ownership decision names the new holder and deputy and ends the ones they replace. Nothing changes until it is accepted; the new owners are asked by name.</>}
      trigger={props.trigger}
      size="wide"
      input={() => buildHandOver({
        applicationId: props.applicationId,
        ownerRoleKey: props.ownerRoleKey,
        holderObjectId: holder?.objectId ?? '',
        endingHolder: props.holder,
        deputyObjectId: deputy?.objectId ?? '',
        endingDeputy: props.deputy,
        startOn,
        reason,
      })}
      submitLabel="Propose the hand-over"
      preview
      open={open}
      onOpenChange={next => { setOpen(next); if (!next) reset(); }}
      onDone={(data, envelope) => {
        if (!data.decisionId) return;
        props.onProposed?.(data.decisionId, subject);
        follow.start({ decisionId: data.decisionId, answer: readEngineAnswer('propose_owner_change', envelope) });
        setOpen(false);
        reset();
      }}
    >
      <p className="m-0 text-sm text-navy-2">
        Now: {props.holder ? `${props.holder.principal.displayLabel} holds the role` : 'nobody holds the role'}
        {props.deputy ? `, with ${props.deputy.principal.displayLabel} as deputy.` : ', with no deputy.'}
      </p>
      <PersonSearchField label={`New ${roleName.toLowerCase()}`} name="changes.addObjectId" required value={holder} onChange={setHolder} exclude={props.holder ? [props.holder.principal.objectId] : undefined} activeOnly />
      <PersonSearchField label="New deputy" name="changes.isDeputy" value={deputy} onChange={setDeputy} hint="Optional. Type two or more letters of the name." activeOnly exclude={holder ? [holder.objectId] : undefined} />
      <Field label="Starts on" name="changes.startOn" hint="Once the decision is accepted. Leave it empty to start on acceptance.">
        {p => <Input {...p} type="date" value={startOn} onChange={e => setStartOn(e.target.value)} className="w-[200px]" />}
      </Field>
      <Field label="Reason" name="reason" required hint="The approvers read this, such as: the app graduates from the maker to IT.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={subject} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Dependencies
// ---------------------------------------------------------------------------------------------------------------------

// An application found by name. The chosen one shows with a Change button.
export function ApplicationSearchField(props: { label: string; name: string; value: ApplicationSummary | null; onChange: (app: ApplicationSummary | null) => void; exclude?: string[]; hint?: string; required?: boolean }) {
  const [textValue, setText] = useState('');
  const settled = useSettled(textValue.trim());
  const asking = settled.length >= 2 && !props.value;
  const state = useRead('find_applications', asking ? { text: settled, top: 8 } : null);
  const found = (dataOf(state)?.items ?? []).filter(a => !(props.exclude ?? []).some(x => sameId(x, a.businessApplicationId)));
  const failed = asking && state.status === 'done' && !state.envelope.ok ? state.envelope : null;
  if (props.value) {
    return (
      <Field label={props.label} name={props.name} required={props.required} hint={props.hint}>
        {p => (
          <div className="flex items-center gap-2 rounded-md border border-line bg-secondary px-3 py-2">
            <Workflow className="size-4 shrink-0 text-navy-3" aria-hidden />
            <span id={p.id} className="min-w-0 flex-1 text-sm font-medium text-navy [overflow-wrap:anywhere]" aria-describedby={p['aria-describedby']}>{props.value!.name}</span>
            <Button type="button" variant="ghost" size="sm" onClick={() => { props.onChange(null); setText(''); }} aria-label={`Choose an application other than ${props.value!.name}`}>
              <X className="size-4" aria-hidden />
              Change
            </Button>
          </div>
        )}
      </Field>
    );
  }
  return (
    <Field label={props.label} name={props.name} required={props.required} hint={props.hint ?? 'Type two or more letters of the name.'}>
      {p => (
        <div className="flex flex-col gap-1.5">
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
            <Input {...p} required={false} type="search" value={textValue} onChange={e => setText(e.target.value)} placeholder="Search by name" autoComplete="off" spellCheck={false} maxLength={100} className="pl-8" />
          </div>
          {asking && (
            <div aria-live="polite" className="flex flex-col gap-1">
              {state.status === 'loading' && <span className="text-xs text-navy-3">Searching the register</span>}
              {failed && <span className="text-xs text-danger">The search did not answer: {failed.error.message}</span>}
              {state.status === 'done' && !failed && found.length === 0 && <span className="text-xs text-navy-2">No registered application matches "{settled}".</span>}
              {found.length > 0 && (
                <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={`Applications that match ${settled}`}>
                  {found.map(a => (
                    <li key={a.businessApplicationId}>
                      <Button type="button" variant="outline" size="sm" className="h-auto w-full justify-start py-1.5 text-left" onClick={() => props.onChange(a)}>
                        <Workflow className="size-4 shrink-0 text-navy-3" aria-hidden />
                        <span className="min-w-0 [overflow-wrap:anywhere]">{a.name}</span>
                        <span className="text-xs text-navy-3">{[a.kind.label, a.tier?.label].filter(Boolean).join(', ')}</span>
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      )}
    </Field>
  );
}

// A new dependency of the application, either way round.
export function NewDependencySheet(props: {
  applicationId: string;
  applicationName: string;
  // Start on a data dependency (reads data from) for the Data section.
  dataOnly?: boolean;
  trigger: ReactElement;
}) {
  const types = useLabels('dependency-type');
  const criticality = useListValues('criticality');
  const [direction, setDirection] = useState<'depends-on' | 'used-by'>('depends-on');
  const [other, setOther] = useState<ApplicationSummary | null>(null);
  const firstType = props.dataOnly ? 'reads-data-from' : '';
  const [typeKey, setTypeKey] = useState(firstType);
  const [crit, setCrit] = useState('');
  const [notes, setNotes] = useState('');
  const reset = () => { setDirection('depends-on'); setOther(null); setTypeKey(firstType); setCrit(''); setNotes(''); };
  const typeChoices = types.keys()
    .filter(k => !props.dataOnly || k === 'reads-data-from' || k === 'writes-data-to')
    .map(k => ({ value: k, label: direction === 'depends-on' ? types.label(k) : types.field<string>(k, 'inverseLabel') ?? types.label(k) }));
  const sentence = other && typeKey
    ? direction === 'depends-on'
      ? `${props.applicationName} ${lowerFirst(types.label(typeKey))} ${other.name}.`
      : `${other.name} ${lowerFirst(types.label(typeKey))} ${props.applicationName}.`
    : null;
  return (
    <ActionSheet
      tool="save_dependency" site="pages/app360/sheets#NewDependencySheet"
      title={props.dataOnly ? 'Add a data link' : 'Add a dependency'}
      description="Recorded as you report it. The owner of the depending application, an architect or the platform admin records it; the engine checks."
      trigger={props.trigger}
      input={() => buildNewDependency({ rootId: props.applicationId, direction, otherId: other?.businessApplicationId ?? '', dependencyTypeKey: typeKey, criticalityKey: crit, notes })}
      submitLabel="Record it"
      preview
      onOpenChange={open => { if (!open) reset(); }}
    >
      <Field label="Which way" name="direction" required>
        {p => (
          <SelectBox
            control={p}
            value={direction}
            onChange={v => setDirection(v === 'used-by' ? 'used-by' : 'depends-on')}
            choices={[
              { value: 'depends-on', label: `${props.applicationName} needs the other application` },
              { value: 'used-by', label: `The other application needs ${props.applicationName}` },
            ]}
            placeholder="Choose"
          />
        )}
      </Field>
      <ApplicationSearchField label="The other application" name={direction === 'depends-on' ? 'toApplicationId' : 'fromApplicationId'} required value={other} onChange={setOther} exclude={[props.applicationId]} />
      <Field label="Kind of dependency" name="dependencyTypeKey" required>
        {p => <SelectBox control={p} value={typeKey} onChange={setTypeKey} choices={typeChoices} placeholder="Choose the kind" />}
      </Field>
      <Field label="Criticality" name="criticalityKey" hint="How badly the depending application is hit if the other one fails.">
        {p => <SelectBox control={p} value={crit} onChange={setCrit} choices={criticality.choices} placeholder="Not set" noneLabel="Not set" className="w-[220px]" />}
      </Field>
      <Field label="Notes" name="notes" hint="Optional. Any detail an architect needs.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} />}
      </Field>
      {sentence && <p className="m-0 rounded-md bg-secondary px-3 py-2 text-sm text-navy" aria-live="polite">{sentence}</p>}
    </ActionSheet>
  );
}

export interface DependencyEdit {
  edge: { fromApplicationId: string; toApplicationId: string; dependencyTypeKey: string; fromName: string; toName: string };
  criticalityKey: string | null;
  mode: 'change' | 'end';
}

// Change the criticality of a link, or end it. One open link per pair and type, so the depending application, the one
// it depends on and the type find it. A list whose row may vanish after the write (an ended link leaves the walk) opens
// it from outside the list (open and onOpenChange, no trigger), so the engine's answer stays in view; give it a key per
// edit so each starts fresh.
export function DependencyChangeSheet(props: DependencyEdit & {
  today: string;
  trigger?: ReactElement;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}) {
  const types = useLabels('dependency-type');
  const criticality = useListValues('criticality');
  const [crit, setCrit] = useState(props.criticalityKey ?? '');
  const [notes, setNotes] = useState('');
  const [endedOn, setEndedOn] = useState(props.today);
  const sentence = `${props.edge.fromName} ${lowerFirst(types.label(props.edge.dependencyTypeKey))} ${props.edge.toName}`;
  const end = props.mode === 'end';
  return (
    <ActionSheet
      tool="save_dependency" site="pages/app360/sheets#DependencyChangeSheet"
      title={end ? 'End this dependency' : 'Change this dependency'}
      description={<>{sentence}. {end ? 'An ended dependency stays in the history and never changes again.' : 'Recorded as you report it.'}</>}
      trigger={props.trigger}
      open={props.open}
      input={() => {
        const base = { fromApplicationId: props.edge.fromApplicationId, toApplicationId: props.edge.toApplicationId, dependencyTypeKey: props.edge.dependencyTypeKey };
        const n = notes.trim();
        if (end) {
          if (!ISO_DATE.test(endedOn)) return { problem: 'Give the date the dependency ended.', field: 'endedOn' };
          return { ...base, endedOn, ...(n ? { notes: n } : {}) };
        }
        if (!crit && !n) return { problem: 'Change the criticality or add a note.', field: 'criticalityKey' };
        return { ...base, ...(crit ? { criticalityKey: crit } : {}), ...(n ? { notes: n } : {}) };
      }}
      submitLabel={end ? 'End it' : 'Save'}
      submitTone={end ? 'destructive' : 'default'}
      preview
      confirm={end ? { title: 'End this dependency?', body: `${sentence} no longer holds from ${fmtDate(endedOn)}. This cannot be undone; a new link can be recorded later.`, confirmLabel: 'End it', tone: 'destructive' } : undefined}
      onOpenChange={open => {
        if (!open) { setCrit(props.criticalityKey ?? ''); setNotes(''); setEndedOn(props.today); }
        props.onOpenChange?.(open);
      }}
    >
      {end ? (
        <Field label="Ended on" name="endedOn" required>
          {p => <Input {...p} type="date" value={endedOn} max={props.today} onChange={e => setEndedOn(e.target.value)} className="w-[200px]" />}
        </Field>
      ) : (
        <Field label="Criticality" name="criticalityKey">
          {p => <SelectBox control={p} value={crit} onChange={setCrit} choices={criticality.choices} placeholder="Not set" className="w-[220px]" />}
        </Field>
      )}
      <Field label="Note" name="notes" hint="Optional.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

