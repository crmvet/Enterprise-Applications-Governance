// The web app's writes: the retry ID each write carries, and how the engine's answer reads on the page.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Envelope, WriteToolData } from './contracts';
import { createRequestIdKeeper, newClientRequestId, outcomeUnknown, payloadKey, plainEngineText, PREVIEW_SUMMARY, quotedKeys, readEngineAnswer, retryKeyField, writeBody } from './writes';

const stamp = {
  recordId: '00000000-0000-4000-8000-000000000901',
  performedBy: {
    actorKind: 'person' as const,
    principal: { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' },
    onBehalfOf: null,
    clientApp: { appId: '00000000-0000-4000-8000-0000000000ad', label: 'Web app, demo operator' },
  },
  isAiCreated: false,
  engineStamp: { engineVersion: '1.0.0', engineCheckedOn: '2026-11-24T09:00:00Z' },
  created: true,
};

function ok<T>(data: T, extra: Partial<Envelope<T>> = {}): Envelope<T> {
  return {
    ok: true,
    summary: 'Done.',
    data,
    asOf: '2026-11-24T09:00:00Z',
    configurationSetVersion: 3,
    couldNotRead: [],
    warnings: [],
    untrustedFields: [],
    nextSteps: [],
    correlationId: 'corr-ok',
    ...extra,
  } as Envelope<T>;
}

const refused = (code: string, message: string, more: Record<string, unknown> = {}, correlationId = 'corr-refused') =>
  ({ ok: false, summary: message, error: { code, message, ...more }, correlationId }) as Envelope<never>;

test('a retry ID is "web-" and a random UUID, at most 64 characters', () => {
  assert.equal(newClientRequestId(() => 'abc'), 'web-abc');
  assert.match(newClientRequestId(), /^web-[0-9a-z-]{8,}$/);
  assert.equal(newClientRequestId(() => 'x'.repeat(100)).length, 64);
});

test('the payload key ignores key order, the retry ID, validateOnly and unset fields', () => {
  assert.equal(
    payloadKey({ b: 1, a: { d: [1, { z: 1, y: 2 }], c: undefined }, clientRequestId: 'x', validateOnly: true }),
    payloadKey({ a: { d: [1, { y: 2, z: 1 }] }, b: 1 }),
  );
  assert.notEqual(payloadKey({ a: 1 }), payloadKey({ a: 2 }));
});

test('a retry ID is sent again only after an unknown outcome with the same payload', () => {
  let n = 0;
  const keeper = createRequestIdKeeper(() => `id-${++n}`);
  const input = { approvalRecordId: 'r1', answer: 'approve' };
  assert.equal(keeper.idFor(input), 'id-1');
  // A timeout: the product may have written. The retry carries the same ID, so it never writes twice.
  keeper.settle(refused('upstream_unavailable', 'No answer.', {}, 'client-side'));
  assert.equal(keeper.idFor({ answer: 'approve', approvalRecordId: 'r1' }), 'id-1');
  // A different payload after the unknown outcome gets a new ID (the same ID would answer conflict).
  assert.equal(keeper.idFor({ ...input, reason: 'Checked the runbook.' }), 'id-2');
  // A refusal is an answer: nothing was written, and the next attempt starts fresh.
  keeper.settle(refused('rule_violation', 'Refused.'));
  assert.equal(keeper.idFor({ ...input, reason: 'Checked the runbook.' }), 'id-3');
  // So is a success.
  keeper.settle(ok({}));
  assert.equal(keeper.idFor({ ...input, reason: 'Checked the runbook.' }), 'id-4');
});

test('only upstream_unavailable leaves the outcome unknown', () => {
  assert.equal(outcomeUnknown(refused('upstream_unavailable', 'x')), true);
  for (const code of ['not_authorized', 'rule_violation', 'validation', 'conflict', 'rate_limited']) {
    assert.equal(outcomeUnknown(refused(code, 'x')), false, code);
  }
  assert.equal(outcomeUnknown(ok({})), false);
});

test('a refusal names the rule, the guideline, the field and the correlation ID, and says nothing was written', () => {
  const answer = readEngineAnswer('answer_approval', refused('rule_violation', 'You proposed this decision, and the separation rule keeps your answer from counting.', {
    ruleKey: 'self-approval-tier-3',
    guideline: 'G17',
    field: 'approvalRecordId',
  }));
  assert.equal(answer.kind, 'refused');
  if (answer.kind !== 'refused') return;
  assert.equal(answer.title, 'A rule refused this');
  assert.equal(answer.ruleKey, 'self-approval-tier-3');
  assert.equal(answer.guideline, 'G17');
  assert.equal(answer.field, 'approvalRecordId');
  assert.equal(answer.correlationId, 'corr-refused');
  assert.equal(answer.outcomeUnknown, false);
});

test('a timeout says the outcome is unknown, except on a preview, which never writes', () => {
  const timeout = refused('upstream_unavailable', 'The product did not answer within 30 seconds.', {}, 'client-side');
  const write = readEngineAnswer('save_intake_draft', timeout);
  assert.equal(write.kind === 'refused' && write.outcomeUnknown, true);
  const preview = readEngineAnswer('save_intake_draft', timeout, true);
  assert.equal(preview.kind === 'refused' && preview.outcomeUnknown, false);
});

test('a submit names who is asked now, consultations included, and the stamp on the write', () => {
  const data: WriteToolData['submit_record'] = {
    ...stamp,
    subjectKind: 'intake-request',
    subjectId: '00000000-0000-4000-8000-000000000801',
    status: { label: 'Proposed', meaning: 'proposed' },
    decisionId: '00000000-0000-4000-8000-000000000901',
    approvalsRequested: [
      { approvalRecordId: 'a1', requiredRoleKey: 'platform-admin', approvalMode: 'first-to-respond', expiresOn: '2026-12-01T09:00:00Z', channelKey: 'teams', isConsultation: false },
      { approvalRecordId: 'a2', requiredRoleKey: 'architect', approvalMode: 'first-to-respond', expiresOn: null, channelKey: 'teams', isConsultation: true },
      { approvalRecordId: 'a3', requiredOwnerRoleKey: 'business-owner', approvalMode: 'everyone-must-approve', expiresOn: null, channelKey: 'teams', isConsultation: false },
    ],
    standardChangeApplied: null,
    engineAccepted: null,
  };
  const answer = readEngineAnswer('submit_record', ok(data, { warnings: [{ message: 'The window ends before it starts.' }], links: { recordUrl: 'https://product.example.org/app/records/decision/00000000-0000-4000-8000-000000000901' } }));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.askedNow.map(a => [a.by, a.key, a.isConsultation]), [
    ['role', 'platform-admin', false],
    ['role', 'architect', true],
    ['owner-role', 'business-owner', false],
  ]);
  assert.equal(answer.askedNow[0].expiresOn, '2026-12-01T09:00:00Z');
  assert.deepEqual(answer.askedLater, []);
  assert.equal(answer.stampedAs, 'Nadia Ferreira, through Web app, demo operator');
  assert.equal(answer.warnings.length, 1);
  assert.equal(answer.recordUrl, 'https://product.example.org/app/records/decision/00000000-0000-4000-8000-000000000901');
  assert.equal(answer.correlationId, 'corr-ok');
});

test('an ownership submit names the successor asked by name, and a proposal names the proposed owners', () => {
  const data: WriteToolData['submit_record'] = {
    ...stamp,
    subjectKind: 'decision',
    subjectId: '00000000-0000-4000-8000-000000000902',
    status: { label: 'Proposed', meaning: 'proposed' },
    decisionId: '00000000-0000-4000-8000-000000000902',
    approvalsRequested: [
      { approvalRecordId: 'a1', requiredOwnerRoleKey: 'sponsor', askedPrincipal: null, approvalMode: 'everyone-must-approve', expiresOn: null, channelKey: 'teams', isConsultation: false },
      { approvalRecordId: 'a2', requiredOwnerRoleKey: 'business-owner', askedPrincipal: { objectId: '00000000-0000-4000-8000-000000001101', displayLabel: 'Lena Okoro' }, approvalMode: 'everyone-must-approve', expiresOn: null, channelKey: 'teams', isConsultation: false },
    ],
    standardChangeApplied: null,
    engineAccepted: null,
  };
  const answer = readEngineAnswer('submit_record', ok(data));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.askedNow.map(a => [a.by, a.key, a.personName ?? null]), [
    ['owner-role', 'sponsor', null],
    ['person', 'business-owner', 'Lena Okoro'],
  ]);

  const proposal: WriteToolData['save_decision_proposal'] = {
    ...stamp,
    status: { label: 'Proposed', meaning: 'proposed' },
    decider: { accountableOwnerRoleKey: 'business-owner', principal: null },
    approvalsNeeded: [
      { approverOwnerRoleKey: 'business-owner', approvalMode: 'everyone-must-approve', minimumCount: 1 },
      { approverProposedOwners: true, approvalMode: 'everyone-must-approve', minimumCount: 1 },
    ],
    evidenceLinkIds: [],
  };
  const later = readEngineAnswer('save_decision_proposal', ok(proposal));
  assert.ok(later.kind === 'ok');
  if (later.kind !== 'ok') return;
  assert.deepEqual(later.askedLater.map(a => a.by), ['owner-role', 'proposed-owners']);
});

test('a proposal names who will be asked once it is submitted, and the decider', () => {
  const data: WriteToolData['save_decision_proposal'] = {
    ...stamp,
    status: { label: 'Proposed', meaning: 'proposed' },
    decider: { accountableOwnerRoleKey: 'business-owner', principal: { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' } },
    approvalsNeeded: [{ approverOwnerRoleKey: 'business-owner', approvalMode: 'first-to-respond', minimumCount: 1 }],
    evidenceLinkIds: [],
  };
  const answer = readEngineAnswer('save_decision_proposal', ok(data));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.askedLater.map(a => [a.by, a.key, a.minimumCount]), [['owner-role', 'business-owner', 1]]);
  // Nadia proposes and decides: the answer says so to her, never her own name as if someone else decided (QA walk).
  assert.ok(answer.facts.includes('You are the decider.'));
  assert.ok(!answer.facts.some(f => f.includes('The decider is Nadia Ferreira')));
  const other = readEngineAnswer('save_decision_proposal', ok({ ...data, decider: { accountableOwnerRoleKey: 'business-owner', principal: { objectId: '00000000-0000-4000-8000-000000000213', displayLabel: 'Rosa Tamaki' } } }));
  assert.ok(other.kind === 'ok' && other.facts.includes('The decider is Rosa Tamaki.'));
  assert.ok(answer.facts.some(f => f.startsWith('The proposal is saved.')));
  const preview = readEngineAnswer('save_decision_proposal', ok(data), true);
  assert.ok(preview.kind === 'ok' && !preview.facts.some(f => f.startsWith('The proposal is saved.')));
  assert.equal(preview.kind === 'ok' && preview.stampedAs, null);
});

test('an approval answer says whether it counts and where the decision stands', () => {
  const data: WriteToolData['answer_approval'] = {
    ...stamp,
    status: { label: 'Approved', meaning: 'approved' },
    approvalRecordId: 'a1',
    decisionId: 'd1',
    answer: 'approve',
    answeredAs: { roleKey: null, ownerRoleKey: 'business-owner' },
    isConsultation: false,
    countsTowardRequirement: true,
    decisionStatus: { label: 'Accepted', meaning: 'accepted' },
    openedApprovalRecordIds: [],
    cancelledApprovalRecordIds: ['a2'],
  };
  const answer = readEngineAnswer('answer_approval', ok(data));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.facts, [
    'Your answer counts toward the approval.',
    'The decision is now accepted.',
    '1 request that is no longer needed was canceled.',
  ]);
});

test('a check says what would happen, never that it happened, and drops the validateOnly warning', () => {
  const data: WriteToolData['answer_approval'] = {
    ...stamp,
    status: { label: 'Approved', meaning: 'approved' },
    approvalRecordId: 'a1',
    decisionId: 'd1',
    answer: 'approve',
    answeredAs: { roleKey: null, ownerRoleKey: 'business-owner' },
    isConsultation: false,
    countsTowardRequirement: true,
    decisionStatus: { label: 'Accepted', meaning: 'accepted' },
    openedApprovalRecordIds: ['a3'],
    cancelledApprovalRecordIds: [],
  };
  const envelope = ok(data, {
    summary: 'The decision is now accepted.',
    warnings: [{ code: 'request.validate-only', message: 'validateOnly was true: every rule ran and nothing was written.' }, { code: 'other', message: 'The window closes on 2027-01-15.' }],
  });
  const answer = readEngineAnswer('answer_approval', envelope, true);
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.equal(answer.summary, PREVIEW_SUMMARY);
  assert.deepEqual(answer.facts, [
    'Your answer would count toward the approval.',
    'The decision would be accepted.',
    'The next step would open 1 approval request.',
  ]);
  assert.deepEqual(answer.warnings.map(w => w.code), ['other']);
  assert.ok(!answer.warnings.some(w => w.message.includes('validateOnly')));
});

test('a stamp names on whose behalf only when that is someone else', () => {
  const self = { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' };
  const data = { ...stamp, performedBy: { ...stamp.performedBy, onBehalfOf: self } };
  const answer = readEngineAnswer('record_document_link', ok(data as unknown as WriteToolData['record_document_link']));
  assert.ok(answer.kind === 'ok' && answer.stampedAs === 'Nadia Ferreira, through Web app, demo operator');
  const forOther = { ...stamp, performedBy: { ...stamp.performedBy, actorKind: 'ai-client-for-person' as const, principal: { objectId: '00000000-0000-4000-8000-0000000000ac', displayLabel: 'Claude Code' }, onBehalfOf: self } };
  const ai = readEngineAnswer('record_document_link', ok(forOther as unknown as WriteToolData['record_document_link']));
  assert.ok(ai.kind === 'ok' && ai.stampedAs === 'Claude Code for Nadia Ferreira, through Web app, demo operator');
});

test('an owner leave counts the owner checks that cover it, new and already open, in plain grammar', () => {
  const data = {
    ...stamp,
    status: null,
    affected: [
      { businessApplicationId: 'x', ownerRoleKey: 'business-owner', hasDeputy: true },
      { businessApplicationId: 'y', ownerRoleKey: 'business-owner', hasDeputy: false },
    ],
    ownerCheckReviewIds: ['r1', 'r2', 'r3', 'r4', 'r5', 'r6'],
    reusedOwnerCheckReviewIds: ['r6'],
    pendingApprovals: 0,
  };
  const answer = readEngineAnswer('record_owner_leave', ok(data as unknown as WriteToolData['record_owner_leave']));
  assert.ok(answer.kind === 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.facts, [
    '1 of them has no deputy.',
    '6 owner check reviews cover this leave (5 new, 1 already open).',
  ]);
});

test('an engine sentence reads in plain words: labels for keys, no process codes', () => {
  const labels: Record<string, string> = { 'no-self-approval-tier-3': 'No self-approval at tier 3', accept: 'Accept' };
  const labelOf = (k: string) => labels[k] ?? null;
  assert.deepEqual(quotedKeys('Separation Rule `no-self-approval-tier-3` applies; link kind `sharepoint-page`.'), ['no-self-approval-tier-3', 'sharepoint-page']);
  assert.equal(
    plainEngineText('Separation Rule `no-self-approval-tier-3` refuses this answer.', labelOf),
    'Separation Rule "No self-approval at tier 3" refuses this answer.',
  );
  assert.equal(plainEngineText('Gate `accept` is not ready (OP-07), so its gate decision cannot be accepted.', labelOf), 'Gate "Accept" is not ready, so its gate decision cannot be accepted.');
  assert.equal(plainEngineText('The URL does not match the pattern of link kind `sharepoint-page`.', labelOf), 'The URL does not match the pattern of link kind "sharepoint page".');
});

test('an unexpected data shape shows the summary alone', () => {
  const answer = readEngineAnswer('submit_record', ok(null as unknown as WriteToolData['submit_record'], { summary: 'Submitted.' }));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.equal(answer.summary, 'Submitted.');
  assert.deepEqual(answer.facts, []);
  assert.equal(answer.stampedAs, null);
});

test('each write carries its retry key in the field its tool names', () => {
  assert.equal(retryKeyField('save_portfolio'), 'clientRequestId');
  assert.equal(retryKeyField('record_deployment'), 'idempotencyKey');
  assert.equal(retryKeyField('record_deployment_correction'), 'idempotencyKey');
  const retry = () => 'web-retry';
  const fresh = () => 'web-fresh';
  const input = { name: 'Field operations', clientRequestId: 'stale', idempotencyKey: 'stale', validateOnly: false };
  assert.deepEqual(writeBody('save_portfolio', input, false, retry, fresh), { name: 'Field operations', clientRequestId: 'web-retry' });
  assert.deepEqual(writeBody('save_portfolio', input, true, retry, fresh), { name: 'Field operations', validateOnly: true });
  const deployment = { artifactName: 'Crew planner', artifactVersion: '1.4.0', finishedOn: '2026-11-24T09:00:00Z', resultMeaning: 'succeeded' };
  assert.deepEqual(writeBody('record_deployment', deployment, false, retry, fresh), { ...deployment, idempotencyKey: 'web-retry' });
  // A check writes nothing, and the schema requires the key, so it gets a fresh one that matches no earlier record.
  assert.deepEqual(writeBody('record_deployment', deployment, true, retry, fresh), { ...deployment, validateOnly: true, idempotencyKey: 'web-fresh' });
  // The key a retry reuses depends on the payload only, never on the key fields.
  assert.equal(payloadKey({ ...deployment, idempotencyKey: 'a' }), payloadKey({ ...deployment, idempotencyKey: 'b' }));
});

test('a deployment recorded by hand reads its flags in words', () => {
  const data = {
    ...stamp,
    status: { label: 'Succeeded', meaning: 'succeeded' as const },
    deploymentRecordId: stamp.recordId,
    artifactVersionId: null,
    deploymentTargetId: null,
    changeRequestId: null,
    route: { key: 'person-in-app', meaning: 'person-in-app' as const },
    flags: { outsideWindow: false, otherPath: true, missingApproval: true, aiOnlyCodeApproval: false },
    gateLevelApplied: 'flag' as const,
  };
  const answer = readEngineAnswer('record_deployment', ok(data, { summary: 'Recorded.' }));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.facts, ['The engine flagged it: missing approval, came another way.', 'Gate level applied: flag.']);
});
