// Portfolios (screen "Portfolios", priority 2): a per-portfolio overview and the PMO's review pack (playbook 06).
//   /app/portfolios?inactive=1   RouteOf<'portfolios'>: every portfolio in tree order, with its figures. inactive=1 also
//                                lists retired portfolios.
//   /app/portfolios/<id>         one portfolio. The record link the tools return, /app/records/portfolio/<id>, opens it
//                                too.
// Reads: list_portfolios (the tree with the accountable person and the figures, children included), get_portfolio_summary
// (by stage and by tier; and the register-wide owner gap count, so the figure matches the Overview and Owners), find_applications (portfolioId: the review pack), list_reviews (the last review of each
// application, the overdue ones and the benefit reviews), get_decision_log (portfolioId: the open decisions and the
// portfolio decisions), get_application_card (dependencies: the dependants of each application in the pack).
// Writes, each in the shared side sheet: save_portfolio (a person only: the platform admin creates, moves, names the
// accountable person and retires; the PMO edits the name, description and level), save_decision_proposal (a portfolio
// decision about an application: keep, invest, replace or retire; the PMO is responsible, the business owner
// accountable).
// Wave 4 (D-41): the text search q= keeps the portfolios whose name, level or accountable person hold the words, with the
// portfolios above them so the tree still reads as a tree (pmo-portfolio/logic.ts searchTree); the list's count is the
// portfolios that match. The figures above stay the totals of every portfolio, each opening its own rows.
import { ArrowRight, ChevronRight } from 'lucide-react';
import { useState } from 'react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { BarList, DataStamp, Empty, KpiTile, MoreRowsNote, Muted, PageHeader, Panel, StatusBadge, TextLink, ToneBadge, useNarrow } from '../components/blocks';
import { Row, RowList } from '../components/rows';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { Label } from '../components/ui/label';
import { Skeleton } from '../components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { ApplicationCard, ApplicationSummary, DecisionLogItem, PortfolioItem, PortfolioSummary } from '../data/contracts';
import { fmtDate, plural } from '../data/format';
import { useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf, readFailed, useTool } from '../data/toolClient';
import { cn } from '../lib/utils';
import { kpiRowClass4, PersonLink, twoColumnClass } from './owners-reviews/people';
import { lastDoneByApp, mayPropose, openDecisionsByApp, ownerGapsOf, portfolioTotals, portfolioTree, sameId, searchTree, WAY_OUT_OUTCOMES } from './pmo-portfolio/logic';
import { ListSearch, setListText, useListText } from '../components/listSearch';
import { useBelowWide } from './pmo-portfolio/layout';
import { useAllRows, useRightRows } from './pmo-portfolio/reads';
import { PortfolioActiveSheet, PortfolioSheet, ProposeDecisionSheet } from './pmo-portfolio/sheets';
// Wave 4b: a row's Propose stays mounted once the row reads its proposal (Sticky), so its "Send it for approval
// now?" follow-up stays open.
import { Sticky } from './work-approvals/parts';

const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

// A figure that opens its rows. aria-label says what the number counts, so a screen reader hears more than "3".
function CountLink({ n, href, what }: { n: number; href: string; what: string }) {
  if (n === 0) return <span className="text-sm text-navy-3 tabular-nums" aria-label={`No ${what}`}>0</span>;
  return <TextLink href={href} className="text-sm tabular-nums" aria-label={`${n} ${what}`}>{n}</TextLink>;
}

const drills = (portfolioId?: string) => ({
  applications: hrefTo({ page: 'applications', filter: portfolioId ? { portfolioIds: [portfolioId] } : {} }),
  gaps: hrefTo({ page: 'owners', view: 'gaps', ...(portfolioId ? { portfolioId } : {}) }),
  overdue: hrefTo({ page: 'reviews', state: 'overdue', ...(portfolioId ? { portfolioId } : {}) }),
  decisions: hrefTo({ page: 'decisions', statusMeanings: ['proposed'], ...(portfolioId ? { portfolioId } : {}) }),
});

// =====================================================================================================================
// The list: /app/portfolios
// =====================================================================================================================

export function Portfolios() {
  const session = useSession();
  const narrow = useNarrow();
  const inactive = useQueryParam('inactive', /^1$/) === '1';
  const list = useAllRows('list_portfolios', { top: 100, ...(inactive ? { includeInactive: true } : {}) });
  const items = dataOf(list)?.items ?? [];
  const text = useListText();
  const found = searchTree(portfolioTree(items), text);
  const rows = found.rows;
  const totals = dataOf(list) ? portfolioTotals(items.filter(i => i.isActive)) : null;
  // The register-wide owner gap count is the server's one count (document 24, B24-17), the number the Overview shows for
  // all portfolios and the Owners page lists: it also holds the applications in no portfolio, which no row adds up.
  const summary = useTool('get_portfolio_summary', { groupBy: 'portfolio' });
  const allGaps = dataOf(summary)?.ownerGaps ?? totals?.ownerGaps ?? null;
  const isAdmin = session.hasRole('platform-admin');
  const canCreate = session.can('save_portfolio') && isAdmin;
  const all = drills();

  return (
    <>
      <PageHeader
        eyebrow="Register"
        title="Portfolios"
        subtitle="Each portfolio with its applications, owner gaps, overdue reviews and open decisions. A portfolio's figures include the portfolios inside it."
        meta={<DataStamp envelope={list.status === 'done' ? list.envelope : undefined} />}
        actions={canCreate ? <PortfolioSheet existing={null} portfolios={items} fullRights /> : undefined}
      />

      <div className={kpiRowClass4} role="group" aria-label="Key figures">
        <KpiTile label="Applications" value={totals?.applications ?? null} failed={readFailed(list)} caption="Registered and draft, in every portfolio" onClick={() => navigate(all.applications)} />
        <KpiTile label="Owner gaps" value={allGaps} failed={readFailed(summary) && readFailed(list)} caption="Owner roles with no holder, no deputy, or a holder whose account is off, across the register" attention={!!allGaps} onClick={() => navigate(all.gaps)} />
        <KpiTile label="Reviews overdue" value={totals?.reviewsOverdue ?? null} failed={readFailed(list)} caption="Due, past the due date" attention={!!totals?.reviewsOverdue} onClick={() => navigate(all.overdue)} />
        <KpiTile label="Open decisions" value={totals?.openDecisions ?? null} failed={readFailed(list)} caption="Proposed, not decided yet" onClick={() => navigate(all.decisions)} />
      </div>

      <div className={twoColumnClass}>
        <Panel
          title="Portfolios"
          count={dataOf(list) ? (text ? found.matched.size : rows.length) : undefined}
          state={list}
          skeletonRows={6}
          actions={(
            <div className="flex flex-wrap items-center gap-2">
              <ListSearch label="Search the portfolios by name, level or accountable person" placeholder="Portfolio or person" className="w-[220px]" />
              <Checkbox id="portfolios-inactive" checked={inactive} onCheckedChange={v => navigate(withParams(`${window.location.pathname}${window.location.search}`, { inactive: v === true ? '1' : undefined }), true)} />
              <Label htmlFor="portfolios-inactive" className="font-normal">Show retired portfolios</Label>
            </div>
          )}
        >
          {d => (
            <>
              {rows.length === 0 && text ? (
                <span className="flex flex-col items-start gap-2">
                  <Empty>{`No portfolio matches "${text}".`}</Empty>
                  <Button variant="outline" size="sm" onClick={() => setListText(undefined)}>Clear the search</Button>
                </span>
              ) : rows.length === 0 ? (
                <Empty>{canCreate ? 'No portfolio yet. Create the first one.' : 'No portfolio yet. The platform admin creates them.'}</Empty>
              ) : narrow ? (
                <RowList label="Portfolios">
                  {rows.map(({ item, depth }) => {
                    const links = drills(item.portfolioId.toLowerCase());
                    return (
                      <Row
                        key={item.portfolioId}
                        primary={(
                          <span style={{ paddingLeft: `${depth * 12}px` }} className="inline-flex items-center gap-1">
                            {depth > 0 && <ChevronRight className="size-3.5 text-navy-3" aria-hidden />}
                            <TextLink href={hrefTo({ page: 'portfolio', id: item.portfolioId.toLowerCase() })}>{item.name}</TextLink>
                          </span>
                        )}
                        secondary={[item.level?.label, item.accountablePrincipal ? `Accountable: ${item.accountablePrincipal.displayLabel}` : 'No accountable person'].filter(Boolean).join(' · ')}
                        tertiary={(
                          <span className="flex flex-wrap gap-x-3 gap-y-0.5">
                            <span>Applications <CountLink n={item.counts.applications} href={links.applications} what={`applications in ${item.name}`} /></span>
                            <span>Owner gaps <CountLink n={ownerGapsOf(item.counts)} href={links.gaps} what={`owner gaps in ${item.name}`} /></span>
                            <span>Overdue <CountLink n={item.counts.reviewsOverdue} href={links.overdue} what={`overdue reviews in ${item.name}`} /></span>
                            <span>Open decisions <CountLink n={item.counts.openDecisions} href={links.decisions} what={`open decisions in ${item.name}`} /></span>
                          </span>
                        )}
                        end={!item.isActive ? <ToneBadge tone="subtle" icon={null}>Retired</ToneBadge> : undefined}
                      />
                    );
                  })}
                </RowList>
              ) : (
                <Table aria-label="Portfolios">
                  <TableHeader>
                    <TableRow>
                      <TableHead className="min-w-[200px]">Portfolio</TableHead>
                      <TableHead>Accountable</TableHead>
                      <TableHead className="text-right">Applications</TableHead>
                      <TableHead className="text-right">Owner gaps</TableHead>
                      <TableHead className="text-right">Reviews overdue</TableHead>
                      <TableHead className="text-right">Open decisions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {rows.map(({ item, depth }) => {
                      const links = drills(item.portfolioId.toLowerCase());
                      return (
                        <TableRow key={item.portfolioId} className="align-top">
                          <TableCell className="whitespace-normal">
                            <div className="flex flex-col items-start gap-0.5" style={{ paddingLeft: `${depth * 18}px` }}>
                              <span className="inline-flex items-center gap-1">
                                {depth > 0 && <ChevronRight className="size-3.5 text-navy-3" aria-hidden />}
                                <TextLink href={hrefTo({ page: 'portfolio', id: item.portfolioId.toLowerCase() })} className="text-sm">{item.name}</TextLink>
                                {!item.isActive && <ToneBadge tone="subtle" icon={null}>Retired</ToneBadge>}
                              </span>
                              {item.level && <Muted>{item.level.label}</Muted>}
                            </div>
                          </TableCell>
                          <TableCell className="text-sm whitespace-normal">
                            {item.accountablePrincipal ? <PersonLink principal={item.accountablePrincipal} /> : <Muted>Not named</Muted>}
                          </TableCell>
                          <TableCell className="text-right"><CountLink n={item.counts.applications} href={links.applications} what={`applications in ${item.name}`} /></TableCell>
                          <TableCell className="text-right"><CountLink n={ownerGapsOf(item.counts)} href={links.gaps} what={`owner gaps in ${item.name}`} /></TableCell>
                          <TableCell className="text-right"><CountLink n={item.counts.reviewsOverdue} href={links.overdue} what={`overdue reviews in ${item.name}`} /></TableCell>
                          <TableCell className="text-right"><CountLink n={item.counts.openDecisions} href={links.decisions} what={`open decisions in ${item.name}`} /></TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              )}
              <MoreRowsNote skipToken={d.skipToken} what="portfolios" />
            </>
          )}
        </Panel>
        <div className={stack}>
          <Panel title="Applications by portfolio" subtitle="The top of the tree; each one counts the portfolios inside it" state={list} skeletonRows={5} notes={[]}>
            {() => {
              const top = rows.filter(r => r.depth === 0 && r.item.isActive);
              if (!top.length) return <Empty>No portfolio to count.</Empty>;
              return (
                <BarList
                  ariaLabel="Applications by portfolio"
                  noun={['application', 'applications']}
                  labelWidth={160}
                  items={top.map(r => ({ key: r.item.portfolioId.toLowerCase(), label: r.item.name, count: r.item.counts.applications }))}
                  onDrill={b => b.key && navigate(hrefTo({ page: 'portfolio', id: b.key }))}
                />
              );
            }}
          </Panel>
          <Panel title="Owner gaps by portfolio" subtitle="Owner roles with no holder, no deputy, or a holder whose account is off" state={list} skeletonRows={5} notes={[]}>
            {() => {
              const top = rows.filter(r => r.depth === 0 && r.item.isActive && ownerGapsOf(r.item.counts) > 0);
              if (!top.length) return <Empty>No application has an owner gap.</Empty>;
              return (
                <BarList
                  ariaLabel="Owner gaps by portfolio"
                  noun={['owner gap', 'owner gaps']}
                  labelWidth={160}
                  items={top.map(r => ({ key: r.item.portfolioId.toLowerCase(), label: r.item.name, count: ownerGapsOf(r.item.counts) }))}
                  onDrill={b => b.key && navigate(drills(b.key).gaps)}
                />
              );
            }}
          </Panel>
          <Panel title="Where the PMO works">
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0 text-sm">
              <li><TextLink href={hrefTo({ page: 'demand' })}>Demand: requests to score and priorities waiting</TextLink></li>
              <li><TextLink href={hrefTo({ page: 'reviews', state: 'due', typeKeys: ['benefit'] })}>Benefit reviews due</TextLink></li>
              <li><TextLink href={hrefTo({ page: 'retirement' })}>Applications on their way out</TextLink></li>
            </ul>
          </Panel>
        </div>
      </div>
    </>
  );
}

// =====================================================================================================================
// One portfolio: /app/portfolios/<id>
// =====================================================================================================================

const PACK_PAGE = 25;

export function PortfolioPage({ portfolioId }: { portfolioId: string }) {
  const session = useSession();
  const list = useAllRows('list_portfolios', { top: 100, includeInactive: true });
  const items = dataOf(list)?.items ?? [];
  const portfolio = items.find(p => sameId(p.portfolioId, portfolioId)) ?? null;

  if (list.status === 'loading') {
    return (
      <>
        <PageHeader eyebrow="Portfolio" title={<Skeleton className="h-8 w-64" />} />
        <Panel title="Portfolio" state={list}>{() => null}</Panel>
      </>
    );
  }
  if (!list.envelope.ok || !portfolio) {
    return (
      <>
        <PageHeader eyebrow="Portfolio" title="Portfolio" />
        {!list.envelope.ok ? (
          <Panel title="Portfolio" state={list}>{() => null}</Panel>
        ) : (
          <Panel title="Nothing to show">
            <Empty>No portfolio with this address is in the list you can read. It may have been removed, or the link is wrong.</Empty>
            <TextLink href={hrefTo({ page: 'portfolios' })}>All portfolios</TextLink>
          </Panel>
        )}
      </>
    );
  }
  return <PortfolioView portfolio={portfolio} all={items} envelope={list.envelope} canSave={session.can('save_portfolio')} isAdmin={session.hasRole('platform-admin')} />;
}

function PortfolioView({ portfolio, all, envelope, canSave, isAdmin }: {
  portfolio: PortfolioItem;
  all: PortfolioItem[];
  envelope: Parameters<typeof DataStamp>[0]['envelope'];
  canSave: boolean;
  isAdmin: boolean;
}) {
  const id = portfolio.portfolioId.toLowerCase();
  const links = drills(id);
  const children = portfolioTree(all).filter(r => sameId(r.item.parentPortfolioId, id));
  const parents = portfolio.path.slice(0, -1);
  const byStage = useTool('get_portfolio_summary', { portfolioId: id, groupBy: 'stage' });
  const byTier = useTool('get_portfolio_summary', { portfolioId: id, groupBy: 'tier' });

  return (
    <>
      <nav aria-label="Portfolio path" className="mb-2 flex flex-wrap items-center gap-1 text-xs text-navy-3">
        <TextLink href={hrefTo({ page: 'portfolios' })}>Portfolios</TextLink>
        {parents.map(p => (
          <span key={p.portfolioId} className="inline-flex items-center gap-1">
            <ChevronRight className="size-3" aria-hidden />
            <TextLink href={hrefTo({ page: 'portfolio', id: p.portfolioId.toLowerCase() })}>{p.name}</TextLink>
          </span>
        ))}
      </nav>
      <PageHeader
        eyebrow={portfolio.level?.label ?? 'Portfolio'}
        title={portfolio.name}
        status={!portfolio.isActive ? <ToneBadge tone="subtle" icon={null}>Retired</ToneBadge> : undefined}
        subtitle={portfolio.accountablePrincipal
          ? <>Accountable: <PersonLink principal={portfolio.accountablePrincipal} /></>
          : 'No accountable person is named.'}
        meta={<DataStamp envelope={envelope} />}
        actions={canSave ? (
          <>
            <PortfolioSheet existing={portfolio} portfolios={all} fullRights={isAdmin} />
            {isAdmin && portfolio.isActive && (
              <PortfolioSheet
                existing={null}
                portfolios={all}
                fullRights
                parentPortfolioId={portfolio.portfolioId}
                trigger={<Button variant="outline">New portfolio inside</Button>}
              />
            )}
            {isAdmin && <PortfolioActiveSheet portfolio={portfolio} />}
          </>
        ) : undefined}
      />

      <div className={kpiRowClass4} role="group" aria-label="Key figures">
        <KpiTile label="Applications" value={portfolio.counts.applications} caption="Here and in the portfolios inside" onClick={() => navigate(links.applications)} />
        <KpiTile label="Owner gaps" value={ownerGapsOf(portfolio.counts)} caption="Owner roles with no holder, no deputy, or a holder whose account is off" attention={ownerGapsOf(portfolio.counts) > 0} onClick={() => navigate(links.gaps)} />
        <KpiTile label="Reviews overdue" value={portfolio.counts.reviewsOverdue} caption="Due, past the due date" attention={portfolio.counts.reviewsOverdue > 0} onClick={() => navigate(links.overdue)} />
        <KpiTile label="Open decisions" value={portfolio.counts.openDecisions} caption="Proposed, not decided yet" onClick={() => navigate(links.decisions)} />
      </div>

      <div className="mb-4 narrow:mb-3">
        <ReviewPack portfolio={portfolio} />
      </div>

      <div className={twoColumnClass}>
        <div className={stack}>
          <PortfolioDecisions portfolioId={id} />
          <BenefitReviews portfolioId={id} />
        </div>
        <div className={stack}>
          <Panel title="By stage" subtitle="Applications in this portfolio and below it" state={byStage} skeletonRows={5} notes={[]}>
            {(d: PortfolioSummary) => <SummaryBars data={d} noun="stage" onDrill={key => navigate(hrefTo({ page: 'applications', filter: { portfolioIds: [id], lifecycleStageKeys: [key] } }))} />}
          </Panel>
          <Panel title="By tier" subtitle="Applications in this portfolio and below it" state={byTier} skeletonRows={3} notes={[]}>
            {(d: PortfolioSummary) => <SummaryBars data={d} noun="tier" onDrill={key => navigate(hrefTo({ page: 'applications', filter: { portfolioIds: [id], tierKeys: [key] } }))} />}
          </Panel>
          {children.length > 0 && (
            <Panel title="Portfolios inside" count={children.length}>
              <RowList label="Portfolios inside">
                {children.map(({ item }) => (
                  <Row
                    key={item.portfolioId}
                    primary={<TextLink href={hrefTo({ page: 'portfolio', id: item.portfolioId.toLowerCase() })}>{item.name}</TextLink>}
                    secondary={`${plural(item.counts.applications, 'application', 'applications')} · ${item.accountablePrincipal ? `Accountable: ${item.accountablePrincipal.displayLabel}` : 'No accountable person'}`}
                    end={!item.isActive ? <ToneBadge tone="subtle" icon={null}>Retired</ToneBadge> : undefined}
                  />
                ))}
              </RowList>
            </Panel>
          )}
          <Panel title="Go to">
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0 text-sm">
              <li><TextLink href={hrefTo({ page: 'demand', portfolioId: id })}>Demand in this portfolio</TextLink></li>
              <li><TextLink href={hrefTo({ page: 'owners', portfolioId: id })}>Owners in this portfolio</TextLink></li>
              <li><TextLink href={hrefTo({ page: 'reviews', portfolioId: id })}>Reviews in this portfolio</TextLink></li>
              <li><TextLink href={hrefTo({ page: 'decisions', portfolioId: id })}>Decisions in this portfolio</TextLink></li>
              <li><TextLink href={hrefTo({ page: 'documents', portfolioId: id })}>Documents in this portfolio</TextLink></li>
            </ul>
          </Panel>
        </div>
      </div>
    </>
  );
}

function SummaryBars({ data, noun, onDrill }: { data: PortfolioSummary; noun: string; onDrill: (key: string) => void }) {
  const buckets = data.buckets.filter(b => b.count > 0);
  if (!buckets.length) return <Empty>No application to count.</Empty>;
  return (
    <BarList
      ariaLabel={`Applications by ${noun}`}
      noun={['application', 'applications']}
      labelWidth={150}
      items={buckets.map(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }))}
      onDrill={b => b.key && onDrill(b.key)}
    />
  );
}

// ---------- the review pack ----------

// The PMO's review pack (playbook 06): each application with its owner, tier, stage, last review, open decisions and
// dependants, and the portfolio decision to propose about it.
function ReviewPack({ portfolio }: { portfolio: PortfolioItem }) {
  const session = useSession();
  const narrow = useBelowWide();
  const id = portfolio.portfolioId.toLowerCase();
  const [shown, setShown] = useState(PACK_PAGE);
  const apps = useAllRows('find_applications', { portfolioId: id, top: 100 });
  const done = useAllRows('list_reviews', { portfolioId: id, state: 'done', top: 100 });
  const overdue = useAllRows('list_reviews', { portfolioId: id, state: 'overdue', top: 100 });
  const open = useAllRows('get_decision_log', { portfolioId: id, statusMeanings: ['proposed'], top: 100 });
  const portfolioLog = useAllRows('get_decision_log', { portfolioId: id, decisionTypeKeys: ['portfolio'], statusMeanings: ['accepted'], top: 100 });
  const rights = useRightRows();
  const lastDone = lastDoneByApp(dataOf(done)?.items ?? []);
  const overdueApps = new Set((dataOf(overdue)?.items ?? []).map(r => r.businessApplicationId?.toLowerCase()).filter(Boolean));
  const openByApp = openDecisionsByApp(dataOf(open)?.items ?? [], d => (d.subjectKind === 'business-application' ? d.subjectId : null));
  // A decision-log read that failed (a refused read, "Too many requests just now") says so on each row: "Not read",
  // never 0 open decisions and "No portfolio decision" with a Propose button (QA, fix round 1).
  const openNotRead = open.status === 'done' && !open.envelope.ok;
  const callNotRead = (open.status === 'done' && !open.envelope.ok) || (portfolioLog.status === 'done' && !portfolioLog.envelope.ok);
  // A portfolio decision already proposed about the application: the pack links it, and offers no second proposal.
  const proposedCall = (appId: string) => openByApp.get(appId)?.find(d => d.decisionType.key === 'portfolio');
  const acceptedByApp = new Map<string, DecisionLogItem>();
  for (const d of dataOf(portfolioLog)?.items ?? []) {
    const key = d.subjectId.toLowerCase();
    if (d.subjectKind === 'business-application' && !d.supersededBy && !acceptedByApp.has(key)) acceptedByApp.set(key, d);
  }
  const canPropose = session.can('save_decision_proposal');
  const mayFor = (a: ApplicationSummary) => {
    if (!canPropose) return false;
    const owner = [
      sameId(a.businessOwner?.objectId, session.objectId) ? 'business-owner' : null,
      sameId(a.technicalOwner?.objectId, session.objectId) ? 'technical-owner' : null,
    ].filter((x): x is string => !!x);
    return mayPropose(rights.rows, 'portfolio', session.roleKeys, owner);
  };
  const items = dataOf(apps)?.items ?? [];
  const visible = items.slice(0, shown);

  return (
    <Panel
      title="Review pack"
      subtitle="Each application with its owner, tier, stage, last review, open decisions and dependants"
      count={dataOf(apps) ? items.length : undefined}
      state={apps}
      skeletonRows={8}
      id="review-pack"
    >
      {d => (
        <>
          {items.length === 0 ? <Empty>No registered application sits in this portfolio.</Empty> : narrow ? (
            <RowList label="Review pack">
              {visible.map(a => {
                const appId = a.businessApplicationId.toLowerCase();
                const last = lastDone.get(appId);
                const openCount = openByApp.get(appId)?.length ?? 0;
                return (
                  <Row
                    key={a.businessApplicationId}
                    wideEnd
                    primary={<TextLink href={hrefTo({ page: 'app', id: appId })}>{a.name}</TextLink>}
                    secondary={[a.tier?.label ?? 'No tier', a.stage?.label ?? 'No stage', a.businessOwner ? `Business owner ${a.businessOwner.displayLabel}` : 'No business owner'].join(' · ')}
                    tertiary={(
                      <span className="flex flex-wrap gap-x-3 gap-y-0.5">
                        <span>{last ? `Last review ${fmtDate(last.doneOn)}` : 'Never reviewed'}</span>
                        <span>Open decisions {openNotRead ? <Muted>Not read</Muted> : <CountLink n={openCount} href={hrefTo({ page: 'decisions', applicationId: appId, statusMeanings: ['proposed'] })} what={`open decisions about ${a.name}`} />}</span>
                        <span>Dependants <DependantsCount applicationId={appId} name={a.name} /></span>
                      </span>
                    )}
                    badges={overdueApps.has(appId) ? <ToneBadge tone="danger">Review overdue</ToneBadge> : undefined}
                    end={callNotRead ? <Muted>Not read</Muted> : <PortfolioCall decision={acceptedByApp.get(appId)} proposed={proposedCall(appId)} />}
                    endSub={!callNotRead && mayFor(a) ? <Sticky show={!proposedCall(appId)}><ProposeFor app={a} accepted={acceptedByApp.get(appId)} /></Sticky> : undefined}
                  />
                );
              })}
            </RowList>
          ) : (
            <Table aria-label="Review pack">
              <TableHeader>
                <TableRow>
                  <TableHead className="min-w-[180px]">Application</TableHead>
                  <TableHead>Business owner</TableHead>
                  <TableHead>Tier and stage</TableHead>
                  <TableHead>Last review</TableHead>
                  <TableHead className="text-right">Open decisions</TableHead>
                  <TableHead className="text-right">Dependants</TableHead>
                  <TableHead className="min-w-[150px]">Portfolio call</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visible.map(a => {
                  const appId = a.businessApplicationId.toLowerCase();
                  const last = lastDone.get(appId);
                  const openCount = openByApp.get(appId)?.length ?? 0;
                  return (
                    <TableRow key={a.businessApplicationId} className="align-top">
                      <TableCell className="whitespace-normal">
                        <div className="flex flex-col items-start gap-1">
                          <TextLink href={hrefTo({ page: 'app', id: appId })} className="text-sm">{a.name}</TextLink>
                          <Muted>{a.kind.label}</Muted>
                        </div>
                      </TableCell>
                      <TableCell className="text-sm whitespace-normal">
                        {a.businessOwner ? <PersonLink principal={a.businessOwner} /> : <ToneBadge tone="warning">Not named</ToneBadge>}
                      </TableCell>
                      <TableCell className="text-sm whitespace-normal text-navy">
                        {a.tier?.label ?? 'No tier'}
                        <div className="text-xs text-navy-3">{a.stage?.label ?? 'No stage'}</div>
                      </TableCell>
                      <TableCell className="text-sm whitespace-normal text-navy">
                        {last
                          ? <TextLink href={recordHref(last.recordUrl) ?? hrefTo({ page: 'review', id: last.reviewId.toLowerCase() })}>{fmtDate(last.doneOn)}</TextLink>
                          : <Muted>Never</Muted>}
                        {last && <div className="text-xs text-navy-3">{last.reviewType.label}</div>}
                        {overdueApps.has(appId) && <div className="mt-1"><ToneBadge tone="danger">Overdue</ToneBadge></div>}
                      </TableCell>
                      <TableCell className="text-right">
                        {openNotRead ? <Muted>Not read</Muted> : <CountLink n={openCount} href={hrefTo({ page: 'decisions', applicationId: appId, statusMeanings: ['proposed'] })} what={`open decisions about ${a.name}`} />}
                      </TableCell>
                      <TableCell className="text-right"><DependantsCount applicationId={appId} name={a.name} /></TableCell>
                      <TableCell className="whitespace-normal">
                        <div className="flex flex-col items-start gap-1.5">
                          {callNotRead ? <Muted>Not read</Muted> : <PortfolioCall decision={acceptedByApp.get(appId)} proposed={proposedCall(appId)} />}
                          {!callNotRead && mayFor(a) && <Sticky show={!proposedCall(appId)}><ProposeFor app={a} accepted={acceptedByApp.get(appId)} /></Sticky>}
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
          {items.length > shown && (
            <Button variant="outline" size="sm" className="self-start" onClick={() => setShown(n => n + PACK_PAGE)}>
              Show {Math.min(PACK_PAGE, items.length - shown)} more of {items.length}
            </Button>
          )}
          <MoreRowsNote skipToken={d.skipToken} what="applications" />
        </>
      )}
    </Panel>
  );
}

// The latest accepted portfolio decision about the application (keep, invest, replace or retire), and a proposed one
// that waits.
function PortfolioCall({ decision, proposed }: { decision: DecisionLogItem | undefined; proposed: DecisionLogItem | undefined }) {
  const waiting = proposed ? (
    <TextLink href={hrefTo({ page: 'decision', id: proposed.decisionId.toLowerCase() })} className="text-xs">
      {`Proposed: ${proposed.outcome?.label ?? 'no outcome'}`}
    </TextLink>
  ) : null;
  if (!decision) return waiting ? <span className="flex flex-col items-start gap-0.5"><Muted>No accepted decision</Muted>{waiting}</span> : <Muted>No portfolio decision</Muted>;
  const out = !!decision.outcome && (WAY_OUT_OUTCOMES as readonly string[]).includes(decision.outcome.key);
  return (
    <span className="flex flex-col items-start gap-0.5">
      <TextLink href={hrefTo({ page: 'decision', id: decision.decisionId.toLowerCase() })} className="text-sm">
        {decision.outcome?.label ?? 'No outcome'}
      </TextLink>
      <span className="text-xs text-navy-3">
        {decision.decidedOn ? fmtDate(decision.decidedOn) : decision.status.label}
        {out && <> · <TextLink href={hrefTo({ page: 'retirement', applicationId: decision.subjectId.toLowerCase() })}>Retirement</TextLink></>}
      </span>
      {waiting}
    </span>
  );
}

function ProposeFor({ app, accepted }: { app: ApplicationSummary; accepted: DecisionLogItem | undefined }) {
  return (
    <ProposeDecisionSheet
      decisionTypeKey="portfolio"
      typeLabel="Portfolio"
      subjectId={app.businessApplicationId}
      subjectLabel={app.name}
      triggerLabel="Propose"
      supersedes={accepted ? { decisionId: accepted.decisionId, label: `the ${accepted.outcome?.label.toLowerCase() ?? 'earlier'} decision of ${fmtDate(accepted.decidedOn)}` } : null}
      effect="The business owner decides: keep, invest, replace or retire."
    />
  );
}

// The live applications that depend on this one (get_application_card, the dependencies section).
function DependantsCount({ applicationId, name }: { applicationId: string; name: string }) {
  const state = useTool('get_application_card', { businessApplicationId: applicationId, sections: ['dependencies'] });
  const data = dataOf(state) as ApplicationCard | null;
  if (state.status === 'loading') return <Skeleton className="ml-auto inline-block h-4 w-6" aria-label={`Reading the dependants of ${name}`} />;
  if (!data?.dependencies) return <Muted>Not read</Muted>;
  return <CountLink n={data.dependencies.usedByCount} href={hrefTo({ page: 'map', id: applicationId })} what={`applications depend on ${name}`} />;
}

// ---------- decisions and benefit reviews ----------

function PortfolioDecisions({ portfolioId }: { portfolioId: string }) {
  const state = useRead('get_decision_log', { portfolioId, decisionTypeKeys: ['portfolio'], top: 25 });
  return (
    <Panel
      title="Portfolio decisions"
      subtitle="Keep, invest, replace or retire, newest first"
      state={state}
      skeletonRows={4}
      actions={<TextLink href={hrefTo({ page: 'decisions', portfolioId, typeKeys: ['portfolio'] })} className="inline-flex items-center gap-1 text-sm">All <ArrowRight className="size-3.5" aria-hidden /></TextLink>}
    >
      {d => d.items.length === 0 ? <Empty>No portfolio decision about these applications yet.</Empty> : (
        <>
          <RowList label="Portfolio decisions">
            {d.items.map(item => (
              <Row
                key={item.decisionId}
                primary={<TextLink href={hrefTo({ page: 'decision', id: item.decisionId.toLowerCase() })}>{item.statement || item.subjectLabel}</TextLink>}
                secondary={`${item.subjectLabel} · ${item.outcome?.label ?? 'No outcome'}`}
                tertiary={item.decidedOn ? `Decided ${fmtDate(item.decidedOn)}${item.decider ? ` by ${item.decider.displayLabel}` : ''}` : item.proposedBy ? `Proposed by ${item.proposedBy.displayLabel}` : 'Not decided yet'}
                end={<StatusBadge status={item.status} />}
              />
            ))}
          </RowList>
          <MoreRowsNote skipToken={d.skipToken} what="portfolio decisions" />
        </>
      )}
    </Panel>
  );
}

function BenefitReviews({ portfolioId }: { portfolioId: string }) {
  const state = useRead('list_reviews', { portfolioId, reviewTypeKeys: ['benefit'], state: 'all', top: 25 });
  return (
    <Panel
      title="Benefit reviews"
      subtitle="Did the application deliver what it promised? Each records a portfolio decision."
      state={state}
      skeletonRows={3}
      actions={<TextLink href={hrefTo({ page: 'reviews', state: 'all', typeKeys: ['benefit'], portfolioId })} className="inline-flex items-center gap-1 text-sm">All <ArrowRight className="size-3.5" aria-hidden /></TextLink>}
    >
      {d => d.items.length === 0 ? <Empty>No benefit review in this portfolio.</Empty> : (
        <>
          <RowList label="Benefit reviews">
            {d.items.map(r => (
              <Row
                key={r.reviewId}
                primary={<TextLink href={hrefTo({ page: 'review', id: r.reviewId.toLowerCase() })}>{r.applicationName ?? r.reviewType.label}</TextLink>}
                secondary={r.status.meaning === 'done' ? `Done ${fmtDate(r.doneOn)}` : r.dueOn ? `Due ${fmtDate(r.dueOn)}` : 'No due date'}
                end={(
                  <span className={cn('flex flex-wrap justify-end gap-1')}>
                    <StatusBadge status={r.status} />
                    {r.isOverdue && <ToneBadge tone="danger">Overdue</ToneBadge>}
                  </span>
                )}
              />
            ))}
          </RowList>
          <MoreRowsNote skipToken={d.skipToken} what="benefit reviews" />
        </>
      )}
    </Panel>
  );
}


