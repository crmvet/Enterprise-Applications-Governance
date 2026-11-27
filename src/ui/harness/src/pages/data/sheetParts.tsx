// Pieces the data write sheets share (./sheets.tsx), kept apart so the sheets file reads as the six sheets.
//   SheetHoldContext / useSheetOpen / useHeld   a tab keeps its rows as they were while one of its sheets is open, so a
//                                               write that moves a row out of the list (a gap fixed, a state changed)
//                                               leaves the sheet and the engine's answer in view
//   SubmitFollowUp, useFollowUp                 after a write that opened a decision and did not send it: send it now.
//                                               Wave 4: every proposal sheet uses them (the owner sheets, Hand over,
//                                               Claim, Register, the agent decisions, Propose priority), as the data
//                                               sheets do
//   markSent, useSentHere                       a decision sent from a follow-up in this tab, so a banner or notice
//                                               that offers the same submit says it is sent
//   useDomainChoices, useLocationChoices        the domains and locations a sheet offers, read when it opens
//   RadioField, CheckField                      labeled radios and a labeled checkbox
//   ApproverList                                who a data decision would ask, by name
//   useConfigLabels, useListChoices, useOutcomeChoices   configured labels, list values and outcomes, read only while
//                                               a sheet is open: a list row carries several sheets, and a closed one
//                                               reads nothing
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore, type ReactNode } from 'react';
import { ActionSheet, EngineAnswerView, Field } from '../../components/actions';
import { Muted, ToneBadge } from '../../components/blocks';
import { Checkbox } from '../../components/ui/checkbox';
import { Label } from '../../components/ui/label';
import { Textarea } from '../../components/ui/textarea';
import { childRows, outcomesOf, type KeyedChoice } from '../../data/configRows';
import type { DataApprover, DataDomainItem, DataLocationItem } from '../../data/contracts';
import { humanizeKey } from '../../data/format';
import { usePagedRead } from '../../data/reads';
import { dataOf, useTool } from '../../data/toolClient';
import type { EngineAnswer } from '../../data/writes';
import { cn } from '../../lib/utils';
import { buildDomainTree } from './logic';

// ---------- keeping a tab's rows while a sheet is open ----------

// A tab provides it; a sheet tells it when it opens and closes. Elsewhere (the record pages) it is null and nothing is
// held.
export const SheetHoldContext = createContext<((open: boolean) => void) | null>(null);

// A sheet's open state that tells the tab. Closing (or unmounting while open) releases the hold.
export function useSheetOpen(): [boolean, (open: boolean) => void] {
  const hold = useContext(SheetHoldContext);
  const [open, setOpenState] = useState(false);
  const openRef = useRef(false);
  const holdRef = useRef(hold);
  holdRef.current = hold;
  const setOpen = useCallback((next: boolean) => {
    if (openRef.current !== next) {
      openRef.current = next;
      holdRef.current?.(next);
    }
    setOpenState(next);
  }, []);
  useEffect(() => () => {
    if (openRef.current) holdRef.current?.(false);
  }, []);
  return [open, setOpen];
}

// The hold a tab provides: a count of the sheets open in it.
export function useHoldCount(): [number, (open: boolean) => void] {
  const [count, setCount] = useState(0);
  const hold = useCallback((open: boolean) => setCount(n => Math.max(0, n + (open ? 1 : -1))), []);
  return [count, hold];
}

// The value as it was when the hold began; the live value again once nothing holds it.
export function useHeld<T>(value: T, held: boolean): T {
  const ref = useRef(value);
  if (!held) ref.current = value;
  return ref.current;
}

// ---------- the follow-up: send the decision ----------

// The decisions sent for approval from a follow-up in this tab. A banner or notice that offers the same submit (the
// owner pages' ProposalBanner, the register's saved draft notice) reads it, so it never offers a second send.
const sentHere = new Set<string>();
const sentListeners = new Set<() => void>();

export function markSent(decisionId: string): void {
  sentHere.add(decisionId.toLowerCase());
  for (const listener of sentListeners) listener();
}

export function useSentHere(decisionId: string | null | undefined): boolean {
  return useSyncExternalStore(
    listener => { sentListeners.add(listener); return () => { sentListeners.delete(listener); }; },
    () => !!decisionId && sentHere.has(decisionId.toLowerCase()),
  );
}

// The follow-up state a sheet keeps: the decision its write opened and did not send, with the engine's answer.
export interface FollowUp {
  decisionId: string;
  answer: EngineAnswer | null;
}

// A sheet's follow-up: start it from the write's onDone (then close the sheet), and render SubmitFollowUp while data is
// set.
export function useFollowUp() {
  const [open, setOpen] = useSheetOpen();
  const [data, setData] = useState<FollowUp | null>(null);
  return {
    open,
    data,
    start: (next: FollowUp) => { setData(next); setOpen(true); },
    onOpenChange: (next: boolean) => { setOpen(next); if (!next) setData(null); },
  };
}

// After a write that opened a decision and did not send it (an owner change, a decision proposal, a copy decision an
// AI client's row opened): the earlier answer, then submit_record. Cancel leaves the decision proposed; the decision page
// sends it later.
export function SubmitFollowUp({ decisionId, previous, subject, open, onOpenChange }: {
  decisionId: string;
  previous: EngineAnswer | null;
  subject: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [note, setNote] = useState('');
  return (
    <ActionSheet
      tool="submit_record" site="pages/data/sheetParts#SubmitFollowUp"
      title="Send it for approval now?"
      description={`${subject}. The proposal is saved, and nobody is asked until it is sent.`}
      open={open}
      onOpenChange={next => { if (!next) setNote(''); onOpenChange(next); }}
      input={() => {
        const text = note.trim();
        if (text.length > 1000) return { problem: 'Keep the note to 1,000 characters.', field: 'note' };
        return { subjectKind: 'decision' as const, subjectId: decisionId, ...(text ? { note: text } : {}) };
      }}
      submitLabel="Send for approval"
      preview="Who would be asked"
      onDone={() => markSent(decisionId)}
    >
      {previous && <EngineAnswerView answer={previous} />}
      <Field label="Note for the approvers" name="note" hint="Optional. Shown with the request. Cancel to send it later from the decision page.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// ---------- what a sheet offers ----------

export interface DomainChoice {
  key: string;
  path: string;
  depth: number;
  declarable: boolean;
  isActive: boolean;
  item: DataDomainItem;
}

// Every domain in tree order, read once the sheet opens.
export function useDomainChoices(enabled: boolean): { choices: DomainChoice[]; loading: boolean; failed: boolean } {
  const read = usePagedRead('find_data_domains', enabled ? { top: 100 } : null, d => d.items, { maxPages: 10 });
  const items = read.status === 'done' ? read.items : null;
  const choices = useMemo(() => {
    if (!items) return [];
    const tree = buildDomainTree(items);
    return tree.order.map(k => {
      const n = tree.nodes.get(k)!;
      return { key: k, path: n.item.path || n.item.label, depth: n.depth, declarable: n.item.declarable, isActive: n.item.isActive, item: n.item };
    });
  }, [items]);
  return { choices, loading: enabled && read.status === 'loading', failed: read.status === 'failed' };
}

// Every open location, read once the sheet opens.
export function useLocationChoices(enabled: boolean): { items: DataLocationItem[]; loading: boolean } {
  const read = usePagedRead('find_data_locations', enabled ? { top: 100 } : null, d => d.items, { maxPages: 5 });
  return { items: read.status === 'done' ? read.items : [], loading: enabled && read.status === 'loading' };
}

// ---------- configured labels, read while a sheet is open ----------

export interface SheetLabels {
  label(key: string | null | undefined): string;
  field<T = unknown>(key: string, name: string): T | undefined;
  // Active keys in configured order: a rank, stageRank or sortOrder field where the rows carry one, else as read.
  keys(): string[];
  loading: boolean;
}

export function useConfigLabels(entity: string, enabled: boolean): SheetLabels {
  const state = useTool('get_configuration', enabled ? { entity, top: 100 } : null);
  const rows = dataOf(state)?.rows;
  const loading = enabled && state.status === 'loading';
  return useMemo<SheetLabels>(() => {
    const list = rows ?? [];
    const byKey = new Map(list.map(r => [r.key, r]));
    const rankOf = (r: (typeof list)[number], i: number) => {
      const rank = [r.fields.rank, r.fields.stageRank, r.fields.sortOrder].find(v => typeof v === 'number');
      return typeof rank === 'number' ? rank : i;
    };
    const active = list.map((r, i) => ({ r, at: rankOf(r, i), i })).filter(x => x.r.isActive).sort((a, b) => a.at - b.at || a.i - b.i).map(x => x.r.key);
    return {
      label: key => (key ? byKey.get(key)?.label ?? humanizeKey(key) : 'Not set'),
      field: <T,>(key: string, name: string) => byKey.get(key)?.fields[name] as T | undefined,
      keys: () => active,
      loading,
    };
  }, [rows, loading]);
}

export function useListChoices(listKey: string, enabled: boolean): { value: string; label: string }[] {
  const state = useTool('get_configuration', enabled ? { entity: 'list-value', top: 100 } : null);
  const rows = dataOf(state)?.rows;
  return useMemo(() => childRows(rows ?? [], listKey).map(o => ({ value: o.key, label: o.label })), [rows, listKey]);
}

export function useOutcomeChoices(typeKey: string, enabled: boolean): { options: KeyedChoice[]; loading: boolean; needsOptions: boolean } {
  const outcomes = useTool('get_configuration', enabled ? { entity: 'decision-outcome', top: 100 } : null);
  const type = useTool('get_configuration', enabled && typeKey ? { entity: 'decision-type', keys: [typeKey] } : null);
  const outcomeRows = dataOf(outcomes)?.rows;
  const typeRows = dataOf(type)?.rows;
  return useMemo(() => {
    const typeRow = typeRows?.find(r => r.key === typeKey);
    return {
      options: outcomesOf(outcomeRows ?? [], typeRow, typeKey),
      loading: enabled && (outcomes.status === 'loading' || type.status === 'loading'),
      needsOptions: typeRow?.fields.needsOptionsConsidered === true,
    };
  }, [outcomeRows, typeRows, typeKey, enabled, outcomes.status, type.status]);
}

// ---------- form controls ----------

export function RadioField<T extends string>({ legend, name, value, onChange, options, hint, required }: {
  legend: string;
  name: string;
  value: T | '';
  onChange: (value: T) => void;
  options: { value: T; label: ReactNode; help?: ReactNode }[];
  hint?: ReactNode;
  required?: boolean;
}) {
  return (
    <fieldset className="m-0 flex min-w-0 flex-col gap-1.5 border-0 p-0">
      <legend className="mb-1.5 flex items-center gap-2 text-sm leading-none font-medium text-navy">
        {legend}
        {required && <span className="text-xs font-normal text-navy-3">Required</span>}
      </legend>
      {options.map(o => (
        <label
          key={o.value}
          className={cn('flex cursor-pointer items-start gap-2 rounded-md border px-3 py-2 text-sm text-navy', value === o.value ? 'border-primary bg-info-soft' : 'border-line')}
        >
          <input type="radio" name={name} className="mt-0.5 size-4 shrink-0 accent-[var(--primary)]" checked={value === o.value} onChange={() => onChange(o.value)} />
          <span className="flex min-w-0 flex-col gap-0.5">
            <span className="font-medium">{o.label}</span>
            {o.help && <span className="text-xs leading-relaxed text-navy-3">{o.help}</span>}
          </span>
        </label>
      ))}
      {hint && <p className="m-0 text-xs leading-relaxed text-navy-3">{hint}</p>}
    </fieldset>
  );
}

export function CheckField({ id, label, checked, onChange, hint }: { id: string; label: ReactNode; checked: boolean; onChange: (checked: boolean) => void; hint?: ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-start gap-2">
        <Checkbox id={id} checked={checked} onCheckedChange={v => onChange(v === true)} className="mt-0.5" aria-describedby={hint ? `${id}-hint` : undefined} />
        <Label htmlFor={id} className="leading-5 font-normal">{label}</Label>
      </div>
      {hint && <p id={`${id}-hint`} className="m-0 pl-6 text-xs leading-relaxed text-navy-3">{hint}</p>}
    </div>
  );
}

// ---------- who would be asked ----------

export function approverText(a: DataApprover): string {
  const part = a.ownerRole ? a.ownerRole.label.toLowerCase() : a.role ? a.role.label.toLowerCase() : 'approver';
  const from = a.inheritedFrom ? `, from ${a.inheritedFrom.label}` : '';
  if (a.noOneToAsk) return `No one holds the ${part} role${from}`;
  if (a.principal) return `${a.principal.displayLabel || 'A named person'}, ${part}${from}`;
  return a.role ? `Anyone with the ${part} role` : `The ${part}${from}`;
}

export function ApproverList({ approvers, empty }: { approvers: DataApprover[]; empty: string }) {
  if (!approvers.length) return <Muted>{empty}</Muted>;
  return (
    <ul className="m-0 flex list-none flex-col gap-1 p-0">
      {approvers.map((a, i) => (
        <li key={i} className="flex flex-wrap items-center gap-1.5 text-sm text-navy">
          <span className={a.noOneToAsk ? 'font-medium text-danger' : 'font-medium'}>{approverText(a)}</span>
          {a.isConsultation && <ToneBadge tone="subtle" icon={null}>Consulted, advice only</ToneBadge>}
          {a.noOneToAsk && <ToneBadge tone="danger">No one to ask</ToneBadge>}
        </li>
      ))}
    </ul>
  );
}
