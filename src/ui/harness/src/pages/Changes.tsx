// Changes (D-36: the web app acts; the engine checks every write).
//   /app/changes?view=schedule|list&app=<id>      the schedule (two weeks, or a month) and the list with its filters
//   /app/changes/new?app=<id>                     a new change: choose the application, plan it, check and submit
//   /app/records/change-request/<id>              one change from its draft to its result and post-change review
// Reads: get_change_schedule, get_change_request, check_standard_change, get_approval_status, get_requirements,
// get_dependency_impact, get_deployments, list_approvals, list_reviews, get_decision_log, get_my_queue,
// list_owner_assignments, find_applications, get_application_card, whoami.
// Writes, each in the shared side sheet: save_change_request_draft, submit_record, withdraw_record, record_change_result,
// save_link. An approval is answered on the decision page only (D-38); this page links there.
// Wave 4 (D-41): the list's filters live in the address (status=, type=, auth=, timing=, range=), with the text search
// q= over the title, application, target, type and status, so Back keeps them; the figures and the schedule follow the
// search, so each figure opens the rows it counts. "Your change drafts" follows the application filter (wave 3 QA: it
// ignored it).
import { ArrowRight, CalendarDays, ChevronLeft, ChevronRight, CircleCheck, CircleX, Clock, Archive, PencilLine, Plus, Search, Siren, X } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { goTo, hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import type { RouteOf } from '../app/routes';
import type { ApplicationCard, ChangeRequestDetail, RecordStatusMeaning, ScheduledChange } from '../data/contracts';
import type { ToolState } from '../data/toolClient';
import { addDays, dayKey, fmtDate, fmtDateTime, fmtTimeRange, fmtWeekday, fmtWindow, plural, utcDayRange, zoneLabel } from '../data/format';
import { useLabels, useListValues, type Labels } from '../data/labels';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { DataStamp, Empty, KpiTile, MoreRowsNote, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Input } from '../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import { ChangeTypeBadge } from './Application360';
import { BeforeSubmit, CompleteChangeSheet, RecordResultSheet, ResultSummary, SubmitChangeSheet, useApprovalPreview, useTargetOf, WithdrawChangeSheet } from './changes-records/ChangeActions';
import { ChangeDraftSheet } from './changes-records/ChangeDraftSheet';
import {
  activeFilterCount,
  addMonths,
  AUTH_PARAM,
  changeFigures,
  changeFilterParams,
  changeText,
  durationText,
  filterChanges,
  firstOfMonth,
  isClosed,
  monthWeeks,
  NO_FILTER,
  sameId,
  samePrincipal,
  STATUS_PARAM,
  TIMING_PARAM,
  windowMinutes,
  withAppHint,
  type ChangeFigure,
  type ChangeFilter,
  type StatusFilter,
} from './changes-records/logic';
import { ApprovalNeededList, ApprovalStatusView, column, DecisionsAbout, Fact, Facts, FlagBadges, LinksList, plainText, recordGrid, SaveLinkSheet, SideFailure, subhead } from './changes-records/parts';
import { canUse, myObjectId, usePagedRead, useWhoAmI } from './changes-records/reads';

// ---------------------------------------------------------------------------------------------------------------------
// Shared bits
// ---------------------------------------------------------------------------------------------------------------------

function changeHref(id: string): string | null {
  return recordHrefOf('change-request', id);
}

function appHref(id: string | null | undefined): string | null {
  return id ? recordHrefOf('business-application', id) : null;
}

// How a change stands on authorization, in words and a tone. Status is never shown by color alone. A rejected
// authorization says so: the rejected decision changes nothing on the change, so its status stays submitted.
function authorizationOf(c: { authorized: boolean; authorizationStatus?: { meaning: string } | null; changeType: { key: string }; status: { meaning: string } }): { text: string; tone: 'success' | 'warning' | 'subtle' | 'danger' } {
  if (c.authorized && c.changeType.key === 'standard') return { text: 'Pre-authorized', tone: 'success' };
  if (c.authorized) return { text: 'Authorized', tone: 'success' };
  if (c.authorizationStatus?.meaning === 'rejected') return { text: 'Authorization rejected', tone: 'danger' };
  if (c.status.meaning === 'draft') return { text: 'Not submitted', tone: 'subtle' };
  if (isClosed(c.status.meaning as RecordStatusMeaning)) return { text: 'Not authorized', tone: 'subtle' };
  return { text: 'Not authorized yet', tone: 'warning' };
}

function AuthorizationBadge({ change }: { change: ScheduledChange }) {
  const a = authorizationOf(change);
  return <ToneBadge tone={a.tone} icon={a.tone === 'warning' ? <Clock aria-hidden /> : undefined}>{a.text}</ToneBadge>;
}

// On the change record, the authorization decision's own status wins: a rejected or withdrawn authorization says so.
// An accepted authorization also moves the change to in progress (the update-subject effect); a rejected one changes
// nothing but the decision.
function RecordAuthorizationBadge({ authorization, scheduled }: { authorization: ChangeRequestDetail['authorization']; scheduled: ScheduledChange | null | undefined }) {
  const meaning = authorization.status?.meaning;
  if (meaning === 'rejected') return <ToneBadge tone="danger">Authorization rejected</ToneBadge>;
  if (meaning === 'withdrawn') return <ToneBadge tone="subtle" icon={null}>Authorization withdrawn</ToneBadge>;
  if (meaning === 'accepted') return <ToneBadge tone="success">Authorized</ToneBadge>;
  return scheduled ? <AuthorizationBadge change={scheduled} /> : null;
}

function targetText(change: ScheduledChange, stages: Labels): string {
  return change.target ? `${change.target.label} (${stages.label(change.target.targetStageKey).toLowerCase()})` : 'no target yet';
}

const STATUS_CHOICES: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'Every status' },
  { value: 'open', label: 'Open (not closed)' },
  { value: 'draft', label: 'Draft' },
  { value: 'submitted', label: 'Submitted' },
  { value: 'in-progress', label: 'In progress' },
  { value: 'completed', label: 'Completed' },
  { value: 'rejected', label: 'Rejected' },
  { value: 'withdrawn', label: 'Withdrawn' },
  { value: 'cancelled', label: 'Canceled' },
];

const FIGURE_TEXT: Record<ChangeFigure['key'], { label: string; caption: string; attention?: boolean }> = {
  waiting: { label: 'Waiting for authorization', caption: 'Submitted, not authorized yet', attention: true },
  coming: { label: 'Authorized, coming up', caption: 'Window still to come' },
  // The in progress meaning holds every authorized change not closed yet, scheduled ones too (Balanced labels it
  // Scheduled), so the caption says so (QA 2026-11-24).
  'in-progress': { label: 'Scheduled or under way', caption: 'Authorized, not closed yet' },
  emergency: { label: 'Emergency changes', caption: 'Not closed yet', attention: true },
  drafts: { label: 'Drafts', caption: 'With a window, not submitted' },
};

type ListRange = 'around' | 'coming' | 'past';
const LIST_RANGES: Record<ListRange, { label: string; back: number; ahead: number }> = {
  around: { label: '30 back, 60 ahead', back: 30, ahead: 60 },
  coming: { label: 'Next 90 days', back: 0, ahead: 90 },
  past: { label: 'Last 90 days', back: 90, ahead: 0 },
};

// ---------------------------------------------------------------------------------------------------------------------
// The list and the schedule
// ---------------------------------------------------------------------------------------------------------------------

export function Changes({ route }: { route: RouteOf<'changes'> }) {
  const host = useHost();
  const narrow = useNarrow();
  const who = useWhoAmI();
  const stages = useLabels('target-stage');
  const types = useLabels('change-type');
  const view = route.view ?? 'schedule';
  const appId = route.applicationId;
  const today = host.today;
  const nowMs = Date.now();

  const statusParam = useQueryParam('status', STATUS_PARAM) as StatusFilter | undefined;
  const typeParam = useQueryParam('type', /^[a-z][a-z0-9-]{0,63}$/);
  const authParam = useQueryParam('auth', AUTH_PARAM) as ChangeFilter['authorization'] | undefined;
  const timingParam = useQueryParam('timing', TIMING_PARAM) as ChangeFilter['timing'] | undefined;
  const range = (useQueryParam('range', /^(coming|past)$/) as ListRange | undefined) ?? 'around';
  const text = useListText();
  const filter: ChangeFilter = { status: statusParam ?? 'all', type: typeParam ?? 'all', authorization: authParam ?? 'all', timing: timingParam ?? 'all' };
  const pageParams = { ...changeFilterParams(filter), range: range === 'around' ? undefined : range, q: text };
  const [span, setSpan] = useState<'two-weeks' | 'month'>('two-weeks');
  const [month, setMonth] = useState(firstOfMonth(today));

  const r = LIST_RANGES[range];
  const listFrom = addDays(today, -r.back);
  const listTo = addDays(today, r.ahead);
  const scoped = (input: Record<string, unknown>) => (appId ? { ...input, businessApplicationId: appId } : input);
  // The list feeds the figures in both views.
  const list = usePagedRead('get_change_schedule', scoped({ ...utcDayRange(listFrom, listTo), top: 100 }), d => d.items);
  const listItems = filterByText(list.status === 'done' ? list.items : [], text, changeText) as ScheduledChange[];
  const shown = filterChanges(listItems, filter, nowMs);
  const figures = changeFigures(listItems, nowMs);

  const weeks = monthWeeks(month);
  const scheduleFrom = span === 'month' ? weeks[0][0] : today;
  const scheduleTo = span === 'month' ? weeks[weeks.length - 1][6] : addDays(today, 13);
  const schedule = useTool('get_change_schedule', view === 'schedule' ? scoped({ ...utcDayRange(scheduleFrom, scheduleTo), top: 100 }) : null);

  const appCard = useTool('get_application_card', appId ? { businessApplicationId: appId, sections: ['links'] } : null);
  const appName = dataOf(appCard)?.application.name ?? listItems.find(c => sameId(c.businessApplicationId, appId))?.applicationName ?? null;
  const drafts = useTool('get_my_queue', { kinds: ['draft'], top: 50 });
  // The person's change drafts, under the application filter and the search, as the list is.
  const myDrafts = filterByText(
    (dataOf(drafts)?.items ?? []).filter(i => i.subjectKind === 'change-request' && (!appId || sameId(i.businessApplicationId, appId))),
    text,
    i => [i.title, i.why],
  );

  // A view or application change keeps the list's filters; a figure sets its own filter and keeps the search.
  const go = (next: { view?: 'schedule' | 'list'; applicationId?: string }, params: Record<string, string | undefined> = pageParams) =>
    navigate(withParams(hrefTo({ page: 'changes', ...(next.view ? { view: next.view } : {}), ...(next.applicationId ? { applicationId: next.applicationId } : {}) }), params), true);
  const setFilter = (f: ChangeFilter) => go({ view, applicationId: appId }, { ...pageParams, ...changeFilterParams(f) });
  const setRange = (r: ListRange) => go({ view, applicationId: appId }, { ...pageParams, range: r === 'around' ? undefined : r });
  const drill = (f: ChangeFilter) => go({ view: 'list', applicationId: appId }, { ...pageParams, ...changeFilterParams(f) });
  const apps = useMemo(() => {
    const seen = new Map<string, string>();
    for (const c of listItems) if (!seen.has(c.businessApplicationId)) seen.set(c.businessApplicationId, c.applicationName);
    return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
  }, [listItems]);

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Changes"
        subtitle="Planned and past changes: their windows, their authorization and how they ended. Open a change to submit it, follow its approvals or record its result."
        meta={<DataStamp envelope={list.status === 'done' ? list.first : undefined} />}
        actions={(
          <>
            {canUse(who, 'save_change_request_draft') && (
              <Button asChild>
                <a href={hrefTo({ page: 'change-new', ...(appId ? { applicationId: appId } : {}) })}><Plus aria-hidden />New change</a>
              </Button>
            )}
            <Button variant="outline" asChild>
              <a href={host.href({ page: 'timeline' })}><CalendarDays aria-hidden />Timeline</a>
            </Button>
          </>
        )}
      />

      <section aria-label="View and application" className="mb-4 flex flex-wrap items-center gap-x-5 gap-y-2.5">
        <SwitchTabs<'schedule' | 'list'>
          label="View"
          ariaLabel="View"
          value={view}
          onChange={v => go({ view: v, applicationId: appId })}
          options={[{ value: 'schedule', label: 'Schedule' }, { value: 'list', label: 'List' }]}
        />
        {appId ? (
          <span className="inline-flex items-center gap-1 rounded-md border border-info-line bg-info-soft py-0.5 pr-1 pl-2 text-xs font-semibold text-brand-dark">
            {`Application: ${appName ?? 'reading the name'}`}
            <button
              type="button"
              onClick={() => go({ view })}
              aria-label="Show every application"
              className="inline-flex size-5 cursor-pointer items-center justify-center rounded-sm border-0 bg-transparent p-0 text-brand-dark hover:bg-card focus-visible:outline-2 focus-visible:outline-ring"
            >
              <X className="size-3.5" aria-hidden />
            </button>
          </span>
        ) : apps.length > 1 && (
          <div className="flex items-center gap-2">
            <span className="text-xs text-navy-3" id="changes-app-label">Application</span>
            <Select value="__all__" onValueChange={v => { if (v !== '__all__') go({ view, applicationId: v }); }}>
              <SelectTrigger className="w-[240px] narrow:w-[200px]" aria-labelledby="changes-app-label">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__all__">Every application</SelectItem>
                {apps.map(a => <SelectItem key={a.id} value={a.id}>{a.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
        )}
        <ListSearch label="Search the changes by title, application, target, type or status" placeholder="Change, application or target" className="narrow:basis-full" />
        {text && <Muted>{`Matching "${text}": the figures, the schedule and the list follow the search.`}</Muted>}
      </section>

      {list.status === 'failed' ? (
        <div className="mb-4"><PanelFailure failure={list.failure} /></div>
      ) : (
        <div className="mb-4 grid grid-cols-5 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5" role="group" aria-label={`Key figures for windows from ${fmtDate(listFrom)} to ${fmtDate(listTo)}`}>
          {figures.map(f => (
            <KpiTile
              key={f.key}
              label={FIGURE_TEXT[f.key].label}
              value={list.status === 'done' ? f.value : null}
              caption={FIGURE_TEXT[f.key].caption}
              attention={!!FIGURE_TEXT[f.key].attention && f.value > 0}
              onClick={() => drill(f.filter)}
            />
          ))}
        </div>
      )}

      {myDrafts.length > 0 && (
        <Panel title="Your change drafts" count={myDrafts.length} subtitle={`${appId ? `For ${appName ?? 'this application'}. ` : ''}Drafts with no window are not in the schedule. Open one to finish it and submit it.`} className="mb-4">
          <RowList label="Your change drafts">
            {myDrafts.map(d => {
              const href = recordHref(d.recordUrl);
              return (
                <Row
                  key={d.subjectId}
                  primary={href ? <TextLink href={href}>{d.title}</TextLink> : d.title}
                  secondary={d.why}
                  badges={<AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />}
                  end={d.status ? <StatusBadge status={d.status} /> : undefined}
                />
              );
            })}
          </RowList>
        </Panel>
      )}

      {view === 'schedule' ? (
        <Panel
          title={span === 'month' ? monthTitle(month) : 'Next 14 days'}
          subtitle={`Change windows by day. Times in ${zoneLabel()}. Read only: no booking and no freeze.`}
          state={schedule}
          skeletonRows={8}
          actions={(
            <div className="flex flex-wrap items-center gap-2">
              {span === 'month' && (
                <span className="flex items-center gap-1">
                  <Button variant="outline" size="icon-sm" aria-label="Previous month" onClick={() => setMonth(m => addMonths(m, -1))}><ChevronLeft aria-hidden /></Button>
                  <Button variant="outline" size="icon-sm" aria-label="Next month" onClick={() => setMonth(m => addMonths(m, 1))}><ChevronRight aria-hidden /></Button>
                </span>
              )}
              <SwitchTabs<'two-weeks' | 'month'>
                label="Span"
                ariaLabel="Span"
                value={span}
                onChange={setSpan}
                options={[{ value: 'two-weeks', label: '14 days' }, { value: 'month', label: 'Month' }]}
              />
            </div>
          )}
        >
          {data => (
            <>
              {span === 'month' && !narrow
                ? <MonthGrid weeks={weeks} month={month} today={today} items={filterByText(data.items, text, changeText) as ScheduledChange[]} />
                : <Agenda items={filterByText(data.items, text, changeText) as ScheduledChange[]} from={scheduleFrom} to={scheduleTo} today={today} stages={stages} />}
              <MoreRowsNote skipToken={data.skipToken} what="change windows" />
              {span === 'month' && !narrow && <CalendarLegend />}
            </>
          )}
        </Panel>
      ) : (
        <Panel
          title="Changes"
          count={list.status === 'done' ? shown.length : undefined}
          subtitle={`Windows ${fmtDate(listFrom)} to ${fmtDate(listTo)}. Changes with no window yet are not in this list.`}
          state={list.status === 'done' ? { status: 'done', envelope: list.first } : list.status === 'loading' ? { status: 'loading' } : undefined}
          skeletonRows={8}
        >
          <ListFilters filter={filter} setFilter={setFilter} range={range} setRange={setRange} types={types} />
          {list.status === 'done' && (
            <>
              {shown.length
                ? (narrow ? <ChangeRows items={shown} stages={stages} /> : <ChangeTable items={shown} stages={stages} />)
                : (
                  <div className="flex flex-col items-start gap-2 py-1">
                    <Empty>{listItems.length ? 'No change matches these filters.' : 'No change has a window in this range.'}</Empty>
                    {(activeFilterCount(filter) > 0 || text) && <Button variant="outline" size="sm" onClick={() => go({ view, applicationId: appId }, { range: pageParams.range })}>Clear the filters</Button>}
                  </div>
                )}
              {list.laterFailure && <SideFailure failure={list.laterFailure} what="More changes" />}
              {list.skipToken && (
                <div className="flex flex-wrap items-center gap-2">
                  <Button variant="outline" size="sm" onClick={list.more} disabled={list.loadingMore}>Show more</Button>
                  <Muted>The figures and filters count the changes read so far.</Muted>
                </div>
              )}
            </>
          )}
        </Panel>
      )}
    </>
  );
}

function monthTitle(month: string): string {
  return new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${month}T00:00:00Z`));
}

function ListFilters(props: { filter: ChangeFilter; setFilter: (f: ChangeFilter) => void; range: ListRange; setRange: (r: ListRange) => void; types: Labels }) {
  const { filter, setFilter } = props;
  const typeChoices = props.types.keys();
  const select = (id: string, label: string, value: string, onChange: (v: string) => void, options: { value: string; label: string }[]) => (
    <div className="flex min-w-0 flex-col gap-1">
      <span className="text-xs text-navy-3" id={id}>{label}</span>
      <Select value={value} onValueChange={onChange}>
        <SelectTrigger className="w-[190px] narrow:w-full" aria-labelledby={id}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {options.map(o => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  );
  return (
    <div className="flex flex-wrap items-end gap-3 border-b border-line pb-3 narrow:grid narrow:grid-cols-2 narrow:gap-2" role="group" aria-label="Filters">
      {select('changes-range', 'Windows', props.range, v => props.setRange(v as ListRange), (Object.keys(LIST_RANGES) as ListRange[]).map(k => ({ value: k, label: LIST_RANGES[k].label })))}
      {select('changes-status', 'Status', filter.status, v => setFilter({ ...filter, status: v as StatusFilter }), STATUS_CHOICES)}
      {select('changes-type', 'Type', filter.type, v => setFilter({ ...filter, type: v }), [{ value: 'all', label: 'Every type' }, ...typeChoices.map(k => ({ value: k, label: props.types.label(k) }))])}
      {select('changes-auth', 'Authorization', filter.authorization, v => setFilter({ ...filter, authorization: v as ChangeFilter['authorization'] }), [
        { value: 'all', label: 'Any' },
        { value: 'authorized', label: 'Authorized' },
        { value: 'not-authorized', label: 'Not authorized' },
      ])}
      {select('changes-timing', 'Window', filter.timing, v => setFilter({ ...filter, timing: v as ChangeFilter['timing'] }), [
        { value: 'all', label: 'Past and coming' },
        { value: 'coming', label: 'Not over yet' },
        { value: 'past', label: 'Over' },
      ])}
      {activeFilterCount(filter) > 0 && (
        <Button variant="link" size="sm" className="h-9 px-1" onClick={() => setFilter(NO_FILTER)}>Clear the filters</Button>
      )}
    </div>
  );
}

function ChangeTable({ items, stages }: { items: ScheduledChange[]; stages: Labels }) {
  return (
    <Table aria-label="Changes">
      <TableHeader>
        <TableRow>
          <TableHead className="w-[32%]">Change</TableHead>
          <TableHead>Window</TableHead>
          <TableHead>Target</TableHead>
          <TableHead>Type</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Authorization</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map(c => {
          const href = changeHref(c.changeRequestId);
          const app = appHref(c.businessApplicationId);
          return (
            <TableRow key={c.changeRequestId} className="align-top">
              <TableCell className="whitespace-normal">
                {href ? <TextLink href={href} className="text-sm">{c.title}</TextLink> : <span className="text-sm font-semibold">{c.title}</span>}
                <div className="mt-1.5 text-xs text-navy-3">{app ? <TextLink href={app} className="inline-flex min-h-6 items-center">{c.applicationName}</TextLink> : c.applicationName}</div>
              </TableCell>
              <TableCell className="text-sm whitespace-normal text-navy">{fmtWindow(c.windowStart, c.windowEnd)}</TableCell>
              <TableCell className="text-sm whitespace-normal text-navy">{c.target ? targetText(c, stages) : 'No target yet'}</TableCell>
              <TableCell><ChangeTypeBadge type={c.changeType} /></TableCell>
              <TableCell><StatusBadge status={c.status} /></TableCell>
              <TableCell><AuthorizationBadge change={c} /></TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

function ChangeRows({ items, stages }: { items: ScheduledChange[]; stages: Labels }) {
  return (
    <RowList label="Changes">
      {items.map(c => {
        const href = changeHref(c.changeRequestId);
        return (
          <Row
            key={c.changeRequestId}
            primary={href ? <TextLink href={href}>{c.title}</TextLink> : c.title}
            secondary={`${c.applicationName} to ${targetText(c, stages)}`}
            tertiary={fmtWindow(c.windowStart, c.windowEnd)}
            badges={<><ChangeTypeBadge type={c.changeType} /><StatusBadge status={c.status} /><AuthorizationBadge change={c} /></>}
          />
        );
      })}
    </RowList>
  );
}

// Day by day, from the first day of the range to the last. A window over several days shows on the day it starts.
function Agenda({ items, from, to, today, stages }: { items: ScheduledChange[]; from: string; to: string; today: string; stages: Labels }) {
  const byDay = new Map<string, ScheduledChange[]>();
  for (const c of items) {
    if (!c.windowStart) continue;
    const day = dayKey(c.windowStart) < from ? from : dayKey(c.windowStart);
    if (day > to) continue;
    byDay.set(day, [...(byDay.get(day) ?? []), c]);
  }
  const days = [...byDay.keys()].sort();
  if (!days.length) return <Empty>No change window in these days.</Empty>;
  return (
    <div className="flex flex-col">
      <Muted>{`${plural(days.reduce((n, d) => n + (byDay.get(d)?.length ?? 0), 0), 'change', 'changes')} on ${plural(days.length, 'day', 'days')}.`}</Muted>
      {days.map(day => (
        <div key={day} className="mt-3.5 flex flex-col gap-0.5 first:mt-2">
          <h3 className={cn(subhead, 'flex items-center gap-2')}>
            {fmtWeekday(day)}
            {day === today && <span className="rounded-full bg-gold px-2 py-px text-[10px] leading-4 font-semibold text-navy normal-case">Today</span>}
          </h3>
          <RowList label={fmtWeekday(day)}>
            {byDay.get(day)!.sort((a, b) => Date.parse(a.windowStart!) - Date.parse(b.windowStart!)).map(c => {
              const href = changeHref(c.changeRequestId);
              const a = authorizationOf(c);
              return (
                <Row
                  key={c.changeRequestId}
                  lead={<StateIcon change={c} />}
                  primary={href ? <TextLink href={href}>{c.title}</TextLink> : c.title}
                  secondary={`${c.applicationName} to ${targetText(c, stages)}`}
                  tertiary={c.windowEnd ? `${fmtTimeRange(c.windowStart!, c.windowEnd)} ${zoneLabel()}. ${a.text}.` : `${a.text}.`}
                  badges={<><ChangeTypeBadge type={c.changeType} /><StatusBadge status={c.status} /></>}
                />
              );
            })}
          </RowList>
        </div>
      ))}
    </div>
  );
}

// The state of a change as an icon with its words, for the calendar and the agenda.
function stateOf(c: ScheduledChange): { text: string; icon: ReactNode; tone: string } {
  if (c.status.meaning === 'draft') return { text: 'Draft', icon: <PencilLine className="size-3.5" aria-hidden />, tone: 'bg-muted text-navy-2' };
  if (isClosed(c.status.meaning as RecordStatusMeaning)) return { text: c.status.label, icon: <Archive className="size-3.5" aria-hidden />, tone: 'bg-subtle-soft text-navy-2' };
  if (c.authorized) return { text: authorizationOf(c).text, icon: <CircleCheck className="size-3.5" aria-hidden />, tone: 'bg-success-soft text-success' };
  if (c.authorizationStatus?.meaning === 'rejected') return { text: 'Authorization rejected', icon: <CircleX className="size-3.5" aria-hidden />, tone: 'bg-danger-soft text-danger' };
  return { text: 'Not authorized yet', icon: <Clock className="size-3.5" aria-hidden />, tone: 'bg-warning-soft text-warning' };
}

function StateIcon({ change }: { change: ScheduledChange }) {
  const s = stateOf(change);
  return <span className={cn('grid size-8 place-items-center rounded-full', s.tone)} aria-hidden>{s.icon}</span>;
}

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function MonthGrid({ weeks, month, today, items }: { weeks: string[][]; month: string; today: string; items: ScheduledChange[] }) {
  const on = (day: string) => items
    .filter(c => c.windowStart && dayKey(c.windowStart) <= day && dayKey(c.windowEnd ?? c.windowStart) >= day)
    .sort((a, b) => Date.parse(a.windowStart!) - Date.parse(b.windowStart!));
  const inMonth = (day: string) => day.slice(0, 7) === month.slice(0, 7);
  return (
    <div role="table" aria-label={`Change windows, ${monthTitle(month)}`} className="flex flex-col border-t border-l border-line">
      <div role="row" className="grid grid-cols-7">
        {WEEKDAYS.map(d => (
          <div key={d} role="columnheader" className="border-r border-b border-line bg-[#fafafa] px-2 py-1 text-[11px] font-semibold tracking-[0.04em] text-navy-3 uppercase">{d}</div>
        ))}
      </div>
      {weeks.map(week => (
        <div key={week[0]} role="row" className="grid grid-cols-7">
          {week.map(day => {
            const list = on(day);
            const isToday = day === today;
            return (
              <div
                key={day}
                role="cell"
                aria-label={`${fmtWeekday(day)}: ${list.length ? plural(list.length, 'change', 'changes') : 'no change'}`}
                className={cn('flex min-h-[92px] min-w-0 flex-col gap-1 border-r border-b border-line p-1.5', !inMonth(day) && 'bg-[#fafafa]')}
              >
                <span className={cn('self-start rounded-full px-1.5 text-xs leading-5 tabular-nums', isToday ? 'bg-gold font-semibold text-navy' : inMonth(day) ? 'text-navy-2' : 'text-navy-4')}>
                  {Number(day.slice(8, 10))}
                  {isToday && <span className="sr-only">, today</span>}
                </span>
                {list.map(c => <CalendarChip key={c.changeRequestId} change={c} />)}
              </div>
            );
          })}
        </div>
      ))}
    </div>
  );
}

function CalendarChip({ change }: { change: ScheduledChange }) {
  const href = changeHref(change.changeRequestId);
  const s = stateOf(change);
  const emergency = change.changeType.key === 'emergency';
  const label = `${change.title}, ${change.applicationName}. ${fmtWindow(change.windowStart, change.windowEnd)}. ${change.changeType.label}. ${change.status.label}. ${s.text}.`;
  const body = (
    <>
      <span className={cn('inline-flex shrink-0 rounded-sm p-px', s.tone)}>{s.icon}</span>
      {emergency && <Siren className="size-3.5 shrink-0 text-danger" aria-hidden />}
      <span className="truncate">{change.title}</span>
    </>
  );
  const cls = cn(
    'flex min-w-0 items-center gap-1 rounded-[5px] border bg-card px-1 py-0.5 text-xs leading-4 text-navy no-underline',
    emergency ? 'border-danger-line' : 'border-line',
  );
  return href
    ? <a href={href} className={cn(cls, 'hover:border-primary focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring')} aria-label={label} title={label}>{body}</a>
    : <span className={cls} aria-label={label} title={label}>{body}</span>;
}

function CalendarLegend() {
  const item = 'inline-flex items-center gap-1.5';
  const chip = (tone: string, icon: ReactNode) => <span className={cn('inline-flex rounded-sm p-px', tone)} aria-hidden>{icon}</span>;
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2 pt-2 text-xs text-navy-2" role="group" aria-label="Legend">
      <span className={item}>{chip('bg-success-soft text-success', <CircleCheck className="size-3.5" />)}Authorized</span>
      <span className={item}>{chip('bg-warning-soft text-warning', <Clock className="size-3.5" />)}Not authorized yet</span>
      <span className={item}>{chip('bg-muted text-navy-2', <PencilLine className="size-3.5" />)}Draft</span>
      <span className={item}>{chip('bg-subtle-soft text-navy-2', <Archive className="size-3.5" />)}Closed</span>
      <span className={item}><Siren className="size-3.5 text-danger" aria-hidden />Emergency</span>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// A new change
// ---------------------------------------------------------------------------------------------------------------------

export function ChangeNew({ route }: { route: RouteOf<'change-new'> }) {
  const who = useWhoAmI();
  const appId = route.applicationId ?? null;
  const [savedId, setSavedId] = useState<string | null>(null);
  const card = useTool('get_application_card', appId ? { businessApplicationId: appId, sections: ['owners'] } : null);
  const app = dataOf(card)?.application ?? null;
  const allowed = canUse(who, 'save_change_request_draft');
  const saved = useTool('get_change_request', savedId ? { changeRequestId: savedId } : null);
  const savedMeaning = dataOf(saved)?.change.status.meaning ?? null;
  const submitted = !!savedMeaning && savedMeaning !== 'draft';

  // A new application chosen in the address starts the plan again.
  useEffect(() => { setSavedId(null); }, [appId]);

  return (
    <>
      <PageHeader
        eyebrow="Changes"
        title="New change"
        subtitle="Choose the application, plan the change, then check it and submit it. The engine checks every step and asks the approvers when you submit."
        meta={app ? <span>{`For ${app.name}`}</span> : undefined}
        actions={(
          <Button variant="outline" asChild>
            <a href={hrefTo({ page: 'changes', ...(appId ? { applicationId: appId } : {}) })}>Back to changes</a>
          </Button>
        )}
      />
      {who.status === 'known' && !allowed && (
        <Panel title="You cannot plan changes" className="mb-4">
          <p className={plainText}>
            Your roles do not include planning changes. People with the maker, application owner or change manager role
            plan them. You can still read every change you are allowed to see.
          </p>
        </Panel>
      )}
      <ol className="m-0 flex list-none flex-col gap-4 p-0" aria-label="Steps">
        <li>
          <Panel title="1. The application" subtitle="The main application the change is for. Its tier sets who must authorize it.">
            {appId ? (
              card.status === 'loading' ? <Muted>Reading the application.</Muted>
                : card.status === 'done' && !card.envelope.ok ? <PanelFailure failure={card.envelope} />
                  : app && <ChosenApp app={app} owners={dataOf(card)?.owners ?? []} />
            ) : <AppPicker myId={myObjectId(who)} />}
          </Panel>
        </li>
        {appId && app && (
          <li>
            <PlanStep app={app} allowed={allowed} saved={!!savedId} submitted={submitted} onSaved={setSavedId} />
          </li>
        )}
        {appId && app && savedId && (
          <li>
            <SavedDraft changeRequestId={savedId} state={saved} app={app} who={who} />
          </li>
        )}
      </ol>
    </>
  );
}

function ChosenApp({ app, owners }: { app: ApplicationCard['application']; owners: NonNullable<ApplicationCard['owners']> }) {
  const href = appHref(app.businessApplicationId);
  const business = owners.find(o => o.ownerRoleKey === 'business-owner' && !o.isDeputy);
  return (
    <div className="flex flex-col gap-3">
      <Facts>
        <Fact label="Application">{href ? <TextLink href={href}>{app.name}</TextLink> : app.name}</Fact>
        <Fact label="Tier" sub={app.tier ? undefined : 'Only an accepted tier decision sets the tier.'}>{app.tier?.label ?? 'No tier yet'}</Fact>
        <Fact label="Stage">{app.stage?.label ?? 'Not set'}</Fact>
        <Fact label="Kind">{app.kind.label}</Fact>
        <Fact label="Business owner">{business?.principal.displayLabel ?? 'No holder'}</Fact>
      </Facts>
      <Button variant="link" size="sm" className="h-7 self-start px-0" onClick={() => goTo({ page: 'change-new' }, true)}>Choose another application</Button>
    </div>
  );
}

// Search the register, or pick one of the applications the person owns.
function AppPicker({ myId }: { myId: string | null }) {
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  useEffect(() => {
    const t = window.setTimeout(() => setQuery(text.trim()), 300);
    return () => window.clearTimeout(t);
  }, [text]);
  const found = useTool('find_applications', query ? { text: query, top: 8 } : null);
  const owned = useTool('list_owner_assignments', myId ? { principalObjectId: myId, state: 'current', top: 50 } : null);
  const ownedApps = useMemo(() => {
    const seen = new Map<string, string>();
    for (const o of dataOf(owned)?.items ?? []) if (!seen.has(o.businessApplicationId)) seen.set(o.businessApplicationId, o.applicationName);
    return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
  }, [owned]);
  const pick = (id: string) => goTo({ page: 'change-new', applicationId: id }, true);
  const choice = (id: string, name: string, note?: string) => (
    <li key={id}>
      <button
        type="button"
        onClick={() => pick(id)}
        className="flex w-full cursor-pointer items-center justify-between gap-3 rounded-md border border-line bg-card px-3 py-2 text-left hover:border-primary focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
      >
        <span className="flex min-w-0 flex-col">
          <span className="text-sm font-semibold [overflow-wrap:anywhere] text-navy">{name}</span>
          {note && <span className="text-xs text-navy-3">{note}</span>}
        </span>
        <ArrowRight className="size-4 shrink-0 text-navy-3" aria-hidden />
      </button>
    </li>
  );
  return (
    <div className="flex flex-col gap-3">
      <div className="relative max-w-[480px]">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
        <Input type="search" value={text} onChange={e => setText(e.target.value)} placeholder="Search by name or purpose" aria-label="Search applications by name or purpose" className="pl-8" maxLength={200} />
      </div>
      {query ? (
        found.status === 'loading' ? <Muted>Searching.</Muted>
          : !found.envelope.ok ? <PanelFailure failure={found.envelope} />
            : found.envelope.data.items.length ? (
              <ul className="m-0 flex max-w-[640px] list-none flex-col gap-1.5 p-0" aria-label="Matching applications">
                {found.envelope.data.items.map(a => choice(a.businessApplicationId, a.name, [a.kind.label, a.tier?.label ?? 'No tier yet', a.stage?.label].filter(Boolean).join('. ')))}
              </ul>
            ) : <Empty>No registered application matches.</Empty>
      ) : ownedApps.length > 0 ? (
        <div className="flex flex-col gap-1.5">
          <h3 className={subhead}>Applications you own</h3>
          <ul className="m-0 flex max-w-[640px] list-none flex-col gap-1.5 p-0" aria-label="Applications you own">
            {ownedApps.map(a => choice(a.id, a.name))}
          </ul>
        </div>
      ) : <Muted>Type to search the register.</Muted>}
    </div>
  );
}

function PlanStep({ app, allowed, saved, submitted, onSaved }: { app: ApplicationCard['application']; allowed: boolean; saved: boolean; submitted: boolean; onSaved: (id: string) => void }) {
  const host = useHost();
  const types = useLabels('change-type');
  const stages = useLabels('target-stage');
  const keys = types.keys().slice(0, 4);
  const scopeFor = (k: string | undefined) => (k && app.tier
    ? { scope: { tierKey: app.tier.key, applicationKindKey: app.kind.key, ...(app.stage ? { lifecycleStageKey: app.stage.key } : {}), changeTypeKey: k } }
    : null);
  const reqs = [
    useTool('get_requirements', scopeFor(keys[0])),
    useTool('get_requirements', scopeFor(keys[1])),
    useTool('get_requirements', scopeFor(keys[2])),
    useTool('get_requirements', scopeFor(keys[3])),
  ];
  const walk = useTool('get_dependency_impact', { businessApplicationId: app.businessApplicationId, direction: 'dependants', depth: 2 });
  const coming = useTool('get_change_schedule', { ...utcDayRange(host.today, addDays(host.today, 28)), businessApplicationId: app.businessApplicationId, top: 50 });
  return (
    <Panel
      title="2. Plan the change"
      subtitle="What it changes, the window, the target and the other applications it touches. Saved as a draft."
      actions={allowed ? (
        <span className="flex flex-wrap items-center gap-2">
          {saved && <ToneBadge tone="success">{submitted ? 'Submitted' : 'Draft saved'}</ToneBadge>}
          <ChangeDraftSheet
            applicationId={app.businessApplicationId}
            applicationName={app.name}
            trigger={saved ? <Button variant="outline"><PencilLine aria-hidden />Edit the draft</Button> : <Button><Plus aria-hidden />Plan the change</Button>}
            show={!submitted}
            onSaved={d => { if (d.recordId) onSaved(d.recordId); }}
          />
        </span>
      ) : undefined}
    >
      <div className="grid grid-cols-3 gap-4 medium:grid-cols-1">
        <div className="flex min-w-0 flex-col gap-1.5">
          <h3 className={subhead}>Who authorizes it, by type</h3>
          {!app.tier ? (
            <p className={plainText}>{app.name} has no tier yet, so the approvals are worked out when the change is submitted.</p>
          ) : (
            <ul className="m-0 flex list-none flex-col gap-2.5 p-0">
              {keys.map((k, i) => {
                const st = reqs[i];
                const needs = types.field<boolean>(k, 'requiresAuthorization');
                const approvals = (dataOf(st)?.approvals ?? []).filter(a => a.decisionTypeKey === 'change-authorization');
                return (
                  <li key={k} className="flex flex-col gap-1">
                    <span className="text-sm font-semibold text-navy">{types.label(k)}</span>
                    {needs === false ? <Muted>No authorization when an approved standard model fits.</Muted>
                      : st.status === 'loading' ? <Muted>Reading the rules.</Muted>
                        : !st.envelope.ok ? <Muted>{`The rules could not be read: ${st.envelope.error.message}`}</Muted>
                          : <ApprovalNeededList items={approvals} empty="No authorization is asked." />}
                  </li>
                );
              })}
            </ul>
          )}
        </div>
        <div className="flex min-w-0 flex-col gap-1.5">
          <h3 className={subhead}>Applications that depend on it</h3>
          {walk.status === 'loading' ? <Muted>Walking the dependencies.</Muted>
            : !walk.envelope.ok ? <PanelFailure failure={walk.envelope} />
              : walk.envelope.data.nodes.length ? (
                <RowList label="Applications that depend on it">
                  {walk.envelope.data.nodes.map(n => {
                    const href = appHref(n.businessApplicationId);
                    return (
                      <Row
                        key={n.businessApplicationId}
                        primary={href ? <TextLink href={href}>{n.name}</TextLink> : n.name}
                        secondary={`${n.depth === 1 ? 'Directly' : `Through ${n.depth - 1} more`}. ${n.tier?.label ?? 'No tier yet'}`}
                      />
                    );
                  })}
                </RowList>
              ) : <Empty>No application depends on it.</Empty>}
          <Muted>The plan marks the direct dependants as affected. You choose.</Muted>
        </div>
        <div className="flex min-w-0 flex-col gap-1.5">
          <h3 className={subhead}>Its changes in the next 4 weeks</h3>
          {coming.status === 'loading' ? <Muted>Reading the schedule.</Muted>
            : !coming.envelope.ok ? <PanelFailure failure={coming.envelope} />
              : coming.envelope.data.items.length ? (
                <RowList label="Its changes in the next 4 weeks">
                  {coming.envelope.data.items.map(c => {
                    const href = changeHref(c.changeRequestId);
                    return (
                      <Row
                        key={c.changeRequestId}
                        primary={href ? <TextLink href={href}>{c.title}</TextLink> : c.title}
                        secondary={`${fmtWindow(c.windowStart, c.windowEnd)}. To ${targetText(c, stages)}`}
                        end={<StatusBadge status={c.status} />}
                      />
                    );
                  })}
                </RowList>
              ) : <Empty>Nothing planned. No window to avoid.</Empty>}
        </div>
      </div>
    </Panel>
  );
}

// After the draft is saved: the checks before submitting, and submit. The submit sheet stays mounted after the change is
// submitted, so the engine's answer (who is asked now) stays in view.
function SavedDraft({ changeRequestId, state, app, who }: { changeRequestId: string; state: ToolState<ChangeRequestDetail>; app: ApplicationCard['application']; who: ReturnType<typeof useWhoAmI> }) {
  const detail = dataOf(state);
  const { target } = useTargetOf(detail?.change ?? null);
  const preview = useApprovalPreview(detail?.change ?? null, app);
  const href = changeHref(changeRequestId);
  if (state.status === 'loading' || !state.envelope.ok || !detail) return <Panel title="3. Check and submit" state={state}>{null}</Panel>;
  const draft = detail.change.status.meaning === 'draft';
  return (
    <BeforeSubmit
      detail={detail}
      app={app}
      targetStageKey={target?.stageKey ?? null}
      preview={preview}
      title="3. Check and submit"
      footer={(
        <div className="flex flex-wrap items-center gap-2 border-t border-line pt-3">
          <SubmitChangeSheet changeRequestId={changeRequestId} title={detail.change.title} askedText={preview.asked} show={draft && canUse(who, 'submit_record')} />
          {!draft && <StatusBadge status={detail.change.status} />}
          {!draft && <Muted>The approvers are asked. Follow the approvals on the change record.</Muted>}
          {href && <TextLink href={href} className="inline-flex items-center gap-1 text-sm">Open the change record <ArrowRight className="size-4" aria-hidden /></TextLink>}
        </div>
      )}
    />
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// The change request record
// ---------------------------------------------------------------------------------------------------------------------

export function ChangeRecord({ changeRequestId }: { changeRequestId: string }) {
  const host = useHost();
  const who = useWhoAmI();
  const me = myObjectId(who);
  const stages = useLabels('target-stage');
  const impactRoles = useListValues('impact-role');
  const state = useTool('get_change_request', { changeRequestId });
  const detail = dataOf(state);
  const change = detail?.change ?? null;
  const appId = change?.businessApplicationId ?? null;

  const card = useTool('get_application_card', appId ? { businessApplicationId: appId, sections: ['owners'] } : null);
  const app = dataOf(card)?.application ?? null;
  const { target, reading: targetReading, scheduled } = useTargetOf(change);
  const preview = useApprovalPreview(change, app);

  const meaning = change?.status.meaning ?? null;
  const decisionId = detail?.authorization.decisionId ?? null;
  const approval = useTool('get_approval_status', decisionId ? { subjectKind: 'change-request', subjectId: changeRequestId } : null);
  const pendingDecision = !!decisionId && detail?.authorization.status?.meaning === 'proposed';
  const mine = useTool('list_approvals', pendingDecision && appId ? { view: 'mine', businessApplicationId: appId, top: 50 } : null);
  const askedOfMe = (dataOf(mine)?.items ?? []).find(i => sameId(i.decision.decisionId, decisionId) && i.outcome.meaning === 'pending') ?? null;

  if (state.status === 'done' && !state.envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Change request" title="Change request" />
        <PanelFailure failure={state.envelope} />
      </>
    );
  }
  if (!detail || !change) {
    return (
      <>
        <PageHeader eyebrow="Change request" title="Loading the change" />
        <Panel title="The change" state={state} skeletonRows={6}>{null}</Panel>
      </>
    );
  }

  const isCreator = !change.createdBy || samePrincipal(change.createdBy, me) || samePrincipal(change.performedBy?.onBehalfOf, me);
  const draft = meaning === 'draft';
  const canEdit = draft && isCreator && canUse(who, 'save_change_request_draft');
  const canSubmit = draft && isCreator && canUse(who, 'submit_record');
  const canWithdraw = (draft || meaning === 'submitted') && isCreator && canUse(who, 'withdraw_record');
  // A change whose authorization people rejected is not carried out, so it takes no result (QA 2026-11-24). The engine
  // keeps a rejected decision to itself (document 25, B25-8): the change stays submitted, and the page says why.
  const authorizationRejected = detail.authorization.status?.meaning === 'rejected';
  const canResult = (meaning === 'submitted' || meaning === 'in-progress') && !authorizationRejected && canUse(who, 'record_change_result');
  const canClose = !!detail.closable && canUse(who, 'complete_change');
  const canLink = canUse(who, 'save_link');
  const aiForMe = change.isAiCreated && (samePrincipal(change.performedBy?.onBehalfOf, me) || samePrincipal(change.createdBy, me));
  const minutes = windowMinutes(change.windowStart, change.windowEnd);
  const appLink = appHref(change.businessApplicationId);
  const decisionHref = decisionId ? recordHrefOf('decision', decisionId) : null;

  return (
    <>
      <PageHeader
        eyebrow="Change request"
        title={change.title}
        status={authorizationRejected && meaning === 'submitted'
          ? <StatusBadge status={{ meaning: 'rejected', label: 'Not authorized' }} />
          : <StatusBadge status={change.status} />}
        subtitle={(
          <span className="flex flex-wrap items-center gap-1.5">
            <ChangeTypeBadge type={change.changeType} />
            <ToneBadge tone={change.mode === 'linked' ? 'informative' : 'subtle'} icon={null}>{change.mode === 'linked' ? 'Linked to ITSM' : 'Built-in approvals'}</ToneBadge>
            <RecordAuthorizationBadge authorization={detail.authorization} scheduled={scheduled} />
            <AiMark isAiCreated={change.isAiCreated} performedBy={change.performedBy} />
          </span>
        )}
        meta={(
          <span className="flex flex-col gap-0.5">
            <span>
              {app ? <>For {appLink ? <TextLink href={appLink}>{app.name}</TextLink> : app.name}. </> : null}
              {fmtWindow(change.windowStart, change.windowEnd)}.
            </span>
            <StampLine performedBy={change.performedBy} />
            <DataStamp envelope={state.status === 'done' ? state.envelope : undefined} />
          </span>
        )}
        actions={(
          <>
            {/* Each act shows while it fits the record, and its sheet stays open after the write changes the record. */}
            <SubmitChangeSheet changeRequestId={change.changeRequestId} title={change.title} label={aiForMe ? 'Accept and submit' : 'Submit'} askedText={preview.asked} show={canSubmit} />
            <ChangeDraftSheet
              applicationId={change.businessApplicationId}
              applicationName={app?.name ?? null}
              existing={{ change, affected: detail.affected }}
              trigger={<Button variant="outline"><PencilLine aria-hidden />Edit the draft</Button>}
              show={canEdit}
            />
            <CompleteChangeSheet detail={detail} show={canClose} />
            <RecordResultSheet detail={detail} show={canResult} />
            <WithdrawChangeSheet changeRequestId={change.changeRequestId} title={change.title} show={canWithdraw} />
            <Button variant="ghost" asChild>
              <a href={hrefTo({ page: 'changes', applicationId: change.businessApplicationId })}>Its changes</a>
            </Button>
          </>
        )}
      />

      {askedOfMe && decisionHref && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-info-line bg-info-soft px-4 py-3 text-navy" role="status">
          <span className="text-sm">
            <span className="font-semibold">You are asked to authorize this change.</span>{' '}
            {askedOfMe.askedRole ? `As ${askedOfMe.askedRole.label.toLowerCase()}. ` : askedOfMe.askedOwnerRole ? `As ${askedOfMe.askedOwnerRole.label.toLowerCase()} of the application. ` : ''}
            Answer on the decision page, where your answer is recorded under your name.
          </span>
          <Button asChild><a href={decisionHref}>Answer on the decision page <ArrowRight aria-hidden /></a></Button>
        </div>
      )}

      <div className={recordGrid}>
        <div className={column}>
          <Panel title="The change">
            <p className="m-0 max-w-[760px] text-sm leading-relaxed whitespace-pre-wrap [overflow-wrap:anywhere] text-navy-2">{change.description}</p>
            <Facts className="border-t border-line pt-3.5">
              <Fact label="Application">{appLink ? <TextLink href={appLink}>{app?.name ?? 'Open the application'}</TextLink> : 'Not set'}</Fact>
              <Fact label="Window" sub={minutes !== null ? durationText(minutes) : undefined}>{fmtWindow(change.windowStart, change.windowEnd)}</Fact>
              <Fact label="Planned target" sub={target?.stageKey ? stages.label(target.stageKey) : undefined}>
                {change.plannedTargetId ? target?.label ?? (targetReading ? 'Reading' : 'A target with no deployment yet') : 'No target yet'}
              </Fact>
              <Fact label="Tier" sub="From the application">{app?.tier?.label ?? (app ? 'No tier yet' : 'Reading')}</Fact>
              <Fact label="Standard model">{change.standardChangeModel?.label ?? 'None'}</Fact>
              <Fact label="Created by">{change.createdBy?.displayLabel ?? 'Not recorded'}</Fact>
              {(change.externalChangeNumber || change.mode === 'linked') && (
                <Fact label="ITSM change" wide>
                  {change.externalChangeNumber
                    ? change.externalChangeUrl ? <ExternalLink href={change.externalChangeUrl}>{change.externalChangeNumber}</ExternalLink> : change.externalChangeNumber
                    : 'No number recorded yet'}
                </Fact>
              )}
            </Facts>
          </Panel>

          {draft ? (
            <BeforeSubmit
              detail={detail}
              app={app}
              targetStageKey={target?.stageKey ?? null}
              preview={preview}
              footer={!isCreator && change.createdBy ? (
                <div className="border-t border-line pt-3"><Muted>{`Only ${change.createdBy.displayLabel} can edit or submit this draft.`}</Muted></div>
              ) : canSubmit ? (
                <div className="border-t border-line pt-3"><Muted>When the checks read right, submit the change from the top of the page.</Muted></div>
              ) : null}
            />
          ) : (
            <Panel
              title="Authorization"
              subtitle={change.mode === 'linked' ? 'Authorized in your ITSM tool' : 'The change authorization decision and its approvals'}
              actions={decisionHref ? <TextLink href={decisionHref} className="inline-flex items-center gap-1 text-sm">Decision page <ArrowRight className="size-4" aria-hidden /></TextLink> : undefined}
            >
              <Authorization detail={detail} approval={approval} />
            </Panel>
          )}

          <Panel title="Deployments" count={detail.deployments.length} subtitle="Deployments linked to this change">
            {detail.deployments.length ? (
              <RowList label="Deployments">
                {detail.deployments.map(d => {
                  const href = withAppHint(recordHrefOf('deployment-record', d.deploymentRecordId), change.businessApplicationId);
                  return (
                    <Row
                      key={d.deploymentRecordId}
                      primary={href ? <TextLink href={href}>{d.targetLabel}</TextLink> : d.targetLabel}
                      secondary={`Finished ${fmtDateTime(d.finishedOn)}`}
                      end={<StatusBadge status={d.result} />}
                      badges={<FlagBadges flags={d.flags} />}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>{draft ? 'Nothing is deployed for a draft.' : 'No deployment is linked to this change yet.'}</Empty>}
          </Panel>

          {!draft && (
            <Panel title="Result">
              <ResultSummary detail={detail} />
              <div className="flex flex-col gap-1">
                <h3 className={subhead}>Post-change reviews of this change</h3>
                {detail.postChangeReviews?.length ? (
                  <RowList label="Post-change reviews">
                    {detail.postChangeReviews.map(r => {
                      const href = recordHrefOf('review', r.reviewId);
                      return (
                        <Row
                          key={r.reviewId}
                          primary={href ? <TextLink href={href}>Post-change review</TextLink> : 'Post-change review'}
                          secondary={r.doneOn ? `Done ${fmtDate(r.doneOn)}` : r.dueOn ? `Due ${fmtDate(r.dueOn)}` : 'No due date'}
                          end={<StatusBadge status={r.status} />}
                        />
                      );
                    })}
                  </RowList>
                ) : <Empty>{meaning === 'completed' || detail.result ? 'No post-change review: this change needed none.' : 'None yet. One opens when the change type asks for it or the change does not fully succeed.'}</Empty>}
                {appId && <TextLink className="self-start text-xs" href={hrefTo({ page: 'reviews', state: 'all', applicationId: appId.toLowerCase(), typeKeys: ['post-change'] })}>Every post-change review of {app?.name ?? 'the application'}</TextLink>}
              </div>
            </Panel>
          )}
        </div>

        <div className={column}>
          <Panel title="Affected applications" count={detail.affected.length}>
            {detail.affected.length ? (
              <RowList label="Affected applications">
                {detail.affected.map(a => {
                  const href = appHref(a.businessApplicationId);
                  return (
                    <Row
                      key={a.businessApplicationId}
                      primary={href ? <TextLink href={href}>{a.name}</TextLink> : a.name}
                      secondary={a.impactRoleKey ? impactRoles.label(a.impactRoleKey) : 'No impact role'}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>No other application is marked as affected.</Empty>}
          </Panel>

          <Panel
            title="Work items and evidence"
            count={detail.links.length}
            actions={canLink ? (
              <span className="flex flex-wrap gap-1.5">
                <SaveLinkSheet subjectKind="change-request" subjectId={change.changeRequestId} category="delivery" />
                <SaveLinkSheet subjectKind="change-request" subjectId={change.changeRequestId} category="evidence" />
              </span>
            ) : undefined}
          >
            <LinksList links={detail.links} />
          </Panel>

          <DecisionsAbout subjectKind="change-request" subjectId={change.changeRequestId} title="Decisions about this change" empty="No decision is recorded about this change yet." />

          <Panel title="On the timeline">
            <p className={plainText}>See this change beside the other windows and the deployments on the same targets.</p>
            <TextLink href={host.href({ page: 'timeline' })} className="inline-flex items-center gap-1 self-start text-sm">Open the timeline <ArrowRight className="size-4" aria-hidden /></TextLink>
          </Panel>
        </div>
      </div>
    </>
  );
}

function Authorization({ detail, approval }: { detail: ChangeRequestDetail; approval: ReturnType<typeof useTool<'get_approval_status'>> }) {
  const auth = detail.authorization;
  if (detail.change.mode === 'linked') {
    const source = auth.linkedSource === 'itsm-step' ? 'Your ITSM tool reported the authorization through its step.'
      : auth.linkedSource === 'attestation' ? 'A person attested the authorization.'
        : 'No authorization has been reported from your ITSM tool yet.';
    return (
      <div className="flex flex-col gap-2">
        <p className={plainText}>{source} A recorded change number alone never counts as authorization.</p>
        {detail.change.externalChangeNumber && (
          <span className="text-sm text-navy">
            ITSM change{' '}
            {detail.change.externalChangeUrl
              ? <ExternalLink href={detail.change.externalChangeUrl}>{detail.change.externalChangeNumber}</ExternalLink>
              : detail.change.externalChangeNumber}
          </span>
        )}
      </div>
    );
  }
  if (!auth.decisionId) return <Empty>No authorization decision yet. It opens when the change is submitted.</Empty>;
  return (
    <div className="flex flex-col gap-3">
      {auth.status && (
        <span className="flex flex-wrap items-center gap-2 text-sm text-navy">
          The decision is <StatusBadge status={auth.status} />
        </span>
      )}
      {approval.status === 'loading' ? <Muted>Reading the approvals.</Muted>
        : !approval.envelope.ok ? <PanelFailure failure={approval.envelope} />
          : <ApprovalStatusView status={approval.envelope.data} settled={auth.status} />}
    </div>
  );
}
