// "Ask again" (renew_approval_request, tool 100; document 24 B24-22, choice 3).
// Under a request that expired with nobody asked again, or one with fewer than 48 hours left, the proposer of the
// decision and a platform admin see "Ask again", with the check first, a confirm step and the engine's answer, which
// names who is asked now and until when. Anyone else sees no button. A request that expired and was asked again
// already (by a person, or by the daily refresh's restart) says so in one line instead, for everyone.
//   AskAgainAction   the decision page's hook (pages/Decision.tsx RecordRow; the page is frozen, the props are fixed)
//   AskAgainSheet    the sheet itself, which My work (pages/Work.tsx) uses for the requests waiting on others
import { RotateCcw } from 'lucide-react';
import type { ReactElement, ReactNode } from 'react';
import { ActionSheet } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import type { DecisionApprovalRecord, DecisionDetail, RenewApprovalRequestResult } from '../../data/contracts';
import { fmtDateTime } from '../../data/format';
import { useSession } from '../../data/session';
import { Sticky } from '../work-approvals/parts';
import { askedAgainBy, askedWords, mayAskAgain, renewState, type RenewState } from './logic';

export function AskAgainAction({ record, detail, now }: { record: DecisionApprovalRecord; detail: DecisionDetail; now: number }): ReactNode {
  const session = useSession();
  const state = renewState(record, detail.decision.status.meaning, now);
  const again = state ? askedAgainBy(record, detail.approvals.records, now) : null;
  const offer = !!state && !again && mayAskAgain(session, detail.decision.proposedBy);
  const asked = askedWords({ askedPerson: record.askedPrincipal, askedRole: record.askedRole, askedOwnerRole: record.askedOwnerRole, isConsultation: record.isConsultation });
  return (
    <>
      {state === 'expired' && again && (
        <span className="text-navy-2">
          {`Asked again${again.requestedOn ? ` ${fmtDateTime(again.requestedOn)}` : ''}: the newer request is open${again.expiresOn ? ` until ${fmtDateTime(again.expiresOn)}` : ''}.`}
        </span>
      )}
      {/* Sticky keeps the sheet open on the engine's answer after the page reads again and the request is closed. */}
      <Sticky show={offer}>
        <span className="mt-1 flex flex-wrap items-center gap-2">
          <AskAgainSheet
            approvalRecordId={record.approvalRecordId}
            state={state ?? 'expired'}
            asked={asked}
            expiresOn={record.expiresOn}
            decision={{ recordNumber: detail.decision.recordNumber, statement: detail.decision.statement }}
          />
          <Muted>{state === 'expired' ? 'It expired with nobody asked again.' : 'Fewer than 48 hours are left to answer.'}</Muted>
        </span>
      </Sticky>
    </>
  );
}

export function AskAgainSheet(props: {
  approvalRecordId: string;
  state: RenewState;
  // Who the request asks, in words ("the change manager").
  asked: string;
  expiresOn: string | null;
  decision: { recordNumber: string | null; statement: string };
  trigger?: ReactElement;
  onDone?: (result: RenewApprovalRequestResult) => void;
}) {
  const name = props.decision.recordNumber ? `decision ${props.decision.recordNumber}` : 'the decision';
  const when = props.expiresOn ? fmtDateTime(props.expiresOn) : null;
  const what = props.state === 'expired'
    ? `The request to ${props.asked} expired${when ? ` on ${when}` : ''} with no answer.`
    : `The request to ${props.asked} expires${when ? ` on ${when}` : ' soon'}, and nobody has answered it yet.`;
  return (
    <ActionSheet
      tool="renew_approval_request" site="pages/demo-admin/AskAgain#AskAgainSheet"
      title="Ask again"
      description={`${what} Asking again opens a new request to the same people, with the time the rule gives.`}
      trigger={props.trigger ?? (
        <Button size="sm" variant="outline" aria-label={`Ask again: ${props.asked}, on ${name}`}>
          <RotateCcw aria-hidden />
          Ask again
        </Button>
      )}
      input={() => ({ approvalRecordId: props.approvalRecordId })}
      submitLabel="Ask again"
      preview="Check first"
      confirm={{
        title: 'Ask again?',
        body: props.state === 'expired'
          ? `The expired request stays on record. A new request goes to ${props.asked}, who answers on the decision page.`
          : `The open request is canceled now and a new one goes to ${props.asked}, who answers on the decision page. The canceled request stays on record.`,
        confirmLabel: 'Ask again',
      }}
      onDone={data => props.onDone?.(data)}
    >
      <div className="flex flex-col gap-1 rounded-md border border-line bg-background px-3 py-2.5">
        <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{props.decision.recordNumber ? `Decision ${props.decision.recordNumber}` : 'The decision'}</span>
        <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{props.decision.statement || 'A decision without a statement'}</span>
      </div>
      <p className="m-0 text-sm leading-relaxed text-navy-2">
        The proposer of the decision and a platform admin may ask again. The engine checks that the request expired or has
        fewer than 48 hours left, that the decision is still proposed, and that nobody was asked again already. Check
        first to see who would be asked and until when, without changing anything.
      </p>
    </ActionSheet>
  );
}
