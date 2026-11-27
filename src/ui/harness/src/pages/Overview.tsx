// Page 1: Portfolio overview and "needs attention".
// Reads (document 24 names): get_portfolio_summary three times (by stage with the owner gap list, by tier, by kind),
// get_deployments (flagged, last 30 days), get_change_schedule (next 14 days), get_configuration (owner-role labels).
// Every number comes from the server (stored rollups at scale). The page counts nothing itself.
// Product review 2026-11-24 (D-41): a portfolio picker (?portfolio=<id>, list_portfolios) narrows the summary tiles and
// the owner gaps, and the tiles they open; three more tiles count the approvals waiting (list_approvals, open), the AI
// drafts waiting (get_ai_activity) and the open exceptions (get_decision_log).
// Wave 3 (D-41): flagged deployments and the change windows follow the portfolio too, and the Timeline link carries
// the portfolio. Wave 4: both reads take the portfolio (portfolioId, child portfolios included; B24-21), so the page no
// longer reads the portfolio's applications to keep their rows. A data gaps tile (get_portfolio_summary dataGaps) opens /app/data/gaps. The owner
// gap tile is the server's one count (document 24, B24-17), the number Owners and Portfolios show for the same scope.
// Wave 4b review (D-41): the approvals and AI drafts tiles take the portfolio too (list_approvals and get_ai_activity
// portfolioId), and open Approvals and AI activity on that portfolio (portfolio=), where a "One portfolio" chip clears it.
import { ArrowRight, ShieldAlert, UserRoundX } from 'lucide-react';
import type { ReactNode } from 'react';
import { hrefTo, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import type { Bucket, ScheduledChange } from '../data/contracts';
import { addDays, dayOfMonth, fmtDate, fmtMonth, fmtTimeRange, fmtWeekday, fmtWeekdayOnly, inDays, inWindowDays, plural, utcDayRange, zoneLabel } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { activeFlags, flagHelp, flagText } from '../data/meanings';
import { dataOf, readFailed, useTool, type ToolState } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { BarList, DataStamp, Empty, KpiTile, MoreRowsNote, Muted, PageHeader, Panel, PanelFailure, TextLink, ToneBadge } from '../components/blocks';
import { Tooltip, TooltipContent, TooltipTrigger } from '../components/ui/tooltip';
import { ChangeTypeBadge } from './Application360';

// Two columns side by side; one column below 1024 px.
const rowClass = 'mb-4 grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-stretch gap-4 medium:grid-cols-1 narrow:mb-3 narrow:gap-3';
const groupTitleClass = 'mt-1 mb-1.5 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';
const itemBodyClass = 'flex min-w-0 flex-col gap-y-[3px]';
const itemNameClass = 'text-sm font-semibold text-navy';
const badgesClass = 'mt-[3px] flex flex-wrap gap-1.5';

const ID = /^[0-9a-fA-F-]{36}$/;

export function Overview() {
  const host = useHost();
  const today = host.today;
  const portfolio = useQueryParam('portfolio', ID)?.toLowerCase();
  const scope = portfolio ? { portfolioId: portfolio } : {};
  const portfolios = useTool('list_portfolios', { top: 100 });
  const portfolioItems = dataOf(portfolios)?.items.filter(p => p.isActive) ?? [];
  const portfolioName = portfolio ? portfolioItems.find(p => p.portfolioId.toLowerCase() === portfolio)?.name ?? 'One portfolio' : null;
  const byStage = useTool('get_portfolio_summary', { groupBy: 'stage', includeOwnerGaps: true, top: 50, ...scope });
  const byTier = useTool('get_portfolio_summary', { groupBy: 'tier', ...scope });
  const byKind = useTool('get_portfolio_summary', { groupBy: 'kind', ...scope });
  const approvals = useTool('list_approvals', { view: 'open', top: 1, ...scope });
  const aiWaiting = useTool('get_ai_activity', { view: 'waiting', top: 1, ...scope });
  const exceptions = useTool('get_decision_log', { decisionTypeKeys: ['exception'], statusMeanings: ['proposed', 'accepted'], top: 100, ...scope });
  const pick = (value: string) => navigate(withParams(hrefTo({ page: 'overview' }), { portfolio: value === 'all' ? undefined : value }), true);
  // The ranges are calendar days in the display zone. The tools filter by UTC date, so each call asks for the UTC dates
  // that cover those days, and the page keeps the items of the days it names.
  const flaggedFrom = addDays(today, -30);
  const comingTo = addDays(today, 14);
  const flagged = useTool('get_deployments', { latestOnly: false, flaggedOnly: true, ...utcDayRange(flaggedFrom, today), ...scope });
  const coming = useTool('get_change_schedule', { ...utcDayRange(today, comingTo), ...scope });
  const timelineHref = portfolio && host.open ? withParams(host.href({ page: 'timeline' }), { portfolio }) : host.href({ page: 'timeline' });
  const ownerRoles = useLabels('owner-role');
  const stages = useLabels('lifecycle-stage');
  const tiers = useLabels('tier');
  // Bars in configured order (stages by stage rank, tiers by rank, "not set" last); kinds by count, largest first.
  const ordered = (buckets: Bucket[], labels: Labels) => [...buckets].sort((a, b) => (a.key === null ? 1 : 0) - (b.key === null ? 1 : 0) || labels.rank(a.key) - labels.rank(b.key));

  const summary = dataOf(byStage);
  const total = summary ? summary.buckets.reduce((n, b) => n + b.count, 0) : null;
  const stageEnvelope = byStage.status === 'done' ? byStage.envelope : undefined;

  return (
    <>
      <PageHeader
        eyebrow="Portfolio"
        title="Portfolio overview"
        subtitle={total === null ? portfolioName ?? 'All portfolios' : `${portfolioName ?? 'All portfolios'}. ${plural(total, 'application', 'applications')} in ${portfolioName ? 'it' : 'the register'}.`}
        meta={<DataStamp envelope={stageEnvelope} />}
        actions={(
          <div className="flex items-center gap-2">
            <span className="text-sm text-navy-2" id="overview-portfolio-label">Portfolio</span>
            <Select value={portfolio ?? 'all'} onValueChange={pick}>
              <SelectTrigger className="w-56 narrow:w-full" aria-labelledby="overview-portfolio-label"><SelectValue placeholder="All portfolios" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All portfolios</SelectItem>
                {portfolioItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{p.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
        )}
      />

      {stageEnvelope && !stageEnvelope.ok ? (
        <div className={rowClass}><PanelFailure failure={stageEnvelope} /></div>
      ) : (
        // Five across, three below 1024 px, two below 720 px. The three-column rule is limited to "not narrow" because
        // the medium variant is emitted after the narrow one and would otherwise win on a phone.
        <div
          className="mb-4 grid grid-cols-4 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5"
          role="group"
          aria-label="Key figures"
        >
          <KpiTile className="narrow:col-span-full" label="Applications" value={total} failed={readFailed(byStage)} caption={portfolioName ? `${portfolioName}, every stage` : 'All portfolios, every stage'} onClick={() => host.drill('All applications', portfolio ? { portfolioIds: [portfolio] } : {})} />
          <KpiTile label="Owner gaps" value={summary?.ownerGaps ?? null} failed={readFailed(byStage)} caption="Owner roles with no holder or no deputy" attention={!!summary?.ownerGaps} onClick={() => (host.open ? host.open({ page: 'owners', view: 'gaps', ...(portfolio ? { portfolioId: portfolio } : {}) }) : host.drill('Applications with owner gaps'))} />
          <KpiTile label="Reviews overdue" value={summary?.reviewsOverdue ?? null} failed={readFailed(byStage)} caption="Past their due date" attention={!!summary?.reviewsOverdue} onClick={() => (host.open ? host.open({ page: 'reviews', state: 'overdue', ...(portfolio ? { portfolioId: portfolio } : {}) }) : host.drill('Overdue reviews'))} />
          <KpiTile label="Documents missing or stale" value={summary?.documentsMissingOrStale ?? null} failed={readFailed(byStage)} caption="Required by each app's tier" attention={!!summary?.documentsMissingOrStale} onClick={() => (host.open ? host.open({ page: 'documents', state: 'missing', ...(portfolio ? { portfolioId: portfolio } : {}) }) : host.drill('Missing or stale documents'))} />
          <KpiTile label="Approvals waiting" value={dataOf(approvals)?.totalCount ?? null} failed={readFailed(approvals)} caption={portfolioName ? `${portfolioName}, asked and not answered yet` : 'Asked and not answered yet'} onClick={() => host.open?.({ page: 'approvals', view: 'open', ...(portfolio ? { portfolioId: portfolio } : {}) })} />
          <KpiTile label="AI drafts waiting" value={dataOf(aiWaiting)?.counts.waiting ?? null} failed={readFailed(aiWaiting)} caption="Drafted by an AI client, no person has taken them forward" onClick={() => host.open?.({ page: 'ai-activity', view: 'waiting', ...(portfolio ? { portfolioId: portfolio } : {}) })} />
          <KpiTile label="Exceptions" value={dataOf(exceptions)?.items.length ?? null} failed={readFailed(exceptions)} caption="Standing or waiting for answers" onClick={() => host.open?.({ page: 'decisions', view: 'exceptions', ...(portfolio ? { portfolioId: portfolio } : {}) })} />
          <KpiTile label="Old drafts" value={summary?.draftsOlderThanDays ?? null} failed={readFailed(byStage)} caption={portfolio ? 'About its applications, older than the configured draft age' : 'Older than the configured draft age'} onClick={() => (host.open ? host.open({ page: 'intake', view: 'drafts' }) : host.drill('Drafts past the configured age'))} />
          {host.open && summary?.dataGaps && (
            <KpiTile
              label="Data gaps"
              value={summary.dataGaps.total}
              caption={summary.dataGaps.high ? `${summary.dataGaps.high} high: owners, rules and decisions on the data` : 'Owners, rules and decisions on the data'}
              attention={summary.dataGaps.high > 0}
              onClick={() => host.open?.({ page: 'data', tab: 'gaps' })}
            />
          )}
        </div>
      )}

      <div className={rowClass}>
        <Panel title="Needs attention" subtitle={portfolioName ? `Owner gaps in ${portfolioName}, and its deployments flagged in the last 30 days` : 'Owner gaps across the register, and deployments flagged in the last 30 days'} id="attention">
          <div className="flex flex-col gap-y-0.5">
            <h3 className={groupTitleClass}>Owner gaps</h3>
            <Section state={byStage}>
              {summary?.ownerGapList?.length ? (
                summary.ownerGapList.map((g, i) => (
                  <AttentionItem
                    key={`${g.businessApplicationId}-${g.ownerRoleKey}`}
                    first={i === 0}
                    icon={<UserRoundX className="size-5" aria-hidden />}
                    name={<TextLink href={host.href({ page: 'app', id: g.businessApplicationId })}>{g.name}</TextLink>}
                    aside={g.portfolio ? <Muted>{g.portfolio.name}</Muted> : null}
                    text={`${ownerRoles.label(g.ownerRoleKey)}: ${g.missing === 'holder' ? 'no holder' : 'no deputy'}`}
                    badges={
                      g.holderDirectoryState === 'disabled' || g.holderDirectoryState === 'deleted' ? (
                        <ToneBadge tone="danger">Holder's account is {g.holderDirectoryState}</ToneBadge>
                      ) : null
                    }
                  />
                ))
              ) : (
                <Empty>Every owner role has a holder and a deputy.</Empty>
              )}
              <MoreRowsNote skipToken={summary?.skipToken} what="owner gaps" />
            </Section>
          </div>
          <div className="flex flex-col gap-y-0.5">
            <h3 className={groupTitleClass}>Flagged deployments</h3>
            <Section state={flagged}>
              {(() => {
                const data = dataOf(flagged);
                const items = (data?.items ?? []).filter(d => inDays(d.finishedOn, flaggedFrom, today));
                const more = <MoreRowsNote skipToken={data?.skipToken} what="flagged deployments" />;
                if (!items.length) return <><Empty>No flagged deployments in the last 30 days.</Empty>{more}</>;
                return <>{items.map((d, i) => (
                  <AttentionItem
                    key={d.deploymentRecordId}
                    first={i === 0}
                    icon={<ShieldAlert className="size-5" aria-hidden />}
                    name={
                      d.businessApplicationId
                        ? <TextLink href={host.href({ page: 'app', id: d.businessApplicationId })}>{d.applicationName}</TextLink>
                        : <span>{d.applicationName ?? 'Unknown application'}</span>
                    }
                    aside={<Muted>{fmtDate(d.finishedOn)}</Muted>}
                    text={`${d.artifactName} ${d.artifactVersion} to ${d.targetLabel ?? 'an unknown target'}`}
                    badges={activeFlags(d.flags).map(f => (
                      // The flag's explanation describes the badge (the trigger gets aria-describedby while it shows).
                      <Tooltip key={f}>
                        <TooltipTrigger asChild>
                          <span tabIndex={0} className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring">
                            <ToneBadge tone="warning">{flagText[f]}</ToneBadge>
                          </span>
                        </TooltipTrigger>
                        <TooltipContent sideOffset={4}>{flagHelp[f]}</TooltipContent>
                      </Tooltip>
                    ))}
                  />
                ))}{more}</>;
              })()}
            </Section>
          </div>
        </Panel>

        <Panel
          title="Coming up"
          subtitle={portfolioName ? `Change windows in the next 14 days in ${portfolioName}, read-only` : 'Change windows in the next 14 days, read-only'}
          state={coming}
          skeletonRows={5}
          actions={
            <TextLink href={timelineHref} className="inline-flex items-center gap-x-1 font-semibold">
              Timeline <ArrowRight className="size-4" aria-hidden />
            </TextLink>
          }
        >
          {data => {
            const items = data.items.filter(c => inWindowDays(c.windowStart, c.windowEnd, today, comingTo));
            return (
              <>
                {items.length ? items.map((c, i) => <ComingChange key={c.changeRequestId} change={c} first={i === 0} />) : <Empty>No change windows in the next 14 days.</Empty>}
                <MoreRowsNote skipToken={data.skipToken} what="change windows" />
              </>
            );
          }}
        </Panel>
      </div>
      <div className={rowClass}>
        <Panel title="Applications by stage" subtitle="Lifecycle stages from the configuration in force, in stage order" state={byStage} skeletonRows={8}>
          {data => (
            <BarList
              ariaLabel="Applications by lifecycle stage"
              noun={['application', 'applications']}
              labelWidth={210}
              items={ordered(data.buckets, stages).map(b => ({ key: b.key, label: b.label, count: b.count }))}
              onDrill={b => host.drill(`Applications in stage ${b.label}`, b.key ? { lifecycleStageKeys: [b.key] } : undefined)}
            />
          )}
        </Panel>
        <div className="flex min-w-0 flex-col gap-y-4 narrow:gap-y-3">
          <Panel title="By tier" state={byTier} skeletonRows={4}>
            {data => (
              <BarList
                ariaLabel="Applications by tier"
                noun={['application', 'applications']}
                labelWidth={120}
                items={ordered(data.buckets, tiers).map(b => ({ key: b.key, label: b.key === null ? 'No tier yet' : b.label, count: b.count, muted: b.key === null }))}
                onDrill={b => host.drill(`Applications: ${b.label}`, b.key ? { tierKeys: [b.key] } : undefined)}
              />
            )}
          </Panel>
          <Panel title="By kind" state={byKind} skeletonRows={6}>
            {data => (
              <BarList
                ariaLabel="Applications by kind"
                noun={['application', 'applications']}
                labelWidth={150}
                items={[...data.buckets].sort((a, b) => b.count - a.count || a.label.localeCompare(b.label)).map(b => ({ key: b.key, label: b.label, count: b.count }))}
                onDrill={b => host.drill(`Applications of kind ${b.label}`, b.key ? { applicationKindKeys: [b.key] } : undefined)}
              />
            )}
          </Panel>
        </div>
      </div>

    </>
  );
}

// A part of a panel that has its own data source and fails on its own.
function Section<T>({ state, children }: { state: ToolState<T>; children: ReactNode }) {
  if (state.status === 'loading') return <Muted>Loading.</Muted>;
  if (!state.envelope.ok) return <PanelFailure failure={state.envelope} />;
  return <>{children}</>;
}

function AttentionItem(props: { first: boolean; icon: ReactNode; name: ReactNode; aside: ReactNode; text: string; badges: ReactNode }) {
  return (
    <div className={cn('grid grid-cols-[36px_minmax(0,1fr)] gap-x-3 border-t border-line py-2.5', props.first && 'border-t-0 pt-1')}>
      <span className="grid size-9 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden>{props.icon}</span>
      <div className={itemBodyClass}>
        <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
          <span className={itemNameClass}>{props.name}</span>
          {props.aside}
        </div>
        <span className="text-sm text-navy-2">{props.text}</span>
        {props.badges && <div className={badgesClass}>{props.badges}</div>}
      </div>
    </div>
  );
}

function ComingChange({ change, first }: { change: ScheduledChange; first: boolean }) {
  const start = change.windowStart;
  const authorized = change.authorized;
  // WORKING DEFAULT (open design question 8): a standard change is on the locked list the engine may accept
  // without a card, because a person approved its model once. The page calls it pre-authorized.
  const preAuthorized = authorized && change.changeType.key === 'standard';
  return (
    <div className={cn('grid grid-cols-[52px_minmax(0,1fr)] gap-x-3 border-t border-line py-2.5', first && 'border-t-0 pt-1')}>
      {/* The date chip is drawn for the eye and hidden from screen readers; the sr-only line below reads the date. */}
      <div className="flex h-14 w-[52px] flex-col items-center justify-center rounded-lg bg-secondary leading-[1.1] text-navy" aria-hidden>
        <span className="text-[10px] tracking-[0.05em] text-navy-2 uppercase">{start ? fmtWeekdayOnly(start) : 'TBD'}</span>
        <span className="text-xl font-semibold">{start ? dayOfMonth(start) : '-'}</span>
        <span className="text-[10px] tracking-[0.05em] text-navy-2 uppercase">{start ? fmtMonth(start) : ''}</span>
      </div>
      <div className={itemBodyClass}>
        <span className="sr-only">{start ? `${fmtWeekday(start)}.` : 'No date yet.'}</span>
        <span className={itemNameClass}>{change.title}</span>
        <Muted>
          {change.applicationName} to {change.target?.label ?? 'no target yet'}
          {start && change.windowEnd ? `, ${fmtTimeRange(start, change.windowEnd)} ${zoneLabel()}` : ''}
        </Muted>
        <div className={badgesClass}>
          <ChangeTypeBadge type={change.changeType} />
          {preAuthorized ? (
            <ToneBadge tone="success">Pre-authorized</ToneBadge>
          ) : authorized ? (
            <ToneBadge tone="success">Authorized</ToneBadge>
          ) : (
            <ToneBadge tone="warning">Not authorized yet</ToneBadge>
          )}
        </div>
      </div>
    </div>
  );
}
