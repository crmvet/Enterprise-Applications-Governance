// The acts on a change request, each in the shared side sheet, and the checks a person sees before submitting:
//   BeforeSubmit        the standard change check (check_standard_change), who will be asked to authorize it at this
//                       tier and type (get_requirements), the gate level at the planned target, and the direct
//                       dependants the draft does not list. check_change_approval is a Later tool, so the approvals
//                       come from the approval rules the engine resolves for the change's scope.
//   SubmitChangeSheet   submit_record: the engine opens the authorization decision and asks the approvers.
//   WithdrawChangeSheet withdraw_record: open approval requests are canceled.
//   RecordResultSheet   record_change_result: how the change ended, as reported, with evidence links.
//   CompleteChangeSheet complete_change (person only): closes an authorized change with its result; the engine opens the
//                       post-change review once when the change type or the result asks for one (document 23, B23-10).
import { CircleCheck, CircleSlash, Info, TriangleAlert } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import type { ApplicationCard, ApprovalNeeded, ChangeRequestDetail, GateLevel, Requirements, ScheduledChange, StandardChangeCheck, StatusMeaning } from '../../data/contracts';
import { useLabels, useListValues } from '../../data/labels';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { ActionSheet, Field } from '../../components/actions';
import { Empty, Muted, Panel, PanelFailure, TextLink, ToneBadge } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Textarea } from '../../components/ui/textarea';
import { recordHrefOf } from '../../app/links';
import { gateLevelLabel, gateLevelText, isHttpsUrl, sameId, suggestResult } from './logic';
import { ApprovalNeededList, choicesOf, SelectField, subhead, useSheet } from './parts';

type AppFacts = ApplicationCard['application'];

// ---------- the planned target ----------

// The planned target's name and stage. The change request read gives only its ID: the schedule names the target of a
// change with a window, and otherwise the targets the application has deployed to name it.
export function useTargetOf(change: ChangeRequestDetail['change'] | null): { target: { label: string; stageKey: string | null } | null; reading: boolean; scheduled: ScheduledChange | null } {
  const sched = useTool('get_change_schedule', change?.windowStart ? {
    from: change.windowStart.slice(0, 10),
    to: (change.windowEnd ?? change.windowStart).slice(0, 10),
    businessApplicationId: change.businessApplicationId,
    top: 100,
  } : null);
  const scheduled = dataOf(sched)?.items.find(i => sameId(i.changeRequestId, change?.changeRequestId)) ?? null;
  const needTargets = !!change?.plannedTargetId && (!change.windowStart || (sched.status === 'done' && !scheduled?.target));
  const targets = useTool('get_deployments', needTargets && change ? { businessApplicationId: change.businessApplicationId, latestOnly: true, top: 100 } : null);
  const row = dataOf(targets)?.items.find(d => sameId(d.deploymentTargetId, change?.plannedTargetId));
  const target = !change?.plannedTargetId ? null
    : scheduled?.target ? { label: scheduled.target.label, stageKey: scheduled.target.targetStageKey }
      : row ? { label: row.targetLabel ?? 'Unnamed target', stageKey: row.targetStageKey } : null;
  const reading = !!change?.plannedTargetId && !target && ((!!change.windowStart && sched.status === 'loading') || (needTargets && targets.status === 'loading'));
  return { target, reading, scheduled };
}

// "a normal", "an emergency".
const withArticle = (word: string) => `${/^[aeiou]/i.test(word) ? 'an' : 'a'} ${word}`;

// ---------- who will authorize it ----------

export interface ApprovalPreview {
  requirements: ToolState<Requirements>;
  approvals: ApprovalNeeded[];
  requiresAuthorization: boolean | undefined;
  // Who will be asked, for the submit's confirm step.
  asked: ReactNode;
}

// The authorization a change will need, from the approval rules the engine resolves for its scope: the application's
// tier, kind and stage, and the change type. check_change_approval is a Later tool, so this is the read before submit.
export function useApprovalPreview(change: ChangeRequestDetail['change'] | null, app: AppFacts | null): ApprovalPreview {
  const types = useLabels('change-type');
  const requiresAuthorization = change ? types.field<boolean>(change.changeType.key, 'requiresAuthorization') : undefined;
  const scope = change && app?.tier
    ? {
      tierKey: app.tier.key,
      applicationKindKey: app.kind.key,
      ...(app.stage ? { lifecycleStageKey: app.stage.key } : {}),
      changeTypeKey: change.changeType.key,
    }
    : null;
  const requirements = useTool('get_requirements', scope ? { scope } : null);
  const approvals = (dataOf(requirements)?.approvals ?? []).filter(a => a.decisionTypeKey === 'change-authorization');
  const asked: ReactNode = change?.mode === 'linked'
    ? <p className="m-0">Linked mode: no built-in approval starts.</p>
    : requiresAuthorization === false
      ? <p className="m-0">No authorization is asked when a standard model fits.</p>
      : approvals.length
        ? <><p className="m-0 mb-1 font-semibold text-navy">Who will be asked</p><ApprovalNeededList items={approvals} empty="" /></>
        : null;
  return { requirements, approvals, requiresAuthorization, asked };
}

// ---------- before you submit ----------

export function BeforeSubmit(props: {
  detail: ChangeRequestDetail;
  app: AppFacts | null;
  targetStageKey: string | null;
  preview: ApprovalPreview;
  title?: string;
  // Under the checks: the acts, or a note on who may act.
  footer?: ReactNode;
}) {
  const { detail, app } = props;
  const change = detail.change;
  const { requirements, approvals, requiresAuthorization } = props.preview;
  const standard = useTool('check_standard_change', { changeRequestId: change.changeRequestId });
  const walk = useTool('get_dependency_impact', { businessApplicationId: change.businessApplicationId, direction: 'dependants', depth: 1 });

  const req = dataOf(requirements);
  const gate = props.targetStageKey && req
    ? req.gatePolicy.find(g => g.targetStageKey === props.targetStageKey && g.changeTypeKey === change.changeType.key)
      ?? req.gatePolicy.find(g => g.targetStageKey === props.targetStageKey)
    : null;
  const listed = new Set(detail.affected.map(a => a.businessApplicationId.toLowerCase()));
  const unlisted = (dataOf(walk)?.nodes ?? []).filter(n => !listed.has(n.businessApplicationId.toLowerCase()));

  return (
    <Panel title={props.title ?? 'Before you submit'} subtitle="What the engine will check, and who it will ask">
      <div className="flex flex-col gap-1.5">
        <h3 className={subhead}>Standard change check</h3>
        <StandardCheck state={standard} isStandardType={requiresAuthorization === false} />
      </div>

      <div className="flex flex-col gap-1.5">
        <h3 className={subhead}>Who will authorize it</h3>
        {change.mode === 'linked' ? (
          <p className="m-0 text-sm text-navy-2">
            Linked mode: your ITSM tool authorizes this change, so no built-in approval starts. A recorded number alone never
            counts as authorization.
          </p>
        ) : requiresAuthorization === false ? (
          <p className="m-0 text-sm text-navy-2">The {change.changeType.label.toLowerCase()} type needs no authorization when an approved standard change model fits it.</p>
        ) : !app ? (
          <Muted>Reading the application.</Muted>
        ) : !app.tier ? (
          <p className="m-0 text-sm text-navy-2">
            {app.name} has no tier yet, so the approvals cannot be shown here. The engine works them out when you submit.
          </p>
        ) : requirements.status === 'loading' ? (
          <Muted>Reading the approval rules.</Muted>
        ) : requirements.status === 'done' && !requirements.envelope.ok ? (
          <PanelFailure failure={requirements.envelope} />
        ) : (
          <>
            <ApprovalNeededList items={approvals} empty="The approval rules ask for no authorization for this change." />
            <Muted>{`From the approval rules for ${withArticle(change.changeType.label.toLowerCase())} change at ${app.tier.label}. The engine asks them when you submit.`}</Muted>
          </>
        )}
      </div>

      <div className="flex flex-col gap-1.5">
        <h3 className={subhead}>Gate level at the target</h3>
        {!props.targetStageKey ? (
          <Muted>No planned target yet. Choose one in the draft to see the gate level.</Muted>
        ) : gate ? (
          <p className="m-0 text-sm text-navy-2">
            <span className="font-semibold text-navy">{gateLevelLabel[gate.gateLevel as GateLevel] ?? gate.gateLevel}.</span>{' '}
            {gateLevelText[gate.gateLevel as GateLevel] ?? ''}
          </p>
        ) : requirements.status === 'loading' ? <Muted>Reading the gate policy.</Muted> : <Muted>The gate policy could not be read for this target.</Muted>}
      </div>

      {unlisted.length > 0 && (
        <div className="flex items-start gap-2 rounded-md border border-warning-line bg-warning-soft px-3 py-2 text-sm text-warning">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>
            {`Depend${unlisted.length === 1 ? 's' : ''} on this application but not listed as affected: `}
            {unlisted.map(n => n.name).join(', ')}. Edit the draft to add {unlisted.length === 1 ? 'it' : 'them'}, or leave {unlisted.length === 1 ? 'it' : 'them'} out on purpose.
          </span>
        </div>
      )}
      {props.footer}
    </Panel>
  );
}

function StandardCheck({ state, isStandardType }: { state: ToolState<StandardChangeCheck>; isStandardType: boolean }) {
  if (state.status === 'loading') return <Muted>Checking the standard change models.</Muted>;
  if (!state.envelope.ok) return <PanelFailure failure={state.envelope} />;
  const d = state.envelope.data;
  if (d.qualifies && d.model) {
    const decision = recordHrefOf('decision', d.model.approvedByDecisionId);
    return (
      <div className="flex flex-col gap-1">
        <ToneBadge tone="success">Fits the standard model {d.model.label}</ToneBadge>
        <span className="text-xs text-navy-3">
          A person approved the model once{decision ? <>, in <TextLink href={decision}>its decision</TextLink></> : ''}. A change that fits it is pre-authorized.
        </span>
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-1.5">
      <span className="flex items-center gap-1.5 text-sm font-semibold text-navy">
        <CircleSlash className="size-4 shrink-0 text-navy-3" aria-hidden />
        {d.model ? `Does not fit the standard model ${d.model.label}` : 'Does not fit a standard change model'}
      </span>
      {d.failedConditions.length > 0 && (
        <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy-2">
          {d.failedConditions.map((f, i) => <li key={`${f.condition}-${i}`}>{f.why}</li>)}
        </ul>
      )}
      <span className="text-xs text-navy-3">
        {isStandardType
          ? 'It is typed as standard, so it needs a model that fits. Otherwise change the type before you submit.'
          : 'It goes through the approvals its type and tier need.'}
      </span>
    </div>
  );
}

// ---------- submit ----------

// show: the trigger shows. A sheet stays open after its write changes the record (the change is then no longer a draft),
// so the engine's answer stays in view until the person closes it.
export function SubmitChangeSheet(props: { changeRequestId: string; title: string; label?: string; askedText?: ReactNode; show?: boolean }) {
  const [note, setNote] = useState('');
  const sheet = useSheet(() => setNote(''));
  if (props.show === false && !sheet.open) return null;
  return (
    <ActionSheet
      tool="submit_record" site="pages/changes-records/ChangeActions#SubmitChangeSheet"
      title={`Submit: ${props.title}`}
      description="The engine opens the change's authorization decision and asks the approvers the rules name. Only people answer, on the decision page."
      trigger={props.show === false ? undefined : <Button>{props.label ?? 'Submit'}</Button>}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => ({ subjectKind: 'change-request' as const, subjectId: props.changeRequestId, ...(note.trim() ? { note: note.trim() } : {}) })}
      submitLabel="Submit"
      preview
      confirm={{
        title: 'Submit this change?',
        body: (
          <>
            <p className="m-0">The approvers are asked now. You can withdraw the change while it waits, which cancels the open requests.</p>
            {props.askedText && <div className="mt-2">{props.askedText}</div>}
          </>
        ),
        confirmLabel: 'Submit',
      }}
    >
      <Field label="Note for the approvers" name="note" hint="Optional. Shown with the request.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// ---------- withdraw ----------

export function WithdrawChangeSheet(props: { changeRequestId: string; title: string; show?: boolean }) {
  const [reason, setReason] = useState('');
  const sheet = useSheet(() => setReason(''));
  if (props.show === false && !sheet.open) return null;
  return (
    <ActionSheet
      tool="withdraw_record" site="pages/changes-records/ChangeActions#WithdrawChangeSheet"
      title={`Withdraw: ${props.title}`}
      description="The change stops here. Approval requests still open are canceled."
      trigger={props.show === false ? undefined : <Button variant="outline">Withdraw</Button>}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => (reason.trim()
        ? { subjectKind: 'change-request' as const, subjectId: props.changeRequestId, reason: reason.trim() }
        : { problem: 'Say why the change is withdrawn.', field: 'reason' })}
      submitLabel="Withdraw"
      submitTone="destructive"
      confirm={{
        title: 'Withdraw this change?',
        body: <p className="m-0">A withdrawn change cannot be submitted again. Plan a new change if the work comes back.</p>,
        confirmLabel: 'Withdraw',
        tone: 'destructive',
      }}
    >
      <Field label="Reason" name="reason" required hint="Stored with the change. The approvers who were asked can read it.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// ---------- record the result ----------

export function RecordResultSheet(props: { detail: ChangeRequestDetail; show?: boolean }) {
  const { detail } = props;
  const results = useListValues('change-result');
  const kinds = useLabels('link-kind');
  const evidenceKinds = choicesOf(kinds, k => kinds.field<string>(k, 'linkCategory') === 'evidence');
  const suggested = suggestResult(detail.deployments.map(d => d.result.meaning as StatusMeaning));
  const [resultKey, setResultKey] = useState('');
  const [note, setNote] = useState('');
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const [kind, setKind] = useState('');
  const chosen = resultKey || detail.result?.key || suggested || '';
  const chosenKind = kind || (evidenceKinds.length === 1 ? evidenceKinds[0].value : '');
  const reset = () => { setResultKey(''); setNote(''); setUrl(''); setLabel(''); setKind(''); };
  const sheet = useSheet(reset);
  if (props.show === false && !sheet.open) return null;
  return (
    <ActionSheet
      tool="record_change_result" site="pages/changes-records/ChangeActions#RecordResultSheet"
      title={`How did it end: ${detail.change.title}`}
      description="Records the result as reported. The engine opens a post-change review when the change type asks for one or the change did not fully succeed."
      trigger={props.show === false ? undefined : <Button variant={detail.result ? 'outline' : 'default'}>{detail.result ? 'Change the result' : 'Record the result'}</Button>}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => {
        if (!chosen) return { problem: 'Choose how the change ended.', field: 'resultKey' };
        const u = url.trim();
        const l = label.trim();
        if ((u || l) && (!u || !l || !chosenKind)) return { problem: 'An evidence link needs its kind, its address and a label, or leave all three empty.', field: 'evidence' };
        if (u && !isHttpsUrl(u)) return { problem: 'The evidence link must be a full https address.', field: 'evidence' };
        return {
          changeRequestId: detail.change.changeRequestId,
          resultKey: chosen,
          ...(note.trim() ? { note: note.trim() } : {}),
          ...(u ? { evidence: [{ linkKindKey: chosenKind, url: u, label: l }] } : {}),
        };
      }}
      submitLabel="Record the result"
      preview
    >
      <SelectField
        label="Result"
        name="resultKey"
        value={chosen}
        onChange={setResultKey}
        choices={results.choices}
        required
        placeholder={results.loaded ? 'Choose the result' : 'Reading the result values'}
        hint={suggested && !detail.result ? `Suggested from the linked deployments: ${results.label(suggested)}. You decide.` : undefined}
      />
      <Field label="Note" name="note" hint="Optional. What happened, in plain words.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
      <div className="flex flex-col gap-3 border-t border-line pt-3">
        <h3 className={subhead}>Evidence, optional</h3>
        <SelectField label="Kind" name="evidence" value={chosenKind} onChange={setKind} choices={evidenceKinds} placeholder="Choose a kind" />
        <Field label="Link" name="evidence">
          {p => <Input {...p} type="url" inputMode="url" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://" maxLength={2000} />}
        </Field>
        <Field label="Label" name="evidence">
          {p => <Input {...p} value={label} onChange={e => setLabel(e.target.value)} maxLength={200} placeholder="Smoke test run 2140" />}
        </Field>
      </div>
    </ActionSheet>
  );
}

// ---------- close the change ----------

export function CompleteChangeSheet(props: { detail: ChangeRequestDetail; show?: boolean }) {
  const { detail } = props;
  const results = useListValues('change-result');
  const suggested = suggestResult(detail.deployments.map(d => d.result.meaning as StatusMeaning));
  const [resultKey, setResultKey] = useState('');
  const chosen = resultKey || detail.result?.key || suggested || '';
  const reset = () => { setResultKey(''); };
  const sheet = useSheet(reset);
  if (props.show === false && !sheet.open) return null;
  return (
    <ActionSheet
      tool="complete_change" site="pages/changes-records/ChangeActions#CompleteChangeSheet"
      title={`Close the change: ${detail.change.title}`}
      description="You close the change with how it ended. It leaves the open part of the schedule. A post-change review opens when the change type asks for one or the change did not fully succeed."
      trigger={props.show === false ? undefined : <Button>Close the change</Button>}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => {
        if (!chosen) return { problem: 'Choose how the change ended.', field: 'resultKey' };
        return { changeRequestId: detail.change.changeRequestId, resultKey: chosen };
      }}
      submitLabel="Close the change"
      preview
    >
      <SelectField
        label="Result"
        name="resultKey"
        value={chosen}
        onChange={setResultKey}
        choices={results.choices}
        required
        placeholder={results.loaded ? 'Choose the result' : 'Reading the result values'}
        hint={detail.result ? `Recorded so far: ${detail.result.label}.` : suggested ? `Suggested from the linked deployments: ${results.label(suggested)}. You decide.` : undefined}
      />
    </ActionSheet>
  );
}

// ---------- the result panel ----------

export function ResultSummary({ detail }: { detail: ChangeRequestDetail }) {
  const results = useListValues('change-result');
  const suggested = suggestResult(detail.deployments.map(d => d.result.meaning as StatusMeaning));
  if (detail.result) {
    return (
      <span className="flex items-center gap-1.5 text-sm font-semibold text-navy">
        <CircleCheck className="size-4 shrink-0 text-success" aria-hidden />
        {`Result recorded: ${detail.result.label}`}
      </span>
    );
  }
  return (
    <div className="flex flex-col gap-1">
      <Empty>No result recorded yet.</Empty>
      {suggested && (
        <span className="flex items-center gap-1.5 text-xs text-navy-3">
          <Info className="size-3.5 shrink-0" aria-hidden />
          {`The linked deployments suggest: ${results.label(suggested)}.`}
        </span>
      )}
    </div>
  );
}
