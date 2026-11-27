// Owners and people: the list (/app/owners?view=by-person|by-role|gaps|leavers&app=<id>&portfolio=<id>, and the page's
// own role=<owner role key>) and one person (/app/people/<objectId>).
// Reads: list_owner_assignments (current rows in scope, the leavers, one person's rows with ended ones, the signed-in
// person's own rows), get_portfolio_summary (the owner gap list), get_decision_log (ownership decisions in flight),
// list_reviews (owner check reviews), list_portfolios, list_approvals (on the signed-in person's own page), whoami,
// list_teams (wave 4: the person page's "Member of", by memberObjectId).
// Wave 4 (the teams group): every view has the list's text search in
// the address (q=, components/listSearch.tsx); a change of view clears it, a change of portfolio or application keeps
// it. A group's name links to its team page (people.tsx PersonLink); By role names the teams that hold each owner role;
// the person page lists the teams the person is in and no longer shows the Entra object ID.
// Writes, each in the shared side sheet: propose_owner_change, record_owner_leave, submit_record (a decision).
// An action shows only where the person's roles and owner rows allow it; the engine still checks every write.
import { UserRoundX, UsersRound } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { goTo, hrefTo, teamsHref } from '../app/links';
import { navigate } from '../app/navigation';
import { OWNERS_VIEWS, type OwnersView, type RouteOf } from '../app/routes';
import { DataStamp, Empty, KpiTile, LinkButton, MoreRowsNote, Muted, PageHeader, Panel, StatusBadge, SwitchTabs, TextLink, ToneBadge, useNarrow, BarList } from '../components/blocks';
import { ListSearch, useListText } from '../components/listSearch';
import { Row, RowList } from '../components/rows';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import type { DataDomainList, DecisionLog, DecisionLogItem, OwnerAssignmentItem, OwnerAssignmentList, OwnerCoverage, PortfolioSummary, ReviewItem, ReviewList, TeamList } from '../data/contracts';
import { fmtDate, plural } from '../data/format';
import { useLabels, type Labels } from '../data/labels';
import { useRead } from '../data/reads';
import { filterByText, matchesText } from '../data/textSearch';
import { dataOf, readFailed, useTool, type ToolState } from '../data/toolClient';
import { useHost } from '../harness';
import { useAllPages, useQueryParam, useWhoAmI, withParams } from './owners-reviews/hooks';
import {
  approvalsText,
  canProposeOwnerChange,
  canRecordLeave,
  deputyOf,
  namedSuccessorOf,
  groupByPerson,
  holdsRole,
  leaveReason,
  mayCall,
  ownedApplicationIds,
  personOptions,
  proposalsByApplication,
  sameId,
  sortPeople,
  type PersonGroup,
  type PersonSort,
  type WhoAmI,
} from './owners-reviews/model';
import { ProposalBanner, ProposeOwnerChangeSheet, RecordLeaveSheet, type Proposal } from './owners-reviews/OwnerSheets';
import { OwnerCheckSheet } from './deployments-places/sheets';
import { Chip, Fact, facts2Class, kpiRowClass, kpiRowClass3, LeaveBadge, PersonAvatar, PersonLink, scrollToPanel, twoColumnClass } from './owners-reviews/people';
import { recordHref, recordHrefOf } from '../app/links';
import { LeadIcon } from './ai-audit/parts';
import { TeamLink } from './teams/links';
import { roleTeamText, teamsByOwnerRole } from './teams/logic';
import { KindBadges, teamSectionHref } from './teams/TeamsList';

const KEY = /^[a-z][a-z0-9-]{0,63}$/;

const VIEW_LABELS: Record<OwnersView, string> = {
  'by-person': 'By person',
  'by-role': 'By role',
  gaps: 'Gaps',
  leavers: 'Leavers',
};

const PERSON_SORTS: { key: PersonSort; label: string }[] = [
  { key: 'name', label: 'Name, A to Z' },
  { key: 'applications', label: 'Most applications' },
  { key: 'leaving', label: 'Leavers first' },
];

// The list's search box, per view: what it searches.
const SEARCH_TEXT: Record<OwnersView, { label: string; placeholder: string }> = {
  'by-person': { label: 'Search the people by name, application or owner role', placeholder: 'Person, application or owner role' },
  'by-role': { label: 'Search the owner roles by role or team name', placeholder: 'Owner role or team' },
  gaps: { label: 'Search the owner gaps by application, owner role or portfolio', placeholder: 'Application, owner role or portfolio' },
  leavers: { label: 'Search the leavers by name or application', placeholder: 'Person or application' },
};

const groupTitleClass = 'm-0 mt-2 mb-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase first:mt-0';

// The review types whose outcome is an ownership decision: the owner check reviews a leave opens.
function useOwnerCheckTypes(): string[] {
  const reviewTypes = useLabels('review-type');
  const keys = reviewTypes.keys().filter(k => reviewTypes.field(k, 'producesDecisionTypeKey') === 'ownership');
  return keys.length ? keys : ['owner-check'];
}

// Owner roles a person holds, as "Business owner on 3, Technical owner on 1".
function rolesText(assignments: OwnerAssignmentItem[], ownerRoles: Labels, deputy: boolean): string {
  const counts = new Map<string, number>();
  for (const a of assignments) if (a.isDeputy === deputy) counts.set(a.ownerRoleKey, (counts.get(a.ownerRoleKey) ?? 0) + 1);
  return [...counts.entries()].map(([k, n]) => `${ownerRoles.label(k)} on ${n}`).join(', ');
}

// =====================================================================================================================
// The list: /app/owners
// =====================================================================================================================

export function Owners({ route }: { route: RouteOf<'owners'> }) {
  const host = useHost();
  const today = host.today;
  const view: OwnersView = route.view ?? 'by-person';
  const role = useQueryParam('role', KEY);
  const ownerRoles = useLabels('owner-role');
  const { me } = useWhoAmI();
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const text = useListText() ?? '';
  const [sort, setSort] = useState<PersonSort>('name');

  const filters: Record<string, unknown> = {};
  if (route.portfolioId) filters.portfolioId = route.portfolioId;
  if (route.applicationId) filters.businessApplicationId = route.applicationId;
  // Who is leaving, and when, is personal data: the product shows it to a platform admin only (the owner check's
  // audience), so only an admin's page asks for the leavers.
  const isAdmin = holdsRole(me, 'platform-admin');
  const scope = useAllPages('list_owner_assignments', { state: 'current', top: 100, ...filters });
  const leavers = useAllPages('list_owner_assignments', isAdmin ? { state: 'current', leaversOnly: true, top: 100, ...filters } : null);
  // The assignments an accepted owner change starts later: a leaver whose successor is named needs no other proposal.
  const upcoming = useAllPages('list_owner_assignments', (view === 'leavers' && isAdmin) || view === 'gaps' ? { state: 'upcoming', top: 100, ...filters } : null);
  const gaps = useTool('get_portfolio_summary', { groupBy: 'portfolio', includeOwnerGaps: true, top: 100, ...(route.portfolioId ? { portfolioId: route.portfolioId } : {}) });
  const mineState = useAllPages('list_owner_assignments', me ? { principalObjectId: me.principal.objectId, state: 'current', top: 100 } : null);
  const portfolios = useTool('list_portfolios', { top: 100 });
  const ownerCheckTypes = useOwnerCheckTypes();
  const ownerChecks = useAllPages('list_reviews', view === 'leavers' && isAdmin ? { state: 'due', reviewTypeKeys: ownerCheckTypes, top: 100, ...filters } : null);
  const proposals = useTool('get_decision_log', (view === 'leavers' && isAdmin) || view === 'gaps' ? { decisionTypeKeys: ['ownership'], statusMeanings: ['proposed'], top: 100 } : null);

  const scopeData = dataOf(scope);
  const scopeItems = scopeData?.items ?? [];
  const upcomingItems = dataOf(upcoming)?.items ?? [];
  const leaverItems = dataOf(leavers)?.items ?? [];
  const mine = useMemo(() => ownedApplicationIds(dataOf(mineState)?.items ?? []), [mineState]);
  const people = useMemo(() => groupByPerson(scopeItems), [scopeItems]);
  const leaverPeople = useMemo(() => groupByPerson(leaverItems), [leaverItems]);
  const gapData = dataOf(gaps);
  const gapList = (gapData?.ownerGapList ?? []).filter(g => !route.applicationId || sameId(g.businessApplicationId, route.applicationId));
  // The gaps view lists roles with no holder or no deputy, and holders whose account is off, one row per application and
  // role. The tile shows the server's one owner gap count (document 24, B24-17), the number the Overview and Portfolios
  // show for the same scope; for one application it counts the rows the view shows.
  // For one application the scope's own count (list_owner_assignments ownerGaps, the same computation) is used when the
  // read carries it, so the number matches Application 360 and the Overview to the gap.
  const gapCount = route.applicationId
    ? (typeof scopeData?.ownerGaps === 'number' ? scopeData.ownerGaps : gapData ? gapList.length + offHolders(scopeItems, gapList).length : null)
    : gapData ? gapData.ownerGaps : null;

  // A change of view clears the search, which searches other words on each view; a change of scope keeps it.
  const go = (next: { view?: OwnersView; applicationId?: string; portfolioId?: string }, nextRole: string | undefined = role) => {
    const target = { page: 'owners' as const, view: route.view, applicationId: route.applicationId, portfolioId: route.portfolioId, ...next };
    const sameView = (target.view ?? 'by-person') === view;
    navigate(withParams(hrefTo(target), { role: nextRole, q: sameView ? text || undefined : undefined }), true);
  };
  // The role filter belongs to the by-person and gaps views; the other views drop it.
  const setView = (v: OwnersView) => go({ view: v === 'by-person' ? undefined : v }, v === 'by-person' || v === 'gaps' ? role : undefined);

  const appName = route.applicationId
    ? scopeItems.find(a => sameId(a.businessApplicationId, route.applicationId))?.applicationName
      ?? gapList.find(g => sameId(g.businessApplicationId, route.applicationId))?.name
      ?? 'This application'
    : null;
  const portfolioItems = dataOf(portfolios)?.items ?? [];
  const portfolioName = (id: string) => {
    const p = portfolioItems.find(x => sameId(x.portfolioId, id));
    return p ? p.path.map(x => x.name).join(' / ') || p.name : 'Portfolio';
  };

  const leaveCandidates = personOptions(
    scopeItems.filter(a => isAdmin || mine.has(a.businessApplicationId.toLowerCase()) || sameId(a.principal.objectId, me?.principal.objectId)),
    me,
    { includeAway: true },
  );
  const showLeave = mayCall(me, 'record_owner_leave') && (isAdmin || mine.size > 0);

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Owners and people"
        subtitle="Who owns what across the portfolio, the owner roles with no holder or no deputy, and the people who leave. Every owner change is a decision that people accept."
        meta={<DataStamp envelope={scope.status === 'done' ? scope.envelope : undefined} />}
        actions={showLeave ? (
          <RecordLeaveSheet person={null} candidates={leaveCandidates} trigger={<Button variant="outline">Record a leave</Button>} />
        ) : undefined}
      />

      <ProposalBanner key={proposal?.decisionId ?? 'none'} proposal={proposal} onDismiss={() => setProposal(null)} />

      <div className={isAdmin ? kpiRowClass : kpiRowClass3} role="group" aria-label="Key figures">
        <KpiTile
          className="narrow:col-span-full"
          label="Applications"
          value={scopeData?.applicationsInScope ?? null}
          caption={route.applicationId ? 'The one application chosen' : route.portfolioId ? 'Registered, in this portfolio' : 'Registered, all portfolios'}
          onClick={() => (route.applicationId
            ? goTo({ page: 'app', id: route.applicationId })
            : goTo({ page: 'applications', filter: route.portfolioId ? { portfolioIds: [route.portfolioId] } : {} }))}
        />
        <KpiTile label="People with an owner role" value={scopeData ? people.length : null} caption="Holders and deputies now" onClick={() => go({ view: undefined }, undefined)} />
        <KpiTile label="Owner gaps" value={gapCount} caption="No holder, no deputy, or a holder whose account is off" attention={!!gapCount} onClick={() => go({ view: 'gaps' }, undefined)} />
        {isAdmin && (
          <>
            <KpiTile label="Leaving or disabled" value={dataOf(leavers) ? leaverPeople.length : null} caption="Leave date, or account off in Entra" attention={leaverPeople.length > 0} onClick={() => go({ view: 'leavers' }, undefined)} />
            <KpiTile
              label="Approvals on leavers' roles"
              value={dataOf(leavers) ? leaverPeople.reduce((n, p) => n + p.pendingApprovals, 0) : null}
              caption="Pending, would need to move"
              onClick={() => go({ view: 'leavers' }, undefined)}
            />
          </>
        )}
      </div>

      <section aria-label="View and filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <SwitchTabs
            label="View"
            ariaLabel="Owners view"
            value={view}
            onChange={setView}
            options={OWNERS_VIEWS.filter(v => isAdmin || v !== 'leavers').map(v => ({ value: v, label: VIEW_LABELS[v] }))}
          />
          <div className="ml-auto flex items-center gap-2 narrow:ml-0 narrow:w-full">
            <span className="text-xs text-navy-3" id="owners-portfolio-label">Portfolio</span>
            <Select value={route.portfolioId ?? 'all'} onValueChange={v => go({ portfolioId: v === 'all' ? undefined : v })}>
              <SelectTrigger className="w-[240px] narrow:flex-1" aria-labelledby="owners-portfolio-label"><SelectValue /></SelectTrigger>
              <SelectContent className="max-h-[320px]">
                <SelectItem value="all">All portfolios</SelectItem>
                {portfolioItems.map(p => <SelectItem key={p.portfolioId} value={p.portfolioId.toLowerCase()}>{portfolioName(p.portfolioId)}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
        </div>
        {(view !== 'leavers' || isAdmin) && (
          <div className="flex flex-wrap items-center gap-2">
            <ListSearch label={SEARCH_TEXT[view].label} placeholder={SEARCH_TEXT[view].placeholder} className="w-auto min-w-[240px] flex-1 narrow:w-full narrow:min-w-0 narrow:basis-full" />
            {view === 'by-person' && <div className="flex items-center gap-2">
              <span className="text-xs text-navy-3" id="owners-sort-label">Sort</span>
              <Select value={sort} onValueChange={v => setSort(v as PersonSort)}>
                <SelectTrigger className="w-[180px]" aria-labelledby="owners-sort-label"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {PERSON_SORTS.map(s => <SelectItem key={s.key} value={s.key}>{s.label}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>}
          </div>
        )}
        {(route.applicationId || role || route.portfolioId) && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {route.applicationId && <Chip label={`Application: ${appName}`} onRemove={() => go({ applicationId: undefined })} />}
            {route.portfolioId && <Chip label={`Portfolio: ${portfolioName(route.portfolioId)}`} onRemove={() => go({ portfolioId: undefined })} />}
            {role && (view === 'by-person' || view === 'gaps') && <Chip label={`Owner role: ${ownerRoles.label(role)}`} onRemove={() => go({}, undefined)} />}
          </div>
        )}
      </section>

      {view === 'by-person' && <ByPerson scope={scope} role={role} text={text} sort={sort} today={today} ownerRoles={ownerRoles} />}
      {view === 'by-role' && (
        <ByRole
          scope={scope}
          text={text}
          ownerRoles={ownerRoles}
          onPeople={k => go({ view: undefined }, k)}
          onGaps={k => go({ view: 'gaps' }, k)}
        />
      )}
      {view === 'gaps' && (
        <Gaps
          gaps={gaps}
          gapList={gapList}
          scopeItems={scopeItems}
          upcomingItems={upcomingItems}
          text={text}
          role={role}
          applicationId={route.applicationId}
          me={me}
          mine={mine}
          today={today}
          ownerRoles={ownerRoles}
          onProposed={setProposal}
          inFlight={proposalsByApplication(dataOf(proposals)?.items ?? [])}
        />
      )}
      {view === 'leavers' && !isAdmin && (
        <Panel title="Leavers">
          <Empty>Who is leaving, and when, is shown to a platform admin only. The owner gaps show any role that needs a successor.</Empty>
        </Panel>
      )}
      {view === 'leavers' && isAdmin && (
        <Leavers
          leavers={leavers}
          text={text}
          scopeItems={scopeItems}
          upcomingItems={upcomingItems}
          ownerChecks={ownerChecks}
          proposals={proposals}
          me={me}
          mine={mine}
          today={today}
          onProposed={setProposal}
          canCheck={mayCall(me, 'run_owner_check')}
        />
      )}
    </>
  );
}

// ---------- by person ----------

function ByPerson({ scope, role, text, sort, today, ownerRoles }: {
  scope: ToolState<OwnerAssignmentList>;
  role: string | undefined;
  text: string;
  sort: PersonSort;
  today: string;
  ownerRoles: Labels;
}) {
  const narrow = useNarrow();
  const data = dataOf(scope);
  // The data domains each person owns in their own name (product review, fix round 1), from one find_data_domains read.
  const domainState = useRead('find_data_domains', { top: 100 });
  const domainsOf = useMemo(() => {
    const counts = new Map<string, number>();
    for (const d of dataOf(domainState)?.items ?? []) {
      const owner = d.owner && !d.owner.inheritedFrom ? d.owner.principal.objectId.toLowerCase() : null;
      if (owner) counts.set(owner, (counts.get(owner) ?? 0) + 1);
    }
    return (objectId: string) => (dataOf(domainState) ? counts.get(objectId.toLowerCase()) ?? 0 : null);
  }, [domainState]);
  const people = useMemo(() => {
    const items = (data?.items ?? []).filter(a => !role || a.ownerRoleKey === role);
    const found = filterByText(groupByPerson(items), text, p => [p.principal.displayLabel, p.assignments.map(a => a.applicationName), p.assignments.map(a => a.label)]);
    return sortPeople([...found], sort);
  }, [data, role, text, sort]);
  return (
    <Panel title="People and what they own" count={data ? people.length : undefined} state={scope} skeletonRows={8} subtitle="Current holders and deputies. Open a person for their applications, gaps and leave.">
      {d => (
        <>
          {!people.length ? (
            <Empty>{text.trim() ? `No person with an owner role matches "${text.trim()}".` : role ? `Nobody holds the ${ownerRoles.label(role).toLowerCase()} role in this scope.` : 'No current owner assignment in this scope.'}</Empty>
          ) : narrow ? (
            <RowList label="People">
              {people.map(p => (
                <Row
                  key={p.principal.objectId}
                  lead={<PersonAvatar principal={p.principal} />}
                  primary={<PersonLink principal={p.principal} />}
                  secondary={rolesText(p.assignments, ownerRoles, false) || 'Deputy only'}
                  tertiary={`${plural(p.applications, 'application', 'applications')}${p.deputyFor ? `, deputy on ${p.deputyFor}` : ''}${domainsOf(p.principal.objectId) ? `, data owner of ${plural(domainsOf(p.principal.objectId)!, 'data domain', 'data domains')}` : ''}${p.pendingApprovals ? `, ${plural(p.pendingApprovals, 'approval waits', 'approvals wait')} on their roles` : ''}`}
                  badges={<PersonBadges person={p} today={today} />}
                />
              ))}
            </RowList>
          ) : (
            <Table aria-label="People and what they own">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[30%]">Person</TableHead>
                  <TableHead>Holds</TableHead>
                  <TableHead>Deputy for</TableHead>
                  <TableHead className="text-right">Applications</TableHead>
                  <TableHead className="text-right">Data domains</TableHead>
                  <TableHead className="text-right">Approvals on their roles</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {people.map(p => (
                  <TableRow key={p.principal.objectId} className="align-top">
                    <TableCell className="whitespace-normal">
                      <div className="flex items-start gap-2.5">
                        <PersonAvatar principal={p.principal} className="size-7" />
                        <div className="flex min-w-0 flex-col gap-1">
                          <PersonLink principal={p.principal} className="text-sm" />
                          <div className="flex flex-wrap gap-1"><PersonBadges person={p} today={today} /></div>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell className="text-sm whitespace-normal text-navy">{rolesText(p.assignments, ownerRoles, false) || <Muted>None</Muted>}</TableCell>
                    <TableCell className="text-sm whitespace-normal text-navy">{rolesText(p.assignments, ownerRoles, true) || <Muted>None</Muted>}</TableCell>
                    <TableCell className="text-right text-sm text-navy tabular-nums">
                      <TextLink href={hrefTo({ page: 'person', objectId: p.principal.objectId.toLowerCase() })} aria-label={`${p.applications} applications: open ${p.principal.displayLabel}`}>{p.applications}</TextLink>
                    </TableCell>
                    <TableCell className="text-right text-sm text-navy tabular-nums">
                      {domainsOf(p.principal.objectId) === null ? <Muted>Not read</Muted> : domainsOf(p.principal.objectId)
                        ? <TextLink href={`${hrefTo({ page: 'person', objectId: p.principal.objectId.toLowerCase() })}#person-data`} aria-label={`${domainsOf(p.principal.objectId)} data domains: open ${p.principal.displayLabel}`}>{domainsOf(p.principal.objectId)}</TextLink>
                        : <Muted>0</Muted>}
                    </TableCell>
                    <TableCell className="text-right text-sm text-navy tabular-nums">{p.pendingApprovals || <Muted>0</Muted>}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <MoreRowsNote skipToken={d.skipToken} what="owner assignments" />
        </>
      )}
    </Panel>
  );
}

function PersonBadges({ person, today }: { person: PersonGroup; today: string }) {
  return (
    <>
      {person.principal.principalKind === 'group' && <Badge variant="subtle"><UsersRound aria-hidden />Group</Badge>}
      <LeaveBadge holder={{ directoryState: person.directoryState, leavesOn: person.leavesOn }} today={today} />
    </>
  );
}

// ---------- by role ----------

function ByRole({ scope, text, ownerRoles, onPeople, onGaps }: {
  scope: ToolState<OwnerAssignmentList>;
  text: string;
  ownerRoles: Labels;
  onPeople: (role: string) => void;
  onGaps: (role: string) => void;
}) {
  const narrow = useNarrow();
  // The groups that hold each owner role in scope (wave 4): each links to its team page.
  const scopeData = dataOf(scope);
  const teams = useMemo(() => teamsByOwnerRole(scopeData?.items ?? []), [scopeData]);
  const matches = (c: OwnerCoverage) => matchesText(text, c.label, (teams.get(c.ownerRoleKey) ?? []).map(t => t.principal.displayLabel));
  const order = <T extends { ownerRoleKey: string }>(list: T[]): T[] => [...list].sort((a, b) => ownerRoles.rank(a.ownerRoleKey) - ownerRoles.rank(b.ownerRoleKey));
  const teamsOf = (key: string) => {
    const list = teams.get(key) ?? [];
    if (!list.length) return <Muted>No team</Muted>;
    return (
      <span className="flex flex-col gap-0.5">
        {list.map(t => (
          <span key={t.principal.objectId} className="text-sm">
            <TeamLink objectId={t.principal.objectId} label={t.principal.displayLabel} />
            <span className="text-xs text-navy-3">{`, ${roleTeamText(t)}`}</span>
          </span>
        ))}
      </span>
    );
  };
  return (
    <div className={twoColumnClass}>
      <Panel
        title="Coverage of the owner roles"
        state={scope}
        skeletonRows={8}
        subtitle="How many applications in scope have a holder and a deputy for each owner role, and the teams that hold it"
        actions={<TextLink href={teamsHref({ kind: 'support-team' })} className="text-sm">Support teams</TextLink>}
      >
        {d => {
          const all = order(d.coverage);
          const rows = all.filter(matches);
          if (!all.length) return <Empty>The configuration in force has no active owner role.</Empty>;
          if (!rows.length) return <Empty>{`No owner role or team matches "${text.trim()}".`}</Empty>;
          if (narrow) {
            return (
              <RowList label="Owner role coverage">
                {rows.map(c => (
                  <Row
                    key={c.ownerRoleKey}
                    primary={c.label}
                    secondary={<LinkButton onClick={() => onPeople(c.ownerRoleKey)}>Holder on {c.applicationsHeld} of {d.applicationsInScope}</LinkButton>}
                    tertiary={(
                      <span className="flex flex-col gap-1">
                        <span>{`Deputy on ${c.applicationsWithDeputy}. ${c.requiresDeputy ? 'A deputy is required.' : 'No deputy required.'}`}</span>
                        {(teams.get(c.ownerRoleKey) ?? []).length > 0 && <span>Teams: {teamsOf(c.ownerRoleKey)}</span>}
                      </span>
                    )}
                    end={c.gaps
                      ? <LinkButton onClick={() => onGaps(c.ownerRoleKey)} aria-label={`${c.gaps} gaps for ${c.label}: show them`}><ToneBadge tone="warning">{plural(c.gaps, 'gap', 'gaps')}</ToneBadge></LinkButton>
                      : <ToneBadge tone="success">No gaps</ToneBadge>}
                  />
                ))}
              </RowList>
            );
          }
          return (
            <Table aria-label="Owner role coverage">
              <TableHeader>
                <TableRow>
                  <TableHead>Owner role</TableHead>
                  <TableHead className="text-right">Holder on</TableHead>
                  <TableHead className="text-right">Deputy on</TableHead>
                  <TableHead>Deputy</TableHead>
                  <TableHead>Teams that hold it</TableHead>
                  <TableHead className="text-right">Gaps</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map(c => (
                  <TableRow key={c.ownerRoleKey}>
                    <TableCell className="text-sm font-medium text-navy">{c.label}</TableCell>
                    <TableCell className="text-right text-sm tabular-nums">
                      <LinkButton onClick={() => onPeople(c.ownerRoleKey)} aria-label={`${c.label}: holder on ${c.applicationsHeld} of ${d.applicationsInScope} applications. Show the holders.`}>
                        {c.applicationsHeld} of {d.applicationsInScope}
                      </LinkButton>
                    </TableCell>
                    <TableCell className="text-right text-sm text-navy tabular-nums">{c.applicationsWithDeputy}</TableCell>
                    <TableCell className="text-sm text-navy-2">{c.requiresDeputy ? 'Required' : 'Optional'}</TableCell>
                    <TableCell className="whitespace-normal">{teamsOf(c.ownerRoleKey)}</TableCell>
                    <TableCell className="text-right">
                      {c.gaps
                        ? <LinkButton onClick={() => onGaps(c.ownerRoleKey)} aria-label={`${c.gaps} gaps for ${c.label}: show them`}><ToneBadge tone="warning">{c.gaps}</ToneBadge></LinkButton>
                        : <ToneBadge tone="success">None</ToneBadge>}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          );
        }}
      </Panel>
      <Panel title="Gaps by owner role" state={scope} skeletonRows={6} notes={[]}>
        {d => {
          const items = order(d.coverage.filter(c => c.gaps > 0 && matches(c)));
          if (!items.length) return <Empty>{text.trim() ? `No owner role with gaps matches "${text.trim()}".` : 'Every owner role in scope has its holder and, where required, a deputy.'}</Empty>;
          return (
            <BarList
              ariaLabel="Owner gaps by owner role"
              noun={['gap', 'gaps']}
              labelWidth={150}
              items={items.map(c => ({ key: c.ownerRoleKey, label: c.label, count: c.gaps }))}
              onDrill={b => b.key && onGaps(b.key)}
            />
          );
        }}
      </Panel>
    </div>
  );
}

// The owner changes of an application that change this role, matched by the owner role key and deputy flag the
// decision log gives for each proposed change (product review: matching words of the statement hid "Propose a deputy"
// behind an unrelated technical owner change). deputy undefined matches the holder and the deputy changes alike.
function proposalsForRole(items: DecisionLogItem[], ownerRoleKey: string, deputy?: boolean, addsOnly = false): DecisionLogItem[] {
  return items.filter(d => (d.ownerChanges ?? []).some(c => c.ownerRoleKey === ownerRoleKey && (deputy === undefined || c.isDeputy === deputy) && (!addsOnly || c.change === 'add')));
}

// ---------- gaps ----------

type GapKind = 'all' | 'holder' | 'deputy' | 'disabled';

function Gaps(props: {
  gaps: ToolState<PortfolioSummary>;
  gapList: NonNullable<PortfolioSummary['ownerGapList']>;
  scopeItems: OwnerAssignmentItem[];
  upcomingItems: OwnerAssignmentItem[];
  text: string;
  role: string | undefined;
  applicationId: string | undefined;
  me: WhoAmI | null;
  mine: Set<string>;
  today: string;
  ownerRoles: Labels;
  onProposed: (p: Proposal) => void;
  // The owner changes waiting, by application (QA 2026-11-24: a gap kept offering "Propose a deputy" after one was
  // submitted, so a second decision was easy to make).
  inFlight: Map<string, DecisionLogItem[]>;
}) {
  const [kind, setKind] = useState<GapKind>('all');
  const { ownerRoles, role } = props;
  // One row per application and role: a no-holder gap and a no-deputy gap on the same role are one gap to fix.
  const seen = new Set<string>();
  // The search follows the role filter; the counts on the tabs follow both, never the tab.
  const missing = props.gapList.filter(g => !role || g.ownerRoleKey === role)
    .filter(g => matchesText(props.text, g.name, ownerRoles.label(g.ownerRoleKey), g.portfolio?.name)).filter(g => {
    const key = `${g.businessApplicationId.toLowerCase()}|${g.ownerRoleKey}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
  // A holder whose account the last owner check found disabled or deleted: the role is held on paper only. A role that
  // already shows a gap above is not listed twice.
  const disabled = offHolders(props.scopeItems, props.gapList).filter(a => !role || a.ownerRoleKey === role)
    .filter(a => matchesText(props.text, a.applicationName, a.label, a.principal.displayLabel));
  const shownMissing = missing.filter(g => kind === 'all' || g.missing === kind);
  const shownDisabled = kind === 'all' || kind === 'disabled' ? disabled : [];
  const count = shownMissing.length + shownDisabled.length;
  const counts = {
    holder: missing.filter(g => g.missing === 'holder').length,
    deputy: missing.filter(g => g.missing === 'deputy').length,
    disabled: disabled.length,
  };

  return (
    <Panel
      title="Owner gaps"
      count={props.gaps.status === 'done' ? count : undefined}
      state={props.gaps}
      skeletonRows={6}
      subtitle="An owner role with no holder, a required deputy missing, or a holder whose account is off. A gap closes when an ownership decision is accepted."
    >
      {d => (
        <>
          <SwitchTabs
            label="Show"
            ariaLabel="Kind of gap"
            value={kind}
            onChange={setKind}
            options={[
              { value: 'all', label: 'All' },
              { value: 'holder', label: `No holder (${counts.holder})` },
              { value: 'deputy', label: `No deputy (${counts.deputy})` },
              { value: 'disabled', label: `Holder disabled (${counts.disabled})` },
            ]}
          />
          {!count ? (
            <Empty>{props.text.trim() ? `No owner gap matches "${props.text.trim()}".` : kind === 'all' ? 'No owner gap in this scope.' : 'No gap of this kind in this scope.'}</Empty>
          ) : (
            <RowList label="Owner gaps">
              {shownMissing.map(g => {
                const waiting = proposalsForRole(props.inFlight.get(g.businessApplicationId.toLowerCase()) ?? [], g.ownerRoleKey, g.missing === 'deputy', true)[0] ?? null;
                const allowed = !waiting && canProposeOwnerChange(props.me, g.businessApplicationId, props.mine);
                return (
                  <Row
                    key={`${g.businessApplicationId}-${g.ownerRoleKey}-${g.missing}`}
                    wideEnd
                    lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" aria-hidden /></span>}
                    primary={<TextLink href={hrefTo({ page: 'app', id: g.businessApplicationId.toLowerCase() })}>{g.name}</TextLink>}
                    secondary={`${ownerRoles.label(g.ownerRoleKey)}: ${g.missing === 'holder' ? 'no holder' : 'no deputy'}`}
                    tertiary={g.portfolio?.name}
                    badges={(
                      <>
                        <ToneBadge tone="warning">{g.missing === 'holder' ? 'No holder' : 'No deputy'}</ToneBadge>
                        {(g.holderDirectoryState === 'disabled' || g.holderDirectoryState === 'deleted') && <ToneBadge tone="danger">Holder's account is {g.holderDirectoryState}</ToneBadge>}
                      </>
                    )}
                    end={waiting ? (
                      <TextLink href={hrefTo({ page: 'decision', id: waiting.decisionId.toLowerCase() })}>Owner change waiting</TextLink>
                    ) : allowed ? (
                      <ProposeOwnerChangeSheet
                        application={{ id: g.businessApplicationId, name: g.name }}
                        mode="add"
                        ownerRoleKey={g.ownerRoleKey}
                        isDeputy={g.missing === 'deputy'}
                        me={props.me}
                        onProposed={props.onProposed}
                        trigger={<Button size="sm" variant="outline" aria-label={`Propose a ${g.missing} for ${ownerRoles.label(g.ownerRoleKey)} of ${g.name}`}>Propose a {g.missing}</Button>}
                      />
                    ) : undefined}
                  />
                );
              })}
              {shownDisabled.map(a => {
                const allowed = canProposeOwnerChange(props.me, a.businessApplicationId, props.mine);
                const deputy = deputyOf(props.scopeItems, a.businessApplicationId, a.ownerRoleKey, a.principal.objectId);
                const successor = namedSuccessorOf(props.upcomingItems, a);
                return (
                  <Row
                    key={a.ownerAssignmentId}
                    wideEnd
                    lead={<PersonAvatar principal={a.principal} />}
                    primary={<TextLink href={hrefTo({ page: 'app', id: a.businessApplicationId.toLowerCase() })}>{a.applicationName}</TextLink>}
                    secondary={<>{a.label}: <PersonLink principal={a.principal} /></>}
                    tertiary={successor ? `${successor.principal.displayLabel} takes over${successor.startOn ? ` on ${fmtDate(successor.startOn)}` : ''}, named by an accepted owner change.` : deputy ? `${deputy.principal.displayLabel} is the deputy.` : 'No deputy.'}
                    badges={<LeaveBadge holder={a.holder} today={props.today} />}
                    end={successor ? <ToneBadge tone="success">Successor named</ToneBadge> : allowed ? (
                      <ProposeOwnerChangeSheet
                        application={{ id: a.businessApplicationId, name: a.applicationName }}
                        mode="replace"
                        ownerRoleKey={a.ownerRoleKey}
                        ending={{ assignmentId: a.ownerAssignmentId, principal: a.principal, isDeputy: false }}
                        suggested={deputy?.principal ?? null}
                        me={props.me}
                        onProposed={props.onProposed}
                        trigger={<Button size="sm" variant="outline" aria-label={`Propose a successor to ${a.principal.displayLabel} as ${a.label} of ${a.applicationName}`}>Propose a successor</Button>}
                      />
                    ) : undefined}
                  />
                );
              })}
            </RowList>
          )}
          <MoreRowsNote skipToken={d.skipToken} what="owner gaps" />
        </>
      )}
    </Panel>
  );
}

// ---------- leavers ----------

function Leavers(props: {
  leavers: ToolState<OwnerAssignmentList>;
  text: string;
  scopeItems: OwnerAssignmentItem[];
  upcomingItems: OwnerAssignmentItem[];
  ownerChecks: ToolState<ReviewList>;
  proposals: ToolState<DecisionLog>;
  me: WhoAmI | null;
  mine: Set<string>;
  today: string;
  onProposed: (p: Proposal) => void;
  // The platform admin runs the owner check (run_owner_check) from here as from the Gap check page.
  canCheck: boolean;
}) {
  const data = dataOf(props.leavers);
  const people = useMemo(
    () => sortPeople([...filterByText(groupByPerson(data?.items ?? []), props.text, p => [p.principal.displayLabel, p.assignments.map(a => a.applicationName)])], 'leaving'),
    [data, props.text],
  );
  const checks = dataOf(props.ownerChecks)?.items ?? [];
  const inFlight = proposalsByApplication(dataOf(props.proposals)?.items ?? []);
  return (
    <Panel
      title="Leavers"
      count={data ? people.length : undefined}
      state={props.leavers}
      skeletonRows={6}
      subtitle="Holders with a recorded leave date, or whose account the last owner check found disabled or deleted. Each owner role they hold needs a successor through an ownership decision."
      actions={props.canCheck ? <OwnerCheckSheet trigger={<Button size="sm" variant="outline">Check the owners</Button>} /> : undefined}
    >
      {d => (
        <>
          {!people.length && <Empty>{props.text.trim() ? `No leaver matches "${props.text.trim()}".` : "Nobody who holds an owner role in this scope is leaving, and no holder's account is off."}</Empty>}
          {people.map((p, i) => (
            <LeaverBlock
              key={p.principal.objectId}
              person={p}
              first={i === 0}
              scopeItems={props.scopeItems}
              upcomingItems={props.upcomingItems}
              checks={checks}
              inFlight={inFlight}
              me={props.me}
              mine={props.mine}
              today={props.today}
              onProposed={props.onProposed}
            />
          ))}
          <MoreRowsNote skipToken={d.skipToken} what="leavers' owner assignments" />
          <Muted>
            Directory states come from the last owner check, which the platform admin runs with Check the owners. Open
            approvals do not move to a deputy by themselves yet: a successor is asked once the ownership decision is
            accepted.
          </Muted>
        </>
      )}
    </Panel>
  );
}

function LeaverBlock(props: {
  person: PersonGroup;
  first: boolean;
  scopeItems: OwnerAssignmentItem[];
  upcomingItems: OwnerAssignmentItem[];
  checks: ReviewItem[];
  inFlight: Map<string, DecisionLogItem[]>;
  me: WhoAmI | null;
  mine: Set<string>;
  today: string;
  onProposed: (p: Proposal) => void;
}) {
  const p = props.person;
  const appIds = p.assignments.map(a => a.businessApplicationId);
  const allowedLeave = canRecordLeave(props.me, p.principal.objectId, appIds, props.mine);
  return (
    <div className={props.first ? 'flex flex-col gap-2' : 'flex flex-col gap-2 border-t border-line-strong pt-4'}>
      <div className="flex flex-wrap items-start gap-x-3 gap-y-2">
        <PersonAvatar principal={p.principal} />
        <div className="flex min-w-[220px] flex-1 flex-col gap-1">
          <h3 className="m-0 text-sm font-semibold text-navy"><PersonLink principal={p.principal} /></h3>
          <div className="flex flex-wrap gap-1"><LeaveBadge holder={{ directoryState: p.directoryState, leavesOn: p.leavesOn }} today={props.today} /></div>
          <Muted>
            {/* Two counts of roles, then the applications they sit on (QA walk: "on 12 applications, deputy on 7" read as
                two counts of applications). */}
            Holds {plural(p.holds, 'owner role', 'owner roles')}{p.deputyFor ? ` and ${plural(p.deputyFor, 'deputy role', 'deputy roles')}` : ''} on {plural(p.applications, 'application', 'applications')}.
            {p.pendingApprovals ? ` ${plural(p.pendingApprovals, 'pending approval asks', 'pending approvals ask')} the roles they hold.` : ''}
          </Muted>
        </div>
        {allowedLeave && (
          <RecordLeaveSheet
            person={p.principal}
            currentLeavesOn={p.leavesOn}
            trigger={<Button size="sm" variant="outline" aria-label={`${p.leavesOn ? 'Change the leave date of' : 'Record a leave for'} ${p.principal.displayLabel}`}>{p.leavesOn ? 'Change the leave date' : 'Record a leave'}</Button>}
          />
        )}
      </div>
      <RowList label={`Owner roles of ${p.principal.displayLabel}`}>
        {p.assignments.map(a => (
          <LeaverAssignment
            key={a.ownerAssignmentId}
            assignment={a}
            scopeItems={props.scopeItems}
            upcomingItems={props.upcomingItems}
            checks={props.checks}
            inFlight={props.inFlight}
            me={props.me}
            mine={props.mine}
            onProposed={props.onProposed}
          />
        ))}
      </RowList>
    </div>
  );
}

function LeaverAssignment(props: {
  assignment: OwnerAssignmentItem;
  scopeItems: OwnerAssignmentItem[];
  upcomingItems: OwnerAssignmentItem[];
  checks: ReviewItem[];
  inFlight: Map<string, DecisionLogItem[]>;
  me: WhoAmI | null;
  mine: Set<string>;
  onProposed: (p: Proposal) => void;
}) {
  const a = props.assignment;
  const deputy = a.isDeputy ? null : deputyOf(props.scopeItems, a.businessApplicationId, a.ownerRoleKey, a.principal.objectId);
  const check = props.checks.find(r => sameId(r.businessApplicationId, a.businessApplicationId));
  const proposals = proposalsForRole(props.inFlight.get(a.businessApplicationId.toLowerCase()) ?? [], a.ownerRoleKey);
  const allowed = canProposeOwnerChange(props.me, a.businessApplicationId, props.mine);
  // An accepted owner change already named who takes over: the row says so and offers no other proposal.
  const successor = namedSuccessorOf(props.upcomingItems, a);
  const lines: ReactNode[] = [];
  if (successor) {
    lines.push(
      <span key="successor">
        Successor: <PersonLink principal={successor.principal} />{successor.startOn ? ` from ${fmtDate(successor.startOn)}` : ''}, named by an accepted owner change.
      </span>,
    );
  } else if (!a.isDeputy) {
    lines.push(deputy
      ? <span key="deputy">Deputy: <PersonLink principal={deputy.principal} />, the natural successor.</span>
      : <span key="deputy" className="font-semibold text-gold-strong">No deputy: the role has nobody once they leave.</span>);
  }
  if (a.pendingApprovalCount) {
    lines.push(
      <span key="approvals">
        <TextLink href={hrefTo({ page: 'approvals', view: 'open', applicationId: a.businessApplicationId.toLowerCase() })}>
          {plural(a.pendingApprovalCount, 'pending approval', 'pending approvals')}
        </TextLink>{' '}
        {a.pendingApprovalCount === 1 ? 'asks' : 'ask'} this owner role.
      </span>,
    );
  }
  if (check) {
    lines.push(
      <span key="check">
        <TextLink href={hrefTo({ page: 'review', id: check.reviewId.toLowerCase() })}>{check.reviewType.label}</TextLink>
        {check.dueOn ? ` due ${fmtDate(check.dueOn)}` : ' due'}.
      </span>,
    );
  }
  for (const d of proposals) {
    lines.push(
      <span key={d.decisionId} className="flex flex-wrap items-center gap-1.5">
        <TextLink href={hrefTo({ page: 'decision', id: d.decisionId.toLowerCase() })}>Owner change proposed</TextLink>
        <StatusBadge status={d.status} />
        <span>{approvalsText(d)}.</span>
      </span>,
    );
  }
  return (
    <Row
      wideEnd
      primary={(
        <>
          <TextLink href={hrefTo({ page: 'app', id: a.businessApplicationId.toLowerCase() })}>{a.applicationName}</TextLink>
          <span className="font-normal text-navy-2">, {a.label}{a.isDeputy ? ' (deputy)' : ''}</span>
        </>
      )}
      secondary={lines.length ? <span className="flex flex-col gap-1">{lines}</span> : undefined}
      end={successor ? <ToneBadge tone="success">Successor named</ToneBadge> : allowed ? (
        <ProposeOwnerChangeSheet
          application={{ id: a.businessApplicationId, name: a.applicationName }}
          mode="replace"
          ownerRoleKey={a.ownerRoleKey}
          ending={{ assignmentId: a.ownerAssignmentId, principal: a.principal, isDeputy: a.isDeputy }}
          suggested={deputy?.principal ?? null}
          me={props.me}
          onProposed={props.onProposed}
          trigger={<Button size="sm" variant="outline" aria-label={`Propose a successor for ${a.label} of ${a.applicationName}`}>Propose a successor</Button>}
        />
      ) : undefined}
    />
  );
}

// =====================================================================================================================
// One person: /app/people/<objectId>
// =====================================================================================================================

export function PersonPage({ objectId }: { objectId: string }) {
  const host = useHost();
  const today = host.today;
  const ownerRoles = useLabels('owner-role');
  const { me } = useWhoAmI();
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const all = useAllPages('list_owner_assignments', { principalObjectId: objectId, state: 'all', top: 100 });
  const data = dataOf(all);
  const items = useMemo(() => data?.items ?? [], [data]);
  const current = items.filter(a => a.state === 'current');
  const upcoming = items.filter(a => a.state === 'upcoming');
  const ended = items.filter(a => a.state === 'ended');
  const self = sameId(me?.principal.objectId, objectId);
  const principal = items[0]?.principal ?? (self && me ? { objectId: me.principal.objectId, displayLabel: me.principal.displayLabel } : null);
  const name = principal?.displayLabel || 'Person';
  const holder = items[0]?.holder ?? null;
  const reason = holder ? leaveReason(holder) : null;
  const appIds = [...new Set(current.map(a => a.businessApplicationId.toLowerCase()))];
  const holdsAny = current.length > 0;

  const mineState = useAllPages('list_owner_assignments', me && !self ? { principalObjectId: me.principal.objectId, state: 'current', top: 100 } : null);
  const mine = useMemo(() => ownedApplicationIds(self ? current : dataOf(mineState)?.items ?? []), [self, current, mineState]);
  // Every current owner row, for the deputies who would succeed this person. Read only when they hold a role.
  const everyone = useAllPages('list_owner_assignments', holdsAny ? { state: 'current', top: 100 } : null);
  const everyoneItems = dataOf(everyone)?.items ?? [];
  const gaps = useTool('get_portfolio_summary', holdsAny ? { groupBy: 'portfolio', includeOwnerGaps: true, top: 100 } : null);
  const proposals = useTool('get_decision_log', holdsAny ? { decisionTypeKeys: ['ownership'], statusMeanings: ['proposed'], top: 100 } : null);
  const myApprovals = useTool('list_approvals', self ? { view: 'mine', top: 5 } : null);
  // The teams the person is in (wave 4): list_teams by memberObjectId. A group's own page is its team page instead.
  const isGroup = principal?.principalKind === 'group';
  const teams = useRead('list_teams', data && !isGroup ? { memberObjectId: objectId, top: 50 } : null);
  // The data domains the person owns (product review, fix round 1: "owners of the apps and data"): data owner or deputy, their
  // own or inherited, with the classification and the gaps.
  const domains = useRead('find_data_domains', { ownerObjectId: objectId, top: 100 });
  // A leave date is read by the platform admin and by the person; anyone else is told it is not shown to their role
  // (product review, fix round 1: a reader saw "None recorded" for a recorded leave).
  const readsLeave = self || holdsRole(me, 'platform-admin');

  const gapRows = (dataOf(gaps)?.ownerGapList ?? []).filter(g => appIds.includes(g.businessApplicationId.toLowerCase()));
  const inFlight = (dataOf(proposals)?.items ?? []).filter(d => d.subjectKind === 'business-application' && appIds.includes(d.subjectId.toLowerCase()));
  const allowedLeave = principal ? canRecordLeave(me, objectId, appIds, mine) : false;
  const holds = current.filter(a => !a.isDeputy).length;
  const deputyFor = current.length - holds;
  const pending = current.filter(a => !a.isDeputy).reduce((n, a) => n + a.pendingApprovalCount, 0);
  const gapsTitle = self ? 'Gaps on your applications' : 'Gaps on their applications';

  // The person's current rows by application, in name order.
  const byApp = useMemo(() => {
    const map = new Map<string, OwnerAssignmentItem[]>();
    for (const a of current) map.set(a.businessApplicationId.toLowerCase(), [...(map.get(a.businessApplicationId.toLowerCase()) ?? []), a]);
    return [...map.values()].sort((x, y) => x[0].applicationName.localeCompare(y[0].applicationName));
  }, [current]);

  return (
    <>
      <PageHeader
        eyebrow="Person"
        title={name}
        subtitle={`What ${self ? 'you own' : `${name} owns`} now, the gaps on those applications, owner changes in flight and any leave.`}
        meta={<DataStamp envelope={all.status === 'done' ? all.envelope : undefined} />}
        actions={(allowedLeave && principal) || isGroup ? (
          <>
            {isGroup && <TextLink href={hrefTo({ page: 'team', id: objectId.toLowerCase() })} className="text-sm">Open its team page</TextLink>}
            {allowedLeave && principal && (
              <RecordLeaveSheet
                person={principal}
                currentLeavesOn={holder?.leavesOn ?? null}
                trigger={<Button variant="outline">{holder?.leavesOn ? 'Change the leave date' : 'Record a leave'}</Button>}
              />
            )}
          </>
        ) : undefined}
      />
      {(self || reason || principal?.principalKind === 'group') && (
        <div className="-mt-3 mb-4 flex flex-wrap gap-1.5">
          {self && <Badge variant="brand">This is you</Badge>}
          {principal?.principalKind === 'group' && <Badge variant="subtle"><UsersRound aria-hidden />Group</Badge>}
          {holder && <LeaveBadge holder={holder} today={today} />}
        </div>
      )}

      <ProposalBanner key={proposal?.decisionId ?? 'none'} proposal={proposal} onDismiss={() => setProposal(null)} />

      <div className={kpiRowClass} role="group" aria-label="Key figures">
        <KpiTile label="Owner roles now" value={data ? holds : null} caption={`On ${plural(appIds.length, 'application', 'applications')}`} onClick={() => scrollToPanel('person-owns')} />
        <KpiTile label="Deputy roles" value={data ? deputyFor : null} caption="Stands in for a holder" onClick={() => scrollToPanel('person-owns')} />
        <KpiTile label={self ? 'Gaps on your applications' : 'Gaps on their applications'} value={holdsAny ? (dataOf(gaps) ? gapRows.length : null) : data ? 0 : null} caption="No holder or no deputy" attention={gapRows.length > 0} onClick={() => scrollToPanel('person-gaps')} />
        <KpiTile label={self ? 'Approvals on your roles' : 'Approvals on their roles'} value={data ? pending : null} caption={self ? 'Pending, would move if you leave' : 'Pending, would move if they leave'} onClick={() => scrollToPanel('person-owns')} />
        <KpiTile label="Data domains" value={dataOf(domains) ? dataOf(domains)!.items.length : null} failed={readFailed(domains)} caption={self ? 'You own, or stand in for' : 'Owned, or stood in for'} onClick={() => scrollToPanel('person-data')} />
      </div>

      <div className={twoColumnClass}>
        <Panel
          title="Owns now"
          id="person-owns"
          state={all}
          skeletonRows={6}
          count={data ? current.length : undefined}
          subtitle="Current owner assignments, by application"
          actions={<TextLink href={hrefTo({ page: 'applications', filter: { ownerObjectId: objectId.toLowerCase() } })} className="text-sm">In the register</TextLink>}
        >
          {d => (
            <>
              {!byApp.length && (
                <Empty>
                  {self ? 'You hold' : `${name} holds`} no current owner assignment you can see.
                  {items.length ? ' Earlier or coming assignments are listed below.' : ''}
                </Empty>
              )}
              {byApp.map(rows => (
                <div key={rows[0].businessApplicationId} className="flex flex-col">
                  <h3 className={groupTitleClass}>
                    <TextLink href={hrefTo({ page: 'app', id: rows[0].businessApplicationId.toLowerCase() })} className="text-sm tracking-normal normal-case">{rows[0].applicationName}</TextLink>
                  </h3>
                  <RowList label={`Owner roles on ${rows[0].applicationName}`}>
                    {rows.map(a => (
                      <PersonAssignment
                        key={a.ownerAssignmentId}
                        assignment={a}
                        everyone={everyoneItems}
                        me={me}
                        mine={mine}
                        onProposed={setProposal}
                      />
                    ))}
                  </RowList>
                </div>
              ))}
              <MoreRowsNote skipToken={d.skipToken} what="owner assignments" />
            </>
          )}
        </Panel>

        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          {!isGroup && <MemberOf teams={teams} self={self} name={name} />}
          <Panel title="Leave and account" state={all} skeletonRows={3} notes={[]}>
            {() => (
              <>
                <dl className={facts2Class}>
                  <Fact label="Leave date">{holder?.leavesOn ? fmtDate(holder.leavesOn) : readsLeave ? 'None recorded' : 'Not shown to your role'}</Fact>
                  <Fact label="Entra account">
                    {directoryText(holder?.directoryState ?? null)}
                    {holder?.directoryStateOn ? <Muted className="block">As of {fmtDate(holder.directoryStateOn)}</Muted> : null}
                  </Fact>
                </dl>
                {reason && (
                  <Muted>
                    Each owner role {self ? 'you hold' : 'they hold'} needs a successor through an ownership decision. Open
                    approvals do not move by themselves yet.
                  </Muted>
                )}
              </>
            )}
          </Panel>

          <PersonDataDomains state={domains} self={self} name={name} objectId={objectId} />

          {!data ? (
            <Panel title={gapsTitle} id="person-gaps" state={all} skeletonRows={3} notes={[]}>{() => null}</Panel>
          ) : !holdsAny ? (
            <Panel title={gapsTitle} id="person-gaps">
              <Empty>No application to check: {self ? 'you hold' : 'they hold'} no owner role now.</Empty>
            </Panel>
          ) : (
            <Panel title={gapsTitle} id="person-gaps" state={gaps} skeletonRows={3} count={dataOf(gaps) ? gapRows.length : undefined} notes={[]}>
              {() => (!gapRows.length ? (
                <Empty>Every owner role on {self ? 'your' : 'their'} applications has its holder and, where required, a deputy.</Empty>
              ) : (
                <RowList label="Owner gaps">
                  {gapRows.map(g => (
                    <Row
                      key={`${g.businessApplicationId}-${g.ownerRoleKey}-${g.missing}`}
                      wideEnd
                      primary={<TextLink href={hrefTo({ page: 'app', id: g.businessApplicationId.toLowerCase() })}>{g.name}</TextLink>}
                      secondary={`${ownerRoles.label(g.ownerRoleKey)}: ${g.missing === 'holder' ? 'no holder' : 'no deputy'}`}
                      end={canProposeOwnerChange(me, g.businessApplicationId, mine) ? (
                        <ProposeOwnerChangeSheet
                          application={{ id: g.businessApplicationId, name: g.name }}
                          mode="add"
                          ownerRoleKey={g.ownerRoleKey}
                          isDeputy={g.missing === 'deputy'}
                          me={me}
                          onProposed={setProposal}
                          trigger={<Button size="sm" variant="outline" aria-label={`Propose a ${g.missing} for ${ownerRoles.label(g.ownerRoleKey)} of ${g.name}`}>Propose a {g.missing}</Button>}
                        />
                      ) : <ToneBadge tone="warning">{g.missing === 'holder' ? 'No holder' : 'No deputy'}</ToneBadge>}
                    />
                  ))}
                </RowList>
              ))}
            </Panel>
          )}

          {data && holdsAny && (
            <Panel title="Owner changes in flight" id="person-changes" state={proposals} skeletonRows={2} count={dataOf(proposals) ? inFlight.length : undefined}>
              {() => (!inFlight.length ? <Empty>No ownership decision is waiting on {self ? 'your' : 'their'} applications.</Empty> : (
                <RowList label="Ownership decisions in flight">
                  {inFlight.map(dec => (
                    <Row
                      key={dec.decisionId}
                      primary={<TextLink href={hrefTo({ page: 'decision', id: dec.decisionId.toLowerCase() })}>{dec.statement}</TextLink>}
                      secondary={`${approvalsText(dec)}${dec.proposedBy ? `. Proposed by ${dec.proposedBy.displayLabel}` : ''}.`}
                      end={<StatusBadge status={dec.status} />}
                    />
                  ))}
                </RowList>
              ))}
            </Panel>
          )}
        </div>
      </div>

      {self && (
        <div className="mb-4">
          <Panel
            title="Approvals asked of you"
            state={myApprovals}
            skeletonRows={3}
            count={dataOf(myApprovals)?.totalCount}
            actions={<TextLink href={hrefTo({ page: 'approvals', view: 'mine' })} className="text-sm">All approvals asked of you</TextLink>}
          >
            {d => (!d.items.length ? <Empty>Nothing waits for your answer.</Empty> : (
              <RowList label="Approvals asked of you">
                {d.items.map(item => (
                  <Row
                    key={item.approvalRecordId}
                    primary={<TextLink href={hrefTo({ page: 'decision', id: item.decision.decisionId.toLowerCase() })}>{item.decision.statement}</TextLink>}
                    secondary={`${item.decision.decisionType.label}: ${item.subject.label}${item.requestedOn ? `. Asked ${fmtDate(item.requestedOn)}` : ''}${item.expiresOn ? `. Expires ${fmtDate(item.expiresOn)}` : ''}.`}
                    // Wave 4b verify round: a request a separation rule keeps the person from answering says so, as on Approvals.
                    tertiary={item.nextTool !== 'answer_approval' && item.cannotAnswer ? 'A separation rule keeps you from answering. Another person who is asked answers it.' : undefined}
                    end={<StatusBadge status={item.outcome} />}
                  />
                ))}
              </RowList>
            ))}
          </Panel>
        </div>
      )}

      <Panel title="Coming and ended assignments" id="person-history" state={all} skeletonRows={3} count={data ? upcoming.length + ended.length : undefined} notes={[]}>
        {() => (!upcoming.length && !ended.length ? <Empty>No coming or ended owner assignment.</Empty> : (
          <RowList label="Coming and ended assignments">
            {[...upcoming, ...ended].map(a => (
              <Row
                key={a.ownerAssignmentId}
                primary={<TextLink href={hrefTo({ page: 'app', id: a.businessApplicationId.toLowerCase() })}>{a.applicationName}</TextLink>}
                secondary={`${a.label}${a.isDeputy ? ', deputy' : ''}`}
                end={<ToneBadge tone={a.state === 'upcoming' ? 'informative' : 'subtle'}>{a.state === 'upcoming' ? 'Coming' : 'Ended'}</ToneBadge>}
                endSub={a.state === 'upcoming' ? fmtDate(a.startOn, 'once accepted') : fmtDate(a.endOn, 'date not recorded')}
              />
            ))}
          </RowList>
        ))}
      </Panel>
    </>
  );
}

function directoryText(state: OwnerAssignmentItem['holder']['directoryState']): string {
  switch (state) {
    case 'enabled':
      return 'Enabled';
    case 'disabled':
      return 'Disabled';
    case 'deleted':
      return 'Deleted';
    case 'unknown':
      return 'Not known';
    default:
      return 'Not checked yet';
  }
}

// The teams a person is in (list_teams memberObjectId), each linking to its team page. With no directory reads the
// product cannot say, and the panel says membership is managed in Entra.
function MemberOf({ teams, self, name }: { teams: ToolState<TeamList>; self: boolean; name: string }) {
  const data = dataOf(teams);
  return (
    <Panel
      title="Member of"
      id="person-teams"
      state={teams}
      skeletonRows={2}
      count={data && data.membersReadFrom !== 'not-read' ? data.totalCount : undefined}
      subtitle="The teams and committees the product knows that this person is in"
      actions={<TextLink href={teamsHref()} className="text-sm">All teams</TextLink>}
      notes={[]}
    >
      {d => (d.membersReadFrom === 'not-read' ? (
        <Empty>Membership is managed in Entra. This install does not read group members, so it cannot list the teams {self ? 'you are' : `${name} is`} in.</Empty>
      ) : !d.items.length ? (
        <Empty>{self ? 'You are' : `${name} is`} in no team the product knows{d.membersReadFrom === 'demo-personas' ? ', among the demo groups' : ''}.</Empty>
      ) : (
        <RowList label="Teams this person is in">
          {d.items.map(t => (
            <Row
              key={t.teamId}
              lead={<LeadIcon tone="brand"><UsersRound /></LeadIcon>}
              primary={<TeamLink objectId={t.teamId} label={t.team.displayLabel} />}
              secondary={t.roles.length ? `Holds ${t.roles.map(r => r.label).join(', ')}` : 'Holds no product role'}
              badges={<KindBadges kinds={t.kinds} />}
              end={t.waitingCount
                ? <TextLink href={teamSectionHref(t.teamId, 'waiting')} className="text-xs" aria-label={`${plural(t.waitingCount, 'request waits', 'requests wait')} on ${t.team.displayLabel}. Open them.`}>{plural(t.waitingCount, 'request waits', 'requests wait')}</TextLink>
                : undefined}
            />
          ))}
        </RowList>
      ))}
    </Panel>
  );
}

function PersonAssignment(props: {
  assignment: OwnerAssignmentItem;
  everyone: OwnerAssignmentItem[];
  me: WhoAmI | null;
  mine: Set<string>;
  onProposed: (p: Proposal) => void;
}) {
  const a = props.assignment;
  const deputy = a.isDeputy ? null : deputyOf(props.everyone, a.businessApplicationId, a.ownerRoleKey, a.principal.objectId);
  const allowed = canProposeOwnerChange(props.me, a.businessApplicationId, props.mine);
  const facts = [
    a.startOn ? `Since ${fmtDate(a.startOn)}` : 'Start not recorded',
    a.pendingApprovalCount && !a.isDeputy ? plural(a.pendingApprovalCount, 'pending approval asks this role', 'pending approvals ask this role') : null,
  ].filter(Boolean).join('. ');
  return (
    <Row
      wideEnd
      primary={`${a.label}${a.isDeputy ? ', deputy' : ''}`}
      secondary={`${facts}.`}
      tertiary={a.isDeputy ? undefined : deputy ? <>Deputy: <PersonLink principal={deputy.principal} /></> : props.everyone.length ? 'No deputy.' : undefined}
      end={allowed ? (
        <ProposeOwnerChangeSheet
          application={{ id: a.businessApplicationId, name: a.applicationName }}
          mode="replace"
          ownerRoleKey={a.ownerRoleKey}
          ending={{ assignmentId: a.ownerAssignmentId, principal: a.principal, isDeputy: a.isDeputy }}
          suggested={deputy?.principal ?? null}
          me={props.me}
          onProposed={props.onProposed}
          trigger={<Button size="sm" variant="outline" aria-label={`Hand over ${a.label} of ${a.applicationName}`}>Hand over</Button>}
        />
      ) : undefined}
    />
  );
}

// The main holders whose account is off, once per application and role, leaving out a role the gap list already names
// (document 24, B24-17: one owner gap per application and role).
function offHolders(items: OwnerAssignmentItem[], gapList: NonNullable<PortfolioSummary['ownerGapList']>): OwnerAssignmentItem[] {
  const key = (app: string, role: string) => `${app.toLowerCase()}|${role}`;
  const seen = new Set(gapList.map(g => key(g.businessApplicationId, g.ownerRoleKey)));
  return items.filter(a => {
    if (a.isDeputy || (a.holder.directoryState !== 'disabled' && a.holder.directoryState !== 'deleted')) return false;
    const k = key(a.businessApplicationId, a.ownerRoleKey);
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}

// The data domains a person owns, deputizes or stewards, their own or inherited (product review, fix round 1), from
// find_data_domains with ownerObjectId: the path, the role, the classification and the gaps, each linking to the domain.
function PersonDataDomains({ state, self, name, objectId }: { state: ToolState<DataDomainList>; self: boolean; name: string; objectId: string }) {
  return (
    <Panel
      title="Data it owns"
      id="person-data"
      state={state}
      skeletonRows={2}
      count={dataOf(state)?.items.length}
      subtitle={self ? 'The data domains you own, stand in for or steward' : `The data domains ${name} owns, stands in for or stewards`}
      notes={[]}
    >
      {d => (!d.items.length ? <Empty>{self ? 'You own' : `${name} owns`} no data domain, and stands in for none.</Empty> : (
        <RowList label="Data domains owned">
          {d.items.map(item => {
            const inherited = (v: { inheritedFrom: { label: string } | null } | null) => (v?.inheritedFrom ? `, inherited from ${v.inheritedFrom.label}` : ', its own');
            const role = sameId(item.owner?.principal.objectId, objectId) ? `Data owner${inherited(item.owner)}`
              : sameId(item.deputy?.objectId, objectId) ? 'Deputy data owner'
              : sameId(item.steward?.principal.objectId, objectId) ? `Data steward${inherited(item.steward)}`
              : 'Data owner';
            return (
              <Row
                key={item.key}
                primary={<TextLink href={recordHref(item.recordUrl) ?? recordHrefOf('data-domain', item.key) ?? '#'}>{item.path}</TextLink>}
                secondary={`${role}${item.classification ? `. ${item.classification.label}` : ''}`}
                badges={item.gaps.length ? <ToneBadge tone="warning">{plural(item.gaps.length, 'gap', 'gaps')}</ToneBadge> : undefined}
              />
            );
          })}
        </RowList>
      ))}
    </Panel>
  );
}
