// Draft Azure SQL migration: the first schema migration from the logical model, bound as document 22,
// section 11 says. Text only. Nothing here connects to a database; the migration runner comes later.
import { DRAFT, GENERATOR, MODEL_PATH, NOT_APPLIED, AZURE_HISTORY_TEXT, AZURE_DERIVED_VIEWS, AZURE_NAME_MAX, pascal, pascalKey, wrap } from './physical.mjs';

const WIDTH = 118;
const RULE = `-- ${'-'.repeat(WIDTH - 3)}`;
const BIN2 = 'COLLATE Latin1_General_BIN2';

// Binding notes from the design that one table carries.
const TABLE_NOTES = {
  O05: 'Document 22, section 11.2 asks for a short history retention, so that erased labels age out. No period is set here. The setup sets HISTORY_RETENTION_PERIOD once a period is picked (REVIEW).',
  O09: 'Superseded By has no column here. The view [ops].[DecisionCurrent] computes it, so a frozen decision is never updated (document 22, sections 8.1 and 11.2).',
  C46: 'An updatable ledger table, as document 22, sections 2.3 and 11.2 say. Document 20, section 3.5 and document 25, section 12.3 call it a plain updatable table (REVIEW).',
};

const q = s => `'${String(s).split("'").join("''")}'`;
const andList = ids => (ids.length < 2 ? ids.join('') : `${ids.slice(0, -1).join(', ')} and ${ids[ids.length - 1]}`);

// A value list on as many lines as it needs, each at most WIDTH characters, indented by eight spaces.
function valueLines(values) {
  const lines = [];
  let line = '';
  for (const v of values.map(q)) {
    const next = line ? `${line}, ${v}` : v;
    if (line && 8 + next.length + 1 > WIDTH) { lines.push(`${line},`); line = v; }
    else line = next;
  }
  if (line) lines.push(line);
  return lines.map(l => `        ${l}`).join('\n');
}
const nq = s => `N${q(s)}`;
const br = s => `[${s}]`;

function historyLine(e, className) {
  return `History class ${e.historyClass} (${className}): ${AZURE_HISTORY_TEXT[e.azure.history]}. Delete policy: ${e.deletePolicy}.`;
}

export function renderAzureSql(bound) {
  const model = bound.model;
  const classNames = new Map(model.historyClasses.map(h => [h.id, h.name.toLowerCase()]));
  const tables = bound.entities.filter(e => e.inScope);
  const stats = { tables: tables.length, temporal: 0, 'updatable-ledger': 0, 'append-only-ledger': 0, columns: 0, uniqueConstraints: 0, filteredUniqueIndexes: 0, checks: 0, foreignKeys: 0, views: 0 };
  const constraintNames = new Set();
  const useName = (name, where) => {
    if (name.length > AZURE_NAME_MAX) throw new Error(`${where}: constraint name ${name} is longer than ${AZURE_NAME_MAX}`);
    if (constraintNames.has(name)) throw new Error(`${where}: constraint name ${name} is used twice`);
    constraintNames.add(name);
    return name;
  };

  // ---- tables ----
  const body = [];
  for (const e of tables) {
    stats[e.azure.history]++;
    const t = e.azure.table;
    const cols = e.columns.filter(c => c.azure && c.azure.bound && !c.azure.storedIn && !c.azure.inView);
    const byName = new Map(e.columns.map(c => [c.name, c]));
    const phys = name => {
      const c = byName.get(name);
      return c && c.azure && c.azure.bound && !c.azure.storedIn && !c.azure.inView ? c.azure.column : null;
    };

    body.push(RULE);
    body.push(`-- ${e.id} ${e.name}: ${e.kind}, ${e.status}, release ${e.release}.`);
    for (const l of wrap(historyLine(e, classNames.get(e.historyClass)), WIDTH, '-- ')) body.push(l);
    if (e.status === 'proposal') for (const l of wrap(`PROPOSAL, not yet accepted (${e.source}).`, WIDTH, '-- ')) body.push(l);
    if (e.workingDefault) for (const l of wrap(e.workingDefault, WIDTH, '-- ', '--   ')) body.push(l);
    if (TABLE_NOTES[e.id]) for (const l of wrap(TABLE_NOTES[e.id], WIDTH, '-- ')) body.push(l);
    for (const k of e.azure.engineKeys) {
      for (const l of wrap(`Checked by the engine, not by a constraint: ${k.kind} key ${k.columns.join(' + ')}${k.condition ? ` (${k.condition})` : ''}.`, WIDTH, '-- ', '--   ')) body.push(l);
    }
    const unbound = e.columns.filter(c => c.azure && !c.azure.bound && c.type !== 'platform-identity');
    for (const c of unbound) for (const l of wrap(`No column for ${c.name}: ${c.azure.reason}.`, WIDTH, '-- ', '--   ')) body.push(l);
    body.push(RULE);
    body.push(`CREATE TABLE ${e.azure.qualified} (`);

    const items = [];
    for (const c of cols) {
      const a = c.azure;
      let def = `${br(a.column)} ${a.sqlType} ${a.nullable ? 'NULL' : 'NOT NULL'}`;
      if (a.default) def += ` CONSTRAINT ${br(useName(`DF_${t}_${a.column}`, e.id))} DEFAULT ${a.default}`;
      const flags = [];
      if (c.required === 'C') flags.push('conditional');
      if (c.status === 'proposal') flags.push(`proposal (${c.source || 'not in document 22'})`);
      if (c.fillOnce) flags.push('fill once');
      if (c.restated) flags.push(`restates the ${c.set} column`);
      if (c.type === 'ref' && a.foreignKey === null) flags.push('no foreign key: the target has no table in this draft');
      items.push({ pre: c.workingDefault ? wrap(c.workingDefault, WIDTH, '    -- ', '    --   ') : [], text: def, note: flags.join('; ') });
      stats.columns++;
    }
    if (e.azure.history === 'temporal') {
      items.push({ pre: [], text: '[ValidFrom] datetime2(7) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL', note: 'period start' });
      items.push({ pre: [], text: '[ValidTo] datetime2(7) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL', note: 'period end' });
      items.push({ pre: [], text: 'PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])', note: '' });
    }
    items.push({ pre: [], text: `CONSTRAINT ${br(useName(`PK_${t}`, e.id))} PRIMARY KEY CLUSTERED ([Id])`, note: '' });
    for (const u of e.azure.uniques.filter(x => !x.filterOn.length)) {
      items.push({ pre: [], text: `CONSTRAINT ${br(useName(u.name, e.id))} UNIQUE (${u.physical.map(br).join(', ')})`, note: `${u.kind} key` });
      stats.uniqueConstraints++;
    }
    // Configuration keys: lowercase letters, digits and hyphens, starting with a letter (document 22, section 11.1).
    for (const c of cols.filter(x => x.type === 'key')) {
      const col = br(c.azure.column);
      const check = [`CHECK (`, `        ${col} NOT LIKE '%[^a-z0-9-]%' ${BIN2}`, `        AND ${col} LIKE '[a-z]%' ${BIN2}`, '    )'].join('\n');
      items.push({ pre: [], text: `CONSTRAINT ${br(useName(`CK_${t}_${c.azure.column}`, e.id))} ${check}`, note: 'configuration key format' });
      stats.checks++;
    }
    // Engine meanings: the contract's values, compared ordinally like the engine (document 25, section 3.2).
    for (const c of cols.filter(x => x.type === 'meaning')) {
      const contract = bound.contracts.get(c.meaning);
      const proposed = new Set((contract && contract.proposedValues) || []);
      const values = c.azure.values;
      const note = [`contract ${c.meaning}`];
      if (values.some(v => proposed.has(v))) note.push(`includes proposed value(s): ${values.filter(v => proposed.has(v)).join(', ')}`);
      if (contract && contract.workingDefault) note.push(contract.workingDefault.slice(0, contract.workingDefault.indexOf(')') + 1));
      const head = `CONSTRAINT ${br(useName(`CK_${t}_${c.azure.column}`, e.id))} CHECK (${br(c.azure.column)} ${BIN2} IN (`;
      const oneLine = `${head}${values.map(q).join(', ')}))`;
      items.push({ pre: [], text: 4 + oneLine.length + 1 <= WIDTH ? oneLine : `${head}\n${valueLines(values)}\n    ))`, note: note.join('; ') });
      stats.checks++;
    }
    // Exactly one, at least one, at most one (document 22, sections 2.6 and 4).
    for (const k of e.constraints) {
      const names = k.columns.map(phys);
      if (names.some(n => !n)) { items.push({ pre: [], text: null, note: `${k.kind} over ${k.columns.join(', ')} has an unbound column; the engine checks it` }); continue; }
      const sum = names.map((n, i) => `${i ? '+ ' : ''}CASE WHEN ${br(n)} IS NULL THEN 0 ELSE 1 END`).join('\n        ');
      const op = { 'exactly-one': '= 1', 'at-least-one': '>= 1', 'at-most-one': '<= 1' }[k.kind];
      const name = useName(`CK_${t}_${pascalKey(k.kind)}_${names[0]}`, e.id);
      items.push({ pre: [`    -- ${k.note}`], text: `CONSTRAINT ${br(name)} CHECK (\n        ${sum}\n        ${op}\n    )`, note: '' });
      stats.checks++;
    }
    // A subject that can be one of several entities: the set column matches Subject Kind (document 22, section 2.6).
    if (e.subjects) {
      const kindCol = phys(e.subjects.kindColumn);
      const parts = e.subjects.options.map(o => `(${br(kindCol)} = ${nq(o.value)} AND ${br(phys(o.column))} IS NOT NULL)`);
      const name = useName(`CK_${t}_${pascal(e.subjects.kindColumn)}`, e.id);
      items.push({ pre: ['    -- The subject column that is set matches Subject Kind'], text: `CONSTRAINT ${br(name)} CHECK (\n        ${parts.join('\n        OR ')}\n    )`, note: '' });
      stats.checks++;
    }

    const real = items.filter(i => i.text !== null);
    for (const i of items) {
      if (i.text === null) { body.push(`    -- ${i.note}`); continue; }
      for (const p of i.pre) body.push(p);
      const last = i === real[real.length - 1];
      body.push(`    ${i.text}${last ? '' : ','}${i.note ? ` -- ${i.note}` : ''}`);
    }
    if (e.azure.history === 'temporal') body.push(`)\nWITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = ${e.azure.historyTable}));`);
    else if (e.azure.history === 'updatable-ledger') body.push(`)\nWITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = ${e.azure.historyTable}), LEDGER = ON);`);
    else body.push(')\nWITH (LEDGER = ON (APPEND_ONLY = ON));');
    body.push('GO');
    for (const u of e.azure.uniques.filter(x => x.filterOn.length)) {
      for (const l of wrap(`${e.id} ${u.kind} key ${u.columns.join(' + ')}, enforced where ${u.filterOn.join(' and ')} ${u.filterOn.length > 1 ? 'are' : 'is'} set.${u.condition ? ` Model: ${u.condition}.` : ''}`, WIDTH, '-- ', '--   ')) body.push(l);
      body.push(`CREATE UNIQUE NONCLUSTERED INDEX ${br(useName(u.name, e.id))} ON ${e.azure.qualified} (${u.physical.map(br).join(', ')})`);
      body.push(`    WHERE ${u.filterOn.map(n => `${br(n)} IS NOT NULL`).join(' AND ')};`);
      body.push('GO');
      stats.filteredUniqueIndexes++;
    }
    body.push('');
  }

  // ---- foreign keys ----
  const fks = [];
  fks.push(RULE);
  for (const l of wrap('Foreign keys (document 22, section 6). Added after every table exists, because references run in both directions. Restrict is ON DELETE NO ACTION; the one Cascade relationship (R80) is ON DELETE CASCADE; self references are always NO ACTION.', WIDTH, '-- ')) fks.push(l);
  fks.push(RULE);
  for (const e of tables) {
    const refs = e.columns.filter(c => c.azure && c.azure.bound && c.azure.foreignKey);
    if (!refs.length) continue;
    fks.push(`-- ${e.id} ${e.name}`);
    for (const c of refs) {
      const name = useName(`FK_${e.azure.table}_${c.azure.column}`, e.id);
      const rel = c.relationship ? ` -- ${c.relationship.id}, ${c.relationship.cardinality}` : '';
      fks.push(`ALTER TABLE ${e.azure.qualified} ADD CONSTRAINT ${br(name)}`);
      fks.push(`    FOREIGN KEY (${br(c.azure.column)}) REFERENCES ${c.azure.foreignKey.table} ([Id]) ON DELETE ${c.azure.foreignKey.onDelete};${rel}`);
      stats.foreignKeys++;
    }
    fks.push('GO');
  }
  fks.push('');

  // ---- views ----
  const views = [];
  for (const v of AZURE_DERIVED_VIEWS) {
    const e = bound.byId.get(v.entity);
    if (!e || !e.inScope) continue;
    const cols = e.columns.filter(c => c.azure && c.azure.bound && !c.azure.storedIn && !c.azure.inView);
    const derived = e.columns.find(c => c.name === v.column);
    const inverse = e.columns.find(c => c.name === v.inverseOf);
    const where = e.columns.find(c => c.name === v.where.column);
    if (!derived || !inverse || !where) throw new Error(`${v.entity}: view ${v.view} names a missing column`);
    views.push(RULE);
    for (const l of wrap(`${e.id} ${e.name}: ${v.column} is derived from the later decision's ${v.inverseOf} link, so the frozen row is never updated (document 22, section 8.1). One decision is superseded by at most one accepted decision (an engine rule, R70).`, WIDTH, '-- ')) views.push(l);
    views.push(RULE);
    views.push(`CREATE VIEW [${e.azure.schema}].[${v.view}]`);
    views.push('AS');
    views.push('SELECT');
    for (const c of cols) views.push(`    d.${br(c.azure.column)},`);
    views.push(`    s.[Id] AS ${br(derived.azure.column)}`);
    views.push(`FROM ${e.azure.qualified} AS d`);
    views.push('OUTER APPLY (');
    views.push('    SELECT TOP (1) n.[Id]');
    views.push(`    FROM ${e.azure.qualified} AS n`);
    views.push(`    WHERE n.${br(inverse.azure.column)} = d.[Id] AND n.${br(where.azure.column)} = ${q(v.where.value)}`);
    views.push('    ORDER BY n.[Id]');
    views.push(') AS s;');
    views.push('GO');
    views.push('');
    stats.views++;
  }

  // ---- header ----
  const head = [];
  const para = text => { for (const l of wrap(text, WIDTH, '-- ')) head.push(l); };
  const item = (n, text) => { for (const l of wrap(text, WIDTH, `--   ${n}. `, `--   ${' '.repeat(String(n).length)}  `)) head.push(l); };
  head.push('-- Product Azure SQL Database schema, migration 0001 (initial schema)');
  head.push(`-- Status: ${DRAFT}.`);
  para(`${NOT_APPLIED} Generated by ${GENERATOR} from ${MODEL_PATH}, model ${model.modelVersion} (${model.date}), canonical SHA-256 ${bound.hash}. Do not edit by hand: change the model and run "node model/generate.mjs write".`);
  head.push('--');
  para('Binding: document 22, section 11 (Azure SQL Database). Names: document 20, section 3.5. Batches are separated by GO, as sqlcmd and the usual migration runners read them; the runner decides transactions.');
  head.push('--');
  para(`Holds: ${stats.tables} tables, one per entity of the first release, adopted and proposed (${stats.temporal} temporal, ${stats['updatable-ledger']} updatable ledger, ${stats['append-only-ledger']} append-only ledger), ${stats.columns} columns, ${stats.uniqueConstraints} unique constraints, ${stats.filteredUniqueIndexes} filtered unique indexes, ${stats.checks} check constraints, ${stats.foreignKeys} foreign keys and ${stats.views} view.`);
  para('Leaves out: row-level security predicates and security policies (document 22, section 11.3), database principals and permissions (section 11.1), ledger digest storage, the seed load and the migration runner, and the later entities L01 to L05.');
  head.push('--');
  para('Rules applied:');
  item(1, 'Primary key Id uniqueidentifier with a NEWSEQUENTIALID() default. Created On and Modified On default to SYSUTCDATETIME(); the API sets Modified On on update (no trigger here).');
  item(2, 'Required Y is NOT NULL. Required N and C (conditional) are NULL; the engine checks conditions.');
  item(3, 'Configuration keys are varchar(64) with the format check of document 22, section 11.1. Engine meanings are varchar(32) with a check listing the contract values. Both compare with Latin1_General_BIN2, so the checks are ordinal, like the engine (document 25, section 3.2). A meaning copy never stores a derived value (superseded, missing).');
  item(4, 'A store-enforced key with an optional column is a filtered unique index that ignores rows where that column is empty. It refuses the same rows as the matching Dataverse alternate key, which does not enforce uniqueness on null values [S15]. The engine checks the rest (document 22, section 7). Keys marked "engine" have no constraint.');
  const byHistory = h => tables.filter(e => e.azure.history === h).map(e => e.id);
  item(5, `History classes follow document 22, sections 2.3 and 11.2: system-versioned temporal tables for mutable rows (${byHistory('temporal').length} tables), updatable ledger tables for ${andList(byHistory('updatable-ledger'))}, append-only ledger tables for ${andList(byHistory('append-only-ledger'))}.`);
  item(6, 'A subject that can be one of several entities gets one nullable foreign key per kind, an exactly-one check and a check that the set column matches Subject Kind (document 22, section 2.6).');
  head.push('--');
  for (const l of wrap(`${model.meaningContracts.workingDefault} On this binding every meaning column is varchar(32) with a check constraint that lists its contract's values (document 22, section 11.1). If the answer is no, these columns become references to configuration rows.`, WIDTH, '-- ', '--   ')) head.push(l);
  head.push('--');
  para('Review points:');
  let n = 1;
  for (const cy of bound.requiredCycles) {
    const names = cy.members.map(id => `${id} ${bound.byId.get(id).name}`);
    item(n++, `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]} need each other through NOT NULL foreign keys (${cy.links.join('; ')}). SQL Server checks a foreign key at every statement and has no deferred constraints, so the first rows of these tables cannot be inserted. The bootstrap of version 1 (document 21, section 4.8) needs a way in, for example a conditional Configuration Set Version on O05. Open until the migration runner is built; the model is unchanged.`);
  }
  if (bound.requiredSelfReferences.length) {
    item(n++, `NOT NULL self references (${bound.requiredSelfReferences.join('; ')}): the first row must carry its own Id, given by the caller instead of the NEWSEQUENTIALID() default.`);
  }
  item(n++, 'UNVERIFIED here: foreign keys, check constraints, filtered unique indexes and json columns on ledger tables, and ALTER TABLE ADD CONSTRAINT on a ledger table after it is created. They are to be checked on Azure SQL before the migration runner is built.');
  item(n++, 'Created By and Modified By (platform identity) have no column: document 22 says temporal or ledger history records the database principal. Whether temporal history shows the principal of each version is UNVERIFIED here. If it does not, the temporal tables need those two columns.');
  item(n++, 'C46 Configuration Draft is an updatable ledger table (document 22). Documents 20 and 25 call it a plain updatable table.');
  item(n++, `Record Number (${andList(tables.filter(e => e.recordNumberPrefix).map(e => e.id))}) is assigned by the engine. No sequence is created here.`);
  if (bound.warnings.length) item(n++, `Generator warnings: ${bound.warnings.join('; ')}.`);
  head.push('');
  head.push(RULE);
  head.push('-- Schemas (document 22, section 11.1). security holds the row-level security predicate functions; none are in this draft.');
  head.push(RULE);
  for (const s of ['config', 'ops', 'audit', 'security']) { head.push(`CREATE SCHEMA ${br(s)};`); head.push('GO'); }
  head.push('');

  const tail = [`-- End of migration 0001. Status: ${DRAFT}.`];
  return { text: `${[...head, ...body, ...fks, ...views, ...tail].join('\n')}\n`, stats };
}
