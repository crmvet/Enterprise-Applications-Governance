// The engine's answer in words, wave 4b: a person's claim asks the owners now, a withdrawal says what it canceled
// once, and a proposal sheet hands its decision and the engine's answer to the follow-up. Fictional people
// and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Envelope, WriteToolData } from './contracts';
import { proposalHandOff, readEngineAnswer, restatedInSummary } from './writes';

const person = {
  actorKind: 'person' as const,
  principal: { objectId: '00000000-0000-4000-8000-000000000214', displayLabel: 'Owen Mbeki' },
  onBehalfOf: null,
  clientApp: { appId: '00000000-0000-4000-8000-0000000000ad', label: 'Web app' },
};
const stamp = { engineVersion: '1.0.0', engineCheckedOn: '2026-11-25T09:00:00Z' };

function ok<T>(data: T, summary = 'Done.'): Envelope<T> {
  return {
    ok: true,
    summary,
    data,
    asOf: '2026-11-25T09:00:00Z',
    configurationSetVersion: 3,
    couldNotRead: [],
    warnings: [],
    untrustedFields: [],
    nextSteps: [],
    correlationId: 'corr-4b',
  } as Envelope<T>;
}

function claim(over: Partial<WriteToolData['claim_resource']>): WriteToolData['claim_resource'] {
  return {
    recordId: '00000000-0000-4000-8000-00000000c001',
    status: { label: 'Claimed', meaning: 'claimed' },
    performedBy: person,
    isAiCreated: false,
    engineStamp: stamp,
    created: false,
    technicalComponentId: '00000000-0000-4000-8000-00000000c101',
    businessApplicationId: '00000000-0000-4000-8000-00000000a001',
    applicationCreated: false,
    applicationStatus: { label: 'Registered', meaning: 'registered' },
    ownershipDecisionId: '00000000-0000-4000-8000-00000000d001',
    intakeRequestId: null,
    decisionSubmitted: true,
    approvalsRequested: [],
    approvalsNeeded: [{ approverOwnerRoleKey: 'business-owner', approvalMode: 'first-to-respond', minimumCount: 1 }],
    ...over,
  };
}

test("a person's claim asks the owners now, and a check of it says who would be asked (S-03.6)", () => {
  const sent = claim({
    approvalsRequested: [{ approvalRecordId: '00000000-0000-4000-8000-00000000e001', requiredOwnerRoleKey: 'business-owner', approvalMode: 'first-to-respond', expiresOn: '2026-11-30T09:00:00Z', channelKey: 'web', isConsultation: false }],
  });
  const answer = readEngineAnswer('claim_resource', ok(sent, 'The item is claimed and added to Field Knowledge. 1 owner is asked to confirm it on the decision page.'));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.askedNow.map(a => [a.by, a.key]), [['owner-role', 'business-owner']]);
  assert.deepEqual(answer.askedLater, []);

  // A check opens no request (approvalsRequested is empty), so it lists the approvals needed as would be asked.
  const check = readEngineAnswer('claim_resource', ok(claim({ approvalsRequested: [] })), true);
  assert.equal(check.kind, 'ok');
  if (check.kind !== 'ok') return;
  assert.deepEqual(check.askedNow.map(a => [a.by, a.key]), [['owner-role', 'business-owner']]);
  assert.deepEqual(check.askedLater, []);
});

test("an AI client's claim leaves the ownership decision proposed: the owners are asked once the person submits it", () => {
  const answer = readEngineAnswer('claim_resource', ok(claim({ decisionSubmitted: false, isAiCreated: true })));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.askedNow, []);
  assert.deepEqual(answer.askedLater.map(a => a.key), ['business-owner']);
});

function withdrawn(n: number): WriteToolData['withdraw_record'] {
  return {
    recordId: '00000000-0000-4000-8000-00000000b001',
    status: { label: 'Withdrawn', meaning: 'withdrawn' },
    performedBy: person,
    isAiCreated: false,
    engineStamp: stamp,
    created: false,
    subjectKind: 'intake-request',
    subjectId: '00000000-0000-4000-8000-00000000b001',
    cancelledApprovals: Array.from({ length: n }, (_, i) => `00000000-0000-4000-8000-00000000f00${i}`),
    withdrawnDecisionId: null,
  };
}

test('a withdrawal says once what it canceled: the engine says it after the write, the page in a check', () => {
  const done = readEngineAnswer('withdraw_record', ok(withdrawn(5), 'Withdrawn. 5 waiting approvals are canceled.'));
  assert.equal(done.kind, 'ok');
  if (done.kind !== 'ok') return;
  assert.equal(done.summary, 'Withdrawn. 5 waiting approvals are canceled.');
  assert.deepEqual(done.facts, []);
  const check = readEngineAnswer('withdraw_record', ok(withdrawn(5), 'Would withdraw.'), true);
  assert.equal(check.kind, 'ok');
  if (check.kind !== 'ok') return;
  assert.deepEqual(check.facts, ['5 approval requests would be canceled.']);
});

test('a fact whose count and noun the summary already states is not said twice', () => {
  // The withdraw example of the QA walk.
  assert.equal(restatedInSummary('Withdrawn. 5 waiting approvals are canceled.', '5 approval requests were canceled.'), true);
  assert.equal(restatedInSummary('Withdrawn. 1 waiting approval is canceled.', '1 approval request was canceled.'), true);
  // Another count, another noun, or a fact of more than one sentence stays.
  assert.equal(restatedInSummary('Withdrawn. 4 waiting approvals are canceled.', '5 approval requests were canceled.'), false);
  assert.equal(restatedInSummary('Withdrawn. 5 waiting approvals are canceled.', '5 owner roles are affected.'), false);
  assert.equal(restatedInSummary('The leave of 2 owner roles is recorded.', '2 owner roles are affected. 1 of them has no deputy.'), false);
  assert.equal(restatedInSummary('Saved.', 'The decider is Rosa Tamaki.'), false);
  assert.equal(restatedInSummary('', '5 approval requests were canceled.'), false);
});

function proposal(recordId: string | null): WriteToolData['save_decision_proposal'] {
  return {
    recordId,
    status: { label: 'Proposed', meaning: 'proposed' },
    performedBy: person,
    isAiCreated: false,
    engineStamp: stamp,
    created: true,
    approvalsNeeded: [{ approverOwnerRoleKey: 'business-owner', approvalMode: 'first-to-respond', minimumCount: 1 }],
  } as unknown as WriteToolData['save_decision_proposal'];
}

test('a proposal sheet hands the new decision and the engine\'s answer to "Send it for approval now?"', () => {
  const id = '00000000-0000-4000-8000-00000000d201';
  const handed = proposalHandOff(ok(proposal(id), 'The priority decision is proposed. Submit it to ask for the approvals.') as Parameters<typeof proposalHandOff>[0]);
  assert.ok(handed);
  if (!handed) return;
  assert.equal(handed.decisionId, id);
  assert.equal(handed.answer.kind, 'ok');
  if (handed.answer.kind !== 'ok') return;
  assert.equal(handed.answer.summary, 'The priority decision is proposed. Submit it to ask for the approvals.');
  assert.deepEqual(handed.answer.askedLater.map(a => a.key), ['business-owner']);
  // No decision written, nothing to send.
  assert.equal(proposalHandOff(ok(proposal(null)) as Parameters<typeof proposalHandOff>[0]), null);
});
