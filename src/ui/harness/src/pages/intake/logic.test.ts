// The intake pages' logic: the business owner line in the need, the draft input, the wizard's checks, the tier the
// answers point to, who counts as the requester, the default view and the demand buckets.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { aiMarkText } from '../../components/stamps';
import type { IntakeSummary, TierQuestion } from '../../data/contracts';
import {
  EMPTY_FIELDS,
  aiStampOf,
  answerList,
  approvalsText,
  bucketsOf,
  changedFields,
  checkFields,
  composeNeed,
  daysSinceSaved,
  defaultIntakeView,
  draftInput,
  intakeProgress,
  isRequesterOf,
  matchPercent,
  priorityOf,
  savedValueStays,
  splitNeed,
  tierFromAnswers,
  type WizardFields,
  splitApprovals,
} from './logic';

const PRIYA = '00000000-0000-4000-8000-000000000301';
const NADIA = '00000000-0000-4000-8000-000000000212';

test('the business owner is kept as the last line of the need, and read back from there', () => {
  const need = composeNeed('Track crew overtime per shift.  ', ' Nadia   Ferreira ');
  assert.equal(need, 'Track crew overtime per shift.\n\nBusiness owner: Nadia Ferreira');
  assert.deepEqual(splitNeed(need), { purpose: 'Track crew overtime per shift.', owner: 'Nadia Ferreira' });
  assert.equal(composeNeed('Only the need.', '   '), 'Only the need.');
  assert.deepEqual(splitNeed('Only the need.'), { purpose: 'Only the need.', owner: '' });
  assert.deepEqual(splitNeed('Business owner: Hana Lindqvist\n'), { purpose: '', owner: 'Hana Lindqvist' });
  // A line in the middle of the text is part of the need, not the owner.
  assert.deepEqual(splitNeed('Business owner: someone\nThen more text.'), { purpose: 'Business owner: someone\nThen more text.', owner: '' });
});

const fields = (more: Partial<WizardFields> = {}): WizardFields => ({ ...EMPTY_FIELDS, title: 'Crew overtime tracker', purpose: 'Track overtime.', ...more });

test('the draft input leaves out empty optional fields and sends the ones set', () => {
  assert.deepEqual(draftInput(fields(), null), { title: 'Crew overtime tracker', need: 'Track overtime.' });
  assert.deepEqual(
    draftInput(fields({ owner: 'Nadia', proposedKindKey: 'power-platform-app', usesAi: 'no', horizonKey: 'now', expectedBenefit: ' Saves time ' }), 'id-1'),
    {
      intakeRequestId: 'id-1',
      title: 'Crew overtime tracker',
      need: 'Track overtime.\n\nBusiness owner: Nadia',
      proposedKindKey: 'power-platform-app',
      usesAi: false,
      expectedBenefit: 'Saves time',
      horizonKey: 'now',
    },
  );
  assert.equal(draftInput(fields({ usesAi: 'yes' }), null).usesAi, true);
});

test('the wizard checks the title, the need and the lengths, and names the field', () => {
  assert.deepEqual(checkFields(fields({ title: '  ' }))?.field, 'title');
  assert.deepEqual(checkFields(fields({ title: 'x'.repeat(201) }))?.field, 'title');
  assert.deepEqual(checkFields(fields({ purpose: '' }))?.field, 'need');
  assert.deepEqual(checkFields(fields({ purpose: 'x'.repeat(3990), owner: 'Nadia Ferreira' }))?.field, 'need');
  assert.deepEqual(checkFields(fields({ expectedBenefit: 'x'.repeat(2001) }))?.field, 'expectedBenefit');
  assert.equal(checkFields(fields()), null);
});

test('changed fields compare what would be sent; an emptied field is no change but the saved value stays', () => {
  const saved = fields({ horizonKey: 'next', proposedKindKey: 'agent' });
  assert.deepEqual(changedFields(saved, null), ['new']);
  assert.deepEqual(changedFields(saved, saved), []);
  assert.deepEqual(changedFields({ ...saved, title: 'Other' }, saved), ['title']);
  assert.deepEqual(changedFields({ ...saved, horizonKey: '' }, saved), []);
  assert.deepEqual(savedValueStays({ ...saved, horizonKey: '' }, saved), ['horizonKey']);
  assert.deepEqual(savedValueStays(saved, saved), []);
});

const questions: TierQuestion[] = [
  {
    questionKey: 'data-change',
    text: 'Does it change data?',
    helpText: null,
    answers: [
      { answerKey: 'reads-only', text: 'Reads only', impliedTier: { key: 'tier-1', label: 'Tier 1' } },
      { answerKey: 'as-user', text: 'As the user', impliedTier: { key: 'tier-2', label: 'Tier 2' } },
    ],
  },
  {
    questionKey: 'sensitivity',
    text: 'Does it touch sensitive data?',
    helpText: null,
    answers: [
      { answerKey: 'internal', text: 'Internal', impliedTier: { key: 'tier-2', label: 'Tier 2' } },
      { answerKey: 'restricted', text: 'Restricted', impliedTier: { key: 'tier-3', label: 'Tier 3' } },
    ],
  },
  {
    questionKey: 'reach',
    text: 'Who can reach it?',
    helpText: null,
    answers: [{ answerKey: 'department', text: 'A department', impliedTier: { key: 'tier-2', label: 'Tier 2' } }],
  },
];
const rank = (key: string) => ({ 'tier-1': 0, 'tier-2': 1, 'tier-3': 2 })[key] ?? -1;

test('the highest tier any answer implies wins, and every answer at that tier is decisive', () => {
  assert.deepEqual(tierFromAnswers(questions, {}, rank), { tier: null, decisive: [], answered: 0, total: 3 });
  const two = tierFromAnswers(questions, { 'data-change': 'as-user', reach: 'department' }, rank);
  assert.deepEqual(two, { tier: { key: 'tier-2', label: 'Tier 2' }, decisive: ['data-change', 'reach'], answered: 2, total: 3 });
  const three = tierFromAnswers(questions, { 'data-change': 'reads-only', sensitivity: 'restricted', reach: 'department' }, rank);
  assert.deepEqual(three.tier, { key: 'tier-3', label: 'Tier 3' });
  assert.deepEqual(three.decisive, ['sensitivity']);
  // Ranks not read yet: the key decides, so the hint still moves the right way.
  assert.equal(tierFromAnswers(questions, { 'data-change': 'reads-only', sensitivity: 'restricted' }, () => -1).tier?.key, 'tier-3');
});

test('the tier answers sent are only valid answers to the questions that apply, in question order', () => {
  assert.deepEqual(answerList(questions, { reach: 'department', 'data-change': 'as-user', sensitivity: 'gone', other: 'x' }), [
    { questionKey: 'data-change', answerKey: 'as-user' },
    { questionKey: 'reach', answerKey: 'department' },
  ]);
});

test('the requester, or the person an AI client drafted for, counts as the requester', () => {
  const base = { requester: { objectId: PRIYA, displayLabel: 'Priya Castell' }, performedBy: null };
  assert.equal(isRequesterOf(base, PRIYA.toUpperCase()), true);
  assert.equal(isRequesterOf(base, NADIA), false);
  assert.equal(isRequesterOf(base, null), false);
  const drafted = {
    requester: null,
    performedBy: {
      actorKind: 'ai-client-for-person' as const,
      principal: { objectId: PRIYA, displayLabel: 'Priya Castell' },
      onBehalfOf: { objectId: PRIYA, displayLabel: 'Priya Castell' },
      clientAppLabel: 'Claude Code',
      stampedOn: '2026-11-24T09:00:00Z',
    },
  };
  assert.equal(isRequesterOf(drafted, PRIYA), true);
  assert.equal(isRequesterOf(drafted, NADIA), false);
});

test('the first view is triage for a platform admin, demand for the PMO, else my requests', () => {
  assert.equal(defaultIntakeView(['requester', 'platform-admin']), 'triage');
  assert.equal(defaultIntakeView(['pmo', 'architect']), 'demand');
  assert.equal(defaultIntakeView(['maker']), 'mine');
  assert.equal(defaultIntakeView([]), 'mine');
});

test('days since the last save come from the stamp, and seeded rows have none', () => {
  const stamp = { actorKind: 'person' as const, principal: { objectId: PRIYA, displayLabel: 'Priya' }, onBehalfOf: null, clientAppLabel: null, stampedOn: '2026-11-10T09:00:00Z' };
  assert.equal(daysSinceSaved(stamp, '2026-11-24'), 14);
  assert.equal(daysSinceSaved(null, '2026-11-24'), null);
  assert.equal(daysSinceSaved({ ...stamp, stampedOn: null }, '2026-11-24'), null);
});

test('demand buckets count by key, in configured order, with "Not set" last', () => {
  const item = (horizon: { key: string; label: string } | null) => ({ horizon }) as unknown as IntakeSummary;
  const items = [item({ key: 'later', label: 'Later' }), item(null), item({ key: 'now', label: 'Now' }), item({ key: 'now', label: 'Now' })];
  assert.deepEqual(bucketsOf(items, i => i.horizon), [
    { key: 'now', label: 'Now', count: 2 },
    { key: 'later', label: 'Later', count: 1 },
    { key: null, label: 'Not set', count: 1 },
  ]);
  const order = (key: string) => ['now', 'next', 'later'].indexOf(key);
  assert.deepEqual(bucketsOf(items, i => i.horizon, order).map(b => b.key), ['now', 'later', null]);
});

test('the words for a match and for the approvals', () => {
  assert.equal(matchPercent(0.724), '72% match');
  assert.equal(matchPercent(1.4), '100% match');
  assert.equal(approvalsText({ required: 0, approved: 0, rejected: 0, pending: 0 }), null);
  assert.equal(approvalsText({ required: 1, approved: 0, rejected: 0, pending: 1 }), '0 of 1 approval in, 1 pending');
  assert.equal(approvalsText({ required: 2, approved: 1, rejected: 1, pending: 0 }), '1 of 2 approvals in, 1 rejected');
});

test('the progress of a request reads in words: current, done, stopped', () => {
  const states = (steps: ReturnType<typeof intakeProgress>) => steps.map(s => `${s.label}:${s.state}${s.note ? `:${s.note}` : ''}`);
  assert.deepEqual(states(intakeProgress('draft', null, null)), ['Draft:current', 'Submitted:todo', 'Intake decision:todo', 'Outcome:todo']);
  assert.deepEqual(states(intakeProgress('submitted', { meaning: 'proposed', label: 'Proposed' }, null)), [
    'Draft:done', 'Submitted:done', 'Intake decision:current:Proposed', 'Outcome:todo',
  ]);
  assert.deepEqual(states(intakeProgress('submitted', { meaning: 'accepted', label: 'Accepted' }, 'Accept')), [
    'Draft:done', 'Submitted:done', 'Intake decision:done:Accepted', 'Outcome:done:Accept',
  ]);
  assert.deepEqual(states(intakeProgress('rejected', { meaning: 'rejected', label: 'Rejected' }, null)).at(-1), 'Outcome:done:Declined');
  assert.deepEqual(states(intakeProgress('withdrawn', null, null)), ['Draft:done', 'Submitted:stopped:Withdrawn', 'Intake decision:todo', 'Outcome:todo']);
  assert.deepEqual(states(intakeProgress('withdrawn', { meaning: 'withdrawn', label: 'Withdrawn' }, null)), [
    'Draft:done', 'Submitted:done', 'Intake decision:stopped:Withdrawn', 'Outcome:todo',
  ]);
});

test('the AI mark takes only the stamp of an AI client, never a later edit by a person', () => {
  const person = { actorKind: 'person' as const, principal: { objectId: PRIYA, displayLabel: 'Priya' }, onBehalfOf: null, clientAppLabel: 'Web app', stampedOn: null };
  const ai = { ...person, actorKind: 'ai-client-for-person' as const, clientAppLabel: 'Claude Code' };
  assert.equal(aiStampOf(person), null);
  assert.equal(aiStampOf(ai), ai);
  assert.equal(aiStampOf(null), null);
});

test('a stamp with the drafted by pair keeps the AI mark naming the client after a later write', () => {
  const engine = { actorKind: 'engine' as const, principal: { objectId: PRIYA, displayLabel: 'Priya' }, onBehalfOf: { objectId: PRIYA, displayLabel: 'Priya Castell' }, clientAppLabel: null, stampedOn: null,
    draftedBy: { clientAppLabel: 'Claude Code', forPrincipal: { objectId: PRIYA, displayLabel: 'Priya Castell' } } };
  assert.equal(aiStampOf(engine), engine);
  assert.equal(aiMarkText(true, aiStampOf(engine)), 'Drafted by Claude Code for Priya Castell');
});

test('the named business owner goes as its object ID, and a saved one stays when the field is emptied', () => {
  const owner = { objectId: '00000000-0000-4000-8000-000000000206', displayLabel: 'Hana Lindqvist', principalKind: 'user' as const };
  assert.equal(draftInput(fields({ businessOwner: owner }), null).businessOwnerObjectId, owner.objectId);
  assert.equal(draftInput(fields(), null).businessOwnerObjectId, undefined);
  assert.deepEqual(savedValueStays(fields(), fields({ businessOwner: owner })), ['businessOwner']);
  assert.deepEqual(savedValueStays(fields({ businessOwner: owner }), fields({ businessOwner: owner })), []);
  assert.deepEqual(changedFields(fields({ businessOwner: owner }), fields()), ['businessOwnerObjectId']);
});

test('the priority comes from the list read when it names one, else from the latest priority decision', () => {
  const status = { label: 'Proposed', meaning: 'proposed' };
  const base = { decisions: [{ decisionId: 'd-1', decisionType: { key: 'priority', label: 'Priority' }, status, outcome: { key: 'defer', label: 'Defer' }, approvalsSummary: null }] } as unknown as Parameters<typeof priorityOf>[0];
  assert.equal(priorityOf(base)?.outcome?.key, 'defer');
  const named = { ...base, priority: { decisionId: 'd-2', outcome: { key: 'fund', label: 'Fund' }, status: { label: 'Accepted', meaning: 'accepted' }, recordUrl: 'https://example.org/d-2' } } as typeof base;
  assert.equal(priorityOf(named)?.outcome?.key, 'fund');
  assert.equal(priorityOf({ ...base, decisions: [] } as typeof base), null);
});

test('what the tier needs leads with the approvals this request meets: intake and the decision types its gates name', () => {
  const approval = (decisionTypeKey: string) => ({ decisionTypeKey, approverRoleKey: 'pmo', approvalMode: 'first-to-respond' as const, minimumCount: 1 });
  const split = splitApprovals({
    gates: [{ requirements: [{ decisionTypeKey: 'architecture' }, { decisionTypeKey: undefined }] }, { requirements: [{ decisionTypeKey: 'go-live' }] }],
    approvals: [approval('exception'), approval('intake'), approval('go-live'), approval('data-use'), approval('architecture')],
  });
  assert.deepEqual(split.now.map(a => a.decisionTypeKey), ['intake', 'go-live', 'architecture']);
  assert.deepEqual(split.later.map(a => a.decisionTypeKey), ['exception', 'data-use']);
});
