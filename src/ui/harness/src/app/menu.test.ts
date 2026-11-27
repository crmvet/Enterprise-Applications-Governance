// The top menu: what sits in the bar, what goes under More, which pages a person's roles leave out, and which entry
// each page belongs to.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { groupEntries, menuEntries, menuKeyOf, splitMenu } from './menu';
import { appHref, parseAppPath, type AppRoute } from './routes';

const APP = '00000000-0000-4000-8000-000000000511';
const ID = '00000000-0000-4000-8000-000000000901';

test('a wide screen keeps the daily pages in the bar, Changes, Reviews, Owners and Teams among them, and the rest under More', () => {
  const { bar, more } = splitMenu(menuEntries(null), 'wide');
  assert.deepEqual(bar.map(e => e.key), ['work', 'approvals', 'applications', 'overview', 'intake', 'changes', 'reviews', 'owners', 'teams']);
  assert.deepEqual(more.map(e => e.key), [
    'demand', 'deployments', 'documents', 'gap-check', 'retirement', 'portfolios', 'agents', 'data', 'places', 'decisions', 'timeline', 'audit',
    'ai-activity', 'configuration', 'roles', 'guidelines', 'measures', 'admin',
  ]);
});

test('a medium screen keeps six pages in the bar and moves the overview, owners and teams under More', () => {
  const { bar, more } = splitMenu(menuEntries(null), 'medium');
  assert.deepEqual(bar.map(e => e.key), ['work', 'approvals', 'applications', 'intake', 'changes', 'reviews']);
  assert.ok(more.some(e => e.key === 'overview'));
  assert.ok(more.some(e => e.key === 'owners'));
  assert.ok(more.some(e => e.key === 'teams'));
});

test('a phone keeps three pages in the bar and every other page under More', () => {
  const { bar, more } = splitMenu(menuEntries(null), 'narrow');
  assert.deepEqual(bar.map(e => e.key), ['work', 'approvals', 'applications']);
  assert.equal(bar.length + more.length, menuEntries(null).length);
});

test('More groups its pages as the menu defines them', () => {
  const { more } = splitMenu(menuEntries(APP, { demo: true }), 'narrow');
  assert.deepEqual(groupEntries(more).map(g => g.label), ['Home', 'Work', 'Register', 'Record', 'AI', 'Govern', 'Demo', 'Open application']);
  const work = groupEntries(more).find(g => g.group === 'work');
  assert.deepEqual(work?.items.map(e => e.key), ['intake', 'changes', 'reviews', 'owners', 'teams', 'demand', 'deployments', 'documents', 'gap-check', 'retirement']);
  const register = groupEntries(more).find(g => g.group === 'register');
  // Applications sits in the bar on every screen; Data comes between Agents and Places.
  assert.deepEqual(register?.items.map(e => e.key), ['portfolios', 'agents', 'data', 'places']);
  const govern = groupEntries(more).find(g => g.group === 'govern');
  assert.deepEqual(govern?.items.map(e => e.label), ['Configuration', 'Roles and access', 'Guidelines', 'Measures', 'Admin']);
});

test('the Demo guide shows only in a demo install', () => {
  assert.equal(menuEntries(null).some(e => e.key === 'demo-guide'), false);
  assert.equal(menuEntries(null, { demo: false }).some(e => e.key === 'demo-guide'), false);
  assert.equal(menuEntries(null, { demo: true }).some(e => e.key === 'demo-guide'), true);
});

test('a page whose tools the person cannot call is left out; while whoami is unknown every page shows', () => {
  const reader = new Set(['whoami', 'find_applications', 'get_my_queue', 'list_approvals']);
  const keys = menuEntries(null, { tools: reader }).map(e => e.key);
  assert.equal(keys.includes('audit'), false);
  assert.equal(keys.includes('gap-check'), false);
  assert.equal(keys.includes('decisions'), true);
  const auditor = new Set(['get_audit_trail', 'list_claims']);
  const auditorKeys = menuEntries(null, { tools: auditor }).map(e => e.key);
  assert.equal(auditorKeys.includes('audit'), true);
  assert.equal(auditorKeys.includes('gap-check'), true);
  const unknown = menuEntries(null, { tools: null }).map(e => e.key);
  assert.equal(unknown.includes('audit'), true);
  assert.equal(unknown.includes('gap-check'), true);
  assert.equal(unknown.includes('admin'), true);
});

test('the wave 3 pages follow the tools too: Admin needs get_admin_status, the others a read any role holds', () => {
  const reader = new Set(['whoami', 'find_data_domains', 'get_data_gaps', 'list_configuration_changes', 'get_configuration', 'get_role_access', 'list_guidelines', 'get_measures']);
  const keys = menuEntries(null, { tools: reader }).map(e => e.key);
  for (const k of ['data', 'configuration', 'roles', 'guidelines', 'measures']) assert.equal(keys.includes(k as never), true, k);
  assert.equal(keys.includes('admin'), false);
  const admin = menuEntries(null, { tools: new Set(['get_admin_status']) }).map(e => e.key);
  assert.equal(admin.includes('admin'), true);
  assert.equal(admin.includes('data'), false);
  assert.equal(admin.includes('roles'), false);
});

test('a phone lists the wave 3 pages in the side sheet, grouped, while the bar keeps three pages', () => {
  const { bar, more } = splitMenu(menuEntries(null), 'narrow');
  assert.equal(bar.length, 3);
  const groups = groupEntries(more);
  assert.ok(groups.find(g => g.group === 'register')?.items.some(e => e.key === 'data'));
  assert.deepEqual(groups.find(g => g.group === 'govern')?.items.map(e => e.short), ['Configuration', 'Roles', 'Guidelines', 'Measures', 'Admin']);
});

test('Application 360 and the map join More once an application is open', () => {
  assert.equal(menuEntries(null).some(e => e.key === 'app' || e.key === 'map'), false);
  const { more } = splitMenu(menuEntries(APP), 'wide');
  const open = more.filter(e => e.group === 'open-app');
  assert.deepEqual(open.map(e => e.route), [{ page: 'app', id: APP }, { page: 'map', id: APP }]);
});

test('every menu entry opens its own page, and that page highlights it', () => {
  for (const entry of menuEntries(APP, { demo: true })) {
    const href = appHref(entry.route, '/app/');
    const url = new URL(href, 'https://product.example.org');
    const route = parseAppPath(url.pathname, url.search, '/app/');
    assert.equal(menuKeyOf(route), entry.key, href);
  }
});

test('a record page highlights its list; pages with no entry highlight nothing', () => {
  const cases: [AppRoute, string | null][] = [
    [{ page: 'decision', id: ID }, 'approvals'],
    [{ page: 'intake-new' }, 'intake'],
    [{ page: 'intake-edit', id: ID }, 'intake'],
    [{ page: 'intake-request', id: ID }, 'intake'],
    [{ page: 'person', objectId: ID }, 'owners'],
    [{ page: 'teams', kind: 'committee' }, 'teams'],
    [{ page: 'team', id: ID }, 'teams'],
    [{ page: 'review', id: ID }, 'reviews'],
    [{ page: 'change-new' }, 'changes'],
    [{ page: 'change-request', id: ID }, 'changes'],
    [{ page: 'deployment-record', id: ID }, 'deployments'],
    [{ page: 'deployment-new' }, 'deployments'],
    [{ page: 'document-link', id: ID }, 'documents'],
    [{ page: 'claim', id: ID }, 'gap-check'],
    [{ page: 'gap-check-run', id: ID }, 'gap-check'],
    [{ page: 'place', id: ID }, 'places'],
    [{ page: 'portfolio', id: ID }, 'portfolios'],
    [{ page: 'evidence', subjectKind: 'decision', id: ID }, 'audit'],
    [{ page: 'retirement', applicationId: APP }, 'retirement'],
    [{ page: 'search', text: 'DEC-0123' }, null],
    [{ page: 'data', tab: 'uses', applicationId: APP }, 'data'],
    [{ page: 'data-domain', key: 'payroll' }, 'data'],
    [{ page: 'data-location', id: ID }, 'data'],
    [{ page: 'data-mapping', id: ID }, 'data'],
    [{ page: 'data-use', id: ID }, 'data'],
    [{ page: 'configuration', view: 'drafts' }, 'configuration'],
    [{ page: 'configuration-draft', id: ID }, 'configuration'],
    [{ page: 'roles', roleKey: 'auditor' }, 'roles'],
    [{ page: 'guidelines', guideline: 'G17' }, 'guidelines'],
    [{ page: 'measures' }, 'measures'],
    [{ page: 'admin' }, 'admin'],
    [{ page: 'record', entity: 'technical-component', id: ID }, null],
    [{ page: 'not-found' }, null],
  ];
  for (const [route, key] of cases) assert.equal(menuKeyOf(route), key, route.page);
});

test('Teams sits right after Owners, in the bar from 1024 px, and shows only to a person who may call list_teams', () => {
  const all = menuEntries(null);
  const at = all.findIndex(e => e.key === 'owners');
  assert.equal(all[at + 1]?.key, 'teams');
  assert.equal(all[at + 1]?.group, 'work');
  assert.equal(all[at + 1]?.bar, 'wide');
  assert.equal(menuEntries(null, { tools: new Set(['list_teams']) }).some(e => e.key === 'teams'), true);
  assert.equal(menuEntries(null, { tools: new Set(['get_team', 'whoami']) }).some(e => e.key === 'teams'), false);
  assert.equal(menuEntries(null, { tools: null }).some(e => e.key === 'teams'), true);
  // On a phone the bar still keeps three pages; Teams is in the side sheet under Work.
  const { bar, more } = splitMenu(all, 'narrow');
  assert.equal(bar.length, 3);
  assert.ok(groupEntries(more).find(g => g.group === 'work')?.items.some(e => e.key === 'teams'));
});
