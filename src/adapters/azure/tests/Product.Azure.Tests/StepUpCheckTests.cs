using System;
using System.Security.Claims;
using System.Text;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// The step-up check of a person's approval answer (D-38; document 26, B26-9): it reads only the token's claims.
public sealed class StepUpCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OffNeedsNothing()
    {
        Assert.True(StepUpCheck.Evaluate(new ClaimsPrincipal(new ClaimsIdentity()), new ProductStepUpOptions(), Now).Satisfied);
        Assert.Empty(new ProductStepUpOptions().Problems());
    }

    [Fact]
    public void AFreshSignInMustBeWithinTheMaximumAge()
    {
        var options = new ProductStepUpOptions { Mode = ProductStepUpMode.FreshSignIn, MaxAgeMinutes = 10 };
        Assert.True(StepUpCheck.Evaluate(SignedIn(Now.AddMinutes(-5)), options, Now).Satisfied);

        StepUpResult stale = StepUpCheck.Evaluate(SignedIn(Now.AddMinutes(-11)), options, Now);
        Assert.False(stale.Satisfied);
        Assert.Contains("10 minutes", stale.Message, StringComparison.Ordinal);
        Assert.Null(stale.ClaimsChallenge);

        Assert.False(StepUpCheck.Evaluate(new ClaimsPrincipal(new ClaimsIdentity()), options, Now).Satisfied);
        Assert.False(StepUpCheck.Evaluate(SignedIn(Now.AddHours(2)), options, Now).Satisfied);
        Assert.NotEmpty(new ProductStepUpOptions { Mode = ProductStepUpMode.FreshSignIn, MaxAgeMinutes = 0 }.Problems());
    }

    [Fact]
    public void AnAuthenticationContextMustBeInTheTokenElseTheRefusalCarriesTheChallenge()
    {
        var options = new ProductStepUpOptions { Mode = ProductStepUpMode.AuthenticationContext, AuthenticationContextId = "c1" };
        Assert.True(StepUpCheck.Evaluate(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("acrs", "c1") })), options, Now).Satisfied);
        Assert.True(StepUpCheck.Evaluate(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("acrs", "[\"c3\",\"c1\"]") })), options, Now).Satisfied);

        StepUpResult missing = StepUpCheck.Evaluate(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("acrs", "c2") })), options, Now);
        Assert.False(missing.Satisfied);
        string claims = Encoding.UTF8.GetString(Convert.FromBase64String(missing.ClaimsChallenge!));
        Assert.Equal("{\"access_token\":{\"acrs\":{\"essential\":true,\"value\":\"c1\"}}}", claims);
        Assert.NotEmpty(new ProductStepUpOptions { Mode = ProductStepUpMode.AuthenticationContext, AuthenticationContextId = "mfa" }.Problems());
    }

    private static ClaimsPrincipal SignedIn(DateTime at)
    {
        long seconds = (long)(at - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("auth_time", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)) }));
    }
}
