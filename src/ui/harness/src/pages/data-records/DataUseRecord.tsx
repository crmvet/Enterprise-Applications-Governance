// The data use record (/app/records/data-use/<id>): one use (O27, DU-0001) of one application, with its decision.
// Shows the application, the domain path and classification, the location, the access, the purpose, the AI flag and
// service, de-identified or not, the state with "1 of 2 approvals", the governing rule and where it is set (the engine's
// own words from check_data_use), who is or would be asked, the decisions about the use with their approvals, and the
// AI mark "Drafted by Claude Code for Priya Castell".
// Reads: get_data_domain (uses, decisions; the use is found in its domain, see reads.ts useRowInDomain), check_data_use
// (the rule, the approvers, the engine's explanation, whether a save is refused), get_decision (the open decision's
// approvals). Writes, each in the shared side sheet: confirm an AI draft, change or end the use (DataUseSheet,
// save_data_use), propose its decision (DataDecisionSheet), send it for decision (submit_record), withdraw a draft
// (withdraw_record). The engine checks every one; a use whose rule is never is refused when saved (G-25).
import { ArrowRight, Pencil, ShieldCheck, Square, Undo2 } from 'lucide-react';
import { hrefTo, recordHrefOf, useQueryParam } from '../../app/links';
import { DataStamp, Empty, Muted, PageHeader, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ClassificationBadge, DataRuleChip, DataUseStateBadge, DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { AiMark, StampLine } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import { Skeleton } from '../../components/ui/skeleton';
import type { DataApprover, DataDomainDetail, DataUseCheck, DataUseItem, DecisionDetail, Success } from '../../data/contracts';
import { DATA_ACCESS_TEXT, DATA_USE_STATE_HELP } from '../../data/dataTaxonomy';
import { fmtDate, fmtDateTime } from '../../data/format';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { tabHref } from '../app360/nav';
import { Fact, Facts } from '../changes-records/parts';
import { DataDecisionSheet, DataUseSheet } from '../data/sheets';
import { askedOfText, cancelReasonText, notSubmitted, proposedByMe, recordOutcomeText } from '../work-approvals/logic';
import { Sticky, SubmitSheet, WithdrawSheet } from '../work-approvals/parts';
import { aiText, decisionsAbout, deidentifiedText, isDraftRow, KEY_PATTERN, needsDecision } from './logic';
import { ApproverList, GoverningRuleLine, NeverRuleAlert, RecordFrame, subhead, useCanWrite } from './parts';
import { useRowInDomain } from './reads';

const grid = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

const VERB: Record<DataUseItem['access'], string> = { read: 'reads', write: 'writes', 'read-write': 'reads and writes' };

export function DataUseRecord({ dataUseId }: { dataUseId: string }) {
  const hint = useQueryParam('domain', KEY_PATTERN);
  const lookup = useRowInDomain<DataUseItem>({
    entity: 'data-use',
    id: dataUseId,
    hint,
    sections: ['uses', 'decisions'],
    pick: d => d.uses,
    idOf: u => u.dataUseId,
  });
  if (lookup.status !== 'found') {
    return (
      <RecordFrame
        eyebrow="Data use"
        title="Data use"
        state={lookup.status === 'failed' ? lookup.failure : lookup.status}
        back={{ label: 'All data uses', href: hrefTo({ page: 'data', tab: 'uses' }) }}
        gone={lookup.status === 'gone' && (
          <>
            The product lists open uses only, and this one has ended or was withdrawn.
            {lookup.domainKey && <> Its data is <TextLink href={hrefTo({ page: 'data-domain', key: lookup.domainKey })}>{lookup.detail?.data.domain.path ?? 'its domain'}</TextLink>; the domain lists the uses open now.</>}
          </>
        )}
      />
    );
  }
  return <UseBody use={lookup.item} detail={lookup.detail} />;
}

function UseBody({ use, detail }: { use: DataUseItem; detail: Success<DataDomainDetail> }) {
  const session = useSession();
  const canWrite = useCanWrite();
  const check = useRead('check_data_use', {
    applicationId: use.businessApplicationId,
    domainKey: use.domain.key,
    ...(use.location ? { locationId: use.location.dataLocationId } : {}),
    access: use.access,
    reachesAi: use.reachesAi,
    isDeidentified: use.isDeidentified,
  });
  const decisionState = useRead('get_decision', use.decisionId ? { decisionId: use.decisionId } : null);
  const decision = dataOf(decisionState);
  const checked = dataOf(check);
  const decisions = decisionsAbout(detail.data.decisions, 'data-use', use.dataUseId);
  const open = decisions.find(d => d.status.meaning === 'proposed') ?? null;
  const draft = isDraftRow(use);
  const ended = !!use.endedOn;
  const breaks = use.state === 'breaks-rule' || checked?.refusedWhenSaved === true;
  const mineUnsent = !!decision && notSubmitted(decision) && proposedByMe(decision, session.objectId);
  const canEdit = canWrite('save_data_use') && !ended;
  const canPropose = canWrite('save_decision_proposal') && !draft && !ended && !open && needsDecision(use.state);
  const canSend = canWrite('submit_record') && !draft && !ended && (mineUnsent || (!open && needsDecision(use.state)));
  const canWithdraw = canWrite('withdraw_record') && draft;
  const title = `${use.applicationName} ${VERB[use.access]} ${use.domain.label}`;
  const label = `${use.recordNumber ?? 'Data use'}: ${title}`;
  const domain = detail.data.domain;

  return (
    <>
      <PageHeader
        eyebrow={use.recordNumber ? `Data use ${use.recordNumber}` : 'Data use'}
        title={title}
        status={(
          <span className="flex flex-wrap items-center gap-1.5">
            <StatusBadge status={use.status} />
            <DataUseStateBadge state={use.state} approvalsGiven={use.approvalsGiven} approvalsNeeded={use.approvalsNeeded} />
          </span>
        )}
        subtitle={use.purpose}
        meta={(
          <span className="flex flex-col gap-0.5">
            <AiMark isAiCreated={use.isAiCreated} performedBy={use.performedBy} className="self-start" />
            <StampLine performedBy={use.performedBy} />
            <span><DataStamp envelope={detail} /></span>
          </span>
        )}
        actions={(
          <>
            <Sticky show={canEdit && draft}>
              <DataUseSheet applicationId={use.businessApplicationId} applicationName={use.applicationName} mode="confirm" use={use} trigger={<Button size="sm"><ShieldCheck aria-hidden />Check and confirm</Button>} />
            </Sticky>
            <Sticky show={canEdit}>
              <DataUseSheet applicationId={use.businessApplicationId} applicationName={use.applicationName} mode="edit" use={use} trigger={<Button size="sm" variant="outline"><Pencil aria-hidden />Change</Button>} />
            </Sticky>
            <Sticky show={canEdit && !draft}>
              <DataUseSheet applicationId={use.businessApplicationId} applicationName={use.applicationName} mode="end" use={use} trigger={<Button size="sm" variant="outline"><Square aria-hidden />End the use</Button>} />
            </Sticky>
            <Sticky show={canPropose}>
              <DataDecisionSheet subjectKind="data-use" subjectId={use.dataUseId} decisionTypeKey="data-use" label={label} trigger={<Button size="sm" variant="outline">Propose a decision</Button>} />
            </Sticky>
            <Sticky show={canSend}>
              <SubmitSheet subjectKind="data-use" subjectId={use.dataUseId} title={label} isAiDraft={false} size="sm" />
            </Sticky>
            <Sticky show={canWithdraw}>
              <WithdrawSheet subjectKind="data-use" subjectId={use.dataUseId} title={label} />
            </Sticky>
          </>
        )}
      />

      {breaks && (
        <div className="mb-4">
          <NeverRuleAlert rule={checked?.governingRule ?? null} explanation={checked?.explanation ?? null} domainPath={use.domain.path} saved />
        </div>
      )}
      {draft && (
        <p className="mt-0 mb-4 flex items-start gap-1.5 text-sm text-navy-2">
          <Undo2 className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>
            A draft is not a fact yet: nobody is asked about it, and no decision opens, until a person checks and confirms it.
            An AI client may draft a use; a person confirms it.
          </span>
        </p>
      )}

      <div className={grid}>
        <div className={stack}>
          <Panel title="The use">
            <Facts>
              <Fact label="Application">
                <TextLink href={tabHref(use.businessApplicationId, 'data')}>{use.applicationName}</TextLink>
              </Fact>
              <Fact label="Data" sub={domain.level?.label}>
                <DomainLink domain={use.domain} />
              </Fact>
              <Fact label="Classification">
                <ClassificationBadge classification={checked?.classification ?? domain.classification} domainKey={use.domain.key} />
              </Fact>
              <Fact label="Where" sub={use.location ? [use.location.platform.label, use.location.stage?.label].filter(Boolean).join(', ') : undefined}>
                {use.location
                  ? <TextLink href={recordHrefOf('data-location', use.location.dataLocationId) ?? '#'}>{use.location.label}</TextLink>
                  : 'No location named'}
              </Fact>
              <Fact label="Access">{DATA_ACCESS_TEXT[use.access]}</Fact>
              <Fact label="AI">{aiText(use)}</Fact>
              <Fact label="De-identified">{deidentifiedText(use.isDeidentified)}</Fact>
              {use.accessApproach && <Fact label="How it reaches the data">{use.accessApproach.label}</Fact>}
              <Fact label="Started">{fmtDate(use.startOn, 'Not recorded')}</Fact>
              {use.endedOn && <Fact label="Ended">{fmtDate(use.endedOn)}</Fact>}
              <Fact label="Purpose" wide>{use.purpose || 'Not given'}</Fact>
            </Facts>
          </Panel>

          <Panel title="Decision state" subtitle="Computed by the engine from the rules in force and the decision. It is never stored.">
            <div className="flex flex-col gap-3">
              <div className="flex flex-col gap-1">
                <span className="flex flex-wrap items-center gap-2">
                  <DataUseStateBadge state={use.state} approvalsGiven={use.approvalsGiven} approvalsNeeded={use.approvalsNeeded} />
                  {use.approvalsNeeded > 0 && <Muted>{`${use.approvalsNeeded} ${use.approvalsNeeded === 1 ? 'approval' : 'approvals'} needed`}</Muted>}
                </span>
                <span className="text-sm text-navy-2">{DATA_USE_STATE_HELP[use.state]}</span>
              </div>
              {check.status === 'loading' ? (
                <div role="status" aria-label="Reading the rule" className="flex flex-col gap-2"><Skeleton className="h-4 w-full" /><Skeleton className="h-4 w-2/3" /></div>
              ) : !check.envelope.ok ? (
                <Muted>{`The rule that decides it could not be read: ${check.envelope.error.message} Reference ${check.envelope.correlationId}.`}</Muted>
              ) : (
                <CheckedRule use={use} checked={check.envelope.data} asked={decision?.data?.asked ?? null} />
              )}
            </div>
          </Panel>
        </div>

        <div className={stack}>
          <Panel
            title="Decisions about this use"
            count={decisions.length}
            subtitle="Open first. Approve or reject on the decision page."
          >
            {decisions.length ? (
              <RowList label="Decisions about this use">
                {decisions.map(d => (
                  <Row
                    key={d.decisionId}
                    primary={<TextLink href={hrefTo({ page: 'decision', id: d.decisionId })}>{d.statement || d.decisionType.label}</TextLink>}
                    secondary={[d.recordNumber, `${d.decisionType.label} decision`, d.outcome ? `Outcome: ${d.outcome.label}` : null].filter(Boolean).join('. ')}
                    tertiary={d.decidedOn ? `Decided ${fmtDateTime(d.decidedOn)}` : undefined}
                    end={<StatusBadge status={d.status} />}
                  />
                ))}
              </RowList>
            ) : (
              <Empty>{needsDecision(use.state) ? 'No decision yet. The rule in force asks for one.' : 'No decision is recorded about this use.'}</Empty>
            )}
          </Panel>
          {decision && <DecisionApprovals detail={decision} />}
          <Panel title="The domain's rules" subtitle={`Every use of ${domain.path} follows them, unless a domain below sets its own.`}>
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
              {(['use', 'ai', 'copy'] as const).map(name => (
                <li key={name}><DataRuleChip name={name} rule={domain.rules[name]} /></li>
              ))}
            </ul>
            <TextLink href={hrefTo({ page: 'data-domain', key: domain.key })} className="inline-flex items-center gap-1 self-start text-sm">
              The domain, its owners and its other uses <ArrowRight className="size-3.5" aria-hidden />
            </TextLink>
          </Panel>
        </div>
      </div>
    </>
  );
}

// The rule that decides the use, in the engine's words, the tier answer the data implies, and who is or would be asked.
function CheckedRule({ use, checked, asked }: { use: DataUseItem; checked: DataUseCheck; asked: DataApprover[] | null }) {
  return (
    <>
      <div className="flex flex-col gap-1">
        <h3 className={subhead}>The rule that decides it</h3>
        <GoverningRuleLine rule={checked.governingRule} sentence={!checked.explanation} />
        {checked.explanation && <span className="text-sm text-navy-2">{checked.explanation}</span>}
        <Muted>Under configuration version {checked.configurationSetVersion}.</Muted>
      </div>
      {checked.impliedAnswer && (
        <p className="m-0 text-sm text-navy-2">
          The data implies the tier answer <span className="font-semibold text-navy">{checked.impliedAnswer.label}</span>.{' '}
          <TextLink href={tabHref(use.businessApplicationId, 'tier')}>Tier answers</TextLink>
        </p>
      )}
      {use.state === 'breaks-rule' || checked.refusedWhenSaved ? (
        <Muted>No decision can allow it, so nobody is asked.</Muted>
      ) : (
        <div className="flex flex-col gap-1.5">
          <h3 className={subhead}>{asked?.length ? 'Who is asked' : 'Who would be asked'}</h3>
          <ApproverList
            approvers={asked?.length ? asked : checked.approvers}
            label="Approvers"
            empty={checked.decisionNeeded ? 'The rules name no approver.' : 'No decision is needed, so nobody is asked.'}
          />
        </div>
      )}
    </>
  );
}

// The approvals of the use's current decision: each request, who it asks, and the answer.
function DecisionApprovals({ detail }: { detail: DecisionDetail }) {
  const records = detail.approvals.records;
  const data = detail.data ?? null;
  return (
    <Panel
      title="Approvals"
      count={records.length}
      subtitle={detail.decision.status.meaning === 'proposed' ? 'Of the open decision. Each person asked answers on the decision page.' : 'Of the latest decision.'}
      actions={<TextLink href={hrefTo({ page: 'decision', id: detail.decision.decisionId })} className="text-sm font-semibold">Decision page</TextLink>}
    >
      {records.length ? (
        <RowList label="Approval requests">
          {records.map(r => (
            <Row
              key={r.approvalRecordId}
              primary={askedOfText({ ...r, data })}
              secondary={r.outcome.meaning === 'pending'
                ? (r.requestedOn ? `Asked ${fmtDateTime(r.requestedOn)}.` : 'Asked.')
                : recordOutcomeText(r, cancelReasonText(r, detail.decision.status.meaning, records))}
              badges={r.isConsultation ? <ToneBadge tone="informative" icon={null}>Consulted, advice only</ToneBadge> : undefined}
              end={<StatusBadge status={r.outcome} />}
            />
          ))}
        </RowList>
      ) : (
        <Empty>{notSubmitted(detail) ? 'Nobody is asked yet: the decision is not sent for approval.' : 'No approval request is on record.'}</Empty>
      )}
    </Panel>
  );
}
