#!/usr/bin/env node
// Check runner for src/. The same command runs on a developer machine and in the public repository's CI workflow
// (.github/workflows/ci.yml runs "node tools/ci.mjs --no-scrub" from src/).
// Checks, in order: tool packages, restore, build, offline tests, test, Azure adapter, preset build, fixtures, replay,
// dash check, scrub gate, JSON check, model check, content check, UI harness packages, pipeline record step samples,
// demo kit, end-to-end suite (packages and specs, no browser), product docs, model generators.
// Every check runs. Then a summary prints, and the exit code is 1 if any check failed.
//
// Usage: node tools/ci.mjs [--configuration Release] [--skip <name,name>] [--no-scrub]
//   --skip       skip checks by name: packages, restore, build, offline, test, azure, presets, fixtures, replay, dash,
//                scrub, json, model, content, ui, samples, kit, e2e, docs, generated
//   --no-scrub   for a copy of src/ without the workspace scrub script, such as the public repository. The content
//                check then also runs with --no-sources, because content/SOURCES.md is not published either
//
// No sign-in and no tenant. Package restore uses the public nuget.org and npmjs feeds only.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { SRC, walk, option } from './lib/files.mjs';

const args = process.argv.slice(2);
const configuration = option(args, '--configuration', 'Release');
const skip = new Set(String(option(args, '--skip', '')).split(',').map(s => s.trim()).filter(Boolean));
const noScrub = args.includes('--no-scrub');

const TOOLS = path.join(SRC, 'tools');
const SOLUTION = 'Product.sln';
// The scrub script and its terms live outside src/ and are not published.
const SCRUB_SCRIPT = path.resolve(SRC, '..', 'scripts', 'scrub-check.cjs');

// No telemetry from the .NET tools during checks.
const env = {
  ...process.env,
  DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  DOTNET_NOLOGO: '1',
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
  TESTINGPLATFORM_TELEMETRY_OPTOUT: '1',
};

const results = [];

function run(command, commandArgs, cwd, useShell = false) {
  // Arguments are fixed in this file. A shell is used only for npm on Windows, where npm is a .cmd file.
  const r = spawnSync(command, commandArgs, { cwd, env, stdio: 'inherit', shell: useShell });
  if (r.error) console.error(`  cannot start ${command}: ${r.error.message}`);
  return r.status === null ? 1 : r.status;
}

function check(name, title, fn, blockedBy = []) {
  if (skip.has(name)) { results.push({ name, title, status: 'SKIP', note: 'skipped by --skip' }); return; }
  const failedDep = blockedBy.find(dep => results.find(r => r.name === dep && r.status === 'FAIL'));
  if (failedDep) { results.push({ name, title, status: 'SKIP', note: `${failedDep} failed` }); return; }
  console.log(`\n=== ${title} ===`);
  const started = Date.now();
  let status;
  let note = '';
  try {
    const out = fn();
    status = out.code === 0 ? 'PASS' : out.code === 'skip' ? 'SKIP' : 'FAIL';
    note = out.note || '';
  } catch (e) {
    status = 'FAIL';
    note = e.message;
  }
  results.push({ name, title, status, note, seconds: ((Date.now() - started) / 1000).toFixed(1) });
}

// ---- checks ----

check('packages', 'Tool packages (npm ci in tools/)', () => {
  const lock = path.join(TOOLS, 'package-lock.json');
  const installed = path.join(TOOLS, 'node_modules', '.package-lock.json');
  if (fs.existsSync(installed) && fs.statSync(installed).mtimeMs >= fs.statSync(lock).mtimeMs) {
    console.log('  already installed from package-lock.json');
    return { code: 0, note: 'already installed' };
  }
  const code = run('npm', ['ci', '--no-audit', '--no-fund', '--ignore-scripts'], TOOLS, process.platform === 'win32');
  return { code, note: 'npm ci' };
});

check('restore', 'dotnet restore', () => ({ code: run('dotnet', ['restore', SOLUTION], SRC) }));

check('build', `dotnet build (${configuration})`,
  () => ({ code: run('dotnet', ['build', SOLUTION, '-c', configuration, '--no-restore'], SRC) }),
  ['restore']);

// Directory.Build.targets removes the test platform telemetry hook. Proves it from the generated code under root.
function telemetryLeaks(root) {
  const generated = [];
  const find = dir => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (e.name === 'node_modules' || e.name === '.git') continue;
      const full = path.join(dir, e.name);
      if (e.isDirectory()) find(full);
      else if (e.name === 'SelfRegisteredExtensions.cs' && full.includes(`${path.sep}obj${path.sep}`)) generated.push(full);
    }
  };
  find(root);
  const leaks = generated.filter(f => fs.readFileSync(f, 'utf8').includes('Extensions.Telemetry'));
  for (const f of leaks) console.error(`  telemetry extension registered in ${path.relative(SRC, f)}`);
  console.log(`  ${generated.length} generated registration file(s) checked`);
  return { files: generated.length, leaks: leaks.length };
}

check('offline', 'Offline tests (no telemetry extension)', () => {
  const { files, leaks } = telemetryLeaks(SRC);
  return { code: leaks ? 1 : 0, note: `${files} registration files` };
}, ['build']);

// The net48 test targets need .NET Framework, so they run only on Windows. Elsewhere (Linux, including WSL) the check runs
// the net10.0 targets and says so; the Windows CI job runs both.
check('test', `dotnet test (${configuration})`,
  () => process.platform === 'win32'
    ? { code: run('dotnet', ['test', '--solution', SOLUTION, '-c', configuration, '--no-build'], SRC) }
    : { code: run('dotnet', ['test', '--solution', SOLUTION, '-c', configuration, '--no-build', '-f', 'net10.0'], SRC), note: 'net10.0 only; net48 runs on Windows' },
  ['restore', 'build', 'offline']);

// The Azure adapter has its own solution, adapters/azure/Product.Azure.sln: its hosts, stores and two test projects,
// plus the engine and MCP projects it builds on. The check builds that solution as the adapter's README does, proves
// its test projects stay offline too, and then runs only the adapter's two test projects, because the engine and MCP
// suites already ran in the test check. The Azure SQL store tests skip unless PRODUCT_TEST_SQLSERVER names an empty test
// database, so the check needs no secret, no database and no sign-in.
const AZURE = path.join(SRC, 'adapters', 'azure');
const AZURE_TESTS = ['tests/Product.Azure.Tests', 'tests/Product.Azure.Stores.Tests'];
check('azure', `Azure adapter: build and test (${configuration})`, () => {
  let code = run('dotnet', ['build', 'Product.Azure.sln', '-c', configuration], AZURE);
  if (code !== 0) return { code, note: 'build failed' };
  const { files, leaks } = telemetryLeaks(AZURE);
  if (leaks) return { code: 1, note: `telemetry extension registered in ${leaks} of ${files} files` };
  for (const project of AZURE_TESTS) {
    code = run('dotnet', ['test', '--project', project, '-c', configuration, '--no-build'], AZURE);
    if (code !== 0) return { code, note: `${project} failed` };
  }
  return { code: 0, note: `${AZURE_TESTS.length} test projects` };
}, ['build']);

// The engine command line. It reads files only.
const CLI = path.join(SRC, 'engine', 'Product.Engine.Cli', 'bin', configuration, 'net10.0', 'Product.Engine.Cli.dll');

check('presets', 'Preset build and validation (OP-09)',
  () => ({ code: run('dotnet', [CLI, 'build-presets'], SRC) }),
  ['build']);

check('fixtures', 'Engine fixtures',
  () => ({ code: run('dotnet', [CLI, 'run-fixtures', 'fixtures'], SRC) }),
  ['build']);

// The replay tool (document 25, section 9): every record under fixtures/replay replays against its snapshot
// version with exactly the differences it expects (none for a true record).
check('replay', 'Engine replay records',
  () => ({ code: run('dotnet', [CLI, 'replay', path.join('fixtures', 'replay')], SRC) }),
  ['build']);

check('dash', 'Dash check (no U+2013, no U+2014)',
  () => ({ code: run(process.execPath, [path.join(TOOLS, 'dash-check.mjs')], SRC) }));

check('scrub', 'Scrub gate (source files only)', () => {
  if (!fs.existsSync(SCRUB_SCRIPT)) {
    if (noScrub) return { code: 'skip', note: 'no scrub script here (--no-scrub)' };
    console.error(`  scrub script not found: ${SCRUB_SCRIPT}`);
    return { code: 1, note: 'scrub script not found' };
  }
  // Third-party DLLs in bin/ hold words that match blocked terms (for example localized resource strings).
  // Build output never leaves this machine, so the gate scans a copy of the source files only.
  const staging = fs.mkdtempSync(path.join(os.tmpdir(), 'src-scrub-'));
  try {
    let count = 0;
    for (const { full, rel } of walk(SRC)) {
      const target = path.join(staging, rel);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.copyFileSync(full, target);
      count++;
    }
    console.log(`  ${count} source files copied for the scan (bin, obj and node_modules left out)`);
    return { code: run(process.execPath, [SCRUB_SCRIPT, '--root', staging], SRC), note: `${count} source files` };
  } finally {
    fs.rmSync(staging, { recursive: true, force: true });
  }
});

check('json', 'JSON check (parse, schemas, schema map)',
  () => ({ code: run(process.execPath, [path.join(TOOLS, 'json-check.mjs')], SRC) }),
  ['packages']);
check('model', 'Model check (logical model)', () => ({ code: run(process.execPath, [path.join(SRC, 'model', 'check-model.mjs')], SRC) }), ['packages']);
check('content', 'Content check (playbook and guidelines)',
  () => ({ code: run(process.execPath, [path.join(TOOLS, 'content-check.mjs'), ...(noScrub ? ['--no-sources'] : [])], SRC) }));
// The page harness: exact versions, registry sources, licenses, and THIRD-PARTY.md against the lock file.
// It reads files only. Building the harness and its screenshots stays a separate step (ui/README.md).
check('ui', 'UI harness packages',
  () => ({ code: run(process.execPath, [path.join(SRC, 'ui', 'harness', 'scripts', 'check-packages.mjs')], SRC) }));
// The pipeline record step samples: the record step script against a local stub that validates every
// request against the record endpoint contract. It listens on 127.0.0.1 only. No sign-in, no tenant.
check('samples', 'Pipeline record step samples',
  () => ({ code: run(process.execPath, [path.join(SRC, 'samples', 'pipeline-steps', 'test', 'record-steps.test.mjs')], SRC) }),
  ['packages']);
// The demo kit (demo/kit/README.md): its own tests, offline, against a small fake server on 127.0.0.1. No packages, no
// product server, no WSL and no Azure: the sandbox rebuild runs only as a dry run there.
check('kit', 'Demo kit tests',
  () => ({ code: run(process.execPath, ['--test', path.join('demo', 'kit', 'test', 'kit.test.mjs')], SRC) }));
// The end-to-end suite (tests/e2e/README.md), without a browser or a server: its packages by the same rules as the web
// app's (exact versions, registry sources, licenses, THIRD-PARTY.md), its specs against the presenter scripts (one
// test per step, same IDs, titles and order), and every count a script's proof names checked by its step's test
// (check-scripts.mjs, product review, fix round 2), and every write tool the web app calls played in a presenter step whose
// test performs it, with its row in the catalog's table of write actions (check-actions.mjs, wave 4b). It reads files
// only. Running the suite needs a browser and a server: on
// Windows, "node run.mjs" in tests/e2e (docs/testing.md, "The end-to-end tests").
check('e2e', 'End-to-end suite (packages, specs against the scripts, counts, write actions)', () => {
  const packages = run(process.execPath, [path.join(SRC, 'ui', 'harness', 'scripts', 'check-packages.mjs'), '--package', 'tests/e2e'], SRC);
  const suite = run(process.execPath, [path.join(SRC, 'tests', 'e2e', 'tools', 'check-suite.mjs')], SRC);
  const counts = run(process.execPath, [path.join(SRC, 'tests', 'e2e', 'tools', 'check-scripts.mjs')], SRC);
  const actions = run(process.execPath, [path.join(SRC, 'tests', 'e2e', 'tools', 'check-actions.mjs')], SRC);
  return { code: packages || suite || counts || actions };
});
// The product documentation (docs/README.md): its generated parts (the tool list, the error codes, the roles and what
// each may call) match a fresh run from the contracts and the shipped configuration, and every link in docs/ resolves
// inside src/. It reads files only; "node tools/docs-gen.mjs" writes the generated parts.
check('docs', 'Product docs (generated parts, links)',
  () => ({ code: run(process.execPath, [path.join(TOOLS, 'docs-gen.mjs'), '--check'], SRC) }));
// The model generators: two runs give the same bytes, and model/generated/ matches a fresh run. The outputs are
// drafts (not applied; D-26 chose Azure SQL for the first release). Nothing is applied to any database or environment.
// No packages.
check('generated', 'Model generators (deterministic)',
  () => ({ code: run(process.execPath, [path.join(SRC, 'model', 'check-generated.mjs')], SRC) }));

// ---- summary ----
console.log('\n=== Summary ===');
for (const r of results) {
  const time = r.seconds ? `${r.seconds}s` : '';
  console.log(`  ${r.status.padEnd(4)}  ${r.title.padEnd(42)} ${time.padStart(7)}  ${r.note}`);
}
const failed = results.filter(r => r.status === 'FAIL');
console.log(failed.length ? `\nCI: ${failed.length} check(s) failed.` : '\nCI: all checks passed.');
process.exit(failed.length ? 1 : 0);
