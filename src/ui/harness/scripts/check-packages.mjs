#!/usr/bin/env node
// Package check for the UI harness. Reads files only: no install and no network.
// 1. Every direct dependency in package.json is an exact version.
// 2. Every package in package-lock.json comes from the public npm registry with a sha512 integrity hash (a package
//    bundled inside its parent's tarball is covered by the parent's).
// 3. Every license is MIT, Apache-2.0 or BSD (BSD-2-Clause, BSD-3-Clause, 0BSD), or a listed exception (with its
//    platform binaries).
// 4. src/THIRD-PARTY.md lists every package of the lock file, with the same version and license, and nothing more.
//
// Usage: node scripts/check-packages.mjs     Exit code: 0 clean, 1 problems found, 2 setup error.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const THIRD_PARTY = path.resolve(HARNESS, '..', '..', 'THIRD-PARTY.md');
const SECTION = '## Node: ui/harness only';
const ALLOWED = new Set(['MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', '0BSD']);
// Exceptions need a reason here and a row in THIRD-PARTY.md that says so.
const EXCEPTIONS = new Map([
  ['picocolors', { licence: 'ISC', why: 'Terminal colors for postcss inside the Vite build. Build time only, never in the page bundle. An open point in ui/design/pages.md.' }],
  ['lucide-react', { licence: 'ISC', why: 'The icons of the pages (shadcn/ui uses them). In the page bundle. ISC is a short permissive license, close to MIT.' }],
  ['lightningcss', { licence: 'MPL-2.0', why: 'The CSS compiler inside Tailwind CSS 4. Build time only, never in the page bundle, used unchanged.' }],
  ['graceful-fs', { licence: 'ISC', why: 'File reads inside the Tailwind CSS build. Build time only, never in the page bundle. ISC is a short permissive license, close to MIT.' }],
]);
// The platform binaries of an excepted package (lightningcss-win32-x64-msvc and the like) share its exception.
function exceptionFor(name) {
  if (EXCEPTIONS.has(name)) return EXCEPTIONS.get(name);
  const base = name.replace(/-(win32|darwin|linux|freebsd|android)-.*$/, '');
  return base !== name ? EXCEPTIONS.get(base) : undefined;
}

let pkg;
let lock;
let md;
try {
  pkg = JSON.parse(fs.readFileSync(path.join(HARNESS, 'package.json'), 'utf8'));
  lock = JSON.parse(fs.readFileSync(path.join(HARNESS, 'package-lock.json'), 'utf8'));
  md = fs.readFileSync(THIRD_PARTY, 'utf8');
} catch (e) {
  console.error(`Package check: cannot read the inputs: ${e.message}`);
  process.exit(2);
}

const problems = [];
const notes = [];

// 1. exact versions
for (const field of ['dependencies', 'devDependencies']) {
  for (const [name, version] of Object.entries(pkg[field] ?? {})) {
    if (!/^\d+\.\d+\.\d+$/.test(version)) problems.push(`package.json ${field}: ${name} is "${version}", not an exact version`);
  }
}

// 2 and 3. the lock file
if (lock.lockfileVersion !== 3) problems.push(`package-lock.json: lockfileVersion ${lock.lockfileVersion}, expected 3`);
const locked = new Map(); // name@version -> { name, version, licence }
for (const [where, entry] of Object.entries(lock.packages ?? {})) {
  if (!where) continue;
  const name = where.replace(/^.*node_modules\//, '');
  const id = `${name}@${entry.version}`;
  // A bundled package (inBundle) ships inside its parent's tarball, so the parent's registry source and hash cover it.
  const inParent = entry.inBundle === true && where.includes('/node_modules/', 'node_modules/'.length);
  if (!inParent && !entry.resolved?.startsWith('https://registry.npmjs.org/')) problems.push(`${where}: not from https://registry.npmjs.org/ (${entry.resolved})`);
  if (!inParent && !entry.integrity?.startsWith('sha512-')) problems.push(`${where}: no sha512 integrity hash`);
  const licence = entry.license ?? '(none)';
  const exception = exceptionFor(name);
  if (!ALLOWED.has(licence)) {
    if (exception && exception.licence === licence) notes.push(`${id}: ${licence}, a listed exception. ${exception.why}`);
    else problems.push(`${id}: license ${licence} is not MIT, Apache-2.0 or BSD`);
  }
  locked.set(id, { name, version: entry.version, licence });
}

// 4. THIRD-PARTY.md
const start = md.indexOf(SECTION);
if (start < 0) {
  problems.push(`THIRD-PARTY.md has no "${SECTION}" section`);
} else {
  const rest = md.slice(start + SECTION.length);
  const next = rest.search(/\n## /);
  const body = next < 0 ? rest : rest.slice(0, next);
  const listed = new Map();
  for (const line of body.split(/\r?\n/)) {
    const cells = line.split('|').map(c => c.trim());
    if (cells.length < 5 || !/^\d/.test(cells[2] ?? '')) continue;
    const name = cells[1].replace(/`/g, '');
    listed.set(`${name}@${cells[2]}`, { licence: cells[3].split(' ')[0] });
  }
  for (const [id, p] of locked) {
    const row = listed.get(id);
    if (!row) problems.push(`THIRD-PARTY.md: ${id} is in the lock file but not listed`);
    else if (row.licence !== p.licence) problems.push(`THIRD-PARTY.md: ${id} is listed as ${row.licence}, the lock file says ${p.licence}`);
  }
  for (const id of listed.keys()) if (!locked.has(id)) problems.push(`THIRD-PARTY.md: ${id} is listed but not in the lock file`);
}

for (const n of notes) console.log(`  note: ${n}`);
const summary = `${Object.keys(pkg.dependencies ?? {}).length + Object.keys(pkg.devDependencies ?? {}).length} direct, ${locked.size} in the lock file`;
if (!problems.length) {
  console.log(`Package check: clean. ${summary}, every one listed in THIRD-PARTY.md.`);
  process.exit(0);
}
console.log(`Package check: ${problems.length} problem(s). ${summary}.`);
for (const p of problems) console.log(`  ${p}`);
process.exit(1);
