// The web app's addresses, under its base path (/app/):
//   /app/                                            My work, the home for signed-in people (also /app/work)
//   /app/overview                                    the portfolio overview
//   /app/timeline                                    the timeline
//   /app/search?q=<text>                             the global search: applications, people, record numbers
//   /app/applications?stage=<key>&tier=<key>&kind=<key>&portfolio=<id>&owner=<objectId>&registration=<meaning>
//                     &ai=yes|no&build=<key>&q=<text>  the applications register (ai and build: wave 4, find_applications
//                                                    usesAi and buildPathKeys; ai=unknown and build=not-stated stay the
//                                                    register's own parameters, because the tool cannot filter on them)
//   /app/approvals?view=mine|asked-by-me|open|answered|rules&app=<id>&portfolio=<id>   approval requests (portfolio:
//                                                    wave 4b, list_approvals portfolioId)
//   /app/intake?view=triage|demand|mine|drafts|all&q=<text>                        intake and demand
//   /app/intake/new                                  the intake wizard, a new request
//   /app/demand?portfolio=<id>                       the PMO's demand board: scored demand, priorities waiting
//   /app/owners?view=by-person|by-role|gaps|leavers&app=<id>&portfolio=<id>       owners and people
//   /app/people/<objectId>                           one person: what they own, what they are asked
//   /app/reviews?state=overdue|due|done|cancelled|all&type=<key>&app=<id>&portfolio=<id>   reviews
//   /app/changes?view=schedule|list&app=<id>          changes
//   /app/changes/new?app=<id>                        a new change request
//   /app/deployments?view=all|flagged&flag=<key>&app=<id>&change=<id>&target=<id>  deployments and their flags
//   /app/deployments/new?app=<id>&change=<id>        record a deployment by hand (path C, D-19)
//   /app/places?view=targets|pipelines&stage=<key>   deployment targets and pipelines
//   /app/places/<targetId>                           one deployment target (record link: deployment-target)
//   /app/gap-check?view=open|claimed|dismissed|vanished|runs                        the gap check and its claims
//   /app/ai-activity?view=<view>&app=<id>&portfolio=<id>&mine=1   what AI clients did and who decided (also /app/ai;
//                                                    portfolio: wave 4b, get_ai_activity portfolioId)
//   /app/agents?view=register|flagged|autonomy       the agent register
//   /app/audit?record=<id>&person=<objectId>&app=<id>&actor=<kind>&client=<name>   the audit trail (client: wave 4,
//                                                    get_audit_trail clientApp, the name the engine stamped)
//   /app/evidence/<kind>/<id>                        the evidence pack of a business-application or a decision
//                                                    (also /app/records/<kind>/<id>/evidence)
//   /app/decisions?view=log|exceptions&type=<key>&status=<meaning>&app=<id>&portfolio=<id>   the decision log
//   /app/documents?state=all|missing|stale|draft|current&type=<key>&app=<id>&portfolio=<id>&mine=1   documents
//   /app/portfolios                                  the portfolios
//   /app/portfolios/<id>                             one portfolio and its review pack (record link: portfolio)
//   /app/retirement                                  applications on their way out
//   /app/retirement/<applicationId>                  one application's retirement tracker
//                                                    (also /app/records/business-application/<id>/retire)
//   /app/demo-guide                                  the demo story, step by step (Demo installs only; also /app/demo)
// ---- wave 3 (data taxonomy D-37, the Govern pages) ----
//   /app/data?domain=<key>&app=<id>&location=<id>&mine=1             the Data page, Domains tab (also /app/data/domains)
//   /app/data/locations|mapping|uses|gaps?domain=&app=&location=&mine=1   its other tabs (docs/architecture/data-taxonomy.md, Screens)
//   /app/configuration?view=rules|presets|versions|drafts             the rules in force and the configuration change flow
//   /app/roles?role=<key>                            roles and access: groups, holders, grants, tools, decision rights
//   /app/guidelines?g=<G17>                          the guidelines and the playbook (a chip opens its guideline here)
//   /app/measures                                    the measures and the slide-back signals
//   /app/admin                                       run the product: settings, health, outbox, reminders, clients
// ---- wave 4 (teams and committees) ----
//   /app/teams?kind=committee|support-team|role-group|other&q=<text>&role=<roleKey>   the teams and committees
//   /app/teams/<objectId>                            one team: an Entra group the install knows, by its object ID
//                                                    (record link: team, /app/records/team/<objectId> opens it)
//   Text search on a list page that has no q= in its route reads q= itself (components/listSearch.tsx useListText).
//   /app/records/<entity>/<id>                       a record. This is the record link the tools return (links.recordUrl
//                                                    and recordUrl), so a link from an AI client, a card or an email
//                                                    opens the page. Entities with their own page:
//                                                      business-application (Application 360), decision,
//                                                      intake-request, review, change-request, deployment-record,
//                                                      document-link, claim, gap-check-run, deployment-target (opens
//                                                      /app/places/<id>), portfolio (opens /app/portfolios/<id>),
//                                                      configuration-draft, data-location, data-mapping, data-use,
//                                                      team (opens /app/teams/<objectId>)
//                                                    Any other entity opens a plain "not shown here" panel.
//   /app/records/data-domain/<key>                   a data domain. Its record link carries the domain key (C49), not a
//                                                    row ID: a domain is a configuration row (data taxonomy, 10.4)
//   /app/records/business-application/<id>/map       the dependency map of that application
//   /app/records/intake-request/<id>/edit            the intake wizard on an existing request (an AI draft, say)
// The server answers every /app/ path that is not a file with index.html, and the page reads the path.
import type { ApplicationFilter, Route } from '../harness';

export const APPROVALS_VIEWS = ['mine', 'asked-by-me', 'open', 'answered', 'rules'] as const;
export const INTAKE_VIEWS = ['triage', 'demand', 'mine', 'drafts', 'all'] as const;
export const OWNERS_VIEWS = ['by-person', 'by-role', 'gaps', 'leavers'] as const;
export const REVIEW_STATES = ['overdue', 'due', 'done', 'cancelled', 'all'] as const;
export const CHANGES_VIEWS = ['schedule', 'list'] as const;
export const DEPLOYMENTS_VIEWS = ['all', 'flagged'] as const;
// The four deployment flags, as get_deployments takes them in flagKeys.
export const DEPLOYMENT_FLAG_KEYS = ['outside-window', 'other-path', 'missing-approval', 'ai-only-code-approval'] as const;
export const PLACES_VIEWS = ['targets', 'pipelines'] as const;
// Claims by state (list_claims statusMeanings), vanished items (findingKinds vanished), and the run history.
export const GAP_CHECK_VIEWS = ['open', 'claimed', 'dismissed', 'vanished', 'runs'] as const;
// The views get_ai_activity takes.
export const AI_ACTIVITY_VIEWS = ['all', 'drafts', 'waiting', 'accepted', 'withdrawn', 'refused', 'answers', 'code-approvals'] as const;
export const AGENTS_VIEWS = ['register', 'flagged', 'autonomy'] as const;
export const DECISIONS_VIEWS = ['log', 'exceptions'] as const;
export const DECISION_STATUS_MEANINGS = ['proposed', 'accepted', 'rejected', 'superseded', 'withdrawn'] as const;
// The states list_documents takes.
export const DOCUMENT_STATES = ['all', 'missing', 'stale', 'draft', 'current'] as const;
// The actor kinds get_audit_trail takes (C38).
export const AUDIT_ACTOR_KINDS = ['person', 'ai-client-for-person', 'agent-identity', 'pipeline', 'approval-runner', 'engine'] as const;
// The subjects get_evidence packs: an application or a decision.
export const EVIDENCE_KINDS = ['business-application', 'decision'] as const;
// The registration states find_applications takes (registrationMeanings): the register's owner and registration filters.
export const REGISTRATION_MEANINGS = ['draft', 'registered', 'archived'] as const;
// The tabs of the Data page, each with an address of its own (docs/architecture/data-taxonomy.md, Screens). Domains is the default.
export const DATA_TABS = ['domains', 'locations', 'mapping', 'uses', 'gaps'] as const;
// The views of the Configuration page. The rules in force is the default.
export const CONFIGURATION_VIEWS = ['rules', 'presets', 'versions', 'drafts'] as const;
// ---- wave 4 ----
// The kinds of a team (list_teams kind, document 24 B24-20), in the order the Teams page lists them.
export const TEAM_KINDS = ['committee', 'support-team', 'role-group', 'other'] as const;
// The answers of the register's uses AI filter that the tool takes (find_applications usesAi). ai=unknown is the
// register's own parameter: an application with no answer matches neither true nor false.
export const USES_AI_ROUTE_VALUES = ['yes', 'no'] as const;
// The build path the register reads itself (no answer yet); never sent to the tool as a key.
export const BUILD_NOT_STATED_KEY = 'not-stated';
// A guideline number, such as G17 (C39 Guideline).
const GUIDELINE = /^G[1-9][0-9]{0,2}$/;

export type ApprovalsView = (typeof APPROVALS_VIEWS)[number];
export type IntakeView = (typeof INTAKE_VIEWS)[number];
export type OwnersView = (typeof OWNERS_VIEWS)[number];
export type ReviewState = (typeof REVIEW_STATES)[number];
export type ChangesView = (typeof CHANGES_VIEWS)[number];
export type DeploymentsView = (typeof DEPLOYMENTS_VIEWS)[number];
export type DeploymentFlagRouteKey = (typeof DEPLOYMENT_FLAG_KEYS)[number];
export type PlacesView = (typeof PLACES_VIEWS)[number];
export type GapCheckView = (typeof GAP_CHECK_VIEWS)[number];
export type AiActivityRouteView = (typeof AI_ACTIVITY_VIEWS)[number];
export type AgentsView = (typeof AGENTS_VIEWS)[number];
export type DecisionsView = (typeof DECISIONS_VIEWS)[number];
export type DecisionStatusRouteMeaning = (typeof DECISION_STATUS_MEANINGS)[number];
export type DocumentRouteState = (typeof DOCUMENT_STATES)[number];
export type AuditActorKind = (typeof AUDIT_ACTOR_KINDS)[number];
export type EvidenceKind = (typeof EVIDENCE_KINDS)[number];
export type DataTab = (typeof DATA_TABS)[number];
export type ConfigurationView = (typeof CONFIGURATION_VIEWS)[number];
export type TeamKindRoute = (typeof TEAM_KINDS)[number];

// The record entities with a page of their own, and the page each one opens.
export const RECORD_PAGES = {
  'business-application': 'app',
  decision: 'decision',
  'intake-request': 'intake-request',
  review: 'review',
  'change-request': 'change-request',
  'deployment-record': 'deployment-record',
  'document-link': 'document-link',
  claim: 'claim',
  'gap-check-run': 'gap-check-run',
  'deployment-target': 'place',
  portfolio: 'portfolio',
  // ---- wave 3 ----
  'configuration-draft': 'configuration-draft',
  'data-domain': 'data-domain',
  'data-location': 'data-location',
  'data-mapping': 'data-mapping',
  'data-use': 'data-use',
  // ---- wave 4 ----
  // A team's record link carries the group's Entra object ID (list_teams recordUrl) and opens /app/teams/<objectId>.
  team: 'team',
} as const;

// The record entities whose record link carries a configuration key instead of a row ID: a data domain is a C49 row,
// so its link is /app/records/data-domain/<key>.
export const KEYED_RECORD_PAGES = ['data-domain'] as const;

export type RecordEntity = keyof typeof RECORD_PAGES;
type KeyedPage = (typeof KEYED_RECORD_PAGES)[number];
type RecordPage = Exclude<(typeof RECORD_PAGES)[RecordEntity], 'app' | KeyedPage>;
// One member per record page, so RouteOf<'place'> is { page: 'place'; id: string }, and RouteOf<'data-domain'> is
// { page: 'data-domain'; key: string }.
type RecordRoute = { [P in RecordPage]: { page: P; id: string } }[RecordPage] | { page: 'data-domain'; key: string };

// The entity of each record page, for its address.
const PAGE_ENTITY = Object.fromEntries(Object.entries(RECORD_PAGES).map(([entity, page]) => [page, entity])) as Record<RecordPage | 'app' | KeyedPage, RecordEntity>;

// True when the web app has a page for records of this entity, by row ID or (a data domain) by key.
export function hasRecordPage(entity: string): entity is RecordEntity {
  return Object.prototype.hasOwnProperty.call(RECORD_PAGES, entity);
}

const isKeyedEntity = (entity: string): entity is KeyedPage => (KEYED_RECORD_PAGES as readonly string[]).includes(entity);

// Optional fields are left out when the address does not set them, so a parsed route compares equal to a built one.
export type AppRoute =
  | Route
  | { page: 'work' }
  | { page: 'search'; text?: string }
  | { page: 'applications'; filter: ApplicationFilter }
  // text and typeKey: the list's search (q=) and decision type (type=), so a filtered list can be linked (product review, fix round 2).
  // portfolioId (wave 4b review): the requests about one portfolio's applications, as the Overview's tile opens them.
  | { page: 'approvals'; view?: ApprovalsView; applicationId?: string; portfolioId?: string; text?: string; typeKey?: string }
  | { page: 'intake'; view?: IntakeView; text?: string }
  | { page: 'intake-new' }
  | { page: 'intake-edit'; id: string }
  | { page: 'demand'; portfolioId?: string }
  | { page: 'owners'; view?: OwnersView; applicationId?: string; portfolioId?: string }
  | { page: 'person'; objectId: string }
  | { page: 'reviews'; state?: ReviewState; typeKeys?: string[]; applicationId?: string; portfolioId?: string }
  | { page: 'changes'; view?: ChangesView; applicationId?: string }
  | { page: 'change-new'; applicationId?: string }
  | {
    page: 'deployments';
    view?: DeploymentsView;
    flagKeys?: DeploymentFlagRouteKey[];
    applicationId?: string;
    changeRequestId?: string;
    targetId?: string;
  }
  | { page: 'deployment-new'; applicationId?: string; changeRequestId?: string }
  | { page: 'places'; view?: PlacesView; stageKeys?: string[] }
  | { page: 'gap-check'; view?: GapCheckView }
  | { page: 'ai-activity'; view?: AiActivityRouteView; applicationId?: string; portfolioId?: string; mine?: true }
  | { page: 'agents'; view?: AgentsView }
  // clientApp (wave 4): only the entries made through this client app, by the name the engine stamped (client=).
  | { page: 'audit'; recordId?: string; principalObjectId?: string; applicationId?: string; actorKinds?: AuditActorKind[]; clientApp?: string }
  | { page: 'evidence'; subjectKind: EvidenceKind; id: string }
  | {
    page: 'decisions';
    view?: DecisionsView;
    typeKeys?: string[];
    statusMeanings?: DecisionStatusRouteMeaning[];
    applicationId?: string;
    portfolioId?: string;
  }
  | { page: 'documents'; state?: DocumentRouteState; typeKeys?: string[]; applicationId?: string; portfolioId?: string; mine?: true }
  | { page: 'portfolios' }
  | { page: 'retirement'; applicationId?: string }
  | { page: 'demo-guide' }
  // ---- wave 3 ----
  // The Data page. The tab is left out for Domains. domainKey, applicationId and locationId narrow the tab to one
  // domain, application or location; mine narrows it to the domains the person owns, deputizes or stewards.
  | { page: 'data'; tab?: Exclude<DataTab, 'domains'>; domainKey?: string; applicationId?: string; locationId?: string; mine?: true }
  | { page: 'configuration'; view?: Exclude<ConfigurationView, 'rules'> }
  | { page: 'roles'; roleKey?: string }
  | { page: 'guidelines'; guideline?: string }
  | { page: 'measures' }
  | { page: 'admin' }
  // ---- wave 4 ----
  // The teams and committees: kind narrows the list to one kind, text searches the team and role names (q=), roleKey
  // keeps the teams that hold one role (role=: the decision page's "waiting on" link). The team page is RecordRoute's
  // { page: 'team'; id } with the group's object ID.
  | { page: 'teams'; kind?: TeamKindRoute; text?: string; roleKey?: string }
  | RecordRoute
  | { page: 'record'; entity: string; id: string }
  | { page: 'not-found' };

export type AppPage = AppRoute['page'];

// The route of one page, for its props: RouteOf<'reviews'> is { page: 'reviews'; state?; typeKeys?; ... }.
export type RouteOf<P extends AppPage> = Extract<AppRoute, { page: P }>;

const ID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const ENTITY = /^[a-z][a-z0-9-]{0,63}$/;
const KEY = /^[a-z][a-z0-9-]{0,63}$/;
const APPLICATION = 'business-application';
const MAX_TEXT = 200;

// The base path with one leading and one trailing slash: "/app/".
export function normalizeBase(base: string): string {
  const trimmed = base.replace(/^\/+|\/+$/g, '');
  return trimmed ? `/${trimmed}/` : '/';
}

function keys(params: URLSearchParams, name: string): string[] | undefined {
  const list = params.getAll(name).filter(k => KEY.test(k));
  return list.length ? [...new Set(list)].slice(0, 20) : undefined;
}

function oneOf<T extends string>(params: URLSearchParams, name: string, allowed: readonly T[]): T | undefined {
  const value = params.get(name);
  return value !== null && (allowed as readonly string[]).includes(value) ? (value as T) : undefined;
}

function someOf<T extends string>(params: URLSearchParams, name: string, allowed: readonly T[]): T[] | undefined {
  const list = [...new Set(params.getAll(name))].filter((v): v is T => (allowed as readonly string[]).includes(v));
  return list.length ? list : undefined;
}

function idParam(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name);
  return value !== null && ID.test(value) ? value.toLowerCase() : undefined;
}

// One configuration key, such as a domain key or a role key.
function keyParam(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name);
  return value !== null && KEY.test(value) ? value : undefined;
}

// A guideline number in any case ("g17", "G17"), written as the product writes it: G17.
function guidelineParam(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name)?.toUpperCase() ?? null;
  return value !== null && GUIDELINE.test(value) ? value : undefined;
}

// The Data page's shared filters, for every tab.
function dataRoute(tab: DataTab, params: URLSearchParams): AppRoute {
  return defined({
    page: 'data',
    tab: tab === 'domains' ? undefined : tab,
    domainKey: keyParam(params, 'domain'),
    applicationId: idParam(params, 'app'),
    locationId: idParam(params, 'location'),
    mine: flagParam(params, 'mine'),
  });
}

function textParam(params: URLSearchParams, name: string): string | undefined {
  const value = (params.get(name) ?? '').trim().slice(0, MAX_TEXT);
  return value || undefined;
}

// A client app's name as the engine stamped it ("Claude Code"): plain text with no control characters, up to 200.
function clientParam(params: URLSearchParams, name: string): string | undefined {
  const value = textParam(params, name);
  return value !== undefined && !/[\u0000-\u001f\u007f]/.test(value) ? value : undefined;
}

// "mine=1" sets a flag; anything else leaves it out.
function flagParam(params: URLSearchParams, name: string): true | undefined {
  return params.get(name) === '1' ? true : undefined;
}

// Copies only the fields that are set, so a route never carries an undefined member.
function defined<T extends object>(route: T): T {
  return Object.fromEntries(Object.entries(route).filter(([, v]) => v !== undefined)) as T;
}

// The page a record link opens: its own page for the entities in RECORD_PAGES, else the plain record panel. A data
// domain is named by its key; every other record by its row ID.
export function recordRoute(entity: string, id: string): AppRoute {
  if (!ENTITY.test(entity)) return { page: 'not-found' };
  if (isKeyedEntity(entity)) return KEY.test(id) ? { page: entity, key: id } : { page: 'not-found' };
  if (!ID.test(id)) return { page: 'not-found' };
  const normalId = id.toLowerCase();
  const page = (RECORD_PAGES as Record<string, string>)[entity];
  if (page === undefined) return { page: 'record', entity, id: normalId };
  return { page, id: normalId } as AppRoute;
}

// The list pages: one path segment after the base, with their own query parameters.
function listRoute(name: string, params: URLSearchParams): AppRoute {
  switch (name) {
    case 'overview':
      return { page: 'overview' };
    case 'timeline':
      return { page: 'timeline' };
    case 'search':
      return defined({ page: 'search', text: textParam(params, 'q') });
    case 'applications': {
      const filter: ApplicationFilter = {};
      const stage = keys(params, 'stage');
      const tier = keys(params, 'tier');
      const kind = keys(params, 'kind');
      if (stage) filter.lifecycleStageKeys = stage;
      if (tier) filter.tierKeys = tier;
      if (kind) filter.applicationKindKeys = kind;
      const text = textParam(params, 'q');
      if (text) filter.text = text;
      const portfolios = [...new Set(params.getAll('portfolio').map(p => p.toLowerCase()).filter(p => ID.test(p)))].slice(0, 20);
      if (portfolios.length) filter.portfolioIds = portfolios;
      const owner = idParam(params, 'owner');
      if (owner) filter.ownerObjectId = owner;
      const registration = someOf(params, 'registration', REGISTRATION_MEANINGS);
      if (registration) filter.registrationMeanings = registration;
      // Wave 4: the two filters find_applications now takes. ai=unknown and build=not-stated are left to the register.
      const usesAi = oneOf(params, 'ai', USES_AI_ROUTE_VALUES);
      if (usesAi) filter.usesAi = usesAi === 'yes';
      const build = keys(params, 'build')?.filter(k => k !== BUILD_NOT_STATED_KEY);
      if (build?.length) filter.buildPathKeys = build;
      return { page: 'applications', filter };
    }
    case 'approvals':
      return defined({ page: 'approvals', view: oneOf(params, 'view', APPROVALS_VIEWS), applicationId: idParam(params, 'app'), portfolioId: idParam(params, 'portfolio'), text: textParam(params, 'q'), typeKey: keyParam(params, 'type') });
    case 'intake':
      return defined({ page: 'intake', view: oneOf(params, 'view', INTAKE_VIEWS), text: textParam(params, 'q') });
    case 'demand':
      return defined({ page: 'demand', portfolioId: idParam(params, 'portfolio') });
    case 'owners':
      return defined({
        page: 'owners',
        view: oneOf(params, 'view', OWNERS_VIEWS),
        applicationId: idParam(params, 'app'),
        portfolioId: idParam(params, 'portfolio'),
      });
    case 'reviews':
      return defined({
        page: 'reviews',
        state: oneOf(params, 'state', REVIEW_STATES),
        typeKeys: keys(params, 'type'),
        applicationId: idParam(params, 'app'),
        portfolioId: idParam(params, 'portfolio'),
      });
    case 'changes':
      return defined({ page: 'changes', view: oneOf(params, 'view', CHANGES_VIEWS), applicationId: idParam(params, 'app') });
    case 'deployments':
      return defined({
        page: 'deployments',
        view: oneOf(params, 'view', DEPLOYMENTS_VIEWS),
        flagKeys: someOf(params, 'flag', DEPLOYMENT_FLAG_KEYS),
        applicationId: idParam(params, 'app'),
        changeRequestId: idParam(params, 'change'),
        targetId: idParam(params, 'target'),
      });
    case 'places':
      return defined({ page: 'places', view: oneOf(params, 'view', PLACES_VIEWS), stageKeys: keys(params, 'stage') });
    case 'gap-check':
      return defined({ page: 'gap-check', view: oneOf(params, 'view', GAP_CHECK_VIEWS) });
    case 'ai':
    case 'ai-activity':
      return defined({
        page: 'ai-activity',
        view: oneOf(params, 'view', AI_ACTIVITY_VIEWS),
        applicationId: idParam(params, 'app'),
        portfolioId: idParam(params, 'portfolio'),
        mine: flagParam(params, 'mine'),
      });
    case 'agents':
      return defined({ page: 'agents', view: oneOf(params, 'view', AGENTS_VIEWS) });
    case 'audit':
      return defined({
        page: 'audit',
        recordId: idParam(params, 'record'),
        principalObjectId: idParam(params, 'person'),
        applicationId: idParam(params, 'app'),
        actorKinds: someOf(params, 'actor', AUDIT_ACTOR_KINDS),
        clientApp: clientParam(params, 'client'),
      });
    case 'decisions':
      return defined({
        page: 'decisions',
        view: oneOf(params, 'view', DECISIONS_VIEWS),
        typeKeys: keys(params, 'type'),
        statusMeanings: someOf(params, 'status', DECISION_STATUS_MEANINGS),
        applicationId: idParam(params, 'app'),
        portfolioId: idParam(params, 'portfolio'),
      });
    case 'documents':
      return defined({
        page: 'documents',
        state: oneOf(params, 'state', DOCUMENT_STATES),
        typeKeys: keys(params, 'type'),
        applicationId: idParam(params, 'app'),
        portfolioId: idParam(params, 'portfolio'),
        mine: flagParam(params, 'mine'),
      });
    case 'portfolios':
      return { page: 'portfolios' };
    case 'retirement':
      return { page: 'retirement' };
    case 'demo':
    case 'demo-guide':
      return { page: 'demo-guide' };
    // ---- wave 3 ----
    case 'data':
      return dataRoute('domains', params);
    case 'configuration': {
      const view = oneOf(params, 'view', CONFIGURATION_VIEWS);
      return defined({ page: 'configuration', view: view === 'rules' ? undefined : view });
    }
    case 'roles':
      return defined({ page: 'roles', roleKey: keyParam(params, 'role') });
    case 'guidelines':
      return defined({ page: 'guidelines', guideline: guidelineParam(params, 'g') });
    case 'measures':
      return { page: 'measures' };
    case 'admin':
      return { page: 'admin' };
    // ---- wave 4 ----
    case 'teams':
      return defined({ page: 'teams', kind: oneOf(params, 'kind', TEAM_KINDS), text: textParam(params, 'q'), roleKey: keyParam(params, 'role') });
    default:
      return { page: 'not-found' };
  }
}

// Reads a path and query string, such as "/app/records/business-application/<id>" and "?stage=build".
export function parseAppPath(pathname: string, search: string, base: string): AppRoute {
  const root = normalizeBase(base);
  const path = pathname === root.slice(0, -1) ? root : pathname;
  if (!path.startsWith(root)) return { page: 'not-found' };
  let parts: string[];
  try {
    parts = path.slice(root.length).split('/').filter(Boolean).map(decodeURIComponent);
  } catch {
    return { page: 'not-found' };
  }
  const params = new URLSearchParams(search);

  if (parts.length === 0 || (parts.length === 1 && parts[0] === 'work')) return { page: 'work' };
  if (parts.length === 1) return listRoute(parts[0], params);
  if (parts.length === 2 && parts[0] === 'data' && (DATA_TABS as readonly string[]).includes(parts[1])) return dataRoute(parts[1] as DataTab, params);
  if (parts.length === 2 && parts[1] === 'new') {
    if (parts[0] === 'intake') return { page: 'intake-new' };
    if (parts[0] === 'changes') return defined({ page: 'change-new', applicationId: idParam(params, 'app') });
    if (parts[0] === 'deployments') {
      return defined({ page: 'deployment-new', applicationId: idParam(params, 'app'), changeRequestId: idParam(params, 'change') });
    }
  }
  if (parts.length === 2 && ID.test(parts[1])) {
    const id = parts[1].toLowerCase();
    if (parts[0] === 'people') return { page: 'person', objectId: id };
    if (parts[0] === 'places') return { page: 'place', id };
    if (parts[0] === 'portfolios') return { page: 'portfolio', id };
    if (parts[0] === 'retirement') return { page: 'retirement', applicationId: id };
    if (parts[0] === 'teams') return { page: 'team', id };
  }
  if (parts.length === 3 && parts[0] === 'evidence') {
    const [, kind, id] = parts;
    if ((EVIDENCE_KINDS as readonly string[]).includes(kind) && ID.test(id)) return { page: 'evidence', subjectKind: kind as EvidenceKind, id: id.toLowerCase() };
  }
  if (parts[0] === 'records' && (parts.length === 3 || parts.length === 4)) {
    const [, entity, id, view] = parts;
    if (ENTITY.test(entity) && isKeyedEntity(entity)) return view === undefined ? recordRoute(entity, id) : { page: 'not-found' };
    if (!ENTITY.test(entity) || !ID.test(id)) return { page: 'not-found' };
    const normalId = id.toLowerCase();
    if (view === undefined) return recordRoute(entity, normalId);
    if (entity === APPLICATION && view === 'map') return { page: 'map', id: normalId };
    if (entity === APPLICATION && view === 'retire') return { page: 'retirement', applicationId: normalId };
    if (view === 'evidence' && (EVIDENCE_KINDS as readonly string[]).includes(entity)) return { page: 'evidence', subjectKind: entity as EvidenceKind, id: normalId };
    if (entity === 'intake-request' && view === 'edit') return { page: 'intake-edit', id: normalId };
  }
  return { page: 'not-found' };
}

function withQuery(path: string, params: URLSearchParams): string {
  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

// The query of a route: each entry once, a list entry once per value, empty entries left out.
function query(entries: [string, string | readonly string[] | undefined][]): URLSearchParams {
  const params = new URLSearchParams();
  for (const [name, value] of entries) {
    if (typeof value === 'string') {
      if (value) params.append(name, value);
    } else if (value) {
      for (const v of value) params.append(name, v);
    }
  }
  return params;
}

const flag = (on: true | undefined) => (on ? '1' : undefined);

// The address of a route, under the base path.
export function appHref(route: AppRoute, base: string): string {
  const root = normalizeBase(base);
  const record = (entity: string, id: string) => `${root}records/${encodeURIComponent(entity)}/${encodeURIComponent(id)}`;
  switch (route.page) {
    case 'work':
      return `${root}work`;
    case 'overview':
      return `${root}overview`;
    case 'timeline':
      return `${root}timeline`;
    case 'search':
      return withQuery(`${root}search`, query([['q', route.text]]));
    case 'app':
      return record(APPLICATION, route.id);
    case 'map':
      return `${record(APPLICATION, route.id)}/map`;
    case 'record':
      return record(route.entity, route.id);
    case 'place':
      return `${root}places/${encodeURIComponent(route.id)}`;
    case 'portfolio':
      return `${root}portfolios/${encodeURIComponent(route.id)}`;
    case 'team':
      return `${root}teams/${encodeURIComponent(route.id)}`;
    case 'decision':
    case 'intake-request':
    case 'review':
    case 'change-request':
    case 'deployment-record':
    case 'document-link':
    case 'claim':
    case 'gap-check-run':
    case 'configuration-draft':
    case 'data-location':
    case 'data-mapping':
    case 'data-use':
      return record(PAGE_ENTITY[route.page], route.id);
    case 'data-domain':
      return record('data-domain', route.key);
    case 'intake-edit':
      return `${record('intake-request', route.id)}/edit`;
    case 'applications': {
      const params = new URLSearchParams();
      for (const k of route.filter.lifecycleStageKeys ?? []) params.append('stage', k);
      for (const k of route.filter.tierKeys ?? []) params.append('tier', k);
      for (const k of route.filter.applicationKindKeys ?? []) params.append('kind', k);
      for (const p of route.filter.portfolioIds ?? []) params.append('portfolio', p);
      if (route.filter.ownerObjectId) params.set('owner', route.filter.ownerObjectId);
      for (const m of route.filter.registrationMeanings ?? []) params.append('registration', m);
      if (route.filter.usesAi !== undefined) params.set('ai', route.filter.usesAi ? 'yes' : 'no');
      for (const k of route.filter.buildPathKeys ?? []) params.append('build', k);
      if (route.filter.text) params.set('q', route.filter.text);
      return withQuery(`${root}applications`, params);
    }
    case 'approvals':
      return withQuery(`${root}approvals`, query([['view', route.view], ['app', route.applicationId], ['portfolio', route.portfolioId], ['q', route.text], ['type', route.typeKey]]));
    case 'intake':
      return withQuery(`${root}intake`, query([['view', route.view], ['q', route.text]]));
    case 'intake-new':
      return `${root}intake/new`;
    case 'demand':
      return withQuery(`${root}demand`, query([['portfolio', route.portfolioId]]));
    case 'owners':
      return withQuery(`${root}owners`, query([['view', route.view], ['app', route.applicationId], ['portfolio', route.portfolioId]]));
    case 'person':
      return `${root}people/${encodeURIComponent(route.objectId)}`;
    case 'reviews':
      return withQuery(`${root}reviews`, query([['state', route.state], ['type', route.typeKeys], ['app', route.applicationId], ['portfolio', route.portfolioId]]));
    case 'changes':
      return withQuery(`${root}changes`, query([['view', route.view], ['app', route.applicationId]]));
    case 'change-new':
      return withQuery(`${root}changes/new`, query([['app', route.applicationId]]));
    case 'deployments':
      return withQuery(`${root}deployments`, query([
        ['view', route.view],
        ['flag', route.flagKeys],
        ['app', route.applicationId],
        ['change', route.changeRequestId],
        ['target', route.targetId],
      ]));
    case 'deployment-new':
      return withQuery(`${root}deployments/new`, query([['app', route.applicationId], ['change', route.changeRequestId]]));
    case 'places':
      return withQuery(`${root}places`, query([['view', route.view], ['stage', route.stageKeys]]));
    case 'gap-check':
      return withQuery(`${root}gap-check`, query([['view', route.view]]));
    case 'ai-activity':
      return withQuery(`${root}ai-activity`, query([['view', route.view], ['app', route.applicationId], ['portfolio', route.portfolioId], ['mine', flag(route.mine)]]));
    case 'agents':
      return withQuery(`${root}agents`, query([['view', route.view]]));
    case 'audit':
      return withQuery(`${root}audit`, query([
        ['record', route.recordId],
        ['person', route.principalObjectId],
        ['app', route.applicationId],
        ['actor', route.actorKinds],
        ['client', route.clientApp],
      ]));
    case 'evidence':
      return `${root}evidence/${encodeURIComponent(route.subjectKind)}/${encodeURIComponent(route.id)}`;
    case 'decisions':
      return withQuery(`${root}decisions`, query([
        ['view', route.view],
        ['type', route.typeKeys],
        ['status', route.statusMeanings],
        ['app', route.applicationId],
        ['portfolio', route.portfolioId],
      ]));
    case 'documents':
      return withQuery(`${root}documents`, query([
        ['state', route.state],
        ['type', route.typeKeys],
        ['app', route.applicationId],
        ['portfolio', route.portfolioId],
        ['mine', flag(route.mine)],
      ]));
    case 'portfolios':
      return `${root}portfolios`;
    case 'retirement':
      return route.applicationId ? `${root}retirement/${encodeURIComponent(route.applicationId)}` : `${root}retirement`;
    case 'demo-guide':
      return `${root}demo-guide`;
    // ---- wave 3 ----
    case 'data':
      return withQuery(route.tab ? `${root}data/${route.tab}` : `${root}data`, query([
        ['domain', route.domainKey],
        ['app', route.applicationId],
        ['location', route.locationId],
        ['mine', flag(route.mine)],
      ]));
    case 'configuration':
      return withQuery(`${root}configuration`, query([['view', route.view]]));
    case 'roles':
      return withQuery(`${root}roles`, query([['role', route.roleKey]]));
    case 'guidelines':
      return withQuery(`${root}guidelines`, query([['g', route.guideline]]));
    case 'measures':
      return `${root}measures`;
    case 'admin':
      return `${root}admin`;
    // ---- wave 4 ----
    case 'teams':
      return withQuery(`${root}teams`, query([['kind', route.kind], ['q', route.text], ['role', route.roleKey]]));
    default:
      return root;
  }
}

// The in-app address (path and query) a link points to, or null when the link leaves the web app: another origin,
// a path outside the base, or a file such as config.json. Used to turn clicks on page links into in-app navigation.
export function internalTarget(href: string, pageOrigin: string, base: string): string | null {
  let url: URL;
  try {
    url = new URL(href, pageOrigin);
  } catch {
    return null;
  }
  const root = normalizeBase(base);
  if (url.origin !== pageOrigin) return null;
  if (url.pathname !== root.slice(0, -1) && !url.pathname.startsWith(root)) return null;
  if (/\.[a-z0-9]+$/i.test(url.pathname)) return null;
  return url.pathname + url.search;
}

// The in-app address of a record link a tool returned (recordUrl, links.recordUrl), on this page's own origin, or null
// when the link is not a record page of this web app. The product writes record links with its public address, which
// can differ from the page's own origin (a local dev loop, a second host name), so only the path is kept. A record
// whose page has an address of its own (a deployment target, a portfolio) gets that address.
export function recordPath(recordUrl: string | null | undefined, base: string): string | null {
  if (typeof recordUrl !== 'string' || !recordUrl) return null;
  let url: URL;
  try {
    url = new URL(recordUrl);
  } catch {
    return null;
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
  const root = normalizeBase(base);
  if (!url.pathname.startsWith(`${root}records/`)) return null;
  const route = parseAppPath(url.pathname, '', base);
  return route.page === 'not-found' ? null : appHref(route, base);
}

// The names of the Data page's tabs and the Configuration page's views, for titles and tab bars.
export const DATA_TAB_TITLES: Record<DataTab, string> = {
  domains: 'Data domains',
  locations: 'Data locations',
  mapping: 'Data mapping',
  uses: 'Data uses',
  gaps: 'Data gaps',
};

export const CONFIGURATION_VIEW_TITLES: Record<ConfigurationView, string> = {
  rules: 'Rules in force',
  presets: 'Presets and overlays',
  versions: 'Versions',
  drafts: 'Drafts',
};

// The names of the team kinds, for titles and the Teams page's tabs (plural: each tab lists teams of that kind).
export const TEAM_KIND_TITLES: Record<TeamKindRoute, string> = {
  committee: 'Committees',
  'support-team': 'Support teams',
  'role-group': 'Role groups',
  other: 'Other groups',
};

// A short page title for document.title.
export function routeTitle(route: AppRoute): string {
  switch (route.page) {
    case 'work':
      return 'My work';
    case 'overview':
      return 'Portfolio overview';
    case 'timeline':
      return 'Timeline';
    case 'search':
      return route.text ? `Search: ${route.text}` : 'Search';
    case 'app':
      return 'Application 360';
    case 'map':
      return 'Dependency map';
    case 'applications':
      return 'Applications';
    case 'approvals':
      return 'Approvals';
    case 'decision':
      return 'Decision';
    case 'intake':
      return 'Intake and demand';
    case 'intake-new':
      return 'New request';
    case 'intake-edit':
      return 'Edit request';
    case 'intake-request':
      return 'Intake request';
    case 'demand':
      return 'Demand';
    case 'owners':
      return 'Owners and people';
    case 'person':
      return 'Person';
    case 'reviews':
      return 'Reviews';
    case 'review':
      return 'Review';
    case 'changes':
      return 'Changes';
    case 'change-new':
      return 'New change';
    case 'change-request':
      return 'Change request';
    case 'deployments':
      return 'Deployments';
    case 'deployment-new':
      return 'Record a deployment';
    case 'deployment-record':
      return 'Deployment';
    case 'places':
      return 'Places';
    case 'place':
      return 'Deployment target';
    case 'gap-check':
      return 'Gap check';
    case 'gap-check-run':
      return 'Gap check run';
    case 'claim':
      return 'Claim';
    case 'ai-activity':
      return 'AI activity';
    case 'agents':
      return 'Agents';
    case 'audit':
      return 'Audit trail';
    case 'evidence':
      return 'Evidence pack';
    case 'decisions':
      return route.view === 'exceptions' ? 'Exceptions' : 'Decision log';
    case 'documents':
      return 'Documents';
    case 'document-link':
      return 'Document';
    case 'portfolios':
      return 'Portfolios';
    case 'portfolio':
      return 'Portfolio';
    case 'retirement':
      return route.applicationId ? 'Retirement tracker' : 'Retirement';
    case 'demo-guide':
      return 'Demo guide';
    // ---- wave 3 ----
    case 'data':
      return DATA_TAB_TITLES[route.tab ?? 'domains'];
    case 'data-domain':
      return 'Data domain';
    case 'data-location':
      return 'Data location';
    case 'data-mapping':
      return 'Data mapping';
    case 'data-use':
      return 'Data use';
    case 'configuration':
      return route.view ? `Configuration: ${CONFIGURATION_VIEW_TITLES[route.view]}` : 'Configuration';
    case 'configuration-draft':
      return 'Configuration draft';
    case 'roles':
      return 'Roles and access';
    case 'guidelines':
      return 'Guidelines and playbook';
    case 'measures':
      return 'Measures';
    case 'admin':
      return 'Admin';
    // ---- wave 4 ----
    case 'teams':
      return route.kind ? `Teams: ${TEAM_KIND_TITLES[route.kind]}` : 'Teams and committees';
    case 'team':
      return 'Team';
    case 'record':
      return 'Record';
    default:
      return 'Page not found';
  }
}
