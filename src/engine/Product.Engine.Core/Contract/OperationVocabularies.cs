// Fixed words in the operation records of document 25, section 5.
// These are not engine meanings: no label maps to them and clients never configure them.
// They are part of the API contract, so they have one wire value each, like the meanings.
//
// Never reorder or renumber members. Add new members at the end.

namespace Product.Engine.Core.Contract;

// Reason severity (document 25, section 5.1, Reason).
public enum ReasonSeverity
{
    Info,
    Warning,
    Error,
}

// What a configuration row did in a rule trace (document 25, section 5.1, RuleTrace).
// Dropped means a broader group that lost to a more specific one.
public enum RuleTraceRole
{
    Matched,
    Won,
    Dropped,
    Required,
    Skipped,
}

// The operation of a guarded write (document 25, OP-08, WriteRequest).
public enum WriteOperation
{
    Create,
    Update,
    Delete,
}

// The moment of a gate decision (document 25, OP-04). Check is before the deploy. Record is after it.
public enum GateMode
{
    Check,
    Record,
}

// The verdict of a gate decision (document 25, OP-04, GateDecision).
public enum GateVerdict
{
    NotApplicable,
    Allow,
    Warn,
    Stop,
    AllowWithException,
}

// The state of a moment against a change window (document 25, OP-05).
public enum WindowState
{
    Inside,
    Before,
    After,
    NoWindow,
}

// Who approved the code merge, as the record step reports it (document 25, OP-04). Evidence only, never counted.
public enum CodeApprovalKind
{
    Person,
    AiOnly,
    None,
    Unknown,
}

// Where a required approval comes from (document 25, OP-02, RequiredApproval).
public enum ApprovalRequirementSource
{
    ApprovalRule,
    Accountable,
}

// The kind of a follow-on write (document 25, OP-08, Effect).
public enum EffectKind
{
    UpdateSubject,
    SupersedeDecision,
    AcceptDecision,
    ActivateConfiguration,
    RecomputeApprovals,
    WriteAuditEntry,
}

// The overall state of a decision's approvals (document 25, OP-03, ApprovalStatusResult).
public enum ApprovalOverall
{
    Pending,
    Satisfied,
    Rejected,
    Expired,
}

// The state an authorization fact reports (document 25, OP-14, AuthorizationFact).
public enum AuthorizationState
{
    Authorized,
    NotAuthorized,
    Cancelled,
}

// What decided a linked-mode authorization (document 25, OP-14, LinkedAuthorizationResult).
public enum AuthorizationBasis
{
    ItsmStep,
    Attestation,
    None,
}

// The direction of an impact walk (document 25, OP-10).
public enum ImpactDirection
{
    Dependants,
    Dependencies,
    Both,
}

// A reminder action for today (document 25, OP-11). Escalate goes to the deputy.
public enum ReminderAction
{
    Remind,
    Repeat,
    Escalate,
}
