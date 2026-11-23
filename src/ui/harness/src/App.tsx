// The harness shell: a light top bar, the four page tabs, and the page area.
// In the product the host is the model-driven app (binding A) or the web app (binding C, src/app/); only the page area
// carries over.
import {
  Tab,
  TabList,
  Toast,
  ToastBody,
  ToastTitle,
  Toaster,
  makeStyles,
  tokens,
  useId,
  useToastController,
} from '@fluentui/react-components';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { fixtureClient } from './data/fixtureClient';
import { ToolClientContext } from './data/toolClient';
import { HostContext, parseRoute, routeHref, type HandOff, type Host, type Route } from './harness';
import { Application360 } from './pages/Application360';
import { DependencyMap } from './pages/DependencyMap';
import { Overview } from './pages/Overview';
import { Timeline } from './pages/Timeline';
import { media, useNarrow } from './components/ui';
import { palette } from './theme';

// The fixtures are a snapshot taken on this day (their asOf), so the harness clock stands still on it.
const HARNESS_TODAY = '2026-11-22';
const DEFAULT_APP = '10000000-0000-4000-8000-000000000001';

const useStyles = makeStyles({
  bar: {
    position: 'sticky',
    top: 0,
    zIndex: 10,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${palette.line}`,
    boxShadow: '0 1px 2px rgba(10, 42, 74, 0.04)',
  },
  accent: { height: '3px', backgroundColor: palette.blue },
  barInner: {
    maxWidth: '1280px',
    margin: '0 auto',
    padding: '0 24px',
    display: 'flex',
    alignItems: 'center',
    columnGap: '24px',
    minHeight: '52px',
    [media.narrow]: { padding: '8px 16px 0', flexWrap: 'wrap', rowGap: '4px', columnGap: '12px', minHeight: 'auto' },
  },
  brand: { display: 'flex', alignItems: 'center', columnGap: '10px', flexShrink: 0 },
  mark: {
    width: '28px',
    height: '28px',
    borderRadius: '7px',
    backgroundColor: palette.navy,
    color: palette.white,
    display: 'grid',
    placeItems: 'center',
    fontWeight: tokens.fontWeightBold,
    fontSize: '15px',
    position: 'relative',
    overflow: 'hidden',
    '::after': { content: '""', position: 'absolute', right: 0, bottom: 0, width: '9px', height: '9px', backgroundColor: palette.gold },
  },
  name: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase400, color: palette.navy },
  harnessTag: {
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.goldDark,
    backgroundColor: palette.lightGold,
    border: `1px solid #f3dfa6`,
    borderRadius: '10px',
    padding: '1px 8px',
    whiteSpace: 'nowrap',
  },
  tabs: {
    marginLeft: 'auto',
    [media.narrow]: { marginLeft: '-10px', width: 'calc(100% + 20px)' },
  },
  main: {
    maxWidth: '1280px',
    margin: '0 auto',
    padding: '24px 24px 48px',
    [media.narrow]: { padding: '16px 16px 32px' },
  },
});

const tabs: { value: Route['page']; label: string; short: string }[] = [
  { value: 'overview', label: 'Overview', short: 'Overview' },
  { value: 'app', label: 'Application 360', short: 'App 360' },
  { value: 'map', label: 'Dependency map', short: 'Map' },
  { value: 'timeline', label: 'Timeline', short: 'Timeline' },
];

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
  const s = useStyles();
  const route = useHashRoute();
  const narrow = useNarrow();
  const toasterId = useId('toaster');
  const { dispatchToast } = useToastController(toasterId);

  const drill = useCallback(
    (what: string) => {
      dispatchToast(
        <Toast>
          <ToastTitle>Opens a list: {what}</ToastTitle>
          <ToastBody>The product opens the matching view (binding A) or list (binding C). The harness stops here.</ToastBody>
        </Toast>,
        { intent: 'info', timeout: 5000 },
      );
    },
    [dispatchToast],
  );

  const handOff = useCallback(
    (request: HandOff) => {
      dispatchToast(
        <Toast>
          <ToastTitle>{request.title}</ToastTitle>
          <ToastBody>Read-only page: the web app shows this request for an AI client, which calls {request.tool}. The harness stops here.</ToastBody>
        </Toast>,
        { intent: 'info', timeout: 5000 },
      );
    },
    [dispatchToast],
  );

  const host = useMemo<Host>(
    () => ({ today: HARNESS_TODAY, href: routeHref, drill, handOff, showRecordLinks: true }),
    [drill, handOff],
  );

  const appId = route.page === 'app' || route.page === 'map' ? route.id : DEFAULT_APP;

  return (
    <ToolClientContext.Provider value={fixtureClient}>
      <HostContext.Provider value={host}>
        <div className={s.bar}>
          <div className={s.accent} />
          <div className={s.barInner}>
            <div className={s.brand}>
              <span className={s.mark} aria-hidden>P</span>
              <span className={s.name}>Product</span>
              <span className={s.harnessTag}>Local harness, fictional data</span>
            </div>
            <TabList
              className={s.tabs}
              selectedValue={route.page}
              size={narrow ? 'small' : 'medium'}
              aria-label="Pages"
              onTabSelect={(_, d) => {
                const page = d.value as Route['page'];
                const next: Route = page === 'app' || page === 'map' ? { page, id: appId } : page === 'timeline' ? { page } : { page: 'overview' };
                window.location.hash = routeHref(next);
              }}
            >
              {tabs.map(t => (
                <Tab key={t.value} value={t.value}>{narrow ? t.short : t.label}</Tab>
              ))}
            </TabList>
          </div>
        </div>
        <main className={s.main}>
          {route.page === 'overview' && <Overview />}
          {route.page === 'app' && <Application360 key={route.id} applicationId={route.id} />}
          {route.page === 'map' && <DependencyMap key={route.id} applicationId={route.id} />}
          {route.page === 'timeline' && <Timeline />}
        </main>
        <Toaster toasterId={toasterId} position="bottom-end" />
      </HostContext.Provider>
    </ToolClientContext.Provider>
  );
}
