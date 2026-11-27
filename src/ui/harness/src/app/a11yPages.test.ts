// The accessibility check (scripts/a11y.mjs --app) opens every page of the web app: each address of
// scripts/a11y-pages.mjs is a page of the routes, and together they open every page kind.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appPages, DEMO_IDS } from '../../scripts/a11y-pages.mjs';
import { parseAppPath, type AppPage } from './routes';

// Every page kind of the routes. A new kind fails the typecheck here until it is listed, and then the first test below
// until scripts/a11y-pages.mjs opens it.
const EVERY_PAGE: Record<AppPage, true> = {
  overview: true,
  app: true,
  map: true,
  timeline: true,
  work: true,
  search: true,
  applications: true,
  approvals: true,
  intake: true,
  'intake-new': true,
  'intake-edit': true,
  demand: true,
  owners: true,
  person: true,
  reviews: true,
  changes: true,
  'change-new': true,
  deployments: true,
  'deployment-new': true,
  places: true,
  'gap-check': true,
  'ai-activity': true,
  agents: true,
  audit: true,
  evidence: true,
  decisions: true,
  documents: true,
  portfolios: true,
  retirement: true,
  'demo-guide': true,
  data: true,
  configuration: true,
  roles: true,
  guidelines: true,
  measures: true,
  admin: true,
  teams: true,
  team: true,
  decision: true,
  'intake-request': true,
  review: true,
  'change-request': true,
  'deployment-record': true,
  'document-link': true,
  claim: true,
  'gap-check-run': true,
  place: true,
  portfolio: true,
  'configuration-draft': true,
  'data-domain': true,
  'data-location': true,
  'data-mapping': true,
  'data-use': true,
  record: true,
  'not-found': true,
};

// The director's records get new IDs in every store; the check reads them, and these stand in for them here.
const IDS: Record<string, string | null> = {
  ...DEMO_IDS,
  claim: '00000000-0000-4000-8000-0000000000c1',
  gapCheckRun: '00000000-0000-4000-8000-0000000000c2',
  configurationDraft: '00000000-0000-4000-8000-0000000000c3',
};

const routeOf = (path: string) => {
  const url = new URL(path, 'http://127.0.0.1/app/');
  return parseAppPath(url.pathname, url.search, '/app/');
};

test('the accessibility check opens every page kind of the web app', () => {
  const seen = new Set<string>();
  for (const page of appPages(IDS)) {
    assert.ok(page.path !== null, `${page.name} has an address`);
    const route = routeOf(page.path ?? '');
    seen.add(route.page);
    if (page.name !== 'missing') assert.notEqual(route.page, 'not-found', `${page.name} (${page.path}) is a page of the routes`);
  }
  assert.deepEqual([...seen].sort(), Object.keys(EVERY_PAGE).sort());
});

test('each checked page has a name of its own, and a record the check could not read leaves its page without an address', () => {
  const names = appPages(IDS).map(p => p.name);
  assert.equal(new Set(names).size, names.length);
  const withoutClaim = appPages({ ...IDS, claim: null });
  assert.equal(withoutClaim.find(p => p.name === 'claim')?.path, null);
  assert.equal(withoutClaim.find(p => p.name === 'gap-check-run')?.path, `records/gap-check-run/${IDS.gapCheckRun}`);
});
