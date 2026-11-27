// The development sign-in: the page names a fictional persona in the X-Product-Dev-Identity header, and the local
// server signs the request in as that persona with the claims an Entra token would carry.
//
// Two guards on this side: config.json must say auth.mode "development", and the page must run on a loopback host
// name. The server keeps its own guards: the Development environment, Product:DevIdentity:Enabled (off in every
// committed file) and loopback requests with no forwarding header. The Azure deployment has no development identity.
import { isLoopbackHost, type DevPersona } from '../app/config';
import { listeners, SignInRequired, type AuthSession, type SignedInUser } from './types';

export const DEV_IDENTITY_HEADER = 'X-Product-Dev-Identity';

// Where the chosen persona is kept for the tab. A per-viewer convenience: losing it only shows the picker again. The
// demo persona switch (app/demoPersona.ts) keeps its choice the same way, under its own key.
export interface PersonaStore {
  get(): string | null;
  set(name: string | null): void;
}

const STORE_KEY = 'product.devPersona';

export function sessionPersonaStore(storage: () => Storage, key: string = STORE_KEY): PersonaStore {
  let memory: string | null = null;
  return {
    get() {
      try {
        return storage().getItem(key);
      } catch {
        return memory;
      }
    },
    set(name) {
      memory = name;
      try {
        if (name === null) storage().removeItem(key);
        else storage().setItem(key, name);
      } catch {
        // Storage can be blocked; the in-memory copy still works for this page.
      }
    },
  };
}

export interface DevAuth extends AuthSession {
  readonly mode: 'development';
  readonly personas: readonly DevPersona[];
  persona(): DevPersona | null;
  choose(name: string): void;
}

export function createDevAuth(options: { personas: DevPersona[]; hostname: string; store: PersonaStore }): DevAuth {
  if (!isLoopbackHost(options.hostname)) {
    throw new Error('The development sign-in works only on a loopback host name (127.0.0.1 or localhost).');
  }
  const personas = options.personas.slice();
  const events = listeners();
  let problem: string | null = null;
  const find = (name: string | null) => (name === null ? null : personas.find(p => p.name === name) ?? null);
  let current = find(options.store.get());

  return {
    mode: 'development',
    personas,
    persona: () => current,
    choose(name) {
      const next = find(name);
      if (!next) throw new Error(`No development persona is named ${name}.`);
      current = next;
      problem = null;
      options.store.set(next.name);
      events.notify();
    },
    async headers() {
      if (!current) throw new SignInRequired('Choose a development persona to see this section.');
      return { [DEV_IDENTITY_HEADER]: current.name };
    },
    stepUp() {
      // A development persona has no sign-in to refresh: the local server turns step-up off.
      problem = 'This answer needs a fresh sign-in, which a development persona cannot give. Turn Product:WebApp:StepUp:Mode off on the local server.';
      events.notify();
    },
    onUnauthorized() {
      if (!current) return;
      problem = `The server did not sign in the persona ${current.name}. Start it in the Development environment with `
        + 'Product:DevIdentity:Enabled=true, and check that the persona is in its appsettings.Development.json.';
      current = null;
      options.store.set(null);
      events.notify();
    },
    user(): SignedInUser | null {
      return current ? { name: current.label, username: current.name } : null;
    },
    async signOut() {
      current = null;
      problem = null;
      options.store.set(null);
      events.notify();
    },
    subscribe: events.subscribe,
    problem: () => problem,
  };
}
