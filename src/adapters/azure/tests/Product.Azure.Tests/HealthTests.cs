using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Azure.Stores;
using Xunit;

namespace Product.Azure.Tests;

// The health probes: anonymous, status only. Readiness asks the store every time, within a time budget, and a store that
// hangs holds at most one check.
public sealed class HealthTests
{
    [Fact]
    public async Task BothProbesAnswerAnonymouslyWithTheStatusOnly()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall live = await host.Get("/health/live");
        Assert.Equal(200, live.Status);
        Assert.Equal("{\"status\":\"live\"}", live.Body);
        HttpCall ready = await host.Get("/health/ready");
        Assert.Equal(200, ready.Status);
        Assert.Equal("{\"status\":\"ready\"}", ready.Body);
        Assert.Equal("no-store", ready.Header("Cache-Control"));
    }

    [Fact]
    public async Task ReadinessAnswers503WhileTheStoreRefusesAndReadyAgainWithoutARestart()
    {
        var probe = new StoreProbe();
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings { Services = probe.Install });
        Assert.Equal(200, (await host.Get("/health/ready")).Status);

        probe.Reachable = false;
        HttpCall down = await host.Get("/health/ready");
        Assert.Equal(503, down.Status);
        Assert.Equal("{\"status\":\"unavailable\"}", down.Body);
        Assert.Equal("no-store", down.Header("Cache-Control"));
        Assert.Contains(host.Logs.Entries, e => e.Contains("Product.Azure.Host.Health", StringComparison.Ordinal) && e.Contains("answered 503", StringComparison.Ordinal));

        // No answer is kept: the next probe asks the store again.
        probe.Reachable = true;
        Assert.Equal(200, (await host.Get("/health/ready")).Status);
        Assert.Equal(3, probe.Checks);
    }

    [Fact]
    public async Task ReadinessAnswers503WithinItsBudgetWhenTheStoreHangsAndStartsNoSecondCheck()
    {
        var probe = new StoreProbe();
        var settings = new HostSettings { Services = probe.Install };
        settings.Settings["Product:Health:ReadyTimeoutSeconds"] = "1";
        await using ProductTestHost host = ProductTestHost.Start(settings);

        probe.Hang();
        try
        {
            var clock = Stopwatch.StartNew();
            HttpCall[] answers = await Task.WhenAll(host.Get("/health/ready"), host.Get("/health/ready"), host.Get("/health/ready"));
            clock.Stop();
            Assert.All(answers, a => Assert.Equal(503, a.Status));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "The probes took " + clock.Elapsed);

            // The three probes waited for one check, which is still waiting on the store.
            Assert.Equal(1, probe.Checks);
        }
        finally
        {
            probe.Release();
        }

        // Once the store answers, readiness is back without a restart. The probe right after the release may still find
        // the first check running and wait for it, so the store saw one or two checks.
        HttpCall ready = await WaitForReady(host);
        Assert.Equal(200, ready.Status);
        Assert.InRange(probe.Checks, 1, 2);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    public void AReadinessBudgetOutsideOneToSixtySecondsFailsStartup(string seconds)
    {
        var settings = new HostSettings { Seed = false };
        settings.Settings["Product:Health:ReadyTimeoutSeconds"] = seconds;
        Assert.Contains("Product:Health:ReadyTimeoutSeconds must be from 1 to 60", StartupFailures.Messages(settings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReadinessCheckAsksTheStoreAgainAfterEachAnswer()
    {
        var probe = new StoreProbe();
        await using ProductTestHost host = ProductTestHost.Start();
        var readiness = new ProductReadiness(probe.Wrap(host.Get<IProductStore>()));

        Assert.True(await readiness.Check(TimeSpan.FromSeconds(5)));
        probe.Reachable = false;
        Assert.False(await readiness.Check(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, probe.Checks);
    }

    // The hung check from the first probes ends once the store answers; until then a probe waits for it.
    private static async Task<HttpCall> WaitForReady(ProductTestHost host)
    {
        HttpCall last = await host.Get("/health/ready");
        for (int i = 0; i < 20 && last.Status != 200; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            last = await host.Get("/health/ready");
        }

        return last;
    }
}
