// The generated files of the model generators, in the order they are written. generate.mjs writes them and check-generated.mjs
// compares them.
import { renderColumnMap } from './column-map.mjs';
import { renderAzureSql } from './azure-sql.mjs';
import { renderDataverse } from './dataverse.mjs';

export const OUTPUTS = ['column-map.json', 'azure-sql-0001-initial.draft.sql', 'dataverse-metadata.draft.json'];

// Renders every output. Pure: the same bound model gives the same text.
export function renderAll(bound) {
  const sql = renderAzureSql(bound);
  return {
    files: [
      { name: OUTPUTS[0], text: renderColumnMap(bound) },
      { name: OUTPUTS[1], text: sql.text },
      { name: OUTPUTS[2], text: renderDataverse(bound) },
    ],
    sqlStats: sql.stats,
  };
}
