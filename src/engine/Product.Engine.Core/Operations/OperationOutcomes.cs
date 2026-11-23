using System;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Core.Operations;

// Outcome words for answers that are not one of an operation's own outcomes. They follow the error categories
// of document 25, section 7, and match the words the fixture schema allows for every operation.
public static class OperationOutcomes
{
    // A missing or malformed parameter, or an unknown key. HTTP 400.
    public const string InvalidInput = "invalid-input";

    // A rule refuses the request, such as standard-model.does-not-qualify in OP-02.
    public const string Refused = "refused";

    // No valid snapshot, or a broken configuration at run time. HTTP 503. Never a silent fallback (rule 10).
    public const string Unavailable = "unavailable";
}

// Stops an operation with one reason. Operations catch it at their edge and return a result, never an exception
// (document 25, section 7: a rule outcome is a result). Internal, so no caller ever sees it.
internal sealed class OperationStop : Exception
{
    private OperationStop(string outcome, Reason reason)
        : base(reason.Message)
    {
        Outcome = outcome;
        Reason = reason;
    }

    public string Outcome { get; }

    public Reason Reason { get; }

    // A missing or malformed input value. The message says which value and why.
    public static OperationStop Input(string code, string message, string? reference = null)
    {
        return new OperationStop(OperationOutcomes.InvalidInput, new Reason(code, ReasonSeverity.Error, message, null, reference));
    }

    // A configuration the engine cannot work with. The engine refuses and reports it (document 25, rule 10;
    // document 21, section 10.4). The code's catalog entry must carry the Unavailable category.
    public static OperationStop Broken(string code, string message, string? entity, string? reference)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        if (!info.Categories.HasFlag(ReasonCategories.Unavailable))
        {
            throw new InvalidOperationException("The reason code " + code + " is not an unavailable code in the catalog.");
        }

        return new OperationStop(OperationOutcomes.Unavailable, new Reason(code, ReasonSeverity.Error, message, entity, reference));
    }

    // A rule refuses the request as asked.
    public static OperationStop Refuse(string code, string message, string? entity, string? reference)
    {
        return new OperationStop(OperationOutcomes.Refused, new Reason(code, ReasonSeverity.Error, message, entity, reference));
    }
}
