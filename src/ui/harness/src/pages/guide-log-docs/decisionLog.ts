// The decision log's own logic, apart from the page so it can be tested: the filters the page applies to the rows it
// read, the counts its tiles and bars show, the exceptions register's review dates, and the supersede chains.
//
// Design choices (D-41, 2026-11-24):
// - The page reads the log once for the filters get_decision_log takes (type, application, portfolio, dates), every
//   page of it up to 1,000 decisions, and applies the status and the AI filters to those rows itself. So the tiles count
//   every status of the same filters, and a tile and the list it opens always agree. Where the store holds more than the
//   page read, the page says so (MoreRowsNote) and offers to read more.
// - "Drafted by an AI client" uses the same rule as the AI mark on every row (components/stamps.tsx), so the filter
//   and the mark never disagree.
// - An exception is standing while it is accepted and nothing supersedes it. Its review is overdue once the review
//   date has passed, and due soon within 30 days.
// - A proposal nobody has been asked about (proposed, no approval request) can be submitted from the log by the person
//   who proposed it, or accepted and submitted when an AI client drafted it for them.
import { recordRoute, type AppRoute, type DecisionStatusRouteMeaning } from '../../app/routes';
import type { DecisionLogItem, DecisionStatusMeaning } from '../../data/contracts';
import { daysBetween } from '../../data/format';
import { aiMarkText } from '../../components/stamps';

export const EXCEPTION_TYPE = 'exception';
export const REVIEW_SOON_DAYS = 30;
export const REVIEW_WINDOWS = ['overdue', 'soon'] as const;
export type ReviewWindow = (typeof REVIEW_WINDOWS)[number];

// The engine's words for each status meaning, for a status the configuration gives no label.
export const STATUS_WORDS: Record<DecisionStatusMeaning, string> = {
  proposed: 'Proposed',
  accepted: 'Accepted',
  rejected: 'Rejected',
  superseded: 'Superseded',
  withdrawn: 'Withdrawn',
};

export const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

export function isAiMade(item: Pick<DecisionLogItem, 'isAiCreated' | 'performedBy'>): boolean {
  return aiMarkText(item.isAiCreated, item.performedBy) !== null;
}

// Accepted and not superseded: the exception stands.
export function isStanding(item: DecisionLogItem): boolean {
  return item.status.meaning === 'accepted' && !item.supersededBy;
}

// When a standing exception is reviewed again, against today.
export function reviewState(item: DecisionLogItem, today: string): 'overdue' | 'soon' | 'later' | 'none' {
  const on = item.exception?.reviewOn;
  if (!on || !isStanding(item)) return 'none';
  const days = daysBetween(today, on);
  if (days < 0) return 'overdue';
  return days <= REVIEW_SOON_DAYS ? 'soon' : 'later';
}

export function reviewText(item: DecisionLogItem, today: string): string | null {
  const on = item.exception?.reviewOn;
  if (!on) return null;
  const days = daysBetween(today, on);
  if (!isStanding(item)) return null;
  if (days < 0) return `Review overdue by ${-days} ${-days === 1 ? 'day' : 'days'}`;
  if (days === 0) return 'Review due today';
  return `Review in ${days} ${days === 1 ? 'day' : 'days'}`;
}

export interface LogFilter {
  statusMeanings?: readonly DecisionStatusRouteMeaning[];
  aiOnly?: boolean;
  review?: ReviewWindow;
  today: string;
}

// The rows the page shows: the rows read, narrowed by the filters the page applies itself.
export function filterDecisions(items: DecisionLogItem[], filter: LogFilter): DecisionLogItem[] {
  return items.filter(d => {
    if (filter.statusMeanings?.length && !filter.statusMeanings.includes(d.status.meaning as DecisionStatusMeaning)) return false;
    if (filter.aiOnly && !isAiMade(d)) return false;
    if (filter.review) {
      const state = reviewState(d, filter.today);
      if (filter.review === 'overdue' ? state !== 'overdue' : state !== 'soon') return false;
    }
    return true;
  });
}

export function countByStatus(items: DecisionLogItem[]): Record<DecisionStatusMeaning, number> {
  const counts: Record<DecisionStatusMeaning, number> = { proposed: 0, accepted: 0, rejected: 0, superseded: 0, withdrawn: 0 };
  for (const d of items) {
    const m = d.status.meaning as DecisionStatusMeaning;
    if (m in counts) counts[m] += 1;
  }
  return counts;
}

export interface Bucket {
  key: string;
  label: string;
  count: number;
}

// Rows by decision type, most first, then by label.
export function countByType(items: DecisionLogItem[]): Bucket[] {
  const map = new Map<string, Bucket>();
  for (const d of items) {
    const b = map.get(d.decisionType.key) ?? { key: d.decisionType.key, label: d.decisionType.label, count: 0 };
    b.count += 1;
    map.set(d.decisionType.key, b);
  }
  return [...map.values()].sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
}

// "1 of 2 approvals in, 1 request open". Consultations are asked too but never count, so when more requests are open
// than approvals are missing, the text says so. Null when nobody was asked.
export function approvalsText(summary: DecisionLogItem['approvalsSummary']): string | null {
  const { required, approved, rejected, pending } = summary;
  if (!required && !pending && !approved && !rejected) return null;
  const parts = [`${approved} of ${required} ${required === 1 ? 'approval' : 'approvals'} in`];
  if (rejected) parts.push(`${rejected} rejected`);
  if (pending) {
    const consulted = pending > Math.max(0, required - approved - rejected);
    parts.push(`${pending} ${pending === 1 ? 'request' : 'requests'} open${consulted ? ', consultations included' : ''}`);
  }
  return parts.join(', ');
}

// A sentence as written, with one full stop at the end.
export function sentence(text: string): string {
  const t = text.trim();
  return /[.!?]$/.test(t) ? t : `${t}.`;
}

// Proposed and nobody asked yet: saved, never submitted.
export function notAskedYet(item: DecisionLogItem): boolean {
  const s = item.approvalsSummary;
  return item.status.meaning === 'proposed' && !s.required && !s.pending && !s.approved && !s.rejected;
}

// The person proposed it, or an AI client drafted it for them.
export function proposedBy(item: DecisionLogItem, me: string | null): boolean {
  if (!me) return false;
  if (sameId(item.proposedBy?.objectId, me)) return true;
  return isAiMade(item) && sameId(item.performedBy?.onBehalfOf?.objectId, me);
}

// The page of what a decision is about, when the web app has one.
export function subjectRoute(item: Pick<DecisionLogItem, 'subjectKind' | 'subjectId'>): AppRoute | null {
  const route = recordRoute(item.subjectKind, item.subjectId);
  return route.page === 'record' || route.page === 'not-found' ? null : route;
}

export const SUBJECT_WORDS: Record<DecisionLogItem['subjectKind'], string> = {
  'business-application': 'Application',
  'intake-request': 'Intake request',
  'change-request': 'Change request',
  'deployment-record': 'Flagged deployment',
  'technical-component': 'Component',
  claim: 'Gap check claim',
  review: 'Review',
  'configuration-draft': 'Configuration change',
  'data-use': 'Data use',
  'data-mapping': 'Data copy',
  'data-domain': 'Data domain',
  'data-location': 'Data location',
};

// ---------- supersede and amend chains ----------

export interface ChainLink {
  id: string;
  // Null when the decision is outside the rows read (another filter, or not readable).
  item: DecisionLogItem | null;
  // How this decision relates to the one before it in the chain.
  how: 'first' | 'supersedes' | 'amends';
}

const MAX_CHAIN = 20;

// Every chain of decisions that supersede or amend each other, oldest first, among the rows read. A decision outside
// the rows read shows as a link without its details. Chains are listed newest change first.
export function decisionChains(items: DecisionLogItem[]): ChainLink[][] {
  const byId = new Map(items.map(d => [d.decisionId.toLowerCase(), d]));
  const chains = new Map<string, ChainLink[]>();
  const starts = items.filter(d => d.supersedes || d.amends || d.supersededBy);
  for (const start of starts) {
    // Back to the first decision.
    const chain: ChainLink[] = [];
    const seen = new Set<string>();
    let cur: DecisionLogItem | null = start;
    let curId = start.decisionId.toLowerCase();
    while (cur && chain.length < MAX_CHAIN && !seen.has(curId)) {
      seen.add(curId);
      const prevId: string | null = cur.supersedes ?? cur.amends ?? null;
      chain.unshift({ id: curId, item: cur, how: prevId ? (cur.supersedes ? 'supersedes' : 'amends') : 'first' });
      if (!prevId) break;
      const prev: DecisionLogItem | null = byId.get(prevId.toLowerCase()) ?? null;
      if (!prev) {
        chain.unshift({ id: prevId.toLowerCase(), item: null, how: 'first' });
        break;
      }
      cur = prev;
      curId = prevId.toLowerCase();
    }
    // Forward to the newest decision.
    let last = chain[chain.length - 1];
    while (last.item?.supersededBy && chain.length < MAX_CHAIN) {
      const nextId = last.item.supersededBy.toLowerCase();
      if (chain.some(c => c.id === nextId)) break;
      const next = byId.get(nextId) ?? null;
      last = { id: nextId, item: next, how: 'supersedes' };
      chain.push(last);
    }
    if (chain.length < 2) continue;
    const key = chain[0].id;
    const known = chains.get(key);
    if (!known || known.length < chain.length) chains.set(key, chain);
  }
  const newest = (chain: ChainLink[]) => chain.reduce((at, c) => {
    const when = c.item?.decidedOn ?? c.item?.performedBy?.stampedOn ?? '';
    return when > at ? when : at;
  }, '');
  return [...chains.values()].sort((a, b) => newest(b).localeCompare(newest(a)));
}
