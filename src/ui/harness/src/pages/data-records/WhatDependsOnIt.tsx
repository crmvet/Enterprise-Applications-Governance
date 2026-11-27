// "What depends on it?" for a technical component or a data location (wave 4):
// get_dependency_impact started at the component or the location (document 24, B24-22), the dependants three hops out,
// in a dialog. Each application says how it joined the walk (via): built from the component, using it as its AI
// component, holding a data use at the location, being the application the location is, or depending on an application
// already in the walk. A location start also says what the location holds. Each application opens its 360.
// Used by Application 360's components and the data location record. A read only: nothing here writes.
import { Network } from 'lucide-react';
import { useState, type ReactElement } from 'react';
import { Empty, Muted, PanelFailure, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '../../components/ui/dialog';
import type { DependencyImpact } from '../../data/contracts';
import { DATA_ORIGIN_SHORT } from '../../data/dataTaxonomy';
import { plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { lifecycleText } from '../../data/meanings';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { useHost } from '../../harness';
import { criticalityTone } from '../app360/parts';
import { impactGroups, viaText } from './logic';

export type ImpactStart = { technicalComponentId: string } | { dataLocationId: string };

const DEPTH = 3;

export function WhatDependsOnIt({ start, name, trigger }: { start: ImpactStart; name: string; trigger?: ReactElement }) {
  const [open, setOpen] = useState(false);
  const host = useHost();
  const types = useLabels('dependency-type');
  const read = useRead('get_dependency_impact', open ? { ...start, direction: 'dependants', depth: DEPTH } : null);
  const data = dataOf(read);
  const failure = read.status === 'done' && !read.envelope.ok ? read.envelope : null;
  const groups = data ? impactGroups(data) : [];
  const isLocation = 'dataLocationId' in start;
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        {trigger ?? (
          <Button size="sm" variant="outline" aria-label={`What depends on ${name}?`}>
            <Network aria-hidden />What depends on it?
          </Button>
        )}
      </DialogTrigger>
      <DialogContent showCloseButton={false} className="gap-4 bg-card text-navy sm:max-w-xl">
        <DialogHeader className="text-left">
          <DialogTitle className="text-lg leading-6 text-navy">{`What depends on ${name}?`}</DialogTitle>
          <DialogDescription className="m-0 text-sm text-navy-2">
            {isLocation
              ? `The applications with a data use there, the application the location is, and what depends on them, up to ${DEPTH} hops out.`
              : `The application built from it, the applications that depend on it or use it as their AI component, and what depends on them, up to ${DEPTH} hops out.`}
          </DialogDescription>
        </DialogHeader>
        <div className="flex max-h-[60vh] flex-col gap-3 overflow-y-auto">
          {failure ? <PanelFailure failure={failure} />
            : !data ? <Muted>Reading what depends on it.</Muted>
              : (
                <>
                  {data.dataLocation && <LocationFacts location={data.dataLocation} />}
                  {groups.length === 0 ? (
                    <Empty>{isLocation ? 'No application uses data here, and no application is this location.' : 'No application is built from it, depends on it or uses it.'}</Empty>
                  ) : groups.map(g => (
                    <section key={g.depth} aria-label={g.title} className="flex flex-col gap-1">
                      <h3 className="m-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{g.title}</h3>
                      <RowList label={g.title}>
                        {g.nodes.map(n => (
                          <Row
                            key={`${g.depth}-${n.node.businessApplicationId}`}
                            primary={<TextLink href={host.href({ page: 'app', id: n.node.businessApplicationId.toLowerCase() })} onClick={() => setOpen(false)}>{n.node.name}</TextLink>}
                            secondary={viaText(n.node, n.parentName ?? name, key => types.label(key))}
                            tertiary={[n.node.tier?.label ?? 'No tier yet', n.node.lifecycleMeaning ? lifecycleText[n.node.lifecycleMeaning] : null, n.node.businessOwner ? `Business owner ${n.node.businessOwner.displayLabel}` : 'No business owner'].filter(Boolean).join('. ')}
                            end={n.node.criticalityKey ? <ToneBadge tone={criticalityTone(n.node.criticalityKey)} icon={null}>{`${humanCriticality(n.node.criticalityKey)} criticality`}</ToneBadge> : undefined}
                          />
                        ))}
                      </RowList>
                    </section>
                  ))}
                  {data.truncatedAtDepth && <Muted>{`The walk stopped at ${DEPTH} hops. More applications may depend on these.`}</Muted>}
                </>
              )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function humanCriticality(key: string): string {
  return key.charAt(0).toUpperCase() + key.slice(1).replace(/-/g, ' ');
}

function LocationFacts({ location }: { location: NonNullable<DependencyImpact['dataLocation']> }) {
  return (
    <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2 text-sm text-navy">
      <span>
        {`${location.label}${location.platform ? `, ${location.platform.label}` : ''}. `}
        {`${plural(location.dataUseCount, 'open data use names it', 'open data uses name it')}`}
        {location.childLocationIds.length ? `, with ${plural(location.childLocationIds.length, 'location', 'locations')} inside it` : ''}.
      </span>
      {location.hostedBy && <span className="text-navy-2">{`It is the location of ${location.hostedBy.name}.`}</span>}
      {location.domainsHeld.length > 0 && (
        <span className="text-navy-2">
          {'Holds '}
          {location.domainsHeld.map((d, i) => `${i ? ', ' : ''}${d.path} (${d.origins.map(o => DATA_ORIGIN_SHORT[o]).join(', ')})`).join('')}
          .
        </span>
      )}
    </div>
  );
}
