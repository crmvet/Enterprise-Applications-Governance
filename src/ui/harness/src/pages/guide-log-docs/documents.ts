// The Documents page's own logic, apart from the page so it can be tested: the owner filter, the counts by type and by
// owner, which act each row offers and whether it shows for the person, the words for each state, and the gates that
// need a document type current.
//
// Design choices (D-41, 2026-11-24):
// - Each row offers the one act that moves it on: a missing document is linked; a draft is confirmed current; a stale
//   or current document is confirmed still true. All but the link go through confirm_document_checked: for a person it
//   records the check and makes the document current (an AI client's check of a type that needs a person leaves the
//   status as it was, OP-08). It sends nothing else, so it never changes the link, its owner or its last updated date,
//   which record_document_link would rewrite from what the list row lacks. Marking a document stale, and editing its
//   link, stay on the document's own page.
// - An act shows when whoami lists its tool and the person owns the document, owns the application, or holds a role
//   other than application owner. The Tool Policy lets an application owner act only on the applications they own; the
//   other roles it names act on any. whoami does not say which role grants a tool, so a person whose other roles do not
//   grant it may still see the act, and the engine refuses it with its reason.
// - The owner filter (?owner=<objectId>, or none) is the page's own: list_documents has no owner filter, so the page
//   narrows the rows it read. Its counts come from the same rows, so a bar and the list it opens agree.
import type { DocumentList, Id, Key } from '../../data/contracts';
import { daysBetween, fmtDate } from '../../data/format';

export type DocRow = DocumentList['items'][number];

export const NO_OWNER = 'none';
export const OWNER_PARAM = /^(none|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$/;

const same = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

export function ownerKeyOf(row: DocRow): string {
  return row.owner ? row.owner.objectId.toLowerCase() : NO_OWNER;
}

export function filterByOwner(rows: DocRow[], owner: string | undefined): DocRow[] {
  if (!owner) return rows;
  const key = owner.toLowerCase();
  return rows.filter(r => ownerKeyOf(r) === key);
}

export interface Bucket {
  key: string;
  label: string;
  count: number;
}

const byCount = (a: Bucket, b: Bucket) => b.count - a.count || a.label.localeCompare(b.label);

export function countByType(rows: DocRow[]): Bucket[] {
  const map = new Map<string, Bucket>();
  for (const r of rows) {
    const b = map.get(r.documentType.key) ?? { key: r.documentType.key, label: r.documentType.label, count: 0 };
    b.count += 1;
    map.set(r.documentType.key, b);
  }
  return [...map.values()].sort(byCount);
}

// Rows by owner. A missing document has no owner yet: the owners of its application link it.
export function countByOwner(rows: DocRow[]): Bucket[] {
  const map = new Map<string, Bucket>();
  for (const r of rows) {
    const key = ownerKeyOf(r);
    const b = map.get(key) ?? { key, label: r.owner?.displayLabel || (key === NO_OWNER ? 'No owner yet' : 'A person the product knows'), count: 0 };
    b.count += 1;
    map.set(key, b);
  }
  return [...map.values()].sort((a, b) => (a.key === NO_OWNER ? 1 : 0) - (b.key === NO_OWNER ? 1 : 0) || byCount(a, b));
}

export type RowAct = 'link' | 'confirm-current' | 'confirm-still-true';

export function actOf(row: DocRow): RowAct | null {
  switch (row.state) {
    case 'missing':
      return 'link';
    case 'draft':
      return row.documentLinkId ? 'confirm-current' : null;
    case 'stale':
    case 'current':
      return row.documentLinkId ? 'confirm-still-true' : null;
    default:
      return null;
  }
}

export interface ActRights {
  me: string | null;
  // The applications the person holds an owner role on now.
  ownedApps: ReadonlySet<string>;
  // The person holds a role other than application owner (see the design note above).
  otherRole: boolean;
  canLink: boolean;
  canConfirm: boolean;
}

export function mayAct(row: DocRow, act: RowAct, rights: ActRights): boolean {
  const ownsApp = rights.ownedApps.has(row.businessApplicationId.toLowerCase());
  if (act === 'link') return rights.canLink && (ownsApp || rights.otherRole);
  return rights.canConfirm && (same(row.owner?.objectId, rights.me) || ownsApp || rights.otherRole);
}

export const ACT_LABELS: Record<RowAct, string> = {
  link: 'Link it',
  'confirm-current': 'Confirm current',
  'confirm-still-true': 'Confirm still true',
};

// What the state means for this row, in words, against today.
export function stateText(row: DocRow, today: string): string {
  switch (row.state) {
    case 'missing':
      return row.requiredFrom ? `Required from the ${row.requiredFrom.label.toLowerCase()} stage. No link recorded.` : 'Required. No link recorded.';
    case 'stale':
      return row.staleOn ? `Stale since ${fmtDate(row.staleOn)}.` : row.lastCheckedOn ? 'Marked stale.' : 'Stale: no check or update date is recorded to count its freshness from.';
    case 'draft':
      return 'Recorded, not confirmed current yet.';
    case 'current': {
      if (!row.staleOn) return 'Current. This type does not turn stale by date.';
      const days = daysBetween(today, row.staleOn);
      return days >= 0 ? `Current until ${fmtDate(row.staleOn)}.` : `Past its date, ${fmtDate(row.staleOn)}.`;
    }
    default:
      return 'Status not known.';
  }
}

// A gate requirement row (C configuration gate-requirement) that needs a document type current.
export interface GateNeed {
  gateKey: Key;
  tierKey: Key | null;
  applicationKindKey: Key | null;
}

// The gates that need a document type current, from the gate-requirement rows in force.
export function gateNeedsOf(rows: { fields: Record<string, unknown>; isActive: boolean }[], documentTypeKey: Key): GateNeed[] {
  const text = (v: unknown) => (typeof v === 'string' && v ? v : null);
  return rows
    .filter(r => r.isActive && r.fields.requirementKind === 'document-current' && r.fields.requiredDocumentTypeKey === documentTypeKey && text(r.fields.gateKey))
    .map(r => ({ gateKey: text(r.fields.gateKey)!, tierKey: text(r.fields.tierKey), applicationKindKey: text(r.fields.applicationKindKey) }));
}

// "Go-live gate, at Tier 2 and Tier 3" from the needs of one gate, with labels.
export function gateNeedText(needs: GateNeed[], label: { gate: (k: string) => string; tier: (k: string) => string; kind: (k: string) => string }): string[] {
  const byGate = new Map<string, GateNeed[]>();
  for (const n of needs) byGate.set(n.gateKey, [...(byGate.get(n.gateKey) ?? []), n]);
  return [...byGate.entries()].map(([gate, list]) => {
    const scopes = list.some(n => !n.tierKey && !n.applicationKindKey)
      ? []
      : list.map(n => [n.tierKey ? label.tier(n.tierKey) : null, n.applicationKindKey ? `${label.kind(n.applicationKindKey).toLowerCase()} applications` : null].filter(Boolean).join(', '));
    const unique = [...new Set(scopes)];
    return unique.length ? `${label.gate(gate)} gate, at ${joinWords(unique)}` : `${label.gate(gate)} gate, for every application`;
  });
}

export function joinWords(words: string[]): string {
  if (words.length <= 1) return words[0] ?? '';
  return `${words.slice(0, -1).join(', ')} and ${words[words.length - 1]}`;
}

// The applications a person holds an owner role on now, from list_owner_assignments rows.
export function ownedApplications(rows: { businessApplicationId: Id }[]): Set<string> {
  return new Set(rows.map(r => r.businessApplicationId.toLowerCase()));
}
