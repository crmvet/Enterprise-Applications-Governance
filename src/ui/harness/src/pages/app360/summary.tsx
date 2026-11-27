// The parts Application360.tsx puts on its summary in the web app: the section bar (?tab=), the draft-application
// banner, the banner after a proposal, and the actions on the owner, document, review, change and deployment rows.
// Each reads the App 360 context and renders nothing outside it (the harness).
import { FilePen } from 'lucide-react';
import type { MouseEvent } from 'react';
import { recordHref } from '../../app/links';
import { navigate } from '../../app/navigation';
import { LinkButton } from '../../components/blocks';
import { AiMark } from '../../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Button } from '../../components/ui/button';
import type { OwnerAssignment } from '../../data/contracts';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { cn } from '../../lib/utils';
import { ProposalBanner, ProposeOwnerChangeSheet, RecordLeaveSheet } from '../owners-reviews/OwnerSheets';
import { DocumentLinkSheet } from '../Records';
import { WithdrawSheet } from '../work-approvals/parts';
import { ownsOrAdmin, useApp360 } from './context';
import { APP360_TABS, deputiesOf, holderOf, isActiveAssignment, sameId, TAB_LABELS, type App360Tab } from './logic';
import { tabHref } from './nav';
import { KeepMounted } from './parts';
import { checkedLabel, DocumentCheckedSheet, DocumentCurrentSheet, HandOverSheet } from './sheets';
import { PlanChangeButton, RecordDeploymentButton, RequestReviewButton, ReviewCompleteAction } from './work';

const TECHNICAL_OWNER = 'technical-owner';

// ---------- the section bar ----------

// The sections as links: the address carries the section, so Back returns to it and a link can be shared. A plain click
// changes the section in place and keeps the bar where it is on screen.
export function App360TabBar({ applicationId, tab }: { applicationId: string; tab: App360Tab }) {
  const go = (event: MouseEvent<HTMLAnchorElement>, href: string) => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const y = window.scrollY;
    navigate(href);
    window.scrollTo(0, y);
  };
  return (
    <nav aria-label="Application 360 sections" className="mb-4 narrow:mb-3">
      <ul className="m-0 flex list-none flex-wrap gap-1 rounded-lg border border-line bg-card p-1 shadow-xs">
        {APP360_TABS.map(t => {
          const href = tabHref(applicationId, t);
          const current = t === tab;
          return (
            <li key={t}>
              <a
                href={href}
                aria-current={current ? 'page' : undefined}
                onClick={e => go(e, href)}
                className={cn(
                  'inline-flex h-8 items-center rounded-md px-3 text-sm font-medium no-underline transition-colors focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring narrow:h-7 narrow:px-2.5 narrow:text-xs',
                  current ? 'bg-secondary text-navy ring-1 ring-primary' : 'text-navy-2 hover:bg-background hover:text-navy',
                )}
              >
                {TAB_LABELS[t]}
              </a>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

// ---------- banners ----------

// A registered draft: not in the register yet. Written by save_application_draft or a claim for a new application;
// AI drafts, a person accepts (D-28).
export function DraftBanner() {
  const ctx = useApp360();
  const app = ctx?.card?.application ?? null;
  if (!ctx || !app) return null;
  return (
    <KeepMounted show={app.status.meaning === 'draft'} className="contents">
      <DraftBannerBody />
    </KeepMounted>
  );
}

function DraftBannerBody() {
  const ctx = useApp360();
  const app = ctx?.card?.application ?? null;
  if (!ctx || !app) return null;
  const ownership = ctx.appDecisions.find(d => d.decisionType.key === 'ownership' && d.status.meaning === 'proposed') ?? null;
  const href = ownership ? recordHref(ownership.recordUrl) : null;
  return (
    <Alert variant="info" className="mb-4 narrow:mb-3">
      <FilePen aria-hidden />
      <AlertTitle>A draft application</AlertTitle>
      <AlertDescription>
        <div className="flex flex-col gap-2">
          <p className="m-0">
            It is not in the register yet. It is registered once its ownership decision is accepted, or its intake request
            is accepted at triage. Until then it counts in no portfolio figure.
          </p>
          <AiMark isAiCreated={app.isAiCreated} performedBy={app.performedBy} />
          <div className="flex flex-wrap items-center gap-2">
            {href && <Button size="sm" asChild><a href={href}>Open its ownership decision</a></Button>}
            {ctx.can('withdraw_record') && <WithdrawSheet subjectKind="application-draft" subjectId={ctx.applicationId} title={app.name} />}
          </div>
        </div>
      </AlertDescription>
    </Alert>
  );
}

export function ProposalNotice() {
  const ctx = useApp360();
  if (!ctx) return null;
  const submitted = ctx.proposal
    ? ctx.appDecisions.some(d => sameId(d.decisionId, ctx.proposal!.decisionId) && d.approvalsSummary.required > 0)
    : false;
  return <ProposalBanner proposal={ctx.proposal} onDismiss={ctx.dismissProposal} alreadySubmitted={submitted} />;
}

// ---------- owners ----------

export function OwnersPanelActions() {
  const ctx = useApp360();
  const roles = useLabels('owner-role');
  if (!ctx || !ctx.can('propose_owner_change') || !ownsOrAdmin(ctx)) return null;
  const holder = holderOf(ctx.owners, TECHNICAL_OWNER, ctx.today);
  const deputy = deputiesOf(ctx.owners, TECHNICAL_OWNER, ctx.today)[0] ?? null;
  return (
    <>
      {roles.has(TECHNICAL_OWNER) && (
        <HandOverSheet
          applicationId={ctx.applicationId}
          applicationName={ctx.applicationName}
          ownerRoleKey={TECHNICAL_OWNER}
          holder={holder}
          deputy={deputy}
          trigger={<Button size="sm" variant="outline">Hand over</Button>}
          onProposed={ctx.onProposed}
        />
      )}
      <ProposeOwnerChangeSheet
        application={{ id: ctx.applicationId, name: ctx.applicationName }}
        mode="add"
        me={ctx.session.who}
        onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
        trigger={<Button size="sm" variant="outline">Propose an owner</Button>}
      />
    </>
  );
}

// Successor, end and leave on one assignment that holds now.
export function OwnerRowActions({ assignment }: { assignment: OwnerAssignment }) {
  const ctx = useApp360();
  // The holders of this application who have a leave on record (one read for every row: the client keeps it), so a
  // person who has one is offered "Change the leave date", as on Owners (wave 4b QA walk: Ines was offered a second
  // "Record a leave").
  const leavers = useRead('list_owner_assignments', ctx && ctx.can('record_owner_leave') ? { businessApplicationId: ctx.applicationId, leaversOnly: true, top: 100 } : null);
  if (!ctx || !isActiveAssignment(assignment, ctx.today)) return null;
  const mayPropose = ctx.can('propose_owner_change') && ownsOrAdmin(ctx);
  const isPerson = assignment.principal.principalKind !== 'group';
  const mayLeave = isPerson && ctx.can('record_owner_leave')
    && (sameId(assignment.principal.objectId, ctx.session.objectId) || ctx.ownerRoles.length > 0 || ctx.session.hasRole('platform-admin'));
  if (!mayPropose && !mayLeave) return null;
  const leavesOn = dataOf(leavers)?.items.find(i => sameId(i.principal.objectId, assignment.principal.objectId) && i.holder.leavesOn)?.holder.leavesOn ?? null;
  const name = assignment.principal.displayLabel;
  // The role in each name, so two rows of one person read apart (QA walk): "End Ines Varga's assignment as data owner deputy".
  const as = `${assignment.label.charAt(0).toLowerCase()}${assignment.label.slice(1)}${assignment.isDeputy ? ' deputy' : ''}`;
  const deputy = assignment.isDeputy ? null : deputiesOf(ctx.owners, assignment.ownerRoleKey, ctx.today)[0] ?? null;
  const ending = { assignmentId: assignment.ownerAssignmentId, principal: assignment.principal, isDeputy: assignment.isDeputy };
  return (
    <span className="mt-0.5 flex flex-wrap gap-x-3 gap-y-1">
      {mayPropose && (
        <ProposeOwnerChangeSheet
          application={{ id: ctx.applicationId, name: ctx.applicationName }}
          mode="replace"
          ownerRoleKey={assignment.ownerRoleKey}
          ending={ending}
          suggested={deputy?.principal ?? null}
          me={ctx.session.who}
          onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
          trigger={<LinkButton className="text-xs" aria-label={`Propose a successor to ${name} as ${as}`}>Propose a successor</LinkButton>}
        />
      )}
      {mayPropose && (
        <ProposeOwnerChangeSheet
          application={{ id: ctx.applicationId, name: ctx.applicationName }}
          mode="end"
          ownerRoleKey={assignment.ownerRoleKey}
          ending={ending}
          me={ctx.session.who}
          onProposed={p => ctx.onProposed(p.decisionId, p.subject)}
          trigger={<LinkButton className="text-xs" aria-label={`End ${name}'s assignment as ${as}`}>End</LinkButton>}
        />
      )}
      {mayLeave && (
        <RecordLeaveSheet
          person={assignment.principal}
          currentLeavesOn={leavesOn}
          trigger={<LinkButton className="text-xs" aria-label={`${leavesOn ? 'Change the leave date of' : 'Record a leave for'} ${name}`}>{leavesOn ? 'Change the leave date' : 'Record a leave'}</LinkButton>}
        />
      )}
    </span>
  );
}

// ---------- documents ----------

// Link a missing document, confirm a draft current, or confirm a stale or current one is still true.
export function DocumentRowActions({ documentTypeKey, typeLabel, statusMeaning }: { documentTypeKey: string; typeLabel: string; statusMeaning: string }) {
  const ctx = useApp360();
  if (!ctx) return null;
  const doc = ctx.docs?.documents.find(d => d.documentTypeKey === documentTypeKey) ?? null;
  const missing = statusMeaning === 'missing' || !doc;
  // Each action stays mounted once used, so its sheet keeps the engine's answer when the document changes state.
  return (
    <>
      {ctx.can('record_document_link') && (
        <KeepMounted show={missing && statusMeaning === 'missing'} className="mt-1 flex">
          <DocumentLinkSheet app={{ businessApplicationId: ctx.applicationId }} typeLabel={typeLabel} missing={[documentTypeKey]} show />
        </KeepMounted>
      )}
      {doc && ctx.can('record_document_link') && (
        <KeepMounted show={!missing && doc.status.meaning === 'draft'} className="mt-0.5 flex">
          <DocumentCurrentSheet applicationId={ctx.applicationId} doc={doc} typeLabel={typeLabel} trigger={<LinkButton className="text-xs">Confirm it is current</LinkButton>} />
        </KeepMounted>
      )}
      {doc && ctx.can('confirm_document_checked') && (
        <KeepMounted show={!missing && doc.status.meaning !== 'draft'} className="mt-0.5 flex">
          <DocumentCheckedSheet doc={doc} typeLabel={typeLabel} trigger={<LinkButton className="text-xs" aria-label={checkedLabel(typeLabel, ctx.applicationName)}>Confirm it is still true</LinkButton>} />
        </KeepMounted>
      )}
    </>
  );
}

// ---------- reviews, changes, deployments ----------

export function ReviewsPanelActions() {
  const ctx = useApp360();
  return ctx ? <RequestReviewButton ctx={ctx} /> : null;
}

export function ReviewRowActions({ reviewId, subject, done }: { reviewId: string; subject: string; done: boolean }) {
  const ctx = useApp360();
  return ctx ? <ReviewCompleteAction ctx={ctx} reviewId={reviewId} subject={subject} done={done} /> : null;
}

export function ChangesPanelActions() {
  const ctx = useApp360();
  return ctx ? <PlanChangeButton ctx={ctx} /> : null;
}

export function DeploymentsPanelActions() {
  const ctx = useApp360();
  return ctx ? <RecordDeploymentButton ctx={ctx} /> : null;
}
