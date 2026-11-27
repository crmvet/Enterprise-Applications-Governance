// Data (P02-DATA, UC-13): what the application's data is, who owns it, and what it may do with it. At the top, the data
// taxonomy part (D-37, pages/data-records/App360DataPanel.tsx): the uses with their state, the derived data owners,
// where it reaches the data, retention. Below, the sensitive-data answer from the latest tier assessment; the
// application's data owner and deputy, with a proposal or a hand-over for a gap; the data decisions about it and its
// uses with their yeses (restricted data to AI at tier 3 takes two: the data owner and the security reviewer); the
// "reads data from" and "writes data to" links, both ways, with a write on each.
// Reads: get_tier_assessment, get_dependency_impact (one hop, both directions), and the shared owners and decisions.
import { ArrowRight, Database, Plus, UserRoundX } from 'lucide-react';
import { useState } from 'react';
import { hrefTo, recordHref } from '../../app/links';
import { Empty, LinkButton, Muted, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { DependencyImpact, ImpactNode } from '../../data/contracts';
import { fmtDate } from '../../data/format';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { dataOf, type ToolState } from '../../data/toolClient';
import { App360DataPanel } from '../data-records';
import { dataDecisionsOf } from '../data-records/logic';
import { ProposeOwnerChangeSheet } from '../owners-reviews/OwnerSheets';
import { ownsOrAdmin, useApp360, type App360Context } from './context';
import { criticalityTone, mayEditEdge, deputiesOf, edgeOf, holderOf, isDataDependency, sensitivityAnswer, yesesText } from './logic';
import { tabHref } from './nav';
import { DependencyChangeSheet, HandOverSheet, NewDependencySheet, ProposeDecisionSheet, type DependencyEdit } from './sheets';

const twoColumns = 'grid grid-cols-2 items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const DATA_OWNER = 'data-owner';

export function DataSection() {
  const ctx = useApp360();
  if (!ctx) return null;
  return <DataBody ctx={ctx} />;
}

function DataBody({ ctx }: { ctx: App360Context }) {
  const assessment = useRead('get_tier_assessment', { businessApplicationId: ctx.applicationId });
  const needs = useRead('get_dependency_impact', { businessApplicationId: ctx.applicationId, direction: 'dependencies', depth: 1 });
  const usedBy = useRead('get_dependency_impact', { businessApplicationId: ctx.applicationId, direction: 'dependants', depth: 1 });
  const sensitive = sensitivityAnswer(dataOf(assessment)?.answers ?? []);
  const app = ctx.card?.application ?? null;
  return (
    <>
    {/* The data taxonomy part (D-37): uses with state, derived data owners, DC-09 to DC-11. pages/data-records. */}
    <App360DataPanel applicationId={ctx.applicationId} />
    <div className={twoColumns}>
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        <Panel title="The data" subtitle="From the latest tier answers." state={assessment} skeletonRows={2}>
          {() => (
            <div className="flex flex-col gap-2">
              <div className="flex flex-wrap items-center gap-2 text-sm text-navy">
                <Database className="size-4 text-navy-3" aria-hidden />
                <span className="font-semibold">{sensitive ? sensitive.answer : 'Not answered yet'}</span>
                {sensitive?.impliedTier && <span className="text-xs text-navy-3">implies {sensitive.impliedTier.label}</span>}
                {sensitive?.isDriving && <ToneBadge tone="warning" icon={null}>Sets the tier</ToneBadge>}
              </div>
              <Muted>{sensitive ? sensitive.question : 'The sensitive-data question is one of the tier questions. Answer them on the Tier section.'}</Muted>
              {app && (
                <Muted>
                  {app.usesAi === true ? `Uses AI${app.agreedAutonomyLevel ? `, agreed autonomy: ${app.agreedAutonomyLevel.label}` : ', no autonomy level agreed yet'}.` : app.usesAi === false ? 'Uses no AI, as its owner states.' : 'Whether it uses AI is not stated.'}
                </Muted>
              )}
              <TextLink href={tabHref(ctx.applicationId, 'tier')} className="inline-flex items-center gap-1 self-start text-sm">Tier answers <ArrowRight className="size-3.5" aria-hidden /></TextLink>
            </div>
          )}
        </Panel>
        <DataOwnerPanel ctx={ctx} />
        <DataUsePanel ctx={ctx} />
      </div>
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        <DataLinksPanel ctx={ctx} needs={needs} usedBy={usedBy} />
      </div>
    </div>
    </>
  );
}

function DataOwnerPanel({ ctx }: { ctx: App360Context }) {
  const roles = useLabels('owner-role');
  const holder = holderOf(ctx.owners, DATA_OWNER, ctx.today);
  const deputies = deputiesOf(ctx.owners, DATA_OWNER, ctx.today);
  const pending = ctx.owners?.owners.filter(o => o.ownerRoleKey === DATA_OWNER && !o.startOn) ?? [];
  const known = roles.has(DATA_OWNER);
  const mayPropose = ctx.can('propose_owner_change') && ownsOrAdmin(ctx);
  if (!known && roles.keys().length) return null;
  return (
    <Panel
      title="Data owner"
      subtitle="Decides what the application may do with its data. At tiers 2 and 3 a data owner is named before go-live."
      actions={mayPropose ? (
        <HandOverSheet
          applicationId={ctx.applicationId}
          applicationName={ctx.applicationName}
          ownerRoleKey={DATA_OWNER}
          holder={holder}
          deputy={deputies[0] ?? null}
          trigger={<Button size="sm" variant="outline">Hand over</Button>}
          onProposed={ctx.onProposed}
        />
      ) : undefined}
    >
      {ctx.owners === null ? <Muted>Reading the owners.</Muted> : (
        <RowList label="Data owner and deputies">
          {holder ? (
            <Row primary={holder.principal.displayLabel} secondary="Data owner" endSub={holder.startOn ? `since ${fmtDate(holder.startOn)}` : undefined} />
          ) : (
            <Row
              lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" /></span>}
              primary="No data owner"
              secondary="An owner change names one. It needs a decision by a person."
              end={mayPropose ? (
                <ProposeOwnerChangeSheet
                  application={{ id: ctx.applicationId, name: ctx.applicationName }}
                  mode="add"
                  ownerRoleKey={DATA_OWNER}
                  me={ctx.session.who}
                  onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
                  trigger={<Button size="sm" variant="outline">Propose a data owner</Button>}
                />
              ) : undefined}
            />
          )}
          {deputies.map(d => <Row key={d.ownerAssignmentId} primary={d.principal.displayLabel} secondary="Deputy data owner" endSub={d.startOn ? `since ${fmtDate(d.startOn)}` : undefined} />)}
          {holder && !deputies.length && (
            <Row
              primary="No deputy"
              secondary="A data owner needs a deputy."
              end={mayPropose ? (
                <ProposeOwnerChangeSheet
                  application={{ id: ctx.applicationId, name: ctx.applicationName }}
                  mode="add"
                  ownerRoleKey={DATA_OWNER}
                  isDeputy
                  me={ctx.session.who}
                  onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
                  trigger={<Button size="sm" variant="outline">Propose a deputy</Button>}
                />
              ) : undefined}
            />
          )}
          {pending.map(p => <Row key={p.ownerAssignmentId} primary={p.principal.displayLabel} secondary={`${p.isDeputy ? 'Deputy data owner' : 'Data owner'}, waiting for its decision`} />)}
        </RowList>
      )}
    </Panel>
  );
}

function DataUsePanel({ ctx }: { ctx: App360Context }) {
  // The data decisions about the application itself and about its uses and copies (D-37): data use and data retention.
  const list = dataDecisionsOf(dataOf(ctx.decisionLog)?.items ?? []);
  const canPropose = ctx.proposable.includes('data-use');
  return (
    <Panel
      title="Data decisions"
      subtitle="What the application may do with its data, such as send restricted data to an approved AI model, and what happens to it at retirement."
      state={ctx.decisionLog}
      notes={[]}
      count={list.length}
      actions={canPropose ? (
        <ProposeDecisionSheet
          applicationId={ctx.applicationId}
          applicationName={ctx.applicationName}
          typeKey="data-use"
          title="Propose a data-use decision"
          trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Propose</Button>}
          onProposed={ctx.onProposed}
        />
      ) : undefined}
    >
      {() => (list.length ? (
        <RowList label="Data-use decisions">
          {list.map(d => (
            <Row
              key={d.decisionId}
              primary={<TextLink href={recordHref(d.recordUrl) ?? '#'}>{d.statement}</TextLink>}
              secondary={[
                d.subjectKind !== 'business-application' && d.subjectLabel ? `${d.decisionType.label}: ${d.subjectLabel}` : d.decisionType.key !== 'data-use' ? d.decisionType.label : null,
                d.outcome ? `Outcome: ${d.outcome.label}` : null,
                d.decidedOn ? `Decided ${fmtDate(d.decidedOn)}` : null,
              ].filter(Boolean).join('. ') || undefined}
              tertiary={d.status.meaning === 'proposed' ? yesesText(d.approvalsSummary) ?? 'Not submitted yet' : undefined}
              badges={<AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />}
              end={<StatusBadge status={d.status} />}
            />
          ))}
        </RowList>
      ) : <Empty>No data decision is recorded.</Empty>)}
    </Panel>
  );
}

function DataLinksPanel({ ctx, needs, usedBy }: { ctx: App360Context; needs: ToolState<DependencyImpact>; usedBy: ToolState<DependencyImpact> }) {
  const types = useLabels('dependency-type');
  const mayWrite = ctx.can('save_dependency');
  const owns = ctx.ownerRoles.length > 0;
  const wideRole = ctx.session.hasRole('architect', 'platform-admin');
  const out = (dataOf(needs)?.nodes ?? []).filter(n => isDataDependency(n.dependencyTypeKey ?? ''));
  const into = (dataOf(usedBy)?.nodes ?? []).filter(n => isDataDependency(n.dependencyTypeKey ?? ''));
  const components = (ctx.card?.dependencies?.top ?? []).filter(e => e.technicalComponentId && isDataDependency(e.dependencyType.key));
  const names = new Map<string, string>();
  // The link being changed or ended; its sheet lives outside the list, so an ended link keeps the engine's answer in view.
  const [editing, setEditing] = useState<(DependencyEdit & { n: number }) | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const edit = (next: DependencyEdit) => {
    setEditing(e => ({ ...next, n: (e?.n ?? 0) + 1 }));
    setSheetOpen(true);
  };
  const rowOf = (side: 'dependencies' | 'dependants', n: ImpactNode) => {
    const edge = edgeOf(side, n, ctx.applicationId, names, ctx.applicationName);
    const typeKey = n.dependencyTypeKey ?? '';
    const words = side === 'dependencies' ? types.label(typeKey) : types.field<string>(typeKey, 'inverseLabel') ?? types.label(typeKey);
    return (
      <Row
        key={`${side}-${n.businessApplicationId}-${n.dependencyTypeKey}`}
        primary={<TextLink href={hrefTo({ page: 'app', id: n.businessApplicationId })}>{n.name}</TextLink>}
        secondary={words}
        tertiary={mayWrite && mayEditEdge(edge.fromApplicationId, ctx.applicationId, owns, wideRole) ? (
          <span className="flex flex-wrap gap-x-3">
            <LinkButton className="text-xs" aria-label={`Change the link with ${n.name}`} onClick={() => edit({ edge, criticalityKey: n.criticalityKey, mode: 'change' })}>Change</LinkButton>
            <LinkButton className="text-xs" aria-label={`End the link with ${n.name}`} onClick={() => edit({ edge, criticalityKey: n.criticalityKey, mode: 'end' })}>End</LinkButton>
          </span>
        ) : undefined}
        end={<ToneBadge tone={criticalityTone(n.criticalityKey)} icon={null}>{n.criticalityKey ? `${n.criticalityKey.charAt(0).toUpperCase()}${n.criticalityKey.slice(1)} criticality` : 'Criticality not set'}</ToneBadge>}
      />
    );
  };
  const failed = [needs, usedBy].map(s => (s.status === 'done' && !s.envelope.ok ? s.envelope : null)).find(Boolean) ?? null;
  return (
    <Panel
      title="Where its data goes"
      subtitle="Reads data from, writes data to, and the applications that read or write its data."
      state={needs}
      notes={[]}
      skeletonRows={4}
      actions={mayWrite ? (
        <NewDependencySheet applicationId={ctx.applicationId} applicationName={ctx.applicationName} dataOnly trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Add a data link</Button>} />
      ) : undefined}
    >
      {() => (
        <div className="flex flex-col gap-2">
          {failed && <Muted>{`Part of the links could not be read: ${failed.error.message}`}</Muted>}
          {out.length + into.length + components.length ? (
            <RowList label="Data links">
              {out.map(n => rowOf('dependencies', n))}
              {into.map(n => rowOf('dependants', n))}
              {components.map(c => (
                <Row key={`component-${c.technicalComponentId}`} primary={c.name} secondary={`${c.dependencyType.label}, a component`} end={<ToneBadge tone={criticalityTone(c.criticality?.key)} icon={null}>{c.criticality ? `${c.criticality.label} criticality` : 'Criticality not set'}</ToneBadge>} />
              ))}
            </RowList>
          ) : <Empty>No data link is recorded.</Empty>}
          <TextLink href={hrefTo({ page: 'map', id: ctx.applicationId })} className="inline-flex items-center gap-1 self-start text-sm">Dependency map <ArrowRight className="size-3.5" aria-hidden /></TextLink>
          {editing && (
            <DependencyChangeSheet
              key={editing.n}
              edge={editing.edge}
              criticalityKey={editing.criticalityKey}
              mode={editing.mode}
              today={ctx.today}
              open={sheetOpen}
              onOpenChange={setSheetOpen}
            />
          )}
        </div>
      )}
    </Panel>
  );
}
