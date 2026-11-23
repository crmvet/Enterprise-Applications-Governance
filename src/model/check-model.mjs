#!/usr/bin/env node
// Model check for model/logical-model.json.
// 1. The model validates against model/logical-model.schema.json (draft 2020-12).
// 2. Every relationship target exists, and every reference column has its relationship.
// 3. Every CFG reference points to a configuration entity. OPS points to an operational entity. PR points to O05.
// 4. No duplicate names: entity IDs and names, columns per entity (with its column sets), relationship IDs,
//    contract keys and value list keys.
// 5. Keys, constraints, subjects and meaning copies name columns that exist.
// 6. The counts block matches the entities.
// Uses the pinned ajv in tools/node_modules. Run "npm ci" in tools/ first (tools/ci.mjs does it).
//
// Usage: node model/check-model.mjs [--model <file>] [--schema <file>]
// Exit code: 0 clean, 1 problems found, 2 setup error.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { SRC, option } from '../tools/lib/files.mjs';

const args = process.argv.slice(2);
const modelFile = path.resolve(option(args, '--model', path.join(SRC, 'model', 'logical-model.json')));
const schemaFile = path.resolve(option(args, '--schema', path.join(SRC, 'model', 'logical-model.schema.json')));

// Bare names Microsoft already uses. Entity names avoid them (document 20, section 3.7).
const AVOID = new Set(['Environment', 'Solution', 'Team', 'Application', 'Pipeline', 'Approval', 'Inventory', 'Catalog', 'Registry', 'Release']);

const problems = [];
const notes = [];

function readJson(file, label) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
  } catch (e) {
    console.error(`Model check: cannot read the ${label} ${file}: ${e.message}`);
    process.exit(2);
  }
}
const model = readJson(modelFile, 'model');
const schema = readJson(schemaFile, 'schema');

// ---- 1. schema ----
// ajv lives in tools/node_modules. Resolve it from there, with the same options as tools/json-check.mjs.
let Ajv2020;
let addFormats;
try {
  const requireFromTools = createRequire(path.join(SRC, 'tools', 'package.json'));
  const ajvModule = requireFromTools('ajv/dist/2020');
  const formatsModule = requireFromTools('ajv-formats');
  Ajv2020 = ajvModule.default || ajvModule;
  addFormats = formatsModule.default || formatsModule;
} catch {
  console.error('Model check: ajv is not installed. Run "npm ci" in src/tools first.');
  process.exit(2);
}
const ajv = new Ajv2020({ allErrors: true, strict: true, strictTypes: false, strictTuples: false, strictRequired: false });
addFormats(ajv);
let validate;
try {
  validate = ajv.compile(schema);
} catch (e) {
  console.error(`Model check: the schema does not compile: ${e.message}`);
  process.exit(2);
}
if (!validate(model)) {
  for (const err of (validate.errors || []).slice(0, 25)) {
    problems.push(`schema: ${err.instancePath || '(root)'} ${err.message} [${err.schemaPath}]`);
  }
  if (validate.errors.length > 25) problems.push(`schema: ... and ${validate.errors.length - 25} more errors`);
  report(false);
}

// ---- 2. indexes and duplicates ----
const dupes = (list, label) => {
  const seen = new Set();
  for (const item of list) {
    if (seen.has(item)) problems.push(`duplicate ${label}: ${item}`);
    seen.add(item);
  }
};
const entities = model.entities;
dupes(entities.map(e => e.id), 'entity ID');
dupes(entities.map(e => e.name), 'entity name');
const byId = new Map(entities.map(e => [e.id, e]));

dupes(model.columnSets.map(s => s.name), 'column set');
const sets = new Map(model.columnSets.map(s => [s.name, s]));
for (const s of model.columnSets) dupes(s.attributes.map(a => a.name), `column in set ${s.name}`);

const contracts = new Map();
dupes(model.meaningContracts.contracts.map(c => c.key), 'meaning contract');
for (const c of model.meaningContracts.contracts) contracts.set(c.key, c);
for (const c of contracts.values()) {
  for (const u of c.unionOf || []) if (!contracts.has(u)) problems.push(`contract ${c.key}: unionOf names unknown contract ${u}`);
}
// Every value a meaning column may hold. A union contract holds the values of its members.
const contractValues = key => {
  const c = contracts.get(key);
  if (!c) return [];
  return c.unionOf ? c.unionOf.flatMap(contractValues) : c.values;
};

dupes(model.valueLists.map(v => v.key), 'value list');
const valueLists = new Set(model.valueLists.map(v => v.key));

const relIds = [...model.sharedRelationships.map(r => r.id), ...entities.flatMap(e => e.relationships.map(r => r.id))];
dupes(relIds, 'relationship ID');

// ---- 3. attribute checks, shared by column sets and entities ----
const statusRank = { adopted: 0, proposal: 1, later: 2 };
function checkAttribute(where, a, owner) {
  if (a.type === 'ref') {
    const target = byId.get(a.target);
    if (!target) problems.push(`${where}: target ${a.target} does not exist`);
    else if (a.ref === 'CFG' && target.kind !== 'configuration') problems.push(`${where}: CFG reference to ${a.target} ${target.name}, which is ${target.kind}`);
    else if (a.ref === 'OPS' && target.kind !== 'operational') problems.push(`${where}: OPS reference to ${a.target} ${target.name}, which is ${target.kind}`);
    if (a.target === 'C07' && !a.valueList) {
      if (owner && owner.status === 'later') notes.push(`${where}: later entity names no value list yet`);
      else problems.push(`${where}: a List Value reference must name its value list (document 22, section 2.2, rule 6)`);
    }
  }
  if (a.keyOf && a.keyOf !== 'configuration') {
    for (const id of a.keyOf) {
      const target = byId.get(id);
      if (!target) problems.push(`${where}: keyOf names ${id}, which does not exist`);
      else if (target.kind !== 'configuration') problems.push(`${where}: CFG key list names ${id} ${target.name}, which is ${target.kind}`);
    }
  }
  if (a.valueList && !valueLists.has(a.valueList)) problems.push(`${where}: value list ${a.valueList} is not declared in valueLists`);
  if (a.type === 'meaning') {
    // WORKING DEFAULT (open design question 1): the engine contract is a short fixed list of meanings.
    // If the answer is no, meaning columns become configuration references and this check changes with them.
    const c = contracts.get(a.meaning);
    if (!c) problems.push(`${where}: meaning contract ${a.meaning} does not exist`);
    else if (owner && statusRank[c.status] > statusRank[a.status || owner.status]) {
      problems.push(`${where}: uses ${c.status} contract ${c.key} from a ${a.status || owner.status} column`);
    }
  }
}

for (const s of model.columnSets) {
  for (const a of s.attributes) checkAttribute(`set ${s.name} / ${a.name}`, a, null);
}

// Shared relationships: one per reference column of a column set.
for (const r of model.sharedRelationships) {
  const where = `${r.id} (set ${r.columnSet} / ${r.column})`;
  const set = sets.get(r.columnSet);
  const a = set && set.attributes.find(x => x.name === r.column);
  if (!set) problems.push(`${where}: no column set ${r.columnSet}`);
  else if (!a) problems.push(`${where}: no such column in the set`);
  else if (a.type !== 'ref' || a.target !== r.target) problems.push(`${where}: column points to ${a.target}, relationship to ${r.target}`);
  if (!byId.has(r.target)) problems.push(`${where}: target ${r.target} does not exist`);
}
for (const s of model.columnSets) {
  for (const a of s.attributes.filter(x => x.type === 'ref')) {
    const n = model.sharedRelationships.filter(r => r.columnSet === s.name && r.column === a.name).length;
    if (n !== 1) problems.push(`set ${s.name} / ${a.name}: ${n} shared relationships, expected 1`);
  }
}

// ---- 4. entity checks ----
const leftSide = required => (required === 'Y' ? '1' : '0..1');
const cascadeParents = new Map();
const markers = new Map();
const mark = text => {
  const m = /^WORKING DEFAULT \(open design question (\d+)\)/.exec(text || '');
  if (m) markers.set(m[1], (markers.get(m[1]) || 0) + 1);
};
mark(model.meaningContracts.workingDefault);
for (const c of contracts.values()) mark(c.workingDefault);

for (const e of entities) {
  const at = `${e.id} ${e.name}`;
  mark(e.workingDefault);
  if (AVOID.has(e.name)) problems.push(`${at}: bare name collides with a Microsoft name (document 20, section 3.7)`);

  // Column sets fit the entity kind.
  if (e.kind === 'operational' && !e.storedIn && !e.columnSets.includes('operational')) problems.push(`${at}: an operational entity needs the operational column set`);
  if (e.kind === 'configuration' && e.columnSets.includes('operational')) problems.push(`${at}: a configuration entity cannot use the operational column set`);
  if (e.kind === 'operational' && e.columnSets.includes('taxonomy')) problems.push(`${at}: an operational entity cannot use the taxonomy column set`);
  for (const s of e.columnSets) if (!sets.has(s)) problems.push(`${at}: unknown column set ${s}`);

  if (e.storedIn) {
    const host = byId.get(e.storedIn);
    if (!host) problems.push(`${at}: storedIn ${e.storedIn} does not exist`);
    else if (host.id === e.id || host.kind !== e.kind) problems.push(`${at}: storedIn must name another entity of the same kind`);
  }
  if (e.historyClass === 'A' && e.deletePolicy !== 'never') problems.push(`${at}: class A rows are never deleted, so deletePolicy must be never`);

  // Columns: own attributes plus the column sets. A repeat of a set column must say it restates it.
  const setColumns = new Map();
  for (const s of e.columnSets) for (const a of (sets.get(s) || { attributes: [] }).attributes) setColumns.set(a.name, { set: s, attr: a });
  dupes(e.attributes.map(a => `${e.id} / ${a.name}`), 'column');
  for (const a of e.attributes) {
    const inSet = setColumns.get(a.name);
    if (inSet && a.restates !== inSet.set) problems.push(`${at} / ${a.name}: duplicates a column of set ${inSet.set} without restates`);
    if (!inSet && a.restates) problems.push(`${at} / ${a.name}: restates ${a.restates}, but that set has no such column here`);
  }
  const columns = new Map([...setColumns].map(([n, v]) => [n, v.attr]));
  for (const a of e.attributes) columns.set(a.name, a);
  const has = name => columns.has(name);

  for (const a of e.attributes) {
    const where = `${at} / ${a.name}`;
    mark(a.workingDefault);
    checkAttribute(where, a, e);
    if (a.storedIn && !has(a.storedIn)) problems.push(`${where}: storedIn ${a.storedIn} is not a column here`);
    if (a.meaningCopyOf) {
      const src = columns.get(a.meaningCopyOf);
      if (!src) problems.push(`${where}: meaning copy of ${a.meaningCopyOf}, which is not a column here`);
      else if (src.type !== 'ref' || !['C05', 'C38'].includes(src.target)) problems.push(`${where}: a meaning copy follows a Status (C05) or Actor Kind (C38) reference`);
      else if (src.target === 'C05' && !contractValues('status-meaning').length) problems.push(`${where}: no status-meaning contract to check against`);
      else if (src.target === 'C05' && !(contracts.get('status-meaning').unionOf || []).includes(a.meaning)) problems.push(`${where}: contract ${a.meaning} is not a status meaning`);
      else if (src.target === 'C38' && a.meaning !== 'actor-kind') problems.push(`${where}: a copy of an Actor Kind uses the actor-kind contract`);
    }
  }

  // Every reference column has exactly one relationship, and every relationship matches its column.
  for (const a of e.attributes.filter(x => x.type === 'ref')) {
    const n = e.relationships.filter(r => r.column === a.name).length;
    if (n !== 1) problems.push(`${at} / ${a.name}: ${n} relationships, expected 1`);
  }
  for (const r of e.relationships) {
    const where = `${at} ${r.id} (${r.column})`;
    const a = e.attributes.find(x => x.name === r.column);
    if (!byId.has(r.target)) problems.push(`${where}: target ${r.target} does not exist`);
    if (!a) { problems.push(`${where}: no such column on this entity`); continue; }
    if (a.type !== 'ref' || a.target !== r.target) problems.push(`${where}: column points to ${a.target}, relationship to ${r.target}`);
    if (r.cardinality.split(' to ')[0] !== leftSide(a.required)) problems.push(`${where}: cardinality ${r.cardinality} does not fit required ${a.required}`);
    if (r.catalogId && !r.id.startsWith(r.catalogId)) problems.push(`${where}: id does not start with its catalog id ${r.catalogId}`);
    if (r.target === e.id && r.delete !== 'Restrict') problems.push(`${where}: a self reference is always Restrict`);
    if (r.delete === 'Cascade') {
      cascadeParents.set(e.id, [...(cascadeParents.get(e.id) || []), r.target]);
      const parent = byId.get(r.target);
      if (parent && parent.deletePolicy !== 'draft-only') problems.push(`${where}: Cascade needs a parent that may be deleted while a draft`);
    }
  }

  // Meaning copies: an operational row with a configurable status also carries its engine meaning.
  if (e.kind === 'operational' && e.status !== 'later') {
    for (const a of e.attributes.filter(x => x.type === 'ref' && x.target === 'C05')) {
      if (!e.attributes.some(x => x.meaningCopyOf === a.name)) problems.push(`${at} / ${a.name}: no meaning copy (document 22, section 2.1.4)`);
    }
  }

  const keySigs = [];
  for (const k of e.keys) {
    for (const c of k.columns) if (!has(c)) problems.push(`${at}: ${k.kind} key column ${c} does not exist`);
    keySigs.push(`${e.id} key ${[...k.columns].sort().join(' + ')}`);
  }
  dupes(keySigs, 'key');
  for (const c of e.toolIds || []) if (!has(c)) problems.push(`${at}: tool ID column ${c} does not exist`);
  for (const k of e.constraints || []) for (const c of k.columns) if (!has(c)) problems.push(`${at}: ${k.kind} column ${c} does not exist`);

  if (e.subjects) {
    if (!has(e.subjects.kindColumn)) problems.push(`${at}: subject kind column ${e.subjects.kindColumn} does not exist`);
    const allowed = new Set(contractValues('subject-kind'));
    for (const o of e.subjects.options) {
      const a = columns.get(o.column);
      if (!allowed.has(o.value)) problems.push(`${at}: subject kind ${o.value} is not in the subject-kind contract`);
      if (!a || a.type !== 'ref') { problems.push(`${at}: subject column ${o.column} is not a reference here`); continue; }
      const target = byId.get(a.target);
      if (target && target.name !== o.value) problems.push(`${at}: subject ${o.value} uses ${o.column}, which points to ${target.name}`);
    }
  }
}

// Dataverse allows one cascading parent per child table (document 22, section 6, note T2).
for (const [child, parents] of cascadeParents) {
  if (parents.length > 1) problems.push(`${child}: ${parents.length} cascading parents, at most 1 allowed`);
}

// ---- 5. counts ----
// An entity counts as a document 22 entity when its source starts with "document 22".
const inDoc22 = e => e.source.startsWith('document 22') && e.status !== 'later';
const actual = {
  entities: entities.length,
  document22FirstRelease: entities.filter(inDoc22).length,
  document22Configuration: entities.filter(e => inDoc22(e) && e.kind === 'configuration').length,
  document22Operational: entities.filter(e => inDoc22(e) && e.kind === 'operational').length,
  adoptedOutsideDocument22: entities.filter(e => e.status === 'adopted' && !inDoc22(e)).length,
  proposals: entities.filter(e => e.status === 'proposal').length,
  later: entities.filter(e => e.status === 'later').length,
};
for (const [k, v] of Object.entries(actual)) {
  if (model.counts[k] !== v) problems.push(`counts.${k} is ${model.counts[k]}, the entities give ${v}`);
}

report(true);

// ---- report ----
function report(ranCrossChecks) {
  const lines = [];
  if (ranCrossChecks) {
    const list = pred => entities.filter(pred).map(e => e.id).join(', ');
    const attrs = entities.reduce((n, e) => n + e.attributes.length, 0);
    const rels = entities.reduce((n, e) => n + e.relationships.length, 0);
    const cfgRefs = entities.reduce((n, e) => n + e.attributes.filter(a => a.ref === 'CFG').length, 0)
      + model.columnSets.reduce((n, s) => n + s.attributes.filter(a => a.ref === 'CFG').length, 0);
    lines.push(`  entities: ${actual.entities} (configuration ${entities.filter(e => e.id.startsWith('C')).length}, operational ${entities.filter(e => e.id.startsWith('O')).length}, later ${actual.later})`);
    lines.push(`  document 22 first release: ${actual.document22FirstRelease} (configuration ${actual.document22Configuration}, operational ${actual.document22Operational})`);
    lines.push(`  adopted outside document 22: ${actual.adoptedOutsideDocument22} (${list(e => e.status === 'adopted' && !inDoc22(e))})`);
    lines.push(`  proposals: ${actual.proposals} (${list(e => e.status === 'proposal')})`);
    lines.push(`  later: ${actual.later} (${list(e => e.status === 'later')})`);
    lines.push(`  attributes: ${attrs} own, ${model.columnSets.reduce((n, s) => n + s.attributes.length, 0)} in column sets; CFG references: ${cfgRefs}`);
    lines.push(`  relationships: ${rels} on entities, ${model.sharedRelationships.length} shared; meaning contracts: ${contracts.size}; value lists: ${valueLists.size}`);
    lines.push(`  working default markers: ${[...markers].sort((a, b) => a[0] - b[0]).map(([q, n]) => `question ${q} x${n}`).join(', ') || 'none'}`);
  }
  for (const n of notes) console.log(`  note: ${n}`);
  if (!problems.length) {
    console.log(`Model check: clean. ${path.relative(SRC, modelFile).split(path.sep).join('/')} validates and every cross-check passes.`);
    for (const l of lines) console.log(l);
    process.exit(0);
  }
  console.log(`Model check: ${problems.length} problem(s)${ranCrossChecks ? '' : ' (schema failed, cross-checks skipped)'}.`);
  for (const p of problems.slice(0, 200)) console.log(`  ${p}`);
  if (problems.length > 200) console.log(`  ... and ${problems.length - 200} more`);
  for (const l of lines) console.log(l);
  process.exit(1);
}
