// Reading child configuration rows as get_configuration returns them. The server answers a parent row (a value list,
// a decision type) without its children: each child is a row of its own entity, keyed "<parent>/<child>":
//   list-value          "change-result/succeeded"    (C07 under C06 value-list)
//   decision-outcome    "exception/accept"           (C19 under C18 decision-type)
// These helpers are pure, so the tests can feed them rows in the server's shape.
import type { ConfigurationRows } from './contracts';

export type ConfigRow = ConfigurationRows['rows'][number];

export interface KeyedChoice {
  key: string;
  label: string;
}

// The active child rows under one parent key, in configured order: a numeric sortOrder field where the rows carry one,
// else the order the server returned them in (key order).
export function childRows(rows: readonly ConfigRow[], parentKey: string): KeyedChoice[] {
  const prefix = `${parentKey}/`;
  return rows
    .map((r, i) => ({ r, i }))
    .filter(({ r }) => r.isActive && r.key.startsWith(prefix) && r.key.length > prefix.length)
    .map(({ r, i }) => ({ key: r.key.slice(prefix.length), label: r.label, at: typeof r.fields?.sortOrder === 'number' ? r.fields.sortOrder : Number.MAX_SAFE_INTEGER, i }))
    .sort((a, b) => a.at - b.at || a.i - b.i)
    .map(({ key, label }) => ({ key, label }));
}

// The outcomes a decision type allows: the decision-outcome rows under the type. A type row that still carries its own
// outcomes list (an older shape) is read when no child rows came back.
export function outcomesOf(outcomeRows: readonly ConfigRow[], typeRow: ConfigRow | undefined, typeKey: string): KeyedChoice[] {
  const fromRows = childRows(outcomeRows, typeKey);
  if (fromRows.length) return fromRows;
  const listed = typeRow?.fields?.outcomes;
  if (!Array.isArray(listed)) return [];
  return listed.flatMap(o => (o && typeof o === 'object' && typeof (o as { key?: unknown }).key === 'string'
    ? [{ key: (o as { key: string }).key, label: typeof (o as { label?: unknown }).label === 'string' ? (o as { label: string }).label : (o as { key: string }).key }]
    : []));
}
