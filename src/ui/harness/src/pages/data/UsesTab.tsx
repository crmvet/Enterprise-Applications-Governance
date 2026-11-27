// The Uses tab (/app/data/uses): every data use across the applications, with its decision state, for the security
// reviewer and the data owners. Page parameters: state=attention|proposed|allowed|denied|not-needed, ai=1,
// access=read|write|read-write, q=<text>.
// Wave 4: the rows are the page's one list_data_uses read (ctx.uses), with the domain or application filter sent to the
// tool; the location, the state, the access, AI and the words are kept on the page, so every count is the rows it opens.
import { Bot, Plus } from 'lucide-react';
import { useQueryParam } from '../../app/links';
import { Empty, Muted, Panel, PanelFailure, TextLink, ToneBadge, useNarrow } from '../../components/blocks';
import { DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import type { DataAccess, DataUseItem } from '../../data/contracts';
import { DATA_ACCESS_TEXT } from '../../data/dataTaxonomy';
import { plural } from '../../data/format';
import { useSession } from '../../data/session';
import { cn } from '../../lib/utils';
import { Chip } from '../deployments-places/parts';
import { sortUses, stateCounts, USE_STATE_FILTER_TEXT, USE_STATE_FILTERS, useMatches, type UseFilters, type UseStateFilter } from './logic';
import { ListSearch } from '../../components/listSearch';
import { appHref, FilterLabel, filterSection, ListNotes, NoDomainsYet, TEXT, useHref, UseState, useParams, type DataContext } from './parts';
import { LocationName, UseActionButtons } from './rowActions';
import { SheetHoldContext, useHeld, useHoldCount } from './sheetParts';
import { DataUseSheet } from './sheets';

const STATE = /^(attention|proposed|allowed|denied|not-needed)$/;
const ACCESS = /^(read|write|read-write)$/;

export function UsesTab({ ctx }: { ctx: DataContext }) {
  const narrow = useNarrow();
  const session = useSession();
  const setParams = useParams();
  const state = useQueryParam('state', STATE) as UseStateFilter | undefined;
  const aiOnly = useQueryParam('ai', /^1$/) === '1';
  const access = useQueryParam('access', ACCESS) as DataAccess | undefined;
  const text = useQueryParam('q', TEXT);
  const loading = !ctx.domainsLoaded || !ctx.uses.loaded;
  const [holds, hold] = useHoldCount();
  const all = useHeld(ctx.uses.items, holds > 0);

  const scoped: UseFilters = { domainKeys: ctx.scope.domainKeys, applicationId: ctx.route.applicationId, locationIds: ctx.scope.locationIds };
  const base = all.filter(u => useMatches(u, { ...scoped, aiOnly, access, text }));
  const shown = sortUses(base.filter(u => useMatches(u, { state })));
  const counts = stateCounts(base);
  const pageFilter = !!state || aiOnly || !!access || !!text;
  const clear = () => setParams({ state: undefined, ai: undefined, access: undefined, q: undefined });
  const aiCount = all.filter(u => useMatches(u, { ...scoped, access, text }) && u.reachesAi).length;

  return (
    <SheetHoldContext.Provider value={hold}>
      <section aria-label="Use filters" className={filterSection}>
        <div className="flex flex-col gap-1.5">
          <span className="text-xs text-navy-3" id="uses-state-label">Decision state</span>
          <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0" aria-labelledby="uses-state-label">
            <li><StateButton label="All" count={loading ? null : base.length} pressed={!state} onClick={() => setParams({ state: undefined })} /></li>
            {USE_STATE_FILTERS.map(f => (
              <li key={f}><StateButton label={USE_STATE_FILTER_TEXT[f]} count={loading ? null : counts[f]} pressed={state === f} attention={f === 'attention'} onClick={() => setParams({ state: state === f ? undefined : f })} /></li>
            ))}
          </ul>
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <FilterLabel id="uses-access-label">Access</FilterLabel>
            <Select value={access ?? 'all'} onValueChange={v => setParams({ access: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[200px] narrow:w-full" aria-labelledby="uses-access-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Any access</SelectItem>
                {(['read', 'write', 'read-write'] as const).map(a => <SelectItem key={a} value={a}>{DATA_ACCESS_TEXT[a]}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <ListSearch label="Search the uses by application, data, purpose or number" placeholder="Application, data or number" />
          <div className="flex items-center gap-2 pb-2">
            <Checkbox id="uses-ai" checked={aiOnly} onCheckedChange={v => setParams({ ai: v === true ? '1' : undefined })} />
            <Label htmlFor="uses-ai" className="font-normal">{`Only uses that reach AI${loading ? '' : ` (${aiCount})`}`}</Label>
          </div>
        </div>
        {pageFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active use filters">
            {state && <Chip label={USE_STATE_FILTER_TEXT[state]} onRemove={() => setParams({ state: undefined })} />}
            {access && <Chip label={`Access: ${DATA_ACCESS_TEXT[access]}`} onRemove={() => setParams({ access: undefined })} />}
            {aiOnly && <Chip label="Reaches AI" onRemove={() => setParams({ ai: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => setParams({ q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clear}>Clear these</Button>
          </div>
        )}
      </section>

      {ctx.domainsFailure || ctx.uses.failure ? <PanelFailure failure={(ctx.domainsFailure ?? ctx.uses.failure)!} /> : (
        <Panel
          title="Data uses"
          count={loading ? undefined : shown.length}
          subtitle="Which applications read or write which data, and whether the rules allow it"
          actions={session.can('save_data_use') ? (
            <DataUseSheet
              mode="declare"
              applicationId={ctx.route.applicationId ?? ''}
              applicationName={ctx.appName ?? ''}
              domainKey={ctx.route.domainKey}
              trigger={<Button size="sm"><Plus aria-hidden />Declare a use</Button>}
            />
          ) : undefined}
        >
          {loading ? <Muted>Reading the data uses.</Muted>
            : ctx.tree && ctx.tree.order.length === 0 ? <NoDomainsYet />
              : shown.length === 0 ? (
                <span className="flex flex-col items-start gap-2">
                  <Empty>{pageFilter ? 'No use matches these filters.' : 'No application declares a use of this data yet.'}</Empty>
                  {pageFilter && <Button variant="outline" size="sm" onClick={clear}>Clear the filters</Button>}
                </span>
              ) : narrow ? <UseRows uses={shown} ctx={ctx} /> : <UseTable uses={shown} ctx={ctx} />}
          <ListNotes reads={[ctx.uses]} one="use" many="uses" />
        </Panel>
      )}
    </SheetHoldContext.Provider>
  );
}

function StateButton({ label, count, pressed, attention, onClick }: { label: string; count: number | null; pressed: boolean; attention?: boolean; onClick: () => void }) {
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
      {count !== null && (
        <span className={cn('rounded-full px-1.5 text-xs font-semibold tabular-nums', attention && count > 0 ? 'bg-gold-soft text-gold-strong' : 'bg-muted text-navy-2')}>{count}</span>
      )}
    </button>
  );
}

function UseName({ use }: { use: DataUseItem }) {
  const record = useHref(use);
  const app = appHref(use.businessApplicationId);
  return (
    <span className="flex min-w-0 flex-col gap-0.5">
      <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">
        {app ? <TextLink href={app}>{use.applicationName}</TextLink> : use.applicationName}
      </span>
      <span className="text-xs text-navy-3">
        {record ? <TextLink href={record} className="text-xs">{use.recordNumber ?? 'Draft use'}</TextLink> : use.recordNumber ?? 'Draft use'}
        {use.status.meaning === 'draft' && use.recordNumber ? ', a draft' : use.status.meaning === 'draft' ? ', no number yet' : ''}
      </span>
    </span>
  );
}

function AiBadge({ use }: { use: DataUseItem }) {
  if (!use.reachesAi) return null;
  return <ToneBadge tone="informative" icon={<Bot aria-hidden />}>{use.aiServiceAsSeen ? `AI: ${use.aiServiceAsSeen}` : 'Reaches AI'}</ToneBadge>;
}

function UseTable({ uses, ctx }: { uses: DataUseItem[]; ctx: DataContext }) {
  return (
    <div className="-mx-1 overflow-x-auto">
      <table className="w-full min-w-[860px] border-collapse text-sm" aria-label="Data uses">
        <thead>
          <tr className="border-b border-line text-left text-xs text-navy-3">
            <th scope="col" className="py-2 pr-3 pl-1 font-medium">Use</th>
            <th scope="col" className="px-3 py-2 font-medium">Data</th>
            <th scope="col" className="px-3 py-2 font-medium">Access</th>
            <th scope="col" className="px-3 py-2 font-medium">Decision state</th>
            <th scope="col" className="py-2 pr-1 pl-3 text-right font-medium"><span className="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {uses.map(u => (
            <tr key={u.dataUseId} className="border-b border-line align-top last:border-b-0">
              <td className="py-2.5 pr-3 pl-1">
                <UseName use={u} />
                <AiMark isAiCreated={u.isAiCreated} performedBy={u.performedBy} className="mt-1" />
              </td>
              <td className="px-3 py-2.5">
                <span className="flex flex-col gap-0.5">
                  <DomainLink domain={u.domain} className="text-sm" />
                  <span className="text-xs text-navy-3">{u.location ? <>At <LocationName location={u.location} /></> : 'Location not stated'}</span>
                  <span className="text-xs text-navy-3 [overflow-wrap:anywhere]">{u.purpose}</span>
                </span>
              </td>
              <td className="px-3 py-2.5">
                <span className="flex flex-col items-start gap-1">
                  <span className="text-sm text-navy">{DATA_ACCESS_TEXT[u.access]}</span>
                  <AiBadge use={u} />
                  {u.isDeidentified && <ToneBadge tone="subtle" icon={null}>Masked data</ToneBadge>}
                </span>
              </td>
              <td className="px-3 py-2.5"><UseState use={u} /></td>
              <td className="py-2.5 pr-1 pl-3"><span className="flex justify-end"><UseActionButtons use={u} ctx={ctx} /></span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function UseRows({ uses, ctx }: { uses: DataUseItem[]; ctx: DataContext }) {
  return (
    <RowList label="Data uses">
      {uses.map(u => (
        <Row
          key={u.dataUseId}
          wideEnd
          primary={<UseName use={u} />}
          secondary={<>{`${DATA_ACCESS_TEXT[u.access]} `}<DomainLink domain={u.domain} className="text-xs" />{u.location ? <>{' at '}<LocationName location={u.location} /></> : null}</>}
          tertiary={u.purpose}
          badges={(
            <>
              <UseState use={u} />
              <AiBadge use={u} />
              {u.isDeidentified && <ToneBadge tone="subtle" icon={null}>Masked data</ToneBadge>}
              <AiMark isAiCreated={u.isAiCreated} performedBy={u.performedBy} />
            </>
          )}
          end={<UseActionButtons use={u} ctx={ctx} />}
        />
      ))}
      <li className="pt-2"><Muted>{plural(uses.length, 'use', 'uses')} shown.</Muted></li>
    </RowList>
  );
}
