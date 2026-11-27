// Agents (screen "Agents", priority 2): every agent with its autonomy level, security review, go-live readiness and
// owners (playbook 11, D-20), the agents that need attention, and the AI lead's and security reviewer's decisions.
//   /app/agents?view=register|flagged|autonomy, and on the register the page's own need=autonomy|owner|security|go-live
//   (from the figures), so Back returns to the same list.
// Reads: list_agents (tool 93: every agent in one call, with its autonomy level, owners and gaps, last security review,
//   go-live gate state, flags and data uses; page by page, up to 500), get_decision_log (autonomy level and flagged agent
//   decisions, for the decision lists), get_configuration (stage labels and decision rights).
// Writes, each in the shared side sheet: save_decision_proposal (propose an autonomy level; decide on a flagged agent:
//   keep, reassign, consolidate or retire) and request_security_review (tool 94). An action shows only when a decision
//   right of the type names one of the reader's roles or owner roles (or, for the review, when the Tool Policy gives
//   the tool); the engine still decides.
// Design choices (D-41) for AI activity, Agents and Audit, recorded here for wave 3:
//   - "Flagged" means an agent with a flagged-agent decision, plus the agents list_agents flags (no autonomy level, an
//     owner gap, a security review missing where the gate needs one or overdue, a go-live gate not ready before
//     go-live). No reader of Microsoft's agent registry exists yet, so a flag Microsoft raises is recorded as a
//     flagged-agent decision.
//   - The figures count the rows the page lists, from the same answer, so a figure always opens the rows it counts.
//   - "Request a security review" (tool 94) replaces the architecture review request this page offered while no tool
//     opened a security review. The architecture review stays on Application 360.
//   - Wave 4: q= searches the agents by name, number, kind, tier, stage, owner and autonomy level (list_agents takes no
//     words, so the page filters the agents it read). The figures, the tabs and the three views follow the search, so
//     each number still opens the rows it counts.
import { Bot, Database, Flag, Gauge, ShieldAlert, Users } from 'lucide-react';
import { useMemo, useSyncExternalStore, type ReactNode } from 'react';
import { hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { AGENTS_VIEWS, type AgentsView, type RouteOf } from '../app/routes';
import { DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../components/blocks';
import { ListSearch, setListText, useListText } from '../components/listSearch';
import { ExternalLink, Row, RowList } from '../components/rows';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { AgentItem, DecisionLogItem } from '../data/contracts';
import { fmtDateTime, plural } from '../data/format';
import { useLabels, useListValues } from '../data/labels';
import { useConfigRows, usePagedRead, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { filterByText } from '../data/textSearch';
import { dataOf, type ToolState } from '../data/toolClient';
import { AgentDecisionSheet, ProposeAutonomySheet, SecurityReviewSheet, type AgentRef } from './ai-audit/agentSheets';
import {
  agentGaps,
  agentHolder,
  AUTONOMY_TYPE,
  dataUsesText,
  decisionsFor,
  FLAGGED_TYPE,
  GO_LIVE_GATE,
  lifecycleOf,
  mayPropose,
  myAgentOwnerRoles,
  NEED_TEXT,
  needsOfAgent,
  reviewOfAgent,
  type AgentNeed,
  type OwnerGapText,
} from './ai-audit/agentsLogic';
import { LeadIcon, TabCount } from './ai-audit/parts';
import { AiMark } from '../components/stamps';
import { KeepMounted } from './app360/parts';
import { Chip } from './owners-reviews/people';
import { GuidelineMark } from './register-extras/parts';

const NEED = /^(autonomy|owner|security|go-live)$/;
const kpiRowClass6 = 'mb-4 grid grid-cols-6 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5';

const VIEW_TEXT: Record<AgentsView, string> = {
  register: 'Register',
  flagged: 'Flagged',
  autonomy: 'Autonomy decisions',
};

// True from 1024 px: the register shows as a table there, as cards below.
const WIDE = '(min-width: 1024px)';
function useWide(): boolean {
  return useSyncExternalStore(
    cb => {
      const mq = window.matchMedia(WIDE);
      mq.addEventListener('change', cb);
      return () => mq.removeEventListener('change', cb);
    },
    () => window.matchMedia(WIDE).matches,
    () => true,
  );
}

// One agent as the page uses it: the list item, where it stands against go-live, its needs and the decisions about it.
interface AgentRow {
  item: AgentItem;
  lifecycle: 'pre-live' | 'live' | 'retired' | null;
  review: ReturnType<typeof reviewOfAgent>;
  gaps: OwnerGapText[];
  needs: AgentNeed[];
  ownerRoles: string[];
  autonomyWaiting: DecisionLogItem[];
  flagged: DecisionLogItem[];
}

export function Agents({ route }: { route: RouteOf<'agents'> }) {
  const view: AgentsView = route.view ?? 'register';
  const need = useQueryParam('need', NEED) as AgentNeed | undefined;
  const text = useListText();
  const session = useSession();

  // Every agent, in pages of a hundred, up to five pages: the figures and the lists count the same rows.
  const list = usePagedRead('list_agents', { top: 100 }, d => d.items, { maxPages: 5 });
  const agents = list.status === 'done' ? list.items : [];
  const log = useRead('get_decision_log', { decisionTypeKeys: [AUTONOMY_TYPE, FLAGGED_TYPE], top: 100 });
  const logItems = useMemo(() => dataOf(log)?.items ?? [], [log]);

  const stages = useLabels('lifecycle-stage');
  const gates = useLabels('gate');
  const goLiveGate = list.status === 'done' ? list.first.data.goLiveGate?.key ?? GO_LIVE_GATE : GO_LIVE_GATE;
  const goLiveAfter = gates.field<string>(goLiveGate, 'afterStageKey');
  const goLiveAfterRank = goLiveAfter ? stages.field<number>(goLiveAfter, 'stageRank') : undefined;
  const rights = useConfigRows('decision-right');

  const searched = filterByText(agents, text, a => [
    a.name,
    a.recordNumber,
    a.applicationKind?.label,
    a.tier?.label,
    a.lifecycleStage?.label,
    a.autonomy.agreed?.label,
    a.owners.map(o => o.principal?.displayLabel),
  ]);
  const rows: AgentRow[] = searched.map(item => {
    const stageKey = item.lifecycleStage?.key ?? null;
    const lifecycle = stageKey ? lifecycleOf(stages.field(stageKey, 'engineMeaning'), stages.field<number>(stageKey, 'stageRank'), goLiveAfterRank) : null;
    return {
      item,
      lifecycle,
      review: reviewOfAgent(item.securityReview),
      gaps: agentGaps(item),
      needs: needsOfAgent(item, lifecycle),
      ownerRoles: myAgentOwnerRoles(item, session.objectId),
      autonomyWaiting: decisionsFor(logItems, item.businessApplicationId, AUTONOMY_TYPE).filter(d => d.status.meaning === 'proposed'),
      flagged: decisionsFor(logItems, item.businessApplicationId, FLAGGED_TYPE),
    };
  });

  const settled = list.status === 'done';
  const countNeed = (n: AgentNeed) => (settled ? rows.filter(r => r.needs.includes(n)).length : null);
  const autonomyWaiting = log.status === 'done' ? logItems.filter(d => d.decisionType.key === AUTONOMY_TYPE && d.status.meaning === 'proposed').length : null;
  // The flagged view lists the agents that need attention and those with a flagged-agent decision.
  const flaggedCount = settled && log.status === 'done' ? rows.filter(r => r.needs.length > 0 || r.flagged.length > 0).length : null;

  const go = (next: { view?: AgentsView; need?: AgentNeed }) => {
    const v = next.view ?? view;
    navigate(withParams(hrefTo({ page: 'agents', ...(v !== 'register' ? { view: v } : {}) }), { need: v === 'register' ? next.need : undefined }), true);
  };
  const agentRefs: AgentRef[] = agents.map(a => ({ id: a.businessApplicationId, name: a.name }));
  const canProposeFlagged = (ownerRoles: string[]) => session.can('save_decision_proposal') && mayPropose(rights.rows, FLAGGED_TYPE, session.roleKeys, ownerRoles);
  const canProposeAutonomy = (ownerRoles: string[]) => session.can('save_decision_proposal') && mayPropose(rights.rows, AUTONOMY_TYPE, session.roleKeys, ownerRoles);
  const actions: Actions = {
    autonomy: r => canProposeAutonomy(r.ownerRoles) && r.lifecycle !== 'retired' && r.autonomyWaiting.length === 0 && !r.item.autonomy.waiting,
    decide: r => canProposeFlagged(r.ownerRoles),
    // A review already open is answered, not asked for twice (the tool would return the open one).
    review: r => session.can('request_security_review') && r.lifecycle !== 'retired' && r.review.state !== 'due' && r.review.state !== 'overdue',
  };
  const failure = list.status === 'failed' ? list.failure : null;

  return (
    <>
      <PageHeader
        eyebrow="Register"
        title="Agents"
        subtitle="Every agent with its agreed autonomy level, its security review, its go-live gate and its owners. People agree each level on a decision; the AI lead decides on agents that are flagged."
        meta={<DataStamp envelope={list.status === 'done' ? list.first : undefined} />}
        actions={(
          <>
            {canProposeFlagged([]) && agentRefs.length > 0 && <AgentDecisionSheet agents={agentRefs} label="Decide on a flagged agent" />}
          </>
        )}
      />

      {failure ? (
        <PanelFailure failure={failure} />
      ) : (
        <>
          <div className={kpiRowClass6} role="group" aria-label="Key figures">
            <KpiTile label="Agents" value={settled ? rows.length : null} caption={text ? 'Matching the search' : 'Registered or in draft'} onClick={() => go({ view: 'register', need: undefined })} />
            <KpiTile label="No autonomy level" value={countNeed('autonomy')} caption="None agreed yet" attention={!!countNeed('autonomy')} onClick={() => go({ view: 'register', need: 'autonomy' })} />
            <KpiTile label="Security review" value={countNeed('security')} caption="Missing where the gate needs one, or overdue" attention={!!countNeed('security')} onClick={() => go({ view: 'register', need: 'security' })} />
            <KpiTile label="Owner gaps" value={countNeed('owner')} caption="No holder or no deputy" attention={!!countNeed('owner')} onClick={() => go({ view: 'register', need: 'owner' })} />
            <KpiTile label="Go-live gate" value={countNeed('go-live')} caption="Not ready, before go-live" onClick={() => go({ view: 'register', need: 'go-live' })} />
            <KpiTile label="Autonomy decisions" value={autonomyWaiting} caption="Waiting for people to agree" onClick={() => go({ view: 'autonomy' })} />
          </div>

          <div className="mb-4 flex flex-col gap-3">
            <SwitchTabs
              label="Show"
              ariaLabel="Agents view"
              value={view}
              onChange={v => go({ view: v, need: undefined })}
              options={AGENTS_VIEWS.map(v => ({
                value: v,
                label: (
                  <span className="inline-flex items-center gap-1.5">
                    {VIEW_TEXT[v]}
                    {v === 'flagged' && <TabCount value={flaggedCount} noun="agents" />}
                    {v === 'autonomy' && <TabCount value={autonomyWaiting} noun="decisions waiting" />}
                  </span>
                ),
              }))}
            />
            <ListSearch label="Search the agents by name, number, kind, stage, owner or autonomy level" placeholder="Agent, owner or level" />
            {((view === 'register' && need) || text) && (
              <div className="flex flex-wrap items-center gap-2" aria-label="Filters in use">
                {view === 'register' && need && <Chip label={NEED_TEXT[need].filter} onRemove={() => go({ view: 'register', need: undefined })} />}
                {text && <Chip label={`Matching "${text}"`} onRemove={() => setListText(undefined)} />}
              </div>
            )}
          </div>

          {view === 'register' && (
            <RegisterPanel rows={rows} need={need} settled={settled} actions={actions} more={list.status === 'done' && !!list.skipToken} searched={!!text && agents.length > 0} />
          )}
          {view === 'flagged' && (
            <FlaggedView rows={rows} log={log} settled={settled} actions={actions} agentRefs={agentRefs} canDecide={canProposeFlagged([])} />
          )}
          {view === 'autonomy' && <AutonomyView rows={rows} log={log} settled={settled} actions={actions} />}
        </>
      )}
    </>
  );
}

interface Actions {
  autonomy: (r: AgentRow) => boolean;
  decide: (r: AgentRow) => boolean;
  review: (r: AgentRow) => boolean;
}

// ---------- the facts of one agent, as small parts ----------

function AgentName({ row }: { row: AgentRow }) {
  return <TextLink href={hrefTo({ page: 'app', id: row.item.businessApplicationId.toLowerCase() })}>{row.item.name}</TextLink>;
}

function AgentMeta({ row }: { row: AgentRow }) {
  const i = row.item;
  const parts = [i.recordNumber, i.tier?.label, i.lifecycleStage?.label].filter(Boolean).join(', ');
  return (
    <>
      {parts || 'No tier yet'}
      {i.registration && i.registration.meaning !== 'registered' ? `. ${i.registration.label}` : ''}
    </>
  );
}

function AutonomyCell({ row }: { row: AgentRow }) {
  const a = row.item.autonomy;
  const waiting = a.waiting;
  const waitingHref = waiting ? recordHref(waiting.recordUrl) ?? recordHrefOf('decision', waiting.decisionId) : null;
  return (
    <span className="flex flex-col items-start gap-1">
      {a.agreed
        ? <ToneBadge tone="success" icon={<Gauge aria-hidden />}>{a.agreed.label}</ToneBadge>
        : <ToneBadge tone="danger">Not agreed</ToneBadge>}
      {waiting && (
        <span className="text-xs text-navy-3">
          {'Proposal waiting: '}
          {waitingHref ? <TextLink href={waitingHref}>{waiting.proposedLevel?.label ?? waiting.recordNumber ?? 'open it'}</TextLink> : waiting.proposedLevel?.label ?? 'a level'}
        </span>
      )}
      {!a.agreed && a.requiredAtGoLive && <span className="text-xs text-navy-3">Needed before go-live</span>}
    </span>
  );
}

function ReviewCell({ row }: { row: AgentRow }) {
  const r = row.review;
  const href = row.item.securityReview.recordUrl ? recordHref(row.item.securityReview.recordUrl) : r.reviewId ? recordHrefOf('review', r.reviewId.toLowerCase()) : null;
  const badge = <ToneBadge tone={r.tone}>{r.text}</ToneBadge>;
  return href ? <a href={href} className="rounded-md focus-visible:outline-2 focus-visible:outline-ring" aria-label={`Security review: ${r.text}. Open the review.`}>{badge}</a> : badge;
}

function GateCell({ row }: { row: AgentRow }) {
  const g = row.item.goLive;
  if (row.lifecycle === 'live') return <ToneBadge tone="success">Past go-live</ToneBadge>;
  if (row.lifecycle === 'retired') return <ToneBadge tone="subtle">Retired</ToneBadge>;
  if (!g) return <Muted>No go-live gate in the rules in force</Muted>;
  if (g.ready) return <ToneBadge tone="success">{`${g.gate?.label ?? 'Go-live'}: ready`}</ToneBadge>;
  return (
    <span className="flex flex-col items-start gap-1">
      <ToneBadge tone="warning">{`${plural(g.unmet.length, 'requirement', 'requirements')} still needed`}</ToneBadge>
      {g.unmet.map(u => (
        <span key={u.requirementKey} className="flex flex-wrap items-center gap-1 text-xs text-navy-3">
          {u.label}
          {u.guidelineRef && <GuidelineMark guideline={u.guidelineRef} />}
        </span>
      ))}
    </span>
  );
}

function OwnersCell({ row }: { row: AgentRow }) {
  const business = agentHolder(row.item, 'business-owner');
  const technical = agentHolder(row.item, 'technical-owner');
  return (
    <span className="flex flex-col items-start gap-1 text-sm">
      <span className="text-navy-2">
        {business ? `Business: ${business}` : 'No business owner'}
        {technical ? <><br />Technical: {technical}</> : null}
      </span>
      {row.gaps.map(g => <ToneBadge key={`${g.ownerRoleKey}-${g.holderMissing}`} tone={g.holderMissing ? 'danger' : 'warning'}>{g.text}</ToneBadge>)}
      {row.gaps.length > 0 && (
        <TextLink className="text-xs" href={hrefTo({ page: 'owners', view: 'gaps', applicationId: row.item.businessApplicationId.toLowerCase() })}>Fix the gap</TextLink>
      )}
    </span>
  );
}

// The agent's Microsoft resources (up to five), as links when the reader may see them.
function RegistryLinks({ row }: { row: AgentRow }) {
  const links = row.item.components;
  if (!links.length) return null;
  return (
    <span className="flex flex-wrap gap-x-4 gap-y-2 text-xs">
      {links.map((l, i) => {
        const label = l.componentKind ? `${l.componentKind.label}: ${l.label}` : l.label;
        return l.url ? <ExternalLink key={i} href={l.url}>{label}</ExternalLink> : <span key={i} className="text-navy-3">{label}</span>;
      })}
    </span>
  );
}

// The agent's open data uses, linked to the Data page's uses of the application.
function DataUsesLine({ row }: { row: AgentRow }) {
  const text = dataUsesText(row.item.dataUses);
  if (!text) return null;
  return (
    <span className="inline-flex items-center gap-1 text-xs text-navy-3">
      <Database className="size-3.5 shrink-0" aria-hidden />
      <TextLink href={hrefTo({ page: 'data', tab: 'uses', applicationId: row.item.businessApplicationId.toLowerCase() })}>{text}</TextLink>
    </span>
  );
}

// Each row action stays mounted once used, so the engine's answer in its sheet (and its "Open the record" link) stays in
// view after the write, when the row read again no longer offers the action (QA 2026-11-24: proposing an autonomy level
// closed the sheet by itself). A waiting flagged-agent decision is answered, never proposed again.
function RowActions({ row, actions }: { row: AgentRow; actions: Actions }) {
  const ref = { id: row.item.businessApplicationId, name: row.item.name };
  const waitingFlag = row.flagged.find(d => d.status.meaning === 'proposed') ?? null;
  const autonomy = actions.autonomy(row) && !row.item.autonomy.agreed;
  const decide = actions.decide(row) && !waitingFlag && row.needs.length > 0;
  const review = actions.review(row);
  const waitingAutonomy = row.item.autonomy.waiting;
  if (!autonomy && !decide && !review && !waitingFlag && !waitingAutonomy) return null;
  return (
    <span className="flex flex-wrap gap-1.5 print:hidden">
      <KeepMounted show={autonomy}><ProposeAutonomySheet agent={ref} /></KeepMounted>
      {waitingAutonomy && <TextLink className="text-xs" href={recordHref(waitingAutonomy.recordUrl) ?? recordHrefOf('decision', waitingAutonomy.decisionId) ?? '#'}>Open the autonomy decision</TextLink>}
      <KeepMounted show={decide}><AgentDecisionSheet agents={[]} fixed={ref} /></KeepMounted>
      {waitingFlag && <TextLink className="text-xs" href={recordHrefOf('decision', waitingFlag.decisionId) ?? '#'}>Answer the flagged-agent decision</TextLink>}
      <KeepMounted show={review}><SecurityReviewSheet agent={ref} /></KeepMounted>
    </span>
  );
}

function NeedBadges({ row }: { row: AgentRow }) {
  if (!row.needs.length && !row.flagged.length) return null;
  return (
    <>
      {row.flagged.length > 0 && <ToneBadge tone="warning" icon={<Flag aria-hidden />}>Flagged</ToneBadge>}
      {row.needs.map(n => <ToneBadge key={n} tone={n === 'go-live' ? 'warning' : 'danger'}>{NEED_TEXT[n].label}</ToneBadge>)}
    </>
  );
}

// ---------- the register ----------

function RegisterPanel(props: { rows: AgentRow[]; need: AgentNeed | undefined; settled: boolean; actions: Actions; more: boolean; searched: boolean }) {
  const wide = useWide();
  const shown = props.need ? props.rows.filter(r => r.needs.includes(props.need!)) : props.rows;
  const title = props.need ? NEED_TEXT[props.need].filter : 'Agent register';
  return (
    <Panel
      title={title}
      subtitle="Autonomy is the level people agreed on a decision. The go-live gate lists what the rules in force still need for this agent."
      count={props.settled ? shown.length : undefined}
      id="agents-register"
    >
      {!props.settled ? <Muted>Reading the agents.</Muted>
        : !props.rows.length && props.searched ? <Empty>No agent matches this search.</Empty>
        : !props.rows.length ? <Empty>No agent is registered yet. An agent shows here once it is registered with an application kind the autonomy level rule covers.</Empty>
          : !shown.length ? <Empty>{props.need ? NEED_TEXT[props.need].empty : 'No agent.'}</Empty>
            : (
              <>
                {wide ? (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Agent</TableHead>
                        <TableHead>Autonomy</TableHead>
                        <TableHead>Security review</TableHead>
                        <TableHead>Go-live gate</TableHead>
                        <TableHead>Owners</TableHead>
                        <TableHead><span className="sr-only">Actions</span></TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {shown.map(r => (
                        <TableRow key={r.item.businessApplicationId} className="align-top">
                          <TableCell className="max-w-[260px] whitespace-normal">
                            <span className="flex flex-col items-start gap-1">
                              <span className="font-semibold"><AgentName row={r} /></span>
                              <span className="text-xs text-navy-3"><AgentMeta row={r} /></span>
                              <RegistryLinks row={r} />
                              <DataUsesLine row={r} />
                              {r.flagged.length > 0 && <ToneBadge tone="warning" icon={<Flag aria-hidden />}>Flagged</ToneBadge>}
                              {r.item.isAiCreated && <ToneBadge tone="informative" icon={<Bot aria-hidden />}>Drafted by an AI client</ToneBadge>}
                            </span>
                          </TableCell>
                          <TableCell className="max-w-[220px] whitespace-normal"><AutonomyCell row={r} /></TableCell>
                          <TableCell className="whitespace-normal"><ReviewCell row={r} /></TableCell>
                          <TableCell className="max-w-[240px] whitespace-normal"><GateCell row={r} /></TableCell>
                          <TableCell className="whitespace-normal"><OwnersCell row={r} /></TableCell>
                          <TableCell className="w-[1%] whitespace-normal"><span className="flex flex-col items-end gap-1.5 [&>span]:flex-col [&>span]:items-end"><RowActions row={r} actions={props.actions} /></span></TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                ) : (
                  <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label={title}>
                    {shown.map(r => <AgentCard key={r.item.businessApplicationId} row={r} actions={props.actions} />)}
                  </ul>
                )}
                {props.more && <Muted>The product holds more than 500 agents. The first 500 are listed and counted.</Muted>}
              </>
            )}
    </Panel>
  );
}

function AgentCard({ row, actions }: { row: AgentRow; actions: Actions }) {
  const fact = (label: string, value: ReactNode) => (
    <div className="flex min-w-0 flex-col gap-1">
      <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{label}</dt>
      <dd className="m-0 text-sm text-navy [overflow-wrap:anywhere]">{value}</dd>
    </div>
  );
  return (
    <li className="flex flex-col gap-3 rounded-lg border border-line p-3">
      <div className="flex items-start gap-3">
        <LeadIcon tone="brand"><Bot /></LeadIcon>
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-sm font-semibold"><AgentName row={row} /></span>
          <span className="text-xs text-navy-3"><AgentMeta row={row} /></span>
          <RegistryLinks row={row} />
          <DataUsesLine row={row} />
          <span className="flex flex-wrap gap-1"><NeedBadges row={row} /></span>
        </div>
      </div>
      <dl className="m-0 grid grid-cols-2 gap-x-4 gap-y-3 narrow:grid-cols-1">
        {fact('Autonomy', <AutonomyCell row={row} />)}
        {fact('Security review', <ReviewCell row={row} />)}
        {fact('Go-live gate', <GateCell row={row} />)}
        {fact('Owners', <OwnersCell row={row} />)}
      </dl>
      <RowActions row={row} actions={actions} />
    </li>
  );
}

// ---------- flagged ----------

function DecisionRow({ d, agentName }: { d: DecisionLogItem; agentName: string }) {
  const href = recordHref(d.recordUrl);
  const s = d.approvalsSummary;
  return (
    <Row
      lead={<LeadIcon tone={d.decisionType.key === FLAGGED_TYPE ? 'warning' : 'brand'}>{d.decisionType.key === FLAGGED_TYPE ? <Flag /> : <Gauge />}</LeadIcon>}
      primary={(() => {
        const link = href ?? recordHrefOf('decision', d.decisionId);
        return link ? <TextLink href={link}>{d.statement}</TextLink> : d.statement;
      })()}
      secondary={(
        <>
          {agentName}
          {d.outcome ? `. ${d.status.meaning === 'proposed' ? 'Proposed' : 'Outcome'}: ${d.outcome.label}` : ''}
          {d.proposedBy ? `. Proposed by ${d.proposedBy.displayLabel}` : ''}
        </>
      )}
      tertiary={d.status.meaning === 'proposed'
        ? (s.required || s.pending
          ? `${s.approved} of ${s.required} approvals so far${s.pending ? `; ${plural(s.pending, 'request', 'requests')} still open` : ''}`
          : 'Not submitted yet: nobody is asked until its proposer submits it')
        : d.decidedOn ? `Decided ${fmtDateTime(d.decidedOn)}${d.decider ? ` by ${d.decider.displayLabel}` : ''}` : undefined}
      badges={d.isAiCreated || d.performedBy ? <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} /> : undefined}
      end={d.status.meaning === 'proposed' && (s.required || s.pending)
        ? <span className="flex flex-col items-end gap-1"><StatusBadge status={d.status} /><TextLink className="text-xs" href={recordHrefOf('decision', d.decisionId) ?? '#'}>Answer</TextLink></span>
        : <StatusBadge status={d.status} />}
      wideEnd
    />
  );
}

// What list_agents flags on one agent, in its words, each opening the record behind it when there is one.
function FlagLines({ row }: { row: AgentRow }) {
  if (!row.item.flags.length) return null;
  return (
    <ul className="m-0 flex list-none flex-col gap-0.5 p-0">
      {row.item.flags.map((f, i) => {
        const href = f.recordUrl ? recordHref(f.recordUrl) : null;
        return <li key={`${f.kind}-${i}`}>{href ? <TextLink href={href}>{f.label}</TextLink> : f.label}</li>;
      })}
    </ul>
  );
}

function FlaggedView({ rows, log, settled, actions, agentRefs, canDecide }: { rows: AgentRow[]; log: ToolState<unknown>; settled: boolean; actions: Actions; agentRefs: AgentRef[]; canDecide: boolean }) {
  const logData = dataOf(log as ToolState<{ items: DecisionLogItem[] }>);
  const flaggedDecisions = (logData?.items ?? []).filter(d => d.decisionType.key === FLAGGED_TYPE);
  const nameOf = (id: string) => rows.find(r => r.item.businessApplicationId.toLowerCase() === id.toLowerCase())?.item.name;
  const attention = rows.filter(r => r.needs.length > 0);
  return (
    <div className="grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <Panel
        title="Agents that need attention"
        subtitle="Flagged by the product's own records, each with what was found. Choose what happens to each: keep, reassign, consolidate or retire."
        count={settled ? attention.length : undefined}
        id="agents-attention"
      >
        {!settled ? <Muted>Reading the agents.</Muted> : attention.length ? (
          <RowList label="Agents that need attention">
            {attention.map(r => (
              <Row
                key={r.item.businessApplicationId}
                lead={<LeadIcon tone="danger"><ShieldAlert /></LeadIcon>}
                primary={<AgentName row={r} />}
                secondary={<AgentMeta row={r} />}
                tertiary={<FlagLines row={r} />}
                badges={<NeedBadges row={r} />}
                endSub={(() => {
                  // A flagged-agent decision already waiting is answered on its page, never proposed a second time.
                  const waiting = r.flagged.find(d => d.status.meaning === 'proposed');
                  if (waiting) return <span className="mt-1 print:hidden"><TextLink href={recordHrefOf('decision', waiting.decisionId) ?? '#'}>Answer the decision waiting</TextLink></span>;
                  return actions.decide(r) ? <span className="mt-1 print:hidden"><KeepMounted show><AgentDecisionSheet agents={[]} fixed={{ id: r.item.businessApplicationId, name: r.item.name }} /></KeepMounted></span> : undefined;
                })()}
                wideEnd
              />
            ))}
          </RowList>
        ) : <Empty>No agent needs attention: each has an agreed autonomy level, owners with deputies, a security review in date and its gate on track.</Empty>}
      </Panel>
      <Panel
        title="Decisions on flagged agents"
        subtitle="The AI lead decides on each. A flag Microsoft raises is recorded here as a decision, so the answer stays with the agent's record."
        count={logData ? flaggedDecisions.length : undefined}
        state={log}
        actions={canDecide && agentRefs.length ? <AgentDecisionSheet agents={agentRefs} label="Propose a decision on an agent" /> : undefined}
        id="agents-flagged-decisions"
      >
        {() => (flaggedDecisions.length ? (
          <RowList label="Decisions on flagged agents">
            {flaggedDecisions.map(d => <DecisionRow key={d.decisionId} d={d} agentName={nameOf(d.subjectId) ?? d.subjectLabel} />)}
          </RowList>
        ) : <Empty>No decision on a flagged agent yet.</Empty>)}
      </Panel>
    </div>
  );
}

// ---------- autonomy ----------

function AutonomyView({ rows, log, settled, actions }: { rows: AgentRow[]; log: ToolState<unknown>; settled: boolean; actions: Actions }) {
  const levels = useListValues('autonomy-level');
  const logData = dataOf(log as ToolState<{ items: DecisionLogItem[] }>);
  const decisions = (logData?.items ?? []).filter(d => d.decisionType.key === AUTONOMY_TYPE);
  const waiting = decisions.filter(d => d.status.meaning === 'proposed');
  const settledDecisions = decisions.filter(d => d.status.meaning !== 'proposed');
  const none = rows.filter(r => r.needs.includes('autonomy'));
  const nameOf = (id: string) => rows.find(r => r.item.businessApplicationId.toLowerCase() === id.toLowerCase())?.item.name;
  const byLevel = levels.options.map(l => ({ level: l, agents: rows.filter(r => r.item.autonomy.agreed?.key === l.key) }));
  return (
    <div className="grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        <Panel
          title="Autonomy decisions waiting"
          subtitle="The security reviewer agrees each level, and the AI lead is consulted (Balanced). Open one to answer it if you are asked."
          count={logData ? waiting.length : undefined}
          state={log}
          id="agents-autonomy-waiting"
        >
          {() => (waiting.length ? (
            <RowList label="Autonomy decisions waiting">
              {waiting.map(d => <DecisionRow key={d.decisionId} d={d} agentName={nameOf(d.subjectId) ?? d.subjectLabel} />)}
            </RowList>
          ) : <Empty>No autonomy level decision is waiting.</Empty>)}
        </Panel>
        <Panel title="Decided" subtitle="Autonomy level decisions agreed, declined or withdrawn." count={logData ? settledDecisions.length : undefined} state={log} notes={[]}>
          {() => (settledDecisions.length ? (
            <RowList label="Autonomy decisions decided">
              {settledDecisions.map(d => <DecisionRow key={d.decisionId} d={d} agentName={nameOf(d.subjectId) ?? d.subjectLabel} />)}
            </RowList>
          ) : <Empty>No autonomy level decision is decided yet.</Empty>)}
        </Panel>
      </div>
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        <Panel title="No autonomy level yet" subtitle="An agent passes go-live only once people agree its level." count={settled ? none.length : undefined} id="agents-no-autonomy">
          {!settled ? <Muted>Reading the agents.</Muted> : none.length ? (
            <RowList label="Agents with no autonomy level">
              {none.map(r => (
                <Row
                  key={r.item.businessApplicationId}
                  lead={<LeadIcon tone="danger"><Gauge /></LeadIcon>}
                  primary={<AgentName row={r} />}
                  secondary={<AgentMeta row={r} />}
                  tertiary={r.autonomyWaiting.length ? `${plural(r.autonomyWaiting.length, 'proposal', 'proposals')} waiting` : undefined}
                  endSub={actions.autonomy(r) ? <span className="mt-1 print:hidden"><ProposeAutonomySheet agent={{ id: r.item.businessApplicationId, name: r.item.name }} /></span> : undefined}
                  wideEnd
                />
              ))}
            </RowList>
          ) : <Empty>Every agent has an agreed autonomy level.</Empty>}
        </Panel>
        <Panel title="Agreed levels" subtitle="Which agent works at which level." id="agents-levels">
          {!settled ? <Muted>Reading the agents.</Muted> : (
            <dl className="m-0 flex flex-col gap-3">
              {byLevel.map(({ level, agents }) => (
                <div key={level.key} className="flex flex-col gap-1">
                  <dt className="flex items-center gap-2 text-sm font-semibold text-navy">
                    <Gauge className="size-4 text-navy-3" aria-hidden />
                    {level.label}
                    <span className="rounded-full bg-muted px-2 text-xs leading-[18px] text-navy-3">{plural(agents.length, 'agent', 'agents')}</span>
                  </dt>
                  <dd className="m-0 flex flex-wrap gap-x-3 gap-y-1 pl-6 text-sm">
                    {agents.length ? agents.map(r => <AgentName key={r.item.businessApplicationId} row={r} />) : <Muted>None</Muted>}
                  </dd>
                </div>
              ))}
              {!byLevel.length && <Muted>The autonomy level list could not be read.</Muted>}
            </dl>
          )}
        </Panel>
        <Muted className="flex items-start gap-1.5">
          <Users className="mt-px size-3.5 shrink-0" aria-hidden />
          The level is stored on the agent only when people accept the decision with the outcome Agree.
        </Muted>
      </div>
    </div>
  );
}
