// Application 360's Data panel (rendered at the top of app360/data.tsx): what business data the application uses, where,
// who owns that data, and whether a decision allows each use.
//   Data uses         the summary line ("Reads 3 domains, writes 1. Highest class: Restricted. Sensitivity answer:
//                     Internal (below the data)"), banners for DC-09 (the sensitivity answer is below the data), DC-10
//                     (data it reads that no use declares) and DC-11 (a use with no mapping), the other findings, and each
//                     use with its state and the AI mark "Drafted by Claude Code for Priya Castell"
//   Data owners       the data owners its uses lead to, each with where the owner is set, and a domain nobody owns
//   Where it sits     the locations its uses reach
//   Retention         while the application retires, the data-retention decision of each use (DC-13)
// Reads: get_application_card (section data), get_tier_assessment (the sensitivity answer; Application 360 shares the
// read), the decision log of Application 360's context. Writes, in the shared side sheets: declare a use (DataUseSheet,
// check_data_use as its preview, then save_data_use), confirm an AI draft, end a use, map the data, propose a retention
// decision (DataDecisionSheet). The engine checks every one; a use whose rule is never is refused when saved.
import { ArrowRight, Database, Plus, ShieldCheck, Square, TriangleAlert, UserRoundX } from 'lucide-react';
import { hrefTo, recordHrefOf } from '../../app/links';
import { Empty, Muted, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Button } from '../../components/ui/button';
import type { ApplicationCardData, DataUseItem, DecisionLogItem } from '../../data/contracts';
import { fmtDate, plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { useApp360, type App360Context } from '../app360/context';
import { retirementStep, sensitivityAnswer } from '../app360/logic';
import { tabHref } from '../app360/nav';
import { DataDecisionSheet, DataMappingSheet, DataUseSheet } from '../data/sheets';
import { ActionSheet } from '../../components/actions';
import { aiUsesOf, dataSummary, isDraftRow, locationsOfUses, retentionRows, splitFindings, type DataSummary } from './logic';
import { FindingRow, UseRow } from './parts';
import { Sticky } from '../work-approvals/parts';
import { rowHref } from './reads';

const columns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

export function App360DataPanel({ applicationId }: { applicationId: string }) {
  const ctx = useApp360();
  const card = useRead('get_application_card', { businessApplicationId: applicationId, sections: ['data'] });
  const env = card.status === 'done' ? card.envelope : null;
  // An install that does not use the data taxonomy, or a server without the data section, shows nothing here.
  if (env?.ok && !env.data.data) return null;
  if (!ctx) return null;
  return (
    <div className={`${columns} mb-4 narrow:mb-3`}>
      <div className={stack}>
        <Panel
          title="Data uses"
          subtitle="Which business data it reads or writes, where, and whether a decision allows each use."
          state={card}
          count={env?.ok ? env.data.data?.uses.length : undefined}
          skeletonRows={4}
          id="data-uses"
          actions={ctx.can('save_data_use') ? (
            <DataUseSheet
              applicationId={applicationId}
              applicationName={ctx.applicationName}
              mode="declare"
              trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Declare a data use</Button>}
            />
          ) : undefined}
        >
          {c => (c.data ? <UsesBody ctx={ctx} data={c.data} /> : null)}
        </Panel>
      </div>
      <div className={stack}>
        {env?.ok && env.data.data && (
          <>
            <OwnersPanel data={env.data.data} applicationId={applicationId} />
            <LocationsPanel uses={env.data.data.uses} />
            <RetentionPanel ctx={ctx} data={env.data.data} />
          </>
        )}
      </div>
    </div>
  );
}

// ---------- data uses ----------

// Marks the application as holding no business data, which meets the data-uses-declared gate requirement (O02 Holds No
// Business Data; data design review: the DC-10 fix had no action in the web app).
function NoBusinessDataSheet({ ctx }: { ctx: App360Context }) {
  const app = ctx.card!.application;
  return (
    <ActionSheet
      tool="save_application_draft" site="pages/data-records/App360DataPanel#NoBusinessDataSheet"
      title={`${app.name} holds no business data`}
      description="Say so only when the application reads, writes and moves no business data: no records about customers, staff, money or operations. The design gate then counts its data uses as declared. An owner changes it back here at any time."
      trigger={<Button size="sm" variant="outline">It holds no business data</Button>}
      input={() => ({ businessApplicationId: app.businessApplicationId, name: app.name, purpose: app.purpose, applicationKindKey: app.kind.key, holdsNoBusinessData: true })}
      submitLabel="Mark it"
    />
  );
}

function UsesBody({ ctx, data }: { ctx: App360Context; data: ApplicationCardData }) {
  const assessment = useRead('get_tier_assessment', { businessApplicationId: ctx.applicationId });
  const sensitive = sensitivityAnswer(dataOf(assessment)?.answers ?? []);
  const summary = dataSummary({ uses: data.uses, highest: data.highestClassification, sensitivity: sensitive?.answer ?? null, findings: data.findings, holdsNoBusinessData: data.holdsNoBusinessData });
  const found = splitFindings(data.findings);
  const usesHref = hrefTo({ page: 'data', tab: 'uses', applicationId: ctx.applicationId });
  const canSave = ctx.can('save_data_use');
  const canMap = ctx.can('save_data_mapping');
  // An owner of the application or the platform admin marks it as holding no business data (DC-10's other fix).
  const canMarkNoData = ctx.can('save_application_draft') && !data.holdsNoBusinessData && !!ctx.card
    && (ctx.ownerRoles.length > 0 || ctx.session.hasRole('platform-admin'));
  const open = data.uses.filter(u => !u.endedOn);
  const drafts = open.filter(isDraftRow);
  const ai = aiUsesOf(open);
  return (
    <div className="flex flex-col gap-3">
      <SummaryLine summary={summary} applicationName={ctx.applicationName} usesHref={usesHref} tierHref={tabHref(ctx.applicationId, 'tier')} />
      {(ai.length > 0 || drafts.length > 0) && (
        <p className="m-0 flex flex-wrap gap-x-3 gap-y-1 text-xs text-navy-3">
          {ai.length > 0 && <span>{`${plural(ai.length, 'use sends', 'uses send')} data to AI.`}</span>}
          {drafts.length > 0 && <span>{`${plural(drafts.length, 'draft waits', 'drafts wait')} for a person to confirm.`}</span>}
        </p>
      )}

      {found.dc09.length > 0 && (
        <Alert variant="warning">
          <TriangleAlert aria-hidden />
          <AlertTitle>The sensitivity answer is below the data</AlertTitle>
          <AlertDescription>
            <div className="flex flex-col gap-1.5">
              {found.dc09.map((f, i) => <p key={i} className="m-0">{f.message}</p>)}
              <TextLink href={tabHref(ctx.applicationId, 'tier')} className="self-start text-sm">Answer the tier questions again</TextLink>
            </div>
          </AlertDescription>
        </Alert>
      )}
      {/* Sticky (QA, fix round 1): the alert stays mounted, hidden, once a save clears its finding, so the open "Declare
          it" sheet shows the engine's answer instead of closing with it. */}
      <Sticky show={found.dc10.length > 0}>
        <Alert variant="info">
          <Database aria-hidden />
          <AlertTitle>Data it reaches that no use declares</AlertTitle>
          <AlertDescription>
            <div className="flex flex-col gap-1.5">
              <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5">
                {found.dc10.map((f, i) => <li key={i}>{f.message}</li>)}
              </ul>
              {/* Sticky (wave 4b review): marking the application turns canMarkNoData false, and the open sheet stays
                  mounted until the person has read its answer. */}
              <span className="flex flex-wrap gap-2 self-start empty:hidden">
                {canSave && <DataUseSheet applicationId={ctx.applicationId} applicationName={ctx.applicationName} mode="declare" trigger={<Button size="sm" variant="outline">Declare it</Button>} />}
                <Sticky show={canMarkNoData}><NoBusinessDataSheet ctx={ctx} /></Sticky>
              </span>
            </div>
          </AlertDescription>
        </Alert>
      </Sticky>
      {found.dc11.length > 0 && (
        <Alert variant="info">
          <Database aria-hidden />
          <AlertTitle>Uses with no mapping</AlertTitle>
          <AlertDescription>
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
              {found.dc11.map((f, i) => (
                <li key={i} className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <span>{f.message}</span>
                  {canMap && f.domain && (
                    <DataMappingSheet mode="add" domainKey={f.domain.key} trigger={<Button size="sm" variant="outline" aria-label={`Map the data of ${f.domain.path}`}>Map the data</Button>} />
                  )}
                </li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {data.uses.length ? (
        <RowList label="Data uses">
          {data.uses.map(u => (
            <UseRow
              key={u.dataUseId}
              use={u}
              href={rowHref(u.recordUrl, u.domain.key)}
              show="domain"
              actions={canSave && !u.endedOn ? <UseActions ctx={ctx} use={u} /> : undefined}
            />
          ))}
        </RowList>
      ) : (
        <Empty>
          {data.holdsNoBusinessData
            ? 'Its owner states it holds no business data.'
            : 'No data use is declared yet. Declare what business data it reads or writes, so the data owner can decide what it may do.'}
        </Empty>
      )}

      {found.other.length > 0 && (
        <div className="flex flex-col gap-1.5">
          <h3 className="m-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">What else is missing</h3>
          <RowList label="Other data findings">
            {found.other.map((f, i) => <FindingRow key={`${f.check}-${f.subjectId}-${i}`} finding={f} />)}
          </RowList>
        </div>
      )}
      <TextLink href={usesHref} className="inline-flex items-center gap-1 self-start text-sm">
        Its uses on the Data page <ArrowRight className="size-3.5" aria-hidden />
      </TextLink>
    </div>
  );
}

// "Reads 3 domains, writes 1. Highest class: Restricted. Sensitivity answer: Internal (below the data)." Each count opens
// the uses; the answer opens the tier answers.
function SummaryLine({ summary, applicationName, usesHref, tierHref }: { summary: DataSummary; applicationName: string; usesHref: string; tierHref: string }) {
  return (
    <p className="m-0 text-sm leading-relaxed text-navy">
      {summary.none
        ? `${applicationName} ${summary.none}.`
        : <>Reads <TextLink href={usesHref}>{plural(summary.reads, 'domain', 'domains')}</TextLink>, writes <TextLink href={usesHref}>{summary.writes}</TextLink>.</>}
      {summary.highest && <> Highest class: <span className="font-semibold">{summary.highest.label}</span>.</>}
      {summary.sensitivity
        ? <> Sensitivity answer: <TextLink href={tierHref}>{summary.sensitivity}</TextLink>{summary.belowData && <span className="font-semibold text-warning"> (below the data)</span>}.</>
        : summary.belowData ? <> <span className="font-semibold text-warning">The sensitivity answer is below the data.</span></> : null}
    </p>
  );
}

function UseActions({ ctx, use }: { ctx: App360Context; use: DataUseItem }) {
  const what = use.domain.path;
  return (
    <>
      {isDraftRow(use) && (
        <DataUseSheet
          applicationId={ctx.applicationId}
          applicationName={ctx.applicationName}
          mode="confirm"
          use={use}
          trigger={<Button size="sm" aria-label={`Check and confirm the use of ${what}`}><ShieldCheck aria-hidden />Confirm</Button>}
        />
      )}
      {!isDraftRow(use) && (
        <DataUseSheet
          applicationId={ctx.applicationId}
          applicationName={ctx.applicationName}
          mode="end"
          use={use}
          trigger={<Button size="sm" variant="outline" aria-label={`End the use of ${what}`}><Square aria-hidden />End</Button>}
        />
      )}
    </>
  );
}

// ---------- the data owners its uses lead to ----------

function OwnersPanel({ data, applicationId }: { data: ApplicationCardData; applicationId: string }) {
  const owners = data.dataOwners;
  const missing = owners.filter(o => !o.principal).length;
  return (
    <Panel
      title="Data owners from its uses"
      count={owners.length}
      subtitle="The owner of each domain it uses decides what it may do with that data. An owner set higher up the tree answers for the domains below."
    >
      {owners.length ? (
        <>
          {missing > 0 && (
            <p className="m-0 flex items-start gap-1.5 text-sm text-navy">
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
              <span>{`${plural(missing, 'domain it uses has', 'domains it uses have')} no data owner, so a decision there has no one to ask.`}</span>
            </p>
          )}
          <RowList label="Data owners from its uses">
            {owners.map(o => (
              <Row
                key={o.domain.key}
                lead={o.principal ? undefined : <span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" /></span>}
                primary={<DomainLink domain={o.domain} />}
                secondary={o.principal
                  ? `${o.principal.displayLabel}${o.inheritedFrom ? `, from ${o.inheritedFrom.label}` : ''}`
                  : 'No data owner'}
                end={(
                  <TextLink href={hrefTo({ page: 'data', tab: 'uses', applicationId, domainKey: o.domain.key })} className="text-xs">
                    {plural(o.dataUseIds.length, 'use', 'uses')}
                  </TextLink>
                )}
              />
            ))}
          </RowList>
        </>
      ) : <Empty>No data owner yet: it declares no use of a domain.</Empty>}
    </Panel>
  );
}

// ---------- where its data sits ----------

function LocationsPanel({ uses }: { uses: DataUseItem[] }) {
  const places = locationsOfUses(uses);
  return (
    <Panel title="Where it reaches the data" count={places.length} subtitle="The data locations its uses name.">
      {places.length ? (
        <RowList label="Where it reaches the data">
          {places.map(p => (
            <Row
              key={p.location.dataLocationId}
              primary={<TextLink href={recordHrefOf('data-location', p.location.dataLocationId) ?? '#'}>{p.location.label}</TextLink>}
              secondary={[p.location.platform.label, p.location.stage?.label].filter(Boolean).join(', ')}
              tertiary={p.domains.map(d => d.path).join('; ')}
            />
          ))}
        </RowList>
      ) : <Empty>No use names a location yet.</Empty>}
    </Panel>
  );
}

// ---------- retention when it retires ----------

const RETENTION_TEXT = { decided: 'Decided', waiting: 'Waiting for approval', open: 'No retention decision' } as const;

function RetentionPanel({ ctx, data }: { ctx: App360Context; data: ApplicationCardData }) {
  const stages = useLabels('lifecycle-stage');
  const app = ctx.card?.application ?? null;
  const meaning = app?.stage ? stages.field<string>(app.stage.key, 'engineMeaning') ?? null : null;
  const step = retirementStep(ctx.appDecisions, meaning);
  const retiring = !!step.decided || step.retired;
  // One row per sentence: two uses of one domain at two places give the same DC-13 finding words (wave 4b QA walk).
  const dc13 = data.findings.filter(f => f.check === 'DC-13').filter((f, i, all) => all.findIndex(o => o.message === f.message) === i);
  if (!retiring && !dc13.length) return null;
  const log: DecisionLogItem[] = dataOf(ctx.decisionLog)?.items ?? [];
  const rows = retentionRows(data.uses, log);
  const canPropose = ctx.can('save_decision_proposal');
  const openCount = rows.filter(r => r.state === 'open').length;
  return (
    <Panel
      title="Retention"
      count={rows.length}
      subtitle={step.retired
        ? 'It is retired. Each use of its data has a retention decision: keep, archive, delete or transfer.'
        : 'It is on its way out. Before retirement is complete, each use of its data needs a retention decision: keep, archive, delete or transfer.'}
      state={ctx.decisionLog}
      notes={[]}
    >
      {() => (
        <div className="flex flex-col gap-2">
          {openCount > 0 && <Muted>{`${plural(openCount, 'use has', 'uses have')} no retention decision yet.`}</Muted>}
          {rows.length ? (
            <RowList label="Retention of each use">
              {rows.map(r => (
                <Row
                  key={r.use.dataUseId}
                  primary={<DomainLink domain={r.use.domain} />}
                  secondary={[r.use.recordNumber, r.decision?.outcome ? `Outcome: ${r.decision.outcome.label}` : null, r.decision?.decidedOn ? `Decided ${fmtDate(r.decision.decidedOn)}` : null].filter(Boolean).join('. ') || undefined}
                  tertiary={r.decision ? <TextLink href={hrefTo({ page: 'decision', id: r.decision.decisionId })} className="font-normal">The retention decision</TextLink> : undefined}
                  end={r.decision ? <StatusBadge status={r.decision.status} /> : <ToneBadge tone="warning">{RETENTION_TEXT.open}</ToneBadge>}
                  endSub={r.state === 'open' && canPropose ? (
                    <DataDecisionSheet
                      subjectKind="data-use"
                      subjectId={r.use.dataUseId}
                      decisionTypeKey="data-retention"
                      label={`Retention of ${r.use.domain.path}`}
                      trigger={<Button size="sm" variant="outline" className="mt-1" aria-label={`Propose the retention of ${r.use.domain.path}`}>Propose retention</Button>}
                    />
                  ) : r.state === 'waiting' ? RETENTION_TEXT.waiting : undefined}
                  wideEnd
                />
              ))}
            </RowList>
          ) : <Empty>No open data use, so no retention decision is needed.</Empty>}
          {dc13.length > 0 && (
            <RowList label="Retention findings">
              {dc13.map((f, i) => <FindingRow key={`${f.subjectId}-${i}`} finding={f} />)}
            </RowList>
          )}
        </div>
      )}
    </Panel>
  );
}
