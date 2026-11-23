// Contract lists added after document 20, section 2.4 was written.
// Document 21, section 13.4 adds the first five. Document 25, section 15 asks for the last two.
// They follow the same rule as the engine meanings: fixed values, configurable labels.
//
// WORKING DEFAULT (open design question 1)
// These lists are part of the fixed engine vocabulary that question 1 asks about.
//
// Never reorder or renumber members. Add new members at the end.

namespace Product.Engine.Core.Contract;

// Setting engine-acceptable-decision-types (document 21, section 13.4).
// The only conditions under which the engine may accept a decision without a person's card.
// WORKING DEFAULT (open design question 8): the locked list holds intake at the lowest tier
// and change authorization for a standard change. Everything else goes on a card.
public enum EngineAcceptanceCondition
{
    LowestTier,
    StandardModel,
}

// C04 Setting value type (document 21, sections 3.2 and 13.4).
// The value type text is "kind" or "kind:argument", for example "key:change-type" or "contract:gate-level".
// This list holds the kind part only.
public enum SettingValueType
{
    Text,
    Number,
    YesNo,
    Json,
    Key,
    Contract,
}

// Setting intake-visibility (document 22; 28, issue 10 and Q13). Own means requester, PMO, platform admin, architect.
public enum IntakeVisibility
{
    Own,
    All,
}

// Setting claim-visibility (document 22; 28, issue 10 and Q13).
public enum ClaimVisibility
{
    AdminsAndClaimant,
    All,
}

// Setting record-access-mode (document 26; 28, issue 10 and Q13).
public enum RecordAccessMode
{
    OpenRead,
    PortfolioRead,
}

// C21 Standard Change Model, Conditions Json (document 25, OP-12 and section 16).
// The tests a standard change model's conditions may use.
public enum ConditionKind
{
    MaxTierRank,
    TargetStagesIn,
    MaxAffectedApplications,
    ComponentKindsWithin,
    MaxWindowMinutes,
    Stated,
}

// Authorization fact source kind (document 25, OP-14 and section 16).
// A client-built ITSM step (a workload principal) or a person's attestation on a confirmation card.
public enum AuthorizationFactSourceKind
{
    ItsmStep,
    Attestation,
}
