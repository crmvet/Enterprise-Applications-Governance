#!/usr/bin/env node
// The script check: every count a presenter script promises on screen is one its end-to-end test checks. Without a
// browser, a server or a package.
//
// For each step of each presenter script (src/docs/demo/scenarios/<ID>-<slug>.md), the numbers its "Proof on screen"
// names (such as "9 applications depend on it" or "Submitted. 2 approvals are asked.") must each appear in the body of
// the test with the same step ID (step('S-17.1 ...', ...)). A number the test does not name means the script can drift
// from the product without a test failing (product review).
//
// Not counted, since a test cannot or need not check them as numbers: dates and times ("25 Nov 2026", "07:40"), years,
// step and scenario IDs (S-04.4), guideline numbers (G17), record numbers (DEC-0123), version numbers (1.12.1), money,
// "Application 360", and a number inside a link or a code span. A step can name a number on purpose in its script and
// leave its check to another step: write "(checked in S-xx.n)" after it. A count that falls as the store ages is
// written as N with what a fresh store shows in brackets, such as "(N is the days left: 4 on a fresh store)".
//
// Usage: node tools/check-scripts.mjs [--list]     Exit code: 0 clean, 1 counts no test checks, 2 setup error.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const E2E = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.resolve(E2E, '..', '..');
const SCENARIOS = path.join(SRC, 'demo', 'kit', 'scenarios.json');
const list = process.argv.includes('--list');

let catalog;
try {
  catalog = JSON.parse(fs.readFileSync(SCENARIOS, 'utf8'));
} catch (e) {
  console.error(`Script check: cannot read ${SCENARIOS}: ${e.message}`);
  process.exit(2);
}

const MONTHS = 'Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec|January|February|March|April|June|July|August|September|October|November|December';

// The numbers a proof names, with the text that is not a count taken out first.
export function countsIn(proof) {
  const text = proof
    .replace(/`[^`]*`/g, ' ')
    .replace(/\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/https?:\/\/\S+/g, ' ')
    .replace(new RegExp(`\\b\\d{1,2} (?:${MONTHS})(?: \\d{4})?\\b`, 'g'), ' ')
    .replace(new RegExp(`\\b(?:${MONTHS}) \\d{1,2}(?:, \\d{4})?\\b`, 'g'), ' ')
    .replace(/\b\d{1,2}:\d{2}\b/g, ' ')
    .replace(/\b(?:19|20)\d{2}\b/g, ' ')
    .replace(/\bS-\d\d(?:\.\d+)?\b/g, ' ')
    .replace(/\bG\d+\b/g, ' ')
    .replace(/\b[A-Z]{2,5}-\d+\b/g, ' ')
    .replace(/\b\d+(?:\.\d+){1,3}\b/g, ' ')
    .replace(/[$€£]\s?\d[\d,.]*/g, ' ')
    .replace(/\b(?:Application|the) 360\b/g, ' ')
    .replace(/\([^)]*\bon a fresh store\)/g, ' ')
    .replace(/\(checked in S-\d\d\.\d+\)/g, ' ');
  return [...new Set([...text.matchAll(/(?<![\w.-])(\d+)(?![\w.-]|:\d)/g)].map(m => m[1]))];
}

// The body of each test: from its step(...) call to the next one.
function testBodies(spec) {
  const starts = [...spec.matchAll(/step\((['"])(S-\d\d\.\d+) /g)].map(m => ({ id: m[2], at: m.index }));
  const bodies = new Map();
  starts.forEach((s, i) => bodies.set(s.id, spec.slice(s.at, i + 1 < starts.length ? starts[i + 1].at : spec.length)));
  return bodies;
}

// The "Proof on screen" bullet of each step.
function proofs(script) {
  const out = new Map();
  const sections = script.split(/^### /m).slice(1);
  for (const section of sections) {
    const id = /^(S-\d\d\.\d+) /.exec(section)?.[1];
    if (!id) continue;
    const m = /- \*\*Proof on screen:\*\*([\s\S]*?)(?=\n- \*\*|\n#|$)/.exec(section);
    if (m) out.set(id, m[1].replace(/\s+/g, ' ').trim());
  }
  return out;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  const misses = [];
  let checked = 0;
  for (const s of catalog.scenarios ?? []) {
    const specFile = path.join(E2E, s.spec ?? '');
    const scriptFile = path.join(SRC, String(s.script ?? '').replace(/^src\//, ''));
    if (!s.spec || !fs.existsSync(specFile) || !s.script || !fs.existsSync(scriptFile)) continue;
    const bodies = testBodies(fs.readFileSync(specFile, 'utf8'));
    for (const [id, proof] of proofs(fs.readFileSync(scriptFile, 'utf8'))) {
      const body = bodies.get(id) ?? '';
      for (const n of countsIn(proof)) {
        checked++;
        if (!new RegExp(`(?<![\\w.])${n}(?![\\w])`).test(body)) misses.push({ id, n, proof });
      }
    }
  }
  if (!misses.length) {
    console.log(`Script check: clean. ${checked} counts in the presenter scripts' proofs, each checked by its step's test.`);
    process.exit(0);
  }
  console.log(`Script check: ${misses.length} of ${checked} counts in the proofs are not checked by the step's test.`);
  for (const m of misses) console.log(`  ${m.id}: ${m.n}${list ? `  (${m.proof.slice(0, 160)})` : ''}`);
  process.exit(1);
}
