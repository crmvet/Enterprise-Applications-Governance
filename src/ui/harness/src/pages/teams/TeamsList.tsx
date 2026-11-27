// The Teams list (/app/teams?kind=committee|support-team|role-group|other&q=<text>&role=<roleKey>). The product
// owner asked for a page of the teams, committees and people who own the apps and data, to demo.
// Reads: list_teams (tool 95), with the kind, the words and the role from the address; get_configuration (role labels).
// No writes: a team's members are managed in Entra, its roles on Roles and access, its owner roles on Owners.
// Choices (D-41):
//   - The kind tabs show the counts list_teams gives. They follow the search and the role, never the tab, so each tab's
//     count is the rows it opens. "Other groups" shows only when there is one or it is the tab in use.
//   - Every figure in a row opens the team page at the section with its rows (?at=members|waiting|applications|gaps).
//   - The rows come in the read's order: committees first, then support teams, role groups and other groups, by name.
//   - A table from 1024 px; cards below it, as the Agents page does. The filters stay in the address, so Back returns
//     to the same list.
import { UsersRound } from 'lucide-react';
import type { ReactNode } from 'react';
import { hrefTo, teamHref, withParams } from '../../app/links';
import { navigate } from '../../app/navigation';
import { TEAM_KIND_TITLES, type RouteOf, type TeamKindRoute } from '../../app/routes';
import { DataStamp, Empty, EnvelopeNotes, Muted, PageHeader, Panel, PanelFailure, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { ListSearch } from '../../components/listSearch';
import { Badge } from '../../components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/table';
import type { DirectoryState, LabelledKey, MembersReadFrom, TeamKind, TeamListItem } from '../../data/contracts';
import { plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { cn } from '../../lib/utils';
import { usePagedRead } from '../../data/reads';
import { FilterSelect, filterBarClass, LeadIcon, MoreButton, TabCount } from '../ai-audit/parts';
import { Chip } from '../owners-reviews/people';
import { useBelowWide } from '../pmo-portfolio/layout';
import { directoryText, kindCount, orderKinds, peopleText, TEAM_KIND_HELP, TEAM_KIND_ORDER, TEAM_KIND_WORDS, teamsInput, type TeamSection } from './logic';

type Tab = TeamKindRoute | 'all';

// A team page section's address: the team page scrolled to the rows a figure counts.
export function teamSectionHref(objectId: string, at: TeamSection): string {
  return withParams(teamHref(objectId), { at });
}

// A team's kinds as badges, with their words (never color alone).
export function KindBadges({ kinds }: { kinds: readonly TeamKind[] }) {
  return (
    <>
      {orderKinds(kinds).map(k => (
        <Badge key={k} variant={k === 'committee' ? 'brand' : k === 'support-team' ? 'informative' : 'subtle'}>
          {TEAM_KIND_WORDS[k]}
        </Badge>
      ))}
    </>
  );
}

// A group's state in Entra when it is not plain enabled.
export function DirectoryBadge({ state }: { state: DirectoryState | null }) {
  const text = directoryText(state);
  if (!text) return null;
  return <ToneBadge tone={state === 'unknown' ? 'warning' : 'danger'}>{text}</ToneBadge>;
}

export function TeamsPage({ route }: { route: RouteOf<'teams'> }) {
  const below = useBelowWide();
  const roles = useLabels('role');
  const list = usePagedRead('list_teams', { ...teamsInput(route) }, d => d.items);
  const first = list.status === 'done' ? list.first : null;
  const data = first?.data ?? null;
  const tab: Tab = route.kind ?? 'all';

  // Every filter change replaces the address, so Back leaves the page instead of undoing a filter.
  const go = (next: { kind?: TeamKindRoute | null; text?: string | null; roleKey?: string | null }) => {
    const pick = <T,>(value: T | null | undefined, current: T | undefined): T | undefined => (value === undefined ? current : value ?? undefined);
    navigate(hrefTo({
      page: 'teams',
      kind: pick(next.kind, route.kind),
      text: pick(next.text, route.text),
      roleKey: pick(next.roleKey, route.roleKey),
    }), true);
  };

  const tabs: Tab[] = ['all', ...TEAM_KIND_ORDER.filter(k => k !== 'other' || route.kind === 'other' || (data ? data.counts.other > 0 : false))];
  // Only the roles a group can hold: a workload role (the approval writer, the deployment recorder) is held through a
  // workload grant, never by a team (QA, fix round 1).
  const roleOptions = roles.keys().filter(k => roles.field<boolean>(k, 'isWorkloadRole') !== true).map(k => ({ value: k, label: roles.label(k) }));
  if (route.roleKey && !roleOptions.some(o => o.value === route.roleKey)) roleOptions.push({ value: route.roleKey, label: roles.label(route.roleKey) });
  const filtered = !!(route.text || route.roleKey);
  const title = route.kind ? TEAM_KIND_TITLES[route.kind] : 'All teams';

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Teams and committees"
        subtitle="Every group the product knows: the committees that decide, the support teams that own applications, and the groups that carry a product role. Open a team for its people, what it decides and what waits on it."
        meta={<DataStamp envelope={first ?? undefined} />}
        actions={(
          <>
            <TextLink href={hrefTo({ page: 'owners' })} className="text-sm">Owners and people</TextLink>
            <TextLink href={hrefTo({ page: 'roles' })} className="text-sm">Roles and access</TextLink>
          </>
        )}
      />

      <section aria-label="Kind and filters" className={cn(filterBarClass, 'mb-4 flex-col items-stretch')}>
        <SwitchTabs
          label="Show"
          ariaLabel="Kind of team"
          value={tab}
          onChange={v => go({ kind: v === 'all' ? null : v })}
          options={tabs.map(k => ({
            value: k,
            label: (
              <span className="inline-flex items-center gap-1.5">
                {k === 'all' ? 'All' : TEAM_KIND_TITLES[k]}
                <TabCount value={data ? kindCount(data.counts, k === 'all' ? undefined : k) : null} noun={k === 'all' ? 'teams' : TEAM_KIND_TITLES[k].toLowerCase()} />
              </span>
            ),
          }))}
        />
        <div className="flex flex-wrap items-end gap-x-3 gap-y-2.5">
          <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
            <span className="text-xs text-navy-2" aria-hidden>Search</span>
            <ListSearch
              label="Search the teams by team or role name"
              placeholder="Team or role name"
              value={route.text}
              onChange={text => go({ text: text ?? null })}
            />
          </div>
          <FilterSelect
            label="Holds the role"
            value={route.roleKey ?? 'all'}
            onChange={v => go({ roleKey: v === 'all' ? null : v })}
            allLabel="Any role"
            options={roleOptions}
            width="w-[240px]"
          />
        </div>
        {filtered && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Filters in use">
            {route.text && <Chip label={`Words: ${route.text}`} onRemove={() => go({ text: null })} />}
            {route.roleKey && <Chip label={`Holds the role: ${roles.label(route.roleKey)}`} onRemove={() => go({ roleKey: null })} />}
          </div>
        )}
        <Muted className="text-sm">{route.kind ? TEAM_KIND_HELP[route.kind] : 'A group can be more than one kind. The kinds come from the rules in force, so they change when the rules do.'}</Muted>
      </section>

      {list.status === 'failed' ? (
        <PanelFailure failure={list.failure} />
      ) : (
        <Panel
          title={title}
          id="teams-list"
          count={data ? data.totalCount : undefined}
          subtitle={data?.membership ?? (data ? membersNote(data.membersReadFrom) : undefined)}
        >
          {list.status === 'loading' ? (
            <div role="status" aria-label="Loading the teams"><Muted>Reading the teams.</Muted></div>
          ) : !list.items.length ? (
            <Empty>{emptyText(route, roles.label)}</Empty>
          ) : (
            <>
              {below ? (
                <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label={title}>
                  {list.items.map(t => <TeamCard key={t.teamId} team={t} readFrom={data!.membersReadFrom} />)}
                </ul>
              ) : (
                <TeamsTable items={list.items} readFrom={data!.membersReadFrom} title={title} />
              )}
              <MoreButton skipToken={list.skipToken} loading={list.loadingMore} onMore={list.more} what="teams" laterFailure={list.laterFailure} />
            </>
          )}
          {first && <HiddenNotes couldNotRead={first.couldNotRead} />}
          {first && <EnvelopeNotes couldNotRead={[]} warnings={first.warnings} />}
        </Panel>
      )}
    </>
  );
}

// The approval requests the reader may not see, in words. A team whose members Entra did not read comes back as the
// read's warning, shown under it.
function HiddenNotes({ couldNotRead }: { couldNotRead: { kind: string; count: number }[] }) {
  const approvals = couldNotRead.find(c => c.kind === 'approval-record')?.count ?? 0;
  if (!approvals) return null;
  return <Muted>{`${plural(approvals, 'approval request is', 'approval requests are')} hidden by your access and not counted as waiting.`}</Muted>;
}

function membersNote(readFrom: MembersReadFrom): string {
  return readFrom === 'entra-live' ? 'Members are read live from Entra for the signed-in person, and never stored.' : 'Members come from the demo personas.';
}

function emptyText(route: RouteOf<'teams'>, roleLabel: (key: string) => string): string {
  if (route.roleKey && route.text) return `No team that holds the ${roleLabel(route.roleKey)} role matches "${route.text}".`;
  if (route.roleKey) return `No group holds the ${roleLabel(route.roleKey)} role${route.kind ? ` among the ${TEAM_KIND_TITLES[route.kind].toLowerCase()}` : ''}. A configuration admin binds a group to it on Roles and access.`;
  if (route.text) return `No team or role name matches "${route.text}".`;
  if (route.kind) return `No group is ${TEAM_KIND_WORDS[route.kind].toLowerCase()} under the rules in force.`;
  return 'The product knows no group yet. A group shows here once a product role is bound to it, or once it holds an owner role.';
}

// ---------- one team's facts, as parts the table and the cards share ----------

function RolesText({ roles }: { roles: LabelledKey[] }) {
  if (!roles.length) return <Muted className="text-sm">No product role</Muted>;
  return (
    <span className="flex flex-wrap gap-x-1.5 gap-y-0.5 text-sm">
      {roles.map((r, i) => (
        <span key={r.key}>
          <TextLink href={hrefTo({ page: 'roles', roleKey: r.key })}>{r.label}</TextLink>
          {i < roles.length - 1 ? ',' : ''}
        </span>
      ))}
    </span>
  );
}

// A figure that opens the team page at its rows; a zero is plain text, with nothing to open.
function Figure({ team, at, value, text, label }: { team: TeamListItem; at: TeamSection; value: number; text?: string; label: string }) {
  const shown = text ?? String(value);
  if (!value) return <Muted className="text-sm">{shown}</Muted>;
  return (
    <TextLink href={teamSectionHref(team.teamId, at)} aria-label={`${label}: ${shown}. Open ${team.team.displayLabel} at these rows.`} className="tabular-nums">
      {shown}
    </TextLink>
  );
}

function PeopleFigure({ team, readFrom }: { team: TeamListItem; readFrom: MembersReadFrom }) {
  const text = peopleText(team, readFrom);
  if (team.memberCount === null) return <Muted className="text-sm">{text}</Muted>;
  return <Figure team={team} at="members" value={team.memberCount} text={text} label="People" />;
}

function WaitingFigure({ team }: { team: TeamListItem }) {
  if (!team.waitingCount) return <Muted className="text-sm">Nothing waits</Muted>;
  return (
    <TextLink href={teamSectionHref(team.teamId, 'waiting')} aria-label={`${plural(team.waitingCount, 'approval request waits', 'approval requests wait')} on ${team.team.displayLabel}. Open them.`}>
      <ToneBadge tone="warning">{plural(team.waitingCount, 'request', 'requests')}</ToneBadge>
    </TextLink>
  );
}

function ApplicationsFigure({ team }: { team: TeamListItem }) {
  if (!team.assignmentCount) return <Muted className="text-sm">No owner role</Muted>;
  return (
    <span className="flex flex-col items-start gap-0.5">
      <Figure team={team} at="applications" value={team.applicationCount} text={plural(team.applicationCount, 'application', 'applications')} label="Applications" />
      <Muted>{plural(team.assignmentCount, 'owner role', 'owner roles')} held</Muted>
    </span>
  );
}

function GapsFigure({ team }: { team: TeamListItem }) {
  if (!team.applicationCount) return <Muted className="text-sm">None to check</Muted>;
  if (!team.ownerGapCount) return <ToneBadge tone="success">No gaps</ToneBadge>;
  return (
    <TextLink href={teamSectionHref(team.teamId, 'gaps')} aria-label={`${plural(team.ownerGapCount, 'owner gap', 'owner gaps')} on the applications of ${team.team.displayLabel}. Open them.`}>
      <ToneBadge tone="warning">{plural(team.ownerGapCount, 'gap', 'gaps')}</ToneBadge>
    </TextLink>
  );
}

function TeamName({ team }: { team: TeamListItem }): ReactNode {
  return (
    <span className="flex flex-col items-start gap-1">
      <TextLink href={teamHref(team.teamId)} className="font-semibold">{team.team.displayLabel || 'A group'}</TextLink>
      <span className="flex flex-wrap gap-1">
        <KindBadges kinds={team.kinds} />
        <DirectoryBadge state={team.directoryState} />
      </span>
    </span>
  );
}

// ---------- table (from 1024 px) and cards (below) ----------

function TeamsTable({ items, readFrom, title }: { items: TeamListItem[]; readFrom: MembersReadFrom; title: string }) {
  return (
    <Table aria-label={title}>
      <TableHeader>
        <TableRow>
          <TableHead className="w-[26%]">Team</TableHead>
          <TableHead>Product roles</TableHead>
          <TableHead>People</TableHead>
          <TableHead>Waiting now</TableHead>
          <TableHead>Applications</TableHead>
          <TableHead>Owner gaps</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map(t => (
          <TableRow key={t.teamId} className="align-top">
            <TableCell className="whitespace-normal"><TeamName team={t} /></TableCell>
            <TableCell className="max-w-[260px] whitespace-normal"><RolesText roles={t.roles} /></TableCell>
            <TableCell className="text-sm"><PeopleFigure team={t} readFrom={readFrom} /></TableCell>
            <TableCell><WaitingFigure team={t} /></TableCell>
            <TableCell className="text-sm"><ApplicationsFigure team={t} /></TableCell>
            <TableCell><GapsFigure team={t} /></TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function TeamCard({ team, readFrom }: { team: TeamListItem; readFrom: MembersReadFrom }) {
  const fact = (label: string, value: ReactNode, wide = false) => (
    <div className={cn('flex min-w-0 flex-col gap-1', wide && 'col-span-2')}>
      <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{label}</dt>
      <dd className="m-0 text-sm text-navy [overflow-wrap:anywhere]">{value}</dd>
    </div>
  );
  return (
    <li className="flex flex-col gap-3 rounded-lg border border-line p-3">
      <div className="flex items-start gap-3">
        <LeadIcon tone="brand"><UsersRound /></LeadIcon>
        <div className="min-w-0 flex-1"><TeamName team={team} /></div>
      </div>
      <dl className="m-0 grid grid-cols-2 gap-x-4 gap-y-3">
        {fact('Product roles', <RolesText roles={team.roles} />, true)}
        {fact('People', <PeopleFigure team={team} readFrom={readFrom} />)}
        {fact('Waiting now', <WaitingFigure team={team} />)}
        {fact('Applications', <ApplicationsFigure team={team} />)}
        {fact('Owner gaps', <GapsFigure team={team} />)}
      </dl>
    </li>
  );
}
