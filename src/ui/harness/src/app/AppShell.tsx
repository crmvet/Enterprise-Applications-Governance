// The web app's shell: sign-in, the top bar with the page tabs and the signed-in person, the page area and a footer.
// It is the host of the four pages (Host in ../harness.tsx): today in the viewer's zone, path routes under /app/,
// drill-downs to the applications list, and hand-offs to an AI client. The pages are the same components the harness
// renders; only the tool client differs (the REST API here, fixture JSON there).
import { ChevronDown, CircleAlert, LogOut, RefreshCw, TriangleAlert, UserRound } from 'lucide-react';
import { Popover as PopoverPrimitive } from 'radix-ui';
import { useCallback, useEffect, useId, useMemo, useRef, useState, useSyncExternalStore } from 'react';
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
import { useNarrow } from '../components/blocks';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Avatar, AvatarFallback } from '../components/ui/avatar';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '../components/ui/tooltip';
import { cn } from '../lib/utils';
import { ApplicationsList, NotFound, OtherRecord } from './AppPages';
import { AppDialog, type DialogRequest } from './dialogs';
import { navigate, useLinkInterception, useLocationKey } from './navigation';
import { appHref, parseAppPath, routeTitle, type AppRoute } from './routes';
import { DevSignIn, EntraSignIn } from './SignIn';

export type AppAuth = MsalAuth | DevAuth;

// Page width and gutters shared by the bar, the banners, the page area and the footer.
const frame = 'mx-auto max-w-[1280px] px-6 narrow:px-4';
const cardLine = 'text-xs text-navy-3 [overflow-wrap:anywhere]';
const cardHead = 'm-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

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

// Up to two initials for the avatar: the first letters of the first and last words of the name (or of the part of a
// sign-in name before the @). A name with a note after a comma ("Nadia Ferreira, business owner") uses the part before
// it; "Ferreira, Nadia" keeps both parts.
function initialsOf(name: string): string {
  let base = name.includes('@') && !name.trim().includes(' ') ? name.slice(0, name.indexOf('@')) : name;
  const beforeComma = base.split(',')[0].trim();
  if (beforeComma.split(/\s+/).length >= 2) base = beforeComma;
  const words = base.split(/[\s._-]+/).filter(w => /^[\p{L}\p{N}]/u.test(w));
  if (!words.length) return '';
  const first = Array.from(words[0])[0];
  const last = words.length > 1 ? Array.from(words[words.length - 1])[0] : '';
  return `${first}${last}`.toUpperCase();
}

function useAuthVersion(auth: AppAuth): number {
  const version = useRef(0);
  const subscribe = useCallback((callback: () => void) => auth.subscribe(() => { version.current += 1; callback(); }), [auth]);
  return useSyncExternalStore(subscribe, () => version.current, () => version.current);
}

function signedIn(auth: AppAuth): boolean {
  return auth.mode === 'development' ? auth.persona() !== null : auth.account() !== null;
}

export function AppShell({ auth, api, base, demo = false }: { auth: AppAuth; api: ApiClient; base: string; demo?: boolean }) {
  useAuthVersion(auth);
  useLinkInterception(base);
  if (!signedIn(auth)) {
    return auth.mode === 'development' ? <DevSignIn auth={auth} /> : <EntraSignIn auth={auth} />;
  }
  return <SignedIn auth={auth} api={api} base={base} demo={demo} key={auth.user()?.username ?? 'user'} />;
}

const tabs: { value: Route['page']; label: string; short: string }[] = [
  { value: 'overview', label: 'Overview', short: 'Overview' },
  { value: 'app', label: 'Application 360', short: 'App 360' },
  { value: 'map', label: 'Dependency map', short: 'Map' },
  { value: 'timeline', label: 'Timeline', short: 'Timeline' },
];

function SignedIn({ auth, api, base, demo }: { auth: AppAuth; api: ApiClient; base: string; demo: boolean }) {
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
    document.title = `${routeTitle(route)} | Enterprise Applications Governance`;
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
  // Each tab is a real link to its page. A plain click becomes an in-app navigation (useLinkInterception); a modified
  // click opens the page in a new tab. The App 360 and map tabs show only once an application is open.
  const tabHref = (page: Route['page']): string | null => {
    const next: Route | null = page === 'app' || page === 'map' ? (appId ? { page, id: appId } : null) : page === 'timeline' ? { page } : { page: 'overview' };
    return next ? appHref(next, base) : null;
  };

  return (
    <ToolClientContext.Provider value={client}>
      <HostContext.Provider value={host}>
        <a
          className="absolute -top-12 left-2 z-20 rounded-md border-2 border-primary bg-white px-3 py-2 text-sm font-medium text-navy focus:top-2"
          href="#main"
          onClick={e => { e.preventDefault(); mainRef.current?.focus({ preventScroll: true }); }}
        >
          Skip to content
        </a>
        <header className="sticky top-0 z-10 border-b border-line bg-card shadow-xs" ref={headerRef}>
          <div className="h-[3px] bg-primary" />
          <div className={cn(frame, 'flex min-h-[52px] items-center gap-x-5 narrow:min-h-0 narrow:flex-wrap narrow:gap-x-3 narrow:gap-y-1 narrow:pt-2')}>
            <a
              className="flex min-w-0 items-center gap-x-2.5 rounded-md text-navy no-underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              href={appHref({ page: 'overview' }, base)}
              aria-label="Enterprise Applications Governance, portfolio overview"
            >
              <span
                className="relative grid size-7 shrink-0 place-items-center overflow-hidden rounded-[7px] bg-navy text-[15px] font-bold text-white after:absolute after:right-0 after:bottom-0 after:size-[9px] after:bg-gold"
                aria-hidden
              >
                E
              </span>
              <span className="truncate text-base font-semibold text-navy narrow:text-sm">Enterprise Applications Governance</span>
            </a>
            {demo && (
              <Badge variant="warning" className="shrink-0 rounded-full px-2 font-medium">
                {narrow ? 'Demo data' : 'Demo, fictional data'}
              </Badge>
            )}
            {auth.mode === 'development' && (
              <Badge variant="warning" className="shrink-0 rounded-full px-2 font-medium">
                {narrow ? 'Development sign-in' : 'Development sign-in, fictional persona'}
              </Badge>
            )}
            <nav className="ml-auto narrow:order-3 narrow:-ml-2.5 narrow:w-[calc(100%+20px)]" aria-label="Main">
              <ul className="m-0 flex list-none gap-1 p-0 narrow:gap-0" aria-label="Pages">
                {shownTabs.map(t => {
                  const href = tabHref(t.value);
                  if (!href) return null;
                  const current = selectedTab === t.value;
                  return (
                    <li key={t.value}>
                      <a className={tabClass(current)} href={href} aria-current={current ? 'page' : undefined}>
                        <TabText text={narrow ? t.short : t.label} />
                      </a>
                    </li>
                  );
                })}
              </ul>
            </nav>
            <div className="flex items-center gap-x-1 narrow:ml-auto">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    className="text-navy-2 hover:text-navy"
                    aria-label="Read the data again"
                    onClick={() => setGeneration(g => g + 1)}
                  >
                    <RefreshCw aria-hidden />
                  </Button>
                </TooltipTrigger>
                <TooltipContent side="bottom">Read the data again</TooltipContent>
              </Tooltip>
              <UserCard auth={auth} me={me} narrow={narrow} />
            </div>
          </div>
        </header>
        <AuthBanner auth={auth} />
        <MeBanner me={me} />
        <main className={cn(frame, 'pt-6 pb-8 outline-none narrow:pt-4 narrow:pb-6')} id="main" ref={mainRef} tabIndex={-1}>
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
  const personaId = useId();
  const user = auth.user();
  const name = (me?.ok ? me.value.name : null) ?? user?.name ?? user?.username ?? 'Signed in';
  const roles = me?.ok ? me.value.roles : [];
  const initials = initialsOf(name);
  // The popover is modal (Radix): it traps focus and hides the rest of the page from screen readers while it is open.
  // Signing out or switching the persona replaces the page, so the popover closes first and the action runs after.
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
    <PopoverPrimitive.Root open={open} onOpenChange={setOpen} modal>
      <PopoverPrimitive.Trigger asChild>
        <Button
          variant="ghost"
          size={narrow ? 'icon-sm' : 'sm'}
          className={cn('text-navy hover:text-navy', !narrow && 'max-w-[260px] pr-2.5 pl-1')}
          aria-label={`Signed in as ${name}. Account and roles`}
        >
          <Avatar size="sm" aria-hidden>
            <AvatarFallback className="bg-secondary text-[11px] font-semibold text-brand-dark ring-1 ring-info-line ring-inset">
              {initials || <UserRound className="size-3.5" />}
            </AvatarFallback>
          </Avatar>
          {narrow ? null : <span className="truncate">{name}</span>}
        </Button>
      </PopoverPrimitive.Trigger>
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          aria-label="Account and roles"
          side="bottom"
          align="end"
          sideOffset={6}
          collisionPadding={8}
          className="z-50 w-80 max-w-[calc(100vw-16px)] origin-(--radix-popover-content-transform-origin) rounded-xl border bg-popover p-4 text-popover-foreground shadow-lg outline-none data-[side=bottom]:slide-in-from-top-2 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
        >
          <div className="flex flex-col gap-2.5 text-navy">
            <span className="text-base font-semibold [overflow-wrap:anywhere]">{name}</span>
            {user?.username && user.username !== name && <span className={cn(cardLine, '-mt-1.5')}>{user.username}</span>}
            {me?.ok && actorText(me.value.actorKind) && <span className={cardLine}>{actorText(me.value.actorKind)}</span>}
            <div className="my-0.5 h-px bg-border" />
            <h2 className={cardHead}>Product roles</h2>
            {me === null ? (
              <span className={cardLine}>Reading your roles.</span>
            ) : !me.ok ? (
              <span className={cardLine}>{me.failure.error.message}</span>
            ) : roles.length ? (
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                {roles.map(r => (
                  <li key={`${r.roleKey}-${r.viaGroup?.objectId ?? ''}`} className="flex flex-col text-sm">
                    <span className="font-medium">{r.label}</span>
                    {r.viaGroup && <span className={cardLine}>through the group {r.viaGroup.displayLabel || r.viaGroup.objectId}</span>}
                  </li>
                ))}
              </ul>
            ) : (
              <span className={cardLine}>No product role.</span>
            )}
            {auth.mode === 'development' && (
              <>
                <label htmlFor={personaId} className={cardHead}>Development persona</label>
                {/* A native select, as before: the persona changes as soon as one is chosen. */}
                <div className="relative">
                  <select
                    id={personaId}
                    className="h-9 w-full cursor-pointer appearance-none rounded-md border border-input bg-card py-1 pr-9 pl-3 text-sm text-navy shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
                    value={auth.persona()?.name ?? ''}
                    onChange={e => { const persona = e.target.value; afterClose(() => auth.choose(persona)); }}
                  >
                    {auth.personas.map(p => <option key={p.name} value={p.name}>{p.label}</option>)}
                  </select>
                  <ChevronDown className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
                </div>
              </>
            )}
            <Button variant="outline" className="mt-1" onClick={() => afterClose(() => { void auth.signOut(); })}>
              <LogOut aria-hidden />
              Sign out
            </Button>
          </div>
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}

// A sign-in problem while signed in, such as a token the API keeps refusing: say so, and offer a fresh sign-in.
function AuthBanner({ auth }: { auth: AppAuth }) {
  const problem = auth.problem();
  if (!problem) return null;
  return (
    <div className={cn(frame, 'mt-4')} role="region" aria-label="Sign-in notice">
      <Alert variant="warning">
        <TriangleAlert aria-hidden />
        <AlertTitle>Sign-in needs another try</AlertTitle>
        <AlertDescription>
          <p>{problem}</p>
        </AlertDescription>
        {auth.mode === 'entra' && (
          <div className="col-start-2 mt-2">
            <Button variant="outline" size="sm" onClick={() => { void auth.signIn(); }}>Sign in again</Button>
          </div>
        )}
      </Alert>
    </div>
  );
}

function MeBanner({ me }: { me: Answer<Me> | null }) {
  if (!me) return null;
  if (me.ok && me.value.roles.length) return null;
  const code = me.ok ? null : me.failure.error.code;
  if (code === 'not_signed_in') return null;
  const failed = !!code && code !== 'not_authorized';
  return (
    <div className={cn(frame, 'mt-4')} role="region" aria-label="Session notice">
      <Alert variant={failed ? 'error' : 'warning'}>
        {failed ? <CircleAlert aria-hidden /> : <TriangleAlert aria-hidden />}
        <AlertTitle>{failed ? 'Your session could not be read' : 'You hold no product role'}</AlertTitle>
        <AlertDescription>
          <p>
            {failed
              ? `${me.ok ? '' : me.failure.error.message} Reference ${me.ok ? '' : me.failure.correlationId}.`
              : 'You are signed in, but no group of yours grants a product role, so the pages show nothing. Ask your platform admin; roles come only from the groups your organization assigns.'}
          </p>
        </AlertDescription>
      </Alert>
    </div>
  );
}

function Footer({ info }: { info: Answer<ServiceInfo> | null }) {
  const parts: string[] = ['Read-only pages.', `Times in ${zoneLabel()}.`];
  if (info?.ok) {
    if (info.value.engineVersion) parts.push(`Engine ${info.value.engineVersion}.`);
    parts.push(info.value.configuration ? `Configuration ${info.value.configuration.configurationSetKey}, version ${info.value.configuration.versionNumber}.` : 'No configuration in force yet.');
  }
  return (
    <footer className={cn(frame, 'pb-8 text-xs leading-relaxed text-navy-3 narrow:pb-6')}>
      {parts.join(' ')}
      {info?.ok && info.value.mcpEndpoint && <> AI clients connect to the MCP endpoint at <code className="font-mono [overflow-wrap:anywhere]">{info.value.mcpEndpoint}</code>.</>}
    </footer>
  );
}
