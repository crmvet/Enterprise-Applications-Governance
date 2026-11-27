// The web app's top menu: which pages it lists, which sit in the bar and which under More, and which entry a page
// belongs to. Groups:
// Home, Work, Register, Record, AI, Govern, and in a demo install the Demo guide. Wave 3 adds Data under Register and
// the Govern group: Configuration, Roles and access, Guidelines, Measures and Admin. They sit under More on every width.
// Wave 4 adds Teams to the Work group, right after Owners.
//
// The bar keeps the pages people use every day: My work, Approvals and Applications on every screen; Intake, Changes
// and Reviews from 720 px; the Overview, Owners and Teams from 1024 px. Everything else sits under More. On a phone More
// opens a side sheet with every page and the search, so the bar fits one row at 360 px.
//
// A page whose tools the person's roles cannot call is left out (whoami tools, the Tool Policy): it is never shown and
// then refused. While whoami is still read, or when it cannot be read, every page shows and the engine decides.
import type { AppRoute } from './routes';

export type MenuKey =
  | 'work' | 'overview'
  | 'approvals' | 'intake' | 'demand' | 'changes' | 'deployments' | 'reviews' | 'documents' | 'owners' | 'teams' | 'gap-check' | 'retirement'
  | 'applications' | 'portfolios' | 'agents' | 'data' | 'places'
  | 'decisions' | 'timeline' | 'audit'
  | 'ai-activity'
  | 'configuration' | 'roles' | 'guidelines' | 'measures' | 'admin'
  | 'demo-guide'
  | 'app' | 'map';

export type MenuGroup = 'home' | 'work' | 'register' | 'record' | 'ai' | 'govern' | 'demo' | 'open-app';

export type ScreenWidth = 'narrow' | 'medium' | 'wide';

export interface MenuEntry {
  key: MenuKey;
  label: string;
  // The label in the bar on a phone.
  short: string;
  route: AppRoute;
  // The narrowest screen on which the entry sits in the bar: every screen (narrow), 720 px and up (medium), 1024 px
  // and up (wide), or never (always under More).
  bar: ScreenWidth | 'never';
  group: MenuGroup;
  // The page shows only to a person whose roles give them at least one of these tools.
  needsAnyTool?: readonly string[];
  // The page exists only in a demo install.
  demoOnly?: boolean;
}

export const GROUP_LABELS: Record<MenuGroup, string> = {
  home: 'Home',
  work: 'Work',
  register: 'Register',
  record: 'Record',
  ai: 'AI',
  govern: 'Govern',
  demo: 'Demo',
  'open-app': 'Open application',
};

export const GROUP_ORDER: readonly MenuGroup[] = ['home', 'work', 'register', 'record', 'ai', 'govern', 'demo', 'open-app'];

const ENTRIES: readonly MenuEntry[] = [
  { key: 'work', label: 'My work', short: 'My work', route: { page: 'work' }, bar: 'narrow', group: 'home' },
  { key: 'approvals', label: 'Approvals', short: 'Approvals', route: { page: 'approvals' }, bar: 'narrow', group: 'work' },
  { key: 'applications', label: 'Applications', short: 'Apps', route: { page: 'applications', filter: {} }, bar: 'narrow', group: 'register' },
  { key: 'overview', label: 'Overview', short: 'Overview', route: { page: 'overview' }, bar: 'wide', group: 'home' },
  { key: 'intake', label: 'Intake', short: 'Intake', route: { page: 'intake' }, bar: 'medium', group: 'work' },
  { key: 'changes', label: 'Changes', short: 'Changes', route: { page: 'changes' }, bar: 'medium', group: 'work' },
  { key: 'reviews', label: 'Reviews', short: 'Reviews', route: { page: 'reviews' }, bar: 'medium', group: 'work' },
  { key: 'owners', label: 'Owners', short: 'Owners', route: { page: 'owners' }, bar: 'wide', group: 'work' },
  // Wave 4: the teams and committees, next to Owners in the bar from 1024 px. Every product role may call list_teams
  // under the shipped Tool Policy; a Tool Policy that takes it away also takes the entry away.
  { key: 'teams', label: 'Teams', short: 'Teams', route: { page: 'teams' }, bar: 'wide', group: 'work', needsAnyTool: ['list_teams'] },
  { key: 'demand', label: 'Demand', short: 'Demand', route: { page: 'demand' }, bar: 'never', group: 'work' },
  { key: 'deployments', label: 'Deployments', short: 'Deployments', route: { page: 'deployments' }, bar: 'never', group: 'work' },
  { key: 'documents', label: 'Documents', short: 'Documents', route: { page: 'documents' }, bar: 'never', group: 'work' },
  { key: 'gap-check', label: 'Gap check', short: 'Gap check', route: { page: 'gap-check' }, bar: 'never', group: 'work', needsAnyTool: ['list_claims', 'list_gap_check_runs'] },
  { key: 'retirement', label: 'Retirement', short: 'Retirement', route: { page: 'retirement' }, bar: 'never', group: 'work' },
  { key: 'portfolios', label: 'Portfolios', short: 'Portfolios', route: { page: 'portfolios' }, bar: 'never', group: 'register' },
  { key: 'agents', label: 'Agents', short: 'Agents', route: { page: 'agents' }, bar: 'never', group: 'register' },
  { key: 'data', label: 'Data', short: 'Data', route: { page: 'data' }, bar: 'never', group: 'register', needsAnyTool: ['find_data_domains', 'get_data_gaps'] },
  { key: 'places', label: 'Places', short: 'Places', route: { page: 'places' }, bar: 'never', group: 'register' },
  { key: 'decisions', label: 'Decisions', short: 'Decisions', route: { page: 'decisions' }, bar: 'never', group: 'record' },
  { key: 'timeline', label: 'Timeline', short: 'Timeline', route: { page: 'timeline' }, bar: 'never', group: 'record' },
  { key: 'audit', label: 'Audit trail', short: 'Audit', route: { page: 'audit' }, bar: 'never', group: 'record', needsAnyTool: ['get_audit_trail'] },
  { key: 'ai-activity', label: 'AI activity', short: 'AI activity', route: { page: 'ai-activity' }, bar: 'never', group: 'ai' },
  { key: 'configuration', label: 'Configuration', short: 'Configuration', route: { page: 'configuration' }, bar: 'never', group: 'govern', needsAnyTool: ['list_configuration_changes', 'get_configuration'] },
  { key: 'roles', label: 'Roles and access', short: 'Roles', route: { page: 'roles' }, bar: 'never', group: 'govern', needsAnyTool: ['get_role_access'] },
  { key: 'guidelines', label: 'Guidelines', short: 'Guidelines', route: { page: 'guidelines' }, bar: 'never', group: 'govern', needsAnyTool: ['list_guidelines'] },
  { key: 'measures', label: 'Measures', short: 'Measures', route: { page: 'measures' }, bar: 'never', group: 'govern', needsAnyTool: ['get_measures'] },
  { key: 'admin', label: 'Admin', short: 'Admin', route: { page: 'admin' }, bar: 'never', group: 'govern', needsAnyTool: ['get_admin_status'] },
  { key: 'demo-guide', label: 'Demo guide', short: 'Demo guide', route: { page: 'demo-guide' }, bar: 'never', group: 'demo', demoOnly: true },
];

export interface MenuOptions {
  // A demo install: the Demo guide joins the menu.
  demo?: boolean;
  // The tools whoami gives the person, or null while it is read or when it cannot be read.
  tools?: ReadonlySet<string> | null;
}

// The menu, in order. The Application 360 and map entries show only once an application has been opened.
export function menuEntries(openApplicationId: string | null, options: MenuOptions = {}): MenuEntry[] {
  const tools = options.tools ?? null;
  const entries = ENTRIES.filter(e => (!e.demoOnly || options.demo === true) && (!e.needsAnyTool || tools === null || e.needsAnyTool.some(t => tools.has(t))));
  if (openApplicationId) {
    entries.push(
      { key: 'app', label: 'Application 360', short: 'App 360', route: { page: 'app', id: openApplicationId }, bar: 'never', group: 'open-app' },
      { key: 'map', label: 'Dependency map', short: 'Map', route: { page: 'map', id: openApplicationId }, bar: 'never', group: 'open-app' },
    );
  }
  return entries;
}

const WIDTH_RANK: Record<ScreenWidth | 'never', number> = { narrow: 0, medium: 1, wide: 2, never: 3 };

// The entries in the bar, and the ones under More, for a screen width.
export function splitMenu(entries: MenuEntry[], width: ScreenWidth): { bar: MenuEntry[]; more: MenuEntry[] } {
  const inBar = (e: MenuEntry) => WIDTH_RANK[e.bar] <= WIDTH_RANK[width];
  return { bar: entries.filter(inBar), more: entries.filter(e => !inBar(e)) };
}

// The entries of a list, grouped in menu order, groups with no entry left out.
export function groupEntries(entries: MenuEntry[]): { group: MenuGroup; label: string; items: MenuEntry[] }[] {
  return GROUP_ORDER.map(group => ({ group, label: GROUP_LABELS[group], items: entries.filter(e => e.group === group) })).filter(g => g.items.length > 0);
}

// The menu entry a page belongs to: a record page belongs to its list. Null for a page with no entry (the search, any
// other record, a page not found).
export function menuKeyOf(route: AppRoute): MenuKey | null {
  switch (route.page) {
    case 'work':
      return 'work';
    case 'approvals':
    case 'decision':
      return 'approvals';
    case 'applications':
      return 'applications';
    case 'overview':
      return 'overview';
    case 'intake':
    case 'intake-new':
    case 'intake-edit':
    case 'intake-request':
      return 'intake';
    case 'demand':
      return 'demand';
    case 'changes':
    case 'change-new':
    case 'change-request':
      return 'changes';
    case 'deployments':
    case 'deployment-new':
    case 'deployment-record':
      return 'deployments';
    case 'reviews':
    case 'review':
      return 'reviews';
    case 'documents':
    case 'document-link':
      return 'documents';
    case 'owners':
    case 'person':
      return 'owners';
    case 'teams':
    case 'team':
      return 'teams';
    case 'gap-check':
    case 'gap-check-run':
    case 'claim':
      return 'gap-check';
    case 'retirement':
      return 'retirement';
    case 'portfolios':
    case 'portfolio':
      return 'portfolios';
    case 'agents':
      return 'agents';
    case 'places':
    case 'place':
      return 'places';
    case 'decisions':
      return 'decisions';
    case 'timeline':
      return 'timeline';
    case 'audit':
    case 'evidence':
      return 'audit';
    case 'ai-activity':
      return 'ai-activity';
    case 'demo-guide':
      return 'demo-guide';
    // ---- wave 3 ----
    case 'data':
    case 'data-domain':
    case 'data-location':
    case 'data-mapping':
    case 'data-use':
      return 'data';
    case 'configuration':
    case 'configuration-draft':
      return 'configuration';
    case 'roles':
      return 'roles';
    case 'guidelines':
      return 'guidelines';
    case 'measures':
      return 'measures';
    case 'admin':
      return 'admin';
    case 'app':
      return 'app';
    case 'map':
      return 'map';
    default:
      return null;
  }
}
