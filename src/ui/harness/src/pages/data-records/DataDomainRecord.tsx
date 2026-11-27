// The data domain record (/app/records/data-domain/<key>): one business data domain (C49) in the tree drawn for this
// install. Shows the path, the owners chain with every level up and the level that answers marked, the rules with where
// each is set and the configuration version, where its data sits (mappings by location), the applications using it
// with their state, the open decisions, the Fabric and Purview matches with the day each was checked, the findings, the
// domains below it, and the configuration versions that changed it.
// Reads: get_data_domain (every section), get_data_domain owners of each level above (the chain), and
// list_configuration_changes (the versions). Writes, each in the shared side sheet: propose an owner, a deputy or a
// steward (ProposeDataOwnerSheet, propose_owner_change with dataDomainKey), add a mapping (DataMappingSheet), move its
// rows to another domain (MoveDataReferencesSheet, platform admin and data governance lead). The tree itself changes
// only through a configuration change (the Configuration page).
import { ArrowRight, ChevronRight, CircleCheck, MapPin, MoveRight, UserRoundX } from 'lucide-react';
import { Fragment, type ReactNode } from 'react';
import { hrefTo, recordHrefOf } from '../../app/links';
import { DataStamp, Empty, Muted, PageHeader, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ClassificationBadge, DataOriginBadge, DataRuleChip, DataUseStateBadge } from '../../components/dataChips';
import { ExternalLink, Row, RowList } from '../../components/rows';
import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import type { DataDomainDetail, DataDomainHolder, Success } from '../../data/contracts';
import { DATA_RULE_NAME, ruleSourceText } from '../../data/dataTaxonomy';
import { fmtDate, fmtDateTime } from '../../data/format';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { DataMappingSheet, MoveDataReferencesSheet, ProposeDataOwnerSheet } from '../data/sheets';
import { answeringLevel, mappingsByLocation, openFirst, ownHolder, sameId, versionLineText, versionsTouching } from './logic';
import { FindingRow, RecordFrame, subhead, UseRow, useCanWrite } from './parts';
import { Sticky } from '../work-approvals/parts';
import { rowHref } from './reads';

const grid = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

type Owners = NonNullable<DataDomainDetail['owners']>;

export function DataDomainRecord({ domainKey }: { domainKey: string }) {
  const state = useRead('get_data_domain', { domainKey });
  const back = { label: 'All data domains', href: hrefTo({ page: 'data' }) };
  if (state.status === 'loading') return <RecordFrame eyebrow="Data domain" title="Data domain" state="loading" back={back} />;
  if (!state.envelope.ok) return <RecordFrame eyebrow="Data domain" title="Data domain" state={state.envelope} back={back} />;
  return <DomainBody envelope={state.envelope} />;
}

function DomainBody({ envelope }: { envelope: Success<DataDomainDetail> }) {
  const canWrite = useCanWrite();
  const session = useSession();
  const detail = envelope.data;
  const d = detail.domain;
  const uses = detail.uses ?? [];
  const mappings = detail.mappings ?? [];
  const gaps = detail.gaps ?? [];
  const decisions = openFirst(detail.decisions ?? []);
  const subject = { kind: 'data-domain' as const, domainKey: d.key, label: d.path };
  const canOwner = canWrite('propose_owner_change') && d.isActive;
  const canMap = canWrite('save_data_mapping') && d.isActive;
  const canMove = canWrite('move_data_references') && (mappings.length + uses.length > 0);
  const versions = useRead('list_configuration_changes', session.can('list_configuration_changes') ? { view: 'versions', top: 100 } : null);
  const touched = versionsTouching(dataOf(versions)?.versions ?? [], d.key);
  const ancestors = d.pathKeys.slice(0, -1);

  return (
    <>
      <PageHeader
        eyebrow={d.level ? `Data domain, ${d.level.label.toLowerCase()}` : 'Data domain'}
        title={d.label}
        status={(
          <span className="flex flex-wrap items-center gap-1.5">
            <ClassificationBadge classification={d.classification} domainKey={d.key} />
            {!d.isActive && <ToneBadge tone="danger">Retired</ToneBadge>}
            {d.isActive && !d.declarable && <ToneBadge tone="subtle" icon={null}>Not for declaring uses</ToneBadge>}
          </span>
        )}
        subtitle={(
          <nav aria-label="Where it sits in the tree" className="flex flex-wrap items-center gap-1">
            {d.pathKeys.map((p, i) => (
              <Fragment key={p.key}>
                {i > 0 && <ChevronRight className="size-3.5 text-navy-3" aria-hidden />}
                {i < d.pathKeys.length - 1
                  ? <TextLink href={hrefTo({ page: 'data-domain', key: p.key })}>{p.label}</TextLink>
                  : <span aria-current="page" className="font-semibold text-navy">{p.label}</span>}
              </Fragment>
            ))}
          </nav>
        )}
        meta={(
          <span className="flex flex-col gap-0.5">
            {d.description && <span className="text-sm text-navy-2">{d.description}</span>}
            <span><DataStamp envelope={envelope} /></span>
          </span>
        )}
        actions={(
          <>
            {canOwner && <ProposeDataOwnerSheet subject={subject} trigger={<Button size="sm">Propose an owner</Button>} />}
            {canMap && <DataMappingSheet mode="add" domainKey={d.key} trigger={<Button size="sm" variant="outline"><MapPin aria-hidden />Add a mapping</Button>} />}
            {/* Sticky (QA, fix round 1): a move that empties the domain hides the action, and the open sheet stays to show the answer. */}
            <Sticky show={canMove}><MoveDataReferencesSheet fromDomainKey={d.key} trigger={<Button size="sm" variant="outline"><MoveRight aria-hidden />Move its rows</Button>} /></Sticky>
          </>
        )}
      />

      {!d.isActive && (
        <p className="mt-0 mb-4 text-sm text-navy-2">
          This domain is retired. Rows that still point at it are a finding (rows on a retired domain); move them to another domain.
        </p>
      )}

      <div className={grid}>
        <div className={stack}>
          <OwnersPanel detail={detail} ancestors={ancestors} canOwner={canOwner} subject={subject} />

          <Panel title="Rules" subtitle={`The rules in force under configuration version ${envelope.configurationSetVersion}. A rule set here passes down to the domains below unless one sets its own.`}>
            <dl className="m-0 flex flex-col">
              <RuleFact label="Classification">
                <ClassificationBadge classification={d.classification} domainKey={d.key} />
                <Muted>{d.classification ? (d.classification.setOn.key === d.key ? 'Set here' : `From ${d.classification.setOn.label}`) : 'No level above sets one'}</Muted>
              </RuleFact>
              {(['use', 'ai', 'copy'] as const).map(name => (
                <RuleFact key={name} label={`${DATA_RULE_NAME[name]} rule`}>
                  <DataRuleChip name={name} rule={d.rules[name]} />
                  <Muted>{d.rules[name].setOn?.key === d.key ? 'Set here' : `${ruleSourceText(d.rules[name]).charAt(0).toUpperCase()}${ruleSourceText(d.rules[name]).slice(1)}`}</Muted>
                </RuleFact>
              ))}
              <RuleFact label="Retention">
                {d.retention ? <><span className="text-sm text-navy">{d.retention.retentionClass.label}</span><Muted>{d.retention.setOn.key === d.key ? 'Set here' : `From ${d.retention.setOn.label}`}</Muted></> : <Muted>No retention class set</Muted>}
              </RuleFact>
              {d.impliedAnswer && (
                <RuleFact label="Tier answer it implies">
                  <span className="text-sm text-navy">{d.impliedAnswer.label}</span>
                  <Muted>An application that uses this data answers the sensitivity question at least this high.</Muted>
                </RuleFact>
              )}
            </dl>
          </Panel>

          <Panel
            title="Where its data sits"
            count={mappings.length}
            subtitle="By location: the origin of the data there, and the containers it sits in."
            actions={<TextLink href={hrefTo({ page: 'data', tab: 'mapping', domainKey: d.key })} className="text-sm font-semibold">On the mapping</TextLink>}
          >
            {mappings.length ? (
              <RowList label="Where its data sits">
                {mappingsByLocation(mappings).map(group => (
                  <Row
                    key={group.location.dataLocationId}
                    primary={<TextLink href={recordHrefOf('data-location', group.location.dataLocationId) ?? '#'}>{group.location.label}</TextLink>}
                    secondary={[group.location.platform.label, group.location.stage?.label].filter(Boolean).join(', ')}
                    tertiary={(
                      <span className="flex flex-col gap-0.5">
                        {group.mappings.map(m => (
                          <span key={m.dataMappingId}>
                            <TextLink href={rowHref(m.recordUrl, d.key) ?? '#'} className="font-normal">{m.containers.length ? m.containers.join(', ') : 'The whole location'}</TextLink>
                            {m.copyState ? <> <DataUseStateBadge state={m.copyState} approvalsNeeded={m.approvalsNeeded} className="ml-1 align-middle" /></> : null}
                          </span>
                        ))}
                      </span>
                    )}
                    end={<span className="flex flex-wrap justify-end gap-1">{[...new Set(group.mappings.map(m => m.origin))].map(o => <DataOriginBadge key={o} origin={o} short />)}</span>}
                  />
                ))}
              </RowList>
            ) : <Empty>No location holds this domain's data on record yet.</Empty>}
          </Panel>

          <Panel
            title="Applications using it"
            count={uses.length}
            subtitle="Each open use with its decision state. A domain's uses count only the uses declared on this domain, not the ones below."
            actions={<TextLink href={hrefTo({ page: 'data', tab: 'uses', domainKey: d.key })} className="text-sm font-semibold">On the Uses tab</TextLink>}
          >
            {uses.length ? (
              <RowList label="Applications using it">
                {uses.map(u => <UseRow key={u.dataUseId} use={u} href={rowHref(u.recordUrl, d.key)} show="application" />)}
              </RowList>
            ) : <Empty>No application declares a use of this domain.</Empty>}
          </Panel>
        </div>

        <div className={stack}>
          <Panel title="What is missing" count={gaps.length} subtitle="The data checks on this domain and what points at it.">
            {gaps.length ? (
              <RowList label="Findings about this domain">
                {gaps.map((f, i) => <FindingRow key={`${f.check}-${f.subjectId}-${i}`} finding={f} showDomain={false} />)}
              </RowList>
            ) : <Empty>No data check finds anything here.</Empty>}
            <TextLink href={hrefTo({ page: 'data', tab: 'gaps', domainKey: d.key })} className="inline-flex items-center gap-1 self-start text-sm">
              Gaps here and below <ArrowRight className="size-3.5" aria-hidden />
            </TextLink>
          </Panel>

          <Panel title="Decisions" count={decisions.length} subtitle="About this domain, its uses and its mappings. Open first.">
            {decisions.length ? (
              <RowList label="Decisions about this domain">
                {decisions.map(x => (
                  <Row
                    key={x.decisionId}
                    primary={<TextLink href={hrefTo({ page: 'decision', id: x.decisionId })}>{x.statement || x.decisionType.label}</TextLink>}
                    secondary={[x.recordNumber, `${x.decisionType.label} decision`, x.outcome ? `Outcome: ${x.outcome.label}` : null].filter(Boolean).join('. ')}
                    tertiary={x.decidedOn ? `Decided ${fmtDateTime(x.decidedOn)}` : undefined}
                    end={<StatusBadge status={x.status} />}
                  />
                ))}
              </RowList>
            ) : <Empty>No decision is recorded about this domain.</Empty>}
          </Panel>

          {d.children.length > 0 && (
            <Panel title="Below it" count={d.children.length}>
              <RowList label="Domains below it">
                {d.children.map(c => (
                  <Row
                    key={c.key}
                    primary={<TextLink href={hrefTo({ page: 'data-domain', key: c.key })}>{c.label}</TextLink>}
                    end={c.isActive ? undefined : <ToneBadge tone="subtle" icon={null}>Retired</ToneBadge>}
                  />
                ))}
              </RowList>
            </Panel>
          )}

          {(d.examples || (detail.matches ?? []).length > 0) && (
            <Panel title="What belongs here">
              {d.examples && <p className="m-0 text-sm leading-relaxed whitespace-pre-wrap text-navy-2 [overflow-wrap:anywhere]">{d.examples}</p>}
              {(detail.matches ?? []).length > 0 && (
                <div className="flex flex-col gap-1.5">
                  <h3 className={subhead}>Matches in the catalogs</h3>
                  <RowList label="Matches in the catalogs">
                    {(detail.matches ?? []).map(m => (
                      <Row
                        key={`${m.vocabularyKey}-${m.term}`}
                        primary={m.url ? <ExternalLink href={m.url}>{m.term}</ExternalLink> : m.term}
                        secondary={VOCABULARY_TEXT[m.vocabularyKey] ?? 'An outside catalog'}
                        endSub={m.checkedOn ? `Checked ${fmtDate(m.checkedOn)}` : 'Never checked'}
                      />
                    ))}
                  </RowList>
                  <Muted>The product links to the catalog and never copies it.</Muted>
                </div>
              )}
            </Panel>
          )}

          {session.can('list_configuration_changes') && <Panel
            title="Versions that changed it"
            count={versions.status === 'done' ? touched.length : undefined}
            state={versions}
            notes={[]}
            skeletonRows={2}
            subtitle="Configuration versions whose change names this domain. Owners change through owner decisions, not versions."
          >
            {() => touched.length ? (
              <RowList label="Versions that changed it">
                {touched.map(t => (
                  <Row
                    key={t.version.version}
                    primary={<TextLink href={hrefTo({ page: 'configuration', view: 'versions' })}>{`Version ${t.version.version}`}</TextLink>}
                    secondary={t.lines.map(l => versionLineText(l, d.key)).join(' ')}
                    tertiary={[
                      t.version.publishedOn ? `In force from ${fmtDateTime(t.version.publishedOn)}` : null,
                      t.version.acceptedBy.length ? `Accepted by ${t.version.acceptedBy.map(a => a.principal?.displayLabel ?? 'the engine').join(', ')}` : null,
                    ].filter(Boolean).join('. ') || undefined}
                    end={t.version.isActive ? <ToneBadge tone="success" icon={null}>In force</ToneBadge> : undefined}
                  />
                ))}
              </RowList>
            ) : <Empty>No version names this domain since it was drawn with the first one.</Empty>}
          </Panel>}
        </div>
      </div>
    </>
  );
}

const VOCABULARY_TEXT: Record<string, string> = {
  purview: 'Microsoft Purview',
  'purview-label': 'Microsoft Purview label',
  fabric: 'Microsoft Fabric domain',
  'fabric-domain': 'Microsoft Fabric domain',
};

function RuleFact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[160px_minmax(0,1fr)] items-start gap-x-4 border-t border-line py-2 first:border-t-0 first:pt-0 narrow:grid-cols-1 narrow:gap-y-1">
      <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{label}</dt>
      <dd className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1">{children}</dd>
    </div>
  );
}

// ---------- owners and the chain up the tree ----------

const DIRECTORY_TEXT: Partial<Record<NonNullable<DataDomainHolder['directoryState']>, string>> = {
  disabled: 'Disabled in the directory',
  deleted: 'Gone from the directory',
};

function HolderBadges({ holder }: { holder: DataDomainHolder }) {
  const gone = holder.directoryState ? DIRECTORY_TEXT[holder.directoryState] : undefined;
  if (!gone && !holder.isLeaving) return null;
  return (
    <>
      {gone && <ToneBadge tone="danger">{gone}</ToneBadge>}
      {holder.isLeaving && <ToneBadge tone="warning">Leaving</ToneBadge>}
    </>
  );
}

function OwnersPanel({ detail, ancestors, canOwner, subject }: {
  detail: DataDomainDetail;
  ancestors: DataDomainDetail['domain']['pathKeys'];
  canOwner: boolean;
  subject: { kind: 'data-domain'; domainKey: string; label: string };
}) {
  const d = detail.domain;
  const owners: Owners = detail.owners ?? [];
  const roles = owners.map(o => o.ownerRole);
  return (
    <Panel
      title="Who answers for it"
      subtitle={d.ownerRequired ? 'A data owner is required at this level. An owner set higher up answers until one is named here.' : 'An owner set higher up answers for every domain below it, unless one is named lower down.'}
    >
      {owners.length ? (
        <div className="flex flex-col gap-3">
          {owners.map(o => {
            const own = !o.inheritedFrom;
            return (
              <div key={o.ownerRole.key} className="flex flex-col gap-1.5">
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className={subhead}>{o.ownerRole.label}</h3>
                  {!o.inheritsDown && <Muted>Does not pass down to the domains below.</Muted>}
                  {canOwner && (
                    <span className="ml-auto">
                      <ProposeDataOwnerSheet
                        subject={subject}
                        ownerRoleKey={o.ownerRole.key}
                        endAssignmentId={own && o.holder ? o.holder.ownerAssignmentId : undefined}
                        trigger={<Button size="sm" variant="outline">{own && o.holder ? 'Propose a successor' : 'Name one here'}</Button>}
                      />
                    </span>
                  )}
                </div>
                <RowList label={o.ownerRole.label}>
                  {o.holder ? (
                    <Row
                      primary={o.holder.principal.displayLabel}
                      secondary={own ? 'Named on this domain' : `From ${o.inheritedFrom?.label ?? 'a domain above'}`}
                      badges={<HolderBadges holder={o.holder} />}
                      end={o.acting && sameId(o.acting.objectId, o.holder.principal.objectId) ? <ToneBadge tone="success" icon={<CircleCheck aria-hidden />}>Acts now</ToneBadge> : undefined}
                    />
                  ) : (
                    <Row
                      lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" /></span>}
                      primary={`No ${o.ownerRole.label.toLowerCase()}`}
                      secondary="Neither this domain nor any above it names one. An owner change names one; a person decides it."
                    />
                  )}
                  {o.deputies.map(p => (
                    <Row
                      key={p.ownerAssignmentId}
                      primary={p.principal.displayLabel}
                      secondary={`Deputy ${o.ownerRole.label.toLowerCase()}`}
                      badges={<HolderBadges holder={p} />}
                      end={o.acting && sameId(o.acting.objectId, p.principal.objectId) ? <ToneBadge tone="success" icon={<CircleCheck aria-hidden />}>Acts now</ToneBadge> : undefined}
                    />
                  ))}
                  {o.otherHolders.map(p => (
                    <Row key={p.ownerAssignmentId} primary={p.principal.displayLabel} secondary={`Also ${o.ownerRole.label.toLowerCase()}`} badges={<HolderBadges holder={p} />} />
                  ))}
                </RowList>
              </div>
            );
          })}
        </div>
      ) : <Empty>No owner role is configured for data domains.</Empty>}

      {roles.length > 0 && (
        <div className="flex flex-col gap-1.5">
          <h3 className={subhead}>The chain up the tree</h3>
          <ol className="m-0 flex list-none flex-col p-0" aria-label="Owners at each level, from the top">
            {ancestors.map((level, i) => (
              <AncestorLevel key={level.key} level={level} depth={i} roles={roles} answering={roles.map(r => ({ key: r.key, at: answeringLevel(owners, r.key, d.key) }))} />
            ))}
            <ChainLevel
              level={d.pathKeys[d.pathKeys.length - 1]}
              depth={ancestors.length}
              current
              owners={owners}
              roles={roles}
              answering={roles.map(r => ({ key: r.key, at: answeringLevel(owners, r.key, d.key) }))}
            />
          </ol>
        </div>
      )}
    </Panel>
  );
}

type Level = DataDomainDetail['domain']['pathKeys'][number];
type Answering = { key: string; at: string | null }[];

function AncestorLevel({ level, depth, roles, answering }: { level: Level; depth: number; roles: Owners[number]['ownerRole'][]; answering: Answering }) {
  const state = useRead('get_data_domain', { domainKey: level.key, sections: ['owners'] });
  const owners = dataOf(state)?.owners ?? null;
  return <ChainLevel level={level} depth={depth} owners={owners} roles={roles} answering={answering} loading={state.status === 'loading'} failed={state.status === 'done' && !state.envelope.ok} />;
}

function ChainLevel({ level, depth, current, owners, roles, answering, loading, failed }: {
  level: Level;
  depth: number;
  current?: boolean;
  owners: Owners | null;
  roles: Owners[number]['ownerRole'][];
  answering: Answering;
  loading?: boolean;
  failed?: boolean;
}) {
  const here = answering.filter(a => a.at === level.key);
  return (
    <li className="border-t border-line py-2 first:border-t-0 first:pt-0.5" style={{ paddingLeft: `${Math.min(depth, 4) * 14}px` }}>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        {current
          ? <span className="text-sm font-semibold text-navy" aria-current="page">{level.label}</span>
          : <TextLink href={hrefTo({ page: 'data-domain', key: level.key })} className="text-sm">{level.label}</TextLink>}
        {level.level && <Muted>{level.level.label}</Muted>}
        {current && <Badge variant="outline" className="font-medium">This domain</Badge>}
        {here.map(a => (
          <ToneBadge key={a.key} tone="brand" icon={<CircleCheck aria-hidden />}>
            {`Answers as ${roles.find(r => r.key === a.key)?.label.toLowerCase() ?? 'owner'}`}
          </ToneBadge>
        ))}
      </div>
      <div className="mt-0.5 flex flex-col gap-0.5 text-xs text-navy-3">
        {loading ? <span>Reading who is named here.</span>
          : failed ? <span>Who is named here could not be read.</span>
            : roles.map(r => {
              const own = ownHolder(owners, r.key);
              return <span key={r.key}>{`${r.label}: ${own?.holder ? own.holder.principal.displayLabel : 'none of its own'}`}</span>;
            })}
      </div>
    </li>
  );
}
