using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Validation;

// The OP-09 payload (document 25, section 5.11): isValid, issues, changeSummary and contentHash.
public sealed class ValidationReport
{
    public ValidationReport(IEnumerable<ValidationIssue> issues, IEnumerable<string> changeSummary, string contentHash)
    {
        // Same order as Reason.CanonicalOrder: errors first, then code, entity and reference. Stable on every machine.
        List<ValidationIssue> sorted = (issues ?? throw new ArgumentNullException(nameof(issues)))
            .OrderBy(i => i.Reason, Reason.CanonicalOrder)
            .ToList();
        Issues = new ReadOnlyCollection<ValidationIssue>(sorted);
        ChangeSummary = new ReadOnlyCollection<string>((changeSummary ?? Enumerable.Empty<string>()).ToList());
        ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
    }

    // True when no issue has severity error.
    public bool IsValid => ErrorCount == 0;

    public IReadOnlyList<ValidationIssue> Issues { get; }

    // Plain lines of what changed from the active version.
    public IReadOnlyList<string> ChangeSummary { get; }

    // SHA-256 over the canonical form of the checked bundle (CanonicalHash).
    public string ContentHash { get; }

    public int ErrorCount => Issues.Count(i => i.Severity == ReasonSeverity.Error);

    public int WarningCount => Issues.Count(i => i.Severity == ReasonSeverity.Warning);

    // The issues as Reasons, for an EvaluationResult.
    public IEnumerable<Reason> Reasons => Issues.Select(i => i.Reason);

    // The rule IDs that reported at least one issue, sorted.
    public IReadOnlyList<string> RuleIds => Issues.Where(i => i.RuleId != null).Select(i => i.RuleId!).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();

    // The wire form, as config-bundle.schema.json#/$defs/validationReport describes it.
    public JsonTreeNode ToJson()
    {
        return JsonTreeNode.CreateObject(
            JsonNodes.Member("isValid", JsonTreeNode.FromBoolean(IsValid)),
            JsonNodes.Member("issues", JsonTreeNode.CreateArray(Issues.Select(i => i.ToJson()))),
            JsonNodes.Member("changeSummary", JsonTreeNode.CreateArray(ChangeSummary.Select(JsonTreeNode.FromString))),
            JsonNodes.Member("contentHash", ContentHash));
    }
}
