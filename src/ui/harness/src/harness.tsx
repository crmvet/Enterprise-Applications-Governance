// What the host gives every page: today's date, navigation and the drill-down hand-off.
// In a model-driven app the host is the app shell; in the web app it is the router. Here it is the harness.
import { createContext, useContext } from 'react';
import type { IsoDate } from './data/contracts';

export type Route =
  | { page: 'overview' }
  | { page: 'app'; id: string }
  | { page: 'map'; id: string }
  | { page: 'timeline' };

export interface Host {
  // The harness clock is fixed to the fixtures' asOf date, so every render is the same.
  today: IsoDate;
  // The application the harness opens by default (Application 360 and the dependency map).
  defaultApplicationId: string;
  href(route: Route): string;
  // Every number drills to rows. Pages hand the drill to the host, which opens the matching view or grid.
  drill(what: string): void;
}

export const HostContext = createContext<Host | null>(null);

export function useHost(): Host {
  const host = useContext(HostContext);
  if (!host) throw new Error('useHost needs a HostContext provider.');
  return host;
}

export function parseRoute(hash: string, defaultApplicationId: string): Route {
  const parts = hash.replace(/^#\/?/, '').split('/').filter(Boolean);
  switch (parts[0]) {
    case 'app':
      return { page: 'app', id: parts[1] ?? defaultApplicationId };
    case 'map':
      return { page: 'map', id: parts[1] ?? defaultApplicationId };
    case 'timeline':
      return { page: 'timeline' };
    default:
      return { page: 'overview' };
  }
}

export function routeHref(route: Route): string {
  switch (route.page) {
    case 'app':
      return `#/app/${route.id}`;
    case 'map':
      return `#/map/${route.id}`;
    case 'timeline':
      return '#/timeline';
    default:
      return '#/overview';
  }
}
