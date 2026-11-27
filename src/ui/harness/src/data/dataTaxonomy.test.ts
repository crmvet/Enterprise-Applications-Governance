// The data taxonomy in words, and how the engine's answer to a data write, the due-state refresh and a configuration
// change reads on the page.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { DataUseItem, Envelope, WriteToolData } from './contracts';
import {
  DATA_CHECK_CODES,
  DATA_CHECKS,
  DATA_USE_STATE_TEXT,
  DATA_USE_STATES,
  dataCheckOf,
  dataUseStateText,
  governingRuleText,
  inheritedText,
  needsAttention,
  ruleSourceText,
} from './dataTaxonomy';
import { readEngineAnswer, retryKeyField, writeBody } from './writes';

const PERSON = { objectId: '00000000-0000-4000-8000-000000000231', displayLabel: 'Hana Lindqvist' };

const stamp = {
  recordId: '00000000-0000-4000-8000-000000000c01',
  status: { label: 'Registered', meaning: 'registered' as const },
  performedBy: {
    actorKind: 'person' as const,
    principal: { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' },
    onBehalfOf: null,
    clientApp: { appId: '00000000-0000-4000-8000-0000000000ad', label: 'Web app' },
  },
  isAiCreated: false,
  engineStamp: { engineVersion: '1.0.0', engineCheckedOn: '2026-11-24T09:00:00Z' },
  created: true,
};

function ok<T>(data: T): Envelope<T> {
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
  } as Envelope<T>;
}

const use = (extra: Partial<DataUseItem> = {}): DataUseItem => ({
  dataUseId: '00000000-0000-4000-8000-000000000c01',
  recordNumber: 'DU-0007',
  businessApplicationId: '00000000-0000-4000-8000-000000000511',
  applicationName: 'Crew planner',
  domain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' },
  location: null,
  access: 'read',
  purpose: 'Shift costs',
  reachesAi: true,
  aiServiceAsSeen: null,
  isDeidentified: false,
  accessApproach: null,
  status: { label: 'Registered', meaning: 'registered' },
  state: 'proposed',
  approvalsNeeded: 2,
  approvalsGiven: 1,
  decisionId: '00000000-0000-4000-8000-000000000901',
  startOn: null,
  endedOn: null,
  isAiCreated: false,
  performedBy: null,
  recordUrl: 'https://product.example.org/app/records/data-use/00000000-0000-4000-8000-000000000c01',
  ...extra,
});

test('every data use state has words, and a proposed one counts its approvals', () => {
  for (const state of DATA_USE_STATES) assert.ok(DATA_USE_STATE_TEXT[state].length > 0, state);
  assert.equal(dataUseStateText('proposed', 1, 2), '1 of 2 approvals');
  assert.equal(dataUseStateText('proposed', 0, 1), '0 of 1 approval');
  assert.equal(dataUseStateText('proposed', 3, 2), '2 of 2 approvals');
  assert.equal(dataUseStateText('proposed', null, 2), 'Waiting for approval');
  assert.equal(dataUseStateText('breaks-rule'), 'Breaks a rule');
  assert.equal(needsAttention('needed'), true);
  assert.equal(needsAttention('no-one-to-ask'), true);
  assert.equal(needsAttention('allowed'), false);
  assert.equal(needsAttention('proposed'), false);
});

test('a rule says where it is set, and the governing rule reads as one sentence', () => {
  assert.equal(ruleSourceText({ setOn: { key: 'payroll', label: 'People / Payroll' }, fromClassification: null }), 'set on People / Payroll');
  assert.equal(ruleSourceText({ setOn: null, fromClassification: { key: 'restricted', label: 'Restricted' } }), 'from Restricted');
  assert.equal(ruleSourceText({ setOn: null, fromClassification: null }), 'the default');
  assert.equal(
    governingRuleText({ name: 'ai', value: 'never', setOn: { key: 'payroll', label: 'People / Payroll' }, fromClassification: null }),
    'The AI use rule is Never, set on People / Payroll.',
  );
});

test('the data checks are named, and an unknown check is null', () => {
  assert.equal(DATA_CHECK_CODES.length, 14);
  for (const code of DATA_CHECK_CODES) assert.ok(DATA_CHECKS[code].title, code);
  assert.equal(dataCheckOf('DC-07'), 'DC-07');
  assert.equal(dataCheckOf('DC-15'), null);
  assert.equal(inheritedText({ principal: PERSON, inheritedFrom: { key: 'people', label: 'People' } }), 'Hana Lindqvist, from People');
  assert.equal(inheritedText({ principal: PERSON, inheritedFrom: null }), 'Hana Lindqvist');
  assert.equal(inheritedText(null), 'No one');
});

test('a saved data use reads its rule, its state, the decision it opened and who is asked by name', () => {
  const data: WriteToolData['save_data_use'] = {
    ...stamp,
    dataUseId: stamp.recordId,
    recordNumber: 'DU-0007',
    replacedDataUseId: null,
    use: use(),
    governingRule: { name: 'ai', value: 'decision', setOn: { key: 'payroll', label: 'People / Payroll' }, fromClassification: null },
    decisionId: '00000000-0000-4000-8000-000000000901',
    decisionSubmitted: true,
    approvalsNeeded: 2,
    approvalsRequested: [
      { approvalRecordId: '00000000-0000-4000-8000-000000000e01', requiredRoleKey: null, requiredOwnerRoleKey: 'data-owner', askedPrincipal: PERSON, isConsultation: false },
      { approvalRecordId: '00000000-0000-4000-8000-000000000e02', requiredRoleKey: 'security-reviewer', requiredOwnerRoleKey: null, askedPrincipal: null, isConsultation: false },
    ],
  };
  const answer = readEngineAnswer('save_data_use', ok(data));
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.facts, [
    'The AI use rule is Decision, set on People / Payroll.',
    "The use's state: 1 of 2 approvals.",
    'A data-use decision was opened and sent to the people who answer it.',
  ]);
  assert.deepEqual(answer.askedNow.map(a => [a.by, a.key, a.personName ?? null]), [['person', 'data-owner', 'Hana Lindqvist'], ['role', 'security-reviewer', null]]);
});

test('a never rule refuses the save and names the rule, for a person as for an AI client', () => {
  const answer = readEngineAnswer('save_data_use', {
    ok: false,
    error: { code: 'rule_violation', message: 'The AI use rule is never, set on People / Payroll.', ruleKey: 'payroll' },
    correlationId: 'corr-never',
  });
  assert.equal(answer.kind, 'refused');
  if (answer.kind !== 'refused') return;
  assert.equal(answer.title, 'A rule refused this');
  assert.equal(answer.ruleKey, 'payroll');
});

test('moving references, saving locations and mappings, and the refresh read in words', () => {
  const moved = readEngineAnswer('move_data_references', ok({
    ...stamp,
    fromDomain: { key: 'hr', label: 'HR', path: 'HR' },
    toDomain: { key: 'people', label: 'People', path: 'People' },
    movedMappingIds: ['00000000-0000-4000-8000-000000000d01'],
    movedUseIds: ['00000000-0000-4000-8000-000000000c01', '00000000-0000-4000-8000-000000000c02'],
    changedUses: [{ dataUseId: '00000000-0000-4000-8000-000000000c01', stateBefore: 'allowed', stateAfter: 're-decide' }],
    ownerChange: null,
  } as WriteToolData['move_data_references']), true);
  assert.equal(moved.kind, 'ok');
  if (moved.kind === 'ok') assert.deepEqual(moved.facts, ['It would move 1 mapping and 2 uses from HR to People. Both domains have the same data owner, so each row would keep its identity and its decisions.', '1 use would need a decision again.']);

  // Wave 3 verification: with another data owner the decisions stay behind, and the check says before the move who would
  // be asked and which rows would be declared again; the result says what happened. Never "Their decisions stay with them".
  const ownerChange = { newDataOwner: { objectId: '00000000-0000-4000-8000-000000000a01', displayLabel: 'Fixture owner', principalKind: 'user' }, redeclaredCount: 1, reaskedCount: 1, openedCount: 2, withdrawnAnsweredCount: 1, withdrawnNoOwnerCount: 0, waitingForOwnerCount: 0 };
  const across = {
    ...stamp,
    fromDomain: { key: 'hr', label: 'HR', path: 'HR' },
    toDomain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' },
    movedMappingIds: [],
    movedUseIds: ['00000000-0000-4000-8000-000000000c01', '00000000-0000-4000-8000-000000000c02', '00000000-0000-4000-8000-000000000c03'],
    changedUses: [{ dataUseId: '00000000-0000-4000-8000-000000000c01', stateBefore: 'allowed', stateAfter: 'proposed' }],
    ownerChange,
  } as WriteToolData['move_data_references'];
  const check = readEngineAnswer('move_data_references', ok(across), true);
  if (check.kind === 'ok') {
    assert.deepEqual(check.facts, [
      'It would move 3 uses from HR to People / Payroll. The data owner changes, so the decisions would not move with the rows.',
      '1 decided row would be ended and declared again in People / Payroll. The accepted decision would stay with the ended row.',
      '1 decision someone has answered would be withdrawn.',
      '1 waiting decision would ask Fixture owner, from the start.',
      'The engine would open 2 new decisions for this move, asking Fixture owner.',
    ]);
  }
  const done = readEngineAnswer('move_data_references', ok(across));
  if (done.kind === 'ok') {
    assert.ok(done.facts.includes('The engine opened 2 new decisions for this move. They ask Fixture owner.'));
    assert.ok(!done.facts.some(f => f.includes('stay with them')));
  }

  const locations = readEngineAnswer('save_data_location', ok({ ...stamp, items: [], createdCount: 2, updatedCount: 1 } as WriteToolData['save_data_location']));
  if (locations.kind === 'ok') assert.deepEqual(locations.facts, ['2 locations added, 1 location updated.']);

  const counts = { reviewsOpened: 1, reviewsOverdue: 0, documentsStale: 2, decisionsRefreshed: 0, approvalsExpired: 0, approvalsCancelled: 0, approvalsOpened: 0, reminders: 3, escalations: 0, flagNotices: 0, failed: 0 };
  const refresh = readEngineAnswer('run_due_state_refresh', ok({ trigger: 'person', dryRun: false, counts, reviewsOpened: [], documentsStale: [], approvals: { decisions: 0, expiredRecordIds: [], cancelledRecordIds: [], openedRecordIds: [] }, notices: [], failed: [], more: false } as WriteToolData['run_due_state_refresh']));
  if (refresh.kind === 'ok') assert.deepEqual(refresh.facts, ['Done: 1 review opened, 2 documents turned stale, 3 reminders.']);
});

test('the due-state refresh sends no retry key; its check sends validateOnly only', () => {
  assert.equal(retryKeyField('run_due_state_refresh'), null);
  assert.equal(retryKeyField('save_data_use'), 'clientRequestId');
  const retry = () => 'web-retry';
  assert.deepEqual(writeBody('run_due_state_refresh', {}, false, retry), {});
  assert.deepEqual(writeBody('run_due_state_refresh', {}, true, retry), { validateOnly: true });
  assert.deepEqual(writeBody('save_data_use', { applicationId: 'a' }, false, retry), { applicationId: 'a', clientRequestId: 'web-retry' });
});

test('a configuration draft reads its rule errors, its impact and who approves it', () => {
  const data = {
    ...stamp,
    valid: false,
    errors: [{ entity: 'data-domain', key: 'payroll', ruleKey: 'V53', message: 'A subdomain is classified lower than its domain.' }],
    configurationDraftId: '00000000-0000-4000-8000-000000000f01',
    diffSummary: [{ entity: 'data-domain', key: 'payroll', op: 'updated' as const, before: {}, after: {} }],
    contentHash: null,
    impact: { applicationsAffected: 3, byChange: [], gateChanges: [], recordsInFlight: 0 },
    approvalsNeeded: [{ approverRoleKey: 'configuration-admin', approvalMode: 'first-to-respond' as const, minimumCount: 1 }],
    activation: 'Submit the draft. A person accepts the decision on its decision page. Nothing changes until then.',
  } as WriteToolData['propose_configuration_change'];
  const answer = readEngineAnswer('propose_configuration_change', ok(data), true);
  assert.equal(answer.kind, 'ok');
  if (answer.kind !== 'ok') return;
  assert.deepEqual(answer.facts, [
    'The draft breaks 1 rule of the configuration. It cannot be submitted until they are fixed.',
    '1 row changes.',
    '3 applications are affected.',
  ]);
  assert.deepEqual(answer.askedLater.map(a => a.key), ['configuration-admin']);
});
