#!/usr/bin/env node
// Writes the generated parts of the product documentation in docs/ from the contracts and the shipped configuration,
// so they never drift from the code:
//   docs/README.md          the guide per role: the role, its label and duties (config/presets/balanced/role.json)
//                           and the demo persona who plays it (adapters/azure/seed/demo-personas.json);
//   docs/mcp.md             the tool list by toolset, the AI write states, the AI rules, the acts that are not tools
//                           and what AI May Submit allows (mcp/contracts/tools.json, tool-policy.seed.json);
//   docs/api.md             the fixed error codes (mcp/contracts/errors.json);
//   docs/install.md         the human product roles that need an app role and a group;
//   docs/guide/<role>.md    the demo persona, and what the shipped Tool Policy lets the role call.
// Each generated part sits between two marker lines, and only the text between them changes:
//   <!-- generated: <name> (node tools/docs-gen.mjs) -->
//   <!-- end generated: <name> -->
//
// --check writes nothing. It exits 1 when a generated part differs from a fresh run, when a human role has no guide,
// or when a link in docs/ does not resolve, points at a heading that does not exist, or leaves src/. The docs are
// public: a link stays inside src/, and a path into the repository's docs/ folder names only what is published there.
//
// Usage, from src/: node tools/docs-gen.mjs [--check]
// Exit code: 0 done or up to date, 1 drift or a broken link, 2 an input is missing.
// No packages. Node 20 or later.
import fs from 'node:fs';
import path from 'node:path';
import { SRC } from './lib/files.mjs';

const CHECK = process.argv.slice(2).includes('--check');
const DOCS = path.join(SRC, 'docs');

function readJson(rel) {
  const full = path.join(SRC, rel);
  if (!fs.existsSync(full)) {
    console.error(`docs-gen: ${rel} is missing.`);
    process.exit(2);
  }
  return JSON.parse(fs.readFileSync(full, 'utf8'));
}

const tools = readJson('mcp/contracts/tools.json');
const policy = readJson('mcp/contracts/tool-policy.seed.json');
const errors = readJson('mcp/contracts/errors.json');
const roles = readJson('config/presets/balanced/role.json');
const personas = readJson('adapters/azure/seed/demo-personas.json');

const humanRoleKeys = tools.humanRoleKeys;
const roleRow = new Map(roles.rows.map(r => [r.key, r]));
const policyRow = new Map(policy.rows.map(r => [r.toolKey, r]));
const toolsetLabel = new Map(tools.toolsets.map(t => [t.key, t.label]));

// Text for people: the contracts cite design decisions (D-38) and logical model entities (C46) in brackets, which mean
// nothing to a reader of these pages, so those brackets are left out.
const plain = text => String(text ?? '')
  .replace(/\s*\([^()]*\bD-\d{2}\b[^()]*\)/g, '')
  .replace(/\s*\([COL]\d{2}\)/g, '');
// Table cells: one line, no pipe.
const cell = text => plain(text).replace(/\r?\n/g, ' ').replace(/\|/g, '/').trim();
const code = text => '`' + text + '`';
const personOnly = tool => Array.isArray(tool.entries) && tool.entries.length === 1 && tool.entries[0] === 'person';
const served = tool => {
  const row = policyRow.get(tool.name);
  return Boolean(row && row.isEnabled && String(row.release).toLowerCase() !== 'later' && tool.release === 'R1');
};
const whereServed = tool => (!served(tool) ? 'Not served yet (ships disabled)' : personOnly(tool) ? 'Web app only' : 'Web app and AI clients');

// The demo personas who play a role: a persona's label ends with the role's label ("Lena Okoro, requester").
function personasOf(roleKey) {
  const label = (roleRow.get(roleKey)?.label ?? roleKey).toLowerCase();
  return personas.personas
    .filter(p => String(p.label).toLowerCase().endsWith(', ' + label))
    .map(p => ({ name: p.name, person: String(p.label).slice(0, String(p.label).length - label.length - 2) }));
}
const personaText = roleKey => {
  const found = personasOf(roleKey);
  if (!found.length) return 'No demo persona plays this role.';
  return found.map(p => `${p.person} (${code(p.name)})`).join(' and ');
};

// ---- the generated parts ----

function guidesTable() {
  const lines = ['| Guide | Role key | What the role does (Balanced) | Demo persona |', '|---|---|---|---|'];
  for (const key of humanRoleKeys) {
    const role = roleRow.get(key);
    lines.push(`| [${cell(role?.label ?? key)}](guide/${key}.md) | ${code(key)} | ${cell(role?.duties)} | ${cell(personaText(key))} |`);
  }
  return lines.join('\n');
}

function toolList() {
  const out = [];
  const all = tools.tools;
  const reads = all.filter(t => t.access === 'read').length;
  const writes = all.length - reads;
  const people = all.filter(personOnly).map(t => code(t.name));
  const notServed = all.filter(t => !served(t)).map(t => code(t.name));
  out.push(`${all.length} tools: ${reads} read and ${writes} write. ${people.length} are served only to a person in the web app: ${people.join(', ')}. ` +
    (notServed.length ? `${notServed.join(', ')} ${notServed.length === 1 ? 'ships' : 'ship'} disabled, for a later release.` : 'Every tool is served.'));
  out.push('');
  out.push('"Who may call" is the shipped Tool Policy default, which applies while the configuration holds no Tool Policy rows (Balanced holds none). A configuration that adds Tool Policy rows replaces the whole set, so it lists every tool; its Max AI State can only tighten the states each contract allows. The engine still checks the relation a row names (an owner of the application, the person the request asks).');
  for (const set of tools.toolsets) {
    const inSet = all.filter(t => t.toolset === set.key).sort((a, b) => a.number - b.number);
    if (!inSet.length) continue;
    out.push('');
    out.push(`### ${set.label} (${code(set.key)})`);
    out.push('');
    out.push(cell(set.covers) + '.');
    out.push('');
    out.push('| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |');
    out.push('|---|---|---|---|---|---|');
    for (const t of inSet) {
      out.push(`| ${t.number} | ${code(t.name)} | ${cell(t.question)} | ${cell(t.aiWriteStateText)} | ${cell(t.defaultRoles?.text)} | ${whereServed(t)} |`);
    }
  }
  return out.join('\n');
}

function aiStates() {
  const lines = ['| State | What it means |', '|---|---|'];
  for (const s of tools.aiWriteStates) lines.push(`| ${code(s.state)} | ${cell(s.meaning)} |`);
  return lines.join('\n');
}

function aiRules() {
  return tools.aiRules.map((rule, i) => `${i + 1}. ${cell(rule)}`).join('\n');
}

function notTools() {
  const lines = ['| Act | Why it is not a tool | Where it happens instead |', '|---|---|---|'];
  for (const n of tools.notTools) lines.push(`| ${cell(n.act)} | ${cell(n.why)} | ${cell(n.insteadWhere)} |`);
  return lines.join('\n');
}

function aiMaySubmit() {
  const lines = ['| Tool | Subject kind | An AI client may submit it |', '|---|---|---|'];
  for (const t of tools.tools.filter(x => x.aiMaySubmit).sort((a, b) => a.number - b.number)) {
    for (const [kind, may] of Object.entries(t.aiMaySubmit)) lines.push(`| ${code(t.name)} | ${code(kind)} | ${may ? 'Yes' : 'No: a person submits it'} |`);
  }
  return lines.join('\n');
}

function errorTable() {
  const lines = ['| Code | HTTP status | Retry the same call? | What it means | What the client does |', '|---|---|---|---|---|'];
  for (const e of errors.codes) {
    lines.push(`| ${code(e.code)} | ${e.httpStatus} | ${e.retryable ? 'Yes, later' : 'No'} | ${cell(e.meaning)} | ${cell(e.clientAction)} |`);
  }
  return lines.join('\n');
}

function humanRoles() {
  return humanRoleKeys.map(k => code(k)).join(', ');
}

function roleTools(roleKey) {
  const out = [];
  out.push(`- **Demo persona:** ${personaText(roleKey)}.`);
  const mine = tools.tools.filter(t => served(t) && (policyRow.get(t.name)?.allowedRoleKeys ?? []).includes(roleKey));
  const writes = mine.filter(t => t.access === 'write').sort((a, b) => a.number - b.number);
  const allReads = tools.tools.filter(t => served(t) && t.access === 'read');
  const reads = mine.filter(t => t.access === 'read');
  const restricted = reads.filter(t => (policyRow.get(t.name)?.allowedRoleKeys ?? []).length < humanRoleKeys.length).sort((a, b) => a.number - b.number);
  out.push(`- **Reads:** ${reads.length} of the ${allReads.length} read tools` +
    (restricted.length ? `, among them the ones only some roles get: ${restricted.map(t => code(t.name)).join(', ')}.` : '. It gets none of the reads kept to a few roles.'));
  out.push('');
  if (!writes.length) {
    out.push('The shipped Tool Policy gives this role no write tool.');
    return out.join('\n');
  }
  out.push(`The shipped Tool Policy lets this role call ${writes.length} write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.`);
  out.push('');
  out.push('| No. | Tool | What it does | Who exactly | Served |');
  out.push('|---|---|---|---|---|');
  for (const t of writes) out.push(`| ${t.number} | ${code(t.name)} | ${cell(t.question)} | ${cell(t.defaultRoles?.text)} | ${whereServed(t)} |`);
  return out.join('\n');
}

// file (relative to docs/) -> { part name -> text }
const parts = new Map([
  ['README.md', { guides: guidesTable() }],
  ['mcp.md', { 'tool-list': toolList(), 'ai-states': aiStates(), 'ai-rules': aiRules(), 'not-tools': notTools(), 'ai-may-submit': aiMaySubmit() }],
  ['api.md', { errors: errorTable() }],
  ['install.md', { 'human-roles': humanRoles() }],
]);
for (const key of humanRoleKeys) parts.set(`guide/${key}.md`, { 'role-tools': roleTools(key) });

// ---- write or check ----

let problems = 0;
const problem = message => { problems++; console.error(`  ${message}`); };
const start = name => `<!-- generated: ${name} (node tools/docs-gen.mjs) -->`;
const end = name => `<!-- end generated: ${name} -->`;

for (const [rel, named] of parts) {
  const full = path.join(DOCS, rel);
  if (!fs.existsSync(full)) {
    problem(`docs/${rel} is missing`);
    continue;
  }
  const before = fs.readFileSync(full, 'utf8');
  const newline = before.includes('\r\n') ? '\r\n' : '\n';
  let text = before.replace(/\r\n/g, '\n');
  for (const [name, body] of Object.entries(named)) {
    const a = text.indexOf(start(name));
    const b = text.indexOf(end(name));
    if (a < 0 || b < a) {
      problem(`docs/${rel} has no generated part "${name}" (the two marker lines)`);
      continue;
    }
    // A part inside a list item keeps the indent of its start marker, so the list stays one list.
    const indent = /[ \t]*$/.exec(text.slice(0, a))[0];
    const indented = body.split('\n').map(line => (line ? indent + line : line)).join('\n');
    text = text.slice(0, a) + start(name) + '\n' + indented + '\n' + indent + text.slice(b);
  }
  const after = text.replace(/\n/g, newline);
  if (after !== before) {
    if (CHECK) problem(`docs/${rel} is out of date: run node tools/docs-gen.mjs`);
    else {
      fs.writeFileSync(full, after);
      console.log(`  wrote docs/${rel}`);
    }
  }
}

// ---- links ----

// GitHub's heading anchors: lower case, punctuation dropped, spaces to hyphens, repeats numbered.
function anchors(markdown) {
  const seen = new Map();
  const found = new Set();
  let fence = false;
  for (const line of markdown.split(/\r?\n/)) {
    if (/^\s*```/.test(line)) { fence = !fence; continue; }
    if (fence) continue;
    const m = /^#{1,6}\s+(.*?)\s*#*\s*$/.exec(line);
    if (!m) continue;
    let slug = m[1].toLowerCase().replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-');
    const n = seen.get(slug) ?? 0;
    seen.set(slug, n + 1);
    if (n) slug = `${slug}-${n}`;
    found.add(slug);
  }
  return found;
}

// A path into the repository's own docs/ folder may name only what the public repository publishes there. A path inside
// src/docs (docs/guide/admin.md, say) names one of its own entries, and a path with a folder before it (pages/docs/...,
// a URL) is not a docs/ path of the repository.
const PUBLIC_DOCS = new Set(['overview.md', 'microsoft-fit.md', 'architecture', 'build-log', 'decisions', 'diagrams', 'design']);
const PUBLIC_DESIGN = new Set(['step2', 'systems']);
const OWN_DOCS = new Set(fs.readdirSync(DOCS));
function outsidePublicDocs(line) {
  for (const m of line.matchAll(/(?<![\w./-])docs\/([\w.-]+)(?:\/([\w.-]+))?/g)) {
    const [whole, first, second] = m;
    if (OWN_DOCS.has(first)) continue;
    if (!PUBLIC_DOCS.has(first)) return whole;
    if (first === 'design' && second && !PUBLIC_DESIGN.has(second)) return whole;
  }
  return null;
}

function* markdownFiles(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => (a.name < b.name ? -1 : 1))) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) yield* markdownFiles(full);
    else if (e.name.endsWith('.md')) yield full;
  }
}

let links = 0;
for (const file of markdownFiles(DOCS)) {
  const rel = path.relative(SRC, file).split(path.sep).join('/');
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  let fence = false;
  lines.forEach((line, i) => {
    if (/^\s*```/.test(line)) { fence = !fence; return; }
    if (fence) return;
    const outside = outsidePublicDocs(line);
    if (outside) problem(`${rel}:${i + 1} names a docs/ path the public repository does not publish ("${outside}")`);
    for (const m of line.matchAll(/\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
      const target = m[1];
      if (/^(https?:|mailto:)/i.test(target)) continue;
      links++;
      const [pathPart, anchor] = target.split('#');
      const resolved = pathPart ? path.resolve(path.dirname(file), decodeURIComponent(pathPart)) : file;
      const inside = path.relative(SRC, resolved);
      if (inside.startsWith('..') || path.isAbsolute(inside)) {
        problem(`${rel}:${i + 1} links outside src/: ${target}`);
        continue;
      }
      if (!fs.existsSync(resolved)) {
        problem(`${rel}:${i + 1} links to a file that does not exist: ${target}`);
        continue;
      }
      if (anchor && resolved.endsWith('.md') && !anchors(fs.readFileSync(resolved, 'utf8')).has(anchor)) {
        problem(`${rel}:${i + 1} links to a heading that does not exist: ${target}`);
      }
    }
  });
}

if (problems) {
  console.error(`docs-gen: ${problems} problem(s).`);
  process.exit(1);
}
console.log(`docs-gen: ${parts.size} files ${CHECK ? 'up to date' : 'generated'}, ${links} links in docs/ resolve.`);
