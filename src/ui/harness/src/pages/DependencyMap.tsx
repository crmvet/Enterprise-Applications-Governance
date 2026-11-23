// Page 3: the dependency map. "What breaks if it changes or retires?" in both directions, to a set depth.
// Reads (document 24 names): get_dependency_impact twice (direction dependants, direction dependencies; depth 3),
// get_configuration (dependency-type labels and inverse labels). The walk runs on the server in one call per
// direction, because the built-in query tool appears to lack recursion (document 24, tool 7).
// The graph is a layered drawing: what it needs on the left, the application in the middle, what depends on it on
// the right. Arrows point to what is needed. The list view holds every node, so nothing is reachable only by mouse.
import { ArrowLeft, Info, TriangleAlert } from 'lucide-react';
import { useMemo, useState } from 'react';
import type { Bucket, DependencyImpact, ImpactNode } from '../data/contracts';
import { plural } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { lifecycleText } from '../data/meanings';
import { dataOf, useTool, type ToolState } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { palette } from '../theme';
import { Row, RowList } from '../components/rows';
import { BarList, DataStamp, Empty, PageHeader, Panel, PanelFailure, SwitchTabs, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { Alert, AlertDescription } from '../components/ui/alert';
import { Button } from '../components/ui/button';
import { criticalityTone } from './Application360';

type Side = 'dependants' | 'dependencies';
type Show = 'both' | Side;

const NODE_W = 164;
const NODE_H = 84;
const GAP_X = 36;
const GAP_Y = 14;
const PAD = 6;
const HEADER_H = 26;

// Section heads inside the panels (hops, "By tier", "By criticality").
const smallHead = 'text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

const edgeStyle: Record<string, { stroke: string; width: number; dash?: string }> = {
  high: { stroke: palette.navy, width: 2.5 },
  medium: { stroke: palette.navy3, width: 2 },
  low: { stroke: palette.muted, width: 1.5 },
  none: { stroke: palette.muted, width: 1.5, dash: '5 4' },
};
const critKey = (k: string | null) => (k === 'high' || k === 'medium' || k === 'low' ? k : 'none');

export function DependencyMap({ applicationId }: { applicationId: string }) {
  const host = useHost();
  const narrow = useNarrow();
  const dependants = useTool('get_dependency_impact', { businessApplicationId: applicationId, direction: 'dependants', depth: 3 });
  const dependencies = useTool('get_dependency_impact', { businessApplicationId: applicationId, direction: 'dependencies', depth: 3 });
  const types = useLabels('dependency-type');
  const [show, setShow] = useState<Show>('both');
  const [view, setView] = useState<'graph' | 'list' | null>(null);
  const shownView = view ?? (narrow ? 'list' : 'graph');

  const right = dataOf(dependants);
  const left = dataOf(dependencies);
  const rootName = right?.root.name ?? left?.root.name ?? 'This application';
  const stampEnv = dependants.status === 'done' ? dependants.envelope : undefined;
  const bothFailed = dependants.status === 'done' && !dependants.envelope.ok && dependencies.status === 'done' && !dependencies.envelope.ok;

  return (
    <>
      <PageHeader
        eyebrow="Dependency map"
        title={rootName}
        subtitle={`What breaks if ${rootName} changes or retires, and what it needs to run. Up to 3 hops.`}
        meta={<DataStamp envelope={stampEnv} />}
        actions={
          <Button variant="outline" className="bg-card" asChild>
            <a href={host.href({ page: 'app', id: applicationId })}>
              <ArrowLeft aria-hidden />
              Application 360
            </a>
          </Button>
        }
      />

      <div className="mb-3.5 flex flex-wrap items-center gap-x-5 gap-y-2">
        <SwitchTabs<Show>
          label="Show"
          ariaLabel="Direction"
          value={show}
          onChange={setShow}
          options={[
            { value: 'both', label: 'Both' },
            { value: 'dependants', label: 'What breaks' },
            { value: 'dependencies', label: 'What it needs' },
          ]}
        />
        <SwitchTabs<'graph' | 'list'>
          label="View"
          ariaLabel="View"
          value={shownView}
          onChange={setView}
          options={[
            { value: 'graph', label: 'Graph' },
            { value: 'list', label: 'List' },
          ]}
        />
      </div>

      {bothFailed && dependants.status === 'done' && !dependants.envelope.ok ? (
        <PanelFailure failure={dependants.envelope} />
      ) : shownView === 'graph' ? (
        <Panel title="Graph" subtitle="Hover or focus an application to trace its path. Select it to open its 360.">
          <GraphBody show={show} rootId={applicationId} rootName={rootName} left={left} right={right} types={types} />
        </Panel>
      ) : (
        <div className="grid grid-cols-2 gap-4 medium:grid-cols-1 narrow:gap-3">
          {show !== 'dependencies' && (
            <ImpactListPanel side="dependants" title={`What breaks if ${rootName} changes or retires`} state={dependants} rootId={applicationId} rootName={rootName} types={types} />
          )}
          {show !== 'dependants' && (
            <ImpactListPanel side="dependencies" title={`What ${rootName} needs`} state={dependencies} rootId={applicationId} rootName={rootName} types={types} />
          )}
        </div>
      )}

      <div className="mt-4 grid grid-cols-2 gap-4 medium:grid-cols-1 narrow:mt-3 narrow:gap-3">
        {show !== 'dependencies' && <CountsPanel title="What breaks, in numbers" state={dependants} noun="depend on it" />}
        {show !== 'dependants' && <CountsPanel title="What it needs, in numbers" state={dependencies} noun="it needs" />}
      </div>
    </>
  );
}

interface Placed {
  key: string;
  node: ImpactNode | null; // null for the root
  side: Side | 'root';
  col: number;
  x: number;
  y: number;
  parentKey: string | null;
}

// Each application sits level with the one it hangs from, as far as the column allows; siblings fan out around it.
function layout(show: Show, rootId: string, left: DependencyImpact | null, right: DependencyImpact | null) {
  const sides: { side: Side; data: DependencyImpact | null; sign: number }[] = [];
  if (show !== 'dependants') sides.push({ side: 'dependencies', data: left, sign: -1 });
  if (show !== 'dependencies') sides.push({ side: 'dependants', data: right, sign: 1 });

  const STEP = NODE_H + GAP_Y;
  const rootKey = `root:${rootId}`;
  const all: Placed[] = [{ key: rootKey, node: null, side: 'root', col: 0, x: 0, y: -NODE_H / 2, parentKey: null }];
  const byKey = new Map(all.map(p => [p.key, p]));
  const critRank = (k: string | null) => (k === 'high' ? 0 : k === 'medium' ? 1 : k === 'low' ? 2 : 3);

  for (const { side, data, sign } of sides) {
    if (!data) continue;
    const maxDepth = Math.max(0, ...data.nodes.map(n => n.depth));
    for (let depth = 1; depth <= maxDepth; depth++) {
      const groups = new Map<string, ImpactNode[]>();
      for (const n of data.nodes.filter(x => x.depth === depth)) {
        const parentKey = depth === 1 ? rootKey : `${side}:${n.path[n.path.length - 2]}`;
        groups.set(parentKey, [...(groups.get(parentKey) ?? []), n]);
      }
      const parents = [...groups.keys()].sort((a, b) => (byKey.get(a)?.y ?? 0) - (byKey.get(b)?.y ?? 0));
      let lastBottom = -Infinity;
      for (const parentKey of parents) {
        const kids = groups.get(parentKey)!.sort((a, b) => critRank(a.criticalityKey) - critRank(b.criticalityKey) || a.name.localeCompare(b.name));
        const parentCenter = (byKey.get(parentKey)?.y ?? -NODE_H / 2) + NODE_H / 2;
        kids.forEach((n, j) => {
          const idealTop = parentCenter + (j - (kids.length - 1) / 2) * STEP - NODE_H / 2;
          const top = Math.max(idealTop, lastBottom + GAP_Y);
          lastBottom = top + NODE_H;
          const p: Placed = { key: `${side}:${n.businessApplicationId}`, node: n, side, col: sign * depth, x: 0, y: top, parentKey };
          all.push(p);
          byKey.set(p.key, p);
        });
      }
    }
  }

  const cols = [...new Set(all.map(p => p.col))].sort((a, b) => a - b);
  const minCol = cols[0];
  const minTop = Math.min(...all.map(p => p.y));
  const maxBottom = Math.max(...all.map(p => p.y + NODE_H));
  for (const p of all) {
    p.x = PAD + (p.col - minCol) * (NODE_W + GAP_X);
    p.y = p.y - minTop + HEADER_H + PAD;
  }
  const width = PAD * 2 + cols.length * NODE_W + (cols.length - 1) * GAP_X;
  const height = HEADER_H + PAD * 2 + (maxBottom - minTop);
  return { placed: all, cols, minCol, width, height, rootKey };
}

function GraphBody(props: { show: Show; rootId: string; rootName: string; left: DependencyImpact | null; right: DependencyImpact | null; types: Labels }) {
  const host = useHost();
  const [focus, setFocus] = useState<string | null>(null);
  const { placed, cols, minCol, width, height, rootKey } = useMemo(
    () => layout(props.show, props.rootId, props.left, props.right),
    [props.show, props.rootId, props.left, props.right],
  );
  const byKey = new Map(placed.map(p => [p.key, p]));
  const fan = new Map<string, string[]>();
  for (const p of [...placed].sort((a, b) => a.y - b.y)) {
    if (!p.parentKey) continue;
    const k = `${p.parentKey}|${p.side}`;
    fan.set(k, [...(fan.get(k) ?? []), p.key]);
  }
  const fanOffset = (p: Placed) => {
    const list = fan.get(`${p.parentKey}|${p.side}`) ?? [];
    if (list.length < 2) return 0;
    const step = Math.min(9, (NODE_H - 28) / (list.length - 1));
    return (list.indexOf(p.key) - (list.length - 1) / 2) * step;
  };

  // The path from the root to the focused node lights up; everything else dims.
  const lit = new Set<string>();
  if (focus) {
    let k: string | null = focus;
    while (k) {
      lit.add(k);
      k = byKey.get(k)?.parentKey ?? null;
    }
  }

  const truncated = (props.show !== 'dependencies' && props.right?.truncatedAtDepth) || (props.show !== 'dependants' && props.left?.truncatedAtDepth);

  const colTitle = (c: number) => (c === 0 ? 'This application' : c > 0 ? `Depends on it, ${plural(c, 'hop', 'hops')}` : `It needs, ${plural(-c, 'hop', 'hops')}`);

  return (
    <>
      {truncated && (
        <Alert variant="info" aria-label="Note">
          <Info aria-hidden />
          <AlertDescription>
            <p>The walk stopped at 3 hops and there is more beyond. A deeper walk is possible up to the limit the configuration sets.</p>
          </AlertDescription>
        </Alert>
      )}
      <div className="overflow-x-auto overflow-y-hidden pb-1">
        <div className="relative mx-auto" style={{ width, height }}>
          <svg className="absolute inset-0 overflow-visible" width={width} height={height} aria-hidden>
            <defs>
              {Object.entries(edgeStyle).map(([k, st]) => (
                <marker key={k} id={`arrow-${k}`} viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
                  <path d="M 0 0 L 10 5 L 0 10 z" fill={st.stroke} />
                </marker>
              ))}
              <marker id="arrow-lit" viewBox="0 0 10 10" refX="9" refY="5" markerUnits="userSpaceOnUse" markerWidth="9" markerHeight="9" orient="auto-start-reverse">
                <path d="M 0 0 L 10 5 L 0 10 z" fill={palette.blue} />
              </marker>
            </defs>
            {placed.filter(p => p.parentKey).map(p => {
              const parent = byKey.get(p.parentKey!)!;
              const [a, b] = p.col > parent.col ? [parent, p] : [p, parent];
              // Arrows point to what is needed: always to the left end of the edge.
              const off = fanOffset(p);
              const x1 = b.x;
              const y1 = b.y + NODE_H / 2 + (b === parent ? off : 0);
              const x2 = a.x + NODE_W;
              const y2 = a.y + NODE_H / 2 + (a === parent ? off : 0);
              const mid = (x1 - x2) / 2;
              const ck = critKey(p.node!.criticalityKey);
              const st = edgeStyle[ck];
              const isLit = focus !== null && lit.has(p.key) && lit.has(p.parentKey!);
              const dim = focus !== null && !isLit;
              return (
                <path
                  key={`edge-${p.key}`}
                  d={`M ${x1} ${y1} C ${x1 - mid} ${y1}, ${x2 + mid} ${y2}, ${x2 + 2} ${y2}`}
                  fill="none"
                  stroke={isLit ? palette.blue : st.stroke}
                  strokeWidth={isLit ? 3 : st.width}
                  strokeDasharray={st.dash}
                  strokeLinecap="round"
                  opacity={dim ? 0.25 : 1}
                  markerEnd={`url(#arrow-${isLit ? 'lit' : ck})`}
                />
              );
            })}
          </svg>
          {cols.map(c => (
            <div
              key={`head-${c}`}
              className="absolute top-0 text-center text-[10px] font-semibold tracking-[0.05em] text-navy-3 uppercase"
              style={{ left: PAD + (c - minCol) * (NODE_W + GAP_X), width: NODE_W }}
            >
              {colTitle(c)}
            </div>
          ))}
          {placed.map(p => {
            const isRoot = p.key === rootKey;
            const n = p.node;
            const name = isRoot ? props.rootName : n!.name;
            const relation = isRoot
              ? 'Starting point'
              : p.side === 'dependants'
                ? props.types.label(n!.dependencyTypeKey)
                : (props.types.field<string>(n!.dependencyTypeKey, 'inverseLabel') ?? props.types.label(n!.dependencyTypeKey));
            const facts = isRoot ? '' : [n!.tier?.label ?? 'No tier yet', n!.lifecycleMeaning ? lifecycleText[n!.lifecycleMeaning] : null].filter(Boolean).join(', ');
            const noOwner = !isRoot && !n!.businessOwner;
            const href = host.href({ page: 'app', id: isRoot ? props.rootId : n!.businessApplicationId });
            const label = isRoot
              ? `${name}, the starting point. Opens its 360.`
              : `${name}. ${relation} ${byKey.get(p.parentKey!)?.node?.name ?? props.rootName}. ${facts}.${noOwner ? ' No business owner.' : ''} Criticality ${n!.criticalityKey ?? 'not set'}. Opens its 360.`;
            return (
              <a
                key={p.key}
                href={href}
                className={cn(
                  'absolute flex flex-col justify-center gap-y-px rounded-md border border-line-strong bg-card px-2.5 py-1.5 text-navy no-underline shadow-[0_1px_2px_rgba(10,42,74,0.06)] transition-[opacity,border-color,box-shadow] duration-120',
                  'hover:border-primary hover:shadow-[0_2px_6px_rgba(10,42,74,0.12)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
                  // The application itself: light blue with a gold bar along the top.
                  isRoot && 'border-2 border-primary bg-secondary before:absolute before:-inset-x-0.5 before:-top-0.5 before:h-[5px] before:rounded-t-md before:bg-gold',
                  focus !== null && !lit.has(p.key) && 'opacity-38',
                  focus !== null && lit.has(p.key) && !isRoot && 'border-primary',
                )}
                style={{ left: p.x, top: p.y, width: NODE_W, height: NODE_H }}
                onMouseEnter={() => setFocus(p.key)}
                onMouseLeave={() => setFocus(null)}
                onFocus={() => setFocus(p.key)}
                onBlur={() => setFocus(null)}
                aria-label={label}
                title={name}
              >
                <span className="line-clamp-2 shrink-0 text-[13px] leading-[17px] font-semibold">{name}</span>
                <span className="shrink-0 truncate text-xs leading-4 text-navy-3">{relation}</span>
                {!isRoot && (
                  <span className="flex min-w-0 shrink-0 items-center gap-x-1 text-xs leading-4 text-navy-3">
                    {noOwner && <TriangleAlert className="size-3.5 shrink-0 text-gold-strong" aria-hidden />}
                    <span className="truncate">{noOwner ? 'No business owner' : facts}</span>
                  </span>
                )}
                {isRoot && <span className="shrink-0 truncate text-xs leading-4 text-navy-3">Select to open its 360</span>}
              </a>
            );
          })}
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-x-[18px] gap-y-2 border-t border-line pt-3 text-xs text-navy-2" role="group" aria-label="Legend">
        <span>Criticality of the link:</span>
        {(['high', 'medium', 'low', 'none'] as const).map(k => (
          <span key={k} className="inline-flex items-center gap-x-1.5">
            <svg width="30" height="10" aria-hidden>
              <line x1="1" y1="5" x2="29" y2="5" stroke={edgeStyle[k].stroke} strokeWidth={edgeStyle[k].width} strokeDasharray={edgeStyle[k].dash} strokeLinecap="round" />
            </svg>
            {k === 'none' ? 'Not set' : k.charAt(0).toUpperCase() + k.slice(1)}
          </span>
        ))}
        <span className="inline-flex items-center gap-x-1.5">
          <svg width="22" height="10" aria-hidden>
            <line x1="20" y1="5" x2="6" y2="5" stroke={palette.navy3} strokeWidth="1.5" />
            <path d="M 2 5 L 8 1.5 L 8 8.5 z" fill={palette.navy3} />
          </svg>
          Arrows point to what is needed
        </span>
        <span className="inline-flex items-center gap-x-1.5">
          <TriangleAlert className="size-3.5 shrink-0 text-gold-strong" aria-hidden />
          No business owner
        </span>
      </div>
    </>
  );
}

function ImpactListPanel(props: { side: Side; title: string; state: ToolState<DependencyImpact>; rootId: string; rootName: string; types: Labels }) {
  const host = useHost();
  return (
    <Panel title={props.title} state={props.state} count={dataOf(props.state)?.nodes.length} skeletonRows={6}>
      {data => {
        if (!data.nodes.length) return <Empty>{props.side === 'dependants' ? 'Nothing depends on it.' : 'It needs nothing recorded.'}</Empty>;
        const names = new Map<string, string>([[props.rootId, props.rootName], ...data.nodes.map(n => [n.businessApplicationId, n.name] as [string, string])]);
        const depths = [...new Set(data.nodes.map(n => n.depth))].sort((a, b) => a - b);
        return (
          <>
            {data.truncatedAtDepth && (
              <Alert variant="info" aria-label="Note">
                <Info aria-hidden />
                <AlertDescription>
                  <p>There is more beyond 3 hops. A deeper walk is possible up to the configured limit.</p>
                </AlertDescription>
              </Alert>
            )}
            {depths.map(depth => (
              <div key={depth}>
                <h3 className={cn(smallHead, 'mt-2.5 mb-0.5')}>{plural(depth, 'hop', 'hops')}</h3>
                <RowList label={`${plural(depth, 'hop', 'hops')} away`}>
                  {data.nodes.filter(n => n.depth === depth).map(n => {
                    const parent = names.get(n.path[n.path.length - 2]) ?? props.rootName;
                    const relation = props.side === 'dependants'
                      ? `${props.types.label(n.dependencyTypeKey)} ${parent}`
                      : `${props.types.field<string>(n.dependencyTypeKey, 'inverseLabel') ?? props.types.label(n.dependencyTypeKey)} ${parent}`;
                    const owners = n.businessOwner ? `Business owner ${n.businessOwner.displayLabel}` : null;
                    return (
                      <Row
                        key={n.businessApplicationId}
                        primary={<TextLink href={host.href({ page: 'app', id: n.businessApplicationId })}>{n.name}</TextLink>}
                        secondary={relation}
                        tertiary={owners ?? (
                          <span className="inline-flex items-center gap-x-1 font-semibold text-gold-strong">
                            <TriangleAlert className="size-3.5 shrink-0" aria-hidden />
                            No business owner
                          </span>
                        )}
                        end={<ToneBadge tone={criticalityTone(n.criticalityKey)} icon={null}>{critText(n.criticalityKey)}</ToneBadge>}
                        endSub={[n.tier?.label ?? 'No tier yet', n.lifecycleMeaning ? lifecycleText[n.lifecycleMeaning] : null].filter(Boolean).join(', ')}
                      />
                    );
                  })}
                </RowList>
              </div>
            ))}
          </>
        );
      }}
    </Panel>
  );
}

const critText = (k: string | null) => (k === 'high' ? 'High' : k === 'medium' ? 'Medium' : k === 'low' ? 'Low' : 'Not set');

function CountsPanel({ title, state, noun }: { title: string; state: ToolState<DependencyImpact>; noun: string }) {
  const host = useHost();
  const toItems = (buckets: Bucket[]) => buckets.map(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }));
  return (
    <Panel title={title} state={state} skeletonRows={4}>
      {data => (
        <>
          <div className="text-sm text-navy-2">
            <span className="mr-1.5 text-2xl font-semibold text-navy tabular-nums">{data.nodes.length}</span>
            {data.nodes.length === 1 ? 'application' : 'applications'} {noun} within 3 hops{data.truncatedAtDepth ? ', and more beyond' : ''}
          </div>
          {data.nodes.length > 0 && <div className="grid grid-cols-2 gap-4 narrow:grid-cols-1">
            <div>
              <h3 className={cn(smallHead, 'mt-0 mb-1')}>By tier</h3>
              <BarList ariaLabel={`${title} by tier`} noun={['application', 'applications']} labelWidth={90} items={toItems(data.counts.byTier)} onDrill={b => host.drill(`${title}: ${b.label}`)} />
            </div>
            <div>
              <h3 className={cn(smallHead, 'mt-0 mb-1')}>By criticality</h3>
              <BarList ariaLabel={`${title} by criticality`} noun={['link', 'links']} labelWidth={90} items={toItems(data.counts.byCriticality)} onDrill={b => host.drill(`${title}: ${b.label} criticality`)} />
            </div>
          </div>}
        </>
      )}
    </Panel>
  );
}
