// Deployments (screen "Deployments", priority 1; D-36: the web app acts): every deployment with its flags and its
// follow-up, and path C: a person records a deployment by hand (D-19).
//   /app/deployments?view=all|flagged&flag=<key>&app=<id>&change=<id>&target=<id>   RouteOf<'deployments'>
//     and the page's own: within=7|30|90|365 or from=<date>&to=<date>, result=<meaning>, latest=1
//   /app/deployments/new?app=<id>&change=<id>                                        RouteOf<'deployment-new'>
// The deployment record page (/app/records/deployment-record/<id>) is DeploymentRecord in Records.tsx.
// Reads: get_deployments (the list, page by page, and its counts per flag), list_deployment_targets (the target and
// application filters, flagged per target), get_change_request (the change filter's name), get_application_card,
// get_change_schedule and get_pipeline_map (the record-by-hand form).
// Writes, each in the shared side sheet: record_deployment (the action sheet sends its idempotencyKey) and the follow-up
// save_decision_proposal (an exception about a flagged deployment). A change after the fact opens the new change page.
//
// Design choices (D-41), recorded here:
// - The new-deployment form offers the targets in service only (list_deployment_targets state active, wave 3): a retired
//   target refuses every new deployment (deployment.target-retired), so it is never offered.
// - The list shows the history (every record, corrections included) by default; "Latest per application and target"
//   narrows it to what runs where. A list of deployments that hides the earlier ones would hide flagged history.
// - The figures above the list count the same filters before the flag filters (the tool's counts), so choosing a flag
//   never changes the figures; each figure opens its rows.
// - The date range is a preset (last 7, 30, 90 or 365 days) or two dates, kept in the address with the result and
//   the latest switch, so Back and a shared link keep them.
// - Flagged per target counts every record the target holds, whatever the filters: the target read counts them.
// - Wave 4: a portfolio filter (portfolio=<id>, get_deployments portfolioId, the portfolios below it included) beside the
//   target and the application, and the text search q= over the application, artifact, version, target and the AI
//   client that started it. get_deployments takes no words, so the search keeps the rows read so far and says so.
import { ChevronDown, CircleCheck, ListFilter, MapPin, Plus, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { goTo, hrefTo, recordHrefOf, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { DEPLOYMENT_FLAG_KEYS, type DeploymentFlagRouteKey, type RouteOf } from '../app/routes';
import type { DeploymentItem, DeploymentResultMeaning, RecordDeploymentResult } from '../data/contracts';
import { fmtDate, plural } from '../data/format';
import { flagHelp, flagText } from '../data/meanings';
import { useConfigRows, usePagedRead, useRead } from '../data/reads';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import { useSession } from '../data/session';
import { dataOf } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { GuidelineChip } from '../components/actions';
import { BarList, DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../components/blocks';
import { Row, RowList } from '../components/rows';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '../components/ui/dropdown-menu';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { flagFollowUp, flagGuideline, gateLevelLabel, withAppHint } from './changes-records/logic';
import { mayPropose as mayProposeType } from './pmo-portfolio/logic';
import {
  appsFromTargets,
  correctedIds,
  deploymentText,
  DATE_PARAM,
  exceptionCovering,
  dateRangeOf,
  deploymentsQuery,
  FLAG_FIELD,
  FLAG_KEY_OF,
  FLAG_ORDER,
  flagExplanation,
  LATEST_PARAM,
  RESULT_MEANINGS,
  RESULT_PARAM,
  RESULT_TEXT,
  sameId,
  WITHIN_PARAM,
} from './deployments-places/logic';
import { AppPicker, Chip, column, DeploymentRows, placeHref, plainText, ShowMore, subhead, targetChoices, twoColumns, useAllowedRoutes, useRouteWords, useTargets, type AppChoice } from './deployments-places/parts';
import { RecordDeploymentSheet } from './deployments-places/sheets';
import { ProposeExceptionSheet } from './Records';

const WITHIN_TEXT: Record<string, string> = { '7': 'Last 7 days', '30': 'Last 30 days', '90': 'Last 90 days', '365': 'Last 365 days' };

const FLAG_FIGURES: { flag: DeploymentFlagRouteKey; caption: string }[] = [
  { flag: 'missing-approval', caption: 'No authorization where one is expected' },
  { flag: 'outside-window', caption: 'Finished outside the change window' },
  { flag: 'other-path', caption: 'Not a route the target allows' },
  { flag: 'ai-only-code-approval', caption: 'Only an AI approved the code' },
];

type Params = { within?: string; from?: string; to?: string; result?: string; latest?: string; portfolio?: string; q?: string };
const ID_PARAM = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export function Deployments({ route }: { route: RouteOf<'deployments'> }) {
  const host = useHost();
  const session = useSession();
  const routeWords = useRouteWords();
  const params: Params = {
    within: useQueryParam('within', WITHIN_PARAM),
    from: useQueryParam('from', DATE_PARAM),
    to: useQueryParam('to', DATE_PARAM),
    result: useQueryParam('result', RESULT_PARAM),
    latest: useQueryParam('latest', LATEST_PARAM),
    portfolio: useQueryParam('portfolio', ID_PARAM)?.toLowerCase(),
    q: useListText(),
  };
  const portfolios = useRead('list_portfolios', { top: 100 });
  const portfolioItems = (dataOf(portfolios)?.items ?? []).filter(p => p.isActive);
  const portfolioName = params.portfolio ? portfolioItems.find(p => sameId(p.portfolioId, params.portfolio))?.name ?? 'One portfolio' : null;
  const view = route.view ?? 'all';
  const flagKeys = route.flagKeys ?? [];
  const input = deploymentsQuery(route, { ...params, latest: params.latest === '1' }, host.today);
  const text = params.q;
  const list = usePagedRead('get_deployments', input, d => d.items);
  const targets = useTargets();
  const change = useRead('get_change_request', route.changeRequestId ? { changeRequestId: route.changeRequestId } : null);
  // The smallest section: the page needs only the application's name (get_application_card asks for at least one).
  const appCard = useRead('get_application_card', route.applicationId ? { businessApplicationId: route.applicationId, sections: ['owners'] } : null);
  // Who may propose an exception, and the exceptions that already cover a flagged deployment (QA 2026-11-24).
  const rights = useConfigRows('decision-right');
  const exceptionLog = useRead('get_decision_log', { decisionTypeKeys: ['exception'], statusMeanings: ['proposed', 'accepted'], top: 100 });
  const exceptions = dataOf(exceptionLog)?.items ?? [];
  const [selected, setSelected] = useState<DeploymentItem | null>(null);
  const [customDates, setCustomDates] = useState(!!(params.from || params.to));

  const data = list.status === 'done' ? list.first.data : null;
  const counts = data?.counts ?? null;
  const allItems = list.status === 'done' ? list.items : [];
  const items = filterByText(allItems, text, deploymentText) as DeploymentItem[];
  const corrected = correctedIds(allItems);
  const apps = appsFromTargets(targets.items);
  const appName = dataOf(appCard)?.application.name ?? apps.find(a => sameId(a.id, route.applicationId))?.name ?? items.find(i => sameId(i.businessApplicationId, route.applicationId))?.applicationName ?? 'This application';
  const targetName = targets.items.find(t => sameId(t.deploymentTargetId, route.targetId))?.label ?? items.find(i => sameId(i.deploymentTargetId, route.targetId))?.targetLabel ?? 'This target';
  const range = dateRangeOf({ within: params.within, from: params.from, to: params.to }, host.today);

  type Next = Partial<Pick<RouteOf<'deployments'>, 'view' | 'flagKeys' | 'applicationId' | 'changeRequestId' | 'targetId'>>;
  const go = (next: Next, nextParams: Params = params) => {
    const target = { page: 'deployments' as const, view: route.view, flagKeys: route.flagKeys, applicationId: route.applicationId, changeRequestId: route.changeRequestId, targetId: route.targetId, ...next };
    const clean = Object.fromEntries(Object.entries(target).filter(([, v]) => v !== undefined && !(Array.isArray(v) && v.length === 0))) as RouteOf<'deployments'>;
    if (clean.view === 'all') delete clean.view;
    navigate(withParams(hrefTo(clean), nextParams), true);
  };
  const toggleFlag = (key: DeploymentFlagRouteKey) => {
    const next = flagKeys.includes(key) ? flagKeys.filter(k => k !== key) : [...flagKeys, key];
    go({ flagKeys: next.length ? next : undefined });
  };
  const setWhen = (value: string) => {
    if (value === 'custom') { setCustomDates(true); return; }
    setCustomDates(false);
    go({}, { ...params, within: value === 'any' ? undefined : value, from: undefined, to: undefined });
  };
  const whenValue = params.within ?? (customDates || params.from || params.to ? 'custom' : 'any');
  const anyFilter = view === 'flagged' || flagKeys.length > 0 || !!route.applicationId || !!route.targetId || !!route.changeRequestId || !!range.from || !!range.to || !!params.result || params.latest === '1' || !!params.portfolio || !!text;
  const clearAll = () => { setCustomDates(false); go({ view: undefined, flagKeys: undefined, applicationId: undefined, targetId: undefined, changeRequestId: undefined }, {}); };
  const flagged = targets.items.filter(t => t.flaggedCount > 0).sort((a, b) => b.flaggedCount - a.flaggedCount || a.label.localeCompare(b.label));
  const mayPropose = session.can('save_decision_proposal') && mayProposeType(rights.rows, 'exception', session.roleKeys, []);

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Deployments"
        subtitle="The deployments on record, with their route, gate level and the flags the engine raised. Follow up a flagged one with an exception, or a change after the fact."
        meta={<DataStamp envelope={list.status === 'done' ? list.first : undefined} />}
        actions={(
          <>
            {session.can('record_deployment') && (
              <Button asChild>
                <a href={hrefTo({ page: 'deployment-new', ...(route.applicationId ? { applicationId: route.applicationId } : {}), ...(route.changeRequestId ? { changeRequestId: route.changeRequestId } : {}) })}><Plus aria-hidden />Record a deployment</a>
              </Button>
            )}
            <Button variant="outline" asChild>
              <a href={hrefTo({ page: 'places' })}><MapPin aria-hidden />Places</a>
            </Button>
          </>
        )}
      />

      {list.status === 'failed' ? (
        <div className="mb-4"><PanelFailure failure={list.failure} /></div>
      ) : (
        <div className="mb-4 grid grid-cols-6 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5" role="group" aria-label="Key figures for the chosen filters">
          <KpiTile label="Deployments" value={counts?.records ?? null} caption={params.latest === '1' ? 'Latest per application and target' : 'Records, corrections included'} onClick={() => go({ view: undefined, flagKeys: undefined })} />
          <KpiTile label="Flagged" value={counts?.flagged ?? null} caption="At least one flag" attention={!!counts?.flagged} onClick={() => go({ view: 'flagged', flagKeys: undefined })} />
          {FLAG_FIGURES.map(f => {
            const value = counts ? counts[FLAG_FIELD[f.flag]] : null;
            return (
              <KpiTile
                key={f.flag}
                label={flagText[FLAG_FIELD[f.flag]]}
                value={value}
                caption={f.caption}
                attention={!!value}
                onClick={() => go({ view: 'flagged', flagKeys: [f.flag] })}
              />
            );
          })}
        </div>
      )}

      <section aria-label="Filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <SwitchTabs<'all' | 'flagged'>
            label="Show"
            ariaLabel="Which deployments"
            value={view}
            onChange={v => go({ view: v === 'all' ? undefined : v, ...(v === 'all' ? { flagKeys: undefined } : {}) })}
            options={[{ value: 'all', label: 'All' }, { value: 'flagged', label: 'Flagged' }]}
          />
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" className={cn('gap-1.5', flagKeys.length && 'border-primary text-primary')}>
                <ListFilter aria-hidden />
                Flag
                {flagKeys.length > 0 && <span className="rounded-full bg-primary px-1.5 text-[11px] leading-4 text-primary-foreground">{flagKeys.length}</span>}
                <ChevronDown className="size-3.5 opacity-70" aria-hidden />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="min-w-[240px]">
              <DropdownMenuLabel>Any of these flags</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {DEPLOYMENT_FLAG_KEYS.map(k => (
                <DropdownMenuCheckboxItem key={k} checked={flagKeys.includes(k)} onCheckedChange={() => toggleFlag(k)} onSelect={e => e.preventDefault()}>
                  {flagText[FLAG_FIELD[k]]}
                </DropdownMenuCheckboxItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
          <ListSearch label="Search the deployments by application, artifact, version, target or AI client" placeholder="Application, version or target" />
          <div className="flex items-center gap-2 narrow:w-full">
            <Checkbox id="deployments-latest" checked={params.latest === '1'} onCheckedChange={v => go({}, { ...params, latest: v === true ? '1' : undefined })} />
            <Label htmlFor="deployments-latest" className="font-normal">Latest per application and target only</Label>
          </div>
        </div>
        <div className="grid grid-cols-5 gap-3 medium:grid-cols-2 narrow:grid-cols-1">
          <FilterSelect
            label="Portfolio"
            value={params.portfolio ?? 'all'}
            onChange={v => go({}, { ...params, portfolio: v === 'all' ? undefined : v })}
            options={[{ value: 'all', label: 'Every portfolio' }, ...portfolioItems.map(p => ({ value: p.portfolioId.toLowerCase(), label: p.name })), ...(params.portfolio && !portfolioItems.some(p => sameId(p.portfolioId, params.portfolio)) ? [{ value: params.portfolio, label: 'The portfolio chosen' }] : [])]}
          />
          <FilterSelect
            label="Target"
            value={route.targetId ?? 'all'}
            onChange={v => go({ targetId: v === 'all' ? undefined : v })}
            options={[{ value: 'all', label: 'Every target' }, ...targetChoices(targets.items), ...(route.targetId && !targets.items.some(t => sameId(t.deploymentTargetId, route.targetId)) ? [{ value: route.targetId, label: targetName }] : [])]}
          />
          <FilterSelect
            label="Application"
            value={route.applicationId ?? 'all'}
            onChange={v => go({ applicationId: v === 'all' ? undefined : v })}
            options={[{ value: 'all', label: 'Every application' }, ...apps.map(a => ({ value: a.id, label: a.name })), ...(route.applicationId && !apps.some(a => sameId(a.id, route.applicationId)) ? [{ value: route.applicationId, label: appName }] : [])]}
          />
          <FilterSelect
            label="Finished"
            value={whenValue}
            onChange={setWhen}
            options={[{ value: 'any', label: 'Any time' }, ...Object.entries(WITHIN_TEXT).map(([value, label]) => ({ value, label })), { value: 'custom', label: 'Between two dates' }]}
          />
          <FilterSelect
            label="Result"
            value={params.result ?? 'any'}
            onChange={v => go({}, { ...params, result: v === 'any' ? undefined : v })}
            options={[{ value: 'any', label: 'Any result' }, ...RESULT_MEANINGS.map(m => ({ value: m, label: RESULT_TEXT[m] }))]}
          />
        </div>
        {whenValue === 'custom' && (
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1">
              <Label htmlFor="deployments-from" className="text-xs font-normal text-navy-3">From</Label>
              <Input id="deployments-from" type="date" className="w-[170px]" value={params.from ?? ''} onChange={e => go({}, { ...params, within: undefined, from: DATE_PARAM.test(e.target.value) ? e.target.value : undefined })} />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="deployments-to" className="text-xs font-normal text-navy-3">To</Label>
              <Input id="deployments-to" type="date" className="w-[170px]" value={params.to ?? ''} onChange={e => go({}, { ...params, within: undefined, to: DATE_PARAM.test(e.target.value) ? e.target.value : undefined })} />
            </div>
            {range.from && range.to && range.from > range.to && <Muted className="pb-2 text-warning">The start is after the end, so nothing matches.</Muted>}
          </div>
        )}
        {anyFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {view === 'flagged' && flagKeys.length === 0 && <Chip label="Flagged" onRemove={() => go({ view: undefined })} />}
            {flagKeys.map(k => <Chip key={k} label={`Flag: ${flagText[FLAG_FIELD[k]]}`} onRemove={() => toggleFlag(k)} />)}
            {params.portfolio && <Chip label={`Portfolio: ${portfolioName}`} onRemove={() => go({}, { ...params, portfolio: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => go({}, { ...params, q: undefined })} />}
            {route.targetId && <Chip label={`Target: ${targetName}`} onRemove={() => go({ targetId: undefined })} />}
            {route.applicationId && <Chip label={`Application: ${appName}`} onRemove={() => go({ applicationId: undefined })} />}
            {route.changeRequestId && <Chip label={`Change: ${dataOf(change)?.change.title ?? 'reading the title'}`} onRemove={() => go({ changeRequestId: undefined })} />}
            {params.within && <Chip label={WITHIN_TEXT[params.within]} onRemove={() => go({}, { ...params, within: undefined })} />}
            {range.from && !params.within && <Chip label={`From ${fmtDate(range.from)}`} onRemove={() => go({}, { ...params, from: undefined })} />}
            {range.to && <Chip label={`To ${fmtDate(range.to)}`} onRemove={() => go({}, { ...params, to: undefined })} />}
            {params.result && <Chip label={`Result: ${RESULT_TEXT[params.result as DeploymentResultMeaning]}`} onRemove={() => go({}, { ...params, result: undefined })} />}
            {params.latest === '1' && <Chip label="Latest only" onRemove={() => go({}, { ...params, latest: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearAll}>Clear all</Button>
          </div>
        )}
      </section>

      <div className={twoColumns}>
        <Panel
          title={view === 'flagged' || flagKeys.length ? 'Flagged deployments' : 'Deployments'}
          count={text ? items.length : data?.totalCount}
          subtitle={params.latest === '1' ? 'The latest record per application and target, newest first' : 'Every record, newest first'}
          state={list.status === 'done' ? { status: 'done', envelope: list.first } : list.status === 'loading' ? { status: 'loading' } : { status: 'done', envelope: list.failure }}
          skeletonRows={8}
        >
          {() => (
            <>
              <DeploymentRows
                items={items}
                corrected={corrected}
                followUp={d => {
                  const covering = exceptionCovering(exceptions, d.deploymentRecordId, d.businessApplicationId);
                  if (covering) {
                    return (
                      <TextLink href={recordHrefOf('decision', covering.decisionId) ?? '#'}>
                        {covering.status.meaning === 'proposed' ? 'Exception waiting' : 'Covered by an exception'}
                      </TextLink>
                    );
                  }
                  return mayPropose ? (
                    <Button variant="outline" size="sm" className="h-7" onClick={() => setSelected(d)} aria-label={`Propose an exception for ${d.applicationName ?? d.artifactName} ${d.artifactVersion}`}>
                      Propose an exception
                    </Button>
                  ) : null;
                }}
                empty={(
                  <span className="flex flex-col items-start gap-2">
                    <span>{anyFilter ? 'No deployment matches these filters.' : 'No deployment is recorded yet. Pipelines record theirs; a person records one by hand.'}</span>
                    {anyFilter && <Button variant="outline" size="sm" onClick={clearAll}>Clear the filters</Button>}
                  </span>
                )}
              />
              {text && list.status === 'done' && list.skipToken && <Muted>The search covers the deployments read so far. Show more to search further.</Muted>}
              {list.status === 'done' && list.laterFailure && <Muted>{`More rows could not be read: ${list.laterFailure.error.message}`}</Muted>}
              {list.status === 'done' && <ShowMore shown={items.length} skipToken={list.skipToken} loading={list.loadingMore} onMore={list.more} what="deployments" />}
            </>
          )}
        </Panel>

        <div className={column}>
          <Panel title="Flagged by target" subtitle="Every record each target holds, whatever the filters" state={targets.read.status === 'done' ? { status: 'done', envelope: targets.read.first } : targets.read.status === 'loading' ? { status: 'loading' } : { status: 'done', envelope: targets.read.failure }} notes={[]}>
            {() => (flagged.length ? (
              <BarList
                ariaLabel="Flagged deployments by target"
                noun={['flagged deployment', 'flagged deployments']}
                labelWidth={170}
                items={flagged.map(t => ({ key: t.deploymentTargetId.toLowerCase(), label: `${t.label} (${t.targetStage.label})`, count: t.flaggedCount }))}
                onDrill={b => b.key && go({ view: 'flagged', flagKeys: undefined, targetId: b.key })}
              />
            ) : <Empty>No target holds a flagged deployment.</Empty>)}
          </Panel>

          <Panel title="What the flags mean" subtitle="The engine works them out. Nobody can set or clear one.">
            <ul className="m-0 flex list-none flex-col gap-3 p-0">
              {FLAG_ORDER.map(f => (
                <li key={f} className="flex flex-col gap-1">
                  <span className="flex flex-wrap items-center gap-2">
                    <ToneBadge tone="warning">{flagText[f]}</ToneBadge>
                    <TextLink href={withParams(hrefTo({ page: 'deployments', view: 'flagged', flagKeys: [FLAG_KEY_OF[f]] }), params)} className="text-xs">Show these</TextLink>
                  </span>
                  <span className="text-xs leading-relaxed text-navy-2">{flagExplanation(flagHelp[f], flagFollowUp[f])}</span>
                </li>
              ))}
            </ul>
            <div className="flex flex-col gap-1.5">
              <h3 className={subhead}>Guidelines they put into practice</h3>
              {[...new Set(FLAG_ORDER.map(f => flagGuideline[f]))].map(g => <GuidelineChip key={g} number={g} />)}
            </div>
          </Panel>

          {route.targetId && (
            <Panel title="This target">
              {(() => {
                const t = targets.items.find(x => sameId(x.deploymentTargetId, route.targetId));
                if (!t) return <Muted>{targets.read.status === 'loading' ? 'Reading the target.' : 'The target is not among those you can see.'}</Muted>;
                const href = placeHref(t.deploymentTargetId);
                return (
                  <div className="flex flex-col gap-1 text-sm text-navy-2">
                    {href ? <TextLink href={href}>{t.label}</TextLink> : <span className="font-semibold text-navy">{t.label}</span>}
                    <span>{`${t.targetStage.label}. Change path ${t.changePath.label.toLowerCase()}: ${routeWords(t.changePath)}.`}</span>
                  </div>
                );
              })()}
            </Panel>
          )}
        </div>
      </div>

      {selected && (
        <ProposeExceptionSheet
          key={selected.deploymentRecordId}
          deployment={selected}
          open
          onOpenChange={open => { if (!open) setSelected(null); }}
        />
      )}
    </>
  );
}

// A labeled select in the filter bar.
function FilterSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (value: string) => void; options: { value: string; label: string }[] }) {
  const id = `deployments-filter-${label.toLowerCase()}`;
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <span className="text-xs text-navy-3" id={id}>{label}</span>
      <Select value={value} onValueChange={onChange}>
        <SelectTrigger className="w-full" aria-labelledby={id}><SelectValue /></SelectTrigger>
        <SelectContent className="max-h-[320px]">
          {options.map(o => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Record a deployment by hand (path C, D-19)
// ---------------------------------------------------------------------------------------------------------------------

export function DeploymentNew({ route }: { route: RouteOf<'deployment-new'> }) {
  const session = useSession();
  const routeWords = useRouteWords();
  const allowedRoutes = useAllowedRoutes();
  const change = useRead('get_change_request', route.changeRequestId ? { changeRequestId: route.changeRequestId } : null);
  const changeApp = dataOf(change)?.change.businessApplicationId.toLowerCase() ?? null;
  const appId = route.applicationId ?? changeApp;
  const card = useRead('get_application_card', appId ? { businessApplicationId: appId, sections: ['deployments'] } : null);
  const app = dataOf(card)?.application ?? null;
  // Only the targets in service: a retired target refuses new deployments (deployment.target-retired).
  const targets = useTargets({ state: 'active' });
  const [recorded, setRecorded] = useState<RecordDeploymentResult[]>([]);
  const allowed = session.can('record_deployment');
  const changeTitle = dataOf(change)?.change.title ?? null;
  const pick = (a: AppChoice) => goTo({ page: 'deployment-new', applicationId: a.id }, true);
  const waitingForChange = !route.applicationId && !!route.changeRequestId && change.status === 'loading';

  return (
    <>
      <PageHeader
        eyebrow="Deployments"
        title="Record a deployment"
        subtitle="Say what reached which target and how it ended. The engine records the route as a person in the app, works out the flags and applies the gate level of the target's stage, as it does for a pipeline's record."
        meta={app ? <span>{`For ${app.name}${changeTitle ? `, change ${changeTitle}` : ''}`}</span> : undefined}
        actions={(
          <Button variant="outline" asChild>
            <a href={hrefTo({ page: 'deployments', ...(appId ? { applicationId: appId } : {}) })}>All deployments</a>
          </Button>
        )}
      />
      {session.known && !allowed && (
        <Panel title="You cannot record deployments" className="mb-4">
          <p className={plainText}>
            Your roles do not include recording deployments. People with the maker, application owner or change manager
            role record them. Pipelines record theirs through their own record step.
          </p>
        </Panel>
      )}
      <ol className="m-0 flex list-none flex-col gap-4 p-0" aria-label="Steps">
        <li>
          <Panel title="1. The application" subtitle="The application whose version reached a target.">
            {waitingForChange ? <Muted>Reading the change.</Muted>
              : appId ? (
                card.status === 'loading' ? <Muted>Reading the application.</Muted>
                  : card.status === 'done' && !card.envelope.ok ? <PanelFailure failure={card.envelope} />
                    : app && (
                      <div className="flex flex-col gap-2">
                        <span className="text-sm text-navy">
                          <TextLink href={hrefTo({ page: 'app', id: app.businessApplicationId.toLowerCase() })}>{app.name}</TextLink>
                          <span className="text-navy-3">{` ${[app.tier?.label ?? 'No tier yet', app.stage?.label].filter(Boolean).join('. ')}.`}</span>
                        </span>
                        <Button variant="link" size="sm" className="h-7 self-start px-0" onClick={() => goTo({ page: 'deployment-new' }, true)}>Choose another application</Button>
                      </div>
                    )
              ) : <AppPicker onPick={pick} />}
          </Panel>
        </li>
        {appId && app && (
          <li>
            <Panel
              title="2. What reached the target"
              subtitle="Where it runs now, and the targets a person may record a deployment to without a flag."
              actions={allowed ? (
                <RecordDeploymentSheet
                  app={{ id: app.businessApplicationId.toLowerCase(), name: app.name }}
                  targets={targets.items}
                  changeRequestId={route.changeRequestId}
                  trigger={<Button><Plus aria-hidden />{recorded.length ? 'Record another' : 'Record the deployment'}</Button>}
                  onRecorded={d => setRecorded(list => [d, ...list])}
                />
              ) : undefined}
            >
              <div className="grid grid-cols-2 gap-4 medium:grid-cols-1">
                <div className="flex min-w-0 flex-col gap-1.5">
                  <h3 className={subhead}>Where it runs now</h3>
                  {(dataOf(card)?.deployments ?? []).length ? (
                    <RowList label="Latest deployment per target">
                      {(dataOf(card)?.deployments ?? []).map(d => {
                        const href = withAppHint(recordHrefOf('deployment-record', d.deploymentRecordId), app.businessApplicationId);
                        return (
                          <Row
                            key={d.deploymentRecordId}
                            primary={href ? <TextLink href={href}>{d.targetLabel}</TextLink> : d.targetLabel}
                            secondary={`${d.artifactName} ${d.artifactVersion}${d.finishedOn ? `. ${fmtDate(d.finishedOn)}` : ''}`}
                            end={<StatusBadge status={d.result} />}
                          />
                        );
                      })}
                    </RowList>
                  ) : <Empty>Nothing is recorded as deployed yet.</Empty>}
                </div>
                <div className="flex min-w-0 flex-col gap-1.5">
                  <h3 className={subhead}>Targets and the routes they allow</h3>
                  {targets.read.status === 'loading' ? <Muted>Reading the targets.</Muted>
                    : targets.read.status === 'failed' ? <PanelFailure failure={targets.read.failure} />
                      : targets.items.length ? (
                        <RowList label="Targets">
                          {[...targets.items].sort((a, b) => a.targetStage.rank - b.targetStage.rank || a.label.localeCompare(b.label)).map(t => {
                            const ok = allowedRoutes(t.changePath).includes('person-in-app');
                            return (
                              <Row
                                key={t.deploymentTargetId}
                                primary={placeHref(t.deploymentTargetId) ? <TextLink href={placeHref(t.deploymentTargetId)!}>{t.label}</TextLink> : t.label}
                                secondary={`${t.targetStage.label}. ${t.changePath.label}: ${routeWords(t.changePath)}.`}
                                end={ok ? <ToneBadge tone="success">A person may record</ToneBadge> : <ToneBadge tone="warning">Flagged if a person records</ToneBadge>}
                                wideEnd
                              />
                            );
                          })}
                        </RowList>
                      ) : <Empty>No target is registered yet. A platform admin registers one on the Places page.</Empty>}
                </div>
              </div>
            </Panel>
          </li>
        )}
        {recorded.length > 0 && app && (
          <li>
            <Panel title="3. Recorded" count={recorded.length} subtitle="Open a record to see each flag explained, or follow it up.">
              <RowList label="Recorded deployments">
                {recorded.map((r, i) => {
                  const href = r.deploymentRecordId ? withAppHint(recordHrefOf('deployment-record', r.deploymentRecordId), app.businessApplicationId) : null;
                  const raised = FLAG_ORDER.filter(f => r.flags[f]);
                  return (
                    <Row
                      key={r.deploymentRecordId ?? i}
                      lead={<span className="grid size-8 place-items-center rounded-full bg-success-soft text-success" aria-hidden><CircleCheck className="size-4" /></span>}
                      primary={href ? <TextLink href={href}>Open the deployment record</TextLink> : 'Recorded'}
                      secondary={raised.length ? `Flagged: ${raised.map(f => flagText[f].toLowerCase()).join(', ')}.` : 'No flag raised.'}
                      tertiary={`Gate level ${gateLevelLabel[r.gateLevelApplied] ?? r.gateLevelApplied}.`}
                      end={r.status ? <StatusBadge status={r.status} /> : undefined}
                    />
                  );
                })}
              </RowList>
              {recorded.some(r => FLAG_ORDER.some(f => r.flags[f])) && (
                <p className="m-0 flex items-center gap-1.5 text-sm text-navy-2">
                  <TriangleAlert className="size-4 shrink-0 text-warning" aria-hidden />
                  {`${plural(recorded.filter(r => FLAG_ORDER.some(f => r.flags[f])).length, 'record is', 'records are')} flagged. Follow up from the record: propose an exception or draft a change after the fact.`}
                </p>
              )}
            </Panel>
          </li>
        )}
      </ol>
    </>
  );
}
