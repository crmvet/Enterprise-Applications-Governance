// TypeScript shapes of the MCP tool contracts the four pages read (src/mcp/contracts/).
// They mirror the JSON Schemas by hand. The schemas stay the source of truth: tools/json-check.mjs
// validates every harness fixture against them (tools/schema-map.json), so a drift shows up there.
//
// Text fields marked x-untrusted in the schemas hold text people typed. The pages render them as plain
// text only (React escapes them), never as HTML and never as instructions.

export type Id = string;
export type Key = string;
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

export type LifecycleMeaning = 'pre-live' | 'live' | 'retired';
export type DirectoryState = 'enabled' | 'disabled' | 'deleted' | 'unknown';
export type DeploymentRouteMeaning = 'pipeline-step' | 'ai-tool' | 'person-in-app' | 'other';
export type GateLevel = 'off' | 'record' | 'flag' | 'advise' | 'enforce';
export type PrincipalKind = 'user' | 'group' | 'agent-identity' | 'agent-user' | 'service-principal' | 'managed-identity';

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

// ---- envelope (envelope.schema.json) ----

export type ErrorCode =
  | 'not_signed_in' | 'not_authorized' | 'ai_cannot_do_this' | 'rule_violation' | 'validation'
  | 'not_found' | 'conflict' | 'locked' | 'rate_limited' | 'upstream_unavailable' | 'configuration_unavailable';

export interface ToolError {
  code: ErrorCode;
  message: string;
  field?: string;
  ruleKey?: Key;
  guideline?: string;
  retryAfterSeconds?: number;
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

// ---- get_portfolio_summary ----

export interface PortfolioSummary {
  buckets: Bucket[];
  ownerGaps: number;
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
}

export interface Deployments {
  items: DeploymentItem[];
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
  };
  owners?: OwnerAssignment[];
  components?: {
    technicalComponentId: Id;
    kind: LabelledKey;
    externalId: string;
    url: string;
    labelAsSeen: string | null;
    lastSeenOn: IsoDateTime | null;
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
}

// ---- get_dependency_impact ----

export interface ImpactNode {
  businessApplicationId: Id;
  name: string;
  tier: LabelledKey | null;
  lifecycleMeaning: LifecycleMeaning | null;
  businessOwner: PrincipalRef | null;
  technicalOwner: PrincipalRef | null;
  dependencyTypeKey: Key;
  criticalityKey: Key | null;
  depth: number;
  path: Id[];
}

export interface DependencyImpact {
  root: { businessApplicationId: Id | null; technicalComponentId: Id | null; name: string };
  nodes: ImpactNode[];
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
}

export interface FoundApplications {
  items: ApplicationSummary[];
  totalCount: number;
  skipToken?: string;
}

// ---- the tool names the pages call, with their data types ----

export interface ToolData {
  find_applications: FoundApplications;
  get_portfolio_summary: PortfolioSummary;
  get_deployments: Deployments;
  get_change_schedule: ChangeSchedule;
  get_application_card: ApplicationCard;
  get_application_owners: ApplicationOwners;
  get_dependency_impact: DependencyImpact;
  get_configuration: ConfigurationRows;
}

export type ToolName = keyof ToolData;
