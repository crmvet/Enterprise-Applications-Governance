// The Govern pages' logic (Roles and access, Guidelines, Measures, Admin): words for keys, filters, sorting, the binding
// check findings, the measures' trend call and the notices. Fictional names and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { AdminStatus, DueStateRefreshResult, RoleBindingCheck } from '../../data/contracts';
import {
  aiLine,
  bindingFindings,
  bindingOperations,
  daysUntil,
  drillOf,
  grantExpiryText,
  grantsByExpiry,
  groupByToolset,
  guidelineCounts,
  guidelineMatches,
  guidelineNumber,
  guidelineShown,
  isUnbound,
  lowerFirst,
  measureValueText,
  noticesOf,
  policyMatches,
  ratioText,
  refreshLines,
  refreshTotal,
  roleCounts,
  roleMatches,
  schemaState,
  shortHash,
  signalState,
  submitKindsText,
  supersededNumber,
  toolMatches,
  toolTitle,
  toWhomText,
  trendCall,
  usedByText,
  chapterTitleText,
  whoMayCall,
  windowDaysOf,
  withoutToolNames,
  type GuidelineItem,
  type MeasureItem,
  type PolicyRow,
  type RoleItem,
} from './logic';

const GROUP = '00000000-0000-4000-8000-000000000105';
const PIPELINE = { objectId: '00000000-0000-4000-8000-000000000901', displayLabel: 'Build pipeline' };
const TODAY = '2026-11-24';
const labels: Record<string, string> = { 'platform-admin': 'Platform admin', 'deployment-recorder': 'Deployment recorder', maker: 'Maker', auditor: 'Auditor' };
const roleLabel = (k: string) => labels[k] ?? k;

const role = (over: Partial<RoleItem>): RoleItem => ({
  role: { key: 'maker', label: 'Maker' },
  party: null,
  duties: null,
  isWorkloadRole: false,
  isReadOnly: false,
  canChangeConfiguration: false,
  heldByMe: false,
  bindings: [],
  holders: null,
  workloadGrants: [],
  tools: [],
  decisionRights: [],
  approvals: [],
  separationRules: [],
  ...over,
});

test('tool names read as words, with acronyms in capitals', () => {
  assert.equal(toolTitle('save_data_use'), 'Save data use');
  assert.equal(toolTitle('get_ai_activity'), 'Get AI activity');
  assert.equal(toolTitle('whoami'), 'Who am I');
});

test('engine text drops tool API names in brackets', () => {
  assert.equal(withoutToolNames('Direct role assignments need Microsoft Graph: run the role binding check (check_role_bindings).'), 'Direct role assignments need Microsoft Graph: run the role binding check.');
  assert.equal(withoutToolNames('Emergency share above 20% (of changes)'), 'Emergency share above 20% (of changes)');
});

test('a role label inside a sentence keeps acronyms', () => {
  assert.equal(lowerFirst('Change manager'), 'change manager');
  assert.equal(lowerFirst('AI lead'), 'AI lead');
  assert.equal(lowerFirst('PMO'), 'PMO');
});

test('the AI line says how far an AI client may go', () => {
  assert.equal(aiLine({ access: 'read', personOnly: false, maxAiState: 'read-only', aiMaySubmit: [] }), 'AI clients may read it for the person.');
  assert.match(aiLine({ access: 'write', personOnly: true, maxAiState: 'read-only', aiMaySubmit: [] }), /A person only/);
  const draft = aiLine({ access: 'write', personOnly: false, maxAiState: 'draft', aiMaySubmit: ['intake-request', 'data-use'] });
  assert.match(draft, /a draft, which a person takes forward/);
  assert.match(draft, /It may submit intake requests and data uses\./);
  assert.equal(submitKindsText(['change-request']), 'change requests');
  assert.equal(submitKindsText(['policy']), 'policies');
});

test('tools group by toolset in tool number order', () => {
  const groups = groupByToolset([
    { tool: 'b', number: 12, toolset: 'intake' },
    { tool: 'a', number: 3, toolset: 'session' },
    { tool: 'c', number: 4, toolset: 'intake' },
  ]);
  assert.deepEqual(groups.map(g => g.label), ['Session', 'Intake']);
  assert.deepEqual(groups[1].tools.map(t => t.number), [4, 12]);
});

test('tool filters', () => {
  const write = { access: 'write' as const, personOnly: false, maxAiState: 'draft' };
  const personOnly = { access: 'write' as const, personOnly: true, maxAiState: 'read-only' };
  assert.equal(toolMatches(write, 'ai-writes'), true);
  assert.equal(toolMatches(personOnly, 'ai-writes'), false);
  assert.equal(toolMatches(personOnly, 'person-only'), true);
  assert.equal(toolMatches(write, 'read'), false);
});

test('roles: unbound, counts and filters', () => {
  const bound = role({ role: { key: 'platform-admin', label: 'Platform admin' }, heldByMe: true, bindings: [{ group: { objectId: GROUP, displayLabel: 'Platform admins' }, bindingMethod: 'entra-app-role-assignment', lastVerifiedOn: null }] });
  const unbound = role({ role: { key: 'auditor', label: 'Auditor' } });
  const workload = role({
    role: { key: 'deployment-recorder', label: 'Deployment recorder' },
    isWorkloadRole: true,
    workloadGrants: [{ principal: PIPELINE, reason: 'Records deployments', expiresOn: '2026-11-01', isExpired: true }],
  });
  assert.equal(isUnbound(bound), false);
  assert.equal(isUnbound(unbound), true);
  assert.equal(isUnbound(workload), false);
  assert.deepEqual(roleCounts([bound, unbound, workload]), { people: 2, bound: 1, unbound: 1, workload: 1, mine: 1, grants: 1, expiredGrants: 1 });
  assert.equal(roleMatches(bound, 'mine'), true);
  assert.equal(roleMatches(workload, 'unbound'), false);
});

test('grants sort by expiry, expired first, and read in words', () => {
  const r = role({
    isWorkloadRole: true,
    role: { key: 'deployment-recorder', label: 'Deployment recorder' },
    workloadGrants: [
      { principal: PIPELINE, reason: null, expiresOn: '2027-03-01', isExpired: false },
      { principal: PIPELINE, reason: null, expiresOn: '2026-12-10', isExpired: false },
      { principal: PIPELINE, reason: null, expiresOn: '2026-11-01', isExpired: true },
    ],
  });
  assert.deepEqual(grantsByExpiry([r]).map(g => g.expiresOn), ['2026-11-01', '2026-12-10', '2027-03-01']);
  assert.equal(daysUntil('2026-12-10', TODAY), 16);
  assert.deepEqual(grantExpiryText({ expiresOn: '2026-12-10', isExpired: false }, TODAY), { text: 'Ends in 16 days', tone: 'warning' });
  assert.deepEqual(grantExpiryText({ expiresOn: '2026-11-01', isExpired: true }, TODAY), { text: 'Ended 23 days ago', tone: 'danger' });
  assert.equal(grantExpiryText({ expiresOn: null, isExpired: true }, TODAY).tone, 'danger');
  assert.equal(grantExpiryText({ expiresOn: '2027-03-01', isExpired: false }, TODAY).tone, 'success');
});

test('binding findings come from the rows and name no key or object ID', () => {
  const check: RoleBindingCheck = {
    bindings: [
      { roleKey: 'platform-admin', group: { objectId: GROUP, displayLabel: 'Platform admins' }, bindingMethod: 'entra-app-role-assignment', groupExists: false, lastVerifiedOn: null },
      { roleKey: 'maker', group: { objectId: GROUP, displayLabel: 'Makers' }, bindingMethod: 'entra-app-role-assignment', groupExists: true, lastVerifiedOn: null },
    ],
    workloadGrants: [
      { principal: PIPELINE, principalKind: 'service-principal', roleKey: 'deployment-recorder', reason: 'Records deployments', decisionId: null, expiresOn: '2026-11-01', assignmentFound: false, isExpired: true },
    ],
    directAssignments: [
      { principal: { objectId: '00000000-0000-4000-8000-000000001101', displayLabel: 'Hana Lindqvist' }, principalKind: 'user', roleKey: 'auditor', where: 'Entra app role', coveredByGrant: false },
    ],
    findings: ['The group bound to platform-admin (00000000-0000-4000-8000-000000000105) does not exist in Entra.'],
    auditEntryId: null,
  };
  const found = bindingFindings(check, roleLabel);
  assert.equal(found.length, 4);
  assert.match(found[0].text, /Platform admins.*does not exist/);
  for (const f of found) {
    assert.doesNotMatch(f.text, /00000000-/);
    assert.doesNotMatch(f.text, /platform-admin|deployment-recorder/);
  }
  assert.equal(bindingFindings({ ...check, bindings: [check.bindings[1]], workloadGrants: [], directAssignments: [] }, roleLabel).length, 0);
});

test('a binding change starts from the role binding rows, or a new row', () => {
  const rows = [
    { key: 'maker-group', isActive: true, fields: { roleKey: 'maker', entraGroupObjectId: GROUP, bindingMethod: 'entra-app-role-assignment' } },
    { key: 'old-maker', isActive: false, fields: { roleKey: 'maker' } },
  ];
  assert.deepEqual(bindingOperations('maker', 'Maker', rows), [{ op: 'upsert', entity: 'role-group-binding', key: 'maker-group', values: rows[0].fields }]);
  const fresh = bindingOperations('auditor', 'Auditor', rows);
  assert.equal(fresh[0].key, 'auditor-group');
  assert.equal(fresh[0].values.roleKey, 'auditor');
});

const guideline = (over: Partial<GuidelineItem>): GuidelineItem => ({
  key: 'g17',
  number: 'G17',
  version: '1.0',
  title: 'People decide',
  rule: 'AI clients draft; a person accepts every decision.',
  homeChapter: null,
  chapters: [],
  isActive: true,
  isAdopted: true,
  notAdoptedReason: null,
  localText: null,
  localUrl: null,
  hubUrl: null,
  url: null,
  supersedes: null,
  usedBy: { approvalRules: 0, gateRequirements: 0, columnProtections: 0, refusalCodePrefixes: [] },
  ...over,
});

test('guidelines: filters, search, counts and numbers', () => {
  const list = [
    guideline({}),
    guideline({ key: 'g10', number: 'G10', isActive: false, title: 'Old rule' }),
    guideline({ key: 'g3', number: 'G3', isAdopted: false, title: 'Set a risk tier', notAdoptedReason: 'We tier by audit instead.' }),
  ];
  assert.deepEqual(guidelineCounts(list), { 'in-force': 2, 'not-adopted': 1, retired: 1, all: 3 });
  assert.equal(guidelineShown(list[1], 'in-force'), false);
  assert.equal(guidelineShown(list[2], 'not-adopted'), true);
  assert.equal(guidelineMatches(list[0], 'people ACCEPTS'), true);
  assert.equal(guidelineMatches(list[2], 'audit'), true);
  assert.equal(guidelineMatches(list[0], 'tier'), false);
  assert.equal(supersededNumber('g10', list), 'G10');
  assert.equal(supersededNumber(null, list), null);
  assert.equal(guidelineNumber('g17'), 'G17');
  assert.equal(guidelineNumber('G0'), undefined);
});

test('a guideline says what applies it', () => {
  assert.equal(usedByText({ approvalRules: 2, gateRequirements: 1, columnProtections: 0, refusalCodePrefixes: ['ai.'] }), 'Applied by 2 approval rules, 1 gate requirement. The engine names it when it refuses a call.');
  assert.equal(usedByText({ approvalRules: 0, gateRequirements: 0, columnProtections: 0, refusalCodePrefixes: [] }), 'No rule of the configuration names it yet.');
  assert.equal(chapterTitleText({ number: 1, title: 'Chapter 1. How to adopt the playbook' }), 'How to adopt the playbook');
  assert.equal(chapterTitleText({ number: 17, title: 'Slide-back signals' }), 'Slide-back signals');
  assert.equal(chapterTitleText({ number: 2, title: 'Chapter 12: Other' }), 'Chapter 12: Other');
});

const measure = (over: Partial<MeasureItem>): MeasureItem => ({
  key: 'lead-time',
  label: 'Lead time from idea to production',
  definition: 'Days from submission to production.',
  kind: 'flow',
  unit: 'days',
  watcher: null,
  guideline: 'G24',
  healthyDirection: 'down',
  value: null,
  numerator: null,
  denominator: null,
  trend: [],
  baseline: null,
  target: null,
  targetSource: 'not-set',
  secondary: null,
  rowCount: 0,
  ...over,
});
const point = (index: number, value: number | null) => ({ index, value, numerator: null, denominator: null });

test('measure values read in words', () => {
  assert.equal(measureValueText(12.46, 'days'), '12.5 days');
  assert.equal(measureValueText(1, 'days'), '1 day');
  assert.equal(measureValueText(83.33, 'percent'), '83%');
  assert.equal(measureValueText(6.25, 'percent'), '6.3%');
  assert.equal(measureValueText(1200, 'count'), '1,200');
  assert.equal(measureValueText(null, 'count'), 'No value');
  assert.equal(ratioText(10, 12), '10 of 12');
  assert.equal(ratioText(10, null), null);
});

test('the trend call reads the healthy direction', () => {
  const down = measure({ trend: [point(0, 20), point(1, null), point(2, 30), point(3, 14)] });
  assert.deepEqual(trendCall(down), { call: 'better', from: 30, to: 14 });
  const up = measure({ healthyDirection: 'up', unit: 'percent', trend: [point(4, 80), point(5, 70)] });
  assert.equal(trendCall(up)?.call, 'worse');
  assert.equal(trendCall(measure({ trend: [point(4, 5), point(5, 5.01)] }))?.call, 'same');
  // A coverage measure holds only the newest window: nothing to compare.
  assert.equal(trendCall(measure({ kind: 'coverage', trend: [point(0, null), point(5, 90)] })), null);
  // The newest window holds no value: no call on an older pair.
  assert.equal(trendCall(measure({ trend: [point(3, 10), point(4, 12), point(5, null)] })), null);
});

test('signals, drills and windows', () => {
  assert.equal(signalState({ firing: true, readBy: 'product' }), 'firing');
  assert.equal(signalState({ firing: null, readBy: 'outside' }), 'outside');
  assert.deepEqual(drillOf('lead-time', '3'), { measureKey: 'lead-time' });
  assert.deepEqual(drillOf(undefined, '10'), { signalNumber: 10 });
  assert.equal(drillOf(undefined, '11'), null);
  assert.equal(windowDaysOf('90'), 90);
  assert.equal(windowDaysOf('45'), 30);
});

test('the store schema against the version the host needs', () => {
  assert.equal(schemaState({ schemaVersion: 8, requiredSchemaVersion: 8 }).tone, 'success');
  assert.deepEqual(schemaState({ schemaVersion: 7, requiredSchemaVersion: 8 }), { text: 'Version 7; the host needs 8', tone: 'danger' });
  assert.equal(schemaState({ schemaVersion: null, requiredSchemaVersion: null }).tone, 'subtle');
  assert.equal(shortHash('sha256:0123456789abcdef0123'), '0123456789ab');
});

test('notices: loose items are typed, and whom they went to reads in words', () => {
  const good = {
    notice: 'escalate', subjectKind: 'approval', entity: 'approval-record', rowId: '00000000-0000-4000-8000-00000000c001', label: 'DEC-0112 approval',
    businessApplicationId: null, applicationName: null, ruleKey: null, reminderNumber: 2, escalated: true, channel: null, dueOn: null,
    to: { people: [{ objectId: '00000000-0000-4000-8000-000000001109', displayLabel: 'Dmitri Halloran' }], roles: [], ownerRole: null, toDeputies: true },
    recordUrl: 'https://eag.example.org/app/records/decision/00000000-0000-4000-8000-00000000d001',
  };
  const items = noticesOf([good, { notice: 'unknown', label: 'x', recordUrl: 'y', to: {} }, { notice: 'remind' }] as AdminStatus['notices']['items']);
  assert.equal(items.length, 1);
  assert.equal(toWhomText(items[0].to), 'Dmitri Halloran, and the deputies');
  assert.equal(toWhomText({ people: [], roles: [{ key: 'change-manager', label: 'Change manager' }], ownerRole: { key: 'business-owner', label: 'Business owner' }, toDeputies: false }), 'Change manager, the business owner');
});

const policy = (over: Partial<PolicyRow>): PolicyRow => ({
  tool: 'save_data_use', number: 79, toolset: 'data', access: 'write', entries: ['ai-client', 'person'], release: 'R1',
  isEnabled: true, isServed: true, allowedRoleKeys: ['maker'], anyoneSignedIn: false, relations: [], maxAiState: 'draft',
  aiMaySubmit: [], callsPerPersonPerHour: null, ...over,
});

test('the Tool Policy reads who may call a tool, and filters', () => {
  assert.equal(whoMayCall(policy({}), roleLabel), 'Maker');
  assert.equal(whoMayCall(policy({ anyoneSignedIn: true }), roleLabel), 'Anyone signed in');
  assert.equal(whoMayCall(policy({ relations: ['owner-of-application'] }), roleLabel), 'Maker, or an owner of the application');
  assert.equal(whoMayCall(policy({ allowedRoleKeys: [], relations: ['owner-of-application'] }), roleLabel), 'An owner of the application');
  assert.equal(policyMatches(policy({}), 'all', 'data use', roleLabel), true);
  assert.equal(policyMatches(policy({}), 'all', 'maker', roleLabel), true);
  assert.equal(policyMatches(policy({}), 'read', '', roleLabel), false);
  assert.equal(policyMatches(policy({ entries: ['person'] }), 'person-only', '', roleLabel), true);
  assert.equal(policyMatches(policy({ isEnabled: false }), 'off', '', roleLabel), true);
});

test('what one run of the daily check changed', () => {
  const counts: DueStateRefreshResult['counts'] = {
    reviewsOpened: 2, reviewsOverdue: 0, documentsStale: 1, decisionsRefreshed: 0, approvalsExpired: 0, approvalsCancelled: 0,
    approvalsOpened: 0, reminders: 3, escalations: 1, flagNotices: 0, failed: 1,
  };
  const lines = refreshLines(counts).filter(l => l.count > 0);
  assert.deepEqual(lines.map(l => l.text), ['Reviews opened for their next due day', 'Document turned stale', 'Reminders sent', 'Escalation sent', 'Item failed, the rest were kept']);
  assert.equal(refreshTotal(counts), 7);
});
