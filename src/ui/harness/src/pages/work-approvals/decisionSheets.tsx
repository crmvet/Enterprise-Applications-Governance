// The decision page's own writes, each in the shared side sheet (components/actions.tsx):
//   SupersedeSheet  propose a decision that supersedes an accepted one (save_decision_proposal, supersedesDecisionId);
//                   the new proposal is saved, not submitted. Wave 4b: the sheet then closes and asks "Send it
//                   for approval now?" with the engine's answer on top (data/sheetParts.tsx SubmitFollowUp), as every
//                   proposal sheet does; Cancel leaves it proposed for its own page.
//   EvidenceSheet   add an evidence link to the decision (save_link, linkCategory evidence)
//   EditProposalSheet  change a proposal, or an AI draft, before anyone is asked (save_decision_proposal, decisionId).
//                   It is opened on the decision page, which submits it, so it asks nothing next.
//   NewProposalSheet  propose a decision of a given type about a record, such as the priority of an intake request.
//                   Wave 4b: it closes once the proposal is saved and hands the decision and the engine's answer
//                   to onProposed; the page that opened it holds the follow-up, because the sheet's button may leave the
//                   page once the record reads again.
// An exception decision also names the standard it departs from and the date it is reviewed again (ExceptionFields).
// The engine checks each exactly as it checks an AI client's write.
import { FilePen, Link2, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { DecisionDetail, DecisionSubjectKind } from '../../data/contracts';
import { externalHref } from '../../data/format';
import { useOutcomes } from '../../data/labels';
import { proposalHandOff, type EngineAnswer } from '../../data/writes';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';
import { optionsWeighed } from './logic';
import { dataOf, useTool } from '../../data/toolClient';

// save_decision_proposal takes no configuration draft, data domain or data location subject (a domain's or a location's
// decisions are ownership decisions, through propose_owner_change).
type ProposalSubjectKind = Exclude<DecisionSubjectKind, 'configuration-draft' | 'data-domain' | 'data-location'>;

const EXCEPTION = 'exception';

// The standard an exception departs from and the date it is reviewed again: required for an exception decision, and
// only there (document 23, P04 step 6).
function ExceptionFields(props: { standard: string; reviewOn: string; setStandard: (v: string) => void; setReviewOn: (v: string) => void }) {
  return (
    <>
      <Field label="Standard it departs from" name="exception.standard" required hint="The standard, guideline or rule the exception sets aside, in plain words.">
        {p => <Input {...p} maxLength={200} value={props.standard} onChange={e => props.setStandard(e.target.value)} />}
      </Field>
      <Field label="Review on" name="exception.reviewOn" required hint="The date the exception is looked at again: today or later.">
        {p => <Input {...p} type="date" value={props.reviewOn} onChange={e => props.setReviewOn(e.target.value)} className="w-[200px]" />}
      </Field>
    </>
  );
}

// The exception input, or a problem for the form.
function exceptionInput(typeKey: string, standard: string, reviewOn: string): { exception?: { standard: string; reviewOn: string } } | { problem: string; field: string } {
  if (typeKey !== EXCEPTION) return {};
  if (!standard.trim()) return { problem: 'Name the standard the exception departs from.', field: 'exception.standard' };
  if (!/^\d{4}-\d{2}-\d{2}$/.test(reviewOn)) return { problem: 'Give the date the exception is reviewed again.', field: 'exception.reviewOn' };
  return { exception: { standard: standard.trim(), reviewOn } };
}

export function SupersedeSheet({ detail }: { detail: DecisionDetail }) {
  const d = detail.decision;
  const subject = detail.subject;
  const outcomes = useOutcomes(d.decisionType.key);
  const [statement, setStatement] = useState(d.statement);
  const [context, setContext] = useState('');
  const [outcome, setOutcome] = useState(d.outcome?.key ?? '');
  const [reason, setReason] = useState('');
  const [option, setOption] = useState('');
  const [whyNot, setWhyNot] = useState('');
  const [standard, setStandard] = useState(d.exception?.standard ?? '');
  const [reviewOn, setReviewOn] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  const reset = () => {
    setStatement(d.statement);
    setContext('');
    setStandard(d.exception?.standard ?? '');
    setReviewOn('');
    setOutcome(d.outcome?.key ?? '');
    setReason('');
    setOption('');
    setWhyNot('');
  };
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/work-approvals/decisionSheets#SupersedeSheet"
      title="Propose a superseding decision"
      description={`A new ${d.decisionType.label.toLowerCase()} decision about ${subject.label} that replaces this one once it is accepted. It is saved as your proposal, and the next sheet asks whether to send it for approval now; nobody is asked before that.`}
      size="wide"
      open={open}
      trigger={(
        <Button size="sm" variant="outline">
          <FilePen aria-hidden />
          Supersede
        </Button>
      )}
      input={() => {
        const s = statement.trim();
        const c = context.trim();
        const r = reason.trim();
        const o = option.trim();
        const w = whyNot.trim();
        if (!s) return { problem: 'Say what the new decision is.', field: 'statement' };
        if (!c) return { problem: 'Say what changed since this decision: the context.', field: 'context' };
        if (!outcome) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
        if (!r) return { problem: 'Say why.', field: 'reason' };
        if ((o && !w) || (!o && w)) return { problem: 'Give the option weighed and why it was not chosen, or leave both empty.', field: 'optionsConsidered' };
        if (outcomes.needsOptions && !o) return { problem: 'This decision type needs at least one option weighed.', field: 'optionsConsidered' };
        const exception = exceptionInput(d.decisionType.key, standard, reviewOn);
        if ('problem' in exception) return exception;
        return {
          ...exception,
          decisionTypeKey: d.decisionType.key,
          subjectKind: subject.subjectKind as ProposalSubjectKind,
          subjectId: subject.subjectId,
          statement: s,
          context: c,
          proposedOutcomeKey: outcome,
          reason: r,
          supersedesDecisionId: d.decisionId,
          ...(o && w ? { optionsConsidered: [{ option: o, whyNot: w }] } : {}),
        };
      }}
      submitLabel="Save the proposal"
      preview
      onOpenChange={next => { setOpen(next); if (!next) reset(); }}
      onDone={(_, envelope) => {
        const handed = proposalHandOff(envelope);
        if (!handed) return;
        follow.start(handed);
        setOpen(false);
        reset();
      }}
    >
      <Field label="Statement" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="Context" name="context" required hint="What changed since this decision, and what the new one rests on.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label="Proposed outcome" name="proposedOutcomeKey" required>
        {p => outcomes.options.length ? (
          <Select value={outcome} onValueChange={setOutcome}>
            <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full">
              <SelectValue placeholder="Choose an outcome" />
            </SelectTrigger>
            <SelectContent>
              {outcomes.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        ) : (
          <Muted>{outcomes.loading ? 'Reading the outcomes this decision type allows.' : 'The outcomes of this decision type could not be read. Close the sheet and try again.'}</Muted>
        )}
      </Field>
      <Field label="Reason" name="reason" required hint="Why this outcome. Stored on the decision; everyone who may read it sees it.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      {d.decisionType.key === EXCEPTION && <ExceptionFields standard={standard} reviewOn={reviewOn} setStandard={setStandard} setReviewOn={setReviewOn} />}
      <Field
        label="Option weighed"
        name="optionsConsidered"
        required={outcomes.needsOptions}
        hint={outcomes.needsOptions ? 'This decision type records the options weighed.' : 'Optional: an option you weighed and did not choose.'}
      >
        {p => <Input {...p} maxLength={500} value={option} onChange={e => setOption(e.target.value)} />}
      </Field>
      <Field label="Why not that option" hint="Needed when you name an option.">
        {p => <Textarea {...p} rows={2} maxLength={1000} value={whyNot} onChange={e => setWhyNot(e.target.value)} />}
      </Field>
    </ActionSheet>
    {follow.data && (
      <SubmitFollowUp
        decisionId={follow.data.decisionId}
        previous={follow.data.answer}
        subject={`The ${d.decisionType.label.toLowerCase()} decision that supersedes this one`}
        open={follow.open}
        onOpenChange={follow.onOpenChange}
      />
    )}
    </>
  );
}

export function EvidenceSheet({ decisionId }: { decisionId: string }) {
  const kinds = useTool('get_configuration', { entity: 'link-kind', top: 100 });
  const evidenceKinds = (dataOf(kinds)?.rows ?? []).filter(r => r.isActive && r.fields.linkCategory === 'evidence');
  const [kind, setKind] = useState('');
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const chosen = kind || evidenceKinds[0]?.key || '';
  return (
    <ActionSheet
      tool="save_link" site="pages/work-approvals/decisionSheets#EvidenceSheet"
      title="Add an evidence link"
      description="A link to proof held elsewhere, such as a test report or a signed record. The product stores the link, not the file."
      trigger={(
        <Button size="sm" variant="outline">
          <Link2 aria-hidden />
          Add evidence
        </Button>
      )}
      input={() => {
        const u = url.trim();
        const l = label.trim();
        if (!chosen) return { problem: 'No evidence link kind is configured, so the link cannot be saved.', field: 'linkKindKey' };
        if (!externalHref(u)) return { problem: 'Give a full https address.', field: 'url' };
        if (!l) return { problem: 'Give the link a short label.', field: 'label' };
        return { linkCategory: 'evidence' as const, subjectKind: 'decision' as const, subjectId: decisionId, linkKindKey: chosen, url: u, label: l };
      }}
      submitLabel="Add the link"
      preview
      onOpenChange={open => { if (!open) { setKind(''); setUrl(''); setLabel(''); } }}
    >
      {evidenceKinds.length > 1 && (
        <Field label="Kind of link" name="linkKindKey" required>
          {p => (
            <Select value={chosen} onValueChange={setKind}>
              <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {evidenceKinds.map(k => <SelectItem key={k.key} value={k.key}>{k.label}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
        </Field>
      )}
      {evidenceKinds.length === 1 && <Muted>Kind of link: {evidenceKinds[0].label}.</Muted>}
      {kinds.status === 'done' && !evidenceKinds.length && (
        <Muted>{kinds.envelope.ok ? 'The configuration in force has no evidence link kind.' : 'The link kinds could not be read.'}</Muted>
      )}
      <Field label="Address" name="url" required hint="Starts with https://">
        {p => <Input {...p} type="url" inputMode="url" maxLength={2048} placeholder="https://" value={url} onChange={e => setUrl(e.target.value)} />}
      </Field>
      <Field label="Label" name="label" required hint="What the link shows, such as 'Penetration test report, November'.">
        {p => <Input {...p} maxLength={200} value={label} onChange={e => setLabel(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// Edit a proposal before anyone is asked (D-28: an AI drafts, a person accepts, and may change it first). The engine
// lets the person who proposed the decision change it while it is proposed and on no approval card; save_decision_proposal
// with decisionId replaces the fields. Submitting stays a separate step on the decision page.
export function EditProposalSheet({ detail, aiDraft }: { detail: DecisionDetail; aiDraft: boolean }) {
  const d = detail.decision;
  const subject = detail.subject;
  const outcomes = useOutcomes(d.decisionType.key);
  const [standard, setStandard] = useState(d.exception?.standard ?? '');
  const [reviewOn, setReviewOn] = useState(d.exception?.reviewOn ?? '');
  const initialOptions = () => {
    const list = optionsWeighed(d.optionsConsidered);
    return list && list.length ? list : [{ option: '', whyNot: '' }];
  };
  const [statement, setStatement] = useState(d.statement);
  const [context, setContext] = useState(d.context ?? '');
  const [outcome, setOutcome] = useState(d.outcome?.key ?? '');
  const [reason, setReason] = useState(d.reason ?? '');
  const [options, setOptions] = useState<{ option: string; whyNot: string }[]>(initialOptions);
  const reset = () => {
    setStatement(d.statement);
    setContext(d.context ?? '');
    setOutcome(d.outcome?.key ?? '');
    setReason(d.reason ?? '');
    setOptions(initialOptions());
  };
  return (
    <ActionSheet
      tool="save_decision_proposal" site="pages/work-approvals/decisionSheets#EditProposalSheet"
      title={aiDraft ? 'Edit the AI draft' : 'Edit the proposal'}
      description={aiDraft
        ? 'Change what the AI client drafted before you submit it. Nobody is asked until you submit it from this page.'
        : 'Change the proposal before you submit it. Nobody is asked until you submit it from this page.'}
      size="wide"
      trigger={(
        <Button size="sm" variant="outline">
          <FilePen aria-hidden />
          Edit
        </Button>
      )}
      input={() => {
        const s = statement.trim();
        const c = context.trim();
        const r = reason.trim();
        const opts = options.map(o => ({ option: o.option.trim(), whyNot: o.whyNot.trim() })).filter(o => o.option || o.whyNot);
        if (!s) return { problem: 'Say what is decided.', field: 'statement' };
        if (!c) return { problem: 'Give the context.', field: 'context' };
        if (!outcome) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
        if (!r) return { problem: 'Say why.', field: 'reason' };
        if (opts.some(o => !o.option || !o.whyNot)) return { problem: 'Each option weighed needs the option and why it was not chosen.', field: 'optionsConsidered' };
        if (outcomes.needsOptions && !opts.length) return { problem: 'This decision type needs at least one option weighed.', field: 'optionsConsidered' };
        const exception = exceptionInput(d.decisionType.key, standard, reviewOn);
        if ('problem' in exception) return exception;
        return {
          ...exception,
          decisionId: d.decisionId,
          decisionTypeKey: d.decisionType.key,
          subjectKind: subject.subjectKind as ProposalSubjectKind,
          subjectId: subject.subjectId,
          statement: s,
          context: c,
          proposedOutcomeKey: outcome,
          reason: r,
          ...(d.confidence ? { confidenceKey: d.confidence.key } : {}),
          ...(d.supersedes ? { supersedesDecisionId: d.supersedes } : {}),
          ...(d.amends ? { amendsDecisionId: d.amends } : {}),
          ...(opts.length ? { optionsConsidered: opts } : {}),
        };
      }}
      submitLabel="Save the changes"
      preview
      onOpenChange={open => { if (!open) reset(); }}
    >
      <Field label="Statement" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="Context" name="context" required>
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label="Proposed outcome" name="proposedOutcomeKey" required>
        {p => outcomes.options.length ? (
          <Select value={outcome} onValueChange={setOutcome}>
            <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full">
              <SelectValue placeholder="Choose an outcome" />
            </SelectTrigger>
            <SelectContent>
              {outcomes.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        ) : (
          <Muted>{outcomes.loading ? 'Reading the outcomes this decision type allows.' : 'The outcomes of this decision type could not be read. Close the sheet and try again.'}</Muted>
        )}
      </Field>
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      {d.decisionType.key === EXCEPTION && <ExceptionFields standard={standard} reviewOn={reviewOn} setStandard={setStandard} setReviewOn={setReviewOn} />}
      <fieldset className="m-0 flex min-w-0 flex-col gap-3 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">
          Options weighed <span className="text-xs font-normal text-navy-3">{outcomes.needsOptions ? 'Required for this decision type' : 'Optional'}</span>
        </legend>
        {options.map((o, i) => (
          <div key={i} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] items-end gap-2 narrow:grid-cols-1">
            <Field label={`Option ${i + 1}`} name={i === 0 ? 'optionsConsidered' : undefined}>
              {p => <Input {...p} value={o.option} maxLength={500} onChange={e => setOptions(list => list.map((x, j) => (j === i ? { ...x, option: e.target.value } : x)))} />}
            </Field>
            <Field label="Why not">
              {p => <Input {...p} value={o.whyNot} maxLength={1000} onChange={e => setOptions(list => list.map((x, j) => (j === i ? { ...x, whyNot: e.target.value } : x)))} />}
            </Field>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label={`Remove option ${i + 1}`}
              disabled={options.length === 1}
              onClick={() => setOptions(list => list.filter((_, j) => j !== i))}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        ))}
        <Button type="button" variant="outline" size="sm" className="self-start" onClick={() => setOptions(list => [...list, { option: '', whyNot: '' }])} disabled={options.length >= 10}>
          <Plus aria-hidden />
          Add an option
        </Button>
      </fieldset>
    </ActionSheet>
  );
}

// A new proposal of one decision type about a record, such as the PMO's priority proposal for an intake request on the
// table (document 23, P01 step 8). It is saved as the person's proposal; nobody is asked until it is sent. The engine
// checks that the person may propose this type in this scope. Once the proposal is saved the sheet closes and hands the
// new decision and the engine's answer to onProposed, which opens "Send it for approval now?" (wave 4b, S10-15).
export function NewProposalSheet(props: {
  decisionTypeKey: string;
  typeLabel: string;
  subjectKind: ProposalSubjectKind;
  subjectId: string;
  subjectLabel: string;
  triggerLabel: string;
  onProposed?: (decisionId: string, answer: EngineAnswer | null) => void;
}) {
  const outcomes = useOutcomes(props.decisionTypeKey);
  const [statement, setStatement] = useState('');
  const [context, setContext] = useState('');
  const [outcome, setOutcome] = useState('');
  const [reason, setReason] = useState('');
  const [option, setOption] = useState('');
  const [whyNot, setWhyNot] = useState('');
  const [standard, setStandard] = useState('');
  const [reviewOn, setReviewOn] = useState('');
  const [open, setOpen] = useState(false);
  const reset = () => {
    setStatement('');
    setContext('');
    setOutcome('');
    setReason('');
    setOption('');
    setWhyNot('');
    setStandard('');
    setReviewOn('');
  };
  return (
    <ActionSheet
      tool="save_decision_proposal" site="pages/work-approvals/decisionSheets#NewProposalSheet"
      title={`Propose a ${props.typeLabel.toLowerCase()} decision`}
      description={props.onProposed
        ? `About ${props.subjectLabel}. It is saved as your proposal, and the next sheet asks whether to send it for approval now; nobody is asked before that.`
        : `About ${props.subjectLabel}. It is saved as your proposal; nobody is asked until you submit it from its page.`}
      size="wide"
      open={open}
      trigger={(
        <Button size="sm" variant="outline">
          <Plus aria-hidden />
          {props.triggerLabel}
        </Button>
      )}
      input={() => {
        const s = statement.trim();
        const c = context.trim();
        const r = reason.trim();
        const o = option.trim();
        const w = whyNot.trim();
        if (!s) return { problem: 'Say what is decided.', field: 'statement' };
        if (!c) return { problem: 'Give the context.', field: 'context' };
        if (!outcome) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
        if (!r) return { problem: 'Say why.', field: 'reason' };
        if ((o && !w) || (!o && w)) return { problem: 'An option weighed needs the option and why it was not chosen.', field: 'optionsConsidered' };
        if (outcomes.needsOptions && !o) return { problem: 'This decision type needs at least one option weighed.', field: 'optionsConsidered' };
        const exception = exceptionInput(props.decisionTypeKey, standard, reviewOn);
        if ('problem' in exception) return exception;
        return {
          ...exception,
          decisionTypeKey: props.decisionTypeKey,
          subjectKind: props.subjectKind,
          subjectId: props.subjectId,
          statement: s,
          context: c,
          proposedOutcomeKey: outcome,
          reason: r,
          ...(o ? { optionsConsidered: [{ option: o, whyNot: w }] } : {}),
        };
      }}
      submitLabel="Propose"
      preview
      onOpenChange={next => { setOpen(next); if (!next) reset(); }}
      onDone={(_, envelope) => {
        const handed = proposalHandOff(envelope);
        if (!handed || !props.onProposed) return;
        // The page's follow-up takes over: the sheet closes, so the follow-up never opens over its answer.
        props.onProposed(handed.decisionId, handed.answer);
        setOpen(false);
        reset();
      }}
    >
      <Field label="Statement" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="Context" name="context" required>
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label="Proposed outcome" name="proposedOutcomeKey" required>
        {p => outcomes.options.length ? (
          <Select value={outcome} onValueChange={setOutcome}>
            <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full">
              <SelectValue placeholder="Choose an outcome" />
            </SelectTrigger>
            <SelectContent>
              {outcomes.options.map(x => <SelectItem key={x.key} value={x.key}>{x.label}</SelectItem>)}
            </SelectContent>
          </Select>
        ) : (
          <Muted>{outcomes.loading ? 'Reading the outcomes this decision type allows.' : 'The outcomes of this decision type could not be read. Close the sheet and try again.'}</Muted>
        )}
      </Field>
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      {props.decisionTypeKey === EXCEPTION && <ExceptionFields standard={standard} reviewOn={reviewOn} setStandard={setStandard} setReviewOn={setReviewOn} />}
      <div className="grid grid-cols-2 gap-2 narrow:grid-cols-1">
        <Field label="An option weighed" name="optionsConsidered" hint={outcomes.needsOptions ? 'Required for this decision type.' : 'Optional.'}>
          {p => <Input {...p} required={false} maxLength={500} value={option} onChange={e => setOption(e.target.value)} />}
        </Field>
        <Field label="Why not">
          {p => <Input {...p} maxLength={1000} value={whyNot} onChange={e => setWhyNot(e.target.value)} />}
        </Field>
      </div>
    </ActionSheet>
  );
}
