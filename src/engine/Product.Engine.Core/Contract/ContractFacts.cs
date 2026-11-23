namespace Product.Engine.Core.Contract;

// What the engine knows about a meaning, beyond its name.
// Question 1 says why these exist: "the engine must know that a status is final, an approver is a person,
// or a route is a pipeline". These facts are fixed. They never read a label.
//
// WORKING DEFAULT (open design question 1)
public static class ContractFacts
{
    // G-05: a decision with a final status never changes.
    public static bool IsFinal(DecisionStatusMeaning meaning)
    {
        return meaning == DecisionStatusMeaning.Accepted
            || meaning == DecisionStatusMeaning.Rejected
            || meaning == DecisionStatusMeaning.Superseded
            || meaning == DecisionStatusMeaning.Withdrawn;
    }

    // OP-03: only a person's answer can count toward an approval.
    public static bool IsPerson(ActorKindMeaning meaning)
    {
        return meaning == ActorKindMeaning.Person;
    }

    // G-08 and OP-03: an AI client acting for a person, or an agent identity.
    // An AI actor never approves and cannot set accepted, registered, approved or completed.
    public static bool IsAiActor(ActorKindMeaning meaning)
    {
        return meaning == ActorKindMeaning.AiClientForPerson
            || meaning == ActorKindMeaning.AgentIdentity;
    }

    // OP-03: a pipeline, the approval runner or the engine. A workload's answer never counts.
    public static bool IsWorkloadActor(ActorKindMeaning meaning)
    {
        return meaning == ActorKindMeaning.Pipeline
            || meaning == ActorKindMeaning.ApprovalRunner
            || meaning == ActorKindMeaning.Engine;
    }

    // OP-03: only a user principal can approve. An agent user is not a user here.
    public static bool CanApprove(PrincipalKind kind)
    {
        return kind == PrincipalKind.User;
    }

    // G-12 and V35: a role group binding points only to a group (D-11).
    public static bool CanBeRoleBindingTarget(PrincipalKind kind)
    {
        return kind == PrincipalKind.Group;
    }

    // Document 20, section 2.4: missing is computed only and never stored on a Document Link row.
    public static bool IsStoredOnRow(DocumentStatusMeaning meaning)
    {
        return meaning != DocumentStatusMeaning.Missing;
    }

    // OP-04: the check step itself is the pipeline route.
    public static bool IsPipeline(DeploymentRouteMeaning meaning)
    {
        return meaning == DeploymentRouteMeaning.PipelineStep;
    }

    // The ladder position of a gate level, from 0 (off) to 4 (enforce). Compared, never shown.
    public static int LadderRank(GateLevel level)
    {
        switch (level)
        {
            case GateLevel.Off:
                return 0;
            case GateLevel.Record:
                return 1;
            case GateLevel.Flag:
                return 2;
            case GateLevel.Advise:
                return 3;
            case GateLevel.Enforce:
                return 4;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(level));
        }
    }
}
