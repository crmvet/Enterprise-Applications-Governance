// Configured labels for keys that a tool returns without a label, such as dependencyTypeKey or targetStageKey.
// The page reads them once through get_configuration. If that read fails, the key is shown in plain words,
// so a label failure never blanks a panel.
import { useMemo } from 'react';
import { humanizeKey } from './format';
import { dataOf, useTool } from './toolClient';

export interface Labels {
  label(key: string | null | undefined): string;
  field<T = unknown>(key: string, name: string): T | undefined;
  rank(key: string | null | undefined): number;
  // The configured keys in configured order (active rows only). Empty when the read failed or is still loading.
  keys(): string[];
}

export function useLabels(entity: string): Labels {
  const state = useTool('get_configuration', { entity });
  const data = dataOf(state);
  return useMemo<Labels>(() => {
    const list = data?.rows ?? [];
    const rows = new Map(list.map(r => [r.key, r]));
    // Configured order: a numeric rank field where the entity has one (target stages), else the order the rows came in.
    const rankOf = (r: (typeof list)[number], i: number) => (typeof r.fields.rank === 'number' ? r.fields.rank : i);
    const sorted = list.map((r, i) => ({ r, at: rankOf(r, i), i })).sort((a, b) => a.at - b.at || a.i - b.i).map(x => x.r);
    const order = sorted.map(r => r.key);
    const active = sorted.filter(r => r.isActive).map(r => r.key);
    return {
      label: key => (key ? rows.get(key)?.label ?? humanizeKey(key) : 'Not set'),
      field: <T,>(key: string, name: string) => rows.get(key)?.fields[name] as T | undefined,
      rank: key => {
        const i = key ? order.indexOf(key) : -1;
        return i < 0 ? Number.MAX_SAFE_INTEGER : i;
      },
      keys: () => active,
    };
  }, [data]);
}
