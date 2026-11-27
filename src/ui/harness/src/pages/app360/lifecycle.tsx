// Lifecycle and gates (A-LIFECYCLE, P02, P09): the stages in order with the application's stage marked, and each gate
// met or held, with every requirement met or not and the action that fixes the one not met. The gates come from the
// configuration in force (gate, gate-requirement); get_requirements says met for the decision, document and review
// requirements, and the owners answer the owner ones. Retirement (P09) closes the section.
//   LifecycleStrip   the summary's one-line stepper and where the next gate stands
//   LifecycleSection the stepper, every gate in full, and retirement
import { ArrowRight, Circle, CircleCheck, CircleDot, CircleX, Plus, Shield, ShieldAlert, ShieldCheck, ShieldQuestion } from 'lucide-react';
import { Fragment, useMemo, useRef, type ReactElement } from 'react';
import { hrefTo, recordHref, recordHrefOf } from '../../app/links';
import { GuidelineChip } from '../../components/actions';
import { Empty, Muted, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Skeleton } from '../../components/ui/skeleton';
import { plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import type { Tone } from '../../data/meanings';
import { useConfigRows, useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { cn } from '../../lib/utils';
import { ProposeOwnerChangeSheet } from '../owners-reviews/OwnerSheets';
import { DocumentLinkSheet } from '../Records';
import { ownsOrAdmin, useApp360, type App360Context } from './context';
import { gateDecisionPassed, gateStanding, gateViews, lifecycleSteps, retirementStep, withArticle, type GateRequirementView, type GateStanding, type GateView, type StageStep } from './logic';
import { tabHref } from './nav';
import { KeepMounted } from './parts';
import { ArchitectureReviewSheet, checkedLabel, DocumentCheckedSheet, DocumentCurrentSheet, ProposeDecisionSheet } from './sheets';

// The review type an architecture review request opens (Balanced: architecture).
const ARCHITECTURE_REVIEW = 'architecture';

// ---------- reads ----------

function useLifecycle(ctx: App360Context) {
  const stagesRows = useConfigRows('lifecycle-stage');
  const gateRows = useConfigRows('gate');
  const requirementRows = useConfigRows('gate-requirement');
  const stageLabels = useLabels('lifecycle-stage');
  const requirements = useRead('get_requirements', { businessApplicationId: ctx.applicationId });
  const app = ctx.card?.application ?? null;
  const stageKey = app?.stage?.key ?? null;
  const steps = useMemo(() => lifecycleSteps(stagesRows.rows, stageKey), [stagesRows.rows, stageKey]);
  const gates = useMemo(() => gateViews({
    gates: gateRows.rows,
    requirementRows: requirementRows.rows,
    requirements: dataOf(requirements),
    owners: ctx.owners,
    decisions: ctx.gateDecisions,
    stageRank: key => stageLabels.rank(key),
    currentStageKey: stageKey,
    tierKey: app?.tier?.key ?? null,
    kindKey: app?.kind.key ?? null,
    today: ctx.today,
  }), [gateRows.rows, requirementRows.rows, requirements, ctx.owners, ctx.gateDecisions, stageLabels, stageKey, app, ctx.today]);
  const loading = stagesRows.loading || gateRows.loading || requirementRows.loading;
  const configFailure = [stagesRows.state, gateRows.state, requirementRows.state].map(s => (s.status === 'done' && !s.envelope.ok ? s.envelope : null)).find(Boolean) ?? null;
  const requirementsFailure = requirements.status === 'done' && !requirements.envelope.ok ? requirements.envelope : null;
  return { steps, gates, loading, configFailure, requirementsFailure, requirements };
}

// ---------- words and tones ----------

const STANDING: Record<GateStanding, { text: string; tone: Tone; icon: ReactElement }> = {
  passed: { text: 'Passed', tone: 'success', icon: <ShieldCheck aria-hidden /> },
  'passed-unmet': { text: 'Passed, not met now', tone: 'warning', icon: <ShieldAlert aria-hidden /> },
  ready: { text: 'Ready', tone: 'informative', icon: <ShieldCheck aria-hidden /> },
  held: { text: 'Held', tone: 'warning', icon: <ShieldAlert aria-hidden /> },
  checking: { text: 'Being checked', tone: 'subtle', icon: <ShieldQuestion aria-hidden /> },
};

function StandingBadge({ gate }: { gate: GateView }) {
  const s = STANDING[gateStanding(gate)];
  const ahead = gate.reach === 'ahead' && gateStanding(gate) !== 'held';
  return <ToneBadge tone={ahead ? 'subtle' : s.tone} icon={ahead ? <Shield aria-hidden /> : s.icon}>{ahead ? 'Ahead' : s.text}</ToneBadge>;
}

const stepIcon = (state: StageStep['state']) =>
  state === 'done' ? <CircleCheck className="size-4 shrink-0 text-success" aria-hidden />
    : state === 'current' ? <CircleDot className="size-4 shrink-0 text-primary" aria-hidden />
      : <Circle className="size-4 shrink-0 text-navy-4" aria-hidden />;

const stepWords: Record<StageStep['state'], string> = { done: 'done', current: 'the current stage', upcoming: 'not reached' };

// ---------- the stepper ----------

function Stepper({ steps, gates, compact }: { steps: StageStep[]; gates: GateView[]; compact?: boolean }) {
  return (
    <ol className={cn('m-0 flex list-none flex-wrap items-center gap-x-1.5 gap-y-2 p-0', !compact && 'narrow:flex-col narrow:items-stretch')} aria-label="Lifecycle stages and gates">
      {steps.map(step => (
        <Fragment key={step.key}>
          <li className="flex min-w-0 items-center">
            <span
              className={cn(
                'inline-flex min-w-0 items-center gap-1.5 rounded-md border px-2 py-1 text-xs leading-4',
                step.state === 'current' ? 'border-primary bg-info-soft font-semibold text-navy' : step.state === 'done' ? 'border-line bg-card text-navy-2' : 'border-dashed border-line-strong bg-card text-navy-3',
              )}
              aria-current={step.state === 'current' ? 'step' : undefined}
            >
              {stepIcon(step.state)}
              <span className="[overflow-wrap:anywhere]">{step.label}</span>
              <span className="sr-only">, {stepWords[step.state]}</span>
            </span>
          </li>
          {gates.filter(g => g.afterStageKey === step.key).map(g => {
            const standing = STANDING[gateStanding(g)];
            const ahead = g.reach === 'ahead' && gateStanding(g) !== 'held';
            const badge = <ToneBadge tone={ahead ? 'subtle' : standing.tone} icon={ahead ? <Shield aria-hidden /> : standing.icon}>{g.label} gate{compact ? `: ${ahead ? 'ahead' : standing.text.toLowerCase()}` : ''}</ToneBadge>;
            return (
              <li key={g.key} className="flex items-center">
                {compact ? badge : (
                  <a
                    href={`#gate-${g.key}`}
                    className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    aria-label={`${g.label} gate: ${ahead ? 'ahead' : standing.text.toLowerCase()}. Go to the gate.`}
                    onClick={e => {
                      const target = document.getElementById(`gate-${g.key}`);
                      e.preventDefault();
                      if (!target) return;
                      target.scrollIntoView({ block: 'start' });
                      target.focus({ preventScroll: true });
                    }}
                  >
                    {badge}
                  </a>
                )}
              </li>
            );
          })}
        </Fragment>
      ))}
    </ol>
  );
}

// The summary's line: the stepper, and where the next gate stands.
export function LifecycleStrip({ lifecycleHref }: { lifecycleHref: string }) {
  const ctx = useApp360();
  if (!ctx) return null;
  return <LifecycleStripBody ctx={ctx} lifecycleHref={lifecycleHref} />;
}

function LifecycleStripBody({ ctx, lifecycleHref }: { ctx: App360Context; lifecycleHref: string }) {
  const { steps, gates, loading, configFailure } = useLifecycle(ctx);
  if (configFailure) return <Muted>{`The lifecycle could not be read: ${configFailure.error.message}`}</Muted>;
  if (loading) return <Skeleton className="h-7 w-full" aria-label="Loading the lifecycle" />;
  const next = gates.find(g => g.reach === 'next') ?? null;
  const unmetPassed = gates.filter(g => gateStanding(g) === 'passed-unmet');
  return (
    <div className="flex flex-col gap-2 border-t border-line pt-3.5">
      <Stepper steps={steps} gates={gates} compact />
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-navy-2">
        {next && <span>Next gate: <span className="font-semibold text-navy">{next.label}</span>, {gateStanding(next) === 'held' ? `${plural(next.unmet, 'requirement', 'requirements')} not met` : STANDING[gateStanding(next)].text.toLowerCase()}.</span>}
        {unmetPassed.map(g => <span key={g.key}>{g.label} passed, but {plural(g.unmet, 'requirement is', 'requirements are')} not met now.</span>)}
        <TextLink href={lifecycleHref} className="inline-flex items-center gap-1 font-semibold">Lifecycle and gates <ArrowRight className="size-3.5" aria-hidden /></TextLink>
      </div>
    </div>
  );
}

// ---------- the section ----------

export function LifecycleSection() {
  const ctx = useApp360();
  if (!ctx) return null;
  return <LifecycleBody ctx={ctx} />;
}

function LifecycleBody({ ctx }: { ctx: App360Context }) {
  const { steps, gates, loading, configFailure, requirementsFailure } = useLifecycle(ctx);
  const app = ctx.card?.application ?? null;
  return (
    <div className="flex flex-col gap-4 narrow:gap-3">
      <Panel
        title="Lifecycle"
        subtitle={app?.stage ? `${app.stage.label} now. Stages and gates come from the configuration in force.` : 'No stage is set yet. Stages and gates come from the configuration in force.'}
      >
        {configFailure ? <PanelFailure failure={configFailure} /> : loading ? <Skeleton className="h-16 w-full" aria-label="Loading the lifecycle" /> : <Stepper steps={steps} gates={gates} />}
        {app?.stage?.decisionId && (
          <Muted>
            The stage was set by <TextLink href={recordHrefOf('decision', app.stage.decisionId) ?? '#'}>its decision</TextLink>.
          </Muted>
        )}
      </Panel>
      {requirementsFailure && <PanelFailure failure={requirementsFailure} />}
      {!loading && !configFailure && (gates.length ? (
        <div className="grid grid-cols-2 items-start gap-4 medium:grid-cols-1 narrow:gap-3">
          {gates.map(g => <GatePanel key={g.key} gate={g} ctx={ctx} />)}
        </div>
      ) : <Panel title="Gates"><Empty>The configuration in force has no gates.</Empty></Panel>)}
      <RetirementPanel ctx={ctx} />
    </div>
  );
}

function GatePanel({ gate, ctx }: { gate: GateView; ctx: App360Context }) {
  const stages = useLabels('lifecycle-stage');
  const types = useLabels('decision-type');
  const standing = gateStanding(gate);
  const decisionLabel = gate.decisionTypeKey ? types.label(gate.decisionTypeKey) : null;
  const canPropose = gate.decisionTypeKey ? ctx.proposable.includes(gate.decisionTypeKey) : false;
  // No second proposal once the gate's decision is accepted with an outcome that passes it (product review 2026-11-24).
  const showPropose = gate.reach === 'next' && !gate.open && !gateDecisionPassed(gate) && canPropose && gate.decisionTypeKey;
  return (
    <section id={`gate-${gate.key}`} tabIndex={-1} className="flex min-w-0 flex-col gap-3 rounded-xl border bg-card px-5 pt-[18px] pb-4 shadow-xs outline-none focus-visible:ring-2 focus-visible:ring-ring narrow:rounded-lg narrow:px-3.5 narrow:pt-3.5 narrow:pb-3" aria-labelledby={`gate-${gate.key}-title`}>
      <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1.5">
        <h2 className="m-0 text-base leading-6 font-semibold text-navy" id={`gate-${gate.key}-title`}>{gate.label} gate</h2>
        <StandingBadge gate={gate} />
      </div>
      <p className="-mt-2 mb-0 text-xs text-navy-3">
        After {gate.afterStageKey ? stages.label(gate.afterStageKey).toLowerCase() : 'its stage'}.{decisionLabel ? ` ${withArticle(`${decisionLabel.toLowerCase()} decision`, true)} passes it.` : ''}
        {standing === 'held' && ' The engine accepts that decision only once every requirement is met.'}
      </p>
      {gate.requirements.length ? (
        <ul className="m-0 flex list-none flex-col p-0" aria-label={`What the ${gate.label} gate needs`}>
          {gate.requirements.map(r => <RequirementRow key={r.key} req={r} ctx={ctx} />)}
        </ul>
      ) : <Empty>Nothing beyond its decision at this tier.</Empty>}
      {(gate.open || gate.settled) && (
        <div className="flex flex-col gap-1 border-t border-line pt-2.5">
          {[gate.open, gate.settled].filter((d, i, all) => !!d && (i === 0 || d.decisionId !== all[0]?.decisionId)).map(d => (
            <div key={d!.decisionId} className="flex flex-wrap items-center gap-2 text-sm">
              <TextLink href={recordHref(d!.recordUrl) ?? recordHrefOf('decision', d!.decisionId) ?? '#'}>{d!.statement}</TextLink>
              <StatusBadge status={d!.status} />
              {d!.outcome && <span className="text-xs text-navy-3">Outcome: {d!.outcome.label}</span>}
              <AiMark isAiCreated={d!.isAiCreated} performedBy={d!.performedBy} />
            </div>
          ))}
        </div>
      )}
      {gate.decisionTypeKey && canPropose && (
        <KeepMounted show={!!showPropose} className="block">
          <ProposeDecisionSheet
            applicationId={ctx.applicationId}
            applicationName={ctx.applicationName}
            typeKey={gate.decisionTypeKey}
            title={`Propose the ${gate.label.toLowerCase()} decision`}
            description={<>The {decisionLabel?.toLowerCase()} decision passes the {gate.label.toLowerCase()} gate of {ctx.applicationName}. It is saved as your proposal; submit it to ask the approvers.{standing === 'held' ? ' The engine holds it until every requirement is met.' : ''}</>}
            trigger={<Button size="sm" variant={standing === 'ready' ? 'default' : 'outline'}><Plus aria-hidden />Propose the {gate.label.toLowerCase()} decision</Button>}
            onProposed={ctx.onProposed}
          />
        </KeepMounted>
      )}
    </section>
  );
}

function RequirementRow({ req, ctx }: { req: GateRequirementView; ctx: App360Context }) {
  const guidelines = useLabels('guideline');
  const number = req.guidelineKey ? guidelines.field<string>(req.guidelineKey, 'number') ?? null : null;
  const icon = req.met === true ? <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
    : req.met === false ? <CircleX className="mt-0.5 size-4 shrink-0 text-danger" aria-hidden />
      : <Circle className="mt-0.5 size-4 shrink-0 text-navy-4" aria-hidden />;
  return (
    <li className="grid grid-cols-[16px_minmax(0,1fr)] gap-x-2.5 border-t border-line py-2.5 first:border-t-0 first:pt-0.5">
      {icon}
      <div className="flex min-w-0 flex-col gap-1.5">
        <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">
          {req.label}
          <span className="ml-2 text-xs font-normal text-navy-3">{req.met === true ? 'Met' : req.met === false ? 'Not met' : 'Not known yet'}</span>
        </span>
        <RequirementFix req={req} ctx={ctx} />
        {req.met === false && number && <GuidelineChip number={number} />}
      </div>
    </li>
  );
}

const lineClass = 'flex flex-wrap items-center gap-2 text-xs text-navy-2';

// The action that fixes a requirement that is not met, where the person may take it; otherwise where to look. Each
// action stays mounted once used (KeepMounted), so its sheet keeps the engine's answer after the requirement turns met.
function RequirementFix({ req, ctx }: { req: GateRequirementView; ctx: App360Context }) {
  const ownerRoles = useLabels('owner-role');
  const docTypes = useLabels('document-type');
  const reviewTypes = useLabels('review-type');
  const decisionTypes = useLabels('decision-type');
  const key = req.typeKey;
  const unmet = req.met === false;
  // The data requirements: the application's data uses declared, or each one decided. Both are done on its Data section.
  if (req.kind === 'data-uses-declared' || req.kind === 'data-uses-decided') {
    if (!unmet) return null;
    return (
      <div className={lineClass}>
        {req.kind === 'data-uses-declared' ? 'Its data uses are not declared yet.' : 'A data use waits for its decision.'}
        {' '}
        <TextLink href={tabHref(ctx.applicationId, 'data')}>{req.kind === 'data-uses-declared' ? 'Declare them in the Data section' : 'Open the Data section'}</TextLink>
      </div>
    );
  }
  if (!key) return null;

  if (req.kind === 'owner-assigned') {
    const role = ownerRoles.label(key);
    const may = ctx.can('propose_owner_change') && ownsOrAdmin(ctx);
    return (
      <>
        {may && (
          <KeepMounted show={unmet} className={lineClass}>
            <ProposeOwnerChangeSheet
              application={{ id: ctx.applicationId, name: ctx.applicationName }}
              mode="add"
              ownerRoleKey={key}
              me={ctx.session.who}
              onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
              trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Propose {withArticle(role.toLowerCase())}</Button>}
            />
          </KeepMounted>
        )}
        {unmet && !may && (
          <div className={lineClass}>An owner of the application proposes {withArticle(role.toLowerCase())}. <TextLink href={hrefTo({ page: 'owners', view: 'gaps', applicationId: ctx.applicationId })}>Owner gaps</TextLink></div>
        )}
      </>
    );
  }

  if (req.kind === 'decision-accepted') {
    const typeLabel = decisionTypes.label(key);
    const open = ctx.appDecisions.find(d => d.decisionType.key === key && d.status.meaning === 'proposed');
    const may = ctx.proposable.includes(key);
    return (
      <>
        {unmet && open && (
          <div className={lineClass}>{withArticle(`${typeLabel.toLowerCase()} decision`, true)} waits: <TextLink href={recordHref(open.recordUrl) ?? '#'}>{open.statement}</TextLink></div>
        )}
        {may && (
          <KeepMounted show={unmet && !open} className={lineClass}>
            <ProposeDecisionSheet
              applicationId={ctx.applicationId}
              applicationName={ctx.applicationName}
              typeKey={key}
              trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Propose {withArticle(`${typeLabel.toLowerCase()} decision`)}</Button>}
              onProposed={ctx.onProposed}
            />
          </KeepMounted>
        )}
        {unmet && !open && !may && <div className={lineClass}>No {typeLabel.toLowerCase()} decision is accepted yet. Its decision rights name who proposes it.</div>}
      </>
    );
  }

  if (req.kind === 'document-current') {
    const typeLabel = docTypes.label(key);
    const doc = ctx.docs?.documents.find(d => d.documentTypeKey === key) ?? null;
    const recordLink = doc ? recordHrefOf('document-link', doc.documentLinkId) : null;
    const mayRecord = ctx.can('record_document_link');
    const mayCheck = ctx.can('confirm_document_checked');
    return (
      <>
        {mayRecord && (
          <KeepMounted show={unmet && !doc} className={lineClass}>
            <DocumentLinkSheet app={{ businessApplicationId: ctx.applicationId }} typeLabel={typeLabel} missing={[key]} show />
          </KeepMounted>
        )}
        {unmet && !doc && !mayRecord && <div className={lineClass}>No {typeLabel.toLowerCase()} is linked yet. Its document owner links it.</div>}
        {doc && (
          <div className={unmet ? lineClass : 'hidden'}>
            {mayRecord && (
              <KeepMounted show={unmet && doc.status.meaning === 'draft'}>
                <DocumentCurrentSheet applicationId={ctx.applicationId} doc={doc} typeLabel={typeLabel} trigger={<Button size="sm">Confirm it is current</Button>} />
              </KeepMounted>
            )}
            {mayCheck && (
              <KeepMounted show={unmet && doc.status.meaning !== 'draft'}>
                <DocumentCheckedSheet doc={doc} typeLabel={typeLabel} trigger={<Button size="sm" aria-label={checkedLabel(typeLabel, ctx.applicationName)}>Confirm it is still true</Button>} />
              </KeepMounted>
            )}
            <StatusBadge status={doc.status} />
            {recordLink && <TextLink href={recordLink}>Open the {typeLabel.toLowerCase()}</TextLink>}
          </div>
        )}
      </>
    );
  }

  if (req.kind === 'review-done') {
    const typeLabel = reviewTypes.label(key);
    const review = ctx.card?.reviews?.find(r => r.reviewType.key === key && r.status.meaning !== 'done');
    const mayRequest = key === ARCHITECTURE_REVIEW && ctx.can('request_architecture_review');
    return (
      <>
        {unmet && review && (
          <div className={lineClass}>The {typeLabel.toLowerCase()} is <StatusBadge status={review.status} /> <TextLink href={recordHrefOf('review', review.reviewId) ?? '#'}>Open the review</TextLink></div>
        )}
        {mayRequest && (
          <KeepMounted show={unmet && !review} className={lineClass}>
            <ArchitectureReviewSheet
              applicationId={ctx.applicationId}
              applicationName={ctx.applicationName}
              changes={(ctx.card?.changes ?? []).map(c => ({ changeRequestId: c.changeRequestId, title: c.title }))}
              trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Request an architecture review</Button>}
            />
          </KeepMounted>
        )}
        {unmet && !review && !mayRequest && (
          <div className={lineClass}>No open {typeLabel.toLowerCase()}. It opens from its cadence or a tier answer. <TextLink href={hrefTo({ page: 'reviews', applicationId: ctx.applicationId, state: 'all' })}>Reviews of this application</TextLink></div>
        )}
      </>
    );
  }
  return null;
}

// ---------- retirement ----------

function RetirementPanel({ ctx }: { ctx: App360Context }) {
  const app = ctx.card?.application ?? null;
  const stages = useLabels('lifecycle-stage');
  const meaning = app?.stage ? stages.field<string>(app.stage.key, 'engineMeaning') ?? null : null;
  const step = retirementStep(ctx.appDecisions, meaning);
  const usedBy = ctx.card?.dependencies?.usedByCount ?? null;
  // The step last offered, so its sheet stays mounted with the engine's answer once the proposal makes it wait.
  const offered = useRef(step.next);
  if (step.next) offered.current = step.next;
  const next = offered.current;
  const canPropose = next ? ctx.proposable.includes(next.decisionTypeKey) : false;
  return (
    <Panel
      title="Retirement"
      subtitle="Take the application out of use on purpose: a retire decision, the impact checked, the removal done in Microsoft's tools, and retirement complete."
      state={ctx.decisionLog}
      notes={[]}
      actions={<TextLink href={hrefTo({ page: 'retirement', applicationId: ctx.applicationId })} className="inline-flex items-center gap-1 font-semibold">Retirement tracker <ArrowRight className="size-4" aria-hidden /></TextLink>}
    >
      {() => (
        <div className="flex flex-col gap-2.5">
          {step.retired ? (
            <ToneBadge tone="subtle">Retired</ToneBadge>
          ) : step.waiting ? (
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span>Waiting for its decision:</span>
              <TextLink href={recordHref(step.waiting.recordUrl) ?? '#'}>{step.waiting.statement}</TextLink>
              <StatusBadge status={step.waiting.status} />
            </div>
          ) : step.decided ? (
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span>Retirement is decided:</span>
              <TextLink href={recordHref(step.decided.recordUrl) ?? '#'}>{step.decided.statement}</TextLink>
              <StatusBadge status={step.decided.status} />
            </div>
          ) : <Empty>No retirement is decided. The application stays in use.</Empty>}
          {usedBy !== null && !step.retired && (
            <Muted>
              {usedBy === 0 ? 'Nothing depends on it.' : `${plural(usedBy, 'application depends', 'applications depend')} on it. `}
              {usedBy > 0 && <TextLink href={hrefTo({ page: 'map', id: ctx.applicationId })}>See what breaks</TextLink>}
            </Muted>
          )}
          {next && canPropose && (
            <KeepMounted show={!!step.next} className="block">
              <ProposeDecisionSheet
                key={`${next.decisionTypeKey}-${next.outcomeKey}`}
                applicationId={ctx.applicationId}
                applicationName={ctx.applicationName}
                typeKey={next.decisionTypeKey}
                outcomeKey={next.outcomeKey}
                statement={next.outcomeKey === 'retire' ? `Retire ${ctx.applicationName}.` : `Retirement of ${ctx.applicationName} is complete.`}
                title={next.label}
                description={next.outcomeKey === 'retire'
                  ? <>A portfolio decision with the outcome retire. The business owner decides; once it is accepted, check the impact, settle the data and plan the removal change.</>
                  : <>The retirement decision with the outcome complete. Once accepted, the engine moves {ctx.applicationName} to the retired stage and cancels the reviews still due.</>}
                trigger={<Button size="sm" variant="outline">{next.label}</Button>}
                onProposed={ctx.onProposed}
              />
            </KeepMounted>
          )}
        </div>
      )}
    </Panel>
  );
}
