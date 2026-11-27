// Small pieces the four Govern pages of the admin group share: moving a page parameter in the address without losing the
// scroll position, a labelled search box, a compact guideline link, a figure that drills, the page's one failure, and a
// short note.
import { BookOpen, CircleAlert, Info, Search, TriangleAlert } from 'lucide-react';
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { guidelineHref, hrefTo, withParams } from '../../app/links';
import { navigate, useLocationKey } from '../../app/navigation';
import { LinkButton, TextLink } from '../../components/blocks';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import type { Failure } from '../../data/contracts';
import { cn } from '../../lib/utils';

// Sets or clears page parameters of the current address. A filter replaces the address (Back leaves the page, as on the
// other lists); opening a record view or a drill pushes it, so Back closes it. The scroll position stays unless asked.
export function useParamNav(): (params: Record<string, string | undefined>, options?: { push?: boolean; top?: boolean }) => void {
  const location = useLocationKey();
  return (params, options = {}) => {
    const y = window.scrollY;
    navigate(withParams(location, params), !options.push);
    if (!options.top) window.scrollTo(0, y);
  };
}

// A search box whose text lives in the address (?q=). It keeps its own text while the person types and writes the
// address a moment later, so typing stays smooth and Back keeps the search.
export function SearchField(props: { label: string; value: string; onChange: (value: string) => void; placeholder?: string; className?: string }) {
  const id = useId();
  const [text, setText] = useState(props.value);
  const latest = useRef(props.onChange);
  latest.current = props.onChange;
  const sent = useRef(props.value);
  // The address changed from outside (Back, a cleared filter): show it. The text this box wrote itself is not shown
  // again, so a letter typed meanwhile is kept.
  useEffect(() => {
    if (props.value !== sent.current) {
      sent.current = props.value;
      setText(props.value);
    }
  }, [props.value]);
  useEffect(() => {
    if (text === sent.current) return;
    const t = window.setTimeout(() => {
      sent.current = text;
      latest.current(text);
    }, 250);
    return () => window.clearTimeout(t);
  }, [text]);
  return (
    <div className={cn('flex min-w-0 flex-col gap-1', props.className)}>
      <Label htmlFor={id} className="text-xs text-navy-2">{props.label}</Label>
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
        <Input
          id={id}
          type="search"
          className="pl-8"
          placeholder={props.placeholder}
          value={text}
          onChange={e => setText(e.target.value)}
          autoComplete="off"
          spellCheck={false}
          maxLength={100}
        />
      </div>
    </div>
  );
}

// A guideline as a small link: "G17 People decide". It opens the Guidelines page at that guideline. The full chip with
// the rule text (components/actions.tsx GuidelineChip) reads the configuration once per chip, so long lists use this.
export function GuidelineLink({ number, title, className }: { number: string; title?: string | null; className?: string }) {
  return (
    <a
      href={guidelineHref(number)}
      className={cn(
        'inline-flex max-w-full items-center gap-1 rounded-md border border-info-line bg-info-soft px-1.5 py-px text-xs leading-[18px] font-semibold text-brand-dark no-underline hover:underline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring',
        className,
      )}
      aria-label={`Guideline ${number}${title ? `, ${title}` : ''}. Opens the guideline.`}
    >
      <BookOpen className="size-3 shrink-0" aria-hidden />
      <span className="truncate">{title ? `${number} ${title}` : number}</span>
    </a>
  );
}

// A number in a sentence that opens the rows it counts.
export function Figure({ value, label, onOpen, attention }: { value: number; label: string; onOpen?: () => void; attention?: boolean }) {
  if (!onOpen || value === 0) {
    return <span className={cn('tabular-nums', attention && value > 0 && 'font-semibold text-gold-strong')}>{value.toLocaleString('en-US')} {label}</span>;
  }
  return (
    <LinkButton onClick={onOpen} className={cn('tabular-nums', attention && 'text-gold-strong')} aria-label={`${value} ${label}. Opens the list.`}>
      {value.toLocaleString('en-US')} {label}
    </LinkButton>
  );
}

// The page's main read failed: one failure for the whole page, rather than the same failure in every panel, and a way
// on. A refusal by role says which roles read the page.
export function PageFailure({ failure, readers }: { failure: Failure; readers?: string }) {
  const code = failure.error.code;
  const soft = code === 'not_authorized' || code === 'not_signed_in' || code === 'rate_limited' || code === 'not_found';
  const title = code === 'not_authorized' ? 'You cannot see this page'
    : code === 'not_signed_in' ? 'Sign in to see this page'
      : code === 'rate_limited' ? 'Too many requests just now'
        : 'This page could not load';
  return (
    <div className="mb-4 flex flex-col gap-2">
      <Alert variant={soft ? 'warning' : 'error'} role="alert">
        {soft ? <TriangleAlert aria-hidden /> : <CircleAlert aria-hidden />}
        <AlertTitle>{title}</AlertTitle>
        <AlertDescription>
          <p className="m-0">
            {failure.error.message}
            {code === 'not_authorized' && readers ? ` ${readers}` : ''}
            {code === 'not_authorized' || code === 'not_signed_in' ? '' : ' Try again in a moment.'}
            {` Reference ${failure.correlationId}.`}
          </p>
        </AlertDescription>
      </Alert>
      <p className="m-0 text-sm"><TextLink href={hrefTo({ page: 'overview' })}>Go to the overview</TextLink></p>
    </div>
  );
}

// A short note under a panel's content: where a number comes from, or why something is not shown.
export function Note({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <p className={cn('m-0 flex items-start gap-1.5 text-xs leading-relaxed text-navy-3', className)}>
      <Info className="mt-px size-3.5 shrink-0" aria-hidden />
      <span>{children}</span>
    </p>
  );
}

// A small uppercase heading inside a panel.
export function SubHeading({ children, id }: { children: ReactNode; id?: string }) {
  return <h3 id={id} className="m-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{children}</h3>;
}
