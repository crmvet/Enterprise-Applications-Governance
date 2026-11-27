// The intake pages' write sheets that more than one page offers: submit, withdraw, the tier answers on a record, and a
// delivery or evidence link. Each one is the shared action sheet (components/actions.tsx): the engine checks the write
// and its answer shows in the sheet.
import { useMemo, useState } from 'react';
import type { LinkCategory, TierAnswersResult } from '../../data/contracts';
import { externalHref } from '../../data/format';
import { dataOf, useTool } from '../../data/toolClient';
import { ActionSheet, Field } from '../../components/actions';
import { Empty, PanelFailure } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Skeleton } from '../../components/ui/skeleton';
import { Textarea } from '../../components/ui/textarea';
import { cn } from '../../lib/utils';
import { TierHintView, TierQuestionsForm, useHorizonOptions, useSizeBandOptions, useTierHint } from './shared';

const NO_VALUE = 'no-value';

// The PMO's demand fields on a request on the table (score_intake_request): score, size band, horizon and expected
// benefit, recorded as the PMO reports them. The request keeps its status. Priority is a decision: proposed separately,
// and its accountable holder answers on the decision page.
export function ScoreSheet(props: {
  intakeRequestId: string;
  title: string;
  score: number | null;
  sizeBandKey: string | null;
  horizonKey: string | null;
  size?: 'sm' | 'default';
  // The sheet closed (after its answer, or without a save).
  onClosed?: () => void;
}) {
  const sizes = useSizeBandOptions();
  const horizons = useHorizonOptions();
  const [score, setScore] = useState(props.score === null ? '' : String(props.score));
  const [size, setSize] = useState(props.sizeBandKey ?? '');
  const [horizon, setHorizon] = useState(props.horizonKey ?? '');
  const [benefit, setBenefit] = useState('');
  const reset = () => {
    setScore(props.score === null ? '' : String(props.score));
    setSize(props.sizeBandKey ?? '');
    setHorizon(props.horizonKey ?? '');
    setBenefit('');
  };
  return (
    <ActionSheet
      tool="score_intake_request" site="pages/intake/sheets#ScoreSheet"
      title="Score the request"
      description={`For ${props.title}. The score, size band and horizon order the demand list. Priority is a separate decision that its accountable holder answers.`}
      trigger={<Button size={props.size ?? 'sm'} variant="outline" aria-label={`Score ${props.title}`}>Score</Button>}
      onOpenChange={open => { if (!open) { reset(); props.onClosed?.(); } }}
      input={() => {
        const text = score.trim();
        const value = text === '' ? null : Number(text);
        if (value !== null && (!Number.isFinite(value) || value < 0 || value > 100)) return { problem: 'Give a score from 0 to 100.', field: 'score' };
        const changedScore = value !== props.score;
        const input = {
          intakeRequestId: props.intakeRequestId,
          ...(changedScore ? (value === null ? { clearScore: true } : { score: value }) : {}),
          ...(size && size !== props.sizeBandKey ? { sizeBandKey: size } : {}),
          ...(horizon && horizon !== props.horizonKey ? { horizonKey: horizon } : {}),
          ...(benefit.trim() ? { expectedBenefit: benefit.trim() } : {}),
        };
        return Object.keys(input).length > 1 ? input : { problem: 'Change the score, the size band, the horizon or the benefit to record it.', field: 'score' };
      }}
      submitLabel="Record"
      preview
    >
      <Field label="Score" name="score" hint="0 to 100. Higher comes first in the demand list. Leave it empty to clear a score.">
        {p => <Input {...p} required={false} type="number" inputMode="decimal" min={0} max={100} step="any" value={score} onChange={e => setScore(e.target.value)} className="w-[140px]" />}
      </Field>
      <Field label="Size band" name="sizeBandKey" hint="A rough size of the work.">
        {p => (
          <Select value={size || NO_VALUE} onValueChange={v => setSize(v === NO_VALUE ? '' : v)} disabled={sizes === null}>
            <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full"><SelectValue placeholder={sizes === null ? 'Reading the values' : 'Choose'} /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NO_VALUE}>Not set</SelectItem>
              {(sizes ?? []).map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </Field>
      <Field label="Horizon" name="horizonKey" hint="When the business needs it.">
        {p => (
          <Select value={horizon || NO_VALUE} onValueChange={v => setHorizon(v === NO_VALUE ? '' : v)} disabled={horizons === null}>
            <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full"><SelectValue placeholder={horizons === null ? 'Reading the values' : 'Choose'} /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NO_VALUE}>Not set</SelectItem>
              {(horizons ?? []).map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </Field>
      <Field label="Expected benefit" name="expectedBenefit" hint="Optional. Replaces the benefit the requester gave, when you fill it.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={benefit} onChange={e => setBenefit(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// aiDraft: an AI client drafted the request for the person. Submitting it is the person accepting the draft as theirs
// (D-28), so the sheet says so.
export function SubmitSheet(props: { intakeRequestId: string | null; unsaved?: boolean; disabled?: boolean; size?: 'sm' | 'default'; aiDraft?: boolean }) {
  const [note, setNote] = useState('');
  return (
    <ActionSheet
      tool="submit_record" site="pages/intake/sheets#SubmitSheet"
      title={props.aiDraft ? 'Accept the AI draft and submit it' : 'Submit the request'}
      description={props.aiDraft
        ? 'An AI client drafted this request for you. Submitting it accepts the draft as yours: the engine opens the intake decision and asks the people the rules name. You can withdraw the request while it is open.'
        : 'Submitting opens the intake decision and asks the people the rules name. You can withdraw the request while it is open.'}
      trigger={<Button size={props.size} disabled={props.disabled}>{props.aiDraft ? 'Accept and submit' : 'Submit'}</Button>}
      input={() => {
        if (!props.intakeRequestId) return { problem: 'Save the draft first.' };
        if (props.unsaved) return { problem: 'Save the draft first: some changes are not saved yet, and the engine submits what is saved.' };
        const text = note.trim();
        return { subjectKind: 'intake-request' as const, subjectId: props.intakeRequestId, ...(text ? { note: text } : {}) };
      }}
      submitLabel={props.aiDraft ? 'Accept and submit' : 'Submit'}
      preview
      onOpenChange={open => { if (!open) setNote(''); }}
    >
      <Field label="A note for the people who decide" name="note" hint="Optional. Up to 1,000 characters.">
        {p => <Textarea {...p} rows={4} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

export function WithdrawSheet(props: { intakeRequestId: string; title: string; draft?: boolean; disabled?: boolean; size?: 'sm' | 'default' }) {
  const [reason, setReason] = useState('');
  const what = props.draft ? 'draft' : 'request';
  return (
    <ActionSheet
      tool="withdraw_record" site="pages/intake/sheets#WithdrawSheet"
      title={`Withdraw the ${what}`}
      description={props.draft
        ? 'A withdrawn request stays on record and cannot be submitted again. Start a new request if the need comes back.'
        : 'A withdrawn request stays on record. Its open intake decision is withdrawn too, and the approval requests still open are canceled.'}
      trigger={<Button size={props.size} variant="outline" className="text-danger hover:text-danger" disabled={props.disabled}>Withdraw</Button>}
      input={() => (reason.trim() ? { subjectKind: 'intake-request' as const, subjectId: props.intakeRequestId, reason: reason.trim() } : { problem: 'Say why the request is withdrawn.', field: 'reason' })}
      submitLabel="Withdraw"
      submitTone="destructive"
      confirm={{
        title: `Withdraw ${props.title ? `"${props.title}"` : `this ${what}`}?`,
        body: 'The request is kept, marked withdrawn, and can no longer be submitted. This cannot be undone.',
        confirmLabel: 'Withdraw',
        tone: 'destructive',
      }}
      onOpenChange={open => { if (!open) setReason(''); }}
    >
      <Field label="Reason" name="reason" required hint="Kept with the request, and on its intake decision.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// The tier questions of a request that is no longer a draft (or any request, from its record): the answers are chosen in
// the sheet. The questions are read when the sheet opens.
export function RecordTierSheet(props: { intakeRequestId: string; kind: string; kindLabel: string; onDone?: (result: TierAnswersResult) => void }) {
  const [answers, setAnswers] = useState<Record<string, string>>({});
  return (
    <ActionSheet
      tool="answer_tier_questions" site="pages/intake/sheets#RecordTierSheet"
      title="Answer the tier questions"
      description="The engine works out the tier from the answers and keeps it with the request as a suggestion. Only an accepted tier decision sets the tier."
      trigger={<Button size="sm" variant="outline">Answer the tier questions</Button>}
      input={() => {
        const list = Object.entries(answers).map(([questionKey, answerKey]) => ({ questionKey, answerKey }));
        return list.length ? { intakeRequestId: props.intakeRequestId, answers: list } : { problem: 'Answer at least one question.', field: 'answers' };
      }}
      submitLabel="Save answers"
      preview="Check with the engine"
      size="wide"
      onDone={data => props.onDone?.(data)}
      onOpenChange={open => { if (!open) setAnswers({}); }}
    >
      <TierSheetBody kind={props.kind} kindLabel={props.kindLabel} answers={answers} setAnswers={setAnswers} intakeRequestId={props.intakeRequestId} />
    </ActionSheet>
  );
}

function TierSheetBody(props: { kind: string; kindLabel: string; answers: Record<string, string>; setAnswers: (a: Record<string, string>) => void; intakeRequestId: string }) {
  // The request named, so the answer its linked application's declared data implies comes back (impliedAnswer).
  const state = useTool('get_tier_questions', { applicationKindKey: props.kind, intakeRequestId: props.intakeRequestId });
  const questions = useMemo(() => dataOf(state)?.questions ?? [], [state]);
  const hint = useTierHint(questions, props.answers);
  if (state.status === 'loading') return <div role="status" aria-label="Loading the tier questions" className="flex flex-col gap-2"><Skeleton className="h-24 w-full" /><Skeleton className="h-24 w-full" /></div>;
  if (!state.envelope.ok) return <PanelFailure failure={state.envelope} />;
  if (!questions.length) return <Empty>No tier question applies to {props.kindLabel}.</Empty>;
  return (
    <div className="flex flex-col gap-4">
      <TierHintView hint={hint} questions={questions} />
      <Field label={`The questions for ${props.kindLabel}`} name="answers">
        {p => (
          <div id={p.id} aria-describedby={p['aria-describedby']}>
            <TierQuestionsForm questions={questions} answers={props.answers} onChange={props.setAnswers} idPrefix="record-tier" implied={dataOf(state)?.impliedAnswer ?? null} />
          </div>
        )}
      </Field>
    </div>
  );
}

// A delivery link (a work item, an issue, a plan) or an evidence link on the request. The link kinds come from the
// configuration in force; the engine checks the URL against the kind's pattern and the forbidden patterns.
export function LinkSheet({ intakeRequestId }: { intakeRequestId: string }) {
  const [category, setCategory] = useState<LinkCategory>('delivery');
  const [kindKey, setKindKey] = useState('');
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const [externalId, setExternalId] = useState('');
  const reset = () => {
    setCategory('delivery');
    setKindKey('');
    setUrl('');
    setLabel('');
    setExternalId('');
  };
  return (
    <ActionSheet
      tool="save_link" site="pages/intake/sheets#LinkSheet"
      title="Link a work item or evidence"
      description="The product stores the ID, the address and a label, never a copy of the item."
      trigger={<Button size="sm" variant="outline">Add a link</Button>}
      input={() => {
        if (!kindKey) return { problem: 'Choose the kind of link.', field: 'linkKindKey' };
        if (!externalHref(url.trim())) return { problem: 'Give the full https address of the item.', field: 'url' };
        if (!label.trim()) return { problem: 'Give the link a short label.', field: 'label' };
        return {
          linkCategory: category,
          subjectKind: 'intake-request' as const,
          subjectId: intakeRequestId,
          linkKindKey: kindKey,
          url: url.trim(),
          label: label.trim(),
          ...(category === 'delivery' && externalId.trim() ? { externalId: externalId.trim() } : {}),
        };
      }}
      submitLabel="Save the link"
      preview
      onOpenChange={open => { if (!open) reset(); }}
    >
      <fieldset className="m-0 flex flex-col gap-1.5 border-0 p-0">
        <legend className="mb-1.5 p-0 text-sm font-medium text-navy">What it is</legend>
        <div className="flex flex-wrap gap-2">
          {([['delivery', 'A work item or plan'], ['evidence', 'Evidence']] as const).map(([value, text]) => (
            <label key={value} className={cn(
              'flex cursor-pointer items-center gap-2 rounded-md border px-3 py-1.5 text-sm text-navy has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-ring',
              category === value ? 'border-primary bg-info-soft' : 'border-line bg-card hover:border-line-strong',
            )}>
              <input type="radio" name="link-category" value={value} checked={category === value} onChange={() => { setCategory(value); setKindKey(''); }} className="size-4 accent-primary" />
              {text}
            </label>
          ))}
        </div>
      </fieldset>
      <LinkKindField category={category} value={kindKey} onChange={setKindKey} />
      <Field label="Address" name="url" required hint="The full https address that opens the item.">
        {p => <Input {...p} type="url" inputMode="url" placeholder="https://" value={url} maxLength={2000} onChange={e => setUrl(e.target.value)} className="bg-card" />}
      </Field>
      <Field label="Label" name="label" required hint="A short name people will read, such as the work item title.">
        {p => <Input {...p} value={label} maxLength={200} onChange={e => setLabel(e.target.value)} className="bg-card" />}
      </Field>
      {category === 'delivery' && (
        <Field label="ID in its own tool" name="externalId" hint="Optional, such as the work item number.">
          {p => <Input {...p} value={externalId} maxLength={400} onChange={e => setExternalId(e.target.value)} className="bg-card" />}
        </Field>
      )}
    </ActionSheet>
  );
}

function LinkKindField({ category, value, onChange }: { category: LinkCategory; value: string; onChange: (key: string) => void }) {
  const state = useTool('get_configuration', { entity: 'link-kind' });
  const kinds = (dataOf(state)?.rows ?? []).filter(r => r.isActive && r.fields.linkCategory === category);
  return (
    <Field label="Kind of link" name="linkKindKey" required hint={state.status === 'done' && !state.envelope.ok ? 'The link kinds could not be read. Refresh to try again.' : undefined}>
      {p => (
        <Select value={value || undefined} onValueChange={onChange} disabled={!kinds.length}>
          <SelectTrigger {...p} className="w-full bg-card">
            <SelectValue placeholder={state.status === 'loading' ? 'Reading the link kinds' : kinds.length ? 'Choose a kind' : 'No kind of this category'} />
          </SelectTrigger>
          <SelectContent>
            {kinds.map(k => <SelectItem key={k.key} value={k.key}>{k.label}</SelectItem>)}
          </SelectContent>
        </Select>
      )}
    </Field>
  );
}
