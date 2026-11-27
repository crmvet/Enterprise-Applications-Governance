// The demo and admin work of wave 4, the parts with no React in them (tested in logic.test.ts).
//   renewState(record, decisionMeaning, now)   whether a request may be asked again: expired, or fewer than 48 hours
//                                              left, while its decision is still proposed; else null
//   askedAgainBy(record, records, now)         the newer open request of the same ask, when one was asked already
//   mayAskAgain(session, proposedBy)           whether the page offers "Ask again" to the person at all
//   expiryWords(hoursLeft, state)              "Expired 6 hours ago", "Expires in 20 hours"
//   askedWords(request)                        who a request asks, in words
//   refreshCommand(origin), directorCommand(origin)   the exact demo kit and demo director commands for this install
//   storyPersonaName(name, personas)           the install's persona that plays a story persona (local runs name them
//                                              demo-<name> or <name>-web)
//   freshnessFilterOf(value)                   the Demo guide's ?fresh= filter
//
// The engine decides "Ask again": it refuses a request that is not renewable (approval.not-renewable), a decision that is
// final (decision.final) and anyone but the proposer or a platform admin (approval.renew-not-allowed). The page only
// offers the button where the engine would take it, and shows the engine's answer either way.
import type { ApprovalOutcomeMeaning, ApprovalRequestBrief, LabelledKey, PrincipalRef } from '../../data/contracts';
import { plural } from '../../data/format';

// The last hours of a request's time in which it may be asked again before it expires (B24-22).
export const RENEW_WINDOW_HOURS = 48;

export type RenewState = 'expired' | 'expires-soon';

const HOUR_MS = 60 * 60 * 1000;

// Whether a request may be asked again now. A pending request whose Expires On has come counts as expired, whether or
// not the daily refresh marked it.
export function renewState(
  record: { outcome: { meaning: ApprovalOutcomeMeaning | string }; expiresOn: string | null },
  decisionMeaning: string | null | undefined,
  now: number,
): RenewState | null {
  if (decisionMeaning !== 'proposed') return null;
  if (record.outcome.meaning === 'expired') return 'expired';
  if (record.outcome.meaning !== 'pending' || !record.expiresOn) return null;
  const expires = Date.parse(record.expiresOn);
  if (Number.isNaN(expires)) return null;
  if (expires <= now) return 'expired';
  return expires - now < RENEW_WINDOW_HOURS * HOUR_MS ? 'expires-soon' : null;
}

// One request as the decision page holds it: enough to tell whether two requests ask the same thing.
export interface AskShape {
  approvalRecordId: string;
  approvalRuleKey: string;
  isConsultation: boolean;
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  outcome: { meaning: ApprovalOutcomeMeaning | string };
  requestedOn: string | null;
  expiresOn: string | null;
}

// The open request that already asks the same thing again: the same rule, the same consultation flag, the same role
// and owner role, asked no earlier than this one, pending and not yet past its time (the engine's own test, B25-24).
// The daily refresh's restart and a person's "Ask again" both leave one. Null when nobody asked again yet.
export function askedAgainBy<T extends AskShape>(record: AskShape, records: readonly T[], now: number): T | null {
  const started = record.requestedOn ?? '';
  const open = records.filter(r => r.approvalRecordId !== record.approvalRecordId
    && r.outcome.meaning === 'pending'
    && (!r.expiresOn || Date.parse(r.expiresOn) > now)
    && r.approvalRuleKey === record.approvalRuleKey
    && r.isConsultation === record.isConsultation
    && (r.askedRole?.key ?? '') === (record.askedRole?.key ?? '')
    && (r.askedOwnerRole?.key ?? '') === (record.askedOwnerRole?.key ?? '')
    && (r.requestedOn ?? '') >= started);
  return open.sort((a, b) => (b.requestedOn ?? '').localeCompare(a.requestedOn ?? ''))[0] ?? null;
}

// Whether the page offers "Ask again" to this person: a person on the web app (D-36; an AI client is never served the
// tool), whose Tool Policy row allows it, who proposed the decision or holds the platform admin role. The engine checks
// the same again on the write.
export function mayAskAgain(
  session: { isPerson: boolean; objectId: string | null; can(tool: 'renew_approval_request'): boolean; hasRole(...keys: string[]): boolean },
  proposedBy: Pick<PrincipalRef, 'objectId'> | null | undefined,
): boolean {
  if (!session.isPerson || !session.can('renew_approval_request')) return false;
  if (session.hasRole('platform-admin')) return true;
  return !!proposedBy && !!session.objectId && proposedBy.objectId.toLowerCase() === session.objectId.toLowerCase();
}

// When a request expired, or how long it has left, from get_demo_freshness hoursLeft (whole hours, rounded down, so an
// expired request has a negative count).
export function expiryWords(hoursLeft: number, state: RenewState): string {
  if (state === 'expired') {
    const ago = Math.max(0, -hoursLeft);
    if (ago < 1) return 'Expired within the last hour';
    if (ago < 48) return `Expired ${plural(ago, 'hour', 'hours')} ago`;
    return `Expired ${plural(Math.floor(ago / 24), 'day', 'days')} ago`;
  }
  if (hoursLeft < 1) return 'Expires within the hour';
  return `Expires in ${plural(hoursLeft, 'hour', 'hours')}`;
}

// Who a request asks, in words: the person by name, else the role or owner role, lowercased inside a sentence where the
// label is a common noun ("the change manager"), with "AI lead" and "PMO" kept.
export function askedWords(request: Pick<ApprovalRequestBrief, 'askedPerson' | 'askedRole' | 'askedOwnerRole' | 'isConsultation'>): string {
  const lower = (label: string) => label.replace(/^([A-Z])([a-z])/, (_, a: string, b: string) => a.toLowerCase() + b);
  const who = request.askedPerson?.displayLabel
    ?? (request.askedRole ? `the ${lower(request.askedRole.label)}` : request.askedOwnerRole ? `the ${lower(request.askedOwnerRole.label)}` : 'the people the rules name');
  return request.isConsultation ? `${who}, for advice` : who;
}

// ---------- the Demo guide ----------

// The demo kit's command line (src/demo/kit/README.md): Node 22 and no packages, run from the root of the
// product's repository. The kit has no installed "demo" command, so the page names the file it runs.
export const DEMO_KIT_ENTRY = 'src/demo/kit/demo.mjs';

// The environment variable the demo kit and the demo director read the operator's web app token from (never printed).
export const DEMO_TOKEN_VARIABLE = 'PRODUCT_DEMO_TOKEN';

const LOOPBACK = /^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(:[0-9]{1,5})?$/i;

// True for a local run: the development sign-in on this machine, which needs no token.
export function isLoopback(origin: string): boolean {
  return LOOPBACK.test(origin.replace(/\/+$/, ''));
}

function baseOf(origin: string): string {
  return origin.replace(/\/+$/, '') || 'https://<this install>';
}

// The exact "demo refresh" for this install: a local run acts through the development sign-in, a demo install with the
// operator's own token, as the demo director does.
export function refreshCommand(origin: string): string {
  const base = baseOf(origin);
  const run = `node ${DEMO_KIT_ENTRY} refresh --base ${base}`;
  return isLoopback(base) ? run : `${DEMO_TOKEN_VARIABLE}=<the operator's web app token> ${run}`;
}

// The demo director, from src/adapters/azure: dev mode on a local run, demo mode with the operator's token elsewhere.
export function directorCommand(origin: string): string {
  const base = baseOf(origin);
  return isLoopback(base)
    ? `node seed/demo-director.mjs --base ${base}`
    : `${DEMO_TOKEN_VARIABLE}=<the operator's web app token> node seed/demo-director.mjs --mode demo --base ${base}`;
}

// The install's persona that plays a story persona. A demo install lists the story's own names (seed/demo-personas.json).
// A local run's development sign-in names the same people demo-<name>, or <name>-web for the four first personas
// (Product.Azure.Server/appsettings.Development.json), so those are tried in that order. Null when none is listed.
export function storyPersonaName(name: string, personas: readonly { name: string }[]): string | null {
  for (const candidate of [name, `demo-${name}`, `${name}-web`]) {
    if (personas.some(p => p.name === candidate)) return candidate;
  }
  return null;
}

export const FRESHNESS_FILTERS = ['expired', 'soon'] as const;
export type FreshnessFilter = (typeof FRESHNESS_FILTERS)[number];

export function freshnessFilterOf(value: string | undefined): FreshnessFilter | undefined {
  return (FRESHNESS_FILTERS as readonly string[]).includes(value ?? '') ? (value as FreshnessFilter) : undefined;
}

// A freshness item's state as the filter names it.
export function freshnessMatches(state: RenewState, filter: FreshnessFilter | undefined): boolean {
  if (!filter) return true;
  return filter === 'expired' ? state === 'expired' : state === 'expires-soon';
}
