// Physical names printout: the generator stub prints both physical names for every
// column. One line per entity and one per column: the logical name, the Dataverse schema name and the Azure SQL name.
import { DRAFT, NOT_APPLIED, PREFIX } from './physical.mjs';

function dataverseCell(e, c) {
  if (!c) {
    if (e.storedIn) return `stored in ${e.storedIn} (${e.dataverse.schemaName})`;
    return e.dataverse.schemaName;
  }
  const d = c.dataverse;
  if (!d || !d.bound) return `(none: ${d ? d.reason : 'not bound'})`;
  if (d.storedIn) return `${d.schemaName} (stored in ${d.storedIn})`;
  if (d.kind === 'built-in') return `${d.schemaName} (built-in)`;
  if (d.kind === 'platform-state') return `${d.schemaName} (platform state)`;
  return d.schemaName;
}

function azureCell(e, c) {
  if (!c) {
    if (e.storedIn) return `stored in ${e.storedIn} (${e.azure.qualified})`;
    return e.azure.qualified;
  }
  const a = c.azure;
  if (!a || !a.bound) return `(none: ${a ? a.reason : 'not bound'})`;
  if (a.storedIn) return `${a.column} (stored in ${a.storedIn})`;
  if (a.inView) return `${a.column} (view ${a.inView})`;
  return a.column;
}

export function renderNames(bound, { entity } = {}) {
  const chosen = bound.entities.filter(e => !entity || e.id === entity);
  const rows = [];
  for (const e of chosen) {
    const tag = e.inScope ? '' : e.storedIn ? ' (no table)' : ' (later, no table in the drafts)';
    rows.push([e.id, `${e.name}${e.status === 'proposal' ? ' (proposal)' : ''}${tag}`, dataverseCell(e, null), azureCell(e, null)]);
    for (const c of e.columns) rows.push(['', `  ${c.name}`, dataverseCell(e, c), azureCell(e, c)]);
  }
  const w1 = Math.max(...rows.map(r => r[1].length), 'Entity / column'.length);
  const w2 = Math.max(...rows.map(r => r[2].length).filter(n => n <= 48), 'Dataverse schema name'.length);
  const line = r => `${r[0].padEnd(4)} ${r[1].padEnd(w1)}  ${r[2].padEnd(w2)}  ${r[3]}`.trimEnd();
  const columns = chosen.reduce((n, e) => n + e.columns.length, 0);
  const out = [
    `Physical names. Status: ${DRAFT}. ${NOT_APPLIED}`,
    `Model ${bound.model.modelVersion} (${bound.model.date}), canonical SHA-256 ${bound.hash}. Prefix placeholder: ${PREFIX}.`,
    '',
    line(['ID', 'Entity / column', 'Dataverse schema name', 'Azure SQL name']),
    ...rows.map(line),
    '',
    `${chosen.length} entities, ${columns} columns. ${chosen.filter(e => e.inScope).length} entities have a table in the drafts.`,
  ];
  return { text: `${out.join('\n')}\n`, entities: chosen.length, columns };
}
