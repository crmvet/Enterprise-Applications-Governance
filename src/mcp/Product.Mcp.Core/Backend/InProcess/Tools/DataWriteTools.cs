using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// The data toolset, writes (tools 82 to 85; docs/architecture/data-taxonomy.md; data taxonomy slice
// 3, D-36, D-37 and D-41). Every write goes through OP-08, so the engine judges a person's write exactly as an AI
// client's: G-18 (an active, declarable domain), G-21 (an AI client drafts and never confirms), G-22 (no secrets),
// G-23 (copy origins), G-24 (two levels of locations) and G-25 (a never rule is refused for everyone, D-41 question 7 as
// the product owner changed it). A person's confirmed use or copy sends its decision to the approvers; an AI client's stays proposed.
internal static partial class ToolHandlers
{
    // ---- 82. save_data_use ----

    private static JsonTreeNode SaveDataUse(ToolScope s)
    {
        s.RequireAiState("draft");
        var tree = new DataTree(s.Snapshot);
        StoredRow app = s.RequireRow("business-application", s.Text("applicationId"), "applicationId");
        RequireOwnerRelation(s, app.Id);
        string domainKey = s.RequireText("domainKey");
        if (tree.Domain(domainKey) == null)
        {
            throw s.Invalid("domainKey", "domainKey \"" + domainKey + "\" is not a data domain in the configuration in force. Find one with find_data_domains.");
        }

        string? locationId = s.Text("locationId");
        if (locationId != null)
        {
            s.RequireRow("data-location", locationId, "locationId");
        }

        if (s.Text("aiComponentId") is string component)
        {
            s.RequireRow("technical-component", component, "aiComponentId");
        }

        RequireListValue(s, "access-approach", s.Text("accessApproachKey"), "accessApproachKey");
        string access = s.RequireText("access");
        bool reachesAi = s.Flag("reachesAi") == true;
        bool? confirm = s.Flag("confirm");
        string? endOn = s.Text("endOn");

        StoredRow? existing = s.Text("dataUseId") is string useId ? s.RequireRow("data-use", useId, "dataUseId") : OpenUseOf(s, app.Id, domainKey, locationId, reachesAi);
        if (existing != null && !Same(existing.Text("businessApplication"), app.Id))
        {
            throw s.Invalid("dataUseId", "Data use " + existing.Id + " belongs to another application.");
        }

        if (existing != null && !IsOpenDataRow(existing, s))
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "Data use " + (existing.Text("recordNumber") ?? existing.Id) + " is ended or archived, so it never changes again. Declare a new use.");
        }

        if (s.IsAiCaller && confirm == true && existing == null)
        {
            s.Call.Warn(Product.Engine.Core.Reasons.ReasonCodes.AiDataRowDraftOnly, "confirm is not taken from an AI client: the data use is saved as a draft, and a person confirms it on the web page (G-21).", "Data Use", null);
        }

        // Widening a confirmed use (it starts to write, it starts to reach AI) or pointing it at other data ends it and
        // starts a new one in the same call, so the decision about the earlier use stays with it (docs/architecture/data-taxonomy.md, The decision state).
        string? replaced = null;
        if (existing != null && endOn == null && existing.Text("statusMeaning") == "registered"
            && ((!Writes(existing.Text("access")) && Writes(access)) || (!existing.Flag("reachesAi") && reachesAi)
                || existing.Text("dataDomain") != domainKey || (locationId != null && !Same(existing.Text("dataLocation"), locationId))))
        {
            s.Update("data-use", existing.Id, Js.Obj(("endedOn", JsonTreeNode.FromString(s.Today))));
            replaced = existing.Id;
            locationId ??= existing.Text("dataLocation");
            existing = null;
        }

        string? status = null;
        if (existing == null)
        {
            status = s.StatusFor("data-use", !s.IsAiCaller && confirm != false ? "registered" : "draft");
        }
        else if (confirm == true && existing.Text("statusMeaning") != "registered")
        {
            status = s.StatusFor("data-use", "registered");
        }

        var columns = new List<(string, JsonTreeNode?)>
        {
            ("businessApplication", JsonTreeNode.FromString(app.Id)),
            ("dataDomain", JsonTreeNode.FromString(domainKey)),
            ("access", JsonTreeNode.FromString(access)),
            ("purpose", JsonTreeNode.FromString(s.RequireText("purpose"))),
            ("reachesAi", JsonTreeNode.FromBoolean(reachesAi)),
            ("dataLocation", Js.OptStr(locationId)),
            ("aiServiceAsSeen", Js.OptStr(s.Text("aiServiceAsSeen"))),
            ("aiComponent", Js.OptStr(s.Text("aiComponentId"))),
            ("accessApproach", Js.OptStr(s.Text("accessApproachKey"))),
            ("endedOn", Js.OptStr(endOn)),
            ("status", Js.OptStr(status)),
        };
        if (existing == null || s.Flag("isDeidentified") != null)
        {
            columns.Add(("isDeidentified", JsonTreeNode.FromBoolean(s.Flag("isDeidentified") == true)));
        }

        if (existing == null)
        {
            columns.Add(("startOn", JsonTreeNode.FromString(s.Text("startOn") ?? s.Today)));
        }
        else if (s.Text("startOn") is string start)
        {
            columns.Add(("startOn", JsonTreeNode.FromString(start)));
        }

        ComposedWrite write = existing == null ? s.Create("data-use", Js.Obj(columns.ToArray())) : s.Update("data-use", existing.Id, Js.Obj(columns.ToArray()));
        StoredRow row = s.Rows.Get("data-use", write.RowId)!;
        JsonTreeNode? evaluation = s.Call.DataUse(row.Id)?.Payload;
        string state = Js.Text(evaluation, "state") ?? "needed";
        (string? decisionId, bool submitted, List<JsonTreeNode> asked) = OpenOrSendDataDecision(
            s, tree, row, EngineKeys.DataUseSubjectKind, state, Js.Text(evaluation, "decisionId"), app,
            "Allow " + AppName(app) + " to " + AccessWords(access) + " " + tree.PathText(domainKey) + LocationWords(s, locationId) + (reachesAi ? ", with AI" : string.Empty) + ".",
            row.Text("purpose") ?? string.Empty,
            s.Flag("proposeDecision") != false);

        JsonTreeNode use = UseItem(s, tree, row);
        EffectiveDataRule governing = tree.GoverningRuleForUse(domainKey, reachesAi);
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("data-use", write),
            ("dataUseId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(row.Id)),
            ("recordNumber", Js.Str(row.Text("recordNumber"))),
            ("replacedDataUseId", Js.Str(replaced)),
            ("use", use),
            ("governingRule", GoverningRuleValue(s, tree, governing)),
            ("decisionId", Js.Str(decisionId)),
            ("decisionSubmitted", JsonTreeNode.FromBoolean(submitted)),
            ("approvalsNeeded", JsonTreeNode.FromInt64(Js.Number(use, "approvalsNeeded") ?? 0)),
            ("approvalsRequested", Arr(asked)));
        // A use with an End On still to come is open until that day (reads count it as open), so the answer says when it
        // ends instead of calling it ended (wave 3 verification).
        string? endedOn = row.Text("endedOn");
        string draft = row.Text("statusMeaning") == "draft"
            ? (s.IsAiCaller ? "The data use is saved as a draft. A person confirms it on the web page, and then its decision goes to the approvers." : "The data use is saved as a draft.")
            : endedOn == null ? "The data use is confirmed."
            : IsOpenDataRow(row, s) ? "The data use is confirmed, and it ends on " + endedOn.Substring(0, Math.Min(10, endedOn.Length)) + "."
            : "The data use is ended.";
        string decisionWords = Js.Text(use, "state") switch
        {
            "not-needed" => " No decision is needed: " + tree.DescribeRule(governing, domainKey) + ".",
            "proposed" => submitted ? " The data-use decision went to " + Count(asked.Count(a => Js.Flag(a, "isConsultation") != true), "approver", "approvers") + "." : " Its data-use decision is proposed.",
            "no-one-to-ask" => " A decision is needed, but nobody can answer it now. Propose a data owner for " + tree.PathText(domainKey) + " first.",
            "allowed" => " An accepted decision allows it.",
            "breaks-rule" => " It breaks the rule: " + tree.DescribeRule(governing, domainKey) + ".",
            _ => " A data-use decision is needed: " + tree.DescribeRule(governing, domainKey) + ".",
        };
        string? id = Js.Text(data, "dataUseId");
        return s.Answer(data, draft + decisionWords + (replaced == null ? string.Empty : " The earlier use is ended, and its decision stays with it."), id == null ? null : s.RecordUrl("data-use", id),
            decisionId != null && !submitted && !s.Call.IsDryRun && !s.IsAiCaller ? new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decisionId))) } : Array.Empty<NextStep>());
    }

    private static bool Writes(string? access)
    {
        return access == "write" || access == "read-write";
    }

    private static string AccessWords(string access)
    {
        return access == "write" ? "write" : access == "read-write" ? "read and write" : "read";
    }

    private static string LocationWords(ToolScope s, string? locationId)
    {
        StoredRow? location = locationId == null ? null : s.Rows.Get("data-location", locationId);
        return location == null ? string.Empty : " in " + (location.Text("labelAsSeen") ?? location.Id);
    }

    // Opens the data-use decision a use or a copy needs, when it has none, and sends it when a person confirmed the row
    // (D-36: the person acts in the web app; an AI client only proposes). Opening it needs the proposal right (C31), read
    // first, so a caller without it gets a warning, not a refusal. Returns the decision, whether it went to the
    // approvers, and the approval requests it started.
    private static (string? DecisionId, bool Submitted, List<JsonTreeNode> Asked) OpenOrSendDataDecision(
        ToolScope s, DataTree tree, StoredRow row, string subjectKind, string state, string? decisionId, StoredRow? app, string statement, string context, bool propose)
    {
        var asked = new List<JsonTreeNode>();
        bool open = IsOpenDataRow(row, s);
        if (!open)
        {
            return (decisionId, false, asked);
        }

        string domainKey = row.Text("dataDomain")!;
        if (state == "no-one-to-ask")
        {
            s.Call.Warn(Product.Engine.Core.Reasons.ReasonCodes.DataNoOneToAsk, "DC-06: a decision is needed about " + tree.PathText(domainKey) + ", but nobody can answer it now. Propose a data owner first, then the decision.", "Data Domain", domainKey);
            return (decisionId, false, asked);
        }

        if (decisionId == null && propose && (state == "needed" || state == "re-decide"))
        {
            if (!MayProposeDataDecision(s, tree, domainKey, subjectKind, app, row.Flag("reachesAi")))
            {
                s.Call.Warn("decision.propose-not-allowed", "The " + (subjectKind == EngineKeys.DataMappingSubjectKind ? "copy" : "data-use") + " decision is not opened: the decision rights let " + (subjectKind == EngineKeys.DataMappingSubjectKind ? "the platform admin" : "the application's business owner, its technical owner or the platform admin") + " propose it, and you hold none of these.", "Decision Right", EngineKeys.DataUseDecisionType);
            }
            else
            {
                string? outcome = s.ConfigRow("decision-outcome", EngineKeys.DataUseDecisionType + "/" + EngineKeys.DataUseAllowOutcome) is SnapshotRow allow && allow.IsActive ? EngineKeys.DataUseAllowOutcome : null;
                var columns = new List<(string, JsonTreeNode?)>
                {
                    ("decisionType", JsonTreeNode.FromString(EngineKeys.DataUseDecisionType)),
                    ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                    (subjectKind == EngineKeys.DataMappingSubjectKind ? "subjectDataMapping" : "subjectDataUse", JsonTreeNode.FromString(row.Id)),
                    ("subjectApplication", Js.OptStr(app?.Id)),
                    ("statement", JsonTreeNode.FromString(statement)),
                    ("context", JsonTreeNode.FromString(context.Length == 0 ? statement : context)),
                    ("reason", JsonTreeNode.FromString(context.Length == 0 ? statement : context)),
                    ("outcome", Js.OptStr(outcome)),
                    ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed"))),
                };
                decisionId = s.Create("decision", Js.Obj(columns.ToArray())).RowId;
                s.Call.RequireProposalRight(decisionId);
            }
        }

        // A person's confirmed row sends its proposed decision; an AI client never does (AI May Submit is no for copies,
        // and a data use waits for the person who confirms it).
        if (decisionId == null || s.IsAiCaller || row.Text("statusMeaning") != "registered")
        {
            return (decisionId, false, asked);
        }

        StoredRow? decision = s.Rows.Get("decision", decisionId);
        if (decision == null || decision.Text("statusMeaning") != "proposed" || s.Query("approval-record", ("decision", decisionId)).Count > 0)
        {
            return (decisionId, false, asked);
        }

        JsonTreeNode requested = s.Call.RequestApprovals(decisionId);
        asked.AddRange(Js.Strings(requested, "approvalRecordIds").Select(r => s.Rows.Get("approval-record", r)).Where(r => r != null).Select(r => ApprovalAsked(s, r!)));
        return (decisionId, true, asked);
    }

    // The proposal right check (C31), read only, for a data decision about a use (the application's scope, the domain's
    // classification and the AI flag) or a copy (no application).
    private static bool MayProposeDataDecision(ToolScope s, DataTree tree, string domainKey, string subjectKind, StoredRow? app, bool reachesAi)
    {
        IEnumerable<string> ownerRoles = app == null
            ? Enumerable.Empty<string>()
            : s.CurrentOwners(app.Id).Where(o => Same(o.Text("principal"), s.PersonId)).Select(o => o.Text("ownerRole")).Where(r => r != null).Select(r => r!);
        JsonTreeNode input = Js.Obj(
            ("decisionTypeKey", JsonTreeNode.FromString(EngineKeys.DataUseDecisionType)),
            ("tierKey", Js.OptStr(app?.Text("tier"))),
            ("lifecycleStageKey", Js.OptStr(app?.Text("lifecycleStage"))),
            ("applicationKindKey", Js.OptStr(app?.Text("applicationKind"))),
            ("portfolioPath", Js.Strs(PortfolioPath(s, app?.Text("portfolio")))),
            ("subjectKind", JsonTreeNode.FromString(subjectKind)),
            ("classificationKey", Js.OptStr(tree.Classification(domainKey)?.Key)),
            ("aiUse", subjectKind == EngineKeys.DataUseSubjectKind ? JsonTreeNode.FromBoolean(reachesAi) : null),
            ("roleKeys", Js.Strs(s.Session.RoleKeys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))),
            ("ownerRoleKeys", Js.Strs(ownerRoles.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))));
        return s.Call.ProposalRight(input).Outcome == ProposalRightsOperation.Allowed;
    }

    private static JsonTreeNode ApprovalAsked(ToolScope s, StoredRow record)
    {
        string? ownerRole = record.Text("requiredOwnerRole");
        return Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(record.Id)),
            ("requiredRoleKey", ownerRole == null ? Js.Str(record.Text("requiredRole")) : JsonTreeNode.Null),
            ("requiredOwnerRoleKey", Js.Str(ownerRole)),
            ("askedPrincipal", s.PrincipalRef(record.Text("askedPrincipal"))),
            ("isConsultation", JsonTreeNode.FromBoolean(record.Flag("isConsultation"))));
    }

    // The status a save asks for: a new row confirmed for a person unless confirm is false, a draft for an AI client (the
    // engine forces it anyway, G-08); an existing row confirmed only when confirm is true (an AI client is refused, G-21).
    private static string? DataRowStatus(ToolScope s, string entity, StoredRow? existing, bool? confirm, bool retire)
    {
        if (retire)
        {
            return s.StatusFor(entity, "archived");
        }

        if (existing == null)
        {
            return s.StatusFor(entity, !s.IsAiCaller && confirm != false ? "registered" : "draft");
        }

        return confirm == true && existing.Text("statusMeaning") != "registered" ? s.StatusFor(entity, "registered") : null;
    }

    // ---- 83. save_data_location ----

    private static JsonTreeNode SaveDataLocation(ToolScope s)
    {
        s.RequireAiState("draft");
        var results = new List<(ComposedWrite Write, bool Matched)>();
        int confirmedCount = 0;
        int index = 0;
        foreach (JsonTreeNode item in s.Items("items"))
        {
            string field = "items[" + index++ + "]";
            StoredRow? existing = Js.Text(item, "dataLocationId") is string id ? s.RequireRow("data-location", id, field + ".dataLocationId") : null;
            string? platform = Js.Text(item, "platformKey") ?? existing?.Text("dataPlatform");
            SnapshotRow platformRow = s.RequireKey("data-platform", platform, field + ".platformKey");
            string? externalId = Js.Text(item, "externalId")?.Trim();
            string? url = Js.Text(item, "url")?.Trim();
            bool matched = false;
            if (existing == null)
            {
                if (externalId == null && url == null)
                {
                    throw s.Invalid(field + ".externalId", "A data location is held as a link: give its externalId, its url, or both.");
                }

                existing = s.Query("data-location", ("dataPlatform", platformRow.KeyPath))
                    .Where(l => l.Text("retiredOn") == null && l.Text("statusMeaning") != "archived")
                    .FirstOrDefault(l => externalId != null ? string.Equals(l.Text("externalId"), externalId, StringComparison.OrdinalIgnoreCase) : l.Text("externalId") == null && string.Equals(l.Text("url"), url, StringComparison.OrdinalIgnoreCase));
                matched = existing != null;
            }

            if (existing != null && !s.HoldsRole("platform-admin") && !s.HoldsRole("architect") && !s.HoldsRole("data-governance-lead")
                && !IsTechnicalOwnerOf(s, existing) && !Same(existing.Text("createdByPrincipal"), s.PersonId))
            {
                throw s.Refused(McpErrorCodes.NotAuthorized, (existing.Text("labelAsSeen") ?? existing.Id) + " is registered. Changing it needs the role platform-admin, architect or data-governance-lead, or the technical owner role on the location.");
            }

            string? parent = Js.Text(item, "parentLocationId");
            if (parent != null && (existing != null && Same(parent, existing.Id)))
            {
                throw s.Invalid(field + ".parentLocationId", "A location is not its own parent.");
            }

            if (parent != null)
            {
                s.RequireRow("data-location", parent, field + ".parentLocationId");
            }

            string? stage = Js.Text(item, "targetStageKey");
            if (stage != null)
            {
                s.RequireKey("target-stage", stage, field + ".targetStageKey");
            }

            string? linkKind = Js.Text(item, "linkKindKey");
            if (linkKind != null)
            {
                s.RequireKey("link-kind", linkKind, field + ".linkKindKey");
            }

            if (Js.Text(item, "deploymentTargetId") is string target)
            {
                s.RequireRow("deployment-target", target, field + ".deploymentTargetId");
            }

            if (Js.Text(item, "hostedByApplicationId") is string hosted)
            {
                s.RequireRow("business-application", hosted, field + ".hostedByApplicationId");
            }

            bool retire = Js.Flag(item, "retire") == true;
            bool? seen = Js.Flag(item, "seenNow");
            string? locationStatus = DataRowStatus(s, "data-location", existing, Js.Flag(item, "confirm"), retire);
            // A person's save that takes a draft out of draft: the summary says so (wave 4b QA).
            if (existing != null && existing.Text("statusMeaning") == "draft" && locationStatus != null && locationStatus == s.StatusFor("data-location", "registered"))
            {
                confirmedCount++;
            }
            // Never the External Id or the Url as the label: those are restricted reads (document 26, section 6.2), and the
            // label goes to every role (security review, minor).
            string? label = Js.Text(item, "labelAsSeen") ?? existing?.Text("labelAsSeen") ?? "Unnamed " + (Js.Text(platformRow.Columns, "label") ?? platformRow.KeyPath) + " location";
            JsonTreeNode columns = Js.Obj(
                ("dataPlatform", JsonTreeNode.FromString(platformRow.KeyPath)),
                ("parentLocation", Js.OptStr(parent)),
                ("targetStage", Js.OptStr(stage)),
                ("deploymentTarget", Js.OptStr(Js.Text(item, "deploymentTargetId"))),
                ("hostedByApplication", Js.OptStr(Js.Text(item, "hostedByApplicationId"))),
                ("linkKind", linkKind != null ? JsonTreeNode.FromString(linkKind) : existing == null ? Js.OptStr(Js.Text(platformRow.Columns, "defaultLinkKindKey")) : null),
                ("externalId", Js.OptStr(externalId)),
                ("url", Js.OptStr(url)),
                ("labelAsSeen", Js.OptStr(label)),
                ("lastSeenOn", seen == true || (existing == null && seen != false) ? Js.Utc(s.Now) : null),
                // The person's own day (wave 4b review): a location retired this evening in New York is retired today.
                ("retiredOn", retire ? JsonTreeNode.FromString(existing?.Text("retiredOn") ?? s.PersonToday) : null),
                ("status", Js.OptStr(locationStatus)));
            ComposedWrite write = existing == null ? s.Create("data-location", columns) : s.Update("data-location", existing.Id, columns);
            results.Add((write, matched));
        }

        var items = results.Select(r =>
        {
            StoredRow row = s.Rows.Get("data-location", r.Write.RowId)!;
            bool wouldCreate = r.Write.Created && s.Call.IsDryRun;
            return Js.Obj(
                ("dataLocationId", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(row.Id)),
                ("created", JsonTreeNode.FromBoolean(r.Write.Created && !s.Call.IsDryRun)),
                ("matchedExisting", JsonTreeNode.FromBoolean(r.Matched)),
                ("status", s.StatusValue(row) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
                ("label", JsonTreeNode.FromString(row.Text("labelAsSeen") ?? row.Id)),
                ("platform", s.LabelledKey("data-platform", row.Text("dataPlatform") ?? "other")),
                ("recordUrl", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl("data-location", row.Id))));
        }).ToList();
        int created = results.Count(r => r.Write.Created);
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("data-location", results[0].Write),
            ("items", Arr(items)),
            ("createdCount", JsonTreeNode.FromInt64(created)),
            ("updatedCount", JsonTreeNode.FromInt64(results.Count - created)));
        bool drafts = items.Any(i => PathText(i, "status.meaning") == "draft");
        string summary = (created == 0 ? string.Empty : Count(created, "location is registered", "locations are registered") + (results.Count - created == 0 ? "." : ", ")) + (results.Count - created == 0 ? string.Empty : Count(results.Count - created, "location is changed", "locations are changed") + ".")
            + (confirmedCount > 0 ? " " + Count(confirmedCount, "draft is confirmed", "drafts are confirmed") + ": no longer a draft." : string.Empty)
            + (drafts ? (s.IsAiCaller ? " AI drafts wait for a person to confirm them." : " Drafts wait for confirmation.") : string.Empty);
        string? first = Js.Text(items[0], "dataLocationId");
        return s.Answer(data, summary, first == null ? null : s.RecordUrl("data-location", first), first == null ? Array.Empty<NextStep>() : new[] { ToolScope.Next("save_data_mapping") });
    }

    // ---- 84. save_data_mapping ----

    private static JsonTreeNode SaveDataMapping(ToolScope s)
    {
        s.RequireAiState("draft");
        var tree = new DataTree(s.Snapshot);
        bool proposeCopy = s.Flag("proposeCopyDecision") != false;
        var results = new List<JsonTreeNode>();
        ComposedWrite? firstWrite = null;
        int confirmedMappings = 0;
        int index = 0;
        foreach (JsonTreeNode item in s.Items("items"))
        {
            string field = "items[" + index++ + "]";
            string domainKey = Js.Text(item, "domainKey")!;
            if (tree.Domain(domainKey) == null)
            {
                throw s.Invalid(field + ".domainKey", "domainKey \"" + domainKey + "\" is not a data domain in the configuration in force. Find one with find_data_domains.");
            }

            StoredRow location = s.RequireRow("data-location", Js.Text(item, "locationId"), field + ".locationId");
            string origin = Js.Text(item, "origin")!;
            StoredRow? existing = Js.Text(item, "mappingId") is string id ? s.RequireRow("data-mapping", id, field + ".mappingId") : null;
            bool matched = false;
            if (existing == null)
            {
                existing = s.Query("data-mapping", ("dataDomain", domainKey), ("dataLocation", location.Id)).Where(OpenOn(s)).FirstOrDefault(m => m.Text("dataOrigin") == origin);
                matched = existing != null;
            }
            else if (!IsOpenDataRow(existing, s))
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "Data mapping " + existing.Id + " is ended or archived, so it never changes again. Map the data again.");
            }

            if (!s.HoldsRole("platform-admin") && !s.HoldsRole("data-governance-lead") && !IsDomainOwnerOrSteward(s, domainKey) && !IsTechnicalOwnerOf(s, location)
                && !(existing != null && Same(existing.Text("createdByPrincipal"), s.PersonId) && existing.Text("dataDomain") == domainKey && Same(existing.Text("dataLocation"), location.Id)))
            {
                throw s.Refused(McpErrorCodes.NotAuthorized, "Mapping " + tree.PathText(domainKey) + " in " + (location.Text("labelAsSeen") ?? location.Id) + " needs the role platform-admin or data-governance-lead, the data owner or steward role on the domain, or the technical owner role on the location.");
            }

            if (Js.Text(item, "copiedFromMappingId") is string from)
            {
                s.RequireRow("data-mapping", from, field + ".copiedFromMappingId");
            }

            bool? confirm = Js.Flag(item, "confirm");
            string? status = DataRowStatus(s, "data-mapping", existing, confirm, false);
            bool confirming = !s.IsAiCaller && status != null && status == s.StatusFor("data-mapping", "registered");
            if (confirming && existing != null && existing.Text("statusMeaning") == "draft")
            {
                confirmedMappings++;
            }
            JsonTreeNode? containers = Js.Member(item, "containers");
            JsonTreeNode columns = Js.Obj(
                ("dataDomain", JsonTreeNode.FromString(domainKey)),
                ("dataLocation", JsonTreeNode.FromString(location.Id)),
                ("dataOrigin", JsonTreeNode.FromString(origin)),
                ("containers", containers),
                ("copiedFrom", Js.OptStr(Js.Text(item, "copiedFromMappingId"))),
                ("startOn", existing == null ? JsonTreeNode.FromString(s.Today) : null),
                ("endedOn", Js.OptStr(Js.Text(item, "endOn"))),
                ("confirmedOn", confirming ? JsonTreeNode.FromString(s.Today) : null),
                ("status", Js.OptStr(status)));
            ComposedWrite write = existing == null ? s.Create("data-mapping", columns) : s.Update("data-mapping", existing.Id, columns);
            firstWrite ??= write;
            StoredRow row = s.Rows.Get("data-mapping", write.RowId)!;
            JsonTreeNode? evaluation = s.Call.DataUse(row.Id)?.Payload;
            bool copy = Js.Member(evaluation, "governingRule") is JsonTreeNode rule && rule.Kind == JsonTreeKind.Object;
            string state = Js.Text(evaluation, "state") ?? "not-needed";
            (string? decisionId, bool submitted, _) = copy
                ? OpenOrSendDataDecision(s, tree, row, EngineKeys.DataMappingSubjectKind, state, Js.Text(evaluation, "decisionId"), null,
                    "Allow a " + origin.Replace('-', ' ') + " of " + tree.PathText(domainKey) + " in " + (location.Text("labelAsSeen") ?? location.Id) + ", outside production.",
                    "The " + origin.Replace('-', ' ') + " sits at a location whose stage is not production, and the copy rule asks for a decision.",
                    proposeCopy)
                : ((string?)null, false, new List<JsonTreeNode>());
            JsonTreeNode after = copy ? s.Call.DataUse(row.Id)?.Payload ?? JsonTreeNode.Null : JsonTreeNode.Null;
            bool wouldCreate = write.Created && s.Call.IsDryRun;
            results.Add(Js.Obj(
                ("dataMappingId", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(row.Id)),
                ("created", JsonTreeNode.FromBoolean(write.Created && !s.Call.IsDryRun)),
                ("matchedExisting", JsonTreeNode.FromBoolean(matched)),
                ("status", s.StatusValue(row) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
                ("domain", DomainRef(tree, domainKey)),
                ("dataLocationId", JsonTreeNode.FromString(location.Id)),
                ("origin", JsonTreeNode.FromString(origin)),
                ("copyState", copy ? Js.Str(Js.Text(after, "state") ?? state) : JsonTreeNode.Null),
                ("decisionId", Js.Str(decisionId)),
                ("decisionSubmitted", JsonTreeNode.FromBoolean(submitted)),
                ("approvalsNeeded", JsonTreeNode.FromInt64(Js.Number(after, "approvalsNeeded") ?? 0)),
                ("recordUrl", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl("data-mapping", row.Id)))));
        }

        JsonTreeNode data = ToolScope.Merge(s.WriteResult("data-mapping", firstWrite!), ("items", Arr(results)));
        int copies = results.Count(r => Js.Text(r, "copyState") is string c && c != "not-needed" && c != "allowed");
        string summary = Count(results.Count, "mapping is saved", "mappings are saved") + "."
            + (confirmedMappings > 0 ? " " + Count(confirmedMappings, "draft is confirmed", "drafts are confirmed") + ": no longer a draft." : string.Empty)
            + (results.Any(r => PathText(r, "status.meaning") == "draft") ? (s.IsAiCaller ? " AI drafts wait for a person to confirm them." : " Drafts wait for confirmation.") : string.Empty)
            + (copies > 0 ? " " + Count(copies, "copy outside production needs a decision or breaks", "copies outside production need a decision or break") + " the copy rule." : string.Empty);
        string? first = Js.Text(results[0], "dataMappingId");
        return s.Answer(data, summary, first == null ? null : s.RecordUrl("data-mapping", first));
    }

    // ---- 85. move_data_references ----

    // Moves the open mappings and uses of one domain to another (docs/architecture/data-taxonomy.md, Changing the taxonomy). When both domains answer to the same data
    // owner, each row keeps its identity, so the decisions about it stay with it and its state is computed again ("2 allowed
    // uses keep their decision"; a stricter new domain makes an allowed use re-decide). When the owner
    // differs, the new owner is asked (choices under D-41): a decided row is declared again in the
    // new domain, a waiting decision is asked again of the new owner, and the engine opens and sends the decision each
    // row then needs. OP-08 judges each change: G-18 needs an active, declarable target and refuses a duplicate use, and
    // G-25 refuses a move into a domain whose rule is never. An AI client previews only (validateOnly), and its preview is
    // the person's own preview (MovePreviewAsPerson).
    private static JsonTreeNode MoveDataReferences(ToolScope s)
    {
        if (s.IsAiCaller && !s.ValidateOnly)
        {
            throw s.Refused(McpErrorCodes.AiCannotDoThis, "An AI client previews a move with validateOnly; a person moves the rows in the product's page.");
        }

        if (s.IsAiCaller)
        {
            return MovePreviewForAiClient(s);
        }

        var tree = new DataTree(s.Snapshot);
        string from = s.RequireText("fromDomainKey");
        string to = s.RequireText("toDomainKey");
        if (tree.Domain(from) == null)
        {
            throw s.NotFound("Data Domain", from, "fromDomainKey");
        }

        SnapshotRow target = tree.Domain(to) ?? throw s.Invalid("toDomainKey", "toDomainKey \"" + to + "\" is not a data domain in the configuration in force.");
        if (from == to)
        {
            throw s.Invalid("toDomainKey", "The rows already point at " + tree.PathText(to) + ".");
        }

        if (!target.IsActive || !tree.IsDeclarable(to))
        {
            throw s.Invalid("toDomainKey", tree.PathText(to) + (target.IsActive ? " sits at a level that is not declarable" : " is retired") + ". Move the rows to an active domain at a declarable level.");
        }

        IReadOnlyList<string> include = s.Strings("include");
        string reason = s.RequireText("reason");
        var mappings = include.Contains("mappings") ? s.Query("data-mapping", ("dataDomain", from)).Where(OpenOn(s)).OrderBy(m => m.Id, StringComparer.Ordinal).ToList() : new List<StoredRow>();
        var uses = include.Contains("uses") ? s.Query("data-use", ("dataDomain", from)).Where(OpenOn(s)).OrderBy(u => u.Id, StringComparer.Ordinal).ToList() : new List<StoredRow>();
        if (mappings.Count + uses.Count == 0)
        {
            throw s.Invalid("fromDomainKey", "No open " + string.Join(" or ", include.Select(i => i == "uses" ? "data use" : "mapping")) + " points at " + tree.PathText(from) + ", so there is nothing to move.");
        }

        RefuseDuplicateMove(s, tree, uses, to);
        RefuseMappingCollision(s, tree, mappings, to);
        WarnLooserTarget(s, tree, from, to);

        // When the effective data owner differs between the two domains, a decision the old owner gave does not carry
        // over (security review, major; wave 3 verification, major twice: the move left decisions with nobody asked). A
        // row with an accepted decision is ended and declared again in the new domain. A proposed decision nobody answered
        // yet is asked again from the start, now of the new owner. A proposed decision someone already answered is
        // withdrawn by the engine. Each row left with no open decision gets one the engine opens and sends, for the person
        // who moved it. When the new domain has no data owner, the proposed decision is withdrawn and the use waits for a
        // data owner (DC-06).
        string? newOwner = DataOwnerOf(s, to);
        bool ownerChanges = !Same(DataOwnerOf(s, from), newOwner);
        ComposedWrite? first = null;
        string firstEntity = "data-mapping";
        var movedMappings = new List<string>();
        var asks = new MoveAsks();
        foreach (StoredRow mapping in mappings)
        {
            ComposedWrite write;
            if (ownerChanges && HasAcceptedDataDecision(s, "subjectDataMapping", mapping.Id))
            {
                s.Update("data-mapping", mapping.Id, Js.Obj(("endedOn", JsonTreeNode.FromString(s.Today))));
                write = s.Create("data-mapping", Js.Obj(CopyColumns(mapping, to, s.Today, "dataLocation", "dataOrigin", "containers", "status")));
                asks.Redeclared++;
            }
            else
            {
                write = s.Update("data-mapping", mapping.Id, Js.Obj(("dataDomain", JsonTreeNode.FromString(to))));
            }

            if (ownerChanges)
            {
                AskNewOwner(s, tree, "subjectDataMapping", s.Rows.Get("data-mapping", write.RowId)!, newOwner, to, reason, asks);
            }

            // A preview lists the rows that would move by their own IDs: a row declared again has no ID until it is written.
            movedMappings.Add(s.Call.IsDryRun ? mapping.Id : write.RowId);
            first ??= write;
        }

        var changed = new List<JsonTreeNode>();
        var movedUses = new List<string>();
        foreach (StoredRow use in uses)
        {
            string before = s.Call.DataUse(use.Id)?.Payload is JsonTreeNode b ? Js.Text(b, "state") ?? "needed" : "needed";
            ComposedWrite write;
            if (ownerChanges && HasAcceptedDataDecision(s, "subjectDataUse", use.Id))
            {
                s.Update("data-use", use.Id, Js.Obj(("endedOn", JsonTreeNode.FromString(s.Today))));
                write = s.Create("data-use", Js.Obj(CopyColumns(use, to, s.Today, "businessApplication", "access", "purpose", "reachesAi", "isDeidentified", "dataLocation", "aiServiceAsSeen", "aiComponent", "accessApproach", "status")));
                asks.Redeclared++;
            }
            else
            {
                write = s.Update("data-use", use.Id, Js.Obj(("dataDomain", JsonTreeNode.FromString(to))));
            }

            if (ownerChanges)
            {
                AskNewOwner(s, tree, "subjectDataUse", s.Rows.Get("data-use", write.RowId)!, newOwner, to, reason, asks);
            }

            string reported = s.Call.IsDryRun ? use.Id : write.RowId;
            movedUses.Add(reported);
            if (first == null)
            {
                first = write;
                firstEntity = "data-use";
            }

            string after = s.Call.DataUse(write.RowId)?.Payload is JsonTreeNode a ? Js.Text(a, "state") ?? "needed" : "needed";
            if (before != after)
            {
                changed.Add(Js.Obj(("dataUseId", JsonTreeNode.FromString(reported)), ("stateBefore", JsonTreeNode.FromString(before)), ("stateAfter", JsonTreeNode.FromString(after))));
            }
        }

        bool dryRun = s.Call.IsDryRun;
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(firstEntity, first!),
            ("fromDomain", DomainRef(tree, from)),
            ("toDomain", DomainRef(tree, to)),
            ("movedMappingIds", Ids(movedMappings)),
            ("movedUseIds", Ids(movedUses)),
            ("changedUses", Arr(changed)),
            ("ownerChange", ownerChanges ? asks.Json(s, newOwner) : JsonTreeNode.Null));
        string summary = (dryRun ? "Preview: " : string.Empty) + Count(mappings.Count, "mapping", "mappings") + " and " + Count(uses.Count, "data use", "data uses")
            + (dryRun ? " would move" : " moved") + " from " + tree.PathText(from) + " to " + tree.PathText(to) + ". "
            + (changed.Count == 0
                ? (dryRun ? "No use would change its decision state." : "No use changes its decision state.")
                : Count(changed.Count, dryRun ? "use would change its" : "use changes its", dryRun ? "uses would change their" : "uses change their") + " decision state, such as " + StateWords(Js.Text(changed[0], "stateBefore")) + " to " + StateWords(Js.Text(changed[0], "stateAfter")) + ".")
            + (ownerChanges
                ? asks.Words(dryRun, tree.PathText(to), newOwner == null ? null : s.DisplayLabel(newOwner))
                : " Both domains answer to the same data owner, so each row " + (dryRun ? "would keep" : "keeps") + " its identity and its decisions.")
            + " Reason: " + AsSentence(reason);
        return s.Answer(data, summary, s.RecordUrl("data-domain", to), ToolScope.Next("get_data_gaps", ("domainKey", JsonTreeNode.FromString(to))));
    }

    // A move into a looser domain is allowed, but said (wave 4b review; D-41, docs/architecture/data-taxonomy.md): when the target's effective
    // classification ranks lower, or its use, AI use or copy to test rule is less strict than the source's, the preview and
    // the answer carry the warning data-move.looser-target, such as "Workplace is less strict than Workplace / Visitor log
    // (Internal against Confidential; AI use allowed against never)."
    private static void WarnLooserTarget(ToolScope s, DataTree tree, string from, string to)
    {
        var parts = new List<string>();
        EffectiveClassification? source = tree.Classification(from);
        EffectiveClassification? target = tree.Classification(to);
        if (source != null && (target == null || target.Rank < source.Rank))
        {
            parts.Add(ClassificationWords(tree, target) + " against " + ClassificationWords(tree, source));
        }

        foreach ((DataRuleName name, string words) in new[] { (DataRuleName.Use, "use"), (DataRuleName.Ai, "AI use"), (DataRuleName.Copy, "copy to test") })
        {
            DataHandlingRule was = tree.Rule(from, name).Value;
            DataHandlingRule now = tree.Rule(to, name).Value;
            if (now < was)
            {
                parts.Add(words + " " + RuleValueWords(now) + " against " + RuleValueWords(was));
            }
        }

        if (parts.Count > 0)
        {
            s.Call.Warn("data-move.looser-target", tree.PathText(to) + " is less strict than " + tree.PathText(from) + " (" + string.Join("; ", parts) + ").", "Data Domain", to);
        }
    }

    private static string ClassificationWords(DataTree tree, EffectiveClassification? classification)
    {
        if (classification == null)
        {
            return "no classification";
        }

        SnapshotRow? row = tree.ClassificationRow(classification.Key);
        return (row == null ? null : Js.Text(row.Columns, "label")) ?? classification.Key.Replace('-', ' ');
    }

    private static string RuleValueWords(DataHandlingRule value)
    {
        return value == DataHandlingRule.Decision ? "needs a decision" : value == DataHandlingRule.MaskedOnly ? "masked only" : value == DataHandlingRule.Never ? "never" : "allowed";
    }

    // A person's text as the end of a sentence: a final period when it has no closing mark of its own.
    private static string AsSentence(string text)
    {
        string trimmed = text.TrimEnd();
        return trimmed.Length == 0 || trimmed.EndsWith(".", StringComparison.Ordinal) || trimmed.EndsWith("!", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal) ? trimmed : trimmed + ".";
    }

    // G-18, the second key of O27, in the words of a move (wave 3 verification: the guard's own refusal said "Change that
    // use instead of declaring a second one" and named a record as the field at fault). A moved use may not land on an
    // open use of the same application with the same location and AI flag in the new domain. The guard refuses the write
    // too; this check refuses first, before anything is written, so a person's preview and an AI client's say the same.
    private static void RefuseDuplicateMove(ToolScope s, DataTree tree, IReadOnlyList<StoredRow> uses, string to)
    {
        var landing = s.Query("data-use", ("dataDomain", to)).Where(u => u.Text("statusMeaning") != "archived" && IsOpenDataRow(u, s)).ToList();
        foreach (StoredRow use in uses)
        {
            StoredRow? twin = landing.FirstOrDefault(o =>
                Same(o.Text("businessApplication"), use.Text("businessApplication"))
                && o.Flag("reachesAi") == use.Flag("reachesAi")
                && string.Equals(o.Text("dataLocation") ?? string.Empty, use.Text("dataLocation") ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (twin == null)
            {
                continue;
            }

            StoredRow? app = use.Text("businessApplication") is string appId ? s.Rows.Get("business-application", appId) : null;
            string name = app == null ? "the application" : AppName(app);
            throw s.Call.Fail(
                OperationOutcomes.Refused,
                Product.Engine.Core.Reasons.ReasonCodes.DataUseDuplicate,
                "Data use " + (use.Text("recordNumber") ?? use.Id) + " cannot move to " + tree.PathText(to) + ": data use " + (twin.Text("recordNumber") ?? twin.Id) + " already declares " + name + "'s "
                    + (use.Flag("reachesAi") ? "AI " : string.Empty) + "use of that domain at the same location. End one of the two uses first, or move the rows to another domain.",
                "Data Domain",
                "toDomainKey");
        }
    }

    // A mapping whose new domain already has an open mapping at the same location with the same origin would collide with
    // it: one domain is mapped once per location and origin (the store's key of O32). The move is refused in the preview
    // and in the write alike, before anything is written (wave 4b: the write met the store's key and answered an internal
    // error while the preview said nothing). Choice (D-41): refuse, not merge.
    // Merging would end one row and lose its identity and the decisions about it, which a same-owner move promises to keep.
    private static void RefuseMappingCollision(ToolScope s, DataTree tree, IReadOnlyList<StoredRow> mappings, string to)
    {
        if (mappings.Count == 0)
        {
            return;
        }

        // The store's key (O26) holds every mapping with no Ended On, whatever its status, so the check reads the same rows:
        // a withdrawn draft from before wave 4b's fix kept its place with no end date (review).
        var landing = s.Query("data-mapping", ("dataDomain", to)).Where(m => string.IsNullOrEmpty(m.Text("endedOn"))).ToList();
        foreach (StoredRow mapping in mappings)
        {
            StoredRow? twin = landing.FirstOrDefault(o =>
                Same(o.Text("dataLocation"), mapping.Text("dataLocation"))
                && string.Equals(o.Text("dataOrigin") ?? string.Empty, mapping.Text("dataOrigin") ?? string.Empty, StringComparison.Ordinal));
            if (twin == null)
            {
                continue;
            }

            StoredRow? location = mapping.Text("dataLocation") is string locationId ? s.Rows.Get("data-location", locationId) : null;
            string where = location?.Text("labelAsSeen") ?? mapping.Text("dataLocation") ?? "its location";
            string origin = (mapping.Text("dataOrigin") ?? "system-of-record").Replace('-', ' ');
            string head = "The mapping of " + tree.PathText(mapping.Text("dataDomain") ?? string.Empty) + " at " + where + " cannot move to " + tree.PathText(to) + ": ";
            throw s.Invalid(
                "toDomainKey",
                twin.Text("statusMeaning") == "archived"
                    ? head + "a withdrawn draft mapping of " + tree.PathText(to) + " still holds that place, as the " + origin + ". Move only the uses."
                    : head + tree.PathText(to) + " is mapped there already, as the " + origin + ". End one of the two mappings first, or move only the uses.");
        }
    }

    // An AI client's preview of a move (wave 3 verification): the person's own preview ran first, as a dry run
    // (MovePreviewAsPerson), so the duplicate check and every guard judged the same rows the person's preview judges. Its
    // answer is the AI client's answer, with the AI client named in performedBy. A refusal never reaches this point: the
    // first step's refusal is the tool's answer.
    private static JsonTreeNode MovePreviewForAiClient(ToolScope s)
    {
        RuntimeOutcome preview = s.Prepared ?? throw new InvalidOperationException("move_data_references reads the person's preview its first step ran.");
        s.Call.Report(preview.Reasons);
        JsonTreeNode payload = preview.Payload ?? Js.EmptyObject;
        JsonTreeNode data = Js.With(Js.Member(payload, "data") ?? Js.EmptyObject, ("performedBy", s.CallerPerformedBy()));
        NextStep[] next = Js.Items(payload, "nextSteps").Where(n => Js.Text(n, "tool") != null).Select(n => new NextStep(Js.Text(n, "tool")!, Js.Member(n, "inputs") ?? Js.EmptyObject)).ToArray();
        string summary = (Js.Text(payload, "summary") ?? "Preview of the move.") + " A person moves the rows in the product's page. This preview ran every rule the person's move runs.";
        return s.Answer(data, summary, Js.Text(payload, "recordUrl"), next);
    }

    // The first step of move_data_references for an AI client's preview: the same tool body, run as the person the AI
    // client acts for, in a dry run that rolls back and leaves no audit entry (wave 3 verification: the AI preview ran no
    // write, so it promised a move that the engine refused for the person, a duplicate use). G-21 keeps the AI client off
    // rows a person confirmed even in a dry run, so its own call writes nothing; the person's dry run writes every row as
    // the person's move would, and then nothing commits. Null for any other call (docs/architecture/data-taxonomy.md).
    internal static RuntimeOutcome? MovePreviewAsPerson(InProcessBackend backend, BackendSession session, ToolInvocation invocation)
    {
        if (session.ActorKind != "ai-client-for-person" || Js.Flag(invocation.Arguments, "validateOnly") != true)
        {
            return null;
        }

        BackendCaller ai = session.Caller;
        var person = new BackendCaller(CallerEntry.Person, ai.ObjectId, ai.PrincipalKind, ai.ClientAppId, ai.GroupObjectIds, ai.DirectoryRoleIds) { ClientAppLabel = ai.ClientAppLabel };
        var personSession = new BackendSession(person, "person", session.Roles, session.ConfigurationSetKey, session.ConfigurationSetVersion, session.ContentHash, session.PublishedOnUtc,
            session.ToolPolicy, session.KnownClients, session.PageSizeMax, session.AsOfUtc, session.State);
        var request = new ComposeRequest(invocation.Tool.Name, Js.Without(invocation.Arguments, "clientRequestId", "validateOnly"))
        {
            DryRun = true,
            CorrelationId = invocation.CorrelationId,
        };
        return backend.Runtime.Compose(InProcessBackend.Identity(person), request, call => MoveDataReferences(new ToolScope(backend, call, personSession, invocation)));
    }

    // What a move with a change of data owner did to the decisions, for its summary and its ownerChange answer.
    private sealed class MoveAsks
    {
        public int Redeclared { get; set; }

        public int Reasked { get; set; }

        public int Opened { get; set; }

        public int WithdrawnAnswered { get; set; }

        public int WithdrawnNoOwner { get; set; }

        public int Waiting { get; set; }

        public JsonTreeNode Json(ToolScope s, string? newOwner)
        {
            return Js.Obj(
                ("newDataOwner", s.PrincipalRef(newOwner)),
                ("redeclaredCount", JsonTreeNode.FromInt64(Redeclared)),
                ("reaskedCount", JsonTreeNode.FromInt64(Reasked)),
                ("openedCount", JsonTreeNode.FromInt64(Opened)),
                ("withdrawnAnsweredCount", JsonTreeNode.FromInt64(WithdrawnAnswered)),
                ("withdrawnNoOwnerCount", JsonTreeNode.FromInt64(WithdrawnNoOwner)),
                ("waitingForOwnerCount", JsonTreeNode.FromInt64(Waiting)));
        }

        public string Words(bool dryRun, string domainText, string? ownerName)
        {
            string owner = ownerName ?? "the new data owner";
            var parts = new List<string>();
            if (Redeclared > 0)
            {
                parts.Add(Count(Redeclared, "decided row", "decided rows") + (dryRun ? " would be" : (Redeclared == 1 ? " is" : " are")) + " ended and declared again in " + domainText);
            }

            if (WithdrawnAnswered > 0)
            {
                parts.Add(Count(WithdrawnAnswered, "earlier decision", "earlier decisions") + (dryRun ? " would be" : (WithdrawnAnswered == 1 ? " is" : " are")) + " withdrawn, since someone answered "
                    + (WithdrawnAnswered == 1 ? "it" : "them") + " for the old domain");
            }

            if (WithdrawnNoOwner > 0)
            {
                parts.Add(Count(WithdrawnNoOwner, "waiting decision", "waiting decisions") + (dryRun ? " would be" : (WithdrawnNoOwner == 1 ? " is" : " are")) + " withdrawn, since " + domainText + " has no data owner");
            }

            if (Reasked > 0)
            {
                parts.Add(Count(Reasked, "waiting decision", "waiting decisions") + (dryRun ? " would ask " : (Reasked == 1 ? " now asks " : " now ask ")) + owner + " from the start");
            }

            if (Opened > 0)
            {
                parts.Add(Count(Opened, "new decision", "new decisions") + (dryRun ? " would ask " : (Opened == 1 ? " asks " : " ask ")) + owner);
            }

            if (Waiting > 0)
            {
                parts.Add(Count(Waiting, "use", "uses") + (dryRun ? " would wait" : (Waiting == 1 ? " waits" : " wait")) + " for a data owner of " + domainText);
            }

            string head = " The two domains answer to different data owners, so the decisions do not move with the rows.";
            return parts.Count == 0 ? head : head + " " + char.ToUpperInvariant(parts[0][0]) + parts[0].Substring(1) + (parts.Count > 1 ? "; " + string.Join("; ", parts.Skip(1)) : string.Empty) + ".";
        }
    }

    // The new data owner answers for a moved row (wave 3 verification, major twice). A proposed decision nobody answered
    // yet is asked again from the start (the engine cancels its requests and opens new ones under the row's new domain).
    // One that someone answered, or any when the new domain has no data owner, is withdrawn by the engine. A confirmed row
    // that then has no open decision and needs one gets a new one, which the engine opens and sends for the person who
    // moved the rows (ComposedCall.OpenDataDecision): whoever may move the rows, the platform admin or the data governance
    // lead, the new owner is asked, and no proposal right is asked of the mover (a choice under D-41).
    private static void AskNewOwner(ToolScope s, DataTree tree, string column, StoredRow row, string? newOwner, string toDomain, string reason, MoveAsks asks)
    {
        foreach (StoredRow decision in ProposedDataDecisions(s, column, row.Id))
        {
            // A decision nobody sent yet (an AI client's draft waits for its person) asks nobody: OP-02 resolves who
            // answers when it is submitted, under the row's new domain, so it stays as it is.
            IReadOnlyList<StoredRow> records = s.Query("approval-record", ("decision", decision.Id));
            if (records.Count == 0)
            {
                continue;
            }

            bool answered = records.Any(r => r.Text("outcomeMeaning") is string o && o != "pending" && o != "cancelled" && o != "expired");
            if (newOwner != null && !answered)
            {
                s.Call.ReaskApprovals(decision.Id);
                asks.Reasked++;
                continue;
            }

            s.Call.WithdrawDataDecision(decision.Id, "The rows moved to " + tree.PathText(toDomain) + ", which answers to another data owner"
                + (newOwner == null ? " and has none now" : ", after someone had answered this decision for the old domain") + ". Reason: " + reason);
            if (newOwner == null)
            {
                asks.WithdrawnNoOwner++;
            }
            else
            {
                asks.WithdrawnAnswered++;
            }
        }

        if (!IsOpenDataRow(row, s) || row.Text("statusMeaning") != "registered" || ProposedDataDecisions(s, column, row.Id).Count > 0)
        {
            return;
        }

        bool mapping = column == "subjectDataMapping";
        JsonTreeNode? evaluation = s.Call.DataUse(row.Id)?.Payload;
        string state = Js.Text(evaluation, "state") ?? "needed";
        if (state == "no-one-to-ask")
        {
            asks.Waiting++;
            s.Call.Warn(Product.Engine.Core.Reasons.ReasonCodes.DataNoOneToAsk, "DC-06: " + (mapping ? "a copy" : "a data use") + " moved to " + tree.PathText(toDomain) + " needs a decision, but nobody can answer it now. Propose a data owner first, then the decision.", "Data Domain", toDomain);
            return;
        }

        if (state != "needed" && state != "re-decide")
        {
            return;
        }

        StoredRow? app = mapping ? null : row.Text("businessApplication") is string appId ? s.Rows.Get("business-application", appId) : null;
        StoredRow? location = row.Text("dataLocation") is string locationId ? s.Rows.Get("data-location", locationId) : null;
        string statement = mapping
            ? "Allow a " + (row.Text("dataOrigin") ?? "copy").Replace('-', ' ') + " of " + tree.PathText(toDomain) + " in " + (location?.Text("labelAsSeen") ?? row.Text("dataLocation") ?? "its location") + ", outside production."
            : "Allow " + (app == null ? "the application" : AppName(app)) + " to " + AccessWords(row.Text("access") ?? "read") + " " + tree.PathText(toDomain) + LocationWords(s, row.Text("dataLocation")) + (row.Flag("reachesAi") ? ", with AI" : string.Empty) + ".";
        string context = "The " + (mapping ? "copy" : "use") + " moved to " + tree.PathText(toDomain) + ", which answers to another data owner, so its data owner decides it. "
            + (mapping ? string.Empty : (row.Text("purpose") is string purpose && purpose.Length > 0 ? "Purpose: " + purpose + " " : string.Empty)) + "Reason for the move: " + reason;
        s.Call.OpenDataDecision(mapping ? EngineKeys.DataMappingSubjectKind : EngineKeys.DataUseSubjectKind, row.Id, statement, context, out _);
        asks.Opened++;
    }

    private static List<StoredRow> ProposedDataDecisions(ToolScope s, string column, string rowId)
    {
        return s.Query("decision", (column, rowId), ("decisionType", EngineKeys.DataUseDecisionType), ("statusMeaning", "proposed")).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
    }

    // A use's decision state in words, for a summary.
    private static string StateWords(string? state)
    {
        return state switch
        {
            "not-needed" => "no decision needed",
            "no-one-to-ask" => "nobody to ask",
            "re-decide" => "decide again",
            "breaks-rule" => "breaks a rule",
            null => "unknown",
            _ => state.Replace('-', ' '),
        };
    }
    // The acting data owner of a domain (OP-15), or null when it resolves to nobody.
    private static string? DataOwnerOf(ToolScope s, string domainKey)
    {
        JsonTreeNode? resolved = s.Call.DataDomains(new[] { domainKey })[0].Payload;
        JsonTreeNode? owner = Js.Items(resolved, "owners").FirstOrDefault(o => Js.Text(o, "ownerRoleKey") == EngineKeys.DataOwnerRole);
        return Js.Text(Js.Member(owner, "holder"), "principalId");
    }

    private static bool HasAcceptedDataDecision(ToolScope s, string column, string rowId)
    {
        return s.Query("decision", (column, rowId), ("decisionType", EngineKeys.DataUseDecisionType), ("statusMeaning", "accepted")).Count > 0;
    }

    // The columns of a row declared again in another domain, from today.
    private static (string, JsonTreeNode?)[] CopyColumns(StoredRow row, string domainKey, string today, params string[] names)
    {
        var columns = new List<(string, JsonTreeNode?)> { ("dataDomain", JsonTreeNode.FromString(domainKey)), ("startOn", JsonTreeNode.FromString(today)) };
        foreach (string name in names)
        {
            if (row.Column(name) is JsonTreeNode value && value.Kind != JsonTreeKind.Null)
            {
                columns.Add((name, value));
            }
        }

        return columns.ToArray();
    }
}
