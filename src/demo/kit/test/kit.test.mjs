// kit.test.mjs: the demo kit's own tests. Offline: no product server, no WSL, no Azure. The commands that call a
// server run against a small fake server on 127.0.0.1 that answers the way the product does.
// Run from src/: node --test demo/kit/test/kit.test.mjs (tools/ci.mjs runs it as its "kit" check).
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { UsageError, parseArgs } from '../lib/args.mjs';
import { check, evaluate, matchesWhere, meets, readScenarios, storyRequestOf } from '../lib/check.mjs';
import { plain, readPersonas } from '../lib/client.mjs';
import { local, mirrorRules, serverArgs, snapshotOf } from '../lib/local.mjs';
import { SRC, toWslPath } from '../lib/paths.mjs';
import { play, playPlan } from '../lib/play.mjs';
import { refresh } from '../lib/refresh.mjs';
import { decisionNumber, engineTime } from '../lib/rehearse.mjs';
import { STEPS, parseEnvValues, redact, sandbox, shownPath } from '../lib/sandbox.mjs';

function capture() {
  const lines = [];
  const sink = { write: c => lines.push(String(c)) };
  return { lines, out: { log: l => lines.push(l), error: l => lines.push(l), outStream: sink, errStream: sink }, text: () => lines.join('\n') };
}

// ---- the command line ----

test('options take a value, a value after =, or nothing for a flag', () => {
  const a = parseArgs(['local', 'up', '--port', '5086', '--folder=/root/x', '--native', '--rebuild']);
  assert.deepEqual(a.words, ['local', 'up']);
  assert.equal(a.number('--port', 5080), 5086);
  assert.equal(a.text('--folder'), '/root/x');
  assert.equal(a.flag('--native'), true);
  assert.equal(a.text('--missing', 'fallback'), 'fallback');
  assert.deepEqual(a.passOn(['--native']), ['--port=5086', '--folder=/root/x', '--rebuild']);
  assert.throws(() => parseArgs(['check', '--base']), UsageError);
  assert.throws(() => parseArgs(['local', 'up', '--port', 'x']).number('--port', 1), UsageError);
});

test('a Windows path reads the way WSL mounts it', () => {
  assert.equal(toWslPath('D:\\Github\\repo\\src'), '/mnt/d/Github/repo/src');
  assert.equal(toWslPath('/root/eag-kit'), '/root/eag-kit');
});

test('the copy into WSL keeps the built web app and leaves out build output, packages and stores', () => {
  const rules = mirrorRules();
  assert.ok(rules.indexOf('--include=/ui/harness/dist/app/***') < rules.indexOf('--exclude=dist/'), 'the web app is included before dist/ is left out');
  for (const r of ['--exclude=bin/', '--exclude=obj/', '--exclude=node_modules/', '--exclude=*.db', '--exclude=.azure/', '--exclude=/tests/e2e/runs/']) assert.ok(rules.includes(r), r);
  assert.equal(snapshotOf('/root/eag-kit/data/demo.db'), '/root/eag-kit/data/demo.after-director.db');
});

test('the local server runs in Development on loopback with the demo switches', () => {
  const a = serverArgs({ port: 5086, db: '/tmp/demo.db', webApp: '/tmp/app' });
  for (const s of ['Development', 'http://127.0.0.1:5086', '--Product:DevIdentity:Enabled=true', '--Product:WebApp:Demo=true', '--Product:Directory:Mode=Demo', '--Product:WebApp:RootPath=/tmp/app']) {
    assert.ok(a.includes(s), s);
  }
});

test('printed text carries no ID', () => {
  assert.equal(plain('Decision 00000000-0000-4000-8000-000000000701 waits'), 'Decision <id> waits');
  assert.equal(redact('see https://host.example.org/x and 10.1.2.3'), 'see <url> and <address>');
});

// ---- the scenario file ----

const contracts = JSON.parse(readFileSync(join(SRC, 'mcp', 'contracts', 'tools.json'), 'utf8'));
const toolNames = new Set((contracts.tools ?? contracts).map(t => t.name));
const readOnly = name => {
  const file = join(SRC, 'mcp', 'contracts', 'tools', name + '.output.schema.json');
  return existsSync(file) && (JSON.parse(readFileSync(file, 'utf8'))['x-ai-write-states'] ?? []).includes('read-only');
};

test('every scenario has a unique ID, known personas, and preconditions that only read', () => {
  const file = readScenarios();
  const personas = new Set(readPersonas().map(p => p.name));
  assert.equal(file.personaCount, personas.size, 'personaCount matches the persona file');
  const ids = new Set();
  const lookups = input => (input && typeof input === 'object' ? Object.values(input).flatMap(v => (v?.$lookup ? [v.$lookup, ...lookups(v.$lookup.input)] : lookups(v))) : []);
  for (const s of file.scenarios) {
    assert.match(s.id, /^S-\d{2}$/);
    assert.ok(!ids.has(s.id), 'unique ' + s.id);
    ids.add(s.id);
    for (const p of s.personas) assert.ok(personas.has(p), s.id + ' persona ' + p);
    assert.ok((s.preconditions ?? []).length > 0, s.id + ' has preconditions');
    for (const pre of s.preconditions) {
      if (pre.pending) {
        assert.ok(personas.has(pre.pending), s.id + ': ' + pre.label);
        continue;
      }
      assert.ok(personas.has(pre.as), s.id + ': ' + pre.label + ': persona ' + pre.as);
      for (const tool of [pre.tool, ...lookups(pre.input).map(l => l.tool)]) {
        assert.ok(toolNames.has(tool), s.id + ': ' + pre.label + ': tool ' + tool + ' exists');
        assert.ok(readOnly(tool), s.id + ': ' + pre.label + ': ' + tool + ' only reads');
      }
      if (pre.value !== undefined) assert.ok(pre.expect !== undefined || pre.where !== undefined, s.id + ': ' + pre.label + ': a value check names what it expects');
    }
  }
  assert.ok(ids.size >= 15, 'the fifteen steps of the guided demo, and more');
});

// ---- the precondition language ----

test('matchers: equality ignores case, and contains, in, min, max, exists, some, none and length test', () => {
  assert.ok(meets('Field operations', 'field operations'));
  assert.ok(meets(3, { min: 2, max: 3 }));
  assert.ok(!meets(4, { max: 3 }));
  assert.ok(meets('Authorize the change: Retire the old export flow', { contains: 'old export' }));
  assert.ok(meets('due', { in: ['due', 'overdue'] }));
  assert.ok(meets(null, { exists: false }));
  assert.ok(meets([{ a: 1 }, { a: 2 }], { some: { a: 2 }, length: { equals: 2 } }));
  assert.ok(meets([{ a: 1 }], { none: { a: 2 } }));
  assert.ok(matchesWhere({ decision: { statement: 'Keep it' } }, { 'decision.statement': { contains: 'keep' } }));
  assert.throws(() => meets(1, { near: 1 }), UsageError);
});

test('a precondition counts matching items, reads one value, or reads a pending count', async () => {
  const read = {
    all: async () => [{ name: 'A', n: 1 }, { name: 'B', n: 2 }],
    call: async () => ({ ok: true, data: { ownerGaps: 2, items: [{ portfolioId: 'p1', name: 'Field operations' }] } }),
  };
  const pending = [{ persona: 'kai', approvalsAskedOfThem: 1 }];
  assert.equal((await evaluate({ label: 'x', as: 'owen', tool: 't', where: { n: { min: 2 } } }, { read, pending })).ok, true);
  assert.equal((await evaluate({ label: 'x', as: 'owen', tool: 't', where: { name: 'C' } }, { read, pending })).ok, false);
  assert.equal((await evaluate({ label: 'x', as: 'owen', tool: 't', count: { equals: 2 } }, { read, pending })).ok, true);
  assert.equal((await evaluate({ label: 'x', as: 'owen', tool: 't', value: 'data.ownerGaps', expect: 2 }, { read, pending })).ok, true);
  assert.equal((await evaluate({ label: 'x', pending: 'kai', field: 'approvalsAskedOfThem' }, { read, pending })).ok, true);
  assert.equal((await evaluate({ label: 'x', pending: 'kai', field: 'approvalsAskedOfThem', expect: { min: 2 } }, { read, pending })).ok, false);
  const looked = await evaluate({
    label: 'x', as: 'owen', tool: 't', value: 'data.ownerGaps', expect: 2,
    input: { portfolioId: { $lookup: { tool: 'list_portfolios', where: { name: 'B' }, pick: 'name' } } },
  }, { read, pending });
  assert.equal(looked.ok, true);
});

// ---- a fake product server ----

const PERSONAS = readPersonas();

function fakeServer(handlers) {
  const calls = [];
  const server = createServer((req, res) => {
    let body = '';
    req.on('data', c => { body += c; });
    req.on('end', () => {
      const send = (status, value) => {
        res.writeHead(status, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(value));
      };
      const who = String(req.headers['x-product-dev-identity'] ?? '').replace(/^demo-/, '');
      if (req.url === '/health/live' || req.url === '/health/ready') return send(200, { status: 'ok' });
      if (req.url === '/app/config.json') return send(200, { demo: true, development: { personas: PERSONAS.map(p => ({ name: 'demo-' + p.name, label: p.label })) }, demoPersonas: [] });
      if (req.url === '/api/me') {
        const p = PERSONAS.find(x => x.name === who);
        return p ? send(200, { actorKind: 'person', objectId: p.objectId, configurationSetVersion: 1, demoPersona: null }) : send(401, {});
      }
      const tool = req.url.replace('/api/', '');
      const input = body ? JSON.parse(body) : {};
      calls.push({ who, tool, input });
      const h = handlers[tool];
      if (!h) return send(200, { ok: true, data: { items: [], totalCount: 0 } });
      const r = h(input, who);
      return r.ok === false ? send(r.status ?? 422, r) : send(200, r);
    });
  });
  return new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve({ server, calls, base: 'http://127.0.0.1:' + server.address().port })));
}

const fresh = { ok: true, data: { withinHours: 48, counts: { expired: 0, expiresSoon: 0 }, needsRefresh: false, items: [], refreshWith: 'demo refresh', renewTool: 'renew_approval_request' } };

test('demo check: GO when every check passes, NO GO with what is missing', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'kit-check-'));
  const scenarioFile = join(dir, 'scenarios.json');
  writeFileSync(scenarioFile, JSON.stringify({
    description: 'test', personaCount: PERSONAS.length,
    scenarios: [
      { id: 'S-01', title: 'Kai answers', personas: ['kai'], useCases: [], screens: [], preconditions: [{ label: 'Kai has the card', as: 'kai', tool: 'list_approvals', input: { view: 'mine' }, where: { 'decision.statement': { contains: 'safety findings' } } }] },
      { id: 'S-02', title: 'Teams', personas: ['owen'], useCases: [], screens: [], preconditions: [{ label: 'the Change board waits', as: 'owen', tool: 'list_teams', where: { name: 'Change board', waitingCount: { min: 1 } } }] },
    ],
    runs: {
      'kai-track': { title: 'Track: Kai', doc: 'tracks.md', steps: ['S-01.1', 'S-01.2'] },
      'board-track': { title: 'Track: the board', doc: 'tracks.md', before: ['S-02.1'], steps: ['S-01.1'] },
    },
  }));
  const { server, base } = await fakeServer({
    get_demo_freshness: () => fresh,
    list_approvals: (input, who) => ({ ok: true, data: { items: who === 'kai' && input.view === 'mine' ? [{ decision: { statement: 'Allow Visit Report Agent to send the open safety findings' } }] : [], totalCount: 0 } }),
    list_teams: () => ({ ok: true, data: { items: [{ name: 'Change board', waitingCount: 0 }], totalCount: 1 } }),
  });
  try {
    const c = capture();
    const code = await check(parseArgs(['check', '--base', base]), c.out, { scenarioFile });
    assert.equal(code, 1, c.text());
    assert.match(c.text(), /S-01 .* GO/);
    assert.match(c.text(), /S-02 is not ready:\n {4}- the Change board waits: found 0/);
    assert.match(c.text(), /NO GO: 1 scenario is not ready/);
    const ok = capture();
    assert.equal(await check(parseArgs(['check', '--base', base, '--scenario', 'S-01']), ok.out, { scenarioFile }), 0, ok.text());
    assert.match(ok.text(), /GO: every check passed/);
    // --track checks only the scenarios the track plays (product review, fix round 2).
    const tracked = capture();
    assert.equal(await check(parseArgs(['check', '--base', base, '--track', 'kai-track']), tracked.out, { scenarioFile }), 0, tracked.text());
    assert.match(tracked.text(), /the scenario is ready/);
    assert.doesNotMatch(tracked.text(), /S-02/);
    // The scenarios of a track's "before" steps are checked too (wave 4b verify round): here S-02, which is not ready.
    const before = capture();
    assert.equal(await check(parseArgs(['check', '--base', base, '--track', 'board-track']), before.out, { scenarioFile }), 1, before.text());
    assert.match(before.text(), /S-02 is not ready/);
    await assert.rejects(check(parseArgs(['check', '--base', base, '--track', 'nobody']), capture().out, { scenarioFile }), /No run or track nobody/);
  } finally {
    server.close();
    rmSync(dir, { recursive: true, force: true });
  }
});

// Wave 4b verify round: the pmo track plays S-03.3 off-screen first ("before"), so its check covers S-03 as well.
test('demo check --track pmo checks S-03, for its "before" step, as well as S-01, S-04, S-09, S-13, S-16 and S-17', async () => {
  const { server, base } = await fakeServer({ get_demo_freshness: () => fresh });
  try {
    const c = capture();
    await check(parseArgs(['check', '--base', base, '--track', 'pmo', '--json']), c.out);
    const checked = JSON.parse(c.lines.join('\n')).scenarios.map(s => s.id).sort();
    assert.deepEqual(checked, ['S-01', 'S-03', 'S-04', 'S-09', 'S-13', 'S-16', 'S-17']);
  } finally {
    server.close();
  }
});

test('demo check: an expired story request is NO GO and names demo refresh', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'kit-check-'));
  const scenarioFile = join(dir, 'scenarios.json');
  writeFileSync(scenarioFile, JSON.stringify({ description: 'test', personaCount: PERSONAS.length, scenarios: [{ id: 'S-01', title: 'x', personas: ['kai'], useCases: [], screens: [], preconditions: [{ label: 'always', pending: 'kai', field: 'queue', expect: { min: 0 } }] }] }));
  const { server, base } = await fakeServer({
    get_demo_freshness: () => ({ ok: true, data: { ...fresh.data, counts: { expired: 1, expiresSoon: 0 }, needsRefresh: true, items: [{ approvalRecordId: 'r1', state: 'expired', hoursLeft: -3, decision: { recordNumber: 'DEC-0201', statement: 'Old one' }, application: null, request: {} }] } }),
  });
  try {
    const c = capture();
    assert.equal(await check(parseArgs(['check', '--base', base]), c.out, { scenarioFile }), 1);
    assert.match(c.text(), /Story requests\s+FAIL\s+1 expired, 0 expire within 48 hours: run "demo refresh"/);
    assert.match(c.text(), /DEC-0201 Old one \(expired\)/);
  } finally {
    server.close();
    rmSync(dir, { recursive: true, force: true });
  }
});

// QA, fix round 1: the scenario whose approval precondition names the expired request's decision is NO GO, with the
// request; one that expires soon leaves its scenario GO, with a warning.
test('demo check: a scenario that stands on an expired story request is NO GO, and one on a request about to expire warns', () => {
  const pre = { label: 'asked', as: 'nadia', tool: 'list_approvals', input: { view: 'mine' }, where: { 'decision.statement': { contains: 'retire the old export' } } };
  const items = [
    { state: 'expires-soon', hoursLeft: 20, decision: { recordNumber: 'DEC-0300', statement: 'Something else' } },
    { state: 'expired', hoursLeft: -3, decision: { recordNumber: 'DEC-0206', statement: 'Authorize the change: Retire the old export flow' } },
  ];
  assert.deepEqual(storyRequestOf(pre, items), { state: 'expired', item: items[1] });
  assert.equal(storyRequestOf({ ...pre, where: { 'decision.statement': 'Something else' } }, items).state, 'soon');
  assert.equal(storyRequestOf({ ...pre, tool: 'list_documents' }, items), null);
  assert.equal(storyRequestOf(pre, []), null);
});

test('demo refresh: asks each expired or soon-expiring request again as the platform admin, and says what it renewed', async () => {
  let renewedIds = [];
  const item = (id, state, hours) => ({ approvalRecordId: id, state, hoursLeft: hours, decision: { recordNumber: 'DEC-0' + id, statement: 'Statement ' + id }, application: { businessApplicationId: 'a', name: 'Crew Roster' }, request: { askedRole: { key: 'change-board', label: 'Change board' }, askedOwnerRole: null, askedPerson: null, isConsultation: false } });
  const { server, calls, base } = await fakeServer({
    get_demo_freshness: () => {
      const items = [item('101', 'expired', -2), item('102', 'expires-soon', 20)].filter(i => !renewedIds.includes(i.approvalRecordId));
      return { ok: true, data: { ...fresh.data, counts: { expired: items.filter(i => i.state === 'expired').length, expiresSoon: items.filter(i => i.state !== 'expired').length }, needsRefresh: items.length > 0, items } };
    },
    renew_approval_request: input => {
      if (!input.validateOnly) renewedIds.push(input.approvalRecordId);
      return { ok: true, data: { approvalRecordId: input.approvalRecordId, renewed: true, request: { askedRole: { key: 'change-board', label: 'Change board' }, expiresOn: '2026-12-05T10:00:00Z', isConsultation: false } } };
    },
  });
  try {
    const dry = capture();
    assert.equal(await refresh(parseArgs(['refresh', '--base', base, '--dry-run']), dry.out), 0, dry.text());
    assert.match(dry.text(), /would ask again\s+DEC-0101 "Statement 101" on Crew Roster: asks the change board until 2026-12-05/);
    assert.deepEqual(renewedIds, []);
    assert.ok(calls.filter(c => c.tool === 'renew_approval_request').every(c => c.input.validateOnly === true && c.who === 'owen'));

    const c = capture();
    assert.equal(await refresh(parseArgs(['refresh', '--base', base, '--no-director']), c.out), 0, c.text());
    assert.deepEqual(renewedIds.sort(), ['101', '102']);
    assert.match(c.text(), /asked again\s+DEC-0101/);
    assert.match(c.text(), /Renewed: 2 asked again, 0 already asked again, 0 not asked again/);
    assert.match(c.text(), /The demo is fresh/);
  } finally {
    server.close();
  }
});

test('demo refresh: a request the engine will not ask again makes the run fail, with the engine\'s words', async () => {
  const { server, base } = await fakeServer({
    get_demo_freshness: () => ({ ok: true, data: { ...fresh.data, counts: { expired: 1, expiresSoon: 0 }, needsRefresh: true, items: [{ approvalRecordId: 'r9', state: 'expired', hoursLeft: -1, decision: { recordNumber: 'DEC-0900', statement: 'Old' }, application: null, request: {} }] } }),
    renew_approval_request: () => ({ ok: false, status: 409, error: { code: 'refused', message: 'Decision DEC-0900 is accepted, so its requests are not asked again.' } }),
  });
  try {
    const c = capture();
    assert.equal(await refresh(parseArgs(['refresh', '--base', base, '--no-director']), c.out), 1);
    assert.match(c.text(), /not asked again\s+DEC-0900 "Old": renew_approval_request: refused \(409\)\. Decision DEC-0900 is accepted/);
  } finally {
    server.close();
  }
});

test('a demo install needs the operator token, and dev mode needs a loopback URL', async () => {
  const c = capture();
  await assert.rejects(refresh(parseArgs(['refresh', '--base', 'https://demo.example.org']), c.out, { env: {} }), /PRODUCT_DEMO_TOKEN/);
  await assert.rejects(refresh(parseArgs(['refresh', '--base', 'https://demo.example.org', '--mode', 'dev']), c.out, { env: {} }), /loopback/);
});

// ---- demo sandbox rebuild: dry run only ----

test('sandbox rebuild --dry-run prints every step and command, with $NAME for each value, and runs and writes nothing', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'kit-sandbox-'));
  const folder = join(dir, 'never-created');
  const envFile = join(dir, 'env');
  writeFileSync(envFile, 'AZURE_RESOURCE_GROUP="rg-not-to-print"\nAZURE_SQL_SERVER_NAME="sql-not-to-print"\nPRODUCT_DEMO="true"\n');
  try {
    const c = capture();
    assert.equal(await sandbox(parseArgs(['sandbox', 'rebuild', '--dry-run', '--folder', folder, '--env-file', envFile]), c.out), 0);
    const text = c.text();
    for (const [i, step] of STEPS.entries()) assert.match(text, new RegExp('\\n' + (i + 1) + '\\. ' + step + ': '), step);
    assert.match(text, /\$ az sql db delete --resource-group "\$AZURE_RESOURCE_GROUP" --server "\$AZURE_SQL_SERVER_NAME" --name "\$AZURE_SQL_DATABASE_NAME" --yes/);
    assert.match(text, /\$ azd env set PRODUCT_DEMO_PERSONAS true/);
    assert.match(text, /\$ azd provision --no-prompt --no-state/);
    assert.match(text, /\$ azd deploy --no-prompt/);
    assert.match(text, /seed --environment Demo --preset balanced/);
    assert.match(text, /ConnectionStrings__Product="Server=tcp:\$AZURE_SQL_SERVER_FQDN,1433;Database=\$AZURE_SQL_DATABASE_NAME;/);
    assert.match(text, /demo-director\.mjs --base http:\/\/127\.0\.0\.1:5099 --timeout 240/);
    assert.match(text, /demo\.mjs check --base http:\/\/127\.0\.0\.1:5099/);
    assert.match(text, /would write \d+ roles in \d+ bindings, and \d+ rows/);
    assert.match(text, /Dry run done: nothing ran\./);
    assert.ok(!text.includes('rg-not-to-print') && !text.includes('sql-not-to-print'), 'no value of the install is printed');
    assert.ok(!existsSync(folder), 'a dry run writes no file');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// QA, wave 4b: the files step and the seed command printed the full checkout path on a shared screen.
test('sandbox rebuild --dry-run prints no absolute path, with the default folder or one outside the source tree', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'kit-sandbox-'));
  const envFile = join(dir, 'env');
  writeFileSync(envFile, 'PRODUCT_DEMO="true"\n');
  const extraBinding = join(dir, 'extra-binding.json');
  const extraRows = join(dir, 'extra-rows.json');
  writeFileSync(extraBinding, JSON.stringify({ bindingOperations: [] }));
  writeFileSync(extraRows, JSON.stringify({ rows: [] }));
  const absolute = text => {
    assert.doesNotMatch(text, /(^|[\s"'(=])[A-Za-z]:[\\/]/, 'no drive letter');
    assert.ok(!text.includes('/root/'), 'no /root/ path');
    for (const p of [SRC, toWslPath(SRC), dir, tmpdir(), homedir()]) assert.ok(!text.includes(p), 'no ' + p);
  };
  try {
    const inside = capture();
    assert.equal(await sandbox(parseArgs(['sandbox', 'rebuild', '--dry-run', '--env-file', envFile]), inside.out), 0);
    assert.match(inside.text(), /write \.azure\/demo-kit\/sandbox-binding\.patch\.json and \.azure\/demo-kit\/sandbox-world\.json from seed\/demo-binding\.patch\.json and seed\/demo-estate\.json/);
    assert.match(inside.text(), /--binding \.azure\/demo-kit\/sandbox-binding\.patch\.json --rows \.azure\/demo-kit\/sandbox-world\.json/);
    absolute(inside.text());
    const outside = capture();
    assert.equal(await sandbox(parseArgs(['sandbox', 'rebuild', '--dry-run', '--env-file', envFile, '--folder', join(dir, 'work'), '--extra-binding', extraBinding, '--extra-rows', extraRows]), outside.out), 0);
    assert.match(outside.text(), /write <folder>\/sandbox-binding\.patch\.json and <folder>\/sandbox-world\.json from .* with the bindings of the --extra-binding file, with the rows of the --extra-rows file/);
    assert.match(outside.text(), /--binding <folder>\/sandbox-binding\.patch\.json --rows <folder>\/sandbox-world\.json/);
    absolute(outside.text());
    assert.equal(shownPath(join(SRC, 'adapters', 'azure', 'seed', 'x.json'), 'fallback'), 'seed/x.json');
    assert.equal(shownPath(join(dir, 'x.json'), 'fallback'), 'fallback');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('sandbox rebuild refuses any real run without --yes, and an unknown step', async () => {
  const c = capture();
  await assert.rejects(sandbox(parseArgs(['sandbox', 'rebuild']), c.out), /Pass --yes/);
  await assert.rejects(sandbox(parseArgs(['sandbox', 'rebuild', '--steps', 'seed,director']), c.out), /Pass --yes/);
  await assert.rejects(sandbox(parseArgs(['sandbox', 'rebuild', '--dry-run', '--steps', 'files,nuke']), c.out), /Unknown step "nuke"/);
  assert.deepEqual(parseEnvValues('A="1"\nB=two\n# note\n'), { A: '1', B: 'two' });
});

// Security review, wave 4: an install that is not a demo stops before the first step, whatever --steps says, and no command starts.
test('sandbox rebuild on an install whose PRODUCT_DEMO is not true runs no command, with or without the reset', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'kit-sandbox-'));
  const envFile = join(dir, 'env');
  writeFileSync(envFile, 'AZURE_RESOURCE_GROUP="rg-prod"\nAZURE_SQL_SERVER_NAME="sql-prod"\nAZURE_SQL_DATABASE_NAME="db-prod"\nPRODUCT_DEMO="false"\n');
  const started = [];
  const runner = {
    spawnSync: (command, args) => { started.push([command, ...args].join(' ')); return { status: 0, stdout: '', stderr: '' }; },
    spawn: (command, args) => { started.push([command, ...args].join(' ')); throw new Error('no process may start'); },
  };
  try {
    for (const steps of [null, 'reset,provision', 'seed,director']) {
      const c = capture();
      const argv = ['sandbox', 'rebuild', '--yes', '--folder', join(dir, 'work'), '--env-file', envFile, ...(steps ? ['--steps', steps] : [])];
      assert.equal(await sandbox(parseArgs(argv), c.out, runner), 1, steps ?? 'every step');
      assert.match(c.text(), /PRODUCT_DEMO is not true in the azd environment, so this is not a demo install\. The rebuild stops before its first step, and nothing ran\./);
    }
    assert.deepEqual(started, []);
    const dry = capture();
    assert.equal(await sandbox(parseArgs(['sandbox', 'rebuild', '--dry-run', '--folder', join(dir, 'work'), '--env-file', envFile]), dry.out, runner), 0);
    assert.match(dry.text(), /0\. demo only: stop unless PRODUCT_DEMO is true[\s\S]*a real run would stop here/);
    assert.deepEqual(started, []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---- demo play: the end-to-end suite in play mode ----

test('demo play of a run or a track plays its steps in order, one Playwright run per scenario, each with its steps only', async () => {
  const { runGroups, stepGrep } = await import('../lib/play.mjs');
  const file = readScenarios();
  for (const [name, run] of Object.entries(file.runs)) {
    const groups = runGroups(run, file.scenarios);
    assert.deepEqual(groups.flatMap(g => g.steps), run.steps, name);
    for (const g of groups) assert.ok(g.steps.every(s => s.startsWith(g.id + '.')), name);
  }
  const re = new RegExp(stepGrep(['S-09.2', 'S-09.9']));
  assert.ok(re.test('S-09 Demand S-09.9 Propose retirement complete'));
  assert.ok(!re.test('S-09 Demand S-09.10 The archive answer'));
  assert.ok(file.runs['run-15'].steps.includes('S-09.9'), 'the 15-minute run proposes retirement complete before S-09.11 answers it');
});

test('demo play names the suite, the spec, the project and the play-mode variables, and stops while the suite is not installed', async () => {
  const plan = playPlan({ id: 'S-04', spec: 'specs/S-04-business-owner-approvals.spec.ts' }, { base: 'http://127.0.0.1:5086', record: true });
  assert.ok(plan.args.includes('--headed') && plan.args.includes('specs/S-04-business-owner-approvals.spec.ts'));
  assert.ok(plan.args.includes('--project=desktop-1280'));
  assert.equal(plan.env.DEMO_PLAY, '1');
  assert.equal(plan.env.DEMO_CAPTIONS, '1');
  assert.equal(plan.env.DEMO_RECORD, '1');
  assert.equal(plan.env.E2E_BASE_URL, 'http://127.0.0.1:5086');
  assert.equal(plan.env.PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD, '1');
  const phone = playPlan({ id: 'S-04', spec: 'specs/S-04-business-owner-approvals.spec.ts' }, { base: 'http://127.0.0.1:5086', phone: true, readOnly: true });
  assert.ok(phone.args.includes('--project=phone-390'));
  assert.equal(phone.env.E2E_READ_ONLY, '1');
  assert.equal(phone.env.DEMO_RECORD, undefined);
  // Every scenario names its spec, and each spec is in the suite.
  for (const s of readScenarios().scenarios) assert.ok(existsSync(join(SRC, 'tests', 'e2e', s.spec)), `${s.id}: ${s.spec} is in src/tests/e2e`);
  if (!plan.ready) {
    const c = capture();
    assert.equal(await play(parseArgs(['play', 'S-01', '--base', 'http://127.0.0.1:5086']), c.out), 2);
    assert.match(c.text(), /the end-to-end suite is not installed/);
  }
});

// ---- demo local age-request and fail-card: rehearsal and test aids on a local run (wave 4b QA) ----

const sqlite = await (async () => {
  const emit = process.emitWarning;
  process.emitWarning = (w, ...rest) => (/SQLite/i.test(String(w?.message ?? w)) ? undefined : emit.call(process, w, ...rest));
  try {
    return await import('node:sqlite');
  } catch {
    return null;
  } finally {
    process.emitWarning = emit;
  }
})();

const NOW = Date.parse('2026-11-25T12:00:00Z');
const DAY = 24 * 3600 * 1000;
const ID = n => '00000000-0000-4000-8000-' + String(n).padStart(12, '0');

// A store with the two tables the commands touch: a proposed decision with two timed requests, an accepted decision,
// and a proposed decision whose request has no expiry; one outbox row per pending request.
function rehearsalFolder() {
  const dir = mkdtempSync(join(tmpdir(), 'kit-rehearse-'));
  mkdirSync(join(dir, 'data'));
  const file = join(dir, 'data', 'demo.db');
  const db = new sqlite.DatabaseSync(file);
  db.exec('CREATE TABLE "Row" ("Entity" TEXT NOT NULL, "Id" TEXT NOT NULL, "Key" TEXT NULL, "Columns" TEXT NOT NULL, "Version" INTEGER NOT NULL, "UniqueKey" TEXT NULL, "CreatedOn" TEXT NOT NULL, "ModifiedOn" TEXT NOT NULL, PRIMARY KEY ("Entity", "Id")) WITHOUT ROWID');
  db.exec('CREATE TABLE "ApprovalOutbox" ("ApprovalRecordId" TEXT NOT NULL PRIMARY KEY, "DecisionId" TEXT NOT NULL, "EnqueuedOn" TEXT NOT NULL, "State" TEXT NOT NULL, "ClaimId" TEXT NULL, "ClaimedUntil" TEXT NULL, "Attempts" INTEGER NOT NULL, "CardRef" TEXT NULL, "RunnerRunUrl" TEXT NULL, "UpdatedOn" TEXT NOT NULL, "RowVersion" INTEGER NOT NULL) WITHOUT ROWID');
  const row = db.prepare('INSERT INTO "Row" VALUES (?, ?, NULL, ?, 1, NULL, ?, ?)');
  const on = new Date(NOW - DAY).toISOString();
  row.run('decision', ID(4206), JSON.stringify({ recordNumber: 'DEC-0206', statement: 'Authorize the change: Retire the old export flow', statusMeaning: 'proposed' }), on, on);
  row.run('decision', ID(4101), JSON.stringify({ recordNumber: 'DEC-0101', statement: 'Go live for all field regions.', statusMeaning: 'accepted' }), on, on);
  row.run('decision', ID(4219), JSON.stringify({ recordNumber: 'DEC-0219', statement: 'Agree the assist level', statusMeaning: 'proposed' }), on, on);
  const request = (n, decision, extra) => row.run('approval-record', ID(n), JSON.stringify({ decision: ID(decision), outcome: 'approval-pending', outcomeMeaning: 'pending', requestedOn: on, ...extra }), on, on);
  request(5901, 4206, { requiredOwnerRole: 'business-owner', expiresOn: engineTime(new Date(NOW + 4 * DAY)) });
  request(5902, 4206, { requiredRole: 'change-manager', expiresOn: engineTime(new Date(NOW + 4 * DAY)), requestedOn: new Date(NOW).toISOString() });
  request(5903, 4219, { requiredRole: 'security-reviewer' });
  const box = db.prepare('INSERT INTO "ApprovalOutbox" VALUES (?, ?, ?, ?, NULL, NULL, 0, NULL, NULL, ?, 1)');
  for (const [n, d] of [[5901, 4206], [5902, 4206], [5903, 4219]]) box.run(ID(n), ID(d), engineTime(new Date(NOW - DAY)), 'pending', engineTime(new Date(NOW - DAY)));
  db.close();
  return { dir, file };
}

function configServer(config) {
  const server = createServer((req, res) => {
    res.writeHead(req.url === '/app/config.json' ? 200 : 404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(req.url === '/app/config.json' ? config : {}));
  });
  return new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve({ server, base: 'http://127.0.0.1:' + server.address().port })));
}

const LOCAL_RUN = { schemaVersion: 1, auth: { mode: 'development' }, demo: true, development: { personas: [] }, demoPersonas: [] };
const DEMO_INSTALL = { schemaVersion: 1, auth: { mode: 'entra' }, demo: true, development: { personas: [] }, demoPersonas: [{ name: 'owen', label: 'Owen Achterberg, platform admin' }] };

function readStore(file) {
  const db = new sqlite.DatabaseSync(file, { readOnly: true });
  try {
    const rows = Object.fromEntries(db.prepare('SELECT "Id", "Columns", "Version" FROM "Row" WHERE "Entity" = \'approval-record\'').all().map(r => [r.Id, { ...JSON.parse(r.Columns), version: r.Version }]));
    const outbox = Object.fromEntries(db.prepare('SELECT * FROM "ApprovalOutbox"').all().map(o => [o.ApprovalRecordId, { ...o }]));
    return { rows, outbox };
  } finally {
    db.close();
  }
}

test('decision numbers and engine times read the way the store keeps them', () => {
  assert.equal(decisionNumber('dec-0206'), 'DEC-0206');
  assert.equal(decisionNumber('206'), 'DEC-0206');
  assert.throws(() => decisionNumber('the old export'), UsageError);
  assert.throws(() => decisionNumber(undefined), UsageError);
  assert.equal(engineTime(new Date('2026-11-27T04:00:00.123Z')), '2026-11-27T04:00:00.1230000Z');
});

test('local age-request moves one pending request into its last 48 hours, and fail-card fails its card, on a local run', { skip: !sqlite && 'needs node:sqlite (Node.js 22.13 or later)' }, async () => {
  const { dir, file } = rehearsalFolder();
  const { server, base } = await configServer(LOCAL_RUN);
  writeFileSync(join(dir, 'state.json'), JSON.stringify({ port: Number(new URL(base).port), pid: 0, base, db: file }));
  const run = (argv, out) => local(parseArgs(['local', ...argv, '--native', '--folder', dir]), out, { now: NOW });
  try {
    const aged = capture();
    assert.equal(await run(['age-request', '--decision', 'DEC-0206'], aged.out), 0, aged.text());
    assert.match(aged.text(), /DEC-0206 "Authorize the change: Retire the old export flow"/);
    assert.match(aged.text(), /The request that asks the owner role business-owner now expires 2026-11-27 04:00 UTC, in 40 hours/);
    assert.match(aged.text(), /DEC-0206 has 2 pending requests; this is the oldest/);
    assert.match(aged.text(), /the audit trail does not show this change/);
    let store = readStore(file);
    assert.equal(store.rows[ID(5901)].expiresOn, '2026-11-27T04:00:00.0000000Z');
    assert.equal(store.rows[ID(5901)].version, 2, 'the row version moves on');
    assert.equal(store.rows[ID(5902)].expiresOn, engineTime(new Date(NOW + 4 * DAY)), 'the other request is left as it is');

    const picked = capture();
    assert.equal(await run(['age-request', '--decision', '206', '--role', 'change-manager', '--hours', '2', '--json'], picked.out), 0, picked.text());
    const result = JSON.parse(picked.text());
    assert.equal(result.approvalRecordId, ID(5902));
    assert.equal(result.expiresOn, '2026-11-25T14:00:00.0000000Z');
    assert.equal(result.serverAnswered, true);
    await assert.rejects(run(['age-request', '--decision', 'DEC-0206', '--hours', '48'], capture().out), /--hours takes a whole number from 1 to 47/);
    await assert.rejects(run(['age-request', '--decision', 'DEC-0101'], capture().out), /DEC-0101 is accepted/);
    await assert.rejects(run(['age-request', '--decision', 'DEC-0219'], capture().out), /have no expiry/);
    await assert.rejects(run(['age-request', '--decision', 'DEC-0999'], capture().out), /no decision DEC-0999/);
    await assert.rejects(run(['age-request', '--decision', 'DEC-0206', '--role', 'auditor'], capture().out), /no pending request that asks auditor/);

    const failed = capture();
    assert.equal(await run(['fail-card', '--decision', 'DEC-0206'], failed.out), 0, failed.text());
    assert.match(failed.text(), /The card of the request that asks the owner role business-owner is failed now, after 3 attempts \(it was pending\)/);
    assert.match(failed.text(), /"Send again"/);
    store = readStore(file);
    assert.equal(store.outbox[ID(5901)].State, 'failed');
    assert.equal(store.outbox[ID(5901)].Attempts, 3);
    assert.equal(store.outbox[ID(5901)].RowVersion, 2);
    assert.equal(store.outbox[ID(5901)].UpdatedOn, '2026-11-25T12:00:00.0000000Z');
    assert.equal(store.outbox[ID(5902)].State, 'pending', 'the other card is left as it is');
    const again = capture();
    assert.equal(await run(['fail-card', '--decision', 'DEC-0206'], again.out), 0);
    assert.match(again.text(), /had failed already\. Nothing changed/);
    assert.equal(readStore(file).outbox[ID(5901)].RowVersion, 2);
  } finally {
    server.close();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('local age-request and fail-card refuse a demo install, a non-loopback base and --mode demo, and change nothing', { skip: !sqlite && 'needs node:sqlite (Node.js 22.13 or later)' }, async () => {
  const { dir, file } = rehearsalFolder();
  const demo = await configServer(DEMO_INSTALL);
  writeFileSync(join(dir, 'state.json'), JSON.stringify({ port: Number(new URL(demo.base).port), pid: 0, base: demo.base, db: file }));
  const run = argv => local(parseArgs(['local', ...argv, '--native', '--folder', dir]), capture().out, { now: NOW });
  const before = readStore(file);
  try {
    for (const verb of ['age-request', 'fail-card']) {
      await assert.rejects(run([verb, '--decision', 'DEC-0206']), /local run only.*is a demo install/);
      await assert.rejects(run([verb, '--decision', 'DEC-0206', '--base', 'https://demo.example.org']), /local run only.*not a local server on a loopback http URL/);
      await assert.rejects(run([verb, '--decision', 'DEC-0206', '--mode', 'demo']), /local run only.*--mode demo/);
      await assert.rejects(run([verb, '--decision', 'DEC-0206', '--base', 'http://127.0.0.1:1']), /is not the server this folder started/);
    }
    assert.deepEqual(readStore(file), before, 'the store is unchanged');
    rmSync(join(dir, 'state.json'));
    await assert.rejects(run(['fail-card', '--decision', 'DEC-0206']), /has no state\.json/);
  } finally {
    demo.server.close();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the kit holds no package manifest: the non-browser commands need no packages', () => {
  const kit = join(SRC, 'demo', 'kit');
  assert.ok(!readdirSync(kit).some(f => f === 'package.json' || f === 'node_modules'));
});
