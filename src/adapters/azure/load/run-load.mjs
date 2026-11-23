#!/usr/bin/env node
// A load run against the local server: many read calls at once from the fictional development personas, through the
// REST API (the web app's personas) and the MCP endpoint (the AI client personas). It prints the answers by status and
// the time each took. No packages: Node 22 or later.
//
// It only calls a server on this machine: the base URL must be http://127.0.0.1, http://localhost or http://[::1]. The
// personas sign in with the development identity header, which the local server takes only in Development, with the
// switch on, on a loopback address. By default it only reads; --writes adds intake drafts, which stay in the local file.
//
// Usage, from src/adapters/azure, with the local server running (see README.md, "Run it locally"):
//   node load/run-load.mjs --base http://127.0.0.1:5080 --requests 2000 --concurrency 32
// Options:
//   --base <url>          the local server (default http://127.0.0.1:5080)
//   --requests <n>        how many calls in all (default 1000)
//   --concurrency <n>     how many calls at once (default 16)
//   --writes              also save intake drafts as priya through the MCP endpoint (one call in ten)
//   --json                print the summary as JSON
// Exit code: 0 when no call failed with a server failure (5xx) or a network error, 1 otherwise, 2 for bad usage.
// A 429 answer is the request limit per caller at work: it is counted and reported, not a failure. The limits count per
// caller, route group and UTC minute in the store (defaults: 120 MCP and 300 REST requests a minute). With the defaults,
// --requests 2000 sends 150 MCP calls for each of the five AI client personas and 250 REST calls for each web app persona,
// so a run on fresh counters that ends inside one minute answers 429 to exactly 150 calls. A run that crosses a minute
// boundary gets fewer, and a second run inside the same minute gets many more, because the store keeps the counts.

const args = process.argv.slice(2);
const flag = name => args.includes(name);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};

const base = new URL(opt('--base', 'http://127.0.0.1:5080'));
const requests = Number(opt('--requests', '1000'));
const concurrency = Number(opt('--concurrency', '16'));
const writes = flag('--writes');
const asJson = flag('--json');

if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)) {
  console.error('run-load: --base must be a local server on http://127.0.0.1, http://localhost or http://[::1].');
  process.exit(2);
}
if (!Number.isInteger(requests) || requests < 1 || !Number.isInteger(concurrency) || concurrency < 1 || concurrency > 256) {
  console.error('run-load: --requests must be 1 or more, --concurrency from 1 to 256.');
  process.exit(2);
}

const DEV_HEADER = 'X-Product-Dev-Identity';
const WEB = ['nadia-web', 'priya-web', 'owen-web', 'mateo-web', 'config-admin-web'];
const MCP = ['priya', 'nadia', 'owen', 'mateo', 'config-admin'];

function today(offsetDays) {
  const d = new Date(Date.now() + offsetDays * 86400000);
  return d.toISOString().slice(0, 10);
}

// One call of each kind, as a function of the call number. Reads only, unless --writes.
const kinds = [
  { name: 'rest get_portfolio_summary', rest: 'get_portfolio_summary', body: { includeOwnerGaps: true } },
  { name: 'rest find_applications', rest: 'find_applications', body: { text: 'inspection' } },
  { name: 'rest get_change_schedule', rest: 'get_change_schedule', body: { from: today(-7), to: today(30) } },
  { name: 'rest get_my_queue', rest: 'get_my_queue', body: {} },
  { name: 'rest me', get: '/api/me' },
  { name: 'mcp find_applications', mcp: 'find_applications', args: { text: 'crew' } },
  { name: 'mcp get_portfolio_summary', mcp: 'get_portfolio_summary', args: {} },
  { name: 'mcp whoami', mcp: 'whoami', args: {} },
];

let intakeNumber = 0;
function pick(n) {
  if (writes && n % 10 === 9) {
    intakeNumber++;
    return {
      name: 'mcp save_intake_draft',
      persona: 'priya',
      mcp: 'save_intake_draft',
      args: { title: `Load run intake ${intakeNumber}`, need: 'Crews retype site forms at the office; a load run draft.', proposedKindKey: 'power-platform-app' },
    };
  }
  const kind = kinds[n % kinds.length];
  const personas = kind.mcp ? MCP : WEB;
  return { ...kind, persona: personas[Math.floor(n / kinds.length) % personas.length] };
}

async function call(n) {
  const job = pick(n);
  const headers = { [DEV_HEADER]: job.persona };
  let url;
  let init;
  if (job.get) {
    url = new URL(job.get, base);
    init = { method: 'GET', headers };
  } else if (job.rest) {
    url = new URL(`/api/${job.rest}`, base);
    init = { method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' }, body: JSON.stringify(job.body) };
  } else {
    url = new URL('/mcp', base);
    const message = { jsonrpc: '2.0', id: n + 1, method: 'tools/call', params: { name: job.mcp, arguments: job.args } };
    init = { method: 'POST', headers: { ...headers, 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' }, body: JSON.stringify(message) };
  }
  const started = performance.now();
  try {
    const response = await fetch(url, { ...init, redirect: 'manual', signal: AbortSignal.timeout(60000) });
    const text = await response.text();
    let ok = response.ok;
    if (ok && job.mcp) {
      const body = JSON.parse(text);
      ok = body.result?.structuredContent?.ok === true;
    } else if (ok && job.rest) {
      ok = JSON.parse(text).ok === true;
    }
    return { name: job.name, status: response.status, ok, ms: performance.now() - started, retryAfter: response.headers.get('retry-after') };
  } catch (e) {
    return { name: job.name, status: 0, ok: false, ms: performance.now() - started, error: e.name === 'TimeoutError' ? 'timeout' : e.message };
  }
}

function percentile(sorted, p) {
  if (sorted.length === 0) return 0;
  return sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(p * sorted.length) - 1))];
}

const health = await fetch(new URL('/health/ready', base)).catch(() => null);
if (!health || health.status !== 200) {
  console.error(`run-load: ${base.origin}/health/ready did not answer 200. Start the local server first.`);
  process.exit(2);
}

const results = [];
let next = 0;
const started = performance.now();
await Promise.all(Array.from({ length: Math.min(concurrency, requests) }, async () => {
  while (next < requests) {
    const n = next++;
    results.push(await call(n));
  }
}));
const seconds = (performance.now() - started) / 1000;

const byStatus = {};
for (const r of results) byStatus[r.status] = (byStatus[r.status] ?? 0) + 1;
const served = results.filter(r => r.status !== 429 && r.status !== 0).map(r => r.ms).sort((a, b) => a - b);
const byKind = {};
for (const r of results) {
  const k = (byKind[r.name] ??= { calls: 0, ms: [] });
  k.calls++;
  if (r.status !== 429 && r.status !== 0) k.ms.push(r.ms);
}
const failures = results.filter(r => r.status === 0 || r.status >= 500);
const notOk = results.filter(r => r.status === 200 && !r.ok);
const summary = {
  base: base.origin,
  requests,
  concurrency,
  writes,
  seconds: Number(seconds.toFixed(2)),
  perSecond: Number((results.length / seconds).toFixed(1)),
  byStatus,
  served: {
    p50: Math.round(percentile(served, 0.5)),
    p95: Math.round(percentile(served, 0.95)),
    p99: Math.round(percentile(served, 0.99)),
    max: Math.round(served.at(-1) ?? 0),
  },
  byKind: Object.fromEntries(Object.entries(byKind).map(([name, k]) => {
    const ms = k.ms.sort((a, b) => a - b);
    return [name, { calls: k.calls, p50: Math.round(percentile(ms, 0.5)), p95: Math.round(percentile(ms, 0.95)) }];
  })),
  serverFailures: failures.length,
  answeredNotOk: notOk.length,
};

if (asJson) {
  console.log(JSON.stringify(summary, null, 2));
} else {
  console.log(`${summary.requests} calls, ${summary.concurrency} at once, in ${summary.seconds} s (${summary.perSecond} a second)${writes ? ', with writes' : ''}`);
  console.log(`answers by status: ${Object.entries(byStatus).map(([s, c]) => `${s === '0' ? 'network error' : s} ${c}`).join(', ')}`);
  console.log(`time per served call: p50 ${summary.served.p50} ms, p95 ${summary.served.p95} ms, p99 ${summary.served.p99} ms, max ${summary.served.max} ms`);
  for (const [name, k] of Object.entries(summary.byKind)) {
    console.log(`  ${name.padEnd(28)} ${String(k.calls).padStart(6)} calls  p50 ${String(k.p50).padStart(5)} ms  p95 ${String(k.p95).padStart(5)} ms`);
  }
  if (notOk.length) console.log(`answered 200 without ok: ${notOk.length}`);
  if (failures.length) console.log(`server failures or network errors: ${failures.length} (first: ${failures[0].name} ${failures[0].status} ${failures[0].error ?? ''})`);
}
process.exit(failures.length ? 1 : 0);
