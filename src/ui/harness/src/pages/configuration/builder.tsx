// The draft builder's parts: a typed field for each kind of configuration value, the editor that adds, changes or
// retires one row, the list of changes a draft holds, and the engine's check of the draft. The sheet in ./sheets.tsx
// puts them together. Every choice a person makes shows labels; keys stay out of sight (a new row's key is made from
// its label).
import { Pencil, Plus, Trash2, TriangleAlert, Undo2, X } from 'lucide-react';
import { useId, useMemo, useState, type ReactNode } from 'react';
import { Muted, ToneBadge } from '../../components/blocks';
import { Field, EngineAnswerView } from '../../components/actions';
import { Alert, AlertDescription } from '../../components/ui/alert';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { ConfigurationChangeResult, Envelope } from '../../data/contracts';
import { humanizeKey, plural } from '../../data/format';
import type { EngineAnswer } from '../../data/writes';
import { entitySpec, FIELD_SPECS, type EntitySpec, type FieldSpec } from './fieldSpecs';
import {
  AREAS,
  areaOf,
  choiceText,
  diffWords,
  entityName,
  keyFromLabel,
  missingRequired,
  settingShape,
  sortRows,
  upsertValues,
  valueText,
  type ConfigRow,
  type DraftOperation,
} from './logic';
import { ApprovalsNeededList, ByChangeList, DataUsesTouched, DiffList, GateChangeList, RuleCheck, Section } from './parts';
import type { ConfigRowsInForce } from './rows';

const UNSET = '__unset';
const ANY = '__any';

// ---------- the choices a key field offers ----------

interface Choice {
  value: string;
  label: string;
}

// The rows a key field may name: the rows of its entity, the values of its list, or the children of the row another
// field names. A value in use that is no longer in force is kept as a choice, marked.
export function keyChoices(field: FieldSpec, form: Record<string, unknown>, config: ConfigRowsInForce): Choice[] {
  const entity = field.refFrom ? (typeof form[field.refFrom] === 'string' ? (form[field.refFrom] as string) : null) : field.ref ?? null;
  if (!entity) return [];
  const parent = field.list ?? (field.parentFrom && typeof form[field.parentFrom] === 'string' ? (form[field.parentFrom] as string) : null);
  const rows = sortRows(config.of(entity));
  if (parent) {
    const prefix = `${parent}/`;
    return rows.filter(r => r.key.startsWith(prefix)).map(r => ({ value: r.key.slice(prefix.length), label: r.label }));
  }
  if (field.parentFrom) return [];
  return rows.filter(r => !r.key.includes('/')).map(r => ({ value: r.key, label: r.label }));
}

function withCurrent(choices: Choice[], current: unknown, text: (key: string) => string): Choice[] {
  if (typeof current !== 'string' || current === '*' || choices.some(c => c.value === current)) return choices;
  return [...choices, { value: current, label: `${text(current)} (not in force)` }];
}

// ---------- one field ----------

function SelectField(props: { id: string; describedBy?: string; invalid?: true; value: string; onChange: (v: string) => void; choices: Choice[]; unsetLabel?: string | null; anyLabel?: string | null; placeholder?: string }) {
  return (
    // No "not set" choice: an empty value shows the placeholder.
    <Select value={props.value === UNSET && !props.unsetLabel ? '' : props.value} onValueChange={props.onChange}>
      <SelectTrigger id={props.id} aria-describedby={props.describedBy} aria-invalid={props.invalid} className="w-full">
        <SelectValue placeholder={props.placeholder ?? 'Choose'} />
      </SelectTrigger>
      <SelectContent className="max-h-[320px]">
        {props.unsetLabel && <SelectItem value={UNSET}>{props.unsetLabel}</SelectItem>}
        {props.anyLabel && <SelectItem value={ANY}>{props.anyLabel}</SelectItem>}
        {props.choices.map(c => <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>)}
      </SelectContent>
    </Select>
  );
}

function CheckGroup(props: { legend: string; help?: string; choices: Choice[]; value: unknown; onChange: (v: string[]) => void; required?: boolean }) {
  const id = useId();
  const chosen = Array.isArray(props.value) ? (props.value as unknown[]).filter((v): v is string => typeof v === 'string') : [];
  const toggle = (v: string, on: boolean) => props.onChange(on ? [...chosen, v] : chosen.filter(x => x !== v));
  return (
    <fieldset className="m-0 flex min-w-0 flex-col gap-1.5 border-0 p-0" aria-describedby={props.help ? `${id}-help` : undefined}>
      <legend className="mb-1.5 flex items-center gap-2 p-0 text-sm font-medium text-navy">
        {props.legend}
        {props.required && <span className="text-xs font-normal text-navy-3">Required</span>}
      </legend>
      {props.choices.length === 0 ? <Muted>Nothing to choose from in the rules in force.</Muted> : (
        <div className="grid grid-cols-2 gap-x-3 gap-y-1.5 narrow:grid-cols-1">
          {props.choices.map(c => (
            <label key={c.value} className="flex cursor-pointer items-start gap-2 text-sm text-navy">
              <Checkbox checked={chosen.includes(c.value)} onCheckedChange={on => toggle(c.value, on === true)} className="mt-0.5" />
              <span className="[overflow-wrap:anywhere]">{c.label}</span>
            </label>
          ))}
        </div>
      )}
      {props.help && <p id={`${id}-help`} className="m-0 text-xs leading-relaxed text-navy-3">{props.help}</p>}
    </fieldset>
  );
}

// One field of a row, typed by its kind. A structured value the web app does not edit is shown as kept.
export function FieldEditor(props: {
  entity: string;
  field: FieldSpec;
  form: Record<string, unknown>;
  onChange: (name: string, value: unknown) => void;
  config: ConfigRowsInForce;
  // The value in force, for a change: shown beside a structured field that is kept.
  current?: Record<string, unknown> | null;
}) {
  const { field, form, config } = props;
  const value = form[field.name];
  const set = (v: unknown) => props.onChange(field.name, v);
  const required = !!field.required;
  const hint = field.help || undefined;
  const lookupKey = (key: string) => config.lookup(field.ref ?? '', field.list ? `${field.list}/${key}` : key) ?? humanizeKey(key);

  switch (field.kind) {
    case 'text':
    case 'url':
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => <Input {...p} required={false} type={field.kind === 'url' ? 'url' : 'text'} value={typeof value === 'string' ? value : ''} maxLength={field.max ?? 500} onChange={e => set(e.target.value)} placeholder={field.kind === 'url' ? 'https://' : undefined} />}
        </Field>
      );
    case 'long-text':
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => <Textarea {...p} required={false} rows={3} value={typeof value === 'string' ? value : ''} maxLength={field.max ?? 4000} onChange={e => set(e.target.value)} />}
        </Field>
      );
    case 'date':
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => <Input {...p} required={false} type="date" value={typeof value === 'string' ? value : ''} onChange={e => set(e.target.value || undefined)} className="w-[200px] narrow:w-full" />}
        </Field>
      );
    case 'integer':
    case 'number':
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => (
            <Input
              {...p}
              required={false}
              type="number"
              inputMode={field.kind === 'integer' ? 'numeric' : 'decimal'}
              step={field.kind === 'integer' ? 1 : 'any'}
              min={field.min}
              value={typeof value === 'number' ? String(value) : typeof value === 'string' ? value : ''}
              onChange={e => set(e.target.value === '' ? undefined : Number(e.target.value))}
              className="w-[200px] narrow:w-full"
            />
          )}
        </Field>
      );
    case 'yes-no':
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => (
            <SelectField
              id={p.id}
              describedBy={p['aria-describedby']}
              invalid={p['aria-invalid']}
              value={value === true ? 'yes' : value === false ? 'no' : UNSET}
              onChange={v => set(v === 'yes' ? true : v === 'no' ? false : undefined)}
              choices={[{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }]}
              unsetLabel="Not set"
            />
          )}
        </Field>
      );
    case 'choice': {
      const choices = (field.values ?? []).map(v => ({ value: v, label: choiceText(v) }));
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => (
            <SelectField
              id={p.id}
              describedBy={p['aria-describedby']}
              invalid={p['aria-invalid']}
              value={typeof value === 'string' ? value : UNSET}
              onChange={v => set(v === UNSET ? undefined : v)}
              choices={withCurrent(choices, value, choiceText)}
              unsetLabel={required ? null : 'Not set'}
            />
          )}
        </Field>
      );
    }
    case 'choices':
      return <CheckGroup legend={field.label} help={hint} required={required} choices={(field.values ?? []).map(v => ({ value: v, label: choiceText(v) }))} value={value} onChange={set} />;
    case 'entity': {
      const choices = FIELD_SPECS.entities.map(e => ({ value: e.entity, label: e.name })).sort((a, b) => a.label.localeCompare(b.label));
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={hint}>
          {p => (
            <SelectField
              id={p.id}
              describedBy={p['aria-describedby']}
              invalid={p['aria-invalid']}
              value={typeof value === 'string' ? value : UNSET}
              onChange={v => set(v === UNSET ? undefined : v)}
              choices={withCurrent(choices, value, humanizeKey)}
              unsetLabel={required ? null : 'Not set'}
            />
          )}
        </Field>
      );
    }
    case 'key': {
      const choices = withCurrent(keyChoices(field, form, config), value, lookupKey);
      const waiting = field.refFrom && typeof form[field.refFrom] !== 'string'
        ? `Choose the ${props.entity === 'vocabulary-mapping' ? 'source entity' : 'row above'} first.`
        : field.parentFrom && typeof form[field.parentFrom] !== 'string' ? 'Choose the tier question first.' : null;
      return (
        <Field label={field.label} name={`values.${field.name}`} required={required} hint={waiting ?? hint}>
          {p => (
            <SelectField
              id={p.id}
              describedBy={p['aria-describedby']}
              invalid={p['aria-invalid']}
              value={value === '*' ? ANY : typeof value === 'string' ? value : UNSET}
              onChange={v => set(v === UNSET ? undefined : v === ANY ? '*' : v)}
              choices={choices}
              unsetLabel={required ? null : 'Not set'}
              anyLabel={field.any ? 'Any' : null}
              placeholder={config.loading ? 'Reading the rules in force' : 'Choose'}
            />
          )}
        </Field>
      );
    }
    case 'keys':
      return <CheckGroup legend={field.label} help={hint} required={required} choices={keyChoices(field, form, config)} value={value} onChange={set} />;
    case 'setting-value':
      return <SettingValueEditor form={form} value={value} onChange={set} config={config} />;
    case 'kept':
    default: {
      const shown = props.current?.[field.name] ?? value;
      if (shown === undefined || shown === null) return null;
      return (
        <div className="flex flex-col gap-0.5 rounded-md border border-dashed border-line-strong px-3 py-2">
          <span className="text-sm font-medium text-navy">{field.label}</span>
          <span className="text-xs text-navy-2">{`${valueText(field, shown, form, config.lookup)}. Kept as it is: an AI client or a configuration file changes this field.`}</span>
        </div>
      );
    }
  }
}

// A setting's value, in the shape its valueType names.
function SettingValueEditor({ form, value, onChange, config }: { form: Record<string, unknown>; value: unknown; onChange: (v: unknown) => void; config: ConfigRowsInForce }) {
  const shape = settingShape(form.valueType);
  const field: FieldSpec = { name: 'value', label: 'Value', help: '', kind: 'kept' };
  if (shape.kind === 'yes-no') return <FieldEditor entity="setting" field={{ ...field, kind: 'yes-no', required: true }} form={form} onChange={(_, v) => onChange(v)} config={config} />;
  if (shape.kind === 'number') return <FieldEditor entity="setting" field={{ ...field, kind: 'number' }} form={form} onChange={(_, v) => onChange(v)} config={config} />;
  if (shape.kind === 'text') return <FieldEditor entity="setting" field={{ ...field, kind: 'text', max: 2000 }} form={form} onChange={(_, v) => onChange(v)} config={config} />;
  if (shape.kind === 'key') return <FieldEditor entity="setting" field={{ ...field, kind: 'key', ref: shape.ref, list: shape.list }} form={form} onChange={(_, v) => onChange(v)} config={config} />;
  if (shape.kind === 'contract') {
    const values = FIELD_SPECS.contracts[shape.contract ?? ''] ?? [];
    return <FieldEditor entity="setting" field={{ ...field, kind: 'choice', values, required: true }} form={form} onChange={(_, v) => onChange(v)} config={config} />;
  }
  return (
    <div className="flex flex-col gap-0.5 rounded-md border border-dashed border-line-strong px-3 py-2">
      <span className="text-sm font-medium text-navy">Value</span>
      <span className="text-xs text-navy-2">{`${valueText({ ...field, kind: 'setting-value' }, value, form, config.lookup)}. A structured value: an AI client or a configuration file changes it.`}</span>
    </div>
  );
}

// ---------- one change ----------

export type ChangeMode = 'add' | 'change' | 'retire';

const MODE_TEXT: Record<ChangeMode, string> = { add: 'Add a row', change: 'Change a row', retire: 'Retire a row' };

// The modes an entity allows here: a setting is changed, never added or retired (each setting is the product's own);
// a child row needs its parent.
export function modesOf(entity: string): ChangeMode[] {
  if (entity === 'setting') return ['change'];
  return ['add', 'change', 'retire'];
}

// The rows a change or a retirement may name: active, and not locked.
export function editableRows(config: ConfigRowsInForce, entity: string): ConfigRow[] {
  return sortRows(config.of(entity)).filter(r => r.fields.isLocked !== true);
}

function rowChoiceLabel(row: ConfigRow, config: ConfigRowsInForce, spec: EntitySpec | null): string {
  if (spec?.parent && row.key.includes('/')) {
    const parent = config.lookup(spec.parent, row.key.split('/')[0]);
    return parent ? `${parent}: ${row.label}` : row.label;
  }
  return row.label;
}

// The fields a form shows: a setting shows its value and words only, a child row leaves out nothing.
function formFields(spec: EntitySpec): FieldSpec[] {
  if (spec.entity === 'setting') return spec.fields.filter(f => f.name === 'value' || f.name === 'label' || f.name === 'description');
  return spec.fields;
}

export interface EditorStart {
  entity?: string;
  mode?: ChangeMode;
  key?: string;
  // An operation of the draft being edited again.
  op?: DraftOperation;
}

// Adds one change to the draft: pick what to change, then add a row, change one, or retire one.
export function ChangeEditor(props: {
  config: ConfigRowsInForce;
  start: EditorStart;
  onKeep: (op: DraftOperation) => void;
  onCancel: () => void;
}) {
  const { config } = props;
  const startOp = props.start.op;
  const startRow = startOp ? config.rows.find(r => r.entity === startOp.entity && r.key === startOp.key && r.isActive) : null;
  const [entity, setEntity] = useState<string>(startOp?.entity ?? props.start.entity ?? '');
  const [mode, setMode] = useState<ChangeMode>(startOp ? (startOp.op === 'retire' ? 'retire' : startRow ? 'change' : 'add') : props.start.mode ?? (props.start.entity === 'setting' ? 'change' : 'add'));
  const [rowKey, setRowKey] = useState<string>(startOp?.key ?? props.start.key ?? '');
  const [parentKey, setParentKey] = useState<string>(startOp && !startRow && startOp.key.includes('/') ? startOp.key.split('/')[0] : '');
  const current = mode === 'change' || mode === 'retire' ? config.rows.find(r => r.entity === entity && r.key === rowKey && r.isActive) ?? null : null;
  const [form, setForm] = useState<Record<string, unknown>>(() => {
    const base = startRow ? { ...startRow.fields, label: startRow.label } : props.start.key ? (() => {
      const r = config.rows.find(x => x.entity === props.start.entity && x.key === props.start.key && x.isActive);
      return r ? { ...r.fields, label: r.label } : {};
    })() : {};
    return { ...base, ...(startOp?.values ?? {}) };
  });
  const [problem, setProblem] = useState<string | null>(null);
  const spec = entity ? entitySpec(entity) : null;
  const editingExisting = !!startOp;

  const chooseEntity = (e: string) => {
    setEntity(e);
    setMode(modesOf(e)[0]);
    setRowKey('');
    setParentKey('');
    setForm({});
    setProblem(null);
  };
  const chooseMode = (m: ChangeMode) => {
    setMode(m);
    setRowKey('');
    setParentKey('');
    setForm({});
    setProblem(null);
  };
  const chooseRow = (key: string) => {
    setRowKey(key);
    const r = config.rows.find(x => x.entity === entity && x.key === key && x.isActive);
    setForm(r ? { ...r.fields, label: r.label } : {});
    setProblem(null);
  };
  const setField = (name: string, value: unknown) => setForm(f => ({ ...f, [name]: value }));

  const keep = () => {
    if (!entity || !spec) return setProblem('Choose what to change.');
    if (mode === 'retire') {
      if (!current) return setProblem('Choose the row to retire.');
      return props.onKeep({ op: 'retire', entity, key: current.key });
    }
    if (mode === 'change') {
      if (!current) return setProblem('Choose the row to change.');
      const values = upsertValues(form, { ...current.fields, label: current.label });
      const bad = badNumbers(spec, values);
      if (bad) return setProblem(bad);
      if (!Object.keys(values).length) return setProblem('Change at least one field, or cancel.');
      return props.onKeep({ op: 'upsert', entity, key: current.key, values });
    }
    const label = typeof form.label === 'string' ? form.label.trim() : '';
    if (!label) return setProblem('Give the new row a label: the words people see.');
    if (spec.parent && !parentKey && !(startOp && startOp.key.includes('/'))) return setProblem(`Choose the ${entityName(spec.parent).toLowerCase()} the new row sits under.`);
    const values = upsertValues({ ...form, label }, null);
    const missing = missingRequired(spec, values);
    if (missing.length) return setProblem(`Fill in ${missing.join(', ')}.`);
    const bad = badNumbers(spec, values);
    if (bad) return setProblem(bad);
    const key = startOp && !startRow
      ? startOp.key
      : keyFromLabel(label, new Set(config.of(entity, true).map(r => r.key)), spec.parent ? parentKey : '');
    return props.onKeep({ op: 'upsert', entity, key, values });
  };

  const rows = entity ? editableRows(config, entity) : [];
  const lockedCount = entity ? config.of(entity).filter(r => r.fields.isLocked === true).length : 0;
  const parents = spec?.parent ? sortRows(config.of(spec.parent)) : [];

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-line-strong bg-background p-3" role="group" aria-label={editingExisting ? 'Change this part of the draft' : 'Add a change to the draft'}>
      {problem && (
        <Alert variant="warning" role="alert">
          <TriangleAlert aria-hidden />
          <AlertDescription><p className="m-0">{problem}</p></AlertDescription>
        </Alert>
      )}
      <EntityPicker value={entity} onChange={chooseEntity} disabled={editingExisting} />
      {spec && <Muted>{spec.about}</Muted>}
      {entity && !editingExisting && modesOf(entity).length > 1 && (
        <div className="flex flex-wrap gap-1" role="group" aria-label="What to do">
          {modesOf(entity).map(m => (
            <Button key={m} type="button" size="sm" variant={mode === m ? 'secondary' : 'ghost'} aria-pressed={mode === m} onClick={() => chooseMode(m)} className={mode === m ? 'ring-1 ring-primary' : undefined}>
              {MODE_TEXT[m]}
            </Button>
          ))}
        </div>
      )}
      {entity && (mode === 'change' || mode === 'retire') && !editingExisting && (
        <Field label={mode === 'change' ? 'The row to change' : 'The row to retire'} name="key" required hint={lockedCount ? `${plural(lockedCount, 'locked row is', 'locked rows are')} not listed: no configuration change may alter ${lockedCount === 1 ? 'it' : 'them'}.` : undefined}>
          {p => (
            <SelectField
              id={p.id}
              describedBy={p['aria-describedby']}
              value={rowKey || UNSET}
              onChange={v => chooseRow(v === UNSET ? '' : v)}
              choices={rows.map(r => ({ value: r.key, label: rowChoiceLabel(r, config, spec) }))}
              placeholder={config.loading ? 'Reading the rules in force' : rows.length ? 'Choose a row' : 'No row in force'}
            />
          )}
        </Field>
      )}
      {spec?.parent && mode === 'add' && !(startOp && startOp.key.includes('/')) && (
        <Field label={`The ${entityName(spec.parent).toLowerCase()} it sits under`} name="parent" required>
          {p => (
            <SelectField id={p.id} describedBy={p['aria-describedby']} value={parentKey || UNSET} onChange={v => setParentKey(v === UNSET ? '' : v)} choices={parents.map(r => ({ value: r.key, label: r.label }))} />
          )}
        </Field>
      )}
      {spec && mode === 'retire' && current && (
        <Muted>{`Retiring keeps the row's history and its key: records that name it still read it. ${entityName(entity)} "${current.label}" stops applying once the change is accepted.`}</Muted>
      )}
      {spec && (mode === 'add' || (mode === 'change' && current)) && (
        <div className="flex flex-col gap-3">
          {formFields(spec).filter(f => !(f.kind === 'kept' && mode === 'add')).map(f => (
            <FieldEditor key={f.name} entity={entity} field={f} form={form} onChange={setField} config={config} current={current ? current.fields : null} />
          ))}
        </div>
      )}
      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={props.onCancel}>
          <X aria-hidden />
          Cancel
        </Button>
        <Button type="button" size="sm" onClick={keep} disabled={!entity}>
          {editingExisting ? 'Keep this change' : 'Add to the draft'}
        </Button>
      </div>
    </div>
  );
}

function badNumbers(spec: EntitySpec, values: Record<string, unknown>): string | null {
  for (const f of spec.fields) {
    const v = values[f.name];
    if (v === undefined) continue;
    if ((f.kind === 'integer' || f.kind === 'number') && (typeof v !== 'number' || Number.isNaN(v))) return `${f.label} needs a number.`;
    if (f.kind === 'integer' && typeof v === 'number' && !Number.isInteger(v)) return `${f.label} needs a whole number.`;
    if (typeof f.min === 'number' && typeof v === 'number' && v < f.min) return `${f.label} is at least ${f.min}.`;
  }
  return null;
}

// What to change: every configuration entity, grouped by area.
function EntityPicker({ value, onChange, disabled }: { value: string; onChange: (entity: string) => void; disabled?: boolean }) {
  const id = useId();
  const groups = useMemo(() => {
    const known = new Set(FIELD_SPECS.entities.map(e => e.entity));
    return AREAS.map(a => ({ key: a.key, label: a.label, entities: a.entities.filter(e => known.has(e)) }))
      .map(g => ({ ...g, entities: [...g.entities, ...FIELD_SPECS.entities.map(e => e.entity).filter(e => areaOf(e) === g.key && !(g.entities as readonly string[]).includes(e))] }));
  }, []);
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>What to change</Label>
      <Select value={value || undefined} onValueChange={onChange} disabled={disabled}>
        <SelectTrigger id={id} className="w-full">
          <SelectValue placeholder="Choose a kind of rule" />
        </SelectTrigger>
        <SelectContent className="max-h-[360px]">
          {groups.map(g => (
            <SelectGroup key={g.key}>
              <SelectLabel>{g.label}</SelectLabel>
              {g.entities.map(e => <SelectItem key={e} value={e}>{entityName(e)}</SelectItem>)}
            </SelectGroup>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

// ---------- the changes a draft holds ----------

export function OperationCard(props: { op: DraftOperation; config: ConfigRowsInForce; onEdit?: () => void; onRemove?: () => void }) {
  const { op, config } = props;
  const current = config.rows.find(r => r.entity === op.entity && r.key === op.key && r.isActive) ?? null;
  const before = current ? { ...current.fields, label: current.label } : null;
  const w = diffWords({
    entity: op.entity,
    key: op.key,
    op: op.op === 'retire' ? 'retired' : current ? 'updated' : 'added',
    before,
    after: op.op === 'retire' ? null : { ...(before ?? {}), ...(op.values ?? {}) },
  }, config.lookup);
  return (
    <li className="flex flex-col gap-1.5 rounded-md border border-line bg-card px-3 py-2">
      <div className="flex flex-wrap items-start gap-2">
        <span className="flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1">
          <ToneBadge tone={w.tone} icon={null}>{w.verb === 'Changed' ? 'Change' : w.verb === 'Added' ? 'Add' : 'Retire'}</ToneBadge>
          <span className="text-xs text-navy-3">{w.entity}</span>
          <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{w.title}</span>
        </span>
        <span className="flex gap-1">
          {props.onEdit && op.op === 'upsert' && (
            <Button type="button" size="icon-sm" variant="ghost" onClick={props.onEdit} aria-label={`Edit the change to ${w.title}`}>
              <Pencil aria-hidden />
            </Button>
          )}
          {props.onRemove && (
            <Button type="button" size="icon-sm" variant="ghost" onClick={props.onRemove} aria-label={`Take the change to ${w.title} out of the draft`}>
              <Trash2 aria-hidden />
            </Button>
          )}
        </span>
      </div>
      {w.lines.length > 0 && (
        <dl className="m-0 grid grid-cols-[minmax(0,160px)_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs narrow:grid-cols-1">
          {w.lines.map(l => (
            <div key={l.label} className="contents">
              <dt className="font-semibold text-navy-2">{l.label}</dt>
              <dd className="m-0 text-navy [overflow-wrap:anywhere]">
                {l.before !== null ? <><span className="text-navy-3 line-through">{l.before}</span><span className="text-navy-3"> becomes </span>{l.after}</> : l.after}
              </dd>
            </div>
          ))}
        </dl>
      )}
    </li>
  );
}

// ---------- the engine's check ----------

export function CheckResult({ envelope, answer, config }: { envelope: Envelope<ConfigurationChangeResult>; answer: EngineAnswer | null; config: ConfigRowsInForce }) {
  if (!envelope.ok) return answer ? <EngineAnswerView answer={answer} /> : null;
  const d = envelope.data;
  const warnings = (envelope.warnings ?? []).filter(w => w.code !== 'request.validate-only').map(w => ({ entity: null, key: null, message: w.message }));
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-info-line bg-card p-3">
      <span className="text-sm font-semibold text-navy">The engine's check. Nothing was saved yet.</span>
      <RuleCheck valid={d.valid} errors={d.errors} warnings={warnings} labelOfAnyKey={config.labelOfAnyKey} saved={false} />
      <Section title="What changes">
        <DiffList diff={d.diffSummary} lookup={config.lookup} />
      </Section>
      <Section title="Live applications whose rules change">
        <p className="m-0 text-sm text-navy">{d.impact.applicationsAffected ? plural(d.impact.applicationsAffected, 'application', 'applications') : 'None'}</p>
        <ByChangeList byChange={d.impact.byChange} lookup={config.lookup} />
        {d.impact.applicationsAffected > 0 && <Muted>The draft's page lists them by name once it is saved.</Muted>}
      </Section>
      <Section title="Gate levels">
        <GateChangeList changes={d.impact.gateChanges} lookup={config.lookup} />
      </Section>
      <Section title="Data uses">
        <DataUsesTouched diff={d.diffSummary} uses={d.impact.dataUses} />
      </Section>
      <Section title="Records in flight">
        <p className="m-0 text-sm text-navy">
          {d.impact.recordsInFlight
            ? `${plural(d.impact.recordsInFlight, 'record already submitted keeps the rules in force when it was submitted', 'records already submitted keep the rules in force when each was submitted')}.`
            : 'No submitted record waits.'}
        </p>
      </Section>
      <Section title="Who accepts it, once submitted">
        <ApprovalsNeededList items={d.approvalsNeeded} />
        <Muted>A person accepts the exact change on the decision page. Nothing changes until then.</Muted>
      </Section>
    </div>
  );
}

// ---------- small helpers for the sheet ----------

export function AddButton({ onClick, children }: { onClick: () => void; children: ReactNode }) {
  return (
    <Button type="button" variant="outline" size="sm" onClick={onClick} className="self-start">
      <Plus aria-hidden />
      {children}
    </Button>
  );
}

export function UndoNote({ onUndo, what }: { onUndo: () => void; what: string }) {
  return (
    <p className="m-0 flex items-center gap-2 text-xs text-navy-2" role="status">
      <span>{`${what} was taken out of the draft.`}</span>
      <Button type="button" variant="link" size="xs" onClick={onUndo} className="h-auto p-0 text-xs">
        <Undo2 aria-hidden />
        Put it back
      </Button>
    </p>
  );
}
