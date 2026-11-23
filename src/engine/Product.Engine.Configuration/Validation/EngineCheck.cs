using System;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation;

// One OP-09 engine check beyond V01 to V44 (document 25, section 5.11, "Engine checks beyond V01 to V44").
// Document 25 proposes these checks for document 21's list, but gives them no V-ID yet. So an engine check is named
// by its reason code alone, and its issues carry no rule ID. The code, severity and summary come from the reason
// code catalog, as they do for a ValidationRule. Each message starts with the reason code, then names
// the entity, the key and the fix, so an AI client can act on it.
public abstract class EngineCheck
{
    protected EngineCheck(string code, string title)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        if (info.RuleIds.Count != 0)
        {
            throw new InvalidOperationException("The reason code " + code + " belongs to a V-ID rule. Write it as a ValidationRule.");
        }

        if ((info.Categories & ReasonCategories.Validation) == 0)
        {
            throw new InvalidOperationException("The reason code " + code + " is not a validation code.");
        }

        Code = code;
        Severity = info.DefaultSeverity;
        Summary = info.Message;
        Title = title;
    }

    // The reason code, such as "role.config-admin". It is the check's name.
    public string Code { get; }

    public ReasonSeverity Severity { get; }

    // The catalog's plain sentence for the check.
    public string Summary { get; }

    // A few words for lists.
    public string Title { get; }

    // Builds an issue with no rule ID. The message starts with the reason code.
    protected ValidationIssue Issue(string message, string? entityKey, string? keyPath, string? location = null)
    {
        return new ValidationIssue(
            null,
            Code,
            Severity,
            Code + ": " + message,
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
