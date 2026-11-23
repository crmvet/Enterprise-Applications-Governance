// The harness shell: a light top bar, the four page tabs, and the page area.
// In the product the host is the model-driven app (binding A) or the web app (binding C, src/app/); only the page area
// carries over.
import { useEffect, useMemo, useState } from 'react';
import { fixtureClient } from './data/fixtureClient';
import { ToolClientContext } from './data/toolClient';
import { HostContext, parseRoute, routeHref, type HandOff, type Host, type Route } from './harness';
import { Application360 } from './pages/Application360';
import { DependencyMap } from './pages/DependencyMap';
import { Overview } from './pages/Overview';
import { Timeline } from './pages/Timeline';
import { useNarrow } from './components/blocks';
import { Badge } from './components/ui/badge';
import { Toaster, toast } from './components/ui/toast';
import { cn } from './lib/utils';

// The fixtures are a snapshot taken on this day (their asOf), so the harness clock stands still on it.
const HARNESS_TODAY = '2026-11-22';
const DEFAULT_APP = '10000000-0000-4000-8000-000000000001';

const tabs: { value: Route['page']; label: string; short: string }[] = [
  { value: 'overview', label: 'Overview', short: 'Overview' },
  { value: 'app', label: 'Application 360', short: 'App 360' },
  { value: 'map', label: 'Dependency map', short: 'Map' },
  { value: 'timeline', label: 'Timeline', short: 'Timeline' },
];

// One page link in the underlined tab bar. The current page gets the blue line and semibold text; a hidden semibold
// copy of the label reserves its width, so the bar does not shift when the current page changes.
function tabClass(current: boolean): string {
  return cn(
    'relative flex h-[52px] items-center rounded-md px-3 text-sm text-navy-2 no-underline transition-colors',
    'after:absolute after:inset-x-3 after:bottom-1.5 after:h-[3px] after:rounded-full after:transition-colors',
    'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
    'narrow:h-10 narrow:px-2.5 narrow:after:inset-x-2.5 narrow:after:bottom-1',
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

function useHashRoute(): Route {
  const read = () => parseRoute(window.location.hash, DEFAULT_APP);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const onHash = () => {
      setRoute(read());
      window.scrollTo(0, 0);
    };
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);
  return route;
}

export function App() {
  const route = useHashRoute();
  const narrow = useNarrow();

  const host = useMemo<Host>(
    () => ({
      today: HARNESS_TODAY,
      href: routeHref,
      drill: (what: string) => {
        toast({
          title: `Opens a list: ${what}`,
          description: 'The product opens the matching view (binding A) or list (binding C). The harness stops here.',
        });
      },
      handOff: (request: HandOff) => {
        toast({
          title: request.title,
          description: `Read-only page: the web app shows this request for an AI client, which calls ${request.tool}. The harness stops here.`,
        });
      },
      showRecordLinks: true,
    }),
    [],
  );

  const appId = route.page === 'app' || route.page === 'map' ? route.id : DEFAULT_APP;

  return (
    <ToolClientContext.Provider value={fixtureClient}>
      <HostContext.Provider value={host}>
        <div className="sticky top-0 z-10 border-b border-line bg-card shadow-xs">
          <div className="h-[3px] bg-primary" />
          <div className="mx-auto flex min-h-[52px] max-w-[1280px] items-center gap-x-6 px-6 narrow:min-h-0 narrow:flex-wrap narrow:gap-x-3 narrow:gap-y-1 narrow:px-4 narrow:pt-2">
            <div className="flex min-w-0 items-center gap-x-2.5">
              <span
                className="relative grid size-7 shrink-0 place-items-center overflow-hidden rounded-[7px] bg-navy text-[15px] font-bold text-white after:absolute after:right-0 after:bottom-0 after:size-[9px] after:bg-gold"
                aria-hidden
              >
                E
              </span>
              <span className="truncate text-base font-semibold text-navy narrow:text-sm">Enterprise Applications Governance</span>
              <Badge variant="warning" className="shrink-0 rounded-full px-2 font-medium">
                <span className="narrow:hidden">Local harness, fictional data</span>
                <span className="hidden narrow:inline">Fictional data</span>
              </Badge>
            </div>
            <nav className="ml-auto narrow:-ml-2.5 narrow:w-[calc(100%+20px)]" aria-label="Pages">
              <ul className="m-0 flex list-none gap-1 p-0 narrow:gap-0">
                {tabs.map(t => {
                  const next: Route = t.value === 'app' || t.value === 'map' ? { page: t.value, id: appId } : t.value === 'timeline' ? { page: 'timeline' } : { page: 'overview' };
                  const current = route.page === t.value;
                  return (
                    <li key={t.value}>
                      <a className={tabClass(current)} href={routeHref(next)} aria-current={current ? 'page' : undefined}>
                        <TabText text={narrow ? t.short : t.label} />
                      </a>
                    </li>
                  );
                })}
              </ul>
            </nav>
          </div>
        </div>
        <main className="mx-auto max-w-[1280px] px-6 pt-6 pb-12 narrow:px-4 narrow:pt-4 narrow:pb-8">
          {route.page === 'overview' && <Overview />}
          {route.page === 'app' && <Application360 key={route.id} applicationId={route.id} />}
          {route.page === 'map' && <DependencyMap key={route.id} applicationId={route.id} />}
          {route.page === 'timeline' && <Timeline />}
        </main>
        <Toaster duration={5000} />
      </HostContext.Provider>
    </ToolClientContext.Provider>
  );
}
