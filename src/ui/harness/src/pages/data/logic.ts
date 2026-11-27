// The Data page's logic with no React in it, tested in logic.test.ts. Every tab and every data write sheet reads it, so
// a tree, a filter or a form reads the same wherever it shows.
//   dataTabRoute(route, tab)   the address of another tab, keeping the shared filters (domain, application, location,
//                              mine), so "Uses" from a domain's Mapping tab still shows that domain
//   DATA_TAB_LABELS            the tab names in the tab bar
//   buildDomainTree, domainRows, drillRows, subtreeOf, ancestorsOf    the domain tree, flat with its parent keys
//   domainMatches, gapChecks, notableRules                            the Domains tab's filters and marks
//   groupLocations, locationMatches, LOCATION_FLAGS                   the Locations tab
//   matrixColumns, mappingCells, appColumns, useCells                 the Mapping tab's two matrices
//   USE_STATE_FILTERS, useMatchesState, sortUses, useActionsOf        the Uses tab
//   mappingMatches, findingMatches                                    the Mapping and Gaps tabs' scope and search
//   groupFindings, fixOf, findingText, plainFinding                   the Gaps tab
//   mayProposeDomainOwner, mayProposeLocationOwner, mayMapData        who sees which action (the engine still decides)
//   buildUseInput, buildLocationInput, buildMappingInput, buildOwnerInput, buildDecisionInput, buildMoveInput,
//   parseContainers                                                   the six data write sheets' tool inputs
import type { DataTab, RouteOf } from '../../app/routes';
import type {
  DataAccess,
  DataCheckCode,
  DataDomainItem,
  DataFinding,
  DataHandlingRule,
  DataLocationItem,
  DataMappingItem,
  DataOrigin,
  DataRules,
  DataUseItem,
  DataUseState,
  LabelledKey,
  PrincipalRef,
} from '../../data/contracts';
import { DATA_CHECK_CODES, DATA_ORIGIN_TEXT, needsAttention } from '../../data/dataTaxonomy';
import { matchesText } from '../../data/textSearch';
import type { WriteInput } from '../../data/writes';

// The UTC calendar day. Data rows keep the UTC day (wave 4, B24-21: the engine judges a use or a mapping open or ended by
// the UTC day, and a move ends one row and starts the next on the same day), so an end date starts from it, not from
// the person's own day, which the other date-only defaults use.
export function utcToday(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}

export const DATA_TAB_LABELS: Record<DataTab, string> = {
  domains: 'Domains',
  locations: 'Locations',
  mapping: 'Mapping',
  uses: 'Uses',
  gaps: 'Gaps',
};

export function dataTabRoute(route: RouteOf<'data'>, tab: DataTab): RouteOf<'data'> {
  const next: RouteOf<'data'> = { page: 'data' };
  if (tab !== 'domains') next.tab = tab;
  if (route.domainKey) next.domainKey = route.domainKey;
  if (route.applicationId) next.applicationId = route.applicationId;
  if (route.locationId) next.locationId = route.locationId;
  if (route.mine) next.mine = true;
  return next;
}

// The same filters on one tab with one shared filter changed (undefined removes it).
export function withShared(route: RouteOf<'data'>, change: { domainKey?: string; applicationId?: string; locationId?: string; mine?: boolean }): RouteOf<'data'> {
  const next = dataTabRoute(route, route.tab ?? 'domains');
  if ('domainKey' in change) { if (change.domainKey) next.domainKey = change.domainKey; else delete next.domainKey; }
  if ('applicationId' in change) { if (change.applicationId) next.applicationId = change.applicationId; else delete next.applicationId; }
  if ('locationId' in change) { if (change.locationId) next.locationId = change.locationId; else delete next.locationId; }
  if ('mine' in change) { if (change.mine) next.mine = true; else delete next.mine; }
  return next;
}

export const sameId = (a: string | null | undefined, b: string | null | undefined): boolean => !!a && !!b && a.toLowerCase() === b.toLowerCase();

// ---------------------------------------------------------------------------------------------------------------------
// The domain tree
// ---------------------------------------------------------------------------------------------------------------------

export interface DomainNode {
  item: DataDomainItem;
  depth: number;
  parentKey: string | null;
  childKeys: string[];
}

export interface DomainTree {
  // Depth first, siblings in the order the read returned them (the configured order).
  order: string[];
  nodes: Map<string, DomainNode>;
}

// The tree from find_data_domains' flat rows. A row whose parent is not in the list is a root, so a filtered or partial
// read still shows every row. A cycle (which V49 refuses) cannot loop: each key is placed once.
export function buildDomainTree(items: readonly DataDomainItem[]): DomainTree {
  const byKey = new Map<string, DataDomainItem>();
  for (const item of items) if (!byKey.has(item.key)) byKey.set(item.key, item);
  const children = new Map<string | null, string[]>();
  for (const item of byKey.values()) {
    const parent = item.parentKey && byKey.has(item.parentKey) && item.parentKey !== item.key ? item.parentKey : null;
    const list = children.get(parent) ?? [];
    list.push(item.key);
    children.set(parent, list);
  }
  const nodes = new Map<string, DomainNode>();
  const order: string[] = [];
  const place = (key: string, depth: number, parentKey: string | null) => {
    if (nodes.has(key)) return;
    const item = byKey.get(key)!;
    const node: DomainNode = { item, depth, parentKey, childKeys: [] };
    nodes.set(key, node);
    order.push(key);
    for (const child of children.get(key) ?? []) {
      if (nodes.has(child)) continue;
      node.childKeys.push(child);
      place(child, depth + 1, key);
    }
  };
  for (const root of children.get(null) ?? []) place(root, 0, null);
  // Anything left sits in a cycle: shown at the top.
  for (const key of byKey.keys()) place(key, 0, null);
  return { order, nodes };
}

// The keys above a domain, from the top down (the domain itself left out).
export function ancestorsOf(tree: DomainTree, key: string): string[] {
  const out: string[] = [];
  const seen = new Set<string>([key]);
  let parent = tree.nodes.get(key)?.parentKey ?? null;
  while (parent && !seen.has(parent)) {
    out.unshift(parent);
    seen.add(parent);
    parent = tree.nodes.get(parent)?.parentKey ?? null;
  }
  return out;
}

// A domain and every domain below it.
export function subtreeOf(tree: DomainTree, key: string): Set<string> {
  const out = new Set<string>();
  const walk = (k: string) => {
    if (out.has(k) || !tree.nodes.has(k)) return;
    out.add(k);
    for (const c of tree.nodes.get(k)!.childKeys) walk(c);
  };
  walk(key);
  return out;
}

// The Domains tab's own filters. scopeKeys narrows to the domains the shared filters reach (a domain's subtree, the
// domains an application uses or a location holds); mineKeys to the domains the person owns, deputizes or stewards.
export interface DomainFilters {
  classificationKey?: string;
  levelKey?: string;
  gapsOnly?: boolean;
  text?: string;
  scopeKeys?: ReadonlySet<string> | null;
  mineKeys?: ReadonlySet<string> | null;
}

export function domainMatches(item: DataDomainItem, f: DomainFilters): boolean {
  if (f.scopeKeys && !f.scopeKeys.has(item.key)) return false;
  if (f.mineKeys && !f.mineKeys.has(item.key)) return false;
  if (f.classificationKey && item.classification?.key !== f.classificationKey) return false;
  if (f.levelKey && item.level?.key !== f.levelKey) return false;
  if (f.gapsOnly && item.gaps.length === 0) return false;
  if (f.text) {
    const words = f.text.toLowerCase();
    const hay = `${item.label} ${item.path} ${item.owner?.principal.displayLabel ?? ''} ${item.steward?.principal.displayLabel ?? ''}`.toLowerCase();
    if (!hay.includes(words)) return false;
  }
  return true;
}

export interface DomainRow {
  key: string;
  item: DataDomainItem;
  depth: number;
  // False for a domain shown only because a matching domain sits below it.
  matched: boolean;
  // A child is shown (matched or on the way to a match).
  hasChildren: boolean;
  collapsed: boolean;
}

// The rows of the tree for the filters: every matching domain and the domains above it, in tree order, with the
// branches under a collapsed domain left out.
export function domainRows(tree: DomainTree, f: DomainFilters, collapsed: ReadonlySet<string> = new Set()): { rows: DomainRow[]; matchedCount: number } {
  const matched = new Set(tree.order.filter(k => domainMatches(tree.nodes.get(k)!.item, f)));
  const visible = new Set<string>(matched);
  for (const k of matched) for (const a of ancestorsOf(tree, k)) visible.add(a);
  const rows: DomainRow[] = [];
  for (const key of tree.order) {
    if (!visible.has(key)) continue;
    if (ancestorsOf(tree, key).some(a => collapsed.has(a))) continue;
    const node = tree.nodes.get(key)!;
    rows.push({
      key,
      item: node.item,
      depth: node.depth,
      matched: matched.has(key),
      hasChildren: node.childKeys.some(c => visible.has(c)),
      collapsed: collapsed.has(key),
    });
  }
  return { rows, matchedCount: matched.size };
}

// The phone's drill-down: the shown children of one domain (or the top level), each with how many shown domains sit
// below it.
export function drillRows(tree: DomainTree, f: DomainFilters, atKey: string | null): { item: DataDomainItem; matched: boolean; below: number }[] {
  const matched = new Set(tree.order.filter(k => domainMatches(tree.nodes.get(k)!.item, f)));
  const visible = new Set<string>(matched);
  for (const k of matched) for (const a of ancestorsOf(tree, k)) visible.add(a);
  const keys = atKey ? tree.nodes.get(atKey)?.childKeys ?? [] : tree.order.filter(k => tree.nodes.get(k)!.parentKey === null);
  return keys.filter(k => visible.has(k)).map(k => {
    const below = [...subtreeOf(tree, k)].filter(x => x !== k && visible.has(x)).length;
    return { item: tree.nodes.get(k)!.item, matched: matched.has(k), below };
  });
}

// The domain gaps a row carries are reason codes (DC-01 to DC-04 on a domain); the check each one belongs to.
export const REASON_CHECK: Record<string, DataCheckCode> = {
  'data.no-owner': 'DC-01',
  'data.own-owner-missing': 'DC-02',
  'data.no-deputy': 'DC-03',
  'data.owner-inactive': 'DC-04',
  'data.decision-missing': 'DC-05',
  'data.no-one-to-ask': 'DC-06',
  'data.breaks-rule': 'DC-07',
  'data.copy-outside-production': 'DC-08',
  'data.sensitivity-below-data': 'DC-09',
  'data.undeclared': 'DC-10',
  'data.mapping-missing': 'DC-11',
  'data.location-unowned': 'DC-12',
  'data.location-stale': 'DC-12',
  'data.retention-open': 'DC-13',
  'data.retired-domain-rows': 'DC-14',
};

export function gapChecks(codes: readonly string[]): DataCheckCode[] {
  const found = new Set(codes.map(c => REASON_CHECK[c]).filter((c): c is DataCheckCode => !!c));
  return DATA_CHECK_CODES.filter(c => found.has(c));
}

// The rules worth a chip: those that are not "allowed". A domain whose three rules allow everything shows no chip.
export function notableRules(rules: DataRules): ('use' | 'ai' | 'copy')[] {
  return (['use', 'ai', 'copy'] as const).filter(n => rules[n].value !== 'allowed');
}

const RULE_STRICTNESS: Record<DataHandlingRule, number> = { allowed: 0, decision: 1, 'masked-only': 2, never: 3 };

// True when one of the rules is never: the row shows the lock.
export function hasNever(rules: DataRules): boolean {
  return (['use', 'ai', 'copy'] as const).some(n => RULE_STRICTNESS[rules[n].value] === 3);
}

// ---------------------------------------------------------------------------------------------------------------------
// Who sees which action. The engine still checks every write; these only keep an action off a page that would refuse it.
// ---------------------------------------------------------------------------------------------------------------------

export interface Me {
  objectId: string | null;
  roleKeys: readonly string[];
}

const isMe = (me: Me, p: { objectId: string } | null | undefined) => !!p && sameId(p.objectId, me.objectId);
const holds = (me: Me, ...roles: string[]) => roles.some(r => me.roleKeys.includes(r));

// A domain's owners are proposed by the platform admin, the data governance lead, or a data owner of the domain or of a
// domain above it (docs/architecture/data-taxonomy.md, Appointing owners; IntakeTools.ProposeDataOwnerChange).
export function mayProposeDomainOwner(me: Me, tree: DomainTree, key: string): boolean {
  if (holds(me, 'platform-admin', 'data-governance-lead')) return true;
  return [...ancestorsOf(tree, key), key].some(k => {
    const owner = tree.nodes.get(k)?.item.owner;
    return !!owner && owner.inheritedFrom === null && isMe(me, owner.principal);
  });
}

// A location's technical owner is proposed by the platform admin, the data governance lead or its technical owner.
export function mayProposeLocationOwner(me: Me, location: Pick<DataLocationItem, 'technicalOwner'> | null | undefined): boolean {
  if (holds(me, 'platform-admin', 'data-governance-lead')) return true;
  return isMe(me, location?.technicalOwner?.principal);
}

// Data is mapped by the platform admin, the data governance lead, the domain's data owner or steward, or the location's
// technical owner (DataWriteTools.SaveDataMapping). Either side may be unknown yet: then only the roles count.
export function mayMapData(me: Me, domain: Pick<DataDomainItem, 'owner' | 'deputy' | 'steward'> | null | undefined, location?: Pick<DataLocationItem, 'technicalOwner'> | null): boolean {
  if (holds(me, 'platform-admin', 'data-governance-lead')) return true;
  if (domain && (isMe(me, domain.owner?.principal) || isMe(me, domain.deputy) || isMe(me, domain.steward?.principal))) return true;
  return isMe(me, location?.technicalOwner?.principal);
}

// A registered location is changed by the platform admin, an architect, the data governance lead or its technical owner
// (DataWriteTools.SaveDataLocation); a draft also by whoever drafted it, which the page cannot see, so a draft offers it.
export function mayChangeLocation(me: Me, location: Pick<DataLocationItem, 'technicalOwner' | 'flags'>): boolean {
  if (holds(me, 'platform-admin', 'architect', 'data-governance-lead')) return true;
  return location.flags.draft || isMe(me, location.technicalOwner?.principal);
}

// ---------------------------------------------------------------------------------------------------------------------
// Locations
// ---------------------------------------------------------------------------------------------------------------------

export const LOCATION_FLAGS = ['copy', 'stale', 'no-owner', 'unmapped', 'draft'] as const;
export type LocationFlag = (typeof LOCATION_FLAGS)[number];

export const LOCATION_FLAG_TEXT: Record<LocationFlag, string> = {
  copy: 'Copy outside production',
  stale: 'Not seen recently',
  'no-owner': 'No technical owner',
  unmapped: 'No data mapped',
  draft: 'Draft',
};

export function hasFlag(item: DataLocationItem, flag: LocationFlag): boolean {
  switch (flag) {
    case 'copy': return item.flags.copyOutsideProduction;
    case 'stale': return item.flags.stale;
    case 'no-owner': return item.flags.noTechnicalOwner;
    case 'unmapped': return item.flags.unmapped;
    case 'draft': return item.flags.draft;
  }
}

export function flagsOf(item: DataLocationItem): LocationFlag[] {
  return LOCATION_FLAGS.filter(f => hasFlag(item, f));
}

export interface LocationFilters {
  platformKey?: string;
  stageKey?: string;
  flag?: LocationFlag;
  text?: string;
  // Locations that hold one of these domains.
  domainKeys?: ReadonlySet<string> | null;
  // One application: the system it is, or a location one of its uses names.
  applicationId?: string;
  applicationLocationIds?: ReadonlySet<string> | null;
  // One location and the stores inside it.
  locationId?: string;
}

export function locationMatches(item: DataLocationItem, f: LocationFilters): boolean {
  if (f.platformKey && item.platform.key !== f.platformKey) return false;
  if (f.stageKey && item.stage?.key !== f.stageKey) return false;
  if (f.flag && !hasFlag(item, f.flag)) return false;
  if (f.domainKeys && !item.domains.some(d => f.domainKeys!.has(d.domain.key))) return false;
  if (f.applicationId) {
    const hosted = sameId(item.hostedByApplication?.businessApplicationId, f.applicationId);
    const used = !!f.applicationLocationIds && [...f.applicationLocationIds].some(id => sameId(id, item.dataLocationId));
    if (!hosted && !used) return false;
  }
  if (f.locationId && !sameId(item.dataLocationId, f.locationId) && !sameId(item.parent?.dataLocationId, f.locationId)) return false;
  if (f.text) {
    const words = f.text.toLowerCase();
    const hay = `${item.label} ${item.platform.label} ${item.parent?.label ?? ''} ${item.hostedByApplication?.name ?? ''} ${item.technicalOwner?.principal.displayLabel ?? ''} ${item.domains.map(d => d.domain.path).join(' ')}`.toLowerCase();
    if (!hay.includes(words)) return false;
  }
  return true;
}

export interface LocationRow {
  item: DataLocationItem;
  depth: 0 | 1;
  // False for an instance shown only because a store inside it matches.
  matched: boolean;
}

export interface LocationGroup {
  platform: LabelledKey;
  rows: LocationRow[];
  // The matching locations in the group.
  count: number;
}

const byLabel = (a: { label: string }, b: { label: string }) => a.label.localeCompare(b.label);

// Grouped by platform (in the configured order), then by parent: each instance with the stores inside it under it. A
// store whose instance does not match shows its instance as context. A store whose instance is not in the list stands
// alone.
export function groupLocations(items: readonly DataLocationItem[], matches: (item: DataLocationItem) => boolean, platformRank: (key: string) => number): LocationGroup[] {
  const byId = new Map(items.map(i => [i.dataLocationId.toLowerCase(), i]));
  const parentOf = (i: DataLocationItem) => (i.parent ? byId.get(i.parent.dataLocationId.toLowerCase()) ?? null : null);
  const tops = items.filter(i => !parentOf(i));
  const groups = new Map<string, LocationGroup>();
  for (const top of [...tops].sort(byLabel)) {
    const children = items.filter(i => parentOf(i) === top).sort(byLabel);
    const topMatches = matches(top);
    const shownChildren = children.filter(matches);
    if (!topMatches && !shownChildren.length) continue;
    const group = groups.get(top.platform.key) ?? { platform: top.platform, rows: [], count: 0 };
    group.rows.push({ item: top, depth: 0, matched: topMatches });
    for (const c of shownChildren) group.rows.push({ item: c, depth: 1, matched: true });
    group.count += (topMatches ? 1 : 0) + shownChildren.length;
    groups.set(top.platform.key, group);
  }
  return [...groups.values()].sort((a, b) => platformRank(a.platform.key) - platformRank(b.platform.key) || a.platform.label.localeCompare(b.platform.label));
}

// Days since a date, or null when there is none.
export function daysSince(value: string | null | undefined, today: string): number | null {
  if (!value) return null;
  const a = Date.parse(`${value.slice(0, 10)}T00:00:00Z`);
  const b = Date.parse(`${today.slice(0, 10)}T00:00:00Z`);
  if (Number.isNaN(a) || Number.isNaN(b)) return null;
  return Math.max(0, Math.round((b - a) / 86_400_000));
}

// ---------------------------------------------------------------------------------------------------------------------
// The Mapping tab's matrices
// ---------------------------------------------------------------------------------------------------------------------

export interface MatrixColumn {
  id: string;
  label: string;
  platform: LabelledKey | null;
}

export interface MatrixColumnGroup {
  stage: LabelledKey | null;
  columns: MatrixColumn[];
}

// Domains against locations: one column per location that holds a shown mapping, grouped by stage in the configured
// order (production first), locations with no stage last.
export function matrixColumns(mappings: readonly DataMappingItem[], stageRank: (key: string) => number, stageKey?: string): MatrixColumnGroup[] {
  const groups = new Map<string, MatrixColumnGroup>();
  for (const m of mappings) {
    const stage = m.location.stage;
    if (stageKey && stage?.key !== stageKey) continue;
    const gk = stage?.key ?? '';
    const group = groups.get(gk) ?? { stage, columns: [] };
    if (!group.columns.some(c => sameId(c.id, m.location.dataLocationId))) {
      group.columns.push({ id: m.location.dataLocationId, label: m.location.label, platform: m.location.platform });
    }
    groups.set(gk, group);
  }
  for (const g of groups.values()) g.columns.sort(byLabel);
  return [...groups.values()].sort((a, b) => {
    if (!a.stage) return 1;
    if (!b.stage) return -1;
    return stageRank(a.stage.key) - stageRank(b.stage.key) || a.stage.label.localeCompare(b.stage.label);
  });
}

export const cellKey = (domainKey: string, columnId: string) => `${domainKey}|${columnId.toLowerCase()}`;

// The mappings of each domain and location, in origin order (system of record first).
const ORIGIN_ORDER: DataOrigin[] = ['system-of-record', 'replica', 'production-copy', 'masked-copy', 'synthetic'];

export function mappingCells(mappings: readonly DataMappingItem[]): Map<string, DataMappingItem[]> {
  const cells = new Map<string, DataMappingItem[]>();
  for (const m of mappings) {
    const k = cellKey(m.domain.key, m.location.dataLocationId);
    const list = cells.get(k) ?? [];
    list.push(m);
    cells.set(k, list);
  }
  for (const list of cells.values()) list.sort((a, b) => ORIGIN_ORDER.indexOf(a.origin) - ORIGIN_ORDER.indexOf(b.origin));
  return cells;
}

// Domains against applications: one column per application with a shown use, by name.
export function appColumns(uses: readonly DataUseItem[]): MatrixColumn[] {
  const seen = new Map<string, MatrixColumn>();
  for (const u of uses) {
    const id = u.businessApplicationId.toLowerCase();
    if (!seen.has(id)) seen.set(id, { id, label: u.applicationName || 'Unnamed application', platform: null });
  }
  return [...seen.values()].sort(byLabel);
}

export function useCells(uses: readonly DataUseItem[]): Map<string, DataUseItem[]> {
  const cells = new Map<string, DataUseItem[]>();
  for (const u of sortUses(uses)) {
    const k = cellKey(u.domain.key, u.businessApplicationId);
    const list = cells.get(k) ?? [];
    list.push(u);
    cells.set(k, list);
  }
  return cells;
}

// The copy mappings a person may need to act on: a copy outside production with a copy state, the ones to act on first.
export function copiesToWatch(mappings: readonly DataMappingItem[]): DataMappingItem[] {
  return mappings
    .filter(m => m.copyState !== null)
    .sort((a, b) => STATE_ORDER.indexOf(a.copyState!) - STATE_ORDER.indexOf(b.copyState!) || a.domain.path.localeCompare(b.domain.path));
}

// ---------------------------------------------------------------------------------------------------------------------
// Uses
// ---------------------------------------------------------------------------------------------------------------------

// The Uses tab's state filter: a group of states, or one state.
export const USE_STATE_FILTERS = ['attention', 'proposed', 'allowed', 'denied', 'not-needed'] as const;
export type UseStateFilter = (typeof USE_STATE_FILTERS)[number];

export const USE_STATE_FILTER_TEXT: Record<UseStateFilter, string> = {
  attention: 'Needs a decision or a change',
  proposed: 'Waiting for approval',
  allowed: 'Allowed',
  denied: 'Denied',
  'not-needed': 'No decision needed',
};

export function useMatchesState(use: Pick<DataUseItem, 'state'>, filter: UseStateFilter | undefined): boolean {
  if (!filter) return true;
  if (filter === 'attention') return needsAttention(use.state);
  return use.state === filter;
}

export interface UseFilters {
  state?: UseStateFilter;
  aiOnly?: boolean;
  access?: DataAccess;
  text?: string;
  domainKeys?: ReadonlySet<string> | null;
  applicationId?: string;
  locationIds?: ReadonlySet<string> | null;
}

export function useMatches(use: DataUseItem, f: UseFilters): boolean {
  if (!useMatchesState(use, f.state)) return false;
  if (f.aiOnly && !use.reachesAi) return false;
  if (f.access && use.access !== f.access) return false;
  if (f.domainKeys && !f.domainKeys.has(use.domain.key)) return false;
  if (f.applicationId && !sameId(use.businessApplicationId, f.applicationId)) return false;
  if (f.locationIds && !(use.location && [...f.locationIds].some(id => sameId(id, use.location!.dataLocationId)))) return false;
  if (f.text && !matchesText(f.text, use.recordNumber, use.applicationName, use.domain.path, use.location?.label, use.purpose, use.aiServiceAsSeen)) return false;
  return true;
}

// The order the states read in: what someone must act on first.
const STATE_ORDER: DataUseState[] = ['breaks-rule', 'no-one-to-ask', 'needed', 're-decide', 'proposed', 'denied', 'allowed', 'not-needed'];

export function sortUses<T extends Pick<DataUseItem, 'state' | 'applicationName' | 'domain' | 'recordNumber'>>(uses: readonly T[]): T[] {
  return [...uses].sort((a, b) => STATE_ORDER.indexOf(a.state) - STATE_ORDER.indexOf(b.state)
    || a.applicationName.localeCompare(b.applicationName)
    || a.domain.path.localeCompare(b.domain.path)
    || (a.recordNumber ?? '').localeCompare(b.recordNumber ?? ''));
}

export function stateCounts(uses: readonly Pick<DataUseItem, 'state'>[]): Record<UseStateFilter, number> {
  const counts = { attention: 0, proposed: 0, allowed: 0, denied: 0, 'not-needed': 0 } as Record<UseStateFilter, number>;
  for (const u of uses) for (const f of USE_STATE_FILTERS) if (useMatchesState(u, f)) counts[f] += 1;
  return counts;
}

const isOpen = (row: { endedOn: string | null; status: { meaning: string } }) => !row.endedOn && row.status.meaning !== 'archived';

// The acts a use offers, from its state and status. The person's tools decide which of them show.
export interface UseActions {
  // A person confirms an AI client's draft.
  confirm: boolean;
  // A decision is needed and none is proposed yet.
  propose: boolean;
  // A decision about it exists: open it.
  openDecision: boolean;
  // The approver resolves to nobody: appoint a data owner first.
  proposeOwner: boolean;
  // The rule does not allow it: end it, or change it (masked data).
  mustChange: boolean;
  edit: boolean;
  end: boolean;
}

export function useActionsOf(use: Pick<DataUseItem, 'state' | 'status' | 'decisionId' | 'endedOn'>): UseActions {
  const open = isOpen(use);
  return {
    confirm: open && use.status.meaning === 'draft',
    propose: open && use.status.meaning !== 'draft' && (use.state === 'needed' || use.state === 're-decide'),
    openDecision: !!use.decisionId,
    proposeOwner: open && use.state === 'no-one-to-ask',
    mustChange: open && use.state === 'breaks-rule',
    edit: open,
    end: open,
  };
}

// The acts a copy mapping offers.
export function mappingActionsOf(m: Pick<DataMappingItem, 'copyState' | 'status' | 'decisionId' | 'endedOn'>): { propose: boolean; openDecision: boolean; mustChange: boolean; edit: boolean; end: boolean } {
  const open = isOpen(m);
  return {
    propose: open && m.status.meaning !== 'draft' && (m.copyState === 'needed' || m.copyState === 're-decide'),
    openDecision: !!m.decisionId,
    mustChange: open && m.copyState === 'breaks-rule',
    edit: open,
    end: open,
  };
}

// ---------------------------------------------------------------------------------------------------------------------
// Gaps
// ---------------------------------------------------------------------------------------------------------------------

export function groupFindings(items: readonly DataFinding[]): { check: DataCheckCode | null; label: string; items: DataFinding[] }[] {
  const groups = new Map<string, DataFinding[]>();
  for (const f of items) {
    const list = groups.get(f.check) ?? [];
    list.push(f);
    groups.set(f.check, list);
  }
  const known = DATA_CHECK_CODES.filter(c => groups.has(c)).map(c => ({ check: c as DataCheckCode | null, label: c as string, items: groups.get(c)! }));
  const other = [...groups.entries()].filter(([c]) => !(DATA_CHECK_CODES as readonly string[]).includes(c)).map(([c, list]) => ({ check: null, label: c, items: list }));
  return [...known, ...other];
}

export function checkCounts(items: readonly DataFinding[]): Partial<Record<DataCheckCode, number>> {
  const out: Partial<Record<DataCheckCode, number>> = {};
  for (const f of items) if ((DATA_CHECK_CODES as readonly string[]).includes(f.check)) out[f.check as DataCheckCode] = (out[f.check as DataCheckCode] ?? 0) + 1;
  return out;
}

// The one act that fixes a finding (docs/architecture/data-taxonomy.md, Data checks; a choice under D-41).
export type FindingFix =
  | { kind: 'owner-domain'; domainKey: string; ownerRoleKey?: string }
  | { kind: 'owner-location'; dataLocationId: string; ownerRoleKey?: string }
  | { kind: 'confirm-seen'; dataLocationId: string }
  | { kind: 'decide-use'; dataUseId: string; decisionTypeKey: 'data-use' | 'data-retention' }
  | { kind: 'change-use'; dataUseId: string }
  | { kind: 'declare-use'; applicationId: string; applicationName: string }
  | { kind: 'copy'; dataMappingId: string }
  | { kind: 'add-mapping'; domainKey: string; dataLocationId: string }
  | { kind: 'tier'; applicationId: string }
  | { kind: 'move'; domainKey: string }
  | { kind: 'none' };

export function fixOf(f: DataFinding): FindingFix {
  const domainKey = f.domain?.key ?? (f.subjectKind === 'data-domain' ? f.subjectId : null);
  switch (f.check) {
    case 'DC-01':
    case 'DC-02':
      return domainKey ? { kind: 'owner-domain', domainKey, ownerRoleKey: 'data-owner' } : { kind: 'none' };
    case 'DC-03':
    case 'DC-04': {
      // The message names the owner role ("The data-owner of People / Payroll has no deputy"), so the sheet opens on it.
      const role = f.ownerRoleKey ?? /^The (?:deputy )?([a-z][a-z0-9-]*) of /.exec(f.message)?.[1];
      if (f.subjectKind === 'data-location') return { kind: 'owner-location', dataLocationId: f.subjectId, ...(role ? { ownerRoleKey: role } : {}) };
      return domainKey ? { kind: 'owner-domain', domainKey, ...(role ? { ownerRoleKey: role } : {}) } : { kind: 'none' };
    }
    case 'DC-06':
      return domainKey ? { kind: 'owner-domain', domainKey, ownerRoleKey: 'data-owner' } : { kind: 'none' };
    case 'DC-12':
      if (f.code === 'data.location-stale') return { kind: 'confirm-seen', dataLocationId: f.subjectId };
      return { kind: 'owner-location', dataLocationId: f.subjectId };
    case 'DC-05':
      return f.subjectKind === 'data-use' ? { kind: 'decide-use', dataUseId: f.subjectId, decisionTypeKey: 'data-use' } : { kind: 'none' };
    case 'DC-13':
      return f.subjectKind === 'data-use' ? { kind: 'decide-use', dataUseId: f.subjectId, decisionTypeKey: 'data-retention' } : { kind: 'none' };
    case 'DC-07':
      return f.subjectKind === 'data-use' ? { kind: 'change-use', dataUseId: f.subjectId } : { kind: 'none' };
    case 'DC-10':
      return f.businessApplicationId ? { kind: 'declare-use', applicationId: f.businessApplicationId, applicationName: f.applicationName ?? '' } : { kind: 'none' };
    case 'DC-08':
      return f.subjectKind === 'data-mapping' ? { kind: 'copy', dataMappingId: f.subjectId } : { kind: 'none' };
    case 'DC-11':
      return domainKey && f.dataLocationId ? { kind: 'add-mapping', domainKey, dataLocationId: f.dataLocationId } : { kind: 'none' };
    case 'DC-09':
      return f.businessApplicationId ? { kind: 'tier', applicationId: f.businessApplicationId } : { kind: 'none' };
    case 'DC-14':
      return domainKey ? { kind: 'move', domainKey } : { kind: 'none' };
    default:
      return { kind: 'none' };
  }
}

// The findings' own words as the engine wrote them, made fit for a person: a sentence that names a tool or a row ID is
// dropped (the page offers the act itself), and a known key reads as its label.
export function plainFinding(message: string, keyLabels: ReadonlyMap<string, string> = new Map()): string {
  const guid = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/i;
  const tool = /\b[a-z]+_[a-z_]+\b/;
  // A message that starts by naming its row ("DU-0007 (Field Knowledge MCP Server, Field operations): ...") keeps what
  // follows: the page names the row itself.
  const body = message.replace(/^[^:()]{1,40} \([^)]{1,300}\): /, '');
  const sentences = body.split(/(?<=\.)\s+/).filter(s => s.trim() && !tool.test(s) && !guid.test(s));
  let text = sentences.join(' ');
  text = text.charAt(0).toUpperCase() + text.slice(1);
  for (const [key, label] of keyLabels) text = text.replace(new RegExp(`(?<![a-z-])${key.replace(/[-]/g, '\\-')}(?![a-z-])`, 'g'), label.toLowerCase());
  return text.trim();
}

// The checks whose engine message adds a fact the sentence does not hold: the rule and where it is set, the copy rule,
// the day a location was last seen, the retention it waits for.
export const DETAIL_CHECKS: readonly string[] = ['DC-05', 'DC-06', 'DC-07', 'DC-08', 'DC-12', 'DC-13'];

// The words for a finding: a sentence built from its fields (never a raw key or ID), and the engine's own detail.
export function findingText(f: DataFinding, names: { location: (id: string | null) => string | null; ownerRole: (messageRoleKey: string | null) => string | null }): string {
  const path = f.domain?.path ?? 'A domain';
  const app = f.applicationName ?? 'An application';
  const location = names.location(f.dataLocationId) ?? (f.subjectKind === 'data-location' ? names.location(f.subjectId) : null) ?? 'A location';
  const who = f.principal?.displayLabel || null;
  const subject = f.subjectKind === 'data-location' ? location : path;
  // The finding names its owner role and the deputy flag (never parsed from the message, which can word it any way).
  const roleKey = f.ownerRoleKey ?? /^The (?:deputy )?([a-z][a-z0-9-]*) of /.exec(f.message)?.[1] ?? null;
  const deputy = f.isDeputy ?? /^The deputy /.test(f.message);
  const role = names.ownerRole(roleKey) ?? 'owner';
  switch (f.check) {
    case 'DC-01': return `${path} has no data owner, of its own or above it.`;
    case 'DC-02': return `${path} needs a data owner of its own. It has only an inherited one.`;
    case 'DC-03': return `The ${role.toLowerCase()} of ${subject}${who ? `, ${who},` : ''} has no deputy.`;
    case 'DC-04': return deputy
      ? `${who ?? 'A deputy'} holds the deputy ${role.toLowerCase()} role on ${subject} and is disabled, deleted or leaving.`
      : `${who ?? 'An owner'} holds the ${role.toLowerCase()} role on ${subject} and is disabled, deleted or leaving.`;
    case 'DC-05': return `${app} uses ${path}, and no decision allows it yet.`;
    case 'DC-06': return `${app} uses ${path}, and no one can answer the decision it needs.`;
    case 'DC-07': return `${app} uses ${path} in a way the rules do not allow.`;
    case 'DC-08': return `${location} holds a copy of ${path} outside production.`;
    case 'DC-09': return `${app}'s sensitivity answer ranks below the data it uses.`;
    case 'DC-10': return `${app} has a data dependency or uses AI, and declares no data use.`;
    case 'DC-11': return `${app} reaches ${path} at ${location}, where that data is not mapped.`;
    case 'DC-12': return f.code === 'data.location-stale' ? `${location} has not been confirmed seen recently.` : `${location} has no technical owner.`;
    case 'DC-13': return `${app} is retiring and writes ${path}, with no retention decision yet.`;
    case 'DC-14': return `A row still points at ${path}, which is retired.`;
    default: return plainFinding(f.message);
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// The data write sheets' inputs
// ---------------------------------------------------------------------------------------------------------------------

export interface Problem {
  problem: string;
  field?: string;
}

export const isProblem = (v: unknown): v is Problem => typeof v === 'object' && v !== null && typeof (v as Problem).problem === 'string';

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const KEY = /^[a-z][a-z0-9-]{0,63}$/;

// Container names as typed, one per line or comma: trimmed, without repeats, at most 100 of 200 characters each.
export function parseContainers(text: string): { list: string[] } | Problem {
  const list = [...new Set(text.split(/[\n,]/).map(s => s.trim()).filter(Boolean))];
  if (list.length > 100) return { problem: 'Name at most 100 containers. Leave the field empty to map the whole location.', field: 'items.containers' };
  const long = list.find(s => s.length > 200);
  if (long) return { problem: `A container name is longer than 200 characters: "${long.slice(0, 40)}...".`, field: 'items.containers' };
  return { list };
}

export interface UseForm {
  applicationId: string;
  domainKey: string;
  locationId: string;
  access: DataAccess | '';
  purpose: string;
  reachesAi: boolean;
  aiServiceAsSeen: string;
  isDeidentified: boolean;
  accessApproachKey: string;
  startOn: string;
  endOn: string;
}

export function useFormOf(use: DataUseItem | undefined, applicationId: string, domainKey = ''): UseForm {
  return {
    applicationId: use?.businessApplicationId ?? applicationId,
    domainKey: use?.domain.key ?? domainKey,
    locationId: use?.location?.dataLocationId ?? '',
    access: use?.access ?? '',
    purpose: use?.purpose ?? '',
    reachesAi: use?.reachesAi ?? false,
    aiServiceAsSeen: use?.aiServiceAsSeen ?? '',
    isDeidentified: use?.isDeidentified ?? false,
    accessApproachKey: use?.accessApproach?.key ?? '',
    startOn: '',
    endOn: '',
  };
}

export type UseMode = 'declare' | 'edit' | 'confirm' | 'end';

export function buildUseInput(form: UseForm, mode: UseMode, use?: DataUseItem): WriteInput<'save_data_use'> | Problem {
  if (mode === 'end') return buildEndUseInput(form.endOn, use);
  if (!form.applicationId) return { problem: 'Choose the application that uses the data.', field: 'applicationId' };
  if (!form.domainKey || !KEY.test(form.domainKey)) return { problem: 'Choose the data, by its domain.', field: 'domainKey' };
  if (!form.access) return { problem: 'Say whether the application reads the data, writes it, or both.', field: 'access' };
  const purpose = form.purpose.trim();
  if (!purpose) return { problem: 'Say in one sentence why the application needs the data.', field: 'purpose' };
  if (purpose.length > 500) return { problem: 'Keep the purpose to 500 characters.', field: 'purpose' };
  const ai = form.aiServiceAsSeen.trim();
  if (ai.length > 200) return { problem: 'Keep the AI service name to 200 characters.', field: 'aiServiceAsSeen' };
  if (mode !== 'declare' && !use) return { problem: 'The use to change could not be read. Close the sheet and try again.' };
  if (form.startOn && !DATE.test(form.startOn)) return { problem: 'Give the start date as a date.', field: 'startOn' };
  const input: WriteInput<'save_data_use'> = {
    applicationId: form.applicationId,
    domainKey: form.domainKey,
    access: form.access,
    purpose,
    reachesAi: form.reachesAi,
  };
  if (use && mode !== 'declare') input.dataUseId = use.dataUseId;
  if (form.locationId) input.locationId = form.locationId;
  if (form.reachesAi && ai) input.aiServiceAsSeen = ai;
  if (form.isDeidentified) input.isDeidentified = true;
  if (form.accessApproachKey) input.accessApproachKey = form.accessApproachKey;
  if (form.startOn && mode === 'declare') input.startOn = form.startOn;
  if (mode === 'confirm') input.confirm = true;
  return input;
}

// Ending a use sends the use as it is on record now, with the day it ends: never a form seeded earlier, so a change saved
// a moment before on the same page stays (wave 4b QA).
export function buildEndUseInput(endOn: string, use?: DataUseItem): WriteInput<'save_data_use'> | Problem {
  if (!use) return { problem: 'The use to change could not be read. Close the sheet and try again.' };
  if (!endOn || !DATE.test(endOn)) return { problem: 'Give the day the use ends.', field: 'endOn' };
  const input: WriteInput<'save_data_use'> = {
    dataUseId: use.dataUseId,
    applicationId: use.businessApplicationId,
    domainKey: use.domain.key,
    access: use.access,
    purpose: use.purpose,
    reachesAi: use.reachesAi,
    endOn,
  };
  if (use.location) input.locationId = use.location.dataLocationId;
  if (use.reachesAi && use.aiServiceAsSeen) input.aiServiceAsSeen = use.aiServiceAsSeen;
  if (use.isDeidentified) input.isDeidentified = true;
  if (use.accessApproach) input.accessApproachKey = use.accessApproach.key;
  return input;
}

export interface LocationForm {
  platformKey: string;
  parentLocationId: string;
  targetStageKey: string;
  labelAsSeen: string;
  externalId: string;
  url: string;
  linkKindKey: string;
  deploymentTargetId: string;
  hostedByApplicationId: string;
  seenNow: boolean;
}

export function locationFormOf(location: DataLocationItem | undefined, parentLocationId = ''): LocationForm {
  return {
    platformKey: location?.platform.key ?? '',
    parentLocationId: location?.parent?.dataLocationId ?? parentLocationId,
    targetStageKey: location?.stage?.key ?? '',
    labelAsSeen: location?.label ?? '',
    externalId: location?.externalId ?? '',
    url: location?.url ?? '',
    linkKindKey: location?.linkKind.key ?? '',
    deploymentTargetId: location?.deploymentTargetId ?? '',
    hostedByApplicationId: location?.hostedByApplication?.businessApplicationId ?? '',
    seenNow: !location,
  };
}

// confirm: a person checks a draft (an AI client's) and confirms it, so it is no longer a draft.
export type LocationMode = 'add' | 'edit' | 'confirm' | 'confirm-seen' | 'retire';

type LocationItemInput = WriteInput<'save_data_location'>['items'][number];

export function buildLocationInput(form: LocationForm, mode: LocationMode, location?: DataLocationItem): WriteInput<'save_data_location'> | Problem {
  if (mode !== 'add' && !location) return { problem: 'The location could not be read. Close the sheet and try again.' };
  // Seen today on a draft also confirms it, as the sheet says.
  if (mode === 'confirm-seen') return { items: [{ dataLocationId: location!.dataLocationId, seenNow: true, ...(location!.status.meaning === 'draft' ? { confirm: true } : {}) }] };
  if (mode === 'retire') return { items: [{ dataLocationId: location!.dataLocationId, retire: true }] };
  if (!form.platformKey) return { problem: 'Choose the kind of store.', field: 'items.platformKey' };
  if (!form.parentLocationId && !form.targetStageKey) return { problem: 'Choose the stage: production, test or development.', field: 'items.targetStageKey' };
  const label = form.labelAsSeen.trim();
  if (!label) return { problem: 'Give the name the store goes by.', field: 'items.labelAsSeen' };
  if (label.length > 200) return { problem: 'Keep the name to 200 characters.', field: 'items.labelAsSeen' };
  const externalId = form.externalId.trim();
  const url = form.url.trim();
  // A reader sees neither (restricted read): an edit that shows none keeps what is on record.
  const hidden = (mode === 'edit' || mode === 'confirm') && location!.externalId === null && location!.url === null;
  if (!externalId && !url && !hidden) return { problem: 'Give the ID the store has in its own tool, its link, or both.', field: 'items.externalId' };
  if (externalId.length > 400) return { problem: 'Keep the ID to 400 characters.', field: 'items.externalId' };
  if (url && !/^https:\/\/\S+$/.test(url)) return { problem: 'A link starts with https:// and has no spaces.', field: 'items.url' };
  const item: LocationItemInput = { platformKey: form.platformKey, labelAsSeen: label };
  if (mode === 'edit' || mode === 'confirm') item.dataLocationId = location!.dataLocationId;
  if (mode === 'confirm') item.confirm = true;
  if (form.parentLocationId) item.parentLocationId = form.parentLocationId;
  else item.targetStageKey = form.targetStageKey;
  if (externalId) item.externalId = externalId;
  if (url) item.url = url;
  if (form.linkKindKey) item.linkKindKey = form.linkKindKey;
  if (form.deploymentTargetId) item.deploymentTargetId = form.deploymentTargetId;
  if (form.hostedByApplicationId) item.hostedByApplicationId = form.hostedByApplicationId;
  if (form.seenNow) item.seenNow = true;
  return { items: [item] };
}

export interface MappingForm {
  domainKey: string;
  locationId: string;
  origin: DataOrigin | '';
  containers: string;
  copiedFromMappingId: string;
  endOn: string;
}

export function mappingFormOf(mapping: DataMappingItem | undefined, domainKey = '', dataLocationId = ''): MappingForm {
  return {
    domainKey: mapping?.domain.key ?? domainKey,
    locationId: mapping?.location.dataLocationId ?? dataLocationId,
    origin: mapping?.origin ?? '',
    containers: (mapping?.containers ?? []).join('\n'),
    copiedFromMappingId: mapping?.copiedFromMappingId ?? '',
    endOn: '',
  };
}

// confirm: a person checks a draft mapping (an AI client's) and confirms it.
export type MappingMode = 'add' | 'edit' | 'confirm' | 'end';

// The origins that come from another mapping.
export const COPY_ORIGINS: readonly DataOrigin[] = ['replica', 'production-copy', 'masked-copy'];

export function buildMappingInput(form: MappingForm, mode: MappingMode, mapping?: DataMappingItem): WriteInput<'save_data_mapping'> | Problem {
  if (mode !== 'add' && !mapping) return { problem: 'The mapping could not be read. Close the sheet and try again.' };
  if (mode === 'end') {
    // Ending sends the mapping as it is on record now, with the day the data left (wave 4b QA).
    if (!form.endOn || !DATE.test(form.endOn)) return { problem: 'Give the day the data left.', field: 'items.endOn' };
    const ended: WriteInput<'save_data_mapping'>['items'][number] = { mappingId: mapping!.dataMappingId, domainKey: mapping!.domain.key, locationId: mapping!.location.dataLocationId, origin: mapping!.origin, endOn: form.endOn };
    if (mapping!.containers.length) ended.containers = [...mapping!.containers];
    if (mapping!.copiedFromMappingId && COPY_ORIGINS.includes(mapping!.origin)) ended.copiedFromMappingId = mapping!.copiedFromMappingId;
    return { items: [ended] };
  }
  if (!form.domainKey || !KEY.test(form.domainKey)) return { problem: 'Choose the data, by its domain.', field: 'items.domainKey' };
  if (!form.locationId) return { problem: 'Choose where the data sits.', field: 'items.locationId' };
  if (!form.origin) return { problem: 'Say where this data comes from.', field: 'items.origin' };
  const containers = parseContainers(form.containers);
  if (isProblem(containers)) return containers;
  const item: WriteInput<'save_data_mapping'>['items'][number] = { domainKey: form.domainKey, locationId: form.locationId, origin: form.origin };
  if (mapping) item.mappingId = mapping.dataMappingId;
  if (containers.list.length) item.containers = containers.list;
  if (form.copiedFromMappingId && COPY_ORIGINS.includes(form.origin)) item.copiedFromMappingId = form.copiedFromMappingId;
  if (mode === 'confirm') item.confirm = true;
  return { items: [item] };
}

export interface OwnerForm {
  ownerRoleKey: string;
  isDeputy: boolean;
  person: PrincipalRef | null;
  // The assignment that ends when the decision is accepted, or ''.
  endAssignmentId: string;
  startOn: string;
  reason: string;
}

export type OwnerSubject = { kind: 'data-domain'; domainKey: string } | { kind: 'data-location'; dataLocationId: string };

export function buildOwnerInput(form: OwnerForm, subject: OwnerSubject): WriteInput<'propose_owner_change'> | Problem {
  if (!form.ownerRoleKey) return { problem: 'Choose the owner role.', field: 'changes.ownerRoleKey' };
  if (!form.person && !form.endAssignmentId) return { problem: 'Find the person to name, or choose an assignment to end.', field: 'changes.addObjectId' };
  if (form.startOn && !DATE.test(form.startOn)) return { problem: 'Give the start date as a date.', field: 'changes.startOn' };
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say why the change is needed. The approvers read it.', field: 'reason' };
  if (reason.length > 2000) return { problem: 'Keep the reason to 2,000 characters.', field: 'reason' };
  const change: WriteInput<'propose_owner_change'>['changes'][number] = { ownerRoleKey: form.ownerRoleKey };
  if (form.person) change.addObjectId = form.person.objectId;
  if (form.endAssignmentId) change.endAssignmentId = form.endAssignmentId;
  if (form.isDeputy && form.person) change.isDeputy = true;
  if (form.startOn && form.person) change.startOn = form.startOn;
  return subject.kind === 'data-domain'
    ? { dataDomainKey: subject.domainKey, changes: [change], reason }
    : { dataLocationId: subject.dataLocationId, changes: [change], reason };
}

export interface DecisionForm {
  decisionTypeKey: string;
  statement: string;
  context: string;
  outcomeKey: string;
  reason: string;
  optionText: string;
  optionWhyNot: string;
}

export function buildDecisionInput(form: DecisionForm, subjectKind: 'data-use' | 'data-mapping', subjectId: string, needsOptions = false): WriteInput<'save_decision_proposal'> | Problem {
  if (!subjectId) return { problem: 'The record the decision is about could not be read. Close the sheet and try again.' };
  if (!form.decisionTypeKey) return { problem: 'Choose the kind of decision.', field: 'decisionTypeKey' };
  const statement = form.statement.trim();
  if (!statement) return { problem: 'Say in one sentence what is decided.', field: 'statement' };
  if (statement.length > 500) return { problem: 'Keep the statement to 500 characters.', field: 'statement' };
  const context = form.context.trim();
  if (!context) return { problem: 'Describe the situation the approvers need to know.', field: 'context' };
  if (context.length > 4000) return { problem: 'Keep the context to 4,000 characters.', field: 'context' };
  if (!form.outcomeKey) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say why you propose this outcome.', field: 'reason' };
  if (reason.length > 2000) return { problem: 'Keep the reason to 2,000 characters.', field: 'reason' };
  const option = form.optionText.trim();
  const whyNot = form.optionWhyNot.trim();
  if (needsOptions && (!option || !whyNot)) return { problem: 'This kind of decision asks for an option you weighed and why it was not chosen.', field: 'optionsConsidered' };
  if ((option && !whyNot) || (!option && whyNot)) return { problem: 'Give the option you weighed and why it was not chosen, or leave both empty.', field: 'optionsConsidered' };
  if (option.length > 500 || whyNot.length > 1000) return { problem: 'Keep the option to 500 characters and the reason it was not chosen to 1,000.', field: 'optionsConsidered' };
  const input: WriteInput<'save_decision_proposal'> = {
    decisionTypeKey: form.decisionTypeKey,
    subjectKind,
    subjectId,
    statement,
    context,
    proposedOutcomeKey: form.outcomeKey,
    reason,
  };
  if (option && whyNot) input.optionsConsidered = [{ option, whyNot }];
  return input;
}

export interface MoveForm {
  toDomainKey: string;
  mappings: boolean;
  uses: boolean;
  reason: string;
}

export function buildMoveInput(form: MoveForm, fromDomainKey: string): WriteInput<'move_data_references'> | Problem {
  if (!form.toDomainKey) return { problem: 'Choose the domain the rows move to.', field: 'toDomainKey' };
  if (form.toDomainKey === fromDomainKey) return { problem: 'Choose another domain: the rows already sit here.', field: 'toDomainKey' };
  const include: ('mappings' | 'uses')[] = [];
  if (form.mappings) include.push('mappings');
  if (form.uses) include.push('uses');
  if (!include.length) return { problem: 'Choose what moves: the mappings, the uses, or both.', field: 'include' };
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say why the rows move, such as the split it serves.', field: 'reason' };
  if (reason.length > 500) return { problem: 'Keep the reason to 500 characters.', field: 'reason' };
  return { fromDomainKey, toDomainKey: form.toDomainKey, include, reason };
}

// The decision a write opened and did not send: the follow-up submit sheet offers to send it.
export function unsentDecision(data: unknown): string | null {
  const d = data as { decisionId?: unknown; decisionSubmitted?: unknown; items?: { decisionId?: unknown; decisionSubmitted?: unknown }[] } | null;
  if (!d) return null;
  if (typeof d.decisionId === 'string' && d.decisionSubmitted !== true) return d.decisionId;
  const item = (d.items ?? []).find(i => typeof i.decisionId === 'string' && i.decisionSubmitted !== true);
  return item ? (item.decisionId as string) : null;
}

// ---------------------------------------------------------------------------------------------------------------------
// The shared filters as sets every tab reads
// ---------------------------------------------------------------------------------------------------------------------

export interface DataScope {
  // The domains the shared filters reach, or null for every domain.
  domainKeys: Set<string> | null;
  // One location and the stores inside it, or null.
  locationIds: Set<string> | null;
  // The locations an application's uses name, or null.
  applicationLocationIds: Set<string> | null;
}

const intersect = (a: Set<string> | null, b: Set<string>): Set<string> => (a ? new Set([...a].filter(k => b.has(k))) : b);

// domain=<key> reaches its subtree; mine=1 the domains the person owns, deputizes or stewards; app=<id> the domains its
// uses name; location=<id> the domains it and the stores inside it hold. Each narrows the others.
export function dataScope(input: {
  tree: DomainTree | null;
  domainKey?: string;
  mineKeys?: ReadonlySet<string> | null;
  appUses?: readonly DataUseItem[] | null;
  locationId?: string;
  locations: readonly DataLocationItem[];
}): DataScope {
  let domainKeys: Set<string> | null = null;
  if (input.domainKey) domainKeys = input.tree ? subtreeOf(input.tree, input.domainKey) : new Set([input.domainKey]);
  if (input.mineKeys) domainKeys = intersect(domainKeys, new Set(input.mineKeys));
  let applicationLocationIds: Set<string> | null = null;
  if (input.appUses) {
    domainKeys = intersect(domainKeys, new Set(input.appUses.map(u => u.domain.key)));
    applicationLocationIds = new Set(input.appUses.flatMap(u => (u.location ? [u.location.dataLocationId.toLowerCase()] : [])));
  }
  let locationIds: Set<string> | null = null;
  if (input.locationId) {
    const id = input.locationId.toLowerCase();
    const inside = input.locations.filter(l => l.dataLocationId.toLowerCase() === id || l.parent?.dataLocationId.toLowerCase() === id);
    locationIds = new Set([id, ...inside.map(l => l.dataLocationId.toLowerCase())]);
    domainKeys = intersect(domainKeys, new Set(inside.flatMap(l => l.domains.map(d => d.domain.key))));
  }
  return { domainKeys, locationIds, applicationLocationIds };
}

// A mapping in the scope of the shared filters and the Mapping tab's own: the domains in scope, the location (and the
// stores inside it), the stage, and the words of the search (domain path, location, platform, stage, origin).
export interface MappingFilters {
  domainKeys?: ReadonlySet<string> | null;
  locationIds?: ReadonlySet<string> | null;
  stageKey?: string;
  text?: string;
}

export function mappingMatches(m: DataMappingItem, f: MappingFilters): boolean {
  if (f.domainKeys && !f.domainKeys.has(m.domain.key)) return false;
  if (f.locationIds && !f.locationIds.has(m.location.dataLocationId.toLowerCase())) return false;
  if (f.stageKey && m.location.stage?.key !== f.stageKey) return false;
  if (f.text && !matchesText(f.text, m.domain.path, m.location.label, m.location.platform.label, m.location.stage?.label, DATA_ORIGIN_TEXT[m.origin])) return false;
  return true;
}

// A finding that holds the words of the Gaps tab's search: its sentence, the check, the domain, the application, the
// location and the person it names.
export function findingMatches(f: DataFinding, text: string | undefined, sentence: string, locationLabel: string | null): boolean {
  if (!text) return true;
  return matchesText(text, sentence, f.check, f.domain?.path, f.applicationName, locationLabel, f.principal?.displayLabel, f.suggestion?.applications.map(a => a.name));
}

// A finding the person should see under mine=1: about a domain they own, deputize or steward, or naming them.
export function isMyFinding(f: Pick<DataFinding, 'domain' | 'principal'>, mineKeys: ReadonlySet<string> | null, me: Me): boolean {
  return (!!f.domain && !!mineKeys && mineKeys.has(f.domain.key)) || isMe(me, f.principal);
}
