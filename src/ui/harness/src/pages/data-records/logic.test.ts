// The data records' logic: finding a row's domain from the rule trace, the Application 360 summary line, the person's
// part on a domain, who a data decision asks, the versions that touched a domain, and retention while an app retires.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type {
  ConfigurationChanges,
  DataApprover,
  DataDomainDetail,
  DataDomainItem,
  DataFinding,
  DataMappingItem,
  DataUseItem,
  DecisionLogItem,
  RulesExplained,
} from '../../data/contracts';
import {
  aiText,
  aiUsesOf,
  answeringLevel,
  approversSentence,
  approverWords,
  copyStateHelp,
  dataDecisionsOf,
  dataSummary,
  dataSummaryText,
  decisionsAbout,
  domainKeyOfTrace,
  fixText,
  impactGroups,
  viaText,
  locationFlags,
  locationsOfUses,
  mappingsByLocation,
  myDomainCounts,
  myPartsOn,
  needsDecision,
  ownHolder,
  partsText,
  queueCheck,
  retentionRows,
  splitFindings,
  stageText,
  versionLineText,
  versionsTouching,
} from './logic';

const APP = '00000000-0000-4000-8000-000000000511';
const HANA = { objectId: '00000000-0000-4000-8000-000000000116', displayLabel: 'Hana Lindqvist' };
const ROSA = { objectId: '00000000-0000-4000-8000-000000000117', displayLabel: 'Rosa Tamaki' };
const PEOPLE = { key: 'people', label: 'People' };
const PAYROLL = { key: 'payroll', label: 'People / Payroll', path: 'People / Payroll' };

function use(over: Partial<DataUseItem> = {}): DataUseItem {
  return {
    dataUseId: '00000000-0000-4000-8000-000000000901',
    recordNumber: 'DU-0001',
    businessApplicationId: APP,
    applicationName: 'Visit Report Agent',
    domain: PAYROLL,
    location: null,
    access: 'read',
    purpose: 'Reads pay grades for the report.',
    reachesAi: false,
    aiServiceAsSeen: null,
    isDeidentified: false,
    accessApproach: null,
    status: { label: 'Registered', meaning: 'registered' },
    state: 'not-needed',
    approvalsNeeded: 0,
    approvalsGiven: null,
    decisionId: null,
    startOn: '2026-11-01',
    endedOn: null,
    isAiCreated: false,
    performedBy: null,
    recordUrl: `https://eag.example.org/app/records/data-use/00000000-0000-4000-8000-000000000901`,
    ...over,
  } as DataUseItem;
}

function finding(check: string, over: Partial<DataFinding> = {}): DataFinding {
  return {
    check,
    code: 'data.x',
    severity: 'medium',
    subjectKind: 'data-domain',
    subjectId: 'payroll',
    message: `Finding ${check}.`,
    domain: PAYROLL,
    businessApplicationId: null,
    applicationName: null,
    dataLocationId: null,
    principal: null,
    fixTool: null,
    recordUrl: null,
    ...over,
  };
}

function decision(over: Partial<DecisionLogItem>): DecisionLogItem {
  return {
    decisionId: '00000000-0000-4000-8000-000000000a01',
    decisionType: { key: 'data-use', label: 'Data use' },
    subjectKind: 'data-use',
    subjectId: '00000000-0000-4000-8000-000000000901',
    subjectLabel: 'DU-0001',
    statement: 'Allow it.',
    reason: '',
    exception: null,
    outcome: null,
    status: { label: 'Proposed', meaning: 'proposed' },
    decider: null,
    decidedOn: null,
    supersedes: null,
    supersededBy: null,
    amends: null,
    approvalsSummary: { required: 0, approved: 0, rejected: 0, pending: 0 },
    configurationSetVersion: 7,
    proposedBy: null,
    isAiCreated: false,
    performedBy: null,
    recordUrl: 'https://eag.example.org/app/records/decision/00000000-0000-4000-8000-000000000a01',
    ...over,
  } as DecisionLogItem;
}

test('the row\'s domain is the last level the data trace names for the first time', () => {
  const trace: RulesExplained = {
    configurationSetVersion: 7,
    engineVersion: '1.0',
    summary: '1 rule trace(s).',
    aspects: [{
      aspect: 'data',
      operation: 'OP-16',
      winner: { entity: 'data-domain', key: 'people', scope: { rule: 'ai', value: 'never' } },
      dropped: [],
      trace: [
        { step: 1, entity: 'data-domain', key: 'people', matched: true, why: 'People: classification restricted.' },
        { step: 2, entity: 'data-domain', key: 'payroll', matched: true, why: 'Payroll: sets nothing of its own.' },
        { step: 3, entity: 'data-classification', key: 'restricted', matched: false, why: 'The default.' },
        { step: 4, entity: 'data-domain', key: 'people', matched: true, why: 'Governs: AI use never.' },
      ],
    }],
  };
  assert.equal(domainKeyOfTrace(trace), 'payroll');
  assert.equal(domainKeyOfTrace({ ...trace, aspects: [] }), null);
  assert.equal(domainKeyOfTrace(null), null);
});

test('the summary line counts domains read and written, names the highest class and the sensitivity answer', () => {
  const uses = [
    use({ domain: PAYROLL, access: 'read' }),
    use({ dataUseId: 'b', domain: { key: 'bank', label: 'Bank', path: 'People / Bank' }, access: 'read-write' }),
    use({ dataUseId: 'c', domain: { key: 'notes', label: 'Notes', path: 'Field / Notes' }, access: 'read' }),
    use({ dataUseId: 'd', domain: { key: 'old', label: 'Old', path: 'Old' }, access: 'write', endedOn: '2026-10-01' }),
  ];
  const s = dataSummary({ uses, highest: { key: 'restricted', label: 'Restricted' }, sensitivity: 'Internal', findings: [finding('DC-09')], holdsNoBusinessData: null });
  assert.deepEqual([s.reads, s.writes, s.belowData, s.none], [2, 1, true, null]);
  assert.equal(dataSummaryText(s, 'Visit Report Agent'), 'Reads 2 domains, writes 1. Highest class: Restricted. Sensitivity answer: Internal (below the data).');
  const none = dataSummary({ uses: [], highest: null, sensitivity: null, findings: [], holdsNoBusinessData: true });
  assert.equal(dataSummaryText(none, 'Visit Report Agent'), 'Visit Report Agent holds no business data.');
  const undeclared = dataSummary({ uses: [], highest: null, sensitivity: null, findings: [finding('DC-09')], holdsNoBusinessData: false });
  assert.equal(dataSummaryText(undeclared, 'Visit Report Agent'), 'Visit Report Agent declares no data use yet. The sensitivity answer is below the data.');
});

test('findings split into the banners, retention and the rest', () => {
  const split = splitFindings([finding('DC-09'), finding('DC-10'), finding('DC-11'), finding('DC-13'), finding('DC-05'), finding('DC-07')]);
  assert.deepEqual([split.dc09.length, split.dc10.length, split.dc11.length, split.dc13.length, split.other.map(f => f.check)], [1, 1, 1, 1, ['DC-05', 'DC-07']]);
});

test('data decisions: data use, retention and any data subject, open first, withdrawn left out', () => {
  const list = dataDecisionsOf([
    decision({ decisionId: '1', decisionType: { key: 'tier', label: 'Tier' }, subjectKind: 'business-application', status: { label: 'Accepted', meaning: 'accepted' } }),
    decision({ decisionId: '2', status: { label: 'Accepted', meaning: 'accepted' } }),
    decision({ decisionId: '3', decisionType: { key: 'data-retention', label: 'Data retention' } }),
    decision({ decisionId: '4', decisionType: { key: 'ownership', label: 'Ownership' }, subjectKind: 'data-domain', subjectId: 'payroll' }),
    decision({ decisionId: '5', status: { label: 'Withdrawn', meaning: 'withdrawn' } }),
  ]);
  assert.deepEqual(list.map(d => d.decisionId), ['3', '4', '2']);
});

test('retention: a waiting decision first, else the accepted one, else open; ended uses need none', () => {
  const a = use({ dataUseId: 'a' });
  const b = use({ dataUseId: 'b' });
  const c = use({ dataUseId: 'c' });
  const ended = use({ dataUseId: 'd', endedOn: '2026-11-02' });
  const rows = retentionRows([a, b, c, ended], [
    decision({ decisionId: 'x', decisionType: { key: 'data-retention', label: 'Data retention' }, subjectId: 'A', status: { label: 'Accepted', meaning: 'accepted' }, outcome: { key: 'archive', label: 'Archive' } }),
    decision({ decisionId: 'y', decisionType: { key: 'data-retention', label: 'Data retention' }, subjectId: 'b' }),
    decision({ decisionId: 'z', decisionType: { key: 'data-use', label: 'Data use' }, subjectId: 'c' }),
  ]);
  assert.deepEqual(rows.map(r => [r.use.dataUseId, r.state, r.decision?.decisionId ?? null]), [['a', 'decided', 'x'], ['b', 'waiting', 'y'], ['c', 'open', null]]);
});

function domain(over: Partial<DataDomainItem>): DataDomainItem {
  return {
    key: 'payroll',
    label: 'Payroll',
    path: 'People / Payroll',
    parentKey: 'people',
    level: { key: 'subdomain', label: 'Subdomain' },
    isActive: true,
    declarable: true,
    classification: null,
    owner: null,
    deputy: null,
    steward: null,
    rules: {
      use: { value: 'allowed', setOn: null, fromClassification: null },
      ai: { value: 'decision', setOn: null, fromClassification: null },
      copy: { value: 'masked-only', setOn: null, fromClassification: null },
    },
    counts: { mappings: 0, uses: 0, openDecisions: 0 },
    gaps: [],
    recordUrl: 'https://eag.example.org/app/records/data-domain/payroll',
    ...over,
  };
}

test('the person\'s part on a domain: owner, deputy or steward, own or from a domain above', () => {
  const own = domain({ key: 'people', path: 'People', owner: { principal: HANA, ownerAssignmentId: 'o1', inheritedFrom: null }, deputy: ROSA });
  const inherited = domain({ owner: { principal: HANA, ownerAssignmentId: 'o1', inheritedFrom: PEOPLE }, deputy: ROSA });
  const steward = domain({ key: 'bank', path: 'People / Bank', owner: { principal: ROSA, ownerAssignmentId: 'o2', inheritedFrom: null }, steward: { principal: HANA, ownerAssignmentId: 'o3', inheritedFrom: null } });
  assert.equal(partsText(myPartsOn(own, HANA.objectId.toUpperCase())), 'Data owner');
  assert.equal(partsText(myPartsOn(inherited, HANA.objectId)), 'Data owner, from People');
  assert.equal(partsText(myPartsOn(inherited, ROSA.objectId)), 'Deputy data owner, from People');
  assert.equal(partsText(myPartsOn(steward, HANA.objectId)), 'Data steward');
  assert.equal(partsText(myPartsOn(domain({}), HANA.objectId)), 'Yours through a domain above it');
  assert.deepEqual(myDomainCounts([own, inherited, steward], HANA.objectId), { own: 1, inherited: 1, deputy: 0, steward: 1 });
});

test('a data-gap queue item names its check in its title', () => {
  assert.deepEqual(queueCheck('DC-05: People / Payroll'), { check: 'DC-05', rest: 'People / Payroll' });
  assert.deepEqual(queueCheck('DC-99: Something new'), { check: null, rest: 'Something new' });
  assert.deepEqual(queueCheck('No code here'), { check: null, rest: 'No code here' });
  assert.equal(fixText('propose_owner_change'), 'Propose an owner');
  assert.equal(fixText('some_new_tool'), null);
});

function approver(over: Partial<DataApprover>): DataApprover {
  return { kind: 'owner-role', role: null, ownerRole: { key: 'data-owner', label: 'Data owner' }, ownerSubject: 'data-domain', principal: HANA, inheritedFrom: PEOPLE, isConsultation: false, noOneToAsk: false, ...over };
}

test('who a data decision asks, in words', () => {
  assert.deepEqual(approverWords(approver({})), { who: 'Hana Lindqvist', part: 'Data owner, from People', noOneToAsk: false, isConsultation: false });
  const reviewer = approver({ kind: 'role', role: { key: 'security-reviewer', label: 'Security reviewer' }, ownerRole: null, ownerSubject: null, principal: null, inheritedFrom: null });
  assert.equal(approverWords(reviewer).who, 'Any security reviewer');
  const nobody = approver({ principal: null, inheritedFrom: null, noOneToAsk: true });
  assert.deepEqual([approverWords(nobody).who, approverWords(nobody).noOneToAsk], ['No data owner to ask', true]);
  assert.equal(approversSentence([approver({}), reviewer, approver({ isConsultation: true })]), 'Hana Lindqvist (data owner, from People) and any security reviewer.');
  assert.equal(approversSentence([]), 'Nobody needs to approve it.');
});

test('the owners chain: the holder set on a level, and the level that answers for the domain', () => {
  const holder = { ownerAssignmentId: 'o1', principal: HANA, directoryState: 'enabled' as const, isLeaving: false };
  const current: NonNullable<DataDomainDetail['owners']> = [
    { ownerRole: { key: 'data-owner', label: 'Data owner' }, inheritsDown: true, holder, otherHolders: [], deputies: [], inheritedFrom: PEOPLE, acting: HANA },
    { ownerRole: { key: 'data-steward', label: 'Data steward' }, inheritsDown: true, holder: { ...holder, principal: ROSA }, otherHolders: [], deputies: [], inheritedFrom: null, acting: ROSA },
  ];
  assert.equal(ownHolder(current, 'data-owner'), null);
  assert.equal(ownHolder(current, 'data-steward')?.holder?.principal.displayLabel, 'Rosa Tamaki');
  assert.equal(answeringLevel(current, 'data-owner', 'payroll'), 'people');
  assert.equal(answeringLevel(current, 'data-steward', 'payroll'), 'payroll');
  assert.equal(answeringLevel(current, 'nobody', 'payroll'), null);
});

test('the versions that changed a domain, newest first, in plain words', () => {
  const v = (version: number, changeSummary: string[]) => ({
    version, isActive: false, baseVersion: version - 1, publishedOn: null, contentHash: null, changeSummary, instructedBy: null, decision: null, acceptedBy: [], acceptedHow: 'approval' as const, sourceDraftId: null,
  });
  const versions: ConfigurationChanges['versions'] = [
    v(5, ['Changed Data Domain `payroll`: aiUseRule, classificationKey.']),
    v(6, ['Changed Setting `change-management-mode`: value.']),
    v(7, ['Added Data Domain `payroll-history`.', 'Retired Data Domain `payroll`.']),
  ];
  const touched = versionsTouching(versions, 'payroll');
  assert.deepEqual(touched.map(t => [t.version.version, t.lines.length]), [[7, 1], [5, 1]]);
  assert.equal(versionLineText(touched[1].lines[0], 'payroll'), 'Changed this domain: AI use rule, classification.');
  assert.equal(versionLineText(touched[0].lines[0], 'payroll'), 'Retired this domain.');
});

test('mappings group by location, decisions about one row come open first', () => {
  const loc = (id: string, label: string) => ({ dataLocationId: id, label, platform: { key: 'azure-sql', label: 'Azure SQL' }, stage: null, recordUrl: '' });
  const m = (id: string, location: ReturnType<typeof loc>) => ({ dataMappingId: id, location } as unknown as DataMappingItem);
  const groups = mappingsByLocation([m('1', loc('b', 'Payroll DB')), m('2', loc('a', 'HR share')), m('3', loc('B', 'Payroll DB'))]);
  assert.deepEqual(groups.map(g => [g.location.label, g.mappings.length]), [['HR share', 1], ['Payroll DB', 2]]);
  const list = decisionsAbout([
    { decisionId: 'x', recordNumber: null, decisionType: { key: 'data-use', label: 'Data use' }, subjectKind: 'data-use', subjectId: 'U1', statement: '', status: { label: 'Accepted', meaning: 'accepted' }, outcome: null, decidedOn: null, recordUrl: '' },
    { decisionId: 'y', recordNumber: null, decisionType: { key: 'data-use', label: 'Data use' }, subjectKind: 'data-use', subjectId: 'u1', statement: '', status: { label: 'Proposed', meaning: 'proposed' }, outcome: null, decidedOn: null, recordUrl: '' },
    { decisionId: 'z', recordNumber: null, decisionType: { key: 'data-use', label: 'Data use' }, subjectKind: 'data-mapping', subjectId: 'u1', statement: '', status: { label: 'Proposed', meaning: 'proposed' }, outcome: null, decidedOn: null, recordUrl: '' },
  ], 'data-use', 'u1');
  assert.deepEqual(list.map(d => d.decisionId), ['y', 'x']);
});

test('use and location words', () => {
  assert.equal(aiText({ reachesAi: true, aiServiceAsSeen: 'Azure OpenAI' }), 'Sends data to AI: Azure OpenAI, as seen');
  assert.equal(aiText({ reachesAi: false, aiServiceAsSeen: null }), 'No AI');
  assert.equal(copyStateHelp('needed'), 'The rule in force asks for a copy decision, and no decision allows it yet.');
  assert.deepEqual([needsDecision('needed'), needsDecision('re-decide'), needsDecision('proposed'), needsDecision(null)], [true, true, false, false]);
  assert.deepEqual(locationFlags({ draft: false, stale: true, unmapped: false, noTechnicalOwner: true, copyOutsideProduction: false }), ['stale', 'noTechnicalOwner']);
  assert.deepEqual([stageText({ label: 'Production' }, true), stageText({ label: 'Live' }, true), stageText({ label: 'Test' }, false), stageText(null, false)], ['Production', 'Live (production)', 'Test', 'No stage set']);
  const l = { dataLocationId: 'L1', label: 'Payroll DB', platform: { key: 'azure-sql', label: 'Azure SQL' }, stage: null, recordUrl: '' };
  const uses = [use({ location: l }), use({ dataUseId: 'b', location: { ...l, dataLocationId: 'l1' }, domain: { key: 'bank', label: 'Bank', path: 'People / Bank' }, reachesAi: true }), use({ dataUseId: 'c', location: null })];
  assert.deepEqual(locationsOfUses(uses).map(p => [p.location.label, p.domains.map(d => d.key)]), [['Payroll DB', ['payroll', 'bank']]]);
  assert.deepEqual(aiUsesOf(uses).map(u => u.dataUseId), ['b']);
});

test('what depends on a component or a location: the first hop says how it joined, later hops name the app before them', () => {
  const node = (id: string, name: string, depth: number, path: string[], via: 'part-of' | 'ai-component' | 'data-use' | 'hosts-location' | 'dependency', type: string | null = null) => ({
    businessApplicationId: id, name, tier: null, lifecycleMeaning: null, businessOwner: null, technicalOwner: null,
    dependencyTypeKey: type, via, criticalityKey: null, depth, path,
  });
  const groups = impactGroups({ nodes: [
    node('A2', 'Route Planner', 2, ['A1', 'A2'], 'dependency', 'reads-data-from'),
    node('A1', 'Inspection Scheduler', 1, ['A1'], 'data-use'),
    node('A0', 'Field Hub', 1, ['A0'], 'hosts-location'),
  ] });
  assert.deepEqual(groups.map(g => [g.title, g.nodes.map(n => n.node.name)]), [['Directly', ['Field Hub', 'Inspection Scheduler']], ['2 hops away', ['Route Planner']]]);
  assert.equal(groups[1].nodes[0].parentName, 'Inspection Scheduler');
  const label = (k: string) => (k === 'reads-data-from' ? 'Reads data from' : k);
  assert.equal(viaText(groups[0].nodes[1].node, 'Field DB', label), 'Has a data use at Field DB');
  assert.equal(viaText(groups[0].nodes[0].node, 'Field DB', label), "Field DB is this application's location");
  assert.equal(viaText(groups[1].nodes[0].node, 'Inspection Scheduler', label), 'Reads data from Inspection Scheduler');
  assert.equal(viaText({ via: 'part-of', dependencyTypeKey: null, depth: 1 }, 'Field app', label), 'Built from Field app');
  assert.equal(viaText({ via: 'ai-component', dependencyTypeKey: null, depth: 1 }, 'Copilot model', label), 'Uses Copilot model as its AI component');
});
