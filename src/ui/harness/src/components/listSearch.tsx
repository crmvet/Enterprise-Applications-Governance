// The text search of a list page, kept in the address (wave 4: "no search, no FILTER for list of APPS?"). Every list
// page searches the same way: one box above the list, the words in q= so a search can be linked and survives a refresh,
// and the address replaced (not pushed) while the person types, so Back leaves the page instead of undoing letters.
//
//   const text = useListText();                       the words in q= (or another parameter), or undefined
//   <ListSearch label="Search the teams" placeholder="Team or role" />
//                                                     the box: writes q= once the person stops typing, follows the
//                                                     address when it changes elsewhere (a link, Back)
//   filterByText(rows, text, r => [r.name, ...])      data/textSearch.ts: the rows that match, for a tool with no text
//
// A page whose route carries the text (applications, intake, search, teams) passes it as value and its own onChange
// that builds the route; every other page uses the address parameter as is. A page whose tool takes text sends the
// words to the tool; a page whose tool does not filters the rows it holds and says so when more rows exist.
import { Search } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { withParams } from '../app/links';
import { navigate, useLocationKey } from '../app/navigation';
import { cn } from '../lib/utils';
import { Input } from './ui/input';

const MAX = 200;
const SETTLE_MS = 350;

function paramOf(locationKey: string, name: string): string | undefined {
  const at = locationKey.indexOf('?');
  const value = new URLSearchParams(at < 0 ? '' : locationKey.slice(at + 1)).get(name);
  const text = (value ?? '').trim().slice(0, MAX);
  return text || undefined;
}

// The words of the list's search in the address (q= unless named otherwise), trimmed, or undefined.
export function useListText(name = 'q'): string | undefined {
  return paramOf(useLocationKey(), name);
}

// Sets the list's search in the address in place (replace), keeping the scroll position and every other parameter. A
// paged list's own position (skip, page, at) is reset by the page through extra.
export function setListText(text: string | undefined, name = 'q', extra: Record<string, string | undefined> = {}): void {
  const y = window.scrollY;
  navigate(withParams(`${window.location.pathname}${window.location.search}`, { ...extra, [name]: text?.trim().slice(0, MAX) || undefined }), true);
  window.scrollTo(0, y);
}

export function ListSearch(props: {
  // The accessible name, such as "Search the teams by name or role".
  label: string;
  placeholder: string;
  // The address parameter (q). Ignored when value and onChange are given.
  name?: string;
  // A page whose route carries the text: its value, and how to change it.
  value?: string;
  onChange?: (text: string | undefined) => void;
  // Parameters to clear when the words change, such as a paged list's position.
  resets?: string[];
  className?: string;
}) {
  const name = props.name ?? 'q';
  const fromAddress = useListText(name);
  const value = props.onChange ? props.value : fromAddress;
  const [text, setText] = useState(value ?? '');
  const id = useId();
  const typed = useRef(false);

  // The address changed elsewhere (a link, Back): the box follows it.
  useEffect(() => {
    typed.current = false;
    setText(value ?? '');
  }, [value]);

  // Once the person stops typing, the words go to the address (or the page's own route).
  useEffect(() => {
    if (!typed.current) return;
    const timer = window.setTimeout(() => {
      const next = text.trim().slice(0, MAX) || undefined;
      if (next === (value ?? undefined)) return;
      if (props.onChange) props.onChange(next);
      else setListText(next, name, Object.fromEntries((props.resets ?? []).map(k => [k, undefined])));
    }, SETTLE_MS);
    return () => window.clearTimeout(timer);
  }, [text]);

  return (
    <div className={cn('relative w-[280px] narrow:w-full', props.className)} role="search">
      <label htmlFor={id} className="sr-only">{props.label}</label>
      <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
      <Input
        id={id}
        type="search"
        value={text}
        onChange={e => { typed.current = true; setText(e.target.value); }}
        placeholder={props.placeholder}
        autoComplete="off"
        spellCheck={false}
        maxLength={MAX}
        className="pl-8"
      />
    </div>
  );
}
