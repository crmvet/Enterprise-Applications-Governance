// The one action pattern of the web app (D-36): each action lives on the record it changes, as a button that opens a
// side sheet. The sheet holds the form, calls the write tool as the signed-in person (POST /api/{tool}), and shows the
// engine's answer inline:
//   ok        what the engine did, who is asked now (or once submitted), warnings, the stamp on the write;
//   refused   the engine's sentence, the rule that refused it, the guideline with its text, the field at fault, and the
//             correlation ID. A refused write writes nothing.
// A write that succeeds reads the page's data again in place (useRefresh). Where the tool has validateOnly the sheet can
// check first: every rule runs and nothing is written. Approve and reject, and any act that cannot be undone, go through
// a confirm step.
//
// Pages build their form fields as children (controlled inputs: the page keeps their values) and hand the sheet an
// input() function that returns the tool input, or a FormProblem to show before anything is sent.
//
//   <ActionSheet
//     tool="withdraw_record"
//     title="Withdraw the request"
//     trigger={<Button variant="outline">Withdraw</Button>}
//     input={() => reason.trim() ? { subjectKind: 'intake-request', subjectId: id, reason: reason.trim() } : { problem: 'Say why.', field: 'reason' }}
//     submitLabel="Withdraw"
//     confirm={{ title: 'Withdraw this request?', confirmLabel: 'Withdraw', tone: 'destructive' }}
//   >
//     <Field label="Reason" name="reason" required>{p => <Textarea {...p} value={reason} onChange={e => setReason(e.target.value)} />}</Field>
//   </ActionSheet>
import { BookOpen, CircleAlert, CircleCheck, Info, Loader2, TriangleAlert } from 'lucide-react';
import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type FormEvent, type ReactElement, type ReactNode } from 'react';
import { guidelineHref } from '../app/links';
import { recordPath, normalizeBase } from '../app/routes';
import type { ApprovalAnswerResult, ApprovalMode, Envelope, GuidelineRef, Success, WriteToolData, WriteToolName } from '../data/contracts';
import { fmtDateTime } from '../data/format';
import { useLabels } from '../data/labels';
import { dataOf, useRefresh, useTool, useToolClient } from '../data/toolClient';
import { createRequestIdKeeper, plainEngineText, quotedKeys, readEngineAnswer, writeBody, type AskedNext, type EngineAnswer, type WriteInput } from '../data/writes';
import { cn } from '../lib/utils';
import { TextLink } from './blocks';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';
import { Button } from './ui/button';
import { Label } from './ui/label';
import { Sheet, SheetClose, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle, SheetTrigger } from './ui/sheet';
import { Textarea } from './ui/textarea';

export type { WriteInput } from '../data/writes';

// A problem the page finds in its own form before anything is sent. field names the tool input field, so the Field
// with that name is marked.
export interface FormProblem {
  problem: string;
  field?: string;
}

export interface ConfirmStep {
  title: string;
  body?: ReactNode;
  confirmLabel: string;
  tone?: 'default' | 'destructive';
}

// The fresh sign-in an approval answer may need (D-38: step-up is a setting, off in the demo). The web app's shell
// provides it from the sign-in; elsewhere it is null and the refusal only explains.
export const StepUpContext = createContext<((claimsChallenge: string | null) => void) | null>(null);

const isProblem = (value: unknown): value is FormProblem =>
  typeof value === 'object' && value !== null && typeof (value as FormProblem).problem === 'string';

// ---------- the write hook ----------

export interface WriteAction<N extends WriteToolName> {
  sending: boolean;
  envelope: Envelope<WriteToolData[N]> | null;
  answer: EngineAnswer | null;
  // Sends the write. preview runs it with validateOnly: every rule, nothing written, no retry ID.
  run(input: WriteInput<N>, options?: { preview?: boolean }): Promise<Envelope<WriteToolData[N]>>;
  reset(): void;
}

export interface WriteActionOptions<N extends WriteToolName> {
  // After a write that succeeded (not a preview).
  onDone?: (data: WriteToolData[N], envelope: Success<WriteToolData[N]>) => void;
  // False keeps the page data as it is after a success. Default true: the page reads its data again in place.
  refresh?: boolean;
}

// One write tool, called as the signed-in person. The same retry ID goes with a retry only when the last attempt's
// outcome is unknown, so a retry never writes twice (data/writes.ts).
export function useWriteAction<N extends WriteToolName>(tool: N, options: WriteActionOptions<N> = {}): WriteAction<N> {
  const client = useToolClient();
  const refresh = useRefresh();
  const keeper = useRef(createRequestIdKeeper());
  const inFlight = useRef<Promise<Envelope<WriteToolData[N]>> | null>(null);
  const latest = useRef(options);
  latest.current = options;
  const [state, setState] = useState<{ sending: boolean; envelope: Envelope<WriteToolData[N]> | null; answer: EngineAnswer | null }>({
    sending: false,
    envelope: null,
    answer: null,
  });

  const run = useCallback((input: WriteInput<N>, runOptions: { preview?: boolean } = {}) => {
    if (inFlight.current) return inFlight.current;
    const preview = runOptions.preview === true;
    // The retry key goes in the field the tool names: clientRequestId, or idempotencyKey for the deployment writes.
    const body = writeBody(tool, input as Record<string, unknown>, preview, b => keeper.current.idFor(b));
    setState(s => ({ ...s, sending: true }));
    const call: Promise<Envelope<WriteToolData[N]>> = client
      ? client.call(tool, body).catch(() => ({
        ok: false as const,
        error: { code: 'upstream_unavailable' as const, message: 'The product could not be reached. Try again: the same request is never written twice.' },
        correlationId: 'client-side',
      }))
      : Promise.resolve({ ok: false as const, error: { code: 'not_signed_in' as const, message: 'Sign in to do this.' }, correlationId: 'client-side' });
    const done = call.then(envelope => {
      inFlight.current = null;
      if (!preview) keeper.current.settle(envelope);
      setState({ sending: false, envelope, answer: readEngineAnswer(tool, envelope, preview) });
      if (envelope.ok && !preview) {
        if (latest.current.refresh !== false) refresh();
        latest.current.onDone?.(envelope.data, envelope);
      }
      return envelope;
    });
    inFlight.current = done;
    return done;
  }, [client, refresh, tool]);

  const reset = useCallback(() => setState({ sending: false, envelope: null, answer: null }), []);
  return { ...state, run, reset };
}

// ---------- the engine's answer ----------

const MODE_TEXT: Record<ApprovalMode, string> = {
  'everyone-must-approve': 'everyone must approve',
  'first-to-respond': 'the first answer counts',
  sequential: 'in order',
};

// Rendered only for a list that has items: it reads the role labels.
function AskedList({ title, items }: { title: string; items: AskedNext[] }) {
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{title}</span>
      <ul className="m-0 flex list-none flex-col gap-1 p-0">
        {items.map((a, i) => {
          const name = a.by === 'role'
            ? roles.label(a.key)
            : a.by === 'proposed-owners'
              ? 'Each proposed owner, by name'
              : a.by === 'person'
                // Asked by name: a proposed owner's consent, or the person who answers for the role. The role names the
                // part they answer for, not one they hold (QA 2026-11-24: a proposed deputy read as the business owner).
                // "About the role": a proposed deputy is asked about the role they would take, not as its holder (QA,
                // fix round 1: "asked by name for the technical owner role" for the proposed deputy).
                ? `${a.personName ?? 'A named person'}, asked by name about the ${ownerRoles.label(a.key).toLowerCase()} role`
                : `${ownerRoles.label(a.key)} of the application`;
          const notes = [
            a.isConsultation ? 'consulted, advice only' : null,
            a.approvalMode ? MODE_TEXT[a.approvalMode] : null,
            a.minimumCount !== null && a.minimumCount > 1 ? `${a.minimumCount} approvals` : null,
            a.expiresOn ? `expires ${fmtDateTime(a.expiresOn)}` : null,
          ].filter(Boolean);
          return (
            <li key={`${a.by}-${a.key}-${i}`} className="text-sm text-navy">
              <span className="font-medium">{name}</span>
              {notes.length > 0 && <span className="text-navy-3">{` (${notes.join(', ')})`}</span>}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

// The guideline a refusing rule, a requirement or a gate applies, with its label and rule text from the configuration in
// force, and a link to it on the Guidelines page (/app/guidelines?g=G17), where the organization's own copy opens. A
// read that carries the guideline as a chip (guidelineRef: number, title, link) passes it, and its title shows while the
// rule text loads. If the configuration cannot be read, the number (or the chip's title) alone.
export function GuidelineChip({ number, guideline }: { number: string; guideline?: GuidelineRef | null }) {
  const key = /^G[1-9][0-9]{0,2}$/.test(number) ? number.toLowerCase() : null;
  const state = useTool('get_configuration', key ? { entity: 'guideline', keys: [key] } : null);
  const row = dataOf(state)?.rows.find(r => r.key === key);
  const rule = typeof row?.fields.rule === 'string' ? row.fields.rule : null;
  const title = row?.label ?? (guideline ? `${guideline.number} ${guideline.title}` : `Guideline ${number}`);
  return (
    <div className="flex flex-col gap-1 rounded-md border border-info-line bg-info-soft px-3 py-2 text-navy">
      <span className="flex items-center gap-1.5 text-sm font-semibold">
        <BookOpen className="size-4 shrink-0 text-info" aria-hidden />
        {title}
      </span>
      {rule && <span className="text-xs leading-relaxed text-navy-2">{rule}</span>}
      {key && <TextLink href={guidelineHref(number)} className="self-start text-xs">Read the guideline</TextLink>}
    </div>
  );
}

// The configured labels of the keys an engine sentence names, read once from the configuration in force (any entity).
// While they load, or when the read fails, a key shows in plain words.
function useKeyLabelsIn(texts: string[]): (key: string) => string | null {
  const keys = [...new Set(texts.flatMap(quotedKeys))].slice(0, 25);
  const state = useTool('get_configuration', keys.length ? { keys, top: 100 } : null);
  const rows = dataOf(state)?.rows ?? [];
  return key => rows.find(r => r.key === key)?.label ?? null;
}

// The engine's answer to a write: shown inline in the sheet, or anywhere a page runs a write.
export function EngineAnswerView({ answer, className }: { answer: EngineAnswer; className?: string }) {
  const base = normalizeBase(import.meta.env.BASE_URL);
  const stepUp = useContext(StepUpContext);
  const texts = answer.kind === 'refused' ? [answer.message, answer.ruleKey ? `\`${answer.ruleKey}\`` : ''] : [answer.summary, ...answer.facts, ...answer.warnings.map(w => w.message)];
  const labelOf = useKeyLabelsIn(texts);
  const plain = (text: string) => plainEngineText(text, labelOf);
  if (answer.kind === 'refused') {
    const ruleLabel = answer.ruleKey ? labelOf(answer.ruleKey) : null;
    const pointsTo = recordPath(answer.recordUrl, base);
    const soft = answer.code === 'rate_limited' || answer.code === 'not_found' || answer.outcomeUnknown;
    return (
      <Alert variant={soft ? 'warning' : 'error'} className={className} role="alert">
        {soft ? <TriangleAlert aria-hidden /> : <CircleAlert aria-hidden />}
        <AlertTitle>{answer.preview ? `Check: ${answer.title.charAt(0).toLowerCase()}${answer.title.slice(1)}` : answer.title}</AlertTitle>
        <AlertDescription>
          <div className="flex flex-col gap-2">
            <p className="m-0">{plain(answer.message)}</p>
            {pointsTo && <p className="m-0"><TextLink href={pointsTo}>Open that record</TextLink></p>}
            {answer.ruleKey && (
              <p className="m-0 text-xs text-navy-2">
                {ruleLabel
                  ? <>The rule: <span className="font-semibold text-navy">{ruleLabel}</span></>
                  : <>The rule: <code className="rounded bg-card px-1 py-px font-mono text-[0.92em] text-navy">{answer.ruleKey}</code></>}
              </p>
            )}
            {answer.guideline && <GuidelineChip number={answer.guideline} />}
            {answer.field && <p className="m-0 text-xs text-navy-2">The field at fault: {answer.field}.</p>}
            {answer.retryAfterSeconds !== null && <p className="m-0 text-xs text-navy-2">Try again in {answer.retryAfterSeconds} seconds.</p>}
            {answer.code === 'step_up_required' && stepUp && (
              <div>
                <Button type="button" size="sm" onClick={() => stepUp(answer.claimsChallenge)}>Sign in again</Button>
                <p className="m-0 mt-1 text-xs text-navy-2">You come back to this page. Answer again once you are signed in.</p>
              </div>
            )}
            <p className="m-0 text-xs text-navy-2">
              {answer.outcomeUnknown
                ? 'The product may have saved this. Try again: the same request is never written twice.'
                : 'Nothing was written.'}
              {' '}Reference {answer.correlationId}.
            </p>
          </div>
        </AlertDescription>
      </Alert>
    );
  }
  const local = answer.preview ? null : recordPath(answer.recordUrl, base);
  return (
    <Alert variant={answer.preview ? 'info' : 'success'} className={className} role="status">
      {answer.preview ? <Info aria-hidden /> : <CircleCheck aria-hidden />}
      <AlertTitle>{answer.preview ? 'Checked: nothing was written yet' : 'Done'}</AlertTitle>
      <AlertDescription>
        <div className="flex flex-col gap-2.5">
          <p className="m-0">{plain(answer.summary)}</p>
          {answer.facts.length > 0 && (
            <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5">
              {answer.facts.map(f => <li key={f}>{plain(f)}</li>)}
            </ul>
          )}
          {answer.askedNow.length > 0 && <AskedList title={answer.preview ? 'Would be asked' : 'Asked now'} items={answer.askedNow} />}
          {answer.askedLater.length > 0 && <AskedList title="Will be asked once submitted" items={answer.askedLater} />}
          {answer.warnings.map((w, i) => (
            <p key={i} className="m-0 flex items-start gap-1.5 text-sm text-warning">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
              <span>{plain(w.message)}</span>
            </p>
          ))}
          {answer.stampedAs && <p className="m-0 text-xs text-navy-2">Stamped as {answer.stampedAs}.</p>}
          <p className="m-0 text-xs text-navy-2">
            {local && <><TextLink href={local}>Open the record</TextLink>. </>}
            Reference {answer.correlationId}.
          </p>
        </div>
      </AlertDescription>
    </Alert>
  );
}

// ---------- form fields ----------

const FieldContext = createContext<string | null>(null);

export interface FieldControl {
  id: string;
  'aria-describedby'?: string;
  'aria-invalid'?: true;
  required?: boolean;
}

// One labelled control. name is the tool input field it fills: when the page's own check or the engine names that
// field, the control is marked. children gets the props to spread on the control.
export function Field(props: {
  label: ReactNode;
  name?: string;
  hint?: ReactNode;
  required?: boolean;
  className?: string;
  children: (control: FieldControl) => ReactNode;
}) {
  const id = useId();
  const invalidField = useContext(FieldContext);
  const invalid = !!props.name && props.name === invalidField;
  const hintId = props.hint ? `${id}-hint` : undefined;
  const problemId = invalid ? `${id}-problem` : undefined;
  const describedBy = [hintId, problemId].filter(Boolean).join(' ') || undefined;
  return (
    <div className={cn('flex flex-col gap-1.5', props.className)}>
      <Label htmlFor={id}>
        {props.label}
        {props.required && <span className="text-xs font-normal text-navy-3">Required</span>}
      </Label>
      {props.children({ id, 'aria-describedby': describedBy, 'aria-invalid': invalid ? true : undefined, required: props.required })}
      {props.hint && <p id={hintId} className="m-0 text-xs leading-relaxed text-navy-3">{props.hint}</p>}
      {invalid && <p id={problemId} className="m-0 text-xs font-medium text-danger">Check this field.</p>}
    </div>
  );
}

// ---------- the side sheet ----------

export interface ActionSheetProps<N extends WriteToolName> {
  tool: N;
  // Where the sheet sits in the source: "<path under src, no extension>#<component>", such as
  // "pages/app360/sheets#DocumentCurrentSheet". The sheet carries it as data-action-site, so the end-to-end suite knows
  // which sheet a step submitted, and tests/e2e/tools/check-actions.mjs holds every sheet to a presenter step that
  // performs it (wave 4b QA). The check also proves the value matches the place.
  site: string;
  title: string;
  description?: ReactNode;
  // The control that opens the sheet, such as <Button>Propose</Button>. Leave it out to open the sheet with open.
  trigger?: ReactElement;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  // Builds the tool input from the form when the person submits, or returns a problem to show.
  input: () => WriteInput<N> | FormProblem;
  submitLabel: string;
  submitTone?: 'default' | 'destructive';
  // Adds a button that runs the write with validateOnly first: every rule runs, nothing is written. true labels it
  // "Check first"; a string labels it.
  preview?: boolean | string;
  // A confirm step before the write.
  confirm?: ConfirmStep;
  // After a write that succeeded. The page data reads again by itself.
  onDone?: (data: WriteToolData[N], envelope: Success<WriteToolData[N]>) => void;
  // After a success the answer stays in view with a Done button (default), or the sheet closes at once.
  afterSuccess?: 'stay' | 'close';
  // Wider for long forms such as the intake wizard or a new change.
  size?: 'default' | 'wide';
  children?: ReactNode;
}

type Step = 'form' | 'confirm' | 'done';

export function ActionSheet<N extends WriteToolName>(props: ActionSheetProps<N>) {
  const [ownOpen, setOwnOpen] = useState(false);
  const open = props.open ?? ownOpen;
  const [step, setStep] = useState<Step>('form');
  const [problem, setProblem] = useState<FormProblem | null>(null);
  const pendingInput = useRef<WriteInput<N> | null>(null);
  const action = useWriteAction(props.tool, { onDone: props.onDone });
  const answerRef = useRef<HTMLDivElement>(null);

  const openRef = useRef(open);
  openRef.current = open;

  const setOpen = (next: boolean) => {
    if (props.open === undefined) setOwnOpen(next);
    props.onOpenChange?.(next);
  };

  // A closed sheet starts again from the form next time.
  const { reset } = action;
  useEffect(() => {
    if (open) return;
    setStep('form');
    setProblem(null);
    pendingInput.current = null;
    reset();
  }, [open, reset]);

  // A new answer is read out: move the focus to it, so a keyboard or screen reader user lands on what happened.
  useEffect(() => {
    if (action.answer) answerRef.current?.focus({ preventScroll: false });
  }, [action.answer]);

  const send = async (input: WriteInput<N>, preview: boolean) => {
    const envelope = await action.run(input, { preview });
    // Closed while the write was on its way: the page data still reads again; the sheet starts fresh next time.
    if (!openRef.current) return;
    if (envelope.ok && !preview) {
      if (props.afterSuccess === 'close') setOpen(false);
      else setStep('done');
    } else {
      setStep('form');
    }
  };

  const collect = (): WriteInput<N> | null => {
    const built = props.input();
    if (isProblem(built)) {
      setProblem(built);
      return null;
    }
    setProblem(null);
    return built;
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (action.sending) return;
    if (step === 'confirm') {
      if (pendingInput.current) void send(pendingInput.current, false);
      return;
    }
    if (step === 'done') {
      setOpen(false);
      return;
    }
    const input = collect();
    if (!input) return;
    if (props.confirm) {
      pendingInput.current = input;
      action.reset();
      setStep('confirm');
      return;
    }
    void send(input, false);
  };

  const onPreview = () => {
    if (action.sending) return;
    const input = collect();
    if (input) void send(input, true);
  };

  const invalidField = problem?.field ?? (action.answer?.kind === 'refused' ? action.answer.field : null);
  const previewLabel = typeof props.preview === 'string' ? props.preview : 'Check first';
  const destructive = (step === 'confirm' ? props.confirm?.tone : props.submitTone) === 'destructive';

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      {props.trigger && <SheetTrigger asChild>{props.trigger}</SheetTrigger>}
      <SheetContent
        side="right"
        data-action-site={props.site}
        data-action-tool={props.tool}
        className={cn('w-full gap-0 p-0 text-navy', props.size === 'wide' ? 'sm:max-w-2xl' : 'sm:max-w-lg')}
      >
        <SheetHeader className="border-b border-line px-5 pt-5 pb-4 pr-12">
          <SheetTitle className="text-lg leading-6 text-navy">{props.title}</SheetTitle>
          {props.description
            ? <SheetDescription className="text-sm leading-relaxed text-navy-2">{props.description}</SheetDescription>
            : <SheetDescription className="sr-only">The engine checks this change before anything is written.</SheetDescription>}
        </SheetHeader>
        <form className="flex min-h-0 flex-1 flex-col" onSubmit={onSubmit} noValidate>
          <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-5 py-4">
            {problem && (
              <Alert variant="warning" role="alert">
                <TriangleAlert aria-hidden />
                <AlertTitle>Check the form</AlertTitle>
                <AlertDescription><p className="m-0">{problem.problem}</p></AlertDescription>
              </Alert>
            )}
            {action.answer && (
              <div ref={answerRef} tabIndex={-1} className="outline-none" aria-live="polite">
                <EngineAnswerView answer={action.answer} />
              </div>
            )}
            {step === 'confirm' && props.confirm && (
              <div className="flex flex-col gap-2 rounded-lg border border-line-strong bg-card p-4" role="group" aria-label={props.confirm.title}>
                <span className="text-base font-semibold text-navy">{props.confirm.title}</span>
                {props.confirm.body && <div className="text-sm leading-relaxed text-navy-2">{props.confirm.body}</div>}
              </div>
            )}
            <FieldContext.Provider value={invalidField}>
              <fieldset className={cn('m-0 min-w-0 flex-col gap-4 border-0 p-0', step === 'form' ? 'flex' : 'hidden')} disabled={action.sending}>
                {props.children}
              </fieldset>
            </FieldContext.Provider>
          </div>
          <SheetFooter className="flex-row flex-wrap justify-end gap-2 border-t border-line px-5 py-3">
            {step === 'form' && (
              <>
                <SheetClose asChild>
                  <Button type="button" variant="ghost">Cancel</Button>
                </SheetClose>
                {props.preview && (
                  <Button type="button" variant="outline" onClick={onPreview} disabled={action.sending}>{previewLabel}</Button>
                )}
                <Button type="submit" variant={destructive ? 'destructive' : 'default'} disabled={action.sending}>
                  {action.sending && <Loader2 className="animate-spin" aria-hidden />}
                  {props.submitLabel}
                </Button>
              </>
            )}
            {step === 'confirm' && props.confirm && (
              <>
                <Button type="button" variant="ghost" onClick={() => { setStep('form'); action.reset(); }} disabled={action.sending}>Back</Button>
                <Button type="submit" variant={destructive ? 'destructive' : 'default'} disabled={action.sending}>
                  {action.sending && <Loader2 className="animate-spin" aria-hidden />}
                  {props.confirm.confirmLabel}
                </Button>
              </>
            )}
            {step === 'done' && <Button type="submit">Done</Button>}
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

// ---------- approve and reject (D-38) ----------

// Approve and Reject for one approval request that asks the signed-in person. Each opens a sheet with the typed reason
// and a confirm step, and calls answer_approval: the token proves who answered, and the engine checks that the person
// is asked, that no separation rule keeps the answer from counting, and that the request is still open. Show these only
// on a request whose nextTool is answer_approval (list_approvals, get_my_queue) or that get_decision shows as pending
// for the person; the engine refuses anyone else anyway.
export function ApprovalAnswerButtons(props: {
  approvalRecordId: string;
  // What is being answered, for the sheet and the confirm step, such as "DEC-0112: accept the residual risk".
  subject: string;
  // True when the approval rule asks for a typed reason to approve too. A reject always asks for one.
  reasonToApprove?: boolean;
  isConsultation?: boolean;
  onAnswered?: (result: ApprovalAnswerResult) => void;
  size?: 'sm' | 'default';
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <AnswerSheet {...props} answer="approve" />
      <AnswerSheet {...props} answer="reject" />
    </div>
  );
}

function AnswerSheet(props: {
  approvalRecordId: string;
  subject: string;
  reasonToApprove?: boolean;
  isConsultation?: boolean;
  onAnswered?: (result: ApprovalAnswerResult) => void;
  size?: 'sm' | 'default';
  answer: 'approve' | 'reject';
}) {
  const [reason, setReason] = useState('');
  const approve = props.answer === 'approve';
  const reasonRequired = !approve || !!props.reasonToApprove;
  const verb = approve ? 'Approve' : 'Reject';
  return (
    <ActionSheet
      tool="answer_approval" site="components/actions#AnswerSheet"
      title={`${verb}: ${props.subject}`}
      description={props.isConsultation
        ? 'You are consulted. Your answer is recorded and shown as advice, and it never counts toward the approval.'
        : 'Your answer is recorded under your name. The engine counts it under the approval rule.'}
      trigger={(
        <Button size={props.size ?? 'sm'} variant={approve ? 'default' : 'outline'}>
          {verb}
        </Button>
      )}
      input={() => {
        const text = reason.trim();
        if (reasonRequired && !text) return { problem: approve ? 'The approval rule asks for a reason to approve.' : 'Say why you reject it.', field: 'reason' };
        return { approvalRecordId: props.approvalRecordId, answer: props.answer, ...(text ? { reason: text } : {}) };
      }}
      submitLabel={verb}
      submitTone={approve ? 'default' : 'destructive'}
      preview="Check my answer"
      confirm={{
        title: approve ? 'Approve this decision?' : 'Reject this decision?',
        body: (
          <>
            <p className="m-0">
              Your answer is final: it cannot be changed afterwards. The engine then counts the answers and moves the
              decision when the rule is met.
            </p>
            {reason.trim() && (
              <p className="m-0 mt-2 border-l-2 border-line-strong pl-3 whitespace-pre-wrap text-navy [overflow-wrap:anywhere]">{reason.trim()}</p>
            )}
          </>
        ),
        confirmLabel: verb,
        tone: approve ? 'default' : 'destructive',
      }}
      onDone={data => props.onAnswered?.(data)}
      onOpenChange={next => { if (!next) setReason(''); }}
    >
      <Field
        label="Reason"
        name="reason"
        required={reasonRequired}
        hint="Stored on the approval record as you type it. Others asked about this decision can read it."
      >
        {p => <Textarea {...p} rows={4} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}
