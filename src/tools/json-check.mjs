#!/usr/bin/env node
// JSON check for src/.
// 1. Every JSON file parses.
// 2. Every *.schema.json file is JSON Schema draft 2020-12 and compiles.
// 3. Files listed in tools/schema-map.json validate against their schema.
// 4. The examples at the root of each schema validate against that schema.
// Uses the pinned ajv in tools/node_modules. Run "npm ci" in tools/ first (tools/ci.mjs does it).
//
// Usage: node tools/json-check.mjs [--root <dir>] [--map <file>]
// Exit code: 0 clean, 1 problems found, 2 setup error.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { SRC, walk, globToRegExp, option } from './lib/files.mjs';

const DRAFT_2020_12 = 'https://json-schema.org/draft/2020-12/schema';

const args = process.argv.slice(2);
const root = path.resolve(option(args, '--root', SRC));
const mapFile = path.resolve(option(args, '--map', path.join(SRC, 'tools', 'schema-map.json')));

let Ajv2020;
let addFormats;
try {
  Ajv2020 = (await import('ajv/dist/2020.js')).default;
  addFormats = (await import('ajv-formats')).default;
} catch {
  console.error('JSON check: ajv is not installed. Run "npm ci" in src/tools first.');
  process.exit(2);
}

const problems = [];
const notes = [];

// ---- 1. parse every JSON file ----
const docs = new Map(); // rel path -> parsed value
for (const { full, rel } of walk(root)) {
  if (!rel.toLowerCase().endsWith('.json')) continue;
  let text = fs.readFileSync(full, 'utf8');
  if (text.charCodeAt(0) === 0xfeff) {
    notes.push(`${rel}: starts with a byte order mark; save as UTF-8 without BOM`);
    text = text.slice(1);
  }
  try {
    docs.set(rel, JSON.parse(text));
  } catch (e) {
    problems.push(`${rel}: does not parse: ${e.message}`);
  }
}

// ---- 2. compile every schema ----
const warnings = [];
const ajv = new Ajv2020({
  allErrors: true,
  strict: true, // unknown keywords are errors, so typos surface
  strictTypes: false,
  strictTuples: false,
  strictRequired: false,
  logger: { log() {}, warn: m => warnings.push(String(m)), error: m => warnings.push(String(m)) },
});
addFormats(ajv);

// Keys that start with "x-" are annotations (for example a proposal marker). Register them so strict mode allows them.
const annotationKeys = new Set();
const collectAnnotations = node => {
  if (Array.isArray(node)) { node.forEach(collectAnnotations); return; }
  if (node && typeof node === 'object') {
    for (const [k, v] of Object.entries(node)) {
      if (k.startsWith('x-')) annotationKeys.add(k);
      collectAnnotations(v);
    }
  }
};

const schemaKeys = new Map(); // rel path -> key used in ajv
const schemaRels = [...docs.keys()].filter(rel => rel.endsWith('.schema.json'));
for (const rel of schemaRels) collectAnnotations(docs.get(rel));
for (const k of [...annotationKeys].sort()) ajv.addKeyword(k);

const seenIds = new Map();
for (const rel of schemaRels) {
  const schema = docs.get(rel);
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) {
    problems.push(`${rel}: a schema must be a JSON object`);
    continue;
  }
  if (schema.$schema !== DRAFT_2020_12) {
    problems.push(`${rel}: "$schema" must be "${DRAFT_2020_12}"`);
    continue;
  }
  const key = typeof schema.$id === 'string' ? schema.$id : pathToFileURL(path.join(root, rel)).href;
  if (seenIds.has(key)) {
    problems.push(`${rel}: "$id" ${key} is also used by ${seenIds.get(key)}`);
    continue;
  }
  seenIds.set(key, rel);
  try {
    // Without its own $id, a schema gets its file URL as key, so relative $ref values resolve by path.
    if (typeof schema.$id === 'string') ajv.addSchema(schema);
    else ajv.addSchema(schema, key);
    schemaKeys.set(rel, key);
  } catch (e) {
    problems.push(`${rel}: cannot add schema: ${e.message}`);
  }
}

let compiled = 0;
for (const [rel, key] of schemaKeys) {
  try {
    ajv.getSchema(key);
    compiled++;
  } catch (e) {
    problems.push(`${rel}: does not compile: ${e.message}`);
    schemaKeys.delete(rel);
  }
}

// ---- 3. validate mapped files ----
let map = { entries: [] };
if (fs.existsSync(mapFile)) {
  try {
    map = JSON.parse(fs.readFileSync(mapFile, 'utf8'));
  } catch (e) {
    console.error(`JSON check: cannot read the schema map ${mapFile}: ${e.message}`);
    process.exit(2);
  }
} else {
  notes.push(`no schema map at ${mapFile}; only parse and compile checks ran`);
}

let validated = 0;
let pending = 0;
const mapped = new Set();
for (const entry of map.entries || []) {
  const [schemaRel, pointer] = String(entry.schema || '').split('#');
  const include = (entry.include || []).map(globToRegExp);
  const exclude = (entry.exclude || []).map(globToRegExp);
  const files = [...docs.keys()]
    .filter(rel => include.some(re => re.test(rel)) && !exclude.some(re => re.test(rel)));
  files.forEach(rel => mapped.add(rel));

  if (!docs.has(schemaRel)) {
    if (files.length) problems.push(`${schemaRel}: schema missing, but ${files.length} file(s) match its entry, for example ${files[0]}`);
    else { pending++; notes.push(`pending: ${schemaRel} (not written yet, no files match)`); }
    continue;
  }
  if (!schemaKeys.has(schemaRel)) continue; // already reported above

  const ref = schemaKeys.get(schemaRel) + (pointer ? '#' + pointer : '');
  let validate;
  try {
    validate = ajv.getSchema(ref);
  } catch (e) {
    problems.push(`${entry.schema}: cannot resolve: ${e.message}`);
    continue;
  }
  if (!validate) {
    problems.push(`${entry.schema}: cannot resolve the pointer`);
    continue;
  }
  for (const rel of files) {
    validated++;
    if (!validate(docs.get(rel))) {
      for (const err of (validate.errors || []).slice(0, 10)) {
        problems.push(`${rel}: ${err.instancePath || '(root)'} ${err.message} [${entry.schema} ${err.schemaPath}]`);
      }
      if ((validate.errors || []).length > 10) problems.push(`${rel}: ... and ${validate.errors.length - 10} more errors`);
    }
  }
}

// ---- 4. validate the examples each schema carries at its root ----
let examples = 0;
for (const [rel, key] of schemaKeys) {
  const schema = docs.get(rel);
  if (!Array.isArray(schema.examples)) continue;
  const validate = ajv.getSchema(key);
  schema.examples.forEach((example, i) => {
    examples++;
    if (!validate(example)) {
      for (const err of (validate.errors || []).slice(0, 5)) {
        problems.push(`${rel}: example ${i}: ${err.instancePath || '(root)'} ${err.message} [${err.schemaPath}]`);
      }
    }
  });
}

// Data files in schema-governed folders that no map entry covers.
const governed = /^(config\/(presets|build)|fixtures|mcp\/contracts)\//;
const unmapped = [...docs.keys()].filter(rel => governed.test(rel) && !rel.endsWith('.schema.json') && !mapped.has(rel));
if (unmapped.length) notes.push(`${unmapped.length} data file(s) have no schema map entry yet, for example ${unmapped[0]}`);

// ---- report ----
for (const w of warnings) notes.push(`ajv: ${w}`);
const summary = `${docs.size} JSON files parsed, ${compiled} of ${schemaRels.length} schemas compiled, ${validated} files validated, ${examples} schema examples validated, ${pending} map entries pending`;
for (const n of notes) console.log(`  note: ${n}`);
if (!problems.length) {
  console.log(`JSON check: clean. ${summary}, root ${root}.`);
  process.exit(0);
}
console.log(`JSON check: ${problems.length} problem(s). ${summary}, root ${root}.`);
for (const p of problems.slice(0, 200)) console.log(`  ${p}`);
if (problems.length > 200) console.log(`  ... and ${problems.length - 200} more`);
process.exit(1);
