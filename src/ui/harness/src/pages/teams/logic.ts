// The teams pages' words and counts, with no React in them (tested in logic.test.ts).
//   TEAM_KIND_WORDS, TEAM_KIND_HELP    a kind as a badge says it, and what it means in one sentence
//   orderKinds(kinds)                  a team's kinds in the page's order: committee, support team, role group, other
//   kindCount(counts, kind)            list_teams counts for one tab (All when no kind)
//   membersFromText(readFrom)          where the members come from, in a sentence
//   peopleText(item, readFrom)         the People figure of a row: "3 people", "In Entra" or "Not read"
//   directoryText(state)               a group's state in Entra when it is not plain enabled, else null
//   teamsInput(route)                  the list_teams input of the Teams list's address
//   decisionWords(type)                what a team does on one decision type: "Approves", "Consulted", "Accountable"
//   rightText / approvalText           one decision right or approval rule of a role, in words
//   askedText(ask)                     what a request asks: the role or owner role, and whether it is a consultation
//   expiry(ask, now)                   a waiting request's expiry: its words and how urgent it is
//   answerText(ask)                    "Approved 2 Dec 2026" for a recent answer
//   ownerHolders(owners, roleKey)      who holds an owner role on an application now: the holders, then the deputies
//   TEAM_SECTIONS, TEAM_AT             the team page's sections, which the list's figures open (?at=<section>)
//   scopeText(scope)                   where a decision right or approval rule applies, in a sentence
//   membersFigure(detail)              the team page's Members figure: the count, or "Not read" and why
//   assignmentsText(assignments)       the owner roles a team holds on one application: "Support owner (deputy)"
//   holderTarget(kind)                 where a name links: a group to its team page, a person to the person page
//   waitingOnLead(n)                   the decision page's words before the teams a request waits on
//   teamsByOwnerRole(items)            Owners by role: the groups that hold each owner role, and on how many apps
import type { RouteOf } from '../../app/routes';
import type {
  DirectoryState,
  ListTeamsInput,
  MembersReadFrom,
  OwnerAssignment,
  PrincipalKind,
  PrincipalRef,
  TeamAsk,
  TeamAssignment,
  TeamDetail,
  TeamKind,
  TeamList,
  TeamListItem,
} from '../../data/contracts';
import { fmtDate, plural } from '../../data/format';

export const TEAM_KIND_WORDS: Record<TeamKind, string> = {
  committee: 'Committee',
  'support-team': 'Support team',
  'role-group': 'Role group',
  other: 'Other group',
};

// What each kind means, for the list's tab and the team page's header. The kinds are worked out from the rules in force
// on every read (document 24, B24-20, choice 3), so the words say what makes a group that kind, not which groups are.
export const TEAM_KIND_HELP: Record<TeamKind, string> = {
  committee: 'A committee holds a product role that the rules ask to approve, to be consulted, or to be responsible or accountable for a decision.',
  'support-team': 'A support team holds support owner or technical owner roles on applications or data.',
  'role-group': 'A role group carries a product role that answers no decision, such as makers, requesters or readers.',
  other: 'A group the product knows that holds no product role and no owner role.',
};

// The kinds in the order the page lists them: committees first, then support teams, role groups and other groups.
export const TEAM_KIND_ORDER: readonly TeamKind[] = ['committee', 'support-team', 'role-group', 'other'];

export function orderKinds(kinds: readonly TeamKind[]): TeamKind[] {
  return TEAM_KIND_ORDER.filter(k => kinds.includes(k));
}

// The count list_teams gives for one tab. The counts follow the search, the role and the member filters, never the
// kind, so each tab's count is the rows that tab opens.
export function kindCount(counts: TeamList['counts'], kind: TeamKind | undefined): number {
  switch (kind) {
    case 'committee': return counts.committee;
    case 'support-team': return counts.supportTeam;
    case 'role-group': return counts.roleGroup;
    case 'other': return counts.other;
    default: return counts.all;
  }
}

// Where the members come from, for the list's note and the team page's Members section.
export function membersFromText(readFrom: MembersReadFrom): string {
  switch (readFrom) {
    case 'entra-live': return 'Members are read live from Entra as you, and never stored.';
    case 'demo-personas': return 'On this demo install the members are the demo personas.';
    default: return 'Membership is managed in Entra. This install does not read it.';
  }
}

// The People figure of a list row. A count when the members were read; "In Entra" when this install does not read them;
// "Not read" when Entra did not answer for this team.
export function peopleText(item: Pick<TeamListItem, 'memberCount'>, readFrom: MembersReadFrom): string {
  if (item.memberCount !== null) return plural(item.memberCount, 'person', 'people');
  return readFrom === 'not-read' ? 'In Entra' : 'Not read';
}

// A group's state in Entra as the owner check last read it, when it needs a word: a plain enabled group, and a group the
// product holds no reference to (null), show nothing.
export function directoryText(state: DirectoryState | null): string | null {
  switch (state) {
    case 'disabled': return 'Group disabled in Entra';
    case 'deleted': return 'Group deleted in Entra';
    case 'unknown': return 'Not found in Entra at the last check';
    default: return null;
  }
}

// The list_teams input of the Teams list's address. The page reads 50 teams a page.
export function teamsInput(route: Pick<RouteOf<'teams'>, 'kind' | 'text' | 'roleKey'>, top = 50): ListTeamsInput {
  const input: ListTeamsInput = { top };
  if (route.kind) input.kind = route.kind;
  if (route.text) input.text = route.text;
  if (route.roleKey) input.roleKey = route.roleKey;
  return input;
}

// ---------- what a team decides ----------

// The decision right letters in words (C31 letter).
export const LETTER_WORDS: Record<string, string> = {
  responsible: 'Responsible',
  accountable: 'Accountable',
  consulted: 'Consulted',
  informed: 'Informed',
  supports: 'Supports, as an AI',
};

const LETTER_ORDER = ['accountable', 'responsible', 'consulted', 'informed', 'supports'];

export function letterWords(letter: string): string {
  return LETTER_WORDS[letter] ?? 'Has a decision right';
}

// What a team does on one decision type, strongest first, each said once: it approves (an approval rule asks it), it is
// consulted (an approval rule consults it, or its decision right letter is consulted), and its decision right letters.
export function decisionWords(type: Pick<TeamDetail['decisionTypes'][number], 'letters' | 'approves' | 'consulted'>): string[] {
  const words: string[] = [];
  if (type.approves) words.push('Approves');
  const letters = [...type.letters].sort((a, b) => rank(a) - rank(b));
  for (const letter of letters) {
    const word = letterWords(letter);
    if (!words.includes(word)) words.push(word);
  }
  if (type.consulted && !words.includes('Consulted')) words.push('Consulted');
  return words;
}

function rank(letter: string): number {
  const i = LETTER_ORDER.indexOf(letter);
  return i < 0 ? LETTER_ORDER.length : i;
}

// One decision right of a role: "Accountable for Exception".
export function rightText(right: TeamDetail['roles'][number]['decisionRights'][number]): string {
  const letter = right.letter;
  const joiner = letter === 'consulted' ? 'on' : letter === 'informed' ? 'of' : letter === 'supports' ? 'on' : 'for';
  return `${letterWords(letter)} ${joiner} ${right.decisionType.label}`;
}

// One approval rule that asks a role: "Approves Exception" or "Consulted on Intake".
export function approvalText(approval: TeamDetail['roles'][number]['approvals'][number]): string {
  return approval.isConsultation ? `Consulted on ${approval.decisionType.label}` : `Approves ${approval.decisionType.label}`;
}

// ---------- requests ----------

// What a request asks, in words: "Asks the Change board role", "Asks the business owner", with "for advice" on a
// consultation, whose answer does not count toward the decision.
export function askedText(ask: Pick<TeamAsk, 'askedRole' | 'askedOwnerRole' | 'isConsultation'>): string {
  const who = ask.askedRole
    ? `Asks the ${ask.askedRole.label} role`
    : ask.askedOwnerRole
      ? `Asks the ${ask.askedOwnerRole.label.toLowerCase()}`
      : 'Asks the team by name';
  return ask.isConsultation ? `${who} for advice` : who;
}

export type ExpiryTone = 'danger' | 'warning' | 'subtle';

// A waiting request's expiry. Expired, or due within 48 hours, needs attention; anything later is plain. A request with
// no expiry says so.
export function expiry(ask: Pick<TeamAsk, 'expiresOn' | 'isExpired'>, now: number): { text: string; tone: ExpiryTone } {
  if (!ask.expiresOn) return { text: 'No expiry', tone: 'subtle' };
  const at = Date.parse(ask.expiresOn);
  if (ask.isExpired || (!Number.isNaN(at) && at <= now)) return { text: `Expired ${fmtDate(ask.expiresOn)}`, tone: 'danger' };
  if (!Number.isNaN(at) && at - now <= 48 * 3600 * 1000) return { text: `Expires ${fmtDate(ask.expiresOn)}`, tone: 'warning' };
  return { text: `Expires ${fmtDate(ask.expiresOn)}`, tone: 'subtle' };
}

// A recent answer, in words: "Approved 2 Dec 2026". The outcome's label comes from the configuration.
export function answerText(ask: Pick<TeamAsk, 'outcome' | 'respondedOn'>): string {
  return ask.respondedOn ? `${ask.outcome.label} ${fmtDate(ask.respondedOn)}` : ask.outcome.label;
}

// ---------- owners ----------

// Who holds an owner role on an application now, as get_application_owners lists them: the holders first, then the
// deputies. A pending request that asks the owner role waits on any of them (document 24, B24-20, choice 8).
export function ownerHolders(owners: readonly OwnerAssignment[], ownerRoleKey: string): OwnerAssignment[] {
  const rows = owners.filter(o => o.ownerRoleKey === ownerRoleKey && o.startOn !== null);
  return [...rows.filter(o => !o.isDeputy), ...rows.filter(o => o.isDeputy)];
}

// The kinds of a directory member that is not a person, in words.
export const PRINCIPAL_KIND_WORDS: Record<PrincipalKind, string> = {
  user: 'Person',
  group: 'Group',
  'agent-identity': 'Agent identity',
  'agent-user': 'Agent user',
  'service-principal': 'Service principal',
  'managed-identity': 'Managed identity',
};

// ---------- the team page's sections ----------

// The sections the list's figures open: /app/teams/<id>?at=waiting scrolls to Waiting now.
export const TEAM_SECTIONS = ['members', 'roles', 'decisions', 'waiting', 'applications', 'gaps', 'data', 'answers'] as const;
export type TeamSection = (typeof TEAM_SECTIONS)[number];
export const TEAM_AT = /^(members|roles|decisions|waiting|applications|gaps|data|answers)$/;

export function sectionId(section: TeamSection): string {
  return `team-${section}`;
}

// The header's one line about the team: its kinds in words, and what it holds.
export function teamSummary(detail: Pick<TeamDetail, 'kinds' | 'counts'>): string {
  const kinds = orderKinds(detail.kinds).map(k => TEAM_KIND_WORDS[k].toLowerCase());
  // A group that is only "other" is a group the product knows, and says so.
  const kindText = kinds.length > 1
    ? `${kinds.slice(0, -1).join(', ')} and ${kinds[kinds.length - 1]}`
    : !kinds.length || detail.kinds[0] === 'other' ? 'group the product knows' : kinds[0];
  const parts: string[] = [];
  if (detail.counts.roles) parts.push(`holds ${plural(detail.counts.roles, 'product role', 'product roles')}`);
  if (detail.counts.decisionTypes) parts.push(`answers ${plural(detail.counts.decisionTypes, 'decision type', 'decision types')}`);
  if (detail.counts.applications) parts.push(`holds owner roles on ${plural(detail.counts.applications, 'application', 'applications')}`);
  else if (detail.counts.assignments) parts.push('holds owner roles on data');
  const article = /^[aeiou]/.test(kindText) ? 'An' : 'A';
  return parts.length ? `${article} ${kindText} that ${joinAnd(parts)}.` : `${article} ${kindText}. It holds no product role and no owner role now.`;
}

export function joinAnd(parts: readonly string[]): string {
  if (parts.length <= 1) return parts[0] ?? '';
  return `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`;
}

// ---------- roles, scopes and members ----------

// Where a decision right or an approval rule applies (get_team scope: "All", or the tier, stage, kind and so on).
export function scopeText(scope: string | null | undefined): string {
  return !scope || scope === 'All' ? 'Applies in every case' : `Applies to ${scope}`;
}

// The Members figure: the number of people when the members were read, else "Not read" with the reason.
export function membersFigure(detail: Pick<TeamDetail, 'counts' | 'membersReadFrom'>): { value: number | null; failed: boolean; caption: string } {
  const value = detail.counts.members;
  if (value !== null) {
    return { value, failed: false, caption: detail.membersReadFrom === 'demo-personas' ? 'People of the demo in the group' : 'People, read live from Entra' };
  }
  return { value: null, failed: true, caption: detail.membersReadFrom === 'not-read' ? 'Membership is managed in Entra' : 'Entra did not answer just now' };
}

// The owner roles a team holds on one application or data subject: "Support owner, Technical owner (deputy)".
export function assignmentsText(assignments: readonly Pick<TeamAssignment, 'ownerRole' | 'isDeputy'>[]): string {
  return assignments.map(a => `${a.ownerRole.label}${a.isDeputy ? ' (deputy)' : ''}`).join(', ');
}

export const DATA_SUBJECT_WORDS: Record<'data-domain' | 'data-location', string> = {
  'data-domain': 'Data domain',
  'data-location': 'Data location',
};

// A long list on the team page gets its own search box: more than this many rows.
export const SEARCH_ABOVE = 8;

// ---------- links in ----------

// Where an owner's or a member's name links: a group to its team page, a person to the person page, and a workload
// identity (an agent, a service principal) nowhere, because neither page is about it.
export type HolderTarget = 'team' | 'person' | 'none';

export function holderTarget(kind: PrincipalKind | null | undefined): HolderTarget {
  if (kind === 'group') return 'team';
  return !kind || kind === 'user' ? 'person' : 'none';
}

// The decision page's "waiting on" words: one team is named, several are each a way to answer.
export function waitingOnLead(teams: number): string {
  return teams > 1 ? 'Waiting on any of' : 'Waiting on';
}

// The groups that hold each owner role in the rows given (Owners by role): for each owner role, every group holder with
// the applications it holds the role on and those it deputizes on, by name. People are left out: the person view
// lists them.
export interface RoleTeam {
  principal: PrincipalRef;
  holds: number;
  deputy: number;
}

export function teamsByOwnerRole(items: readonly Pick<OwnerAssignment, 'ownerRoleKey' | 'principal' | 'isDeputy'>[]): Map<string, RoleTeam[]> {
  const byRole = new Map<string, Map<string, RoleTeam>>();
  for (const a of items) {
    if (a.principal.principalKind !== 'group') continue;
    const groups = byRole.get(a.ownerRoleKey) ?? new Map<string, RoleTeam>();
    byRole.set(a.ownerRoleKey, groups);
    const id = a.principal.objectId.toLowerCase();
    const entry = groups.get(id) ?? { principal: a.principal, holds: 0, deputy: 0 };
    groups.set(id, entry);
    if (a.isDeputy) entry.deputy++;
    else entry.holds++;
  }
  const result = new Map<string, RoleTeam[]>();
  for (const [role, groups] of byRole) {
    result.set(role, [...groups.values()].sort((x, y) => x.principal.displayLabel.localeCompare(y.principal.displayLabel)));
  }
  return result;
}

// One group's share of an owner role, in words: "holder on 3, deputy on 1".
export function roleTeamText(team: Pick<RoleTeam, 'holds' | 'deputy'>): string {
  const parts: string[] = [];
  if (team.holds) parts.push(`holder on ${team.holds}`);
  if (team.deputy) parts.push(`deputy on ${team.deputy}`);
  return parts.join(', ');
}
