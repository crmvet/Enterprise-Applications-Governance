// AI activity (screen "AI activity (the keynote)", priority 1): D-28 made visible. AI clients draft, people decide.
//   /app/ai-activity?view=all|drafts|waiting|accepted|withdrawn|refused|answers|code-approvals&app=<id>&mine=1
//   and the page's own filters: client=<label> (an AI client), person=<objectId> (whom the AI acted for, the estate view
//   only) and, on the drafts view, only=edited|ai-submitted. /app/ai opens it too. Every filter lives in the address, so
//   Back returns to the same list.
// Reads: get_ai_activity, twice under the same scope: once for the counts and groups (by client, person and
//   application) and the Known Client list, once for the rows of the view, page by page. The AI lead, the security
//   reviewer, the platform admin and the auditor see the whole estate; anyone else sees the AI work done for them.
// The story band tells D-28 in three steps (an AI client drafts, a person takes it forward, the rules hold the line), and
// every figure on it opens the rows it counts. The groups open the same list filtered by client, person or application.
// Writes: submit_record, as "Accept and submit", only on a waiting draft made for the reader. Nothing else: there is no
// approve action on this page, by design. Opening a draft leads to its own page, where it can be changed first.
// Wave 3: the clients view (get_ai_activity clients: per AI client its recorded calls, drafts, what a person took
// forward and its refusals, each number opening those rows) and the Tool Policy per role (get_role_access: how far a
// role's AI client may go with each write tool, and what it may submit). The role shown is the page's own
// policyRole=<key>, so Back keeps it.
// Design choices (D-41) for AI activity, Agents and Audit:
//   - The two sub-filters the tool counts but has no view for (changed by a person first, submitted by the AI client)
//     read the drafts view, up to 1,000 rows, and keep the matching ones. The page says so when there are more.
//   - "Accept and submit" is offered here as on My work: the row stays on screen after the write, with the engine's answer.
//   - The clients view replaces the "By AI client" bars when the read carries it: the same counts and more. Wave 4: a
//     client's calls open its entries in the audit trail (client=, get_audit_trail clientApp) for a reader who may read
//     it, else everything it did in the scope. The Tool Policy panel opens on a role the reader holds; the Roles page
//     holds the full grid.
//   - Wave 4: q= searches the rows read (record, client, person, application, refused tool); get_ai_activity takes no
//     words, so the page says when more rows exist than it read. One deployment an AI client both drafted and started
//     shows once, as its code approval row with the draft's line, and the page says how many it folded.
//   - Wave 4b review: portfolio=<id> (the Overview's "AI drafts waiting" tile under a portfolio) narrows both reads to that
//     portfolio's applications (get_ai_activity portfolioId), with a "One portfolio" chip that clears it.
//   - Wave 4b review: the Everything tab counts each activity once (counts.everything). In the drafts view, a deployment an
//     AI client drafted shows as its code approval row reading "Drafted by <client> for <person>, then recorded", with
//     the draft's state, so the drafts the tab counts are the rows it lists.
import { ArrowRight, BookOpen, Bot, Check, CircleSlash, GitPullRequest, Info, MessageSquareQuote, ShieldCheck, ShieldX, X } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { guidelineHref, hrefTo, recordHref, useQueryParam, withParams } from '../app/links';
import { navigate, useLocationKey } from '../app/navigation';
import { AI_ACTIVITY_VIEWS, type RouteOf } from '../app/routes';
import { BarList, DataStamp, Empty, EnvelopeNotes, KpiTile, LinkButton, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../components/blocks';
import { ListSearch, useListText } from '../components/listSearch';
import { Row, RowList } from '../components/rows';
import { Button } from '../components/ui/button';
import { Checkbox } from '../components/ui/checkbox';
import { Label } from '../components/ui/label';
import type { AiActivity as AiActivityData, AiActivityItem, AiActivityView, StampedBy } from '../data/contracts';
import { fmtDateTime, plural } from '../data/format';
import { usePagedRead, useRead } from '../data/reads';
import { useSession, type Session } from '../data/session';
import { filterByText } from '../data/textSearch';
import { dataOf, type ToolState } from '../data/toolClient';
import { cn } from '../lib/utils';
import { FlagBadges } from './changes-records/parts';
import {
  AI_VIEW_TEXT,
  aiStateText,
  answerHeadline,
  canAcceptHere,
  clientSentence,
  clientStanding,
  DRAFT_ONLY,
  DRAFT_ONLY_TEXT,
  draftedLine,
  draftState,
  filterDrafts,
  foldDeploymentDrafts,
  activityText,
  isDraftedCodeApproval,
  itemKey,
  refusalCodeWords,
  refusalHeadline,
  roleToolSummary,
  sameId,
  scopeInput,
  storySteps,
  submitKindsText,
  submittableEntity,
  viewCount,
  type DraftOnly,
  type StoryStep,
  type StoryTarget,
} from './ai-audit/aiActivityLogic';
import { filterBarClass, FilterSelect, LeadIcon, MoreButton, TabCount } from './ai-audit/parts';
import { toolTitle } from './admin/logic';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Chip, twoColumnClass } from './owners-reviews/people';
import { Sticky, SubmitSheet } from './work-approvals/parts';

const PAGE_SIZE = 25;
const ID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const CLIENT = /^[^\u0000-\u001f]{1,200}$/;
const ONLY = /^(edited|ai-submitted)$/;
// The roles that read the whole estate (get_ai_activity, document 26).
const OVERSEERS = ['ai-lead', 'security-reviewer', 'platform-admin', 'auditor'];

interface Filters {
  view: AiActivityView;
  applicationId?: string;
  portfolioId?: string;
  mine?: true;
  client?: string;
  person?: string;
  only?: DraftOnly;
}

export function AiActivity({ route }: { route: RouteOf<'ai-activity'> }) {
  const session = useSession();
  const view: AiActivityView = route.view ?? 'all';
  const client = useQueryParam('client', CLIENT);
  const personParam = useQueryParam('person', ID);
  const onlyParam = useQueryParam('only', ONLY) as DraftOnly | undefined;
  const person = route.mine ? undefined : personParam?.toLowerCase();
  const only = view === 'drafts' ? onlyParam : undefined;
  const filters: Filters = { view, applicationId: route.applicationId, portfolioId: route.portfolioId, mine: route.mine, client, person, only };

  const scope = scopeInput({ mine: route.mine, applicationId: route.applicationId, portfolioId: route.portfolioId, client, person });
  const summary = useRead('get_ai_activity', { ...scope, view: 'all', top: 1 });
  const data = dataOf(summary);
  const estate = data?.scope === 'estate';
  // The PMO reads the estate's figures and only the rows done for them (get_ai_activity countsScope). The page then says
  // which is which, and the tabs, which describe the list, carry no estate counts (wave 3 verification: "Everything 29"
  // stood above "No AI client has done anything here yet").
  const estateFigures = !!data && data.scope === 'mine' && data.countsScope === 'estate';
  const overseer = session.hasRole(...OVERSEERS);

  const go = (next: Partial<Filters>) => {
    const f = { ...filters, ...next };
    const target = {
      page: 'ai-activity' as const,
      ...(f.view !== 'all' ? { view: f.view } : {}),
      ...(f.applicationId ? { applicationId: f.applicationId } : {}),
      ...(f.portfolioId ? { portfolioId: f.portfolioId } : {}),
      ...(f.mine ? { mine: true as const } : {}),
    };
    navigate(withParams(hrefTo(target), { client: f.client, person: f.mine ? undefined : f.person, only: f.view === 'drafts' ? f.only : undefined }), true);
  };
  const drill = (t: StoryTarget) => go({ view: t.view, only: t.only });

  const appName = route.applicationId
    ? data?.byApplication.find(a => sameId(a.businessApplicationId, route.applicationId))?.name ?? 'This application'
    : null;
  const personName = person ? data?.byPerson.find(p => sameId(p.onBehalfOf.objectId, person))?.onBehalfOf.displayLabel ?? 'This person' : null;
  const filtered = !!(route.applicationId || route.portfolioId || client || person || route.mine);
  const clearAll: Partial<Filters> = { applicationId: undefined, portfolioId: undefined, client: undefined, person: undefined, mine: undefined };

  return (
    <>
      <PageHeader
        eyebrow="AI"
        title="AI activity"
        subtitle={(
          <>
            AI clients draft and people decide. What AI clients did{data ? (estate ? ' across the estate' : ' for you') : ''}, who took each
            draft forward, and the calls the rules refused.
          </>
        )}
        meta={(
          <span className="flex flex-col gap-0.5">
            {data && (
              <span>
                {estate
                  ? 'You see the AI work done for everyone, through your role.'
                  : estateFigures
                    ? 'The figures count the AI work across the estate, through your role. The lists show the AI work done for you: the AI lead, the security reviewer, the platform admin and the auditor see every row.'
                    : overseer
                    ? 'You see only the AI work done for you. Clear that filter to see the whole estate.'
                    : 'You see the AI work done for you. The AI lead, the security reviewer, the platform admin and the auditor see the whole estate.'}
              </span>
            )}
            <span><DataStamp envelope={summary.status === 'done' ? summary.envelope : undefined} /></span>
          </span>
        )}
        actions={(
          <>
            <Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'agents' })}>Agents</a></Button>
            {session.can('get_audit_trail') && (
              <Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'audit', actorKinds: ['ai-client-for-person', 'agent-identity'] })}>AI entries in the audit trail</a></Button>
            )}
          </>
        )}
      />

      {summary.status === 'done' && !summary.envelope.ok
        ? <div className="mb-4"><PanelFailure failure={summary.envelope} /></div>
        : <StoryBand counts={data?.counts ?? null} onDrill={drill} active={{ view, only }} estateFigures={estateFigures} />}
      {summary.status === 'done' && summary.envelope.ok && (summary.envelope.couldNotRead.length > 0 || summary.envelope.warnings.length > 0) && (
        <div className="mb-4">
          <EnvelopeNotes couldNotRead={summary.envelope.couldNotRead} warnings={summary.envelope.warnings} />
        </div>
      )}

      <div className="mb-4 flex flex-col gap-3">
        <SwitchTabs
          label="Show"
          ariaLabel="Which AI activity"
          value={view}
          onChange={v => go({ view: v, only: undefined })}
          options={AI_ACTIVITY_VIEWS.map(v => ({
            value: v,
            label: (
              <span className="inline-flex items-center gap-1.5">
                {AI_VIEW_TEXT[v].tab}
                {!estateFigures && <TabCount value={data ? viewCount(data.counts, v) : null} noun="items" />}
              </span>
            ),
          }))}
        />
        {view === 'drafts' && (
          <SwitchTabs
            label="Drafts"
            ariaLabel="Which drafts"
            value={only ?? 'every'}
            onChange={v => go({ only: v === 'every' ? undefined : (v as DraftOnly) })}
            options={[
              { value: 'every', label: <span className="inline-flex items-center gap-1.5">Every draft{!estateFigures && <TabCount value={data?.counts.drafts ?? null} noun="drafts" />}</span> },
              ...DRAFT_ONLY.map(o => ({
                value: o,
                label: (
                  <span className="inline-flex items-center gap-1.5">
                    {DRAFT_ONLY_TEXT[o].tab}
                    {!estateFigures && <TabCount value={data ? (o === 'edited' ? data.counts.editedByPersonFirst : data.counts.submittedByAi) : null} noun="drafts" />}
                  </span>
                ),
              })),
            ]}
          />
        )}
        <FilterBar data={data} filters={filters} go={go} overseer={overseer} estate={estate} />
        {filtered && (
          <div className="flex flex-wrap items-center gap-2" aria-label="Filters in use">
            {route.mine && <Chip label="Only AI work done for me" onRemove={() => go({ mine: undefined })} />}
            {client && <Chip label={`Client: ${client}`} onRemove={() => go({ client: undefined })} />}
            {person && <Chip label={`For: ${personName}`} onRemove={() => go({ person: undefined })} />}
            {route.applicationId && <Chip label={`Application: ${appName}`} onRemove={() => go({ applicationId: undefined })} />}
            {route.portfolioId && <Chip label="One portfolio" onRemove={() => go({ portfolioId: undefined })} />}
            <LinkButton className="text-xs" onClick={() => go(clearAll)}>Clear the filters</LinkButton>
          </div>
        )}
      </div>

      <div className={twoColumnClass}>
        <ActivityList
          key={JSON.stringify(scope)}
          scope={scope}
          view={view}
          only={only}
          session={session}
          filtered={filtered}
          estateFigures={estateFigures}
          onClear={() => go(clearAll)}
        />
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <GroupsPanels state={summary} estate={estate} estateFigures={estateFigures} go={go} />
          <ClientsPanel data={data} state={summary} />
          {session.can('get_role_access') && <ToolPolicyPanel />}
          <RulesPanel />
        </div>
      </div>
    </>
  );
}

// ---------- the story band ----------

function StoryBand({ counts, onDrill, active, estateFigures }: { counts: AiActivityData['counts'] | null; onDrill: (t: StoryTarget) => void; active: StoryTarget; estateFigures: boolean }) {
  const steps: (StoryStep | null)[] = counts ? storySteps(counts) : [null, null, null];
  return (
    <>
      {estateFigures && (
        <p className="mt-0 mb-2 flex items-start gap-1.5 text-sm text-navy-2">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>These figures count the whole estate. Each one opens the AI work done for you only.</span>
        </p>
      )}
      <section aria-label={estateFigures ? 'AI drafts, people decide: the figures across the estate' : 'AI drafts, people decide: the figures'} className="mb-4 grid grid-cols-3 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-1 narrow:gap-2.5">
        {steps.map((s, i) => <StoryColumn key={s?.key ?? i} step={s} index={i} onDrill={onDrill} active={active} />)}
      </section>
    </>
  );
}

const STEP_TITLES = ['An AI client drafts', 'A person takes it forward', 'The rules hold the line'];

function StoryColumn({ step, index, onDrill, active }: { step: StoryStep | null; index: number; onDrill: (t: StoryTarget) => void; active: StoryTarget }) {
  const title = step?.title ?? STEP_TITLES[index];
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex items-center gap-2">
        <span className="grid size-6 shrink-0 place-items-center rounded-full bg-navy text-xs font-bold text-white" aria-hidden>{index + 1}</span>
        <h2 className="m-0 text-sm font-semibold text-navy">
          <span className="sr-only">Step {index + 1}: </span>
          {title}
        </h2>
        {index < 2 && <ArrowRight className="ml-auto size-4 text-navy-4 narrow:hidden" aria-hidden />}
      </div>
      <KpiTile
        label={step?.lead.label ?? 'Reading'}
        value={step ? step.lead.value : null}
        caption={step?.caption ?? 'Reading the figures'}
        attention={!!step?.lead.attention}
        onClick={() => step && onDrill(step.lead.target)}
        className={cn(step && isActive(step.lead.target, active) && 'ring-2 ring-primary')}
      />
      {step && (
        <ul className="m-0 flex list-none flex-col gap-1 p-0 pl-1" aria-label={`More figures for step ${index + 1}`}>
          {step.more.map(f => (
            <li key={f.key} className="flex items-baseline gap-1.5 text-sm">
              <LinkButton
                onClick={() => onDrill(f.target)}
                className={cn('tabular-nums', isActive(f.target, active) && 'underline')}
                aria-label={`${f.value} ${f.label}. Opens the list.`}
                aria-current={isActive(f.target, active) ? 'true' : undefined}
              >
                {f.value}
              </LinkButton>
              <span className={cn('text-navy-2', f.attention && 'font-semibold text-navy')}>{f.label}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const isActive = (t: StoryTarget, active: StoryTarget) => t.view === active.view && (t.only ?? null) === (active.only ?? null);

// ---------- the filter bar ----------

function FilterBar({ data, filters, go, overseer, estate }: { data: AiActivityData | null; filters: Filters; go: (f: Partial<Filters>) => void; overseer: boolean; estate: boolean }) {
  const clientOptions = useMemo(() => {
    const labels = new Map<string, string>();
    for (const k of data?.knownClients ?? []) if (k.isAiClient) labels.set(k.label.toLowerCase(), k.label);
    for (const c of data?.byClient ?? []) if (c.clientApp) labels.set(c.clientApp.toLowerCase(), c.clientApp);
    if (filters.client) labels.set(filters.client.toLowerCase(), filters.client);
    return [...labels.values()].sort((a, b) => a.localeCompare(b)).map(l => ({ value: l, label: l }));
  }, [data, filters.client]);
  const personOptions = useMemo(() => {
    const list = (data?.byPerson ?? []).map(p => ({ value: p.onBehalfOf.objectId.toLowerCase(), label: p.onBehalfOf.displayLabel || 'A person' }));
    if (filters.person && !list.some(p => p.value === filters.person)) list.push({ value: filters.person, label: 'The person chosen' });
    return list;
  }, [data, filters.person]);
  const appOptions = useMemo(() => {
    const list = (data?.byApplication ?? []).map(a => ({ value: a.businessApplicationId.toLowerCase(), label: a.name }));
    if (filters.applicationId && !list.some(a => a.value === filters.applicationId)) list.push({ value: filters.applicationId, label: 'The application chosen' });
    return list;
  }, [data, filters.applicationId]);
  return (
    <div className={filterBarClass} role="group" aria-label="Filters">
      <FilterSelect label="AI client" value={filters.client ?? 'all'} onChange={v => go({ client: v === 'all' ? undefined : v })} allLabel="Every client" options={clientOptions} />
      {(estate || filters.person) && (
        <FilterSelect label="Acting for" value={filters.person ?? 'all'} onChange={v => go({ person: v === 'all' ? undefined : v })} allLabel="Every person" options={personOptions} />
      )}
      <FilterSelect label="Application" value={filters.applicationId ?? 'all'} onChange={v => go({ applicationId: v === 'all' ? undefined : v })} allLabel="Every application" options={appOptions} width="w-[240px]" />
      {overseer && (
        <div className="flex items-center gap-2 pb-2 narrow:w-full narrow:pb-0">
          <Checkbox id="ai-mine-only" checked={!!filters.mine} onCheckedChange={c => go({ mine: c === true ? true : undefined, person: undefined })} />
          <Label htmlFor="ai-mine-only" className="text-sm font-normal text-navy">Only AI work done for me</Label>
        </div>
      )}
    </div>
  );
}

// ---------- the list ----------

function ActivityList(props: {
  scope: ReturnType<typeof scopeInput>;
  view: AiActivityView;
  only: DraftOnly | undefined;
  session: Session;
  filtered: boolean;
  estateFigures: boolean;
  onClear: () => void;
}) {
  const { view, only, session } = props;
  const words = useListText();
  // A sub-filter reads the drafts view whole (up to 1,000 rows) and keeps the matching drafts.
  const list = usePagedRead('get_ai_activity', { ...props.scope, view, top: only ? 100 : PAGE_SIZE }, d => d.items, only ? { maxPages: 10 } : {});
  // Drafts accepted on this page stay in the list with the engine's answer, even when the view no longer holds them.
  const [held, setHeld] = useState<Record<string, AiActivityItem>>({});
  const text = only ? DRAFT_ONLY_TEXT[only] : AI_VIEW_TEXT[view];

  let body: ReactNode;
  let count: number | undefined;
  if (list.status === 'loading') {
    body = <Muted>Reading what AI clients did.</Muted>;
  } else if (list.status === 'failed') {
    body = <PanelFailure failure={list.failure} />;
  } else {
    const rows = filterDrafts(list.items, only);
    const keys = new Set(rows.map(itemKey));
    const kept = Object.values(held).filter(i => !keys.has(itemKey(i)));
    const folded = foldDeploymentDrafts([...kept, ...rows]);
    const all = filterByText(folded.rows, words, r => [...activityText(r.item), ...(r.draft ? activityText(r.draft) : [])]);
    count = words ? all.length : only ? rows.length : list.first.data.totalCount;
    body = all.length ? (
      <>
        <RowList label={text.title}>
          {all.map(({ item, draft }) => (
            <ActivityRow
              key={itemKey(item)}
              item={item}
              draft={draft}
              view={view}
              session={session}
              submittedHere={!!held[itemKey(item)]}
              onSubmitted={() => setHeld(h => ({ ...h, [itemKey(item)]: item }))}
            />
          ))}
        </RowList>
        {folded.folded > 0 && <Muted>{`${plural(folded.folded, 'deployment an AI client drafted and started shows', 'deployments an AI client drafted and started show')} once, with its code approval.`}</Muted>}
        {words && list.skipToken && <Muted>The search covers the rows read so far. Show more to search further.</Muted>}
        {only && list.skipToken && <Muted>Only the first 1,000 drafts were read. Narrow the filters to see the rest.</Muted>}
        {!only && <MoreButton skipToken={list.skipToken} loading={list.loadingMore} onMore={list.more} what="AI activity" laterFailure={list.laterFailure} />}
      </>
    ) : (
      <Empty>
        {words
          ? <>Nothing read so far matches this search.{list.skipToken ? ' More rows exist: show more, or narrow the filters.' : ''} {!only && list.skipToken && <LinkButton onClick={list.more}>Show more</LinkButton>}</>
          : props.filtered
          ? <>Nothing here matches these filters. <LinkButton onClick={props.onClear}>Clear the filters</LinkButton></>
          : props.estateFigures
            ? `${text.empty} This list shows only the AI work done for you; the figures above count the whole estate, whose rows are for the AI lead, the security reviewer, the platform admin and the auditor.`
            : text.empty}
      </Empty>
    );
  }
  return (
    <Panel title={text.title} subtitle={text.subtitle} count={count} id="ai-activity-list">
      <ListSearch label="Search the AI activity by record, client, person or application" placeholder="Record, client, person or application" className="mb-1 w-full" />
      {body}
    </Panel>
  );
}

function ActivityRow({ item, draft, view, session, submittedHere, onSubmitted }: { item: AiActivityItem; draft: AiActivityItem | null; view: AiActivityView; session: Session; submittedHere: boolean; onSubmitted: () => void }) {
  switch (item.kind) {
    case 'draft':
      return <DraftRow item={item} session={session} submittedHere={submittedHere} onSubmitted={onSubmitted} />;
    case 'refusal':
      return <RefusalRow item={item} session={session} />;
    case 'answer':
      return <AnswerRow item={item} session={session} />;
    default:
      return <CodeApprovalRow item={item} draft={draft} view={view} session={session} />;
  }
}

function recordLink(item: AiActivityItem, id?: string): ReactNode {
  if (!item.record) return 'A record no longer found';
  const href = recordHref(item.record.recordUrl);
  return href ? <TextLink id={id} href={href}>{item.record.label}</TextLink> : item.record.label;
}

function AppLine({ item }: { item: AiActivityItem }) {
  if (!item.application) return null;
  return (
    <>
      Application: <TextLink href={hrefTo({ page: 'app', id: item.application.businessApplicationId.toLowerCase() })}>{item.application.name}</TextLink>
    </>
  );
}

// The audit trail of the record, for a reader who may read it.
function AuditLink({ item, session }: { item: AiActivityItem; session: Session }) {
  if (!session.can('get_audit_trail')) return null;
  const href = item.record
    ? hrefTo({ page: 'audit', recordId: item.record.recordId.toLowerCase() })
    : item.onBehalfOf
      ? withParams(hrefTo({ page: 'audit', principalObjectId: item.onBehalfOf.objectId.toLowerCase() }), { op: item.kind === 'refusal' ? 'call-refused' : undefined })
      : null;
  if (!href) return null;
  return (
    <Button asChild size="sm" variant="ghost" className="print:hidden">
      <a href={href} aria-label={`Audit trail: ${item.record?.label ?? 'this call'}`}>Audit trail</a>
    </Button>
  );
}

function DraftRow({ item, session, submittedHere, onSubmitted }: { item: AiActivityItem; session: Session; submittedHere: boolean; onSubmitted: () => void }) {
  const state = draftState(item, session.objectId);
  const linkId = `ai-draft-${item.record?.recordId ?? 'none'}`;
  const kind = submittableEntity(item.record?.entity);
  const accept = canAcceptHere(item, session.objectId, session.can('submit_record'));
  const mine = sameId(item.onBehalfOf?.objectId, session.objectId);
  const openHref = item.record
    ? item.record.entity === 'intake-request' && item.state === 'waiting' && mine
      ? hrefTo({ page: 'intake-edit', id: item.record.recordId.toLowerCase() })
      : recordHref(item.record.recordUrl)
    : null;
  const title = item.record?.label ?? 'An AI draft';
  return (
    <Row
      lead={<LeadIcon tone="brand"><Bot /></LeadIcon>}
      primary={recordLink(item, linkId)}
      secondary={<>{draftedLine(item)}{item.application && <>. <AppLine item={item} /></>}</>}
      tertiary={submittedHere ? 'You accepted and submitted it from this page. The people the rules name are asked now.' : state.detail ?? undefined}
      badges={(
        <>
          <ToneBadge tone={submittedHere ? 'success' : state.tone}>{submittedHere ? 'Accepted by you' : state.label}</ToneBadge>
          {item.editedByPersonFirst && !submittedHere && <ToneBadge tone="informative" icon={<Check aria-hidden />}>Changed by a person first</ToneBadge>}
          {state.note && !submittedHere && <Muted className="w-full">{state.note}</Muted>}
        </>
      )}
      end={item.record?.status ? <StatusBadge status={item.record.status} /> : undefined}
      endSub={(
        <span className="mt-1 flex flex-wrap justify-end gap-1.5 narrow:justify-start">
          {openHref && (
            <Button asChild size="sm" variant="outline">
              <a href={openHref} aria-label={`${mine && item.state === 'waiting' ? 'Open to change' : 'Open'}: ${title}`}>{mine && item.state === 'waiting' ? 'Open to change' : 'Open'}</a>
            </Button>
          )}
          {kind && item.record && (
            <Sticky show={accept && !submittedHere} focusTarget={() => document.getElementById(linkId)}>
              <SubmitSheet subjectKind={kind} subjectId={item.record.recordId} title={title} isAiDraft performedBy={draftStamp(item)} onDone={onSubmitted} />
            </Sticky>
          )}
          <AuditLink item={item} session={session} />
        </span>
      )}
      wideEnd
    />
  );
}

// The draft's stamp as the AI mark reads it ("Drafted by Claude Code for Priya Castell"), from the activity item.
function draftStamp(item: AiActivityItem): StampedBy | null {
  if (!item.onBehalfOf) return null;
  return { actorKind: 'ai-client-for-person', principal: item.onBehalfOf, onBehalfOf: item.onBehalfOf, clientAppLabel: item.clientApp, stampedOn: item.occurredOn };
}

function RefusalRow({ item, session }: { item: AiActivityItem; session: Session }) {
  const r = item.refusal;
  return (
    <Row
      lead={<LeadIcon tone="danger"><ShieldX /></LeadIcon>}
      primary={refusalHeadline(item)}
      secondary={<>{draftedLine(item)}{item.record && <>. About: {recordLink(item)}</>}</>}
      tertiary={r ? <span className="text-navy-2">The rule: {r.rule}</span> : undefined}
      badges={(
        <>
          <ToneBadge tone="danger" icon={<CircleSlash aria-hidden />}>Refused, nothing written</ToneBadge>
          {r && <ToneBadge tone="subtle">{refusalCodeWords(r.code, r.knownTool)}</ToneBadge>}
          {r?.guideline && <RefusalGuidelineChip number={r.guideline} title={r.guidelineTitle ?? null} />}
        </>
      )}
      endSub={<span className="mt-1 flex flex-wrap justify-end gap-1.5 narrow:justify-start"><AuditLink item={item} session={session} /></span>}
      wideEnd
    />
  );
}

// The guideline a refusal applies, as a chip that opens Guidelines at that number (story step 12). No read: the
// activity read carries the number and its title.
function RefusalGuidelineChip({ number, title }: { number: string; title: string | null }) {
  return (
    <a
      href={guidelineHref(number)}
      className="inline-flex items-center gap-1 rounded-full border border-info-line bg-info-soft px-2 py-px text-xs font-semibold text-navy no-underline hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
      onClick={e => { if (!e.metaKey && !e.ctrlKey && !e.shiftKey && e.button === 0) { e.preventDefault(); navigate(guidelineHref(number)); } }}
    >
      <BookOpen className="size-3.5 shrink-0 text-info" aria-hidden />
      {number}{title ? ` ${title}` : ''}
    </a>
  );
}

function AnswerRow({ item, session }: { item: AiActivityItem; session: Session }) {
  const a = item.answer;
  return (
    <Row
      lead={<LeadIcon tone="brand"><MessageSquareQuote /></LeadIcon>}
      primary={recordLink(item)}
      secondary={<>{answerHeadline(item)}. {draftedLine(item)}</>}
      tertiary={a && !a.countsTowardRequirement
        ? 'A recommendation only: it never counts toward the approval. A person answers on the decision page.'
        : 'The engine counted this answer.'}
      badges={(
        <>
          {a && <StatusBadge status={a.outcome} />}
          {a && !a.countsTowardRequirement && <ToneBadge tone="informative" icon={<Info aria-hidden />}>Not counted</ToneBadge>}
          {item.application && <Muted className="w-full"><AppLine item={item} /></Muted>}
        </>
      )}
      endSub={<span className="mt-1 flex flex-wrap justify-end gap-1.5 narrow:justify-start"><AuditLink item={item} session={session} /></span>}
      wideEnd
    />
  );
}

function CodeApprovalRow({ item, draft, view, session }: { item: AiActivityItem; draft: AiActivityItem | null; view: AiActivityView; session: Session }) {
  const d = item.deployment;
  // In the drafts view, a deployment an AI client drafted reads as one of the drafts: its line says it was drafted,
  // then recorded, and it carries the draft's state. The folded draft's own line would only say it again.
  const asDraft = isDraftedCodeApproval(item, view);
  const state = asDraft ? draftState(item, session.objectId) : null;
  return (
    <Row
      lead={<LeadIcon tone="warning"><GitPullRequest /></LeadIcon>}
      primary={recordLink(item)}
      secondary={<>{draftedLine(item, view)}{draft && !asDraft && <>. {draftedLine(draft)}</>}{item.application && <>. <AppLine item={item} /></>}</>}
      tertiary="The only code approval came from an AI. That never counts as authorization, so a person follows the flag up."
      badges={d || state ? (
        <>
          {state && <ToneBadge tone={state.tone}>{state.label}</ToneBadge>}
          {d && <FlagBadges flags={d.flags} />}
        </>
      ) : undefined}
      end={item.record?.status ? <StatusBadge status={item.record.status} /> : undefined}
      endSub={<span className="mt-1 flex flex-wrap justify-end gap-1.5 narrow:justify-start"><AuditLink item={item} session={session} /></span>}
      wideEnd
    />
  );
}

// ---------- the groups ----------

function GroupsPanels({ state, estate, estateFigures, go }: { state: ToolState<AiActivityData>; estate: boolean; estateFigures: boolean; go: (f: Partial<Filters>) => void }) {
  const mayAudit = useSession().can('get_audit_trail');
  const none = estateFigures ? 'No AI client has done anything for you yet.' : 'No AI client has done anything here yet.';
  return (
    <>
      <Panel
        title="AI clients"
        subtitle={estateFigures
          ? 'Per client, for the AI work done for you: the calls it made that wrote or were refused, the rows it drafted, what a person took forward, and its refused calls. Each number opens its rows.'
          : 'Per client, in the scope of the figures: the calls it made that wrote or were refused, the rows it drafted, what a person took forward, and its refused calls. Each number opens its rows.'}
        state={state}
        count={dataOf(state)?.clients?.length}
        notes={[]}
        skeletonRows={3}
        id="ai-activity-clients"
      >
        {d => (d.clients ? (
          d.clients.length ? (
            <RowList label="AI clients">
              {d.clients.map(c => <ClientRow key={c.clientApp} client={c} go={go} mayAudit={mayAudit} />)}
            </RowList>
          ) : <Empty>{none}</Empty>
        ) : d.byClient.length ? (
          <BarList
            ariaLabel="AI activity by client"
            noun={['item', 'items']}
            labelWidth={150}
            items={d.byClient.map(c => ({ key: c.clientApp, label: c.clientApp ?? 'No client recorded', count: c.count, muted: !c.clientApp }))}
            onDrill={b => { if (b.key) go({ client: b.key }); }}
          />
        ) : <Empty>{none}</Empty>)}
      </Panel>
      {estate && (
        <Panel title="Acting for" subtitle="The person each AI client acted for. Choose one to see the work done for them." state={state} notes={[]} skeletonRows={2}>
          {d => (d.byPerson.length ? (
            <BarList
              ariaLabel="AI activity by the person it was done for"
              noun={['item', 'items']}
              labelWidth={150}
              items={d.byPerson.map(p => ({ key: p.onBehalfOf.objectId.toLowerCase(), label: p.onBehalfOf.displayLabel || 'A person', count: p.count }))}
              onDrill={b => { if (b.key) go({ person: b.key }); }}
            />
          ) : <Empty>No AI work was done for anyone here yet.</Empty>)}
        </Panel>
      )}
      <Panel title="By application" subtitle="The applications the AI work was about. Choose one to see it." state={state} notes={[]} skeletonRows={2}>
        {d => (d.byApplication.length ? (
          <BarList
            ariaLabel="AI activity by application"
            noun={['item', 'items']}
            labelWidth={150}
            items={d.byApplication.map(a => ({ key: a.businessApplicationId.toLowerCase(), label: a.name, count: a.count }))}
            onDrill={b => { if (b.key) go({ applicationId: b.key }); }}
          />
        ) : <Empty>No AI work here is about an application yet.</Empty>)}
      </Panel>
    </>
  );
}

// One AI client of the clients view: whether the Known Client list allows it, and its figures, each opening its rows.
function ClientRow({ client: c, go, mayAudit }: { client: NonNullable<AiActivityData['clients']>[number]; go: (f: Partial<Filters>) => void; mayAudit: boolean }) {
  const standing = clientStanding(c);
  // The calls are audit entries: they open the client's entries in the audit trail for a reader who may read it.
  const calls = mayAudit ? (
    <TextLink href={hrefTo({ page: 'audit', clientApp: c.clientApp })} className="inline-flex min-h-6 items-center tabular-nums" aria-label={`${c.calls} ${c.calls === 1 ? 'call' : 'calls'} by ${c.clientApp}. Opens its entries in the audit trail.`}>
      {`${c.calls} ${c.calls === 1 ? 'call' : 'calls'}`}
    </TextLink>
  ) : null;
  const open = (view: AiActivityView, label: string, value: number) => (
    <LinkButton
      onClick={() => go({ client: c.clientApp, view, only: undefined })}
      className="inline-flex min-h-6 items-center tabular-nums"
      aria-label={`${value} ${label} by ${c.clientApp}. Opens the list.`}
    >
      {`${value} ${label}`}
    </LinkButton>
  );
  return (
    <Row
      lead={<LeadIcon tone="brand"><Bot /></LeadIcon>}
      primary={<LinkButton onClick={() => go({ client: c.clientApp })} aria-label={`Show what ${c.clientApp} did`}>{c.clientApp}</LinkButton>}
      secondary={(
        <span className="flex flex-wrap gap-x-2 gap-y-0.5">
          <span className="sr-only">{clientSentence(c)}</span>
          {calls ?? open('all', c.calls === 1 ? 'call' : 'calls', c.calls)}
          {open('drafts', c.drafts === 1 ? 'draft' : 'drafts', c.drafts)}
          {open('accepted', 'taken forward', c.acceptedByPerson)}
          {open('waiting', 'waiting', c.waiting)}
          {open('refused', 'refused', c.refused)}
        </span>
      )}
      tertiary={c.lastActivityOn ? `Last activity ${fmtDateTime(c.lastActivityOn)}` : 'No activity recorded yet'}
      end={<ToneBadge tone={standing.tone} icon={standing.tone === 'danger' ? <X aria-hidden /> : undefined}>{standing.text}</ToneBadge>}
    />
  );
}

// ---------- the Tool Policy per role ----------

const ROLE_PARAM = /^[a-z][a-z0-9-]{0,63}$/;

// How far each role's AI client may go with each write tool, from the Tool Policy in force (get_role_access). The page
// opens on a role the reader holds; the choice is policyRole=<key> in the address.
function ToolPolicyPanel() {
  const access = useRead('get_role_access', { includeHolders: false });
  const chosenParam = useQueryParam('policyRole', ROLE_PARAM);
  const location = useLocationKey();
  const data = dataOf(access);
  const roles = (data?.roles ?? []).filter(r => !r.isWorkloadRole);
  const role = roles.find(r => r.role.key === chosenParam) ?? roles.find(r => r.heldByMe) ?? roles[0] ?? null;
  const summary = role ? roleToolSummary(role.tools) : null;
  const pick = (key: string) => navigate(withParams(location, { policyRole: key }), true);
  // Tool Policy is not a configuration entity get_configuration lists, so the names come from the tool name itself
  // (QA: the label read answered 400 on every load).
  const toolName = (tool: string) => toolTitle(tool);
  return (
    <Panel
      title="Tool Policy per role"
      subtitle="What an AI client acting for someone in this role may do with each write tool. The Tool Policy can only make the rules stricter."
      state={access}
      notes={[]}
      skeletonRows={4}
      actions={role ? <TextLink href={hrefTo({ page: 'roles', roleKey: role.role.key })} className="text-sm">Roles and access</TextLink> : undefined}
      id="ai-activity-tool-policy"
    >
      {d => (!role || !summary ? <Empty>No role is configured.</Empty> : (
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-1">
            <span className="text-xs text-navy-3" id="policy-role-label">Role</span>
            <Select value={role.role.key} onValueChange={pick}>
              <SelectTrigger className="w-full" aria-labelledby="policy-role-label"><SelectValue /></SelectTrigger>
              <SelectContent>
                {roles.map(r => <SelectItem key={r.role.key} value={r.role.key}>{r.heldByMe ? `${r.role.label} (yours)` : r.role.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <p className="m-0 text-sm text-navy-2">
            {`${role.role.label} may call ${plural(summary.reads, 'read tool', 'read tools')} and ${plural(summary.writes.length, 'write tool', 'write tools')}.`}
            {role.isReadOnly ? ' A read-only role.' : ''}
          </p>
          {summary.writes.length ? (
            <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label={`Write tools of ${role.role.label}`}>
              {summary.writes.map(t => {
                const submits = submitKindsText(t.aiMaySubmit);
                return (
                  <li key={t.tool} className="flex flex-col gap-0.5 border-t border-line pt-2 first:border-t-0 first:pt-0">
                    <span className="text-sm font-semibold text-navy">{toolName(t.tool)}</span>
                    <span className="flex flex-wrap items-center gap-1.5 text-xs text-navy-2">
                      <ToneBadge tone={t.personOnly ? 'subtle' : t.maxAiState === 'submitted' ? 'informative' : 'brand'} icon={t.personOnly ? <ShieldCheck aria-hidden /> : <Bot aria-hidden />}>
                        {aiStateText(t.maxAiState, t.personOnly)}
                      </ToneBadge>
                      {submits && !t.personOnly && <span>{`May submit ${submits}.`}</span>}
                    </span>
                  </li>
                );
              })}
            </ul>
          ) : <Empty>This role calls no write tool.</Empty>}
          <Muted>
            {d.toolPolicySource === 'shipped-seed'
              ? 'The shipped Tool Policy applies: the configuration in force holds no Tool Policy rows of its own.'
              : 'From the Tool Policy rows of the configuration in force.'}
          </Muted>
        </div>
      ))}
    </Panel>
  );
}

// ---------- the sanctioned clients ----------

function ClientsPanel({ data, state }: { data: AiActivityData | null; state: ToolState<AiActivityData> }) {
  const unknown = (data?.byClient ?? []).filter(c => c.clientApp && !(data?.knownClients ?? []).some(k => k.label.toLowerCase() === c.clientApp!.toLowerCase()));
  return (
    <Panel
      title="Sanctioned clients"
      subtitle="The Known Client list: the clients the product lets call it, and which of them are AI clients."
      count={data?.knownClients.length}
      state={state}
      notes={[]}
      skeletonRows={2}
    >
      {d => (
        <div className="flex flex-col gap-2">
          {d.knownClients.length ? (
            <RowList label="Known clients">
              {d.knownClients.map(k => (
                <Row
                  key={k.key}
                  lead={<LeadIcon tone={k.isAiClient ? 'brand' : 'subtle'}>{k.isAiClient ? <Bot /> : <ShieldCheck />}</LeadIcon>}
                  primary={k.label}
                  secondary={k.isAiClient ? 'AI client: what it drafts waits for a person' : 'Not an AI client'}
                  end={k.isAllowed
                    ? <ToneBadge tone="success">Allowed</ToneBadge>
                    : <ToneBadge tone="danger" icon={<X aria-hidden />}>Blocked</ToneBadge>}
                />
              ))}
            </RowList>
          ) : <Empty>The Known Client list is empty, so no client is sanctioned by name.</Empty>}
          {unknown.length > 0 && (
            <Muted>
              {plural(unknown.length, 'client', 'clients')} in the activity {unknown.length === 1 ? 'is' : 'are'} not on the list: {unknown.map(c => c.clientApp).join(', ')}.
            </Muted>
          )}
        </div>
      )}
    </Panel>
  );
}

// ---------- the rules ----------

// What the product lets an AI client do, in words: the AI write states of document 24 (section 6.0) and D-28. These are
// engine contract facts, the same in every install; the Tool Policy can only tighten them.
function RulesPanel() {
  const rules: { icon: ReactNode; text: string }[] = [
    { icon: <Bot />, text: 'An AI client acts for a person, through that person\'s own sign-in. The engine stamps the client and the person on every row it writes.' },
    { icon: <Check />, text: 'It may draft, propose, report facts and add links. It submits only where the Tool Policy says so, and a submit is a request, never an approval.' },
    { icon: <CircleSlash />, text: 'No tool an AI client is served approves, accepts or activates anything; the one answer tool is served only to a person in the web app. An AI answer to an approval request is kept as a recommendation and never counts.' },
    { icon: <ShieldCheck />, text: 'An AI client\'s writes go through the same engine and rules as a person\'s, and meet the AI rules too. A refused call writes nothing and stays in the audit trail.' },
  ];
  return (
    <Panel title="The rules AI clients work under" subtitle="The same in every install. Configuration can make them stricter, never looser.">
      <ul className="m-0 flex list-none flex-col gap-2.5 p-0">
        {rules.map(r => (
          <li key={r.text} className="grid grid-cols-[32px_minmax(0,1fr)] items-start gap-3 text-sm leading-5 text-navy-2">
            <LeadIcon>{r.icon}</LeadIcon>
            <span>{r.text}</span>
          </li>
        ))}
      </ul>
    </Panel>
  );
}
