// The teams pages' words and counts.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { TEAM_KINDS } from '../../app/routes';
import type { OwnerAssignment, TeamAsk } from '../../data/contracts';
import {
  answerText,
  approvalText,
  askedText,
  assignmentsText,
  decisionWords,
  directoryText,
  expiry,
  holderTarget,
  kindCount,
  membersFigure,
  membersFromText,
  orderKinds,
  ownerHolders,
  peopleText,
  rightText,
  roleTeamText,
  scopeText,
  sectionId,
  TEAM_AT,
  TEAM_KIND_HELP,
  TEAM_KIND_ORDER,
  TEAM_KIND_WORDS,
  TEAM_SECTIONS,
  teamsByOwnerRole,
  teamsInput,
  teamSummary,
  waitingOnLead,
} from './logic';

const lk = (key: string, label: string) => ({ key, label });

test('every kind the route takes has words, help and a place in the order', () => {
  assert.deepEqual([...TEAM_KIND_ORDER], [...TEAM_KINDS]);
  for (const kind of TEAM_KINDS) {
    assert.ok(TEAM_KIND_WORDS[kind].length > 0, kind);
    assert.ok(TEAM_KIND_HELP[kind].length > 0, kind);
  }
  assert.deepEqual(orderKinds(['support-team', 'committee']), ['committee', 'support-team']);
});

test('each tab counts the rows it opens, and no kind counts them all', () => {
  const counts = { all: 19, committee: 9, supportTeam: 4, roleGroup: 6, other: 0 };
  assert.equal(kindCount(counts, undefined), 19);
  assert.equal(kindCount(counts, 'committee'), 9);
  assert.equal(kindCount(counts, 'support-team'), 4);
  assert.equal(kindCount(counts, 'role-group'), 6);
  assert.equal(kindCount(counts, 'other'), 0);
});

test('the list reads the address as list_teams input, and sends nothing the address does not hold', () => {
  assert.deepEqual(teamsInput({}), { top: 50 });
  assert.deepEqual(teamsInput({ kind: 'committee', text: 'change', roleKey: 'change-board' }), { top: 50, kind: 'committee', text: 'change', roleKey: 'change-board' });
});

test('the members note says where the members come from, and never claims Entra on a demo install', () => {
  assert.match(membersFromText('entra-live'), /Entra/);
  assert.match(membersFromText('demo-personas'), /demo personas/);
  assert.doesNotMatch(membersFromText('demo-personas'), /Entra/);
  assert.match(membersFromText('not-read'), /managed in Entra/);
});

test('the People figure is a count when read, "In Entra" when this install does not read it, "Not read" when Entra did not answer', () => {
  assert.equal(peopleText({ memberCount: 3 }, 'demo-personas'), '3 people');
  assert.equal(peopleText({ memberCount: 1 }, 'entra-live'), '1 person');
  assert.equal(peopleText({ memberCount: null }, 'not-read'), 'In Entra');
  assert.equal(peopleText({ memberCount: null }, 'entra-live'), 'Not read');
});

test('the Members figure never shows a number the read did not give', () => {
  const read = membersFigure({ counts: { members: 4, roles: 1, decisionTypes: 2, applications: 0, assignments: 0, waiting: 0, waitingOnMembers: 0, membersOwn: 0, answered: 0, ownerGaps: 0 }, membersReadFrom: 'demo-personas' });
  assert.deepEqual(read, { value: 4, failed: false, caption: 'People of the demo in the group' });
  const off = membersFigure({ counts: { members: null, roles: 1, decisionTypes: 2, applications: 0, assignments: 0, waiting: 0, waitingOnMembers: 0, membersOwn: 0, answered: 0, ownerGaps: 0 }, membersReadFrom: 'not-read' });
  assert.equal(off.value, null);
  assert.equal(off.failed, true);
  assert.match(off.caption, /managed in Entra/);
  const down = membersFigure({ counts: { members: null, roles: 0, decisionTypes: 0, applications: 0, assignments: 0, waiting: 0, waitingOnMembers: 0, membersOwn: 0, answered: 0, ownerGaps: 0 }, membersReadFrom: 'entra-live' });
  assert.match(down.caption, /did not answer/);
});

test('a directory state needs words only when the group is not plainly enabled', () => {
  assert.equal(directoryText('enabled'), null);
  assert.equal(directoryText(null), null);
  assert.match(directoryText('disabled') ?? '', /disabled/);
  assert.match(directoryText('deleted') ?? '', /deleted/);
  assert.match(directoryText('unknown') ?? '', /Not found/);
});

test('what a team does on a decision type is said once, strongest first', () => {
  assert.deepEqual(decisionWords({ letters: ['consulted', 'accountable'], approves: true, consulted: true }), ['Approves', 'Accountable', 'Consulted']);
  assert.deepEqual(decisionWords({ letters: [], approves: false, consulted: true }), ['Consulted']);
  assert.deepEqual(decisionWords({ letters: ['responsible'], approves: false, consulted: false }), ['Responsible']);
});

test('a decision right and an approval rule read as a sentence, with their scope', () => {
  const right = { decisionRight: lk('r1', 'R1'), decisionType: lk('exception', 'Exception'), letter: 'accountable', scope: 'All' };
  assert.equal(rightText(right), 'Accountable for Exception');
  assert.equal(rightText({ ...right, letter: 'consulted' }), 'Consulted on Exception');
  assert.equal(rightText({ ...right, letter: 'informed' }), 'Informed of Exception');
  const rule = { approvalRule: lk('a1', 'A1'), decisionType: lk('intake', 'Intake'), isConsultation: true, scope: 'Tier 1', guidelineRef: null };
  assert.equal(approvalText(rule), 'Consulted on Intake');
  assert.equal(approvalText({ ...rule, isConsultation: false }), 'Approves Intake');
  assert.equal(scopeText('All'), 'Applies in every case');
  assert.equal(scopeText('Tier 1, reaching AI'), 'Applies to Tier 1, reaching AI');
});

const ask = (over: Partial<TeamAsk>): TeamAsk => ({
  approvalRecordId: 'a',
  decision: { decisionId: 'd', recordNumber: 'DEC-0001', decisionType: lk('exception', 'Exception'), statement: 'S', status: { label: 'Proposed', meaning: 'proposed' } },
  subject: { subjectKind: 'business-application', label: 'App', businessApplicationId: null, applicationName: null },
  askedRole: lk('change-board', 'Change board'),
  askedOwnerRole: null,
  isConsultation: false,
  outcome: { label: 'Pending', meaning: 'pending' },
  approver: null,
  requestedOn: null,
  respondedOn: null,
  expiresOn: null,
  isExpired: false,
  recordUrl: '/app/records/decision/d',
  ...over,
} as TeamAsk);

test('a request says what it asks, and a consultation says it asks for advice', () => {
  assert.equal(askedText(ask({})), 'Asks the Change board role');
  assert.equal(askedText(ask({ askedRole: null, askedOwnerRole: lk('business-owner', 'Business owner'), isConsultation: true })), 'Asks the business owner for advice');
});

test('an expired request or one due within 48 hours needs attention; a later one is plain', () => {
  const now = Date.parse('2026-11-25T12:00:00Z');
  assert.equal(expiry(ask({}), now).tone, 'subtle');
  assert.equal(expiry(ask({ expiresOn: '2026-11-24T12:00:00Z' }), now).tone, 'danger');
  assert.equal(expiry(ask({ expiresOn: '2026-11-30T12:00:00Z', isExpired: true }), now).tone, 'danger');
  assert.equal(expiry(ask({ expiresOn: '2026-11-26T12:00:00Z' }), now).tone, 'warning');
  assert.equal(expiry(ask({ expiresOn: '2026-12-05T12:00:00Z' }), now).tone, 'subtle');
  assert.match(answerText(ask({ outcome: { label: 'Approved', meaning: 'approved' }, respondedOn: '2026-11-20T10:00:00Z' })), /^Approved \d/);
});

const assignment = (over: Partial<OwnerAssignment>): OwnerAssignment => ({
  ownerAssignmentId: 'x',
  ownerRoleKey: 'support-owner',
  label: 'Support owner',
  principal: { objectId: 'G1', displayLabel: 'Field Apps Support', principalKind: 'group' },
  isDeputy: false,
  deputyFor: null,
  startOn: '2026-01-01',
  endOn: null,
  ownershipDecisionId: null,
  ...over,
});

test('an owner role waits on its holders first, then the deputies, and never on an assignment not started', () => {
  const rows = [
    assignment({ ownerAssignmentId: 'd', isDeputy: true, principal: { objectId: 'P2', displayLabel: 'Hugo', principalKind: 'user' } }),
    assignment({ ownerAssignmentId: 'h' }),
    assignment({ ownerAssignmentId: 'later', startOn: null }),
    assignment({ ownerAssignmentId: 'other', ownerRoleKey: 'business-owner' }),
  ];
  assert.deepEqual(ownerHolders(rows, 'support-owner').map(r => r.ownerAssignmentId), ['h', 'd']);
});

test('Owners by role lists each group that holds a role, with its holder and deputy counts, and leaves people out', () => {
  const rows = [
    assignment({ ownerAssignmentId: '1' }),
    assignment({ ownerAssignmentId: '2', principal: { objectId: 'g1', displayLabel: 'Field Apps Support', principalKind: 'group' } }),
    assignment({ ownerAssignmentId: '3', isDeputy: true, principal: { objectId: 'G2', displayLabel: 'Service Desk', principalKind: 'group' } }),
    assignment({ ownerAssignmentId: '4', principal: { objectId: 'P1', displayLabel: 'Priya', principalKind: 'user' } }),
  ];
  const teams = teamsByOwnerRole(rows).get('support-owner') ?? [];
  assert.deepEqual(teams.map(t => [t.principal.displayLabel, t.holds, t.deputy]), [['Field Apps Support', 2, 0], ['Service Desk', 0, 1]]);
  assert.equal(roleTeamText(teams[0]), 'holder on 2');
  assert.equal(roleTeamText({ holds: 1, deputy: 2 }), 'holder on 1, deputy on 2');
  assert.equal(assignmentsText([{ ownerRole: lk('support-owner', 'Support owner'), isDeputy: false }, { ownerRole: lk('technical-owner', 'Technical owner'), isDeputy: true }]), 'Support owner, Technical owner (deputy)');
});

test('a group links to its team page, a person to the person page, a workload identity nowhere', () => {
  assert.equal(holderTarget('group'), 'team');
  assert.equal(holderTarget('user'), 'person');
  assert.equal(holderTarget(undefined), 'person');
  assert.equal(holderTarget('agent-identity'), 'none');
  assert.equal(holderTarget('service-principal'), 'none');
  assert.equal(waitingOnLead(1), 'Waiting on');
  assert.equal(waitingOnLead(3), 'Waiting on any of');
});

test('every section the list opens has an anchor the address accepts', () => {
  for (const s of TEAM_SECTIONS) {
    assert.ok(TEAM_AT.test(s), s);
    assert.equal(sectionId(s), `team-${s}`);
  }
  assert.equal(TEAM_AT.test('everything'), false);
});

test('the header says what the team is and what it holds, in words', () => {
  const counts = { members: 3, roles: 1, decisionTypes: 2, applications: 0, assignments: 0, waiting: 0, waitingOnMembers: 0, membersOwn: 0, answered: 0, ownerGaps: 0 };
  assert.equal(teamSummary({ kinds: ['committee'], counts }), 'A committee that holds 1 product role and answers 2 decision types.');
  assert.equal(
    teamSummary({ kinds: ['support-team'], counts: { ...counts, roles: 0, decisionTypes: 0, applications: 4 } }),
    'A support team that holds owner roles on 4 applications.',
  );
  assert.equal(teamSummary({ kinds: ['other'], counts: { ...counts, roles: 0, decisionTypes: 0 } }), 'A group the product knows. It holds no product role and no owner role now.');
  assert.equal(teamSummary({ kinds: ['support-team'], counts: { ...counts, roles: 0, decisionTypes: 0, assignments: 2 } }), 'A support team that holds owner roles on data.');
});
