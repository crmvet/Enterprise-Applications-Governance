// The pieces other pages render to link into the teams pages.
// The wave 4 scaffold placed each one on its page; the teams group builds them here and keeps the props, because the
// pages that render them are frozen for this group:
//   TeamLink         a group's name as a link to its team page (Roles and access, a binding's group; any group name)
//   HolderLink       a holder's name: a group to its team page, a person to the person page, a workload identity as text
//   OwnerHolderName  Application 360's owners (pages/Application360.tsx, web app only): HolderLink
//   WaitingOnTeams   the decision page (pages/Decision.tsx), under each request: who a pending request waits on
// Choices (D-41):
//   - A request that asks a product role reads list_teams with roleKey: one team is named and linked ("Waiting on the
//     Change board"); several are each linked ("Waiting on any of ..."), because any holder of the role may answer;
//     none says nobody can answer yet and links to Roles and access, where a binding is proposed.
//   - A request that asks an owner role names the holder now, then the deputy: on a data decision from the decision's
//     own data facts (who is asked), on an application from get_application_owners. A group links to its team page.
//   - A request that names someone asks that principal alone: a group named links to its team page; a person is named
//     by the request row already, so nothing more is shown.
//   - While a read is on its way the line stays empty, so the row does not jump; a failed read falls back to the Teams
//     list filtered to the role, which still answers the question.
import { UsersRound } from 'lucide-react';
import type { ReactNode } from 'react';
import { hrefTo, teamHref, teamsHref } from '../../app/links';
import { TextLink } from '../../components/blocks';
import type { DecisionApprovalRecord, DecisionDetail, LabelledKey, PrincipalRef } from '../../data/contracts';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { holderTarget, ownerHolders, waitingOnLead } from './logic';

// A group's name as a link to its team page. label falls back to "A group" when the product holds no name.
export function TeamLink({ objectId, label, className }: { objectId: string; label: string | null | undefined; className?: string }): ReactNode {
  return <TextLink className={className} href={teamHref(objectId)}>{label || 'A group'}</TextLink>;
}

// A holder's name: a group links to its team page, a person to the person page; a workload identity stays text.
export function HolderLink({ principal, className }: { principal: PrincipalRef; className?: string }): ReactNode {
  const target = holderTarget(principal.principalKind);
  if (target === 'team') return <TeamLink className={className} objectId={principal.objectId} label={principal.displayLabel} />;
  if (target === 'person') {
    return (
      <TextLink className={className} href={hrefTo({ page: 'person', objectId: principal.objectId.toLowerCase() })}>
        {principal.displayLabel || 'A person'}
      </TextLink>
    );
  }
  return <span className={className}>{principal.displayLabel || 'A workload identity'}</span>;
}

// An owner's name on Application 360: a group links to its team page, a person to their page.
export function OwnerHolderName({ principal }: { principal: PrincipalRef }): ReactNode {
  return <HolderLink principal={principal} />;
}

// The line under a request: an icon and the words, small, as the request's other notes.
function WaitLine({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex flex-wrap items-center gap-x-1 gap-y-0.5 text-xs text-navy-3">
      <UsersRound className="size-3.5 shrink-0" aria-hidden />
      {children}
    </span>
  );
}

// Names joined as a list of links: "A, B and C".
function joined(nodes: ReactNode[]): ReactNode[] {
  const out: ReactNode[] = [];
  nodes.forEach((n, i) => {
    if (i > 0) out.push(i === nodes.length - 1 ? ' and ' : ', ');
    out.push(n);
  });
  return out;
}

// Under a request on the decision page: the team, teams or holder a pending request waits on.
export function WaitingOnTeams({ record, detail }: { record: DecisionApprovalRecord; detail: DecisionDetail }): ReactNode {
  if (record.outcome.meaning !== 'pending') return null;
  if (record.askedPrincipal) {
    if (record.askedPrincipal.principalKind !== 'group') return null;
    return <WaitLine>Waiting on <TeamLink objectId={record.askedPrincipal.objectId} label={record.askedPrincipal.displayLabel} /></WaitLine>;
  }
  if (record.askedRole) return <WaitingOnRole role={record.askedRole} />;
  if (record.askedOwnerRole) return <WaitingOnOwner ownerRole={record.askedOwnerRole} detail={detail} />;
  return null;
}

// The teams that hold the product role a request asks (list_teams roleKey).
function WaitingOnRole({ role }: { role: LabelledKey }) {
  const state = useRead('list_teams', { roleKey: role.key, top: 5 });
  if (state.status === 'loading') return null;
  const data = dataOf(state);
  if (!data) {
    return <WaitLine><TextLink href={teamsHref({ roleKey: role.key })}>{`Teams that hold the ${role.label} role`}</TextLink></WaitLine>;
  }
  const teams = data.items;
  if (!teams.length) {
    return (
      <WaitLine>
        {`No group holds the ${role.label} role, so nobody can answer it yet.`}
        <TextLink href={hrefTo({ page: 'roles', roleKey: role.key })}>See the role</TextLink>
      </WaitLine>
    );
  }
  const more = data.totalCount - teams.length;
  const links = teams.map(t => <TeamLink key={t.teamId} objectId={t.teamId} label={t.team.displayLabel} />);
  if (more > 0) links.push(<TextLink key="more" href={teamsHref({ roleKey: role.key })}>{`${more} more`}</TextLink>);
  return <WaitLine>{waitingOnLead(data.totalCount)} {joined(links)}</WaitLine>;
}

// The holder of the owner role a request asks, then the deputy: a data decision names them in its data facts; an
// application's are read from its owners.
function WaitingOnOwner({ ownerRole, detail }: { ownerRole: LabelledKey; detail: DecisionDetail }) {
  const dataAsked = detail.data?.asked.find(a => a.kind === 'owner-role' && a.ownerRole?.key === ownerRole.key) ?? null;
  const applicationId = !detail.data ? detail.subject.businessApplicationId : null;
  const owners = useRead('get_application_owners', applicationId ? { businessApplicationId: applicationId } : null);
  if (detail.data) {
    if (!dataAsked) return null;
    if (dataAsked.noOneToAsk || !dataAsked.principal) return <WaitLine>{`Nobody holds the ${ownerRole.label.toLowerCase()} role here yet.`}</WaitLine>;
    return <WaitLine>Waiting on <HolderLink principal={dataAsked.principal} /></WaitLine>;
  }
  if (!applicationId) return null;
  const data = dataOf(owners);
  if (!data) return null;
  const holders = ownerHolders(data.owners, ownerRole.key);
  if (!holders.length) {
    return (
      <WaitLine>
        {`Nobody holds the ${ownerRole.label.toLowerCase()} role on this application yet.`}
        <TextLink href={hrefTo({ page: 'owners', view: 'gaps', applicationId: applicationId.toLowerCase() })}>See the owner gaps</TextLink>
      </WaitLine>
    );
  }
  const main = holders.filter(h => !h.isDeputy);
  const deputies = holders.filter(h => h.isDeputy);
  return (
    <WaitLine>
      {main.length ? <>Waiting on {joined(main.map(h => <HolderLink key={h.ownerAssignmentId} principal={h.principal} />))}</> : 'Waiting on the deputy'}
      {deputies.length > 0 && (
        <>
          {main.length ? ', or deputy ' : ' '}
          {joined(deputies.map(h => <HolderLink key={h.ownerAssignmentId} principal={h.principal} />))}
        </>
      )}
    </WaitLine>
  );
}
