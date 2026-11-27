// The dependency map's component and data place nodes, from the application card. Fictional names and patterned
// placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ApplicationCard, DataUseItem } from '../../data/contracts';
import { combineAccess, partNodes, usesWithoutPlace } from './mapParts';

const APP = '00000000-0000-4000-8000-00000000a001';
const COMP = '00000000-0000-4000-8000-00000000c001';
const SHARED = '00000000-0000-4000-8000-00000000c002';
const LOC = '00000000-0000-4000-8000-00000000d001';
const LOC2 = '00000000-0000-4000-8000-00000000d002';

const relation = (key: string, label: string) => (key === 'reads-data-from' ? 'Reads data from' : label);

const use = (over: Partial<DataUseItem> = {}): DataUseItem => ({
  dataUseId: '00000000-0000-4000-8000-00000000e001',
  recordNumber: 'DU-0001',
  businessApplicationId: APP,
  applicationName: 'Crew Roster',
  domain: { key: 'payroll', label: 'Payroll', path: 'People / Payroll' },
  location: { dataLocationId: LOC, label: 'HR Dataverse', platform: { key: 'dataverse', label: 'Dataverse' }, stage: { key: 'production', label: 'Production' }, recordUrl: 'https://example.org/app/records/data-location/x' },
  access: 'read',
  purpose: 'Rosters crews.',
  reachesAi: false,
  aiServiceAsSeen: null,
  isDeidentified: false,
  accessApproach: null,
  status: { label: 'Registered', meaning: 'registered' },
  state: 'allowed',
  approvalsNeeded: 0,
  approvalsGiven: null,
  decisionId: null,
  startOn: null,
  endedOn: null,
  isAiCreated: false,
  performedBy: null,
  recordUrl: 'https://example.org/app/records/data-use/x',
  ...over,
});

const card = (over: Partial<ApplicationCard> = {}): Pick<ApplicationCard, 'components' | 'dependencies' | 'data'> => ({
  components: [{ technicalComponentId: COMP, kind: { key: 'canvas-app', label: 'Canvas app' }, externalId: 'app-1', url: 'https://apps.example.org/app-1', labelAsSeen: 'Roster app', lastSeenOn: null }],
  dependencies: {
    dependsOnCount: 2,
    usedByCount: 0,
    top: [
      { direction: 'depends-on', businessApplicationId: null, technicalComponentId: SHARED, name: 'Shared lookup flow', dependencyType: { key: 'reads-data-from', label: 'Reads data from' }, criticality: { key: 'high', label: 'High' } },
      { direction: 'depends-on', businessApplicationId: '00000000-0000-4000-8000-00000000a002', technicalComponentId: null, name: 'Payroll', dependencyType: { key: 'calls', label: 'Calls' }, criticality: null },
    ],
  },
  data: {
    summary: 'Reads 2 domains.',
    holdsNoBusinessData: false,
    highestClassification: null,
    uses: [
      use(),
      use({ dataUseId: '00000000-0000-4000-8000-00000000e002', domain: { key: 'leave', label: 'Leave', path: 'People / Leave' }, access: 'write' }),
      use({ dataUseId: '00000000-0000-4000-8000-00000000e003', location: { dataLocationId: LOC2, label: 'Finance SQL', platform: { key: 'azure-sql', label: 'Azure SQL' }, stage: null, recordUrl: 'https://example.org/app/records/data-location/y' } }),
      use({ dataUseId: '00000000-0000-4000-8000-00000000e004', location: null }),
      use({ dataUseId: '00000000-0000-4000-8000-00000000e005', location: { dataLocationId: '00000000-0000-4000-8000-00000000d003', label: 'Old share', platform: { key: 'file-share', label: 'File share' }, stage: null, recordUrl: 'https://example.org/z' }, endedOn: '2026-10-01' }),
    ],
    dataOwners: [],
    findings: [],
  },
  ...over,
});

test('read and write together read as both', () => {
  assert.equal(combineAccess(['read']), 'read');
  assert.equal(combineAccess(['write', 'write']), 'write');
  assert.equal(combineAccess(['read', 'write']), 'read-write');
  assert.equal(combineAccess(['read-write']), 'read-write');
});

test('the parts: components, the components it depends on, then one node per data place', () => {
  const nodes = partNodes(card(), relation);
  assert.deepEqual(nodes.map(n => [n.kind, n.name, n.relation]), [
    ['component', 'Roster app', 'Built from'],
    ['component-link', 'Shared lookup flow', 'Reads data from'],
    ['data-place', 'Finance SQL', 'Reads data from'],
    ['data-place', 'HR Dataverse', 'Reads and writes data in'],
  ]);
  const hr = nodes.find(n => n.name === 'HR Dataverse')!;
  assert.equal(hr.facts, 'People / Payroll, People / Leave. Dataverse, Production');
  assert.equal(hr.recordUrl, 'https://example.org/app/records/data-location/x');
  assert.equal(nodes[0].externalUrl, 'https://apps.example.org/app-1');
  assert.equal(nodes[1].criticalityKey, 'high');
});

test('an ended use draws no place, and a use with no place is counted', () => {
  assert.ok(!partNodes(card(), relation).some(n => n.name === 'Old share'));
  assert.equal(usesWithoutPlace(card()), 1);
  assert.deepEqual(partNodes(null, relation), []);
  assert.equal(usesWithoutPlace(null), 0);
});
