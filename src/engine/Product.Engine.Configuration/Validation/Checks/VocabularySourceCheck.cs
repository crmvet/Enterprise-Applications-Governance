using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Checks;

// Engine check vocabulary.source (document 25, section 5.11), a warning: vocabulary mappings point to an existing row.
// A Vocabulary Mapping (C11) names its row by entity and key in two text fields, sourceEntity and sourceKey, not by a
// key reference, so V06 does not resolve it (document 22: "It points to a taxonomy row by entity and key, not by
// reference, so one table maps any entity"). The mapping only labels a row with an outside term, so a broken one
// never changes a rule's outcome: it warns on the approval card and blocks nothing.
// A retired row still exists, so a mapping to it does not warn. Only active mappings count.
public sealed class VocabularySourceCheck : EngineCheck, IContentRule
{
    public static readonly VocabularySourceCheck Instance = new();

    private VocabularySourceCheck()
        : base(ReasonCodes.VocabularySource, "Vocabulary mappings point to an existing row")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        foreach (ContentRow mapping in RowFacts.Active(content, "vocabulary-mapping"))
        {
            string? entity = RowFacts.Text(mapping, "sourceEntity");
            string? key = RowFacts.Text(mapping, "sourceKey");
            if (entity == null || key == null)
            {
                // V01 reports the missing required field.
                continue;
            }

            if (!ConfigurationEntities.TryGet(entity, out ConfigurationEntity? info))
            {
                issues.Add(Issue(
                    "Vocabulary Mapping " + Q(mapping.KeyPath) + " names the source entity " + Q(entity) + ", which is not a configuration entity. "
                    + "Fix sourceEntity, or retire the mapping.",
                    "vocabulary-mapping",
                    mapping.KeyPath,
                    mapping.Pointer + "/sourceEntity"));
                continue;
            }

            if (content.TryFindRow(entity, key, out _))
            {
                continue;
            }

            issues.Add(Issue(
                "Vocabulary Mapping " + Q(mapping.KeyPath) + " points to " + info.LogicalName + " " + Q(key) + ", which is not in the set, so the term "
                + Q(RowFacts.Text(mapping, "term")) + " labels nothing. Fix sourceKey, or retire the mapping.",
                "vocabulary-mapping",
                mapping.KeyPath,
                mapping.Pointer + "/sourceKey"));
        }
    }
}
