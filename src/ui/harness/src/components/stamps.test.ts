// The AI mark and the stamp line: words from the engine's stamp, never from the reader's own session.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { StampedBy } from '../data/contracts';
import { aiMarkText, stampText } from './stamps';

const priya = { objectId: '00000000-0000-4000-8000-000000000211', displayLabel: 'Priya Castell' };
const claude = { objectId: '00000000-0000-4000-8000-0000000000c1', displayLabel: 'Claude Code' };

const byAi: StampedBy = { actorKind: 'ai-client-for-person', principal: claude, onBehalfOf: priya, clientAppLabel: 'Claude Code', stampedOn: '2026-11-24T09:00:00Z' };
const byPerson: StampedBy = { actorKind: 'person', principal: priya, onBehalfOf: null, clientAppLabel: 'Web app, demo operator', stampedOn: null };

test('a row an AI client created says who drafted it and for whom', () => {
  assert.equal(aiMarkText(true, byAi), 'Drafted by Claude Code for Priya Castell');
  assert.equal(aiMarkText(false, byAi), 'Drafted by Claude Code for Priya Castell');
  assert.equal(aiMarkText(true, null), 'Drafted by an AI client');
});

test('an AI draft a person edited still says an AI drafted it, never the web app', () => {
  assert.equal(aiMarkText(true, byPerson), 'Drafted by an AI client, then edited by Priya Castell');
});

test('an AI draft its person edited keeps the drafted by pair (B22-15)', () => {
  const drafted = { clientAppLabel: 'Claude Code', forPrincipal: priya };
  assert.equal(aiMarkText(true, { ...byPerson, draftedBy: drafted }), 'Drafted by Claude Code for Priya Castell');
  const nadia = { objectId: '00000000-0000-4000-8000-000000000212', displayLabel: 'Nadia Ferreira' };
  assert.equal(aiMarkText(true, { ...byPerson, principal: nadia, draftedBy: drafted }), 'Drafted by Claude Code for Priya Castell, then edited by Nadia Ferreira');
  assert.equal(aiMarkText(true, { ...byAi, draftedBy: drafted }), 'Drafted by Claude Code for Priya Castell');
});

test('a person\'s row and an unstamped row carry no AI mark', () => {
  assert.equal(aiMarkText(false, byPerson), null);
  assert.equal(aiMarkText(false, null), null);
});

test('the stamp line names the actor, the person acted for, the client app and the time', () => {
  assert.match(stampText(byAi) ?? '', /^Last change by Claude Code for Priya Castell through Claude Code, /);
  assert.equal(stampText(byPerson), 'Last change by Priya Castell through Web app, demo operator');
  assert.equal(stampText(null), null);
});
