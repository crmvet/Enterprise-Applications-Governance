// The Demo guide, Decision log and Documents pages' logic: the story's links and coverage, the decision log's filters,
// counts, review dates and chains, and the documents' acts, owner counts and gate needs. Fictional people and
// patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appHref } from '../../app/routes';
import type { DecisionLogItem } from '../../data/contracts';
import {
  approvalsText,
  countByStatus,
  sentence,
  countByType,
  decisionChains,
  filterDecisions,
  isStanding,
  notAskedYet,
  proposedBy,
  reviewState,
  reviewText,
  subjectRoute,
} from './decisionLog';
import { COVERAGE_NOTES, DEMO_MINUTES, STORY_STEPS, coverageOf, lookupOf, personaOf, resolveLink, soonLabel, stepOf } from './demoStory';
import {
  actOf,
  countByOwner,
  countByType as countDocsByType,
  filterByOwner,
  gateNeedText,
  gateNeedsOf,
  mayAct,
  NO_OWNER,
  stateText,
  type ActRights,
  type DocRow,
} from './documents';

const APP_IM = '00000000-0000-4000-8000-00000000a001';
const APP_IS = '00000000-0000-4000-8000-00000000a002';
const PF_FO = '00000000-0000-4000-8000-00000000b001';
const NADIA = { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira', principalKind: 'user' as const };
const PRIYA = { objectId: '00000000-0000-4000-8000-000000000211', displayLabel: 'Priya Castell', principalKind: 'user' as const };
const TODAY = '2026-11-24';

// ---------------------------------------------------------------------------------------------------------------------
// Demo story
// ---------------------------------------------------------------------------------------------------------------------

test('the guide lists the 23 scenarios in order, S-01 to S-23, each with personas, steps, links and use cases', () => {
  assert.equal(STORY_STEPS.length, 23);
  STORY_STEPS.forEach((s, i) => {
    assert.equal(s.number, i + 1);
    assert.equal(s.id, `S-${String(i + 1).padStart(2, '0')}`);
    assert.ok(s.personas.length > 0);
    assert.ok(s.points.length > 0);
    assert.ok(s.links.length > 0);
    assert.ok(s.useCases.length > 0);
  });
});

test('each step carries the step ID of its scenario, in order, as the presenter scripts and the end-to-end tests do', () => {
  for (const s of STORY_STEPS) {
    s.points.forEach((p, i) => assert.equal(p.id, `${s.id}.${i + 1}`, `${s.id}: ${p.text}`));
    assert.equal(new Set(s.points.map(p => p.text)).size, s.points.length, `${s.id} has no repeated step`);
  }
});

test('S-01 to S-16 are the 15-minute story with a time each; S-17 to S-23 are the deep dive', () => {
  assert.equal(DEMO_MINUTES, 15);
  assert.deepEqual(STORY_STEPS.filter(s => s.time !== null).map(s => s.id), STORY_STEPS.slice(0, 16).map(s => s.id));
  assert.ok(STORY_STEPS.slice(16).every(s => s.time === null));
});

test('the data step opens the data pages by domain key, and every screen group is linked', () => {
  const routes = STORY_STEPS.flatMap(s => s.links.map(l => resolveLink(l, null).route));
  assert.ok(routes.some(r => r.page === 'data-domain' && r.key === 'payroll'));
  assert.ok(routes.some(r => r.page === 'data' && r.tab === 'gaps'));
  for (const page of ['data', 'configuration', 'roles', 'guidelines', 'measures', 'admin', 'teams', 'search', 'decisions', 'documents', 'intake-new', 'retirement', 'demand', 'agents', 'audit']) assert.ok(routes.some(r => r.page === page), page);
  assert.ok(routes.some(r => r.page === 'teams' && r.roleKey === 'change-board'));
  assert.deepEqual(STORY_STEPS[9].personas.map(p => p.name), ['yusuf', 'jonah']);
});

test('Marcus Oyelaran answers the design decision and the change board exception (wave 4b product review)', () => {
  assert.ok(STORY_STEPS[5].personas.some(p => p.name === 'marcus'));
  assert.ok(STORY_STEPS[15].personas.some(p => p.name === 'marcus'));
  assert.match(STORY_STEPS[5].points[2].text, /Marcus/);
  assert.match(STORY_STEPS[15].points[3].text, /Marcus/);
});

test('every link names a persona its step lists', () => {
  for (const s of STORY_STEPS) {
    for (const l of s.links) assert.ok(s.personas.some(p => p.name === personaOf(s, l)), `${s.title}: ${l.label}`);
  }
});

test('story text keeps to the style: no em or en dashes', () => {
  const text = JSON.stringify(STORY_STEPS) + JSON.stringify(COVERAGE_NOTES);
  assert.ok(!/[\u2013\u2014]/.test(text));
});

test('a link finds its application and portfolio by name, ignoring case and spacing', () => {
  const lookup = lookupOf(
    [{ name: 'Invoice  Matching', id: APP_IM.toUpperCase() }, { name: 'Inspection Scheduler', id: APP_IS }],
    [{ name: 'Field operations', id: PF_FO }],
  );
  const evidence = STORY_STEPS[14].links[0];
  const found = resolveLink(evidence, lookup);
  assert.equal(found.missing, null);
  assert.deepEqual(found.route, { page: 'evidence', subjectKind: 'business-application', id: APP_IM });
  const owners = resolveLink(STORY_STEPS[0].links[2], lookup);
  assert.deepEqual(owners.route, { page: 'owners', view: 'gaps', portfolioId: PF_FO });
});

test('a link whose name the register does not hold opens the closest list and says what is missing', () => {
  const lookup = lookupOf([], []);
  const evidence = resolveLink(STORY_STEPS[14].links[0], lookup);
  assert.equal(evidence.missing, 'Invoice Matching');
  assert.deepEqual(evidence.route, { page: 'applications', filter: { text: 'Invoice Matching' } });
  const portfolio = resolveLink(STORY_STEPS[8].links[2], null);
  assert.equal(portfolio.missing, 'Field operations');
  assert.deepEqual(portfolio.route, { page: 'portfolios' });
});

test('every resolved link builds an address in the web app', () => {
  const lookup = lookupOf(
    ['Inspection Scheduler', 'Crew Roster', 'Invoice Matching', 'Claims Triage Agent', 'Visit Report Agent', 'Supplier Price Agent', 'Legacy Timesheet App', 'Quote Builder', 'Field Knowledge MCP Server', 'Route Planner', 'Identity Directory Sync', 'Finance Ledger', 'Contractor Portal']
      .map((name, i) => ({ name, id: `00000000-0000-4000-8000-0000000000${String(10 + i)}` })),
    [{ name: 'Field operations', id: PF_FO }],
  );
  for (const s of STORY_STEPS) {
    for (const l of s.links) {
      const { route, missing } = resolveLink(l, lookup);
      assert.equal(missing, null, l.label);
      const href = appHref(route, '/app/');
      assert.ok(href.startsWith('/app/') && href !== '/app/', `${l.label}: ${href}`);
    }
  }
});

test('a link to one section of a page carries its parameter only when it found its application', () => {
  const data = STORY_STEPS[6].links.find(l => l.params)!;
  const lookup = lookupOf([{ name: 'Route Planner', id: APP_IS }], []);
  const found = resolveLink(data, lookup);
  assert.deepEqual(found.route, { page: 'app', id: APP_IS });
  assert.deepEqual(found.params, { tab: 'data' });
  const fallback = resolveLink(data, lookupOf([], []));
  assert.equal(fallback.missing, 'Route Planner');
  assert.equal(fallback.params, null);
  assert.equal(resolveLink(STORY_STEPS[0].links[0], lookup).params, null);
});

test('coverage lists each use case once with its steps in order, processes first', () => {
  const coverage = coverageOf(STORY_STEPS);
  const keys = coverage.map(c => c.useCase);
  assert.equal(new Set(keys).size, keys.length);
  assert.deepEqual(coverage.find(c => c.useCase === 'P01')?.steps, [2, 3, 20, 22, 23]);
  assert.deepEqual(coverage.find(c => c.useCase === 'P10')?.steps, [2, 7, 8, 15, 21, 23]);
  assert.deepEqual(coverage.find(c => c.useCase === 'A-DEPENDENCIES')?.steps, [5, 17, 22]);
  // Each wave 3 screen has its own scenario.
  assert.deepEqual(coverage.find(c => c.useCase === 'A-MEASURES')?.steps, [13, 23]);
  assert.deepEqual(coverage.find(c => c.useCase === 'A-ADMIN')?.steps, [14]);
  assert.ok(keys.indexOf('P01') < keys.indexOf('UC-01'));
  assert.ok(keys.indexOf('UC-14') < keys.indexOf('A-AGENTS'));
  assert.ok(keys.indexOf('UC-02') < keys.indexOf('UC-10'));
});

test('the scenario in the address is a known place in the guide, or nothing', () => {
  assert.equal(stepOf('3'), 3);
  assert.equal(stepOf('22'), 22);
  assert.equal(stepOf('23'), 23);
  assert.equal(stepOf('24'), null);
  assert.equal(stepOf('0'), null);
  assert.equal(stepOf(undefined), null);
});

// ---------------------------------------------------------------------------------------------------------------------
// Decision log
// ---------------------------------------------------------------------------------------------------------------------

let n = 0;
function decision(over: Partial<DecisionLogItem> = {}): DecisionLogItem {
  n++;
  const id = `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
  return {
    decisionId: id,
    decisionType: { key: 'risk-acceptance', label: 'Risk acceptance' },
    subjectKind: 'business-application',
    subjectId: APP_IS,
    subjectLabel: 'Inspection Scheduler',
    statement: `Decision ${n}`,
    reason: 'Because.',
    exception: null,
    outcome: { key: 'accept', label: 'Accept' },
    status: { label: 'Accepted', meaning: 'accepted' },
    decider: NADIA,
    decidedOn: `2026-11-${String(10 + (n % 10)).padStart(2, '0')}T09:00:00Z`,
    supersedes: null,
    supersededBy: null,
    amends: null,
    approvalsSummary: { required: 1, approved: 1, rejected: 0, pending: 0 },
    configurationSetVersion: 1,
    proposedBy: NADIA,
    isAiCreated: false,
    performedBy: null,
    recordUrl: `https://eag.example.org/app/records/decision/${id}`,
    ...over,
  };
}

const aiStamp = {
  actorKind: 'ai-client-for-person' as const,
  principal: PRIYA,
  onBehalfOf: PRIYA,
  clientAppLabel: 'Claude Code',
  stampedOn: '2026-11-23T10:00:00Z',
};

test('the page filters by status, by AI-made and by review window, and counts every status', () => {
  const proposed = decision({ status: { label: 'Proposed', meaning: 'proposed' }, decidedOn: null, approvalsSummary: { required: 2, approved: 1, rejected: 0, pending: 1 } });
  const ai = decision({ status: { label: 'Proposed', meaning: 'proposed' }, isAiCreated: true, performedBy: aiStamp, decidedOn: null });
  const accepted = decision();
  const rejected = decision({ status: { label: 'Rejected', meaning: 'rejected' } });
  const all = [proposed, ai, accepted, rejected];
  assert.deepEqual(filterDecisions(all, { statusMeanings: ['proposed'], today: TODAY }), [proposed, ai]);
  assert.deepEqual(filterDecisions(all, { aiOnly: true, today: TODAY }), [ai]);
  assert.deepEqual(filterDecisions(all, { statusMeanings: ['rejected', 'accepted'], today: TODAY }), [accepted, rejected]);
  assert.deepEqual(countByStatus(all), { proposed: 2, accepted: 1, rejected: 1, superseded: 0, withdrawn: 0 });
  // An AI stamp alone marks the row, as the AI mark does.
  assert.deepEqual(filterDecisions([decision({ performedBy: aiStamp })], { aiOnly: true, today: TODAY }).length, 1);
});

test('an exception stands while accepted and not superseded, and its review is overdue, soon or later', () => {
  const ex = (reviewOn: string, over: Partial<DecisionLogItem> = {}) => decision({ decisionType: { key: 'exception', label: 'Exception' }, exception: { standard: 'A person reviews the code.', reviewOn }, ...over });
  const overdue = ex('2026-11-20');
  const soon = ex('2026-12-04');
  const later = ex('2027-03-01');
  const superseded = ex('2026-11-20', { supersededBy: decision().decisionId });
  const proposed = ex('2026-11-20', { status: { label: 'Proposed', meaning: 'proposed' } });
  assert.equal(reviewState(overdue, TODAY), 'overdue');
  assert.equal(reviewState(soon, TODAY), 'soon');
  assert.equal(reviewState(later, TODAY), 'later');
  assert.equal(isStanding(superseded), false);
  assert.equal(reviewState(superseded, TODAY), 'none');
  assert.equal(reviewState(proposed, TODAY), 'none');
  assert.equal(reviewText(overdue, TODAY), 'Review overdue by 4 days');
  assert.equal(reviewText(soon, TODAY), 'Review in 10 days');
  assert.equal(reviewText(ex(TODAY), TODAY), 'Review due today');
  assert.deepEqual(filterDecisions([overdue, soon, later], { review: 'overdue', today: TODAY }), [overdue]);
  assert.deepEqual(filterDecisions([overdue, soon, later], { review: 'soon', today: TODAY }), [soon]);
});

test('decision types are counted most first', () => {
  const a = decision({ decisionType: { key: 'design', label: 'Design' } });
  const b = decision({ decisionType: { key: 'tier', label: 'Tier' } });
  const c = decision({ decisionType: { key: 'tier', label: 'Tier' } });
  assert.deepEqual(countByType([a, b, c]), [
    { key: 'tier', label: 'Tier', count: 2 },
    { key: 'design', label: 'Design', count: 1 },
  ]);
});

test('approvals read in words, and a proposal nobody was asked about is not asked yet', () => {
  assert.equal(approvalsText({ required: 2, approved: 1, rejected: 0, pending: 1 }), '1 of 2 approvals in, 1 request open');
  assert.equal(approvalsText({ required: 1, approved: 0, rejected: 0, pending: 4 }), '0 of 1 approval in, 4 requests open, consultations included');
  assert.equal(sentence('A person reviews the code.'), 'A person reviews the code.');
  assert.equal(sentence(' No full stop '), 'No full stop.');
  assert.equal(approvalsText({ required: 1, approved: 0, rejected: 1, pending: 0 }), '0 of 1 approval in, 1 rejected');
  assert.equal(approvalsText({ required: 0, approved: 0, rejected: 0, pending: 0 }), null);
  const saved = decision({ status: { label: 'Proposed', meaning: 'proposed' }, approvalsSummary: { required: 0, approved: 0, rejected: 0, pending: 0 } });
  assert.equal(notAskedYet(saved), true);
  assert.equal(notAskedYet(decision({ status: { label: 'Proposed', meaning: 'proposed' }, approvalsSummary: { required: 1, approved: 0, rejected: 0, pending: 1 } })), false);
  assert.equal(notAskedYet(decision({ approvalsSummary: { required: 0, approved: 0, rejected: 0, pending: 0 } })), false);
});

test('the person proposed a decision, or an AI client drafted it for them', () => {
  const mine = decision({ proposedBy: NADIA });
  const forPriya = decision({ proposedBy: PRIYA, isAiCreated: true, performedBy: aiStamp });
  assert.equal(proposedBy(mine, NADIA.objectId.toUpperCase()), true);
  assert.equal(proposedBy(mine, PRIYA.objectId), false);
  assert.equal(proposedBy(forPriya, PRIYA.objectId), true);
  assert.equal(proposedBy(mine, null), false);
});

test('a subject opens its own page where the web app has one', () => {
  assert.deepEqual(subjectRoute({ subjectKind: 'business-application', subjectId: APP_IS }), { page: 'app', id: APP_IS });
  assert.deepEqual(subjectRoute({ subjectKind: 'deployment-record', subjectId: APP_IM }), { page: 'deployment-record', id: APP_IM });
  assert.equal(subjectRoute({ subjectKind: 'technical-component', subjectId: APP_IM }), null);
});

test('supersede and amend chains run oldest first, and a decision outside the rows still shows as a link', () => {
  const first = decision();
  const second = decision({ supersedes: first.decisionId, supersededBy: null });
  first.supersededBy = second.decisionId;
  const third = decision({ amends: second.decisionId });
  const outside = '00000000-0000-4000-8000-0000000fffff';
  const lone = decision({ supersedes: outside });
  const plain = decision();
  const chains = decisionChains([plain, third, second, first, lone]);
  assert.equal(chains.length, 2);
  const long = chains.find(c => c.length === 3)!;
  assert.deepEqual(long.map(c => [c.id, c.how]), [[first.decisionId, 'first'], [second.decisionId, 'supersedes'], [third.decisionId, 'amends']]);
  const short = chains.find(c => c.length === 2)!;
  assert.equal(short[0].item, null);
  assert.equal(short[0].id, outside);
  assert.equal(short[1].how, 'supersedes');
});

test('a chain never loops on bad links', () => {
  const a = decision();
  const b = decision({ supersedes: a.decisionId, supersededBy: a.decisionId });
  a.supersedes = b.decisionId;
  a.supersededBy = b.decisionId;
  const chains = decisionChains([a, b]);
  for (const c of chains) assert.ok(c.length <= 3);
});

// ---------------------------------------------------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------------------------------------------------

let d = 0;
function doc(over: Partial<DocRow> = {}): DocRow {
  d++;
  const id = `00000000-0000-4000-8000-${String(500 + d).padStart(12, '0')}`;
  return {
    businessApplicationId: APP_IS,
    applicationName: 'Inspection Scheduler',
    documentType: { key: 'runbook', label: 'Runbook' },
    state: 'current',
    documentLinkId: id,
    url: 'https://wiki.example.org/runbook',
    owner: NADIA,
    lastCheckedOn: '2026-10-01',
    staleOn: '2027-03-30',
    requirementKey: 'runbook-tier-2',
    requiredFrom: { key: 'change-and-release', label: 'Change and release' },
    isAiCreated: false,
    performedBy: null,
    recordUrl: `https://eag.example.org/app/records/document-link/${id}`,
    ...over,
  };
}

const rights = (over: Partial<ActRights> = {}): ActRights => ({
  me: NADIA.objectId,
  ownedApps: new Set<string>(),
  otherRole: false,
  canLink: true,
  canConfirm: true,
  ...over,
});

test('each document state offers the act that moves it on', () => {
  assert.equal(actOf(doc({ state: 'missing', documentLinkId: null, owner: null })), 'link');
  assert.equal(actOf(doc({ state: 'draft' })), 'confirm-current');
  assert.equal(actOf(doc({ state: 'stale' })), 'confirm-still-true');
  assert.equal(actOf(doc({ state: 'current' })), 'confirm-still-true');
  assert.equal(actOf(doc({ state: 'other' })), null);
  assert.equal(actOf(doc({ state: 'draft', documentLinkId: null })), null);
});

test('an act shows to the document owner, an owner of the application, or a person with another role', () => {
  const missing = doc({ state: 'missing', documentLinkId: null, owner: null, businessApplicationId: APP_IM });
  assert.equal(mayAct(missing, 'link', rights()), false);
  assert.equal(mayAct(missing, 'link', rights({ ownedApps: new Set([APP_IM]) })), true);
  assert.equal(mayAct(missing, 'link', rights({ otherRole: true })), true);
  assert.equal(mayAct(missing, 'link', rights({ otherRole: true, canLink: false })), false);
  const stale = doc({ state: 'stale', owner: NADIA, businessApplicationId: APP_IM });
  assert.equal(mayAct(stale, 'confirm-still-true', rights()), true);
  assert.equal(mayAct(stale, 'confirm-still-true', rights({ me: PRIYA.objectId })), false);
  assert.equal(mayAct(stale, 'confirm-still-true', rights({ canConfirm: false })), false);
});

test('rows count by type and by owner, with no owner last, and the owner filter keeps one owner', () => {
  const rows = [
    doc({ owner: NADIA }),
    doc({ owner: NADIA, documentType: { key: 'wiki-page', label: 'Wiki page' } }),
    doc({ owner: PRIYA }),
    doc({ state: 'missing', owner: null, documentLinkId: null }),
    doc({ state: 'missing', owner: null, documentLinkId: null }),
    doc({ state: 'missing', owner: null, documentLinkId: null }),
  ];
  assert.deepEqual(countByOwner(rows).map(b => [b.key, b.count]), [[NADIA.objectId, 2], [PRIYA.objectId, 1], [NO_OWNER, 3]]);
  assert.deepEqual(countDocsByType(rows).map(b => [b.key, b.count]), [['runbook', 5], ['wiki-page', 1]]);
  assert.equal(filterByOwner(rows, NO_OWNER).length, 3);
  assert.equal(filterByOwner(rows, PRIYA.objectId.toUpperCase()).length, 1);
  assert.equal(filterByOwner(rows, undefined).length, 6);
});

test('the state reads in words', () => {
  assert.equal(stateText(doc({ state: 'missing', documentLinkId: null }), TODAY), 'Required from the change and release stage. No link recorded.');
  assert.match(stateText(doc({ state: 'stale', staleOn: '2026-11-01' }), TODAY), /^Stale since /);
  assert.equal(stateText(doc({ state: 'stale', staleOn: null, lastCheckedOn: null }), TODAY), 'Stale: no check or update date is recorded to count its freshness from.');
  assert.equal(stateText(doc({ state: 'stale', staleOn: null }), TODAY), 'Marked stale.');
  assert.match(stateText(doc({ state: 'current', staleOn: '2027-03-30' }), TODAY), /^Current until /);
  assert.equal(stateText(doc({ state: 'current', staleOn: null }), TODAY), 'Current. This type does not turn stale by date.');
  assert.equal(stateText(doc({ state: 'draft' }), TODAY), 'Recorded, not confirmed current yet.');
});

test('the gates that need a document type current read from the gate requirements in force', () => {
  const rows = [
    { isActive: true, fields: { gateKey: 'go-live', tierKey: 'tier-2', requirementKind: 'document-current', requiredDocumentTypeKey: 'runbook' } },
    { isActive: true, fields: { gateKey: 'go-live', tierKey: 'tier-3', requirementKind: 'document-current', requiredDocumentTypeKey: 'runbook' } },
    { isActive: true, fields: { gateKey: 'design', tierKey: 'tier-3', requirementKind: 'document-current', requiredDocumentTypeKey: 'architecture-decision-record' } },
    { isActive: false, fields: { gateKey: 'value', requirementKind: 'document-current', requiredDocumentTypeKey: 'runbook' } },
    { isActive: true, fields: { gateKey: 'go-live', requirementKind: 'review-done', requiredReviewTypeKey: 'security-review' } },
  ];
  const needs = gateNeedsOf(rows, 'runbook');
  assert.equal(needs.length, 2);
  const label = { gate: (k: string) => (k === 'go-live' ? 'Go-live' : k), tier: (k: string) => (k === 'tier-2' ? 'Tier 2' : 'Tier 3'), kind: (k: string) => k };
  assert.deepEqual(gateNeedText(needs, label), ['Go-live gate, at Tier 2 and Tier 3']);
  assert.deepEqual(gateNeedText([{ gateKey: 'go-live', tierKey: null, applicationKindKey: null }], label), ['Go-live gate, for every application']);
  assert.deepEqual(gateNeedsOf(rows, 'support-guide'), []);
});

test('the freshness figure says expires for one request and expire for several (wave 4b QA)', () => {
  assert.equal(soonLabel(1, 48), 'expires within 48 hours');
  assert.equal(soonLabel(2, 48), 'expire within 48 hours');
  assert.equal(soonLabel(0, 1), 'expire within 1 hour');
});
