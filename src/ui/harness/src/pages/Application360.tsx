// Page 2: Application 360. On binding A it sits in a tab of the Application main form and receives the record ID.
// Reads (document 24 names): get_application_card (every section except owners), get_application_owners,
// get_configuration (owner-role and target-stage labels). Two custom API calls, so the six-table limit of
// generative pages does not bind this page (the card reads its tables on the server).
// Read-only: edits happen in the form, or through an AI client, and every write goes through the engine.
// In the web app the route is /app/records/business-application/<id>, the record link the tools return.
import { Avatar, Badge, Button, Link, makeStyles, tokens } from '@fluentui/react-components';
import {
  ArrowRight16Regular,
  CalendarLtr20Regular,
  Flowchart20Regular,
  Info16Regular,
  Open16Regular,
  PeopleTeam16Regular,
  PersonWarning20Regular,
  Sparkle16Regular,
} from '@fluentui/react-icons';
import type { ReactNode } from 'react';
import type { ApplicationCard, ApplicationOwners, LabelledKey } from '../data/contracts';
import { externalHref, fmtDate, fmtWindow } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { dataOf, useTool } from '../data/toolClient';
import { useHost } from '../harness';
import { palette } from '../theme';
import { ExternalLink, Row, RowList } from '../components/rows';
import { DataStamp, Empty, Muted, PageHeader, Panel, PanelFailure, StatusBadge, ToneBadge, media } from '../components/ui';
import type { Tone } from '../data/meanings';

const CARD_SECTIONS = ['components', 'dependencies', 'decisions', 'changes', 'deployments', 'documents', 'reviews', 'links'];

const useStyles = makeStyles({
  head: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${palette.line}`,
    borderRadius: '10px',
    padding: '20px 24px',
    marginBottom: '16px',
    display: 'flex',
    flexDirection: 'column',
    rowGap: '14px',
    [media.narrow]: { padding: '16px 14px', marginBottom: '12px', rowGap: '12px' },
  },
  purpose: { margin: 0, fontSize: tokens.fontSizeBase400, lineHeight: tokens.lineHeightBase400, color: palette.navy2, maxWidth: '820px', [media.narrow]: { fontSize: tokens.fontSizeBase300 } },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'repeat(6, minmax(0, 1fr))',
    gap: '12px',
    margin: 0,
    padding: '14px 0 0',
    borderTop: `1px solid ${palette.line}`,
    [media.medium]: { gridTemplateColumns: 'repeat(3, minmax(0, 1fr))' },
    [media.narrow]: { gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', rowGap: '14px' },
  },
  fact: { display: 'flex', flexDirection: 'column', rowGap: '2px', minWidth: 0 },
  factLabel: { fontSize: tokens.fontSizeBase200, color: palette.navy3, margin: 0 },
  factValue: { fontSize: tokens.fontSizeBase300, fontWeight: tokens.fontWeightSemibold, color: palette.navy, margin: 0, display: 'flex', alignItems: 'center', columnGap: '4px' },
  attention: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: '6px 8px',
    padding: '10px 12px',
    borderRadius: '8px',
    backgroundColor: palette.lightGold,
    border: '1px solid #f3dfa6',
  },
  attentionLabel: { fontSize: tokens.fontSizeBase200, fontWeight: tokens.fontWeightSemibold, color: palette.navy, marginRight: '4px' },
  readOnly: { display: 'flex', alignItems: 'center', columnGap: '6px', fontSize: tokens.fontSizeBase200, color: palette.navy3 },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 7fr) minmax(0, 5fr)',
    gap: '16px',
    alignItems: 'start',
    [media.medium]: { gridTemplateColumns: 'minmax(0, 1fr)' },
    [media.narrow]: { display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'stretch' },
  },
  column: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: '16px',
    minWidth: 0,
    [media.narrow]: { display: 'contents' },
  },
  o1: { [media.narrow]: { order: 1 } },
  o2: { [media.narrow]: { order: 2 } },
  o3: { [media.narrow]: { order: 3 } },
  o4: { [media.narrow]: { order: 4 } },
  o5: { [media.narrow]: { order: 5 } },
  o6: { [media.narrow]: { order: 6 } },
  o7: { [media.narrow]: { order: 7 } },
  o8: { [media.narrow]: { order: 8 } },
  o9: { [media.narrow]: { order: 9 } },
  stats: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' },
  stat: {
    appearance: 'none',
    margin: 0,
    fontFamily: 'inherit',
    fontSize: 'inherit',
    lineHeight: 'inherit',
    color: 'inherit',
    textAlign: 'left',
    textDecoration: 'none',
    cursor: 'pointer',
    boxSizing: 'border-box',
    padding: '10px 12px',
    borderRadius: '8px',
    backgroundColor: palette.lightBlue,
    display: 'flex',
    flexDirection: 'column',
    ':hover': { backgroundColor: '#dcebfa' },
    ':focus-visible': { outline: `2px solid ${palette.blue}`, outlineOffset: '2px' },
  },
  statValue: { fontSize: '24px', lineHeight: '30px', fontWeight: tokens.fontWeightSemibold, color: palette.navy },
  statLabel: { fontSize: tokens.fontSizeBase200, color: palette.navy2 },
  subhead: {
    margin: '6px 0 2px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  },
  gapIcon: {
    width: '32px',
    height: '32px',
    borderRadius: '50%',
    display: 'grid',
    placeItems: 'center',
    backgroundColor: palette.lightGold,
    color: palette.goldDark,
  },
  info: {
    display: 'flex',
    columnGap: '8px',
    alignItems: 'flex-start',
    padding: '10px 12px',
    borderRadius: '8px',
    backgroundColor: palette.lightBlue,
    fontSize: tokens.fontSizeBase200,
    color: palette.navy2,
  },
  infoIcon: { color: palette.blueDark, flexShrink: 0, marginTop: '1px' },
  panelLink: { display: 'inline-flex', alignItems: 'center', columnGap: '4px', fontWeight: tokens.fontWeightSemibold },
});

export function Application360({ applicationId }: { applicationId: string }) {
  const s = useStyles();
  const host = useHost();
  const card = useTool('get_application_card', { businessApplicationId: applicationId, sections: CARD_SECTIONS });
  const owners = useTool('get_application_owners', { businessApplicationId: applicationId });
  const ownerRoles = useLabels('owner-role');
  const stages = useLabels('target-stage');

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

  return (
    <>
      <PageHeader
        eyebrow="Application 360"
        title={
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            {app?.name ?? 'Loading'}
            {app && <StatusBadge status={app.status} />}
          </span>
        }
        meta={<DataStamp envelope={cardEnv} />}
        actions={
          <>
            <Button appearance="primary" icon={<Flowchart20Regular />} as="a" href={mapHref}>Dependency map</Button>
            <Button appearance="secondary" icon={<CalendarLtr20Regular />} as="a" href={host.href({ page: 'timeline' })}>Timeline</Button>
            {recordUrl && (
              <Button appearance="subtle" icon={<Open16Regular />} as="a" href={recordUrl} target="_blank" rel="noopener noreferrer">
                Open the form
              </Button>
            )}
          </>
        }
      />

      {app && data && (
        <section className={s.head} aria-label="Summary">
          <p className={s.purpose}>{app.purpose}</p>
          <AttentionStrip card={data} owners={dataOf(owners)} ownerRoles={ownerRoles} />
          <dl className={s.facts}>
            <Fact label="Kind" value={app.kind.label} />
            <Fact label="Tier" value={app.tier?.label ?? 'No tier yet'} />
            <Fact label="Stage" value={app.stage?.label ?? 'Not set'} sub={app.stage?.since ? `since ${fmtDate(app.stage.since)}` : undefined} />
            <Fact label="Build path" value={app.buildPath?.label ?? 'Not set'} />
            <Fact
              label="Uses AI"
              value={<>{app.usesAi === true && <Sparkle16Regular aria-hidden />}{app.usesAi === null ? 'Not stated' : app.usesAi ? 'Yes' : 'No'}</>}
              sub={app.agreedAutonomyLevel ? `Autonomy: ${app.agreedAutonomyLevel.label}` : app.usesAi ? "Owner's statement" : undefined}
            />
            <Fact label="Next review" value={fmtDate(app.nextReviewOn)} />
          </dl>
          <div className={s.readOnly}>
            <Info16Regular aria-hidden />
            Read-only page. Changes happen {host.showRecordLinks ? 'in the form or through an AI client' : 'through an AI client'}, and the engine checks every write.
          </div>
        </section>
      )}

      <div className={s.grid}>
        <div className={s.column}>
          <Panel title="Deployments" subtitle="Latest deployment per target" state={card} className={s.o2} notes={['deployment-record']}>
            {d => <DeploymentsList card={d} stages={stages} />}
          </Panel>
          <Panel
            title="Open changes"
            state={card}
            className={s.o3}
            notes={['change-request']}
            actions={<Link href={host.href({ page: 'timeline' })} className={s.panelLink}>Timeline <ArrowRight16Regular /></Link>}
          >
            {d => (d.changes?.length ? (
              <RowList label="Open changes">
                {d.changes.map(c => (
                  <Row
                    key={c.changeRequestId}
                    primary={c.title}
                    secondary={fmtWindow(c.windowStart, c.windowEnd)}
                    wideEnd
                    end={<><ChangeTypeBadge type={c.changeType} /><StatusBadge status={c.status} /></>}
                  />
                ))}
              </RowList>
            ) : <Empty>No open changes.</Empty>)}
          </Panel>
          <Panel
            title="Dependencies"
            state={card}
            className={s.o6}
            notes={['dependency']}
            actions={<Link href={mapHref} className={s.panelLink}>Dependency map <ArrowRight16Regular /></Link>}
          >
            {d => <DependencySummary card={d} mapHref={mapHref} />}
          </Panel>
          <Panel title="Decisions" subtitle="The latest five" state={card} className={s.o7} notes={['decision']}>
            {d => (d.decisions?.length ? (
              <RowList label="Decisions">
                {d.decisions.map(x => (
                  <Row
                    key={x.decisionId}
                    primary={x.statement}
                    secondary={[x.decisionType.label, x.outcome?.label].filter(Boolean).join(', ')}
                    end={<StatusBadge status={x.status} />}
                    endSub={x.decidedOn ? fmtDate(x.decidedOn) : 'Waiting'}
                  />
                ))}
              </RowList>
            ) : <Empty>No decisions yet.</Empty>)}
          </Panel>
          <Panel title="Delivery links" state={card} className={s.o9} notes={['delivery-link']}>
            {d => (d.links?.length ? (
              <RowList label="Delivery links">
                {d.links.map(l => (
                  <Row key={l.deliveryLinkId} primary={<ExternalLink href={l.url}>{l.label}</ExternalLink>} secondary={l.linkKind.label} />
                ))}
              </RowList>
            ) : <Empty>No delivery links.</Empty>)}
          </Panel>
        </div>

        <div className={s.column}>
          <Panel title="Owners" state={owners} className={s.o1} skeletonRows={5}>
            {o => <OwnersList owners={o} ownerRoles={ownerRoles} applicationName={app?.name ?? null} />}
          </Panel>
          <Panel title="Documents" subtitle="Status per required document type" state={card} className={s.o4} notes={['document-link']}>
            {d => (d.documents?.length ? (
              <RowList label="Documents">
                {d.documents.map(doc => (
                  <Row
                    key={doc.documentType.key}
                    primary={doc.url ? <ExternalLink href={doc.url}>{doc.documentType.label}</ExternalLink> : doc.documentType.label}
                    secondary={
                      doc.status.meaning === 'missing' ? 'No link recorded yet'
                        : doc.status.meaning === 'stale' ? `Stale since ${fmtDate(doc.staleOn)}. Last checked ${fmtDate(doc.lastCheckedOn)}`
                          : `Checked ${fmtDate(doc.lastCheckedOn)}. Stale on ${fmtDate(doc.staleOn)}`
                    }
                    end={<StatusBadge status={doc.status} />}
                  />
                ))}
              </RowList>
            ) : <Empty>No documents are required at this tier.</Empty>)}
          </Panel>
          <Panel title="Reviews" subtitle="Due, and the last one done per type" state={card} className={s.o5} notes={['review']}>
            {d => (d.reviews?.length ? (
              <RowList label="Reviews">
                {d.reviews.map(r => (
                  <Row
                    key={r.reviewId}
                    primary={r.reviewType.label}
                    secondary={r.status.meaning === 'done' ? `Done ${fmtDate(r.doneOn)}` : `Due ${fmtDate(r.dueOn)}`}
                    end={<StatusBadge status={r.status} />}
                  />
                ))}
              </RowList>
            ) : <Empty>No reviews due.</Empty>)}
          </Panel>
          <Panel title="Components" subtitle="Technical parts, linked where they live" state={card} className={s.o8} notes={['technical-component']}>
            {d => (d.components?.length ? (
              <RowList label="Components">
                {d.components.map(c => (
                  <Row
                    key={c.technicalComponentId}
                    primary={<ExternalLink href={c.url}>{c.labelAsSeen ?? c.externalId}</ExternalLink>}
                    secondary={`${c.kind.label}. Seen ${fmtDate(c.lastSeenOn, 'never')}`}
                  />
                ))}
              </RowList>
            ) : <Empty>No components linked.</Empty>)}
          </Panel>
        </div>
      </div>
    </>
  );
}

function Fact({ label, value, sub }: { label: string; value: ReactNode; sub?: string }) {
  const s = useStyles();
  return (
    <div className={s.fact}>
      <dt className={s.factLabel}>{label}</dt>
      <dd className={s.factValue}>{value}</dd>
      {sub && <dd className={s.factLabel}>{sub}</dd>}
    </div>
  );
}

// The few things on this card that need someone. Each chip repeats a fact shown in its panel; nothing is counted here.
function AttentionStrip({ card, owners, ownerRoles }: { card: ApplicationCard; owners: ApplicationOwners | null; ownerRoles: Labels }) {
  const s = useStyles();
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
    <div className={s.attention} role="group" aria-label="Needs attention">
      <span className={s.attentionLabel}>Needs attention</span>
      {chips.map(c => <ToneBadge key={c.key} tone={c.tone}>{c.text}</ToneBadge>)}
    </div>
  );
}

function DeploymentsList({ card, stages }: { card: ApplicationCard; stages: Labels }) {
  const list = [...(card.deployments ?? [])].sort((a, b) => stages.rank(b.targetStageKey) - stages.rank(a.targetStageKey));
  if (!list.length) return <Empty>Nothing deployed yet.</Empty>;
  return (
    <RowList label="Latest deployment per target">
      {list.map(d => (
        <Row
          key={d.deploymentRecordId}
          primary={d.targetLabel}
          secondary={`${stages.label(d.targetStageKey)}. ${d.artifactName} ${d.artifactVersion}`}
          end={<StatusBadge status={d.result} />}
          endSub={d.finishedOn ? fmtDate(d.finishedOn) : 'Not finished'}
        />
      ))}
    </RowList>
  );
}

export function criticalityTone(key: string | null | undefined): Tone {
  return key === 'high' ? 'danger' : key === 'medium' ? 'warning' : key === 'low' ? 'informative' : 'subtle';
}

export function CriticalityBadge({ criticality }: { criticality: LabelledKey | null }) {
  return (
    <ToneBadge tone={criticalityTone(criticality?.key)} icon={null}>
      {criticality ? `${criticality.label} criticality` : 'Criticality not set'}
    </ToneBadge>
  );
}

export function ChangeTypeBadge({ type }: { type: LabelledKey }) {
  return (
    <Badge appearance="outline" shape="rounded" color={type.key === 'emergency' ? 'danger' : 'informative'}>
      {type.label}
    </Badge>
  );
}

function DependencySummary({ card, mapHref }: { card: ApplicationCard; mapHref: string }) {
  const s = useStyles();
  const host = useHost();
  const deps = card.dependencies;
  if (!deps) return <Empty>No dependencies recorded.</Empty>;
  const needs = deps.top.filter(e => e.direction === 'depends-on');
  const usedBy = deps.top.filter(e => e.direction === 'used-by');
  const row = (e: (typeof deps.top)[number]) => (
    <Row
      key={`${e.direction}-${e.businessApplicationId ?? e.technicalComponentId}`}
      primary={e.businessApplicationId ? <Link href={host.href({ page: 'app', id: e.businessApplicationId })}>{e.name}</Link> : e.name}
      secondary={e.dependencyType.label}
      end={<CriticalityBadge criticality={e.criticality} />}
    />
  );
  return (
    <>
      <div className={s.stats}>
        <a className={s.stat} href={mapHref}>
          <span className={s.statValue}>{deps.dependsOnCount}</span>
          <span className={s.statLabel}>It depends on</span>
        </a>
        <a className={s.stat} href={mapHref}>
          <span className={s.statValue}>{deps.usedByCount}</span>
          <span className={s.statLabel}>Depend on it</span>
        </a>
      </div>
      {usedBy.length > 0 && (
        <div>
          <h3 className={s.subhead}>Most important dependants</h3>
          <RowList label="Most important dependants">{usedBy.map(row)}</RowList>
        </div>
      )}
      {needs.length > 0 && (
        <div>
          <h3 className={s.subhead}>Most important dependencies</h3>
          <RowList label="Most important dependencies">{needs.map(row)}</RowList>
        </div>
      )}
    </>
  );
}

function OwnersList({ owners, ownerRoles, applicationName }: { owners: ApplicationOwners; ownerRoles: Labels; applicationName: string | null }) {
  const s = useStyles();
  const host = useHost();
  return (
    <>
      <RowList label="Owner assignments">
        {owners.owners.map(o => (
          <Row
            key={o.ownerAssignmentId}
            lead={
              o.principal.principalKind === 'group'
                ? <Avatar size={32} icon={<PeopleTeam16Regular />} color="brand" aria-hidden />
                : <Avatar size={32} name={o.principal.displayLabel} color="colorful" aria-hidden />
            }
            primary={o.principal.displayLabel}
            secondary={o.isDeputy ? `${o.label}, deputy` : o.label}
            endSub={o.startOn ? `since ${fmtDate(o.startOn)}` : 'Waiting for its decision'}
          />
        ))}
        {owners.gaps.map(g => (
          <Row
            key={`gap-${g.ownerRoleKey}-${g.missing}`}
            lead={<span className={s.gapIcon} aria-hidden><PersonWarning20Regular /></span>}
            primary={`${ownerRoles.label(g.ownerRoleKey)}: no ${g.missing}`}
            secondary="An owner change needs a decision by a person."
            end={
              <Link
                as="button"
                aria-label={`Propose an owner change for ${ownerRoles.label(g.ownerRoleKey)}`}
                onClick={() => host.handOff({
                  title: 'Propose an owner change',
                  tool: 'propose_owner_change',
                  prompt: `Propose a ${g.missing === 'holder' ? 'holder' : 'deputy'} for the ${ownerRoles.label(g.ownerRoleKey).toLowerCase()} role of ${applicationName ?? 'this application'}.`,
                })}
              >
                Propose
              </Link>
            }
          />
        ))}
      </RowList>
      {owners.microsoftOwnerDiffers && (
        <div className={s.info}>
          <Info16Regular className={s.infoIcon} aria-hidden />
          <span>The owner Microsoft lists for a component differs from this register. The product never copies that owner; check it at the next owner check.</span>
        </div>
      )}
      <Muted>Owner check last ran {fmtDate(owners.ownerCheckedOn, 'never')}.</Muted>
    </>
  );
}
