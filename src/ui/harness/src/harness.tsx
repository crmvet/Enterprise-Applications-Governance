// What the host gives every page: today's date, navigation, the drill-down and the hand-off to an AI client.
// In a model-driven app the host is the app shell; in the web app it is the app shell in src/app/ (path routes under
// /app/); in the harness it is App.tsx (hash routes). The pages only ever see this interface.
import { createContext, useContext } from 'react';
import type { IsoDate } from './data/contracts';

export type Route =
  | { page: 'overview' }
  | { page: 'app'; id: string }
  | { page: 'map'; id: string }
  | { page: 'timeline' };

// A drill-down the applications list can serve: find_applications filters by configured keys.
export interface ApplicationFilter {
  lifecycleStageKeys?: string[];
  tierKeys?: string[];
  applicationKindKeys?: string[];
}

// A write the pages never make. The page hands the person a request to give to their AI client, which calls the tool;
// the engine checks the write, and only a person decides on an approval card.
export interface HandOff {
  title: string;
  prompt: string;
  tool: string;
}

export interface Host {
  // Today in the display zone. The harness clock is fixed to the fixtures' asOf date, so every render is the same.
  today: IsoDate;
  href(route: Route): string;
  // Every number drills to rows. A filter means the applications list can show the rows; without one, the host says
  // where the list lives.
  drill(what: string, filter?: ApplicationFilter): void;
  handOff(request: HandOff): void;
  // True when links.recordUrl opens a form outside these pages (binding A). The web app's record links point at these
  // pages, so it shows none.
  showRecordLinks: boolean;
}

export const HostContext = createContext<Host | null>(null);

export function useHost(): Host {
  const host = useContext(HostContext);
  if (!host) throw new Error('useHost needs a HostContext provider.');
  return host;
}

// ---- the harness's hash routes ----

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
