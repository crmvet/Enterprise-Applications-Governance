// Tier (P02, UC-02): the latest tier assessment with each answer, the tier it implies and the answers that drive the
// result; the Microsoft vocabulary next to the tier (Power Platform support tier, Copilot Studio zone, agent risk
// tier); the tier decision; what the tier needs (documents and review cadences) and what the answers trigger; the older
// assessments. A person answers the questions again and proposes the tier decision; only an accepted tier decision
// changes the tier the application holds.
// Reads: get_tier_assessment, get_configuration (vocabulary-mapping, gate-requirement labels), get_requirements when no
// assessment is recorded yet.
import { Bot, CircleAlert, ClipboardList, Plus } from 'lucide-react';
import { recordHref, recordHrefOf } from '../../app/links';
import { Empty, Muted, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ExternalLink, Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import type { TierAssessmentDetail } from '../../data/contracts';
import { plural } from '../../data/format';
import { useLabels, useListValues } from '../../data/labels';
import { useConfigRows, useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { useApp360, type App360Context } from './context';
import { answersOf } from './logic';
import { KeepMounted } from './parts';
import { ProposeDecisionSheet, TierAnswersSheet } from './sheets';

const twoColumns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const column = 'flex min-w-0 flex-col gap-4 narrow:gap-3';
const subhead = 'm-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

export function TierSection() {
  const ctx = useApp360();
  if (!ctx) return null;
  return <TierBody ctx={ctx} />;
}

function TierBody({ ctx }: { ctx: App360Context }) {
  const state = useRead('get_tier_assessment', { businessApplicationId: ctx.applicationId });
  const detail = dataOf(state);
  const app = ctx.card?.application ?? null;
  const computed = detail?.assessment?.computedTier ?? null;
  const applied = app?.tier ?? null;
  const openTier = ctx.appDecisions.find(d => d.decisionType.key === 'tier' && d.status.meaning === 'proposed') ?? null;
  const canAnswer = ctx.can('answer_tier_questions');
  const canPropose = ctx.proposable.includes('tier');

  return (
    <div className="flex flex-col gap-4 narrow:gap-3">
      <div className={twoColumns}>
        <div className={column}>
          <Panel
            title="Tier assessment"
            subtitle="The latest answers, the tier each implies, and the answers that set the result."
            state={state}
            skeletonRows={6}
            actions={canAnswer && app ? (
              <TierAnswersSheet
                applicationId={ctx.applicationId}
                kindKey={app.kind.key}
                kindLabel={app.kind.label}
                current={answersOf(detail)}
                trigger={<Button size="sm" variant={detail?.assessment ? 'outline' : 'default'}><ClipboardList aria-hidden />{detail?.assessment ? 'Answer again' : 'Answer the tier questions'}</Button>}
              />
            ) : undefined}
          >
            {d => <AssessmentBody detail={d} appliedLabel={applied?.label ?? null} />}
          </Panel>
          <NeedsPanel ctx={ctx} detail={detail} />
        </div>
        <div className={column}>
          <Panel title="The tier" subtitle="The tier the application holds, and the decision that set it.">
            <div className="flex flex-wrap items-end gap-x-6 gap-y-2">
              <TierFigure label="Holds now" value={applied?.label ?? 'No tier yet'} />
              <TierFigure label="Latest answers compute" value={computed?.label ?? (state.status === 'loading' ? 'Reading' : 'Not assessed')} />
            </div>
            {detail?.differsFromApplied && (
              <Alert variant="warning">
                <CircleAlert aria-hidden />
                <AlertTitle>The answers point to another tier</AlertTitle>
                <AlertDescription><p className="m-0">Only an accepted tier decision changes the tier. Propose one to confirm, raise or lower it.</p></AlertDescription>
              </Alert>
            )}
            {applied?.decisionId && (
              <Muted>Set by <TextLink href={recordHrefOf('decision', applied.decisionId) ?? '#'}>its tier decision</TextLink>.</Muted>
            )}
            {openTier && (
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <span>A tier decision waits:</span>
                <TextLink href={recordHref(openTier.recordUrl) ?? '#'}>{openTier.statement}</TextLink>
                <StatusBadge status={openTier.status} />
                <AiMark isAiCreated={openTier.isAiCreated} performedBy={openTier.performedBy} />
              </div>
            )}
            {canPropose && (
              <KeepMounted show={!openTier} className="block">
                <ProposeDecisionSheet
                  applicationId={ctx.applicationId}
                  applicationName={ctx.applicationName}
                  typeKey="tier"
                  computedTierKey={computed?.key ?? null}
                  appliedTierKey={applied?.key ?? null}
                  statement={computed ? `${ctx.applicationName} is ${computed.label}.` : ''}
                  title="Propose a tier decision"
                  description={<>Confirm the tier the answers compute, or raise or lower it with a reason. The approvers the rules name for the tier at stake answer it; only then does the tier change.</>}
                  trigger={<Button size="sm"><Plus aria-hidden />Propose a tier decision</Button>}
                  onProposed={ctx.onProposed}
                />
              </KeepMounted>
            )}
          </Panel>
          <VocabularyPanel tierKey={computed?.key ?? applied?.key ?? null} tierLabel={computed?.label ?? applied?.label ?? null} fromAssessment={detail?.vocabulary ?? null} />
          {detail && detail.history.length > 1 && (
            <Panel title="Earlier assessments" count={detail.history.length - 1}>
              <RowList label="Earlier assessments">
                {detail.history.slice(1).map(h => (
                  <Row
                    key={h.tierAssessmentId}
                    primary={h.computedTier?.label ?? 'No tier computed'}
                    secondary={h.assessedBy ? `Assessed by ${h.assessedBy.displayLabel}` : 'Assessed before the engine stamped who'}
                    badges={h.isAiCreated ? <Badge variant="brand"><Bot aria-hidden />Drafted by an AI client</Badge> : undefined}
                    end={h.tierDecisionId ? <TextLink href={recordHrefOf('decision', h.tierDecisionId) ?? '#'}>Its decision</TextLink> : undefined}
                  />
                ))}
              </RowList>
            </Panel>
          )}
        </div>
      </div>
    </div>
  );
}

function TierFigure({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-navy-3">{label}</span>
      <span className="text-2xl leading-8 font-semibold text-navy">{value}</span>
    </div>
  );
}

function AssessmentBody({ detail, appliedLabel }: { detail: TierAssessmentDetail; appliedLabel: string | null }) {
  const requirementLabels = useLabels('gate-requirement');
  const a = detail.assessment;
  if (!a) {
    return (
      <Empty>
        No tier assessment is recorded yet{appliedLabel ? `; the application holds ${appliedLabel} from an earlier decision` : ''}. Answer the tier questions
        to get one.
      </Empty>
    );
  }
  const triggers = [...detail.triggers.reviewTypes.map(r => r.label), ...detail.triggers.gateRequirementKeys.map(k => requirementLabels.label(k))];
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm text-navy-2">Computes</span>
        <ToneBadge tone="brand" icon={null}>{a.computedTier.label}</ToneBadge>
        {a.isFrozen && <ToneBadge tone="subtle">Frozen by its tier decision</ToneBadge>}
        <AiMark isAiCreated={a.isAiCreated} performedBy={a.performedBy} />
      </div>
      {a.assessedBy && <Muted>Answered by {a.assessedBy.displayLabel}.</Muted>}
      {detail.answers.length ? (
        <RowList label="Answers">
          {detail.answers.map(x => (
            <Row
              key={x.questionKey}
              primary={x.question}
              secondary={x.answer}
              badges={x.isDriving ? <ToneBadge tone="warning" icon={null}>Sets the tier</ToneBadge> : undefined}
              end={x.impliedTier ? <span className="text-xs whitespace-nowrap text-navy-2">Implies {x.impliedTier.label}</span> : undefined}
            />
          ))}
        </RowList>
      ) : <Empty>The assessment holds no answers.</Empty>}
      {detail.missingQuestionKeys.length > 0 && (
        <Muted>{plural(detail.missingQuestionKeys.length, 'question is', 'questions are')} not answered yet. Answer again to complete it.</Muted>
      )}
      {triggers.length > 0 && (
        <div className="flex flex-col gap-1">
          <h3 className={subhead}>The answers trigger</h3>
          <div className="flex flex-wrap gap-1.5">{triggers.map(t => <ToneBadge key={t} tone="informative">{t}</ToneBadge>)}</div>
        </div>
      )}
    </div>
  );
}

// The Microsoft words for the tier, with the page each comes from. From the assessment when there is one; else from the
// configuration's vocabulary mapping for the tier the application holds.
function VocabularyPanel({ tierKey, tierLabel, fromAssessment }: { tierKey: string | null; tierLabel: string | null; fromAssessment: TierAssessmentDetail['vocabulary'] | null }) {
  const mapping = useConfigRows('vocabulary-mapping');
  const vocab = useListValues('vocabulary');
  const rows = fromAssessment && fromAssessment.length
    ? fromAssessment.map(v => ({ key: `${v.vocabulary?.key ?? 'other'}-${v.term}`, vocabulary: v.vocabulary?.label ?? 'Outside vocabulary', term: v.term, url: v.sourceUrl }))
    : mapping.rows
      .filter(r => r.isActive && r.fields.sourceEntity === 'tier' && r.fields.sourceKey === tierKey)
      .map(r => ({
        key: r.key,
        vocabulary: vocab.label(typeof r.fields.vocabularyKey === 'string' ? r.fields.vocabularyKey : null),
        term: typeof r.fields.term === 'string' ? r.fields.term : r.label,
        url: typeof r.fields.sourceUrl === 'string' && r.fields.sourceUrl.startsWith('https://') ? r.fields.sourceUrl : null,
      }));
  return (
    <Panel title="Microsoft's words for it" subtitle={tierLabel ? `${tierLabel} in the vocabulary Microsoft's guidance uses.` : 'The tier in the vocabulary Microsoft\'s guidance uses.'} state={mapping.state} notes={[]}>
      {() => (rows.length ? (
        <RowList label="Microsoft vocabulary">
          {rows.map(r => (
            <Row key={r.key} primary={r.term} secondary={r.vocabulary} end={r.url ? <ExternalLink href={r.url}>Source</ExternalLink> : undefined} />
          ))}
        </RowList>
      ) : <Empty>{tierKey ? 'The configuration maps no outside term to this tier.' : 'No tier yet, so no outside term.'}</Empty>)}
    </Panel>
  );
}

// What the tier needs: the documents and the review cadence, each with where the application stands.
function NeedsPanel({ ctx, detail }: { ctx: App360Context; detail: TierAssessmentDetail | null }) {
  const fallback = useRead('get_requirements', detail?.assessment ? null : { businessApplicationId: ctx.applicationId });
  const docTypes = useLabels('document-type');
  const reviewTypes = useLabels('review-type');
  const stages = useLabels('lifecycle-stage');
  const req = dataOf(fallback);
  const documents = detail?.assessment
    ? detail.tierNeeds.documents.map(d => ({ key: d.documentType.key, label: d.documentType.label, from: d.fromStage?.label ?? null }))
    : (req?.documents ?? []).map(d => ({ key: d.documentTypeKey, label: docTypes.label(d.documentTypeKey), from: stages.label(d.fromStageKey) }));
  const reviews = detail?.assessment
    ? detail.tierNeeds.reviews.map(r => ({ key: r.reviewType.key, label: r.reviewType.label, days: r.intervalDays }))
    : (req?.reviews ?? []).map(r => ({ key: r.reviewTypeKey, label: reviewTypes.label(r.reviewTypeKey), days: r.intervalDays }));
  const cardDocs = ctx.card?.documents ?? [];
  const cardReviews = ctx.card?.reviews ?? [];
  const loading = !detail || (!detail.assessment && fallback.status === 'loading');
  return (
    <Panel title="What this tier needs" subtitle={detail?.assessment ? `For ${detail.assessment.computedTier.label}, the tier the answers compute.` : 'For the tier the application holds.'}>
      {loading ? <Muted>Reading what the tier needs.</Muted> : (
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-1">
            <h3 className={subhead}>Documents</h3>
            {documents.length ? (
              <RowList label="Documents the tier needs">
                {documents.map(d => {
                  const have = cardDocs.find(x => x.documentType.key === d.key);
                  return (
                    <Row
                      key={d.key}
                      primary={have?.documentLinkId ? <TextLink href={recordHrefOf('document-link', have.documentLinkId) ?? '#'}>{d.label}</TextLink> : d.label}
                      secondary={d.from ? `From ${d.from.toLowerCase()}` : undefined}
                      end={have ? <StatusBadge status={have.status} /> : <ToneBadge tone="subtle">Not yet required</ToneBadge>}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>No document is required at this tier.</Empty>}
          </div>
          <div className="flex flex-col gap-1">
            <h3 className={subhead}>Reviews</h3>
            {reviews.length ? (
              <RowList label="Reviews the tier needs">
                {reviews.map(r => {
                  const have = cardReviews.find(x => x.reviewType.key === r.key);
                  return (
                    <Row
                      key={r.key}
                      primary={have ? <TextLink href={recordHrefOf('review', have.reviewId) ?? '#'}>{r.label}</TextLink> : r.label}
                      secondary={`Every ${plural(r.days, 'day', 'days')}`}
                      end={have ? <StatusBadge status={have.status} /> : undefined}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>No review cadence at this tier.</Empty>}
          </div>
        </div>
      )}
    </Panel>
  );
}
