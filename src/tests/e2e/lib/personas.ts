// The seventeen fictional personas of src/adapters/azure/seed/demo-personas.json, as the scenarios name them
// (src/docs/demo/scenarios.md, "The personas").
//
// A local run signs the browser in with the development persona the server offers for the person
// (appsettings.Development.json, Product:WebApp:DevelopmentPersonas): demo-<name> for all seventeen, as the sign-in page
// lists them (product review, fix round 1). The API checks use the same logins. On a Demo install the
// persona switch takes the bare name, both in the page (product.demoPersona) and in the X-Product-Demo-Persona header.
import { env } from './env';

export const PERSONAS = {
  lena: { name: 'Lena Okoro', role: 'Requester' },
  priya: { name: 'Priya Castell', role: 'Maker' },
  nadia: { name: 'Nadia Ferreira', role: 'Application owner' },
  kai: { name: 'Kai Ostrowski', role: 'Data owner' },
  elif: { name: 'Elif Dunmore', role: 'Architect' },
  hugo: { name: 'Hugo Brandvold', role: 'Change manager' },
  tomas: { name: 'Tomas Rendel', role: 'Change board' },
  marcus: { name: 'Marcus Oyelaran', role: 'Architect and change board' },
  mateo: { name: 'Mateo Brandt', role: 'PMO' },
  dmitri: { name: 'Dmitri Halloran', role: 'AI lead' },
  noor: { name: 'Noor Safadi', role: 'Security reviewer' },
  owen: { name: 'Owen Achterberg', role: 'Platform admin' },
  yusuf: { name: 'Yusuf Brennan', role: 'Configuration admin' },
  jonah: { name: 'Jonah Pereira', role: 'Configuration admin' },
  bianca: { name: 'Bianca Ferro', role: 'Auditor' },
  theo: { name: 'Theo Marsh', role: 'Reader' },
  hana: { name: 'Hana Lindqvist', role: 'Data governance lead' },
} as const;

export type Persona = keyof typeof PERSONAS;

// The web app keeps the chosen persona for the tab under these keys (ui/harness/src/auth/devAuth.ts and
// ui/harness/src/app/demoPersona.ts). The fixture writes the same key before the page's scripts run.
export const DEV_PERSONA_KEY = 'product.devPersona';
export const DEMO_PERSONA_KEY = 'product.demoPersona';
export const personaKey = () => (env.mode === 'dev' ? DEV_PERSONA_KEY : DEMO_PERSONA_KEY);

// The value the page stores: the development persona's name on a local run, the bare name on a Demo install.
export function browserLogin(persona: Persona): string {
  return env.mode === 'dev' ? `demo-${persona}` : persona;
}

export const displayName = (persona: Persona) => PERSONAS[persona].name;
