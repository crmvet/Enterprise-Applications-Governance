// Page 2: Application 360. On binding A it sits in a tab of the Application main form and receives the record ID.
// Reads (document 24 names): get_application_card (every section except owners), get_application_owners,
// get_configuration (owner-role and target-stage labels). Two custom API calls, so the six-table limit of
// generative pages does not bind this page (the card reads its tables on the server).
// In the harness the page is read-only: edits happen in the form, or through an AI client, and every write goes through
// the engine. In the web app (D-36) people act here: the page wraps itself in the App 360 scope (./app360), adds the
// section bar (?tab=), the summary's actions and the other sections; see app360/index.tsx.
// In the web app the route is /app/records/business-application/<id>, the record link the tools return.
import { ArrowRight, CalendarDays, ExternalLink as ExternalLinkIcon, Info, Sparkles, UserRoundX, UsersRound, Workflow } from 'lucide-react';
import type { ReactNode } from 'react';
import type { ApplicationCard, ApplicationOwners } from '../data/contracts';
import { externalHref, fmtDate, fmtWindow } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { recordHrefOf, useQueryParam } from '../app/links';
import { ProposeOwnerChangeSheet } from './owners-reviews/OwnerSheets';
import {
  App360Panels,
  App360Scope,
  App360TabBar,
  ChangesPanelActions,
  ChangeTypeBadge,
  CriticalityBadge,
  DeploymentsPanelActions,
  DocumentRowActions,
  DraftBanner,
  LifecycleStrip,
  OwnerRowActions,
  OwnersPanelActions,
  ProposalNotice,
  ReadDedupe,
  ReviewRowActions,
  ReviewsPanelActions,
  TAB_PATTERN,
  changeStamp,
  deploymentExtras,
  portfolioPathOf,
  tabHref,
  tabOf,
  useApp360,
} from './app360';
import { FlagBadges } from './changes-records/parts';
// Wave 4 hook point (the teams group builds it): an owner that is a group links to its team page, in the web app only.
import { OwnerHolderName } from './teams';
// Wave 4: "What depends on it?" from a component (get_dependency_impact technicalComponentId), in the web app only.
import { WhatDependsOnIt } from './data-records/WhatDependsOnIt';
import { cn } from '../lib/utils';
import { ExternalLink, Row, RowList } from '../components/rows';
import { DataStamp, Empty, LinkButton, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../components/blocks';
import { AiMark } from '../components/stamps';
import { Avatar, AvatarFallback } from '../components/ui/avatar';
import { Button } from '../components/ui/button';
import type { Tone } from '../data/meanings';

// The badges other pages import from here; they live in ./app360/parts.
export { ChangeTypeBadge, CriticalityBadge, criticalityTone } from './app360';

const CARD_SECTIONS = ['components', 'dependencies', 'decisions', 'changes', 'deployments', 'documents', 'reviews', 'links'];

// Below 720 px the two columns dissolve (display: contents) into one list, and these set the phone order:
// owners, deployments, changes, documents, reviews, dependencies, decisions, components, links.
// Written out in full so Tailwind finds each class in the source.
const order = {
  owners: 'narrow:order-1',
  deployments: 'narrow:order-2',
  changes: 'narrow:order-3',
  documents: 'narrow:order-4',
  reviews: 'narrow:order-5',
  dependencies: 'narrow:order-6',
  decisions: 'narrow:order-7',
  components: 'narrow:order-8',
  links: 'narrow:order-9',
} as const;

const columnClass = 'flex min-w-0 flex-col gap-y-4 narrow:contents';
const panelLinkClass = 'inline-flex items-center gap-x-1 font-semibold';
const factLabelClass = 'm-0 text-xs text-navy-3';
const subheadClass = 'mt-1.5 mb-0.5 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

// Identical reads of the page's many parts share one request (app360/dedupe.ts).
export function Application360({ applicationId }: { applicationId: string }) {
  return <ReadDedupe><Application360Page applicationId={applicationId} /></ReadDedupe>;
}

function Application360Page({ applicationId }: { applicationId: string }) {
  const host = useHost();
  // In the web app every row opens its record page, and people act on the application here (D-36). The harness has no
  // record pages and stays read-only.
  const webApp = !!host.open;
  const link = (entity: string, id: string | null | undefined) => (webApp && id ? recordHrefOf(entity, id) : null);
  const card = useTool('get_application_card', { businessApplicationId: applicationId, sections: CARD_SECTIONS });
  const owners = useTool('get_application_owners', { businessApplicationId: applicationId });
  const ownerRoles = useLabels('owner-role');
  const stages = useLabels('target-stage');
  const tabParam = useQueryParam('tab', TAB_PATTERN);
  const tab = webApp ? tabOf(tabParam) : 'summary';

  const cardEnv = card.status === 'done' ? card.envelope : undefined;
  const data = dataOf(card);
  const app = data?.application;

  if (cardEnv && !cardEnv.ok) {
    return (
      <>
        <PageHeader eyebrow="Application" title="Application 360" />
        <PanelFailure failure={cardEnv} />
      </>
    );
  }

  const mapHref = host.href({ page: 'map', id: applicationId });
  const recordUrl = host.showRecordLinks && cardEnv?.ok ? externalHref(cardEnv.links?.recordUrl) : null;
  const path = portfolioPathOf(data);

  const page = (
    <>
      <PageHeader
        eyebrow="Application 360"
        title={
          <span className="inline-flex flex-wrap items-center gap-3">
            {app?.name ?? 'Loading'}
            {app && <StatusBadge status={app.status} />}
          </span>
        }
        meta={<DataStamp envelope={cardEnv} />}
        actions={
          <>
            <Button asChild>
              <a href={mapHref}><Workflow aria-hidden />Dependency map</a>
            </Button>
            <Button variant="outline" asChild>
              <a href={host.href({ page: 'timeline' })}><CalendarDays aria-hidden />Timeline</a>
            </Button>
            {recordUrl && (
              <Button variant="ghost" asChild>
                <a href={recordUrl} target="_blank" rel="noopener noreferrer"><ExternalLinkIcon aria-hidden />Open the form</a>
              </Button>
            )}
          </>
        }
      />

      {webApp && <ProposalNotice />}
      {webApp && <DraftBanner />}

      {app && data && (
        <section
          className="mb-4 flex flex-col gap-y-3.5 rounded-xl border bg-card px-6 py-5 shadow-xs narrow:mb-3 narrow:gap-y-3 narrow:rounded-lg narrow:px-3.5 narrow:py-4"
          aria-label="Summary"
        >
          <p className="m-0 max-w-[820px] text-base leading-6 text-navy-2 narrow:text-sm">{app.purpose}</p>
          {path.length > 0 && (
            <nav aria-label="Portfolio" className="-mt-1.5 text-xs text-navy-3">
              <span className="mr-1">Portfolio:</span>
              {path.map((p, i) => {
                const href = webApp ? recordHrefOf('portfolio', p.portfolioId) : null;
                return (
                  <span key={p.portfolioId}>
                    {i > 0 && <span aria-hidden> / </span>}
                    {href ? <TextLink href={href}>{p.name}</TextLink> : <span className="text-navy-2">{p.name}</span>}
                  </span>
                );
              })}
            </nav>
          )}
          {webApp && app.isAiCreated && <AiMark isAiCreated={app.isAiCreated} performedBy={app.performedBy} className="self-start" />}
          <AttentionStrip card={data} owners={dataOf(owners)} ownerRoles={ownerRoles} />
          {/* Six across, three below 1024 px, two below 720 px. The three-column rule is limited to "not narrow" because
              the medium variant is emitted after the narrow one and would otherwise win on a phone. */}
          <dl className="m-0 grid grid-cols-6 gap-3 border-t border-line pt-3.5 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-y-3.5">
            <Fact label="Kind" value={app.kind.label} />
            <Fact label="Tier" value={webApp ? <TextLink href={tabHref(applicationId, 'tier')}>{app.tier?.label ?? 'No tier yet'}</TextLink> : app.tier?.label ?? 'No tier yet'} />
            <Fact
              label="Stage"
              value={webApp ? <TextLink href={tabHref(applicationId, 'lifecycle')}>{app.stage?.label ?? 'Not set'}</TextLink> : app.stage?.label ?? 'Not set'}
              sub={app.stage?.since ? `since ${fmtDate(app.stage.since)}` : undefined}
            />
            <Fact label="Build path" value={app.buildPath?.label ?? 'Not set'} />
            <Fact
              label="Uses AI"
              value={<>{app.usesAi === true && <Sparkles className="size-4" aria-hidden />}{app.usesAi === null ? 'Not stated' : app.usesAi ? 'Yes' : 'No'}</>}
              sub={app.agreedAutonomyLevel ? `Autonomy: ${app.agreedAutonomyLevel.label}` : app.usesAi ? "Owner's statement" : undefined}
            />
            <Fact label="Next review" value={fmtDate(app.nextReviewOn)} />
          </dl>
          {webApp && <LifecycleStrip lifecycleHref={tabHref(applicationId, 'lifecycle')} />}
          <div className="flex items-center gap-x-1.5 text-xs text-navy-3">
            <Info className="size-4 shrink-0" aria-hidden />
            {webApp
              ? 'You act on the application here, and each change, decision, document, review and deployment opens its own page. The engine checks what you save against the rules.'
              : <>Read-only page. Changes happen {host.showRecordLinks ? 'in the form or through an AI client' : 'through an AI client'}, and the engine checks them against the rules.</>}
          </div>
        </section>
      )}

      {webApp && <App360TabBar applicationId={applicationId} tab={tab} />}

      {tab !== 'summary' ? <App360Panels tab={tab} /> : (
        <div className="grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:flex narrow:flex-col narrow:items-stretch narrow:gap-3">
          <div className={columnClass}>
            <Panel
              title="Deployments"
              subtitle="Latest deployment per target"
              state={card}
              className={order.deployments}
              notes={['deployment-record']}
              actions={webApp ? <DeploymentsPanelActions /> : undefined}
            >
              {d => <DeploymentsList card={d} stages={stages} link={link} webApp={webApp} />}
            </Panel>
            <Panel
              title="Open changes"
              state={card}
              className={order.changes}
              notes={['change-request']}
              actions={
                <>
                  {webApp && <ChangesPanelActions />}
                  <TextLink href={host.href({ page: 'timeline' })} className={panelLinkClass}>Timeline <ArrowRight className="size-4" aria-hidden /></TextLink>
                </>
              }
            >
              {d => (d.changes?.length ? (
                <RowList label="Open changes">
                  {d.changes.map(c => {
                    const stamp = changeStamp(c);
                    return (
                      <Row
                        key={c.changeRequestId}
                        primary={link('change-request', c.changeRequestId) ? <TextLink href={link('change-request', c.changeRequestId)!}>{c.title}</TextLink> : c.title}
                        secondary={fmtWindow(c.windowStart, c.windowEnd)}
                        badges={webApp && stamp.isAiCreated ? <AiMark isAiCreated={stamp.isAiCreated} performedBy={stamp.performedBy} /> : undefined}
                        wideEnd
                        end={<><ChangeTypeBadge type={c.changeType} /><StatusBadge status={c.status} /></>}
                      />
                    );
                  })}
                </RowList>
              ) : <Empty>No open changes.</Empty>)}
            </Panel>
            <Panel
              title="Dependencies"
              state={card}
              className={order.dependencies}
              notes={['dependency']}
              actions={<TextLink href={mapHref} className={panelLinkClass}>Dependency map <ArrowRight className="size-4" aria-hidden /></TextLink>}
            >
              {d => <DependencySummary card={d} mapHref={mapHref} />}
            </Panel>
            <Panel
              title="Decisions"
              subtitle="The latest five"
              state={card}
              className={order.decisions}
              notes={['decision']}
              actions={webApp ? <TextLink href={tabHref(applicationId, 'decisions')} className={panelLinkClass}>All decisions <ArrowRight className="size-4" aria-hidden /></TextLink> : undefined}
            >
              {d => (d.decisions?.length ? (
                <RowList label="Decisions">
                  {d.decisions.map(x => (
                    <Row
                      key={x.decisionId}
                      primary={link('decision', x.decisionId) ? <TextLink href={link('decision', x.decisionId)!}>{x.statement}</TextLink> : x.statement}
                      secondary={[x.decisionType.label, x.outcome?.label].filter(Boolean).join(', ')}
                      badges={webApp && x.isAiCreated ? <AiMark isAiCreated={x.isAiCreated} performedBy={x.performedBy} /> : undefined}
                      end={<StatusBadge status={x.status} />}
                      endSub={x.decidedOn ? fmtDate(x.decidedOn) : 'Waiting'}
                    />
                  ))}
                </RowList>
              ) : <Empty>No decisions yet.</Empty>)}
            </Panel>
            <Panel title="Delivery links" state={card} className={order.links} notes={['delivery-link']}>
              {d => (d.links?.length ? (
                <RowList label="Delivery links">
                  {d.links.map(l => (
                    <Row key={l.deliveryLinkId} primary={<ExternalLink href={l.url}>{l.label}</ExternalLink>} secondary={l.linkKind.label} />
                  ))}
                </RowList>
              ) : <Empty>No delivery links.</Empty>)}
            </Panel>
          </div>

          <div className={columnClass}>
            <Panel title="Owners" state={owners} className={order.owners} skeletonRows={5} actions={webApp ? <OwnersPanelActions /> : undefined}>
              {o => <OwnersList owners={o} ownerRoles={ownerRoles} applicationName={app?.name ?? null} webApp={webApp} />}
            </Panel>
            <Panel
              title="Documents"
              subtitle="Status per required document type"
              state={card}
              className={order.documents}
              notes={['document-link']}
            >
              {d => (d.documents?.length ? (
                <RowList label="Documents">
                  {d.documents.map(doc => (
                    <Row
                      key={doc.documentType.key}
                      primary={link('document-link', doc.documentLinkId)
                        ? <TextLink href={link('document-link', doc.documentLinkId)!}>{doc.documentType.label}</TextLink>
                        : doc.url ? <ExternalLink href={doc.url}>{doc.documentType.label}</ExternalLink> : doc.documentType.label}
                      secondary={
                        doc.status.meaning === 'missing' ? 'No link recorded yet'
                          : doc.status.meaning === 'stale' ? `Stale since ${fmtDate(doc.staleOn)}. Last checked ${fmtDate(doc.lastCheckedOn)}`
                            : `Checked ${fmtDate(doc.lastCheckedOn)}. Stale on ${fmtDate(doc.staleOn)}`
                      }
                      tertiary={webApp ? <DocumentRowActions documentTypeKey={doc.documentType.key} typeLabel={doc.documentType.label} statusMeaning={doc.status.meaning} /> : undefined}
                      end={<StatusBadge status={doc.status} />}
                    />
                  ))}
                </RowList>
              ) : <Empty>No documents are required at this tier.</Empty>)}
            </Panel>
            <Panel
              title="Reviews"
              subtitle="Due, and the last one done per type"
              state={card}
              className={order.reviews}
              notes={['review']}
              actions={webApp ? <ReviewsPanelActions /> : undefined}
            >
              {d => (d.reviews?.length ? (
                <RowList label="Reviews">
                  {d.reviews.map(r => (
                    <Row
                      key={r.reviewId}
                      primary={link('review', r.reviewId) ? <TextLink href={link('review', r.reviewId)!}>{r.reviewType.label}</TextLink> : r.reviewType.label}
                      secondary={r.status.meaning === 'done' ? `Done ${fmtDate(r.doneOn)}` : `Due ${fmtDate(r.dueOn)}`}
                      tertiary={webApp ? <ReviewRowActions reviewId={r.reviewId} subject={r.reviewType.label} done={r.status.meaning === 'done'} /> : undefined}
                      end={<StatusBadge status={r.status} />}
                    />
                  ))}
                </RowList>
              ) : <Empty>No reviews due.</Empty>)}
            </Panel>
            <Panel title="Components" subtitle="Technical parts, linked where they live" state={card} className={order.components} notes={['technical-component']}>
              {d => (d.components?.length ? (
                <RowList label="Components">
                  {d.components.map(c => (
                    <Row
                      key={c.technicalComponentId}
                      primary={c.url ? <ExternalLink href={c.url}>{c.labelAsSeen ?? c.externalId}</ExternalLink> : (c.labelAsSeen ?? c.externalId)}
                      secondary={`${c.kind.label}. Seen ${fmtDate(c.lastSeenOn, 'never')}`}
                      end={webApp ? <WhatDependsOnIt start={{ technicalComponentId: c.technicalComponentId }} name={c.labelAsSeen ?? c.externalId} /> : undefined}
                    />
                  ))}
                </RowList>
              ) : <Empty>No components linked.</Empty>)}
            </Panel>
          </div>
        </div>
      )}
    </>
  );

  return webApp ? <App360Scope applicationId={applicationId} card={data} owners={dataOf(owners)}>{page}</App360Scope> : page;
}

function Fact({ label, value, sub }: { label: string; value: ReactNode; sub?: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-y-0.5">
      <dt className={factLabelClass}>{label}</dt>
      <dd className="m-0 flex items-center gap-x-1 text-sm font-semibold text-navy">{value}</dd>
      {sub && <dd className={factLabelClass}>{sub}</dd>}
    </div>
  );
}

// The few things on this card that need someone. Each chip repeats a fact shown in its panel; nothing is counted here.
function AttentionStrip({ card, owners, ownerRoles }: { card: ApplicationCard; owners: ApplicationOwners | null; ownerRoles: Labels }) {
  const chips: { key: string; tone: Tone; text: string }[] = [];
  for (const g of owners?.gaps ?? []) {
    chips.push({ key: `gap-${g.ownerRoleKey}-${g.missing}`, tone: 'warning', text: `${ownerRoles.label(g.ownerRoleKey)}: no ${g.missing}` });
  }
  for (const d of card.documents ?? []) {
    if (d.status.meaning === 'missing' || d.status.meaning === 'stale') {
      chips.push({ key: `doc-${d.documentType.key}`, tone: d.status.meaning === 'missing' ? 'danger' : 'warning', text: `${d.documentType.label}: ${d.status.label.toLowerCase()}` });
    }
  }
  for (const d of card.deployments ?? []) {
    if (d.result.meaning === 'failed') chips.push({ key: `dep-${d.deploymentRecordId}`, tone: 'danger', text: `Last deployment to ${d.targetLabel} failed` });
  }
  if (owners?.microsoftOwnerDiffers) chips.push({ key: 'ms-owner', tone: 'informative', text: "Microsoft's listed owner differs" });
  if (!chips.length) return null;
  return (
    <div
      className="flex flex-wrap items-center gap-x-2 gap-y-1.5 rounded-lg border border-warning-line bg-gold-soft px-3 py-2.5"
      role="group"
      aria-label="Needs attention"
    >
      <span className="mr-1 text-xs font-semibold text-navy">Needs attention</span>
      {chips.map(c => <ToneBadge key={c.key} tone={c.tone}>{c.text}</ToneBadge>)}
    </div>
  );
}

function DeploymentsList({ card, stages, link, webApp }: { card: ApplicationCard; stages: Labels; link: (entity: string, id: string | null | undefined) => string | null; webApp: boolean }) {
  const list = [...(card.deployments ?? [])].sort((a, b) => stages.rank(b.targetStageKey) - stages.rank(a.targetStageKey));
  if (!list.length) return <Empty>Nothing deployed yet.</Empty>;
  return (
    <RowList label="Latest deployment per target">
      {list.map(d => {
        // The route, flags and stamp the card carries; the web app shows them (the harness fixtures may hold none).
        const extras = webApp ? deploymentExtras(d) : null;
        return (
          <Row
            key={d.deploymentRecordId}
            primary={link('deployment-record', d.deploymentRecordId) ? <TextLink href={link('deployment-record', d.deploymentRecordId)!}>{d.targetLabel}</TextLink> : d.targetLabel}
            secondary={`${stages.label(d.targetStageKey)}. ${d.artifactName} ${d.artifactVersion}`}
            badges={extras && (extras.flags || extras.isAiCreated) ? (
              <>
                {extras.flags && <FlagBadges flags={extras.flags} />}
                <AiMark isAiCreated={extras.isAiCreated} performedBy={extras.performedBy} />
              </>
            ) : undefined}
            end={<StatusBadge status={d.result} />}
            endSub={d.finishedOn ? fmtDate(d.finishedOn) : 'Not finished'}
          />
        );
      })}
    </RowList>
  );
}

function DependencySummary({ card, mapHref }: { card: ApplicationCard; mapHref: string }) {
  const host = useHost();
  const deps = card.dependencies;
  if (!deps) return <Empty>No dependencies recorded.</Empty>;
  const needs = deps.top.filter(e => e.direction === 'depends-on');
  const usedBy = deps.top.filter(e => e.direction === 'used-by');
  const row = (e: (typeof deps.top)[number]) => (
    <Row
      key={`${e.direction}-${e.businessApplicationId ?? e.technicalComponentId}`}
      primary={e.businessApplicationId ? <TextLink href={host.href({ page: 'app', id: e.businessApplicationId })}>{e.name}</TextLink> : e.name}
      secondary={e.dependencyType.label}
      end={<CriticalityBadge criticality={e.criticality} />}
    />
  );
  const statClass =
    'flex cursor-pointer flex-col rounded-lg bg-secondary px-3 py-2.5 text-left text-inherit no-underline transition-colors hover:bg-[#cfe4fa] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring';
  return (
    <>
      <div className="grid grid-cols-2 gap-2.5">
        <a className={statClass} href={mapHref}>
          <span className="text-2xl leading-[30px] font-semibold text-navy tabular-nums">{deps.dependsOnCount}</span>
          <span className="text-xs text-navy-2">It depends on</span>
        </a>
        <a className={statClass} href={mapHref}>
          <span className="text-2xl leading-[30px] font-semibold text-navy tabular-nums">{deps.usedByCount}</span>
          <span className="text-xs text-navy-2">Depend on it</span>
        </a>
      </div>
      {usedBy.length > 0 && (
        <div>
          <h3 className={subheadClass}>Most important dependants</h3>
          <RowList label="Most important dependants">{usedBy.map(row)}</RowList>
        </div>
      )}
      {needs.length > 0 && (
        <div>
          <h3 className={subheadClass}>Most important dependencies</h3>
          <RowList label="Most important dependencies">{needs.map(row)}</RowList>
        </div>
      )}
    </>
  );
}

// Initials for a person's avatar: the first letters of the first and the last word of the name.
function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (!words.length) return '';
  const first = Array.from(words[0])[0] ?? '';
  const last = words.length > 1 ? Array.from(words[words.length - 1])[0] ?? '' : '';
  return (first + last).toUpperCase();
}

// A stable tint per name, so the same person keeps the same color on every render. Theme tints only; the color
// carries no meaning.
const avatarTints = [
  'bg-info-soft text-info',
  'bg-gold-soft text-gold-strong',
  'bg-success-soft text-success',
  'bg-subtle-soft text-navy-2',
];

function avatarTint(name: string): string {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0;
  return avatarTints[Math.abs(h) % avatarTints.length];
}

// In the web app a person proposes the owner change here, on the sheet the Owners page uses; the harness hands it to an
// AI client instead. Shown when the person may propose an owner change on this application.
function GapProposeSheet({ ownerRoleKey, missing, label }: { ownerRoleKey: string; missing: string; label: string }) {
  const ctx = useApp360();
  if (!ctx || !ctx.can('propose_owner_change') || !(ctx.ownerRoles.length > 0 || ctx.session.hasRole('platform-admin', 'ai-lead'))) return null;
  return (
    <ProposeOwnerChangeSheet
      application={{ id: ctx.applicationId, name: ctx.applicationName }}
      mode="add"
      ownerRoleKey={ownerRoleKey}
      isDeputy={missing === 'deputy'}
      me={ctx.session.who}
      onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
      trigger={<Button size="sm" variant="outline" aria-label={`Propose a ${missing} for ${label}`}>Propose a {missing}</Button>}
    />
  );
}

function OwnersList({ owners, ownerRoles, applicationName, webApp }: { owners: ApplicationOwners; ownerRoles: Labels; applicationName: string | null; webApp: boolean }) {
  const host = useHost();
  return (
    <>
      <RowList label="Owner assignments">
        {owners.owners.map(o => (
          <Row
            key={o.ownerAssignmentId}
            lead={
              o.principal.principalKind === 'group'
                ? (
                  <Avatar aria-hidden>
                    <AvatarFallback className="bg-primary text-primary-foreground"><UsersRound className="size-4" aria-hidden /></AvatarFallback>
                  </Avatar>
                )
                : (
                  <Avatar aria-hidden>
                    <AvatarFallback className={cn('text-xs font-semibold', avatarTint(o.principal.displayLabel))}>{initials(o.principal.displayLabel)}</AvatarFallback>
                  </Avatar>
                )
            }
            primary={webApp ? <OwnerHolderName principal={o.principal} /> : o.principal.displayLabel}
            secondary={o.isDeputy ? `${o.label}, deputy` : o.label}
            tertiary={webApp ? <OwnerRowActions assignment={o} /> : undefined}
            endSub={o.startOn ? `since ${fmtDate(o.startOn)}` : 'Waiting for its decision'}
          />
        ))}
        {owners.gaps.map(g => (
          <Row
            key={`gap-${g.ownerRoleKey}-${g.missing}`}
            lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" aria-hidden /></span>}
            primary={`${ownerRoles.label(g.ownerRoleKey)}: no ${g.missing}`}
            secondary="An owner change needs a decision by a person."
            end={webApp ? (
              <GapProposeSheet ownerRoleKey={g.ownerRoleKey} missing={g.missing} label={ownerRoles.label(g.ownerRoleKey)} />
            ) : (
              <LinkButton
                aria-label={`Propose an owner change for ${ownerRoles.label(g.ownerRoleKey)}`}
                onClick={() => host.handOff({
                  title: 'Propose an owner change',
                  tool: 'propose_owner_change',
                  prompt: `Propose a ${g.missing === 'holder' ? 'holder' : 'deputy'} for the ${ownerRoles.label(g.ownerRoleKey).toLowerCase()} role of ${applicationName ?? 'this application'}.`,
                })}
              >
                Propose
              </LinkButton>
            )}
          />
        ))}
      </RowList>
      {owners.microsoftOwnerDiffers && (
        <div className="flex items-start gap-x-2 rounded-lg bg-secondary px-3 py-2.5 text-xs leading-5 text-navy-2">
          <Info className="mt-px size-4 shrink-0 text-brand-dark" aria-hidden />
          <span>The owner Microsoft lists for a component differs from this register. The product never copies that owner; check it at the next owner check.</span>
        </div>
      )}
      <Muted>Owner check last ran {fmtDate(owners.ownerCheckedOn, 'never')}.</Muted>
    </>
  );
}
