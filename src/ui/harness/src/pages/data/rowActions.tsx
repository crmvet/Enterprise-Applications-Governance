// The acts a data use or a copy mapping offers, and their facts, shared by the Uses, Mapping and Gaps tabs so a use reads
// and acts the same wherever it shows. Each act shows only when the person's tools hold its write tool and its state
// asks for it; the engine still decides.
import { Bot } from 'lucide-react';
import { DataOriginBadge, DataUseStateBadge, DomainLink } from '../../components/dataChips';
import { StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { DataLocationBrief, DataMappingItem, DataUseItem } from '../../data/contracts';
import { DATA_ACCESS_TEXT } from '../../data/dataTaxonomy';
import { fmtDate } from '../../data/format';
import { useSession } from '../../data/session';
import { Fact, Facts } from '../changes-records/parts';
import { mappingActionsOf, mayProposeDomainOwner, useActionsOf } from './logic';
import { appHref, decisionHref, locationHref, mappingHref, useHref, type DataContext } from './parts';
import { DataDecisionSheet, DataMappingSheet, DataUseSheet, ProposeDataOwnerSheet } from './sheets';

export const useLabel = (u: Pick<DataUseItem, 'applicationName' | 'domain' | 'recordNumber'>) => `${u.recordNumber ? `${u.recordNumber}: ` : ''}${u.applicationName} using ${u.domain.path}`;
export const mappingLabel = (m: Pick<DataMappingItem, 'domain' | 'location'>) => `${m.domain.path} at ${m.location.label}`;

// The acts of one use: confirm an AI draft, propose a decision, appoint a data owner, change or end a use that breaks a
// rule, and open its decision. full adds Change and End for any open use (the selected use, a record).
export function UseActionButtons({ use, ctx, full = false }: { use: DataUseItem; ctx: DataContext; full?: boolean }) {
  const session = useSession();
  const acts = useActionsOf(use);
  const canUse = session.can('save_data_use');
  const canDecide = session.can('save_decision_proposal');
  const canOwner = acts.proposeOwner && !!ctx.tree && session.can('propose_owner_change') && mayProposeDomainOwner(ctx.me, ctx.tree, use.domain.key);
  const decision = decisionHref(use.decisionId);
  const items = [
    acts.confirm && canUse && <DataUseSheet key="confirm" mode="confirm" use={use} applicationId={use.businessApplicationId} applicationName={use.applicationName} trigger={<Button size="sm">Confirm</Button>} />,
    acts.propose && canDecide && <DataDecisionSheet key="propose" subjectKind="data-use" subjectId={use.dataUseId} decisionTypeKey="data-use" label={useLabel(use)} trigger={<Button size="sm">Propose a decision</Button>} />,
    canOwner && <ProposeDataOwnerSheet key="owner" subject={{ kind: 'data-domain', domainKey: use.domain.key, label: use.domain.path }} ownerRoleKey="data-owner" trigger={<Button size="sm">Propose a data owner</Button>} />,
    (acts.mustChange || (full && acts.edit)) && canUse && !acts.confirm && <DataUseSheet key="edit" mode="edit" use={use} applicationId={use.businessApplicationId} applicationName={use.applicationName} trigger={<Button variant="outline" size="sm">Change</Button>} />,
    (acts.mustChange || full) && acts.end && canUse && <DataUseSheet key="end" mode="end" use={use} applicationId={use.businessApplicationId} applicationName={use.applicationName} trigger={<Button variant="outline" size="sm">End the use</Button>} />,
    decision && (
      <Button key="decision" variant={acts.propose || acts.confirm ? 'ghost' : 'outline'} size="sm" asChild>
        <a href={decision}>Open the decision</a>
      </Button>
    ),
  ].filter(Boolean);
  if (!items.length) return null;
  return <span className="flex flex-wrap items-center gap-1.5">{items}</span>;
}

// The facts of one use, for the selected use on the Mapping tab.
export function UseFacts({ use }: { use: DataUseItem }) {
  const app = appHref(use.businessApplicationId);
  const record = useHref(use);
  return (
    <div className="flex flex-col gap-3">
      <Facts>
        <Fact label="Application">{app ? <TextLink href={app}>{use.applicationName}</TextLink> : use.applicationName}</Fact>
        <Fact label="Data"><DomainLink domain={use.domain} /></Fact>
        <Fact label="Where">{use.location ? <LocationName location={use.location} /> : 'Not known'}</Fact>
        <Fact label="Access">{DATA_ACCESS_TEXT[use.access]}</Fact>
        <Fact label="AI">{use.reachesAi ? <ToneBadge tone="informative" icon={<Bot aria-hidden />}>{use.aiServiceAsSeen ? `Reaches ${use.aiServiceAsSeen}` : 'Reaches AI'}</ToneBadge> : 'No'}</Fact>
        <Fact label="Masked data">{use.isDeidentified ? 'Yes' : 'No'}</Fact>
        <Fact label="Decision state"><DataUseStateBadge state={use.state} approvalsGiven={use.approvalsGiven} approvalsNeeded={use.approvalsNeeded} /></Fact>
        <Fact label="Status"><StatusBadge status={use.status} /></Fact>
        <Fact label="Since">{use.startOn ? fmtDate(use.startOn) : 'Not stated'}</Fact>
        <Fact label="Purpose" wide>{use.purpose}</Fact>
      </Facts>
      <span className="flex flex-wrap items-center gap-2">
        <AiMark isAiCreated={use.isAiCreated} performedBy={use.performedBy} />
        {record && <TextLink href={record} className="text-sm">Open the use</TextLink>}
      </span>
    </div>
  );
}

// The acts of one mapping: propose a copy decision, change or end a copy that breaks the rule, open its decision. full
// adds Change and End for any open mapping.
export function MappingActionButtons({ mapping, ctx, full = false }: { mapping: DataMappingItem; ctx: DataContext; full?: boolean }) {
  void ctx;
  const session = useSession();
  const acts = mappingActionsOf(mapping);
  const canMap = session.can('save_data_mapping');
  const decision = decisionHref(mapping.decisionId);
  const items = [
    acts.propose && session.can('save_decision_proposal') && <DataDecisionSheet key="propose" subjectKind="data-mapping" subjectId={mapping.dataMappingId} decisionTypeKey="data-use" label={mappingLabel(mapping)} trigger={<Button size="sm">Propose a copy decision</Button>} />,
    (acts.mustChange || (full && acts.edit)) && canMap && <DataMappingSheet key="edit" mode="edit" mapping={mapping} trigger={<Button variant="outline" size="sm">{acts.mustChange ? 'Change the copy' : 'Edit the mapping'}</Button>} />,
    (acts.mustChange || full) && acts.end && canMap && <DataMappingSheet key="end" mode="end" mapping={mapping} trigger={<Button variant="outline" size="sm">End the mapping</Button>} />,
    decision && (
      <Button key="decision" variant="outline" size="sm" asChild>
        <a href={decision}>Open the decision</a>
      </Button>
    ),
  ].filter(Boolean);
  if (!items.length) return null;
  return <span className="flex flex-wrap items-center gap-1.5">{items}</span>;
}

export function MappingFacts({ mapping, sourceLabel }: { mapping: DataMappingItem; sourceLabel: string | null }) {
  const record = mappingHref(mapping);
  const where = locationHref(mapping.location);
  return (
    <div className="flex flex-col gap-3">
      <Facts>
        <Fact label="Data"><DomainLink domain={mapping.domain} /></Fact>
        <Fact label="Where">{where ? <TextLink href={where}>{mapping.location.label}</TextLink> : mapping.location.label}</Fact>
        <Fact label="Stage">{mapping.location.stage?.label ?? 'No stage'}</Fact>
        <Fact label="Origin"><DataOriginBadge origin={mapping.origin} /></Fact>
        <Fact label="Copy decision">{mapping.copyState ? <DataUseStateBadge state={mapping.copyState} approvalsNeeded={mapping.approvalsNeeded} /> : 'None needed'}</Fact>
        <Fact label="Status"><StatusBadge status={mapping.status} /></Fact>
        <Fact label="Containers" wide>{mapping.containers.length ? mapping.containers.join(', ') : 'The whole location'}</Fact>
        {mapping.copiedFromMappingId && <Fact label="Copied from">{sourceLabel ?? 'Another mapping'}</Fact>}
        <Fact label="Confirmed">{mapping.confirmedOn ? fmtDate(mapping.confirmedOn) : 'Not yet'}</Fact>
      </Facts>
      <span className="flex flex-wrap items-center gap-2">
        <AiMark isAiCreated={mapping.isAiCreated} performedBy={mapping.performedBy} />
        {record && <TextLink href={record} className="text-sm">Open the mapping</TextLink>}
      </span>
    </div>
  );
}

// A location by its name, linked to its record page when it has one.
export function LocationName({ location }: { location: DataLocationBrief }) {
  const href = locationHref(location);
  return href ? <TextLink href={href}>{location.label}</TextLink> : <>{location.label}</>;
}
