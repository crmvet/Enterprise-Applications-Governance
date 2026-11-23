using System;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Core.Canonical;

// Thrown when text or a value cannot take part in canonical JSON.
// Adapters map it to the invalid input category (document 25, section 7) with input.json-invalid.
public sealed class CanonicalJsonException : FormatException
{
    public CanonicalJsonException(string message, int position = -1)
        : base(position >= 0 ? message + " (at character " + position.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : message)
    {
        Position = position;
    }

    // The zero-based character index in the JSON text, or -1 when the value did not come from text.
    public int Position { get; }

    public string ReasonCode => ReasonCodes.InputJsonInvalid;
}
