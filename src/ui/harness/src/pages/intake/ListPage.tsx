// Intake and demand (/app/intake?view=triage|demand|mine|drafts|all&q=<text>): the triage queue for the platform admin,
// the one demand list for the PMO, and "my requests" and drafts for each requester. Reads list_intake_requests (one
// page of rows, and the count of each view for the tiles) and whoami (the first view and the New request action).
// Every number opens rows: a tile opens its view, a demand bar narrows the list below it. The demand view orders and
// filters by score, priority, size band and horizon on the server (list_intake_requests orderBy and filters), and the
// PMO scores a request from its row (score_intake_request).
import { Plus, Search, TriangleAlert, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { RouteOf, IntakeView } from '../../app/routes';
import { goTo, hrefTo } from '../../app/links';
import type { IntakeList, IntakeOrderBy, IntakeSummary, ListIntakeRequestsInput } from '../../data/contracts';
import { useOutcomes } from '../../data/labels';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { ScoreSheet } from './sheets';
import { useSizeBandOptions } from './shared';
import { todayInZone } from '../../data/format';
import { useLabels } from '../../data/labels';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { BarList, DataStamp, Empty, KpiTile, MoreRowsNote, PageHeader, Panel, StatusBadge, SwitchTabs, TextLink, ToneBadge, type BarItem } from '../../components/blocks';
import { AiMark } from '../../components/stamps';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import {
  aiStampOf,
  approvalsText,
  bucketsOf,
  daysSinceSaved,
  defaultIntakeView,
  demandValue,
  isRequesterOf,
  priorityOf,
  type DemandDimension,
} from './logic';
import { mayCall, meOf, RolesUnreadNote, useDebounced, useWhoami, type Whoami } from './shared';

const VIEW_TEXT: Record<IntakeView, { tab: string; title: string; subtitle: string; empty: string }> = {
  triage: {
    tab: 'Triage',
    title: 'Waiting for triage',
    subtitle: 'Submitted requests waiting for the intake decision, newest first.',
    empty: 'No request waits for triage.',
  },
  demand: {
    tab: 'Demand',
    title: 'Demand',
    subtitle: 'Requests on the table (submitted, in progress or completed), the highest score first.',
    empty: 'No request is on the table.',
  },
  mine: {
    tab: 'My requests',
    title: 'My requests',
    subtitle: 'Requests you made, or an AI client drafted for you.',
    empty: 'You have no intake requests yet.',
  },
  drafts: {
    tab: 'Drafts',
    title: 'Drafts',
    subtitle: 'Requests not submitted yet, with the days since they were last saved.',
    empty: 'No draft waits.',
  },
  all: {
    tab: 'All',
    title: 'All requests',
    subtitle: 'Every intake request you may read.',
    empty: 'No intake request you may read.',
  },
};

const VIEW_ORDER: IntakeView[] = ['triage', 'demand', 'mine', 'drafts', 'all'];

const DIMENSION_TEXT: Record<DemandDimension, string> = {
  horizon: 'Horizon',
  sizeBand: 'Size band',
  suggestedTier: 'Suggested tier',
  status: 'Status',
};

interface Drill {
  dimension: DemandDimension;
  key: string | null;
  label: string;
}

export function IntakeListPage({ route }: { route: RouteOf<'intake'> }) {
  const who = useWhoami();
  const me = meOf(who);
  // The address names the view; with none, the person's roles choose it once they are known.
  const view: IntakeView | null = route.view ?? (who.status === 'loading' ? null : defaultIntakeView(me?.roleKeys ?? []));
  const text = route.text;
  const withText = (input: ListIntakeRequestsInput): ListIntakeRequestsInput => (text ? { ...input, text } : input);

  const triage = useTool('list_intake_requests', withText({ view: 'triage', top: 1 }));
  const demand = useTool('list_intake_requests', withText({ view: 'demand', top: 1 }));
  const mine = useTool('list_intake_requests', withText({ view: 'mine', top: 1 }));
  const drafts = useTool('list_intake_requests', withText({ view: 'drafts', top: 1 }));
  // The demand view's order and filters, sent to the server so the order and the filter hold beyond the first page.
  const [demandQuery, setDemandQuery] = useState<DemandQuery>(DEMAND_DEFAULT);
  const list = useTool('list_intake_requests', view ? withText({ view, top: 100, ...(view === 'demand' ? demandInput(demandQuery) : {}) }) : null);
  const canScore = mayCall(who, 'score_intake_request');

  // The search box writes the address (q), so the list, the tiles and a shared link agree.
  const routeRef = useRef(route);
  routeRef.current = route;
  const [query, setQuery] = useState(route.text ?? '');
  const typed = useDebounced(query.trim(), 400);
  useEffect(() => {
    const r = routeRef.current;
    if (typed !== (r.text ?? '')) goTo({ page: 'intake', view: r.view, text: typed || undefined }, true);
  }, [typed]);
  useEffect(() => {
    setQuery(q => (q.trim() === (route.text ?? '') ? q : route.text ?? ''));
  }, [route.text]);

  // A demand bar narrows the rows of the loaded page. A new view or search starts again.
  const [drill, setDrill] = useState<Drill | null>(null);
  useEffect(() => setDrill(null), [view, text]);

  const showView = (next: IntakeView) => goTo({ page: 'intake', view: next, text: route.text }, true);
  const canStart = mayCall(who, 'save_intake_draft');
  const listEnvelope = list.status === 'done' ? list.envelope : undefined;
  const texts = view ? VIEW_TEXT[view] : null;
  const isAdmin = !!me?.roleKeys.includes('platform-admin');

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Intake and demand"
        subtitle="Requests for new applications and for changes to existing ones, from the first draft to the intake decision."
        meta={<DataStamp envelope={listEnvelope} />}
        actions={canStart ? (
          <Button asChild>
            <a href={hrefTo({ page: 'intake-new' })}><Plus aria-hidden />New request</a>
          </Button>
        ) : undefined}
      />

      <div className="mb-4 grid grid-cols-4 gap-3 medium:grid-cols-2 narrow:gap-2.5" role="group" aria-label="Requests by view">
        <CountTile label="Waiting for triage" state={triage} caption="Submitted, no intake decision yet" attention={isAdmin} onClick={() => showView('triage')} />
        <CountTile label="On the table" state={demand} caption="Submitted, in progress or completed" onClick={() => showView('demand')} />
        <CountTile label="My requests" state={mine} caption="Made by you or drafted for you" onClick={() => showView('mine')} />
        <CountTile label="Drafts" state={drafts} caption="Not submitted yet" onClick={() => showView('drafts')} />
      </div>

      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        {view && (
          <SwitchTabs
            label="View"
            ariaLabel="Which requests"
            value={view}
            onChange={showView}
            options={VIEW_ORDER.map(v => ({ value: v, label: VIEW_TEXT[v].tab }))}
          />
        )}
        <div className="flex min-w-[260px] flex-col gap-1 narrow:w-full narrow:min-w-0">
          <label htmlFor="intake-search" className="text-xs text-navy-3">Find a request</label>
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
            <Input
              id="intake-search"
              type="search"
              className="bg-card pr-9 pl-8"
              placeholder="Title, need or IR number"
              value={query}
              maxLength={200}
              onChange={e => setQuery(e.target.value)}
            />
            {query && (
              <button
                type="button"
                className="absolute top-1/2 right-1.5 grid size-6 -translate-y-1/2 cursor-pointer place-items-center rounded-sm border-0 bg-transparent text-navy-3 hover:text-navy focus-visible:outline-2 focus-visible:outline-ring"
                onClick={() => setQuery('')}
                aria-label="Clear the search"
              >
                <X className="size-4" aria-hidden />
              </button>
            )}
          </div>
        </div>
      </div>

      {view === 'demand' && <DemandControls value={demandQuery} onChange={setDemandQuery} />}

      {view === 'demand' && list.status === 'done' && list.envelope.ok && (
        <DemandBreakdown
          items={list.envelope.data.items}
          more={!!list.envelope.data.skipToken}
          onDrill={next => {
            setDrill(next);
            // The rows sit below the bars: bring them into view, so a phone shows what the bar opened.
            window.requestAnimationFrame(() => document.getElementById('intake-list')?.scrollIntoView({ block: 'start' }));
          }}
        />
      )}

      <Panel
        title={texts?.title ?? 'Requests'}
        subtitle={texts?.subtitle}
        count={dataOf(list)?.totalCount}
        state={view ? list : undefined}
        id="intake-list"
        skeletonRows={5}
        actions={drill ? (
          <Button variant="outline" size="sm" onClick={() => setDrill(null)} aria-label={`${DIMENSION_TEXT[drill.dimension]}: ${drill.label}. Clear this filter and show every row`}>
            <X aria-hidden />
            {DIMENSION_TEXT[drill.dimension]}: {drill.label}
          </Button>
        ) : undefined}
      >
        {view ? (data => {
          const items = drill ? data.items.filter(i => (demandValue(i, drill.dimension)?.key ?? null) === drill.key) : data.items;
          return (
            <>
              <RolesUnreadNote state={who} />
              {items.length ? (
                <RowList label={texts?.title}>
                  {items.map(item => <IntakeRow key={item.intakeRequestId} item={item} view={view} me={me} canScore={canScore} />)}
                </RowList>
              ) : (
                <Empty>
                  {text ? `No request matches "${text}" in this view.` : texts?.empty}
                  {!text && view === 'mine' && canStart && <> <TextLink href={hrefTo({ page: 'intake-new' })}>Start a new request</TextLink>.</>}
                </Empty>
              )}
              {drill && <p className="m-0 text-xs text-navy-3">Showing {items.length} of the {data.items.length} rows on this page with {DIMENSION_TEXT[drill.dimension].toLowerCase()} {drill.label}.</p>}
              <MoreRowsNote skipToken={data.skipToken} what="requests" />
            </>
          );
        }) : <Empty>Reading your roles to choose the view.</Empty>}
      </Panel>
    </>
  );
}

// A view's count as a tile that opens the view. A count that could not be read says so, and never shows a number.
function CountTile(props: { label: string; state: ToolState<IntakeList>; caption: string; attention?: boolean; onClick: () => void }) {
  const { state } = props;
  if (state.status === 'done' && !state.envelope.ok) {
    return (
      <div className="flex min-w-0 flex-col gap-0.5 rounded-xl border bg-card py-3.5 pr-4 pl-[18px] shadow-xs">
        <span className="text-sm font-semibold text-navy-2">{props.label}</span>
        <span className="flex items-center gap-1.5 py-2 text-sm text-navy-2"><TriangleAlert className="size-4 shrink-0 text-warning" aria-hidden />Could not be read</span>
        <span className="text-xs leading-4 text-navy-3">Reference {state.envelope.correlationId}</span>
      </div>
    );
  }
  const value = dataOf(state)?.totalCount ?? null;
  return <KpiTile label={props.label} value={value} caption={props.caption} attention={!!props.attention && !!value} onClick={props.onClick} />;
}

function IntakeRow({ item, view, me, canScore }: { item: IntakeSummary; view: IntakeView; me: Whoami | null; canScore: boolean }) {
  const href = hrefTo({ page: 'intake-request', id: item.intakeRequestId });
  const mineDraft = item.status.meaning === 'draft' && isRequesterOf(item, me?.objectId) && !!me?.tools.includes('save_intake_draft');
  const age = item.status.meaning === 'draft' ? daysSinceSaved(item.performedBy, todayInZone()) : null;
  const secondary = [
    item.requester ? `Requested by ${item.requester.displayLabel || item.requester.objectId}` : 'No requester recorded',
    item.proposedKind?.label,
    item.usesAi === true ? 'Uses AI' : null,
    item.applicationName ? `About ${item.applicationName}` : null,
  ].filter(Boolean).join(' · ');
  let tertiary: string;
  if (view === 'demand') {
    const priority = priorityOf(item);
    tertiary = [
      `Score ${item.score ?? 'not set'}`,
      `Horizon ${item.horizon?.label ?? 'not set'}`,
      `Size ${item.sizeBand?.label ?? 'not set'}`,
      `Priority ${priority ? `${priority.status.label.toLowerCase()}${priority.outcome ? `, ${priority.outcome.label}` : ''}` : 'not decided'}`,
    ].join(' · ');
  } else if (item.status.meaning === 'draft') {
    tertiary = age === null ? 'Draft. Last save not recorded.' : `Draft. Last saved ${age === 0 ? 'today' : age === 1 ? '1 day ago' : `${age} days ago`}.`;
  } else if (item.triageDecision) {
    const d = item.triageDecision;
    const approvals = approvalsText(d.approvalsSummary);
    tertiary = `Intake decision ${d.status.label.toLowerCase()}${d.outcome ? `: ${d.outcome.label}` : ''}.${approvals ? ` ${approvals}.` : ''}`;
  } else {
    tertiary = 'No intake decision yet.';
  }
  return (
    <Row
      primary={<TextLink href={href}>{item.recordNumber ? `${item.recordNumber}: ` : ''}{item.title || 'Untitled request'}</TextLink>}
      secondary={secondary}
      tertiary={tertiary}
      badges={(item.isAiCreated || item.suggestedTier || item.duplicateOfId) ? (
        <>
          <AiMark isAiCreated={item.isAiCreated} performedBy={aiStampOf(item.performedBy)} />
          {item.suggestedTier && <ToneBadge tone="subtle" icon={null}>Suggested {item.suggestedTier.label}</ToneBadge>}
          {item.duplicateOfId && <ToneBadge tone="warning">Marked as a duplicate</ToneBadge>}
        </>
      ) : undefined}
      end={<StatusBadge status={item.status} />}
      endSub={mineDraft
        ? <TextLink href={hrefTo({ page: 'intake-edit', id: item.intakeRequestId })} aria-label={`Continue the draft ${item.title}`}>Continue</TextLink>
        : view === 'demand' && canScore
          ? <ScoreSheet intakeRequestId={item.intakeRequestId} title={item.title} score={item.score} sizeBandKey={item.sizeBand?.key ?? null} horizonKey={item.horizon?.key ?? null} />
          : undefined}
      wideEnd
    />
  );
}

// ---------- the demand view's order and filters ----------

interface DemandQuery {
  orderBy: IntakeOrderBy;
  sizeBand: string;
  priority: string;
  scored: '' | 'yes' | 'no';
}

const DEMAND_DEFAULT: DemandQuery = { orderBy: 'score', sizeBand: '', priority: '', scored: '' };
const ANY = 'any';

const ORDER_TEXT: Record<IntakeOrderBy, string> = {
  score: 'Score, highest first',
  priority: 'Priority',
  'size-band': 'Size band',
  horizon: 'Horizon',
  recent: 'Last changed',
  title: 'Title',
};

function demandInput(q: DemandQuery): ListIntakeRequestsInput {
  return {
    orderBy: q.orderBy,
    ...(q.sizeBand ? { sizeBandKeys: [q.sizeBand] } : {}),
    ...(q.priority ? { priorityKeys: [q.priority] } : {}),
    ...(q.scored ? { scored: q.scored === 'yes' } : {}),
  };
}

function DemandControls({ value, onChange }: { value: DemandQuery; onChange: (next: DemandQuery) => void }) {
  const sizes = useSizeBandOptions();
  const priorities = useOutcomes('priority');
  const set = <K extends keyof DemandQuery>(key: K, next: DemandQuery[K]) => onChange({ ...value, [key]: next });
  const changed = JSON.stringify(value) !== JSON.stringify(DEMAND_DEFAULT);
  const control = (id: string, label: string, current: string, options: { value: string; label: string }[], apply: (v: string) => void) => (
    <div className="flex min-w-[170px] flex-col gap-1 narrow:min-w-0">
      <label htmlFor={id} className="text-xs text-navy-3">{label}</label>
      <Select value={current || ANY} onValueChange={v => apply(v === ANY ? '' : v)}>
        <SelectTrigger id={id} className="w-full bg-card"><SelectValue /></SelectTrigger>
        <SelectContent>
          {options.map(o => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  );
  return (
    <div className="mb-4 flex flex-wrap items-end gap-3 narrow:grid narrow:grid-cols-2" role="group" aria-label="Order and filter the demand">
      {control('demand-order', 'Order by', value.orderBy, (Object.keys(ORDER_TEXT) as IntakeOrderBy[]).map(k => ({ value: k, label: ORDER_TEXT[k] })), v => set('orderBy', (v || 'score') as IntakeOrderBy))}
      {control('demand-size', 'Size band', value.sizeBand, [
        { value: ANY, label: 'Any size' },
        ...(sizes ?? []).map(o => ({ value: o.key, label: o.label })),
        { value: 'none', label: 'Not set' },
      ], v => set('sizeBand', v))}
      {control('demand-priority', 'Priority', value.priority, [
        { value: ANY, label: 'Any priority' },
        ...priorities.options.map(o => ({ value: o.key, label: `Decided: ${o.label}` })),
        { value: 'proposed', label: 'Proposed, waiting' },
        { value: 'none', label: 'Not proposed' },
      ], v => set('priority', v))}
      {control('demand-scored', 'Score', value.scored, [
        { value: ANY, label: 'Scored or not' },
        { value: 'yes', label: 'Scored' },
        { value: 'no', label: 'Not scored yet' },
      ], v => set('scored', v as DemandQuery['scored']))}
      {changed && (
        <Button variant="outline" size="sm" onClick={() => onChange(DEMAND_DEFAULT)}>
          <X aria-hidden />
          Clear
        </Button>
      )}
    </div>
  );
}

// The demand on the loaded page by horizon, size band, suggested tier and status. A bar narrows the list to its rows.
function DemandBreakdown({ items, more, onDrill }: { items: IntakeSummary[]; more: boolean; onDrill: (drill: Drill) => void }) {
  const tiers = useLabels('tier');
  const horizonOrder = (key: string) => ['now', 'next', 'later'].indexOf(key) + 1 || 99;
  const groups = useMemo(() => ([
    { dimension: 'horizon' as const, title: 'By horizon', buckets: bucketsOf(items, i => demandValue(i, 'horizon'), horizonOrder) },
    { dimension: 'sizeBand' as const, title: 'By size band', buckets: bucketsOf(items, i => demandValue(i, 'sizeBand')) },
    { dimension: 'suggestedTier' as const, title: 'By suggested tier', buckets: bucketsOf(items, i => demandValue(i, 'suggestedTier'), k => tiers.rank(k)) },
    { dimension: 'status' as const, title: 'By status', buckets: bucketsOf(items, i => demandValue(i, 'status')) },
  ]), [items, tiers]);
  if (!items.length) return null;
  return (
    <div className="mb-4 grid grid-cols-4 gap-3 medium:grid-cols-2 narrow:grid-cols-1 narrow:gap-2.5">
      {groups.map(g => (
        <Panel key={g.dimension} title={g.title} subtitle={more ? 'The first 100 requests' : `${items.length} requests`}>
          <BarList
            items={g.buckets.map<BarItem>(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }))}
            noun={['request', 'requests']}
            labelWidth={96}
            ariaLabel={`Demand ${g.title.toLowerCase()}. Each bar shows its requests in the list below.`}
            onDrill={item => onDrill({ dimension: g.dimension, key: item.key, label: item.label })}
          />
        </Panel>
      ))}
    </div>
  );
}
