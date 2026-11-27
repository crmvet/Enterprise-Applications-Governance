// AI and evidence (P10, UC-09, UC-11, A-EVIDENCE) for one application: what AI clients drafted on it and who took each
// draft forward, what the product refused them, their recommendation-only answers and AI-only code approvals; and the
// evidence pack in numbers, each opening the pack, with the audit trail for those who may read it. AI drafts, a person
// accepts (D-28): a draft waits until a person takes it forward.
// Reads: get_ai_activity (this application), get_evidence (this application).
import { ArrowRight, Bot, FileSearch, ScrollText } from 'lucide-react';
import { evidenceHref, goTo, hrefTo, recordHref } from '../../app/links';
import { Empty, KpiTile, Muted, MoreRowsNote, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import type { AiActivityItem, AiActivityView } from '../../data/contracts';
import { fmtDateTime, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { useApp360, type App360Context } from './context';

const tiles = 'grid grid-cols-3 gap-2.5 medium:grid-cols-2 narrow:grid-cols-2';

export function EvidenceSection() {
  const ctx = useApp360();
  if (!ctx) return null;
  return (
    <div className="grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <AiActivityPanel ctx={ctx} />
      <EvidencePanel ctx={ctx} />
    </div>
  );
}

const KIND_WORDS: Record<AiActivityItem['kind'], string> = {
  draft: 'Draft',
  refusal: 'Refused',
  answer: 'AI answer, a recommendation only',
  'code-approval': 'AI-only code approval',
};

function stateWords(item: AiActivityItem): string | null {
  if (item.kind === 'refusal') return item.refusal ? `Refused: ${item.refusal.rule}` : 'Refused by the product.';
  if (item.kind === 'answer') return item.answer ? `${item.answer.outcome.label}. ${item.answer.countsTowardRequirement ? 'Counted.' : 'Never counts toward an approval.'}` : null;
  if (item.kind === 'code-approval') return 'The only code approval came from an AI. It never counts as authorization.';
  if (item.withdrawn) return `Withdrawn${item.withdrawn.principal ? ` by ${item.withdrawn.principal.displayLabel}` : ''}.`;
  if (item.decided) return `Decided${item.decided.outcome ? `: ${item.decided.outcome.label}` : ''}${item.decided.principal ? `, by ${item.decided.principal.displayLabel}` : ''}.`;
  if (item.takenForward) return `Taken forward by ${item.takenForward.principal?.displayLabel ?? 'a person'}${item.editedByPersonFirst ? ', edited first' : ''}.`;
  if (item.state === 'waiting') return 'Waits for a person to take it forward.';
  return null;
}

function AiActivityPanel({ ctx }: { ctx: App360Context }) {
  const state = useRead('get_ai_activity', { businessApplicationId: ctx.applicationId, top: 20 });
  const data = dataOf(state);
  const go = (view: AiActivityView) => goTo({ page: 'ai-activity', view, applicationId: ctx.applicationId });
  return (
    <Panel
      title="AI activity"
      subtitle={data?.scope === 'mine' ? 'The AI work done for you on this application. The AI lead, the security reviewer, the platform admin and the auditor see all of it.' : 'What AI clients did on this application, and who decided.'}
      state={state}
      count={data?.totalCount}
      skeletonRows={5}
      actions={<TextLink href={hrefTo({ page: 'ai-activity', applicationId: ctx.applicationId })} className="inline-flex items-center gap-1 font-semibold">AI activity <ArrowRight className="size-4" aria-hidden /></TextLink>}
    >
      {d => (
        <div className="flex flex-col gap-3">
          <div className={tiles}>
            <KpiTile label="Drafts" value={d.counts.drafts} caption="Written by an AI client" onClick={() => go('drafts')} />
            <KpiTile label="Waiting" value={d.counts.waiting} caption="For a person to take forward" attention={d.counts.waiting > 0} onClick={() => go('waiting')} />
            <KpiTile label="Taken forward" value={d.counts.takenForwardByPerson} caption="By a person" onClick={() => go('accepted')} />
            <KpiTile label="Refused" value={d.counts.refused} caption="Calls the product refused" attention={d.counts.refused > 0} onClick={() => go('refused')} />
            <KpiTile label="AI answers" value={d.counts.answers} caption="Recommendation only" onClick={() => go('answers')} />
            <KpiTile label="AI-only code approvals" value={d.counts.codeApprovals} caption="Flagged deployments" attention={d.counts.codeApprovals > 0} onClick={() => go('code-approvals')} />
          </div>
          {d.items.length ? (
            <RowList label="AI activity">
              {d.items.map((item, i) => {
                const href = item.record ? recordHref(item.record.recordUrl) : null;
                const what = item.record ? `${item.record.recordNumber ? `${item.record.recordNumber} ` : ''}${item.record.label}` : item.refusal ? `A call to ${item.refusal.tool}` : 'A call';
                const who = [item.clientApp, item.onBehalfOf ? `for ${item.onBehalfOf.displayLabel}` : null].filter(Boolean).join(' ');
                return (
                  <Row
                    key={`${item.kind}-${item.record?.recordId ?? item.refusal?.auditEntryId ?? i}`}
                    lead={<span className="grid size-8 place-items-center rounded-full bg-info-soft text-brand-dark" aria-hidden><Bot className="size-4" /></span>}
                    primary={href ? <TextLink href={href}>{what}</TextLink> : what}
                    secondary={[KIND_WORDS[item.kind], who ? `by ${who}` : null].filter(Boolean).join(', ')}
                    tertiary={stateWords(item) ?? undefined}
                    end={item.record?.status ? <StatusBadge status={item.record.status} /> : item.kind === 'refusal' ? <ToneBadge tone="danger">Refused</ToneBadge> : undefined}
                    endSub={item.occurredOn ? fmtDateTime(item.occurredOn) : undefined}
                  />
                );
              })}
            </RowList>
          ) : <Empty>No AI client has worked on this application{d.scope === 'mine' ? ' for you' : ''}.</Empty>}
          <MoreRowsNote skipToken={d.skipToken} what="AI activity" />
        </div>
      )}
    </Panel>
  );
}

function EvidencePanel({ ctx }: { ctx: App360Context }) {
  const state = useRead('get_evidence', { businessApplicationId: ctx.applicationId });
  const pack = evidenceHref('business-application', ctx.applicationId);
  const open = () => goTo({ page: 'evidence', subjectKind: 'business-application', id: ctx.applicationId });
  return (
    <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
      <Panel
        title="Evidence pack"
        subtitle="Every decision and approval answer, change, deployment, review, document and evidence link of the application, with the configuration version stamped on each."
        state={state}
        skeletonRows={4}
        actions={<Button size="sm" asChild><a href={pack}><FileSearch aria-hidden />Open the pack</a></Button>}
      >
        {d => (
          <div className="flex flex-col gap-3">
            <div className="grid grid-cols-2 gap-2.5">
              <KpiTile label="Decisions" value={d.counts.decisions} caption={plural(d.counts.approvalAnswers, 'approval answer', 'approval answers')} onClick={open} />
              <KpiTile label="Deployments" value={d.counts.deployments} caption={`${d.counts.flaggedDeployments} flagged`} attention={d.counts.flaggedDeployments > 0} onClick={open} />
              <KpiTile label="Changes" value={d.counts.changes} caption="With their result" onClick={open} />
              <KpiTile label="Reviews" value={d.counts.reviews} caption="Due and done" onClick={open} />
              <KpiTile label="Documents" value={d.counts.documents} caption={plural(d.counts.evidenceLinks, 'evidence link', 'evidence links')} onClick={open} />
              <KpiTile label="AI-made rows" value={d.counts.aiCreated} caption="Drafted by an AI client" onClick={open} />
            </div>
            <Muted>
              Configuration in force: version {d.configurationInForce.configurationSetVersion}.
              {d.versionsStamped.length > 0 && ` Versions stamped on its rows: ${d.versionsStamped.join(', ')}.`}
              {d.truncated && ' The pack is long: the evidence page reads it in full.'}
            </Muted>
          </div>
        )}
      </Panel>
      {ctx.can('get_audit_trail') && (
        <Panel title="Audit trail" subtitle="Every write and refusal on the application, who made it and through which client.">
          <Button variant="outline" size="sm" className="self-start" asChild>
            <a href={hrefTo({ page: 'audit', applicationId: ctx.applicationId })}><ScrollText aria-hidden />Open the audit trail</a>
          </Button>
        </Panel>
      )}
    </div>
  );
}
