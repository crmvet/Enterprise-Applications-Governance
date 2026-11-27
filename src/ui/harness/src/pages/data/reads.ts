// The Data page's reads, shared by its tabs and its sheets.
//   useDomains()                 every active domain (find_data_domains, up to 1,000), as a tree
//   useMineKeys(objectId)        the domains a person owns, deputizes or stewards, own or inherited (ownerObjectId)
//   useLocations(includeRetired) every location (find_data_locations, up to 500)
//   useUseList(scope)            every open data use across the domains (list_data_uses, up to 1,000), in one list
//   useMappingList(scope)        every open mapping across the domains (list_data_mappings, up to 1,000), in one list
//   useGaps(scope)               the findings in a scope (get_data_gaps, up to 1,000)
//
// Wave 4 (B24-21): the Uses, Mapping and Gaps tabs read one list across the domains instead of get_data_domain once per
// domain (up to 60 reads a tab). The page reads both lists once, with the domain or application filter sent to the tool,
// and filters the rest on the page, as it does the domains and the locations, so the tab counts, the figures and the
// lists agree and a tab change reads nothing again.
import { useMemo } from 'react';
import type { DataFinding, DataMappingItem, DataUseItem, Failure, GetDataGapsInput } from '../../data/contracts';
import { usePagedRead, type PagedState } from '../../data/reads';
import { buildDomainTree, type DomainTree } from './logic';

export function useDomains() {
  const read = usePagedRead('find_data_domains', { top: 100 }, d => d.items, { maxPages: 10 });
  const items = read.status === 'done' ? read.items : null;
  const tree = useMemo<DomainTree | null>(() => (items ? buildDomainTree(items) : null), [items]);
  return { read, tree, items: items ?? [] };
}

export function useMineKeys(objectId: string | null, enabled: boolean): { keys: Set<string> | null; failure: Failure | null } {
  const read = usePagedRead('find_data_domains', enabled && objectId ? { ownerObjectId: objectId, top: 100 } : null, d => d.items, { maxPages: 10 });
  const items = read.status === 'done' ? read.items : null;
  const keys = useMemo(() => (items ? new Set(items.map(i => i.key)) : null), [items]);
  return { keys: enabled ? keys : null, failure: enabled && read.status === 'failed' ? read.failure : null };
}

export function useLocations(includeRetired = false) {
  const read = usePagedRead('find_data_locations', includeRetired ? { top: 100, includeRetired: true } : { top: 100 }, d => d.items, { maxPages: 5 });
  return { read, items: read.status === 'done' ? read.items : [], loaded: read.status === 'done' };
}

export function useGaps(scope: Pick<GetDataGapsInput, 'domainKey' | 'applicationId' | 'locationId'>) {
  const input: Record<string, unknown> = { top: 100 };
  if (scope.domainKey) { input.domainKey = scope.domainKey; input.includeSubtree = true; }
  if (scope.applicationId) input.applicationId = scope.applicationId;
  if (scope.locationId) input.locationId = scope.locationId;
  const read = usePagedRead('get_data_gaps', input, d => d.items, { maxPages: 10 });
  return { read, items: read.status === 'done' ? read.items : ([] as DataFinding[]), loaded: read.status === 'done' };
}

// One list read across the domains, as the tabs hold it: the rows, whether they are read, the failure of the first page,
// and how many rows match when the page did not read them all.
export interface RowsRead<T> {
  items: T[];
  loaded: boolean;
  failure: Failure | null;
  // A later page that failed: the rows before it still show.
  laterFailure: Failure | null;
  // The rows the tool holds for the filters it was sent, when more than the page read; else null.
  notRead: number | null;
}

const MAX_PAGES = 10;

function rowsOf<T, D extends { totalCount: number }>(read: PagedState<T, D>): RowsRead<T> {
  if (read.status === 'failed') return { items: [], loaded: false, failure: read.failure, laterFailure: null, notRead: null };
  if (read.status !== 'done') return { items: [], loaded: false, failure: null, laterFailure: null, notRead: null };
  const total = read.first.data.totalCount;
  return {
    items: read.items,
    loaded: true,
    failure: null,
    laterFailure: read.laterFailure,
    notRead: read.skipToken && total > read.items.length ? total - read.items.length : null,
  };
}

// The open data uses in a scope: one domain (and the domains below it) or one application, else every domain.
export function useUseList(scope: { domainKey?: string; applicationId?: string }, enabled = true): RowsRead<DataUseItem> {
  const input: Record<string, unknown> = { top: 100 };
  if (scope.domainKey) input.domainKey = scope.domainKey;
  if (scope.applicationId) input.businessApplicationId = scope.applicationId;
  const read = usePagedRead('list_data_uses', enabled ? input : null, d => d.items, { maxPages: MAX_PAGES });
  return rowsOf(read);
}

// The open mappings in a scope: one domain (and the domains below it), else every domain.
export function useMappingList(scope: { domainKey?: string }, enabled = true): RowsRead<DataMappingItem> {
  const input: Record<string, unknown> = { top: 100 };
  if (scope.domainKey) input.domainKey = scope.domainKey;
  const read = usePagedRead('list_data_mappings', enabled ? input : null, d => d.items, { maxPages: MAX_PAGES });
  return rowsOf(read);
}
