// The AI activity page's logic with no React in it (D-28: AI drafts, a person decides). Every fact comes from
// get_ai_activity; these functions only put the tool's answer into words and build the inputs the page sends.
// Nothing here decides what the engine decides: whether a draft was taken forward, by whom, and whether an answer
// counts are the tool's facts.
import type { ActorKind, AiActivity, AiActivityItem, AiActivityView, GetAiActivityInput, PrincipalRef, SubmittableKind } from '../../data/contracts';
import { fmtDateTime } from '../../data/format';
import type { Tone } from '../../data/meanings';

// ---------- views ----------

export interface ViewText {
  tab: string;
  title: string;
  subtitle: string;
  empty: string;
}

export const AI_VIEW_TEXT: Record<AiActivityView, ViewText> = {
  all: {
    tab: 'Everything',
    title: 'Everything AI clients did',
    subtitle: 'Drafts, refused calls, AI answers and AI-only code approvals, newest first.',
    empty: 'No AI client has done anything here yet.',
  },
  drafts: {
    tab: 'Drafts',
    title: 'Every row an AI client created',
    subtitle: 'Each one with where it stands now: waiting for a person, taken forward, decided or withdrawn.',
    empty: 'No AI client has created a row here yet.',
  },
  waiting: {
    tab: 'Waiting for a person',
    title: 'AI drafts waiting for a person',
    subtitle: 'Nothing an AI client drafts goes anywhere until the person it was drafted for accepts it, changes it or withdraws it.',
    empty: 'No AI draft is waiting for a person.',
  },
  accepted: {
    tab: 'Accepted by a person',
    title: 'AI drafts a person took forward',
    subtitle: 'Who took each one forward and when, and whether they changed it first.',
    empty: 'No person has taken an AI draft forward yet.',
  },
  withdrawn: {
    tab: 'Withdrawn',
    title: 'AI drafts withdrawn',
    subtitle: 'Drafts a person or the AI client took back before anyone was asked.',
    empty: 'No AI draft was withdrawn.',
  },
  refused: {
    tab: 'Refused',
    title: 'Calls the product refused an AI client',
    subtitle: 'Each with the rule that refused it. A refused call writes nothing, and it is kept in the audit trail.',
    empty: 'The product has refused no call from an AI client.',
  },
  answers: {
    tab: 'AI answers',
    title: 'Approval answers by an AI actor',
    subtitle: 'An AI answer is a recommendation only. It never counts toward an approval.',
    empty: 'No AI actor has answered an approval request.',
  },
  'code-approvals': {
    tab: 'AI-only code approvals',
    title: 'Deployments whose only code approval came from an AI',
    subtitle: 'An AI code approval never counts as authorization, so the engine flags the deployment for a person to follow up.',
    empty: 'No deployment relied on an AI code approval alone.',
  },
};

// A sub-filter of the drafts the tool counts but has no view for: drafts a person changed before they went forward,
// drafts the AI client submitted itself where the Tool Policy lets it, and rows an AI client reported as a fact (a link,
// a deployment, a claim, a run). The page reads the drafts view and keeps these.
export const DRAFT_ONLY = ['edited', 'ai-submitted', 'recorded'] as const;
export type DraftOnly = (typeof DRAFT_ONLY)[number];

export const DRAFT_ONLY_TEXT: Record<DraftOnly, ViewText> = {
  edited: {
    tab: 'Changed by a person first',
    title: 'AI drafts a person changed before they went forward',
    subtitle: 'The person read the draft and changed it before accepting it. The call history shows each change.',
    empty: 'No person has changed an AI draft before taking it forward.',
  },
  'ai-submitted': {
    tab: 'Submitted by the AI client',
    title: 'AI drafts the AI client submitted itself',
    subtitle: 'Submitting is a request, never an approval: the people the rules name still decide. The Tool Policy says where an AI client may submit.',
    empty: 'No AI client has submitted its own draft.',
  },
  recorded: {
    tab: 'Recorded as facts',
    title: 'Rows an AI client recorded as facts',
    subtitle: 'A link, a deployment, a claim or a run an AI client reported. Nothing waits for a person to take it forward.',
    empty: 'No AI client has recorded a fact.',
  },
};

// ---------- the actor ----------

export function isAiActor(kind: ActorKind | string | null | undefined): boolean {
  return kind === 'ai-client-for-person' || kind === 'agent-identity';
}

export const sameId = (a: string | null | undefined, b: string | null | undefined): boolean => !!a && !!b && a.toLowerCase() === b.toLowerCase();

const nameOf = (p: PrincipalRef | null | undefined): string | null => (p ? p.displayLabel || p.objectId : null);

// ---------- the scope the page reads ----------

export interface AiScope {
  mine?: true;
  applicationId?: string;
  // Wave 4b review: one portfolio's applications, as the Overview's "AI drafts waiting" tile opens them.
  portfolioId?: string;
  client?: string;
  person?: string;
}

// The input every read of the page shares: the person, the client, the application and the portfolio. The view and
// paging come on top.
export function scopeInput(scope: AiScope): GetAiActivityInput {
  return {
    ...(scope.mine ? { mineOnly: true } : {}),
    ...(scope.person && !scope.mine ? { onBehalfOfObjectId: scope.person } : {}),
    ...(scope.client ? { clientApp: scope.client } : {}),
    ...(scope.applicationId ? { businessApplicationId: scope.applicationId } : {}),
    ...(scope.portfolioId ? { portfolioId: scope.portfolioId } : {}),
  };
}

// The drafts a sub-filter keeps.
export function filterDrafts(items: AiActivityItem[], only: DraftOnly | undefined): AiActivityItem[] {
  if (!only) return items;
  if (only === 'recorded') return items.filter(i => i.state === 'recorded');
  return items.filter(i => i.kind === 'draft' && (only === 'edited' ? i.editedByPersonFirst === true : isAiActor(i.takenForward?.actorKind)));
}

// ---------- the story in numbers ----------

export type StoryTarget = { view: AiActivityView; only?: DraftOnly };

export interface StoryFigure {
  key: string;
  label: string;
  value: number;
  target: StoryTarget;
  attention?: boolean;
}

export interface StoryStep {
  key: 'draft' | 'person' | 'rules';
  number: number;
  title: string;
  caption: string;
  lead: StoryFigure;
  more: StoryFigure[];
}

// The three steps of D-28 as the page tells them: an AI client drafts, a person takes it forward, the rules hold the
// line. Every figure opens the rows it counts.
export function storySteps(counts: AiActivity['counts']): StoryStep[] {
  return [
    {
      key: 'draft',
      number: 1,
      title: 'An AI client drafts',
      caption: 'Rows an AI client created for a person',
      lead: { key: 'drafts', label: 'AI drafts', value: counts.drafts, target: { view: 'drafts' } },
      more: [
        { key: 'waiting', label: 'waiting for a person', value: counts.waiting, target: { view: 'waiting' }, attention: counts.waiting > 0 },
        { key: 'withdrawn', label: 'withdrawn', value: counts.withdrawn, target: { view: 'withdrawn' } },
        // The parts add up to the drafts (wave 4b review): what a person took forward is step 2, the facts recorded here.
        ...((counts.recorded ?? 0) > 0 ? [{ key: 'recorded', label: 'recorded as facts, nothing to take forward', value: counts.recorded ?? 0, target: { view: 'drafts' as const, only: 'recorded' as const } }] : []),
      ],
    },
    {
      key: 'person',
      number: 2,
      title: 'A person takes it forward',
      caption: 'Accepted and submitted under their own name',
      lead: { key: 'accepted', label: 'Taken forward by a person', value: counts.takenForwardByPerson, target: { view: 'accepted' } },
      more: [
        { key: 'edited', label: 'changed by the person first', value: counts.editedByPersonFirst, target: { view: 'drafts', only: 'edited' } },
        { key: 'ai-submitted', label: 'submitted by the AI client, as the Tool Policy lets it', value: counts.submittedByAi, target: { view: 'drafts', only: 'ai-submitted' } },
      ],
    },
    {
      key: 'rules',
      number: 3,
      title: 'The rules hold the line',
      caption: 'Refused, never counted, or flagged',
      lead: { key: 'refused', label: 'Calls refused', value: counts.refused, target: { view: 'refused' }, attention: counts.refused > 0 },
      more: [
        { key: 'answers', label: 'AI answers, a recommendation only', value: counts.answers, target: { view: 'answers' } },
        { key: 'code-approvals', label: 'deployments with an AI-only code approval', value: counts.codeApprovals, target: { view: 'code-approvals' }, attention: counts.codeApprovals > 0 },
      ],
    },
  ];
}

// The count a tab shows: the figure of the view under the same scope. "Everything" is counts.everything, each activity
// once (wave 4b review): a deployment an AI client drafted and started is both a draft and a code approval, so the sum of
// the figures counts it twice. A server before wave 4b sends no everything, and the sum stands in.
export function viewCount(counts: AiActivity['counts'], view: AiActivityView): number {
  switch (view) {
    case 'all':
      return typeof counts.everything === 'number' ? counts.everything : counts.drafts + counts.refused + counts.answers + counts.codeApprovals;
    case 'drafts':
      return counts.drafts;
    case 'waiting':
      return counts.waiting;
    case 'accepted':
      return counts.takenForwardByPerson;
    case 'withdrawn':
      return counts.withdrawn;
    case 'refused':
      return counts.refused;
    case 'answers':
      return counts.answers;
    case 'code-approvals':
      return counts.codeApprovals;
  }
}

// ---------- one item in words ----------

// "Drafted by Claude Code for Priya Castell, 24 Nov 2026, 08:00 UTC". In the drafts view, a code approval row that an AI
// client drafted (its state is set) reads as the draft it is: "Drafted by Claude Code for Priya Castell, then recorded"
// (wave 4b review), so the drafts the tab counts are the rows it lists.
export function draftedLine(item: AiActivityItem, view?: AiActivityView): string {
  const client = item.clientApp || 'an AI client';
  const person = nameOf(item.onBehalfOf);
  const forWhom = person ? ` for ${person}` : '';
  const head = item.kind === 'draft'
    ? `Drafted by ${client}${forWhom}`
    : isDraftedCodeApproval(item, view)
      ? `Drafted by ${client}${forWhom}, then recorded`
    : item.kind === 'answer'
      ? `Answered through ${client}${forWhom}`
      : item.kind === 'refusal'
        ? `Called through ${client}${forWhom}`
        : item.state === 'recorded'
          // A deployment the AI client recorded itself and that relies on an AI-only code approval: one row (B24-17).
          ? `Recorded by ${client}${forWhom}`
          : item.clientApp ? `Started by ${item.clientApp}${forWhom}` : 'Deployed';
  return item.occurredOn ? `${head}, ${fmtDateTime(item.occurredOn)}` : head;
}

// A code approval row the drafts view lists as one of its drafts: a deployment an AI client drafted (wave 4b review).
export function isDraftedCodeApproval(item: AiActivityItem, view: AiActivityView | undefined): boolean {
  return item.kind === 'code-approval' && view === 'drafts' && item.state !== null;
}

export interface StateWords {
  label: string;
  tone: Tone;
  // What happened to the draft since, in a sentence. Null when there is nothing more to say.
  detail: string | null;
  // A note on the facts themselves, such as a row with no call history.
  note: string | null;
}

// Where an AI draft stands, for the person reading the page. mine is the reader's object ID.
export function draftState(item: AiActivityItem, mine: string | null): StateWords {
  const forMe = sameId(item.onBehalfOf?.objectId, mine);
  const person = nameOf(item.onBehalfOf) ?? 'the person it was drafted for';
  const note = item.editedByPersonFirst === null && !item.hasCallHistory
    ? 'No call history is recorded for this row, so whether a person changed it first is not known.'
    : null;
  switch (item.state) {
    case 'waiting':
      return {
        label: 'Waiting for a person',
        tone: 'warning',
        detail: forMe
          ? 'Waiting for you: accept and submit it, change it first, or withdraw it.'
          : `Waiting for ${person} to accept it, change it first, or withdraw it.`,
        note: item.editedByPersonFirst ? 'Changed by a person since the AI client drafted it.' : note,
      };
    case 'withdrawn': {
      const by = nameOf(item.withdrawn?.principal);
      const on = item.withdrawn?.on ? `, ${fmtDateTime(item.withdrawn.on)}` : '';
      return { label: 'Withdrawn', tone: 'subtle', detail: by ? `Withdrawn by ${by}${on}.` : `Withdrawn${on}.`, note: null };
    }
    case 'recorded':
      return { label: 'Recorded', tone: 'informative', detail: 'A fact or a link the AI client reported. It records what happened and decides nothing.', note: null };
    case 'submitted':
    case 'decided': {
      const parts: string[] = [];
      const forward = item.takenForward;
      let label = item.state === 'decided' ? 'Decided' : 'Submitted';
      let tone: Tone = 'brand';
      if (forward) {
        const who = nameOf(forward.principal);
        const on = forward.on ? `, ${fmtDateTime(forward.on)}` : '';
        if (isAiActor(forward.actorKind)) {
          label = 'Submitted by the AI client';
          tone = 'informative';
          parts.push(`The AI client submitted it itself${on}, as the Tool Policy lets it. People still decide.`);
        } else {
          label = 'Taken forward by a person';
          tone = 'success';
          parts.push(`Taken forward by ${who ?? 'a person'}${on}${forward.fromHistory ? '' : ' (from the last stamp on the row)'}.`);
        }
      }
      if (item.editedByPersonFirst) parts.push('The person changed it before it went forward.');
      if (item.decided) {
        const outcome = item.decided.outcome?.label;
        const who = nameOf(item.decided.principal);
        const on = item.decided.on ? `, ${fmtDateTime(item.decided.on)}` : '';
        parts.push(`Decided${outcome ? `: ${outcome}` : ''}${who ? ` by ${who}` : ''}${on}.`);
      }
      return { label, tone, detail: parts.join(' ') || null, note };
    }
    default:
      return { label: 'Draft', tone: 'informative', detail: null, note };
  }
}

// A refused call, in words: who tried what, and the rule.
export function refusalHeadline(item: AiActivityItem): string {
  const r = item.refusal;
  if (!r) return 'A refused call';
  const client = item.clientApp || 'An AI client';
  return r.knownTool ? `${client} called ${r.tool}, and the product refused it` : `${client} called ${r.tool}, a tool the product does not have`;
}

// An AI actor's approval answer, in words.
export function answerHeadline(item: AiActivityItem): string {
  const a = item.answer;
  if (!a) return 'An AI answer';
  const who = nameOf(a.approver) || item.clientApp || 'An AI actor';
  return `${who} answered "${a.outcome.label}"`;
}

// The record kinds the page can offer "Accept and submit" on: the ones submit_record takes.
const SUBMITTABLE: readonly SubmittableKind[] = ['intake-request', 'change-request', 'decision'];

export function submittableEntity(entity: string | null | undefined): SubmittableKind | null {
  return entity && (SUBMITTABLE as readonly string[]).includes(entity) ? (entity as SubmittableKind) : null;
}

// True when the reader may accept and submit this draft here: it waits, it was drafted for them, and submit_record takes
// its kind. The engine still checks the write.
export function canAcceptHere(item: AiActivityItem, mine: string | null, canSubmit: boolean): boolean {
  return canSubmit && item.kind === 'draft' && item.state === 'waiting' && sameId(item.onBehalfOf?.objectId, mine) && submittableEntity(item.record?.entity) !== null;
}

// A stable key for a list row.
export function itemKey(item: AiActivityItem): string {
  return `${item.kind}-${item.refusal?.auditEntryId ?? item.answer?.approvalRecordId ?? item.deployment?.deploymentRecordId ?? item.record?.recordId ?? item.occurredOn ?? 'none'}`;
}

// One deployment an AI client both drafted and started (wave 3 QA: "Deployment of route-planner 2.0.0-rc.1" twice, once
// "Drafted 14:40" and once "Started 11:40"): the list shows it once, as its code approval row, with the draft's line.
// A draft folds into a code approval row when both name the same deployment record. Every other row stays as it is.
export interface ListRow {
  item: AiActivityItem;
  // The draft row of the same deployment, shown inside this row.
  draft: AiActivityItem | null;
}

export function foldDeploymentDrafts(items: readonly AiActivityItem[]): { rows: ListRow[]; folded: number } {
  const deploymentOf = (i: AiActivityItem) => (i.deployment?.deploymentRecordId ?? (i.record?.entity === 'deployment-record' ? i.record.recordId : null))?.toLowerCase() ?? null;
  const approvals = new Set(items.filter(i => i.kind === 'code-approval').map(deploymentOf).filter((id): id is string => !!id));
  const drafts = new Map<string, AiActivityItem>();
  for (const i of items) {
    const id = i.kind === 'draft' ? deploymentOf(i) : null;
    if (id && approvals.has(id) && !drafts.has(id)) drafts.set(id, i);
  }
  const rows: ListRow[] = [];
  let folded = 0;
  for (const i of items) {
    const id = deploymentOf(i);
    if (i.kind === 'draft' && id && drafts.get(id) === i) {
      folded += 1;
      continue;
    }
    rows.push({ item: i, draft: i.kind === 'code-approval' && id ? drafts.get(id) ?? null : null });
  }
  return { rows, folded };
}

// The words a row holds for the page's text search: the record, the client, the person, the application, a refused
// tool and an answer's approver.
export function activityText(item: AiActivityItem): (string | null | undefined)[] {
  return [
    item.record?.label,
    item.record?.recordNumber,
    item.clientApp,
    nameOf(item.onBehalfOf),
    item.application?.name,
    item.refusal?.tool,
    item.refusal?.rule,
    nameOf(item.answer?.approver ?? null),
  ];
}

// ---------- the clients view (get_ai_activity clients, document 24 B24-18) ----------

export type ClientActivity = NonNullable<AiActivity['clients']>[number];

// Whether the Known Client list lets the client call: allowed, blocked, or not on the list.
export function clientStanding(c: Pick<ClientActivity, 'knownClientKey' | 'isAllowed'>): { text: string; tone: Tone } {
  if (!c.knownClientKey) return { text: 'Not on the Known Client list', tone: 'warning' };
  return c.isAllowed === false ? { text: 'Blocked', tone: 'danger' } : { text: 'Allowed', tone: 'success' };
}

// One client's figures in a sentence: "12 calls recorded. 5 drafts: 3 taken forward by a person, 2 waiting. 1 refused."
export function clientSentence(c: Pick<ClientActivity, 'calls' | 'drafts' | 'acceptedByPerson' | 'waiting' | 'refused'>): string {
  const n = (v: number, one: string, many: string) => `${v} ${v === 1 ? one : many}`;
  const parts = [`${n(c.calls, 'call', 'calls')} recorded.`];
  if (c.drafts) parts.push(`${n(c.drafts, 'draft', 'drafts')}: ${c.acceptedByPerson} taken forward by a person, ${c.waiting} waiting.`);
  else parts.push('No drafts.');
  parts.push(c.refused ? `${c.refused} refused.` : 'None refused.');
  return parts.join(' ');
}

// ---------- the Tool Policy per role (get_role_access tools) ----------

// How far an AI client may go with a tool (the Tool Policy's Max AI State), in words. A tool served to people only says
// so. An unknown state shows in plain words.
export const AI_STATE_TEXT: Record<string, string> = {
  'read-only': 'Reads only',
  draft: 'Drafts only: the person takes it forward',
  candidate: 'Drafts a candidate: a person submits it',
  proposed: 'Proposes only: people decide',
  submitted: 'Submits for the person: a request, never an approval',
  withdrawn: 'Withdraws only what it drafted',
  'as-reported': 'Reports facts as seen',
  link: 'Adds links',
};

export function aiStateText(maxAiState: string, personOnly: boolean): string {
  if (personOnly) return 'People only: no AI client may call it';
  return AI_STATE_TEXT[maxAiState] ?? maxAiState.replace(/-/g, ' ');
}

// What an AI client may submit through a tool, in words: "an intake request, a decision".
const SUBMIT_KIND_TEXT: Record<string, string> = {
  'intake-request': 'an intake request',
  'change-request': 'a change request',
  decision: 'a decision',
  'configuration-draft': 'a configuration draft',
  'data-use': 'a data use',
  'data-mapping': 'a data mapping',
  'review-request': 'a review request',
};

export function submitKindsText(kinds: readonly string[]): string | null {
  if (!kinds.length) return null;
  return kinds.map(k => SUBMIT_KIND_TEXT[k] ?? k.replace(/-/g, ' ')).join(', ');
}

// The Tool Policy row key of a tool: tool names use underscores, configuration keys hyphens.
export const toolPolicyKey = (tool: string): string => tool.replace(/_/g, '-');

// A role's tools grouped for the panel: the write tools in Tool Policy order, and how many it may read.
export function roleToolSummary<T extends { tool: string; number: number; access: 'read' | 'write' }>(tools: readonly T[]): { writes: T[]; reads: number } {
  return {
    writes: tools.filter(t => t.access === 'write').sort((a, b) => a.number - b.number),
    reads: tools.filter(t => t.access === 'read').length,
  };
}

// The fixed error code in words people read; the code itself stays in the audit detail (product review, step 12).
export function refusalCodeWords(code: string, knownTool = true): string {
  if (!knownTool) return 'No such tool';
  switch (code) {
    case 'ai_cannot_do_this': return 'AI clients cannot do this';
    case 'not_authorized': return 'Not allowed for this person';
    case 'rule_violation': return 'A rule refused it';
    case 'not_found': return 'Record not found';
    case 'locked': return 'Locked setting';
    case 'validation': return 'The input was not valid';
    case 'conflict': return 'Changed by someone else';
    default: return 'Refused';
  }
}
