// Page 4: the change and deployment timeline. Targets (environments) are the lanes. Change windows show read-only
// (D-13): no booking and no freeze tables. Enforcing a window stays in the deployment tool.
// Reads (document 24 names): get_change_schedule (two weeks back, four weeks ahead), get_deployments (history, two
// weeks back, latestOnly false), get_configuration (target-stage and deployment-route labels).
import { Tab, TabList, Tooltip, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { CalendarLtr20Regular, Rocket20Regular } from '@fluentui/react-icons';
import { useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import type { DeploymentItem, ScheduledChange } from '../data/contracts';
import { addDays, dayKey, dayStartMs, daysBetween, fmtDate, fmtDateTime, fmtDayMonth, fmtTime, fmtTimeRange, fmtWeekday, fmtWindow, inDays, inWindowDays, plural, utcDayRange, zoneLabel } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { activeFlags, flagHelp, flagText } from '../data/meanings';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { palette } from '../theme';
import { Row, RowList } from '../components/rows';
import { DataStamp, Empty, EnvelopeNotes, MoreRowsNote, Muted, PageHeader, Panel, PanelFailure, StatusBadge, ToneBadge, media, useNarrow } from '../components/ui';
import { ChangeTypeBadge } from './Application360';

const BACK_DAYS = 14;
const AHEAD_DAYS = 28;
const LANE_H = 50;
const DANGER = '#c4314b';
const AMBER = '#c98a00';

const useStyles = makeStyles({
  controls: { display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '8px 20px', marginBottom: '14px' },
  control: { display: 'flex', alignItems: 'center', columnGap: '4px', flexWrap: 'wrap' },
  controlLabel: { fontSize: tokens.fontSizeBase200, color: palette.navy3, marginRight: '4px' },
  chart: { display: 'grid', gridTemplateColumns: '196px minmax(0, 1fr)', borderTop: `1px solid ${palette.line}` },
  axisLabel: { height: '34px', borderBottom: `1px solid ${palette.line}` },
  axis: { position: 'relative', height: '34px', borderBottom: `1px solid ${palette.line}` },
  tick: { position: 'absolute', top: '8px', fontSize: tokens.fontSizeBase200, color: palette.navy3, transform: 'translateX(-50%)', whiteSpace: 'nowrap' },
  todayPill: {
    position: 'absolute',
    top: '6px',
    transform: 'translateX(-50%)',
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
    backgroundColor: palette.gold,
    borderRadius: '10px',
    padding: '1px 8px',
    whiteSpace: 'nowrap',
    zIndex: 2,
  },
  groupLabel: {
    gridColumn: '1 / -1',
    padding: '6px 0 4px',
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy3,
    textTransform: 'uppercase',
    letterSpacing: '0.06em',
    borderBottom: `1px solid ${palette.line}`,
    backgroundColor: '#fafbfd',
    paddingLeft: '2px',
  },
  laneLabel: {
    height: `${LANE_H}px`,
    display: 'flex',
    flexDirection: 'column',
    justifyContent: 'center',
    paddingLeft: '2px',
    paddingRight: '12px',
    borderBottom: `1px solid ${palette.line}`,
    minWidth: 0,
  },
  laneName: { fontSize: tokens.fontSizeBase300, fontWeight: tokens.fontWeightSemibold, color: palette.navy, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  lane: { position: 'relative', height: `${LANE_H}px`, borderBottom: `1px solid ${palette.line}` },
  past: { position: 'absolute', top: 0, bottom: 0, left: 0, backgroundColor: '#f5f7fa' },
  grid: { position: 'absolute', top: 0, bottom: 0, width: '1px', backgroundColor: '#e7ecf2' },
  today: { position: 'absolute', top: 0, bottom: 0, width: '2px', marginLeft: '-1px', backgroundColor: palette.gold, zIndex: 1 },
  window: {
    position: 'absolute',
    top: '9px',
    height: '14px',
    minWidth: '10px',
    borderRadius: '4px',
    boxSizing: 'border-box',
    cursor: 'default',
    zIndex: 2,
    ':focus-visible': { outline: `2px solid ${palette.navy}`, outlineOffset: '2px' },
  },
  wAuthorized: { backgroundColor: palette.blue },
  wPending: { backgroundColor: palette.white, border: `2px solid ${palette.blue}` },
  wDraft: { backgroundColor: palette.white, border: `2px solid ${palette.muted}` },
  wClosed: { backgroundColor: '#a9c1dc' },
  wEmergency: { boxShadow: `0 0 0 2px ${DANGER}` },
  windowText: {
    position: 'absolute',
    top: '6px',
    fontSize: tokens.fontSizeBase200,
    color: palette.navy2,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    pointerEvents: 'none',
    zIndex: 2,
  },
  marker: {
    position: 'absolute',
    top: '29px',
    width: '12px',
    height: '12px',
    marginLeft: '-6px',
    borderRadius: '50%',
    boxSizing: 'border-box',
    border: `2px solid ${palette.white}`,
    cursor: 'default',
    zIndex: 3,
    ':focus-visible': { outline: `2px solid ${palette.navy}`, outlineOffset: '2px' },
  },
  mSucceeded: { backgroundColor: palette.navy },
  mPartly: { backgroundColor: AMBER },
  mFailed: { backgroundColor: DANGER },
  mOther: { backgroundColor: palette.muted },
  mFlagged: { boxShadow: `0 0 0 3px ${palette.gold}` },
  tip: { display: 'flex', flexDirection: 'column', rowGap: '2px', maxWidth: '280px' },
  tipTitle: { fontWeight: tokens.fontWeightSemibold },
  legend: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px 16px',
    paddingTop: '12px',
    fontSize: tokens.fontSizeBase200,
    color: palette.navy2,
  },
  legendItem: { display: 'inline-flex', alignItems: 'center', columnGap: '6px' },
  legendBar: { display: 'inline-block', width: '22px', height: '10px', borderRadius: '3px', boxSizing: 'border-box' },
  legendDot: { display: 'inline-block', width: '10px', height: '10px', borderRadius: '50%' },
  lists: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)',
    gap: '16px',
    marginTop: '16px',
    alignItems: 'start',
    [media.medium]: { gridTemplateColumns: 'minmax(0, 1fr)' },
  },
  dayHead: {
    margin: '14px 0 2px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    ':first-child': { marginTop: 0 },
  },
  todayRule: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '8px',
    margin: '14px 0 4px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
    '::before': { content: '""', height: '2px', width: '16px', backgroundColor: palette.gold },
    '::after': { content: '""', height: '2px', flexGrow: 1, backgroundColor: palette.gold },
  },
  agendaIcon: {
    width: '32px',
    height: '32px',
    borderRadius: '50%',
    display: 'grid',
    placeItems: 'center',
    backgroundColor: palette.lightBlue,
    color: palette.blueDark,
  },
  agendaIconDeploy: { backgroundColor: palette.lightGrey, color: palette.navy2 },
  flags: { display: 'contents' },
});

interface Lane {
  key: string;
  label: string;
  stageKey: string | null;
}

export function Timeline() {
  const s = useStyles();
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

      <div className={s.controls}>
        <div className={s.control}>
          <span className={s.controlLabel}>Targets</span>
          <TabList size="small" appearance="subtle-circular" selectedValue={stage} onTabSelect={(_, d) => setStage(String(d.value))} aria-label="Target stage">
            <Tab value="all">All</Tab>
            {stageOptions.map(k => <Tab key={k} value={k}>{stages.label(k)}</Tab>)}
          </TabList>
        </div>
        <div className={s.control}>
          <span className={s.controlLabel}>View</span>
          <TabList size="small" appearance="subtle-circular" selectedValue={shownView} onTabSelect={(_, d) => setView(d.value as 'chart' | 'agenda')} aria-label="View">
            <Tab value="chart">Chart</Tab>
            <Tab value="agenda">Agenda</Tab>
          </TabList>
        </div>
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
          <div className={s.lists}>
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
  const s = useStyles();
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
      rows.push(<div key={`g-${group}`} className={s.groupLabel}>{group}</div>);
      lastGroup = group;
    }
    const laneChanges = props.changes
      .filter(c => (c.target?.deploymentTargetId ?? 'none') === lane.key && c.windowStart && c.windowEnd)
      .sort((a, b) => Date.parse(a.windowStart!) - Date.parse(b.windowStart!));
    const laneDeploys = props.deployments.filter(d => d.deploymentTargetId === lane.key);
    rows.push(
      <div key={`l-${lane.key}`} className={s.laneLabel}>
        <span className={s.laneName} title={lane.label}>{lane.key === 'none' ? 'Target to come' : lane.label}</span>
      </div>,
    );
    rows.push(
      <div key={`r-${lane.key}`} className={s.lane}>
        <div className={s.past} style={{ width: `${todayPct}%` }} />
        {ticks.map(t => <div key={t} className={s.grid} style={{ left: `${dayPct(t)}%` }} />)}
        <div className={s.today} style={{ left: `${todayPct}%` }} />
        {laneChanges.map((c, i) => {
          const x1 = pct(c.windowStart!);
          const x2 = pct(c.windowEnd!);
          const next = laneChanges[i + 1];
          const room = (next ? pct(next.windowStart!) : 100) - x2;
          const kind =
            c.status.meaning === 'completed' ? s.wClosed
              : c.status.meaning === 'draft' ? s.wDraft
                : c.authorized ? s.wAuthorized : s.wPending;
          return (
            <span key={c.changeRequestId}>
              <Tooltip relationship="description" withArrow content={<ChangeTip change={c} stages={props.stages} />}>
                <span
                  tabIndex={0}
                  role="img"
                  aria-label={`Change window: ${c.title}, ${fmtWindow(c.windowStart, c.windowEnd)}, ${c.status.label}`}
                  className={mergeClasses(s.window, kind, c.changeType.key === 'emergency' && s.wEmergency)}
                  style={{ left: `${x1}%`, width: `${Math.max(0, x2 - x1)}%` }}
                />
              </Tooltip>
              {Date.parse(c.windowEnd!) >= todayStart && room > 7 && (
                <span className={s.windowText} style={{ left: `calc(${x2}% + 14px)`, maxWidth: `calc(${room}% - 22px)` }}>{c.title}</span>
              )}
            </span>
          );
        })}
        {laneDeploys.map(d => {
          const flagged = activeFlags(d.flags).length > 0;
          const m = d.result.meaning;
          const kind = m === 'succeeded' ? s.mSucceeded : m === 'partly-succeeded' ? s.mPartly : m === 'failed' ? s.mFailed : s.mOther;
          return (
            <Tooltip key={d.deploymentRecordId} relationship="description" withArrow content={<DeployTip deployment={d} routes={props.routes} />}>
              <span
                tabIndex={0}
                role="img"
                aria-label={`Deployment: ${d.applicationName} ${d.artifactVersion}, ${d.result.label}, ${fmtDateTime(d.finishedOn)}${flagged ? ', flagged' : ''}`}
                className={mergeClasses(s.marker, kind, flagged && s.mFlagged)}
                style={{ left: `${pct(d.finishedOn)}%` }}
              />
            </Tooltip>
          );
        })}
      </div>,
    );
  }

  return (
    <div className={s.chart} role="group" aria-label="Timeline chart. The lists below hold the same items.">
      <div className={s.axisLabel} />
      <div className={s.axis}>
        {ticks.filter(t => Math.abs(dayPct(t) - todayPct) > 6).map(t => <span key={t} className={s.tick} style={{ left: `${dayPct(t)}%` }}>{fmtDayMonth(t)}</span>)}
        <span className={s.todayPill} style={{ left: `${todayPct}%` }}>Today, {fmtDayMonth(props.today)}</span>
      </div>
      {rows}
    </div>
  );
}

function ChangeTip({ change, stages }: { change: ScheduledChange; stages: Labels }) {
  const s = useStyles();
  return (
    <div className={s.tip}>
      <span className={s.tipTitle}>{change.title}</span>
      <span>{change.applicationName} to {change.target ? `${change.target.label} (${stages.label(change.target.targetStageKey)})` : 'no target yet'}</span>
      <span>{fmtWindow(change.windowStart, change.windowEnd)}</span>
      <span>{change.changeType.label} change, {change.status.label.toLowerCase()}. {authorizationText(change)}.</span>
    </div>
  );
}

function DeployTip({ deployment, routes }: { deployment: DeploymentItem; routes: Labels }) {
  const s = useStyles();
  const flags = activeFlags(deployment.flags);
  return (
    <div className={s.tip}>
      <span className={s.tipTitle}>{deployment.applicationName} {deployment.artifactVersion}</span>
      <span>{deployment.result.label}, {fmtDateTime(deployment.finishedOn)}</span>
      <span>Recorded by: {routes.label(deployment.route.key)}</span>
      {flags.length > 0 && <span>Flags: {flags.map(f => flagText[f]).join(', ')}</span>}
    </div>
  );
}

function Legend() {
  const s = useStyles();
  const bar = (style: CSSProperties) => <span className={s.legendBar} style={style} aria-hidden />;
  const dot = (style: CSSProperties) => <span className={s.legendDot} style={style} aria-hidden />;
  return (
    <div className={s.legend} role="group" aria-label="Legend">
      <span className={s.legendItem}>{bar({ backgroundColor: palette.blue })}Authorized window</span>
      <span className={s.legendItem}>{bar({ border: `2px solid ${palette.blue}`, backgroundColor: palette.white })}Not authorized yet</span>
      <span className={s.legendItem}>{bar({ border: `2px solid ${palette.muted}`, backgroundColor: palette.white })}Draft</span>
      <span className={s.legendItem}>{bar({ backgroundColor: '#a9c1dc' })}Closed</span>
      <span className={s.legendItem}>{bar({ backgroundColor: palette.blue, boxShadow: `0 0 0 2px ${DANGER}` })}Emergency</span>
      <span className={s.legendItem}>{dot({ backgroundColor: palette.navy })}Succeeded</span>
      <span className={s.legendItem}>{dot({ backgroundColor: AMBER })}Partly succeeded</span>
      <span className={s.legendItem}>{dot({ backgroundColor: DANGER })}Failed</span>
      <span className={s.legendItem}>{dot({ backgroundColor: palette.navy, boxShadow: `0 0 0 3px ${palette.gold}` })}Flagged</span>
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
  const s = useStyles();
  const flags = activeFlags(deployment.flags);
  if (!flags.length) return null;
  return (
    <span className={s.flags}>
      {flags.map(f => (
        <Tooltip key={f} content={flagHelp[f]} relationship="description" withArrow>
          <span tabIndex={0}><ToneBadge tone="warning">{flagText[f]}</ToneBadge></span>
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

function Agenda(props: { changes: ScheduledChange[]; deployments: DeploymentItem[]; today: string; stages: Labels; routes: Labels }) {
  const s = useStyles();
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
      out.push(<div key="today" className={s.todayRule}>Today, {fmtDayMonth(props.today)}</div>);
      todayShown = true;
    }
    if (d !== day) {
      flush();
      day = d;
      out.push(<h3 key={`h-${d}`} className={s.dayHead}>{fmtWeekday(d)}</h3>);
    }
    if (item.kind === 'change') {
      const c = item.change;
      group.push(
        <Row
          key={c.changeRequestId}
          lead={<span className={s.agendaIcon} aria-hidden><CalendarLtr20Regular /></span>}
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
          lead={<span className={mergeClasses(s.agendaIcon, s.agendaIconDeploy)} aria-hidden><Rocket20Regular /></span>}
          primary={`${dep.applicationName ?? 'Unknown application'} ${dep.artifactVersion}`}
          secondary={`Deployed to ${dep.targetLabel ?? 'an unknown target'} at ${fmtTime(dep.finishedOn)} ${zoneLabel()}`}
          tertiary={`Recorded by: ${props.routes.label(dep.route.key)}`}
          badges={<><StatusBadge status={dep.result} /><FlagBadges deployment={dep} /></>}
        />,
      );
    }
  }
  flush();
  if (!todayShown) out.push(<div key="today" className={s.todayRule}>Today, {fmtDayMonth(props.today)}</div>);
  return (
    <>
      <Muted>{plural(items.length, 'item', 'items')}, oldest first. Times in {zoneLabel()}.</Muted>
      {out}
    </>
  );
}
