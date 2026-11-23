using System.Linq;
using System.Text;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V02: every key matches ^[a-z][a-z0-9-]{0,63}$ (document 20, section 3.2).
// Applies to row keys, child keys, keys made by wildcard expansion, and the keys in patch operations.
public sealed class V02KeyFormat : ValidationRule, IContentRule
{
    public static readonly V02KeyFormat Instance = new();

    private V02KeyFormat()
        : base("V02", ReasonCodes.KeyFormat, "Keys use the key format")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (ContentRow row in context.Content.Rows)
        {
            if (row.Key == null)
            {
                issues.Add(Issue(
                    "a " + row.LogicalName + " row at " + row.Pointer + " has no key. Add a key of lowercase letters, digits and hyphens.",
                    row.Entity,
                    row.ParentKey,
                    row.Pointer));
                continue;
            }

            if (!ConfigurationKey.IsValid(row.Key))
            {
                issues.Add(BadKey(row.Entity, row.Key, row.KeyPath, row.Pointer));
            }
        }
    }

    // Checks one key that is not a row of the content yet, such as the key of a patch operation.
    public bool CheckKey(string key, string entityKey, string? location, ValidationIssues issues)
    {
        if (ConfigurationKey.IsValid(key))
        {
            return true;
        }

        issues.Add(BadKey(entityKey, key, key, location));
        return false;
    }

    // Checks a "parent/child" key path of a child entity operation, or a plain key for any other entity.
    public bool CheckKeyPath(string keyPath, ConfigurationEntity entity, string? location, ValidationIssues issues)
    {
        string[] parts = keyPath.Split('/');
        if (entity.IsChild)
        {
            if (parts.Length != 2)
            {
                issues.Add(Issue(
                    entity.LogicalName + " key " + Q(keyPath) + " must be a key path: the " + ConfigurationEntities.LogicalNameOf(entity.ParentKey!) + " key, a slash, then the child key.",
                    entity.Key,
                    keyPath,
                    location));
                return false;
            }

            return parts.All(part => CheckKey(part, entity.Key, location, issues));
        }

        if (parts.Length != 1)
        {
            issues.Add(Issue(
                entity.LogicalName + " key " + Q(keyPath) + " holds a slash, which only child entities use. Use a plain key.",
                entity.Key,
                keyPath,
                location));
            return false;
        }

        return CheckKey(keyPath, entity.Key, location, issues);
    }

    private ValidationIssue BadKey(string entityKey, string key, string keyPath, string? location)
    {
        return Issue(
            ConfigurationEntities.LogicalNameOf(entityKey) + " key " + Q(key) + " is not lowercase letters, digits and hyphens, 1 to 64 characters, starting with a letter. Rename it, for example to " + Q(Suggest(key)) + ".",
            entityKey,
            keyPath,
            location);
    }

    // A key in the right format that stays close to the given text.
    internal static string Suggest(string key)
    {
        var sb = new StringBuilder();
        foreach (char c in key.ToLowerInvariant())
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            sb.Append(ok ? c : '-');
        }

        string text = sb.ToString().Trim('-');
        while (text.Contains("--"))
        {
            text = text.Replace("--", "-");
        }

        if (text.Length == 0 || text[0] < 'a' || text[0] > 'z')
        {
            text = "k-" + text;
        }

        return text.Length > ConfigurationKey.MaxLength ? text.Substring(0, ConfigurationKey.MaxLength).TrimEnd('-') : text;
    }
}
