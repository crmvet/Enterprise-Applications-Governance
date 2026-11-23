using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Product.Azure.Tests;

// The MCP core's scripted walkthrough (src/demo/walkthrough/transcript.json) replayed over HTTP against the local server
// on SQLite, and against a fresh in-process run of the MCP core on the in-memory store seeded with the same demo seed.
// Every exchange must give the same outcome digest on both sides and in the transcript, every MCP answer must be the same
// on both sides (correlation IDs masked), and both stores must hold the same rows at the end.
public sealed class WalkthroughReplayTests
{
    [Fact]
    public async Task TheWalkthroughOverRawHttpMatchesTheInProcessRun()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        InProcessWorld world = InProcessWorld.Create();
        await using var channel = new RawMcpChannel(host);

        List<ReplayedExchange> results = await WalkthroughReplay.Run(host, world, channel);

        Assert.Equal(22, results.Count);
        Assert.True(results.All(r => r.DigestsMatch), "Outcome digests differ:\n" + string.Join("\n", results.Where(r => !r.DigestsMatch)) + "\n" + host.Logs);
        Assert.True(results.All(r => r.ViewsMatch), "Answers differ between the in-process run and HTTP:\n" + string.Join("\n", results.Where(r => !r.ViewsMatch)) + "\n" + host.Logs);
        (string inProcessDump, string httpDump) = WalkthroughReplay.Dumps(host, world);
        Assert.Equal(inProcessDump, httpDump);
    }

    [Fact]
    public async Task TheInProcessRunStillAnswersWhatTheTranscriptRecorded()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        InProcessWorld world = InProcessWorld.Create();
        await using var channel = new RawMcpChannel(host);

        List<ReplayedExchange> results = await WalkthroughReplay.Run(host, world, channel);

        List<ReplayedExchange> differ = results.Where(r => r.TranscriptView != null && r.TranscriptView != r.InProcessRecordedView).ToList();
        Assert.True(differ.Count == 0, "Recorded answers differ from this run:\n" + string.Join("\n", differ.Select(r => "#" + r.N + " " + r.Label + "\n  transcript: " + r.TranscriptView + "\n  this run:   " + r.InProcessRecordedView)));
    }

    [Fact]
    public async Task TheWalkthroughThroughTheOfficialSdkClientMatchesTheInProcessRun()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        InProcessWorld world = InProcessWorld.Create();
        await using var channel = new SdkMcpChannel(host);

        List<ReplayedExchange> results = await WalkthroughReplay.Run(host, world, channel);

        Assert.Equal(22, results.Count);
        List<ReplayedExchange> mcp = results.Where(r => r.Label.Contains(" mcp ", System.StringComparison.Ordinal)).ToList();
        Assert.Equal(16, mcp.Count);

        // The SDK refuses to go on when the handshake is refused, so the app-only exchange is compared by its status only.
        ReplayedExchange appOnly = Assert.Single(results, r => r.HttpStatus == 403);
        Assert.Contains("record_deployment", appOnly.Label, System.StringComparison.Ordinal);
        Assert.Equal(403, appOnly.InProcessStatus);
        List<ReplayedExchange> others = results.Where(r => r != appOnly).ToList();
        Assert.True(others.All(r => r.DigestsMatch), "Outcome digests differ:\n" + string.Join("\n", others.Where(r => !r.DigestsMatch)) + "\n" + host.Logs);
        Assert.True(others.All(r => r.ViewsMatch), "Answers differ between the in-process run and the SDK client:\n" + string.Join("\n", others.Where(r => !r.ViewsMatch)) + "\n" + host.Logs);
        (string inProcessDump, string httpDump) = WalkthroughReplay.Dumps(host, world);
        Assert.Equal(inProcessDump, httpDump);
    }

    // The same replay with the SDK server transport behind the endpoint (Product:Mcp:Transport = sdk), the documented
    // alternative when a client needs protocol features the core's own transport does not answer.
    [Fact]
    public async Task TheWalkthroughOverTheSdkServerTransportMatchesTheInProcessRun()
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = "sdk";
        await using ProductTestHost host = ProductTestHost.Start(settings);
        InProcessWorld world = InProcessWorld.Create();

        // The stateless SDK transport opens no server stream, so the client is told not to ask for one.
        await using var channel = new SdkMcpChannel(host, standaloneGetStream: false);

        List<ReplayedExchange> results = await WalkthroughReplay.Run(host, world, channel);

        ReplayedExchange appOnly = Assert.Single(results, r => r.HttpStatus == 403);
        Assert.Equal(403, appOnly.InProcessStatus);
        List<ReplayedExchange> others = results.Where(r => r != appOnly).ToList();
        HttpCall get = await host.Send(System.Net.Http.HttpMethod.Get, "/mcp", Personas.Priya, null, r => r.Headers.TryAddWithoutValidation("Accept", "text/event-stream"));
        Assert.Equal(405, get.Status);
        Assert.True(others.All(r => r.DigestsMatch), "Outcome digests differ:\n" + string.Join("\n", others.Where(r => !r.DigestsMatch)) + "\n" + host.Logs);
        Assert.True(others.All(r => r.ViewsMatch), "Answers differ between the in-process run and the SDK transport:\n" + string.Join("\n", others.Where(r => !r.ViewsMatch)) + "\n" + host.Logs);
    }
}
