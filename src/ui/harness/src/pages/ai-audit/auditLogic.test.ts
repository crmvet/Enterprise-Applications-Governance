// The audit trail and evidence pack logic: an entry in words, the page's own filters, and the configuration versions.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  actorKindLabel,
  approvalCountText,
  auditInput,
  datesReversed,
  entryHeadline,
  isIsoDate,
  parseOperations,
  scopeOnly,
  statusAfterText,
  toolDone,
  TOOLS_WITH_WORDS,
  versionNote,
  versionsText,
} from './auditLogic';

const ID = '00000000-0000-4000-8000-00000000d001';

test('a call that wrote reads as what it did; a tool not in the list shows its name', () => {
  assert.equal(entryHeadline({ operation: 'composed-call', what: 'Called save_intake_draft', tool: 'save_intake_draft', refusal: null }), 'Saved an intake draft');
  assert.equal(entryHeadline({ operation: 'composed-call', what: 'Called new_tool', tool: 'new_tool', refusal: null }), 'Called new_tool');
  assert.equal(toolDone('answer_approval'), 'Answered an approval request');
});

test('a refused call reads as what was tried, or as a tool the product does not have', () => {
  const refusal = { code: 'ai_cannot_do_this', ruleKey: null, reasonCode: null, ruleIds: ['G-08'], rule: 'AI never approves.', knownTool: true };
  assert.equal(entryHeadline({ operation: 'call-refused', what: 'Refused answer_approval', tool: 'answer_approval', refusal }), 'Refused the call to answer an approval request');
  assert.equal(
    entryHeadline({ operation: 'call-refused', what: 'Refused approve_decision', tool: 'approve_decision', refusal: { ...refusal, knownTool: false } }),
    'Refused a call to approve_decision, a tool the product does not have',
  );
  assert.equal(entryHeadline({ operation: 'decision-accepted', what: 'Decision accepted', tool: null, refusal: null }), 'Decision accepted');
});

test('the status a call left, in words', () => {
  assert.equal(statusAfterText('in-progress'), 'Left the record in progress');
  assert.equal(statusAfterText(null), null);
});

test('the operations of the address: known ones once each', () => {
  assert.deepEqual(parseOperations('?op=call-refused&op=nonsense&op=call-refused&op=decision-accepted'), ['call-refused', 'decision-accepted']);
  assert.equal(parseOperations('?op=nonsense'), undefined);
  assert.equal(parseOperations(''), undefined);
});

test('the tool input carries only the filters set; the figures keep the scope and drop actor and operation', () => {
  assert.deepEqual(auditInput({}, 50), { top: 50 });
  const f = { recordId: ID, actorKinds: ['person' as const], operations: ['call-refused' as const], from: '2026-11-01', to: '2026-11-30' };
  assert.deepEqual(auditInput(f, 1), { recordId: ID, actorKinds: ['person'], operations: ['call-refused'], from: '2026-11-01', to: '2026-11-30', top: 1 });
  assert.deepEqual(auditInput(scopeOnly(f), 1), { recordId: ID, from: '2026-11-01', to: '2026-11-30', top: 1 });
});

test('dates: real calendar dates only, and a reversed range is caught', () => {
  assert.equal(isIsoDate('2026-11-24'), true);
  assert.equal(isIsoDate('2026-02-30'), false);
  assert.equal(isIsoDate('24/11/2026'), false);
  assert.equal(isIsoDate(undefined), false);
  assert.equal(datesReversed('2026-11-30', '2026-11-01'), true);
  assert.equal(datesReversed('2026-11-01', undefined), false);
});

test('the actor kind: the configured label for the engine meaning, else the fixed words', () => {
  const rows = [{ key: 'ai-client', label: 'AI client for a person (configured)', fields: { engineMeaning: 'ai-client-for-person' } }];
  assert.equal(actorKindLabel('ai-client-for-person', rows), 'AI client for a person (configured)');
  assert.equal(actorKindLabel('pipeline', rows), 'Pipeline');
  assert.equal(actorKindLabel('something-new'), 'something-new');
});

test('a row checked under an older version says so next to the version in force', () => {
  assert.deepEqual(versionNote(2, 2), { text: 'Checked under version 2, in force now', older: false });
  assert.deepEqual(versionNote(1, 2), { text: 'Checked under version 1; version 2 is in force now', older: true });
  assert.equal(versionNote(null, 2).text, 'No version stamp (seeded data)');
});

test('the versions of the pack in words', () => {
  assert.equal(versionsText([], 3), 'No row in the pack carries a version stamp. Version 3 is in force now.');
  assert.equal(versionsText([3, 3], 3), 'Every stamped row was checked under version 3, the version in force now.');
  assert.equal(versionsText([1, 3, 2], 3), 'Rows were checked under versions 1, 2 and 3. Version 3 is in force now, so 2 older versions apply to some rows.');
  assert.equal(versionsText([1, 3], 3), 'Rows were checked under versions 1 and 3. Version 3 is in force now, so one older version applies to some rows.');
});

test('whether an approval answer counted, and why', () => {
  const base = { isConsultation: false, approverActorKind: 'person', outcome: { label: 'Approved', meaning: 'approved' as const }, countsTowardRequirement: true };
  assert.equal(approvalCountText(base).text, 'Counted toward the approval');
  assert.equal(approvalCountText({ ...base, isConsultation: true, countsTowardRequirement: false }).text, 'Advice only: a consultation never counts');
  assert.equal(approvalCountText({ ...base, approverActorKind: 'ai-client-for-person', countsTowardRequirement: false }).text, 'A recommendation only: an AI answer never counts');
  assert.equal(approvalCountText({ ...base, outcome: { label: 'Pending', meaning: 'pending' }, countsTowardRequirement: false }).text, 'Not answered yet');
  assert.equal(approvalCountText({ ...base, outcome: { label: 'Expired', meaning: 'expired' }, countsTowardRequirement: false }).text, 'Closed without an answer');
  assert.equal(approvalCountText({ ...base, countsTowardRequirement: false }).text, 'Not counted');
});

test('every write tool in tools.json reads as words on the audit trail', async () => {
  // @ts-expect-error node:fs has no type declarations here
  const fs = await import('node:fs');
  // @ts-expect-error node:path has no type declarations here
  const path = await import('node:path');
  const cwd = (globalThis as unknown as { process: { cwd(): string } }).process.cwd();
  const raw = JSON.parse(fs.readFileSync(path.resolve(cwd, '..', '..', 'mcp', 'contracts', 'tools.json'), 'utf8')) as { tools?: { name: string; access: string }[] } | { name: string; access: string }[];
  const tools = Array.isArray(raw) ? raw : raw.tools ?? [];
  const missing = tools.filter(t => t.access === 'write' && !TOOLS_WITH_WORDS.includes(t.name)).map(t => t.name);
  assert.deepEqual(missing, []);
});
