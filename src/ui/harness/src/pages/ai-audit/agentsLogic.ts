// The Agents page's logic with no React in it: what each agent's records say about its autonomy level, its security
// review, its go-live gate and its owners, which agents need attention, and whether the reader's roles let them propose
// a decision. The engine still decides every proposal; these checks only decide whether a button shows.
// The page reads list_agents (tool 93) for the register; the older helpers below still read the card, owners and
// requirements shapes, for other pages and the tests.
import type { AgentList, ApplicationCard, ApplicationOwners, DecisionLogItem, IsoDate, LabelledKey, Requirements } from '../../data/contracts';
import { fmtDate } from '../../data/format';
import type { Tone } from '../../data/meanings';
import { sameId } from './aiActivityLogic';

// The application kind of an agent, and the decision types the page proposes (Balanced: document 21, B21-9).
export const AGENT_KIND = 'agent';
export const AUTONOMY_TYPE = 'autonomy-level';
export const FLAGGED_TYPE = 'flagged-agent';
export const SECURITY_REVIEW_TYPE = 'security-review';
export const GO_LIVE_GATE = 'go-live';
// The component kinds that link an agent to Microsoft's own registries, when the application kind names none.
export const REGISTRY_KINDS_FALLBACK = ['copilot-studio-agent', 'entra-agent-identity'];

export const AGENT_NEEDS = ['autonomy', 'owner', 'security', 'go-live'] as const;
export type AgentNeed = (typeof AGENT_NEEDS)[number];

export const NEED_TEXT: Record<AgentNeed, { label: string; filter: string; empty: string }> = {
  autonomy: { label: 'No autonomy level agreed', filter: 'No autonomy level', empty: 'Every agent has an agreed autonomy level.' },
  owner: { label: 'Owner gap', filter: 'Owner gaps', empty: 'Every agent has its owner roles held, each with a deputy.' },
  security: { label: 'Security review overdue or missing', filter: 'Security review overdue or missing', empty: 'Every agent has a security review that is not overdue.' },
  'go-live': { label: 'Go-live gate not met', filter: 'Go-live gate not met', empty: 'No agent before go-live has a requirement left open.' },
};

// ---------- security review ----------

export interface ReviewState {
  state: 'overdue' | 'due' | 'done' | 'none';
  text: string;
  tone: Tone;
  reviewId: string | null;
}

// The agent's security review: an open one first (the earliest due), else the latest done. today is YYYY-MM-DD.
export function securityReviewOf(reviews: ApplicationCard['reviews'] | undefined, today: IsoDate, typeKey = SECURITY_REVIEW_TYPE): ReviewState {
  const list = (reviews ?? []).filter(r => r.reviewType.key === typeKey);
  const open = list.filter(r => r.status.meaning === 'due').sort((a, b) => (a.dueOn ?? '9999').localeCompare(b.dueOn ?? '9999'));
  if (open.length) {
    const r = open[0];
    const overdue = !!r.dueOn && r.dueOn.slice(0, 10) < today;
    return overdue
      ? { state: 'overdue', text: `Overdue since ${fmtDate(r.dueOn)}`, tone: 'danger', reviewId: r.reviewId }
      : { state: 'due', text: r.dueOn ? `Due ${fmtDate(r.dueOn)}` : 'Due', tone: 'warning', reviewId: r.reviewId };
  }
  const done = list.filter(r => r.status.meaning === 'done').sort((a, b) => (b.doneOn ?? '').localeCompare(a.doneOn ?? ''));
  if (done.length) {
    const r = done[0];
    return { state: 'done', text: r.doneOn ? `Done ${fmtDate(r.doneOn)}` : 'Done', tone: 'success', reviewId: r.reviewId };
  }
  return { state: 'none', text: 'No security review', tone: 'danger', reviewId: null };
}

// ---------- the go-live gate ----------

export type GateRequirement = Requirements['gates'][number]['requirements'][number];

export interface GateReadiness {
  met: number;
  total: number;
  unmet: GateRequirement[];
}

// How far the agent is toward its go-live gate, from get_requirements for the application. Null when the rules in force
// set no requirement on that gate, or the read did not say which are met.
export function gateReadiness(requirements: Requirements | null | undefined, gateKey = GO_LIVE_GATE): GateReadiness | null {
  const gate = requirements?.gates.find(g => g.gateKey === gateKey);
  if (!gate || !gate.requirements.length) return null;
  if (gate.requirements.some(r => r.met === undefined)) return null;
  const unmet = gate.requirements.filter(r => r.met === false);
  return { met: gate.requirements.length - unmet.length, total: gate.requirements.length, unmet };
}

export interface RequirementLabels {
  decisionType(key: string): string;
  documentType(key: string): string;
  reviewType(key: string): string;
}

// One gate requirement in words, such as "Autonomy level decision accepted".
export function requirementText(r: GateRequirement, labels: RequirementLabels): string {
  if (r.requirementKind === 'decision-accepted' && r.decisionTypeKey) return `${labels.decisionType(r.decisionTypeKey)} decision accepted`;
  if (r.requirementKind === 'document-current' && r.documentTypeKey) return `${labels.documentType(r.documentTypeKey)} current`;
  if (r.requirementKind === 'review-done' && r.reviewTypeKey) return `${labels.reviewType(r.reviewTypeKey)} done`;
  return r.requirementKind.replace(/-/g, ' ');
}

// Where an agent stands against go-live: the stage's configured engine meaning when the read carries it, else its rank
// against the stage the go-live gate follows (gate afterStageKey). get_configuration returns a stage's rank but not its
// engine meaning, so the rank decides in practice. Null when neither is known.
export function lifecycleOf(meaning: unknown, stageRank: number | undefined, goLiveAfterRank: number | undefined): 'pre-live' | 'live' | 'retired' | null {
  if (meaning === 'pre-live' || meaning === 'live' || meaning === 'retired') return meaning;
  if (typeof stageRank !== 'number' || typeof goLiveAfterRank !== 'number') return null;
  return stageRank <= goLiveAfterRank ? 'pre-live' : 'live';
}

// ---------- owners ----------

export interface OwnerGapText {
  ownerRoleKey: string;
  text: string;
  holderMissing: boolean;
}

export function ownerGaps(owners: ApplicationOwners | null | undefined, label: (key: string) => string): OwnerGapText[] {
  return (owners?.gaps ?? []).map(g => ({
    ownerRoleKey: g.ownerRoleKey,
    holderMissing: g.missing === 'holder',
    text: g.missing === 'holder' ? `No ${label(g.ownerRoleKey).toLowerCase()}` : `No deputy ${label(g.ownerRoleKey).toLowerCase()}`,
  }));
}

// The owner roles the reader holds on the agent, as holder or deputy.
export function myOwnerRoles(owners: ApplicationCard['owners'] | undefined, objectId: string | null): string[] {
  if (!objectId) return [];
  return [...new Set((owners ?? []).filter(o => sameId(o.principal.objectId, objectId)).map(o => o.ownerRoleKey))];
}

// The holder (not a deputy) of an owner role, by name.
export function holderName(owners: ApplicationCard['owners'] | undefined, ownerRoleKey: string): string | null {
  const o = (owners ?? []).find(x => x.ownerRoleKey === ownerRoleKey && !x.isDeputy);
  return o ? o.principal.displayLabel || o.principal.objectId : null;
}

// ---------- links to Microsoft's registries ----------

export function registryLinks(components: ApplicationCard['components'] | undefined, kindKeys: readonly string[]): { key: string; label: string; url: string | null; externalId: string }[] {
  return (components ?? [])
    .filter(c => kindKeys.includes(c.kind.key))
    .map(c => ({ key: c.technicalComponentId, label: c.kind.label, url: c.url, externalId: c.externalId }));
}

// ---------- the facts of one agent ----------

export interface AgentFacts {
  businessApplicationId: string;
  name: string;
  tier: LabelledKey | null;
  stage: LabelledKey | null;
  // pre-live, live or retired, from the lifecycle stage's configured meaning; null when unknown.
  lifecycle: string | null;
  autonomy: LabelledKey | null;
  // False while the card is read or when its read failed: the autonomy level is then not known.
  autonomyKnown: boolean;
  review: ReviewState | null;
  gate: GateReadiness | null;
  gaps: OwnerGapText[] | null;
  autonomyWaiting: DecisionLogItem[];
  flagged: DecisionLogItem[];
}

// The needs of one agent: each one a reason it shows under "needs attention". A fact not read yet is no need.
export function needsOf(f: AgentFacts): AgentNeed[] {
  const needs: AgentNeed[] = [];
  if (f.autonomyKnown && !f.autonomy && f.lifecycle !== 'retired') needs.push('autonomy');
  if (f.gaps && f.gaps.length > 0) needs.push('owner');
  if (f.review && (f.review.state === 'overdue' || f.review.state === 'none') && f.lifecycle !== 'retired') needs.push('security');
  if (f.gate && f.gate.met < f.gate.total && f.lifecycle === 'pre-live') needs.push('go-live');
  return needs;
}

// The decisions of the log about one application, by type.
export function decisionsFor(items: DecisionLogItem[], applicationId: string, typeKey: string): DecisionLogItem[] {
  return items.filter(d => d.decisionType.key === typeKey && d.subjectKind === 'business-application' && sameId(d.subjectId, applicationId));
}

// ---------- who may propose ----------

export interface DecisionRightRow {
  key: string;
  isActive: boolean;
  fields: Record<string, unknown>;
}

const PROPOSING = ['responsible', 'accountable', 'consulted'];

// True when a decision right of the type names one of the reader's roles or owner roles with a letter that may propose
// (responsible, accountable or consulted; document 21, C31). The engine checks the single most specific group itself,
// so this only decides whether the button shows.
export function mayPropose(rights: DecisionRightRow[], decisionTypeKey: string, roleKeys: string[], ownerRoleKeys: string[]): boolean {
  return rights.some(r => {
    if (!r.isActive || r.fields.decisionTypeKey !== decisionTypeKey || !PROPOSING.includes(String(r.fields.letter))) return false;
    const role = typeof r.fields.roleKey === 'string' ? r.fields.roleKey : null;
    const owner = typeof r.fields.ownerRoleKey === 'string' ? r.fields.ownerRoleKey : null;
    return (!!role && roleKeys.includes(role)) || (!!owner && ownerRoleKeys.includes(owner));
  });
}

// ---------- the autonomy proposal ----------

export interface AutonomyForm {
  levelKey: string;
  levelLabel: string;
  agentName: string;
  context: string;
  reason: string;
}

export interface Problem {
  problem: string;
  field?: string;
}

// The save_decision_proposal input of an autonomy level proposal. autonomyLevelKey is the level the decision stores on
// the agent once people accept it with the outcome agree.
export function autonomyProposal(form: AutonomyForm, businessApplicationId: string, outcomeKey: string | null): Problem | {
  decisionTypeKey: string;
  subjectKind: 'business-application';
  subjectId: string;
  statement: string;
  context: string;
  proposedOutcomeKey: string;
  reason: string;
  autonomyLevelKey: string;
} {
  if (!form.levelKey) return { problem: 'Choose the autonomy level you propose.', field: 'autonomyLevelKey' };
  if (!outcomeKey) return { problem: 'The autonomy level decision type has no outcome to agree a level. Ask a configuration admin.', field: 'proposedOutcomeKey' };
  const context = form.context.trim();
  const reason = form.reason.trim();
  if (!context) return { problem: 'Say what the agent does and what it may do on its own.', field: 'context' };
  if (!reason) return { problem: 'Say why this level is right.', field: 'reason' };
  return {
    decisionTypeKey: AUTONOMY_TYPE,
    subjectKind: 'business-application',
    subjectId: businessApplicationId,
    statement: `Agree the autonomy level ${form.levelLabel} for ${form.agentName}`,
    context,
    proposedOutcomeKey: outcomeKey,
    reason,
    autonomyLevelKey: form.levelKey,
  };
}

// The outcome of the autonomy level type that agrees a level. The engine stores the level only on the fixed outcome key
// "agree" (EngineKeys.AutonomyAgreeOutcome), so no other outcome stands in for it.
export const AGREE_OUTCOME = 'agree';

export function agreeOutcome(options: { key: string }[]): string | null {
  return options.some(o => o.key === AGREE_OUTCOME) ? AGREE_OUTCOME : null;
}

// ---------- the agent register from list_agents (tool 93) ----------
// list_agents returns every agent in one call, with the facts the page once composed from four reads per agent. These
// helpers turn one item into the page's words. The server decides what is flagged; the page only names it.

export type AgentListItem = AgentList['items'][number];

// The security review of one agent as the register shows it. A missing review is a need only when the go-live gate
// requires one (requiredAtGoLive); the server flags it then.
export function reviewOfAgent(r: AgentListItem['securityReview']): ReviewState & { required: boolean } {
  const required = r.requiredAtGoLive;
  switch (r.state) {
    case 'overdue':
      return { state: 'overdue', text: r.dueOn ? `Overdue since ${fmtDate(r.dueOn)}` : 'Overdue', tone: 'danger', reviewId: r.reviewId, required };
    case 'due':
      return { state: 'due', text: r.dueOn ? `Due ${fmtDate(r.dueOn)}` : 'Due', tone: 'warning', reviewId: r.reviewId, required };
    case 'done':
      return { state: 'done', text: r.lastDoneOn ? `Done ${fmtDate(r.lastDoneOn)}` : 'Done', tone: 'success', reviewId: r.reviewId, required };
    default:
      return required
        ? { state: 'none', text: 'No security review', tone: 'danger', reviewId: null, required }
        : { state: 'none', text: 'None yet, not required', tone: 'subtle', reviewId: null, required };
  }
}

// The owner gaps of one agent in words: "No business owner", "No deputy technical owner".
export function agentGaps(item: Pick<AgentListItem, 'ownerGaps'>): OwnerGapText[] {
  return item.ownerGaps.map(g => {
    const label = (g.ownerRole?.label ?? 'owner').toLowerCase();
    const holderMissing = g.missing === 'holder';
    return { ownerRoleKey: g.ownerRole?.key ?? 'owner', holderMissing, text: holderMissing ? `No ${label}` : `No deputy ${label}` };
  });
}

// The owner roles the reader holds on the agent, as holder or deputy.
export function myAgentOwnerRoles(item: Pick<AgentListItem, 'owners'>, objectId: string | null): string[] {
  if (!objectId) return [];
  return [...new Set(item.owners.filter(o => o.ownerRole && sameId(o.principal?.objectId, objectId)).map(o => o.ownerRole!.key))];
}

// The holder (not a deputy) of an owner role on the agent, by name.
export function agentHolder(item: Pick<AgentListItem, 'owners'>, ownerRoleKey: string): string | null {
  const o = item.owners.find(x => x.ownerRole?.key === ownerRoleKey && !x.isDeputy && x.principal);
  return o?.principal ? o.principal.displayLabel || o.principal.objectId : null;
}

// The needs of one agent, from the server's facts: no agreed autonomy level, an owner gap, a security review the server
// flags (missing where required, or overdue), a go-live gate not ready before go-live. A retired agent needs none of the
// first, third or fourth.
export function needsOfAgent(item: Pick<AgentListItem, 'autonomy' | 'ownerGaps' | 'flags' | 'goLive'>, lifecycle: string | null): AgentNeed[] {
  const needs: AgentNeed[] = [];
  const retired = lifecycle === 'retired';
  if (!item.autonomy.agreed && !retired) needs.push('autonomy');
  if (item.ownerGaps.length > 0) needs.push('owner');
  if (!retired && item.flags.some(f => f.kind === 'security-review-missing' || f.kind === 'security-review-overdue')) needs.push('security');
  if (item.goLive && !item.goLive.ready && lifecycle === 'pre-live') needs.push('go-live');
  return needs;
}

// The data uses of an agent in words, or null when it has none open.
export function dataUsesText(d: AgentListItem['dataUses']): string | null {
  if (!d.open && !d.drafts) return null;
  const parts = [d.open ? `${d.open} open ${d.open === 1 ? 'data use' : 'data uses'}` : null, d.reachingAi ? `${d.reachingAi} reach AI` : null, d.drafts ? `${d.drafts} drafted` : null].filter(Boolean);
  return parts.join(', ');
}
