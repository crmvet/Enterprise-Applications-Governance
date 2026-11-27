// The Mapping tab (/app/data/mapping): where each domain's data sits, as a matrix of domains (the tree) against the
// locations that hold them, grouped by stage; or (by=apps) domains against the applications that use them. A cell opens
// its details below the matrix (mapping=<id> or use=<id>), with its acts. At phone width both become lists by domain.
// Page parameters: by=apps, stage=<key>, mapping=<id>, use=<id>, q=<text>.
// Wave 4: the cells are the page's one list_data_mappings and list_data_uses reads (ctx.mappings, ctx.uses), not a read
// per domain. The search keeps the cells whose domain, location, platform, stage or origin (or, against applications,
// whose application, domain, location or purpose) hold the words, and the matrix keeps only the domains with a cell left,
// so a search reads as a short list. Its count in the tab bar is the mappings in the shared scope.
import { Bot, TriangleAlert } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { domainHref, useQueryParam } from '../../app/links';
import { Empty, Muted, Panel, PanelFailure, SwitchTabs, TextLink, ToneBadge, useNarrow } from '../../components/blocks';
import { DataOriginBadge, DataUseStateBadge } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import type { DataMappingItem, DataUseItem } from '../../data/contracts';
import { DATA_ACCESS_SHORT, DATA_ACCESS_TEXT, DATA_ORIGIN_TEXT, DATA_USE_STATE_TEXT, DATA_USE_STATE_TONE, needsAttention } from '../../data/dataTaxonomy';
import { plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { cn } from '../../lib/utils';
import {
  appColumns,
  cellKey,
  copiesToWatch,
  domainRows,
  mappingCells,
  mappingMatches,
  matrixColumns,
  sameId,
  useCells,
  useMatches,
  type DomainRow,
  type MatrixColumnGroup,
} from './logic';
import { ListSearch, useListText } from '../../components/listSearch';
import { Chip } from '../deployments-places/parts';
import { appHref, column, filterSection, GUID, KEY, ListNotes, locationHref, NoDomainsYet, STATE_SHORT, twoColumns, useParams, type DataContext } from './parts';
import { LocationName, MappingActionButtons, MappingFacts, mappingLabel, UseActionButtons, UseFacts, useLabel } from './rowActions';
import { SheetHoldContext, useHeld, useHoldCount } from './sheetParts';

export function MappingTab({ ctx }: { ctx: DataContext }) {
  const narrow = useNarrow();
  const setParams = useParams();
  const stages = useLabels('target-stage');
  const byApps = useQueryParam('by', /^apps$/) === 'apps';
  const stageKey = useQueryParam('stage', KEY);
  const selectedMapping = useQueryParam('mapping', GUID);
  const selectedUse = useQueryParam('use', GUID);
  const text = useListText();
  const [holds, hold] = useHoldCount();
  const allMappings = useHeld(ctx.mappings.items, holds > 0);
  const allUses = useHeld(ctx.uses.items, holds > 0);

  const scoped = allMappings.filter(m => mappingMatches(m, { domainKeys: ctx.scope.domainKeys, locationIds: ctx.scope.locationIds }));
  const mappings = scoped.filter(m => mappingMatches(m, { stageKey, text }));
  const uses = allUses.filter(u => useMatches(u, { domainKeys: ctx.scope.domainKeys, applicationId: ctx.route.applicationId, locationIds: ctx.scope.locationIds, text }));
  const allRows = ctx.tree ? domainRows(ctx.tree, { scopeKeys: ctx.scope.domainKeys }).rows : [];
  // With a search, only the domains that keep a cell; without one, every domain in scope, as the tree draws them.
  const rows = text ? allRows.filter(r => (byApps ? uses : mappings).some(x => x.domain.key === r.key)) : allRows;
  const pickedMapping = selectedMapping ? allMappings.find(m => sameId(m.dataMappingId, selectedMapping)) ?? null : null;
  const pickedUse = selectedUse ? allUses.find(u => sameId(u.dataUseId, selectedUse)) ?? null : null;
  const copies = copiesToWatch(mappings);
  const stageKeys = [...new Set(scoped.flatMap(m => (m.location.stage ? [m.location.stage.key] : [])))].sort((a, b) => stages.rank(a) - stages.rank(b));
  const loading = !ctx.domainsLoaded || !ctx.mappings.loaded || !ctx.uses.loaded;
  const failure = ctx.domainsFailure ?? ctx.mappings.failure ?? ctx.uses.failure;
  // A picked cell opens its details under the matrix: bring them into view and move the focus there.
  const selectedRef = useRef<HTMLDivElement>(null);
  const pickedId = pickedMapping?.dataMappingId ?? pickedUse?.dataUseId ?? null;
  useEffect(() => {
    if (!pickedId) return;
    selectedRef.current?.scrollIntoView({ block: 'nearest' });
    selectedRef.current?.focus({ preventScroll: true });
  }, [pickedId]);

  return (
    <SheetHoldContext.Provider value={hold}>
      <section aria-label="Mapping view" className={filterSection}>
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2.5">
          <SwitchTabs<'locations' | 'apps'>
            label="Domains against"
            ariaLabel="Domains against locations or applications"
            value={byApps ? 'apps' : 'locations'}
            onChange={v => setParams({ by: v === 'apps' ? 'apps' : undefined, mapping: undefined, use: undefined })}
            options={[{ value: 'locations', label: 'Locations' }, { value: 'apps', label: 'Applications' }]}
          />
          {!byApps && stageKeys.length > 1 && (
            <SwitchTabs<string>
              label="Stage"
              ariaLabel="Stage"
              value={stageKey ?? 'all'}
              onChange={v => setParams({ stage: v === 'all' ? undefined : v, mapping: undefined })}
              options={[{ value: 'all', label: 'All' }, ...stageKeys.map(k => ({ value: k, label: stages.label(k) }))]}
            />
          )}
        </div>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <ListSearch
            label={byApps ? 'Search the uses by application, data, location or purpose' : 'Search where the data sits by domain, location, platform, stage or origin'}
            placeholder={byApps ? 'Application, data or location' : 'Domain, location or platform'}
            resets={['mapping', 'use']}
          />
          <Muted>
            {byApps
              ? 'Each cell is a use: R reads, W writes, RW both, with the AI mark and the decision state.'
              : 'Each cell is where the data sits: SoR the system of record, Replica, Copy (of production), Masked or Synthetic.'}
          </Muted>
        </div>
        {text && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active mapping filters">
            <Chip label={`Matching "${text}"`} onRemove={() => setParams({ q: undefined })} />
          </div>
        )}
      </section>

      {failure ? <PanelFailure failure={failure} /> : (
        <div className="flex flex-col gap-4 narrow:gap-3">
          <Panel
            title={byApps ? 'Domains against applications' : 'Domains against locations'}
            count={byApps ? uses.length : mappings.length}
            subtitle={byApps ? 'The uses of each domain, by application' : 'Where each domain\'s data sits, by stage'}
          >
            {loading ? <Muted>Reading where the data sits.</Muted>
              : ctx.tree && ctx.tree.order.length === 0 ? <NoDomainsYet />
                : (byApps ? uses.length === 0 : mappings.length === 0) ? (
                  text || (!byApps && stageKey) ? (
                    <span className="flex flex-col items-start gap-2">
                      <Empty>{byApps ? 'No use matches this search.' : 'No mapping matches these filters.'}</Empty>
                      <Button variant="outline" size="sm" onClick={() => setParams({ q: undefined, stage: undefined })}>Clear the filters</Button>
                    </span>
                  ) : <Empty>{byApps ? 'No application declares a use of this data yet.' : 'No data is mapped to a location in this scope yet. Map it from the Locations tab.'}</Empty>
                ) : narrow
                  ? (byApps ? <UseList rows={rows} uses={uses} onPick={id => setParams({ use: id, mapping: undefined })} /> : <MappingList rows={rows} mappings={mappings} onPick={id => setParams({ mapping: id, use: undefined })} />)
                  : byApps
                    ? <AppMatrix rows={rows} uses={uses} selected={selectedUse} onPick={id => setParams({ use: id, mapping: undefined })} />
                    : <LocationMatrix rows={rows} groups={matrixColumns(mappings, k => stages.rank(k))} mappings={mappings} selected={selectedMapping} onPick={id => setParams({ mapping: id, use: undefined })} />}
            <ListNotes reads={byApps ? [ctx.uses] : [ctx.mappings]} one={byApps ? 'use' : 'mapping'} many={byApps ? 'uses' : 'mappings'} />
          </Panel>

          {(pickedMapping || pickedUse) && (
            <div ref={selectedRef} tabIndex={-1} className="outline-none" aria-live="polite">
            <Panel
              title={pickedMapping ? mappingLabel(pickedMapping) : useLabel(pickedUse!)}
              subtitle={pickedMapping ? 'The selected mapping' : 'The selected use'}
              actions={<Button variant="ghost" size="sm" onClick={() => setParams({ mapping: undefined, use: undefined })}>Close</Button>}
            >
              {pickedMapping ? (
                <>
                  <MappingFacts mapping={pickedMapping} sourceLabel={sourceLabelOf(pickedMapping, allMappings)} />
                  <MappingActionButtons mapping={pickedMapping} ctx={ctx} full />
                </>
              ) : (
                <>
                  <UseFacts use={pickedUse!} />
                  <UseActionButtons use={pickedUse!} ctx={ctx} full />
                </>
              )}
            </Panel>
            </div>
          )}

          {!byApps && (
            <div className={twoColumns}>
              <Panel title="Copies outside production" count={loading ? undefined : copies.length} subtitle="Copies of production data in test or development, judged by the copy rule">
                {loading ? <Muted>Reading the copies.</Muted> : copies.length ? (
                  <RowList label="Copies outside production">
                    {copies.map(m => (
                      <Row
                        key={m.dataMappingId}
                        wideEnd
                        primary={<TextLink href={domainHref(m.domain.key)}>{m.domain.path}</TextLink>}
                        secondary={<>{DATA_ORIGIN_TEXT[m.origin]} at <LocationName location={m.location} />{m.location.stage ? ` (${m.location.stage.label})` : ''}</>}
                        badges={m.copyState ? <DataUseStateBadge state={m.copyState} approvalsNeeded={m.approvalsNeeded} /> : undefined}
                        end={<MappingActionButtons mapping={m} ctx={ctx} />}
                      />
                    ))}
                  </RowList>
                ) : <Empty>No copy of production data sits outside production in this scope.</Empty>}
              </Panel>
              <div className={column}>
                <Panel title="How a copy is judged">
                  <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm leading-relaxed text-navy-2">
                    <li>The copy rule of the domain, or of its classification, decides: allowed, decision, masked only or never.</li>
                    <li>A copy whose rule is never is refused when it is saved. An unmasked copy where only masked copies may go breaks the rule.</li>
                    <li>The fix happens in the platform's own tools: mask or delete the copy. Then change the mapping's origin, or end it.</li>
                  </ul>
                </Panel>
              </div>
            </div>
          )}
        </div>
      )}
    </SheetHoldContext.Provider>
  );
}

function sourceLabelOf(m: DataMappingItem, all: DataMappingItem[]): string | null {
  const source = m.copiedFromMappingId ? all.find(x => sameId(x.dataMappingId, m.copiedFromMappingId)) : null;
  return source ? `${source.location.label}, ${DATA_ORIGIN_TEXT[source.origin].toLowerCase()}` : null;
}

const cellButton = 'inline-flex max-w-full cursor-pointer items-center gap-1 rounded-md border-0 bg-transparent p-0.5 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring';

function DomainHeader({ row }: { row: DomainRow }) {
  return (
    <th scope="row" className="sticky left-0 z-[1] min-w-[180px] max-w-[220px] border-b border-line bg-card py-2 pr-3 text-left align-top font-normal">
      <span className="flex flex-col" style={{ paddingLeft: `${row.depth * 14}px` }}>
        <TextLink href={domainHref(row.key)} className="text-sm">{row.item.label}</TextLink>
        <span className="text-[11px] text-navy-3">{row.item.level?.label ?? ''}</span>
      </span>
    </th>
  );
}

function LocationMatrix({ rows, groups, mappings, selected, onPick }: { rows: DomainRow[]; groups: MatrixColumnGroup[]; mappings: DataMappingItem[]; selected: string | undefined; onPick: (id: string) => void }) {
  const cells = mappingCells(mappings);
  const columns = groups.flatMap(g => g.columns);
  return (
    <div className="-mx-1 overflow-x-auto pb-1">
      <table className="border-separate border-spacing-0 text-sm">
        <caption className="sr-only">Domains against the locations that hold their data, grouped by stage. Each cell names where the data comes from.</caption>
        <thead>
          <tr>
            <th scope="col" rowSpan={2} className="sticky left-0 z-[2] border-b border-line bg-card py-2 pr-3 text-left align-bottom text-xs font-medium text-navy-3">Domain</th>
            {groups.map(g => (
              <th key={g.stage?.key ?? 'none'} scope="colgroup" colSpan={g.columns.length} className="border-b border-l border-line px-2 py-1.5 text-left text-xs font-semibold text-navy">
                {g.stage?.label ?? 'No stage'}
              </th>
            ))}
          </tr>
          <tr>
            {groups.flatMap((g, gi) => g.columns.map((c, ci) => {
              const href = locationHref({ dataLocationId: c.id });
              return (
                <th key={c.id} scope="col" className={cn('min-w-[78px] max-w-[120px] border-b border-line px-1.5 py-1.5 text-left align-bottom text-xs font-medium', ci === 0 && 'border-l', gi === 0 && ci === 0 && 'border-l')}>
                  <span className="flex flex-col gap-0.5">
                    {href ? <TextLink href={href} className="text-xs [overflow-wrap:anywhere]">{c.label}</TextLink> : <span className="[overflow-wrap:anywhere] text-navy">{c.label}</span>}
                    {c.platform && <span className="text-[11px] font-normal text-navy-3">{c.platform.label}</span>}
                  </span>
                </th>
              );
            }))}
          </tr>
        </thead>
        <tbody>
          {rows.map(r => (
            <tr key={r.key} className="align-top">
              <DomainHeader row={r} />
              {columns.map((c, i) => {
                const list = cells.get(cellKey(r.key, c.id)) ?? [];
                const groupStart = groups.some(g => g.columns[0]?.id === c.id);
                return (
                  <td key={c.id} className={cn('border-b border-line px-1.5 py-1.5', (groupStart || i === 0) && 'border-l')}>
                    <span className="flex flex-col items-start gap-0.5">
                      {list.map(m => {
                        const attention = !!m.copyState && needsAttention(m.copyState);
                        const words = `${r.item.path} at ${c.label}: ${DATA_ORIGIN_TEXT[m.origin].toLowerCase()}${m.copyState ? `, ${DATA_USE_STATE_TEXT[m.copyState].toLowerCase()}` : ''}. Show its details.`;
                        return (
                          <button key={m.dataMappingId} type="button" className={cn(cellButton, sameId(selected, m.dataMappingId) && 'ring-2 ring-primary')} aria-label={words} aria-pressed={sameId(selected, m.dataMappingId)} onClick={() => onPick(m.dataMappingId.toLowerCase())}>
                            <DataOriginBadge origin={m.origin} short />
                            {attention && <TriangleAlert className="size-3.5 shrink-0 text-warning" aria-hidden />}
                          </button>
                        );
                      })}
                    </span>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function AppMatrix({ rows, uses, selected, onPick }: { rows: DomainRow[]; uses: DataUseItem[]; selected: string | undefined; onPick: (id: string) => void }) {
  const columns = appColumns(uses);
  const cells = useCells(uses);
  return (
    <div className="-mx-1 overflow-x-auto pb-1">
      <table className="border-separate border-spacing-0 text-sm">
        <caption className="sr-only">Domains against the applications that use them. Each cell names the access, whether the data reaches AI, and the decision state.</caption>
        <thead>
          <tr>
            <th scope="col" className="sticky left-0 z-[2] border-b border-line bg-card py-2 pr-3 text-left align-bottom text-xs font-medium text-navy-3">Domain</th>
            {columns.map(c => {
              const href = appHref(c.id);
              return (
                <th key={c.id} scope="col" className="min-w-[96px] max-w-[140px] border-b border-l border-line px-1.5 py-1.5 text-left align-bottom text-xs font-medium">
                  {href ? <TextLink href={href} className="text-xs [overflow-wrap:anywhere]">{c.label}</TextLink> : c.label}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map(r => (
            <tr key={r.key} className="align-top">
              <DomainHeader row={r} />
              {columns.map(c => {
                const list = cells.get(cellKey(r.key, c.id)) ?? [];
                return (
                  <td key={c.id} className="border-b border-l border-line px-1.5 py-1.5">
                    <span className="flex flex-col items-start gap-1">
                      {list.map(u => {
                        const words = `${c.label} ${DATA_ACCESS_TEXT[u.access].toLowerCase()} ${r.item.path}${u.reachesAi ? ', with AI' : ''}: ${DATA_USE_STATE_TEXT[u.state].toLowerCase()}. Show its details.`;
                        return (
                          <button key={u.dataUseId} type="button" className={cn(cellButton, 'flex-wrap', sameId(selected, u.dataUseId) && 'ring-2 ring-primary')} aria-label={words} aria-pressed={sameId(selected, u.dataUseId)} onClick={() => onPick(u.dataUseId.toLowerCase())}>
                            <span className="font-mono text-xs font-semibold text-navy" aria-hidden>{DATA_ACCESS_SHORT[u.access]}</span>
                            {u.reachesAi && <Bot className="size-3.5 text-info" aria-hidden />}
                            <ToneBadge tone={DATA_USE_STATE_TONE[u.state]} className="text-[11px]">{STATE_SHORT[u.state]}</ToneBadge>
                          </button>
                        );
                      })}
                    </span>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// At phone width: each domain with data, and its mappings.
function MappingList({ rows, mappings, onPick }: { rows: DomainRow[]; mappings: DataMappingItem[]; onPick: (id: string) => void }) {
  const cells = mappingCells(mappings);
  const shown = rows.filter(r => mappings.some(m => m.domain.key === r.key));
  return (
    <div className="flex flex-col gap-3">
      {shown.map(r => {
        const list = [...cells.entries()].filter(([k]) => k.startsWith(`${r.key}|`)).flatMap(([, v]) => v);
        return (
          <section key={r.key} aria-label={r.item.path} className="flex flex-col gap-1">
            <TextLink href={domainHref(r.key)} className="text-sm">{r.item.path}</TextLink>
            <RowList label={`Where ${r.item.path} sits`}>
              {list.map(m => (
                <Row
                  key={m.dataMappingId}
                  primary={m.location.label}
                  secondary={`${m.location.stage?.label ?? 'No stage'}. ${DATA_ORIGIN_TEXT[m.origin]}.`}
                  badges={m.copyState ? <DataUseStateBadge state={m.copyState} approvalsNeeded={m.approvalsNeeded} /> : undefined}
                  end={<Button variant="outline" size="sm" onClick={() => onPick(m.dataMappingId.toLowerCase())} aria-label={`Details of ${m.domain.path} at ${m.location.label}`}>Details</Button>}
                />
              ))}
            </RowList>
          </section>
        );
      })}
      <Muted>{plural(mappings.length, 'mapping', 'mappings')} shown.</Muted>
    </div>
  );
}

function UseList({ rows, uses, onPick }: { rows: DomainRow[]; uses: DataUseItem[]; onPick: (id: string) => void }) {
  const shown = rows.filter(r => uses.some(u => u.domain.key === r.key));
  return (
    <div className="flex flex-col gap-3">
      {shown.map(r => (
        <section key={r.key} aria-label={r.item.path} className="flex flex-col gap-1">
          <TextLink href={domainHref(r.key)} className="text-sm">{r.item.path}</TextLink>
          <RowList label={`Uses of ${r.item.path}`}>
            {uses.filter(u => u.domain.key === r.key).map(u => (
              <Row
                key={u.dataUseId}
                primary={u.applicationName}
                secondary={`${DATA_ACCESS_TEXT[u.access]}${u.reachesAi ? ', with AI' : ''}${u.location ? ` at ${u.location.label}` : ''}.`}
                badges={<DataUseStateBadge state={u.state} approvalsGiven={u.approvalsGiven} approvalsNeeded={u.approvalsNeeded} />}
                end={<Button variant="outline" size="sm" onClick={() => onPick(u.dataUseId.toLowerCase())} aria-label={`Details of ${u.applicationName} using ${u.domain.path}`}>Details</Button>}
              />
            ))}
          </RowList>
        </section>
      ))}
    </div>
  );
}
