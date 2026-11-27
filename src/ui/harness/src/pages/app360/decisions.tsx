// Decisions and approvals (UC-04, P02, P03, P04, P08): the approval requests open on the application's decisions, who is
// asked and what counts, and every decision about the application and its records. A person answers an approval on the
// decision page (D-38), so each request links there; a person proposes a decision of any type their decision rights
// allow here. The list view (open or all) is in the address (?decisions=all), so Back keeps it.
// Reads: list_approvals (view open, this application), and the shared decision log.
import { ArrowRight, Plus, Users } from 'lucide-react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../../app/links';
import { navigate, useLocationKey } from '../../app/navigation';
import { Empty, Muted, MoreRowsNote, Panel, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { ApprovalItem } from '../../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { useApp360, type App360Context } from './context';
import { groupApprovals, yesesText } from './logic';
import { ProposeDecisionSheet } from './sheets';

type DecisionView = 'open' | 'all';

export function DecisionsSection() {
  const ctx = useApp360();
  if (!ctx) return null;
  return <DecisionsBody ctx={ctx} />;
}

function DecisionsBody({ ctx }: { ctx: App360Context }) {
  return (
    <div className="grid grid-cols-[minmax(0,6fr)_minmax(0,6fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <ApprovalsPanel ctx={ctx} />
      <DecisionLogPanel ctx={ctx} />
    </div>
  );
}

function askedText(item: ApprovalItem): string {
  if (item.askedPrincipal) return `${item.askedPrincipal.displayLabel}${item.askedOwnerRole ? `, ${item.askedOwnerRole.label.toLowerCase()}` : ''}`;
  if (item.askedOwnerRole) return `The ${item.askedOwnerRole.label.toLowerCase()} of the application`;
  if (item.askedRole) return item.askedRole.label;
  return 'Not named';
}

function countText(item: ApprovalItem): string {
  if (item.isConsultation) return 'Consulted: advice only, never counted.';
  if (item.outcome.meaning === 'pending') return item.otherPendingCount ? `${plural(item.otherPendingCount, 'other request waits', 'other requests wait')} on this decision.` : 'The last request this decision waits on.';
  return item.countsTowardRequirement ? 'Counts toward the rule.' : 'Does not count toward the rule.';
}

function ApprovalsPanel({ ctx }: { ctx: App360Context }) {
  const state = useRead('list_approvals', { view: 'open', businessApplicationId: ctx.applicationId, top: 50 });
  const data = dataOf(state);
  const groups = groupApprovals(data?.items ?? []);
  return (
    <Panel
      title="Approvals waiting"
      subtitle="Who is asked on each open decision, and what counts. Every approval is answered on its decision page."
      state={state}
      count={data?.totalCount}
      skeletonRows={4}
      actions={<TextLink href={hrefTo({ page: 'approvals', view: 'open', applicationId: ctx.applicationId })} className="inline-flex items-center gap-1 font-semibold">Approvals <ArrowRight className="size-4" aria-hidden /></TextLink>}
    >
      {d => (groups.length ? (
        <div className="flex flex-col gap-3">
          {groups.map(g => {
            const href = recordHref(g.recordUrl);
            const mine = g.items.some(i => i.canAnswer);
            return (
              <div key={g.decision.decisionId} className="flex flex-col gap-1.5 rounded-lg border border-line p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{href ? <TextLink href={href}>{g.decision.statement}</TextLink> : g.decision.statement}</span>
                  <StatusBadge status={g.decision.status} />
                  {g.decision.isAiCreated && <AiMark isAiCreated performedBy={null} />}
                </div>
                <span className="text-xs text-navy-3">
                  {g.decision.decisionType.label}
                  {g.subject.subjectKind !== 'business-application' ? `, about ${g.subject.label}` : ''}
                  {g.decision.proposedBy ? `. Proposed by ${g.decision.proposedBy.displayLabel}` : ''}
                </span>
                <RowList label={`Requests on ${g.decision.statement}`}>
                  {g.items.map(i => (
                    <Row
                      key={i.approvalRecordId}
                      lead={<span className="grid size-8 place-items-center rounded-full bg-secondary text-navy-2" aria-hidden><Users className="size-4" /></span>}
                      primary={askedText(i)}
                      secondary={countText(i)}
                      tertiary={i.cannotAnswer
                        ? `You are asked but cannot answer it: ${i.cannotAnswer.message.split('`').join('')}`
                        : [i.requestedOn ? `Asked ${fmtDateTime(i.requestedOn)}` : null, i.expiresOn ? `expires ${fmtDateTime(i.expiresOn)}` : null].filter(Boolean).join(', ') || undefined}
                      badges={(
                        <>
                          {i.isConsultation && <ToneBadge tone="informative">Consulted</ToneBadge>}
                          {i.canAnswer && <ToneBadge tone="brand">You are asked</ToneBadge>}
                        </>
                      )}
                      end={<StatusBadge status={i.outcome} />}
                    />
                  ))}
                </RowList>
                {mine && href && (
                  <div>
                    <Button size="sm" asChild><a href={href}>Answer on the decision page</a></Button>
                  </div>
                )}
              </div>
            );
          })}
          <MoreRowsNote skipToken={d.skipToken} what="approval requests" />
        </div>
      ) : <Empty>No approval request is open on this application's decisions.</Empty>)}
    </Panel>
  );
}

function DecisionLogPanel({ ctx }: { ctx: App360Context }) {
  const view: DecisionView = useQueryParam('decisions', /^(open|all)$/) === 'all' ? 'all' : 'open';
  const location = useLocationKey();
  const setView = (next: DecisionView) => {
    const y = window.scrollY;
    navigate(withParams(location, { decisions: next === 'open' ? undefined : next }));
    window.scrollTo(0, y);
  };
  const all = dataOf(ctx.decisionLog)?.items ?? [];
  const shown = view === 'open' ? all.filter(d => d.status.meaning === 'proposed') : all;
  return (
    <Panel
      title="Decisions"
      subtitle="About the application and its changes, intake requests and reviews. Newest first."
      state={ctx.decisionLog}
      count={shown.length}
      skeletonRows={5}
      actions={ctx.proposable.length ? (
        <ProposeDecisionSheet
          applicationId={ctx.applicationId}
          applicationName={ctx.applicationName}
          types={ctx.proposable}
          trigger={<Button size="sm"><Plus aria-hidden />Propose a decision</Button>}
          onProposed={ctx.onProposed}
        />
      ) : undefined}
    >
      {d => (
        <div className="flex flex-col gap-2.5">
          <SwitchTabs<DecisionView>
            label="Show"
            ariaLabel="Which decisions"
            value={view}
            onChange={setView}
            options={[{ value: 'open', label: 'Waiting' }, { value: 'all', label: 'All' }]}
          />
          {shown.length ? (
            <RowList label="Decisions">
              {shown.map(x => {
                const href = recordHref(x.recordUrl);
                const yeses = x.status.meaning === 'proposed' ? yesesText(x.approvalsSummary) : null;
                return (
                  <Row
                    key={x.decisionId}
                    primary={href ? <TextLink href={href}>{x.statement}</TextLink> : x.statement}
                    secondary={[
                      x.decisionType.label,
                      x.subjectKind !== 'business-application' ? `about ${x.subjectLabel}` : null,
                      x.outcome ? `outcome ${x.outcome.label.toLowerCase()}` : null,
                    ].filter(Boolean).join(', ')}
                    tertiary={yeses ?? (x.decidedOn ? `Decided ${fmtDate(x.decidedOn)}${x.decider ? ` by ${x.decider.displayLabel}` : ''}` : undefined)}
                    badges={<AiMark isAiCreated={x.isAiCreated} performedBy={x.performedBy} />}
                    end={<StatusBadge status={x.status} />}
                  />
                );
              })}
            </RowList>
          ) : <Empty>{view === 'open' ? 'No decision waits.' : 'No decision is recorded yet.'}</Empty>}
          <MoreRowsNote skipToken={d.skipToken} what="decisions" />
          {!ctx.proposable.length && ctx.session.known && <Muted>Your decision rights name no kind of decision you propose about this application.</Muted>}
          <TextLink href={hrefTo({ page: 'decisions', applicationId: ctx.applicationId })} className="inline-flex items-center gap-1 self-start text-sm">Decision log <ArrowRight className="size-3.5" aria-hidden /></TextLink>
        </div>
      )}
    </Panel>
  );
}
