// Links between the web app's pages, for the pages themselves. The four original pages link through the host
// (useHost().href), because they also run in the harness; the work pages run only in the web app and use these.
//   hrefTo({ page: 'decision', id })      the address of a page, for <a href>: a plain click navigates in the app
//   goTo({ page: 'approvals', view })     navigates from code, such as after a write that created a record
//   recordHref(item.recordUrl)            a record link a tool returned, as an in-app address (or null)
//   recordHrefOf('change-request', id)    the record page of an entity and ID
//   evidenceHref('decision', id)          the evidence pack of an application or a decision
//   domainHref('payroll')                 a data domain's record page (/app/records/data-domain/<key>; a domain has a key)
//   guidelineHref('G17')                  the Guidelines page open at one guideline (a guideline chip links here)
//   teamHref(objectId)                    a team's page (/app/teams/<objectId>), by the group's Entra object ID (wave 4)
//   teamsHref({ roleKey: 'change-board' }) the Teams list, filtered (the decision page's "waiting on" with several teams)
//   useQueryParam('tab', /^[a-z-]+$/)     a page's own query parameter that its route does not carry (a tab, a sort)
//   withParams(href, { tab: 'tier' })     an address with such parameters set or removed
// A list's text search in the address (q=) is components/listSearch.tsx (useListText, ListSearch).
import { navigate, useLocationKey } from './navigation';
import { appHref, normalizeBase, recordPath, recordRoute, type AppRoute, type EvidenceKind, type RouteOf } from './routes';

const base = () => normalizeBase(import.meta.env.BASE_URL);

export function hrefTo(route: AppRoute): string {
  return appHref(route, base());
}

export function goTo(route: AppRoute, replace = false): void {
  navigate(hrefTo(route), replace);
}

export function recordHref(recordUrl: string | null | undefined): string | null {
  return recordPath(recordUrl, base());
}

export function recordHrefOf(entity: string, id: string): string | null {
  const route = recordRoute(entity, id);
  return route.page === 'not-found' ? null : hrefTo(route);
}

export function evidenceHref(subjectKind: EvidenceKind, id: string): string {
  return hrefTo({ page: 'evidence', subjectKind, id });
}

export function domainHref(domainKey: string): string {
  return hrefTo({ page: 'data-domain', key: domainKey });
}

// The Guidelines page at one guideline, or the page itself when the number is not a guideline number.
export function guidelineHref(number: string | null | undefined): string {
  const g = (number ?? '').toUpperCase();
  return hrefTo(/^G[1-9][0-9]{0,2}$/.test(g) ? { page: 'guidelines', guideline: g } : { page: 'guidelines' });
}

// A team's page, by the group's Entra object ID (list_teams teamId, a group's PrincipalRef objectId).
export function teamHref(objectId: string): string {
  return hrefTo({ page: 'team', id: objectId.toLowerCase() });
}

// The Teams list with its filters: a kind, words to search, or the teams that hold one role.
export function teamsHref(filter: Omit<RouteOf<'teams'>, 'page'> = {}): string {
  return hrefTo({ page: 'teams', ...filter });
}

// A query parameter of the current address, when it matches the pattern; else undefined. The page renders again when
// the address changes. The route of a page carries its shared filters; a parameter only one page reads (an Application
// 360 tab, a sort order) is read here instead, so the route type stays the same for every page.
export function useQueryParam(name: string, pattern: RegExp): string | undefined {
  const key = useLocationKey();
  const at = key.indexOf('?');
  const value = new URLSearchParams(at < 0 ? '' : key.slice(at + 1)).get(name);
  return value !== null && pattern.test(value) ? value : undefined;
}

// The address with the parameters set (or removed when undefined).
export function withParams(href: string, params: Record<string, string | undefined>): string {
  const at = href.indexOf('?');
  const path = at < 0 ? href : href.slice(0, at);
  const query = new URLSearchParams(at < 0 ? '' : href.slice(at + 1));
  for (const [k, v] of Object.entries(params)) {
    if (v) query.set(k, v);
    else query.delete(k);
  }
  const text = query.toString();
  return text ? `${path}?${text}` : path;
}
