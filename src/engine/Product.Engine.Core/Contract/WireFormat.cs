using System.Diagnostics.CodeAnalysis;

namespace Product.Engine.Core.Contract;

// The text shape of contract wire values, contract names, outcome words and reason code parts.
public static class WireFormat
{
    // Kebab-case: lowercase ASCII letters and digits in parts joined by single hyphens, starting with a letter.
    // For example "in-progress", "fail-closed", "engine-accept".
    public static bool IsKebabCase([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value) || value![0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        char previous = value[0];
        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool letterOrDigit = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (c == '-')
            {
                if (previous == '-')
                {
                    return false;
                }
            }
            else if (!letterOrDigit)
            {
                return false;
            }

            previous = c;
        }

        return previous != '-';
    }
}
