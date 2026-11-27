// Documents (screen "Documents", priority 2): missing, stale and unconfirmed documents across applications (D-17).
//   /app/documents?state=all|missing|stale|draft|current&type=<key>&app=<id>&portfolio=<id>&mine=1   RouteOf<'documents'>
//   and the page's own owner=<objectId>|none (the rows of one document owner, or the missing ones nobody owns yet)
// The document record page (/app/records/document-link/<id>) is DocumentLinkRecord in Records.tsx: every linked row
// opens it, with the application named so it finds the record at once. A missing row opens its application's 360.
// Reads: list_documents (missing is computed, never stored; the counts of every state for the same filters; every page up
// to 1,000 rows), get_configuration (document types with their freshness, gate requirements, gate, tier, kind and status
// labels), list_portfolios, list_owner_assignments (the applications the person owns, for which acts show),
// get_application_card (the name of a filtered application).
// Writes, each in the shared side sheet: record_document_link (link a missing document, or any document from the
// header), confirm_document_checked (confirm a draft current, or a stale or current document still true). A person may
// make a gate-needed runbook or ADR current; an AI client may not (OP-08), so its "current" waits here as a draft.
//
// Design choices (D-41, 2026-11-24), beyond those in guide-log-docs/documents.ts:
// - The tiles are list_documents' own counts for the filters it takes, so they count the whole store, not only the
//   rows read. A tile opens its state and clears the page's owner filter, so its number and its rows agree.
// - "Which gate needs each" lists every document type in force with the gates that need it current (gate requirement
//   rows), its freshness and whether a person must confirm it. Each type filters the list.
// - One link sheet and one confirm sheet serve every row (guide-log-docs/DocumentSheets.tsx).
// - Wave 4: the text search q= keeps the rows whose application, document type, owner or link hold the words
//   (list_documents takes no words; the page reads up to 1,000 rows). The two bar charts follow the search, so each bar
//   opens its rows; the four figures stay the tool's counts for the filters.
import { FilePlus2, Info } from 'lucide-react';
import { useMemo, useState } from 'react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { DOCUMENT_STATES, type DocumentRouteState, type RouteOf } from '../app/routes';
import { BarList, DataStamp, Empty, KpiTile, LinkButton, MoreRowsNote, Muted, PageHeader, Panel, StatusBadge, SwitchTabs, TextLink, useNarrow } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { DocumentList, ListDocumentsInput, StatusValue } from '../data/contracts';
import { fmtDate, plural } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { useConfigRows, usePagedRead, useRead } from '../data/reads';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import { useSession } from '../data/session';
import { dataOf } from '../data/toolClient';
import { useHost } from '../harness';
import {
  ACT_LABELS,
  actOf,
  countByOwner,
  countByType,
  filterByOwner,
  gateNeedsOf,
  gateNeedText,
  mayAct,
  NO_OWNER,
  OWNER_PARAM,
  ownedApplications,
  stateText,
  type ActRights,
  type DocRow,
  type RowAct,
} from './guide-log-docs/documents';
import { ConfirmDocumentSheet, LinkDocumentSheet, type LinkTarget } from './guide-log-docs/DocumentSheets';
import { AppFilterBox, CheckFilter, envelopeOf, filterSectionClass, pagedToolState, ShowMoreRows, useApplicationName, useShown } from './guide-log-docs/parts';
import { Chip, kpiRowClass4, twoColumnClass } from './owners-reviews/people';

const STATE_LABELS: Record<DocumentRouteState, string> = {
  all: 'All',
  missing: 'Missing',
  stale: 'Stale',
  draft: 'Drafts',
  current: 'Current',
};

const LIST_TITLES: Record<DocumentRouteState, string> = {
  all: 'All documents',
  missing: 'Missing documents',
  stale: 'Stale documents',
  draft: 'Drafts to confirm',
  current: 'Current documents',
};

const MAX_PAGES = 10;
const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

type Next = Partial<Omit<RouteOf<'documents'>, 'page'>>;

const STATUS_WORDS: Record<Exclude<DocRow['state'], 'other'>, string> = {
  missing: 'Missing',
  stale: 'Stale',
  draft: 'Draft',
  current: 'Current',
};

// The status of a row as a badge: the configured label of its state (document-<state>), else the engine's word.
function rowStatus(row: DocRow, statuses: Labels): StatusValue | null {
  if (row.state === 'other') return null;
  const key = `document-${row.state}`;
  return { label: statuses.has(key) ? statuses.label(key) : STATUS_WORDS[row.state], meaning: row.state };
}

export function Documents({ route }: { route: RouteOf<'documents'> }) {
  const host = useHost();
  const today = host.today;
  const session = useSession();
  const state: DocumentRouteState = route.state ?? 'all';
  const owner = useQueryParam('owner', OWNER_PARAM)?.toLowerCase();
  const docTypes = useLabels('document-type');
  const statuses = useLabels('status');
  const portfolios = useRead('list_portfolios', { top: 100 });

  const input: ListDocumentsInput = { state, top: 100 };
  if (route.typeKeys?.length) input.documentTypeKeys = route.typeKeys;
  if (route.applicationId) input.businessApplicationId = route.applicationId;
  if (route.portfolioId) input.portfolioId = route.portfolioId;
  if (route.mine) input.mineOnly = true;
  const paged = usePagedRead('list_documents', input as Record<string, unknown>, (d: DocumentList) => d.items, { maxPages: MAX_PAGES });
  const list = pagedToolState(paged);
  const all = useMemo(() => (paged.status === 'done' ? paged.items : []), [paged]);
  const text = useListText();
  const rows = useMemo(() => filterByText(filterByOwner(all, owner), text, r => [r.applicationName, r.documentType.label, r.owner?.displayLabel, r.url, r.requiredFrom?.label]) as DocRow[], [all, owner, text]);
  const counts = paged.status === 'done' ? paged.first.data.counts : null;

  // Which acts show: the tools whoami lists, and the applications the person owns now.
  const canLink = session.can('record_document_link');
  const canConfirm = session.can('confirm_document_checked');
  const owned = useRead('list_owner_assignments', session.objectId && (canLink || canConfirm) ? { principalObjectId: session.objectId, state: 'current', top: 100 } : null);
  const rights: ActRights = useMemo(() => ({
    me: session.objectId,
    ownedApps: ownedApplications(dataOf(owned)?.items ?? []),
    otherRole: session.roleKeys.some(r => r !== 'application-owner'),
    canLink,
    canConfirm,
  }), [session.objectId, session.roleKeys, owned, canLink, canConfirm]);

  const [linkOpen, setLinkOpen] = useState(false);
  const [linkTarget, setLinkTarget] = useState<LinkTarget | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmRow, setConfirmRow] = useState<DocRow | null>(null);
  const act = (row: DocRow, kind: RowAct) => {
    if (kind === 'link') {
      setLinkTarget({ app: { id: row.businessApplicationId.toLowerCase(), name: row.applicationName }, typeKey: row.documentType.key, typeLabel: row.documentType.label });
      setLinkOpen(true);
    } else {
      setConfirmRow(row);
      setConfirmOpen(true);
    }
  };

  const go = (next: Next, ownerParam: string | undefined = owner) => {
    const target: RouteOf<'documents'> = {
      page: 'documents',
      state: route.state,
      typeKeys: route.typeKeys,
      applicationId: route.applicationId,
      portfolioId: route.portfolioId,
      mine: route.mine,
      ...next,
    };
    navigate(withParams(hrefTo(target), { owner: ownerParam, q: text }), true);
  };

  const chosenTypes = route.typeKeys ?? [];
  const toggleType = (k: string) => {
    const next = chosenTypes.includes(k) ? chosenTypes.filter(t => t !== k) : [...chosenTypes, k];
    go({ typeKeys: next.length ? next : undefined });
  };
  const portfolioItems = dataOf(portfolios)?.items ?? [];
  const portfolioName = (id: string) => {
    const p = portfolioItems.find(x => sameId(x.portfolioId, id));
    return p ? p.path.map(x => x.name).join(' / ') || p.name : 'Portfolio';
  };
  const knownApp = route.applicationId ? all.find(r => sameId(r.businessApplicationId, route.applicationId))?.applicationName ?? null : null;
  const appName = useApplicationName(route.applicationId, knownApp);
  const ownerLabel = owner === NO_OWNER ? 'No owner yet' : all.find(r => sameId(r.owner?.objectId, owner))?.owner?.displayLabel ?? 'One person';
  const anyFilter = chosenTypes.length > 0 || !!route.applicationId || !!route.portfolioId || !!route.mine || !!owner || !!text;
  const clearAll = () => navigate(withParams(hrefTo({ page: 'documents', state: route.state }), {}), true);
  const mayLinkAny = canLink && (rights.otherRole || rights.ownedApps.size > 0);
  // A long list shows 25 rows at a time, from the top again when a filter changes.
  const shownRows = useShown([state, chosenTypes.join(','), route.applicationId, route.portfolioId, route.mine, owner, text].join('|'));

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Documents"
        subtitle="Missing, stale and unconfirmed documents across the portfolio: by type, by owner, and which gate needs each."
        meta={<DataStamp envelope={envelopeOf(list)} />}
        actions={mayLinkAny ? (
          <Button variant="outline" onClick={() => { setLinkTarget(null); setLinkOpen(true); }}>
            <FilePlus2 aria-hidden />
            Link a document
          </Button>
        ) : undefined}
      />

      {paged.status !== 'failed' && (
        <div className={kpiRowClass4} role="group" aria-label="Key figures">
          <KpiTile label="Missing" value={counts?.missing ?? null} caption="Required, no link recorded" attention={!!counts?.missing} onClick={() => go({ state: 'missing' }, undefined)} />
          <KpiTile label="Stale" value={counts?.stale ?? null} caption="Past their freshness date" attention={!!counts?.stale} onClick={() => go({ state: 'stale' }, undefined)} />
          <KpiTile label="Drafts to confirm" value={counts?.draft ?? null} caption="Recorded, not confirmed current" onClick={() => go({ state: 'draft' }, undefined)} />
          <KpiTile label="Current" value={counts?.current ?? null} caption="Confirmed and in date" onClick={() => go({ state: 'current' }, undefined)} />
        </div>
      )}

      <div className="mb-4 flex items-start gap-x-2 rounded-lg border border-info-line bg-info-soft px-3 py-2.5 text-sm leading-5 text-navy-2" role="note">
        <Info className="mt-0.5 size-4 shrink-0 text-info" aria-hidden />
        <span>
          Missing is worked out from the documentation rules in force, never stored. A person confirms a draft current: an
          AI client can record a runbook or an architecture decision record only as a draft, and it waits here for a person.
        </span>
      </div>

      <section aria-label="Filters" className={filterSectionClass}>
        <SwitchTabs
          label="Show"
          ariaLabel="Document state"
          value={state}
          onChange={v => go({ state: v === 'all' ? undefined : v })}
          options={DOCUMENT_STATES.map(s => ({ value: s, label: STATE_LABELS[s] }))}
        />
        <div className="flex flex-wrap items-center gap-2">
          <CheckFilter label="Document type" options={docTypes.keys().map(k => ({ value: k, label: docTypes.label(k) }))} chosen={chosenTypes} onToggle={toggleType} />
          <Select value={route.portfolioId ?? 'all'} onValueChange={v => go({ portfolioId: v === 'all' ? undefined : v })}>
            <SelectTrigger className="w-[220px] narrow:flex-1" aria-label="Portfolio"><SelectValue /></SelectTrigger>
            <SelectContent className="max-h-[320px]">
              <SelectItem value="all">All portfolios</SelectItem>
              {portfolioItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{portfolioName(p.portfolioId)}</SelectItem>)}
            </SelectContent>
          </Select>
          {!route.applicationId && <AppFilterBox id="documents-app" onPick={id => go({ applicationId: id })} />}
          <ListSearch label="Search the documents by application, type, owner or link" placeholder="Application, type or owner" />
          <div className="flex items-center gap-2">
            <Checkbox id="documents-mine" checked={!!route.mine} onCheckedChange={v => go({ mine: v === true ? true : undefined })} />
            <Label htmlFor="documents-mine" className="font-normal">Only mine: documents I own, and the missing ones of applications I own</Label>
          </div>
        </div>
        {anyFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {chosenTypes.map(k => <Chip key={k} label={`Type: ${docTypes.label(k)}`} onRemove={() => toggleType(k)} />)}
            {route.applicationId && <Chip label={`Application: ${appName ?? 'reading the name'}`} onRemove={() => go({ applicationId: undefined })} />}
            {route.portfolioId && <Chip label={`Portfolio: ${portfolioName(route.portfolioId)}`} onRemove={() => go({ portfolioId: undefined })} />}
            {route.mine && <Chip label="Only mine" onRemove={() => go({ mine: undefined })} />}
            {owner && <Chip label={`Owner: ${ownerLabel}`} onRemove={() => go({}, undefined)} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => navigate(withParams(`${window.location.pathname}${window.location.search}`, { q: undefined }), true)} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearAll}>Clear all</Button>
          </div>
        )}
      </section>

      <div className={twoColumnClass}>
        <Panel title={LIST_TITLES[state]} count={paged.status === 'done' ? rows.length : undefined} state={list} skeletonRows={8}>
          {() => (
            <>
              {rows.length ? <DocumentRows rows={rows.slice(0, shownRows.shown)} today={today} statuses={statuses} rights={rights} onAct={act} /> : (
                <div className="flex flex-col items-start gap-2 py-1">
                  <Empty>
                    {state === 'missing' ? 'No required document is missing' : state === 'stale' ? 'No document is stale' : state === 'draft' ? 'No draft waits for confirmation' : state === 'current' ? 'No document is current' : 'No document is required or linked'}
                    {anyFilter ? ' with these filters.' : '.'}
                  </Empty>
                  {anyFilter && <Button variant="outline" size="sm" onClick={clearAll}>Clear the filters</Button>}
                </div>
              )}
              <ShowMoreRows shown={shownRows.shown} total={rows.length} what="documents" onMore={shownRows.more} />
              {paged.status === 'done' && paged.skipToken && (
                <div className="flex flex-col items-start gap-2">
                  <MoreRowsNote skipToken={paged.skipToken} what="documents" />
                  <Button variant="outline" size="sm" onClick={paged.more} disabled={paged.loadingMore}>{paged.loadingMore ? 'Reading more' : 'Read 100 more'}</Button>
                </div>
              )}
            </>
          )}
        </Panel>
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel title="By type" state={list} skeletonRows={4} notes={[]} subtitle={`${LIST_TITLES[state]} in the list`}>
            {() => {
              const buckets = countByType(rows);
              if (!buckets.length) return <Empty>No document to count.</Empty>;
              return <BarList ariaLabel="Documents by type" noun={['document', 'documents']} labelWidth={170} items={buckets} onDrill={b => b.key && go({ typeKeys: [b.key] })} />;
            }}
          </Panel>
          <Panel title="By owner" state={list} skeletonRows={4} notes={[]} subtitle={`${LIST_TITLES[state]} in the list. A missing document has no owner yet: the owners of its application link it.`}>
            {() => {
              const buckets = countByOwner(rows);
              if (!buckets.length) return <Empty>No document to count.</Empty>;
              return (
                <BarList
                  ariaLabel="Documents by owner"
                  noun={['document', 'documents']}
                  labelWidth={170}
                  items={buckets.map(b => ({ ...b, muted: b.key === NO_OWNER }))}
                  onDrill={b => b.key && go({}, b.key)}
                />
              );
            }}
          </Panel>
          <GateNeeds docTypes={docTypes} onPick={k => go({ typeKeys: [k] })} />
        </div>
      </div>

      <LinkDocumentSheet open={linkOpen} onOpenChange={setLinkOpen} target={linkTarget} docTypes={docTypes} />
      <ConfirmDocumentSheet open={confirmOpen} onOpenChange={setConfirmOpen} row={confirmRow} />
    </>
  );
}

// ---------- the rows ----------

function docHref(row: DocRow): string | null {
  if (row.state === 'missing' || !row.documentLinkId) return null;
  const href = recordHref(row.recordUrl) ?? hrefTo({ page: 'document-link', id: row.documentLinkId.toLowerCase() });
  // The document's page finds its record at once with the application named.
  return withParams(href, { app: row.businessApplicationId.toLowerCase() });
}

function ActButton({ row, rights, onAct }: { row: DocRow; rights: ActRights; onAct: (row: DocRow, act: RowAct) => void }) {
  const kind = actOf(row);
  if (!kind || !mayAct(row, kind, rights)) return null;
  const what = `${row.documentType.label} of ${row.applicationName}`;
  return (
    <Button size="sm" variant={kind === 'link' ? 'outline' : 'default'} onClick={() => onAct(row, kind)} aria-label={`${ACT_LABELS[kind]}: ${what}`}>
      {ACT_LABELS[kind]}
    </Button>
  );
}

function DocumentRows({ rows, today, statuses, rights, onAct }: { rows: DocRow[]; today: string; statuses: Labels; rights: ActRights; onAct: (row: DocRow, act: RowAct) => void }) {
  const narrow = useNarrow();
  const appHref = (r: DocRow) => hrefTo({ page: 'app', id: r.businessApplicationId.toLowerCase() });
  const title = (r: DocRow) => {
    const href = docHref(r);
    return href ? <TextLink href={href}>{r.documentType.label}</TextLink> : <span>{r.documentType.label}</span>;
  };
  const ownerText = (r: DocRow) => r.owner?.displayLabel ?? (r.state === 'missing' ? 'Owners of the application' : 'No owner named');
  const checked = (r: DocRow) => (r.state === 'missing' ? 'No link yet' : r.lastCheckedOn ? `Checked ${fmtDate(r.lastCheckedOn)}` : 'Never checked');
  if (narrow) {
    return (
      <RowList label="Documents">
        {rows.map(r => {
          const status = rowStatus(r, statuses);
          return (
            <Row
              key={`${r.businessApplicationId}-${r.documentType.key}-${r.documentLinkId ?? 'missing'}`}
              wideEnd
              primary={title(r)}
              secondary={<TextLink href={appHref(r)}>{r.applicationName}</TextLink>}
              tertiary={r.state === 'missing'
                ? `${stateText(r, today)} The owners of the application link it.`
                : `${stateText(r, today)} ${ownerText(r)}. ${checked(r)}.`}
              badges={(
                <>
                  <AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} />
                  {r.url && <span className="text-xs"><ExternalLink href={r.url}>Open the document</ExternalLink></span>}
                </>
              )}
              end={(
                <>
                  {status && <StatusBadge status={status} />}
                  <ActButton row={r} rights={rights} onAct={onAct} />
                </>
              )}
            />
          );
        })}
      </RowList>
    );
  }
  return (
    <Table aria-label="Documents">
      <TableHeader>
        <TableRow>
          <TableHead>Document</TableHead>
          <TableHead>Application</TableHead>
          <TableHead>State</TableHead>
          <TableHead>Owner</TableHead>
          <TableHead><span className="sr-only">Act</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(r => {
          const status = rowStatus(r, statuses);
          return (
            <TableRow key={`${r.businessApplicationId}-${r.documentType.key}-${r.documentLinkId ?? 'missing'}`} className="align-top">
              <TableCell className="whitespace-normal">
                <div className="flex flex-col items-start gap-1 text-sm">
                  {title(r)}
                  {r.requiredFrom && <Muted>Required from {r.requiredFrom.label.toLowerCase()}</Muted>}
                  {r.url && <span className="text-xs"><ExternalLink href={r.url}>Open the document</ExternalLink></span>}
                  <AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} />
                </div>
              </TableCell>
              <TableCell className="text-sm whitespace-normal"><TextLink href={appHref(r)}>{r.applicationName}</TextLink></TableCell>
              <TableCell className="max-w-[260px] whitespace-normal">
                <div className="flex flex-col items-start gap-1">
                  {status && <StatusBadge status={status} />}
                  <Muted>{stateText(r, today)}</Muted>
                </div>
              </TableCell>
              <TableCell className="whitespace-normal">
                <div className="flex flex-col gap-0.5 text-sm text-navy">
                  {ownerText(r)}
                  <Muted>{checked(r)}</Muted>
                </div>
              </TableCell>
              <TableCell className="text-right whitespace-normal"><ActButton row={r} rights={rights} onAct={onAct} /></TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

// ---------- which gate needs each type ----------

function GateNeeds({ docTypes, onPick }: { docTypes: Labels; onPick: (typeKey: string) => void }) {
  const requirements = useConfigRows('gate-requirement');
  const gates = useLabels('gate');
  const tiers = useLabels('tier');
  const kinds = useLabels('application-kind');
  const keys = docTypes.keys();
  return (
    <Panel title="Which gate needs each" state={requirements.state} skeletonRows={4} notes={[]} subtitle="From the configuration in force">
      {() => (keys.length ? (
        <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label="Document types and the gates that need them">
          {keys.map(k => {
            const needs = gateNeedText(gateNeedsOf(requirements.rows, k), { gate: gates.label, tier: tiers.label, kind: kinds.label });
            const days = docTypes.field<number>(k, 'freshnessDays');
            const person = docTypes.field<boolean>(k, 'currentNeedsPerson') === true;
            return (
              <li key={k} className="flex flex-col gap-0.5">
                <LinkButton className="self-start text-sm" onClick={() => onPick(k)} aria-label={`Show only ${docTypes.label(k)} documents`}>{docTypes.label(k)}</LinkButton>
                <span className="text-xs leading-4 text-navy-2">
                  {needs.length ? `Needed current for the ${needs.join('; the ')}.` : 'No gate needs it current.'}
                </span>
                <span className="text-xs leading-4 text-navy-3">
                  {typeof days === 'number' ? `Turns stale ${plural(days, 'day', 'days')} after its last check.` : 'Does not turn stale by date.'}
                  {person ? ' A person confirms it current.' : ''}
                </span>
              </li>
            );
          })}
        </ul>
      ) : <Empty>The document types could not be read.</Empty>)}
    </Panel>
  );
}
