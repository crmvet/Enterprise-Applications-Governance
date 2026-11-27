// Building blocks the Changes pages and the deployment, document and claim record pages share: facts, the record lookup
// states, the approvals a change needs or has, the decisions about a record, the links on a record with the sheet that
// adds one, and the deployment flags. Built on the shared blocks (Panel, StatusBadge, ToneBadge, Row) and shadcn/ui.
import { CircleCheck, CircleDashed, CircleX, Link2, Plus, Search } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { recordHref } from '../../app/links';
import type {
  StatusValue,
  DecisionGate,
  ApprovalMode,
  ApprovalNeeded,
  ApprovalStatus,
  DecisionSubjectKind,
  DeploymentFlags,
  Failure,
  LinkCategory,
  RecordLink,
  SaveLinkInput,
} from '../../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../../data/format';
import { useLabels, useListValues, type Labels } from '../../data/labels';
import { activeFlags, flagHelp, flagText } from '../../data/meanings';
import { dataOf, useTool } from '../../data/toolClient';
import { ActionSheet, Field, type FieldControl } from '../../components/actions';
import { Empty, Muted, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../../components/blocks';
import { ExternalLink, Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { GateHold } from '../../components/gateHold';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Skeleton } from '../../components/ui/skeleton';
import { Tooltip, TooltipContent, TooltipTrigger } from '../../components/ui/tooltip';
import { cn } from '../../lib/utils';
import { isHttpsUrl } from './logic';

export const subhead = 'm-0 mt-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';
export const plainText = 'm-0 text-sm leading-relaxed text-navy-2';
export const mono = 'font-mono text-xs [overflow-wrap:anywhere] text-navy-2';
// Two columns on a wide screen, one below 1024 px.
export const recordGrid = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
export const column = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

// ---------- facts ----------

export function Facts({ children, className }: { children: ReactNode; className?: string }) {
  return <dl className={cn('m-0 grid grid-cols-3 gap-x-4 gap-y-3.5 medium:grid-cols-2 narrow:grid-cols-1', className)}>{children}</dl>;
}

export function Fact({ label, children, sub, wide }: { label: string; children: ReactNode; sub?: ReactNode; wide?: boolean }) {
  return (
    <div className={cn('flex min-w-0 flex-col gap-0.5', wide && 'col-span-full')}>
      <dt className="m-0 text-xs text-navy-3">{label}</dt>
      <dd className="m-0 flex flex-wrap items-center gap-1.5 text-sm font-semibold [overflow-wrap:anywhere] text-navy">{children}</dd>
      {sub && <dd className="m-0 text-xs leading-4 text-navy-3">{sub}</dd>}
    </div>
  );
}

// ---------- sheets ----------

// The open state of an action sheet the page keeps itself. A page shows a sheet's trigger only while its act fits the
// record, and keeps the sheet mounted while it is open: a write that changes the record (a draft submitted, a claim
// dismissed) then leaves the engine's answer in view until the person closes the sheet.
export function useSheet(onClose?: () => void): { open: boolean; onOpenChange: (open: boolean) => void } {
  const [open, setOpen] = useState(false);
  return {
    open,
    onOpenChange: next => {
      setOpen(next);
      if (!next) onClose?.();
    },
  };
}

// ---------- form controls ----------

// The Field control props a Select trigger takes (a button has no required attribute).
export function triggerProps(p: FieldControl) {
  return { id: p.id, 'aria-describedby': p['aria-describedby'], 'aria-invalid': p['aria-invalid'] };
}

export interface Choice {
  value: string;
  label: string;
}

// A labeled select inside an action sheet. An empty value shows the placeholder; noneLabel adds a choice that clears it.
export function SelectField(props: {
  label: string;
  name: string;
  value: string;
  onChange: (value: string) => void;
  choices: Choice[];
  placeholder?: string;
  noneLabel?: string;
  hint?: ReactNode;
  required?: boolean;
}) {
  const NONE = '__none__';
  return (
    <Field label={props.label} name={props.name} hint={props.hint} required={props.required}>
      {p => (
        <Select value={props.value || (props.noneLabel ? NONE : '')} onValueChange={v => props.onChange(v === NONE ? '' : v)}>
          <SelectTrigger {...triggerProps(p)} className="w-full">
            <SelectValue placeholder={props.placeholder ?? 'Choose'} />
          </SelectTrigger>
          <SelectContent>
            {props.noneLabel && <SelectItem value={NONE}>{props.noneLabel}</SelectItem>}
            {props.choices.map(c => <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>)}
          </SelectContent>
        </Select>
      )}
    </Field>
  );
}

// Active configured rows of an entity as choices, in configured order.
export function choicesOf(labels: Labels, filter?: (key: string) => boolean): Choice[] {
  return labels.keys().filter(k => !filter || filter(k)).map(k => ({ value: k, label: labels.label(k) }));
}

// ---------- record lookup ----------

// A record page whose entity has no read by ID looks through a list read. While it looks, it says how far it got; when
// the record is not in what the person can see, it says so plainly and what the page looked through.
export function LookupState(props: { title: string; pagesRead: number; failure?: Failure | null; notFound?: ReactNode; next?: ReactNode; id: string }) {
  if (props.failure) {
    return (
      <Panel title={props.title}>
        <PanelFailure failure={props.failure} />
      </Panel>
    );
  }
  if (props.notFound) {
    return (
      <Panel title="Not found">
        <div className="flex flex-col gap-2">
          {props.notFound}
          <span className={mono}>{props.id}</span>
          {props.next}
        </div>
      </Panel>
    );
  }
  return (
    <Panel title={props.title}>
      <div role="status" aria-label={`Loading ${props.title}`} className="flex flex-col gap-2.5 pt-1">
        <span className="flex items-center gap-1.5 text-xs text-navy-3">
          <Search className="size-3.5" aria-hidden />
          {props.pagesRead > 0 ? `Looking for the record: ${plural(props.pagesRead, 'page', 'pages')} read so far.` : 'Looking for the record.'}
        </span>
        {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-4 w-full" />)}
      </div>
    </Panel>
  );
}

// ---------- approvals ----------

const MODE_TEXT: Record<ApprovalMode, string> = {
  'everyone-must-approve': 'everyone asked must approve',
  'first-to-respond': 'the first answer counts',
  sequential: 'in order, one after another',
};

export function useWhoLabels() {
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  return {
    who: (roleKey: string | undefined, ownerRoleKey: string | undefined) =>
      roleKey ? roles.label(roleKey) : ownerRoleKey ? `${ownerRoles.label(ownerRoleKey)} of the application` : 'Not named',
  };
}

// The approvals a change will need, as the approval rules say for its tier and type.
export function ApprovalNeededList({ items, empty }: { items: ApprovalNeeded[]; empty: ReactNode }) {
  const { who } = useWhoLabels();
  if (!items.length) return <Empty>{empty}</Empty>;
  return (
    <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
      {items.map((a, i) => (
        <li key={`${a.approverRoleKey ?? a.approverOwnerRoleKey}-${i}`} className="flex items-start gap-2 text-sm text-navy">
          <CircleDashed className="mt-0.5 size-4 shrink-0 text-navy-3" aria-hidden />
          <span>
            <span className="font-semibold">{who(a.approverRoleKey, a.approverOwnerRoleKey)}</span>
            <span className="text-navy-3">{`: ${MODE_TEXT[a.approvalMode]}${a.minimumCount > 1 ? `, ${a.minimumCount} approvals` : ''}.`}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}

// The words for who answered when it was not the person directly.
export function actorKindWords(kind: string): string {
  switch (kind) {
    case 'ai-client-for-person': return 'Answered through an AI client';
    case 'agent-identity': return 'Answered by an agent';
    case 'approval-runner': return 'Recorded by the approval runner';
    case 'engine': return 'Recorded by the engine';
    case 'pipeline': return 'Recorded by a pipeline';
    default: return `Answered as ${kind.replace(/-/g, ' ')}`;
  }
}

// The approval state of a submitted record (get_approval_status): what the rules require, each answer and whether it
// counts, and what is still missing. settled is the decision's status once it is no longer proposed: then nothing is
// missing any more, and the page shows the answers the product holds. gate is the gate that holds a proposed decision
// whose approvals are in (get_decision).
export function ApprovalStatusView({ status, settled, gate, subjectKind }: { status: ApprovalStatus; settled?: StatusValue | null; gate?: DecisionGate | null; subjectKind?: DecisionSubjectKind }) {
  const { who } = useWhoLabels();
  const separation = useLabels('separation-rule');
  const isSettled = !!settled && settled.meaning !== 'proposed';
  return (
    <div className="flex flex-col gap-3">
      {isSettled ? (
        <span className="flex flex-wrap items-center gap-2 text-sm text-navy">
          The decision is settled: <StatusBadge status={settled!} />
          {status.settledBy === 'engine' && <span className="text-navy-2">The engine accepted it under the locked list, with no approval request.</span>}
        </span>
      ) : status.ready ? (
        gate && !gate.ready
          ? <GateHold gate={gate} approvalsIn subjectKind={subjectKind} />
          : <ToneBadge tone="success">The approvals the rules require are in</ToneBadge>
      ) : status.decisionId ? (
        <ToneBadge tone="warning">{plural(status.missing.reduce((n, m) => n + m.stillNeeded, 0), 'approval is', 'approvals are')} still missing</ToneBadge>
      ) : null}
      {!isSettled && status.required.length > 0 && (
        <div className="flex flex-col gap-1">
          <h3 className={subhead}>What the rules require</h3>
          <RowList label="What the rules require">
            {status.required.map(r => (
              <Row
                key={r.approvalRuleKey}
                primary={who(r.requiredRoleKey, r.requiredOwnerRoleKey)}
                secondary={`${MODE_TEXT[r.approvalMode]}${r.minimumCount > 1 ? `, ${r.minimumCount} approvals` : ''}${r.isConsultation ? '. Consulted: advice only, never counted' : ''}`}
                end={<span className="text-sm font-semibold text-navy tabular-nums">{r.received} of {Math.max(1, r.minimumCount)}</span>}
                endSub="received"
              />
            ))}
          </RowList>
        </div>
      )}
      <div className="flex flex-col gap-1">
        <h3 className={subhead}>Answers</h3>
        {status.records.length ? (
          <RowList label="Answers">
            {status.records.map(r => (
              <Row
                key={r.approvalRecordId}
                lead={<AnswerIcon meaning={r.outcome.meaning} />}
                primary={r.approver ? r.approver.displayLabel : 'Not answered yet'}
                secondary={[
                  r.respondedOn ? `Answered ${fmtDateTime(r.respondedOn)}` : 'Waiting for an answer',
                  r.approverActorKind && r.approverActorKind !== 'person' ? actorKindWords(r.approverActorKind) : null,
                ].filter(Boolean).join('. ')}
                tertiary={r.typedReason ? <span className="whitespace-pre-wrap text-navy-2">{`Reason: ${r.typedReason}`}</span> : undefined}
                end={<StatusBadge status={r.outcome} />}
                endSub={r.respondedOn
                  ? r.refusedBySeparationRuleKey
                    ? `Does not count: separation rule "${separation.label(r.refusedBySeparationRuleKey)}"`
                    : r.countsTowardRequirement ? 'Counts' : 'Does not count'
                  : undefined}
                wideEnd
              />
            ))}
          </RowList>
        ) : <Empty>{!isSettled ? 'No approval request is open yet.'
          : status.settledBy === 'recorded' ? `It was recorded as ${settled!.label.toLowerCase()} outside the product's approvals, as the demo estate is, so no approval request is on record.`
            : status.settledBy === 'engine' ? 'No approval request was needed.'
              : 'No approval answer is recorded for this decision.'}</Empty>}
      </div>
      {!isSettled && status.missing.length > 0 && (
        <div className="flex flex-col gap-1">
          <h3 className={subhead}>Still missing</h3>
          <ul className="m-0 flex list-none flex-col gap-1 p-0">
            {status.missing.map(m => (
              <li key={m.approvalRuleKey} className="text-sm text-navy">
                <span className="font-semibold">{who(m.requiredRoleKey, m.requiredOwnerRoleKey)}</span>
                <span className="text-navy-3">{`: ${plural(m.stillNeeded, 'more approval', 'more approvals')}`}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function AnswerIcon({ meaning }: { meaning: string }) {
  const base = 'grid size-8 place-items-center rounded-full';
  if (meaning === 'approved') return <span className={cn(base, 'bg-success-soft text-success')} aria-hidden><CircleCheck className="size-4" /></span>;
  if (meaning === 'rejected' || meaning === 'expired') return <span className={cn(base, 'bg-danger-soft text-danger')} aria-hidden><CircleX className="size-4" /></span>;
  return <span className={cn(base, 'bg-muted text-navy-3')} aria-hidden><CircleDashed className="size-4" /></span>;
}

// ---------- decisions about a record ----------

// Every decision recorded about one record (get_decision_log by subject), each leading to its decision page.
export function DecisionsAbout({ subjectKind, subjectId, title = 'Decisions about it', empty }: { subjectKind: DecisionSubjectKind; subjectId: string; title?: string; empty: string }) {
  const log = useTool('get_decision_log', { subjectKind, subjectId, top: 25 });
  return (
    <Panel title={title} count={dataOf(log)?.items.length} state={log}>
      {data => data.items.length ? (
        <RowList label={title}>
          {data.items.map(d => {
            const href = recordHref(d.recordUrl);
            return (
              <Row
                key={d.decisionId}
                primary={href ? <TextLink href={href}>{d.statement}</TextLink> : d.statement}
                secondary={[d.decisionType.label, d.outcome ? `Outcome: ${d.outcome.label}` : null, d.decidedOn ? `Decided ${fmtDate(d.decidedOn)}` : null].filter(Boolean).join('. ')}
                tertiary={d.approvalsSummary.required > 0 ? `${d.approvalsSummary.approved} of ${d.approvalsSummary.required} approvals in${d.approvalsSummary.rejected ? `, ${d.approvalsSummary.rejected} rejected` : ''}` : undefined}
                badges={d.isAiCreated || d.performedBy?.actorKind === 'ai-client-for-person' ? <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} /> : undefined}
                end={<StatusBadge status={d.status} />}
              />
            );
          })}
        </RowList>
      ) : <Empty>{empty}</Empty>}
    </Panel>
  );
}

// ---------- links ----------

// Delivery links (work items) and evidence links on a record, each opening outside the product.
export function LinksList({ links }: { links: RecordLink[] }) {
  if (!links.length) return <Empty>No work item or evidence is linked yet.</Empty>;
  const groups: { category: LinkCategory; title: string }[] = [
    { category: 'delivery', title: 'Work items' },
    { category: 'evidence', title: 'Evidence' },
  ];
  return (
    <div className="flex flex-col gap-2">
      {groups.map(g => {
        const items = links.filter(l => l.linkCategory === g.category);
        if (!items.length) return null;
        return (
          <div key={g.category} className="flex flex-col gap-1">
            <h3 className={subhead}>{g.title}</h3>
            <RowList label={g.title}>
              {items.map(l => (
                <Row
                  key={l.linkId}
                  lead={<span className="grid size-8 place-items-center rounded-full bg-background text-navy-2" aria-hidden><Link2 className="size-4" /></span>}
                  primary={<ExternalLink href={l.url}>{l.label}</ExternalLink>}
                  secondary={l.linkKind.label}
                />
              ))}
            </RowList>
          </div>
        );
      })}
    </div>
  );
}

// Adds a work item link or an evidence link to a record (save_link). The link kinds come from configuration, filtered to
// the chosen category; the engine checks the kind's URL pattern and that the person may write on the record.
export function SaveLinkSheet(props: {
  subjectKind: SaveLinkInput['subjectKind'];
  subjectId: string;
  category: LinkCategory;
}) {
  const kinds = useLabels('link-kind');
  const levels = useListValues('evidence-level');
  const choices = choicesOf(kinds, k => kinds.field<string>(k, 'linkCategory') === props.category);
  const [kind, setKind] = useState('');
  const [url, setUrl] = useState('');
  const [label, setLabel] = useState('');
  const [externalId, setExternalId] = useState('');
  const [level, setLevel] = useState('');
  const delivery = props.category === 'delivery';
  const chosenKind = kind || (choices.length === 1 ? choices[0].value : '');
  const reset = () => { setKind(''); setUrl(''); setLabel(''); setExternalId(''); setLevel(''); };
  return (
    <ActionSheet
      tool="save_link" site="pages/changes-records/parts#SaveLinkSheet"
      title={delivery ? 'Link a work item' : 'Add evidence'}
      description={delivery
        ? 'A link to the work item in its own tool, such as an Azure DevOps work item or a GitHub issue. The product stores the link, never the item.'
        : 'A link to proof held elsewhere, such as a test result or a sign-off. Evidence is append-only: a saved link is never edited.'}
      trigger={(
        <Button variant="outline" size="sm">
          <Plus aria-hidden />
          {delivery ? 'Link a work item' : 'Add evidence'}
        </Button>
      )}
      input={() => {
        if (!chosenKind) return { problem: 'Choose the kind of link.', field: 'linkKindKey' };
        const u = url.trim();
        if (!isHttpsUrl(u)) return { problem: 'Give the full https address of the link.', field: 'url' };
        const l = label.trim();
        if (!l) return { problem: 'Give the link a short label people will recognize.', field: 'label' };
        return {
          linkCategory: props.category,
          subjectKind: props.subjectKind,
          subjectId: props.subjectId,
          linkKindKey: chosenKind,
          url: u,
          label: l,
          ...(externalId.trim() ? { externalId: externalId.trim() } : {}),
          ...(!delivery && level ? { evidenceLevelKey: level } : {}),
        };
      }}
      submitLabel={delivery ? 'Link it' : 'Add evidence'}
      preview
      onOpenChange={open => { if (!open) reset(); }}
    >
      <SelectField label="Kind" name="linkKindKey" value={chosenKind} onChange={setKind} choices={choices} required placeholder={choices.length ? 'Choose a kind' : 'No link kind of this category is configured'} />
      <Field label="Link" name="url" required hint="The full https address.">
        {p => <Input {...p} type="url" inputMode="url" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      <Field label="Label" name="label" required>
        {p => <Input {...p} value={label} onChange={e => setLabel(e.target.value)} maxLength={200} />}
      </Field>
      {delivery && (
        <Field label="ID in its own tool" name="externalId" hint="Optional, such as the work item number.">
          {p => <Input {...p} value={externalId} onChange={e => setExternalId(e.target.value)} maxLength={200} />}
        </Field>
      )}
      {!delivery && levels.choices.length > 0 && (
        <SelectField label="Evidence level" name="evidenceLevelKey" value={level} onChange={setLevel} choices={levels.choices} noneLabel="As reported (the default)" />
      )}
    </ActionSheet>
  );
}

// ---------- deployment flags ----------

// The raised flags of a deployment as badges, each with its explanation on hover and focus.
export function FlagBadges({ flags }: { flags: DeploymentFlags }) {
  const raised = activeFlags(flags);
  if (!raised.length) return null;
  return (
    <span className="contents">
      {raised.map(f => (
        <Tooltip key={f}>
          <TooltipTrigger asChild>
            <span tabIndex={0} className="rounded-md focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring">
              <ToneBadge tone="warning">{flagText[f]}</ToneBadge>
            </span>
          </TooltipTrigger>
          <TooltipContent className="max-w-[280px]">{flagHelp[f]}</TooltipContent>
        </Tooltip>
      ))}
    </span>
  );
}

// A muted line for a supporting read that failed, inside a panel that still shows its main content.
export function SideFailure({ failure, what }: { failure: Failure; what: string }) {
  return <Muted>{`${what} could not be read: ${failure.error.message} Reference ${failure.correlationId}.`}</Muted>;
}
