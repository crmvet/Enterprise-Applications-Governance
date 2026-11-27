// The demo persona switch (D-39), the page's side. In a demo install only, the signed-in operator may act as any of the
// install's fictional personas: the page sends the persona's name in X-Product-Demo-Persona on every call to the API,
// and for that request the server runs as the persona. The Tool Policy, the engine's checks and the row-level security
// all see the persona, and every write is stamped with the persona and the Client App label "Web app, demo operator".
//
// The page offers the switch only when config.json lists demo personas, which the host does only in the Demo
// environment. The server keeps its own guards: outside Demo it ignores the header, and a host that names demo personas
// anywhere else refuses to start. A name that is not in the install's list is never sent, so the operator never acts
// as somebody else by mistake. With no persona chosen the operator acts as themselves.
import type { DevAuth, PersonaStore } from '../auth/devAuth';
import { listeners } from '../auth/types';
import type { DevPersona } from './config';

export const DEMO_PERSONA_HEADER = 'X-Product-Demo-Persona';
export const DEMO_PERSONA_STORE_KEY = 'product.demoPersona';

export type DemoPersona = DevPersona;

export interface DemoPersonaSwitch {
  readonly personas: readonly DemoPersona[];
  // True when the install lists demo personas: the page shows the switch.
  readonly available: boolean;
  current(): DemoPersona | null;
  // A name of the install's list, or null to act as yourself.
  choose(name: string | null): void;
  // The header for a call to the API base: the persona's name, or nothing.
  headers(): Record<string, string>;
  subscribe(callback: () => void): () => void;
}

export function createDemoPersonaSwitch(personas: readonly DemoPersona[], store: PersonaStore): DemoPersonaSwitch {
  const list = personas.slice();
  const events = listeners();
  const find = (name: string | null) => (name === null ? null : list.find(p => p.name === name) ?? null);
  // A stored name the install no longer lists is dropped.
  let current = find(store.get());
  if (!current && store.get() !== null) store.set(null);

  return {
    personas: list,
    available: list.length > 0,
    current: () => current,
    choose(name) {
      const next = find(name);
      if (name !== null && !next) throw new Error(`This demo install has no persona named ${name}.`);
      if ((next?.name ?? null) === (current?.name ?? null)) return;
      current = next;
      store.set(next ? next.name : null);
      events.notify();
    },
    headers: (): Record<string, string> => (current ? { [DEMO_PERSONA_HEADER]: current.name } : {}),
    subscribe: events.subscribe,
  };
}

// The development sign-in as a persona switch (wave 4), for the Demo guide on a local run: choosing a persona signs in
// as that development persona, which the local server serves as that person. choose(null) keeps the persona in use,
// because the development sign-in always acts as someone. headers() is empty: the development sign-in already sends
// its own identity header on every call (devAuth.ts), so nothing is sent twice.
export function devPersonaSwitch(auth: Pick<DevAuth, 'personas' | 'persona' | 'choose' | 'subscribe'>): DemoPersonaSwitch {
  return {
    personas: auth.personas,
    available: auth.personas.length > 0,
    current: () => auth.persona(),
    choose(name) {
      if (name === null) return;
      if ((auth.persona()?.name ?? null) === name) return;
      auth.choose(name);
    },
    headers: (): Record<string, string> => ({}),
    subscribe: callback => auth.subscribe(callback),
  };
}

// "Nadia Ferreira, application owner" reads as the name "Nadia Ferreira" and the role "application owner".
export function personaParts(label: string): { name: string; role: string | null } {
  const at = label.indexOf(',');
  if (at < 0) return { name: label.trim(), role: null };
  const role = label.slice(at + 1).trim();
  return { name: label.slice(0, at).trim(), role: role || null };
}
