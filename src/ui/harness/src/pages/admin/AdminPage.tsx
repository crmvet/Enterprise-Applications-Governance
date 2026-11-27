// Admin (screen "Admin", priority 3; UC-08): run the product. The host settings that are safe to show, the
// configuration in force and its health, the clients that call the product and what each did, the reminders and notices
// the daily check sent, the approval outbox, and the Tool Policy as a readable list. The platform admin, the configuration
// admin and the auditor read it; only the platform admin and the auditor see the notices listed (the configuration admin
// sees their counts).
//   /app/admin                     the last 7 days (?days=30 for 30)
//   ?notice=remind|escalate|overdue|flag   only the notices of that kind (the figures of the daily check)
//   ?outbox=failed|expired|...     only the outbox requests in that state (the outbox bars)
//   ?q=<text>&tools=write|read|person-only|off   searches and narrows the Tool Policy
// Reads: get_admin_status, get_configuration_health, and for the platform admin list_approvals view open (the requests the
// outbox cards stand for). Writes: "Run the daily check now" (run_due_state_refresh, person only, platform admin) in the
// shared side sheet, with the check first (validateOnly); what the run changed stays on the page. Wave 4: "Send again"
// or "Close" on an outbox card that needs a look (retry_approval_card, person only, platform admin).
import { Bot, CalendarClock, CircleAlert, CircleSlash, Inbox, MonitorSmartphone, Play, Send, Siren } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../../app/links';
import { navigate } from '../../app/navigation';
import { ActionSheet } from '../../components/actions';
import { BarList, DataStamp, Empty, KpiTile, LinkButton, Muted, PageHeader, Panel, PanelFailure, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import type { AdminStatus, ApprovalItem, ConfigurationHealth, DueStateRefreshResult, RetryApprovalCardResult } from '../../data/contracts';
import { fmtDate, fmtDateTime, plural, todayInZone } from '../../data/format';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf, type ToolState } from '../../data/toolClient';
import { cn } from '../../lib/utils';
import { LeadIcon } from '../ai-audit/parts';
import { askedWords } from '../demo-admin/logic';
import { Fact, scrollToPanel, twoColumnClass } from '../owners-reviews/people';
import { Sticky, useNow } from '../work-approvals/parts';
import {
  ADMIN_DAYS,
  adminDaysOf,
  CARD_ACTION_TEXT,
  cardActionOf,
  aiLine,
  clientState,
  groupByToolset,
  isOff,
  isPersonOnly,
  NOTICE_TEXT,
  NOTICE_TONE,
  noticesOf,
  OUTBOX_STATES,
  OUTBOX_TEXT,
  outboxStateOf,
  POLICY_FILTER_TEXT,
  POLICY_FILTERS,
  policyMatches,
  refreshLines,
  refreshTotal,
  schemaState,
  shortHash,
  stepUpText,
  storeText,
  SUBJECT_TEXT,
  toolTitle,
  toWhomText,
  whoMayCall,
  type CardAction,
  type ClientItem,
  type Notice,
  type OutboxState,
  type PolicyFilter,
} from './logic';
import { Figure, Note, PageFailure, SearchField, SubHeading, useParamNav } from './parts';

const NOTICE_FILTERS = ['remind', 'escalate', 'overdue', 'flag'] as const;
type NoticeFilter = (typeof NOTICE_FILTERS)[number];
const NOTICE_FILTER_TEXT: Record<NoticeFilter, string> = { remind: 'Reminders', escalate: 'Escalations', overdue: 'Overdue and expired notices', flag: 'Flag notices' };
const NOTICE_NOUN: Record<NoticeFilter, [string, string]> = {
  remind: ['reminder', 'reminders'],
  escalate: ['escalation', 'escalations'],
  overdue: ['overdue or expired notice', 'overdue or expired notices'],
  flag: ['flag notice', 'flag notices'],
};
// The counts get_admin_status gives fold some kinds together: reminders are remind and repeat; overdue is overdue and
// an approval request's expiry.
const NOTICE_KINDS: Record<NoticeFilter, Notice['notice'][]> = { remind: ['remind', 'repeat'], escalate: ['escalate'], overdue: ['overdue', 'expired'], flag: ['flag'] };

const TEXT = /^[^\u0000-\u001f]{1,100}$/;
const kpiRow6 = 'mb-4 grid grid-cols-6 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5';
const factsClass = 'm-0 grid grid-cols-2 gap-x-6 gap-y-3 narrow:grid-cols-1';

export function AdminPage() {
  const session = useSession();
  const days = adminDaysOf(useQueryParam('days', /^(7|30)$/));
  const status = useRead('get_admin_status', { days, includeToolPolicy: true });
  const health = useRead('get_configuration_health', {});
  const data = dataOf(status);
  const go = useParamNav();
  const [lastRun, setLastRun] = useState<DueStateRefreshResult | null>(null);
  const envelope = status.status === 'done' ? status.envelope : undefined;

  const refreshAction = data?.actions.find(a => a.tool === 'run_due_state_refresh') ?? null;
  const canRefresh = session.isPerson && session.can('run_due_state_refresh') && refreshAction?.allowed === true;
  const onRun = (result: DueStateRefreshResult) => {
    setLastRun(result);
    window.setTimeout(() => scrollToPanel('daily-check'), 0);
  };

  const notices = data?.notices;
  const noticeTotal = notices ? notices.reminders + notices.escalations + notices.overdue + notices.flagNotices : null;
  const active = data ? data.clients.filter(c => c.calls > 0 || c.drafts > 0).length : null;
  const refused = data ? data.clients.reduce((n, c) => n + c.refused, 0) : null;
  const outboxIssues = data ? (data.outbox ? data.outbox.counts.failed + data.outbox.counts.expired : 0) : null;

  const header = (
    <PageHeader
      eyebrow="Govern"
      title="Admin"
      subtitle="Run the product: its settings, the rules in force, the clients that call it, the reminders the daily check sent, the approval outbox, and who may call each tool."
      meta={<DataStamp envelope={envelope} />}
      actions={(
        <>
          <SwitchTabs
            label="Activity over"
            ariaLabel="How many days of activity"
            value={String(days)}
            onChange={v => go({ days: v === '7' ? undefined : v })}
            options={ADMIN_DAYS.map(d => ({ value: String(d), label: `${d} days` }))}
          />
          {canRefresh && <RefreshSheet onRun={onRun} />}
        </>
      )}
    />
  );
  if (envelope && !envelope.ok) return <>{header}<PageFailure failure={envelope} readers="The Admin page is for the platform admin, the configuration admin and the auditor." /></>;

  return (
    <>
      {header}

      <div className={kpiRow6} role="group" aria-label="Key figures">
        <KpiTile
          label="Configuration"
          value={data?.configuration.version ?? null}
          caption={data ? `Version in force, published ${fmtDate(data.configuration.publishedOn)}` : 'Version in force'}
          onClick={() => navigate(hrefTo({ page: 'configuration', view: 'versions' }))}
        />
        <KpiTile label="Open drafts" value={data?.configuration.openDrafts ?? null} caption="Configuration changes not in force yet" attention={!!data?.configuration.openDrafts} onClick={() => navigate(hrefTo({ page: 'configuration', view: 'drafts' }))} />
        <KpiTile label="Clients that called" value={active} caption={`Wrote or drafted in the last ${days} days`} onClick={() => scrollToPanel('clients')} />
        <KpiTile label="Refused calls" value={refused} caption="Calls the rules refused, all clients" attention={!!refused} onClick={() => scrollToPanel('clients')} />
        <KpiTile label="Notices sent" value={noticeTotal} caption={`By the daily check, last ${days} days`} onClick={() => { go({ notice: undefined }); scrollToPanel('daily-check'); }} />
        <KpiTile label="Outbox needs a look" value={outboxIssues} caption="Approval requests failed or expired" attention={!!outboxIssues} onClick={() => { go({ outbox: data?.outbox?.counts.failed ? 'failed' : 'expired' }); scrollToPanel('outbox'); }} />
      </div>

      <div className={twoColumnClass}>
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <DailyCheckPanel status={status} lastRun={lastRun} canRefresh={canRefresh} why={refreshAction?.why ?? null} onRun={onRun} />
          <ClientsPanel status={status} days={days} />
          <OutboxPanel status={status} />
        </div>
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <HostPanel status={status} />
          <ConfigurationPanel status={status} health={health} />
          <Panel title="Role bindings">
            <Note>
              The binding check asks Entra whether each role's group exists and who holds a role directly. The platform admin runs
              it on <TextLink href={hrefTo({ page: 'roles' })}>Roles and access</TextLink>.
            </Note>
          </Panel>
        </div>
      </div>
      {/* The longest list last, full width, so on a phone the settings and the activity come first. */}
      <ToolPolicyPanel status={status} />
    </>
  );
}

type StatusState = ToolState<AdminStatus>;

// ---------- the daily check ----------

type RecentRun = NonNullable<AdminStatus['notices']['recentRuns']>[number];

function runCountsText(r: RecentRun): string {
  const parts = [
    r.reviewsOpened ? plural(r.reviewsOpened, 'review opened', 'reviews opened') : '',
    r.reviewsOverdue ? plural(r.reviewsOverdue, 'review marked overdue', 'reviews marked overdue') : '',
    r.documentsStale ? plural(r.documentsStale, 'document turned stale', 'documents turned stale') : '',
    r.notices ? plural(r.notices, 'notice sent', 'notices sent') : '',
  ].filter(Boolean);
  return parts.length ? `${parts.join(', ')}.` : 'nothing was due.';
}

function RefreshSheet({ onRun, label = 'Run the daily check now', variant = 'default' }: { onRun: (r: DueStateRefreshResult) => void; label?: string; variant?: 'default' | 'outline' }) {
  return (
    <ActionSheet
      tool="run_due_state_refresh" site="pages/admin/AdminPage#RefreshSheet"
      title="Run the daily check now"
      description="The daily timer runs this check on a host that turns it on. Run now, it opens each review on its next due day, marks documents past their stale day, expires approval requests past their date, and sends the reminders and escalations that are due. A second run today changes nothing and sends nothing."
      trigger={<Button size="sm" variant={variant}><Play aria-hidden />{label}</Button>}
      input={() => ({})}
      submitLabel="Run it now"
      preview="Check what it would do"
      confirm={{
        title: 'Run the daily check now?',
        body: 'The reminders and escalations that are due go out to the people they name. Every change is written to the audit trail under the engine, with you as the person who asked.',
        confirmLabel: 'Run it now',
      }}
      onDone={data => onRun(data)}
    >
      <p className="m-0 text-sm leading-relaxed text-navy-2">
        Check first to see what it would change, without changing or sending anything.
      </p>
    </ActionSheet>
  );
}

function DailyCheckPanel({ status, lastRun, canRefresh, why, onRun }: { status: StatusState; lastRun: DueStateRefreshResult | null; canRefresh: boolean; why: string | null; onRun: (r: DueStateRefreshResult) => void }) {
  const filter = useQueryParam('notice', /^(remind|escalate|overdue|flag)$/) as NoticeFilter | undefined;
  const go = useParamNav();
  const set = (f: NoticeFilter) => go({ notice: filter === f ? undefined : f });
  return (
    <Panel
      title="The daily check"
      id="daily-check"
      state={status}
      notes={['notice']}
      subtitle="Reviews opened, documents turned stale, approval requests expired, and the reminders and escalations sent."
      actions={canRefresh ? <RefreshSheet onRun={onRun} label="Run it now" variant="outline" /> : undefined}
    >
      {d => {
        const n = d.notices;
        const items = noticesOf(n.items);
        const shown = filter ? items.filter(i => NOTICE_KINDS[filter].includes(i.notice)) : items;
        return (
          <>
            <div className="flex flex-col gap-1 text-sm text-navy">
              <span className="flex items-start gap-1.5">
                <CalendarClock className="mt-0.5 size-4 shrink-0 text-navy-3" aria-hidden />
                <span>{d.host?.dueStateRefresh.schedule ?? 'The host gave no schedule.'}</span>
              </span>
              <Muted>{n.lastRefreshChangeOn ? `It last changed something ${fmtDateTime(n.lastRefreshChangeOn)}.` : 'It has changed nothing yet.'}</Muted>
              {!canRefresh && why && <Muted>{why}</Muted>}
            </div>
            {lastRun && <RunResult result={lastRun} />}
            {(n.recentRuns ?? []).length > 0 && (
              <>
                <SubHeading>Recent runs</SubHeading>
                <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label="Recent runs of the daily check">
                  {(n.recentRuns ?? []).map(r => (
                    <li key={r.ranOn} className="flex flex-col gap-0.5 text-sm text-navy">
                      <span>
                        {`${fmtDateTime(r.ranOn)}, ${r.ranBy ? `run by ${r.ranBy.displayLabel}` : 'by the daily timer'}: `}
                        {runCountsText(r)}
                      </span>
                      {r.reviews.length > 0 && (
                        <span className="flex flex-wrap gap-x-3 gap-y-0.5 text-sm">
                          <span className="text-navy-2">Opened:</span>
                          {r.reviews.map(v => <TextLink key={v.reviewId} href={recordHref(v.recordUrl) ?? undefined}>{v.label}</TextLink>)}
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </>
            )}
            <SubHeading>{`Sent in the last ${n.days} days`}</SubHeading>
            <ul className="m-0 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-sm" aria-label="Notices by kind">
              {NOTICE_FILTERS.map(f => {
                const count = f === 'remind' ? n.reminders : f === 'escalate' ? n.escalations : f === 'overdue' ? n.overdue : n.flagNotices;
                return (
                  <li key={f} className={cn(filter === f && 'font-semibold underline')}>
                    <Figure value={count} label={NOTICE_NOUN[f][count === 1 ? 0 : 1]} attention={f !== 'remind'} onOpen={items.length ? () => set(f) : undefined} />
                  </li>
                );
              })}
            </ul>
            {filter && (
              <p className="m-0 text-xs text-navy-2">
                {`Showing ${NOTICE_FILTER_TEXT[filter].toLowerCase()} only. `}
                <LinkButton className="text-xs" onClick={() => go({ notice: undefined })}>Show every notice</LinkButton>
              </p>
            )}
            {items.length === 0 ? (
              n.reminders + n.escalations + n.overdue + n.flagNotices === 0 ? <Empty>No notice was sent in this window.</Empty> : null
            ) : shown.length === 0 ? <Empty>No notice of this kind in the newest 25.</Empty> : (
              <RowList label="Notices sent">
                {shown.map((i, at) => <NoticeRow key={`${i.rowId}-${i.notice}-${i.reminderNumber ?? 0}-${at}`} notice={i} />)}
              </RowList>
            )}
            {items.length >= 25 && <Note>The newest 25 notices show here. The audit trail holds every one.</Note>}
          </>
        );
      }}
    </Panel>
  );
}

function NoticeRow({ notice: i }: { notice: Notice }) {
  const href = recordHref(i.recordUrl);
  return (
    <Row
      lead={<LeadIcon tone={NOTICE_TONE[i.notice] === 'danger' ? 'danger' : NOTICE_TONE[i.notice] === 'warning' ? 'warning' : 'brand'}>{i.notice === 'flag' ? <Siren /> : <Inbox />}</LeadIcon>}
      primary={href ? <TextLink href={href}>{i.label}</TextLink> : i.label}
      secondary={`${SUBJECT_TEXT[i.subjectKind] ?? 'Record'}${i.applicationName ? `, ${i.applicationName}` : ''}${i.dueOn ? `, due ${fmtDate(i.dueOn)}` : ''}`}
      tertiary={`To ${toWhomText(i.to)}${i.channel ? `, by ${i.channel.label.toLowerCase()}` : ''}`}
      wideEnd
      end={(
        <>
          <ToneBadge tone={NOTICE_TONE[i.notice]}>{NOTICE_TEXT[i.notice]}{i.reminderNumber && i.reminderNumber > 1 ? ` ${i.reminderNumber}` : ''}</ToneBadge>
          {i.escalated && i.notice !== 'escalate' && <ToneBadge tone="warning">Escalated</ToneBadge>}
        </>
      )}
    />
  );
}

// What one run changed: the counts, and the reviews, documents, notices and failures it names.
function RunResult({ result: r }: { result: DueStateRefreshResult }) {
  const lines = refreshLines(r.counts).filter(l => l.count > 0);
  const total = refreshTotal(r.counts);
  const notices = r.notices;
  return (
    <section aria-label="What the run changed" className="flex flex-col gap-2 rounded-lg border border-info-line bg-info-soft/50 p-3">
      <h3 className="m-0 text-sm font-semibold text-navy">{r.dryRun ? 'What the run would change' : 'What the run changed'}</h3>
      {total === 0 && r.counts.failed === 0 ? (
        <p className="m-0 text-sm text-navy-2">Nothing was due. A second run today changes nothing.</p>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-0.5 p-0 text-sm text-navy" aria-label="Counts">
          {lines.map(l => (
            <li key={l.key} className="flex items-baseline gap-2">
              <span className={cn('min-w-6 text-right font-semibold tabular-nums', l.attention && 'text-gold-strong')}>{l.count}</span>
              <span>{l.text}</span>
            </li>
          ))}
        </ul>
      )}
      {r.reviewsOpened.length > 0 && (
        <RunList title="Reviews opened">
          {r.reviewsOpened.map(v => {
            const href = recordHref(v.recordUrl);
            const text = `${v.reviewType.label}, ${v.applicationName}${v.dueOn ? `, due ${fmtDate(v.dueOn)}` : ''}`;
            return <li key={v.reviewId ?? v.fromReviewId}>{href ? <TextLink href={href}>{v.recordNumber ? `${v.recordNumber} ${text}` : text}</TextLink> : text}</li>;
          })}
        </RunList>
      )}
      {r.documentsStale.length > 0 && (
        <RunList title="Documents turned stale">
          {r.documentsStale.map(v => {
            const href = recordHref(v.recordUrl);
            const text = `${v.documentType.label}${v.applicationName ? `, ${v.applicationName}` : ''}${v.staleOn ? `, stale since ${fmtDate(v.staleOn)}` : ''}`;
            return <li key={v.documentLinkId}>{href ? <TextLink href={href}>{text}</TextLink> : text}</li>;
          })}
        </RunList>
      )}
      {notices.length > 0 && (
        <RunList title="Notices sent">
          {notices.slice(0, 25).map((v, at) => {
            const href = recordHref(v.recordUrl);
            return (
              <li key={`${v.rowId}-${v.notice}-${at}`}>
                <span className="font-semibold">{NOTICE_TEXT[v.notice]}: </span>
                {href ? <TextLink href={href}>{v.label}</TextLink> : v.label}
                <span className="text-navy-3">{`, to ${toWhomText(v.to)}`}</span>
              </li>
            );
          })}
        </RunList>
      )}
      {r.failed.length > 0 && (
        <RunList title="Failed, the rest were kept">
          {r.failed.map((f, at) => (
            <li key={`${f.rowId}-${at}`} className="flex items-start gap-1.5">
              <CircleAlert className="mt-0.5 size-3.5 shrink-0 text-danger" aria-hidden />
              <span>{`${f.what}${f.message ? `: ${f.message}` : ''}`}</span>
            </li>
          ))}
        </RunList>
      )}
      {r.more && <Note>More items wait. Run it again to continue.</Note>}
    </section>
  );
}

function RunList({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <SubHeading>{title}</SubHeading>
      <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">{children}</ul>
    </div>
  );
}

// ---------- the host, the engine, the configuration ----------

function HostPanel({ status }: { status: StatusState }) {
  return (
    <Panel title="Host and engine" state={status} notes={[]} subtitle="The settings that are safe to show. The product never shows a key, a connection string or a tenant.">
      {d => {
        const h = d.host;
        const schema = h ? schemaState(h.store) : null;
        return (
          <dl className={factsClass}>
            {h ? (
              <>
                <Fact label="Environment">{h.environmentName}{h.isDemo ? ' (demo install, fictional data)' : ''}</Fact>
                <Fact label="Fresh sign-in to approve">{stepUpText(h.stepUpMode)}</Fact>
                <Fact label="Daily check">{h.dueStateRefresh.enabled ? `On, at ${String(h.dueStateRefresh.hourUtc ?? 0).padStart(2, '0')}:05 UTC` : 'Off: only a run now refreshes'}</Fact>
                <Fact label="Store">{storeText(h.store.kind)}</Fact>
                <Fact label="Store schema">{schema && <ToneBadge tone={schema.tone}>{schema.text}</ToneBadge>}</Fact>
              </>
            ) : (
              <div className="col-span-full"><Note>The host gave no settings, so this part is empty. The rest of the page still works.</Note></div>
            )}
            <Fact label="Engine version">{d.engine.engineVersion}</Fact>
            <Fact label="Snapshot schema">{d.engine.snapshotSchemaVersion}</Fact>
          </dl>
        );
      }}
    </Panel>
  );
}

function ConfigurationPanel({ status, health }: { status: StatusState; health: ToolState<ConfigurationHealth> }) {
  const today = todayInZone();
  const h = dataOf(health);
  return (
    <Panel title="Configuration in force" state={status} notes={[]}>
      {d => {
        const c = d.configuration;
        const reviewLate = c.reviewDueOn !== null && c.reviewDueOn < today;
        return (
          <div className="flex flex-col gap-3">
            <dl className={factsClass}>
              <Fact label="Version"><TextLink href={hrefTo({ page: 'configuration', view: 'versions' })}>{`Version ${c.version}`}</TextLink></Fact>
              <Fact label="Published">{fmtDateTime(c.publishedOn)}</Fact>
              <Fact label="Review due">
                {c.reviewDueOn ? (
                  <span className="flex flex-wrap items-center gap-1.5">
                    {fmtDate(c.reviewDueOn)}
                    {reviewLate && <ToneBadge tone="warning">Overdue</ToneBadge>}
                  </span>
                ) : 'No review day is set'}
              </Fact>
              <Fact label="Open drafts"><TextLink href={hrefTo({ page: 'configuration', view: 'drafts' })}>{plural(c.openDrafts, 'draft', 'drafts')}</TextLink></Fact>
              <Fact label="Fingerprint"><span className="font-mono text-xs">{shortHash(c.contentHash)}</span></Fact>
              <Fact label="Engine snapshot">
                {health.status === 'loading' ? <Muted>Reading</Muted>
                  : !h ? <Muted>Could not be read</Muted>
                    : h.engine.loaded ? <ToneBadge tone="success">Loaded</ToneBadge> : <ToneBadge tone="danger">Not loaded</ToneBadge>}
              </Fact>
            </dl>
            {h && (
              h.findings.length === 0
                ? <Muted>The configuration health check found nothing to fix.</Muted>
                : (
                  <div className="flex flex-col gap-1">
                    <SubHeading>{`Health findings (${h.findings.length})`}</SubHeading>
                    <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm text-navy">
                      {h.findings.map((f, i) => (
                        <li key={`${f.ruleKey}-${i}`} className="flex items-start gap-1.5">
                          <CircleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
                          <span>{f.message}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )
            )}
            {health.status === 'done' && !health.envelope.ok && <PanelFailure failure={health.envelope} />}
          </div>
        );
      }}
    </Panel>
  );
}

// ---------- clients ----------

function ClientsPanel({ status, days }: { status: StatusState; days: number }) {
  return (
    <Panel title="Clients" id="clients" state={status} notes={[]} count={dataOf(status)?.clients.length} subtitle={`Every client that wrote, drafted or was refused in the last ${days} days, and every client on the Known Client list. Reads leave no record, so they are not counted.`}>
      {d => d.clients.length === 0 ? <Empty>No client called in this window, and the Known Client list is empty.</Empty> : (
        <RowList label="Clients">
          {d.clients.map(c => <ClientRow key={c.key ?? c.label} client={c} />)}
        </RowList>
      )}
    </Panel>
  );
}

function ClientRow({ client: c }: { client: ClientItem }) {
  // The product's own web app is no outside client: it needs no Known Client row (QA: "Kind not known, Not on the Known
  // Client list" on the product's own entry).
  const own = c.label === 'Web app' || c.label.startsWith('Web app,');
  const st = own ? { text: 'The product itself', tone: 'success' as const } : clientState(c);
  // AI work opens on the AI activity page, filtered by the client. The web app's own calls have no list of their own yet.
  const ai = c.isAiClient !== false;
  const open = (view: 'all' | 'drafts' | 'refused') => () => navigate(withParams(hrefTo({ page: 'ai-activity', ...(view === 'all' ? {} : { view }) }), { client: c.label }));
  return (
    <Row
      lead={<LeadIcon tone={st.tone === 'success' ? 'brand' : st.tone === 'danger' ? 'danger' : 'warning'}>{c.isAiClient ? <Bot /> : <MonitorSmartphone />}</LeadIcon>}
      primary={c.label}
      secondary={own ? "The product's own web app, where people work" : c.isAiClient === null ? 'Kind not known' : c.isAiClient ? 'AI client' : 'Not an AI client'}
      tertiary={(
        <span className="flex flex-wrap gap-x-3 gap-y-0.5">
          <Figure value={c.calls} label={c.calls === 1 ? 'call' : 'calls'} onOpen={ai ? open('all') : undefined} />
          <Figure value={c.refused} label="refused" attention onOpen={ai ? open('refused') : undefined} />
          <Figure value={c.drafts} label={c.drafts === 1 ? 'draft' : 'drafts'} onOpen={ai ? open('drafts') : undefined} />
          {c.lastCallOn && <span>{`last ${fmtDateTime(c.lastCallOn)}`}</span>}
        </span>
      )}
      end={<ToneBadge tone={st.tone}>{st.text}</ToneBadge>}
    />
  );
}

// ---------- the approval outbox ----------

type AttentionItem = NonNullable<AdminStatus['outbox']>['attention'][number];

// Wave 4 (retry_approval_card, tool 101): each card that needs a look
// offers the platform admin "Send again", or "Close" when its request was settled meanwhile. The open requests
// (list_approvals view open) tell the two apart and name the decision and who is asked; the engine's check in the sheet
// decides in the end. A card handled from this page stays listed, marked, until the person leaves, so the sheet keeps
// the engine's answer in view after the page reads again.
function OutboxPanel({ status }: { status: StatusState }) {
  const filter = outboxStateOf(useQueryParam('outbox', /^[a-z]{1,20}$/));
  const go = useParamNav();
  const session = useSession();
  const box = dataOf(status)?.outbox ?? null;
  const canRetry = session.isPerson && session.can('retry_approval_card') && session.hasRole('platform-admin');
  const open = useRead('list_approvals', canRetry && !!box?.attention.length ? { view: 'open', top: 100 } : null);
  const openData = dataOf(open);
  const requests = new Map((openData?.items ?? []).map(i => [i.approvalRecordId.toLowerCase(), i]));
  const facts = openData ? new Map([...requests].map(([id, i]) => [id, { expiresOn: i.expiresOn, decisionMeaning: i.decision.status.meaning }])) : null;
  const complete = !!openData && !openData.skipToken;
  const [handled, setHandled] = useState<Record<string, { item: AttentionItem; action: CardAction }>>({});
  const now = useNow();
  return (
    <Panel title="Approval outbox" id="outbox" state={status} notes={[]} subtitle="The approval requests the runner sends as cards and links. A person can always answer on the decision page.">
      {d => {
        const outbox = d.outbox;
        if (!outbox) return <Note>This host has no approval outbox, because the approval runner is not set up here. People answer approvals on the web app, from My work and the decision page.</Note>;
        const total = OUTBOX_STATES.reduce((n, s) => n + outbox.counts[s], 0);
        const listed = new Set(outbox.attention.map(a => a.approvalRecordId));
        const kept = Object.values(handled).filter(h => !listed.has(h.item.approvalRecordId)).map(h => h.item);
        const all = [...outbox.attention, ...kept];
        const rows = filter ? all.filter(a => a.state === filter) : all;
        return (
          <div className="flex flex-col gap-3">
            {total === 0 ? <Empty>The outbox has held no request yet.</Empty> : (
              <BarList
                ariaLabel="Approval requests by state"
                noun={['request', 'requests']}
                labelWidth={150}
                items={OUTBOX_STATES.map(s => ({ key: s, label: OUTBOX_TEXT[s].text, count: outbox.counts[s], muted: s === 'answered' || s === 'cancelled' }))}
                onDrill={item => go({ outbox: filter === item.key ? undefined : item.key ?? undefined })}
              />
            )}
            <SubHeading>{filter ? `${OUTBOX_TEXT[filter].text}: ${OUTBOX_TEXT[filter].help}` : 'Requests that need a look'}</SubHeading>
            {rows.length === 0 ? (
              <Empty>
                {filter
                  ? outbox.counts[filter] > 0 ? 'These requests need no attention. Each one shows on its decision page.' : 'No request is in this state.'
                  : 'No request needs a look: nothing failed, and nothing waits too long.'}
              </Empty>
            ) : (
              <RowList label="Requests that need a look">
                {rows.map(a => {
                  const done = handled[a.approvalRecordId];
                  const action = canRetry ? done?.action ?? cardActionOf(a, facts, complete, now) : null;
                  return (
                    <OutboxRow
                      key={a.approvalRecordId}
                      item={a}
                      request={requests.get(a.approvalRecordId.toLowerCase()) ?? null}
                      action={action}
                      done={!!done}
                      onDone={act => setHandled(h => ({ ...h, [a.approvalRecordId]: { item: a, action: act } }))}
                    />
                  );
                })}
              </RowList>
            )}
            {filter && <LinkButton className="self-start text-xs" onClick={() => go({ outbox: undefined })}>Show every request that needs a look</LinkButton>}
            {outbox.attention.length >= 50 && <Note>The 50 oldest requests that need a look show here.</Note>}
            {canRetry && outbox.attention.length > 0 && (
              <Note>
                A failed card, or one sent more than two days ago with no answer, can be sent again: the approval runner sends it
                on its next pull. A card whose request was answered, expired or canceled meanwhile is closed instead.
              </Note>
            )}
          </div>
        );
      }}
    </Panel>
  );
}

function OutboxRow({ item: a, request, action, done, onDone }: {
  item: AttentionItem;
  request: ApprovalItem | null;
  action: CardAction | null;
  done: boolean;
  onDone: (action: CardAction) => void;
}) {
  const href = recordHref(a.recordUrl);
  const state = outboxStateOf(a.state);
  const text = state ? OUTBOX_TEXT[state as OutboxState].text : 'In another state';
  // A settled request is in no open list: its decision is read once, so the row and its button name it (QA walk).
  const settled = useRead('get_decision', !request && a.decisionId ? { decisionId: a.decisionId } : null);
  const statement = request?.decision.statement || dataOf(settled)?.decision?.statement || null;
  const asked = request ? askedWords({ askedPerson: request.askedPrincipal, askedRole: request.askedRole, askedOwnerRole: request.askedOwnerRole, isConsultation: request.isConsultation }) : null;
  return (
    <Row
      primary={href ? <TextLink href={href}>{statement ?? 'Open the decision'}</TextLink> : statement ?? 'An approval request with no decision'}
      secondary={`${asked ? `Asks ${asked}. ` : ''}Queued ${fmtDateTime(a.enqueuedOn)}; last change ${fmtDateTime(a.updatedOn)}`}
      tertiary={plural(a.attempts, 'attempt to send', 'attempts to send')}
      wideEnd
      end={(
        <>
          <ToneBadge tone={a.state === 'failed' || a.state === 'expired' ? 'danger' : 'warning'}>{text}</ToneBadge>
          {done && action && <ToneBadge tone="success">{CARD_ACTION_TEXT[action].done}</ToneBadge>}
        </>
      )}
      endSub={action ? (
        <span className="mt-1 flex justify-end narrow:justify-start">
          <Sticky show={!done}>
            <CardSheet approvalRecordId={a.approvalRecordId} action={action} statement={statement} onDone={result => onDone(result.action === 'closed' ? 'close' : 'send-again')} />
          </Sticky>
        </span>
      ) : undefined}
    />
  );
}

// "Send again" or "Close" for one card, platform admin only, with the engine's check first.
function CardSheet({ approvalRecordId, action, statement, onDone }: { approvalRecordId: string; action: CardAction; statement: string | null; onDone: (result: RetryApprovalCardResult) => void }) {
  const words = CARD_ACTION_TEXT[action];
  return (
    <ActionSheet
      tool="retry_approval_card" site="pages/admin/AdminPage#CardSheet"
      title={words.title}
      description={action === 'close'
        ? 'Its request was answered, expired or canceled while the card waited, so no card is needed any more. Closing it takes it off this list; the decision page keeps the request and its outcome.'
        : 'The card goes back to the approval runner with its attempts reset, and the runner sends it again on its next pull. The person can still answer on the decision page.'}
      trigger={(
        <Button size="sm" variant="outline" aria-label={`${words.button}: ${statement ?? 'the approval card'}`}>
          {action === 'close' ? <CircleSlash aria-hidden /> : <Send aria-hidden />}
          {words.button}
        </Button>
      )}
      input={() => ({ approvalRecordId })}
      submitLabel={words.button}
      preview="Check first"
      confirm={{
        title: action === 'close' ? 'Close this card?' : 'Send this card again?',
        body: 'The audit trail records that you asked, as the platform admin. The engine decides from the request as it stands now, so its answer can differ from the button.',
        confirmLabel: words.button,
      }}
      onDone={data => onDone(data)}
    >
      {statement && (
        <div className="flex flex-col gap-1 rounded-md border border-line bg-background px-3 py-2.5">
          <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">The decision</span>
          <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{statement}</span>
        </div>
      )}
      <p className="m-0 text-sm leading-relaxed text-navy-2">Check first to see what the engine would do, without changing anything.</p>
    </ActionSheet>
  );
}

// ---------- the Tool Policy ----------

const POLICY = new RegExp(`^(${POLICY_FILTERS.join('|')})$`);

function ToolPolicyPanel({ status }: { status: StatusState }) {
  const filter = (useQueryParam('tools', POLICY) ?? 'all') as PolicyFilter;
  const text = useQueryParam('q', TEXT) ?? '';
  const go = useParamNav();
  const roles = useLabels('role');
  const roleLabel = (k: string) => roles.label(k);
  const data = dataOf(status);
  const rows = data ? data.toolPolicy.rows.filter(r => policyMatches(r, filter, text, roleLabel)) : [];
  return (
    <Panel
      title="Tool Policy"
      id="tool-policy"
      state={status}
      notes={[]}
      count={data ? rows.length : undefined}
      subtitle="Who may call each tool, and how far an AI client acting for them may go. The engine still checks every write."
    >
      {d => (
        <>
          <div className="flex flex-wrap items-end gap-3">
            <SearchField className="w-[260px] narrow:w-full" label="Search the tools" placeholder="Data use, platform admin, 42" value={text} onChange={v => go({ q: v.trim() ? v : undefined })} />
            <SwitchTabs
              label="Show"
              ariaLabel="Which tools"
              value={filter}
              onChange={v => go({ tools: v === 'all' ? undefined : v })}
              options={POLICY_FILTERS.map(f => ({ value: f, label: POLICY_FILTER_TEXT[f] }))}
            />
          </div>
          {d.toolPolicy.source === 'shipped-seed' && <Note>These rows are the shipped Tool Policy: the configuration in force holds no Tool Policy rows of its own.</Note>}
          {d.toolPolicy.rows.length === 0 ? <Empty>The Tool Policy was not read.</Empty> : rows.length === 0 ? (
            <Empty>
              No tool matches.{' '}
              <LinkButton onClick={() => go({ q: undefined, tools: undefined })}>Clear the search</LinkButton>
            </Empty>
          ) : (
            <div className="grid grid-cols-2 items-start gap-x-8 gap-y-3 medium:grid-cols-1">
              {groupByToolset(rows).map(g => (
                <div key={g.toolset} className="flex min-w-0 flex-col gap-1">
                  <SubHeading>{`${g.label} (${g.tools.length})`}</SubHeading>
                  <RowList label={`${g.label} tools`}>
                    {g.tools.map(r => {
                      const personOnly = isPersonOnly(r);
                      return (
                        <Row
                          key={r.tool}
                          primary={toolTitle(r.tool)}
                          secondary={whoMayCall(r, roleLabel)}
                          tertiary={`${aiLine({ access: r.access, personOnly, maxAiState: r.maxAiState, aiMaySubmit: r.aiMaySubmit })}${r.callsPerPersonPerHour !== null ? ` At most ${plural(r.callsPerPersonPerHour, 'call', 'calls')} per person per hour.` : ''}`}
                          wideEnd
                          end={(
                            <>
                              {isOff(r) && <ToneBadge tone="danger">{r.isEnabled ? 'Not served here' : 'Turned off'}</ToneBadge>}
                              <ToneBadge tone={r.access === 'write' ? 'brand' : 'subtle'} icon={null}>{r.access === 'write' ? 'Writes' : 'Reads'}</ToneBadge>
                              {personOnly && <ToneBadge tone="informative" icon={null}>A person only</ToneBadge>}
                            </>
                          )}
                          endSub={`Tool ${r.number}, release ${r.release}`}
                        />
                      );
                    })}
                  </RowList>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </Panel>
  );
}
