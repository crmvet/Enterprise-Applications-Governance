// Tabs that live in the address: each tab is a link, so Back, a shared link and a new browser tab land on it. The Data
// page's tabs (/app/data/<tab>) and the Configuration page's views (?view=) use it; Application 360's section bar is
// the same pattern. A plain click navigates in place and keeps the scroll position, so the page does not jump.
// Each tab may carry a count (a number, or null while it is read) after its label.
//   <PageTabs label="Data views" tabs={[{ key: 'domains', label: 'Domains', href, count: 25 }, ...]} current="domains" />
import type { MouseEvent, ReactNode } from 'react';
import { navigate } from '../app/navigation';
import { cn } from '../lib/utils';

export interface PageTab<K extends string> {
  key: K;
  label: ReactNode;
  href: string;
  // A small count after the label. Null or undefined shows none.
  count?: number | null;
  // Marks the count as needing attention (gaps, breaks).
  attention?: boolean;
}

export function PageTabs<K extends string>({ label, tabs, current, className }: { label: string; tabs: PageTab<K>[]; current: K; className?: string }) {
  const go = (event: MouseEvent<HTMLAnchorElement>, href: string) => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const y = window.scrollY;
    navigate(href);
    window.scrollTo(0, y);
  };
  return (
    <nav aria-label={label} className={cn('mb-4 narrow:mb-3', className)}>
      <ul className="m-0 flex list-none flex-wrap gap-1 rounded-lg border border-line bg-card p-1 shadow-xs">
        {tabs.map(t => {
          const isCurrent = t.key === current;
          return (
            <li key={t.key}>
              <a
                href={t.href}
                aria-current={isCurrent ? 'page' : undefined}
                onClick={e => go(e, t.href)}
                className={cn(
                  'inline-flex h-8 items-center gap-1.5 rounded-md px-3 text-sm font-medium no-underline transition-colors focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring narrow:h-7 narrow:px-2.5 narrow:text-xs',
                  isCurrent ? 'bg-secondary text-navy ring-1 ring-primary' : 'text-navy-2 hover:bg-background hover:text-navy',
                )}
              >
                {t.label}
                {typeof t.count === 'number' && (
                  <span
                    className={cn(
                      'inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-full px-1.5 text-[11px] leading-none font-semibold tabular-nums',
                      t.attention && t.count > 0 ? 'bg-gold-soft text-gold-strong' : 'bg-muted text-navy-2',
                    )}
                  >
                    {t.count}
                  </span>
                )}
              </a>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
