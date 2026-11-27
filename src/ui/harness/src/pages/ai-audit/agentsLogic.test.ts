// The Agents page's logic: the security review, the go-live gate, the owner gaps, the needs, who may propose, and the
// autonomy proposal the sheet sends. Fictional names and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { AgentItem, ApplicationCard, DecisionLogItem, Requirements } from '../../data/contracts';
import {
  agentGaps,
  agentHolder,
  agreeOutcome,
  autonomyProposal,
  dataUsesText,
  decisionsFor,
  gateReadiness,
  holderName,
  lifecycleOf,
  mayPropose,
  myAgentOwnerRoles,
  myOwnerRoles,
  needsOf,
  needsOfAgent,
  ownerGaps,
  registryLinks,
  requirementText,
  reviewOfAgent,
  securityReviewOf,
  type AgentFacts,
} from './agentsLogic';

const APP = '00000000-0000-4000-8000-00000000a001';
const OTHER = '00000000-0000-4000-8000-00000000a002';
const DMITRI = { objectId: '00000000-0000-4000-8000-000000001109', displayLabel: 'Dmitri Halloran' };
const TODAY = '2026-11-24';

type Review = NonNullable<ApplicationCard['reviews']>[number];
const review = (over: Partial<Review>): Review => ({
  reviewId: '00000000-0000-4000-8000-00000000e001',
  reviewType: { key: 'security-review', label: 'Security review' },
  status: { label: 'Due', meaning: 'due' },
  dueOn: '2026-12-10',
  doneOn: null,
  ...over,
});

test('the security review: an overdue one first, then the earliest due, then the latest done, else none', () => {
  assert.equal(securityReviewOf(undefined, TODAY).state, 'none');
  assert.equal(securityReviewOf([review({ reviewType: { key: 'architecture', label: 'Architecture' } })], TODAY).state, 'none');
  const due = securityReviewOf([review({ dueOn: '2026-12-10' }), review({ reviewId: 'r2', dueOn: '2026-11-20' })], TODAY);
  assert.equal(due.state, 'overdue');
  assert.equal(due.reviewId, 'r2');
  assert.equal(due.text, 'Overdue since 20 Nov 2026');
  assert.equal(securityReviewOf([review({})], TODAY).text, 'Due 10 Dec 2026');
  const done = securityReviewOf([
    review({ reviewId: 'old', status: { label: 'Done', meaning: 'done' }, doneOn: '2026-01-10' }),
    review({ reviewId: 'new', status: { label: 'Done', meaning: 'done' }, doneOn: '2026-06-10' }),
  ], TODAY);
  assert.equal(done.state, 'done');
  assert.equal(done.reviewId, 'new');
});

const requirements = (met: (boolean | undefined)[]): Requirements => ({
  gates: [{
    gateKey: 'go-live',
    requirements: met.map((m, i) => ({ requirementKind: 'decision-accepted' as const, decisionTypeKey: i === 0 ? 'autonomy-level' : 'go-live', guideline: null, met: m })),
  }],
  approvals: [],
  documents: [],
  reviews: [],
  gatePolicy: [],
});

test('the go-live gate: met of total, the open ones, and nothing when the rules set no requirement there', () => {
  assert.deepEqual(gateReadiness(requirements([false, true]))?.met, 1);
  assert.equal(gateReadiness(requirements([false, true]))?.unmet.length, 1);
  assert.equal(gateReadiness(requirements([undefined])), null);
  assert.equal(gateReadiness(requirements([])), null);
  assert.equal(gateReadiness(null), null);
  assert.equal(gateReadiness(requirements([true]), 'design'), null);
});

test('a gate requirement in words', () => {
  const labels = { decisionType: (k: string) => (k === 'autonomy-level' ? 'Autonomy level' : k), documentType: () => 'Runbook', reviewType: () => 'Security review' };
  assert.equal(requirementText({ requirementKind: 'decision-accepted', decisionTypeKey: 'autonomy-level', guideline: 'G17' }, labels), 'Autonomy level decision accepted');
  assert.equal(requirementText({ requirementKind: 'document-current', documentTypeKey: 'runbook', guideline: null }, labels), 'Runbook current');
  assert.equal(requirementText({ requirementKind: 'review-done', reviewTypeKey: 'security-review', guideline: null }, labels), 'Security review done');
});

test('owner gaps in words, and the reader\'s own owner roles', () => {
  const gaps = ownerGaps({ owners: [], gaps: [{ ownerRoleKey: 'business-owner', missing: 'holder' }, { ownerRoleKey: 'technical-owner', missing: 'deputy' }], microsoftOwnerDiffers: null, ownerCheckedOn: null }, k => (k === 'business-owner' ? 'Business owner' : 'Technical owner'));
  assert.deepEqual(gaps.map(g => g.text), ['No business owner', 'No deputy technical owner']);
  assert.deepEqual(gaps.map(g => g.holderMissing), [true, false]);
  const owners: ApplicationCard['owners'] = [
    { ownerAssignmentId: 'o1', ownerRoleKey: 'business-owner', label: 'Business owner', principal: DMITRI, isDeputy: false, deputyFor: null, startOn: null, endOn: null, ownershipDecisionId: null },
    { ownerAssignmentId: 'o2', ownerRoleKey: 'technical-owner', label: 'Technical owner', principal: DMITRI, isDeputy: true, deputyFor: 'x', startOn: null, endOn: null, ownershipDecisionId: null },
  ];
  assert.deepEqual(myOwnerRoles(owners, DMITRI.objectId.toUpperCase()), ['business-owner', 'technical-owner']);
  assert.deepEqual(myOwnerRoles(owners, null), []);
  assert.equal(holderName(owners, 'business-owner'), 'Dmitri Halloran');
  assert.equal(holderName(owners, 'technical-owner'), null);
});

const facts = (over: Partial<AgentFacts>): AgentFacts => ({
  businessApplicationId: APP,
  name: 'Supplier Price Agent',
  tier: null,
  stage: null,
  lifecycle: 'pre-live',
  autonomy: { key: 'assist', label: 'Assist' },
  autonomyKnown: true,
  review: { state: 'done', text: 'Done', tone: 'success', reviewId: null },
  gate: { met: 2, total: 2, unmet: [] },
  gaps: [],
  autonomyWaiting: [],
  flagged: [],
  ...over,
});

test('the needs of an agent; a fact not read yet is no need, and a retired agent needs no level or review', () => {
  assert.deepEqual(needsOf(facts({})), []);
  assert.deepEqual(needsOf(facts({ autonomy: null })), ['autonomy']);
  assert.deepEqual(needsOf(facts({ autonomy: null, autonomyKnown: false })), []);
  assert.deepEqual(needsOf(facts({ gaps: [{ ownerRoleKey: 'business-owner', text: 'No business owner', holderMissing: true }] })), ['owner']);
  assert.deepEqual(needsOf(facts({ review: { state: 'none', text: 'No security review', tone: 'danger', reviewId: null } })), ['security']);
  assert.deepEqual(needsOf(facts({ gate: { met: 1, total: 2, unmet: [] } })), ['go-live']);
  assert.deepEqual(needsOf(facts({ gate: { met: 1, total: 2, unmet: [] }, lifecycle: 'live' })), []);
  assert.deepEqual(needsOf(facts({ autonomy: null, review: null, lifecycle: 'retired' })), []);
});

test('who may propose: a responsible, accountable or consulted right by role or owner role, active rows only', () => {
  const rights = [
    { key: 'autonomy-level-a', isActive: true, fields: { decisionTypeKey: 'autonomy-level', letter: 'accountable', roleKey: 'security-reviewer' } },
    { key: 'autonomy-level-c', isActive: true, fields: { decisionTypeKey: 'autonomy-level', letter: 'consulted', roleKey: 'ai-lead' } },
    { key: 'autonomy-level-i', isActive: true, fields: { decisionTypeKey: 'autonomy-level', letter: 'informed', roleKey: 'reader' } },
    { key: 'flagged-agent-c', isActive: true, fields: { decisionTypeKey: 'flagged-agent', letter: 'consulted', ownerRoleKey: 'business-owner' } },
    { key: 'flagged-agent-old', isActive: false, fields: { decisionTypeKey: 'flagged-agent', letter: 'responsible', roleKey: 'maker' } },
  ];
  assert.equal(mayPropose(rights, 'autonomy-level', ['ai-lead'], []), true);
  assert.equal(mayPropose(rights, 'autonomy-level', ['reader'], []), false);
  assert.equal(mayPropose(rights, 'flagged-agent', [], ['business-owner']), true);
  assert.equal(mayPropose(rights, 'flagged-agent', ['maker'], []), false);
});

test('the autonomy proposal: every field checked, the level and the agree outcome sent', () => {
  const form = { levelKey: 'assist', levelLabel: 'Assist', agentName: 'Supplier Price Agent', context: 'Reads supplier prices.', reason: 'It only suggests.' };
  assert.deepEqual(autonomyProposal({ ...form, levelKey: '' }, APP, 'agree'), { problem: 'Choose the autonomy level you propose.', field: 'autonomyLevelKey' });
  assert.equal((autonomyProposal(form, APP, null) as { field: string }).field, 'proposedOutcomeKey');
  assert.equal((autonomyProposal({ ...form, context: ' ' }, APP, 'agree') as { field: string }).field, 'context');
  assert.equal((autonomyProposal({ ...form, reason: '' }, APP, 'agree') as { field: string }).field, 'reason');
  assert.deepEqual(autonomyProposal(form, APP, 'agree'), {
    decisionTypeKey: 'autonomy-level',
    subjectKind: 'business-application',
    subjectId: APP,
    statement: 'Agree the autonomy level Assist for Supplier Price Agent',
    context: 'Reads supplier prices.',
    proposedOutcomeKey: 'agree',
    reason: 'It only suggests.',
    autonomyLevelKey: 'assist',
  });
  assert.equal(agreeOutcome([{ key: 'decline' }, { key: 'agree' }]), 'agree');
  assert.equal(agreeOutcome([{ key: 'decline' }]), null);
});

test('the decisions of one agent by type, and its links to Microsoft\'s registries', () => {
  const item = (subjectId: string, type: string): DecisionLogItem => ({
    decisionId: `${subjectId}-${type}`,
    decisionType: { key: type, label: type },
    subjectKind: 'business-application',
    subjectId,
    subjectLabel: 'Agent',
    statement: 'Agree assist',
    reason: '',
    exception: null,
    outcome: null,
    status: { label: 'Proposed', meaning: 'proposed' },
    decider: null,
    decidedOn: null,
    supersedes: null,
    supersededBy: null,
    amends: null,
    approvalsSummary: { required: 1, approved: 0, rejected: 0, pending: 1 },
    configurationSetVersion: 1,
    proposedBy: null,
    isAiCreated: false,
    performedBy: null,
    recordUrl: 'https://product.example.org/app/records/decision/x',
  });
  const log = [item(APP, 'autonomy-level'), item(APP.toUpperCase(), 'flagged-agent'), item(OTHER, 'autonomy-level')];
  assert.equal(decisionsFor(log, APP, 'autonomy-level').length, 1);
  assert.equal(decisionsFor(log, APP, 'flagged-agent').length, 1);
  const components: ApplicationCard['components'] = [
    { technicalComponentId: 'c1', kind: { key: 'copilot-studio-agent', label: 'Copilot Studio agent' }, externalId: 'agent-1', url: 'https://make.example.org/agents/agent-1', labelAsSeen: null, lastSeenOn: null },
    { technicalComponentId: 'c2', kind: { key: 'repository', label: 'Repository' }, externalId: 'repo', url: null, labelAsSeen: null, lastSeenOn: null },
  ];
  assert.deepEqual(registryLinks(components, ['copilot-studio-agent', 'entra-agent-identity']).map(l => l.label), ['Copilot Studio agent']);
});

test('before or after go-live: the configured meaning when read, else the stage rank against the stage the gate follows', () => {
  assert.equal(lifecycleOf('retired', 3, 7), 'retired');
  assert.equal(lifecycleOf(undefined, 5, 7), 'pre-live');
  assert.equal(lifecycleOf(undefined, 7, 7), 'pre-live');
  assert.equal(lifecycleOf(undefined, 8, 7), 'live');
  assert.equal(lifecycleOf(undefined, undefined, 7), null);
  assert.equal(lifecycleOf(undefined, 5, undefined), null);
});

// ---------- the register from list_agents ----------

const agent = (over: Partial<AgentItem> = {}): AgentItem => ({
  businessApplicationId: APP,
  recordNumber: 'APP-0042',
  name: 'Supplier Price Agent',
  registration: { label: 'Registered', meaning: 'registered' },
  applicationKind: { key: 'agent', label: 'Agent' },
  tier: { key: 'tier-3', label: 'Tier 3' },
  lifecycleStage: { key: 'build', label: 'Build' },
  autonomy: { agreed: null, decisionId: null, waiting: null, requiredAtGoLive: true },
  owners: [
    { ownerRole: { key: 'business-owner', label: 'Business owner' }, principal: DMITRI, isDeputy: false },
    { ownerRole: { key: 'business-owner', label: 'Business owner' }, principal: { objectId: OTHER, displayLabel: 'Noor Safadi' }, isDeputy: true },
  ],
  ownerGaps: [{ ownerRole: { key: 'technical-owner', label: 'Technical owner' }, missing: 'holder' }],
  securityReview: { reviewType: { key: 'security-review', label: 'Security review' }, state: 'none', requiredAtGoLive: true, lastDoneOn: null, dueOn: null, reviewId: null, recordUrl: null },
  goLive: { gate: { key: 'go-live', label: 'Go-live' }, ready: false, unmet: [] },
  flaggedDecision: null,
  flags: [{ kind: 'security-review-missing', label: 'The go-live gate needs a security review, and none is open or done.', recordUrl: null }],
  dataUses: { open: 2, reachingAi: 1, drafts: 0 },
  components: [],
  isAiCreated: false,
  recordUrl: 'https://example.org/app/records/business-application/x',
  ...over,
});

test('an agent from list_agents: its needs come from the facts the server gives', () => {
  assert.deepEqual(needsOfAgent(agent(), 'pre-live'), ['autonomy', 'owner', 'security', 'go-live']);
  assert.deepEqual(needsOfAgent(agent(), 'live'), ['autonomy', 'owner', 'security']);
  assert.deepEqual(needsOfAgent(agent(), 'retired'), ['owner']);
  const fine = agent({ autonomy: { agreed: { key: 'supervised', label: 'Supervised' }, decisionId: null, waiting: null, requiredAtGoLive: true }, ownerGaps: [], flags: [], goLive: { gate: null, ready: true, unmet: [] } });
  assert.deepEqual(needsOfAgent(fine, 'pre-live'), []);
});

test('the security review of an agent in words, a missing one a need only where the gate requires it', () => {
  assert.deepEqual([reviewOfAgent(agent().securityReview).text, reviewOfAgent(agent().securityReview).tone], ['No security review', 'danger']);
  const optional = reviewOfAgent({ ...agent().securityReview, requiredAtGoLive: false });
  assert.equal(optional.tone, 'subtle');
  assert.equal(reviewOfAgent({ ...agent().securityReview, state: 'overdue', dueOn: '2026-11-20' }).text, 'Overdue since 20 Nov 2026');
  assert.equal(reviewOfAgent({ ...agent().securityReview, state: 'done', lastDoneOn: '2026-06-10' }).text, 'Done 10 Jun 2026');
});

test('the owners, gaps, reader roles and data uses of an agent', () => {
  assert.deepEqual(agentGaps(agent()).map(g => g.text), ['No technical owner']);
  assert.equal(agentHolder(agent(), 'business-owner'), 'Dmitri Halloran');
  assert.equal(agentHolder(agent(), 'technical-owner'), null);
  assert.deepEqual(myAgentOwnerRoles(agent(), DMITRI.objectId.toUpperCase()), ['business-owner']);
  assert.deepEqual(myAgentOwnerRoles(agent(), null), []);
  assert.equal(dataUsesText(agent().dataUses), '2 open data uses, 1 reach AI');
  assert.equal(dataUsesText({ open: 0, reachingAi: 0, drafts: 0 }), null);
});
