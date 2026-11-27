// Building blocks the Deployments, Places and Gap check pages share: the target reads, the application picker of the
// sheets, the target choice, the filter chips, the result and route words, and the deployment rows of a list.
import { ArrowRight, Search, X } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { hrefTo, recordHref, recordHrefOf } from '../../app/links';
import type { DeploymentItem, DeploymentTargetItem } from '../../data/contracts';
import { fmtDateTime } from '../../data/format';
import { useLabels } from '../../data/labels';
import { usePagedRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf, useTool } from '../../data/toolClient';
import { Field } from '../../components/actions';
import { Empty, Muted, StatusBadge, TextLink, ToneBadge, useNarrow } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/table';
import { cn } from '../../lib/utils';
import { gateLevelLabel, withAppHint } from '../changes-records/logic';
import { FlagBadges, SelectField, type Choice } from '../changes-records/parts';
import { isFlagged } from './logic';

export const subhead = 'm-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';
export const plainText = 'm-0 text-sm leading-relaxed text-navy-2';
// Two columns on a wide screen (list and side panels), one below 1024 px.
export const twoColumns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
export const column = 'flex min-w-0 flex-col gap-4 narrow:gap-3';
export const radioClass = 'size-4 accent-[var(--primary)]';

// ---------- reads ----------

// Every registered target, up to five pages of a hundred: the Places page, the target filter and the target choices.
export function useTargets(input: Record<string, unknown> = {}) {
  const read = usePagedRead('list_deployment_targets', { top: 100, ...input }, d => d.items, { maxPages: 5 });
  return {
    read,
    items: read.status === 'done' ? read.items : [],
    total: read.status === 'done' ? read.first.data.totalCount : null,
  };
}

// The record page of a deployment, with the application hint that lets it find the record at once.
export function deploymentHref(d: Pick<DeploymentItem, 'recordUrl' | 'deploymentRecordId' | 'businessApplicationId'>): string | null {
  return withAppHint(recordHref(d.recordUrl) ?? recordHrefOf('deployment-record', d.deploymentRecordId), d.businessApplicationId);
}

export const appHref = (id: string | null | undefined) => (id ? hrefTo({ page: 'app', id: id.toLowerCase() }) : null);
export const placeHref = (id: string | null | undefined) => (id ? hrefTo({ page: 'place', id: id.toLowerCase() }) : null);

// ---------- filter chips ----------

export function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex max-w-full items-center gap-1 rounded-md border border-info-line bg-info-soft py-0.5 pr-1 pl-2 text-xs font-semibold text-brand-dark">
      <span className="truncate">{label}</span>
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Remove the filter ${label}`}
        className="inline-flex size-4 shrink-0 cursor-pointer items-center justify-center rounded-sm border-0 bg-transparent p-0 text-brand-dark hover:bg-card focus-visible:outline-2 focus-visible:outline-ring"
      >
        <X className="size-3" aria-hidden />
      </button>
    </span>
  );
}

// ---------- the application picker ----------

export interface AppChoice {
  id: string;
  name: string;
}

// The applications the signed-in person holds an owner role on, by name.
export function useOwnedApps(enabled = true): AppChoice[] {
  const session = useSession();
  const owned = useTool('list_owner_assignments', enabled && session.objectId ? { principalObjectId: session.objectId, state: 'current', top: 100 } : null);
  return useMemo(() => {
    const seen = new Map<string, string>();
    for (const o of dataOf(owned)?.items ?? []) if (!seen.has(o.businessApplicationId.toLowerCase())) seen.set(o.businessApplicationId.toLowerCase(), o.applicationName);
    return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
  }, [owned]);
}

// Search the register by name or purpose, or pick an application the person owns. Inside a sheet it is a field (the
// chosen application, with Change); on a page, onPick may navigate instead.
export function AppPicker({ onPick, label = 'Search applications by name or purpose', className }: { onPick: (app: AppChoice) => void; label?: string; className?: string }) {
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  useEffect(() => {
    const t = window.setTimeout(() => setQuery(text.trim()), 300);
    return () => window.clearTimeout(t);
  }, [text]);
  const found = useTool('find_applications', query ? { text: query, top: 8 } : null);
  const owned = useOwnedApps();
  const results: (AppChoice & { note?: string })[] = query
    ? (dataOf(found)?.items ?? []).map(a => ({ id: a.businessApplicationId, name: a.name, note: [a.kind.label, a.tier?.label ?? 'No tier yet', a.stage?.label].filter(Boolean).join('. ') }))
    : owned;
  return (
    <div className={cn('flex flex-col gap-2', className)}>
      <div className="relative max-w-[480px]">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
        <Input type="search" value={text} onChange={e => setText(e.target.value)} placeholder="Search by name or purpose" aria-label={label} className="pl-8" maxLength={200} />
      </div>
      {query && found.status === 'loading' && <Muted>Searching.</Muted>}
      {query && found.status === 'done' && !found.envelope.ok && <Muted>{`The search could not be read: ${found.envelope.error.message}`}</Muted>}
      {!query && owned.length > 0 && <span className={subhead}>Applications you own</span>}
      {results.length > 0 ? (
        <ul className="m-0 flex max-w-[640px] list-none flex-col gap-1.5 p-0" aria-label={query ? 'Matching applications' : 'Applications you own'} aria-live="polite">
          {results.map(a => (
            <li key={a.id}>
              <button
                type="button"
                onClick={() => onPick({ id: a.id.toLowerCase(), name: a.name })}
                className="flex w-full cursor-pointer items-center justify-between gap-3 rounded-md border border-line bg-card px-3 py-2 text-left hover:border-primary focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
              >
                <span className="flex min-w-0 flex-col">
                  <span className="text-sm font-semibold [overflow-wrap:anywhere] text-navy">{a.name}</span>
                  {a.note && <span className="text-xs text-navy-3">{a.note}</span>}
                </span>
                <ArrowRight className="size-4 shrink-0 text-navy-3" aria-hidden />
              </button>
            </li>
          ))}
        </ul>
      ) : query && found.status === 'done' && found.envelope.ok ? <Muted>No registered application matches.</Muted>
        : !query ? <Muted>Type to search the register.</Muted> : null}
    </div>
  );
}

// The application field of a sheet: the chosen one with Change, or the picker.
export function AppField({ label, name, value, onChange, hint, required }: { label: string; name: string; value: AppChoice | null; onChange: (app: AppChoice | null) => void; hint?: ReactNode; required?: boolean }) {
  return (
    <Field label={label} name={name} hint={hint} required={required}>
      {p => (value ? (
        <div className="flex items-center gap-2 rounded-md border border-line bg-secondary px-3 py-2">
          <span id={p.id} aria-describedby={p['aria-describedby']} className="min-w-0 flex-1 text-sm font-semibold [overflow-wrap:anywhere] text-navy">{value.name}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => onChange(null)} aria-label={`Choose an application other than ${value.name}`}>
            <X className="size-4" aria-hidden />Change
          </Button>
        </div>
      ) : <AppPicker onPick={onChange} />)}
    </Field>
  );
}

// ---------- the target choice ----------

export function targetChoices(targets: DeploymentTargetItem[]): Choice[] {
  return [...targets]
    .sort((a, b) => a.targetStage.rank - b.targetStage.rank || a.label.localeCompare(b.label))
    .map(t => ({ value: t.deploymentTargetId.toLowerCase(), label: `${t.label} (${t.targetStage.label})` }));
}

export function TargetSelectField({ targets, value, onChange, label = 'Target', name = 'deploymentTargetId', required, hint }: { targets: DeploymentTargetItem[]; value: string; onChange: (id: string) => void; label?: string; name?: string; required?: boolean; hint?: ReactNode }) {
  const choices = targetChoices(targets);
  return (
    <SelectField
      label={label}
      name={name}
      value={value}
      onChange={onChange}
      choices={choices}
      required={required}
      hint={hint}
      placeholder={choices.length ? 'Choose a target' : 'No target is registered yet'}
    />
  );
}

type ChangePathLike = Pick<DeploymentTargetItem['changePath'], 'key' | 'allowedRoutes'>;

// The routes a target's change path allows. list_deployment_targets returns them empty today (it reads a column the
// change path row does not have), so the configuration's own Allowed Route Keys fill in.
export function useAllowedRoutes(): (path: ChangePathLike) => string[] {
  const paths = useLabels('change-path');
  return path => (path.allowedRoutes.length ? path.allowedRoutes : paths.field<string[]>(path.key, 'allowedRouteKeys') ?? []);
}

// The routes a change path allows, in their configured words: "Pipeline step or Owner's AI tool".
export function useRouteWords(): (keys: string[] | ChangePathLike) => string {
  const routes = useLabels('deployment-route');
  const allowed = useAllowedRoutes();
  return keysOrPath => {
    const keys = Array.isArray(keysOrPath) ? keysOrPath : allowed(keysOrPath);
    const words = keys.map(k => routes.label(k));
    if (!words.length) return 'No route';
    return words.length === 1 ? words[0] : `${words.slice(0, -1).join(', ')} or ${words[words.length - 1]}`;
  };
}

// ---------- deployment rows ----------

// A list of deployment records: a table on a wide screen, rows below 720 px and in a side column (compact). Each row
// opens its record; a flagged row can carry a follow-up action.
export function DeploymentRows({ items, corrected, followUp, showApp = true, showTarget = true, compact = false, empty }: {
  items: DeploymentItem[];
  corrected?: Set<string>;
  followUp?: (d: DeploymentItem) => ReactNode;
  showApp?: boolean;
  showTarget?: boolean;
  compact?: boolean;
  empty: ReactNode;
}) {
  const narrow = useNarrow() || compact;
  const routes = useLabels('deployment-route');
  const stages = useLabels('target-stage');
  if (!items.length) return <Empty>{empty}</Empty>;
  const title = (d: DeploymentItem) => `${showApp ? `${d.applicationName ?? 'Unknown application'} ` : `${d.artifactName} `}${d.artifactVersion}`;
  const isCorrected = (d: DeploymentItem) => corrected?.has(d.deploymentRecordId.toLowerCase()) ?? false;
  const notes = (d: DeploymentItem) => [
    d.correctsRecordId ? 'Corrects an earlier record' : null,
    isCorrected(d) ? 'Corrected by a later record' : null,
  ].filter(Boolean).join('. ');

  if (narrow) {
    return (
      <RowList label="Deployments">
        {items.map(d => {
          const href = deploymentHref(d);
          const action = isFlagged(d.flags) ? followUp?.(d) : null;
          return (
            <Row
              key={d.deploymentRecordId}
              wideEnd
              primary={href ? <TextLink href={href}>{title(d)}</TextLink> : title(d)}
              secondary={[showTarget ? `${d.targetLabel ?? 'Unknown target'}${d.targetStageKey ? ` (${stages.label(d.targetStageKey).toLowerCase()})` : ''}` : null, `Finished ${fmtDateTime(d.finishedOn)}`].filter(Boolean).join('. ')}
              tertiary={[`${routes.label(d.route.key)}. Gate level ${gateLevelLabel[d.gateLevelApplied] ?? d.gateLevelApplied}`, notes(d)].filter(Boolean).join('. ')}
              badges={(
                <>
                  <FlagBadges flags={d.flags} />
                  <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
                  {action}
                </>
              )}
              end={<StatusBadge status={d.result} />}
            />
          );
        })}
      </RowList>
    );
  }
  return (
    <Table aria-label="Deployments">
      <TableHeader>
        <TableRow>
          <TableHead>Deployment</TableHead>
          {showTarget && <TableHead>Target</TableHead>}
          <TableHead>Finished</TableHead>
          <TableHead>Route and gate</TableHead>
          <TableHead>Result and flags</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map(d => {
          const href = deploymentHref(d);
          const place = placeHref(d.deploymentTargetId);
          const action = isFlagged(d.flags) ? followUp?.(d) : null;
          return (
            <TableRow key={d.deploymentRecordId} className="align-top">
              <TableCell className="whitespace-normal">
                <div className="flex flex-col items-start gap-0.5">
                  {href ? <TextLink href={href} className="text-sm">{title(d)}</TextLink> : <span className="text-sm font-semibold text-navy">{title(d)}</span>}
                  {showApp && <Muted>{d.artifactName}</Muted>}
                  {notes(d) && <Muted>{notes(d)}</Muted>}
                  <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} className="mt-0.5" />
                </div>
              </TableCell>
              {showTarget && (
                <TableCell className="whitespace-normal">
                  {place && d.targetLabel ? <TextLink href={place} className="text-sm">{d.targetLabel}</TextLink> : <span className="text-sm text-navy">{d.targetLabel ?? 'Unknown target'}</span>}
                  {d.targetStageKey && <div className="text-xs text-navy-3">{stages.label(d.targetStageKey)}</div>}
                </TableCell>
              )}
              <TableCell className="text-sm whitespace-normal text-navy">{fmtDateTime(d.finishedOn)}</TableCell>
              <TableCell className="whitespace-normal">
                <span className="text-sm text-navy">{routes.label(d.route.key)}</span>
                <div className="text-xs text-navy-3">{`Gate level ${gateLevelLabel[d.gateLevelApplied] ?? d.gateLevelApplied}`}</div>
              </TableCell>
              <TableCell className="whitespace-normal">
                <div className="flex flex-col items-start gap-1">
                  <StatusBadge status={d.result} />
                  <span className="flex flex-wrap gap-1">
                    {isFlagged(d.flags) ? <FlagBadges flags={d.flags} /> : <ToneBadge tone="subtle" icon={null}>No flag</ToneBadge>}
                  </span>
                  {action}
                </div>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

// A small "Show more" under a list read page by page.
export function ShowMore({ shown, skipToken, loading, onMore, what }: { shown: number; skipToken: string | undefined; loading: boolean; onMore: () => void; what: string }) {
  if (!skipToken) return null;
  return (
    <div className="flex flex-wrap items-center gap-3 pt-1">
      <Button variant="outline" size="sm" onClick={onMore} disabled={loading}>{loading ? 'Reading more' : `Show more ${what}`}</Button>
      <Muted>{`${shown} shown. The product holds more.`}</Muted>
    </div>
  );
}
