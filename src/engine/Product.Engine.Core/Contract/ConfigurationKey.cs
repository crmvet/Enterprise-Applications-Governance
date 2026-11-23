namespace Product.Engine.Core.Contract;

// The key format for configuration rows (document 20, section 3.2; document 21, section 8.5).
// Lowercase ASCII letters, digits and hyphens. 1 to 64 characters. Starts with a letter.
// Keys are already lowercase ASCII, so every comparison is ordinal.
public static class ConfigurationKey
{
    public const int MaxLength = 64;

    // True when the value is a well-formed key. Same rule as V02 (key.format).
    public static bool IsValid(string? value)
    {
        if (value == null || value.Length == 0 || value.Length > MaxLength)
        {
            return false;
        }

        if (value[0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
