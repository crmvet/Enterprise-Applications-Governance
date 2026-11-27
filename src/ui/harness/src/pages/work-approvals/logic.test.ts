// My work, Approvals and the decision page: the words they put on the engine's answers, and how the queue splits.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ApprovalItem, DecisionApprovalRecord, DecisionDetail, QueueItem, StampedBy } from '../../data/contracts';
import {
  ageText,
  answerableTotal,
  approvalRuleText,
  askedOfText,
  askedUnderText,
  cancelReasonText,
  countExplained,
  decisionHistory,
  expiryText,
  groupQueue,
  keptFromAnswering,
  matchesText,
  notSubmitted,
  optionsWeighed,
  ownerSubjectText,
  proposedByMe,
  recordOutcomeText,
  requestFacts,
  separationRuleText,
  splitKept,
  splitMine,
  submittableKind,
  type RuleLabels,
} from './logic';

const NOW = Date.parse('2026-11-24T12:00:00Z');
const priya = { objectId: '00000000-0000-4000-8000-000000000211', displayLabel: 'Priya Castell' };
const nadia = { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' };
const claude = { objectId: '00000000-0000-4000-8000-0000000000c1', displayLabel: 'Claude Code' };
const byAi: StampedBy = { actorKind: 'ai-client-for-person', principal: claude, onBehalfOf: priya, clientAppLabel: 'Claude Code', stampedOn: '2026-11-24T11:00:00Z' };
const byPerson: StampedBy = { actorKind: 'person', principal: priya, onBehalfOf: null, clientAppLabel: 'Web app', stampedOn: '2026-11-24T10:00:00Z' };

const labels: RuleLabels & { decisionType(key: string): string } = {
  role: k => ({ 'change-manager': 'Change manager' })[k] ?? k,
  ownerRole: k => ({ 'business-owner': 'Business owner' })[k] ?? k,
  tier: k => ({ 'tier-3': 'Tier 3' })[k] ?? k,
  changeType: k => ({ emergency: 'Emergency' })[k] ?? k,
  applicationKind: k => k,
  lifecycleStage: k => k,
  targetStage: k => ({ production: 'Production' })[k] ?? k,
  decisionType: k => ({ 'change-authorization': 'Change authorization' })[k] ?? k,
};

function queueItem(over: Partial<QueueItem>): QueueItem {
  return {
    kind: 'draft',
    subjectKind: 'intake-request',
    subjectId: '10000000-0000-4000-8000-000000000001',
    title: 'Crew overtime tracker',
    businessApplicationId: null,
    dueOn: null,
    status: { label: 'Draft', meaning: 'draft' },
    why: 'Your draft is not submitted yet.',
    recordUrl: 'https://example.invalid/app/records/intake-request/10000000-0000-4000-8000-000000000001',
    nextTool: 'submit_record',
    isAiCreated: false,
    performedBy: null,
    ...over,
  };
}

function approvalItem(over: Partial<ApprovalItem>): ApprovalItem {
  return {
    approvalRecordId: '20000000-0000-4000-8000-000000000001',
    decision: {
      decisionId: '30000000-0000-4000-8000-000000000001',
      decisionType: { key: 'change-authorization', label: 'Change authorization' },
      statement: 'Authorize the export flow change',
      status: { label: 'Proposed', meaning: 'proposed' },
      proposedBy: priya,
      isAiCreated: false,
    },
    subject: { subjectKind: 'change-request', subjectId: '40000000-0000-4000-8000-000000000001', label: 'Retire the old export flow', businessApplicationId: null, applicationName: 'Inspection Scheduler' },
    approvalRuleKey: 'change-auth-owner',
    isConsultation: false,
    askedRole: null,
    askedOwnerRole: { key: 'business-owner', label: 'Business owner' },
    askedPrincipal: null,
    outcome: { label: 'Pending', meaning: 'pending' },
    approver: null,
    typedReason: null,
    countsTowardRequirement: false,
    requestedOn: '2026-11-22T12:00:00Z',
    respondedOn: null,
    expiresOn: '2026-11-25T18:00:00Z',
    otherPendingCount: 1,
    isOwnProposal: false,
    recordUrl: 'https://example.invalid/app/records/decision/30000000-0000-4000-8000-000000000001',
    nextTool: 'answer_approval',
    ...over,
  };
}

function record(over: Partial<DecisionApprovalRecord>): DecisionApprovalRecord {
  return {
    approvalRecordId: '20000000-0000-4000-8000-000000000001',
    approvalRuleKey: 'change-auth-owner',
    approver: null,
    approverActorKind: null,
    approverPrincipalKind: null,
    outcome: { label: 'Pending', meaning: 'pending' },
    typedReason: null,
    respondedOn: null,
    countsTowardRequirement: false,
    refusedBySeparationRuleKey: null,
    askedRole: null,
    askedOwnerRole: { key: 'business-owner', label: 'Business owner' },
    askedPrincipal: null,
    isConsultation: false,
    requestedOn: '2026-11-22T12:00:00Z',
    expiresOn: '2026-11-27T12:00:00Z',
    ...over,
  };
}

function detail(over: { records?: DecisionApprovalRecord[]; status?: DecisionDetail['decision']['status']; decidedOn?: string | null; proposedBy?: typeof priya | null; performedBy?: StampedBy | null; isAiCreated?: boolean }): DecisionDetail {
  return {
    decision: {
      decisionId: '30000000-0000-4000-8000-000000000001',
      recordNumber: 'DEC-0112',
      decisionType: { key: 'change-authorization', label: 'Change authorization' },
      statement: 'Authorize the export flow change',
      context: null,
      optionsConsidered: null,
      confidence: null,
      outcome: null,
      reason: null,
      status: over.status ?? { label: 'Proposed', meaning: 'proposed' },
      decider: null,
      decidedOn: over.decidedOn ?? null,
      supersedes: null,
      supersededBy: null,
      amends: null,
      amendedBy: [],
      configurationSetVersion: 7,
      proposedBy: over.proposedBy === undefined ? priya : over.proposedBy,
      isAiCreated: over.isAiCreated ?? false,
      performedBy: over.performedBy ?? null,
      recordUrl: 'https://example.invalid/app/records/decision/30000000-0000-4000-8000-000000000001',
    },
    subject: { subjectKind: 'change-request', subjectId: '40000000-0000-4000-8000-000000000001', label: 'Retire the old export flow', businessApplicationId: null, applicationName: null, recordUrl: 'https://example.invalid/app/records/change-request/40000000-0000-4000-8000-000000000001' },
    approvals: { decisionId: '30000000-0000-4000-8000-000000000001', evaluated: true, required: [], records: over.records ?? [], missing: [], ready: false },
    evidence: [],
  };
}

test('ages and expiries read in plain words, and an unreadable time gives nothing', () => {
  assert.equal(ageText('2026-11-24T11:59:40Z', NOW), 'just now');
  assert.equal(ageText('2026-11-24T11:55:00Z', NOW), '5 minutes ago');
  assert.equal(ageText('2026-11-24T11:00:00Z', NOW), '1 hour ago');
  assert.equal(ageText('2026-11-21T12:00:00Z', NOW), '3 days ago');
  assert.equal(ageText(null, NOW), null);
  assert.equal(ageText('not a date', NOW), null);
  assert.deepEqual(expiryText('2026-11-24T11:00:00Z', NOW), { text: 'Expired', state: 'expired' });
  assert.deepEqual(expiryText('2026-11-24T17:00:00Z', NOW), { text: 'Expires in 5 hours', state: 'soon' });
  assert.deepEqual(expiryText('2026-11-26T13:00:00Z', NOW), { text: 'Expires in 2 days', state: 'soon' });
  assert.deepEqual(expiryText('2026-12-01T12:00:00Z', NOW), { text: 'Expires in 7 days', state: 'later' });
  assert.equal(expiryText(null, NOW), null);
});

test('the queue splits AI drafts from the person\'s own drafts, by the engine\'s flag or its stamp', () => {
  const groups = groupQueue([
    queueItem({ isAiCreated: true, performedBy: byAi }),
    queueItem({ isAiCreated: false, performedBy: byAi, subjectId: '10000000-0000-4000-8000-000000000002' }),
    queueItem({ performedBy: byPerson, subjectId: '10000000-0000-4000-8000-000000000003' }),
    queueItem({ kind: 'review', subjectKind: 'review', nextTool: 'save_decision_proposal' }),
    queueItem({ kind: 'owner-gap', subjectKind: 'business-application', nextTool: 'propose_owner_change' }),
    queueItem({ kind: 'approval', subjectKind: 'decision', nextTool: 'answer_approval' }),
  ]);
  assert.equal(groups['ai-drafts'].length, 2);
  assert.equal(groups.drafts.length, 1);
  assert.equal(groups.reviews.length, 1);
  assert.equal(groups['owner-gaps'].length, 1);
  assert.equal(groups.documents.length + groups.claims.length, 0);
});

test('only an intake, change or decision draft whose next tool is submit_record can be submitted from My work', () => {
  assert.equal(submittableKind(queueItem({})), 'intake-request');
  assert.equal(submittableKind(queueItem({ subjectKind: 'decision' })), 'decision');
  assert.equal(submittableKind(queueItem({ subjectKind: 'business-application', nextTool: 'save_application_draft' })), null);
  assert.equal(submittableKind(queueItem({ kind: 'review', subjectKind: 'review' })), null);
});

test('who a request asks: a named person, a role, or an owner role of the application', () => {
  assert.equal(askedOfText({ askedRole: null, askedOwnerRole: { key: 'business-owner', label: 'Business owner' }, askedPrincipal: null }), 'Business owner of the application');
  assert.equal(askedOfText({ askedRole: { key: 'change-manager', label: 'Change manager' }, askedOwnerRole: null, askedPrincipal: null }), 'Change manager');
  assert.equal(askedOfText({ askedRole: { key: 'change-manager', label: 'Change manager' }, askedOwnerRole: null, askedPrincipal: nadia }), 'Nadia Ferreira');
  assert.equal(askedOfText({ askedRole: null, askedOwnerRole: null, askedPrincipal: null }), 'The accountable holder');
});

test('a data decision names the domain or the location an owner role is asked on', () => {
  const payroll = { key: 'payroll', label: 'Payroll', path: 'People / Payroll' };
  const onUse = { subjectKind: 'data-use' as const, domain: payroll };
  assert.equal(askedOfText({ askedRole: null, askedOwnerRole: { key: 'data-owner', label: 'Data owner' }, askedPrincipal: null, data: onUse }), 'Data owner of People / Payroll');
  // Retention asks the business owner of the application, even about a use.
  assert.equal(askedOfText({ askedRole: null, askedOwnerRole: { key: 'business-owner', label: 'Business owner' }, askedPrincipal: null, data: onUse }), 'Business owner of the application');
  assert.equal(ownerSubjectText('technical-owner', { subjectKind: 'data-location', domain: null, location: { label: 'Payroll database' } }), 'of the location Payroll database');
  assert.equal(ownerSubjectText('data-owner', null), 'of the application');
});

test('counted or not, and why, from the stored flags only', () => {
  assert.equal(countExplained(record({})).state, 'waiting');
  assert.equal(countExplained(record({ outcome: { label: 'Approved', meaning: 'approved' }, countsTowardRequirement: true })).state, 'counted');
  assert.equal(countExplained(record({ outcome: { label: 'Approved', meaning: 'approved' }, isConsultation: true })).state, 'advice');
  assert.equal(countExplained(record({ outcome: { label: 'Recommendation only', meaning: 'recommendation-only' } })).state, 'recommendation');
  assert.equal(countExplained(record({ outcome: { label: 'Approved', meaning: 'approved' }, approverActorKind: 'ai-client-for-person' })).state, 'recommendation');
  const refused = countExplained(record({ outcome: { label: 'Approved', meaning: 'approved' }, refusedBySeparationRuleKey: 'no-self-approval-tier-3' }), k => (k === 'no-self-approval-tier-3' ? 'No self-approval at tier 3' : k));
  assert.equal(refused.state, 'not-counted');
  assert.match(refused.text, /No self-approval at tier 3/);
  assert.equal(countExplained(record({ outcome: { label: 'Expired', meaning: 'expired' } })).state, 'closed');
});

test('a request line names who it asks, how long it waited, when it expires and who else is asked', () => {
  const facts = requestFacts(approvalItem({}), NOW);
  assert.deepEqual(facts, ['Asks Business owner of the application', 'waiting 2 days', 'expires in 1 day', '1 other request pending on this decision']);
  const answered = requestFacts(approvalItem({ outcome: { label: 'Approved', meaning: 'approved' }, otherPendingCount: 3 }), NOW);
  assert.deepEqual(answered, ['Asks Business owner of the application']);
});

test('waiting for me splits the requests the person may answer now from the ones a separation rule keeps them from (V1)', () => {
  const mayAnswer = approvalItem({ approvalRecordId: '20000000-0000-4000-8000-000000000011' });
  const ownTier3 = approvalItem({
    approvalRecordId: '20000000-0000-4000-8000-000000000012',
    isOwnProposal: true,
    nextTool: null,
    canAnswer: false,
    cannotAnswer: { code: 'rule_violation', message: 'The separation rule No self-approval at tier 3 refuses the answer.', separationRule: { key: 'no-self-approval-tier-3', label: 'No self-approval at tier 3' } },
  });
  const notNow = approvalItem({ approvalRecordId: '20000000-0000-4000-8000-000000000013', nextTool: null });
  const split = splitMine([ownTier3, mayAnswer, notNow]);
  assert.deepEqual(split.answerable.map(i => i.approvalRecordId), [mayAnswer.approvalRecordId]);
  assert.deepEqual(split.others.map(i => i.approvalRecordId), [ownTier3.approvalRecordId, notNow.approvalRecordId]);
  assert.deepEqual(splitMine([]), { answerable: [], others: [] });
  // The kept rows in two kinds (review): an expired pending request is not a separation rule's.
  const expired = approvalItem({ approvalRecordId: '20000000-0000-4000-8000-000000000014', nextTool: null, canAnswer: false, cannotAnswer: { code: 'rule_violation', message: 'This approval request expired before it was answered.', separationRule: null } });
  const kept = splitKept([ownTier3, expired, notNow]);
  assert.deepEqual(kept.separated.map(i => i.approvalRecordId), [ownTier3.approvalRecordId]);
  assert.deepEqual(kept.notNow.map(i => i.approvalRecordId), [expired.approvalRecordId, notNow.approvalRecordId]);
});

test('the count waiting for the person is answerableCount; a server without it gives the total (V1)', () => {
  // Nadia in S-22.13: four requests ask her, and the separation rule keeps her from one.
  assert.equal(answerableTotal({ totalCount: 4, answerableCount: 3 }), 3);
  assert.equal(keptFromAnswering({ totalCount: 4, answerableCount: 3 }), 1);
  assert.equal(answerableTotal({ totalCount: 4, answerableCount: 0 }), 0);
  assert.equal(keptFromAnswering({ totalCount: 4, answerableCount: 0 }), 4);
  assert.equal(answerableTotal({ totalCount: 4 }), 4);
  assert.equal(answerableTotal({ totalCount: 4, answerableCount: null }), 4);
  assert.equal(keptFromAnswering({ totalCount: 4 }), 0);
  assert.equal(answerableTotal(null), null);
  assert.equal(keptFromAnswering(undefined), 0);
});

test('the list search matches the statement, the subject, the application and who is asked', () => {
  const item = approvalItem({});
  assert.ok(matchesText(item, 'export'));
  assert.ok(matchesText(item, 'INSPECTION'));
  assert.ok(matchesText(item, 'business owner'));
  assert.ok(matchesText(item, '  '));
  assert.ok(!matchesText(item, 'payroll'));
});

test('a request no longer pending reads as answered, canceled or expired, never "Someone answered"', () => {
  const base = { outcome: { meaning: 'cancelled', label: 'Canceled' }, approver: null, respondedOn: null, requestedOn: '2026-11-20T09:00:00Z', expiresOn: null } as unknown as Parameters<typeof recordOutcomeText>[0];
  assert.match(recordOutcomeText(base), /canceled by the engine before anyone answered\.$/);
  assert.match(recordOutcomeText(base, 'the decision was withdrawn'), /canceled by the engine before anyone answered: the decision was withdrawn\.$/);
  assert.match(recordOutcomeText({ ...base, outcome: { meaning: 'expired', label: 'Expired' } } as typeof base), /expired without an answer/);
  assert.match(recordOutcomeText({ ...base, outcome: { meaning: 'approved', label: 'Approved' }, approver: { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' } } as typeof base), /^Nadia Ferreira answered\.$/);
  assert.doesNotMatch(recordOutcomeText({ ...base, outcome: { meaning: 'approved', label: 'Approved' } } as typeof base), /Someone/);
});

// Wave 3 verification: after a move to a domain with another data owner the engine canceled the old owner's request and
// asked the new one, and the page said "the decision moved on or was withdrawn". The reason now says what happened.
test('a canceled request says why: the data owner changed, the decision was withdrawn or settled', () => {
  const dataOwner = { key: 'data-owner', label: 'Data owner' };
  const old = { approvalRecordId: 'r1', askedOwnerRole: dataOwner, askedPrincipal: priya, requestedOn: '2026-11-20T09:00:00Z', isConsultation: false } as unknown as DecisionApprovalRecord;
  const now = { approvalRecordId: 'r2', askedOwnerRole: dataOwner, askedPrincipal: nadia, requestedOn: '2026-11-25T09:00:00Z', isConsultation: false } as unknown as DecisionApprovalRecord;
  assert.equal(cancelReasonText(old, 'proposed', [old, now]), 'the data owner changed, so the decision now asks Nadia Ferreira');
  assert.equal(cancelReasonText(old, 'withdrawn', [old, now]), 'the decision was withdrawn');
  assert.equal(cancelReasonText(old, 'accepted', [old]), 'the decision was accepted, so this request was no longer needed');
  assert.equal(cancelReasonText(old, 'proposed', [old]), 'the decision moved on before anyone answered');
  const explained = countExplained({ outcome: { meaning: 'cancelled', label: 'Canceled' }, isConsultation: false, countsTowardRequirement: false }, undefined, cancelReasonText(old, 'proposed', [old, now]));
  assert.equal(explained.text, 'Canceled: the data owner changed, so the decision now asks Nadia Ferreira.');
  assert.doesNotMatch(explained.text, /cancelled/);
});

test('an approval rule and a separation rule read in plain words', () => {
  const rule = approvalRuleText({ approverRoleKey: 'change-manager', approvalMode: 'first-to-respond', minimumCount: 1, timeoutDays: 5, requiresTypedReason: true, tierKey: 'tier-3', changeTypeKey: 'emergency' }, labels);
  assert.equal(rule.who, 'Change manager');
  assert.equal(rule.scope, 'at Tier 3, for a change of type Emergency');
  assert.equal(rule.how, 'the first answer counts, expires after 5 days, a typed reason is needed to approve');
  assert.equal(rule.consultation, false);
  const owner = approvalRuleText({ approverOwnerRoleKey: 'business-owner', approvalMode: 'everyone-must-approve', isConsultation: true }, labels);
  assert.equal(owner.who, 'Business owner of the application');
  assert.equal(owner.how, 'consulted: advice only, never counts');
  // A rule whose owner sits on the data domain says so (wave 3 verification).
  assert.ok(approvalRuleText({ approverOwnerRoleKey: 'business-owner', ownerSubject: 'data-domain', approvalMode: 'first-to-respond' }, labels).who.endsWith(' of the data domain'));
  assert.equal(
    separationRuleText({ mustDifferFrom: 'proposer', tierKey: 'tier-3', enforcement: 'refuse' }, labels),
    'The approver must differ from the person who proposed the decision, at Tier 3. The engine refuses an answer that breaks it.',
  );
  assert.equal(
    separationRuleText({ mustDifferFrom: 'deployer', decisionTypeKey: 'change-authorization', targetStageKey: 'production', enforcement: 'flag' }, labels),
    'The approver must differ from the person who deployed, on change authorization decisions, deploying to Production. The engine records the answer and flags it.',
  );
});

test('the history of a decision lists requests, answers and the settlement oldest first, and guesses no time', () => {
  const d = detail({
    status: { label: 'Accepted', meaning: 'accepted' },
    decidedOn: '2026-11-23T15:00:00Z',
    performedBy: byPerson,
    records: [
      record({ outcome: { label: 'Approved', meaning: 'approved' }, approver: nadia, respondedOn: '2026-11-23T14:00:00Z', typedReason: 'Low risk.', countsTowardRequirement: true }),
      record({ approvalRecordId: '20000000-0000-4000-8000-000000000002', requestedOn: null, expiresOn: null, isConsultation: true, askedOwnerRole: null, askedRole: { key: 'change-manager', label: 'Change manager' } }),
    ],
  });
  const events = decisionHistory(d);
  assert.deepEqual(events.map(e => e.title), [
    'Asked: Business owner of the application',
    'Nadia Ferreira: Approved',
    'Decision accepted',
    'Last change by Priya Castell',
  ]);
  assert.equal(events[1].detail, 'Low risk.');
});

test('the proposer, or the person an AI client proposed for, may submit a decision nobody was asked about', () => {
  assert.ok(proposedByMe(detail({}), priya.objectId.toUpperCase()));
  assert.ok(!proposedByMe(detail({}), nadia.objectId));
  assert.ok(!proposedByMe(detail({}), null));
  assert.ok(proposedByMe(detail({ proposedBy: claude, isAiCreated: true, performedBy: byAi }), priya.objectId));
  assert.ok(notSubmitted(detail({})));
  assert.ok(!notSubmitted(detail({ records: [record({})] })));
  assert.ok(!notSubmitted(detail({ status: { label: 'Accepted', meaning: 'accepted' } })));
});

test('the options a proposal weighed read as a list; free text stays as written', () => {
  const stored = JSON.stringify([{ option: 'Copy the photos into the agent store.', whyNot: 'A second copy to protect.' }, { option: 'A custom model.', whyNot: 'More to build.' }]);
  assert.deepEqual(optionsWeighed(stored), [
    { option: 'Copy the photos into the agent store.', whyNot: 'A second copy to protect.' },
    { option: 'A custom model.', whyNot: 'More to build.' },
  ]);
  assert.equal(optionsWeighed('We looked at two vendors.'), null);
  assert.equal(optionsWeighed('[not json'), null);
  assert.equal(optionsWeighed('[1, 2]'), null);
  assert.equal(optionsWeighed(null), null);
});

test('an approval asked under a decision right names the right, not a key in plain words', () => {
  const rules = { has: (k: string) => k === 'go-live-owner', label: (k: string) => (k === 'go-live-owner' ? 'Business owner approves go-live' : k) };
  const rights = { has: (k: string) => k === 'design-a', label: (k: string) => (k === 'design-a' ? 'Design: accountable' : k) };
  assert.equal(askedUnderText('go-live-owner', rules, rights), 'Rule: Business owner approves go-live');
  assert.equal(askedUnderText('design-a', rules, rights), 'Decision right: Design: accountable');
});
