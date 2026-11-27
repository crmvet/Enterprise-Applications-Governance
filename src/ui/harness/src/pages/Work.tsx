// My work (/app/work, also /app/): what needs the signed-in person now, the home after sign-in.
// Reads: get_my_queue (drafts, AI drafts, reviews due, documents, owner gaps, claims), list_approvals view mine (the
// approvals asked of the person, with the role they are asked through and the expiry) and view asked-by-me (what the
// person proposed that waits on others), whoami (the person, their roles, the tools they may call), and for the role
// sections one light count each (intake triage and demand, reviews overdue, changes not authorized, open approvals,
// leavers). Every number opens its rows.
// Writes: submit_record, to accept an AI draft made for the person and submit it, or to submit their own draft (D-28: AI
// drafts, a person accepts). An approval is answered on its decision page, never here (D-38).
// Wave 4: renew_approval_request ("Ask again", pages/demo-admin/AskAgain.tsx) under a request in "Waiting on others"
// that expired, or has fewer than 48 hours left, for the proposer of its decision and a platform admin. The line sits
// right under the request it acts on; a request asked again from this page stays listed, marked, until the person leaves,
// so the sheet that shows the engine's answer stays open (as a submitted draft does).
// Wave 4b verify round: "Approvals asked of you" lists what the person may answer now under its title, and the requests a
// separation rule keeps them from answering apart, under "Asked of you, but another person answers". The tile and the
// panel count only the first kind (list_approvals answerableCount), as Approvals and the top bar do.
import { ArrowRight, Bot, CalendarClock, ClipboardCheck, FilePen, FileText, PackageSearch, RotateCcw, Send, UserRoundX } from 'lucide-react';
import { Fragment, useState, type ReactElement, type ReactNode } from 'react';
import { goTo, hrefTo, recordHref } from '../app/links';
import type { AppRoute } from '../app/routes';
import { DataStamp, Empty, KpiTile, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../components/blocks';
import { Row, RowList } from '../components/rows';
import { AiMark } from '../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import type { ApprovalItem, QueueItem } from '../data/contracts';
import { addDays, fmtDate, todayInZone, utcDayRange } from '../data/format';
import { dataOf, useTool } from '../data/toolClient';
import { WorkDataSection } from './data-records';
import { AskAgainSheet, askedWords, mayAskAgain, renewState } from './demo-admin';
import { ageText, answerableTotal, groupQueue, isAiMade, keptFromAnswering, QUEUE_SECTIONS, splitMine, submittableKind, type QueueSection } from './work-approvals/logic';
import { ApprovalRow, KeptFromAnswering, Sticky, SubmitSheet, useNow } from './work-approvals/parts';
import { sessionOf, useWhoami, type Session } from './work-approvals/session';

const APPROVALS_SHOWN = 25;
const QUEUE_TOP = 100;

const SECTION_ICON: Record<QueueSection, ReactElement> = {
  'ai-drafts': <Bot className="size-4" />,
  drafts: <FilePen className="size-4" />,
  reviews: <ClipboardCheck className="size-4" />,
  documents: <FileText className="size-4" />,
  'owner-gaps': <UserRoundX className="size-4" />,
  claims: <PackageSearch className="size-4" />,
};

const SECTION_ID: Record<QueueSection | 'approvals', string> = {
  approvals: 'work-approvals',
  'ai-drafts': 'work-ai-drafts',
  drafts: 'work-drafts',
  reviews: 'work-reviews',
  documents: 'work-documents',
  'owner-gaps': 'work-owner-gaps',
  claims: 'work-claims',
};

const CLEAR_ID = 'work-clear';

// Moves to a section and puts the keyboard focus on its heading, so a tile works the same with a mouse, a keyboard or a
// screen reader.
function jumpTo(id: string) {
  const heading = document.getElementById(`${id}-title`) ?? document.getElementById(id);
  if (!heading) return;
  heading.setAttribute('tabindex', '-1');
  heading.scrollIntoView({ behavior: 'smooth', block: 'start' });
  heading.focus({ preventScroll: true });
}

export function MyWork() {
  const now = useNow();
  const whoState = useWhoami();
  const session = sessionOf(whoState);
  const queue = useTool('get_my_queue', { kinds: ['draft', 'review', 'document', 'owner-gap', 'claim'], top: QUEUE_TOP });
  const approvals = useTool('list_approvals', { view: 'mine', top: APPROVALS_SHOWN });
  const askedByMe = useTool('list_approvals', { view: 'asked-by-me', top: 10 });

  const queueData = dataOf(queue);
  const groups = groupQueue(queueData?.items ?? []);
  // A draft submitted from this page leaves the queue when the page reads again. Its row stays, marked submitted, until
  // the person leaves the page, so the sheet that shows the engine's answer stays open and the person sees what moved.
  const [submitted, setSubmitted] = useState<Record<string, { item: QueueItem; section: QueueSection }>>({});
  const shownIn = (section: QueueSection): QueueItem[] => {
    const current = groups[section];
    const keys = new Set(current.map(itemKey));
    const kept = Object.values(submitted).filter(h => h.section === section && !keys.has(itemKey(h.item))).map(h => h.item);
    return [...current, ...kept];
  };
  const markSubmitted = (item: QueueItem, section: QueueSection) => setSubmitted(s => ({ ...s, [itemKey(item)]: { item, section } }));
  // Requests asked again from this page: kept in "Waiting on others" until the person leaves (the read drops them).
  const [renewed, setRenewed] = useState<Record<string, ApprovalItem>>({});
  const waitingShown = (items: ApprovalItem[]): ApprovalItem[] => {
    const ids = new Set(items.map(i => i.approvalRecordId));
    return [...items, ...Object.values(renewed).filter(i => !ids.has(i.approvalRecordId))];
  };
  const mayRenew = (item: ApprovalItem) => mayAskAgain({ ...session, isPerson: session.who?.actorKind === 'person' }, item.decision.proposedBy);
  const approvalData = dataOf(approvals);
  // What waits for the person's answer (V1): a request a separation rule keeps them from answering is not their work.
  const answerable = answerableTotal(approvalData);
  const queueFailed = queue.status === 'done' && !queue.envelope.ok;
  const count = (section: QueueSection) => (queueData ? groups[section].length : null);
  const filled = QUEUE_SECTIONS.filter(s => groups[s.key].length > 0);
  const empty = QUEUE_SECTIONS.filter(s => shownIn(s.key).length === 0);
  const nothing = !!queueData && answerable === 0 && filled.length === 0;
  const name = session.who?.principal.displayLabel;
  const approvalEnvelope = approvals.status === 'done' ? approvals.envelope : undefined;
  const tileFor = (section: QueueSection) => (count(section) ? SECTION_ID[section] : CLEAR_ID);
  const left: QueueSection[] = ['ai-drafts', 'drafts'];
  const right: QueueSection[] = ['reviews', 'documents', 'owner-gaps', 'claims'];

  return (
    <>
      <PageHeader
        eyebrow="Home"
        title="My work"
        subtitle={name ? `What needs you now, ${name}: approvals asked of you, drafts waiting for you, reviews, documents, owner gaps and claims.` : 'What needs you now: approvals asked of you, drafts waiting for you, reviews, documents, owner gaps and claims.'}
        meta={(
          <span className="flex flex-col gap-1.5">
            {session.who && session.who.productRoles.length > 0 && (
              <span className="flex flex-wrap items-center gap-1.5">
                <span>Your roles:</span>
                {session.who.productRoles.map(r => <Badge key={r.roleKey} variant="outline" className="font-medium">{r.label}</Badge>)}
              </span>
            )}
            <span><DataStamp envelope={approvalEnvelope} /></span>
          </span>
        )}
      />

      {whoState.status === 'done' && !whoState.envelope.ok && (
        <Alert variant="warning" className="mb-4">
          <AlertTitle>Your roles could not be read</AlertTitle>
          <AlertDescription>
            <p className="m-0">
              The lists below still show what needs you, but the buttons that act are hidden until the product says what you
              may do. {whoState.envelope.error.message} Reference {whoState.envelope.correlationId}.
            </p>
          </AlertDescription>
        </Alert>
      )}

      {nothing && (
        <Alert variant="success" className="mb-4">
          <AlertTitle>Nothing needs you right now</AlertTitle>
          <AlertDescription>
            <p className="m-0">
              No approval, draft, review, document, owner gap or claim waits for you. See the{' '}
              <TextLink href={hrefTo({ page: 'overview' })}>portfolio overview</TextLink> or the{' '}
              <TextLink href={hrefTo({ page: 'applications', filter: {} })}>applications register</TextLink>.
            </p>
          </AlertDescription>
        </Alert>
      )}

      <div
        className="mb-4 grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-3 narrow:grid-cols-2 narrow:gap-2.5"
        role="group"
        aria-label="What needs you, by kind"
      >
        <KpiTile
          label="Approvals"
          value={answerable ?? (approvals.status === 'done' ? 0 : null)}
          caption="Waiting for your answer"
          attention={!!answerable}
          onClick={() => jumpTo(SECTION_ID.approvals)}
        />
        {!queueFailed && (
          <>
            <KpiTile label="AI drafts" value={count('ai-drafts')} caption="Waiting for you to accept" attention={!!count('ai-drafts')} onClick={() => jumpTo(tileFor('ai-drafts'))} />
            <KpiTile label="Your drafts" value={count('drafts')} caption="Not submitted yet" onClick={() => jumpTo(tileFor('drafts'))} />
            <KpiTile label="Reviews due" value={count('reviews')} caption="Yours to do" onClick={() => jumpTo(tileFor('reviews'))} />
            <KpiTile label="Documents" value={count('documents')} caption="Missing, stale or to check" onClick={() => jumpTo(tileFor('documents'))} />
            <KpiTile label="Owner gaps" value={count('owner-gaps')} caption="On your applications" attention={!!count('owner-gaps')} onClick={() => jumpTo(tileFor('owner-gaps'))} />
            <KpiTile label="Claims" value={count('claims')} caption="Suggested to you" onClick={() => jumpTo(tileFor('claims'))} />
          </>
        )}
      </div>

      <div className="grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel
            title="Approvals asked of you"
            subtitle="Open one to answer it on its decision page. Your answer is final; an AI client never answers for you."
            state={approvals}
            count={answerable ?? undefined}
            id={SECTION_ID.approvals}
            skeletonRows={4}
            actions={<PanelLink route={{ page: 'approvals' }}>All approvals</PanelLink>}
          >
            {data => {
              if (!data.items.length) return <Empty>No approval request waits for you.</Empty>;
              const split = splitMine(data.items);
              const more = data.totalCount - data.items.length;
              return (
                <>
                  {split.answerable.length > 0 ? (
                    <RowList label="Approvals asked of you">
                      {split.answerable.map(item => <ApprovalRow key={item.approvalRecordId} item={item} now={now} view="mine" />)}
                    </RowList>
                  ) : <Empty>No approval request waits for your answer.</Empty>}
                  {split.others.length > 0 && <KeptFromAnswering items={split.others} now={now} count={keptFromAnswering(data) || split.others.length} />}
                  {more > 0 && (
                    <Muted>
                      {more} more {more === 1 ? 'is' : 'are'} asked of you.{' '}
                      <TextLink href={hrefTo({ page: 'approvals' })}>Open them all</TextLink>.
                    </Muted>
                  )}
                </>
              );
            }}
          </Panel>

          {queueFailed && queue.status === 'done' && !queue.envelope.ok && (
            <Panel title="Drafts, reviews, documents, owner gaps and claims">
              <PanelFailure failure={queue.envelope} />
            </Panel>
          )}
          {queue.status === 'loading' && <Panel title="Your drafts and AI drafts" state={queue} skeletonRows={3}>{() => null}</Panel>}
          {left.filter(s => shownIn(s).length).map(s => (
            <QueueSectionPanel key={s} section={s} items={shownIn(s)} submitted={submitted} onSubmitted={markSubmitted} session={session} now={now} more={!!queueData?.skipToken} />
          ))}

          <Panel
            title="Waiting on others"
            subtitle="Approval requests still open on what you proposed or requested, and who each waits for."
            state={askedByMe}
            count={dataOf(askedByMe)?.totalCount}
            skeletonRows={2}
            actions={<PanelLink route={{ page: 'approvals', view: 'asked-by-me' }}>All of them</PanelLink>}
          >
            {data => data.items.length ? (
              <>
                <RowList label="Waiting on others">
                  {waitingShown(data.items).map(item => (
                    <Fragment key={item.approvalRecordId}>
                      <ApprovalRow item={item} now={now} view="asked-by-me" />
                      <AskAgainLine
                        item={item}
                        now={now}
                        allowed={mayRenew(item)}
                        renewed={!!renewed[item.approvalRecordId]}
                        onRenewed={() => setRenewed(r => ({ ...r, [item.approvalRecordId]: item }))}
                      />
                    </Fragment>
                  ))}
                </RowList>
                {data.totalCount > data.items.length && (
                  <Muted>
                    {data.totalCount - data.items.length} more.{' '}
                    <TextLink href={hrefTo({ page: 'approvals', view: 'asked-by-me' })}>Open the list</TextLink>.
                  </Muted>
                )}
              </>
            ) : <Empty>Nothing you proposed or requested waits for an answer.</Empty>}
          </Panel>
        </div>

        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          {right.filter(s => shownIn(s).length).map(s => (
            <QueueSectionPanel key={s} section={s} items={shownIn(s)} submitted={submitted} onSubmitted={markSubmitted} session={session} now={now} more={!!queueData?.skipToken} />
          ))}
          {/* The data section (D-37): the data approvals, the domains the person owns, their gaps. pages/data-records. */}
          <WorkDataSection />
          <RoleSections session={session} />
          {queueData && empty.length > 0 && (
            <Panel title="Clear" subtitle="Nothing waits for you here." id={CLEAR_ID}>
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                {empty.map(s => (
                  <li key={s.key} className="flex items-start gap-2 text-sm text-navy-2">
                    <span className="mt-0.5 text-navy-3" aria-hidden>{SECTION_ICON[s.key]}</span>
                    <span><span className="font-medium text-navy">{s.title}:</span> {s.empty}</span>
                  </li>
                ))}
              </ul>
            </Panel>
          )}
        </div>
      </div>
    </>
  );
}

// "Ask again" under a request that waits on others: shown to the proposer of its decision and a platform admin while the
// request expired or has fewer than 48 hours left; marked once asked again from this page. Nothing for anyone else.
function AskAgainLine({ item, now, allowed, renewed, onRenewed }: { item: ApprovalItem; now: number; allowed: boolean; renewed: boolean; onRenewed: () => void }) {
  const state = renewState(item, item.decision.status.meaning, now);
  if (!state || (!allowed && !renewed)) return null;
  const asked = askedWords({ askedPerson: item.askedPrincipal, askedRole: item.askedRole, askedOwnerRole: item.askedOwnerRole, isConsultation: item.isConsultation });
  return (
    <li className="-mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 pb-2.5 pl-11 text-xs text-navy-2">
      {renewed ? (
        <span className="flex items-center gap-1.5 font-medium text-navy">
          <RotateCcw className="size-3.5 text-navy-3" aria-hidden />
          Asked again from this page. The open request is listed in this panel.
        </span>
      ) : <span>{state === 'expired' ? 'It expired with no answer.' : 'Fewer than 48 hours are left to answer.'}</span>}
      <Sticky show={!renewed}>
        <AskAgainSheet
          approvalRecordId={item.approvalRecordId}
          state={state}
          asked={asked}
          expiresOn={item.expiresOn}
          decision={{ recordNumber: null, statement: item.decision.statement }}
          onDone={onRenewed}
        />
      </Sticky>
    </li>
  );
}

function PanelLink({ route, children }: { route: AppRoute; children: ReactNode }) {
  return (
    <TextLink href={hrefTo(route)} className="inline-flex items-center gap-x-1 text-sm font-semibold">
      {children} <ArrowRight className="size-4" aria-hidden />
    </TextLink>
  );
}

// ---------- one section of the queue ----------

const SECTION_SUBTITLE: Record<QueueSection, string> = {
  'ai-drafts': 'An AI client drafted these for you. Nothing goes anywhere until you accept and submit it.',
  drafts: 'Your own drafts and proposals. Nobody is asked until you submit them.',
  reviews: 'Reviews of applications you own, and reviews whose outcome you may propose.',
  documents: 'Required documents of your applications that are missing, stale, or waiting for your check.',
  'owner-gaps': 'Owner roles on your applications with no holder or no deputy.',
  claims: 'Things the gap check found that it suggests you own.',
};

function itemKey(item: QueueItem): string {
  return `${item.kind}|${item.subjectKind}|${item.subjectId}|${item.title}`;
}

function QueueSectionPanel(props: {
  section: QueueSection;
  items: QueueItem[];
  submitted: Record<string, unknown>;
  onSubmitted: (item: QueueItem, section: QueueSection) => void;
  session: Session;
  now: number;
  more: boolean;
}) {
  const { section, items, session, now, more } = props;
  const meta = QUEUE_SECTIONS.find(s => s.key === section)!;
  const waiting = items.filter(i => !props.submitted[itemKey(i)]).length;
  return (
    <Panel title={meta.title} subtitle={SECTION_SUBTITLE[section]} count={waiting} id={SECTION_ID[section]}>
      <RowList label={meta.title}>
        {items.map(item => (
          <QueueRow
            key={itemKey(item)}
            item={item}
            section={section}
            session={session}
            now={now}
            submitted={!!props.submitted[itemKey(item)]}
            onSubmitted={() => props.onSubmitted(item, section)}
          />
        ))}
      </RowList>
      {more && <Muted>The queue holds more items than this page reads at once. Finish these, then read the page again.</Muted>}
    </Panel>
  );
}

// Where an item opens: the page where its action lives.
function itemHref(item: QueueItem): string | null {
  const app = item.businessApplicationId;
  switch (item.kind) {
    case 'draft':
      if (item.subjectKind === 'intake-request') return hrefTo({ page: 'intake-edit', id: item.subjectId });
      if (item.subjectKind === 'business-application') return hrefTo({ page: 'app', id: item.subjectId });
      return recordHref(item.recordUrl);
    case 'owner-gap':
      return app ? hrefTo({ page: 'owners', view: 'gaps', applicationId: app }) : recordHref(item.recordUrl);
    default:
      return recordHref(item.recordUrl);
  }
}

function actionText(item: QueueItem): string {
  switch (item.kind) {
    case 'draft':
      return item.subjectKind === 'intake-request' ? 'Open in the wizard' : 'Open';
    case 'review':
      return 'Open the review';
    case 'document':
      if (item.status?.meaning === 'missing') return 'Add the link';
      if (item.status?.meaning === 'stale') return 'Check it';
      return 'Confirm it';
    case 'owner-gap':
      return 'Fix the gap';
    case 'claim':
      return 'Open the claim';
    default:
      return 'Open';
  }
}

function QueueRow({ item, section, session, now, submitted, onSubmitted }: { item: QueueItem; section: QueueSection; session: Session; now: number; submitted: boolean; onSubmitted: () => void }) {
  const href = itemHref(item);
  const linkId = `work-item-${item.kind}-${item.subjectId}`;
  const ai = isAiMade(item.isAiCreated, item.performedBy);
  const kind = submittableKind(item);
  const canSubmit = kind !== null && session.can('submit_record');
  const drafted = ai ? ageText(item.performedBy?.stampedOn, now) : null;
  // A review past its due date is overdue, as the Reviews page says, whatever status word the row carries.
  const overdue = section === 'reviews' && !!item.dueOn && item.dueOn.slice(0, 10) < todayInZone(new Date(now));
  const details = [
    item.dueOn ? (overdue ? `Overdue since ${fmtDate(item.dueOn)}` : `Due ${fmtDate(item.dueOn)}`) : null,
    drafted ? `Drafted ${drafted}` : null,
  ].filter(Boolean).join('. ');
  return (
    <Row
      lead={<span className="grid size-8 place-items-center rounded-full bg-secondary text-navy-2" aria-hidden>{section === 'reviews' && item.dueOn ? <CalendarClock className="size-4" /> : SECTION_ICON[section]}</span>}
      primary={href ? <TextLink id={linkId} href={href}>{item.title}</TextLink> : item.title}
      secondary={submitted ? 'Submitted from this page. The people the rules name are asked now.' : item.why}
      tertiary={details || undefined}
      badges={ai || overdue ? <>{overdue && <ToneBadge tone="danger">Overdue</ToneBadge>}{ai && <AiMark isAiCreated={item.isAiCreated} performedBy={item.performedBy} />}</> : undefined}
      end={submitted
        ? <ToneBadge tone="success" icon={<Send aria-hidden />}>Submitted</ToneBadge>
        : item.status ? <StatusBadge status={item.status} /> : undefined}
      endSub={(
        <span className="mt-1 flex flex-wrap justify-end gap-1.5 narrow:justify-start">
          {href && (
            <Button asChild size="sm" variant="outline">
              <a href={href} aria-label={`${submitted ? 'Open' : actionText(item)}: ${item.title}`}>{submitted ? 'Open' : actionText(item)}</a>
            </Button>
          )}
          {kind && (
            <Sticky show={canSubmit && !submitted} focusTarget={() => document.getElementById(linkId)}>
              <SubmitSheet subjectKind={kind} subjectId={item.subjectId} title={item.title} isAiDraft={ai} performedBy={item.performedBy} onDone={onSubmitted} />
            </Sticky>
          )}
        </span>
      )}
      wideEnd
    />
  );
}

// ---------- what the person's roles look after ----------

function RoleSections({ session }: { session: Session }) {
  if (!session.who) return null;
  const tiles: ReactElement[] = [];
  if (session.hasRole('platform-admin')) tiles.push(<TriageTile key="triage" />);
  if (session.hasRole('pmo')) tiles.push(<DemandTile key="demand" />);
  if (session.hasRole('change-manager')) tiles.push(<ChangesTile key="changes" />);
  if (session.hasRole('architect', 'security-reviewer', 'pmo')) tiles.push(<ReviewsOverdueTile key="reviews" />);
  if (session.hasRole('platform-admin')) tiles.push(<LeaversTile key="leavers" />);
  if (session.hasRole('auditor', 'platform-admin', 'configuration-admin', 'change-board')) tiles.push(<OpenApprovalsTile key="open" />);
  if (!tiles.length) return null;
  return (
    <Panel title="For your roles" subtitle="What your roles look after across the register. Each number opens its list.">
      <div className="grid grid-cols-2 gap-2.5" role="group" aria-label="For your roles">{tiles}</div>
    </Panel>
  );
}

// A count that opens its list. A failed read shows a short note in place of a number, never a number the server did not send.
function RoleTile({ label, caption, value, failed, route, attention }: { label: string; caption: string; value: number | null; failed: string | null; route: AppRoute; attention?: boolean }) {
  if (failed) {
    return (
      <div className="flex flex-col gap-0.5 rounded-xl border bg-card px-3.5 py-3 text-sm">
        <span className="font-semibold text-navy-2">{label}</span>
        <Muted>Could not be read. {failed}</Muted>
        <TextLink href={hrefTo(route)} className="text-xs">Open the list</TextLink>
      </div>
    );
  }
  return <KpiTile label={label} caption={caption} value={value} attention={attention && !!value} onClick={() => goTo(route)} className="py-3 pl-3.5" />;
}

function failureText(state: { status: 'loading' } | { status: 'done'; envelope: { ok: boolean; correlationId: string } }): string | null {
  return state.status === 'done' && !state.envelope.ok ? `Reference ${state.envelope.correlationId}.` : null;
}

function TriageTile() {
  const s = useTool('list_intake_requests', { view: 'triage', top: 1 });
  return <RoleTile label="Requests to triage" caption="Submitted, waiting for triage" value={dataOf(s)?.totalCount ?? null} failed={failureText(s)} route={{ page: 'intake', view: 'triage' }} attention />;
}

function DemandTile() {
  const s = useTool('list_intake_requests', { view: 'demand', top: 1 });
  return <RoleTile label="Demand list" caption="Requests to score and prioritize" value={dataOf(s)?.totalCount ?? null} failed={failureText(s)} route={{ page: 'intake', view: 'demand' }} />;
}

function ReviewsOverdueTile() {
  const s = useTool('list_reviews', { state: 'overdue', top: 1 });
  return <RoleTile label="Reviews overdue" caption="Across the register" value={dataOf(s)?.counts.overdue ?? null} failed={failureText(s)} route={{ page: 'reviews', state: 'overdue' }} attention />;
}

function LeaversTile() {
  const s = useTool('list_owner_assignments', { leaversOnly: true, top: 1 });
  return <RoleTile label="Owners leaving" caption="Leave dates, or gone from the directory" value={dataOf(s)?.totalCount ?? null} failed={failureText(s)} route={{ page: 'owners', view: 'leavers' }} attention />;
}

function OpenApprovalsTile() {
  const s = useTool('list_approvals', { view: 'open', top: 1 });
  return <RoleTile label="Open approvals" caption="Every pending request" value={dataOf(s)?.totalCount ?? null} failed={failureText(s)} route={{ page: 'approvals', view: 'open' }} />;
}

// Changes in the next 14 days that are not authorized yet. The page counts the rows of one read; when the schedule holds
// more than one read returns, the caption says so.
function ChangesTile() {
  const today = todayInZone();
  const s = useTool('get_change_schedule', { ...utcDayRange(today, addDays(today, 14)), top: 100 });
  const data = dataOf(s);
  const value = data ? data.items.filter(c => !c.authorized).length : null;
  return (
    <RoleTile
      label="Changes to authorize"
      caption={data?.skipToken ? 'Next 14 days, first 100 changes read' : 'Not authorized yet, next 14 days'}
      value={value}
      failed={failureText(s)}
      route={{ page: 'changes', view: 'schedule' }}
      attention
    />
  );
}
