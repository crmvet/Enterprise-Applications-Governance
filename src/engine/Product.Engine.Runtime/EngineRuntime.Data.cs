using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Facts;
using Product.Engine.Runtime.Snapshots;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Runtime;

// The data taxonomy in the runtime (docs/architecture/data-taxonomy.md; data taxonomy slice 2):
// the facts OP-15 to OP-17 read, the data scope of a decision for OP-02, who a data owner subject asks, the data use
// counts OP-07 reads, the facts G-20 to G-25 read, and three reads for the tools of slice 3. The data-use state is
// computed when read, never stored.
public sealed partial class EngineRuntime
{
    // ---- Reads (slice 3 serves them as tools 77, 80 and 81) ----

    // OP-15 for one domain: its path, effective classification and rules with their source, and its owners up the tree.
    public RuntimeOutcome ResolveDataDomain(CallerIdentity caller, string domainKey)
    {
        return Run(caller, SnapshotPurpose.Read, call =>
        {
            var tree = new DataTree(call.Snapshot.Snapshot);
            List<StoredRow> owners = DomainOwnerRows(call, tree.Chain(domainKey).Select(d => d.KeyPath));
            JsonTreeNode input = RuntimeJson.Object(
                ("domainKey", JsonTreeNode.FromString(domainKey)),
                ("ownerAssignments", JsonTreeNode.CreateArray(owners.Select(OwnerFact).Where(f => f != null).Select(f => f!))),
                ("principals", JsonTreeNode.CreateArray(PrincipalFacts(call, owners))));
            return Answer(call, EngineOperation.ResolveDataDomain, input, domainKey);
        });
    }

    // OP-16 for one data use or one data mapping: its state, governing rule and approvers.
    public RuntimeOutcome EvaluateDataUse(CallerIdentity caller, string dataUseOrMappingId)
    {
        return Run(caller, SnapshotPurpose.Read, call =>
        {
            StoredRow? use = call.Tx.Get("data-use", dataUseOrMappingId);
            StoredRow? mapping = use == null ? call.Tx.Get("data-mapping", dataUseOrMappingId) : null;
            if (use == null && mapping == null)
            {
                throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Data use or data mapping " + dataUseOrMappingId + " does not exist.", "Data Use", dataUseOrMappingId);
            }

            JsonTreeNode input = use != null ? UseInput(call, use) : MappingInput(call, mapping!);
            return Answer(call, EngineOperation.EvaluateDataUse, input, dataUseOrMappingId);
        });
    }

    // OP-17 over every data fact the store holds, narrowed by the scope (domainKey, includeSubtree, applicationId,
    // locationId, principalId; none means everything).
    public RuntimeOutcome RunDataChecks(CallerIdentity caller, JsonTreeNode? scope = null)
    {
        return Run(caller, SnapshotPurpose.Read, call => Answer(call, EngineOperation.RunDataChecks, ChecksInput(call, scope), null));
    }

    // ---- Composed reads (data taxonomy slice 3: the data tools read through their own composed call) ----

    // OP-15 for each domain named, with the owner facts of every chain fetched once. A key that is not a domain gives
    // OP-15's invalid-input result, never an exception.
    internal IReadOnlyList<EvaluationResult<JsonTreeNode?>> ComposedDataDomains(RuntimeCall call, IReadOnlyList<string> domainKeys)
    {
        var tree = new DataTree(call.Snapshot.Snapshot);
        List<StoredRow> owners = DomainOwnerRows(call, domainKeys.Where(k => tree.Domain(k) != null).SelectMany(k => tree.Chain(k)).Select(d => d.KeyPath));
        JsonTreeNode ownerFacts = JsonTreeNode.CreateArray(owners.Select(OwnerFact).Where(f => f != null).Select(f => f!));
        JsonTreeNode principals = JsonTreeNode.CreateArray(PrincipalFacts(call, owners));
        return domainKeys.Select(key => Evaluate(call, EngineOperation.ResolveDataDomain, call.Snapshot, RuntimeJson.Object(
            ("domainKey", JsonTreeNode.FromString(key)),
            ("ownerAssignments", ownerFacts),
            ("principals", principals)))).ToList();
    }

    // OP-16 for one stored data use or data mapping (the call's own writes included), or null when neither exists.
    internal EvaluationResult<JsonTreeNode?>? ComposedDataUse(RuntimeCall call, string dataUseOrMappingId)
    {
        StoredRow? use = call.Tx.Get("data-use", dataUseOrMappingId);
        StoredRow? mapping = use == null ? call.Tx.Get("data-mapping", dataUseOrMappingId) : null;
        if (use == null && mapping == null)
        {
            return null;
        }

        return Evaluate(call, EngineOperation.EvaluateDataUse, call.Snapshot, use != null ? UseInput(call, use) : MappingInput(call, mapping!));
    }

    // OP-16 for a data use that is not saved yet (check_data_use): the domain, the application or the intake request, the
    // location, and the AI and de-identified flags. The application's scope comes from the application, else from the
    // intake request (its proposed kind, its linked application, its latest computed tier and sensitivity answer).
    internal EvaluationResult<JsonTreeNode?> ComposedDataUseQuestion(RuntimeCall call, DataUseQuestion question)
    {
        var tree = new DataTree(call.Snapshot.Snapshot);
        string domain = question.DomainKey;
        StoredRow? intake = question.IntakeRequestId == null ? null : call.Tx.Get("intake-request", question.IntakeRequestId);
        string? appId = question.ApplicationId ?? intake?.Text("businessApplication");
        StoredRow? app = appId == null ? null : call.Tx.Get("business-application", appId);
        JsonTreeNode? appFact = app == null ? null : ApplicationFact(call, app, false);
        List<StoredRow> owners = tree.Domain(domain) == null ? new List<StoredRow>() : DomainOwnerRows(call, tree.Chain(domain).Select(d => d.KeyPath));
        if (app != null)
        {
            FactSet appOwners = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("app-owners", RowQuery.For("owner-assignment").Where("businessApplication", app.Id))) });
            call.FactRounds += appOwners.Rounds;
            owners.AddRange(appOwners.All("app-owners"));
        }

        StoredRow? assessment = null;
        if (intake != null)
        {
            FactSet assessments = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("assessments", RowQuery.For("tier-assessment").Where("intakeRequest", intake.Id))) });
            call.FactRounds += assessments.Rounds;
            assessment = assessments.All("assessments").Where(a => a.Text("computedTier") != null)
                .OrderBy(a => a.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
        }

        bool? mapped = null;
        if (question.LocationId != null)
        {
            FactSet m = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("mapped", RowQuery.For("data-mapping").Where("dataDomain", domain).Where("dataLocation", question.LocationId))) });
            call.FactRounds += m.Rounds;
            mapped = m.All("mapped").Any(r => DataTree.IsOpenOn(r.Text("endedOn"), DataTree.DayOf(call.AsOfUtc)) && r.Text("statusMeaning") != "archived");
        }

        JsonTreeNode? Member(string name) => appFact != null && appFact.TryGetMember(name, out JsonTreeNode? value) ? value : null;
        string? impliedQuestion = tree.ImpliedQuestionKey;
        string? intakeAnswer = AnswerTo(assessment?.Column("answersJson"), impliedQuestion);
        return Evaluate(call, EngineOperation.EvaluateDataUse, call.Snapshot, RuntimeJson.Object(
            ("subjectKind", JsonTreeNode.FromString(EngineKeys.DataUseSubjectKind)),
            ("domainKey", JsonTreeNode.FromString(domain)),
            ("reachesAi", JsonTreeNode.FromBoolean(question.ReachesAi)),
            ("isDeidentified", JsonTreeNode.FromBoolean(question.IsDeidentified)),
            ("locationId", RuntimeJson.OptionalText(question.LocationId)),
            ("locationStageKey", RuntimeJson.OptionalText(LocationStage(call, question.LocationId))),
            ("applicationId", RuntimeJson.OptionalText(app?.Id)),
            ("tierKey", Member("tierKey") ?? RuntimeJson.OptionalText(assessment?.Text("computedTier"))),
            ("lifecycleStageKey", Member("lifecycleStageKey")),
            ("applicationKindKey", Member("applicationKindKey") ?? RuntimeJson.OptionalText(intake?.Text("proposedKind"))),
            ("portfolioPath", Member("portfolioPath")),
            ("ownerAssignments", JsonTreeNode.CreateArray(owners.Select(OwnerFact).Where(f => f != null).Select(f => f!))),
            ("principals", JsonTreeNode.CreateArray(PrincipalFacts(call, owners))),
            ("decisions", JsonTreeNode.CreateArray(Array.Empty<JsonTreeNode>())),
            ("mappedAtLocation", mapped.HasValue ? JsonTreeNode.FromBoolean(mapped.Value) : null),
            ("sensitivityAnswerKey", Member("sensitivityAnswerKey") ?? RuntimeJson.OptionalText(intakeAnswer))));
    }

    // OP-17 inside a composed call, over the store's data facts and the call's own writes.
    internal EvaluationResult<JsonTreeNode?> ComposedDataChecks(RuntimeCall call, JsonTreeNode? scope)
    {
        return Evaluate(call, EngineOperation.RunDataChecks, call.Snapshot, ChecksInput(call, scope));
    }

    // OP-17's input: every owner assignment, location, mapping, use, application and data dependency, and the decisions
    // about each use and mapping.
    private JsonTreeNode ChecksInput(RuntimeCall call, JsonTreeNode? scope)
    {
        {
            FactSet facts = FactSet.Run(call.Tx, f => new[]
            {
                FactPlan.Of(
                    FactQuery.Where("owners", RowQuery.For("owner-assignment")),
                    FactQuery.Where("locations", RowQuery.For("data-location")),
                    FactQuery.Where("mappings", RowQuery.For("data-mapping")),
                    FactQuery.Where("uses", RowQuery.For("data-use")),
                    FactQuery.Where("applications", RowQuery.For("business-application")),
                    FactQuery.Where("dependencies", RowQuery.For("dependency"))),
            });
            call.FactRounds += facts.Rounds;
            var tree = new DataTree(call.Snapshot.Snapshot);

            // One application's findings (the Application 360 data panel, get_data_gaps by application; wave 4b review): a
            // finding names an application only when it is about one of its uses (DC-05 to DC-07, DC-11, DC-13, DC-14) or
            // about the application itself (DC-09, DC-10), so the facts are that application and its uses, with the
            // decisions about those uses. Every owner, location, mapping and dependency stays, because those checks read
            // them. The other applications, their uses and the decisions about mappings (DC-08 names no application) are
            // left out: at 2,000 applications they were the whole cost of one card.
            string? onlyApp = ScopeApplication(scope);
            List<StoredRow> applications = facts.All("applications").Where(a => a.Text("statusMeaning") != "archived" && (onlyApp == null || string.Equals(a.Id, onlyApp, StringComparison.OrdinalIgnoreCase))).ToList();
            PrefetchDataFacts(call, facts.All("uses").Where(u => onlyApp == null || string.Equals(u.Text("businessApplication"), onlyApp, StringComparison.OrdinalIgnoreCase)), onlyApp == null ? facts.All("mappings") : Enumerable.Empty<StoredRow>(), applications.Select(a => a.Id));
            List<StoredRow> owners = facts.All("owners").ToList();
            var dataDependants = new HashSet<string>(
                facts.All("dependencies").Where(d => d.Text("endedOn") == null && EngineKeys.DataDependencyTypes.Contains(d.Text("dependencyType") ?? string.Empty, StringComparer.Ordinal))
                    .Select(d => d.Text("fromApplication")).Where(a => a != null).Select(a => a!),
                StringComparer.OrdinalIgnoreCase);
            List<StoredRow> uses = facts.All("uses").Where(u => onlyApp == null || string.Equals(u.Text("businessApplication"), onlyApp, StringComparison.OrdinalIgnoreCase)).ToList();
            List<StoredRow> mappings = facts.All("mappings").ToList();
            List<JsonTreeNode> decisions = uses.SelectMany(u => DataDecisionFacts(call, "subjectDataUse", u.Id, EngineKeys.DataUseSubjectKind, u.Text("dataDomain"), u.Flag("reachesAi"), null))
                .Concat((onlyApp == null ? mappings : new List<StoredRow>()).SelectMany(m => DataDecisionFacts(call, "subjectDataMapping", m.Id, EngineKeys.DataMappingSubjectKind, m.Text("dataDomain"), false, LocationStage(call, m.Text("dataLocation")))))
                .ToList();
            JsonTreeNode input = RuntimeJson.Object(
                ("scope", scope != null && scope.Kind == JsonTreeKind.Object && scope.Members.Count > 0 ? scope : null),
                ("ownerAssignments", JsonTreeNode.CreateArray(owners.Select(OwnerFact).Where(f => f != null).Select(f => f!))),
                ("principals", JsonTreeNode.CreateArray(PrincipalFacts(call, owners))),
                ("locations", JsonTreeNode.CreateArray(facts.All("locations").Select(l => RuntimeJson.Object(
                    ("locationId", JsonTreeNode.FromString(l.Id)),
                    ("parentLocationId", RuntimeJson.OptionalText(l.Text("parentLocation"))),
                    ("targetStageKey", RuntimeJson.OptionalText(l.Text("targetStage"))),
                    ("statusMeaning", RuntimeJson.OptionalText(l.Text("statusMeaning"))),
                    ("retiredOn", RuntimeJson.OptionalText(l.Text("retiredOn"))),
                    ("lastSeenOn", RuntimeJson.OptionalText(l.Text("lastSeenOn"))),
                    ("label", RuntimeJson.OptionalText(l.Text("labelAsSeen"))))))),
                ("mappings", JsonTreeNode.CreateArray(mappings.Where(m => m.Text("dataDomain") != null && m.Text("dataLocation") != null && m.Text("dataOrigin") != null).Select(m => RuntimeJson.Object(
                    ("mappingId", JsonTreeNode.FromString(m.Id)),
                    ("domainKey", JsonTreeNode.FromString(m.Text("dataDomain")!)),
                    ("locationId", JsonTreeNode.FromString(m.Text("dataLocation")!)),
                    ("origin", JsonTreeNode.FromString(m.Text("dataOrigin")!)),
                    ("statusMeaning", RuntimeJson.OptionalText(m.Text("statusMeaning"))),
                    ("endedOn", RuntimeJson.OptionalText(m.Text("endedOn"))))))),
                ("uses", JsonTreeNode.CreateArray(uses.Where(u => u.Text("dataDomain") != null && u.Text("businessApplication") != null && u.Text("access") != null).Select(u => RuntimeJson.Object(
                    ("useId", JsonTreeNode.FromString(u.Id)),
                    ("recordNumber", RuntimeJson.OptionalText(u.Text("recordNumber"))),
                    ("applicationId", JsonTreeNode.FromString(u.Text("businessApplication")!)),
                    ("domainKey", JsonTreeNode.FromString(u.Text("dataDomain")!)),
                    ("locationId", RuntimeJson.OptionalText(u.Text("dataLocation"))),
                    ("access", JsonTreeNode.FromString(u.Text("access")!)),
                    ("reachesAi", JsonTreeNode.FromBoolean(u.Flag("reachesAi"))),
                    ("isDeidentified", JsonTreeNode.FromBoolean(u.Flag("isDeidentified"))),
                    ("statusMeaning", RuntimeJson.OptionalText(u.Text("statusMeaning"))),
                    ("endedOn", RuntimeJson.OptionalText(u.Text("endedOn"))))))),
                ("applications", JsonTreeNode.CreateArray(applications.Select(a => ApplicationFact(call, a, dataDependants.Contains(a.Id))))),
                ("decisions", JsonTreeNode.CreateArray(decisions)));
            return input;
        }
    }

    // The application of a scope that names only an application (OP-17's scope { applicationId }), else null.
    private static string? ScopeApplication(JsonTreeNode? scope)
    {
        if (scope == null || scope.Kind != JsonTreeKind.Object || scope.Members.Count != 1)
        {
            return null;
        }

        return RuntimeJson.Text(scope, "applicationId");
    }

    private static CallAnswer Answer(RuntimeCall call, EngineOperation operation, JsonTreeNode input, string? rowId)
    {
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, operation, call.Snapshot, input);
        if (!IsAnswer(operation, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons);
        return new CallAnswer(result.Outcome, rowId, false, result.Payload);
    }

    // ---- Facts ----

    // Loads ahead, in a read call, the facts OP-16 and OP-17 read one row at a time for each use, mapping and
    // application (document 25, B25-23): the applications, their owners, decisions, tier assessments and intake requests;
    // the owners of every domain and of the locations named; the decisions about each use and mapping and the approval
    // records of the open ones; the mappings of the domains the uses name; the locations and their parents. About fifteen
    // store calls whatever the size of the estate, after which every read the evaluations make is answered by the call's
    // read cache. A write call reads from the store as before, so this does nothing there.
    internal static void PrefetchDataFacts(RuntimeCall call, IEnumerable<StoredRow> uses, IEnumerable<StoredRow> mappings, IEnumerable<string?> applicationIds)
    {
        if (!(call.Tx is CallReadCache cache))
        {
            return;
        }

        List<StoredRow> useRows = uses.ToList();
        List<StoredRow> mappingRows = mappings.ToList();
        List<string> apps = KeySetReads.Distinct(applicationIds.Concat(useRows.Select(u => u.Text("businessApplication"))));
        var tree = new DataTree(call.Snapshot.Snapshot);
        cache.LoadAll("portfolio");
        cache.LoadAll("principal-reference");
        cache.GetMany("business-application", apps);
        cache.QueryIn("owner-assignment", "businessApplication", apps);
        cache.QueryIn("owner-assignment", "subjectDataDomain", tree.Domains.Select(d => d.KeyPath).ToList());
        List<string> locations = KeySetReads.Distinct(useRows.Select(u => u.Text("dataLocation")).Concat(mappingRows.Select(m => m.Text("dataLocation"))));
        cache.QueryIn("owner-assignment", "subjectDataLocation", locations);
        IReadOnlyList<StoredRow> places = cache.GetMany("data-location", locations);
        cache.GetMany("data-location", KeySetReads.Distinct(places.Select(l => l.Text("parentLocation"))));
        cache.QueryIn("data-mapping", "dataDomain", KeySetReads.Distinct(useRows.Select(u => u.Text("dataDomain"))));
        cache.QueryIn("decision", "subjectApplication", apps);
        IReadOnlyList<StoredRow> useDecisions = cache.QueryIn("decision", "subjectDataUse", useRows.Select(u => u.Id).ToList());
        IReadOnlyList<StoredRow> mappingDecisions = cache.QueryIn("decision", "subjectDataMapping", mappingRows.Select(m => m.Id).ToList());
        cache.QueryIn("approval-record", "decision", useDecisions.Concat(mappingDecisions).Where(d => d.Text("statusMeaning") == "proposed").Select(d => d.Id).ToList());
        cache.QueryIn("tier-assessment", "businessApplication", apps);
        IReadOnlyList<StoredRow> intakes = cache.QueryIn("intake-request", "businessApplication", apps);
        cache.QueryIn("tier-assessment", "intakeRequest", intakes.Select(i => i.Id).ToList());
    }

    // The data uses and mappings with these IDs, and the facts OP-16 reads for each (PrefetchDataFacts).
    internal static void PrefetchDataRows(RuntimeCall call, IEnumerable<string?> dataUseOrMappingIds)
    {
        if (!(call.Tx is CallReadCache cache))
        {
            return;
        }

        List<string> ids = KeySetReads.Distinct(dataUseOrMappingIds);
        PrefetchDataFacts(call, cache.GetMany("data-use", ids), cache.GetMany("data-mapping", ids), Array.Empty<string?>());
    }

    // The current owner assignments of every domain named (one query each).
    private static List<StoredRow> DomainOwnerRows(RuntimeCall call, IEnumerable<string> domainKeys)
    {
        string[] keys = domainKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0)
        {
            return new List<StoredRow>();
        }

        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(keys.Select((k, i) => FactQuery.Where("domain-owners-" + i, RowQuery.For("owner-assignment").Where("subjectDataDomain", k))).ToArray()) });
        call.FactRounds += facts.Rounds;
        return keys.SelectMany((k, i) => facts.All("domain-owners-" + i)).ToList();
    }

    private static List<StoredRow> LocationOwnerRows(RuntimeCall call, string? locationId)
    {
        if (locationId == null)
        {
            return new List<StoredRow>();
        }

        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("location-owners", RowQuery.For("owner-assignment").Where("subjectDataLocation", locationId))) });
        call.FactRounds += facts.Rounds;
        return facts.All("location-owners").ToList();
    }

    // One O06 row as OP-15 reads it, or null when it names no subject.
    private static JsonTreeNode? OwnerFact(StoredRow row)
    {
        string? domain = row.Text("subjectDataDomain");
        string? location = domain == null ? row.Text("subjectDataLocation") : null;
        string? app = domain == null && location == null ? row.Text("businessApplication") : null;
        string? role = row.Text("ownerRole");
        string? principal = row.Text("principal");
        if ((domain ?? location ?? app) == null || role == null || principal == null)
        {
            return null;
        }

        return RuntimeJson.Object(
            ("assignmentId", JsonTreeNode.FromString(row.Id)),
            ("ownerRoleKey", JsonTreeNode.FromString(role)),
            ("principalId", JsonTreeNode.FromString(principal)),
            ("isDeputy", JsonTreeNode.FromBoolean(row.Flag("isDeputy"))),
            ("deputyForId", RuntimeJson.OptionalText(row.Text("deputyFor"))),
            ("startOn", RuntimeJson.OptionalText(row.Text("startOn"))),
            ("endOn", RuntimeJson.OptionalText(row.Text("endOn"))),
            ("dataDomainKey", RuntimeJson.OptionalText(domain)),
            ("dataLocationId", RuntimeJson.OptionalText(location)),
            ("applicationId", RuntimeJson.OptionalText(app)));
    }

    private static DataOwnerFact? OwnerFactOf(StoredRow row)
    {
        JsonTreeNode? fact = OwnerFact(row);
        if (fact == null)
        {
            return null;
        }

        return new DataOwnerFact(row.Id, row.Text("ownerRole")!, row.Text("principal")!, row.Flag("isDeputy"), row.Text("deputyFor"), row.Text("startOn"), row.Text("endOn"),
            row.Text("subjectDataDomain"), row.Text("subjectDataDomain") == null ? row.Text("subjectDataLocation") : null,
            row.Text("subjectDataDomain") == null && row.Text("subjectDataLocation") == null ? row.Text("businessApplication") : null);
    }

    // The directory state of each principal the rows name (O05 Directory State, Leaves On, Is Erased).
    private static IEnumerable<JsonTreeNode> PrincipalFacts(RuntimeCall call, IEnumerable<StoredRow> owners)
    {
        string[] ids = owners.Select(o => o.Text("principal")).Where(p => p != null).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<JsonTreeNode>();
        }

        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(ids.Select((p, i) => FactQuery.Where("principal-" + i, RowQuery.For("principal-reference").Where("entraObjectId", p))).ToArray()) });
        call.FactRounds += facts.Rounds;
        var list = new List<JsonTreeNode>();
        for (int i = 0; i < ids.Length; i++)
        {
            StoredRow? row = facts.All("principal-" + i).FirstOrDefault();
            if (row == null)
            {
                continue;
            }

            string? state = row.Text("directoryState");
            list.Add(RuntimeJson.Object(
                ("principalId", JsonTreeNode.FromString(ids[i])),
                ("directoryState", state == "enabled" || state == "disabled" || state == "deleted" || state == "unknown" ? JsonTreeNode.FromString(state) : null),
                ("leavesOn", RuntimeJson.OptionalText(row.Text("leavesOn"))),
                ("isErased", row.Flag("isErased") ? JsonTreeNode.True : null)));
        }

        return list;
    }

    private static Func<string, DataPrincipalFact?> PrincipalLookup(IEnumerable<JsonTreeNode> facts)
    {
        var map = new Dictionary<string, DataPrincipalFact>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonTreeNode fact in facts)
        {
            string id = RuntimeJson.Text(fact, "principalId")!;
            map[id] = new DataPrincipalFact(id, RuntimeJson.Text(fact, "directoryState"), RuntimeJson.Text(fact, "leavesOn"), RuntimeJson.Flag(fact, "isErased"));
        }

        return id => map.TryGetValue(id, out DataPrincipalFact? found) ? found : null;
    }

    // The effective stage of a location: its own, or its parent's (docs/architecture/data-taxonomy.md, Stages and data
    // origin).
    private static string? LocationStage(RuntimeCall call, string? locationId)
    {
        StoredRow? location = locationId == null ? null : call.Tx.Get("data-location", locationId);
        if (location == null)
        {
            return null;
        }

        string? stage = location.Text("targetStage");
        string? parent = location.Text("parentLocation");
        return stage ?? (parent == null ? null : call.Tx.Get("data-location", parent)?.Text("targetStage"));
    }

    // The decisions about one data use or mapping, with what each was judged by: the classification rank and governing
    // rule under the configuration version the decision stamped (re-decide; docs/architecture/data-taxonomy.md, The
    // decision state), and the approvals that count.
    private IEnumerable<JsonTreeNode> DataDecisionFacts(RuntimeCall call, string subjectColumn, string subjectId, string subjectKind, string? domainKey, bool reachesAi, string? stage)
    {
        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("decisions", RowQuery.For("decision").Where(subjectColumn, subjectId))) });
        call.FactRounds += facts.Rounds;
        foreach (StoredRow decision in facts.All("decisions"))
        {
            string? type = decision.Text("decisionType");
            string? meaning = decision.Text("statusMeaning");
            if (type == null || meaning == null || !EngineContract.DecisionStatusMeanings.TryParse(meaning, out _))
            {
                continue;
            }

            int? rank = null;
            string? rule = null;
            if (meaning == "accepted" && domainKey != null)
            {
                var then = new DataTree(StampedSnapshot(call, decision).Snapshot);
                if (then.Domain(domainKey) != null)
                {
                    rank = then.Classification(domainKey)?.Rank;
                    EffectiveDataRule governing = subjectKind == EngineKeys.DataMappingSubjectKind ? then.Rule(domainKey, DataRuleName.Copy) : then.GoverningRuleForUse(domainKey, reachesAi);
                    rule = EngineContract.DataHandlingRules.ToWire(governing.Value);
                }
            }

            int? given = null;
            if (meaning == "proposed")
            {
                given = Records(call, decision.Id).Count(r => r.Flag("countsTowardRequirement"));
            }

            yield return RuntimeJson.Object(
                ("decisionId", JsonTreeNode.FromString(decision.Id)),
                ("decisionTypeKey", JsonTreeNode.FromString(type)),
                ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                ("subjectId", JsonTreeNode.FromString(subjectId)),
                ("statusMeaning", JsonTreeNode.FromString(meaning)),
                ("outcomeKey", RuntimeJson.OptionalText(decision.Text("outcome"))),
                ("decidedOn", RuntimeJson.OptionalText(decision.Text("decidedOn"))),
                ("judgedClassificationRank", rank.HasValue ? JsonTreeNode.FromInt64(rank.Value) : null),
                ("judgedRule", RuntimeJson.OptionalText(rule)),
                ("approvalsGiven", given.HasValue ? JsonTreeNode.FromInt64(given.Value) : null));
        }
    }

    // The application's scope and data facts for OP-16 and OP-17: tier, stage, kind, portfolio path, Uses AI, Holds No
    // Business Data, its latest sensitivity answer (the implied question of the classifications) and whether it retires.
    private static JsonTreeNode ApplicationFact(RuntimeCall call, StoredRow app, bool hasDataDependency)
    {
        FactSet facts = FactSet.Run(call.Tx, f => ApplicationFacts(f, app.Id));
        call.FactRounds += facts.Rounds;
        // Retiring: at a retired stage, or with a proposed decision whose outcome moves it there (P09).
        string? stage = app.Text("lifecycleStage");
        bool retiring = stage != null && call.Snapshot.Snapshot.TryGetRow("lifecycle-stage", stage, out Core.Snapshots.SnapshotRow? stageRow) && stageRow.EngineMeaning == "retired";
        if (!retiring)
        {
            FactSet proposed = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("proposed", RowQuery.For("decision").Where("subjectApplication", app.Id).Where("statusMeaning", "proposed"))) });
            call.FactRounds += proposed.Rounds;
            retiring = proposed.All("proposed").Any(d => d.Text("decisionType") is string type && d.Text("outcome") is string outcome && RetiringOutcome(call.Snapshot, type, outcome));
        }

        return RuntimeJson.Object(
            ("applicationId", JsonTreeNode.FromString(app.Id)),
            ("label", RuntimeJson.OptionalText(app.Text("name") ?? app.Text("recordNumber"))),
            ("tierKey", RuntimeJson.OptionalText(app.Text("tier"))),
            ("lifecycleStageKey", RuntimeJson.OptionalText(stage)),
            ("applicationKindKey", RuntimeJson.OptionalText(app.Text("applicationKind"))),
            ("portfolioPath", RuntimeJson.Strings(DecisionScope.PortfolioPathOf(facts))),
            ("usesAi", app.Flag("usesAi") ? JsonTreeNode.True : null),
            ("holdsNoBusinessData", app.Flag("holdsNoBusinessData") ? JsonTreeNode.True : null),
            ("hasDataDependency", hasDataDependency ? JsonTreeNode.True : null),
            ("sensitivityAnswerKey", RuntimeJson.OptionalText(SensitivityAnswer(call, app.Id))),
            ("isRetiring", retiring ? JsonTreeNode.True : null),
            ("statusMeaning", RuntimeJson.OptionalText(app.Text("statusMeaning"))));
    }

    // The application's latest answer to the tier question the classifications imply (O08 Answers Json), or null.
    private static string? SensitivityAnswer(RuntimeCall call, string applicationId)
    {
        string? question = new DataTree(call.Snapshot.Snapshot).ImpliedQuestionKey;
        StoredRow? app = question == null ? null : call.Tx.Get("business-application", applicationId);
        if (question == null || app == null)
        {
            return null;
        }

        // The latest computed assessment of the application and its intake requests.
        FactSet facts = FactSet.Run(call.Tx, f => SubjectFactsPlan(f, app, true, false));
        call.FactRounds += facts.Rounds;
        return AnswerTo(LatestAssessment(facts)?.Column("answersJson"), question);
    }

    // One answer of O08 Answers Json. answer_tier_questions stores [{"questionKey": "...", "answerKey": "..."}]; the
    // shape [{"question": "...", "answer": "..."}] is read too. Data taxonomy slice 5 (D-41): slice 2 read only the second
    // shape, so DC-09 never fired from an answer a tool had stored (docs/architecture/data-taxonomy.md).
    private static string? AnswerTo(JsonTreeNode? answers, string? question)
    {
        if (question == null || answers == null || answers.Kind != JsonTreeKind.Array)
        {
            return null;
        }

        JsonTreeNode? item = answers.Items.LastOrDefault(i => (RuntimeJson.Text(i, "questionKey") ?? RuntimeJson.Text(i, "question")) == question);
        return RuntimeJson.Text(item, "answerKey") ?? RuntimeJson.Text(item, "answer");
    }

    // OP-16's input for a stored data use.
    private JsonTreeNode UseInput(RuntimeCall call, StoredRow use)
    {
        var tree = new DataTree(call.Snapshot.Snapshot);
        string domain = use.Text("dataDomain") ?? throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "Data use " + use.Id + " names no data domain.", "Data Use", use.Id);
        string? appId = use.Text("businessApplication");
        string? location = use.Text("dataLocation");
        StoredRow? app = appId == null ? null : call.Tx.Get("business-application", appId);
        JsonTreeNode? appFact = app == null ? null : ApplicationFact(call, app, false);
        List<StoredRow> owners = DomainOwnerRows(call, tree.Chain(domain).Select(d => d.KeyPath));
        if (appId != null)
        {
            owners.AddRange(FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("app-owners", RowQuery.For("owner-assignment").Where("businessApplication", appId))) }).All("app-owners"));
        }

        bool? mapped = null;
        if (location != null)
        {
            FactSet m = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("mapped", RowQuery.For("data-mapping").Where("dataDomain", domain).Where("dataLocation", location))) });
            mapped = m.All("mapped").Any(r => DataTree.IsOpenOn(r.Text("endedOn"), DataTree.DayOf(call.AsOfUtc)) && r.Text("statusMeaning") != "archived");
        }

        return RuntimeJson.Object(
            ("subjectKind", JsonTreeNode.FromString(EngineKeys.DataUseSubjectKind)),
            ("domainKey", JsonTreeNode.FromString(domain)),
            ("reachesAi", JsonTreeNode.FromBoolean(use.Flag("reachesAi"))),
            ("isDeidentified", JsonTreeNode.FromBoolean(use.Flag("isDeidentified"))),
            ("locationId", RuntimeJson.OptionalText(location)),
            ("locationStageKey", RuntimeJson.OptionalText(LocationStage(call, location))),
            ("applicationId", RuntimeJson.OptionalText(appId)),
            ("tierKey", appFact == null ? null : appFact.TryGetMember("tierKey", out JsonTreeNode? t) ? t : null),
            ("lifecycleStageKey", appFact == null ? null : appFact.TryGetMember("lifecycleStageKey", out JsonTreeNode? s) ? s : null),
            ("applicationKindKey", appFact == null ? null : appFact.TryGetMember("applicationKindKey", out JsonTreeNode? k) ? k : null),
            ("portfolioPath", appFact == null ? null : appFact.TryGetMember("portfolioPath", out JsonTreeNode? p) ? p : null),
            ("ownerAssignments", JsonTreeNode.CreateArray(owners.Select(OwnerFact).Where(f => f != null).Select(f => f!))),
            ("principals", JsonTreeNode.CreateArray(PrincipalFacts(call, owners))),
            ("decisions", JsonTreeNode.CreateArray(DataDecisionFacts(call, "subjectDataUse", use.Id, EngineKeys.DataUseSubjectKind, domain, use.Flag("reachesAi"), null))),
            ("mappedAtLocation", mapped.HasValue ? JsonTreeNode.FromBoolean(mapped.Value) : null),
            ("sensitivityAnswerKey", appFact == null ? null : appFact.TryGetMember("sensitivityAnswerKey", out JsonTreeNode? a) ? a : null));
    }

    // OP-16's input for a stored data mapping (a copy outside production needs a decision or breaks the copy rule).
    private JsonTreeNode MappingInput(RuntimeCall call, StoredRow mapping)
    {
        var tree = new DataTree(call.Snapshot.Snapshot);
        string domain = mapping.Text("dataDomain") ?? throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "Data mapping " + mapping.Id + " names no data domain.", "Data Mapping", mapping.Id);
        string? location = mapping.Text("dataLocation");
        string? stage = LocationStage(call, location);
        List<StoredRow> owners = DomainOwnerRows(call, tree.Chain(domain).Select(d => d.KeyPath));
        owners.AddRange(LocationOwnerRows(call, location));
        return RuntimeJson.Object(
            ("subjectKind", JsonTreeNode.FromString(EngineKeys.DataMappingSubjectKind)),
            ("domainKey", JsonTreeNode.FromString(domain)),
            ("origin", RuntimeJson.OptionalText(mapping.Text("dataOrigin"))),
            ("locationId", RuntimeJson.OptionalText(location)),
            ("locationStageKey", RuntimeJson.OptionalText(stage)),
            ("ownerAssignments", JsonTreeNode.CreateArray(owners.Select(OwnerFact).Where(f => f != null).Select(f => f!))),
            ("principals", JsonTreeNode.CreateArray(PrincipalFacts(call, owners))),
            ("decisions", JsonTreeNode.CreateArray(DataDecisionFacts(call, "subjectDataMapping", mapping.Id, EngineKeys.DataMappingSubjectKind, domain, false, stage))));
    }

    // ---- The data scope of a decision (OP-02 scope fields and owner subjects) ----

    // Sets the decision's data scope: the effective classification and AI flag of its use, the level of its domain, the
    // data taxonomy change of its configuration draft and the domains it touches, and the owner facts the data owner
    // subjects resolve through.
    private void ApplyDataScope(RuntimeCall call, DecisionScope scope)
    {
        var tree = new DataTree(call.Snapshot.Snapshot);
        string? change = null;
        IReadOnlyList<string> touched = Array.Empty<string>();
        if (scope.SubjectKind == "configuration-draft" && scope.Draft != null && tree.Mode != DataTaxonomyMode.Off)
        {
            DataTaxonomyDiff? diff = DraftDiff(call, scope.Draft);
            if (diff?.Change != null)
            {
                change = EngineContract.DataTaxonomyChanges.ToWire(diff.Change.Value);
                touched = diff.TouchedDomainKeys;
            }
        }

        if (!scope.IsDataSubject && change == null && scope.DecisionTypeKey != EngineKeys.DataRetentionDecisionType)
        {
            return;
        }

        string? domain = scope.DataDomainKey;
        string? classification = domain != null && tree.Domain(domain) != null ? tree.Classification(domain)?.Key : null;
        bool? aiUse = scope.SubjectKind == EngineKeys.DataUseSubjectKind && scope.DataUse != null && scope.DecisionTypeKey == EngineKeys.DataUseDecisionType ? scope.DataUse.Flag("reachesAi") : null;
        string? level = scope.SubjectKind == EngineKeys.DataDomainSubjectKind && domain != null ? tree.LevelKeyOf(domain) : null;

        var keys = new List<string>();
        if (domain != null)
        {
            keys.AddRange(tree.Chain(domain).Select(n => n.KeyPath));
        }

        foreach (string key in touched)
        {
            keys.AddRange(tree.Chain(key).Select(n => n.KeyPath));
        }

        List<StoredRow> rows = DomainOwnerRows(call, keys);
        rows.AddRange(LocationOwnerRows(call, scope.DataLocationId));
        List<DataOwnerFact> owners = rows.Select(OwnerFactOf).Where(o => o != null).Select(o => o!).ToList();
        Func<string, DataPrincipalFact?> principals = PrincipalLookup(PrincipalFacts(call, rows));

        // The owner rows of the subject itself (not inherited), for the update-subject effect of an ownership decision.
        IReadOnlyList<StoredRow> subjectOwners = scope.SubjectKind == EngineKeys.DataDomainSubjectKind && domain != null
            ? DecisionScope.CurrentOwners(rows.Where(r => string.Equals(r.Text("subjectDataDomain"), domain, StringComparison.Ordinal)), call.AsOfUtc)
            : scope.SubjectKind == EngineKeys.DataLocationSubjectKind && scope.DataLocationId != null
                ? DecisionScope.CurrentOwners(rows.Where(r => string.Equals(r.Text("subjectDataLocation"), scope.DataLocationId, StringComparison.OrdinalIgnoreCase)), call.AsOfUtc)
                : Array.Empty<StoredRow>();
        scope.UseDataScope(classification, aiUse, level, change, touched, tree, owners, principals, subjectOwners);
    }

    // The data taxonomy change of a configuration draft: the draft's content against the active version's.
    private DataTaxonomyDiff? DraftDiff(RuntimeCall call, StoredRow draft)
    {
        JsonTreeNode? changeJson = draft.Column("changeJson");
        if (changeJson == null)
        {
            return null;
        }

        try
        {
            CompiledSnapshot active = call.Snapshot;
            int baseNumber = (int)(draft.Number("baseVersionNumber") ?? active.VersionNumber);
            CompiledSnapshot baseVersion = Cache.LoadVersion(_configuration, active.ConfigurationSetKey, baseNumber).Snapshot ?? active;
            ConfigurationPatch patch = ConfigurationPatch.FromDocument(changeJson, PatchKind.Draft, "Configuration Draft " + draft.Id);
            ComposedConfiguration candidate = _validator.Composer.Compose(ContentSource.Bundle(baseVersion.ContentJson, "draft").WithPatch(patch), ValidationMode.Draft, new ValidationIssues());
            ComposedConfiguration current = _validator.Composer.Compose(ContentSource.Bundle(active.ContentJson, "active"), ValidationMode.Draft, new ValidationIssues());
            if (!candidate.IsComplete || !current.IsComplete)
            {
                return null;
            }

            // The domains open uses and mappings point at, so retiring or moving one of them counts as loosens (section
            // 8.3; security review, major).
            string day = DataTree.DayOf(call.AsOfUtc);
            FactSet rows = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("uses", RowQuery.For("data-use")), FactQuery.Where("mappings", RowQuery.For("data-mapping"))) });
            call.FactRounds += rows.Rounds;
            IEnumerable<string> inUse = rows.All("uses").Concat(rows.All("mappings"))
                .Where(r => DataTree.IsOpenOn(r.Text("endedOn"), day) && r.Text("statusMeaning") != "archived" && r.Text("dataDomain") != null)
                .Select(r => r.Text("dataDomain")!)
                .Distinct(StringComparer.Ordinal);
            return DataTaxonomyDiff.Compute(current.Content, candidate.Content, inUse);
        }
        catch (Exception e) when (e is ArgumentException || e is ConfigurationInputException || e is FormatException || e is InvalidOperationException)
        {
            return null;
        }
    }

    // The effect of a configuration change on the live data uses and copies (docs/architecture/data-taxonomy.md, Changing the taxonomy): OP-16 for each
    // open use and mapping on the domains the change touches, under the configuration in force and under the preview of
    // the change. Each row: kind, id, record number, application, domain, state before and after, and whether it would
    // break a rule or need a new decision. Empty when the change touches no domain or does not compose.
    internal JsonTreeNode ComposedDataUseImpact(RuntimeCall call, long baseVersionNumber, JsonTreeNode changeJson)
    {
        CompiledSnapshot active = call.Snapshot;
        CompiledSnapshot? preview;
        DataTaxonomyDiff? diff;
        try
        {
            CompiledSnapshot baseVersion = Cache.LoadVersion(_configuration, active.ConfigurationSetKey, (int)baseVersionNumber).Snapshot ?? active;
            ConfigurationPatch patch = ConfigurationPatch.FromDocument(changeJson, PatchKind.Draft, "Configuration change preview");
            ComposedConfiguration candidate = _validator.Composer.Compose(ContentSource.Bundle(baseVersion.ContentJson, "draft").WithPatch(patch), ValidationMode.Draft, new ValidationIssues());
            ComposedConfiguration current = _validator.Composer.Compose(ContentSource.Bundle(active.ContentJson, "active"), ValidationMode.Draft, new ValidationIssues());
            if (!candidate.IsComplete || !current.IsComplete)
            {
                return JsonTreeNode.CreateArray();
            }

            diff = DataTaxonomyDiff.Compute(current.Content, candidate.Content);
            preview = SnapshotCompiler.Compile(candidate.Bundle, active.VersionNumber + 1, active.ConfigurationSetKey);
        }
        catch (Exception e) when (e is ArgumentException || e is ConfigurationInputException || e is FormatException || e is InvalidOperationException || e is SnapshotCompileException)
        {
            return JsonTreeNode.CreateArray();
        }

        if (diff.TouchedDomainKeys.Count == 0)
        {
            return JsonTreeNode.CreateArray();
        }

        string day = DataTree.DayOf(call.AsOfUtc);
        var touched = new HashSet<string>(diff.TouchedDomainKeys, StringComparer.Ordinal);
        FactSet rows = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("uses", RowQuery.For("data-use")), FactQuery.Where("mappings", RowQuery.For("data-mapping"))) });
        call.FactRounds += rows.Rounds;
        bool Open(StoredRow r) => DataTree.IsOpenOn(r.Text("endedOn"), day) && r.Text("statusMeaning") == "registered" && r.Text("dataDomain") is string k && touched.Contains(k);
        var items = new List<JsonTreeNode>();
        foreach ((StoredRow row, bool isUse) in rows.All("uses").Where(Open).Select(r => (r, true)).Concat(rows.All("mappings").Where(Open).Select(r => (r, false)))
            .OrderBy(x => x.r.Text("recordNumber") ?? x.r.Id, StringComparer.Ordinal).Take(100))
        {
            JsonTreeNode input = isUse ? UseInput(call, row) : MappingInput(call, row);
            string? before = RuntimeJson.Text(Evaluate(call, EngineOperation.EvaluateDataUse, active, input).Payload, "state");
            string? after = RuntimeJson.Text(Evaluate(call, EngineOperation.EvaluateDataUse, preview, input).Payload, "state");
            if (before == null || after == null)
            {
                continue;
            }

            bool decidedBefore = before == "allowed" || before == "not-needed";
            items.Add(RuntimeJson.Object(
                ("kind", JsonTreeNode.FromString(isUse ? "data-use" : "data-mapping")),
                ("id", JsonTreeNode.FromString(row.Id)),
                ("recordNumber", RuntimeJson.OptionalText(row.Text("recordNumber"))),
                ("businessApplicationId", RuntimeJson.OptionalText(row.Text("businessApplication"))),
                ("domainKey", JsonTreeNode.FromString(row.Text("dataDomain")!)),
                ("stateBefore", JsonTreeNode.FromString(before)),
                ("stateAfter", JsonTreeNode.FromString(after)),
                ("breaksRule", JsonTreeNode.FromBoolean(after == "breaks-rule" && before != "breaks-rule")),
                ("needsNewDecision", JsonTreeNode.FromBoolean(decidedBefore && (after == "needed" || after == "re-decide" || after == "no-one-to-ask")))));
        }

        return JsonTreeNode.CreateArray(items);
    }

    // Who a requirement with a data owner subject asks (docs/architecture/data-taxonomy.md, Who answers): the acting holder of the owner role on the data
    // domain, its parent or the location, by name. Null when the requirement resolves on the application.
    private static AskTarget? DataAsk(DecisionScope scope, JsonTreeNode requirement)
    {
        string? subject = RuntimeJson.Text(requirement, "ownerSubject");
        string? role = RuntimeJson.Text(requirement, "approverOwnerRoleKey");
        if (subject == null || role == null || scope.DataTree == null)
        {
            return null;
        }

        string day = scope.AsOfDay;
        OwnerResolution resolution;
        switch (subject)
        {
            case "data-domain":
                resolution = scope.DataDomainKey == null ? OwnerResolution.Nobody(role) : scope.DataTree.ResolveOnDomain(role, scope.DataDomainKey, scope.DataOwners, scope.DataPrincipals, day);
                break;
            case "parent-data-domain":
                string? parent = scope.DataDomainKey == null ? null : scope.DataTree.ParentKey(scope.DataDomainKey);
                resolution = parent == null ? OwnerResolution.Nobody(role) : scope.DataTree.ResolveOnDomain(role, parent, scope.DataOwners, scope.DataPrincipals, day);
                break;
            case "data-location":
                resolution = scope.DataLocationId == null ? OwnerResolution.Nobody(role) : scope.DataTree.ResolveOnLocation(role, scope.DataLocationId, scope.DataOwners, scope.DataPrincipals, day);
                break;
            default:
                return null;
        }

        return new AskTarget(null, role, resolution.ActingPrincipalId);
    }

    // The acting data owners of the domains a configuration draft touches, once each (touched-data-domains).
    private static IReadOnlyList<string> TouchedOwners(DecisionScope scope, string role)
    {
        if (scope.DataTree == null)
        {
            return Array.Empty<string>();
        }

        string day = scope.AsOfDay;
        return scope.TouchedDomainKeys
            .Select(k => scope.DataTree.ResolveOnDomain(role, k, scope.DataOwners, scope.DataPrincipals, day).ActingPrincipalId)
            .Where(p => p != null)
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- OP-07: the data use counts of an application (docs/architecture/data-taxonomy.md, Gate requirements) ----

    private JsonTreeNode? DataUseCounts(RuntimeCall call, StoredRow? app)
    {
        var tree = new DataTree(call.Snapshot.Snapshot);
        if (app == null || tree.Mode == DataTaxonomyMode.Off)
        {
            return null;
        }

        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("uses", RowQuery.For("data-use").Where("businessApplication", app.Id))) });
        call.FactRounds += facts.Rounds;
        List<StoredRow> open = facts.All("uses").Where(u => DataTree.IsOpenOn(u.Text("endedOn"), DataTree.DayOf(call.AsOfUtc)) && u.Text("statusMeaning") != "archived" && u.Text("dataDomain") != null).ToList();
        int undecided = 0;
        int retention = 0;
        foreach (StoredRow use in open.Where(u => u.Text("statusMeaning") == "registered"))
        {
            JsonTreeNode input = UseInput(call, use);
            EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateDataUse, call.Snapshot, input);
            if (result.Outcome != "not-needed" && result.Outcome != "allowed")
            {
                undecided++;
            }

            string? location = use.Text("dataLocation");
            if (use.Text("access") != "read" && location != null)
            {
                FactSet m = FactSet.Run(call.Tx, f => new[]
                {
                    FactPlan.Of(
                        FactQuery.Where("sor", RowQuery.For("data-mapping").Where("dataDomain", use.Text("dataDomain")).Where("dataLocation", location).Where("dataOrigin", "system-of-record")),
                        FactQuery.Where("retention", RowQuery.For("decision").Where("subjectDataUse", use.Id).Where("decisionType", EngineKeys.DataRetentionDecisionType).Where("statusMeaning", "accepted"))),
                });
                call.FactRounds += m.Rounds;
                if (m.All("sor").Any(r => DataTree.IsOpenOn(r.Text("endedOn"), DataTree.DayOf(call.AsOfUtc))) && m.All("retention").Count == 0)
                {
                    retention++;
                }
            }
        }

        return RuntimeJson.Object(
            ("declaredCount", JsonTreeNode.FromInt64(open.Count)),
            ("holdsNoBusinessData", JsonTreeNode.FromBoolean(app.Flag("holdsNoBusinessData"))),
            ("undecidedCount", JsonTreeNode.FromInt64(undecided)),
            ("retentionOpenCount", JsonTreeNode.FromInt64(retention)));
    }

    // The other open use with the same second key of O27 (application, domain, location, Reaches AI) as the use being
    // written, by its record number, or null (G-18; data design review: the key was marked engine and never checked).
    private static string? DuplicateUse(RuntimeCall call, WriteSpec spec, StoredRow? before, Func<string, string?> column)
    {
        string? app = column("businessApplication");
        string? domain = column("dataDomain");
        if (app == null || domain == null)
        {
            return null;
        }

        bool reachesAi = spec.After != null && spec.After.TryGetMember("reachesAi", out JsonTreeNode? flag) ? flag.Kind == JsonTreeKind.True : before?.Flag("reachesAi") == true;
        string? location = column("dataLocation");
        string day = DataTree.DayOf(call.AsOfUtc);
        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("same", RowQuery.For("data-use").Where("businessApplication", app).Where("dataDomain", domain))) });
        call.FactRounds += facts.Rounds;
        StoredRow? other = facts.All("same").FirstOrDefault(u =>
            !string.Equals(u.Id, spec.RowId, StringComparison.OrdinalIgnoreCase)
            && DataTree.IsOpenOn(u.Text("endedOn"), day) && u.Text("statusMeaning") != "archived"
            && u.Flag("reachesAi") == reachesAi
            && string.Equals(u.Text("dataLocation") ?? string.Empty, location ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        return other == null ? null : other.Text("recordNumber") ?? other.Id;
    }

    // The alternate key of O25 (G-18; docs/architecture/data-taxonomy.md): another data location in service with the same Data
    // Platform and Url, neither with an External Id. Only asked when the written row has a URL and no External Id.
    private static string? DuplicateLocation(RuntimeCall call, WriteSpec spec, Func<string, string?> column)
    {
        string? platform = column("dataPlatform");
        string? url = column("url");
        if (platform == null || url == null || column("externalId") != null || column("retiredOn") != null)
        {
            return null;
        }

        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("same", RowQuery.For("data-location").Where("dataPlatform", platform).Where("url", url))) });
        call.FactRounds += facts.Rounds;
        StoredRow? other = facts.All("same").FirstOrDefault(l =>
            !string.Equals(l.Id, spec.RowId, StringComparison.OrdinalIgnoreCase)
            && l.Text("retiredOn") == null && l.Text("externalId") == null && l.Text("statusMeaning") != "archived");
        return other == null ? null : other.Text("labelAsSeen") ?? other.Id;
    }

    // ---- OP-08: the facts of G-20, G-23, G-24 and G-25 ----

    private static DataWriteFacts? DataWriteFactsOf(RuntimeCall call, WriteSpec spec, StoredRow? before)
    {
        string? Column(string name) => RuntimeJson.Text(spec.After, name) ?? before?.Text(name);
        switch (spec.Entity)
        {
            case "data-use":
                return new DataWriteFacts(locationStageKey: LocationStage(call, Column("dataLocation")), duplicateUse: DuplicateUse(call, spec, before, Column));
            case "data-mapping":
                return new DataWriteFacts(locationStageKey: LocationStage(call, Column("dataLocation")));
            case "data-location":
            {
                string? parent = Column("parentLocation");
                StoredRow? parentRow = parent == null ? null : call.Tx.Get("data-location", parent);
                string? target = Column("deploymentTarget");
                StoredRow? targetRow = target == null ? null : call.Tx.Get("deployment-target", target);
                return new DataWriteFacts(
                    parentLocation: parentRow == null ? null : new DataParentLocationFact(parentRow.Text("parentLocation") != null, parentRow.Text("targetStage")),
                    deploymentTargetStageKey: targetRow?.Text("targetStage"),
                    duplicateLocation: DuplicateLocation(call, spec, Column));
            }

            case "decision" when Column("decisionType") == EngineKeys.DataUseDecisionType:
            {
                string? useId = Column("subjectDataUse");
                string? mappingId = Column("subjectDataMapping");
                StoredRow? subject = useId != null ? call.Tx.Get("data-use", useId) : mappingId != null ? call.Tx.Get("data-mapping", mappingId) : null;
                string? domain = subject?.Text("dataDomain");
                if (subject == null || domain == null)
                {
                    return null;
                }

                bool isMapping = useId == null;
                return new DataWriteFacts(decisionSubject: new DataDecisionSubjectFact(
                    isMapping ? EngineKeys.DataMappingSubjectKind : EngineKeys.DataUseSubjectKind,
                    domain,
                    !isMapping && subject.Flag("reachesAi"),
                    !isMapping && subject.Flag("isDeidentified"),
                    isMapping && EngineContract.DataOrigins.TryParse(subject.Text("dataOrigin"), out DataOrigin origin) ? origin : null,
                    LocationStage(call, subject.Text("dataLocation"))));
            }

            default:
                return null;
        }
    }
}
