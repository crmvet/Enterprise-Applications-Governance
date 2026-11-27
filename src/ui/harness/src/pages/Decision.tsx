// The decision page (/app/records/decision/<id>): one decision, its approvals, and the one place a person answers an
// approval (D-38). Every approval card and email links here.
// Reads: get_decision (the decision, its subject, the approval state, the evidence, what acceptance writes, and an
// exception's standard and review date); list_approvals view mine (which pending requests ask the signed-in person, and
// whether the engine would take their answer now: only those show Approve and Reject; a request a separation rule keeps
// from them says so and names the rule); get_configuration (the rule labels,
// and whether the approval rule asks for a typed reason to approve); whoami (the person and the tools they may call).
// Writes, each in the shared side sheet: answer_approval (ApprovalAnswerButtons), submit_record (the proposer submits,
// or accepts an AI draft and submits), withdraw_record, save_decision_proposal (a superseding proposal), save_link
// (an evidence link). The engine checks every one exactly as it checks an AI client's write; a refusal shows its rule.
// Self-approval is as the configuration in force allows it (D-40): the page says so plainly when the person proposed the
// decision, and never implies a separation the rules do not make.
// Wave 4: the page is frozen for the page groups. Under each request it renders WaitingOnTeams (pages/teams) and
// AskAgainAction (pages/demo-admin); the decision's facts name the slide-back signal it acts on (slideBackSignal).
import { ArrowRight, CircleCheck, Clock, FileText, GitBranch, History, Info, MessageSquareQuote, TriangleAlert } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { hrefTo, withParams } from '../app/links';
import { ApprovalAnswerButtons } from '../components/actions';
import { DataStamp, Empty, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import type { ApprovalAnswerResult, ApprovalItem, ApprovalRequirement, DecisionApprovalRecord, DecisionDetail, ProposedChanges } from '../data/contracts';
import { fmtDate, fmtDateTime, humanizeKey } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { dataOf, useTool } from '../data/toolClient';
import { EditProposalSheet, EvidenceSheet, SupersedeSheet } from './work-approvals/decisionSheets';
import { askedOfText, askedUnderText, cancelReasonText, countExplained, decisionHistory, expiryText, MODE_WORDS, notSubmitted, optionsWeighed, ownerSubjectText, proposedByMe, recordOutcomeText, splitRecords } from './work-approvals/logic';
import { GateHold } from '../components/gateHold';
import { DecisionDataPanel } from './data-records';
import { ConsultedBadge, CountNote, ExpiryBadge, Quote, Sticky, SubmitSheet, subjectHref, useFocusAfterSheet, useNow, WithdrawSheet } from './work-approvals/parts';
import { sessionOf, useWhoami } from './work-approvals/session';
// ---- wave 4 hook points: each group builds its piece in its own folder ----
import { AskAgainAction } from './demo-admin';
import { WaitingOnTeams } from './teams';

const columns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

export function DecisionRecord({ decisionId }: { decisionId: string }) {
  const now = useNow();
  const state = useTool('get_decision', { decisionId });
  const session = sessionOf(useWhoami());
  const detail = dataOf(state);

  // The pending requests on this decision that ask the signed-in person. list_approvals says so (nextTool), with the
  // engine's own approval answer check run as a pre-check: role, owner role, deputy and the separation rules. A request
  // that asks the person but that a rule keeps from them comes with cannotAnswer and shows no Approve or Reject.
  const pending = detail?.approvals.records.some(r => r.outcome.meaning === 'pending') ?? false;
  const mine = useTool('list_approvals', detail && pending ? { view: 'mine', decisionId, top: 100 } : null);
  // Only while a request is still pending: once the decision settles, the last list read is stale and asks nobody.
  const mineHere = pending ? (dataOf(mine)?.items ?? []).filter(i => sameId(i.decision.decisionId, decisionId)) : [];
  const askingNow = mineHere.filter(i => i.nextTool === 'answer_approval');
  const blocked = mineHere.filter(i => i.nextTool !== 'answer_approval' && !!i.cannotAnswer);

  // A request answered here stays on the page with the person's answer, so the open sheet that shows the engine's answer
  // is not taken away when the page reads its data again.
  const held = useRef(new Map<string, ApprovalItem>());
  for (const i of askingNow) held.current.set(i.approvalRecordId, i);
  const [answers, setAnswers] = useState<Record<string, ApprovalAnswerResult>>({});
  const askingIds = new Set(askingNow.map(i => i.approvalRecordId));
  const answerItems = [...held.current.values()].filter(i => askingIds.has(i.approvalRecordId) || answers[i.approvalRecordId]);

  if (state.status === 'loading') {
    return (
      <>
        <PageHeader eyebrow="Decision" title="Decision" />
        <Panel title="The decision" state={state}>{() => null}</Panel>
      </>
    );
  }
  if (!state.envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Decision" title="The decision cannot be shown" meta={<span className="font-mono [overflow-wrap:anywhere]">{decisionId}</span>} />
        <PanelFailure failure={state.envelope} />
        <p className="mt-4 text-sm text-navy-2">
          <TextLink href={hrefTo({ page: 'approvals' })}>Back to Approvals</TextLink>
        </p>
      </>
    );
  }

  const envelope = state.envelope;
  const d = envelope.data.decision;
  const data = envelope.data;
  const mineDecision = proposedByMe(data, session.objectId);
  const unsubmitted = notSubmitted(data);
  const proposed = d.status.meaning === 'proposed';
  const canSubmit = unsubmitted && mineDecision && session.can('submit_record');
  const canEdit = unsubmitted && mineDecision && session.can('save_decision_proposal') && data.subject.subjectKind !== 'configuration-draft';
  const canWithdraw = proposed && mineDecision && session.can('withdraw_record');
  const canSupersede = d.status.meaning === 'accepted' && !d.supersededBy && data.subject.subjectKind !== 'configuration-draft' && session.can('save_decision_proposal');
  const canAddEvidence = d.status.meaning !== 'withdrawn' && session.can('save_link');
  const aiDraft = d.isAiCreated || d.performedBy?.actorKind === 'ai-client-for-person' || d.performedBy?.actorKind === 'agent-identity';
  const subjectLine = `${d.recordNumber ? `${d.recordNumber}: ` : ''}${d.statement}`;

  return (
    <>
      <PageHeader
        eyebrow={d.recordNumber ? `Decision ${d.recordNumber}` : 'Decision'}
        title={d.statement || 'Decision without a statement'}
        subtitle={(
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={d.status} />
            <span>{d.decisionType.label} decision{d.outcome ? `, ${proposed ? 'proposed outcome' : 'outcome'} ${d.outcome.label}` : ''}</span>
            <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
          </span>
        )}
        meta={(
          <span className="flex flex-col gap-0.5">
            <StampLine performedBy={d.performedBy} />
            <span><DataStamp envelope={envelope} /></span>
          </span>
        )}
        actions={(
          <>
            <Sticky show={canSubmit}><SubmitSheet subjectKind="decision" subjectId={d.decisionId} title={d.statement} isAiDraft={aiDraft} performedBy={d.performedBy} size="default" /></Sticky>
            <Sticky show={canEdit}><EditProposalSheet detail={data} aiDraft={aiDraft} /></Sticky>
            <Sticky show={canWithdraw}><WithdrawSheet subjectKind="decision" subjectId={d.decisionId} title={d.statement} size="default" /></Sticky>
            <Sticky show={canSupersede}><SupersedeSheet detail={data} /></Sticky>
          </>
        )}
      />

      {answerItems.length > 0 && (
        <YourAnswer
          items={answerItems}
          answers={answers}
          onAnswered={(id, result) => setAnswers(a => ({ ...a, [id]: result }))}
          subjectLine={subjectLine}
          now={now}
          gate={proposed ? data.gate : null}
        />
      )}
      {mine.status === 'done' && !mine.envelope.ok && pending && (
        <Alert variant="warning" className="mb-4">
          <TriangleAlert aria-hidden />
          <AlertTitle>Whether this decision asks you could not be read</AlertTitle>
          <AlertDescription>
            <p className="m-0">{mine.envelope.error.message} Reload the page to try again. Reference {mine.envelope.correlationId}.</p>
          </AlertDescription>
        </Alert>
      )}
      {blocked.length > 0 && answerItems.length === 0 && <CannotAnswerNote items={blocked} />}
      <Sticky show={unsubmitted}><NotSubmittedNote mine={mineDecision} canSubmit={canSubmit} aiDraft={aiDraft} detail={data} submitted={!unsubmitted} /></Sticky>
      {pending && mine.status === 'done' && mine.envelope.ok && askingNow.length === 0 && blocked.length === 0 && answerItems.length === 0 && (
        <p className="mt-0 mb-4 flex items-start gap-1.5 text-sm text-navy-2">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>This decision does not ask you. The approvals below show who is asked.</span>
        </p>
      )}

      <div className={columns}>
        <div className={stack}>
          <DecisionFacts detail={data} />
          {/* A data decision's facts (D-37): domain, rule and its source, AI flag, who is asked. pages/data-records. */}
          {data.data && <DecisionDataPanel detail={data} />}
          <ApprovalsPanel detail={data} askingIds={askingIds} now={now} />
          <HistoryPanel detail={data} />
        </div>
        <div className={stack}>
          <SubjectPanel detail={data} />
          <ChainPanel detail={data} />
          <Panel
            title="Evidence"
            count={data.evidence.length}
            actions={<Sticky show={canAddEvidence}><EvidenceSheet decisionId={d.decisionId} /></Sticky>}
          >
            {data.evidence.length ? (
              <RowList label="Evidence links">
                {data.evidence.map(e => (
                  <Row
                    key={e.evidenceLinkId}
                    primary={<ExternalLink href={e.url}>{e.label || e.url}</ExternalLink>}
                    secondary={[e.linkKind.label, e.evidenceLevel ? `Evidence level: ${e.evidenceLevel.label}` : null].filter(Boolean).join('. ')}
                    tertiary={e.capturedOn ? `Captured ${fmtDateTime(e.capturedOn)}` : undefined}
                  />
                ))}
              </RowList>
            ) : <Empty>No evidence link on this decision.</Empty>}
          </Panel>
        </div>
      </div>
    </>
  );
}

// ---------- your answer ----------

function YourAnswer(props: {
  items: ApprovalItem[];
  answers: Record<string, ApprovalAnswerResult>;
  onAnswered: (approvalRecordId: string, result: ApprovalAnswerResult) => void;
  subjectLine: string;
  now: number;
  gate: DecisionDetail['gate'];
}) {
  const ruleKeys = [...new Set(props.items.map(i => i.approvalRuleKey))].filter(k => /^[a-z][a-z0-9-]{0,63}(\/[a-z][a-z0-9-]{0,63})?$/.test(k));
  const rules = useTool('get_configuration', ruleKeys.length ? { entity: 'approval-rule', keys: ruleKeys } : null);
  const ruleRows = dataOf(rules)?.rows ?? [];
  const reasonToApprove = (key: string) => ruleRows.find(r => r.key === key)?.fields.requiresTypedReason === true;
  const open = props.items.filter(i => !props.answers[i.approvalRecordId]).length;

  // After an answer, Approve and Reject step aside, so when the person closes the sheet the focus has no trigger to go
  // back to. It goes to the recorded answer instead of the top of the page.
  const lastAnswered = useRef<HTMLDivElement | null>(null);
  const answeredCount = Object.keys(props.answers).length;
  useFocusAfterSheet(answeredCount > 0, () => lastAnswered.current);

  return (
    <Panel
      title={open ? 'Your answer is asked' : 'Your answer is recorded'}
      subtitle="You answer here, signed in as yourself. Your answer is final, and the engine counts it under the approval rule."
      className="mb-4 border-primary ring-1 ring-primary/30"
      id="your-answer"
    >
      <ul className="m-0 flex list-none flex-col gap-4 p-0">
        {props.items.map(item => {
          const answered = props.answers[item.approvalRecordId];
          const expiry = expiryText(item.expiresOn, props.now);
          const needsReason = reasonToApprove(item.approvalRuleKey);
          return (
            <li key={item.approvalRecordId} className="flex flex-col gap-2.5 border-t border-line pt-3 first:border-t-0 first:pt-0">
              <p className="m-0 text-sm text-navy">
                {/* A person asked by name is asked by name, never "as" themselves (QA walk). */}
                {item.askedPrincipal
                  ? (answered ? 'You were asked by name' : 'You are asked by name')
                  : <>{answered ? 'You were asked as ' : 'You are asked as '}<span className="font-semibold">{askedOfText(item)}</span></>}
                {item.isConsultation ? ', consulted for advice' : ''}.
                {!answered && expiry ? ` ${expiry.text}${item.expiresOn ? ` (${fmtDateTime(item.expiresOn)})` : ''}.` : ''}
                {!answered && item.otherPendingCount > 0 ? ` ${item.otherPendingCount} other request${item.otherPendingCount === 1 ? ' is' : 's are'} pending on this decision.` : ''}
              </p>
              {!answered && (item.isConsultation || needsReason || expiry?.state !== 'later') && (
                <div className="flex flex-wrap gap-1.5">
                  {item.isConsultation && <ConsultedBadge />}
                  <ExpiryBadge expiresOn={item.expiresOn} now={props.now} />
                  {needsReason && <ToneBadge tone="informative">A typed reason is needed to approve</ToneBadge>}
                </div>
              )}
              {!answered && props.gate && !props.gate.ready && !item.isConsultation && (
                <p className="m-0 flex items-start gap-1.5 text-sm text-navy-2">
                  <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
                  <span>
                    {/* Conditional (QA, fix round 1): an answer can itself make the gate ready, as a named business owner's acceptance does. */}
                    {`The ${props.gate.label.toLowerCase()} gate is not ready yet. If it is still not ready once every approval is in, the decision stays proposed until it is, and the engine then accepts it. Your answer may itself make it ready: "Check first" says what your answer would do.`}
                  </span>
                </p>
              )}
              {item.isOwnProposal && !answered && (
                <Alert variant="info">
                  <Info aria-hidden />
                  <AlertTitle>You proposed this decision</AlertTitle>
                  <AlertDescription>
                    <p className="m-0">
                      The configuration in force lets the person who proposed a decision answer it, unless a separation rule
                      applies. If one applies, the engine refuses your answer, or records it and flags it, and names the rule.
                    </p>
                  </AlertDescription>
                </Alert>
              )}
              {answered && (
                <Alert variant="success" role="status" tabIndex={-1} ref={lastAnswered} className="outline-none focus-visible:ring-2 focus-visible:ring-ring">
                  <CircleCheck aria-hidden />
                  <AlertTitle>You answered: {answered.status.label}</AlertTitle>
                  <AlertDescription>
                    <p className="m-0">
                      {answered.isConsultation
                        ? 'Your answer is recorded as advice. It does not count toward the approval.'
                        : answered.countsTowardRequirement
                          ? 'Your answer counts toward the approval.'
                          : 'Your answer is recorded, and it does not count toward the approval. The approvals below say why.'}
                      {' '}{answered.decisionStatus.meaning === 'proposed'
                        ? `The decision stays ${answered.decisionStatus.label.toLowerCase()}.`
                        : `The decision is now ${answered.decisionStatus.label.toLowerCase()}.`}
                    </p>
                  </AlertDescription>
                </Alert>
              )}
              {/* After an answer the buttons step aside, but stay mounted, so the sheet that shows the engine's answer stays open. */}
              <div className={answered ? 'hidden' : undefined}>
                <ApprovalAnswerButtons
                  approvalRecordId={item.approvalRecordId}
                  subject={props.subjectLine}
                  reasonToApprove={needsReason}
                  isConsultation={item.isConsultation}
                  onAnswered={result => props.onAnswered(item.approvalRecordId, result)}
                  size="default"
                />
              </div>
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}

// A request asks the person, and the engine would not take their answer: the page says why and names the rule, in place
// of Approve and Reject (D-38, D-40: self-approval is as the configuration in force has it).
function CannotAnswerNote({ items }: { items: ApprovalItem[] }) {
  return (
    <Alert variant="info" className="mb-4">
      <Info aria-hidden />
      <AlertTitle>{items.length === 1 ? 'You are asked, and another person answers' : 'You are asked, and other people answer'}</AlertTitle>
      <AlertDescription>
        <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
          {items.map(i => (
            <li key={i.approvalRecordId} className="flex flex-col gap-0.5">
              <span>
                As <span className="font-semibold">{askedOfText(i)}</span>
                {i.cannotAnswer?.separationRule ? <>: the separation rule <span className="font-semibold">{i.cannotAnswer.separationRule.label}</span> keeps you from answering.</> : '.'}
              </span>
              {/* The engine names the rule in plain words (wave 4b review), so its message shows as it is. */}
              {i.cannotAnswer && <span className="text-xs text-navy-2">{i.cannotAnswer.message}</span>}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}

// Once submitted, the note stays mounted only so the open submit sheet keeps the engine's answer; it is hidden and says
// nothing to assistive technology (QA 2026-11-24: "Proposed, not submitted yet" stayed after the submit).
function NotSubmittedNote({ mine, canSubmit, aiDraft, detail, submitted }: { mine: boolean; canSubmit: boolean; aiDraft: boolean; detail: DecisionDetail; submitted: boolean }) {
  const d = detail.decision;
  return (
    <Alert variant="info" className="mb-4" hidden={submitted} aria-hidden={submitted || undefined} role={submitted ? 'presentation' : undefined}>
      <Clock aria-hidden />
      <AlertTitle>{aiDraft ? 'An AI draft, not submitted yet' : 'Proposed, not submitted yet'}</AlertTitle>
      <AlertDescription>
        <div className="flex flex-col gap-2">
          <p className="m-0">
            Nobody is asked until it is submitted.
            {mine
              ? aiDraft
                ? ' An AI client drafted it for you. Read it, change it with Edit if you need to, then accept and submit it: it becomes yours, and the engine asks the people the rules name.'
                : ' Submit it when it is ready; the engine then asks the people the rules name.'
              : d.proposedBy
                ? ` Only ${d.proposedBy.displayLabel}, who proposed it, can submit it.`
                : ' Only the person who proposed it can submit it.'}
          </p>
          {canSubmit && (
            <div>
              <SubmitSheet subjectKind="decision" subjectId={d.decisionId} title={d.statement} isAiDraft={aiDraft} performedBy={d.performedBy} size="default" />
            </div>
          )}
        </div>
      </AlertDescription>
    </Alert>
  );
}

// ---------- the decision ----------

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[150px_minmax(0,1fr)] gap-x-4 border-t border-line py-2 first:border-t-0 first:pt-0 narrow:grid-cols-1 narrow:gap-y-0.5">
      <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{label}</dt>
      <dd className="m-0 text-sm text-navy [overflow-wrap:anywhere] whitespace-pre-wrap">{children}</dd>
    </div>
  );
}

function DecisionFacts({ detail }: { detail: DecisionDetail }) {
  const d = detail.decision;
  const settled = d.status.meaning !== 'proposed';
  return (
    <Panel title="The decision" id="decision">
      <dl className="m-0 flex flex-col">
        <Fact label="Statement">{d.statement || 'Not set'}</Fact>
        <Fact label="Type">{d.decisionType.label}</Fact>
        <Fact label={settled ? 'Outcome' : 'Proposed outcome'}>{d.outcome?.label ?? 'Not set'}</Fact>
        <Fact label="Reason">{d.reason || 'Not given'}</Fact>
        <Fact label="Context">{d.context || 'Not given'}</Fact>
        {d.optionsConsidered && <Fact label="Options weighed"><OptionsWeighed text={d.optionsConsidered} /></Fact>}
        {d.exception?.standard && <Fact label="Departs from">{d.exception.standard}</Fact>}
        {d.exception?.reviewOn && <Fact label="Review on">{fmtDate(d.exception.reviewOn)}</Fact>}
        {/* Wave 4 (B24-22): the slide-back signal the decision acts on, linking to it on the Measures page. */}
        {d.slideBackSignal && (
          <Fact label="Acts on">
            <TextLink href={withParams(hrefTo({ page: 'measures' }), { signal: String(d.slideBackSignal.number) })}>
              {`Slide-back signal ${d.slideBackSignal.number}: ${d.slideBackSignal.label}`}
            </TextLink>
          </Fact>
        )}
        {d.proposedChanges && <Fact label={settled ? 'What it changed' : 'What acceptance changes'}><ProposedChangesList changes={d.proposedChanges} /></Fact>}
        {d.configurationChange && <Fact label={settled ? 'What it changed' : 'What changes'}><ConfigurationChangeList change={d.configurationChange} accepted={d.status.meaning === 'accepted'} /></Fact>}
        {d.confidence && <Fact label="Confidence">{d.confidence.label}</Fact>}
        <Fact label="Proposed by">
          <span className="flex flex-col items-start gap-1">
            <span>{d.proposedBy?.displayLabel ?? 'Not recorded'}</span>
            <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
          </span>
        </Fact>
        <Fact label="Decided">
          {d.decidedOn
            ? `${d.decider ? `${d.decider.displayLabel}, ` : ''}${fmtDateTime(d.decidedOn)}`
            : settled ? 'Not recorded' : 'Not yet'}
        </Fact>
        <Fact label="Rules version">Configuration version {d.configurationSetVersion}, the version the decision was checked under.</Fact>
      </dl>
    </Panel>
  );
}

// What the engine writes on the subject once the decision is accepted, in plain words.
function ProposedChangesList({ changes }: { changes: ProposedChanges }) {
  const lines: string[] = [
    ...changes.owners.map(o => {
      // "deputy business owner" for a deputy, in the role's own words (QA 2026-11-24: an ended deputy read as the
      // business owner).
      const roleWords = (o.ownerRole?.label ?? 'an owner role').toLowerCase();
      const role = `${o.isDeputy ? 'deputy ' : ''}${roleWords}`;
      const endedRole = `${o.endsIsDeputy ? 'deputy ' : ''}${roleWords}`;
      const start = o.startOn ? ` from ${fmtDate(o.startOn)}` : '';
      if (o.add && o.ends) return `${o.add.displayLabel} becomes ${role}${start}, in place of ${o.ends.displayLabel} (${endedRole}).`;
      if (o.add) return `${o.add.displayLabel} becomes ${role}${start}.`;
      return `${o.ends?.displayLabel ?? 'The current holder'} stops being ${endedRole}.`;
    }),
    ...(changes.tier ? [`The tier becomes ${changes.tier.label}.`] : []),
    ...(changes.autonomyLevel ? [`The agreed autonomy level becomes ${changes.autonomyLevel.label}.`] : []),
  ];
  if (!lines.length) return <>Nothing beyond the decision itself.</>;
  return (
    <ul className="m-0 flex list-disc flex-col gap-1 pl-5 whitespace-normal">
      {lines.map((l, i) => <li key={i}>{l}</li>)}
    </ul>
  );
}

// What a configuration change decision changes, from the draft's change summary, so the second admin reads it before
// answering (product review 2026-11-24).
function ConfigurationChangeList({ change, accepted }: { change: NonNullable<DecisionDetail['decision']['configurationChange']>; accepted: boolean }) {
  return (
    <span className="flex flex-col gap-1.5 whitespace-normal">
      {change.changeSummary.length ? (
        <ul className="m-0 flex list-disc flex-col gap-1 pl-5">
          {change.changeSummary.map((l, i) => <li key={i}>{l}</li>)}
        </ul>
      ) : <span>The draft names no change.</span>}
      <span className="text-xs text-navy-2">
        {change.baseVersionNumber !== null ? (accepted ? `Version ${change.baseVersionNumber} became version ${change.baseVersionNumber + 1} when it was accepted. ` : `Version ${change.baseVersionNumber} becomes version ${change.baseVersionNumber + 1} once accepted. `) : ''}
        {change.isValid === false ? 'The rules check found problems when the draft was saved: activation checks again and discards a draft that still fails.' : 'The rules check passed when the draft was saved.'}
      </span>
      {change.problems.length > 0 && (
        <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-xs text-navy-2">
          {change.problems.map((p, i) => <li key={i}>{p}</li>)}
        </ul>
      )}
    </span>
  );
}

function OptionsWeighed({ text }: { text: string }) {
  const options = optionsWeighed(text);
  if (!options) return <>{text}</>;
  return (
    <ul className="m-0 flex list-disc flex-col gap-1.5 pl-5 whitespace-normal">
      {options.map((o, i) => (
        <li key={i}>
          <span className="font-semibold">{o.option}</span>
          {o.whyNot && <span className="text-navy-2">{` Why not: ${o.whyNot}`}</span>}
        </li>
      ))}
    </ul>
  );
}

// ---------- approvals ----------

// For a data decision, an owner role on the domain or the location names it ("Data owner of People / Payroll").
function whoRequired(r: { requiredRoleKey?: string; requiredOwnerRoleKey?: string; asksProposedOwners?: boolean }, roles: Labels, ownerRoles: Labels, data?: DecisionDetail['data']): string {
  if (r.requiredRoleKey) return roles.label(r.requiredRoleKey);
  if (r.requiredOwnerRoleKey) return `${ownerRoles.label(r.requiredOwnerRoleKey)} ${ownerSubjectText(r.requiredOwnerRoleKey, data)}`;
  if (r.asksProposedOwners) return 'Each proposed owner, by name';
  return 'The accountable holder';
}

function ApprovalsPanel({ detail, askingIds, now }: { detail: DecisionDetail; askingIds: Set<string>; now: number }) {
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  const rules = useLabels('approval-rule');
  const rights = useLabels('decision-right');
  const separation = useLabels('separation-rule');
  const ruleText = (key: string) => askedUnderText(key, rules, rights);
  const a = detail.approvals;
  const held = !!detail.gate && !detail.gate.ready;
  const { approvals, consultations } = splitRecords(a.records);
  const counting = a.required.filter(r => !r.isConsultation);
  return (
    <Panel
      title="Approvals"
      subtitle={a.evaluated
        ? 'The engine evaluated the rules now, under the configuration version the decision was checked under.'
        : 'The decision is settled. These are the requests and answers the engine stored when it settled.'}
      count={a.records.length}
      id="approvals"
    >
      {a.evaluated && (
        a.ready ? (
          held ? <GateHold gate={detail.gate} approvalsIn subjectKind={detail.subject.subjectKind} /> : (
            <p className="m-0 flex items-start gap-1.5 text-sm text-navy">
              <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
              <span>Every approval the rules require is in.</span>
            </p>
          )
        ) : a.missing.length ? (
          <div className="flex flex-col gap-1">
            <p className="m-0 flex items-start gap-1.5 text-sm font-semibold text-navy">
              <Clock className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
              <span>Still needed</span>
            </p>
            <ul className="m-0 flex list-disc flex-col gap-0.5 pl-9 text-sm text-navy">
              {a.missing.map((m, i) => (
                <li key={`${m.approvalRuleKey}-${i}`}>
                  {m.stillNeeded} approval{m.stillNeeded === 1 ? '' : 's'} from {whoRequired(m, roles, ownerRoles, detail.data)}
                  {(() => {
                    // A rule with a fallback owner role asks that role when the first has no holder (product review
                    // 2026-11-24: the page named the sponsor while the business owner was asked).
                    const fallback = rules.field<string>(m.approvalRuleKey, 'fallbackOwnerRoleKey') ?? rights.field<string>(m.approvalRuleKey, 'fallbackOwnerRoleKey');
                    return fallback && m.requiredOwnerRoleKey ? `, or the ${ownerRoles.label(fallback).toLowerCase()} when no ${ownerRoles.label(m.requiredOwnerRoleKey).toLowerCase()} is named` : null;
                  })()}
                  <span className="text-navy-3"> ({ruleText(m.approvalRuleKey).replace(/^Rule: /, '')})</span>
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <p className="m-0 text-sm text-navy-2">The rules are not met yet, and the engine names no missing approval. Read the requests below.</p>
        )
      )}
      {/* The gate holds the decision even once the approvals are in, so the page says so before anyone approves (wave 3
          verification: the hold showed only after the last approval). */}
      {a.evaluated && !a.ready && held && <GateHold gate={detail.gate} approvalsIn={false} subjectKind={detail.subject.subjectKind} />}

      {a.evaluated && counting.length > 0 && (
        <div className="flex flex-col gap-1.5">
          <h3 className="m-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">What the rules require</h3>
          <RowList label="Required approvals">
            {counting.map((r, i) => <RequirementRow key={`${r.approvalRuleKey}-${i}`} r={r} who={whoRequired(r, roles, ownerRoles, detail.data)} ruleText={ruleText(r.approvalRuleKey)} />)}
          </RowList>
        </div>
      )}

      <div className="flex flex-col gap-1.5">
        <h3 className="m-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Requests and answers</h3>
        {approvals.length ? (
          <RowList label="Approval requests">
            {approvals.map(r => <RecordRow key={r.approvalRecordId} r={r} detail={detail} data={detail.data ?? null} now={now} asksYou={askingIds.has(r.approvalRecordId) && r.outcome.meaning === 'pending'} ruleText={ruleText(r.approvalRuleKey)} separationLabel={k => separation.label(k)} cancelReason={cancelReasonText(r, detail.decision.status.meaning, a.records)} />)}
          </RowList>
        ) : (
          <Empty>{notSubmitted(detail) ? 'Nobody is asked yet: the decision is not submitted.'
            : a.settledBy === 'recorded' ? `It was recorded as ${detail.decision.status.label.toLowerCase()} outside the product's approvals, as the demo estate is, so no approval request is on record.`
              : a.settledBy === 'engine' ? 'No approval request was needed: the engine accepted it under the locked list.'
                : 'No approval request on this decision.'}</Empty>
        )}
      </div>

      {(consultations.length > 0 || a.required.some(r => r.isConsultation)) && (
        <div className="flex flex-col gap-1.5">
          <h3 className="m-0 flex items-center gap-1.5 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">
            <MessageSquareQuote className="size-3.5" aria-hidden />
            Consultations: recommendation only
          </h3>
          {consultations.length ? (
            <RowList label="Consultations">
              {consultations.map(r => <RecordRow key={r.approvalRecordId} r={r} detail={detail} data={detail.data ?? null} now={now} asksYou={askingIds.has(r.approvalRecordId) && r.outcome.meaning === 'pending'} ruleText={ruleText(r.approvalRuleKey)} separationLabel={k => separation.label(k)} cancelReason={cancelReasonText(r, detail.decision.status.meaning, a.records)} />)}
            </RowList>
          ) : <Empty>The rules consult someone, and no consultation request is open yet.</Empty>}
        </div>
      )}
    </Panel>
  );
}

function RequirementRow({ r, who, ruleText }: { r: ApprovalRequirement; who: string; ruleText: string }) {
  const met = r.received >= r.minimumCount;
  return (
    <Row
      primary={who}
      secondary={`${MODE_WORDS[r.approvalMode].charAt(0).toUpperCase()}${MODE_WORDS[r.approvalMode].slice(1)}. ${ruleText}.`}
      end={met
        ? <ToneBadge tone="success">{`${r.received} of ${r.minimumCount} in`}</ToneBadge>
        : <ToneBadge tone="warning" icon={<Clock aria-hidden />}>{`${r.received} of ${r.minimumCount} in`}</ToneBadge>}
    />
  );
}

// One request. Wave 4 hook points: WaitingOnTeams (the teams group) names the
// team a pending request waits on; AskAgainAction (the demo-admin group) offers "Ask again" on an expired request or one
// about to expire. Each renders nothing where it does not apply.
function RecordRow({ r, detail, data, now, asksYou, ruleText, separationLabel, cancelReason }: { r: DecisionApprovalRecord; detail: DecisionDetail; data: DecisionDetail['data']; now: number; asksYou: boolean; ruleText: string; separationLabel: (key: string) => string; cancelReason: string }) {
  const pending = r.outcome.meaning === 'pending';
  return (
    <Row
      primary={askedOfText({ ...r, data })}
      secondary={(
        <>
          {pending
            ? r.requestedOn ? `Asked ${fmtDateTime(r.requestedOn)}.` : 'Asked.'
            : recordOutcomeText(r)}
          {' '}{ruleText}.
        </>
      )}
      tertiary={(
        <span className="flex flex-col gap-1">
          {r.typedReason && <Quote>{r.typedReason}</Quote>}
          <CountNote explained={countExplained(r, separationLabel, cancelReason)} />
          <WaitingOnTeams record={r} detail={detail} />
          <AskAgainAction record={r} detail={detail} now={now} />
        </span>
      )}
      badges={(asksYou || r.isConsultation || pending || !!r.cannotAnswer) ? (
        <>
          {asksYou && <Badge variant="default">Asks you</Badge>}
          {!asksYou && r.cannotAnswer && <ToneBadge tone="informative">{r.cannotAnswer.separationRule ? `Asks you, ${r.cannotAnswer.separationRule.label} applies` : 'Asks you, another person answers'}</ToneBadge>}
          {r.isConsultation && <ConsultedBadge />}
          {pending && <ExpiryBadge expiresOn={r.expiresOn} now={now} />}
        </>
      ) : undefined}
      end={<StatusBadge status={r.outcome} />}
      wideEnd
    />
  );
}

// ---------- history ----------

function HistoryPanel({ detail }: { detail: DecisionDetail }) {
  const events = decisionHistory(detail);
  return (
    <Panel title="History" subtitle="From the times the product stored: each request, each answer, the settlement and the last change." id="history">
      {events.length ? (
        <ol className="m-0 flex list-none flex-col p-0" aria-label="History, oldest first">
          {events.map((e, i) => (
            <li key={`${e.at}-${i}`} className="grid grid-cols-[20px_minmax(0,1fr)] gap-x-3 pb-3 last:pb-0">
              <span className="flex flex-col items-center" aria-hidden>
                <History className="mt-0.5 size-4 text-navy-3" />
                {i < events.length - 1 && <span className="mt-1 w-px flex-1 bg-line" />}
              </span>
              <span className="flex min-w-0 flex-col gap-0.5">
                <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{e.title}</span>
                <Muted>{fmtDateTime(e.at)}</Muted>
                {e.detail && <span className="text-xs leading-relaxed whitespace-pre-wrap text-navy-2 [overflow-wrap:anywhere]">{e.detail}</span>}
              </span>
            </li>
          ))}
        </ol>
      ) : <Empty>The product holds no dated event for this decision yet.</Empty>}
    </Panel>
  );
}

// ---------- subject and chain ----------

function SubjectPanel({ detail }: { detail: DecisionDetail }) {
  const s = detail.subject;
  const plain = subjectHref(s.subjectKind, s.subjectId);
  // A use or a mapping record finds its row in its domain: naming the domain saves it a lookup.
  const domain = detail.data?.domain;
  const href = plain && domain && (s.subjectKind === 'data-use' || s.subjectKind === 'data-mapping') ? withParams(plain, { domain: domain.key }) : plain;
  return (
    <Panel title="What it decides about" id="subject">
      <div className="flex items-start gap-3">
        <span className="grid size-9 shrink-0 place-items-center rounded-full bg-secondary text-navy-2" aria-hidden><FileText className="size-4" /></span>
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{humanizeKey(s.subjectKind)}</span>
          <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{href ? <TextLink href={href}>{s.label}</TextLink> : s.label}</span>
          {s.businessApplicationId && s.subjectKind !== 'business-application' && (
            <span className="text-sm text-navy-2">
              Application:{' '}
              <TextLink href={hrefTo({ page: 'app', id: s.businessApplicationId })}>{s.applicationName ?? 'Open the application'}</TextLink>
            </span>
          )}
        </div>
      </div>
      {s.businessApplicationId && (
        <TextLink href={hrefTo({ page: 'approvals', view: 'open', applicationId: s.businessApplicationId })} className="inline-flex items-center gap-1 text-sm">
          Open approvals on this application <ArrowRight className="size-4" aria-hidden />
        </TextLink>
      )}
    </Panel>
  );
}

function ChainPanel({ detail }: { detail: DecisionDetail }) {
  const d = detail.decision;
  const links: { label: string; id: string }[] = [
    ...(d.supersedes ? [{ label: 'Supersedes an earlier decision', id: d.supersedes }] : []),
    ...(d.supersededBy ? [{ label: 'Superseded by a later decision', id: d.supersededBy }] : []),
    ...(d.amends ? [{ label: 'Amends an earlier decision', id: d.amends }] : []),
    ...d.amendedBy.map(id => ({ label: 'Amended by a later decision', id })),
  ];
  if (!links.length) return null;
  return (
    <Panel title="Related decisions" count={links.length} id="chain">
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {links.map(l => (
          <li key={`${l.label}-${l.id}`} className="flex items-start gap-2 text-sm">
            <GitBranch className="mt-0.5 size-4 shrink-0 text-navy-3" aria-hidden />
            <TextLink href={hrefTo({ page: 'decision', id: l.id })}>{l.label}</TextLink>
          </li>
        ))}
      </ul>
      {d.supersededBy && (
        <p className="m-0 flex items-start gap-1.5 text-xs text-navy-2">
          <Info className="mt-px size-3.5 shrink-0" aria-hidden />
          <span>A later decision replaces this one. Read that one for what holds now.</span>
        </p>
      )}
    </Panel>
  );
}

