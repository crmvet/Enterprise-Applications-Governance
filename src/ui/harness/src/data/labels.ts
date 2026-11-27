// Configured labels for keys that a tool returns without a label, such as dependencyTypeKey or targetStageKey.
// The page reads them once through get_configuration. If that read fails, the key is shown in plain words,
// so a label failure never blanks a panel.
import { useMemo } from 'react';
import { humanizeKey } from './format';
import { childRows, outcomesOf, type KeyedChoice } from './configRows';
import { dataOf, useTool } from './toolClient';

export interface Labels {
  label(key: string | null | undefined): string;
  field<T = unknown>(key: string, name: string): T | undefined;
  rank(key: string | null | undefined): number;
  // The configured keys in configured order (active rows only). Empty when the read failed or is still loading.
  keys(): string[];
  // True when the configuration returned a row with this key.
  has(key: string | null | undefined): boolean;
}

export function useLabels(entity: string): Labels {
  // top 100: get_configuration pages at 25 rows by default, and an entity such as decision-right holds more, so a label
  // past the 25th row by key showed as its key in words ("Priority a"; product review 2026-11-24). The same input as
  // useConfigRows, so both share one read.
  const state = useTool('get_configuration', { entity, top: 100 });
  const data = dataOf(state);
  return useMemo<Labels>(() => {
    const list = data?.rows ?? [];
    const rows = new Map(list.map(r => [r.key, r]));
    // Configured order: a numeric rank field where the entity has one (rank for tiers and target stages, stageRank for
    // lifecycle stages), else the order the rows came in.
    const rankOf = (r: (typeof list)[number], i: number) => {
      const rank = typeof r.fields.rank === 'number' ? r.fields.rank : r.fields.stageRank;
      return typeof rank === 'number' ? rank : i;
    };
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
      has: key => !!key && rows.has(key),
    };
  }, [data]);
}

// The values of one configured value list (C07 List Value rows "<list>/<value>"), such as change-result or confidence,
// in configured order. get_configuration returns them as rows of their own, never inside the value-list row.
export function useListValues(listKey: string): {
  options: KeyedChoice[];
  choices: { value: string; label: string }[];
  label: (key: string | null | undefined) => string;
  loading: boolean;
  loaded: boolean;
} {
  const state = useTool('get_configuration', { entity: 'list-value', top: 100 });
  const data = dataOf(state);
  return useMemo(() => {
    const options = childRows(data?.rows ?? [], listKey);
    return {
      options,
      choices: options.map(o => ({ value: o.key, label: o.label })),
      label: (key: string | null | undefined) => (key ? options.find(o => o.key === key)?.label ?? humanizeKey(key) : 'Not set'),
      loading: state.status === 'loading',
      loaded: data !== null,
    };
  }, [data, listKey, state.status]);
}

// The outcomes a decision type allows, from the configuration in force (C19 Decision Outcome rows "<type>/<outcome>"),
// and whether the type asks for the options considered.
export function useOutcomes(typeKey: string): { options: KeyedChoice[]; loading: boolean; needsOptions: boolean } {
  const outcomes = useTool('get_configuration', { entity: 'decision-outcome', top: 100 });
  const type = useTool('get_configuration', { entity: 'decision-type', keys: [typeKey] });
  const outcomeRows = dataOf(outcomes)?.rows;
  const typeRows = dataOf(type)?.rows;
  return useMemo(() => {
    const typeRow = typeRows?.find(r => r.key === typeKey);
    return {
      options: outcomesOf(outcomeRows ?? [], typeRow, typeKey),
      loading: outcomes.status === 'loading' || type.status === 'loading',
      needsOptions: typeRow?.fields.needsOptionsConsidered === true,
    };
  }, [outcomeRows, typeRows, typeKey, outcomes.status, type.status]);
}
