// The Locations tab (/app/data/locations): every store, on any platform, by platform and then by the instance it sits
// in. Page parameters: platform=<key>, stage=<key>, flag=copy|stale|no-owner|unmapped|draft, q=<text>, retired=1.
import { CornerDownRight, Database, Map as MapIcon, Plus } from 'lucide-react';
import { useQueryParam } from '../../app/links';
import { BarList, Empty, Muted, Panel, PanelFailure, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import type { DataLocationItem } from '../../data/contracts';
import { fmtDate, plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { useSession } from '../../data/session';
import { Chip } from '../deployments-places/parts';
import {
  daysSince,
  flagsOf,
  groupLocations,
  hasFlag,
  LOCATION_FLAG_TEXT,
  LOCATION_FLAGS,
  locationMatches,
  mayChangeLocation,
  mayMapData,
  mayProposeLocationOwner,
  type LocationFilters,
  type LocationFlag,
} from './logic';
import { appHref, column, FilterLabel, filterSection, HeldChip, KEY, locationHref, placeHref, SearchBox, TEXT, twoColumns, useParams, type DataContext } from './parts';
import { useLocations } from './reads';
import { SheetHoldContext, useHeld, useHoldCount } from './sheetParts';
import { DataLocationSheet, DataMappingSheet, ProposeDataOwnerSheet } from './sheets';

const FLAG = /^(copy|stale|no-owner|unmapped|draft)$/;

export function LocationsTab({ ctx }: { ctx: DataContext }) {
  const session = useSession();
  const setParams = useParams();
  const platforms = useLabels('data-platform');
  const stages = useLabels('target-stage');
  const platformKey = useQueryParam('platform', KEY);
  const stageKey = useQueryParam('stage', KEY);
  const flag = useQueryParam('flag', FLAG) as LocationFlag | undefined;
  const text = useQueryParam('q', TEXT);
  const retired = useQueryParam('retired', /^1$/) === '1';
  // With retired=1 the tab reads its own list; otherwise the page's.
  const withRetired = useLocations(retired);
  const live = retired ? withRetired.items : ctx.locations;
  const loaded = retired ? withRetired.loaded : ctx.locationsLoaded;
  const failure = retired && withRetired.read.status === 'failed' ? withRetired.read.failure : null;
  const [holds, hold] = useHoldCount();
  const all = useHeld(live, holds > 0);

  const scoped: LocationFilters = {
    domainKeys: ctx.scope.domainKeys,
    applicationId: ctx.route.applicationId,
    applicationLocationIds: ctx.scope.applicationLocationIds,
    locationId: ctx.route.locationId,
  };
  const inScope = all.filter(l => locationMatches(l, scoped));
  const filters: LocationFilters = { ...scoped, platformKey, stageKey, flag, text };
  const matches = (l: DataLocationItem) => locationMatches(l, filters);
  const sortOrder = (k: string) => platforms.field<number>(k, 'sortOrder') ?? Number.MAX_SAFE_INTEGER;
  const groups = groupLocations(all, matches, sortOrder);
  // A store inside an instance is as fresh as its instance (the engine's stale flag reads the instance's last seen date).
  const byId = new Map(all.map(l => [l.dataLocationId.toLowerCase(), l]));
  const shownCount = groups.reduce((n, g) => n + g.count, 0);
  const pageFilter = !!platformKey || !!stageKey || !!flag || !!text;
  const clear = () => setParams({ platform: undefined, stage: undefined, flag: undefined, q: undefined });
  const platformsSeen = [...new Map(inScope.map(l => [l.platform.key, l.platform.label])).entries()].sort((a, b) => sortOrder(a[0]) - sortOrder(b[0]) || a[1].localeCompare(b[1]));
  const stageOrder = stages.keys().length ? stages.keys() : [...new Set(inScope.flatMap(l => (l.stage ? [l.stage.key] : [])))];

  return (
    <SheetHoldContext.Provider value={hold}>
      <section aria-label="Location filters" className={filterSection}>
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="locations-platform-label">Platform</FilterLabel>
            <Select value={platformKey ?? 'all'} onValueChange={v => setParams({ platform: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[220px] narrow:w-full" aria-labelledby="locations-platform-label"><SelectValue /></SelectTrigger>
              <SelectContent className="max-h-[320px]">
                <SelectItem value="all">Every platform</SelectItem>
                {platformsSeen.map(([k, label]) => <SelectItem key={k} value={k}>{label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="locations-stage-label">Stage</FilterLabel>
            <Select value={stageKey ?? 'all'} onValueChange={v => setParams({ stage: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[180px] narrow:w-full" aria-labelledby="locations-stage-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Every stage</SelectItem>
                {stageOrder.map(k => <SelectItem key={k} value={k}>{stages.label(k)}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="locations-flag-label">Flag</FilterLabel>
            <Select value={flag ?? 'all'} onValueChange={v => setParams({ flag: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[220px] narrow:w-full" aria-labelledby="locations-flag-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Any or none</SelectItem>
                {LOCATION_FLAGS.map(f => <SelectItem key={f} value={f}>{LOCATION_FLAG_TEXT[f]}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <SearchBox value={text} placeholder="Name, data, owner or system" label="Search the locations" onChange={q => setParams({ q })} />
          <div className="flex items-center gap-2 pb-2">
            <Checkbox id="locations-retired" checked={retired} onCheckedChange={v => setParams({ retired: v === true ? '1' : undefined })} />
            <Label htmlFor="locations-retired" className="font-normal">Show retired locations</Label>
          </div>
        </div>
        {pageFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active location filters">
            {platformKey && <Chip label={`Platform: ${platforms.label(platformKey)}`} onRemove={() => setParams({ platform: undefined })} />}
            {stageKey && <Chip label={`Stage: ${stages.label(stageKey)}`} onRemove={() => setParams({ stage: undefined })} />}
            {flag && <Chip label={LOCATION_FLAG_TEXT[flag]} onRemove={() => setParams({ flag: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => setParams({ q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clear}>Clear these</Button>
          </div>
        )}
      </section>

      <div className={twoColumns}>
        <div className={column}>
          {failure ? <PanelFailure failure={failure} />
            : !loaded ? <Panel title="Locations" state={{ status: 'loading' }} skeletonRows={6}>{() => null}</Panel>
              : shownCount === 0 ? (
                <Panel
                  title="Locations"
                  count={0}
                  actions={session.can('save_data_location') ? <DataLocationSheet mode="add" trigger={<Button size="sm"><Plus aria-hidden />Add a location</Button>} /> : undefined}
                >
                  <span className="flex flex-col items-start gap-2">
                    <Empty>
                      {pageFilter || ctx.scope.domainKeys || ctx.route.applicationId || ctx.route.locationId
                        ? 'No location matches these filters.'
                        : 'No location is registered yet. Add the stores where business data sits: an environment, a server, a database, a Salesforce org or a legacy host.'}
                    </Empty>
                    {pageFilter && <Button variant="outline" size="sm" onClick={clear}>Clear the filters</Button>}
                  </span>
                </Panel>
              ) : groups.map((g, i) => (
                <Panel
                  key={g.platform.key}
                  title={g.platform.label}
                  count={g.count}
                  subtitle={`${plural(g.count, 'location', 'locations')} on ${g.platform.label}, each instance with the stores inside it`}
                  actions={i === 0 && session.can('save_data_location') ? <DataLocationSheet mode="add" trigger={<Button size="sm"><Plus aria-hidden />Add a location</Button>} /> : undefined}
                >
                  <RowList label={`${g.platform.label} locations`}>
                    {g.rows.map(r => <LocationRow key={r.item.dataLocationId} item={r.item} depth={r.depth} matched={r.matched} ctx={ctx} instance={r.item.parent ? byId.get(r.item.parent.dataLocationId.toLowerCase()) : undefined} />)}
                  </RowList>
                </Panel>
              ))}
          {loaded && all.length >= 500 && <Muted>The product holds more than 500 locations. Narrow the filters to find the rest.</Muted>}
        </div>
        <div className={column}>
          <Panel title="By platform" subtitle="The locations in scope. Each bar narrows the list.">
            {platformsSeen.length ? (
              <BarList
                ariaLabel="Locations by platform"
                noun={['location', 'locations']}
                labelWidth={170}
                items={platformsSeen.map(([k, label]) => ({ key: k, label, count: inScope.filter(l => l.platform.key === k).length }))}
                onDrill={b => b.key && setParams({ platform: b.key })}
              />
            ) : <Empty>{loaded ? 'No location to count.' : 'Reading the locations.'}</Empty>}
          </Panel>
          <Panel title="Flags" subtitle="What needs a look. Each count narrows the list.">
            <ul className="m-0 flex list-none flex-col gap-1 p-0">
              {LOCATION_FLAGS.map(f => {
                const n = inScope.filter(l => hasFlag(l, f)).length;
                return (
                  <li key={f}>
                    <button
                      type="button"
                      onClick={() => setParams({ flag: flag === f ? undefined : f })}
                      aria-pressed={flag === f}
                      className="flex w-full cursor-pointer items-center justify-between gap-3 rounded-md border border-transparent px-2 py-1.5 text-left text-sm text-navy hover:bg-background focus-visible:outline-2 focus-visible:outline-ring aria-pressed:border-primary aria-pressed:bg-info-soft"
                    >
                      <span>{LOCATION_FLAG_TEXT[f]}</span>
                      <span className="font-semibold tabular-nums">{loaded ? n : ''}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </Panel>
        </div>
      </div>
    </SheetHoldContext.Provider>
  );
}

function FlagBadge({ item, flag, today, instance }: { item: DataLocationItem; flag: LocationFlag; today: string; instance?: DataLocationItem }) {
  switch (flag) {
    case 'copy': return <ToneBadge tone="warning">Copy outside production</ToneBadge>;
    case 'stale': {
      // A store inside an instance counts the instance's days, as the engine's flag does, so confirming the store alone
      // never leaves "Not seen for 0 days" beside the flag (wave 4b QA walk).
      if (item.parent && instance?.lastSeenOn) {
        const days = daysSince(instance.lastSeenOn, today) ?? 0;
        return <ToneBadge tone="warning">{`Its instance not seen for ${plural(days, 'day', 'days')}`}</ToneBadge>;
      }
      const days = daysSince(item.lastSeenOn, today);
      return <ToneBadge tone="warning">{days === null ? 'Never confirmed seen' : `Not seen for ${plural(days, 'day', 'days')}`}</ToneBadge>;
    }
    case 'no-owner': return <ToneBadge tone="danger">No technical owner</ToneBadge>;
    case 'unmapped': return <ToneBadge tone="subtle">No data mapped</ToneBadge>;
    case 'draft': return <ToneBadge tone="informative">Draft</ToneBadge>;
  }
}

function LocationRow({ item, depth, matched, ctx, instance }: { item: DataLocationItem; depth: 0 | 1; matched: boolean; ctx: DataContext; instance?: DataLocationItem }) {
  const session = useSession();
  const href = locationHref(item);
  const owner = item.technicalOwner;
  const target = placeHref(item.deploymentTargetId);
  const system = item.hostedByApplication;
  const open = !item.retiredOn && item.status.meaning !== 'archived';
  // A data owner maps their own domains anywhere; the engine checks the domain they choose.
  const canMap = open && session.can('save_data_mapping') && (mayMapData(ctx.me, null, item) || session.hasRole('data-owner'));
  const canConfirm = open && session.can('save_data_location') && mayChangeLocation(ctx.me, item);
  const canOwner = open && item.flags.noTechnicalOwner && session.can('propose_owner_change') && mayProposeLocationOwner(ctx.me, item) && !item.parent;
  return (
    <Row
      wideEnd
      lead={depth === 0
        ? <Database className="size-5 text-navy-3" aria-hidden />
        : <CornerDownRight className="ml-2 size-4 text-navy-3" aria-hidden />}
      primary={href ? <TextLink href={href}>{item.label}</TextLink> : item.label}
      secondary={[
        depth === 1 && item.parent ? `Inside ${item.parent.label}` : null,
        item.stage?.label ?? 'No stage',
        owner ? `Technical owner ${owner.principal.displayLabel}${owner.fromParent ? ', from the instance' : ''}` : 'No technical owner',
        !matched ? 'shown for the stores inside it' : null,
      ].filter(Boolean).join('. ')}
      tertiary={(
        <span className="flex flex-wrap gap-x-4 gap-y-2">
          <span>{item.lastSeenOn ? `Last seen ${fmtDate(item.lastSeenOn)}` : 'Never confirmed seen'}</span>
          {target && <TextLink href={target} className="text-xs">Deployment target in Places</TextLink>}
          {system && appHref(system.businessApplicationId) && <span>System of <TextLink href={appHref(system.businessApplicationId)!} className="text-xs">{system.name}</TextLink></span>}
          {item.childCount > 0 && depth === 0 && <span>{plural(item.childCount, 'store inside', 'stores inside')}</span>}
        </span>
      )}
      badges={(
        <>
          {item.retiredOn && <ToneBadge tone="subtle" icon={null}>{`Retired ${fmtDate(item.retiredOn)}`}</ToneBadge>}
          {!item.retiredOn && item.status.meaning === 'archived' && <ToneBadge tone="subtle" icon={null}>Withdrawn</ToneBadge>}
          {open && flagsOf(item).map(f => <FlagBadge key={f} item={item} flag={f} today={ctx.today} instance={instance} />)}
          <AiMark isAiCreated={item.isAiCreated} performedBy={item.performedBy} />
          {item.domains.map(d => <HeldChip key={d.dataMappingId} path={d.domain.path || d.domain.label} domainKey={d.domain.key} origin={d.origin} />)}
        </>
      )}
      end={(canConfirm || canMap || canOwner) ? (
        <span className="flex flex-wrap items-center gap-1.5">
          {canConfirm && <DataLocationSheet mode="confirm-seen" location={item} trigger={<Button variant="outline" size="sm">Confirm seen</Button>} />}
          {canMap && <DataMappingSheet mode="add" dataLocationId={item.dataLocationId} trigger={<Button variant="outline" size="sm"><MapIcon aria-hidden />Map data</Button>} />}
          {canOwner && <ProposeDataOwnerSheet subject={{ kind: 'data-location', dataLocationId: item.dataLocationId, label: item.label }} ownerRoleKey="technical-owner" trigger={<Button variant="outline" size="sm">Propose an owner</Button>} />}
        </span>
      ) : undefined}
    />
  );
}
