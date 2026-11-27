// The top bar's parts, inside the shell's tool client (AppShell.tsx renders them):
//   SessionScope   reads whoami once per data version and shares it (data/session.ts), for the menu and every page
//   SearchBox      the global search: applications by name or number, people by name, records by number (DEC-0123).
//                  It opens the search page (/app/search?q=<text>).
//   MainMenu       the page menu: the bar, and More (a menu on a wide screen, a side sheet with every page and the
//                  search on a phone). My work carries the number of items that need the person.
// Which page sits where is app/menu.ts; the count and the search parsing are app/shellLogic.ts.
import { ChevronDown, Menu, Search } from 'lucide-react';
import { Fragment, useEffect, useId, useState, useSyncExternalStore, type FormEvent, type ReactNode } from 'react';
import { useRead } from '../data/reads';
import { SessionContext, toolsOf, useWhoAmIState } from '../data/session';
import { dataOf, useTool } from '../data/toolClient';
import { Button } from '../components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../components/ui/dropdown-menu';
import { Input } from '../components/ui/input';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '../components/ui/sheet';
import { cn } from '../lib/utils';
import { groupEntries, menuEntries, menuKeyOf, splitMenu, type MenuEntry, type MenuKey, type ScreenWidth } from './menu';
import { navigate } from './navigation';
import { appHref, type AppRoute } from './routes';
import { countText, MY_WORK_QUEUE_KINDS, MY_WORK_QUEUE_TOP, myWorkCount } from './shellLogic';
import { answerableTotal } from '../pages/work-approvals/logic';

// ---------- the screen width ----------

const NARROW = '(max-width: 719px)';
const MEDIUM = '(max-width: 1023px)';

function subscribeWidth(callback: () => void) {
  const narrow = window.matchMedia(NARROW);
  const medium = window.matchMedia(MEDIUM);
  narrow.addEventListener('change', callback);
  medium.addEventListener('change', callback);
  return () => {
    narrow.removeEventListener('change', callback);
    medium.removeEventListener('change', callback);
  };
}

function readWidth(): ScreenWidth {
  if (window.matchMedia(NARROW).matches) return 'narrow';
  return window.matchMedia(MEDIUM).matches ? 'medium' : 'wide';
}

// The menu's three widths: narrow below 720 px, medium below 1024 px, wide above. The same breakpoints as the
// narrow: and medium: variants in styles/globals.css.
export function useScreenWidth(): ScreenWidth {
  return useSyncExternalStore(subscribeWidth, readWidth, () => 'wide' as const);
}

// ---------- the session ----------

export function SessionScope({ children }: { children: ReactNode }) {
  const state = useTool('whoami', {});
  return <SessionContext.Provider value={state}>{children}</SessionContext.Provider>;
}

// ---------- the global search ----------

export function SearchBox({ base, initial = '', className, autoFocus, onSearched, id }: {
  base: string;
  initial?: string;
  className?: string;
  autoFocus?: boolean;
  // After the search page opened, such as closing the phone menu.
  onSearched?: () => void;
  id?: string;
}) {
  const ownId = useId();
  const inputId = id ?? ownId;
  const [text, setText] = useState(initial);
  useEffect(() => { setText(initial); }, [initial]);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const q = text.trim();
    navigate(appHref(q ? { page: 'search', text: q } : { page: 'search' }, base));
    onSearched?.();
  };
  return (
    <form role="search" className={cn('relative flex items-center', className)} onSubmit={submit}>
      <label htmlFor={inputId} className="sr-only">Search applications, people and record numbers</label>
      <Search className="pointer-events-none absolute left-2.5 size-4 text-navy-3" aria-hidden />
      <Input
        id={inputId}
        type="search"
        value={text}
        onChange={e => setText(e.target.value)}
        placeholder="Search apps, people, DEC-0123"
        maxLength={100}
        autoFocus={autoFocus}
        className="h-8 bg-card pl-8 text-sm text-navy placeholder:text-navy-3"
      />
    </form>
  );
}

// ---------- the My work count ----------

// The number of items My work lists for the person, or null while it is read or when it cannot be read.
function useMyWorkCount(): { count: number; more: boolean } | null {
  const queue = useRead('get_my_queue', { kinds: [...MY_WORK_QUEUE_KINDS], top: MY_WORK_QUEUE_TOP });
  const approvals = useRead('list_approvals', { view: 'mine', top: 1 });
  const q = dataOf(queue);
  const a = dataOf(approvals);
  // The approvals the person may answer now (answerableCount, wave 4b verify round), as My work and Approvals count them.
  return myWorkCount(answerableTotal(a), q ? q.items : null, !!q?.skipToken);
}

// ---------- the page menu ----------

// One page link in the underlined menu row. The current page gets the blue line and semibold text; a hidden semibold
// copy of the label reserves its width, so the row does not shift when the current page changes.
function tabClass(current: boolean): string {
  return cn(
    'relative flex h-11 cursor-pointer items-center gap-1 rounded-md px-2.5 text-sm whitespace-nowrap text-navy-2 no-underline transition-colors',
    'after:absolute after:inset-x-2.5 after:bottom-1 after:h-[3px] after:rounded-full after:transition-colors',
    'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring narrow:h-10',
    current ? 'font-semibold text-navy after:bg-primary' : 'hover:text-navy hover:after:bg-line-strong',
  );
}

function TabText({ text }: { text: string }) {
  return (
    <span className="grid">
      <span className="col-start-1 row-start-1">{text}</span>
      <span className="invisible col-start-1 row-start-1 font-semibold" aria-hidden>{text}</span>
    </span>
  );
}

function CountPill({ value }: { value: { count: number; more: boolean } | null }) {
  if (!value || value.count === 0) return null;
  const text = countText(value);
  return (
    <>
      <span className="ml-0.5 inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-primary px-1.5 text-[11px] leading-none font-semibold text-white" aria-hidden>
        {text}
      </span>
      <span className="sr-only">({text} {value.count === 1 && !value.more ? 'item needs' : 'items need'} you)</span>
    </>
  );
}

export function MainMenu({ route, base, openApplicationId, demo, frameClass }: {
  route: AppRoute;
  base: string;
  openApplicationId: string | null;
  demo: boolean;
  frameClass: string;
}) {
  const width = useScreenWidth();
  const whoami = useWhoAmIState();
  const count = useMyWorkCount();
  const entries = menuEntries(openApplicationId, { demo, tools: toolsOf(whoami) });
  const { bar, more } = splitMenu(entries, width);
  const currentKey = menuKeyOf(route);
  const href = (r: AppRoute) => appHref(r, base);
  const narrow = width === 'narrow';
  return (
    <nav className={cn(frameClass, 'flex')} aria-label="Main">
      <ul className="m-0 -ml-2.5 flex min-w-0 list-none items-center gap-1 overflow-x-auto p-0 narrow:gap-0" aria-label="Pages">
        {bar.map(entry => {
          const current = currentKey === entry.key;
          return (
            <li key={entry.key}>
              <a className={tabClass(current)} href={href(entry.route)} aria-current={current ? 'page' : undefined}>
                <TabText text={narrow ? entry.short : entry.label} />
                {entry.key === 'work' && <CountPill value={count} />}
              </a>
            </li>
          );
        })}
        {more.length > 0 && (
          <li>
            {narrow
              ? <MenuSheet entries={more} currentKey={currentKey} href={href} base={base} />
              : <MoreMenu entries={more} currentKey={currentKey} href={href} />}
          </li>
        )}
      </ul>
    </nav>
  );
}

// The pages that are not in the bar, on a wide or medium screen. When the current page is one of them, the trigger
// names it and carries the line.
function MoreMenu({ entries, currentKey, href }: { entries: MenuEntry[]; currentKey: MenuKey | null; href: (r: AppRoute) => string }) {
  const currentEntry = entries.find(e => e.key === currentKey) ?? null;
  const groups = groupEntries(entries);
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className={tabClass(currentEntry !== null)}
          aria-label={currentEntry ? `More pages. Current page: ${currentEntry.label}` : 'More pages'}
        >
          <TabText text={currentEntry ? currentEntry.label : 'More'} />
          <ChevronDown className="size-4 shrink-0" aria-hidden />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" collisionPadding={8} className="max-h-[min(70vh,560px)] min-w-56 overflow-y-auto text-navy">
        {groups.map((g, i) => (
          <Fragment key={g.group}>
            {i > 0 && <DropdownMenuSeparator />}
            <DropdownMenuGroup>
              <DropdownMenuLabel className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{g.label}</DropdownMenuLabel>
              {g.items.map(entry => {
                const current = entry.key === currentKey;
                return (
                  <DropdownMenuItem key={entry.key} asChild className={cn('cursor-pointer text-navy', current && 'font-semibold')}>
                    <a href={href(entry.route)} aria-current={current ? 'page' : undefined}>{entry.label}</a>
                  </DropdownMenuItem>
                );
              })}
            </DropdownMenuGroup>
          </Fragment>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

// On a phone: a side sheet with the search and every page that is not in the bar, by group. A link closes it. The
// trigger always reads More, so the bar keeps one row at 360 px; when the current page is in the sheet it carries the
// line, and its name says which page.
function MenuSheet({ entries, currentKey, href, base }: { entries: MenuEntry[]; currentKey: MenuKey | null; href: (r: AppRoute) => string; base: string }) {
  const [open, setOpen] = useState(false);
  const currentEntry = entries.find(e => e.key === currentKey) ?? null;
  const groups = groupEntries(entries);
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <button
          type="button"
          className={tabClass(currentEntry !== null)}
          aria-label={currentEntry ? `Menu. Current page: ${currentEntry.label}` : 'Menu'}
        >
          <Menu className="size-4 shrink-0" aria-hidden />
          <TabText text="More" />
        </button>
      </SheetTrigger>
      <SheetContent side="left" className="w-[min(320px,88vw)] gap-0 overflow-y-auto text-navy">
        <SheetHeader className="pb-2">
          <SheetTitle className="text-base text-navy">Menu</SheetTitle>
          <SheetDescription className="text-xs text-navy-3">Every page, and the search.</SheetDescription>
        </SheetHeader>
        <div className="px-4 pb-3">
          <SearchBox base={base} onSearched={() => setOpen(false)} />
        </div>
        <nav aria-label="All pages" className="flex flex-col gap-3 px-2 pb-6">
          {groups.map(g => (
            <div key={g.group} className="flex flex-col">
              <h2 className="m-0 px-2 pb-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{g.label}</h2>
              <ul className="m-0 flex list-none flex-col p-0">
                {g.items.map(entry => {
                  const current = entry.key === currentKey;
                  return (
                    <li key={entry.key}>
                      <a
                        href={href(entry.route)}
                        aria-current={current ? 'page' : undefined}
                        onClick={() => setOpen(false)}
                        className={cn(
                          'flex min-h-10 items-center rounded-md px-2 text-sm text-navy no-underline hover:bg-muted focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
                          current && 'bg-secondary font-semibold',
                        )}
                      >
                        {entry.label}
                      </a>
                    </li>
                  );
                })}
              </ul>
            </div>
          ))}
        </nav>
      </SheetContent>
    </Sheet>
  );
}

// The search box's place in the top row: a field on a wide screen; below 1024 px an icon that opens the search page,
// whose own field takes the focus, so the row keeps room for the demo persona switch and the person card (the phone
// menu holds the field too).
export function TopBarSearch({ base, route }: { base: string; route: AppRoute }) {
  const width = useScreenWidth();
  const initial = route.page === 'search' ? route.text ?? '' : '';
  if (width !== 'wide') {
    return (
      <Button variant="ghost" size="icon-sm" className="text-navy-2 hover:text-navy" asChild>
        <a href={appHref({ page: 'search' }, base)} aria-label="Search applications, people and record numbers"><Search aria-hidden /></a>
      </Button>
    );
  }
  return <SearchBox base={base} initial={initial} className="w-[272px]" />;
}

