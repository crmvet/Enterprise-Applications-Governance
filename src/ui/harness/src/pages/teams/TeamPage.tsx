// One team (/app/teams/<objectId>; the record link /app/records/team/<objectId> opens it too).
// ?at=members|roles|decisions|waiting|applications|data|gaps|answers opens it at a section:
// the Teams list's figures link there.
// Reads: get_team (tool 96): the group, its kinds, its members (Entra live, the demo personas, or not read), the roles
// it holds with what each decides, the decision types it answers, the applications and data it holds owner roles on,
// what waits on it now, the latest answers its people gave through it, and the owner gaps on its applications.
// No writes here: a person answers a request on its decision page (D-38), proposes an owner on Owners, and a binding on
// Roles and access. Each place is one link away.
// Choices (D-41):
//   - Every figure is a count get_team returns, and opens the section with its rows.
//   - Members link to their person page. A member that is not a person (a nested group, an agent identity) says what it
//     is and links nowhere: the product holds no team page for a group it does not know.
//   - With no directory reads the page says membership is managed in Entra, in the read's own sentence; when Entra did
//     not answer it says so and offers to read again.
//   - A list longer than eight rows gets its own search box, kept in the address (member=, waiting=, application=).
//   - The group's own owner assignments, with the ended ones and the Hand over action, stay on its person page; the
//     Applications section links there.
import { BookOpenCheck, CircleSlash, Clock, Database, LayoutGrid, MessageSquareReply, UserRoundX } from 'lucide-react';
import { useEffect, useMemo, useRef, type ReactNode } from 'react';
import { hrefTo, recordHref, recordHrefOf, teamsHref, useQueryParam, withParams } from '../../app/links';
import { DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ListSearch, useListText } from '../../components/listSearch';
import { Row, RowList } from '../../components/rows';
import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import type { Envelope, PrincipalRef, TeamAsk, TeamDetail } from '../../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { filterByText } from '../../data/textSearch';
import { dataOf, useRefresh } from '../../data/toolClient';
import { BINDING_METHOD_TEXT } from '../admin/logic';
import { LeadIcon } from '../ai-audit/parts';
import { PersonAvatar, scrollToPanel } from '../owners-reviews/people';
import { GuidelineMark } from '../register-extras/parts';
import { HolderLink } from './links';
import {
  answerText,
  approvalText,
  askedText,
  assignmentsText,
  DATA_SUBJECT_WORDS,
  decisionWords,
  expiry,
  membersFigure,
  PRINCIPAL_KIND_WORDS,
  rightText,
  scopeText,
  SEARCH_ABOVE,
  sectionId,
  TEAM_AT,
  teamSummary,
  type TeamSection,
} from './logic';
import { DirectoryBadge, KindBadges } from './TeamsList';

const kpiRowClass6 = 'mb-4 grid grid-cols-6 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5';
// Two columns, the narrow one first, one column below 1024 px.
const columnsClass = 'mb-4 grid grid-cols-[minmax(0,5fr)_minmax(0,7fr)] items-start gap-4 medium:grid-cols-1 narrow:mb-3 narrow:gap-3';
// The same, the wide one first.
const columnsWideFirstClass = 'mb-4 grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:mb-3 narrow:gap-3';
const stackClass = 'flex min-w-0 flex-col gap-4 narrow:gap-3';
const subTitleClass = 'm-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

const appOwnersHref = (id: string) => withParams(hrefTo({ page: 'app', id: id.toLowerCase() }), { tab: 'owners' });
const decisionHref = (ask: TeamAsk) => recordHref(ask.recordUrl) ?? recordHrefOf('decision', ask.decision.decisionId.toLowerCase()) ?? '#';

export function TeamPage({ teamId }: { teamId: string }) {
  const state = useRead('get_team', { teamId });
  const detail = dataOf(state);
  const at = useQueryParam('at', TEAM_AT) as TeamSection | undefined;

  // Opened at a section (a figure on the Teams list): move there once the team is read.
  const moved = useRef<string | null>(null);
  useEffect(() => {
    if (!detail || !at || moved.current === at) return;
    moved.current = at;
    window.requestAnimationFrame(() => scrollToPanel(sectionId(at)));
  }, [detail, at]);

  if (state.status === 'loading') {
    return (
      <>
        <PageHeader eyebrow="Team" title="Team" />
        <Panel title="Reading the team" state={state} skeletonRows={6}>{() => null}</Panel>
      </>
    );
  }
  if (!state.envelope.ok) {
    return (
      <>
        <PageHeader
          eyebrow="Team"
          title="Team"
          actions={<TextLink href={teamsHref()} className="text-sm">All teams and committees</TextLink>}
        />
        <PanelFailure failure={state.envelope} />
      </>
    );
  }
  return <TeamBody detail={state.envelope.data} envelope={state.envelope} />;
}

function TeamBody({ detail, envelope }: { detail: TeamDetail; envelope: Envelope<TeamDetail> }) {
  const c = detail.counts;
  const members = membersFigure(detail);
  const now = useMemo(() => Date.now(), [detail]);
  // A team with no product role shows one Roles panel, and one with no owner role one Applications panel, instead of
  // several empty ones; the figures then open that one panel.
  const hasRoles = detail.roles.length > 0;
  const ownsAny = c.assignments > 0;
  const go = (section: TeamSection) => {
    const shown: TeamSection = !hasRoles && section === 'decisions' ? 'roles' : !ownsAny && (section === 'gaps' || section === 'data') ? 'applications' : section;
    scrollToPanel(sectionId(shown));
  };
  const name = detail.team.displayLabel || 'A group';
  return (
    <>
      <PageHeader
        eyebrow="Team"
        title={name}
        status={(
          <span className="flex flex-wrap gap-1">
            <KindBadges kinds={detail.kinds} />
            <DirectoryBadge state={detail.directoryState} />
          </span>
        )}
        subtitle={teamSummary(detail)}
        meta={<DataStamp envelope={envelope} />}
        actions={<TextLink href={teamsHref()} className="text-sm">All teams and committees</TextLink>}
      />

      <div className={kpiRowClass6} role="group" aria-label="Key figures">
        <KpiTile label="Members" value={members.value} failed={members.failed} caption={members.caption} onClick={() => go('members')} />
        <KpiTile label="Product roles" value={c.roles} caption="Bound to this group" onClick={() => go('roles')} />
        <KpiTile label="Decision types" value={c.decisionTypes} caption="It approves, decides or advises on" onClick={() => go('decisions')} />
        <KpiTile
          label="Waiting now"
          value={c.waiting + c.waitingOnMembers}
          caption={c.waitingOnMembers ? `${c.waiting} on the team, ${c.waitingOnMembers} asked of its members` : 'Approval requests it can answer'}
          attention={c.waiting + c.waitingOnMembers > 0}
          onClick={() => go('waiting')}
        />
        <KpiTile label="Applications" value={c.applications} caption={ownsAny ? `${plural(c.assignments, 'owner role', 'owner roles')} held, on applications and data` : 'No owner role held'} onClick={() => go('applications')} />
        <KpiTile label="Owner gaps" value={c.ownerGaps} caption="On its applications" attention={c.ownerGaps > 0} onClick={() => go('gaps')} />
      </div>

      <div className={columnsClass}>
        <div className={stackClass}>
          <MembersPanel detail={detail} />
          {hasRoles && <DecisionsPanel detail={detail} />}
        </div>
        <RolesPanel detail={detail} />
      </div>

      <div className="mb-4 narrow:mb-3">
        <WaitingPanel detail={detail} now={now} />
      </div>

      {ownsAny ? (
        <div className={columnsWideFirstClass}>
          <ApplicationsPanel detail={detail} />
          <div className={stackClass}>
            <GapsPanel detail={detail} />
            <DataPanel detail={detail} />
          </div>
        </div>
      ) : (
        <div className="mb-4 narrow:mb-3">
          <Panel title="Applications and data" id={sectionId('applications')} count={0} subtitle="The applications, data domains and data locations this team holds an owner role on.">
            <Empty>This team holds no owner role on an application or on data, so it has no owner gaps to show.</Empty>
          </Panel>
        </div>
      )}

      {detail.members !== null && (
        <div className="mb-4 narrow:mb-3">
          <MembersOwnPanel detail={detail} />
        </div>
      )}

      <AnswersPanel detail={detail} />
    </>
  );
}

// A section's own search, in the address, when its list is long.
function SectionSearch({ show, name, label, placeholder }: { show: boolean; name: string; label: string; placeholder: string }) {
  if (!show) return null;
  return <ListSearch name={name} label={label} placeholder={placeholder} className="w-full" />;
}

// ---------- 2. members ----------

function MembersPanel({ detail }: { detail: TeamDetail }) {
  const refresh = useRefresh();
  const text = useListText('member');
  const all = detail.members;
  const rows = all ? filterByText(all, text, m => [m.displayLabel, PRINCIPAL_KIND_WORDS[m.principalKind]]) : [];
  const notAnswered = all === null && detail.membersReadFrom !== 'not-read';
  return (
    <Panel
      title="Members"
      id={sectionId('members')}
      count={detail.counts.members ?? undefined}
      subtitle={all ? detail.membership : undefined}
      actions={notAnswered ? <Button size="sm" variant="outline" onClick={refresh}>Read again</Button> : undefined}
    >
      {all === null ? (
        <Empty>{detail.membership}</Empty>
      ) : !all.length ? (
        <Empty>Nobody is in this group.</Empty>
      ) : (
        <>
          <SectionSearch show={all.length > SEARCH_ABOVE} name="member" label="Search the members by name" placeholder="Member name" />
          {!rows.length ? <Empty>{`No member matches "${text}".`}</Empty> : (
            <RowList label={`Members of ${detail.team.displayLabel}`}>
              {rows.map(m => (
                <Row
                  key={m.objectId}
                  lead={<PersonAvatar principal={{ objectId: m.objectId, displayLabel: m.displayLabel, principalKind: m.principalKind }} />}
                  primary={m.principalKind === 'user'
                    ? <HolderLink principal={{ objectId: m.objectId, displayLabel: m.displayLabel, principalKind: 'user' }} />
                    : m.displayLabel || PRINCIPAL_KIND_WORDS[m.principalKind]}
                  secondary={m.principalKind === 'user' ? undefined : `${PRINCIPAL_KIND_WORDS[m.principalKind]}. It holds no product role through this group.`}
                />
              ))}
            </RowList>
          )}
        </>
      )}
    </Panel>
  );
}

// ---------- 3. roles and what they decide ----------

function RolesPanel({ detail }: { detail: TeamDetail }) {
  return (
    <Panel
      title="Roles and what they decide"
      id={sectionId('roles')}
      count={detail.roles.length}
      subtitle="The product roles bound to this group, from the rules in force: what each role is accountable or responsible for, and which approvals ask it."
    >
      {!detail.roles.length ? (
        <Empty>
          {detail.kinds.includes('support-team')
            ? 'No product role is bound to this group, so it answers no decision through a role. It is a team because it holds owner roles on applications or data.'
            : 'No product role is bound to this group, so it answers no decision through a role.'}
        </Empty>
      ) : (
        <div className="flex flex-col">
          {detail.roles.map((r, i) => {
            // The decision types its people answer as named owners of their own data, not through the team (the data
            // owner). Fix round 2: the Data owners card said "Answers no decision" beside "Decisions it answers 4".
            const asOwner = detail.decisionTypes.filter(t => t.throughOwnerRole?.key === r.role.key);
            return (
            <div key={r.role.key} className={i === 0 ? 'flex flex-col gap-2' : 'flex flex-col gap-2 border-t border-line-strong pt-3 mt-3'}>
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <h3 className="m-0 text-sm font-semibold text-navy">
                  <TextLink href={hrefTo({ page: 'roles', roleKey: r.role.key })}>{r.role.label}</TextLink>
                </h3>
                {r.party && <Badge variant="subtle">{r.party.label}</Badge>}
                {r.answersDecisions
                  ? <ToneBadge tone="informative" icon={<MessageSquareReply aria-hidden />}>Answers decisions</ToneBadge>
                  : asOwner.length
                  ? <ToneBadge tone="informative" icon={<MessageSquareReply aria-hidden />}>Answers as named owners</ToneBadge>
                  : <ToneBadge tone="subtle" icon={<CircleSlash aria-hidden />}>Answers no decision</ToneBadge>}
              </div>
              {r.duties && <p className="m-0 text-sm text-navy-2">{r.duties}</p>}
              <Muted>
                {r.binding.bindingMethod ? `Bound through ${BINDING_METHOD_TEXT[r.binding.bindingMethod] ?? 'a binding'}` : 'Binding method not set'}
                {r.binding.lastVerifiedOn
                  ? `, last checked ${fmtDateTime(r.binding.lastVerifiedOn)}.`
                  // A demo's groups are fictional: their members are the demo's people, and no Entra binding is there to
                  // check (product review, fix round 1: "not checked yet" read as an unfinished setup on stage).
                  : detail.membersReadFrom === 'demo-personas' ? '. On this install the members are the demo personas and the estate\'s other fictional people.' : ', not checked yet.'}
              </Muted>
              {r.decisionRights.length > 0 && (
                <div className="flex flex-col gap-1">
                  <h4 className={subTitleClass}>Decision rights</h4>
                  <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={`Decision rights of ${r.role.label}`}>
                    {r.decisionRights.map(d => (
                      <li key={d.decisionRight.key} className="flex flex-col text-sm text-navy">
                        <span>{rightText(d)}</span>
                        <Muted>{scopeText(d.scope)}</Muted>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              {r.approvals.length > 0 && (
                <div className="flex flex-col gap-1">
                  <h4 className={subTitleClass}>Approvals that ask it</h4>
                  <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={`Approvals that ask ${r.role.label}`}>
                    {r.approvals.map(a => (
                      <li key={a.approvalRule.key} className="flex flex-col text-sm text-navy">
                        <span className="flex flex-wrap items-center gap-1.5">
                          {approvalText(a)}
                          {a.guidelineRef && <GuidelineMark guideline={a.guidelineRef} />}
                        </span>
                        <Muted>{scopeText(a.scope)}{a.isConsultation ? '. Its answer is advice and does not count toward the decision.' : ''}</Muted>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              {asOwner.length > 0 && (
                <div className="flex flex-col gap-1">
                  <h4 className={subTitleClass}>Asked of its people as named owners</h4>
                  <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={`Decisions asked of each named ${r.role.label.toLowerCase()}`}>
                    {asOwner.map(t => (
                      <li key={t.decisionType.key} className="flex flex-col text-sm text-navy">
                        <span>{t.decisionType.label}</span>
                        <Muted>
                          {t.consulted && !t.approves ? 'Consulted' : 'Approves'} as the named {(t.throughOwnerRole?.label ?? r.role.label).toLowerCase()} of each of their own data domains, not through the team.
                        </Muted>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              {!r.decisionRights.length && !r.approvals.length && !asOwner.length && <Muted>No decision right and no approval rule names this role.</Muted>}
            </div>
            );
          })}
        </div>
      )}
    </Panel>
  );
}

function DecisionsPanel({ detail }: { detail: TeamDetail }) {
  const roleLabel = (key: string) => detail.roles.find(r => r.role.key === key)?.role.label ?? 'a role';
  return (
    <Panel
      title="Decisions it answers"
      id={sectionId('decisions')}
      count={detail.decisionTypes.length}
      subtitle="Each decision type its roles approve, decide or advise on. Open one for the decisions of that type."
    >
      {!detail.decisionTypes.length ? (
        <Empty>{detail.roles.length ? 'Its roles answer no decision under the rules in force.' : 'It holds no product role, so it answers no decision through a role.'}</Empty>
      ) : (
        <RowList label="Decision types it answers">
          {detail.decisionTypes.map(t => (
            <Row
              key={t.decisionType.key}
              primary={<TextLink href={hrefTo({ page: 'decisions', typeKeys: [t.decisionType.key] })}>{t.decisionType.label}</TextLink>}
              secondary={t.throughOwnerRole
                ? `As the named ${t.throughOwnerRole.label.toLowerCase()} of their own data domains, not through the team`
                : `Through the ${t.roleKeys.map(roleLabel).join(' and ')} role${t.roleKeys.length > 1 ? 's' : ''}`}
              badges={decisionWords(t).map(w => <Badge key={w} variant={w === 'Approves' ? 'brand' : 'subtle'}>{w}</Badge>)}
            />
          ))}
        </RowList>
      )}
    </Panel>
  );
}

// ---------- 4. waiting now ----------

function AskTitle({ ask }: { ask: TeamAsk }) {
  return (
    <TextLink href={decisionHref(ask)}>
      {ask.decision.recordNumber ? `${ask.decision.recordNumber}: ` : ''}{ask.decision.statement || ask.decision.decisionType.label}
    </TextLink>
  );
}

function AskSubject({ ask }: { ask: TeamAsk }): ReactNode {
  const s = ask.subject;
  const about = s.businessApplicationId && s.applicationName
    ? <TextLink href={hrefTo({ page: 'app', id: s.businessApplicationId.toLowerCase() })}>{s.applicationName}</TextLink>
    : s.label;
  return <>{ask.decision.decisionType.label}, about {about}</>;
}

function WaitingPanel({ detail, now }: { detail: TeamDetail; now: number }) {
  const text = useListText('waiting');
  const words = (a: TeamAsk) => [a.decision.recordNumber, a.decision.statement, a.decision.decisionType.label, a.subject.label, a.subject.applicationName, a.askedRole?.label, a.askedOwnerRole?.label];
  const rows = filterByText(detail.waiting, text, words);
  const onMembers = filterByText(detail.waitingOnMembers, text, m => [...words(m.request), m.member.displayLabel]);
  const total = detail.waiting.length + detail.waitingOnMembers.length;
  return (
    <Panel
      title="Waiting now"
      id={sectionId('waiting')}
      count={total}
      subtitle="The approval requests this team can answer, and those asked of its members by name, soonest expiry first. A person answers on the decision page, never here."
    >
      <SectionSearch show={total > SEARCH_ABOVE} name="waiting" label="Search the waiting requests" placeholder="Number, statement, application or member" />
      {!detail.waiting.length ? <Empty>{detail.waitingOnMembers.length ? 'Nothing waits on the team as a whole now.' : 'Nothing waits on this team or its members now.'}</Empty> : (
        <>
          {!rows.length ? <Empty>{`No request waiting on the team matches "${text}".`}</Empty> : (
            <RowList label="Approval requests waiting on this team">
              {rows.map(a => <WaitingRow key={a.approvalRecordId} ask={a} now={now} />)}
            </RowList>
          )}
        </>
      )}
      {detail.waitingOnMembers.length > 0 && (
        <div className="mt-3 flex flex-col gap-1.5">
          <h3 className={subTitleClass}>Asked of its members by name</h3>
          {!onMembers.length ? <Empty>{`No request asked of its members matches "${text}".`}</Empty> : (
            <RowList label="Approval requests asked of its members">
              {onMembers.map(m => <WaitingRow key={m.request.approvalRecordId} ask={m.request} now={now} member={m.member} />)}
            </RowList>
          )}
        </div>
      )}
    </Panel>
  );
}

function WaitingRow({ ask: a, now, member }: { ask: TeamAsk; now: number; member?: PrincipalRef }) {
  const e = expiry(a, now);
  return (
    <Row
      wideEnd
      lead={<LeadIcon tone={e.tone === 'danger' ? 'danger' : e.tone === 'warning' ? 'warning' : 'brand'}><Clock /></LeadIcon>}
      primary={<AskTitle ask={a} />}
      secondary={<AskSubject ask={a} />}
      tertiary={member
        ? <>Asked of <HolderLink principal={member} />{a.askedOwnerRole ? `, as ${a.askedOwnerRole.label.toLowerCase()}` : ''}.{a.requestedOn ? ` Asked ${fmtDate(a.requestedOn)}.` : ''}</>
        : `${askedText(a)}.${a.requestedOn ? ` Asked ${fmtDate(a.requestedOn)}.` : ''}`}
      badges={a.isConsultation ? <Badge variant="subtle">Consultation</Badge> : undefined}
      end={(
        <>
          <ToneBadge tone={e.tone}>{e.text}</ToneBadge>
          <TextLink href={decisionHref(a)} className="text-xs" aria-label={`Answer on the decision page: ${a.decision.recordNumber ?? a.decision.statement}`}>Answer on the decision page</TextLink>
        </>
      )}
    />
  );
}

// ---------- 5. applications and data ----------

function ApplicationsPanel({ detail }: { detail: TeamDetail }) {
  const text = useListText('application');
  const rows = filterByText(detail.applications, text, a => [a.name, a.recordNumber, a.tier?.label, a.stage?.label, assignmentsText(a.assignments)]);
  const ownerPage = hrefTo({ page: 'person', objectId: detail.team.objectId.toLowerCase() });
  return (
    <Panel
      title="Applications and owner roles"
      id={sectionId('applications')}
      count={detail.applications.length}
      subtitle="The applications this team holds an owner role on now, with the roles it holds there."
      actions={detail.counts.assignments > 0 ? <TextLink href={ownerPage} className="text-sm">Hand over, or see ended roles</TextLink> : undefined}
    >
      {!detail.applications.length ? <Empty>This team holds no owner role on an application now.</Empty> : (
        <>
          <SectionSearch show={detail.applications.length > SEARCH_ABOVE} name="application" label="Search the applications" placeholder="Application, tier or owner role" />
          {!rows.length ? <Empty>{`No application matches "${text}".`}</Empty> : (
            <RowList label="Applications this team holds owner roles on">
              {rows.map(a => (
                <Row
                  key={a.businessApplicationId}
                  wideEnd
                  lead={<LeadIcon tone="brand"><LayoutGrid /></LeadIcon>}
                  primary={<TextLink href={appOwnersHref(a.businessApplicationId)}>{a.name}</TextLink>}
                  secondary={[a.recordNumber, a.tier?.label ?? 'No tier yet', a.stage?.label].filter(Boolean).join(', ')}
                  tertiary={assignmentsText(a.assignments)}
                  badges={a.registration === 'draft' ? <Badge variant="informative">Draft</Badge> : undefined}
                  end={a.ownerGapCount
                    ? (
                      <TextLink href={hrefTo({ page: 'owners', view: 'gaps', applicationId: a.businessApplicationId.toLowerCase() })} aria-label={`${plural(a.ownerGapCount, 'owner gap', 'owner gaps')} on ${a.name}: open them on Owners`}>
                        <ToneBadge tone="warning">{plural(a.ownerGapCount, 'gap', 'gaps')}</ToneBadge>
                      </TextLink>
                    )
                    : <ToneBadge tone="success">No gaps</ToneBadge>}
                />
              ))}
            </RowList>
          )}
        </>
      )}
    </Panel>
  );
}

function DataPanel({ detail }: { detail: TeamDetail }) {
  return (
    <Panel
      title="Data it owns"
      id={sectionId('data')}
      count={detail.dataAssignments.length}
      subtitle="The data domains and data locations this team holds an owner role on."
    >
      {!detail.dataAssignments.length ? <Empty>This team holds no owner role on a data domain or a data location.</Empty> : (
        <RowList label="Data this team holds owner roles on">
          {detail.dataAssignments.map(d => (
            <Row
              key={d.ownerAssignmentId}
              lead={<LeadIcon tone="subtle"><Database /></LeadIcon>}
              primary={<TextLink href={recordHref(d.recordUrl) ?? recordHrefOf(d.subjectKind, d.subjectId) ?? '#'}>{d.label}</TextLink>}
              secondary={`${DATA_SUBJECT_WORDS[d.subjectKind]}. ${assignmentsText([d])}`}
              endSub={d.startOn ? `Since ${fmtDate(d.startOn)}` : undefined}
            />
          ))}
        </RowList>
      )}
    </Panel>
  );
}

// ---------- 6. owner gaps ----------

function GapsPanel({ detail }: { detail: TeamDetail }) {
  return (
    <Panel
      title="Owner gaps"
      id={sectionId('gaps')}
      count={detail.ownerGaps.length}
      subtitle="On the applications this team holds owner roles on: a role with no holder, a required deputy missing, or a holder whose account is off. A person proposes the missing owner on Owners."
    >
      {!detail.applications.length ? <Empty>No application to check: this team holds no owner role on one.</Empty>
        : !detail.ownerGaps.length ? <Empty>Every owner role on its applications has its holder and, where required, a deputy.</Empty> : (
          <RowList label="Owner gaps on this team's applications">
            {detail.ownerGaps.map(g => (
              <Row
                key={`${g.businessApplicationId}-${g.ownerRole.key}-${g.missing}`}
                wideEnd
                lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><UserRoundX className="size-[18px]" aria-hidden /></span>}
                primary={<TextLink href={appOwnersHref(g.businessApplicationId)}>{g.applicationName}</TextLink>}
                secondary={`${g.ownerRole.label}: ${g.missing === 'holder' ? 'no holder' : 'no deputy'}`}
                badges={g.holderDirectoryState === 'disabled' || g.holderDirectoryState === 'deleted'
                  ? <ToneBadge tone="danger">{`Holder's account is ${g.holderDirectoryState}`}</ToneBadge>
                  : undefined}
                end={(
                  <TextLink href={hrefTo({ page: 'owners', view: 'gaps', applicationId: g.businessApplicationId.toLowerCase() })} className="text-xs" aria-label={`Propose the missing ${g.missing} for ${g.ownerRole.label} of ${g.applicationName} on Owners`}>
                    Propose on Owners
                  </TextLink>
                )}
              />
            ))}
          </RowList>
        )}
    </Panel>
  );
}

// ---------- 6b. what its members own ----------

// What its members own in their own name (product review, fix round 1): the applications and the data they hold owner roles on,
// so a committee page shows the owners of the apps and data, as the product owner asked.
function MembersOwnPanel({ detail }: { detail: TeamDetail }) {
  const text = useListText('owns');
  const rows = filterByText(detail.membersOwn, text, o => [o.member.displayLabel, o.label, o.ownerRole.label]);
  const shown = detail.membersOwn.length;
  const total = detail.counts.membersOwn;
  return (
    <Panel
      title="What its members own"
      id="team-members-own"
      count={total}
      subtitle={total > shown
        ? `The first ${shown} of ${total} owner roles its members hold in their own name, on applications and on data.`
        : 'The owner roles its members hold in their own name, on applications and on data.'}
    >
      {!shown ? <Empty>None of its members holds an owner role in their own name.</Empty> : (
        <>
          <SectionSearch show={shown > SEARCH_ABOVE} name="owns" label="Search what its members own" placeholder="Member, application, domain or owner role" />
          {!rows.length ? <Empty>{`Nothing its members own matches "${text}".`}</Empty> : (
            <RowList label="What the members of this team own">
              {rows.map(o => (
                <Row
                  key={`${o.member.objectId}-${o.subjectKind}-${o.subjectId}-${o.ownerRole.key}-${o.isDeputy}`}
                  lead={<LeadIcon tone={o.subjectKind === 'business-application' ? 'brand' : 'subtle'}>{o.subjectKind === 'business-application' ? <LayoutGrid /> : <Database />}</LeadIcon>}
                  primary={<TextLink href={o.subjectKind === 'business-application' ? appOwnersHref(o.subjectId) : recordHref(o.recordUrl) ?? recordHrefOf(o.subjectKind, o.subjectId) ?? '#'}>{o.label}</TextLink>}
                  secondary={<>{`${o.ownerRole.label}${o.isDeputy ? ' (deputy)' : ''}: `}<HolderLink principal={o.member} /></>}
                  tertiary={o.subjectKind === 'business-application' ? undefined : DATA_SUBJECT_WORDS[o.subjectKind]}
                />
              ))}
            </RowList>
          )}
        </>
      )}
    </Panel>
  );
}

// ---------- 7. recent answers ----------

function AnswersPanel({ detail }: { detail: TeamDetail }) {
  const shown = detail.recentAnswers.length;
  const total = detail.counts.answered;
  return (
    <Panel
      title="Recent answers"
      id={sectionId('answers')}
      count={shown}
      subtitle={total > shown
        ? `The latest ${shown} of ${total} answers its people gave through this team, newest first.`
        : 'The answers its people gave through this team, newest first.'}
    >
      {!shown ? (
        <Empty>Nobody has answered a request through this team yet.</Empty>
      ) : (
        <RowList label="Recent answers through this team">
          {detail.recentAnswers.map(a => (
            <Row
              key={a.approvalRecordId}
              wideEnd
              lead={<LeadIcon tone={a.outcome.meaning === 'rejected' ? 'danger' : 'success'}><BookOpenCheck /></LeadIcon>}
              primary={<AskTitle ask={a} />}
              secondary={<AskSubject ask={a} />}
              tertiary={a.approver
                ? <>{answerText(a)} by <HolderLink principal={a.approver} />{a.isConsultation ? ', as advice' : ''}.</>
                : `${answerText(a)}${a.isConsultation ? ', as advice' : ''}.`}
              end={<StatusBadge status={a.outcome} />}
            />
          ))}
        </RowList>
      )}
    </Panel>
  );
}

