// Gap check and claims (screen "Gap check and claims", priority 2; D-15): find what is live in the estate but not
// registered, and claim or dismiss it; resolve what the register holds that has gone.
//   /app/gap-check?view=open|claimed|dismissed|vanished|runs   RouteOf<'gap-check'>; the page's own: mine=1, kind=<key>
//   /app/records/gap-check-run/<id>                            one run (the record link list_gap_check_runs returns)
// The claim record page (/app/records/claim/<id>) is ClaimRecord in Records.tsx.
// Reads: list_claims (every state, page by page; suggestedToMe, componentKindKeys), list_gap_check_runs (follows
// claim-visibility).
// Writes, each in the shared side sheet: run_gap_check (platform admin, as themselves with their own admin role),
// compare_inventory_list (the same check over a list from the admin's own tool), run_owner_check, claim_resource (into
// an existing application, or a new one, which starts a draft intake request), dismiss_claim (with undo),
// link_component (a vanished item: gone or moved).
// The menu shows this page only to a person whose roles give list_claims or list_gap_check_runs.
//
// Design choices (D-41), recorded here:
// - One read of every claim (up to 1,000) feeds the figures, the views and the kind bars, so they always agree.
// - Wave 4: the text search q= keeps the claims whose name, kind, place, finding, suggested maker or claimant hold
//   the words (list_claims takes no words). The figures, the views and the kind bars follow it, so each number opens
//   its rows; the runs view lists the runs as before.
// - Each row acts in place: Claim and Dismiss on an item not registered, Resolve and Still there on a vanished one,
//   Reopen on a dismissed one. One sheet at a time opens for the row.
// - A run that has more pages offers Continue at once: the next page needs the run's own continuation token, which only
//   the answer to the last page carries.
// - Directory reads may be off (the demo), so Compare a list sits beside Run the gap check: the same comparison over a
//   list the admin pasted, marked as reported.
// - "Owner differs" on matched items is not shown: the gap check does not work it out yet (a backend slice).
import { ArrowRight, ListChecks, Play, UserRoundSearch } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../app/links';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import { navigate } from '../app/navigation';
import { GAP_CHECK_VIEWS, type GapCheckView, type RouteOf } from '../app/routes';
import type { ClaimItem, GapCheckRunList } from '../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../data/format';
import { usePagedRead, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf } from '../data/toolClient';
import { BarList, DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { Label } from '../components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import { cn } from '../lib/utils';
import { samePrincipal } from './changes-records/logic';
import { Fact, Facts, LookupState, recordGrid } from './changes-records/parts';
import { claimCounts, claimsInView, claimsOfRun, kindBuckets, lastSeenText, runSourceText, sameId } from './deployments-places/logic';
import { Chip, column, plainText, subhead, twoColumns } from './deployments-places/parts';
import { CompareListSheet, OwnerCheckSheet, ResolveVanishedSheet, RunGapCheckSheet, type RunContinuation } from './deployments-places/sheets';
import { ClaimedFacts, ClaimSheet, DismissSheet, FINDING_TEXT } from './Records';
import { Sticky } from './work-approvals/parts';

const KEY = /^[a-z][a-z0-9-]{0,63}$/;

const VIEW_TEXT: Record<GapCheckView, { tab: string; title: string; empty: string }> = {
  open: { tab: 'Not registered', title: 'Not registered', empty: 'Nothing unregistered is waiting. Every item the last run found is claimed or dismissed.' },
  vanished: { tab: 'Vanished', title: 'Vanished', empty: 'No registered item has gone from the estate.' },
  claimed: { tab: 'Claimed', title: 'Claimed', empty: 'No item is claimed yet.' },
  dismissed: { tab: 'Dismissed', title: 'Dismissed', empty: 'No item is dismissed.' },
  runs: { tab: 'Runs', title: 'Runs', empty: 'No gap check has run yet.' },
};

type RowAction = 'claim' | 'dismiss' | 'still-there' | 'undo' | 'resolve';
type Run = GapCheckRunList['items'][number];

export function GapCheck({ route }: { route: RouteOf<'gap-check'> }) {
  const session = useSession();
  const view: GapCheckView = route.view ?? 'open';
  const mine = useQueryParam('mine', /^1$/) === '1';
  const kind = useQueryParam('kind', KEY);
  const claims = usePagedRead('list_claims', { statusMeanings: ['open', 'claimed', 'dismissed'], top: 100, ...(mine ? { suggestedToMe: true } : {}), ...(kind ? { componentKindKeys: [kind] } : {}) }, d => d.items, { maxPages: 10 });
  const mayRuns = session.known && session.can('list_gap_check_runs');
  // The runs figure shows while whoami is read (as a placeholder) and to a person whose roles show the run history.
  const showRuns = !session.known || mayRuns;
  const runs = useRead('list_gap_check_runs', mayRuns ? { top: 25 } : null);
  const [active, setActive] = useState<{ claim: ClaimItem; action: RowAction } | null>(null);
  const [continuation, setContinuation] = useState<RunContinuation | null>(null);
  // The continuation the sheet answered last: kept after the final page, so the engine's answer to that page stays in
  // view in the Continue sheet once the banner goes (wave 4b, found by the demo's S-22.15: the sheet unmounted on the
  // last page and its answer was never shown).
  const [answered, setAnswered] = useState<RunContinuation | null>(null);
  const continueRun = (d: { more?: boolean | null; skipToken?: string | null; gapCheckRunId?: string | null }, input: Omit<RunContinuation, 'gapCheckRunId' | 'skipToken'>) => {
    const next = d.more && d.skipToken && d.gapCheckRunId ? { gapCheckRunId: d.gapCheckRunId, skipToken: d.skipToken, ...input } : null;
    if (!next && continuation) setAnswered(continuation);
    setContinuation(next);
  };

  const text = useListText();
  const items = filterByText(claims.status === 'done' ? claims.items : [], text, c => [
    c.labelAsSeen,
    c.componentKind?.label,
    c.targetAsSeen,
    FINDING_TEXT[c.findingKind].label,
    c.suggestedClaimant?.displayLabel,
    c.claimedBy?.displayLabel,
  ]) as ClaimItem[];
  const counts = claimCounts(items);
  const shown = claimsInView(items, view);
  const runItems = dataOf(runs)?.items ?? [];
  const runTotal = dataOf(runs)?.totalCount ?? null;
  const latest = runItems[0] ?? null;
  const me = session.objectId;

  type Params = { mine?: string; kind?: string; q?: string };
  const params: Params = { mine: mine ? '1' : undefined, kind, q: text };
  const go = (next: { view?: GapCheckView }, nextParams: Params = params) => {
    const v = 'view' in next ? next.view : route.view;
    navigate(withParams(hrefTo({ page: 'gap-check', ...(v && v !== 'open' ? { view: v } : {}) }), nextParams), true);
  };
  const kindLabel = items.find(c => c.componentKind?.key === kind)?.componentKind?.label ?? kind;
  const anyFilter = mine || !!kind || !!text;
  const control = (action: RowAction) => (active && active.action === action ? { open: true, onOpenChange: (o: boolean) => { if (!o) setActive(null); } } : undefined);
  const claimsDone = claims.status === 'done';
  const actionsFor = (c: ClaimItem) => {
    const m = c.status.meaning;
    const open = (action: RowAction) => () => setActive({ claim: c, action });
    const btn = (action: RowAction, label: string, variant: 'default' | 'outline' = 'outline') => (
      <Button key={action} size="sm" variant={variant} className="h-7" onClick={open(action)} aria-label={`${label}: ${c.labelAsSeen}`}>{label}</Button>
    );
    const list: ReactNode[] = [];
    if (m === 'open' && c.findingKind === 'unregistered') {
      if (session.can('claim_resource')) list.push(btn('claim', 'Claim', 'default'));
      if (session.can('dismiss_claim')) list.push(btn('dismiss', 'Dismiss'));
    } else if (m === 'open') {
      if (session.can('link_component')) list.push(btn('resolve', 'Resolve', 'default'));
      if (session.can('dismiss_claim')) list.push(btn('still-there', 'Still there'));
    } else if (m === 'dismissed' && session.can('dismiss_claim')) {
      list.push(btn('undo', 'Reopen'));
    }
    return list.length ? <span className="flex flex-wrap justify-end gap-1.5">{list}</span> : null;
  };

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Gap check"
        subtitle="What runs in the estate that is not in the register, and what the register holds that has gone. Claim an item into its application, start an intake for a new one, or dismiss it with the reason."
        meta={<DataStamp envelope={claims.status === 'done' ? claims.first : undefined} />}
        actions={(
          <>
            {session.can('run_gap_check') && <RunGapCheckSheet trigger={<Button><Play aria-hidden />Run the gap check</Button>} onRan={continueRun} />}
            {session.can('compare_inventory_list') && <CompareListSheet trigger={<Button variant="outline"><ListChecks aria-hidden />Compare a list</Button>} />}
            {session.can('run_owner_check') && <OwnerCheckSheet trigger={<Button variant="outline"><UserRoundSearch aria-hidden />Check the owners</Button>} />}
          </>
        )}
      />

      {(continuation ?? answered) && session.can('run_gap_check') && (
        <Sticky show={!!continuation}>
          <div className="mb-4 flex flex-wrap items-center gap-3 rounded-lg border border-warning-line bg-warning-soft px-3 py-2.5 text-sm text-navy" role="status">
            <span className="min-w-0 flex-1">The run has more pages. Continue it to read the rest of the estate; vanished items are raised only after the last page.</span>
            <RunGapCheckSheet
              continuation={(continuation ?? answered)!}
              trigger={<Button size="sm">Continue the run</Button>}
              onRan={continueRun}
            />
          </div>
        </Sticky>
      )}

      {claims.status === 'failed' ? (
        <div className="mb-4"><PanelFailure failure={claims.failure} /></div>
      ) : (
        <div className={cn('mb-4 grid gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5', showRuns ? 'grid-cols-5' : 'grid-cols-4')} role="group" aria-label="Key figures">
          <KpiTile label="Not registered" value={claimsDone ? counts.open : null} caption="Open claims to claim or dismiss" attention={counts.open > 0} onClick={() => go({ view: 'open' })} />
          <KpiTile label="Vanished" value={claimsDone ? counts.vanished : null} caption="Registered, no longer found" attention={counts.vanished > 0} onClick={() => go({ view: 'vanished' })} />
          <KpiTile label="Claimed" value={claimsDone ? counts.claimed : null} caption="Now in an application" onClick={() => go({ view: 'claimed' })} />
          <KpiTile label="Dismissed" value={claimsDone ? counts.dismissed : null} caption="Not a business application" onClick={() => go({ view: 'dismissed' })} />
          {showRuns && <KpiTile label="Runs" value={runTotal} caption="Gap checks run so far" onClick={() => go({ view: 'runs' })} />}
        </div>
      )}

      <section aria-label="Filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2.5">
          <SwitchTabs<GapCheckView>
            label="Show"
            ariaLabel="Which claims"
            value={view}
            onChange={v => go({ view: v })}
            options={GAP_CHECK_VIEWS.map(v => ({ value: v, label: VIEW_TEXT[v].tab }))}
          />
          {view !== 'runs' && <ListSearch label="Search the items by name, kind, place, finding or person" placeholder="Item, kind or place" />}
          {view !== 'runs' && (
            <div className="flex items-center gap-2">
              <Checkbox id="gap-mine" checked={mine} onCheckedChange={v => go({}, { ...params, mine: v === true ? '1' : undefined })} />
              <Label htmlFor="gap-mine" className="font-normal">Only items suggested to me</Label>
            </div>
          )}
        </div>
        {anyFilter && view !== 'runs' && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {mine && <Chip label="Suggested to me" onRemove={() => go({}, { ...params, mine: undefined })} />}
            {kind && <Chip label={`Kind: ${kindLabel}`} onRemove={() => go({}, { ...params, kind: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => go({}, { ...params, q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={() => go({}, {})}>Clear all</Button>
          </div>
        )}
      </section>

      <div className={twoColumns}>
        {view === 'runs' ? (
          <Panel title="Runs" count={runTotal ?? undefined} state={mayRuns ? runs : undefined} skeletonRows={6}>
            {mayRuns ? (d: GapCheckRunList) => (d.items.length ? <RunsList runs={d.items} /> : <Empty>{VIEW_TEXT.runs.empty}</Empty>)
              : session.known ? <Empty>Your roles do not show the run history. The platform admins, the AI lead and the auditor see it.</Empty> : <Muted>Reading who you are.</Muted>}
          </Panel>
        ) : (
          <Panel
            title={VIEW_TEXT[view].title}
            count={claimsDone ? shown.length : undefined}
            state={claims.status === 'done' ? { status: 'done', envelope: claims.first } : claims.status === 'loading' ? { status: 'loading' } : { status: 'done', envelope: claims.failure }}
            skeletonRows={6}
          >
            {() => (
              <>
                {shown.length ? (
                  <RowList label={VIEW_TEXT[view].title}>
                    {shown.map(c => <ClaimRow key={c.claimId} claim={c} me={me} actions={actionsFor(c)} />)}
                  </RowList>
                ) : (
                  <span className="flex flex-col items-start gap-2">
                    <Empty>{anyFilter ? 'No claim matches these filters.' : VIEW_TEXT[view].empty}</Empty>
                    {anyFilter && <Button variant="outline" size="sm" onClick={() => go({}, {})}>Clear the filters</Button>}
                  </span>
                )}
                {claims.status === 'done' && claims.skipToken && <Muted>The product holds more than 1,000 claims. Narrow the list by kind to see the rest.</Muted>}
              </>
            )}
          </Panel>
        )}

        <div className={column}>
          {mayRuns && (
            <Panel title="Latest run" state={runs} actions={latest ? <TextLink href={hrefTo({ page: 'gap-check', view: 'runs' })} className="text-sm">All runs</TextLink> : undefined} notes={[]}>
              {() => (latest ? <RunSummary run={latest} /> : (
                <p className={plainText}>
                  {session.can('run_gap_check')
                    ? 'No gap check has run yet. Run it from the estate, or compare a list from your own tool if the directory reads are off.'
                    : 'No gap check has run yet. A platform admin runs it.'}
                </p>
              ))}
            </Panel>
          )}
          {view !== 'runs' && (
            <Panel title="By kind" subtitle={`${VIEW_TEXT[view].title} items`} state={claims.status === 'done' ? { status: 'done', envelope: claims.first } : { status: 'loading' }} notes={[]}>
              {() => {
                const buckets = kindBuckets(shown);
                return buckets.length ? (
                  <BarList
                    ariaLabel={`${VIEW_TEXT[view].title} items by kind`}
                    noun={['item', 'items']}
                    labelWidth={170}
                    items={buckets.map(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }))}
                    onDrill={b => b.key && go({}, { ...params, kind: b.key })}
                  />
                ) : <Empty>No item to count.</Empty>;
              }}
            </Panel>
          )}
          <Panel title="How it works">
            <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm leading-relaxed text-navy-2">
              <li>A platform admin runs the check as themselves, with their own admin role. The product reads the estate only then, and only as them.</li>
              <li>Each item no application holds opens a claim, suggested to the maker the estate reports.</li>
              <li>Claim it into an application: it becomes a component. A new application starts an intake request that triage decides.</li>
              <li>A registered item the estate no longer shows is vanished: mark it gone or moved, or say it is still there.</li>
            </ul>
          </Panel>
        </div>
      </div>

      {active && (
        <>
          <ClaimSheet key={`claim-${active.claim.claimId}`} claim={active.claim} show={false} control={control('claim')} />
          {(['dismiss', 'still-there', 'undo'] as const).map(v => (control(v) ? <DismissSheet key={`${v}-${active.claim.claimId}`} claim={active.claim} variant={v} show={false} control={control(v)} /> : null))}
          {control('resolve') && <ResolveVanishedSheet key={`resolve-${active.claim.claimId}`} claim={active.claim} open onOpenChange={o => { if (!o) setActive(null); }} />}
        </>
      )}
    </>
  );
}

function ClaimRow({ claim: c, me, actions }: { claim: ClaimItem; me: string | null; actions: ReactNode }) {
  const href = recordHrefOf('claim', c.claimId);
  const meaning = c.status.meaning;
  const suggestedToMe = samePrincipal(c.suggestedClaimant, me);
  const who = meaning === 'claimed'
    ? `Claimed${c.claimedBy ? ` by ${c.claimedBy.displayLabel}` : ''}${c.claimedOn ? `, ${fmtDate(c.claimedOn)}` : ''}`
    : c.suggestedClaimant ? `Suggested to ${suggestedToMe ? 'you' : c.suggestedClaimant.displayLabel}` : 'Nobody suggested';
  const next = meaning === 'claimed'
    ? c.intakeRequestId ? recordHrefOf('intake-request', c.intakeRequestId) : c.businessApplicationId ? hrefTo({ page: 'app', id: c.businessApplicationId.toLowerCase() }) : null
    : null;
  return (
    <Row
      wideEnd
      primary={href ? <TextLink href={href}>{c.labelAsSeen}</TextLink> : c.labelAsSeen}
      secondary={[c.componentKind?.label ?? 'Kind not known', c.targetAsSeen ? `in ${c.targetAsSeen}` : null].filter(Boolean).join(' ')}
      tertiary={(
        <>
          {[c.findingKind === 'vanished' ? `Last seen ${c.lastSeenOn ? fmtDate(c.lastSeenOn) : 'not known'}` : `Found ${fmtDate(c.foundOn)}`, who].join('. ')}
          {next && <>. <TextLink href={next} className="text-xs">{c.intakeRequestId ? 'Open its intake request' : 'Open its application'}</TextLink></>}
          {c.url && <>. <ExternalLink href={c.url}>Open it where it lives</ExternalLink></>}
        </>
      )}
      badges={suggestedToMe && meaning === 'open' ? <ToneBadge tone="brand" icon={null}>Suggested to you</ToneBadge> : undefined}
      end={(
        <>
          <StatusBadge status={c.status} />
          {actions}
        </>
      )}
    />
  );
}

function RunsList({ runs }: { runs: Run[] }) {
  const narrow = useNarrow();
  const href = (r: Run) => recordHref(r.recordUrl) ?? recordHrefOf('gap-check-run', r.gapCheckRunId);
  const title = (r: Run) => (r.startedOn ? fmtDateTime(r.startedOn) : 'Start not recorded');
  const state = (r: Run) => (r.state === 'finished' ? <ToneBadge tone="success">Finished</ToneBadge> : <ToneBadge tone="warning">Not finished</ToneBadge>);
  const counts = (r: Run) => `${r.itemsSeen ?? 0} seen, ${r.newClaims ?? 0} new, ${r.vanishedClaims ?? 0} vanished`;
  if (narrow) {
    return (
      <RowList label="Runs">
        {runs.map(r => (
          <Row
            key={r.gapCheckRunId}
            wideEnd
            primary={href(r) ? <TextLink href={href(r)!}>{title(r)}</TextLink> : title(r)}
            secondary={`${r.runBy?.displayLabel ?? 'Not recorded'}. ${runSourceText(r.adminRoleUsed).text}`}
            tertiary={`${r.estatesCovered.map(e => e.label).join(', ') || 'No estate named'}. ${counts(r)}.`}
            badges={<AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} />}
            end={state(r)}
          />
        ))}
      </RowList>
    );
  }
  return (
    <Table aria-label="Runs">
      <TableHeader>
        <TableRow>
          <TableHead>Started</TableHead>
          <TableHead>Run by</TableHead>
          <TableHead>Estates</TableHead>
          <TableHead>Found</TableHead>
          <TableHead>State</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {runs.map(r => (
          <TableRow key={r.gapCheckRunId} className="align-top">
            <TableCell className="whitespace-normal">
              {href(r) ? <TextLink href={href(r)!} className="text-sm">{title(r)}</TextLink> : <span className="text-sm text-navy">{title(r)}</span>}
              <AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} className="mt-1" />
            </TableCell>
            <TableCell className="text-sm whitespace-normal text-navy">
              {r.runBy?.displayLabel ?? 'Not recorded'}
              <div className="text-xs text-navy-3">{runSourceText(r.adminRoleUsed).text}</div>
            </TableCell>
            <TableCell className="text-sm whitespace-normal text-navy">{r.estatesCovered.map(e => e.label).join(', ') || 'None named'}</TableCell>
            <TableCell className="text-sm whitespace-normal text-navy tabular-nums">{counts(r)}</TableCell>
            <TableCell>{state(r)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function RunSummary({ run: r }: { run: Run }) {
  const href = recordHref(r.recordUrl) ?? recordHrefOf('gap-check-run', r.gapCheckRunId);
  return (
    <div className="flex flex-col gap-2">
      <span className="flex flex-wrap items-center gap-2 text-sm text-navy">
        {href ? <TextLink href={href}>{r.startedOn ? fmtDateTime(r.startedOn) : 'Open the run'}</TextLink> : null}
        {r.state === 'finished' ? <ToneBadge tone="success">Finished</ToneBadge> : <ToneBadge tone="warning">Not finished</ToneBadge>}
      </span>
      <span className="text-xs text-navy-2">{`${r.runBy?.displayLabel ?? 'Someone'}. ${runSourceText(r.adminRoleUsed).text}.`}</span>
      <span className="text-xs text-navy-2">{`${plural(r.itemsSeen ?? 0, 'item', 'items')} seen, ${plural(r.newClaims ?? 0, 'new claim', 'new claims')}, ${r.vanishedClaims ?? 0} vanished. ${lastSeenText(r)}`}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// One run
// ---------------------------------------------------------------------------------------------------------------------

// Wave 4 (B24-21): one read of the run by its ID (list_gap_check_runs gapCheckRunId), in place of a scan of the history.
export function GapCheckRunRecord({ gapCheckRunId }: { gapCheckRunId: string }) {
  const scan = usePagedRead('list_gap_check_runs', { gapCheckRunId, top: 1 }, d => d.items, { maxPages: 1 });
  const items = scan.status === 'done' ? scan.items : [];
  const found = items.find(r => sameId(r.gapCheckRunId, gapCheckRunId)) ?? null;
  if (!found) {
    const done = scan.status === 'done';
    const hidden = done ? scan.first.couldNotRead.find(c => c.kind === 'gap-check-run')?.count ?? 0 : 0;
    return (
      <>
        <PageHeader eyebrow="Gap check run" title={done ? 'Run not found' : scan.status === 'failed' ? 'The run cannot be shown' : 'Looking for the run'} />
        <LookupState
          title="The run"
          id={gapCheckRunId}
          pagesRead={scan.status === 'loading' ? scan.pagesRead : 0}
          failure={scan.status === 'failed' ? scan.failure : null}
          notFound={done ? (
            <p className={plainText}>
              {hidden
                ? 'The run history shows to the platform admins, the AI lead and the auditor, as the claim visibility setting says, so this run is hidden from you.'
                : 'The run is not in the run history you can see. The link may be old, or the run may have been removed.'}
            </p>
          ) : undefined}
          next={<TextLink href={hrefTo({ page: 'gap-check' })} className="inline-flex items-center gap-1 self-start text-sm">The gap check <ArrowRight className="size-4" aria-hidden /></TextLink>}
        />
      </>
    );
  }
  return <RunView run={found} envelope={scan.status === 'done' ? scan.first : undefined} />;
}

function RunView({ run: r, envelope }: { run: Run; envelope: Parameters<typeof DataStamp>[0]['envelope'] }) {
  const session = useSession();
  const source = runSourceText(r.adminRoleUsed);
  const claims = usePagedRead('list_claims', { statusMeanings: ['open', 'claimed', 'dismissed'], top: 100 }, d => d.items, { maxPages: 10 });
  const found = claims.status === 'done' ? claimsOfRun(claims.items, r.startedOn) : [];
  const me = session.objectId;
  return (
    <>
      <PageHeader
        eyebrow="Gap check run"
        title={r.startedOn ? `Gap check, ${fmtDate(r.startedOn)}` : 'Gap check run'}
        status={r.state === 'finished' ? <ToneBadge tone="success">Finished</ToneBadge> : <ToneBadge tone="warning">Not finished</ToneBadge>}
        subtitle={source.text}
        meta={(
          <span className="flex flex-col gap-1">
            <AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} className="self-start" />
            <StampLine performedBy={r.performedBy} />
            <DataStamp envelope={envelope} />
          </span>
        )}
        actions={(
          <Button variant="outline" asChild>
            <a href={hrefTo({ page: 'gap-check', view: 'runs' })}>All runs</a>
          </Button>
        )}
      />
      <div className={recordGrid}>
        <div className={column}>
          <Panel title="The run">
            <Facts>
              <Fact label="Run by">{r.runBy?.displayLabel ?? 'Not recorded'}</Fact>
              <Fact label={source.live ? 'Admin role used' : 'Source'} sub={source.live ? 'The product read the estate only as this person, with this role.' : 'A list from the admin\'s own tool, marked as reported.'}>
                {source.live ? (r.adminRoleUsed && r.adminRoleUsed !== 'unknown' ? r.adminRoleUsed : 'Not reported') : 'As reported'}
              </Fact>
              <Fact label="Estates">{r.estatesCovered.map(e => e.label).join(', ') || 'None named'}</Fact>
              <Fact label="Started">{r.startedOn ? fmtDateTime(r.startedOn) : 'Not recorded'}</Fact>
              <Fact label="Finished" sub={r.state === 'finished' ? undefined : 'Its last page was never read, so it raised no vanished item.'}>{r.finishedOn ? fmtDateTime(r.finishedOn) : 'Not finished'}</Fact>
              <Fact label="Items seen">{String(r.itemsSeen ?? 0)}</Fact>
              <Fact label="New claims">{String(r.newClaims ?? 0)}</Fact>
              <Fact label="Vanished">{String(r.vanishedClaims ?? 0)}</Fact>
              {/* Wave 4b review: the claims not registered and the vanished items still open, each counted apart. */}
              {typeof r.openVanishedLastSeen === 'number' ? (
                <>
                  <Fact label="Not registered, still open" sub="Open claims whose last sighting was this run.">{String(r.openClaimsLastSeen)}</Fact>
                  <Fact label="Vanished, still open" sub="Vanished items not yet resolved whose last sighting was this run.">{String(r.openVanishedLastSeen)}</Fact>
                </>
              ) : <Fact label="Open claims last seen in it">{String(r.openClaimsLastSeen)}</Fact>}
            </Facts>
          </Panel>
        </div>
        <div className={column}>
          <Panel
            title="Claims it raised"
            count={claims.status === 'done' ? found.length : undefined}
            subtitle="The claims found when this run started"
            state={claims.status === 'done' ? { status: 'done', envelope: claims.first } : claims.status === 'loading' ? { status: 'loading' } : { status: 'done', envelope: claims.failure }}
          >
            {() => (found.length ? (
              <RowList label="Claims it raised">
                {found.map(c => {
                  const href = recordHrefOf('claim', c.claimId);
                  return (
                    <Row
                      key={c.claimId}
                      primary={href ? <TextLink href={href}>{c.labelAsSeen}</TextLink> : c.labelAsSeen}
                      secondary={[FINDING_TEXT[c.findingKind].label, c.componentKind?.label, c.targetAsSeen].filter(Boolean).join('. ')}
                      tertiary={c.suggestedClaimant ? `Suggested to ${samePrincipal(c.suggestedClaimant, me) ? 'you' : c.suggestedClaimant.displayLabel}` : undefined}
                      end={<StatusBadge status={c.status} />}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>{(r.newClaims ?? 0) + (r.vanishedClaims ?? 0) > 0 ? 'The claims it raised are hidden from you, or were raised on a later page of the run.' : 'The run raised no claim: everything it saw is in the register.'}</Empty>)}
          </Panel>
          {found.some(c => c.status.meaning === 'claimed') && (
            <Panel title="Where the claimed items went">
              <div className="flex flex-col gap-4">
                {found.filter(c => c.status.meaning === 'claimed').map(c => (
                  <div key={c.claimId} className="flex flex-col gap-2">
                    <h3 className={subhead}>{c.labelAsSeen}</h3>
                    <ClaimedFacts claim={c} />
                  </div>
                ))}
              </div>
            </Panel>
          )}
        </div>
      </div>
    </>
  );
}
