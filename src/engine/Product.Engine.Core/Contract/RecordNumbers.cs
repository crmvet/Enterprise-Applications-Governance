using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Product.Engine.Core.Contract;

// Record numbers (document 22: Record Number on O02, O07, O09, O11, O21, O27 and C46; document 25, B25-9). People name a
// record by its number, such as IR-0108 or DEC-0145; the row ID stays the key. The engine gives a number to every row of
// these entities it creates, its own effects included, and nobody else writes one: OP-08 drops a caller's value (G-09).
//
// The store hands out the number, as it hands out row IDs (document 25, section 3.2): one above the highest number the
// store has given for the entity or holds in a row with the entity's prefix, so the numbers go on after a seeded estate.
// The store reserves it at once and never gives it again, so two calls at the same moment never share a number; a call
// that does not commit leaves a gap, as a database sequence does. A dry run (validateOnly) takes no number.
//
// WORKING DEFAULT (open design question 1): the prefixes are fixed, like the engine meanings. The estate the demo seeds
// uses them with four digits, so a number has at least four digits.
public static class RecordNumbers
{
    // The column every numbered entity holds its number in.
    public const string Column = "recordNumber";

    // The fewest digits a number shows: IR-0001, IR-0108, IR-12345.
    public const int MinimumDigits = 4;

    // The entity keys that carry a record number, and their prefixes.
    public static readonly IReadOnlyDictionary<string, string> Prefixes = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["business-application"] = "APP",
        ["intake-request"] = "IR",
        ["decision"] = "DEC",
        ["change-request"] = "CHG",
        ["review"] = "REV",
        ["configuration-draft"] = "CFG",
        ["data-use"] = "DU",
    });

    public static bool TryGetPrefix(string entity, out string prefix)
    {
        if (entity != null && Prefixes.TryGetValue(entity, out string? found))
        {
            prefix = found;
            return true;
        }

        prefix = string.Empty;
        return false;
    }

    // The number as people read it: the prefix, a hyphen and at least four digits.
    public static string Format(string prefix, long number)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            throw new ArgumentException("A record number has a prefix.", nameof(prefix));
        }

        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Record numbers start at 1.");
        }

        return prefix + "-" + number.ToString("D" + MinimumDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // The number in a Record Number with this prefix, such as 108 for IR-0108. Null for any other text.
    public static long? Parse(string prefix, string? text)
    {
        if (string.IsNullOrEmpty(prefix) || text == null || text.Length <= prefix.Length + 1 || text.Length > prefix.Length + 19
            || !text.StartsWith(prefix + "-", StringComparison.Ordinal))
        {
            return null;
        }

        string digits = text.Substring(prefix.Length + 1);
        foreach (char c in digits)
        {
            if (c < '0' || c > '9')
            {
                return null;
            }
        }

        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0 ? number : null;
    }
}
