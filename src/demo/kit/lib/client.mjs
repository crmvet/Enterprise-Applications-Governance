// client.mjs: calls to a running server as the demo personas, the same way the demo director makes them.
//   dev   a local server with the development identity: each request names the development persona demo-<name> in
//         X-Product-Dev-Identity. Loopback http URLs only. The default for a loopback URL.
//   demo  a demo install (D-39): each request carries the demo operator's own bearer token for the web app, read from
//         an environment variable (PRODUCT_DEMO_TOKEN unless --token-env names another), and names the persona in
//         X-Product-Demo-Persona. The token is never printed.
// Text from the server is printed through plain(), which turns IDs into <id>.
import { readFileSync } from 'node:fs';
import { UsageError } from './args.mjs';
import { PERSONA_FILE } from './paths.mjs';

export class Refused extends Error {
  constructor(tool, status, code, message) {
    super(tool + ': ' + code + ' (' + status + '). ' + message);
    this.tool = tool;
    this.code = code;
    this.status = status;
  }
}

const ID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

// Text made safe to print: IDs become <id>, white space is one space, and long text is cut.
export function plain(text, max = 300) {
  const s = String(text ?? '').replace(ID, '<id>').replace(/\s+/g, ' ').trim();
  return s.length > max ? s.slice(0, max - 3) + '...' : s;
}

export function readPersonas(file = PERSONA_FILE) {
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(file, 'utf8'));
  } catch (e) {
    throw new UsageError('Cannot read the persona file ' + file + ': ' + e.message);
  }
  return (parsed.personas ?? []).map(p => ({ ...p, person: String(p.label ?? p.name).split(',')[0] }));
}

const LOOPBACK = ['127.0.0.1', 'localhost', '[::1]'];

export function isLoopback(url) {
  return LOOPBACK.includes(url.hostname);
}

// The base URL, the mode and the token, from the options. Throws UsageError for a bad combination.
export function connection(args, { env = process.env } = {}) {
  const text = args.text('--base');
  if (!text) throw new UsageError('--base <url> names the server, such as http://127.0.0.1:5080.');
  let base;
  try {
    base = new URL(text);
  } catch {
    throw new UsageError('--base is not a URL.');
  }
  const loopback = isLoopback(base);
  const mode = args.text('--mode', loopback ? 'dev' : 'demo');
  if (mode !== 'dev' && mode !== 'demo') throw new UsageError('--mode is dev or demo.');
  if (mode === 'dev' && (!loopback || base.protocol !== 'http:')) {
    throw new UsageError('--mode dev works only against a local server on http://127.0.0.1, http://localhost or http://[::1]: the development identity takes loopback requests only.');
  }
  if (mode === 'demo' && base.protocol !== 'https:' && !loopback) throw new UsageError('A demo install is reached over https.');
  const tokenVariable = args.text('--token-env', 'PRODUCT_DEMO_TOKEN');
  const token = mode === 'demo' ? String(env[tokenVariable] ?? '').trim() : '';
  if (mode === 'demo' && !token) {
    throw new UsageError('Set ' + tokenVariable + ' to the demo operator\'s bearer token for the web app (the token the page sends to /api after sign-in). It is never printed.');
  }
  return {
    base,
    mode,
    token,
    tokenVariable,
    devPrefix: args.text('--dev-prefix', 'demo-'),
    timeoutSeconds: args.number('--timeout', 180, { min: 5, max: 3600 }),
  };
}

export function createClient({ base, mode, token = '', devPrefix = 'demo-', timeoutSeconds = 180, fetchImpl = fetch }) {
  const counters = { requests: 0 };

  async function send(path, init = {}) {
    for (let attempt = 0; ; attempt++) {
      counters.requests++;
      let response;
      try {
        response = await fetchImpl(new URL(path, base), { ...init, redirect: 'manual', signal: AbortSignal.timeout(timeoutSeconds * 1000) });
      } catch (e) {
        const why = e?.name === 'TimeoutError' ? 'no answer within ' + timeoutSeconds + ' seconds (--timeout)' : plain(e?.cause?.code ?? e?.message ?? e);
        return { status: 0, body: null, text: '', error: why };
      }
      const text = await response.text();
      // The request limits per caller answer 429 with Retry-After; wait and try again, a few times.
      if (response.status === 429 && attempt < 3) {
        const wait = Math.min(60, Number(response.headers.get('retry-after')) || 5);
        await new Promise(resolve => setTimeout(resolve, wait * 1000));
        continue;
      }
      let body = null;
      try {
        body = text ? JSON.parse(text) : null;
      } catch {
        body = null;
      }
      return { status: response.status, body, text };
    }
  }

  function headers(personaName) {
    return mode === 'dev'
      ? { 'X-Product-Dev-Identity': devPrefix + personaName }
      : { Authorization: 'Bearer ' + token, 'X-Product-Demo-Persona': personaName };
  }

  function envelopeOf(tool, r) {
    const env = r.body;
    if (env && env.ok === true) return env;
    if (r.status === 0) throw new Refused(tool, 0, 'no-answer', r.error ?? 'The server did not answer.');
    const error = env?.error ?? {};
    throw new Refused(tool, r.status, error.code ?? 'no-envelope', plain(error.message ?? 'The server gave no envelope.'));
  }

  // One tool call on the REST API as the persona (the person entry).
  async function call(personaName, tool, input = {}) {
    return envelopeOf(tool, await send('/api/' + tool, {
      method: 'POST',
      headers: { ...headers(personaName), 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    }));
  }

  // Every item of a paged list, up to limit.
  async function all(personaName, tool, input = {}, { list = 'data.items', limit = 2000 } = {}) {
    const items = [];
    let skipToken;
    do {
      const env = await call(personaName, tool, { ...input, top: 100, ...(skipToken ? { skipToken } : {}) });
      items.push(...(at(env, list) ?? []));
      skipToken = env.data?.skipToken ?? undefined;
    } while (skipToken && items.length < limit);
    return items;
  }

  async function me(personaName) {
    return send('/api/me', { method: 'GET', headers: headers(personaName) });
  }

  async function get(path) {
    return send(path, { method: 'GET' });
  }

  return { base, mode, send, call, all, me, get, headers, counters };
}

// The value at a dot path such as "data.items" or "data.counts.expired". Numbers index arrays.
export function at(value, path) {
  if (!path) return value;
  let v = value;
  for (const part of String(path).split('.')) {
    if (v === null || v === undefined) return undefined;
    v = v[part];
  }
  return v;
}
