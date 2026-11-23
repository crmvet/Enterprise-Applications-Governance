// Page 4: the change and deployment timeline. Targets (environments) are the lanes. Change windows show read-only
// (D-13): no booking and no freeze tables. Enforcing a window stays in the deployment tool.
// Reads (document 24 names): get_change_schedule (two weeks back, four weeks ahead), get_deployments (history, two
// weeks back, latestOnly false), get_configuration (target-stage and deployment-route labels).
import { CalendarDays, Rocket } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import type { DeploymentItem, ScheduledChange } from '../data/contracts';
import { addDays, dayKey, dayStartMs, daysBetween, fmtDate, fmtDateTime, fmtDayMonth, fmtTime, fmtTimeRange, fmtWeekday, fmtWindow, inDays, inWindowDays, plural, utcDayRange, zoneLabel } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { activeFlags, flagHelp, flagText } from '../data/meanings';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { Row, RowList } from '../components/rows';
import { DataStamp, Empty, EnvelopeNotes, MoreRowsNote, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, ToneBadge, useNarrow } from '../components/blocks';
import { Tooltip, TooltipContent, TooltipTrigger } from '../components/ui/tooltip';
import { ChangeTypeBadge } from './Application360';

const BACK_DAYS = 14;
const AHEAD_DAYS = 28;

// The chart marks, shared by the chart and its legend. Lanes are 50 px high (h-[50px]). Change windows: solid blue
// when authorized, hollow blue when not yet, hollow gray for a draft, pale blue when closed, a red ring (c4314b) for an
// emergency. Deployments: navy succeeded, amber (c98a00) partly succeeded, red failed, a gold ring when flagged.
const windowTone = {
  authorized: 'bg-primary',
  pending: 'border-2 border-primary bg-card',
  draft: 'border-2 border-bar-muted bg-card',
  closed: 'bg-[#b4d6fa]',
};
const emergencyRing = 'ring-2 ring-[#c50f1f]';
const markerTone = {
  succeeded: 'bg-navy',
  partly: 'bg-[#f7630c]',
  failed: 'bg-[#c50f1f]',
  other: 'bg-bar-muted',
};
const flaggedRing = 'ring-3 ring-gold';

// Day headings in the agenda.
const smallHead = 'text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

interface Lane {
  key: string;
  label: string;
  stageKey: string | null;
}

export function Timeline() {
  const host = useHost();
  const narrow = useNarrow();
  const from = addDays(host.today, -BACK_DAYS);
  const to = addDays(host.today, AHEAD_DAYS);
  // The range is calendar days in the display zone. The tools filter by UTC date, so each call asks for the UTC dates
  // that cover those days, and the page keeps the items of the days it shows.
  const changes = useTool('get_change_schedule', { ...utcDayRange(from, to), top: 100 });
  const deployments = useTool('get_deployments', { latestOnly: false, ...utcDayRange(from, host.today), top: 100 });
  const stages = useLabels('target-stage');
  const routes = useLabels('deployment-route');
  const [stage, setStage] = useState<string>('all');
  const [view, setView] = useState<'chart' | 'agenda' | null>(null);
  const shownView = view ?? (narrow ? 'agenda' : 'chart');

  const changeItems = (dataOf(changes)?.items ?? []).filter(c => inWindowDays(c.windowStart, c.windowEnd, from, to));
  const deployItems = (dataOf(deployments)?.items ?? []).filter(d => inDays(d.finishedOn, from, host.today));
  // While the deployments are still being read, the chart and the agenda say so instead of showing none.
  const deploymentsLoading = deployments.status === 'loading' ? <Muted>Reading the deployments.</Muted> : null;
  const keep = (stageKey: string | null) => stage === 'all' || stageKey === stage;
  const shownChanges = changeItems.filter(c => keep(c.target?.targetStageKey ?? null));
  const shownDeploys = deployItems.filter(d => keep(d.targetStageKey));
  // Stage filters from the configured target stages, highest rank (production) first. If the labels cannot be read,
  // the stages seen in the data stand in.
  const configuredStages = stages.keys();
  const stageOptions = useMemo(() => {
    if (configuredStages.length) return [...configuredStages].reverse();
    const seen = new Set<string>();
    for (const c of changeItems) if (c.target?.targetStageKey) seen.add(c.target.targetStageKey);
    for (const d of deployItems) if (d.targetStageKey) seen.add(d.targetStageKey);
    return [...seen].sort();
  }, [configuredStages, changeItems, deployItems]);

  return (
    <>
      <PageHeader
        eyebrow="Timeline"
        title="Changes and deployments"
        subtitle={`Planned change windows and recorded deployments by target, ${fmtDate(from)} to ${fmtDate(to)}. Read-only.`}
        meta={<DataStamp envelope={changes.status === 'done' ? changes.envelope : undefined} />}
      />

      <div className="mb-3.5 flex flex-wrap items-center gap-x-5 gap-y-2">
        <SwitchTabs
          label="Targets"
          ariaLabel="Target stage"
          value={stage}
          onChange={setStage}
          options={[{ value: 'all', label: 'All' }, ...stageOptions.map(k => ({ value: k, label: stages.label(k) }))]}
        />
        <SwitchTabs<'chart' | 'agenda'>
          label="View"
          ariaLabel="View"
          value={shownView}
          onChange={setView}
          options={[
            { value: 'chart', label: 'Chart' },
            { value: 'agenda', label: 'Agenda' },
          ]}
        />
      </div>

      {shownView === 'chart' ? (
        <>
          <Panel title="By target" subtitle="Each lane is a deployment target. Bars are change windows; dots are deployments." state={changes} skeletonRows={8} notes={[]}>
            {() => (
              <>
                {deployments.status === 'done' && !deployments.envelope.ok && <PanelFailure failure={deployments.envelope} />}
                {deploymentsLoading}
                <Chart changes={shownChanges} deployments={shownDeploys} from={from} to={to} today={host.today} stages={stages} routes={routes} />
                <Legend />
              </>
            )}
          </Panel>
          <div className="mt-4 grid grid-cols-2 items-start gap-4 medium:grid-cols-1">
            <Panel title="Planned changes" count={dataOf(changes) ? shownChanges.length : undefined} state={changes} skeletonRows={6}>
              {data => (
                <>
                  {shownChanges.length ? (
                    <RowList label="Planned changes">
                      {shownChanges.map(c => <ChangeRow key={c.changeRequestId} change={c} stages={stages} />)}
                    </RowList>
                  ) : <Empty>No change windows in this range.</Empty>}
                  <MoreRowsNote skipToken={data.skipToken} what="change windows" />
                </>
              )}
            </Panel>
            <Panel title="Deployments" count={dataOf(deployments) ? shownDeploys.length : undefined} state={deployments} skeletonRows={6}>
              {data => (
                <>
                  {shownDeploys.length ? (
                    <RowList label="Deployments">
                      {shownDeploys.map(d => <DeploymentRow key={d.deploymentRecordId} deployment={d} routes={routes} />)}
                    </RowList>
                  ) : <Empty>No deployments in this range.</Empty>}
                  <MoreRowsNote skipToken={data.skipToken} what="deployments" />
                </>
              )}
            </Panel>
          </div>
        </>
      ) : (
        <Panel title="Agenda" subtitle="Change windows and deployments, day by day" state={changes} skeletonRows={8}>
          {() => (
            <>
              {deployments.status === 'done' && !deployments.envelope.ok && <PanelFailure failure={deployments.envelope} />}
              {deployments.status === 'done' && deployments.envelope.ok && (
                <EnvelopeNotes couldNotRead={deployments.envelope.couldNotRead} warnings={deployments.envelope.warnings} />
              )}
              {deploymentsLoading}
              <MoreRowsNote skipToken={dataOf(changes)?.skipToken} what="change windows" />
              <MoreRowsNote skipToken={dataOf(deployments)?.skipToken} what="deployments" />
              <Agenda changes={shownChanges} deployments={shownDeploys} today={host.today} stages={stages} routes={routes} />
            </>
          )}
        </Panel>
      )}
    </>
  );
}

// ---------- chart ----------

function lanesOf(changes: ScheduledChange[], deployments: DeploymentItem[], stages: Labels): Lane[] {
  const lanes = new Map<string, Lane>();
  for (const d of deployments) {
    if (d.deploymentTargetId) lanes.set(d.deploymentTargetId, { key: d.deploymentTargetId, label: d.targetLabel ?? 'Unknown target', stageKey: d.targetStageKey });
  }
  for (const c of changes) {
    const key = c.target?.deploymentTargetId ?? 'none';
    if (!lanes.has(key)) lanes.set(key, { key, label: c.target?.label ?? 'No target yet', stageKey: c.target?.targetStageKey ?? null });
  }
  // Production first: the configured stage rank, highest first. Lanes with no target come last.
  return [...lanes.values()].sort((a, b) => {
    if (a.key === 'none') return 1;
    if (b.key === 'none') return -1;
    return stages.rank(b.stageKey) - stages.rank(a.stageKey) || a.label.localeCompare(b.label);
  });
}

function Chart(props: { changes: ScheduledChange[]; deployments: DeploymentItem[]; from: string; to: string; today: string; stages: Labels; routes: Labels }) {
  // Day boundaries are midnights in the display zone, so a window shows on the day the viewer reads.
  const start = dayStartMs(props.from);
  const end = dayStartMs(addDays(props.to, 1));
  const at = (ms: number) => ((ms - start) / (end - start)) * 100;
  const pct = (iso: string) => at(Date.parse(iso));
  const dayPct = (date: string) => at(dayStartMs(date));
  const todayStart = dayStartMs(props.today);
  const todayPct = dayPct(props.today);
  const lanes = lanesOf(props.changes, props.deployments, props.stages);

  // Week lines on Mondays.
  const ticks: string[] = [];
  for (let i = 0; i <= daysBetween(props.from, props.to); i++) {
    const d = addDays(props.from, i);
    if (new Date(`${d}T00:00:00Z`).getUTCDay() === 1) ticks.push(d);
  }

  if (!lanes.length) return <Empty>Nothing planned or deployed in this range.</Empty>;

  const rows: ReactNode[] = [];
  let lastGroup: string | null | undefined;
  for (const lane of lanes) {
    const group = lane.key === 'none' ? 'No target yet' : props.stages.label(lane.stageKey);
    if (group !== lastGroup) {
      rows.push(
        <div key={`g-${group}`} className="col-span-full border-b border-line bg-[#fafafa] pt-1.5 pb-1 pl-0.5 text-[10px] font-semibold tracking-[0.06em] text-navy-3 uppercase">
          {group}
        </div>,
      );
      lastGroup = group;
    }
    const laneChanges = props.changes
      .filter(c => (c.target?.deploymentTargetId ?? 'none') === lane.key && c.windowStart && c.windowEnd)
      .sort((a, b) => Date.parse(a.windowStart!) - Date.parse(b.windowStart!));
    const laneDeploys = props.deployments.filter(d => d.deploymentTargetId === lane.key);
    rows.push(
      <div key={`l-${lane.key}`} className="flex h-[50px] min-w-0 flex-col justify-center border-b border-line pr-3 pl-0.5">
        <span className="truncate text-sm font-semibold text-navy" title={lane.label}>{lane.key === 'none' ? 'Target to come' : lane.label}</span>
      </div>,
    );
    rows.push(
      <div key={`r-${lane.key}`} className="relative h-[50px] border-b border-line">
        <div className="absolute inset-y-0 left-0 bg-[#f5f5f5]" style={{ width: `${todayPct}%` }} />
        {ticks.map(t => <div key={t} className="absolute inset-y-0 w-px bg-[#ebebeb]" style={{ left: `${dayPct(t)}%` }} />)}
        <div className="absolute inset-y-0 z-[1] -ml-px w-0.5 bg-gold" style={{ left: `${todayPct}%` }} />
        {laneChanges.map((c, i) => {
          const x1 = pct(c.windowStart!);
          const x2 = pct(c.windowEnd!);
          const next = laneChanges[i + 1];
          const room = (next ? pct(next.windowStart!) : 100) - x2;
          const kind =
            c.status.meaning === 'completed' ? windowTone.closed
              : c.status.meaning === 'draft' ? windowTone.draft
                : c.authorized ? windowTone.authorized : windowTone.pending;
          return (
            <span key={c.changeRequestId}>
              <Tooltip>
                <TooltipTrigger asChild>
                  <span
                    tabIndex={0}
                    role="img"
                    aria-label={`Change window: ${c.title}, ${fmtWindow(c.windowStart, c.windowEnd)}, ${c.status.label}`}
                    className={cn(
                      'absolute top-[9px] z-[2] h-3.5 min-w-2.5 cursor-default rounded-[4px] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy',
                      kind,
                      c.changeType.key === 'emergency' && emergencyRing,
                    )}
                    style={{ left: `${x1}%`, width: `${Math.max(0, x2 - x1)}%` }}
                  />
                </TooltipTrigger>
                <TooltipContent className="max-w-[280px]">
                  <ChangeTip change={c} stages={props.stages} />
                </TooltipContent>
              </Tooltip>
              {Date.parse(c.windowEnd!) >= todayStart && room > 7 && (
                <span
                  className="pointer-events-none absolute top-1.5 z-[2] truncate text-xs text-navy-2"
                  style={{ left: `calc(${x2}% + 14px)`, maxWidth: `calc(${room}% - 22px)` }}
                >
                  {c.title}
                </span>
              )}
            </span>
          );
        })}
        {laneDeploys.map(d => {
          const flagged = activeFlags(d.flags).length > 0;
          const m = d.result.meaning;
          const kind = m === 'succeeded' ? markerTone.succeeded : m === 'partly-succeeded' ? markerTone.partly : m === 'failed' ? markerTone.failed : markerTone.other;
          return (
            <Tooltip key={d.deploymentRecordId}>
              <TooltipTrigger asChild>
                <span
                  tabIndex={0}
                  role="img"
                  aria-label={`Deployment: ${d.applicationName} ${d.artifactVersion}, ${d.result.label}, ${fmtDateTime(d.finishedOn)}${flagged ? ', flagged' : ''}`}
                  className={cn(
                    'absolute top-[29px] z-[3] -ml-1.5 size-3 cursor-default rounded-full border-2 border-card focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-navy',
                    kind,
                    flagged && flaggedRing,
                  )}
                  style={{ left: `${pct(d.finishedOn)}%` }}
                />
              </TooltipTrigger>
              <TooltipContent className="max-w-[280px]">
                <DeployTip deployment={d} routes={props.routes} />
              </TooltipContent>
            </Tooltip>
          );
        })}
      </div>,
    );
  }

  return (
    <div className="grid grid-cols-[196px_minmax(0,1fr)] border-t border-line" role="group" aria-label="Timeline chart. The lists below hold the same items.">
      <div className="h-[34px] border-b border-line" />
      <div className="relative h-[34px] border-b border-line">
        {ticks.filter(t => Math.abs(dayPct(t) - todayPct) > 6).map(t => (
          <span key={t} className="absolute top-2 -translate-x-1/2 text-xs whitespace-nowrap text-navy-3" style={{ left: `${dayPct(t)}%` }}>
            {fmtDayMonth(t)}
          </span>
        ))}
        <span
          className="absolute top-1.5 z-[2] -translate-x-1/2 rounded-full bg-gold px-2 py-px text-[10px] leading-4 font-semibold whitespace-nowrap text-navy"
          style={{ left: `${todayPct}%` }}
        >
          Today, {fmtDayMonth(props.today)}
        </span>
      </div>
      {rows}
    </div>
  );
}

function ChangeTip({ change, stages }: { change: ScheduledChange; stages: Labels }) {
  return (
    <div className="flex flex-col gap-y-0.5">
      <span className="font-semibold">{change.title}</span>
      <span>{change.applicationName} to {change.target ? `${change.target.label} (${stages.label(change.target.targetStageKey)})` : 'no target yet'}</span>
      <span>{fmtWindow(change.windowStart, change.windowEnd)}</span>
      <span>{change.changeType.label} change, {change.status.label.toLowerCase()}. {authorizationText(change)}.</span>
    </div>
  );
}

function DeployTip({ deployment, routes }: { deployment: DeploymentItem; routes: Labels }) {
  const flags = activeFlags(deployment.flags);
  return (
    <div className="flex flex-col gap-y-0.5">
      <span className="font-semibold">{deployment.applicationName} {deployment.artifactVersion}</span>
      <span>{deployment.result.label}, {fmtDateTime(deployment.finishedOn)}</span>
      <span>Recorded by: {routes.label(deployment.route.key)}</span>
      {flags.length > 0 && <span>Flags: {flags.map(f => flagText[f]).join(', ')}</span>}
    </div>
  );
}

function Legend() {
  const bar = (tone: string) => <span className={cn('inline-block h-2.5 w-[22px] rounded-[3px]', tone)} aria-hidden />;
  const dot = (tone: string) => <span className={cn('inline-block size-2.5 rounded-full', tone)} aria-hidden />;
  const item = 'inline-flex items-center gap-x-1.5';
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2 pt-3 text-xs text-navy-2" role="group" aria-label="Legend">
      <span className={item}>{bar(windowTone.authorized)}Authorized window</span>
      <span className={item}>{bar(windowTone.pending)}Not authorized yet</span>
      <span className={item}>{bar(windowTone.draft)}Draft</span>
      <span className={item}>{bar(windowTone.closed)}Closed</span>
      <span className={item}>{bar(cn(windowTone.authorized, emergencyRing))}Emergency</span>
      <span className={item}>{dot(markerTone.succeeded)}Succeeded</span>
      <span className={item}>{dot(markerTone.partly)}Partly succeeded</span>
      <span className={item}>{dot(markerTone.failed)}Failed</span>
      <span className={item}>{dot(cn(markerTone.succeeded, flaggedRing))}Flagged</span>
    </div>
  );
}

// ---------- rows and agenda ----------

function authorizationText(c: ScheduledChange): string {
  // WORKING DEFAULT (open design question 8): standard changes are pre-authorized by their approved model.
  if (c.authorized && c.changeType.key === 'standard') return 'Pre-authorized';
  return c.authorized ? 'Authorized' : 'Not authorized yet';
}

function ChangeRow({ change, stages }: { change: ScheduledChange; stages: Labels }) {
  return (
    <Row
      primary={change.title}
      secondary={`${change.applicationName} to ${change.target ? `${change.target.label}, ${stages.label(change.target.targetStageKey).toLowerCase()}` : 'no target yet'}`}
      tertiary={fmtWindow(change.windowStart, change.windowEnd)}
      wideEnd
      end={<><ChangeTypeBadge type={change.changeType} /><StatusBadge status={change.status} /></>}
      endSub={authorizationText(change)}
    />
  );
}

function FlagBadges({ deployment }: { deployment: DeploymentItem }) {
  const flags = activeFlags(deployment.flags);
  if (!flags.length) return null;
  return (
    <span className="contents">
      {flags.map(f => (
        <Tooltip key={f}>
          <TooltipTrigger asChild>
            <span tabIndex={0} className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring">
              <ToneBadge tone="warning">{flagText[f]}</ToneBadge>
            </span>
          </TooltipTrigger>
          <TooltipContent className="max-w-[280px]">{flagHelp[f]}</TooltipContent>
        </Tooltip>
      ))}
    </span>
  );
}

function DeploymentRow({ deployment, routes }: { deployment: DeploymentItem; routes: Labels }) {
  // WORKING DEFAULT (open design question 12): a "Pipeline step" record arrives under the pipeline's own
  // workload identity, which holds the deployment recorder role through a recorded, expiring workload grant.
  return (
    <Row
      primary={deployment.applicationName ?? 'Unknown application'}
      secondary={`${deployment.artifactName} ${deployment.artifactVersion} to ${deployment.targetLabel ?? 'an unknown target'}`}
      tertiary={`${fmtDateTime(deployment.finishedOn)}. Recorded by: ${routes.label(deployment.route.key)}`}
      end={<StatusBadge status={deployment.result} />}
      badges={activeFlags(deployment.flags).length ? <FlagBadges deployment={deployment} /> : undefined}
    />
  );
}

type AgendaItem =
  | { at: string; kind: 'change'; change: ScheduledChange }
  | { at: string; kind: 'deploy'; deployment: DeploymentItem };

// The gold "Today" rule between the past and the coming days.
const todayRule = 'mt-3.5 mb-1 flex items-center gap-x-2 text-xs font-semibold text-navy before:h-0.5 before:w-4 before:bg-gold after:h-0.5 after:grow after:bg-gold';
const agendaIcon = 'grid size-8 place-items-center rounded-full';

function Agenda(props: { changes: ScheduledChange[]; deployments: DeploymentItem[]; today: string; stages: Labels; routes: Labels }) {
  const items: AgendaItem[] = [
    ...props.changes.filter(c => c.windowStart).map(c => ({ at: c.windowStart!, kind: 'change' as const, change: c })),
    ...props.deployments.map(d => ({ at: d.finishedOn, kind: 'deploy' as const, deployment: d })),
  ].sort((a, b) => Date.parse(a.at) - Date.parse(b.at));
  if (!items.length) return <Empty>Nothing planned or deployed in this range.</Empty>;

  const out: ReactNode[] = [];
  let day = '';
  let todayShown = false;
  let group: ReactNode[] = [];
  const flush = () => {
    if (group.length) out.push(<RowList key={`list-${day}`} label={day}>{group}</RowList>);
    group = [];
  };
  for (const item of items) {
    const d = dayKey(item.at);
    if (!todayShown && d >= props.today) {
      flush();
      out.push(<div key="today" className={todayRule}>Today, {fmtDayMonth(props.today)}</div>);
      todayShown = true;
    }
    if (d !== day) {
      flush();
      day = d;
      out.push(<h3 key={`h-${d}`} className={cn(smallHead, 'mt-3.5 mb-0.5 first:mt-0')}>{fmtWeekday(d)}</h3>);
    }
    if (item.kind === 'change') {
      const c = item.change;
      group.push(
        <Row
          key={c.changeRequestId}
          lead={<span className={cn(agendaIcon, 'bg-secondary text-brand-dark')} aria-hidden><CalendarDays className="size-4.5" aria-hidden /></span>}
          primary={c.title}
          secondary={`${c.applicationName} to ${c.target?.label ?? 'no target yet'}`}
          tertiary={c.windowStart && c.windowEnd ? `${fmtTimeRange(c.windowStart, c.windowEnd)} ${zoneLabel()}. ${authorizationText(c)}.` : `${authorizationText(c)}.`}
          badges={<><ChangeTypeBadge type={c.changeType} /><StatusBadge status={c.status} /></>}
        />,
      );
    } else {
      const dep = item.deployment;
      group.push(
        <Row
          key={dep.deploymentRecordId}
          lead={<span className={cn(agendaIcon, 'bg-background text-navy-2')} aria-hidden><Rocket className="size-4.5" aria-hidden /></span>}
          primary={`${dep.applicationName ?? 'Unknown application'} ${dep.artifactVersion}`}
          secondary={`Deployed to ${dep.targetLabel ?? 'an unknown target'} at ${fmtTime(dep.finishedOn)} ${zoneLabel()}`}
          tertiary={`Recorded by: ${props.routes.label(dep.route.key)}`}
          badges={<><StatusBadge status={dep.result} /><FlagBadges deployment={dep} /></>}
        />,
      );
    }
  }
  flush();
  if (!todayShown) out.push(<div key="today" className={todayRule}>Today, {fmtDayMonth(props.today)}</div>);
  return (
    <>
      <Muted>{plural(items.length, 'item', 'items')}, oldest first. Times in {zoneLabel()}.</Muted>
      {out}
    </>
  );
}
