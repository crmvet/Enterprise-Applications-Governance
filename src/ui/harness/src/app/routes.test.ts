// The web app's addresses, and the record links the tools return (<base>/app/records/<entity>/<id>).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appHref, internalTarget, normalizeBase, parseAppPath, recordPath, recordRoute, routeTitle, type AppRoute } from './routes';

const BASE = '/app/';
const APP = '00000000-0000-4000-8000-000000000511';
const ID = '00000000-0000-4000-8000-000000000901';
const PERSON = '00000000-0000-4000-8000-000000000212';
const PORTFOLIO = '00000000-0000-4000-8000-000000000501';

test('the base path is normalized', () => {
  assert.equal(normalizeBase('/app/'), '/app/');
  assert.equal(normalizeBase('app'), '/app/');
  assert.equal(normalizeBase('/'), '/');
});

test('the home page is My work, with or without the trailing slash, and the overview has its own address', () => {
  assert.deepEqual(parseAppPath('/app/', '', BASE), { page: 'work' });
  assert.deepEqual(parseAppPath('/app', '', BASE), { page: 'work' });
  assert.deepEqual(parseAppPath('/app/work', '', BASE), { page: 'work' });
  assert.deepEqual(parseAppPath('/app/overview', '', BASE), { page: 'overview' });
  assert.deepEqual(parseAppPath('/app/timeline', '', BASE), { page: 'timeline' });
  assert.equal(appHref({ page: 'work' }, BASE), '/app/work');
  assert.equal(appHref({ page: 'overview' }, BASE), '/app/overview');
});

test('a business application record link opens Application 360', () => {
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP}`, '', BASE), { page: 'app', id: APP });
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP.toUpperCase()}`, '', BASE), { page: 'app', id: APP });
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP}/map`, '', BASE), { page: 'map', id: APP });
});

test('each record entity with a page of its own opens that page', () => {
  const pages: [string, AppRoute['page']][] = [
    ['decision', 'decision'],
    ['intake-request', 'intake-request'],
    ['review', 'review'],
    ['change-request', 'change-request'],
    ['deployment-record', 'deployment-record'],
    ['document-link', 'document-link'],
    ['claim', 'claim'],
    ['gap-check-run', 'gap-check-run'],
    ['deployment-target', 'place'],
    ['portfolio', 'portfolio'],
  ];
  for (const [entity, page] of pages) {
    assert.deepEqual(parseAppPath(`/app/records/${entity}/${ID}`, '', BASE), { page, id: ID }, entity);
    assert.deepEqual(parseAppPath(`/app/records/${entity}/${ID.toUpperCase()}`, '', BASE), { page, id: ID }, entity);
    assert.deepEqual(recordRoute(entity, ID), { page, id: ID }, entity);
  }
  assert.deepEqual(parseAppPath(`/app/records/intake-request/${ID}/edit`, '', BASE), { page: 'intake-edit', id: ID });
});

test('a record link of another entity opens the plain "not shown here" panel', () => {
  assert.deepEqual(parseAppPath(`/app/records/technical-component/${ID}`, '', BASE), { page: 'record', entity: 'technical-component', id: ID });
  assert.deepEqual(parseAppPath(`/app/records/owner-assignment/${ID}`, '', BASE), { page: 'record', entity: 'owner-assignment', id: ID });
  assert.deepEqual(recordRoute('technical-component', ID), { page: 'record', entity: 'technical-component', id: ID });
  assert.deepEqual(recordRoute('Bad_Entity', ID), { page: 'not-found' });
  assert.deepEqual(recordRoute('decision', 'not-a-guid'), { page: 'not-found' });
});

test('malformed record links and unknown pages are not found', () => {
  for (const path of [
    '/app/records/business-application/not-a-guid',
    '/app/records/business-application',
    '/app/records/Business_Application/00000000-0000-4000-8000-000000000511',
    `/app/records/decision/${APP}/map`,
    `/app/records/decision/${APP}/edit`,
    `/app/records/business-application/${APP}/history`,
    `/app/records/business-application/${APP}/edit`,
    '/app/records/%E0%A4%A/x',
    '/app/anything',
    '/app/intake/old',
    '/app/changes/new/extra',
    '/app/work/extra',
    '/app/people',
    '/app/people/not-a-guid',
    `/app/people/${PERSON}/apps`,
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

test('the work lists read their view, filters and IDs, and drop values they do not know', () => {
  assert.deepEqual(parseAppPath('/app/approvals', '?view=asked-by-me', BASE), { page: 'approvals', view: 'asked-by-me' });
  assert.deepEqual(parseAppPath('/app/approvals', `?view=everything&app=${APP.toUpperCase()}`, BASE), { page: 'approvals', applicationId: APP });
  assert.deepEqual(parseAppPath('/app/approvals', '', BASE), { page: 'approvals' });
  assert.deepEqual(parseAppPath('/app/intake', '?view=triage&q=%20crew%20', BASE), { page: 'intake', view: 'triage', text: 'crew' });
  assert.deepEqual(parseAppPath('/app/intake', '?q=%20%20', BASE), { page: 'intake' });
  assert.deepEqual(parseAppPath('/app/intake/new', '', BASE), { page: 'intake-new' });
  assert.deepEqual(parseAppPath('/app/owners', `?view=leavers&portfolio=${PORTFOLIO}&app=bad`, BASE), { page: 'owners', view: 'leavers', portfolioId: PORTFOLIO });
  assert.deepEqual(parseAppPath(`/app/people/${PERSON.toUpperCase()}`, '', BASE), { page: 'person', objectId: PERSON });
  assert.deepEqual(parseAppPath('/app/reviews', '?state=overdue&type=architecture&type=Bad_Key&type=architecture', BASE), {
    page: 'reviews',
    state: 'overdue',
    typeKeys: ['architecture'],
  });
  assert.deepEqual(parseAppPath('/app/reviews', '?state=late', BASE), { page: 'reviews' });
  assert.deepEqual(parseAppPath('/app/changes', '?view=schedule', BASE), { page: 'changes', view: 'schedule' });
  assert.deepEqual(parseAppPath('/app/changes/new', `?app=${APP}`, BASE), { page: 'change-new', applicationId: APP });
  assert.deepEqual(parseAppPath('/app/changes/new', '?app=nope', BASE), { page: 'change-new' });
});

test('hrefs round-trip through the parser', () => {
  const routes: AppRoute[] = [
    { page: 'work' },
    { page: 'overview' },
    { page: 'timeline' },
    { page: 'app', id: APP },
    { page: 'map', id: APP },
    { page: 'applications', filter: { tierKeys: ['tier-1', 'tier-2'] } },
    { page: 'approvals' },
    { page: 'approvals', view: 'open', applicationId: APP },
    { page: 'decision', id: ID },
    { page: 'intake' },
    { page: 'intake', view: 'demand', text: 'crew overtime' },
    { page: 'intake-new' },
    { page: 'intake-request', id: ID },
    { page: 'intake-edit', id: ID },
    { page: 'owners', view: 'gaps', applicationId: APP, portfolioId: PORTFOLIO },
    { page: 'person', objectId: PERSON },
    { page: 'reviews', state: 'due', typeKeys: ['architecture', 'security'], applicationId: APP, portfolioId: PORTFOLIO },
    { page: 'review', id: ID },
    { page: 'changes', view: 'list', applicationId: APP },
    { page: 'change-new' },
    { page: 'change-new', applicationId: APP },
    { page: 'change-request', id: ID },
    { page: 'deployment-record', id: ID },
    { page: 'document-link', id: ID },
    { page: 'claim', id: ID },
    { page: 'record', entity: 'technical-component', id: ID },
  ];
  for (const route of routes) {
    const href = appHref(route, BASE);
    const url = new URL(href, 'https://product.example.org');
    assert.deepEqual(parseAppPath(url.pathname, url.search, BASE), route, href);
    assert.notEqual(routeTitle(route), 'Page not found', href);
  }
  assert.equal(appHref({ page: 'app', id: APP }, BASE), `/app/records/business-application/${APP}`);
  assert.equal(appHref({ page: 'decision', id: ID }, BASE), `/app/records/decision/${ID}`);
  assert.equal(appHref({ page: 'intake-edit', id: ID }, BASE), `/app/records/intake-request/${ID}/edit`);
  assert.equal(appHref({ page: 'change-new', applicationId: APP }, BASE), `/app/changes/new?app=${APP}`);
  assert.equal(appHref({ page: 'approvals' }, BASE), '/app/approvals');
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

test('a record link from a tool becomes an in-app address, whatever host the product wrote it with', () => {
  assert.equal(recordPath(`https://product.example.org/app/records/decision/${ID.toUpperCase()}`, BASE), `/app/records/decision/${ID}`);
  assert.equal(recordPath(`https://other-name.example.org/app/records/business-application/${APP}`, BASE), `/app/records/business-application/${APP}`);
  assert.equal(recordPath(`https://product.example.org/app/records/configuration-draft/${ID}`, BASE), `/app/records/configuration-draft/${ID}`);
  assert.equal(recordPath(`https://product.example.org/app/records/technical-component/${ID}`, BASE), `/app/records/technical-component/${ID}`);
  assert.equal(recordPath(`https://product.example.org/app/records/decision/not-a-guid`, BASE), null);
  assert.equal(recordPath('https://product.example.org/app/approvals', BASE), null);
  assert.equal(recordPath(`javascript:alert(1)//app/records/decision/${ID}`, BASE), null);
  assert.equal(recordPath(`/app/records/decision/${ID}`, BASE), null);
  assert.equal(recordPath('', BASE), null);
  assert.equal(recordPath(null, BASE), null);
});

test('the applications list keeps its search text and portfolios in the address', () => {
  const route = parseAppPath('/app/applications', `?q=%20inspection%20&portfolio=${PORTFOLIO}&portfolio=bad&tier=tier-1`, BASE);
  assert.deepEqual(route, {
    page: 'applications',
    filter: { tierKeys: ['tier-1'], text: 'inspection', portfolioIds: [PORTFOLIO] },
  });
  const href = appHref(route, BASE);
  assert.equal(href, `/app/applications?tier=tier-1&portfolio=${PORTFOLIO}&q=inspection`);
  assert.deepEqual(parseAppPath('/app/applications', href.split('?')[1] ? `?${href.split('?')[1]}` : '', BASE), route);
});

// ---------- the wave 2 pages ----------

const TARGET = '00000000-0000-4000-8000-000000000611';
const CHANGE = '00000000-0000-4000-8000-000000000711';

test('the wave 2 list pages read their views and filters, and drop values they do not know', () => {
  assert.deepEqual(parseAppPath('/app/deployments', `?view=flagged&flag=other-path&flag=bogus&flag=other-path&app=${APP}&change=${CHANGE.toUpperCase()}&target=nope`, BASE), {
    page: 'deployments',
    view: 'flagged',
    flagKeys: ['other-path'],
    applicationId: APP,
    changeRequestId: CHANGE,
  });
  assert.deepEqual(parseAppPath('/app/deployments/new', `?app=${APP}&change=bad`, BASE), { page: 'deployment-new', applicationId: APP });
  assert.deepEqual(parseAppPath('/app/places', '?view=pipelines&stage=production&stage=Bad_Key', BASE), { page: 'places', view: 'pipelines', stageKeys: ['production'] });
  assert.deepEqual(parseAppPath('/app/gap-check', '?view=vanished', BASE), { page: 'gap-check', view: 'vanished' });
  assert.deepEqual(parseAppPath('/app/gap-check', '?view=everything', BASE), { page: 'gap-check' });
  assert.deepEqual(parseAppPath('/app/ai-activity', `?view=refused&app=${APP}&mine=1`, BASE), { page: 'ai-activity', view: 'refused', applicationId: APP, mine: true });
  assert.deepEqual(parseAppPath('/app/ai-activity', '?mine=yes', BASE), { page: 'ai-activity' });
  assert.deepEqual(parseAppPath('/app/agents', '?view=autonomy', BASE), { page: 'agents', view: 'autonomy' });
  assert.deepEqual(parseAppPath('/app/audit', `?record=${ID}&person=${PERSON}&actor=ai-client-for-person&actor=robot`, BASE), {
    page: 'audit',
    recordId: ID,
    principalObjectId: PERSON,
    actorKinds: ['ai-client-for-person'],
  });
  assert.deepEqual(parseAppPath('/app/decisions', `?view=exceptions&type=exception&status=accepted&status=maybe&portfolio=${PORTFOLIO}`, BASE), {
    page: 'decisions',
    view: 'exceptions',
    typeKeys: ['exception'],
    statusMeanings: ['accepted'],
    portfolioId: PORTFOLIO,
  });
  assert.deepEqual(parseAppPath('/app/documents', `?state=missing&type=runbook&app=${APP}&mine=1`, BASE), {
    page: 'documents',
    state: 'missing',
    typeKeys: ['runbook'],
    applicationId: APP,
    mine: true,
  });
  assert.deepEqual(parseAppPath('/app/demand', `?portfolio=${PORTFOLIO}`, BASE), { page: 'demand', portfolioId: PORTFOLIO });
  assert.deepEqual(parseAppPath('/app/portfolios', '', BASE), { page: 'portfolios' });
  assert.deepEqual(parseAppPath('/app/retirement', '', BASE), { page: 'retirement' });
  assert.deepEqual(parseAppPath('/app/demo-guide', '', BASE), { page: 'demo-guide' });
  assert.deepEqual(parseAppPath('/app/search', '?q=%20DEC-0123%20', BASE), { page: 'search', text: 'DEC-0123' });
  assert.deepEqual(parseAppPath('/app/search', '', BASE), { page: 'search' });
});

test('the wave 2 pages of one record or one subject read their ID', () => {
  assert.deepEqual(parseAppPath(`/app/places/${TARGET.toUpperCase()}`, '', BASE), { page: 'place', id: TARGET });
  assert.deepEqual(parseAppPath(`/app/portfolios/${PORTFOLIO}`, '', BASE), { page: 'portfolio', id: PORTFOLIO });
  assert.deepEqual(parseAppPath(`/app/retirement/${APP}`, '', BASE), { page: 'retirement', applicationId: APP });
  assert.deepEqual(parseAppPath(`/app/evidence/business-application/${APP}`, '', BASE), { page: 'evidence', subjectKind: 'business-application', id: APP });
  assert.deepEqual(parseAppPath(`/app/evidence/decision/${ID.toUpperCase()}`, '', BASE), { page: 'evidence', subjectKind: 'decision', id: ID });
  assert.deepEqual(parseAppPath(`/app/records/gap-check-run/${ID}`, '', BASE), { page: 'gap-check-run', id: ID });
  for (const path of [
    '/app/places/not-a-guid',
    `/app/places/${TARGET}/extra`,
    '/app/portfolios/not-a-guid',
    '/app/retirement/not-a-guid',
    `/app/evidence/review/${ID}`,
    '/app/evidence/decision/not-a-guid',
    '/app/evidence/decision',
    '/app/deployments/old',
    '/app/deployments/new/extra',
    `/app/records/review/${ID}/evidence`,
    `/app/records/decision/${ID}/retire`,
  ]) {
    assert.deepEqual(parseAppPath(path, '', BASE), { page: 'not-found' }, path);
  }
});

test('the other addresses of a page open the same page, and links use the main one', () => {
  assert.deepEqual(parseAppPath('/app/ai', '?view=drafts', BASE), { page: 'ai-activity', view: 'drafts' });
  assert.deepEqual(parseAppPath('/app/demo', '', BASE), { page: 'demo-guide' });
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP}/retire`, '', BASE), { page: 'retirement', applicationId: APP });
  assert.deepEqual(parseAppPath(`/app/records/business-application/${APP}/evidence`, '', BASE), { page: 'evidence', subjectKind: 'business-application', id: APP });
  assert.deepEqual(parseAppPath(`/app/records/decision/${ID}/evidence`, '', BASE), { page: 'evidence', subjectKind: 'decision', id: ID });
  assert.equal(appHref({ page: 'ai-activity', view: 'drafts' }, BASE), '/app/ai-activity?view=drafts');
  assert.equal(appHref({ page: 'demo-guide' }, BASE), '/app/demo-guide');
});

test('a deployment target or portfolio record link opens its own address', () => {
  assert.deepEqual(recordRoute('deployment-target', TARGET), { page: 'place', id: TARGET });
  assert.deepEqual(recordRoute('portfolio', PORTFOLIO), { page: 'portfolio', id: PORTFOLIO });
  assert.equal(recordPath(`https://product.example.org/app/records/deployment-target/${TARGET}`, BASE), `/app/places/${TARGET}`);
  assert.equal(recordPath(`https://product.example.org/app/records/portfolio/${PORTFOLIO.toUpperCase()}`, BASE), `/app/portfolios/${PORTFOLIO}`);
  assert.equal(recordPath(`https://product.example.org/app/records/gap-check-run/${ID}`, BASE), `/app/records/gap-check-run/${ID}`);
});

test('the wave 2 hrefs round-trip through the parser and every page has a title', () => {
  const routes: AppRoute[] = [
    { page: 'search' },
    { page: 'search', text: 'DEC-0123' },
    { page: 'demand' },
    { page: 'demand', portfolioId: PORTFOLIO },
    { page: 'deployments' },
    { page: 'deployments', view: 'flagged', flagKeys: ['outside-window', 'ai-only-code-approval'], applicationId: APP, changeRequestId: CHANGE, targetId: TARGET },
    { page: 'deployment-new' },
    { page: 'deployment-new', applicationId: APP, changeRequestId: CHANGE },
    { page: 'places' },
    { page: 'places', view: 'targets', stageKeys: ['test', 'production'] },
    { page: 'place', id: TARGET },
    { page: 'gap-check' },
    { page: 'gap-check', view: 'runs' },
    { page: 'gap-check-run', id: ID },
    { page: 'ai-activity' },
    { page: 'ai-activity', view: 'code-approvals', applicationId: APP, mine: true },
    { page: 'agents' },
    { page: 'agents', view: 'flagged' },
    { page: 'audit' },
    { page: 'audit', recordId: ID, principalObjectId: PERSON, applicationId: APP, actorKinds: ['person', 'engine'] },
    { page: 'evidence', subjectKind: 'business-application', id: APP },
    { page: 'evidence', subjectKind: 'decision', id: ID },
    { page: 'decisions' },
    { page: 'decisions', view: 'exceptions', typeKeys: ['exception'], statusMeanings: ['proposed', 'accepted'], applicationId: APP, portfolioId: PORTFOLIO },
    { page: 'documents' },
    { page: 'documents', state: 'stale', typeKeys: ['runbook'], applicationId: APP, portfolioId: PORTFOLIO, mine: true },
    { page: 'portfolios' },
    { page: 'portfolio', id: PORTFOLIO },
    { page: 'retirement' },
    { page: 'retirement', applicationId: APP },
    { page: 'demo-guide' },
  ];
  for (const route of routes) {
    const href = appHref(route, BASE);
    const url = new URL(href, 'https://product.example.org');
    assert.deepEqual(parseAppPath(url.pathname, url.search, BASE), route, href);
    assert.notEqual(routeTitle(route), 'Page not found', href);
  }
  assert.equal(appHref({ page: 'place', id: TARGET }, BASE), `/app/places/${TARGET}`);
  assert.equal(appHref({ page: 'portfolio', id: PORTFOLIO }, BASE), `/app/portfolios/${PORTFOLIO}`);
  assert.equal(appHref({ page: 'gap-check-run', id: ID }, BASE), `/app/records/gap-check-run/${ID}`);
  assert.equal(appHref({ page: 'evidence', subjectKind: 'decision', id: ID }, BASE), `/app/evidence/decision/${ID}`);
  assert.equal(appHref({ page: 'retirement', applicationId: APP }, BASE), `/app/retirement/${APP}`);
  assert.equal(appHref({ page: 'search', text: 'DEC-0123' }, BASE), '/app/search?q=DEC-0123');
});
