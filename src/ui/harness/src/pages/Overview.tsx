// Page 1: Portfolio overview and "needs attention".
// Reads (document 24 names): get_portfolio_summary three times (by stage with the owner gap list, by tier, by kind),
// get_deployments (flagged, last 30 days), get_change_schedule (next 14 days), get_configuration (owner-role labels).
// Every number comes from the server (stored rollups at scale). The page counts nothing itself.
import { Badge, Link, Tooltip, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ArrowRight16Regular, PersonWarning20Regular, ShieldError20Regular } from '@fluentui/react-icons';
import type { ReactNode } from 'react';
import type { ScheduledChange } from '../data/contracts';
import { addDays, dayOfMonth, fmtDate, fmtMonth, fmtTime, fmtWeekdayOnly, plural } from '../data/format';
import { useLabels } from '../data/labels';
import { activeFlags, flagHelp, flagText } from '../data/meanings';
import { dataOf, useTool, type ToolState } from '../data/toolClient';
import { useHost } from '../harness';
import { palette } from '../theme';
import { BarList, DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, ToneBadge, media } from '../components/ui';

const useStyles = makeStyles({
  kpis: {
    display: 'grid',
    gridTemplateColumns: 'repeat(5, minmax(0, 1fr))',
    gap: '12px',
    marginBottom: '16px',
    [media.medium]: { gridTemplateColumns: 'repeat(3, minmax(0, 1fr))' },
    [media.narrow]: { gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: '10px' },
  },
  heroTile: { [media.narrow]: { gridColumn: '1 / -1' } },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 7fr) minmax(0, 5fr)',
    gap: '16px',
    marginBottom: '16px',
    alignItems: 'stretch',
    [media.medium]: { gridTemplateColumns: 'minmax(0, 1fr)' },
    [media.narrow]: { gap: '12px', marginBottom: '12px' },
  },
  stack: { display: 'flex', flexDirection: 'column', rowGap: '16px', minWidth: 0, [media.narrow]: { rowGap: '12px' } },
  group: { display: 'flex', flexDirection: 'column', rowGap: '2px' },
  groupTitle: {
    margin: '4px 0 6px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  },
  item: {
    display: 'grid',
    gridTemplateColumns: '36px minmax(0, 1fr)',
    columnGap: '12px',
    padding: '10px 0',
    borderTop: `1px solid ${palette.line}`,
  },
  firstItem: { borderTop: 'none', paddingTop: '4px' },
  itemIcon: {
    width: '36px',
    height: '36px',
    borderRadius: '50%',
    display: 'grid',
    placeItems: 'center',
    backgroundColor: palette.lightGold,
    color: palette.goldDark,
  },
  itemBody: { display: 'flex', flexDirection: 'column', rowGap: '3px', minWidth: 0 },
  itemTop: { display: 'flex', alignItems: 'baseline', columnGap: '8px', flexWrap: 'wrap', rowGap: '2px' },
  itemName: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300 },
  itemText: { fontSize: tokens.fontSizeBase300, color: palette.navy2 },
  badges: { display: 'flex', flexWrap: 'wrap', gap: '6px', marginTop: '3px' },
  change: {
    display: 'grid',
    gridTemplateColumns: '52px minmax(0, 1fr)',
    columnGap: '12px',
    padding: '10px 0',
    borderTop: `1px solid ${palette.line}`,
  },
  dateChip: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    width: '52px',
    height: '56px',
    borderRadius: '8px',
    backgroundColor: palette.lightBlue,
    color: palette.navy,
    lineHeight: 1.1,
  },
  dateDay: { fontSize: '20px', fontWeight: tokens.fontWeightSemibold },
  dateWeek: { fontSize: tokens.fontSizeBase100, textTransform: 'uppercase', letterSpacing: '0.05em', color: palette.navy2 },
  footerLink: { display: 'inline-flex', alignItems: 'center', columnGap: '4px', fontWeight: tokens.fontWeightSemibold },
});

export function Overview() {
  const s = useStyles();
  const host = useHost();
  const today = host.today;
  const byStage = useTool('get_portfolio_summary', { groupBy: 'stage', includeOwnerGaps: true, top: 50 });
  const byTier = useTool('get_portfolio_summary', { groupBy: 'tier' });
  const byKind = useTool('get_portfolio_summary', { groupBy: 'kind' });
  const flagged = useTool('get_deployments', { latestOnly: false, flaggedOnly: true, from: addDays(today, -30), to: today });
  const coming = useTool('get_change_schedule', { from: today, to: addDays(today, 14) });
  const ownerRoles = useLabels('owner-role');

  const summary = dataOf(byStage);
  const total = summary ? summary.buckets.reduce((n, b) => n + b.count, 0) : null;
  const stageEnvelope = byStage.status === 'done' ? byStage.envelope : undefined;

  return (
    <>
      <PageHeader
        eyebrow="Portfolio"
        title="Portfolio overview"
        subtitle={total === null ? 'All portfolios' : `All portfolios. ${plural(total, 'application', 'applications')} in the register.`}
        meta={<DataStamp envelope={stageEnvelope} />}
      />

      {stageEnvelope && !stageEnvelope.ok ? (
        <div className={s.row}><PanelFailure failure={stageEnvelope} /></div>
      ) : (
        <div className={s.kpis} role="group" aria-label="Key figures">
          <KpiTile className={s.heroTile} label="Applications" value={total ?? 0} caption="All portfolios, every stage" onClick={() => host.drill('All applications')} />
          <KpiTile label="Owner gaps" value={summary?.ownerGaps ?? 0} caption="Owner roles with no holder or no deputy" attention={!!summary?.ownerGaps} onClick={() => host.drill('Applications with owner gaps')} />
          <KpiTile label="Reviews overdue" value={summary?.reviewsOverdue ?? 0} caption="Past their due date" attention={!!summary?.reviewsOverdue} onClick={() => host.drill('Overdue reviews')} />
          <KpiTile label="Documents missing or stale" value={summary?.documentsMissingOrStale ?? 0} caption="Required by each app's tier" attention={!!summary?.documentsMissingOrStale} onClick={() => host.drill('Missing or stale documents')} />
          <KpiTile label="Old drafts" value={summary?.draftsOlderThanDays ?? 0} caption="Older than the configured draft age" onClick={() => host.drill('Drafts past the configured age')} />
        </div>
      )}

      <div className={s.row}>
        <Panel title="Needs attention" subtitle="Owner gaps across the register, and deployments flagged in the last 30 days" id="attention">
          <div className={s.group}>
            <h3 className={s.groupTitle}>Owner gaps</h3>
            <Section state={byStage}>
              {summary?.ownerGapList?.length ? (
                summary.ownerGapList.map((g, i) => (
                  <AttentionItem
                    key={`${g.businessApplicationId}-${g.ownerRoleKey}`}
                    first={i === 0}
                    icon={<PersonWarning20Regular />}
                    name={<Link href={host.href({ page: 'app', id: g.businessApplicationId })}>{g.name}</Link>}
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
            </Section>
          </div>
          <div className={s.group}>
            <h3 className={s.groupTitle}>Flagged deployments</h3>
            <Section state={flagged}>
              {(() => {
                const items = dataOf(flagged)?.items ?? [];
                if (!items.length) return <Empty>No flagged deployments in the last 30 days.</Empty>;
                return items.map((d, i) => (
                  <AttentionItem
                    key={d.deploymentRecordId}
                    first={i === 0}
                    icon={<ShieldError20Regular />}
                    name={
                      d.businessApplicationId
                        ? <Link href={host.href({ page: 'app', id: d.businessApplicationId })}>{d.applicationName}</Link>
                        : <span>{d.applicationName ?? 'Unknown application'}</span>
                    }
                    aside={<Muted>{fmtDate(d.finishedOn)}</Muted>}
                    text={`${d.artifactName} ${d.artifactVersion} to ${d.targetLabel ?? 'an unknown target'}`}
                    badges={activeFlags(d.flags).map(f => (
                      <Tooltip key={f} content={flagHelp[f]} relationship="description" withArrow>
                        <span tabIndex={0}><ToneBadge tone="warning">{flagText[f]}</ToneBadge></span>
                      </Tooltip>
                    ))}
                  />
                ));
              })()}
            </Section>
          </div>
        </Panel>

        <Panel
          title="Coming up"
          subtitle="Change windows in the next 14 days, read-only"
          state={coming}
          skeletonRows={5}
          actions={<Link href={host.href({ page: 'timeline' })} className={s.footerLink}>Timeline <ArrowRight16Regular /></Link>}
        >
          {data => (data.items.length ? data.items.map((c, i) => <ComingChange key={c.changeRequestId} change={c} first={i === 0} />) : <Empty>No change windows in the next 14 days.</Empty>)}
        </Panel>
      </div>
      <div className={s.row}>
        <Panel title="Applications by stage" subtitle="Lifecycle stages from the configuration in force, in stage order" state={byStage} skeletonRows={8}>
          {data => (
            <BarList
              ariaLabel="Applications by lifecycle stage"
              noun={['application', 'applications']}
              labelWidth={210}
              items={data.buckets.map(b => ({ key: b.key, label: b.label, count: b.count }))}
              onDrill={b => host.drill(`Applications in stage ${b.label}`)}
            />
          )}
        </Panel>
        <div className={s.stack}>
          <Panel title="By tier" state={byTier} skeletonRows={4}>
            {data => (
              <BarList
                ariaLabel="Applications by tier"
                noun={['application', 'applications']}
                labelWidth={120}
                items={data.buckets.map(b => ({ key: b.key, label: b.label, count: b.count, muted: b.key === null }))}
                onDrill={b => host.drill(`Applications: ${b.label}`)}
              />
            )}
          </Panel>
          <Panel title="By kind" state={byKind} skeletonRows={6}>
            {data => (
              <BarList
                ariaLabel="Applications by kind"
                noun={['application', 'applications']}
                labelWidth={150}
                items={data.buckets.map(b => ({ key: b.key, label: b.label, count: b.count }))}
                onDrill={b => host.drill(`Applications of kind ${b.label}`)}
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
  const s = useStyles();
  return (
    <div className={mergeClasses(s.item, props.first && s.firstItem)}>
      <span className={s.itemIcon} aria-hidden>{props.icon}</span>
      <div className={s.itemBody}>
        <div className={s.itemTop}>
          <span className={s.itemName}>{props.name}</span>
          {props.aside}
        </div>
        <span className={s.itemText}>{props.text}</span>
        {props.badges && <div className={s.badges}>{props.badges}</div>}
      </div>
    </div>
  );
}

function ComingChange({ change, first }: { change: ScheduledChange; first: boolean }) {
  const s = useStyles();
  const start = change.windowStart;
  const authorized = change.authorized;
  // WORKING DEFAULT (open design question 8): a standard change is on the locked list the engine may accept
  // without a card, because a person approved its model once. The page calls it pre-authorized.
  const preAuthorized = authorized && change.changeType.key === 'standard';
  return (
    <div className={mergeClasses(s.change, first && s.firstItem)}>
      <div className={s.dateChip} aria-hidden>
        <span className={s.dateWeek}>{start ? fmtWeekdayOnly(start) : 'TBD'}</span>
        <span className={s.dateDay}>{start ? dayOfMonth(start) : '-'}</span>
        <span className={s.dateWeek}>{start ? fmtMonth(start) : ''}</span>
      </div>
      <div className={s.itemBody}>
        <span className={s.itemName}>{change.title}</span>
        <Muted>
          {change.applicationName} to {change.target?.label ?? 'no target yet'}
          {start && change.windowEnd ? `, ${fmtTime(start)} to ${fmtTime(change.windowEnd)} UTC` : ''}
        </Muted>
        <div className={s.badges}>
          <Badge appearance="outline" color={change.changeType.key === 'emergency' ? 'danger' : 'informative'} shape="rounded">
            {change.changeType.label}
          </Badge>
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
