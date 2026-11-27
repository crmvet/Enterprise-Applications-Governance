// Every configuration row of the version in force, read once and shared: the rules view lists them, the draft builder
// fills its choices from them, and every key a page shows turns into the label of the row it names. The Configuration
// page reads them once and provides them (ConfigRowsProvider); a sheet opened from another page reads them itself.
//   const config = useConfigRowsInForce();
//   config.rows                 every row, retired rows included (keys are never reused, so a new key avoids them too)
//   config.lookup(entity, key)  the label of a row, or null
//   config.version              the version in force, or null while it is read
import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { Failure } from '../../data/contracts';
import { usePagedRead } from '../../data/reads';
import { anyKeyLookup, lookupOf, type ConfigRow, type LabelLookup } from './logic';

export interface ConfigRowsInForce {
  rows: ConfigRow[];
  version: number | null;
  contentHash: string | null;
  loading: boolean;
  failure: Failure | null;
  // True when the product holds more rows than were read (more than 2,000).
  partial: boolean;
  lookup: LabelLookup;
  // The label of a key of any entity, for the engine's sentences.
  labelOfAnyKey: (key: string) => string | null;
  // The rows of one entity.
  of: (entity: string, includeRetired?: boolean) => ConfigRow[];
}

const ConfigRowsContext = createContext<ConfigRowsInForce | null>(null);

// 100 rows a page, up to 20 pages: Balanced ships about 650 rows.
const MAX_PAGES = 20;

function useReadAllRows(enabled: boolean): ConfigRowsInForce {
  const read = usePagedRead('get_configuration', enabled ? { top: 100, includeRetired: true } : null, d => d.rows, { maxPages: MAX_PAGES });
  const rows = useMemo(() => (read.status === 'done' ? read.items : []), [read]);
  const first = read.status === 'done' ? read.first : null;
  return useMemo<ConfigRowsInForce>(() => {
    const lookup = lookupOf(rows);
    const byEntity = new Map<string, ConfigRow[]>();
    for (const r of rows) {
      const list = byEntity.get(r.entity) ?? [];
      list.push(r);
      byEntity.set(r.entity, list);
    }
    return {
      rows,
      version: first?.data.configurationSet.version ?? null,
      contentHash: first?.data.configurationSet.contentHash ?? null,
      loading: read.status === 'loading',
      failure: read.status === 'failed' ? read.failure : read.status === 'done' ? read.laterFailure : null,
      partial: read.status === 'done' && !!read.skipToken,
      lookup,
      labelOfAnyKey: anyKeyLookup(rows),
      of: (entity, includeRetired = false) => (byEntity.get(entity) ?? []).filter(r => includeRetired || r.isActive),
    };
  }, [rows, first, read]);
}

export function ConfigRowsProvider({ children }: { children: ReactNode }) {
  const value = useReadAllRows(true);
  return <ConfigRowsContext.Provider value={value}>{children}</ConfigRowsContext.Provider>;
}

export function useConfigRowsInForce(): ConfigRowsInForce {
  const shared = useContext(ConfigRowsContext);
  const own = useReadAllRows(shared === null);
  return shared ?? own;
}
