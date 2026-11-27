// The Gaps tab (/app/data/gaps): the data checks DC-01 to DC-14 in the scope of the shared filters, by check, each finding
// with the one act that fixes it. Page parameters: check=DC-05, severity=high|medium|low, q=<text>.
// Wave 4 (D-41):
// - The search keeps the findings whose sentence, check, domain, application, location or person hold the words; the
//   severity and check counts follow it, so each count opens its rows.
// - The use or copy a fix acts on comes from the page's one list of uses and mappings (ctx.uses, ctx.mappings); a row
//   that list does not hold is read by its ID (list_data_uses dataUseIds, list_data_mappings dataMappingIds), never a
//   read per domain.
// - DC-10 shows the engine's offer ("Reads data from Payroll Hub: declare?") with a button per domain the other
//   application holds, each opening the declare sheet on that domain.
// - Under domains-required, the application-level data owners left from before are listed for clean-up, each with
//   "End" through an owner change (get_data_gaps appLevelDataOwners); the list shows only when it has rows.
import { CircleCheck, Lightbulb } from 'lucide-react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../../app/links';
import { Empty, Muted, Panel, PanelFailure, TextLink, ToneBadge } from '../../components/blocks';
import { DataSeverityBadge, DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { ListSearch, useListText } from '../../components/listSearch';
import type { DataCheckCode, DataFinding, DataGaps, DataLocationItem, DataMappingItem, DataSeverity, DataUseItem } from '../../data/contracts';
import { DATA_CHECK_CODES, DATA_CHECKS, dataCheckOf } from '../../data/dataTaxonomy';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { filterByText } from '../../data/textSearch';
import { dataOf } from '../../data/toolClient';
import { cn } from '../../lib/utils';
import { Chip } from '../deployments-places/parts';
import {
  checkCounts,
  DETAIL_CHECKS,
  findingMatches,
  findingText,
  fixOf,
  groupFindings,
  isMyFinding,
  mappingActionsOf,
  mayChangeLocation,
  mayMapData,
  mayProposeDomainOwner,
  mayProposeLocationOwner,
  plainFinding,
  sameId,
  type FindingFix,
} from './logic';
import { ProposeOwnerChangeSheet } from '../owners-reviews/OwnerSheets';
import { appHref, column, filterSection, ListNotes, locationHref, locationLabel, twoColumns, useParams, type DataContext } from './parts';
import type { useGaps } from './reads';
import { MappingActionButtons } from './rowActions';
import { SheetHoldContext, useHeld, useHoldCount } from './sheetParts';
import { DataDecisionSheet, DataLocationSheet, DataMappingSheet, DataUseSheet, MoveDataReferencesSheet, ProposeDataOwnerSheet } from './sheets';

const CHECK = /^DC-(0[1-9]|1[0-4])$/;
const SEVERITY = /^(high|medium|low)$/;
const SEVERITIES: DataSeverity[] = ['high', 'medium', 'low'];
const SEVERITY_TEXT: Record<DataSeverity, string> = { high: 'High', medium: 'Medium', low: 'Low' };

export function GapsTab({ ctx, gaps }: { ctx: DataContext; gaps: ReturnType<typeof useGaps> }) {
  const setParams = useParams();
  const ownerRoles = useLabels('owner-role');
  const check = useQueryParam('check', CHECK) as DataCheckCode | undefined;
  const severity = useQueryParam('severity', SEVERITY) as DataSeverity | undefined;
  const text = useListText();
  const [holds, hold] = useHoldCount();
  const live = gaps.items;
  const items = useHeld(live, holds > 0);
  const ownerRoleKeys = new Map(ownerRoles.keys().map(k => [k, ownerRoles.label(k)]));
  const names = {
    location: (id: string | null) => locationLabel(ctx.locations, id),
    ownerRole: (key: string | null) => (key && ownerRoles.has(key) ? ownerRoles.label(key) : null),
  };
  const mine = ctx.route.mine ? items.filter(f => isMyFinding(f, ctx.mineKeys, ctx.me)) : items;
  const searched = text ? mine.filter(f => findingMatches(f, text, findingText(f, names), names.location(findingLocationId(f)))) : mine;
  const counts = checkCounts(searched);
  const bySeverity = Object.fromEntries(SEVERITIES.map(s => [s, searched.filter(f => f.severity === s).length])) as Record<DataSeverity, number>;
  const shown = searched.filter(f => (!check || f.check === check) && (!severity || f.severity === severity));
  const groups = groupFindings(shown);
  const rows = useFixRows(shown, ctx, holds > 0);
  const failure = gaps.read.status === 'failed' ? gaps.read.failure : null;
  const first: DataGaps | null = gaps.read.status === 'done' ? gaps.read.first.data : null;
  const summary = gaps.read.status === 'done' ? gaps.read.first.summary : null;
  const appLevel = first?.appLevelDataOwners ?? [];
  const pageFilter = !!check || !!severity || !!text;
  const clear = () => setParams({ check: undefined, severity: undefined, q: undefined });

  return (
    <SheetHoldContext.Provider value={hold}>
      <section aria-label="Gap filters" className={filterSection}>
        <div className="flex flex-col gap-1.5">
          <span className="text-xs text-navy-3" id="gaps-severity-label">Severity</span>
          <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0" aria-labelledby="gaps-severity-label">
            <li><FilterButton label="All" count={gaps.loaded ? mine.length : null} pressed={!severity} onClick={() => setParams({ severity: undefined })} /></li>
            {SEVERITIES.map(s => <li key={s}><FilterButton label={SEVERITY_TEXT[s]} count={gaps.loaded ? bySeverity[s] : null} pressed={severity === s} onClick={() => setParams({ severity: severity === s ? undefined : s })} /></li>)}
          </ul>
        </div>
        <ListSearch label="Search the findings by what they say, domain, application, location or person" placeholder="Domain, application or person" />
        {pageFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active gap filters">
            {check && <Chip label={`${check} ${DATA_CHECKS[check].title}`} onRemove={() => setParams({ check: undefined })} />}
            {severity && <Chip label={`Severity: ${SEVERITY_TEXT[severity]}`} onRemove={() => setParams({ severity: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => setParams({ q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clear}>Clear these</Button>
          </div>
        )}
      </section>

      <div className={twoColumns}>
        <div className={column}>
          {failure ? <PanelFailure failure={failure} />
            : !gaps.loaded ? <Panel title="Findings" state={{ status: 'loading' }} skeletonRows={6}>{() => null}</Panel>
              : groups.length === 0 ? (
                <Panel title="Findings" count={0}>
                  <span className="flex flex-col items-start gap-2">
                    {pageFilter ? <Empty>No finding matches these filters.</Empty> : (
                      <span className="flex items-center gap-2 text-sm text-navy">
                        <CircleCheck className="size-4 text-success" aria-hidden />
                        {summary && /off/i.test(summary) ? summary : 'No data gap in this scope.'}
                      </span>
                    )}
                    {pageFilter && <Button variant="outline" size="sm" onClick={clear}>Clear the filters</Button>}
                  </span>
                </Panel>
              ) : groups.map(g => {
                const code = g.check;
                return (
                  <Panel
                    key={g.label}
                    title={code ? `${code} ${DATA_CHECKS[code].title}` : g.label}
                    count={g.items.length}
                    subtitle={code ? CHECK_HELP[code] : undefined}
                  >
                    <RowList label={code ? DATA_CHECKS[code].title : g.label}>
                      {g.items.map((f, i) => (
                        <FindingRow
                          key={`${f.check}-${f.code}-${f.subjectId}-${i}`}
                          finding={f}
                          ctx={ctx}
                          text={findingText(f, names)}
                          detail={DETAIL_CHECKS.includes(f.check) ? plainFinding(f.message, ownerRoleKeys) : ''}
                          rows={rows}
                        />
                      ))}
                    </RowList>
                  </Panel>
                );
              })}
          {gaps.read.status === 'done' && gaps.read.skipToken && <Muted>The product holds more findings than this page shows. Narrow to one domain, application or location to see the rest.</Muted>}
          <ListNotes reads={[ctx.uses, ctx.mappings]} one="use or copy" many="uses and copies" />
        </div>
        <div className={column}>
          <Panel title="By check" subtitle="The findings in scope. Each check narrows the list.">
            <ul className="m-0 flex list-none flex-col gap-0.5 p-0">
              {DATA_CHECK_CODES.map(c => {
                const n = counts[c] ?? 0;
                return (
                  <li key={c}>
                    <button
                      type="button"
                      onClick={() => setParams({ check: check === c ? undefined : c })}
                      aria-pressed={check === c}
                      className={cn(
                        'grid w-full cursor-pointer grid-cols-[52px_minmax(0,1fr)_auto] items-center gap-2 rounded-md border border-transparent px-2 py-1.5 text-left text-sm hover:bg-background focus-visible:outline-2 focus-visible:outline-ring aria-pressed:border-primary aria-pressed:bg-info-soft',
                        n === 0 ? 'text-navy-3' : 'text-navy',
                      )}
                    >
                      <span className="font-mono text-xs">{c}</span>
                      <span>{DATA_CHECKS[c].title}</span>
                      <span className={cn('font-semibold tabular-nums', n > 0 && DATA_CHECKS[c].severity === 'high' && 'text-danger')}>{gaps.loaded ? n : ''}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </Panel>
          {appLevel.length > 0 && <AppLevelOwners items={appLevel} text={text} />}
        </div>
      </div>
    </SheetHoldContext.Provider>
  );
}

// The location a finding is about, for its words and its link.
function findingLocationId(f: DataFinding): string | null {
  return f.dataLocationId ?? (f.subjectKind === 'data-location' ? f.subjectId : null);
}

// The uses and copies the shown findings' fixes act on: from the page's lists, else read by their IDs (at most 100 each).
interface FixRows {
  uses: DataUseItem[];
  mappings: DataMappingItem[];
  // True while a row a fix needs is still being read.
  reading: boolean;
}

function useFixRows(shown: DataFinding[], ctx: DataContext, held: boolean): FixRows {
  const listUses = useHeld(ctx.uses.items, held);
  const listMappings = useHeld(ctx.mappings.items, held);
  const fixes = shown.map(fixOf);
  const wantUses = [...new Set(fixes.flatMap(x => (x.kind === 'change-use' ? [x.dataUseId.toLowerCase()] : [])))];
  const wantMappings = [...new Set(fixes.flatMap(x => (x.kind === 'copy' ? [x.dataMappingId.toLowerCase()] : [])))];
  const missingUses = ctx.uses.loaded ? wantUses.filter(id => !listUses.some(u => sameId(u.dataUseId, id))).sort().slice(0, 100) : [];
  const missingMappings = ctx.mappings.loaded ? wantMappings.filter(id => !listMappings.some(m => sameId(m.dataMappingId, id))).sort().slice(0, 100) : [];
  const extraUses = useRead('list_data_uses', missingUses.length ? { dataUseIds: missingUses, includeEnded: true, top: 100 } : null);
  const extraMappings = useRead('list_data_mappings', missingMappings.length ? { dataMappingIds: missingMappings, includeEnded: true, top: 100 } : null);
  return {
    uses: [...listUses, ...(dataOf(extraUses)?.items ?? [])],
    mappings: [...listMappings, ...(dataOf(extraMappings)?.items ?? [])],
    reading: !ctx.uses.loaded || !ctx.mappings.loaded || (missingUses.length > 0 && extraUses.status === 'loading') || (missingMappings.length > 0 && extraMappings.status === 'loading'),
  };
}

// Under domains-required: the data owners still held on applications, which no longer answer anything. Each ends
// through an owner change, as any owner assignment does.
function AppLevelOwners({ items, text }: { items: NonNullable<DataGaps['appLevelDataOwners']>; text: string | undefined }) {
  const session = useSession();
  const shown = filterByText(items, text, o => [o.applicationName, o.principal?.displayLabel]);
  const mayEnd = session.can('propose_owner_change');
  return (
    <Panel
      title="Application data owners to end"
      count={shown.length}
      subtitle="This install sets data owners on the domains, so a data owner held on an application no longer answers anything."
    >
      {shown.length === 0 ? <Empty>No application data owner matches this search.</Empty> : (
        <RowList label="Application data owners to end">
          {shown.map(o => {
            const app = appHref(o.businessApplicationId);
            const who = o.principal?.displayLabel || 'A person the product knows';
            return (
              <Row
                key={o.ownerAssignmentId}
                wideEnd
                primary={app ? <TextLink href={app}>{o.applicationName}</TextLink> : o.applicationName}
                secondary={`${who}, data owner${o.isDeputy ? ', deputy' : ''}`}
                end={mayEnd && o.principal ? (
                  <ProposeOwnerChangeSheet
                    application={{ id: o.businessApplicationId, name: o.applicationName }}
                    mode="end"
                    ownerRoleKey="data-owner"
                    ending={{ assignmentId: o.ownerAssignmentId, principal: o.principal, isDeputy: o.isDeputy }}
                    me={null}
                    trigger={<Button size="sm" variant="outline" aria-label={`End ${who} as data owner of ${o.applicationName}`}>End</Button>}
                  />
                ) : undefined}
              />
            );
          })}
        </RowList>
      )}
    </Panel>
  );
}

// What each check means, in one line under its heading.
const CHECK_HELP: Record<DataCheckCode, string> = {
  'DC-01': 'No data owner on the domain or any domain above it.',
  'DC-02': 'The level needs an owner of its own, and only an inherited one exists.',
  'DC-03': 'A holder whose owner role needs a deputy has none.',
  'DC-04': 'A holder or deputy is disabled, deleted or leaving.',
  'DC-05': 'A use needs a decision, or a decision again, and none allows it.',
  'DC-06': 'A use needs a decision, but an approver it needs resolves to nobody.',
  'DC-07': 'A use the rule in force does not allow. No decision can allow it.',
  'DC-08': 'A copy of production data outside production that needs a decision or breaks the copy rule.',
  'DC-09': 'The application\'s sensitivity answer ranks below the data it uses.',
  'DC-10': 'A data dependency, or AI, with no data use declared.',
  'DC-11': 'A use names a location where its data is not mapped.',
  'DC-12': 'A location with no technical owner, or not confirmed seen recently.',
  'DC-13': 'A retiring application writes data with no retention decision.',
  'DC-14': 'Rows still point at a retired domain.',
};

function FilterButton({ label, count, pressed, onClick }: { label: string; count: number | null; pressed: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={pressed}
      className={cn(
        'inline-flex h-8 cursor-pointer items-center gap-1.5 rounded-md border px-2.5 text-sm text-navy focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring narrow:h-7 narrow:text-xs',
        pressed ? 'border-primary bg-info-soft font-medium' : 'border-line bg-card hover:bg-background',
      )}
    >
      {label}
      {count !== null && <span className="rounded-full bg-muted px-1.5 text-xs font-semibold text-navy-2 tabular-nums">{count}</span>}
    </button>
  );
}

function FindingRow({ finding: f, ctx, text, detail, rows }: {
  finding: DataFinding;
  ctx: DataContext;
  text: string;
  detail: string;
  rows: FixRows;
}) {
  const app = appHref(f.businessApplicationId);
  const locationId = findingLocationId(f);
  const location = locationId ? ctx.locations.find(l => sameId(l.dataLocationId, locationId)) ?? null : null;
  const record = recordHref(f.recordUrl);
  const extra = detail && detail !== text && !text.includes(detail) ? detail : null;
  return (
    <Row
      wideEnd
      primary={text}
      secondary={f.suggestion ? <Suggestion finding={f} /> : extra ?? undefined}
      tertiary={(
        <span className="flex flex-wrap gap-x-4 gap-y-2">
          {f.domain && <DomainLink domain={f.domain} className="text-xs" />}
          {app && f.applicationName && <TextLink href={app} className="text-xs">{f.applicationName}</TextLink>}
          {location && locationHref(location) && <TextLink href={locationHref(location)!} className="text-xs">{location.label}</TextLink>}
          {f.principal && <span>{f.principal.displayLabel}</span>}
          {record && <TextLink href={record} className="text-xs">Open the record</TextLink>}
        </span>
      )}
      badges={(
        <>
          <DataSeverityBadge severity={f.severity} />
          {dataCheckOf(f.check) === null && <ToneBadge tone="subtle" icon={null}>{f.check}</ToneBadge>}
        </>
      )}
      end={<FixAction fix={fixOf(f)} finding={f} ctx={ctx} location={location} rows={rows} />}
    />
  );
}

// DC-10's offer: the other applications this one reads from or writes to, and the domains each holds, each a button that
// opens the declare sheet on that domain (B24-22). The person still chooses the access and the purpose.
function Suggestion({ finding: f }: { finding: DataFinding }) {
  const session = useSession();
  const s = f.suggestion!;
  const domains = new Map<string, { key: string; path: string }>();
  for (const a of s.applications) for (const d of a.domains) if (!domains.has(d.key)) domains.set(d.key, { key: d.key, path: d.path });
  const may = session.can('save_data_use') && !!f.businessApplicationId;
  return (
    <span className="flex flex-col gap-1.5">
      <span className="inline-flex items-center gap-1.5 text-sm text-navy">
        <Lightbulb className="size-4 shrink-0 text-navy-3" aria-hidden />
        {s.text}
      </span>
      {domains.size > 0 && (
        <span className="flex flex-wrap items-center gap-1.5" role="group" aria-label={`Declare a use of the data ${s.applications.map(a => a.name).join(', ')} holds`}>
          {[...domains.values()].slice(0, 6).map(d => may ? (
            <DataUseSheet
              key={d.key}
              mode="declare"
              applicationId={f.businessApplicationId!}
              applicationName={f.applicationName ?? ''}
              domainKey={d.key}
              trigger={<Button size="sm" variant="outline" aria-label={`Declare a use of ${d.path} by ${f.applicationName ?? 'the application'}`}>{`Declare ${d.path}`}</Button>}
            />
          ) : <ToneBadge key={d.key} tone="subtle" icon={null}>{d.path}</ToneBadge>)}
          {domains.size > 6 && <Muted>{`and ${domains.size - 6} more`}</Muted>}
        </span>
      )}
    </span>
  );
}

// The one act that fixes a finding, when the person may do it.
function FixAction({ fix, finding: f, ctx, location, rows }: {
  fix: FindingFix;
  finding: DataFinding;
  ctx: DataContext;
  location: DataLocationItem | null;
  rows: FixRows;
}) {
  const session = useSession();
  const path = f.domain?.path ?? 'the domain';
  switch (fix.kind) {
    case 'owner-domain': {
      if (!ctx.tree || !session.can('propose_owner_change') || !mayProposeDomainOwner(ctx.me, ctx.tree, fix.domainKey)) return null;
      const domain = ctx.tree.nodes.get(fix.domainKey)?.item;
      return <ProposeDataOwnerSheet subject={{ kind: 'data-domain', domainKey: fix.domainKey, label: domain?.path ?? path }} ownerRoleKey={fix.ownerRoleKey} trigger={<Button size="sm">{f.check === 'DC-03' ? 'Propose a deputy' : f.check === 'DC-04' ? 'Propose a successor' : 'Propose an owner'}</Button>} />;
    }
    case 'owner-location': {
      if (!session.can('propose_owner_change') || !mayProposeLocationOwner(ctx.me, location)) return null;
      return <ProposeDataOwnerSheet subject={{ kind: 'data-location', dataLocationId: fix.dataLocationId, label: location?.label ?? 'the location' }} ownerRoleKey={fix.ownerRoleKey ?? (f.check === 'DC-12' ? 'technical-owner' : undefined)} trigger={<Button size="sm">{f.check === 'DC-03' ? 'Propose a deputy' : f.check === 'DC-04' ? 'Propose a successor' : 'Propose an owner'}</Button>} />;
    }
    case 'confirm-seen':
      if (!location || !session.can('save_data_location') || !mayChangeLocation(ctx.me, location)) return null;
      return <DataLocationSheet mode="confirm-seen" location={location} trigger={<Button size="sm">Confirm seen</Button>} />;
    case 'decide-use':
      if (!session.can('save_decision_proposal')) return null;
      return <DataDecisionSheet subjectKind="data-use" subjectId={fix.dataUseId} decisionTypeKey={fix.decisionTypeKey} label={`${f.applicationName ?? 'the application'} using ${path}`} trigger={<Button size="sm">{fix.decisionTypeKey === 'data-retention' ? 'Propose a retention decision' : 'Propose a decision'}</Button>} />;
    case 'change-use': {
      const use = rows.uses.find(u => sameId(u.dataUseId, fix.dataUseId));
      if (!use) return rows.reading ? <Muted>Reading the use.</Muted> : null;
      if (!session.can('save_data_use')) return null;
      return (
        <span className="flex flex-wrap gap-1.5">
          <DataUseSheet mode="edit" use={use} applicationId={use.businessApplicationId} applicationName={use.applicationName} trigger={<Button size="sm">Change the use</Button>} />
          <DataUseSheet mode="end" use={use} applicationId={use.businessApplicationId} applicationName={use.applicationName} trigger={<Button variant="outline" size="sm">End it</Button>} />
        </span>
      );
    }
    case 'declare-use':
      if (!session.can('save_data_use')) return null;
      return <DataUseSheet mode="declare" applicationId={fix.applicationId} applicationName={fix.applicationName} trigger={<Button size="sm">Declare its data</Button>} />;
    case 'copy': {
      const mapping = rows.mappings.find(m => sameId(m.dataMappingId, fix.dataMappingId));
      if (!mapping) return rows.reading ? <Muted>Reading the copy.</Muted> : null;
      const acts = mappingActionsOf(mapping);
      if (!acts.propose && !acts.mustChange && !mapping.decisionId) return null;
      return <MappingActionButtons mapping={mapping} ctx={ctx} />;
    }
    case 'add-mapping': {
      const domain = ctx.tree?.nodes.get(fix.domainKey)?.item ?? null;
      if (!session.can('save_data_mapping') || !(mayMapData(ctx.me, domain, location) || session.hasRole('data-owner'))) return null;
      return <DataMappingSheet mode="add" domainKey={fix.domainKey} dataLocationId={fix.dataLocationId} trigger={<Button size="sm">Map the data</Button>} />;
    }
    case 'tier':
      return (
        <Button size="sm" variant="outline" asChild>
          <a href={withParams(hrefTo({ page: 'app', id: fix.applicationId.toLowerCase() }), { tab: 'tier' })}>Answer the tier questions</a>
        </Button>
      );
    case 'move':
      return <MoveDataReferencesSheet fromDomainKey={fix.domainKey} trigger={<Button size="sm">Move the rows</Button>} />;
    case 'none':
      return null;
  }
}
