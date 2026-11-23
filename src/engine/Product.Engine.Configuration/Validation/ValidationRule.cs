using System;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation;

// One OP-09 rule (document 21, section 9; document 25, section 5.11): its rule ID, reason code and severity.
// The code, severity and summary come from the reason code catalog, so they stay in one place.
// Each rule's message names the rule, the entity, the key and the fix, so an AI client can act on it.
public abstract class ValidationRule
{
    protected ValidationRule(string ruleId, string code, string title)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        if (!info.RuleIds.Contains(ruleId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The reason code " + code + " does not belong to rule " + ruleId + ".");
        }

        RuleId = ruleId;
        Code = code;
        Severity = info.DefaultSeverity;
        Summary = info.Message;
        Title = title;
    }

    // V01 to V44.
    public string RuleId { get; }

    // The reason code, such as "key.format".
    public string Code { get; }

    public ReasonSeverity Severity { get; }

    // The catalog's plain sentence for the rule.
    public string Summary { get; }

    // A few words for lists, such as "Keys are unique".
    public string Title { get; }

    // Builds an issue. The message starts with the rule ID, as document 21, section 9 shows.
    protected ValidationIssue Issue(string message, string? entityKey, string? keyPath, string? location = null, ReasonSeverity? severity = null)
    {
        return new ValidationIssue(
            RuleId,
            Code,
            severity ?? Severity,
            RuleId + ": " + message,
            entityKey == null ? null : ConfigurationEntities.LogicalNameOf(entityKey),
            string.IsNullOrEmpty(keyPath) ? null : keyPath,
            location);
    }

    // Builds an issue with another reason code of the same rule, such as stage.rank-duplicate for V14.
    protected ValidationIssue IssueWithCode(string code, string message, string? entityKey, string? keyPath, string? location = null, ReasonSeverity? severity = null)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        if (!info.RuleIds.Contains(RuleId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The reason code " + code + " does not belong to rule " + RuleId + ".");
        }

        return new ValidationIssue(
            RuleId,
            code,
            severity ?? info.DefaultSeverity,
            RuleId + ": " + message,
            entityKey == null ? null : ConfigurationEntities.LogicalNameOf(entityKey),
            string.IsNullOrEmpty(keyPath) ? null : keyPath,
            location);
    }

    // A key or value in backticks for messages, as in document 21, section 9.
    protected static string Q(string? value)
    {
        return "`" + (value ?? string.Empty) + "`";
    }
}

// A rule that checks the composed content as a whole.
public interface IContentRule
{
    void Check(ValidationContext context, ValidationIssues issues);
}
