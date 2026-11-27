// The web app's shell: sign-in, the top bar (the product name, the global search, the demo persona switch, the
// signed-in person) with the page menu below it (My work carries its count), the page area and a footer. It is the host
// of the four original pages (Host in ../harness.tsx): today in the viewer's zone, path routes under /app/, drill-downs
// to the applications list, and hand-offs to an AI client. Those pages are the same components the harness renders;
// only the tool client differs (the REST API here, fixture JSON there). The work pages (D-36) run only here: they read
// and write through the same tool client, and a write reads the page's data again in place (RefreshContext).
// The shell also shares whoami (SessionContext, data/session.ts) and what the install is (InstallContext, install.ts).
import { ChevronDown, CircleAlert, LogOut, RefreshCw, TriangleAlert, UserRound } from 'lucide-react';
import { Popover as PopoverPrimitive } from 'radix-ui';
import { useCallback, useEffect, useId, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import type { DevAuth } from '../auth/devAuth';
import type { MsalAuth } from '../auth/msalAuth';
import { offeredPersonas } from './config';
import type { Answer, ApiClient, Me, ServiceInfo } from '../data/apiClient';
import { todayInZone, zoneLabel } from '../data/format';
import { cachingClient, RefreshContext, ToolClientContext, type ToolClient } from '../data/toolClient';
import { WRITE_TOOLS } from '../data/contracts';
import { HostContext, type ApplicationFilter, type HandOff, type Host, type Route } from '../harness';
import { Agents } from '../pages/Agents';
import { AiActivity } from '../pages/AiActivity';
import { Application360 } from '../pages/Application360';
import { Approvals } from '../pages/Approvals';
import { Audit, EvidencePack } from '../pages/Audit';
import { ChangeNew, ChangeRecord, Changes } from '../pages/Changes';
import { DecisionRecord } from '../pages/Decision';
import { DecisionLog } from '../pages/DecisionLog';
import { Demand } from '../pages/Demand';
import { DemoGuide } from '../pages/DemoGuide';
import { DependencyMap } from '../pages/DependencyMap';
import { DeploymentNew, Deployments } from '../pages/Deployments';
import { Documents } from '../pages/Documents';
import { GapCheck, GapCheckRunRecord } from '../pages/GapCheck';
import { IntakeEdit, IntakeList, IntakeNew, IntakeRecord } from '../pages/Intake';
import { Overview } from '../pages/Overview';
import { Owners, PersonPage } from '../pages/Owners';
import { PlacePage, Places } from '../pages/Places';
import { PortfolioPage, Portfolios } from '../pages/Portfolios';
import { ClaimRecord, DeploymentRecord, DocumentLinkRecord } from '../pages/Records';
import { Retirement } from '../pages/Retirement';
import { ReviewRecord, Reviews } from '../pages/Reviews';
import { SearchPage } from '../pages/Search';
import { Timeline } from '../pages/Timeline';
import { MyWork } from '../pages/Work';
// ---- wave 3: one folder per page group ----
import { AdminPage, GuidelinesPage, MeasuresPage, RolesPage } from '../pages/admin';
import { ConfigurationDraftRecord, ConfigurationPage } from '../pages/configuration';
import { DataPage } from '../pages/data';
import { DataDomainRecord, DataLocationRecord, DataMappingRecord, DataUseRecord } from '../pages/data-records';
import { ApplicationsList } from '../pages/register-extras';
// ---- wave 4: one folder per page group ----
import { TeamPage, TeamsPage } from '../pages/teams';
import { StepUpContext } from '../components/actions';
import { useNarrow } from '../components/blocks';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Avatar, AvatarFallback } from '../components/ui/avatar';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '../components/ui/tooltip';
import { cn } from '../lib/utils';
import { NotFound, OtherRecord } from './AppPages';
import { DemoPersonaMenu } from './DemoPersonaMenu';
import { devPersonaSwitch, type DemoPersonaSwitch } from './demoPersona';
import { AppDialog, type DialogRequest } from './dialogs';
import { InstallContext, type Install } from './install';
import { navigate, useLinkInterception, useLocationKey } from './navigation';
import { appHref, parseAppPath, routeTitle, type AppRoute } from './routes';
import { DevSignIn, EntraSignIn } from './SignIn';
import { MainMenu, SessionScope, TopBarSearch } from './TopBar';

export type AppAuth = MsalAuth | DevAuth;

// Page width and gutters shared by the bar, the banners, the page area and the footer.
const frame = 'mx-auto max-w-[1280px] px-6 narrow:px-4';
const cardLine = 'text-xs text-navy-3 [overflow-wrap:anywhere]';
const cardHead = 'm-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

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

// The chosen demo persona's name ('' for none), so the shell renders again when it changes.
function useDemoPersonaName(personas: DemoPersonaSwitch | undefined): string {
  const subscribe = useCallback((callback: () => void) => personas?.subscribe(callback) ?? (() => {}), [personas]);
  const read = () => personas?.current()?.name ?? '';
  return useSyncExternalStore(subscribe, read, read);
}

function signedIn(auth: AppAuth): boolean {
  return auth.mode === 'development' ? auth.persona() !== null : auth.account() !== null;
}

export function AppShell({ auth, api, base, demo = false, demoPersonas }: {
  auth: AppAuth;
  api: ApiClient;
  base: string;
  demo?: boolean;
  // The demo persona switch (D-39). Shown only when the install lists demo personas.
  demoPersonas?: DemoPersonaSwitch;
}) {
  useAuthVersion(auth);
  useLinkInterception(base);
  const persona = useDemoPersonaName(demoPersonas);
  if (!signedIn(auth)) {
    return auth.mode === 'development' ? <DevSignIn auth={auth} /> : <EntraSignIn auth={auth} />;
  }
  // A new persona is a new person to the product: every panel, dialog and remembered choice starts again.
  const switcher = demoPersonas?.available ? demoPersonas : undefined;
  return <SignedIn auth={auth} api={api} base={base} demo={demo} personas={switcher} key={`${auth.user()?.username ?? 'user'}|${persona}`} />;
}

function SignedIn({ auth, api, base, demo, personas }: { auth: AppAuth; api: ApiClient; base: string; demo: boolean; personas: DemoPersonaSwitch | undefined }) {
  const narrow = useNarrow();
  const locationKey = useLocationKey();
  const route = useMemo<AppRoute>(() => {
    const url = new URL(locationKey, window.location.origin);
    return parseAppPath(url.pathname, url.search, base);
  }, [locationKey, base]);
  // generation: the refresh button. Every page mounts again and reads everything again, the session too.
  // dataVersion: a write that succeeded. Every panel reads again in place and keeps its state (an open sheet stays).
  const [generation, setGeneration] = useState(0);
  const [dataVersion, setDataVersion] = useState(0);
  const refresh = useCallback(() => setDataVersion(v => v + 1), []);
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

  // Keyboard focus must not land under the sticky top bar (WCAG 2.4.11). Its height changes with the width (the bar
  // wraps on a phone), so the page's scroll padding follows it.
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

  // The application the App 360 and map entries open: the one in the address, or the last one opened.
  const [lastApp, setLastApp] = useState<string | null>(null);
  const appId = route.page === 'app' || route.page === 'map' ? route.id : lastApp;
  useEffect(() => {
    if (route.page === 'app' || route.page === 'map') setLastApp(route.id);
  }, [route]);

  // The page title, and focus on the new page for keyboard and screen reader users (not on the first load).
  useEffect(() => {
    // The Demo guide exists only in a demo install; anywhere else its address is a page not found, and says so.
    const shown: AppRoute = route.page === 'demo-guide' && !demo ? { page: 'not-found' } : route;
    document.title = `${routeTitle(shown)} | Enterprise Applications Governance`;
    const previous = shownLocation.current;
    shownLocation.current = locationKey;
    // A change of the query string alone (a list's search, filters or view) is not a new page: the focus stays where
    // the person is typing or choosing.
    const pathOf = (key: string) => key.split('?')[0];
    if (previous !== null && pathOf(previous) !== pathOf(locationKey)) mainRef.current?.focus({ preventScroll: true });
  }, [route, locationKey, demo]);

  useEffect(() => {
    let live = true;
    void api.me().then(answer => { if (live) setMe(answer); });
    void api.info().then(answer => { if (live) setInfo(answer); });
    return () => { live = false; };
  }, [api, generation]);

  // A new client object per refresh or write makes every panel read again.
  // Reads are shared and kept for a few seconds within it (cachingClient, fix round 1: one page sent 60 calls).
  const client = useMemo<ToolClient>(() => cachingClient({ call: (tool, input) => api.call(tool, input) }, tool => (WRITE_TOOLS as readonly string[]).includes(tool)), [api, generation, dataVersion]);

  const host = useMemo<Host>(() => ({
    today: todayInZone(),
    href: (r: Route) => appHref(r, base),
    drill: (what: string, filter?: ApplicationFilter) => {
      if (filter) navigate(appHref({ page: 'applications', filter }, base));
      else openDialog({ kind: 'drill', what });
    },
    handOff: (request: HandOff) => openDialog({ kind: 'hand-off', request }),
    open: (r: AppRoute) => navigate(appHref(r, base)),
    showRecordLinks: false,
  }), [base, generation, openDialog]);

  // On a local run with the development sign-in, the Demo guide switches the development identity (wave 4).
  const devPersonas = useMemo(() => (auth.mode === 'development' ? devPersonaSwitch(auth) : null), [auth]);
  const install = useMemo<Install>(() => ({ demo, personas: personas ?? null, devPersonas }), [demo, personas, devPersonas]);
  const href = (r: AppRoute) => appHref(r, base);

  return (
    <ToolClientContext.Provider value={client}>
      <StepUpContext.Provider value={auth.stepUp}>
      <RefreshContext.Provider value={refresh}>
        <HostContext.Provider value={host}>
        <InstallContext.Provider value={install}>
        <SessionScope>
          <a
            className="absolute -top-12 left-2 z-20 rounded-md border-2 border-primary bg-white px-3 py-2 text-sm font-medium text-navy focus:top-2"
            href="#main"
            onClick={e => { e.preventDefault(); mainRef.current?.focus({ preventScroll: true }); }}
          >
            Skip to content
          </a>
          <header className="sticky top-0 z-10 border-b border-line bg-card shadow-xs" ref={headerRef}>
            <div className="h-[3px] bg-primary" />
            <div className={cn(frame, 'flex min-h-[52px] items-center gap-x-4 gap-y-1 narrow:min-h-12 narrow:flex-wrap narrow:gap-x-2 narrow:py-1')}>
              <a
                className="flex min-w-0 items-center gap-x-2.5 rounded-md text-navy no-underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring narrow:flex-1 narrow:basis-0"
                href={href({ page: 'work' })}
                aria-label="Enterprise Applications Governance, my work"
              >
                <span
                  className="relative grid size-7 shrink-0 place-items-center overflow-hidden rounded-[7px] bg-navy text-[15px] font-bold text-white after:absolute after:right-0 after:bottom-0 after:size-[9px] after:bg-gold"
                  aria-hidden
                >
                  E
                </span>
                <span className="truncate text-base font-semibold text-navy narrow:text-sm">Enterprise Applications Governance</span>
              </a>
              {/* On a phone the persona switch carries the Demo mark, so the data badge gives it the room. */}
              {demo && !(narrow && personas) && (
                <Badge variant="warning" className="shrink-0 rounded-full px-2 font-medium">
                  {narrow ? 'Demo data' : 'Demo, fictional data'}
                </Badge>
              )}
              {auth.mode === 'development' && (
                <Badge variant="warning" className="shrink-0 rounded-full px-2 font-medium">
                  {narrow ? 'Development sign-in' : 'Development sign-in, fictional persona'}
                </Badge>
              )}
              <div className="ml-auto flex shrink-0 items-center gap-x-1.5">
                <TopBarSearch base={base} route={route} />
                {personas && <DemoPersonaMenu personas={personas} me={me} narrow={narrow} />}
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
            <MainMenu route={route} base={base} openApplicationId={appId} demo={demo} frameClass={frame} />
          </header>
          <AuthBanner auth={auth} />
          <MeBanner me={me} />
          <main className={cn(frame, 'pt-6 pb-8 outline-none narrow:pt-4 narrow:pb-6')} id="main" ref={mainRef} tabIndex={-1}>
            <Page route={route} generation={generation} demo={demo} />
          </main>
          <Footer info={info} />
          <AppDialog request={dialog} onClose={closeDialog} />
        </SessionScope>
        </InstallContext.Provider>
        </HostContext.Provider>
      </RefreshContext.Provider>
      </StepUpContext.Provider>
    </ToolClientContext.Provider>
  );
}

// The page of a route. Every page mounts again on the refresh button (generation). A list page keeps its state while
// its view or filters change in the address; a record page mounts again for another record. The Demo guide exists only
// in a demo install: anywhere else its address is a page not found.
function Page({ route, generation, demo }: { route: AppRoute; generation: number; demo: boolean }) {
  const g = generation;
  switch (route.page) {
    case 'work':
      return <MyWork key={g} />;
    case 'search':
      return <SearchPage key={g} route={route} />;
    case 'overview':
      return <Overview key={g} />;
    case 'app':
      return <Application360 key={`${route.id}-${g}`} applicationId={route.id} />;
    case 'map':
      return <DependencyMap key={`${route.id}-${g}`} applicationId={route.id} />;
    case 'timeline':
      return <Timeline key={g} />;
    case 'applications':
      return <ApplicationsList key={`applications-${g}`} filter={route.filter} />;
    case 'approvals':
      return <Approvals key={g} route={route} />;
    case 'decision':
      return <DecisionRecord key={`${route.id}-${g}`} decisionId={route.id} />;
    case 'intake':
      return <IntakeList key={g} route={route} />;
    case 'intake-new':
      return <IntakeNew key={g} />;
    case 'intake-edit':
      return <IntakeEdit key={`${route.id}-${g}`} intakeRequestId={route.id} />;
    case 'intake-request':
      return <IntakeRecord key={`${route.id}-${g}`} intakeRequestId={route.id} />;
    case 'owners':
      return <Owners key={g} route={route} />;
    case 'person':
      return <PersonPage key={`${route.objectId}-${g}`} objectId={route.objectId} />;
    case 'reviews':
      return <Reviews key={g} route={route} />;
    case 'review':
      return <ReviewRecord key={`${route.id}-${g}`} reviewId={route.id} />;
    case 'changes':
      return <Changes key={g} route={route} />;
    case 'change-new':
      return <ChangeNew key={g} route={route} />;
    case 'change-request':
      return <ChangeRecord key={`${route.id}-${g}`} changeRequestId={route.id} />;
    case 'deployment-record':
      return <DeploymentRecord key={`${route.id}-${g}`} deploymentRecordId={route.id} />;
    case 'document-link':
      return <DocumentLinkRecord key={`${route.id}-${g}`} documentLinkId={route.id} />;
    case 'claim':
      return <ClaimRecord key={`${route.id}-${g}`} claimId={route.id} />;
    // ---- wave 2 ----
    case 'demand':
      return <Demand key={g} route={route} />;
    case 'deployments':
      return <Deployments key={g} route={route} />;
    case 'deployment-new':
      return <DeploymentNew key={g} route={route} />;
    case 'places':
      return <Places key={g} route={route} />;
    case 'place':
      return <PlacePage key={`${route.id}-${g}`} deploymentTargetId={route.id} />;
    case 'gap-check':
      return <GapCheck key={g} route={route} />;
    case 'gap-check-run':
      return <GapCheckRunRecord key={`${route.id}-${g}`} gapCheckRunId={route.id} />;
    case 'ai-activity':
      return <AiActivity key={g} route={route} />;
    case 'agents':
      return <Agents key={g} route={route} />;
    case 'audit':
      return <Audit key={g} route={route} />;
    case 'evidence':
      return <EvidencePack key={`${route.subjectKind}-${route.id}-${g}`} subjectKind={route.subjectKind} subjectId={route.id} />;
    case 'decisions':
      return <DecisionLog key={g} route={route} />;
    case 'documents':
      return <Documents key={g} route={route} />;
    case 'portfolios':
      return <Portfolios key={g} />;
    case 'portfolio':
      return <PortfolioPage key={`${route.id}-${g}`} portfolioId={route.id} />;
    case 'retirement':
      return <Retirement key={`${route.applicationId ?? 'all'}-${g}`} route={route} />;
    case 'demo-guide':
      return demo ? <DemoGuide key={g} /> : <NotFound />;
    // ---- wave 3 ----
    case 'data':
      return <DataPage key={g} route={route} />;
    case 'data-domain':
      return <DataDomainRecord key={`${route.key}-${g}`} domainKey={route.key} />;
    case 'data-location':
      return <DataLocationRecord key={`${route.id}-${g}`} dataLocationId={route.id} />;
    case 'data-mapping':
      return <DataMappingRecord key={`${route.id}-${g}`} dataMappingId={route.id} />;
    case 'data-use':
      return <DataUseRecord key={`${route.id}-${g}`} dataUseId={route.id} />;
    case 'configuration':
      return <ConfigurationPage key={g} route={route} />;
    case 'configuration-draft':
      return <ConfigurationDraftRecord key={`${route.id}-${g}`} configurationDraftId={route.id} />;
    case 'roles':
      return <RolesPage key={g} route={route} />;
    case 'guidelines':
      return <GuidelinesPage key={g} route={route} />;
    case 'measures':
      return <MeasuresPage key={g} />;
    case 'admin':
      return <AdminPage key={g} />;
    // ---- wave 4 ----
    case 'teams':
      return <TeamsPage key={g} route={route} />;
    case 'team':
      return <TeamPage key={`${route.id}-${g}`} teamId={route.id} />;
    case 'record':
      return <OtherRecord entity={route.entity} id={route.id} />;
    default:
      return <NotFound />;
  }
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
            {me?.ok && me.value.demoPersona && (
              <span className={cardLine}>
                Demo: the operator acts as {me.value.demoPersona.label}. Every change is stamped with the persona and "demo operator".
              </span>
            )}
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
                    {offeredPersonas(auth.personas, window.location.search, auth.persona()?.name ?? null).map(p => <option key={p.name} value={p.name}>{p.label}</option>)}
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
  const parts: string[] = ['The engine checks every change, the same way for a person and an AI client.', `Times in ${zoneLabel()}.`];
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
