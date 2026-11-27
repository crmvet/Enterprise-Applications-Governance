// The Configuration page's logic, with no React in it: the areas the rules are grouped in, configuration values in
// words, the operations a draft builder sends, a diff read as sentences, the data domains a change touches, and the
// words of versions and drafts. The pages and the tests read the same code.
import type { ConfigurationChanges, ConfigurationDiffEntry, ConfigurationRows, DataDomainItem, DraftStateKey, GateLevel, PresetList } from '../../data/contracts';
import { fmtDate, humanizeKey } from '../../data/format';
import type { Tone } from '../../data/meanings';
import { entitySpec, FIELD_SPECS, fieldLabelOf, type EntitySpec, type FieldSpec } from './fieldSpecs';

export type ConfigRow = ConfigurationRows['rows'][number];

// ---------- areas ----------

// The areas the rules view groups the entities in, in the order a configuration admin reads them (D-41: the design
// names the parts of "the rules in force" but no grouping, so the page groups them by the process they serve).
export const AREAS = [
  { key: 'tiers', label: 'Tiers and questions', entities: ['tier', 'tier-question', 'tier-answer-option', 'application-kind', 'component-kind', 'vocabulary-mapping'] },
  { key: 'gates', label: 'Lifecycle and gates', entities: ['lifecycle-stage', 'gate', 'gate-requirement', 'gate-policy', 'target-stage', 'change-path', 'deployment-route'] },
  { key: 'decisions', label: 'Decisions and approvals', entities: ['decision-type', 'decision-outcome', 'decision-right', 'approval-rule', 'separation-rule'] },
  { key: 'change', label: 'Change types', entities: ['change-type', 'standard-change-model'] },
  { key: 'reviews', label: 'Reviews and documents', entities: ['review-type', 'review-cadence', 'reminder-rule', 'document-type', 'documentation-requirement'] },
  { key: 'people', label: 'People and access', entities: ['party', 'owner-role', 'role', 'role-group-binding', 'workload-grant', 'tool-policy', 'column-protection-rule', 'portfolio-access', 'known-client', 'actor-kind'] },
  { key: 'data', label: 'Data taxonomy', entities: ['data-classification', 'data-domain-level', 'data-domain', 'data-platform'] },
  { key: 'lists', label: 'Settings, statuses and lists', entities: ['setting', 'status', 'value-list', 'list-value', 'dependency-type', 'link-kind', 'guideline'] },
] as const;

export type AreaKey = (typeof AREAS)[number]['key'];
export const AREA_KEYS: readonly string[] = AREAS.map(a => a.key);

// The area of an entity; an entity no area names (a later product release) falls under the last one.
export function areaOf(entity: string): AreaKey {
  return AREAS.find(a => (a.entities as readonly string[]).includes(entity))?.key ?? 'lists';
}

// Every entity in area order: the named ones first, then any the field facts know and no area names.
export function entitiesInOrder(): string[] {
  const named: string[] = AREAS.flatMap(a => [...a.entities]);
  const rest = FIELD_SPECS.entities.map(e => e.entity).filter(e => !named.includes(e));
  return [...named, ...rest];
}

export function entityName(entity: string): string {
  return entitySpec(entity)?.name ?? humanizeKey(entity);
}

// ---------- labels of the rows a value points to ----------

// The label of a configuration row by entity and key ("list-value", "retention-class/keep-7-years"), or null.
export type LabelLookup = (entity: string, key: string) => string | null;

export function lookupOf(rows: readonly ConfigRow[]): LabelLookup {
  const map = new Map(rows.map(r => [`${r.entity}|${r.key}`, r.label]));
  return (entity, key) => map.get(`${entity}|${key}`) ?? null;
}

// A key that names a row of any entity, for the engine's sentences ("`no-self-approval-tier-3`").
export function anyKeyLookup(rows: readonly ConfigRow[]): (key: string) => string | null {
  const map = new Map<string, string>();
  for (const r of rows) if (!map.has(r.key)) map.set(r.key, r.label);
  return key => map.get(key) ?? null;
}

// ---------- values in words ----------

// A label inside a sentence: its first letter in lowercase, unless its first word is an acronym ("AI lead", "SQL Server")
// or a name in capitals. "Business owner" reads "business owner"; "AI lead" stays "AI lead".
export function inSentence(label: string): string {
  const first = label.split(/\s+/)[0] ?? '';
  if (first.length > 1 && first === first.toUpperCase() && /[A-Z]/.test(first)) return label;
  if (first.length > 1 && /[A-Z]/.test(first.slice(1))) return label;
  return label.charAt(0).toLowerCase() + label.slice(1);
}

const isObj = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

// A value of a fixed engine list in words: "everyone-must-approve" becomes "Everyone must approve".
export function choiceText(value: unknown): string {
  return typeof value === 'string' ? humanizeKey(value) : String(value);
}

// The entity and key a key field's value names, for the lookup: a list value sits under its list, an implied answer
// under its tier question, and a vocabulary mapping's row under the entity it names.
export function refTarget(field: FieldSpec, key: string, row: Record<string, unknown>): { entity: string; key: string } | null {
  if (field.refFrom) {
    const entity = row[field.refFrom];
    return typeof entity === 'string' ? { entity, key } : null;
  }
  if (field.parentFrom) {
    const parent = row[field.parentFrom];
    return field.ref && typeof parent === 'string' ? { entity: field.ref, key: `${parent}/${key}` } : null;
  }
  if (!field.ref) return null;
  return { entity: field.ref, key: field.list ? `${field.list}/${key}` : key };
}

function keyText(field: FieldSpec, key: string, row: Record<string, unknown>, lookup: LabelLookup): string {
  if (key === '*') return 'Any';
  const target = refTarget(field, key, row);
  return (target ? lookup(target.entity, target.key) : null) ?? humanizeKey(key);
}

// The entity and list a setting's valueType names: key:<entity>, key:list-value:<list>, contract:<name>.
export function settingShape(valueType: unknown): { kind: 'text' | 'number' | 'yes-no' | 'json' | 'key' | 'contract'; ref?: string; list?: string; contract?: string } {
  if (typeof valueType !== 'string') return { kind: 'json' };
  if (valueType === 'text' || valueType === 'number' || valueType === 'yes-no' || valueType === 'json') return { kind: valueType };
  const parts = valueType.split(':');
  if (parts[0] === 'key') return parts[1] === 'list-value' && parts[2] ? { kind: 'key', ref: 'list-value', list: parts[2] } : { kind: 'key', ref: parts[1] };
  if (parts[0] === 'contract') return { kind: 'contract', contract: parts[1] };
  return { kind: 'json' };
}

// A field's value in words, never raw JSON, an object ID or an internal key: a key shows the label of the row it names,
// a structured value says it is set.
export function valueText(field: FieldSpec | null, value: unknown, row: Record<string, unknown>, lookup: LabelLookup): string {
  if (value === null || value === undefined || value === '') return 'Not set';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (!field) {
    if (typeof value === 'number') return value.toLocaleString('en-US');
    if (typeof value === 'string') return /^[a-z][a-z0-9-]*$/.test(value) && value.includes('-') ? humanizeKey(value) : value;
    if (Array.isArray(value)) return value.length === 1 ? 'One item' : `${value.length} items`;
    return 'A structured value';
  }
  switch (field.kind) {
    case 'yes-no':
      return value ? 'Yes' : 'No';
    case 'integer':
    case 'number':
      return typeof value === 'number' ? value.toLocaleString('en-US') : String(value);
    case 'key':
      return typeof value === 'string' ? keyText(field, value, row, lookup) : 'Not set';
    case 'keys':
      return Array.isArray(value) ? (value.length ? value.map(v => (typeof v === 'string' ? keyText(field, v, row, lookup) : String(v))).join(', ') : 'None') : 'Not set';
    case 'choice':
      return choiceText(value);
    case 'choices':
      return Array.isArray(value) ? (value.length ? value.map(choiceText).join(', ') : 'None') : choiceText(value);
    case 'entity':
      return typeof value === 'string' ? entityName(value) : 'Not set';
    case 'date':
      return typeof value === 'string' ? fmtDate(value, value) : 'Not set';
    case 'text':
    case 'long-text':
    case 'url':
      return String(value);
    case 'setting-value': {
      const shape = settingShape(row.valueType);
      if (shape.kind === 'yes-no') return value ? 'Yes' : 'No';
      if (shape.kind === 'number' || shape.kind === 'text') return String(value);
      if (shape.kind === 'key' && typeof value === 'string') {
        return lookup(shape.ref ?? '', shape.list ? `${shape.list}/${value}` : value) ?? humanizeKey(value);
      }
      if (shape.kind === 'contract') return choiceText(value);
      return Array.isArray(value) ? (value.length === 1 ? 'A list of one item' : `A list of ${value.length} items`) : 'A structured value';
    }
    case 'kept':
    default:
      if (Array.isArray(value)) return value.length === 1 ? 'One item' : `${value.length} items`;
      if (isObj(value)) return 'Set';
      // A kept scalar can be an ID (a group, a portfolio): never shown.
      return 'Set';
  }
}

// The field facts of one field of an entity, or a plain one for a field the facts do not know.
export function fieldOf(entity: string, name: string): FieldSpec | null {
  return entitySpec(entity)?.fields.find(f => f.name === name) ?? null;
}

export function fieldLabel(entity: string, name: string): string {
  return fieldOf(entity, name)?.label ?? fieldLabelOf(name);
}

// Fields a person never reads as a row's facts: the key and the engine's own columns.
const HIDDEN_FIELDS = new Set(['key', 'origin', 'isActive', 'label']);

// The facts of a row in words, in the order the entity's fields are defined, set fields only.
export function rowFacts(entity: string, fields: Record<string, unknown>, lookup: LabelLookup): { name: string; label: string; text: string; help: string }[] {
  const spec = entitySpec(entity);
  const known = spec?.fields.map(f => f.name) ?? [];
  const names = [...known.filter(n => n in fields), ...Object.keys(fields).filter(n => !known.includes(n)).sort()];
  return names
    .filter(n => !HIDDEN_FIELDS.has(n) && fields[n] !== undefined && fields[n] !== null && fields[n] !== '')
    .map(n => {
      const field = fieldOf(entity, n);
      return { name: n, label: field?.label ?? fieldLabelOf(n), text: valueText(field, fields[n], fields, lookup), help: field?.help ?? '' };
    });
}

// One line that says what a row does, in plain words, for the list. A few entities read as a sentence; the others list
// their first facts.
export function rowSummary(entity: string, fields: Record<string, unknown>, lookup: LabelLookup): string {
  const t = (name: string) => valueText(fieldOf(entity, name), fields[name], fields, lookup);
  const has = (name: string) => fields[name] !== undefined && fields[name] !== null && fields[name] !== '';
  const scope = (names: string[]) => names.filter(has).map(t).join(', ');
  switch (entity) {
    case 'approval-rule': {
      const who = has('approverRoleKey') ? `the ${inSentence(t('approverRoleKey'))}` : has('approverOwnerRoleKey') ? `the ${inSentence(t('approverOwnerRoleKey'))}` : fields.approverProposedOwners ? 'each proposed owner, by name' : 'nobody named';
      const where = scope(['tierKey', 'lifecycleStageKey', 'changeTypeKey', 'applicationKindKey', 'targetStageKey', 'classificationKey', 'dataDomainLevelKey']);
      const how = [has('approvalMode') ? inSentence(t('approvalMode')) : null, fields.isConsultation ? 'advice only' : null, fields.requiresTypedReason ? 'a typed reason' : null].filter(Boolean).join(', ');
      return `${t('decisionTypeKey')}${where ? ` (${where})` : ''}: ${who} ${fields.isConsultation ? 'is consulted' : 'approves'}${how ? `; ${how}` : ''}.`;
    }
    case 'decision-right': {
      const who = has('roleKey') ? t('roleKey') : has('ownerRoleKey') ? `The ${inSentence(t('ownerRoleKey'))}` : 'An AI client or a person';
      const where = scope(['tierKey', 'lifecycleStageKey', 'changeTypeKey', 'applicationKindKey', 'classificationKey', 'dataDomainLevelKey']);
      return `${who} is ${inSentence(t('letter'))} for ${inSentence(t('decisionTypeKey'))}${where ? ` (${where})` : ''}.`;
    }
    case 'gate-policy':
      return `${t('tierKey')}, ${inSentence(t('changeTypeKey'))} change, ${inSentence(t('targetStageKey'))}: ${inSentence(t('gateLevel'))}${has('failMode') ? `, ${inSentence(t('failMode'))}` : ''}.`;
    case 'separation-rule': {
      const where = scope(['decisionTypeKey', 'tierKey', 'targetStageKey']);
      const what = fields.ruleKind === 'roles-in-different-groups' ? `These roles never share a group: ${t('roleKeys')}` : `The approver must differ from the ${inSentence(t('mustDifferFrom'))}`;
      return `${what}${where ? ` (${where})` : ''}. ${t('enforcement')}.`;
    }
    case 'setting':
      return `${t('value')}${fields.isLocked ? '. Locked' : ''}.`;
    case 'review-cadence':
      return `${t('reviewTypeKey')} for ${inSentence(t('tierKey'))}: every ${t('intervalDays')} days.`;
    case 'documentation-requirement':
      return `${t('documentTypeKey')} from ${inSentence(t('fromStageKey'))}${has('tierKey') ? `, ${inSentence(t('tierKey'))}` : ''}.`;
    case 'gate-requirement':
      return `${t('gateKey')}${has('tierKey') ? `, ${inSentence(t('tierKey'))}` : ''}: ${inSentence(t('requirementKind'))}.`;
    default: {
      const facts = rowFacts(entity, fields, lookup).filter(f => f.name !== 'description' && f.name !== 'sortOrder');
      if (facts.length === 0) return typeof fields.description === 'string' ? fields.description : '';
      // Each fact without its own closing period, a long one cut short: the row's facts list holds every word.
      const short = (text: string) => {
        const plain = text.replace(/[.\s]+$/, '');
        return plain.length > 140 ? `${plain.slice(0, 137).trimEnd()}...` : plain;
      };
      return facts.slice(0, 3).map(f => `${f.label}: ${short(f.text)}`).join('. ') + '.';
    }
  }
}

// True when the row's label, summary or facts hold the search words (every word, any order, any case).
export function rowMatches(row: ConfigRow, text: string, lookup: LabelLookup): boolean {
  const words = text.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return true;
  const hay = [row.label, entityName(row.entity), typeof row.fields.description === 'string' ? row.fields.description : '', rowSummary(row.entity, row.fields, lookup)]
    .join(' ')
    .toLowerCase();
  return words.every(w => hay.includes(w));
}

// ---------- keys of new rows ----------

// A key for a new row, made from its label: lowercase words joined by hyphens, starting with a letter, at most 64
// characters, never one the entity used before (keys are never reused, retired rows included).
export function keyFromLabel(label: string, taken: ReadonlySet<string>, prefix = ''): string {
  let base = label
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/^[0-9-]+/, '');
  if (!base) base = 'row';
  base = base.slice(0, 60).replace(/-+$/, '');
  const full = (k: string) => (prefix ? `${prefix}/${k}` : k);
  if (!taken.has(full(base))) return full(base);
  for (let i = 2; i < 1000; i++) {
    const candidate = `${base}-${i}`;
    if (!taken.has(full(candidate))) return full(candidate);
  }
  return full(`${base}-${Date.now().toString(36)}`);
}

// ---------- operations ----------

export type DraftOperation = { op: 'upsert' | 'retire'; entity: string; key: string; values?: Record<string, unknown> };

const sameValue = (a: unknown, b: unknown): boolean => JSON.stringify(a ?? null) === JSON.stringify(b ?? null);

// The values an upsert sends: for a change, only the fields that differ from the row in force; for a new row, every
// field that is set. An empty field on a change is not sent (fields not named keep their value).
export function upsertValues(form: Record<string, unknown>, current: Record<string, unknown> | null): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  for (const [name, value] of Object.entries(form)) {
    if (HIDDEN_FIELDS.has(name) && name !== 'label') continue;
    const empty = value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0 && !(current && Array.isArray(current[name])));
    if (current) {
      if (empty) continue;
      if (!sameValue(value, current[name])) values[name] = value;
    } else if (!empty) {
      values[name] = value;
    }
  }
  return values;
}

// The required fields a new row still lacks, by label.
export function missingRequired(spec: EntitySpec, values: Record<string, unknown>): string[] {
  return spec.fields
    .filter(f => f.required && f.kind !== 'kept' && (values[f.name] === undefined || values[f.name] === null || values[f.name] === ''))
    .map(f => f.label);
}

// The operations of a stored draft, rebuilt from its diff so the draft can be changed: a retired row is retired again,
// a new row sends every field, a changed row sends the fields that changed.
export function operationsFromDiff(diff: readonly ConfigurationDiffEntry[]): DraftOperation[] {
  return diff.map(d => {
    if (d.op === 'retired') return { op: 'retire', entity: d.entity, key: d.key };
    const after = { ...(d.after ?? {}) };
    delete after.key;
    delete after.origin;
    delete after.isActive;
    if (d.op === 'added' || !d.before) return { op: 'upsert', entity: d.entity, key: d.key, values: after };
    const values: Record<string, unknown> = {};
    for (const [name, value] of Object.entries(after)) if (!sameValue(value, d.before[name])) values[name] = value;
    return { op: 'upsert', entity: d.entity, key: d.key, values: Object.keys(values).length ? values : { label: after.label ?? d.key } };
  });
}

// ---------- a diff in words ----------

export interface DiffLine {
  label: string;
  before: string | null;
  after: string | null;
}

export interface DiffWords {
  verb: 'Added' | 'Changed' | 'Retired';
  tone: Tone;
  entity: string;
  title: string;
  lines: DiffLine[];
}

// One diff entry as a person reads it: "Changed: Gate policy 'Tier 2, normal, production'", then each field that
// changed with its value before and after, in words.
export function diffWords(entry: ConfigurationDiffEntry, lookup: LabelLookup): DiffWords {
  const before = entry.before ?? {};
  const after = entry.after ?? {};
  const title = (typeof after.label === 'string' && after.label) || (typeof before.label === 'string' && before.label) || lookup(entry.entity, entry.key) || humanizeKey(entry.key.split('/').pop() ?? entry.key);
  const verb = entry.op === 'added' ? 'Added' : entry.op === 'retired' ? 'Retired' : 'Changed';
  const tone: Tone = entry.op === 'added' ? 'success' : entry.op === 'retired' ? 'subtle' : 'brand';
  const spec = entitySpec(entry.entity);
  const known = spec?.fields.map(f => f.name) ?? [];
  const names = [...new Set([...known, ...Object.keys(before), ...Object.keys(after)])].filter(n => n !== 'key' && n !== 'origin' && n !== 'isActive');
  const lines: DiffLine[] = [];
  if (entry.op !== 'retired') {
    for (const name of names) {
      const was = before[name];
      const now = after[name];
      if (entry.op === 'added' ? now === undefined || now === null || name === 'label' : sameValue(was, now)) continue;
      const field = fieldOf(entry.entity, name);
      lines.push({
        label: field?.label ?? fieldLabelOf(name),
        before: entry.op === 'added' ? null : valueText(field, was, before, lookup),
        after: valueText(field, now, after, lookup),
      });
    }
  }
  return { verb, tone, entity: entityName(entry.entity), title, lines };
}

// ---------- the data domains a change touches ----------

// The domains whose data uses a change may move: a changed or retired domain and every domain under it, every domain
// whose classification in force is a changed classification, and every domain at a changed level. A new domain holds
// no uses yet.
export function touchedDomains(diff: readonly ConfigurationDiffEntry[], domains: readonly DataDomainItem[]): DataDomainItem[] {
  const children = new Map<string, DataDomainItem[]>();
  for (const d of domains) {
    if (!d.parentKey) continue;
    const list = children.get(d.parentKey) ?? [];
    list.push(d);
    children.set(d.parentKey, list);
  }
  const byKey = new Map(domains.map(d => [d.key, d]));
  const touched = new Map<string, DataDomainItem>();
  const addTree = (key: string) => {
    const d = byKey.get(key);
    if (!d || touched.has(key)) return;
    touched.set(key, d);
    for (const c of children.get(key) ?? []) addTree(c.key);
  };
  for (const e of diff) {
    if (e.entity === 'data-domain') addTree(e.key);
    if (e.entity === 'data-classification') for (const d of domains) if (d.classification?.key === e.key) touched.set(d.key, d);
    if (e.entity === 'data-domain-level') for (const d of domains) if (d.level?.key === e.key) touched.set(d.key, d);
  }
  return [...touched.values()].sort((a, b) => a.path.localeCompare(b.path));
}

// True when a diff can move the state of a data use: it changes a domain, a classification or a level. A platform
// change touches locations, not uses.
export function touchesDataUses(diff: readonly { entity: string }[]): boolean {
  return diff.some(e => e.entity === 'data-classification' || e.entity === 'data-domain' || e.entity === 'data-domain-level');
}

// ---------- versions and drafts ----------

export const DRAFT_STATE_TONE: Record<DraftStateKey, Tone> = {
  draft: 'informative',
  submitted: 'warning',
  activated: 'success',
  discarded: 'subtle',
};

// The draft states a filter on the Drafts view selects: open means not yet settled (draft or submitted).
export const DRAFT_FILTERS = ['open', 'all', 'draft', 'submitted', 'activated', 'discarded'] as const;
export type DraftFilter = (typeof DRAFT_FILTERS)[number];

export const DRAFT_FILTER_TEXT: Record<DraftFilter, string> = {
  open: 'Open',
  all: 'Every draft',
  draft: 'Not submitted',
  submitted: 'Waiting for a decision',
  activated: 'In force',
  discarded: 'Withdrawn or discarded',
};

export function draftStatesOf(filter: DraftFilter): DraftStateKey[] | undefined {
  if (filter === 'all') return undefined;
  if (filter === 'open') return ['draft', 'submitted'];
  return [filter];
}

// How a version came into force, in words.
export function acceptedHowText(how: 'approval' | 'engine' | 'bootstrap' | 'not-accepted', names: string[]): string {
  switch (how) {
    case 'approval':
      return names.length ? `Accepted by ${names.join(' and ')}` : 'Accepted on the decision page';
    case 'engine':
      return 'Accepted by the engine, under a rule that needs no approval request';
    case 'bootstrap':
      return 'The first version, written when the product was installed';
    default:
      return 'Not accepted';
  }
}

// ---------- gate levels ----------

export const GATE_LEVELS: readonly GateLevel[] = ['off', 'record', 'flag', 'advise', 'enforce'];

export const GATE_LEVEL_TONE: Record<GateLevel, Tone> = {
  off: 'subtle',
  record: 'subtle',
  flag: 'informative',
  advise: 'warning',
  enforce: 'danger',
};

// The gate level in force for a tier and a target stage, for a normal change: the most specific active gate policy
// row (an exact change type before "*", an exact tier before "*").
export function gateLevelIn(rows: readonly ConfigRow[], tierKey: string, stageKey: string, changeTypeKey = 'normal'): GateLevel | null {
  const candidates = rows.filter(r => r.entity === 'gate-policy' && r.isActive
    && (r.fields.tierKey === tierKey || r.fields.tierKey === '*')
    && (r.fields.targetStageKey === stageKey || r.fields.targetStageKey === '*')
    && (r.fields.changeTypeKey === changeTypeKey || r.fields.changeTypeKey === '*'));
  const score = (r: ConfigRow) => (r.fields.tierKey === tierKey ? 4 : 0) + (r.fields.changeTypeKey === changeTypeKey ? 2 : 0) + (r.fields.targetStageKey === stageKey ? 1 : 0);
  const best = candidates.sort((a, b) => score(b) - score(a))[0];
  const level = best?.fields.gateLevel;
  return typeof level === 'string' && (GATE_LEVELS as readonly string[]).includes(level) ? (level as GateLevel) : null;
}

// How many of a full preset's tiers and gate levels the rules in force hold. No read says which preset an install
// started from, so the Presets view compares.
export function presetMatch(preset: PresetList['presets'][number], rows: readonly ConfigRow[]): { matched: number; total: number } {
  const tiersInForce = new Set(rows.filter(r => r.entity === 'tier' && r.isActive).map(t => t.key));
  let matched = 0;
  let total = 0;
  for (const t of preset.summary.tiers) {
    total += 1;
    if (tiersInForce.has(t.key)) matched += 1;
  }
  for (const g of preset.summary.gateLevelsByTierAndStage) {
    total += 1;
    if (gateLevelIn(rows, g.tierKey, g.targetStageKey) === g.gateLevel) matched += 1;
  }
  return { matched, total };
}

// The rows of an entity sorted for reading: a rank field where the entity has one, then sort order, then label.
export function sortRows(rows: readonly ConfigRow[]): ConfigRow[] {
  const num = (v: unknown) => (typeof v === 'number' ? v : Number.MAX_SAFE_INTEGER);
  return [...rows].sort((a, b) =>
    num(a.fields.rank ?? a.fields.stageRank) - num(b.fields.rank ?? b.fields.stageRank)
    || num(a.fields.sortOrder) - num(b.fields.sortOrder)
    || a.label.localeCompare(b.label, 'en', { numeric: true }));
}

// The active child rows under one parent row, such as the values of a list.
export function childRowsOf(rows: readonly ConfigRow[], entity: string, parentKey: string): ConfigRow[] {
  return sortRows(rows.filter(r => r.entity === entity && r.isActive && r.key.startsWith(`${parentKey}/`)));
}

// ---------- wave 4: importing data domains (import_data_taxonomy, tool 99) ----------

// The import takes up to 500,000 characters of CSV text (tools/import_data_taxonomy.input.schema.json).
export const IMPORT_CSV_MAX = 500_000;

export type ImportSource = 'csv' | 'fabric' | 'purview';

export const IMPORT_SOURCE_TEXT: Record<ImportSource, string> = {
  csv: 'A spreadsheet or another list (CSV)',
  fabric: 'Microsoft Fabric domains, exported as CSV',
  purview: 'Microsoft Purview, exported as CSV',
};

// The column names of the CSV's header row, lowercased and trimmed, quotes dropped.
export function csvHeader(text: string): string[] {
  const first = text.replace(/^﻿/, '').split(/\r?\n/, 1)[0] ?? '';
  return first.split(',').map(c => c.trim().replace(/^"(.*)"$/, '$1').trim().toLowerCase()).filter(Boolean);
}

// What the page finds wrong in a CSV before it is sent, or null. The engine checks every row and rule again; this only
// saves a round trip for an empty file, one too large, or one with no label column.
export function csvProblem(text: string): string | null {
  if (!text.trim()) return 'Choose a file, or paste the CSV text.';
  if (text.length > IMPORT_CSV_MAX) return `The text is too long: the import takes up to ${IMPORT_CSV_MAX.toLocaleString('en-US')} characters.`;
  const header = csvHeader(text);
  if (!header.includes('label')) return 'The first row must be a header row with a label column: one domain per row after it.';
  const rows = text.trim().split(/\r?\n/).length - 1;
  if (rows < 1) return 'The file holds a header row and no domain.';
  return null;
}

// A data domain's name: its label in the configuration, else the label the draft gives a new domain, else its key in
// words.
export function domainLabelOf(key: string, diff: readonly ConfigurationDiffEntry[], lookup: (entity: string, key: string) => string | null): string {
  const known = lookup('data-domain', key);
  if (known) return known;
  const added = diff.find(e => e.entity === 'data-domain' && e.key === key)?.after?.label;
  return typeof added === 'string' && added ? added : humanizeKey(key);
}

// ---------- wave 4: searching the versions and drafts ----------

type VersionLike = ConfigurationChanges['versions'][number];
type DraftLike = ConfigurationChanges['drafts'][number];

// The words a version is found by: its number, who instructed and accepted it, its decision, and its change summary.
export function versionSearchFields(v: VersionLike): string[] {
  return [
    `version ${v.version}`,
    v.instructedBy?.displayLabel ?? '',
    ...v.acceptedBy.map(a => a.principal?.displayLabel ?? ''),
    v.decision?.recordNumber ?? '',
    ...v.changeSummary,
  ];
}

// The words a draft is found by: its number, who instructed it, its state, its decision, and its change summary.
export function draftSearchFields(d: DraftLike): string[] {
  return [
    d.recordNumber ?? '',
    d.instructedBy?.displayLabel ?? '',
    d.state.label,
    d.decision?.recordNumber ?? '',
    `version ${d.baseVersion}`,
    ...d.changeSummary,
  ];
}
