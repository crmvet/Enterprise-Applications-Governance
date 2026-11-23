// config.json checks: what the page accepts, and what it refuses before any sign-in or API call.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { loadConfig, validateConfig, type PageLocation } from './config';

const deployed: PageLocation = { origin: 'https://product.example.org', hostname: 'product.example.org' };
const local: PageLocation = { origin: 'http://127.0.0.1:4319', hostname: '127.0.0.1' };

const entra = {
  schemaVersion: 1,
  apiBase: '/api',
  auth: {
    mode: 'entra',
    authority: 'https://login.microsoftonline.com/00000000-0000-4000-8000-0000000000aa',
    clientId: '00000000-0000-4000-8000-0000000000ad',
    apiScope: 'api://00000000-0000-4000-8000-0000000000ab/api.access',
  },
};

const development = {
  schemaVersion: 1,
  apiBase: '/api',
  auth: { mode: 'development' },
  development: { personas: [{ name: 'nadia-web', label: 'Nadia Ferreira, business owner' }] },
};

const problemsOf = (raw: unknown, page: PageLocation) => {
  const r = validateConfig(raw, page);
  return r.ok ? [] : r.problems;
};

test('an Entra config with a same-origin API base is accepted and the base becomes absolute', () => {
  const r = validateConfig(entra, deployed);
  assert.equal(r.ok, true);
  if (r.ok) {
    assert.equal(r.config.apiBase, 'https://product.example.org/api');
    assert.equal(r.config.auth.mode, 'entra');
  }
});

test('a trailing slash on the API base is dropped', () => {
  const r = validateConfig({ ...entra, apiBase: '/api/' }, deployed);
  assert.equal(r.ok && r.config.apiBase, 'https://product.example.org/api');
});

test('a non-https authority is refused', () => {
  const p = problemsOf({ ...entra, auth: { ...entra.auth, authority: 'http://login.microsoftonline.com/x' } }, deployed);
  assert.ok(p.some(x => x.includes('authority')), p.join(' | '));
});

test('a cross-origin API base that is not https is refused', () => {
  const p = problemsOf({ ...entra, apiBase: 'http://api.example.org/api' }, deployed);
  assert.ok(p.some(x => x.includes('apiBase')), p.join(' | '));
});

test('a cross-origin https API base is accepted', () => {
  const r = validateConfig({ ...entra, apiBase: 'https://api.example.org/api' }, deployed);
  assert.equal(r.ok && r.config.apiBase, 'https://api.example.org/api');
});

test('an API base with credentials, a query or a fragment is refused', () => {
  const withCredentials = new URL('https://api.example.org/api');
  withCredentials.username = 'user';
  withCredentials.password = 'pw';
  for (const apiBase of [withCredentials.toString(), '/api?x=1', '/api#x']) {
    assert.ok(problemsOf({ ...entra, apiBase }, deployed).length > 0, apiBase);
  }
});

test('a client ID that is not a GUID, and a scope with spaces, are refused', () => {
  assert.ok(problemsOf({ ...entra, auth: { ...entra.auth, clientId: 'web-app' } }, deployed).some(x => x.includes('clientId')));
  assert.ok(problemsOf({ ...entra, auth: { ...entra.auth, apiScope: 'api.access openid' } }, deployed).some(x => x.includes('apiScope')));
});

test('the development sign-in is accepted on a loopback host', () => {
  const r = validateConfig(development, local);
  assert.equal(r.ok, true);
  if (r.ok) {
    assert.equal(r.config.auth.mode, 'development');
    assert.equal(r.config.apiBase, 'http://127.0.0.1:4319/api');
    assert.equal(r.config.development.personas[0].name, 'nadia-web');
  }
});

test('the development sign-in is refused on any other host', () => {
  const p = problemsOf(development, deployed);
  assert.ok(p.some(x => x.includes('loopback')), p.join(' | '));
  assert.ok(problemsOf(development, { origin: 'http://10.0.0.5:4319', hostname: '10.0.0.5' }).length > 0);
});

test('the development sign-in needs a persona, and persona names are checked', () => {
  assert.ok(problemsOf({ ...development, development: { personas: [] } }, local).length > 0);
  assert.ok(problemsOf({ ...development, development: { personas: [{ name: 'bad name<>', label: 'x' }] } }, local).length > 0);
});

test('an unknown schema version, an unknown mode and a non-object are refused', () => {
  assert.ok(problemsOf({ ...entra, schemaVersion: 2 }, deployed).length > 0);
  assert.ok(problemsOf({ ...entra, auth: { mode: 'anonymous' } }, deployed).length > 0);
  assert.ok(problemsOf([], deployed).length > 0);
  assert.ok(problemsOf(null, deployed).length > 0);
});

test('loadConfig reads without the cache and reports a missing file', async () => {
  let init: RequestInit | undefined;
  const ok = await loadConfig((async (_url: RequestInfo | URL, i?: RequestInit) => {
    init = i;
    return new Response(JSON.stringify(entra), { status: 200 });
  }) as typeof fetch, '/app/config.json', deployed);
  assert.equal(ok.ok, true);
  assert.equal(init?.cache, 'no-store');

  const missing = await loadConfig((async () => new Response('', { status: 404 })) as typeof fetch, '/app/config.json', deployed);
  assert.equal(missing.ok, false);
});
