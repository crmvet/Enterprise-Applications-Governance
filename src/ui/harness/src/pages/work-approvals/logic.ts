// The parts of My work, Approvals and the decision page with no React in them: how the queue splits into sections, how
// an approval request and its answer read in plain words, who approves what, and the history of one decision.
// Every fact comes from a tool's answer. Nothing here counts what the engine decides: whether an answer counts, whether
// the rules are met, and who may answer are the engine's, and these functions only put its answer into words.
import type {
  ActorKind,
  ApprovalItem,
  ApprovalMode,
  DataDomainRef,
  DataSubjectKind,
  DecisionApprovalRecord,
  DecisionDetail,
  IsoDate,
  IsoDateTime,
  LabelledKey,
  PrincipalRef,
  QueueItem,
  StampedBy,
} from '../../data/contracts';
import { fmtDateTime, forSomeoneElse } from '../../data/format';

// ---------- time in words ----------

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

const toMs = (value: IsoDate | IsoDateTime) => Date.parse(value.length === 10 ? `${value}T00:00:00Z` : value);

// "just now", "5 minutes ago", "3 hours ago", "2 days ago". Null for a missing or unreadable time.
export function ageText(value: IsoDateTime | null | undefined, nowMs: number): string | null {
  if (!value) return null;
  const at = toMs(value);
  if (Number.isNaN(at)) return null;
  const diff = Math.max(0, nowMs - at);
  if (diff < MINUTE) return 'just now';
  if (diff < HOUR) return unit(Math.floor(diff / MINUTE), 'minute');
  if (diff < DAY) return unit(Math.floor(diff / HOUR), 'hour');
  return unit(Math.floor(diff / DAY), 'day');
}

function unit(n: number, word: string): string {
  return `${n} ${word}${n === 1 ? '' : 's'} ago`;
}

export type ExpiryState = 'expired' | 'soon' | 'later';

// When an approval request runs out, in words, with a state the page shows with an icon and a word (never color alone).
// soon: within two days.
export function expiryText(expiresOn: IsoDateTime | null | undefined, nowMs: number): { text: string; state: ExpiryState } | null {
  if (!expiresOn) return null;
  const at = toMs(expiresOn);
  if (Number.isNaN(at)) return null;
  const diff = at - nowMs;
  if (diff <= 0) return { text: 'Expired', state: 'expired' };
  if (diff < HOUR) return { text: 'Expires within the hour', state: 'soon' };
  if (diff < DAY) {
    const hours = Math.floor(diff / HOUR);
    return { text: `Expires in ${hours} hour${hours === 1 ? '' : 's'}`, state: 'soon' };
  }
  const days = Math.floor(diff / DAY);
  return { text: `Expires in ${days} day${days === 1 ? '' : 's'}`, state: days <= 2 ? 'soon' : 'later' };
}

// ---------- the queue ----------

export type QueueSection = 'ai-drafts' | 'drafts' | 'reviews' | 'documents' | 'owner-gaps' | 'claims';

export const QUEUE_SECTIONS: { key: QueueSection; title: string; empty: string }[] = [
  { key: 'ai-drafts', title: 'AI drafts waiting for you', empty: 'No AI client has drafted anything for you.' },
  { key: 'drafts', title: 'Your drafts', empty: 'You have no unsubmitted drafts.' },
  { key: 'reviews', title: 'Reviews due', empty: 'No review is due for you.' },
  { key: 'documents', title: 'Documents to add or check', empty: 'No document is missing, stale or waiting for your check.' },
  { key: 'owner-gaps', title: 'Owner gaps on your applications', empty: 'Every owner role on your applications has a holder and a deputy.' },
  { key: 'claims', title: 'Claims suggested to you', empty: 'The gap check suggests nothing to you.' },
];

// True when an AI client made the row: the engine's flag, or its stamp.
export function isAiMade(isAiCreated: boolean, performedBy: StampedBy | null | undefined): boolean {
  return isAiCreated || performedBy?.actorKind === 'ai-client-for-person' || performedBy?.actorKind === 'agent-identity';
}

// The section of My work a queue item goes in. Approvals are not here: My work reads them from list_approvals, which
// says through which role the person is asked.
export function sectionOf(item: QueueItem): QueueSection | null {
  switch (item.kind) {
    case 'draft':
      return isAiMade(item.isAiCreated, item.performedBy) ? 'ai-drafts' : 'drafts';
    case 'review':
      return 'reviews';
    case 'document':
      return 'documents';
    case 'owner-gap':
      return 'owner-gaps';
    case 'claim':
      return 'claims';
    default:
      return null;
  }
}

export function groupQueue(items: QueueItem[]): Record<QueueSection, QueueItem[]> {
  const groups: Record<QueueSection, QueueItem[]> = { 'ai-drafts': [], drafts: [], reviews: [], documents: [], 'owner-gaps': [], claims: [] };
  for (const item of items) {
    const section = sectionOf(item);
    if (section) groups[section].push(item);
  }
  return groups;
}

// The records submit_record takes from My work. A draft application goes through its own page.
export const SUBMITTABLE_FROM_QUEUE = ['intake-request', 'change-request', 'decision'] as const;
export type QueueSubmittable = (typeof SUBMITTABLE_FROM_QUEUE)[number];

export function submittableKind(item: QueueItem): QueueSubmittable | null {
  if (item.kind !== 'draft' || item.nextTool !== 'submit_record') return null;
  return (SUBMITTABLE_FROM_QUEUE as readonly string[]).includes(item.subjectKind) ? (item.subjectKind as QueueSubmittable) : null;
}

// ---------- approval requests in words ----------

export interface AskedOf {
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  askedPrincipal: PrincipalRef | null;
  // The data facts of a data decision (list_approvals and get_decision data), so an owner role on a domain or a location
  // names it instead of the application. Left out for other decisions.
  data?: DataSubjectOf | null;
}

// What a data decision is about, as far as an owner role's subject goes.
export interface DataSubjectOf {
  subjectKind: DataSubjectKind;
  domain: DataDomainRef | null;
  location?: { label: string } | null;
}

// The data owner and data steward roles sit on a domain (docs/architecture/data-taxonomy.md, Owners at any level); the technical owner of a data
// location on the location. Every other owner role a data decision asks sits on the application.
const DOMAIN_OWNER_ROLES = ['data-owner', 'data-steward'];

// Where an owner role is asked: "of the application", "of People / Payroll", "of the location Payroll database".
export function ownerSubjectText(ownerRoleKey: string, data?: DataSubjectOf | null): string {
  if (data?.domain && DOMAIN_OWNER_ROLES.includes(ownerRoleKey)) return `of ${data.domain.path || data.domain.label}`;
  if (data?.subjectKind === 'data-location' && ownerRoleKey === 'technical-owner') return data.location ? `of the location ${data.location.label}` : 'of the location';
  return 'of the application';
}

// Who a request asks: "Change manager", "Business owner of the application", "Data owner of People / Payroll", or a
// named person.
export function askedOfText(a: AskedOf): string {
  if (a.askedPrincipal) return a.askedPrincipal.displayLabel || 'A named person';
  if (a.askedRole) return a.askedRole.label;
  if (a.askedOwnerRole) return `${a.askedOwnerRole.label} ${ownerSubjectText(a.askedOwnerRole.key, a.data)}`;
  return 'The accountable holder';
}

export const MODE_WORDS: Record<ApprovalMode, string> = {
  'everyone-must-approve': 'everyone asked must approve',
  'first-to-respond': 'the first answer counts',
  sequential: 'one step after another',
};

export type CountState = 'waiting' | 'counted' | 'advice' | 'recommendation' | 'not-counted' | 'closed';

export interface CountExplained {
  state: CountState;
  text: string;
}

// Why an answer counted toward the approval or not, from the engine's stored flags. separationLabel names the
// separation rule that kept the answer from counting, when the record names one.
export function countExplained(
  r: {
    outcome: { meaning: string; label: string };
    isConsultation: boolean;
    countsTowardRequirement: boolean;
    refusedBySeparationRuleKey?: string | null;
    approverActorKind?: ActorKind | string | null;
  },
  separationLabel?: (key: string) => string,
  cancelReason?: string | null,
): CountExplained {
  const meaning = r.outcome.meaning;
  if (meaning === 'pending') return { state: 'waiting', text: 'Waiting for an answer.' };
  if (meaning === 'expired') return { state: 'closed', text: 'Expired before anyone answered.' };
  if (meaning === 'cancelled') return { state: 'closed', text: cancelReason ? `Canceled: ${cancelReason}.` : 'Canceled before anyone answered.' };
  if (meaning === 'recommendation-only' || r.approverActorKind === 'ai-client-for-person' || r.approverActorKind === 'agent-identity') {
    return { state: 'recommendation', text: 'Stored as a recommendation only. An answer from an AI never counts.' };
  }
  if (r.isConsultation) return { state: 'advice', text: 'A consultation: advice only. It never counts toward the approval.' };
  if (r.refusedBySeparationRuleKey) {
    const rule = separationLabel ? separationLabel(r.refusedBySeparationRuleKey) : r.refusedBySeparationRuleKey;
    return { state: 'not-counted', text: `Did not count: the separation rule "${rule}" applies to this answer.` };
  }
  if (r.countsTowardRequirement) return { state: 'counted', text: 'Counted toward the approval.' };
  return { state: 'not-counted', text: 'Did not count toward the approval. The decision page shows why.' };
}

// The line under an approval request in a list: who it asks, when it was asked, when it runs out, who else is asked.
export function requestFacts(item: ApprovalItem, nowMs: number): string[] {
  const facts: string[] = [];
  facts.push(`Asks ${askedOfText(item)}${item.isConsultation ? ' (consulted)' : ''}`);
  const asked = ageText(item.requestedOn, nowMs);
  if (asked && item.outcome.meaning === 'pending') facts.push(`waiting ${asked.replace(' ago', '')}`);
  if (item.outcome.meaning === 'pending') {
    const expiry = expiryText(item.expiresOn, nowMs);
    if (expiry) facts.push(expiry.text.toLowerCase());
    if (item.otherPendingCount > 0) facts.push(`${item.otherPendingCount} other request${item.otherPendingCount === 1 ? '' : 's'} pending on this decision`);
  }
  return facts;
}

// ---------- answerable now, or asked and kept from answering (wave 4b verify round) ----------

// The rows of view mine in two groups: the requests the person may answer now (nextTool answer_approval), and the ones
// that ask them but that they may not answer, such as their own proposal under a separation rule (cannotAnswer). The
// second group shows apart, because another person answers it. Order within each group is the server's.
export function splitMine(items: readonly ApprovalItem[]): { answerable: ApprovalItem[]; others: ApprovalItem[] } {
  const answerable: ApprovalItem[] = [];
  const others: ApprovalItem[] = [];
  for (const item of items) (item.nextTool === 'answer_approval' ? answerable : others).push(item);
  return { answerable, others };
}

// The requests the person may not answer, in two kinds (wave 4b review): the ones a separation rule keeps them from
// (cannotAnswer.separationRule), which another person answers, and the ones that cannot be answered now for another
// reason, such as a request whose time ran out before the daily check closed it. Each keeps the server's order.
export function splitKept(items: readonly ApprovalItem[]): { separated: ApprovalItem[]; notNow: ApprovalItem[] } {
  const separated: ApprovalItem[] = [];
  const notNow: ApprovalItem[] = [];
  for (const item of items) (item.cannotAnswer?.separationRule ? separated : notNow).push(item);
  return { separated, notNow };
}

// How many requests the person may answer now: list_approvals answerableCount (view mine). A server before wave 4b
// sends no answerableCount, and then the total stands in, as before. Null while the list is not read.
export function answerableTotal(list: { totalCount: number; answerableCount?: number | null } | null | undefined): number | null {
  if (!list) return null;
  return typeof list.answerableCount === 'number' ? list.answerableCount : list.totalCount;
}

// How many requests ask the person but are answered by another person (the total less the answerable ones).
export function keptFromAnswering(list: { totalCount: number; answerableCount?: number | null } | null | undefined): number {
  if (!list || typeof list.answerableCount !== 'number') return 0;
  return Math.max(0, list.totalCount - list.answerableCount);
}

// Matches a client-side search against what a row shows.
export function matchesText(item: ApprovalItem, text: string): boolean {
  const t = text.trim().toLowerCase();
  if (!t) return true;
  return [item.decision.statement, item.decision.decisionType.label, item.subject.label, item.subject.applicationName ?? '', askedOfText(item), item.data?.domain?.path ?? '']
    .some(s => s.toLowerCase().includes(t));
}

// ---------- who approves what ----------

export interface RuleLabels {
  role(key: string): string;
  ownerRole(key: string): string;
  tier(key: string): string;
  changeType(key: string): string;
  applicationKind(key: string): string;
  lifecycleStage(key: string): string;
  targetStage(key: string): string;
}

const str = (v: unknown): string | null => (typeof v === 'string' && v ? v : null);
const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);

// The scope words of a configuration row: "at tier 3, for an emergency change".
export function scopeText(fields: Record<string, unknown>, labels: RuleLabels): string | null {
  const parts: string[] = [];
  const tier = str(fields.tierKey);
  const change = str(fields.changeTypeKey);
  const kind = str(fields.applicationKindKey);
  const stage = str(fields.lifecycleStageKey);
  const target = str(fields.targetStageKey);
  if (tier) parts.push(`at ${labels.tier(tier)}`);
  if (change) parts.push(`for a change of type ${labels.changeType(change)}`);
  if (kind) parts.push(`for applications of kind ${labels.applicationKind(kind)}`);
  if (stage) parts.push(`in the stage ${labels.lifecycleStage(stage)}`);
  if (target) parts.push(`deploying to ${labels.targetStage(target)}`);
  if (fields.portfolioId) parts.push('in one portfolio');
  return parts.length ? parts.join(', ') : null;
}

// One approval rule row in plain words.
export function approvalRuleText(fields: Record<string, unknown>, labels: RuleLabels): { who: string; how: string; scope: string | null; consultation: boolean } {
  const role = str(fields.approverRoleKey);
  const ownerRole = str(fields.approverOwnerRoleKey);
  // The owner role is held on the data domain, its parent or the location when the rule says so (C32 Owner Subject).
  const subject = str(fields.ownerSubject);
  const of = subject === 'data-domain' ? 'of the data domain'
    : subject === 'parent-data-domain' ? 'of the parent data domain'
      : subject === 'data-location' ? 'of the data location'
        : subject === 'touched-data-domains' ? 'of each data domain the change touches'
          : 'of the application';
  const who = role ? labels.role(role) : ownerRole ? `${labels.ownerRole(ownerRole)} ${of}` : 'Not set';
  const consultation = fields.isConsultation === true;
  const mode = str(fields.approvalMode) as ApprovalMode | null;
  const bits: string[] = [];
  if (consultation) bits.push('consulted: advice only, never counts');
  else if (mode && MODE_WORDS[mode]) bits.push(MODE_WORDS[mode]);
  const min = num(fields.minimumCount);
  if (!consultation && min !== null && min > 1) bits.push(`${min} approvals needed`);
  const sequence = num(fields.sequence);
  if (sequence !== null) bits.push(`step ${sequence}`);
  const days = num(fields.timeoutDays);
  if (days !== null) bits.push(`expires after ${days} day${days === 1 ? '' : 's'}`);
  if (fields.requiresTypedReason === true) bits.push('a typed reason is needed to approve');
  return { who, how: bits.join(', '), scope: scopeText(fields, labels), consultation };
}

// One separation rule row in plain words.
export function separationRuleText(fields: Record<string, unknown>, labels: RuleLabels & { decisionType(key: string): string }): string {
  const differ = str(fields.mustDifferFrom);
  const who = differ === 'proposer' ? 'the person who proposed the decision'
    : differ === 'deployer' ? 'the person who deployed'
      : differ ? differ.replace(/-/g, ' ') : 'another party';
  const type = str(fields.decisionTypeKey);
  const scope = [type ? `on ${labels.decisionType(type).toLowerCase()} decisions` : null, scopeText(fields, labels)].filter(Boolean).join(', ');
  const enforcement = str(fields.enforcement);
  const effect = enforcement === 'refuse' ? 'The engine refuses an answer that breaks it.'
    : enforcement === 'flag' ? 'The engine records the answer and flags it.'
      : enforcement ? `Enforcement: ${enforcement}.` : '';
  return `The approver must differ from ${who}${scope ? `, ${scope}` : ''}. ${effect}`.trim();
}

// ---------- one decision ----------

export interface HistoryEvent {
  at: IsoDateTime;
  title: string;
  detail: string | null;
}

type CancelFacts = Pick<DecisionApprovalRecord, 'approvalRecordId' | 'askedOwnerRole' | 'askedPrincipal' | 'requestedOn' | 'isConsultation'>;

// Why the engine canceled a request, from the decision's status and the requests asked after it (wave 3 verification:
// after a move re-asked the new data owner, the page said "the decision moved on or was withdrawn"). A settled or
// withdrawn decision needs no more answers. A request on a decision still proposed was canceled because the decision was
// asked again from the start: when its data owner changed, a later request asks the new one.
export function cancelReasonText(r: CancelFacts, decisionMeaning: string | null | undefined, records: readonly CancelFacts[]): string {
  if (decisionMeaning === 'withdrawn') return 'the decision was withdrawn';
  if (decisionMeaning === 'accepted' || decisionMeaning === 'rejected') return `the decision was ${decisionMeaning}, so this request was no longer needed`;
  const later = records.filter(o => o.approvalRecordId !== r.approvalRecordId && o.isConsultation === r.isConsultation
    && !!o.requestedOn && !!r.requestedOn && o.requestedOn > r.requestedOn);
  const next = later.find(o => o.askedOwnerRole?.key === r.askedOwnerRole?.key) ?? later[0];
  if (!next) return 'the decision moved on before anyone answered';
  const who = next.askedPrincipal?.displayLabel || null;
  if (r.askedOwnerRole?.key === 'data-owner' && next.askedPrincipal?.objectId !== r.askedPrincipal?.objectId) {
    return `the data owner changed, so the decision now asks ${who ?? 'the new data owner'}`;
  }
  return `the decision was asked again from the start${who ? `, of ${who}` : ''}`;
}

// What happened to a request that is no longer pending. A canceled or expired request has no answer, so it never reads
// "Someone answered" (wave 3 verification). cancelReason, when given, says why the engine canceled it.
export function recordOutcomeText(r: Pick<DecisionApprovalRecord, 'outcome' | 'approver' | 'respondedOn' | 'requestedOn' | 'expiresOn'>, cancelReason?: string | null): string {
  const asked = r.requestedOn ? `Asked ${fmtDateTime(r.requestedOn)}` : 'Asked';
  switch (r.outcome.meaning) {
    case 'cancelled': return `${asked}, then canceled by the engine before anyone answered${cancelReason ? `: ${cancelReason}` : ''}.`;
    case 'expired': return `${asked}, and it expired without an answer${r.expiresOn ? ` on ${fmtDateTime(r.expiresOn)}` : ''}.`;
    default: return r.approver
      ? `${r.approver.displayLabel} answered${r.respondedOn ? ` ${fmtDateTime(r.respondedOn)}` : ''}.`
      : `Answered${r.respondedOn ? ` ${fmtDateTime(r.respondedOn)}` : ''}.`;
  }
}

// What happened to a decision, oldest first, from the times the reads carry: each request asked, each answer, the
// settlement, and the last engine-stamped change. A time the reads do not carry is left out, never guessed.
export function decisionHistory(detail: DecisionDetail): HistoryEvent[] {
  const events: HistoryEvent[] = [];
  const records = detail.approvals.records;
  for (const r of records) {
    const asked = askedOfText({ ...r, data: detail.data ?? null });
    if (r.requestedOn) {
      events.push({ at: r.requestedOn, title: `${r.isConsultation ? 'Consulted' : 'Asked'}: ${asked}`, detail: r.expiresOn ? `The request expires ${fmtDateTime(r.expiresOn)}.` : null });
    }
    if (r.respondedOn && r.outcome.meaning !== 'pending') {
      const who = r.approver?.displayLabel || 'Someone';
      events.push({ at: r.respondedOn, title: `${who}: ${r.outcome.label}`, detail: r.typedReason });
    }
  }
  const d = detail.decision;
  if (d.decidedOn) {
    events.push({ at: d.decidedOn, title: `Decision ${d.status.label.toLowerCase()}`, detail: d.decider ? `Decided by ${d.decider.displayLabel}.` : null });
  }
  const stamp = d.performedBy;
  if (stamp?.stampedOn && !events.some(e => e.at === stamp.stampedOn)) {
    const actor = stamp.principal.displayLabel || 'Someone';
    const other = forSomeoneElse(stamp.principal, stamp.onBehalfOf);
    const forWhom = other ? ` for ${other.displayLabel || other.objectId}` : '';
    events.push({ at: stamp.stampedOn, title: `Last change by ${actor}${forWhom}`, detail: stamp.clientAppLabel ? `Through ${stamp.clientAppLabel}.` : null });
  }
  return events.sort((a, b) => toMs(a.at) - toMs(b.at));
}

// The options a proposal weighed, as the engine stores them: a JSON list of { option, whyNot }. Null when the text is
// not that list (an older free-text note), so the page shows it as written.
export function optionsWeighed(text: string | null | undefined): { option: string; whyNot: string }[] | null {
  if (!text) return null;
  const trimmed = text.trim();
  if (!trimmed.startsWith('[')) return null;
  try {
    const parsed: unknown = JSON.parse(trimmed);
    if (!Array.isArray(parsed)) return null;
    const list = parsed.flatMap(o => (o && typeof o === 'object' && typeof (o as { option?: unknown }).option === 'string'
      ? [{ option: (o as { option: string }).option, whyNot: typeof (o as { whyNot?: unknown }).whyNot === 'string' ? (o as { whyNot: string }).whyNot : '' }]
      : []));
    return list.length === parsed.length ? list : null;
  } catch {
    return null;
  }
}

// The words for the rule that asked an approval. An approval rule by its label; when no approval rule applies, the
// accountable holder of the decision right answers, and the record names that decision right instead.
export function askedUnderText(key: string, rules: { has(key: string): boolean; label(key: string): string }, rights: { has(key: string): boolean; label(key: string): string }): string {
  if (rules.has(key)) return `Rule: ${rules.label(key)}`;
  if (rights.has(key)) return `Decision right: ${rights.label(key)}`;
  return `Rule: ${rules.label(key)}`;
}

// True when the signed-in person proposed the decision, or an AI client proposed it for them.
export function proposedByMe(detail: DecisionDetail, myObjectId: string | null | undefined): boolean {
  if (!myObjectId) return false;
  const me = myObjectId.toLowerCase();
  const d = detail.decision;
  return d.proposedBy?.objectId.toLowerCase() === me || (isAiMade(d.isAiCreated, d.performedBy) && d.performedBy?.onBehalfOf?.objectId.toLowerCase() === me);
}

// A proposed decision nobody has been asked about yet: it was saved and never submitted.
export function notSubmitted(detail: DecisionDetail): boolean {
  return detail.decision.status.meaning === 'proposed' && detail.approvals.records.length === 0;
}

// The requests of a decision split for the page: answers and pending requests that count, and consultations.
export function splitRecords(records: DecisionApprovalRecord[]): { approvals: DecisionApprovalRecord[]; consultations: DecisionApprovalRecord[] } {
  return {
    approvals: records.filter(r => !r.isConsultation),
    consultations: records.filter(r => r.isConsultation),
  };
}
