// The Domains tab (/app/data): the business data tree drawn for this install. Wide: a tree table, each branch collapsible.
// Phone: a drill-down list (at=<key> opens a branch), or a flat list of matches while a filter is on.
// Page parameters: class=<key>, level=<key>, gaps=1, q=<text>, at=<key>.
import { ChevronDown, ChevronLeft, ChevronRight, Pencil, Plus, UserPlus } from 'lucide-react';
import { useState } from 'react';
import { domainHref, useQueryParam, withParams } from '../../app/links';
import { navigate } from '../../app/navigation';
import { Empty, Muted, Panel, PanelFailure, TextLink, useNarrow } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import type { DataDomainItem } from '../../data/contracts';
import { useLabels } from '../../data/labels';
import { useSession } from '../../data/session';
import { cn } from '../../lib/utils';
import { ConfigurationChangeSheet } from '../configuration/sheets';
import { Chip } from '../deployments-places/parts';
import { ancestorsOf, domainRows, drillRows, mayProposeDomainOwner, type DomainFilters, type DomainRow } from './logic';
import { ClassificationCell, CountLinks, FilterLabel, filterSection, GapBadges, KEY, NeverMark, NoDomainsYet, OwnerLines, RuleChips, SearchBox, TEXT, useParams, type DataContext } from './parts';
import { SheetHoldContext, useHeld, useHoldCount } from './sheetParts';
import { ProposeDataOwnerSheet } from './sheets';

export function DomainsTab({ ctx }: { ctx: DataContext }) {
  const narrow = useNarrow();
  const session = useSession();
  const setParams = useParams();
  const classes = useLabels('data-classification');
  const levels = useLabels('data-domain-level');
  const classKey = useQueryParam('class', KEY);
  const levelKey = useQueryParam('level', KEY);
  const gapsOnly = useQueryParam('gaps', /^1$/) === '1';
  const text = useQueryParam('q', TEXT);
  const at = useQueryParam('at', KEY);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [holds, hold] = useHoldCount();
  const tree = useHeld(ctx.tree, holds > 0);

  const filters: DomainFilters = { classificationKey: classKey, levelKey, gapsOnly, text, scopeKeys: ctx.scope.domainKeys };
  const pageFilter = !!classKey || !!levelKey || gapsOnly || !!text;
  const anyFilter = pageFilter || !!ctx.scope.domainKeys;
  const result = tree ? domainRows(tree, filters, collapsed) : null;
  const toggle = (key: string) => setCollapsed(s => {
    const next = new Set(s);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    return next;
  });
  const clearPage = () => setParams({ class: undefined, level: undefined, gaps: undefined, q: undefined, at: undefined });
  const canChangeTree = session.can('propose_configuration_change');

  return (
    <SheetHoldContext.Provider value={hold}>
      <section aria-label="Domain filters" className={filterSection}>
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="domains-class-label">Classification</FilterLabel>
            <Select value={classKey ?? 'all'} onValueChange={v => setParams({ class: v === 'all' ? undefined : v, at: undefined })}>
              <SelectTrigger className="w-[200px] narrow:w-full" aria-labelledby="domains-class-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Every classification</SelectItem>
                {classes.keys().map(k => <SelectItem key={k} value={k}>{classes.label(k)}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="domains-level-label">Level</FilterLabel>
            <Select value={levelKey ?? 'all'} onValueChange={v => setParams({ level: v === 'all' ? undefined : v, at: undefined })}>
              <SelectTrigger className="w-[180px] narrow:w-full" aria-labelledby="domains-level-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Every level</SelectItem>
                {levels.keys().map(k => <SelectItem key={k} value={k}>{levels.label(k)}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <SearchBox value={text} placeholder="Domain or owner" label="Search the domains by name, path or owner" onChange={q => setParams({ q, at: undefined })} />
          <div className="flex items-center gap-2 pb-2">
            <Checkbox id="domains-gaps" checked={gapsOnly} onCheckedChange={v => setParams({ gaps: v === true ? '1' : undefined, at: undefined })} />
            <Label htmlFor="domains-gaps" className="font-normal">Only domains with an owner gap</Label>
          </div>
        </div>
        {pageFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active domain filters">
            {classKey && <Chip label={`Classification: ${classes.label(classKey)}`} onRemove={() => setParams({ class: undefined })} />}
            {levelKey && <Chip label={`Level: ${levels.label(levelKey)}`} onRemove={() => setParams({ level: undefined })} />}
            {gapsOnly && <Chip label="With an owner gap" onRemove={() => setParams({ gaps: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => setParams({ q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearPage}>Clear these</Button>
          </div>
        )}
      </section>

      {ctx.domainsFailure ? <PanelFailure failure={ctx.domainsFailure} /> : (
        <Panel
          id="data-domain-tree"
          title="Domain tree"
          count={result?.matchedCount}
          subtitle={anyFilter ? 'The domains that match, with the domains above them for context' : 'Every business data domain, with its effective classification, owners and rules'}
          actions={(
            <>
              {!narrow && result && result.rows.some(r => r.hasChildren) && (
                collapsed.size > 0
                  ? <Button variant="ghost" size="sm" onClick={() => setCollapsed(new Set())}>Expand all</Button>
                  : <Button variant="ghost" size="sm" onClick={() => setCollapsed(new Set(result.rows.filter(r => r.hasChildren).map(r => r.key)))}>Collapse all</Button>
              )}
              {canChangeTree && (
                <ConfigurationChangeSheet
                  title="Propose a change to the data domain tree"
                  rationaleHint="Such as a new subdomain, a split, a stricter AI use rule on a branch, or a new classification."
                  trigger={<Button variant="outline" size="sm"><Plus aria-hidden />Propose a tree change</Button>}
                />
              )}
            </>
          )}
        >
          {!ctx.domainsLoaded || !tree ? <Muted>Reading the domain tree.</Muted>
            : tree.order.length === 0 ? <NoDomainsYet />
              : !result || result.matchedCount === 0 ? (
                <span className="flex flex-col items-start gap-2">
                  <Empty>{ctx.route.mine ? 'You own, deputize or steward no domain that matches.' : 'No domain matches these filters.'}</Empty>
                  {pageFilter && <Button variant="outline" size="sm" onClick={clearPage}>Clear the filters</Button>}
                </span>
              ) : narrow ? (
                pageFilter || ctx.scope.domainKeys
                  ? <FlatList rows={result.rows.filter(r => r.matched)} ctx={ctx} />
                  : <DrillList atKey={at && tree.nodes.has(at) ? at : null} filters={filters} ctx={ctx} />
              ) : <TreeTable rows={result.rows} ctx={ctx} onToggle={toggle} canChangeTree={canChangeTree} />}
        </Panel>
      )}
    </SheetHoldContext.Provider>
  );
}

function ProposeOwnerButton({ item, ctx, compact }: { item: DataDomainItem; ctx: DataContext; compact?: boolean }) {
  const session = useSession();
  if (!ctx.tree || !session.can('propose_owner_change') || !mayProposeDomainOwner(ctx.me, ctx.tree, item.key)) return null;
  const missing = item.gaps.includes('data.no-owner') || item.gaps.includes('data.own-owner-missing');
  const label = `Propose an owner for ${item.path}`;
  return (
    <ProposeDataOwnerSheet
      subject={{ kind: 'data-domain', domainKey: item.key, label: item.path }}
      ownerRoleKey={missing ? 'data-owner' : undefined}
      trigger={missing || compact === false
        ? <Button variant={missing ? 'default' : 'outline'} size="sm"><UserPlus aria-hidden />Propose an owner</Button>
        : <Button variant="ghost" size="icon-sm" aria-label={label} title={label}><UserPlus aria-hidden /></Button>}
    />
  );
}

function ChangeDomainButton({ item }: { item: DataDomainItem }) {
  const own = (n: 'use' | 'ai' | 'copy') => (item.rules[n].setOn?.key === item.key ? item.rules[n].value : undefined);
  const values: Record<string, unknown> = { label: item.label };
  if (item.parentKey) values.parentKey = item.parentKey;
  if (item.level) values.levelKey = item.level.key;
  if (item.classification && item.classification.setOn.key === item.key) values.classificationKey = item.classification.key;
  if (own('use')) values.useRule = own('use');
  if (own('ai')) values.aiUseRule = own('ai');
  if (own('copy')) values.copyToTestRule = own('copy');
  const label = `Propose a change to ${item.path}`;
  return (
    <ConfigurationChangeSheet
      title={label}
      operations={[{ op: 'upsert', entity: 'data-domain', key: item.key, values }]}
      rationaleHint="Such as a new name, a stricter rule for this branch, or a higher classification."
      trigger={<Button variant="ghost" size="icon-sm" aria-label={label} title={label}><Pencil aria-hidden /></Button>}
    />
  );
}

function TreeTable({ rows, ctx, onToggle, canChangeTree }: { rows: DomainRow[]; ctx: DataContext; onToggle: (key: string) => void; canChangeTree: boolean }) {
  return (
    <div className="-mx-1 overflow-x-auto">
      <table className="w-full min-w-[880px] border-collapse text-sm" aria-label="The data domain tree">
        <thead>
          <tr className="border-b border-line text-left text-xs text-navy-3">
            <th scope="col" className="py-2 pr-3 pl-1 font-medium">Domain</th>
            <th scope="col" className="px-3 py-2 font-medium">Owners</th>
            <th scope="col" className="px-3 py-2 font-medium">Rules</th>
            <th scope="col" className="px-3 py-2 font-medium">Data and gaps</th>
            <th scope="col" className="py-2 pr-1 pl-3 text-right font-medium"><span className="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {rows.map(r => {
            const item = r.item;
            return (
              <tr key={r.key} className={cn('border-b border-line align-top last:border-b-0', !r.matched && 'bg-background/60')}>
                <th scope="row" className="py-2.5 pr-3 pl-1 text-left font-normal">
                  <div className="flex items-start gap-1" style={{ paddingLeft: `${r.depth * 20}px` }}>
                    {r.hasChildren ? (
                      <Button
                        variant="ghost"
                        size="icon-xs"
                        className="mt-0.5"
                        aria-expanded={!r.collapsed}
                        aria-label={`${r.collapsed ? 'Show' : 'Hide'} the domains under ${item.label}`}
                        onClick={() => onToggle(r.key)}
                      >
                        {r.collapsed ? <ChevronRight aria-hidden /> : <ChevronDown aria-hidden />}
                      </Button>
                    ) : <span className="inline-block size-6 shrink-0" aria-hidden />}
                    <div className="flex min-w-0 flex-col gap-1">
                      <span className="flex items-center gap-1.5">
                        <TextLink href={domainHref(item.key)} className={cn('text-sm', !r.matched && 'text-navy-2')}>{item.label}</TextLink>
                        <NeverMark item={item} />
                      </span>
                      <Muted>{[item.level?.label ?? 'No level', !item.declarable ? 'takes no data uses' : null, !r.matched ? 'shown for the domains below it' : null].filter(Boolean).join(', ')}</Muted>
                      <ClassificationCell item={item} />
                    </div>
                  </div>
                </th>
                <td className="px-3 py-2.5"><OwnerLines item={item} /></td>
                <td className="px-3 py-2.5"><RuleChips item={item} /></td>
                <td className="px-3 py-2.5">
                  <div className="flex flex-col gap-1.5">
                    <CountLinks item={item} route={ctx.route} />
                    <GapBadges item={item} route={ctx.route} />
                  </div>
                </td>
                <td className="py-2.5 pr-1 pl-3 text-right">
                  <div className="flex flex-wrap items-center justify-end gap-1">
                    <ProposeOwnerButton item={item} ctx={ctx} />
                    {canChangeTree && <ChangeDomainButton item={item} />}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// One domain as a phone row.
function DomainPhoneRow({ item, ctx, below, onOpen, contextOnly }: { item: DataDomainItem; ctx: DataContext; below?: number; onOpen?: () => void; contextOnly?: boolean }) {
  return (
    <Row
      wideEnd
      primary={<span className="inline-flex items-center gap-1.5"><TextLink href={domainHref(item.key)}>{item.label}</TextLink><NeverMark item={item} /></span>}
      secondary={[item.level?.label ?? 'No level', contextOnly ? 'shown for the domains below it' : null].filter(Boolean).join(', ')}
      tertiary={<OwnerLines item={item} compact />}
      badges={(
        <>
          <ClassificationCell item={item} />
          <RuleChips item={item} />
          <GapBadges item={item} route={ctx.route} />
          <CountLinks item={item} route={ctx.route} />
        </>
      )}
      end={(
        <span className="flex flex-wrap items-center gap-1.5">
          <ProposeOwnerButton item={item} ctx={ctx} compact={false} />
          {!!below && onOpen && (
            <Button variant="outline" size="sm" onClick={onOpen} aria-label={`Open the ${below} domains under ${item.label}`}>
              {`${below} below`}
              <ChevronRight aria-hidden />
            </Button>
          )}
        </span>
      )}
    />
  );
}

// The phone's drill-down: one level at a time, with the path back up.
function DrillList({ atKey, filters, ctx }: { atKey: string | null; filters: DomainFilters; ctx: DataContext }) {
  const tree = ctx.tree!;
  const rows = drillRows(tree, filters, atKey);
  const up = atKey ? ancestorsOf(tree, atKey) : [];
  // A step down or up the tree is a new address, so Back goes up a level again. The tree comes into view, and the
  // focus moves to where the person now is.
  const open = (key: string | null) => {
    navigate(withParams(`${window.location.pathname}${window.location.search}`, { at: key ?? undefined }));
    window.setTimeout(() => {
      const panel = document.getElementById('data-domain-tree');
      panel?.scrollIntoView({ block: 'start' });
      (panel?.querySelector('[aria-current="location"]') as HTMLElement | null ?? panel?.querySelector('h2') as HTMLElement | null)?.focus({ preventScroll: true });
    }, 0);
  };
  const here = atKey ? tree.nodes.get(atKey)!.item : null;
  return (
    <div className="flex flex-col gap-3">
      {here && (
        <nav aria-label="Where you are in the tree" className="flex flex-col gap-2">
          <ol className="m-0 flex list-none flex-wrap items-center gap-1 p-0 text-sm">
            <li><Button variant="link" size="sm" className="h-auto px-0" onClick={() => open(null)}>All domains</Button></li>
            {up.map(k => (
              <li key={k} className="flex items-center gap-1">
                <ChevronRight className="size-3.5 text-navy-3" aria-hidden />
                <Button variant="link" size="sm" className="h-auto px-0" onClick={() => open(k)}>{tree.nodes.get(k)!.item.label}</Button>
              </li>
            ))}
            <li className="flex items-center gap-1 rounded-sm outline-none focus-visible:outline-2 focus-visible:outline-ring" aria-current="location" tabIndex={-1}>
              <ChevronRight className="size-3.5 text-navy-3" aria-hidden />
              <span className="font-semibold text-navy">{here.label}</span>
            </li>
          </ol>
          <Button variant="outline" size="sm" className="self-start" onClick={() => open(tree.nodes.get(atKey!)!.parentKey)}>
            <ChevronLeft aria-hidden />Up a level
          </Button>
          <RowList label={here.label}>
            <DomainPhoneRow item={here} ctx={ctx} />
          </RowList>
          <span className="m-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{`Under ${here.label}`}</span>
        </nav>
      )}
      {rows.length ? (
        <RowList label={here ? `Domains under ${here.label}` : 'Top-level domains'}>
          {rows.map(r => <DomainPhoneRow key={r.item.key} item={r.item} ctx={ctx} below={r.below} onOpen={() => open(r.item.key)} contextOnly={!r.matched} />)}
        </RowList>
      ) : <Empty>No domain below this one.</Empty>}
      {!here && <Muted>Open a domain's branch to see the domains under it.</Muted>}
    </div>
  );
}

// The phone's list while a filter is on: every matching domain with its path.
function FlatList({ rows, ctx }: { rows: DomainRow[]; ctx: DataContext }) {
  return (
    <RowList label="Matching domains">
      {rows.map(r => (
        <Row
          key={r.key}
          wideEnd
          primary={<span className="inline-flex items-center gap-1.5"><TextLink href={domainHref(r.key)}>{r.item.path || r.item.label}</TextLink><NeverMark item={r.item} /></span>}
          secondary={r.item.level?.label ?? 'No level'}
          tertiary={<OwnerLines item={r.item} compact />}
          badges={(
            <>
              <ClassificationCell item={r.item} />
              <RuleChips item={r.item} />
              <GapBadges item={r.item} route={ctx.route} />
              <CountLinks item={r.item} route={ctx.route} />
            </>
          )}
          end={<ProposeOwnerButton item={r.item} ctx={ctx} compact={false} />}
        />
      ))}
    </RowList>
  );
}
