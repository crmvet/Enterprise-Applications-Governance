using System.Globalization;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V41: the snapshot fits the binding. On Dataverse a multiline text column holds up to 1,048,576 characters [T3];
// over that is an error, and over 80 percent is a warning. The size is the canonical snapshot text that Content Json
// stores and the hash covers. The limit is a fact of the binding, so a host on another store can pass its own.
public sealed class V41SnapshotSize : ValidationRule, IContentRule
{
    public static readonly V41SnapshotSize Instance = new();

    private V41SnapshotSize()
        : base("V41", ReasonCodes.SnapshotSize, "The snapshot fits the binding")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        long size = CanonicalJson.Write(context.Bundle, CanonicalProfile.Snapshot).Length;
        long limit = context.Facts.SnapshotCharacterLimit;
        string sizeText = size.ToString("N0", CultureInfo.InvariantCulture);
        string limitText = limit.ToString("N0", CultureInfo.InvariantCulture);
        if (size > limit)
        {
            issues.Add(Issue(
                "the snapshot is " + sizeText + " characters, over the " + limitText + " characters the binding stores in one column. Shorten long text, or retire and drop rows you do not need.",
                null,
                null,
                null));
        }
        else if (size * 5 > limit * 4)
        {
            long percent = size * 100 / limit;
            issues.Add(Issue(
                "the snapshot is " + sizeText + " characters, " + percent.ToString(CultureInfo.InvariantCulture) + " percent of the " + limitText
                + " characters the binding stores in one column. Plan to shorten long text before it reaches the limit.",
                null,
                null,
                null,
                ReasonSeverity.Warning));
        }
    }
}
