// Reviews: the list (/app/reviews?state=overdue|due|done|cancelled|all&type=<key>&app=<id>&portfolio=<id>, and the
// page's own within=30|90 and mine=1) and one review (/app/records/review/<id>).
// Reads: list_reviews (the list and its counts; the other reviews of an application), get_review (one review with its
// evidence, outcome decision, cadence, the gates it feeds and whether the person may complete it), get_decision_log
// (the outcome decisions), list_portfolios, whoami.
// Writes, each in the shared side sheet: save_decision_proposal (the outcome decision about the review), submit_record
// (that decision), save_link (evidence on the review), complete_review (the person marks it done once the outcome is
// accepted; document 23, B23-10).
// Wave 4 (D-41): the text search q= keeps the reviews whose number, type, application, portfolio, reviewer or lead
// owner hold the words (list_reviews takes no words; the page reads every page of the list). The two bar charts follow
// the search, so each bar opens its rows; the four figures stay the tool's counts for the filters, as before.
import { ChevronDown, CircleCheck, Clock, Info, ListFilter } from 'lucide-react';
import { useMemo, useState, type ReactElement, type ReactNode } from 'react';
import { hrefTo } from '../app/links';
import { navigate } from '../app/navigation';
import { REVIEW_STATES, type ReviewState, type RouteOf } from '../app/routes';
import { GuidelineChip } from '../components/actions';
import { BarList, DataStamp, Empty, KpiTile, MoreRowsNote, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '../components/ui/dropdown-menu';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Skeleton } from '../components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { DecisionLogItem, ReviewItem } from '../data/contracts';
import { fmtDate, plural } from '../data/format';
import { useLabels } from '../data/labels';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { useAllPages, useQueryParam, useWhoAmI, withParams } from './owners-reviews/hooks';
import { approvalsText, countBy, isSubmitted, mayCall, overdueDays, previousDone, sameId, sortReviews, triggerText, withArticle, type ReviewSort } from './owners-reviews/model';
import { ProposalBanner, SubmitDecisionSheet, useStickyAction, type Proposal } from './owners-reviews/OwnerSheets';
import { Chip, Fact, factsClass, kpiRowClass4, PersonLink, twoColumnClass } from './owners-reviews/people';
import { AddEvidenceSheet, CompleteReviewSheet, ProposeOutcomeSheet } from './owners-reviews/ReviewSheets';

const STATE_LABELS: Record<ReviewState, string> = {
  overdue: 'Overdue',
  due: 'Due',
  done: 'Done',
  cancelled: 'Canceled',
  all: 'All',
};

const SORTS: { key: ReviewSort; label: string }[] = [
  { key: 'due', label: 'Due date' },
  { key: 'application', label: 'Application' },
  { key: 'type', label: 'Review type' },
];

const WITHIN = /^(30|90)$/;

// "Overdue by 5 days" for a due review past its date; null otherwise.
function OverdueBadge({ review, today }: { review: ReviewItem; today: string }) {
  if (!review.isOverdue) return null;
  const days = overdueDays(review.dueOn, today);
  return <ToneBadge tone="danger">{days ? `Overdue by ${plural(days, 'day', 'days')}` : 'Overdue'}</ToneBadge>;
}

function dueText(r: ReviewItem): string {
  if (r.status.meaning === 'done') return r.doneOn ? `Done ${fmtDate(r.doneOn)}` : 'Done';
  return r.dueOn ? `Due ${fmtDate(r.dueOn)}` : 'No due date';
}

// =====================================================================================================================
// The list: /app/reviews
// =====================================================================================================================

export function Reviews({ route }: { route: RouteOf<'reviews'> }) {
  const host = useHost();
  const today = host.today;
  const state: ReviewState = route.state ?? 'due';
  const within = useQueryParam('within', WITHIN);
  const mineOnly = useQueryParam('mine', /^1$/) === '1';
  const reviewTypes = useLabels('review-type');
  const [sort, setSort] = useState<ReviewSort>('due');

  const filters: Record<string, unknown> = {};
  if (route.typeKeys?.length) filters.reviewTypeKeys = route.typeKeys;
  if (route.applicationId) filters.businessApplicationId = route.applicationId;
  if (route.portfolioId) filters.portfolioId = route.portfolioId;
  if (mineOnly) filters.mineOnly = true;
  const shownWithin = state === 'due' ? within : undefined;
  const list = useAllPages('list_reviews', { state, top: 100, ...filters, ...(shownWithin ? { dueWithinDays: Number(shownWithin) } : {}) });
  const soon = useTool('list_reviews', { ...filters, state: 'due', dueWithinDays: 30, top: 1 });
  const portfolios = useTool('list_portfolios', { top: 100 });
  const data = dataOf(list);
  const text = useListText();
  const items = useMemo(
    () => sortReviews(filterByText(data?.items ?? [], text, r => [r.recordNumber, r.reviewType.label, r.applicationName, r.portfolio?.name, r.reviewer?.displayLabel, r.leadOwner?.displayLabel]) as ReviewItem[], sort),
    [data, sort, text],
  );

  type Next = { state?: ReviewState; typeKeys?: string[]; applicationId?: string; portfolioId?: string };
  const go = (next: Next, params: { within?: string; mine?: string; q?: string } = { within, mine: mineOnly ? '1' : undefined }) => {
    const target = { page: 'reviews' as const, state: route.state, typeKeys: route.typeKeys, applicationId: route.applicationId, portfolioId: route.portfolioId, ...next };
    // The search stays unless a call clears it (q: undefined).
    navigate(withParams(hrefTo(target), { q: text, ...params }), true);
  };
  const keep = { within, mine: mineOnly ? '1' : undefined };

  const portfolioItems = dataOf(portfolios)?.items ?? [];
  const portfolioName = (id: string) => {
    const p = portfolioItems.find(x => sameId(x.portfolioId, id));
    return p ? p.path.map(x => x.name).join(' / ') || p.name : 'Portfolio';
  };
  const appName = route.applicationId ? items.find(r => sameId(r.businessApplicationId, route.applicationId))?.applicationName ?? 'This application' : null;
  const chosenTypes = route.typeKeys ?? [];
  const toggleType = (key: string) => {
    const next = chosenTypes.includes(key) ? chosenTypes.filter(k => k !== key) : [...chosenTypes, key];
    go({ typeKeys: next.length ? next : undefined }, keep);
  };
  const hasArchitecture = reviewTypes.keys().includes('architecture');
  const anyFilter = chosenTypes.length > 0 || !!route.applicationId || !!route.portfolioId || mineOnly || !!shownWithin || !!text;

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Reviews"
        subtitle="Owner checks, tier checks, access, benefit, architecture, post-change and security reviews across the portfolio: what is overdue, what is due and what is done."
        meta={<DataStamp envelope={list.status === 'done' ? list.envelope : undefined} />}
        actions={hasArchitecture ? (
          <Button variant="outline" onClick={() => go({ state: 'due', typeKeys: ['architecture'], applicationId: undefined, portfolioId: undefined }, {})}>
            Architecture queue
          </Button>
        ) : undefined}
      />

      <div className={kpiRowClass4} role="group" aria-label="Key figures">
        <KpiTile label="Overdue" value={data?.counts.overdue ?? null} caption="Due, past the due date" attention={!!data?.counts.overdue} onClick={() => go({ state: 'overdue' }, { mine: keep.mine })} />
        <KpiTile label="Due in 30 days" value={dataOf(soon)?.totalCount ?? null} caption="Overdue ones included" onClick={() => go({ state: 'due' }, { within: '30', mine: keep.mine })} />
        <KpiTile label="Due" value={data?.counts.due ?? null} caption="Every open review" onClick={() => go({ state: 'due' }, { mine: keep.mine })} />
        <KpiTile label="Done" value={data?.counts.done ?? null} caption="With their outcome recorded" onClick={() => go({ state: 'done' }, { mine: keep.mine })} />
      </div>

      <div className="mb-4 flex items-start gap-x-2 rounded-lg border border-info-line bg-info-soft px-3 py-2.5 text-sm leading-5 text-navy-2" role="note">
        <Info className="mt-0.5 size-4 shrink-0 text-info" aria-hidden />
        <span>
          On a review's page its people propose the outcome decision, submit it and add evidence. Once the outcome is
          accepted, the person whose review it is completes it there, and the next one is due by its cadence.
        </span>
      </div>

      <section aria-label="Filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <SwitchTabs
            label="Show"
            ariaLabel="Review state"
            value={state}
            onChange={v => go({ state: v === 'due' ? undefined : v }, { within: v === 'due' ? within : undefined, mine: keep.mine })}
            options={REVIEW_STATES.map(s => ({ value: s, label: STATE_LABELS[s] }))}
          />
          {state === 'due' && (
            <div className="flex items-center gap-2">
              <span className="text-xs text-navy-3" id="reviews-within-label">Due within</span>
              <Select value={within ?? 'any'} onValueChange={v => go({}, { within: v === 'any' ? undefined : v, mine: keep.mine })}>
                <SelectTrigger className="w-[140px]" aria-labelledby="reviews-within-label"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="any">Any time</SelectItem>
                  <SelectItem value="30">30 days</SelectItem>
                  <SelectItem value="90">90 days</SelectItem>
                </SelectContent>
              </Select>
            </div>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" className={cn('gap-1.5', chosenTypes.length && 'border-primary text-primary')}>
                <ListFilter aria-hidden />
                Review type
                {chosenTypes.length > 0 && <span className="rounded-full bg-primary px-1.5 text-[11px] leading-4 text-primary-foreground">{chosenTypes.length}</span>}
                <ChevronDown className="size-3.5 opacity-70" aria-hidden />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="min-w-[240px]">
              <DropdownMenuLabel>Review type</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {reviewTypes.keys().length === 0 && <div className="px-2 py-1.5 text-sm text-navy-3">Loading the choices.</div>}
              {reviewTypes.keys().map(k => (
                <DropdownMenuCheckboxItem key={k} checked={chosenTypes.includes(k)} onCheckedChange={() => toggleType(k)} onSelect={e => e.preventDefault()}>
                  {reviewTypes.label(k)}
                </DropdownMenuCheckboxItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
          <Select value={route.portfolioId ?? 'all'} onValueChange={v => go({ portfolioId: v === 'all' ? undefined : v }, keep)}>
            <SelectTrigger className="w-[220px] narrow:flex-1" aria-label="Portfolio"><SelectValue /></SelectTrigger>
            <SelectContent className="max-h-[320px]">
              <SelectItem value="all">All portfolios</SelectItem>
              {portfolioItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{portfolioName(p.portfolioId)}</SelectItem>)}
            </SelectContent>
          </Select>
          <ListSearch label="Search the reviews by number, type, application, portfolio or person" placeholder="Review, application or person" />
          <div className="flex items-center gap-2">
            <Checkbox id="reviews-mine" checked={mineOnly} onCheckedChange={v => go({}, { within, mine: v === true ? '1' : undefined })} />
            <Label htmlFor="reviews-mine" className="font-normal">Only reviews that are mine to do</Label>
          </div>
          <div className="ml-auto flex items-center gap-2 narrow:ml-0">
            <span className="text-xs text-navy-3" id="reviews-sort-label">Sort</span>
            <Select value={sort} onValueChange={v => setSort(v as ReviewSort)}>
              <SelectTrigger className="w-[160px]" aria-labelledby="reviews-sort-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                {SORTS.map(s => <SelectItem key={s.key} value={s.key}>{s.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
        </div>
        {anyFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {chosenTypes.map(k => <Chip key={k} label={`Type: ${reviewTypes.label(k)}`} onRemove={() => toggleType(k)} />)}
            {route.applicationId && <Chip label={`Application: ${appName}`} onRemove={() => go({ applicationId: undefined }, keep)} />}
            {route.portfolioId && <Chip label={`Portfolio: ${portfolioName(route.portfolioId)}`} onRemove={() => go({ portfolioId: undefined }, keep)} />}
            {shownWithin && <Chip label={`Due within ${shownWithin} days`} onRemove={() => go({}, { mine: keep.mine })} />}
            {mineOnly && <Chip label="Mine to do" onRemove={() => go({}, { within })} />}
            {text && <Chip label={`Matching "${text}"`} onRemove={() => go({}, { ...keep, q: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={() => go({ typeKeys: undefined, applicationId: undefined, portfolioId: undefined }, { q: undefined })}>Clear all</Button>
          </div>
        )}
      </section>

      <div className={twoColumnClass}>
        <Panel title={`${STATE_LABELS[state]} reviews`} count={data ? items.length : undefined} state={list} skeletonRows={8}>
          {d => (
            <>
              {items.length ? <ReviewTable items={items} today={today} /> : (
                <div className="flex flex-col items-start gap-2 py-1">
                  <Empty>
                    {state === 'overdue' ? 'No review is overdue' : state === 'due' ? 'No review is due' : state === 'done' ? 'No review is done' : state === 'cancelled' ? 'No review is cancelled' : 'No review'}
                    {anyFilter ? ' with these filters.' : '.'}
                  </Empty>
                  {anyFilter && <Button variant="outline" size="sm" onClick={() => go({ typeKeys: undefined, applicationId: undefined, portfolioId: undefined }, { q: undefined })}>Clear the filters</Button>}
                </div>
              )}
              <MoreRowsNote skipToken={d.skipToken} what="reviews" />
            </>
          )}
        </Panel>
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel title="By review type" state={list} skeletonRows={5} notes={[]} subtitle={`${STATE_LABELS[state]} reviews in the list`}>
            {() => {
              const counts = countBy(items, r => r.reviewType, 'No type');
              if (!counts.length) return <Empty>No review to count.</Empty>;
              return (
                <BarList
                  ariaLabel="Reviews by type"
                  noun={['review', 'reviews']}
                  labelWidth={150}
                  items={counts.map(c => ({ key: c.key, label: c.label, count: c.count }))}
                  onDrill={b => b.key && go({ typeKeys: [b.key] }, keep)}
                />
              );
            }}
          </Panel>
          <Panel title="By portfolio" state={list} skeletonRows={4} notes={[]} subtitle={`${STATE_LABELS[state]} reviews in the list`}>
            {() => {
              const counts = countBy(items, r => (r.portfolio ? { key: r.portfolio.portfolioId.toLowerCase(), label: r.portfolio.name } : null), 'No portfolio');
              if (!counts.length) return <Empty>No review to count.</Empty>;
              return (
                <BarList
                  ariaLabel="Reviews by portfolio"
                  noun={['review', 'reviews']}
                  labelWidth={150}
                  items={counts.map(c => ({ key: c.key, label: c.label, count: c.count, muted: c.key === null }))}
                  onDrill={b => (b.key ? go({ portfolioId: b.key }, keep) : undefined)}
                />
              );
            }}
          </Panel>
        </div>
      </div>
    </>
  );
}

function ReviewTable({ items, today }: { items: ReviewItem[]; today: string }) {
  const narrow = useNarrow();
  if (narrow) {
    return (
      <RowList label="Reviews">
        {items.map(r => (
          <Row
            key={r.reviewId}
            wideEnd
            primary={<TextLink href={hrefTo({ page: 'review', id: r.reviewId.toLowerCase() })}>{r.reviewType.label}</TextLink>}
            secondary={r.businessApplicationId
              ? <TextLink href={hrefTo({ page: 'app', id: r.businessApplicationId.toLowerCase() })}>{r.applicationName}</TextLink>
              : 'No application'}
            tertiary={`${dueText(r)}.${r.leadOwnerRole ? ` ${r.leadOwnerRole.label}: ${r.leadOwner?.displayLabel ?? 'no holder'}.` : ''}`}
            badges={<AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} />}
            end={(
              <>
                <StatusBadge status={r.status} />
                <OverdueBadge review={r} today={today} />
                {r.isMine && <Badge variant="brand">Yours</Badge>}
              </>
            )}
          />
        ))}
      </RowList>
    );
  }
  return (
    <Table aria-label="Reviews">
      <TableHeader>
        <TableRow>
          <TableHead>Review</TableHead>
          <TableHead>Application</TableHead>
          <TableHead>When</TableHead>
          <TableHead>Lead owner</TableHead>
          <TableHead>Status</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map(r => (
          <TableRow key={r.reviewId} className="align-top">
            <TableCell className="whitespace-normal">
              <div className="flex flex-col items-start gap-1">
                <TextLink href={hrefTo({ page: 'review', id: r.reviewId.toLowerCase() })} className="text-sm">{r.reviewType.label}</TextLink>
                <Muted>{triggerText(r.trigger)}</Muted>
                <AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} />
              </div>
            </TableCell>
            <TableCell className="whitespace-normal">
              {r.businessApplicationId
                ? <TextLink href={hrefTo({ page: 'app', id: r.businessApplicationId.toLowerCase() })} className="text-sm">{r.applicationName}</TextLink>
                : <Muted>No application</Muted>}
              {r.portfolio && <div className="text-xs text-navy-3">{r.portfolio.name}</div>}
            </TableCell>
            <TableCell className="text-sm whitespace-normal text-navy">
              {dueText(r)}
              {r.status.meaning === 'done' && r.nextDueOn && <div className="text-xs text-navy-3">Next due {fmtDate(r.nextDueOn)}</div>}
            </TableCell>
            <TableCell className="text-sm whitespace-normal text-navy">
              {r.leadOwnerRole ? <div className="text-xs text-navy-3">{r.leadOwnerRole.label}</div> : null}
              {r.leadOwner ? <PersonLink principal={r.leadOwner} /> : r.leadOwnerRole ? <ToneBadge tone="warning">No holder</ToneBadge> : <Muted>Not set</Muted>}
            </TableCell>
            <TableCell>
              <div className="flex flex-wrap gap-1">
                <StatusBadge status={r.status} />
                <OverdueBadge review={r} today={today} />
                {r.isMine && <Badge variant="brand">Yours</Badge>}
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// =====================================================================================================================
// One review: /app/records/review/<id>
// =====================================================================================================================

export function ReviewRecord({ reviewId }: { reviewId: string }) {
  const host = useHost();
  const today = host.today;
  const { me } = useWhoAmI();
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const [proposing, setProposing] = useStickyAction();
  const [submitting, setSubmitting] = useStickyAction();
  const [completing, setCompleting] = useStickyAction();
  const found = useTool('get_review', { reviewId });
  const detail = dataOf(found);
  const review = detail?.review ?? null;
  const appId = review?.businessApplicationId ?? null;
  const decisionType = review?.producesDecisionType ?? null;
  const appReviews = useAllPages('list_reviews', appId ? { state: 'all', businessApplicationId: appId, top: 100 } : null);
  const decisions = useTool('get_decision_log', review ? { subjectKind: 'review', subjectId: review.reviewId, top: 50 } : null);
  const typeDecisions = useTool('get_decision_log', appId && decisionType ? { businessApplicationId: appId, decisionTypeKeys: [decisionType.key], top: 20 } : null);

  if (found.status === 'loading') {
    return (
      <>
        <PageHeader eyebrow="Review" title="Review" meta={<span className="font-mono [overflow-wrap:anywhere]">{reviewId}</span>} />
        <div role="status" aria-label="Loading the review" className="flex flex-col gap-2.5 rounded-xl border bg-card p-5">
          {Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-4 w-full" />)}
        </div>
      </>
    );
  }
  if (!found.envelope.ok || !detail || !review) {
    return (
      <>
        <PageHeader eyebrow="Review" title="Review not found" meta={<span className="font-mono [overflow-wrap:anywhere]">{reviewId}</span>} />
        {!found.envelope.ok && found.envelope.error.code !== 'not_found' ? <PanelFailure failure={found.envelope} /> : (
          <Panel title="Nothing to show">
            <Empty>No review with this ID is one you can read.</Empty>
            <TextLink className="self-start text-sm" href={hrefTo({ page: 'reviews' })}>Go to the reviews</TextLink>
          </Panel>
        )}
      </>
    );
  }

  const title = `${review.reviewType.label}${review.applicationName ? `: ${review.applicationName}` : ''}`;
  const about = (dataOf(decisions)?.items ?? []).filter(d => d.status.meaning !== 'withdrawn');
  const current = about.find(d => d.status.meaning === 'proposed' || d.status.meaning === 'accepted') ?? null;
  const open = review.status.meaning === 'due';
  const isDone = review.status.meaning === 'done';
  // The outcome decision the review row names, when the decision log does not list it as about the review.
  const linkedOnly = review.outcomeDecisionId && !about.some(d => sameId(d.decisionId, review.outcomeDecisionId)) ? review.outcomeDecisionId : null;
  const mayPropose = (open && !!decisionType && review.isMine && mayCall(me, 'save_decision_proposal') && !current) || proposing;
  const mayEvidence = open && review.isMine && mayCall(me, 'save_link');
  const mayComplete = (detail.canComplete && mayCall(me, 'complete_review')) || completing;
  const cadence = detail.cadence;
  const appRows = dataOf(appReviews)?.items ?? [];
  const last = previousDone(appRows, review);
  const lastAccepted = (dataOf(typeDecisions)?.items ?? []).filter(d => d.status.meaning === 'accepted')
    .sort((a, b) => (b.decidedOn ?? '').localeCompare(a.decidedOn ?? ''))[0] ?? null;
  const subject = `${review.reviewType.label}${review.applicationName ? ` of ${review.applicationName}` : ''}`;
  const subjectInText = `${review.reviewType.label.toLowerCase()}${review.applicationName ? ` of ${review.applicationName}` : ''}`;
  const outcomeText = detail.outcomeDecision
    ? `${detail.outcomeDecision.statement}${detail.outcomeDecision.statement.endsWith('.') ? '' : '.'}`
    : 'none yet.';
  const completeSheet = (trigger: ReactElement) => (
    <CompleteReviewSheet
      reviewId={review.reviewId}
      subject={subject}
      outcome={outcomeText}
      nextDue={detail.nextDueIfDoneToday ? fmtDate(detail.nextDueIfDoneToday) : null}
      today={today}
      onOpenChange={setCompleting}
      trigger={trigger}
    />
  );

  return (
    <>
      <PageHeader
        eyebrow={review.recordNumber ? `Review ${review.recordNumber}` : 'Review'}
        title={title}
        subtitle={(
          <span className="flex flex-wrap items-center gap-1.5">
            <StatusBadge status={review.status} />
            <OverdueBadge review={review} today={today} />
            {review.isMine && <Badge variant="brand">Yours to do</Badge>}
            <AiMark isAiCreated={review.isAiCreated} performedBy={review.performedBy} />
          </span>
        )}
        meta={(
          <span className="flex flex-col gap-0.5">
            <StampLine performedBy={review.performedBy} />
            <DataStamp envelope={found.envelope} />
          </span>
        )}
        actions={(mayPropose || mayEvidence || mayComplete) ? (
          <>
            {mayEvidence && (
              <AddEvidenceSheet reviewId={review.reviewId} subject={subject} trigger={<Button variant="outline">Add evidence</Button>} />
            )}
            {mayPropose && decisionType && (
              <ProposeOutcomeSheet
                review={{ reviewId: review.reviewId, typeLabel: review.reviewType.label, applicationName: review.applicationName }}
                decisionType={decisionType}
                onProposed={setProposal}
                onOpenChange={setProposing}
                trigger={<Button>Propose the outcome</Button>}
              />
            )}
            {mayComplete && completeSheet(<Button>Complete the review</Button>)}
          </>
        ) : undefined}
      />

      <ProposalBanner
        key={proposal?.decisionId ?? 'none'}
        proposal={proposal}
        onDismiss={() => setProposal(null)}
        alreadySubmitted={!!proposal && about.some(d => sameId(d.decisionId, proposal.decisionId) && (isSubmitted(d) || d.status.meaning !== 'proposed'))}
      />

      <div className={twoColumnClass}>
        <Panel title="The review" notes={[]}>
          <dl className={factsClass}>
            <Fact label="Application">
              {review.businessApplicationId
                ? <TextLink href={hrefTo({ page: 'app', id: review.businessApplicationId.toLowerCase() })}>{review.applicationName}</TextLink>
                : 'None'}
            </Fact>
            <Fact label="Portfolio">{review.portfolio?.name ?? 'None'}</Fact>
            <Fact label="Review type">{review.reviewType.label}</Fact>
            <Fact label="Why it opened">{triggerText(review.trigger)}</Fact>
            <Fact label="Due on">{fmtDate(review.dueOn, 'No due date')}</Fact>
            <Fact label="Done on">{fmtDate(review.doneOn, 'Not done')}</Fact>
            <Fact label="Lead owner">
              {review.leadOwnerRole ? <span className="block text-xs text-navy-3">{review.leadOwnerRole.label}</span> : null}
              {review.leadOwner ? <PersonLink principal={review.leadOwner} /> : review.leadOwnerRole ? 'No holder' : 'Not set'}
            </Fact>
            <Fact label="Reviewer">{review.reviewer ? <PersonLink principal={review.reviewer} /> : 'Not recorded'}</Fact>
            <Fact label="Outcome recorded as">{decisionType ? withArticle(`${decisionType.label.toLowerCase()} decision`, true) : 'No decision type'}</Fact>
            <Fact label="Cadence">
              {cadence ? `Every ${plural(cadence.intervalDays, 'day', 'days')} for this application's tier` : 'No cadence: it opens when something asks for it'}
            </Fact>
            <Fact label="Next due">
              {fmtDate(review.nextDueOn, isDone ? 'None: no cadence' : detail.nextDueIfDoneToday ? `${fmtDate(detail.nextDueIfDoneToday)} if done today` : 'None: no cadence')}
            </Fact>
          </dl>
          {review.description && <p className="m-0 whitespace-pre-wrap text-sm text-navy-2">{review.description}</p>}
        </Panel>

        <Panel title="What completing it needs" notes={[]} subtitle="Propose the outcome, have it accepted, then complete the review">
          <ol className="m-0 flex list-none flex-col gap-0 p-0" aria-label="Steps to complete the review">
            <Step
              done={!!current || isDone}
              title={decisionType ? `Propose the outcome: ${withArticle(`${decisionType.label.toLowerCase()} decision`)}` : 'Propose the outcome decision'}
              detail={current
                ? <TextLink href={hrefTo({ page: 'decision', id: current.decisionId.toLowerCase() })}>{current.statement}</TextLink>
                : isDone ? 'Recorded.'
                  : !decisionType ? 'This review type names no decision type for its outcome.'
                    : mayPropose ? 'Use "Propose the outcome" at the top of the page.'
                      : review.isMine ? 'Your roles cannot propose this decision.'
                        : `The lead owner${review.leadOwner ? `, ${review.leadOwner.displayLabel},` : ''} or a person with the decision right proposes it.`}
            />
            <Step
              done={(!!current && (isSubmitted(current) || current.status.meaning === 'accepted')) || isDone}
              title="Submit it to the people who accept it"
              detail={!current ? (isDone ? 'Recorded.' : 'After the proposal.')
                : submitting || (!isSubmitted(current) && current.status.meaning === 'proposed' && sameId(current.proposedBy?.objectId, me?.principal.objectId) && mayCall(me, 'submit_record'))
                  ? <SubmitDecisionSheet decisionId={current.decisionId} subject={`Outcome of the ${subjectInText}`} onOpenChange={setSubmitting} trigger={<Button size="sm" variant="outline" className="mt-1">Submit for approval</Button>} />
                  : isSubmitted(current) || current.status.meaning === 'accepted' ? `${approvalsText(current)}.`
                    : `Not submitted yet. ${current.proposedBy ? `${current.proposedBy.displayLabel} proposed it and` : 'Whoever proposed it'} submits it.`}
            />
            <Step
              done={current?.status.meaning === 'accepted' || isDone}
              title="People accept the decision"
              detail={current?.status.meaning === 'accepted'
                ? `Accepted${current.decidedOn ? ` ${fmtDate(current.decidedOn)}` : ''}${current.outcome ? `: ${current.outcome.label}` : ''}.`
                : isDone ? 'Accepted.'
                : current && isSubmitted(current) ? <>Waiting. <TextLink href={hrefTo({ page: 'decision', id: current.decisionId.toLowerCase() })}>See who is asked</TextLink>.</>
                  : 'After it is submitted.'}
            />
            <Step
              done={isDone}
              title="Complete the review"
              detail={isDone
                ? `Done${review.doneOn ? ` ${fmtDate(review.doneOn)}` : ''}${review.reviewer ? ` by ${review.reviewer.displayLabel}` : ''}.${review.nextDueOn ? ` The next one is due ${fmtDate(review.nextDueOn)}.` : ''}`
                : mayComplete ? completeSheet(<Button size="sm" variant="outline" className="mt-1">Complete the review</Button>)
                  : detail.cannotComplete ?? 'After the outcome decision is accepted.'}
            />
          </ol>
          {detail.gates.length > 0 && (
            <div className="flex flex-col gap-2 border-t border-line pt-3">
              {detail.gates.map(g => (
                <div key={`${g.gateKey}-${g.requirementKey ?? 'decision'}`} className="flex flex-col gap-1.5">
                  <span className="flex flex-wrap items-center gap-1.5 text-sm text-navy">
                    {g.feedsBy === 'gate-decision'
                      ? `Feeds the ${g.gateLabel.toLowerCase()} gate: its outcome decision is the gate's decision.`
                      : `Feeds the ${g.gateLabel.toLowerCase()} gate: a done ${review.reviewType.label.toLowerCase()} is required.`}
                    {g.met ? <ToneBadge tone="success">Met</ToneBadge> : <ToneBadge tone="warning">Not met yet</ToneBadge>}
                  </span>
                  {g.guideline && <GuidelineChip number={g.guideline} guideline={g.guidelineRef} />}
                </div>
              ))}
            </div>
          )}
        </Panel>
      </div>

      <div className={twoColumnClass}>
        <Panel title="Outcome decisions" state={decisions} skeletonRows={3} count={dataOf(decisions) ? about.length + (linkedOnly ? 1 : 0) : undefined} subtitle="Decisions recorded about this review">
          {() => (
            <>
              {!about.length && !linkedOnly && <Empty>No outcome decision about this review yet.</Empty>}
              {linkedOnly && (
                <span className="text-sm text-navy">
                  The review records its outcome as{' '}
                  <TextLink href={hrefTo({ page: 'decision', id: linkedOnly.toLowerCase() })}>{detail.outcomeDecision?.statement ?? 'this decision'}</TextLink>.
                </span>
              )}
              {about.length > 0 && <DecisionRows items={about} />}
            </>
          )}
        </Panel>
        <Panel title="Evidence" count={detail.evidence.length} subtitle="Links to what the review looked at, held where they live">
          {detail.evidence.length ? (
            <RowList label="Evidence">
              {detail.evidence.map(e => (
                <Row
                  key={e.evidenceLinkId}
                  primary={e.url ? <ExternalLink href={e.url}>{e.label}</ExternalLink> : e.label}
                  secondary={`${e.linkKind.label}${e.evidenceLevel ? `. ${e.evidenceLevel.label}` : ''}${e.capturedOn ? `. Added ${fmtDate(e.capturedOn)}` : ''}`}
                  badges={<AiMark isAiCreated={e.isAiCreated} performedBy={e.performedBy} />}
                />
              ))}
            </RowList>
          ) : <Empty>No evidence is linked to this review.</Empty>}
        </Panel>
      </div>

      <div className={twoColumnClass}>
        <Panel title="Last outcome" state={appId ? appReviews : undefined} skeletonRows={3} notes={[]}>
          {appId ? () => (
            <LastOutcome last={last} lastAccepted={lastAccepted} decisionTypeLabel={decisionType?.label ?? null} />
          ) : <Empty>This review has no application, so it has no earlier review to compare.</Empty>}
        </Panel>
        {appId ? (
          <Panel
            title="Other reviews of this application"
            state={appReviews}
            skeletonRows={4}
            count={dataOf(appReviews) ? appRows.filter(r => r.reviewId !== review.reviewId).length : undefined}
            actions={<TextLink className="text-sm" href={hrefTo({ page: 'reviews', state: 'all', applicationId: appId.toLowerCase() })}>All of them in the list</TextLink>}
          >
            {() => {
              const others = appRows.filter(r => r.reviewId !== review.reviewId);
              if (!others.length) return <Empty>No other review of this application.</Empty>;
              return (
                <RowList label="Other reviews of this application">
                  {others.map(r => (
                    <Row
                      key={r.reviewId}
                      primary={<TextLink href={hrefTo({ page: 'review', id: r.reviewId.toLowerCase() })}>{r.reviewType.label}</TextLink>}
                      secondary={`${dueText(r)}. ${triggerText(r.trigger)}.`}
                      end={<><StatusBadge status={r.status} /><OverdueBadge review={r} today={today} /></>}
                    />
                  ))}
                </RowList>
              );
            }}
          </Panel>
        ) : <div />}
      </div>
    </>
  );
}

// One step of completing a review: an icon and a word for its state (never color alone), a title and a detail line.
function Step({ done, title, detail }: { done: boolean; title: string; detail: ReactNode }) {
  const Icon = done ? CircleCheck : Clock;
  return (
    <li className="grid grid-cols-[24px_minmax(0,1fr)] gap-x-2.5 border-t border-line py-2.5 first:border-t-0 first:pt-0.5">
      <Icon className={cn('mt-0.5 size-5', done ? 'text-success' : 'text-gold-strong')} aria-hidden />
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="flex flex-wrap items-center gap-1.5 text-sm font-semibold text-navy">
          {title}
          {done ? <ToneBadge tone="success">Done</ToneBadge> : <ToneBadge tone="warning" icon={<Clock aria-hidden />}>To do</ToneBadge>}
        </span>
        <span className="text-xs leading-5 text-navy-2 [overflow-wrap:anywhere]">{detail}</span>
      </div>
    </li>
  );
}

function DecisionRows({ items }: { items: DecisionLogItem[] }) {
  return (
    <RowList label="Outcome decisions">
      {items.map(d => (
        <Row
          key={d.decisionId}
          wideEnd
          primary={<TextLink href={hrefTo({ page: 'decision', id: d.decisionId.toLowerCase() })}>{d.statement}</TextLink>}
          secondary={`${d.decisionType.label}${d.outcome ? `: ${d.outcome.label}` : ''}. ${approvalsText(d)}.`}
          tertiary={d.proposedBy ? `Proposed by ${d.proposedBy.displayLabel}${d.decidedOn ? `, decided ${fmtDate(d.decidedOn)}` : ''}.` : undefined}
          badges={<AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />}
          end={<StatusBadge status={d.status} />}
        />
      ))}
    </RowList>
  );
}

function LastOutcome({ last, lastAccepted, decisionTypeLabel }: { last: ReviewItem | null; lastAccepted: DecisionLogItem | null; decisionTypeLabel: string | null }) {
  if (!last && !lastAccepted) return <Empty>No earlier review of this type is done, and no {decisionTypeLabel ? `${decisionTypeLabel.toLowerCase()} ` : ''}decision is accepted for this application.</Empty>;
  return (
    <div className="flex flex-col gap-3">
      {last && (
        <div className="flex flex-col gap-0.5">
          <span className="text-sm text-navy">
            The last {last.reviewType.label.toLowerCase()} was done {fmtDate(last.doneOn, 'on a date not recorded')}
            {last.reviewer ? <> by <PersonLink principal={last.reviewer} /></> : null}.
          </span>
          <span className="flex flex-wrap gap-x-3 text-xs">
            <TextLink href={hrefTo({ page: 'review', id: last.reviewId.toLowerCase() })}>Open that review</TextLink>
            {last.outcomeDecisionId && <TextLink href={hrefTo({ page: 'decision', id: last.outcomeDecisionId.toLowerCase() })}>Its outcome decision</TextLink>}
          </span>
        </div>
      )}
      {lastAccepted && (
        <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2">
          <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Last accepted {lastAccepted.decisionType.label.toLowerCase()} decision</span>
          <TextLink href={hrefTo({ page: 'decision', id: lastAccepted.decisionId.toLowerCase() })} className="text-sm">{lastAccepted.statement}</TextLink>
          <span className="text-xs text-navy-2">
            {lastAccepted.outcome ? `${lastAccepted.outcome.label}. ` : ''}
            {lastAccepted.decidedOn ? `Accepted ${fmtDate(lastAccepted.decidedOn)}` : 'Accepted'}
            {lastAccepted.decider ? ` by ${lastAccepted.decider.displayLabel}` : ''}.
          </span>
        </div>
      )}
    </div>
  );
}
