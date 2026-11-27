// Pieces My work, Approvals and the decision page share: an approval request row, the counted-or-not note, the expiry
// badge, the submit and withdraw sheets, and Sticky, which keeps an action's sheet open after the record moves on.
// Every write goes through the shared ActionSheet (components/actions.tsx).
import { Bot, CircleCheck, CircleSlash, Clock, Gavel, Hourglass, Info, Lock, MessageSquareQuote, Send, Undo2, UserRoundCheck } from 'lucide-react';
import { useEffect, useState, type ReactElement, type ReactNode } from 'react';
import { hrefTo, recordHrefOf, withParams } from '../../app/links';
import { RECORD_PAGES } from '../../app/routes';
import { ActionSheet, Field } from '../../components/actions';
import { Muted, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ClassificationBadge, DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Textarea } from '../../components/ui/textarea';
import type { ApprovalItem, StampedBy, SubmittableKind, WithdrawableKind } from '../../data/contracts';
import { DATA_RULE_TEXT, DATA_RULE_TONE } from '../../data/dataTaxonomy';
import { fmtDateTime } from '../../data/format';
import type { Tone } from '../../data/meanings';
import { countExplained, expiryText, requestFacts, splitKept, type CountExplained, type CountState } from './logic';

// The time the page opened, so ages and expiries do not shift while the person reads.
export function useNow(): number {
  const [now] = useState(() => Date.now());
  return now;
}

// ---------- actions that outlive their condition ----------

// A write reads the page's data again while its sheet still shows the engine's answer. When that new data takes the
// action away (a submitted draft leaves the queue, an answered request stops asking), the sheet must not vanish with it.
// When active turns true while a sheet is open, this waits for the sheet to close and then, if the focus fell to the
// page body because its trigger is gone, puts it on target (else on the page's main region).
export function useFocusAfterSheet(active: boolean, target?: () => HTMLElement | null): void {
  useEffect(() => {
    if (!active || typeof MutationObserver === 'undefined' || !document.querySelector('[role="dialog"]')) return;
    const observer = new MutationObserver(() => {
      if (document.querySelector('[role="dialog"]')) return;
      observer.disconnect();
      if (document.activeElement !== null && document.activeElement !== document.body) return;
      const element = target?.() ?? document.getElementById('main');
      element?.focus({ preventScroll: false });
    });
    observer.observe(document.body, { childList: true });
    return () => observer.disconnect();
    // target is read when the sheet closes, not when the effect starts, so it is not a dependency.
  }, [active]);
}

// Shows an action while show is true. Once shown, it stays mounted (hidden) after show turns false, so a sheet it opened
// keeps showing the engine's answer until the person closes it.
export function Sticky({ show, children, focusTarget }: { show: boolean; children: ReactNode; focusTarget?: () => HTMLElement | null }) {
  const [shown, setShown] = useState(show);
  if (show && !shown) setShown(true);
  useFocusAfterSheet(shown && !show, focusTarget);
  if (!shown) return null;
  return <span className={show ? 'contents' : 'hidden'}>{children}</span>;
}

// ---------- small marks ----------

export function ExpiryBadge({ expiresOn, now }: { expiresOn: string | null; now: number }) {
  const expiry = expiryText(expiresOn, now);
  if (!expiry || expiry.state === 'later') return null;
  return (
    <ToneBadge tone={expiry.state === 'expired' ? 'danger' : 'warning'} icon={<Hourglass aria-hidden />}>
      <span title={expiresOn ? fmtDateTime(expiresOn) : undefined}>{expiry.text}</span>
    </ToneBadge>
  );
}

const COUNT_LOOK: Record<CountState, { tone: Tone; icon: ReactElement }> = {
  waiting: { tone: 'warning', icon: <Clock aria-hidden /> },
  counted: { tone: 'success', icon: <CircleCheck aria-hidden /> },
  advice: { tone: 'informative', icon: <MessageSquareQuote aria-hidden /> },
  recommendation: { tone: 'informative', icon: <Info aria-hidden /> },
  'not-counted': { tone: 'subtle', icon: <CircleSlash aria-hidden /> },
  closed: { tone: 'subtle', icon: <CircleSlash aria-hidden /> },
};

// Counted or not, and why: an icon and a sentence, never color alone.
export function CountNote({ explained, className }: { explained: CountExplained; className?: string }) {
  const look = COUNT_LOOK[explained.state];
  return (
    <span className={`inline-flex items-start gap-1.5 text-xs leading-4 text-navy-2 ${className ?? ''}`}>
      <span className={`mt-px shrink-0 [&>svg]:size-3.5 ${toneText[look.tone]}`}>{look.icon}</span>
      <span>{explained.text}</span>
    </span>
  );
}

const toneText: Record<Tone, string> = {
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
  brand: 'text-brand-dark',
  informative: 'text-info',
  subtle: 'text-navy-3',
};

export function ConsultedBadge() {
  return <ToneBadge tone="informative" icon={<MessageSquareQuote aria-hidden />}>Consulted, advice only</ToneBadge>;
}

export function OwnProposalBadge() {
  return <ToneBadge tone="subtle" icon={<UserRoundCheck aria-hidden />}>You proposed this</ToneBadge>;
}

// A quoted reason a person typed. Plain text only (x-untrusted).
export function Quote({ children }: { children: ReactNode }) {
  return <span className="block border-l-2 border-line-strong pl-2.5 text-xs leading-relaxed whitespace-pre-wrap text-navy [overflow-wrap:anywhere]">{children}</span>;
}

// ---------- one approval request in a list ----------

export function ApprovalRow({ item, now, view }: { item: ApprovalItem; now: number; view: 'mine' | 'asked-by-me' | 'open' | 'answered' }) {
  const decisionHref = hrefTo({ page: 'decision', id: item.decision.decisionId });
  const subjectHref = subjectLink(item);
  const answerable = item.nextTool === 'answer_approval';
  const facts = requestFacts(item, now);
  const answered = item.outcome.meaning !== 'pending';
  const subjectText = item.subject.applicationName && item.subject.applicationName !== item.subject.label
    ? `${item.subject.label}, ${item.subject.applicationName}`
    : item.subject.label;
  return (
    <Row
      lead={<span className="grid size-8 place-items-center rounded-full bg-secondary text-navy-2" aria-hidden><Gavel className="size-4" /></span>}
      primary={<TextLink href={decisionHref}>{item.decision.statement || 'Decision without a statement'}</TextLink>}
      secondary={(
        <>
          {item.decision.decisionType.label} decision about{' '}
          {subjectHref ? <TextLink href={subjectHref}>{subjectText}</TextLink> : subjectText}
          {item.isOwnProposal ? '. Proposed by you' : item.decision.proposedBy ? `. Proposed by ${item.decision.proposedBy.displayLabel}` : ''}
        </>
      )}
      tertiary={(
        <span className="flex flex-col gap-1">
          {item.data && <DataFacts data={item.data} />}
          <span>{facts.join(', ')}.</span>
          {answered && (
            <>
              <span>
                {item.approver ? `${item.approver.displayLabel} answered` : 'Answered'}
                {item.respondedOn ? ` ${fmtDateTime(item.respondedOn)}` : ''}.
              </span>
              {item.typedReason && <Quote>{item.typedReason}</Quote>}
              <CountNote explained={countExplained(item)} />
            </>
          )}
          {!answered && !answerable && item.cannotAnswer && (
            <span className="text-navy-2">
              {item.cannotAnswer.separationRule
                ? `The separation rule ${item.cannotAnswer.separationRule.label} keeps you from answering. Another person who is asked answers it.`
                : 'The engine would not take your answer on this request. Another person who is asked answers it.'}
            </span>
          )}
        </span>
      )}
      badges={(item.decision.isAiCreated || item.isConsultation || item.isOwnProposal || !answered) ? (
        <>
          <AiMark isAiCreated={item.decision.isAiCreated} performedBy={null} />
          {item.isConsultation && <ConsultedBadge />}
          {item.isOwnProposal && view !== 'asked-by-me' && <OwnProposalBadge />}
          {!answered && <ExpiryBadge expiresOn={item.expiresOn} now={now} />}
        </>
      ) : undefined}
      end={<StatusBadge status={item.outcome} />}
      endSub={answerable ? (
        <Button asChild size="sm" className="mt-1">
          <a href={decisionHref} aria-label={`Answer: ${item.decision.statement}`}>Answer</a>
        </Button>
      ) : undefined}
      wideEnd
    />
  );
}

// The requests that ask the person but that another person answers (wave 4b verify round), each with the separation rule that
// keeps them out (ApprovalRow says it). Approvals' Waiting for me and My work list them apart from what the person may
// answer now, so nobody reads them as theirs to answer; they stay in view so the person sees what was asked and why.
// count is the server's count of them (list_approvals totalCount less answerableCount), or the rows shown.
export function KeptFromAnswering({ items, now, count }: { items: ApprovalItem[]; now: number; count: number }) {
  // A separation rule's rows apart from the ones that cannot be answered now for another reason (review).
  const { separated, notNow } = splitKept(items);
  return (
    <>
      {separated.length > 0 && (
        <KeptGroup
          title="Asked of you, but another person answers"
          note="A separation rule keeps you from answering these. Another person answers each, as its decision page says."
          items={separated}
          count={Math.max(separated.length, count - notNow.length)}
          now={now}
        />
      )}
      {notNow.length > 0 && (
        <KeptGroup
          title="Asked of you, but it cannot be answered now"
          note="Each row says why, such as a request whose time ran out before the daily check closed it."
          items={notNow}
          count={notNow.length}
          now={now}
        />
      )}
    </>
  );
}

function KeptGroup({ title, note, items, count, now }: { title: string; note: string; items: ApprovalItem[]; count: number; now: number }) {
  return (
    <div className="mt-3 flex flex-col gap-1.5 border-t border-line pt-3">
      <h3 className="m-0 flex items-center gap-2 text-sm font-semibold text-navy">
        {title}
        <span className="rounded-full bg-muted px-1.5 text-xs leading-[18px] font-semibold text-navy-3 tabular-nums" aria-label={`${count} ${count === 1 ? 'request' : 'requests'}`}>{count}</span>
      </h3>
      <Muted>{note}</Muted>
      <RowList label={title}>
        {items.map(item => <ApprovalRow key={item.approvalRecordId} item={item} now={now} view="mine" />)}
      </RowList>
    </div>
  );
}

// A data decision's facts on its row (list_approvals data): the domain, its classification, the rule that governs, and
// whether the data reaches AI or is de-identified. Every mark carries its words.
function DataFacts({ data }: { data: NonNullable<ApprovalItem['data']> }) {
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      {data.domain && <DomainLink domain={data.domain} />}
      {data.classification && <ClassificationBadge classification={data.classification} />}
      {data.rule && <ToneBadge tone={DATA_RULE_TONE[data.rule]} icon={data.rule === 'never' ? <Lock aria-hidden /> : null}>{`Rule: ${DATA_RULE_TEXT[data.rule]}`}</ToneBadge>}
      {data.reachesAi && <ToneBadge tone="warning" icon={<Bot aria-hidden />}>Sends to AI</ToneBadge>}
      {data.isDeidentified && <ToneBadge tone="informative" icon={null}>De-identified</ToneBadge>}
    </span>
  );
}

function subjectLink(item: ApprovalItem): string | null {
  const href = subjectHref(item.subject.subjectKind, item.subject.subjectId);
  // A use or a mapping record finds its row in its domain: naming the domain saves it a lookup.
  const domain = item.data?.domain;
  return href && domain && (item.subject.subjectKind === 'data-use' || item.subject.subjectKind === 'data-mapping') ? withParams(href, { domain: domain.key }) : href;
}

// The page of a decision's subject, when the web app has a page for that kind of record.
export function subjectHref(kind: string, id: string): string | null {
  return Object.prototype.hasOwnProperty.call(RECORD_PAGES, kind) ? recordHrefOf(kind, id) : null;
}

// ---------- submit and withdraw ----------

// Submit a record, or accept an AI draft as yours and submit it (D-28: AI drafts, a person accepts). The engine checks
// the record and answers who is asked now.
export function SubmitSheet(props: {
  subjectKind: SubmittableKind;
  subjectId: string;
  title: string;
  isAiDraft: boolean;
  performedBy?: StampedBy | null;
  trigger?: ReactElement;
  size?: 'sm' | 'default';
  onDone?: () => void;
}) {
  const [note, setNote] = useState('');
  const verb = props.isAiDraft ? 'Accept and submit' : 'Submit';
  const what = props.subjectKind === 'intake-request' ? 'request' : props.subjectKind === 'change-request' ? 'change' : props.subjectKind === 'decision' ? 'decision' : 'draft';
  return (
    <ActionSheet
      tool="submit_record" site="pages/work-approvals/parts#SubmitSheet"
      title={`${verb}: ${props.title}`}
      description={props.isAiDraft
        ? `An AI client drafted this ${what} for you. Submitting makes it yours: it is stamped with your name, and the engine checks it and asks the people the rules name.`
        : `The engine checks the ${what} and asks the people the rules name. You can withdraw it while it waits.`}
      trigger={props.trigger ?? (
        // The button names its draft too, so several on My work each say which one they submit (QA walk, wave 4b). The
        // visible words stay the verb.
        <Button size={props.size ?? 'sm'} variant={props.isAiDraft ? 'default' : 'outline'} aria-label={`${verb}: ${props.title}`}>
          <Send aria-hidden />
          {verb}
        </Button>
      )}
      input={() => {
        const text = note.trim();
        return { subjectKind: props.subjectKind, subjectId: props.subjectId, ...(text ? { note: text } : {}) };
      }}
      submitLabel={verb}
      preview
      onDone={() => props.onDone?.()}
      onOpenChange={open => { if (!open) setNote(''); }}
    >
      <div className="flex flex-col gap-1.5 rounded-md border border-line bg-background px-3 py-2.5">
        <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{props.title}</span>
        <AiMark isAiCreated={props.isAiDraft} performedBy={props.performedBy ?? null} />
        {props.isAiDraft && <Muted>Read the draft on its own page before you accept it, if you have not yet.</Muted>}
      </div>
      <Field label="Note" name="note" hint="Optional. Stored with the submission; the people asked can read it.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

export function WithdrawSheet(props: { subjectKind: WithdrawableKind; subjectId: string; title: string; size?: 'sm' | 'default' }) {
  const [reason, setReason] = useState('');
  return (
    <ActionSheet
      tool="withdraw_record" site="pages/work-approvals/parts#WithdrawSheet"
      title={`Withdraw: ${props.title}`}
      description="Withdrawing takes the record back. Open approval requests on it are canceled, and the people asked are no longer asked."
      trigger={(
        <Button size={props.size ?? 'sm'} variant="outline">
          <Undo2 aria-hidden />
          Withdraw
        </Button>
      )}
      input={() => {
        const text = reason.trim();
        if (!text) return { problem: 'Say why you withdraw it.', field: 'reason' };
        return { subjectKind: props.subjectKind, subjectId: props.subjectId, reason: text };
      }}
      submitLabel="Withdraw"
      submitTone="destructive"
      preview
      confirm={{
        title: 'Withdraw this record?',
        body: <p className="m-0">It cannot be submitted again. To try again, propose a new one.</p>,
        confirmLabel: 'Withdraw',
        tone: 'destructive',
      }}
      onOpenChange={open => { if (!open) setReason(''); }}
    >
      <Field label="Reason" name="reason" required hint="Stored on the record. The people who were asked can read it.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}
