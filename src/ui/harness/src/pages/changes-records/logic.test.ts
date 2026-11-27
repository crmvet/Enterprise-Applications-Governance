// The Changes and Records pages' own logic: the change draft input, the window in the viewer's zone, the list filters
// and figures, the suggested result, the change a deployment may belong to, the month calendar and the record hints.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ScheduledChange } from '../../data/contracts';
import {
  addMonths,
  appHintOf,
  buildChangeDraft,
  changeFigures,
  durationText,
  emptyChangeForm,
  filterChanges,
  isProblem,
  lastOfMonth,
  localInputToUtc,
  monthWeeks,
  nearbyChanges,
  NO_FILTER,
  suggestResult,
  utcToLocalInput,
  windowMinutes,
  withAppHint,
} from './logic';

const APP = '00000000-0000-4000-8000-000000000513';
const OTHER = '00000000-0000-4000-8000-000000000514';
const TARGET = '00000000-0000-4000-8000-000000000522';

const form = () => ({ ...emptyChangeForm(), title: ' Release 3.5 ', changeTypeKey: 'normal', description: ' New export. ' });

test('a datetime-local value goes to UTC with no milliseconds and comes back unchanged', () => {
  const utc = localInputToUtc('2026-12-02T18:00');
  assert.ok(utc && /^2026-12-0[23]T\d{2}:\d{2}:00Z$/.test(utc));
  assert.equal(utcToLocalInput(utc), '2026-12-02T18:00');
  assert.equal(localInputToUtc(''), null);
  assert.equal(localInputToUtc('tomorrow'), null);
  assert.equal(utcToLocalInput(null), '');
});

test('the draft input trims, leaves out empty parts and drops the main application from the affected list', () => {
  const built = buildChangeDraft({
    ...form(),
    affected: [
      { businessApplicationId: APP, impactRoleKey: 'informed' },
      { businessApplicationId: OTHER, impactRoleKey: 'depends-on-change' },
      { businessApplicationId: OTHER.toUpperCase(), impactRoleKey: 'informed' },
    ],
  }, APP);
  assert.ok(!isProblem(built));
  assert.deepEqual(built, {
    title: 'Release 3.5',
    businessApplicationId: APP,
    changeTypeKey: 'normal',
    description: 'New export.',
    affected: [{ businessApplicationId: OTHER, impactRoleKey: 'depends-on-change' }],
  });
});

test('editing a draft sends its ID, the window, the target and the linked change', () => {
  const built = buildChangeDraft({
    ...form(),
    windowStart: '2026-12-02T18:00',
    windowEnd: '2026-12-02T19:30',
    plannedTargetId: TARGET,
    externalChangeNumber: 'CHG100',
    externalChangeUrl: 'https://itsm.example.org/change/100',
  }, APP, 'd0000000-0000-4000-8000-000000000002');
  assert.ok(!isProblem(built));
  if (isProblem(built)) return;
  assert.equal(built.changeRequestId, 'd0000000-0000-4000-8000-000000000002');
  assert.equal(built.plannedTargetId, TARGET);
  assert.equal(windowMinutes(built.windowStart ?? null, built.windowEnd ?? null), 90);
  assert.equal(built.externalChangeNumber, 'CHG100');
});

test('the page names the field at fault before anything is sent', () => {
  const problem = (values: Partial<ReturnType<typeof form>>) => {
    const built = buildChangeDraft({ ...form(), ...values }, APP);
    return isProblem(built) ? built.field : null;
  };
  assert.equal(problem({ title: '  ' }), 'title');
  assert.equal(problem({ changeTypeKey: '' }), 'changeTypeKey');
  assert.equal(problem({ description: '' }), 'description');
  assert.equal(problem({ windowStart: '2026-12-02T18:00' }), 'windowEnd');
  assert.equal(problem({ windowStart: '2026-12-02T18:00', windowEnd: '2026-12-02T17:00' }), 'windowEnd');
  assert.equal(problem({ externalChangeNumber: 'CHG1' }), 'externalChangeUrl');
  assert.equal(problem({ externalChangeNumber: 'CHG1', externalChangeUrl: 'http://itsm.example.org/1' }), 'externalChangeUrl');
  assert.equal(problem({}), null);
});

const change = (over: Partial<ScheduledChange>): ScheduledChange => ({
  changeRequestId: 'c',
  title: 'Change',
  businessApplicationId: APP,
  applicationName: 'Crew Roster',
  target: { deploymentTargetId: TARGET, label: 'Field Ops Prod', targetStageKey: 'production' },
  windowStart: '2026-11-27T18:00:00Z',
  windowEnd: '2026-11-27T19:00:00Z',
  changeType: { key: 'normal', label: 'Normal' },
  status: { label: 'Submitted', meaning: 'submitted' },
  authorized: false,
  authorizationStatus: null,
  ...over,
});

test('the figures count the rows their filter shows', () => {
  const now = Date.parse('2026-11-24T09:00:00Z');
  const items = [
    change({ changeRequestId: 'a' }),
    change({ changeRequestId: 'b', authorized: true, status: { label: 'Submitted', meaning: 'submitted' } }),
    change({ changeRequestId: 'c', status: { label: 'In progress', meaning: 'in-progress' }, authorized: true, windowStart: '2026-11-20T18:00:00Z', windowEnd: '2026-11-20T19:00:00Z' }),
    change({ changeRequestId: 'd', changeType: { key: 'emergency', label: 'Emergency' }, status: { label: 'Completed', meaning: 'completed' } }),
    change({ changeRequestId: 'e', status: { label: 'Draft', meaning: 'draft' } }),
  ];
  const figures = Object.fromEntries(changeFigures(items, now).map(f => [f.key, f.value]));
  assert.deepEqual(figures, { waiting: 1, coming: 1, 'in-progress': 1, emergency: 0, drafts: 1 });
  for (const f of changeFigures(items, now)) assert.equal(filterChanges(items, f.filter, now).length, f.value);
  assert.equal(filterChanges(items, NO_FILTER, now).length, 5);
  assert.equal(filterChanges(items, { ...NO_FILTER, timing: 'past' }, now).map(c => c.changeRequestId).join(), 'c');
});

test('the deployments suggest a result only when they say one', () => {
  assert.equal(suggestResult([]), null);
  assert.equal(suggestResult(['not-deployed']), null);
  assert.equal(suggestResult(['succeeded', 'succeeded']), 'succeeded');
  assert.equal(suggestResult(['failed']), 'failed');
  assert.equal(suggestResult(['succeeded', 'failed']), 'partly-succeeded');
  assert.equal(suggestResult(['partly-succeeded']), 'partly-succeeded');
});

test('a deployment\'s nearby changes put the window that holds it first and skip other targets', () => {
  const holds = change({ changeRequestId: 'holds', title: 'Holds' });
  const later = change({ changeRequestId: 'later', title: 'Later', windowStart: '2026-11-28T18:00:00Z', windowEnd: '2026-11-28T19:00:00Z' });
  const elsewhere = change({ changeRequestId: 'elsewhere', target: { deploymentTargetId: OTHER, label: 'Test', targetStageKey: 'test' } });
  const otherApp = change({ changeRequestId: 'other-app', businessApplicationId: OTHER });
  const found = nearbyChanges([later, elsewhere, otherApp, holds], { businessApplicationId: APP, deploymentTargetId: TARGET, finishedOn: '2026-11-27T18:20:00Z' });
  assert.deepEqual(found.map(c => c.changeRequestId), ['holds', 'later']);
});

test('the month calendar starts on a Monday and covers every day of the month', () => {
  const weeks = monthWeeks('2026-11-01');
  assert.equal(weeks[0][0], '2026-10-26');
  assert.ok(weeks.every(w => w.length === 7));
  const days = weeks.flat();
  assert.ok(days.includes('2026-11-01') && days.includes('2026-11-30'));
  assert.equal(lastOfMonth('2026-02-01'), '2026-02-28');
  assert.equal(addMonths('2026-12-01', 1), '2027-01-01');
  assert.equal(addMonths('2026-01-01', -1), '2025-12-01');
});

test('durations read in words', () => {
  assert.equal(durationText(90), '1 hour 30 minutes');
  assert.equal(durationText(45), '45 minutes');
  assert.equal(durationText(1500), '1 day 1 hour');
});

test('the application hint is a lowercase ID or nothing', () => {
  assert.equal(appHintOf(`?app=${APP.toUpperCase()}`), APP);
  assert.equal(appHintOf('?app=nope'), null);
  assert.equal(appHintOf(''), null);
  assert.equal(withAppHint('/app/records/claim/x', APP), `/app/records/claim/x?app=${APP}`);
  assert.equal(withAppHint('/app/records/claim/x', null), '/app/records/claim/x');
  assert.equal(withAppHint(null, APP), null);
});
