// The logic of the four Govern pages the admin group builds (Roles and access, Guidelines and playbook, Measures, Admin):
// the words for the keys the tools return, the filters the pages keep in the address, the sorting, and the findings of
// the binding check read from its structured rows. Pure functions only, so logic.test.ts covers them without a browser.
import type {
  AdminStatus,
  DueStateRefreshResult,
  GuidelineList,
  IsoDate,
  Measures,
  PrincipalRef,
  RoleAccess,
  RoleBindingCheck,
  RoleToolLine,
  SlideBackSignalKey,
} from '../../data/contracts';
import { humanizeKey, plural } from '../../data/format';
import type { Tone } from '../../data/meanings';

// ---------- shared ----------

// A key in words, with the acronyms the product uses kept in capitals.
export function plainWords(key: string): string {
  return humanizeKey(key).replace(/\b(ai|mcp|api|id|url|sql|pmo|coe|dc)\b/gi, m => m.toUpperCase());
}

// A role label inside a sentence: "Change manager" reads "change manager", while "AI lead" and "PMO" keep their capitals.
export function lowerFirst(label: string): string {
  return label.replace(/^([A-Z])([a-z])/, (_, a: string, b: string) => a.toLowerCase() + b);
}

// Engine text for a person: a tool's API name in brackets, such as "(check_role_bindings)", is dropped, because the
// page names the place to go in words.
export function withoutToolNames(text: string): string {
  return text.replace(/\s*\((?:[a-z]+_)+[a-z]+\)/g, '');
}

// A tool's name in words: "save_data_use" reads "Save data use", "get_ai_activity" reads "Get AI activity".
export function toolTitle(tool: string): string {
  if (tool === 'whoami') return 'Who am I';
  return plainWords(tool.replace(/_/g, '-'));
}

const TOOLSET_TEXT: Record<string, string> = {
  session: 'Session',
  portfolio: 'Portfolio',
  intake: 'Intake',
  decisions: 'Decisions',
  change: 'Change',
  deployment: 'Deployment',
  documentation: 'Documentation',
  gapcheck: 'Gap check',
  configuration: 'Configuration',
  links: 'Links',
  data: 'Data',
};

export function toolsetText(key: string): string {
  return TOOLSET_TEXT[key] ?? plainWords(key);
}

// ---------- roles and access ----------

export type RoleItem = RoleAccess['roles'][number];

export const BINDING_METHOD_TEXT: Record<string, string> = {
  'entra-app-role-assignment': 'Entra app role assignment',
  'dataverse-group-team': 'Dataverse group team',
};

const LETTER_TEXT: Record<string, string> = {
  responsible: 'Responsible: proposes',
  accountable: 'Accountable: decides',
  consulted: 'Consulted: gives advice',
  informed: 'Informed: is told',
  r: 'Responsible: proposes',
  a: 'Accountable: decides',
  c: 'Consulted: gives advice',
  i: 'Informed: is told',
};

export function letterText(letter: string): string {
  return LETTER_TEXT[letter.toLowerCase()] ?? plainWords(letter);
}

// How far an AI client may take a record through a tool (tools.json aiWriteStates), in words.
const AI_STATE_TEXT: Record<string, string> = {
  'read-only': 'reads only',
  draft: 'a draft, which a person takes forward',
  proposed: 'a proposed decision, which a person submits',
  submitted: 'a submitted request, never an approval',
  'as-reported': 'a fact it reports, never a decision',
  link: 'a link to something another tool owns',
  candidate: 'a configuration draft, never in force',
  withdrawn: 'withdrawing a record it created',
  'person-only': 'nothing: a person only',
};

export function aiStateText(state: string): string {
  return AI_STATE_TEXT[state] ?? plainWords(state).toLowerCase();
}

// Who besides a role may call a tool (tools.json relations), in words.
const RELATION_TEXT: Record<string, string> = {
  'owner-of-application': 'an owner of the application',
  'technical-owner-of-application': 'the technical owner of the application',
  'owner-of-subject-application': 'an owner of the record\'s application',
  'intake-requester': 'the requester of the intake',
  'leaving-person': 'the person who leaves',
  'owner-of-leaving-persons-application': 'an owner of an application the leaver owns',
  'record-requester-or-creator': 'the requester or creator of the record',
  'decision-right-holder': 'a holder of a decision right for the decision',
  'asked-approver': 'the person an approval request asks',
  'document-owner': 'the owner of the document',
  'writer-of-evidence-subject': 'a person who may write the record the evidence supports',
  'reviewer-of-review': 'the person whose review it is',
  'owner-of-data-domain': 'an owner or steward of the data domain',
  'technical-owner-of-location': 'the technical owner of the data location',
};

export function relationText(key: string): string {
  return RELATION_TEXT[key] ?? plainWords(key).toLowerCase();
}

// The subject kinds an AI client may submit, in words: "intake requests and data uses".
export function submitKindsText(kinds: string[]): string {
  const words = kinds.map(k => `${plainWords(k).toLowerCase()}s`.replace(/ys$/, 'ies'));
  if (words.length <= 1) return words[0] ?? '';
  return `${words.slice(0, -1).join(', ')} and ${words[words.length - 1]}`;
}

// One sentence on what an AI client may do with a tool.
export function aiLine(line: { access: 'read' | 'write'; personOnly: boolean; maxAiState: string; aiMaySubmit: string[] }): string {
  if (line.personOnly) return 'A person only, on the web app. AI clients never see it.';
  if (line.access === 'read') return 'AI clients may read it for the person.';
  const base = `An AI client goes as far as ${aiStateText(line.maxAiState)}.`;
  return line.aiMaySubmit.length ? `${base} It may submit ${submitKindsText(line.aiMaySubmit)}.` : base;
}

export interface ToolGroup<T extends { toolset: string; number: number }> {
  toolset: string;
  label: string;
  tools: T[];
}

// Tools grouped by toolset, in the order the toolsets first appear by tool number, each group by number.
export function groupByToolset<T extends { toolset: string; number: number }>(lines: readonly T[]): ToolGroup<T>[] {
  const sorted = [...lines].sort((a, b) => a.number - b.number);
  const groups = new Map<string, T[]>();
  for (const line of sorted) {
    const list = groups.get(line.toolset) ?? [];
    list.push(line);
    groups.set(line.toolset, list);
  }
  return [...groups.entries()].map(([toolset, tools]) => ({ toolset, label: toolsetText(toolset), tools }));
}

export const TOOL_FILTERS = ['all', 'write', 'read', 'ai-writes', 'person-only'] as const;
export type ToolFilter = (typeof TOOL_FILTERS)[number];

export const TOOL_FILTER_TEXT: Record<ToolFilter, string> = {
  all: 'Every tool',
  write: 'Writes',
  read: 'Reads',
  'ai-writes': 'AI clients may write',
  'person-only': 'A person only',
};

export function toolMatches(line: Pick<RoleToolLine, 'access' | 'personOnly' | 'maxAiState'>, filter: ToolFilter): boolean {
  switch (filter) {
    case 'write':
      return line.access === 'write';
    case 'read':
      return line.access === 'read';
    case 'ai-writes':
      return line.access === 'write' && !line.personOnly && line.maxAiState !== 'read-only';
    case 'person-only':
      return line.personOnly;
    default:
      return true;
  }
}

// A role a person can hold only through a group, with no group bound to it.
export function isUnbound(role: RoleItem): boolean {
  return !role.isWorkloadRole && !role.bindings.some(b => b.group !== null);
}

export const ROLE_SHOWS = ['all', 'mine', 'unbound', 'workload', 'changes-configuration'] as const;
export type RoleShow = (typeof ROLE_SHOWS)[number];

export const ROLE_SHOW_TEXT: Record<RoleShow, string> = {
  all: 'Every role',
  mine: 'Roles I hold',
  unbound: 'No group bound',
  workload: 'Workload roles',
  'changes-configuration': 'Change the configuration',
};

export function roleMatches(role: RoleItem, show: RoleShow): boolean {
  switch (show) {
    case 'mine':
      return role.heldByMe;
    case 'unbound':
      return isUnbound(role);
    case 'workload':
      return role.isWorkloadRole;
    case 'changes-configuration':
      return role.canChangeConfiguration;
    default:
      return true;
  }
}

export interface RoleCounts {
  people: number;
  bound: number;
  unbound: number;
  workload: number;
  mine: number;
  grants: number;
  expiredGrants: number;
}

export function roleCounts(roles: RoleItem[]): RoleCounts {
  const people = roles.filter(r => !r.isWorkloadRole);
  const grants = roles.flatMap(r => r.workloadGrants);
  return {
    people: people.length,
    bound: people.filter(r => !isUnbound(r)).length,
    unbound: people.filter(isUnbound).length,
    workload: roles.length - people.length,
    mine: roles.filter(r => r.heldByMe).length,
    grants: grants.length,
    expiredGrants: grants.filter(g => g.isExpired).length,
  };
}

export interface GrantRow {
  roleKey: string;
  roleLabel: string;
  principal: PrincipalRef | null;
  reason: string | null;
  expiresOn: IsoDate | null;
  isExpired: boolean;
}

// Every workload grant, soonest expiry first: the expired ones (or with no expiry, which the engine treats as expired)
// lead, then by the day they end.
export function grantsByExpiry(roles: RoleItem[]): GrantRow[] {
  const rows = roles.flatMap(r => r.workloadGrants.map(g => ({
    roleKey: r.role.key,
    roleLabel: r.role.label,
    principal: g.principal,
    reason: g.reason,
    expiresOn: g.expiresOn,
    isExpired: g.isExpired,
  })));
  return rows.sort((a, b) => (a.isExpired === b.isExpired ? 0 : a.isExpired ? -1 : 1) || (a.expiresOn ?? '').localeCompare(b.expiresOn ?? '') || a.roleLabel.localeCompare(b.roleLabel));
}

// Days from today to a grant's end; negative once it ended.
export function daysUntil(day: IsoDate, today: IsoDate): number {
  return Math.round((Date.parse(`${day}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000);
}

export function grantExpiryText(grant: Pick<GrantRow, 'expiresOn' | 'isExpired'>, today: IsoDate): { text: string; tone: Tone } {
  if (!grant.expiresOn) return { text: 'No end date: treated as expired', tone: 'danger' };
  const days = daysUntil(grant.expiresOn, today);
  if (grant.isExpired || days < 0) return { text: days === 0 ? 'Ended today' : `Ended ${plural(-days, 'day', 'days')} ago`, tone: 'danger' };
  if (days === 0) return { text: 'Ends today', tone: 'warning' };
  return { text: `Ends in ${plural(days, 'day', 'days')}`, tone: days <= 30 ? 'warning' : 'success' };
}

export interface Finding {
  tone: 'danger' | 'warning';
  text: string;
}

// The findings of the binding check, read from its rows rather than its finding sentences, which name role keys and
// object IDs. roleLabel turns a role key into its configured label.
export function bindingFindings(check: RoleBindingCheck, roleLabel: (key: string) => string): Finding[] {
  const found: Finding[] = [];
  for (const b of check.bindings) {
    if (!b.group) found.push({ tone: 'danger', text: `${roleLabel(b.roleKey)} names no group, so nobody holds it.` });
    else if (!b.groupExists) found.push({ tone: 'danger', text: `The group bound to ${roleLabel(b.roleKey)} (${b.group.displayLabel}) does not exist in the directory.` });
  }
  for (const g of check.workloadGrants) {
    const who = g.principal.displayLabel || 'A workload identity';
    if (g.isExpired) found.push({ tone: 'danger', text: `${who}'s grant of ${roleLabel(g.roleKey)} has expired.` });
    if (g.principalKind === 'agent-identity' || g.principalKind === 'agent-user') found.push({ tone: 'danger', text: `${who} is an agent identity, which never holds a grant.` });
    if (!g.assignmentFound) found.push({ tone: 'warning', text: `${who}'s grant of ${roleLabel(g.roleKey)} has no matching app role assignment in the directory.` });
  }
  for (const d of check.directAssignments) {
    if (!d.coveredByGrant) found.push({ tone: 'danger', text: `${d.principal.displayLabel || 'Someone'} holds ${roleLabel(d.roleKey)} directly, with no current workload grant.` });
  }
  return found;
}

// The rows a binding change starts from, for the configuration change sheet: the role's binding rows as they are now
// (the person changes the group before the check), or a new binding row named after the role when it has none. The
// binding rows come from get_configuration (entity role-group-binding), because get_role_access gives no row key.
export function bindingOperations(
  roleKey: string,
  roleLabel: string,
  rows: { key: string; isActive: boolean; fields: Record<string, unknown> }[],
): { op: 'upsert'; entity: string; key: string; values: Record<string, unknown> }[] {
  const own = rows.filter(r => r.isActive && r.fields.roleKey === roleKey);
  if (own.length) return own.map(r => ({ op: 'upsert' as const, entity: 'role-group-binding', key: r.key, values: { ...r.fields } }));
  return [{
    op: 'upsert',
    entity: 'role-group-binding',
    key: `${roleKey}-group`,
    values: { label: `${roleLabel} group`, roleKey, bindingMethod: 'entra-app-role-assignment' },
  }];
}

// ---------- guidelines ----------

export type GuidelineItem = GuidelineList['guidelines'][number];

export const GUIDELINE_SHOWS = ['in-force', 'not-adopted', 'retired', 'all'] as const;
export type GuidelineShow = (typeof GUIDELINE_SHOWS)[number];

export const GUIDELINE_SHOW_TEXT: Record<GuidelineShow, string> = {
  'in-force': 'In force',
  'not-adopted': 'Not adopted',
  retired: 'Retired',
  all: 'All',
};

export function guidelineShown(g: GuidelineItem, show: GuidelineShow): boolean {
  switch (show) {
    case 'in-force':
      return g.isActive;
    case 'not-adopted':
      return g.isActive && !g.isAdopted;
    case 'retired':
      return !g.isActive;
    default:
      return true;
  }
}

// A search over the number, the title, the rule and the organization's own text. Every word must match.
export function guidelineMatches(g: GuidelineItem, text: string): boolean {
  const words = text.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return true;
  const hay = [g.number, g.title, g.rule, g.localText ?? '', g.notAdoptedReason ?? ''].join(' ').toLowerCase();
  return words.every(w => hay.includes(w));
}

export function guidelineCounts(list: GuidelineItem[]): Record<GuidelineShow, number> {
  return {
    'in-force': list.filter(g => g.isActive).length,
    'not-adopted': list.filter(g => g.isActive && !g.isAdopted).length,
    retired: list.filter(g => !g.isActive).length,
    all: list.length,
  };
}

// What a guideline replaces, by its number: supersedes holds a guideline key or number.
export function supersededNumber(supersedes: string | null, list: GuidelineItem[]): string | null {
  if (!supersedes) return null;
  const hit = list.find(g => g.key === supersedes || g.number.toLowerCase() === supersedes.toLowerCase());
  return hit ? hit.number : /^g[0-9]+$/i.test(supersedes) ? supersedes.toUpperCase() : null;
}

// A playbook chapter's title without the "Chapter 1." its stored title may start with, so a line that names the
// chapter by number does not repeat it (wave 3 verification: "chapter 1, Chapter 1. How to adopt").
export function chapterTitleText(chapter: { number: number | string; title: string }): string {
  const title = chapter.title.trim();
  const stripped = title.replace(new RegExp(`^chapter\\s+${String(chapter.number).replace(/[^0-9a-z]/gi, '')}(?![0-9])\\s*[.:,-]?\\s*`, 'i'), '');
  return stripped.length ? stripped : title;
}

export function usedByText(usedBy: GuidelineItem['usedBy']): string {
  const parts = [
    usedBy.approvalRules ? plural(usedBy.approvalRules, 'approval rule', 'approval rules') : null,
    usedBy.gateRequirements ? plural(usedBy.gateRequirements, 'gate requirement', 'gate requirements') : null,
    usedBy.columnProtections ? plural(usedBy.columnProtections, 'protected field', 'protected fields') : null,
  ].filter((p): p is string => p !== null);
  const refusals = usedBy.refusalCodePrefixes.length > 0;
  if (!parts.length) return refusals ? 'The engine names it when it refuses a call.' : 'No rule of the configuration names it yet.';
  return `Applied by ${parts.join(', ')}.${refusals ? ' The engine names it when it refuses a call.' : ''}`;
}

// A guideline number as the page parameter takes it ("G17"), or undefined.
export function guidelineNumber(value: string | undefined): string | undefined {
  const g = (value ?? '').toUpperCase();
  return /^G[1-9][0-9]{0,2}$/.test(g) ? g : undefined;
}

// ---------- measures ----------

export type MeasureItem = Measures['measures'][number];
export type SignalItem = Measures['signals'][number];

export const MEASURE_WINDOW_DAYS = [7, 30, 90] as const;
export type MeasureWindowDays = (typeof MEASURE_WINDOW_DAYS)[number];
export const MEASURE_WINDOWS = 6;

export function windowDaysOf(value: string | undefined): MeasureWindowDays {
  const n = Number(value);
  return (MEASURE_WINDOW_DAYS as readonly number[]).includes(n) ? (n as MeasureWindowDays) : 30;
}

const round1 = (n: number) => Math.round(n * 10) / 10;

// A measure's value in words: "12.5 days", "83%", "4". Null reads "No value".
export function measureValueText(value: number | null, unit: 'days' | 'percent' | 'count' | null): string {
  if (value === null || !Number.isFinite(value)) return 'No value';
  if (unit === 'percent') return `${Math.abs(value) < 10 ? round1(value) : Math.round(value)}%`;
  if (unit === 'days') {
    const d = round1(value);
    return `${d} ${d === 1 ? 'day' : 'days'}`;
  }
  return Math.round(value).toLocaleString('en-US');
}

// "10 of 12" when both are known.
export function ratioText(numerator: number | null, denominator: number | null): string | null {
  if (numerator === null || denominator === null) return null;
  return `${numerator.toLocaleString('en-US')} of ${denominator.toLocaleString('en-US')}`;
}

export const HEALTHY_TEXT: Record<MeasureItem['healthyDirection'], string> = {
  up: 'Higher is better',
  down: 'Lower is better',
  quiet: 'None is best',
};

export type TrendCall = 'better' | 'worse' | 'same';

// The newest window against the window before it that holds a value, read by the measure's healthy direction. Null when
// there is nothing to compare (a coverage measure holds only the newest window).
export function trendCall(measure: Pick<MeasureItem, 'trend' | 'healthyDirection'>): { call: TrendCall; from: number; to: number } | null {
  const points = [...measure.trend].sort((a, b) => a.index - b.index).filter(p => p.value !== null);
  if (points.length < 2) return null;
  const to = points[points.length - 1];
  const last = measure.trend.reduce((m, p) => Math.max(m, p.index), 0);
  if (to.index !== last) return null;
  const from = points[points.length - 2];
  const a = from.value as number;
  const b = to.value as number;
  if (round1(a) === round1(b)) return { call: 'same', from: a, to: b };
  const up = b > a;
  const better = measure.healthyDirection === 'up' ? up : !up;
  return { call: better ? 'better' : 'worse', from: a, to: b };
}

export type SignalState = 'firing' | 'quiet' | 'outside' | 'unknown';

export function signalState(signal: Pick<SignalItem, 'firing' | 'readBy'>): SignalState {
  if (signal.firing === true) return 'firing';
  if (signal.firing === false) return 'quiet';
  return signal.readBy === 'outside' ? 'outside' : 'unknown';
}

export const SIGNAL_STATE_TEXT: Record<SignalState, { text: string; tone: Tone }> = {
  firing: { text: 'Firing', tone: 'danger' },
  quiet: { text: 'Quiet', tone: 'success' },
  outside: { text: 'Read outside the product', tone: 'subtle' },
  unknown: { text: 'Not read yet', tone: 'subtle' },
};

// The measure or signal a drill opens, from the page parameters. A measure wins over a signal.
export function drillOf(measureKey: string | undefined, signal: string | undefined): { measureKey: string } | { signalNumber: number } | null {
  if (measureKey) return { measureKey };
  const n = Number(signal);
  return Number.isInteger(n) && n >= 1 && n <= 10 ? { signalNumber: n } : null;
}

// ---------- admin ----------

export const STEP_UP_TEXT: Record<string, string> = {
  off: 'Off: an approval answer needs no fresh sign-in',
  'fresh-sign-in': 'A fresh sign-in before each approval answer',
  'authentication-context': 'A Conditional Access check before each approval answer',
};

export function stepUpText(mode: string): string {
  return STEP_UP_TEXT[mode] ?? plainWords(mode);
}

const STORE_TEXT: Record<string, string> = {
  'azure-sql': 'Azure SQL',
  sqlite: 'SQLite, on this machine',
  'in-memory': 'In memory, on this machine (nothing is kept after a restart)',
  'row-store': 'Row store',
};

export function storeText(kind: string | null): string {
  return kind ? STORE_TEXT[kind] ?? plainWords(kind) : 'Not reported';
}

// The store's schema against the version the host needs.
export function schemaState(store: { schemaVersion: number | null; requiredSchemaVersion: number | null }): { text: string; tone: Tone } {
  const { schemaVersion: at, requiredSchemaVersion: need } = store;
  if (at === null && need === null) return { text: 'This store keeps no schema version', tone: 'subtle' };
  if (at === null) return { text: `Version unknown; the host needs ${need}`, tone: 'warning' };
  if (need === null || at === need) return { text: `Version ${at}${need === null ? '' : ', as the host needs'}`, tone: 'success' };
  if (at < need) return { text: `Version ${at}; the host needs ${need}`, tone: 'danger' };
  return { text: `Version ${at}, newer than the ${need} the host needs`, tone: 'warning' };
}

export type Notice = DueStateRefreshResult['notices'][number];

export const NOTICE_TEXT: Record<Notice['notice'], string> = {
  remind: 'Reminder',
  repeat: 'Repeat reminder',
  escalate: 'Escalation',
  overdue: 'Overdue',
  expired: 'Expired',
  flag: 'Flag notice',
};

export const NOTICE_TONE: Record<Notice['notice'], Tone> = {
  remind: 'informative',
  repeat: 'informative',
  escalate: 'warning',
  overdue: 'warning',
  expired: 'danger',
  flag: 'danger',
};

export const SUBJECT_TEXT: Record<Notice['subjectKind'], string> = {
  review: 'Review',
  document: 'Document',
  approval: 'Approval request',
  claim: 'Gap check claim',
  deployment: 'Deployment',
};

const NOTICE_KINDS = Object.keys(NOTICE_TEXT);

// get_admin_status types its notice items loosely (data/contracts.ts); they have the shape of run_due_state_refresh's
// notices. An item that does not is left out.
export function noticesOf(items: AdminStatus['notices']['items']): Notice[] {
  return items.filter((i): i is Notice & Record<string, unknown> =>
    typeof i === 'object' && i !== null
    && typeof i.notice === 'string' && NOTICE_KINDS.includes(i.notice)
    && typeof i.label === 'string'
    && typeof i.recordUrl === 'string'
    && typeof i.to === 'object' && i.to !== null);
}

// Whom a notice went to, in words: "Nadia Ferreira and the deputies", "Change manager".
export function toWhomText(to: Notice['to']): string {
  const names = [
    ...(to.people ?? []).map(p => p.displayLabel).filter(Boolean),
    ...(to.roles ?? []).map(r => r.label),
    ...(to.ownerRole ? [`the ${to.ownerRole.label.toLowerCase()}`] : []),
  ];
  const text = names.length ? names.join(', ') : 'Nobody named';
  return to.toDeputies ? `${text}, and the deputies` : text;
}

export const OUTBOX_STATES = ['failed', 'expired', 'pending', 'claimed', 'sent', 'answered', 'cancelled'] as const;
export type OutboxState = (typeof OUTBOX_STATES)[number];

export const OUTBOX_TEXT: Record<OutboxState, { text: string; help: string }> = {
  pending: { text: 'Waiting to send', help: 'Queued for the approval runner, not sent yet.' },
  claimed: { text: 'Being sent', help: 'The approval runner took it and is sending the card.' },
  sent: { text: 'Sent, no answer yet', help: 'The card or link went out; the person has not answered.' },
  answered: { text: 'Answered', help: 'The person answered on the decision page.' },
  expired: { text: 'Expired', help: 'Nobody answered before the request expired.' },
  cancelled: { text: 'Canceled', help: 'The decision moved on, or was asked again of someone else, so the engine canceled the request.' },
  failed: { text: 'Failed', help: 'The runner could not send it after its attempts.' },
};

export function outboxStateOf(value: string | undefined): OutboxState | undefined {
  return (OUTBOX_STATES as readonly string[]).includes(value ?? '') ? (value as OutboxState) : undefined;
}

export type PolicyRow = AdminStatus['toolPolicy']['rows'][number];

export const POLICY_FILTERS = ['all', 'write', 'read', 'person-only', 'off'] as const;
export type PolicyFilter = (typeof POLICY_FILTERS)[number];

export const POLICY_FILTER_TEXT: Record<PolicyFilter, string> = {
  all: 'Every tool',
  write: 'Writes',
  read: 'Reads',
  'person-only': 'A person only',
  off: 'Turned off',
};

export function isPersonOnly(row: Pick<PolicyRow, 'entries'>): boolean {
  return !row.entries.includes('ai-client');
}

export function isOff(row: Pick<PolicyRow, 'isEnabled' | 'isServed'>): boolean {
  return !row.isEnabled || !row.isServed;
}

export function policyMatches(row: PolicyRow, filter: PolicyFilter, text: string, roleLabel: (key: string) => string): boolean {
  if (filter === 'write' && row.access !== 'write') return false;
  if (filter === 'read' && row.access !== 'read') return false;
  if (filter === 'person-only' && !isPersonOnly(row)) return false;
  if (filter === 'off' && !isOff(row)) return false;
  const words = text.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return true;
  const hay = [toolTitle(row.tool), row.tool, String(row.number), toolsetText(row.toolset), ...row.allowedRoleKeys.map(roleLabel)].join(' ').toLowerCase();
  return words.every(w => hay.includes(w));
}

// Who may call a tool, in words: "Anyone signed in", "Platform admin, Auditor", "Maker, or an owner of the application".
export function whoMayCall(row: Pick<PolicyRow, 'anyoneSignedIn' | 'allowedRoleKeys' | 'relations'>, roleLabel: (key: string) => string): string {
  if (row.anyoneSignedIn) return 'Anyone signed in';
  const roles = row.allowedRoleKeys.map(roleLabel);
  const relations = row.relations.map(relationText);
  if (!roles.length && !relations.length) return 'Nobody: no role is allowed';
  if (!relations.length) return roles.join(', ');
  if (!roles.length) return relations.join(', or ').replace(/^./, c => c.toUpperCase());
  return `${roles.join(', ')}, or ${relations.join(', or ')}`;
}

export type ClientItem = AdminStatus['clients'][number];

export function clientState(c: Pick<ClientItem, 'isKnown' | 'isAllowed'>): { text: string; tone: Tone } {
  if (!c.isKnown) return { text: 'Not on the Known Client list', tone: 'warning' };
  return c.isAllowed === false ? { text: 'Blocked', tone: 'danger' } : { text: 'Sanctioned', tone: 'success' };
}

// What one run of the due-state refresh changed, as lines with a count, for the page's "What the run changed" panel.
export function refreshLines(counts: DueStateRefreshResult['counts']): { key: string; text: string; count: number; attention: boolean }[] {
  const line = (key: keyof DueStateRefreshResult['counts'], one: string, many: string, attention = false) => ({ key, text: counts[key] === 1 ? one : many, count: counts[key], attention });
  return [
    line('reviewsOpened', 'Review opened for its next due day', 'Reviews opened for their next due day'),
    line('reviewsOverdue', 'Review turned overdue', 'Reviews turned overdue', true),
    line('documentsStale', 'Document turned stale', 'Documents turned stale', true),
    line('decisionsRefreshed', 'Decision whose approvals were refreshed', 'Decisions whose approvals were refreshed'),
    line('approvalsExpired', 'Approval request expired', 'Approval requests expired', true),
    line('approvalsCancelled', 'Approval request canceled', 'Approval requests canceled'),
    line('approvalsOpened', 'Approval request opened again', 'Approval requests opened again'),
    line('reminders', 'Reminder sent', 'Reminders sent'),
    line('escalations', 'Escalation sent', 'Escalations sent', true),
    line('flagNotices', 'Flag notice sent', 'Flag notices sent', true),
    line('failed', 'Item failed, the rest were kept', 'Items failed, the rest were kept', true),
  ];
}

export function refreshTotal(counts: DueStateRefreshResult['counts']): number {
  return refreshLines(counts).filter(l => l.key !== 'failed').reduce((n, l) => n + l.count, 0);
}

export const ADMIN_DAYS = [7, 30] as const;
export type AdminDays = (typeof ADMIN_DAYS)[number];

export function adminDaysOf(value: string | undefined): AdminDays {
  return value === '30' ? 30 : 7;
}

// A content hash, short: the first 12 characters after its algorithm name, for telling two versions apart.
export function shortHash(hash: string): string {
  const at = hash.indexOf(':');
  return (at >= 0 ? hash.slice(at + 1) : hash).slice(0, 12);
}

// ---------- wave 4: the outbox retry (retry_approval_card, tool 101) ----------

export type CardAction = 'send-again' | 'close';

// A pending request as list_approvals view open gives it: when it expires and where its decision stands.
export interface OpenRequestFacts {
  expiresOn: string | null;
  decisionMeaning: string;
}

// What "Send again" would do to a card that needs a look, as the engine decides it (B24-22): a card whose request was
// answered, expired, or canceled meanwhile (its decision moved on) is closed; a failed card, or one sent more than two
// days ago with its request still open, goes back to the approval runner. open holds the pending requests by ID
// (lowercase); complete is true when that list holds every pending request. A card the list cannot place is offered as
// "Send again", and the engine's check says whether it closes instead. Cards in any other state offer nothing.
export function cardActionOf(
  item: { approvalRecordId: string; state: string },
  open: ReadonlyMap<string, OpenRequestFacts> | null,
  complete: boolean,
  now: number,
): CardAction | null {
  if (item.state !== 'failed' && item.state !== 'sent') return null;
  if (!open) return 'send-again';
  const request = open.get(item.approvalRecordId.toLowerCase());
  if (!request) return complete ? 'close' : 'send-again';
  if (request.decisionMeaning !== 'proposed') return 'close';
  if (request.expiresOn && Date.parse(request.expiresOn) <= now) return 'close';
  return 'send-again';
}

export const CARD_ACTION_TEXT: Record<CardAction, { button: string; title: string; done: string }> = {
  'send-again': { button: 'Send again', title: 'Send the card again', done: 'Sent again from this page' },
  // Never "Close" alone: the sheet's own dismiss button is named Close (QA walk).
  close: { button: 'Close the card', title: 'Close the card', done: 'Closed from this page' },
};

// ---------- wave 4: measure targets and signal thresholds (B24-22) ----------

// The slide-back signals a decision may name (playbook chapter 17), as save_decision_proposal takes them.
export const SLIDE_BACK_SIGNAL_KEYS: readonly SlideBackSignalKey[] = [
  'changes-wait-for-a-meeting', 'environments-by-ticket', 'gap-check-stale', 'registered-late', 'configuration-not-kept',
  'unsanctioned-ai', 'tool-rebuilt', 'no-human-approver', 'documents-slipping', 'access-drift',
];

export function slideBackKeyOf(key: string): SlideBackSignalKey | null {
  return (SLIDE_BACK_SIGNAL_KEYS as readonly string[]).includes(key) ? (key as SlideBackSignalKey) : null;
}

export type TargetState = 'met' | 'missed' | 'unset' | 'no-value';

// A measure against its target: met or missed by the newest value in the healthy direction; unset until the sponsor
// agrees one (the measure-targets setting); no value when a target is set and nothing was measured yet.
export function targetState(m: Pick<MeasureItem, 'target' | 'targetMet'>): TargetState {
  if (m.target === null) return 'unset';
  if (m.targetMet === true) return 'met';
  if (m.targetMet === false) return 'missed';
  return 'no-value';
}

export const TARGET_FILTERS = ['met', 'missed', 'unset'] as const;
export type TargetFilter = (typeof TARGET_FILTERS)[number];

export function targetFilterOf(value: string | undefined): TargetFilter | undefined {
  return (TARGET_FILTERS as readonly string[]).includes(value ?? '') ? (value as TargetFilter) : undefined;
}

export const TARGET_TEXT: Record<TargetState, { text: string; tone: Tone }> = {
  met: { text: 'Meets its target', tone: 'success' },
  missed: { text: 'Short of its target', tone: 'warning' },
  unset: { text: 'No target yet', tone: 'subtle' },
  'no-value': { text: 'No value to compare', tone: 'subtle' },
};

export const TARGET_FILTER_TEXT: Record<TargetFilter, string> = {
  met: 'Meeting their target',
  missed: 'Short of their target',
  unset: 'With no target yet',
};

export function targetCounts(measures: Pick<MeasureItem, 'target' | 'targetMet'>[]): Record<TargetState, number> {
  const counts: Record<TargetState, number> = { met: 0, missed: 0, unset: 0, 'no-value': 0 };
  for (const m of measures) counts[targetState(m)] += 1;
  return counts;
}

// Where a signal's threshold comes from, in words.
export function thresholdSourceText(source: SignalItem['thresholdSource']): string {
  return source === 'configuration' ? 'set in your configuration' : "the playbook's default";
}

// The page's one sentence on where the targets and thresholds come from (the measure-targets and signal-thresholds
// settings), from the counts the read gives.
export function targetsAndThresholdsText(measures: Pick<MeasureItem, 'targetSource'>[], signals: Pick<SignalItem, 'thresholdSource'>[]): string {
  const targets = measures.filter(m => m.targetSource === 'configuration').length;
  const thresholds = signals.filter(s => s.thresholdSource === 'configuration').length;
  const t = targets === 0
    ? 'No measure has a target yet: measure first, then agree one'
    : `${targets} of ${plural(measures.length, 'measure has', 'measures have')} a target set in your configuration`;
  const h = thresholds === 0
    ? "every signal threshold is the playbook's default"
    : thresholds === signals.length
      ? 'every signal threshold is set in your configuration'
      : `${thresholds} of ${signals.length} signal thresholds are set in your configuration, the rest are the playbook's defaults`;
  return `${t}; ${h}.`;
}

// ---------- wave 4: the decision rights of owner roles (Roles and access) ----------

// A configuration row as get_configuration returns it.
export interface ConfigRowLike {
  key: string;
  label: string;
  isActive: boolean;
  fields: Record<string, unknown>;
}

export interface OwnerRoleDuty {
  ownerRole: { key: string; label: string; subjectKinds: string[] };
  // The decision rights that name the owner role, as the holder (fallback false) or as the fallback when the role the
  // right names has no holder on the record (fallback true).
  rights: { key: string; label: string; decisionTypeKey: string; letter: string; fallback: boolean }[];
  // The approval rules that ask the owner role's holder, or ask it as the fallback.
  approvals: { key: string; label: string; decisionTypeKey: string; isConsultation: boolean; fallback: boolean }[];
}

const textOf = (v: unknown): string | null => (typeof v === 'string' && v ? v : null);

// Each owner role with the decision rights and approval rules that name it, from the configuration in force. An owner
// role is held on a record (an application, a data domain, a data location), not through a group, so get_role_access
// lists none of these under a product role: the page reads them from the decision-right and approval-rule rows.
export function ownerRoleDuties(ownerRoles: readonly ConfigRowLike[], rights: readonly ConfigRowLike[], rules: readonly ConfigRowLike[]): OwnerRoleDuty[] {
  return ownerRoles.filter(o => o.isActive).map(o => {
    const kinds = Array.isArray(o.fields.subjectKinds) ? (o.fields.subjectKinds as unknown[]).filter((k): k is string => typeof k === 'string') : [];
    return {
      ownerRole: { key: o.key, label: o.label, subjectKinds: kinds.length ? kinds : ['business-application'] },
      rights: rights.filter(r => r.isActive && (r.fields.ownerRoleKey === o.key || r.fields.fallbackOwnerRoleKey === o.key)).map(r => ({
        key: r.key,
        label: r.label,
        decisionTypeKey: textOf(r.fields.decisionTypeKey) ?? '',
        letter: textOf(r.fields.letter) ?? '',
        fallback: r.fields.ownerRoleKey !== o.key,
      })),
      approvals: rules.filter(r => r.isActive && (r.fields.approverOwnerRoleKey === o.key || r.fields.fallbackOwnerRoleKey === o.key)).map(r => ({
        key: r.key,
        label: r.label,
        decisionTypeKey: textOf(r.fields.decisionTypeKey) ?? '',
        isConsultation: r.fields.isConsultation === true,
        fallback: r.fields.approverOwnerRoleKey !== o.key,
      })),
    };
  });
}

// The product roles whose holders decide through owner roles on records: the application owner holds owner roles on
// applications, the data owner on data domains (their duties in the shipped roles say so). Each maps to the record kind
// its owner roles are held on.
export const OWNER_ROLE_SUBJECT_OF: Readonly<Record<string, string>> = {
  'application-owner': 'business-application',
  'data-owner': 'data-domain',
};

// The owner roles a product role decides through, or none.
export function ownerRolesOf(roleKey: string, duties: readonly OwnerRoleDuty[]): OwnerRoleDuty[] {
  const subject = OWNER_ROLE_SUBJECT_OF[roleKey];
  if (!subject) return [];
  return duties.filter(d => d.ownerRole.subjectKinds.includes(subject) && (d.rights.length > 0 || d.approvals.length > 0));
}

const SUBJECT_TEXT_PLURAL: Record<string, string> = {
  'business-application': 'applications',
  'data-domain': 'data domains',
  'data-location': 'data locations',
};

// Where owner roles are held, in words: "applications and data domains".
export function subjectKindsText(kinds: string[]): string {
  const words = kinds.map(k => SUBJECT_TEXT_PLURAL[k] ?? plainWords(k).toLowerCase());
  if (words.length <= 1) return words[0] ?? '';
  return `${words.slice(0, -1).join(', ')} and ${words[words.length - 1]}`;
}

// The fields the Roles list's search reads: a role matches when every word is in its name, duties, party, bound groups,
// or the decision types it decides or approves.
export function roleSearchFields(role: RoleItem): string[] {
  return [
    role.role.label,
    role.duties ?? '',
    role.party?.label ?? '',
    ...role.bindings.map(b => b.group?.displayLabel ?? ''),
    ...role.decisionRights.map(r => r.decisionType.label),
    ...role.approvals.map(a => a.decisionType.label),
  ];
}
