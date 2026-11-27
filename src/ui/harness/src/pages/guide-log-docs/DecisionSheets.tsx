// The decision log's own writes, each in the shared side sheet (components/actions.tsx), checked by the engine exactly
// as it checks an AI client's write:
//   ProposeDecisionSheet   propose a decision of any type that is about an application (save_decision_proposal); the
//                          Measures page opens it from a slide-back signal with slideBackSignalKey set (wave 4)
//   ProposeExceptionSheet  propose an exception about an application or one of its flagged deployments, with the
//                          standard it departs from and the date it is reviewed again (save_decision_proposal)
// A proposal is saved as the person's own; nobody is asked until it is sent. Wave 4b: once it is saved, the sheet
// closes and asks "Send it for approval now?" with the engine's answer on top (data/sheetParts.tsx SubmitFollowUp), as
// every proposal sheet does; Cancel leaves it proposed for its decision page.
//
// Design choices (D-41, 2026-11-24):
// - The log proposes only the decision types whose subject is an application (defaultSubjectKind in the configuration).
//   An intake, priority or change decision belongs to its own record, and its page proposes it with the record named.
//   An exception has its own sheet, because it can cover one flagged deployment and needs its standard and review date.
// - The outcome list and the "options weighed" rule are read only while a sheet is open, from the configuration in
//   force, so a closed sheet costs the page no reads.
import { FilePlus2, ShieldAlert } from 'lucide-react';
import { useEffect, useState, type ReactElement } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { DeploymentItem, Deployments, SaveDecisionProposalInput, SlideBackSignalKey } from '../../data/contracts';
import { fmtDate, todayInZone } from '../../data/format';
import { useOutcomes } from '../../data/labels';
import { activeFlags, flagText } from '../../data/meanings';
import { dataOf, useTool } from '../../data/toolClient';
import { proposalHandOff } from '../../data/writes';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';
import { SelectField, triggerProps, type Choice } from '../changes-records/parts';
import { EXCEPTION_TYPE } from './decisionLog';
import { AppSearchField } from './parts';

type App = { id: string; name: string };
type Problem = { problem: string; field: string };

const isDate = (v: string) => /^\d{4}-\d{2}-\d{2}$/.test(v);

// The outcomes a decision type allows. Rendered only inside an open sheet; it tells the sheet whether the type needs
// the options weighed.
function OutcomeField(props: { typeKey: string; value: string; onChange: (v: string) => void; onNeedsOptions: (needs: boolean) => void }) {
  const outcomes = useOutcomes(props.typeKey);
  const { onNeedsOptions } = props;
  useEffect(() => { onNeedsOptions(outcomes.needsOptions); }, [outcomes.needsOptions, onNeedsOptions]);
  return (
    <Field label="Proposed outcome" name="proposedOutcomeKey" required>
      {p => outcomes.options.length ? (
        <Select value={props.value} onValueChange={props.onChange}>
          <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue placeholder="Choose an outcome" /></SelectTrigger>
          <SelectContent>
            {outcomes.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
          </SelectContent>
        </Select>
      ) : (
        <Muted>{outcomes.loading ? 'Reading the outcomes this decision type allows.' : 'The outcomes of this decision type could not be read. Close the sheet and try again.'}</Muted>
      )}
    </Field>
  );
}

// The fields every proposal has, with their state and the checks before anything is sent.
function useProposalForm() {
  const [statement, setStatement] = useState('');
  const [context, setContext] = useState('');
  const [outcome, setOutcome] = useState('');
  const [reason, setReason] = useState('');
  const [option, setOption] = useState('');
  const [whyNot, setWhyNot] = useState('');
  const [needsOptions, setNeedsOptions] = useState(false);
  const reset = () => {
    setStatement('');
    setContext('');
    setOutcome('');
    setReason('');
    setOption('');
    setWhyNot('');
  };
  const check = (): Problem | null => {
    if (!statement.trim()) return { problem: 'Say what is decided, in one sentence.', field: 'statement' };
    if (!context.trim()) return { problem: 'Give the context.', field: 'context' };
    if (!outcome) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
    if (!reason.trim()) return { problem: 'Say why.', field: 'reason' };
    const o = option.trim();
    const w = whyNot.trim();
    if ((o && !w) || (!o && w)) return { problem: 'An option weighed needs the option and why it was not chosen.', field: 'optionsConsidered' };
    if (needsOptions && !o) return { problem: 'This decision type needs at least one option weighed.', field: 'optionsConsidered' };
    return null;
  };
  const body = () => ({
    statement: statement.trim(),
    context: context.trim(),
    proposedOutcomeKey: outcome,
    reason: reason.trim(),
    ...(option.trim() ? { optionsConsidered: [{ option: option.trim(), whyNot: whyNot.trim() }] } : {}),
  });
  return { statement, setStatement, context, setContext, outcome, setOutcome, reason, setReason, option, setOption, whyNot, setWhyNot, needsOptions, setNeedsOptions, reset, check, body };
}

type ProposalForm = ReturnType<typeof useProposalForm>;

function ProposalFields({ form, typeKey }: { form: ProposalForm; typeKey: string }) {
  return (
    <>
      <Field label="Statement" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} maxLength={500} value={form.statement} onChange={e => form.setStatement(e.target.value)} />}
      </Field>
      <Field label="Context" name="context" required hint="What led to it, in plain words.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={form.context} onChange={e => form.setContext(e.target.value)} />}
      </Field>
      {typeKey
        ? <OutcomeField typeKey={typeKey} value={form.outcome} onChange={form.setOutcome} onNeedsOptions={form.setNeedsOptions} />
        : <Muted>Choose the decision type first: it sets the outcomes you can propose.</Muted>}
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={form.reason} onChange={e => form.setReason(e.target.value)} />}
      </Field>
      <div className="grid grid-cols-2 gap-2 narrow:grid-cols-1">
        <Field label="An option weighed" name="optionsConsidered" hint={form.needsOptions ? 'Required for this decision type.' : 'Optional.'}>
          {p => <Input {...p} required={false} maxLength={500} value={form.option} onChange={e => form.setOption(e.target.value)} />}
        </Field>
        <Field label="Why not">
          {p => <Input {...p} maxLength={1000} value={form.whyNot} onChange={e => form.setWhyNot(e.target.value)} />}
        </Field>
      </div>
    </>
  );
}

const SAVED_NOTE = 'It is saved as your proposal, and the next sheet asks whether to send it for approval now. Nobody is asked before that.';

// ---------- propose a decision ----------

export function ProposeDecisionSheet(props: {
  // The decision types the log proposes: those about an application, exceptions left out.
  typeChoices: Choice[];
  initialType?: string;
  initialApp?: App | null;
  // Wave 4 (B24-22): the slide-back signal the decision acts on, set when the Measures page opens the sheet from a
  // signal ("Record a decision"). The decision keeps its own subject, an application.
  slideBackSignalKey?: SlideBackSignalKey;
  // The button that opens the sheet, when the page needs other words than "Propose a decision".
  trigger?: ReactElement;
}) {
  const form = useProposalForm();
  const startType = props.initialType && props.typeChoices.some(c => c.value === props.initialType) ? props.initialType : '';
  const [typeKey, setTypeKey] = useState(startType);
  const [app, setApp] = useState<App | null>(props.initialApp ?? null);
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  // The subject as it was proposed: the form resets once the follow-up opens.
  const [followSubject, setFollowSubject] = useState('');
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/guide-log-docs/DecisionSheets#ProposeDecisionSheet"
      title="Propose a decision"
      description={`About one application. ${SAVED_NOTE}`}
      size="wide"
      trigger={props.trigger ?? <Button variant="outline"><FilePlus2 aria-hidden />Propose a decision</Button>}
      open={open}
      onOpenChange={next => {
        setOpen(next);
        if (next) {
          setTypeKey(startType);
          setApp(props.initialApp ?? null);
        } else {
          form.reset();
        }
      }}
      input={() => {
        if (!typeKey) return { problem: 'Choose the decision type.', field: 'decisionTypeKey' };
        if (!app) return { problem: 'Choose the application it is about.', field: 'subjectId' };
        const problem = form.check();
        if (problem) return problem;
        const input: SaveDecisionProposalInput = {
          decisionTypeKey: typeKey,
          subjectKind: 'business-application',
          subjectId: app.id,
          ...form.body(),
          ...(props.slideBackSignalKey ? { slideBackSignalKey: props.slideBackSignalKey } : {}),
        };
        return input;
      }}
      submitLabel="Propose"
      preview
      onDone={(_, envelope) => {
        const handed = proposalHandOff(envelope);
        if (!handed) return;
        const type = props.typeChoices.find(c => c.value === typeKey)?.label ?? 'new';
        setFollowSubject(`The ${type.toLowerCase()} decision${app ? ` about ${app.name}` : ''}`);
        follow.start(handed);
        setOpen(false);
        form.reset();
      }}
    >
      <SelectField
        label="Decision type"
        name="decisionTypeKey"
        value={typeKey}
        onChange={v => { setTypeKey(v); form.setOutcome(''); }}
        choices={props.typeChoices}
        placeholder={props.typeChoices.length ? 'Choose a type' : 'The decision types could not be read'}
        hint="The engine checks that you hold a decision right for this type, as it does for an AI client."
        required
      />
      <AppSearchField label="Application" name="subjectId" value={app} onChange={setApp} required />
      <ProposalFields form={form} typeKey={typeKey} />
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={followSubject} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

// ---------- propose an exception ----------

const APP_ITSELF = 'application';

// The application's flagged deployments, for an exception that covers one. Rendered only inside an open sheet.
function CoversField(props: { appId: string; value: string; onChange: (v: string) => void }) {
  const state = useTool('get_deployments', { businessApplicationId: props.appId, flaggedOnly: true, latestOnly: false, top: 50 });
  const items: DeploymentItem[] = (dataOf(state) as Deployments | null)?.items ?? [];
  const label = (d: DeploymentItem) => {
    const flags = activeFlags(d.flags).map(f => flagText[f].toLowerCase()).join(', ');
    return `${d.artifactName} ${d.artifactVersion}${d.targetLabel ? ` to ${d.targetLabel}` : ''}, ${fmtDate(d.finishedOn)}${flags ? `: ${flags}` : ''}`;
  };
  const choices: Choice[] = [{ value: APP_ITSELF, label: 'The application as a whole' }, ...items.map(d => ({ value: d.deploymentRecordId.toLowerCase(), label: label(d) }))];
  const hint = state.status === 'loading'
    ? 'Reading the flagged deployments of this application.'
    : state.status === 'done' && !state.envelope.ok
      ? `The flagged deployments could not be read: ${state.envelope.error.message}`
      : items.length
        ? 'An exception about a flagged deployment shows on that deployment as its follow-up.'
        : 'This application has no flagged deployment, so the exception is about the application.';
  return <SelectField label="What it covers" name="subjectId" value={props.value} onChange={props.onChange} choices={choices} hint={hint} required />;
}

export function ProposeExceptionSheet(props: { initialApp?: App | null }) {
  const form = useProposalForm();
  const [app, setApp] = useState<App | null>(props.initialApp ?? null);
  const [covers, setCovers] = useState(APP_ITSELF);
  const [standard, setStandard] = useState('');
  const [reviewOn, setReviewOn] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  const [followSubject, setFollowSubject] = useState('');
  const clear = () => {
    form.reset();
    setCovers(APP_ITSELF);
    setStandard('');
    setReviewOn('');
  };
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/guide-log-docs/DecisionSheets#ProposeExceptionSheet"
      title="Propose an exception"
      description={`An exception sets a standard aside for one application, or for one flagged deployment, until a review date. ${SAVED_NOTE}`}
      size="wide"
      trigger={<Button variant="outline"><ShieldAlert aria-hidden />Propose an exception</Button>}
      open={open}
      onOpenChange={next => {
        setOpen(next);
        if (next) {
          setApp(props.initialApp ?? null);
        } else {
          clear();
        }
      }}
      input={() => {
        if (!app) return { problem: 'Choose the application the exception is about.', field: 'subjectId' };
        if (!standard.trim()) return { problem: 'Name the standard the exception departs from.', field: 'exception.standard' };
        if (!isDate(reviewOn)) return { problem: 'Give the date the exception is reviewed again.', field: 'exception.reviewOn' };
        const problem = form.check();
        if (problem) return problem;
        const deployment = covers !== APP_ITSELF;
        const input: SaveDecisionProposalInput = {
          decisionTypeKey: EXCEPTION_TYPE,
          subjectKind: deployment ? 'deployment-record' : 'business-application',
          subjectId: deployment ? covers : app.id,
          exception: { standard: standard.trim(), reviewOn },
          ...form.body(),
        };
        return input;
      }}
      submitLabel="Propose"
      preview
      onDone={(_, envelope) => {
        const handed = proposalHandOff(envelope);
        if (!handed) return;
        setFollowSubject(`The exception${app ? ` for ${app.name}` : ''}`);
        follow.start(handed);
        setOpen(false);
        clear();
      }}
    >
      <AppSearchField label="Application" name="subjectId" value={app} onChange={a => { setApp(a); setCovers(APP_ITSELF); }} required />
      {app && <CoversField appId={app.id} value={covers} onChange={setCovers} />}
      <Field label="Standard it departs from" name="exception.standard" required hint="The standard, guideline or rule the exception sets aside, in plain words.">
        {p => <Input {...p} maxLength={200} value={standard} onChange={e => setStandard(e.target.value)} />}
      </Field>
      <Field label="Review on" name="exception.reviewOn" required hint="The date the exception is looked at again: today or later.">
        {p => <Input {...p} type="date" min={todayInZone()} value={reviewOn} onChange={e => setReviewOn(e.target.value)} className="w-[200px]" />}
      </Field>
      <ProposalFields form={form} typeKey={EXCEPTION_TYPE} />
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={followSubject} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}
