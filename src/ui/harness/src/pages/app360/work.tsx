// Changes and deployments (P03, P05, A-DELIVERY) and reviews (P04, P07) of one application, in full: every change with
// its window and authorization, every deployment with its route, flags and the gate level applied, every review with
// what triggered it. Plan a change and record a deployment open their own pages with this application; a review is
// requested or completed here. Every number opens its list, filtered to this application; the change view is in the
// address (?changes=all), so Back keeps it.
// Reads: get_change_schedule, get_deployments, list_reviews (all for this application), get_review per open review the
// person may complete.
import { ArrowRight, CalendarPlus, Plus, Rocket } from 'lucide-react';
import { useState } from 'react';
import { goTo, hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../../app/links';
import { navigate, useLocationKey } from '../../app/navigation';
import { BarList, Empty, KpiTile, LinkButton, Muted, MoreRowsNote, Panel, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { DeploymentFlags, DeploymentItem } from '../../data/contracts';
import { addDays, fmtDate, fmtDateTime, fmtWindow, utcDayRange } from '../../data/format';
import { useLabels } from '../../data/labels';
import { flagText } from '../../data/meanings';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { gateLevelLabel, isClosed } from '../changes-records/logic';
import { FlagBadges } from '../changes-records/parts';
import { triggerText } from '../owners-reviews/model';
import { CompleteReviewSheet } from '../owners-reviews/ReviewSheets';
import { ChangeTypeBadge, KeepMounted } from './parts';
import { useApp360, type App360Context } from './context';
import { reviewOrder } from './logic';
import { ArchitectureReviewSheet } from './sheets';

type ChangeView = 'open' | 'all';

const twoColumns = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const column = 'flex min-w-0 flex-col gap-4 narrow:gap-3';
const tiles = 'grid grid-cols-3 gap-2.5 narrow:grid-cols-1';

export function WorkSection() {
  const ctx = useApp360();
  if (!ctx) return null;
  return (
    <div className={twoColumns}>
      <div className={column}>
        <ChangesPanel ctx={ctx} />
        <DeploymentsPanel ctx={ctx} />
      </div>
      <div className={column}>
        <ReviewsPanel ctx={ctx} />
      </div>
    </div>
  );
}

// ---------- changes ----------

export function PlanChangeButton({ ctx, size = 'sm' }: { ctx: App360Context; size?: 'sm' | 'default' }) {
  if (!ctx.can('save_change_request_draft')) return null;
  return (
    <Button size={size} variant="outline" asChild>
      <a href={hrefTo({ page: 'change-new', applicationId: ctx.applicationId })}><CalendarPlus aria-hidden />Plan a change</a>
    </Button>
  );
}

export function RecordDeploymentButton({ ctx, size = 'sm' }: { ctx: App360Context; size?: 'sm' | 'default' }) {
  if (!ctx.can('record_deployment')) return null;
  return (
    <Button size={size} variant="outline" asChild>
      <a href={hrefTo({ page: 'deployment-new', applicationId: ctx.applicationId })}><Rocket aria-hidden />Record a deployment</a>
    </Button>
  );
}

function ChangesPanel({ ctx }: { ctx: App360Context }) {
  const view: ChangeView = useQueryParam('changes', /^(open|all)$/) === 'all' ? 'all' : 'open';
  const location = useLocationKey();
  // The schedule asks for a range: a year back and a year ahead, in UTC dates. A change with no window yet is not on it.
  const state = useRead('get_change_schedule', { businessApplicationId: ctx.applicationId, ...utcDayRange(addDays(ctx.today, -365), addDays(ctx.today, 365)), top: 100 });
  const all = dataOf(state)?.items ?? [];
  const shown = view === 'open' ? all.filter(c => !isClosed(c.status.meaning)) : all;
  const setView = (next: ChangeView) => {
    const y = window.scrollY;
    navigate(withParams(location, { changes: next === 'open' ? undefined : next }));
    window.scrollTo(0, y);
  };
  return (
    <Panel
      title="Changes"
      subtitle="Windows a year back and a year ahead, their authorization and their result. A draft with no window yet is not listed."
      state={state}
      count={dataOf(state) ? shown.length : undefined}
      skeletonRows={4}
      actions={<PlanChangeButton ctx={ctx} />}
    >
      {d => (
        <div className="flex flex-col gap-2.5">
          <SwitchTabs<ChangeView> label="Show" ariaLabel="Which changes" value={view} onChange={setView} options={[{ value: 'open', label: 'Open' }, { value: 'all', label: 'All' }]} />
          {shown.length ? (
            <RowList label="Changes">
              {shown.map(c => (
                <Row
                  key={c.changeRequestId}
                  primary={<TextLink href={recordHrefOf('change-request', c.changeRequestId) ?? '#'}>{c.title}</TextLink>}
                  secondary={[fmtWindow(c.windowStart, c.windowEnd), c.target?.label].filter(Boolean).join(', ')}
                  tertiary={c.authorized ? 'Authorized' : c.authorizationStatus ? `Authorization ${c.authorizationStatus.label.toLowerCase()}` : 'No authorization decision yet'}
                  wideEnd
                  end={<><ChangeTypeBadge type={c.changeType} /><StatusBadge status={c.status} /></>}
                />
              ))}
            </RowList>
          ) : <Empty>{view === 'open' ? 'No open change.' : 'No change is recorded for this application.'}</Empty>}
          <MoreRowsNote skipToken={d.skipToken} what="changes" />
          <TextLink href={hrefTo({ page: 'changes', view: 'list', applicationId: ctx.applicationId })} className="inline-flex items-center gap-1 self-start text-sm">Changes of this application <ArrowRight className="size-3.5" aria-hidden /></TextLink>
        </div>
      )}
    </Panel>
  );
}

// ---------- deployments ----------

const FLAG_KEYS: { key: keyof DeploymentFlags; route: 'outside-window' | 'other-path' | 'missing-approval' | 'ai-only-code-approval'; count: keyof DeploymentsCounts }[] = [
  { key: 'missingApproval', route: 'missing-approval', count: 'missingApproval' },
  { key: 'outsideWindow', route: 'outside-window', count: 'outsideWindow' },
  { key: 'otherPath', route: 'other-path', count: 'otherPath' },
  { key: 'aiOnlyCodeApproval', route: 'ai-only-code-approval', count: 'aiOnlyCodeApproval' },
];

type DeploymentsCounts = { records: number; flagged: number; outsideWindow: number; otherPath: number; missingApproval: number; aiOnlyCodeApproval: number };

export function DeploymentLine({ item }: { item: Pick<DeploymentItem, 'route' | 'gateLevelApplied'> }) {
  const routes = useLabels('deployment-route');
  return <>{`${routes.label(item.route.key)}. Gate ${gateLevelLabel[item.gateLevelApplied]?.toLowerCase() ?? item.gateLevelApplied}`}</>;
}

function DeploymentsPanel({ ctx }: { ctx: App360Context }) {
  const state = useRead('get_deployments', { businessApplicationId: ctx.applicationId, latestOnly: false, top: 25 });
  const data = dataOf(state);
  return (
    <Panel
      title="Deployments"
      subtitle="Every version that reached a target, newest first, with the flags the engine raised."
      state={state}
      count={data?.totalCount}
      skeletonRows={5}
      actions={<RecordDeploymentButton ctx={ctx} />}
    >
      {d => (
        <div className="flex flex-col gap-3">
          <div className={tiles}>
            <KpiTile label="Recorded" value={d.counts.records} caption="Every deployment of it" onClick={() => goTo({ page: 'deployments', view: 'all', applicationId: ctx.applicationId })} />
            <KpiTile label="Flagged" value={d.counts.flagged} caption="At least one flag" attention={d.counts.flagged > 0} onClick={() => goTo({ page: 'deployments', view: 'flagged', applicationId: ctx.applicationId })} />
          </div>
          {d.counts.flagged > 0 && (
            <BarList
              ariaLabel="Flags on its deployments"
              noun={['deployment', 'deployments']}
              labelWidth={170}
              items={FLAG_KEYS.map(f => ({ key: f.route, label: flagText[f.key], count: d.counts[f.count] }))}
              onDrill={item => goTo({ page: 'deployments', view: 'flagged', applicationId: ctx.applicationId, flagKeys: item.key ? [item.key as (typeof FLAG_KEYS)[number]['route']] : undefined })}
            />
          )}
          {d.items.length ? (
            <RowList label="Deployments">
              {d.items.map(x => {
                const href = recordHref(x.recordUrl) ?? recordHrefOf('deployment-record', x.deploymentRecordId);
                return (
                  <Row
                    key={x.deploymentRecordId}
                    primary={href ? <TextLink href={href}>{`${x.artifactName} ${x.artifactVersion}`}</TextLink> : `${x.artifactName} ${x.artifactVersion}`}
                    secondary={<>{x.targetLabel ?? 'A target not registered'}. <DeploymentLine item={x} /></>}
                    tertiary={x.changeRequestId ? <>For <TextLink href={recordHrefOf('change-request', x.changeRequestId) ?? '#'}>its change</TextLink></> : 'No change matched'}
                    badges={<><FlagBadges flags={x.flags} /><AiMark isAiCreated={x.isAiCreated} performedBy={x.performedBy} /></>}
                    end={<StatusBadge status={x.result} />}
                    endSub={fmtDateTime(x.finishedOn)}
                  />
                );
              })}
            </RowList>
          ) : <Empty>Nothing deployed yet.</Empty>}
          <MoreRowsNote skipToken={d.skipToken} what="deployments" />
        </div>
      )}
    </Panel>
  );
}

// ---------- reviews ----------

export function RequestReviewButton({ ctx }: { ctx: App360Context }) {
  if (!ctx.can('request_architecture_review')) return null;
  return (
    <ArchitectureReviewSheet
      applicationId={ctx.applicationId}
      applicationName={ctx.applicationName}
      changes={(ctx.card?.changes ?? []).map(c => ({ changeRequestId: c.changeRequestId, title: c.title }))}
      trigger={<Button size="sm" variant="outline"><Plus aria-hidden />Request an architecture review</Button>}
    />
  );
}

// Complete a review the person may close, once its outcome decision is accepted (get_review canComplete). Read only for a
// person who may call complete_review, and only for a review not done.
export function ReviewCompleteAction({ ctx, reviewId, subject, done }: { ctx: App360Context; reviewId: string; subject: string; done: boolean }) {
  // Once the person opens the sheet, the review is read on after it is done, so the sheet keeps the engine's answer.
  const [used, setUsed] = useState(false);
  const may = (!done || used) && ctx.can('complete_review');
  const state = useRead('get_review', may ? { reviewId } : null);
  const detail = dataOf(state);
  if (!may || !detail) return null;
  const outcome = detail.outcomeDecision;
  return (
    <>
      {!done && !detail.canComplete && detail.yourPart && detail.cannotComplete && <Muted>{detail.cannotComplete}</Muted>}
      {(detail.canComplete || used) && (
        <KeepMounted show={!done && detail.canComplete}>
          <span className="contents" onClickCapture={() => setUsed(true)}>
            <CompleteReviewSheet
              reviewId={reviewId}
              subject={subject}
              outcome={outcome ? `${outcome.decisionType.label.toLowerCase()}, ${outcome.statement}` : 'the decision it records.'}
              nextDue={detail.nextDueIfDoneToday ? fmtDate(detail.nextDueIfDoneToday) : null}
              today={ctx.today}
              trigger={<LinkButton className="text-xs">Complete the review</LinkButton>}
            />
          </span>
        </KeepMounted>
      )}
    </>
  );
}

function ReviewsPanel({ ctx }: { ctx: App360Context }) {
  const state = useRead('list_reviews', { state: 'all', businessApplicationId: ctx.applicationId, top: 50 });
  const data = dataOf(state);
  const go = (s: 'overdue' | 'due' | 'done') => goTo({ page: 'reviews', state: s, applicationId: ctx.applicationId });
  return (
    <Panel
      title="Reviews"
      subtitle="Due, overdue and done, with what opened each."
      state={state}
      count={data?.totalCount}
      skeletonRows={5}
      actions={<RequestReviewButton ctx={ctx} />}
    >
      {d => (
        <div className="flex flex-col gap-3">
          <div className={tiles}>
            <KpiTile label="Overdue" value={d.counts.overdue} caption="Past their due date" attention={d.counts.overdue > 0} onClick={() => go('overdue')} />
            <KpiTile label="Due" value={d.counts.due} caption="Open, not late" onClick={() => go('due')} />
            <KpiTile label="Done" value={d.counts.done} caption="Completed" onClick={() => go('done')} />
          </div>
          {d.items.length ? (
            <RowList label="Reviews">
              {reviewOrder(d.items).map(r => {
                const href = recordHref(r.recordUrl) ?? recordHrefOf('review', r.reviewId);
                const name = `${r.reviewType.label}${r.recordNumber ? ` ${r.recordNumber}` : ''}`;
                const done = r.status.meaning === 'done';
                return (
                  <Row
                    key={r.reviewId}
                    primary={href ? <TextLink href={href}>{name}</TextLink> : name}
                    secondary={done ? `Done ${fmtDate(r.doneOn)}${r.reviewer ? ` by ${r.reviewer.displayLabel}` : ''}` : `Due ${fmtDate(r.dueOn)}. ${triggerText(r.trigger)}`}
                    tertiary={<ReviewCompleteAction ctx={ctx} reviewId={r.reviewId} subject={name} done={done} />}
                    badges={<>{r.isOverdue && <ToneBadge tone="danger">Overdue</ToneBadge>}<AiMark isAiCreated={r.isAiCreated} performedBy={r.performedBy} /></>}
                    end={<StatusBadge status={r.status} />}
                  />
                );
              })}
            </RowList>
          ) : <Empty>No review is recorded for this application.</Empty>}
          <MoreRowsNote skipToken={d.skipToken} what="reviews" />
        </div>
      )}
    </Panel>
  );
}
