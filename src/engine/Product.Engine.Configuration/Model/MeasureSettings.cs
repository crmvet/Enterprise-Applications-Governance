using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Product.Engine.Configuration.Model;

// The measure targets and the slide-back signal thresholds as configuration rows (document 21, section 3.6; document 24,
// B24-22; playbook chapters 17 and 19). Two C04 Setting rows of value type json hold them, so the sponsor's targets and
// the thresholds change through a configuration change and an accepted decision like any other rule:
// - measure-targets: { "<measure key>": <number or null> }. A measure with no member, or null, has no target yet; the
//   playbook says to measure first and then agree a target, so Balanced ships an empty object.
// - signal-thresholds: { "<signal key>": { "<parameter>": <number> } }, the numbers of the signals that have one. A signal
//   with no member keeps the playbook's default.
// V38 checks both shapes against the keys below, and get_measures reads them. The keys are the product's own (the nine
// default measures and the ten signals of chapter 17), so a typo is refused when the draft is validated.
public static class MeasureSettings
{
    public const string TargetsSetting = "measure-targets";

    public const string ThresholdsSetting = "signal-thresholds";

    // The measures get_measures computes, in its order. slide-back-signals counts the signals that fire, so its target is
    // a count (0 means quiet).
    public static readonly IReadOnlyList<string> MeasureKeys = new ReadOnlyCollection<string>(new[]
    {
        "lead-time", "share-registered", "owner-coverage", "decisions-on-time", "standard-change-share",
        "deployments-on-record", "documents-current", "ai-work-accepted", "slide-back-signals",
    });

    // The ten signals of chapter 17, by number.
    public static readonly IReadOnlyList<string> SignalKeys = new ReadOnlyCollection<string>(new[]
    {
        "changes-wait-for-a-meeting", "environments-by-ticket", "gap-check-stale", "registered-late", "configuration-not-kept",
        "unsanctioned-ai", "tool-rebuilt", "no-human-approver", "documents-slipping", "access-drift",
    });

    // The numbers a signal's threshold takes, with the playbook's default for each. Signals that fire on any row have no
    // number; the configuration-not-kept signal reads the configuration-review-days setting.
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> ThresholdDefaults =
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>>(new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.Ordinal)
        {
            ["changes-wait-for-a-meeting"] = Numbers(("emergencySharePercent", 20m), ("standardShareDropPoints", 10m)),
            ["gap-check-stale"] = Numbers(("days", 30m)),
            ["documents-slipping"] = Numbers(("percent", 20m)),
        });

    // The problems with a measure-targets value, or none. Each is a sentence that ends with its fix.
    public static IEnumerable<string> TargetProblems(IEnumerable<KeyValuePair<string, decimal?>> members)
    {
        foreach (KeyValuePair<string, decimal?> member in members)
        {
            if (!MeasureKeys.Contains(member.Key, StringComparer.Ordinal))
            {
                yield return "names the measure `" + member.Key + "`, which is not one of " + string.Join(", ", MeasureKeys.Select(k => "`" + k + "`")) + ". Use one of them";
            }
            else if (member.Value.HasValue && member.Value.Value < 0)
            {
                yield return "gives `" + member.Key + "` the target " + member.Value.Value + ". A target is a number of zero or more, or null for no target";
            }
        }
    }

    // The problems with one signal's member of a signal-thresholds value, or none.
    public static IEnumerable<string> ThresholdProblems(string signalKey, IEnumerable<KeyValuePair<string, decimal?>> parameters)
    {
        if (!ThresholdDefaults.TryGetValue(signalKey, out IReadOnlyDictionary<string, decimal>? known))
        {
            yield return "names the signal `" + signalKey + "`, which has no threshold number. Use one of " + string.Join(", ", ThresholdDefaults.Keys.Select(k => "`" + k + "`"));
            yield break;
        }

        foreach (KeyValuePair<string, decimal?> parameter in parameters)
        {
            if (!known.ContainsKey(parameter.Key))
            {
                yield return "gives `" + signalKey + "` the number `" + parameter.Key + "`, which it does not take. Use " + string.Join(" or ", known.Keys.Select(k => "`" + k + "`"));
            }
            else if (!parameter.Value.HasValue || parameter.Value.Value < 0)
            {
                yield return "gives `" + signalKey + "." + parameter.Key + "` no number of zero or more. Give it a number";
            }
        }
    }

    private static IReadOnlyDictionary<string, decimal> Numbers(params (string Name, decimal Value)[] values)
    {
        return new ReadOnlyDictionary<string, decimal>(values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));
    }
}
