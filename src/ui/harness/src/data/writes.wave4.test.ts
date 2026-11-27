// The engine's answer to the wave 4 writes, in words: "Ask again" (renew_approval_request), "Send again"
// (retry_approval_card), the import of data domains (import_data_taxonomy), and a configuration proposal's data uses.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { DataUseImpactItem, Envelope, WriteToolData } from './contracts';
import { dataUseImpactFacts, importFacts, readEngineAnswer, retryKeyField } from './writes';

const person = {
  actorKind: 'person' as const,
  principal: { objectId: '00000000-0000-4000-8000-000000000214', displayLabel: 'Owen Mbeki' },
  onBehalfOf: null,
  clientApp: { appId: '00000000-0000-4000-8000-0000000000ad', label: 'Web app' },
};

function ok<T>(data: T): Envelope<T> {
  return {
    ok: true,
    summary: 'Done.',
    data,
    asOf: '2026-11-25T09:00:00Z',
    configurationSetVersion: 3,
    couldNotRead: [],
    warnings: [],
    untrustedFields: [],
    nextSteps: [],
    correlationId: 'corr-ok',
  } as Envelope<T>;
}

const decision = {
  decisionId: '00000000-0000-4000-8000-000000004000',
  recordNumber: 'DEC-0101',
  statement: 'Inspection Scheduler goes live',
  decisionType: { key: 'go-live', label: 'Go-live' },
  status: { label: 'Proposed', meaning: 'proposed' as const },
  recordUrl: 'https://product.example.org/app/records/decision/00000000-0000-4000-8000-000000004000',
};

test('each new write takes its retry key in clientRequestId', () => {
  for (const tool of ['renew_approval_request', 'retry_approval_card', 'import_data_taxonomy'] as const) assert.equal(retryKeyField(tool), 'clientRequestId', tool);
});

test('"Ask again" names who is asked and until when', () => {
  const data: WriteToolData['renew_approval_request'] = {
    approvalRecordId: '00000000-0000-4000-8000-000000009001',
    renewed: true,
    previousOutcome: 'pending',
    decision,
    request: {
      approvalRecordId: '00000000-0000-4000-8000-000000009002',
      askedRole: null,
      askedOwnerRole: { key: 'business-owner', label: 'Business owner' },
      askedPerson: null,
      isConsultation: false,
      requestedOn: '2026-11-25T09:00:00Z',
      expiresOn: '2026-11-30T09:00:00Z',
    },
    openedRecordIds: ['00000000-0000-4000-8000-000000009002'],
    status: decision.status,
    performedBy: person,
    dryRun: false,
  };
  const answer = readEngineAnswer('renew_approval_request', ok(data));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.askedNow.map(a => [a.by, a.key, a.expiresOn]), [['owner-role', 'business-owner', '2026-11-30T09:00:00Z']]);
  assert.equal(answer.facts[0], 'The old request is closed, and the same people are asked again with new time to answer.');
  // The expiry is in the engine's summary and the Asked now block, never a third time as a fact (wave 4b verify).
  assert.equal(answer.facts.length, 1);
  const again = readEngineAnswer('renew_approval_request', ok({ ...data, renewed: false }));
  assert.ok(again.kind === 'ok' && again.facts[0].startsWith('The request was already asked again'));
  const check = readEngineAnswer('renew_approval_request', ok({ ...data, dryRun: true }), true);
  assert.ok(check.kind === 'ok' && check.facts[0].startsWith('The old request would close'));
});

test('"Send again" puts the card back, or closes one whose request settled', () => {
  const base: WriteToolData['retry_approval_card'] = {
    approvalRecordId: '00000000-0000-4000-8000-000000009001',
    decision: { decisionId: decision.decisionId, recordNumber: 'DEC-0101', statement: decision.statement, recordUrl: decision.recordUrl },
    action: 'sent-again',
    stateBefore: 'failed',
    stateAfter: 'pending',
    attemptsBefore: 5,
    performedBy: person,
    dryRun: false,
  };
  const sent = readEngineAnswer('retry_approval_card', ok(base));
  assert.ok(sent.kind === 'ok');
  if (sent.kind !== 'ok') return;
  assert.deepEqual(sent.facts, ['The card goes back to the approval runner with no attempts used. It had used 5 attempts.']);
  const closed = readEngineAnswer('retry_approval_card', ok({ ...base, action: 'closed' as const, stateBefore: 'sent' as const, stateAfter: 'cancelled' as const }));
  assert.ok(closed.kind === 'ok' && closed.facts[0] === 'The card is closed: its request was canceled in the meantime.');
});

test('an import says what the draft changes, what the file leaves out, and that owners change only through an owner change', () => {
  const data = {
    recordId: '00000000-0000-4000-8000-0000000000c3',
    status: { label: 'Draft', meaning: 'draft' as const },
    performedBy: person,
    isAiCreated: false,
    engineStamp: { engineVersion: '1.0.0', engineCheckedOn: '2026-11-25T09:00:00Z' },
    created: true,
    configurationDraftId: '00000000-0000-4000-8000-0000000000c3',
    valid: true,
    rowsRead: 12,
    counts: { domainsAdded: 3, domainsChanged: 2, domainsUnchanged: 7, matchesAdded: 0, notInSource: 1 },
    problems: [],
    errors: [],
    diffSummary: [],
    notInSource: [{ key: 'legacy-hr', path: 'People / Legacy HR' }],
    preparedOwnerChanges: [],
    catalogOwnerDifferences: [],
  } as WriteToolData['import_data_taxonomy'];
  assert.deepEqual(importFacts(data, false), [
    '12 rows read.',
    'The draft adds 3 domains, changes 2 domains.',
    '7 domains are already as the file says.',
    '1 domain in the configuration is not in the file. The import never retires a domain.',
    'Nothing changes until the draft is submitted and a person accepts the decision.',
  ]);
  assert.equal(importFacts(data, true)[1], 'The draft would add 3 domains, change 2 domains.');
});

test("a configuration proposal's preview counts the data uses it would move", () => {
  const item = (kind: DataUseImpactItem['kind'], breaksRule: boolean, needsNewDecision: boolean): DataUseImpactItem => ({
    kind,
    id: '00000000-0000-4000-8000-000000006200',
    recordNumber: null,
    application: null,
    domain: { key: 'payroll', path: 'People / Payroll' },
    stateBefore: 'allowed',
    stateAfter: breaksRule ? 'breaks-rule' : 're-decide',
    breaksRule,
    needsNewDecision,
    recordUrl: 'https://product.example.org/app/records/data-use/00000000-0000-4000-8000-000000006200',
  });
  assert.deepEqual(dataUseImpactFacts([]), []);
  assert.deepEqual(dataUseImpactFacts([item('data-use', false, true), item('data-use', true, false), item('data-mapping', false, true)]), [
    '2 data uses and 1 copy would change state when the change takes effect.',
    '1 row would break a rule.',
    '2 rows would need a new decision.',
  ]);
});
