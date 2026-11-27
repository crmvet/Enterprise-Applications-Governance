// Roles and access (screen "Roles and access", priority 3): which Entra group carries which product role (D-11: people
// hold a role only through its group), who holds it, the workload grants by expiry, what each role may do, and the
// separation rules in one sentence each.
//   /app/roles                     every role: its group, what it may do in counts, and the figures that open them
//                                  (?show=mine|unbound|workload|changes-configuration narrows the list)
//   /app/roles?role=<key>          one role: its groups and their members read live from Entra, its tools with how far
//                                  an AI client may go (?tools=write|read|ai-writes|person-only), its decision rights,
//                                  the approvals it gives with their guideline, and the separation rules that name it
//   ?q=<text>                      searches the roles (name, duties, party, bound groups, the decisions they make) and
//                                  the owner roles; on one role it searches its tools
// Reads: get_role_access (all roles without holders; one role with holders), list_reviews (access checks), get_configuration
// (owner roles, decision rights, approval rules: what each owner role decides), and, for the platform admin,
// check_role_bindings on request. Write: propose a binding change through the configuration change sheet
// (pages/configuration/sheets.tsx), shown to a person whose tools include propose_configuration_change.
// Wave 4 choices (D-41):
// - Owner roles get a panel of their own: an owner role (business owner, data owner, data steward and the rest) is held
//   on a record, not through a group, so get_role_access lists none of its decision rights under a product role. The
//   wave 3 check found the data owner role reading "0 decision rights, 0 approvals". The panel reads the decision-right
//   and approval-rule rows that name each owner role, as the holder or as the fallback.
// - The application owner and data owner product roles say "Decides through owner roles" instead of 0, and their page
//   lists those owner roles, because their duties are to hold owner roles on applications and on data domains.
// - The search is q= in the address (components/listSearch.tsx), as on every list page.
import { Bot, Cog, ShieldCheck, UsersRound } from 'lucide-react';
import { useMemo, useState } from 'react';
import { hrefTo, useQueryParam } from '../../app/links';
import type { RouteOf } from '../../app/routes';
import { DataStamp, Empty, KpiTile, LinkButton, Muted, PageHeader, Panel, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { ListSearch, setListText, useListText } from '../../components/listSearch';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import type { RoleAccess, RoleBindingCheck } from '../../data/contracts';
import { fmtDate, fmtDateTime, plural, todayInZone } from '../../data/format';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { useSession, type Session } from '../../data/session';
import { filterByText } from '../../data/textSearch';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { useInstall } from '../../app/install';
import { ConfigurationChangeSheet } from '../configuration/sheets';
import { LeadIcon } from '../ai-audit/parts';
import { kpiRowClass, scrollToPanel, twoColumnClass } from '../owners-reviews/people';
import {
  aiLine,
  BINDING_METHOD_TEXT,
  bindingFindings,
  bindingOperations,
  grantExpiryText,
  grantsByExpiry,
  groupByToolset,
  isUnbound,
  letterText,
  ownerRoleDuties,
  ownerRolesOf,
  relationText,
  roleSearchFields,
  subjectKindsText,
  ROLE_SHOW_TEXT,
  ROLE_SHOWS,
  roleCounts,
  roleMatches,
  TOOL_FILTER_TEXT,
  TOOL_FILTERS,
  toolMatches,
  toolTitle,
  type OwnerRoleDuty,
  type RoleItem,
  type RoleShow,
  type ToolFilter,
} from './logic';
import { GuidelineLink, Note, PageFailure, SubHeading, useParamNav } from './parts';
// Wave 4 hook point (the teams group builds it): each bound group links to its team page.
import { TeamLink } from '../teams';

const SHOW = new RegExp(`^(${ROLE_SHOWS.join('|')})$`);
const TOOLS = new RegExp(`^(${TOOL_FILTERS.join('|')})$`);

// Each owner role with the decision rights and approval rules that name it, from the configuration in force. The three
// reads share the input useLabels uses, so a page that also reads labels reads each entity once.
function useOwnerRoleDuties(): { duties: OwnerRoleDuty[]; loading: boolean; failed: boolean } {
  const owners = useTool('get_configuration', { entity: 'owner-role', top: 100 });
  const rights = useTool('get_configuration', { entity: 'decision-right', top: 100 });
  const rules = useTool('get_configuration', { entity: 'approval-rule', top: 100 });
  const o = dataOf(owners)?.rows;
  const r = dataOf(rights)?.rows;
  const a = dataOf(rules)?.rows;
  const duties = useMemo(() => (o && r && a ? ownerRoleDuties(o, r, a) : []), [o, r, a]);
  const loading = owners.status === 'loading' || rights.status === 'loading' || rules.status === 'loading';
  const failed = [owners, rights, rules].some(s => s.status === 'done' && !s.envelope.ok);
  return { duties, loading, failed };
}

export function RolesPage({ route }: { route: RouteOf<'roles'> }) {
  const session = useSession();
  return route.roleKey ? <RoleDetail roleKey={route.roleKey} session={session} /> : <RoleList session={session} />;
}

// ---------- every role ----------

function RoleList({ session }: { session: Session }) {
  const install = useInstall();
  const all = useRead('get_role_access', {});
  const data = dataOf(all);
  const show = (useQueryParam('show', SHOW) ?? 'all') as RoleShow;
  const text = useListText();
  const go = useParamNav();
  const owners = useOwnerRoleDuties();
  const counts = data ? roleCounts(data.roles) : null;
  const shown = data ? filterByText(data.roles.filter(r => roleMatches(r, show)), text, roleSearchFields) : [];
  const canCheck = session.can('check_role_bindings');
  const [runs, setRuns] = useState(0);
  const runCheck = () => {
    setRuns(n => n + 1);
    window.setTimeout(() => scrollToPanel('binding-check'), 0);
  };
  const envelope = all.status === 'done' ? all.envelope : undefined;

  const header = (
    <PageHeader
      eyebrow="Govern"
      title="Roles and access"
      subtitle="People hold a product role only through its Entra group. Workload identities, such as the build pipeline, hold theirs through a recorded grant. What each role may do comes from the configuration in force."
      meta={(
        <span className="flex flex-col gap-0.5">
          {data && <span>{data.myRoleKeys.length ? `You hold ${plural(data.myRoleKeys.length, 'role', 'roles')}: ${data.roles.filter(r => r.heldByMe).map(r => r.role.label).join(', ') || 'none listed here'}.` : 'You hold no product role.'}</span>}
          {install.demo && <span>The demo persona switch is not a role. It lets the demo operator act as one of the fictional people, who then holds the roles of their groups.</span>}
          <span><DataStamp envelope={envelope} /></span>
        </span>
      )}
      actions={canCheck ? <Button size="sm" variant="outline" onClick={runCheck}><ShieldCheck aria-hidden />Run the binding check</Button> : undefined}
    />
  );
  if (envelope && !envelope.ok) return <>{header}<PageFailure failure={envelope} /></>;

  return (
    <>
      {header}

      <div className={kpiRowClass} role="group" aria-label="Key figures">
        <KpiTile label="Product roles" value={counts?.people ?? null} caption="Held by people through their groups" onClick={() => go({ show: undefined })} />
        <KpiTile label="No group bound" value={counts?.unbound ?? null} caption="Nobody can hold these roles yet" attention={!!counts?.unbound} onClick={() => go({ show: 'unbound' })} />
        <KpiTile label="Roles I hold" value={counts?.mine ?? null} caption="Through the groups you are in" onClick={() => go({ show: 'mine' })} />
        <KpiTile
          label="Workload grants"
          value={counts?.grants ?? null}
          caption={counts?.expiredGrants ? `${counts.expiredGrants} expired` : 'Soonest expiry first'}
          attention={!!counts?.expiredGrants}
          onClick={() => scrollToPanel('grants')}
        />
        <KpiTile label="Separation rules" value={data?.separationRules.length ?? null} caption="Who may not approve what" onClick={() => scrollToPanel('separation')} />
      </div>

      <div className={twoColumnClass}>
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel
            title="Roles"
            id="roles"
            count={data ? shown.length : undefined}
            state={all}
            subtitle="Open a role to see who holds it now and everything it may do."
          >
            {d => (
              <>
                <div className="flex flex-wrap items-end gap-3">
                  <ListSearch label="Search the roles by name, group or the decisions they make" placeholder="Role, group or decision" />
                  <SwitchTabs
                    label="Show"
                    ariaLabel="Which roles"
                    value={show}
                    onChange={v => go({ show: v === 'all' ? undefined : v })}
                    options={ROLE_SHOWS.map(s => ({ value: s, label: ROLE_SHOW_TEXT[s] }))}
                  />
                </div>
                {d.toolPolicySource === 'shipped-seed' && (
                  <Note>The tools each role may call come from the shipped Tool Policy, because the configuration in force holds no Tool Policy rows of its own.</Note>
                )}
                {shown.length === 0 ? (
                  text ? (
                    <Empty>
                      {`No role matches "${text}". `}
                      <LinkButton onClick={() => setListText(undefined)}>Clear the search</LinkButton>
                    </Empty>
                  ) : <Empty>{show === 'unbound' ? 'Every product role has a group bound to it.' : show === 'mine' ? 'You hold no product role.' : 'No role matches.'}</Empty>
                ) : (
                  <RowList label="Product roles">
                    {shown.map(r => <RoleRow key={r.role.key} role={r} through={ownerRolesOf(r.role.key, owners.duties)} />)}
                  </RowList>
                )}
              </>
            )}
          </Panel>
          <OwnerRolesPanel owners={owners} text={text} />
        </div>

        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <BindingCheckPanel canCheck={canCheck} runs={runs} onRun={runCheck} readFrom={data?.holdersReadFrom} />
          <GrantsPanel state={all} />
          <SeparationPanel state={all} />
          <AccessChecksPanel />
        </div>
      </div>
    </>
  );
}

function RoleRow({ role, through }: { role: RoleItem; through: OwnerRoleDuty[] }) {
  // A role with no decision right or approval of its own whose holders decide through owner roles on records says so,
  // rather than "0 decision rights, 0 approvals" (wave 3 verification).
  const viaOwners = !role.isWorkloadRole && role.decisionRights.length === 0 && role.approvals.length === 0 && through.length > 0;
  const ownerRights = through.reduce((n, d) => n + d.rights.length, 0);
  const ownerApprovals = through.reduce((n, d) => n + d.approvals.length, 0);
  const groups = role.bindings.filter(b => b.group).map(b => b.group!.displayLabel);
  const where = role.isWorkloadRole
    ? role.workloadGrants.length ? `Held through ${plural(role.workloadGrants.length, 'workload grant', 'workload grants')}` : 'No workload grant: nothing holds it'
    : groups.length ? `Through ${groups.join(', ')}` : 'No group bound: nobody holds it';
  return (
    <Row
      lead={<LeadIcon tone={role.isWorkloadRole ? 'subtle' : isUnbound(role) ? 'warning' : 'brand'}>{role.isWorkloadRole ? <Cog /> : <UsersRound />}</LeadIcon>}
      primary={<TextLink href={hrefTo({ page: 'roles', roleKey: role.role.key })}>{role.role.label}</TextLink>}
      secondary={role.duties ?? undefined}
      tertiary={where}
      badges={(
        <>
          {role.heldByMe && <ToneBadge tone="success">You hold it</ToneBadge>}
          {isUnbound(role) && <ToneBadge tone="warning">No group</ToneBadge>}
          {role.isWorkloadRole && <ToneBadge tone="subtle" icon={<Bot aria-hidden />}>Workload role</ToneBadge>}
          {role.isReadOnly && <ToneBadge tone="subtle" icon={null}>Reads only</ToneBadge>}
          {role.canChangeConfiguration && <ToneBadge tone="brand" icon={null}>Changes the configuration</ToneBadge>}
        </>
      )}
      end={<span className="text-xs text-navy-2 tabular-nums">{role.isWorkloadRole ? 'Records through its grant' : plural(role.tools.length, 'tool', 'tools')}</span>}
      endSub={role.isWorkloadRole ? undefined : viaOwners ? (
        <LinkButton className="text-xs" onClick={() => scrollToPanel('owner-roles')} aria-label={`${role.role.label} decides through ${plural(through.length, 'owner role', 'owner roles')}. Shows the owner roles.`}>
          {`Through ${plural(through.length, 'owner role', 'owner roles')}: ${plural(ownerRights, 'decision right', 'decision rights')}, ${plural(ownerApprovals, 'approval', 'approvals')}`}
        </LinkButton>
      ) : `${plural(role.decisionRights.length, 'decision right', 'decision rights')}, ${plural(role.approvals.length, 'approval', 'approvals')}`}
    />
  );
}

// ---------- owner roles ----------

// One owner role's duties in words: each decision right with its letter, each approval rule, the fallbacks marked.
function OwnerRoleDuties({ duty, typeLabel }: { duty: OwnerRoleDuty; typeLabel: (key: string) => string }) {
  const others = (key: string) => typeLabel(key);
  return (
    <div className="flex flex-col gap-1">
      {duty.rights.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-0.5 p-0 text-xs text-navy-2" aria-label={`Decision rights of the ${duty.ownerRole.label.toLowerCase()}`}>
          {duty.rights.map(r => (
            <li key={r.key}>
              <span className="font-semibold text-navy">{others(r.decisionTypeKey)}</span>
              {`: ${letterText(r.letter)}${r.fallback ? ', when the role it names has no holder' : ''}`}
            </li>
          ))}
        </ul>
      )}
      {duty.approvals.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-0.5 p-0 text-xs text-navy-2" aria-label={`Approvals the ${duty.ownerRole.label.toLowerCase()} gives`}>
          {duty.approvals.map(a => (
            <li key={a.key}>
              <span className="font-semibold text-navy">{others(a.decisionTypeKey)}</span>
              {`: ${a.isConsultation ? 'consulted, advice only' : 'approves'}${a.fallback ? ', when the owner role the rule names has no holder' : ''} (${a.label})`}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function OwnerRolesPanel({ owners, text }: { owners: { duties: OwnerRoleDuty[]; loading: boolean; failed: boolean }; text: string | undefined }) {
  const types = useLabels('decision-type');
  const typeLabel = (key: string) => types.label(key);
  const shown = filterByText(owners.duties, text, d => [
    d.ownerRole.label,
    subjectKindsText(d.ownerRole.subjectKinds),
    ...d.rights.map(r => typeLabel(r.decisionTypeKey)),
    ...d.approvals.map(a => typeLabel(a.decisionTypeKey)),
  ]);
  return (
    <Panel
      title="Owner roles"
      id="owner-roles"
      count={owners.loading || owners.failed ? undefined : shown.length}
      subtitle="An owner role is held on a record, such as the business owner of an application or the data owner of a domain, not through a group. The engine asks the person who holds it on that record."
    >
      {owners.loading ? <Muted>Reading the owner roles.</Muted>
        : owners.failed ? <Note>The owner roles could not be read just now. The rest of the page still works.</Note>
          : shown.length === 0 ? <Empty>{text ? `No owner role matches "${text}".` : 'The configuration holds no owner role.'}</Empty>
            : (
              <RowList label="Owner roles">
                {shown.map(d => (
                  <Row
                    key={d.ownerRole.key}
                    lead={<LeadIcon tone="brand"><UsersRound /></LeadIcon>}
                    primary={d.ownerRole.label}
                    secondary={`Held on ${subjectKindsText(d.ownerRole.subjectKinds)}`}
                    tertiary={d.rights.length || d.approvals.length ? <OwnerRoleDuties duty={d} typeLabel={typeLabel} /> : 'No decision right or approval rule names it.'}
                    endSub={`${plural(d.rights.length, 'decision right', 'decision rights')}, ${plural(d.approvals.length, 'approval', 'approvals')}`}
                  />
                ))}
              </RowList>
            )}
      <Note>
        People take an owner role on the Owners page, through an owner change a person sends. A decision right that names an
        owner role asks its holder on the record the decision is about.
      </Note>
    </Panel>
  );
}

// ---------- the side panels ----------

// Where a binding check looks: Entra, or on a demo install and a local run the demo personas, whose fictional groups
// stand in for Entra groups (wave 3 verification: the page said "against the directory" on a local run).
const directoryWords = (readFrom: RoleAccess['holdersReadFrom'] | undefined) => (readFrom === 'demo-personas' ? 'the demo personas' : 'Entra');
// The verb that goes with it: Entra is asked; the demo personas are checked against.
const askWords = (readFrom: RoleAccess['holdersReadFrom'] | undefined, form: 'asks' | 'ask' | 'asking') => (readFrom === 'demo-personas'
  ? { asks: 'checks against the demo personas', ask: 'Check against the demo personas', asking: 'Checking against the demo personas' }[form]
  : { asks: 'asks Entra', ask: 'Ask Entra', asking: 'Asking Entra' }[form]);

function BindingCheckPanel({ canCheck, runs, onRun, readFrom }: { canCheck: boolean; runs: number; onRun: () => void; readFrom: RoleAccess['holdersReadFrom'] | undefined }) {
  if (!canCheck) {
    return (
      <Panel title="Binding check" id="binding-check">
        <Note>
          The platform admin runs the binding check. It {askWords(readFrom, 'asks')} whether each bound group exists,
          whether each workload grant has its app role assignment, and whether anyone holds a role directly.
        </Note>
      </Panel>
    );
  }
  if (runs === 0) {
    return (
      <Panel title="Binding check" id="binding-check" actions={<Button size="sm" onClick={onRun}>Run it now</Button>}>
        <p className="m-0 text-sm leading-relaxed text-navy-2">
          {askWords(readFrom, 'ask')} whether each bound group exists, whether each workload grant has its app role
          assignment, and whether anyone holds a role directly instead of through a group. The check only reads: it
          changes nothing.
          {readFrom === 'demo-personas' ? ' On this install a bound group counts as found when a demo persona is in it.' : ''}
        </p>
      </Panel>
    );
  }
  return <BindingCheckRun key={runs} onRun={onRun} readFrom={readFrom} />;
}

// The last binding check this session ran, by role key, so a role's page can say when its group was last checked: the
// check reads only and stores nothing (wave 3 verification: a role still read "Not checked yet" just after the check).
const lastBindingCheck: { asOf: string | null; groupExists: Map<string, boolean> } = { asOf: null, groupExists: new Map() };

function BindingCheckRun({ onRun, readFrom }: { onRun: () => void; readFrom: RoleAccess['holdersReadFrom'] | undefined }) {
  const check = useRead('check_role_bindings', { includeDirectAssignments: true });
  const roles = useLabels('role');
  const today = todayInZone();
  if (check.status === 'done' && check.envelope.ok && lastBindingCheck.asOf !== check.envelope.asOf) {
    lastBindingCheck.asOf = check.envelope.asOf;
    lastBindingCheck.groupExists = new Map(check.envelope.data.bindings.map(b => [b.roleKey, b.groupExists]));
  }
  return (
    <Panel
      title="Binding check"
      id="binding-check"
      state={check}
      skeletonRows={4}
      actions={check.status === 'done' ? <Button size="sm" variant="outline" onClick={onRun}>Run it again</Button> : undefined}
      subtitle={check.status === 'done'
        ? (check.envelope.ok ? `Checked ${fmtDateTime(check.envelope.asOf)}, against ${directoryWords(readFrom)}.` : `${readFrom === 'demo-personas' ? 'The demo personas' : 'Entra'} did not answer.`)
        : `${askWords(readFrom, 'asking')} now.`}
    >
      {d => <BindingCheckBody check={d} roleLabel={k => roles.label(k)} today={today} />}
    </Panel>
  );
}

function BindingCheckBody({ check, roleLabel, today }: { check: RoleBindingCheck; roleLabel: (key: string) => string; today: string }) {
  const findings = bindingFindings(check, roleLabel);
  const humanBound = check.bindings.length > 0 && check.bindings.every(b => b.group && b.groupExists);
  return (
    <div className="flex flex-col gap-3">
      {findings.length > 0 && humanBound && (
        <p className="m-0 flex items-start gap-1.5 text-sm text-success">
          <ShieldCheck className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>Every human role is bound to a group that exists. The findings below are about workload identities.</span>
        </p>
      )}
      {findings.length === 0 ? (
        <p className="m-0 flex items-start gap-1.5 text-sm text-success">
          <ShieldCheck className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>Every bound group exists, every workload grant has its assignment, and nobody holds a role directly.</span>
        </p>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-1.5 p-0" aria-label="Findings">
          {findings.map(f => (
            <li key={f.text} className="flex flex-wrap items-start gap-2 text-sm text-navy">
              <ToneBadge tone={f.tone}>{f.tone === 'danger' ? 'Fix' : 'Check'}</ToneBadge>
              <span className="min-w-0 flex-1">{f.text}</span>
            </li>
          ))}
        </ul>
      )}
      <SubHeading>Groups</SubHeading>
      {check.bindings.length === 0 ? <Empty>No role is bound to a group.</Empty> : (
        <RowList label="Bound groups">
          {check.bindings.map(b => (
            <Row
              key={b.roleKey}
              primary={roleLabel(b.roleKey)}
              secondary={b.group ? `${b.group.displayLabel}, ${b.bindingMethod ? BINDING_METHOD_TEXT[b.bindingMethod] : 'binding method not set'}` : 'No group named'}
              end={b.groupExists ? <ToneBadge tone="success">Group exists</ToneBadge> : <ToneBadge tone="danger">Not in the directory</ToneBadge>}
            />
          ))}
        </RowList>
      )}
      {check.workloadGrants.length > 0 && (
        <>
          <SubHeading>Workload grants</SubHeading>
          <RowList label="Workload grants checked">
            {check.workloadGrants.map(g => {
              const expiry = grantExpiryText({ expiresOn: g.expiresOn, isExpired: g.isExpired }, today);
              return (
                <Row
                  key={`${g.principal.objectId}-${g.roleKey}`}
                  primary={g.principal.displayLabel || 'A workload identity'}
                  secondary={`${roleLabel(g.roleKey)}. ${g.reason}`}
                  wideEnd
                  end={(
                    <>
                      <ToneBadge tone={expiry.tone}>{expiry.text}</ToneBadge>
                      {g.assignmentFound ? <ToneBadge tone="success">Assignment found</ToneBadge> : <ToneBadge tone="warning">No assignment</ToneBadge>}
                    </>
                  )}
                />
              );
            })}
          </RowList>
        </>
      )}
      {check.directAssignments.length > 0 && (
        <>
          <SubHeading>Held directly</SubHeading>
          <RowList label="Direct assignments">
            {check.directAssignments.map((a, i) => (
              <Row
                key={`${a.principal.objectId}-${a.roleKey}-${i}`}
                primary={a.principal.displayLabel || 'Someone'}
                secondary={`${roleLabel(a.roleKey)}, ${a.where}`}
                end={a.coveredByGrant ? <ToneBadge tone="success">Covered by a grant</ToneBadge> : <ToneBadge tone="danger">No grant</ToneBadge>}
              />
            ))}
          </RowList>
        </>
      )}
    </div>
  );
}

function GrantsPanel({ state }: { state: ToolState<RoleAccess> }) {
  const today = todayInZone();
  return (
    <Panel title="Workload grants by expiry" id="grants" state={state} notes={[]} subtitle="A workload identity holds its role only through a recorded grant, which ends.">
      {d => {
        const grants = grantsByExpiry(d.roles);
        if (!grants.length) return <Empty>No workload identity holds a grant.</Empty>;
        return (
          <RowList label="Workload grants">
            {grants.map((g, i) => {
              const expiry = grantExpiryText(g, today);
              return (
                <Row
                  key={`${g.roleKey}-${g.principal?.objectId ?? i}`}
                  lead={<LeadIcon tone={g.isExpired ? 'danger' : 'subtle'}><Bot /></LeadIcon>}
                  primary={g.principal?.displayLabel || 'A workload identity'}
                  secondary={<TextLink href={hrefTo({ page: 'roles', roleKey: g.roleKey })}>{g.roleLabel}</TextLink>}
                  tertiary={g.reason ?? undefined}
                  end={<ToneBadge tone={expiry.tone}>{expiry.text}</ToneBadge>}
                  endSub={g.expiresOn ? fmtDate(g.expiresOn) : undefined}
                />
              );
            })}
          </RowList>
        );
      }}
    </Panel>
  );
}

function SeparationPanel({ state }: { state: ToolState<RoleAccess> }) {
  return (
    <Panel title="Separation rules" id="separation" state={state} notes={[]} subtitle="Who may not approve what. The engine refuses or flags an answer that breaks one.">
      {d => d.separationRules.length === 0 ? <Empty>The configuration holds no separation rule.</Empty> : (
        <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label="Separation rules">
          {d.separationRules.map(r => (
            <li key={r.separationRule.key} className="flex flex-col gap-0.5 border-t border-line pt-2 first:border-t-0 first:pt-0">
              <span className="text-sm font-semibold text-navy">{r.separationRule.label}</span>
              <span className="text-sm text-navy-2">{r.words}</span>
              <span><ToneBadge tone={r.enforcement === 'flag' ? 'warning' : 'danger'}>{r.enforcement === 'flag' ? 'Flagged' : 'Refused'}</ToneBadge></span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function AccessChecksPanel() {
  const reviews = useRead('list_reviews', { reviewTypeKeys: ['access-check'], state: 'all', top: 1 });
  const href = (state?: 'overdue' | 'due' | 'done') => hrefTo({ page: 'reviews', typeKeys: ['access-check'], ...(state ? { state } : {}) });
  return (
    <Panel title="Access checks" state={reviews} subtitle="The reviews of who can reach each application, on their own cadence.">
      {d => (
        <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm" aria-label="Access check reviews">
          <li><TextLink href={href('overdue')} aria-label={`${d.counts.overdue} overdue. Opens the list.`}>{plural(d.counts.overdue, 'overdue', 'overdue')}</TextLink>{d.counts.overdue > 0 && <Muted className="ml-1.5">past their due day</Muted>}</li>
          <li><TextLink href={href('due')} aria-label={`${d.counts.due} due. Opens the list.`}>{plural(d.counts.due, 'due', 'due')}</TextLink></li>
          <li><TextLink href={href('done')} aria-label={`${d.counts.done} done. Opens the list.`}>{plural(d.counts.done, 'done', 'done')}</TextLink></li>
        </ul>
      )}
    </Panel>
  );
}

// ---------- one role ----------

function RoleDetail({ roleKey, session }: { roleKey: string; session: Session }) {
  const one = useRead('get_role_access', { roleKey, includeHolders: true });
  const data = dataOf(one);
  const role = data?.roles[0] ?? null;
  const toolFilter = (useQueryParam('tools', TOOLS) ?? 'all') as ToolFilter;
  const toolText = useListText();
  const go = useParamNav();
  const owners = useOwnerRoleDuties();
  const types = useLabels('decision-type');
  const canPropose = session.can('propose_configuration_change');
  const bindingRows = useTool('get_configuration', canPropose ? { entity: 'role-group-binding', top: 100 } : null);
  const envelope = one.status === 'done' ? one.envelope : undefined;
  const back = <Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'roles' })}>All roles</a></Button>;

  if (envelope && !envelope.ok) {
    return (
      <>
        <PageHeader eyebrow="Roles and access" title="Role" actions={back} />
        <PageFailure failure={envelope} />
      </>
    );
  }

  const tools = role ? filterByText(role.tools.filter(t => toolMatches(t, toolFilter)), toolText, t => [toolTitle(t.tool), t.tool, String(t.number)]) : [];
  const through = role ? ownerRolesOf(role.role.key, owners.duties) : [];
  const words = new Map((data?.separationRules ?? []).map(r => [r.separationRule.key, r]));

  return (
    <>
      <PageHeader
        eyebrow="Roles and access"
        title={role?.role.label ?? 'Role'}
        status={role && (
          <span className="flex flex-wrap gap-1.5">
            {role.heldByMe && <ToneBadge tone="success">You hold it</ToneBadge>}
            {role.isWorkloadRole && <ToneBadge tone="subtle" icon={<Bot aria-hidden />}>Workload role</ToneBadge>}
            {isUnbound(role) && <ToneBadge tone="warning">No group bound</ToneBadge>}
          </span>
        )}
        subtitle={role?.duties ?? undefined}
        meta={(
          <span className="flex flex-col gap-0.5">
            {role?.party && <span>Party: {role.party.label}. {role.isReadOnly ? 'This role reads only.' : ''}{role.canChangeConfiguration ? ' It may propose changes to the configuration.' : ''}</span>}
            <span><DataStamp envelope={envelope} /></span>
          </span>
        )}
        actions={(
          <>
            {back}
            {role && canPropose && !role.isWorkloadRole && (
              <ConfigurationChangeSheet
                title={`Propose a group change for ${role.role.label}`}
                trigger={<Button size="sm">Propose a binding change</Button>}
                operations={bindingOperations(role.role.key, role.role.label, dataOf(bindingRows)?.rows ?? [])}
                rationaleHint={`Why ${role.role.label} should move to another group, or gain one, and who asked for it.`}
              />
            )}
          </>
        )}
      />

      <div className={twoColumnClass}>
        <Panel
          title="Tools it may call"
          count={role ? tools.length : undefined}
          state={one}
          notes={[]}
          subtitle="Each tool, and how far an AI client acting for a holder may go with it. The engine still checks every call."
        >
          {() => !role ? <Empty>This role is not in the configuration in force.</Empty> : role.isWorkloadRole ? (
            <Empty>A workload role calls no tool. Its holder records through the record endpoint under its grant.</Empty>
          ) : (
            <>
              <div className="flex flex-wrap items-end gap-3">
                <ListSearch label="Search this role's tools" placeholder="Tool name or number" />
                <SwitchTabs
                  label="Show"
                  ariaLabel="Which tools"
                  value={toolFilter}
                  onChange={v => go({ tools: v === 'all' ? undefined : v })}
                  options={TOOL_FILTERS.map(f => ({ value: f, label: TOOL_FILTER_TEXT[f] }))}
                />
              </div>
              {tools.length === 0 ? <Empty>No tool of this role matches.</Empty> : groupByToolset(tools).map(g => (
                <div key={g.toolset} className="flex flex-col gap-1">
                  <SubHeading>{g.label}</SubHeading>
                  <RowList label={`${g.label} tools`}>
                    {g.tools.map(t => (
                      <Row
                        key={t.tool}
                        primary={toolTitle(t.tool)}
                        secondary={aiLine(t)}
                        tertiary={t.relations.length ? `Also for ${t.relations.map(relationText).join(', or ')}.` : undefined}
                        end={(
                          <>
                            <ToneBadge tone={t.access === 'write' ? 'brand' : 'subtle'} icon={null}>{t.access === 'write' ? 'Writes' : 'Reads'}</ToneBadge>
                            {t.personOnly && <ToneBadge tone="informative" icon={<UsersRound aria-hidden />}>A person only</ToneBadge>}
                          </>
                        )}
                        endSub={`Tool ${t.number}`}
                      />
                    ))}
                  </RowList>
                </div>
              ))}
            </>
          )}
        </Panel>

        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel title={role?.isWorkloadRole ? 'Grants' : 'Groups and holders'} state={one} subtitle={role?.isWorkloadRole ? 'The workload identities that hold it, soonest expiry first.' : data?.holdersReadFrom === 'demo-personas' ? 'People hold it only through these groups. On this install the members are the demo\'s fictional people in each group (the personas and the estate\'s other people), not people read from Entra.' : 'People hold it only through these groups. The members are read live from Entra.'}>
            {d => role ? (role.isWorkloadRole ? <RoleGrants role={role} /> : <RoleHolders role={role} readFrom={d.holdersReadFrom} />) : <Empty>Nothing to show.</Empty>}
          </Panel>
          <Panel title="Decision rights" count={role?.decisionRights.length} state={one} notes={[]} subtitle="What a holder proposes, decides, advises on or is told about.">
            {() => !role?.decisionRights.length ? (
              through.length > 0 ? (
                <div className="flex flex-col gap-2">
                  <p className="m-0 text-sm text-navy-2">
                    {`No decision right names this role itself. Its holders decide through the owner roles they hold on ${subjectKindsText([...new Set(through.flatMap(d => d.ownerRole.subjectKinds))])}:`}
                  </p>
                  <RowList label="Owner roles it decides through">
                    {through.map(d => (
                      <Row
                        key={d.ownerRole.key}
                        primary={d.ownerRole.label}
                        tertiary={<OwnerRoleDuties duty={d} typeLabel={k => types.label(k)} />}
                        endSub={`${plural(d.rights.length, 'decision right', 'decision rights')}, ${plural(d.approvals.length, 'approval', 'approvals')}`}
                      />
                    ))}
                  </RowList>
                </div>
              ) : <Empty>No decision right names this role.</Empty>
            ) : (
              <RowList label="Decision rights">
                {role.decisionRights.map(r => (
                  <Row key={r.decisionRight.key} primary={r.decisionType.label} secondary={letterText(r.letter)} tertiary={r.scope === 'All' ? 'Everywhere' : r.scope} />
                ))}
              </RowList>
            )}
          </Panel>
          <Panel title="Approvals it gives" count={role?.approvals.length} state={one} notes={[]} subtitle="The approval rules that ask a holder of this role.">
            {() => !role?.approvals.length ? <Empty>No approval rule asks this role.</Empty> : (
              <RowList label="Approval rules">
                {role.approvals.map(a => (
                  <Row
                    key={a.approvalRule.key}
                    primary={a.decisionType.label}
                    secondary={a.approvalRule.label}
                    tertiary={a.scope === 'All' ? 'Everywhere' : a.scope}
                    badges={(
                      <>
                        {a.isConsultation && <ToneBadge tone="informative" icon={null}>Consulted, advice only</ToneBadge>}
                        {a.guidelineRef && <GuidelineLink number={a.guidelineRef.number} title={a.guidelineRef.title} />}
                      </>
                    )}
                  />
                ))}
              </RowList>
            )}
          </Panel>
          <Panel title="Separation rules that name it" count={role?.separationRules.length} state={one} notes={[]}>
            {() => !role?.separationRules.length ? <Empty>No separation rule names this role.</Empty> : (
              <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label="Separation rules">
                {role.separationRules.map(s => (
                  <li key={s.key} className="flex flex-col gap-0.5">
                    <span className="text-sm font-semibold text-navy">{s.label}</span>
                    {words.get(s.key) && <span className="text-sm text-navy-2">{words.get(s.key)!.words}</span>}
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
      </div>
    </>
  );
}

function RoleHolders({ role, readFrom }: { role: RoleItem; readFrom: RoleAccess['holdersReadFrom'] }) {
  const groupName = new Map(role.bindings.filter(b => b.group).map(b => [b.group!.objectId.toLowerCase(), b.group!.displayLabel]));
  const bound = role.bindings.filter(b => b.group);
  return (
    <div className="flex flex-col gap-3">
      {bound.length === 0 ? (
        <Empty>No group is bound to this role, so nobody holds it. A configuration admin proposes a binding.</Empty>
      ) : (
        <RowList label="Bound groups">
          {bound.map(b => (
            <Row
              key={b.group!.objectId}
              lead={<LeadIcon tone="brand"><UsersRound /></LeadIcon>}
              primary={<TeamLink objectId={b.group!.objectId} label={b.group!.displayLabel} />}
              secondary={b.bindingMethod ? BINDING_METHOD_TEXT[b.bindingMethod] : 'Binding method not set'}
              tertiary={b.lastVerifiedOn
                ? `Last checked ${fmtDateTime(b.lastVerifiedOn)}`
                : lastBindingCheck.asOf && lastBindingCheck.groupExists.has(role.role.key)
                  ? `Checked ${fmtDateTime(lastBindingCheck.asOf)} by the binding check: ${lastBindingCheck.groupExists.get(role.role.key) ? 'the group exists' : 'the group was not found'}`
                  : 'Not checked in this session. The platform admin runs the binding check on Roles and access.'}
            />
          ))}
        </RowList>
      )}
      <SubHeading>Holders now</SubHeading>
      {role.holders === null ? (
        <Note>{readFrom === 'not-read' ? 'The holders are not listed: members are read from Entra, and this install cannot read them now.' : 'The holders could not be read just now.'}</Note>
      ) : role.holders.length === 0 ? (
        <Empty>Nobody is in the bound groups.</Empty>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label="Holders">
          {role.holders.map(h => (
            <li key={h.objectId} className="flex flex-wrap items-baseline gap-x-2 text-sm">
              <TextLink href={hrefTo({ page: 'person', objectId: h.objectId.toLowerCase() })}>{h.displayLabel}</TextLink>
              {groupName.size > 1 && <Muted>through {groupName.get(h.viaGroup.toLowerCase()) ?? 'a bound group'}</Muted>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function RoleGrants({ role }: { role: RoleItem }) {
  const today = todayInZone();
  const grants = grantsByExpiry([role]);
  if (!grants.length) return <Empty>No workload identity holds a grant of this role.</Empty>;
  return (
    <RowList label="Grants">
      {grants.map((g, i) => {
        const expiry = grantExpiryText(g, today);
        return (
          <Row
            key={g.principal?.objectId ?? i}
            lead={<LeadIcon tone={g.isExpired ? 'danger' : 'subtle'}><Bot /></LeadIcon>}
            primary={g.principal?.displayLabel || 'A workload identity'}
            secondary={g.reason ?? undefined}
            end={<ToneBadge tone={expiry.tone}>{expiry.text}</ToneBadge>}
            endSub={g.expiresOn ? fmtDate(g.expiresOn) : undefined}
          />
        );
      })}
    </RowList>
  );
}
