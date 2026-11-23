// The web app's addresses, and the record links the tools return (<base>/app/records/<entity>/<id>).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appHref, internalTarget, normalizeBase, parseAppPath } from './routes';

const BASE = '/app/';
const APP = '00000000-0000-4000-8000-000000000511';

test('the base path is normalized', () => {
  assert.equal(normalizeBase('/app/'), '/app/');
  assert.equal(normalizeBase('app'), '/app/');
  assert.equal(normalizeBase('/'), '/');
});

test('the home page is the overview, with or without the trailing slash', () => {
  assert.deepEqual(parseAppPath('/app/', '', BASE), { page: 'overview' });
  assert.deepEqual(parseAppPath('/app', '', BASE), { page: 'overview' });
  assert.deepEqual(parseAppPath('/app/overview', '', BASE), { page: 'overview' });
  assert.deepEqual(parseAppPath('/app/timeline', '', BASE), { page: 'timeline' });
});

test('a business application record link opens Application 360', () => {
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP}`, '', BASE), { page: 'app', id: APP });
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP.toUpperCase()}`, '', BASE), { page: 'app', id: APP });
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP}/map`, '', BASE), { page: 'map', id: APP });
});

test('a record link of another entity opens the plain "not shown here" panel', () => {
  const id = '00000000-0000-4000-8000-000000000901';
  assert.deepEqual(parseAppPath(`/app/records/decision/${id}`, '', BASE), { page: 'record', entity: 'decision', id });
  assert.deepEqual(parseAppPath(`/app/records/deployment-record/${id}`, '', BASE), { page: 'record', entity: 'deployment-record', id });
});

test('malformed record links are not found', () => {
  for (const path of [
    '/app/records/business-application/not-a-guid',
    '/app/records/business-application',
    '/app/records/Business_Application/00000000-0000-4000-8000-000000000511',
    `/app/records/decision/${APP}/map`,
    `/app/records/business-application/${APP}/history`,
    '/app/records/%E0%A4%A/x',
    '/app/anything',
    '/other/records/business-application/00000000-0000-4000-8000-000000000511',
  ]) {
    assert.deepEqual(parseAppPath(path, '', BASE), { page: 'not-found' }, path);
  }
});

test('the applications list reads its filter keys and drops malformed ones', () => {
  assert.deepEqual(parseAppPath('/app/applications', '?stage=build&tier=tier-1&kind=canvas-app&stage=<x>', BASE), {
    page: 'applications',
    filter: { lifecycleStageKeys: ['build'], tierKeys: ['tier-1'], applicationKindKeys: ['canvas-app'] },
  });
  assert.deepEqual(parseAppPath('/app/applications', '', BASE), { page: 'applications', filter: {} });
});

test('hrefs round-trip through the parser', () => {
  const routes = [
    { page: 'overview' as const },
    { page: 'timeline' as const },
    { page: 'app' as const, id: APP },
    { page: 'map' as const, id: APP },
    { page: 'applications' as const, filter: { tierKeys: ['tier-1', 'tier-2'] } },
  ];
  for (const route of routes) {
    const href = appHref(route, BASE);
    const url = new URL(href, 'https://product.example.org');
    assert.deepEqual(parseAppPath(url.pathname, url.search, BASE), route, href);
  }
  assert.equal(appHref({ page: 'app', id: APP }, BASE), `/app/records/business-application/${APP}`);
});

test('only links inside the web app become in-app navigation', () => {
  const origin = 'https://product.example.org';
  assert.equal(internalTarget(`/app/records/business-application/${APP}`, origin, BASE), `/app/records/business-application/${APP}`);
  assert.equal(internalTarget(`${origin}/app/applications?tier=tier-1`, origin, BASE), '/app/applications?tier=tier-1');
  assert.equal(internalTarget('https://other.example.org/app/', origin, BASE), null);
  assert.equal(internalTarget('/api/me', origin, BASE), null);
  assert.equal(internalTarget('/mcp', origin, BASE), null);
  assert.equal(internalTarget('/app/config.json', origin, BASE), null);
  assert.equal(internalTarget('https://wiki.example.org/page', origin, BASE), null);
});
