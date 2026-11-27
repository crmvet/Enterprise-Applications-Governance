// Places (screen "Places (environments, targets, pipelines)", priority 2): where applications are built and run, the
// 2021 Environment table's successor.
//   /app/places?view=targets|pipelines&stage=<key>   RouteOf<'places'>; the page's own: kind=<key>, q=<text>, stale=1,
//                                                    state=active|retired (in service or retired; left out for both)
//   /app/places/<targetId>                           one deployment target. The record link the tools return,
//                                                    /app/records/deployment-target/<id>, opens this page too.
// Reads: list_deployment_targets (targets by kind and stage, change path, last seen, apps and versions deployed, the
// pipelines; external IDs hidden from a reader), get_pipeline_map (each pipeline's order of targets), get_deployments
// (the records of one target).
// Writes, each in the shared side sheet: save_deployment_target (a person only, platform admin: register a target,
// change one, retire one with a reason or return it to service), map_pipeline (application owner or platform admin).
//
// Design choices (D-41), recorded here:
// - The page reads every target once (up to 500) and filters on the page, so the figures and the lists always agree.
// - Targets list by stage, in the configured stage order, because the stage decides the gate level a deployment meets.
// - "Not seen for 30 days" marks a target in service whose last seen date is older than 30 days, or that was never seen:
//   a place nobody deploys to any more is a retirement candidate. A retired target is never counted there.
// - Retire and return to service live on the target's own page, as every action lives on the record it changes. A
//   retired target stays in the list, marked with the day and the reason, and keeps its history; the state filter
//   narrows the list to the targets in service or the retired ones (wave 3, document 24 B24-18).
// - Wave 4: the pipelines view reads every pipeline in one call (get_pipeline_map with no input, B24-21), a pipeline
//   mapped to no target too, so a pipeline registered and never mapped shows with "Not mapped to a target yet". The
//   stage, kind, state and "not seen" filters keep the pipelines that reach a target they show; the search also finds a
//   pipeline by its name, its ID, an application it deploys or a target it reaches.
// - Wave 4: a target lists its data locations (list_deployment_targets dataLocations, B24-22), each opening its record,
//   with "What depends on it?" from the location.
import { Archive, GitBranch, Plus, RotateCcw, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import type { RouteOf } from '../app/routes';
import type { DeploymentTargetItem } from '../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../data/format';
import { useLabels } from '../data/labels';
import { usePagedRead, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf } from '../data/toolClient';
import { useHost } from '../harness';
import { BarList, DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { withAppHint } from './changes-records/logic';
import { Fact, Facts, LookupState, mono, recordGrid } from './changes-records/parts';
import { daysSince, entriesByApp, groupByStage, isRetired, pipelinesFromTargets, sameId, TARGET_STATE_PARAM, targetsInState, type PipelineSeen, type TargetState } from './deployments-places/logic';
import { appHref, Chip, column, DeploymentRows, placeHref, plainText, twoColumns, useRouteWords, useTargets } from './deployments-places/parts';
import { MapPipelineSheet, RetireTargetSheet, TargetSheet } from './deployments-places/sheets';
import { WhatDependsOnIt } from './data-records/WhatDependsOnIt';
import { matchesText } from '../data/textSearch';
import type { PipelineMap } from '../data/contracts';

const KEY = /^[a-z][a-z0-9-]{0,63}$/;
const TEXT = /^.{1,200}$/;
const STALE_DAYS = 30;

// A target in service not seen for 30 days, or never seen. A retired target is not a candidate any more.
const isStale = (t: Pick<DeploymentTargetItem, 'lastSeenOn' | 'retiredOn'>, today: string) => {
  if (isRetired(t)) return false;
  const days = daysSince(t.lastSeenOn, today);
  return days === null || days > STALE_DAYS;
};

const STATE_TEXT: Record<TargetState, string> = { all: 'All', active: 'In service', retired: 'Retired' };

export function Places({ route }: { route: RouteOf<'places'> }) {
  const host = useHost();
  const session = useSession();
  const stages = useLabels('target-stage');
  const routeWords = useRouteWords();
  const view = route.view ?? 'targets';
  const kind = useQueryParam('kind', KEY);
  const text = useQueryParam('q', TEXT);
  const stale = useQueryParam('stale', /^1$/) === '1';
  const state = (useQueryParam('state', TARGET_STATE_PARAM) as TargetState | undefined) ?? 'all';
  const targets = useTargets();
  const all = targets.items;
  const stageKeys = route.stageKeys ?? [];
  const [search, setSearch] = useState(text ?? '');

  type Params = { kind?: string; q?: string; stale?: string; state?: string };
  const params: Params = { kind, q: text, stale: stale ? '1' : undefined, state: state === 'all' ? undefined : state };
  const go = (next: { view?: 'targets' | 'pipelines'; stageKeys?: string[] }, nextParams: Params = params) => {
    const v = 'view' in next ? next.view : route.view;
    const s = 'stageKeys' in next ? next.stageKeys : route.stageKeys;
    navigate(withParams(hrefTo({ page: 'places', ...(v && v !== 'targets' ? { view: v } : {}), ...(s?.length ? { stageKeys: s } : {}) }), nextParams), true);
  };

  // The search box writes the address once the person stops typing.
  useEffect(() => {
    const t = window.setTimeout(() => {
      const value = search.trim().slice(0, 200) || undefined;
      if (value !== text) go({}, { ...params, q: value });
    }, 350);
    return () => window.clearTimeout(t);
  }, [search]);
  // The box follows the address when it changes from elsewhere (Back, Clear all).
  useEffect(() => { setSearch(text ?? ''); }, [text]);

  const byFilters = targetsInState(all, state).filter(t => (!stageKeys.length || stageKeys.includes(t.targetStage.key))
    && (!kind || t.targetKind?.key === kind)
    && (!stale || isStale(t, host.today)));
  const shown = byFilters.filter(t => matchesText(text, t.label, t.externalId, t.targetKind?.label, t.applications.map(a => a.name), t.dataLocations?.map(l => l.label)));
  const targetFiltered = stageKeys.length > 0 || !!kind || stale || state !== 'all';
  const stageOrder = stages.keys().length ? stages.keys() : [...new Set(all.map(t => t.targetStage.key))];
  const flaggedTotal = all.reduce((n, t) => n + t.flaggedCount, 0);
  const staleCount = all.filter(t => isStale(t, host.today)).length;
  const retiredCount = targetsInState(all, 'retired').length;
  const kinds = [...new Map(all.filter(t => t.targetKind).map(t => [t.targetKind!.key, t.targetKind!.label])).entries()].sort((a, b) => a[1].localeCompare(b[1]));
  const anyFilter = stageKeys.length > 0 || !!kind || !!text || stale || state !== 'all';
  const clearAll = () => { setSearch(''); go({ stageKeys: undefined }, {}); };
  const loaded = targets.read.status === 'done';
  // Every pipeline, mapped or not, in one read while the pipelines view is open.
  const pipelineMap = useRead('get_pipeline_map', view === 'pipelines' ? {} : null);
  const mapData = dataOf(pipelineMap);
  const inView = new Set(byFilters.map(t => t.deploymentTargetId.toLowerCase()));
  const allPipelines = mapData?.pipelines ?? [];
  const pipelines = allPipelines
    .filter(p => !targetFiltered || p.entries.some(e => inView.has(e.deploymentTargetId.toLowerCase())))
    .filter(p => matchesText(text, p.label, p.externalId, p.entries.map(e => e.applicationName), p.entries.map(e => e.targetLabel)))
    .sort((a, b) => (a.label ?? a.externalId).localeCompare(b.label ?? b.externalId));
  const pipelinesLoaded = pipelineMap.status === 'done';
  const pipelineFailure = pipelineMap.status === 'done' && !pipelineMap.envelope.ok ? pipelineMap.envelope : null;

  return (
    <>
      <PageHeader
        eyebrow="Register"
        title="Places"
        subtitle="Where applications are built and run: the deployment targets by stage, what runs on each, and the pipelines that reach them."
        meta={<DataStamp envelope={targets.read.status === 'done' ? targets.read.first : undefined} />}
        actions={(
          <>
            {session.can('save_deployment_target') && <TargetSheet trigger={<Button><Plus aria-hidden />Register a target</Button>} />}
            {session.can('map_pipeline') && <MapPipelineSheet targets={targetsInState(all, 'active')} trigger={<Button variant="outline"><GitBranch aria-hidden />Map a pipeline</Button>} />}
          </>
        )}
      />

      {targets.read.status === 'failed' ? (
        <div className="mb-4"><PanelFailure failure={targets.read.failure} /></div>
      ) : (
        <div className="mb-4 grid grid-cols-7 gap-3 medium:not-narrow:grid-cols-4 narrow:grid-cols-2 narrow:gap-2.5" role="group" aria-label="Key figures">
          <KpiTile label="Targets" value={targets.total} caption={loaded ? `Registered places, ${all.length - retiredCount} in service` : 'Registered places'} onClick={() => go({ view: 'targets', stageKeys: undefined }, {})} />
          {stageOrder.slice(0, 3).map(k => (
            <KpiTile
              key={k}
              label={stages.label(k)}
              value={loaded ? all.filter(t => t.targetStage.key === k).length : null}
              caption={`${stages.label(k)} targets`}
              onClick={() => go({ view: 'targets', stageKeys: [k] }, {})}
            />
          ))}
          <KpiTile label={`Not seen for ${STALE_DAYS} days`} value={loaded ? staleCount : null} caption="In service, or never seen" attention={staleCount > 0} onClick={() => go({ view: 'targets', stageKeys: undefined }, { stale: '1' })} />
          <KpiTile label="Retired" value={loaded ? retiredCount : null} caption="Records kept, no new deployments" onClick={() => go({ view: 'targets', stageKeys: undefined }, { state: 'retired' })} />
          <KpiTile label="Flagged deployments" value={loaded ? flaggedTotal : null} caption="On every target" attention={flaggedTotal > 0} onClick={() => navigate(hrefTo({ page: 'deployments', view: 'flagged' }))} />
        </div>
      )}

      <section aria-label="Filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2.5">
          <SwitchTabs<'targets' | 'pipelines'>
            label="Show"
            ariaLabel="Targets or pipelines"
            value={view}
            onChange={v => go({ view: v })}
            options={[{ value: 'targets', label: 'Targets' }, { value: 'pipelines', label: 'Pipelines' }]}
          />
          <SwitchTabs<string>
            label="Stage"
            ariaLabel="Stage"
            value={stageKeys.length === 1 ? stageKeys[0] : 'all'}
            onChange={v => go({ stageKeys: v === 'all' ? undefined : [v] })}
            options={[{ value: 'all', label: 'All' }, ...stageOrder.map(k => ({ value: k, label: stages.label(k) }))]}
          />
          <SwitchTabs<TargetState>
            label="State"
            ariaLabel="In service or retired"
            value={state}
            onChange={v => go({}, { ...params, state: v === 'all' ? undefined : v })}
            options={(['all', 'active', 'retired'] as const).map(v => ({ value: v, label: STATE_TEXT[v] }))}
          />
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <span className="text-xs text-navy-3" id="places-kind-label">Kind</span>
            <Select value={kind ?? 'all'} onValueChange={v => go({}, { ...params, kind: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[240px] narrow:w-full" aria-labelledby="places-kind-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Every kind</SelectItem>
                {kinds.map(([k, label]) => <SelectItem key={k} value={k}>{label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="relative w-[280px] narrow:w-full">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
            <Input type="search" value={search} onChange={e => setSearch(e.target.value)} placeholder="Name, ID or application" aria-label="Search the targets by name, ID or application" className="pl-8" maxLength={200} />
          </div>
          <div className="flex items-center gap-2 pb-2">
            <Checkbox id="places-stale" checked={stale} onCheckedChange={v => go({}, { ...params, stale: v === true ? '1' : undefined })} />
            <Label htmlFor="places-stale" className="font-normal">{`Only targets not seen for ${STALE_DAYS} days`}</Label>
          </div>
        </div>
        {anyFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {stageKeys.map(k => <Chip key={k} label={`Stage: ${stages.label(k)}`} onRemove={() => go({ stageKeys: stageKeys.filter(x => x !== k) })} />)}
            {kind && <Chip label={`Kind: ${kinds.find(([k]) => k === kind)?.[1] ?? kind}`} onRemove={() => go({}, { ...params, kind: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => { setSearch(''); go({}, { ...params, q: undefined }); }} />}
            {stale && <Chip label={`Not seen for ${STALE_DAYS} days`} onRemove={() => go({}, { ...params, stale: undefined })} />}
            {state !== 'all' && <Chip label={`State: ${STATE_TEXT[state]}`} onRemove={() => go({}, { ...params, state: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearAll}>Clear all</Button>
          </div>
        )}
      </section>

      {view === 'targets' ? (
        <div className={twoColumns}>
          <div className={column}>
            {targets.read.status === 'loading' ? (
              <Panel title="Targets" state={{ status: 'loading' }} skeletonRows={8}>{() => null}</Panel>
            ) : targets.read.status === 'failed' ? null : shown.length === 0 ? (
              <Panel title="Targets" count={0}>
                <span className="flex flex-col items-start gap-2">
                  <Empty>{anyFilter ? 'No target matches these filters.' : 'No target is registered yet. A platform admin registers the places deployments land; a deployment can be recorded only to a registered target.'}</Empty>
                  {anyFilter && <Button variant="outline" size="sm" onClick={clearAll}>Clear the filters</Button>}
                </span>
              </Panel>
            ) : groupByStage(shown).map(g => (
              <Panel key={g.stage.key} title={g.stage.label} count={g.items.length} subtitle={`${g.stage.label} targets, by name`}>
                <RowList label={`${g.stage.label} targets`}>
                  {g.items.map(t => <TargetRow key={t.deploymentTargetId} target={t} today={host.today} routeWords={routeWords} />)}
                </RowList>
              </Panel>
            ))}
            {targets.read.status === 'done' && targets.read.skipToken && <Muted>The product holds more than 500 targets. Narrow the search to find the rest.</Muted>}
          </div>
          <div className={column}>
            <Panel title="Targets by kind" state={targets.read.status === 'done' ? { status: 'done', envelope: targets.read.first } : { status: 'loading' }} notes={[]}>
              {() => (kinds.length ? (
                <BarList
                  ariaLabel="Targets by kind"
                  noun={['target', 'targets']}
                  labelWidth={170}
                  items={kinds.map(([k, label]) => ({ key: k, label, count: all.filter(t => t.targetKind?.key === k).length }))}
                  onDrill={b => b.key && go({}, { ...params, kind: b.key })}
                />
              ) : <Empty>No target to count.</Empty>)}
            </Panel>
            <ChangePaths />
          </div>
        </div>
      ) : (
        <div className={twoColumns}>
          <Panel title="Pipelines" count={pipelinesLoaded && !pipelineFailure ? pipelines.length : undefined} subtitle="Every pipeline the product knows, with the order it deploys each application in">
            {pipelineFailure ? <PanelFailure failure={pipelineFailure} />
              : !pipelinesLoaded || !loaded ? <Muted>Reading the pipelines and their maps.</Muted>
                : pipelines.length ? (
                  <ul className="m-0 flex list-none flex-col p-0">
                    {pipelines.map(p => <PipelineBlock key={p.pipelineReferenceId} pipeline={{ pipelineReferenceId: p.pipelineReferenceId, label: p.label ?? p.externalId, url: p.url, targets: [] }} known={p} targets={all} />)}
                  </ul>
                ) : <Empty>{anyFilter ? 'No pipeline matches these filters.' : 'No pipeline is known yet. Map one to say which targets it deploys each application to.'}</Empty>}
          </Panel>
          <div className={column}>
            <Panel title="How the map is used">
              <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm leading-relaxed text-navy-2">
                <li>A pipeline's record step reports each deployment it makes. The map says which applications and targets it reaches, in order.</li>
                <li>A deployment that reaches a pipeline-only target by another route is flagged.</li>
                <li>The product stores the map and the link. It never runs a pipeline.</li>
              </ul>
              <Muted>A pipeline that maps no target yet shows with no order. Change its map to say where it deploys.</Muted>
            </Panel>
          </div>
        </div>
      )}
    </>
  );
}

function TargetRow({ target: t, today, routeWords }: { target: DeploymentTargetItem; today: string; routeWords: ReturnType<typeof useRouteWords> }) {
  const href = placeHref(t.deploymentTargetId);
  const days = daysSince(t.lastSeenOn, today);
  const staleNow = isStale(t, today);
  const retired = isRetired(t);
  return (
    <Row
      wideEnd
      primary={href ? <TextLink href={href}>{t.label}</TextLink> : t.label}
      secondary={`${t.targetKind?.label ?? 'Kind not stated'}. ${t.changePath.label}: ${routeWords(t.changePath)}.`}
      tertiary={[
        retired ? `Retired ${fmtDate(t.retiredOn)}${t.retiredReason ? `: ${t.retiredReason}` : ''}` : null,
        t.lastSeenOn ? `Last seen ${fmtDate(t.lastSeenOn)}` : 'Never seen',
        t.applications.length ? `${retired ? 'Ran' : 'Runs'} ${plural(t.applications.length, 'application', 'applications')}` : 'Nothing deployed yet',
        t.pipelines.length ? `Reached by ${t.pipelines.map(p => p.label).join(', ')}` : null,
      ].filter(Boolean).join('. ')}
      badges={(retired || staleNow || t.isAiCreated || t.performedBy?.actorKind === 'ai-client-for-person') ? (
        <>
          {retired && <ToneBadge tone="subtle">Retired</ToneBadge>}
          {staleNow && <ToneBadge tone="warning">{days === null ? 'Never seen' : `Not seen for ${plural(days, 'day', 'days')}`}</ToneBadge>}
          <AiMark isAiCreated={t.isAiCreated} performedBy={t.performedBy} />
        </>
      ) : undefined}
      end={(
        <span className="flex flex-wrap items-center justify-end gap-2">
          <TextLink href={hrefTo({ page: 'deployments', targetId: t.deploymentTargetId.toLowerCase() })} className="text-sm">{plural(t.deploymentCount, 'deployment', 'deployments')}</TextLink>
          {t.flaggedCount > 0 && (
            <a href={hrefTo({ page: 'deployments', view: 'flagged', targetId: t.deploymentTargetId.toLowerCase() })} className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring" aria-label={`${plural(t.flaggedCount, 'flagged deployment', 'flagged deployments')} on ${t.label}`}>
              <ToneBadge tone="warning">{`${t.flaggedCount} flagged`}</ToneBadge>
            </a>
          )}
        </span>
      )}
    />
  );
}

// The change paths of the configuration in force: which routes each one allows. A target's change path decides which
// deployments to it are flagged as having come another way.
function ChangePaths() {
  const paths = useLabels('change-path');
  const routeWords = useRouteWords();
  const keys = paths.keys();
  return (
    <Panel title="Change paths" subtitle="The routes each path allows. Any other route is flagged.">
      {keys.length ? (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {keys.map(k => (
            <li key={k} className="flex flex-col gap-0.5">
              <span className="text-sm font-semibold text-navy">{paths.label(k)}</span>
              <span className="text-xs text-navy-2">{routeWords(paths.field<string[]>(k, 'allowedRouteKeys') ?? [])}</span>
            </li>
          ))}
        </ul>
      ) : <Muted>Reading the change paths.</Muted>}
    </Panel>
  );
}

// One pipeline with its map: each application it deploys and the targets in order, and the change to the map.
function PipelineBlock({ pipeline: p, targets, highlight, known }: { pipeline: PipelineSeen; targets: DeploymentTargetItem[]; highlight?: string; known?: PipelineMap['pipelines'][number] }) {
  const session = useSession();
  // The pipelines view read every map already; a target page reads the map of each pipeline that reaches it.
  const own = useRead('get_pipeline_map', known ? null : { pipelineReferenceId: p.pipelineReferenceId });
  const map = known ? { status: 'done' as const, envelope: { ok: true as const } } : own;
  const ref = known ?? dataOf(own)?.pipelines[0] ?? null;
  const groups = entriesByApp(ref?.entries ?? []);
  return (
    <li className="flex flex-col gap-2 border-t border-line py-3 first:border-t-0 first:pt-0.5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="text-sm font-semibold text-navy">
            {ref?.url ? <ExternalLink href={ref.url}>{ref.label ?? ref.externalId}</ExternalLink> : p.url ? <ExternalLink href={p.url}>{p.label}</ExternalLink> : p.label}
          </span>
          <span className="text-xs text-navy-3">
            {ref ? [ref.linkKind.label, ref.lastSeenOn ? `Last seen ${fmtDate(ref.lastSeenOn)}` : 'Never seen'].join('. ') : map.status === 'loading' ? 'Reading the map.' : null}
          </span>
          {ref?.isMoved && <ToneBadge tone="warning" className="self-start">{ref.movedToUrl ? 'Moved' : 'Moved, new place not known'}</ToneBadge>}
        </div>
        {ref && session.can('map_pipeline') && <MapPipelineSheet pipeline={ref} targets={targetsInState(targets, 'active')} trigger={<Button variant="outline" size="sm">Change the map</Button>} />}
      </div>
      {map.status === 'done' && !map.envelope.ok ? <PanelFailure failure={map.envelope} />
        : groups.length ? (
          <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
            {groups.map(g => {
              const href = appHref(g.businessApplicationId);
              return (
                <li key={g.businessApplicationId} className="flex flex-col gap-1">
                  <span className="text-sm text-navy">{href ? <TextLink href={href}>{g.applicationName}</TextLink> : g.applicationName}</span>
                  <ol className="m-0 flex list-none flex-wrap items-center gap-1 p-0" aria-label={`Where it deploys ${g.applicationName}, in order`}>
                    {g.steps.map((s, i) => {
                      const place = placeHref(s.deploymentTargetId);
                      const here = !!highlight && sameId(s.deploymentTargetId, highlight);
                      return (
                        <li key={`${s.deploymentTargetId}-${s.stageOrder}`} className="flex items-center gap-1">
                          {i > 0 && <span className="text-xs text-navy-3" aria-hidden>then</span>}
                          <span className={here ? 'rounded-md border border-primary bg-info-soft px-1.5 py-0.5 text-xs font-semibold text-brand-dark' : 'rounded-md border border-line px-1.5 py-0.5 text-xs text-navy'}>
                            {`${s.stageOrder}. `}
                            {place ? <TextLink href={place} className="text-xs">{s.targetLabel ?? 'A target'}</TextLink> : s.targetLabel ?? 'A target'}
                            {here && <span className="sr-only"> (this target)</span>}
                          </span>
                        </li>
                      );
                    })}
                  </ol>
                </li>
              );
            })}
          </ul>
        ) : map.status === 'done' ? <Muted>Not mapped to a target yet.</Muted> : null}
    </li>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// One deployment target
// ---------------------------------------------------------------------------------------------------------------------

export function PlacePage({ deploymentTargetId }: { deploymentTargetId: string }) {
  const has = (items: DeploymentTargetItem[]) => items.some(t => sameId(t.deploymentTargetId, deploymentTargetId));
  const scan = usePagedRead('list_deployment_targets', { top: 100 }, d => d.items, { until: has, maxPages: 10 });
  const items = scan.status === 'done' ? scan.items : [];
  const found = items.find(t => sameId(t.deploymentTargetId, deploymentTargetId)) ?? null;
  if (!found) {
    const done = scan.status === 'done';
    return (
      <>
        <PageHeader eyebrow="Deployment target" title={done ? 'Target not found' : scan.status === 'failed' ? 'The target cannot be shown' : 'Looking for the target'} />
        <LookupState
          title="The target"
          id={deploymentTargetId}
          pagesRead={scan.status === 'loading' ? scan.pagesRead : 0}
          failure={scan.status === 'failed' ? scan.failure : null}
          notFound={done ? (
            <p className={plainText}>
              {`The target is not among the ${plural(items.length, 'registered target', 'registered targets')}. It may have been registered under another ID, or the link may be old.`}
            </p>
          ) : undefined}
          next={<TextLink href={hrefTo({ page: 'places' })} className="self-start text-sm">All places</TextLink>}
        />
      </>
    );
  }
  return <PlaceView target={found} all={items} envelope={scan.status === 'done' ? scan.first : undefined} />;
}

function PlaceView({ target: t, all, envelope }: { target: DeploymentTargetItem; all: DeploymentTargetItem[]; envelope: Parameters<typeof DataStamp>[0]['envelope'] }) {
  const host = useHost();
  const session = useSession();
  const routeWords = useRouteWords();
  const recent = useRead('get_deployments', { deploymentTargetId: t.deploymentTargetId, latestOnly: false, top: 10 });
  const days = daysSince(t.lastSeenOn, host.today);
  const id = t.deploymentTargetId.toLowerCase();
  const pipelines = pipelinesFromTargets([t]);
  const retired = isRetired(t);
  return (
    <>
      <PageHeader
        eyebrow="Deployment target"
        title={t.label}
        status={(
          <span className="flex flex-wrap items-center gap-1.5">
            <ToneBadge tone="brand" icon={null}>{t.targetStage.label}</ToneBadge>
            {retired && <ToneBadge tone="subtle">{`Retired ${fmtDate(t.retiredOn)}`}</ToneBadge>}
          </span>
        )}
        subtitle={`${t.targetKind?.label ?? 'Kind not stated'}. Change path ${t.changePath.label.toLowerCase()}: ${routeWords(t.changePath)}.`}
        meta={(
          <span className="flex flex-col gap-1">
            <AiMark isAiCreated={t.isAiCreated} performedBy={t.performedBy} className="self-start" />
            <StampLine performedBy={t.performedBy} />
            <DataStamp envelope={envelope} />
          </span>
        )}
        actions={(
          <>
            {session.can('save_deployment_target') && <TargetSheet existing={t} trigger={<Button variant="outline">Change the target</Button>} />}
            {session.can('save_deployment_target') && (
              <RetireTargetSheet
                target={t}
                trigger={retired ? <Button variant="outline"><RotateCcw aria-hidden />Return to service</Button> : <Button variant="outline"><Archive aria-hidden />Retire the target</Button>}
              />
            )}
            {session.can('map_pipeline') && !retired && <MapPipelineSheet targets={targetsInState(all, 'active')} trigger={<Button variant="outline"><GitBranch aria-hidden />Map a pipeline</Button>} />}
            <Button variant="outline" asChild>
              <a href={hrefTo({ page: 'deployments', targetId: id })}>Deployments here</a>
            </Button>
          </>
        )}
      />

      {retired && (
        <div className="mb-4 rounded-xl border border-line-strong bg-card px-4 py-3 text-sm text-navy narrow:mb-3" role="note">
          <span className="font-semibold">{`Retired ${fmtDate(t.retiredOn)}.`}</span>
          {' '}
          {`It keeps its records and refuses new deployments${t.retiredReason ? ` with the reason: ${t.retiredReason}` : '.'}`}
        </div>
      )}

      <div className={recordGrid}>
        <div className={column}>
          <Panel title="The target">
            <Facts>
              <Fact label="Kind">{t.targetKind?.label ?? 'Not stated'}</Fact>
              <Fact label="Stage" sub="Sets the gate level a deployment here meets.">{t.targetStage.label}</Fact>
              <Fact label="Change path" sub={`Allowed routes: ${routeWords(t.changePath)}. Any other route is flagged.`}>{t.changePath.label}</Fact>
              <Fact label="Link kind">{t.linkKind.label}</Fact>
              <Fact label="ID in its tool" sub={t.externalId === null ? 'Hidden from the reader role.' : undefined}>
                {t.externalId === null ? 'Hidden' : <span className={mono}>{t.externalId}</span>}
              </Fact>
              <Fact label="Last seen" sub={days !== null && days > STALE_DAYS ? `Not seen for ${plural(days, 'day', 'days')}.` : undefined}>{t.lastSeenOn ? fmtDateTime(t.lastSeenOn) : 'Never seen'}</Fact>
              {retired && <Fact label="Retired" sub={t.retiredReason ?? undefined}>{fmtDate(t.retiredOn)}</Fact>}
              <Fact label="Link" wide>{t.url ? <ExternalLink href={t.url}>Open it in its tool</ExternalLink> : t.externalId === null ? 'Hidden from the reader role' : 'No link recorded'}</Fact>
            </Facts>
          </Panel>

          <Panel title={retired ? 'What ran here' : 'What runs here'} count={t.applications.length} subtitle="The latest version of each application deployed here">
            {t.applications.length ? (
              <RowList label="What runs here">
                {t.applications.map(a => {
                  const app = appHref(a.businessApplicationId);
                  const record = withAppHint(recordHrefOf('deployment-record', a.deploymentRecordId), a.businessApplicationId);
                  return (
                    <Row
                      key={a.deploymentRecordId}
                      primary={app ? <TextLink href={app}>{a.name || 'Unnamed application'}</TextLink> : a.name || 'Not matched to an application'}
                      secondary={<>{record ? <TextLink href={record} className="text-xs">{`${a.artifactName} ${a.artifactVersion}`}</TextLink> : `${a.artifactName} ${a.artifactVersion}`}{a.finishedOn ? `, ${fmtDate(a.finishedOn)}` : ''}</>}
                      end={a.result ? <StatusBadge status={a.result} /> : undefined}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>Nothing is recorded as deployed here yet.</Empty>}
          </Panel>
        </div>

        <div className={column}>
          <Panel
            title="Recent deployments here"
            state={recent}
            count={dataOf(recent)?.totalCount}
            actions={<TextLink href={hrefTo({ page: 'deployments', targetId: id })} className="text-sm">All of them</TextLink>}
          >
            {d => (
              <div className="flex flex-col gap-2">
                {d.counts.flagged > 0 && (
                  <p className="m-0 flex flex-wrap items-center gap-2 text-sm text-navy-2">
                    <a href={hrefTo({ page: 'deployments', view: 'flagged', targetId: id })} className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring">
                      <ToneBadge tone="warning">{`${d.counts.flagged} flagged`}</ToneBadge>
                    </a>
                    {`of ${plural(d.counts.records, 'record', 'records')}.`}
                  </p>
                )}
                <DeploymentRows items={d.items} showTarget={false} compact empty="No deployment is recorded here yet." />
              </div>
            )}
          </Panel>

          <Panel title="Data locations here" count={t.dataLocations?.length ?? 0} subtitle="The stores on this target that hold business data">
            {t.dataLocations?.length ? (
              <RowList label="Data locations here">
                {t.dataLocations.map(l => {
                  const href = recordHref(l.recordUrl) ?? recordHrefOf('data-location', l.dataLocationId.toLowerCase());
                  return (
                    <Row
                      key={l.dataLocationId}
                      primary={href ? <TextLink href={href}>{l.label}</TextLink> : l.label}
                      secondary={l.platform?.label ?? 'Platform not stated'}
                      end={<WhatDependsOnIt start={{ dataLocationId: l.dataLocationId }} name={l.label} />}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>No data location is linked to this target. A location names its target on the Data page.</Empty>}
          </Panel>

          <Panel title="Pipelines that deploy here" count={pipelines.length}>
            {pipelines.length ? (
              <ul className="m-0 flex list-none flex-col p-0">
                {pipelines.map(p => <PipelineBlock key={p.pipelineReferenceId} pipeline={p} targets={all} highlight={t.deploymentTargetId} />)}
              </ul>
            ) : <Empty>No pipeline is mapped to this target.</Empty>}
          </Panel>
        </div>
      </div>
    </>
  );
}
