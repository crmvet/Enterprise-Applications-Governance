#!/usr/bin/env node
// The end-to-end run (docs/testing.md, "The end-to-end tests"): every scenario on the desktop (1280 px) and the phone
// (390 px) project, one project at a time, into one run folder, then one HTML report and one summary.json.
//
// A writing run changes the store, so each project needs a fresh store: before the second project the runner runs the
// reset command (--reset or E2E_RESET_COMMAND), such as the sandbox's own reset of a local demo store. Without one it
// plays the first project only and says so. A read-only run (--read-only) plays both projects on the same store.
//
// Usage: node run.mjs [--base <url>] [--read-only] [--projects desktop-1280,phone-390] [--reset "<command>"]
//                     [--kit-folder <folder>] [--run <name>] [<spec filter> ...] [-- <more Playwright options>]
//   --base       the web app or the server, such as http://127.0.0.1:5083/app/ (default E2E_BASE_URL, then port 5083)
//   --read-only  skip every write (E2E_READ_ONLY=1): for a deployed Demo install or a store that must not change
//   --projects   which projects, in order (default both)
//   --reset      a shell command that gives the server a fresh demo store again, run before each project after the first
//   --kit-folder the demo kit's --folder of the local run (E2E_KIT_FOLDER), such as /root/eag-e2e: the steps that need
//                a rehearsal aid ("demo local fail-card") run it there. A writing run against a local server needs it
//   --run        the run folder's name under runs/ (default: the start time)
// Exit code: 0 when every step passed, 1 when one failed or a writing run skipped a write, 2 for bad usage or setup.
// A writing run against a local server performs every write: a skipped write there means an earlier step left out the
// state it needs, so the run fails and lists each one (wave 4b QA).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.dirname(fileURLToPath(import.meta.url));
const CLI = path.join(ROOT, 'node_modules', '@playwright', 'test', 'cli.js');
const PROJECTS = ['desktop-1280', 'phone-390'];

const argv = process.argv.slice(2);
const dashes = argv.indexOf('--');
const own = dashes >= 0 ? argv.slice(0, dashes) : argv;
const passOn = dashes >= 0 ? argv.slice(dashes + 1) : [];
const opt = name => {
  const i = own.indexOf(name);
  return i >= 0 && i + 1 < own.length ? own[i + 1] : null;
};
const usage = message => {
  console.error(`e2e run: ${message}`);
  process.exit(2);
};

const valued = new Set(['--base', '--projects', '--reset', '--run', '--kit-folder']);
const filters = own.filter((a, i) => !a.startsWith('--') && !valued.has(own[i - 1]));
const readOnly = own.includes('--read-only') || /^(1|true|yes)$/i.test(process.env.E2E_READ_ONLY ?? '');
const projects = (opt('--projects') ?? PROJECTS.join(',')).split(',').map(s => s.trim()).filter(Boolean);
for (const p of projects) if (!PROJECTS.includes(p)) usage(`no project ${p}; the projects are ${PROJECTS.join(' and ')}.`);
const reset = opt('--reset') ?? process.env.E2E_RESET_COMMAND ?? '';
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15);
const runId = opt('--run') ?? process.env.E2E_RUN_ID ?? stamp;
if (!/^[A-Za-z0-9._-]+$/.test(runId)) usage('--run takes letters, digits, dots, dashes and underscores only.');
if (!fs.existsSync(CLI)) usage('Playwright is not installed: run "npm ci --ignore-scripts" in src/tests/e2e first, with PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1.');
const baseUrl = opt('--base') ?? process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5083/app/';
let localRun = false;
try {
  localRun = (process.env.E2E_MODE ?? '') !== 'demo' && ['127.0.0.1', 'localhost', '[::1]'].includes(new URL(baseUrl).hostname);
} catch {
  usage(`--base is not a URL: ${baseUrl}`);
}
const kitFolder = opt('--kit-folder') ?? process.env.E2E_KIT_FOLDER ?? '';
if (localRun && !readOnly && !kitFolder) usage("a writing run against a local server needs --kit-folder <folder> (E2E_KIT_FOLDER), the folder the kit runs the server in: S-23.28 makes a failed approval card with the kit there. Or run with --read-only.");

const runDir = path.join(ROOT, 'runs', runId);
const env = {
  ...process.env,
  E2E_RUN_ID: runId,
  PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1',
  ...(opt('--base') ? { E2E_BASE_URL: opt('--base') } : {}),
  ...(readOnly ? { E2E_READ_ONLY: '1' } : {}),
  ...(opt('--kit-folder') ? { E2E_KIT_FOLDER: opt('--kit-folder') } : {}),
};

const playwright = (args, extra = {}) => spawnSync(process.execPath, [CLI, ...args], { cwd: ROOT, env: { ...env, ...extra }, stdio: 'inherit' });

console.log(`e2e run ${runId}: ${projects.join(', ')}${readOnly ? ', read only' : ''}. Run folder: ${runDir}`);
const played = [];
const notPlayed = [];
let failed = false;
const groups = readOnly ? [projects] : projects.map(p => [p]);
for (const [i, group] of groups.entries()) {
  if (i > 0) {
    if (!reset) {
      notPlayed.push(...group);
      console.log(`e2e run: ${group.join(', ')} not played. A writing run needs a fresh store for each project: pass --reset "<command>" or set E2E_RESET_COMMAND, or run with --read-only.`);
      continue;
    }
    console.log(`e2e run: a fresh store for ${group.join(', ')}: ${reset}`);
    const r = spawnSync(reset, { shell: true, stdio: 'inherit', env });
    if (r.status !== 0) {
      console.error(`e2e run: the reset command failed (exit ${r.status}); ${group.join(', ')} not played.`);
      notPlayed.push(...group);
      failed = true;
      continue;
    }
  }
  const r = playwright(['test', ...group.map(p => `--project=${p}`), ...filters, ...passOn], { E2E_BLOB_NAME: group.join('+') });
  played.push(...group);
  if (r.status !== 0) failed = true;
}

// One HTML report over the projects played.
const blobDir = path.join(runDir, 'blob');
if (fs.existsSync(blobDir)) {
  const merged = playwright(['merge-reports', '--reporter', 'html', blobDir], { PLAYWRIGHT_HTML_OUTPUT_DIR: path.join(runDir, 'report'), PLAYWRIGHT_HTML_OPEN: 'never' });
  if (merged.status !== 0) console.error('e2e run: the HTML report could not be merged.');
}

let summary = null;
try {
  summary = JSON.parse(fs.readFileSync(path.join(runDir, 'summary.json'), 'utf8'));
  summary.notPlayed = notPlayed;
  fs.writeFileSync(path.join(runDir, 'summary.json'), `${JSON.stringify(summary, null, 2)}\n`);
} catch {
  summary = null;
}
console.log('');
console.log(`e2e run ${runId}: played ${played.join(', ') || 'nothing'}${notPlayed.length ? `; not played ${notPlayed.join(', ')}` : ''}.`);
if (summary) {
  for (const [p, c] of Object.entries(summary.projects ?? {})) console.log(`  ${p}: ${c.passed} passed, ${c.failed} failed, ${c.skipped} skipped`);
  if (summary.failedSteps?.length) console.log(`  failed steps: ${summary.failedSteps.join(', ')}`);
  const skipped = localRun && !readOnly ? (summary.steps ?? []).filter(r => r.skipped?.length) : [];
  if (skipped.length) {
    console.log(`  skipped writes on a writing local run (each is a failure):`);
    for (const r of skipped) console.log(`    ${r.project} ${r.step}: ${r.skipped.join('; ')}`);
  }
  console.log(`  summary: ${path.join(runDir, 'summary.json')}`);
  console.log(`  report:  ${path.join(runDir, 'report', 'index.html')}`);
  console.log(`  screenshots: ${path.join(runDir, 'screenshots')}`);
}
const skippedOnLocal = localRun && !readOnly && (summary?.skippedWrites ?? 0) > 0;
process.exit(failed || summary?.failed || skippedOnLocal ? 1 : 0);
