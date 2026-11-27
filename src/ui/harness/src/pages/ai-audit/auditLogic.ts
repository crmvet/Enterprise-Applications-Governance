// The audit trail and evidence pack logic with no React in it: the words for an audit entry, the page's own filters in
// the address, and what the configuration version stamped on a row says next to the version in force. Every fact comes
// from get_audit_trail or get_evidence; these functions only put it into words.
import type { ActorKind, AuditItem, EvidenceApproval, GetAuditTrailInput, IsoDate } from '../../data/contracts';
import type { AuditActorKind } from '../../app/routes';
import type { Tone } from '../../data/meanings';

// ---------- actors ----------

// The fixed engine meanings of the actor kinds (C38), in words. The configured label wins where the configuration
// names one for the meaning (actor-kind rows carry engineMeaning).
export const ACTOR_WORDS: Record<ActorKind, string> = {
  person: 'Person',
  'ai-client-for-person': 'AI client for a person',
  'agent-identity': 'Agent identity',
  pipeline: 'Pipeline',
  'approval-runner': 'Approval runner',
  engine: 'Engine',
};

export function actorKindLabel(kind: ActorKind | string, configured: { key: string; label: string; fields: Record<string, unknown> }[] = []): string {
  const row = configured.find(r => r.fields.engineMeaning === kind);
  return row?.label ?? (ACTOR_WORDS as Record<string, string>)[kind] ?? kind;
}

export function actorTone(kind: ActorKind | string): Tone {
  if (kind === 'person') return 'success';
  if (kind === 'ai-client-for-person' || kind === 'agent-identity') return 'brand';
  return 'subtle';
}

// ---------- operations ----------

export const AUDIT_OPERATIONS = ['composed-call', 'call-refused', 'approval-outcome-written', 'decision-accepted', 'subject-updated'] as const;
export type AuditOperation = (typeof AUDIT_OPERATIONS)[number];

export const OPERATION_TEXT: Record<AuditOperation, string> = {
  'composed-call': 'Calls that wrote',
  'call-refused': 'Refused calls',
  'approval-outcome-written': 'Approval answers',
  'decision-accepted': 'Decisions accepted',
  'subject-updated': 'Effects applied',
};

// The write tools in words: what a call did ("Saved an intake draft") and what a refused call tried ("save an intake
// draft"). A tool not listed here shows its name.
const TOOL_WORDS: Record<string, [string, string]> = {
  save_intake_draft: ['Saved an intake draft', 'save an intake draft'],
  score_intake_request: ['Scored an intake request', 'score an intake request'],
  answer_tier_questions: ['Answered the tier questions', 'answer the tier questions'],
  submit_record: ['Submitted a record', 'submit a record'],
  withdraw_record: ['Withdrew a record', 'withdraw a record'],
  answer_approval: ['Answered an approval request', 'answer an approval request'],
  save_decision_proposal: ['Proposed a decision', 'propose a decision'],
  propose_owner_change: ['Proposed an owner change', 'propose an owner change'],
  record_owner_leave: ['Recorded an owner leave', 'record an owner leave'],
  save_change_request_draft: ['Saved a change draft', 'save a change draft'],
  record_change_result: ['Recorded a change result', 'record a change result'],
  complete_change: ['Completed a change', 'complete a change'],
  complete_review: ['Completed a review', 'complete a review'],
  record_document_link: ['Recorded a document link', 'record a document link'],
  confirm_document_checked: ['Confirmed a document current', 'confirm a document current'],
  claim_resource: ['Claimed an item', 'claim an item'],
  dismiss_claim: ['Dismissed an item', 'dismiss an item'],
  save_link: ['Saved a link', 'save a link'],
  save_deployment_target: ['Saved a deployment target', 'save a deployment target'],
  save_dependency: ['Saved a dependency', 'save a dependency'],
  save_portfolio: ['Saved a portfolio', 'save a portfolio'],
  record_deployment: ['Recorded a deployment', 'record a deployment'],
  record_deployment_correction: ['Corrected a deployment record', 'correct a deployment record'],
  map_pipeline: ['Mapped a pipeline', 'map a pipeline'],
  run_gap_check: ['Ran the gap check', 'run the gap check'],
  compare_inventory_list: ['Compared an inventory list', 'compare an inventory list'],
  run_owner_check: ['Ran the owner check', 'run the owner check'],
  link_component: ['Linked a component', 'link a component'],
  request_architecture_review: ['Asked for an architecture review', 'ask for an architecture review'],
  save_application_draft: ['Saved an application draft', 'save an application draft'],
  propose_configuration_change: ['Proposed a configuration change', 'propose a configuration change'],
  save_data_use: ['Saved a data use', 'save a data use'],
  save_data_location: ['Saved a data location', 'save a data location'],
  save_data_mapping: ['Saved a data mapping', 'save a data mapping'],
  move_data_references: ['Moved data uses and mappings', 'move data uses and mappings'],
  run_due_state_refresh: ['Ran the daily check', 'run the daily check'],
  import_data_taxonomy: ['Imported a list of data domains', 'import a list of data domains'],
  renew_approval_request: ['Asked an approval request again', 'ask an approval request again'],
  retry_approval_card: ['Sent an approval card again', 'send an approval card again'],
  request_security_review: ['Asked for a security review', 'ask for a security review'],
};

// The write tools that have words, for the test that every write tool in tools.json has them.
export const TOOLS_WITH_WORDS: readonly string[] = Object.keys(TOOL_WORDS);

export function toolDone(tool: string): string {
  return TOOL_WORDS[tool]?.[0] ?? `Called ${tool}`;
}

// What an audit entry says happened, in a short line.
export function entryHeadline(item: Pick<AuditItem, 'operation' | 'what' | 'tool' | 'refusal'>): string {
  if (item.operation === 'composed-call' && item.tool) return toolDone(item.tool);
  if (item.operation === 'call-refused') {
    const tool = item.tool ?? 'a tool';
    if (item.refusal && !item.refusal.knownTool) return `Refused a call to ${tool}, a tool the product does not have`;
    const tried = item.tool ? TOOL_WORDS[item.tool]?.[1] : undefined;
    return tried ? `Refused the call to ${tried}` : `Refused a call to ${tool}`;
  }
  return item.what;
}

// The status meaning a call left on its record, in words.
export function statusAfterText(meaning: string | null): string | null {
  if (!meaning) return null;
  return `Left the record ${meaning.replace(/-/g, ' ')}`;
}

// ---------- the page's own filters in the address ----------

const DATE = /^\d{4}-\d{2}-\d{2}$/;

export function isIsoDate(value: string | null | undefined): value is IsoDate {
  if (!value || !DATE.test(value)) return false;
  const d = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === value;
}

export interface AuditFilters {
  recordId?: string;
  principalObjectId?: string;
  applicationId?: string;
  actorKinds?: AuditActorKind[];
  operations?: AuditOperation[];
  // Wave 4: only the entries made through this client app (get_audit_trail clientApp, client= in the route).
  clientApp?: string;
  from?: IsoDate;
  to?: IsoDate;
}

// The operations of the query string (op=call-refused&op=composed-call), known ones only.
export function parseOperations(search: string): AuditOperation[] | undefined {
  const list = [...new Set(new URLSearchParams(search).getAll('op'))].filter((v): v is AuditOperation => (AUDIT_OPERATIONS as readonly string[]).includes(v));
  return list.length ? list : undefined;
}

// The tool input of a filter set, with paging on top.
export function auditInput(f: AuditFilters, top: number): GetAuditTrailInput {
  return {
    ...(f.recordId ? { recordId: f.recordId } : {}),
    ...(f.principalObjectId ? { principalObjectId: f.principalObjectId } : {}),
    ...(f.applicationId ? { businessApplicationId: f.applicationId } : {}),
    ...(f.actorKinds?.length ? { actorKinds: f.actorKinds } : {}),
    ...(f.operations?.length ? { operations: f.operations } : {}),
    ...(f.clientApp ? { clientApp: f.clientApp } : {}),
    ...(f.from ? { from: f.from } : {}),
    ...(f.to ? { to: f.to } : {}),
    top,
  };
}

// The scope a figure counts in: the record, the person, the application, the client and the dates, without the actor
// and operation filters the figure sets itself.
export function scopeOnly(f: AuditFilters): AuditFilters {
  return { recordId: f.recordId, principalObjectId: f.principalObjectId, applicationId: f.applicationId, clientApp: f.clientApp, from: f.from, to: f.to };
}

// True when the dates are the wrong way round.
export function datesReversed(from: string | undefined, to: string | undefined): boolean {
  return !!from && !!to && from > to;
}

// ---------- the evidence pack ----------

// What the configuration version stamped on a row says next to the version in force.
export function versionNote(stamped: number | null, inForce: number): { text: string; older: boolean } {
  if (stamped === null) return { text: 'No version stamp (seeded data)', older: false };
  if (stamped === inForce) return { text: `Checked under version ${stamped}, in force now`, older: false };
  return { text: `Checked under version ${stamped}; version ${inForce} is in force now`, older: stamped < inForce };
}

// The versions the pack's rows were checked under, in words.
export function versionsText(stamped: number[], inForce: number): string {
  const list = [...new Set(stamped)].sort((a, b) => a - b);
  if (!list.length) return `No row in the pack carries a version stamp. Version ${inForce} is in force now.`;
  const older = list.filter(v => v < inForce);
  const named = list.length === 1 ? `version ${list[0]}` : `versions ${list.slice(0, -1).join(', ')} and ${list[list.length - 1]}`;
  return older.length
    ? `Rows were checked under ${named}. Version ${inForce} is in force now, so ${older.length === 1 ? 'one older version applies' : `${older.length} older versions apply`} to some rows.`
    : `Every stamped row was checked under ${named}, the version in force now.`;
}

// Whether an approval answer counted, and why, in words.
export function approvalCountText(a: Pick<EvidenceApproval, 'isConsultation' | 'approverActorKind' | 'outcome' | 'countsTowardRequirement'>): { text: string; tone: Tone } {
  if (a.outcome.meaning === 'pending') return { text: 'Not answered yet', tone: 'warning' };
  if (a.isConsultation) return { text: 'Advice only: a consultation never counts', tone: 'informative' };
  if (a.approverActorKind === 'ai-client-for-person' || a.approverActorKind === 'agent-identity' || a.outcome.meaning === 'recommendation-only') {
    return { text: 'A recommendation only: an AI answer never counts', tone: 'informative' };
  }
  if (a.countsTowardRequirement) return { text: 'Counted toward the approval', tone: 'success' };
  if (a.outcome.meaning === 'expired' || a.outcome.meaning === 'cancelled') return { text: 'Closed without an answer', tone: 'subtle' };
  return { text: 'Not counted', tone: 'subtle' };
}

// The section anchors of the pack, for the figures that move to them.
export const PACK_SECTIONS = ['decisions', 'changes', 'deployments', 'reviews', 'documents', 'evidence-links'] as const;
export type PackSection = (typeof PACK_SECTIONS)[number];
