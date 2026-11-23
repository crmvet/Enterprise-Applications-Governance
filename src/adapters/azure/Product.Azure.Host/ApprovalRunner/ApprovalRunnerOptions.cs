using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Product.Azure.Host;

/// <summary>
/// Settings of the approval runner's routes, bound from <c>Product:ApprovalRunner</c>. None of them is product
/// configuration: who approves, how many and by when stay in the configuration in force.
/// </summary>
public sealed class ApprovalRunnerOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:ApprovalRunner";

    /// <summary>The lease a claim gets when the runner names none: 30 to 3600 seconds. Default 15 minutes.</summary>
    public const int MinLeaseSeconds = 30;

    /// <summary>The longest lease a claim may ask for.</summary>
    public const int MaxLeaseSeconds = 3600;

    /// <summary>
    /// Serves the runner's routes. Off by default: the outbox fills either way, and an admin turns the routes on once the
    /// runner flows, the runner's app registration and its Workload Grant exist. Off, every runner route answers
    /// not_found.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>The lease of a claim, in seconds, when the request names none. Default 900.</summary>
    public int DefaultLeaseSeconds { get; set; } = 900;

    /// <summary>The most items one claim returns: 1 to 50. Default 10.</summary>
    public int MaxClaim { get; set; } = 10;

    /// <summary>
    /// How many times an item may be given back (a release, or a lease that ran out) before it becomes failed: 1 to 20.
    /// Default 3.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;
}

/// <summary>Startup checks of <see cref="ApprovalRunnerOptions"/>.</summary>
public sealed class ApprovalRunnerOptionsValidator : IValidateOptions<ApprovalRunnerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ApprovalRunnerOptions options)
    {
        var failures = new List<string>();
        if (options.DefaultLeaseSeconds < ApprovalRunnerOptions.MinLeaseSeconds || options.DefaultLeaseSeconds > ApprovalRunnerOptions.MaxLeaseSeconds)
        {
            failures.Add("Product:ApprovalRunner:DefaultLeaseSeconds must be from " + ApprovalRunnerOptions.MinLeaseSeconds + " to " + ApprovalRunnerOptions.MaxLeaseSeconds + ".");
        }

        if (options.MaxClaim < 1 || options.MaxClaim > 50)
        {
            failures.Add("Product:ApprovalRunner:MaxClaim must be from 1 to 50.");
        }

        if (options.MaxAttempts < 1 || options.MaxAttempts > 20)
        {
            failures.Add("Product:ApprovalRunner:MaxAttempts must be from 1 to 20.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
