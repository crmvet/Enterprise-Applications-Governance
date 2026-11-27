// Retirement (screen "Retirement tracker", priority 2): P09 as a stepper, from the retire decision to retirement
// complete (document 23, P09).
//   /app/retirement?show=proposed|decided|completing|retired   the applications on their way out
//   /app/retirement/<applicationId>    the tracker of one application (/app/records/business-application/<id>/retire
//                                      opens it too)
// Steps: 1 portfolio decision (retire or replace), 2 dependants told (get_dependency_impact, both ways), 3 data
// retention decision (data use), 4 removal change, 5 removal evidence, 6 gap check confirmation (components vanished
// and resolved as retired), 7 retirement complete (the retirement outcome complete; Balanced, B21-9).
// Reads: get_decision_log, find_applications (the stages whose meaning is retired; a search to open a tracker),
// get_application_card, get_dependency_impact, get_evidence (the changes and the evidence links of the application),
// list_claims (vanished), get_configuration (the lifecycle stages and the decision rights).
// Writes, each in the shared side sheet: save_decision_proposal (the portfolio decision, the retention decision,
// retirement complete), save_change_request_draft (the removal change), save_link (evidence on the removal change),
// link_component (a component the gap check no longer sees is retired). Submitting a proposal happens on its decision
// page, and the business owner answers there.
import { CircleCheck, CircleDashed, Clock, Search, TriangleAlert } from 'lucide-react';
import { useRef, useState, type ReactElement, type ReactNode } from 'react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import type { RouteOf } from '../app/routes';
import { DataStamp, Empty, KpiTile, MoreRowsNote, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { useSettled } from '../components/people';
import { ExternalLink, Row, RowList } from '../components/rows';
import { Button } from '../components/ui/button';
import { Input } from '../components/ui/input';
import { Skeleton } from '../components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { ApplicationCard, ClaimItem, DecisionLogItem, DependencyImpact, EvidencePack, ImpactNode } from '../data/contracts';
import { fmtDate, fmtDateTime, fmtWindow, plural } from '../data/format';
import { useLabels } from '../data/labels';
import { useConfigRows, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf, useTool, type ToolState } from '../data/toolClient';
import { filterByText } from '../data/textSearch';
import { ListSearch, useListText } from '../components/listSearch';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { cn } from '../lib/utils';
import { ChangeDraftSheet } from './changes-records/ChangeDraftSheet';
import { SaveLinkSheet } from './changes-records/parts';
import { kpiRowClass4, PersonLink, twoColumnClass } from './owners-reviews/people';
import {
  mayPropose,
  ownerRolesOf,
  RETENTION_OUTCOME,
  RETIREMENT_DONE_OUTCOME,
  retirementSteps,
  STEP_STATE_TEXT,
  STEP_TONE,
  vanishedClaimFor,
  WAY_OUT_STAGES,
  wayOutRows,
  type CardComponent,
  type RetirementPicture,
  type RetirementStep,
  type StepState,
  type WayOutRow,
  type WayOutStage,
} from './pmo-portfolio/logic';
import { useAllRows, useRightRows } from './pmo-portfolio/reads';
import { ProposeDecisionSheet, RetireComponentSheet } from './pmo-portfolio/sheets';
// Wave 4b: each proposal sheet stays mounted once the step reads again (Sticky), so its "Send it for approval
// now?" follow-up stays open after the step no longer offers the proposal.
import { Sticky } from './work-approvals/parts';

const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';
const SHOW = /^(proposed|decided|completing|retired)$/;
const ID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

// The stage keys whose engine meaning is retired (C12), from the configuration in force.
function useRetiredStageKeys(): string[] | null {
  const { rows, state } = useConfigRows('lifecycle-stage');
  if (state.status === 'loading') return null;
  return rows.filter(r => r.fields.engineMeaning === 'retired').map(r => r.key);
}

export function Retirement({ route }: { route: RouteOf<'retirement'> }) {
  return route.applicationId ? <Tracker applicationId={route.applicationId} /> : <WayOutList />;
}

// =====================================================================================================================
// The list: /app/retirement
// =====================================================================================================================

function WayOutList() {
  const narrow = useNarrow();
  const show = useQueryParam('show', SHOW) as WayOutStage | undefined;
  const decisions = useAllRows('get_decision_log', { decisionTypeKeys: ['portfolio', 'retirement'], top: 100 });
  const retiredKeys = useRetiredStageKeys();
  const retired = useAllRows('find_applications', retiredKeys && retiredKeys.length
    ? { lifecycleStageKeys: retiredKeys, registrationMeanings: ['registered', 'archived'], top: 100 }
    : null);
  const retiredApps = retiredKeys && retiredKeys.length === 0 ? [] : dataOf(retired)?.items ?? null;
  const log = dataOf(decisions)?.items ?? null;
  const rows = log && retiredApps ? wayOutRows(log, retiredApps) : null;
  // A search over the rows (q=) and a portfolio filter (portfolio=), as on every other list (product review, fix round 1).
  const text = useListText();
  const portfolio = useQueryParam('portfolio', ID)?.toLowerCase();
  const portfolios = useTool('list_portfolios', { top: 100 });
  const portfolioItems = (dataOf(portfolios)?.items ?? []).filter(p => p.isActive);
  const inPortfolio = useAllRows('find_applications', portfolio ? { portfolioId: portfolio, registrationMeanings: ['registered', 'archived', 'draft'], top: 100 } : null);
  const portfolioApps = portfolio ? new Set((dataOf(inPortfolio)?.items ?? []).map(a => a.businessApplicationId.toLowerCase())) : null;
  const pickPortfolio = (value: string) => navigate(withParams(`${window.location.pathname}${window.location.search}`, { portfolio: value === 'all' ? undefined : value }), true);
  const narrowed = filterByText(
    (rows ?? []).filter(r => !portfolioApps || portfolioApps.has(r.businessApplicationId.toLowerCase())),
    text,
    r => [r.name, r.wayOut?.statement, r.wayOut?.outcome?.label, r.retirement?.statement, WAY_OUT_STAGES.find(s => s.key === r.stage)?.label],
  );
  const shown = rows ? (show ? narrowed.filter(r => r.stage === show) : narrowed) : [];
  const count = (stage: WayOutStage) => (rows ? rows.filter(r => r.stage === stage).length : null);
  const setShow = (next: WayOutStage | undefined) => navigate(withParams(hrefTo({ page: 'retirement' }), { show: next }), true);
  // The list is ready once both reads answered; either one failing shows in the panel.
  const state: ToolState<unknown> = decisions.status === 'loading' ? decisions
    : !decisions.envelope.ok ? decisions
      : retiredKeys === null || (retiredKeys.length > 0 && retired.status === 'loading') ? { status: 'loading' }
        : retiredKeys.length > 0 && retired.status === 'done' && !retired.envelope.ok ? retired
          : decisions;
  const stageLabel = WAY_OUT_STAGES.find(s => s.key === show)?.label;

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Retirement"
        subtitle="Applications on their way out, and how far each one has come: from the portfolio decision to retire or replace it, to retirement complete."
        meta={<DataStamp envelope={decisions.status === 'done' ? decisions.envelope : undefined} />}
      />

      <div className={kpiRowClass4} role="group" aria-label="Key figures">
        {WAY_OUT_STAGES.map(s => (
          <KpiTile
            key={s.key}
            label={s.label}
            value={count(s.key)}
            caption={s.caption}
            attention={s.key === 'completing' && !!count(s.key)}
            onClick={() => setShow(show === s.key ? undefined : s.key)}
            className={cn(show === s.key && 'border-primary ring-1 ring-primary')}
          />
        ))}
      </div>

      <div className={twoColumnClass}>
        <Panel
          title={stageLabel ? `${stageLabel}` : 'On their way out'}
          subtitle="Newest first. Each one opens its tracker with every step."
          count={rows ? shown.length : undefined}
          state={state}
          skeletonRows={6}
          actions={show ? <Button variant="link" size="sm" className="h-7 px-1" onClick={() => setShow(undefined)}>Show all</Button> : undefined}
        >
          {() => (
            <>
              <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Narrow the list">
                <ListSearch label="Search the applications on their way out" placeholder="Application or decision" />
                <Select value={portfolio ?? 'all'} onValueChange={pickPortfolio}>
                  <SelectTrigger className="w-56 narrow:w-full" aria-label="Portfolio"><SelectValue placeholder="All portfolios" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All portfolios</SelectItem>
                    {portfolioItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{p.name}</SelectItem>)}
                  </SelectContent>
                </Select>
              </div>
              {shown.length === 0 ? (
                <Empty>{text || portfolio ? 'No application on its way out matches.' : show ? 'No application is at this point.' : 'No application is on its way out. A portfolio decision to retire or replace one starts its retirement.'}</Empty>
              ) : narrow ? (
                <RowList label="Applications on their way out">
                  {shown.map(r => (
                    <Row
                      key={r.businessApplicationId}
                      primary={<TextLink href={hrefTo({ page: 'retirement', applicationId: r.businessApplicationId })}>{r.name}</TextLink>}
                      secondary={decisionLine('Portfolio', r.wayOut)}
                      tertiary={decisionLine('Retirement', r.retirement)}
                      end={<StageBadge stage={r.stage} />}
                    />
                  ))}
                </RowList>
              ) : (
                <WayOutTable rows={shown} />
              )}
              {decisions.status === 'done' && decisions.envelope.ok && <MoreRowsNote skipToken={decisions.envelope.data.skipToken} what="decisions" />}
            </>
          )}
        </Panel>
        <div className={stack}>
          <OpenTracker />
          <Panel title="How a retirement runs">
            <ol className="m-0 flex list-decimal flex-col gap-1 pl-5 text-sm text-navy-2">
              <li>The PMO proposes a portfolio decision to retire or replace; the business owner decides.</li>
              <li>The owners of every application that depends on it are told.</li>
              <li>The data owner decides what happens to the data.</li>
              <li>A change request plans the removal.</li>
              <li>People remove it in Microsoft's tools and link the evidence.</li>
              <li>The gap check no longer sees its components, and each is confirmed retired.</li>
              <li>The business owner accepts retirement complete, and the stage moves to retired.</li>
            </ol>
          </Panel>
        </div>
      </div>
    </>
  );
}

function decisionLine(what: string, d: DecisionLogItem | null): string {
  if (!d) return `${what}: no decision`;
  const when = d.decidedOn ? ` ${fmtDate(d.decidedOn)}` : '';
  return `${what}: ${d.outcome?.label ?? 'no outcome'}, ${d.status.label.toLowerCase()}${when}`;
}

function StageBadge({ stage }: { stage: WayOutStage }) {
  const label = WAY_OUT_STAGES.find(s => s.key === stage)?.label ?? stage;
  const tone = stage === 'retired' ? 'success' : stage === 'completing' ? 'warning' : stage === 'decided' ? 'brand' : 'warning';
  return <ToneBadge tone={tone}>{label}</ToneBadge>;
}

function DecisionCell({ d }: { d: DecisionLogItem | null }) {
  if (!d) return <Muted>None</Muted>;
  return (
    <span className="flex flex-col items-start gap-0.5">
      <TextLink href={hrefTo({ page: 'decision', id: d.decisionId.toLowerCase() })} className="text-sm">{d.outcome?.label ?? 'No outcome'}</TextLink>
      <span className="text-xs text-navy-3">{d.status.label}{d.decidedOn ? `, ${fmtDate(d.decidedOn)}` : ''}</span>
    </span>
  );
}

function WayOutTable({ rows }: { rows: readonly WayOutRow[] }) {
  return (
    <Table aria-label="Applications on their way out">
      <TableHeader>
        <TableRow>
          <TableHead>Application</TableHead>
          <TableHead>Portfolio decision</TableHead>
          <TableHead>Retirement decision</TableHead>
          <TableHead>Where it is</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(r => (
          <TableRow key={r.businessApplicationId} className="align-top">
            <TableCell className="whitespace-normal">
              <div className="flex flex-col items-start gap-2">
                <TextLink href={hrefTo({ page: 'retirement', applicationId: r.businessApplicationId })} className="text-sm">{r.name}</TextLink>
                <TextLink href={hrefTo({ page: 'app', id: r.businessApplicationId })} className="text-xs">Application 360</TextLink>
              </div>
            </TableCell>
            <TableCell className="whitespace-normal"><DecisionCell d={r.wayOut} /></TableCell>
            <TableCell className="whitespace-normal"><DecisionCell d={r.retirement} /></TableCell>
            <TableCell><StageBadge stage={r.stage} /></TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// A search for any application, to open its tracker: a retirement starts with a portfolio decision proposed there.
function OpenTracker() {
  const [text, setText] = useState('');
  const settled = useSettled(text.trim());
  const asking = settled.length >= 2;
  const found = useRead('find_applications', asking ? { text: settled, top: 8 } : null);
  const items = dataOf(found)?.items ?? [];
  return (
    <Panel title="Start or follow a retirement" subtitle="Open the tracker of any application. A retirement starts with a portfolio decision proposed there.">
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
        <Input
          type="search"
          value={text}
          onChange={e => setText(e.target.value)}
          placeholder="Application name"
          aria-label="Find an application"
          autoComplete="off"
          maxLength={200}
          className="pl-8"
        />
      </div>
      {asking && (
        <div aria-live="polite">
          {found.status === 'loading' ? <Muted>Searching.</Muted>
            : !found.envelope.ok ? <Muted>The search did not answer: {found.envelope.error.message}</Muted>
              : items.length === 0 ? <Muted>No application matches "{settled}".</Muted>
                : (
                  <RowList label="Applications found">
                    {items.map(a => (
                      <Row
                        key={a.businessApplicationId}
                        primary={<TextLink href={hrefTo({ page: 'retirement', applicationId: a.businessApplicationId.toLowerCase() })}>{a.name}</TextLink>}
                        secondary={[a.stage?.label ?? 'No stage', a.tier?.label].filter(Boolean).join(' · ')}
                      />
                    ))}
                  </RowList>
                )}
        </div>
      )}
    </Panel>
  );
}

// =====================================================================================================================
// One application's tracker: /app/retirement/<applicationId>
// =====================================================================================================================

const STEP_ICON: Record<StepState, ReactElement> = {
  done: <CircleCheck aria-hidden />,
  waiting: <Clock aria-hidden />,
  'to-do': <TriangleAlert aria-hidden />,
  attention: <TriangleAlert aria-hidden />,
  'not-yet': <CircleDashed aria-hidden />,
};

function StepBadge({ state }: { state: StepState }) {
  return <ToneBadge tone={STEP_TONE[state]} icon={STEP_ICON[state]}>{STEP_STATE_TEXT[state]}</ToneBadge>;
}

const OVERALL: Record<RetirementPicture['overall'], { label: string; tone: 'success' | 'brand' | 'warning' | 'subtle' }> = {
  'not-out': { label: 'Not on its way out', tone: 'subtle' },
  proposed: { label: 'Retire proposed', tone: 'warning' },
  'on-the-way': { label: 'On its way out', tone: 'brand' },
  retired: { label: 'Retired', tone: 'success' },
};

function Tracker({ applicationId }: { applicationId: string }) {
  const session = useSession();
  const card = useRead('get_application_card', { businessApplicationId: applicationId });
  const decisions = useAllRows('get_decision_log', { businessApplicationId: applicationId, decisionTypeKeys: ['portfolio', 'retirement', 'data-use'], top: 100 });
  const dependants = useRead('get_dependency_impact', { businessApplicationId: applicationId, direction: 'dependants', depth: 3 });
  const dependencies = useRead('get_dependency_impact', { businessApplicationId: applicationId, direction: 'dependencies', depth: 1 });
  const evidence = useRead('get_evidence', { businessApplicationId: applicationId });
  const claims = useRead('list_claims', session.can('list_claims') ? { findingKinds: ['vanished'], statusMeanings: ['open'], top: 100 } : null);
  const stages = useLabels('lifecycle-stage');
  const rights = useRightRows();

  const c = dataOf(card) as ApplicationCard | null;
  const app = c?.application ?? null;
  const portfolioPath = (c?.application as { portfolio?: { portfolioId: string; name: string }[] } | undefined)?.portfolio ?? [];
  const log = dataOf(decisions)?.items ?? [];
  const impact = dataOf(dependants) as DependencyImpact | null;
  const pack = dataOf(evidence) as EvidencePack | null;
  const liveDependants = impact ? impact.nodes.filter(n => n.lifecycleMeaning !== 'retired') : null;
  const retiredStage = !!app?.stage && stages.field<string>(app.stage.key, 'engineMeaning') === 'retired';
  const picture = retirementSteps({
    decisions: log,
    dependants: liveDependants,
    dependantsFailed: dependants.status === 'done' && !dependants.envelope.ok,
    changes: pack?.changes ?? [],
    evidenceLinks: pack?.evidenceLinks ?? [],
    components: c ? c.components ?? [] : null,
    retiredStage,
    retentionGaps: c?.data ? c.data.findings.filter(f => f.code === 'DC-13') : null,
  });

  const myOwnerRoles = ownerRolesOf(c?.owners, session.objectId);
  const may = (typeKey: string) => session.can('save_decision_proposal') && mayPropose(rights.rows, typeKey, session.roleKeys, myOwnerRoles);
  const name = app?.name ?? 'This application';

  if (card.status === 'done' && !card.envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Retirement tracker" title="Retirement tracker" />
        <Panel title="The application" state={card}>{() => null}</Panel>
        <p className="mt-3 text-sm"><TextLink href={hrefTo({ page: 'retirement' })}>All retirements</TextLink></p>
      </>
    );
  }

  const ready = decisions.status === 'done' && evidence.status === 'done' && card.status === 'done';
  const stepById = (key: RetirementStep['key']) => picture.steps.find(s => s.key === key)!;

  return (
    <>
      <PageHeader
        eyebrow="Retirement tracker"
        title={app ? app.name : <Skeleton className="h-8 w-64" />}
        status={ready ? <ToneBadge tone={OVERALL[picture.overall].tone}>{OVERALL[picture.overall].label}</ToneBadge> : undefined}
        subtitle={app ? [
          `Stage: ${app.stage?.label ?? 'not set'}`,
          app.tier ? app.tier.label : null,
          portfolioPath.length ? `Portfolio: ${portfolioPath.map(p => p.name).join(' / ')}` : null,
        ].filter(Boolean).join(' · ') : undefined}
        meta={<DataStamp envelope={card.status === 'done' ? card.envelope : undefined} />}
        actions={(
          <>
            <Button asChild variant="outline"><a href={hrefTo({ page: 'app', id: applicationId })}>Application 360</a></Button>
            <Button asChild variant="outline"><a href={hrefTo({ page: 'evidence', subjectKind: 'business-application', id: applicationId })}>Evidence pack</a></Button>
          </>
        )}
      />

      <nav aria-label="Retirement steps" className="mb-4 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <ol className="m-0 grid list-none grid-cols-7 gap-2 p-0 medium:grid-cols-4 narrow:grid-cols-1 narrow:gap-1.5">
          {picture.steps.map(s => (
            <li key={s.key} className="min-w-0">
              <a
                href={`#step-${s.key}`}
                className="flex h-full flex-col gap-1 rounded-lg border border-line px-2.5 py-2 text-left no-underline hover:border-line-strong hover:bg-[#fafafa] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring narrow:flex-row narrow:items-center narrow:justify-between"
                onClick={e => {
                  e.preventDefault();
                  document.getElementById(`step-${s.key}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                  document.getElementById(`step-${s.key}-title`)?.focus({ preventScroll: true });
                }}
              >
                <span className="text-xs font-semibold text-navy-2">{s.number}. {s.title}</span>
                {ready ? <StepBadge state={s.state} /> : <Skeleton className="h-4 w-16" />}
              </a>
            </li>
          ))}
        </ol>
      </nav>

      {[decisions, evidence].map((st, i) => (st.status === 'done' && !st.envelope.ok ? (
        <div key={i} className="mb-4">
          <PanelFailure failure={st.envelope} />
          <Muted>{i === 0 ? 'The decisions could not be read, so steps 1, 3 and 7 show nothing on record.' : 'The changes and evidence links could not be read, so steps 4 and 5 show nothing on record.'}</Muted>
        </div>
      ) : null))}

      {ready && (
        <div className="mb-4">
          {picture.overall === 'not-out' ? (
            <div className="flex items-start gap-x-2 rounded-lg border border-info-line bg-info-soft px-3 py-2.5 text-sm leading-5 text-navy-2" role="note">
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-info" aria-hidden />
              <span>{name} is not on its way out. A retirement starts with a portfolio decision to retire or replace it, in step 1; the later steps open once the business owner accepts it.</span>
            </div>
          ) : picture.left.length === 0 ? (
            <div className="flex items-start gap-x-2 rounded-lg border border-success-line bg-success-soft px-3 py-2.5 text-sm leading-5 text-navy-2" role="status">
              <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
              <span>Every step is done. {name} is retired, with the evidence on record.</span>
            </div>
          ) : (
            <div className="flex items-start gap-x-2 rounded-lg border border-info-line bg-info-soft px-3 py-2.5 text-sm leading-5 text-navy-2" role="note">
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-info" aria-hidden />
              <span>
                <span className="font-semibold text-navy">What is left: </span>
                {picture.left.map(s => `${s.number}. ${s.title.toLowerCase()}`).join('; ')}.
                {picture.overall === 'retired' && ' The application is retired, and these steps still have nothing on record.'}
              </span>
            </div>
          )}
        </div>
      )}

      <div className={twoColumnClass}>
        <div className={stack}>
          <StepPanel step={stepById('decide')} ready={ready}>
            <DecisionFacts decision={picture.wayOut} empty={picture.overruledBy ? <OverruledNote d={picture.overruledBy} /> : 'No portfolio decision says retire or replace.'} />
            <Sticky show={!picture.wayOut && may('portfolio')}>
              <ProposeDecisionSheet
                decisionTypeKey="portfolio"
                typeLabel="Portfolio"
                subjectId={applicationId}
                subjectLabel={name}
                triggerLabel="Propose a portfolio decision"
                outcome="retire"
                statement={`Retire ${name}.`}
                supersedes={picture.overruledBy ? { decisionId: picture.overruledBy.decisionId, label: `the ${picture.overruledBy.outcome?.label.toLowerCase() ?? 'earlier'} decision` } : null}
                effect="The business owner decides. Once it is accepted, the retirement steps open."
                variant="default"
                size="default"
              />
            </Sticky>
          </StepPanel>

          <StepPanel step={stepById('impact')} ready={dependants.status === 'done'}>
            <ImpactList state={dependants} title="Depend on it" empty="No live application depends on it." />
            <ImpactList state={dependencies} title="It depends on" empty="It depends on no other application." quiet />
            <p className="m-0 text-xs text-navy-3">
              The product records no message to the dependants' owners: tell them, and plan a replacement where one is needed.
              The removal change lists them as affected. <TextLink href={hrefTo({ page: 'map', id: applicationId })}>Dependency map</TextLink>
            </p>
          </StepPanel>

          <StepPanel step={stepById('data')} ready={ready}>
            <DecisionFacts decision={picture.retention} empty="No data use decision since the way-out decision." />
            <DataOwners card={c} applicationId={applicationId} decided={!!picture.retention} gaps={(c?.data?.findings ?? []).some(f => f.code === 'DC-13')} />
            <Sticky show={!picture.retention && may('data-use')}>
              <ProposeDecisionSheet
                decisionTypeKey="data-use"
                typeLabel="Data use"
                subjectId={applicationId}
                subjectLabel={name}
                triggerLabel="Propose the retention decision"
                outcome={RETENTION_OUTCOME}
                statement={`Retain the data of ${name} under its retention rules, read-only, until it is archived.`}
                effect="Link where the data went as evidence on the decision page."
              />
            </Sticky>
          </StepPanel>

          <StepPanel step={stepById('change')} ready={ready}>
            {picture.removal ? (
              <RowList label="Removal change">
                <Row
                  primary={<TextLink href={recordHref(picture.removal.recordUrl) ?? hrefTo({ page: 'change-request', id: picture.removal.changeRequestId.toLowerCase() })}>{picture.removal.recordNumber ? `${picture.removal.recordNumber}: ` : ''}{picture.removal.title}</TextLink>}
                  secondary={`${picture.removal.changeType.label} · ${picture.removal.windowStart ? fmtWindow(picture.removal.windowStart, picture.removal.windowEnd) : 'No window yet'}`}
                  tertiary={picture.removal.status?.meaning === 'draft' ? 'A draft: its creator submits it on its page for authorization.' : picture.removal.result ? `Result: ${picture.removal.result.label}` : undefined}
                  end={picture.removal.status ? <StatusBadge status={picture.removal.status} /> : undefined}
                />
              </RowList>
            ) : null}
            {!picture.removal && session.can('save_change_request_draft') && (
              <ChangeDraftSheet
                applicationId={applicationId}
                applicationName={app?.name ?? null}
                trigger={<Button size="sm" variant="outline">Draft the removal change</Button>}
              />
            )}
            <Muted>The tracker takes the latest change on the application whose title says it removes or retires it, or one planned since the way-out decision.</Muted>
          </StepPanel>
        </div>

        <div className={stack}>
          <StepPanel step={stepById('evidence')} ready={ready}>
            {picture.removalEvidence.length ? (
              <RowList label="Removal evidence">
                {picture.removalEvidence.map(l => (
                  <Row
                    key={l.evidenceLinkId}
                    primary={l.url ? <ExternalLink href={l.url}>{l.label}</ExternalLink> : l.label}
                    secondary={[l.linkKind.label, l.evidenceLevel?.label].filter(Boolean).join(' · ')}
                    tertiary={l.capturedOn ? `Linked ${fmtDateTime(l.capturedOn)}` : undefined}
                  />
                ))}
              </RowList>
            ) : null}
            <Muted>People remove it in Microsoft's tools (the admin centers, Entra, Agent 365) and link each step done here. The product removes nothing itself.</Muted>
            {session.can('save_link') && (picture.removal
              ? <SaveLinkSheet subjectKind="change-request" subjectId={picture.removal.changeRequestId} category="evidence" />
              : picture.retirement ? <SaveLinkSheet subjectKind="decision" subjectId={picture.retirement.decisionId} category="evidence" /> : null)}
          </StepPanel>

          <StepPanel step={stepById('gap-check')} ready={card.status === 'done'}>
            <ComponentsList
              card={c}
              applicationId={applicationId}
              claims={dataOf(claims)?.items ?? []}
              canRetire={session.can('link_component')}
              claimsRead={session.can('list_claims')}
            />
          </StepPanel>

          <StepPanel step={stepById('complete')} ready={ready}>
            <DecisionFacts decision={picture.retirement} empty={retiredStage ? 'The application is in the retired stage, with no retirement decision on record.' : 'Nothing is proposed yet: the retirement decision comes once the steps above are done.'} />
            <Sticky show={(!picture.retirement || (picture.retirement.status.meaning === 'accepted' && picture.retirement.outcome?.key !== RETIREMENT_DONE_OUTCOME)) && !retiredStage && may('retirement')}>
              <ProposeDecisionSheet
                decisionTypeKey="retirement"
                typeLabel="Retirement"
                subjectId={applicationId}
                subjectLabel={name}
                triggerLabel="Propose retirement complete"
                outcome={RETIREMENT_DONE_OUTCOME}
                statement={`${name} is retired: nobody uses it, and nothing of it is left running.`}
                effect="Once the business owner accepts it, the application moves to the retired stage and its due reviews are canceled."
                variant={picture.left.length <= 1 ? 'default' : 'outline'}
              />
            </Sticky>
            {picture.overall === 'on-the-way' && picture.left.filter(s => s.key !== 'complete').length > 0 && !picture.retirement && (
              <Muted>{plural(picture.left.filter(s => s.key !== 'complete').length, 'step is', 'steps are')} still open. The business owner reads them before accepting.</Muted>
            )}
            {/* Wave 4: proposing retirement complete with the gap check still open warns. */}
            {picture.steps.some(s => s.key === 'gap-check' && s.state !== 'done') && !retiredStage && (
              <Muted>Step 6 is still open: the gap check still sees a component of it. Retirement complete is accepted only when nothing of it is left running.</Muted>
            )}
          </StepPanel>
        </div>
      </div>
    </>
  );
}

function StepPanel({ step, ready, children }: { step: RetirementStep; ready: boolean; children: ReactNode }) {
  return (
    <Panel
      id={`step-${step.key}`}
      title={`${step.number}. ${step.title}`}
      subtitle={ready ? step.summary : 'Reading.'}
      actions={ready ? <StepBadge state={step.state} /> : <Skeleton className="h-5 w-20" />}
    >
      {ready ? <div className="flex flex-col items-start gap-2.5">{children}</div> : <Skeleton className="h-4 w-full" />}
    </Panel>
  );
}

function OverruledNote({ d }: { d: DecisionLogItem }) {
  return (
    <span>
      The latest accepted portfolio decision says {d.outcome?.label.toLowerCase() ?? 'something else'}
      {' '}(<TextLink href={hrefTo({ page: 'decision', id: d.decisionId.toLowerCase() })}>{d.statement || 'open it'}</TextLink>).
      A new decision to retire supersedes it.
    </span>
  );
}

function DecisionFacts({ decision, empty }: { decision: DecisionLogItem | null; empty: ReactNode }) {
  if (!decision) return <Empty>{empty}</Empty>;
  const waiting = decision.status.meaning === 'proposed';
  const s = decision.approvalsSummary;
  const submitted = s.required + s.approved + s.rejected + s.pending > 0;
  return (
    <RowList label={decision.decisionType.label}>
      <Row
        primary={<TextLink href={hrefTo({ page: 'decision', id: decision.decisionId.toLowerCase() })}>{decision.statement || decision.decisionType.label}</TextLink>}
        secondary={`${decision.decisionType.label}: ${decision.outcome?.label ?? 'no outcome'}`}
        tertiary={decision.decidedOn
          ? `Decided ${fmtDate(decision.decidedOn)}${decision.decider ? ` by ${decision.decider.displayLabel}` : ''}`
          : waiting
            ? submitted ? `Submitted: ${plural(s.pending, 'answer', 'answers')} to come.` : 'Not submitted yet: its proposer submits it on the decision page.'
            : decision.status.label}
        end={<StatusBadge status={decision.status} />}
      />
    </RowList>
  );
}

function ImpactList({ state, title, empty, quiet }: { state: ToolState<DependencyImpact>; title: string; empty: string; quiet?: boolean }) {
  if (state.status === 'loading') return <Skeleton className="h-4 w-full" />;
  if (!state.envelope.ok) return <Muted>{title}: the dependency walk did not answer. {state.envelope.error.message}</Muted>;
  const nodes = state.envelope.data.nodes;
  const live = nodes.filter(n => n.lifecycleMeaning !== 'retired');
  return (
    <div className="flex w-full flex-col gap-1">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{title}</span>
      {live.length === 0 ? <Muted>{empty}</Muted> : (
        <RowList label={title}>
          {live.map(n => <ImpactRow key={`${n.businessApplicationId}-${n.depth}`} node={n} quiet={quiet} />)}
        </RowList>
      )}
      {state.envelope.data.truncatedAtDepth && <Muted>The walk stopped at its depth limit: more may lie beyond.</Muted>}
    </div>
  );
}

function ImpactRow({ node, quiet }: { node: ImpactNode; quiet?: boolean }) {
  const owners = [node.businessOwner, node.technicalOwner].filter((p): p is NonNullable<typeof p> => !!p);
  return (
    <Row
      primary={<TextLink href={hrefTo({ page: 'app', id: node.businessApplicationId.toLowerCase() })}>{node.name}</TextLink>}
      secondary={[node.depth > 1 ? `${node.depth} hops away` : 'Directly', node.tier?.label, node.criticalityKey ? `${node.criticalityKey} criticality` : null].filter(Boolean).join(' · ')}
      tertiary={quiet ? undefined : owners.length ? <>Tell: {owners.map((p, i) => <span key={p.objectId}>{i > 0 ? ', ' : ''}<PersonLink principal={p} /></span>)}</> : 'No owner named to tell'}
    />
  );
}

// decided: a data use decision is on record above; gaps: a domain it writes has no retention decision of its own (DC-13).
function DataOwners({ card, applicationId, decided, gaps }: { card: ApplicationCard | null; applicationId: string; decided: boolean; gaps: boolean }) {
  const owners = (card?.owners ?? []).filter(o => o.ownerRoleKey === 'data-owner');
  // The data owners of the domains it uses decide their retention (DC-13), named before the application's own.
  const domainOwners = (card?.data?.dataOwners ?? []).filter(o => o.principal);
  if (!card) return null;
  if (domainOwners.length) {
    // Fix round 2: "Decided 1 Jul 2026 by Rosa Tamaki" beside "Decided by the data owner of each domain" read as two
    // deciders of one decision. The decision above is the record; the line below says who decides from now on.
    return (
      <span className="text-sm text-navy-2">
        {decided ? `The decision above was taken for the application as a whole${gaps ? ", so each domain it writes still needs its data owner's retention decision" : ''}. ` : ''}
        Each domain's retention is its data owner's to decide: {domainOwners.map((o, i) => <span key={`${o.domain.key}-${i}`}>{i > 0 ? '; ' : ''}{o.domain.path ?? o.domain.label}, <PersonLink principal={o.principal!} /></span>)}.
      </span>
    );
  }
  if (decided && gaps) {
    return <Muted>The decision above is the application's own. Each domain it writes still needs its data owner's retention decision.</Muted>;
  }
  if (!owners.length) {
    return (
      <Muted>
        No data owner is named on the application, so nobody holds the data use decision.{' '}
        <TextLink href={hrefTo({ page: 'owners', view: 'gaps', applicationId })}>Name one</TextLink>
      </Muted>
    );
  }
  return (
    <span className="text-sm text-navy-2">
      Decided by the data owner: {owners.map((o, i) => <span key={o.ownerAssignmentId}>{i > 0 ? ', ' : ''}<PersonLink principal={o.principal} />{o.isDeputy ? ' (deputy)' : ''}</span>)}.
    </span>
  );
}

function ComponentsList({ card, applicationId, claims, canRetire, claimsRead }: {
  card: ApplicationCard | null;
  applicationId: string;
  claims: ClaimItem[];
  canRetire: boolean;
  claimsRead: boolean;
}) {
  const linked = card?.components ?? [];
  // A component confirmed retired from this list stays in it, marked retired, until the person leaves the page: its
  // sheet keeps the engine's answer in view. The card leaves out retired components.
  const listed = useRef(new Map<string, CardComponent>());
  for (const comp of linked) listed.current.set(comp.technicalComponentId.toLowerCase(), comp);
  const linkedIds = new Set(linked.map(comp => comp.technicalComponentId.toLowerCase()));
  const components = [...listed.current.values()];
  if (!components.length) return <Empty>No component is still linked to the application.</Empty>;
  return (
    <>
      <RowList label="Components still linked">
        {components.map(comp => {
          const claim = vanishedClaimFor(comp, claims);
          const gone = !linkedIds.has(comp.technicalComponentId.toLowerCase());
          return (
            <Row
              key={comp.technicalComponentId}
              wideEnd
              primary={comp.url ? <ExternalLink href={comp.url}>{comp.labelAsSeen || comp.externalId}</ExternalLink> : comp.labelAsSeen || comp.externalId}
              secondary={`${comp.kind.label} · ${comp.lastSeenOn ? `last seen ${fmtDateTime(comp.lastSeenOn)}` : 'never seen by a gap check'}`}
              badges={gone
                ? <ToneBadge tone="success">Retired just now</ToneBadge>
                : claim || comp.vanishedInGapCheck
                  ? <ToneBadge tone="warning">Vanished in the last gap check</ToneBadge>
                  : <ToneBadge tone="subtle" icon={null}>Still seen, or not checked</ToneBadge>}
              end={canRetire ? <RetireComponentSheet businessApplicationId={applicationId} component={comp} claimId={claim?.claimId ?? null} /> : undefined}
            />
          );
        })}
      </RowList>
      <Muted>
        {claimsRead
          ? <>Run the gap check once the removal is done: <TextLink href={hrefTo({ page: 'gap-check', view: 'vanished' })}>Gap check, vanished</TextLink>. </>
          : 'The platform admin runs the gap check once the removal is done. '}
        A component it still sees keeps the retirement open.
      </Muted>
    </>
  );
}

