// The development sign-in's own guards: loopback host names only, known personas only, the header only once chosen.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { isLoopbackHost } from '../app/config';
import { hasSignInResponse } from './bridge';
import { createDevAuth, DEV_IDENTITY_HEADER, type PersonaStore } from './devAuth';

const personas = [
  { name: 'nadia-web', label: 'Nadia Ferreira, business owner' },
  { name: 'mateo-web', label: 'Mateo Brandt, planner' },
];

function memoryStore(initial: string | null = null): PersonaStore & { value: string | null } {
  const store = {
    value: initial,
    get: () => store.value,
    set: (name: string | null) => { store.value = name; },
  };
  return store;
}

test('loopback host names', () => {
  for (const host of ['127.0.0.1', 'localhost', 'LOCALHOST', '127.10.0.1', '[::1]', '::1']) assert.equal(isLoopbackHost(host), true, host);
  for (const host of ['product.example.org', '10.0.0.5', '192.168.1.10', '127.0.0.1.example.org', 'localhost.example.org', '128.0.0.1', '127.0.0.256', '']) {
    assert.equal(isLoopbackHost(host), false, host);
  }
});

test('the development sign-in refuses a host name that is not loopback', () => {
  assert.throws(() => createDevAuth({ personas, hostname: 'product.example.org', store: memoryStore() }), /loopback/);
  assert.throws(() => createDevAuth({ personas, hostname: '10.0.0.5', store: memoryStore() }), /loopback/);
});

test('no header before a persona is chosen', async () => {
  const auth = createDevAuth({ personas, hostname: '127.0.0.1', store: memoryStore() });
  assert.equal(auth.persona(), null);
  let refused = false;
  try {
    await auth.headers();
  } catch {
    refused = true;
  }
  assert.equal(refused, true);
});

test('a chosen persona is sent in the development header and kept for the tab', async () => {
  const store = memoryStore();
  const auth = createDevAuth({ personas, hostname: 'localhost', store });
  let changes = 0;
  auth.subscribe(() => { changes += 1; });
  auth.choose('nadia-web');
  assert.deepEqual(await auth.headers(), { [DEV_IDENTITY_HEADER]: 'nadia-web' });
  assert.equal(store.value, 'nadia-web');
  assert.equal(changes, 1);
  assert.equal(auth.user()?.name, 'Nadia Ferreira, business owner');
});

test('only a persona from config.json can be chosen, and a stored unknown name is ignored', () => {
  const auth = createDevAuth({ personas, hostname: '127.0.0.1', store: memoryStore('owen-web') });
  assert.equal(auth.persona(), null);
  assert.throws(() => auth.choose('owen-web'), /No development persona/);
});

test('a 401 from the server drops the persona and says why', () => {
  const store = memoryStore('mateo-web');
  const auth = createDevAuth({ personas, hostname: '127.0.0.1', store });
  assert.equal(auth.persona()?.name, 'mateo-web');
  auth.onUnauthorized();
  assert.equal(auth.persona(), null);
  assert.equal(store.value, null);
  assert.match(auth.problem() ?? '', /Product:DevIdentity:Enabled=true/);
});

test('signing out clears the persona', async () => {
  const store = memoryStore('mateo-web');
  const auth = createDevAuth({ personas, hostname: '127.0.0.1', store });
  await auth.signOut();
  assert.equal(auth.persona(), null);
  assert.equal(store.value, null);
});

test('a sign-in response is recognized in the fragment or the query string only with state and code or error', () => {
  assert.equal(hasSignInResponse('#code=abc&state=xyz', ''), true);
  assert.equal(hasSignInResponse('', '?code=abc&state=xyz'), true);
  assert.equal(hasSignInResponse('#error=access_denied&state=xyz', ''), true);
  assert.equal(hasSignInResponse('#code=abc', ''), false);
  assert.equal(hasSignInResponse('', '?stage=build&tier=tier-1'), false);
  assert.equal(hasSignInResponse('', ''), false);
});
