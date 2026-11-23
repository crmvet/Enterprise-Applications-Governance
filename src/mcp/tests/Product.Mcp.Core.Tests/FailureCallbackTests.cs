using System;
using System.Collections.Generic;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The failure callback (McpServerOptions.OnInternalFailure): the core hands an unexpected failure and its correlation ID
// to the host before it answers with the JSON-RPC internal error, so operators can see why a request failed. Expected
// refusals are answers, not failures, and never reach it.
public sealed class FailureCallbackTests
{
    private const string Call = "{\"name\":\"find_applications\",\"arguments\":{}}";

    [Fact]
    public void AnUnexpectedFailureReachesTheCallbackOnceWithItsCorrelationId()
    {
        McpHarness h = McpHarness.Create();
        var failures = new List<(Exception Failure, string CorrelationId)>();
        McpServerCore server = Server(h, (failure, correlationId) => failures.Add((failure, correlationId)));

        McpResponse response = server.Handle(McpRequest.Call(1, "tools/call", Js.Parse(Call), "Bearer " + McpHarness.TokenOwner));
        Assert.Equal(200, response.HttpStatus);
        Assert.Equal(JsonRpcCodes.InternalError, Js.Number(response.Error, "code"));
        string? named = Js.Text(Js.Member(response.Error, "data"), "correlationId");

        (Exception failure, string correlationId) = Assert.Single(failures);
        Assert.Equal(named, correlationId);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(ThrowingBackend.Message, failure.Message);

        // The client still learns nothing but the correlation ID.
        Assert.DoesNotContain(ThrowingBackend.Message, Js.Write(response.Body!), StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackThatThrowsLeavesTheAnswerUnchanged()
    {
        McpHarness h = McpHarness.Create();
        McpResponse plain = Server(h, null).Handle(McpRequest.Call(1, "tools/call", Js.Parse(Call), "Bearer " + McpHarness.TokenOwner));
        int calls = 0;
        McpResponse withThrowingCallback = Server(h, (failure, correlationId) =>
        {
            calls++;
            throw new InvalidOperationException("the host's logger failed");
        }).Handle(McpRequest.Call(1, "tools/call", Js.Parse(Call), "Bearer " + McpHarness.TokenOwner));

        Assert.Equal(1, calls);
        Assert.Equal(plain.HttpStatus, withThrowingCallback.HttpStatus);
        Assert.Equal(Js.Write(plain.Body!), Js.Write(withThrowingCallback.Body!));
    }

    [Fact]
    public void ExpectedRefusalsAndRethrownFailuresNeverReachTheCallback()
    {
        McpHarness h = McpHarness.Create();
        int calls = 0;
        McpServerCore server = Server(h, (failure, correlationId) => calls++);

        // An unknown tool, a missing role and a bad input are fixed error codes in the envelope.
        Assert.Equal(McpErrorCodes.NotFound, Code(server.Handle(McpRequest.Call(1, "tools/call", Js.Parse("{\"name\":\"approve_decision\",\"arguments\":{}}"), "Bearer " + McpHarness.TokenOwner))));
        Assert.Equal(McpErrorCodes.NotAuthorized, Code(server.Handle(McpRequest.Call(2, "tools/call", Js.Parse("{\"name\":\"run_gap_check\",\"arguments\":{}}"), "Bearer " + McpHarness.TokenReader))));
        Assert.Equal(McpErrorCodes.Validation, Code(server.Handle(McpRequest.Call(3, "tools/call", Js.Parse("{\"name\":\"find_applications\",\"arguments\":{\"colour\":\"blue\"}}"), "Bearer " + McpHarness.TokenOwner))));
        Assert.Equal(401, server.Handle(McpRequest.Call(4, "tools/call", Js.Parse(Call), null)).HttpStatus);
        Assert.Equal(0, calls);

        // With RethrowInternalErrors on, the failure surfaces as the exception and the callback is not called.
        McpServerCore rethrowing = McpHarness.NewServer(new ThrowingBackend(h.Backend), new RecordingTelemetry(), h.Clock, configure: o => o.OnInternalFailure = (failure, correlationId) => calls++);
        Assert.Throws<InvalidOperationException>(() => rethrowing.Handle(McpRequest.Call(5, "tools/call", Js.Parse(Call), "Bearer " + McpHarness.TokenOwner)));
        Assert.Equal(0, calls);
    }

    private static McpServerCore Server(McpHarness h, Action<Exception, string>? callback)
    {
        return McpHarness.NewServer(new ThrowingBackend(h.Backend), new RecordingTelemetry(), h.Clock, configure: o =>
        {
            o.RethrowInternalErrors = false;
            o.OnInternalFailure = callback;
        });
    }

    private static string? Code(McpResponse response)
    {
        return Js.Text(Js.Member(Js.Member(response.Result, "structuredContent"), "error"), "code");
    }

    // The harness's backend, except that find_applications fails as a bug would.
    private sealed class ThrowingBackend : IGovernanceBackend
    {
        public const string Message = "Fixture failure inside the backend.";

        private readonly IGovernanceBackend _inner;

        public ThrowingBackend(IGovernanceBackend inner)
        {
            _inner = inner;
        }

        public IRateLimitStore RateLimits => _inner.RateLimits;

        public SessionResult OpenSession(BackendCaller caller, string correlationId)
        {
            return _inner.OpenSession(caller, correlationId);
        }

        public ToolResult Invoke(BackendSession session, ToolInvocation invocation)
        {
            if (invocation.Tool.Name == "find_applications")
            {
                throw new InvalidOperationException(Message);
            }

            return _inner.Invoke(session, invocation);
        }

        public ResourceResult ReadResource(BackendSession session, string uri, string correlationId)
        {
            return _inner.ReadResource(session, uri, correlationId);
        }
    }
}
