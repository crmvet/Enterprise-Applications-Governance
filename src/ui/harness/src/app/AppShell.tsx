// The web app's shell: sign-in, the top bar with the page tabs and the signed-in person, the page area and a footer.
// It is the host of the four pages (Host in ../harness.tsx): today in the viewer's zone, path routes under /app/,
// drill-downs to the applications list, and hand-offs to an AI client. The pages are the same components the harness
// renders; only the tool client differs (the REST API here, fixture JSON there).
import {
  Avatar,
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Select,
  Tab,
  TabList,
  makeStyles,
  tokens,
  useId,
} from '@fluentui/react-components';
import { ArrowClockwise16Regular, SignOut20Regular } from '@fluentui/react-icons';
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import type { DevAuth } from '../auth/devAuth';
import type { MsalAuth } from '../auth/msalAuth';
import type { Answer, ApiClient, Me, ServiceInfo } from '../data/apiClient';
import { todayInZone, zoneLabel } from '../data/format';
import { ToolClientContext, type ToolClient } from '../data/toolClient';
import { HostContext, type ApplicationFilter, type HandOff, type Host, type Route } from '../harness';
import { Application360 } from '../pages/Application360';
import { DependencyMap } from '../pages/DependencyMap';
import { Overview } from '../pages/Overview';
import { Timeline } from '../pages/Timeline';
import { media, useNarrow } from '../components/ui';
import { palette } from '../theme';
import { ApplicationsList, NotFound, OtherRecord } from './AppPages';
import { AppDialog, type DialogRequest } from './dialogs';
import { navigate, useLinkInterception, useLocationKey } from './navigation';
import { appHref, parseAppPath, routeTitle, type AppRoute } from './routes';
import { DevSignIn, EntraSignIn } from './SignIn';

export type AppAuth = MsalAuth | DevAuth;

const useStyles = makeStyles({
  skip: {
    position: 'absolute',
    left: '8px',
    top: '-48px',
    zIndex: 20,
    padding: '8px 12px',
    backgroundColor: palette.white,
    color: palette.navy,
    border: `2px solid ${palette.blue}`,
    borderRadius: '6px',
    ':focus': { top: '8px' },
  },
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
    columnGap: '20px',
    minHeight: '52px',
    [media.narrow]: { padding: '8px 16px 0', flexWrap: 'wrap', rowGap: '4px', columnGap: '12px', minHeight: 'auto' },
  },
  brand: { display: 'flex', alignItems: 'center', columnGap: '10px', flexShrink: 0, color: palette.navy, textDecoration: 'none' },
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
  devTag: {
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.goldDark,
    backgroundColor: palette.lightGold,
    border: '1px solid #f3dfa6',
    borderRadius: '10px',
    padding: '1px 8px',
    whiteSpace: 'nowrap',
  },
  tabs: {
    marginLeft: 'auto',
    [media.narrow]: { order: 3, marginLeft: '-10px', width: 'calc(100% + 20px)' },
  },
  user: { display: 'flex', alignItems: 'center', columnGap: '4px', [media.narrow]: { marginLeft: 'auto' } },
  main: {
    maxWidth: '1280px',
    margin: '0 auto',
    padding: '24px 24px 32px',
    outline: 'none',
    [media.narrow]: { padding: '16px 16px 24px' },
  },
  banner: { maxWidth: '1280px', margin: '16px auto 0', padding: '0 24px', [media.narrow]: { padding: '0 16px' } },
  footer: {
    maxWidth: '1280px',
    margin: '0 auto',
    padding: '0 24px 32px',
    fontSize: tokens.fontSizeBase200,
    color: palette.navy3,
    [media.narrow]: { padding: '0 16px 24px' },
  },
  code: { fontFamily: tokens.fontFamilyMonospace, overflowWrap: 'anywhere' },
  card: { display: 'flex', flexDirection: 'column', rowGap: '10px', maxWidth: '320px', color: palette.navy },
  cardName: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase400 },
  cardLine: { fontSize: tokens.fontSizeBase200, color: palette.navy3, overflowWrap: 'anywhere' },
  cardHead: { margin: '4px 0 0', fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold, color: palette.navy3, textTransform: 'uppercase', letterSpacing: '0.04em' },
  roles: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', rowGap: '6px' },
  role: { display: 'flex', flexDirection: 'column', fontSize: tokens.fontSizeBase300 },
});

function useAuthVersion(auth: AppAuth): number {
  const version = useRef(0);
  const subscribe = useCallback((callback: () => void) => auth.subscribe(() => { version.current += 1; callback(); }), [auth]);
  return useSyncExternalStore(subscribe, () => version.current, () => version.current);
}

function signedIn(auth: AppAuth): boolean {
  return auth.mode === 'development' ? auth.persona() !== null : auth.account() !== null;
}

export function AppShell({ auth, api, base }: { auth: AppAuth; api: ApiClient; base: string }) {
  useAuthVersion(auth);
  useLinkInterception(base);
  if (!signedIn(auth)) {
    return auth.mode === 'development' ? <DevSignIn auth={auth} /> : <EntraSignIn auth={auth} />;
  }
  return <SignedIn auth={auth} api={api} base={base} key={auth.user()?.username ?? 'user'} />;
}

const tabs: { value: Route['page']; label: string; short: string }[] = [
  { value: 'overview', label: 'Overview', short: 'Overview' },
  { value: 'app', label: 'Application 360', short: 'App 360' },
  { value: 'map', label: 'Dependency map', short: 'Map' },
  { value: 'timeline', label: 'Timeline', short: 'Timeline' },
];

function SignedIn({ auth, api, base }: { auth: AppAuth; api: ApiClient; base: string }) {
  const s = useStyles();
  const narrow = useNarrow();
  const locationKey = useLocationKey();
  const route = useMemo<AppRoute>(() => {
    const url = new URL(locationKey, window.location.origin);
    return parseAppPath(url.pathname, url.search, base);
  }, [locationKey, base]);
  const [generation, setGeneration] = useState(0);
  const [dialog, setDialog] = useState<DialogRequest | null>(null);
  // The control that opened the dialog. The dialog has no trigger of its own, so focus goes back here when it closes.
  const dialogOpener = useRef<HTMLElement | null>(null);
  const openDialog = useCallback((request: DialogRequest) => {
    dialogOpener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setDialog(request);
  }, []);
  const closeDialog = useCallback(() => {
    setDialog(null);
    const opener = dialogOpener.current;
    dialogOpener.current = null;
    if (opener) window.requestAnimationFrame(() => { if (opener.isConnected) opener.focus(); });
  }, []);
  const [me, setMe] = useState<Answer<Me> | null>(null);
  const [info, setInfo] = useState<Answer<ServiceInfo> | null>(null);
  const mainRef = useRef<HTMLElement>(null);
  const headerRef = useRef<HTMLElement>(null);
  const shownLocation = useRef<string | null>(null);

  // Keyboard focus must not land under the sticky top bar (WCAG 2.4.11). Its height changes with the width (the tabs
  // wrap to a second row on a phone), so the page's scroll padding follows it.
  useEffect(() => {
    const header = headerRef.current;
    if (!header || typeof ResizeObserver === 'undefined') return;
    const root = document.documentElement;
    const update = () => { root.style.scrollPaddingTop = `${Math.ceil(header.getBoundingClientRect().height) + 8}px`; };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(header);
    return () => {
      observer.disconnect();
      root.style.scrollPaddingTop = '';
    };
  }, []);

  // The application the App 360 and map tabs open: the one in the address, or the last one opened.
  const [lastApp, setLastApp] = useState<string | null>(null);
  const appId = route.page === 'app' || route.page === 'map' ? route.id : lastApp;
  useEffect(() => {
    if (route.page === 'app' || route.page === 'map') setLastApp(route.id);
  }, [route]);

  // The page title, and focus on the new page for keyboard and screen reader users (not on the first load).
  useEffect(() => {
    document.title = `${routeTitle(route)} | Product`;
    const previous = shownLocation.current;
    shownLocation.current = locationKey;
    if (previous !== null && previous !== locationKey) mainRef.current?.focus({ preventScroll: true });
  }, [route, locationKey]);

  useEffect(() => {
    let live = true;
    void api.me().then(answer => { if (live) setMe(answer); });
    void api.info().then(answer => { if (live) setInfo(answer); });
    return () => { live = false; };
  }, [api, generation]);

  // A new client object per refresh makes every panel read again.
  const client = useMemo<ToolClient>(() => ({ call: (tool, input) => api.call(tool, input) }), [api, generation]);

  const host = useMemo<Host>(() => ({
    today: todayInZone(),
    href: (r: Route) => appHref(r, base),
    drill: (what: string, filter?: ApplicationFilter) => {
      if (filter) navigate(appHref({ page: 'applications', filter }, base));
      else openDialog({ kind: 'drill', what });
    },
    handOff: (request: HandOff) => openDialog({ kind: 'hand-off', request }),
    showRecordLinks: false,
  }), [base, generation, openDialog]);

  const selectedTab = route.page === 'overview' || route.page === 'timeline' || route.page === 'app' || route.page === 'map' ? route.page : null;
  const shownTabs = tabs.filter(t => appId || (t.value !== 'app' && t.value !== 'map'));

  return (
    <ToolClientContext.Provider value={client}>
      <HostContext.Provider value={host}>
        <a className={s.skip} href="#main" onClick={e => { e.preventDefault(); mainRef.current?.focus({ preventScroll: true }); }}>Skip to content</a>
        <header className={s.bar} ref={headerRef}>
          <div className={s.accent} />
          <div className={s.barInner}>
            <a className={s.brand} href={appHref({ page: 'overview' }, base)} aria-label="Product, portfolio overview">
              <span className={s.mark} aria-hidden>P</span>
              <span className={s.name}>Product</span>
            </a>
            {auth.mode === 'development' && <span className={s.devTag}>{narrow ? 'Development sign-in' : 'Development sign-in, fictional persona'}</span>}
            <nav className={s.tabs} aria-label="Main">
              <TabList
                selectedValue={selectedTab}
                size={narrow ? 'small' : 'medium'}
                aria-label="Pages"
                onTabSelect={(_, d) => {
                  const page = d.value as Route['page'];
                  const next: Route | null = page === 'app' || page === 'map' ? (appId ? { page, id: appId } : null) : page === 'timeline' ? { page } : { page: 'overview' };
                  if (next) navigate(appHref(next, base));
                }}
              >
                {shownTabs.map(t => <Tab key={t.value} value={t.value}>{narrow ? t.short : t.label}</Tab>)}
              </TabList>
            </nav>
            <div className={s.user}>
              <Button
                appearance="subtle"
                icon={<ArrowClockwise16Regular />}
                aria-label="Read the data again"
                title="Read the data again"
                onClick={() => setGeneration(g => g + 1)}
              />
              <UserCard auth={auth} me={me} narrow={narrow} />
            </div>
          </div>
        </header>
        <AuthBanner auth={auth} />
        <MeBanner me={me} />
        <main className={s.main} id="main" ref={mainRef} tabIndex={-1}>
          {route.page === 'overview' && <Overview key={generation} />}
          {route.page === 'app' && <Application360 key={`${route.id}-${generation}`} applicationId={route.id} />}
          {route.page === 'map' && <DependencyMap key={`${route.id}-${generation}`} applicationId={route.id} />}
          {route.page === 'timeline' && <Timeline key={generation} />}
          {route.page === 'applications' && <ApplicationsList key={`${locationKey}-${generation}`} filter={route.filter} />}
          {route.page === 'record' && <OtherRecord entity={route.entity} id={route.id} />}
          {route.page === 'not-found' && <NotFound />}
        </main>
        <Footer info={info} />
        <AppDialog request={dialog} onClose={closeDialog} />
      </HostContext.Provider>
    </ToolClientContext.Provider>
  );
}

function actorText(kind: string | null): string | null {
  if (kind === 'person') return 'You act as yourself: the engine stamps your changes as a person.';
  if (kind === 'ai-client-for-person') return 'The server treats this page as an AI client acting for you. Reads are the same.';
  return null;
}

function UserCard({ auth, me, narrow }: { auth: AppAuth; me: Answer<Me> | null; narrow: boolean }) {
  const s = useStyles();
  const personaId = useId('persona');
  const user = auth.user();
  const name = (me?.ok ? me.value.name : null) ?? user?.name ?? user?.username ?? 'Signed in';
  const roles = me?.ok ? me.value.roles : [];
  // The popover traps focus and hides the rest of the page from screen readers while it is open. Signing out or
  // switching the persona replaces the page, so the popover closes first: otherwise the page stays hidden.
  const [open, setOpen] = useState(false);
  const pending = useRef<(() => void) | null>(null);
  useEffect(() => {
    if (open || !pending.current) return;
    const action = pending.current;
    pending.current = null;
    // After the popover has gone and its focus trap has given the page back.
    const timer = window.setTimeout(action, 50);
    return () => window.clearTimeout(timer);
  }, [open]);
  const afterClose = (action: () => void) => {
    pending.current = action;
    setOpen(false);
  };
  return (
    <Popover open={open} onOpenChange={(_, d) => setOpen(d.open)} trapFocus positioning="below-end" withArrow>
      <PopoverTrigger disableButtonEnhancement>
        <Button appearance="subtle" icon={<Avatar size={24} name={name} color="colorful" aria-hidden />} aria-label={`Signed in as ${name}. Account and roles`}>
          {narrow ? null : name}
        </Button>
      </PopoverTrigger>
      <PopoverSurface aria-label="Account and roles">
        <div className={s.card}>
          <span className={s.cardName}>{name}</span>
          {user?.username && user.username !== name && <span className={s.cardLine}>{user.username}</span>}
          {me?.ok && actorText(me.value.actorKind) && <span className={s.cardLine}>{actorText(me.value.actorKind)}</span>}
          <h2 className={s.cardHead}>Product roles</h2>
          {me === null ? (
            <span className={s.cardLine}>Reading your roles.</span>
          ) : !me.ok ? (
            <span className={s.cardLine}>{me.failure.error.message}</span>
          ) : roles.length ? (
            <ul className={s.roles}>
              {roles.map(r => (
                <li key={`${r.roleKey}-${r.viaGroup?.objectId ?? ''}`} className={s.role}>
                  <span>{r.label}</span>
                  {r.viaGroup && <span className={s.cardLine}>through the group {r.viaGroup.displayLabel || r.viaGroup.objectId}</span>}
                </li>
              ))}
            </ul>
          ) : (
            <span className={s.cardLine}>No product role.</span>
          )}
          {auth.mode === 'development' && (
            <>
              <label htmlFor={personaId} className={s.cardHead}>Development persona</label>
              <Select id={personaId} value={auth.persona()?.name ?? ''} onChange={(_, d) => { const persona = d.value; afterClose(() => auth.choose(persona)); }}>
                {auth.personas.map(p => <option key={p.name} value={p.name}>{p.label}</option>)}
              </Select>
            </>
          )}
          <Button appearance="secondary" icon={<SignOut20Regular />} onClick={() => afterClose(() => { void auth.signOut(); })}>
            Sign out
          </Button>
        </div>
      </PopoverSurface>
    </Popover>
  );
}

// A sign-in problem while signed in, such as a token the API keeps refusing: say so, and offer a fresh sign-in.
function AuthBanner({ auth }: { auth: AppAuth }) {
  const s = useStyles();
  const problem = auth.problem();
  if (!problem) return null;
  return (
    <div className={s.banner} role="region" aria-label="Sign-in notice">
      <MessageBar intent="warning" layout="multiline">
        <MessageBarBody>
          <MessageBarTitle>Sign-in needs another try</MessageBarTitle>
          {problem}
        </MessageBarBody>
        {auth.mode === 'entra' && (
          <MessageBarActions>
            <Button appearance="secondary" onClick={() => { void auth.signIn(); }}>Sign in again</Button>
          </MessageBarActions>
        )}
      </MessageBar>
    </div>
  );
}

function MeBanner({ me }: { me: Answer<Me> | null }) {
  const s = useStyles();
  if (!me) return null;
  if (me.ok && me.value.roles.length) return null;
  const code = me.ok ? null : me.failure.error.code;
  if (code === 'not_signed_in') return null;
  return (
    <div className={s.banner} role="region" aria-label="Session notice">
      <MessageBar intent={code && code !== 'not_authorized' ? 'error' : 'warning'} layout="multiline">
        <MessageBarBody>
          <MessageBarTitle>{code && code !== 'not_authorized' ? 'Your session could not be read' : 'You hold no product role'}</MessageBarTitle>
          {code && code !== 'not_authorized'
            ? `${me.ok ? '' : me.failure.error.message} Reference ${me.ok ? '' : me.failure.correlationId}.`
            : 'You are signed in, but no group of yours grants a product role, so the pages show nothing. Ask your platform admin; roles come only from the groups your organization assigns.'}
        </MessageBarBody>
      </MessageBar>
    </div>
  );
}

function Footer({ info }: { info: Answer<ServiceInfo> | null }) {
  const s = useStyles();
  const parts: string[] = ['Read-only pages.', `Times in ${zoneLabel()}.`];
  if (info?.ok) {
    if (info.value.engineVersion) parts.push(`Engine ${info.value.engineVersion}.`);
    parts.push(info.value.configuration ? `Configuration ${info.value.configuration.configurationSetKey}, version ${info.value.configuration.versionNumber}.` : 'No configuration in force yet.');
  }
  return (
    <footer className={s.footer}>
      {parts.join(' ')}
      {info?.ok && info.value.mcpEndpoint && <> AI clients connect to the MCP endpoint at <code className={s.code}>{info.value.mcpEndpoint}</code>.</>}
    </footer>
  );
}
