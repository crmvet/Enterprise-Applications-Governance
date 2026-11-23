// Page 3: the dependency map. "What breaks if it changes or retires?" in both directions, to a set depth.
// Reads (document 24 names): get_dependency_impact twice (direction dependants, direction dependencies; depth 3),
// get_configuration (dependency-type labels and inverse labels). The walk runs on the server in one call per
// direction, because the built-in query tool appears to lack recursion (document 24, tool 7).
// The graph is a layered drawing: what it needs on the left, the application in the middle, what depends on it on
// the right. Arrows point to what is needed. The list view holds every node, so nothing is reachable only by mouse.
import { Button, Link, MessageBar, MessageBarBody, Tab, TabList, makeStyles, mergeClasses, shorthands, tokens } from '@fluentui/react-components';
import { ArrowLeft16Regular, Warning16Filled } from '@fluentui/react-icons';
import { useMemo, useState } from 'react';
import type { Bucket, DependencyImpact, ImpactNode } from '../data/contracts';
import { plural } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { lifecycleText } from '../data/meanings';
import { dataOf, useTool, type ToolState } from '../data/toolClient';
import { useHost } from '../harness';
import { palette } from '../theme';
import { Row, RowList } from '../components/rows';
import { BarList, DataStamp, Empty, PageHeader, Panel, PanelFailure, ToneBadge, media, useNarrow } from '../components/ui';
import { criticalityTone } from './Application360';

type Side = 'dependants' | 'dependencies';
type Show = 'both' | Side;

const NODE_W = 164;
const NODE_H = 84;
const GAP_X = 36;
const GAP_Y = 14;
const PAD = 6;
const HEADER_H = 26;

const useStyles = makeStyles({
  controls: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: '8px 20px',
    marginBottom: '14px',
  },
  controlLabel: { fontSize: tokens.fontSizeBase200, color: palette.navy3, marginRight: '4px' },
  control: { display: 'flex', alignItems: 'center', columnGap: '4px', flexWrap: 'wrap' },
  scroller: { overflowX: 'auto', overflowY: 'hidden', paddingBottom: '4px' },
  canvas: { position: 'relative', margin: '0 auto' },
  svg: { position: 'absolute', inset: 0, overflow: 'visible' },
  colHead: {
    position: 'absolute',
    top: 0,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy3,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    textAlign: 'center',
  },
  node: {
    position: 'absolute',
    boxSizing: 'border-box',
    display: 'flex',
    flexDirection: 'column',
    justifyContent: 'center',
    rowGap: '1px',
    padding: '6px 10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${palette.lineStrong}`,
    borderRadius: '8px',
    color: palette.navy,
    textDecoration: 'none',
    boxShadow: '0 1px 2px rgba(10, 42, 74, 0.06)',
    transitionProperty: 'opacity, border-color, box-shadow',
    transitionDuration: '120ms',
    ':hover': { ...shorthands.borderColor(palette.blue), boxShadow: '0 2px 6px rgba(10, 42, 74, 0.12)' },
    ':focus-visible': { outline: `2px solid ${palette.blue}`, outlineOffset: '2px' },
  },
  root: {
    backgroundColor: palette.lightBlue,
    border: `2px solid ${palette.blue}`,
    '::before': {
      content: '""',
      position: 'absolute',
      left: '-2px',
      right: '-2px',
      top: '-2px',
      height: '5px',
      borderRadius: '8px 8px 0 0',
      backgroundColor: palette.gold,
    },
  },
  dim: { opacity: 0.38 },
  lit: { ...shorthands.borderColor(palette.blue) },
  nodeName: {
    fontSize: '13px',
    lineHeight: '17px',
    fontWeight: tokens.fontWeightSemibold,
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical',
    overflow: 'hidden',
    flexShrink: 0,
  },
  nodeLine: { flexShrink: 0, lineHeight: '16px', fontSize: '12px', color: palette.navy3, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', display: 'flex', alignItems: 'center', columnGap: '4px' },
  warnIcon: { color: palette.goldDark, flexShrink: 0 },
  legend: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px 18px',
    paddingTop: '12px',
    borderTop: `1px solid ${palette.line}`,
    fontSize: tokens.fontSizeBase200,
    color: palette.navy2,
  },
  legendItem: { display: 'inline-flex', alignItems: 'center', columnGap: '6px' },
  counts: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)',
    gap: '16px',
    marginTop: '16px',
    [media.medium]: { gridTemplateColumns: 'minmax(0, 1fr)' },
    [media.narrow]: { gap: '12px', marginTop: '12px' },
  },
  countCols: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', [media.narrow]: { gridTemplateColumns: '1fr' } },
  countHead: { margin: '0 0 4px', fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold, color: palette.navy3, textTransform: 'uppercase', letterSpacing: '0.04em' },
  total: { fontSize: tokens.fontSizeBase300, color: palette.navy2 },
  totalNumber: { fontSize: '24px', fontWeight: tokens.fontWeightSemibold, color: palette.navy, marginRight: '6px' },
  depthHead: { margin: '10px 0 2px', fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold, color: palette.navy3, textTransform: 'uppercase', letterSpacing: '0.04em' },
  listGrid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)',
    gap: '16px',
    [media.medium]: { gridTemplateColumns: 'minmax(0, 1fr)' },
    [media.narrow]: { gap: '12px' },
  },
  ownerWarn: { display: 'inline-flex', alignItems: 'center', columnGap: '4px', color: palette.goldDark, fontWeight: tokens.fontWeightSemibold },
});

const edgeStyle: Record<string, { stroke: string; width: number; dash?: string }> = {
  high: { stroke: palette.navy, width: 2.5 },
  medium: { stroke: palette.navy3, width: 2 },
  low: { stroke: palette.muted, width: 1.5 },
  none: { stroke: palette.muted, width: 1.5, dash: '5 4' },
};
const critKey = (k: string | null) => (k === 'high' || k === 'medium' || k === 'low' ? k : 'none');

export function DependencyMap({ applicationId }: { applicationId: string }) {
  const s = useStyles();
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
        actions={<Button appearance="secondary" icon={<ArrowLeft16Regular />} as="a" href={host.href({ page: 'app', id: applicationId })}>Application 360</Button>}
      />

      <div className={s.controls}>
        <div className={s.control}>
          <span className={s.controlLabel}>Show</span>
          <TabList size="small" appearance="subtle-circular" selectedValue={show} onTabSelect={(_, d) => setShow(d.value as Show)} aria-label="Direction">
            <Tab value="both">Both</Tab>
            <Tab value="dependants">What breaks</Tab>
            <Tab value="dependencies">What it needs</Tab>
          </TabList>
        </div>
        <div className={s.control}>
          <span className={s.controlLabel}>View</span>
          <TabList size="small" appearance="subtle-circular" selectedValue={shownView} onTabSelect={(_, d) => setView(d.value as 'graph' | 'list')} aria-label="View">
            <Tab value="graph">Graph</Tab>
            <Tab value="list">List</Tab>
          </TabList>
        </div>
      </div>

      {bothFailed && dependants.status === 'done' && !dependants.envelope.ok ? (
        <PanelFailure failure={dependants.envelope} />
      ) : shownView === 'graph' ? (
        <Panel title="Graph" subtitle="Hover or focus an application to trace its path. Select it to open its 360.">
          <GraphBody show={show} rootId={applicationId} rootName={rootName} left={left} right={right} types={types} />
        </Panel>
      ) : (
        <div className={s.listGrid}>
          {show !== 'dependencies' && (
            <ImpactListPanel side="dependants" title={`What breaks if ${rootName} changes or retires`} state={dependants} rootId={applicationId} rootName={rootName} types={types} />
          )}
          {show !== 'dependants' && (
            <ImpactListPanel side="dependencies" title={`What ${rootName} needs`} state={dependencies} rootId={applicationId} rootName={rootName} types={types} />
          )}
        </div>
      )}

      <div className={s.counts}>
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
  const s = useStyles();
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
        <MessageBar intent="info" layout="multiline" aria-labelledby={undefined} aria-label="Note">
          <MessageBarBody>
            The walk stopped at 3 hops and there is more beyond. A deeper walk is possible up to the limit the configuration sets.
          </MessageBarBody>
        </MessageBar>
      )}
      <div className={s.scroller}>
        <div className={s.canvas} style={{ width, height }}>
          <svg className={s.svg} width={width} height={height} aria-hidden>
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
            <div key={`head-${c}`} className={s.colHead} style={{ left: PAD + (c - minCol) * (NODE_W + GAP_X), width: NODE_W }}>
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
                className={mergeClasses(s.node, isRoot && s.root, focus !== null && !lit.has(p.key) && s.dim, focus !== null && lit.has(p.key) && !isRoot && s.lit)}
                style={{ left: p.x, top: p.y, width: NODE_W, height: NODE_H }}
                onMouseEnter={() => setFocus(p.key)}
                onMouseLeave={() => setFocus(null)}
                onFocus={() => setFocus(p.key)}
                onBlur={() => setFocus(null)}
                aria-label={label}
                title={name}
              >
                <span className={s.nodeName}>{name}</span>
                <span className={s.nodeLine}>{relation}</span>
                {!isRoot && (
                  <span className={s.nodeLine}>
                    {noOwner && <Warning16Filled className={s.warnIcon} aria-hidden />}
                    {noOwner ? 'No business owner' : facts}
                  </span>
                )}
                {isRoot && <span className={s.nodeLine}>Select to open its 360</span>}
              </a>
            );
          })}
        </div>
      </div>
      <div className={s.legend} role="group" aria-label="Legend">
        <span>Criticality of the link:</span>
        {(['high', 'medium', 'low', 'none'] as const).map(k => (
          <span key={k} className={s.legendItem}>
            <svg width="30" height="10" aria-hidden>
              <line x1="1" y1="5" x2="29" y2="5" stroke={edgeStyle[k].stroke} strokeWidth={edgeStyle[k].width} strokeDasharray={edgeStyle[k].dash} strokeLinecap="round" />
            </svg>
            {k === 'none' ? 'Not set' : k.charAt(0).toUpperCase() + k.slice(1)}
          </span>
        ))}
        <span className={s.legendItem}>
          <svg width="22" height="10" aria-hidden>
            <line x1="20" y1="5" x2="6" y2="5" stroke={palette.navy3} strokeWidth="1.5" />
            <path d="M 2 5 L 8 1.5 L 8 8.5 z" fill={palette.navy3} />
          </svg>
          Arrows point to what is needed
        </span>
        <span className={s.legendItem}><Warning16Filled className={s.warnIcon} aria-hidden />No business owner</span>
      </div>
    </>
  );
}

function ImpactListPanel(props: { side: Side; title: string; state: ToolState<DependencyImpact>; rootId: string; rootName: string; types: Labels }) {
  const s = useStyles();
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
              <MessageBar intent="info" layout="multiline" aria-labelledby={undefined} aria-label="Note">
                <MessageBarBody>There is more beyond 3 hops. A deeper walk is possible up to the configured limit.</MessageBarBody>
              </MessageBar>
            )}
            {depths.map(depth => (
              <div key={depth}>
                <h3 className={s.depthHead}>{plural(depth, 'hop', 'hops')}</h3>
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
                        primary={<Link href={host.href({ page: 'app', id: n.businessApplicationId })}>{n.name}</Link>}
                        secondary={relation}
                        tertiary={owners ?? <span className={s.ownerWarn}><Warning16Filled aria-hidden />No business owner</span>}
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
  const s = useStyles();
  const host = useHost();
  const toItems = (buckets: Bucket[]) => buckets.map(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }));
  return (
    <Panel title={title} state={state} skeletonRows={4}>
      {data => (
        <>
          <div className={s.total}>
            <span className={s.totalNumber}>{data.nodes.length}</span>
            {data.nodes.length === 1 ? 'application' : 'applications'} {noun} within 3 hops{data.truncatedAtDepth ? ', and more beyond' : ''}
          </div>
          {data.nodes.length > 0 && <div className={s.countCols}>
            <div>
              <h3 className={s.countHead}>By tier</h3>
              <BarList ariaLabel={`${title} by tier`} noun={['application', 'applications']} labelWidth={90} items={toItems(data.counts.byTier)} onDrill={b => host.drill(`${title}: ${b.label}`)} />
            </div>
            <div>
              <h3 className={s.countHead}>By criticality</h3>
              <BarList ariaLabel={`${title} by criticality`} noun={['link', 'links']} labelWidth={90} items={toItems(data.counts.byCriticality)} onDrill={b => host.drill(`${title}: ${b.label} criticality`)} />
            </div>
          </div>}
        </>
      )}
    </Panel>
  );
}
