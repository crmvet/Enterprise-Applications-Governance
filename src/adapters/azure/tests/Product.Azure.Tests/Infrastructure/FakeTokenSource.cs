using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Product.Azure.Host;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Tests;

// The token source of the directory read tests: a fixed fictional token per downstream API, or a refusal, and a record of
// who asked. No Entra, no network. The on-behalf-of exchange itself is tested in OnBehalfOfTests.
internal sealed class FakeTokenSource : IDownstreamTokenSource
{
    public const string GraphToken = "fake-graph-token-0001";
    public const string PowerPlatformToken = "fake-power-platform-token-0001";

    private readonly ConcurrentQueue<(DownstreamApi Api, string ObjectId)> _asked = new();

    // When set, every request is refused with this reason.
    public string? RefuseWith { get; set; }

    public IReadOnlyCollection<(DownstreamApi Api, string ObjectId)> Asked => _asked.ToArray();

    public static string TokenFor(DownstreamApi api)
    {
        return api == DownstreamApi.MicrosoftGraph ? GraphToken : PowerPlatformToken;
    }

    public DownstreamToken ForUser(DownstreamApi api, BackendCaller asPerson, CancellationToken cancellationToken)
    {
        _asked.Enqueue((api, asPerson.ObjectId));
        return RefuseWith != null ? DownstreamToken.Refused(RefuseWith) : DownstreamToken.FromAccessToken(TokenFor(api));
    }
}
