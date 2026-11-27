// The demo persona switch (D-39): what the page sends, and what it refuses to send.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { PersonaStore } from '../auth/devAuth';
import { createDevAuth } from '../auth/devAuth';
import { DEMO_PERSONA_HEADER, createDemoPersonaSwitch, devPersonaSwitch, personaParts } from './demoPersona';

const personas = [
  { name: 'lena', label: 'Lena Okoro, requester' },
  { name: 'nadia', label: 'Nadia Ferreira, application owner' },
];

function memoryStore(initial: string | null = null): PersonaStore & { value: string | null } {
  return {
    value: initial,
    get() { return this.value; },
    set(name) { this.value = name; },
  };
}

test('with no persona chosen the operator acts as themselves and no header is sent', () => {
  const s = createDemoPersonaSwitch(personas, memoryStore());
  assert.equal(s.available, true);
  assert.equal(s.current(), null);
  assert.deepEqual(s.headers(), {});
});

test('choosing a persona sends its name in the header, remembers it for the tab, and notifies', () => {
  const store = memoryStore();
  const s = createDemoPersonaSwitch(personas, store);
  let notified = 0;
  s.subscribe(() => { notified += 1; });
  s.choose('nadia');
  assert.deepEqual(s.headers(), { [DEMO_PERSONA_HEADER]: 'nadia' });
  assert.equal(store.value, 'nadia');
  assert.equal(notified, 1);
  s.choose('nadia');
  assert.equal(notified, 1, 'choosing the same persona again changes nothing');
  s.choose(null);
  assert.deepEqual(s.headers(), {});
  assert.equal(store.value, null);
  assert.equal(notified, 2);
});

test('a remembered persona comes back, and one the install no longer lists is dropped', () => {
  assert.equal(createDemoPersonaSwitch(personas, memoryStore('lena')).current()?.name, 'lena');
  const store = memoryStore('mallory');
  const s = createDemoPersonaSwitch(personas, store);
  assert.equal(s.current(), null);
  assert.equal(store.value, null);
  assert.deepEqual(s.headers(), {});
});

test('a name outside the install list is refused and nothing changes', () => {
  const s = createDemoPersonaSwitch(personas, memoryStore('lena'));
  assert.throws(() => s.choose('mallory'), /no persona named mallory/);
  assert.equal(s.current()?.name, 'lena');
});

test('outside a demo install the list is empty: no switch and no header, whatever was stored', () => {
  const s = createDemoPersonaSwitch([], memoryStore('nadia'));
  assert.equal(s.available, false);
  assert.equal(s.current(), null);
  assert.deepEqual(s.headers(), {});
  assert.throws(() => s.choose('nadia'));
});

test('a persona label reads as a name and a role', () => {
  assert.deepEqual(personaParts('Nadia Ferreira, application owner'), { name: 'Nadia Ferreira', role: 'application owner' });
  assert.deepEqual(personaParts('A configuration admin'), { name: 'A configuration admin', role: null });
  assert.deepEqual(personaParts('Theo Marsh,'), { name: 'Theo Marsh', role: null });
});

test('on a local run the Demo guide switches the development identity, and sends no header of its own', () => {
  const auth = createDevAuth({ personas: [{ name: 'nadia-web', label: 'Nadia Ferreira, application owner' }, { name: 'owen-web', label: 'Owen Mbeki, platform admin' }], hostname: '127.0.0.1', store: memoryStore('nadia-web') });
  const s = devPersonaSwitch(auth);
  assert.equal(s.available, true);
  assert.equal(s.current()?.name, 'nadia-web');
  let notified = 0;
  const stop = s.subscribe(() => { notified += 1; });
  s.choose('owen-web');
  assert.equal(auth.persona()?.name, 'owen-web');
  assert.equal(s.current()?.name, 'owen-web');
  assert.equal(notified, 1);
  // The same persona again, or none, changes nothing: the development sign-in always acts as someone.
  s.choose('owen-web');
  s.choose(null);
  assert.equal(notified, 1);
  assert.equal(auth.persona()?.name, 'owen-web');
  assert.deepEqual(s.headers(), {});
  assert.throws(() => s.choose('nobody'));
  stop();
});
