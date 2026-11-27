// Decision log and exceptions (screen "Decision log and exceptions", priority 2): one decision log with the ADR status
// words, and a register of standing exceptions with the standard each departs from, its review date and the flagged
// deployment it covers.
//   /app/decisions?view=log|exceptions&type=<key>&status=<meaning>&app=<id>&portfolio=<id>   RouteOf<'decisions'>
//   and the page's own: from=<date>&to=<date> (decided between), ai=1 (drafted by an AI client), review=overdue|soon
//   (exceptions whose review is past, or within 30 days)
// The decision record page (/app/records/decision/<id>) is DecisionRecord in Decision.tsx: every row opens it.
// Reads: get_decision_log (type, application, portfolio and dates; every page up to 1,000 decisions), get_configuration
// (decision types, status labels), list_portfolios, get_application_card (the name of a filtered application).
// Writes, each in the shared side sheet: save_decision_proposal (propose a decision about an application, or an
// exception about an application or one of its flagged deployments), submit_record (the person's own proposal, or an
// AI client's draft for them, that nobody was asked about yet).
//
// Design choices (D-41, 2026-11-24), beyond those in guide-log-docs/decisionLog.ts:
// - The status and "drafted by an AI client" filters apply to the rows read, so the tiles above the list count every
//   status for the same filters, and a tile opens exactly the rows it counts.
// - Dates and the AI filter are the page's own address parameters: get_decision_log has from and to but the shared
//   route does not, and the log has no AI filter of its own.
// - The exceptions view reads the exception type only; its tiles count standing exceptions, reviews overdue and reviews
//   within 30 days, and proposals waiting. An exception about a flagged deployment links to that deployment.
// - Supersede and amend chains show beside the list, oldest first, for the rows read.
// - Wave 4: the text search q= keeps the decisions whose statement, subject, type, reason, standard, proposer or decider
//   hold the words (get_decision_log takes no words; the page reads up to 1,000). The figures and the side charts follow
//   it, so each number opens the rows it counts.
import { ArrowRight } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import { hrefTo, recordHref, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { DECISION_STATUS_MEANINGS, type DecisionStatusRouteMeaning, type RouteOf } from '../app/routes';
import { BarList, Empty, KpiTile, MoreRowsNote, Muted, PageHeader, Panel, StatusBadge, SwitchTabs, TextLink, ToneBadge, useNarrow, DataStamp } from '../components/blocks';
import { Row, RowList } from '../components/rows';
import { AiMark } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { DecisionLog as DecisionLogData, DecisionLogItem, GetDecisionLogInput } from '../data/contracts';
import { fmtDate } from '../data/format';
import { useLabels } from '../data/labels';
import { usePagedRead, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf } from '../data/toolClient';
import { useHost } from '../harness';
import {
  approvalsText,
  countByStatus,
  countByType,
  decisionChains,
  EXCEPTION_TYPE,
  filterDecisions,
  isAiMade,
  isStanding,
  notAskedYet,
  proposedBy,
  REVIEW_WINDOWS,
  reviewState,
  reviewText,
  sameId,
  sentence,
  STATUS_WORDS,
  SUBJECT_WORDS,
  subjectRoute,
  type ChainLink,
  type ReviewWindow,
} from './guide-log-docs/decisionLog';
import { ProposeDecisionSheet, ProposeExceptionSheet } from './guide-log-docs/DecisionSheets';
import { AppFilterBox, CheckFilter, DateFilter, envelopeOf, filterSectionClass, pagedToolState, ShowMoreRows, useApplicationName, useShown } from './guide-log-docs/parts';
import { cn } from '../lib/utils';
import { Chip, kpiRowClass4 } from './owners-reviews/people';
import { SubmitSheet } from './work-approvals/parts';

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const REVIEW = new RegExp(`^(${REVIEW_WINDOWS.join('|')})$`);
const MAX_PAGES = 10;

type Next = Partial<Omit<RouteOf<'decisions'>, 'page'>>;
type Params = { from?: string; to?: string; ai?: string; review?: string; q?: string };

const decisionHref = (d: DecisionLogItem) => recordHref(d.recordUrl) ?? hrefTo({ page: 'decision', id: d.decisionId.toLowerCase() });

export function DecisionLog({ route }: { route: RouteOf<'decisions'> }) {
  const host = useHost();
  const today = host.today;
  const session = useSession();
  const exceptions = route.view === 'exceptions';
  const from = useQueryParam('from', DATE);
  const to = useQueryParam('to', DATE);
  const aiOnly = useQueryParam('ai', /^1$/) === '1';
  const reviewParam = useQueryParam('review', REVIEW) as ReviewWindow | undefined;
  const review = exceptions ? reviewParam : undefined;
  const types = useLabels('decision-type');
  const statuses = useLabels('status');
  const portfolios = useRead('list_portfolios', { top: 100 });

  const input: GetDecisionLogInput = { top: 100 };
  if (exceptions) input.decisionTypeKeys = [EXCEPTION_TYPE];
  else if (route.typeKeys?.length) input.decisionTypeKeys = route.typeKeys;
  if (route.applicationId) input.businessApplicationId = route.applicationId;
  if (route.portfolioId) input.portfolioId = route.portfolioId;
  if (from) input.from = from;
  if (to) input.to = to;
  const paged = usePagedRead('get_decision_log', input, (d: DecisionLogData) => d.items, { maxPages: MAX_PAGES });
  const state = pagedToolState(paged);
  const text = useListText();
  const all = useMemo(() => filterByText(paged.status === 'done' ? paged.items : [], text, d => [
    d.statement,
    d.subjectLabel,
    d.decisionType.label,
    d.reason,
    d.exception?.standard,
    d.proposedBy?.displayLabel,
    d.decider?.displayLabel,
  ]) as DecisionLogItem[], [paged, text]);
  const rows = useMemo(() => filterDecisions(all, { statusMeanings: route.statusMeanings, aiOnly, review, today }), [all, route.statusMeanings, aiOnly, review, today]);
  const counts = countByStatus(all);
  const loaded = paged.status === 'done';

  const params: Params = { from, to, ai: aiOnly ? '1' : undefined, review };
  const go = (next: Next, nextParams: Params = params, replace = true) => {
    const target: RouteOf<'decisions'> = {
      page: 'decisions',
      view: route.view,
      typeKeys: route.typeKeys,
      statusMeanings: route.statusMeanings,
      applicationId: route.applicationId,
      portfolioId: route.portfolioId,
      ...next,
    };
    // The search stays unless a call clears it (q: undefined).
    navigate(withParams(hrefTo(target), { q: text, ...nextParams }), replace);
  };

  const statusLabel = (m: DecisionStatusRouteMeaning) => (statuses.has(`decision-${m}`) ? statuses.label(`decision-${m}`) : STATUS_WORDS[m]);
  const chosenStatus = route.statusMeanings ?? [];
  const toggleStatus = (m: DecisionStatusRouteMeaning) => {
    const next = chosenStatus.includes(m) ? chosenStatus.filter(s => s !== m) : [...chosenStatus, m];
    go({ statusMeanings: next.length ? next : undefined });
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
  const knownAppName = route.applicationId
    ? all.find(d => d.subjectKind === 'business-application' && sameId(d.subjectId, route.applicationId))?.subjectLabel ?? null
    : null;
  const appName = useApplicationName(route.applicationId, knownAppName);

  const proposeTypes = types.keys()
    .filter(k => k !== EXCEPTION_TYPE && types.field<string>(k, 'defaultSubjectKind') === 'business-application')
    .map(k => ({ value: k, label: types.label(k) }));
  const initialApp = route.applicationId && appName && appName !== 'This application' ? { id: route.applicationId, name: appName } : null;
  const canPropose = session.can('save_decision_proposal');

  const anyFilter = chosenTypes.length > 0 || chosenStatus.length > 0 || !!route.applicationId || !!route.portfolioId || !!from || !!to || aiOnly || !!review || !!text;
  const clearAll = () => go({ typeKeys: undefined, statusMeanings: undefined, applicationId: undefined, portfolioId: undefined }, { q: undefined });

  const aiCount = all.filter(isAiMade).length;
  const standing = all.filter(isStanding).length;
  const overdue = all.filter(d => reviewState(d, today) === 'overdue').length;
  const soon = all.filter(d => reviewState(d, today) === 'soon').length;
  const value = (n: number) => (loaded ? n : null);
  const canSubmit = session.can('submit_record');
  // A long list shows 25 rows at a time, from the top again when a filter changes.
  const list = useShown([route.view, chosenTypes.join(','), chosenStatus.join(','), route.applicationId, route.portfolioId, from, to, aiOnly, review, text].join('|'));

  return (
    <>
      <PageHeader
        eyebrow="Record"
        title={exceptions ? 'Exceptions' : 'Decision log'}
        subtitle={exceptions
          ? 'Every exception: the standard it sets aside, why, the flagged deployment it covers, and when it is reviewed again.'
          : 'Every decision, with what was decided, by whom, when and why. Open one to see its approvals and evidence.'}
        meta={<DataStamp envelope={envelopeOf(state)} />}
        actions={canPropose ? (
          <>
            {!exceptions && <ProposeDecisionSheet typeChoices={proposeTypes} initialType={chosenTypes.length === 1 ? chosenTypes[0] : undefined} initialApp={initialApp} />}
            <ProposeExceptionSheet initialApp={initialApp} />
          </>
        ) : undefined}
      />

      {paged.status !== 'failed' && (exceptions ? (
        <div className={kpiRowClass4} role="group" aria-label="Key figures">
          <KpiTile label="Standing" value={value(standing)} caption="Accepted, not superseded" onClick={() => go({ statusMeanings: ['accepted'] }, { from, to })} />
          <KpiTile label="Review overdue" value={value(overdue)} caption="Standing, past the review date" attention={overdue > 0} onClick={() => go({ statusMeanings: undefined }, { from, to, review: 'overdue' })} />
          <KpiTile label="Review in 30 days" value={value(soon)} caption="Standing, review date coming" onClick={() => go({ statusMeanings: undefined }, { from, to, review: 'soon' })} />
          <KpiTile label="Waiting for answers" value={value(counts.proposed)} caption="Proposed exceptions" onClick={() => go({ statusMeanings: ['proposed'] }, { from, to })} />
        </div>
      ) : (
        <div className={kpiRowClass4} role="group" aria-label="Key figures">
          <KpiTile label="Waiting for answers" value={value(counts.proposed)} caption="Proposed, not decided yet" attention={counts.proposed > 0} onClick={() => go({ statusMeanings: ['proposed'] }, { from, to })} />
          <KpiTile label="Accepted" value={value(counts.accepted)} caption="In force or done" onClick={() => go({ statusMeanings: ['accepted'] }, { from, to })} />
          <KpiTile label="Rejected or withdrawn" value={value(counts.rejected + counts.withdrawn)} caption="Not taken" onClick={() => go({ statusMeanings: ['rejected', 'withdrawn'] }, { from, to })} />
          <KpiTile label="Drafted by AI" value={value(aiCount)} caption="An AI client drafted it for a person" onClick={() => go({ statusMeanings: undefined }, { from, to, ai: '1' })} />
        </div>
      ))}

      <section aria-label="Filters" className={filterSectionClass}>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <SwitchTabs
            label="Show"
            ariaLabel="The decision log or the exceptions register"
            value={exceptions ? 'exceptions' : 'log'}
            onChange={v => go(
              { view: v === 'exceptions' ? 'exceptions' : undefined, typeKeys: undefined, statusMeanings: undefined },
              { from, to, ai: aiOnly ? '1' : undefined },
              false,
            )}
            options={[{ value: 'log', label: 'Decision log' }, { value: 'exceptions', label: 'Exceptions' }]}
          />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {!exceptions && (
            <CheckFilter
              label="Type"
              options={types.keys().map(k => ({ value: k, label: types.label(k) }))}
              chosen={chosenTypes}
              onToggle={toggleType}
            />
          )}
          <CheckFilter
            label="Status"
            options={DECISION_STATUS_MEANINGS.map(m => ({ value: m, label: statusLabel(m) }))}
            chosen={chosenStatus}
            onToggle={v => toggleStatus(v as DecisionStatusRouteMeaning)}
          />
          <Select value={route.portfolioId ?? 'all'} onValueChange={v => go({ portfolioId: v === 'all' ? undefined : v })}>
            <SelectTrigger className="w-[220px] narrow:flex-1" aria-label="Portfolio"><SelectValue /></SelectTrigger>
            <SelectContent className="max-h-[320px]">
              <SelectItem value="all">All portfolios</SelectItem>
              {portfolioItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{portfolioName(p.portfolioId)}</SelectItem>)}
            </SelectContent>
          </Select>
          {!route.applicationId && <AppFilterBox id="decisions-app" onPick={id => go({ applicationId: id })} />}
          <ListSearch label="Search the decisions by statement, subject, type, reason, standard or person" placeholder="Decision, subject or person" />
        </div>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <DateFilter id="decisions-from" label="Decided from" value={from} onChange={v => go({}, { ...params, from: v })} />
          <DateFilter id="decisions-to" label="to" value={to} onChange={v => go({}, { ...params, to: v })} />
          <div className="flex items-center gap-2">
            <Checkbox id="decisions-ai" checked={aiOnly} onCheckedChange={v => go({}, { ...params, ai: v === true ? '1' : undefined })} />
            <Label htmlFor="decisions-ai" className="font-normal">Only those drafted by an AI client</Label>
          </div>
        </div>
        {anyFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {chosenTypes.map(k => <Chip key={k} label={`Type: ${types.label(k)}`} onRemove={() => toggleType(k)} />)}
            {chosenStatus.map(m => <Chip key={m} label={`Status: ${statusLabel(m)}`} onRemove={() => toggleStatus(m)} />)}
            {route.applicationId && <Chip label={`Application: ${appName ?? 'reading the name'}`} onRemove={() => go({ applicationId: undefined })} />}
            {route.portfolioId && <Chip label={`Portfolio: ${portfolioName(route.portfolioId)}`} onRemove={() => go({ portfolioId: undefined })} />}
            {from && <Chip label={`Decided from ${fmtDate(from)}`} onRemove={() => go({}, { ...params, from: undefined })} />}
            {to && <Chip label={`Decided to ${fmtDate(to)}`} onRemove={() => go({}, { ...params, to: undefined })} />}
            {aiOnly && <Chip label="Drafted by an AI client" onRemove={() => go({}, { ...params, ai: undefined })} />}
            {review && <Chip label={review === 'overdue' ? 'Review overdue' : 'Review in 30 days'} onRemove={() => go({}, { ...params, review: undefined })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => go({}, { ...params, q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearAll}>Clear all</Button>
          </div>
        )}
      </section>

      <Panel
        title={exceptions ? 'Exceptions register' : 'Decisions'}
        count={loaded ? rows.length : undefined}
        state={state}
        skeletonRows={8}
        className="mb-4 narrow:mb-3"
      >
        {() => (
          <>
            {rows.length === 0
              ? <EmptyRows anyFilter={anyFilter} clearAll={clearAll} what={exceptions ? (review ? 'No standing exception has its review in this window' : 'No exception') : 'No decision'} />
              : exceptions
                ? <ExceptionTable rows={rows.slice(0, list.shown)} today={today} appHint={route.applicationId} me={session.objectId} canSubmit={canSubmit} />
                : <DecisionTable rows={rows.slice(0, list.shown)} appHint={route.applicationId} me={session.objectId} canSubmit={canSubmit} />}
            <ShowMoreRows shown={list.shown} total={rows.length} what={exceptions ? 'exceptions' : 'decisions'} onMore={list.more} />
            <ReadMore paged={paged} />
          </>
        )}
      </Panel>
      <div className={cn('grid items-start gap-4 medium:grid-cols-1 narrow:gap-3', exceptions ? 'grid-cols-2' : 'grid-cols-3')}>
        {!exceptions && (
          <Panel title="By type" state={state} skeletonRows={5} notes={[]} subtitle="The decisions in the list">
            {() => {
              const buckets = countByType(rows);
              if (!buckets.length) return <Empty>No decision to count.</Empty>;
              return <BarList ariaLabel="Decisions by type" noun={['decision', 'decisions']} labelWidth={150} items={buckets} onDrill={b => b.key && go({ typeKeys: [b.key] })} />;
            }}
          </Panel>
        )}
        <StatusBars state={state} rows={rows} label={statusLabel} onDrill={m => go({ statusMeanings: [m] })} />
        <ChainsPanel state={state} all={all} />
      </div>
    </>
  );
}

function EmptyRows({ anyFilter, clearAll, what }: { anyFilter: boolean; clearAll: () => void; what: string }) {
  return (
    <div className="flex flex-col items-start gap-2 py-1">
      <Empty>{what}{anyFilter ? ' matches these filters.' : ' is recorded yet.'}</Empty>
      {anyFilter && <Button variant="outline" size="sm" onClick={clearAll}>Clear the filters</Button>}
    </div>
  );
}

function ReadMore({ paged }: { paged: { status: string; skipToken?: string; loadingMore?: boolean; more: () => void } }) {
  if (paged.status !== 'done' || !paged.skipToken) return null;
  return (
    <div className="flex flex-col items-start gap-2">
      <MoreRowsNote skipToken={paged.skipToken} what="decisions" />
      <Button variant="outline" size="sm" onClick={paged.more} disabled={paged.loadingMore}>{paged.loadingMore ? 'Reading more' : 'Read 100 more'}</Button>
    </div>
  );
}

function StatusBars({ state, rows, label, onDrill }: { state: Parameters<typeof Panel>[0]['state']; rows: DecisionLogItem[]; label: (m: DecisionStatusRouteMeaning) => string; onDrill: (m: DecisionStatusRouteMeaning) => void }) {
  return (
    <Panel title="By status" state={state} skeletonRows={4} notes={[]} subtitle="The decisions in the list">
      {() => {
        const counts = countByStatus(rows);
        const items = DECISION_STATUS_MEANINGS.filter(m => counts[m] > 0).map(m => ({ key: m, label: label(m), count: counts[m], muted: m === 'superseded' || m === 'withdrawn' }));
        if (!items.length) return <Empty>No decision to count.</Empty>;
        return <BarList ariaLabel="Decisions by status" noun={['decision', 'decisions']} labelWidth={150} items={items} onDrill={b => b.key && onDrill(b.key as DecisionStatusRouteMeaning)} />;
      }}
    </Panel>
  );
}

// ---------- the subject of a decision ----------

function SubjectLink({ d, appHint }: { d: DecisionLogItem; appHint: string | undefined }) {
  const route = subjectRoute(d);
  if (!route) return <span>{d.subjectLabel}</span>;
  const href = hrefTo(route);
  // A deployment page finds its record faster with the application named.
  const withHint = d.subjectKind === 'deployment-record' && appHint ? withParams(href, { app: appHint }) : href;
  return <TextLink href={withHint}>{d.subjectLabel}</TextLink>;
}

// The person's own proposal, or an AI client's draft for them, that nobody was asked about yet. The sheet stays mounted
// once opened, so its answer stays in view after the row moves on.
function SubmitOwn({ d, me, canSubmit }: { d: DecisionLogItem; me: string | null; canSubmit: boolean }) {
  const [kept, setKept] = useState(false);
  const show = canSubmit && notAskedYet(d) && proposedBy(d, me);
  if (!show && !kept) return null;
  const aiDraft = isAiMade(d);
  return (
    <span onClickCapture={() => setKept(true)} className="inline-flex">
      <SubmitSheet subjectKind="decision" subjectId={d.decisionId} title={d.statement} isAiDraft={aiDraft} performedBy={d.performedBy} />
    </span>
  );
}

function ChainNotes({ d }: { d: DecisionLogItem }) {
  const link = (id: string, text: string) => <TextLink href={hrefTo({ page: 'decision', id: id.toLowerCase() })}>{text}</TextLink>;
  const notes: ReactNode[] = [];
  if (d.supersedes) notes.push(<span key="s">Supersedes {link(d.supersedes, 'an earlier decision')}.</span>);
  if (d.amends) notes.push(<span key="a">Amends {link(d.amends, 'an earlier decision')}.</span>);
  if (d.supersededBy) notes.push(<span key="b">Superseded by {link(d.supersededBy, 'a later decision')}.</span>);
  if (!notes.length) return null;
  return <span className="flex flex-col gap-0.5 text-xs text-navy-3">{notes}</span>;
}

function decidedText(d: DecisionLogItem): string {
  if (d.decidedOn) return `${fmtDate(d.decidedOn)}${d.decider ? `, ${d.decider.displayLabel}` : ''}`;
  return d.status.meaning === 'proposed' ? 'Not decided yet' : 'No decision date';
}

// ---------- the log ----------

function DecisionTable({ rows, appHint, me, canSubmit }: { rows: DecisionLogItem[]; appHint: string | undefined; me: string | null; canSubmit: boolean }) {
  const narrow = useNarrow();
  if (narrow) {
    return (
      <RowList label="Decisions">
        {rows.map(d => (
          <Row
            key={d.decisionId}
            wideEnd
            primary={<TextLink href={decisionHref(d)}>{d.statement}</TextLink>}
            secondary={<>{d.decisionType.label} about <SubjectLink d={d} appHint={appHint} /></>}
            tertiary={[d.outcome ? `Outcome: ${d.outcome.label}` : null, decidedText(d), approvalsText(d.approvalsSummary)].filter(Boolean).join('. ')}
            badges={<><AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} /><SubmitOwn d={d} me={me} canSubmit={canSubmit} /></>}
            end={<StatusBadge status={d.status} />}
          />
        ))}
      </RowList>
    );
  }
  return (
    <Table aria-label="Decisions">
      <TableHeader>
        <TableRow>
          <TableHead>Decision</TableHead>
          <TableHead>About</TableHead>
          <TableHead>Outcome</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Decided</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(d => (
          <TableRow key={d.decisionId} className="align-top">
            <TableCell className="max-w-[340px] whitespace-normal">
              <div className="flex flex-col items-start gap-1">
                <TextLink href={decisionHref(d)} className="text-sm [overflow-wrap:anywhere]">{d.statement}</TextLink>
                <Muted>{d.decisionType.label}{d.proposedBy ? `. Proposed by ${d.proposedBy.displayLabel}` : ''}</Muted>
                <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
                <SubmitOwn d={d} me={me} canSubmit={canSubmit} />
              </div>
            </TableCell>
            <TableCell className="whitespace-normal">
              <div className="flex flex-col gap-0.5 text-sm">
                <SubjectLink d={d} appHint={appHint} />
                <Muted>{SUBJECT_WORDS[d.subjectKind]}</Muted>
              </div>
            </TableCell>
            <TableCell className="text-sm whitespace-normal text-navy">{d.outcome?.label ?? <Muted>Not decided</Muted>}</TableCell>
            <TableCell className="whitespace-normal">
              <div className="flex flex-col items-start gap-1">
                <StatusBadge status={d.status} />
                {approvalsText(d.approvalsSummary) && <Muted>{approvalsText(d.approvalsSummary)}</Muted>}
                <ChainNotes d={d} />
              </div>
            </TableCell>
            <TableCell className="text-sm whitespace-normal text-navy">{decidedText(d)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// ---------- the exceptions register ----------

function ReviewBadge({ d, today }: { d: DecisionLogItem; today: string }) {
  const state = reviewState(d, today);
  const text = reviewText(d, today);
  if (!text) return null;
  if (state === 'overdue') return <ToneBadge tone="danger">{text}</ToneBadge>;
  if (state === 'soon') return <ToneBadge tone="warning">{text}</ToneBadge>;
  return <Muted>{text}</Muted>;
}

function ExceptionTable({ rows, today, appHint, me, canSubmit }: { rows: DecisionLogItem[]; today: string; appHint: string | undefined; me: string | null; canSubmit: boolean }) {
  const narrow = useNarrow();
  if (narrow) {
    return (
      <RowList label="Exceptions">
        {rows.map(d => (
          <Row
            key={d.decisionId}
            wideEnd
            primary={<TextLink href={decisionHref(d)}>{d.statement}</TextLink>}
            secondary={<>{SUBJECT_WORDS[d.subjectKind]}: <SubjectLink d={d} appHint={appHint} /></>}
            tertiary={[
              d.exception?.standard ? `Sets aside: ${sentence(d.exception.standard)}` : 'No standard named.',
              d.exception?.reviewOn ? `Review on ${fmtDate(d.exception.reviewOn)}.` : 'No review date.',
            ].join(' ')}
            badges={<><AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} /><ReviewBadge d={d} today={today} /><SubmitOwn d={d} me={me} canSubmit={canSubmit} /></>}
            end={<StatusBadge status={d.status} />}
          />
        ))}
      </RowList>
    );
  }
  return (
    <Table aria-label="Exceptions">
      <TableHeader>
        <TableRow>
          <TableHead>Exception</TableHead>
          <TableHead>Covers</TableHead>
          <TableHead>Standard it sets aside</TableHead>
          <TableHead>Review on</TableHead>
          <TableHead>Status</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(d => (
          <TableRow key={d.decisionId} className="align-top">
            <TableCell className="max-w-[320px] whitespace-normal">
              <div className="flex flex-col items-start gap-1">
                <TextLink href={decisionHref(d)} className="text-sm [overflow-wrap:anywhere]">{d.statement}</TextLink>
                {d.reason && <span className="text-xs [overflow-wrap:anywhere] text-navy-3">Why: {d.reason}</span>}
                <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
                <SubmitOwn d={d} me={me} canSubmit={canSubmit} />
              </div>
            </TableCell>
            <TableCell className="whitespace-normal">
              <div className="flex flex-col gap-0.5 text-sm">
                <SubjectLink d={d} appHint={appHint} />
                <Muted>{SUBJECT_WORDS[d.subjectKind]}</Muted>
              </div>
            </TableCell>
            <TableCell className="max-w-[280px] text-sm whitespace-normal text-navy [overflow-wrap:anywhere]">{d.exception?.standard ?? <Muted>Not named</Muted>}</TableCell>
            <TableCell className="whitespace-normal">
              <div className="flex flex-col items-start gap-1 text-sm text-navy">
                {d.exception?.reviewOn ? fmtDate(d.exception.reviewOn) : <Muted>No date</Muted>}
                <ReviewBadge d={d} today={today} />
              </div>
            </TableCell>
            <TableCell className="whitespace-normal">
              <div className="flex flex-col items-start gap-1">
                <StatusBadge status={d.status} />
                <Muted>{decidedText(d)}</Muted>
                {approvalsText(d.approvalsSummary) && <Muted>{approvalsText(d.approvalsSummary)}</Muted>}
                <ChainNotes d={d} />
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// ---------- supersede chains ----------

function ChainsPanel({ state, all }: { state: Parameters<typeof Panel>[0]['state']; all: DecisionLogItem[] }) {
  const chains = useMemo(() => decisionChains(all), [all]);
  return (
    <Panel title="Supersede chains" count={state?.status === 'done' && state.envelope.ok ? chains.length : undefined} state={state} skeletonRows={3} notes={[]} subtitle="Decisions that replace or amend another, oldest first">
      {() => (chains.length ? (
        <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label="Supersede chains">
          {chains.map(chain => <ChainView key={chain[0].id} chain={chain} />)}
        </ul>
      ) : <Empty>No decision in this list supersedes or amends another.</Empty>)}
    </Panel>
  );
}

function ChainView({ chain }: { chain: ChainLink[] }) {
  return (
    <li className="rounded-md border border-line px-3 py-2">
      <ol className="m-0 flex list-none flex-col gap-1.5 p-0">
        {chain.map((c, i) => (
          <li key={`${c.id}-${i}`} className="flex flex-col gap-0.5">
            {i > 0 && (
              <span className="flex items-center gap-1 text-xs font-semibold text-navy-3">
                <ArrowRight className="size-3.5 rotate-90" aria-hidden />
                {c.how === 'amends' ? 'Amended by' : 'Superseded by'}
              </span>
            )}
            {c.item ? (
              <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <TextLink href={decisionHref(c.item)} className="text-sm [overflow-wrap:anywhere]">{c.item.statement}</TextLink>
                <StatusBadge status={c.item.status} />
                <Muted>{c.item.decisionType.label}{c.item.decidedOn ? `, ${fmtDate(c.item.decidedOn)}` : ''}</Muted>
              </span>
            ) : (
              <TextLink href={hrefTo({ page: 'decision', id: c.id })} className="text-sm">A decision outside this list</TextLink>
            )}
          </li>
        ))}
      </ol>
    </li>
  );
}
