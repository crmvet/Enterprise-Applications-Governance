// The Data page's logic: the tree, the filters, the matrices, the fixes and the sheets' tool inputs. Fictional data only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appHref, DATA_TABS } from '../../app/routes';
import type { DataDomainItem, DataFinding, DataLocationItem, DataMappingItem, DataRuleValue, DataUseItem, DataUseState } from '../../data/contracts';
import {
  appColumns,
  buildDecisionInput,
  buildDomainTree,
  buildLocationInput,
  buildMappingInput,
  buildMoveInput,
  buildOwnerInput,
  buildUseInput,
  cellKey,
  checkCounts,
  copiesToWatch,
  DATA_TAB_LABELS,
  dataScope,
  dataTabRoute,
  domainRows,
  drillRows,
  findingText,
  fixOf,
  gapChecks,
  groupFindings,
  groupLocations,
  isMyFinding,
  isProblem,
  locationFormOf,
  locationMatches,
  mappingActionsOf,
  mappingCells,
  mappingFormOf,
  matrixColumns,
  mayMapData,
  mayProposeDomainOwner,
  mayProposeLocationOwner,
  notableRules,
  parseContainers,
  plainFinding,
  sortUses,
  stateCounts,
  subtreeOf,
  unsentDecision,
  useActionsOf,
  useCells,
  useFormOf,
  useMatches,
  withShared,
} from './logic';

const APP = '00000000-0000-4000-8000-000000000511';
const APP2 = '00000000-0000-4000-8000-000000000512';
const HANA = { objectId: '00000000-0000-4000-8000-000000000901', displayLabel: 'Hana Lindqvist' };
const GRETA = { objectId: '00000000-0000-4000-8000-000000000902', displayLabel: 'Greta Lund' };
const LOC_PROD = '00000000-0000-4000-8000-000000000701';
const LOC_DB = '00000000-0000-4000-8000-000000000702';
const LOC_TEST = '00000000-0000-4000-8000-000000000703';

const rule = (value: DataRuleValue['value'], setOn: string | null = null): DataRuleValue => ({
  value,
  setOn: setOn ? { key: setOn, label: setOn } : null,
  fromClassification: setOn ? null : { key: 'confidential', label: 'Confidential' },
});

function domain(key: string, parentKey: string | null, extra: Partial<DataDomainItem> = {}): DataDomainItem {
  const label = key.charAt(0).toUpperCase() + key.slice(1);
  return {
    key,
    label,
    path: label,
    parentKey,
    level: { key: parentKey ? 'subdomain' : 'domain', label: parentKey ? 'Subdomain' : 'Domain' },
    isActive: true,
    declarable: true,
    classification: { key: 'confidential', label: 'Confidential', rank: 3, setOn: { key: parentKey ?? key, label: 'People' } },
    owner: null,
    deputy: null,
    steward: null,
    rules: { use: rule('allowed'), ai: rule('decision'), copy: rule('allowed') },
    counts: { mappings: 0, uses: 0, openDecisions: 0 },
    gaps: [],
    recordUrl: `https://eag.example.org/app/records/data-domain/${key}`,
    ...extra,
  };
}

const TREE_ITEMS: DataDomainItem[] = [
  domain('people', null, { owner: { principal: HANA, ownerAssignmentId: 'a1', inheritedFrom: null } }),
  domain('payroll', 'people', {
    owner: { principal: GRETA, ownerAssignmentId: 'a2', inheritedFrom: null },
    rules: { use: rule('decision'), ai: rule('never', 'payroll'), copy: rule('never') },
    counts: { mappings: 2, uses: 3, openDecisions: 1 },
  }),
  domain('bank', 'payroll', { owner: { principal: GRETA, ownerAssignmentId: 'a2', inheritedFrom: { key: 'payroll', label: 'People / Payroll' } }, gaps: ['data.no-deputy'] }),
  domain('customers', null, { classification: { key: 'internal', label: 'General', rank: 2, setOn: { key: 'customers', label: 'Customers' } }, gaps: ['data.no-owner'], counts: { mappings: 1, uses: 0, openDecisions: 0 } }),
];

function location(id: string, extra: Partial<DataLocationItem> = {}): DataLocationItem {
  return {
    dataLocationId: id,
    label: `Location ${id.slice(-3)}`,
    platform: { key: 'sql-server', label: 'SQL Server' },
    parent: null,
    stage: { key: 'production', label: 'Production' },
    isProduction: true,
    deploymentTargetId: null,
    hostedByApplication: null,
    linkKind: { key: 'external-data-system', label: 'External data system' },
    externalId: `host-${id.slice(-3)}`,
    url: null,
    lastSeenOn: '2026-11-01T00:00:00Z',
    status: { label: 'Registered', meaning: 'registered' },
    retiredOn: null,
    technicalOwner: null,
    domains: [],
    childCount: 0,
    flags: { draft: false, stale: false, unmapped: true, noTechnicalOwner: true, copyOutsideProduction: false },
    isAiCreated: false,
    performedBy: null,
    recordUrl: `https://eag.example.org/app/records/data-location/${id}`,
    ...extra,
  };
}

function brief(id: string, stage: string | null) {
  return { dataLocationId: id, label: `Location ${id.slice(-3)}`, platform: { key: 'sql-server', label: 'SQL Server' }, stage: stage ? { key: stage, label: stage === 'production' ? 'Production' : 'Test' } : null, recordUrl: '' };
}

function mapping(id: string, domainKey: string, locationId: string, stage: string | null, extra: Partial<DataMappingItem> = {}): DataMappingItem {
  return {
    dataMappingId: id,
    domain: { key: domainKey, label: domainKey, path: domainKey },
    location: brief(locationId, stage),
    containers: [],
    origin: 'system-of-record',
    copiedFromMappingId: null,
    status: { label: 'Registered', meaning: 'registered' },
    copyState: null,
    decisionId: null,
    approvalsNeeded: 0,
    confirmedOn: null,
    startOn: null,
    endedOn: null,
    isAiCreated: false,
    performedBy: null,
    recordUrl: '',
    ...extra,
  };
}

function use(id: string, state: DataUseState, extra: Partial<DataUseItem> = {}): DataUseItem {
  return {
    dataUseId: id,
    recordNumber: `DU-${id.slice(-4)}`,
    businessApplicationId: APP,
    applicationName: 'Mileage Claims',
    domain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' },
    location: null,
    access: 'write',
    purpose: 'Pays mileage with the salary.',
    reachesAi: false,
    aiServiceAsSeen: null,
    isDeidentified: false,
    accessApproach: null,
    status: { label: 'Registered', meaning: 'registered' },
    state,
    approvalsNeeded: 1,
    approvalsGiven: 0,
    decisionId: null,
    startOn: '2026-11-01',
    endedOn: null,
    isAiCreated: false,
    performedBy: null,
    recordUrl: '',
    ...extra,
  };
}

function finding(check: string, extra: Partial<DataFinding> = {}): DataFinding {
  return {
    check,
    code: 'data.finding',
    severity: 'medium',
    subjectKind: 'data-domain',
    subjectId: 'payroll',
    message: 'A message.',
    domain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' },
    businessApplicationId: null,
    applicationName: null,
    dataLocationId: null,
    principal: null,
    fixTool: null,
    recordUrl: null,
    ...extra,
  };
}

// ---------------------------------------------------------------------------------------------------------------------

test('another tab keeps the domain, application, location and mine filters', () => {
  const route = { page: 'data' as const, tab: 'mapping' as const, domainKey: 'payroll', applicationId: APP, mine: true as const };
  assert.deepEqual(dataTabRoute(route, 'uses'), { page: 'data', tab: 'uses', domainKey: 'payroll', applicationId: APP, mine: true });
  assert.deepEqual(dataTabRoute(route, 'domains'), { page: 'data', domainKey: 'payroll', applicationId: APP, mine: true });
  assert.equal(appHref(dataTabRoute({ page: 'data' }, 'gaps'), '/app/'), '/app/data/gaps');
});

test('every tab has a name', () => {
  for (const tab of DATA_TABS) assert.ok(DATA_TAB_LABELS[tab], tab);
});

test('a shared filter changes on the same tab and the others stay', () => {
  const route = { page: 'data' as const, tab: 'uses' as const, domainKey: 'payroll', mine: true as const };
  assert.deepEqual(withShared(route, { domainKey: undefined }), { page: 'data', tab: 'uses', mine: true });
  assert.deepEqual(withShared(route, { locationId: LOC_DB, mine: false }), { page: 'data', tab: 'uses', domainKey: 'payroll', locationId: LOC_DB });
});

test('the tree nests by parent key, depth first, and a missing parent makes a root', () => {
  const tree = buildDomainTree([...TREE_ITEMS, domain('orphan', 'gone')]);
  assert.deepEqual(tree.order, ['people', 'payroll', 'bank', 'customers', 'orphan']);
  assert.equal(tree.nodes.get('bank')!.depth, 2);
  assert.equal(tree.nodes.get('orphan')!.depth, 0);
  assert.deepEqual([...subtreeOf(tree, 'payroll')].sort(), ['bank', 'payroll']);
});

test('a cycle in the parent keys cannot loop', () => {
  const tree = buildDomainTree([domain('a', 'b'), domain('b', 'a')]);
  assert.deepEqual(tree.order.sort(), ['a', 'b']);
});

test('a filter shows the matching domains with the domains above them for context', () => {
  const tree = buildDomainTree(TREE_ITEMS);
  const { rows, matchedCount } = domainRows(tree, { gapsOnly: true });
  assert.equal(matchedCount, 2);
  assert.deepEqual(rows.map(r => [r.key, r.matched]), [['people', false], ['payroll', false], ['bank', true], ['customers', true]]);
  const classed = domainRows(tree, { classificationKey: 'internal' });
  assert.deepEqual(classed.rows.map(r => r.key), ['customers']);
  const text = domainRows(tree, { text: 'greta' });
  assert.deepEqual(text.rows.filter(r => r.matched).map(r => r.key), ['payroll', 'bank']);
});

test('a collapsed branch hides the domains under it and says it has children', () => {
  const tree = buildDomainTree(TREE_ITEMS);
  const { rows } = domainRows(tree, {}, new Set(['payroll']));
  assert.deepEqual(rows.map(r => r.key), ['people', 'payroll', 'customers']);
  const payroll = rows.find(r => r.key === 'payroll')!;
  assert.equal(payroll.hasChildren, true);
  assert.equal(payroll.collapsed, true);
});

test('the phone drill-down lists one level and counts what sits below', () => {
  const tree = buildDomainTree(TREE_ITEMS);
  assert.deepEqual(drillRows(tree, {}, null).map(r => [r.item.key, r.below]), [['people', 2], ['customers', 0]]);
  assert.deepEqual(drillRows(tree, {}, 'people').map(r => r.item.key), ['payroll']);
  assert.deepEqual(drillRows(tree, { scopeKeys: new Set(['bank']) }, null).map(r => [r.item.key, r.matched]), [['people', false]]);
});

test('domain gaps read as their checks, and only rules that are not allowed get a chip', () => {
  assert.deepEqual(gapChecks(['data.no-deputy', 'data.no-owner', 'data.unknown', 'data.no-owner']), ['DC-01', 'DC-03']);
  assert.deepEqual(notableRules(TREE_ITEMS[1].rules), ['use', 'ai', 'copy']);
  assert.deepEqual(notableRules(TREE_ITEMS[0].rules), ['ai']);
});

test('a data owner of the domain or of one above may propose its owners; the roles may always', () => {
  const tree = buildDomainTree(TREE_ITEMS);
  assert.equal(mayProposeDomainOwner({ objectId: HANA.objectId, roleKeys: [] }, tree, 'bank'), true);
  assert.equal(mayProposeDomainOwner({ objectId: GRETA.objectId, roleKeys: [] }, tree, 'people'), false);
  assert.equal(mayProposeDomainOwner({ objectId: GRETA.objectId, roleKeys: [] }, tree, 'bank'), true);
  assert.equal(mayProposeDomainOwner({ objectId: null, roleKeys: ['data-governance-lead'] }, tree, 'customers'), true);
  const owned = location(LOC_PROD, { technicalOwner: { principal: HANA, ownerAssignmentId: 'a9', fromParent: false } });
  assert.equal(mayProposeLocationOwner({ objectId: HANA.objectId.toUpperCase(), roleKeys: [] }, owned), true);
  assert.equal(mayMapData({ objectId: GRETA.objectId, roleKeys: [] }, TREE_ITEMS[1], null), true);
  assert.equal(mayMapData({ objectId: GRETA.objectId, roleKeys: [] }, TREE_ITEMS[0], owned), false);
});

test('locations group by platform, each instance with the stores inside it', () => {
  const items = [
    location(LOC_PROD, { label: 'Payroll host', domains: [{ domain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' }, dataMappingId: 'm1', origin: 'system-of-record', containers: [] }] }),
    location(LOC_DB, { label: 'PAYLIB', parent: { dataLocationId: LOC_PROD, label: 'Payroll host' } }),
    location(LOC_TEST, { label: 'CRM test', platform: { key: 'dataverse', label: 'Dataverse' }, stage: { key: 'test', label: 'Test' }, isProduction: false, flags: { draft: false, stale: true, unmapped: false, noTechnicalOwner: false, copyOutsideProduction: true } }),
  ];
  const rank = (k: string) => (k === 'dataverse' ? 1 : 7);
  const groups = groupLocations(items, () => true, rank);
  assert.deepEqual(groups.map(g => g.platform.key), ['dataverse', 'sql-server']);
  assert.deepEqual(groups[1].rows.map(r => [r.item.label, r.depth]), [['Payroll host', 0], ['PAYLIB', 1]]);
  // A store that matches brings its instance as context.
  const onlyStore = groupLocations(items, l => l.label === 'PAYLIB', rank);
  assert.deepEqual(onlyStore[0].rows.map(r => [r.item.label, r.matched]), [['Payroll host', false], ['PAYLIB', true]]);
  assert.equal(onlyStore[0].count, 1);
  assert.equal(locationMatches(items[2], { flag: 'copy' }), true);
  assert.equal(locationMatches(items[0], { domainKeys: new Set(['payroll']) }), true);
  assert.equal(locationMatches(items[1], { locationId: LOC_PROD }), true);
  assert.equal(locationMatches(items[2], { applicationId: APP, applicationLocationIds: new Set([LOC_TEST]) }), true);
  assert.equal(locationMatches(items[0], { applicationId: APP, applicationLocationIds: new Set() }), false);
});

test('the location matrix has a column per location that holds data, by stage, production first', () => {
  const ms = [
    mapping('m1', 'payroll', LOC_PROD, 'production'),
    mapping('m2', 'payroll', LOC_TEST, 'test', { origin: 'masked-copy', copyState: 'needed' }),
    mapping('m3', 'customers', LOC_TEST, 'test', { origin: 'production-copy', copyState: 'breaks-rule' }),
    mapping('m4', 'payroll', LOC_PROD, 'production', { origin: 'replica' }),
  ];
  const groups = matrixColumns(ms, k => (k === 'production' ? 1 : 2));
  assert.deepEqual(groups.map(g => [g.stage?.key, g.columns.length]), [['production', 1], ['test', 1]]);
  assert.deepEqual(matrixColumns(ms, () => 0, 'test').map(g => g.stage?.key), ['test']);
  const cells = mappingCells(ms);
  assert.deepEqual(cells.get(cellKey('payroll', LOC_PROD))!.map(m => m.origin), ['system-of-record', 'replica']);
  assert.deepEqual(copiesToWatch(ms).map(m => m.dataMappingId), ['m3', 'm2']);
});

test('the application matrix has a column per application, and the cells hold its uses', () => {
  const us = [use('u1', 'allowed'), use('u2', 'needed', { businessApplicationId: APP2, applicationName: 'Expense Assistant', reachesAi: true })];
  assert.deepEqual(appColumns(us).map(c => c.label), ['Expense Assistant', 'Mileage Claims']);
  assert.equal(useCells(us).get(cellKey('payroll', APP2))![0].dataUseId, 'u2');
});

test('uses sort with what needs action first, and the state filter groups the attention states', () => {
  const us = [use('u1', 'allowed'), use('u2', 'proposed'), use('u3', 'breaks-rule'), use('u4', 're-decide'), use('u5', 'not-needed')];
  assert.deepEqual(sortUses(us).map(u => u.state), ['breaks-rule', 're-decide', 'proposed', 'allowed', 'not-needed']);
  assert.deepEqual(stateCounts(us), { attention: 2, proposed: 1, allowed: 1, denied: 0, 'not-needed': 1 });
  assert.equal(useMatches(us[0], { state: 'attention' }), false);
  assert.equal(useMatches(use('u6', 'needed', { reachesAi: true }), { state: 'attention', aiOnly: true }), true);
  assert.equal(useMatches(us[0], { text: 'mileage' }), true);
  assert.equal(useMatches(us[0], { locationIds: new Set([LOC_DB]) }), false);
});

test('a use offers the act its state asks for', () => {
  assert.deepEqual(useActionsOf(use('u1', 'needed')), { confirm: false, propose: true, openDecision: false, proposeOwner: false, mustChange: false, edit: true, end: true });
  const draft = useActionsOf(use('u2', 'needed', { status: { label: 'Draft', meaning: 'draft' } }));
  assert.equal(draft.confirm, true);
  assert.equal(draft.propose, false);
  assert.equal(useActionsOf(use('u3', 'no-one-to-ask')).proposeOwner, true);
  assert.equal(useActionsOf(use('u4', 'breaks-rule')).mustChange, true);
  const ended = useActionsOf(use('u5', 'allowed', { endedOn: '2026-11-20', decisionId: 'd1' }));
  assert.deepEqual([ended.edit, ended.end, ended.openDecision], [false, false, true]);
  assert.equal(mappingActionsOf(mapping('m1', 'payroll', LOC_TEST, 'test', { copyState: 're-decide' })).propose, true);
  assert.equal(mappingActionsOf(mapping('m2', 'payroll', LOC_TEST, 'test', { copyState: 'breaks-rule' })).mustChange, true);
});

test('each finding has the one act that fixes it', () => {
  assert.deepEqual(fixOf(finding('DC-01')), { kind: 'owner-domain', domainKey: 'payroll', ownerRoleKey: 'data-owner' });
  assert.deepEqual(fixOf(finding('DC-03', { subjectKind: 'data-location', subjectId: LOC_PROD })), { kind: 'owner-location', dataLocationId: LOC_PROD });
  assert.deepEqual(fixOf(finding('DC-03', { message: 'The data-owner of People / Payroll has no deputy, and the owner role needs one.' })), { kind: 'owner-domain', domainKey: 'payroll', ownerRoleKey: 'data-owner' });
  assert.deepEqual(fixOf(finding('DC-04', { message: 'The deputy data-steward of People is disabled, deleted or leaving.' })), { kind: 'owner-domain', domainKey: 'payroll', ownerRoleKey: 'data-steward' });
  assert.deepEqual(fixOf(finding('DC-12', { code: 'data.location-stale', subjectKind: 'data-location', subjectId: LOC_PROD })), { kind: 'confirm-seen', dataLocationId: LOC_PROD });
  assert.deepEqual(fixOf(finding('DC-12', { code: 'data.location-unowned', subjectKind: 'data-location', subjectId: LOC_PROD })), { kind: 'owner-location', dataLocationId: LOC_PROD });
  assert.deepEqual(fixOf(finding('DC-05', { subjectKind: 'data-use', subjectId: 'u1' })), { kind: 'decide-use', dataUseId: 'u1', decisionTypeKey: 'data-use' });
  assert.deepEqual(fixOf(finding('DC-13', { subjectKind: 'data-use', subjectId: 'u1' })), { kind: 'decide-use', dataUseId: 'u1', decisionTypeKey: 'data-retention' });
  assert.deepEqual(fixOf(finding('DC-07', { subjectKind: 'data-use', subjectId: 'u1' })), { kind: 'change-use', dataUseId: 'u1' });
  assert.deepEqual(fixOf(finding('DC-10', { subjectKind: 'business-application', subjectId: APP, businessApplicationId: APP, applicationName: 'Mileage Claims', domain: null })), { kind: 'declare-use', applicationId: APP, applicationName: 'Mileage Claims' });
  assert.deepEqual(fixOf(finding('DC-08', { subjectKind: 'data-mapping', subjectId: 'm1' })), { kind: 'copy', dataMappingId: 'm1' });
  assert.deepEqual(fixOf(finding('DC-11', { subjectKind: 'data-use', subjectId: 'u1', dataLocationId: LOC_DB })), { kind: 'add-mapping', domainKey: 'payroll', dataLocationId: LOC_DB });
  assert.deepEqual(fixOf(finding('DC-09', { businessApplicationId: APP })), { kind: 'tier', applicationId: APP });
  assert.deepEqual(fixOf(finding('DC-14', { subjectKind: 'data-use', subjectId: 'u1' })), { kind: 'move', domainKey: 'payroll' });
  assert.deepEqual(fixOf(finding('DC-99')), { kind: 'none' });
});

test('a finding reads as a sentence with no key or row ID in it', () => {
  const names = { location: (id: string | null) => (id === LOC_TEST ? 'CRM test' : null), ownerRole: (k: string | null) => (k === 'data-owner' ? 'Data owner' : null) };
  assert.equal(findingText(finding('DC-03', { message: 'The data-owner of People / Payroll has no deputy, and the owner role needs one.', principal: GRETA }), names), 'The data owner of People / Payroll, Greta Lund, has no deputy.');
  assert.equal(findingText(finding('DC-08', { subjectKind: 'data-mapping', subjectId: 'm1', dataLocationId: LOC_TEST }), names), 'CRM test holds a copy of People / Payroll outside production.');
  assert.equal(findingText(finding('DC-12', { code: 'data.location-stale', subjectKind: 'data-location', subjectId: LOC_TEST, domain: null }), names), 'CRM test has not been confirmed seen recently.');
  assert.equal(
    plainFinding('DU-0003 (Expense Assistant, People / Payroll / Bank): the AI use rule is never, set on People / Payroll. Confirm it with save_data_location. Row 00000000-0000-4000-8000-000000000001 is odd.', new Map([['data-owner', 'Data owner']])),
    'The AI use rule is never, set on People / Payroll.',
  );
  assert.equal(plainFinding('The data-owner of Payroll is leaving.', new Map([['data-owner', 'Data owner']])), 'The data owner of Payroll is leaving.');
});

test('findings group in check order and count per check', () => {
  const items = [finding('DC-07'), finding('DC-01'), finding('DC-07'), finding('DC-99')];
  assert.deepEqual(groupFindings(items).map(g => [g.label, g.items.length]), [['DC-01', 1], ['DC-07', 2], ['DC-99', 1]]);
  assert.deepEqual(checkCounts(items), { 'DC-01': 1, 'DC-07': 2 });
  const me = { objectId: GRETA.objectId, roleKeys: [] };
  assert.equal(isMyFinding(finding('DC-05', { principal: GRETA, domain: null }), null, me), true);
  assert.equal(isMyFinding(finding('DC-05'), new Set(['payroll']), { objectId: null, roleKeys: [] }), true);
  assert.equal(isMyFinding(finding('DC-05'), new Set(['people']), { objectId: null, roleKeys: [] }), false);
});

test('the shared filters narrow each other into the domains, locations and application locations in scope', () => {
  const tree = buildDomainTree(TREE_ITEMS);
  const locations = [
    location(LOC_PROD, { domains: [{ domain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' }, dataMappingId: 'm1', origin: 'system-of-record', containers: [] }] }),
    location(LOC_DB, { parent: { dataLocationId: LOC_PROD, label: 'Payroll host' }, domains: [{ domain: { key: 'bank', label: 'Bank', path: 'People / Payroll / Bank' }, dataMappingId: 'm2', origin: 'system-of-record', containers: [] }] }),
  ];
  assert.deepEqual([...dataScope({ tree, domainKey: 'payroll', locations }).domainKeys!].sort(), ['bank', 'payroll']);
  assert.deepEqual([...dataScope({ tree, domainKey: 'people', mineKeys: new Set(['bank', 'customers']), locations }).domainKeys!], ['bank']);
  const byLocation = dataScope({ tree, locationId: LOC_PROD.toUpperCase(), locations });
  assert.deepEqual([...byLocation.domainKeys!].sort(), ['bank', 'payroll']);
  assert.deepEqual([...byLocation.locationIds!].sort(), [LOC_PROD, LOC_DB].sort());
  const byApp = dataScope({ tree, appUses: [use('u1', 'allowed', { location: brief(LOC_DB, 'production') })], locations });
  assert.deepEqual([...byApp.domainKeys!], ['payroll']);
  assert.deepEqual([...byApp.applicationLocationIds!], [LOC_DB]);
  assert.equal(dataScope({ tree, locations }).domainKeys, null);
});

test('container names split on lines and commas, without repeats, within the limits', () => {
  assert.deepEqual(parseContainers('PAYMAST\n dbo.Invoices , PAYMAST\n\n'), { list: ['PAYMAST', 'dbo.Invoices'] });
  assert.ok(isProblem(parseContainers(Array.from({ length: 101 }, (_, i) => `t${i}`).join('\n'))));
  assert.ok(isProblem(parseContainers('x'.repeat(201))));
});

test('a use input carries the fields the tool needs, and an end carries the end day', () => {
  const form = { ...useFormOf(undefined, APP, 'payroll'), access: 'write' as const, purpose: '  Pays mileage.  ', reachesAi: true, aiServiceAsSeen: 'Azure OpenAI', locationId: LOC_DB };
  assert.deepEqual(buildUseInput(form, 'declare'), { applicationId: APP, domainKey: 'payroll', access: 'write', purpose: 'Pays mileage.', reachesAi: true, locationId: LOC_DB, aiServiceAsSeen: 'Azure OpenAI' });
  assert.deepEqual(buildUseInput({ ...form, access: '' }, 'declare'), { problem: 'Say whether the application reads the data, writes it, or both.', field: 'access' });
  assert.ok(isProblem(buildUseInput({ ...form, applicationId: '' }, 'declare')));
  const existing = use('00000000-0000-4000-8000-000000000801', 'needed', { status: { label: 'Draft', meaning: 'draft' } });
  const confirm = buildUseInput(useFormOf(existing, ''), 'confirm', existing);
  assert.ok(!isProblem(confirm) && confirm.confirm === true && confirm.dataUseId === existing.dataUseId);
  assert.ok(isProblem(buildUseInput(useFormOf(existing, ''), 'end', existing)));
  const ended = buildUseInput({ ...useFormOf(existing, ''), endOn: '2026-11-24' }, 'end', existing);
  assert.ok(!isProblem(ended) && ended.endOn === '2026-11-24' && ended.purpose === existing.purpose);
  // Ending sends the use as it is now, never a form seeded earlier (wave 4b QA): a stale purpose in the form is ignored.
  const changedSince = { ...existing, purpose: 'Plans the routes of the day.' };
  const endedNow = buildUseInput({ ...useFormOf(existing, ''), endOn: '2026-11-24' }, 'end', changedSince);
  assert.ok(!isProblem(endedNow) && endedNow.purpose === 'Plans the routes of the day.' && endedNow.dataUseId === existing.dataUseId && endedNow.confirm === undefined);
});

test('a location input needs a platform, a stage at the top, a name and an ID or a link', () => {
  const form = { ...locationFormOf(undefined), platformKey: 'ibm-i', targetStageKey: 'production', labelAsSeen: 'Payroll host', externalId: 'payroll-host-01' };
  assert.deepEqual(buildLocationInput(form, 'add'), { items: [{ platformKey: 'ibm-i', labelAsSeen: 'Payroll host', targetStageKey: 'production', externalId: 'payroll-host-01', seenNow: true }] });
  assert.ok(isProblem(buildLocationInput({ ...form, externalId: '' }, 'add')));
  assert.ok(isProblem(buildLocationInput({ ...form, url: 'http://example.org' }, 'add')));
  const child = buildLocationInput({ ...form, parentLocationId: LOC_PROD, targetStageKey: '' }, 'add');
  assert.ok(!isProblem(child) && child.items[0].parentLocationId === LOC_PROD && child.items[0].targetStageKey === undefined);
  const loc = location(LOC_PROD);
  assert.deepEqual(buildLocationInput(locationFormOf(loc), 'confirm-seen', loc), { items: [{ dataLocationId: LOC_PROD, seenNow: true }] });
  // A person confirms an AI client's draft (wave 4b QA): confirm is sent, so the engine takes it out of draft. Seen today on a
  // draft confirms it too, as the sheet says.
  const draft = location(LOC_PROD, { status: { label: 'Draft', meaning: 'draft' } });
  const confirmed = buildLocationInput(locationFormOf(draft), 'confirm', draft);
  assert.ok(!isProblem(confirmed) && confirmed.items[0].confirm === true && confirmed.items[0].dataLocationId === LOC_PROD);
  const edited = buildLocationInput(locationFormOf(draft), 'edit', draft);
  assert.ok(!isProblem(edited) && edited.items[0].confirm === undefined);
  assert.deepEqual(buildLocationInput(locationFormOf(draft), 'confirm-seen', draft), { items: [{ dataLocationId: LOC_PROD, seenNow: true, confirm: true }] });
  assert.deepEqual(buildLocationInput(locationFormOf(loc), 'retire', loc), { items: [{ dataLocationId: LOC_PROD, retire: true }] });
  // A reader sees no ID and no link: an edit keeps them.
  const hidden = location(LOC_PROD, { externalId: null, url: null });
  const edit = buildLocationInput(locationFormOf(hidden), 'edit', hidden);
  assert.ok(!isProblem(edit) && edit.items[0].externalId === undefined && edit.items[0].dataLocationId === LOC_PROD);
});

test('a mapping input carries its origin and containers, and a copy its source', () => {
  const form = { ...mappingFormOf(undefined, 'payroll', LOC_TEST), origin: 'masked-copy' as const, containers: 'PAYMAST', copiedFromMappingId: 'm1' };
  assert.deepEqual(buildMappingInput(form, 'add'), { items: [{ domainKey: 'payroll', locationId: LOC_TEST, origin: 'masked-copy', containers: ['PAYMAST'], copiedFromMappingId: 'm1' }] });
  const sor = buildMappingInput({ ...form, origin: 'system-of-record' }, 'add');
  assert.ok(!isProblem(sor) && sor.items[0].copiedFromMappingId === undefined);
  assert.ok(isProblem(buildMappingInput({ ...form, origin: '' }, 'add')));
  const m = mapping('00000000-0000-4000-8000-000000000601', 'payroll', LOC_TEST, 'test');
  const end = buildMappingInput({ ...mappingFormOf(m), endOn: '2026-11-24' }, 'end', m);
  assert.ok(!isProblem(end) && end.items[0].mappingId === m.dataMappingId && end.items[0].endOn === '2026-11-24');
  // Ending sends the mapping as it is now (wave 4b QA): an origin changed a moment before stays.
  const replica = { ...m, origin: 'replica' as const };
  const endNow = buildMappingInput({ ...mappingFormOf(m), endOn: '2026-11-24' }, 'end', replica);
  assert.ok(!isProblem(endNow) && endNow.items[0].origin === 'replica');
  // A person confirms an AI client's draft mapping (wave 4b QA).
  const confirmed = buildMappingInput(mappingFormOf(m), 'confirm', m);
  assert.ok(!isProblem(confirmed) && confirmed.items[0].confirm === true && confirmed.items[0].mappingId === m.dataMappingId);
  const edited = buildMappingInput(mappingFormOf(m), 'edit', m);
  assert.ok(!isProblem(edited) && edited.items[0].confirm === undefined);
});

test('an owner change names one subject and one change', () => {
  const form = { ownerRoleKey: 'data-owner', isDeputy: true, person: GRETA, endAssignmentId: '', startOn: '', reason: ' Greta covers payroll. ' };
  assert.deepEqual(buildOwnerInput(form, { kind: 'data-domain', domainKey: 'payroll' }), { dataDomainKey: 'payroll', changes: [{ ownerRoleKey: 'data-owner', addObjectId: GRETA.objectId, isDeputy: true }], reason: 'Greta covers payroll.' });
  assert.deepEqual(buildOwnerInput({ ...form, isDeputy: false, person: null, endAssignmentId: 'a2' }, { kind: 'data-location', dataLocationId: LOC_PROD }), { dataLocationId: LOC_PROD, changes: [{ ownerRoleKey: 'data-owner', endAssignmentId: 'a2' }], reason: 'Greta covers payroll.' });
  assert.ok(isProblem(buildOwnerInput({ ...form, person: null }, { kind: 'data-domain', domainKey: 'payroll' })));
  assert.ok(isProblem(buildOwnerInput({ ...form, reason: ' ' }, { kind: 'data-domain', domainKey: 'payroll' })));
});

test('a data decision proposal carries its subject, and an option needs its reason', () => {
  const form = { decisionTypeKey: 'data-use', statement: 'Allow mileage pay.', context: 'Pays mileage with the salary.', outcomeKey: 'allow', reason: 'Needed for pay.', optionText: '', optionWhyNot: '' };
  assert.deepEqual(buildDecisionInput(form, 'data-use', 'u1'), { decisionTypeKey: 'data-use', subjectKind: 'data-use', subjectId: 'u1', statement: 'Allow mileage pay.', context: 'Pays mileage with the salary.', proposedOutcomeKey: 'allow', reason: 'Needed for pay.' });
  assert.ok(isProblem(buildDecisionInput({ ...form, optionText: 'Pay by hand' }, 'data-use', 'u1')));
  assert.ok(isProblem(buildDecisionInput(form, 'data-mapping', 'm1', true)));
  const withOption = buildDecisionInput({ ...form, optionText: 'Pay by hand', optionWhyNot: 'Too slow.' }, 'data-mapping', 'm1', true);
  assert.ok(!isProblem(withOption) && withOption.optionsConsidered?.[0].whyNot === 'Too slow.');
});

test('a move needs another domain, something to move and a reason', () => {
  assert.deepEqual(buildMoveInput({ toDomainKey: 'bank', mappings: true, uses: false, reason: 'Split bank details out.' }, 'payroll'), { fromDomainKey: 'payroll', toDomainKey: 'bank', include: ['mappings'], reason: 'Split bank details out.' });
  assert.ok(isProblem(buildMoveInput({ toDomainKey: 'payroll', mappings: true, uses: true, reason: 'x' }, 'payroll')));
  assert.ok(isProblem(buildMoveInput({ toDomainKey: 'bank', mappings: false, uses: false, reason: 'x' }, 'payroll')));
});

test('the follow-up offers to send a decision a write opened and did not send', () => {
  assert.equal(unsentDecision({ decisionId: 'd1', decisionSubmitted: false }), 'd1');
  assert.equal(unsentDecision({ decisionId: 'd1', decisionSubmitted: true }), null);
  assert.equal(unsentDecision({ decisionId: 'd2', pendingAssignments: [] }), 'd2');
  assert.equal(unsentDecision({ items: [{ decisionId: null }, { decisionId: 'd3', decisionSubmitted: false }] }), 'd3');
  assert.equal(unsentDecision({ decisionId: null }), null);
});
