// The applications register's logic: the uses AI and build path filters over the cards read, their counts, the
// registration words, and the save_application_draft input of "Register an app already built". Fictional names and
// patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  BUILD_NOT_STATED,
  buildRegisterInput,
  emptyComponent,
  emptyRegisterForm,
  isProblem,
  keepRow,
  scanCounts,
  shownRegistrations,
  usesAiOf,
  type CardFacts,
  type RegisterForm,
} from './logic';

// The input, or the test fails with the problem the form found.
function built(form: RegisterForm, roles?: { business: boolean; technical: boolean }) {
  const r = buildRegisterInput(form, roles);
  if (isProblem(r)) throw new Error(r.problem);
  return r;
}

const PRIYA = { objectId: '00000000-0000-4000-8000-000000001101', displayLabel: 'Priya Castell' };
const DMITRI = { objectId: '00000000-0000-4000-8000-000000001109', displayLabel: 'Dmitri Halloran' };
const PORTFOLIO = '00000000-0000-4000-8000-000000000201';

const facts = (usesAi: boolean | null, path: string | null): CardFacts => ({
  usesAi,
  buildPath: path ? { key: path, label: path === 'low-code' ? 'Low code' : 'Pro code' } : null,
  registration: 'registered',
});

test('uses AI reads yes, no, or not stated', () => {
  assert.equal(usesAiOf(true), 'yes');
  assert.equal(usesAiOf(false), 'no');
  assert.equal(usesAiOf(null), 'unknown');
  assert.equal(usesAiOf(undefined), 'unknown');
});

test('a row passes the page filters once its card is read', () => {
  assert.equal(keepRow(undefined, undefined, undefined), true);
  assert.equal(keepRow(undefined, 'yes', undefined), null);
  assert.equal(keepRow(facts(true, 'low-code'), 'yes', undefined), true);
  assert.equal(keepRow(facts(false, 'low-code'), 'yes', undefined), false);
  assert.equal(keepRow(facts(null, null), 'unknown', BUILD_NOT_STATED), true);
  assert.equal(keepRow(facts(true, 'pro-code'), 'yes', 'low-code'), false);
  assert.equal(keepRow(facts(true, 'low-code'), undefined, 'low-code'), true);
});

test('the counts: each uses AI answer, and each build path by count with "not stated" last', () => {
  const c = scanCounts([facts(true, 'low-code'), facts(true, 'low-code'), facts(false, 'pro-code'), facts(null, null)]);
  assert.deepEqual(c.usesAi, { yes: 2, no: 1, unknown: 1 });
  assert.deepEqual(c.buildPaths.map(b => [b.key, b.count]), [['low-code', 2], ['pro-code', 1], [BUILD_NOT_STATED, 1]]);
  assert.equal(c.read, 4);
  assert.deepEqual(scanCounts([]).buildPaths, []);
});

test('with no registration chosen the list shows registered applications', () => {
  assert.deepEqual(shownRegistrations(undefined), ['registered']);
  assert.deepEqual(shownRegistrations([]), ['registered']);
  assert.deepEqual(shownRegistrations(['draft', 'archived']), ['draft', 'archived']);
});

const filled = (over: Partial<RegisterForm> = {}): RegisterForm => ({
  ...emptyRegisterForm(PRIYA),
  name: '  Site inspection app ',
  purpose: 'Records site inspections on a phone.',
  kindKey: 'power-platform-app',
  ...over,
});

test('the register input: the trimmed facts, the business owner proposed, empty fields left out', () => {
  const input = built(filled());
  assert.deepEqual(input, {
    name: 'Site inspection app',
    purpose: 'Records site inspections on a phone.',
    applicationKindKey: 'power-platform-app',
    proposedOwners: [{ ownerRoleKey: 'business-owner', objectId: PRIYA.objectId }],
  });
});

test('the register input carries the portfolio, build path, uses AI, the technical owner and the components', () => {
  const input = built(filled({
    portfolioId: PORTFOLIO,
    buildPathKey: 'low-code',
    usesAi: 'no',
    technicalOwner: DMITRI,
    components: [
      { kindKey: 'canvas-app', externalId: ' app-0001 ', url: 'https://apps.example.org/app-0001', label: 'Site inspection' },
      emptyComponent(),
    ],
  }));
  assert.equal(input.portfolioId, PORTFOLIO);
  assert.equal(input.buildPathKey, 'low-code');
  assert.equal(input.usesAi, false);
  assert.deepEqual(input.proposedOwners, [
    { ownerRoleKey: 'business-owner', objectId: PRIYA.objectId },
    { ownerRoleKey: 'technical-owner', objectId: DMITRI.objectId },
  ]);
  assert.deepEqual(input.components, [{ componentKindKey: 'canvas-app', externalId: 'app-0001', url: 'https://apps.example.org/app-0001', labelAsSeen: 'Site inspection' }]);
});

test('the register input names the field at fault', () => {
  const field = (form: RegisterForm) => {
    const r = buildRegisterInput(form);
    return isProblem(r) ? r.field : null;
  };
  assert.equal(field(filled({ name: ' ' })), 'name');
  assert.equal(field(filled({ purpose: '' })), 'purpose');
  assert.equal(field(filled({ kindKey: '' })), 'applicationKindKey');
  assert.equal(field(filled({ businessOwner: null })), 'proposedOwners');
  assert.equal(field(filled({ technicalOwner: PRIYA })), 'proposedOwners');
  assert.equal(field(filled({ components: [{ kindKey: '', externalId: 'x', url: '', label: '' }] })), 'components.componentKindKey');
  assert.equal(field(filled({ components: [{ kindKey: 'canvas-app', externalId: 'x', url: 'http://apps.example.org/x', label: '' }] })), 'components.url');
  assert.equal(field(filled({ components: [{ kindKey: 'canvas-app', externalId: '', url: 'https://apps.example.org/x', label: '' }] })), 'components.externalId');
});

test('an install with no business owner role needs no business owner', () => {
  const input = built(filled({ businessOwner: null }), { business: false, technical: true });
  assert.equal(input.proposedOwners, undefined);
});
