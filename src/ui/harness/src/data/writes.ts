// The web app's writes (D-36), the parts with no React in them: the retry ID each write carries, and the engine's
// answer read into what the action sheet shows.
//
// A person writes through POST <apiBase>/<tool> (apiClient.ts), on the product's person entry. The engine checks the
// write exactly as it checks an AI client's: Tool Policy roles, the input schema, the engine's guards, the decision
// rights, the separation rules. A refused write writes nothing. AI drafts and a person accepts (D-28); a person decides
// every approval, on the decision page (D-38).
import type {
  ApprovalAsked,
  ApprovalCardState,
  ApprovalMode,
  ApprovalNeeded,
  ApprovalRequestBrief,
  ApprovalRequested,
  DataUseImpactItem,
  DeploymentFlags,
  Envelope,
  ErrorCode,
  ImportDataTaxonomyResult,
  MoveOwnerChange,
  PerformedBy,
  Warning,
  WriteToolData,
  WriteToolInput,
  WriteToolName,
} from './contracts';
import { fmtDate, forSomeoneElse, humanizeDates } from './format';
import { dataUseStateText, governingRuleText, needsAttention } from './dataTaxonomy';
import { activeFlags, flagText } from './meanings';

// The input a page builds: the tool's input without the fields the action sheet sets itself (the retry key and
// validateOnly).
export type WriteInput<N extends WriteToolName> = Omit<WriteToolInput[N], 'clientRequestId' | 'idempotencyKey' | 'validateOnly'>;

// The input field each write tool takes its retry key in. The two deployment writes take the sender's idempotencyKey
// (a pipeline sends its run ID there); run_due_state_refresh takes none (a rerun the same day changes nothing, so it
// needs no key, and its schema allows none); every other write takes clientRequestId. The schemas allow no other
// field, so the action sheet sends the key in the one each tool names.
export const RETRY_KEY_FIELD: Partial<Record<WriteToolName, 'idempotencyKey' | null>> = {
  record_deployment: 'idempotencyKey',
  record_deployment_correction: 'idempotencyKey',
  run_due_state_refresh: null,
};

export function retryKeyField(tool: WriteToolName): 'clientRequestId' | 'idempotencyKey' | null {
  const field = RETRY_KEY_FIELD[tool];
  return field === undefined ? 'clientRequestId' : field;
}

// The body a write sends: the page's input, validateOnly for a check, and the retry key in the tool's own field. A check
// writes nothing, so it carries no clientRequestId; a tool whose schema requires idempotencyKey gets a fresh one that
// matches no earlier record.
export function writeBody(tool: WriteToolName, input: Record<string, unknown>, preview: boolean, retryId: (body: Record<string, unknown>) => string, freshId: () => string = newClientRequestId): Record<string, unknown> {
  const body: Record<string, unknown> = { ...input };
  delete body.clientRequestId;
  delete body.idempotencyKey;
  delete body.validateOnly;
  const field = retryKeyField(tool);
  if (preview) {
    body.validateOnly = true;
    if (field === 'idempotencyKey') body.idempotencyKey = freshId();
  } else if (field !== null) {
    body[field] = retryId(body);
  }
  return body;
}

// ---------- the retry ID (clientRequestId) ----------

const ID_MAX = 64;

// A new retry ID: "web-" and a random UUID. The server keeps the first result for an ID: a retry with the same ID
// returns it and writes nothing new; the same ID with a different payload answers conflict.
export function newClientRequestId(random: () => string = defaultRandom): string {
  return `web-${random()}`.slice(0, ID_MAX);
}

function defaultRandom(): string {
  const c = (globalThis as { crypto?: Crypto }).crypto;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID();
  // Older browsers: a random value is enough here; the ID only matches a retry to its first attempt.
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}-${Math.random().toString(36).slice(2, 12)}`;
}

// The payload a retry ID stands for: the input without the retry key (clientRequestId or idempotencyKey) and
// validateOnly, keys in a fixed order.
export function payloadKey(input: Record<string, unknown>): string {
  const canonical = (value: unknown): unknown => {
    if (Array.isArray(value)) return value.map(canonical);
    if (value && typeof value === 'object') {
      return Object.fromEntries(
        Object.entries(value as Record<string, unknown>)
          .filter(([k, v]) => v !== undefined && k !== 'clientRequestId' && k !== 'idempotencyKey' && k !== 'validateOnly')
          .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
          .map(([k, v]) => [k, canonical(v)]),
      );
    }
    return value;
  };
  return JSON.stringify(canonical(input));
}

// Keeps one write's retry ID. The same ID is sent again only when the last attempt's outcome is unknown (a timeout or a
// network fault) and the payload is the same, so a retry never writes twice. After any answer from the server, ok or
// refused, the next attempt gets a new ID.
export interface RequestIdKeeper {
  idFor(input: Record<string, unknown>): string;
  settle(envelope: Envelope<unknown>): void;
}

export function createRequestIdKeeper(newId: () => string = newClientRequestId): RequestIdKeeper {
  let pending: { key: string; id: string } | null = null;
  return {
    idFor(input) {
      const key = payloadKey(input);
      if (!pending || pending.key !== key) pending = { key, id: newId() };
      return pending.id;
    },
    settle(envelope) {
      if (!outcomeUnknown(envelope)) pending = null;
    },
  };
}

// True when a failure may still have written: the product did not answer (timeout, network fault, 5xx).
export function outcomeUnknown(envelope: Envelope<unknown>): boolean {
  return !envelope.ok && envelope.error.code === 'upstream_unavailable';
}

// ---------- the engine's answer ----------

// One approval the engine asks for next, or will ask for once the record is submitted.
export interface AskedNext {
  // A product role (C29), an owner role on the application (C28), each proposed owner by name, or one named person.
  by: 'role' | 'owner-role' | 'proposed-owners' | 'person';
  key: string;
  // The person asked by name, when by is person.
  personName?: string;
  isConsultation: boolean;
  approvalMode: ApprovalMode | null;
  minimumCount: number | null;
  expiresOn: string | null;
}

export type EngineAnswer =
  | {
    kind: 'ok';
    // True when the call ran with validateOnly: every rule ran and nothing was written.
    preview: boolean;
    summary: string;
    // "Asked now" after a submit; "will be asked" for a draft or a proposal that is not submitted yet.
    askedNow: AskedNext[];
    askedLater: AskedNext[];
    // Plain sentences about what the engine did, in the order the page shows them.
    facts: string[];
    warnings: Warning[];
    // The stamp on the write: who acted, through which client app ("Web app, demo operator" for a demo persona).
    stampedAs: string | null;
    recordUrl: string | null;
    correlationId: string;
  }
  | {
    kind: 'refused';
    preview: boolean;
    code: ErrorCode;
    title: string;
    message: string;
    // The input field at fault, when there is one.
    field: string | null;
    // The configuration row that refused the write, and the guideline its rule applies (such as G17).
    ruleKey: string | null;
    guideline: string | null;
    retryAfterSeconds: number | null;
    // step_up_required: the claims challenge to sign in again with, or null when a fresh sign-in alone is enough.
    claimsChallenge: string | null;
    // The record the refusal points to, such as the link to update instead, or null.
    recordUrl: string | null;
    // True when the product may have written (no answer): a retry with the same input is safe and writes nothing twice.
    outcomeUnknown: boolean;
    correlationId: string;
  };

const REFUSAL_TITLES: Record<ErrorCode, string> = {
  not_signed_in: 'Sign in again to do this',
  not_authorized: 'You cannot do this',
  step_up_required: 'Sign in again to answer',
  ai_cannot_do_this: 'Only a person can do this',
  rule_violation: 'A rule refused this',
  validation: 'Check what you entered',
  not_found: 'The record was not found',
  conflict: 'The record changed in the meantime',
  locked: 'The record is locked',
  rate_limited: 'Too many requests just now',
  upstream_unavailable: 'The product did not answer',
  configuration_unavailable: 'The rules could not be read',
};

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

function fromNeeded(list: ApprovalNeeded[] | undefined): AskedNext[] {
  return (list ?? []).flatMap(a => {
    const key = a.approverRoleKey ?? a.approverOwnerRoleKey ?? (a.approverProposedOwners ? 'proposed-owners' : undefined);
    if (!key) return [];
    return [{
      by: a.approverRoleKey ? 'role' as const : a.approverOwnerRoleKey ? 'owner-role' as const : 'proposed-owners' as const,
      key,
      isConsultation: a.isConsultation === true,
      approvalMode: a.approvalMode,
      minimumCount: a.minimumCount,
      expiresOn: null,
    }];
  });
}

function fromRequested(list: ApprovalRequested[] | undefined): AskedNext[] {
  return (list ?? []).flatMap(a => {
    const key = a.requiredRoleKey ?? a.requiredOwnerRoleKey;
    if (!key) return [];
    return [{
      by: a.askedPrincipal ? 'person' as const : a.requiredRoleKey ? 'role' as const : 'owner-role' as const,
      key,
      ...(a.askedPrincipal ? { personName: a.askedPrincipal.displayLabel || 'A named person' } : {}),
      isConsultation: a.isConsultation,
      approvalMode: a.approvalMode,
      minimumCount: null,
      expiresOn: a.expiresOn,
    }];
  });
}

// The approval requests a data write opened (save_data_use approvalsRequested): a named person, a role or an owner role.
function fromAsked(list: ApprovalAsked[] | undefined): AskedNext[] {
  return (list ?? []).flatMap(a => {
    const key = a.requiredRoleKey ?? a.requiredOwnerRoleKey;
    if (!key) return [];
    return [{
      by: a.askedPrincipal ? 'person' as const : a.requiredRoleKey ? 'role' as const : 'owner-role' as const,
      key,
      ...(a.askedPrincipal ? { personName: a.askedPrincipal.displayLabel || 'A named person' } : {}),
      isConsultation: a.isConsultation,
      approvalMode: null,
      minimumCount: null,
      expiresOn: null,
    }];
  });
}

// The engine's flags on a deployment, in words: "came another way, outside window".
function deploymentFlagText(flags: DeploymentFlags): string[] {
  return activeFlags(flags).map(k => flagText[k].toLowerCase());
}

function stampOf(performedBy: PerformedBy | undefined): string | null {
  if (!performedBy?.principal) return null;
  const who = performedBy.principal.displayLabel || performedBy.principal.objectId;
  const client = performedBy.clientApp?.label;
  const other = forSomeoneElse(performedBy.principal, performedBy.onBehalfOf);
  const forWhom = other ? ` for ${other.displayLabel || other.objectId}` : '';
  return `${who}${forWhom}${client ? `, through ${client}` : ''}`;
}

// What a move did to the decisions when the data owner changes, or would do in a check: one line per kind, so the
// person sees before moving who will be asked and which rows are declared again.
export function moveOwnerChangeFacts(change: MoveOwnerChange, to: string, preview: boolean): string[] {
  const owner = change.newDataOwner?.displayLabel ?? null;
  const lines: string[] = [];
  const line = (n: number, done: string, would: string) => { if (n > 0) lines.push(preview ? would : done); };
  line(change.redeclaredCount,
    `${plural(change.redeclaredCount, 'decided row was', 'decided rows were')} ended and declared again in ${to}. The accepted decision stays with the ended row.`,
    `${plural(change.redeclaredCount, 'decided row would be', 'decided rows would be')} ended and declared again in ${to}. The accepted decision would stay with the ended row.`);
  line(change.withdrawnAnsweredCount,
    `${plural(change.withdrawnAnsweredCount, 'decision someone had answered was', 'decisions someone had answered were')} withdrawn.`,
    `${plural(change.withdrawnAnsweredCount, 'decision someone has answered would be', 'decisions someone has answered would be')} withdrawn.`);
  line(change.withdrawnNoOwnerCount,
    `${plural(change.withdrawnNoOwnerCount, 'waiting decision was', 'waiting decisions were')} withdrawn: ${to} has no data owner.`,
    `${plural(change.withdrawnNoOwnerCount, 'waiting decision would be', 'waiting decisions would be')} withdrawn: ${to} has no data owner.`);
  const asks = owner ?? 'the new data owner';
  line(change.reaskedCount,
    `${plural(change.reaskedCount, 'waiting decision now asks', 'waiting decisions now ask')} ${asks}, from the start.`,
    `${plural(change.reaskedCount, 'waiting decision would ask', 'waiting decisions would ask')} ${asks}, from the start.`);
  line(change.openedCount,
    `The engine opened ${plural(change.openedCount, 'new decision', 'new decisions')} for this move. ${change.openedCount === 1 ? 'It asks' : 'They ask'} ${asks}.`,
    `The engine would open ${plural(change.openedCount, 'new decision', 'new decisions')} for this move, asking ${asks}.`);
  line(change.waitingForOwnerCount,
    `${plural(change.waitingForOwnerCount, 'row waits', 'rows wait')} for a data owner of ${to} before anyone can decide.`,
    `${plural(change.waitingForOwnerCount, 'row would wait', 'rows would wait')} for a data owner of ${to} before anyone could decide.`);
  return lines;
}

// ---------- wave 4: the import, "Ask again", "Send again" and a proposal's data uses ----------

// The request "Ask again" opened: who it asks and until when.
function askedFromBrief(r: ApprovalRequestBrief): AskedNext {
  const key = r.askedRole?.key ?? r.askedOwnerRole?.key ?? 'someone';
  return {
    by: r.askedPerson ? 'person' : r.askedRole ? 'role' : 'owner-role',
    key,
    ...(r.askedPerson ? { personName: r.askedPerson.displayLabel || 'A named person' } : {}),
    isConsultation: r.isConsultation,
    approvalMode: null,
    minimumCount: null,
    expiresOn: r.expiresOn,
  };
}

// What happened to a card's request while it waited, for "Send again" that closes it instead.
const CARD_SETTLED_WORDS: Partial<Record<ApprovalCardState, string>> = {
  answered: 'was answered',
  expired: 'expired',
  cancelled: 'was canceled',
};

// The data uses and copies a configuration change would move, in counts: what changes state, what would break a rule
// and what would need a new decision.
export function dataUseImpactFacts(items: readonly DataUseImpactItem[]): string[] {
  if (!items.length) return [];
  const uses = items.filter(i => i.kind === 'data-use').length;
  const copies = items.length - uses;
  const what = [uses ? plural(uses, 'data use', 'data uses') : null, copies ? plural(copies, 'copy', 'copies') : null].filter(Boolean).join(' and ');
  const lines = [`${what} would change state when the change takes effect.`];
  const breaks = items.filter(i => i.breaksRule).length;
  if (breaks) lines.push(`${plural(breaks, 'row would break', 'rows would break')} a rule.`);
  const decide = items.filter(i => i.needsNewDecision).length;
  if (decide) lines.push(`${plural(decide, 'row would need', 'rows would need')} a new decision.`);
  return lines;
}

// What an import of data domains did, or would do: the rows read, the draft's changes, what the file leaves out, the
// problems, the owners it prepared and the catalog owners that differ (DC-15).
export function importFacts(d: ImportDataTaxonomyResult, preview: boolean): string[] {
  const lines: string[] = [];
  if (typeof d.rowsRead === 'number') lines.push(`${plural(d.rowsRead, 'row', 'rows')} read.`);
  const c = d.counts;
  if (c) {
    // "The draft adds 3 domains, changes 2 domains" once saved; "The draft would add 3 domains, change 2 domains" in a check.
    const verb = (done: string, would: string) => (preview ? would : done);
    const parts = [
      c.domainsAdded ? `${verb('adds', 'add')} ${plural(c.domainsAdded, 'domain', 'domains')}` : null,
      c.domainsChanged ? `${verb('changes', 'change')} ${plural(c.domainsChanged, 'domain', 'domains')}` : null,
      c.matchesAdded ? `${verb('adds', 'add')} ${plural(c.matchesAdded, 'match', 'matches')}` : null,
    ].filter(Boolean);
    if (parts.length) lines.push(`The draft ${preview ? 'would ' : ''}${parts.join(', ')}.`);
    else lines.push(preview ? 'The draft would change no domain.' : 'The draft changes no domain.');
    if (c.domainsUnchanged) lines.push(`${plural(c.domainsUnchanged, 'domain is', 'domains are')} already as the file says.`);
  }
  if (d.notInSource?.length) lines.push(`${plural(d.notInSource.length, 'domain in the configuration is', 'domains in the configuration are')} not in the file. The import never retires a domain.`);
  if (d.problems?.length) lines.push(`${plural(d.problems.length, 'row has a problem', 'rows have a problem')}. Fix ${d.problems.length === 1 ? 'it' : 'them'} and import again.`);
  if (d.valid === false) lines.push(`The draft breaks ${plural(d.errors?.length ?? 0, 'rule', 'rules')} of the configuration. It cannot be submitted until they are fixed.`);
  if (d.preparedOwnerChanges?.length) lines.push(`${plural(d.preparedOwnerChanges.length, 'owner change is', 'owner changes are')} prepared. Owners change only through an owner change a person sends.`);
  if (d.catalogOwnerDifferences?.length) lines.push(`${plural(d.catalogOwnerDifferences.length, 'domain has', 'domains have')} a different data owner in the catalog (DC-15).`);
  if (!preview && d.configurationDraftId) lines.push('Nothing changes until the draft is submitted and a person accepts the decision.');
  return lines;
}

// True when the engine's summary already states a fact: the fact is one sentence, and the summary holds its count with
// the same noun a few words after it (wave 4b review). "5 approval requests were canceled." is said by "Withdrawn. 5
// waiting approvals are canceled.", so the sheet does not say it twice. A fact of more than one sentence carries more
// than its count, and stays.
export function restatedInSummary(summary: string, fact: string): boolean {
  const sentence = fact.trim().replace(/\.$/, '');
  if (!sentence || sentence.includes('. ')) return false;
  const words = (text: string) => text.toLowerCase().replace(/[^a-z0-9 ]+/g, ' ').split(' ').filter(Boolean);
  const stem = (word: string) => word.replace(/(es|s)$/, '');
  const factWords = words(sentence);
  const at = factWords.findIndex(w => /^\d+$/.test(w));
  if (at < 0) return false;
  const count = factWords[at];
  // The noun after the count: its first word that is not a plain qualifier ("5 waiting approvals", "2 open claims").
  const qualifiers = new Set(['waiting', 'open', 'new', 'other', 'pending', 'more']);
  const noun = factWords.slice(at + 1, at + 4).find(w => !qualifiers.has(w));
  if (!noun) return false;
  const said = words(summary);
  return said.some((w, i) => w === count && said.slice(i + 1, i + 4).some(n => stem(n) === stem(noun)));
}

// The facts and the approvals a tool's result carries. Each write tool says different things; anything a result does
// not carry is left out, never guessed. A check (preview, validateOnly) wrote nothing, so its facts say what would
// happen: "would count", never "counts". After a write, a fact the engine's summary already states is left out
// (restatedInSummary).
function readResult<N extends WriteToolName>(tool: N, raw: WriteToolData[N], preview: boolean, summary = ''): { askedNow: AskedNext[]; askedLater: AskedNext[]; facts: string[] } {
  const read = readResultFacts(tool, raw, preview);
  if (preview || !summary) return read;
  return { ...read, facts: read.facts.filter(f => !restatedInSummary(summary, f)) };
}

function readResultFacts<N extends WriteToolName>(tool: N, raw: WriteToolData[N], preview: boolean): { askedNow: AskedNext[]; askedLater: AskedNext[]; facts: string[] } {
  const facts: string[] = [];
  let askedNow: AskedNext[] = [];
  let askedLater: AskedNext[] = [];
  const data = raw as unknown as Record<string, unknown>;
  // The same fact as done, or as what would happen.
  const say = (done: string, would: string) => facts.push(preview ? would : done);
  switch (tool) {
    case 'answer_approval': {
      const d = raw as WriteToolData['answer_approval'];
      if (d.isConsultation) say('Your answer is recorded as advice. A consultation never counts toward the approval.', 'Your answer would be recorded as advice. A consultation never counts toward the approval.');
      else if (d.countsTowardRequirement) say('Your answer counts toward the approval.', 'Your answer would count toward the approval.');
      else say('Your answer is recorded, and it does not count toward the approval.', 'Your answer would be recorded, and it would not count toward the approval.');
      // An answer that does not settle the decision leaves it as it was: "stays proposed", never "is now proposed".
      if (d.decisionStatus) {
        const label = d.decisionStatus.label.toLowerCase();
        if (d.decisionStatus.meaning === 'proposed') say(`The decision stays ${label}.`, `The decision would stay ${label}.`);
        else say(`The decision is now ${label}.`, `The decision would be ${label}.`);
      }
      if (d.openedApprovalRecordIds?.length) {
        const n = plural(d.openedApprovalRecordIds.length, 'approval request', 'approval requests');
        say(`The next step opened ${n}.`, `The next step would open ${n}.`);
      }
      if (d.cancelledApprovalRecordIds?.length) {
        const one = d.cancelledApprovalRecordIds.length === 1;
        const n = plural(d.cancelledApprovalRecordIds.length, 'request that is', 'requests that are');
        say(`${n} no longer needed ${one ? 'was' : 'were'} canceled.`, `${n} no longer needed would be canceled.`);
      }
      break;
    }
    case 'submit_record': {
      const d = raw as WriteToolData['submit_record'];
      askedNow = fromRequested(d.approvalsRequested);
      if (d.engineAccepted) {
        if (d.engineAccepted.condition === 'standard-model') say('The engine accepted the decision under an approved standard change model. No approval was needed.', 'The engine would accept the decision under an approved standard change model. No approval would be needed.');
        else say('The engine accepted the decision: the lowest tier needs no approval.', 'The engine would accept the decision: the lowest tier needs no approval.');
      }
      if (d.standardChangeApplied) facts.push(`The standard change model ${d.standardChangeApplied.modelKey} applies.`);
      if (!preview && !d.engineAccepted && !askedNow.length && d.status?.meaning === 'submitted') facts.push('Submitted. No approval request was opened.');
      if (preview && !d.engineAccepted && !askedNow.length) facts.push('It would be submitted, and no approval request would open.');
      break;
    }
    case 'save_decision_proposal': {
      const d = raw as WriteToolData['save_decision_proposal'];
      askedLater = fromNeeded(d.approvalsNeeded);
      const decider = d.decider?.principal?.displayLabel;
      const deciderRole = d.decider?.accountableRoleKey ?? d.decider?.accountableOwnerRoleKey;
      // The person who proposes and decides reads "You are the decider", never their own name as if someone else
      // decided (wave 4b QA walk: S-22.13 said "The decider is Nadia Ferreira" to Nadia, and nobody else is asked).
      const stamp = (raw as { performedBy?: PerformedBy }).performedBy;
      const self = stamp?.onBehalfOf?.objectId ?? stamp?.principal?.objectId;
      const deciderId = d.decider?.principal?.objectId;
      if (decider && self && deciderId && self.toLowerCase() === deciderId.toLowerCase()) facts.push(preview ? 'You would be the decider.' : 'You are the decider.');
      else if (decider) facts.push(`The decider ${preview ? 'would be' : 'is'} ${decider}.`);
      // The role key goes in backticks, so the answer shows the role's label (QA walk).
      else if (deciderRole) facts.push(`The decider ${preview ? 'would hold' : 'holds'} the role \`${deciderRole}\`.`);
      if (!preview) facts.push('The proposal is saved. Submit it to ask for the approvals.');
      break;
    }
    case 'propose_owner_change': {
      const d = raw as WriteToolData['propose_owner_change'];
      askedLater = fromNeeded(d.approvalsNeeded);
      if (d.pendingAssignments?.length) {
        const n = d.pendingAssignments.length;
        say(`${plural(n, 'owner assignment waits', 'owner assignments wait')} for the ownership decision.`, `${plural(n, 'owner assignment would wait', 'owner assignments would wait')} for the ownership decision.`);
      }
      break;
    }
    case 'claim_resource': {
      const d = raw as WriteToolData['claim_resource'];
      // A person's claim submits the ownership decision in the same call (decisionSubmitted): the owners are asked now,
      // or would be asked in a check, which opens no request and lists the approvals needed instead. Only an AI
      // client's claim leaves the decision proposed, to be asked once the person submits it (wave 4b review).
      if (d.decisionSubmitted) askedNow = preview ? fromNeeded(d.approvalsNeeded) : fromRequested(d.approvalsRequested);
      else askedLater = fromNeeded(d.approvalsNeeded);
      if (d.applicationCreated) say('A new draft application was registered for the claimed resource.', 'A new draft application would be registered for the claimed resource.');
      break;
    }
    case 'save_change_request_draft': {
      const d = raw as WriteToolData['save_change_request_draft'];
      askedLater = fromNeeded(d.requirementsPreview?.approvals);
      if (d.requirementsPreview) facts.push(d.requirementsPreview.requiresAuthorization ? 'This change needs authorization before it is deployed.' : 'This change needs no authorization.');
      if (d.affectedApplicationIds?.length) {
        const n = d.affectedApplicationIds.length;
        say(`${plural(n, 'other application is', 'other applications are')} marked as affected.`, `${plural(n, 'other application would be', 'other applications would be')} marked as affected.`);
      }
      break;
    }
    case 'withdraw_record': {
      const d = raw as WriteToolData['withdraw_record'];
      // After the write the engine's summary says what was canceled ("Withdrawn. 5 waiting approvals are canceled."), so
      // only a check, whose summary is the page's own, says it (wave 4b review).
      if (preview && d.cancelledApprovals?.length) {
        const n = d.cancelledApprovals.length;
        facts.push(`${plural(n, 'approval request would be', 'approval requests would be')} canceled.`);
      }
      break;
    }
    case 'record_owner_leave': {
      const d = raw as WriteToolData['record_owner_leave'];
      if (d.affected?.length) {
        const noDeputy = d.affected.filter(a => !a.hasDeputy).length;
        const deputies = noDeputy === 0
          ? (d.affected.length === 1 ? 'It has a deputy.' : 'Each has a deputy.')
          : noDeputy === d.affected.length
            ? (noDeputy === 1 ? 'It has no deputy.' : 'None of them has a deputy.')
            : `${noDeputy} of them ${noDeputy === 1 ? 'has' : 'have'} no deputy.`;
        // The engine's summary counts the roles affected; the fact says only who stands in (QA walk: it said the count twice).
        facts.push(deputies);
      }
      const total = d.ownerCheckReviewIds?.length ?? 0;
      const reused = Math.min(total, d.reusedOwnerCheckReviewIds?.length ?? 0);
      const opened = total - reused;
      if (total) {
        const split = reused ? ` (${opened} new, ${reused} already open)` : '';
        say(
          `${plural(total, 'owner check review covers', 'owner check reviews cover')} this leave${split}.`,
          `${plural(total, 'owner check review would cover', 'owner check reviews would cover')} this leave${split}.`,
        );
      }
      if (d.pendingApprovals) facts.push(`${plural(d.pendingApprovals, 'pending approval waits', 'pending approvals wait')} on this person.`);
      break;
    }
    case 'answer_tier_questions': {
      const d = raw as WriteToolData['answer_tier_questions'];
      if (d.computedTier) facts.push(`Suggested tier: ${d.computedTier.label}. Only an accepted tier decision sets the tier.`);
      break;
    }
    case 'record_change_result': {
      const d = raw as WriteToolData['record_change_result'];
      if (d.result) say(`Result recorded: ${d.result.label}.`, `The result would be recorded as ${d.result.label}.`);
      if (d.postChangeReviewId) say('A post-change review was opened.', 'A post-change review would be opened.');
      break;
    }
    case 'record_document_link':
    case 'confirm_document_checked': {
      const d = raw as WriteToolData['record_document_link'] | WriteToolData['confirm_document_checked'];
      // A document marked stale is stale now: its stale date says nothing more (QA walk).
      const staleNow = (d as { status?: { meaning?: string } | null }).status?.meaning === 'stale';
      if (staleNow) say('It is stale now, until someone confirms it again.', 'It would be stale now, until someone confirms it again.');
      else if (d.staleOn) {
        const on = fmtDate(d.staleOn, d.staleOn);
        say(`The document turns stale on ${on} unless someone confirms it again.`, `The document would turn stale on ${on} unless someone confirms it again.`);
      }
      if (d.confirmationCardSent) say('The document owner was asked to confirm it.', 'The document owner would be asked to confirm it.');
      break;
    }
    case 'record_deployment':
    case 'record_deployment_correction': {
      const d = raw as WriteToolData['record_deployment'] | WriteToolData['record_deployment_correction'];
      const flags = d.flags ? deploymentFlagText(d.flags) : [];
      if (flags.length) say(`The engine flagged it: ${flags.join(', ')}.`, `The engine would flag it: ${flags.join(', ')}.`);
      else if (d.flags) say('The engine raised no flag.', 'The engine would raise no flag.');
      if (d.gateLevelApplied) facts.push(`Gate level applied: ${d.gateLevelApplied}.`);
      if (tool === 'record_deployment_correction') say('The record it corrects stays as it was, and the new record names it.', 'The record it corrects would stay as it was, and the new record would name it.');
      break;
    }
    case 'run_gap_check':
    case 'compare_inventory_list': {
      const d = raw as WriteToolData['run_gap_check'];
      if (typeof d.itemsSeen === 'number') facts.push(`${plural(d.itemsSeen, 'item', 'items')} seen.`);
      if (d.newClaims?.length) say(`${plural(d.newClaims.length, 'new claim was', 'new claims were')} opened for items not in the register.`, `${plural(d.newClaims.length, 'new claim would be', 'new claims would be')} opened for items not in the register.`);
      if (d.vanished?.length) say(`${plural(d.vanished.length, 'registered item has', 'registered items have')} vanished from the estate.`, `${plural(d.vanished.length, 'registered item would show', 'registered items would show')} as vanished.`);
      if (d.more) facts.push('The run has more pages. Run it again to continue.');
      break;
    }
    case 'run_owner_check': {
      const d = raw as WriteToolData['run_owner_check'];
      if (typeof d.principalsChecked === 'number') facts.push(`${plural(d.principalsChecked, 'owner', 'owners')} checked.`);
      if (d.findings?.length) say(`${plural(d.findings.length, 'owner needs', 'owners need')} a successor or a check.`, `${plural(d.findings.length, 'owner would need', 'owners would need')} a successor or a check.`);
      if (d.more) facts.push('The check has more pages. Run it again to continue.');
      break;
    }
    case 'link_component': {
      const d = raw as WriteToolData['link_component'];
      const what = d.outcome === 'retired' ? 'marked as gone' : d.outcome === 'moved' ? 'recorded as moved' : 'linked to the application';
      say(`The component is ${what}.`, `The component would be ${what}.`);
      if (d.claimStatus) say(`The claim's status is now ${d.claimStatus.label.toLowerCase()}.`, `The claim's status would be ${d.claimStatus.label.toLowerCase()}.`);
      break;
    }
    case 'request_architecture_review': {
      const d = raw as WriteToolData['request_architecture_review'];
      if (d.existingReviewReturned) facts.push('A review of this kind was already open, so no new one was opened.');
      else if (d.dueOn) {
        const on = fmtDate(d.dueOn, d.dueOn);
        say(`A review was opened, due ${on}.`, `A review would be opened, due ${on}.`);
      }
      break;
    }
    case 'save_application_draft': {
      const d = raw as WriteToolData['save_application_draft'];
      askedLater = fromNeeded(d.approvalsNeeded);
      if (d.components?.length) say(`${plural(d.components.length, 'component is', 'components are')} linked.`, `${plural(d.components.length, 'component would be', 'components would be')} linked.`);
      if (d.confirmationCardSent) say('The proposed owners were asked to confirm.', 'The proposed owners would be asked to confirm.');
      break;
    }
    case 'complete_review': {
      const d = raw as WriteToolData['complete_review'];
      if (d.nextDueOn) {
        const on = fmtDate(d.nextDueOn, d.nextDueOn);
        say(`The next review is due ${on}.`, `The next review would be due ${on}.`);
      }
      const met = (d.gates ?? []).filter(g => g.met).length;
      if (met) say(`${plural(met, 'gate this review feeds is', 'gates this review feeds are')} met.`, `${plural(met, 'gate this review feeds would be', 'gates this review feeds would be')} met.`);
      break;
    }
    case 'complete_change': {
      const d = raw as WriteToolData['complete_change'];
      // The engine's summary already names the result in a closed change's answer (QA walk); the check says it here.
      if (d.result && preview) facts.push(`The change would be closed with the result ${d.result.label}.`);
      if (d.postChangeReviewId) say('A post-change review was opened.', 'A post-change review would be opened.');
      break;
    }
    // ---- wave 3: the data taxonomy, the due-state refresh and the configuration change ----
    case 'save_data_use': {
      const d = raw as WriteToolData['save_data_use'];
      askedNow = fromAsked(d.approvalsRequested);
      if (d.governingRule) facts.push(governingRuleText(d.governingRule));
      if (d.use?.state) {
        const state = dataUseStateText(d.use.state, d.use.approvalsGiven, d.use.approvalsNeeded).toLowerCase();
        say(`The use's state: ${state}.`, `The use's state would be: ${state}.`);
      }
      if (d.replacedDataUseId) say('The earlier use is ended, and its decision stays with it.', 'The earlier use would end, and its decision would stay with it.');
      if (d.decisionId) {
        if (d.decisionSubmitted) say('A data-use decision was opened and sent to the people who answer it.', 'A data-use decision would be opened and sent.');
        else say('A data-use decision was opened. A person submits it.', 'A data-use decision would be opened.');
      }
      break;
    }
    case 'save_data_location': {
      const d = raw as WriteToolData['save_data_location'];
      const parts = [
        d.createdCount ? plural(d.createdCount, 'location', 'locations') + (preview ? ' would be added' : ' added') : null,
        d.updatedCount ? plural(d.updatedCount, 'location', 'locations') + (preview ? ' would be updated' : ' updated') : null,
      ].filter(Boolean);
      if (parts.length) facts.push(`${parts.join(', ')}.`);
      const matched = (d.items ?? []).filter(i => i.matchedExisting).length;
      if (matched) facts.push(`${plural(matched, 'item matched a location', 'items matched locations')} already on record.`);
      break;
    }
    case 'save_data_mapping': {
      const d = raw as WriteToolData['save_data_mapping'];
      const items = d.items ?? [];
      const created = items.filter(i => i.created).length;
      if (created) say(`${plural(created, 'mapping was', 'mappings were')} added.`, `${plural(created, 'mapping would be', 'mappings would be')} added.`);
      const copies = items.filter(i => i.decisionId).length;
      if (copies) say(`${plural(copies, 'copy decision was', 'copy decisions were')} opened.`, `${plural(copies, 'copy decision would be', 'copy decisions would be')} opened.`);
      const breaking = items.filter(i => i.copyState === 'breaks-rule').length;
      if (breaking) facts.push(`${plural(breaking, 'copy breaks', 'copies break')} the copy rule.`);
      break;
    }
    case 'move_data_references': {
      const d = raw as WriteToolData['move_data_references'];
      const moved = [
        d.movedMappingIds?.length ? plural(d.movedMappingIds.length, 'mapping', 'mappings') : null,
        d.movedUseIds?.length ? plural(d.movedUseIds.length, 'use', 'uses') : null,
      ].filter(Boolean).join(' and ');
      const from = d.fromDomain?.path ?? 'the domain';
      const to = d.toDomain?.path ?? 'the new domain';
      // The decisions stay with the rows only while the data owner stays the same (wave 3 verification: the sheet said
      // "Their decisions stay with them" beside an engine answer that declared rows again and withdrew a decision).
      const change = d.ownerChange ?? null;
      if (!moved) facts.push(`Nothing to move from ${from}.`);
      else if (!change) say(`Moved ${moved} from ${from} to ${to}. Both domains have the same data owner, so each row kept its identity and its decisions.`, `It would move ${moved} from ${from} to ${to}. Both domains have the same data owner, so each row would keep its identity and its decisions.`);
      else {
        say(`Moved ${moved} from ${from} to ${to}. The data owner changes, so the decisions did not move with the rows.`, `It would move ${moved} from ${from} to ${to}. The data owner changes, so the decisions would not move with the rows.`);
        facts.push(...moveOwnerChangeFacts(change, to, preview));
      }
      const again = (d.changedUses ?? []).filter(u => needsAttention(u.stateAfter) && !needsAttention(u.stateBefore)).length;
      if (again && !change) say(`${plural(again, 'use now needs', 'uses now need')} a decision again.`, `${plural(again, 'use would need', 'uses would need')} a decision again.`);
      break;
    }
    case 'run_due_state_refresh': {
      const d = raw as WriteToolData['run_due_state_refresh'];
      const c = d.counts;
      if (c) {
        const done = [
          c.reviewsOpened ? plural(c.reviewsOpened, 'review opened', 'reviews opened') : null,
          c.documentsStale ? plural(c.documentsStale, 'document turned stale', 'documents turned stale') : null,
          c.reminders ? plural(c.reminders, 'reminder', 'reminders') : null,
          c.escalations ? plural(c.escalations, 'escalation', 'escalations') : null,
          c.approvalsExpired ? plural(c.approvalsExpired, 'approval request expired', 'approval requests expired') : null,
          c.flagNotices ? plural(c.flagNotices, 'flag notice', 'flag notices') : null,
        ].filter(Boolean);
        facts.push(done.length ? `${preview ? 'It would do' : 'Done'}: ${done.join(', ')}.` : 'Nothing was due. A rerun today changes nothing.');
        if (c.failed) facts.push(`${plural(c.failed, 'item failed', 'items failed')}. The rest were kept.`);
      }
      if (d.more) facts.push('More items wait. Run it again to continue.');
      break;
    }
    case 'propose_configuration_change': {
      const d = raw as WriteToolData['propose_configuration_change'];
      askedLater = fromNeeded(d.approvalsNeeded);
      if (d.valid === false) facts.push(`The draft breaks ${plural(d.errors?.length ?? 0, 'rule', 'rules')} of the configuration. It cannot be submitted until they are fixed.`);
      if (d.diffSummary?.length) facts.push(`${plural(d.diffSummary.length, 'row changes', 'rows change')}.`);
      if (d.impact) {
        if (d.impact.applicationsAffected) facts.push(`${plural(d.impact.applicationsAffected, 'application is', 'applications are')} affected.`);
        if (d.impact.gateChanges?.length) facts.push(`${plural(d.impact.gateChanges.length, 'gate level changes', 'gate levels change')}.`);
        if (d.impact.recordsInFlight) facts.push(`${plural(d.impact.recordsInFlight, 'record in flight keeps', 'records in flight keep')} the version they started under.`);
        // Wave 4 (B24-22): the data uses and copies whose state the change would move, in the proposal's own preview.
        facts.push(...dataUseImpactFacts(d.impact.dataUses ?? []));
      }
      if (!preview && d.configurationDraftId) facts.push('Nothing changes until the draft is submitted and a person accepts the decision.');
      break;
    }
    // ---- wave 4: tools 99 to 101 (document 24, B24-22) ----
    case 'import_data_taxonomy': {
      const d = raw as WriteToolData['import_data_taxonomy'];
      facts.push(...importFacts(d, preview));
      break;
    }
    case 'renew_approval_request': {
      const d = raw as WriteToolData['renew_approval_request'];
      if (d.request) askedNow = [askedFromBrief(d.request)];
      if (!d.renewed) facts.push('The request was already asked again, so the open request stands and nothing was written.');
      // The engine closes the old request as expired once its time has come, else as canceled (B24-22); previousOutcome is
      // its stored outcome before, which stays pending until the daily refresh marks it, so the words name neither.
      else say('The old request is closed, and the same people are asked again with new time to answer.', 'The old request would close, and the same people would be asked again with new time to answer.');
      // The new expiry is said once: the engine's summary gives the days ("expires in 2 days"), and the Asked now block
      // gives the local date and time. A third sentence repeated it (wave 4b verify, S-23.11).
      break;
    }
    case 'retry_approval_card': {
      const d = raw as WriteToolData['retry_approval_card'];
      if (d.action === 'sent-again') {
        const tries = d.attemptsBefore ? ` It had used ${plural(d.attemptsBefore, 'attempt', 'attempts')}.` : '';
        say(`The card goes back to the approval runner with no attempts used.${tries}`, `The card would go back to the approval runner with no attempts used.${tries}`);
      } else {
        const why = CARD_SETTLED_WORDS[d.stateAfter] ?? 'settled';
        say(`The card is closed: its request ${why} in the meantime.`, `The card would be closed: its request ${why} in the meantime.`);
      }
      break;
    }
    default:
      break;
  }
  // The data domains and locations an owner leaves (record_owner_leave, run_owner_check).
  if (tool === 'record_owner_leave') {
    const n = (raw as WriteToolData['record_owner_leave']).affectedData?.length ?? 0;
    if (n) facts.push(`${plural(n, 'data domain or location is', 'data domains or locations are')} affected too.`);
  }
  if (tool === 'run_owner_check') {
    const n = ((raw as WriteToolData['run_owner_check']).findings ?? []).reduce((sum, f) => sum + (f.dataSubjects?.length ?? 0), 0);
    if (n) facts.push(`${plural(n, 'data domain or location needs', 'data domains or locations need')} a successor or a check.`);
  }
  if (isObject(data) && data.isAiCreated === true) say('The engine marked this row as created by an AI client.', 'The engine would mark this row as created by an AI client.');
  return { askedNow, askedLater, facts };
}

// The configuration keys an engine sentence names in backticks ("Separation Rule `no-self-approval-tier-3`"), so the
// page can show each one's label instead.
export function quotedKeys(text: string): string[] {
  return [...new Set([...text.matchAll(/`([a-z][a-z0-9-]{0,63}(?:\/[a-z][a-z0-9-]{0,63})?)`/g)].map(m => m[1]))];
}

// An engine sentence in a person's words: each backticked key becomes its configured label in quotes (or the key in
// plain words when the label is unknown), and the internal process codes such as "(OP-07)" are dropped.
export function plainEngineText(text: string, labelOf: (key: string) => string | null): string {
  return text
    .replace(/`([a-z][a-z0-9-]{0,63}(?:\/[a-z][a-z0-9-]{0,63})?)`/g, (_, key: string) => `"${labelOf(key) ?? key.replace(/[-/]+/g, ' ')}"`)
    .replace(/\s*\((?:OP|G|CC|RA|AN)-\d+(?:,\s*(?:OP|G|CC|RA|AN)-\d+)*\)/g, '')
    .replace(/\s{2,}/g, ' ');
}

// What a check says in place of the engine's summary, which describes the write as if it were done.
export const PREVIEW_SUMMARY = 'Every rule ran and passed. Nothing was written: send it to make the change.';

// The warning every check carries ("validateOnly was true"). The page already says nothing was written, in its own words.
const VALIDATE_ONLY_WARNING = 'request.validate-only';

// Reads a write's envelope into what the page shows. Never throws: an unexpected data shape shows the summary only.
// What a proposal sheet hands to "Send it for approval now?" once the proposal is saved (wave 4b, S10-9 and S10-15):
// the new decision and the engine's answer to the save, which the follow-up shows on top. Null when no decision was
// written, so there is nothing to send.
export function proposalHandOff(envelope: Envelope<WriteToolData['save_decision_proposal']>): { decisionId: string; answer: EngineAnswer } | null {
  if (!envelope.ok || !envelope.data?.recordId) return null;
  return { decisionId: envelope.data.recordId, answer: readEngineAnswer('save_decision_proposal', envelope) };
}

export function readEngineAnswer<N extends WriteToolName>(tool: N, envelope: Envelope<WriteToolData[N]>, preview = false): EngineAnswer {
  if (!envelope.ok) {
    const e = envelope.error;
    return {
      kind: 'refused',
      preview,
      code: e.code,
      title: REFUSAL_TITLES[e.code] ?? 'The product refused this',
      message: e.message,
      field: e.field ?? null,
      ruleKey: e.ruleKey ?? null,
      guideline: e.guideline ?? null,
      retryAfterSeconds: e.retryAfterSeconds ?? null,
      claimsChallenge: e.code === 'step_up_required' ? e.claimsChallenge ?? null : null,
      recordUrl: e.recordUrl ?? null,
      outcomeUnknown: !preview && outcomeUnknown(envelope),
      correlationId: envelope.correlationId,
    };
  }
  let read: ReturnType<typeof readResult> = { askedNow: [], askedLater: [], facts: [] };
  let stampedAs: string | null = null;
  try {
    if (isObject(envelope.data)) {
      read = readResult(tool, envelope.data, preview, preview ? '' : humanizeDates(envelope.summary));
      stampedAs = preview ? null : stampOf((envelope.data as { performedBy?: PerformedBy }).performedBy);
    }
  } catch {
    // A data shape the page does not know: show the summary alone.
  }
  return {
    kind: 'ok',
    preview,
    summary: preview ? PREVIEW_SUMMARY : humanizeDates(envelope.summary),
    askedNow: read.askedNow,
    askedLater: read.askedLater,
    facts: read.facts,
    warnings: (envelope.warnings ?? []).filter(w => w.code !== VALIDATE_ONLY_WARNING).map(w => ({ ...w, message: humanizeDates(w.message) })),
    stampedAs,
    recordUrl: envelope.links?.recordUrl ?? null,
    correlationId: envelope.correlationId,
  };
}
