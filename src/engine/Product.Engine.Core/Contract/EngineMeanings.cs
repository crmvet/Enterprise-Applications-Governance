// The fixed meanings of the engine contract (document 20, section 2.4).
// A client can rename, add or hide labels, but every label maps to one of these meanings.
// The engine reads only meanings, never labels (document 25, rule 2).
//
// WORKING DEFAULT (open design question 1)
// Open design question 1 asks if a short list of fixed engine meanings is acceptable under "never hardcoded"
// (D-06, D-10). The recommended answer is yes: about 20 fixed meanings, all labels configurable.
// If the answer is no, these lists become configuration and the rules need a rule language (document 25, section 15).
//
// Wire values live in EngineContract. Enum names are the design's words in PascalCase.
// Never reorder or renumber members. Add new members at the end.

namespace Product.Engine.Core.Contract;

// C05 Status for O09 Decision. Only accepted decisions satisfy gates. Final decisions are append-only.
public enum DecisionStatusMeaning
{
    Proposed,
    Accepted,
    Rejected,
    Superseded,
    Withdrawn,
}

// C05 Status for O07 Intake Request and O11 Change Request. AI-created rows start as draft (D-02).
public enum RecordStatusMeaning
{
    Draft,
    Submitted,
    InProgress,
    Completed,
    Rejected,
    Withdrawn,
    Cancelled,
}

// C05 Status for O02 Business Application. An AI-registered application stays draft until a person confirms it.
public enum RegistrationStatusMeaning
{
    Draft,
    Registered,
    Archived,
}

// C05 Status for O10 Approval Record. Only approved outcomes from a person count (D-02).
public enum ApprovalOutcomeMeaning
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Cancelled,
    RecommendationOnly,
}

// C05 Status for O19 Document Link. Missing is computed only and never stored on a row.
public enum DocumentStatusMeaning
{
    Draft,
    Current,
    Stale,
    Missing,
}

// C05 Status for O23 Claim. The claim list shows open items only.
public enum ClaimStatusMeaning
{
    Open,
    Claimed,
    Dismissed,
}

// C05 Status for O14 Deployment Record. Not deployed covers abandoned runs.
public enum DeploymentResultMeaning
{
    Succeeded,
    PartlySucceeded,
    Failed,
    NotDeployed,
}

// C05 Status for O21 Review. Reminders and escalation read it.
public enum ReviewStatusMeaning
{
    Due,
    Done,
    Cancelled,
}

// C12 Lifecycle Stage. Reviews run for live applications. Retired ones leave the claim comparison.
public enum LifecycleMeaning
{
    PreLive,
    Live,
    Retired,
}

// C33 Gate Policy. The ladder of D-03, from no effect to a stop. Members are in ladder order.
public enum GateLevel
{
    Off,
    Record,
    Flag,
    Advise,
    Enforce,
}

// C33 Gate Policy. What a check step does at enforce when the product cannot answer (D-03, open).
public enum FailMode
{
    FailOpen,
    FailClosed,
}

// C32 Approval Rule. Mirrors the Power Automate approval types.
public enum ApprovalMode
{
    EveryoneMustApprove,
    FirstToRespond,
    Sequential,
}

// C31 Decision Right. Exactly one accountable per row group. AI can only support.
public enum DecisionRightLetter
{
    Responsible,
    Accountable,
    Consulted,
    Informed,
    Supports,
}

// C38 Actor Kind. Who or what performed a write. Only a person can approve (D-02, D-10).
// The approver kind of document 20, section 1.3 is this list too: the actor kind of whoever answered.
// "Approval runner" replaces the earlier "approval flow" (28, issue 7).
public enum ActorKindMeaning
{
    Person,
    AiClientForPerson,
    AgentIdentity,
    Pipeline,
    ApprovalRunner,
    Engine,
}

// O05 Principal Reference. The directory object type. Only a user can approve. Roles bind only to groups (D-11).
public enum PrincipalKind
{
    User,
    Group,
    AgentIdentity,
    AgentUser,
    ServicePrincipal,
    ManagedIdentity,
}

// C36 Deployment Route. The other-path flag compares the route with the change path (D-19).
public enum DeploymentRouteMeaning
{
    PipelineStep,
    AiTool,
    PersonInApp,
    Other,
}

// C37 Link Kind. Each link entity accepts only its category.
public enum LinkCategory
{
    Delivery,
    Pipeline,
    Document,
    Evidence,
    MicrosoftResource,
    ExternalChange,
}

// C04 Setting change-management-mode. Linked mode stores the external reference (D-08).
public enum ChangeManagementMode
{
    BuiltIn,
    Linked,
}

// O23 Claim. Different actions on the claim list (D-15).
public enum FindingKind
{
    Unregistered,
    Vanished,
}

// C14 Gate Requirement. How the engine tests a gate requirement.
// OwnerAssigned is the value document 21, section 13.4 adds. Document 20 lists the first three.
public enum RequirementKind
{
    DecisionAccepted,
    DocumentCurrent,
    ReviewDone,
    OwnerAssigned,
}

// C01 Configuration Set. Presets are read-only templates (D-07).
public enum ConfigurationSetKind
{
    Preset,
    Working,
}

// C03 Preset. One full preset plus declared overlays, validated as one set (28, Q3).
public enum PresetKind
{
    Full,
    Overlay,
}

// C46 Configuration Draft. Only a submitted draft can be accepted. Only the engine activates (28, issue 3).
public enum DraftStatusMeaning
{
    Draft,
    Submitted,
    Activated,
    Discarded,
}

// C46, C02 and overlays. Rows are never deleted. A retired row stays so old records resolve.
public enum PatchOperation
{
    Upsert,
    Retire,
}

// C40 Separation Rule. How the engine tests separation of duties (28, issue 5).
public enum SeparationRuleKind
{
    ApproverDiffers,
    RolesInDifferentGroups,
}

// C40 Separation Rule. Who the approver must differ from.
// InstructingAdmin carries the second admin rule of the strict separation overlay.
// WORKING DEFAULT (open design question 4): the instructing admin may accept their own AI's draft,
// and only a C40 row with this subject asks for a second admin.
public enum SeparationSubject
{
    Proposer,
    InstructingAdmin,
    Deployer,
    OwnerRoleHolder,
    RoleHolder,
}

// C40 Separation Rule. What the engine does when the rule is broken.
public enum SeparationEnforcement
{
    Refuse,
    ExceptionDecision,
    Flag,
}

// C33 Gate Policy and the setting default-ai-only-code-approval-handling.
// What a deployment gets when its only code approval came from an AI.
// WORKING DEFAULT (open design question 7): AI-only code approvals never count as authorization,
// and the shipped default is Flag.
public enum AiOnlyCodeApprovalHandling
{
    Ignore,
    Flag,
    Stop,
}

// Every configuration row. Upgrades never overwrite local rows.
public enum Origin
{
    Shipped,
    Local,
}
