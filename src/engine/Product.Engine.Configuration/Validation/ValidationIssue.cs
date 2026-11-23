using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Validation;

// What OP-09 checks (document 25, section 5.11; document 21, section 9).
public enum ValidationMode
{
    // A shipped preset in the repo's build: V44 on, V37 off. The result is a preset bundle.
    PresetBuild,

    // A draft edit on a client's working set. The result is a snapshot bundle.
    Draft,

    // The check inside activation. Same checks as a draft for the rules built so far.
    Activation,
}

// One OP-09 finding: a Reason (document 25, section 5.1) plus the rule ID behind it and where it was found.
public sealed class ValidationIssue
{
    public ValidationIssue(string? ruleId, string code, ReasonSeverity severity, string message, string? entity, string? reference, string? location)
    {
        // The Reason constructor checks the code, severity and message.
        Reason = new Reason(code, severity, message, entity, reference);
        RuleId = string.IsNullOrEmpty(ruleId) ? null : ruleId;
        Location = string.IsNullOrEmpty(location) ? null : location;
    }

    // V01 to V44. Null for a check with no V-ID, such as an input problem or an engine check.
    public string? RuleId { get; }

    public Reason Reason { get; }

    public string Code => Reason.Code;

    public ReasonSeverity Severity => Reason.Severity;

    // One sentence that names the rule, the entity, the key and the fix.
    public string Message => Reason.Message;

    // The logical entity name, such as "Gate Policy".
    public string? Entity => Reason.Entity;

    // The key or key path the issue points at.
    public string? Ref => Reason.Ref;

    // A file and JSON pointer for people, such as "balanced/tier.json /rows/0". Not part of the wire form.
    public string? Location { get; }

    // The wire form, as config-bundle.schema.json#/$defs/reason describes it.
    public JsonTreeNode ToJson()
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            JsonNodes.Member("code", Code),
            JsonNodes.Member("severity", SeverityWire(Severity)),
            JsonNodes.Member("message", Message),
        };
        if (RuleId != null)
        {
            members.Add(JsonNodes.Member("ruleId", RuleId));
        }

        if (Entity != null)
        {
            members.Add(JsonNodes.Member("entity", Entity));
        }

        if (Ref != null)
        {
            members.Add(JsonNodes.Member("ref", Ref));
        }

        return JsonTreeNode.CreateObject(members);
    }

    public override string ToString()
    {
        return SeverityWire(Severity) + " " + Code + (Location == null ? string.Empty : " at " + Location) + ": " + Message;
    }

    public static string SeverityWire(ReasonSeverity severity)
    {
        switch (severity)
        {
            case ReasonSeverity.Error:
                return "error";
            case ReasonSeverity.Warning:
                return "warning";
            default:
                return "info";
        }
    }
}

// Collects issues in the order rules find them, without exact repeats.
public sealed class ValidationIssues
{
    private readonly List<ValidationIssue> _items = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public IReadOnlyList<ValidationIssue> Items => _items;

    public bool HasErrors => _items.Any(i => i.Severity == ReasonSeverity.Error);

    public void Add(ValidationIssue issue)
    {
        if (issue == null)
        {
            throw new ArgumentNullException(nameof(issue));
        }

        string identity = issue.RuleId + "\u0000" + issue.Code + "\u0000" + issue.Entity + "\u0000" + issue.Ref + "\u0000" + issue.Message;
        if (_seen.Add(identity))
        {
            _items.Add(issue);
        }
    }

    public void AddRange(IEnumerable<ValidationIssue> issues)
    {
        foreach (ValidationIssue issue in issues)
        {
            Add(issue);
        }
    }
}
