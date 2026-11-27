using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace Product.Azure.Host;

/// <summary>How a person's answer to an approval asks for a fresh sign-in (D-38; document 26, B26-9).</summary>
public enum ProductStepUpMode
{
    /// <summary>No step-up: the person's ordinary sign-in is enough. The default, and the demo's setting.</summary>
    Off,

    /// <summary>
    /// The token's <c>auth_time</c> (an optional claim of the API's app registration) must be no older than
    /// <see cref="ProductStepUpOptions.MaxAgeMinutes"/>. The web app signs the person in again with a login prompt.
    /// </summary>
    FreshSignIn,

    /// <summary>
    /// The token's <c>acrs</c> claim must name <see cref="ProductStepUpOptions.AuthenticationContextId"/>, a Conditional
    /// Access authentication context the tenant ties to its own policy (for example multifactor authentication with a
    /// sign-in frequency). The refusal carries the claims challenge the web app signs in again with.
    /// </summary>
    AuthenticationContext,
}

/// <summary>
/// The step-up setting for <c>answer_approval</c> (D-38: "step-up sign-in is a setting, off in the demo"). Bound from
/// <c>Product:WebApp:StepUp</c>. Only the person entry answers approvals, so only that entry reads it.
/// </summary>
public sealed class ProductStepUpOptions
{
    /// <summary>Off (default), FreshSignIn or AuthenticationContext.</summary>
    public ProductStepUpMode Mode { get; set; } = ProductStepUpMode.Off;

    /// <summary>For FreshSignIn: the oldest sign-in, in minutes, an answer is taken with. 1 to 1440; default 10.</summary>
    public int MaxAgeMinutes { get; set; } = 10;

    /// <summary>For AuthenticationContext: the authentication context ID, <c>c1</c> to <c>c99</c>.</summary>
    public string AuthenticationContextId { get; set; } = string.Empty;

    private static readonly Regex ContextId = new("^c([1-9]|[1-9][0-9])$", RegexOptions.CultureInvariant);

    /// <summary>The problems with the setting, empty when it is valid.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Mode == ProductStepUpMode.FreshSignIn && (MaxAgeMinutes < 1 || MaxAgeMinutes > 1440))
        {
            problems.Add("Product:WebApp:StepUp:MaxAgeMinutes is 1 to 1440 minutes.");
        }

        if (Mode == ProductStepUpMode.AuthenticationContext && !ContextId.IsMatch(AuthenticationContextId ?? string.Empty))
        {
            problems.Add("Product:WebApp:StepUp:AuthenticationContextId names a Conditional Access authentication context, c1 to c99.");
        }

        return problems;
    }
}

/// <summary>What the step-up check found.</summary>
public sealed class StepUpResult
{
    private StepUpResult(bool satisfied, string? message, string? claimsChallenge)
    {
        Satisfied = satisfied;
        Message = message;
        ClaimsChallenge = claimsChallenge;
    }

    /// <summary>The setting is off, or the token is fresh enough.</summary>
    public bool Satisfied { get; }

    /// <summary>Why the answer needs a fresh sign-in; null when satisfied.</summary>
    public string? Message { get; }

    /// <summary>The claims challenge (base64 JSON) for the authentication context mode; null otherwise.</summary>
    public string? ClaimsChallenge { get; }

    internal static readonly StepUpResult Ok = new(true, null, null);

    internal static StepUpResult Refuse(string message, string? claimsChallenge = null)
    {
        return new StepUpResult(false, message, claimsChallenge);
    }
}

/// <summary>
/// The step-up check of a person's approval answer (D-38; document 26, B26-9). It reads only the token's claims:
/// <c>auth_time</c> for a fresh sign-in, <c>acrs</c> for an authentication context. The engine still checks who answers;
/// this check only asks that the person proved it is them a moment ago.
/// </summary>
public static class StepUpCheck
{
    /// <summary>The claim that holds when the person last signed in, in seconds since 1970 (UTC).</summary>
    public const string AuthTimeClaim = "auth_time";

    /// <summary>The claim that lists the authentication contexts the sign-in satisfied.</summary>
    public const string AuthenticationContextClaim = "acrs";

    // A sign-in stamped a little in the future is a clock difference, not a fresh sign-in to trust forever.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Checks the signed-in person's token against the setting.</summary>
    public static StepUpResult Evaluate(ClaimsPrincipal? user, ProductStepUpOptions options, DateTime nowUtc)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        switch (options.Mode)
        {
            case ProductStepUpMode.FreshSignIn:
                string? text = user?.FindFirst(AuthTimeClaim)?.Value;
                if (text == null || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
                {
                    return StepUpResult.Refuse("Answering an approval needs a fresh sign-in, and this sign-in does not say when it happened. Sign in again, then answer.");
                }

                DateTime signedIn = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
                if (signedIn > nowUtc + ClockSkew || nowUtc - signedIn > TimeSpan.FromMinutes(options.MaxAgeMinutes))
                {
                    return StepUpResult.Refuse("Answering an approval needs a sign-in from the last " + options.MaxAgeMinutes.ToString(CultureInfo.InvariantCulture) + " minutes. Sign in again, then answer.");
                }

                return StepUpResult.Ok;
            case ProductStepUpMode.AuthenticationContext:
                string wanted = options.AuthenticationContextId;
                if (ContextsOf(user).Contains(wanted, StringComparer.OrdinalIgnoreCase))
                {
                    return StepUpResult.Ok;
                }

                string challenge = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"access_token\":{\"acrs\":{\"essential\":true,\"value\":\"" + wanted + "\"}}}"));
                return StepUpResult.Refuse("Answering an approval needs the extra sign-in check your organization set for it. Sign in again, then answer.", challenge);
            default:
                return StepUpResult.Ok;
        }
    }

    // The acrs claim as Entra issues it: one claim per value, or one claim that holds a JSON array.
    private static IEnumerable<string> ContextsOf(ClaimsPrincipal? user)
    {
        if (user == null)
        {
            yield break;
        }

        foreach (Claim claim in user.FindAll(AuthenticationContextClaim))
        {
            foreach (string part in claim.Value.Split(new[] { ',', '[', ']', '"', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }
        }
    }
}
