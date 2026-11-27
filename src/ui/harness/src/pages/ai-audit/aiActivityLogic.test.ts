// The AI activity page's logic: the scope it reads, the drafts sub-filters, the story figures, and an item in words.
// Fictional people and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { AiActivity, AiActivityItem, RecordRef } from '../../data/contracts';
import {
  aiStateText,
  answerHeadline,
  canAcceptHere,
  clientSentence,
  clientStanding,
  draftedLine,
  draftState,
  filterDrafts,
  foldDeploymentDrafts,
  activityText,
  isDraftedCodeApproval,
  itemKey,
  refusalHeadline,
  roleToolSummary,
  scopeInput,
  storySteps,
  submitKindsText,
  submittableEntity,
  toolPolicyKey,
  viewCount,
} from './aiActivityLogic';

const PRIYA = { objectId: '00000000-0000-4000-8000-000000000211', displayLabel: 'Priya Castell', principalKind: 'user' as const };
const NADIA = { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira', principalKind: 'user' as const };
const APP = '00000000-0000-4000-8000-00000000a001';
const IR = '00000000-0000-4000-8000-00000000b001';

function draft(over: Partial<AiActivityItem> = {}): AiActivityItem {
  return {
    kind: 'draft',
    occurredOn: '2026-11-24T08:00:00Z',
    clientApp: 'Claude Code',
    onBehalfOf: PRIYA,
    application: null,
    record: {
      entity: 'intake-request',
      recordId: IR,
      recordNumber: 'IR-0108',
      label: 'IR-0108: Crew overtime tracker',
      status: { label: 'Draft', meaning: 'draft' },
      recordUrl: `https://product.example.org/app/records/intake-request/${IR}`,
    },
    state: 'waiting',
    takenForward: null,
    editedByPersonFirst: false,
    withdrawn: null,
    decided: null,
    refusal: null,
    answer: null,
    deployment: null,
    hasCallHistory: true,
    ...over,
  };
}

const COUNTS: AiActivity['counts'] = { drafts: 5, waiting: 2, takenForwardByPerson: 2, submittedByAi: 1, editedByPersonFirst: 1, withdrawn: 0, refused: 3, answers: 1, codeApprovals: 1 };

test('the scope: mine keeps the reader only and drops a person filter; the rest pass as they are', () => {
  assert.deepEqual(scopeInput({}), {});
  assert.deepEqual(scopeInput({ mine: true, person: PRIYA.objectId, client: 'Claude', applicationId: APP }), { mineOnly: true, clientApp: 'Claude', businessApplicationId: APP });
  assert.deepEqual(scopeInput({ person: PRIYA.objectId }), { onBehalfOfObjectId: PRIYA.objectId });
});

test('the drafts sub-filters keep the drafts a person changed first, or the ones the AI client submitted itself', () => {
  const edited = draft({ editedByPersonFirst: true, state: 'submitted' });
  const byAi = draft({ state: 'submitted', takenForward: { actorKind: 'ai-client-for-person', principal: PRIYA, on: '2026-11-24T08:05:00Z', fromHistory: true } });
  const byPerson = draft({ state: 'submitted', takenForward: { actorKind: 'person', principal: PRIYA, on: '2026-11-24T09:00:00Z', fromHistory: true } });
  const unknown = draft({ editedByPersonFirst: null });
  const all = [edited, byAi, byPerson, unknown];
  assert.deepEqual(filterDrafts(all, undefined), all);
  assert.deepEqual(filterDrafts(all, 'edited'), [edited]);
  assert.deepEqual(filterDrafts(all, 'ai-submitted'), [byAi]);
});

test('the story: three steps, each figure opening the view it counts', () => {
  const steps = storySteps(COUNTS);
  assert.deepEqual(steps.map(s => s.lead.value), [5, 2, 3]);
  assert.deepEqual(steps[0].lead.target, { view: 'drafts' });
  assert.deepEqual(steps[1].more.map(f => f.target), [{ view: 'drafts', only: 'edited' }, { view: 'drafts', only: 'ai-submitted' }]);
  assert.equal(steps[2].lead.attention, true);
  assert.equal(steps[0].more.find(f => f.key === 'withdrawn')?.attention, undefined);
  assert.equal(steps[0].more.find(f => f.key === 'waiting')?.attention, true);
});

test('a tab counts what its view lists; everything is drafts, refusals, answers and code approvals', () => {
  assert.equal(viewCount(COUNTS, 'all'), 10);
  assert.equal(viewCount(COUNTS, 'accepted'), 2);
  assert.equal(viewCount(COUNTS, 'code-approvals'), 1);
});

test('a waiting draft speaks to the person it was drafted for, and names them to anyone else', () => {
  assert.match(draftState(draft(), PRIYA.objectId).detail ?? '', /^Waiting for you/);
  assert.match(draftState(draft(), NADIA.objectId).detail ?? '', /Waiting for Priya Castell/);
  assert.equal(draftState(draft(), null).tone, 'warning');
});

test('a draft taken forward names who took it forward, from the history or from the last stamp', () => {
  const fromHistory = draftState(draft({ state: 'submitted', takenForward: { actorKind: 'person', principal: PRIYA, on: '2026-11-24T09:00:00Z', fromHistory: true } }), null);
  assert.equal(fromHistory.label, 'Taken forward by a person');
  assert.equal(fromHistory.tone, 'success');
  assert.match(fromHistory.detail ?? '', /Taken forward by Priya Castell, 24 Nov 2026, 09:00 UTC\./);
  const fromStamp = draftState(draft({ state: 'submitted', takenForward: { actorKind: 'person', principal: PRIYA, on: null, fromHistory: false }, editedByPersonFirst: null, hasCallHistory: false }), null);
  assert.match(fromStamp.detail ?? '', /from the last stamp/);
  assert.match(fromStamp.note ?? '', /No call history/);
});

test('a draft the AI client submitted itself says people still decide; a decided one names the outcome', () => {
  const byAi = draftState(draft({ state: 'submitted', takenForward: { actorKind: 'ai-client-for-person', principal: PRIYA, on: null, fromHistory: true } }), null);
  assert.equal(byAi.label, 'Submitted by the AI client');
  assert.match(byAi.detail ?? '', /People still decide/);
  const decided = draftState(draft({
    state: 'decided',
    takenForward: { actorKind: 'person', principal: PRIYA, on: null, fromHistory: true },
    decided: { principal: NADIA, on: '2026-11-25T10:00:00Z', outcome: { key: 'accept', label: 'Accept' } },
    editedByPersonFirst: true,
  }), null);
  assert.match(decided.detail ?? '', /changed it before it went forward/);
  assert.match(decided.detail ?? '', /Decided: Accept by Nadia Ferreira/);
});

test('withdrawn and recorded drafts', () => {
  const withdrawn = draftState(draft({ state: 'withdrawn', withdrawn: { principal: PRIYA, on: null } }), null);
  assert.equal(withdrawn.detail, 'Withdrawn by Priya Castell.');
  assert.equal(draftState(draft({ state: 'recorded' }), null).label, 'Recorded');
});

test('a refused call to a tool the product does not have says so', () => {
  const refusal = draft({
    kind: 'refusal',
    record: null,
    state: null,
    refusal: { code: 'not_found', ruleKey: null, reasonCode: null, ruleIds: [], rule: 'No tool has this name.', knownTool: false, tool: 'approve_decision', auditEntryId: '00000000-0000-4000-8000-00000000c001' },
  });
  assert.equal(refusalHeadline(refusal), 'Claude Code called approve_decision, a tool the product does not have');
  assert.equal(draftedLine(refusal), 'Called through Claude Code for Priya Castell, 24 Nov 2026, 08:00 UTC');
  assert.equal(itemKey(refusal), 'refusal-00000000-0000-4000-8000-00000000c001');
});

test('an AI answer names the approver and the outcome', () => {
  const answer = draft({ kind: 'answer', state: null, answer: { approvalRecordId: 'x', approver: null, approverActorKind: 'ai-client-for-person', outcome: { label: 'Recommendation only', meaning: 'recommendation-only' }, countsTowardRequirement: false } });
  assert.equal(answerHeadline(answer), 'Claude Code answered "Recommendation only"');
});

test('lines without a client or a time still read well', () => {
  assert.equal(draftedLine(draft({ clientApp: null, occurredOn: null, onBehalfOf: null })), 'Drafted by an AI client');
  assert.equal(draftedLine(draft({ kind: 'code-approval', clientApp: null, occurredOn: null })), 'Deployed');
});

test('a deployment an AI client recorded with an AI-only code approval is one row that says who recorded it (B24-17)', () => {
  assert.equal(draftedLine(draft({ kind: 'code-approval', state: 'recorded', occurredOn: null })), 'Recorded by Claude Code for Priya Castell');
});

test('accept and submit shows only on a waiting draft of a submittable kind made for the reader who may submit', () => {
  assert.equal(canAcceptHere(draft(), PRIYA.objectId, true), true);
  assert.equal(canAcceptHere(draft(), PRIYA.objectId.toUpperCase(), true), true);
  assert.equal(canAcceptHere(draft(), NADIA.objectId, true), false);
  assert.equal(canAcceptHere(draft(), PRIYA.objectId, false), false);
  assert.equal(canAcceptHere(draft({ state: 'submitted' }), PRIYA.objectId, true), false);
  assert.equal(canAcceptHere(draft({ record: { ...draft().record!, entity: 'document-link' } }), PRIYA.objectId, true), false);
  assert.equal(submittableEntity('decision'), 'decision');
  assert.equal(submittableEntity('configuration-draft'), null);
});

// ---------- the clients view and the Tool Policy panel ----------


test('a client stands allowed, blocked, or off the Known Client list', () => {
  assert.deepEqual(clientStanding({ knownClientKey: 'claude-code', isAllowed: true }), { text: 'Allowed', tone: 'success' });
  assert.deepEqual(clientStanding({ knownClientKey: 'claude-code', isAllowed: false }), { text: 'Blocked', tone: 'danger' });
  assert.equal(clientStanding({ knownClientKey: null, isAllowed: null }).text, 'Not on the Known Client list');
});

test('a client in a sentence', () => {
  assert.equal(
    clientSentence({ calls: 12, drafts: 5, acceptedByPerson: 3, waiting: 2, refused: 1 }),
    '12 calls recorded. 5 drafts: 3 taken forward by a person, 2 waiting. 1 refused.',
  );
  assert.equal(clientSentence({ calls: 1, drafts: 0, acceptedByPerson: 0, waiting: 0, refused: 0 }), '1 call recorded. No drafts. None refused.');
});

test('how far an AI client may go, in words', () => {
  assert.equal(aiStateText('proposed', false), 'Proposes only: people decide');
  assert.equal(aiStateText('draft', true), 'People only: no AI client may call it');
  assert.equal(aiStateText('something-new', false), 'something new');
  assert.equal(submitKindsText(['intake-request', 'decision']), 'an intake request, a decision');
  assert.equal(submitKindsText([]), null);
  assert.equal(toolPolicyKey('save_intake_draft'), 'save-intake-draft');
});

test('a role\'s tools: the writes in order, the reads counted', () => {
  const s = roleToolSummary([
    { tool: 'submit_record', number: 13, access: 'write' as const },
    { tool: 'whoami', number: 1, access: 'read' as const },
    { tool: 'save_intake_draft', number: 9, access: 'write' as const },
  ]);
  assert.deepEqual(s.writes.map(t => t.tool), ['save_intake_draft', 'submit_record']);
  assert.equal(s.reads, 1);
});

test('one deployment an AI client drafted and started shows once, as its code approval row with the draft line', () => {
  const DEP = '00000000-0000-4000-8000-00000000d001';
  const record: RecordRef = { entity: 'deployment-record', recordId: DEP, recordNumber: null, label: 'Deployment of route-planner 2.0.0-rc.1', status: { label: 'Completed', meaning: 'completed' }, recordUrl: `https://product.example.org/app/records/deployment-record/${DEP}` };
  const drafted = draft({ record, occurredOn: '2026-11-24T14:40:00Z', state: 'recorded' });
  const started = draft({
    kind: 'code-approval',
    record: { ...record, recordId: DEP.toUpperCase() },
    occurredOn: '2026-11-24T11:40:00Z',
    state: null,
    deployment: { deploymentRecordId: DEP, route: { key: 'pipeline-step', meaning: 'pipeline-step' }, flags: { outsideWindow: false, otherPath: false, missingApproval: false, aiOnlyCodeApproval: true }, gateLevelApplied: 'flag' },
  });
  const other = draft();
  const { rows, folded } = foldDeploymentDrafts([drafted, other, started]);
  assert.equal(folded, 1);
  assert.deepEqual(rows.map(r => r.item.kind), ['draft', 'code-approval']);
  assert.equal(rows[0].item, other);
  assert.equal(rows[1].draft, drafted);
  // A draft with no code approval of the same deployment stays a row of its own.
  assert.equal(foldDeploymentDrafts([drafted, other]).folded, 0);
  assert.ok(activityText(other).some(t => t?.includes('Crew overtime tracker')));
});

test('the Everything tab counts each activity once, from counts.everything; a server without it gives the sum', () => {
  // Priya's scope after the story: 8 drafts, one of them a deployment an AI client drafted and started, which is also
  // the one code approval. The sum counts it twice; everything counts it once.
  const counts: AiActivity['counts'] = { ...COUNTS, drafts: 8, codeApprovals: 1, refused: 3, answers: 1, everything: 12 };
  assert.equal(viewCount(counts, 'all'), 12);
  assert.equal(viewCount({ ...counts, everything: undefined }, 'all'), 13);
  assert.equal(viewCount({ ...counts, everything: 0 }, 'all'), 0);
  assert.equal(viewCount(counts, 'drafts'), 8);
});

test('in the drafts view a drafted deployment reads as a draft that was then recorded', () => {
  const DEP = '00000000-0000-4000-8000-00000000d002';
  const record: RecordRef = { entity: 'deployment-record', recordId: DEP, recordNumber: null, label: 'Deployment of route-planner 2.0.0', status: { label: 'Completed', meaning: 'completed' }, recordUrl: `https://product.example.org/app/records/deployment-record/${DEP}` };
  const drafted = draft({ kind: 'code-approval', record, state: 'recorded', occurredOn: null });
  assert.equal(isDraftedCodeApproval(drafted, 'drafts'), true);
  assert.equal(draftedLine(drafted, 'drafts'), 'Drafted by Claude Code for Priya Castell, then recorded');
  // Elsewhere it keeps its own line, and a code approval no AI client drafted (state null) is not a draft.
  assert.equal(isDraftedCodeApproval(drafted, 'code-approvals'), false);
  assert.equal(draftedLine(drafted, 'code-approvals'), 'Recorded by Claude Code for Priya Castell');
  assert.equal(draftedLine(drafted), 'Recorded by Claude Code for Priya Castell');
  const started = draft({ kind: 'code-approval', record, state: null, occurredOn: null });
  assert.equal(isDraftedCodeApproval(started, 'drafts'), false);
  assert.equal(draftedLine(started, 'drafts'), 'Started by Claude Code for Priya Castell');
});

test('the scope passes the portfolio the Overview tile opens with', () => {
  const PORTFOLIO = '00000000-0000-4000-8000-000000000a31';
  assert.deepEqual(scopeInput({ portfolioId: PORTFOLIO }), { portfolioId: PORTFOLIO });
  assert.deepEqual(scopeInput({ portfolioId: PORTFOLIO, applicationId: APP }), { businessApplicationId: APP, portfolioId: PORTFOLIO });
});
