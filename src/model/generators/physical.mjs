// Shared binding rules for the model generators.
// Reads model/logical-model.json and works out, for every entity and column, both physical bindings of document 22:
// section 10 (Dataverse) and section 11 (Azure SQL Database), with the names that document 20, section 3 proposes.
// No packages. Pure functions over the model: the same model gives the same result, in model order, on every machine.
// Nothing here talks to a database or an environment.
import fs from 'node:fs';
import crypto from 'node:crypto';

export const DRAFT = 'draft, platform under review (D-22)';
export const PREFIX = '<prefix>';
export const GENERATOR = 'model/generate.mjs';
export const MODEL_PATH = 'model/logical-model.json';
export const NOT_APPLIED = 'Not applied to any database or environment.';

// A model shape this generator cannot bind. generate.mjs stops with exit code 1 and prints every one.
export class BindError extends Error {
  constructor(problems) {
    super(`${problems.length} binding problem(s)`);
    this.problems = problems;
  }
}

export function readModel(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
}

// SHA-256 of the parsed model written back as compact JSON. Line endings and indentation in the file do not change it,
// so a checkout with CRLF line endings gives the same value.
export function modelHash(model) {
  return crypto.createHash('sha256').update(JSON.stringify(model)).digest('hex');
}

// ---- names (document 20, section 3) ----

// Title Case logical names joined without spaces. Words keep their case: "Uses AI" becomes UsesAI.
export const pascal = logical => logical.split(' ').join('');
// Contract keys are kebab-case: ai-only-code-approval-handling becomes AiOnlyCodeApprovalHandling.
export const pascalKey = key => key.split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join('');
export const lower = s => s.toLowerCase();

// Plural display names for tables (document 20, section 3.4: "the logical names, with plural forms for tables").
export function plural(name) {
  const words = name.split(' ');
  const last = words.pop();
  let p;
  if (/[^aeiou]y$/.test(last)) p = `${last.slice(0, -1)}ies`;
  else if (/(s|x|z|ch|sh)$/.test(last)) p = `${last}es`;
  else p = `${last}s`;
  return [...words, p].join(' ');
}

// Document 20, section 3.4 keeps Dataverse schema names under 40 characters including the prefix (UNVERIFIED there).
// The prefix is 2 to 8 characters, so names are measured with the longest prefix.
export const DATAVERSE_NAME_GUIDE = 40;
export const dataverseLength = name => name.split(PREFIX).join('x'.repeat(8)).length;
// Document 20, section 3.5: Azure SQL identifiers are 1 to 128 characters [S17].
export const AZURE_NAME_MAX = 128;

// ---- Azure SQL binding (document 22, section 11) ----

// Section 11.1: config for C entities, ops for O entities, audit for O24.
const AZURE_SCHEMA_OVERRIDE = { O24: 'audit' };
// Sections 2.3 and 11.2: the history class as a table type. Class F differs per entity, so each one is listed and a
// class F entity of the first release that is not listed stops the generator.
const AZURE_HISTORY_BY_CLASS = { A: 'append-only-ledger', E: 'temporal', M: 'temporal', V: 'temporal' };
const AZURE_HISTORY_CLASS_F = {
  C46: 'updatable-ledger',
  O08: 'updatable-ledger',
  O09: 'updatable-ledger',
  O10: 'updatable-ledger',
  O13: 'temporal',
  O14: 'append-only-ledger',
  O22: 'temporal',
};
export const AZURE_HISTORY_TEXT = {
  temporal: 'system-versioned temporal table',
  'updatable-ledger': 'updatable ledger table',
  'append-only-ledger': 'append-only ledger table',
};
// Sections 8.1 and 11.2: Superseded By is derived. Azure computes it in the view ops.DecisionCurrent, so the frozen
// row is never updated. Dataverse stores it, as the one write a frozen decision ever receives.
export const AZURE_DERIVED_VIEWS = [
  { entity: 'O09', column: 'Superseded By', view: 'DecisionCurrent', inverseOf: 'Supersedes', where: { column: 'Status Meaning', value: 'accepted' } },
];
// Temporal tables get two period columns.
export const AZURE_PERIOD_COLUMNS = ['ValidFrom', 'ValidTo'];

// Section 1.1: logical type to SQL type.
function azureSqlType(a) {
  switch (a.type) {
    case 'id': return 'uniqueidentifier';
    case 'text': return `nvarchar(${a.length})`;
    case 'longtext': return 'nvarchar(max)';
    case 'int': return 'int';
    case 'decimal': return 'decimal(9,2)';
    case 'bool': return 'bit';
    case 'date': return 'date';
    case 'datetime': return 'datetime2(3)';
    case 'url': return 'nvarchar(2000)';
    case 'json': return 'json';
    case 'hash': return 'char(64)';
    case 'key': return 'varchar(64)';
    case 'meaning': return 'varchar(32)';
    case 'ref': return 'uniqueidentifier';
    default: return null;
  }
}

// Bytes a column adds to an index key, for the key size checks. nvarchar counts 2 bytes per character.
export function azureKeyBytes(sqlType) {
  let m;
  if ((m = /^nvarchar\((\d+)\)$/.exec(sqlType))) return 2 * Number(m[1]);
  if ((m = /^n?varchar\((\d+)\)$|^char\((\d+)\)$/.exec(sqlType))) return Number(m[1] || m[2]);
  return { uniqueidentifier: 16, int: 4, bit: 1, date: 3, 'datetime2(3)': 7, 'decimal(9,2)': 5 }[sqlType] ?? 8000;
}
// Nonclustered index keys are limited to 1,700 bytes in SQL Server (UNVERIFIED for Azure SQL Database here).
export const AZURE_INDEX_KEY_MAX = 1700;

// ---- Dataverse binding (document 22, section 10) ----

// Section 10.1: configuration tables (C02 and C46 included), O05 and O24 are organization owned. The other operational
// tables are user or team owned, by default by a records owner team with no members.
const DATAVERSE_ORG_OWNED_OPERATIONAL = new Set(['O05', 'O24']);
// Section 2.1.1: platform-supplied columns map to the built-in columns.
const DATAVERSE_BUILT_IN = { 'Created On': 'createdon', 'Created By': 'createdby', 'Modified On': 'modifiedon', 'Modified By': 'modifiedby' };
// Section 10.1: auditing is off for the columns that change on every gap check run.
export const DATAVERSE_AUDIT_OFF = new Set(['Last Seen On', 'Times Seen', 'Last Seen Run', 'Owner Checked On']);
// Section 10.1: alternate keys sit on keys with text and lookup columns. Section 10.2 also lists whole numbers
// (C02 Version Number, C08 Rank, C12 Stage Rank). Dates, JSON and long text never form a key here.
const DATAVERSE_KEY_TYPES = new Set(['text', 'key', 'hash', 'ref', 'int']);
export const DATAVERSE_KEY_MAX_PER_TABLE = 10; // [S15]
export const DATAVERSE_KEY_MAX_BYTES = 900; // [S15]
export const DATAVERSE_MEMO_MAX = 1048576; // [T8]

function dataverseType(a) {
  switch (a.type) {
    case 'text': return { attributeType: 'String', format: 'Text', maxLength: a.length };
    case 'longtext': return { attributeType: 'Memo', maxLength: DATAVERSE_MEMO_MAX };
    case 'int': return { attributeType: 'Integer' };
    case 'decimal': return { attributeType: 'Decimal', precision: 2 };
    case 'bool': return { attributeType: 'Boolean' };
    case 'date': return { attributeType: 'DateTime', format: 'DateOnly' };
    case 'datetime': return { attributeType: 'DateTime', format: 'DateAndTime' };
    case 'url': return { attributeType: 'String', format: 'Url', maxLength: 2000 };
    case 'json': return { attributeType: 'Memo', format: 'Text', maxLength: DATAVERSE_MEMO_MAX };
    case 'hash': return { attributeType: 'String', format: 'Text', maxLength: 64 };
    case 'key': return { attributeType: 'String', format: 'Text', maxLength: 64 };
    // A custom GUID column is not offered for custom tables (UNVERIFIED here), so a row ID held as a value is text.
    case 'id': return { attributeType: 'String', format: 'Text', maxLength: 36 };
    default: return null;
  }
}
function dataverseKeyBytes(c) {
  if (c.type === 'ref') return 16;
  if (c.type === 'int') return 4;
  const t = dataverseType(c);
  return t && t.maxLength ? 2 * t.maxLength : 8000;
}

// ---- binding ----

// Binds the whole model. Returns { model, hash, entities, byId, contracts, choices, warnings, reviewPoints }.
// Throws BindError when a shape cannot be bound.
export function bind(model) {
  const problems = [];
  const warnings = [];
  const byId = new Map(model.entities.map(e => [e.id, e]));
  const sets = new Map(model.columnSets.map(s => [s.name, s]));
  const contracts = new Map(model.meaningContracts.contracts.map(c => [c.key, c]));
  const statusUnion = new Set((contracts.get('status-meaning') || { unionOf: [] }).unionOf || []);
  const sharedRels = model.sharedRelationships;

  const hasTable = e => e.status !== 'later' && !e.storedIn;
  const tableName = e => pascal(e.name);
  const schemaOf = e => AZURE_SCHEMA_OVERRIDE[e.id] || (e.kind === 'configuration' ? 'config' : 'ops');
  const dvTable = e => `${PREFIX}_${tableName(e)}`;
  const hostOf = e => (e.storedIn ? byId.get(e.storedIn) : e);

  function historyOf(e) {
    if (e.status === 'later') return null;
    if (e.historyClass === 'F') {
      const h = AZURE_HISTORY_CLASS_F[e.id];
      if (!h) problems.push(`${e.id} ${e.name}: class F with no Azure table type in document 22, section 11.2. Add it to AZURE_HISTORY_CLASS_F`);
      return h || null;
    }
    return AZURE_HISTORY_BY_CLASS[e.historyClass] || null;
  }

  // Values a meaning column may store. A meaning copy never stores a derived value (document 22, section 8.1: superseded
  // is derived). A union contract (C05 Engine Meaning) keeps them, because a status label may map to them.
  function contractValues(key, { dropDerived }) {
    const c = contracts.get(key);
    if (!c) return [];
    if (c.unionOf) return [...new Set(c.unionOf.flatMap(k => contractValues(k, { dropDerived })))];
    const derived = new Set(c.derivedValues || []);
    return [...c.values, ...(c.proposedValues || [])].filter(v => !(dropDerived && derived.has(v)));
  }

  // Dataverse global choices: one per contract, except that every status contract shares "status meaning"
  // (document 22, section 10.1; document 20, section 3.4).
  const choiceKeyOf = contractKey => (statusUnion.has(contractKey) ? 'status-meaning' : contractKey);
  const choiceName = contractKey => `${PREFIX}_${pascalKey(choiceKeyOf(contractKey))}`;

  function relationshipOf(e, col) {
    if (col.set) return sharedRels.find(r => r.columnSet === col.set && r.column === col.name) || null;
    return e.relationships.find(r => r.column === col.name) || null;
  }

  const entities = [];
  for (const e of model.entities) {
    const host = hostOf(e);
    const inScope = hasTable(e);
    const history = historyOf(e);
    const be = {
      id: e.id,
      name: e.name,
      kind: e.kind,
      status: e.status,
      release: e.release,
      historyClass: e.historyClass,
      deletePolicy: e.deletePolicy,
      purpose: e.purpose,
      source: e.source,
      storedIn: e.storedIn || null,
      recordNumberPrefix: e.recordNumberPrefix || null,
      workingDefault: e.workingDefault || null,
      inScope,
      keys: e.keys,
      constraints: e.constraints || [],
      subjects: e.subjects || null,
      azure: {
        schema: schemaOf(host),
        table: tableName(host),
        qualified: `[${schemaOf(host)}].[${tableName(host)}]`,
        history,
        historyTable: history && history !== 'append-only-ledger' ? `[${schemaOf(host)}].[${tableName(host)}History]` : null,
      },
      dataverse: {
        schemaName: dvTable(host),
        logicalName: lower(dvTable(host)),
        displayName: host.name,
        displayCollectionName: plural(host.name),
        ownership: host.kind === 'configuration' || DATAVERSE_ORG_OWNED_OPERATIONAL.has(host.id) ? 'OrganizationOwned' : 'UserOwned',
      },
      columns: [],
    };

    // Columns: the column sets in the entity's order, then its own attributes. A restated set column is one physical
    // column with the entity's own description (model open point 8).
    const own = new Map(e.attributes.map(a => [a.name, a]));
    const raw = [];
    for (const setName of e.columnSets) {
      for (const a of sets.get(setName).attributes) {
        const r = own.get(a.name);
        raw.push(r && r.restates === setName ? { attr: r, set: setName, restated: true } : { attr: a, set: setName, restated: false });
      }
    }
    for (const a of e.attributes) if (!a.restates) raw.push({ attr: a, set: null, restated: false });

    for (const { attr: a, set, restated } of raw) {
      const col = {
        name: a.name,
        set,
        restated,
        type: a.type,
        length: a.length ?? null,
        required: a.required,
        ref: a.ref || null,
        target: a.target || null,
        meaning: a.meaning || null,
        meaningCopyOf: a.meaningCopyOf || null,
        valueList: a.valueList || null,
        keyOf: a.keyOf || null,
        status: a.status || null,
        source: a.source || null,
        derived: a.derived === true,
        stored: a.stored !== false,
        storedIn: a.storedIn || null,
        platform: a.platform || null,
        fillOnce: a.fillOnce === true,
        isNew: a.new === true,
        description: a.description,
        workingDefault: a.workingDefault || null,
        relationship: null,
        azure: null,
        dataverse: null,
      };
      if (a.type === 'ref') {
        const rel = relationshipOf(e, col);
        if (!rel) problems.push(`${e.id} ${e.name} / ${a.name}: no relationship for this reference`);
        else col.relationship = { id: rel.id, catalogId: rel.catalogId || null, cardinality: rel.cardinality, delete: rel.delete, ownedChild: rel.ownedChild === true };
      }
      be.columns.push(col);
    }

    // ---- Azure SQL columns ----
    const view = AZURE_DERIVED_VIEWS.find(v => v.entity === e.id);
    for (const col of be.columns) {
      if (!col.stored) { col.azure = { bound: false, reason: 'derived, never stored' }; continue; }
      if (col.platform && col.platform !== 'azure-sql') { col.azure = { bound: false, reason: `${col.platform} binding only` }; continue; }
      if (col.type === 'platform-user') { col.azure = { bound: false, reason: 'Dataverse binding only: a lookup to the built-in user row' }; continue; }
      if (col.type === 'platform-identity') {
        col.azure = { bound: false, reason: 'the database principal, recorded by temporal or ledger history (document 22, section 1.1)' };
        continue;
      }
      const name = col.type === 'ref' ? `${pascal(col.name)}Id` : pascal(col.name);
      if (view && view.column === col.name) {
        col.azure = { bound: true, inView: `[${be.azure.schema}].[${view.view}]`, column: name, sqlType: 'uniqueidentifier', note: 'derived: computed in the view, never stored in the table (document 22, section 8.1)' };
        continue;
      }
      if (col.storedIn) { col.azure = { bound: true, storedIn: col.storedIn, column: pascal(col.storedIn) }; continue; }
      const sqlType = azureSqlType(col);
      if (!sqlType) { problems.push(`${e.id} / ${col.name}: no SQL type for logical type ${col.type}`); continue; }
      const az = { bound: true, column: name, sqlType, nullable: col.required !== 'Y' };
      if (col.name === 'Id' && col.set === 'common') { az.nullable = false; az.default = 'NEWSEQUENTIALID()'; az.primaryKey = true; }
      if ((col.name === 'Created On' || col.name === 'Modified On') && col.set === 'common') az.default = 'SYSUTCDATETIME()';
      if (col.type === 'meaning') az.values = contractValues(col.meaning, { dropDerived: !!col.meaningCopyOf });
      if (col.type === 'ref') {
        const target = byId.get(col.target);
        const tHost = target ? hostOf(target) : null;
        if (!tHost) problems.push(`${e.id} / ${col.name}: target ${col.target} does not exist`);
        else if (!hasTable(tHost)) az.foreignKey = null;
        else {
          az.foreignKey = {
            table: `[${schemaOf(tHost)}].[${tableName(tHost)}]`,
            onDelete: col.relationship && col.relationship.delete === 'Cascade' && col.target !== e.id ? 'CASCADE' : 'NO ACTION',
          };
        }
      }
      col.azure = az;
    }

    // ---- Dataverse columns ----
    for (const col of be.columns) {
      if (!col.stored) { col.dataverse = { bound: false, reason: 'derived, never stored' }; continue; }
      if (col.platform && col.platform !== 'dataverse') { col.dataverse = { bound: false, reason: `${col.platform} binding only` }; continue; }
      if (col.storedIn) {
        col.dataverse = { bound: true, storedIn: col.storedIn };
        continue; // filled below, from the host column
      }
      const dv = { bound: true };
      if (col.name === 'Id' && col.set === 'common') {
        Object.assign(dv, { kind: 'primary-key', schemaName: `${dvTable(host)}Id`, attributeType: 'Uniqueidentifier' });
      } else if (col.name === 'Name' && col.set === 'common') {
        Object.assign(dv, { kind: 'primary-name', schemaName: `${PREFIX}_Name`, ...dataverseType(col) });
      } else if (DATAVERSE_BUILT_IN[col.name] && col.set === 'common') {
        Object.assign(dv, { kind: 'built-in', schemaName: DATAVERSE_BUILT_IN[col.name] });
      } else if (col.name === 'Is Active' && col.set === 'taxonomy') {
        // Section 10.1: Is Active binds to the platform state on configuration tables.
        Object.assign(dv, { kind: 'platform-state', schemaName: 'statecode', note: 'Active or Inactive (document 22, section 10.1)' });
      } else if (col.type === 'ref') {
        const target = byId.get(col.target);
        const tHost = target ? hostOf(target) : null;
        Object.assign(dv, {
          kind: 'lookup',
          schemaName: `${PREFIX}_${pascal(col.name)}Id`,
          attributeType: 'Lookup',
          target: tHost ? lower(dvTable(tHost)) : null,
          relationship: tHost ? `${PREFIX}_${lower(tableName(tHost))}_${lower(tableName(host))}_${lower(pascal(col.name))}id` : null,
          behaviour: col.relationship && col.relationship.delete === 'Cascade' ? 'Parental' : 'Referential, Restrict Delete',
        });
      } else if (col.type === 'platform-user') {
        Object.assign(dv, {
          kind: 'lookup',
          schemaName: `${PREFIX}_${pascal(col.name)}Id`,
          attributeType: 'Lookup',
          target: 'systemuser',
          relationship: `${PREFIX}_systemuser_${lower(tableName(host))}_${lower(pascal(col.name))}id`,
          behaviour: 'Referential, Restrict Delete',
        });
      } else if (col.type === 'meaning') {
        Object.assign(dv, { kind: 'choice', schemaName: `${PREFIX}_${pascal(col.name)}`, attributeType: 'Picklist', globalChoice: lower(choiceName(col.meaning)) });
      } else {
        const t = dataverseType(col);
        if (!t) { problems.push(`${e.id} / ${col.name}: no Dataverse type for logical type ${col.type}`); continue; }
        Object.assign(dv, { kind: 'column', schemaName: `${PREFIX}_${pascal(col.name)}`, ...t });
        if (col.type === 'id') dv.note = 'a row ID held as a value; text, because custom tables offer no GUID column (UNVERIFIED here)';
      }
      dv.logicalName = lower(dv.schemaName);
      col.dataverse = dv;
    }
    // Stored-in columns take the names of the column that holds them (taxonomy Label in Name).
    for (const col of be.columns) {
      if (col.storedIn) {
        const hostCol = be.columns.find(c => c.name === col.storedIn);
        if (!hostCol || !hostCol.dataverse || !hostCol.dataverse.bound) { problems.push(`${e.id} / ${col.name}: stored in ${col.storedIn}, which has no column`); continue; }
        col.dataverse = { bound: true, storedIn: col.storedIn, kind: hostCol.dataverse.kind, schemaName: hostCol.dataverse.schemaName, logicalName: hostCol.dataverse.logicalName };
      }
    }
    // Section 10.1: autonumber columns for Record Number.
    if (be.recordNumberPrefix) {
      const rn = be.columns.find(c => c.name === 'Record Number');
      if (rn && rn.dataverse && rn.dataverse.bound) rn.dataverse.autonumberFormat = `${be.recordNumberPrefix}-{SEQNUM:5}`;
    }
    // Every operational table keeps its platform state Active and uses the Status lookup (section 10.1).
    be.dataverse.platformState = e.kind === 'configuration' && e.columnSets.includes('taxonomy')
      ? 'Is Active binds to statecode (Active or Inactive)'
      : 'statecode stays Active';
    entities.push(be);
  }

  const bound = { model, hash: modelHash(model), entities, byId: new Map(entities.map(e => [e.id, e])), contracts, warnings, reviewPoints: [] };

  // ---- keys ----
  for (const be of entities) {
    const colByName = new Map(be.columns.map(c => [c.name, c]));
    be.azure.uniques = [];
    be.azure.engineKeys = [];
    be.dataverse.alternateKeys = [];
    be.dataverse.engineKeys = [];
    let dvIndex = 0;
    for (const k of be.keys) {
      const cols = k.columns.map(n => colByName.get(n));
      if (cols.some(c => !c)) { problems.push(`${be.id}: key ${k.columns.join(' + ')} names a missing column`); continue; }
      const text = k.columns.join(' + ');
      if (k.enforcement === 'engine') {
        const entry = { kind: k.kind, columns: k.columns, condition: k.condition || null, note: k.note || null };
        be.azure.engineKeys.push(entry);
        be.dataverse.engineKeys.push(entry);
        continue;
      }
      // Azure: a unique constraint, or a filtered unique index when a column is optional. A filtered index refuses the
      // same rows a Dataverse alternate key refuses, which ignores rows with an empty key column [S15]; the engine
      // checks the rest (document 22, section 7).
      const azCols = cols.map(c => c.azure);
      if (azCols.some(a => !a || !a.bound || a.storedIn || a.inView)) {
        problems.push(`${be.id}: key ${text} uses a column with no Azure column`);
      } else {
        const optional = cols.filter(c => c.required !== 'Y').map(c => c.azure.column);
        const name = `UQ_${be.azure.table}_${azCols.map(a => a.column).join('_')}`;
        const bytes = azCols.reduce((n, a) => n + azureKeyBytes(a.sqlType), 0);
        if (bytes > AZURE_INDEX_KEY_MAX) warnings.push(`${be.id} ${be.name}: Azure key ${text} is about ${bytes} bytes, above ${AZURE_INDEX_KEY_MAX}`);
        be.azure.uniques.push({ kind: k.kind, columns: k.columns, physical: azCols.map(a => a.column), name, filterOn: optional, condition: k.condition || null, bytes });
      }
      // Dataverse: an alternate key when every column has a key type (section 10.1).
      const dvCols = cols.map(c => c.dataverse);
      const badType = cols.find(c => !DATAVERSE_KEY_TYPES.has(c.type));
      if (badType) {
        be.dataverse.engineKeys.push({ kind: k.kind, columns: k.columns, condition: k.condition || null, note: `No alternate key: ${badType.name} is a ${badType.type} column. The engine checks this key on Dataverse` });
        continue;
      }
      if (dvCols.some(d => !d || !d.bound)) { problems.push(`${be.id}: key ${text} uses a column with no Dataverse column`); continue; }
      dvIndex++;
      const bytes = cols.reduce((n, c) => n + dataverseKeyBytes(c), 0);
      if (bytes > DATAVERSE_KEY_MAX_BYTES) warnings.push(`${be.id} ${be.name}: Dataverse alternate key ${text} is about ${bytes} bytes, above ${DATAVERSE_KEY_MAX_BYTES} [S15]`);
      const optional = cols.filter(c => c.required !== 'Y').map(c => c.name);
      be.dataverse.alternateKeys.push({
        schemaName: `${be.dataverse.schemaName}Key${dvIndex}`,
        logicalName: lower(`${be.dataverse.schemaName}Key${dvIndex}`),
        displayName: text,
        kind: k.kind,
        columns: k.columns,
        physical: dvCols.map(d => d.logicalName),
        estimatedBytes: bytes,
        condition: k.condition || null,
        note: optional.length ? `${optional.join(' and ')} may be empty. Dataverse alternate keys do not enforce uniqueness on null values [S15], so the engine checks those rows` : null,
      });
    }
    if (be.inScope && be.dataverse.alternateKeys.length > DATAVERSE_KEY_MAX_PER_TABLE) {
      warnings.push(`${be.id} ${be.name}: ${be.dataverse.alternateKeys.length} alternate keys, above ${DATAVERSE_KEY_MAX_PER_TABLE} per table [S15]`);
    }
  }

  // ---- collisions and name lengths ----
  for (const be of entities.filter(x => x.inScope)) {
    const az = new Map();
    if (be.azure.history === 'temporal') for (const r of AZURE_PERIOD_COLUMNS) az.set(r, '(period column)');
    const dv = new Map();
    for (const c of be.columns) {
      if (c.azure && c.azure.bound && !c.azure.storedIn && !c.azure.inView) {
        if (az.has(c.azure.column)) problems.push(`${be.id} ${be.name}: Azure column ${c.azure.column} for both ${az.get(c.azure.column)} and ${c.name}`);
        az.set(c.azure.column, c.name);
        if (c.azure.column.length > AZURE_NAME_MAX) problems.push(`${be.id} / ${c.name}: Azure name longer than ${AZURE_NAME_MAX}`);
      }
      if (c.dataverse && c.dataverse.bound && !c.dataverse.storedIn) {
        const key = c.dataverse.logicalName;
        if (dv.has(key)) problems.push(`${be.id} ${be.name}: Dataverse column ${key} for both ${dv.get(key)} and ${c.name}`);
        dv.set(key, c.name);
      }
    }
  }
  const longNames = [];
  for (const be of entities.filter(x => x.inScope)) {
    const names = [be.dataverse.schemaName, ...be.columns.filter(c => c.dataverse && c.dataverse.bound && !c.dataverse.storedIn && c.dataverse.schemaName.startsWith(PREFIX)).map(c => c.dataverse.schemaName), ...be.dataverse.alternateKeys.map(k => k.schemaName)];
    for (const n of [...new Set(names)]) if (dataverseLength(n) >= DATAVERSE_NAME_GUIDE) longNames.push({ entity: be.id, name: n, length: dataverseLength(n) });
  }
  bound.longDataverseNames = longNames;

  // ---- required reference cycles (Azure inserts) ----
  bound.requiredCycles = requiredCycles(entities.filter(x => x.inScope));
  bound.requiredSelfReferences = entities.filter(x => x.inScope).flatMap(be =>
    be.columns.filter(c => c.type === 'ref' && c.required === 'Y' && c.target === be.id && c.azure && c.azure.bound).map(c => `${be.id} ${be.name} / ${c.name}`));

  // ---- global choices for Dataverse ----
  // A column set column counts once, with the number of tables, so the list stays short.
  const used = new Map();
  for (const be of entities.filter(x => x.inScope)) {
    for (const c of be.columns) {
      if (c.dataverse && c.dataverse.kind === 'choice') {
        const key = choiceKeyOf(c.meaning);
        if (!used.has(key)) used.set(key, { columns: [], setCounts: new Map() });
        const u = used.get(key);
        if (c.set && !c.restated) {
          const label = `${c.name} (${c.set} column set)`;
          if (!u.setCounts.has(label)) { u.setCounts.set(label, 0); u.columns.push(label); }
          u.setCounts.set(label, u.setCounts.get(label) + 1);
        } else u.columns.push(`${be.id} ${c.name}`);
      }
    }
  }
  for (const u of used.values()) u.columns = u.columns.map(l => (u.setCounts.has(l) ? `${l.slice(0, -1)}, ${u.setCounts.get(l)} tables)` : l));
  bound.choices = [...used.entries()].map(([key, u]) => {
    const memberKeys = key === 'status-meaning' ? [...statusUnion] : [key];
    const options = [];
    for (const mk of memberKeys) {
      const c = contracts.get(mk);
      if (!c) continue;
      const derived = new Set(c.derivedValues || []);
      const proposed = new Set(c.proposedValues || []);
      for (const v of [...c.values, ...(c.proposedValues || [])]) {
        let o = options.find(x => x.label === v);
        if (!o) { o = { order: options.length, label: v, contracts: [], derived: false, status: 'adopted' }; options.push(o); }
        o.contracts.push(mk);
        if (derived.has(v)) o.derived = true;
        if (proposed.has(v)) o.status = 'proposal';
      }
    }
    const c = contracts.get(key);
    return {
      schemaName: choiceName(key),
      logicalName: lower(choiceName(key)),
      contract: key,
      contractStatus: c ? c.status : null,
      members: key === 'status-meaning' ? memberKeys : null,
      usedBy: u.columns,
      options,
      workingDefault: c && c.workingDefault ? c.workingDefault : null,
    };
  });

  if (problems.length) throw new BindError(problems);
  return bound;
}

// Strongly connected groups of entities whose required references point at each other. SQL Server checks a foreign key
// at every statement and has no deferred constraints, so the first rows of such a group cannot be inserted with every
// column NOT NULL. Self references are reported apart.
function requiredCycles(entities) {
  const ids = entities.map(e => e.id);
  const inScope = new Set(ids);
  const edges = new Map(ids.map(id => [id, []]));
  for (const e of entities) {
    for (const c of e.columns) {
      if (c.type !== 'ref' || c.required !== 'Y' || !c.azure || !c.azure.bound || !c.azure.foreignKey) continue;
      const t = c.target;
      if (t === e.id || !inScope.has(t)) continue;
      edges.get(e.id).push({ to: t, column: c.name });
    }
  }
  // Tarjan, iterative over model order so the output is stable.
  let index = 0;
  const idx = new Map();
  const low = new Map();
  const onStack = new Set();
  const stack = [];
  const groups = [];
  const strong = v => {
    idx.set(v, index); low.set(v, index); index++;
    stack.push(v); onStack.add(v);
    for (const { to } of edges.get(v)) {
      if (!idx.has(to)) { strong(to); low.set(v, Math.min(low.get(v), low.get(to))); }
      else if (onStack.has(to)) low.set(v, Math.min(low.get(v), idx.get(to)));
    }
    if (low.get(v) === idx.get(v)) {
      const g = [];
      let w;
      do { w = stack.pop(); onStack.delete(w); g.push(w); } while (w !== v);
      if (g.length > 1) groups.push(g);
    }
  };
  for (const v of ids) if (!idx.has(v)) strong(v);
  const order = new Map(ids.map((id, i) => [id, i]));
  return groups.map(g => {
    const members = [...g].sort((a, b) => order.get(a) - order.get(b));
    const set = new Set(members);
    const links = [];
    for (const m of members) for (const { to, column } of edges.get(m)) if (set.has(to)) links.push(`${m} ${column} -> ${to}`);
    // One line per distinct link; the common columns repeat on every entity, so keep each text once.
    return { members, links: [...new Set(links)] };
  });
}

// Wraps text into lines of at most width characters, each starting with lead. Deterministic, no hyphenation.
export function wrap(text, width, lead = '', next = lead) {
  const words = String(text).split(/\s+/).filter(Boolean);
  const lines = [];
  let line = lead;
  let fresh = true;
  for (const w of words) {
    if (!fresh && (line + ' ' + w).length > width) { lines.push(line); line = next + w; }
    else { line = fresh ? line + w : `${line} ${w}`; }
    fresh = false;
  }
  if (!fresh) lines.push(line);
  return lines;
}

// Stable JSON text with a final newline. Two-space indent, except that a small object or array inside an array (at most
// two levels deep, such as one column) is written on one line, so each column is one line in a diff.
const height = v => (v === null || typeof v !== 'object' ? 0 : 1 + Math.max(0, ...(Array.isArray(v) ? v : Object.values(v)).map(height)));
export function jsonText(value) {
  const emit = (v, depth, inArray) => {
    if (v === null || typeof v !== 'object') return JSON.stringify(v);
    if (inArray && height(v) <= 2) return JSON.stringify(v);
    const pad = '  '.repeat(depth + 1);
    const end = '  '.repeat(depth);
    if (Array.isArray(v)) {
      if (!v.length) return '[]';
      return `[\n${v.map(x => pad + emit(x, depth + 1, true)).join(',\n')}\n${end}]`;
    }
    const keys = Object.keys(v).filter(k => v[k] !== undefined);
    if (!keys.length) return '{}';
    return `{\n${keys.map(k => `${pad}${JSON.stringify(k)}: ${emit(v[k], depth + 1, false)}`).join(',\n')}\n${end}}`;
  };
  return `${emit(value, 0, false)}\n`;
}

// The source block every generated file carries.
export function sourceBlock(bound) {
  return {
    model: MODEL_PATH,
    modelVersion: bound.model.modelVersion,
    modelDate: bound.model.date,
    modelSha256: bound.hash,
    modelSha256Note: 'SHA-256 of the parsed model written back as compact JSON, so line endings do not change it',
  };
}

export const PLACEHOLDERS = {
  [PREFIX]: 'The Dataverse publisher prefix, picked with the product name (D-05). tools/rename-product.mjs swaps it',
  Product: 'The product name placeholder. Stays until the product owner picks the name',
};
