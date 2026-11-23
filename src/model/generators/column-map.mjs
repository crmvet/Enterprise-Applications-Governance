// Column map: logical entity and column names to both physical names.
// The engine core works on logical names only; each adapter maps them to schema names (document 25, sections 3.2
// and 11.1). This file is that map for both bindings, generated from the logical model.
import { DRAFT, GENERATOR, NOT_APPLIED, PLACEHOLDERS, AZURE_HISTORY_TEXT, sourceBlock, jsonText } from './physical.mjs';

function dataverseEntry(c) {
  const d = c.dataverse;
  if (!d || !d.bound) return { bound: false, reason: d ? d.reason : 'not bound' };
  const out = { schemaName: d.schemaName, logicalName: d.logicalName, kind: d.kind };
  if (d.storedIn) out.storedIn = d.storedIn;
  if (d.kind === 'lookup') { out.target = d.target; out.relationship = d.relationship; }
  if (d.kind === 'choice') out.globalChoice = d.globalChoice;
  if (d.autonumberFormat) out.autonumberFormat = d.autonumberFormat;
  if (d.note) out.note = d.note;
  return out;
}

function azureEntry(c) {
  const a = c.azure;
  if (!a || !a.bound) return { bound: false, reason: a ? a.reason : 'not bound' };
  if (a.storedIn) return { column: a.column, storedIn: a.storedIn };
  if (a.inView) return { column: a.column, sqlType: a.sqlType, inView: a.inView, note: a.note };
  const out = { column: a.column, sqlType: a.sqlType, nullable: a.nullable };
  if (a.foreignKey) out.references = a.foreignKey.table;
  return out;
}

export function renderColumnMap(bound) {
  const entities = bound.entities.map(e => {
    const entry = {
      id: e.id,
      name: e.name,
      kind: e.kind,
      status: e.status,
      release: e.release,
      inDrafts: e.inScope,
    };
    if (!e.inScope) entry.inDraftsNote = e.storedIn ? `Stored as rows of ${e.storedIn}, so it has no table of its own` : 'Later entity: named here, but no table in the SQL or Dataverse drafts';
    if (e.workingDefault) entry.workingDefault = e.workingDefault;
    entry.dataverse = { table: e.dataverse.schemaName, logicalName: e.dataverse.logicalName, ownership: e.dataverse.ownership };
    entry.azureSql = { table: e.azure.qualified, history: e.azure.history ? AZURE_HISTORY_TEXT[e.azure.history] : null };
    if (e.storedIn) {
      entry.dataverse.storedIn = e.storedIn;
      entry.azureSql.storedIn = e.storedIn;
    }
    entry.columns = e.columns.map(c => {
      const col = { name: c.name, set: c.set, type: c.type };
      if (c.restated) col.restated = true;
      if (c.status) col.status = c.status;
      if (c.workingDefault) col.workingDefault = c.workingDefault;
      col.dataverse = dataverseEntry(c);
      col.azureSql = azureEntry(c);
      return col;
    });
    return entry;
  });
  const columnCount = entities.reduce((n, e) => n + e.columns.length, 0);
  return jsonText({
    title: 'Product column map: logical names to physical names',
    status: DRAFT,
    notApplied: NOT_APPLIED,
    generatedBy: `${GENERATOR}. Do not edit by hand: change the model and run "node model/generate.mjs write"`,
    source: sourceBlock(bound),
    purpose: 'Maps every logical entity and column name of the model to its Dataverse schema name and its Azure SQL name. The engine core uses logical names only; adapters use this map (document 25, sections 3.2 and 11.1)',
    placeholders: PLACEHOLDERS,
    naming: [
      'Dataverse (document 20, section 3.4): tables and columns are <prefix>_ plus the logical name in PascalCase; a lookup ends with Id; the logical name is the lowercase schema name. The primary key is <prefix>_<Table>Id and the primary name is <prefix>_Name. Created On, Created By, Modified On and Modified By are the built-in columns. Is Active on a configuration table is the platform state (document 22, section 10.1)',
      'Azure SQL (document 20, section 3.5; document 22, section 11.1): schema config for configuration entities, ops for operational entities, audit for O24; tables are the logical name in PascalCase with no prefix; a reference column ends with Id; the primary key is Id',
      'Derived columns that are never stored have no physical column. Taxonomy Label is stored in Name. A column restated by an entity is the same physical column as the column set column (model open point 8)',
    ],
    workingDefaults: [
      `${bound.model.meaningContracts.workingDefault} Here every meaning column maps to a Dataverse choice column and an Azure varchar(32) column with a check constraint. If the answer is no, these columns become references to configuration rows on both bindings.`,
    ],
    counts: { entities: entities.length, inDrafts: entities.filter(e => e.inDrafts).length, columns: columnCount },
    entities,
  });
}
