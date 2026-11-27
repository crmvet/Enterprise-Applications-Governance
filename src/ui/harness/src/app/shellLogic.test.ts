// The top bar: the My work count and the global search box.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { QueueItem } from '../data/contracts';
import { countText, myWorkCount, parseSearch } from './shellLogic';

const item = (kind: QueueItem['kind'], extra: Partial<QueueItem> = {}): QueueItem => ({
  kind,
  subjectKind: kind === 'review' ? 'review' : 'decision',
  subjectId: '00000000-0000-4000-8000-000000000901',
  title: 'A row',
  businessApplicationId: null,
  dueOn: null,
  status: null,
  why: 'Why it needs you.',
  recordUrl: 'https://product.example.org/app/records/decision/00000000-0000-4000-8000-000000000901',
  nextTool: null,
  isAiCreated: false,
  performedBy: null,
  ...extra,
});

test('the My work count adds the approvals asked of the person to the queue items My work lists', () => {
  assert.equal(myWorkCount(null, [], false), null);
  assert.equal(myWorkCount(2, null, false), null);
  assert.deepEqual(myWorkCount(0, [], false), { count: 0, more: false });
  assert.deepEqual(myWorkCount(3, [item('review'), item('document'), item('draft')], false), { count: 6, more: false });
  assert.deepEqual(myWorkCount(1, [item('review')], true), { count: 2, more: true });
});

test('the count reads short on the tab', () => {
  assert.equal(countText({ count: 7, more: false }), '7');
  assert.equal(countText({ count: 7, more: true }), '7+');
  assert.equal(countText({ count: 140, more: false }), '99+');
});

test('a record number reads in any spelling, with four digits at least', () => {
  assert.deepEqual(parseSearch(' DEC-0123 ').recordNumber, { prefix: 'DEC', entity: 'decision', number: 'DEC-0123' });
  assert.deepEqual(parseSearch('dec 123').recordNumber, { prefix: 'DEC', entity: 'decision', number: 'DEC-0123' });
  assert.deepEqual(parseSearch('chg7').recordNumber, { prefix: 'CHG', entity: 'change-request', number: 'CHG-0007' });
  assert.deepEqual(parseSearch('IR-12345').recordNumber, { prefix: 'IR', entity: 'intake-request', number: 'IR-12345' });
  assert.deepEqual(parseSearch('app-0042').recordNumber, { prefix: 'APP', entity: 'business-application', number: 'APP-0042' });
  assert.deepEqual(parseSearch('REV-0000').recordNumber, { prefix: 'REV', entity: 'review', number: 'REV-0000' });
  assert.deepEqual(parseSearch('du 7').recordNumber, { prefix: 'DU', entity: 'data-use', number: 'DU-0007' });
  assert.equal(parseSearch('decision log').recordNumber, null);
  assert.equal(parseSearch('DEC-').recordNumber, null);
});

test('words search applications, intake requests and people; a number searches records only', () => {
  assert.deepEqual(parseSearch('  crew   planner '), { text: 'crew planner', recordNumber: null, people: true });
  assert.equal(parseSearch('DEC-0123').people, false);
  assert.equal(parseSearch('a').people, false);
  assert.equal(parseSearch('42').people, false);
  assert.equal(parseSearch('x'.repeat(300)).text.length, 100);
});
