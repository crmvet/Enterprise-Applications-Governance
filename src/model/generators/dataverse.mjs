// Draft Dataverse metadata description: tables, columns, choices and lookups, alternate keys, ownership and
// relationships, bound as document 22, section 10 says. JSON only. Nothing here talks to an environment; the
// solution is built from it later, after D-22 and the name pick.
import { DRAFT, GENERATOR, NOT_APPLIED, PLACEHOLDERS, DATAVERSE_AUDIT_OFF, DATAVERSE_NAME_GUIDE, sourceBlock, jsonText } from './physical.mjs';

const DEFAULT_OWNER = 'A records owner team with no members (the lockbox pattern), or in portfolio read mode the group team that Portfolio Access (C44) links to the portfolio (document 22, section 10.1)';

function columnEntry(c) {
  const d = c.dataverse;
  const out = { logicalColumnName: c.name, set: c.set };
  if (d.storedIn) return { ...out, storedIn: d.storedIn, schemaName: d.schemaName, logicalName: d.logicalName };
  out.schemaName = d.schemaName;
  out.logicalName = d.logicalName;
  out.kind = d.kind;
  for (const k of ['attributeType', 'format', 'maxLength', 'precision']) if (d[k] !== undefined) out[k] = d[k];
  if (d.kind === 'lookup') out.target = d.target;
  if (d.kind === 'choice') out.globalChoice = d.globalChoice;
  if (d.autonumberFormat) out.autonumberFormat = d.autonumberFormat;
  if (d.kind !== 'built-in' && d.kind !== 'platform-state') {
    out.logicalRequired = c.required;
    out.requiredLevel = c.required === 'Y' ? 'ApplicationRequired' : 'None';
  }
  out.audited = !DATAVERSE_AUDIT_OFF.has(c.name);
  if (c.restated) out.restated = true;
  if (c.fillOnce) out.fillOnce = true;
  if (c.derived) out.derived = 'Stored by the engine on Dataverse (document 22, section 8.1)';
  if (c.status) { out.status = c.status; if (c.source) out.source = c.source; }
  if (c.workingDefault) out.workingDefault = c.workingDefault;
  if (d.note) out.note = d.note;
  out.description = c.description;
  return out;
}

export function renderDataverse(bound) {
  const model = bound.model;
  const classes = new Map(model.historyClasses.map(h => [h.id, h]));
  const tables = bound.entities.filter(e => e.inScope);
  const counts = { tables: tables.length, columns: 0, lookups: 0, choiceColumns: 0, globalChoices: bound.choices.length, alternateKeys: 0, relationships: 0, autonumberColumns: 0 };

  const tableEntries = tables.map(e => {
    const cls = classes.get(e.historyClass);
    const bound_ = e.columns.filter(c => c.dataverse && c.dataverse.bound);
    const columns = bound_.map(columnEntry);
    const notStored = e.columns.filter(c => c.dataverse && !c.dataverse.bound).map(c => ({ logicalColumnName: c.name, reason: c.dataverse.reason }));
    const relationships = bound_.filter(c => c.dataverse.kind === 'lookup').map(c => ({
      id: c.relationship ? c.relationship.id : null,
      schemaName: c.dataverse.relationship,
      lookup: c.dataverse.logicalName,
      referencedTable: c.dataverse.target,
      cardinality: c.relationship ? c.relationship.cardinality : null,
      behaviour: c.dataverse.behaviour,
    }));
    counts.columns += columns.filter(x => !x.storedIn).length;
    counts.lookups += relationships.length;
    counts.relationships += relationships.length;
    counts.choiceColumns += columns.filter(x => x.kind === 'choice').length;
    counts.alternateKeys += e.dataverse.alternateKeys.length;
    counts.autonumberColumns += columns.filter(x => x.autonumberFormat).length;

    const engineChecks = [
      ...e.constraints.map(k => ({ kind: k.kind, columns: k.columns, note: k.note })),
      ...(e.subjects ? [{ kind: 'subject-matches-kind', columns: [e.subjects.kindColumn, ...e.subjects.options.map(o => o.column)], note: `One plain lookup per subject kind (document 22, section 2.6). The set lookup matches ${e.subjects.kindColumn}` }] : []),
    ];

    const entry = {
      entityId: e.id,
      entityName: e.name,
      status: e.status,
    };
    if (e.status === 'proposal') entry.proposal = `Not yet accepted (${e.source})`;
    if (e.workingDefault) entry.workingDefault = e.workingDefault;
    Object.assign(entry, {
      schemaName: e.dataverse.schemaName,
      logicalName: e.dataverse.logicalName,
      displayName: e.dataverse.displayName,
      displayCollectionName: e.dataverse.displayCollectionName,
      description: e.purpose,
      tableType: 'Standard',
      ownership: e.dataverse.ownership,
    });
    if (e.dataverse.ownership === 'UserOwned') entry.defaultOwner = DEFAULT_OWNER;
    Object.assign(entry, {
      auditing: true,
      historyClass: `${e.historyClass} (${cls.name.toLowerCase()})`,
      historyBinding: cls.dataverse,
      platformState: e.dataverse.platformState,
    });
    if (e.kind === 'configuration') {
      entry.configurationWrites = e.id === 'C46'
        ? 'Written only by the engine\'s draft operations; frozen at submit (document 22, section 10.2)'
        : 'A synchronous plug-in refuses any write that does not come from the activation operation (document 22, section 10.1)';
    }
    entry.deletePolicy = e.deletePolicy;
    entry.primaryKey = (bound_.find(c => c.dataverse.kind === 'primary-key') || { dataverse: {} }).dataverse.logicalName || null;
    entry.primaryName = (bound_.find(c => c.dataverse.kind === 'primary-name') || { dataverse: {} }).dataverse.logicalName || null;
    entry.columns = columns;
    if (notStored.length) entry.notStored = notStored;
    entry.alternateKeys = e.dataverse.alternateKeys;
    if (e.dataverse.engineKeys.length) entry.keysCheckedByEngine = e.dataverse.engineKeys;
    if (engineChecks.length) entry.rulesCheckedByEngine = engineChecks;
    entry.relationships = relationships;
    return entry;
  });

  const storedIn = bound.entities.filter(e => e.storedIn).map(e => ({ entityId: e.id, entityName: e.name, storedIn: e.storedIn, table: e.dataverse.logicalName, note: `Rows of ${e.storedIn} (${bound.byId.get(e.storedIn).name}); no table of its own` }));
  const later = bound.entities.filter(e => e.status === 'later').map(e => `${e.id} ${e.name}`);

  const review = [];
  const has = (id, cols) => { const e = bound.byId.get(id); return e && e.dataverse.alternateKeys.some(k => k.columns.join(' + ') === cols); };
  if (has('O01', 'Name + Parent Portfolio') && has('O19', 'Business Application + Documentation Requirement')) {
    review.push('Document 22, section 10.2 lists no alternate key for O01 Portfolio (Name + Parent Portfolio) or O19 Document Link (Business Application + Documentation Requirement). Section 10.1 puts one on every section 7 key with text and lookup columns, and this draft follows section 10.1, as the model follows section 7 for C07, C11, C30 and C33 (model open point 5).');
  }
  const c46 = bound.byId.get('C46');
  if (c46 && c46.recordNumberPrefix) review.push(`Document 22, section 10.1 lists autonumber Record Number columns on O02, O07, O09 and O11. The model also gives C46 a record number prefix (${c46.recordNumberPrefix}), so this draft adds one there.`);
  review.push('Option values follow the publisher\'s option value prefix, which comes with the publisher. This draft lists each choice\'s options in order only.');
  review.push('C02 Content Json may pass the multiline text limit of 1,048,576 characters. Document 22, section 10.3 gives the fallback, a compressed file column with the hash kept in the row. It is not in this draft, and file column limits are UNVERIFIED.');
  review.push('O24 Row Id is a row ID held as a value. Custom tables offer no GUID column (UNVERIFIED here), so this draft uses text of 36 characters.');
  review.push(`Document 20, section 3.4 keeps schema names under ${DATAVERSE_NAME_GUIDE} characters including the prefix (UNVERIFIED there). Measured with an 8-character prefix, ${bound.longDataverseNames.length} table, column or key name(s) reach that length${bound.longDataverseNames.length ? `: ${bound.longDataverseNames.map(x => x.name).join(', ')}` : ''}.`);
  review.push('requiredLevel ApplicationRequired marks required Y. Engine-owned columns are filled by the engine\'s PreOperation step (document 25, section 11.3), not by people.');
  for (const w of bound.warnings) review.push(`Generator warning: ${w}.`);

  return jsonText({
    title: 'Product Dataverse metadata description',
    status: DRAFT,
    notApplied: NOT_APPLIED,
    generatedBy: `${GENERATOR}. Do not edit by hand: change the model and run "node model/generate.mjs write"`,
    source: sourceBlock(bound),
    binding: 'Document 22, section 10 (Dataverse). Names: document 20, section 3.4',
    placeholders: PLACEHOLDERS,
    rules: [
      'Every table is a standard table with auditing on (document 22, section 10.1)',
      'Configuration tables (C01, C02 and C04 to C46), O05 and O24 are organization owned. The other operational tables are user or team owned. Ownership cannot change after a table is created [T1]',
      'Configurable lists are lookups to configuration tables, never choice columns (D-06). Engine contract values are global choices, one per contract; every status contract shares the global choice status meaning, and the engine checks that a meaning fits the entity',
      'Is Active binds to the platform state on configuration tables. Operational tables keep the platform state Active and use the product Status lookup',
      'JSON is multiple lines of text in Text format, validated by the engine on every write [T7]',
      'Alternate keys sit on every section 7 key whose columns are text, lookups or whole numbers, at most 10 per table and 900 bytes per key [S15]. Other keys are checked by the engine',
      'Restrict is Referential, Restrict Delete. The one Cascade relationship (R80) is Parental [T2]',
      'Auditing is off for Last Seen On, Times Seen, Last Seen Run and Owner Checked On, which change on every gap check run',
      'Record Number is an autonumber column where the model gives a record number prefix [T11]',
      'A subject that can be one of several entities is one plain lookup per kind (document 22, section 2.6)',
    ],
    workingDefaults: [
      `${model.meaningContracts.workingDefault} On this binding every meaning column is a choice column on a global choice. If the answer is no, these columns become lookups to configuration rows.`,
    ],
    counts,
    globalChoices: bound.choices,
    tables: tableEntries,
    storedInOtherTables: storedIn,
    notInThisDraft: [
      'Plug-in step registration (document 25, section 11.3): generated later from the same model',
      'Custom APIs',
      'Security roles, column security profiles and group teams, including the column security on O10 outcome columns (document 22, section 10.2)',
      'Forms, views, the model-driven app and generative pages',
      'The publisher, the solution and packaging',
      'The C02 file column fallback (document 22, section 10.3)',
      `Later entities: ${later.join(', ')}`,
    ],
    reviewPoints: review,
  });
}
