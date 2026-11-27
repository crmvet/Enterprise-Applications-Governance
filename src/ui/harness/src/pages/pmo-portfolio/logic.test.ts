// The PMO and portfolio pages' logic: who may propose, the demand board's figures and filters, the portfolio tree and
// its form, and the retirement steps. Fictional names and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ClaimItem, DecisionLogItem, ImpactNode, IntakeSummary, PortfolioItem, StatusValue } from '../../data/contracts';
import {
  bucketsBy,
  buildPortfolioInput,
  demandFigures,
  demandInput,
  formOfPortfolio,
  isProblem,
  mayPropose,
  needsOwnRead,
  ownerRolesOf,
  portfolioOfApp,
  portfolioTotals,
  portfolioTree,
  searchTree,
  priorityState,
  removalChange,
  retirementSteps,
  topPortfolioOf,
  vanishedClaimFor,
  wayOutDecision,
  wayOutRows,
  withDescendants,
  type CardComponent,
  type PackChange,
  type PackLink,
  type PortfolioInput,
  type RightRow,
} from './logic';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const APP = id(9001);
const OTHER_APP = id(9002);
const ME = id(214);
const status = (meaning: StatusValue['meaning'], label?: string): StatusValue => ({ meaning, label: label ?? meaning.charAt(0).toUpperCase() + meaning.slice(1) });

// ---------- decision rights ----------

const right = (fields: Record<string, unknown>, isActive = true): RightRow => ({ isActive, fields });
const RIGHTS: RightRow[] = [
  right({ decisionTypeKey: 'portfolio', letter: 'accountable', ownerRoleKey: 'business-owner' }),
  right({ decisionTypeKey: 'portfolio', letter: 'responsible', roleKey: 'pmo' }),
  right({ decisionTypeKey: 'retirement', letter: 'responsible', roleKey: 'platform-admin' }),
  right({ decisionTypeKey: 'change-authorization', letter: 'supports' }),
  right({ decisionTypeKey: 'data-use', letter: 'accountable', ownerRoleKey: 'data-owner' }),
  right({ decisionTypeKey: 'priority', letter: 'informed', roleKey: 'reader' }),
  right({ decisionTypeKey: 'priority', letter: 'responsible', roleKey: 'auditor' }, false),
];

test('a product role or an owner role on the application with a proposing letter may propose', () => {
  assert.equal(mayPropose(RIGHTS, 'portfolio', ['pmo'], []), true);
  assert.equal(mayPropose(RIGHTS, 'portfolio', ['maker'], ['business-owner']), true);
  assert.equal(mayPropose(RIGHTS, 'portfolio', ['maker'], ['technical-owner']), false);
  assert.equal(mayPropose(RIGHTS, 'retirement', ['pmo'], []), false);
  assert.equal(mayPropose(RIGHTS, 'data-use', [], ['data-owner']), true);
  // An informed letter does not propose, and an inactive row counts for nothing.
  assert.equal(mayPropose(RIGHTS, 'priority', ['reader'], []), false);
  assert.equal(mayPropose(RIGHTS, 'priority', ['auditor'], []), false);
  assert.equal(mayPropose([], 'portfolio', ['pmo'], []), false);
});

test('the owner roles a person holds on an application come from its current owners, by object ID in any case', () => {
  const owners = [
    { ownerRoleKey: 'business-owner', principal: { objectId: ME.toUpperCase() } },
    { ownerRoleKey: 'data-owner', principal: { objectId: ME } },
    { ownerRoleKey: 'technical-owner', principal: { objectId: id(1) } },
  ];
  assert.deepEqual(ownerRolesOf(owners, ME), ['business-owner', 'data-owner']);
  assert.deepEqual(ownerRolesOf(owners, null), []);
  assert.deepEqual(ownerRolesOf(undefined, ME), []);
});

// ---------- demand ----------

let nextIntake = 0;
function intake(over: Partial<IntakeSummary> = {}): IntakeSummary {
  nextIntake++;
  return {
    intakeRequestId: id(7000 + nextIntake),
    recordNumber: `IR-${String(100 + nextIntake).padStart(4, '0')}`,
    title: `Request ${nextIntake}`,
    status: status('submitted'),
    requester: null,
    proposedKind: null,
    businessApplicationId: null,
    applicationName: null,
    duplicateOfId: null,
    usesAi: false,
    horizon: null,
    sizeBand: null,
    score: null,
    priority: null,
    suggestedTier: null,
    triageDecision: null,
    decisions: [],
    isAiCreated: false,
    performedBy: null,
    recordUrl: 'https://example.test/app/records/intake-request/x',
    ...over,
  };
}

const priority = (meaning: StatusValue['meaning'], outcome: string | null) => ({
  decisionId: id(8000 + nextIntake),
  outcome: outcome ? { key: outcome, label: outcome.charAt(0).toUpperCase() + outcome.slice(1) } : null,
  status: status(meaning),
  recordUrl: 'https://example.test/app/records/decision/x',
});

test('the demand filters become the list input; the default order needs no read of its own', () => {
  assert.deepEqual(demandInput({ sort: 'score' }), { view: 'demand', top: 100, orderBy: 'score' });
  assert.deepEqual(demandInput({ sort: 'horizon', sizeBand: 'none', horizon: 'now', priority: 'proposed', scored: 'no' }), {
    view: 'demand', top: 100, orderBy: 'horizon', sizeBandKeys: ['none'], horizonKeys: ['now'], priorityKeys: ['proposed'], scored: false,
  });
  assert.equal(needsOwnRead({ sort: 'score' }), false);
  // Wave 4: the search goes to the tool's text.
  assert.equal(demandInput({ sort: 'score', text: 'crew' }).text, 'crew');
  assert.equal(needsOwnRead({ sort: 'score', text: 'crew' }), true);
  // The portfolio filter is applied on the page: no tool takes it.
  assert.equal(needsOwnRead({ sort: 'score', portfolio: APP }), false);
  assert.equal(needsOwnRead({ sort: 'title' }), true);
  assert.equal(needsOwnRead({ sort: 'score', scored: 'yes' }), true);
});

test('demand figures count the table, the unscored, the waiting and the decided priorities', () => {
  const items = [
    intake({ score: 72 }),
    intake({ score: null, priority: priority('proposed', 'fund') }),
    intake({ score: 40, priority: priority('accepted', 'fund') }),
    intake({ score: null, priority: priority('withdrawn', 'defer') }),
  ];
  assert.deepEqual(demandFigures(items), { onTable: 4, toScore: 2, scored: 2, waiting: 1, decided: 1 });
  assert.equal(priorityState(items[0]), 'none');
  assert.equal(priorityState(items[1]), 'proposed');
  assert.equal(priorityState(items[2]), 'accepted');
  assert.equal(priorityState(items[3]), 'other');
});

test('buckets follow the configured order, then unknown keys, then "not set" last', () => {
  const items = [{ k: 'later' }, { k: null }, { k: 'now' }, { k: 'now' }, { k: 'someday' }];
  const order = [{ key: 'now', label: 'Now' }, { key: 'next', label: 'Next' }, { key: 'later', label: 'Later' }];
  assert.deepEqual(bucketsBy(items, i => i.k, order, 'Not set'), [
    { key: 'now', label: 'Now', count: 2 },
    { key: 'later', label: 'Later', count: 1 },
    { key: 'someday', label: 'someday', count: 1 },
    { key: null, label: 'Not set', count: 1 },
  ]);
});

// ---------- portfolios ----------

function portfolio(n: number, name: string, parent: PortfolioItem | null, counts: Partial<PortfolioItem['counts']> = {}, over: Partial<PortfolioItem> = {}): PortfolioItem {
  const pid = id(1200 + n);
  return {
    portfolioId: pid,
    name,
    level: null,
    parentPortfolioId: parent?.portfolioId ?? null,
    path: [...(parent?.path ?? []), { portfolioId: pid, name }],
    accountablePrincipal: null,
    isActive: true,
    counts: { applications: 0, applicationsWithOwnerGap: 0, reviewsOverdue: 0, openDecisions: 0, ...counts },
    recordUrl: 'https://example.test/app/records/portfolio/x',
    ...over,
  };
}

const FINANCE = portfolio(0, 'Finance', null, { applications: 5, applicationsWithOwnerGap: 2, reviewsOverdue: 1, openDecisions: 3 });
const PAYABLES = portfolio(1, 'Accounts payable', FINANCE, { applications: 3, applicationsWithOwnerGap: 1 });
const PEOPLE = portfolio(2, 'People and workplace', null, { applications: 4, reviewsOverdue: 2 });
const CLAIMS = portfolio(3, 'Claims', PAYABLES, { applications: 1 });

test('the tree puts each parent before its children and siblings by name; totals add the top of the tree only', () => {
  const rows = portfolioTree([CLAIMS, PEOPLE, PAYABLES, FINANCE]);
  assert.deepEqual(rows.map(r => `${r.depth}:${r.item.name}`), ['0:Finance', '1:Accounts payable', '2:Claims', '0:People and workplace']);
  assert.deepEqual(portfolioTotals([FINANCE, PAYABLES, PEOPLE, CLAIMS]), { applications: 9, applicationsWithOwnerGap: 2, ownerGaps: 2, reviewsOverdue: 3, openDecisions: 3 });
  // The one owner gap count (B24-17) adds up when the read gives it.
  assert.equal(portfolioTotals([{ ...FINANCE, counts: { ...FINANCE.counts, ownerGaps: 5 } }, PEOPLE]).ownerGaps, 5);
  assert.deepEqual([...withDescendants([FINANCE, PAYABLES, PEOPLE, CLAIMS], FINANCE.portfolioId)].sort(), [FINANCE.portfolioId, PAYABLES.portfolioId, CLAIMS.portfolioId].sort());
});

test('an application sits in the deepest portfolio that lists it, under the top of its tree', () => {
  const all = [FINANCE, PAYABLES, PEOPLE];
  const members = new Map<string, Set<string>>([
    [FINANCE.portfolioId, new Set([APP, OTHER_APP])],
    [PAYABLES.portfolioId, new Set([APP])],
    [PEOPLE.portfolioId, new Set()],
  ]);
  assert.equal(portfolioOfApp(APP.toUpperCase(), members, all)?.name, 'Accounts payable');
  assert.equal(portfolioOfApp(OTHER_APP, members, all)?.name, 'Finance');
  assert.equal(portfolioOfApp(id(1), members, all), null);
  assert.equal(portfolioOfApp(null, members, all), null);
  assert.equal(topPortfolioOf(PAYABLES, all)?.name, 'Finance');
  assert.equal(topPortfolioOf(null, all), null);
});

const asInput = (built: ReturnType<typeof buildPortfolioInput>): PortfolioInput => {
  if (isProblem(built)) throw new Error(built.problem);
  return built;
};

test('a new portfolio sends what is filled; only the platform admin sends the parent and the accountable person', () => {
  const form = { ...formOfPortfolio(null, FINANCE.portfolioId), name: ' Treasury ', levelKey: 'application-group', accountable: { objectId: ME, displayLabel: 'Mateo Brandt' } };
  assert.deepEqual(asInput(buildPortfolioInput(form, null, true)), {
    name: 'Treasury', levelKey: 'application-group', parentPortfolioId: FINANCE.portfolioId, accountablePrincipalObjectId: ME,
  });
  assert.deepEqual(asInput(buildPortfolioInput(form, null, false)), { name: 'Treasury', levelKey: 'application-group' });
  const empty = buildPortfolioInput({ ...form, name: '  ' }, null, true);
  assert.ok(isProblem(empty) && empty.field === 'name');
});

test('a change sends only what differs; the PMO never sends the parent or the accountable person', () => {
  const existing = { ...PAYABLES, level: { key: 'application-group', label: 'Application group' }, accountablePrincipal: { objectId: ME, displayLabel: 'Mateo Brandt' } };
  const same = formOfPortfolio(existing);
  assert.equal(same.parentPortfolioId, FINANCE.portfolioId);
  const nothing = buildPortfolioInput(same, existing, true);
  assert.ok(isProblem(nothing));

  const renamed = asInput(buildPortfolioInput({ ...same, name: 'Payables', parentPortfolioId: '', accountable: null }, existing, false));
  assert.deepEqual(renamed, { portfolioId: existing.portfolioId, name: 'Payables' });

  const moved = asInput(buildPortfolioInput({ ...same, parentPortfolioId: '', accountable: null, description: 'Supplier invoices.' }, existing, true));
  assert.deepEqual(moved, { portfolioId: existing.portfolioId, description: 'Supplier invoices.', clearParent: true, clearAccountable: true });

  const elsewhere = asInput(buildPortfolioInput({ ...same, parentPortfolioId: PEOPLE.portfolioId, accountable: { objectId: id(5), displayLabel: 'Lena Okoro' } }, existing, true));
  assert.deepEqual(elsewhere, { portfolioId: existing.portfolioId, parentPortfolioId: PEOPLE.portfolioId, accountablePrincipalObjectId: id(5) });

  const loop = buildPortfolioInput({ ...same, parentPortfolioId: existing.portfolioId }, existing, true);
  assert.ok(isProblem(loop) && loop.field === 'parentPortfolioId');
});

// ---------- retirement ----------

let nextDecision = 0;
function decision(type: string, outcome: string, meaning: StatusValue['meaning'], decidedOn: string | null, over: Partial<DecisionLogItem> = {}): DecisionLogItem {
  nextDecision++;
  return {
    decisionId: id(4000 + nextDecision),
    decisionType: { key: type, label: type.charAt(0).toUpperCase() + type.slice(1) },
    subjectKind: 'business-application',
    subjectId: APP,
    subjectLabel: 'Legacy Timesheet App',
    statement: `${type} ${outcome}`,
    reason: 'Because.',
    exception: null,
    outcome: { key: outcome, label: outcome.charAt(0).toUpperCase() + outcome.slice(1) },
    status: status(meaning),
    decider: null,
    decidedOn,
    supersedes: null,
    supersededBy: null,
    amends: null,
    approvalsSummary: { required: 0, approved: 0, rejected: 0, pending: 0 },
    configurationSetVersion: 1,
    proposedBy: null,
    isAiCreated: false,
    performedBy: null,
    recordUrl: 'https://example.test/app/records/decision/x',
    ...over,
  };
}

function change(title: string, meaning: StatusValue['meaning'] | null, windowStart: string | null, n: number): PackChange {
  return {
    changeRequestId: id(6000 + n),
    recordNumber: `CHG-${String(n).padStart(4, '0')}`,
    title,
    changeType: { key: 'normal', label: 'Normal' },
    status: meaning ? status(meaning) : null,
    result: null,
    windowStart,
    windowEnd: null,
    authorizationDecisionId: null,
    isAiCreated: false,
    performedBy: null,
    configurationSetVersion: 1,
    recordUrl: 'https://example.test/app/records/change-request/x',
  };
}

const component = (over: Partial<CardComponent> = {}): CardComponent => ({
  technicalComponentId: id(3040),
  kind: { key: 'canvas-app', label: 'Canvas app' },
  externalId: 'app-8c47a2',
  url: 'https://make.example.org/apps/app-8c47a2',
  labelAsSeen: 'Timesheets',
  lastSeenOn: '2026-11-20T06:00:00Z',
  ...over,
});

const dependant = (name: string, meaning: ImpactNode['lifecycleMeaning'] = 'live'): ImpactNode => ({
  businessApplicationId: id(9100), name, tier: null, lifecycleMeaning: meaning, businessOwner: null, technicalOwner: null,
  dependencyTypeKey: 'reads-data-from', criticalityKey: null, depth: 1, path: [],
});

test('the way-out decision is the latest accepted retire or replace; a later keep overrules it', () => {
  const replace = decision('portfolio', 'replace', 'accepted', '2026-06-15T10:00:00Z');
  assert.equal(wayOutDecision([replace]).decision, replace);
  const keep = decision('portfolio', 'keep', 'accepted', '2026-09-01T10:00:00Z');
  const off = wayOutDecision([replace, keep]);
  assert.equal(off.decision, null);
  assert.equal(off.overruledBy, keep);
  // A retire proposal waits on top of an accepted keep.
  const proposal = decision('portfolio', 'retire', 'proposed', null);
  assert.equal(wayOutDecision([replace, keep, proposal]).decision, proposal);
  // A superseded or rejected decision never counts.
  assert.equal(wayOutDecision([decision('portfolio', 'retire', 'accepted', '2026-01-01T00:00:00Z', { supersededBy: id(1) })]).decision, null);
  assert.equal(wayOutDecision([decision('portfolio', 'retire', 'rejected', '2026-01-01T00:00:00Z')]).decision, null);
});

test('the removal change says it removes, or is planned since the way-out decision; failed ones are left out', () => {
  const changes = [
    change('Patch the export', 'completed', '2026-05-01T08:00:00Z', 1),
    change('Quarterly maintenance', 'submitted', '2026-07-01T08:00:00Z', 2),
    change('Remove the Timesheets app', 'rejected', '2026-08-01T08:00:00Z', 3),
  ];
  assert.equal(removalChange(changes, null), null);
  assert.equal(removalChange(changes, '2026-06-15')?.title, 'Quarterly maintenance');
  const named = [...changes, change('Decommission the Timesheets app', 'draft', null, 4)];
  assert.equal(removalChange(named, null)?.title, 'Decommission the Timesheets app');
});

test('a vanished claim matches its component by the link, or by the name and kind', () => {
  const claim = (over: Partial<ClaimItem>): ClaimItem => ({
    claimId: id(5000), findingKind: 'vanished', status: status('open'), componentKind: null, labelAsSeen: 'Other', targetAsSeen: null,
    url: null, suggestedClaimant: null, foundOn: '2026-11-20T06:00:00Z', lastSeenOn: null, ...over,
  });
  const byUrl = claim({ url: 'https://make.example.org/apps/app-8c47a2' });
  assert.equal(vanishedClaimFor(component(), [byUrl]), byUrl);
  const byName = claim({ labelAsSeen: 'Timesheets', componentKind: { key: 'canvas-app', label: 'Canvas app' } });
  assert.equal(vanishedClaimFor(component(), [byName]), byName);
  assert.equal(vanishedClaimFor(component(), [claim({ labelAsSeen: 'Timesheets', componentKind: { key: 'flow', label: 'Flow' } })]), null);
  assert.equal(vanishedClaimFor(component(), [claim({ url: 'https://make.example.org/apps/app-8c47a2', status: status('dismissed') })]), null);
  assert.equal(vanishedClaimFor(component(), [claim({ url: 'https://make.example.org/apps/app-8c47a2', findingKind: 'unregistered' })]), null);
});

test('a retired application with nothing else on record shows what is left', () => {
  // The demo's Legacy Timesheet App: replace accepted, retirement complete accepted, its canvas app still linked.
  const picture = retirementSteps({
    decisions: [
      decision('portfolio', 'replace', 'accepted', '2026-06-15T10:00:00Z'),
      decision('retirement', 'complete', 'accepted', '2026-10-01T09:00:00Z'),
    ],
    dependants: [],
    changes: [],
    evidenceLinks: [],
    components: [component()],
    retiredStage: true,
  });
  assert.equal(picture.overall, 'retired');
  assert.deepEqual(picture.steps.map(s => `${s.number}:${s.state}`), ['1:done', '2:done', '3:to-do', '4:to-do', '5:to-do', '6:to-do', '7:done']);
  assert.deepEqual(picture.left.map(s => s.key), ['data', 'change', 'evidence', 'gap-check']);
});

test('before any decision the later steps wait, and a live dependant only shows once the way out is decided', () => {
  const before = retirementSteps({ decisions: [], dependants: [dependant('Purchase Approvals')], changes: [], evidenceLinks: [], components: [component()], retiredStage: false });
  assert.equal(before.overall, 'not-out');
  assert.deepEqual(before.steps.map(s => s.state), ['to-do', 'not-yet', 'not-yet', 'not-yet', 'not-yet', 'not-yet', 'not-yet']);

  const decided = retirementSteps({
    decisions: [decision('portfolio', 'retire', 'accepted', '2026-11-01T09:00:00Z')],
    dependants: [dependant('Purchase Approvals'), dependant('Old Archive', 'retired')].filter(n => n.lifecycleMeaning !== 'retired'),
    changes: [],
    evidenceLinks: [],
    components: [],
    retiredStage: false,
  });
  assert.equal(decided.overall, 'on-the-way');
  assert.deepEqual(decided.steps.map(s => s.state), ['done', 'attention', 'to-do', 'to-do', 'to-do', 'done', 'to-do']);
  assert.match(decided.steps[1].summary, /1 live application depends on it/);

  const failed = retirementSteps({ decisions: [], dependants: null, dependantsFailed: true, changes: [], evidenceLinks: [], components: null, retiredStage: false });
  assert.equal(failed.steps[1].state, 'attention');
  assert.equal(failed.steps[5].state, 'waiting');
});

test('a full retirement marks every step done, with evidence on the removal change', () => {
  const removal = change('Remove the Timesheets app', 'completed', '2026-11-10T18:00:00Z', 9);
  const link: PackLink = {
    evidenceLinkId: id(2001), subjectKind: 'change-request', subjectId: removal.changeRequestId.toUpperCase(),
    linkKind: { key: 'admin-center-export', label: 'Admin center export' }, url: 'https://example.org/evidence/1', label: 'Deleted in the admin center',
    evidenceLevel: null, capturedOn: '2026-11-10T19:00:00Z', isAiCreated: false, configurationSetVersion: 1,
  };
  const picture = retirementSteps({
    decisions: [
      decision('portfolio', 'retire', 'accepted', '2026-11-01T09:00:00Z'),
      decision('data-use', 'retain', 'accepted', '2026-11-03T09:00:00Z'),
      decision('data-use', 'allow', 'accepted', '2026-01-03T09:00:00Z'),
      decision('retirement', 'complete', 'accepted', '2026-11-20T09:00:00Z'),
    ],
    dependants: [],
    changes: [removal],
    evidenceLinks: [link],
    components: [],
    retiredStage: true,
  });
  assert.equal(picture.retention?.outcome?.key, 'retain');
  assert.equal(picture.removal, removal);
  assert.equal(picture.removalEvidence.length, 1);
  assert.equal(picture.left.length, 0);
  assert.equal(picture.overall, 'retired');
});

test('step 3 says a retention gap that two uses share once (QA walk)', () => {
  const gap = { message: 'Legacy Timesheet App is retiring and writes People / Timesheets on its system of record without an accepted retention decision. Propose a data-retention decision.', domain: { path: 'People / Timesheets' }, principal: { displayLabel: 'Hana Lindqvist' } };
  const picture = retirementSteps({
    decisions: [decision('portfolio', 'retire', 'accepted', '2026-11-01T09:00:00Z')],
    retentionGaps: [gap, { ...gap }],
    dependants: [],
    changes: [],
    evidenceLinks: [],
    components: [],
    retiredStage: false,
  });
  const step = picture.steps.find(s => s.key === 'data');
  assert.equal(step?.summary, `Waiting: ${gap.message}`);
});

test('the list holds every application on its way out, at the point it has reached, newest first', () => {
  const OTHER = id(9003);
  const RETIRED_ONLY = id(9004);
  const rows = wayOutRows([
    decision('portfolio', 'replace', 'accepted', '2026-06-15T10:00:00Z'),
    decision('retirement', 'complete', 'accepted', '2026-10-01T09:00:00Z'),
    decision('portfolio', 'retire', 'proposed', null, { subjectId: OTHER_APP, subjectLabel: 'Purchase Approvals', performedBy: { actorKind: 'person', principal: { objectId: ME, displayLabel: 'Mateo Brandt' }, onBehalfOf: null, clientAppLabel: 'Web app', stampedOn: '2026-11-24T09:00:00Z' } }),
    decision('portfolio', 'keep', 'accepted', '2026-11-02T09:00:00Z', { subjectId: OTHER, subjectLabel: 'Kept App' }),
    decision('portfolio', 'retire', 'accepted', '2026-11-05T09:00:00Z', { subjectId: id(9005), subjectLabel: 'Going App' }),
    decision('retirement', 'complete', 'proposed', null, { subjectId: id(9005), subjectLabel: 'Going App', performedBy: { actorKind: 'person', principal: { objectId: ME, displayLabel: 'Owen Achterberg' }, onBehalfOf: null, clientAppLabel: 'Web app', stampedOn: '2026-11-21T09:00:00Z' } }),
  ], [{ businessApplicationId: RETIRED_ONLY.toUpperCase(), name: 'Crew Roster (classic)' }]);
  assert.deepEqual(rows.map(r => `${r.name}:${r.stage}`), [
    'Purchase Approvals:proposed',
    'Going App:completing',
    'Legacy Timesheet App:retired',
    'Crew Roster (classic):retired',
  ]);
});

test('a search of the tree keeps the matching portfolios and the ones above them', () => {
  const rows = portfolioTree([CLAIMS, PEOPLE, PAYABLES, FINANCE]);
  const found = searchTree(rows, 'claims');
  assert.deepEqual(found.rows.map(r => r.item.name), ['Finance', 'Accounts payable', 'Claims']);
  assert.deepEqual([...found.matched], [CLAIMS.portfolioId.toLowerCase()]);
  assert.equal(searchTree(rows, undefined).rows.length, 4);
  assert.equal(searchTree(rows, 'nothing like it').rows.length, 0);
});
