// The dependency map's component and data place nodes (screen "Dependency map", wave 3), with no React in them, so the
// unit tests run them in Node (mapParts.test.ts). The impact walk (get_dependency_impact) returns applications only, so
// the map reads the application's card (sections components, dependencies and data) for the rest:
//   components      the parts the application is built from (card components): "Built from", opening in their tool
//   component links the application's own dependencies on a component (card dependencies.top, depends-on with a
//                   component): the relation word of the dependency type, such as "Reads data from"
//   data places     each data location its data uses name (card data.uses with a location), one node per location, with
//                   the access in words: "Reads data from", "Writes data to", "Reads and writes data in", and the domains
// A data use that names no location yet is counted, not drawn.
import type { ApplicationCard, DataAccess, DataUseItem } from '../../data/contracts';

export type PartKind = 'component' | 'component-link' | 'data-place';

export interface PartNode {
  key: string;
  kind: PartKind;
  name: string;
  // The relation from the application to the part, as a sentence start: "Built from", "Reads data from".
  relation: string;
  // A short line of facts: the component kind, the domains and the platform.
  facts: string;
  // An in-app record link (a data place) or a link into the part's own tool (a component); null when there is none.
  recordUrl: string | null;
  externalUrl: string | null;
  // A criticality key, for a component link.
  criticalityKey: string | null;
  // True for a component the last gap check no longer saw.
  vanished: boolean;
}

// The relation words of a data access.
export const ACCESS_RELATION: Record<DataAccess, string> = {
  read: 'Reads data from',
  write: 'Writes data to',
  'read-write': 'Reads and writes data in',
};

// The access of several uses of one place: read and write together read as both.
export function combineAccess(list: readonly DataAccess[]): DataAccess {
  const reads = list.some(a => a === 'read' || a === 'read-write');
  const writes = list.some(a => a === 'write' || a === 'read-write');
  return reads && writes ? 'read-write' : writes ? 'write' : 'read';
}

// The uses that are open: not ended and not archived. A map shows what the application does now.
const openUse = (u: Pick<DataUseItem, 'endedOn' | 'status'>) => !u.endedOn && u.status.meaning !== 'archived' && u.status.meaning !== 'withdrawn';

function domainsText(paths: string[]): string {
  const unique = [...new Set(paths)];
  if (unique.length <= 2) return unique.join(', ');
  return `${unique.slice(0, 2).join(', ')} and ${unique.length - 2} more`;
}

// The part nodes of one application card, components first, then component links, then data places, each by name.
export function partNodes(card: Pick<ApplicationCard, 'components' | 'dependencies' | 'data'> | null, dependencyRelation: (typeKey: string, label: string) => string): PartNode[] {
  if (!card) return [];
  const byName = (a: PartNode, b: PartNode) => a.name.localeCompare(b.name);
  const components: PartNode[] = (card.components ?? []).map(c => ({
    key: `component:${c.technicalComponentId.toLowerCase()}`,
    kind: 'component',
    name: c.labelAsSeen || c.externalId,
    relation: 'Built from',
    facts: c.kind.label,
    recordUrl: null,
    externalUrl: c.url,
    criticalityKey: null,
    vanished: !!c.vanishedInGapCheck,
  }));
  const ownComponents = new Set(components.map(c => c.key));
  const links: PartNode[] = (card.dependencies?.top ?? [])
    .filter(d => d.direction === 'depends-on' && d.technicalComponentId && !d.businessApplicationId)
    .map(d => ({
      key: `component-link:${d.technicalComponentId!.toLowerCase()}`,
      kind: 'component-link' as const,
      name: d.name,
      relation: dependencyRelation(d.dependencyType.key, d.dependencyType.label),
      facts: d.criticality ? `Criticality ${d.criticality.label.toLowerCase()}` : 'Criticality not set',
      recordUrl: null,
      externalUrl: null,
      criticalityKey: d.criticality?.key ?? null,
      vanished: false,
    }))
    .filter(l => !ownComponents.has(l.key.replace('component-link:', 'component:')));
  const places = new Map<string, { uses: DataUseItem[] }>();
  for (const u of (card.data?.uses ?? []).filter(openUse)) {
    if (!u.location) continue;
    const k = u.location.dataLocationId.toLowerCase();
    places.set(k, { uses: [...(places.get(k)?.uses ?? []), u] });
  }
  const dataPlaces: PartNode[] = [...places.entries()].map(([id, { uses }]) => {
    const loc = uses[0].location!;
    const access = combineAccess(uses.map(u => u.access));
    const where = [loc.platform.label, loc.stage?.label].filter(Boolean).join(', ');
    return {
      key: `data-place:${id}`,
      kind: 'data-place' as const,
      name: loc.label,
      relation: ACCESS_RELATION[access],
      facts: [domainsText(uses.map(u => u.domain.path || u.domain.label)), where].filter(Boolean).join('. '),
      recordUrl: loc.recordUrl,
      externalUrl: null,
      criticalityKey: null,
      vanished: false,
    };
  });
  return [...components.sort(byName), ...links.sort(byName), ...dataPlaces.sort(byName)];
}

// The open data uses that name no location yet: counted under the map, never drawn.
export function usesWithoutPlace(card: Pick<ApplicationCard, 'data'> | null): number {
  return (card?.data?.uses ?? []).filter(u => openUse(u) && !u.location).length;
}

export const PART_KIND_TEXT: Record<PartKind, string> = {
  component: 'Component',
  'component-link': 'Component it depends on',
  'data-place': 'Data place',
};
