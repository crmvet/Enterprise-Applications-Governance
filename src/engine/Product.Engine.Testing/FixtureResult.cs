using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Product.Engine.Testing;

public enum FixtureStatus
{
    Passed,
    Failed,

    // The fixture is well formed, but its operation is not built yet.
    Skipped,
}

// The result of one fixture file.
public sealed class FixtureResult
{
    public FixtureResult(
        string file,
        string? operation,
        string? caseName,
        FixtureStatus status,
        IEnumerable<string>? problems = null,
        string? actualOutcome = null,
        IEnumerable<string>? actualRuleIds = null,
        IEnumerable<string>? coveredRuleIds = null,
        IEnumerable<string>? actualCodes = null)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        Operation = operation;
        Case = caseName;
        Status = status;
        Problems = new ReadOnlyCollection<string>((problems ?? Enumerable.Empty<string>()).ToList());
        ActualOutcome = actualOutcome;
        ActualRuleIds = new ReadOnlyCollection<string>((actualRuleIds ?? Enumerable.Empty<string>()).ToList());
        CoveredRuleIds = new ReadOnlyCollection<string>((coveredRuleIds ?? Enumerable.Empty<string>()).ToList());
        ActualCodes = new ReadOnlyCollection<string>((actualCodes ?? Enumerable.Empty<string>()).ToList());
    }

    // The path as given to the runner, relative where the runner can make it relative.
    public string File { get; }

    public string? Operation { get; }

    public string? Case { get; }

    public FixtureStatus Status { get; }

    // Why the fixture failed or was skipped. Empty when it passed.
    public IReadOnlyList<string> Problems { get; }

    public string? ActualOutcome { get; }

    // The rule IDs the operation reported.
    public IReadOnlyList<string> ActualRuleIds { get; }

    // The rule IDs the fixture says it covers (its ruleIds field).
    public IReadOnlyList<string> CoveredRuleIds { get; }

    // The reason codes the operation reported, sorted, without repeats. The engine checks beyond V44 have no rule ID,
    // so a test finds their cases by code.
    public IReadOnlyList<string> ActualCodes { get; }
}
