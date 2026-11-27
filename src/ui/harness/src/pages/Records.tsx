// The record pages of a deployment, a document link and a gap check claim (D-36: the web app acts; the engine checks
// every write). Each replaces the "not shown here" panel for its entity.
//   /app/records/deployment-record/<id>   what was deployed, where, by which route and pipeline, the versions before and
//                                         after, each flag explained with the rule the engine used, the change it
//                                         belongs to, the follow-up (an exception, a change after the fact, evidence)
//                                         and the corrections chain, with a correction of its own
//   /app/records/document-link/<id>       the document, its status and freshness, the gates that need it; confirm it,
//                                         mark it stale, update or confirm it current
//   /app/records/claim/<id>               what the gap check found; claim it into an application (or as a new one,
//                                         which starts an intake), dismiss it, or resolve a vanished item
// None of the three entities has a read by ID: get_deployments, get_documentation_status and list_claims have no ID
// filter. Each page looks through its list read, narrowed by the application hint (?app=<id>) when a link carries one,
// and says plainly when the record is not in what the person can see.
// Writes, each in the shared side sheet: save_decision_proposal, save_link, record_deployment_correction,
// record_document_link, confirm_document_checked, claim_resource, dismiss_claim, link_component.
//
// Design choices (D-41), recorded here:
// - The exception that follows up a flagged deployment stays a decision about the deployment record, not about its
//   change: the exception accepts what this deployment did, and its page lists it. The change board's rules are scoped
//   by change type, which a deployment-record subject does not carry; the engine should read it from the deployment's
//   change (a backend slice).
// - The change a deployment belongs to is the record's own Change Request when it names one. Only a record that names
//   none falls back to the changes whose window is near the moment it finished.
// - A vanished claim is never claimed (the engine refuses it): its page offers Resolve (gone or moved, link_component)
//   and Still there (a dismissal with the reason).
import { ArrowRight, CircleCheck, FilePlus2, Plus, Search, TriangleAlert, Undo2 } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { hrefTo, recordHrefOf } from '../app/links';
import { useLocationKey } from '../app/navigation';
import type { ClaimItem, DeploymentFlags, DeploymentItem, DeploymentRouteMeaning, DocumentationStatus, GateLevel, Id } from '../data/contracts';
import { daysBetween, fmtDate, fmtDateTime, plural, utcDayRange, addDays } from '../data/format';
import { useLabels, useListValues, useOutcomes } from '../data/labels';
import { flagHelp, flagText } from '../data/meanings';
import { useConfigRows, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { cn } from '../lib/utils';
import { ActionSheet, Field, GuidelineChip } from '../components/actions';
import { DataStamp, Empty, Muted, PageHeader, Panel, StatusBadge, TextLink, ToneBadge } from '../components/blocks';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Button } from '../components/ui/button';
import { Input } from '../components/ui/input';
import { Textarea } from '../components/ui/textarea';
import {
  appHintOf,
  flagFollowUp,
  flagGuideline,
  gateLevelLabel,
  gateLevelText,
  isHttpsUrl,
  nearbyChanges,
  sameId,
  samePrincipal,
  withAppHint,
} from './changes-records/logic';
import { choicesOf, column, DecisionsAbout, Fact, Facts, FlagBadges, LookupState, plainText, recordGrid, SaveLinkSheet, SelectField, SideFailure, subhead, useSheet, type Choice } from './changes-records/parts';
import { canUse, usePagedRead, useWhoAmI } from './changes-records/reads';
import { exceptionCovering, FLAG_ORDER, flagExplanation } from './deployments-places/logic';
import { mayPropose as mayProposeType } from './pmo-portfolio/logic';
import { placeHref } from './deployments-places/parts';
import { CorrectionSheet, ResolveVanishedSheet } from './deployments-places/sheets';
import { proposalHandOff, readEngineAnswer } from '../data/writes';
import { Sticky } from './work-approvals/parts';
import { SubmitFollowUp, useFollowUp } from './data/sheetParts';

// The search part of the address, for the application hint.
function useSearch(): string {
  const key = useLocationKey();
  const at = key.indexOf('?');
  return at < 0 ? '' : key.slice(at);
}

const appHref = (id: string | null | undefined) => (id ? recordHrefOf('business-application', id) : null);

function WorkLink() {
  return <TextLink href={hrefTo({ page: 'work' })} className="inline-flex items-center gap-1 self-start text-sm">Go to my work <ArrowRight className="size-4" aria-hidden /></TextLink>;
}

// The open state of a sheet a list page keeps for one row at a time: the sheet then has no trigger of its own.
export interface SheetControl {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

// ---------------------------------------------------------------------------------------------------------------------
// Deployment
// ---------------------------------------------------------------------------------------------------------------------

const ROUTE_TEXT: Record<DeploymentRouteMeaning, string> = {
  'pipeline-step': 'A pipeline\'s record step reported it, under the pipeline\'s own identity.',
  'ai-tool': 'The owner\'s AI tool reported it.',
  'person-in-app': 'A person entered it in the app.',
  other: 'Found later. Nobody reported it when it happened.',
};

export function DeploymentRecord({ deploymentRecordId }: { deploymentRecordId: string }) {
  const hint = appHintOf(useSearch());
  const has = (items: DeploymentItem[]) => items.some(i => sameId(i.deploymentRecordId, deploymentRecordId));
  const narrow = usePagedRead('get_deployments', hint ? { latestOnly: false, businessApplicationId: hint, top: 100 } : null, d => d.items, { until: has, maxPages: 20 });
  const narrowMissed = !hint || (narrow.status === 'done' && !has(narrow.items)) || narrow.status === 'failed';
  const wide = usePagedRead('get_deployments', narrowMissed ? { latestOnly: false, top: 100 } : null, d => d.items, { until: has, maxPages: 30 });
  const source = !narrowMissed ? narrow : wide;
  const items = source.status === 'done' ? source.items : [];
  const found = items.find(i => sameId(i.deploymentRecordId, deploymentRecordId)) ?? null;

  if (!found) {
    const failure = source.status === 'failed' ? source.failure : null;
    const done = source.status === 'done';
    return (
      <>
        <PageHeader eyebrow="Deployment" title={done ? 'Deployment not found' : source.status === 'failed' ? 'The deployment cannot be shown' : 'Looking for the deployment'} />
        <LookupState
          title="The deployment"
          id={deploymentRecordId}
          pagesRead={source.status === 'loading' ? source.pagesRead : 0}
          failure={failure}
          notFound={done ? (
            <p className={plainText}>
              The deployment is not among the {plural(items.length, 'deployment', 'deployments')} you can see
              {source.skipToken ? ', and the product holds more than this page looked through' : ''}. The product has no
              read of one deployment by its ID, so the page looks through the deployment history. Open it from its
              application, its change, the Deployments page or the timeline, or give this ID to your AI client.
            </p>
          ) : undefined}
          next={<TextLink href={hrefTo({ page: 'deployments' })} className="inline-flex items-center gap-1 self-start text-sm">All deployments <ArrowRight className="size-4" aria-hidden /></TextLink>}
        />
      </>
    );
  }
  return <DeploymentView deployment={found} scanned={items} envelope={source.status === 'done' ? source.first : undefined} />;
}

function DeploymentView({ deployment: d, scanned, envelope }: { deployment: DeploymentItem; scanned: DeploymentItem[]; envelope: Parameters<typeof DataStamp>[0]['envelope'] }) {
  const session = useSession();
  const routes = useLabels('deployment-route');
  const stages = useLabels('target-stage');
  const raised = FLAG_ORDER.filter(f => d.flags[f]);
  const appLink = appHref(d.businessApplicationId);
  const place = placeHref(d.deploymentTargetId);
  const correctedBy = scanned.filter(i => sameId(i.correctsRecordId, d.deploymentRecordId));
  const correctsHref = d.correctsRecordId ? withAppHint(recordHrefOf('deployment-record', d.correctsRecordId), d.businessApplicationId) : null;
  // The record this one corrects, by its artifact, version and finish time, as "Corrected by" names its records (QA walk).
  const corrects = d.correctsRecordId ? scanned.find(i => sameId(i.deploymentRecordId, d.correctsRecordId)) ?? null : null;
  const gate = d.gateLevelApplied as GateLevel;
  const pipeline = useRead('get_pipeline_map', d.pipelineReferenceId ? { pipelineReferenceId: d.pipelineReferenceId } : null);
  const pipelineRef = dataOf(pipeline)?.pipelines[0] ?? null;
  // Offered only to a person whose role holds an exception decision right, and only while no exception covers the
  // record (QA 2026-11-24: the change manager filled the form and got not_authorized).
  const rights = useConfigRows('decision-right');
  const exceptionLog = useRead('get_decision_log', { decisionTypeKeys: ['exception'], statusMeanings: ['proposed', 'accepted'], top: 100 });
  const covering = exceptionCovering(dataOf(exceptionLog)?.items ?? [], d.deploymentRecordId, d.businessApplicationId);
  const mayProposeException = session.can('save_decision_proposal') && mayProposeType(rights.rows, 'exception', session.roleKeys, []);

  return (
    <>
      <PageHeader
        eyebrow="Deployment"
        title={`${d.applicationName ?? 'Unknown application'} ${d.artifactVersion}`}
        status={<StatusBadge status={d.result} />}
        subtitle={`${d.artifactName} ${d.artifactVersion} to ${d.targetLabel ?? 'an unknown target'}, finished ${fmtDateTime(d.finishedOn)}.`}
        meta={(
          <span className="flex flex-col gap-1">
            {raised.length > 0 && <span className="flex flex-wrap gap-1"><FlagBadges flags={d.flags} /></span>}
            <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} className="self-start" />
            <StampLine performedBy={d.performedBy} />
            <DataStamp envelope={envelope} />
          </span>
        )}
        actions={(
          <>
            {raised.length > 0 && covering && (
              <Button variant="outline" asChild>
                <a href={recordHrefOf('decision', covering.decisionId) ?? '#'}>{covering.status.meaning === 'proposed' ? 'Open the exception waiting' : 'Open the exception that covers it'}</a>
              </Button>
            )}
            {/* Kept mounted once the exception covers it (Sticky), so the sheet's follow-up stays open (wave 4b, S10-9). */}
            <Sticky show={raised.length > 0 && !covering && mayProposeException}><ProposeExceptionSheet deployment={d} trigger={<Button>Propose an exception</Button>} /></Sticky>
            {/* Only while no change is named: a record that names its change needs no change after the fact (QA walk). */}
            {raised.length > 0 && !d.changeRequestId && d.businessApplicationId && session.can('save_change_request_draft') && (
              <Button variant="outline" asChild>
                <a href={hrefTo({ page: 'change-new', applicationId: d.businessApplicationId.toLowerCase() })}><FilePlus2 aria-hidden />Draft a change after the fact</a>
              </Button>
            )}
            {session.can('record_deployment_correction') && d.businessApplicationId && <CorrectionSheet record={d} trigger={<Button variant="outline">Record a correction</Button>} />}
            {session.can('save_link') && <SaveLinkSheet subjectKind="deployment-record" subjectId={d.deploymentRecordId} category="evidence" />}
          </>
        )}
      />

      <div className={recordGrid}>
        <div className={column}>
          <Panel title="What was deployed">
            <Facts>
              <Fact label="Application">{appLink ? <TextLink href={appLink}>{d.applicationName ?? 'Open the application'}</TextLink> : d.applicationName ?? 'Not matched to an application'}</Fact>
              <Fact label="Artifact">{d.artifactName}</Fact>
              <Fact label="Version"><span className="font-mono text-[13px]">{d.artifactVersion}</span></Fact>
              <Fact label="Target" sub={d.targetStageKey ? stages.label(d.targetStageKey) : undefined}>
                {place && d.targetLabel ? <TextLink href={place}>{d.targetLabel}</TextLink> : d.targetLabel ?? 'Unknown target'}
              </Fact>
              <Fact label="Finished">{fmtDateTime(d.finishedOn)}</Fact>
              <Fact label="Result"><StatusBadge status={d.result} /></Fact>
              <Fact label="On the target before and after" sub={d.versionBefore || d.versionAfter ? 'As read on the target.' : undefined}>
                {d.versionBefore || d.versionAfter
                  ? <span className="font-mono text-[13px]">{`${d.versionBefore ?? 'not known'} to ${d.versionAfter ?? 'not known'}`}</span>
                  : 'Not reported'}
              </Fact>
              <Fact label="Pipeline" sub={pipelineRef?.isMoved ? 'The pipeline has moved.' : undefined}>
                {!d.pipelineReferenceId ? 'No pipeline named'
                  : pipelineRef ? <ExternalLink href={pipelineRef.url}>{pipelineRef.label ?? pipelineRef.externalId}</ExternalLink>
                    : pipeline.status === 'loading' ? 'Reading the pipeline' : 'A pipeline the product cannot show'}
              </Fact>
              <Fact label="Run">{d.runUrl ? <ExternalLink href={d.runUrl}>Open the run</ExternalLink> : 'No run link'}</Fact>
              <Fact label="Recorded by" sub={ROUTE_TEXT[d.route.meaning]} wide>
                {routes.label(d.route.key)}{d.startedByAiClient ? `, through ${d.startedByAiClient}` : ''}
              </Fact>
              <Fact label="Gate level when recorded" sub={gateLevelText[gate] ?? undefined} wide>{gateLevelLabel[gate] ?? d.gateLevelApplied}</Fact>
              {d.correctsRecordId && <Fact label="Corrects">{correctsHref ? <TextLink href={correctsHref}>The earlier record</TextLink> : 'The earlier record'}</Fact>}
              {d.configurationSetVersion !== null && <Fact label="Configuration version">{String(d.configurationSetVersion)}</Fact>}
            </Facts>
          </Panel>

          <Panel title="Flags" subtitle="Each flag the engine worked out, with the guideline it puts into practice" count={raised.length}>
            {gate === 'off' && <Muted>At gate level Off the engine never flags a missing approval. Outside window and other path are kept as facts.</Muted>}
            <ul className="m-0 flex list-none flex-col p-0">
              {FLAG_ORDER.map(f => <FlagLine key={f} flag={f} raised={d.flags[f]} />)}
            </ul>
            <RuleUsed deploymentRecordId={d.deploymentRecordId} />
          </Panel>
        </div>

        <div className={column}>
          <LinkedChange deployment={d} />
          <DecisionsAbout subjectKind="deployment-record" subjectId={d.deploymentRecordId} title="Decisions about this deployment" empty="No exception or other decision is recorded about this deployment." />
          <Panel title="Corrections" count={correctedBy.length + (d.correctsRecordId ? 1 : 0)}>
            {d.correctsRecordId || correctedBy.length ? (
              <RowList label="Corrections">
                {d.correctsRecordId && (
                  <Row
                    primary={(() => {
                      const text = corrects ? `Corrects ${corrects.artifactName} ${corrects.artifactVersion}` : 'The record this one corrects';
                      return correctsHref ? <TextLink href={correctsHref}>{text}</TextLink> : text;
                    })()}
                    secondary={corrects ? `Finished ${fmtDateTime(corrects.finishedOn)}` : 'An earlier record of this application'}
                    end={corrects ? <StatusBadge status={corrects.result} /> : undefined}
                  />
                )}
                {correctedBy.map(c => {
                  const href = withAppHint(recordHrefOf('deployment-record', c.deploymentRecordId), c.businessApplicationId);
                  return (
                    <Row
                      key={c.deploymentRecordId}
                      primary={href ? <TextLink href={href}>{`Corrected by ${c.artifactName} ${c.artifactVersion}`}</TextLink> : `Corrected by ${c.artifactName} ${c.artifactVersion}`}
                      secondary={`Finished ${fmtDateTime(c.finishedOn)}`}
                      end={<StatusBadge status={c.result} />}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>This record corrects nothing, and no correction of it was found.</Empty>}
          </Panel>
        </div>
      </div>
    </>
  );
}

function FlagLine({ flag, raised }: { flag: keyof DeploymentFlags; raised: boolean }) {
  return (
    <li className="flex flex-col gap-2 border-t border-line py-3 first:border-t-0 first:pt-1">
      <div className="flex items-start gap-2.5">
        {raised
          ? <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
          : <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />}
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="text-sm font-semibold text-navy">
            {flagText[flag]}: <span className={raised ? 'text-warning' : 'text-success'}>{raised ? 'raised' : 'not raised'}</span>
          </span>
          <span className="text-xs leading-relaxed text-navy-3">{raised ? flagHelp[flag] : `Raised when: ${flagHelp[flag].charAt(0).toLowerCase()}${flagHelp[flag].slice(1)}`}</span>
          {raised && <span className="text-sm leading-relaxed text-navy-2">{flagExplanation(flagHelp[flag], flagFollowUp[flag]).slice(flagHelp[flag].length).trim()}</span>}
        </div>
      </div>
      {raised && <div className="pl-6.5"><GuidelineChip number={flagGuideline[flag]} /></div>}
    </li>
  );
}

// The rule the engine used for the flags and the gate level (explain_rules, aspect gate): the gate policy row for the
// application's tier, the change type and the target stage, and the route it set from the entry point.
function RuleUsed({ deploymentRecordId }: { deploymentRecordId: string }) {
  const explained = useRead('explain_rules', { subjectKind: 'deployment-record', subjectId: deploymentRecordId, aspects: ['gate'] });
  const policies = useLabels('gate-policy');
  const data = dataOf(explained);
  const aspect = data?.aspects[0] ?? null;
  return (
    <div className="flex flex-col gap-1.5 border-t border-line pt-3">
      <h3 className={subhead}>The rule it used</h3>
      {explained.status === 'loading' ? <Muted>Reading the rule.</Muted>
        : !explained.envelope.ok ? <SideFailure failure={explained.envelope} what="The rule" />
          : !aspect ? <Empty>No rule trace applies to this record.</Empty> : (
            <>
              <p className="m-0 text-sm text-navy">
                {aspect.winner
                  ? <>Gate policy <span className="font-semibold">{policies.label(aspect.winner.key)}</span>, under configuration version {data!.configurationSetVersion}.</>
                  : `No gate policy row covers this deployment, under configuration version ${data!.configurationSetVersion}.`}
              </p>
              <ol className="m-0 flex list-decimal flex-col gap-0.5 pl-5 text-xs leading-relaxed text-navy-2">
                {aspect.trace.map(t => <li key={t.step}>{t.why}</li>)}
              </ol>
            </>
          )}
    </div>
  );
}

// The change this deployment belongs to: the record's own change when it names one. A record that names none is checked
// against the changes of the application whose window is near the moment it finished: the closest three are asked
// whether they list this deployment.
function LinkedChange({ deployment: d }: { deployment: DeploymentItem }) {
  if (d.changeRequestId) return <NamedChange deployment={d} changeRequestId={d.changeRequestId} />;
  return <NearbyChange deployment={d} />;
}

function NamedChange({ deployment: d, changeRequestId }: { deployment: DeploymentItem; changeRequestId: string }) {
  const session = useSession();
  const change = useRead('get_change_request', { changeRequestId });
  return (
    <Panel
      title="The change it belongs to"
      state={change}
      actions={session.can('record_deployment_correction') ? <CorrectionSheet record={d} trigger={<Button variant="link" size="sm" className="h-7 px-0">Not this change?</Button>} /> : undefined}
    >
      {c => {
        const href = recordHrefOf('change-request', c.change.changeRequestId);
        const authorized = c.authorization.status?.meaning === 'accepted';
        return (
          <RowList label="Linked change">
            <Row
              lead={<span className="grid size-8 place-items-center rounded-full bg-success-soft text-success" aria-hidden><CircleCheck className="size-4" /></span>}
              primary={href ? <TextLink href={href}>{c.change.title}</TextLink> : c.change.title}
              secondary={`${c.change.changeType.label} change, ${c.change.status.label.toLowerCase()}. ${authorized ? 'Authorized' : c.authorization.status ? `Authorization ${c.authorization.status.label.toLowerCase()}` : 'No authorization on record'}.`}
              tertiary="The record names this change."
            />
          </RowList>
        );
      }}
    </Panel>
  );
}

function NearbyChange({ deployment: d }: { deployment: DeploymentItem }) {
  const session = useSession();
  const day = d.finishedOn.slice(0, 10);
  const schedule = useTool('get_change_schedule', d.businessApplicationId ? { ...utcDayRange(addDays(day, -3), addDays(day, 1)), businessApplicationId: d.businessApplicationId, top: 50 } : null);
  const candidates = nearbyChanges(dataOf(schedule)?.items ?? [], d).slice(0, 3);
  const checks = [
    useTool('get_change_request', candidates[0] ? { changeRequestId: candidates[0].changeRequestId } : null),
    useTool('get_change_request', candidates[1] ? { changeRequestId: candidates[1].changeRequestId } : null),
    useTool('get_change_request', candidates[2] ? { changeRequestId: candidates[2].changeRequestId } : null),
  ];
  const linked = candidates.filter((_, i) => (dataOf(checks[i])?.deployments ?? []).some(x => sameId(x.deploymentRecordId, d.deploymentRecordId)));
  const checking = checks.slice(0, candidates.length).some(c => c.status === 'loading');
  const canCorrect = session.can('record_deployment_correction') && !!d.businessApplicationId;
  return (
    <Panel title="The change it belongs to" state={d.businessApplicationId ? schedule : undefined}>
      {() => (
        <>
          {linked.length ? (
            <RowList label="Linked change">
              {linked.map(c => {
                const href = recordHrefOf('change-request', c.changeRequestId);
                return (
                  <Row
                    key={c.changeRequestId}
                    lead={<span className="grid size-8 place-items-center rounded-full bg-success-soft text-success" aria-hidden><CircleCheck className="size-4" /></span>}
                    primary={href ? <TextLink href={href}>{c.title}</TextLink> : c.title}
                    secondary={`Linked. ${c.changeType.label} change, ${c.status.label.toLowerCase()}. ${c.authorized ? 'Authorized' : 'Not authorized'}.`}
                  />
                );
              })}
            </RowList>
          ) : checking ? <Muted>Checking the changes near this time.</Muted> : (
            <div className="flex flex-col gap-2">
              <Empty>The record names no change, and no change lists this deployment.</Empty>
              {candidates.length > 0 && (
                <>
                  <h3 className={subhead}>Changes near this time, not linked</h3>
                  <RowList label="Changes near this time">
                    {candidates.map(c => {
                      const href = recordHrefOf('change-request', c.changeRequestId);
                      return <Row key={c.changeRequestId} primary={href ? <TextLink href={href}>{c.title}</TextLink> : c.title} secondary={`${c.changeType.label}. ${c.status.label}`} />;
                    })}
                  </RowList>
                </>
              )}
              {canCorrect && (
                <p className="m-0 flex flex-wrap items-center gap-x-1 text-sm text-navy-2">
                  It belongs to one of them?
                  <CorrectionSheet record={d} trigger={<Button variant="link" size="sm" className="h-7 px-0">Record a correction that names it</Button>} />
                </p>
              )}
            </div>
          )}
        </>
      )}
    </Panel>
  );
}

// Follow up a flagged deployment with an exception: a decision proposal about this record. The engine finds the decider
// and the approvers. Wave 4b: once the proposal is saved, the sheet closes and asks "Send it for approval now?"
// with the engine's answer on top, as every proposal sheet does; Cancel leaves it proposed for its decision page. The
// Deployments list opens it for one row at a time (open and onOpenChange, no trigger) and hears that it closed only when
// the follow-up closes, so the row stays chosen and the follow-up stays mounted; the record page gives it a trigger.
export function ProposeExceptionSheet({ deployment: d, trigger, open, onOpenChange }: { deployment: DeploymentItem; trigger?: ReactElement; open?: boolean; onOpenChange?: (open: boolean) => void }) {
  const host = useHost();
  const raised = FLAG_ORDER.filter(f => d.flags[f]);
  const confidence = useListValues('confidence');
  const exception = useOutcomes('exception');
  const guidelines = useLabels('guideline');
  const outcomes = exception.options.map(o => ({ value: o.key, label: o.label }));
  const flagsText = raised.map(f => flagText[f].toLowerCase()).join(', ');
  // The standard the exception departs from: the guidelines the raised flags put into practice, in their own words.
  const standardOf = () => [...new Set(raised.map(f => flagGuideline[f]))].map(g => {
    if (!guidelines.has(g.toLowerCase())) return g;
    const label = guidelines.label(g.toLowerCase());
    return label.toUpperCase().startsWith(`${g} `) ? label : `${g} ${label}`;
  }).join('; ');
  const initial = () => ({
    statement: `Accept ${d.applicationName ?? 'the application'} ${d.artifactVersion} on ${d.targetLabel ?? 'its target'} as deployed, flagged ${flagsText}.`,
    context: raised.map(f => `${flagText[f]}: ${flagHelp[f]}`).join('\n'),
  });
  const [statement, setStatement] = useState(initial().statement);
  const [context, setContext] = useState(initial().context);
  const [outcome, setOutcome] = useState('');
  const [reason, setReason] = useState('');
  const [confidenceKey, setConfidenceKey] = useState('');
  const [standard, setStandard] = useState<string | null>(null);
  const [reviewOn, setReviewOn] = useState(addDays(host.today, 90));
  const [options, setOptions] = useState<{ option: string; whyNot: string }[]>([{ option: '', whyNot: '' }]);
  const [ownOpen, setOwnOpen] = useState(false);
  // True once the proposal is saved and the follow-up has taken over: the proposal sheet shows closed until it closes.
  const [handedOff, setHandedOff] = useState(false);
  const follow = useFollowUp();
  const controlled = open !== undefined;
  const sheetOpen = (controlled ? !!open : ownOpen) && !handedOff;
  // An exception proposal grants the exception unless the person says otherwise.
  const chosenOutcome = outcome || outcomes.find(o => o.value === 'grant')?.value || outcomes[0]?.value || '';
  const chosenStandard = standard ?? standardOf();
  const reset = () => {
    const i = initial();
    setStatement(i.statement); setContext(i.context); setOutcome(''); setReason(''); setConfidenceKey(''); setStandard(null); setReviewOn(addDays(host.today, 90)); setOptions([{ option: '', whyNot: '' }]);
  };
  // A new row chosen in the list starts from that row's words.
  useEffect(() => {
    const i = initial();
    setStatement(i.statement);
    setContext(i.context);
    setStandard(null);
  }, [d.deploymentRecordId]);
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/Records#ProposeExceptionSheet"
      title="Propose an exception"
      description={`An exception is a decision about this deployment of ${d.applicationName ?? 'the application'} ${d.artifactVersion}. It is saved as proposed, and the next sheet asks whether to send it to the approvers the rules name now.`}
      trigger={trigger}
      open={sheetOpen}
      onOpenChange={next => {
        if (!controlled) setOwnOpen(next);
        onOpenChange?.(next);
        if (!next) reset();
      }}
      onDone={(_, envelope) => {
        const handed = proposalHandOff(envelope);
        if (!handed) return;
        follow.start(handed);
        setHandedOff(true);
        if (!controlled) setOwnOpen(false);
        reset();
      }}
      size="wide"
      input={() => {
        const s = statement.trim();
        const c = context.trim();
        const r = reason.trim();
        if (!s) return { problem: 'Say in one line what the exception accepts.', field: 'statement' };
        if (!c) return { problem: 'Give the context: what happened and why.', field: 'context' };
        if (!chosenOutcome) return { problem: 'Choose the proposed outcome.', field: 'proposedOutcomeKey' };
        if (!r) return { problem: 'Say why.', field: 'reason' };
        const std = chosenStandard.trim();
        if (!std) return { problem: 'Name the standard the exception departs from.', field: 'exception.standard' };
        if (std.length > 200) return { problem: 'The standard is longer than 200 characters. Shorten it.', field: 'exception.standard' };
        if (!/^\d{4}-\d{2}-\d{2}$/.test(reviewOn)) return { problem: 'Give the date to review the exception again.', field: 'exception.reviewOn' };
        if (reviewOn < host.today) return { problem: 'The review date is in the past.', field: 'exception.reviewOn' };
        const opts = options.map(o => ({ option: o.option.trim(), whyNot: o.whyNot.trim() })).filter(o => o.option || o.whyNot);
        if (opts.some(o => !o.option || !o.whyNot)) return { problem: 'Each option considered needs the option and why not.', field: 'optionsConsidered' };
        return {
          decisionTypeKey: 'exception',
          subjectKind: 'deployment-record' as const,
          subjectId: d.deploymentRecordId,
          statement: s,
          context: c,
          proposedOutcomeKey: chosenOutcome,
          reason: r,
          exception: { standard: std, reviewOn },
          ...(opts.length ? { optionsConsidered: opts } : {}),
          ...(confidenceKey ? { confidenceKey } : {}),
        };
      }}
      submitLabel="Save the proposal"
      preview
    >
      <Field label="What the exception accepts" name="statement" required hint="One line: what, for which deployment.">
        {p => <Textarea {...p} rows={2} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="Context" name="context" required hint="What happened, and what the flags mean here.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <fieldset className="m-0 flex min-w-0 flex-col gap-2 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">Options considered</legend>
        {options.map((o, i) => (
          <div key={i} className="grid grid-cols-2 gap-2 narrow:grid-cols-1">
            <Field label={`Option ${i + 1}`} name="optionsConsidered">
              {p => <Input {...p} value={o.option} onChange={e => setOptions(list => list.map((x, j) => (j === i ? { ...x, option: e.target.value } : x)))} maxLength={500} placeholder="Roll back and redeploy in the window" />}
            </Field>
            <Field label="Why not" name="optionsConsidered">
              {p => <Input {...p} value={o.whyNot} onChange={e => setOptions(list => list.map((x, j) => (j === i ? { ...x, whyNot: e.target.value } : x)))} maxLength={500} />}
            </Field>
          </div>
        ))}
        {options.length < 4 && (
          <Button type="button" variant="link" size="sm" className="h-7 self-start px-0" onClick={() => setOptions(list => [...list, { option: '', whyNot: '' }])}>
            <Plus aria-hidden />Add another option
          </Button>
        )}
        <p className="m-0 text-xs text-navy-3">The exception type asks for the options considered. The engine says so if they are missing.</p>
      </fieldset>
      <SelectField label="Proposed outcome" name="proposedOutcomeKey" value={chosenOutcome} onChange={setOutcome} choices={outcomes} required placeholder={outcomes.length ? 'Choose' : 'Reading the outcomes'} />
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      <Field label="The standard it departs from" name="exception.standard" required hint="The guideline or rule the deployment did not follow. Filled from the flags' guidelines.">
        {p => <Textarea {...p} rows={2} maxLength={200} value={chosenStandard} onChange={e => setStandard(e.target.value)} />}
      </Field>
      <Field label="Review it again on" name="exception.reviewOn" required hint="An exception is reviewed on this date: today or later.">
        {p => <Input {...p} type="date" min={host.today} value={reviewOn} onChange={e => setReviewOn(e.target.value)} className="w-[200px]" />}
      </Field>
      {confidence.choices.length > 0 && (
        <SelectField label="Confidence" name="confidenceKey" value={confidenceKey} onChange={setConfidenceKey} choices={confidence.choices} noneLabel="Not stated" />
      )}
    </ActionSheet>
    {follow.data && (
      <SubmitFollowUp
        decisionId={follow.data.decisionId}
        previous={follow.data.answer}
        subject={`The exception for ${d.applicationName ?? 'the application'} ${d.artifactVersion}`}
        open={follow.open}
        onOpenChange={next => {
          follow.onOpenChange(next);
          if (next) return;
          setHandedOff(false);
          // The list that chose the row hears now that its sheet is done.
          if (controlled) onOpenChange?.(false);
        }}
      />
    )}
    </>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Document link
// ---------------------------------------------------------------------------------------------------------------------

type DocApp = DocumentationStatus['applications'][number];
type DocRow = DocApp['documents'][number];

export function DocumentLinkRecord({ documentLinkId }: { documentLinkId: string }) {
  const hint = appHintOf(useSearch());
  // Wave 4 (B24-21): list_documents reads one document link by its ID and names its application, so the page reads that
  // application's documents next. The scans below stay as the fallback when the read by ID finds nothing.
  const queue = useTool('list_documents', hint ? null : { documentLinkId, state: 'all', top: 1 });
  const queueApp = dataOf(queue)?.items.find(i => sameId(i.documentLinkId, documentLinkId))?.businessApplicationId ?? null;
  const appId = hint ?? queueApp;
  const byApp = useTool('get_documentation_status', appId ? { businessApplicationId: appId, top: 100 } : null);
  const inApp = (dataOf(byApp)?.applications ?? []).flatMap(a => a.documents.filter(doc => sameId(doc.documentLinkId, documentLinkId)).map(doc => ({ app: a, doc })))[0] ?? null;
  const appMissed = !appId || (byApp.status === 'done' && !inApp);
  const pick = (data: DocumentationStatus) => data.applications.flatMap(a => a.documents.map(doc => ({ app: a, doc })));
  const mine = usePagedRead('get_documentation_status', appMissed && (hint || queue.status === 'done') ? { mine: true, top: 100 } : null, pick, {
    until: items => items.some(x => sameId(x.doc.documentLinkId, documentLinkId)),
    maxPages: 10,
  });
  const fromMine = mine.status === 'done' ? mine.items.find(x => sameId(x.doc.documentLinkId, documentLinkId)) ?? null : null;
  // Last, the documents of every application the person may read (the platform admin, the auditor): a plain record link
  // opens for them too (a11y review: the platform admin got "Document not found").
  const everyApp = usePagedRead('get_documentation_status', appMissed && mine.status === 'done' && !fromMine ? { top: 100 } : null, pick, {
    until: items => items.some(x => sameId(x.doc.documentLinkId, documentLinkId)),
    maxPages: 10,
  });
  const fromAll = everyApp.status === 'done' ? everyApp.items.find(x => sameId(x.doc.documentLinkId, documentLinkId)) ?? null : null;
  const found = inApp ?? fromMine ?? fromAll;

  if (!found) {
    const looking = (appId && byApp.status === 'loading') || (!hint && queue.status === 'loading') || mine.status === 'loading' || everyApp.status === 'loading';
    const failure = byApp.status === 'done' && !byApp.envelope.ok && mine.status !== 'done' ? byApp.envelope : mine.status === 'failed' ? mine.failure : null;
    return (
      <>
        <PageHeader eyebrow="Document" title={looking ? 'Looking for the document' : 'Document not found'} />
        <LookupState
          title="The document"
          id={documentLinkId}
          pagesRead={mine.status === 'loading' ? mine.pagesRead : 0}
          failure={looking ? null : failure}
          notFound={!looking && !failure ? (
            <p className={plainText}>
              The page looked for the document by its ID{appId ? ', in the documents of its application' : ''}, in the documents
              of the applications you own and in every application you may read. It is not there, or you may not read it. Open
              it from its application's 360, where every document is listed.
            </p>
          ) : undefined}
          next={<WorkLink />}
        />
      </>
    );
  }
  return <DocumentView app={found.app} doc={found.doc} envelope={inApp && byApp.status === 'done' ? byApp.envelope : fromMine && mine.status === 'done' ? mine.first : everyApp.status === 'done' ? everyApp.first : undefined} />;
}

function DocumentView({ app, doc, envelope }: { app: DocApp; doc: DocRow; envelope: Parameters<typeof DataStamp>[0]['envelope'] }) {
  const host = useHost();
  const who = useWhoAmI();
  const docTypes = useLabels('document-type');
  const stagesOfLife = useLabels('lifecycle-stage');
  const gates = useLabels('gate');
  const requirements = useTool('get_requirements', { businessApplicationId: app.businessApplicationId });
  const typeLabel = docTypes.label(doc.documentTypeKey);
  const needsPerson = docTypes.field<boolean>(doc.documentTypeKey, 'currentNeedsPerson') === true;
  const freshnessDays = docTypes.field<number>(doc.documentTypeKey, 'freshnessDays');
  const required = app.required.filter(r => r.documentTypeKey === doc.documentTypeKey);
  const gateRows = (dataOf(requirements)?.gates ?? []).flatMap(g => g.requirements
    .filter(r => r.requirementKind === 'document-current' && r.documentTypeKey === doc.documentTypeKey)
    .map(r => ({ gateKey: g.gateKey, met: r.met, guideline: r.guideline })));
  const siblings = app.documents.filter(x => !sameId(x.documentLinkId, doc.documentLinkId));
  const meaning = doc.status.meaning;
  const canLink = canUse(who, 'record_document_link');
  const canCheck = canUse(who, 'confirm_document_checked');
  const appLink = appHref(app.businessApplicationId);

  return (
    <>
      <PageHeader
        eyebrow="Document"
        title={`${typeLabel}: ${app.name}`}
        status={<StatusBadge status={doc.status} />}
        subtitle={<ExternalLink href={doc.url}>Open the document</ExternalLink>}
        meta={<DataStamp envelope={envelope} />}
        actions={(
          <>
            {/* Each act shows while it fits the document; its sheet stays open after the write changes the status. */}
            <ConfirmCurrentSheet app={app} doc={doc} typeLabel={typeLabel} show={meaning === 'draft' && canLink} />
            <CheckedSheet doc={doc} typeLabel={typeLabel} stillTrue show={meaning !== 'draft' && canCheck} />
            <CheckedSheet doc={doc} typeLabel={typeLabel} stillTrue={false} show={meaning !== 'stale' && canCheck} />
            <DocumentLinkSheet app={app} doc={doc} typeLabel={typeLabel} show={canLink} />
          </>
        )}
      />

      <div className={recordGrid}>
        <div className={column}>
          <Panel title="The document">
            <Facts>
              <Fact label="Application">{appLink ? <TextLink href={appLink}>{app.name}</TextLink> : app.name}</Fact>
              <Fact label="Type">{typeLabel}</Fact>
              <Fact label="Status" sub={statusText(meaning, needsPerson)}><StatusBadge status={doc.status} /></Fact>
              <Fact label="Owner">{doc.owner?.displayLabel ?? 'No owner named'}</Fact>
              <Fact label="Last updated">{fmtDate(doc.lastUpdatedOn, 'Not stated')}</Fact>
              <Fact label="Last checked">{fmtDate(doc.lastCheckedOn, 'Never checked')}</Fact>
              <Fact label="Freshness" wide>{freshness(doc.staleOn, host.today, freshnessDays)}</Fact>
              <Fact label="Link" wide><ExternalLink href={doc.url}>{doc.url}</ExternalLink></Fact>
            </Facts>
          </Panel>

          <Panel title="Why it is required">
            {required.length ? (
              <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm text-navy-2">
                {required.map(r => <li key={r.requirementKey}>{`Required from the ${stagesOfLife.label(r.fromStageKey).toLowerCase()} stage for this application.`}</li>)}
              </ul>
            ) : <Empty>No documentation rule requires this document for this application. It is kept because someone linked it.</Empty>}
            <div className="flex flex-col gap-1">
              <h3 className={subhead}>Gates that need it current</h3>
              {requirements.status === 'loading' ? <Muted>Reading the gates.</Muted>
                : !requirements.envelope.ok ? <SideFailure failure={requirements.envelope} what="The gates" />
                  : gateRows.length ? (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                      {gateRows.map((g, i) => (
                        <li key={`${g.gateKey}-${i}`} className="flex flex-col gap-1.5">
                          <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-navy">
                            {gates.label(g.gateKey)}
                            {g.met === undefined ? null : g.met ? <ToneBadge tone="success">Met</ToneBadge> : <ToneBadge tone="warning">Not met</ToneBadge>}
                          </span>
                          {g.guideline && <GuidelineChip number={g.guideline} />}
                        </li>
                      ))}
                    </ul>
                  ) : <Empty>No gate needs this document at this application's tier.</Empty>}
            </div>
          </Panel>
        </div>

        <div className={column}>
          <Panel
            title={`Other documents of ${app.name}`}
            count={siblings.length + app.missing.length}
            actions={canLink ? <DocumentLinkSheet app={app} typeLabel="" missing={app.missing} show={app.missing.length > 0} /> : undefined}
          >
            {siblings.length || app.missing.length ? (
              <RowList label={`Other documents of ${app.name}`}>
                {siblings.map(s => {
                  const href = withAppHint(recordHrefOf('document-link', s.documentLinkId), app.businessApplicationId);
                  return (
                    <Row
                      key={s.documentLinkId}
                      primary={href ? <TextLink href={href}>{docTypes.label(s.documentTypeKey)}</TextLink> : docTypes.label(s.documentTypeKey)}
                      secondary={s.lastCheckedOn ? `Checked ${fmtDate(s.lastCheckedOn)}` : 'Never checked'}
                      end={<StatusBadge status={s.status} />}
                    />
                  );
                })}
                {app.missing.map(k => (
                  <Row key={`missing-${k}`} primary={docTypes.label(k)} secondary="Required, no link recorded yet" end={<ToneBadge tone="danger">Missing</ToneBadge>} />
                ))}
              </RowList>
            ) : <Empty>No other document is linked or required.</Empty>}
          </Panel>
        </div>
      </div>
    </>
  );
}

function statusText(meaning: string, needsPerson: boolean): string {
  switch (meaning) {
    case 'draft':
      return needsPerson ? 'Recorded, not confirmed current. A person confirms this type current; an AI client cannot.' : 'Recorded, not confirmed current yet.';
    case 'current':
      return 'Confirmed current.';
    case 'stale':
      return 'Past its freshness date, or marked stale. Confirm it is still true, or update it.';
    default:
      return 'No link recorded.';
  }
}

function freshness(staleOn: string | null, today: string, freshnessDays: number | undefined): string {
  if (!staleOn) return freshnessDays ? 'No stale date yet. It is set when the document is confirmed.' : 'This document type has no freshness period: it does not turn stale by date.';
  const days = daysBetween(today, staleOn);
  if (days > 0) return `Turns stale on ${fmtDate(staleOn)}, in ${plural(days, 'day', 'days')}, unless someone confirms it again.`;
  return `Stale since ${fmtDate(staleOn)}.`;
}

// confirm_document_checked: still true, or mark it stale.
function CheckedSheet({ doc, typeLabel, stillTrue, show }: { doc: DocRow; typeLabel: string; stillTrue: boolean; show: boolean }) {
  const [note, setNote] = useState('');
  const sheet = useSheet(() => setNote(''));
  if (!show && !sheet.open) return null;
  return (
    <ActionSheet
      tool="confirm_document_checked" site="pages/Records#CheckedSheet"
      title={stillTrue ? `Confirm the ${typeLabel.toLowerCase()} is still true` : `Mark the ${typeLabel.toLowerCase()} stale`}
      description={stillTrue
        ? 'You read the document and it still describes the application. Last checked becomes today.'
        : 'The document no longer describes the application. It shows as stale until someone updates it and confirms it.'}
      trigger={show ? <Button variant={stillTrue ? 'default' : 'outline'}>{stillTrue ? 'Confirm still true' : 'Mark stale'}</Button> : undefined}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => ({ documentLinkId: doc.documentLinkId, stillTrue, ...(note.trim() ? { note: note.trim() } : {}) })}
      submitLabel={stillTrue ? 'Confirm' : 'Mark stale'}
      submitTone={stillTrue ? 'default' : 'destructive'}
      preview
    >
      <Field label="Note" name="note" hint="Optional. What you checked, or what is out of date.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// record_document_link with status current: a person confirms a draft current. An AI client can only store a draft.
function ConfirmCurrentSheet({ app, doc, typeLabel, show }: { app: DocApp; doc: DocRow; typeLabel: string; show: boolean }) {
  const sheet = useSheet();
  if (!show && !sheet.open) return null;
  return (
    <ActionSheet
      tool="record_document_link" site="pages/Records#ConfirmCurrentSheet"
      title={`Confirm the ${typeLabel.toLowerCase()} current`}
      description="You read the document and it is ready to rely on. The engine records it as current under your name and sets its stale date."
      trigger={show ? <Button>Confirm it is current</Button> : undefined}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => ({
        documentLinkId: doc.documentLinkId,
        businessApplicationId: app.businessApplicationId,
        documentTypeKey: doc.documentTypeKey,
        url: doc.url,
        statusMeaning: 'current' as const,
        ...(doc.lastUpdatedOn ? { lastUpdatedOn: doc.lastUpdatedOn } : {}),
        ...(doc.owner ? { documentOwnerObjectId: doc.owner.objectId } : {}),
        // The kind the link was recorded with, unchanged: the engine checks the address against this kind's pattern.
        ...(doc.linkKindKey ? { linkKindKey: doc.linkKindKey } : {}),
      })}
      submitLabel="Confirm current"
      preview
    >
      <p className="m-0 text-sm text-navy-2">
        The link stays <ExternalLink href={doc.url}>{doc.url}</ExternalLink>
        {doc.owner ? `, and ${doc.owner.displayLabel} stays its owner.` : '.'}
      </p>
    </ActionSheet>
  );
}

// record_document_link: update this link, or link a required document that has none yet. Application 360 uses it for
// the documents an application is missing.
export function DocumentLinkSheet({ app, doc, typeLabel, missing, show }: { app: Pick<DocApp, 'businessApplicationId'>; doc?: DocRow; typeLabel: string; missing?: Id[]; show: boolean }) {
  const docTypes = useLabels('document-type');
  const kinds = useLabels('link-kind');
  const kindChoices = choicesOf(kinds, k => kinds.field<string>(k, 'linkCategory') === 'document');
  const typeChoices: Choice[] = (missing ?? []).map(k => ({ value: k, label: docTypes.label(k) }));
  const [typeKey, setTypeKey] = useState('');
  const [url, setUrl] = useState(doc?.url ?? '');
  const [status, setStatus] = useState<'draft' | 'current'>(doc?.status.meaning === 'current' ? 'current' : 'draft');
  const [updated, setUpdated] = useState(doc?.lastUpdatedOn ?? '');
  const [kind, setKind] = useState('');
  const chosenType = doc?.documentTypeKey ?? (typeKey || typeChoices[0]?.value || '');
  // An existing link keeps the kind it was recorded with unless the person picks another; a new link starts on the
  // document type's default.
  const defaultKind = doc?.linkKindKey ?? (chosenType ? docTypes.field<string>(chosenType, 'defaultLinkKindKey') ?? '' : '');
  const chosenKind = kind || defaultKind;
  const reset = () => { setTypeKey(''); setUrl(doc?.url ?? ''); setStatus(doc?.status.meaning === 'current' ? 'current' : 'draft'); setUpdated(doc?.lastUpdatedOn ?? ''); setKind(''); };
  const sheet = useSheet(reset);
  if (!show && !sheet.open) return null;
  return (
    <ActionSheet
      tool="record_document_link" site="pages/Records#DocumentLinkSheet"
      title={doc ? `Update the ${typeLabel.toLowerCase()} link` : 'Link a missing document'}
      description="The product stores the link and the status you report. It never writes the document itself."
      trigger={!show ? undefined : doc ? <Button variant="outline">Update the link</Button> : <Button variant="outline" size="sm"><Plus aria-hidden />Link a document</Button>}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => {
        if (!chosenType) return { problem: 'Choose the document type.', field: 'documentTypeKey' };
        const u = url.trim();
        if (!isHttpsUrl(u)) return { problem: 'Give the full https address of the document.', field: 'url' };
        if (updated && !/^\d{4}-\d{2}-\d{2}$/.test(updated)) return { problem: 'The last updated date is not a date.', field: 'lastUpdatedOn' };
        return {
          ...(doc ? { documentLinkId: doc.documentLinkId } : {}),
          businessApplicationId: app.businessApplicationId,
          documentTypeKey: chosenType,
          url: u,
          statusMeaning: status,
          ...(updated ? { lastUpdatedOn: updated } : {}),
          ...(doc?.owner ? { documentOwnerObjectId: doc.owner.objectId } : {}),
          ...(chosenKind ? { linkKindKey: chosenKind } : {}),
        };
      }}
      submitLabel={doc ? 'Save the link' : 'Link it'}
      preview
    >
      {!doc && <SelectField label="Document type" name="documentTypeKey" value={chosenType} onChange={setTypeKey} choices={typeChoices} required />}
      <Field label="Link" name="url" required hint="The full https address. The engine checks it against the link kind's pattern.">
        {p => <Input {...p} type="url" inputMode="url" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      <SelectField label="Link kind" name="linkKindKey" value={chosenKind} onChange={setKind} choices={kindChoices} placeholder="The type's default" />
      <SelectField
        label="Status"
        name="statusMeaning"
        value={status}
        onChange={v => setStatus(v === 'current' ? 'current' : 'draft')}
        choices={[{ value: 'draft', label: 'Draft: not confirmed yet' }, { value: 'current', label: 'Current: I confirm it' }]}
        required
      />
      <Field label="Last updated" name="lastUpdatedOn" hint="Optional. When the document itself last changed.">
        {p => <Input {...p} type="date" value={updated} onChange={e => setUpdated(e.target.value)} />}
      </Field>
      {doc?.owner && <Muted>{`${doc.owner.displayLabel} stays the document owner.`}</Muted>}
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Claim
// ---------------------------------------------------------------------------------------------------------------------

export const FINDING_TEXT: Record<ClaimItem['findingKind'], { label: string; text: string }> = {
  unregistered: { label: 'Not registered', text: 'Live in the estate, but no registered application holds it.' },
  vanished: { label: 'Vanished', text: 'Registered before, but the gap check no longer finds it in the estate.' },
};

export function ClaimRecord({ claimId }: { claimId: string }) {
  const has = (items: ClaimItem[]) => items.some(i => sameId(i.claimId, claimId));
  const scan = usePagedRead('list_claims', { statusMeanings: ['open', 'claimed', 'dismissed'], top: 100 }, d => d.items, { until: has, maxPages: 20 });
  const items = scan.status === 'done' ? scan.items : [];
  const found = items.find(i => sameId(i.claimId, claimId)) ?? null;
  if (!found) {
    const done = scan.status === 'done';
    return (
      <>
        <PageHeader eyebrow="Claim" title={done ? 'Claim not found' : scan.status === 'failed' ? 'The claim cannot be shown' : 'Looking for the claim'} />
        <LookupState
          title="The claim"
          id={claimId}
          pagesRead={scan.status === 'loading' ? scan.pagesRead : 0}
          failure={scan.status === 'failed' ? scan.failure : null}
          notFound={done ? (
            <p className={plainText}>
              The claim is not among the {plural(items.length, 'claim', 'claims')} you can see. Claims show to the platform admins
              and to the person a claim is suggested to, as the claim visibility setting says. The page looked through the
              claims list.
            </p>
          ) : undefined}
          next={<WorkLink />}
        />
      </>
    );
  }
  return <ClaimView claim={found} others={items.filter(i => !sameId(i.claimId, claimId))} envelope={scan.status === 'done' ? scan.first : undefined} />;
}

function ClaimView({ claim: c, others, envelope }: { claim: ClaimItem; others: ClaimItem[]; envelope: Parameters<typeof DataStamp>[0]['envelope'] }) {
  const session = useSession();
  const me = session.objectId;
  const meaning = c.status.meaning;
  const vanished = c.findingKind === 'vanished';
  const finding = FINDING_TEXT[c.findingKind];
  const openOthers = others.filter(o => o.status.meaning === 'open').slice(0, 10);
  return (
    <>
      <PageHeader
        eyebrow="Claim"
        title={c.labelAsSeen}
        status={<StatusBadge status={c.status} />}
        subtitle={(
          <span className="flex flex-wrap items-center gap-1.5">
            <ToneBadge tone={vanished ? 'warning' : 'informative'}>{finding.label}</ToneBadge>
            {c.componentKind && <ToneBadge tone="subtle" icon={null}>{c.componentKind.label}</ToneBadge>}
            {samePrincipal(c.suggestedClaimant, me) && <ToneBadge tone="brand" icon={null}>Suggested to you</ToneBadge>}
          </span>
        )}
        meta={<DataStamp envelope={envelope} />}
        actions={(
          <>
            {/* Each act shows while it fits the claim; its sheet stays open after the write changes the status. */}
            <ClaimSheet claim={c} show={meaning === 'open' && !vanished && session.can('claim_resource')} />
            {meaning === 'open' && vanished && session.can('link_component') && <ResolveVanishedSheet claim={c} trigger={<Button>Resolve</Button>} />}
            <DismissSheet claim={c} variant={vanished ? 'still-there' : 'dismiss'} show={meaning === 'open' && session.can('dismiss_claim')} />
            <DismissSheet claim={c} variant="undo" show={meaning === 'dismissed' && session.can('dismiss_claim')} />
          </>
        )}
      />

      <div className={recordGrid}>
        <div className={column}>
          <Panel title="What the gap check found">
            <Facts>
              <Fact label="Finding" sub={finding.text} wide>{finding.label}</Fact>
              <Fact label="Seen as">{c.labelAsSeen}</Fact>
              <Fact label="Kind">{c.componentKind?.label ?? 'Not known'}</Fact>
              <Fact label="Where">{c.targetAsSeen ?? 'Not reported'}</Fact>
              <Fact label="Found">{fmtDateTime(c.foundOn)}</Fact>
              <Fact label="Last seen">{c.lastSeenOn ? fmtDateTime(c.lastSeenOn) : 'Not seen since'}</Fact>
              <Fact label="Suggested claimant" sub={c.suggestedClaimant ? 'From the maker the estate reports.' : undefined}>
                {c.suggestedClaimant ? (samePrincipal(c.suggestedClaimant, me) ? `You (${c.suggestedClaimant.displayLabel})` : c.suggestedClaimant.displayLabel) : 'Nobody suggested'}
              </Fact>
              <Fact label="Link" wide>{c.url ? <ExternalLink href={c.url}>Open it where it lives</ExternalLink> : 'No link reported'}</Fact>
            </Facts>
          </Panel>
          <Panel title={meaning === 'open' ? (vanished ? 'How to resolve it' : 'What claiming it does') : 'Where it stands'}>
            {meaning === 'open' && vanished ? (
              <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm leading-relaxed text-navy-2">
                <li>It is gone: the component is marked retired on its application, and the claim is closed.</li>
                <li>It moved: a new component with its new ID and link replaces the old one on the same application.</li>
                <li>It is still there: the gap check missed it. Dismiss the finding with the reason; a dismissal can be undone.</li>
              </ul>
            ) : meaning === 'open' ? (
              <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm leading-relaxed text-navy-2">
                <li>Claim it into an application you own: it is linked to that application at once.</li>
                <li>Claim it into an application someone else owns: it is linked, and an ownership decision goes to that application's owners to confirm it belongs there.</li>
                <li>Claim it as a new application: an intake request starts as your draft, with the item on a draft application, and triage decides.</li>
                <li>Not a business application at all? A platform admin dismisses it with the reason. A dismissal can be undone.</li>
              </ul>
            ) : meaning === 'claimed' ? (
              <ClaimedFacts claim={c} />
            ) : (
              <p className={plainText}>{vanished ? 'Dismissed: the item is still there. A platform admin can reopen it.' : 'Dismissed as not a business application. A platform admin can reopen it.'}</p>
            )}
          </Panel>
        </div>
        <div className={column}>
          <Panel
            title="Other open claims"
            count={openOthers.length}
            actions={<TextLink href={hrefTo({ page: 'gap-check' })} className="text-sm">Gap check</TextLink>}
          >
            {openOthers.length ? (
              <RowList label="Other open claims">
                {openOthers.map(o => {
                  const href = recordHrefOf('claim', o.claimId);
                  return (
                    <Row
                      key={o.claimId}
                      primary={href ? <TextLink href={href}>{o.labelAsSeen}</TextLink> : o.labelAsSeen}
                      secondary={[FINDING_TEXT[o.findingKind].label, o.componentKind?.label, o.targetAsSeen].filter(Boolean).join('. ')}
                      end={o.suggestedClaimant ? <span className="text-xs text-navy-3">{samePrincipal(o.suggestedClaimant, me) ? 'Suggested to you' : o.suggestedClaimant.displayLabel}</span> : undefined}
                    />
                  );
                })}
              </RowList>
            ) : <Empty>No other open claim you can see.</Empty>}
          </Panel>
        </div>
      </div>
    </>
  );
}

// Where a claimed item went: who claimed it, the application that holds it, and the intake request or the ownership
// decision the claim started, each linked to its page.
export function ClaimedFacts({ claim: c }: { claim: ClaimItem }) {
  const appLink = c.businessApplicationId ? hrefTo({ page: 'app', id: c.businessApplicationId.toLowerCase() }) : null;
  const intakeHref = c.intakeRequestId ? recordHrefOf('intake-request', c.intakeRequestId) : null;
  const decisionHref = c.ownershipDecisionId ? recordHrefOf('decision', c.ownershipDecisionId) : null;
  return (
    <Facts>
      <Fact label="Claimed by">{c.claimedBy?.displayLabel ?? 'Not recorded'}</Fact>
      <Fact label="Claimed on">{c.claimedOn ? fmtDateTime(c.claimedOn) : 'Not recorded'}</Fact>
      <Fact label="Application" wide>{appLink ? <TextLink href={appLink}>Open the application that holds it</TextLink> : 'Not recorded'}</Fact>
      {intakeHref && <Fact label="Intake request" sub="Triage decides the new application; its acceptance registers it." wide><TextLink href={intakeHref}>Open the intake request</TextLink></Fact>}
      {decisionHref && <Fact label="Ownership decision" sub="The application's owners confirm the item belongs there." wide><TextLink href={decisionHref}>Open the decision</TextLink></Fact>}
    </Facts>
  );
}

// claim_resource: into an application in the register, or as a new draft application (which starts an intake request).
// show adds its own trigger; a list page opens it for one row at a time with control instead.
// Wave 4: when the claim opened an ownership decision that was not sent in the same call, the
// submit follow-up opens right after ("Send it for approval now?"), as the data sheets do. A person's claim is sent in
// the same call (decisionSubmitted), so it needs none.
export function ClaimSheet({ claim: c, show, control }: { claim: ClaimItem; show: boolean; control?: SheetControl }) {
  const session = useSession();
  const kinds = useLabels('application-kind');
  const [mode, setMode] = useState<'existing' | 'new'>('existing');
  const [appId, setAppId] = useState('');
  const [appName, setAppName] = useState('');
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const [name, setName] = useState(c.labelAsSeen);
  const [purpose, setPurpose] = useState('');
  const [kind, setKind] = useState('');
  const [portfolio, setPortfolio] = useState('');
  const [ownOpen, setOwnOpen] = useState(false);
  const open = control ? control.open : ownOpen;
  const follow = useFollowUp();
  useEffect(() => {
    const t = window.setTimeout(() => setQuery(text.trim()), 300);
    return () => window.clearTimeout(t);
  }, [text]);
  // A new row chosen in a list starts from that row's name.
  useEffect(() => { setName(c.labelAsSeen); }, [c.claimId, c.labelAsSeen]);
  const me = session.objectId;
  const found = useTool('find_applications', open && mode === 'existing' && query ? { text: query, top: 8 } : null);
  const owned = useTool('list_owner_assignments', open && mode === 'existing' && me ? { principalObjectId: me, state: 'current', top: 50 } : null);
  const portfolios = useTool('list_portfolios', open && mode === 'new' ? { top: 100 } : null);
  const ownedApps = useMemo(() => {
    const seen = new Map<string, string>();
    for (const o of dataOf(owned)?.items ?? []) if (!seen.has(o.businessApplicationId)) seen.set(o.businessApplicationId, o.applicationName);
    return [...seen.entries()].map(([id, label]) => ({ id, label })).sort((a, b) => a.label.localeCompare(b.label));
  }, [owned]);
  const results = query ? (dataOf(found)?.items ?? []).map(a => ({ id: a.businessApplicationId, label: a.name })) : ownedApps;
  const reset = () => { setMode('existing'); setAppId(''); setAppName(''); setText(''); setName(c.labelAsSeen); setPurpose(''); setKind(''); setPortfolio(''); };
  const radio = 'size-4 accent-[var(--primary)]';
  // The follow-up stays while it is open, even once the claimed item no longer offers Claim.
  if (!control && !show && !open && !follow.data) return null;
  const close = () => { if (control) control.onOpenChange(false); else setOwnOpen(false); reset(); };
  return (
    <>
    <ActionSheet
      tool="claim_resource" site="pages/Records#ClaimSheet"
      title={`Claim: ${c.labelAsSeen}`}
      description="Say which application this item belongs to. Claiming into an application you do not own asks its owners to confirm it; a new application starts an intake request."
      trigger={!control && show ? <Button>Claim it</Button> : undefined}
      open={open}
      onOpenChange={next => { if (control) control.onOpenChange(next); else setOwnOpen(next); if (!next) reset(); }}
      size="wide"
      input={() => {
        if (mode === 'existing') {
          return appId ? { claimId: c.claimId, businessApplicationId: appId } : { problem: 'Choose the application it belongs to.', field: 'businessApplicationId' };
        }
        if (!name.trim()) return { problem: 'Give the new application a name.', field: 'newApplication.name' };
        if (!purpose.trim()) return { problem: 'Say what the new application is for.', field: 'newApplication.purpose' };
        if (!kind) return { problem: 'Choose the kind of application.', field: 'newApplication.applicationKindKey' };
        return { claimId: c.claimId, newApplication: { name: name.trim(), purpose: purpose.trim(), applicationKindKey: kind, ...(portfolio ? { portfolioId: portfolio } : {}) } };
      }}
      submitLabel="Claim it"
      preview
      onDone={(data, envelope) => {
        if (!data.ownershipDecisionId || data.decisionSubmitted) return;
        follow.start({ decisionId: data.ownershipDecisionId, answer: readEngineAnswer('claim_resource', envelope) });
        close();
      }}
      confirm={{
        title: 'Claim this item?',
        body: mode === 'existing'
          ? <p className="m-0">{`It is linked to ${appName || 'the chosen application'}. If you do not own that application, an ownership decision goes to its owners at once, to confirm it belongs there.`}</p>
          : <p className="m-0">{`A draft application, ${name.trim() || 'with the name you gave'}, holds it, and an intake request starts as your draft, naming you as business owner. Answer its tier questions and submit it; triage decides.`}</p>,
        confirmLabel: 'Claim it',
      }}
    >
      <fieldset className="m-0 flex flex-col gap-2 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">It belongs to</legend>
        <label className="flex cursor-pointer items-center gap-2 text-sm text-navy">
          <input type="radio" name="claim-mode" className={radio} checked={mode === 'existing'} onChange={() => setMode('existing')} />
          An application already in the register
        </label>
        <label className="flex cursor-pointer items-center gap-2 text-sm text-navy">
          <input type="radio" name="claim-mode" className={radio} checked={mode === 'new'} onChange={() => setMode('new')} />
          A new application: start an intake request
        </label>
      </fieldset>
      {mode === 'existing' ? (
        <Field label="Application" name="businessApplicationId" required hint={appId ? `Chosen: ${appName}.` : 'Search the register, or pick one you own.'}>
          {p => (
            <div className="flex flex-col gap-2">
              <div className="relative">
                <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
                <Input {...p} type="search" value={text} onChange={e => setText(e.target.value)} placeholder="Search by name or purpose" className="pl-8" maxLength={200} />
              </div>
              {query && found.status === 'loading' && <Muted>Searching.</Muted>}
              {query && found.status === 'done' && !found.envelope.ok && <Muted>{`The search could not be read: ${found.envelope.error.message}`}</Muted>}
              {!query && ownedApps.length > 0 && <span className={subhead}>Applications you own</span>}
              {results.length > 0 ? (
                <div role="radiogroup" aria-label="Applications" className="flex flex-col gap-1">
                  {results.map(a => (
                    <label key={a.id} className={cn('flex cursor-pointer items-center gap-2 rounded-md border px-3 py-2 text-sm text-navy', sameId(appId, a.id) ? 'border-primary bg-info-soft' : 'border-line')}>
                      <input type="radio" name="claim-app" className={radio} checked={sameId(appId, a.id)} onChange={() => { setAppId(a.id); setAppName(a.label); }} />
                      <span className="[overflow-wrap:anywhere]">{a.label}</span>
                    </label>
                  ))}
                </div>
              ) : query && found.status === 'done' && found.envelope.ok ? <Muted>No registered application matches.</Muted> : null}
            </div>
          )}
        </Field>
      ) : (
        <>
          <Field label="Name" name="newApplication.name" required>
            {p => <Input {...p} value={name} onChange={e => setName(e.target.value)} maxLength={200} />}
          </Field>
          <Field label="What it is for" name="newApplication.purpose" required>
            {p => <Textarea {...p} rows={3} maxLength={2000} value={purpose} onChange={e => setPurpose(e.target.value)} />}
          </Field>
          <SelectField label="Kind" name="newApplication.applicationKindKey" value={kind} onChange={setKind} choices={choicesOf(kinds)} required />
          <SelectField
            label="Portfolio"
            name="newApplication.portfolioId"
            value={portfolio}
            onChange={setPortfolio}
            choices={(dataOf(portfolios)?.items ?? []).filter(p => p.isActive).map(p => ({ value: p.portfolioId, label: p.path.length ? [...p.path.map(x => x.name), p.name].join(' / ') : p.name }))}
            noneLabel="Not placed yet"
          />
        </>
      )}
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The ownership decision for ${c.labelAsSeen}`} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

type DismissVariant = 'dismiss' | 'still-there' | 'undo';

const DISMISS_TEXT: Record<DismissVariant, { title: string; description: string; trigger: string; submit: string; ask: string; placeholder: string }> = {
  dismiss: {
    title: 'Dismiss',
    description: 'Records that this item is not a business application, with your reason. It can be undone.',
    trigger: 'Dismiss',
    submit: 'Dismiss',
    ask: 'Say why it is not a business application.',
    placeholder: 'A personal test flow, removed next week.',
  },
  'still-there': {
    title: 'Still there',
    description: 'The gap check missed it: the item is still in the estate. This dismisses the finding with your reason, and it can be undone.',
    trigger: 'Still there',
    submit: 'Dismiss the finding',
    ask: 'Say where you still see it.',
    placeholder: 'Still in Finance Production; the export left that environment out.',
  },
  undo: {
    title: 'Reopen',
    description: 'The claim goes back to open, so someone can claim or resolve it.',
    trigger: 'Reopen',
    submit: 'Reopen',
    ask: 'Say why it is reopened.',
    placeholder: '',
  },
};

// dismiss_claim: not a business application (or, for a vanished item, still there), with the reason; or undo a
// dismissal. show adds its own trigger; a list page opens it for one row at a time with control instead.
export function DismissSheet({ claim: c, variant, show, control }: { claim: ClaimItem; variant: DismissVariant; show: boolean; control?: SheetControl }) {
  const reasons = useListValues('dismiss-reason');
  const [reason, setReason] = useState('');
  const [reasonKey, setReasonKey] = useState('');
  const own = useSheet(() => { setReason(''); setReasonKey(''); });
  const undo = variant === 'undo';
  const text = DISMISS_TEXT[variant];
  const open = control ? control.open : own.open;
  if (!control && !show && !open) return null;
  return (
    <ActionSheet
      tool="dismiss_claim" site="pages/Records#DismissSheet"
      title={`${text.title}: ${c.labelAsSeen}`}
      description={text.description}
      trigger={!control && show ? <Button variant="outline">{undo && <Undo2 aria-hidden />}{text.trigger}</Button> : undefined}
      open={open}
      onOpenChange={next => { if (control) { control.onOpenChange(next); if (!next) { setReason(''); setReasonKey(''); } } else own.onOpenChange(next); }}
      input={() => (reason.trim()
        ? { claimId: c.claimId, reason: reason.trim(), ...(reasonKey && !undo ? { reasonKey } : {}), ...(undo ? { undo: true } : {}) }
        : { problem: text.ask, field: 'reason' })}
      submitLabel={text.submit}
      preview
    >
      {!undo && reasons.choices.length > 0 && (
        <SelectField label="Kind of reason" name="reasonKey" value={reasonKey} onChange={setReasonKey} choices={reasons.choices} noneLabel="None of these" />
      )}
      <Field label="Reason" name="reason" required hint="Stored with the claim.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} placeholder={text.placeholder} />}
      </Field>
    </ActionSheet>
  );
}
