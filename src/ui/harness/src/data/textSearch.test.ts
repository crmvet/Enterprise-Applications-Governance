// The shared text search of the list pages (wave 4): words, accents, record numbers and the empty query.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { filterByText, matchesText, normalizeText, searchWords } from './textSearch';

test('text is compared in lower case, without accents or extra spaces', () => {
  assert.equal(normalizeText('  Café   Ops '), 'cafe ops');
  assert.deepEqual(searchWords('Payroll  "Field Ops"'), ['payroll', 'field', 'ops']);
  assert.deepEqual(searchWords('   '), []);
  assert.deepEqual(searchWords(null), []);
  assert.equal(searchWords('a b c d e f g h i j k l m n').length, 12);
});

test('every word must be found, each in any field', () => {
  assert.equal(matchesText('field pay', 'Field Ops', 'Payroll'), true);
  assert.equal(matchesText('field hr', 'Field Ops', 'Payroll'), false);
  assert.equal(matchesText('insp', 'Inspection Scheduler'), true);
  assert.equal(matchesText('nadia', null, undefined, ['Lena Okoro', 'Nadia Ferreira']), true);
  assert.equal(matchesText('fe', 'Fé'), true);
  assert.equal(matchesText('x', null, undefined), false);
});

test('a record number matches with or without its dash', () => {
  assert.equal(matchesText('dec0101', 'DEC-0101'), true);
  assert.equal(matchesText('DEC-0101', 'DEC0101'), true);
  assert.equal(matchesText('chg-0207', 'CHG-0208'), false);
});

test('an empty query keeps every row; a query keeps the matching rows in order', () => {
  const rows = [{ name: 'Change board' }, { name: 'Field Apps Support' }, { name: 'Architects' }];
  assert.equal(filterByText(rows, '', r => [r.name]), rows);
  assert.deepEqual(filterByText(rows, 'a', r => [r.name]).map(r => r.name), ['Change board', 'Field Apps Support', 'Architects']);
  assert.deepEqual(filterByText(rows, 'support', r => [r.name]).map(r => r.name), ['Field Apps Support']);
  assert.deepEqual(filterByText(rows, 'nobody', r => [r.name]), []);
});
