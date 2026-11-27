// The run's settings, read from environment variables (docs/testing.md, "The end-to-end tests"). The config, the
// fixtures and the reporter all read them here, so the main process and every worker agree.
//
//   E2E_BASE_URL     the web app, such as http://127.0.0.1:5083/app/ (a server origin alone gets /app/ added)
//   E2E_MODE         dev (the development sign-in, loopback only; the default on a loopback URL) or demo (a Demo
//                    install: the operator's signed-in Chrome, attached with E2E_CDP_PORT; the default otherwise)
//   E2E_READ_ONLY=1  skip every write: the steps open the pages and check what they show before anyone acts
//   E2E_CDP_PORT     demo mode: the DevTools port of the operator's Chrome on 127.0.0.1, signed in to the web app
//   E2E_TOKEN_ENV    demo mode: the variable that holds the operator's bearer token (default PRODUCT_DEMO_TOKEN)
//   E2E_RUN_ID       the run folder's name under runs/ (default: the start time)
//   E2E_CHROME       a Chrome executable, when the installed one is not where channel "chrome" looks
//   E2E_KIT_FOLDER   a local run: the demo kit's --folder of the server, for the rehearsal aids a step needs (lib/kit.ts)
//   DEMO_PLAY=1, DEMO_SLOW_MO_MS, DEMO_HOLD_MS, DEMO_CAPTIONS=1, DEMO_RECORD=1   "demo play" (src/demo/kit/lib/play.mjs)
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const E2E_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const SRC = path.resolve(E2E_ROOT, '..', '..');

function appBase(raw: string): URL {
  const url = new URL(raw);
  if (!/\/app\/?$/.test(url.pathname)) url.pathname = url.pathname.replace(/\/?$/, '/app/');
  if (!url.pathname.endsWith('/')) url.pathname += '/';
  url.search = '';
  url.hash = '';
  return url;
}

const flag = (name: string) => /^(1|true|yes)$/i.test(process.env[name] ?? '');

const base = appBase(process.env.E2E_BASE_URL || 'http://127.0.0.1:5083/app/');
const loopback = ['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname);
const mode = (process.env.E2E_MODE || (loopback ? 'dev' : 'demo')) as 'dev' | 'demo';
const play = flag('DEMO_PLAY');

// One run folder for the main process and its workers: the config sets E2E_RUN_ID before the workers start.
function stamp(): string {
  const d = new Date();
  const two = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}-${two(d.getHours())}${two(d.getMinutes())}${two(d.getSeconds())}${play ? '-play' : ''}`;
}
if (!process.env.E2E_RUN_ID) process.env.E2E_RUN_ID = stamp();
const runId = process.env.E2E_RUN_ID;
if (!/^[A-Za-z0-9._-]+$/.test(runId)) throw new Error('E2E_RUN_ID takes letters, digits, dots, dashes and underscores only.');
const runDir = path.join(E2E_ROOT, 'runs', runId);

const tokenVariable = process.env.E2E_TOKEN_ENV || 'PRODUCT_DEMO_TOKEN';

export const env = {
  base,
  origin: base.origin,
  loopback,
  mode,
  readOnly: flag('E2E_READ_ONLY'),
  play,
  slowMo: Math.max(0, Number(process.env.DEMO_SLOW_MO_MS ?? (play ? 600 : 0)) || 0),
  // demo play: how long the end of each step stays on screen before the next one (default 4 seconds).
  holdMs: Math.max(0, Number(process.env.DEMO_HOLD_MS ?? (play ? 4000 : 0)) || 0),
  captions: flag('DEMO_CAPTIONS'),
  record: flag('DEMO_RECORD'),
  runId,
  runDir,
  shotsDir: path.join(runDir, 'screenshots'),
  cdpPort: process.env.E2E_CDP_PORT ?? '',
  tokenVariable,
  token: mode === 'demo' ? (process.env[tokenVariable] ?? '').trim() : '',
  chrome: process.env.E2E_CHROME ?? '',
  kitFolder: mode === 'dev' ? (process.env.E2E_KIT_FOLDER ?? '').trim() : '',
  // The development identity reaches the MCP endpoint only on a local run. On a Demo install Claude Code works live.
  // Claude Code's seeded work is there on a local run, and on a demo install rebuilt by "demo sandbox rebuild" (its
  // director runs in dev mode over the install's database) or directed with the operator's Claude Code token: say so
  // with E2E_AI_SEEDED=1 for a read-only run against such an install (review, fix round 1).
  mcp: mode === 'dev',
  aiSeeded: mode === 'dev' || flag('E2E_AI_SEEDED'),
};

// The guards of the old story script: the development identity takes loopback requests only, and a Demo install is
// played read only, attached to the operator's own signed-in Chrome.
export function setupProblems(): string[] {
  const problems: string[] = [];
  if (mode !== 'dev' && mode !== 'demo') problems.push('E2E_MODE is dev or demo.');
  if (mode === 'dev' && (!loopback || base.protocol !== 'http:')) problems.push('E2E_MODE dev works only on http://127.0.0.1, http://localhost or http://[::1]: the development identity takes loopback requests only.');
  if (mode === 'demo') {
    if (!env.readOnly) problems.push('Against a Demo install the suite runs read only: set E2E_READ_ONLY=1.');
    if (!/^\d{2,5}$/.test(env.cdpPort)) problems.push('Demo mode needs E2E_CDP_PORT: the DevTools port of a Chrome on 127.0.0.1 that is signed in to the web app.');
    if (!env.token) problems.push(`Set ${tokenVariable} to the demo operator's bearer token for the web app (the token the page sends to /api after sign-in).`);
    if (base.protocol !== 'https:' && !loopback) problems.push('A Demo install is reached over https.');
  }
  return problems;
}
