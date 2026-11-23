using System.Collections.Generic;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Tests;

// The inventory page the walkthrough's gap check reads as the platform admin: two registered apps, the new app, and one
// app nobody registered. The same items as the MCP core's walkthrough world. Fictional, no network. A test stand-in for
// the live inventory reader, which this release does not bind in production.
internal sealed class WalkthroughInventory : IInventoryReader
{
    public const string SchedulerCanvas = "00000000-0000-0000-0000-000000000201";
    public const string RoutePlannerApp = "00000000-0000-0000-0000-000000000202";
    public const string FieldInspectionsCanvas = "00000000-0000-0000-0000-000000000209";
    public const string OvertimeCanvas = "00000000-0000-0000-0000-000000000210";

    public DirectoryAnswer<InventoryPage> Query(BackendCaller asAdmin, string estateKey, IReadOnlyList<string>? environmentIds, string? skipToken)
    {
        return DirectoryAnswer<InventoryPage>.Of(new InventoryPage(
            new[]
            {
                new InventoryItem(SchedulerCanvas, "microsoft.powerapps/canvasapps", "Inspection Scheduler", "Field Ops Prod", "canvas-app"),
                new InventoryItem(RoutePlannerApp, "microsoft.powerapps/apps", "Route Planner", "Field Ops Prod", "model-driven-app"),
                new InventoryItem(FieldInspectionsCanvas, "microsoft.powerapps/canvasapps", "Field Inspections", "Field Ops Prod", "canvas-app"),
                new InventoryItem(OvertimeCanvas, "microsoft.powerapps/canvasapps", "Crew Overtime Tracker", "Field Ops Prod", "canvas-app", Personas.PriyaId),
            },
            null,
            "Power Platform administrator"));
    }
}
