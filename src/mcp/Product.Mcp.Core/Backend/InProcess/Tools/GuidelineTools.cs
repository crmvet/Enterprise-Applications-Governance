using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Resources;

namespace Product.Mcp.Core.Backend.InProcess;

// Tool 90, list_guidelines (document 24, B24-18; the Guidelines and playbook screen): why each rule exists. The guideline
// rows of the configuration in force (C39, the adopter's own copy and whether it is adopted), the playbook chapters by
// track with the guidelines each applies (D-17: a title and a link, never the text), the builder paths and the sanctioned
// clients. It serves on the REST API what the guideline:// and playbook://chapter/ resources serve on the MCP endpoint.
// Any role reads it: configuration rows are read by all roles (document 26, section 5.3).
//
// GuidelineRef is the chip the pages show beside a requirement, a gate or a refusal: the guideline's key, number, title and
// the link to the adopter's copy.
internal static partial class ToolHandlers
{
    // ---- 90. list_guidelines ----

    private static JsonTreeNode ListGuidelines(ToolScope s)
    {
        string? number = s.Text("number");
        bool retired = s.Flag("includeRetired") ?? true;
        string? hub = HubBase(s);
        var rows = s.AllRows("guideline")
            .Where(r => retired || r.IsActive)
            .Where(r => number == null || Js.Text(r.Columns, "number") == number)
            .OrderBy(r => GuidelineOrder(Js.Text(r.Columns, "number"))).ThenBy(r => r.KeyPath, StringComparer.Ordinal)
            .ToList();
        if (number != null && rows.Count == 0)
        {
            throw s.NotFound("Guideline", number, "number");
        }

        IReadOnlyList<PlaybookChapter> chapters = s.Options.Playbook.Chapters.ToList();
        var guidelines = rows.Select(r =>
        {
            string n = Js.Text(r.Columns, "number") ?? r.KeyPath;
            int approvals = s.ActiveRows("approval-rule").Count(a => Js.Text(a.Columns, "guidelineKey") == r.KeyPath);
            int requirements = s.ActiveRows("gate-requirement").Count(g => Js.Text(g.Columns, "guidelineKey") == r.KeyPath);
            int protections = s.ActiveRows("column-protection-rule").Count(c => Js.Text(c.Columns, "guidelineKey") == r.KeyPath);
            string? localUrl = HttpsText(Js.Text(r.Columns, "localUrl"));
            string? hubUrl = HttpsText(Js.Text(r.Columns, "hubUrl"));
            return Js.Obj(
                ("key", JsonTreeNode.FromString(r.KeyPath)),
                ("number", JsonTreeNode.FromString(n)),
                ("version", JsonTreeNode.FromString(Js.Text(r.Columns, "version") ?? "1.0")),
                ("title", JsonTreeNode.FromString(GuidelineTitle(r))),
                ("rule", JsonTreeNode.FromString(Js.Text(r.Columns, "rule") ?? GuidelineTitle(r))),
                ("homeChapter", Js.Str(Js.Text(r.Columns, "chapter"))),
                ("chapters", Arr(chapters.Where(c => c.GuidelineNumbers.Contains(n, StringComparer.Ordinal)).Select(c => JsonTreeNode.FromInt64(c.Number)))),
                ("isActive", JsonTreeNode.FromBoolean(r.IsActive)),
                ("isAdopted", JsonTreeNode.FromBoolean(Js.Flag(r.Columns, "isAdopted") ?? true)),
                ("notAdoptedReason", Js.Str(Js.Text(r.Columns, "notAdoptedReason"))),
                ("localText", Js.Str(Js.Text(r.Columns, "localText"))),
                ("localUrl", Js.Str(localUrl)),
                ("hubUrl", Js.Str(hubUrl)),
                ("url", Js.Str(localUrl ?? hubUrl)),
                ("supersedes", Js.Str(Js.Text(r.Columns, "supersedesKey") ?? Js.Text(r.Columns, "supersedes"))),
                ("usedBy", Js.Obj(
                    ("approvalRules", JsonTreeNode.FromInt64(approvals)),
                    ("gateRequirements", JsonTreeNode.FromInt64(requirements)),
                    ("columnProtections", JsonTreeNode.FromInt64(protections)),
                    ("refusalCodePrefixes", Js.Strs(GuidelineRefs.Prefixes.Where(p => p.Guideline == n).Select(p => p.Prefix))))));
        }).ToList();

        var chapterItems = chapters.Select(c => Js.Obj(
            ("number", JsonTreeNode.FromInt64(c.Number)),
            ("title", JsonTreeNode.FromString(c.Title)),
            ("track", Js.Str(c.Track)),
            ("guidelineNumbers", Js.Strs(c.GuidelineNumbers)),
            ("url", hub == null ? JsonTreeNode.Null : Js.Str(HttpsText(hub.TrimEnd('/') + "/playbook/" + c.Slug))))).ToList();
        var tracks = chapters.GroupBy(c => c.Track ?? "Other", StringComparer.Ordinal)
            .Select(g => Js.Obj(("track", JsonTreeNode.FromString(g.Key)), ("chapters", Arr(g.Select(c => JsonTreeNode.FromInt64(c.Number))))));
        JsonTreeNode buildPaths = Arr(s.ActiveRows("list-value").Where(v => v.KeyPath.StartsWith("build-path/", StringComparison.Ordinal))
            .OrderBy(v => Js.Number(v.Columns, "sortOrder") ?? long.MaxValue).ThenBy(v => v.KeyPath, StringComparer.Ordinal)
            .Select(v => Js.Obj(
                ("key", JsonTreeNode.FromString(v.KeyPath.Substring("build-path/".Length))),
                ("label", JsonTreeNode.FromString(Js.Text(v.Columns, "label") ?? v.KeyPath)),
                ("description", Js.Str(Js.Text(v.Columns, "description"))))));
        JsonTreeNode clients = Arr(s.ActiveRows("known-client").OrderBy(r => r.KeyPath, StringComparer.Ordinal).Select(r => Js.Obj(
            ("key", JsonTreeNode.FromString(r.KeyPath)),
            ("label", JsonTreeNode.FromString(Js.Text(r.Columns, "label") ?? r.KeyPath)),
            ("isAiClient", JsonTreeNode.FromBoolean(Js.Flag(r.Columns, "isAiClient") == true)),
            ("isAllowed", JsonTreeNode.FromBoolean(Js.Flag(r.Columns, "isAllowed") != false)))));

        int active = rows.Count(r => r.IsActive);
        JsonTreeNode data = Js.Obj(
            ("hubBaseUrl", Js.Str(hub)),
            ("guidelines", Arr(guidelines)),
            ("chapters", number == null ? Arr(chapterItems) : Js.EmptyArray),
            ("tracks", number == null ? Arr(tracks) : Js.EmptyArray),
            ("buildPaths", number == null ? buildPaths : Js.EmptyArray),
            ("knownClients", number == null ? clients : Js.EmptyArray));
        string summary = number != null
            ? number + " " + GuidelineTitle(rows[0]) + (rows[0].IsActive ? "." : " (retired).")
            : Count(active, "guideline is", "guidelines are") + " in force, " + Count(rows.Count - active, "retired", "retired") + "; " + Count(chapters.Count, "playbook chapter", "playbook chapters") + "."
                + (hub == null ? " No guideline hub link is set, so the chapters carry no link." : string.Empty);
        return s.Answer(data, summary);
    }

    // { key, number, title, url } for a guideline key, or null: the chip beside a requirement, a gate or a refusal.
    internal static JsonTreeNode GuidelineRef(ToolScope s, string? key)
    {
        SnapshotRow? row = key == null ? null : s.ConfigRow("guideline", key);
        if (row == null)
        {
            return JsonTreeNode.Null;
        }

        return Js.Obj(
            ("key", JsonTreeNode.FromString(row.KeyPath)),
            ("number", JsonTreeNode.FromString(Js.Text(row.Columns, "number") ?? row.KeyPath.ToUpperInvariant())),
            ("title", JsonTreeNode.FromString(GuidelineTitle(row))),
            ("url", Js.Str(HttpsText(Js.Text(row.Columns, "localUrl")) ?? HttpsText(Js.Text(row.Columns, "hubUrl")))));
    }

    internal static string GuidelineTitleOf(SnapshotRow row)
    {
        return GuidelineTitle(row);
    }

    // The guideline's title: its label without the leading number ("G2 Name the owners..." gives "Name the owners...").
    private static string GuidelineTitle(SnapshotRow row)
    {
        string label = Js.Text(row.Columns, "label") ?? Js.Text(row.Columns, "rule") ?? row.KeyPath;
        string? number = Js.Text(row.Columns, "number");
        return number != null && label.StartsWith(number + " ", StringComparison.Ordinal) ? label.Substring(number.Length + 1) : label;
    }

    // G1 before G2 before G10.
    private static int GuidelineOrder(string? number)
    {
        return number != null && number.Length > 1 && int.TryParse(number.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;
    }

    private static string? HubBase(ToolScope s)
    {
        return HttpsText(s.Setting("guideline-hub-base-url"));
    }

    private static string? HttpsText(string? url)
    {
        return url != null && url.StartsWith("https://", StringComparison.Ordinal) && url.Length <= 2048 ? url : null;
    }
}
