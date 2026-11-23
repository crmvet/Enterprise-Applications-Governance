// One list row that reads well at 1280 px and at 390 px: a leading visual, a primary line, muted detail lines,
// and an end column for a badge. Lists replace tables in the panels, so nothing scrolls sideways on a phone.
import { ExternalLink as ExternalLinkIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { externalHref } from '../data/format';
import { cn } from '../lib/utils';
import { linkClass } from './blocks';

export function RowList({ children, label }: { children: ReactNode; label?: string }) {
  return <ul className="m-0 list-none p-0" aria-label={label}>{children}</ul>;
}

export interface RowProps {
  lead?: ReactNode;
  primary: ReactNode;
  secondary?: ReactNode;
  tertiary?: ReactNode;
  end?: ReactNode;
  endSub?: ReactNode;
  // Badges that belong to the text, such as deployment flags. They wrap under the detail lines.
  badges?: ReactNode;
  // A wide end (two or more badges) moves under the text below 720 px, so the text keeps the full width.
  wideEnd?: boolean;
}

export function Row(props: RowProps) {
  const lead = props.lead !== undefined;
  return (
    <li
      className={cn(
        'grid items-start gap-x-3 border-t border-line py-2.5 first:border-t-0 first:pt-0.5 last:pb-0.5',
        lead ? 'grid-cols-[32px_minmax(0,1fr)_auto]' : 'grid-cols-[minmax(0,1fr)_auto]',
        props.wideEnd && (lead ? 'narrow:grid-cols-[32px_minmax(0,1fr)] narrow:gap-y-1.5' : 'narrow:grid-cols-[minmax(0,1fr)] narrow:gap-y-1.5'),
      )}
    >
      {lead && <div className="pt-px">{props.lead}</div>}
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="text-sm leading-5 font-semibold [overflow-wrap:anywhere] text-navy">{props.primary}</span>
        {props.secondary && <span className="text-xs leading-4 text-navy-3">{props.secondary}</span>}
        {props.tertiary && <span className="text-xs leading-4 text-navy-3">{props.tertiary}</span>}
        {props.badges && <div className="mt-1 flex flex-wrap gap-1">{props.badges}</div>}
      </div>
      {(props.end || props.endSub) && (
        <div
          className={cn(
            'flex flex-col items-end gap-1 text-right narrow:max-w-[44vw]',
            props.wideEnd && 'narrow:col-[-2/-1] narrow:max-w-none narrow:items-start narrow:text-left',
          )}
        >
          {props.end && <div className={cn('flex flex-wrap justify-end gap-1', props.wideEnd && 'narrow:justify-start')}>{props.end}</div>}
          {props.endSub && <span className="text-xs leading-4 text-navy-3">{props.endSub}</span>}
        </div>
      )}
    </li>
  );
}

// A link out of the product (a wiki page, a run, a repository). URLs are https only (the schemas say so); anything else
// is shown as text, not as a link (externalHref).
export function ExternalLink({ href, children }: { href: string; children: ReactNode }) {
  const safe = externalHref(href);
  if (!safe) return <span>{children}</span>;
  return (
    <a href={safe} target="_blank" rel="noopener noreferrer" className={cn(linkClass, 'inline-flex items-center gap-[3px]')}>
      {children}
      <ExternalLinkIcon className="size-3" aria-label="opens in a new tab" />
    </a>
  );
}
