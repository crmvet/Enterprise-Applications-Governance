// Demand (the PMO's demand board): scored demand by portfolio, horizon and size band, the requests still to score, and
// the priority decisions waiting on sponsors. The intake list's demand view (/app/intake?view=demand) stays the one
// list of requests; this page is the PMO's planning view over the same rows.
//   /app/demand?portfolio=<id>   RouteOf<'demand'>; the page's own filters, in the address so Back keeps them:
//                                portfolio=none (requests with no portfolio yet), size=<key|none>, horizon=<key|none>,
//                                priority=<outcome|proposed|none>, scored=yes|no, sort=score|priority|size-band|horizon|
//                                recent|title
// Reads: list_intake_requests (view demand: every row for the figures, and the filtered, ordered list; each request with
// its portfolio, its own or its linked application's, document 24 B24-17), list_portfolios (the tree, for the top of
// it), find_applications only for a server whose list carries no portfolio yet (the walk of each portfolio's
// applications this page used before wave 3), get_decision_log (the
// priority decisions waiting, and whether they went for approval), get_configuration (size bands, horizons and the
// priority outcomes: list-value and decision-outcome rows).
// Writes, each in the shared side sheet: score_intake_request (score, size band, horizon and benefit; PMO and platform
// admin), save_decision_proposal (propose a priority: the PMO is responsible; the sponsor answers on the decision page).
// Wave 4 (D-41): the text search q= goes to list_intake_requests text, on the read behind the figures and the bars too,
// so every number on the board counts the requests the search keeps and opens them.
import { Info } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { hrefTo, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import type { RouteOf } from '../app/routes';
import { BarList, DataStamp, Empty, KpiTile, LinkButton, MoreRowsNote, Muted, PageHeader, Panel, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { Row, RowList } from '../components/rows';
import { AiMark } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { DecisionLogItem, IntakeOrderBy, IntakeSummary, PortfolioItem } from '../data/contracts';
import { plural } from '../data/format';
import { useListValues, useOutcomes } from '../data/labels';
import { useSession } from '../data/session';
import { dataOf, readFailed } from '../data/toolClient';
import { ScoreSheet } from './intake/sheets';
import { ListSearch, useListText } from '../components/listSearch';
import { Chip } from './owners-reviews/people';
import { isSubmitted } from './owners-reviews/model';
import { NewProposalSheet } from './work-approvals/decisionSheets';
import { SubmitFollowUp, useFollowUp } from './data/sheetParts';
import {
  bucketsBy,
  DEMAND_SORTS,
  demandFigures,
  demandInput,
  KEY_OR_NONE,
  mayPropose,
  needsOwnRead,
  pathText,
  portfolioOfApp,
  priorityState,
  sameId,
  SORT_PATTERN,
  topPortfolioOf,
  type DemandFilter,
} from './pmo-portfolio/logic';
import { MAX_PORTFOLIOS, useAllRows, usePortfolioMembers, useRightRows } from './pmo-portfolio/reads';

const ANY = '__any__';
const NONE = 'none';
const kpiRow = 'mb-4 grid grid-cols-4 gap-3 medium:not-narrow:grid-cols-2 narrow:grid-cols-2 narrow:gap-2.5';
const columns = 'mb-4 grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:mb-3 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

export function Demand({ route }: { route: RouteOf<'demand'> }) {
  const session = useSession();
  const narrow = useNarrow();
  const noPortfolio = useQueryParam('portfolio', /^none$/) === NONE;
  const filter: DemandFilter = {
    portfolio: route.portfolioId ?? (noPortfolio ? NONE : undefined),
    sizeBand: useQueryParam('size', KEY_OR_NONE),
    horizon: useQueryParam('horizon', KEY_OR_NONE),
    priority: useQueryParam('priority', KEY_OR_NONE),
    scored: useQueryParam('scored', /^(yes|no)$/) as DemandFilter['scored'],
    sort: (useQueryParam('sort', SORT_PATTERN) ?? 'score') as IntakeOrderBy,
    text: useListText(),
  };

  // Every request on the table, for the figures and the bars; the list reads again only for a filter or an order.
  const all = useAllRows('list_intake_requests', { view: 'demand', top: 100, ...(filter.text ? { text: filter.text } : {}) });
  const ownRead = needsOwnRead(filter);
  const own = useAllRows('list_intake_requests', ownRead ? demandInput(filter) : null);
  const listState = ownRead ? own : all;
  const portfolios = useAllRows('list_portfolios', { top: 100 });
  const portfolioItems = dataOf(portfolios)?.items ?? null;
  // The list carries each request's portfolio (wave 3). Only a list without it needs the walk of every portfolio's
  // applications; the walk then reads find_applications once per portfolio, as before.
  const allRows = dataOf(all)?.items ?? null;
  const walk = !!allRows && allRows.some(i => i.portfolio === undefined);
  const members = usePortfolioMembers(walk ? portfolioItems : null);
  const membersLoading = walk && members.loading;
  const waitingLog = useAllRows('get_decision_log', { decisionTypeKeys: ['priority'], statusMeanings: ['proposed'], top: 100 });
  const sizes = useListValues('size-band');
  const horizons = useListValues('horizon');
  const priorities = useOutcomes('priority');
  const rights = useRightRows();

  const pItems = portfolioItems ?? [];
  const portfolioOf = (item: IntakeSummary): PortfolioItem | null => {
    if (item.portfolio !== undefined) return item.portfolio ? pItems.find(x => sameId(x.portfolioId, item.portfolio!.portfolioId)) ?? null : null;
    return portfolioOfApp(item.businessApplicationId, members.members, pItems);
  };
  const inPortfolio = (item: IntakeSummary): boolean => {
    if (!filter.portfolio) return true;
    if (item.portfolio !== undefined) {
      if (filter.portfolio === NONE) return item.portfolio === null;
      if (!item.portfolio) return false;
      // The request's portfolio, or one below the chosen one (its path names the chosen one).
      const own = pItems.find(x => sameId(x.portfolioId, item.portfolio!.portfolioId));
      return sameId(item.portfolio.portfolioId, filter.portfolio) || !!own?.path.some(x => sameId(x.portfolioId, filter.portfolio));
    }
    if (filter.portfolio === NONE) return portfolioOf(item) === null;
    return !!item.businessApplicationId && !!members.members.get(filter.portfolio.toLowerCase())?.has(item.businessApplicationId.toLowerCase());
  };
  const waitingFor = filter.portfolio !== undefined && membersLoading;

  const allItems = dataOf(all)?.items ?? null;
  const scope = allItems && !waitingFor ? allItems.filter(inPortfolio) : null;
  const listItems = (dataOf(listState)?.items ?? []).filter(inPortfolio);
  const figures = scope ? demandFigures(scope) : null;

  const canScore = session.can('score_intake_request');
  const canPropose = session.can('save_decision_proposal') && mayPropose(rights.rows, 'priority', session.roleKeys, []);

  // ---------- the address ----------
  const go = (next: Partial<DemandFilter>) => {
    const f = { ...filter, ...next };
    const portfolioId = f.portfolio && f.portfolio !== NONE ? f.portfolio : undefined;
    const params: Record<string, string | undefined> = {
      size: f.sizeBand,
      horizon: f.horizon,
      priority: f.priority,
      scored: f.scored,
      sort: f.sort === 'score' ? undefined : f.sort,
      q: f.text,
    };
    if (f.portfolio === NONE) params.portfolio = NONE;
    navigate(withParams(hrefTo({ page: 'demand', ...(portfolioId ? { portfolioId } : {}) }), params), true);
  };
  const clearAll = () => go({ portfolio: undefined, sizeBand: undefined, horizon: undefined, priority: undefined, scored: undefined, text: undefined });
  const anyFilter = !!(filter.portfolio || filter.sizeBand || filter.horizon || filter.priority || filter.scored || filter.text);

  const portfolioName = (id: string) => {
    if (id === NONE) return 'No portfolio yet';
    const p = pItems.find(x => sameId(x.portfolioId, id));
    return p ? pathText(p) : 'A portfolio';
  };
  const priorityName = (key: string) => (key === 'proposed' ? 'Proposed, waiting' : key === NONE ? 'No priority' : priorities.options.find(o => o.key === key)?.label ?? key);
  const valueName = (labels: { label: (k: string) => string }, key: string) => (key === NONE ? 'Not set' : labels.label(key));

  const priorityOrder = [...priorities.options.map(o => ({ key: o.key, label: o.label })), { key: 'proposed', label: 'Proposed, waiting' }];
  const waitingItems = scope ? scope.filter(i => priorityState(i) === 'proposed') : [];
  const toScore = scope ? scope.filter(i => i.score === null) : [];
  // A request scored from this panel stays in it, marked scored, while its sheet is open: the sheet keeps the engine's
  // answer in view, and the list does not jump under the pointer. Once the sheet closes the row goes, so the list
  // agrees with its count (QA, fix round 1: three rows under "Still to score 2").
  const listedToScore = useRef(new Set<string>());
  const [, setClosedSheets] = useState(0);
  for (const i of toScore) listedToScore.current.add(i.intakeRequestId.toLowerCase());
  const sheetClosed = (id: string) => {
    listedToScore.current.delete(id.toLowerCase());
    setClosedSheets(n => n + 1);
  };
  const toScoreShown = scope ? scope.filter(i => i.score === null || listedToScore.current.has(i.intakeRequestId.toLowerCase())) : [];
  const logById = new Map((dataOf(waitingLog)?.items ?? []).map(d => [d.decisionId.toLowerCase(), d]));

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Demand"
        subtitle="Requests on the table, scored and sized by the PMO, by portfolio, horizon and size band, and the priority decisions waiting for their sponsor."
        meta={<DataStamp envelope={all.status === 'done' ? all.envelope : undefined} />}
        actions={(
          <Button asChild variant="outline">
            <a href={hrefTo({ page: 'intake', view: 'demand' })}>The demand list</a>
          </Button>
        )}
      />

      <div className={kpiRow} role="group" aria-label="Key figures">
        <KpiTile label="On the table" value={figures?.onTable ?? null} failed={readFailed(all)} caption="Submitted, in progress or completed" onClick={() => go({ sizeBand: undefined, horizon: undefined, priority: undefined, scored: undefined })} />
        <KpiTile label="To score" value={figures?.toScore ?? null} failed={readFailed(all)} caption="No PMO score yet" attention={!!figures?.toScore} onClick={() => go({ scored: 'no', priority: undefined })} />
        <KpiTile label="Priority waiting" value={figures?.waiting ?? null} failed={readFailed(all)} caption="Proposed, the sponsor or the business owner decides" attention={!!figures?.waiting} onClick={() => go({ priority: 'proposed', scored: undefined })} />
        <KpiTile label="Scored" value={figures?.scored ?? null} failed={readFailed(all)} caption="With a PMO score" onClick={() => go({ scored: 'yes', priority: undefined })} />
      </div>

      <div className="mb-4 flex items-start gap-x-2 rounded-lg border border-info-line bg-info-soft px-3 py-2.5 text-sm leading-5 text-navy-2" role="note">
        <Info className="mt-0.5 size-4 shrink-0 text-info" aria-hidden />
        <span>
          The PMO records the score, size band and horizon on each request. Priority is a decision: the PMO proposes it,
          submits it on the decision page, and the sponsor (or, for a new application, the business owner the request
          names) answers it there.
        </span>
      </div>

      <div className={columns}>
        <div className={stack}>
          <Panel
            title="Still to score"
            subtitle="Requests on the table with no PMO score"
            count={scope ? toScore.length : undefined}
            state={all}
            skeletonRows={4}
            notes={[]}
          >
            {() => !scope ? <Muted>Reading the portfolios.</Muted> : toScoreShown.length === 0 ? (
              <Empty>Every request on the table{filter.portfolio ? ' in this portfolio' : ''} has a score.</Empty>
            ) : (
              <RowList label="Requests to score">
                {toScoreShown.slice(0, 8).map(item => (
                  <Row
                    key={item.intakeRequestId}
                    primary={<RequestLink item={item} />}
                    secondary={whereText(item, portfolioOf(item))}
                    tertiary={`Size ${item.sizeBand?.label ?? 'not set'} · Horizon ${item.horizon?.label ?? 'not set'}`}
                    badges={item.score !== null ? <ToneBadge tone="success">{`Scored ${item.score}`}</ToneBadge> : undefined}
                    end={canScore ? <ScoreSheet intakeRequestId={item.intakeRequestId} title={item.title} score={item.score} sizeBandKey={item.sizeBand?.key ?? null} horizonKey={item.horizon?.key ?? null} onClosed={() => sheetClosed(item.intakeRequestId)} /> : undefined}
                  />
                ))}
                {toScoreShown.length > 8 && (
                  <li className="border-t border-line pt-2.5">
                    <LinkButton className="text-sm" onClick={() => go({ scored: 'no', priority: undefined })}>
                      {`Show all ${toScore.length} in the demand list below`}
                    </LinkButton>
                  </li>
                )}
              </RowList>
            )}
          </Panel>
          <Panel
            title="Priorities waiting"
            subtitle="Proposed priority decisions: the sponsor answers each on its decision page"
            count={scope ? waitingItems.length : undefined}
            state={all}
            skeletonRows={3}
            notes={[]}
          >
            {() => !scope ? <Muted>Reading the portfolios.</Muted> : waitingItems.length === 0 ? (
              <Empty>No priority decision waits{filter.portfolio ? ' in this portfolio' : ''}.</Empty>
            ) : (
              <RowList label="Priority decisions waiting">
                {waitingItems.map(item => {
                  const p = item.priority!;
                  const log = logById.get(p.decisionId.toLowerCase());
                  return (
                    <Row
                      key={item.intakeRequestId}
                      primary={<RequestLink item={item} />}
                      secondary={`Proposed priority: ${p.outcome?.label ?? 'no outcome'}`}
                      tertiary={waitingText(log)}
                      end={<TextLink href={hrefTo({ page: 'decision', id: p.decisionId.toLowerCase() })} className="text-sm">Open the decision</TextLink>}
                    />
                  );
                })}
              </RowList>
            )}
          </Panel>
        </div>
        <div className={stack}>
          <Panel title="By portfolio" subtitle="Requests on the table, by the top of the portfolio tree" state={portfolios} skeletonRows={4} notes={[]}>
            {() => {
              if (!scope || membersLoading) return <Muted>Reading which portfolio each request sits in.</Muted>;
              const top = (item: IntakeSummary) => topPortfolioOf(portfolioOf(item), pItems);
              const order = pItems.filter(p => !p.parentPortfolioId).map(p => ({ key: p.portfolioId.toLowerCase(), label: p.name }));
              const buckets = bucketsBy(scope, i => top(i)?.portfolioId.toLowerCase() ?? null, order, 'No portfolio yet');
              return (
                <>
                  {buckets.length ? (
                    <BarList
                      ariaLabel="Requests by portfolio"
                      noun={['request', 'requests']}
                      labelWidth={160}
                      items={buckets.map(b => ({ key: b.key, label: b.key ? portfolioName(b.key) : b.label, count: b.count, muted: b.key === null }))}
                      onDrill={b => go({ portfolio: b.key ?? NONE })}
                    />
                  ) : <Empty>No request to count.</Empty>}
                  <Muted>A request for a new application has no portfolio until triage registers its application.</Muted>
                  {walk && members.failed > 0 && <Muted>The applications of {plural(members.failed, 'portfolio', 'portfolios')} could not be read, so some requests may show with no portfolio.</Muted>}
                  {walk && pItems.length > MAX_PORTFOLIOS && <Muted>Only the first {MAX_PORTFOLIOS} portfolios are read.</Muted>}
                </>
              );
            }}
          </Panel>
          <Panel title="By horizon" subtitle="When the business needs it" state={all} skeletonRows={3} notes={[]}>
            {() => <Bars items={scope} keyOf={i => i.horizon?.key ?? null} order={horizons.options} ariaLabel="Requests by horizon" onDrill={k => go({ horizon: k ?? NONE })} />}
          </Panel>
          <Panel title="By size band" subtitle="A rough size of the work" state={all} skeletonRows={3} notes={[]}>
            {() => <Bars items={scope} keyOf={i => i.sizeBand?.key ?? null} order={sizes.options} ariaLabel="Requests by size band" onDrill={k => go({ sizeBand: k ?? NONE })} />}
          </Panel>
          <Panel title="By priority" subtitle="Accepted priority, proposed or none" state={all} skeletonRows={3} notes={[]}>
            {() => (
              <Bars
                items={scope}
                keyOf={i => (priorityState(i) === 'accepted' ? i.priority?.outcome?.key ?? null : priorityState(i) === 'proposed' ? 'proposed' : null)}
                order={priorityOrder}
                ariaLabel="Requests by priority"
                noneLabel="No priority"
                onDrill={k => go({ priority: k ?? NONE })}
              />
            )}
          </Panel>
        </div>
      </div>

      <section aria-label="Filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <ListSearch label="Search the requests on the table by title, need or number" placeholder="Request, need or number" />
        <div className="flex flex-wrap items-center gap-2">
          <FilterSelect label="Portfolio" value={filter.portfolio} onChange={v => go({ portfolio: v })} anyLabel="All portfolios" wide>
            <SelectItem value={NONE}>No portfolio yet</SelectItem>
            {pItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{pathText(p)}</SelectItem>)}
          </FilterSelect>
          <FilterSelect label="Size band" value={filter.sizeBand} onChange={v => go({ sizeBand: v })} anyLabel="Any size">
            {sizes.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            <SelectItem value={NONE}>Not set</SelectItem>
          </FilterSelect>
          <FilterSelect label="Horizon" value={filter.horizon} onChange={v => go({ horizon: v })} anyLabel="Any horizon">
            {horizons.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            <SelectItem value={NONE}>Not set</SelectItem>
          </FilterSelect>
          <FilterSelect label="Priority" value={filter.priority} onChange={v => go({ priority: v })} anyLabel="Any priority">
            {priorities.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            <SelectItem value="proposed">Proposed, waiting</SelectItem>
            <SelectItem value={NONE}>No priority</SelectItem>
          </FilterSelect>
          <FilterSelect label="Score" value={filter.scored} onChange={v => go({ scored: v as DemandFilter['scored'] })} anyLabel="Scored or not">
            <SelectItem value="yes">Scored</SelectItem>
            <SelectItem value="no">Not scored</SelectItem>
          </FilterSelect>
          <div className="ml-auto flex items-center gap-2 narrow:ml-0">
            <span className="text-xs text-navy-3" id="demand-sort-label">Sort</span>
            <Select value={filter.sort} onValueChange={v => go({ sort: v as IntakeOrderBy })}>
              <SelectTrigger className="w-[190px]" aria-labelledby="demand-sort-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                {DEMAND_SORTS.map(s => <SelectItem key={s.key} value={s.key}>{s.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
        </div>
        {anyFilter && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {filter.portfolio && <Chip label={`Portfolio: ${portfolioName(filter.portfolio)}`} onRemove={() => go({ portfolio: undefined })} />}
            {filter.sizeBand && <Chip label={`Size: ${valueName(sizes, filter.sizeBand)}`} onRemove={() => go({ sizeBand: undefined })} />}
            {filter.horizon && <Chip label={`Horizon: ${valueName(horizons, filter.horizon)}`} onRemove={() => go({ horizon: undefined })} />}
            {filter.priority && <Chip label={`Priority: ${priorityName(filter.priority)}`} onRemove={() => go({ priority: undefined })} />}
            {filter.scored && <Chip label={filter.scored === 'yes' ? 'Scored' : 'Not scored'} onRemove={() => go({ scored: undefined })} />}
            {filter.text && <Chip label={`Matching "${filter.text}"`} onRemove={() => go({ text: undefined })} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearAll}>Clear all</Button>
          </div>
        )}
      </section>

      <Panel
        title="Demand"
        subtitle={`Ordered by ${DEMAND_SORTS.find(s => s.key === filter.sort)?.label.toLowerCase() ?? 'score'}`}
        count={listState.status === 'done' && !waitingFor ? listItems.length : undefined}
        state={listState}
        skeletonRows={8}
      >
        {d => waitingFor ? <Muted>Reading which portfolio each request sits in.</Muted> : (
          <>
            {listItems.length === 0 ? (
              <div className="flex flex-col items-start gap-2 py-1">
                <Empty>{anyFilter ? 'No request on the table matches these filters.' : 'No request is on the table. Requests show here once they are submitted.'}</Empty>
                {anyFilter && <Button variant="outline" size="sm" onClick={clearAll}>Clear the filters</Button>}
              </div>
            ) : narrow ? (
              <RowList label="Demand">
                {listItems.map(item => (
                  <Row
                    key={item.intakeRequestId}
                    wideEnd
                    primary={<RequestLink item={item} />}
                    secondary={whereText(item, portfolioOf(item))}
                    tertiary={`Score ${item.score ?? 'not set'} · Size ${item.sizeBand?.label ?? 'not set'} · Horizon ${item.horizon?.label ?? 'not set'}`}
                    badges={<AiMark isAiCreated={item.isAiCreated} performedBy={item.performedBy} />}
                    end={<PriorityBadge item={item} />}
                    endSub={<RowActions item={item} canScore={canScore} canPropose={canPropose} />}
                  />
                ))}
              </RowList>
            ) : (
              <Table aria-label="Demand">
                <TableHeader>
                  <TableRow>
                    <TableHead>Request</TableHead>
                    <TableHead>Application and portfolio</TableHead>
                    <TableHead className="text-right">Score</TableHead>
                    <TableHead>Size</TableHead>
                    <TableHead>Horizon</TableHead>
                    <TableHead>Priority</TableHead>
                    <TableHead><span className="sr-only">Actions</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {listItems.map(item => {
                    const portfolio = portfolioOf(item);
                    return (
                      <TableRow key={item.intakeRequestId} className="align-top">
                        <TableCell className="whitespace-normal">
                          <div className="flex flex-col items-start gap-1">
                            <RequestLink item={item} />
                            <Muted>{item.requester ? `Requested by ${item.requester.displayLabel}` : 'No requester recorded'}</Muted>
                            <AiMark isAiCreated={item.isAiCreated} performedBy={item.performedBy} />
                          </div>
                        </TableCell>
                        <TableCell className="text-sm whitespace-normal text-navy">
                          {item.businessApplicationId
                            ? <TextLink href={hrefTo({ page: 'app', id: item.businessApplicationId.toLowerCase() })}>{item.applicationName ?? 'Application'}</TextLink>
                            : <Muted>New application</Muted>}
                          <div className="text-xs text-navy-3">{portfolio ? pathText(portfolio) : 'No portfolio yet'}</div>
                        </TableCell>
                        <TableCell className="text-right text-sm font-semibold text-navy tabular-nums">{item.score ?? <Muted>Not set</Muted>}</TableCell>
                        <TableCell className="text-sm text-navy">{item.sizeBand?.label ?? <Muted>Not set</Muted>}</TableCell>
                        <TableCell className="text-sm text-navy">{item.horizon?.label ?? <Muted>Not set</Muted>}</TableCell>
                        <TableCell className="whitespace-normal"><PriorityBadge item={item} /></TableCell>
                        <TableCell><RowActions item={item} canScore={canScore} canPropose={canPropose} /></TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            )}
            <MoreRowsNote skipToken={d.skipToken} what="requests" />
          </>
        )}
      </Panel>
    </>
  );
}

// ---------- parts ----------

function RequestLink({ item }: { item: IntakeSummary }) {
  return (
    <TextLink href={hrefTo({ page: 'intake-request', id: item.intakeRequestId.toLowerCase() })} className="text-sm">
      {item.recordNumber ? `${item.recordNumber}: ` : ''}{item.title || 'Untitled request'}
    </TextLink>
  );
}

function whereText(item: IntakeSummary, portfolio: PortfolioItem | null): string {
  const app = item.businessApplicationId ? item.applicationName ?? 'An application' : 'New application';
  return `${app} · ${portfolio ? pathText(portfolio) : 'No portfolio yet'}`;
}

function waitingText(log: DecisionLogItem | undefined): string {
  if (!log) return 'Waiting for a decision.';
  if (!isSubmitted(log)) return 'Not submitted yet: its proposer submits it on the decision page. Then the sponsor is asked, or the business owner when no sponsor is named.';
  const s = log.approvalsSummary;
  return s.pending > 0 ? `Submitted: ${plural(s.pending, 'answer', 'answers')} still to come.` : 'Submitted: waiting for the engine to settle it.';
}

function PriorityBadge({ item }: { item: IntakeSummary }) {
  const state = priorityState(item);
  if (state === 'accepted') return <ToneBadge tone="success">{item.priority?.outcome?.label ?? 'Decided'}</ToneBadge>;
  if (state === 'proposed') return <ToneBadge tone="warning">{`Proposed: ${item.priority?.outcome?.label ?? 'no outcome'}`}</ToneBadge>;
  if (state === 'other') return <ToneBadge tone="subtle" icon={null}>{item.priority?.status.label ?? 'Not decided'}</ToneBadge>;
  return <ToneBadge tone="subtle" icon={null}>No priority</ToneBadge>;
}

// Wave 4: "Propose priority" opens the submit follow-up right after the proposal is saved ("Send
// it for approval now?"), as the data sheets do, in place of moving to the decision page; Cancel leaves it proposed, and
// the decision page sends it later. The follow-up stays while the row re-reads and no longer offers the proposal.
// Wave 4b: the proposal sheet closes first and hands on the engine's answer, which the follow-up shows on top.
function RowActions({ item, canScore, canPropose }: { item: IntakeSummary; canScore: boolean; canPropose: boolean }) {
  const state = priorityState(item);
  const propose = canPropose && (state === 'none' || state === 'other');
  const follow = useFollowUp();
  const followUp = follow.data && (
    <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The priority decision for ${item.title}`} open={follow.open} onOpenChange={follow.onOpenChange} />
  );
  if (!canScore && !propose) return followUp || null;
  return (
    <div className="flex flex-wrap items-center justify-end gap-2 narrow:justify-start">
      {canScore && <ScoreSheet intakeRequestId={item.intakeRequestId} title={item.title} score={item.score} sizeBandKey={item.sizeBand?.key ?? null} horizonKey={item.horizon?.key ?? null} />}
      {propose && (
        <NewProposalSheet
          decisionTypeKey="priority"
          typeLabel="Priority"
          subjectKind="intake-request"
          subjectId={item.intakeRequestId}
          subjectLabel={item.title}
          triggerLabel="Propose priority"
          onProposed={(decisionId, answer) => follow.start({ decisionId, answer })}
        />
      )}
      {followUp}
    </div>
  );
}

function Bars(props: {
  items: IntakeSummary[] | null;
  keyOf: (item: IntakeSummary) => string | null;
  order: readonly { key: string; label: string }[];
  ariaLabel: string;
  noneLabel?: string;
  onDrill: (key: string | null) => void;
}) {
  if (!props.items) return <Muted>Reading which portfolio each request sits in.</Muted>;
  const buckets = bucketsBy(props.items, props.keyOf, props.order, props.noneLabel ?? 'Not set');
  if (!buckets.length) return <Empty>No request to count.</Empty>;
  return (
    <BarList
      ariaLabel={props.ariaLabel}
      noun={['request', 'requests']}
      labelWidth={140}
      items={buckets.map(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }))}
      onDrill={b => props.onDrill(b.key)}
    />
  );
}

function FilterSelect(props: { label: string; value: string | undefined; onChange: (value: string | undefined) => void; anyLabel: string; wide?: boolean; children: ReactNode }) {
  return (
    <Select value={props.value ?? ANY} onValueChange={v => props.onChange(v === ANY ? undefined : v)}>
      <SelectTrigger className={props.wide ? 'w-[220px] narrow:w-full' : 'w-[160px] narrow:flex-1'} aria-label={props.label}><SelectValue /></SelectTrigger>
      <SelectContent className="max-h-[320px]">
        <SelectItem value={ANY}>{props.anyLabel}</SelectItem>
        {props.children}
      </SelectContent>
    </Select>
  );
}
