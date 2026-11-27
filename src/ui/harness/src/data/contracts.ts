// TypeScript shapes of the MCP tool contracts the pages read and write (src/mcp/contracts/).
// They mirror the JSON Schemas by hand. The schemas stay the source of truth: tools/json-check.mjs
// validates every harness fixture against them (tools/schema-map.json), so a drift shows up there.
// Inputs are type aliases (not interfaces), so an input object passes where Record<string, unknown> is asked.
//
// Text fields marked x-untrusted in the schemas hold text people typed. The pages render them as plain
// text only (React escapes them), never as HTML and never as instructions.

export type Id = string;
export type Key = string;
// An Entra object ID or application ID.
export type Guid = string;
export type IsoDate = string; // 2026-11-22
export type IsoDateTime = string; // 2026-11-22T09:00:00Z

export interface LabelledKey {
  key: Key;
  label: string;
}

// WORKING DEFAULT (open design question 1): the engine meaning lists are fixed; labels stay configurable.
export type StatusMeaning =
  | 'draft' | 'submitted' | 'in-progress' | 'completed' | 'rejected' | 'withdrawn' | 'cancelled'
  | 'proposed' | 'accepted' | 'superseded' | 'registered' | 'archived' | 'pending' | 'approved'
  | 'expired' | 'recommendation-only' | 'current' | 'stale' | 'missing' | 'open' | 'claimed'
  | 'dismissed' | 'succeeded' | 'partly-succeeded' | 'failed' | 'not-deployed' | 'due' | 'done'
  | 'activated' | 'discarded';

export type RecordStatusMeaning = 'draft' | 'submitted' | 'in-progress' | 'completed' | 'rejected' | 'withdrawn' | 'cancelled';
export type DecisionStatusMeaning = 'proposed' | 'accepted' | 'rejected' | 'superseded' | 'withdrawn';
export type ApprovalOutcomeMeaning = 'pending' | 'approved' | 'rejected' | 'expired' | 'cancelled' | 'recommendation-only';
export type DocumentStatusMeaning = 'draft' | 'current' | 'stale' | 'missing';
export type ClaimStatusMeaning = 'open' | 'claimed' | 'dismissed';
export type LifecycleMeaning = 'pre-live' | 'live' | 'retired';
export type DirectoryState = 'enabled' | 'disabled' | 'deleted' | 'unknown';
export type DeploymentRouteMeaning = 'pipeline-step' | 'ai-tool' | 'person-in-app' | 'other';
export type GateLevel = 'off' | 'record' | 'flag' | 'advise' | 'enforce';
export type PrincipalKind = 'user' | 'group' | 'agent-identity' | 'agent-user' | 'service-principal' | 'managed-identity';
// Who acted, from the entry point and the token, never from a caller field (C38).
export type ActorKind = 'person' | 'ai-client-for-person' | 'agent-identity' | 'pipeline' | 'approval-runner' | 'engine';
export type ApprovalMode = 'everyone-must-approve' | 'first-to-respond' | 'sequential';
// The kind of record a Decision (O09) is about.
export type DecisionSubjectKind =
  | 'business-application' | 'intake-request' | 'change-request' | 'deployment-record' | 'technical-component'
  | 'claim' | 'review' | 'configuration-draft'
  // Data taxonomy (D-37): a use, a copy mapping, a domain or a location. A data-domain subject's subjectId is the domain
  // key, not a row ID.
  | 'data-use' | 'data-mapping' | 'data-domain' | 'data-location';
// The data subjects of a decision (get_decision data, list_approvals data).
export type DataSubjectKind = 'data-use' | 'data-mapping' | 'data-domain' | 'data-location';
export type LinkCategory = 'delivery' | 'evidence';

export interface StatusValue {
  label: string;
  meaning: StatusMeaning;
}

export interface PrincipalRef {
  objectId: string;
  displayLabel: string;
  principalKind?: PrincipalKind;
}

export interface Bucket {
  key: Key | null;
  label: string;
  count: number;
}

export interface ClientApp {
  appId: Guid;
  label: string;
}

// Who made a row's last engine-checked write, as a read returns it (common.schema.json stampedBy). Null on a row the
// engine never stamped, such as seeded data.
export interface StampedBy {
  actorKind: ActorKind;
  principal: PrincipalRef;
  // The person an AI client or pipeline acted for. Null when a person acted directly.
  onBehalfOf: PrincipalRef | null;
  // Such as "Claude Code", "Web app" or "Web app, demo operator".
  clientAppLabel: string | null;
  stampedOn: IsoDateTime | null;
  // The AI client that created the row and the person it acted for (document 22, B22-15), kept after a person edits the
  // row. Null for a row no AI client created.
  draftedBy?: { clientAppLabel: string | null; forPrincipal: PrincipalRef | null } | null;
}

// One approval a decision needs, from the single most specific Approval Rule group. Exactly one of approverRoleKey,
// approverOwnerRoleKey and approverProposedOwners is set. approverProposedOwners asks each person the decision proposes
// to make an owner, by name.
export interface ApprovalNeeded {
  decisionTypeKey?: Key;
  approverRoleKey?: Key;
  approverOwnerRoleKey?: Key;
  approverProposedOwners?: true;
  approvalMode: ApprovalMode;
  minimumCount: number;
  // Where the owner role is held when it is not the application's (C32 Owner Subject).
  ownerSubject?: 'data-domain' | 'parent-data-domain' | 'data-location' | 'touched-data-domains';
  // A consulted role: asked once submitted, its answer is advice.
  isConsultation?: true;
}

export interface ApprovalsSummary {
  required: number;
  approved: number;
  rejected: number;
  pending: number;
}

// One link to proof held elsewhere; it becomes an Evidence Link (O20).
export type EvidenceInput = {
  linkKindKey: Key;
  url: string;
  label: string;
};

// A delivery or evidence link on a record, as the record reads return it.
export interface RecordLink {
  linkId: Id;
  linkCategory: LinkCategory;
  linkKind: LabelledKey;
  url: string;
  label: string;
}

// ---- envelope (envelope.schema.json) ----

export type ErrorCode =
  | 'not_signed_in' | 'not_authorized' | 'step_up_required' | 'ai_cannot_do_this' | 'rule_violation' | 'validation'
  | 'not_found' | 'conflict' | 'locked' | 'rate_limited' | 'upstream_unavailable' | 'configuration_unavailable';

export interface ToolError {
  code: ErrorCode;
  message: string;
  field?: string;
  ruleKey?: Key;
  guideline?: string;
  retryAfterSeconds?: number;
  // step_up_required: the claims challenge to sign in again with (base64 JSON), when a fresh sign-in alone is not enough.
  claimsChallenge?: string;
  // The record the refusal points to, such as the document link to update instead (wave 4b).
  recordUrl?: string;
}

export interface Warning {
  message: string;
  code?: string;
  ref?: string;
}

export interface CouldNotReadItem {
  kind: Key;
  count: number;
}

export interface Success<T> {
  ok: true;
  summary: string;
  data: T;
  links?: { recordUrl?: string };
  asOf: IsoDateTime;
  configurationSetVersion: number;
  couldNotRead: CouldNotReadItem[];
  warnings: Warning[];
  untrustedFields: string[];
  nextSteps: { tool: string; inputs: Record<string, unknown> }[];
  correlationId: string;
}

export interface Failure {
  ok: false;
  summary?: string;
  error: ToolError;
  asOf?: IsoDateTime;
  configurationSetVersion?: number;
  correlationId: string;
}

export type Envelope<T> = Success<T> | Failure;

// ---- the fields every write adds to data (envelope.schema.json writeResult) ----

// Who acted, exactly as the engine stamped the write.
export interface PerformedBy {
  actorKind: ActorKind;
  principal: PrincipalRef;
  onBehalfOf: PrincipalRef | null;
  clientApp: ClientApp | null;
}

export interface EngineStamp {
  engineVersion: string;
  engineCheckedOn: IsoDateTime;
}

export interface WriteResult {
  // The main row written. Null only when validateOnly was true and the call would create a new row.
  recordId: Id | null;
  // Null when the main row has no status, such as a Tier Assessment.
  status: StatusValue | null;
  performedBy: PerformedBy;
  isAiCreated: boolean;
  engineStamp: EngineStamp;
  // True when a new row was written. False for an update, a matched retry, or validateOnly.
  created: boolean;
}

// A status value whose meaning a tool narrows to a few values.
export interface StatusOf<M extends StatusMeaning> {
  label: string;
  meaning: M;
}

// Every write tool takes these two. The action sheet sets them; a page never needs to.
export type WriteControls = {
  // The caller's own ID for this request: a retry with the same value returns the first result and writes nothing new.
  clientRequestId?: string;
  // True runs every rule and returns what would be written, without writing.
  validateOnly?: boolean;
};

// ---- get_portfolio_summary ----

export interface PortfolioSummary {
  buckets: Bucket[];
  // The one owner gap count (document 24, B24-17): owner roles on registered applications with no holder, no deputy, or a
  // holder whose account is off. Owners and Portfolios show the same number for the same scope.
  ownerGaps: number;
  applicationsWithOwnerGap?: number;
  reviewsOverdue: number;
  documentsMissingOrStale: number;
  draftsOlderThanDays: number;
  ownerGapList?: {
    businessApplicationId: Id;
    name: string;
    portfolio: { portfolioId: Id; name: string } | null;
    ownerRoleKey: Key;
    missing: 'holder' | 'deputy';
    holderDirectoryState: DirectoryState | null;
  }[];
  skipToken?: string;
  // The data findings (DC-01 to DC-14) in the scope: the total, the high ones, and a count per check (docs/architecture/data-taxonomy.md,
  // MCP tools and REST).
  dataGaps?: { total: number; high: number; counts: Partial<Record<DataCheckCode, number>> };
}

// ---- get_deployments ----

export interface DeploymentFlags {
  outsideWindow: boolean;
  otherPath: boolean;
  missingApproval: boolean;
  aiOnlyCodeApproval: boolean;
}

export interface DeploymentItem {
  deploymentRecordId: Id;
  businessApplicationId: Id | null;
  applicationName: string | null;
  deploymentTargetId: Id | null;
  targetLabel: string | null;
  targetStageKey: Key | null;
  artifactName: string;
  artifactVersion: string;
  result: StatusValue;
  finishedOn: IsoDateTime;
  route: { key: Key; meaning: DeploymentRouteMeaning };
  flags: DeploymentFlags;
  gateLevelApplied: GateLevel;
  runUrl: string | null;
  correctsRecordId: Id | null;
  // The list's own fields (document 24, B24-11).
  changeRequestId: Id | null;
  pipelineReferenceId: Id | null;
  versionBefore: string | null;
  versionAfter: string | null;
  startedByAiClient: string | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  configurationSetVersion: number | null;
  recordUrl: string;
}

export type DeploymentFlagKey = 'outside-window' | 'other-path' | 'missing-approval' | 'ai-only-code-approval';

export interface Deployments {
  items: DeploymentItem[];
  // Counts for the same filters, before the flag filters and paging.
  counts: { records: number; flagged: number; outsideWindow: number; otherPath: number; missingApproval: number; aiOnlyCodeApproval: number };
  totalCount: number;
  skipToken?: string;
}

// ---- get_change_schedule ----

export interface ScheduledChange {
  changeRequestId: Id;
  title: string;
  businessApplicationId: Id;
  applicationName: string;
  target: { deploymentTargetId: Id; label: string; targetStageKey: Key | null } | null;
  windowStart: IsoDateTime | null;
  windowEnd: IsoDateTime | null;
  changeType: LabelledKey;
  status: StatusValue;
  authorized: boolean;
  // The authorization decision's own state: proposed, accepted, rejected or withdrawn. Null with no decision yet.
  authorizationStatus: StatusValue | null;
}

export interface ChangeSchedule {
  items: ScheduledChange[];
  skipToken?: string;
}

// ---- get_application_card ----

export interface OwnerAssignment {
  ownerAssignmentId: Id;
  ownerRoleKey: Key;
  label: string;
  principal: PrincipalRef;
  isDeputy: boolean;
  deputyFor: Id | null;
  startOn: IsoDate | null;
  endOn: IsoDate | null;
  ownershipDecisionId: Id | null;
}

export interface ApplicationCard {
  // The data section (sections: ['data']): uses with state, derived data owners and findings (data taxonomy, 10.3).
  data?: ApplicationCardData;
  application: {
    businessApplicationId: Id;
    name: string;
    purpose: string;
    status: StatusValue;
    kind: LabelledKey;
    tier: { key: Key; label: string; decisionId: Id | null } | null;
    stage: { key: Key; label: string; since: IsoDateTime | null; decisionId: Id | null } | null;
    buildPath: LabelledKey | null;
    usesAi: boolean | null;
    agreedAutonomyLevel: LabelledKey | null;
    nextReviewOn: IsoDate | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
  };
  owners?: OwnerAssignment[];
  components?: {
    technicalComponentId: Id;
    kind: LabelledKey;
    externalId: string;
    // Null when the product holds no link for it (an item claimed from a list that gave none).
    url: string | null;
    labelAsSeen: string | null;
    lastSeenOn: IsoDateTime | null;
    // True when the last gap check no longer saw it (an open vanished claim names it).
    vanishedInGapCheck?: boolean;
  }[];
  dependencies?: {
    dependsOnCount: number;
    usedByCount: number;
    top: {
      direction: 'depends-on' | 'used-by';
      businessApplicationId: Id | null;
      technicalComponentId: Id | null;
      name: string;
      dependencyType: LabelledKey;
      criticality: LabelledKey | null;
    }[];
  };
  decisions?: {
    decisionId: Id;
    decisionType: LabelledKey;
    statement: string;
    outcome: LabelledKey | null;
    status: StatusValue;
    decidedOn: IsoDateTime | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
  }[];
  changes?: {
    changeRequestId: Id;
    title: string;
    changeType: LabelledKey;
    status: StatusValue;
    windowStart: IsoDateTime | null;
    windowEnd: IsoDateTime | null;
  }[];
  deployments?: {
    deploymentRecordId: Id;
    deploymentTargetId: Id;
    targetLabel: string;
    targetStageKey: Key;
    artifactName: string;
    artifactVersion: string;
    result: StatusValue;
    finishedOn: IsoDateTime | null;
  }[];
  documents?: {
    documentType: LabelledKey;
    status: StatusValue;
    documentLinkId: Id | null;
    url: string | null;
    // The link kind the link was recorded with; null when the document is missing.
    linkKindKey: Key | null;
    lastCheckedOn: IsoDate | null;
    staleOn: IsoDate | null;
  }[];
  reviews?: {
    reviewId: Id;
    reviewType: LabelledKey;
    status: StatusValue;
    dueOn: IsoDate | null;
    doneOn: IsoDate | null;
  }[];
  links?: {
    deliveryLinkId: Id;
    linkKind: LabelledKey;
    url: string;
    label: string;
    lastSeenOn: IsoDateTime | null;
  }[];
}

// ---- get_application_owners ----

export interface ApplicationOwners {
  owners: OwnerAssignment[];
  gaps: { ownerRoleKey: Key; missing: 'holder' | 'deputy' }[];
  microsoftOwnerDiffers: boolean | null;
  ownerCheckedOn: IsoDateTime | null;
  // The data owners the application's data uses lead to, each with where the owner is set (data taxonomy, 5.7).
  dataOwners?: DerivedDataOwner[];
}

// ---- get_dependency_impact ----

export interface ImpactNode {
  businessApplicationId: Id;
  name: string;
  tier: LabelledKey | null;
  lifecycleMeaning: LifecycleMeaning | null;
  businessOwner: PrincipalRef | null;
  technicalOwner: PrincipalRef | null;
  // The counted dependency type of the last hop. Null for a node that joined a component or data location start's first
  // ring (document 24, B24-22): see via. An application start gives every node a type.
  dependencyTypeKey: Key | null;
  // How the node joined the walk: a dependency, or in the first ring of a component or data location start, the app built
  // from the component, one using it as its AI component, one with a data use at the location, or the app the location is.
  via?: 'dependency' | 'part-of' | 'ai-component' | 'data-use' | 'hosts-location';
  criticalityKey: Key | null;
  depth: number;
  path: Id[];
}

export interface DependencyImpact {
  root: { businessApplicationId: Id | null; technicalComponentId: Id | null; dataLocationId?: Id | null; kind?: 'application' | 'component' | 'data-location'; name: string };
  nodes: ImpactNode[];
  // For a data location start: what the location holds.
  dataLocation?: {
    label: string;
    platform: LabelledKey | null;
    hostedBy: { businessApplicationId: Id; name: string } | null;
    childLocationIds: Id[];
    domainsHeld: Array<{ key: Key; path: string; origins: DataOrigin[] }>;
    dataUseCount: number;
  } | null;
  counts: { byTier: Bucket[]; byCriticality: Bucket[] };
  truncatedAtDepth: boolean;
}

// ---- get_configuration ----

export interface ConfigurationRows {
  configurationSet: { key: Key; version: number; publishedOn: IsoDateTime; contentHash: string };
  rows: {
    entity: Key;
    key: string;
    label: string;
    isActive: boolean;
    origin: 'shipped' | 'local';
    fields: Record<string, unknown>;
  }[];
  skipToken?: string;
}

// ---- find_applications (the web app's drill-down list) ----

export interface ApplicationSummary {
  businessApplicationId: Id;
  name: string;
  purpose: string;
  kind: LabelledKey;
  tier: LabelledKey | null;
  stage: LabelledKey | null;
  businessOwner: PrincipalRef | null;
  technicalOwner: PrincipalRef | null;
  recordUrl: string;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
}

export interface FoundApplications {
  items: ApplicationSummary[];
  totalCount: number;
  skipToken?: string;
}

// =====================================================================================================================
// The work pages (D-36): reads
// =====================================================================================================================

// ---- get_my_queue ----

// data-gap: a data finding on a domain the person owns, deputizes or stewards (docs/architecture/data-taxonomy.md, MCP tools and REST).
export type QueueItemKind = 'approval' | 'review' | 'document' | 'claim' | 'draft' | 'owner-gap' | 'data-gap';

export type GetMyQueueInput = {
  kinds?: QueueItemKind[];
  top?: number;
  skipToken?: string;
};

export interface QueueItem {
  kind: QueueItemKind;
  subjectKind: Key;
  // A row ID, or a domain key when subjectKind is data-domain.
  subjectId: Id;
  title: string;
  businessApplicationId: Id | null;
  dueOn: IsoDate | null;
  status: StatusValue | null;
  why: string;
  recordUrl: string;
  // answer_approval for a request that asks the person in the web app; null for an AI client.
  nextTool: string | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
}

export interface MyQueue {
  items: QueueItem[];
  skipToken?: string;
}

// ---- get_decision ----

export type GetDecisionInput = {
  decisionId: Id;
};

// The subject of a decision, with its label and application.
export interface DecisionSubject {
  subjectKind: DecisionSubjectKind;
  // A row ID, or the domain key for a data-domain subject.
  subjectId: Id;
  label: string;
  businessApplicationId: Id | null;
  applicationName: string | null;
}

// One requirement of the approval rules. Exactly one of requiredRoleKey, requiredOwnerRoleKey and asksProposedOwners
// is set. asksProposedOwners asks each person the decision proposes to make an owner, by name.
export interface ApprovalRequirement {
  approvalRuleKey: string;
  requiredRoleKey?: Key;
  requiredOwnerRoleKey?: Key;
  asksProposedOwners?: boolean;
  approvalMode: ApprovalMode;
  minimumCount: number;
  received: number;
  isConsultation: boolean;
}

export interface ApprovalStillMissing {
  approvalRuleKey: string;
  requiredRoleKey?: Key;
  requiredOwnerRoleKey?: Key;
  asksProposedOwners?: boolean;
  stillNeeded: number;
}

// One approval record as get_approval_status returns it.
export interface ApprovalRecordRow {
  approvalRecordId: Id;
  approvalRuleKey: string;
  approver: PrincipalRef | null;
  approverActorKind: ActorKind | null;
  approverPrincipalKind: PrincipalKind | null;
  outcome: StatusValue;
  typedReason: string | null;
  respondedOn: IsoDateTime | null;
  countsTowardRequirement: boolean;
  refusedBySeparationRuleKey: string | null;
}

// Why the engine would not take the caller's answer on a request that asks them (the approval answer check, run as a
// pre-check). The page hides Approve and Reject and shows this instead.
export interface CannotAnswer {
  code: string;
  message: string;
  separationRule: LabelledKey | null;
}

// get_decision adds who each record asks, and when, and whether the caller may answer it now.
export interface DecisionApprovalRecord extends ApprovalRecordRow {
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  askedPrincipal: PrincipalRef | null;
  isConsultation: boolean;
  requestedOn: IsoDateTime | null;
  expiresOn: IsoDateTime | null;
  canAnswer?: boolean;
  cannotAnswer?: CannotAnswer | null;
}

// What acceptance writes on the subject (Proposed Changes Json).
export interface ProposedChanges {
  owners: {
    ownerRole: LabelledKey | null;
    add: PrincipalRef | null;
    isDeputy: boolean;
    startOn: IsoDate | null;
    endAssignmentId: Id | null;
    ends: PrincipalRef | null;
    // True when the assignment that ends is a deputy's.
    endsIsDeputy?: boolean;
  }[];
  tier: LabelledKey | null;
  autonomyLevel: LabelledKey | null;
}

export interface DecisionDetail {
  decision: {
    decisionId: Id;
    recordNumber: string | null;
    decisionType: LabelledKey;
    statement: string;
    context: string | null;
    optionsConsidered: string | null;
    confidence: LabelledKey | null;
    outcome: LabelledKey | null;
    reason: string | null;
    status: StatusValue;
    decider: PrincipalRef | null;
    decidedOn: IsoDateTime | null;
    supersedes: Id | null;
    supersededBy: Id | null;
    amends: Id | null;
    amendedBy: Id[];
    configurationSetVersion: number;
    proposedBy: PrincipalRef | null;
    // An exception decision's standard and review date; null for other decisions.
    exception?: { standard: string | null; reviewOn: IsoDate | null } | null;
    // The slide-back signal this decision acts on (B24-22); null for a decision about no signal.
    slideBackSignal?: { number: number; key: SlideBackSignalKey; label: string } | null;
    proposedChanges?: ProposedChanges | null;
    // For a configuration change or gate policy change decision: what the draft changes (product review 2026-11-24).
    configurationChange?: {
      configurationDraftId: Id;
      changeSummary: string[];
      baseVersionNumber: number | null;
      isValid: boolean | null;
      problems: string[];
    } | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
    recordUrl: string;
  };
  subject: DecisionSubject & { recordUrl: string };
  approvals: {
    decisionId: Id;
    // True for a proposed decision: the engine evaluated the rules now. False for a settled one: the stored records.
    evaluated: boolean;
    required: ApprovalRequirement[];
    records: DecisionApprovalRecord[];
    missing: ApprovalStillMissing[];
    ready: boolean;
    settledBy?: SettledBy | null;
  };
  evidence: {
    evidenceLinkId: Id;
    linkKind: LabelledKey;
    url: string;
    label: string;
    evidenceLevel: LabelledKey | null;
    capturedOn: IsoDateTime | null;
  }[];
  // The gate whose gate decision type is this decision's type, for a proposed decision (OP-07, G-14): the engine
  // accepts the decision only once the gate is ready. Null for a settled decision or a type no gate takes.
  gate?: DecisionGate | null;
  // What a data decision page shows (docs/architecture/data-taxonomy.md, Screens): the domain path, the governing rule and where it is
  // set, the classification, the AI and de-identified flags, who is asked, "1 of 2 approvals" and the version. Null for
  // a decision whose subject is not data.
  data?: DecisionDataFacts | null;
}

export interface DecisionGate {
  gateKey: Key;
  label: string;
  ready: boolean;
  unmet: {
    requirementKey: Key;
    label: string;
    requirementKind: string | null;
    requiredOwnerRoleKey: Key | null;
    guideline: string | null;
    // The guideline as a chip: number, title and link (document 24, B24-18).
    guidelineRef?: GuidelineRef | null;
    // For a data-uses-decided requirement: the open uses not decided yet, and the decision that would decide each.
    openUses?: { dataUseId: Id; recordNumber: string | null; label: string; state: string; decisionId: Id | null; recordUrl: string }[];
  }[];
}

export interface DecisionDataFacts {
  subjectKind: DataSubjectKind;
  domain: DataDomainRef | null;
  location: DataLocationBrief | null;
  use: { dataUseId: Id; recordNumber: string | null; access: DataAccess; purpose: string; aiServiceAsSeen: string | null; status: StatusValue } | null;
  mapping: { dataMappingId: Id; origin: DataOrigin; containers: string[] } | null;
  classification: DataClassificationValue | null;
  governingRule: DataGoverningRule | null;
  reachesAi: boolean | null;
  isDeidentified: boolean | null;
  state: DataUseState | null;
  approvalsNeeded: number;
  approvalsGiven: number | null;
  asked: DataApprover[];
  configurationSetVersion: number;
}

// ---- list_approvals ----

export type ApprovalListView = 'mine' | 'asked-by-me' | 'open' | 'answered';

export type ListApprovalsInput = {
  view?: ApprovalListView;
  decisionTypeKeys?: Key[];
  businessApplicationId?: Id;
  decisionId?: Id;
  // Wave 4b review: the requests about the applications of one portfolio, child portfolios included.
  portfolioId?: Id;
  top?: number;
  skipToken?: string;
};

export interface ApprovalItem {
  approvalRecordId: Id;
  decision: {
    decisionId: Id;
    decisionType: LabelledKey;
    statement: string;
    status: StatusValue;
    proposedBy: PrincipalRef | null;
    isAiCreated: boolean;
  };
  subject: DecisionSubject;
  approvalRuleKey: string;
  isConsultation: boolean;
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  askedPrincipal: PrincipalRef | null;
  outcome: StatusValue;
  approver: PrincipalRef | null;
  typedReason: string | null;
  countsTowardRequirement: boolean;
  requestedOn: IsoDateTime | null;
  respondedOn: IsoDateTime | null;
  expiresOn: IsoDateTime | null;
  otherPendingCount: number;
  isOwnProposal: boolean;
  recordUrl: string;
  // True when the request asks the caller and the engine would take their answer now.
  canAnswer?: boolean;
  // Set when the request asks the caller but the check refuses, with the separation rule that refuses.
  cannotAnswer?: CannotAnswer | null;
  // answer_approval only when the person may answer it now, in the web app.
  nextTool: string | null;
  // The data facts of a data decision, for the row: domain, classification, rule, AI and de-identified flags. Null for
  // other decisions.
  data?: {
    subjectKind: DataSubjectKind;
    domain: DataDomainRef | null;
    classification: LabelledKey | null;
    rule: DataHandlingRule | null;
    reachesAi: boolean | null;
    isDeidentified: boolean | null;
  } | null;
}

export interface ApprovalList {
  items: ApprovalItem[];
  totalCount: number;
  // Wave 4b verify round: in view mine, how many of the matches the person may answer now (the rest ask them, but a separation
  // rule keeps them from answering). Null outside mine. Optional: a server before wave 4b leaves it out.
  answerableCount?: number | null;
  skipToken?: string;
}

// ---- get_approval_status ----

export type GetApprovalStatusInput = {
  subjectKind: 'decision' | 'change-request' | 'intake-request';
  subjectId: Id;
};

// How a settled decision was settled: answers (approval records), engine (the locked list, no card), person (withdrawn by
// its proposer or decider), or recorded (no approval record and no engine stamp: recorded as settled outside the engine,
// as the seeded demo estate is). Null while proposed.
export type SettledBy = 'answers' | 'engine' | 'person' | 'recorded';

export interface ApprovalStatus {
  decisionId: Id | null;
  required: ApprovalRequirement[];
  records: ApprovalRecordRow[];
  missing: ApprovalStillMissing[];
  ready: boolean;
  // Absent only on a record with no decision yet.
  settledBy?: SettledBy | null;
}

// ---- get_decision_log ----

export type GetDecisionLogInput = {
  subjectKind?: DecisionSubjectKind;
  subjectId?: Id;
  // Decisions about the domain's uses and mappings and about the domain itself (docs/architecture/data-taxonomy.md, MCP tools and REST).
  dataDomainKey?: Key;
  businessApplicationId?: Id;
  // Decisions about applications in this portfolio, child portfolios included (B24-11).
  portfolioId?: Id;
  decisionTypeKeys?: Key[];
  statusMeanings?: DecisionStatusMeaning[];
  from?: IsoDate;
  to?: IsoDate;
  top?: number;
  skipToken?: string;
};

export interface DecisionLogItem {
  decisionId: Id;
  decisionType: LabelledKey;
  subjectKind: DecisionSubjectKind;
  // A row ID, or the domain key for a data-domain subject.
  subjectId: Id;
  // What the decision is about, as people know it (B24-11).
  subjectLabel: string;
  statement: string;
  reason: string;
  // The exceptions register: the standard an exception departs from and its review date. Null for other decisions.
  exception: { standard: string | null; reviewOn: IsoDate | null } | null;
  outcome: LabelledKey | null;
  status: StatusValue;
  decider: PrincipalRef | null;
  decidedOn: IsoDateTime | null;
  supersedes: Id | null;
  supersededBy: Id | null;
  amends: Id | null;
  approvalsSummary: ApprovalsSummary;
  configurationSetVersion: number;
  proposedBy: PrincipalRef | null;
  // For an ownership decision: each proposed change by owner role key and deputy flag. Empty for other decisions.
  ownerChanges?: { ownerRoleKey: Key; isDeputy: boolean; change: 'add' | 'end' }[];
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

export interface DecisionLog {
  items: DecisionLogItem[];
  skipToken?: string;
}

// ---- list_intake_requests ----

export type IntakeListView = 'triage' | 'mine' | 'drafts' | 'demand' | 'all';

export type IntakeOrderBy = 'score' | 'priority' | 'size-band' | 'horizon' | 'recent' | 'title';

export type ListIntakeRequestsInput = {
  view?: IntakeListView;
  statusMeanings?: RecordStatusMeaning[];
  text?: string;
  // "none" matches a request with no value.
  sizeBandKeys?: Key[];
  horizonKeys?: Key[];
  // An accepted priority outcome key, "proposed" or "none".
  priorityKeys?: string[];
  scored?: boolean;
  // Requests in this portfolio or one below it: the request's own portfolio, else its linked application's (B24-17).
  portfolioId?: Id;
  orderBy?: IntakeOrderBy;
  top?: number;
  skipToken?: string;
};

// The latest priority decision on a request that is not withdrawn.
export interface IntakePriority {
  decisionId: Id;
  outcome: LabelledKey | null;
  status: StatusValue;
  recordUrl: string;
}

export interface IntakeDecisionSummary {
  decisionId: Id;
  decisionType: LabelledKey;
  status: StatusValue;
  outcome: LabelledKey | null;
  approvalsSummary: ApprovalsSummary;
}

export interface IntakeSummary {
  intakeRequestId: Id;
  recordNumber: string | null;
  title: string;
  status: StatusValue;
  requester: PrincipalRef | null;
  proposedKind: LabelledKey | null;
  businessApplicationId: Id | null;
  applicationName: string | null;
  duplicateOfId: Id | null;
  usesAi: boolean | null;
  horizon: LabelledKey | null;
  sizeBand: LabelledKey | null;
  score: number | null;
  priority?: IntakePriority | null;
  // The business owner the requester named.
  businessOwner?: PrincipalRef | null;
  // The request's portfolio, else its linked application's (fromApplication true).
  portfolio?: { portfolioId: Id; name: string; fromApplication: boolean } | null;
  // From the latest tier assessment. A suggestion: only an accepted tier decision sets the tier.
  suggestedTier: LabelledKey | null;
  triageDecision: IntakeDecisionSummary | null;
  // The latest 5 decisions about the request.
  decisions: IntakeDecisionSummary[];
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

export interface IntakeList {
  items: IntakeSummary[];
  totalCount: number;
  skipToken?: string;
}

// ---- get_intake_request ----

export type GetIntakeRequestInput = {
  intakeRequestId: Id;
};

export interface TierAssessmentRow {
  tierAssessmentId: Id;
  computedTier: LabelledKey | null;
  assessedBy: PrincipalRef | null;
  tierDecisionId: Id | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
}

export interface IntakeDecisionRow {
  decisionId: Id;
  decisionType: LabelledKey;
  statement: string;
  outcome: LabelledKey | null;
  status: StatusValue;
  decider: PrincipalRef | null;
  decidedOn: IsoDateTime | null;
  approvalsSummary: ApprovalsSummary;
  isTriage: boolean;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

export interface IntakeDetail {
  intake: IntakeSummary & {
    need: string;
    expectedBenefit: string | null;
    duplicateOfName: string | null;
  };
  tierAssessments: TierAssessmentRow[];
  decisions: IntakeDecisionRow[];
  links: RecordLink[];
}

// ---- get_tier_questions ----

export type GetTierQuestionsInput = {
  applicationKindKey?: Key;
  // Wave 4 (B24-22): the application the answers are for, or an intake linked to one. The answer its declared data
  // implies comes back as impliedAnswer, a suggestion before anyone answers.
  businessApplicationId?: Id;
  intakeRequestId?: Id;
};

export interface TierQuestion {
  questionKey: Key;
  text: string;
  helpText: string | null;
  answers: { answerKey: Key; text: string; impliedTier: LabelledKey }[];
}

export interface TierQuestions {
  questions: TierQuestion[];
  // The answer the application's declared data implies, as a suggestion before the person answers (B24-22). Null with no
  // application or no data that implies one.
  impliedAnswer?: ImpliedAnswer | null;
}

// ---- check_duplicates ----

export type CheckDuplicatesInput = {
  text: string;
  proposedKindKey?: Key;
  componentExternalIds?: string[];
};

export interface DuplicateMatches {
  matches: {
    businessApplicationId: Id;
    name: string;
    reason: 'name' | 'purpose' | 'component' | 'owner';
    score: number;
  }[];
}

// ---- get_requirements ----

export type GetRequirementsInput = {
  businessApplicationId?: Id;
  scope?: {
    tierKey: Key;
    applicationKindKey?: Key;
    lifecycleStageKey?: Key;
    changeTypeKey?: Key;
    portfolioId?: Id;
  };
};

// The requirement kinds a gate can hold. The two data kinds (docs/architecture/data-taxonomy.md, Gate requirements) are judged by
// data-taxonomy-mode: data-uses-declared needs the uses declared (or Holds No Business Data), data-uses-decided needs
// every open use not needed or allowed. data-uses-decided names the decision type (data-use).
export type RequirementKind = 'decision-accepted' | 'document-current' | 'review-done' | 'data-uses-declared' | 'data-uses-decided';

export interface Requirements {
  gates: {
    gateKey: Key;
    requirements: {
      requirementKind: RequirementKind;
      decisionTypeKey?: Key;
      documentTypeKey?: Key;
      reviewTypeKey?: Key;
      // The guideline number the requirement applies, such as G4.
      guideline: string | null;
      // Set when the read is for one application.
      met?: boolean;
      // The guideline as a chip: number, title and link (document 24, B24-18).
      guidelineRef?: GuidelineRef | null;
    }[];
  }[];
  approvals: ApprovalNeeded[];
  documents: { documentTypeKey: Key; fromStageKey: Key }[];
  reviews: { reviewTypeKey: Key; intervalDays: number }[];
  gatePolicy: { targetStageKey: Key; changeTypeKey: Key; gateLevel: GateLevel; failMode: 'fail-open' | 'fail-closed' }[];
}

// ---- list_reviews ----

export type ReviewListState = 'overdue' | 'due' | 'done' | 'cancelled' | 'all';

export type ListReviewsInput = {
  state?: ReviewListState;
  dueWithinDays?: number;
  reviewTypeKeys?: Key[];
  businessApplicationId?: Id;
  portfolioId?: Id;
  mineOnly?: boolean;
  top?: number;
  skipToken?: string;
};

export interface ReviewItem {
  reviewId: Id;
  // REV-0042; null for a review written before the engine numbered reviews.
  recordNumber: string | null;
  reviewType: LabelledKey;
  businessApplicationId: Id | null;
  applicationName: string | null;
  portfolio: { portfolioId: Id; name: string } | null;
  status: StatusValue;
  trigger: 'cadence' | 'tier-answer-trigger' | 'owner-check' | 'request' | null;
  dueOn: IsoDate | null;
  doneOn: IsoDate | null;
  nextDueOn: IsoDate | null;
  isOverdue: boolean;
  reviewer: PrincipalRef | null;
  leadOwnerRole: LabelledKey | null;
  leadOwner: PrincipalRef | null;
  // The decision type the review records its outcome with.
  producesDecisionType: LabelledKey | null;
  outcomeDecisionId: Id | null;
  isMine: boolean;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

export interface ReviewList {
  items: ReviewItem[];
  counts: { overdue: number; due: number; done: number };
  totalCount: number;
  skipToken?: string;
}

// ---- get_review ----

export type GetReviewInput = {
  reviewId: Id;
};

// A gate a review feeds: through its outcome decision when that is the gate's gate decision (the architecture review's
// design decision passes the Design gate), or through a review-done gate requirement of its type.
export interface ReviewGate {
  gateKey: Key;
  gateLabel: string;
  feedsBy: 'gate-decision' | 'requirement';
  requirementKey: Key | null;
  met: boolean;
  guideline: string | null;
  // The guideline as a chip: number, title and link (document 24, B24-18).
  guidelineRef?: GuidelineRef | null;
}

export interface ReviewDetail {
  review: ReviewItem & { description: string | null };
  // Why the review is the caller's to do: lead owner, decides the decision it records, or approves it.
  yourPart: 'lead' | 'decides' | 'approves' | null;
  evidence: {
    evidenceLinkId: Id;
    linkKind: LabelledKey;
    url: string | null;
    label: string;
    evidenceLevel: LabelledKey | null;
    capturedOn: IsoDateTime | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
  }[];
  outcomeDecision: {
    decisionId: Id;
    recordNumber: string | null;
    statement: string;
    decisionType: LabelledKey;
    status: StatusValue;
    decidedOn: IsoDate | null;
  } | null;
  cadence: { cadenceKey: Key; intervalDays: number; leadDays: number } | null;
  nextDueIfDoneToday: IsoDate | null;
  gates: ReviewGate[];
  canComplete: boolean;
  cannotComplete: string | null;
}

// ---- list_portfolios ----

export type ListPortfoliosInput = {
  parentPortfolioId?: Id;
  includeInactive?: boolean;
  top?: number;
  skipToken?: string;
};

export interface PortfolioItem {
  portfolioId: Id;
  name: string;
  level: LabelledKey | null;
  parentPortfolioId: Id | null;
  path: { portfolioId: Id; name: string }[];
  accountablePrincipal: PrincipalRef | null;
  isActive: boolean;
  counts: {
    applications: number;
    applicationsWithOwnerGap: number;
    // The one owner gap count of the portfolio and the ones below it (document 24, B24-17).
    ownerGaps?: number;
    reviewsOverdue: number;
    openDecisions: number;
  };
  recordUrl: string;
}

export interface PortfolioList {
  items: PortfolioItem[];
  totalCount: number;
  skipToken?: string;
}

// ---- list_owner_assignments ----

export type OwnerAssignmentState = 'current' | 'ended' | 'upcoming';

export type ListOwnerAssignmentsInput = {
  principalObjectId?: Guid;
  ownerRoleKeys?: Key[];
  businessApplicationId?: Id;
  portfolioId?: Id;
  state?: OwnerAssignmentState | 'all';
  // Holders with a recorded leave date, or disabled or deleted in Entra.
  leaversOnly?: boolean;
  top?: number;
  skipToken?: string;
};

export interface OwnerAssignmentItem extends OwnerAssignment {
  businessApplicationId: Id;
  applicationName: string;
  state: OwnerAssignmentState;
  holder: {
    directoryState: DirectoryState | null;
    directoryStateOn: IsoDateTime | null;
    leavesOn: IsoDate | null;
  };
  // The approvals that would move if the holder leaves.
  pendingApprovalCount: number;
  recordUrl: string;
}

export interface OwnerCoverage {
  ownerRoleKey: Key;
  label: string;
  requiresDeputy: boolean;
  applicationsHeld: number;
  applicationsWithDeputy: number;
  gaps: number;
}

export interface OwnerAssignmentList {
  items: OwnerAssignmentItem[];
  coverage: OwnerCoverage[];
  applicationsInScope: number;
  // The one owner gap count for the scope (document 24, B24-17).
  ownerGaps?: number;
  totalCount: number;
  skipToken?: string;
}

// ---- get_change_request ----

export type GetChangeRequestInput = {
  changeRequestId: Id;
};

export interface ChangeRequestDetail {
  change: {
    changeRequestId: Id;
    title: string;
    description: string;
    businessApplicationId: Id;
    changeType: LabelledKey;
    standardChangeModel: LabelledKey | null;
    status: StatusValue;
    mode: 'built-in' | 'linked';
    windowStart: IsoDateTime | null;
    windowEnd: IsoDateTime | null;
    plannedTargetId: Id | null;
    externalChangeNumber: string | null;
    externalChangeUrl: string | null;
    createdBy: PrincipalRef | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
  };
  authorization: {
    decisionId: Id | null;
    status: StatusValue | null;
    approvalsSummary: ApprovalsSummary;
    settledBy?: SettledBy | null;
    linkedSource: 'itsm-step' | 'attestation' | 'none' | null;
  };
  affected: { businessApplicationId: Id; name: string; impactRoleKey: Key | null }[];
  deployments: {
    deploymentRecordId: Id;
    deploymentTargetId: Id;
    targetLabel: string;
    result: StatusValue;
    finishedOn: IsoDateTime;
    flags: DeploymentFlags;
  }[];
  result: LabelledKey | null;
  // True when complete_change would close it now: authorized and scheduled, or an emergency that allows authorization after.
  closable?: boolean;
  postChangeReviews?: { reviewId: Id; status: StatusValue; dueOn: IsoDate | null; doneOn: IsoDate | null }[];
  links: RecordLink[];
}

// ---- check_standard_change ----

export type CheckStandardChangeInput = {
  changeRequestId: Id;
};

export interface StandardChangeCheck {
  qualifies: boolean;
  model: { key: Key; label: string; approvedByDecisionId: Id } | null;
  failedConditions: {
    condition: 'max-tier-rank' | 'target-stages-in' | 'max-affected-applications' | 'component-kinds-within' | 'max-window-minutes' | 'stated';
    why: string;
  }[];
}

// ---- get_documentation_status ----

export type GetDocumentationStatusInput = {
  businessApplicationId?: Id;
  mine?: true;
  statusMeanings?: DocumentStatusMeaning[];
  top?: number;
  skipToken?: string;
};

export interface DocumentationStatus {
  applications: {
    businessApplicationId: Id;
    name: string;
    required: { documentTypeKey: Key; fromStageKey: Key; requirementKey: Key }[];
    documents: {
      documentLinkId: Id;
      documentTypeKey: Key;
      url: string;
      // The link kind the link was recorded with. A form that updates the link sends it back unchanged.
      linkKindKey: Key | null;
      status: StatusValue;
      owner: PrincipalRef | null;
      lastUpdatedOn: IsoDate | null;
      lastCheckedOn: IsoDate | null;
      staleOn: IsoDate | null;
    }[];
    missing: Key[];
    stale: Id[];
  }[];
  skipToken?: string;
}

// ---- list_claims ----

export type ListClaimsInput = {
  statusMeanings?: ClaimStatusMeaning[];
  suggestedToMe?: boolean;
  findingKinds?: ('unregistered' | 'vanished')[];
  componentKindKeys?: Key[];
  top?: number;
  skipToken?: string;
};

export interface ClaimItem {
  claimId: Id;
  findingKind: 'unregistered' | 'vanished';
  status: StatusValue;
  componentKind: LabelledKey | null;
  labelAsSeen: string;
  targetAsSeen: string | null;
  url: string | null;
  suggestedClaimant: PrincipalRef | null;
  claimedBy?: PrincipalRef | null;
  claimedOn?: IsoDateTime | null;
  resultingComponentId?: Id | null;
  // The application the claimed component belongs to.
  businessApplicationId?: Id | null;
  // The ownership decision about the claim, and the intake request a claim for a new application started.
  ownershipDecisionId?: Id | null;
  intakeRequestId?: Id | null;
  foundOn: IsoDateTime;
  lastSeenOn: IsoDateTime | null;
}

export interface ClaimList {
  items: ClaimItem[];
  skipToken?: string;
}

// =====================================================================================================================
// The work pages (D-36): writes. A person writes through POST /api/{tool}; the engine checks every write exactly as it
// checks an AI client's. Each input ends with WriteControls (clientRequestId, validateOnly), which the action sheet sets.
// =====================================================================================================================

// ---- answer_approval (person only: never served to an AI client) ----

export type AnswerApprovalInput = WriteControls & {
  approvalRecordId: Id;
  answer: 'approve' | 'reject';
  // Required to reject, and to approve where the approval rule asks for one.
  reason?: string;
};

export type ApprovalAnswerResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'approved' | 'rejected'>;
  approvalRecordId: Id;
  decisionId: Id;
  answer: 'approve' | 'reject';
  // The role, or the owner role on the decision's application, through which the person was asked.
  answeredAs: { roleKey: Key | null; ownerRoleKey: Key | null };
  isConsultation: boolean;
  countsTowardRequirement: boolean;
  decisionStatus: StatusOf<DecisionStatusMeaning>;
  openedApprovalRecordIds: Id[];
  cancelledApprovalRecordIds: Id[];
};

// ---- save_intake_draft ----

export type SaveIntakeDraftInput = WriteControls & {
  intakeRequestId?: Id;
  title: string;
  need: string;
  proposedKindKey?: Key;
  businessApplicationId?: Id;
  expectedBenefit?: string;
  usesAi?: boolean;
  horizonKey?: Key;
  // The person named as business owner, by the object ID find_people gives. Never the requester.
  businessOwnerObjectId?: Id;
  // The portfolio the request belongs to.
  portfolioId?: Id;
};

export type IntakeDraftResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'draft'>;
};

// ---- score_intake_request (the PMO's demand fields) ----

export type ScoreIntakeRequestInput = WriteControls & {
  intakeRequestId: Id;
  score?: number;
  clearScore?: boolean;
  sizeBandKey?: Key;
  horizonKey?: Key;
  expectedBenefit?: string;
};

export type ScoreIntakeResult = WriteResult & {
  score: number | null;
  sizeBand: LabelledKey | null;
  horizon: LabelledKey | null;
  priority: IntakePriority | null;
};

// ---- find_people ----

export type FindPeopleInput = {
  text: string;
  principalKinds?: PrincipalKind[];
  activeOnly?: boolean;
  top?: number;
};

export interface PersonFound {
  objectId: Id;
  displayLabel: string;
  principalKind: PrincipalKind;
  directoryState: 'enabled' | 'disabled' | 'deleted' | 'unknown' | null;
}

export interface PeopleFound {
  items: PersonFound[];
  totalCount: number;
}

// ---- answer_tier_questions ----

export type AnswerTierQuestionsInput = WriteControls & {
  // One of the two.
  intakeRequestId?: Id;
  businessApplicationId?: Id;
  answers: { questionKey: Key; answerKey: Key }[];
};

export type TierAnswersResult = Omit<WriteResult, 'status'> & {
  status: null;
  tierAssessmentId: Id | null;
  computedTier: { key: Key; label: string; rank: number };
  drivingAnswers: Key[];
  // Exactly one key each.
  triggers: { reviewTypeKey?: Key; gateRequirementKey?: Key }[];
  note: string;
  // The highest answer the application's declared data implies (DC-09), as a hint. A person answers; the hint never sets
  // the answer.
  impliedAnswer?: ImpliedAnswer | null;
};

// ---- submit_record ----

// data-use and data-mapping submit the use's or the copy mapping's open decision. An AI client may submit a data-use
// decision only after a person confirmed the use, and never a copy or retention decision.
export type SubmittableKind = 'intake-request' | 'change-request' | 'decision' | 'configuration-draft' | 'data-use' | 'data-mapping';

export type SubmitRecordInput = WriteControls & {
  subjectKind: SubmittableKind;
  subjectId: Id;
  note?: string;
};

// One approval request a submit opened. Exactly one of requiredRoleKey and requiredOwnerRoleKey is set. askedPrincipal
// names the one person asked by name (a proposed owner, or a portfolio's accountable person), else null. A check
// (validateOnly) lists the requests the submit would open, with a null approvalRecordId.
export interface ApprovalRequested {
  approvalRecordId: Id | null;
  requiredRoleKey?: Key;
  requiredOwnerRoleKey?: Key;
  askedPrincipal?: PrincipalRef | null;
  approvalMode: ApprovalMode;
  expiresOn: IsoDateTime | null;
  channelKey: Key;
  isConsultation: boolean;
}

export type SubmitResult = Omit<WriteResult, 'status'> & {
  subjectKind: SubmittableKind;
  subjectId: Id;
  // A check (validateOnly) leaves a draft a draft.
  status: StatusOf<'submitted' | 'proposed' | 'draft'>;
  decisionId: Id | null;
  approvalsRequested: ApprovalRequested[];
  standardChangeApplied: { modelKey: Key; approvedByDecisionId: Id } | null;
  engineAccepted: { decisionId: Id; condition: 'lowest-tier' | 'standard-model' } | null;
};

// ---- withdraw_record ----

// data-use, data-mapping and data-location withdraw a draft data row (it is archived).
export type WithdrawableKind = 'intake-request' | 'change-request' | 'decision' | 'application-draft' | 'configuration-draft' | 'data-use' | 'data-mapping' | 'data-location';

export type WithdrawRecordInput = WriteControls & {
  subjectKind: WithdrawableKind;
  subjectId: Id;
  reason: string;
};

export type WithdrawResult = Omit<WriteResult, 'status'> & {
  subjectKind: WithdrawableKind;
  subjectId: Id;
  status: StatusOf<'withdrawn' | 'archived' | 'discarded'>;
  cancelledApprovals: Id[];
  withdrawnDecisionId: Id | null;
};

// ---- save_decision_proposal ----

export type SaveDecisionProposalInput = WriteControls & {
  decisionId?: Id;
  // data-use and data-retention take a data-use subject, a copy decision a data-mapping subject.
  decisionTypeKey: Key;
  subjectKind: Exclude<DecisionSubjectKind, 'configuration-draft' | 'data-domain' | 'data-location'>;
  subjectId: Id;
  statement: string;
  context: string;
  optionsConsidered?: { option: string; whyNot: string }[];
  proposedOutcomeKey: Key;
  reason: string;
  // A tier decision: the tier it sets.
  tierKey?: Key;
  // An autonomy decision with the outcome agree: the level it agrees.
  autonomyLevelKey?: Key;
  confidenceKey?: Key;
  supersedesDecisionId?: Id;
  amendsDecisionId?: Id;
  evidence?: EvidenceInput[];
  // Required for an exception decision, and only there.
  exception?: { standard: string; reviewOn: IsoDate };
  // The slide-back signal the decision acts on, so the Measures page links the signal to it (B24-22).
  slideBackSignalKey?: SlideBackSignalKey;
};

export type DecisionProposalResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'proposed'>;
  // From the accountable row of the winning Decision Right group. Exactly one of the two keys is set.
  decider: { accountableRoleKey?: Key; accountableOwnerRoleKey?: Key; principal: PrincipalRef | null };
  approvalsNeeded: ApprovalNeeded[];
  evidenceLinkIds: Id[];
};

// ---- propose_owner_change ----

// Exactly one subject: an application, a data domain (by key) or a data location (docs/architecture/data-taxonomy.md, MCP tools and REST). Data
// owner, deputy and steward roles go on a domain; the technical owner on a location.
export type ProposeOwnerChangeInput = WriteControls & {
  businessApplicationId?: Id;
  dataDomainKey?: Key;
  dataLocationId?: Id;
  // Each change adds a principal (addObjectId), ends an assignment (endAssignmentId), or both.
  changes: {
    ownerRoleKey: Key;
    addObjectId?: Guid;
    endAssignmentId?: Id;
    isDeputy?: boolean;
    startOn?: IsoDate;
  }[];
  reason: string;
};

export type OwnerChangeResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'proposed'>;
  decisionId: Id | null;
  pendingAssignments: Id[];
  approvalsNeeded: ApprovalNeeded[];
};

// ---- record_owner_leave ----

export type RecordOwnerLeaveInput = WriteControls & {
  principalObjectId: Guid;
  leavesOn: IsoDate;
  kind: 'leave' | 'role-change';
  note?: string;
};

// A data domain or data location an owner leaves (record_owner_leave affectedData, run_owner_check dataSubjects).
export interface DataOwnerSubject {
  subjectKind: 'data-domain' | 'data-location';
  dataDomainKey: Key | null;
  dataLocationId: Id | null;
  label: string;
  ownerRoleKey: Key;
  hasDeputy: boolean;
}

export type OwnerLeaveResult = Omit<WriteResult, 'status'> & {
  status: null;
  affected: { businessApplicationId: Id; ownerRoleKey: Key; hasDeputy: boolean }[];
  // The data domains and locations the person owns, deputizes or stewards.
  affectedData?: DataOwnerSubject[];
  // Every owner check that covers the leave; reusedOwnerCheckReviewIds names the ones that were already due.
  ownerCheckReviewIds: Id[];
  reusedOwnerCheckReviewIds?: Id[];
  pendingApprovals: number;
};

// ---- save_change_request_draft ----

export type SaveChangeRequestDraftInput = WriteControls & {
  changeRequestId?: Id;
  title: string;
  businessApplicationId: Id;
  changeTypeKey: Key;
  standardChangeModelKey?: Key;
  description: string;
  windowStart?: IsoDateTime;
  windowEnd?: IsoDateTime;
  plannedTargetId?: Id;
  affected?: { businessApplicationId: Id; impactRoleKey: Key }[];
  externalChangeNumber?: string;
  externalChangeUrl?: string;
};

export type ChangeDraftResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'draft'>;
  mode: 'built-in' | 'linked';
  affectedApplicationIds: Id[];
  requirementsPreview: {
    tierKey: Key | null;
    targetStageKey: Key | null;
    requiresAuthorization: boolean;
    gateLevel: GateLevel | null;
    approvals: ApprovalNeeded[];
  };
};

// ---- record_change_result ----

export type RecordChangeResultInput = WriteControls & {
  changeRequestId: Id;
  resultKey: Key;
  note?: string;
  evidence?: EvidenceInput[];
};

export type ChangeResultResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<RecordStatusMeaning>;
  result: LabelledKey;
  postChangeReviewId: Id | null;
  evidenceLinkIds: Id[];
};

// ---- complete_change (person only: never served to an AI client) ----

export type CompleteChangeInput = WriteControls & {
  changeRequestId: Id;
  // Leave out to keep the result recorded with record_change_result.
  resultKey?: Key;
  evidence?: EvidenceInput[];
};

export type CompleteChangeResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'completed'>;
  result: LabelledKey;
  postChangeReviewId: Id | null;
  evidenceLinkIds: Id[];
};

// ---- complete_review (person only: never served to an AI client) ----

export type CompleteReviewInput = WriteControls & {
  reviewId: Id;
  // Leave out to use the outcome decision the review names.
  outcomeDecisionId?: Id;
  doneOn?: IsoDate;
  evidence?: EvidenceInput[];
};

export type CompleteReviewResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'done'>;
  reviewId: Id;
  doneOn: IsoDate;
  reviewer: PrincipalRef;
  outcomeDecisionId: Id;
  nextDueOn: IsoDate | null;
  gates: ReviewGate[];
  evidenceLinkIds: Id[];
};

// ---- record_document_link ----

export type RecordDocumentLinkInput = WriteControls & {
  documentLinkId?: Id;
  businessApplicationId: Id;
  documentTypeKey: Key;
  url: string;
  statusMeaning?: 'draft' | 'current';
  lastUpdatedOn?: IsoDate;
  documentOwnerObjectId?: Guid;
  linkKindKey?: Key;
};

export type DocumentLinkResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'draft' | 'current'>;
  documentLinkId: Id | null;
  staleOn: IsoDate | null;
  confirmationCardSent: boolean;
};

// ---- confirm_document_checked ----

export type ConfirmDocumentCheckedInput = WriteControls & {
  documentLinkId: Id;
  stillTrue: boolean;
  note?: string;
};

export type DocumentCheckedResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'draft' | 'current' | 'stale'>;
  lastCheckedOn: IsoDate;
  staleOn: IsoDate | null;
  confirmationCardSent: boolean;
};

// ---- claim_resource ----

export type ClaimResourceInput = WriteControls & {
  claimId: Id;
  // One of the two: an existing application, or a new draft application.
  businessApplicationId?: Id;
  newApplication?: { name: string; purpose: string; applicationKindKey: Key; portfolioId?: Id; businessOwnerObjectId?: Guid };
};

export type ClaimResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'claimed'>;
  technicalComponentId: Id | null;
  businessApplicationId: Id | null;
  applicationCreated: boolean;
  applicationStatus: StatusOf<'draft' | 'registered' | 'archived'> | null;
  ownershipDecisionId: Id | null;
  // The draft intake request a claim for a new application starts.
  intakeRequestId: Id | null;
  // True when the ownership decision was submitted in the same call (a person's claim).
  decisionSubmitted: boolean;
  // The requests the submitted ownership decision opened, as the output schema gives them (wave 4b: typed in full, so
  // the sheet lists who is asked now).
  approvalsRequested: ApprovalRequested[];
  approvalsNeeded: ApprovalNeeded[];
};

// ---- dismiss_claim ----

export type DismissClaimInput = WriteControls & {
  claimId: Id;
  reason: string;
  reasonKey?: Key;
  // True reopens a dismissed claim.
  undo?: boolean;
};

export type DismissClaimResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'dismissed' | 'open'>;
};

// ---- save_link ----

export type SaveLinkInput = WriteControls & {
  linkId?: Id;
  linkCategory: LinkCategory;
  subjectKind: 'business-application' | 'change-request' | 'intake-request' | 'decision' | 'approval-record' | 'deployment-record' | 'review' | 'claim';
  subjectId: Id;
  linkKindKey: Key;
  externalId?: string;
  url: string;
  label: string;
  evidenceLevelKey?: Key;
  digest?: string;
  movedToUrl?: string;
};

export type SaveLinkResult = Omit<WriteResult, 'status'> & {
  status: null;
  linkId: Id | null;
  linkCategory: LinkCategory;
};

// =====================================================================================================================
// The reads and writes of the remaining screens (document 24, B24-11): tools 65 to 74
// =====================================================================================================================

export interface RecordRef {
  entity: string;
  recordId: string;
  recordNumber: string | null;
  label: string;
  status: StatusValue | null;
  recordUrl: string;
}

export interface RefusalView {
  code: string;
  ruleKey: string | null;
  reasonCode: string | null;
  ruleIds: string[];
  rule: string;
  knownTool: boolean;
  // The guideline the refusing rule applies (G17) and its title, for the chip that opens Guidelines. Null for none.
  guideline?: string | null;
  guidelineTitle?: string | null;
}

// ---- get_audit_trail (the auditor and the platform admin) ----

export interface GetAuditTrailInput {
  recordId?: Id;
  principalObjectId?: Guid;
  businessApplicationId?: Id;
  operations?: string[];
  actorKinds?: ActorKind[];
  // Only the entries made through this client app, by the name the engine stamped, case ignored (wave 4, B24-21).
  clientApp?: string;
  from?: IsoDate;
  to?: IsoDate;
  top?: number;
  skipToken?: string;
}

export interface AuditItem {
  auditEntryId: Id;
  occurredOn: IsoDateTime | null;
  operation: string;
  what: string;
  tool: string | null;
  record: { entity: string | null; recordId: string; label: string | null; recordUrl: string | null } | null;
  actor: PrincipalRef | null;
  actorKind: ActorKind;
  onBehalfOf: PrincipalRef | null;
  clientApp: string | null;
  correlationId: string;
  statusAfter: string | null;
  refusal: RefusalView | null;
  hasRequestKey: boolean;
}

export interface AuditTrail {
  items: AuditItem[];
  totalCount: number;
  skipToken?: string;
}

// ---- get_evidence ----

export interface GetEvidenceInput {
  businessApplicationId?: Id;
  decisionId?: Id;
  from?: IsoDate;
  to?: IsoDate;
}

export interface EvidenceApproval {
  approvalRecordId: Id;
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  isConsultation: boolean;
  approver: PrincipalRef | null;
  approverActorKind: string | null;
  outcome: StatusValue;
  typedReason: string | null;
  requestedOn: IsoDateTime | null;
  respondedOn: IsoDateTime | null;
  countsTowardRequirement: boolean;
  configurationSetVersion: number | null;
}

export interface EvidenceDecision {
  decisionId: Id;
  recordNumber: string | null;
  decisionType: LabelledKey;
  statement: string;
  reason: string | null;
  outcome: LabelledKey | null;
  status: StatusValue;
  proposedBy: PrincipalRef | null;
  decider: PrincipalRef | null;
  decidedOn: IsoDateTime | null;
  settledBy: 'answers' | 'engine' | 'person' | 'recorded' | null;
  approvals: EvidenceApproval[];
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  configurationSetVersion: number | null;
  recordUrl: string;
}

export interface EvidencePack {
  subject: { subjectKind: 'business-application' | 'decision'; subjectId: Id; label: string; businessApplicationId: Id | null; recordUrl: string };
  configurationInForce: { configurationSetVersion: number; contentHash: string };
  versionsStamped: number[];
  decisions: EvidenceDecision[];
  changes: Array<{
    changeRequestId: Id;
    recordNumber: string | null;
    title: string;
    changeType: LabelledKey;
    status: StatusValue | null;
    result: LabelledKey | null;
    windowStart: IsoDateTime | null;
    windowEnd: IsoDateTime | null;
    authorizationDecisionId: Id | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
    configurationSetVersion: number | null;
    recordUrl: string;
  }>;
  deployments: DeploymentItem[];
  reviews: Array<{
    reviewId: Id;
    recordNumber: string | null;
    reviewType: LabelledKey;
    status: StatusValue;
    dueOn: IsoDate | null;
    doneOn: IsoDate | null;
    reviewer: PrincipalRef | null;
    outcomeDecisionId: Id | null;
    configurationSetVersion: number | null;
    recordUrl: string;
  }>;
  documents: Array<{
    documentLinkId: Id;
    documentType: LabelledKey;
    status: StatusValue | null;
    url: string | null;
    owner: PrincipalRef | null;
    lastCheckedOn: IsoDate | null;
    isAiCreated: boolean;
    configurationSetVersion: number | null;
    recordUrl: string;
  }>;
  evidenceLinks: Array<{
    evidenceLinkId: Id;
    subjectKind: 'decision' | 'approval-record' | 'change-request' | 'deployment-record' | 'review';
    subjectId: Id;
    linkKind: LabelledKey;
    url: string | null;
    label: string;
    evidenceLevel: LabelledKey | null;
    capturedOn: IsoDateTime | null;
    isAiCreated: boolean;
    configurationSetVersion: number | null;
  }>;
  counts: { decisions: number; approvalAnswers: number; changes: number; deployments: number; flaggedDeployments: number; reviews: number; documents: number; evidenceLinks: number; aiCreated: number };
  truncated: boolean;
}

// ---- get_ai_activity (the keynote) ----

export type AiActivityView = 'all' | 'drafts' | 'waiting' | 'accepted' | 'withdrawn' | 'refused' | 'answers' | 'code-approvals';

export interface GetAiActivityInput {
  view?: AiActivityView;
  mineOnly?: boolean;
  onBehalfOfObjectId?: Guid;
  clientApp?: string;
  businessApplicationId?: Id;
  // Wave 4b review: the AI work about the applications of one portfolio, child portfolios included.
  portfolioId?: Id;
  from?: IsoDate;
  to?: IsoDate;
  top?: number;
  skipToken?: string;
}

export interface AiActivityItem {
  kind: 'draft' | 'refusal' | 'answer' | 'code-approval';
  occurredOn: IsoDateTime | null;
  clientApp: string | null;
  onBehalfOf: PrincipalRef | null;
  application: { businessApplicationId: Id; name: string } | null;
  record: RecordRef | null;
  state: 'waiting' | 'submitted' | 'decided' | 'withdrawn' | 'recorded' | null;
  takenForward: { actorKind: ActorKind; principal: PrincipalRef | null; on: IsoDateTime | null; fromHistory: boolean } | null;
  editedByPersonFirst: boolean | null;
  withdrawn: { principal: PrincipalRef | null; on: IsoDateTime | null } | null;
  decided: { principal: PrincipalRef | null; on: IsoDateTime | null; outcome: LabelledKey | null } | null;
  refusal: (RefusalView & { tool: string; auditEntryId: Id }) | null;
  answer: { approvalRecordId: Id; approver: PrincipalRef | null; approverActorKind: string | null; outcome: StatusValue; countsTowardRequirement: boolean } | null;
  deployment: { deploymentRecordId: Id; route: { key: Key; meaning: DeploymentRouteMeaning }; flags: DeploymentFlags; gateLevelApplied: GateLevel } | null;
  hasCallHistory: boolean;
}

export interface AiActivity {
  scope: 'mine' | 'estate';
  // Whose work the counts cover: estate for the PMO, who reads the rows done for them only.
  countsScope?: 'mine' | 'estate';
  // everything (wave 4b review): each activity once, the number the "Everything" tab lists. The other counts overlap (a
  // deployment an AI client drafted and started is a draft and a code approval). Optional: a server before wave 4b
  // leaves it out.
  counts: { drafts: number; waiting: number; takenForwardByPerson: number; submittedByAi: number; editedByPersonFirst: number; withdrawn: number; recorded?: number; refused: number; answers: number; codeApprovals: number; everything?: number };
  byClient: Array<{ clientApp: string | null; count: number }>;
  byPerson: Array<{ onBehalfOf: PrincipalRef; count: number }>;
  byApplication: Array<{ businessApplicationId: Id; name: string; count: number }>;
  knownClients: Array<{ key: Key; label: string; isAiClient: boolean; isAllowed: boolean }>;
  // The clients view (document 24, B24-18): per AI client, its recorded calls, drafts, what a person took forward, and refusals.
  clients?: Array<{ clientApp: string; knownClientKey: Key | null; isAllowed: boolean | null; calls: number; drafts: number; acceptedByPerson: number; waiting: number; refused: number; lastActivityOn: IsoDateTime | null }>;
  items: AiActivityItem[];
  totalCount: number;
  skipToken?: string;
}

// ---- get_tier_assessment ----

export interface GetTierAssessmentInput {
  businessApplicationId?: Id;
  intakeRequestId?: Id;
  tierAssessmentId?: Id;
}

export interface TierAssessmentDetail {
  assessment: {
    tierAssessmentId: Id;
    intakeRequestId: Id | null;
    businessApplicationId: Id | null;
    computedTier: { key: Key; label: string; rank: number };
    assessedBy: PrincipalRef | null;
    tierDecision: { decisionId: Id; outcome: LabelledKey | null; status: StatusValue; recordUrl: string } | null;
    isFrozen: boolean;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
    configurationSetVersion: number | null;
  } | null;
  answers: Array<{ questionKey: Key; question: string; answerKey: Key; answer: string; impliedTier: LabelledKey | null; isDriving: boolean }>;
  missingQuestionKeys: Key[];
  triggers: { reviewTypes: LabelledKey[]; gateRequirementKeys: string[] };
  appliedTier: LabelledKey | null;
  differsFromApplied: boolean;
  vocabulary: Array<{ vocabulary: LabelledKey | null; term: string; sourceUrl: string | null }>;
  tierNeeds: { documents: Array<{ documentType: LabelledKey; fromStage: LabelledKey | null }>; reviews: Array<{ reviewType: LabelledKey; intervalDays: number }> };
  history: Array<{ tierAssessmentId: Id; computedTier: LabelledKey | null; assessedBy: PrincipalRef | null; tierDecisionId: Id | null; isAiCreated: boolean }>;
}

// ---- list_documents ----

export type DocumentListState = 'missing' | 'stale' | 'draft' | 'current' | 'other';

export interface ListDocumentsInput {
  state?: 'all' | 'missing' | 'stale' | 'draft' | 'current';
  documentTypeKeys?: Key[];
  businessApplicationId?: Id;
  // One document link by its ID, on its application (wave 4, B24-21).
  documentLinkId?: Id;
  portfolioId?: Id;
  mineOnly?: boolean;
  top?: number;
  skipToken?: string;
}

export interface DocumentList {
  items: Array<{
    businessApplicationId: Id;
    applicationName: string;
    documentType: LabelledKey;
    state: DocumentListState;
    documentLinkId: Id | null;
    url: string | null;
    owner: PrincipalRef | null;
    lastCheckedOn: IsoDate | null;
    staleOn: IsoDate | null;
    requirementKey: string | null;
    requiredFrom: LabelledKey | null;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
    recordUrl: string;
  }>;
  counts: { missing: number; stale: number; draft: number; current: number };
  totalCount: number;
  skipToken?: string;
}

// ---- list_deployment_targets and save_deployment_target (Places) ----

export interface ListDeploymentTargetsInput {
  targetKindKeys?: Key[];
  targetStageKeys?: Key[];
  // active: in service, the targets record_deployment takes; retired: retired ones; all (the default): both.
  state?: 'all' | 'active' | 'retired';
  text?: string;
  top?: number;
  skipToken?: string;
}

export interface DeploymentTargetItem {
  deploymentTargetId: Id;
  label: string;
  targetKind: LabelledKey | null;
  targetStage: { key: Key; label: string; rank: number };
  changePath: { key: Key; label: string; allowedRoutes: string[] };
  linkKind: LabelledKey;
  // Null for a caller who holds only the reader role (document 26, section 6.2).
  externalId: string | null;
  url: string | null;
  lastSeenOn: IsoDateTime | null;
  // The day a person retired the target, and why (document 24, B24-18). A retired target keeps its records and takes no
  // new deployment.
  retiredOn?: IsoDate | null;
  retiredReason?: string | null;
  applications: Array<{ businessApplicationId: Id | null; name: string; artifactName: string; artifactVersion: string; result: StatusValue | null; finishedOn: IsoDateTime | null; deploymentRecordId: Id }>;
  pipelines: Array<{ pipelineReferenceId: Id; label: string; url: string | null }>;
  deploymentCount: number;
  flaggedCount: number;
  // The data locations in service at this target, so Places links to the Data page (B24-22).
  dataLocations?: Array<{ dataLocationId: Id; label: string; platform: LabelledKey | null; recordUrl: string }>;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

export interface DeploymentTargetList {
  items: DeploymentTargetItem[];
  totalCount: number;
  skipToken?: string;
}

export type SaveDeploymentTargetInput = WriteControls & {
  deploymentTargetId?: Id;
  targetKindKey?: Key;
  targetStageKey?: Key;
  changePathKey?: Key;
  linkKindKey?: Key;
  externalId?: string;
  url?: string;
  labelAsSeen?: string;
  description?: string;
  // True retires the target today (with retiredReason); false returns a retired target to service. Nothing else changes.
  retire?: boolean;
  retiredReason?: string;
};

export type SaveDeploymentTargetResult = Omit<WriteResult, 'status'> & {
  status: null;
  deploymentTargetId: Id | null;
  target: DeploymentTargetItem;
};

// ---- list_gap_check_runs ----

export interface ListGapCheckRunsInput {
  runByObjectId?: Guid;
  // One run by its ID (wave 4, B24-21).
  gapCheckRunId?: Id;
  from?: IsoDate;
  to?: IsoDate;
  top?: number;
  skipToken?: string;
}

export interface GapCheckRunList {
  items: Array<{
    gapCheckRunId: Id;
    runBy: PrincipalRef | null;
    adminRoleUsed: string | null;
    estatesCovered: LabelledKey[];
    startedOn: IsoDateTime | null;
    finishedOn: IsoDateTime | null;
    state: 'finished' | 'not-finished';
    itemsSeen: number | null;
    newClaims: number | null;
    vanishedClaims: number | null;
    // Wave 4b review: the open claims of items not registered whose last sighting was this run; a server before wave 4b
    // counted the open vanished items here too.
    openClaimsLastSeen: number;
    // Wave 4b review: the open vanished items whose last sighting was this run. Optional: a server before wave 4b leaves it
    // out.
    openVanishedLastSeen?: number;
    isAiCreated: boolean;
    performedBy: StampedBy | null;
    recordUrl: string;
  }>;
  totalCount: number;
  skipToken?: string;
}

// ---- save_dependency (the map's write) ----

export type SaveDependencyInput = WriteControls & {
  dependencyId?: Id;
  fromApplicationId?: Id;
  toApplicationId?: Id;
  technicalComponentId?: Id;
  dependencyTypeKey?: Key;
  criticalityKey?: Key;
  notes?: string;
  endedOn?: IsoDate;
};

export type SaveDependencyResult = Omit<WriteResult, 'status'> & {
  status: null;
  dependencyId: Id | null;
  fromApplication: { businessApplicationId: Id; name: string };
  to: { businessApplicationId: Id | null; technicalComponentId: Id | null; name: string };
  dependencyType: LabelledKey;
  criticality: LabelledKey | null;
  endedOn: IsoDate | null;
};

// ---- save_portfolio ----

export type SavePortfolioInput = WriteControls & {
  portfolioId?: Id;
  name?: string;
  description?: string;
  levelKey?: Key;
  parentPortfolioId?: Id;
  clearParent?: boolean;
  accountablePrincipalObjectId?: Guid;
  clearAccountable?: boolean;
  isActive?: boolean;
};

export type SavePortfolioResult = Omit<WriteResult, 'status'> & {
  status: null;
  portfolioId: Id | null;
  portfolio: {
    name: string;
    level: LabelledKey | null;
    parentPortfolioId: Id | null;
    path: Array<{ portfolioId: Id; name: string }>;
    accountablePrincipal: PrincipalRef | null;
    isActive: boolean;
  };
};

// =====================================================================================================================
// Wave 2: the other tools the remaining screens call (whoami, role holders, the pipeline map, rule explanations, the
// deployment, pipeline, gap check and component writes, the architecture review request, the application draft).
// Typed here once, so the page files never change this file.
// =====================================================================================================================

// ---- whoami (tool 1): who the signed-in person is to the product, and the tools the Tool Policy gives them ----

export type WhoAmIInput = Record<string, never>;

export interface WhoAmI {
  principal: { objectId: Guid; displayLabel: string; principalKind: PrincipalKind };
  // The product roles the person holds now, each with the Entra group that grants it. Empty when they hold none.
  productRoles: { roleKey: Key; label: string; viaGroup: { objectId: Guid; displayLabel: string } | null }[];
  // person on the web app's own entry (D-36); ai-client-for-person through the MCP endpoint.
  actorKind: 'ai-client-for-person' | 'person';
  clientApp: ClientApp | null;
  configurationSet: { key: Key; version: number; publishedOn: IsoDateTime };
  toolsets: Key[];
  // The tool names the person may call, built per request from the Tool Policy rows and the person's roles.
  tools: string[];
}

// ---- find_records (tool 75): a record by its number, for the global search ----

// A record number as people write it: DEC-0123, dec 123 or DEC0123. The prefix names the entity (APP, IR, DEC, CHG,
// REV, CFG); the digits match with or without leading zeros.
export type FindRecordsInput = { recordNumber: string };

export type NumberedEntity = 'business-application' | 'intake-request' | 'decision' | 'change-request' | 'review' | 'configuration-draft' | 'data-use';

export interface FoundRecords {
  items: Array<{
    entity: NumberedEntity;
    recordId: Id;
    recordNumber: string;
    label: string;
    status: StatusValue | null;
    businessApplicationId: Id | null;
    recordUrl: string;
  }>;
}

// ---- get_role_holders (tool 3): who holds a product role, read live from Entra ----

export type GetRoleHoldersInput = { roleKey: Key };

export interface RoleHolders {
  role: LabelledKey;
  groups: { objectId: Guid; displayLabel: string }[];
  members: { objectId: Guid; displayLabel: string }[];
  readFrom: 'Entra, live' | 'Demo personas';
  note: string | null;
}

// ---- get_pipeline_map (tool 30): which pipelines deploy an application, or where one pipeline deploys ----

// Exactly one of the two.
// One of the two, or neither for every pipeline, a pipeline mapped to no target included (wave 4, B24-21).
export type GetPipelineMapInput = { businessApplicationId?: Id; pipelineReferenceId?: Id };

export interface PipelineMapEntry {
  businessApplicationId: Id;
  applicationName: string;
  deploymentTargetId: Id;
  targetLabel: string | null;
  targetStageKey: Key | null;
  stageOrder: number;
}

export interface PipelineMap {
  pipelines: Array<{
    pipelineReferenceId: Id;
    linkKind: LabelledKey;
    externalId: string;
    label: string | null;
    url: string;
    isMoved: boolean;
    movedToUrl: string | null;
    lastSeenOn: IsoDateTime | null;
    entries: PipelineMapEntry[];
  }>;
}

// ---- explain_rules (tool 50): which rule applied to a record, and why ----

// data: the inheritance trace of a use or a domain, up the tree to the row that sets each rule (data taxonomy, 10.3).
export type ExplainAspect = 'approvals' | 'gate' | 'flags' | 'requirements' | 'separation' | 'data';

export type ExplainRulesInput = {
  subjectKind: 'decision' | 'change-request' | 'intake-request' | 'deployment-record' | 'business-application' | 'data-use' | 'data-mapping' | 'data-domain';
  // A row ID, or the domain key for a data domain.
  subjectId: Id;
  // Only these aspects. Leave out for all.
  aspects?: ExplainAspect[];
  // Explain against this configuration version. Leave out for the version stamped on the record.
  version?: number;
};

export interface RulesExplained {
  configurationSetVersion: number;
  engineVersion: string;
  aspects: Array<{
    aspect: ExplainAspect;
    operation: string;
    winner: { entity: string; key: string; scope: Record<string, unknown> } | null;
    dropped: Array<{ entity: string; key: string; why: string }>;
    trace: Array<{ step: number; entity: string; key: string | null; matched: boolean; why: string }>;
  }>;
  summary: string;
}

// ---- record_deployment (tool 27): this version reached this target (path C: a person records it by hand, D-19) ----

export type DeploymentResultMeaning = 'succeeded' | 'partly-succeeded' | 'failed' | 'not-deployed';
// Who approved the code merge, as reported. Evidence only, never authorization.
export type CodeApprovalKind = 'person' | 'ai-only' | 'none' | 'unknown';

// This tool takes idempotencyKey (the sender's retry key) and no clientRequestId. The action sheet sets the key the
// same way it sets clientRequestId for the other writes (RETRY_KEY_FIELD in data/writes.ts), so a page never does.
export type RecordDeploymentInput = {
  idempotencyKey: string;
  validateOnly?: boolean;
  businessApplicationId?: Id;
  artifactName: string;
  artifactVersion: string;
  artifactKindKey?: Key;
  digest?: string;
  sourceCommit?: string;
  sourceRepositoryUrl?: string;
  // Exactly one of the two: a registered target, or the target's own ID in its tool.
  deploymentTargetId?: Id;
  targetExternalId?: string;
  changeRequestId?: Id;
  pipelineReferenceId?: Id;
  runUrl?: string;
  startedOn?: IsoDateTime;
  finishedOn: IsoDateTime;
  resultMeaning: DeploymentResultMeaning;
  versionBefore?: string;
  versionAfter?: string;
  deployedByObjectId?: Guid;
  mergeActorObjectId?: Guid;
  mergeActorKind?: ActorKind;
  stageOrderRun?: string[];
  codeApprovalKind?: CodeApprovalKind;
  reportedApprovals?: { approverObjectId?: Guid; approverKind: 'person' | 'ai' | 'unknown'; approvedOn?: IsoDateTime; sourceUrl?: string }[];
};

export type RecordDeploymentResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<DeploymentResultMeaning> | null;
  deploymentRecordId: Id | null;
  artifactVersionId: Id | null;
  deploymentTargetId: Id | null;
  changeRequestId: Id | null;
  // Set by the engine from the entry point, never by the caller.
  route: { key: Key; meaning: DeploymentRouteMeaning };
  // Computed by the engine. The caller cannot set them.
  flags: DeploymentFlags;
  gateLevelApplied: GateLevel;
};

// ---- record_deployment_correction (tool 48): that deployment belongs to this change, or a fact was wrong ----

// Like record_deployment, it takes idempotencyKey; the action sheet sets it.
export type RecordDeploymentCorrectionInput = {
  idempotencyKey: string;
  validateOnly?: boolean;
  // The record to correct. It stays as it is; the new record names it in Corrects Record.
  correctsDeploymentRecordId: Id;
  reason: string;
  // At least one of these.
  changeRequestId?: Id;
  resultMeaning?: DeploymentResultMeaning;
  artifactVersion?: string;
  finishedOn?: IsoDateTime;
  runUrl?: string;
};

export type DeploymentCorrectionResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<DeploymentResultMeaning> | null;
  deploymentRecordId: Id | null;
  correctsRecordId: Id;
  flags: DeploymentFlags;
  gateLevelApplied: GateLevel;
};

// ---- map_pipeline (tool 29): this pipeline deploys this app to these targets ----

export type MapPipelineInput = WriteControls & {
  pipelineReferenceId?: Id;
  linkKindKey: Key;
  externalId: string;
  url: string;
  labelAsSeen?: string;
  entries: { businessApplicationId: Id; deploymentTargetId: Id; stageOrder: number }[];
};

export type MapPipelineResult = Omit<WriteResult, 'status'> & {
  status: null;
  pipelineReferenceId: Id | null;
  entries: { pipelineMapEntryId: Id | null; businessApplicationId: Id; deploymentTargetId: Id; stageOrder: number }[];
};

// ---- run_gap_check (tool 35) and compare_inventory_list (tool 36): what runs that is not in the register ----

export type RunGapCheckInput = WriteControls & {
  // Continue a run that has more pages.
  gapCheckRunId?: Id;
  estateKeys?: Key[];
  environmentIds?: string[];
  includeVanished?: boolean;
  compareOnly?: boolean;
  skipToken?: string;
};

export type CompareInventoryListInput = WriteControls & {
  gapCheckRunId?: Id;
  estateKey: Key;
  items: {
    externalId: string;
    componentKindKey?: Key;
    sourceType?: string;
    labelAsSeen: string;
    targetAsSeen?: string;
    suggestedClaimantObjectId?: Guid;
    url?: string;
  }[];
  // Where the list came from, in plain words.
  sourceNote: string;
  isFinalPage?: boolean;
};

export type GapRunResult = Omit<WriteResult, 'status'> & {
  status: null;
  gapCheckRunId: Id | null;
  adminRoleUsed: string | null;
  estatesCovered: Key[];
  itemsSeen: number;
  newClaims: { claimId: Id | null; componentKindKey: Key | null; labelAsSeen: string; targetAsSeen: string | null; suggestedClaimant: PrincipalRef | null }[];
  vanished: { claimId: Id | null; technicalComponentId: Id }[];
  lastSeenUpdated: number;
  evidenceLevelKey: Key;
  // True when the run has more pages: call again with gapCheckRunId and skipToken.
  more: boolean;
  skipToken?: string;
};

// ---- run_owner_check (tool 49): which owners are disabled, deleted or leaving ----

export type RunOwnerCheckInput = WriteControls & {
  gapCheckRunId?: Id;
  businessApplicationIds?: Id[];
  principalObjectIds?: Guid[];
  skipToken?: string;
};

export type OwnerCheckResult = Omit<WriteResult, 'status'> & {
  status: null;
  gapCheckRunId: Id | null;
  principalsChecked: number;
  findings: {
    principal: PrincipalRef;
    directoryState: DirectoryState;
    leavesOn: IsoDate | null;
    applications: { businessApplicationId: Id; ownerRoleKey: Key; hasDeputy: boolean }[];
    // The data domains and locations the person owns, deputizes or stewards.
    dataSubjects?: DataOwnerSubject[];
    ownerCheckReviewIds: Id[];
  }[];
  more: boolean;
  skipToken?: string;
};

// ---- link_component (tool 15): this item belongs to my app; it moved; it is gone ----

export type LinkComponentOutcome = 'linked' | 'moved' | 'retired';

// linked and moved need componentKindKey, externalId and url (moved also replacesComponentId); retired needs
// technicalComponentId. claimId names the vanished claim the call resolves.
export type LinkComponentInput = WriteControls & {
  businessApplicationId: Id;
  outcome?: LinkComponentOutcome;
  componentKindKey?: Key;
  externalId?: string;
  url?: string;
  linkKindKey?: Key;
  labelAsSeen?: string;
  deploymentTargetId?: Id;
  replacesComponentId?: Id;
  technicalComponentId?: Id;
  claimId?: Id;
  note?: string;
};

export type LinkComponentResult = Omit<WriteResult, 'status'> & {
  status: null;
  technicalComponentId: Id | null;
  outcome: LinkComponentOutcome;
  retiredOn?: IsoDate;
  claimStatus?: StatusOf<ClaimStatusMeaning>;
};

// ---- request_architecture_review (tool 47): please review this design ----

export type ReviewTrigger = 'cadence' | 'tier-answer-trigger' | 'owner-check' | 'request';

export type RequestArchitectureReviewInput = WriteControls & {
  businessApplicationId: Id;
  changeRequestId?: Id;
  reason: string;
  dueOn?: IsoDate;
  evidence?: EvidenceInput[];
};

export type ArchitectureReviewResult = Omit<WriteResult, 'status'> & {
  status: StatusValue | null;
  reviewId: Id | null;
  reviewTypeKey: Key;
  trigger: ReviewTrigger;
  assignedRoleKey: Key;
  dueOn: IsoDate;
  // True when a review of this type was already open: the call returned it and opened no new one.
  existingReviewReturned: boolean;
  evidenceLinkIds: Id[];
};

// ---- save_application_draft (tool 14): register an app already built ----

export type SaveApplicationDraftInput = WriteControls & {
  businessApplicationId?: Id;
  name: string;
  purpose: string;
  applicationKindKey: Key;
  portfolioId?: Id;
  buildPathKey?: Key;
  usesAi?: boolean;
  // On a registered application the only field that changes (an owner or the platform admin sets it).
  holdsNoBusinessData?: boolean;
  proposedOwners?: { ownerRoleKey: Key; objectId: Guid; isDeputy?: boolean }[];
  components?: { componentKindKey: Key; externalId: string; url: string; labelAsSeen?: string }[];
  intakeRequestId?: Id;
};

export type ApplicationDraftResult = Omit<WriteResult, 'status'> & {
  status: StatusOf<'draft' | 'registered' | 'archived'> | null;
  components: { technicalComponentId: Id | null; componentKindKey: Key; externalId: string; url: string }[];
  ownershipDecisionId: Id | null;
  approvalsNeeded: ApprovalNeeded[];
  confirmationCardSent: boolean;
};

// ---- The screens still missing (document 24, B24-18): tools 87 to 94 ----

// The chip beside a requirement, a gate, an approval rule or a refusal.
export interface GuidelineRef {
  key: Key;
  number: string;
  title: string;
  url: string | null;
}

export interface DecisionRef {
  decisionId: Id;
  recordNumber: string | null;
  decisionType: LabelledKey;
  status: StatusValue;
  decidedOn: IsoDateTime | null;
  recordUrl: string;
}

export interface Acceptor {
  principal: PrincipalRef | null;
  answeredOn: IsoDateTime | null;
  askedAs: LabelledKey | null;
}

export type DraftStateKey = 'draft' | 'submitted' | 'activated' | 'discarded';

export interface ConfigurationDraftItem {
  configurationDraftId: Id;
  recordNumber: string | null;
  state: { key: DraftStateKey; label: string };
  baseVersion: number;
  isUpgradeDraft: boolean;
  changeSummary: string[];
  operationCount: number;
  isValid: boolean | null;
  errorCount: number;
  warningCount: number;
  instructedBy: PrincipalRef | null;
  submittedOn: IsoDateTime | null;
  expiresOn: IsoDateTime | null;
  discardedOn: IsoDateTime | null;
  discardReason: string | null;
  resultingVersion: number | null;
  decision: DecisionRef | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

export interface ListConfigurationChangesInput {
  view?: 'all' | 'versions' | 'drafts';
  draftStates?: DraftStateKey[];
  top?: number;
  skipToken?: string;
}

export interface ConfigurationChanges {
  configurationSet: { key: string; activeVersion: number; contentHash: string };
  versions: Array<{
    version: number;
    isActive: boolean;
    baseVersion: number | null;
    publishedOn: IsoDateTime | null;
    contentHash: string | null;
    changeSummary: string[];
    instructedBy: PrincipalRef | null;
    decision: DecisionRef | null;
    acceptedBy: Acceptor[];
    acceptedHow: 'approval' | 'engine' | 'bootstrap' | 'not-accepted';
    sourceDraftId: Id | null;
  }>;
  drafts: ConfigurationDraftItem[];
  counts: { versions: number; drafts: number; openDrafts: number };
  skipToken?: string;
}

export interface GetConfigurationDraftInput {
  configurationDraftId: Id;
}

export interface ConfigurationDraftIssue {
  entity: string | null;
  key: string | null;
  ruleKey: string | null;
  message: string;
}

export interface ConfigurationDraftDetail {
  draft: ConfigurationDraftItem;
  proposalRationale: string | null;
  baseMoved: boolean;
  activeVersion: number;
  diff: Array<{ entity: string; key: string; op: 'added' | 'updated' | 'retired'; before: Record<string, unknown> | null; after: Record<string, unknown> | null }>;
  validation: { isValid: boolean | null; errors: ConfigurationDraftIssue[]; warnings: ConfigurationDraftIssue[] };
  impact: {
    applicationsAffected: number;
    applications: Array<{ businessApplicationId: Id; name: string; tier: LabelledKey | null; recordUrl: string }>;
    byChange: Array<{ entity: string; key: string; applicationsAffected: number; sampleApplicationIds: Id[] }>;
    gateChanges: Array<{ tierKey: Key; changeTypeKey: Key; targetStageKey: Key; from: string | null; to: string | null }>;
    gatesTouched: LabelledKey[];
    recordsInFlight: number;
    // The live data uses and copies on the touched domains, with their state now and under the change.
    dataUses?: DataUseImpactItem[];
  };
  decision: DecisionRef | null;
  approvals: ApprovalsSummary | null;
  acceptedBy: Acceptor[];
}

export interface DataUseImpactItem {
  kind: 'data-use' | 'data-mapping';
  id: Id;
  recordNumber: string | null;
  application: { businessApplicationId: Id; name: string } | null;
  domain: { key: Key; path: string };
  stateBefore: string;
  stateAfter: string;
  breaksRule: boolean;
  needsNewDecision: boolean;
  recordUrl: string;
}

export interface GetRoleAccessInput {
  roleKey?: Key;
  includeHolders?: boolean;
}

export interface RoleToolLine {
  tool: string;
  number: number;
  toolset: string;
  access: 'read' | 'write';
  personOnly: boolean;
  maxAiState: string;
  aiMaySubmit: string[];
  relations: string[];
}

export interface RoleAccess {
  roles: Array<{
    role: LabelledKey;
    party: LabelledKey | null;
    duties: string | null;
    isWorkloadRole: boolean;
    isReadOnly: boolean;
    canChangeConfiguration: boolean;
    heldByMe: boolean;
    bindings: Array<{ group: { objectId: Id; displayLabel: string } | null; bindingMethod: 'dataverse-group-team' | 'entra-app-role-assignment' | null; lastVerifiedOn: IsoDateTime | null }>;
    // Null when the holders were not read (all roles by default, or no Graph client).
    holders: Array<{ objectId: Id; displayLabel: string; viaGroup: Id }> | null;
    workloadGrants: Array<{ principal: PrincipalRef | null; reason: string | null; expiresOn: IsoDate | null; isExpired: boolean }>;
    tools: RoleToolLine[];
    decisionRights: Array<{ decisionRight: LabelledKey; decisionType: LabelledKey; letter: string; scope: string }>;
    approvals: Array<{ approvalRule: LabelledKey; decisionType: LabelledKey; isConsultation: boolean; scope: string; guidelineRef: GuidelineRef | null }>;
    separationRules: LabelledKey[];
  }>;
  separationRules: Array<{ separationRule: LabelledKey; ruleKind: string; enforcement: string; words: string }>;
  toolPolicySource: 'configuration' | 'shipped-seed';
  holdersReadFrom: 'entra-live' | 'demo-personas' | 'not-read';
  myRoleKeys: Key[];
}

export interface ListGuidelinesInput {
  number?: string;
  includeRetired?: boolean;
}

export interface GuidelineList {
  hubBaseUrl: string | null;
  guidelines: Array<{
    key: Key;
    number: string;
    version: string;
    title: string;
    rule: string;
    homeChapter: string | null;
    chapters: number[];
    isActive: boolean;
    isAdopted: boolean;
    notAdoptedReason: string | null;
    localText: string | null;
    localUrl: string | null;
    hubUrl: string | null;
    url: string | null;
    supersedes: string | null;
    usedBy: { approvalRules: number; gateRequirements: number; columnProtections: number; refusalCodePrefixes: string[] };
  }>;
  chapters: Array<{ number: number; title: string; track: string | null; guidelineNumbers: string[]; url: string | null }>;
  tracks: Array<{ track: string; chapters: number[] }>;
  buildPaths: Array<{ key: Key; label: string; description: string | null }>;
  knownClients: Array<{ key: Key; label: string; isAiClient: boolean; isAllowed: boolean }>;
}

export interface GetMeasuresInput {
  windowDays?: number;
  windows?: number;
  measureKey?: Key;
  signalNumber?: number;
  top?: number;
  skipToken?: string;
}

export interface MeasurePoint {
  index: number;
  value: number | null;
  numerator: number | null;
  denominator: number | null;
}

export interface Measures {
  asOf: IsoDate;
  windowDays: number;
  windows: Array<{ index: number; from: IsoDate; to: IsoDate }>;
  measures: Array<{
    key: Key;
    label: string;
    definition: string;
    kind: 'flow' | 'coverage';
    unit: 'days' | 'percent' | 'count';
    watcher: LabelledKey | null;
    guideline: string;
    healthyDirection: 'up' | 'down' | 'quiet';
    value: number | null;
    numerator: number | null;
    denominator: number | null;
    trend: MeasurePoint[];
    baseline: { index: number; value: number } | null;
    // From the measure-targets setting (B24-22); null until the sponsor agrees one.
    target: number | null;
    targetSource: 'not-set' | 'configuration';
    // The newest value read in the healthy direction against the target; null with no target or no value.
    targetMet?: boolean | null;
    secondary: { label: string; unit: 'percent' | 'count'; value: number | null; numerator: number | null; denominator: number | null } | null;
    rowCount: number;
  }>;
  signals: Array<{
    number: number;
    key: Key;
    label: string;
    watcher: LabelledKey | null;
    chapter: number;
    threshold: string;
    thresholdSource: 'product-default' | 'configuration';
    readBy: 'product' | 'outside';
    value: number | null;
    unit: 'percent' | 'days' | 'count' | null;
    firing: boolean | null;
    note: string | null;
    rowCount: number;
    // The decisions recorded about this signal (slideBackSignalKey on save_decision_proposal), the latest five.
    decisionCount?: number;
    decisions?: Array<{ decisionId: Id; recordNumber: string | null; statement: string; decisionType: LabelledKey; status: StatusValue | null; decidedOn: IsoDateTime | null; recordUrl: string }>;
  }>;
  firingCount: number;
  rows: {
    measureKey: Key | null;
    signalNumber: number | null;
    items: Array<{ entity: string; recordId: string; label: string; value: number | null; note: string | null; recordUrl: string | null }>;
    totalCount: number;
    skipToken?: string;
  } | null;
}

export interface GetAdminStatusInput {
  days?: number;
  includeToolPolicy?: boolean;
}

export interface AdminStatus {
  host: {
    environmentName: string;
    isDemo: boolean;
    stepUpMode: string;
    dueStateRefresh: { enabled: boolean; hourUtc: number | null; schedule: string };
    store: { kind: string | null; schemaVersion: number | null; requiredSchemaVersion: number | null };
  } | null;
  engine: { engineVersion: string; snapshotSchemaVersion: string };
  configuration: { configurationSetKey: string; version: number; contentHash: string; publishedOn: IsoDateTime; reviewDueOn: IsoDate | null; openDrafts: number };
  toolPolicy: {
    source: 'configuration' | 'shipped-seed';
    rows: Array<{ tool: string; number: number; toolset: string; access: 'read' | 'write'; entries: string[]; release: string; isEnabled: boolean; isServed: boolean; allowedRoleKeys: Key[]; anyoneSignedIn: boolean; relations: string[]; maxAiState: string; aiMaySubmit: string[]; callsPerPersonPerHour: number | null }>;
  };
  clients: Array<{ key: Key | null; label: string; isKnown: boolean; isAiClient: boolean | null; isAllowed: boolean | null; calls: number; refused: number; drafts: number; lastCallOn: IsoDateTime | null }>;
  notices: {
    days: number;
    lastRefreshChangeOn: IsoDateTime | null;
    // The last runs (newest first, at most 5): what each did, and the reviews it opened.
    recentRuns?: Array<{ ranOn: IsoDateTime; trigger: string; ranBy: PrincipalRef | null; reviewsOpened: number; reviewsOverdue: number; documentsStale: number; notices: number; reviews: Array<{ reviewId: Id; label: string; recordUrl: string }> }>;
    reminders: number;
    escalations: number;
    overdue: number;
    flagNotices: number;
    items: Array<Record<string, unknown>>;
  };
  outbox: {
    counts: { pending: number; claimed: number; sent: number; answered: number; expired: number; cancelled: number; failed: number };
    attention: Array<{ approvalRecordId: string; decisionId: Id | null; state: string; attempts: number; enqueuedOn: IsoDateTime; updatedOn: IsoDateTime; recordUrl: string | null }>;
  } | null;
  actions: Array<{ tool: string; label: string; allowed: boolean; why: string }>;
}

export interface ListAgentsInput {
  text?: string;
  tierKeys?: Key[];
  applicationKindKeys?: Key[];
  autonomy?: 'any' | 'agreed' | 'missing';
  flaggedOnly?: boolean;
  portfolioId?: Id;
  top?: number;
  skipToken?: string;
}

export interface AgentItem {
  businessApplicationId: Id;
  recordNumber: string | null;
  name: string;
  registration: StatusValue | null;
  applicationKind: LabelledKey | null;
  tier: LabelledKey | null;
  lifecycleStage: LabelledKey | null;
  autonomy: {
    agreed: LabelledKey | null;
    decisionId: string | null;
    waiting: { decisionId: Id; recordNumber: string | null; proposedLevel: LabelledKey | null; recordUrl: string } | null;
    requiredAtGoLive: boolean;
  };
  owners: Array<{ ownerRole: LabelledKey | null; principal: PrincipalRef | null; isDeputy: boolean }>;
  ownerGaps: Array<{ ownerRole: LabelledKey | null; missing: string }>;
  securityReview: { reviewType: LabelledKey | null; state: 'none' | 'due' | 'overdue' | 'done'; requiredAtGoLive: boolean; lastDoneOn: IsoDate | null; dueOn: IsoDate | null; reviewId: Id | null; recordUrl: string | null };
  goLive: { gate: LabelledKey | null; ready: boolean; unmet: Array<{ requirementKey: Key; label: string; requirementKind: string | null; guidelineRef: GuidelineRef | null }> } | null;
  flaggedDecision: DecisionRef | null;
  flags: Array<{ kind: string; label: string; recordUrl: string | null }>;
  dataUses: { open: number; reachingAi: number; drafts: number };
  components: Array<{ componentKind: LabelledKey | null; label: string; url: string | null }>;
  isAiCreated: boolean;
  recordUrl: string;
}

export interface AgentList {
  applicationKindKeys: Key[];
  goLiveGate: LabelledKey | null;
  counts: { agents: number; autonomyMissing: number; autonomyWaiting: number; securityReviewMissing: number; goLiveNotReady: number; flagged: number };
  items: AgentItem[];
  totalCount: number;
  skipToken?: string;
}

export type RequestSecurityReviewInput = WriteControls & {
  businessApplicationId: Id;
  changeRequestId?: Id;
  reason: string;
  dueOn?: IsoDate;
  evidence?: EvidenceInput[];
};

export type SecurityReviewResult = Omit<WriteResult, 'status'> & {
  status: StatusValue | null;
  reviewId: Id | null;
  reviewTypeKey: Key;
  trigger: ReviewTrigger;
  assignedOwnerRoleKey: Key;
  producesDecisionTypeKey: Key;
  isAgent: boolean;
  dueOn: IsoDate;
  existingReviewReturned: boolean;
  evidenceLinkIds: Id[];
};

// =====================================================================================================================
// Wave 3: the data taxonomy (D-37; tools 76 to 85, toolset data), the due-state refresh (tool 86), and the
// configuration tools the Govern pages call (40 to 44, 51). Shapes from common.schema.json and the tool schemas.
// =====================================================================================================================

// ---- the data taxonomy's shared shapes (common.schema.json) ----

// A handling rule on a domain or a classification: allowed, decision (a data-use decision is needed), masked-only (only
// de-identified data, with a decision), never (refused when saved, G-25).
export type DataHandlingRule = 'allowed' | 'decision' | 'masked-only' | 'never';
// The computed decision state of a use or a copy mapping (docs/architecture/data-taxonomy.md, The decision state). Never stored.
export type DataUseState = 'not-needed' | 'needed' | 'proposed' | 'allowed' | 'denied' | 're-decide' | 'breaks-rule' | 'no-one-to-ask';
// Where a mapping's data comes from: the system of record, a replica, a production copy, a masked copy or synthetic data.
export type DataOrigin = 'system-of-record' | 'replica' | 'production-copy' | 'masked-copy' | 'synthetic';
export type DataAccess = 'read' | 'write' | 'read-write';
// The data checks (docs/architecture/data-taxonomy.md, Data checks). DC-15 comes later.
export type DataCheckCode =
  | 'DC-01' | 'DC-02' | 'DC-03' | 'DC-04' | 'DC-05' | 'DC-06' | 'DC-07'
  | 'DC-08' | 'DC-09' | 'DC-10' | 'DC-11' | 'DC-12' | 'DC-13' | 'DC-14';
export type DataSeverity = 'high' | 'medium' | 'low';

// A domain as other rows name it: key, label and the path from the top ("People / Payroll").
export interface DataDomainRef {
  key: Key;
  label: string;
  path: string;
}

// The effective classification of a domain and the domain it is set on.
export interface DataClassificationValue {
  key: Key;
  label: string;
  rank: number;
  setOn: LabelledKey;
}

// One effective handling rule: its value, the domain that sets it (null when the classification's default applies),
// and the classification it comes from when no domain sets it.
export interface DataRuleValue {
  value: DataHandlingRule;
  setOn: LabelledKey | null;
  fromClassification: LabelledKey | null;
}

export interface DataRules {
  use: DataRuleValue;
  ai: DataRuleValue;
  copy: DataRuleValue;
}

// An effective owner or steward of a domain, with the domain it is inherited from (null when set on the domain itself).
export interface DataPersonValue {
  principal: PrincipalRef;
  ownerAssignmentId: Id;
  inheritedFrom: LabelledKey | null;
}

// One approver a data decision asks: a product role, or an owner role on the domain or location (ownerSubject names
// which), resolved to a person where one holds it. noOneToAsk is true when a counting approver resolves to nobody.
export interface DataApprover {
  kind: 'role' | 'owner-role';
  role: LabelledKey | null;
  ownerRole: LabelledKey | null;
  ownerSubject: Key | null;
  principal: PrincipalRef | null;
  inheritedFrom: LabelledKey | null;
  isConsultation: boolean;
  noOneToAsk: boolean;
}

export interface DataLocationBrief {
  dataLocationId: Id;
  label: string;
  platform: LabelledKey;
  stage: LabelledKey | null;
  recordUrl: string;
}

// One data use (O27, numbered DU-0001): an application reads or writes a domain's data, maybe at a location, maybe
// sending it to AI. state is computed from the rules in force and the decision.
export interface DataUseItem {
  dataUseId: Id;
  recordNumber: string | null;
  businessApplicationId: Id;
  applicationName: string;
  domain: DataDomainRef;
  location: DataLocationBrief | null;
  access: DataAccess;
  purpose: string;
  reachesAi: boolean;
  aiServiceAsSeen: string | null;
  isDeidentified: boolean;
  accessApproach: LabelledKey | null;
  status: StatusValue;
  state: DataUseState;
  approvalsNeeded: number;
  approvalsGiven: number | null;
  decisionId: Id | null;
  startOn: IsoDate | null;
  endedOn: IsoDate | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

// One mapping (O26): a domain's data sits at a location, in some containers, with an origin. A copy outside production
// has a copy state.
export interface DataMappingItem {
  dataMappingId: Id;
  domain: DataDomainRef;
  location: DataLocationBrief;
  containers: string[];
  origin: DataOrigin;
  copiedFromMappingId: Id | null;
  status: StatusValue;
  copyState: DataUseState | null;
  decisionId: Id | null;
  approvalsNeeded: number;
  confirmedOn: IsoDate | null;
  startOn: IsoDate | null;
  endedOn: IsoDate | null;
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

// One technical location (O25) on any platform: an environment, a database, a Salesforce org, a file share.
export interface DataLocationItem {
  dataLocationId: Id;
  label: string;
  platform: LabelledKey;
  parent: { dataLocationId: Id; label: string } | null;
  stage: LabelledKey | null;
  isProduction: boolean;
  deploymentTargetId: Id | null;
  hostedByApplication: { businessApplicationId: Id; name: string } | null;
  linkKind: LabelledKey;
  // Restricted read: null for the reader role (find_data_locations).
  externalId: string | null;
  url: string | null;
  lastSeenOn: IsoDateTime | null;
  status: StatusValue;
  retiredOn: IsoDate | null;
  // fromParent: the owner is inherited from the parent location.
  technicalOwner: { principal: PrincipalRef; ownerAssignmentId: Id; fromParent: boolean } | null;
  domains: { domain: DataDomainRef; dataMappingId: Id; origin: DataOrigin; containers: string[] }[];
  childCount: number;
  flags: { draft: boolean; stale: boolean; unmapped: boolean; noTechnicalOwner: boolean; copyOutsideProduction: boolean };
  isAiCreated: boolean;
  performedBy: StampedBy | null;
  recordUrl: string;
}

// One finding of a data check. check is the check ID (DC-01), code the reason code (data.no-owner), fixTool the tool
// that fixes it.
export interface DataFinding {
  check: string;
  code: string;
  severity: DataSeverity;
  subjectKind: string;
  subjectId: string;
  message: string;
  domain: DataDomainRef | null;
  businessApplicationId: Id | null;
  applicationName: string | null;
  dataLocationId: Id | null;
  principal: PrincipalRef | null;
  // For DC-03 and DC-04: the owner role and whether the person is its deputy.
  ownerRoleKey?: Key | null;
  isDeputy?: boolean | null;
  fixTool: string | null;
  recordUrl: string | null;
  // For DC-10: "declare?", with the other applications and the domains each holds (B24-22).
  suggestion?: {
    text: string;
    applications: Array<{ businessApplicationId: Id; name: string; access: 'read' | 'write'; domains: DataDomainRef[]; recordUrl: string }>;
  } | null;
}

export interface DataCounts {
  mappings: number;
  uses: number;
  openDecisions: number;
}

// One domain as find_data_domains lists it, with its effective values (OP-15).
export interface DataDomainItem {
  key: Key;
  label: string;
  path: string;
  parentKey: Key | null;
  level: LabelledKey | null;
  isActive: boolean;
  declarable: boolean;
  classification: DataClassificationValue | null;
  owner: DataPersonValue | null;
  deputy: PrincipalRef | null;
  steward: DataPersonValue | null;
  rules: DataRules;
  counts: DataCounts;
  // The reason codes of the domain's findings.
  gaps: string[];
  recordUrl: string;
}

// One approval request a data write opened.
export interface ApprovalAsked {
  approvalRecordId: Id;
  requiredRoleKey: Key | null;
  requiredOwnerRoleKey: Key | null;
  askedPrincipal: PrincipalRef | null;
  isConsultation: boolean;
}

// The tier answer the data implies (DC-09), as a hint.
export interface ImpliedAnswer {
  questionKey: Key;
  answerKey: Key;
  label: string;
  domain: DataDomainRef | null;
}

// The rule that decides a use or a copy: use, ai (the stricter of use and AI use, for a use with AI) or copy.
export interface DataGoverningRule {
  name: 'use' | 'ai' | 'copy';
  value: DataHandlingRule;
  setOn: LabelledKey | null;
  fromClassification: LabelledKey | null;
}

// The data owner an application's uses lead to (get_application_owners dataOwners, get_application_card data).
export interface DerivedDataOwner {
  domain: DataDomainRef;
  principal: PrincipalRef | null;
  inheritedFrom: LabelledKey | null;
  dataUseIds: Id[];
}

// get_application_card section data: the summary line, the uses with state, the derived data owners and the findings.
export interface ApplicationCardData {
  // Such as "Reads 3 domains, writes 1. Highest class: Restricted."
  summary: string;
  holdsNoBusinessData: boolean | null;
  highestClassification: LabelledKey | null;
  uses: DataUseItem[];
  dataOwners: DerivedDataOwner[];
  findings: DataFinding[];
}

// ---- find_data_domains (tool 76): which domains match, and who owns them ----

export type FindDataDomainsInput = {
  text?: string;
  levelKeys?: Key[];
  classificationKeys?: Key[];
  // Domains this person owns, deputizes or stewards, their own or inherited.
  ownerObjectId?: Guid;
  gapsOnly?: boolean;
  includeRetired?: boolean;
  top?: number;
  skipToken?: string;
};

export interface DataDomainList {
  items: DataDomainItem[];
  totalCount: number;
  skipToken?: string;
}

// ---- get_data_domain (tool 77): everything about one domain ----

export type DataDomainSection = 'owners' | 'mappings' | 'uses' | 'decisions' | 'matches' | 'gaps';

export type GetDataDomainInput = {
  domainKey: Key;
  // Leave out for every section. A section left out comes back null.
  sections?: DataDomainSection[];
};

export interface DataDomainHolder {
  ownerAssignmentId: Id;
  principal: PrincipalRef;
  directoryState: DirectoryState | null;
  isLeaving: boolean;
}

export interface DataDomainDetail {
  domain: {
    key: Key;
    label: string;
    path: string;
    pathKeys: { key: Key; label: string; level: LabelledKey | null }[];
    parentKey: Key | null;
    level: LabelledKey | null;
    isActive: boolean;
    declarable: boolean;
    ownerRequired: boolean;
    description: string | null;
    // Untrusted text: what belongs in the domain, for an AI client's suggestions.
    examples: string | null;
    classification: DataClassificationValue | null;
    rules: DataRules;
    retention: { retentionClass: LabelledKey; setOn: LabelledKey } | null;
    impliedAnswer: ImpliedAnswer | null;
    children: { key: Key; label: string; isActive: boolean }[];
    counts: DataCounts;
    recordUrl: string;
  };
  // The owner roles up the chain: the holder, other holders, deputies, where each is inherited from, and who acts now.
  owners: {
    ownerRole: LabelledKey;
    inheritsDown: boolean;
    holder: DataDomainHolder | null;
    otherHolders: DataDomainHolder[];
    deputies: DataDomainHolder[];
    inheritedFrom: LabelledKey | null;
    acting: PrincipalRef | null;
  }[] | null;
  mappings: DataMappingItem[] | null;
  uses: DataUseItem[] | null;
  decisions: {
    decisionId: Id;
    recordNumber: string | null;
    decisionType: LabelledKey;
    subjectKind: DecisionSubjectKind;
    subjectId: string;
    statement: string;
    status: StatusValue;
    outcome: LabelledKey | null;
    decidedOn: IsoDateTime | null;
    recordUrl: string;
  }[] | null;
  // Outside matches (Fabric, Purview) through the vocabulary mappings, with the day each was checked.
  matches: { vocabularyKey: Key; term: string; url: string | null; checkedOn: IsoDate | null }[] | null;
  gaps: DataFinding[] | null;
}

// ---- find_data_locations (tool 78): which stores match; with locationId, one store in full ----

export type FindDataLocationsInput = {
  locationId?: Id;
  platformKeys?: Key[];
  stageKeys?: Key[];
  parentLocationId?: Id;
  text?: string;
  unmappedOnly?: boolean;
  staleOnly?: boolean;
  includeRetired?: boolean;
  top?: number;
  skipToken?: string;
};

export interface DataLocationList {
  items: DataLocationItem[];
  totalCount: number;
  skipToken?: string;
}

// ---- resolve_data (tool 79): which data is this table, object or file ----

// Exactly one of locationId, externalId and url.
export type ResolveDataInput = {
  locationId?: Id;
  externalId?: string;
  url?: string;
  platformKey?: Key;
  container?: string;
};

export interface DataResolved {
  location: DataLocationBrief | null;
  matches: {
    domain: DataDomainRef;
    dataMappingId: Id;
    origin: DataOrigin;
    containers: string[];
    containerMatched: boolean;
    owner: DataPersonValue | null;
    classification: DataClassificationValue | null;
    rules: DataRules;
  }[];
  unmapped: boolean;
}

// ---- check_data_use (tool 80): may this app use this data this way, and what is needed ----

// One of applicationId and intakeRequestId; one of domainKey, or locationId with container.
export type CheckDataUseInput = {
  applicationId?: Id;
  intakeRequestId?: Id;
  domainKey?: Key;
  locationId?: Id;
  container?: string;
  access?: DataAccess;
  reachesAi?: boolean;
  isDeidentified?: boolean;
};

export interface DataUseCheck {
  state: DataUseState;
  domain: DataDomainRef;
  location: DataLocationBrief | null;
  classification: DataClassificationValue | null;
  governingRule: DataGoverningRule;
  configurationSetVersion: number;
  decisionNeeded: boolean;
  // True when save_data_use would refuse the use (a never rule, G-25), for a person and an AI client alike.
  refusedWhenSaved: boolean;
  decisionTypeKey: Key;
  approvalsNeeded: number;
  approvers: DataApprover[];
  impliedAnswer: ImpliedAnswer | null;
  existingDataUseId: Id | null;
  explanation: string;
}

// ---- get_data_gaps (tool 81): what is missing, broken or waiting in the data ----

export type GetDataGapsInput = {
  domainKey?: Key;
  includeSubtree?: boolean;
  applicationId?: Id;
  locationId?: Id;
  principalObjectId?: Guid;
  checks?: DataCheckCode[];
  severities?: DataSeverity[];
  top?: number;
  skipToken?: string;
};

export interface DataGaps {
  // The count per check, over the whole scope (not only the page).
  counts: Partial<Record<DataCheckCode, number>>;
  items: DataFinding[];
  totalCount: number;
  skipToken?: string;
  // Under domains-required: the app-level data owners left to clean up through an owner change (B24-22).
  appLevelDataOwners?: Array<{ ownerAssignmentId: Id; businessApplicationId: Id; applicationName: string; principal: PrincipalRef | null; isDeputy: boolean; fixTool: string; recordUrl: string }>;
}

// ---- save_data_use (tool 82): this app reads or writes this data, or it stopped ----

export type SaveDataUseInput = WriteControls & {
  dataUseId?: Id;
  applicationId: Id;
  domainKey: Key;
  locationId?: Id;
  access: DataAccess;
  purpose: string;
  reachesAi: boolean;
  aiServiceAsSeen?: string;
  aiComponentId?: Id;
  isDeidentified?: boolean;
  accessApproachKey?: Key;
  startOn?: IsoDate;
  // Ends the use on this day. Ending is never refused.
  endOn?: IsoDate;
  // A person confirms the use (a person's own save is confirmed when saved; an AI client's row stays a draft).
  confirm?: boolean;
  // Default true: a confirmed use that needs a decision opens it and sends it to the domain's data owner by name.
  proposeDecision?: boolean;
};

export type SaveDataUseResult = WriteResult & {
  dataUseId: Id | null;
  recordNumber: string | null;
  // The confirmed use this save ended, because it widened to write or turned AI on.
  replacedDataUseId: Id | null;
  use: DataUseItem;
  governingRule: DataGoverningRule | null;
  decisionId: Id | null;
  decisionSubmitted: boolean;
  approvalsNeeded: number;
  approvalsRequested: ApprovalAsked[];
};

// ---- save_data_location (tool 83): this store exists, moved, or is gone (up to 100 per call) ----

export type SaveDataLocationInput = WriteControls & {
  items: {
    dataLocationId?: Id;
    platformKey?: Key;
    parentLocationId?: Id;
    targetStageKey?: Key;
    deploymentTargetId?: Id;
    hostedByApplicationId?: Id;
    linkKindKey?: Key;
    externalId?: string;
    url?: string;
    labelAsSeen?: string;
    // Stamps Last Seen On today ("Confirm seen").
    seenNow?: boolean;
    confirm?: boolean;
    retire?: boolean;
  }[];
};

export type SaveDataLocationResult = WriteResult & {
  items: {
    dataLocationId: Id | null;
    created: boolean;
    matchedExisting: boolean;
    status: StatusValue;
    label: string;
    platform: LabelledKey;
    recordUrl: string | null;
  }[];
  createdCount: number;
  updatedCount: number;
};

// ---- save_data_mapping (tool 84): this domain's data sits here, or it left ----

export type SaveDataMappingInput = WriteControls & {
  items: {
    mappingId?: Id;
    domainKey: Key;
    locationId: Id;
    containers?: string[];
    origin: DataOrigin;
    copiedFromMappingId?: Id;
    endOn?: IsoDate;
    confirm?: boolean;
  }[];
  // Default true: a copy outside production that needs a decision opens it.
  proposeCopyDecision?: boolean;
};

export type SaveDataMappingResult = WriteResult & {
  items: {
    dataMappingId: Id | null;
    created: boolean;
    matchedExisting: boolean;
    status: StatusValue;
    domain: DataDomainRef;
    dataLocationId: Id;
    origin: DataOrigin;
    copyState: DataUseState | null;
    decisionId: Id | null;
    decisionSubmitted: boolean;
    approvalsNeeded: number;
    recordUrl: string | null;
  }[];
};

// ---- move_data_references (tool 85): move mappings and uses off a domain ----

// While both domains have the same data owner, rows move in place and their decisions stay with them. When the data owner
// changes, the decisions stay behind (ownerChange says how). An AI client may only preview (validateOnly), and its
// preview runs every rule the person's move runs.
export type MoveDataReferencesInput = WriteControls & {
  fromDomainKey: Key;
  toDomainKey: Key;
  include: ('mappings' | 'uses')[];
  reason: string;
};

// What a move did to the decisions when the two domains answer to different data owners (null when the owner is the same).
export interface MoveOwnerChange {
  newDataOwner: PrincipalRef | null;
  redeclaredCount: number;
  reaskedCount: number;
  openedCount: number;
  withdrawnAnsweredCount: number;
  withdrawnNoOwnerCount: number;
  waitingForOwnerCount: number;
}

export type MoveDataReferencesResult = WriteResult & {
  fromDomain: DataDomainRef;
  toDomain: DataDomainRef;
  movedMappingIds: Id[];
  movedUseIds: Id[];
  changedUses: { dataUseId: Id; stateBefore: DataUseState; stateAfter: DataUseState }[];
  ownerChange: MoveOwnerChange | null;
};

// ---- run_due_state_refresh (tool 86, person only, platform admin): the daily refresh, now ----

// The tool takes validateOnly only: no retry key (RETRY_KEY_FIELD in data/writes.ts). A rerun the same day changes and
// sends nothing.
export type RunDueStateRefreshInput = { validateOnly?: boolean };

export interface DueStateRefreshResult {
  trigger: 'person' | 'timer' | 'command';
  dryRun: boolean;
  counts: {
    reviewsOpened: number;
    reviewsOverdue: number;
    documentsStale: number;
    decisionsRefreshed: number;
    approvalsExpired: number;
    approvalsCancelled: number;
    approvalsOpened: number;
    reminders: number;
    escalations: number;
    flagNotices: number;
    failed: number;
  };
  reviewsOpened: {
    reviewId: Id | null;
    recordNumber: string | null;
    businessApplicationId: Id;
    applicationName: string;
    reviewType: LabelledKey;
    dueOn: IsoDate | null;
    fromReviewId: Id;
    recordUrl: string | null;
  }[];
  documentsStale: {
    documentLinkId: Id;
    businessApplicationId: Id | null;
    applicationName: string | null;
    documentType: LabelledKey;
    staleOn: IsoDate | null;
    recordUrl: string;
  }[];
  approvals: { decisions: number; expiredRecordIds: Id[]; cancelledRecordIds: Id[]; openedRecordIds: Id[] };
  notices: {
    notice: 'remind' | 'repeat' | 'escalate' | 'overdue' | 'expired' | 'flag';
    subjectKind: 'review' | 'document' | 'approval' | 'claim' | 'deployment';
    entity: string;
    rowId: Id;
    label: string;
    businessApplicationId: Id | null;
    applicationName: string | null;
    ruleKey: Key | null;
    reminderNumber: number | null;
    escalated: boolean;
    channel: LabelledKey | null;
    dueOn: IsoDate | null;
    to: { people: PrincipalRef[]; roles: LabelledKey[]; ownerRole: LabelledKey | null; toDeputies: boolean };
    recordUrl: string;
  }[];
  failed: { entity: string; rowId: string; what: string; code: string | null; message: string | null }[];
  more: boolean;
}

// ---- propose_configuration_change (tool 42, configuration admin): a configuration draft (process P11) ----

// Exactly one of operations, preset and fileContent. Preview with validateOnly first; submit the draft with
// submit_record (subjectKind configuration-draft). Nothing changes until a person accepts the decision.
export type ProposeConfigurationChangeInput = WriteControls & {
  configurationDraftId?: Id;
  baseVersionNumber: number;
  proposalRationale: string;
  operations?: { op: 'upsert' | 'retire'; entity: string; key: string; values?: Record<string, unknown> }[];
  preset?: { presetKey: Key; overlayKeys?: Key[] };
  fileContent?: string;
};

export interface ConfigurationDiffEntry {
  entity: string;
  key: string;
  op: 'added' | 'updated' | 'retired';
  before: Record<string, unknown> | null;
  after: Record<string, unknown> | null;
}

export type ConfigurationChangeResult = Omit<WriteResult, 'status'> & {
  status?: StatusValue | null;
  valid: boolean;
  errors: { entity: string | null; key: string | null; ruleKey: string; message: string }[];
  configurationDraftId: Id | null;
  diffSummary: ConfigurationDiffEntry[];
  contentHash: string | null;
  impact: {
    applicationsAffected: number;
    byChange: { entity: string; key: string; applicationsAffected: number; sampleApplicationIds: Id[] }[];
    gateChanges: { tierKey: Key; changeTypeKey: Key; targetStageKey: Key; from: GateLevel | null; to: GateLevel | null }[];
    recordsInFlight: number;
    // The live data uses and copies whose state the change would move, in the proposal's own preview (B24-22).
    dataUses?: DataUseImpactItem[];
  };
  approvalsNeeded: ApprovalNeeded[];
  activation: string;
};

// ---- list_presets (tool 41): the presets and overlays this product release ships ----

export type ListPresetsInput = Record<string, never>;

export interface PresetList {
  presets: {
    presetKey: Key;
    label: string;
    presetKind: 'full' | 'overlay';
    extends: Key | null;
    conflictsWith: Key[];
    productRelease: string;
    summary: {
      tiers: LabelledKey[];
      gateLevelsByTierAndStage: { tierKey: Key; targetStageKey: Key; gateLevel: GateLevel }[];
      changeManagementMode: string | null;
      approvalsSummary: string;
    };
  }[];
}

// ---- get_configuration_health (tool 51): the version in force, the engine's snapshot, findings ----

export type GetConfigurationHealthInput = {
  includeHistory?: boolean;
  includeDrafts?: boolean;
  top?: number;
  skipToken?: string;
};

export interface ConfigurationHealth {
  active: { version: number; activatedOn: IsoDateTime; contentHash: string; decisionId: Id | null } | null;
  engine: { engineVersion: string; snapshotSchemaVersion: string; snapshotHash: string | null; loaded: boolean };
  versions?: { version: number; status: 'active' | 'superseded'; activatedOn: IsoDateTime; decisionId: Id | null; summary: string }[];
  drafts?: { configurationDraftId: Id; status: StatusMeaning; baseVersion: number; submittedOn: IsoDateTime | null; expiresOn: IsoDateTime | null; isUpgradeDraft: boolean }[];
  findings: { ruleKey: string; message: string }[];
  reviewDueOn: IsoDate | null;
  skipToken?: string;
}

// ---- export_configuration (tool 43; configuration admin, platform admin, auditor): the files of a version ----

export type ExportConfigurationInput = { version?: number };

export interface ConfigurationExport {
  configurationSet: { key: Key; version: number };
  // Untrusted text: the content of each file.
  files: { path: string; content: string }[];
  contentHash: string;
}

// ---- check_role_bindings (tool 44, platform admin): each role's group, the workload grants, direct assignments ----

// A read that logs the check (an audit entry).
export type CheckRoleBindingsInput = { includeDirectAssignments?: boolean };

export interface RoleBindingCheck {
  bindings: {
    roleKey: Key;
    group: { objectId: Guid; displayLabel: string } | null;
    bindingMethod: 'dataverse-group-team' | 'entra-app-role-assignment' | null;
    groupExists: boolean;
    lastVerifiedOn: IsoDateTime | null;
  }[];
  workloadGrants: {
    principal: PrincipalRef;
    principalKind: PrincipalKind;
    roleKey: Key;
    reason: string;
    decisionId: Id | null;
    expiresOn: IsoDate;
    assignmentFound: boolean;
    isExpired: boolean;
  }[];
  directAssignments: { principal: PrincipalRef; principalKind: PrincipalKind; roleKey: Key; where: string; coveredByGrant: boolean }[];
  findings: string[];
  auditEntryId: Id | null;
}

// ---- Teams and committees (document 24, B24-20): tools 95 and 96 ----

// A team's kinds, derived on every read and never stored. committee: bound to a product role that answers decisions;
// support-team: holds a current support owner or technical owner assignment; role-group: any other bound group; other:
// none of these. A team can be more than one kind (other only alone).
export type TeamKind = 'committee' | 'support-team' | 'role-group' | 'other';

// Where a team's members come from: Entra, read live as the signed-in person and never stored; the demo personas; or
// nowhere, when membership is managed in Entra and this install does not read it.
export type MembersReadFrom = 'entra-live' | 'demo-personas' | 'not-read';

// ---- The data rows across domains (document 24, B24-21): tools 97 and 98 ----

export interface ListDataUsesInput {
  domainKey?: Key;
  includeSubtree?: boolean;
  businessApplicationId?: Id;
  dataUseIds?: Id[];
  states?: DataUseState[];
  reachesAi?: boolean;
  accessKeys?: DataAccess[];
  text?: string;
  includeEnded?: boolean;
  top?: number;
  skipToken?: string;
}

export interface DataUseList {
  items: DataUseItem[];
  counts: { all: number; byState: Record<DataUseState, number> };
  totalCount: number;
  skipToken?: string | null;
}

export interface ListDataMappingsInput {
  domainKey?: Key;
  includeSubtree?: boolean;
  dataLocationId?: Id;
  dataMappingIds?: Id[];
  originKeys?: DataOrigin[];
  copyStates?: DataUseState[];
  text?: string;
  includeEnded?: boolean;
  top?: number;
  skipToken?: string;
}

export interface DataMappingList {
  items: DataMappingItem[];
  counts: { all: number; copies: number };
  totalCount: number;
  skipToken?: string | null;
}

// The new filters of the reads the original pages called with loose inputs (wave 4, B24-21): find_applications takes
// usesAi and buildPathKeys; get_change_schedule and get_deployments take portfolioId (child portfolios included).
export interface FindApplicationsFilters {
  usesAi?: boolean;
  buildPathKeys?: Key[];
}

export interface PortfolioFilter {
  portfolioId?: Id;
}

// The whole inputs of those reads, typed from wave 4 on (the wave 4 scaffold), so a page that sends the new filters
// gets a type error for a misspelled field. Each follows its input schema in src/mcp/contracts/tools.
export interface FindApplicationsInput extends FindApplicationsFilters {
  text?: string;
  portfolioId?: Id;
  applicationKindKeys?: Key[];
  tierKeys?: Key[];
  lifecycleStageKeys?: Key[];
  ownerObjectId?: Guid;
  registrationMeanings?: Array<'draft' | 'registered' | 'archived'>;
  top?: number;
  skipToken?: string;
}

export interface GetChangeScheduleInput extends PortfolioFilter {
  from: IsoDate;
  to: IsoDate;
  deploymentTargetIds?: Id[];
  targetStageKeys?: Key[];
  businessApplicationId?: Id;
  statusMeanings?: RecordStatusMeaning[];
  top?: number;
  skipToken?: string;
}

export interface GetDeploymentsInput extends PortfolioFilter {
  businessApplicationId?: Id;
  deploymentTargetId?: Id;
  changeRequestId?: Id;
  latestOnly?: boolean;
  flaggedOnly?: boolean;
  flagKeys?: DeploymentFlagKey[];
  resultMeanings?: DeploymentResultMeaning[];
  from?: IsoDate;
  to?: IsoDate;
  top?: number;
  skipToken?: string;
}

// Exactly one start: an application, a technical component, or (wave 4, B24-22) a data location.
export type GetDependencyImpactInput = (
  | { businessApplicationId: Id; technicalComponentId?: never; dataLocationId?: never }
  | { technicalComponentId: Id; businessApplicationId?: never; dataLocationId?: never }
  | { dataLocationId: Id; businessApplicationId?: never; technicalComponentId?: never }
) & {
  direction?: 'dependants' | 'dependencies' | 'both';
  depth?: number;
  dependencyTypeKeys?: Key[];
  includeRetired?: boolean;
};

export interface ListTeamsInput {
  kind?: TeamKind;
  text?: string;
  // The group a decision page says it waits on (askedRole.key).
  roleKey?: Key;
  // The person page's "member of".
  memberObjectId?: Guid;
  top?: number;
  skipToken?: string;
}

export interface TeamListItem {
  // The group's Entra object ID; the team page is /app/teams/<teamId>.
  teamId: Guid;
  team: { objectId: Guid; displayLabel: string };
  kinds: TeamKind[];
  roles: LabelledKey[];
  directoryState: DirectoryState | null;
  // Null when the members are not read.
  memberCount: number | null;
  waitingCount: number;
  applicationCount: number;
  assignmentCount: number;
  ownerGapCount: number;
  recordUrl: string;
}

export interface TeamList {
  items: TeamListItem[];
  // Under the text, role and member filters, never the kind filter, so each count opens its rows.
  counts: { all: number; committee: number; supportTeam: number; roleGroup: number; other: number };
  membersReadFrom: MembersReadFrom;
  membership: string | null;
  totalCount: number;
  skipToken?: string;
}

export interface GetTeamInput {
  teamId: Guid;
}

// One approval request on a team page: waiting on the team now, or answered through it.
export interface TeamAsk {
  approvalRecordId: Id;
  decision: { decisionId: Id; recordNumber: string | null; decisionType: LabelledKey; statement: string; status: StatusValue };
  subject: { subjectKind: DecisionSubjectKind; label: string; businessApplicationId: Id | null; applicationName: string | null };
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  isConsultation: boolean;
  outcome: StatusValue;
  approver: PrincipalRef | null;
  requestedOn: IsoDateTime | null;
  respondedOn: IsoDateTime | null;
  expiresOn: IsoDateTime | null;
  isExpired: boolean;
  recordUrl: string;
}

export interface TeamAssignment {
  ownerAssignmentId: Id;
  ownerRole: LabelledKey;
  isDeputy: boolean;
  startOn: IsoDate | null;
}

export interface TeamDetail {
  team: { objectId: Guid; displayLabel: string };
  kinds: TeamKind[];
  directoryState: DirectoryState | null;
  isKnownToProduct: boolean;
  // Null when the members are not read (directory reads off, or the directory did not answer).
  members: Array<{ objectId: Guid; displayLabel: string; principalKind: PrincipalKind }> | null;
  membersReadFrom: MembersReadFrom;
  membership: string;
  roles: Array<{
    role: LabelledKey;
    party: LabelledKey | null;
    duties: string | null;
    answersDecisions: boolean;
    binding: { bindingKey: Key; bindingMethod: 'dataverse-group-team' | 'entra-app-role-assignment' | null; lastVerifiedOn: IsoDateTime | null };
    decisionRights: Array<{ decisionRight: LabelledKey; decisionType: LabelledKey; letter: string; scope: string }>;
    approvals: Array<{ approvalRule: LabelledKey; decisionType: LabelledKey; isConsultation: boolean; scope: string; guidelineRef: GuidelineRef | null }>;
  }>;
  // throughOwnerRole: its people answer the type as named holders of that owner role on their own data (the data owner).
  decisionTypes: Array<{ decisionType: LabelledKey; letters: string[]; approves: boolean; consulted: boolean; roleKeys: Key[]; throughOwnerRole?: LabelledKey }>;
  applications: Array<{
    businessApplicationId: Id;
    name: string;
    recordNumber: string | null;
    tier: LabelledKey | null;
    stage: LabelledKey | null;
    registration: 'draft' | 'registered' | 'archived' | null;
    assignments: TeamAssignment[];
    ownerGapCount: number;
    recordUrl: string;
  }>;
  dataAssignments: Array<TeamAssignment & { subjectKind: 'data-domain' | 'data-location'; subjectId: string; label: string; recordUrl: string }>;
  ownerGaps: Array<{
    businessApplicationId: Id;
    applicationName: string;
    ownerRole: LabelledKey;
    missing: 'holder' | 'deputy';
    holderDirectoryState: DirectoryState | null;
    recordUrl: string;
  }>;
  waiting: TeamAsk[];
  // Requests asked of one of its members, by name or as the holder of an owner role (product review, fix round 1).
  waitingOnMembers: Array<{ member: PrincipalRef; request: TeamAsk }>;
  // What its members own in their own name, applications and data (up to 200).
  membersOwn: Array<{ member: PrincipalRef; ownerRole: LabelledKey; isDeputy: boolean; subjectKind: 'business-application' | 'data-domain' | 'data-location'; subjectId: string; label: string; recordUrl: string }>;
  recentAnswers: TeamAsk[];
  counts: { members: number | null; roles: number; decisionTypes: number; applications: number; assignments: number; waiting: number; waitingOnMembers: number; membersOwn: number; answered: number; ownerGaps: number };
}

// =====================================================================================================================
// ---- Tools 99 to 102 (document 24, B24-22) ----

// The slide-back signals a decision may name (playbook chapter 17), in their order.
export type SlideBackSignalKey =
  | 'changes-wait-for-a-meeting' | 'environments-by-ticket' | 'gap-check-stale' | 'registered-late' | 'configuration-not-kept'
  | 'unsanctioned-ai' | 'tool-rebuilt' | 'no-human-approver' | 'documents-slipping' | 'access-drift';

// One approval request: who it asks and when it expires.
export interface ApprovalRequestBrief {
  approvalRecordId: Id;
  askedRole: LabelledKey | null;
  askedOwnerRole: LabelledKey | null;
  askedPerson: PrincipalRef | null;
  isConsultation: boolean;
  requestedOn: IsoDateTime | null;
  expiresOn: IsoDateTime | null;
}

// 99. import_data_taxonomy: a CSV or a catalog's rows become a configuration draft of data domains.
export type ImportDataTaxonomyInput = WriteControls & {
  csv?: string;
  rows?: Array<Record<string, string | number | boolean | null>>;
  source?: 'csv' | 'fabric' | 'purview';
  baseVersionNumber?: number;
  proposalRationale: string;
};

// The write result is the configuration draft the import saved (recordId); a check (validateOnly) saves none.
export type ImportDataTaxonomyResult = WriteResult & {
  configurationDraftId: Id | null;
  valid: boolean;
  rowsRead: number;
  counts: { domainsAdded: number; domainsChanged: number; domainsUnchanged: number; matchesAdded: number; notInSource: number };
  problems: Array<{ row: number; column: string | null; message: string }>;
  errors: Array<{ entity: string | null; key: string | null; ruleKey: string; message: string }>;
  diffSummary: ConfigurationDiffEntry[];
  notInSource: Array<{ key: Key; path: string }>;
  preparedOwnerChanges: Array<{
    domainKey: Key;
    ownerRoleKey: Key;
    isDeputy: boolean;
    objectId: string | null;
    userPrincipalName: string | null;
    readyToSend: boolean;
    tool: string;
    input: Record<string, unknown> | null;
    note: string;
  }>;
  catalogOwnerDifferences: Array<{ check: 'DC-15'; domainKey: Key; ownerRoleKey: Key; catalogObjectId: string; currentHolder: PrincipalRef | null; message: string }>;
};

// 100. renew_approval_request: "Ask again" (person only).
export type RenewApprovalRequestInput = WriteControls & { approvalRecordId: Id };

export interface RenewApprovalRequestResult {
  approvalRecordId: Id;
  renewed: boolean;
  previousOutcome: ApprovalOutcomeMeaning | null;
  decision: { decisionId: Id; recordNumber: string | null; statement: string; decisionType: LabelledKey; status: StatusValue | null; recordUrl: string };
  request: ApprovalRequestBrief | null;
  openedRecordIds: Id[];
  status: StatusValue | null;
  performedBy: PerformedBy;
  dryRun: boolean;
}

// 101. retry_approval_card: the platform admin sends a stuck card again (person only).
export type RetryApprovalCardInput = WriteControls & { approvalRecordId: Id };

export type ApprovalCardState = 'pending' | 'claimed' | 'sent' | 'answered' | 'expired' | 'cancelled' | 'failed';

export interface RetryApprovalCardResult {
  approvalRecordId: Id;
  decision: { decisionId: Id; recordNumber: string | null; statement: string; recordUrl: string } | null;
  action: 'sent-again' | 'closed';
  stateBefore: ApprovalCardState;
  stateAfter: ApprovalCardState;
  attemptsBefore: number;
  performedBy: PerformedBy;
  dryRun: boolean;
}

// 102. get_demo_freshness: on a demo install, the story requests that expired or expire soon.
export interface GetDemoFreshnessInput {
  withinHours?: number;
}

export interface DemoFreshness {
  withinHours: number;
  counts: { expired: number; expiresSoon: number };
  needsRefresh: boolean;
  items: Array<{
    approvalRecordId: Id;
    state: 'expired' | 'expires-soon';
    hoursLeft: number;
    decision: { decisionId: Id; recordNumber: string | null; statement: string; decisionType: LabelledKey; recordUrl: string };
    application: { businessApplicationId: Id; name: string } | null;
    request: ApprovalRequestBrief;
  }>;
  refreshWith: string;
  renewTool: string;
}

// The tool names the pages call, with their input and data types
// =====================================================================================================================

export interface ReadToolData {
  find_applications: FoundApplications;
  get_portfolio_summary: PortfolioSummary;
  get_deployments: Deployments;
  get_change_schedule: ChangeSchedule;
  get_application_card: ApplicationCard;
  get_application_owners: ApplicationOwners;
  get_dependency_impact: DependencyImpact;
  get_configuration: ConfigurationRows;
  get_my_queue: MyQueue;
  get_decision: DecisionDetail;
  list_approvals: ApprovalList;
  get_approval_status: ApprovalStatus;
  get_decision_log: DecisionLog;
  list_intake_requests: IntakeList;
  get_intake_request: IntakeDetail;
  get_tier_questions: TierQuestions;
  check_duplicates: DuplicateMatches;
  get_requirements: Requirements;
  list_reviews: ReviewList;
  get_review: ReviewDetail;
  list_portfolios: PortfolioList;
  list_owner_assignments: OwnerAssignmentList;
  get_change_request: ChangeRequestDetail;
  check_standard_change: StandardChangeCheck;
  get_documentation_status: DocumentationStatus;
  list_claims: ClaimList;
  find_people: PeopleFound;
  get_audit_trail: AuditTrail;
  get_evidence: EvidencePack;
  get_ai_activity: AiActivity;
  get_tier_assessment: TierAssessmentDetail;
  list_documents: DocumentList;
  list_deployment_targets: DeploymentTargetList;
  list_gap_check_runs: GapCheckRunList;
  whoami: WhoAmI;
  find_records: FoundRecords;
  get_role_holders: RoleHolders;
  get_pipeline_map: PipelineMap;
  explain_rules: RulesExplained;
  list_configuration_changes: ConfigurationChanges;
  get_configuration_draft: ConfigurationDraftDetail;
  get_role_access: RoleAccess;
  list_guidelines: GuidelineList;
  get_measures: Measures;
  get_admin_status: AdminStatus;
  list_agents: AgentList;
  // ---- wave 3 ----
  find_data_domains: DataDomainList;
  get_data_domain: DataDomainDetail;
  find_data_locations: DataLocationList;
  resolve_data: DataResolved;
  check_data_use: DataUseCheck;
  get_data_gaps: DataGaps;
  list_presets: PresetList;
  get_configuration_health: ConfigurationHealth;
  export_configuration: ConfigurationExport;
  check_role_bindings: RoleBindingCheck;
  // ---- wave 4 ----
  list_teams: TeamList;
  get_team: TeamDetail;
  list_data_uses: DataUseList;
  list_data_mappings: DataMappingList;
  get_demo_freshness: DemoFreshness;
}

export interface WriteToolData {
  answer_approval: ApprovalAnswerResult;
  save_intake_draft: IntakeDraftResult;
  score_intake_request: ScoreIntakeResult;
  answer_tier_questions: TierAnswersResult;
  submit_record: SubmitResult;
  withdraw_record: WithdrawResult;
  save_decision_proposal: DecisionProposalResult;
  propose_owner_change: OwnerChangeResult;
  record_owner_leave: OwnerLeaveResult;
  save_change_request_draft: ChangeDraftResult;
  record_change_result: ChangeResultResult;
  complete_change: CompleteChangeResult;
  complete_review: CompleteReviewResult;
  record_document_link: DocumentLinkResult;
  confirm_document_checked: DocumentCheckedResult;
  claim_resource: ClaimResult;
  dismiss_claim: DismissClaimResult;
  save_link: SaveLinkResult;
  save_deployment_target: SaveDeploymentTargetResult;
  save_dependency: SaveDependencyResult;
  save_portfolio: SavePortfolioResult;
  record_deployment: RecordDeploymentResult;
  record_deployment_correction: DeploymentCorrectionResult;
  map_pipeline: MapPipelineResult;
  run_gap_check: GapRunResult;
  compare_inventory_list: GapRunResult;
  run_owner_check: OwnerCheckResult;
  link_component: LinkComponentResult;
  request_architecture_review: ArchitectureReviewResult;
  request_security_review: SecurityReviewResult;
  save_application_draft: ApplicationDraftResult;
  // ---- wave 3 ----
  save_data_use: SaveDataUseResult;
  save_data_location: SaveDataLocationResult;
  save_data_mapping: SaveDataMappingResult;
  move_data_references: MoveDataReferencesResult;
  run_due_state_refresh: DueStateRefreshResult;
  propose_configuration_change: ConfigurationChangeResult;
  import_data_taxonomy: ImportDataTaxonomyResult;
  renew_approval_request: RenewApprovalRequestResult;
  retry_approval_card: RetryApprovalCardResult;
}

export interface ToolData extends ReadToolData, WriteToolData {}

export type ToolName = keyof ToolData;
export type WriteToolName = keyof WriteToolData;

// The typed inputs. The other reads of the four original pages pass loose inputs (Record<string, unknown>), as before;
// find_applications, get_change_schedule, get_deployments and get_dependency_impact are typed from wave 4 on, for
// useRead. useTool and usePagedRead still take any object, so the existing calls stay as they are.
export interface WriteToolInput {
  answer_approval: AnswerApprovalInput;
  save_intake_draft: SaveIntakeDraftInput;
  score_intake_request: ScoreIntakeRequestInput;
  answer_tier_questions: AnswerTierQuestionsInput;
  submit_record: SubmitRecordInput;
  withdraw_record: WithdrawRecordInput;
  save_decision_proposal: SaveDecisionProposalInput;
  propose_owner_change: ProposeOwnerChangeInput;
  record_owner_leave: RecordOwnerLeaveInput;
  save_change_request_draft: SaveChangeRequestDraftInput;
  record_change_result: RecordChangeResultInput;
  complete_change: CompleteChangeInput;
  complete_review: CompleteReviewInput;
  record_document_link: RecordDocumentLinkInput;
  confirm_document_checked: ConfirmDocumentCheckedInput;
  claim_resource: ClaimResourceInput;
  dismiss_claim: DismissClaimInput;
  save_link: SaveLinkInput;
  save_deployment_target: SaveDeploymentTargetInput;
  save_dependency: SaveDependencyInput;
  save_portfolio: SavePortfolioInput;
  record_deployment: RecordDeploymentInput;
  record_deployment_correction: RecordDeploymentCorrectionInput;
  map_pipeline: MapPipelineInput;
  run_gap_check: RunGapCheckInput;
  compare_inventory_list: CompareInventoryListInput;
  run_owner_check: RunOwnerCheckInput;
  link_component: LinkComponentInput;
  request_architecture_review: RequestArchitectureReviewInput;
  request_security_review: RequestSecurityReviewInput;
  save_application_draft: SaveApplicationDraftInput;
  // ---- wave 3 ----
  save_data_use: SaveDataUseInput;
  save_data_location: SaveDataLocationInput;
  save_data_mapping: SaveDataMappingInput;
  move_data_references: MoveDataReferencesInput;
  run_due_state_refresh: RunDueStateRefreshInput;
  propose_configuration_change: ProposeConfigurationChangeInput;
  import_data_taxonomy: ImportDataTaxonomyInput;
  renew_approval_request: RenewApprovalRequestInput;
  retry_approval_card: RetryApprovalCardInput;
}

export interface ReadToolInput {
  get_my_queue: GetMyQueueInput;
  get_decision: GetDecisionInput;
  list_approvals: ListApprovalsInput;
  get_approval_status: GetApprovalStatusInput;
  get_decision_log: GetDecisionLogInput;
  list_intake_requests: ListIntakeRequestsInput;
  get_intake_request: GetIntakeRequestInput;
  get_tier_questions: GetTierQuestionsInput;
  check_duplicates: CheckDuplicatesInput;
  get_requirements: GetRequirementsInput;
  list_reviews: ListReviewsInput;
  get_review: GetReviewInput;
  list_portfolios: ListPortfoliosInput;
  list_owner_assignments: ListOwnerAssignmentsInput;
  get_change_request: GetChangeRequestInput;
  check_standard_change: CheckStandardChangeInput;
  get_documentation_status: GetDocumentationStatusInput;
  list_claims: ListClaimsInput;
  find_people: FindPeopleInput;
  get_audit_trail: GetAuditTrailInput;
  get_evidence: GetEvidenceInput;
  get_ai_activity: GetAiActivityInput;
  get_tier_assessment: GetTierAssessmentInput;
  list_documents: ListDocumentsInput;
  list_deployment_targets: ListDeploymentTargetsInput;
  list_gap_check_runs: ListGapCheckRunsInput;
  whoami: WhoAmIInput;
  find_records: FindRecordsInput;
  get_role_holders: GetRoleHoldersInput;
  get_pipeline_map: GetPipelineMapInput;
  explain_rules: ExplainRulesInput;
  list_configuration_changes: ListConfigurationChangesInput;
  get_configuration_draft: GetConfigurationDraftInput;
  get_role_access: GetRoleAccessInput;
  list_guidelines: ListGuidelinesInput;
  get_measures: GetMeasuresInput;
  get_admin_status: GetAdminStatusInput;
  list_agents: ListAgentsInput;
  // ---- wave 3 ----
  find_data_domains: FindDataDomainsInput;
  get_data_domain: GetDataDomainInput;
  find_data_locations: FindDataLocationsInput;
  resolve_data: ResolveDataInput;
  check_data_use: CheckDataUseInput;
  get_data_gaps: GetDataGapsInput;
  list_presets: ListPresetsInput;
  get_configuration_health: GetConfigurationHealthInput;
  export_configuration: ExportConfigurationInput;
  check_role_bindings: CheckRoleBindingsInput;
  // ---- wave 4 ----
  list_teams: ListTeamsInput;
  get_team: GetTeamInput;
  list_data_uses: ListDataUsesInput;
  list_data_mappings: ListDataMappingsInput;
  get_demo_freshness: GetDemoFreshnessInput;
  // The original pages' reads whose inputs gained filters (B24-21, B24-22), typed from wave 4 on.
  find_applications: FindApplicationsInput;
  get_change_schedule: GetChangeScheduleInput;
  get_deployments: GetDeploymentsInput;
  get_dependency_impact: GetDependencyImpactInput;
}

// The read tools with a typed input.
export type ReadToolName = keyof ReadToolInput;

// The input of a tool: typed where this file types it, else a loose object.
export type ToolInput<N extends ToolName> = N extends keyof WriteToolInput
  ? WriteToolInput[N]
  : N extends keyof ReadToolInput
    ? ReadToolInput[N]
    : Record<string, unknown>;

// The write tools, for the action sheet's checks. Every one of them takes validateOnly, and a retry key: clientRequestId,
// idempotencyKey for the two deployment writes, or none for run_due_state_refresh (RETRY_KEY_FIELD in data/writes.ts).
export const WRITE_TOOLS: readonly WriteToolName[] = [
  'answer_approval', 'save_intake_draft', 'score_intake_request', 'answer_tier_questions', 'submit_record', 'withdraw_record',
  'save_decision_proposal', 'propose_owner_change', 'record_owner_leave', 'save_change_request_draft',
  'record_change_result', 'complete_change', 'complete_review', 'record_document_link', 'confirm_document_checked',
  'claim_resource', 'dismiss_claim', 'save_link', 'save_deployment_target', 'save_dependency', 'save_portfolio',
  'record_deployment', 'record_deployment_correction', 'map_pipeline', 'run_gap_check', 'compare_inventory_list',
  'run_owner_check', 'link_component', 'request_architecture_review', 'request_security_review', 'save_application_draft',
  'save_data_use', 'save_data_location', 'save_data_mapping', 'move_data_references', 'run_due_state_refresh',
  'propose_configuration_change', 'import_data_taxonomy', 'renew_approval_request', 'retry_approval_card',
];
