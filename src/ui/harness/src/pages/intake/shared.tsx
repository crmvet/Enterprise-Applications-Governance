// Parts the three intake pages share: who the person is (whoami), the horizon values, the tier questions as radio
// groups with the tier they point to, the duplicate suggestions, what a tier needs, and the approval state of the intake
// decision.
import { Ban, Bot, Circle, CircleCheck, Clock, ExternalLink as ExternalLinkIcon, Info } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { ApprovalStatus, DecisionGate, DuplicateMatches, Envelope, Failure, ImpliedAnswer, RequirementKind, Requirements, StatusValue, Success, TierQuestion, ToolName } from '../../data/contracts';
import { Button } from '../../components/ui/button';
import { GateHold } from '../../components/gateHold';
import { fmtDateTime } from '../../data/format';
import { useLabels } from '../../data/labels';
import { dataOf, useTool, useToolClient, type ToolState } from '../../data/toolClient';
import { hrefTo } from '../../app/links';
import { Empty, EnvelopeNotes, LinkButton, Muted, PanelFailure, StatusBadge, ToneBadge, linkClass } from '../../components/blocks';
import { Skeleton } from '../../components/ui/skeleton';
import { GuidelineChip } from '../../components/actions';
import { GuidelineMark } from '../register-extras/parts';
import { Row, RowList } from '../../components/rows';
import { cn } from '../../lib/utils';
import { DUPLICATE_REASON, matchPercent, splitApprovals, tierFromAnswers, type TierHint } from './logic';

// ---------- who the person is ----------

export interface Whoami {
  objectId: string;
  displayLabel: string;
  roleKeys: string[];
  tools: string[];
}

export type WhoState =
  | { status: 'loading' }
  | { status: 'done'; me: Whoami; failure: null }
  | { status: 'done'; me: null; failure: Failure };

interface WhoamiData {
  principal: { objectId: string; displayLabel: string };
  productRoles: { roleKey: string }[];
  tools: string[];
}

// whoami tells the person's roles and the tools the Tool Policy gives them, so a page offers only the actions their
// roles allow. The engine still checks every write. The scaffold's typed tool list has no whoami, so it is called here
// by name through the same tool client.
export function useWhoami(): WhoState {
  const client = useToolClient();
  const [state, setState] = useState<WhoState>({ status: 'loading' });
  useEffect(() => {
    if (!client) return;
    let live = true;
    const call = client.call as unknown as (tool: string, input: Record<string, unknown>) => Promise<Envelope<WhoamiData>>;
    call('whoami' as ToolName, {})
      .then(envelope => {
        if (!live) return;
        if (!envelope.ok) {
          setState({ status: 'done', me: null, failure: envelope });
          return;
        }
        const d = envelope.data;
        setState({
          status: 'done',
          me: {
            objectId: d.principal?.objectId ?? '',
            displayLabel: d.principal?.displayLabel ?? '',
            roleKeys: Array.isArray(d.productRoles) ? d.productRoles.map(r => r.roleKey) : [],
            tools: Array.isArray(d.tools) ? d.tools : [],
          },
          failure: null,
        });
      })
      .catch((e: unknown) => {
        if (!live) return;
        setState({
          status: 'done',
          me: null,
          failure: { ok: false, error: { code: 'upstream_unavailable', message: e instanceof Error ? e.message : 'The call did not complete.' }, correlationId: 'client-side' },
        });
      });
    return () => { live = false; };
  }, [client]);
  return state;
}

export function meOf(state: WhoState): Whoami | null {
  return state.status === 'done' ? state.me : null;
}

// True when the Tool Policy gives the person the tool.
export function mayCall(state: WhoState, tool: string): boolean {
  const me = meOf(state);
  return !!me && me.tools.includes(tool);
}

// A short note where actions would be, when the person's roles could not be read.
export function RolesUnreadNote({ state }: { state: WhoState }) {
  if (state.status !== 'done' || state.me) return null;
  return (
    <p className="m-0 flex items-start gap-1.5 text-xs text-navy-3">
      <Info className="mt-px size-3.5 shrink-0" aria-hidden />
      <span>Your roles could not be read, so the actions are hidden. Refresh to try again. Reference {state.failure.correlationId}.</span>
    </p>
  );
}

// ---------- small hooks ----------

export function useDebounced<T>(value: T, ms: number): T {
  const [shown, setShown] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setShown(value), ms);
    return () => window.clearTimeout(timer);
  }, [value, ms]);
  return shown;
}

export interface Option {
  key: string;
  label: string;
}

const HORIZON_ORDER = ['now', 'next', 'later'];

// The horizon value list (C07 List Value rows "horizon/<key>"), in its configured order where rows carry one.
// The active values of a value list, in the list's order where the configuration gives one, else in the order given.
export function useListOptions(list: string, fallbackOrder: readonly string[]): Option[] | null {
  const state = useTool('get_configuration', { entity: 'list-value', top: 100 });
  const data = dataOf(state);
  return useMemo(() => {
    if (state.status === 'loading') return null;
    const prefix = `${list}/`;
    const rows = (data?.rows ?? []).filter(r => r.isActive && r.key.startsWith(prefix));
    const order = (key: string, sortOrder: unknown) => (typeof sortOrder === 'number' ? sortOrder : fallbackOrder.indexOf(key) < 0 ? 99 : fallbackOrder.indexOf(key));
    return rows
      .map(r => ({ key: r.key.slice(prefix.length), label: r.label, at: order(r.key.slice(prefix.length), r.fields.sortOrder) }))
      .sort((a, b) => a.at - b.at || a.label.localeCompare(b.label))
      .map(({ key, label }) => ({ key, label }));
  }, [data, state.status, list, fallbackOrder]);
}

export const SIZE_BAND_ORDER = ['small', 'medium', 'large'] as const;

export function useSizeBandOptions(): Option[] | null {
  return useListOptions('size-band', SIZE_BAND_ORDER);
}

export function useHorizonOptions(): Option[] | null {
  const state = useTool('get_configuration', { entity: 'list-value', top: 100 });
  const data = dataOf(state);
  return useMemo(() => {
    if (state.status === 'loading') return null;
    const rows = (data?.rows ?? []).filter(r => r.isActive && r.key.startsWith('horizon/'));
    const order = (key: string, sortOrder: unknown) => (typeof sortOrder === 'number' ? sortOrder : HORIZON_ORDER.indexOf(key) < 0 ? 99 : HORIZON_ORDER.indexOf(key));
    return rows
      .map(r => ({ key: r.key.slice('horizon/'.length), label: r.label, at: order(r.key.slice('horizon/'.length), r.fields.sortOrder) }))
      .sort((a, b) => a.at - b.at || a.label.localeCompare(b.label))
      .map(({ key, label }) => ({ key, label }));
  }, [data, state.status]);
}

// ---------- a read inside a panel ----------

// One read's loading, failure and answer, without a heading of its own: for a read shown inside a panel or a step that
// already has one.
export function StateBlock<T>(props: { state: ToolState<T>; label: string; rows?: number; children: (data: T, envelope: Success<T>) => ReactNode }) {
  const { state } = props;
  if (state.status === 'loading') {
    return (
      <div role="status" aria-label={`Loading ${props.label}`} className="flex flex-col gap-2.5 pt-1">
        {Array.from({ length: props.rows ?? 3 }, (_, i) => <Skeleton key={i} className="h-4 w-full" />)}
      </div>
    );
  }
  if (!state.envelope.ok) return <PanelFailure failure={state.envelope} />;
  return (
    <>
      {props.children(state.envelope.data, state.envelope)}
      <EnvelopeNotes couldNotRead={state.envelope.couldNotRead} warnings={state.envelope.warnings} />
    </>
  );
}

// ---------- definition list ----------

export function Facts({ items, className }: { items: { term: string; value: ReactNode }[]; className?: string }) {
  return (
    <dl className={cn('m-0 grid grid-cols-[minmax(0,180px)_minmax(0,1fr)] gap-x-4 gap-y-2 narrow:grid-cols-1 narrow:gap-y-0.5', className)}>
      {items.map(item => (
        <div key={item.term} className="contents">
          <dt className="text-xs font-semibold text-navy-3 narrow:mt-2 narrow:first:mt-0">{item.term}</dt>
          <dd className="m-0 text-sm text-navy [overflow-wrap:anywhere]">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

// Text a person typed, shown as plain text with its line breaks.
export function Prose({ text, empty = 'Not given' }: { text: string | null | undefined; empty?: string }) {
  if (!text || !text.trim()) return <Muted>{empty}</Muted>;
  return <span className="whitespace-pre-wrap">{text}</span>;
}

// ---------- tier questions ----------

// The tier questions as radio groups. Each answer says the tier it implies, and the tier the answers point to shows as
// they are chosen. disabled keeps the choices visible and read-only.
// implied (wave 4, B24-22): the answer the application's declared data implies (get_tier_questions impliedAnswer). It shows
// under its question as a suggestion with a button to choose it, never as a preset answer: the person still answers.
export function TierQuestionsForm(props: {
  questions: TierQuestion[];
  answers: Record<string, string>;
  onChange: (answers: Record<string, string>) => void;
  idPrefix: string;
  disabled?: boolean;
  implied?: ImpliedAnswer | null;
}) {
  return (
    <div className="flex flex-col gap-4">
      {props.questions.map((q, qi) => {
        const name = `${props.idPrefix}-${q.questionKey}`;
        const helpId = q.helpText ? `${name}-help` : undefined;
        return (
          <fieldset key={q.questionKey} className="m-0 flex min-w-0 flex-col gap-2 rounded-lg border border-line p-3.5 narrow:p-3" aria-describedby={helpId} disabled={props.disabled}>
            <legend className="float-left mb-1 w-full p-0 text-sm font-semibold text-navy">
              <span className="mr-1.5 text-navy-3">{qi + 1}.</span>
              {q.text}
            </legend>
            {q.helpText && <p id={helpId} className="clear-both m-0 text-xs leading-relaxed text-navy-3">{q.helpText}</p>}
            {props.implied && props.implied.questionKey === q.questionKey && q.answers.some(a => a.answerKey === props.implied!.answerKey) && (
              <ImpliedSuggestion
                implied={props.implied}
                chosen={props.answers[q.questionKey] === props.implied.answerKey}
                disabled={props.disabled}
                onChoose={() => props.onChange({ ...props.answers, [q.questionKey]: props.implied!.answerKey })}
              />
            )}
            <div className="clear-both flex flex-col gap-1.5">
              {q.answers.map(a => {
                const id = `${name}-${a.answerKey}`;
                const checked = props.answers[q.questionKey] === a.answerKey;
                return (
                  <label
                    key={a.answerKey}
                    htmlFor={id}
                    className={cn(
                      'flex cursor-pointer items-start gap-2.5 rounded-md border px-3 py-2 text-sm text-navy transition-colors',
                      'has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-1 has-[:focus-visible]:outline-ring',
                      checked ? 'border-primary bg-info-soft' : 'border-line hover:border-line-strong hover:bg-background',
                      props.disabled && 'cursor-default opacity-70',
                    )}
                  >
                    <input
                      id={id}
                      type="radio"
                      name={name}
                      value={a.answerKey}
                      checked={checked}
                      onChange={() => props.onChange({ ...props.answers, [q.questionKey]: a.answerKey })}
                      className="mt-0.5 size-4 shrink-0 accent-primary"
                    />
                    <span className="flex min-w-0 flex-1 flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                      <span className="[overflow-wrap:anywhere]">{a.text}</span>
                      <span className="text-xs whitespace-nowrap text-navy-3">Implies {a.impliedTier.label}</span>
                    </span>
                  </label>
                );
              })}
            </div>
          </fieldset>
        );
      })}
    </div>
  );
}

// The tier the answers point to, worked out on the page with the engine's rule (the highest implied tier wins).
export function useTierHint(questions: TierQuestion[], answers: Record<string, string>): TierHint {
  const tiers = useLabels('tier');
  return useMemo(() => {
    const known = new Set(tiers.keys());
    return tierFromAnswers(questions, answers, key => (known.has(key) ? tiers.rank(key) : -1));
  }, [questions, answers, tiers]);
}

export function TierHintView({ hint, questions, saved }: { hint: TierHint; questions: TierQuestion[]; saved?: ReactNode }) {
  const textOf = (key: string) => questions.find(q => q.questionKey === key)?.text ?? key;
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-info-line bg-info-soft px-4 py-3 text-navy" role="status" aria-live="polite">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Your answers point to</span>
      {hint.tier ? (
        <span className="text-xl leading-7 font-semibold">{hint.tier.label}</span>
      ) : (
        <span className="text-sm text-navy-2">No tier yet: answer the questions.</span>
      )}
      <span className="text-xs text-navy-2">
        {hint.answered} of {hint.total} questions answered.
        {hint.decisive.length > 0 && <> Set by: {hint.decisive.map(textOf).join('; ')}</>}
      </span>
      <span className="text-xs leading-relaxed text-navy-2">
        A suggestion only. The engine works the tier out when the answers are saved, and only an accepted tier decision
        sets the tier. An AI client never sets it.
      </span>
      {saved}
    </div>
  );
}

// ---------- duplicates ----------

export function DuplicateList(props: {
  matches: DuplicateMatches['matches'];
  // Opens Application 360 in a new tab, so a form in progress keeps its values.
  newTab?: boolean;
  onUse?: (match: DuplicateMatches['matches'][number]) => void;
  usedId?: string;
  emptyText?: string;
}) {
  if (!props.matches.length) return <Empty>{props.emptyText ?? 'No registered application looks like this.'}</Empty>;
  return (
    <RowList label="Registered applications that look similar">
      {props.matches.map(m => {
        const href = hrefTo({ page: 'app', id: m.businessApplicationId });
        return (
          <Row
            key={m.businessApplicationId}
            primary={props.newTab ? (
              <a href={href} target="_blank" rel="noopener noreferrer" className={cn(linkClass, 'inline-flex items-center gap-[3px]')}>
                {m.name}
                <ExternalLinkIcon className="size-3" aria-label="opens in a new tab" />
              </a>
            ) : (
              <a href={href} className={linkClass}>{m.name}</a>
            )}
            secondary={DUPLICATE_REASON[m.reason] ?? m.reason}
            end={<ToneBadge tone={m.score >= 0.7 ? 'warning' : 'subtle'} icon={null}>{matchPercent(m.score)}</ToneBadge>}
            endSub={props.onUse ? (
              props.usedId === m.businessApplicationId
                ? <span className="inline-flex items-center gap-1 text-navy-2"><CircleCheck className="size-3.5 text-success" aria-hidden />About this app</span>
                : <LinkButton className="text-xs" onClick={() => props.onUse?.(m)} aria-label={`This request is about ${m.name}`}>It is about this app</LinkButton>
            ) : undefined}
          />
        );
      })}
    </RowList>
  );
}

// ---------- what a tier needs ----------

const REQUIREMENT_TEXT: Record<RequirementKind, string> = {
  'decision-accepted': 'Accepted decision',
  'document-current': 'Current document',
  'review-done': 'Review done',
  'data-uses-declared': 'Data uses declared',
  'data-uses-decided': 'Data uses decided',
};

// Whose owner role an approval asks: the application's, or the data domain's or location's the decision is about (wave 3
// verification: a data use read "Data owner of the application", though it goes to its domain's data owner).
const OWNER_SUBJECT_TEXT: Record<NonNullable<Requirements['approvals'][number]['ownerSubject']>, string> = {
  'data-domain': 'of the data domain each use touches',
  'parent-data-domain': 'of the parent data domain',
  'data-location': 'of the data location',
  'touched-data-domains': 'of each data domain the change touches',
};

export function approverText(a: Requirements['approvals'][number], roleLabel: (key: string) => string, ownerRoleLabel: (key: string) => string): string {
  if (a.approverRoleKey) return roleLabel(a.approverRoleKey);
  if (a.approverProposedOwners) return 'Each proposed owner, by name';
  return `${ownerRoleLabel(a.approverOwnerRoleKey ?? 'owner')} ${a.ownerSubject ? OWNER_SUBJECT_TEXT[a.ownerSubject] : 'of the application'}`;
}

// One chip per guideline on a gate: two requirements that apply the same guideline show it once.
export function distinctGuidelines<T extends { guideline: string | null }>(requirements: T[]): T[] {
  const seen = new Set<string>();
  return requirements.filter(r => {
    if (!r.guideline || seen.has(r.guideline)) return false;
    seen.add(r.guideline);
    return true;
  });
}

// forRequest (the wizard): the approvals this request meets come first (intake and the decision types this tier's gates
// name); the tier's other approval rules sit behind a disclosure with their count (splitApprovals).
export function RequirementsView({ data, forRequest = false }: { data: Requirements; forRequest?: boolean }) {
  const gates = useLabels('gate');
  const decisionTypes = useLabels('decision-type');
  const documentTypes = useLabels('document-type');
  const reviewTypes = useLabels('review-type');
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  const stages = useLabels('lifecycle-stage');
  const groupTitle = 'm-0 mb-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';
  const gatesWithRules = data.gates.filter(g => g.requirements.length > 0);
  const modeText = (mode: string) => (mode === 'everyone-must-approve' ? 'everyone must approve' : mode === 'sequential' ? 'in order' : 'the first answer counts');
  return (
    <div className="grid grid-cols-2 gap-x-6 gap-y-4 medium:grid-cols-1">
      <section className="min-w-0">
        <h3 className={groupTitle}>Gates</h3>
        {gatesWithRules.length ? (
          <RowList label="Gate requirements">
            {gatesWithRules.map(g => (
              <Row
                key={g.gateKey}
                primary={gates.label(g.gateKey)}
                secondary={g.requirements.map(r => {
                  const what = r.decisionTypeKey ? decisionTypes.label(r.decisionTypeKey) : r.documentTypeKey ? documentTypes.label(r.documentTypeKey) : r.reviewTypeKey ? reviewTypes.label(r.reviewTypeKey) : '';
                  return what ? `${REQUIREMENT_TEXT[r.requirementKind]}: ${what}` : REQUIREMENT_TEXT[r.requirementKind];
                }).join('; ')}
                badges={distinctGuidelines(g.requirements).map((r, i) => (r.guidelineRef
                  ? <GuidelineMark key={`${r.guideline}-${i}`} guideline={r.guidelineRef} />
                  : <ToneBadge key={`${r.guideline}-${i}`} tone="informative" icon={null}>{r.guideline}</ToneBadge>
                ))}
              />
            ))}
          </RowList>
        ) : <Empty>No gate lists a requirement at this tier.</Empty>}
      </section>
      <section className="min-w-0">
        <h3 className={groupTitle}>Approvals</h3>
        {(() => {
          const rows = (list: Requirements['approvals'], label: string) => (
            <RowList label={label}>
              {list.map((a, i) => (
                <Row
                  key={`${a.decisionTypeKey}-${a.approverRoleKey ?? a.approverOwnerRoleKey}-${i}`}
                  primary={decisionTypes.label(a.decisionTypeKey)}
                  secondary={`${approverText(a, k => roles.label(k), k => ownerRoles.label(k))}, ${modeText(a.approvalMode)}${a.minimumCount > 1 ? `, ${a.minimumCount} approvals` : ''}`}
                />
              ))}
            </RowList>
          );
          if (!data.approvals.length) return <Empty>No decision needs an approval at this tier.</Empty>;
          if (!forRequest) return rows(data.approvals, 'Approvals the decisions need');
          const split = splitApprovals(data);
          return (
            <div className="flex flex-col gap-2">
              {split.now.length ? rows(split.now, 'Approvals this request meets') : <Empty>No approval this request meets at this tier.</Empty>}
              {split.later.length > 0 && (
                <details className="rounded-md border border-line px-3 py-2">
                  <summary className="cursor-pointer text-sm font-medium text-navy focus-visible:outline-2 focus-visible:outline-ring">
                    {`Other decisions at this tier (${split.later.length})`}
                  </summary>
                  <p className="mt-1 mb-2 text-xs text-navy-3">Decisions the application may meet later, such as an exception or a data use. Each asks these approvers when it is proposed.</p>
                  {rows(split.later, 'Approvals of other decisions at this tier')}
                </details>
              )}
            </div>
          );
        })()}
      </section>
      <section className="min-w-0">
        <h3 className={groupTitle}>Documents</h3>
        {data.documents.length ? (
          <RowList label="Documents this tier needs">
            {data.documents.map(d => (
              <Row key={`${d.documentTypeKey}-${d.fromStageKey}`} primary={documentTypes.label(d.documentTypeKey)} secondary={`From the ${stages.label(d.fromStageKey)} stage`} />
            ))}
          </RowList>
        ) : <Empty>No document is required at this tier.</Empty>}
      </section>
      <section className="min-w-0">
        <h3 className={groupTitle}>Reviews</h3>
        {data.reviews.length ? (
          <RowList label="Reviews this tier needs">
            {data.reviews.map(r => (
              <Row key={r.reviewTypeKey} primary={reviewTypes.label(r.reviewTypeKey)} secondary={r.intervalDays > 0 ? `Every ${r.intervalDays} days` : 'When triggered'} />
            ))}
          </RowList>
        ) : <Empty>No review is scheduled at this tier.</Empty>}
      </section>
    </div>
  );
}

// The guidelines a set of requirements applies, each with its text from the configuration in force.
export function GuidelineList({ data }: { data: Requirements }) {
  const numbers = [...new Set(data.gates.flatMap(g => g.requirements.map(r => r.guideline)).filter((g): g is string => !!g))];
  if (!numbers.length) return null;
  return (
    <div className="flex flex-col gap-2">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">The guidelines behind these</span>
      {numbers.map(n => <GuidelineChip key={n} number={n} />)}
    </div>
  );
}

// ---------- the approval state of a decision ----------

const MODE_TEXT: Record<string, string> = {
  'everyone-must-approve': 'everyone must approve',
  'first-to-respond': 'the first answer counts',
  sequential: 'in order',
};

// settled is the decision's status once it is no longer proposed: nothing is missing any more then. gate is the gate that
// holds a proposed decision whose approvals are in (get_decision).
export function ApprovalStatusView({ status, settled, gate, proposedOwner }: { status: ApprovalStatus; settled?: StatusValue | null; gate?: DecisionGate | null; proposedOwner?: string | null }) {
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  const separation = useLabels('separation-rule');
  const isSettled = !!settled && settled.meaning !== 'proposed';
  const asked = (r: { requiredRoleKey?: string; requiredOwnerRoleKey?: string; asksProposedOwners?: boolean }) =>
    r.requiredRoleKey ? roles.label(r.requiredRoleKey) : r.requiredOwnerRoleKey ? `${ownerRoles.label(r.requiredOwnerRoleKey)} of the application` : r.asksProposedOwners ? (proposedOwner ? `${proposedOwner}, the owner the request names` : 'the owner the request names, by name') : 'Not named';
  const ruleOf = (key: string) => status.required.find(r => r.approvalRuleKey === key);
  const groupTitle = 'm-0 mb-1 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';
  if (!status.decisionId) return <Empty>No decision waits for approvals yet. It starts when the request is submitted.</Empty>;
  return (
    <div className="flex flex-col gap-3">
      {isSettled ? (
        <p className="m-0 flex flex-wrap items-center gap-1.5 text-sm text-navy">The decision is settled: <StatusBadge status={settled!} /></p>
      ) : status.ready && gate && !gate.ready ? (
        <GateHold gate={gate} approvalsIn subjectKind="intake-request" />
      ) : (
        <p className="m-0 flex items-center gap-1.5 text-sm text-navy">
          {status.ready
            ? <><CircleCheck className="size-4 shrink-0 text-success" aria-hidden />Every approval the rules require is in.</>
            : <><Clock className="size-4 shrink-0 text-warning" aria-hidden />{status.missing.reduce((n, m) => n + m.stillNeeded, 0) || 'Some'} still needed.</>}
        </p>
      )}
      {!isSettled && status.required.length > 0 && (
        <section>
          <h3 className={groupTitle}>Who is asked</h3>
          <RowList label="Who is asked">
            {status.required.map(r => (
              <Row
                key={r.approvalRuleKey}
                primary={asked(r)}
                secondary={r.isConsultation ? 'Consulted: a recommendation only, never counts' : `${MODE_TEXT[r.approvalMode] ?? r.approvalMode}${r.minimumCount > 1 ? `, ${r.minimumCount} approvals` : ''}`}
                end={r.isConsultation
                  ? <ToneBadge tone="informative">Consultation</ToneBadge>
                  : <ToneBadge tone={r.received >= r.minimumCount ? 'success' : 'warning'}>{`${r.received} of ${r.minimumCount}`}</ToneBadge>}
              />
            ))}
          </RowList>
        </section>
      )}
      <section>
        <h3 className={groupTitle}>Answers</h3>
        {status.records.length ? (
          <RowList label="Approval answers">
            {status.records.map(rec => {
              const rule = ruleOf(rec.approvalRuleKey);
              const consult = !!rule?.isConsultation;
              const counts = rec.refusedBySeparationRuleKey
                ? `The separation rule "${separation.label(rec.refusedBySeparationRuleKey)}" keeps this answer from counting.`
                : consult ? 'Recommendation only.' : rec.outcome.meaning === 'pending' ? null : rec.countsTowardRequirement ? 'Counts toward the approval.' : 'Does not count toward the approval.';
              return (
                <Row
                  key={rec.approvalRecordId}
                  lead={rec.approverActorKind === 'ai-client-for-person' ? <Bot className="size-5 text-brand-dark" aria-label="Answered through an AI client" /> : undefined}
                  primary={rec.approver ? rec.approver.displayLabel || rec.approver.objectId : `Waiting for ${rule ? asked(rule) : 'an answer'}`}
                  secondary={rec.respondedOn ? `Answered ${fmtDateTime(rec.respondedOn)}` : rule ? `Asked: ${asked(rule)}` : undefined}
                  tertiary={counts ?? undefined}
                  badges={rec.typedReason ? <span className="w-full border-l-2 border-line-strong pl-2 text-xs whitespace-pre-wrap text-navy-2 [overflow-wrap:anywhere]">{rec.typedReason}</span> : undefined}
                  end={<StatusBadge status={rec.outcome} />}
                />
              );
            })}
          </RowList>
        ) : <Empty>{isSettled ? 'No approval answer is recorded for this decision.' : 'No approval request is open. The engine may have accepted it under the rules in force.'}</Empty>}
      </section>
      {!isSettled && status.missing.length > 0 && (
        <p className="m-0 text-xs text-navy-3">
          Still needed: {status.missing.map(m => `${m.stillNeeded} from ${asked(m)}`).join('; ')}.
        </p>
      )}
    </div>
  );
}

// ---------- progress ----------

export interface ProgressStep {
  label: string;
  state: 'done' | 'current' | 'todo' | 'stopped';
  note?: string;
}

// A record's progress as an ordered list. Each step says its state in words and with an icon, never by color alone.
export function Progress({ steps, label }: { steps: ProgressStep[]; label: string }) {
  const word = { done: 'done', current: 'current step', todo: 'not reached', stopped: 'stopped here' } as const;
  return (
    <ol className="m-0 flex list-none flex-wrap items-stretch gap-2 p-0" aria-label={label}>
      {steps.map((s, i) => (
        <li
          key={s.label}
          aria-current={s.state === 'current' ? 'step' : undefined}
          className={cn(
            'flex min-w-0 flex-1 basis-[150px] items-start gap-2 rounded-lg border px-3 py-2 narrow:basis-[calc(50%-4px)]',
            s.state === 'current' ? 'border-primary bg-info-soft' : s.state === 'stopped' ? 'border-subtle-line bg-subtle-soft' : 'border-line bg-card',
          )}
        >
          {s.state === 'done' ? <CircleCheck className="mt-px size-4 shrink-0 text-success" aria-hidden />
            : s.state === 'current' ? <Clock className="mt-px size-4 shrink-0 text-brand-dark" aria-hidden />
              : s.state === 'stopped' ? <Ban className="mt-px size-4 shrink-0 text-navy-3" aria-hidden />
                : <Circle className="mt-px size-4 shrink-0 text-navy-3" aria-hidden />}
          <span className="flex min-w-0 flex-col">
            <span className="text-sm font-semibold text-navy">{i + 1}. {s.label}</span>
            <span className="text-xs text-navy-3">{s.note ?? word[s.state]}<span className="sr-only">{s.note ? `, ${word[s.state]}` : ''}</span></span>
          </span>
        </li>
      ))}
    </ol>
  );
}

// The answer the declared data implies, under its question: a suggestion with its reason and a button to choose it.
function ImpliedSuggestion({ implied, chosen, disabled, onChoose }: { implied: ImpliedAnswer; chosen: boolean; disabled?: boolean; onChoose: () => void }) {
  return (
    <div className="clear-both flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-md border border-info-line bg-info-soft px-3 py-2 text-sm text-navy" role="note">
      <span className="min-w-0 flex-1">
        <span className="font-semibold">Suggested from the declared data: </span>
        {implied.label}
        {implied.domain ? `, because the application uses ${implied.domain.path}.` : '.'}
        {' '}A suggestion only: choose the answer that is true.
      </span>
      {chosen
        ? <span className="text-xs text-navy-2">Chosen</span>
        : <Button type="button" size="sm" variant="outline" onClick={onChoose} disabled={disabled}>Use this answer</Button>}
    </div>
  );
}
