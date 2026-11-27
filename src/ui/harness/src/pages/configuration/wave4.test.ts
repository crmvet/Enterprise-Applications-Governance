// Wave 4 on the Configuration page: the import's checks before sending, a domain's name in the import's answer, and the
// search over versions and drafts. Fictional names only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ConfigurationChanges, ConfigurationDiffEntry } from '../../data/contracts';
import { filterByText } from '../../data/textSearch';
import { csvHeader, csvProblem, domainLabelOf, draftSearchFields, IMPORT_CSV_MAX, versionSearchFields } from './logic';

test('the import checks a CSV for a header with a label column and at least one row before it is sent', () => {
  assert.deepEqual(csvHeader('﻿key, "Label" ,parent\r\npeople,People,'), ['key', 'label', 'parent']);
  assert.equal(csvProblem('   '), 'Choose a file, or paste the CSV text.');
  assert.match(csvProblem('key,name\npeople,People') ?? '', /label column/);
  assert.match(csvProblem('key,label\n') ?? '', /no domain/);
  assert.equal(csvProblem('key,label,parent\npeople,People,\npayroll,Payroll,people\n'), null);
  assert.match(csvProblem(`label\n${'x'.repeat(IMPORT_CSV_MAX)}`) ?? '', /too long/);
});

test('a domain in the import answer reads by its configured label, the label the draft gives it, or its key in words', () => {
  const lookup = (entity: string, key: string) => (entity === 'data-domain' && key === 'people' ? 'People' : null);
  const diff: ConfigurationDiffEntry[] = [{ entity: 'data-domain', key: 'payroll', op: 'added', before: null, after: { label: 'Payroll and pensions' } }];
  assert.equal(domainLabelOf('people', diff, lookup), 'People');
  assert.equal(domainLabelOf('payroll', diff, lookup), 'Payroll and pensions');
  assert.equal(domainLabelOf('field-safety', diff, lookup), 'Field safety');
});

type Version = ConfigurationChanges['versions'][number];
type Draft = ConfigurationChanges['drafts'][number];

test('the versions and drafts are found by number, person, decision and change', () => {
  const version = (n: number, over: Partial<Version>): Version => ({
    version: n,
    isActive: false,
    baseVersion: n - 1,
    publishedOn: null,
    contentHash: null,
    changeSummary: [],
    instructedBy: null,
    decision: null,
    acceptedBy: [],
    ...over,
  } as Version);
  const versions = [
    version(2, { instructedBy: { objectId: 'a', displayLabel: 'Yusuf Brennan' }, changeSummary: ['Visitor log: the AI use rule becomes never'] }),
    version(3, { acceptedBy: [{ principal: { objectId: 'b', displayLabel: 'Jonah Pereira' }, answeredOn: null, askedAs: null }] }),
  ];
  const found = (q: string) => filterByText(versions, q, versionSearchFields).map(v => v.version);
  assert.deepEqual(found('visitor never'), [2]);
  assert.deepEqual(found('jonah'), [3]);
  assert.deepEqual(found('version 3'), [3]);

  const draft = (over: Partial<Draft>): Draft => ({
    configurationDraftId: 'x',
    recordNumber: null,
    state: { key: 'draft', label: 'Draft' },
    baseVersion: 3,
    isUpgradeDraft: false,
    changeSummary: [],
    operationCount: 1,
    isValid: true,
    errorCount: 0,
    warningCount: 0,
    instructedBy: null,
    submittedOn: null,
    expiresOn: null,
    discardedOn: null,
    discardReason: null,
    resultingVersion: null,
    ...over,
  } as Draft);
  const drafts = [
    draft({ configurationDraftId: 'a', recordNumber: 'CFG-0004', changeSummary: ['Adds the data domain Payroll'] }),
    draft({ configurationDraftId: 'b', state: { key: 'submitted', label: 'Submitted' }, instructedBy: { objectId: 'a', displayLabel: 'Yusuf Brennan' } }),
  ];
  const ids = (q: string) => filterByText(drafts, q, draftSearchFields).map(d => d.configurationDraftId);
  assert.deepEqual(ids('cfg0004'), ['a']);
  assert.deepEqual(ids('payroll'), ['a']);
  assert.deepEqual(ids('submitted yusuf'), ['b']);
});
