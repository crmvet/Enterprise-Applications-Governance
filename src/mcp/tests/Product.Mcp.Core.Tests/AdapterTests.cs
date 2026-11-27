using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The thin adapter seam for the MCP library. The core references no MCP library; the pass-through stub passes
// one HTTP request straight to the core.
public sealed class AdapterTests
{
    [Fact]
    public void ThePassThroughStubHandsTheMessageToTheCore()
    {
        McpHarness h = McpHarness.Create();
        IMcpLibraryAdapter adapter = new PassThroughMcpAdapter(h.Server);
        Assert.Contains("pass-through stub", adapter.LibraryName, System.StringComparison.Ordinal);

        McpHttpResponse ok = adapter.Handle(new McpHttpRequest("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}", "Bearer " + McpHarness.TokenOwner));
        Assert.Equal(200, ok.Status);
        JsonTreeNode envelope = Js.Member(Js.Member(Js.Parse(ok.Body!), "result"), "structuredContent")!;
        Assert.Equal(true, Js.Flag(envelope, "ok"));

        McpHttpResponse unauthenticated = adapter.Handle(new McpHttpRequest("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));
        Assert.Equal(401, unauthenticated.Status);
        Assert.True(unauthenticated.Headers.ContainsKey("WWW-Authenticate"));

        Assert.Equal(JsonRpcCodes.ParseError, Js.Number(Js.Member(Js.Parse(adapter.Handle(new McpHttpRequest("{not json")).Body!), "error"), "code"));
        Assert.Equal(JsonRpcCodes.InvalidRequest, Js.Number(Js.Member(Js.Parse(adapter.Handle(new McpHttpRequest("[]")).Body!), "error"), "code"));
        Assert.Null(adapter.Handle(new McpHttpRequest("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", "Bearer " + McpHarness.TokenOwner)).Body);
    }
}

// The custom API names of the Dataverse binding (document 24, section 9.1), for the Dataverse backend.
public sealed class CustomApiBindingTests
{
    [Fact]
    public void ToolNamesMapToPrefixedPascalCaseCustomApis()
    {
        Assert.Equal("<prefix>_GetDependencyImpact", Product.Mcp.Core.Backend.CustomApiBinding.ApiName("<prefix>", "get_dependency_impact"));
        Assert.Equal("<prefix>_Whoami", Product.Mcp.Core.Backend.CustomApiBinding.ApiName("<prefix>", "whoami"));
        Assert.Equal("action", Product.Mcp.Core.Backend.CustomApiBinding.Binding(isWrite: true));
        Assert.Equal("function", Product.Mcp.Core.Backend.CustomApiBinding.Binding(isWrite: false));
    }
}
