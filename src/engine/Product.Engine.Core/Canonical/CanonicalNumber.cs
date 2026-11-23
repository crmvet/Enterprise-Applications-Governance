using System;
using System.Globalization;
using System.Text;

namespace Product.Engine.Core.Canonical;

// Canonical form of a JSON number, worked out from its decimal text.
//
// Why not floating point: double formatting differs between .NET Framework 4.8 and .NET 10.
// Working on the digits keeps the result exact and the same on every target and culture.
//
// The rule: the value is sign, digits and a power of ten. Leading and trailing zeros are dropped.
// The layout then follows the ECMAScript Number toString layout (as RFC 8785 does), over the exact digits:
//   integers up to 21 digits are plain ("100", "-7"); small fractions use "0.000001";
//   anything else uses one leading digit and an exponent ("1e+21", "1.5e-7").
// Negative zero is written "0". "1", "1.0", "10e-1" and "0.1E1" all become "1".
internal static class CanonicalNumber
{
    // More exponent digits than this is refused. It bounds the work and the output for hostile input.
    internal const int MaxExponentDigits = 9;

    // Returns true and the canonical text when text is exactly one JSON number (RFC 8259 grammar).
    internal static bool TryCanonicalize(string text, out string canonical, out string error)
    {
        canonical = string.Empty;
        error = string.Empty;
        int n = text.Length;
        int i = 0;

        bool negative = false;
        if (i < n && text[i] == '-')
        {
            negative = true;
            i++;
        }

        // Integer part: "0" or a digit 1 to 9 then digits. No leading zeros, no plus sign.
        int intStart = i;
        if (i < n && text[i] == '0')
        {
            i++;
        }
        else if (i < n && text[i] >= '1' && text[i] <= '9')
        {
            while (i < n && IsDigit(text[i]))
            {
                i++;
            }
        }
        else
        {
            error = "A number needs digits before any point or exponent.";
            return false;
        }

        string intDigits = text.Substring(intStart, i - intStart);

        string fracDigits = string.Empty;
        if (i < n && text[i] == '.')
        {
            i++;
            int fracStart = i;
            while (i < n && IsDigit(text[i]))
            {
                i++;
            }

            if (i == fracStart)
            {
                error = "A number needs digits after the decimal point.";
                return false;
            }

            fracDigits = text.Substring(fracStart, i - fracStart);
        }

        long exponent = 0;
        if (i < n && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            bool exponentNegative = false;
            if (i < n && (text[i] == '+' || text[i] == '-'))
            {
                exponentNegative = text[i] == '-';
                i++;
            }

            int expStart = i;
            while (i < n && IsDigit(text[i]))
            {
                i++;
            }

            if (i == expStart)
            {
                error = "A number needs digits in its exponent.";
                return false;
            }

            string expDigits = text.Substring(expStart, i - expStart).TrimStart('0');
            if (expDigits.Length > MaxExponentDigits)
            {
                error = "A number exponent is out of range.";
                return false;
            }

            exponent = expDigits.Length == 0 ? 0 : long.Parse(expDigits, NumberStyles.None, CultureInfo.InvariantCulture);
            if (exponentNegative)
            {
                exponent = -exponent;
            }
        }

        if (i != n)
        {
            error = "A number has extra characters.";
            return false;
        }

        canonical = Layout(negative, intDigits + fracDigits, exponent - fracDigits.Length);
        return true;
    }

    // digits times ten to the power exponent, with the sign. Digits are ASCII 0 to 9 and may have leading or trailing zeros.
    private static string Layout(bool negative, string digits, long exponent)
    {
        int lead = 0;
        while (lead < digits.Length && digits[lead] == '0')
        {
            lead++;
        }

        if (lead == digits.Length)
        {
            // Zero. Negative zero is also "0".
            return "0";
        }

        int end = digits.Length;
        while (digits[end - 1] == '0')
        {
            end--;
        }

        exponent += digits.Length - end;
        string d = digits.Substring(lead, end - lead);
        int k = d.Length;

        // The value is 0.d times ten to the power point.
        long point = k + exponent;

        var sb = new StringBuilder(k + 24);
        if (negative)
        {
            sb.Append('-');
        }

        if (k <= point && point <= 21)
        {
            sb.Append(d);
            sb.Append('0', (int)(point - k));
        }
        else if (point > 0 && point <= 21)
        {
            sb.Append(d, 0, (int)point);
            sb.Append('.');
            sb.Append(d, (int)point, k - (int)point);
        }
        else if (point > -6 && point <= 0)
        {
            sb.Append("0.");
            sb.Append('0', (int)(-point));
            sb.Append(d);
        }
        else
        {
            long shown = point - 1;
            sb.Append(d[0]);
            if (k > 1)
            {
                sb.Append('.');
                sb.Append(d, 1, k - 1);
            }

            sb.Append('e');
            sb.Append(shown < 0 ? '-' : '+');
            sb.Append(Math.Abs(shown).ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }
}
