// Pieces the Data page's tabs share: what every tab is handed (DataContext), the address helpers for a tab's own
// filters, the search box, the domain cells (owners, rules, gaps), the location and use marks, and the empty states.
import { Lock, Search } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { domainHref, hrefTo, recordHref, recordHrefOf, withParams } from '../../app/links';
import { navigate, useLocationKey } from '../../app/navigation';
import type { RouteOf } from '../../app/routes';
import { Muted, TextLink, ToneBadge } from '../../components/blocks';
import { DataRuleChip, DataUseStateBadge } from '../../components/dataChips';
import { Input } from '../../components/ui/input';
import type { DataCheckCode, DataDomainItem, DataLocationItem, DataMappingItem, DataUseItem, DataUseState, Failure } from '../../data/contracts';
import { DATA_CHECKS, DATA_ORIGIN_SHORT, inheritedText } from '../../data/dataTaxonomy';
import type { PagedState } from '../../data/reads';
import { plural } from '../../data/format';
import { cn } from '../../lib/utils';
import { dataTabRoute, gapChecks, notableRules, sameId, type DataScope, type DomainTree, type Me } from './logic';
import type { RowsRead } from './reads';

export interface DataContext {
  route: RouteOf<'data'>;
  tree: DomainTree | null;
  domainsLoaded: boolean;
  domainsFailure: Failure | null;
  locations: DataLocationItem[];
  locationsLoaded: boolean;
  // The application the app filter names: its uses (get_application_card, data section) and name.
  appUses: DataUseItem[] | null;
  appName: string | null;
  mineKeys: Set<string> | null;
  scope: DataScope;
  me: Me;
  today: string;
  // Every open use and mapping across the domains (list_data_uses, list_data_mappings), read once by the page with the
  // domain or application filter; each tab keeps the rows in its scope.
  uses: RowsRead<DataUseItem>;
  mappings: RowsRead<DataMappingItem>;
}

// ---------- the address ----------

// Sets a tab's own parameters (undefined removes one) and keeps the scroll position, so a filter or a selected cell does
// not move the page.
export function useParams(): (params: Record<string, string | undefined>) => void {
  useLocationKey();
  return params => {
    const y = window.scrollY;
    navigate(withParams(`${window.location.pathname}${window.location.search}`, params), true);
    window.scrollTo(0, y);
  };
}

// The address of another tab with the shared filters kept and some of its own parameters set.
export function tabHref(route: RouteOf<'data'>, tab: RouteOf<'data'>['tab'] | 'domains', params: Record<string, string | undefined> = {}, shared: { domainKey?: string } = {}): string {
  const next = dataTabRoute(route, tab ?? 'domains');
  if (shared.domainKey) next.domainKey = shared.domainKey;
  return withParams(hrefTo(next), params);
}

export const TEXT = /^.{1,200}$/;
export const KEY = /^[a-z][a-z0-9-]{0,63}$/;
export const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

// A search box that writes q= once the person stops typing, and follows the address when it changes elsewhere.
export function SearchBox({ value, placeholder, label, onChange, className }: { value: string | undefined; placeholder: string; label: string; onChange: (text: string | undefined) => void; className?: string }) {
  const [text, setText] = useState(value ?? '');
  useEffect(() => {
    const t = window.setTimeout(() => {
      const next = text.trim().slice(0, 200) || undefined;
      if (next !== value) onChange(next);
    }, 350);
    return () => window.clearTimeout(t);
  }, [text]);
  useEffect(() => { setText(value ?? ''); }, [value]);
  return (
    <div className={cn('relative w-[260px] narrow:w-full', className)}>
      <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
      <Input type="search" value={text} onChange={e => setText(e.target.value)} placeholder={placeholder} aria-label={label} className="pl-8" maxLength={200} />
    </div>
  );
}

// A small filter label above a control.
export function FilterLabel({ id, children }: { id: string; children: ReactNode }) {
  return <span className="text-xs text-navy-3" id={id}>{children}</span>;
}

export const filterSection = 'mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3';
export const twoColumns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
export const column = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

// ---------- links ----------

export const appHref = (id: string | null | undefined) => (id ? hrefTo({ page: 'app', id: id.toLowerCase() }) : null);
export const decisionHref = (id: string | null | undefined) => (id ? hrefTo({ page: 'decision', id: id.toLowerCase() }) : null);
export const placeHref = (id: string | null | undefined) => (id ? hrefTo({ page: 'place', id: id.toLowerCase() }) : null);
export const locationHref = (l: { dataLocationId: string; recordUrl?: string | null }) => recordHref(l.recordUrl) ?? recordHrefOf('data-location', l.dataLocationId.toLowerCase());
export const useHref = (u: { dataUseId: string; recordUrl?: string | null }) => recordHref(u.recordUrl) ?? recordHrefOf('data-use', u.dataUseId.toLowerCase());
export const mappingHref = (m: { dataMappingId: string; recordUrl?: string | null }) => recordHref(m.recordUrl) ?? recordHrefOf('data-mapping', m.dataMappingId.toLowerCase());

// ---------- domain cells ----------

// The classification, with "from People" in muted text when a domain above sets it.
export function ClassificationCell({ item }: { item: DataDomainItem }) {
  const c = item.classification;
  if (!c) return <ToneBadge tone="subtle" icon={null}>Not classified</ToneBadge>;
  const inherited = c.setOn && c.setOn.key !== item.key ? c.setOn.label : null;
  return (
    <span className="inline-flex flex-wrap items-center gap-1.5">
      <ToneBadge tone="brand" icon={null}>{c.label}</ToneBadge>
      {inherited && <Muted>{`from ${inherited}`}</Muted>}
    </span>
  );
}

// The data owner (own, or "Hana Lindqvist, from People"), the deputy and the steward.
export function OwnerLines({ item, compact = false }: { item: DataDomainItem; compact?: boolean }) {
  return (
    <span className="flex min-w-0 flex-col gap-0.5 text-sm text-navy">
      {item.owner ? (
        <span className="[overflow-wrap:anywhere]">
          <span className="text-navy-3">{compact ? 'Owner ' : 'Data owner '}</span>
          <span className="font-medium">{item.owner.principal.displayLabel || 'A person the product knows'}</span>
          {item.owner.inheritedFrom && <span className="text-navy-3">{`, from ${item.owner.inheritedFrom.label}`}</span>}
        </span>
      ) : <span><ToneBadge tone="danger">No data owner</ToneBadge></span>}
      <span className="text-xs text-navy-3 [overflow-wrap:anywhere]">
        {`Deputy ${item.deputy ? item.deputy.displayLabel : 'none'}. Steward ${inheritedText(item.steward, 'none')}.`}
      </span>
    </span>
  );
}

// The rules that are not "allowed", as chips; "Never" carries the lock and the danger tone, always with its label.
export function RuleChips({ item }: { item: DataDomainItem }) {
  const names = notableRules(item.rules);
  if (!names.length) return <Muted>Every use allowed</Muted>;
  return (
    <span className="flex flex-wrap gap-1">
      {names.map(n => <DataRuleChip key={n} name={n} rule={item.rules[n]} />)}
    </span>
  );
}

// The domain's owner gaps as badges, each opening the Gaps tab at its check for this domain.
export function GapBadges({ item, route }: { item: DataDomainItem; route: RouteOf<'data'> }) {
  const checks = gapChecks(item.gaps);
  if (!checks.length) return null;
  return (
    <span className="flex flex-wrap gap-1">
      {checks.map(c => (
        <a
          key={c}
          href={tabHref(route, 'gaps', { check: c }, { domainKey: item.key })}
          className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
          aria-label={`${DATA_CHECKS[c].title} on ${item.path}: open the finding`}
        >
          <ToneBadge tone={DATA_CHECKS[c].severity === 'high' ? 'danger' : 'warning'}>{DATA_CHECKS[c].title}</ToneBadge>
        </a>
      ))}
    </span>
  );
}

// The domain's counts, each opening its rows: mappings on the Mapping tab, uses on the Uses tab, open decisions on the
// domain's record page.
export function CountLinks({ item, route }: { item: DataDomainItem; route: RouteOf<'data'> }) {
  const c = item.counts;
  if (!c.mappings && !c.uses && !c.openDecisions) return <Muted>No data mapped or used</Muted>;
  return (
    <span className="flex flex-wrap gap-x-3 gap-y-0.5 text-sm">
      {c.mappings > 0 && <TextLink href={tabHref(route, 'mapping', {}, { domainKey: item.key })}>{plural(c.mappings, 'mapping', 'mappings')}</TextLink>}
      {c.uses > 0 && <TextLink href={tabHref(route, 'uses', {}, { domainKey: item.key })}>{plural(c.uses, 'use', 'uses')}</TextLink>}
      {c.openDecisions > 0 && <TextLink href={domainHref(item.key)}>{plural(c.openDecisions, 'open decision', 'open decisions')}</TextLink>}
    </span>
  );
}

export function NeverMark({ item }: { item: DataDomainItem }) {
  const never = (['use', 'ai', 'copy'] as const).some(n => item.rules[n].value === 'never');
  if (!never) return null;
  return <Lock className="size-3.5 shrink-0 text-danger" aria-label="A rule here is Never" />;
}

// ---------- uses ----------

// The short state words of a matrix cell (the full words are its title and its label for a screen reader).
export const STATE_SHORT: Record<DataUseState, string> = {
  'not-needed': 'No decision',
  needed: 'Needed',
  proposed: 'Waiting',
  allowed: 'Allowed',
  denied: 'Denied',
  're-decide': 'Decide again',
  'breaks-rule': 'Breaks rule',
  'no-one-to-ask': 'No one to ask',
};

export function UseState({ use }: { use: Pick<DataUseItem, 'state' | 'approvalsGiven' | 'approvalsNeeded'> }) {
  return <DataUseStateBadge state={use.state} approvalsGiven={use.approvalsGiven} approvalsNeeded={use.approvalsNeeded} />;
}

// ---------- locations ----------

// A domain a location holds, with its origin: "People / Payroll  SoR".
export function HeldChip({ path, domainKey, origin }: { path: string; domainKey: string; origin: keyof typeof DATA_ORIGIN_SHORT }) {
  return (
    <span className="inline-flex max-w-full items-center gap-1 rounded-md border border-line bg-card px-1.5 py-0.5 text-xs text-navy">
      <TextLink href={domainHref(domainKey)} className="truncate text-xs">{path}</TextLink>
      <span className={cn('shrink-0 font-semibold', origin === 'production-copy' ? 'text-warning' : 'text-navy-3')}>{DATA_ORIGIN_SHORT[origin]}</span>
    </span>
  );
}

export function locationLabel(locations: readonly DataLocationItem[], id: string | null | undefined): string | null {
  if (!id) return null;
  return locations.find(l => sameId(l.dataLocationId, id))?.label ?? null;
}

// ---------- states ----------

export function NoDomainsYet() {
  return (
    <div className="flex flex-col gap-2 text-sm leading-relaxed text-navy-2">
      <p className="m-0">
        No data domain is in the configuration yet. The tree is drawn per install: a configuration admin adds the business
        data domains, their levels and classifications through a configuration change, for example starting from the data
        domains starter.
      </p>
      <TextLink href={hrefTo({ page: 'configuration' })} className="self-start">Open the configuration</TextLink>
    </div>
  );
}

// What a list shows when the page did not read every row the tool holds, or a later page failed.
export function ListNotes({ reads, one, many }: { reads: RowsRead<unknown>[]; one: string; many: string }) {
  const notRead = reads.reduce((n, r) => n + (r.notRead ?? 0), 0);
  const later = reads.find(r => r.laterFailure)?.laterFailure ?? null;
  if (!notRead && !later) return null;
  return (
    <div className="flex flex-col gap-1 text-xs text-navy-3">
      {notRead > 0 && <span>{`${plural(notRead, `more ${one} is`, `more ${many} are`)} not shown. Narrow to one domain or application to see them.`}</span>}
      {later && <span>{`Some ${many} could not be read: ${later.error.message} Reference ${later.correlationId}.`}</span>}
    </div>
  );
}

export function pagedFailure<T, D>(read: PagedState<T, D>): Failure | null {
  return read.status === 'failed' ? read.failure : null;
}

export const checkTitle = (c: DataCheckCode) => DATA_CHECKS[c].title;
