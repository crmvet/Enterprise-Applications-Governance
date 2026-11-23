#!/usr/bin/env node
// Content check: the playbook chapters, the processes and the guidelines under content/.
// No packages. Node 20 or later. Reads files only.
//
// It fails when:
//   - a chapter, a process or a guideline cites a guideline number that does not exist, or one that is retired;
//   - a chapter cites a process that does not exist;
//   - a chapter's header lists ("Guidelines", "Processes") differ from the numbers its text uses;
//   - a guideline's "Cited in chapters" or a process's "Used in chapters" differs from the chapter headers,
//     or a guideline's "Processes" differs from the processes' "Guidelines";
//   - the chapter index (playbook/README.md) or the guideline index (guidelines/README.md) differs from the files;
//   - the guideline files differ from the shipped guideline rows (number, version, retired state, rule text);
//   - an active guideline lacks a part of its working shape, a Microsoft anchor lacks a link or a checked date,
//     or its product line lacks "designed to";
//   - a relative link points to a missing file or heading;
//   - an image has no alt text or is not a local image file, an image file is shown by no page, or a binary file
//     is neither an image (png, jpg, gif, webp) nor a Visio diagram source (vsdx, vssx);
//   - a file holds a non-ASCII character, an internal marker, a data-store claim or a present-tense product claim
//     (SOURCES.md holds the source pointers and is not published, so only its working default table is checked).
//
// Usage: node tools/content-check.mjs [--root <content dir>] [--seed <guideline.json>] [--chapters <count>] [--no-sources]
//   --no-sources   for a copy of src/ without content/SOURCES.md (it is not published): skip the working default anchors
// Exit code: 0 clean, 1 findings, 2 usage error.
import fs from 'node:fs';
import path from 'node:path';
import { SRC, walk, isBinary, option } from './lib/files.mjs';

const args = process.argv.slice(2);
const ROOT = path.resolve(option(args, '--root', path.join(SRC, 'content')));
const SEED = path.resolve(option(args, '--seed', path.join(SRC, 'config', 'presets', 'balanced', 'guideline.json')));
// Chapter 0 opens the playbook and chapters 1 to 22 follow. --chapters is the count, chapter 0 included.
const EXPECTED_CHAPTERS = Number(option(args, '--chapters', '23'));
const NO_SOURCES = args.includes('--no-sources');
// The six open design questions (document 28, section 3).
const OPEN_QUESTIONS = new Set([1, 4, 7, 8, 9, 12]);

for (const [what, p] of [['content folder', ROOT], ['guideline seed', SEED]]) {
  if (!fs.existsSync(p)) {
    console.error(`Content check: no ${what} at ${p}`);
    process.exit(2);
  }
}

const findings = [];
const fail = (rel, msg) => findings.push(`  ${rel}: ${msg}`);

// ---- read every file ----
const files = new Map(); // rel -> text
// The binary files a page may use: the images it shows, and the Visio sources of those diagrams.
const IMAGE_EXT = /\.(png|jpe?g|gif|webp)$/i;
const DIAGRAM_SOURCE_EXT = /\.(vsdx|vssx)$/i;
const assets = new Set(); // rel of each image or diagram source
for (const { full, rel } of walk(ROOT)) {
  const buf = fs.readFileSync(full);
  if (isBinary(rel, buf)) {
    if (IMAGE_EXT.test(rel) || DIAGRAM_SOURCE_EXT.test(rel)) assets.add(rel);
    else fail(rel, 'binary file in content/ (only images and Visio diagram sources)');
    continue;
  }
  files.set(rel, buf.toString('utf8'));
}
const PRIVATE = 'SOURCES.md';

// ---- helpers ----
const G_RE = /\bG(\d{1,3})\b/g;
const P_RE = /\bP(\d{2})\b/g;
const gRefs = text => new Set([...text.matchAll(G_RE)].map(m => `G${Number(m[1])}`));
const pRefs = text => new Set([...text.matchAll(P_RE)].map(m => `P${m[1]}`));
const nums = text => new Set([...String(text || '').matchAll(/\b(\d+)\b/g)].map(m => Number(m[1])));
const norm = s => s.replace(/\s+/g, ' ').trim();
// Sorts IDs such as G4 or P03, and plain chapter numbers, by their number.
const idNumber = x => Number(String(x).replace(/^[A-Z]/, ''));
const sortIds = set => [...set].sort((a, b) => idNumber(a) - idNumber(b));
const show = set => sortIds(set).join(', ') || '(none)';
const sameSet = (a, b) => a.size === b.size && [...a].every(x => b.has(x));
// Reports the items found in one place but not the other, each side with its own label.
function diff(rel, what, foundIn, found, listedIn, listed) {
  const notListed = [...found].filter(x => !listed.has(x));
  const notFound = [...listed].filter(x => !found.has(x));
  if (notListed.length || notFound.length) {
    fail(rel, `${what}: in ${foundIn} but not in ${listedIn}: ${show(new Set(notListed))}; in ${listedIn} but not in ${foundIn}: ${show(new Set(notFound))}`);
  }
}
// The value after "- **Label:**" on its own line.
function meta(text, label) {
  const re = new RegExp(`^- \\*\\*${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}:\\*\\*\\s*(.*)$`, 'm');
  const m = text.match(re);
  return m ? m[1].trim() : null;
}
// Lines of a "## Heading" section, up to the next "## ".
function section(text, heading) {
  const lines = text.split(/\r?\n/);
  const start = lines.findIndex(l => l.trim() === `## ${heading}`);
  if (start < 0) return null;
  const out = [];
  for (let i = start + 1; i < lines.length && !lines[i].startsWith('## '); i++) out.push(lines[i]);
  return out;
}
function checkAnchors(rel, lines, heading) {
  if (!lines) { fail(rel, `no "## ${heading}" section`); return; }
  const bullets = lines.filter(l => l.startsWith('- '));
  if (!bullets.length) fail(rel, `"${heading}" has no entries`);
  for (const b of bullets) {
    if (!/\]\(https:\/\/[^)\s]+\)/.test(b)) fail(rel, `"${heading}" entry without a https link: ${b.slice(0, 80)}`);
    if (!/checked \d{4}-\d{2}-\d{2}/.test(b)) fail(rel, `"${heading}" entry without a checked date: ${b.slice(0, 80)}`);
  }
}
// GitHub-style heading slug.
const slug = h => h.trim().toLowerCase().replace(/[^a-z0-9 _-]/g, '').replace(/ /g, '-');

// ---- guidelines ----
const guidelines = new Map(); // G<n> -> info
for (const [rel, text] of files) {
  const m = rel.match(/^guidelines\/G(\d{2})-[a-z0-9-]+\.md$/);
  if (!m) continue;
  const n = Number(m[1]);
  const id = `G${n}`;
  const title = text.match(/^# G(\d+)\. (.+)$/m);
  if (!title || Number(title[1]) !== n) fail(rel, `title must start "# ${id}. "`);
  if (meta(text, 'Number') !== id) fail(rel, `"Number" must be ${id}`);
  const version = (meta(text, 'Version') || '').match(/^(\d+\.\d+)/);
  if (!version) fail(rel, '"Version" missing or not two-part');
  const status = meta(text, 'Status');
  if (status !== 'active' && status !== 'retired') fail(rel, '"Status" must be active or retired');
  if (guidelines.has(id)) fail(rel, `second file for ${id}`);
  const info = { id, rel, text, status, version: version ? version[1] : null };
  if (status === 'active') {
    info.home = Number((meta(text, 'Home chapter') || '').match(/^(\d+)/)?.[1]);
    info.citedIn = nums(meta(text, 'Cited in chapters'));
    info.processes = pRefs(meta(text, 'Processes') || '');
    // Chapter 0 is a real chapter, so test for a number, not for a truthy value.
    if (!Number.isInteger(info.home)) fail(rel, '"Home chapter" missing');
    if (!info.citedIn.size) fail(rel, '"Cited in chapters" missing or empty');
    if (!info.processes.size) fail(rel, '"Processes" missing or empty');
    for (const part of ['**Rule.**', '**Why it matters.**', '**How the product is designed to support it.**',
      '- **When:**', '- **Who:**', '- **Steps:**', '- **Done when:**', '- **Evidence:**']) {
      if (!text.includes(part)) fail(rel, `missing "${part}"`);
    }
    if (!/- \*\*Steps:\*\*\s*\r?\n\s+1\. /.test(text)) fail(rel, '"Steps" has no numbered step');
    const productLine = text.match(/\*\*How the product is designed to support it\.\*\*([^\n]*)/);
    if (productLine && !/designed (to|so)\b/.test(productLine[1])) fail(rel, 'the product line does not say "designed to"');
    checkAnchors(rel, section(text, 'Microsoft anchors'), 'Microsoft anchors');
  } else if (status === 'retired') {
    const merged = meta(text, 'Merged into');
    if (!merged || !/^G\d+$/.test(merged)) fail(rel, '"Merged into" missing');
    info.mergedInto = merged;
  }
  guidelines.set(id, info);
}
const gNumbers = [...guidelines.keys()].map(id => Number(id.slice(1))).sort((a, b) => a - b);
for (let i = 1; i <= (gNumbers.at(-1) || 0); i++) {
  if (!guidelines.has(`G${i}`)) fail('guidelines/', `no file for G${i}; numbers are permanent, so a retired number keeps a file`);
}
const activeG = new Set([...guidelines.values()].filter(g => g.status === 'active').map(g => g.id));
for (const g of guidelines.values()) {
  if (g.mergedInto && !activeG.has(g.mergedInto)) fail(g.rel, `merged into ${g.mergedInto}, which is not an active guideline`);
}
// Every guideline number a file cites must exist and be active.
function checkGuidelineRefs(rel, refs) {
  for (const id of refs) {
    if (!guidelines.has(id)) fail(rel, `cites ${id}, which does not exist`);
    else if (!activeG.has(id)) fail(rel, `cites ${id}, which is retired (merged into ${guidelines.get(id).mergedInto})`);
  }
}

// ---- the shipped guideline rows ----
let seedRows = [];
try {
  seedRows = JSON.parse(fs.readFileSync(SEED, 'utf8')).rows || [];
} catch (e) {
  fail(path.basename(SEED), `cannot read: ${e.message}`);
}
const seedRel = path.relative(SRC, SEED).split(path.sep).join('/');
const seedNumbers = new Set(seedRows.map(r => r.number));
for (const row of seedRows) {
  const g = guidelines.get(row.number);
  if (!g) { fail(seedRel, `${row.number} has no guideline file`); continue; }
  if (g.version !== row.version) fail(g.rel, `version ${g.version} differs from the seed row (${row.version})`);
  const seedRetired = row.isActive === false;
  if (seedRetired !== (g.status === 'retired')) fail(g.rel, `status ${g.status} differs from the seed row (${seedRetired ? 'retired' : 'active'})`);
  if (!norm(g.text).includes(norm(row.rule))) fail(g.rel, `does not contain the seed row's rule text: "${row.rule.slice(0, 60)}..."`);
}
for (const id of guidelines.keys()) if (!seedNumbers.has(id)) fail(guidelines.get(id).rel, `${id} has no row in ${seedRel}`);

// ---- processes ----
const PROC_REL = 'playbook/processes.md';
const processes = new Map(); // P<nn> -> info
if (!files.has(PROC_REL)) fail(PROC_REL, 'missing');
else {
  const text = files.get(PROC_REL);
  const parts = text.split(/^## /m).slice(1);
  for (const part of parts) {
    const m = part.match(/^(P\d{2}) (.+)/);
    if (!m) continue;
    const body = '## ' + part;
    const id = m[1];
    if (processes.has(id)) fail(PROC_REL, `second section for ${id}`);
    processes.set(id, {
      id,
      title: m[2].trim(),
      guidelines: gRefs(meta(body, 'Guidelines') || ''),
      usedIn: nums(meta(body, 'Used in chapters')),
    });
    if (!meta(body, 'Guidelines')) fail(PROC_REL, `${id} has no "Guidelines" line`);
    if (!meta(body, 'Used in chapters')) fail(PROC_REL, `${id} has no "Used in chapters" line`);
  }
  const pNumbers = [...processes.keys()].map(id => Number(id.slice(1))).sort((a, b) => a - b);
  for (let i = 1; i <= (pNumbers.at(-1) || 0); i++) {
    const id = `P${String(i).padStart(2, '0')}`;
    if (!processes.has(id)) fail(PROC_REL, `no section for ${id}`);
  }
  checkGuidelineRefs(PROC_REL, gRefs(text));
  for (const id of pRefs(text)) if (!processes.has(id)) fail(PROC_REL, `cites ${id}, which does not exist`);
}

// ---- chapters ----
const chapters = new Map(); // number -> info
const HEADER_LINE = /^- \*\*(Guidelines|Processes):\*\*.*$/gm;
for (const [rel, text] of files) {
  const m = rel.match(/^playbook\/(\d{2})-[a-z0-9-]+\.md$/);
  if (!m) continue;
  const n = Number(m[1]);
  const title = text.match(/^# Chapter (\d+)\. (.+)$/m);
  if (!title || Number(title[1]) !== n) fail(rel, `title must start "# Chapter ${n}. "`);
  if (chapters.has(n)) fail(rel, `second file for chapter ${n}`);
  const headerG = meta(text, 'Guidelines');
  const headerP = meta(text, 'Processes');
  if (!headerG) fail(rel, 'no "Guidelines" header line');
  if (!headerP) fail(rel, 'no "Processes" header line');
  const info = { n, rel, title: title ? title[2].trim() : '', guidelines: gRefs(headerG || ''), processes: pRefs(headerP || '') };
  chapters.set(n, info);

  const body = text.replace(HEADER_LINE, '');
  const allG = gRefs(text);
  const allP = pRefs(text);
  checkGuidelineRefs(rel, allG);
  for (const id of allP) if (!processes.has(id)) fail(rel, `cites ${id}, which does not exist`);
  diff(rel, 'guidelines', 'the text', gRefs(body), 'the header', info.guidelines);
  diff(rel, 'processes', 'the text', pRefs(body), 'the header', info.processes);

  for (const heading of ['Why this chapter exists', 'What good looks like', 'Guidelines this chapter applies',
    'Processes this chapter uses', 'How the product is designed to support it', 'What you can tailor', 'Done when']) {
    if (!section(text, heading)) fail(rel, `no "## ${heading}" section`);
  }
  if (!text.split(/\r?\n/).some(l => l.startsWith('## Who decides'))) fail(rel, 'no "## Who decides" section');
  const support = section(text, 'How the product is designed to support it');
  if (support && !/designed (to|so)\b/.test(support.join(' '))) fail(rel, 'the product section does not say "designed to"');
  checkAnchors(rel, section(text, 'Microsoft guidance it applies'), 'Microsoft guidance it applies');
}
// Chapters are numbered 0 to EXPECTED_CHAPTERS - 1, with no gap.
for (let i = 0; i < Math.max(EXPECTED_CHAPTERS, ...[...chapters.keys()].map(n => n + 1)); i++) {
  if (!chapters.has(i)) fail('playbook/', `no file for chapter ${i}`);
}
if (chapters.size !== EXPECTED_CHAPTERS) fail('playbook/', `${chapters.size} chapters, expected ${EXPECTED_CHAPTERS}`);

// ---- cross-checks ----
for (const g of guidelines.values()) {
  if (g.status !== 'active') continue;
  const actual = new Set([...chapters.values()].filter(c => c.guidelines.has(g.id)).map(c => c.n));
  if (!actual.size) fail(g.rel, `${g.id} is cited by no chapter`);
  diff(g.rel, `chapters for ${g.id}`, 'the chapter headers', actual, '"Cited in chapters"', g.citedIn);
  if (Number.isInteger(g.home) && !actual.has(g.home)) fail(g.rel, `home chapter ${g.home} does not list ${g.id}`);
  checkGuidelineRefs(g.rel, gRefs(g.text));
  for (const p of g.processes) if (!processes.has(p)) fail(g.rel, `cites ${p}, which does not exist`);
  const fromProcesses = new Set([...processes.values()].filter(p => p.guidelines.has(g.id)).map(p => p.id));
  diff(g.rel, `processes for ${g.id}`, 'processes.md', fromProcesses, 'its "Processes" line', g.processes);
}
for (const p of processes.values()) {
  const actual = new Set([...chapters.values()].filter(c => c.processes.has(p.id)).map(c => c.n));
  if (!actual.size) fail(PROC_REL, `${p.id} is used by no chapter`);
  diff(PROC_REL, `chapters for ${p.id}`, 'the chapter headers', actual, '"Used in chapters"', p.usedIn);
}

// ---- indexes ----
const INDEX_REL = 'playbook/README.md';
if (!files.has(INDEX_REL)) fail(INDEX_REL, 'missing');
else {
  const text = files.get(INDEX_REL);
  checkGuidelineRefs(INDEX_REL, gRefs(text));
  for (const id of pRefs(text)) if (!processes.has(id)) fail(INDEX_REL, `cites ${id}, which does not exist`);
  const seen = new Set();
  for (const m of text.matchAll(/^\| (\d+) \| \[([^\]]+)\]\(([^)]+)\) \| ([^|]*) \| ([^|]*) \|$/gm)) {
    const n = Number(m[1]);
    const c = chapters.get(n);
    seen.add(n);
    if (!c) { fail(INDEX_REL, `row for chapter ${n}, which has no file`); continue; }
    if (`playbook/${m[3]}` !== c.rel) fail(INDEX_REL, `chapter ${n} links to ${m[3]}, not ${path.basename(c.rel)}`);
    if (norm(m[2]) !== norm(c.title)) fail(INDEX_REL, `chapter ${n} title differs from its file`);
    if (!sameSet(gRefs(m[4]), c.guidelines)) fail(INDEX_REL, `chapter ${n} guidelines ${show(gRefs(m[4]))} differ from its header ${show(c.guidelines)}`);
    if (!sameSet(pRefs(m[5]), c.processes)) fail(INDEX_REL, `chapter ${n} processes ${show(pRefs(m[5]))} differ from its header ${show(c.processes)}`);
  }
  for (const n of chapters.keys()) if (!seen.has(n)) fail(INDEX_REL, `no row for chapter ${n}`);
}
const GINDEX_REL = 'guidelines/README.md';
if (!files.has(GINDEX_REL)) fail(GINDEX_REL, 'missing');
else {
  const seen = new Set();
  for (const m of files.get(GINDEX_REL).matchAll(/^\| (G\d+) \| \[[^\]]+\]\(([^)]+)\) \|([^|]*)\| (active|retired) \|$/gm)) {
    const g = guidelines.get(m[1]);
    seen.add(m[1]);
    if (!g) { fail(GINDEX_REL, `row for ${m[1]}, which has no file`); continue; }
    if (`guidelines/${m[2]}` !== g.rel) fail(GINDEX_REL, `${m[1]} links to ${m[2]}, not ${path.basename(g.rel)}`);
    if (m[4] !== g.status) fail(GINDEX_REL, `${m[1]} status ${m[4]} differs from its file (${g.status})`);
    if (g.status === 'active' && Number(m[3].trim()) !== g.home) fail(GINDEX_REL, `${m[1]} home chapter ${m[3].trim()} differs from its file (${g.home})`);
  }
  for (const id of guidelines.keys()) if (!seen.has(id)) fail(GINDEX_REL, `no row for ${id}`);
}

// ---- links ----
let linkCount = 0;
const slugCache = new Map();
function slugsOf(rel) {
  if (!slugCache.has(rel)) {
    const text = files.get(rel) || '';
    slugCache.set(rel, new Set([...text.matchAll(/^#{1,6} (.+)$/gm)].map(m => slug(m[1]))));
  }
  return slugCache.get(rel);
}
for (const [rel, text] of files) {
  if (!rel.endsWith('.md')) continue;
  for (const m of text.matchAll(/\[[^\]]*\]\(([^)\s]+)\)/g)) {
    const target = m[1];
    if (/^(https?:|mailto:)/.test(target)) continue;
    linkCount++;
    const [filePart, anchor] = target.split('#');
    const targetRel = filePart
      ? path.posix.normalize(path.posix.join(path.posix.dirname(rel), filePart))
      : rel;
    if (assets.has(targetRel)) {
      if (anchor) fail(rel, `link to ${target}: an image or a diagram source has no headings`);
      continue;
    }
    if (!files.has(targetRel)) { fail(rel, `link to ${target}: no such file`); continue; }
    if (anchor && !slugsOf(targetRel).has(anchor)) fail(rel, `link to ${target}: no such heading`);
  }
}

// ---- images ----
// Every image a page shows has alt text and is a local image file. Every image file is shown by a page.
let imageCount = 0;
const shown = new Set();
for (const [rel, text] of files) {
  if (!rel.endsWith('.md')) continue;
  for (const m of text.matchAll(/!\[([^\]]*)\]\(([^)\s]+)\)/g)) {
    const [, alt, target] = m;
    imageCount++;
    if (!alt.trim()) fail(rel, `image ${target} has no alt text`);
    if (/^[a-z][a-z0-9+.-]*:/i.test(target)) { fail(rel, `image ${target} is not a local file`); continue; }
    const targetRel = path.posix.normalize(path.posix.join(path.posix.dirname(rel), target.split('#')[0]));
    if (!IMAGE_EXT.test(targetRel)) fail(rel, `image ${target} is not a png, jpg, gif or webp file`);
    shown.add(targetRel);
  }
}
for (const rel of assets) if (IMAGE_EXT.test(rel) && !shown.has(rel)) fail(rel, 'image shown by no page');

// ---- hygiene ----
// The content files carry no internal markers. SOURCES.md, which is not published, is the one place for them.
const INTERNAL = [
  [/\bD-\d{2}\b/, 'a decision number'],
  [/WORKING DEFAULT/, 'a working default marker (keep it in SOURCES.md)'],
  [/\bdocs\/(design|plan|product|research|handover)\b/, 'a workspace path'],
  [/\bCC-\d{2}\b/, 'a work item ID'],
  [/\b[COL]\d{2}\b/, 'an entity ID'],
  [/\bresearch file\b/i, 'a research pointer'],
  [/\bquestion \d+\b/i, 'a question marker'],
];
const STORE_CLAIMS = [
  [/built on Dataverse/i, 'a data-store claim'],
  [/Dataverse solution/i, 'a data-store claim'],
  [/\bplug-?ins?\b/i, 'an engine technology claim'],
  [/\bcustom APIs?\b/i, 'an engine technology claim'],
  [/\bgenerative pages?\b/i, 'a page technology claim'],
];
const PRESENT_CLAIM = /\bthe product (will|does|holds|keeps|records|runs|sends|stores|checks|tracks|flags|writes|creates|enforces|blocks)\b/i;
for (const [rel, text] of files) {
  const lines = text.split(/\r?\n/);
  lines.forEach((line, i) => {
    if (/[^\x00-\x7f]/.test(line)) fail(`${rel}:${i + 1}`, 'non-ASCII character');
  });
  if (rel === PRIVATE) continue;
  lines.forEach((line, i) => {
    for (const [re, what] of [...INTERNAL, ...STORE_CLAIMS]) if (re.test(line)) fail(`${rel}:${i + 1}`, `${what}: "${line.match(re)[0]}"`);
    if (PRESENT_CLAIM.test(line)) fail(`${rel}:${i + 1}`, `a present-tense product claim ("${line.match(PRESENT_CLAIM)[0]}"); say "is designed to"`);
  });
}

// ---- working default anchors in SOURCES.md ----
let anchorCount = 0;
if (!files.has(PRIVATE)) {
  if (!NO_SOURCES) fail(PRIVATE, 'missing (it holds the source pointers and the working default anchors); use --no-sources for a copy without it');
} else {
  files.get(PRIVATE).split(/\r?\n/).forEach((line, i) => {
    if (!line.includes('WORKING DEFAULT')) return;
    if (!line.startsWith('|')) return; // prose that names the marker
    const where = `${PRIVATE}:${i + 1}`;
    const marker = line.match(/WORKING DEFAULT \(open design question (\d+)\)/);
    if (!marker) { fail(where, 'malformed marker; use "WORKING DEFAULT (open design question N)"'); return; }
    if (!OPEN_QUESTIONS.has(Number(marker[1]))) fail(where, `question ${marker[1]} is not one of the open questions (${[...OPEN_QUESTIONS].join(', ')})`);
    const cells = line.split('|').map(c => c.trim());
    const file = (cells[2] || '').match(/^`([^`]+)`$/);
    const anchor = (cells[3] || '').match(/^`([^`]+)`$/);
    if (!file || !anchor) { fail(where, 'expected | marker | `file` | `anchor text` | change |'); return; }
    anchorCount++;
    if (!files.has(file[1])) fail(where, `no file ${file[1]}`);
    else if (!files.get(file[1]).includes(anchor[1])) fail(where, `anchor text not found in ${file[1]}: "${anchor[1]}"`);
  });
}

// ---- result ----
const retired = guidelines.size - activeG.size;
const anchorNote = files.has(PRIVATE) ? `${anchorCount} working default anchors` : 'no SOURCES.md, anchors not checked';
const summary = `${chapters.size} chapters, ${processes.size} processes, ${guidelines.size} guideline numbers (${activeG.size} active, ${retired} retired), ${linkCount} relative links, ${imageCount} images, ${anchorNote}, root ${ROOT}`;
if (!findings.length) {
  console.log(`Content check: clean. ${summary}.`);
  process.exit(0);
}
console.log(`Content check: ${findings.length} finding(s). ${summary}.`);
for (const f of findings.slice(0, 300)) console.log(f);
if (findings.length > 300) console.log(`  ... and ${findings.length - 300} more`);
process.exit(1);
