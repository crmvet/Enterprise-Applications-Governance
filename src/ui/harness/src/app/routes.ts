// The web app's addresses, under its base path (/app/):
//   /app/                                            Overview, the home page
//   /app/timeline                                    the timeline
//   /app/records/business-application/<id>           Application 360. This is the record link the tools return
//                                                    (links.recordUrl), so a link from an AI client opens the page
//   /app/records/business-application/<id>/map       the dependency map of that application
//   /app/records/<entity>/<id>                       any other record: a plain "not shown here" panel
//   /app/applications?stage=<key>&tier=<key>&kind=<key>   the applications list a drill-down opens
// The server answers every /app/ path that is not a file with index.html, and the page reads the path.
import type { ApplicationFilter, Route } from '../harness';

export type AppRoute =
  | Route
  | { page: 'applications'; filter: ApplicationFilter }
  | { page: 'record'; entity: string; id: string }
  | { page: 'not-found' };

const ID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const ENTITY = /^[a-z][a-z0-9-]{0,63}$/;
const KEY = /^[a-z][a-z0-9-]{0,63}$/;
const APPLICATION = 'business-application';

// The base path with one leading and one trailing slash: "/app/".
export function normalizeBase(base: string): string {
  const trimmed = base.replace(/^\/+|\/+$/g, '');
  return trimmed ? `/${trimmed}/` : '/';
}

function keys(params: URLSearchParams, name: string): string[] | undefined {
  const list = params.getAll(name).filter(k => KEY.test(k));
  return list.length ? [...new Set(list)].slice(0, 20) : undefined;
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

  if (parts.length === 0 || (parts.length === 1 && parts[0] === 'overview')) return { page: 'overview' };
  if (parts.length === 1 && parts[0] === 'timeline') return { page: 'timeline' };
  if (parts.length === 1 && parts[0] === 'applications') {
    const params = new URLSearchParams(search);
    const filter: ApplicationFilter = {};
    const stage = keys(params, 'stage');
    const tier = keys(params, 'tier');
    const kind = keys(params, 'kind');
    if (stage) filter.lifecycleStageKeys = stage;
    if (tier) filter.tierKeys = tier;
    if (kind) filter.applicationKindKeys = kind;
    return { page: 'applications', filter };
  }
  if (parts[0] === 'records' && (parts.length === 3 || parts.length === 4)) {
    const [, entity, id, view] = parts;
    if (!ENTITY.test(entity) || !ID.test(id)) return { page: 'not-found' };
    const normalId = id.toLowerCase();
    if (entity === APPLICATION && view === undefined) return { page: 'app', id: normalId };
    if (entity === APPLICATION && view === 'map') return { page: 'map', id: normalId };
    if (view === undefined) return { page: 'record', entity, id: normalId };
  }
  return { page: 'not-found' };
}

// The address of a route, under the base path.
export function appHref(route: AppRoute, base: string): string {
  const root = normalizeBase(base);
  switch (route.page) {
    case 'overview':
      return root;
    case 'timeline':
      return `${root}timeline`;
    case 'app':
      return `${root}records/${APPLICATION}/${encodeURIComponent(route.id)}`;
    case 'map':
      return `${root}records/${APPLICATION}/${encodeURIComponent(route.id)}/map`;
    case 'record':
      return `${root}records/${encodeURIComponent(route.entity)}/${encodeURIComponent(route.id)}`;
    case 'applications': {
      const params = new URLSearchParams();
      for (const k of route.filter.lifecycleStageKeys ?? []) params.append('stage', k);
      for (const k of route.filter.tierKeys ?? []) params.append('tier', k);
      for (const k of route.filter.applicationKindKeys ?? []) params.append('kind', k);
      const query = params.toString();
      return `${root}applications${query ? `?${query}` : ''}`;
    }
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

// A short page title for document.title.
export function routeTitle(route: AppRoute): string {
  switch (route.page) {
    case 'overview':
      return 'Portfolio overview';
    case 'timeline':
      return 'Timeline';
    case 'app':
      return 'Application 360';
    case 'map':
      return 'Dependency map';
    case 'applications':
      return 'Applications';
    case 'record':
      return 'Record';
    default:
      return 'Page not found';
  }
}
