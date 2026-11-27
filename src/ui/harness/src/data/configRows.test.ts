// Child configuration rows in the shape get_configuration returns them: each value or outcome is a row of its own,
// keyed "<parent>/<child>", and the parent row carries no list (the server drops it when it compiles the snapshot).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { childRows, outcomesOf, type ConfigRow } from './configRows';

const row = (entity: string, key: string, label: string, fields: Record<string, unknown> = {}, isActive = true): ConfigRow =>
  ({ entity, key, label, isActive, origin: 'shipped', fields });

// As the server answers get_configuration { entity: 'list-value' }: rows in key order, across every list.
const listValues: ConfigRow[] = [
  row('list-value', 'change-result/cancelled', 'Canceled'),
  row('list-value', 'change-result/failed', 'Failed'),
  row('list-value', 'change-result/partly-succeeded', 'Partly succeeded'),
  row('list-value', 'change-result/rolled-back', 'Rolled back'),
  row('list-value', 'change-result/succeeded', 'Succeeded'),
  row('list-value', 'confidence/high', 'High', { sortOrder: 1 }),
  row('list-value', 'confidence/low', 'Low', { sortOrder: 3 }),
  row('list-value', 'confidence/medium', 'Medium', { sortOrder: 2 }),
  row('list-value', 'confidence/unsure', 'Unsure', {}, false),
  row('list-value', 'change-results/other', 'Another list with a longer key'),
];

test('the values of a list are its list-value rows, by key prefix', () => {
  assert.deepEqual(childRows(listValues, 'change-result').map(v => v.key), ['cancelled', 'failed', 'partly-succeeded', 'rolled-back', 'succeeded']);
  assert.deepEqual(childRows(listValues, 'change-result')[4], { key: 'succeeded', label: 'Succeeded' });
});

test('a sortOrder field orders the values, and inactive values are left out', () => {
  assert.deepEqual(childRows(listValues, 'confidence').map(v => v.label), ['High', 'Medium', 'Low']);
});

test('a list with no rows has no values', () => {
  assert.deepEqual(childRows(listValues, 'dismiss-reason'), []);
  assert.deepEqual(childRows([], 'change-result'), []);
});

test('the outcomes of a decision type are its decision-outcome rows; the type row carries none', () => {
  const outcomes = [
    row('decision-outcome', 'acceptance/accept', 'Accept'),
    row('decision-outcome', 'exception/accept', 'Accept the exception'),
    row('decision-outcome', 'exception/decline', 'Decline'),
  ];
  const type = row('decision-type', 'exception', 'Exception', { defaultSubjectKind: 'deployment-record', needsOptionsConsidered: true, requiresApproval: true });
  assert.deepEqual(outcomesOf(outcomes, type, 'exception'), [{ key: 'accept', label: 'Accept the exception' }, { key: 'decline', label: 'Decline' }]);
});

test('an older type row that lists its own outcomes is read when no outcome rows came back', () => {
  const type = row('decision-type', 'design', 'Design', { outcomes: [{ key: 'approve', label: 'Approve' }, { key: 'rework' }, 'x', null] });
  assert.deepEqual(outcomesOf([], type, 'design'), [{ key: 'approve', label: 'Approve' }, { key: 'rework', label: 'rework' }]);
  assert.deepEqual(outcomesOf([], undefined, 'design'), []);
});
