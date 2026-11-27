// The intake pages' logic with no React in it, so the tests can run it under Node: the need text and its business
// owner line, the wizard's draft input and its own checks, the tier the answers point to, the default list view, and
// the demand buckets.
import type { IntakeDetail, IntakeSummary, LabelledKey, PrincipalRef, SaveIntakeDraftInput, StampedBy, TierQuestion } from '../../data/contracts';
import { daysBetween, dayKey } from '../../data/format';
import type { IntakeView } from '../../app/routes';

// ---------- the need text and the business owner ----------

// Requests saved before save_intake_draft took a business owner (businessOwnerObjectId) keep the owner's name as the last
// line of the need, in this exact form. The wizard reads it back from there and keeps it, and names the person with the
// real field (a person found by name), which the Accept gate counts.
export const OWNER_PREFIX = 'Business owner: ';
const OWNER_LINE = /(?:^|\n)[ \t]*Business owner: ([^\n]*\S)[ \t]*\s*$/;

export function composeNeed(purpose: string, owner: string): string {
  const p = purpose.trim();
  const o = owner.trim().replace(/\s+/g, ' ');
  if (!o) return p;
  return p ? `${p}\n\n${OWNER_PREFIX}${o}` : `${OWNER_PREFIX}${o}`;
}

export function splitNeed(need: string): { purpose: string; owner: string } {
  const match = OWNER_LINE.exec(need);
  if (!match) return { purpose: need.trim(), owner: '' };
  return { purpose: need.slice(0, match.index).trim(), owner: match[1].trim() };
}

// ---------- the wizard's draft ----------

export type UsesAiAnswer = 'yes' | 'no' | '';

export interface WizardFields {
  title: string;
  purpose: string;
  owner: string;
  proposedKindKey: string;
  usesAi: UsesAiAnswer;
  expectedBenefit: string;
  horizonKey: string;
  businessApplicationId: string;
  // For display only: the name of the application the request is about.
  applicationName: string;
  // The business owner the requester names: a person the product knows, found by name.
  businessOwner: PrincipalRef | null;
  // The portfolio the request belongs to; the application an accepted request creates starts in it (product review
  // 2026-11-24). The request's read does not return it yet, so a saved request shows it empty and keeps what it holds.
  portfolioId: string;
}

export const EMPTY_FIELDS: WizardFields = {
  title: '',
  purpose: '',
  owner: '',
  proposedKindKey: '',
  usesAi: '',
  expectedBenefit: '',
  horizonKey: '',
  businessApplicationId: '',
  applicationName: '',
  businessOwner: null,
  portfolioId: '',
};

export const LIMITS = { title: 200, need: 4000, expectedBenefit: 2000 } as const;

// The wizard's fields from a saved request.
export function fieldsOf(intake: IntakeDetail['intake']): WizardFields {
  const { purpose, owner } = splitNeed(intake.need ?? '');
  return {
    title: intake.title ?? '',
    purpose,
    owner,
    proposedKindKey: intake.proposedKind?.key ?? '',
    usesAi: intake.usesAi === true ? 'yes' : intake.usesAi === false ? 'no' : '',
    expectedBenefit: intake.expectedBenefit ?? '',
    horizonKey: intake.horizon?.key ?? '',
    businessApplicationId: intake.businessApplicationId ?? '',
    applicationName: intake.applicationName ?? '',
    businessOwner: intake.businessOwner ?? null,
    // The request's own portfolio, so the saved form matches what is stored (wave 2 recheck: the picker showed "Not
    // chosen yet" after a reload and the submit said some changes were not saved). A linked application's portfolio
    // is not the request's own choice.
    portfolioId: intake.portfolio && !intake.portfolio.fromApplication ? intake.portfolio.portfolioId.toLowerCase() : '',
  };
}

export type DraftInput = Omit<SaveIntakeDraftInput, 'clientRequestId' | 'validateOnly'>;

// The save_intake_draft input. An empty optional field is left out: the tool then keeps what the request already holds.
export function draftInput(fields: WizardFields, intakeRequestId: string | null): DraftInput {
  const input: DraftInput = { title: fields.title.trim(), need: composeNeed(fields.purpose, fields.owner) };
  if (intakeRequestId) input.intakeRequestId = intakeRequestId;
  if (fields.proposedKindKey) input.proposedKindKey = fields.proposedKindKey;
  if (fields.businessApplicationId) input.businessApplicationId = fields.businessApplicationId;
  const benefit = fields.expectedBenefit.trim();
  if (benefit) input.expectedBenefit = benefit;
  if (fields.usesAi) input.usesAi = fields.usesAi === 'yes';
  if (fields.horizonKey) input.horizonKey = fields.horizonKey;
  if (fields.businessOwner) input.businessOwnerObjectId = fields.businessOwner.objectId;
  if (fields.portfolioId) input.portfolioId = fields.portfolioId;
  return input;
}

export interface FieldProblem {
  problem: string;
  field: string;
}

// The wizard's own checks before anything is sent, with the tool input field at fault.
export function checkFields(fields: WizardFields): FieldProblem | null {
  const title = fields.title.trim();
  if (!title) return { problem: 'Give the request a short title.', field: 'title' };
  if (title.length > LIMITS.title) return { problem: `Keep the title to ${LIMITS.title} characters.`, field: 'title' };
  if (!fields.purpose.trim()) return { problem: 'Say what the business needs, in plain words.', field: 'need' };
  if (composeNeed(fields.purpose, fields.owner).length > LIMITS.need) {
    return { problem: `Keep the need and the business owner line to ${LIMITS.need} characters together.`, field: 'need' };
  }
  if (fields.expectedBenefit.trim().length > LIMITS.expectedBenefit) {
    return { problem: `Keep the expected benefit to ${LIMITS.expectedBenefit} characters.`, field: 'expectedBenefit' };
  }
  return null;
}

// The fields the person changed since the last save, by tool input field. A field emptied in the form is not a change:
// the tool keeps a saved value it is not sent (see savedValueStays).
export function changedFields(fields: WizardFields, saved: WizardFields | null): string[] {
  if (!saved) return ['new'];
  const now = draftInput(fields, null);
  const before = draftInput(saved, null);
  const changed: string[] = [];
  for (const key of Object.keys(now) as (keyof DraftInput)[]) {
    if (now[key] !== before[key]) changed.push(key);
  }
  return changed;
}

// The optional fields the person emptied although the request holds a value: the save keeps the saved value.
export function savedValueStays(fields: WizardFields, saved: WizardFields | null): (keyof WizardFields)[] {
  if (!saved) return [];
  const optional: (keyof WizardFields)[] = ['proposedKindKey', 'usesAi', 'expectedBenefit', 'horizonKey', 'businessApplicationId', 'portfolioId'];
  const stays = optional.filter(k => !String(fields[k] ?? '').trim() && !!String(saved[k] ?? '').trim());
  return !fields.businessOwner && saved.businessOwner ? [...stays, 'businessOwner'] : stays;
}

// ---------- the tier the answers point to ----------

export interface TierHint {
  tier: LabelledKey | null;
  // The questions whose answers set it: every answer at the highest tier.
  decisive: string[];
  answered: number;
  total: number;
}

// The engine's rule (OP-01): the highest tier any answer implies wins, and every answer at that tier is decisive.
// rank gives a tier's configured rank, or a negative number when it is unknown; ties on an unknown rank fall back to the
// key. The page shows this as a hint while the person answers; the engine works the tier out when the answers are saved.
export function tierFromAnswers(questions: TierQuestion[], answers: Record<string, string>, rank: (key: string) => number): TierHint {
  let best: { tier: LabelledKey; rank: number } | null = null;
  const chosen: { questionKey: string; tier: LabelledKey; rank: number }[] = [];
  for (const q of questions) {
    const answer = q.answers.find(a => a.answerKey === answers[q.questionKey]);
    if (!answer) continue;
    const r = rank(answer.impliedTier.key);
    chosen.push({ questionKey: q.questionKey, tier: answer.impliedTier, rank: r });
    if (!best || r > best.rank || (r === best.rank && r < 0 && answer.impliedTier.key > best.tier.key)) {
      best = { tier: answer.impliedTier, rank: r };
    }
  }
  const top = best;
  return {
    tier: top?.tier ?? null,
    decisive: top ? chosen.filter(c => c.tier.key === top.tier.key).map(c => c.questionKey) : [],
    answered: chosen.length,
    total: questions.length,
  };
}

// The answers in the shape answer_tier_questions takes, in question order, for the questions that apply.
export function answerList(questions: TierQuestion[], answers: Record<string, string>): { questionKey: string; answerKey: string }[] {
  return questions.flatMap(q => (q.answers.some(a => a.answerKey === answers[q.questionKey]) ? [{ questionKey: q.questionKey, answerKey: answers[q.questionKey] }] : []));
}

// ---------- who the person is to the request ----------

const same = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

// The requester, or the person a write on the request was made for or by. The engine allows the requester, the creator
// and the person an AI client acted for to submit or withdraw; it checks again on every write.
export function isRequesterOf(intake: Pick<IntakeSummary, 'requester' | 'performedBy'>, objectId: string | null | undefined): boolean {
  if (!objectId) return false;
  const stamp: StampedBy | null = intake.performedBy;
  return same(intake.requester?.objectId, objectId) || same(stamp?.onBehalfOf?.objectId, objectId) || (stamp?.actorKind === 'person' && same(stamp.principal.objectId, objectId));
}

// The stamp the AI mark reads: the last write's when an AI client made it, or any stamp that carries the drafted by
// pair (document 22, B22-15), from which the mark names the AI client even after a person or the engine wrote last
// (wave 3 verification: Triage read "Drafted by an AI client" for a row Claude Code drafted). Else null: a row an AI
// client created and a person then edited carries the person's stamp, so the mark must not take its client from it.
export function aiStampOf(performedBy: StampedBy | null): StampedBy | null {
  if (!performedBy) return null;
  if (performedBy.draftedBy) return performedBy;
  return performedBy.actorKind === 'ai-client-for-person' || performedBy.actorKind === 'agent-identity' ? performedBy : null;
}

export const OPEN_MEANINGS = ['draft', 'submitted', 'in-progress'] as const;

export function isOpen(meaning: string): boolean {
  return (OPEN_MEANINGS as readonly string[]).includes(meaning);
}

// ---------- the list ----------

// The view a person sees first: the triage queue for a platform admin, the demand list for the PMO, else their own.
export function defaultIntakeView(roleKeys: readonly string[]): IntakeView {
  if (roleKeys.includes('platform-admin')) return 'triage';
  if (roleKeys.includes('pmo')) return 'demand';
  return 'mine';
}

// Whole days since the last engine-checked write, or null when the row carries no stamp (seeded data).
export function daysSinceSaved(performedBy: StampedBy | null, today: string): number | null {
  const on = performedBy?.stampedOn;
  if (!on) return null;
  return Math.max(0, daysBetween(dayKey(on), today));
}

export interface Bucket {
  key: string | null;
  label: string;
  count: number;
}

// Counts of the items by one key with its label, "Not set" last. order sorts the keys (a configured rank); else by count.
export function bucketsOf<T>(items: T[], pick: (item: T) => LabelledKey | null, order?: (key: string) => number): Bucket[] {
  const map = new Map<string | null, Bucket>();
  for (const item of items) {
    const value = pick(item);
    const key = value?.key ?? null;
    const bucket = map.get(key) ?? { key, label: value?.label ?? 'Not set', count: 0 };
    bucket.count += 1;
    map.set(key, bucket);
  }
  return [...map.values()].sort((a, b) => {
    if ((a.key === null) !== (b.key === null)) return a.key === null ? 1 : -1;
    if (order && a.key && b.key) return order(a.key) - order(b.key) || a.label.localeCompare(b.label);
    return b.count - a.count || a.label.localeCompare(b.label);
  });
}

// The demand filters a bar opens: one dimension and one key (null is "Not set").
export type DemandDimension = 'horizon' | 'sizeBand' | 'suggestedTier' | 'status';

export function demandValue(item: IntakeSummary, dimension: DemandDimension): LabelledKey | null {
  switch (dimension) {
    case 'horizon':
      return item.horizon;
    case 'sizeBand':
      return item.sizeBand;
    case 'suggestedTier':
      return item.suggestedTier;
    default:
      return { key: item.status.meaning, label: item.status.label };
  }
}

// The priority decision of a request, from its latest decisions.
// The request's priority decision: as list_intake_requests names it, else the latest priority decision among the five.
export function priorityOf(item: IntakeSummary): Pick<IntakeSummary['decisions'][number], 'status' | 'outcome'> | null {
  if (item.priority) return item.priority;
  return item.decisions.find(d => d.decisionType.key === 'priority') ?? null;
}

// ---------- the record's progress ----------

export interface IntakeStep {
  label: string;
  state: 'done' | 'current' | 'todo' | 'stopped';
  note?: string;
}

// Draft, submitted, the intake decision, the outcome. A withdrawn or canceled request stops at the first step it did not
// reach, and says so.
export function intakeProgress(meaning: string, decision: { meaning: string; label: string } | null, outcomeLabel: string | null): IntakeStep[] {
  const stopped = meaning === 'withdrawn' || meaning === 'cancelled';
  const stopWord = meaning === 'cancelled' ? 'Canceled' : 'Withdrawn';
  const decided = !!decision && ['accepted', 'rejected', 'superseded'].includes(decision.meaning);
  const steps: IntakeStep[] = [
    { label: 'Draft', state: meaning === 'draft' ? 'current' : 'done' },
    { label: 'Submitted', state: meaning === 'draft' ? 'todo' : stopped && !decision ? 'stopped' : 'done' },
    {
      label: 'Intake decision',
      state: decided ? 'done' : decision && !stopped && decision.meaning === 'proposed' ? 'current' : stopped && decision ? 'stopped' : meaning === 'submitted' || meaning === 'in-progress' ? 'current' : 'todo',
      note: decision ? decision.label : undefined,
    },
    {
      label: 'Outcome',
      state: decided || meaning === 'completed' || meaning === 'rejected' ? 'done' : 'todo',
      note: decision?.meaning === 'rejected' || meaning === 'rejected' ? 'Declined' : decided || meaning === 'completed' ? outcomeLabel ?? 'Accepted' : undefined,
    },
  ];
  if (stopped) {
    // The first step not reached says why the request stopped; the rest are not reached.
    const at = steps.findIndex(s => s.state !== 'done');
    if (at >= 0) {
      steps[at] = { ...steps[at], state: 'stopped', note: stopWord };
      for (let i = at + 1; i < steps.length; i++) steps[i] = { label: steps[i].label, state: 'todo' };
    }
  }
  return steps;
}

// ---------- words ----------

export const DUPLICATE_REASON: Record<'name' | 'purpose' | 'component' | 'owner', string> = {
  name: 'Similar name',
  purpose: 'Similar purpose',
  component: 'Shares a technical component',
  owner: 'Same owner',
};

export function matchPercent(score: number): string {
  return `${Math.round(Math.max(0, Math.min(1, score)) * 100)}% match`;
}

// "1 of 2 approvals in", or null when none are required.
export function approvalsText(summary: { required: number; approved: number; rejected: number; pending: number }): string | null {
  if (!summary.required && !summary.approved && !summary.rejected && !summary.pending) return null;
  const parts = [`${summary.approved} of ${summary.required} ${summary.required === 1 ? 'approval' : 'approvals'} in`];
  if (summary.rejected) parts.push(`${summary.rejected} rejected`);
  if (summary.pending) parts.push(`${summary.pending} pending`);
  return parts.join(', ');
}

// ---------- what the tier needs, for one request ----------

// The approvals a request meets on its way, first: the intake decision and the decision types this tier's gates name.
// The other approval rules of the tier (a dozen or more decision types under Balanced: exceptions, data uses, retirement)
// are kept apart, so the wizard's "What the tier needs" leads with what this request will meet (wave 3 QA: step 4 listed
// all 17 decision types). Nothing is dropped: the page shows the rest behind a disclosure with its count.
export function splitApprovals<A extends { decisionTypeKey?: string }>(
  requirements: { gates: { requirements: { decisionTypeKey?: string }[] }[]; approvals: A[] },
): { now: A[]; later: A[] } {
  const named = new Set<string>(['intake']);
  for (const g of requirements.gates) for (const r of g.requirements) if (r.decisionTypeKey) named.add(r.decisionTypeKey);
  const now: A[] = [];
  const later: A[] = [];
  for (const a of requirements.approvals) (a.decisionTypeKey && named.has(a.decisionTypeKey) ? now : later).push(a);
  return { now, later };
}
