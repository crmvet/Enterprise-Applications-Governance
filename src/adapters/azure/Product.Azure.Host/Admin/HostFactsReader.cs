using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Backend.InProcess;

namespace Product.Azure.Host;

/// <summary>
/// The host settings that are safe to show a platform admin on the Admin page (<c>get_admin_status</c>; document 24,
/// B24-18): the environment's name, whether it is the demo, the step-up sign-in mode, the hour of the daily due-state
/// refresh, the store's kind and schema version, and the approval outbox by state with the rows that need a person's eye.
/// Read fresh for each call. No URL, key, connection string, client ID or tenant ID leaves the host's configuration.
/// </summary>
public static class HostFactsReader
{
    /// <summary>The most outbox rows read for the counts. The Admin page lists at most 50 of them.</summary>
    public const int OutboxReadLimit = 2000;

    /// <summary>A sent card older than this with no answer yet needs a person's eye.</summary>
    public static readonly TimeSpan LongSent = TimeSpan.FromDays(2);

    /// <summary>Reads the facts from the service provider.</summary>
    public static HostFacts Read(IServiceProvider services, DateTime nowUtc)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        IHostEnvironment? environment = services.GetService<IHostEnvironment>();
        ProductStepUpOptions stepUp = services.GetService<IOptions<ProductWebAppOptions>>()?.Value.StepUp ?? new ProductStepUpOptions();
        ProductDueStateRefreshOptions refresh = services.GetService<IOptions<ProductDueStateRefreshOptions>>()?.Value ?? new ProductDueStateRefreshOptions();
        IRecordStore? store = services.GetService<IRecordStore>();
        var facts = new HostFacts
        {
            EnvironmentName = environment?.EnvironmentName ?? string.Empty,
            IsDemo = ProductDemo.IsDemo(environment),
            HoldsDemoEstate = services.GetService<IOptions<ProductWebAppOptions>>()?.Value.Demo == true,
            StepUpMode = stepUp.Mode switch
            {
                ProductStepUpMode.FreshSignIn => "fresh-sign-in",
                ProductStepUpMode.AuthenticationContext => "authentication-context",
                _ => "off",
            },
            DueStateRefreshHourUtc = refresh.HourUtc,
            StoreKind = store is RowStore rows ? rows.StoreKind : store == null ? null : "in-memory",
            RequiredSchemaVersion = (store as RowStore)?.RequiredSchemaVersion,
            SchemaVersion = (store as RowStore)?.RequiredSchemaVersion,
        };

        if (store is IApprovalOutboxStore outbox)
        {
            IReadOnlyList<ApprovalOutboxItem> items = outbox.OutboxItems(OutboxReadLimit);
            var counts = ApprovalOutboxStates.All.ToDictionary(state => state, state => items.Count(i => i.State == state), StringComparer.Ordinal);
            var attention = items
                .Where(i => i.State == ApprovalOutboxStates.Failed || (i.State == ApprovalOutboxStates.Sent && nowUtc - i.UpdatedOnUtc > LongSent))
                .OrderBy(i => i.State == ApprovalOutboxStates.Failed ? 0 : 1).ThenBy(i => i.EnqueuedOnUtc)
                .Select(i => new OutboxAttention(i.ApprovalRecordId, i.DecisionId, i.State, i.Attempts, i.EnqueuedOnUtc, i.UpdatedOnUtc));
            facts.Outbox = new OutboxFacts(counts, attention);
        }

        return facts;
    }
}
