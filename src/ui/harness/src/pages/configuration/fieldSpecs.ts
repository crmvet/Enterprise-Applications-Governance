// The configuration entities and their fields, as the draft builder needs them: which fields a row of each entity has,
// what kind of value each takes, which ones are required, and the entity a key field points to. The facts come from the
// configuration bundle schema (src/config/schema/config-bundle.schema.json), the engine's own contract for a draft
// patch. entity-fields.json holds them in a compact form, written by deriveFieldSpecs below; fieldSpecs.test.ts derives
// them again from the schema and fails when the file is out of date (CONFIG_FIELDS_WRITE=1 npm test writes it).
// Pure: no React, so the test and the pages read the same code.
import specsJson from './entity-fields.json';

// How a field's value is entered:
//   text, long-text   typed words (long-text is a multi-line box)
//   integer, number   a number
//   yes-no            a yes or no
//   key               one row of another entity (ref), or of one value list (ref list-value, list); any allows "Any"
//   keys              several rows of another entity
//   choice, choices   one or several values of a fixed engine list (values)
//   entity            one configuration entity
//   url               a link (https only)
//   date              a calendar day
//   setting-value     a setting's value: its shape follows the row's valueType
//   kept              a structured value the web app does not edit; it is kept as it is (an AI client or a file edits it)
export type FieldKind = 'text' | 'long-text' | 'integer' | 'number' | 'yes-no' | 'key' | 'keys' | 'choice' | 'choices' | 'entity' | 'url' | 'date' | 'setting-value' | 'kept';

export interface FieldSpec {
  name: string;
  label: string;
  help: string;
  kind: FieldKind;
  ref?: string;
  list?: string;
  // A key whose entity is named by another field of the row (a vocabulary mapping's sourceKey follows sourceEntity).
  refFrom?: string;
  // A child key under the row another field names (a classification's implied answer sits under its tier question).
  parentFrom?: string;
  any?: boolean;
  values?: string[];
  min?: number;
  max?: number;
  required?: boolean;
  // A working set field (class W): it holds a client's own facts and never ships in a preset.
  working?: boolean;
}

export interface EntitySpec {
  entity: string;
  // The logical model code, such as C32.
  code: string;
  // "Approval rule".
  name: string;
  // What a row of it is, in one or two sentences.
  about: string;
  // The parent entity of a child row (a list value sits under a value list), or null.
  parent: string | null;
  fields: FieldSpec[];
}

export interface FieldSpecs {
  entities: EntitySpec[];
  // The fixed engine lists (contract.<name>), for a setting whose value is one of them.
  contracts: Record<string, string[]>;
}

export const FIELD_SPECS = specsJson as FieldSpecs;

const byEntity = new Map(FIELD_SPECS.entities.map(e => [e.entity, e]));

export function entitySpec(entity: string): EntitySpec | null {
  return byEntity.get(entity) ?? null;
}

// ---------- deriving the specs from the bundle schema ----------

type Json = Record<string, unknown>;
const isObj = (v: unknown): v is Json => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | undefined => (typeof v === 'string' ? v : undefined);

// Words a field name holds that read better in capitals.
const ACRONYMS: Record<string, string> = { ai: 'AI', url: 'URL', urls: 'URLs', id: 'ID', ids: 'IDs', mcp: 'MCP', it: 'IT', sql: 'SQL', jit: 'JIT' };

// "approverOwnerRoleKey" becomes "Approver owner role"; "aiUseRule" becomes "AI use rule".
export function fieldLabelOf(name: string): string {
  const base = name.replace(/(Keys|Key|Ids|Id|Json)$/, '') || name;
  const words = base.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase().split(/\s+/).filter(Boolean);
  const text = words.map((w, i) => ACRONYMS[w] ?? (i === 0 ? w.charAt(0).toUpperCase() + w.slice(1) : w)).join(' ');
  return text || name;
}

// "C32 Approval Rule. Which approvals a decision type needs." becomes code C32, name "Approval rule", and the rest.
function nameParts(description: string, entity: string): { code: string; name: string; about: string } {
  const m = /^(C\d{2})\s+([^.]+)\.\s*(.*)$/s.exec(description.trim());
  if (!m) return { code: '', name: fieldLabelOf(entity.replace(/-([a-z])/g, (_, c: string) => c.toUpperCase())), about: plainSchemaText(description) };
  const words = m[2].trim().split(/\s+/);
  const name = words.map((w, i) => (i === 0 ? w : /^[A-Z]{2,}$/.test(w) ? w : w.toLowerCase())).join(' ');
  return { code: m[1], name, about: plainSchemaText(m[3]) };
}

// Fields the schema types as a plain key whose meaning sits in another field or in an engine list the schema does not
// name: a decision's subject kind (decision rights and approval rules, docs/architecture/data-taxonomy.md, Entities), the mapped row of a
// vocabulary mapping, and the answer a data classification implies.
const SUBJECT_KINDS = ['business-application', 'intake-request', 'change-request', 'deployment-record', 'technical-component', 'claim', 'review', 'configuration-draft', 'data-use', 'data-mapping', 'data-domain', 'data-location'];
const SPECIAL_FIELDS: Record<string, Partial<FieldSpec>> = {
  'decision-right.subjectKind': { kind: 'choice', values: SUBJECT_KINDS },
  'approval-rule.subjectKind': { kind: 'choice', values: SUBJECT_KINDS },
  'vocabulary-mapping.sourceKey': { kind: 'key', refFrom: 'sourceEntity' },
  'data-classification.impliedAnswerKey': { kind: 'key', ref: 'tier-answer-option', parentFrom: 'impliedTierQuestionKey' },
};

const LONG_TEXT = new Set(['description', 'duties', 'examples', 'rule', 'answerText', 'question', 'purpose', 'notes', 'guidance']);
const TEXT_REFS = new Set(['#/$defs/semanticVersion']);

// Schema descriptions cite the design ("(data taxonomy, D-37)", "(V49)", "(document 21, section 3.2)",
// "(docs/architecture/data-taxonomy.md, Entities)"). A person reads the sentence without them.
const INTERNAL_REF = /\s*\((?=[^()]*(?:\bD-\d+|document \d+|section \d|\bV\d{2}\b|\bB\d{2}-\d+|open design question|\bOP-\d+|\bRR-\d+|\bCHK-\d+|\bG-\d+|data taxonomy|\bdocs\/|working default|\bC\d{2}\b|\bO\d{2}\b))[^()]*\)/gi;

export function plainSchemaText(text: string): string {
  return text.replace(INTERNAL_REF, '').replace(/\s+([.,;:])/g, '$1').replace(/\s{2,}/g, ' ').trim();
}

function refName(ref: string): string {
  return ref.replace('#/$defs/', '');
}

function contractValues(defs: Json, ref: string): string[] | null {
  const def = defs[refName(ref)];
  return isObj(def) && Array.isArray(def.enum) ? (def.enum as unknown[]).filter((v): v is string => typeof v === 'string') : null;
}

// The reference a key field points to: "data-domain", or "list-value:retention-class" (one value list).
function refOf(spec: FieldSpec, references: string | undefined): void {
  if (!references) return;
  const [entity, list] = references.split(':');
  spec.ref = entity;
  if (list) spec.list = list;
}

function fieldOf(name: string, prop: Json, defs: Json, entity: string): FieldSpec {
  const help = plainSchemaText(str(prop.description) ?? '');
  const spec: FieldSpec = { name, label: fieldLabelOf(name), help, kind: 'kept' };
  const ref = str(prop.$ref);
  const references = str(prop['x-references']);
  if (entity === 'setting' && name === 'value') {
    spec.kind = 'setting-value';
    return spec;
  }
  const special = SPECIAL_FIELDS[`${entity}.${name}`];
  if (special) return { ...spec, ...special };
  if (ref) {
    if (ref === '#/$defs/key' || ref === '#/$defs/keyPath' || ref === '#/$defs/childKey') {
      if (references) {
        spec.kind = 'key';
        refOf(spec, references);
      }
    } else if (ref === '#/$defs/keyOrAny') {
      spec.kind = 'key';
      spec.any = true;
      refOf(spec, references);
    } else if (ref === '#/$defs/keys') {
      if (references) {
        spec.kind = 'keys';
        refOf(spec, references);
      }
    } else if (ref === '#/$defs/entityKey') {
      spec.kind = 'entity';
    } else if (ref === '#/$defs/httpsUrl') {
      spec.kind = 'url';
    } else if (ref.startsWith('#/$defs/contract.')) {
      const values = contractValues(defs, ref);
      if (values) {
        spec.kind = 'choice';
        spec.values = values;
      }
    } else if (TEXT_REFS.has(ref)) {
      spec.kind = 'text';
    }
    return spec;
  }
  const type = prop.type;
  if (type === 'array' && isObj(prop.items)) {
    const itemRef = str(prop.items.$ref);
    const itemRefs = str(prop.items['x-references']) ?? references;
    if (itemRef?.startsWith('#/$defs/contract.')) {
      const values = contractValues(defs, itemRef);
      if (values) {
        spec.kind = 'choices';
        spec.values = values;
      }
    } else if ((itemRef === '#/$defs/key' || itemRef === '#/$defs/keyOrAny') && itemRefs) {
      spec.kind = 'keys';
      refOf(spec, itemRefs);
    }
    return spec;
  }
  if (type === 'string') {
    if (Array.isArray(prop.enum)) {
      spec.kind = 'choice';
      spec.values = (prop.enum as unknown[]).filter((v): v is string => typeof v === 'string');
      return spec;
    }
    if (prop.format === 'date') {
      spec.kind = 'date';
      return spec;
    }
    const max = typeof prop.maxLength === 'number' ? prop.maxLength : undefined;
    spec.kind = LONG_TEXT.has(name) || (max !== undefined && max > 200) ? 'long-text' : 'text';
    if (max !== undefined) spec.max = max;
    return spec;
  }
  if (type === 'integer' || type === 'number') {
    spec.kind = type;
    if (typeof prop.minimum === 'number') spec.min = prop.minimum;
    return spec;
  }
  if (type === 'boolean') {
    spec.kind = 'yes-no';
    return spec;
  }
  return spec;
}

function propsOf(defs: Json, name: string): [string, Json][] {
  const def = defs[name];
  if (!isObj(def) || !isObj(def.properties)) return [];
  return Object.entries(def.properties).filter((e): e is [string, Json] => isObj(e[1]));
}

// Derives the specs from the parsed bundle schema. The entity order is the draft operation's entity list.
export function deriveFieldSpecs(schema: unknown): FieldSpecs {
  if (!isObj(schema) || !isObj(schema.$defs)) throw new Error('The bundle schema has no $defs.');
  const defs = schema.$defs;
  const op = defs.draftOperation;
  const entityEnum = isObj(op) && isObj(op.properties) && isObj(op.properties.entity) && Array.isArray(op.properties.entity.enum)
    ? (op.properties.entity.enum as unknown[]).filter((v): v is string => typeof v === 'string')
    : [];
  if (!entityEnum.length) throw new Error('The bundle schema names no draft operation entities.');

  // Child rows: X.seedChildren holds arrays of Y.seedRow, so Y sits under X.
  const parents = new Map<string, string>();
  for (const entity of entityEnum) {
    for (const [, prop] of propsOf(defs, `${entity}.seedChildren`)) {
      const itemRef = isObj(prop.items) ? str(prop.items.$ref) : undefined;
      const child = itemRef ? refName(itemRef).replace(/\.seedRow$/, '') : undefined;
      if (child) parents.set(child, entity);
    }
  }

  const entities: EntitySpec[] = entityEnum.map(entity => {
    const setRow = defs[`${entity}.setRow`];
    const description = isObj(setRow) ? str(setRow.description) ?? '' : '';
    const required = new Set(isObj(setRow) && Array.isArray(setRow.required) ? (setRow.required as unknown[]).filter((v): v is string => typeof v === 'string') : []);
    const fields: FieldSpec[] = [];
    const seen = new Set<string>();
    const add = (name: string, prop: Json, working: boolean) => {
      if (seen.has(name)) return;
      seen.add(name);
      const field = fieldOf(name, prop, defs, entity);
      if (required.has(name)) field.required = true;
      if (working) field.working = true;
      fields.push(field);
    };
    const common = new Map(propsOf(defs, 'row.valueFields'));
    for (const name of ['label', 'description']) {
      const prop = common.get(name);
      if (prop) add(name, prop, false);
    }
    for (const [name, prop] of propsOf(defs, `${entity}.seedFields`)) add(name, prop, false);
    for (const [name, prop] of propsOf(defs, `${entity}.workingFields`)) add(name, prop, true);
    const sortOrder = common.get('sortOrder');
    if (sortOrder) add('sortOrder', sortOrder, false);
    const parts = nameParts(description, entity);
    return { entity, code: parts.code, name: parts.name, about: parts.about, parent: parents.get(entity) ?? null, fields };
  });

  const contracts: Record<string, string[]> = {};
  for (const [name, def] of Object.entries(defs)) {
    if (name.startsWith('contract.') && isObj(def) && Array.isArray(def.enum)) {
      contracts[name.slice('contract.'.length)] = (def.enum as unknown[]).filter((v): v is string => typeof v === 'string');
    }
  }
  return { entities, contracts };
}
