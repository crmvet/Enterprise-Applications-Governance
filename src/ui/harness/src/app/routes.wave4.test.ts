// The wave 4 addresses: the Teams list and a team's page (and its record link), the audit trail's client filter, and
// the applications register's uses AI and build path filters that find_applications now takes.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { TeamKind } from '../data/contracts';
import { appHref, hasRecordPage, parseAppPath, recordPath, recordRoute, routeTitle, TEAM_KINDS, type AppRoute, type TeamKindRoute } from './routes';

const BASE = '/app/';
const TEAM = '00000000-0000-4000-8000-000000000111';
const APP = '00000000-0000-4000-8000-000000000511';
const ID = '00000000-0000-4000-8000-000000000901';

test('the Teams list reads its kind, search and role, and drops values it does not know', () => {
  assert.deepEqual(parseAppPath('/app/teams', '', BASE), { page: 'teams' });
  assert.deepEqual(parseAppPath('/app/teams', '?kind=committee&q=%20change%20&role=change-board', BASE), {
    page: 'teams',
    kind: 'committee',
    text: 'change',
    roleKey: 'change-board',
  });
  assert.deepEqual(parseAppPath('/app/teams', '?kind=committees&role=Change_Board&q=', BASE), { page: 'teams' });
  for (const kind of TEAM_KINDS) assert.deepEqual(parseAppPath('/app/teams', `?kind=${kind}`, BASE), { page: 'teams', kind });
  assert.equal(appHref({ page: 'teams', kind: 'support-team', text: 'field apps', roleKey: 'support-owner' }, BASE), '/app/teams?kind=support-team&q=field+apps&role=support-owner');
  assert.equal(appHref({ page: 'teams' }, BASE), '/app/teams');
});

test("a team's page is its group's object ID, and the team record link opens it", () => {
  assert.deepEqual(parseAppPath(`/app/teams/${TEAM.toUpperCase()}`, '', BASE), { page: 'team', id: TEAM });
  assert.deepEqual(parseAppPath(`/app/records/team/${TEAM}`, '', BASE), { page: 'team', id: TEAM });
  assert.deepEqual(recordRoute('team', TEAM.toUpperCase()), { page: 'team', id: TEAM });
  assert.equal(hasRecordPage('team'), true);
  assert.equal(appHref({ page: 'team', id: TEAM }, BASE), `/app/teams/${TEAM}`);
  assert.equal(recordPath(`https://product.example.org/app/records/team/${TEAM}`, BASE), `/app/teams/${TEAM}`);
  for (const path of ['/app/teams/change-board', `/app/teams/${TEAM}/members`, '/app/records/team/change-board', `/app/records/team/${TEAM}/map`]) {
    assert.deepEqual(parseAppPath(path, '', BASE), { page: 'not-found' }, path);
  }
});

test('the route kinds are the kinds list_teams takes', () => {
  const same: Record<TeamKind, TeamKindRoute> = { committee: 'committee', 'support-team': 'support-team', 'role-group': 'role-group', other: 'other' };
  assert.deepEqual(Object.keys(same), [...TEAM_KINDS]);
});

test("the audit trail keeps a client app's name in the address", () => {
  assert.deepEqual(parseAppPath('/app/audit', `?app=${APP}&client=Claude%20Code`, BASE), { page: 'audit', applicationId: APP, clientApp: 'Claude Code' });
  assert.deepEqual(parseAppPath('/app/audit', '?client=%20%20', BASE), { page: 'audit' });
  assert.deepEqual(parseAppPath('/app/audit', '?client=Claude%0ACode', BASE), { page: 'audit' });
  assert.equal(appHref({ page: 'audit', actorKinds: ['ai-client-for-person'], clientApp: 'Claude Code' }, BASE), '/app/audit?actor=ai-client-for-person&client=Claude+Code');
});

test('the applications register keeps uses AI and build path in the address; "unknown" and "not stated" stay its own', () => {
  assert.deepEqual(parseAppPath('/app/applications', '?ai=yes&build=ai-built&build=citizen&build=ai-built', BASE), {
    page: 'applications',
    filter: { usesAi: true, buildPathKeys: ['ai-built', 'citizen'] },
  });
  assert.deepEqual(parseAppPath('/app/applications', '?ai=no', BASE), { page: 'applications', filter: { usesAi: false } });
  assert.deepEqual(parseAppPath('/app/applications', '?ai=unknown&build=not-stated&build=Bad_Key', BASE), { page: 'applications', filter: {} });
  assert.equal(appHref({ page: 'applications', filter: { tierKeys: ['tier-3'], usesAi: false, buildPathKeys: ['pro'], text: 'crew' } }, BASE), '/app/applications?tier=tier-3&ai=no&build=pro&q=crew');
});

test('the wave 4 hrefs round-trip through the parser and every page has a title', () => {
  const routes: AppRoute[] = [
    { page: 'teams' },
    { page: 'teams', kind: 'committee' },
    { page: 'teams', kind: 'role-group', text: 'readers', roleKey: 'reader' },
    { page: 'team', id: TEAM },
    { page: 'audit', recordId: ID, clientApp: 'Claude Code' },
    { page: 'applications', filter: { usesAi: true, buildPathKeys: ['ai-built'], registrationMeanings: ['draft'] } },
  ];
  for (const route of routes) {
    const href = appHref(route, BASE);
    const url = new URL(href, 'https://product.example.org');
    assert.deepEqual(parseAppPath(url.pathname, url.search, BASE), route, href);
    assert.notEqual(routeTitle(route), 'Page not found', href);
  }
  assert.equal(routeTitle({ page: 'teams' }), 'Teams and committees');
  assert.equal(routeTitle({ page: 'teams', kind: 'support-team' }), 'Teams: Support teams');
  assert.equal(routeTitle({ page: 'team', id: TEAM }), 'Team');
});

test('Approvals keeps its search and decision type in the address, and drops a type it cannot read (product review, fix round 2)', () => {
  assert.deepEqual(parseAppPath('/app/approvals', '?view=open&q=%20exception%20&type=acceptance', BASE), {
    page: 'approvals',
    view: 'open',
    text: 'exception',
    typeKey: 'acceptance',
  });
  assert.deepEqual(parseAppPath('/app/approvals', '?type=Not_A_Key&q=', BASE), { page: 'approvals' });
  assert.equal(appHref({ page: 'approvals', view: 'open', text: 'desk booking', typeKey: 'exception' }, BASE), '/app/approvals?view=open&q=desk+booking&type=exception');
  assert.equal(appHref({ page: 'approvals' }, BASE), '/app/approvals');
});
