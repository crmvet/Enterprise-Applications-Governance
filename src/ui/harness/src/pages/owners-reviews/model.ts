// The Owners and Reviews pages' logic with no React in it: grouping the owner assignments by person, the leavers and
// their facts, the review counts, the checks a page runs before it shows an action, and the tool inputs the side sheets
// send. The tests in model.test.ts run it in Node.
//
// The checks that decide whether an action shows are the page's best reading of the Tool Policy and the engine's
// guards. They only hide what the engine would refuse anyway; the engine still checks every write.
import type { CompleteReviewInput, DecisionLogItem, DirectoryState, EvidenceInput, IsoDate, OwnerAssignmentItem, PrincipalRef, ProposeOwnerChangeInput, RecordOwnerLeaveInput, ReviewItem, SaveDecisionProposalInput, SaveLinkInput, SubmitRecordInput } from '../../data/contracts';
import { daysBetween, externalHref } from '../../data/format';

// ---------- who the signed-in person is (whoami) ----------

// The whoami tool's data (src/mcp/contracts/tools/whoami.output.schema.json). Not in data/contracts.ts yet, so typed
// here; the page reads only these fields.
export interface WhoAmI {
  principal: { objectId: string; displayLabel: string; principalKind: string };
  productRoles: { roleKey: string; label: string }[];
  actorKind: 'person' | 'ai-client-for-person';
  tools: string[];
}

export const sameId = (a: string | null | undefined, b: string | null | undefined): boolean =>
  !!a && !!b && a.toLowerCase() === b.toLowerCase();

// True when the person may call the tool here: the web app's own entry stamps the person (D-36), and the tool is one
// the Tool Policy gives their roles.
export function mayCall(me: WhoAmI | null, tool: string): boolean {
  return !!me && me.actorKind === 'person' && me.tools.includes(tool);
}

export function holdsRole(me: WhoAmI | null, ...roleKeys: string[]): boolean {
  return !!me && me.productRoles.some(r => roleKeys.includes(r.roleKey));
}

// The applications the person holds a current owner assignment on (holder or deputy), from their own assignments.
export function ownedApplicationIds(mine: OwnerAssignmentItem[]): Set<string> {
  return new Set(mine.filter(a => a.state === 'current').map(a => a.businessApplicationId.toLowerCase()));
}

// An owner role names where it can be held (C28 subjectKinds, data taxonomy D-37). One held only on data domains or data
// locations, such as the data steward, is not an owner role of an application, so the owner pickers leave it out; the
// tool refuses it too. No list means an application only, as every owner role was before the data taxonomy.
export function heldOnApplications(subjectKinds: unknown): boolean {
  return !Array.isArray(subjectKinds) || subjectKinds.length === 0 || subjectKinds.includes('business-application');
}

// propose_owner_change: Tool Policy roles application-owner, ai-lead, platform-admin. A caller without ai-lead or
// platform-admin must also hold an owner role on the application (IntakeTools.RequireOwnerRelation).
export function canProposeOwnerChange(me: WhoAmI | null, applicationId: string, mine: Set<string>): boolean {
  if (!mayCall(me, 'propose_owner_change')) return false;
  return holdsRole(me, 'ai-lead', 'platform-admin') || mine.has(applicationId.toLowerCase());
}

// record_owner_leave: the leaving person themselves, an owner of an application the leaving person owns, or a platform
// admin (IntakeTools.RecordOwnerLeave).
export function canRecordLeave(me: WhoAmI | null, personObjectId: string, personApplicationIds: Iterable<string>, mine: Set<string>): boolean {
  if (!mayCall(me, 'record_owner_leave') || !me) return false;
  if (sameId(me.principal.objectId, personObjectId) || holdsRole(me, 'platform-admin')) return true;
  for (const id of personApplicationIds) if (mine.has(id.toLowerCase())) return true;
  return false;
}

// ---------- owner assignments by person ----------

export type LeaveReason = 'leaves' | 'disabled' | 'deleted';

// Why a holder counts as a leaver: a recorded leave date, or the last owner check found the account disabled or
// deleted in Entra (list_owner_assignments leaversOnly).
export function leaveReason(holder: { directoryState: DirectoryState | null; leavesOn: IsoDate | null }): LeaveReason | null {
  if (holder.directoryState === 'deleted') return 'deleted';
  if (holder.directoryState === 'disabled') return 'disabled';
  return holder.leavesOn ? 'leaves' : null;
}

export interface PersonGroup {
  principal: PrincipalRef;
  // Every assignment of the person in the rows given.
  assignments: OwnerAssignmentItem[];
  // Owner roles held (not deputy), deputy roles, and the distinct applications.
  holds: number;
  deputyFor: number;
  applications: number;
  // The owner role keys the person holds or deputizes, in first-seen order.
  roleKeys: string[];
  directoryState: DirectoryState | null;
  leavesOn: IsoDate | null;
  leaveReason: LeaveReason | null;
  // The pending approvals that ask the owner roles the person holds (would move if they leave).
  pendingApprovals: number;
}

export function groupByPerson(items: OwnerAssignmentItem[]): PersonGroup[] {
  const groups = new Map<string, PersonGroup & { apps: Set<string> }>();
  for (const a of items) {
    const id = a.principal.objectId.toLowerCase();
    let g = groups.get(id);
    if (!g) {
      g = {
        principal: a.principal,
        assignments: [],
        holds: 0,
        deputyFor: 0,
        applications: 0,
        roleKeys: [],
        directoryState: a.holder.directoryState,
        leavesOn: a.holder.leavesOn,
        leaveReason: leaveReason(a.holder),
        pendingApprovals: 0,
        apps: new Set<string>(),
      };
      groups.set(id, g);
    }
    g.assignments.push(a);
    if (a.isDeputy) g.deputyFor++;
    else {
      g.holds++;
      g.pendingApprovals += a.pendingApprovalCount;
    }
    g.apps.add(a.businessApplicationId.toLowerCase());
    if (!g.roleKeys.includes(a.ownerRoleKey)) g.roleKeys.push(a.ownerRoleKey);
  }
  return [...groups.values()]
    .map(({ apps, ...g }) => ({ ...g, applications: apps.size }))
    .sort((a, b) => a.principal.displayLabel.localeCompare(b.principal.displayLabel) || a.principal.objectId.localeCompare(b.principal.objectId));
}

export type PersonSort = 'name' | 'applications' | 'leaving';

export function sortPeople(people: PersonGroup[], sort: PersonSort): PersonGroup[] {
  const byName = (a: PersonGroup, b: PersonGroup) => a.principal.displayLabel.localeCompare(b.principal.displayLabel);
  const list = [...people];
  if (sort === 'applications') return list.sort((a, b) => b.applications - a.applications || b.holds - a.holds || byName(a, b));
  if (sort === 'leaving') return list.sort((a, b) => Number(!!b.leaveReason) - Number(!!a.leaveReason) || (a.leavesOn ?? '~').localeCompare(b.leavesOn ?? '~') || byName(a, b));
  return list.sort(byName);
}

// People whose name matches every word of the search.
export function matchPeople(people: PersonGroup[], text: string): PersonGroup[] {
  const words = text.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return people;
  return people.filter(p => {
    const name = p.principal.displayLabel.toLowerCase();
    return words.every(w => name.includes(w));
  });
}

// The current deputy of an owner role on an application, other than the person given: the natural successor.
export function deputyOf(items: OwnerAssignmentItem[], applicationId: string, ownerRoleKey: string, notObjectId?: string): OwnerAssignmentItem | null {
  return items.find(a =>
    a.isDeputy && a.state === 'current' && sameId(a.businessApplicationId, applicationId) && a.ownerRoleKey === ownerRoleKey
    && !sameId(a.principal.objectId, notObjectId)) ?? null;
}

// The successor an accepted owner change already named: an upcoming assignment of the same owner role (deputy or not, as
// the leaver's) on the same application, held by someone else, that starts no earlier than the leaver's row ends. The
// earliest one, or null (wave 3 verification: Leavers still offered "Propose a successor" after the owner change was
// accepted).
export function namedSuccessorOf(upcoming: OwnerAssignmentItem[], leaver: OwnerAssignmentItem): OwnerAssignmentItem | null {
  return upcoming
    .filter(a => a.state === 'upcoming' && sameId(a.businessApplicationId, leaver.businessApplicationId) && a.ownerRoleKey === leaver.ownerRoleKey
      && a.isDeputy === leaver.isDeputy && !sameId(a.principal.objectId, leaver.principal.objectId)
      && (!leaver.endOn || !a.startOn || a.startOn >= leaver.endOn))
    .sort((a, b) => (a.startOn ?? '').localeCompare(b.startOn ?? ''))[0] ?? null;
}

// The people a picker offers: the holders and deputies seen in the rows, and the person themselves, in name order.
// A successor picker leaves out anyone whose account is disabled or deleted; the leave picker keeps them (includeAway),
// since they are the people whose leave needs recording.
export interface PersonOption {
  objectId: string;
  displayLabel: string;
  note: string | null;
}

export function personOptions(items: OwnerAssignmentItem[], me: WhoAmI | null, options: { includeAway?: boolean } = {}): PersonOption[] {
  const seen = new Map<string, PersonOption>();
  if (me) seen.set(me.principal.objectId.toLowerCase(), { objectId: me.principal.objectId, displayLabel: me.principal.displayLabel || me.principal.objectId, note: 'you' });
  for (const a of items) {
    const id = a.principal.objectId.toLowerCase();
    if (seen.has(id)) continue;
    const reason = leaveReason(a.holder);
    if ((reason === 'disabled' || reason === 'deleted') && !options.includeAway) continue;
    seen.set(id, {
      objectId: a.principal.objectId,
      displayLabel: a.principal.displayLabel || a.principal.objectId,
      note: a.principal.principalKind === 'group' ? 'group' : reason === 'leaves' ? 'leaving' : reason ? `account ${reason}` : null,
    });
  }
  return [...seen.values()].sort((a, b) => a.displayLabel.localeCompare(b.displayLabel));
}

// The proposed ownership decisions per application (get_decision_log with decisionTypeKeys ownership): the successor
// proposals in flight.
export function proposalsByApplication(items: DecisionLogItem[]): Map<string, DecisionLogItem[]> {
  const map = new Map<string, DecisionLogItem[]>();
  for (const d of items) {
    if (d.subjectKind !== 'business-application') continue;
    const id = d.subjectId.toLowerCase();
    map.set(id, [...(map.get(id) ?? []), d]);
  }
  return map;
}

// "1 of 2 approvals", "Not submitted yet", or "Waiting for its approvals".
export function approvalsText(d: DecisionLogItem): string {
  const s = d.approvalsSummary;
  const asked = s.approved + s.rejected + s.pending;
  if (!asked && !s.required) return d.status.meaning === 'proposed' ? 'Not submitted yet' : 'No approval request on record';
  if (s.rejected) return `${s.rejected} rejected, ${s.approved} approved`;
  return s.required ? `${s.approved} of ${s.required} approvals` : `${s.approved} approved, ${s.pending} waiting`;
}

// ---------- reviews ----------

// "an ownership decision", "a tier decision": the article for a configured label, by its first letter.
export function withArticle(words: string, capital = false): string {
  const article = /^[aeiou]/i.test(words.trim()) ? 'an' : 'a';
  return `${capital ? article.charAt(0).toUpperCase() + article.slice(1) : article} ${words}`;
}

export function overdueDays(dueOn: IsoDate | null, today: IsoDate): number {
  return dueOn ? Math.max(0, daysBetween(dueOn, today)) : 0;
}

export function triggerText(trigger: ReviewItem['trigger']): string {
  switch (trigger) {
    case 'cadence':
      return 'Opened by its cadence';
    case 'tier-answer-trigger':
      return 'Opened by a tier answer';
    case 'owner-check':
      return 'Opened by an owner check or a leave';
    case 'request':
      return 'Asked for by a person';
    default:
      return 'Not recorded';
  }
}

export interface Count {
  key: string | null;
  label: string;
  count: number;
}

// Counts rows by a labelled key, largest first, then by label. A row with no key counts under the label given.
export function countBy<T>(items: T[], pick: (item: T) => { key: string; label: string } | null, noneLabel: string): Count[] {
  const map = new Map<string, Count>();
  for (const item of items) {
    const k = pick(item);
    const id = k?.key ?? '';
    const c = map.get(id) ?? { key: k?.key ?? null, label: k?.label ?? noneLabel, count: 0 };
    c.count++;
    map.set(id, c);
  }
  return [...map.values()].sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
}

export type ReviewSort = 'due' | 'application' | 'type';

export function sortReviews(items: ReviewItem[], sort: ReviewSort): ReviewItem[] {
  const app = (r: ReviewItem) => r.applicationName ?? '';
  const due = (r: ReviewItem) => r.dueOn ?? r.doneOn ?? '~';
  const list = [...items];
  if (sort === 'application') return list.sort((a, b) => app(a).localeCompare(app(b)) || due(a).localeCompare(due(b)));
  if (sort === 'type') return list.sort((a, b) => a.reviewType.label.localeCompare(b.reviewType.label) || due(a).localeCompare(due(b)));
  return items;
}

// The last done review of the same type on the same application, before this one: its outcome is the last outcome.
export function previousDone(items: ReviewItem[], review: ReviewItem): ReviewItem | null {
  return items
    .filter(r => r.reviewId !== review.reviewId && r.reviewType.key === review.reviewType.key && r.status.meaning === 'done')
    .sort((a, b) => (b.doneOn ?? '').localeCompare(a.doneOn ?? ''))[0] ?? null;
}

// True once the decision went for approval: an approval record exists for it.
export function isSubmitted(d: DecisionLogItem): boolean {
  const s = d.approvalsSummary;
  return s.required + s.approved + s.rejected + s.pending > 0;
}



// ---------- the side sheets' inputs ----------

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

export const isGuid = (value: string): boolean => GUID.test(value.trim());

export function isIsoDate(value: string): boolean {
  if (!DATE.test(value)) return false;
  const d = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === value;
}

export interface Problem {
  problem: string;
  field?: string;
}

type Built<T> = { input: T } | Problem;

export const isProblem = <T,>(built: Built<T>): built is Problem => 'problem' in built;

// add: a new holder or deputy. replace: end an assignment and add the successor in one change. end: end an assignment
// with no successor.
export type OwnerChangeMode = 'add' | 'replace' | 'end';

export interface OwnerChangeForm {
  applicationId: string;
  mode: OwnerChangeMode;
  ownerRoleKey: string;
  addObjectId: string;
  isDeputy: boolean;
  startOn: string;
  endAssignmentId: string | null;
  // The person whose assignment ends, so the successor cannot be the same person.
  endingObjectId: string | null;
  reason: string;
}

export function buildOwnerChange(form: OwnerChangeForm): Built<Omit<ProposeOwnerChangeInput, 'clientRequestId' | 'validateOnly'>> {
  if (!form.ownerRoleKey) return { problem: 'Choose the owner role.', field: 'changes.ownerRoleKey' };
  const adds = form.mode !== 'end';
  const add = form.addObjectId.trim();
  if (adds && !add) return { problem: 'Choose the person, or enter their Entra object ID.', field: 'changes.addObjectId' };
  if (adds && !isGuid(add)) return { problem: 'An Entra object ID looks like 00000000-0000-0000-0000-000000000000.', field: 'changes.addObjectId' };
  if (form.mode !== 'add' && !form.endAssignmentId) return { problem: 'Choose the assignment that ends.', field: 'changes.endAssignmentId' };
  if (form.mode === 'replace' && sameId(add, form.endingObjectId)) return { problem: 'The successor is the person who already holds the role. Choose someone else.', field: 'changes.addObjectId' };
  const start = form.startOn.trim();
  if (adds && start && !isIsoDate(start)) return { problem: 'The start date is not a date.', field: 'changes.startOn' };
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say why the owners change. The approvers read it.', field: 'reason' };
  if (reason.length > 2000) return { problem: 'The reason is longer than 2000 characters.', field: 'reason' };
  const change: ProposeOwnerChangeInput['changes'][number] = { ownerRoleKey: form.ownerRoleKey };
  if (adds) {
    change.addObjectId = add.toLowerCase();
    if (form.isDeputy) change.isDeputy = true;
    if (start) change.startOn = start;
  }
  if (form.mode !== 'add' && form.endAssignmentId) change.endAssignmentId = form.endAssignmentId;
  return { input: { businessApplicationId: form.applicationId, changes: [change], reason } };
}

export interface LeaveForm {
  principalObjectId: string;
  leavesOn: string;
  kind: 'leave' | 'role-change';
  note: string;
}

export function buildLeave(form: LeaveForm): Built<Omit<RecordOwnerLeaveInput, 'clientRequestId' | 'validateOnly'>> {
  if (!isGuid(form.principalObjectId)) return { problem: 'Choose the person who leaves.', field: 'principalObjectId' };
  if (!isIsoDate(form.leavesOn)) return { problem: 'Enter the leave date.', field: 'leavesOn' };
  const note = form.note.trim();
  if (note.length > 1000) return { problem: 'The note is longer than 1000 characters.', field: 'note' };
  return {
    input: {
      principalObjectId: form.principalObjectId.toLowerCase(),
      leavesOn: form.leavesOn,
      kind: form.kind,
      ...(note ? { note } : {}),
    },
  };
}

export function buildSubmit(decisionId: string, note: string): Omit<SubmitRecordInput, 'clientRequestId' | 'validateOnly'> {
  const text = note.trim();
  return { subjectKind: 'decision', subjectId: decisionId, ...(text ? { note: text.slice(0, 1000) } : {}) };
}

export interface OutcomeForm {
  reviewId: string;
  decisionTypeKey: string;
  outcomeKey: string;
  statement: string;
  context: string;
  reason: string;
  confidenceKey: string;
  // Required where the decision type needs the options weighed (needsOptionsConsidered, such as Design).
  needsOptions: boolean;
  options: { option: string; whyNot: string }[];
  evidenceUrl: string;
  evidenceLabel: string;
}

export function buildOutcome(form: OutcomeForm): Built<Omit<SaveDecisionProposalInput, 'clientRequestId' | 'validateOnly'>> {
  if (!form.decisionTypeKey) return { problem: 'This review type names no decision type for its outcome, so no outcome can be proposed.', field: 'decisionTypeKey' };
  if (!form.outcomeKey) return { problem: 'Choose the outcome.', field: 'proposedOutcomeKey' };
  const statement = form.statement.trim();
  if (!statement) return { problem: 'Write the decision in one sentence.', field: 'statement' };
  if (statement.length > 500) return { problem: 'The statement is longer than 500 characters.', field: 'statement' };
  const context = form.context.trim();
  if (!context) return { problem: 'Say what the review looked at and found.', field: 'context' };
  if (context.length > 4000) return { problem: 'The context is longer than 4000 characters.', field: 'context' };
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say why this outcome.', field: 'reason' };
  if (reason.length > 2000) return { problem: 'The reason is longer than 2000 characters.', field: 'reason' };
  const options = form.options
    .map(o => ({ option: o.option.trim(), whyNot: o.whyNot.trim() }))
    .filter(o => o.option || o.whyNot);
  if (options.some(o => !o.option || !o.whyNot)) return { problem: 'Each option weighed needs the option and why it was not chosen.', field: 'optionsConsidered' };
  if (form.needsOptions && !options.length) return { problem: 'This decision type needs the options weighed. Add at least one.', field: 'optionsConsidered' };
  const url = form.evidenceUrl.trim();
  const label = form.evidenceLabel.trim();
  let evidence: EvidenceInput[] | undefined;
  if (url || label) {
    if (!externalHref(url)) return { problem: 'The evidence link must be a full https address.', field: 'evidence' };
    if (!label) return { problem: 'Give the evidence link a short label.', field: 'evidence' };
    evidence = [{ linkKindKey: 'evidence-url', url, label: label.slice(0, 200) }];
  }
  return {
    input: {
      decisionTypeKey: form.decisionTypeKey,
      subjectKind: 'review',
      subjectId: form.reviewId,
      statement,
      context,
      proposedOutcomeKey: form.outcomeKey,
      reason,
      ...(form.confidenceKey ? { confidenceKey: form.confidenceKey } : {}),
      ...(options.length ? { optionsConsidered: options } : {}),
      ...(evidence ? { evidence } : {}),
    },
  };
}

export interface EvidenceForm {
  reviewId: string;
  linkKindKey: string;
  url: string;
  label: string;
  evidenceLevelKey: string;
}

// The complete_review input from its sheet: the review, an optional date (never later than today), and an optional
// evidence link with its label. The engine checks the outcome decision; the page shows the sheet only when the read
// says the person may complete it now.
export interface CompletionForm {
  reviewId: string;
  doneOn: string;
  today: string;
  evidenceUrl: string;
  evidenceLabel: string;
}

export function buildCompletion(form: CompletionForm): Built<Omit<CompleteReviewInput, 'clientRequestId' | 'validateOnly'>> {
  const doneOn = form.doneOn.trim();
  if (doneOn && !isIsoDate(doneOn)) return { problem: 'Enter the date the review was done as a date.', field: 'doneOn' };
  if (doneOn && doneOn > form.today) return { problem: 'The review cannot be done later than today.', field: 'doneOn' };
  const url = form.evidenceUrl.trim();
  const label = form.evidenceLabel.trim();
  if (url && !externalHref(url)) return { problem: 'The evidence link must be a full https address.', field: 'evidence' };
  if (url && !label) return { problem: 'Give the evidence link a short label.', field: 'evidence' };
  if (!url && label) return { problem: 'Add the evidence link, or clear its label.', field: 'evidence' };
  return {
    input: {
      reviewId: form.reviewId,
      ...(doneOn ? { doneOn } : {}),
      ...(url ? { evidence: [{ linkKindKey: 'evidence-url', url, label }] } : {}),
    },
  };
}

export function buildEvidence(form: EvidenceForm): Built<Omit<SaveLinkInput, 'clientRequestId' | 'validateOnly'>> {
  if (!form.linkKindKey) return { problem: 'Choose the kind of link.', field: 'linkKindKey' };
  const url = form.url.trim();
  if (!externalHref(url)) return { problem: 'Enter the full https address of the evidence.', field: 'url' };
  const label = form.label.trim();
  if (!label) return { problem: 'Give the link a short label.', field: 'label' };
  if (label.length > 200) return { problem: 'The label is longer than 200 characters.', field: 'label' };
  return {
    input: {
      linkCategory: 'evidence',
      subjectKind: 'review',
      subjectId: form.reviewId,
      linkKindKey: form.linkKindKey,
      url,
      label,
      ...(form.evidenceLevelKey ? { evidenceLevelKey: form.evidenceLevelKey } : {}),
    },
  };
}
