// The people picker of the web app: a person is found by name (find_people), never typed as an object ID. It reads the
// people the product knows (their Principal References): a new name shows once the owner check or the directory
// seeding has met the person. The picker keeps the chosen person as a PrincipalRef, so a form sends the object ID and
// shows the name.
//
//   <PersonSearchField label="Business owner" name="businessOwnerObjectId" value={owner} onChange={setOwner} />
import { Search, UserRound, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { PersonFound, PrincipalRef } from '../data/contracts';
import { dataOf, useTool } from '../data/toolClient';
import { Field } from './actions';
import { Button } from './ui/button';
import { Input } from './ui/input';

const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

// The text the search asks with: two letters at least, after the person stops typing for a moment.
export function useSettled(text: string, delayMs = 250): string {
  const [settled, setSettled] = useState(text);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(text), delayMs);
    return () => clearTimeout(timer);
  }, [text, delayMs]);
  return settled;
}

// The note a found person carries: a person the owner check found disabled or deleted is still found, marked.
export function personNote(p: Pick<PersonFound, 'directoryState'>): string | null {
  if (p.directoryState === 'disabled') return 'disabled in the directory';
  if (p.directoryState === 'deleted') return 'deleted from the directory';
  return null;
}

export function PersonSearchField(props: {
  label: string;
  // The tool input field it fills, so a refusal that names the field marks it.
  name: string;
  value: PrincipalRef | null;
  onChange: (person: PrincipalRef | null) => void;
  hint?: string;
  required?: boolean;
  // People the picker does not offer, such as the holder a successor replaces.
  exclude?: string[];
  // True leaves out people the owner check found disabled or deleted.
  activeOnly?: boolean;
}) {
  const [text, setText] = useState('');
  const settled = useSettled(text.trim());
  const asking = settled.length >= 2 && !props.value;
  const state = useTool('find_people', asking ? { text: settled, activeOnly: props.activeOnly ?? false, top: 8 } : null);
  const found = (dataOf(state)?.items ?? []).filter(p => !(props.exclude ?? []).some(x => sameId(x, p.objectId)));
  const total = dataOf(state)?.totalCount ?? 0;
  const failed = asking && state.status === 'done' && !state.envelope.ok ? state.envelope : null;

  if (props.value) {
    return (
      <Field label={props.label} name={props.name} required={props.required} hint={props.hint}>
        {p => (
          <div className="flex items-center gap-2 rounded-md border border-line bg-secondary px-3 py-2">
            <UserRound className="size-4 shrink-0 text-navy-3" aria-hidden />
            <span id={p.id} className="min-w-0 flex-1 text-sm font-medium text-navy [overflow-wrap:anywhere]" aria-describedby={p['aria-describedby']}>
              {props.value!.displayLabel || 'A person the product knows'}
            </span>
            <Button type="button" variant="ghost" size="sm" onClick={() => { props.onChange(null); setText(''); }} aria-label={`Choose someone other than ${props.value!.displayLabel}`}>
              <X className="size-4" aria-hidden />
              Change
            </Button>
          </div>
        )}
      </Field>
    );
  }

  return (
    <Field label={props.label} name={props.name} required={props.required} hint={props.hint ?? 'Type two or more letters of the name.'}>
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
              placeholder="Search by name"
              autoComplete="off"
              spellCheck={false}
              maxLength={100}
              className="pl-8"
            />
          </div>
          {asking && (
            <div aria-live="polite" className="flex flex-col gap-1">
              {state.status === 'loading' && <span className="text-xs text-navy-3">Searching the people the product knows</span>}
              {failed && <span className="text-xs text-danger">The search did not answer: {failed.error.message}</span>}
              {state.status === 'done' && !failed && found.length === 0 && (
                <span className="text-xs text-navy-2">Nobody the product knows matches "{settled}". Check the spelling, or ask a platform admin to run the owner check.</span>
              )}
              {found.length > 0 && (
                <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={`People who match ${settled}`}>
                  {found.map(person => {
                    const note = personNote(person);
                    return (
                      <li key={person.objectId}>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="h-auto w-full justify-start py-1.5 text-left"
                          onClick={() => props.onChange({ objectId: person.objectId, displayLabel: person.displayLabel, principalKind: person.principalKind })}
                        >
                          <UserRound className="size-4 shrink-0 text-navy-3" aria-hidden />
                          <span className="min-w-0 [overflow-wrap:anywhere]">{person.displayLabel}</span>
                          {note && <span className="text-xs text-navy-3">({note})</span>}
                        </Button>
                      </li>
                    );
                  })}
                </ul>
              )}
              {total > found.length && found.length > 0 && <span className="text-xs text-navy-3">More people match. Type more of the name.</span>}
            </div>
          )}
        </div>
      )}
    </Field>
  );
}
