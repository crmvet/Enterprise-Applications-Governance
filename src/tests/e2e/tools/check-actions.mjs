#!/usr/bin/env node
// The action check: every side sheet the web app offers is played in a presenter step and performed by that step's
// end-to-end test. Without a browser, a server or a package (wave 4b QA: "every web action demoed and tested"; QA, fix round
// 1: by sheet, not only by tool).
//
// A sheet is one <ActionSheet> in src/ui/harness/src: its key is "<path under src, no extension>#<component>", such as
// "pages/app360/sheets#DocumentCurrentSheet" (tools/action-sites.mjs). The sheet carries the key in its site attribute,
// and the page shows it as data-action-site, so lib/demo.ts knows which sheet a step submitted.
//
// 1. The web app's writes: the tools come from WRITE_TOOLS in src/ui/harness/src/data/contracts.ts, as lib/demo.ts reads
//    them. Every one is called by a sheet (or a sheet's own check), every sheet's tool is one of them, and every tool and
//    site is a string literal (the generic forwarder in components/actions.tsx aside). Each sheet's site attribute is its
//    key, so the page can never name another sheet.
// 2. Every sheet is declared by at least one step of the suite: demo.writes('<tool>@<key>') in the body of the test
//    step('S-xx.n ...'). At run time lib/demo.ts fails the step when a declared sheet was not submitted for real and
//    answered ok.
// 3. The catalog table "Every side sheet of the web app" in src/docs/demo/scenarios.md: one row per sheet (or per
//    variant of one), with its write tool, its key and the steps that play it. Every sheet has a row; every row names a
//    known sheet with its own tool, and steps of a presenter script whose tests declare that sheet.
// 4. Every declaration names a write tool the web app calls, a known sheet with that tool, and a presenter step.
//
// Usage: node tools/check-actions.mjs [--list]     Exit code: 0 clean, 1 problems found, 2 setup error.
//   --list  prints each sheet with the steps that declare it.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { scanWebApp, writeTools } from './action-sites.mjs';

const E2E = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.resolve(E2E, '..', '..');
const HARNESS = path.join(SRC, 'ui', 'harness', 'src');
const SCENARIOS = path.join(SRC, 'demo', 'kit', 'scenarios.json');
const CATALOG = path.join(SRC, 'docs', 'demo', 'scenarios.md');
export const TABLE_HEADING = '## Every side sheet of the web app';
const list = process.argv.includes('--list');

// What each step's test declares with demo.writes(...): from its step(...) call to the next one. Each entry is a tool
// ('save_link') or a tool at one sheet ('save_link@pages/intake/sheets#LinkSheet').
export function declaredWrites(spec) {
  const starts = [...spec.matchAll(/step\((['"])(S-\d\d\.\d+) /g)].map(m => ({ id: m[2], at: m.index }));
  const out = new Map();
  starts.forEach((s, i) => {
    const body = spec.slice(s.at, i + 1 < starts.length ? starts[i + 1].at : spec.length);
    const writes = new Set();
    for (const call of body.matchAll(/\.writes\(([^)]*)\)/g)) for (const t of call[1].matchAll(/'([a-z_]+(?:@[A-Za-z0-9_/.-]+#[A-Za-z0-9_]+)?)'/g)) writes.add(t[1]);
    out.set(s.id, writes);
  });
  return out;
}

// The rows of the catalog table: | Action | Where | Tool | Sheet | Step | Same sheet, also from |. The tool cell holds
// `<tool>` (a variant may follow, such as "`withdraw_record` (change)"), the sheet cell `<key>`, the step cell one or
// more step IDs.
export function tableRows(catalog) {
  const at = catalog.indexOf(TABLE_HEADING);
  if (at < 0) return null;
  const after = catalog.slice(at + TABLE_HEADING.length);
  const end = after.search(/\n## /);
  const section = end < 0 ? after : after.slice(0, end);
  const rows = [];
  for (const line of section.split('\n')) {
    if (!/^\|/.test(line) || /^\|\s*-/.test(line) || /^\|\s*Action\s*\|/.test(line)) continue;
    const cells = line.split('|').slice(1, -1).map(c => c.trim());
    if (cells.length < 5) {
      rows.push({ action: cells[0] ?? line, tool: null, site: null, steps: [], line, short: true });
      continue;
    }
    rows.push({
      action: cells[0],
      tool: /`([a-z_]+)`/.exec(cells[2])?.[1] ?? null,
      site: /`([A-Za-z0-9_/.-]+#[A-Za-z0-9_]+)`/.exec(cells[3])?.[1] ?? null,
      steps: [...cells[4].matchAll(/S-\d\d\.\d+/g)].map(m => m[0]),
      line,
    });
  }
  return rows;
}

// Every problem the check finds, from the web app's source, the suite, the scenario file and the catalog.
export function checkActions({ harness = HARNESS, scenarios, catalog, readSpec, readScript }) {
  const problems = [];
  const tools = writeTools(harness);
  const scan = scanWebApp(harness);
  problems.push(...scan.problems);
  if (!tools.size) problems.push('no WRITE_TOOLS list in ui/harness/src/data/contracts.ts');

  // 1. The sheets and the tools.
  const sites = new Map();
  for (const s of scan.sites) {
    if (!s.tool) continue;
    if (!tools.has(s.tool)) problems.push(`${s.file}:${s.line}: the sheet ${s.key} calls ${s.tool}, which is not in WRITE_TOOLS`);
    if (s.site !== s.key) {
      const found = s.site ? `"${s.site}"` : s.siteExpression !== null && s.siteExpression !== undefined ? `not a string literal (site={${s.siteExpression}})` : 'missing';
      problems.push(`${s.file}:${s.line}: the sheet's site is ${found}; it must be site="${s.key}"`);
    }
    if (sites.has(s.key)) problems.push(`${s.file}:${s.line}: a second sheet in ${s.key}; give each sheet a component of its own`);
    else sites.set(s.key, s);
  }
  const called = new Set([...scan.sites.map(s => s.tool), ...scan.calls.map(c => c.tool)].filter(Boolean));
  for (const t of tools) if (!called.has(t)) problems.push(`${t}: in WRITE_TOOLS, and no sheet of the web app calls it`);
  for (const c of scan.calls) if (!tools.has(c.tool)) problems.push(`${c.file}:${c.line}: useWriteAction('${c.tool}'), which is not in WRITE_TOOLS`);

  // 2. The steps and what each declares.
  const scriptSteps = new Set();
  const declared = new Map();
  for (const s of scenarios.scenarios ?? []) {
    const script = s.script ? readScript(s.script) : null;
    if (script) for (const m of script.matchAll(/^### (S-\d\d\.\d+) /gm)) scriptSteps.add(m[1]);
    const spec = s.spec ? readSpec(s.spec) : null;
    if (spec) for (const [id, set] of declaredWrites(spec)) declared.set(id, set);
  }
  const bySite = new Map([...sites.keys()].map(k => [k, []]));
  for (const [id, set] of declared) {
    for (const w of set) {
      const [tool, site] = w.split('@');
      if (!tools.has(tool)) problems.push(`${id} declares ${tool}, which is not a write tool of the web app`);
      else if (!called.has(tool)) problems.push(`${id} declares ${tool}, which no sheet of the web app calls`);
      if (site) {
        const known = sites.get(site);
        if (!known) problems.push(`${id} declares ${w}: no sheet ${site} in the web app`);
        else if (known.tool !== tool) problems.push(`${id} declares ${w}: the sheet ${site} calls ${known.tool}`);
        else bySite.get(site).push(id);
      }
    }
    if (set.size && !scriptSteps.has(id)) problems.push(`${id} declares writes but is not a step of a presenter script`);
  }
  for (const [key, steps] of bySite) if (!steps.length) problems.push(`${key} (${sites.get(key).tool}): no step's test performs this sheet (demo.writes('${sites.get(key).tool}@${key}'))`);

  // 3. The catalog table.
  const rows = tableRows(catalog);
  if (!rows) problems.push(`docs/demo/scenarios.md has no section "${TABLE_HEADING.slice(3)}"`);
  else {
    const inTable = new Set();
    for (const r of rows) {
      if (r.short) {
        problems.push(`catalog row "${r.action}" has fewer than five cells`);
        continue;
      }
      if (!r.tool) problems.push(`catalog row "${r.action}" names no write tool`);
      if (!r.site) {
        problems.push(`catalog row "${r.action}" names no sheet`);
        continue;
      }
      const known = sites.get(r.site);
      if (!known) {
        problems.push(`catalog row "${r.action}": no sheet ${r.site} in the web app`);
        continue;
      }
      inTable.add(r.site);
      if (r.tool && known.tool !== r.tool) problems.push(`catalog row "${r.action}": the sheet ${r.site} calls ${known.tool}, not ${r.tool}`);
      if (!r.steps.length) problems.push(`catalog row "${r.action}" names no step`);
      for (const id of r.steps) {
        if (!scriptSteps.has(id)) problems.push(`catalog row "${r.action}": ${id} is not a step of a presenter script`);
        else if (!declared.get(id)?.has(`${known.tool}@${r.site}`)) problems.push(`catalog row "${r.action}": the test of ${id} does not perform ${r.site} (demo.writes('${known.tool}@${r.site}'))`);
      }
    }
    for (const key of sites.keys()) if (!inTable.has(key)) problems.push(`${key}: no row of the catalog table names this sheet`);
  }
  return { problems, sites, bySite, rows: rows ?? [], tools };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  let catalog;
  let scenarios;
  try {
    scenarios = JSON.parse(fs.readFileSync(SCENARIOS, 'utf8'));
    catalog = fs.readFileSync(CATALOG, 'utf8');
  } catch (e) {
    console.error(`Action check: cannot read the scenario file or the catalog: ${e.message}`);
    process.exit(2);
  }
  const read = file => (fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null);
  const result = checkActions({
    scenarios,
    catalog,
    readSpec: spec => read(path.join(E2E, spec)),
    readScript: script => read(path.join(SRC, String(script).replace(/^src\//, ''))),
  });
  if (list) for (const [key, steps] of [...result.bySite].sort()) console.log(`  ${`${result.sites.get(key).tool}@${key}`.padEnd(78)} ${steps.join(', ') || '(none)'}`);
  if (!result.problems.length) {
    console.log(`Action check: clean. ${result.sites.size} side sheets of the web app (${result.tools.size} write tools) and ${result.rows.length} rows in the catalog table, each sheet played in a presenter step whose test performs it.`);
    process.exit(0);
  }
  console.log(`Action check: ${result.problems.length} problem(s).`);
  for (const p of result.problems) console.log(`  ${p}`);
  process.exit(1);
}
