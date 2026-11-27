// Wave 4b: a date and time names its zone at its own moment, and a review request's due-date hint
// names the days the rules in force set. This file runs in a process of its own (node --test), so the zone it sets
// does not reach the other tests.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { configureDisplayZone, fmtDateTime, fmtWindow, reviewDueHint, zoneLabel } from './format';

// Node reads its zone again when TZ is set (the typecheck has no Node types, so the process object is typed here).
(globalThis as unknown as { process: { env: Record<string, string | undefined> } }).process.env.TZ = 'America/New_York';

test('in the viewer\'s zone, a July time reads EDT and a December time EST, whatever today is', () => {
  configureDisplayZone('local', new Date('2026-11-25T12:00:00Z'));
  assert.equal(zoneLabel(), 'EST');
  assert.equal(fmtDateTime('2026-07-01T16:00:00Z'), '1 Jul 2026, 12:00 EDT');
  assert.equal(fmtDateTime('2026-12-01T17:00:00Z'), '1 Dec 2026, 12:00 EST');
  assert.equal(fmtWindow('2026-07-01T22:00:00Z', '2026-07-02T01:00:00Z'), '1 Jul, 18:00 to 21:00 EDT');
  // A window across the clock change names the zone after each time.
  assert.equal(fmtWindow('2026-11-01T05:00:00Z', '2026-11-01T06:30:00Z'), '1 Nov, 01:00 EDT to 01:30 EST');
  configureDisplayZone('local', new Date('2026-07-01T12:00:00Z'));
  assert.equal(zoneLabel(), 'EDT');
  assert.equal(fmtDateTime('2026-12-01T17:00:00Z'), '1 Dec 2026, 12:00 EST');
});

test('in UTC display every time still reads UTC', () => {
  configureDisplayZone('utc');
  assert.equal(fmtDateTime('2026-07-01T16:00:00Z'), '1 Jul 2026, 16:00 UTC');
  assert.equal(fmtWindow('2026-07-01T22:00:00Z', '2026-07-02T01:00:00Z'), '1 Jul, 22:00 to 2 Jul, 01:00 UTC');
});

test('a review request left without a date is due in the days the rules in force set', () => {
  assert.equal(reviewDueHint(14), 'Optional. Empty: due in 14 days, as the rules in force set.');
  assert.equal(reviewDueHint(1), 'Optional. Empty: due in 1 day, as the rules in force set.');
  assert.equal(reviewDueHint(0), 'Optional. Empty: due today, as the rules in force set.');
  // Not read yet, not set, or not a number: the rules still set it.
  for (const value of [undefined, null, '14', Number.NaN, -1]) assert.equal(reviewDueHint(value), 'Optional. Empty: due on the day the rules in force set.');
});
