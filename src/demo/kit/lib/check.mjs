// check.mjs: "demo check --base <url>", the go or no-go before a demo. It writes nothing.
//   - health: /health/live and /health/ready answer 200;
//   - config: /app/config.json says demo, and on a demo install lists every persona of the persona file;
//   - personas: each persona signs in as itself (GET /api/me) on a configuration version in force;
//   - freshness: no story approval request has expired (get_demo_freshness); one that expires within 48 hours is a
//     warning that names "demo refresh";
//   - pending: each persona's open work, as My work and Approvals count it;
//   - scenarios: each scenario of scenarios.json with its preconditions, ready or not, with what is missing.
// Exit code 1 when a global check fails or any scenario checked is not ready.
import { readFileSync } from 'node:fs';
import { UsageError } from './args.mjs';
import { Refused, at, connection, createClient, plain, readPersonas } from './client.mjs';
import { SCENARIO_FILE } from './paths.mjs';
import { count, table } from './print.mjs';

export function readScenarios(file = SCENARIO_FILE) {
  try {
    return JSON.parse(readFileSync(file, 'utf8'));
  } catch (e) {
    throw new UsageError('Cannot read the scenario file ' + file + ': ' + e.message);
  }
}

// ---------------------------------------------------------------------------------------------------------------
// The precondition language (scenarios.schema.json, $defs/precondition)
// ---------------------------------------------------------------------------------------------------------------

// True when the value meets the matcher: a plain value is equality (text case-insensitive), or an object with one of
// contains, in, min, max, exists, some (an array has an element that meets a nested where), none (no element does),
// length (an array's length meets a nested matcher).
export function meets(value, matcher) {
  if (matcher === null || typeof matcher !== 'object' || Array.isArray(matcher)) {
    if (typeof matcher === 'string' && typeof value === 'string') return value.toLowerCase() === matcher.toLowerCase();
    return value === matcher;
  }
  for (const [op, expected] of Object.entries(matcher)) {
    switch (op) {
      case 'equals':
        if (!meets(value, expected)) return false;
        break;
      case 'contains':
        if (typeof value !== 'string' || !value.toLowerCase().includes(String(expected).toLowerCase())) return false;
        break;
      case 'in':
        if (!expected.some(e => meets(value, e))) return false;
        break;
      case 'min':
        if (typeof value !== 'number' || value < expected) return false;
        break;
      case 'max':
        if (typeof value !== 'number' || value > expected) return false;
        break;
      case 'exists':
        if ((value !== undefined && value !== null) !== expected) return false;
        break;
      case 'some':
        if (!Array.isArray(value) || !value.some(v => matchesWhere(v, expected))) return false;
        break;
      case 'none':
        if (Array.isArray(value) && value.some(v => matchesWhere(v, expected))) return false;
        break;
      case 'length':
        if (!Array.isArray(value) || !meets(value.length, expected)) return false;
        break;
      default:
        throw new UsageError('Unknown matcher "' + op + '" in the scenario file.');
    }
  }
  return true;
}

export function matchesWhere(item, where = {}) {
  return Object.entries(where).every(([path, matcher]) => meets(at(item, path), matcher));
}

function boundsText(b) {
  if (b === undefined || b === null) return 'at least 1';
  if (typeof b !== 'object') return 'exactly ' + b;
  const parts = [];
  if (b.equals !== undefined) parts.push('exactly ' + b.equals);
  if (b.min !== undefined) parts.push('at least ' + b.min);
  if (b.max !== undefined) parts.push('at most ' + b.max);
  return parts.join(' and ') || 'any number';
}

// Reads with a cache, so scenarios that ask the same question ask the server once.
function reader(client) {
  const cache = new Map();
  const once = (key, fn) => {
    if (!cache.has(key)) cache.set(key, fn().then(v => ({ v }), e => ({ e })));
    return cache.get(key).then(r => {
      if (r.e) throw r.e;
      return r.v;
    });
  };
  return {
    call: (persona, tool, input) => once(JSON.stringify(['call', persona, tool, input]), () => client.call(persona, tool, input)),
    all: (persona, tool, input, list) => once(JSON.stringify(['all', persona, tool, input, list]), () => client.all(persona, tool, input, { list })),
  };
}

// Resolves {"$lookup": {...}} values in a precondition's input: a read whose matching item gives the value, such as
// a portfolio's ID by its name.
async function resolveInput(read, persona, input) {
  if (Array.isArray(input)) return Promise.all(input.map(v => resolveInput(read, persona, v)));
  if (!input || typeof input !== 'object') return input;
  if (input.$lookup) {
    const l = input.$lookup;
    const items = await read.all(l.as ?? persona, l.tool, l.input ?? {}, l.list ?? 'data.items');
    const found = items.find(i => matchesWhere(i, l.where));
    if (!found) throw new Error('no ' + l.tool + ' item matches ' + JSON.stringify(l.where));
    return at(found, l.pick);
  }
  const out = {};
  for (const [k, v] of Object.entries(input)) out[k] = await resolveInput(read, persona, v);
  return out;
}

// Whether the director could do Claude Code's part (seed/demo-director.mjs, "--mode"): always on a local server in dev
// mode, and on a demo install only when the operator's Claude Code token is given.
export function aiClientWorks(mode, env, variable = 'PRODUCT_DEMO_MCP_TOKEN') {
  return mode === 'dev' || String(env[variable] ?? '').trim() !== '';
}

// One precondition: { ok, detail }, or { ok: true, skipped: true } for one that needs what this run does not have.
export async function evaluate(pre, { read, pending, aiClient = true }) {
  if (pre.needs === 'ai-client' && !aiClient) {
    return { ok: true, skipped: true, detail: 'skipped: the director did Claude Code\'s part only with the AI client entry (PRODUCT_DEMO_MCP_TOKEN); in the demo Claude Code works live' };
  }
  try {
    if (pre.pending) {
      const row = pending.find(r => r.persona === pre.pending);
      if (!row) return { ok: false, detail: 'no pending counts for persona ' + pre.pending };
      const value = row[pre.field];
      return meets(value, pre.expect ?? { min: 1 }) ? { ok: true, detail: pre.field + ' ' + value } : { ok: false, detail: pre.field + ' is ' + value + ', expected ' + boundsText(pre.expect ?? { min: 1 }) };
    }
    const input = await resolveInput(read, pre.as, pre.input ?? {});
    if (pre.value !== undefined) {
      const env = await read.call(pre.as, pre.tool, input);
      const value = at(env, pre.value);
      // One value meets expect; one object (such as data.counts) meets where.
      const ok = pre.where ? value !== null && typeof value === 'object' && matchesWhere(value, pre.where) : meets(value, pre.expect);
      const wanted = pre.where ?? pre.expect;
      return ok ? { ok: true, detail: pre.value + ' ' + plain(JSON.stringify(value), 120) } : { ok: false, detail: pre.value + ' is ' + plain(JSON.stringify(value), 160) + ', expected ' + plain(JSON.stringify(wanted), 160) };
    }
    const list = pre.list ?? 'data.items';
    const items = pre.page === false ? at(await read.call(pre.as, pre.tool, input), list) ?? [] : await read.all(pre.as, pre.tool, input, list);
    const matching = items.filter(i => matchesWhere(i, pre.where));
    const bounds = pre.count ?? { min: 1 };
    const ok = meets(matching.length, typeof bounds === 'object' ? bounds : { equals: bounds });
    return { ok, detail: 'found ' + matching.length + (ok ? '' : ', expected ' + boundsText(bounds)) };
  } catch (e) {
    return { ok: false, detail: e instanceof Refused ? e.message : plain(e.message) };
  }
}

// ---------------------------------------------------------------------------------------------------------------
// Global checks
// ---------------------------------------------------------------------------------------------------------------

async function health(client) {
  const live = await client.get('/health/live');
  const ready = await client.get('/health/ready');
  const ok = live.status === 200 && ready.status === 200;
  return { ok, detail: 'live ' + (live.status || live.error) + ', ready ' + (ready.status || ready.error) };
}

async function config(client, personas, mode, devPrefix) {
  const r = await client.get('/app/config.json');
  if (r.status !== 200 || !r.body) return { ok: false, detail: '/app/config.json answered ' + (r.status || r.error) + '. Is the web app on (Product:WebApp:Enabled)?', offered: 0 };
  const names = new Set(personas.map(p => p.name));
  const demo = r.body.demo === true;
  if (mode === 'demo') {
    const offered = (r.body.demoPersonas ?? []).filter(p => names.has(p.name)).length;
    const ok = demo && offered === personas.length;
    return { ok, offered, detail: 'demo ' + demo + ', ' + offered + ' of ' + personas.length + ' personas offered by the persona switch' };
  }
  const listed = r.body.development?.personas ?? [];
  const offered = listed.filter(p => !p.harness && String(p.name).startsWith(devPrefix) && names.has(String(p.name).slice(devPrefix.length))).length;
  const harness = listed.filter(p => p.harness).length;
  return { ok: demo, offered, detail: 'demo ' + demo + ', the development sign-in offers ' + offered + ' of ' + personas.length + ' demo personas' + (harness ? ' (and ' + count(harness, 'harness login', 'harness logins') + ' with ?all=1)' : '') };
}

async function signIns(client, personas, mode) {
  const problems = [];
  for (const p of personas) {
    const r = await client.me(p.name);
    const me = r.body;
    if (r.status !== 200 || !me) {
      problems.push(p.name + ': /api/me answered ' + (r.status || r.error));
      continue;
    }
    if (me.actorKind !== 'person') problems.push(p.name + ': signed in as ' + me.actorKind + ', not as a person');
    if (String(me.objectId).toLowerCase() !== String(p.objectId).toLowerCase()) problems.push(p.name + ': signed in as someone else than the persona file says');
    if (mode === 'demo' && me.demoPersona?.name !== p.name) problems.push(p.name + ': the server ignored the persona header');
    if (!me.configurationSetVersion) problems.push(p.name + ': no configuration version in force (seed the store)');
  }
  return { ok: problems.length === 0, problems, detail: (personas.length - new Set(problems.map(x => x.split(':')[0])).size) + ' of ' + personas.length + ' personas sign in as themselves' };
}

async function pendingCounts(client, personas) {
  const rows = [];
  for (const p of personas) {
    try {
      const queue = await client.all(p.name, 'get_my_queue', {}, { limit: 1000 });
      const kinds = {};
      for (const item of queue) kinds[item.kind] = (kinds[item.kind] ?? 0) + 1;
      const mine = await client.all(p.name, 'list_approvals', { view: 'mine' });
      const waiting = (await client.call(p.name, 'list_approvals', { view: 'asked-by-me', top: 1 })).data?.totalCount ?? 0;
      rows.push({
        persona: p.name, label: p.label, approvalsAskedOfThem: mine.length, ofWhichOwnProposal: mine.filter(a => a.isOwnProposal).length,
        waitingOnOthers: waiting, drafts: kinds.draft ?? 0, aiDrafts: queue.filter(i => i.kind === 'draft' && i.isAiCreated).length,
        reviews: kinds.review ?? 0, documents: kinds.document ?? 0, ownerGaps: kinds['owner-gap'] ?? 0, claims: kinds.claim ?? 0, queue: queue.length,
      });
    } catch (e) {
      rows.push({ persona: p.name, label: p.label, error: e instanceof Refused ? e.message : plain(e.message) });
    }
  }
  return rows;
}

async function freshnessCheck(client, persona) {
  try {
    const d = (await client.call(persona, 'get_demo_freshness', {})).data;
    const expired = d.counts.expired;
    const soon = d.counts.expiresSoon;
    // Every request is named on its own line under the table (QA: the table cut the detail to its first request).
    const lines = d.items.map(i => (i.decision?.recordNumber ?? '') + ' ' + plain(i.decision?.statement, 90) + (i.state === 'expired' ? ' (expired)' : ' (' + i.hoursLeft + ' h left)'));
    return {
      ok: expired === 0,
      warn: expired === 0 && soon > 0,
      counts: d.counts,
      items: d.items,
      lines,
      detail: expired + ' expired, ' + soon + ' ' + (soon === 1 ? 'expires' : 'expire') + ' within ' + d.withinHours + ' hours' + (expired + soon ? ': run "demo refresh"' : ''),
    };
  } catch (e) {
    return { ok: false, detail: e instanceof Refused ? e.message : plain(e.message) };
  }
}

// The story request an approval precondition stands on (QA, fix round 1): a list_approvals precondition that names a
// decision by its statement matches the freshness items with that statement. An expired request fails the
// precondition, so the scenario row says NO GO and names "demo refresh"; one that expires soon is a warning there.
// (list_approvals still lists a lapsed request as pending until the daily refresh marks it expired.)
export function storyRequestOf(pre, items) {
  const matcher = pre?.where?.['decision.statement'];
  if (pre?.tool !== 'list_approvals' || matcher === undefined || !items?.length) return null;
  const hits = items.filter(i => meets(i.decision?.statement ?? '', matcher));
  if (!hits.length) return null;
  const expired = hits.find(i => i.state === 'expired');
  return expired ? { state: 'expired', item: expired } : { state: 'soon', item: hits[0] };
}

// ---------------------------------------------------------------------------------------------------------------
// The command
// ---------------------------------------------------------------------------------------------------------------

export async function check(args, out, { env = process.env, fetchImpl, scenarioFile } = {}) {
  const c = connection(args, { env });
  const client = createClient({ ...c, ...(fetchImpl ? { fetchImpl } : {}) });
  const file = readScenarios(scenarioFile ?? args.text('--scenarios', SCENARIO_FILE));
  const personas = readPersonas(args.text('--personas'));
  const only = args.text('--scenario');
  // --track <name>: only the scenarios a run or a track plays, such as "data-governance" or "run-15" (fix round 2,
  // product review: a presenter checks the route they will play, not all of it). The scenarios of its "before" steps count too
  // (wave 4b verify round): the presenter plays those off-screen first, so the pmo track checks S-03 for its S-03.3.
  const track = args.text('--track');
  if (only && track) throw new UsageError('Pass --scenario or --track, not both.');
  const run = track ? (file.runs ?? {})[track.toLowerCase()] : null;
  if (track && !run) throw new UsageError('No run or track ' + track + ' in the scenario file. The runs and tracks: ' + Object.keys(file.runs ?? {}).join(', ') + '.');
  const played = run ? new Set([...(run.steps ?? []), ...(run.before ?? [])].map(step => String(step).split('.')[0])) : null;
  const scenarios = (file.scenarios ?? []).filter(s => (!only || s.id === only) && (!played || played.has(s.id)));
  if (only && !scenarios.length) throw new UsageError('No scenario ' + only + ' in the scenario file.');
  const json = args.flag('--json');
  const admin = args.text('--as', 'owen');

  const result = { mode: c.mode, ready: false, checks: {}, pending: [], scenarios: [] };
  result.checks.health = await health(client);
  if (!result.checks.health.ok) {
    result.checks.health.detail += '. Is the server running at ' + c.base + '?';
  } else {
    result.checks.config = await config(client, personas, c.mode, c.devPrefix);
    result.checks.personas = await signIns(client, personas, c.mode);
    const expected = file.personaCount ?? personas.length;
    result.checks.personaCount = { ok: personas.length === expected, detail: personas.length + ' personas in the persona file, ' + expected + ' expected' };
    result.checks.freshness = await freshnessCheck(client, admin);
    if (result.checks.personas.ok) {
      result.pending = await pendingCounts(client, personas);
      const read = reader(client);
      const aiClient = aiClientWorks(c.mode, env, args.text('--ai-token-env', 'PRODUCT_DEMO_MCP_TOKEN'));
      const storyItems = result.checks.freshness?.items ?? [];
      for (const s of scenarios) {
        const checks = [];
        for (const pre of s.preconditions ?? []) {
          const x = { label: pre.label, ...(await evaluate(pre, { read, pending: result.pending, aiClient })) };
          const story = storyRequestOf(pre, storyItems);
          if (story?.state === 'expired') {
            x.ok = false;
            x.detail = 'its request ' + (story.item.decision?.recordNumber ?? '') + ' expired: run "demo refresh"';
          } else if (story?.state === 'soon' && x.ok) {
            x.warn = true;
            x.detail = (x.detail ? x.detail + '; ' : '') + 'its request expires in ' + story.item.hoursLeft + ' h: run "demo refresh"';
          }
          checks.push(x);
        }
        result.scenarios.push({
          id: s.id, title: s.title, story: s.story ?? null, ready: checks.every(x => x.ok), warn: checks.some(x => x.warn),
          passed: checks.filter(x => x.ok && !x.skipped).length, skipped: checks.filter(x => x.skipped).length, total: checks.length, checks,
        });
      }
    }
  }
  const globalOk = Object.values(result.checks).every(x => x.ok) && result.checks.personas?.ok === true;
  result.ready = globalOk && result.scenarios.length === scenarios.length && result.scenarios.every(s => s.ready);

  if (json) {
    out.log(JSON.stringify(result, null, 2));
  } else {
    print(out, result, c);
  }
  return result.ready ? 0 : 1;
}

function print(out, result, c) {
  const mark = x => (x === undefined ? 'not run' : x.ok ? (x.warn ? 'WARN' : 'ok') : 'FAIL');
  out.log('Demo check: ' + c.base.toString().replace(/\/$/, '') + ' (' + (c.mode === 'dev' ? 'local server, development identity' : 'demo install, persona switch') + ')');
  out.log('');
  const names = { health: 'Health', config: 'Config', personaCount: 'Persona count', personas: 'Persona sign-in', freshness: 'Story requests' };
  out.log(table(['Check', 'Result', 'Detail'], Object.entries(names).map(([k, label]) => [label, mark(result.checks[k]), result.checks[k]?.detail ?? ''] ), { max: 140 }));
  for (const p of result.checks.personas?.problems ?? []) out.log('    ' + p);
  for (const l of result.checks.freshness?.lines ?? []) out.log('    ' + l);

  if (result.pending.length) {
    out.log('\nPending work by persona (My work and Approvals):');
    out.log(table(['Persona', 'Asked of them', 'Waiting on others', 'Drafts', 'Reviews', 'Documents', 'Owner gaps', 'Claims', 'My work'],
      result.pending.map(r => r.error ? [r.persona, 'error: ' + r.error] : [
        r.persona, r.approvalsAskedOfThem + (r.ofWhichOwnProposal ? ' (' + r.ofWhichOwnProposal + ' own)' : ''), r.waitingOnOthers,
        r.drafts + (r.aiDrafts ? ' (' + r.aiDrafts + ' AI)' : ''), r.reviews, r.documents, r.ownerGaps, r.claims, r.queue,
      ].map(String)), { max: 120 }));
  }

  if (result.scenarios.length) {
    out.log('\nScenarios:');
    out.log(table(['ID', 'Story', 'Scenario', 'Go', 'Preconditions'], result.scenarios.map(s => [s.id, s.story ?? '', s.title, s.ready ? (s.warn ? 'GO, WARN' : 'GO') : 'NO GO',
      s.passed + ' of ' + (s.total - s.skipped) + ' met' + (s.skipped ? ', ' + s.skipped + ' skipped' : '')]), { max: 70 }));
    for (const s of result.scenarios.filter(x => !x.ready)) {
      out.log('\n  ' + s.id + ' is not ready:');
      for (const x of s.checks.filter(y => !y.ok)) out.log('    - ' + x.label + ': ' + x.detail);
    }
    for (const s of result.scenarios.filter(x => x.ready && x.warn)) {
      out.log('\n  ' + s.id + ' is ready, with a warning:');
      for (const x of s.checks.filter(y => y.warn)) out.log('    - ' + x.label + ': ' + x.detail);
    }
  }
  const notReady = result.scenarios.filter(s => !s.ready).length;
  const warned = Object.values(result.checks).some(x => x?.ok && x.warn) || result.scenarios.some(s => s.warn);
  out.log('');
  out.log(result.ready ? (warned ? 'GO, with a warning: ' : 'GO: ') + 'every check passed' + (warned ? ' (see WARN above)' : '') + ', and ' + (result.scenarios.length === 1 ? 'the scenario is ready.' : 'all ' + result.scenarios.length + ' scenarios are ready.')
    : 'NO GO: ' + (Object.values(result.checks).some(x => !x.ok) ? 'a global check failed' + (notReady ? ', and ' : '.') : '') + (notReady ? count(notReady, 'scenario is', 'scenarios are') + ' not ready.' : ''));
}
