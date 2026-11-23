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
}

export function useLabels(entity: string): Labels {
  const state = useTool('get_configuration', { entity });
  const data = dataOf(state);
  return useMemo<Labels>(() => {
    const rows = new Map((data?.rows ?? []).map(r => [r.key, r]));
    const order = (data?.rows ?? []).map(r => r.key);
    return {
      label: key => (key ? rows.get(key)?.label ?? humanizeKey(key) : 'Not set'),
      field: <T,>(key: string, name: string) => rows.get(key)?.fields[name] as T | undefined,
      rank: key => {
        const i = key ? order.indexOf(key) : -1;
        return i < 0 ? Number.MAX_SAFE_INTEGER : i;
      },
    };
  }, [data]);
}
