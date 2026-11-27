// The wave 3 addresses: the Data page and its tabs, the data record pages (a data domain by its key), the Govern pages
// (Configuration, Roles and access, Guidelines, Measures, Admin) and the applications register's owner and registration
// filters.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appHref, hasRecordPage, parseAppPath, recordPath, recordRoute, routeTitle, type AppRoute } from './routes';

const BASE = '/app/';
const APP = '00000000-0000-4000-8000-000000000511';
const ID = '00000000-0000-4000-8000-000000000901';
const PERSON = '00000000-0000-4000-8000-000000000212';
const LOCATION = '00000000-0000-4000-8000-000000000a01';

test('the Data page opens on Domains, and each tab has an address of its own', () => {
  assert.deepEqual(parseAppPath('/app/data', '', BASE), { page: 'data' });
  assert.deepEqual(parseAppPath('/app/data/domains', '', BASE), { page: 'data' });
  assert.deepEqual(parseAppPath('/app/data/locations', '', BASE), { page: 'data', tab: 'locations' });
  assert.deepEqual(parseAppPath('/app/data/mapping', '', BASE), { page: 'data', tab: 'mapping' });
  assert.deepEqual(parseAppPath('/app/data/uses', '', BASE), { page: 'data', tab: 'uses' });
  assert.deepEqual(parseAppPath('/app/data/gaps', '', BASE), { page: 'data', tab: 'gaps' });
  assert.equal(appHref({ page: 'data' }, BASE), '/app/data');
  assert.equal(appHref({ page: 'data', tab: 'gaps' }, BASE), '/app/data/gaps');
  for (const path of ['/app/data/owners', '/app/data/uses/extra', `/app/data/${LOCATION}`, '/app/data/Uses']) {
    assert.deepEqual(parseAppPath(path, '', BASE), { page: 'not-found' }, path);
  }
});

test('the Data page keeps its shared filters in the address and drops the malformed ones', () => {
  assert.deepEqual(parseAppPath('/app/data/uses', `?app=${APP.toUpperCase()}&domain=payroll&mine=1`, BASE), {
    page: 'data',
    tab: 'uses',
    domainKey: 'payroll',
    applicationId: APP,
    mine: true,
  });
  assert.deepEqual(parseAppPath('/app/data/gaps', `?location=${LOCATION}&domain=Bad_Key&app=nope&mine=yes`, BASE), {
    page: 'data',
    tab: 'gaps',
    locationId: LOCATION,
  });
  assert.equal(
    appHref({ page: 'data', tab: 'uses', domainKey: 'payroll', applicationId: APP, mine: true }, BASE),
    `/app/data/uses?domain=payroll&app=${APP}&mine=1`,
  );
});

test('a data record link opens its record page; a data domain is named by its key', () => {
  assert.deepEqual(parseAppPath('/app/records/data-domain/payroll', '', BASE), { page: 'data-domain', key: 'payroll' });
  assert.deepEqual(recordRoute('data-domain', 'people-bank-details'), { page: 'data-domain', key: 'people-bank-details' });
  assert.equal(appHref({ page: 'data-domain', key: 'payroll' }, BASE), '/app/records/data-domain/payroll');
  const byId = [['data-location', 'data-location'], ['data-mapping', 'data-mapping'], ['data-use', 'data-use'], ['configuration-draft', 'configuration-draft']] as const;
  for (const [entity, page] of byId) {
    assert.deepEqual(parseAppPath(`/app/records/${entity}/${ID.toUpperCase()}`, '', BASE), { page, id: ID }, entity);
    assert.deepEqual(recordRoute(entity, ID), { page, id: ID }, entity);
    assert.equal(appHref({ page, id: ID }, BASE), `/app/records/${entity}/${ID}`);
    assert.equal(hasRecordPage(entity), true, entity);
  }
  assert.equal(hasRecordPage('data-domain'), true);
  assert.equal(hasRecordPage('technical-component'), false);
  assert.equal(recordPath('https://product.example.org/app/records/data-domain/payroll', BASE), '/app/records/data-domain/payroll');
  assert.equal(recordPath(`https://other.example.org/app/records/data-use/${ID}`, BASE), `/app/records/data-use/${ID}`);
  for (const path of [
    '/app/records/data-domain/Payroll',
    `/app/records/data-domain/${'a'.repeat(65)}`,
    '/app/records/data-domain/payroll/map',
    '/app/records/data-use/payroll',
    `/app/records/data-location/${LOCATION}/edit`,
    `/app/records/configuration-draft/${ID}/evidence`,
  ]) {
    assert.deepEqual(parseAppPath(path, '', BASE), { page: 'not-found' }, path);
  }
  assert.deepEqual(recordRoute('data-domain', ID), { page: 'not-found' });
  assert.deepEqual(recordRoute('data-use', 'payroll'), { page: 'not-found' });
});

test('the Govern pages read their view, role or guideline, and drop values they do not know', () => {
  assert.deepEqual(parseAppPath('/app/configuration', '', BASE), { page: 'configuration' });
  assert.deepEqual(parseAppPath('/app/configuration', '?view=rules', BASE), { page: 'configuration' });
  assert.deepEqual(parseAppPath('/app/configuration', '?view=drafts', BASE), { page: 'configuration', view: 'drafts' });
  assert.deepEqual(parseAppPath('/app/configuration', '?view=everything', BASE), { page: 'configuration' });
  assert.deepEqual(parseAppPath('/app/roles', '?role=auditor', BASE), { page: 'roles', roleKey: 'auditor' });
  assert.deepEqual(parseAppPath('/app/roles', '?role=Not_A_Key', BASE), { page: 'roles' });
  assert.deepEqual(parseAppPath('/app/guidelines', '?g=g17', BASE), { page: 'guidelines', guideline: 'G17' });
  assert.deepEqual(parseAppPath('/app/guidelines', '?g=G0', BASE), { page: 'guidelines' });
  assert.deepEqual(parseAppPath('/app/measures', '', BASE), { page: 'measures' });
  assert.deepEqual(parseAppPath('/app/admin', '', BASE), { page: 'admin' });
  for (const path of ['/app/configuration/drafts', '/app/roles/auditor', '/app/admin/outbox', `/app/configuration/${ID}`]) {
    assert.deepEqual(parseAppPath(path, '', BASE), { page: 'not-found' }, path);
  }
});

test('the applications register keeps the owner and registration filters in the address', () => {
  const route = parseAppPath('/app/applications', `?owner=${PERSON.toUpperCase()}&registration=draft&registration=bogus&registration=draft`, BASE);
  assert.deepEqual(route, { page: 'applications', filter: { ownerObjectId: PERSON, registrationMeanings: ['draft'] } });
  assert.equal(appHref(route, BASE), `/app/applications?owner=${PERSON}&registration=draft`);
  assert.deepEqual(parseAppPath('/app/applications', '?owner=nobody', BASE), { page: 'applications', filter: {} });
});

test('the wave 3 hrefs round-trip through the parser and every page has a title', () => {
  const routes: AppRoute[] = [
    { page: 'data' },
    { page: 'data', domainKey: 'people', mine: true },
    { page: 'data', tab: 'locations' },
    { page: 'data', tab: 'mapping', domainKey: 'payroll' },
    { page: 'data', tab: 'uses', applicationId: APP },
    { page: 'data', tab: 'gaps', locationId: LOCATION, mine: true },
    { page: 'data-domain', key: 'payroll' },
    { page: 'data-location', id: LOCATION },
    { page: 'data-mapping', id: ID },
    { page: 'data-use', id: ID },
    { page: 'configuration' },
    { page: 'configuration', view: 'presets' },
    { page: 'configuration', view: 'versions' },
    { page: 'configuration', view: 'drafts' },
    { page: 'configuration-draft', id: ID },
    { page: 'roles' },
    { page: 'roles', roleKey: 'data-governance-lead' },
    { page: 'guidelines' },
    { page: 'guidelines', guideline: 'G17' },
    { page: 'measures' },
    { page: 'admin' },
    { page: 'applications', filter: { ownerObjectId: PERSON, registrationMeanings: ['draft', 'registered'], tierKeys: ['tier-3'] } },
  ];
  for (const route of routes) {
    const href = appHref(route, BASE);
    const url = new URL(href, 'https://product.example.org');
    assert.deepEqual(parseAppPath(url.pathname, url.search, BASE), route, href);
    assert.notEqual(routeTitle(route), 'Page not found', href);
  }
  assert.equal(routeTitle({ page: 'data', tab: 'uses' }), 'Data uses');
  assert.equal(routeTitle({ page: 'data' }), 'Data domains');
  assert.equal(routeTitle({ page: 'configuration', view: 'drafts' }), 'Configuration: Drafts');
});
