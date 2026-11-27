// The Configuration page's logic: values in words, new keys, the operations a draft sends, a diff in words, the data
// domains a change touches, gate levels and preset matches.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ConfigurationDiffEntry, DataDomainItem, PresetList } from '../../data/contracts';
import { entitySpec, type EntitySpec } from './fieldSpecs';
import {
  acceptedHowText,
  areaOf,
  diffWords,
  draftStatesOf,
  gateLevelIn,
  keyFromLabel,
  lookupOf,
  missingRequired,
  operationsFromDiff,
  presetMatch,
  rowFacts,
  rowMatches,
  rowSummary,
  touchedDomains,
  touchesDataUses,
  upsertValues,
  valueText,
  type ConfigRow,
} from './logic';

const row = (entity: string, key: string, label: string, fields: Record<string, unknown> = {}, isActive = true): ConfigRow =>
  ({ entity, key, label, isActive, origin: 'shipped', fields: { label, ...fields } });

const ROWS: ConfigRow[] = [
  row('decision-type', 'go-live', 'Go-live'),
  row('tier', 'tier-1', 'Tier 1', { rank: 1 }),
  row('tier', 'tier-2', 'Tier 2', { rank: 2 }),
  row('target-stage', 'test', 'Test', { rank: 1 }),
  row('target-stage', 'production', 'Production', { rank: 2 }),
  row('change-type', 'normal', 'Normal'),
  row('owner-role', 'data-owner', 'Data owner'),
  row('role', 'security-reviewer', 'Security reviewer'),
  row('list-value', 'retention-class/keep-7-years', 'Keep 7 years'),
  row('tier-answer-option', 'sensitivity/restricted', 'Restricted data'),
  row('gate-policy', 'any-test', 'Any tier, test', { tierKey: '*', changeTypeKey: '*', targetStageKey: 'test', gateLevel: 'record' }),
  row('gate-policy', 'tier-2-production', 'Tier 2 production', { tierKey: 'tier-2', changeTypeKey: 'normal', targetStageKey: 'production', gateLevel: 'enforce', failMode: 'fail-closed' }),
  row('gate-policy', 'old', 'Old', { tierKey: 'tier-1', changeTypeKey: 'normal', targetStageKey: 'production', gateLevel: 'enforce' }, false),
  row('approval-rule', 'go-live-tier-2-data-owner', 'Tier 2 go-live: data owner', { decisionTypeKey: 'go-live', tierKey: 'tier-2', approverOwnerRoleKey: 'data-owner', approvalMode: 'everyone-must-approve', requiresTypedReason: true }),
];
const lookup = lookupOf(ROWS);

test('a key field shows the label of the row it names, a list value its list label, Any for the wildcard', () => {
  const spec = entitySpec('gate-policy') as EntitySpec;
  const tier = spec.fields.find(f => f.name === 'tierKey') ?? null;
  assert.equal(valueText(tier, 'tier-2', {}, lookup), 'Tier 2');
  assert.equal(valueText(tier, '*', {}, lookup), 'Any');
  assert.equal(valueText(tier, 'tier-9', {}, lookup), 'Tier 9');
  const retention = entitySpec('data-domain')?.fields.find(f => f.name === 'retentionClassKey') ?? null;
  assert.equal(valueText(retention, 'keep-7-years', {}, lookup), 'Keep 7 years');
  const implied = entitySpec('data-classification')?.fields.find(f => f.name === 'impliedAnswerKey') ?? null;
  assert.equal(valueText(implied, 'restricted', { impliedTierQuestionKey: 'sensitivity' }, lookup), 'Restricted data');
});

test('values never show raw JSON or an ID', () => {
  const binding = entitySpec('role-group-binding')?.fields.find(f => f.name === 'entraGroupObjectId') ?? null;
  assert.equal(valueText(binding, '00000000-0000-4000-8000-000000000106', {}, lookup), 'Set');
  const rule = entitySpec('gate-policy')?.fields.find(f => f.name === 'emergencyRule') ?? null;
  assert.equal(valueText(rule, { allowed: true }, {}, lookup), 'Set');
  const value = entitySpec('setting')?.fields.find(f => f.name === 'value') ?? null;
  assert.equal(valueText(value, [{ a: 1 }, { b: 2 }], { valueType: 'json' }, lookup), 'A list of 2 items');
  assert.equal(valueText(value, true, { valueType: 'yes-no' }, lookup), 'Yes');
  assert.equal(valueText(value, 'built-in', { valueType: 'contract:change-management-mode' }, lookup), 'Built in');
  assert.equal(valueText(value, 'tier-1', { valueType: 'key:tier' }, lookup), 'Tier 1');
});

test('an approval rule and a gate policy read as sentences', () => {
  const rule = ROWS.find(r => r.entity === 'approval-rule')!;
  assert.equal(rowSummary('approval-rule', rule.fields, lookup), 'Go-live (Tier 2): the data owner approves; everyone must approve, a typed reason.');
  const gate = ROWS.find(r => r.key === 'tier-2-production')!;
  assert.equal(rowSummary('gate-policy', gate.fields, lookup), 'Tier 2, normal change, production: enforce, fail closed.');
});

test('the facts of a row leave out the key, the label and empty fields', () => {
  const facts = rowFacts('gate-policy', { key: 'x', label: 'X', tierKey: 'tier-2', gateLevel: 'advise', failMode: '' }, lookup);
  assert.deepEqual(facts.map(f => `${f.label}: ${f.text}`), ['Tier: Tier 2', 'Gate level: Advise']);
});

test('the search finds a row by any word of its label, kind or summary', () => {
  const rule = ROWS.find(r => r.entity === 'approval-rule')!;
  assert.ok(rowMatches(rule, 'data owner', lookup));
  assert.ok(rowMatches(rule, 'APPROVAL go-live', lookup));
  assert.ok(!rowMatches(rule, 'payroll', lookup));
});

test('a new key comes from the label and is never one the entity used before', () => {
  assert.equal(keyFromLabel('Bank details', new Set()), 'bank-details');
  assert.equal(keyFromLabel('Bank details', new Set(['bank-details'])), 'bank-details-2');
  assert.equal(keyFromLabel('2026 Rules: Tier 3!', new Set()), 'rules-tier-3');
  assert.equal(keyFromLabel('Keep 10 years', new Set(), 'retention-class'), 'retention-class/keep-10-years');
  assert.equal(keyFromLabel('Ünïcode café', new Set()), 'unicode-cafe');
  assert.equal(keyFromLabel('!!!', new Set()), 'row');
  assert.ok(keyFromLabel('x'.repeat(100), new Set()).length <= 64);
});

test('a change sends only the fields that differ; a new row every field that is set', () => {
  const current = { label: 'Tier 2 production', tierKey: 'tier-2', gateLevel: 'advise', failMode: undefined };
  assert.deepEqual(upsertValues({ ...current, gateLevel: 'enforce', failMode: 'fail-closed' }, current), { gateLevel: 'enforce', failMode: 'fail-closed' });
  assert.deepEqual(upsertValues({ ...current }, current), {});
  assert.deepEqual(upsertValues({ label: 'New', tierKey: 'tier-1', description: '', gateLevel: undefined }, null), { label: 'New', tierKey: 'tier-1' });
});

test('a new row lists the required fields it lacks, by label', () => {
  const spec = entitySpec('approval-rule') as EntitySpec;
  assert.deepEqual(missingRequired(spec, { label: 'X' }), ['Decision type', 'Approval mode']);
  assert.deepEqual(missingRequired(spec, { label: 'X', decisionTypeKey: 'go-live', approvalMode: 'sequential' }), []);
});

const DIFF: ConfigurationDiffEntry[] = [
  { entity: 'gate-policy', key: 'tier-2-production', op: 'updated', before: { label: 'Tier 2 production', tierKey: 'tier-2', gateLevel: 'advise' }, after: { label: 'Tier 2 production', tierKey: 'tier-2', gateLevel: 'enforce', failMode: 'fail-closed' } },
  { entity: 'data-domain', key: 'bank-details', op: 'added', before: null, after: { label: 'Bank details', parentKey: 'payroll', aiUseRule: 'never' } },
  { entity: 'tier', key: 'tier-1', op: 'retired', before: { label: 'Tier 1' }, after: null },
];

test('a diff reads in words: what changed, from what to what', () => {
  const changed = diffWords(DIFF[0], lookup);
  assert.equal(changed.verb, 'Changed');
  assert.equal(changed.entity, 'Gate policy');
  assert.equal(changed.title, 'Tier 2 production');
  assert.deepEqual(changed.lines, [
    { label: 'Gate level', before: 'Advise', after: 'Enforce' },
    { label: 'Fail mode', before: 'Not set', after: 'Fail closed' },
  ]);
  const added = diffWords(DIFF[1], lookup);
  assert.equal(added.verb, 'Added');
  assert.deepEqual(added.lines.map(l => l.label), ['Parent', 'AI use rule']);
  assert.equal(diffWords(DIFF[2], lookup).lines.length, 0);
});

test('a stored draft rebuilds its operations from its diff', () => {
  assert.deepEqual(operationsFromDiff(DIFF), [
    { op: 'upsert', entity: 'gate-policy', key: 'tier-2-production', values: { gateLevel: 'enforce', failMode: 'fail-closed' } },
    { op: 'upsert', entity: 'data-domain', key: 'bank-details', values: { label: 'Bank details', parentKey: 'payroll', aiUseRule: 'never' } },
    { op: 'retire', entity: 'tier', key: 'tier-1' },
  ]);
});

const domain = (key: string, parentKey: string | null, classification: string, uses: number, level = 'subdomain'): DataDomainItem => ({
  key, label: key, path: parentKey ? `${parentKey} / ${key}` : key, parentKey, level: { key: level, label: level }, isActive: true, declarable: true,
  classification: { key: classification, label: classification, rank: 1, setOn: { key, label: key } },
  owner: null, deputy: null, steward: null,
  rules: { use: { value: 'allowed', setOn: null, fromClassification: null }, ai: { value: 'allowed', setOn: null, fromClassification: null }, copy: { value: 'allowed', setOn: null, fromClassification: null } },
  counts: { mappings: 0, uses, openDecisions: 0 }, gaps: [], recordUrl: '',
});

test('a change to a domain touches its subtree; a change to a classification every domain it classifies', () => {
  const domains = [domain('people', null, 'confidential', 1, 'domain'), domain('payroll', 'people', 'restricted', 2), domain('bank-details', 'payroll', 'restricted', 3), domain('sales', null, 'internal', 4, 'domain')];
  const keys = (diff: ConfigurationDiffEntry[]) => touchedDomains(diff, domains).map(d => d.key).sort();
  assert.deepEqual(keys([{ entity: 'data-domain', key: 'payroll', op: 'updated', before: {}, after: {} }]), ['bank-details', 'payroll']);
  assert.deepEqual(keys([{ entity: 'data-classification', key: 'internal', op: 'updated', before: {}, after: {} }]), ['sales']);
  assert.deepEqual(keys([{ entity: 'data-domain-level', key: 'domain', op: 'updated', before: {}, after: {} }]), ['people', 'sales']);
  assert.deepEqual(keys([{ entity: 'data-domain', key: 'new-one', op: 'added', before: null, after: {} }]), []);
  assert.ok(touchesDataUses([{ entity: 'data-classification' }]));
  assert.ok(!touchesDataUses([{ entity: 'data-platform' }, { entity: 'tier' }]));
});

test('the gate level in force is the most specific active row', () => {
  assert.equal(gateLevelIn(ROWS, 'tier-2', 'production'), 'enforce');
  assert.equal(gateLevelIn(ROWS, 'tier-1', 'test'), 'record');
  assert.equal(gateLevelIn(ROWS, 'tier-1', 'production'), null);
});

test('a full preset matches when its tiers and gate levels are in force', () => {
  const preset: PresetList['presets'][number] = {
    presetKey: 'balanced', label: 'Balanced', presetKind: 'full', extends: null, conflictsWith: [], productRelease: '0.1.0',
    summary: { tiers: [{ key: 'tier-1', label: 'Tier 1' }, { key: 'tier-2', label: 'Tier 2' }], gateLevelsByTierAndStage: [{ tierKey: 'tier-2', targetStageKey: 'production', gateLevel: 'enforce' }, { tierKey: 'tier-1', targetStageKey: 'production', gateLevel: 'advise' }], changeManagementMode: 'built-in', approvalsSummary: '' },
  };
  assert.deepEqual(presetMatch(preset, ROWS), { matched: 3, total: 4 });
});

test('versions and drafts in words', () => {
  assert.equal(acceptedHowText('approval', ['Nadia Ferreira on 24 Nov 2026']), 'Accepted by Nadia Ferreira on 24 Nov 2026');
  assert.equal(acceptedHowText('bootstrap', []), 'The first version, written when the product was installed');
  assert.deepEqual(draftStatesOf('open'), ['draft', 'submitted']);
  assert.equal(draftStatesOf('all'), undefined);
  assert.equal(areaOf('approval-rule'), 'decisions');
  assert.equal(areaOf('data-platform'), 'data');
});
