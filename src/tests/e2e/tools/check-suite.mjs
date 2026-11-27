#!/usr/bin/env node
// The suite check: the end-to-end specs match the presenter scripts, without a browser, a server or a package.
// 1. Every scenario of src/demo/kit/scenarios.json has its spec (spec) and its presenter script (script), and every
//    spec in specs/ is one the scenario file names.
// 2. Each spec's scenario() names the scenario's ID and the title scenarios.json gives it.
// 3. Each spec has one step() per step of the presenter script ("### S-03.2 Title"), with the same ID and title, in the
//    same order, and no other step.
// 4. Every step ID appears once in the whole suite.
//
// Usage: node tools/check-suite.mjs     Exit code: 0 clean, 1 problems found, 2 setup error.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const E2E = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.resolve(E2E, '..', '..');
const SCENARIOS = path.join(SRC, 'demo', 'kit', 'scenarios.json');

let catalog;
try {
  catalog = JSON.parse(fs.readFileSync(SCENARIOS, 'utf8'));
} catch (e) {
  console.error(`Suite check: cannot read ${SCENARIOS}: ${e.message}`);
  process.exit(2);
}

const problems = [];
const seen = new Map();
let steps = 0;
// A string literal in single quotes, with \' inside.
const literal = "'((?:[^'\\\\]|\\\\.)*)'";
const unquote = s => s.replace(/\\(.)/g, '$1');

const named = new Set();
for (const s of catalog.scenarios ?? []) {
  if (!s.spec) {
    problems.push(`${s.id}: scenarios.json names no spec`);
    continue;
  }
  named.add(path.basename(s.spec));
  const specFile = path.join(E2E, s.spec);
  const scriptFile = path.join(SRC, String(s.script ?? '').replace(/^src\//, ''));
  if (!fs.existsSync(specFile)) {
    problems.push(`${s.id}: no spec ${s.spec}`);
    continue;
  }
  if (!s.script || !fs.existsSync(scriptFile)) {
    problems.push(`${s.id}: no presenter script ${s.script}`);
    continue;
  }
  if (path.basename(s.spec, '.spec.ts') !== path.basename(scriptFile, '.md')) problems.push(`${s.id}: the spec ${path.basename(s.spec)} is not named after the script ${path.basename(scriptFile)}`);
  const spec = fs.readFileSync(specFile, 'utf8');
  const script = fs.readFileSync(scriptFile, 'utf8');

  const head = new RegExp(`scenario\\(${literal},\\s*${literal}`).exec(spec);
  if (!head) problems.push(`${s.spec}: no scenario('${s.id}', ...) call`);
  else {
    if (unquote(head[1]) !== s.id) problems.push(`${s.spec}: scenario('${unquote(head[1])}'), expected '${s.id}'`);
    if (unquote(head[2]) !== s.title) problems.push(`${s.spec}: the scenario title "${unquote(head[2])}" is not the catalog's "${s.title}"`);
  }

  const want = [...script.matchAll(/^### (S-\d\d\.\d+) (.+?)\s*$/gm)].map(m => ({ id: m[1], title: m[2] }));
  // step('S-03.2 Title', async ({ demo }) => ...) or step("S-03.2 Title with an apostrophe", ...): the ID, a space, the title.
  const have = [...spec.matchAll(new RegExp(`step\\((?:${literal}|"((?:[^"\\\\]|\\\\.)*)"),\\s*async`, 'g'))].map(m => {
    const full = unquote(m[1] ?? m[2]);
    const space = full.indexOf(' ');
    return { id: space < 0 ? full : full.slice(0, space), title: space < 0 ? '' : full.slice(space + 1) };
  });
  if (!want.length) problems.push(`${s.script}: no "### ${s.id}.N" step headings`);
  const n = Math.max(want.length, have.length);
  for (let i = 0; i < n; i++) {
    const w = want[i];
    const h = have[i];
    if (!h) problems.push(`${s.spec}: no test for ${w.id} "${w.title}"`);
    else if (!w) problems.push(`${s.spec}: ${h.id} is not a step of the presenter script`);
    else if (w.id !== h.id) problems.push(`${s.spec}: test ${i + 1} is ${h.id}, the script's step ${i + 1} is ${w.id}`);
    else if (w.title !== h.title) problems.push(`${s.spec}: ${h.id} is titled "${h.title}", the script says "${w.title}"`);
  }
  for (const h of have) {
    if (seen.has(h.id)) problems.push(`${h.id} is a test in both ${seen.get(h.id)} and ${s.spec}`);
    seen.set(h.id, s.spec);
  }
  steps += want.length;
}
for (const f of fs.readdirSync(path.join(E2E, 'specs'))) {
  if (f.endsWith('.spec.ts') && !named.has(f)) problems.push(`specs/${f} is not named in scenarios.json`);
}

const count = (catalog.scenarios ?? []).length;
if (!problems.length) {
  console.log(`Suite check: clean. ${count} specs, ${steps} steps; every presenter step has its test, in order.`);
  process.exit(0);
}
console.log(`Suite check: ${problems.length} problem(s).`);
for (const p of problems) console.log(`  ${p}`);
process.exit(1);
