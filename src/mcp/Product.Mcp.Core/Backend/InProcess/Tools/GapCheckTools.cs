using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Gap check toolset (tools 35 to 39 and 49) of document 24, section 6.9. The live reads (inventory, Graph) come through
// ports (LiveReadPorts); the comparison is OP-13; the state of a run lives in its O22 row, never in the server.
internal static partial class ToolHandlers
{
    // ---- 35. run_gap_check ----

    private static JsonTreeNode RunGapCheck(ToolScope s)
    {
        s.RequireAiState("as-reported");
        IInventoryReader inventory = s.Options.Inventory ?? throw s.Upstream("The Power Platform inventory", "no inventory client is bound to this server; compare_inventory_list is the fallback");
        List<string> estates = s.Strings("estateKeys").Count > 0 ? s.Strings("estateKeys").ToList() : s.ActiveRows("list-value").Where(v => v.ParentKey == "estate").Select(v => v.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (string estate in estates)
        {
            RequireListValue(s, "estate", estate, "estateKeys");
        }

        // skipToken: "<estate index>|<inventory token>", so the next call continues the same run.
        int estateIndex = 0;
        string? inventoryToken = null;
        if (s.Text("skipToken") is string token)
        {
            string[] parts;
            try
            {
                parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split(new[] { '|' }, 2);
            }
            catch (FormatException)
            {
                throw s.Invalid("skipToken", "skipToken is not a token this tool returned.");
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out estateIndex))
            {
                throw s.Invalid("skipToken", "skipToken is not a token this tool returned.");
            }

            inventoryToken = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        }

        if (estateIndex >= estates.Count)
        {
            throw s.Invalid("skipToken", "skipToken does not belong to this run's estates.");
        }

        string estateKey = estates[estateIndex];
        DirectoryAnswer<InventoryPage> page = inventory.Query(s.Session.Caller, estateKey, s.Strings("environmentIds"), inventoryToken);
        if (!page.Answered)
        {
            throw s.Upstream("The Power Platform inventory", page.Problem);
        }

        bool finalPage = page.Value.NextSkipToken == null;
        var items = page.Value.Items.Select(i => new InventoryItem(i.ExternalId, i.SourceType, i.LabelAsSeen, i.TargetAsSeen, i.ComponentKindKey, i.SuggestedClaimantObjectId, i.Url)).ToList();
        bool compareOnly = s.Flag("compareOnly") ?? false;
        bool includeVanished = s.Flag("includeVanished") ?? true;
        JsonTreeNode data = GapCheck(s, estates, estateKey, items, finalPage && includeVanished, isLiveQuery: true, writeClaims: !compareOnly, page.Value.AdminRoleUsed, sourceNote: null);
        bool more = !finalPage || estateIndex + 1 < estates.Count;
        string? next = !more ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(finalPage ? (estateIndex + 1).ToString(CultureInfo.InvariantCulture) + "|" : estateIndex.ToString(CultureInfo.InvariantCulture) + "|" + page.Value.NextSkipToken));
        data = Js.With(data, ("more", JsonTreeNode.FromBoolean(more)), ("skipToken", Js.OptStr(next)));
        return s.Answer(data, GapSummary(data), null, more ? new[] { ToolScope.Next("run_gap_check", ("gapCheckRunId", Js.Member(data, "gapCheckRunId")), ("skipToken", JsonTreeNode.FromString(next!))) } : Array.Empty<NextStep>());
    }

    // ---- 36. compare_inventory_list ----

    private static JsonTreeNode CompareInventoryList(ToolScope s)
    {
        s.RequireAiState("as-reported");
        string estate = s.RequireText("estateKey");
        RequireListValue(s, "estate", estate, "estateKey");
        var items = s.Items("items").Select(i => new InventoryItem(
            Js.Text(i, "externalId")!,
            Js.Text(i, "sourceType") ?? Js.Text(i, "componentKindKey") ?? "unknown",
            Js.Text(i, "labelAsSeen")!,
            Js.Text(i, "targetAsSeen"),
            Js.Text(i, "componentKindKey"),
            Js.Text(i, "suggestedClaimantObjectId"),
            ItemUrl(s, Js.Text(i, "url")))).ToList();
        JsonTreeNode data = GapCheck(s, new List<string> { estate }, estate, items, s.Flag("isFinalPage") ?? false, isLiveQuery: false, writeClaims: true, adminRoleUsed: null, sourceNote: s.RequireText("sourceNote"));
        data = Js.With(data, ("more", JsonTreeNode.False));
        return s.Answer(data, GapSummary(data) + " The list came from your own tool, so the run is marked as reported.");
    }

    // The link an inventory item carries (O23 Url): an https link only, as every link the product shows.
    private static string? ItemUrl(ToolScope s, string? url)
    {
        if (url == null)
        {
            return null;
        }

        if (!url.StartsWith("https://", StringComparison.Ordinal))
        {
            throw s.Invalid("items.url", "An item's url is an https link that opens the item in its tool.");
        }

        return url;
    }

    private static string GapSummary(JsonTreeNode data)
    {
        return Count((int)(Js.Number(data, "itemsSeen") ?? 0), "item", "items") + " seen, " + Count(Js.Items(data, "newClaims").Count, "new claim", "new claims") + ", " + Js.Items(data, "vanished").Count + " vanished.";
    }

    // One page of a gap check run: the O22 row, OP-13 over the page, and the claim writes OP-13 plans.
    private static JsonTreeNode GapCheck(ToolScope s, List<string> estates, string estateKey, List<InventoryItem> items, bool finalPage, bool isLiveQuery, bool writeClaims, string? adminRoleUsed, string? sourceNote)
    {
        string? runId = s.Text("gapCheckRunId");
        StoredRow? run = runId == null ? null : s.RequireRow("gap-check-run", runId, "gapCheckRunId");
        DateTime started = run == null ? s.Now : Js.ParseMoment(run.Text("startedOn")) ?? s.Now;
        string admin = adminRoleUsed ?? run?.Text("adminRoleUsed") ?? (isLiveQuery ? "unknown" : "as reported: " + (sourceNote ?? "the admin's own tool"));
        ComposedWrite runWrite = run == null
            ? s.Create("gap-check-run", Js.Obj(
                ("runBy", JsonTreeNode.FromString(s.PersonId)),
                ("adminRoleUsed", JsonTreeNode.FromString(admin.Length > 200 ? admin.Substring(0, 200) : admin)),
                ("estatesCovered", Js.Strs(estates)),
                ("startedOn", Js.Utc(s.Now)),
                ("itemsSeen", JsonTreeNode.FromInt64(0)),
                ("newClaims", JsonTreeNode.FromInt64(0)),
                ("vanishedClaims", JsonTreeNode.FromInt64(0))))
            : new ComposedWrite(run.Id, false, false, true, null);
        runId = runWrite.RowId;

        // Registered components in the covered estate. O03 holds no estate key, so a component counts as in the estate
        // when its kind is gap-check covered (judgment call).
        var covered = new HashSet<string>(s.ActiveRows("component-kind").Where(k => Js.Flag(k.Columns, "isGapCheckCovered") == true).Select(k => k.KeyPath), StringComparer.Ordinal);
        var components = s.Rows.Query(RowQuery.For("technical-component")).Where(c => c.Text("externalId") != null && covered.Contains(c.Text("componentKind") ?? string.Empty)).ToList();
        var claims = s.Rows.Query(RowQuery.For("claim")).ToList();

        // The components an earlier page of this run stamped, by row ID: OP-13's seenThisRun holds Technical Component IDs
        // (wave 4b review: it held External Ids, so on a run of more than one page every component of an earlier page raised a
        // vanished claim on the last page). A claim needs none: its Last Seen Run already keeps Times Seen to one a run.
        var seen = components.Where(c => (Js.ParseMoment(c.Text("lastSeenOn")) ?? DateTime.MinValue) >= started && run != null).Select(c => c.Id)
            .Distinct(StringComparer.Ordinal).ToList();
        JsonTreeNode input = Js.Obj(
            ("gapCheckRunId", JsonTreeNode.FromString(runId)),
            ("coveredEstateKeys", Js.Strs(new[] { estateKey })),
            ("isFinalPage", JsonTreeNode.FromBoolean(finalPage)),
            ("isLiveQuery", JsonTreeNode.FromBoolean(isLiveQuery)),
            ("items", Arr(items.Select(i => Js.Obj(
                ("externalId", JsonTreeNode.FromString(i.ExternalId)),
                ("sourceType", JsonTreeNode.FromString(i.SourceType)),
                ("componentKindKey", Js.OptStr(i.ComponentKindKey)),
                ("estateKey", JsonTreeNode.FromString(estateKey)),
                ("labelAsSeen", JsonTreeNode.FromString(i.LabelAsSeen)),
                ("targetAsSeen", Js.OptStr(i.TargetAsSeen)),
                ("suggestedClaimantObjectId", Js.OptStr(i.SuggestedClaimantObjectId)))))),
            ("components", Arr(components.Select(c => Js.Obj(
                ("technicalComponentId", JsonTreeNode.FromString(c.Id)),
                ("externalId", JsonTreeNode.FromString(c.Text("externalId")!)),
                ("componentKindKey", JsonTreeNode.FromString(c.Text("componentKind")!)),
                ("estateKey", JsonTreeNode.FromString(estateKey)),
                ("isRetired", JsonTreeNode.FromBoolean(c.Text("retiredOn") != null)))))),
            ("claims", Arr(claims.Where(c => c.Text("claimKey") != null).Select(c => Js.Obj(
                ("claimId", JsonTreeNode.FromString(c.Id)),
                ("claimKey", JsonTreeNode.FromString(c.Text("claimKey")!)),
                ("statusMeaning", JsonTreeNode.FromString(c.Text("statusMeaning") ?? "open")),
                ("findingKind", JsonTreeNode.FromString(c.Text("findingKind") ?? "unregistered")),
                ("timesSeen", JsonTreeNode.FromInt64(c.Number("timesSeen") ?? 1)),
                ("lastSeenRunId", Js.OptStr(c.Text("lastSeenRun"))))))),
            ("seenThisRun", Js.Strs(seen)));
        EvaluationResult<JsonTreeNode?> compared = s.Call.Evaluate(EngineOperation.CompareGapCheckResult, input);
        if (compared.Payload == null)
        {
            Reason? reason = compared.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
            throw s.Call.Fail(compared.Outcome == "unavailable" ? "unavailable" : "invalid-input", reason?.Code ?? "input.malformed", reason?.Message ?? "The gap check comparison did not run.", reason?.Entity, reason?.Ref);
        }

        JsonTreeNode plan = compared.Payload;

        // The item's link (O23 Url, document 23, B23-10): OP-13 compares IDs and never reads links, so the link rides
        // beside the comparison, by External Id, onto the claim it creates or updates.
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (InventoryItem item in items.Where(i => i.Url != null))
        {
            urls[item.ExternalId] = item.Url!;
        }

        var newClaims = new List<JsonTreeNode>();
        var vanished = new List<JsonTreeNode>();
        int lastSeen = 0;
        if (writeClaims)
        {
            foreach (string component in Js.Strings(plan, "componentLastSeen"))
            {
                s.Update("technical-component", component, Js.Obj(("lastSeenOn", Js.Utc(s.Now))));
                lastSeen++;
            }

            foreach (JsonTreeNode claim in Js.Items(plan, "updatedClaims").Concat(Js.Items(plan, "vanishedClaims").Where(c => Js.Text(c, "claimId") != null)))
            {
                StoredRow? stored = s.Rows.Get("claim", Js.Text(claim, "claimId")!);
                string? link = stored?.Text("externalId") is string seenId && urls.TryGetValue(seenId, out string? found) && !Same(found, stored.Text("url")) ? found : null;
                s.Update("claim", Js.Text(claim, "claimId")!, Js.Obj(
                    ("lastSeenRun", JsonTreeNode.FromString(runId)),
                    ("lastSeenOn", Js.Utc(s.Now)),
                    ("timesSeen", JsonTreeNode.FromInt64(Js.Number(claim, "timesSeen") ?? 1)),
                    ("url", Js.OptStr(link))));
                if (Js.Text(claim, "technicalComponentId") is string vanishedComponent)
                {
                    vanished.Add(Js.Obj(("claimId", JsonTreeNode.FromString(Js.Text(claim, "claimId")!)), ("technicalComponentId", JsonTreeNode.FromString(vanishedComponent))));
                }
            }
        }
        else
        {
            lastSeen = Js.Strings(plan, "componentLastSeen").Count;
        }

        foreach (JsonTreeNode claim in Js.Items(plan, "newClaims").Concat(Js.Items(plan, "vanishedClaims").Where(c => Js.Text(c, "claimId") == null)))
        {
            bool isVanished = Js.Text(claim, "findingKind") == "vanished";
            StoredRow? component = Js.Text(claim, "technicalComponentId") is string componentId ? s.Rows.Get("technical-component", componentId) : null;
            string label = Js.Text(claim, "labelAsSeen") ?? component?.Text("labelAsSeen") ?? Js.Text(claim, "externalId") ?? "unknown";
            string? claimId = null;
            if (writeClaims)
            {
                claimId = s.Create("claim", Js.Obj(
                    ("gapCheckRun", JsonTreeNode.FromString(runId)),
                    ("lastSeenRun", JsonTreeNode.FromString(runId)),
                    ("findingKind", JsonTreeNode.FromString(Js.Text(claim, "findingKind") ?? "unregistered")),
                    ("claimKey", JsonTreeNode.FromString(Js.Text(claim, "claimKey")!)),
                    ("componentKind", Js.OptStr(Js.Text(claim, "componentKindKey"))),
                    ("sourceTypeAsSeen", JsonTreeNode.FromString(Js.Text(claim, "sourceType") ?? "unknown")),
                    ("externalId", JsonTreeNode.FromString(Js.Text(claim, "externalId")!)),
                    ("url", Js.OptStr(urls.TryGetValue(Js.Text(claim, "externalId")!, out string? claimUrl) ? claimUrl : component?.Text("url"))),
                    ("labelAsSeen", JsonTreeNode.FromString(label)),
                    ("targetAsSeen", Js.OptStr(Js.Text(claim, "targetAsSeen"))),
                    ("suggestedClaimant", Js.OptStr(Js.Text(claim, "suggestedClaimantObjectId"))),
                    ("registeredComponent", Js.OptStr(component?.Id)),
                    ("status", JsonTreeNode.FromString(s.StatusFor("claim", "open"))),
                    ("lastSeenOn", Js.Utc(s.Now)),
                    ("timesSeen", JsonTreeNode.FromInt64(Js.Number(claim, "timesSeen") ?? 1)))).RowId;
            }

            JsonTreeNode? claimRef = s.Call.IsDryRun || claimId == null ? JsonTreeNode.Null : JsonTreeNode.FromString(claimId);
            if (isVanished && component != null)
            {
                vanished.Add(Js.Obj(("claimId", claimRef), ("technicalComponentId", JsonTreeNode.FromString(component.Id))));
            }
            else if (!isVanished)
            {
                newClaims.Add(Js.Obj(
                    ("claimId", claimRef),
                    ("componentKindKey", Js.Str(Js.Text(claim, "componentKindKey"))),
                    ("labelAsSeen", JsonTreeNode.FromString(label)),
                    ("targetAsSeen", Js.Str(Js.Text(claim, "targetAsSeen"))),
                    ("suggestedClaimant", s.PrincipalRef(Js.Text(claim, "suggestedClaimantObjectId")))));
            }
        }

        JsonTreeNode? counts = Js.Member(plan, "counts");
        long itemsSeen = Js.Number(counts, "itemsSeen") ?? items.Count;

        // Items the comparison left out (OP-13, rule 5: another estate, or a kind the gap check does not cover) are named,
        // so a list that reads "0 items seen" says why (fix round 2).
        long left = Js.Number(counts, "ignored") ?? 0;
        if (left > 0)
        {
            s.Call.Warn("gap-check.items-left-out", Count((int)left, "item was", "items were") + " left out: the gap check covers only the estate it reads and the component kinds marked for it. Give each item its kind, such as cloud-flow.", "Gap Check Run", runId);
        }
        StoredRow runRow = s.Rows.Get("gap-check-run", runId)!;
        ComposedWrite update = s.Update("gap-check-run", runId, Js.Obj(
            ("itemsSeen", JsonTreeNode.FromInt64((runRow.Number("itemsSeen") ?? 0) + itemsSeen)),
            ("newClaims", JsonTreeNode.FromInt64((runRow.Number("newClaims") ?? 0) + newClaims.Count)),
            ("vanishedClaims", JsonTreeNode.FromInt64((runRow.Number("vanishedClaims") ?? 0) + vanished.Count)),
            ("finishedOn", finalPage ? Js.Utc(s.Now) : null)));
        StoredRow written = s.Rows.Get("gap-check-run", runId)!;
        return ToolScope.Merge(
            s.WriteResult(written, runWrite.Created, hasStatus: false),
            ("gapCheckRunId", s.Call.IsDryRun && runWrite.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(runId)),
            ("adminRoleUsed", Js.Str(written.Text("adminRoleUsed"))),
            ("estatesCovered", Js.Strs(estates)),
            ("itemsSeen", JsonTreeNode.FromInt64(itemsSeen)),
            ("newClaims", Arr(newClaims)),
            ("vanished", Arr(vanished)),
            ("lastSeenUpdated", JsonTreeNode.FromInt64(lastSeen)),
            ("evidenceLevelKey", JsonTreeNode.FromString(isLiveQuery ? "read" : "as-reported")));
    }

    // ---- 49. run_owner_check ----

    private static JsonTreeNode RunOwnerCheck(ToolScope s)
    {
        s.RequireAiState("as-reported");
        IDirectoryReader directory = s.Options.Directory ?? throw s.Upstream("Microsoft Graph", "this install has no connection to the directory");
        // A demo's fictional directory (wave 4b QA): the run says so, in its admin role used and its summary, as a gap check
        // over the demo's fictional inventory does. It never claims a live read of Entra.
        bool demoDirectory = directory is IDemoDirectory;
        IReadOnlyList<string> appFilter = s.Strings("businessApplicationIds");
        IReadOnlyList<string> principalFilter = s.Strings("principalObjectIds");
        var assignments = s.Rows.Query(RowQuery.For("owner-assignment"))
            .Where(o => o.Text("endOn") == null && o.Text("principal") != null)
            .Where(o => appFilter.Count == 0 || appFilter.Contains(o.Text("businessApplication") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .Where(o => principalFilter.Count == 0 || principalFilter.Contains(o.Text("principal")!, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var principals = assignments.Select(o => o.Text("principal")!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList();
        List<string> page = s.Page(principals, out string? next);

        string? runId = s.Text("gapCheckRunId");
        ComposedWrite run = runId == null
            ? s.Create("gap-check-run", Js.Obj(
                ("runBy", JsonTreeNode.FromString(s.PersonId)),
                ("adminRoleUsed", JsonTreeNode.FromString(demoDirectory ? DemoDirectoryMarker : s.Session.Caller.DirectoryRoleIds.FirstOrDefault() ?? "unknown")),
                ("estatesCovered", Js.Strs(new[] { "entra-owners" })),
                ("startedOn", Js.Utc(s.Now)),
                ("itemsSeen", JsonTreeNode.FromInt64(0))))
            : new ComposedWrite(s.RequireRow("gap-check-run", runId, "gapCheckRunId").Id, false, false, true, null);
        string reviewType = s.ConfigRow("review-type", "owner-check") is SnapshotRow r && r.IsActive ? "owner-check" : throw s.Call.Fail("unavailable", "meaning.missing", "The owner-check review type is not active, so no owner check review can open.", "Review Type", "owner-check");
        var findings = new List<JsonTreeNode>();
        foreach (string principal in page)
        {
            DirectoryAnswer<DirectoryUserState> state = directory.UserState(s.Session.Caller, principal);
            if (!state.Answered)
            {
                throw s.Upstream("Microsoft Graph", state.Problem);
            }

            string directoryState = state.Value.State;
            string? leavesOn = state.Value.LeavesOnUtc.HasValue ? state.Value.LeavesOnUtc.Value.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture) : null;
            if (s.Principal(principal) is StoredRow reference)
            {
                s.Call.RecordPrincipalFacts(reference.Id, leavesOn, directoryState);
            }

            if (directoryState == "enabled" && leavesOn == null)
            {
                continue;
            }

            var apps = new List<JsonTreeNode>();
            var reviews = new List<string>();
            foreach (IGrouping<string, StoredRow> byApp in assignments.Where(a => Same(a.Text("principal"), principal) && a.Text("businessApplication") != null).GroupBy(a => a.Text("businessApplication")!, StringComparer.Ordinal))
            {
                IReadOnlyList<StoredRow> owners = s.CurrentOwners(byApp.Key);
                foreach (StoredRow assignment in byApp.Where(a => !a.Flag("isDeputy")))
                {
                    string role = assignment.Text("ownerRole") ?? string.Empty;
                    apps.Add(Js.Obj(("businessApplicationId", JsonTreeNode.FromString(byApp.Key)), ("ownerRoleKey", JsonTreeNode.FromString(role)), ("hasDeputy", JsonTreeNode.FromBoolean(owners.Any(o => o.Text("ownerRole") == role && o.Flag("isDeputy"))))));
                }

                StoredRow? open = s.Query("review", ("businessApplication", byApp.Key), ("reviewType", reviewType), ("statusMeaning", "due")).FirstOrDefault();
                string reviewId = open?.Id ?? s.Create("review", Js.Obj(
                    ("businessApplication", JsonTreeNode.FromString(byApp.Key)),
                    ("reviewType", JsonTreeNode.FromString(reviewType)),
                    ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
                    ("trigger", JsonTreeNode.FromString("owner-check")),
                    ("dueOn", JsonTreeNode.FromString(s.PersonToday)))).RowId;
                if (!s.Call.IsDryRun)
                {
                    reviews.Add(reviewId);
                }
            }

            findings.Add(Js.Obj(
                ("principal", s.PrincipalRef(principal)),
                ("directoryState", JsonTreeNode.FromString(directoryState)),
                ("leavesOn", Js.Str(leavesOn)),
                ("applications", Arr(apps)),
                ("dataSubjects", Arr(AffectedDataSubjects(s, assignments.Where(a => Same(a.Text("principal"), principal))))),
                ("ownerCheckReviewIds", Ids(reviews))));
        }

        StoredRow runRow = s.Rows.Get("gap-check-run", run.RowId)!;
        s.Update("gap-check-run", run.RowId, Js.Obj(("itemsSeen", JsonTreeNode.FromInt64((runRow.Number("itemsSeen") ?? 0) + page.Count)), ("finishedOn", next == null ? Js.Utc(s.Now) : null)));
        StoredRow written = s.Rows.Get("gap-check-run", run.RowId)!;
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, run.Created, hasStatus: false),
            ("gapCheckRunId", s.Call.IsDryRun && run.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(run.RowId)),
            ("principalsChecked", JsonTreeNode.FromInt64(page.Count)),
            ("findings", Arr(findings)),
            ("more", JsonTreeNode.FromBoolean(next != null)),
            ("skipToken", Js.OptStr(next)));
        return s.Answer(data, Count(page.Count, "owner", "owners") + (demoDirectory ? " checked in the demo's fictional directory, " : " checked live in Entra, ") + Count(findings.Count, "finding", "findings") + ".");
    }

    // The admin role an owner check over a demo's fictional directory records (DemoDirectoryReader.AdminRoleUsedMarker).
    private const string DemoDirectoryMarker = "demo: the demo's fictional directory";

    // ---- 37. list_claims ----

    private static JsonTreeNode ListClaims(ToolScope s)
    {
        IReadOnlyList<string> meanings = Js.Has(s.Args, "statusMeanings") ? s.Strings("statusMeanings") : new[] { "open" };
        IReadOnlyList<string> kinds = s.Strings("findingKinds");
        IReadOnlyList<string> componentKinds = s.Strings("componentKindKeys");
        bool mine = s.Flag("suggestedToMe") ?? false;
        int hidden = 0;
        var visible = new List<StoredRow>();
        foreach (StoredRow claim in s.Rows.Query(RowQuery.For("claim")))
        {
            if (!meanings.Contains(claim.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal)
                || (kinds.Count > 0 && !kinds.Contains(claim.Text("findingKind") ?? string.Empty, StringComparer.Ordinal))
                || (componentKinds.Count > 0 && !componentKinds.Contains(claim.Text("componentKind") ?? string.Empty, StringComparer.Ordinal))
                || (mine && !Same(claim.Text("suggestedClaimant"), s.PersonId)))
            {
                continue;
            }

            if (!CanReadClaim(s, claim))
            {
                hidden++;
                continue;
            }

            visible.Add(claim);
        }

        s.HideFromCaller("claim", hidden);
        var ordered = visible.OrderBy(c => c.Text("labelAsSeen"), StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(c => Js.Obj(
            ("claimId", JsonTreeNode.FromString(c.Id)),
            ("findingKind", JsonTreeNode.FromString(c.Text("findingKind") ?? "unregistered")),
            ("status", s.StatusValue(c)),
            ("componentKind", s.LabelledKey("component-kind", c.Text("componentKind"))),
            ("labelAsSeen", JsonTreeNode.FromString(c.Text("labelAsSeen") ?? string.Empty)),
            ("targetAsSeen", Js.Str(c.Text("targetAsSeen"))),
            ("url", c.Text("url") is string url && url.StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(url) : JsonTreeNode.Null),
            ("suggestedClaimant", s.PrincipalRef(c.Text("suggestedClaimant"))),
            ("claimedBy", s.PrincipalRef(c.Text("claimedBy"))),
            ("claimedOn", ToolScope.MomentOrNull(c.Text("claimedOn"))),
            ("resultingComponentId", Js.Str(c.Text("resultingComponent"))),
            ("businessApplicationId", Js.Str(c.Text("resultingComponent") is string comp ? s.Rows.Get("technical-component", comp)?.Text("businessApplication") : null)),
            ("ownershipDecisionId", Js.Str(s.Query("decision", ("subjectClaim", c.Id)).OrderByDescending(d => d.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal).Select(d => d.Id).FirstOrDefault())),
            ("intakeRequestId", Js.Str(ClaimIntake(s, c)?.Id)),
            ("foundOn", Js.Utc(Js.ParseMoment(s.Rows.Get("gap-check-run", c.Text("gapCheckRun") ?? string.Empty)?.Text("startedOn")) ?? Js.ParseMoment(c.Text("engineCheckedOn")) ?? s.Now)),
            ("lastSeenOn", ToolScope.MomentOrNull(c.Text("lastSeenOn"))))));
        return s.Answer(Js.Obj(("items", items), ("skipToken", Js.OptStr(next))), Count(ordered.Count, "claim matches", "claims match") + (hidden > 0 ? "; " + hidden + " are not visible to you." : "."));
    }

    // ---- 38. claim_resource ----

    private static JsonTreeNode ClaimResource(ToolScope s)
    {
        StoredRow claim = s.RequireRow("claim", s.Text("claimId"), "claimId");
        if (!CanReadClaim(s, claim))
        {
            throw s.NotFound("Claim", claim.Id, "claimId");
        }

        if (claim.Text("findingKind") == "vanished")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "A vanished claim is resolved with link_component (moved or retired), not claimed.");
        }

        if (claim.Text("statusMeaning") != "open")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The claim is " + claim.Text("statusMeaning") + ", so it cannot be claimed.");
        }

        // P06 step 6 (document 23, B23-10). For a new application the claim starts an intake (P01) with the component
        // pre-filled: a draft application holds the component, and a draft intake request names it as its application and
        // the claimant, or the person given, as business owner. The intake's triage decides; its acceptance registers the
        // draft and makes the named person its business owner (the update-subject effect), so no ownership decision is
        // opened. For an existing application the component is added; when the claimant holds no owner role on it, an
        // ownership decision about the claim asks the people the rules name to confirm it, and a person's claim submits
        // it at once. An AI client's claim leaves that decision proposed for the person to submit, unless the Tool Policy
        // lets it submit.
        JsonTreeNode? fresh = Js.Member(s.Args, "newApplication");
        string appId;
        bool created = false;
        string? decisionId = null;
        string? intakeId = null;
        bool submitted = false;
        JsonTreeNode requested = Js.EmptyObject;
        JsonTreeNode? engineAccepted = null;
        if (fresh != null)
        {
            s.RequireAiState("draft");
            string kindKey = s.RequireKey("application-kind", Js.Text(fresh, "applicationKindKey"), "newApplication.applicationKindKey").KeyPath;
            if (Js.Text(fresh, "portfolioId") is string portfolio)
            {
                s.RequireRow("portfolio", portfolio, "newApplication.portfolioId");
            }

            string? owner = Js.Text(fresh, "businessOwnerObjectId") is string named ? KnownBusinessOwner(s, named, "newApplication.businessOwnerObjectId") : ClaimantAsOwner(s);
            string name = Js.Text(fresh, "name")!;
            appId = s.Create("business-application", Js.Obj(
                ("name", JsonTreeNode.FromString(name)),
                ("purpose", JsonTreeNode.FromString(Js.Text(fresh, "purpose")!)),
                ("applicationKind", JsonTreeNode.FromString(kindKey)),
                ("portfolio", Js.OptStr(Js.Text(fresh, "portfolioId"))),
                ("status", JsonTreeNode.FromString(s.StatusFor("business-application", "draft"))))).RowId;
            created = true;
            string where = claim.Text("targetAsSeen") is string target ? " in " + target : string.Empty;
            intakeId = s.Create("intake-request", Js.Obj(
                ("requester", JsonTreeNode.FromString(s.PersonId)),
                ("title", JsonTreeNode.FromString(name)),
                ("need", JsonTreeNode.FromString(Js.Text(fresh, "purpose")! + " The gap check found it running unregistered: " + (claim.Text("labelAsSeen") ?? claim.Id) + where + ".")),
                ("proposedKind", JsonTreeNode.FromString(kindKey)),
                ("businessApplication", JsonTreeNode.FromString(appId)),
                ("businessOwner", Js.OptStr(owner)),
                ("status", JsonTreeNode.FromString(s.StatusFor("intake-request", "draft"))))).RowId;
        }
        else
        {
            appId = RequireApplication(s).Id;
            if (!s.OwnsApplication(appId))
            {
                s.RequireAiState("proposed");
                StoredRow target = s.Rows.Get("business-application", appId)!;
                decisionId = s.Create("decision", Js.Obj(
                    ("decisionType", JsonTreeNode.FromString(EngineKeys.OwnershipDecisionType)),
                    ("subjectKind", JsonTreeNode.FromString("claim")),
                    ("subjectClaim", JsonTreeNode.FromString(claim.Id)),
                    ("subjectApplication", JsonTreeNode.FromString(appId)),
                    ("statement", JsonTreeNode.FromString("Confirm that " + (claim.Text("labelAsSeen") ?? claim.Id) + " is part of " + AppName(target) + ".")),
                    ("context", JsonTreeNode.FromString(s.DisplayLabel(s.PersonId) + " claimed it from the gap check and holds no owner role on " + AppName(target) + ", so an owner confirms it belongs there.")),
                    ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed"))))).RowId;
                if (!s.IsAiCaller || (s.Session.ToolPolicy.AiMayReach(s.Tool, "submitted") && s.Session.ToolPolicy.AiMaySubmit(s.Tool, "decision")))
                {
                    engineAccepted = EngineAcceptance(s, decisionId, out _);
                    requested = s.Call.RequestApprovals(decisionId);
                    submitted = true;
                }
                else
                {
                    s.Call.Warn("claim.decision-not-submitted", "An AI client claimed this for the person, so the ownership decision stays proposed until the person submits it.", "Decision", decisionId);
                }
            }
            else
            {
                s.RequireAiState("link");
            }
        }

        string kind = claim.Text("componentKind") ?? throw s.Invalid("claimId", "The claim names no component kind, so it cannot become a component.");
        string externalId = claim.Text("externalId") ?? claim.Id;
        string componentId = s.Create("technical-component", Js.Obj(
            ("businessApplication", JsonTreeNode.FromString(appId)),
            ("componentKind", JsonTreeNode.FromString(kind)),
            ("linkKind", JsonTreeNode.FromString(DefaultComponentLinkKind(s, kind))),
            ("externalId", JsonTreeNode.FromString(externalId)),
            ("url", Js.OptStr(claim.Text("url"))),
            ("labelAsSeen", JsonTreeNode.FromString(claim.Text("labelAsSeen") ?? externalId)),
            ("lastSeenOn", Js.Utc(s.Now)),
            ("sourceClaim", JsonTreeNode.FromString(claim.Id)))).RowId;
        ComposedWrite write = s.Update("claim", claim.Id, Js.Obj(
            ("status", JsonTreeNode.FromString(s.StatusFor("claim", "claimed"))),
            ("claimedBy", JsonTreeNode.FromString(s.PersonId)),
            ("claimedOn", Js.Utc(s.Now)),
            ("resultingComponent", JsonTreeNode.FromString(componentId))));
        StoredRow app = s.Rows.Get("business-application", appId)!;
        var approvals = Js.Strings(requested, "approvalRecordIds").Select(r => s.Rows.Get("approval-record", r)).Where(r => r != null).Select(r => ApprovalRequested(s, r!)).ToList();
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("claim", write),
            ("technicalComponentId", s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(componentId)),
            ("businessApplicationId", s.Call.IsDryRun && created ? JsonTreeNode.Null : JsonTreeNode.FromString(appId)),
            ("applicationCreated", JsonTreeNode.FromBoolean(created)),
            ("applicationStatus", s.StatusValue(app)),
            ("intakeRequestId", intakeId == null || s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(intakeId)),
            ("ownershipDecisionId", decisionId == null || s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(decisionId)),
            ("decisionSubmitted", JsonTreeNode.FromBoolean(submitted)),
            ("approvalsRequested", Arr(s.Call.IsDryRun ? Array.Empty<JsonTreeNode>() : approvals)),
            ("engineAccepted", engineAccepted ?? JsonTreeNode.Null),
            ("approvalsNeeded", decisionId == null ? Js.EmptyArray : Arr(ApprovalsNeeded(s, decisionId, EngineKeys.OwnershipDecisionType))));
        string summary = intakeId != null
            ? "The item is claimed for a new draft application, and its intake request is started as a draft. Answer the tier questions and submit it for triage."
            : decisionId == null ? "The item is claimed and added to " + AppName(app) + "."
            : submitted ? "The item is claimed and added to " + AppName(app) + ". " + Count(approvals.Count, "owner is", "owners are") + " asked to confirm it on the decision page."
            : "The item is claimed and added to " + AppName(app) + ", with a proposed ownership decision for the person to submit.";
        var next = new List<NextStep>();
        if (intakeId != null && !s.Call.IsDryRun)
        {
            next.Add(ToolScope.Next("answer_tier_questions", ("intakeRequestId", JsonTreeNode.FromString(intakeId))));
            next.Add(ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("intake-request")), ("subjectId", JsonTreeNode.FromString(intakeId))));
        }
        else if (decisionId != null && !submitted && !s.Call.IsDryRun)
        {
            next.Add(ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decisionId))));
        }

        return s.Answer(data, summary, intakeId != null && !s.Call.IsDryRun ? s.RecordUrl("intake-request", intakeId) : s.RecordUrl("claim", claim.Id), next.ToArray());
    }

    // The claimant as the business owner a new application's intake names, when the store knows them as a person who can
    // own it; null otherwise (the requester then names one in the wizard).
    private static string? ClaimantAsOwner(ToolScope s)
    {
        StoredRow? person = s.Principal(s.PersonId);
        string? state = person?.Text("directoryState");
        if (person == null || person.Text("principalKind") != "user" || person.Flag("isErased") || state == "disabled" || state == "deleted"
            || (person.Text("leavesOn") is string leaves && string.CompareOrdinal(leaves, s.Today) <= 0))
        {
            return null;
        }

        return person.Text("entraObjectId") ?? s.PersonId;
    }

    // The intake request a claim started for a new application: the intake that names the draft application holding the
    // claim's component.
    private static StoredRow? ClaimIntake(ToolScope s, StoredRow claim)
    {
        string? app = claim.Text("resultingComponent") is string comp ? s.Rows.Get("technical-component", comp)?.Text("businessApplication") : null;
        return app == null ? null : s.Query("intake-request", ("businessApplication", app)).OrderBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    // ---- 39. dismiss_claim ----

    private static JsonTreeNode DismissClaim(ToolScope s)
    {
        s.RequireAiState("as-reported");
        StoredRow claim = s.RequireRow("claim", s.Text("claimId"), "claimId");
        bool undo = s.Flag("undo") ?? false;
        string? reasonKey = s.Text("reasonKey");
        if (reasonKey != null && s.ConfigRow("value-list", "dismiss-reason") != null)
        {
            RequireListValue(s, "dismiss-reason", reasonKey, "reasonKey");
        }

        string meaning = undo ? "open" : "dismissed";
        if (claim.Text("statusMeaning") == "claimed")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The claim is claimed, so it is no longer dismissed or reopened here.");
        }

        ComposedWrite write = s.Update("claim", claim.Id, Js.Obj(
            ("status", JsonTreeNode.FromString(s.StatusFor("claim", meaning))),
            ("dismissReason", undo ? JsonTreeNode.Null : JsonTreeNode.FromString((reasonKey == null ? string.Empty : reasonKey + ": ") + s.RequireText("reason")))));
        JsonTreeNode data = Js.Obj(s.WriteResult("claim", write).ToArray());
        return s.Answer(data, undo ? "The claim is open again." : "The claim is dismissed. Reopen brings it back.", s.RecordUrl("claim", claim.Id));
    }
}
