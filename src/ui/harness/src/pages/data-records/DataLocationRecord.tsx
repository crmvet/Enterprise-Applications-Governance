// The data location record (/app/records/data-location/<id>): one technical location (O25) on any platform, such as a
// Dataverse environment, an Azure SQL database, a Salesforce org or a file share. Shows the platform, the stage, the
// parent and the locations inside it, the technical owner (its own or the parent's), the deployment target, the hosting
// application, the domains held with their origin and containers, when it was last seen, and its flags.
// Reads: find_data_locations (locationId for the location, parentLocationId for the ones inside it), get_data_gaps
// (locationId). Writes, each in the shared side sheet: confirm it is still there, change, retire or add a location
// inside it (DataLocationSheet, save_data_location), map data to it (DataMappingSheet), name its technical owner
// (ProposeDataOwnerSheet, propose_owner_change with dataLocationId), withdraw a draft (withdraw_record).
// Wave 4: "What depends on it?" (get_dependency_impact with dataLocationId, WhatDependsOnIt.tsx): the applications with a
// data use there, the application the location is, and what depends on them, each saying how it joined.
import { ArrowRight, Eye, MapPin, Pencil, Plus, ShieldCheck, UserRoundX } from 'lucide-react';
import { hrefTo, recordHrefOf, withParams } from '../../app/links';
import { DataStamp, Empty, Muted, PageHeader, Panel, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { DataOriginBadge, DomainLink } from '../../components/dataChips';
import { ExternalLink, Row, RowList } from '../../components/rows';
import { AiMark, StampLine } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { DataLocationItem, Success, DataLocationList } from '../../data/contracts';
import { fmtDate, fmtDateTime } from '../../data/format';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { tabHref } from '../app360/nav';
import { Fact, Facts } from '../changes-records/parts';
import { DataLocationSheet, DataMappingSheet, ProposeDataOwnerSheet } from '../data/sheets';
import { Sticky, WithdrawSheet } from '../work-approvals/parts';
import { isDraftRow, LOCATION_FLAG_TEXT, locationFlags, stageText } from './logic';
import { FindingRow, RecordFrame, useCanWrite } from './parts';
import { WhatDependsOnIt } from './WhatDependsOnIt';

const grid = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';
const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export function DataLocationRecord({ dataLocationId }: { dataLocationId: string }) {
  const state = useRead('find_data_locations', { locationId: dataLocationId, includeRetired: true });
  const back = { label: 'All data locations', href: hrefTo({ page: 'data', tab: 'locations' }) };
  if (state.status === 'loading') return <RecordFrame eyebrow="Data location" title="Data location" state="loading" back={back} />;
  if (!state.envelope.ok) return <RecordFrame eyebrow="Data location" title="Data location" state={state.envelope} back={back} />;
  const location = state.envelope.data.items.find(i => i.dataLocationId.toLowerCase() === dataLocationId.toLowerCase());
  if (!location) {
    return (
      <RecordFrame
        eyebrow="Data location"
        title="Data location"
        state="gone"
        back={back}
        gone="No location with this link is listed. It may have been withdrawn as a draft, or your access does not show it."
      />
    );
  }
  return <LocationBody location={location} envelope={state.envelope} />;
}

function LocationBody({ location, envelope }: { location: DataLocationItem; envelope: Success<DataLocationList> }) {
  const canWrite = useCanWrite();
  const children = useRead('find_data_locations', location.childCount > 0 ? { parentLocationId: location.dataLocationId, top: 100 } : null);
  const gaps = useRead('get_data_gaps', { locationId: location.dataLocationId, top: 50 });
  const retired = !!location.retiredOn;
  // A withdrawn draft is archived with no retirement date: it is closed as a retired one is (QA walk, found in fix round
  // 1: the page still offered Change, Retire and Map data here on a withdrawn draft).
  const closed = retired || location.status.meaning === 'archived';
  const draft = isDraftRow(location);
  // A closed location has no gaps: "What is missing" reads none, so its badges do not say "No data mapped" or "No
  // technical owner" beside Withdrawn or Retired (wave 4b verify).
  const flags = closed ? [] : locationFlags(location.flags);
  const owner = location.technicalOwner;
  const canSave = canWrite('save_data_location') && !closed;
  const canMap = canWrite('save_data_mapping') && !closed && !draft;
  const canOwner = canWrite('propose_owner_change') && !closed;
  const canWithdraw = canWrite('withdraw_record') && draft;
  const subject = { kind: 'data-location' as const, dataLocationId: location.dataLocationId, label: location.label };
  const stage = stageText(location.stage, location.isProduction);
  const gapItems = dataOf(gaps)?.items ?? [];

  return (
    <>
      <PageHeader
        // The platform keeps its own case in the subtitle and the facts; the eyebrow is uppercase (QA walk, wave 4b).
        eyebrow="Data location"
        title={location.label}
        status={(
          <span className="flex flex-wrap items-center gap-1.5">
            {/* An archived location reads as what happened to it: retired, or a draft withdrawn (QA walk). */}
            <StatusBadge status={location.status.meaning === 'archived' ? { ...location.status, label: retired ? 'Retired' : 'Withdrawn' } : location.status} />
            {flags.map(f => (
              <ToneBadge key={f} tone={f === 'draft' ? 'informative' : f === 'copyOutsideProduction' || f === 'noTechnicalOwner' ? 'danger' : 'warning'}>
                {LOCATION_FLAG_TEXT[f]}
              </ToneBadge>
            ))}
          </span>
        )}
        subtitle={`${location.platform.label}. ${stage}.${location.parent ? ` Inside ${location.parent.label}.` : ''}`}
        meta={(
          <span className="flex flex-col gap-0.5">
            <AiMark isAiCreated={location.isAiCreated} performedBy={location.performedBy} className="self-start" />
            <StampLine performedBy={location.performedBy} />
            <span><DataStamp envelope={envelope} /></span>
          </span>
        )}
        actions={(
          <>
            <Sticky show={canSave && draft}>
              <DataLocationSheet mode="confirm" location={location} trigger={<Button size="sm"><ShieldCheck aria-hidden />Check and confirm</Button>} />
            </Sticky>
            <Sticky show={canSave && !draft}>
              <DataLocationSheet mode="confirm-seen" location={location} trigger={<Button size="sm" variant="outline"><Eye aria-hidden />Confirm it is still there</Button>} />
            </Sticky>
            <Sticky show={canMap}>
              <DataMappingSheet mode="add" dataLocationId={location.dataLocationId} trigger={<Button size="sm" variant="outline"><MapPin aria-hidden />Map data here</Button>} />
            </Sticky>
            <Sticky show={canSave && !draft}>
              <DataLocationSheet mode="edit" location={location} trigger={<Button size="sm" variant="outline"><Pencil aria-hidden />Change</Button>} />
            </Sticky>
            <Sticky show={canSave && !draft}>
              <DataLocationSheet mode="retire" location={location} trigger={<Button size="sm" variant="outline">Retire</Button>} />
            </Sticky>
            <Sticky show={canWithdraw}>
              <WithdrawSheet subjectKind="data-location" subjectId={location.dataLocationId} title={location.label} />
            </Sticky>
            <WhatDependsOnIt start={{ dataLocationId: location.dataLocationId }} name={location.label} />
          </>
        )}
      />

      {retired && (
        <p className="mt-0 mb-4 text-sm text-navy-2">
          Retired on {fmtDate(location.retiredOn)}. It keeps its history; nothing new is mapped to it.
        </p>
      )}

      <div className={grid}>
        <div className={stack}>
          <Panel title="The location">
            <Facts>
              <Fact label="Platform">{location.platform.label}</Fact>
              <Fact label="Stage">{stage}</Fact>
              <Fact label="Inside">
                {location.parent
                  ? <TextLink href={recordHrefOf('data-location', location.parent.dataLocationId) ?? '#'}>{location.parent.label}</TextLink>
                  : 'Nothing: a top location'}
              </Fact>
              <Fact label="Hosted by">
                {location.hostedByApplication
                  ? <TextLink href={tabHref(location.hostedByApplication.businessApplicationId, 'data')}>{location.hostedByApplication.name}</TextLink>
                  : 'No application named'}
              </Fact>
              <Fact label="Deployment target">
                {location.deploymentTargetId
                  ? <TextLink href={hrefTo({ page: 'place', id: location.deploymentTargetId })}>Open it in Places</TextLink>
                  : 'Not linked'}
              </Fact>
              <Fact label="Kind of link">{location.linkKind.label}</Fact>
              <Fact label="Last seen" sub={location.flags.stale ? 'Longer ago than the setting allows: confirm it is still there.' : undefined}>
                {location.lastSeenOn ? fmtDateTime(location.lastSeenOn) : 'Never confirmed'}
              </Fact>
              {location.url && <Fact label="Open it" wide><ExternalLink href={location.url}>{location.label}</ExternalLink></Fact>}
              {location.externalId && !location.url && (
                <Fact label="Its name in the platform" wide>
                  {GUID.test(location.externalId) ? 'Recorded as an ID in the platform' : location.externalId}
                </Fact>
              )}
            </Facts>
            {location.externalId === null && location.url === null && (
              <Muted>Its address in the platform is not shown to every role, or none is recorded.</Muted>
            )}
          </Panel>

          <Panel
            title="Data held here"
            count={location.domains.length}
            subtitle="Each domain's data at this location, with its origin and the containers it sits in."
            actions={<TextLink href={hrefTo({ page: 'data', tab: 'mapping', locationId: location.dataLocationId })} className="text-sm font-semibold">On the mapping</TextLink>}
          >
            {location.domains.length ? (
              <RowList label="Data held here">
                {location.domains.map(d => {
                  const href = recordHrefOf('data-mapping', d.dataMappingId);
                  return (
                    <Row
                      key={d.dataMappingId}
                      primary={<DomainLink domain={d.domain} />}
                      secondary={d.containers.length ? `In ${d.containers.join(', ')}` : 'The whole location'}
                      tertiary={href ? <TextLink href={withParams(href, { domain: d.domain.key })} className="font-normal">The mapping</TextLink> : undefined}
                      end={<DataOriginBadge origin={d.origin} />}
                    />
                  );
                })}
              </RowList>
            ) : (
              <Empty>No domain's data is mapped here yet{location.childCount ? ', though locations inside it may hold some' : ''}.</Empty>
            )}
          </Panel>

          <Panel title="What is missing" state={gaps} count={dataOf(gaps)?.totalCount} skeletonRows={2} notes={[]}>
            {() => gapItems.length ? (
              <>
                <RowList label="Findings about this location">
                  {gapItems.map((f, i) => <FindingRow key={`${f.check}-${f.subjectId}-${i}`} finding={f} />)}
                </RowList>
                <TextLink href={hrefTo({ page: 'data', tab: 'gaps', locationId: location.dataLocationId })} className="inline-flex items-center gap-1 self-start text-sm">
                  On the Gaps tab <ArrowRight className="size-3.5" aria-hidden />
                </TextLink>
              </>
            ) : <Empty>No data check finds anything here.</Empty>}
          </Panel>
        </div>

        <div className={stack}>
          <Panel
            title="Technical owner"
            subtitle="Keeps the location running and answers for it. A location inside another takes the parent's owner unless it names its own."
            actions={canOwner ? (
              <ProposeDataOwnerSheet
                subject={subject}
                ownerRoleKey="technical-owner"
                endAssignmentId={owner && !owner.fromParent ? owner.ownerAssignmentId : undefined}
                trigger={<Button size="sm" variant="outline">{owner && !owner.fromParent ? 'Propose a successor' : 'Propose an owner'}</Button>}
              />
            ) : undefined}
          >
            {owner ? (
              <RowList label="Technical owner">
                <Row primary={owner.principal.displayLabel} secondary={owner.fromParent && location.parent ? `From ${location.parent.label}` : 'Named on this location'} />
              </RowList>
            ) : (
              <RowList label="Technical owner">
                <Row
                  lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" /></span>}
                  primary="No technical owner"
                  secondary="An owner change names one. It needs a decision by a person."
                />
              </RowList>
            )}
          </Panel>

          <Panel
            title="Locations inside it"
            count={location.childCount}
            state={location.childCount > 0 ? children : undefined}
            skeletonRows={2}
            notes={[]}
            actions={canSave && !draft ? (
              <DataLocationSheet mode="add" parentLocationId={location.dataLocationId} trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Add one</Button>} />
            ) : undefined}
          >
            {location.childCount > 0
              ? (() => {
                const items = dataOf(children)?.items ?? [];
                return items.length ? (
                  <RowList label="Locations inside it">
                    {items.map(c => (
                      <Row
                        key={c.dataLocationId}
                        primary={<TextLink href={recordHrefOf('data-location', c.dataLocationId) ?? '#'}>{c.label}</TextLink>}
                        secondary={[c.platform.label, c.stage?.label].filter(Boolean).join(', ')}
                        tertiary={c.domains.length ? `${c.domains.length} ${c.domains.length === 1 ? 'domain' : 'domains'} mapped` : 'No domain mapped'}
                        end={c.status.meaning === 'draft' ? <ToneBadge tone="informative">Draft</ToneBadge> : <StatusBadge status={c.status} />}
                      />
                    ))}
                  </RowList>
                ) : <Empty>None is listed.</Empty>;
              })
              : <Empty>No location sits inside this one.</Empty>}
          </Panel>
        </div>
      </div>
    </>
  );
}
