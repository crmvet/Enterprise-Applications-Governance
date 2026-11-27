using System;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// find_records (tool 75; document 24, B24-12): the global search names a record by its number. The number reads in any
// spelling people use, leading zeros never matter, and a row the caller may not read is never found.
public sealed class RecordSearchTests
{
    private static JsonTreeNode Items(McpHarness h, string token, string recordNumber)
    {
        return McpHarness.Data(h.Ok(token, "find_records", "{\"recordNumber\":\"" + recordNumber + "\"}"));
    }

    [Fact]
    public void ARecordIsFoundByItsNumberInAnySpelling()
    {
        McpHarness h = McpHarness.Create();

        // A seeded application: APP-0001, Fixture expense approvals.
        JsonTreeNode app = Assert.Single(Js.Items(Items(h, McpHarness.TokenReader, "app 1"), "items"));
        Assert.Equal("business-application", McpHarness.Text(app, "entity"));
        Assert.Equal(McpHarness.Expense, McpHarness.Text(app, "recordId"));
        Assert.Equal("APP-0001", McpHarness.Text(app, "recordNumber"));
        Assert.Equal("Fixture expense approvals", McpHarness.Text(app, "label"));
        Assert.Equal(McpHarness.Expense, McpHarness.Text(app, "businessApplicationId"));
        Assert.EndsWith("/records/business-application/" + McpHarness.Expense, McpHarness.Text(app, "recordUrl"), StringComparison.Ordinal);

        // A decision the engine numbered, found with or without its leading zeros, in upper or lower case.
        string decision = Scenarios.SubmittedRetirement(h);
        string number = h.Store.Get("decision", decision)!.Text("recordNumber")!;
        Assert.StartsWith("DEC-", number, StringComparison.Ordinal);
        string digits = number.Substring(4).TrimStart('0');
        foreach (string spelling in new[] { number, "dec" + digits, "DEC-" + digits, "Dec " + number.Substring(4) })
        {
            JsonTreeNode envelope = h.Ok(McpHarness.TokenReader, "find_records", "{\"recordNumber\":\"" + spelling + "\"}");
            JsonTreeNode found = Assert.Single(Js.Items(McpHarness.Data(envelope), "items"));
            Assert.Equal("decision", McpHarness.Text(found, "entity"));
            Assert.Equal(decision, McpHarness.Text(found, "recordId"));
            Assert.Equal(number, McpHarness.Text(found, "recordNumber"));
            Assert.Equal("Retire the travel planner", McpHarness.Text(found, "label"));
            Assert.Equal("proposed", McpHarness.Text(found, "status.meaning"));
            Assert.Equal(McpHarness.Travel, McpHarness.Text(found, "businessApplicationId"));
            Assert.EndsWith("/records/decision/" + decision, McpHarness.Text(envelope, "links.recordUrl"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ANumberNoRecordHasFindsNothingAndAnUnknownPrefixIsRefused()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode none = h.Ok(McpHarness.TokenReader, "find_records", "{\"recordNumber\":\"CHG-9999\"}");
        Assert.Empty(Js.Items(McpHarness.Data(none), "items"));
        Assert.Contains("CHG-9999", McpHarness.Text(none, "summary"), StringComparison.Ordinal);
        Assert.Empty(Js.Items(Items(h, McpHarness.TokenReader, "DEC-0"), "items"));
        h.Fails(McpHarness.TokenReader, "find_records", "{\"recordNumber\":\"XYZ-0001\"}", McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenReader, "find_records", "{\"recordNumber\":\"DEC-01-2\"}", McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenReader, "find_records", "{\"recordNumber\":\"crew planner\"}", McpErrorCodes.Validation);
    }

    // intake-visibility "own" (Balanced): the deputy, a requester with no part in the owner's request for a new
    // application, never finds it by its number, as list_intake_requests never lists it. The platform admin finds it. Its
    // triage decision is hidden the same way: decision numbers run in sequence and its statement names the request
    // (security review 2026-11-24).
    [Fact]
    public void AnIntakeRequestTheCallerMayNotReadIsNeverFound()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.SubmittedIntake(h, linked: false);
        string number = h.Store.Get("intake-request", intake)!.Text("recordNumber")!;
        Assert.StartsWith("IR-", number, StringComparison.Ordinal);

        JsonTreeNode admin = Assert.Single(Js.Items(Items(h, McpHarness.TokenAdmin, number), "items"));
        Assert.Equal(intake, McpHarness.Text(admin, "recordId"));
        Assert.Equal("submitted", McpHarness.Text(admin, "status.meaning"));
        Assert.Empty(Js.Items(Items(h, McpHarness.TokenDeputy, number), "items"));

        string triage = h.Store.Get("intake-request", intake)!.Text("triageDecision")!;
        string decisionNumber = h.Store.Get("decision", triage)!.Text("recordNumber")!;
        Assert.Single(Js.Items(Items(h, McpHarness.TokenAdmin, decisionNumber), "items"));
        Assert.Empty(Js.Items(Items(h, McpHarness.TokenDeputy, decisionNumber), "items"));
    }
}
