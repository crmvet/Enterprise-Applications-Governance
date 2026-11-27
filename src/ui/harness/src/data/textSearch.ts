// Text search over the rows a list page already holds (wave 4: "no search, no FILTER for list of APPS?"). The pages
// that read a list tool with a text input (find_applications, list_intake_requests, list_teams, list_data_uses,
// list_data_mappings, find_data_domains, find_data_locations, list_deployment_targets, find_people) send the words to
// the tool instead; these helpers serve the lists whose tool takes no text, so every list page searches the same way.
//
//   normalizeText(' Café  Ops ')                      'cafe ops': lower case, accents and extra spaces dropped
//   searchWords('payroll  "field ops"')               ['payroll', 'field', 'ops']: the words a query asks for
//   matchesText('field pay', 'Field Ops', 'Payroll')  true: every word is found in at least one field
//   filterByText(rows, q, r => [r.name, r.owner])     the rows that match, in their order; every row for no query
//
// A word matches anywhere inside a field ("insp" finds "Inspection Scheduler"), and a record number matches with or
// without its dash ("dec0101" finds "DEC-0101"), as the global search reads one.

export type SearchField = string | number | null | undefined | readonly (string | number | null | undefined)[];

// Lower case, accents dropped, every run of spaces one space, trimmed.
export function normalizeText(value: string): string {
  return value
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

// A straight or curly double quote: the words inside quotes are searched as words.
const QUOTES = new RegExp(`["${String.fromCharCode(0x201c)}${String.fromCharCode(0x201d)}]`, 'g');

// The words of a query, normalized, quotes left out, at most 12 words of up to 100 characters each.
export function searchWords(query: string | null | undefined): string[] {
  if (!query) return [];
  return normalizeText(query.slice(0, 200))
    .replace(QUOTES, ' ')
    .split(' ')
    .map(w => w.slice(0, 100))
    .filter(Boolean)
    .slice(0, 12);
}

const squash = (text: string) => text.replace(/[-_\s.]/g, '');

function fieldTexts(fields: readonly SearchField[]): string[] {
  const out: string[] = [];
  for (const f of fields) {
    if (f === null || f === undefined) continue;
    if (Array.isArray(f)) {
      for (const v of f) if (v !== null && v !== undefined) out.push(normalizeText(String(v)));
    } else {
      out.push(normalizeText(String(f)));
    }
  }
  return out;
}

// True when every word of the query is found in one of the fields. An empty query matches every row.
export function matchesText(query: string | null | undefined, ...fields: SearchField[]): boolean {
  const words = searchWords(query);
  if (!words.length) return true;
  const texts = fieldTexts(fields);
  if (!texts.length) return false;
  const squashed = texts.map(squash);
  return words.every(w => texts.some(t => t.includes(w)) || squashed.some(t => t.includes(squash(w))));
}

// The rows whose fields match the query, in their own order. An empty query keeps every row (the same array).
export function filterByText<T>(rows: readonly T[], query: string | null | undefined, fields: (row: T) => readonly SearchField[]): readonly T[] {
  if (!searchWords(query).length) return rows;
  return rows.filter(row => matchesText(query, ...fields(row)));
}
