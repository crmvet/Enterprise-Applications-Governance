#!/usr/bin/env node
// Model generators: from model/logical-model.json, both physical names for every entity and
// column, a column map, a draft Azure SQL migration and a draft Dataverse metadata description.
// Status of every output: draft, platform under review (D-22). Text files only. Nothing is applied to any database or
// environment, and nothing here opens a connection. No packages: Node 20 or later.
//
// Usage:
//   node model/generate.mjs names [--entity <id>] [--model <file>]    print both physical names for every column
//   node model/generate.mjs write [--out <dir>] [--model <file>]      write the three files (default model/generated)
// Exit code: 0 done, 1 the model cannot be bound, 2 usage or setup error.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { SRC, option } from '../tools/lib/files.mjs';
import { readModel, bind, BindError } from './generators/physical.mjs';
import { renderNames } from './generators/names.mjs';
import { renderAll } from './generators/outputs.mjs';

const USAGE = 'Usage: node model/generate.mjs names [--entity <id>] [--model <file>] | write [--out <dir>] [--model <file>]';

function load(modelFile) {
  let model;
  try {
    model = readModel(modelFile);
  } catch (e) {
    console.error(`Generate: cannot read the model ${modelFile}: ${e.message}`);
    process.exit(2);
  }
  try {
    return bind(model);
  } catch (e) {
    if (e instanceof BindError) {
      console.error(`Generate: the model cannot be bound. ${e.problems.length} problem(s):`);
      for (const p of e.problems) console.error(`  ${p}`);
      process.exit(1);
    }
    throw e;
  }
}

function main() {
  const args = process.argv.slice(2);
  const command = args[0];
  const modelFile = path.resolve(option(args, '--model', path.join(SRC, 'model', 'logical-model.json')));
  if (command === 'names') {
    const bound = load(modelFile);
    const entity = option(args, '--entity', null);
    if (entity && !bound.byId.has(entity)) {
      console.error(`Generate: no entity ${entity}`);
      process.exit(2);
    }
    // A reader that stops early (for example "| head") is not an error.
    process.stdout.on('error', e => { if (e.code === 'EPIPE') process.exit(0); throw e; });
    process.stdout.write(renderNames(bound, { entity }).text);
    return;
  }
  if (command === 'write') {
    const out = path.resolve(option(args, '--out', path.join(SRC, 'model', 'generated')));
    const bound = load(modelFile);
    let rendered;
    try {
      rendered = renderAll(bound);
    } catch (e) {
      console.error(`Generate: ${e.message}`);
      process.exit(1);
    }
    fs.mkdirSync(out, { recursive: true });
    console.log(`Generate: ${rendered.files.length} files to ${out}. Status: draft, platform under review (D-22). Nothing was applied.`);
    for (const f of rendered.files) {
      fs.writeFileSync(path.join(out, f.name), f.text, 'utf8');
      const sha = crypto.createHash('sha256').update(f.text).digest('hex');
      console.log(`  ${f.name.padEnd(34)} ${String(Buffer.byteLength(f.text)).padStart(8)} bytes  sha256 ${sha}`);
    }
    const s = rendered.sqlStats;
    const names = renderNames(bound);
    console.log(`  names: ${names.entities} entities, ${names.columns} columns`);
    console.log(`  azure sql: ${s.tables} tables (${s.temporal} temporal, ${s['updatable-ledger']} updatable ledger, ${s['append-only-ledger']} append-only ledger), ${s.columns} columns, ${s.uniqueConstraints} unique constraints, ${s.filteredUniqueIndexes} filtered unique indexes, ${s.checks} checks, ${s.foreignKeys} foreign keys, ${s.views} view`);
    const dv = JSON.parse(rendered.files[2].text).counts;
    console.log(`  dataverse: ${dv.tables} tables, ${dv.columns} columns, ${dv.lookups} lookups, ${dv.choiceColumns} choice columns on ${dv.globalChoices} global choices, ${dv.alternateKeys} alternate keys, ${dv.autonumberColumns} autonumber columns`);
    console.log(`  review: ${bound.requiredCycles.length} required reference cycle(s), ${bound.requiredSelfReferences.length} required self reference(s), ${bound.longDataverseNames.length} long Dataverse name(s), ${bound.warnings.length} warning(s)`);
    return;
  }
  console.error(USAGE);
  process.exit(2);
}

main();
