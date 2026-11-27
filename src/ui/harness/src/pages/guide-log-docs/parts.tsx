// Shared parts of the Decision log and Documents pages:
//   pagedToolState(paged)     a paged read (data/reads.ts usePagedRead) as the ToolState a Panel takes
//   useApplicationName(id)    the name of the application a filter names, from the rows or from its card
//   CheckFilter               a filter of several configured values (a dropdown of checkboxes)
//   AppFilterBox              the filter by one application: search the register, pick one; shown as a chip once set
//   AppSearchField            the same search inside a side sheet, as a labeled form field
//   DateFilter                a from or to date in the filter bar
// The filters live in the address, so Back returns to the same list.
import { ChevronDown, ListFilter, Search, X } from 'lucide-react';
import { useState } from 'react';
import { Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { useSettled } from '../../components/people';
import { Button } from '../../components/ui/button';
import { DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '../../components/ui/dropdown-menu';
import { Input } from '../../components/ui/input';
import type { ApplicationSummary, Envelope, FoundApplications, Success } from '../../data/contracts';
import type { PagedState } from '../../data/reads';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { cn } from '../../lib/utils';

// ---------- reads ----------

// A paged read as one envelope whose items are every row read, so a Panel shows its loading, failure and notes as for
// any read. skipToken stays set when the store holds more rows than were read.
export function pagedToolState<T, D extends object>(paged: PagedState<T, D>): ToolState<D & { items: T[]; skipToken?: string }> {
  if (paged.status === 'loading') return { status: 'loading' };
  if (paged.status === 'failed') return { status: 'done', envelope: paged.failure };
  const warnings = paged.laterFailure
    ? [...paged.first.warnings, { message: `Only the first ${paged.items.length} rows could be read. ${paged.laterFailure.error.message} Reference ${paged.laterFailure.correlationId}.` }]
    : paged.first.warnings;
  const envelope: Success<D & { items: T[]; skipToken?: string }> = {
    ...paged.first,
    warnings,
    data: { ...paged.first.data, items: paged.items, skipToken: paged.skipToken },
  };
  return { status: 'done', envelope };
}

export function envelopeOf<T>(state: ToolState<T>): Envelope<T> | undefined {
  return state.status === 'done' ? state.envelope : undefined;
}

// The name of the application a filter names: from the rows on the page when one of them names it, else read from
// its card. Null while it is read; "This application" when it cannot be read.
export function useApplicationName(id: string | undefined, known: string | null): string | null {
  const card = useTool('get_application_card', id && !known ? { businessApplicationId: id } : null);
  if (!id) return null;
  if (known) return known;
  if (card.status === 'loading') return null;
  return dataOf(card)?.application.name ?? 'This application';
}

// ---------- filters ----------

export function CheckFilter(props: {
  label: string;
  options: { value: string; label: string }[];
  chosen: readonly string[];
  onToggle: (value: string) => void;
  loading?: boolean;
}) {
  const count = props.chosen.length;
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" className={cn('gap-1.5', count > 0 && 'border-primary text-primary')} aria-label={`${props.label}${count ? `, ${count} chosen` : ''}`}>
          <ListFilter aria-hidden />
          {props.label}
          {count > 0 && <span className="rounded-full bg-primary px-1.5 text-[11px] leading-4 text-primary-foreground" aria-hidden>{count}</span>}
          <ChevronDown className="size-3.5 opacity-70" aria-hidden />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="max-h-[360px] min-w-[240px] overflow-y-auto">
        <DropdownMenuLabel>{props.label}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {props.options.length === 0 && (
          <div className="px-2 py-1.5 text-sm text-navy-3">{props.loading === false ? 'No choices could be read.' : 'Loading the choices.'}</div>
        )}
        {props.options.map(o => (
          <DropdownMenuCheckboxItem key={o.value} checked={props.chosen.includes(o.value)} onCheckedChange={() => props.onToggle(o.value)} onSelect={e => e.preventDefault()}>
            {o.label}
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function DateFilter({ id, label, value, onChange }: { id: string; label: string; value: string | undefined; onChange: (value: string | undefined) => void }) {
  return (
    <div className="flex items-center gap-2">
      <label htmlFor={id} className="text-xs text-navy-3">{label}</label>
      <Input
        id={id}
        type="date"
        value={value ?? ''}
        onChange={e => onChange(/^\d{4}-\d{2}-\d{2}$/.test(e.target.value) ? e.target.value : undefined)}
        className="w-[160px]"
      />
    </div>
  );
}

// The register searched by name or purpose, eight matches at most.
function useAppSearch(text: string, active: boolean): { items: ApplicationSummary[]; state: ToolState<FoundApplications> | null; query: string } {
  const query = useSettled(text.trim(), 300);
  const asking = active && query.length >= 2;
  const state = useTool('find_applications', asking ? { text: query, top: 8 } : null);
  return { items: asking ? dataOf(state)?.items ?? [] : [], state: asking ? state : null, query };
}

function Matches(props: { query: string; state: ToolState<FoundApplications> | null; items: ApplicationSummary[]; onPick: (app: { id: string; name: string }) => void; label: string }) {
  if (!props.state) return null;
  return (
    <div aria-live="polite" className="flex flex-col gap-1">
      {props.state.status === 'loading' && <Muted>Searching the register.</Muted>}
      {props.state.status === 'done' && !props.state.envelope.ok && <span className="text-xs text-danger">The search did not answer: {props.state.envelope.error.message}</span>}
      {props.state.status === 'done' && props.state.envelope.ok && props.items.length === 0 && <Muted>No registered application matches "{props.query}".</Muted>}
      {props.items.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={props.label}>
          {props.items.map(a => (
            <li key={a.businessApplicationId}>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-auto w-full justify-start py-1.5 text-left whitespace-normal"
                onClick={() => props.onPick({ id: a.businessApplicationId.toLowerCase(), name: a.name })}
              >
                <span className="flex min-w-0 flex-col">
                  <span className="font-semibold [overflow-wrap:anywhere] text-navy">{a.name}</span>
                  <span className="text-xs font-normal text-navy-3">{[a.kind.label, a.tier?.label ?? 'No tier yet', a.stage?.label].filter(Boolean).join('. ')}</span>
                </span>
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// The filter by application in a filter bar. With no application chosen it is a search box; the page shows the chosen
// one as a chip with the other active filters.
export function AppFilterBox({ id, onPick, className }: { id: string; onPick: (applicationId: string) => void; className?: string }) {
  const [text, setText] = useState('');
  const search = useAppSearch(text, true);
  return (
    <div className={cn('flex min-w-[220px] flex-col gap-1.5 narrow:w-full', className)}>
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
        <Input
          id={id}
          type="search"
          value={text}
          onChange={e => setText(e.target.value)}
          placeholder="Application"
          aria-label="Filter by application: type two or more letters of its name"
          autoComplete="off"
          spellCheck={false}
          maxLength={200}
          className="pl-8"
        />
      </div>
      <Matches query={search.query} state={search.state} items={search.items} label="Applications to filter by" onPick={a => { setText(''); onPick(a.id); }} />
    </div>
  );
}

// The application a write is about, inside a side sheet.
export function AppSearchField(props: {
  label: string;
  name: string;
  value: { id: string; name: string } | null;
  onChange: (app: { id: string; name: string } | null) => void;
  hint?: string;
  required?: boolean;
}) {
  const [text, setText] = useState('');
  const search = useAppSearch(text, !props.value);
  if (props.value) {
    return (
      <Field label={props.label} name={props.name} required={props.required} hint={props.hint}>
        {p => (
          <div className="flex items-center gap-2 rounded-md border border-line bg-secondary px-3 py-2">
            <span id={p.id} className="min-w-0 flex-1 text-sm font-medium [overflow-wrap:anywhere] text-navy" aria-describedby={p['aria-describedby']}>
              {props.value!.name}
            </span>
            <Button type="button" variant="ghost" size="sm" onClick={() => { props.onChange(null); setText(''); }} aria-label={`Choose an application other than ${props.value!.name}`}>
              <X className="size-4" aria-hidden />
              Change
            </Button>
          </div>
        )}
      </Field>
    );
  }
  return (
    <Field label={props.label} name={props.name} required={props.required} hint={props.hint ?? 'Type two or more letters of its name or purpose.'}>
      {p => (
        <div className="flex flex-col gap-1.5">
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
            <Input
              {...p}
              required={false}
              type="search"
              value={text}
              onChange={e => setText(e.target.value)}
              placeholder="Search the register"
              autoComplete="off"
              spellCheck={false}
              maxLength={200}
              className="pl-8"
            />
          </div>
          <Matches query={search.query} state={search.state} items={search.items} label="Matching applications" onPick={a => { setText(''); props.onChange(a); }} />
        </div>
      )}
    </Field>
  );
}

export const filterSectionClass = 'mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3';

// ---------- long lists ----------

export const ROWS_STEP = 25;

// How many rows a long list shows: 25, then 25 more each time, from the start again when its filters change (resetKey).
export function useShown(resetKey: string): { shown: number; more: () => void } {
  const [state, setState] = useState({ key: resetKey, shown: ROWS_STEP });
  const shown = state.key === resetKey ? state.shown : ROWS_STEP;
  return { shown, more: () => setState({ key: resetKey, shown: shown + ROWS_STEP }) };
}

export function ShowMoreRows({ shown, total, what, onMore }: { shown: number; total: number; what: string; onMore: () => void }) {
  if (total <= shown) return null;
  const next = Math.min(ROWS_STEP, total - shown);
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
      <Muted>{`The first ${shown} of ${total} ${what}.`}</Muted>
      <Button variant="outline" size="sm" onClick={onMore}>{`Show ${next} more`}</Button>
    </div>
  );
}
