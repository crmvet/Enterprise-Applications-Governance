// The draft builder's field facts (entity-fields.json) match the configuration bundle schema, the engine's contract for
// a draft patch. When the schema changes, run the tests with CONFIG_FIELDS_WRITE=1 to write the file again, then look at
// the diff.
import assert from 'node:assert/strict';
// The harness has no Node type package (its code runs in the browser); the test runs on Node, which has both modules.
// @ts-expect-error node:fs has no type declarations here
import fs from 'node:fs';
// @ts-expect-error node:path has no type declarations here
import path from 'node:path';
import { test } from 'node:test';
import { deriveFieldSpecs, entitySpec, FIELD_SPECS, fieldLabelOf, plainSchemaText, type EntitySpec } from './fieldSpecs';

declare const process: { cwd(): string; env: Record<string, string | undefined> };

// The tests run from the harness folder (scripts/run-tests.mjs).
const HARNESS = process.cwd();
const SCHEMA: string = path.resolve(HARNESS, '..', '..', 'config', 'schema', 'config-bundle.schema.json');
const OUT: string = path.resolve(HARNESS, 'src', 'pages', 'configuration', 'entity-fields.json');

test('entity-fields.json is derived from the bundle schema', () => {
  const derived = deriveFieldSpecs(JSON.parse(fs.readFileSync(SCHEMA, 'utf8')));
  if (process.env.CONFIG_FIELDS_WRITE === '1') {
    fs.writeFileSync(OUT, `${JSON.stringify(derived, null, 1)}\n`);
    return;
  }
  assert.deepEqual(FIELD_SPECS, derived, 'entity-fields.json is out of date: run CONFIG_FIELDS_WRITE=1 npm test in src/ui/harness.');
});

test('field labels read as words', () => {
  assert.equal(fieldLabelOf('approverOwnerRoleKey'), 'Approver owner role');
  assert.equal(fieldLabelOf('aiUseRule'), 'AI use rule');
  assert.equal(fieldLabelOf('sourceUrl'), 'Source URL');
  assert.equal(fieldLabelOf('label'), 'Label');
});

test('the approval rule names its references and its required fields', () => {
  const spec = entitySpec('approval-rule') as EntitySpec;
  assert.ok(spec);
  assert.equal(spec.code, 'C32');
  assert.equal(spec.name, 'Approval rule');
  const field = (name: string) => spec.fields.find(f => f.name === name);
  assert.deepEqual([field('decisionTypeKey')?.kind, field('decisionTypeKey')?.ref, field('decisionTypeKey')?.required], ['key', 'decision-type', true]);
  assert.equal(field('approvalMode')?.kind, 'choice');
  assert.deepEqual(field('approvalMode')?.values, ['everyone-must-approve', 'first-to-respond', 'sequential']);
  assert.equal(field('requiresTypedReason')?.kind, 'yes-no');
  assert.equal(field('portfolioId')?.working, true);
  assert.equal(spec.fields[0].name, 'label');
});

test('child rows know their parent, and a domain its value list reference', () => {
  assert.equal(entitySpec('list-value')?.parent, 'value-list');
  assert.equal(entitySpec('decision-outcome')?.parent, 'decision-type');
  assert.equal(entitySpec('tier-answer-option')?.parent, 'tier-question');
  const retention = entitySpec('data-domain')?.fields.find(f => f.name === 'retentionClassKey');
  assert.deepEqual([retention?.kind, retention?.ref, retention?.list], ['key', 'list-value', 'retention-class']);
  assert.equal(entitySpec('gate-policy')?.fields.find(f => f.name === 'tierKey')?.any, true);
  assert.equal(entitySpec('setting')?.fields.find(f => f.name === 'value')?.kind, 'setting-value');
});

test('schema text drops its references to the design', () => {
  assert.equal(plainSchemaText('One node of the tree, held flat with its parent key (data taxonomy, D-37).'), 'One node of the tree, held flat with its parent key.');
  assert.equal(plainSchemaText('The domain above this one. No cycles (V49).'), 'The domain above this one. No cycles.');
  assert.equal(plainSchemaText('Fields every row has (document 21, section 3.2).'), 'Fields every row has.');
  assert.equal(plainSchemaText('Kinds of store (such as a lake) from any vendor.'), 'Kinds of store (such as a lake) from any vendor.');
});
