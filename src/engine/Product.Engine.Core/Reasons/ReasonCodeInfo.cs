using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core.Reasons;

// The error model categories of document 25, section 7, plus the two kinds of normal answers.
// A code can sit in more than one category. For example rule-group.ambiguous is a validation issue in OP-09
// and an unavailable answer at run time. The operation that returns the code picks the category.
[Flags]
public enum ReasonCategories
{
    None = 0,

    // A reason on a read operation's normal answer.
    Outcome = 1,

    // An OP-09 validation issue in a ValidationReport.
    Validation = 2,

    // Missing or malformed input. HTTP 400.
    InvalidInput = 4,

    // OP-08 refuses a write because of a rule. HTTP 400 on Dataverse, 422 on Azure.
    RefusedRule = 8,

    // The caller lacks the role for the operation. HTTP 403.
    RefusedRole = 16,

    // A referenced row does not exist. HTTP 404.
    NotFound = 32,

    // A reused idempotency key, or an append-only race. HTTP 409.
    Conflict = 64,

    // No valid snapshot, or a broken configuration at run time. HTTP 503. Never a silent fallback.
    Unavailable = 128,
}

// One entry of the reason code catalog.
public sealed class ReasonCodeInfo
{
    public ReasonCodeInfo(
        string code,
        ReasonSeverity defaultSeverity,
        ReasonCategories categories,
        string source,
        IEnumerable<string>? ruleIds,
        bool isProposed,
        string message,
        int? workingDefaultQuestion = null)
    {
        if (!Reason.IsWellFormedCode(code))
        {
            throw new ArgumentException("A reason code is lowercase kebab-case parts joined by dots.", nameof(code));
        }

        if (categories == ReasonCategories.None)
        {
            throw new ArgumentException("A reason code needs at least one category.", nameof(categories));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("A reason code names the operation or rule that returns it.", nameof(source));
        }

        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentException("A reason code needs a plain message.", nameof(message));
        }

        Code = code;
        DefaultSeverity = defaultSeverity;
        Categories = categories;
        Source = source;
        RuleIds = new ReadOnlyCollection<string>(new List<string>(ruleIds ?? Array.Empty<string>()));
        IsProposed = isProposed;
        Message = message;
        WorkingDefaultQuestion = workingDefaultQuestion;
    }

    public string Code { get; }

    // The usual severity. Some operations raise it, for example a gate flag is an error when it stops a deployment.
    public ReasonSeverity DefaultSeverity { get; }

    public ReasonCategories Categories { get; }

    // The operation and document 25 section that return the code, for example "OP-03".
    public string Source { get; }

    // The guard or validation rule IDs that map to the code, for example "G-06" or "V35".
    public IReadOnlyList<string> RuleIds { get; }

    // True when document 25 does not name the code yet. It is proposed by this catalog for document 25 to adopt.
    public bool IsProposed { get; }

    // One plain sentence a person or an AI client can act on. No HTML.
    public string Message { get; }

    // The question in 28, section 3 whose working default the code rests on, if any.
    public int? WorkingDefaultQuestion { get; }

    public override string ToString()
    {
        return Code;
    }
}
