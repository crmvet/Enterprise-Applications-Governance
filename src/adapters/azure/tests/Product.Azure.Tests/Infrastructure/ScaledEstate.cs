using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;

namespace Product.Azure.Tests;

// A larger fictional estate for the speed checks: the demo estate (seed/demo-estate.json) with its applications copied
// until the estate holds the number asked, each copy with its own owners, decisions, reviews, documents, deployments,
// changes, components, links, dependencies and data uses. Nothing random: the same call gives the same rows.
//
// A copy keeps the shape of its original: the same owners (the same fictional people), tier, kind, stage and portfolio,
// so the reads meet the same mix of owner gaps, due reviews and missing documents at scale. IDs are new patterned
// placeholders (00000000-0000-4000-8000-9xxxxxxxxxxx), record numbers move up by 100000 per copy, and the values a store
// key holds (external IDs, idempotency keys) get the copy's suffix. A copied decision keeps its approval records, and
// every fourth copy also waits on a go-live decision with pending approval requests.
internal static class ScaledEstate
{
    // The entities copied with their application, and the column that names it.
    private static readonly (string Entity, string Column)[] PerApplication =
    {
        ("owner-assignment", "businessApplication"),
        ("technical-component", "businessApplication"),
        ("dependency", "fromApplication"),
        ("decision", "subjectApplication"),
        ("change-request", "businessApplication"),
        ("artifact-version", "businessApplication"),
        ("deployment-record", "businessApplication"),
        ("document-link", "businessApplication"),
        ("review", "businessApplication"),
        ("delivery-link", "subjectApplication"),
        ("data-use", "businessApplication"),
    };

    private static readonly Regex RecordNumber = new("^([A-Z]+)-([0-9]+)$", RegexOptions.CultureInvariant);

    // The demo estate's rows with copies of its applications until there are this many. Fewer than the demo estate
    // holds keeps them all.
    public static JsonTreeNode Rows(JsonTreeNode demoEstate, int applications)
    {
        List<JsonTreeNode> rows = Js.Items(demoEstate, "rows").ToList();
        List<JsonTreeNode> apps = rows.Where(r => Js.Text(r, "entity") == "business-application").ToList();
        var byApp = new Dictionary<string, List<JsonTreeNode>>(StringComparer.Ordinal);
        foreach (JsonTreeNode app in apps)
        {
            byApp[Js.Text(app, "id")!] = new List<JsonTreeNode>();
        }

        var decisions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonTreeNode row in rows)
        {
            string entity = Js.Text(row, "entity")!;
            foreach ((string e, string column) in PerApplication)
            {
                if (e == entity && Js.Text(Js.Member(row, "columns"), column) is string app && byApp.TryGetValue(app, out List<JsonTreeNode>? cluster))
                {
                    cluster.Add(row);
                    if (entity == "decision")
                    {
                        decisions[Js.Text(row, "id")!] = app;
                    }
                }
            }
        }

        foreach (JsonTreeNode record in rows.Where(r => Js.Text(r, "entity") == "approval-record"))
        {
            if (Js.Text(Js.Member(record, "columns"), "decision") is string decision && decisions.TryGetValue(decision, out string? app))
            {
                byApp[app].Add(record);
            }
        }

        var output = new List<JsonTreeNode>(rows);
        long next = 0;
        string NewId() => "00000000-0000-4000-8000-9" + (++next).ToString("D11", CultureInfo.InvariantCulture);
        int count = apps.Count;
        for (int copy = 1; count < applications; copy++)
        {
            List<JsonTreeNode> copied = apps.Take(applications - count).ToList();
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonTreeNode app in copied)
            {
                ids[Js.Text(app, "id")!] = NewId();
                foreach (JsonTreeNode row in byApp[Js.Text(app, "id")!])
                {
                    ids[Js.Text(row, "id")!] = NewId();
                }
            }

            int index = 0;
            foreach (JsonTreeNode app in copied)
            {
                output.Add(Copy(app, ids, copy));
                output.AddRange(byApp[Js.Text(app, "id")!].Select(r => Copy(r, ids, copy)));

                // Every fourth copy waits on a go-live decision: a pending request to its business owner, and one in eight
                // also to the change managers, so My work and the teams meet approvals at scale too.
                if (index++ % 4 == 0)
                {
                    string newApp = ids[Js.Text(app, "id")!];
                    string decision = NewId();
                    output.Add(Row("decision", decision, Js.Obj(
                        ("recordNumber", JsonTreeNode.FromString("DEC-" + (copy * 100000L + 9000 + index).ToString(CultureInfo.InvariantCulture))),
                        ("decisionType", JsonTreeNode.FromString("go-live")),
                        ("subjectKind", JsonTreeNode.FromString("business-application")),
                        ("subjectApplication", JsonTreeNode.FromString(newApp)),
                        ("statement", JsonTreeNode.FromString("Go live for the copy " + copy.ToString("D2", CultureInfo.InvariantCulture))),
                        ("status", JsonTreeNode.FromString("decision-proposed")),
                        ("statusMeaning", JsonTreeNode.FromString("proposed")))));
                    output.Add(Row("approval-record", NewId(), Pending(decision, ("requiredOwnerRole", "business-owner"))));
                    if (index % 8 == 1)
                    {
                        output.Add(Row("approval-record", NewId(), Pending(decision, ("requiredRole", "change-manager"))));
                    }
                }
            }

            count += copied.Count;
        }

        return Js.Obj(("rows", Js.Arr(output)));
    }

    private static JsonTreeNode Row(string entity, string id, JsonTreeNode columns)
    {
        return Js.Obj(("entity", JsonTreeNode.FromString(entity)), ("id", JsonTreeNode.FromString(id)), ("columns", columns));
    }

    private static JsonTreeNode Pending(string decision, (string Column, string Value) asks)
    {
        return Js.Obj(
            ("decision", JsonTreeNode.FromString(decision)),
            (asks.Column, JsonTreeNode.FromString(asks.Value)),
            ("isConsultation", JsonTreeNode.False),
            ("requestedOn", JsonTreeNode.FromString("2026-11-24T09:00:00Z")),
            ("expiresOn", JsonTreeNode.FromString("2026-11-29T09:00:00Z")),
            ("outcome", JsonTreeNode.FromString("approval-pending")),
            ("outcomeMeaning", JsonTreeNode.FromString("pending")));
    }

    private static JsonTreeNode Copy(JsonTreeNode row, Dictionary<string, string> ids, int copy)
    {
        string entity = Js.Text(row, "entity")!;
        var columns = new List<(string, JsonTreeNode?)>();
        foreach (KeyValuePair<string, JsonTreeNode> column in Js.Member(row, "columns")!.Members)
        {
            columns.Add((column.Key, CopyValue(entity, column.Key, column.Value, ids, copy)));
        }

        return Js.Obj(
            ("entity", JsonTreeNode.FromString(entity)),
            ("id", JsonTreeNode.FromString(ids[Js.Text(row, "id")!])),
            ("columns", Js.Obj(columns.ToArray())));
    }

    private static JsonTreeNode CopyValue(string entity, string column, JsonTreeNode value, Dictionary<string, string> ids, int copy)
    {
        if (value.Kind == JsonTreeKind.Array)
        {
            return Js.Arr(value.Items.Select(v => CopyValue(entity, column, v, ids, copy)));
        }

        if (value.Kind != JsonTreeKind.String)
        {
            return value;
        }

        string text = value.GetString();
        if (ids.TryGetValue(text, out string? mapped))
        {
            return JsonTreeNode.FromString(mapped);
        }

        string suffix = copy.ToString("D2", CultureInfo.InvariantCulture);
        switch (column)
        {
            case "recordNumber":
                Match match = RecordNumber.Match(text);
                return match.Success
                    ? JsonTreeNode.FromString(match.Groups[1].Value + "-" + (copy * 100000L + long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture))
                    : value;
            case "name" when entity == "business-application":
                return JsonTreeNode.FromString(text + " " + suffix);
            case "externalId":
            case "idempotencyKey":
            case "url":
                return JsonTreeNode.FromString(text + (text.Contains('?') ? "&" : "?") + "copy=" + suffix);
            default:
                return value;
        }
    }
}
