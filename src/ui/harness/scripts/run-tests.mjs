#!/usr/bin/env node
// Unit tests for the web app's logic: the API client, the development sign-in guards, the config.json checks and the
// routes. Bundles every src/**/*.test.ts with esbuild (a pinned devDependency) into dist/tests and runs the bundles
// with Node's own test runner. No browser, no network, no server: the tests pass fakes for fetch and storage.
//
// Usage: node scripts/run-tests.mjs     Exit code: the test runner's (0 when every test passed).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.join(HARNESS, 'src');
const OUT = path.join(HARNESS, 'dist', 'tests');

function find(dir) {
  const found = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => (a.name < b.name ? -1 : 1))) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) found.push(...find(full));
    else if (entry.name.endsWith('.test.ts')) found.push(full);
  }
  return found;
}

const tests = find(SRC);
if (!tests.length) {
  console.error('run-tests: no *.test.ts files under src/.');
  process.exit(2);
}

fs.rmSync(OUT, { recursive: true, force: true });
await build({
  entryPoints: tests,
  outdir: OUT,
  outbase: SRC,
  outExtension: { '.js': '.mjs' },
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node22',
  sourcemap: 'inline',
  logLevel: 'warning',
});

const bundles = tests.map(t => path.join(OUT, path.relative(SRC, t)).replace(/\.ts$/, '.mjs'));
const result = spawnSync(process.execPath, ['--test', ...bundles], { cwd: HARNESS, stdio: 'inherit' });
if (result.error) {
  console.error(`run-tests: cannot start node: ${result.error.message}`);
  process.exit(2);
}
process.exit(result.status ?? 1);
