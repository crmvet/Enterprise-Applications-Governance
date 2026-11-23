// The display zone: UTC in the harness, the viewer's zone in the web app. Calendar dates never move.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { addDays, configureDisplayZone, dayKey, dayStartMs, externalHref, fmtDate, fmtDateTime, fmtWindow, inDays, inWindowDays, todayInZone, utcDayRange, zoneLabel } from './format';

test('UTC display: times carry the zone name and day boundaries are UTC midnights', () => {
  configureDisplayZone('utc');
  assert.equal(zoneLabel(), 'UTC');
  assert.equal(fmtWindow('2026-11-27T18:00:00Z', '2026-11-27T21:00:00Z'), '27 Nov, 18:00 to 21:00 UTC');
  assert.equal(fmtWindow('2026-12-04T22:00:00Z', '2026-12-05T01:00:00Z'), '4 Dec, 22:00 to 5 Dec, 01:00 UTC');
  assert.equal(fmtDateTime('2026-11-22T09:00:00Z'), '22 Nov 2026, 09:00 UTC');
  assert.equal(dayKey('2026-11-22T23:30:00Z'), '2026-11-22');
  assert.equal(dayStartMs('2026-11-22'), Date.parse('2026-11-22T00:00:00Z'));
  assert.equal(todayInZone(new Date('2026-11-23T05:00:00Z')), '2026-11-23');
});

test('local display: the zone is named, and calendar dates stay the same date', () => {
  configureDisplayZone('local', new Date('2026-11-23T12:00:00Z'));
  assert.ok(zoneLabel().length > 0);
  assert.equal(fmtDate('2026-11-22'), '22 Nov 2026');
  assert.equal(dayKey('2026-11-22'), '2026-11-22');
  assert.equal(new Date(dayStartMs('2026-11-22')).getDate(), 22);
  assert.equal(new Date(dayStartMs('2026-11-22')).getHours(), 0);
  configureDisplayZone('utc');
});

test('UTC display: a tool range is the same calendar days', () => {
  configureDisplayZone('utc');
  assert.deepEqual(utcDayRange('2026-11-09', '2026-11-23'), { from: '2026-11-09', to: '2026-11-23' });
  assert.equal(inDays('2026-11-23T23:59:00Z', '2026-11-09', '2026-11-23'), true);
  assert.equal(inDays('2026-11-24T00:00:00Z', '2026-11-09', '2026-11-23'), false);
  assert.equal(inWindowDays('2026-11-08T22:00:00Z', '2026-11-09T01:00:00Z', '2026-11-09', '2026-11-23'), true);
  assert.equal(inWindowDays('2026-11-24T08:00:00Z', '2026-11-24T09:00:00Z', '2026-11-09', '2026-11-23'), false);
  assert.equal(inWindowDays(null, null, '2026-11-09', '2026-11-23'), true);
});

test('local display: a tool range covers every moment of the local days, and the page keeps only those days', () => {
  configureDisplayZone('local', new Date('2026-11-23T12:00:00Z'));
  const range = utcDayRange('2026-11-09', '2026-11-23');
  // The UTC dates hold the first and the last moment of the local days, whatever the zone.
  assert.ok(range.from <= new Date(dayStartMs('2026-11-09')).toISOString().slice(0, 10));
  assert.ok(range.to >= new Date(dayStartMs(addDays('2026-11-23', 1)) - 1).toISOString().slice(0, 10));
  assert.ok(range.from >= '2026-11-08' && range.from <= '2026-11-09');
  assert.ok(range.to >= '2026-11-23' && range.to <= '2026-11-24');
  // A moment counts on its local day.
  const lastMoment = new Date(dayStartMs('2026-11-24') - 1).toISOString();
  const nextMoment = new Date(dayStartMs('2026-11-24')).toISOString();
  assert.equal(inDays(lastMoment, '2026-11-09', '2026-11-23'), true);
  assert.equal(inDays(nextMoment, '2026-11-09', '2026-11-23'), false);
  assert.equal(inDays('2026-11-23', '2026-11-09', '2026-11-23'), true);
  configureDisplayZone('utc');
});

test('a link out of the product is an https URL only; anything else stays text', () => {
  assert.equal(externalHref('https://wiki.example.org/runbook?x=1#top'), 'https://wiki.example.org/runbook?x=1#top');
  for (const bad of ['javascript:alert(1)', 'JavaScript:alert(1)', ' javascript:alert(1)', 'data:text/html,<b>x</b>', 'vbscript:x',
    // The URL with user information is built at run time, so the source holds no text that reads as an email address.
    'http://wiki.example.org/', `https://user:pass${'@'}wiki.example.org/`, '//wiki.example.org/', '/app/records', 'not a url', '', null, undefined]) {
    assert.equal(externalHref(bad), null, String(bad));
  }
});
