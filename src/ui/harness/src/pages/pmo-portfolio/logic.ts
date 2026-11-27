// The logic of the PMO and portfolio pages (Demand, Portfolios, Retirement), with no React in it, so the tests can feed
// it rows in the tools' shapes:
//   mayPropose            whether the person's roles hold a decision right that lets them propose a decision type
//   demand*               the demand board's filters, figures and buckets (list_intake_requests, view demand)
//   portfolio*            the portfolio tree, its totals, the review pack and the save_portfolio input
//   retirementSteps       P09 as seven steps, each with its state, from the reads of one application
//   wayOutRows            the applications on their way out, from the decision log and the retired stage
// The engine decides every write; these only choose what the pages show and offer.
import type {
  ApplicationCard,
  ClaimItem,
  DecisionLogItem,
  EvidencePack,
  ImpactNode,
  IntakeSummary,
  IsoDate,
  IsoDateTime,
  ListIntakeRequestsInput,
  IntakeOrderBy,
  PortfolioItem,
  ReviewItem,
  SavePortfolioInput,
  StatusValue,
} from '../../data/contracts';
import { matchesText } from '../../data/textSearch';

export const sameId = (a: string | null | undefined, b: string | null | undefined): boolean =>
  !!a && !!b && a.toLowerCase() === b.toLowerCase();

const day = (value: IsoDate | IsoDateTime | null | undefined): string | null => (value ? value.slice(0, 10) : null);

export interface Problem {
  problem: string;
  field?: string;
}

export const isProblem = (value: unknown): value is Problem =>
  typeof value === 'object' && value !== null && typeof (value as Problem).problem === 'string';

// =====================================================================================================================
// Decision rights: who may propose
// =====================================================================================================================

// A Decision Right row (C31) as get_configuration returns it: its fields name the decision type, the letter and the
// holder, a product role or an owner role on the subject's application.
export interface RightRow {
  isActive: boolean;
  fields: Record<string, unknown>;
}

// The letters that let a holder propose (the engine's ProposalRights.ProposingLetters).
export const PROPOSING_LETTERS = ['responsible', 'accountable', 'consulted'] as const;

// True when a row of the decision type gives one of the person's product roles, or one of their owner roles on the
// application, a letter that proposes. A scoped row (a tier, a change type) counts too: the engine checks the scope.
export function mayPropose(rows: readonly RightRow[], decisionTypeKey: string, roleKeys: readonly string[], ownerRoleKeys: readonly string[]): boolean {
  return rows.some(r => {
    if (!r.isActive || r.fields.decisionTypeKey !== decisionTypeKey) return false;
    if (!(PROPOSING_LETTERS as readonly string[]).includes(String(r.fields.letter))) return false;
    const role = typeof r.fields.roleKey === 'string' ? r.fields.roleKey : null;
    const owner = typeof r.fields.ownerRoleKey === 'string' ? r.fields.ownerRoleKey : null;
    return (role !== null && roleKeys.includes(role)) || (owner !== null && ownerRoleKeys.includes(owner));
  });
}

// The owner roles a person holds on an application, from its current owner assignments.
export function ownerRolesOf(owners: readonly { ownerRoleKey: string; principal: { objectId: string } }[] | undefined, objectId: string | null): string[] {
  if (!owners || !objectId) return [];
  return [...new Set(owners.filter(o => sameId(o.principal.objectId, objectId)).map(o => o.ownerRoleKey))];
}

// =====================================================================================================================
// Demand
// =====================================================================================================================

export const DEMAND_SORTS: { key: IntakeOrderBy; label: string }[] = [
  { key: 'score', label: 'Score, highest first' },
  { key: 'priority', label: 'Priority' },
  { key: 'size-band', label: 'Size band' },
  { key: 'horizon', label: 'Horizon' },
  { key: 'recent', label: 'Most recent' },
  { key: 'title', label: 'Title' },
];

// The demand board's own filters, read from its address (the route carries the portfolio; the rest are page params).
export interface DemandFilter {
  // A portfolio ID, or "none" for requests with no portfolio yet (a request for a new application).
  portfolio?: string;
  sizeBand?: string;
  horizon?: string;
  // An accepted priority outcome key, "proposed" or "none".
  priority?: string;
  scored?: 'yes' | 'no';
  sort: IntakeOrderBy;
  // Wave 4: the words of the search (q=), sent to list_intake_requests text.
  text?: string;
}

export const KEY_OR_NONE = /^[a-z][a-z0-9-]{0,63}$/;
export const SORT_PATTERN = /^(score|priority|size-band|horizon|recent|title)$/;

// The input of the filtered list read. The demand view holds the requests on the table (submitted, in progress and
// completed); the server filters and orders them.
export function demandInput(filter: DemandFilter): ListIntakeRequestsInput {
  return {
    view: 'demand',
    top: 100,
    orderBy: filter.sort,
    ...(filter.sizeBand ? { sizeBandKeys: [filter.sizeBand] } : {}),
    ...(filter.horizon ? { horizonKeys: [filter.horizon] } : {}),
    ...(filter.priority ? { priorityKeys: [filter.priority] } : {}),
    ...(filter.scored ? { scored: filter.scored === 'yes' } : {}),
    ...(filter.text ? { text: filter.text } : {}),
  };
}

// True when the list needs a read of its own: a server filter or an order other than the default.
export function needsOwnRead(filter: DemandFilter): boolean {
  return !!(filter.sizeBand || filter.horizon || filter.priority || filter.scored || filter.text || filter.sort !== 'score');
}

export type PriorityState = 'none' | 'proposed' | 'accepted' | 'other';

// The request's priority as the board reads it: accepted (with its outcome), proposed (waiting) or none.
export function priorityState(item: Pick<IntakeSummary, 'priority'>): PriorityState {
  const p = item.priority;
  if (!p) return 'none';
  if (p.status.meaning === 'accepted') return 'accepted';
  if (p.status.meaning === 'proposed') return 'proposed';
  return 'other';
}

export interface DemandFigures {
  onTable: number;
  toScore: number;
  scored: number;
  waiting: number;
  decided: number;
}

export function demandFigures(items: readonly IntakeSummary[]): DemandFigures {
  return {
    onTable: items.length,
    toScore: items.filter(i => i.score === null).length,
    scored: items.filter(i => i.score !== null).length,
    waiting: items.filter(i => priorityState(i) === 'proposed').length,
    decided: items.filter(i => priorityState(i) === 'accepted').length,
  };
}

export interface Bucket {
  // null for the "not set" bucket.
  key: string | null;
  label: string;
  count: number;
}

// Counts by a key, in the order given (configured order), then any key the order does not name, then "not set" last.
export function bucketsBy<T>(items: readonly T[], keyOf: (item: T) => string | null, order: readonly { key: string; label: string }[], noneLabel: string): Bucket[] {
  const counts = new Map<string | null, number>();
  for (const item of items) {
    const k = keyOf(item);
    counts.set(k, (counts.get(k) ?? 0) + 1);
  }
  const out: Bucket[] = [];
  for (const o of order) {
    const n = counts.get(o.key);
    if (n) out.push({ key: o.key, label: o.label, count: n });
    counts.delete(o.key);
  }
  for (const [k, n] of counts) {
    if (k !== null) out.push({ key: k, label: k, count: n });
  }
  const none = counts.get(null);
  if (none) out.push({ key: null, label: noneLabel, count: none });
  return out;
}

// =====================================================================================================================
// Portfolios
// =====================================================================================================================

export interface TreeRow {
  item: PortfolioItem;
  depth: number;
}

// The portfolios in tree order: each parent before its children, siblings by name. list_portfolios already answers in
// tree order; this keeps the order when a page merges pages or filters.
// The tree rows a text search keeps (wave 4): each portfolio whose name, level or accountable person holds the words,
// with the portfolios above it, so the tree still reads as a tree. matched says which rows hold the words.
export function searchTree(rows: readonly TreeRow[], text: string | undefined): { rows: TreeRow[]; matched: Set<string> } {
  const matched = new Set<string>();
  if (!text) return { rows: [...rows], matched };
  for (const r of rows) {
    if (matchesText(text, r.item.name, r.item.level?.label, r.item.accountablePrincipal?.displayLabel)) matched.add(r.item.portfolioId.toLowerCase());
  }
  const keep = new Set<string>(matched);
  for (const r of rows) {
    if (!matched.has(r.item.portfolioId.toLowerCase())) continue;
    for (const above of r.item.path) keep.add(above.portfolioId.toLowerCase());
  }
  return { rows: rows.filter(r => keep.has(r.item.portfolioId.toLowerCase())), matched };
}

export function portfolioTree(items: readonly PortfolioItem[]): TreeRow[] {
  const ids = new Set(items.map(i => i.portfolioId.toLowerCase()));
  const children = new Map<string | null, PortfolioItem[]>();
  for (const item of items) {
    const parent = item.parentPortfolioId && ids.has(item.parentPortfolioId.toLowerCase()) ? item.parentPortfolioId.toLowerCase() : null;
    const list = children.get(parent) ?? [];
    list.push(item);
    children.set(parent, list);
  }
  const out: TreeRow[] = [];
  const seen = new Set<string>();
  const walk = (parent: string | null, depth: number) => {
    const list = [...(children.get(parent) ?? [])].sort((a, b) => a.name.localeCompare(b.name));
    for (const item of list) {
      const id = item.portfolioId.toLowerCase();
      if (seen.has(id)) continue;
      seen.add(id);
      out.push({ item, depth });
      walk(id, depth + 1);
    }
  };
  walk(null, 0);
  return out;
}

// The portfolio and every portfolio below it.
export function withDescendants(items: readonly PortfolioItem[], portfolioId: string): Set<string> {
  const found = new Set<string>([portfolioId.toLowerCase()]);
  let grew = true;
  while (grew) {
    grew = false;
    for (const item of items) {
      const id = item.portfolioId.toLowerCase();
      if (!found.has(id) && item.parentPortfolioId && found.has(item.parentPortfolioId.toLowerCase())) {
        found.add(id);
        grew = true;
      }
    }
  }
  return found;
}

// The one owner gap count of a portfolio (document 24, B24-17): owner roles with no holder, no deputy, or a holder whose
// account is off, as the Overview and Owners pages show it. A read from before the count falls back to the applications.
export const ownerGapsOf = (counts: PortfolioItem['counts']): number => counts.ownerGaps ?? counts.applicationsWithOwnerGap;

// The figures across every portfolio. A portfolio's counts include its children, so only the top of the tree adds up.
export function portfolioTotals(items: readonly PortfolioItem[]): Required<PortfolioItem['counts']> {
  const ids = new Set(items.map(i => i.portfolioId.toLowerCase()));
  const top = items.filter(i => !i.parentPortfolioId || !ids.has(i.parentPortfolioId.toLowerCase()));
  return top.reduce(
    (sum, i) => ({
      applications: sum.applications + i.counts.applications,
      applicationsWithOwnerGap: sum.applicationsWithOwnerGap + i.counts.applicationsWithOwnerGap,
      ownerGaps: sum.ownerGaps + ownerGapsOf(i.counts),
      reviewsOverdue: sum.reviewsOverdue + i.counts.reviewsOverdue,
      openDecisions: sum.openDecisions + i.counts.openDecisions,
    }),
    { applications: 0, applicationsWithOwnerGap: 0, ownerGaps: 0, reviewsOverdue: 0, openDecisions: 0 },
  );
}

export const pathText = (item: Pick<PortfolioItem, 'name' | 'path'>): string =>
  item.path.length ? item.path.map(p => p.name).join(' / ') : item.name;

// The portfolio an application sits in: the deepest one whose application list holds it. members maps a portfolio ID
// to the applications in it and below it (find_applications portfolioId).
export function portfolioOfApp(appId: string | null | undefined, members: ReadonlyMap<string, ReadonlySet<string>>, items: readonly PortfolioItem[]): PortfolioItem | null {
  if (!appId) return null;
  const id = appId.toLowerCase();
  let best: PortfolioItem | null = null;
  for (const item of items) {
    if (!members.get(item.portfolioId.toLowerCase())?.has(id)) continue;
    if (!best || item.path.length > best.path.length) best = item;
  }
  return best;
}

// The top of the tree an application sits under, for "demand by portfolio".
export function topPortfolioOf(item: PortfolioItem | null, items: readonly PortfolioItem[]): PortfolioItem | null {
  if (!item) return null;
  const topId = item.path[0]?.portfolioId ?? item.portfolioId;
  return items.find(i => sameId(i.portfolioId, topId)) ?? item;
}

// The latest done review of each application.
export function lastDoneByApp(reviews: readonly ReviewItem[]): Map<string, ReviewItem> {
  const out = new Map<string, ReviewItem>();
  for (const r of reviews) {
    if (!r.businessApplicationId || r.status.meaning !== 'done') continue;
    const id = r.businessApplicationId.toLowerCase();
    const had = out.get(id);
    if (!had || (r.doneOn ?? '') > (had.doneOn ?? '')) out.set(id, r);
  }
  return out;
}

// The proposed decisions about each application (the decision log's subject or its application).
export function openDecisionsByApp(decisions: readonly DecisionLogItem[], appIdOf: (d: DecisionLogItem) => string | null): Map<string, DecisionLogItem[]> {
  const out = new Map<string, DecisionLogItem[]>();
  for (const d of decisions) {
    if (d.status.meaning !== 'proposed') continue;
    const id = appIdOf(d)?.toLowerCase();
    if (!id) continue;
    const list = out.get(id) ?? [];
    list.push(d);
    out.set(id, list);
  }
  return out;
}

// ---------- the portfolio form (save_portfolio) ----------

export interface PortfolioForm {
  name: string;
  description: string;
  levelKey: string;
  // '' for the top of the tree.
  parentPortfolioId: string;
  accountable: { objectId: string; displayLabel: string } | null;
}

export type PortfolioInput = Omit<SavePortfolioInput, 'clientRequestId' | 'validateOnly'>;

// The save_portfolio input from the form. A new portfolio sends what is filled; a change sends only what differs from
// the portfolio as it is, so a PMO who edits the name never sends a field only the platform admin may change.
// fullRights: the person may move it and name the accountable person (the platform admin); the engine still decides.
export function buildPortfolioInput(form: PortfolioForm, existing: PortfolioItem | null, fullRights: boolean): PortfolioInput | Problem {
  const name = form.name.trim();
  if (!name) return { problem: 'Give the portfolio a name.', field: 'name' };
  if (name.length > 200) return { problem: 'Keep the name to 200 characters.', field: 'name' };
  const description = form.description.trim();
  if (description.length > 2000) return { problem: 'Keep the description to 2000 characters.', field: 'description' };
  if (!existing) {
    return {
      name,
      ...(description ? { description } : {}),
      ...(form.levelKey ? { levelKey: form.levelKey } : {}),
      ...(fullRights && form.parentPortfolioId ? { parentPortfolioId: form.parentPortfolioId } : {}),
      ...(fullRights && form.accountable ? { accountablePrincipalObjectId: form.accountable.objectId } : {}),
    };
  }
  if (form.parentPortfolioId && sameId(form.parentPortfolioId, existing.portfolioId)) {
    return { problem: 'A portfolio cannot sit inside itself.', field: 'parentPortfolioId' };
  }
  const input: PortfolioInput = { portfolioId: existing.portfolioId };
  if (name !== existing.name) input.name = name;
  if (description) input.description = description;
  if (form.levelKey && form.levelKey !== (existing.level?.key ?? '')) input.levelKey = form.levelKey;
  if (fullRights) {
    const parentNow = existing.parentPortfolioId ?? '';
    if (!sameId(form.parentPortfolioId, parentNow) && form.parentPortfolioId !== parentNow) {
      if (form.parentPortfolioId) input.parentPortfolioId = form.parentPortfolioId;
      else input.clearParent = true;
    }
    const accountableNow = existing.accountablePrincipal?.objectId ?? null;
    if (form.accountable && !sameId(form.accountable.objectId, accountableNow)) input.accountablePrincipalObjectId = form.accountable.objectId;
    if (!form.accountable && accountableNow) input.clearAccountable = true;
  }
  if (Object.keys(input).length === 1) return { problem: 'Change a field to save it.', field: 'name' };
  return input;
}

export function formOfPortfolio(existing: PortfolioItem | null, parentPortfolioId = ''): PortfolioForm {
  return {
    name: existing?.name ?? '',
    description: '',
    levelKey: existing?.level?.key ?? '',
    parentPortfolioId: existing ? existing.parentPortfolioId ?? '' : parentPortfolioId,
    accountable: existing?.accountablePrincipal ? { objectId: existing.accountablePrincipal.objectId, displayLabel: existing.accountablePrincipal.displayLabel } : null,
  };
}

// =====================================================================================================================
// Retirement (P09)
// =====================================================================================================================

// The portfolio outcomes that send an application on its way out.
export const WAY_OUT_OUTCOMES = ['retire', 'replace'] as const;
export const PORTFOLIO_TYPE = 'portfolio';
export const RETIREMENT_TYPE = 'retirement';
export const DATA_USE_TYPE = 'data-use';
export const RETIREMENT_DONE_OUTCOME = 'complete';
export const RETENTION_OUTCOME = 'retain';

const isWayOut = (d: Pick<DecisionLogItem, 'outcome'>) => !!d.outcome && (WAY_OUT_OUTCOMES as readonly string[]).includes(d.outcome.key);
const live = (d: Pick<DecisionLogItem, 'status'>) => d.status.meaning === 'proposed' || d.status.meaning === 'accepted';
const when = (d: Pick<DecisionLogItem, 'decidedOn' | 'performedBy'>) => d.decidedOn ?? d.performedBy?.stampedOn ?? '';

// The newest first: by the date decided, a proposal by its last change.
export function newestFirst<T extends Pick<DecisionLogItem, 'decidedOn' | 'performedBy' | 'decisionId'>>(items: readonly T[]): T[] {
  return [...items].sort((a, b) => (when(b) > when(a) ? 1 : when(b) < when(a) ? -1 : a.decisionId.localeCompare(b.decisionId)));
}

// The portfolio decision that sends the application out: the latest accepted portfolio decision when it says retire or
// replace, else the latest such proposal that waits. An accepted keep or invest after it means the way out is off.
export function wayOutDecision(decisions: readonly DecisionLogItem[]): { decision: DecisionLogItem | null; overruledBy: DecisionLogItem | null } {
  const portfolio = newestFirst(decisions.filter(d => d.decisionType.key === PORTFOLIO_TYPE && live(d) && !d.supersededBy));
  const accepted = portfolio.find(d => d.status.meaning === 'accepted') ?? null;
  const proposedOut = portfolio.find(d => d.status.meaning === 'proposed' && isWayOut(d)) ?? null;
  if (accepted && isWayOut(accepted)) return { decision: accepted, overruledBy: null };
  if (proposedOut) return { decision: proposedOut, overruledBy: null };
  return { decision: null, overruledBy: accepted };
}

// The retirement decision that counts: the latest accepted or proposed one, not superseded.
export function retirementDecision(decisions: readonly DecisionLogItem[]): DecisionLogItem | null {
  const list = newestFirst(decisions.filter(d => d.decisionType.key === RETIREMENT_TYPE && live(d) && !d.supersededBy));
  return list.find(d => d.status.meaning === 'accepted') ?? list[0] ?? null;
}

// The data decision of the retirement: a data use decision made or proposed since the way-out decision.
export function retentionDecision(decisions: readonly DecisionLogItem[], since: IsoDate | null): DecisionLogItem | null {
  const list = newestFirst(decisions.filter(d =>
    d.decisionType.key === DATA_USE_TYPE && live(d) && !d.supersededBy
    && (d.status.meaning === 'proposed' || since === null || (day(d.decidedOn) ?? '') >= since)));
  return list.find(d => d.status.meaning === 'accepted') ?? list[0] ?? null;
}

const REMOVAL_WORDS = /\b(remov|retir|decommission|sunset|switch(?:es|ed|ing)? off|shut(?:s|ting)? down|archiv)/i;
const CLOSED_FAILED = ['rejected', 'withdrawn', 'cancelled'];

export type PackChange = EvidencePack['changes'][number];

// The removal change (P09 step 4). No field marks a change as a removal, so the tracker reads the change requests on
// the application: one whose title says it removes or retires something, or one planned on or after the day the
// way-out decision was made. Rejected, withdrawn and canceled changes are left out. The latest one wins.
export function removalChange(changes: readonly PackChange[], since: IsoDate | null): PackChange | null {
  const candidates = changes.filter(c => {
    if (c.status && CLOSED_FAILED.includes(c.status.meaning)) return false;
    if (REMOVAL_WORDS.test(c.title)) return true;
    if (since === null) return false;
    const start = day(c.windowStart);
    return start === null ? c.status?.meaning !== 'completed' : start >= since;
  });
  return [...candidates].sort((a, b) => (day(b.windowStart) ?? '9999').localeCompare(day(a.windowStart) ?? '9999'))[0] ?? null;
}

export type PackLink = EvidencePack['evidenceLinks'][number];
export type CardComponent = NonNullable<ApplicationCard['components']>[number];

// The vanished claim the gap check left for a component: the same link, or the same name and kind. list_claims does not
// return the component a vanished claim is about, so the link is matched.
export function vanishedClaimFor(component: CardComponent, claims: readonly ClaimItem[]): ClaimItem | null {
  const open = claims.filter(c => c.findingKind === 'vanished' && c.status.meaning === 'open');
  return open.find(c => !!c.url && !!component.url && c.url === component.url)
    ?? open.find(c => !!component.labelAsSeen && c.labelAsSeen === component.labelAsSeen && (!c.componentKind || c.componentKind.key === component.kind.key))
    ?? null;
}

export type StepState = 'done' | 'waiting' | 'to-do' | 'attention' | 'not-yet';

export const STEP_STATE_TEXT: Record<StepState, string> = {
  done: 'Done',
  waiting: 'Waiting',
  'to-do': 'To do',
  attention: 'Needs attention',
  'not-yet': 'Not started',
};

export type StepKey = 'decide' | 'impact' | 'data' | 'change' | 'evidence' | 'gap-check' | 'complete';

export interface RetirementStep {
  key: StepKey;
  number: number;
  title: string;
  state: StepState;
  // What the step holds now, in one sentence.
  summary: string;
}

export interface RetirementFacts {
  decisions: readonly DecisionLogItem[];
  // The open retention gaps (DC-13) of the application's data: a domain it writes with no accepted retention decision by
  // that domain's data owner. Step 3 is done only when there is none (product review, fix round 1). Undefined when not read.
  retentionGaps?: readonly { message: string; domain: { path?: string; label?: string } | null; principal: { displayLabel: string } | null }[] | null;
  // The live applications that depend on this one (get_dependency_impact, dependants). null while unread.
  dependants: readonly ImpactNode[] | null;
  // True when the dependency walk did not answer.
  dependantsFailed?: boolean;
  changes: readonly PackChange[];
  evidenceLinks: readonly PackLink[];
  // The components still linked (get_application_card components leaves out the retired ones). null while unread.
  components: readonly CardComponent[] | null;
  // True when the application's stage means retired (C12 engine meaning).
  retiredStage: boolean;
}

export interface RetirementPicture {
  wayOut: DecisionLogItem | null;
  overruledBy: DecisionLogItem | null;
  retention: DecisionLogItem | null;
  removal: PackChange | null;
  removalEvidence: PackLink[];
  retirement: DecisionLogItem | null;
  steps: RetirementStep[];
  // The steps still open (not done).
  left: RetirementStep[];
  overall: 'not-out' | 'proposed' | 'on-the-way' | 'retired';
}

function decisionStep(d: DecisionLogItem | null, doneWhen: (d: DecisionLogItem) => boolean): StepState {
  if (!d) return 'to-do';
  if (d.status.meaning === 'accepted') return doneWhen(d) ? 'done' : 'attention';
  return 'waiting';
}

const decidedWords = (d: DecisionLogItem) => `${d.outcome?.label ?? 'No outcome'}, ${d.status.label.toLowerCase()}`;

export function retirementSteps(f: RetirementFacts): RetirementPicture {
  const { decision: wayOut, overruledBy } = wayOutDecision(f.decisions);
  const since = wayOut?.status.meaning === 'accepted' ? day(wayOut.decidedOn) : null;
  const decided = wayOut?.status.meaning === 'accepted';
  const retirement = retirementDecision(f.decisions);
  const retention = retentionDecision(f.decisions, since);
  const removal = removalChange(f.changes, since);
  const subjects = new Set([removal?.changeRequestId, retirement?.decisionId, retention?.decisionId, wayOut?.decisionId].filter((x): x is string => !!x).map(x => x.toLowerCase()));
  const removalEvidence = f.evidenceLinks.filter(l => subjects.has(l.subjectId.toLowerCase()));
  const retiredNow = (retirement?.status.meaning === 'accepted' && retirement.outcome?.key === RETIREMENT_DONE_OUTCOME) || f.retiredStage;

  const steps: RetirementStep[] = [];
  const later = (state: StepState): StepState => ((state === 'to-do' || state === 'attention') && !decided && !retiredNow ? 'not-yet' : state);

  // 1. Decide.
  steps.push({
    key: 'decide',
    number: 1,
    title: 'Portfolio decision: retire',
    state: wayOut ? decisionStep(wayOut, isWayOut) : retiredNow ? 'attention' : 'to-do',
    summary: wayOut
      ? `${wayOut.decisionType.label} decision: ${decidedWords(wayOut)}.`
      : overruledBy
        ? `The latest portfolio decision says ${overruledBy.outcome?.label.toLowerCase() ?? 'something else'}. No decision sends it out.`
        : retiredNow ? 'Retired with no portfolio decision on record.' : 'No portfolio decision says retire or replace yet.',
  });

  // 2. Dependants told.
  const dependants = f.dependants;
  steps.push({
    key: 'impact',
    number: 2,
    title: 'Dependants told',
    state: f.dependantsFailed ? 'attention' : dependants === null ? 'waiting' : dependants.length === 0 ? 'done' : later('attention'),
    summary: f.dependantsFailed
      ? 'The dependency walk did not answer, so the dependants are not known. Check the dependency map.'
      : dependants === null
      ? 'Reading the dependency walk.'
      : dependants.length === 0
        ? 'No live application depends on it.'
        : `${dependants.length} live ${dependants.length === 1 ? 'application depends' : 'applications depend'} on it. Tell their owners, and plan a replacement where one is needed.`,
  });

  // 3. Data retention: the DC-13 rule (product review, fix round 1). Done only when every domain the application writes has an
  // accepted retention decision by that domain's data owner; an application-level decision alone is history.
  const retentionGaps = f.retentionGaps ?? [];
  steps.push({
    key: 'data',
    number: 3,
    title: 'Data retention decision',
    state: retentionGaps.length ? later('to-do') : retention ? decisionStep(retention, () => true) : later('to-do'),
    summary: retentionGaps.length
      // Two uses of one domain at two places give the same DC-13 sentence: say it once (wave 4b QA walk).
      ? `Waiting: ${[...new Set(retentionGaps.map(g => g.message))].join(' ')}`
      : retention
        ? `Data use decision: ${decidedWords(retention)}.`
        : 'The data owner has not decided what happens to the data.',
  });

  // 4. Removal change.
  const changeState: StepState = !removal ? later('to-do')
    : removal.status?.meaning === 'completed' ? 'done'
      : removal.status?.meaning === 'draft' ? 'to-do'
        : 'waiting';
  steps.push({
    key: 'change',
    number: 4,
    title: 'Removal change',
    state: changeState,
    summary: removal
      ? `${removal.recordNumber ? `${removal.recordNumber}: ` : ''}${removal.title}, ${removal.status?.label.toLowerCase() ?? 'no status'}.`
      : 'No change request plans the removal yet.',
  });

  // 5. Removal evidence.
  steps.push({
    key: 'evidence',
    number: 5,
    title: 'Removal evidence',
    state: removalEvidence.length ? 'done' : later('to-do'),
    summary: removalEvidence.length
      ? `${removalEvidence.length} evidence ${removalEvidence.length === 1 ? 'link' : 'links'} on the removal and its decisions.`
      : removal ? 'No evidence is linked to the removal change yet.'
        : retirement ? 'No removal change yet, so evidence goes on the retirement decision.' : 'Evidence goes on the removal change once it exists.',
  });

  // 6. Gap check confirmation.
  const components = f.components;
  steps.push({
    key: 'gap-check',
    number: 6,
    title: 'Gap check confirmation',
    // Nothing linked counts as done only once the application is on its way out (QA walk): before that, the step waits.
    state: components === null ? 'waiting' : components.length === 0 ? (decided || retiredNow ? 'done' : 'not-yet') : later('to-do'),
    summary: components === null
      ? 'Reading the components.'
      : components.length === 0
        ? 'No component is still linked: each one is resolved as retired.'
        : `${components.length} ${components.length === 1 ? 'component is' : 'components are'} still linked. Each is confirmed retired once the gap check no longer sees it.`,
  });

  // 7. Complete.
  steps.push({
    key: 'complete',
    number: 7,
    title: 'Retirement complete',
    state: retirement ? decisionStep(retirement, d => d.outcome?.key === RETIREMENT_DONE_OUTCOME) : f.retiredStage ? 'done' : later('to-do'),
    summary: retirement
      ? `Retirement decision: ${decidedWords(retirement)}.`
      : f.retiredStage ? 'The application is in the retired stage.' : 'No retirement decision yet.',
  });

  const overall: RetirementPicture['overall'] = retiredNow ? 'retired' : decided ? 'on-the-way' : wayOut ? 'proposed' : 'not-out';
  return { wayOut, overruledBy, retention, removal, removalEvidence, retirement, steps, left: steps.filter(s => s.state !== 'done'), overall };
}

// ---------- the list: applications on their way out ----------

export type WayOutStage = 'proposed' | 'decided' | 'completing' | 'retired';

export const WAY_OUT_STAGES: { key: WayOutStage; label: string; caption: string }[] = [
  { key: 'proposed', label: 'Retire proposed', caption: 'A portfolio decision to retire or replace waits' },
  { key: 'decided', label: 'On the way out', caption: 'Decided, retirement not complete' },
  { key: 'completing', label: 'Completion proposed', caption: 'Retirement complete waits for the business owner' },
  { key: 'retired', label: 'Retired', caption: 'Retirement complete, or in the retired stage' },
];

export interface WayOutRow {
  businessApplicationId: string;
  name: string;
  stage: WayOutStage;
  wayOut: DecisionLogItem | null;
  retirement: DecisionLogItem | null;
  // The date the row last moved, for the order: newest first.
  movedOn: string;
}

// The applications on their way out: every application a portfolio decision sends out (retire or replace, proposed or
// accepted), every one with a retirement decision, and every one in a stage that means retired.
export function wayOutRows(decisions: readonly DecisionLogItem[], retiredApps: readonly { businessApplicationId: string; name: string }[]): WayOutRow[] {
  const byApp = new Map<string, DecisionLogItem[]>();
  for (const d of decisions) {
    if (d.subjectKind !== 'business-application') continue;
    const id = d.subjectId.toLowerCase();
    const list = byApp.get(id) ?? [];
    list.push(d);
    byApp.set(id, list);
  }
  const retired = new Map(retiredApps.map(a => [a.businessApplicationId.toLowerCase(), a.name]));
  const ids = new Set([...byApp.keys(), ...retired.keys()]);
  const rows: WayOutRow[] = [];
  for (const id of ids) {
    const list = byApp.get(id) ?? [];
    const { decision: wayOut } = wayOutDecision(list);
    const retirement = retirementDecision(list);
    const inRetiredStage = retired.has(id);
    const complete = retirement?.status.meaning === 'accepted' && retirement.outcome?.key === RETIREMENT_DONE_OUTCOME;
    let stage: WayOutStage | null = null;
    if (complete || inRetiredStage) stage = 'retired';
    else if (retirement?.status.meaning === 'proposed' && retirement.outcome?.key === RETIREMENT_DONE_OUTCOME) stage = 'completing';
    else if (wayOut?.status.meaning === 'accepted') stage = 'decided';
    else if (wayOut?.status.meaning === 'proposed') stage = 'proposed';
    if (!stage) continue;
    const name = retired.get(id) ?? (wayOut ?? retirement ?? list[0])?.subjectLabel ?? 'Application';
    const dates = [wayOut, retirement].filter((d): d is DecisionLogItem => !!d).map(when);
    rows.push({ businessApplicationId: id, name, stage, wayOut, retirement, movedOn: dates.sort().pop() ?? '' });
  }
  return rows.sort((a, b) => (b.movedOn > a.movedOn ? 1 : b.movedOn < a.movedOn ? -1 : a.name.localeCompare(b.name)));
}

// The tone a step state shows with. Never color alone: the badge carries the words.
export const STEP_TONE: Record<StepState, 'success' | 'warning' | 'brand' | 'danger' | 'subtle'> = {
  done: 'success',
  waiting: 'brand',
  'to-do': 'warning',
  attention: 'danger',
  'not-yet': 'subtle',
};

// A status in the words a list row uses: "Proposed" or "Accepted 3 Oct 2026" is left to the page; this keeps the label.
export const statusLabel = (s: StatusValue | null | undefined): string => s?.label ?? 'No status';
