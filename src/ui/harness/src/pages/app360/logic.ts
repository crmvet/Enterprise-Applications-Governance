// Application 360's logic with no React in it: the sections (tabs), the lifecycle stepper and the gates with each
// requirement met or not, which decision types the person may propose, the tier a tier decision sets, the data answers,
// the retirement step, the dependency edges the map writes, and the owner hand-over. The tests in logic.test.ts run it
// in Node. The checks that decide whether an action shows only hide what the engine would refuse anyway; the engine
// still checks every write.
import type {
  ApplicationCard,
  ApplicationOwners,
  ApprovalItem,
  ConfigurationRows,
  DecisionLogItem,
  DeploymentFlags,
  DeploymentRouteMeaning,
  GateLevel,
  ImpactNode,
  OwnerAssignment,
  ProposeOwnerChangeInput,
  Requirements,
  SaveDecisionProposalInput,
  StampedBy,
  TierAssessmentDetail,
} from '../../data/contracts';
import type { Tone } from '../../data/meanings';

export type ConfigRow = ConfigurationRows['rows'][number];

// The tone of a criticality key: high red, medium amber, low blue, not set gray. Always shown with its words.
export function criticalityTone(key: string | null | undefined): Tone {
  return key === 'high' ? 'danger' : key === 'medium' ? 'warning' : key === 'low' ? 'informative' : 'subtle';
}

export const sameId = (a: string | null | undefined, b: string | null | undefined): boolean =>
  !!a && !!b && a.toLowerCase() === b.toLowerCase();

const text = (fields: Record<string, unknown>, name: string): string | null => {
  const v = fields[name];
  return typeof v === 'string' && v ? v : null;
};

const num = (fields: Record<string, unknown>, name: string): number | null => {
  const v = fields[name];
  return typeof v === 'number' ? v : null;
};

// "a tier decision", "an intake decision": the words with the article they take.
export function withArticle(words: string, capital = false): string {
  const article = /^[aeiou]/i.test(words) ? 'an' : 'a';
  return `${capital ? article.charAt(0).toUpperCase() + article.slice(1) : article} ${words}`;
}

export interface Problem {
  problem: string;
  field?: string;
}

export const isProblem = (value: unknown): value is Problem =>
  typeof value === 'object' && value !== null && typeof (value as Problem).problem === 'string';

// ---------------------------------------------------------------------------------------------------------------------
// Sections
// ---------------------------------------------------------------------------------------------------------------------

// The sections of Application 360, as ?tab=<key> (the route stays /app/records/business-application/<id>). The design's
// owners section is the summary's owners panel, so ?tab=owners opens the summary.
export const APP360_TABS = ['summary', 'lifecycle', 'tier', 'data', 'work', 'decisions', 'evidence'] as const;
export type App360Tab = (typeof APP360_TABS)[number];
export const TAB_PATTERN = /^(summary|lifecycle|tier|data|owners|work|decisions|evidence)$/;

export const TAB_LABELS: Record<App360Tab, string> = {
  summary: 'Summary',
  lifecycle: 'Lifecycle and gates',
  tier: 'Tier',
  data: 'Data',
  work: 'Changes and deployments',
  decisions: 'Decisions and approvals',
  evidence: 'AI and evidence',
};

export function tabOf(value: string | undefined): App360Tab {
  if (!value || value === 'owners') return 'summary';
  return (APP360_TABS as readonly string[]).includes(value) ? (value as App360Tab) : 'summary';
}

// ---------------------------------------------------------------------------------------------------------------------
// Card fields the server returns that data/contracts.ts does not type yet (get_application_card.output.schema.json)
// ---------------------------------------------------------------------------------------------------------------------

export interface PortfolioStep {
  portfolioId: string;
  name: string;
}

// The application's portfolio path, top first. Empty when the card holds none.
export function portfolioPathOf(card: ApplicationCard | null): PortfolioStep[] {
  const raw = (card?.application as { portfolio?: unknown } | undefined)?.portfolio;
  if (!Array.isArray(raw)) return [];
  return raw.filter((p): p is PortfolioStep => !!p && typeof p.portfolioId === 'string' && typeof p.name === 'string');
}

export interface CardDeploymentExtras {
  route: { key: string; meaning: DeploymentRouteMeaning } | null;
  flags: DeploymentFlags | null;
  gateLevelApplied: GateLevel | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
}

// The route, flags, gate level and stamp of a card deployment row, when the server sent them.
export function deploymentExtras(row: NonNullable<ApplicationCard['deployments']>[number]): CardDeploymentExtras {
  const r = row as typeof row & Partial<{ route: CardDeploymentExtras['route']; flags: DeploymentFlags; gateLevelApplied: GateLevel; isAiCreated: boolean; performedBy: StampedBy | null }>;
  const flags = r.flags && typeof r.flags === 'object' ? r.flags : null;
  return {
    route: r.route && typeof r.route.meaning === 'string' ? r.route : null,
    flags,
    gateLevelApplied: typeof r.gateLevelApplied === 'string' ? r.gateLevelApplied : null,
    isAiCreated: r.isAiCreated === true,
    performedBy: r.performedBy ?? null,
  };
}

// The stamp of a card change row, when the server sent it.
export function changeStamp(row: NonNullable<ApplicationCard['changes']>[number]): { isAiCreated: boolean; performedBy: StampedBy | null } {
  const r = row as typeof row & Partial<{ isAiCreated: boolean; performedBy: StampedBy | null }>;
  return { isAiCreated: r.isAiCreated === true, performedBy: r.performedBy ?? null };
}

// ---------------------------------------------------------------------------------------------------------------------
// Owners
// ---------------------------------------------------------------------------------------------------------------------

// An assignment that holds now: started (its ownership decision accepted) and not ended.
export function isActiveAssignment(o: OwnerAssignment, today: string): boolean {
  return !!o.startOn && o.startOn <= today && (!o.endOn || o.endOn >= today);
}

// The owner roles the person holds on the application now, as holder or deputy.
export function myOwnerRoles(owners: ApplicationOwners | null, objectId: string | null, today: string): string[] {
  if (!owners || !objectId) return [];
  return [...new Set(owners.owners.filter(o => sameId(o.principal.objectId, objectId) && isActiveAssignment(o, today)).map(o => o.ownerRoleKey))];
}

// The current holder (not the deputy) of an owner role, or null.
export function holderOf(owners: ApplicationOwners | null, ownerRoleKey: string, today: string): OwnerAssignment | null {
  return owners?.owners.find(o => o.ownerRoleKey === ownerRoleKey && !o.isDeputy && isActiveAssignment(o, today)) ?? null;
}

export function deputiesOf(owners: ApplicationOwners | null, ownerRoleKey: string, today: string): OwnerAssignment[] {
  return owners?.owners.filter(o => o.ownerRoleKey === ownerRoleKey && o.isDeputy && isActiveAssignment(o, today)) ?? [];
}

// Hand over (graduation, D-36): one ownership decision that names the new holder of an owner role, ends
// the holder it replaces, and names a new deputy, ending the deputy it replaces. One propose_owner_change call, so the
// approvers answer it once.
export interface HandOverForm {
  applicationId: string;
  ownerRoleKey: string;
  holderObjectId: string;
  endingHolder: OwnerAssignment | null;
  deputyObjectId: string;
  endingDeputy: OwnerAssignment | null;
  startOn: string;
  reason: string;
}

export type HandOverInput = Omit<ProposeOwnerChangeInput, 'clientRequestId' | 'validateOnly'>;

export function buildHandOver(form: HandOverForm): HandOverInput | Problem {
  const holder = form.holderObjectId.trim();
  const deputy = form.deputyObjectId.trim();
  if (!holder) return { problem: 'Choose the new holder.', field: 'changes.addObjectId' };
  if (deputy && sameId(deputy, holder)) return { problem: 'The deputy must be someone other than the new holder.', field: 'changes.addObjectId' };
  if (form.endingHolder && sameId(form.endingHolder.principal.objectId, holder)) return { problem: 'The new holder already holds the role. Choose someone else, or leave the holder as it is.', field: 'changes.addObjectId' };
  if (form.startOn && !/^\d{4}-\d{2}-\d{2}$/.test(form.startOn)) return { problem: 'The start date is not a date.', field: 'changes.startOn' };
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say why the role changes hands. The approvers read it.', field: 'reason' };
  const start = form.startOn ? { startOn: form.startOn } : {};
  const changes: HandOverInput['changes'] = [{
    ownerRoleKey: form.ownerRoleKey,
    addObjectId: holder,
    ...(form.endingHolder ? { endAssignmentId: form.endingHolder.ownerAssignmentId } : {}),
    ...start,
  }];
  if (deputy) {
    changes.push({
      ownerRoleKey: form.ownerRoleKey,
      addObjectId: deputy,
      isDeputy: true,
      ...(form.endingDeputy && !sameId(form.endingDeputy.principal.objectId, deputy) ? { endAssignmentId: form.endingDeputy.ownerAssignmentId } : {}),
      ...start,
    });
  }
  return { businessApplicationId: form.applicationId, changes, reason };
}

// ---------------------------------------------------------------------------------------------------------------------
// Lifecycle and gates
// ---------------------------------------------------------------------------------------------------------------------

export type StepState = 'done' | 'current' | 'upcoming';

export interface StageStep {
  key: string;
  label: string;
  number: number | null;
  meaning: string | null;
  state: StepState;
}

// The lifecycle stages in configured order, each done, current or upcoming against the application's stage.
export function lifecycleSteps(stages: ConfigRow[], currentStageKey: string | null): StageStep[] {
  const ordered = stages
    .filter(s => s.isActive)
    .map((s, i) => ({ s, rank: num(s.fields, 'stageRank') ?? num(s.fields, 'stageNumber') ?? i, i }))
    .sort((a, b) => a.rank - b.rank || a.i - b.i);
  const at = ordered.findIndex(x => x.s.key === currentStageKey);
  return ordered.map((x, i) => ({
    key: x.s.key,
    label: x.s.label,
    number: num(x.s.fields, 'stageNumber'),
    meaning: text(x.s.fields, 'engineMeaning'),
    state: at < 0 ? 'upcoming' : i < at ? 'done' : i === at ? 'current' : 'upcoming',
  }));
}

// The two data requirement kinds (docs/architecture/data-taxonomy.md, Gate requirements) are judged by the engine and read from get_requirements;
// they have no type key, and the page links them to the Data section.
export type RequirementKind = 'decision-accepted' | 'document-current' | 'review-done' | 'owner-assigned' | 'data-uses-declared' | 'data-uses-decided';

export interface GateRequirementView {
  key: string;
  label: string;
  kind: RequirementKind | null;
  // The decision type, document type, review type or owner role the requirement names.
  typeKey: string | null;
  guidelineKey: string | null;
  // True or false; null when the page could not work it out (a read failed or is still loading).
  met: boolean | null;
}

export interface GateDecisionRef {
  decisionId: string;
  statement: string;
  status: DecisionLogItem['status'];
  outcome: DecisionLogItem['outcome'];
  recordUrl: string;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
}

export type GateReach = 'passed' | 'next' | 'ahead';

export interface GateView {
  key: string;
  label: string;
  afterStageKey: string | null;
  decisionTypeKey: string | null;
  reach: GateReach;
  requirements: GateRequirementView[];
  unmet: number;
  unknown: number;
  // The latest gate decision about the application: an open (proposed) one first, else the latest settled one.
  open: GateDecisionRef | null;
  settled: GateDecisionRef | null;
}

const KINDS: RequirementKind[] = ['decision-accepted', 'document-current', 'review-done', 'owner-assigned', 'data-uses-declared', 'data-uses-decided'];

function requirementTypeKey(fields: Record<string, unknown>, kind: string | null): string | null {
  switch (kind) {
    case 'decision-accepted': return text(fields, 'requiredDecisionTypeKey');
    case 'document-current': return text(fields, 'requiredDocumentTypeKey');
    case 'review-done': return text(fields, 'requiredReviewTypeKey');
    case 'owner-assigned': return text(fields, 'requiredOwnerRoleKey');
    default: return null;
  }
}

// The gate requirement rows that apply to an application of this tier and kind (a row with no tier or kind applies to
// every one), as get_requirements chooses them.
export function applicableRequirements(rows: ConfigRow[], gateKey: string, tierKey: string | null, kindKey: string | null): ConfigRow[] {
  return rows
    .filter(r => r.isActive && text(r.fields, 'gateKey') === gateKey)
    .filter(r => {
      const t = text(r.fields, 'tierKey');
      const k = text(r.fields, 'applicationKindKey');
      return (!t || t === tierKey) && (!k || k === kindKey);
    })
    .sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
}

function decisionRef(d: DecisionLogItem): GateDecisionRef {
  return { decisionId: d.decisionId, statement: d.statement, status: d.status, outcome: d.outcome, recordUrl: d.recordUrl, isAiCreated: d.isAiCreated, performedBy: d.performedBy };
}

// The decisions a gate of the application takes: every decision the log holds for the application (the log is read for
// it), whatever its subject kind. The design decision is about the architecture review, the priority decision about the
// request that started the application; each still passes the application's gate (product review 2026-11-24).
export function gateDecisionsOf(decisions: DecisionLogItem[]): DecisionLogItem[] {
  return decisions.filter(d => d.subjectKind !== 'change-request' && d.subjectKind !== 'configuration-draft');
}

// The decisions about the application itself (not its changes or intake requests), newest first as the log gives them.
export function aboutApplication(decisions: DecisionLogItem[], applicationId: string): DecisionLogItem[] {
  return decisions.filter(d => d.subjectKind === 'business-application' && sameId(d.subjectId, applicationId));
}

// The open (proposed) and the latest settled decision of one type about the application.
export function latestOfType(decisions: DecisionLogItem[], typeKey: string | null): { open: GateDecisionRef | null; settled: GateDecisionRef | null } {
  if (!typeKey) return { open: null, settled: null };
  const ofType = decisions.filter(d => d.decisionType.key === typeKey);
  const open = ofType.find(d => d.status.meaning === 'proposed') ?? null;
  const settled = ofType.find(d => d.status.meaning === 'accepted' || d.status.meaning === 'rejected') ?? null;
  return { open: open ? decisionRef(open) : null, settled: settled ? decisionRef(settled) : null };
}

export interface GateInputs {
  gates: ConfigRow[];
  requirementRows: ConfigRow[];
  // get_requirements for the application: met for the decision, document and review requirements. Null while it loads
  // or when it failed.
  requirements: Requirements | null;
  owners: ApplicationOwners | null;
  decisions: DecisionLogItem[];
  stageRank: (stageKey: string | null) => number;
  currentStageKey: string | null;
  tierKey: string | null;
  kindKey: string | null;
  today: string;
}

// Each gate with its requirements met or not, in lifecycle order. get_requirements says met for the decision, document
// and review requirements; it does not list the owner-assigned ones, so the page works those out from the owners: the
// role has a holder now. get_requirements names no requirement key, so each of its rows is matched to the configured
// row of the same gate, kind and type, in key order, the order it lists them in.
export function gateViews(input: GateInputs): GateView[] {
  const currentRank = input.currentStageKey ? input.stageRank(input.currentStageKey) : -1;
  const ordered = input.gates
    .filter(g => g.isActive)
    .map(g => ({ g, rank: input.stageRank(text(g.fields, 'afterStageKey')) }))
    .sort((a, b) => a.rank - b.rank || (a.g.key < b.g.key ? -1 : 1));
  let nextGiven = false;
  return ordered.map(({ g, rank }) => {
    const rows = applicableRequirements(input.requirementRows, g.key, input.tierKey, input.kindKey);
    const fromRead = input.requirements?.gates.find(x => x.gateKey === g.key)?.requirements ?? null;
    const used = new Set<number>();
    const requirements: GateRequirementView[] = rows.map(r => {
      const kindText = text(r.fields, 'requirementKind');
      const kind = (KINDS as string[]).includes(kindText ?? '') ? (kindText as RequirementKind) : null;
      const typeKey = requirementTypeKey(r.fields, kind);
      let met: boolean | null = null;
      if (kind === 'owner-assigned') {
        met = input.owners && typeKey ? holderOf(input.owners, typeKey, input.today) !== null : null;
      } else if (fromRead && kind) {
        const at = fromRead.findIndex((x, i) => !used.has(i) && x.requirementKind === kind
          && (x.decisionTypeKey ?? x.documentTypeKey ?? x.reviewTypeKey ?? null) === typeKey);
        if (at >= 0) {
          used.add(at);
          met = fromRead[at].met ?? null;
        }
      }
      return { key: r.key, label: r.label, kind, typeKey, guidelineKey: text(r.fields, 'guidelineKey'), met };
    });
    const reach: GateReach = currentRank >= 0 && rank < currentRank ? 'passed' : nextGiven ? 'ahead' : 'next';
    if (reach === 'next') nextGiven = true;
    const decisionTypeKey = text(g.fields, 'gateDecisionTypeKey');
    return {
      key: g.key,
      label: g.label,
      afterStageKey: text(g.fields, 'afterStageKey'),
      decisionTypeKey,
      reach,
      requirements,
      unmet: requirements.filter(r => r.met === false).length,
      unknown: requirements.filter(r => r.met === null).length,
      ...latestOfType(input.decisions, decisionTypeKey),
    };
  });
}

export type GateStanding = 'passed' | 'passed-unmet' | 'ready' | 'held' | 'checking';

// The outcomes that decide a gate decision without letting the application through: rework a design, defer or stop a
// priority, no go, park or decline a request. Any other accepted outcome passes the gate. (D-41, 2026-11-24.)
const HOLDING_OUTCOMES = ['rework', 'defer', 'stop', 'no-go', 'park', 'decline', 'redirect', 'reject'];

// True when the gate's latest settled decision is accepted with an outcome that lets the application through.
export function gateDecisionPassed(gate: GateView): boolean {
  const d = gate.settled;
  return !!d && d.status.meaning === 'accepted' && !(d.outcome && HOLDING_OUTCOMES.includes(d.outcome.key));
}

// How a gate stands now, in one word for its badge. A gate is passed once the application's stage is past it, or once
// its decision is accepted with an outcome that lets the application through (product review 2026-11-24: Application
// 360 showed "Ready" for a gate whose decision was accepted). An accepted holding outcome (rework, defer, stop, no go)
// holds it.
export function gateStanding(gate: GateView): GateStanding {
  if (gate.reach === 'passed' || gateDecisionPassed(gate)) return gate.unmet > 0 ? 'passed-unmet' : 'passed';
  if (gate.settled && gate.settled.status.meaning === 'accepted' && !gate.open) return 'held';
  if (gate.unmet > 0) return 'held';
  if (gate.unknown > 0) return 'checking';
  return 'ready';
}

// ---------------------------------------------------------------------------------------------------------------------
// Decision rights: which decision types the person may propose about the application
// ---------------------------------------------------------------------------------------------------------------------

// The letters whose holders may propose (the engine's ProposalRightsOperation: responsible, accountable, consulted).
const PROPOSING_LETTERS = ['responsible', 'accountable', 'consulted'];

export interface ProposerScope {
  roleKeys: readonly string[];
  ownerRoleKeys: readonly string[];
  tierKey: string | null;
  kindKey: string | null;
}

// The decision types about a business application whose decision rights name the person: a product role they hold,
// or an owner role they hold on this application. Rows scoped to another tier or kind, and rows for change types, do
// not apply to the application itself. In configured order.
export function proposableTypes(types: ConfigRow[], rights: ConfigRow[], who: ProposerScope): string[] {
  const roles = new Set(who.roleKeys);
  const ownerRoles = new Set(who.ownerRoleKeys);
  const allowed = new Set<string>();
  for (const r of rights) {
    if (!r.isActive) continue;
    const f = r.fields;
    if (!PROPOSING_LETTERS.includes(text(f, 'letter') ?? '')) continue;
    if (text(f, 'changeTypeKey')) continue;
    const tier = text(f, 'tierKey');
    if (tier && tier !== who.tierKey) continue;
    const kind = text(f, 'applicationKindKey');
    if (kind && kind !== who.kindKey) continue;
    const role = text(f, 'roleKey');
    const ownerRole = text(f, 'ownerRoleKey');
    const fallback = text(f, 'fallbackOwnerRoleKey');
    if ((role && roles.has(role)) || (ownerRole && ownerRoles.has(ownerRole)) || (fallback && ownerRoles.has(fallback))) {
      const type = text(f, 'decisionTypeKey');
      if (type) allowed.add(type);
    }
  }
  return types
    .filter(t => t.isActive && (text(t.fields, 'defaultSubjectKind') ?? 'business-application') === 'business-application' && allowed.has(t.key))
    .map(t => t.key);
}

// ---------------------------------------------------------------------------------------------------------------------
// Tier
// ---------------------------------------------------------------------------------------------------------------------

// The outcome of a tier decision that sets this tier, against the tier the latest answers computed (or, with no
// answers, the tier the application holds): confirm the same tier, raise to a higher one, lower to a lower one.
export function tierOutcome(chosen: string, base: string | null, rank: (key: string) => number): 'confirm' | 'raise' | 'lower' | null {
  if (!chosen) return null;
  if (!base) return null;
  const a = rank(chosen);
  const b = rank(base);
  if (a === b) return 'confirm';
  return a > b ? 'raise' : 'lower';
}

// save_decision_proposal with the tier a tier decision sets (tierKey): in the tool's input schema, not typed in
// data/contracts.ts yet.
export type TierProposalInput = Omit<SaveDecisionProposalInput, 'clientRequestId' | 'validateOnly'> & { tierKey?: string; autonomyLevelKey?: string };

export interface TierProposalForm {
  applicationId: string;
  tierKey: string;
  // The tier the latest answers computed, else the applied tier.
  baseTierKey: string | null;
  computedTierKey: string | null;
  outcomeKeys: readonly string[];
  statement: string;
  context: string;
  reason: string;
  rank: (key: string) => number;
}

export function buildTierProposal(form: TierProposalForm): TierProposalInput | Problem {
  if (!form.tierKey) return { problem: 'Choose the tier the decision sets.', field: 'tierKey' };
  const outcome = tierOutcome(form.tierKey, form.baseTierKey, form.rank);
  if (!outcome) return { problem: 'Answer the tier questions first: a tier decision confirms or changes the tier the answers compute.', field: 'tierKey' };
  if (!form.outcomeKeys.includes(outcome)) return { problem: `The tier decision type in force has no "${outcome}" outcome.`, field: 'proposedOutcomeKey' };
  const statement = form.statement.trim();
  const context = form.context.trim();
  const reason = form.reason.trim();
  if (!statement) return { problem: 'Say what is decided.', field: 'statement' };
  if (!context) return { problem: 'Give the context: what the answers say and what you weighed.', field: 'context' };
  if (!reason) return { problem: 'Say why.', field: 'reason' };
  // Confirm leaves tierKey out: the engine confirms the tier the latest answers computed.
  const keepTier = !(outcome === 'confirm' && form.computedTierKey && form.computedTierKey === form.tierKey);
  return {
    decisionTypeKey: 'tier',
    subjectKind: 'business-application',
    subjectId: form.applicationId,
    statement,
    context,
    proposedOutcomeKey: outcome,
    reason,
    ...(keepTier ? { tierKey: form.tierKey } : {}),
  };
}

// The answer to the sensitive-data question, from the latest assessment: the question keyed sensitivity, else the one
// whose words name sensitive data.
export function sensitivityAnswer(answers: TierAssessmentDetail['answers']): TierAssessmentDetail['answers'][number] | null {
  return answers.find(a => a.questionKey === 'sensitivity')
    ?? answers.find(a => /sensitiv/i.test(a.questionKey) || /sensitive data/i.test(a.question))
    ?? null;
}

// The answers of the latest assessment as a form starts from them: question key to answer key.
export function answersOf(detail: TierAssessmentDetail | null): Record<string, string> {
  const out: Record<string, string> = {};
  for (const a of detail?.answers ?? []) out[a.questionKey] = a.answerKey;
  return out;
}

// ---------------------------------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------------------------------

// The dependency types that move data, as the shipped presets key them.
export const DATA_DEPENDENCY_TYPES = ['reads-data-from', 'writes-data-to'] as const;

export const isDataDependency = (typeKey: string | null | undefined): boolean =>
  !!typeKey && (DATA_DEPENDENCY_TYPES as readonly string[]).includes(typeKey);

// The data-use decisions about the application, open ones first.
export function dataUseDecisions(decisions: DecisionLogItem[]): DecisionLogItem[] {
  const list = decisions.filter(d => d.decisionType.key === 'data-use' && d.status.meaning !== 'withdrawn');
  return [...list.filter(d => d.status.meaning === 'proposed'), ...list.filter(d => d.status.meaning !== 'proposed')];
}

// "Yeses: 1 of 2" for a decision that waits on approvals.
export function yesesText(summary: DecisionLogItem['approvalsSummary']): string | null {
  if (!summary.required) return null;
  const parts = [`Yeses: ${summary.approved} of ${summary.required}`];
  if (summary.rejected) parts.push(`${summary.rejected} ${summary.rejected === 1 ? 'no' : 'noes'}`);
  return parts.join(', ');
}

// ---------------------------------------------------------------------------------------------------------------------
// Retirement (P09)
// ---------------------------------------------------------------------------------------------------------------------

export interface RetirementStep {
  // The decision to propose next, or null when one waits or the application is retired.
  next: { decisionTypeKey: string; outcomeKey: string; label: string } | null;
  decided: DecisionLogItem | null;
  waiting: DecisionLogItem | null;
  retired: boolean;
}

// Where the application stands on its way out: a portfolio decision with outcome retire first (P09 step 1), then a
// retirement decision with outcome complete (step 7), which moves it to the retired stage.
export function retirementStep(decisions: DecisionLogItem[], stageMeaning: string | null): RetirementStep {
  const retireDecisions = decisions.filter(d => d.decisionType.key === 'portfolio' && d.outcome?.key === 'retire');
  const completions = decisions.filter(d => d.decisionType.key === 'retirement');
  const retired = stageMeaning === 'retired';
  const waiting = [...completions, ...retireDecisions].find(d => d.status.meaning === 'proposed') ?? null;
  const decided = retireDecisions.find(d => d.status.meaning === 'accepted') ?? null;
  if (retired || waiting) return { next: null, decided, waiting, retired };
  if (decided) return { next: { decisionTypeKey: 'retirement', outcomeKey: 'complete', label: 'Propose retirement complete' }, decided, waiting, retired };
  return { next: { decisionTypeKey: 'portfolio', outcomeKey: 'retire', label: 'Propose retirement' }, decided, waiting, retired };
}

// ---------------------------------------------------------------------------------------------------------------------
// Approvals
// ---------------------------------------------------------------------------------------------------------------------

export interface ApprovalGroup {
  decision: ApprovalItem['decision'];
  subject: ApprovalItem['subject'];
  recordUrl: string;
  items: ApprovalItem[];
}

// The open approval requests grouped by their decision, in the order the list gives them.
export function groupApprovals(items: ApprovalItem[]): ApprovalGroup[] {
  const groups = new Map<string, ApprovalGroup>();
  for (const item of items) {
    const key = item.decision.decisionId.toLowerCase();
    const group = groups.get(key);
    if (group) group.items.push(item);
    else groups.set(key, { decision: item.decision, subject: item.subject, recordUrl: item.recordUrl, items: [item] });
  }
  return [...groups.values()];
}

// ---------------------------------------------------------------------------------------------------------------------
// Dependency map
// ---------------------------------------------------------------------------------------------------------------------

export type MapSide = 'dependants' | 'dependencies';

export interface DependencyEdge {
  fromApplicationId: string;
  toApplicationId: string;
  dependencyTypeKey: string;
  // The application whose owner may change the link (the depending one).
  fromName: string;
  toName: string;
}

// The link a node of the walk hangs from, as save_dependency names it: the depending application, the one depended on
// and the type. On the dependencies side the parent depends on the node; on the dependants side the node depends on its
// parent. One open link per pair and type, so the three keys find it without its ID.
export function edgeOf(side: MapSide, node: ImpactNode, rootId: string, names: Map<string, string>, rootName: string): DependencyEdge {
  const parentId = node.path.length >= 2 ? node.path[node.path.length - 2] : rootId;
  const parentName = names.get(parentId.toLowerCase()) ?? rootName;
  return side === 'dependencies'
    ? { fromApplicationId: parentId, toApplicationId: node.businessApplicationId, dependencyTypeKey: node.dependencyTypeKey ?? '', fromName: parentName, toName: node.name }
    : { fromApplicationId: node.businessApplicationId, toApplicationId: parentId, dependencyTypeKey: node.dependencyTypeKey ?? '', fromName: node.name, toName: parentName };
}

// Who may change or end a link: the owner of the depending application, an architect or the platform admin (the
// engine's check on save_dependency). The page knows whether the person owns the application it shows; for a link that
// starts at any other application only the two roles show the action. The engine still checks.
export function mayEditEdge(fromApplicationId: string, shownApplicationId: string, ownsShown: boolean, wideRole: boolean): boolean {
  return wideRole || (ownsShown && sameId(fromApplicationId, shownApplicationId));
}

// Reviews in the order people work them: open ones by due date, then done ones, latest first.
export function reviewOrder<T extends { status: { meaning: string }; dueOn: string | null; doneOn: string | null }>(items: T[]): T[] {
  const open = items.filter(r => r.status.meaning !== 'done').sort((a, b) => (a.dueOn ?? '9999').localeCompare(b.dueOn ?? '9999'));
  const done = items.filter(r => r.status.meaning === 'done').sort((a, b) => (b.doneOn ?? '').localeCompare(a.doneOn ?? ''));
  return [...open, ...done];
}

// The nodes behind one bar of the map's counts: by tier or by criticality, null for "not set".
export function nodesInBucket(nodes: ImpactNode[], by: 'tier' | 'criticality', key: string | null): ImpactNode[] {
  return nodes.filter(n => (by === 'tier' ? n.tier?.key ?? null : n.criticalityKey) === key);
}

export interface NewDependencyForm {
  rootId: string;
  // depends-on: the application depends on the other one; used-by: the other one depends on the application.
  direction: 'depends-on' | 'used-by';
  otherId: string;
  dependencyTypeKey: string;
  criticalityKey: string;
  notes: string;
}

export interface DependencyInput {
  fromApplicationId: string;
  toApplicationId: string;
  dependencyTypeKey: string;
  criticalityKey?: string;
  notes?: string;
}

export function buildNewDependency(form: NewDependencyForm): DependencyInput | Problem {
  if (!form.otherId) return { problem: 'Choose the other application.', field: form.direction === 'depends-on' ? 'toApplicationId' : 'fromApplicationId' };
  if (sameId(form.otherId, form.rootId)) return { problem: 'An application cannot depend on itself.', field: 'toApplicationId' };
  if (!form.dependencyTypeKey) return { problem: 'Choose the kind of dependency.', field: 'dependencyTypeKey' };
  const notes = form.notes.trim();
  return {
    fromApplicationId: form.direction === 'depends-on' ? form.rootId : form.otherId,
    toApplicationId: form.direction === 'depends-on' ? form.otherId : form.rootId,
    dependencyTypeKey: form.dependencyTypeKey,
    ...(form.criticalityKey ? { criticalityKey: form.criticalityKey } : {}),
    ...(notes ? { notes } : {}),
  };
}
