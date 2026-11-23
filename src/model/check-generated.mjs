#!/usr/bin/env node
// Determinism check for the model generators.
// 1. Runs "node model/generate.mjs write" twice, as two processes, into two new temporary folders, and compares every
//    file byte for byte.
// 2. Runs "node model/generate.mjs names" twice and compares the output byte for byte. Its column count must match
//    the column map's.
// 3. Compares the fresh files with model/generated/, so the drafts kept there match the model. Line endings aside:
//    git may check text files out with CRLF.
// 4. Each generated file says "draft, not applied; D-26 chose Azure SQL for the first release" and is ASCII, and every
//    working default marker is well formed and names one of the six open questions (document 28, section 3).
// Nothing is applied to any database or environment. No packages.
//
// Usage: node model/check-generated.mjs [--generated <dir>]
// Exit code: 0 clean, 1 problems found, 2 setup error.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { SRC, option } from '../tools/lib/files.mjs';
import { DRAFT } from './generators/physical.mjs';
import { OUTPUTS } from './generators/outputs.mjs';

const args = process.argv.slice(2);
const kept = path.resolve(option(args, '--generated', path.join(SRC, 'model', 'generated')));
const GENERATE = path.join(SRC, 'model', 'generate.mjs');
const QUESTIONS = new Set(['1', '4', '7', '8', '9', '12']);

const problems = [];
const lines = [];
const sha = buf => crypto.createHash('sha256').update(buf).digest('hex');
const rel = p => path.relative(SRC, p).split(path.sep).join('/');

function generate(commandArgs) {
  const r = spawnSync(process.execPath, [GENERATE, ...commandArgs], { cwd: SRC, encoding: 'buffer', maxBuffer: 64 * 1024 * 1024 });
  if (r.error || r.status !== 0) {
    console.error(`Generated check: "node model/generate.mjs ${commandArgs.join(' ')}" failed (exit ${r.status}).`);
    if (r.error) console.error(`  ${r.error.message}`);
    process.stderr.write(r.stderr || '');
    process.exit(2);
  }
  return r.stdout;
}

// The first line where two texts differ, for the report.
function firstDifference(a, b) {
  const x = a.split('\n');
  const y = b.split('\n');
  for (let i = 0; i < Math.max(x.length, y.length); i++) if (x[i] !== y[i]) return i + 1;
  return 0;
}

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'model-generated-'));
try {
  // ---- 1. two runs of write ----
  const runs = ['run-1', 'run-2'].map(n => path.join(tmp, n));
  for (const dir of runs) generate(['write', '--out', dir]);
  for (const dir of runs) {
    const found = fs.readdirSync(dir).sort();
    if (found.join('|') !== [...OUTPUTS].sort().join('|')) problems.push(`${path.basename(dir)} wrote ${found.join(', ')}, expected ${OUTPUTS.join(', ')}`);
  }
  for (const name of OUTPUTS) {
    const a = fs.readFileSync(path.join(runs[0], name));
    const b = fs.readFileSync(path.join(runs[1], name));
    if (!a.equals(b)) problems.push(`${name}: the two runs differ from line ${firstDifference(a.toString('utf8'), b.toString('utf8'))}`);
    lines.push(`  ${name.padEnd(34)} ${String(a.length).padStart(8)} bytes  sha256 ${sha(a)}  ${a.equals(b) ? 'same in both runs' : 'DIFFERENT'}`);
  }

  // ---- 2. two runs of names ----
  const n1 = generate(['names']);
  const n2 = generate(['names']);
  if (!n1.equals(n2)) problems.push(`names: the two runs differ from line ${firstDifference(n1.toString('utf8'), n2.toString('utf8'))}`);
  const summary = /(\d+) entities, (\d+) columns\./.exec(n1.toString('utf8'));
  let mapColumns = null;
  try { mapColumns = JSON.parse(fs.readFileSync(path.join(runs[0], 'column-map.json'), 'utf8')).counts.columns; } catch (e) { problems.push(`column-map.json: ${e.message}`); }
  if (!summary) problems.push('names: no summary line');
  else if (Number(summary[2]) !== mapColumns) problems.push(`names lists ${summary[2]} columns, the column map ${mapColumns}`);
  lines.push(`  names: ${summary ? `${summary[1]} entities, ${summary[2]} columns` : '?'}, ${n1.length} bytes, sha256 ${sha(n1)}  ${n1.equals(n2) ? 'same in both runs' : 'DIFFERENT'}`);

  // ---- 3. the kept drafts match a fresh run ----
  if (!fs.existsSync(kept)) problems.push(`${rel(kept)} does not exist. Run "node model/generate.mjs write"`);
  else {
    for (const name of OUTPUTS) {
      const file = path.join(kept, name);
      if (!fs.existsSync(file)) { problems.push(`${rel(file)} is missing. Run "node model/generate.mjs write"`); continue; }
      const fresh = fs.readFileSync(path.join(runs[0], name), 'utf8');
      const have = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
      if (have !== fresh) problems.push(`${rel(file)} is out of date from line ${firstDifference(have, fresh)}. Run "node model/generate.mjs write"`);
    }
    for (const extra of fs.readdirSync(kept).filter(f => !OUTPUTS.includes(f)).sort()) problems.push(`${rel(path.join(kept, extra))} is not a generator output`);
  }

  // ---- 4. draft marker, ASCII, working default markers ----
  const markers = new Map();
  for (const name of OUTPUTS) {
    const text = fs.readFileSync(path.join(runs[0], name), 'utf8');
    if (!text.includes(DRAFT)) problems.push(`${name}: does not say "${DRAFT}"`);
    const bad = /[^\t\n\r\x20-\x7e]/.exec(text);
    if (bad) problems.push(`${name}: non-ASCII character U+${bad[0].codePointAt(0).toString(16).toUpperCase().padStart(4, '0')} at line ${text.slice(0, bad.index).split('\n').length}`);
    for (const m of text.matchAll(/WORKING DEFAULT[^)\n]*\)?/g)) {
      const ok = /^WORKING DEFAULT \(open design question (\d+)\)$/.exec(m[0]);
      if (!ok) { problems.push(`${name}: malformed marker "${m[0].slice(0, 60)}"`); continue; }
      if (!QUESTIONS.has(ok[1])) problems.push(`${name}: marker names question ${ok[1]}, which is not one of the six open questions`);
      markers.set(ok[1], (markers.get(ok[1]) || 0) + 1);
    }
  }
  lines.push(`  working default markers: ${[...markers].sort((a, b) => a[0] - b[0]).map(([k, v]) => `question ${k} x${v}`).join(', ') || 'none'}`);
} finally {
  fs.rmSync(tmp, { recursive: true, force: true });
}

if (!problems.length) {
  console.log(`Generated check: clean. ${OUTPUTS.length} files and the names printout are the same byte for byte in two runs, and ${rel(kept)} matches a fresh run. Status of every file: ${DRAFT}. Nothing was applied.`);
  for (const l of lines) console.log(l);
  process.exit(0);
}
console.log(`Generated check: ${problems.length} problem(s).`);
for (const p of problems) console.log(`  ${p}`);
for (const l of lines) console.log(l);
process.exit(1);
