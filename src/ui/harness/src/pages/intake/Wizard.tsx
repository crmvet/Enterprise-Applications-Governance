// The intake wizard: a new request (/app/intake/new) and an existing draft, such as one an AI client started
// (/app/records/intake-request/<id>/edit). Five steps on one page: the need, reuse, the tier questions, what the tier
// needs, and review and submit. The form lives on the page; every write goes through the shared side sheet, which shows
// what is sent and the engine's answer: save_intake_draft, answer_tier_questions, submit_record, withdraw_record.
// Reads: get_intake_request, check_duplicates (suggestions while typing), get_tier_questions, get_requirements, whoami.
import { ArrowLeft, ArrowRight, CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { goTo, hrefTo } from '../../app/links';
import type { IntakeDetail, Requirements, TierAnswersResult, TierQuestion, TierQuestions } from '../../data/contracts';
import { fmtDateTime } from '../../data/format';
import { useLabels } from '../../data/labels';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { ActionSheet, Field } from '../../components/actions';
import { PersonSearchField } from '../../components/people';
import { Empty, Muted, PageHeader, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { AiMark } from '../../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Skeleton } from '../../components/ui/skeleton';
import { Textarea } from '../../components/ui/textarea';
import { cn } from '../../lib/utils';
import {
  EMPTY_FIELDS,
  LIMITS,
  aiStampOf,
  answerList,
  changedFields,
  checkFields,
  composeNeed,
  draftInput,
  fieldsOf,
  isRequesterOf,
  savedValueStays,
  type FieldProblem,
  type WizardFields,
} from './logic';
import {
  DuplicateList,
  Facts,
  GuidelineList,
  Prose,
  RequirementsView,
  RolesUnreadNote,
  StateBlock,
  TierHintView,
  TierQuestionsForm,
  mayCall,
  meOf,
  useDebounced,
  useHorizonOptions,
  useTierHint,
  useWhoami,
  type Option,
  type WhoState,
} from './shared';
import { SubmitSheet, WithdrawSheet } from './sheets';

const STEPS = [
  { key: 'need', label: 'The need', short: 'Need' },
  { key: 'reuse', label: 'Reuse', short: 'Reuse' },
  { key: 'tier', label: 'Tier questions', short: 'Tier' },
  { key: 'needs', label: 'What the tier needs', short: 'Needs' },
  { key: 'submit', label: 'Review and submit', short: 'Submit' },
] as const;

const NONE = '__none';

// A new draft hands its form to its edit page, which takes over at the new address.
let handoff: { id: string; fields: WizardFields; answers: Record<string, string>; step: number } | null = null;

// ---------- the two pages ----------

export function IntakeNewPage() {
  const who = useWhoami();
  if (who.status === 'loading') return <WizardSkeleton title="New request" />;
  if (!mayCall(who, 'save_intake_draft')) {
    return (
      <>
        <PageHeader eyebrow="Intake" title="New request" />
        {who.me ? (
          <Alert variant="warning">
            <TriangleAlert aria-hidden />
            <AlertTitle>Your roles do not start intake requests</AlertTitle>
            <AlertDescription>
              <p className="m-0">
                The roles that start a request are requester, maker, application owner, PMO, AI lead and platform admin.
                Ask your platform admin if you need one. <TextLink href={hrefTo({ page: 'intake' })}>Back to intake</TextLink>.
              </p>
            </AlertDescription>
          </Alert>
        ) : <RolesUnreadNote state={who} />}
      </>
    );
  }
  return <Wizard who={who} intakeRequestId={null} detail={null} />;
}

export function IntakeEditPage({ intakeRequestId }: { intakeRequestId: string }) {
  const who = useWhoami();
  const state = useTool('get_intake_request', { intakeRequestId });
  const data = dataOf(state);
  // The page decides once, on the first answer, whether the request can be edited. A submit or withdraw made here then
  // keeps the wizard in view with its answer, read-only.
  const firstMeaning = useRef<string | null>(null);
  if (data && firstMeaning.current === null) firstMeaning.current = data.intake.status.meaning;
  const record = hrefTo({ page: 'intake-request', id: intakeRequestId });

  if (state.status === 'loading' || who.status === 'loading') return <WizardSkeleton title="Edit request" />;
  if (!state.envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Intake" title="The request cannot be edited" meta={<span className="font-mono [overflow-wrap:anywhere]">{intakeRequestId}</span>} />
        <PanelFailure failure={state.envelope} />
      </>
    );
  }
  const intake = state.envelope.data.intake;
  const requester = isRequesterOf(intake, who.me?.objectId);
  const notice = firstMeaning.current !== 'draft'
    ? { title: `This request is ${intake.status.label.toLowerCase()}`, body: 'Only a draft is edited. A submitted request is withdrawn and started again if it must change.' }
    : !who.me
      ? null
      : !requester || !mayCall(who, 'save_intake_draft')
        ? { title: 'Only the requester edits this draft', body: `${intake.requester?.displayLabel ?? 'The requester'} edits and submits it. You can read it on its record page.` }
        : null;
  if (notice || !who.me) {
    return (
      <>
        <PageHeader eyebrow={intake.recordNumber ?? 'Intake'} title={intake.title} meta={<StatusBadge status={intake.status} />} />
        {notice ? (
          <Alert variant="info">
            <Info aria-hidden />
            <AlertTitle>{notice.title}</AlertTitle>
            <AlertDescription><p className="m-0">{notice.body} <TextLink href={record}>Open the record</TextLink>.</p></AlertDescription>
          </Alert>
        ) : <RolesUnreadNote state={who} />}
      </>
    );
  }
  return <Wizard who={who} intakeRequestId={intakeRequestId} detail={state.envelope.data} />;
}

function WizardSkeleton({ title }: { title: string }) {
  return (
    <>
      <PageHeader eyebrow="Intake" title={title} />
      <div role="status" aria-label="Loading the request" className="flex flex-col gap-3">
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    </>
  );
}

// ---------- the wizard ----------

function Wizard({ who, intakeRequestId, detail }: { who: WhoState; intakeRequestId: string | null; detail: IntakeDetail | null }) {
  const me = meOf(who);
  const saved = useMemo(() => (detail ? fieldsOf(detail.intake) : null), [detail]);
  const [initial] = useState(() => {
    const taken = handoff && handoff.id === intakeRequestId ? handoff : null;
    if (taken) handoff = null;
    return taken;
  });
  const [fields, setFields] = useState<WizardFields>(() => initial?.fields ?? saved ?? EMPTY_FIELDS);
  const [answers, setAnswers] = useState<Record<string, string>>(() => initial?.answers ?? {});
  const [step, setStep] = useState<number>(() => initial?.step ?? 0);
  const [visited, setVisited] = useState<number[]>(() => (initial ? [0, 1] : [0]));
  const [problem, setProblem] = useState<FieldProblem | null>(null);
  const [tierResult, setTierResult] = useState<TierAnswersResult | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const shownStep = useRef(step);

  const meaning = detail?.intake.status.meaning ?? 'draft';
  const editable = meaning === 'draft';
  const changed = changedFields(fields, saved);
  const dirty = changed.length > 0;
  const kinds = useLabels('application-kind');
  const horizons = useHorizonOptions();
  const portfolioRead = useTool('list_portfolios', { top: 100 });
  const portfolios = dataOf(portfolioRead)?.items.filter(p => p.isActive).map(p => ({ key: p.portfolioId.toLowerCase(), label: p.name })) ?? null;
  const set = <K extends keyof WizardFields>(key: K, value: WizardFields[K]) => {
    setFields(f => ({ ...f, [key]: value }));
    if (problem && (problem.field === key || (key === 'purpose' && problem.field === 'need'))) setProblem(null);
  };

  // The tier questions follow the proposed kind.
  const kind = fields.proposedKindKey;
  // With a saved request, the read names it, so the answer its linked application's declared data implies comes back as
  // a suggestion (impliedAnswer, B24-22).
  const savedId = detail?.intake.intakeRequestId ?? null;
  const questionsState = useTool('get_tier_questions', kind ? { applicationKindKey: kind, ...(savedId ? { intakeRequestId: savedId } : {}) } : null);
  const questions = useMemo<TierQuestion[]>(() => dataOf(questionsState)?.questions ?? [], [questionsState]);
  const hint = useTierHint(questions, answers);
  const latestAssessment = detail?.tierAssessments[0] ?? null;
  const engineTier = tierResult?.computedTier ?? latestAssessment?.computedTier ?? null;
  const tierKey = engineTier?.key ?? hint.tier?.key ?? null;

  // Duplicate suggestions while the person types.
  const dupText = useDebounced(`${fields.title.trim()} ${fields.purpose.trim()}`.trim().slice(0, 2000), 600);
  const duplicates = useTool('check_duplicates', dupText.length >= 3 ? { text: dupText, ...(kind ? { proposedKindKey: kind } : {}) } : null);
  const matches = dataOf(duplicates)?.matches ?? [];

  const requirements = useTool('get_requirements', step >= 3 && tierKey ? { scope: { tierKey, ...(kind ? { applicationKindKey: kind } : {}) } } : null);

  // A new step moves the focus to its heading, for keyboard and screen reader users.
  useEffect(() => {
    if (shownStep.current === step) return;
    shownStep.current = step;
    headingRef.current?.focus({ preventScroll: false });
  }, [step]);

  const goToStep = (next: number) => {
    if (step === 0 && next > 0) {
      const p = checkFields(fields);
      if (p) {
        setProblem(p);
        return;
      }
    }
    setProblem(null);
    const target = Math.max(0, Math.min(STEPS.length - 1, next));
    setStep(target);
    setVisited(v => (v.includes(target) ? v : [...v, target]));
  };

  // After a new draft is saved, the edit page at its own address takes over, on the tier questions.
  const createdId = useRef<string | null>(null);
  const onDraftSheet = (open: boolean) => {
    if (open || !createdId.current) return;
    handoff = { id: createdId.current, fields, answers, step: Math.max(step, 2) };
    goTo({ page: 'intake-edit', id: createdId.current }, true);
  };

  const stays = savedValueStays(fields, saved);
  const title = intakeRequestId ? (detail?.intake.title || 'Edit request') : 'New request';
  const invalid = (name: string) => (problem?.field === name ? true : undefined);

  const saveSheet = (
    <ActionSheet
      tool="save_intake_draft" site="pages/intake/Wizard#Wizard"
      title={intakeRequestId ? 'Save the draft' : 'Save as a draft'}
      description={intakeRequestId
        ? 'The engine checks the draft before it is saved. A draft goes nowhere until you submit it.'
        : 'The engine checks the draft before it is saved. A draft goes nowhere until you submit it. Once saved, the wizard goes on to the tier questions.'}
      trigger={<Button variant={dirty ? 'default' : 'outline'} disabled={!editable}>Save draft</Button>}
      input={() => checkFields(fields) ?? draftInput(fields, intakeRequestId)}
      submitLabel="Save draft"
      preview
      onDone={data => { if (!intakeRequestId && data.recordId) createdId.current = data.recordId; }}
      onOpenChange={onDraftSheet}
      size="wide"
    >
      <DraftSummary fields={fields} kinds={kinds.label} horizons={horizons} portfolios={portfolios} stays={stays} />
    </ActionSheet>
  );

  return (
    <>
      <PageHeader
        eyebrow={detail?.intake.recordNumber ? `Intake ${detail.intake.recordNumber}` : 'Intake'}
        title={title}
        subtitle={intakeRequestId
          ? 'Finish the request, answer the tier questions, then submit it. The engine checks every save.'
          : 'Say what the business needs. The engine checks every save, and nothing goes anywhere until you submit.'}
        meta={detail ? (
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={detail.intake.status} />
            {detail.intake.requester && <span>Requested by {detail.intake.requester.displayLabel}</span>}
          </span>
        ) : undefined}
        actions={intakeRequestId ? <TextLink href={hrefTo({ page: 'intake-request', id: intakeRequestId })}>Open the record</TextLink> : undefined}
      />

      {detail?.intake.isAiCreated && editable && (
        <Alert variant="info" className="mb-4">
          <Info aria-hidden />
          <AlertTitle className="flex flex-wrap items-center gap-2">
            <AiMark isAiCreated performedBy={aiStampOf(detail.intake.performedBy)} />
            {aiStampOf(detail.intake.performedBy)?.stampedOn && <span className="text-xs font-normal text-navy-2">{fmtDateTime(detail.intake.performedBy!.stampedOn)}</span>}
          </AlertTitle>
          <AlertDescription>
            <div className="flex flex-col gap-2">
              <p className="m-0">
                An AI client drafted this request for {detail.intake.requester?.displayLabel ?? 'you'}. Read it, change what is
                wrong, answer the tier questions, then submit it yourself. Nothing is submitted until you do.
              </p>
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={() => goToStep(4)}>Review and submit</Button>
                <Button size="sm" variant="outline" onClick={() => goToStep(0)}>Edit it first</Button>
              </div>
            </div>
          </AlertDescription>
        </Alert>
      )}

      {detail && !editable && (
        <Alert variant="success" className="mb-4">
          <CircleCheck aria-hidden />
          <AlertTitle>The request is {detail.intake.status.label.toLowerCase()}</AlertTitle>
          <AlertDescription>
            <p className="m-0">
              It can no longer be edited here. <TextLink href={hrefTo({ page: 'intake-request', id: detail.intake.intakeRequestId })}>Open the record</TextLink> to follow the intake decision.
            </p>
          </AlertDescription>
        </Alert>
      )}

      <StepNav step={step} onStep={goToStep} done={[!checkFields(fields), visited.includes(1), !!engineTier, visited.includes(3), !editable]} />

      <section className="mt-4 flex min-w-0 flex-col gap-4 rounded-xl border bg-card px-5 pt-[18px] pb-4 shadow-xs narrow:rounded-lg narrow:px-3.5 narrow:pt-3.5" aria-labelledby="wizard-step-title">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="wizard-step-title" ref={headingRef} tabIndex={-1} className="m-0 text-lg leading-7 font-semibold text-navy outline-none">
            <span className="mr-2 text-navy-3">Step {step + 1} of {STEPS.length}<span className="sr-only">:</span></span>{' '}
            {STEPS[step].label}
          </h2>
          {editable && (
            dirty
              ? <ToneBadge tone="warning">{intakeRequestId ? 'Changes not saved' : 'Not saved yet'}</ToneBadge>
              : <ToneBadge tone="success">Saved</ToneBadge>
          )}
        </div>

        {problem && (
          <Alert variant="warning" role="alert">
            <TriangleAlert aria-hidden />
            <AlertTitle>Check the form</AlertTitle>
            <AlertDescription><p className="m-0">{problem.problem}</p></AlertDescription>
          </Alert>
        )}

        <fieldset className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0" disabled={!editable}>
          {step === 0 && (
            <NeedStep fields={fields} set={set} invalid={invalid} kinds={kinds.keys().map(k => ({ key: k, label: kinds.label(k) }))} horizons={horizons} portfolios={portfolios} stays={stays} matches={matches.length} onReuse={() => goToStep(1)} />
          )}
          {step === 1 && (
            <div className="flex flex-col gap-3">
              <p className="m-0 text-sm text-navy-2">
                Registered applications that look like this request, from its title and need. A suggestion only: reuse is
                cheaper than a new build, and the platform admin weighs it at triage.
              </p>
              {dupText.length < 3 ? (
                <Empty>Write a title and the need in step 1 to see suggestions.</Empty>
              ) : (
                <StateBlock state={duplicates} label="the suggestions">
                  {data => (
                    <DuplicateList
                      matches={data.matches}
                      newTab
                      usedId={fields.businessApplicationId || undefined}
                      onUse={editable ? m => { set('businessApplicationId', m.businessApplicationId); set('applicationName', m.name); } : undefined}
                    />
                  )}
                </StateBlock>
              )}
              {fields.businessApplicationId && (
                <div className="flex flex-wrap items-center gap-2 rounded-md border border-line bg-background px-3 py-2 text-sm text-navy">
                  <span>This request is about <strong>{fields.applicationName || 'an existing application'}</strong>.</span>
                  {saved?.businessApplicationId === fields.businessApplicationId
                    ? <Muted>Saved. A saved application cannot be taken off the request yet.</Muted>
                    : <Button variant="ghost" size="sm" onClick={() => { set('businessApplicationId', saved?.businessApplicationId ?? ''); set('applicationName', saved?.applicationName ?? ''); }}>Undo</Button>}
                </div>
              )}
            </div>
          )}
          {step === 2 && (
            <TierStep
              kind={kind}
              kindLabel={kinds.label(kind)}
              questionsState={questionsState}
              questions={questions}
              answers={answers}
              setAnswers={setAnswers}
              hint={hint}
              saved={engineTier ? { label: engineTier.label, by: tierResult ? 'just now' : latestAssessment?.assessedBy?.displayLabel ?? null, ai: !tierResult && !!latestAssessment?.isAiCreated } : null}
              triggers={tierResult?.triggers ?? []}
            />
          )}
          {step === 3 && (
            <NeedsStep tierKey={tierKey} source={engineTier ? 'engine' : hint.tier ? 'hint' : null} tierLabel={engineTier?.label ?? hint.tier?.label ?? ''} kindLabel={kind ? kinds.label(kind) : null} state={tierKey ? requirements : null} />
          )}
          {step === 4 && (
            <ReviewStep
              fields={fields}
              kindLabel={kind ? kinds.label(kind) : null}
              horizons={horizons}
              engineTier={engineTier?.label ?? null}
              hintTier={hint.tier?.label ?? null}
              answersUnsaved={hint.answered > 0 && !tierResult}
              requirements={dataOf(requirements)}
              dirty={dirty}
              saved={!!intakeRequestId}
            />
          )}
        </fieldset>

        <div className="flex flex-wrap items-center gap-2 border-t border-line pt-3">
          <Button variant="ghost" onClick={() => goToStep(step - 1)} disabled={step === 0}>
            <ArrowLeft aria-hidden />Back
          </Button>
          <div className="ml-auto flex flex-wrap items-center justify-end gap-2">
            {/* The sheets stay mounted after a submit or withdraw made here, so their answer stays in view; their
                buttons are then disabled. */}
            {intakeRequestId && mayCall(who, 'withdraw_record') && (
              <WithdrawSheet intakeRequestId={intakeRequestId} title={detail?.intake.title ?? ''} draft disabled={!editable} />
            )}
            {saveSheet}
            {step === 2 && questions.length > 0 && (
              <TierAnswersSheet
                intakeRequestId={intakeRequestId}
                kindChanged={!!saved && saved.proposedKindKey !== kind}
                kind={kind}
                questions={questions}
                answers={answers}
                allowed={mayCall(who, 'answer_tier_questions')}
                onDone={setTierResult}
                disabled={!editable}
              />
            )}
            {step === 4 && mayCall(who, 'submit_record') && (
              <SubmitSheet intakeRequestId={intakeRequestId} unsaved={dirty} disabled={!editable} aiDraft={!!detail?.intake.isAiCreated} />
            )}
            {step < STEPS.length - 1 && (
              <Button variant={step === 4 ? 'default' : 'outline'} onClick={() => goToStep(step + 1)}>
                Next<ArrowRight aria-hidden />
              </Button>
            )}
          </div>
        </div>
        {me === null && <RolesUnreadNote state={who} />}
      </section>
    </>
  );
}

// ---------- the step list ----------

function StepNav({ step, onStep, done }: { step: number; onStep: (step: number) => void; done: boolean[] }) {
  return (
    <nav aria-label="Steps of the request">
      <ol className="m-0 grid list-none grid-cols-5 gap-2 p-0 narrow:gap-1">
        {STEPS.map((s, i) => {
          const current = i === step;
          return (
            <li key={s.key} className="min-w-0">
              <button
                type="button"
                onClick={() => onStep(i)}
                aria-current={current ? 'step' : undefined}
                className={cn(
                  'flex h-full w-full min-w-0 cursor-pointer items-start gap-2 rounded-lg border px-3 py-2 text-left transition-colors narrow:flex-col narrow:items-center narrow:gap-0.5 narrow:px-1 narrow:py-1.5 narrow:text-center',
                  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
                  current ? 'border-primary bg-info-soft' : 'border-line bg-card hover:border-line-strong',
                )}
              >
                <span
                  className={cn(
                    'grid size-6 shrink-0 place-items-center rounded-full text-xs font-semibold',
                    current ? 'bg-primary text-primary-foreground' : done[i] ? 'bg-success-soft text-success' : 'bg-muted text-navy-3',
                  )}
                  aria-hidden
                >
                  {done[i] && !current ? <CircleCheck className="size-3.5" /> : i + 1}
                </span>
                <span className="flex min-w-0 flex-col">
                  <span className="text-sm font-semibold text-navy narrow:hidden">{s.label}</span>
                  <span className="hidden text-xs font-semibold text-navy narrow:block">{s.short}</span>
                  <span className="text-xs text-navy-3 narrow:sr-only">{current ? 'Current step' : done[i] ? 'Done' : 'To do'}</span>
                </span>
              </button>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

// ---------- step 1: the need ----------

function NeedStep(props: {
  fields: WizardFields;
  set: <K extends keyof WizardFields>(key: K, value: WizardFields[K]) => void;
  invalid: (name: string) => true | undefined;
  kinds: Option[];
  horizons: Option[] | null;
  portfolios: Option[] | null;
  stays: (keyof WizardFields)[];
  matches: number;
  onReuse: () => void;
}) {
  const { fields, set, invalid } = props;
  const stayNote = (key: keyof WizardFields) => (props.stays.includes(key) ? ' The saved value stays: a saved field cannot be emptied yet.' : '');
  const needLength = composeNeed(fields.purpose, fields.owner).length;
  return (
    <div className="grid grid-cols-2 gap-x-6 gap-y-4 medium:grid-cols-1">
      <Field label="Title" name="title" required hint="A short name for the request, such as Crew overtime tracker." className="col-span-full">
        {p => <Input {...p} aria-invalid={invalid('title')} value={fields.title} maxLength={LIMITS.title} onChange={e => props.set('title', e.target.value)} className="bg-card" />}
      </Field>
      <Field
        label="The need"
        name="need"
        required
        hint={<>What the business needs and why, in plain words. The data it touches and who will use it come in the tier questions. {needLength > LIMITS.need - 200 && <strong>{needLength} of {LIMITS.need} characters.</strong>}</>}
        className="col-span-full"
      >
        {p => <Textarea {...p} aria-invalid={invalid('need')} rows={5} value={fields.purpose} maxLength={LIMITS.need} onChange={e => set('purpose', e.target.value)} />}
      </Field>
      {props.matches > 0 && (
        <p className="col-span-full m-0 flex items-start gap-1.5 rounded-md border border-warning-line bg-warning-soft px-3 py-2 text-sm text-navy">
          <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
          <span>
            {props.matches === 1 ? '1 registered application looks' : `${props.matches} registered applications look`} similar.{' '}
            <button type="button" className="cursor-pointer border-0 bg-transparent p-0 font-medium text-primary underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring" onClick={props.onReuse}>
              See them in the reuse step
            </button>.
          </span>
        </p>
      )}
      <PersonSearchField
        label="Business owner"
        name="businessOwnerObjectId"
        value={fields.businessOwner}
        onChange={person => set('businessOwner', person)}
        activeOnly
        hint={[
          'The person who will own the application for the business. The Accept gate needs one, and they become its business owner when the request is accepted.',
          fields.owner ? `The need names ${fields.owner} as the business owner in words; find them here to name them.` : '',
          stayNote('businessOwner').trim(),
        ].filter(Boolean).join(' ')}
      />
      <Field label="Proposed kind" name="proposedKindKey" hint={`The questions in step 3 depend on it.${stayNote('proposedKindKey')}`}>
        {p => (
          <Select value={fields.proposedKindKey || NONE} onValueChange={v => set('proposedKindKey', v === NONE ? '' : v)}>
            <SelectTrigger {...p} className="w-full bg-card"><SelectValue placeholder="Choose a kind" /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>Not chosen yet</SelectItem>
              {props.kinds.map(k => <SelectItem key={k.key} value={k.key}>{k.label}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </Field>
      <fieldset className="m-0 flex min-w-0 flex-col gap-1.5 border-0 p-0">
        <legend className="mb-1.5 p-0 text-sm font-medium text-navy">Will it use AI?</legend>
        <div className="flex flex-wrap gap-2">
          {([['yes', 'Yes'], ['no', 'No'], ['', 'Not sure yet']] as const).map(([value, label]) => (
            <label key={label} className={cn(
              'flex cursor-pointer items-center gap-2 rounded-md border px-3 py-1.5 text-sm text-navy has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-ring',
              fields.usesAi === value ? 'border-primary bg-info-soft' : 'border-line bg-card hover:border-line-strong',
            )}>
              <input type="radio" name="uses-ai" value={value} checked={fields.usesAi === value} onChange={() => set('usesAi', value)} className="size-4 accent-primary" />
              {label}
            </label>
          ))}
        </div>
        <p className="m-0 text-xs text-navy-3">AI use brings its own questions and reviews.{stayNote('usesAi')}</p>
      </fieldset>
      <Field label="Horizon" name="horizonKey" hint={`When the business needs it.${stayNote('horizonKey')}`}>
        {p => (
          <Select value={fields.horizonKey || NONE} onValueChange={v => set('horizonKey', v === NONE ? '' : v)} disabled={props.horizons === null}>
            <SelectTrigger {...p} className="w-full bg-card"><SelectValue placeholder={props.horizons === null ? 'Reading the values' : 'Choose'} /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>Not chosen yet</SelectItem>
              {(props.horizons ?? []).map(h => <SelectItem key={h.key} value={h.key}>{h.label}</SelectItem>)}
              {fields.horizonKey && props.horizons && !props.horizons.some(h => h.key === fields.horizonKey) && <SelectItem value={fields.horizonKey}>{fields.horizonKey}</SelectItem>}
            </SelectContent>
          </Select>
        )}
      </Field>
      <Field label="Portfolio" name="portfolioId" hint="The portfolio the new application belongs to, when you know it. The PMO can place it later.">
        {p => (
          <Select value={fields.portfolioId || NONE} onValueChange={v => set('portfolioId', v === NONE ? '' : v)} disabled={props.portfolios === null}>
            <SelectTrigger {...p} className="w-full bg-card"><SelectValue placeholder={props.portfolios === null ? 'Reading the portfolios' : 'Choose'} /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE}>Not chosen yet</SelectItem>
              {(props.portfolios ?? []).map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </Field>
      <Field label="Expected benefit" name="expectedBenefit" hint={`What it saves or earns, as you see it now.${stayNote('expectedBenefit')}`} className="col-span-full">
        {p => <Textarea {...p} aria-invalid={invalid('expectedBenefit')} rows={3} value={fields.expectedBenefit} maxLength={LIMITS.expectedBenefit} onChange={e => set('expectedBenefit', e.target.value)} />}
      </Field>
    </div>
  );
}

// ---------- step 3: the tier questions ----------

function TierStep(props: {
  kind: string;
  kindLabel: string;
  questionsState: ToolState<TierQuestions>;
  questions: TierQuestion[];
  answers: Record<string, string>;
  setAnswers: (answers: Record<string, string>) => void;
  hint: ReturnType<typeof useTierHint>;
  saved: { label: string; by: string | null; ai: boolean } | null;
  triggers: TierAnswersResult['triggers'];
}) {
  const reviewTypes = useLabels('review-type');
  if (!props.kind) return <Empty>Choose the proposed kind in step 1 first: the questions that apply depend on it.</Empty>;
  const savedLine = props.saved ? (
    <span className="mt-1 flex flex-wrap items-center gap-1.5 border-t border-info-line pt-2 text-xs text-navy-2">
      <CircleCheck className="size-3.5 text-success" aria-hidden />
      The engine's last answer: <strong>{props.saved.label}</strong>
      {props.saved.by && <>{props.saved.by === 'just now' ? ', just now' : `, answers by ${props.saved.by}`}</>}
      {props.saved.ai && ' (an AI client answered)'}.
    </span>
  ) : null;
  return (
    <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,300px)] items-start gap-5 medium:grid-cols-1">
      <div className="flex min-w-0 flex-col gap-3">
        <p className="m-0 text-sm text-navy-2">
          The questions for {props.kindLabel}. They cover the data it changes, how sensitive the data is and who can reach
          it. Earlier answers are not shown again: the product keeps the tier they gave, not the answers.
        </p>
        {props.questionsState.status === 'loading' ? (
          <div role="status" aria-label="Loading the tier questions" className="flex flex-col gap-2"><Skeleton className="h-24 w-full" /><Skeleton className="h-24 w-full" /></div>
        ) : !props.questionsState.envelope.ok ? (
          <PanelFailure failure={props.questionsState.envelope} />
        ) : props.questions.length ? (
          <TierQuestionsForm questions={props.questions} answers={props.answers} onChange={props.setAnswers} idPrefix="wizard-tier" implied={dataOf(props.questionsState)?.impliedAnswer ?? null} />
        ) : <Empty>No tier question applies to this kind.</Empty>}
      </div>
      <div className="flex flex-col gap-3">
        <TierHintView hint={props.hint} questions={props.questions} saved={savedLine} />
        {props.triggers.length > 0 && (
          <div className="flex flex-col gap-1 rounded-lg border border-line px-4 py-3">
            <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">These answers also trigger</span>
            <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
              {props.triggers.map((t, i) => <li key={i}>{t.reviewTypeKey ? `A ${reviewTypes.label(t.reviewTypeKey).toLowerCase()}` : `The gate requirement ${t.gateRequirementKey}`}</li>)}
            </ul>
          </div>
        )}
      </div>
    </div>
  );
}

function TierAnswersSheet(props: {
  intakeRequestId: string | null;
  kindChanged: boolean;
  kind: string;
  questions: TierQuestion[];
  answers: Record<string, string>;
  allowed: boolean;
  onDone: (result: TierAnswersResult) => void;
  disabled: boolean;
}) {
  const list = answerList(props.questions, props.answers);
  if (!props.allowed) return null;
  return (
    <ActionSheet
      tool="answer_tier_questions" site="pages/intake/Wizard#TierAnswersSheet"
      title="Save the tier answers"
      description="The engine works out the tier from your answers and keeps it with the request as a suggestion. Only an accepted tier decision sets the tier."
      trigger={<Button variant="outline" disabled={props.disabled}>Save tier answers</Button>}
      input={() => {
        if (!props.intakeRequestId) return { problem: 'Save the draft first: the answers attach to a saved request.' };
        if (!props.kind) return { problem: 'Choose the proposed kind in step 1 first.' };
        if (props.kindChanged) return { problem: 'Save the draft first: the proposed kind changed, and the engine asks the questions of the saved kind.' };
        if (!list.length) return { problem: 'Answer at least one question.', field: 'answers' };
        return { intakeRequestId: props.intakeRequestId, answers: list };
      }}
      submitLabel="Save answers"
      preview="Check with the engine"
      onDone={data => props.onDone(data)}
    >
      <Field label="Your answers" name="answers" hint={list.length < props.questions.length ? `${props.questions.length - list.length} of ${props.questions.length} questions are not answered. The engine works the tier out from the answers given.` : undefined}>
        {p => (
          <ul id={p.id} aria-describedby={p['aria-describedby']} className="m-0 flex list-none flex-col gap-2 p-0">
            {list.length ? list.map(a => {
              const q = props.questions.find(x => x.questionKey === a.questionKey)!;
              const answer = q.answers.find(x => x.answerKey === a.answerKey)!;
              return (
                <li key={a.questionKey} className="rounded-md border border-line px-3 py-2 text-sm">
                  <span className="block text-xs text-navy-3">{q.text}</span>
                  <span className="text-navy">{answer.text}</span> <Muted>(implies {answer.impliedTier.label})</Muted>
                </li>
              );
            }) : <li><Muted>No answer chosen yet.</Muted></li>}
          </ul>
        )}
      </Field>
    </ActionSheet>
  );
}

// ---------- step 4: what the tier needs ----------

function NeedsStep(props: {
  tierKey: string | null;
  source: 'engine' | 'hint' | null;
  tierLabel: string;
  kindLabel: string | null;
  state: ToolState<Requirements> | null;
}) {
  if (!props.tierKey || !props.state) return <Empty>Answer the tier questions in step 3 first: what a request needs depends on its tier.</Empty>;
  return (
    <div className="flex flex-col gap-3">
      <p className="m-0 text-sm text-navy-2">
        What {props.tierLabel}{props.kindLabel ? ` for ${props.kindLabel}` : ''} needs under the rules in force, from the gates
        to the reviews.{' '}
        {props.source === 'hint' ? 'The tier comes from your answers on this page; save them to have the engine confirm it.' : 'The tier is the engine\'s answer to the saved tier questions.'}
      </p>
      <StateBlock state={props.state} label="what the tier needs" rows={5}>
        {data => (
          <div className="flex flex-col gap-4">
            <RequirementsView data={data} forRequest />
            <GuidelineList data={data} />
          </div>
        )}
      </StateBlock>
    </div>
  );
}

// ---------- step 5: review and submit ----------

function ReviewStep(props: {
  fields: WizardFields;
  kindLabel: string | null;
  horizons: Option[] | null;
  engineTier: string | null;
  hintTier: string | null;
  answersUnsaved: boolean;
  requirements: Requirements | null;
  dirty: boolean;
  saved: boolean;
}) {
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  const f = props.fields;
  const intakeApprovals = props.requirements?.approvals.filter(a => a.decisionTypeKey === 'intake') ?? null;
  const checks: { ok: boolean; text: ReactNode }[] = [
    { ok: props.saved, text: props.saved ? 'The draft is saved.' : 'Save the draft first.' },
    { ok: !props.dirty || !props.saved, text: props.dirty && props.saved ? 'Some changes are not saved. The engine submits what is saved, so save first.' : 'Every change is saved.' },
    { ok: !!props.engineTier, text: props.engineTier ? `The engine suggests ${props.engineTier}.` : props.answersUnsaved ? 'The tier answers are not saved yet. Save them in step 3, or the platform admin asks for them.' : 'No tier answers yet. They help the platform admin decide.' },
  ];
  return (
    <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,340px)] items-start gap-5 medium:grid-cols-1">
      <div className="flex min-w-0 flex-col gap-3">
        <Facts
          items={[
            { term: 'Title', value: f.title.trim() || <Muted>Not given</Muted> },
            { term: 'The need', value: <Prose text={f.purpose} /> },
            { term: 'Business owner', value: f.businessOwner?.displayLabel || f.owner.trim() || <Muted>Not named</Muted> },
            { term: 'Proposed kind', value: props.kindLabel ?? <Muted>Not chosen</Muted> },
            { term: 'Uses AI', value: f.usesAi === 'yes' ? 'Yes' : f.usesAi === 'no' ? 'No' : <Muted>Not said</Muted> },
            { term: 'Horizon', value: props.horizons?.find(h => h.key === f.horizonKey)?.label ?? (f.horizonKey || <Muted>Not chosen</Muted>) },
            { term: 'Expected benefit', value: <Prose text={f.expectedBenefit} empty="Not given" /> },
            { term: 'Existing application', value: f.businessApplicationId ? (f.applicationName || 'Chosen') : <Muted>None: a new application</Muted> },
            { term: 'Tier', value: props.engineTier ? `${props.engineTier} (the engine's suggestion)` : props.hintTier ? `${props.hintTier} (from your answers, not saved)` : <Muted>Not answered</Muted> },
          ]}
        />
      </div>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-2 rounded-lg border border-line px-4 py-3">
          <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Before you submit</span>
          <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
            {checks.map((c, i) => (
              <li key={i} className="flex items-start gap-1.5 text-sm text-navy">
                {c.ok ? <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-label="Done:" /> : <CircleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-label="To do:" />}
                <span>{c.text}</span>
              </li>
            ))}
          </ul>
        </div>
        <div className="flex flex-col gap-2 rounded-lg border border-line px-4 py-3">
          <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Who decides next</span>
          <p className="m-0 text-sm text-navy">
            Submitting opens the intake decision: accept the request, under the decision rights in force.
          </p>
          {intakeApprovals === null ? (
            <Muted>{props.engineTier || props.hintTier ? 'Reading who the rules ask.' : 'Answer the tier questions to see who the rules ask.'}</Muted>
          ) : intakeApprovals.length ? (
            <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
              {intakeApprovals.map((a, i) => (
                <li key={i}>
                  {a.approverRoleKey
                    ? roles.label(a.approverRoleKey)
                    : a.approverProposedOwners
                      ? `Each proposed owner, by name${f.businessOwner?.displayLabel ? ` (${f.businessOwner.displayLabel})` : ''}, accepts the role`
                      : `${ownerRoles.label(a.approverOwnerRoleKey)} of the application`}
                </li>
              ))}
            </ul>
          ) : (
            <Muted>
              The rules in force list no approval for the intake decision at this tier. At the lowest tier the engine
              accepts the intake on submit, with no approval request beyond a yes from the named business owner; the
              engine's answer says which applies.
            </Muted>
          )}
          <Muted>Consulted roles, where the rules name any, give advice that never counts toward the decision.</Muted>
        </div>
      </div>
    </div>
  );
}

// What a save sends, for the sheet: the person reads it before the engine checks it.
function DraftSummary({ fields, kinds, horizons, portfolios, stays }: { fields: WizardFields; kinds: (key: string) => string; horizons: Option[] | null; portfolios: Option[] | null; stays: (keyof WizardFields)[] }) {
  const need = composeNeed(fields.purpose, fields.owner);
  return (
    <div className="flex flex-col gap-3">
      <Facts
        className="grid-cols-[minmax(0,140px)_minmax(0,1fr)]"
        items={[
          { term: 'Title', value: fields.title.trim() || <Muted>Not given</Muted> },
          { term: 'The need', value: <Prose text={need} /> },
          // The business owner the request names goes with the save (wave 3 verification: the summary left it out).
          { term: 'Business owner', value: fields.businessOwner?.displayLabel || (stays.includes('businessOwner') ? <Muted>Kept as saved</Muted> : <Muted>Not named</Muted>) },
          { term: 'Proposed kind', value: fields.proposedKindKey ? kinds(fields.proposedKindKey) : <Muted>Not given</Muted> },
          { term: 'Uses AI', value: fields.usesAi === 'yes' ? 'Yes' : fields.usesAi === 'no' ? 'No' : <Muted>Not given</Muted> },
          { term: 'Horizon', value: fields.horizonKey ? horizons?.find(h => h.key === fields.horizonKey)?.label ?? fields.horizonKey : <Muted>Not given</Muted> },
          { term: 'Expected benefit', value: <Prose text={fields.expectedBenefit} empty="Not given" /> },
          { term: 'Application', value: fields.businessApplicationId ? fields.applicationName || 'An existing application' : <Muted>Not given</Muted> },
          { term: 'Portfolio', value: fields.portfolioId ? portfolios?.find(o => o.key === fields.portfolioId)?.label ?? 'Chosen' : <Muted>Not given</Muted> },
        ]}
      />
      {stays.length > 0 && (
        <p className="m-0 flex items-start gap-1.5 text-xs text-navy-2">
          <Info className="mt-px size-3.5 shrink-0" aria-hidden />
          <span>An emptied field keeps its saved value. The save changes only the fields that have a value.</span>
        </p>
      )}
    </div>
  );
}
