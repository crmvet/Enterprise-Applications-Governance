// Application 360's logic: sections, the lifecycle and its gates, proposal rights, the tier decision, the data answers,
// retirement, approvals, the map's edges and the owner hand-over. Fictional names and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ApplicationOwners, ApprovalItem, DecisionLogItem, ImpactNode, OwnerAssignment, Requirements, TierAssessmentDetail } from '../../data/contracts';
import {
  applicableRequirements,
  buildHandOver,
  buildNewDependency,
  buildTierProposal,
  dataUseDecisions,
  edgeOf,
  gateDecisionPassed,
  gateDecisionsOf,
  gateStanding,
  gateViews,
  groupApprovals,
  holderOf,
  isProblem,
  lifecycleSteps,
  mayEditEdge,
  reviewOrder,
  myOwnerRoles,
  nodesInBucket,
  portfolioPathOf,
  proposableTypes,
  retirementStep,
  sensitivityAnswer,
  tabOf,
  tierOutcome,
  yesesText,
  withArticle,
  type ConfigRow,
  type Problem,
} from './logic';

const APP = '10000000-0000-4000-8000-000000000001';
const OTHER = '10000000-0000-4000-8000-000000000002';
const THIRD = '10000000-0000-4000-8000-000000000003';
const ME = '20000000-0000-4000-8000-000000000001';
const YOU = '20000000-0000-4000-8000-000000000002';
const TODAY = '2026-11-24';

// The built input, or a failed test with the problem the builder found.
function built<T>(value: T): Exclude<T, Problem> {
  if (isProblem(value)) throw new Error(`Expected an input, got the problem: ${value.problem}`);
  return value as Exclude<T, Problem>;
}

const row = (entity: string, key: string, label: string, fields: Record<string, unknown> = {}, isActive = true): ConfigRow =>
  ({ entity, key, label, isActive, origin: 'shipped', fields });

const owner = (id: string, role: string, objectId: string, extra: Partial<OwnerAssignment> = {}): OwnerAssignment => ({
  ownerAssignmentId: id,
  ownerRoleKey: role,
  label: role,
  principal: { objectId, displayLabel: objectId === ME ? 'Nadia Ferreira' : 'Owen Marsh' },
  isDeputy: false,
  deputyFor: null,
  startOn: '2026-01-05',
  endOn: null,
  ownershipDecisionId: null,
  ...extra,
});

const owners = (list: OwnerAssignment[]): ApplicationOwners => ({ owners: list, gaps: [], microsoftOwnerDiffers: null, ownerCheckedOn: null });

const decision = (id: string, type: string, meaning: string, extra: Partial<DecisionLogItem> = {}): DecisionLogItem => ({
  decisionId: id,
  decisionType: { key: type, label: type },
  subjectKind: 'business-application',
  subjectId: APP,
  subjectLabel: 'Inspection Scheduler',
  statement: `${type} decision`,
  reason: 'Because.',
  exception: null,
  outcome: null,
  status: { label: meaning, meaning: meaning as DecisionLogItem['status']['meaning'] },
  decider: null,
  decidedOn: null,
  supersedes: null,
  supersededBy: null,
  amends: null,
  approvalsSummary: { required: 0, approved: 0, rejected: 0, pending: 0 },
  configurationSetVersion: 1,
  proposedBy: null,
  isAiCreated: false,
  performedBy: null,
  recordUrl: `https://example.test/app/records/decision/${id}`,
  ...extra,
});

test('withArticle picks a or an', () => {
  assert.equal(withArticle('intake decision', true), 'An intake decision');
  assert.equal(withArticle('tier decision'), 'a tier decision');
});

test('tabOf reads the section, sends owners to the summary and ignores anything else', () => {
  assert.equal(tabOf(undefined), 'summary');
  assert.equal(tabOf('tier'), 'tier');
  assert.equal(tabOf('owners'), 'summary');
  assert.equal(tabOf('nonsense'), 'summary');
});

test('portfolioPathOf reads the path the server sends and ignores a card without one', () => {
  assert.deepEqual(portfolioPathOf(null), []);
  const card = { application: { portfolio: [{ portfolioId: 'p1', name: 'Field operations' }, { bad: true }] } } as never;
  assert.deepEqual(portfolioPathOf(card), [{ portfolioId: 'p1', name: 'Field operations' }]);
});

test('myOwnerRoles and holderOf count only assignments that hold now', () => {
  const list = owners([
    owner('a1', 'business-owner', ME),
    owner('a2', 'technical-owner', ME, { startOn: null }),
    owner('a3', 'data-owner', ME, { endOn: '2026-01-31' }),
    owner('a4', 'technical-owner', YOU, { isDeputy: true }),
  ]);
  assert.deepEqual(myOwnerRoles(list, ME.toUpperCase(), TODAY), ['business-owner']);
  assert.equal(holderOf(list, 'technical-owner', TODAY), null);
  assert.equal(holderOf(list, 'business-owner', TODAY)?.ownerAssignmentId, 'a1');
});

test('lifecycleSteps orders by stage rank and marks done, current and upcoming', () => {
  const steps = lifecycleSteps([
    row('lifecycle-stage', 'operate', 'Operate', { stageRank: 8, engineMeaning: 'live' }),
    row('lifecycle-stage', 'idea', 'Idea', { stageRank: 0 }),
    row('lifecycle-stage', 'build', 'Build', { stageRank: 5 }),
    row('lifecycle-stage', 'old', 'Old', { stageRank: 3 }, false),
  ], 'build');
  assert.deepEqual(steps.map(s => [s.key, s.state]), [['idea', 'done'], ['build', 'current'], ['operate', 'upcoming']]);
  assert.ok(lifecycleSteps([row('lifecycle-stage', 'idea', 'Idea')], null).every(s => s.state === 'upcoming'));
});

test('applicableRequirements keeps the rows of the gate, tier and kind', () => {
  const rows = [
    row('gate-requirement', 'go-live-runbook-tier-2', 'Runbook current', { gateKey: 'go-live', tierKey: 'tier-2' }),
    row('gate-requirement', 'go-live-runbook-tier-3', 'Runbook current', { gateKey: 'go-live', tierKey: 'tier-3' }),
    row('gate-requirement', 'go-live-autonomy-agent', 'Autonomy', { gateKey: 'go-live', applicationKindKey: 'agent' }),
    row('gate-requirement', 'go-live-acceptance', 'Accepted', { gateKey: 'go-live' }),
    row('gate-requirement', 'accept-business-owner', 'Owner', { gateKey: 'accept' }),
  ];
  assert.deepEqual(applicableRequirements(rows, 'go-live', 'tier-2', 'power-platform-app').map(r => r.key), ['go-live-acceptance', 'go-live-runbook-tier-2']);
});

const stages = ['idea', 'intake-and-triage', 'prioritize-and-fund', 'risk-and-tier', 'change-and-release', 'operate', 'review'];
const stageRank = (key: string | null) => (key ? stages.indexOf(key) : -1);

const gateRows = [
  row('gate', 'accept', 'Accept', { afterStageKey: 'intake-and-triage', gateDecisionTypeKey: 'intake' }),
  row('gate', 'go-live', 'Go-live', { afterStageKey: 'change-and-release', gateDecisionTypeKey: 'go-live' }),
  row('gate', 'value', 'Value', { afterStageKey: 'review', gateDecisionTypeKey: 'portfolio' }),
];

const requirementRows = [
  row('gate-requirement', 'accept-business-owner', 'A business owner is named', { gateKey: 'accept', requirementKind: 'owner-assigned', requiredOwnerRoleKey: 'business-owner', guidelineKey: 'g2' }),
  row('gate-requirement', 'go-live-acceptance', 'Owner accepted after testing', { gateKey: 'go-live', requirementKind: 'decision-accepted', requiredDecisionTypeKey: 'acceptance' }),
  row('gate-requirement', 'go-live-data-owner-tier-2', 'A data owner is named', { gateKey: 'go-live', tierKey: 'tier-2', requirementKind: 'owner-assigned', requiredOwnerRoleKey: 'data-owner' }),
  row('gate-requirement', 'go-live-runbook-tier-2', 'Runbook current', { gateKey: 'go-live', tierKey: 'tier-2', requirementKind: 'document-current', requiredDocumentTypeKey: 'runbook' }),
];

const requirements: Requirements = {
  gates: [
    { gateKey: 'accept', requirements: [] },
    { gateKey: 'go-live', requirements: [
      { requirementKind: 'decision-accepted', decisionTypeKey: 'acceptance', guideline: 'G9', met: true },
      { requirementKind: 'document-current', documentTypeKey: 'runbook', guideline: null, met: false },
    ] },
    { gateKey: 'value', requirements: [] },
  ],
  approvals: [],
  documents: [],
  reviews: [],
  gatePolicy: [],
};

test('gateViews matches each requirement, works out owner ones, and finds the gate decisions', () => {
  const gates = gateViews({
    gates: gateRows,
    requirementRows,
    requirements,
    owners: owners([owner('a1', 'business-owner', ME)]),
    decisions: [decision('d2', 'portfolio', 'proposed'), decision('d1', 'go-live', 'accepted')],
    stageRank,
    currentStageKey: 'operate',
    tierKey: 'tier-2',
    kindKey: 'power-platform-app',
    today: TODAY,
  });
  assert.deepEqual(gates.map(g => [g.key, g.reach]), [['accept', 'passed'], ['go-live', 'passed'], ['value', 'next']]);
  const goLive = gates[1];
  assert.deepEqual(goLive.requirements.map(r => [r.key, r.met]), [['go-live-acceptance', true], ['go-live-data-owner-tier-2', false], ['go-live-runbook-tier-2', false]]);
  assert.equal(goLive.unmet, 2);
  assert.equal(goLive.settled?.decisionId, 'd1');
  assert.equal(gateStanding(goLive), 'passed-unmet');
  assert.equal(gates[0].requirements[0].met, true);
  assert.equal(gateStanding(gates[0]), 'passed');
  assert.equal(gates[2].open?.decisionId, 'd2');
  assert.equal(gateStanding(gates[2]), 'ready');
});

test('a gate whose decision is accepted with a passing outcome is passed; a holding outcome holds it', () => {
  const accepted = (outcome: string) => decision('d9', 'intake', 'accepted', { outcome: { key: outcome, label: outcome } });
  const base = { gates: gateRows, requirementRows, requirements: null, owners: null, stageRank, currentStageKey: null, tierKey: 'tier-2', kindKey: null, today: TODAY };
  const accept = gateViews({ ...base, decisions: [accepted('accept')] })[0];
  assert.equal(accept.key, 'accept');
  assert.equal(accept.reach, 'next');
  assert.equal(gateDecisionPassed(accept), true);
  assert.notEqual(gateStanding(accept), 'ready');
  assert.ok(gateStanding(accept) === 'passed' || gateStanding(accept) === 'passed-unmet');
  const parked = gateViews({ ...base, decisions: [accepted('park')] })[0];
  assert.equal(gateDecisionPassed(parked), false);
  assert.equal(gateStanding(parked), 'held');
});

test('gateDecisionsOf keeps the decisions about a review or a request, and leaves out changes', () => {
  const items = [
    decision('a', 'design', 'accepted', { subjectKind: 'review' }),
    decision('b', 'priority', 'accepted', { subjectKind: 'intake-request' }),
    decision('c', 'change-authorization', 'accepted', { subjectKind: 'change-request' }),
  ];
  assert.deepEqual(gateDecisionsOf(items).map(d => d.decisionId), ['a', 'b']);
});

test('gateViews says unknown, not met, while get_requirements is missing', () => {
  const gates = gateViews({ gates: gateRows, requirementRows, requirements: null, owners: null, decisions: [], stageRank, currentStageKey: null, tierKey: 'tier-2', kindKey: null, today: TODAY });
  assert.equal(gates[0].reach, 'next');
  assert.equal(gates[1].reach, 'ahead');
  assert.ok(gates[1].requirements.every(r => r.met === null));
  assert.equal(gateStanding(gates[1]), 'checking');
});

test('proposableTypes follows the decision rights the person holds on this application', () => {
  const types = [
    row('decision-type', 'tier', 'Tier', { defaultSubjectKind: 'business-application' }),
    row('decision-type', 'data-use', 'Data use', { defaultSubjectKind: 'business-application' }),
    row('decision-type', 'priority', 'Priority', { defaultSubjectKind: 'intake-request' }),
    row('decision-type', 'go-live', 'Go-live', { defaultSubjectKind: 'business-application' }),
    row('decision-type', 'exception', 'Exception', { defaultSubjectKind: 'business-application' }),
  ];
  const rights = [
    row('decision-right', 'tier-r', '', { decisionTypeKey: 'tier', letter: 'responsible', ownerRoleKey: 'builder' }),
    row('decision-right', 'tier-s', '', { decisionTypeKey: 'tier', letter: 'supports' }),
    row('decision-right', 'data-use-a', '', { decisionTypeKey: 'data-use', letter: 'accountable', ownerRoleKey: 'data-owner' }),
    row('decision-right', 'go-live-a', '', { decisionTypeKey: 'go-live', letter: 'accountable', ownerRoleKey: 'business-owner' }),
    row('decision-right', 'exc-chg', '', { decisionTypeKey: 'exception', letter: 'accountable', changeTypeKey: 'normal', roleKey: 'change-board' }),
    row('decision-right', 'priority-r', '', { decisionTypeKey: 'priority', letter: 'responsible', roleKey: 'pmo' }),
  ];
  assert.deepEqual(proposableTypes(types, rights, { roleKeys: ['pmo', 'change-board'], ownerRoleKeys: ['business-owner', 'builder'], tierKey: 'tier-2', kindKey: null }), ['tier', 'go-live']);
  assert.deepEqual(proposableTypes(types, rights, { roleKeys: [], ownerRoleKeys: ['data-owner'], tierKey: null, kindKey: null }), ['data-use']);
});

test('tierOutcome and buildTierProposal confirm, raise or lower against the computed tier', () => {
  const rank = (k: string) => Number(k.slice(-1));
  assert.equal(tierOutcome('tier-2', 'tier-2', rank), 'confirm');
  assert.equal(tierOutcome('tier-3', 'tier-2', rank), 'raise');
  assert.equal(tierOutcome('tier-1', 'tier-2', rank), 'lower');
  assert.equal(tierOutcome('tier-1', null, rank), null);
  const base = { applicationId: APP, baseTierKey: 'tier-2', computedTierKey: 'tier-2', outcomeKeys: ['confirm', 'raise', 'lower'], statement: 'Set the tier.', context: 'The answers.', reason: 'They fit.', rank };
  const confirm = built(buildTierProposal({ ...base, tierKey: 'tier-2' }));
  assert.equal(confirm.proposedOutcomeKey, 'confirm');
  assert.equal('tierKey' in confirm, false);
  const raise = built(buildTierProposal({ ...base, tierKey: 'tier-3' }));
  assert.equal(raise.proposedOutcomeKey, 'raise');
  assert.equal(raise.tierKey, 'tier-3');
  assert.ok(isProblem(buildTierProposal({ ...base, baseTierKey: null, computedTierKey: null, tierKey: 'tier-1' })));
  assert.ok(isProblem(buildTierProposal({ ...base, tierKey: 'tier-3', outcomeKeys: ['confirm'] })));
  assert.ok(isProblem(buildTierProposal({ ...base, tierKey: 'tier-2', reason: ' ' })));
});

test('sensitivityAnswer finds the sensitive-data answer', () => {
  const answers: TierAssessmentDetail['answers'] = [
    { questionKey: 'reach', question: 'Who can reach it?', answerKey: 'department', answer: 'A department', impliedTier: null, isDriving: false },
    { questionKey: 'sensitivity', question: 'Does it touch sensitive data?', answerKey: 'internal', answer: 'Internal data only', impliedTier: null, isDriving: true },
  ];
  assert.equal(sensitivityAnswer(answers)?.answerKey, 'internal');
  assert.equal(sensitivityAnswer(answers.slice(0, 1)), null);
});

test('dataUseDecisions puts open ones first and yesesText counts the yeses', () => {
  const list = dataUseDecisions([
    decision('d1', 'data-use', 'accepted'),
    decision('d2', 'data-use', 'proposed', { approvalsSummary: { required: 2, approved: 1, rejected: 0, pending: 1 } }),
    decision('d3', 'data-use', 'withdrawn'),
    decision('d4', 'tier', 'proposed'),
  ]);
  assert.deepEqual(list.map(d => d.decisionId), ['d2', 'd1']);
  assert.equal(yesesText(list[0].approvalsSummary), 'Yeses: 1 of 2');
  assert.equal(yesesText({ required: 0, approved: 0, rejected: 0, pending: 0 }), null);
});

test('retirementStep proposes retire first, then complete, and waits on an open one', () => {
  assert.equal(retirementStep([], 'live').next?.outcomeKey, 'retire');
  const retire = decision('d1', 'portfolio', 'accepted', { outcome: { key: 'retire', label: 'Retire' } });
  assert.equal(retirementStep([retire], 'live').next?.decisionTypeKey, 'retirement');
  const waiting = retirementStep([decision('d2', 'retirement', 'proposed'), retire], 'live');
  assert.equal(waiting.next, null);
  assert.equal(waiting.waiting?.decisionId, 'd2');
  assert.equal(retirementStep([retire], 'retired').retired, true);
});

test('groupApprovals keeps one group per decision', () => {
  const item = (id: string, decisionId: string) => ({ approvalRecordId: id, decision: { decisionId }, subject: {}, recordUrl: `u-${decisionId}` }) as unknown as ApprovalItem;
  const groups = groupApprovals([item('r1', 'D1'), item('r2', 'd2'), item('r3', 'd1')]);
  assert.deepEqual(groups.map(g => g.items.map(i => i.approvalRecordId)), [['r1', 'r3'], ['r2']]);
});

const node = (id: string, path: string[], extra: Partial<ImpactNode> = {}): ImpactNode => ({
  businessApplicationId: id,
  name: id === OTHER ? 'Asset Register' : 'Crew Roster',
  tier: { key: 'tier-2', label: 'Tier 2' },
  lifecycleMeaning: 'live',
  businessOwner: null,
  technicalOwner: null,
  dependencyTypeKey: 'reads-data-from',
  criticalityKey: 'high',
  depth: path.length - 1,
  path,
  ...extra,
});

test('edgeOf names the depending application on each side of the map', () => {
  const names = new Map([[OTHER.toLowerCase(), 'Asset Register']]);
  const needs = edgeOf('dependencies', node(OTHER, [APP, OTHER]), APP, names, 'Inspection Scheduler');
  assert.deepEqual([needs.fromApplicationId, needs.toApplicationId, needs.fromName], [APP, OTHER, 'Inspection Scheduler']);
  const breaks = edgeOf('dependants', node(THIRD, [APP, OTHER, THIRD]), APP, names, 'Inspection Scheduler');
  assert.deepEqual([breaks.fromApplicationId, breaks.toApplicationId, breaks.toName], [THIRD, OTHER, 'Asset Register']);
});

test('mayEditEdge shows change and end to the owner of the depending application, or an architect or admin', () => {
  assert.equal(mayEditEdge(APP, APP, true, false), true);
  assert.equal(mayEditEdge(OTHER, APP, true, false), false);
  assert.equal(mayEditEdge(OTHER, APP, false, true), true);
  assert.equal(mayEditEdge(APP, APP, false, false), false);
});

test('reviewOrder puts open reviews first by due date, then done ones, latest first', () => {
  const r = (id: string, meaning: string, dueOn: string | null, doneOn: string | null) => ({ id, status: { meaning }, dueOn, doneOn });
  const ordered = reviewOrder([r('a', 'done', null, '2026-06-30'), r('b', 'due', '2026-12-15', null), r('c', 'due', '2026-11-30', null), r('d', 'done', null, '2026-08-28')]);
  assert.deepEqual(ordered.map(x => x.id), ['c', 'b', 'd', 'a']);
});

test('nodesInBucket finds the nodes behind a bar, with null for not set', () => {
  const nodes = [node(OTHER, [APP, OTHER]), node(THIRD, [APP, THIRD], { criticalityKey: null, tier: null })];
  assert.deepEqual(nodesInBucket(nodes, 'criticality', 'high').map(n => n.businessApplicationId), [OTHER]);
  assert.deepEqual(nodesInBucket(nodes, 'tier', null).map(n => n.businessApplicationId), [THIRD]);
});

test('buildNewDependency sets the direction and refuses an application on itself', () => {
  const base = { rootId: APP, otherId: OTHER, dependencyTypeKey: 'reads-data-from', criticalityKey: 'high', notes: ' ' };
  const needs = built(buildNewDependency({ ...base, direction: 'depends-on' }));
  assert.deepEqual([needs.fromApplicationId, needs.toApplicationId, needs.notes], [APP, OTHER, undefined]);
  const used = built(buildNewDependency({ ...base, direction: 'used-by' }));
  assert.equal(used.fromApplicationId, OTHER);
  assert.ok(isProblem(buildNewDependency({ ...base, direction: 'depends-on', otherId: APP })));
  assert.ok(isProblem(buildNewDependency({ ...base, direction: 'depends-on', dependencyTypeKey: '' })));
});

test('buildHandOver names the new holder and deputy and ends the ones they replace', () => {
  const holder = owner('a1', 'technical-owner', YOU);
  const deputy = owner('a2', 'technical-owner', THIRD, { isDeputy: true });
  const handOver = built(buildHandOver({ applicationId: APP, ownerRoleKey: 'technical-owner', holderObjectId: ME, endingHolder: holder, deputyObjectId: OTHER, endingDeputy: deputy, startOn: '', reason: 'Graduates to IT.' }));
  assert.deepEqual(handOver.changes, [
    { ownerRoleKey: 'technical-owner', addObjectId: ME, endAssignmentId: 'a1' },
    { ownerRoleKey: 'technical-owner', addObjectId: OTHER, isDeputy: true, endAssignmentId: 'a2' },
  ]);
  assert.ok(isProblem(buildHandOver({ applicationId: APP, ownerRoleKey: 'technical-owner', holderObjectId: '', endingHolder: null, deputyObjectId: '', endingDeputy: null, startOn: '', reason: 'x' })));
  assert.ok(isProblem(buildHandOver({ applicationId: APP, ownerRoleKey: 'technical-owner', holderObjectId: ME, endingHolder: null, deputyObjectId: ME, endingDeputy: null, startOn: '', reason: 'x' })));
  assert.ok(isProblem(buildHandOver({ applicationId: APP, ownerRoleKey: 'technical-owner', holderObjectId: ME, endingHolder: null, deputyObjectId: '', endingDeputy: null, startOn: '', reason: ' ' })));
});
