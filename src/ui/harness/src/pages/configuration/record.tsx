// The configuration draft's record page (/app/records/configuration-draft/<id>, get_configuration_draft): why it was
// proposed, what it changes row by row, the engine's rule check, what it touches (applications, gate levels, gates,
// records in flight, data uses), and its decision with the approvals. Submit, change or withdraw it here; a second
// configuration admin accepts it on the decision page (D-38).
import { ArrowRight, Info, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { goTo, hrefTo, recordHref, recordHrefOf } from '../../app/links';
import { DataStamp, Empty, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { AiMark, StampLine } from '../../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Button } from '../../components/ui/button';
import { Skeleton } from '../../components/ui/skeleton';
import type { ConfigurationDraftDetail } from '../../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { twoColumnClass } from '../owners-reviews/people';
import { Sticky, SubmitSheet, WithdrawSheet } from '../work-approvals/parts';
import { draftTitle } from './history';
import { DRAFT_STATE_TONE, inSentence, operationsFromDiff } from './logic';
import { DataUsesTouched, DiffList, GateChangeList, GatesTouched, RuleCheck, Section } from './parts';
import { ConfigRowsProvider, useConfigRowsInForce } from './rows';
import { DraftBuilderSheet } from './sheets';

export function ConfigurationDraftRecord({ configurationDraftId }: { configurationDraftId: string }) {
  return (
    <ConfigRowsProvider>
      <DraftRecord configurationDraftId={configurationDraftId} />
    </ConfigRowsProvider>
  );
}

function DraftRecord({ configurationDraftId }: { configurationDraftId: string }) {
  const state = useRead('get_configuration_draft', { configurationDraftId });
  if (state.status === 'loading') {
    return (
      <>
        <PageHeader eyebrow="Configuration draft" title="Configuration draft" />
        <div role="status" aria-label="Loading the configuration draft" className="flex flex-col gap-3">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-48 w-full" />
        </div>
      </>
    );
  }
  if (!state.envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Configuration draft" title="Configuration draft" actions={<Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'configuration', view: 'drafts' })}>Every draft</a></Button>} />
        <PanelFailure failure={state.envelope} />
      </>
    );
  }
  return <DraftRecordBody detail={state.envelope.data} envelope={state.envelope} />;
}

function DraftRecordBody({ detail, envelope }: { detail: ConfigurationDraftDetail; envelope: Parameters<typeof DataStamp>[0]['envelope'] }) {
  const session = useSession();
  const config = useConfigRowsInForce();
  const [editing, setEditing] = useState(false);
  const d = detail.draft;
  const title = draftTitle(d);
  const open = d.state.key === 'draft' || d.state.key === 'submitted';
  const canSubmit = d.state.key === 'draft' && session.can('submit_record') && !!d.instructedBy && d.instructedBy.objectId.toLowerCase() === session.objectId;
  // Only the admin who instructed the draft withdraws it; the engine refuses anyone else (a11y review note).
  const canWithdraw = open && session.can('withdraw_record') && !!d.instructedBy && d.instructedBy.objectId.toLowerCase() === session.objectId;
  const canChange = d.state.key === 'draft' && session.can('propose_configuration_change');
  const decisionHref = detail.decision ? recordHref(detail.decision.recordUrl) ?? recordHrefOf('decision', detail.decision.decisionId) : null;
  const errors = detail.validation.errors;
  const warnings = detail.validation.warnings;

  return (
    <>
      <PageHeader
        eyebrow="Configuration draft"
        title={title}
        status={<ToneBadge tone={DRAFT_STATE_TONE[d.state.key]}>{d.state.label}</ToneBadge>}
        subtitle={d.changeSummary[0] ?? 'A proposed change to the rules. Nothing changes until a person accepts it.'}
        meta={(
          <span className="flex flex-col gap-1">
            <span>
              {[
                d.instructedBy ? `Instructed by ${d.instructedBy.displayLabel}` : null,
                `from version ${d.baseVersion}`,
                d.submittedOn ? `submitted ${fmtDateTime(d.submittedOn)}` : null,
                d.expiresOn && d.state.key === 'submitted' ? `expires ${fmtDateTime(d.expiresOn)}` : null,
              ].filter(Boolean).join(', ')}.
            </span>
            <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
            <StampLine performedBy={d.performedBy} />
            <span><DataStamp envelope={envelope} /></span>
          </span>
        )}
        actions={(
          <>
            <Sticky show={canSubmit}>
              <SubmitSheet subjectKind="configuration-draft" subjectId={d.configurationDraftId} title={title} isAiDraft={d.isAiCreated} performedBy={d.performedBy} size="default" />
            </Sticky>
            {canChange && (
              <Button variant="outline" onClick={() => setEditing(true)}>Change the draft</Button>
            )}
            <Sticky show={canWithdraw}>
              <WithdrawSheet subjectKind="configuration-draft" subjectId={d.configurationDraftId} title={title} size="default" />
            </Sticky>
            {decisionHref && (
              <Button asChild variant={d.state.key === 'submitted' ? 'default' : 'outline'}>
                <a href={decisionHref}>The decision page<ArrowRight aria-hidden /></a>
              </Button>
            )}
          </>
        )}
      />
      {canChange && (
        <DraftBuilderSheet
          open={editing}
          onOpenChange={setEditing}
          title={`Change ${title.charAt(0).toLowerCase()}${title.slice(1)}`}
          configurationDraftId={d.configurationDraftId}
          operations={operationsFromDiff(detail.diff)}
          rationale={detail.proposalRationale ?? ''}
          onSaved={() => setEditing(false)}
        />
      )}

      <div className="mb-4 flex flex-col gap-3 narrow:mb-3">
        {detail.baseMoved && (
          <Alert variant="warning">
            <TriangleAlert aria-hidden />
            <AlertTitle>{`The rules moved on to version ${detail.activeVersion}`}</AlertTitle>
            <AlertDescription>
              <p className="m-0">
                {`This draft started from version ${d.baseVersion}. What it changes below is worked out against version ${detail.activeVersion}, the version in force. When it is accepted, the engine checks it again and discards it if it no longer fits; then propose it again from the version in force.`}
              </p>
            </AlertDescription>
          </Alert>
        )}
        {d.isAiCreated && d.state.key === 'draft' && (
          <Alert variant="info">
            <Info aria-hidden />
            <AlertTitle>An AI client drafted this change</AlertTitle>
            <AlertDescription>
              <p className="m-0">Read what it changes before you submit it. Submitting makes it yours; another configuration admin then accepts or rejects it on the decision page.</p>
            </AlertDescription>
          </Alert>
        )}
      </div>

      <div className={twoColumnClass}>
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel title="Why it was proposed">
            {detail.proposalRationale
              ? <p className="m-0 text-sm whitespace-pre-wrap text-navy [overflow-wrap:anywhere]">{detail.proposalRationale}</p>
              : <Empty>No reason was given.</Empty>}
          </Panel>
          <Panel title="What it changes" count={detail.diff.length} subtitle={open ? 'Against the version in force now.' : 'Against the version it was judged under.'}>
            {d.changeSummary.length > 0 && (
              <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
                {d.changeSummary.map((l, i) => <li key={i}>{l}</li>)}
              </ul>
            )}
            <DiffList diff={detail.diff} lookup={config.lookup} empty="The draft changes no row." />
          </Panel>
          <Panel title="Rules check">
            <RuleCheck valid={detail.validation.isValid} errors={errors} warnings={warnings} labelOfAnyKey={config.labelOfAnyKey} saved />
          </Panel>
        </div>

        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <DecisionPanel detail={detail} />
          <Panel title="What it touches">
            <Section title="Live applications whose rules change">
              {!detail.impact.applicationsAffected ? <Muted>None.</Muted> : (
                <>
                  <ul className="m-0 flex list-none flex-col gap-1 p-0">
                    {detail.impact.applications.map(a => {
                      const href = recordHref(a.recordUrl);
                      return (
                        <li key={a.businessApplicationId} className="flex flex-wrap items-baseline gap-x-2 text-sm">
                          {href ? <TextLink href={href}>{a.name}</TextLink> : <span>{a.name}</span>}
                          {a.tier && <span className="text-xs text-navy-3">{a.tier.label}</span>}
                        </li>
                      );
                    })}
                  </ul>
                  {detail.impact.applicationsAffected > detail.impact.applications.length && (
                    <Muted>{`And ${detail.impact.applicationsAffected - detail.impact.applications.length} more.`}</Muted>
                  )}
                </>
              )}
            </Section>
            <Section title="Gate levels">
              <GateChangeList changes={detail.impact.gateChanges} lookup={config.lookup} />
            </Section>
            <Section title="Gates whose requirements change">
              <GatesTouched gates={detail.impact.gatesTouched} />
            </Section>
            <Section title="Records in flight">
              <p className="m-0 text-sm text-navy">
                {detail.impact.recordsInFlight
                  ? `${plural(detail.impact.recordsInFlight, 'record already submitted keeps the rules in force when it was submitted', 'records already submitted keep the rules in force when each was submitted')}.`
                  : 'No submitted record waits.'}
              </p>
            </Section>
            <Section title="Data uses">
              <DataUsesTouched diff={detail.diff} uses={detail.impact.dataUses} />
            </Section>
          </Panel>
        </div>
      </div>
    </>
  );
}

function DecisionPanel({ detail }: { detail: ConfigurationDraftDetail }) {
  const d = detail.draft;
  const decision = detail.decision;
  const href = decision ? recordHref(decision.recordUrl) ?? recordHrefOf('decision', decision.decisionId) : null;
  const a = detail.approvals;
  const next = d.state.key === 'draft'
    ? 'Submit the draft to open its decision. The people the rules name are then asked on the decision page.'
    : d.state.key === 'submitted'
      ? 'A second configuration admin answers on the decision page. Nothing changes until the decision is accepted.'
      : d.state.key === 'activated'
        ? `Accepted: it became version ${d.resultingVersion ?? detail.activeVersion}.`
        // A reason that ends with its own full stop gets no second one ("...the new quarter.. It changed nothing.").
        : `Ended${d.discardedOn ? ` on ${fmtDate(d.discardedOn)}` : ''}${d.discardReason ? `: ${d.discardReason.trim().replace(/[.!?]+$/, '')}` : ''}. It changed nothing.`;
  return (
    <Panel title="Decision and approvals">
      {decision ? (
        <span className="flex flex-wrap items-center gap-2 text-sm">
          {href ? <TextLink href={href}>{decision.recordNumber ?? 'The decision'}</TextLink> : <span>{decision.recordNumber ?? 'The decision'}</span>}
          <span className="text-navy-2">{decision.decisionType.label}</span>
          <StatusBadge status={decision.status} />
        </span>
      ) : <Muted>No decision yet: the draft opens one when it is submitted.</Muted>}
      {a && (
        <dl className="m-0 grid grid-cols-4 gap-2 narrow:grid-cols-2">
          {([['Needed', a.required], ['Approved', a.approved], ['Rejected', a.rejected], ['Waiting', a.pending]] as const).map(([label, n]) => (
            <div key={label} className="flex flex-col rounded-md border border-line px-2 py-1.5">
              <dt className="text-xs text-navy-3">{label}</dt>
              <dd className="m-0 text-lg font-semibold text-navy tabular-nums">{n}</dd>
            </div>
          ))}
        </dl>
      )}
      {detail.acceptedBy.length > 0 && (
        <Section title="Accepted by">
          <ul className="m-0 flex list-none flex-col gap-0.5 p-0 text-sm text-navy">
            {detail.acceptedBy.map((p, i) => (
              <li key={i}>
                {p.principal?.displayLabel ?? 'A person'}
                <span className="text-xs text-navy-3">{[p.askedAs ? `as ${inSentence(p.askedAs.label)}` : null, p.answeredOn ? fmtDateTime(p.answeredOn) : null].filter(Boolean).map(t => `, ${t}`).join('')}</span>
              </li>
            ))}
          </ul>
        </Section>
      )}
      <p className="m-0 text-sm text-navy-2">{next}</p>
      {d.state.key === 'activated' && (
        <Button variant="outline" size="sm" className="self-start" onClick={() => goTo({ page: 'configuration', view: 'versions' })}>Every version</Button>
      )}
    </Panel>
  );
}
