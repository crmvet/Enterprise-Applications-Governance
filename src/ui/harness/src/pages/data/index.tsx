// The Data page (D-37; docs/architecture/data-taxonomy.md): business data domains, where the data sits, who uses
// it, and what is missing. One page under Register with five tabs, each with an address of its own:
//   /app/data              Domains    the tree drawn per install: level, effective classification ("from People"),
//                                     data owner (own or inherited), deputy and steward, rule chips, counts, gap badges;
//                                     filters classification, level, gaps only, "domains I own" (mine=1); at phone
//                                     width a drill-down list. Actions: propose an owner (propose_owner_change with
//                                     dataDomainKey), propose a tree change (ConfigurationChangeSheet, configuration
//                                     admin only, previewed first)
//   /app/data/locations    Locations  by platform, then parent: platform, stage, technical owner, deployment target
//                                     (links to Places), hosted-by application, domains held with origin, last seen;
//                                     flags copy outside production, stale, no owner, unmapped, draft. Actions: add a
//                                     location, confirm seen, map data (save_data_location, save_data_mapping)
//   /app/data/mapping      Mapping    domains (the tree) against locations by stage: SoR, Replica, Copy, Masked,
//                                     Synthetic; a toggle shows domains against applications (R, W, RW, AI, state); at
//                                     phone width a list by domain. Actions: edit a mapping, propose a copy decision
//   /app/data/uses         Uses       every use: application, domain path, location, access, AI, state chip. Actions:
//                                     confirm a draft, propose a decision, open the decision, change or end a use
//   /app/data/gaps         Gaps       DC-01 to DC-14 by check, each with the one act that fixes it
// Shared filters in the route (every tab): domain=<key>, app=<id>, location=<id>, mine=1. A filter only one tab reads
// (classification, level, platform, stage, check, state, a matrix toggle, a selected cell) is a page parameter.
// Reads: find_data_domains, get_data_domain, find_data_locations, resolve_data, check_data_use, get_data_gaps,
// get_application_card (data section, for the app filter), get_configuration (labels), find_people. Writes: through
// ./sheets.tsx (the data write sheets, shared with the data record pages) and submit_record for the decisions they open.
//
// Choices made under D-41 (recorded here and in the report to the lead):
// - Six figures head every tab: domains and locations in scope, and the findings DC-01 (no data owner), DC-05 (decision
//   missing), DC-07 (breaks a rule) and DC-08 (copy outside production). Each opens its rows. They follow the shared
//   filters, so a figure and the list it opens always agree.
// - The page reads every domain (up to 1,000) and every location (up to 500) once and filters on the page, so the tree,
//   the figures and the lists agree, and a tab change reads nothing again.
// - "mine" is the server's answer (find_data_domains with ownerObjectId: own, deputized or stewarded, own or inherited).
//   On the Gaps tab it keeps the findings about those domains, and the findings that name the person.
// - Wave 4: the page reads every open use and every open mapping across the domains in one list each (list_data_uses,
//   list_data_mappings, up to 1,000 each), with the domain or application filter sent to the tool, and keeps the rest of
//   the scope on the page. The Uses and Mapping tabs carry their counts in the tab bar, and the Gaps tab finds the use or
//   the copy a fix acts on in the same lists. Before, those tabs read get_data_domain once per domain (up to 60 reads).
// - The Domains tab shows only the rules that are not "allowed", so a quiet branch stays quiet; "Never" carries a lock and
//   the danger tone, always with its label. An inherited classification or owner says where it comes from.
// - The Mapping matrix has a column only for a location that holds shown data; an empty location shows on the Locations
//   tab with its "No data mapped" flag. A picked cell opens its facts and acts under the matrix (mapping= or use=).
// - A tab keeps its rows as they were while one of its sheets is open (SheetHoldContext), so a fixed gap or a changed
//   state does not pull the sheet and the engine's answer out from under the person.
// - The Gaps tab writes each finding as a sentence from its fields; the engine's own message shows under it only with
//   tool names and row IDs left out, since the page offers the act itself.
import { useMemo } from 'react';
import { hrefTo } from '../../app/links';
import { navigate } from '../../app/navigation';
import { DATA_TABS, type RouteOf } from '../../app/routes';
import { DataStamp, KpiTile, PageHeader, PanelFailure } from '../../components/blocks';
import { PageTabs } from '../../components/pageTabs';
import { Checkbox } from '../../components/ui/checkbox';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Button } from '../../components/ui/button';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { useHost } from '../../harness';
import { Chip } from '../deployments-places/parts';
import { DomainsTab } from './DomainsTab';
import { GapsTab } from './GapsTab';
import { LocationsTab } from './LocationsTab';
import { DATA_TAB_LABELS, dataScope, dataTabRoute, isMyFinding, locationMatches, mappingMatches, useMatches, withShared, type Me } from './logic';
import { MappingTab } from './MappingTab';
import { FilterLabel, filterSection, pagedFailure, tabHref, type DataContext } from './parts';
import { useDomains, useGaps, useLocations, useMappingList, useMineKeys, useUseList } from './reads';
import { UsesTab } from './UsesTab';

export function DataPage({ route }: { route: RouteOf<'data'> }) {
  const tab = route.tab ?? 'domains';
  const session = useSession();
  const host = useHost();
  const domains = useDomains();
  const mine = useMineKeys(session.objectId, !!route.mine);
  const locations = useLocations();
  const card = useRead('get_application_card', route.applicationId ? { businessApplicationId: route.applicationId, sections: ['data'] } : null);
  const gaps = useGaps({ domainKey: route.domainKey, applicationId: route.applicationId, locationId: route.locationId });
  const uses = useUseList({ domainKey: route.domainKey, applicationId: route.applicationId });
  const mappings = useMappingList({ domainKey: route.domainKey });
  const me: Me = useMemo(() => ({ objectId: session.objectId, roleKeys: session.roleKeys }), [session.objectId, session.roleKeys.join(',')]);

  const cardData = dataOf(card);
  const cardFailure = route.applicationId && card.status === 'done' && !card.envelope.ok ? card.envelope : null;
  const appUses = route.applicationId ? (cardData ? cardData.data?.uses ?? [] : cardFailure ? [] : null) : null;
  const scope = useMemo(() => dataScope({
    tree: domains.tree,
    domainKey: route.domainKey,
    mineKeys: route.mine ? mine.keys ?? new Set<string>() : null,
    appUses,
    locationId: route.locationId,
    locations: locations.items,
  }), [domains.tree, route.domainKey, route.mine, mine.keys, appUses, route.locationId, locations.items]);

  const ctx: DataContext = {
    route,
    tree: domains.tree,
    domainsLoaded: domains.read.status === 'done' && (!route.mine || !!mine.keys || !!mine.failure) && (!route.applicationId || appUses !== null),
    domainsFailure: pagedFailure(domains.read),
    locations: locations.items,
    locationsLoaded: locations.loaded,
    appUses,
    appName: cardData?.application.name ?? null,
    mineKeys: mine.keys,
    scope,
    me,
    today: host.today,
    uses,
    mappings,
  };

  // The figures, in the scope of the shared filters.
  const tree = domains.tree;
  const domainCount = ctx.domainsLoaded && tree ? (scope.domainKeys ? tree.order.filter(k => scope.domainKeys!.has(k)).length : tree.order.length) : null;
  const locationCount = locations.loaded && ctx.domainsLoaded
    ? locations.items.filter(l => locationMatches(l, { domainKeys: scope.domainKeys, applicationId: route.applicationId, applicationLocationIds: scope.applicationLocationIds, locationId: route.locationId })).length
    : null;
  const useCount = uses.loaded && ctx.domainsLoaded
    ? uses.items.filter(u => useMatches(u, { domainKeys: scope.domainKeys, applicationId: route.applicationId, locationIds: scope.locationIds })).length
    : null;
  const mappingCount = mappings.loaded && ctx.domainsLoaded
    ? mappings.items.filter(m => mappingMatches(m, { domainKeys: scope.domainKeys, locationIds: scope.locationIds })).length
    : null;
  const findings = gaps.loaded ? (route.mine ? gaps.items.filter(f => isMyFinding(f, mine.keys, me)) : gaps.items) : null;
  const countOf = (check: string) => (findings ? findings.filter(f => f.check === check).length : null);
  const highCount = findings ? findings.filter(f => f.severity === 'high').length : 0;
  const openGaps = (check: string) => navigate(tabHref(route, 'gaps', { check }));

  const go = (change: Parameters<typeof withShared>[1]) => navigate(hrefTo(withShared(route, change)), true);
  const domainChoices = tree ? tree.order.map(k => ({ key: k, path: tree.nodes.get(k)!.item.path || tree.nodes.get(k)!.item.label })) : [];
  const domainLabel = route.domainKey ? domainChoices.find(d => d.key === route.domainKey)?.path ?? 'One domain' : null;
  const locationLabelNow = route.locationId ? locations.items.find(l => l.dataLocationId.toLowerCase() === route.locationId!.toLowerCase())?.label ?? 'One location' : null;
  const anyShared = !!route.domainKey || !!route.applicationId || !!route.locationId || !!route.mine;

  return (
    <>
      <PageHeader
        eyebrow="Register"
        title="Data"
        subtitle="Business data domains, where the data sits, which applications use it, and what is missing."
        meta={<DataStamp envelope={domains.read.status === 'done' ? domains.read.first : undefined} />}
      />

      <div className="mb-4 grid grid-cols-6 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5" role="group" aria-label="Key figures">
        <KpiTile label="Domains" value={domainCount} failed={domains.read.status === 'failed'} caption={anyShared ? 'In this scope' : 'In the tree'} onClick={() => navigate(hrefTo(dataTabRoute(route, 'domains')))} />
        <KpiTile label="Locations" value={locationCount} failed={locations.read.status === 'failed' || domains.read.status === 'failed'} caption="Stores that hold data" onClick={() => navigate(hrefTo(dataTabRoute(route, 'locations')))} />
        <KpiTile label="No data owner" value={countOf('DC-01')} failed={gaps.read.status === 'failed'} caption="Domains with no owner above" attention={(countOf('DC-01') ?? 0) > 0} onClick={() => openGaps('DC-01')} />
        <KpiTile label="Decisions missing" value={countOf('DC-05')} failed={gaps.read.status === 'failed'} caption="Uses that need a decision" attention={(countOf('DC-05') ?? 0) > 0} onClick={() => openGaps('DC-05')} />
        <KpiTile label="Breaking a rule" value={countOf('DC-07')} failed={gaps.read.status === 'failed'} caption="Uses the rules do not allow" attention={(countOf('DC-07') ?? 0) > 0} onClick={() => openGaps('DC-07')} />
        <KpiTile label="Copies outside production" value={countOf('DC-08')} failed={gaps.read.status === 'failed'} caption="Needing a decision or a fix" attention={(countOf('DC-08') ?? 0) > 0} onClick={() => openGaps('DC-08')} />
      </div>

      <section aria-labelledby="data-scope-title" className={filterSection}>
        <span id="data-scope-title" className="-mb-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Scope, for every tab</span>
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="data-domain-label">Domain</FilterLabel>
            <Select value={route.domainKey ?? 'all'} onValueChange={v => go({ domainKey: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[280px] narrow:w-full" aria-labelledby="data-domain-label"><SelectValue placeholder="Every domain" /></SelectTrigger>
              <SelectContent className="max-h-[360px]">
                <SelectItem value="all">Every domain</SelectItem>
                {route.domainKey && !domainChoices.some(d => d.key === route.domainKey) && <SelectItem value={route.domainKey}>{domainLabel ?? 'One domain'}</SelectItem>}
                {domainChoices.map(d => <SelectItem key={d.key} value={d.key}>{d.path}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="data-location-label">Location</FilterLabel>
            <Select value={route.locationId?.toLowerCase() ?? 'all'} onValueChange={v => go({ locationId: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[260px] narrow:w-full" aria-labelledby="data-location-label"><SelectValue placeholder="Every location" /></SelectTrigger>
              <SelectContent className="max-h-[360px]">
                <SelectItem value="all">Every location</SelectItem>
                {route.locationId && !locations.items.some(l => l.dataLocationId.toLowerCase() === route.locationId!.toLowerCase()) && <SelectItem value={route.locationId.toLowerCase()}>{locationLabelNow ?? 'One location'}</SelectItem>}
                {[...locations.items].sort((a, b) => a.label.localeCompare(b.label)).map(l => <SelectItem key={l.dataLocationId} value={l.dataLocationId.toLowerCase()}>{`${l.label}${l.stage ? ` (${l.stage.label})` : ''}`}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          {session.objectId && (
            <div className="flex items-center gap-2 pb-2">
              <Checkbox id="data-mine" checked={!!route.mine} onCheckedChange={v => go({ mine: v === true })} />
              <Label htmlFor="data-mine" className="font-normal">Only domains I own, deputize or steward</Label>
            </div>
          )}
        </div>
        {anyShared && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active shared filters">
            {route.domainKey && <Chip label={`Domain: ${domainLabel}`} onRemove={() => go({ domainKey: undefined })} />}
            {route.applicationId && <Chip label={`Application: ${cardData?.application.name ?? (cardFailure ? 'not found' : 'reading')}`} onRemove={() => go({ applicationId: undefined })} />}
            {route.locationId && <Chip label={`Location: ${locationLabelNow}`} onRemove={() => go({ locationId: undefined })} />}
            {route.mine && <Chip label="Domains I own, deputize or steward" onRemove={() => go({ mine: false })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={() => go({ domainKey: undefined, applicationId: undefined, locationId: undefined, mine: false })}>Clear all</Button>
          </div>
        )}
        {cardFailure && <PanelFailure failure={cardFailure} />}
        {mine.failure && <PanelFailure failure={mine.failure} />}
      </section>

      <PageTabs
        label="Data views"
        current={tab}
        tabs={DATA_TABS.map(t => ({
          key: t,
          label: DATA_TAB_LABELS[t],
          href: hrefTo(dataTabRoute(route, t)),
          count: t === 'domains' ? domainCount : t === 'locations' ? locationCount : t === 'mapping' ? mappingCount : t === 'uses' ? useCount : findings?.length ?? null,
          // The gap count is marked while any finding is high.
          attention: t === 'gaps' && highCount > 0,
        }))}
      />

      {tab === 'domains' && <DomainsTab ctx={ctx} />}
      {tab === 'locations' && <LocationsTab ctx={ctx} />}
      {tab === 'mapping' && <MappingTab ctx={ctx} />}
      {tab === 'uses' && <UsesTab ctx={ctx} />}
      {tab === 'gaps' && <GapsTab ctx={ctx} gaps={gaps} />}
    </>
  );
}
