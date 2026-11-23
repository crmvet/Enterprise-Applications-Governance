// The web app's runtime configuration: <base>config.json, read once at startup.
//
// One build serves any tenant. The host writes config.json from its own settings (it holds no secret); in the local
// Vite dev loop, public/config.json stands in. Shape, version 1:
//   {
//     "schemaVersion": 1,
//     "apiBase": "/api",                                   same origin by default; an absolute https URL is allowed
//     "auth": { "mode": "entra", "authority": "https://login.microsoftonline.com/<tenant id>",
//               "clientId": "<the web app's client ID>", "apiScope": "api://<API app ID>/api.access" },
//     "development": { "personas": [ { "name": "nadia-web", "label": "Nadia Ferreira, business owner" } ] }
//   }
// "auth": { "mode": "development" } signs in as a fictional persona with the X-Product-Dev-Identity header. The page
// takes it only on a loopback host name, and the server keeps its own guards (Development environment, a switch that is
// off in every committed file, loopback requests only).
//
// The page refuses a non-https authority and a cross-origin API base that is not https. The bearer token only ever
// goes to apiBase.

export const CONFIG_SCHEMA_VERSION = 1;

export interface DevPersona {
  name: string;
  label: string;
}

export type AuthConfig =
  | { mode: 'entra'; authority: string; clientId: string; apiScope: string }
  | { mode: 'development' };

export interface AppConfig {
  schemaVersion: typeof CONFIG_SCHEMA_VERSION;
  // An absolute URL with no trailing slash, such as https://host/api.
  apiBase: string;
  auth: AuthConfig;
  development: { personas: DevPersona[] };
}

// The parts of window.location the checks need.
export interface PageLocation {
  origin: string;
  hostname: string;
}

export type ConfigResult = { ok: true; config: AppConfig } | { ok: false; problems: string[] };

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const PERSONA_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

// localhost, 127.0.0.0/8 and ::1. The same test the server's development identity applies to the host name.
export function isLoopbackHost(hostname: string): boolean {
  const h = hostname.toLowerCase().replace(/^\[/, '').replace(/\]$/, '');
  if (h === 'localhost' || h === '::1') return true;
  const m = /^127\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(h);
  return !!m && m.slice(1).every(n => Number(n) <= 255);
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= max;

function parseUrl(value: string, base?: string): URL | null {
  try {
    return base === undefined ? new URL(value) : new URL(value, base);
  } catch {
    return null;
  }
}

export function validateConfig(raw: unknown, page: PageLocation): ConfigResult {
  const problems: string[] = [];
  if (!isObject(raw)) return { ok: false, problems: ['config.json is not a JSON object.'] };
  if (raw.schemaVersion !== CONFIG_SCHEMA_VERSION) problems.push(`schemaVersion must be ${CONFIG_SCHEMA_VERSION}.`);

  // ---- apiBase ----
  let apiBase = '';
  if (!text(raw.apiBase, 2000)) {
    problems.push('apiBase is missing.');
  } else {
    const url = parseUrl(raw.apiBase, page.origin);
    if (!url) {
      problems.push('apiBase is not a URL or a path.');
    } else if (url.username || url.password || url.search || url.hash) {
      problems.push('apiBase must not carry credentials, a query or a fragment.');
    } else if (url.protocol !== 'https:' && !(url.protocol === 'http:' && url.origin === page.origin)) {
      problems.push('apiBase must be https, or a path on this page\'s own origin.');
    } else {
      apiBase = url.origin + url.pathname.replace(/\/+$/, '');
    }
  }

  // ---- auth ----
  let auth: AuthConfig | null = null;
  const a = raw.auth;
  if (!isObject(a)) {
    problems.push('auth is missing.');
  } else if (a.mode === 'entra') {
    const authority = text(a.authority, 500) ? parseUrl(a.authority) : null;
    if (!authority || authority.protocol !== 'https:' || authority.username || authority.password || authority.search || authority.hash) {
      problems.push('auth.authority must be an https URL, such as https://login.microsoftonline.com/<tenant id>.');
    }
    if (typeof a.clientId !== 'string' || !GUID.test(a.clientId)) problems.push('auth.clientId must be the web app\'s client ID (a GUID).');
    if (!text(a.apiScope, 400) || /\s/.test(a.apiScope) || !a.apiScope.includes('/')) {
      problems.push('auth.apiScope must be the API scope, such as api://<API app ID>/api.access.');
    }
    if (authority && typeof a.clientId === 'string' && text(a.apiScope, 400)) {
      auth = { mode: 'entra', authority: authority.toString().replace(/\/+$/, ''), clientId: a.clientId, apiScope: a.apiScope };
    }
  } else if (a.mode === 'development') {
    if (!isLoopbackHost(page.hostname)) problems.push('The development sign-in works only on a loopback host name (127.0.0.1 or localhost).');
    auth = { mode: 'development' };
  } else {
    problems.push('auth.mode must be "entra" or "development".');
  }

  // ---- development personas ----
  const personas: DevPersona[] = [];
  const d = raw.development;
  if (d !== undefined && d !== null) {
    if (!isObject(d) || !Array.isArray(d.personas)) {
      problems.push('development.personas must be a list.');
    } else {
      for (const p of d.personas.slice(0, 50)) {
        if (isObject(p) && typeof p.name === 'string' && PERSONA_NAME.test(p.name) && text(p.label, 120)) {
          personas.push({ name: p.name, label: p.label });
        } else {
          problems.push('Each development persona needs a name (letters, digits, dot, dash or underscore) and a label.');
          break;
        }
      }
    }
  }
  if (auth?.mode === 'development' && !personas.length) problems.push('The development sign-in needs at least one persona.');

  if (problems.length || !auth) return { ok: false, problems };
  return { ok: true, config: { schemaVersion: CONFIG_SCHEMA_VERSION, apiBase, auth, development: { personas } } };
}

// Reads <base>config.json. Never cached: the host serves it with no-store, and a stale copy could point at an old API.
export async function loadConfig(fetchFn: typeof fetch, url: string, page: PageLocation): Promise<ConfigResult> {
  let response: Response;
  try {
    response = await fetchFn(url, { cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } });
  } catch {
    return { ok: false, problems: ['config.json could not be read. Check the connection and reload the page.'] };
  }
  if (!response.ok) return { ok: false, problems: [`config.json answered ${response.status}.`] };
  let raw: unknown;
  try {
    raw = await response.json();
  } catch {
    return { ok: false, problems: ['config.json is not JSON.'] };
  }
  return validateConfig(raw, page);
}
