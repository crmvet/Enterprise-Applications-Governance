using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// What the data guard rules read beyond the row (docs/architecture/data-taxonomy.md, Guard rules), as the adapter fetches it. A proposal of
// this build, like the other OP-08 facts.
public sealed class DataWriteFacts
{
    internal static readonly string[] Members = { "locationStageKey", "parentLocation", "deploymentTargetStageKey", "decisionSubject", "duplicateUse", "duplicateLocation" };

    public DataWriteFacts(string? locationStageKey = null, DataParentLocationFact? parentLocation = null, string? deploymentTargetStageKey = null, DataDecisionSubjectFact? decisionSubject = null, string? duplicateUse = null, string? duplicateLocation = null)
    {
        LocationStageKey = locationStageKey;
        ParentLocation = parentLocation;
        DeploymentTargetStageKey = deploymentTargetStageKey;
        DecisionSubject = decisionSubject;
        DuplicateUse = duplicateUse;
        DuplicateLocation = duplicateLocation;
    }

    // For a data location with a URL and no External Id: the label (or ID) of another data location in service with the
    // same Data Platform and Url and no External Id either, as the store holds it (the alternate key of O25, enforced by
    // the engine; docs/architecture/data-taxonomy.md). Null for none.
    public string? DuplicateLocation { get; }

    // For a data use: the record number (or ID) of another open use with the same second key of O27 (application, data
    // domain, data location and Reaches AI; an empty location counts as one value), as the store holds it. Null for none.
    public string? DuplicateUse { get; }

    // For a data mapping or use: the effective target stage of the location it names (a child takes its parent's).
    public string? LocationStageKey { get; }

    // For a data location with a parent: the parent as the store holds it (G-24).
    public DataParentLocationFact? ParentLocation { get; }

    // For a data location linked to a deployment target: that target's stage (G-24).
    public string? DeploymentTargetStageKey { get; }

    // For a data-use decision about a use or a mapping: what the decision is about (G-20).
    public DataDecisionSubjectFact? DecisionSubject { get; }

    internal static DataWriteFacts? FromJson(WireObject? wire)
    {
        if (wire == null)
        {
            return null;
        }

        WireObject? parent = wire.OptionalObject("parentLocation", "hasParent", "targetStageKey");
        WireObject? subject = wire.OptionalObject("decisionSubject", "subjectKind", "domainKey", "reachesAi", "isDeidentified", "origin", "locationStageKey");
        return new DataWriteFacts(
            wire.OptionalKey("locationStageKey"),
            parent == null ? null : new DataParentLocationFact(parent.Bool("hasParent", false), parent.OptionalKey("targetStageKey")),
            wire.OptionalKey("deploymentTargetStageKey"),
            DecisionSubjectOf(subject),
            wire.OptionalString("duplicateUse"),
            wire.OptionalString("duplicateLocation"));
    }

    private static DataDecisionSubjectFact? DecisionSubjectOf(WireObject? subject)
    {
        return subject == null ? null : new DataDecisionSubjectFact(
                subject.RequiredKey("subjectKind"),
                subject.RequiredKey("domainKey"),
                subject.Bool("reachesAi", false),
                subject.Bool("isDeidentified", false),
                subject.Has("origin") ? subject.Contract("origin", EngineContract.DataOrigins) : null,
                subject.OptionalKey("locationStageKey"));
    }
}

public sealed class DataParentLocationFact
{
    public DataParentLocationFact(bool hasParent, string? targetStageKey)
    {
        HasParent = hasParent;
        TargetStageKey = targetStageKey;
    }

    // True when the parent has a parent of its own: then the location would sit three levels deep.
    public bool HasParent { get; }

    public string? TargetStageKey { get; }
}

public sealed class DataDecisionSubjectFact
{
    public DataDecisionSubjectFact(string subjectKind, string domainKey, bool reachesAi, bool isDeidentified, DataOrigin? origin, string? locationStageKey)
    {
        SubjectKind = subjectKind;
        DomainKey = domainKey;
        ReachesAi = reachesAi;
        IsDeidentified = isDeidentified;
        Origin = origin;
        LocationStageKey = locationStageKey;
    }

    public string SubjectKind { get; }

    public string DomainKey { get; }

    public bool ReachesAi { get; }

    public bool IsDeidentified { get; }

    public DataOrigin? Origin { get; }

    public string? LocationStageKey { get; }
}

// The data taxonomy's guard rules G-18 to G-25 (docs/architecture/data-taxonomy.md, Guard rules; the choices of this
// build follow D-41). G-21 lives in AiStatus, G-22 beside G-11, and G-25 is D-41, question 7, as the product owner changed it: a data use or a
// copy whose governing rule is never is refused when it is saved, for a person and an AI client alike, with a reason
// that names the rule, the domain and where the rule is set. Ending or archiving such a row is never refused.
public static partial class WriteGuardOperation
{
    private sealed partial class GuardRun
    {
        private static readonly string[] DataUseFactColumns = { "dataDomain", "reachesAi", "isDeidentified", "access", "dataLocation", "status" };
        private static readonly string[] DataMappingFactColumns = { "dataDomain", "dataLocation", "dataOrigin", "status" };

        private DataTree? _tree;

        private DataTree Tree => _tree ??= new DataTree(_snapshot);

        private bool IsDataRow => _entity.Key == "data-use" || _entity.Key == "data-mapping" || _entity.Key == "data-location";

        // True when the row stands open after the write: not archived, and no End On or an End On after today. A create
        // is always judged, whatever End On it names, so a future End On cannot skip G-25. Only an update that ends an
        // existing row today or earlier, or archives it, is never refused (security review, blocker).
        private bool StaysOpen(string? newMeaning, string endColumn)
        {
            string? meaning = newMeaning ?? Text(_before, "statusMeaning");
            if (meaning == "archived")
            {
                return false;
            }

            return Op == WriteOperation.Create || DataTree.IsOpenOn(Text(Column(endColumn), null), DataTree.DayOf(_context.AsOfUtc));
        }

        private bool Touches(IEnumerable<string> columns)
        {
            return Op == WriteOperation.Create || columns.Any(c => _after.TryGetMember(c, out _));
        }

        // ---- O27 Data Use: G-18 and G-25 ----
        private void DataUse(string? newMeaning)
        {
            string? domain = Text(Column("dataDomain"), null);
            if (domain == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "dataDomain is required on " + EntityName + ".", "dataDomain");
            }

            if (Touches(new[] { "dataDomain" }) && !DeclarableDomain(domain))
            {
                return;
            }

            // G-18, the second key of O27 (docs/architecture/data-taxonomy.md, Keys; data design review): one open use per application,
            // domain, location and AI flag.
            if (StaysOpen(newMeaning, "endedOn") && Touches(new[] { "businessApplication", "dataDomain", "dataLocation", "reachesAi", "endedOn", "status" }) && _request.Data?.DuplicateUse is string duplicate)
            {
                Refuse(ReasonCodes.DataUseDuplicate, "Data use " + duplicate + " already declares this application's " + (IsTrue(Column("reachesAi")) ? "AI " : string.Empty) + "use of " + Tree.PathText(domain) + " at the same location. Change that use instead of declaring a second one.", duplicate);
                return;
            }

            if (!StaysOpen(newMeaning, "endedOn") || !Touches(DataUseFactColumns) || Tree.Domain(domain) == null)
            {
                return;
            }

            EffectiveDataRule rule = Tree.GoverningRuleForUse(domain, IsTrue(Column("reachesAi")));
            if (rule.Value == DataHandlingRule.Never)
            {
                RefuseNever(rule, domain, "data use");
            }
        }

        // ---- O26 Data Mapping: G-18, G-22, G-23 and G-25 ----
        private void DataMapping(string? newMeaning)
        {
            string? domain = Text(Column("dataDomain"), null);
            if (domain == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "dataDomain is required on " + EntityName + ".", "dataDomain");
            }

            ForbiddenInContainers();
            if (Touches(new[] { "dataDomain" }) && !DeclarableDomain(domain))
            {
                return;
            }

            string? originText = Text(Column("dataOrigin"), null);
            if (originText == null || !StaysOpen(newMeaning, "endedOn") || !Touches(DataMappingFactColumns))
            {
                return;
            }

            if (!EngineContract.DataOrigins.TryParse(originText, out DataOrigin origin))
            {
                throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "dataOrigin is \"" + originText + "\", which is not one of " + string.Join(", ", EngineContract.DataOrigins.WireValues) + ".", "dataOrigin");
            }

            if (origin == DataOrigin.Replica || origin == DataOrigin.Synthetic)
            {
                return;
            }

            string stage = _request.Data?.LocationStageKey
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.dataFacts.locationStageKey (the stage of the location the mapping names) is required to save a mapping with origin " + originText + ".");
            bool production = Tree.IsProductionStage(stage);
            _trace.Add(new RuleTrace("Target Stage", stage, RuleTraceRole.Matched));
            if (origin == DataOrigin.SystemOfRecord)
            {
                if (!production)
                {
                    Warn(ReasonCodes.DataCopyOrigin, "A system of record of " + Tree.PathText(domain) + " sits at a location whose stage " + Quote(stage) + " is not production. Check that it is not a copy.", stage);
                }

                return;
            }

            if (production)
            {
                Refuse(ReasonCodes.DataCopyOrigin, "A " + originText + " of " + Tree.PathText(domain) + " sits only outside production, and the location's stage " + Quote(stage) + " is production. Use system-of-record or replica instead.", stage);
                return;
            }

            if (Tree.Domain(domain) != null)
            {
                EffectiveDataRule rule = Tree.Rule(domain, DataRuleName.Copy);
                if (rule.Value == DataHandlingRule.Never)
                {
                    RefuseNever(rule, domain, "copy outside production");
                }
            }
        }

        // G-18: an active domain at a declarable level.
        private bool DeclarableDomain(string domain)
        {
            SnapshotRow? row = Tree.Domain(domain);
            if (row == null || !row.IsActive)
            {
                Refuse(ReasonCodes.DataDomainRetired, "Data domain " + Quote(domain) + (row == null ? " does not exist" : " is retired") + " in the configuration in force, so a new data use or mapping cannot point at it. Pick an active domain.", domain);
                return false;
            }

            _trace.Add(new RuleTrace("Data Domain", domain, RuleTraceRole.Matched));
            if (!Tree.IsDeclarable(domain))
            {
                Refuse(ReasonCodes.DataLevelNotDeclarable, Tree.PathText(domain) + " sits at level " + Quote(SnapshotRead.Text(row, "levelKey") ?? "?") + ", which is not declarable. Pick a domain at a lower level.", domain);
                return false;
            }

            return true;
        }

        // G-25 (D-41, question 7): the refusal names the rule, the domain and where the rule is set.
        private void RefuseNever(EffectiveDataRule rule, string domain, string what)
        {
            _trace.Add(rule.SetOnDomainKey != null ? new RuleTrace("Data Domain", rule.SetOnDomainKey, RuleTraceRole.Won) : new RuleTrace("Data Classification", rule.ClassificationKey ?? "none", RuleTraceRole.Won));
            Refuse(
                ReasonCodes.DataRuleNever,
                "Data rule never: " + Tree.DescribeRule(rule, domain) + ". Nobody has the privilege to save this " + what + "; change the rule where it is set through a configuration change, or leave the data out.",
                rule.SetOnDomainKey ?? domain);
        }

        // ---- O25 Data Location: G-18, G-22 and G-24 ----
        private void DataLocation()
        {
            ForbiddenIn("externalId");

            // G-18, the alternate key of O25 (docs/architecture/data-taxonomy.md): a location with no External Id is
            // known by its platform and URL, so a second one in service with the same pair is refused. Retiring one is
            // never refused for its key.
            if (Touches(new[] { "dataPlatform", "url", "externalId", "retiredOn" }) && Text(Column("retiredOn"), null) == null
                && Text(Column("externalId"), null) == null && Text(Column("url"), null) is string url && _request.Data?.DuplicateLocation is string other)
            {
                Refuse(ReasonCodes.DataLocationDuplicate, "Data location " + Quote(other) + " already names " + Quote(Text(Column("dataPlatform"), null) ?? "this platform") + " at " + url + ", and neither has an External Id. Change that location instead of adding a second one, or give each its External Id.", other);
                return;
            }

            if (!Touches(new[] { "parentLocation", "targetStage", "deploymentTarget" }))
            {
                return;
            }

            string? parent = Text(Column("parentLocation"), null);
            string? stage = Text(Column("targetStage"), null);
            string? effective = stage;
            if (parent != null)
            {
                DataParentLocationFact fact = _request.Data?.ParentLocation
                    ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.dataFacts.parentLocation is required to save a data location with a parent.");
                if (fact.HasParent)
                {
                    Refuse(ReasonCodes.LocationDepth, "The parent location has a parent of its own, and a data location goes two levels deep at most: an instance, then a store inside it. Pick the instance as the parent.", parent);
                }

                if (stage != null)
                {
                    Refuse(ReasonCodes.LocationStage, "A child location takes its parent's stage, so it sets no target stage of its own.", stage);
                }

                effective = fact.TargetStageKey;
            }
            else if (stage == null)
            {
                Refuse(ReasonCodes.LocationStage, "A top-level data location sets its target stage, so a copy can be checked against it.", null);
            }

            string? target = Text(Column("deploymentTarget"), null);
            if (target != null)
            {
                string? targetStage = _request.Data?.DeploymentTargetStageKey
                    ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.dataFacts.deploymentTargetStageKey is required to link a data location to a deployment target.");
                if (effective != null && !string.Equals(effective, targetStage, StringComparison.Ordinal))
                {
                    Refuse(ReasonCodes.LocationStage, "The linked deployment target's stage is " + Quote(targetStage) + ", and the location's stage is " + Quote(effective) + ". One place has one stage.", target);
                }
            }
        }

        // ---- O06 Owner Assignment: G-19 ----
        private void OwnerAssignment()
        {
            string[] subjects = { "businessApplication", "subjectDataDomain", "subjectDataLocation" };
            if (!Touches(subjects.Concat(new[] { "subjectKind", "ownerRole" })))
            {
                return;
            }

            string[] set = subjects.Where(c => Text(Column(c), null) != null).ToArray();
            string? kindText = Text(Column("subjectKind"), null);
            OwnerSubjectKind kind = OwnerSubjectKind.BusinessApplication;
            bool kindKnown = kindText == null || EngineContract.OwnerSubjectKinds.TryParse(kindText, out kind);
            string expected = kind == OwnerSubjectKind.DataDomain ? "subjectDataDomain" : kind == OwnerSubjectKind.DataLocation ? "subjectDataLocation" : "businessApplication";
            if (!kindKnown || set.Length != 1 || set[0] != expected)
            {
                Refuse(
                    ReasonCodes.DataOwnerSubject,
                    "An owner assignment names exactly one subject that matches its Subject Kind" + (kindText == null ? " (empty means the application)" : " " + Quote(kindText)) + ", and this one names " + (set.Length == 0 ? "none" : string.Join(" and ", set)) + ".",
                    _request.RowId);
                return;
            }

            string? roleKey = Text(Column("ownerRole"), null);
            SnapshotRow? role = roleKey == null ? null : SnapshotRead.Find(_snapshot, "owner-role", roleKey);
            if (role != null && !DataTree.CanHold(role, kind))
            {
                Refuse(
                    ReasonCodes.DataOwnerSubject,
                    "Owner role " + Quote(roleKey!) + " is not held on a " + EngineContract.OwnerSubjectKinds.ToWire(kind) + " (its Subject Kinds leave it out), so it cannot be assigned here.",
                    roleKey);
            }
        }

        // ---- O09 Decision: G-20 ----
        private void DataDecision(string? newMeaning)
        {
            string? type = Text(Column("decisionType"), null);
            string? subjectKind = Text(Column("subjectKind"), null);
            string? outcome = Text(Column("outcome"), null);
            if (type != EngineKeys.DataUseDecisionType || outcome != EngineKeys.DataUseAllowOutcome
                || (subjectKind != EngineKeys.DataUseSubjectKind && subjectKind != EngineKeys.DataMappingSubjectKind))
            {
                return;
            }

            bool proposesOrAccepts = Op == WriteOperation.Create || _after.TryGetMember("outcome", out _) || newMeaning == "accepted";
            if (!proposesOrAccepts)
            {
                return;
            }

            DataDecisionSubjectFact fact = _request.Data?.DecisionSubject
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.dataFacts.decisionSubject is required for a data-use decision that allows a " + subjectKind + ".");
            if (Tree.Domain(fact.DomainKey) == null)
            {
                return;
            }

            EffectiveDataRule? rule;
            bool deidentified;
            if (fact.SubjectKind == EngineKeys.DataMappingSubjectKind)
            {
                bool copy = (fact.Origin == DataOrigin.ProductionCopy || fact.Origin == DataOrigin.MaskedCopy) && !Tree.IsProductionStage(fact.LocationStageKey);
                rule = copy ? Tree.Rule(fact.DomainKey, DataRuleName.Copy) : null;
                deidentified = fact.Origin == DataOrigin.MaskedCopy;
            }
            else
            {
                rule = Tree.GoverningRuleForUse(fact.DomainKey, fact.ReachesAi);
                deidentified = fact.IsDeidentified;
            }

            if (rule != null && (rule.Value == DataHandlingRule.Never || (rule.Value == DataHandlingRule.MaskedOnly && !deidentified)))
            {
                Refuse(
                    ReasonCodes.DataDecisionCannotAllow,
                    "No decision can allow this " + (fact.SubjectKind == EngineKeys.DataMappingSubjectKind ? "copy" : "use") + ": " + Tree.DescribeRule(rule, fact.DomainKey)
                    + (rule.Value == DataHandlingRule.MaskedOnly ? ", and the data is not masked" : string.Empty) + ". Propose deny, or change the rule where it is set.",
                    rule.SetOnDomainKey ?? fact.DomainKey);
            }
        }

        // G-22: External Id refuses the forbidden patterns, connection string markers included.
        private void ForbiddenIn(string column)
        {
            string? text = Text(_after, column);
            if (text != null)
            {
                Forbidden(text, column);
            }
        }

        private void ForbiddenInContainers()
        {
            if (!_after.TryGetMember("containers", out JsonTreeNode? containers) || containers.Kind == JsonTreeKind.Null)
            {
                return;
            }

            if (containers.Kind != JsonTreeKind.Array || containers.Items.Any(i => i.Kind != JsonTreeKind.String))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.after.containers must be a list of container names as seen, such as [\"account\",\"contact\"].", "containers");
            }

            foreach (JsonTreeNode item in containers.Items)
            {
                if (Forbidden(item.GetString(), "containers"))
                {
                    return;
                }
            }
        }

        private bool Forbidden(string text, string column)
        {
            JsonTreeNode forbidden = SnapshotRead.RequireSetting(_snapshot, EngineKeys.ForbiddenUrlPatterns, n => n.Kind == JsonTreeKind.Array && n.Items.All(i => i.Kind == JsonTreeKind.String), "a list of regular expressions");
            foreach (JsonTreeNode item in forbidden.Items)
            {
                if (Matches(item.GetString(), text, RegexOptions.IgnoreCase, "Setting", EngineKeys.ForbiddenUrlPatterns))
                {
                    Refuse(ReasonCodes.LinkSecret, EntityName + " " + column + " matches the forbidden pattern " + Quote(item.GetString()) + " (forbidden-url-patterns), so it may carry a secret. Save the name without the secret part.", column);
                    return true;
                }
            }

            return false;
        }

        private static bool IsTrue(JsonTreeNode? node)
        {
            return node != null && node.Kind == JsonTreeKind.True;
        }
    }
}
