// The review writes the engine supports today, each in the shared side sheet (components/actions.tsx):
//   ProposeOutcomeSheet   save_decision_proposal about the review, with the decision type the review type produces
//                         (C24). The outcome takes effect only when people accept the decision. Wave 4: the submit
//                         follow-up opens right after it is saved, as the data sheets do; the page's action stays while
//                         the follow-up is open.
//   AddEvidenceSheet      save_link: an evidence link on the review, with its evidence level.
//   CompleteReviewSheet   complete_review (person only): marks the review done once its outcome decision is accepted,
//                         with the person as reviewer and the next due date from the cadence (document 23, B23-10).
import { Plus, Trash2 } from 'lucide-react';
import { useState, type ReactElement } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { LabelledKey } from '../../data/contracts';
import { useLabels, useListValues, useOutcomes } from '../../data/labels';
import { readEngineAnswer } from '../../data/writes';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';
import { buildCompletion, buildEvidence, buildOutcome, isProblem, withArticle } from './model';
import { triggerProps, type Proposal } from './OwnerSheets';

const NONE = 'none';

export function ProposeOutcomeSheet(props: {
  review: { reviewId: string; typeLabel: string; applicationName: string | null };
  decisionType: LabelledKey;
  trigger: ReactElement;
  onProposed?: (proposal: Proposal) => void;
  onOpenChange?: (open: boolean) => void;
}) {
  const allowed = useOutcomes(props.decisionType.key);
  const outcomes = allowed.options;
  const needsOptions = allowed.needsOptions;
  const confidence = useListValues('confidence').options;
  const what = `${props.review.typeLabel}${props.review.applicationName ? ` of ${props.review.applicationName}` : ''}`;
  // The same words inside a sentence: the review type in lower case, the application name as it is.
  const whatInText = `${props.review.typeLabel.toLowerCase()}${props.review.applicationName ? ` of ${props.review.applicationName}` : ''}`;

  const [outcome, setOutcome] = useState('');
  const [statement, setStatement] = useState('');
  const [context, setContext] = useState('');
  const [reason, setReason] = useState('');
  const [confidenceKey, setConfidenceKey] = useState('');
  const [options, setOptions] = useState<{ option: string; whyNot: string }[]>([{ option: '', whyNot: '' }]);
  const [evidenceUrl, setEvidenceUrl] = useState('');
  const [evidenceLabel, setEvidenceLabel] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  const reset = () => {
    setOutcome('');
    setStatement('');
    setContext('');
    setReason('');
    setConfidenceKey('');
    setOptions([{ option: '', whyNot: '' }]);
    setEvidenceUrl('');
    setEvidenceLabel('');
  };
  const outcomeLabel = outcomes.find(o => o.key === outcome)?.label;

  const subject = `Outcome of the ${whatInText}`;
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/owners-reviews/ReviewSheets#ProposeOutcomeSheet"
      title={`Propose the outcome: ${what}`}
      description={`${withArticle(`${props.decisionType.label.toLowerCase()} decision`, true)} about this review. It takes effect only when people accept it. Submit it after saving to ask them.`}
      trigger={props.trigger}
      size="wide"
      open={open}
      onOpenChange={next => { setOpen(next); if (!next) reset(); props.onOpenChange?.(next); }}
      input={() => {
        const built = buildOutcome({
          reviewId: props.review.reviewId,
          decisionTypeKey: props.decisionType.key,
          outcomeKey: outcome,
          statement,
          context,
          reason,
          confidenceKey,
          needsOptions,
          options,
          evidenceUrl,
          evidenceLabel,
        });
        return isProblem(built) ? built : built.input;
      }}
      submitLabel="Propose"
      preview
      onDone={(data, envelope) => {
        if (!data.recordId) return;
        props.onProposed?.({ decisionId: data.recordId, subject });
        // The page keeps the action (and so this follow-up) while it is open: onOpenChange is told only when it closes.
        follow.start({ decisionId: data.recordId, answer: readEngineAnswer('save_decision_proposal', envelope) });
        setOpen(false);
        reset();
      }}
    >
      <Field label="Outcome" name="proposedOutcomeKey" required hint={outcomes.length ? undefined : allowed.loading ? 'Reading the outcomes this decision type allows.' : 'This decision type has no outcomes in the configuration in force.'}>
        {p => (
          <Select
            value={outcome}
            onValueChange={v => {
              setOutcome(v);
              const label = outcomes.find(o => o.key === v)?.label;
              if (label && !statement.trim()) setStatement(`${props.decisionType.label} after the ${whatInText}: ${label.toLowerCase()}.`);
            }}
          >
            <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue placeholder="Choose the outcome" /></SelectTrigger>
            <SelectContent>
              {outcomes.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </Field>
      <Field label="Decision" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} value={statement} maxLength={500} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="What the review looked at" name="context" required hint="The situation, what was checked and what was found.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label={outcomeLabel ? `Why ${outcomeLabel.toLowerCase()}` : 'Why this outcome'} name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      {confidence.length > 0 && (
        <Field label="Confidence" name="confidenceKey">
          {p => (
            <Select value={confidenceKey || NONE} onValueChange={v => setConfidenceKey(v === NONE ? '' : v)}>
              <SelectTrigger {...triggerProps(p)} className="w-[220px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE}>Not stated</SelectItem>
                {confidence.map(c => <SelectItem key={c.key} value={c.key}>{c.label}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
        </Field>
      )}
      <fieldset className="m-0 flex min-w-0 flex-col gap-3 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">
          Options weighed {needsOptions ? <span className="text-xs font-normal text-navy-3">Required for this decision type</span> : <span className="text-xs font-normal text-navy-3">Optional</span>}
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
      <fieldset className="m-0 flex min-w-0 flex-col gap-3 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">Evidence <span className="text-xs font-normal text-navy-3">Optional, a link to proof held elsewhere</span></legend>
        <Field label="Link" name="evidence" hint="A full https address.">
          {p => <Input {...p} type="url" inputMode="url" value={evidenceUrl} maxLength={2000} onChange={e => setEvidenceUrl(e.target.value)} />}
        </Field>
        <Field label="Label">
          {p => <Input {...p} value={evidenceLabel} maxLength={200} onChange={e => setEvidenceLabel(e.target.value)} />}
        </Field>
      </fieldset>
    </ActionSheet>
    {follow.data && (
      <SubmitFollowUp
        decisionId={follow.data.decisionId}
        previous={follow.data.answer}
        subject={subject}
        open={follow.open}
        onOpenChange={next => { follow.onOpenChange(next); if (!next) props.onOpenChange?.(false); }}
      />
    )}
    </>
  );
}

export function AddEvidenceSheet(props: { reviewId: string; subject: string; trigger: ReactElement }) {
  const linkKinds = useLabels('link-kind');
  const kinds = linkKinds.keys().filter(k => linkKinds.field(k, 'linkCategory') === 'evidence');
  const levels = useListValues('evidence-level').options;
  const [kind, setKind] = useState('');
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const [level, setLevel] = useState('');
  // The shipped evidence link kind when the configuration cannot be read; the engine checks the key either way.
  const chosenKind = kind || kinds[0] || 'evidence-url';
  const reset = () => {
    setKind('');
    setUrl('');
    setLabel('');
    setLevel('');
  };

  return (
    <ActionSheet
      tool="save_link" site="pages/owners-reviews/ReviewSheets#AddEvidenceSheet"
      title={`Add evidence: ${props.subject}`}
      description="A link to proof held elsewhere, such as a findings page or an access review result. The product stores the link, never the file."
      trigger={props.trigger}
      onOpenChange={next => { if (!next) reset(); }}
      input={() => {
        const built = buildEvidence({ reviewId: props.reviewId, linkKindKey: chosenKind, url, label, evidenceLevelKey: level });
        return isProblem(built) ? built : built.input;
      }}
      submitLabel="Add the link"
      preview
    >
      {kinds.length > 1 && (
        <Field label="Kind of link" name="linkKindKey" required>
          {p => (
            <Select value={chosenKind} onValueChange={setKind}>
              <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {kinds.map(k => <SelectItem key={k} value={k}>{linkKinds.label(k)}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
        </Field>
      )}
      <Field label="Link" name="url" required hint="A full https address. The rules check it against the link kind's pattern.">
        {p => <Input {...p} type="url" inputMode="url" value={url} maxLength={2000} onChange={e => setUrl(e.target.value)} />}
      </Field>
      <Field label="Label" name="label" required>
        {p => <Input {...p} value={label} maxLength={200} onChange={e => setLabel(e.target.value)} />}
      </Field>
      {levels.length > 0 && (
        <Field label="Evidence level" name="evidenceLevelKey" hint="How far the evidence was checked.">
          {p => (
            <Select value={level || NONE} onValueChange={v => setLevel(v === NONE ? '' : v)}>
              <SelectTrigger {...triggerProps(p)} className="w-[240px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE}>Not stated</SelectItem>
                {levels.map(l => <SelectItem key={l.key} value={l.key}>{l.label}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
        </Field>
      )}
    </ActionSheet>
  );
}

export function CompleteReviewSheet(props: {
  reviewId: string;
  subject: string;
  outcome: string;
  nextDue: string | null;
  today: string;
  trigger: ReactElement;
  onOpenChange?: (open: boolean) => void;
}) {
  const [doneOn, setDoneOn] = useState('');
  const [evidenceUrl, setEvidenceUrl] = useState('');
  const [evidenceLabel, setEvidenceLabel] = useState('');
  const reset = () => {
    setDoneOn('');
    setEvidenceUrl('');
    setEvidenceLabel('');
  };

  return (
    <ActionSheet
      tool="complete_review" site="pages/owners-reviews/ReviewSheets#CompleteReviewSheet"
      title={`Complete the review: ${props.subject}`}
      description={(
        <>
          You record that the review is done, as its reviewer, with its accepted outcome: {props.outcome}
          {props.nextDue ? ` The next one is due ${props.nextDue} when it is done today.` : ' This review type has no cadence, so no next review is set.'}
        </>
      )}
      trigger={props.trigger}
      onOpenChange={next => { if (!next) reset(); props.onOpenChange?.(next); }}
      input={() => {
        const built = buildCompletion({ reviewId: props.reviewId, doneOn, today: props.today, evidenceUrl, evidenceLabel });
        return isProblem(built) ? built : built.input;
      }}
      submitLabel="Complete the review"
      preview
    >
      <Field label="Done on" name="doneOn" hint="Leave empty for today, your own calendar day. Never later than today.">
        {p => <Input {...p} type="date" value={doneOn} max={props.today} onChange={e => setDoneOn(e.target.value)} />}
      </Field>
      <fieldset className="m-0 flex min-w-0 flex-col gap-3 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">Evidence <span className="text-xs font-normal text-navy-3">Optional, a link to what the review looked at</span></legend>
        <Field label="Link" name="evidence" hint="A full https address.">
          {p => <Input {...p} type="url" inputMode="url" value={evidenceUrl} maxLength={2000} onChange={e => setEvidenceUrl(e.target.value)} />}
        </Field>
        <Field label="Label">
          {p => <Input {...p} value={evidenceLabel} maxLength={200} onChange={e => setEvidenceLabel(e.target.value)} />}
        </Field>
      </fieldset>
    </ActionSheet>
  );
}
