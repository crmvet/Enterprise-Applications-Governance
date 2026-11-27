// The intake request record (/app/records/intake-request/<id>): one request from the draft to the outcome. The request,
// its tier assessments, duplicates, the intake decision with its approvals (an answer where the person is asked, D-38),
// every decision about the request, and its links. Reads: get_intake_request, get_approval_status, list_approvals (the
// person's own open requests, for the answer buttons), check_duplicates, whoami. Writes, each through the shared side
// sheet: submit_record, withdraw_record, answer_tier_questions, save_link, answer_approval.
import { Pencil } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { hrefTo, recordHref } from '../../app/links';
import type { ApprovalItem, IntakeDetail } from '../../data/contracts';
import { fmtDate, fmtDateTime } from '../../data/format';
import { useLabels } from '../../data/labels';
import { dataOf, useTool } from '../../data/toolClient';
import { ApprovalAnswerButtons } from '../../components/actions';
import { DataStamp, Empty, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ExternalLink, Row, RowList } from '../../components/rows';
import { AiMark, StampLine } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Skeleton } from '../../components/ui/skeleton';
import { aiStampOf, approvalsText, intakeProgress, isOpen, isRequesterOf, splitNeed } from './logic';
import { ApprovalStatusView, DuplicateList, Facts, Progress, Prose, RolesUnreadNote, StateBlock, mayCall, meOf, useWhoami } from './shared';
import { LinkSheet, RecordTierSheet, ScoreSheet, SubmitSheet, WithdrawSheet } from './sheets';
import { NewProposalSheet } from '../work-approvals/decisionSheets';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';

// Two columns side by side; one column below 1024 px.
const columns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

export function IntakeRecordPage({ intakeRequestId }: { intakeRequestId: string }) {
  const who = useWhoami();
  const me = meOf(who);
  const state = useTool('get_intake_request', { intakeRequestId });
  const detail = dataOf(state);
  // What the page offered on its first answer stays mounted after a write here changes the status, so the sheet that
  // made the change keeps its answer in view. Its button is then disabled.
  const firstMeaning = useRef<string | null>(null);
  if (detail && firstMeaning.current === null) firstMeaning.current = detail.intake.status.meaning;
  // Wave 4: "Propose priority" opens the submit follow-up right after it is saved, as the data sheets do, in place of
  // moving to the decision page. It stays while the page re-reads and no longer offers the proposal. Wave 4b: 
  // the proposal sheet closes first and hands on the engine's answer, which the follow-up shows on top.
  const priorityFollow = useFollowUp();

  if (state.status === 'loading') {
    return (
      <>
        <PageHeader eyebrow="Intake request" title="Intake request" />
        <div role="status" aria-label="Loading the request" className="flex flex-col gap-3">
          <Skeleton className="h-16 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      </>
    );
  }
  if (!state.envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Intake request" title="The request cannot be shown" meta={<span className="font-mono [overflow-wrap:anywhere]">{intakeRequestId}</span>} />
        <PanelFailure failure={state.envelope} />
      </>
    );
  }
  const envelope = state.envelope;
  const data = envelope.data;
  const intake = data.intake;
  const meaning = intake.status.meaning;
  const requester = isRequesterOf(intake, me?.objectId);
  const first = firstMeaning.current ?? meaning;
  const canEdit = requester && meaning === 'draft' && mayCall(who, 'save_intake_draft');
  const canSubmit = requester && first === 'draft' && mayCall(who, 'submit_record');
  const canWithdraw = requester && isOpen(first) && mayCall(who, 'withdraw_record');
  const decision = intake.triageDecision;
  const onTable = meaning === 'submitted' || meaning === 'in-progress' || meaning === 'completed';
  const canScore = onTable && mayCall(who, 'score_intake_request');
  const canProposePriority = onTable && !intake.priority && mayCall(who, 'save_decision_proposal') && !!me?.roleKeys.includes('pmo');

  return (
    <>
      <PageHeader
        eyebrow={intake.recordNumber ? `Intake request ${intake.recordNumber}` : 'Intake request'}
        title={intake.title || 'Untitled request'}
        subtitle={[
          intake.requester ? `Requested by ${intake.requester.displayLabel || intake.requester.objectId}` : 'No requester recorded',
          intake.proposedKind?.label,
          intake.applicationName ? `about ${intake.applicationName}` : null,
        ].filter(Boolean).join(', ')}
        meta={(
          <span className="flex flex-col gap-1.5">
            <span className="flex flex-wrap items-center gap-2">
              <StatusBadge status={intake.status} />
              <AiMark isAiCreated={intake.isAiCreated} performedBy={aiStampOf(intake.performedBy)} />
              {intake.usesAi === true && <ToneBadge tone="informative">Uses AI</ToneBadge>}
            </span>
            <StampLine performedBy={intake.performedBy} />
            <span><DataStamp envelope={envelope} /></span>
          </span>
        )}
        actions={canEdit || canWithdraw || canSubmit || canScore || canProposePriority || priorityFollow.data ? (
          <>
            {canScore && <ScoreSheet intakeRequestId={intake.intakeRequestId} title={intake.title} score={intake.score} sizeBandKey={intake.sizeBand?.key ?? null} horizonKey={intake.horizon?.key ?? null} size="default" />}
            {canProposePriority && (
              <NewProposalSheet
                decisionTypeKey="priority"
                typeLabel="Priority"
                subjectKind="intake-request"
                subjectId={intake.intakeRequestId}
                subjectLabel={intake.title}
                triggerLabel="Propose priority"
                onProposed={(decisionId, answer) => priorityFollow.start({ decisionId, answer })}
              />
            )}
            {priorityFollow.data && (
              <SubmitFollowUp decisionId={priorityFollow.data.decisionId} previous={priorityFollow.data.answer} subject={`The priority decision for ${intake.title}`} open={priorityFollow.open} onOpenChange={priorityFollow.onOpenChange} />
            )}
            {canEdit && (
              <Button asChild variant="outline">
                <a href={hrefTo({ page: 'intake-edit', id: intake.intakeRequestId })}><Pencil aria-hidden />Edit</a>
              </Button>
            )}
            {canWithdraw && <WithdrawSheet intakeRequestId={intake.intakeRequestId} title={intake.title} draft={meaning === 'draft'} disabled={!isOpen(meaning)} />}
            {canSubmit && <SubmitSheet intakeRequestId={intake.intakeRequestId} disabled={meaning !== 'draft'} aiDraft={intake.isAiCreated} />}
          </>
        ) : undefined}
      />

      <div className="mb-4">
        <Progress
          label="Where the request is"
          steps={intakeProgress(meaning, decision ? decision.status : null, decision?.outcome?.label ?? null)}
        />
      </div>
      {who.status === 'done' && !who.me && <div className="mb-4"><RolesUnreadNote state={who} /></div>}

      <div className={columns}>
        <div className={stack}>
          <RequestPanel data={data} />
          <TierPanel data={data} canAnswer={(requester || !!me?.roleKeys.includes('platform-admin')) && mayCall(who, 'answer_tier_questions') && meaning === 'draft'} />
          <DuplicatesPanel data={data} />
          <LinksPanel data={data} canLink={mayCall(who, 'save_link')} />
        </div>
        <div className={stack}>
          <DecisionPanel data={data} />
          <DecisionsPanel data={data} />
        </div>
      </div>
    </>
  );
}

// ---------- the request ----------

function RequestPanel({ data }: { data: IntakeDetail }) {
  const intake = data.intake;
  const { purpose, owner } = splitNeed(intake.need ?? '');
  return (
    <Panel title="The request" id="request">
      <Facts
        items={[
          { term: 'The need', value: <Prose text={purpose} /> },
          { term: 'Business owner', value: intake.businessOwner?.displayLabel || owner || <Muted>Not named</Muted> },
          { term: 'Expected benefit', value: <Prose text={intake.expectedBenefit} /> },
          { term: 'Proposed kind', value: intake.proposedKind?.label ?? <Muted>Not chosen</Muted> },
          { term: 'Uses AI', value: intake.usesAi === true ? 'Yes' : intake.usesAi === false ? 'No' : <Muted>Not said</Muted> },
          { term: 'Horizon', value: intake.horizon?.label ?? <Muted>Not set</Muted> },
          { term: 'Size band', value: intake.sizeBand?.label ?? <Muted>Not set</Muted> },
          { term: 'Score', value: intake.score ?? <Muted>Not scored</Muted> },
          {
            term: 'Priority',
            value: intake.priority
              ? <TextLink href={hrefTo({ page: 'decision', id: intake.priority.decisionId })}>{`${intake.priority.outcome?.label ?? 'No outcome'}, ${intake.priority.status.label.toLowerCase()}`}</TextLink>
              : <Muted>Not decided</Muted>,
          },
          {
            term: 'Application',
            value: intake.businessApplicationId
              ? <TextLink href={hrefTo({ page: 'app', id: intake.businessApplicationId })}>{intake.applicationName ?? 'Open the application'}</TextLink>
              : <Muted>None yet: a new application</Muted>,
          },
          ...(intake.portfolio !== undefined ? [{
            term: 'Portfolio',
            value: intake.portfolio
              ? <span><TextLink href={hrefTo({ page: 'portfolio', id: intake.portfolio.portfolioId.toLowerCase() })}>{intake.portfolio.name}</TextLink>{intake.portfolio.fromApplication ? <Muted className="ml-1.5 inline">from its application</Muted> : null}</span>
              : <Muted>None yet</Muted>,
          }] : []),
          { term: 'Requester', value: intake.requester ? intake.requester.displayLabel || intake.requester.objectId : <Muted>Not recorded</Muted> },
        ]}
      />
    </Panel>
  );
}

// ---------- the tier ----------

function TierPanel({ data, canAnswer }: { data: IntakeDetail; canAnswer: boolean }) {
  const kind = data.intake.proposedKind;
  return (
    <Panel
      title="Tier assessment"
      subtitle="A suggestion from the tier questions. Only an accepted tier decision sets the tier."
      count={data.tierAssessments.length}
      id="tier"
      actions={canAnswer && kind ? <RecordTierSheet intakeRequestId={data.intake.intakeRequestId} kind={kind.key} kindLabel={kind.label} /> : undefined}
    >
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Suggested tier</span>
        <span className="text-xl leading-7 font-semibold text-navy">{data.intake.suggestedTier?.label ?? 'Not assessed'}</span>
      </div>
      {canAnswer && !kind && <Muted>Choose the proposed kind on the draft first: the tier questions depend on it.</Muted>}
      {data.tierAssessments.length ? (
        <RowList label="Tier assessments, newest first">
          {data.tierAssessments.map((a, i) => (
            <Row
              key={a.tierAssessmentId}
              primary={`${a.computedTier?.label ?? 'No tier'}${i === 0 ? ' (latest)' : ''}`}
              secondary={a.assessedBy ? `Answers by ${a.assessedBy.displayLabel || a.assessedBy.objectId}` : 'Answered by someone not recorded'}
              tertiary={a.performedBy?.stampedOn ? fmtDateTime(a.performedBy.stampedOn) : undefined}
              badges={a.isAiCreated ? <AiMark isAiCreated={a.isAiCreated} performedBy={aiStampOf(a.performedBy)} /> : undefined}
              end={a.tierDecisionId ? <TextLink href={hrefTo({ page: 'decision', id: a.tierDecisionId })}>Tier decision</TextLink> : <Muted>No tier decision</Muted>}
            />
          ))}
        </RowList>
      ) : <Empty>No one has answered the tier questions yet.</Empty>}
    </Panel>
  );
}

// ---------- duplicates ----------

function DuplicatesPanel({ data }: { data: IntakeDetail }) {
  const intake = data.intake;
  const { purpose } = splitNeed(intake.need ?? '');
  const text = `${intake.title} ${purpose}`.trim().slice(0, 2000);
  const state = useTool('check_duplicates', text.length >= 3 ? { text, ...(intake.proposedKind ? { proposedKindKey: intake.proposedKind.key } : {}) } : null);
  return (
    <Panel title="Duplicates and reuse" subtitle="Suggestions from the title and the need. The platform admin weighs reuse at triage." id="duplicates" state={text.length >= 3 ? state : undefined}>
      {text.length >= 3 ? (found => (
        <div className="flex flex-col gap-3">
          {intake.duplicateOfId && (
            <p className="m-0 flex flex-wrap items-center gap-2 text-sm text-navy">
              <ToneBadge tone="warning">Marked as a duplicate</ToneBadge>
              <span>of <TextLink href={hrefTo({ page: 'app', id: intake.duplicateOfId })}>{intake.duplicateOfName ?? 'an existing application'}</TextLink>.</span>
            </p>
          )}
          <DuplicateList
            matches={found.matches.filter(m => m.businessApplicationId !== intake.businessApplicationId && m.businessApplicationId !== intake.duplicateOfId)}
            emptyText={intake.duplicateOfId ? 'No other registered application looks like this.' : undefined}
          />
        </div>
      )) : <Empty>The request has too little text to compare.</Empty>}
    </Panel>
  );
}

// ---------- links ----------

function LinksPanel({ data, canLink }: { data: IntakeDetail; canLink: boolean }) {
  const kinds = useLabels('link-kind');
  return (
    <Panel
      title="Links"
      subtitle="Work items, plans and evidence held elsewhere."
      count={data.links.length}
      id="links"
      actions={canLink ? <LinkSheet intakeRequestId={data.intake.intakeRequestId} /> : undefined}
    >
      {data.links.length ? (
        <RowList label="Links">
          {data.links.map(l => (
            <Row
              key={l.linkId}
              primary={<ExternalLink href={l.url}>{l.label || l.url}</ExternalLink>}
              secondary={l.linkKind.label || kinds.label(l.linkKind.key)}
              end={<ToneBadge tone={l.linkCategory === 'evidence' ? 'informative' : 'subtle'} icon={null}>{l.linkCategory === 'evidence' ? 'Evidence' : 'Delivery'}</ToneBadge>}
            />
          ))}
        </RowList>
      ) : <Empty>No link yet, such as the Azure DevOps work item that delivers it.</Empty>}
    </Panel>
  );
}

// ---------- the intake decision ----------

function DecisionPanel({ data }: { data: IntakeDetail }) {
  const intake = data.intake;
  const decision = intake.triageDecision;
  const status = useTool('get_approval_status', intake.triageDecision ? { subjectKind: 'intake-request', subjectId: intake.intakeRequestId } : null);
  // The gate that holds a proposed decision once its approvals are in (the accept gate needs a business owner).
  const held = useTool('get_decision', decision && decision.status.meaning === 'proposed' ? { decisionId: decision.decisionId } : null);
  const detailRow = decision ? data.decisions.find(d => d.decisionId === decision.decisionId) ?? null : null;
  const d = decision?.status.meaning;
  const outcome = !decision ? null
    : d === 'accepted'
      ? (intake.businessApplicationId
        ? <>Accepted{decision.outcome ? `: ${decision.outcome.label}` : ''}. Registered as <TextLink href={hrefTo({ page: 'app', id: intake.businessApplicationId })}>{intake.applicationName ?? 'the application'}</TextLink>; its owners show on Application 360.</>
        : <>Accepted{decision.outcome ? `: ${decision.outcome.label}` : ''}. No application is registered from this request yet.</>)
      : d === 'rejected' ? <>Declined. The reason is on the decision.</>
        : d === 'withdrawn' ? <>Withdrawn with the request.</>
          : d === 'superseded' ? <>Superseded by a later decision.</>
            : null;
  return (
    <Panel title="Intake decision" subtitle="Whether to take the request on, under the decision rights in force." id="intake-decision">
      {decision ? (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={decision.status} />
            <span className="text-sm font-semibold text-navy">{decision.decisionType.label}</span>
            {decision.outcome && <ToneBadge tone="subtle" icon={null}>Outcome: {decision.outcome.label}</ToneBadge>}
          </div>
          {detailRow && <p className="m-0 text-sm text-navy [overflow-wrap:anywhere]">{detailRow.statement}</p>}
          {detailRow?.decider && (
            <Muted>
              Decided by {detailRow.decider.displayLabel || detailRow.decider.objectId}{detailRow.decidedOn ? ` on ${fmtDate(detailRow.decidedOn)}` : ''}.
            </Muted>
          )}
          {outcome && <p className="m-0 text-sm text-navy">{outcome}</p>}
          <p className="m-0 text-sm">
            <TextLink href={hrefTo({ page: 'decision', id: decision.decisionId })}>Open the decision</TextLink>
            <Muted className="ml-1.5">for the reason, the evidence and every answer.</Muted>
          </p>
          <AskedOfMe intakeRequestId={intake.intakeRequestId} recordNumber={intake.recordNumber} />
        </div>
      ) : (
        <Empty>
          {intake.status.meaning === 'draft'
            ? 'No intake decision yet. It starts when the requester submits the request.'
            : 'No intake decision is recorded for this request.'}
        </Empty>
      )}
      {decision && (
        <div className="flex flex-col gap-2 border-t border-line pt-3">
          <h3 className="m-0 text-sm font-semibold text-navy">Approvals</h3>
          <StateBlock state={status} label="the approvals">
            {s => <ApprovalStatusView status={s} settled={decision.status} gate={dataOf(held)?.gate ?? null} proposedOwner={intake.businessOwner?.displayLabel ?? null} />}
          </StateBlock>
        </div>
      )}
    </Panel>
  );
}

// The approval requests on this request's decisions that ask the signed-in person. The answer buttons stay in place
// after an answer, hidden, so the sheet that sent it keeps the engine's answer in view.
function AskedOfMe({ intakeRequestId, recordNumber }: { intakeRequestId: string; recordNumber: string | null }) {
  const state = useTool('list_approvals', { view: 'mine', top: 100 });
  const [seen, setSeen] = useState<ApprovalItem[]>([]);
  const [answered, setAnswered] = useState<Record<string, 'approve' | 'reject'>>({});
  const data = dataOf(state);
  useEffect(() => {
    if (!data) return;
    const mine = data.items.filter(i => i.subject.subjectKind === 'intake-request' && i.subject.subjectId === intakeRequestId && i.nextTool === 'answer_approval' && i.outcome.meaning === 'pending');
    setSeen(prev => {
      const ids = new Set(prev.map(p => p.approvalRecordId));
      const added = mine.filter(m => !ids.has(m.approvalRecordId));
      return added.length ? [...prev, ...added] : prev;
    });
  }, [data, intakeRequestId]);
  if (!seen.length) return null;
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-primary bg-info-soft px-3.5 py-3">
      <span className="text-sm font-semibold text-navy">{seen.length === 1 ? 'You are asked' : `You are asked ${seen.length} times`}</span>
      {seen.map(item => {
        const as = item.askedRole?.label ?? (item.askedOwnerRole ? `${item.askedOwnerRole.label} of the application` : null);
        const done = answered[item.approvalRecordId];
        return (
          <div key={item.approvalRecordId} className="flex flex-col gap-1.5">
            <span className="text-xs text-navy-2">
              {as ? `As ${as}. ` : ''}
              {item.isConsultation ? 'You are consulted: your answer is advice and never counts.' : 'The engine counts your answer under the approval rule.'}
              {item.expiresOn ? ` Expires ${fmtDateTime(item.expiresOn)}.` : ''}
              {item.isOwnProposal ? ' You proposed this; the rules in force still let you answer.' : ''}
            </span>
            {done && <span className="text-sm font-medium text-navy">You answered: {done === 'approve' ? 'approved' : 'rejected'}.</span>}
            <div className={done ? 'hidden' : undefined}>
              <ApprovalAnswerButtons
                approvalRecordId={item.approvalRecordId}
                subject={`${recordNumber ? `${recordNumber}: ` : ''}${item.decision.statement}`}
                isConsultation={item.isConsultation}
                onAnswered={r => setAnswered(a => ({ ...a, [item.approvalRecordId]: r.answer }))}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ---------- every decision about the request ----------

function DecisionsPanel({ data }: { data: IntakeDetail }) {
  return (
    <Panel title="Decisions about this request" subtitle="The intake decision, priority and any other, newest first." count={data.decisions.length} id="decisions">
      {data.decisions.length ? (
        <RowList label="Decisions about this request">
          {data.decisions.map(d => {
            const href = recordHref(d.recordUrl) ?? hrefTo({ page: 'decision', id: d.decisionId });
            const approvals = approvalsText(d.approvalsSummary);
            return (
              <Row
                key={d.decisionId}
                primary={<TextLink href={href}>{d.statement || d.decisionType.label}</TextLink>}
                secondary={[d.decisionType.label, d.outcome ? `outcome ${d.outcome.label}` : null, approvals].filter(Boolean).join(', ')}
                tertiary={d.decider ? `Decided by ${d.decider.displayLabel || d.decider.objectId}${d.decidedOn ? ` on ${fmtDate(d.decidedOn)}` : ''}` : undefined}
                badges={(d.isTriage || d.isAiCreated) ? (
                  <>
                    {d.isTriage && <ToneBadge tone="brand" icon={null}>Intake decision</ToneBadge>}
                    <AiMark isAiCreated={d.isAiCreated} performedBy={aiStampOf(d.performedBy)} />
                  </>
                ) : undefined}
                end={<StatusBadge status={d.status} />}
              />
            );
          })}
        </RowList>
      ) : <Empty>No decision about this request yet.</Empty>}
    </Panel>
  );
}
