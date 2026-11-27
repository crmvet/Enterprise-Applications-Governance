// The Owners and Reviews pages' logic: grouping owners by person, the leavers, the checks that decide whether an action
// shows, and the inputs the side sheets send. Fictional people and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { DecisionLogItem, OwnerAssignmentItem, ReviewItem } from '../../data/contracts';
import {
  approvalsText,
  buildCompletion,
  buildEvidence,
  buildLeave,
  buildOutcome,
  buildOwnerChange,
  canProposeOwnerChange,
  canRecordLeave,
  countBy,
  deputyOf,
  groupByPerson,
  heldOnApplications,
  isIsoDate,
  isProblem,
  isSubmitted,
  leaveReason,
  matchPeople,
  namedSuccessorOf,
  ownedApplicationIds,
  personOptions,
  previousDone,
  sortPeople,
  type OwnerChangeForm,
  type WhoAmI,
} from './model';

const APP_A = '00000000-0000-4000-8000-00000000a001';
const APP_B = '00000000-0000-4000-8000-00000000a002';
const NADIA = { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira', principalKind: 'user' as const };
const AMARA = { objectId: '00000000-0000-4000-8000-000000001120', displayLabel: 'Amara Quist', principalKind: 'user' as const };
const LENA = { objectId: '00000000-0000-4000-8000-000000001101', displayLabel: 'Lena Okoro', principalKind: 'user' as const };

let next = 0;
function assignment(over: Partial<OwnerAssignmentItem> & { principal: OwnerAssignmentItem['principal'] }): OwnerAssignmentItem {
  next++;
  return {
    ownerAssignmentId: `00000000-0000-4000-8000-0000000b${String(next).padStart(4, '0')}`,
    ownerRoleKey: 'business-owner',
    label: 'Business owner',
    isDeputy: false,
    deputyFor: null,
    startOn: '2026-01-01',
    endOn: null,
    ownershipDecisionId: null,
    businessApplicationId: APP_A,
    applicationName: 'Safety Findings Tracker',
    state: 'current',
    holder: { directoryState: 'enabled', directoryStateOn: null, leavesOn: null },
    pendingApprovalCount: 0,
    recordUrl: `https://example.test/app/records/business-application/${APP_A}`,
    ...over,
  };
}

function me(roles: string[], tools: string[], principal = NADIA): WhoAmI {
  return { principal, productRoles: roles.map(r => ({ roleKey: r, label: r })), actorKind: 'person', tools };
}

test('owner rows group by person, counting holds, deputies, applications and approvals on held roles', () => {
  const rows = [
    assignment({ principal: NADIA, pendingApprovalCount: 2 }),
    assignment({ principal: NADIA, ownerRoleKey: 'sponsor', label: 'Sponsor', businessApplicationId: APP_B, applicationName: 'Claims Triage Agent' }),
    assignment({ principal: NADIA, ownerRoleKey: 'technical-owner', label: 'Technical owner', isDeputy: true, pendingApprovalCount: 5 }),
    assignment({ principal: AMARA, holder: { directoryState: 'disabled', directoryStateOn: '2026-11-20T08:00:00Z', leavesOn: null } }),
  ];
  const people = groupByPerson(rows);
  assert.deepEqual(people.map(p => p.principal.displayLabel), ['Amara Quist', 'Nadia Ferreira']);
  const nadia = people[1];
  assert.equal(nadia.holds, 2);
  assert.equal(nadia.deputyFor, 1);
  assert.equal(nadia.applications, 2);
  // A deputy's pending approvals do not count: they ask the holder's role.
  assert.equal(nadia.pendingApprovals, 2);
  assert.deepEqual(nadia.roleKeys, ['business-owner', 'sponsor', 'technical-owner']);
  assert.equal(people[0].leaveReason, 'disabled');
});

test('people sort by name, by applications, or leavers first, and the search matches every word', () => {
  const people = groupByPerson([
    assignment({ principal: NADIA }),
    assignment({ principal: NADIA, businessApplicationId: APP_B }),
    assignment({ principal: LENA, holder: { directoryState: 'enabled', directoryStateOn: null, leavesOn: '2026-12-12' } }),
    assignment({ principal: AMARA }),
  ]);
  assert.deepEqual(sortPeople(people, 'name').map(p => p.principal.displayLabel), ['Amara Quist', 'Lena Okoro', 'Nadia Ferreira']);
  assert.equal(sortPeople(people, 'applications')[0].principal.displayLabel, 'Nadia Ferreira');
  assert.equal(sortPeople(people, 'leaving')[0].principal.displayLabel, 'Lena Okoro');
  assert.deepEqual(matchPeople(people, 'nadia fer').map(p => p.principal.displayLabel), ['Nadia Ferreira']);
  assert.equal(matchPeople(people, '  ').length, 3);
});

test('a leaver has a leave date, or an account the owner check found disabled or deleted', () => {
  assert.equal(leaveReason({ directoryState: 'enabled', leavesOn: null }), null);
  assert.equal(leaveReason({ directoryState: 'enabled', leavesOn: '2026-12-12' }), 'leaves');
  assert.equal(leaveReason({ directoryState: 'disabled', leavesOn: '2026-12-12' }), 'disabled');
  assert.equal(leaveReason({ directoryState: 'deleted', leavesOn: null }), 'deleted');
  assert.equal(leaveReason({ directoryState: 'unknown', leavesOn: null }), null);
});

test('the deputy of the same role on the same application is the natural successor', () => {
  const rows = [
    assignment({ principal: AMARA }),
    assignment({ principal: LENA, isDeputy: true }),
    assignment({ principal: NADIA, isDeputy: true, ownerRoleKey: 'technical-owner' }),
  ];
  assert.equal(deputyOf(rows, APP_A, 'business-owner', AMARA.objectId)?.principal.displayLabel, 'Lena Okoro');
  assert.equal(deputyOf(rows, APP_B, 'business-owner'), null);
  assert.equal(deputyOf(rows, APP_A, 'business-owner', LENA.objectId), null);
});

test('an upcoming assignment of the same role names the successor, and the leaver is offered no other', () => {
  const leaver = assignment({ principal: AMARA, startOn: '2026-10-05', endOn: '2026-12-19' });
  const successor = assignment({ principal: NADIA, startOn: '2026-12-19', state: 'upcoming' });
  const otherRole = assignment({ principal: LENA, startOn: '2026-12-19', state: 'upcoming', ownerRoleKey: 'technical-owner' });
  const deputy = assignment({ principal: LENA, startOn: '2026-12-19', state: 'upcoming', isDeputy: true });
  assert.equal(namedSuccessorOf([otherRole, deputy, successor], leaver)?.principal.displayLabel, 'Nadia Ferreira');
  assert.equal(namedSuccessorOf([otherRole, deputy], leaver), null);
  assert.equal(namedSuccessorOf([assignment({ principal: NADIA, startOn: '2026-11-01', state: 'upcoming' })], leaver), null);
});

test('the successor picker leaves out disabled accounts; the leave picker keeps them', () => {
  const rows = [
    assignment({ principal: AMARA, holder: { directoryState: 'disabled', directoryStateOn: null, leavesOn: null } }),
    assignment({ principal: LENA, holder: { directoryState: 'enabled', directoryStateOn: null, leavesOn: '2026-12-12' } }),
  ];
  const signedIn = me(['application-owner'], []);
  assert.deepEqual(personOptions(rows, signedIn).map(o => `${o.displayLabel} ${o.note}`), ['Lena Okoro leaving', 'Nadia Ferreira you']);
  assert.deepEqual(personOptions(rows, signedIn, { includeAway: true }).map(o => o.displayLabel), ['Amara Quist', 'Lena Okoro', 'Nadia Ferreira']);
});

test('propose an owner change shows for an owner of the application, an AI lead or a platform admin', () => {
  const mine = ownedApplicationIds([assignment({ principal: NADIA, businessApplicationId: APP_A.toUpperCase() })]);
  const owner = me(['application-owner'], ['propose_owner_change']);
  assert.equal(canProposeOwnerChange(owner, APP_A, mine), true);
  assert.equal(canProposeOwnerChange(owner, APP_B, mine), false);
  assert.equal(canProposeOwnerChange(me(['platform-admin'], ['propose_owner_change']), APP_B, new Set()), true);
  // The Tool Policy does not give the tool: nothing shows, whatever the owner rows say.
  assert.equal(canProposeOwnerChange(me(['application-owner'], []), APP_A, mine), false);
  // An AI client's session never writes from the web app.
  assert.equal(canProposeOwnerChange({ ...owner, actorKind: 'ai-client-for-person' }, APP_A, mine), false);
  assert.equal(canProposeOwnerChange(null, APP_A, mine), false);
});

test('record a leave shows for the person, a co-owner of their applications, or a platform admin', () => {
  const tools = ['record_owner_leave'];
  const mine = ownedApplicationIds([assignment({ principal: NADIA })]);
  assert.equal(canRecordLeave(me([], tools), NADIA.objectId.toUpperCase(), [], new Set()), true);
  assert.equal(canRecordLeave(me(['application-owner'], tools), AMARA.objectId, [APP_A], mine), true);
  assert.equal(canRecordLeave(me(['application-owner'], tools), AMARA.objectId, [APP_B], mine), false);
  assert.equal(canRecordLeave(me(['platform-admin'], tools), AMARA.objectId, [], new Set()), true);
  assert.equal(canRecordLeave(me(['platform-admin'], []), AMARA.objectId, [], new Set()), false);
});

const form = (over: Partial<OwnerChangeForm>): OwnerChangeForm => ({
  applicationId: APP_A,
  mode: 'add',
  ownerRoleKey: 'business-owner',
  addObjectId: LENA.objectId,
  isDeputy: false,
  startOn: '',
  endAssignmentId: null,
  endingObjectId: null,
  reason: 'Amara left in November.',
  ...over,
});

test('an owner change sends one change: add, replace (end and add together) or end', () => {
  const add = buildOwnerChange(form({ isDeputy: true, startOn: '2026-12-01', addObjectId: LENA.objectId.toUpperCase() }));
  assert.ok(!isProblem(add));
  if (!isProblem(add)) {
    assert.deepEqual(add.input, {
      businessApplicationId: APP_A,
      changes: [{ ownerRoleKey: 'business-owner', addObjectId: LENA.objectId, isDeputy: true, startOn: '2026-12-01' }],
      reason: 'Amara left in November.',
    });
  }
  const replace = buildOwnerChange(form({ mode: 'replace', endAssignmentId: 'end-1', endingObjectId: AMARA.objectId }));
  assert.ok(!isProblem(replace) && replace.input.changes[0].endAssignmentId === 'end-1' && replace.input.changes[0].addObjectId === LENA.objectId);
  const end = buildOwnerChange(form({ mode: 'end', addObjectId: '', endAssignmentId: 'end-1', startOn: '2026-12-01' }));
  assert.ok(!isProblem(end));
  if (!isProblem(end)) assert.deepEqual(end.input.changes, [{ ownerRoleKey: 'business-owner', endAssignmentId: 'end-1' }]);
});

test('an owner change the engine would refuse is caught on the form, with the field marked', () => {
  const check = (over: Partial<OwnerChangeForm>) => {
    const built = buildOwnerChange(form(over));
    return isProblem(built) ? built.field : null;
  };
  assert.equal(check({ ownerRoleKey: '' }), 'changes.ownerRoleKey');
  assert.equal(check({ addObjectId: '' }), 'changes.addObjectId');
  assert.equal(check({ addObjectId: 'not-a-guid' }), 'changes.addObjectId');
  assert.equal(check({ mode: 'replace', endAssignmentId: null }), 'changes.endAssignmentId');
  assert.equal(check({ mode: 'replace', endAssignmentId: 'end-1', endingObjectId: LENA.objectId }), 'changes.addObjectId');
  assert.equal(check({ startOn: '2026-02-30' }), 'changes.startOn');
  assert.equal(check({ reason: '   ' }), 'reason');
  assert.equal(check({ reason: 'x'.repeat(2001) }), 'reason');
});

test('a leave needs the person and a real date; the note is optional', () => {
  const ok = buildLeave({ principalObjectId: AMARA.objectId, leavesOn: '2026-12-12', kind: 'leave', note: '  ' });
  assert.ok(!isProblem(ok));
  if (!isProblem(ok)) assert.deepEqual(ok.input, { principalObjectId: AMARA.objectId, leavesOn: '2026-12-12', kind: 'leave' });
  const noDate = buildLeave({ principalObjectId: AMARA.objectId, leavesOn: '', kind: 'leave', note: '' });
  assert.ok(isProblem(noDate) && noDate.field === 'leavesOn');
  const nobody = buildLeave({ principalObjectId: '', leavesOn: '2026-12-12', kind: 'role-change', note: '' });
  assert.ok(isProblem(nobody) && nobody.field === 'principalObjectId');
  assert.equal(isIsoDate('2026-11-31'), false);
  assert.equal(isIsoDate('2026-11-30'), true);
});

const outcome = {
  reviewId: '00000000-0000-4000-8000-000000005300',
  decisionTypeKey: 'design',
  outcomeKey: 'approve',
  statement: 'Approve the design.',
  context: 'The architecture review looked at the agent design.',
  reason: 'It meets the reference architecture.',
  confidenceKey: '',
  needsOptions: false,
  options: [{ option: '', whyNot: '' }],
  evidenceUrl: '',
  evidenceLabel: '',
};

test('the outcome of a review is a decision proposal about the review', () => {
  const built = buildOutcome({ ...outcome, confidenceKey: 'high', evidenceUrl: 'https://example.test/findings', evidenceLabel: 'Findings' });
  assert.ok(!isProblem(built));
  if (!isProblem(built)) {
    assert.equal(built.input.subjectKind, 'review');
    assert.equal(built.input.subjectId, outcome.reviewId);
    assert.equal(built.input.confidenceKey, 'high');
    assert.equal(built.input.optionsConsidered, undefined);
    assert.deepEqual(built.input.evidence, [{ linkKindKey: 'evidence-url', url: 'https://example.test/findings', label: 'Findings' }]);
  }
});

test('a design outcome needs the options weighed, and evidence must be an https link with a label', () => {
  const noOptions = buildOutcome({ ...outcome, needsOptions: true });
  assert.ok(isProblem(noOptions) && noOptions.field === 'optionsConsidered');
  const half = buildOutcome({ ...outcome, options: [{ option: 'Buy a product', whyNot: '' }] });
  assert.ok(isProblem(half) && half.field === 'optionsConsidered');
  const withOptions = buildOutcome({ ...outcome, needsOptions: true, options: [{ option: 'Buy a product', whyNot: 'No agent support' }, { option: '', whyNot: '' }] });
  assert.ok(!isProblem(withOptions) && withOptions.input.optionsConsidered?.length === 1);
  const http = buildOutcome({ ...outcome, evidenceUrl: 'http://example.test/x', evidenceLabel: 'X' });
  assert.ok(isProblem(http) && http.field === 'evidence');
  const noLabel = buildOutcome({ ...outcome, evidenceUrl: 'https://example.test/x' });
  assert.ok(isProblem(noLabel) && noLabel.field === 'evidence');
  const noOutcome = buildOutcome({ ...outcome, outcomeKey: '' });
  assert.ok(isProblem(noOutcome) && noOutcome.field === 'proposedOutcomeKey');
});

test('evidence on a review is an evidence link with its level', () => {
  const built = buildEvidence({ reviewId: outcome.reviewId, linkKindKey: 'evidence-url', url: 'https://example.test/r', label: 'Access review result', evidenceLevelKey: 'checked' });
  assert.ok(!isProblem(built));
  if (!isProblem(built)) {
    assert.deepEqual(built.input, {
      linkCategory: 'evidence', subjectKind: 'review', subjectId: outcome.reviewId, linkKindKey: 'evidence-url',
      url: 'https://example.test/r', label: 'Access review result', evidenceLevelKey: 'checked',
    });
  }
  const bad = buildEvidence({ reviewId: outcome.reviewId, linkKindKey: 'evidence-url', url: 'javascript:alert(1)', label: 'x', evidenceLevelKey: '' });
  assert.ok(isProblem(bad) && bad.field === 'url');
});

function review(over: Partial<ReviewItem>): ReviewItem {
  return {
    reviewId: '00000000-0000-4000-8000-000000005301',
    recordNumber: null,
    reviewType: { key: 'tier-check', label: 'Tier check' },
    businessApplicationId: APP_A,
    applicationName: 'Safety Findings Tracker',
    portfolio: null,
    status: { label: 'Due', meaning: 'due' },
    trigger: 'cadence',
    dueOn: '2026-11-30',
    doneOn: null,
    nextDueOn: null,
    isOverdue: false,
    reviewer: null,
    leadOwnerRole: null,
    leadOwner: null,
    producesDecisionType: { key: 'tier', label: 'Tier' },
    outcomeDecisionId: null,
    isMine: false,
    isAiCreated: false,
    performedBy: null,
    recordUrl: 'https://example.test/app/records/review/x',
    ...over,
  };
}

test('reviews count by type, and the last outcome is the latest done review of the same type', () => {
  const rows = [
    review({ reviewId: 'r1' }),
    review({ reviewId: 'r2', status: { label: 'Done', meaning: 'done' }, doneOn: '2025-08-28' }),
    review({ reviewId: 'r3', status: { label: 'Done', meaning: 'done' }, doneOn: '2026-08-28' }),
    review({ reviewId: 'r4', reviewType: { key: 'access-check', label: 'Access check' }, status: { label: 'Done', meaning: 'done' }, doneOn: '2026-10-01' }),
  ];
  assert.deepEqual(countBy(rows, r => r.reviewType, 'No type').map(c => `${c.label} ${c.count}`), ['Tier check 3', 'Access check 1']);
  assert.equal(previousDone(rows, rows[0])?.reviewId, 'r3');
  assert.equal(previousDone([rows[0]], rows[0]), null);
});

test('a decision is submitted once it has approval records; the approvals line says where it stands', () => {
  const d = (s: DecisionLogItem['approvalsSummary'], meaning = 'proposed') => ({ approvalsSummary: s, status: { label: meaning, meaning } } as DecisionLogItem);
  assert.equal(isSubmitted(d({ required: 0, approved: 0, rejected: 0, pending: 0 })), false);
  assert.equal(approvalsText(d({ required: 0, approved: 0, rejected: 0, pending: 0 })), 'Not submitted yet');
  // A decision recorded as accepted outside the product's approvals (the seeded demo estate) was never submitted.
  assert.equal(approvalsText(d({ required: 0, approved: 0, rejected: 0, pending: 0 }, 'accepted')), 'No approval request on record');
  assert.equal(isSubmitted(d({ required: 2, approved: 1, rejected: 0, pending: 1 })), true);
  assert.equal(approvalsText(d({ required: 2, approved: 1, rejected: 0, pending: 1 })), '1 of 2 approvals');
  assert.equal(approvalsText(d({ required: 2, approved: 1, rejected: 1, pending: 0 })), '1 rejected, 1 approved');
});

test('completing a review: a date no later than today, and an evidence link only with its label', () => {
  const base = { reviewId: '00000000-0000-4000-8000-000000005301', doneOn: '', today: '2026-11-24', evidenceUrl: '', evidenceLabel: '' };
  assert.deepEqual(buildCompletion(base), { input: { reviewId: base.reviewId } });
  const later = buildCompletion({ ...base, doneOn: '2026-11-25' });
  assert.ok(isProblem(later) && later.field === 'doneOn');
  const unlabelled = buildCompletion({ ...base, evidenceUrl: 'https://example.test/adr' });
  assert.ok(isProblem(unlabelled) && unlabelled.field === 'evidence');
  const notHttps = buildCompletion({ ...base, evidenceUrl: 'http://example.test/adr', evidenceLabel: 'ADR' });
  assert.ok(isProblem(notHttps) && notHttps.field === 'evidence');
  assert.deepEqual(buildCompletion({ ...base, doneOn: '2026-11-23', evidenceUrl: 'https://example.test/adr', evidenceLabel: 'ADR 2' }), {
    input: { reviewId: base.reviewId, doneOn: '2026-11-23', evidence: [{ linkKindKey: 'evidence-url', url: 'https://example.test/adr', label: 'ADR 2' }] },
  });
});

test('an owner role held only on data domains or locations is not an application owner role (D-37)', () => {
  assert.equal(heldOnApplications(undefined), true);
  assert.equal(heldOnApplications([]), true);
  assert.equal(heldOnApplications(['business-application', 'data-domain']), true);
  assert.equal(heldOnApplications(['data-domain']), false);
  assert.equal(heldOnApplications(['data-location']), false);
});
