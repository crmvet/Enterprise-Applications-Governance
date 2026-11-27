using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

// The Power Platform inventory of a demo install and of a local run (wave 4b review): fictional, read from the product's own
// store and an optional file, never from Microsoft. It follows DemoDirectoryReader: in the Demo environment, and with the
// directory mode Demo in Development, the live reads come from fictional data, so run_gap_check answers instead of
// upstream_unavailable and "Run the gap check" can be performed in a demo.
//
// Choice (D-41, recorded in document 24, tool 35, and the Azure STATUS): the demo's inventory is the tenant the demo
// estate describes. It holds
//   1. every registered component of a kind the gap check covers that has an External Id and is not retired, as the
//      register holds it now (so a component a person registers or claims during the demo is in the next run too);
//   2. the item of every unregistered claim, whatever its status (an item a person dismissed still runs in the tenant);
//   3. the items of the demo inventory file, when there is one (Product:Directory:DemoInventoryFile, default
//      seed/demo-inventory.json): fictional items no application holds yet, each of which opens a claim the first time;
// and leaves out
//   4. every component that already has a vanished claim (it went from the tenant and stays gone, as the demo director's
//      recorded inventory has it: the Timesheets canvas app of the retired Legacy Timesheet App), and
//   5. the External Ids the file lists under "absent".
// A static list alone would not do: every registered component it missed would raise a vanished claim on the final page.
// So a first run opens a claim for each new file item and a second run opens none; the vanished claim is seen again.
// The file is optional, so a demo without one still runs the gap check (no new claim, the vanished one seen again).
// File shape: { "items": [ { "externalId", "labelAsSeen", "componentKindKey", "sourceType", "targetAsSeen", "url",
// "suggestedClaimantObjectId" } ], "absent": [ "<External Id>" ] }. Only https links and fictional object IDs are kept.
// It pages by its own skip token (InventoryPageSize items a page, 25 at most, in External Id order) and names the admin role it used
// as AdminRoleUsedMarker. Environment IDs narrow nothing here: the fictional items carry none, so a run that names them is
// refused with a plain message instead of being read whole.
public sealed class DemoInventoryReader : IInventoryReader
{
    // What the gap check run records as the admin role used (O22 Admin Role Used).
    public const string AdminRoleUsedMarker = "demo: the demo's fictional inventory";

    // The default file, next to the content root or one folder up, as the persona file.
    public static readonly string DefaultFile = Path.Combine("seed", "demo-inventory.json");

    private const string TokenPrefix = "demo:";

    // The demo's fictional inventory pages by 25 at most on every install (wave 4b review; document 24, tool 35), so
    // a run shows "Continue the run" as a large tenant does, on a demo install as on a local run. A smaller
    // InventoryPageSize still counts.
    private const int DemoPageSize = 25;

    private readonly IRecordReader _rows;
    private readonly DirectoryOptions _options;
    private readonly IReadOnlyList<InventoryItem> _fileItems;
    private readonly HashSet<string> _absent;
    private readonly string? _fileProblem;

    public DemoInventoryReader(IRecordReader rows, DirectoryOptions options, IReadOnlyList<InventoryItem>? fileItems = null, IEnumerable<string>? absent = null, string? fileProblem = null)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fileItems = fileItems ?? Array.Empty<InventoryItem>();
        _absent = new HashSet<string>(absent ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _fileProblem = fileProblem;
    }

    // The items the file added (for the tests).
    public IReadOnlyList<InventoryItem> FileItems => _fileItems;

    // The reader for this host: over the product's store, with the demo inventory file when one is found. A file named in
    // Product:Directory:DemoInventoryFile that is missing or unreadable is reported on each run, never served half read.
    public static DemoInventoryReader From(IServiceProvider services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
        DirectoryOptions options = services.GetRequiredService<IOptions<DirectoryOptions>>().Value;
        IRecordReader rows = services.GetRequiredService<IRecordStore>();
        string? named = string.IsNullOrWhiteSpace(options.DemoInventoryFile) ? null : options.DemoInventoryFile.Trim();
        string? file = named != null
            ? ProductDemo.ResolveFile(named, environment)
            : new[] { DefaultFile, Path.Combine("..", DefaultFile) }.Select(c => ProductDemo.ResolveFile(c, environment)).FirstOrDefault(File.Exists);
        if (file == null)
        {
            return new DemoInventoryReader(rows, options);
        }

        if (!File.Exists(file))
        {
            return new DemoInventoryReader(rows, options, fileProblem: "Product:Directory:DemoInventoryFile names " + named + ", which is not in this deployment.");
        }

        try
        {
            (List<InventoryItem> items, List<string> absent) = ReadFile(File.ReadAllText(file));
            return new DemoInventoryReader(rows, options, items, absent);
        }
        catch (Exception exception) when (exception is JsonException || exception is IOException || exception is InvalidOperationException || exception is UnauthorizedAccessException)
        {
            return new DemoInventoryReader(rows, options, fileProblem: "The demo inventory file " + Path.GetFileName(file) + " could not be read: " + exception.Message);
        }
    }

    // The file's items and absent External Ids. An item without an External Id or a label is a mistake in the file and
    // stops the read; a link that is not https and an object ID that is not fictional are left off the item.
    public static (List<InventoryItem> Items, List<string> Absent) ReadFile(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root is not JsonObject body)
        {
            throw new InvalidOperationException("The file is not a JSON object.");
        }

        var items = new List<InventoryItem>();
        foreach (JsonObject item in (body["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            string? externalId = Text(item, "externalId");
            string? label = Text(item, "labelAsSeen");
            if (externalId == null || label == null)
            {
                throw new InvalidOperationException("Each item has an externalId and a labelAsSeen.");
            }

            string? kind = Text(item, "componentKindKey");
            string? url = Text(item, "url");
            string? claimant = Text(item, "suggestedClaimantObjectId");
            items.Add(new InventoryItem(
                externalId,
                Text(item, "sourceType") ?? kind ?? "unknown",
                label,
                Text(item, "targetAsSeen"),
                kind,
                ProductDemo.IsFictionalId(claimant) ? claimant!.Trim() : null,
                url != null && url.StartsWith("https://", StringComparison.Ordinal) ? url : null));
        }

        var absent = (body["absent"] as JsonArray ?? new JsonArray()).Select(a => a is JsonValue v && v.TryGetValue(out string? s) ? s : null).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        return (items, absent);
    }

    public DirectoryAnswer<InventoryPage> Query(BackendCaller asAdmin, string estateKey, IReadOnlyList<string>? environmentIds, string? skipToken)
    {
        if (asAdmin == null)
        {
            throw new ArgumentNullException(nameof(asAdmin));
        }

        if (asAdmin.PrincipalKind != PrincipalKind.User)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("Only a person reads the inventory through the product. Agent identities, agent users and workloads never do.");
        }

        if (!_options.EffectiveInventoryEstateKeys.Contains(estateKey ?? string.Empty, StringComparer.Ordinal))
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The estate " + estateKey + " has no inventory in this demo. Use compare_inventory_list with a list from your own tool.");
        }

        if (environmentIds != null && environmentIds.Any(e => !string.IsNullOrWhiteSpace(e)))
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The demo's fictional inventory has no environments to choose from. Run the gap check over the whole estate.");
        }

        if (_fileProblem != null)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable(_fileProblem);
        }

        int offset = 0;
        if (skipToken != null && (!skipToken.StartsWith(TokenPrefix, StringComparison.Ordinal) || !int.TryParse(skipToken.Substring(TokenPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out offset)))
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The skip token is not one the demo inventory returned.");
        }

        List<InventoryItem> all = Items();
        int size = Math.Max(1, Math.Min(_options.InventoryPageSize, DemoPageSize));
        List<InventoryItem> page = all.Skip(offset).Take(size).ToList();
        string? next = offset + size < all.Count ? TokenPrefix + (offset + size).ToString(CultureInfo.InvariantCulture) : null;
        return DirectoryAnswer<InventoryPage>.Of(new InventoryPage(page, next, AdminRoleUsedMarker));
    }

    // The whole inventory, in External Id order, one item per External Id (a registered component before a claim's item,
    // a claim's item before the file's).
    internal List<InventoryItem> Items()
    {
        var covered = new HashSet<string>(
            _rows.Query(RowQuery.For("component-kind")).Where(k => k.Key != null && k.Flag("isActive") && k.Flag("isGapCheckCovered")).Select(k => k.Key!),
            StringComparer.Ordinal);
        IReadOnlyList<StoredRow> claims = _rows.Query(RowQuery.For("claim"));
        var gone = new HashSet<string>(
            claims.Where(c => c.Text("findingKind") == "vanished").Select(c => c.Text("registeredComponent")).Where(id => id != null).Select(id => id!),
            StringComparer.OrdinalIgnoreCase);
        var goneIds = new HashSet<string>(
            claims.Where(c => c.Text("findingKind") == "vanished").Select(c => c.Text("externalId")).Where(id => id != null).Select(id => id!),
            StringComparer.OrdinalIgnoreCase);

        var byId = new Dictionary<string, InventoryItem>(StringComparer.OrdinalIgnoreCase);
        void Add(InventoryItem item)
        {
            if (!_absent.Contains(item.ExternalId) && !byId.ContainsKey(item.ExternalId))
            {
                byId[item.ExternalId] = item;
            }
        }

        foreach (StoredRow component in _rows.Query(RowQuery.For("technical-component")))
        {
            string? externalId = component.Text("externalId");
            string? kind = component.Text("componentKind");
            if (externalId == null || kind == null || component.Text("retiredOn") != null || !covered.Contains(kind) || gone.Contains(component.Id) || goneIds.Contains(externalId))
            {
                continue;
            }

            Add(new InventoryItem(externalId, kind, component.Text("labelAsSeen") ?? component.Text("name") ?? externalId, component.Text("targetAsSeen"), kind, null, Https(component.Text("url"))));
        }

        foreach (StoredRow claim in claims.Where(c => c.Text("findingKind") != "vanished"))
        {
            string? externalId = claim.Text("externalId");
            if (externalId == null)
            {
                continue;
            }

            string? kind = claim.Text("componentKind");
            Add(new InventoryItem(externalId, claim.Text("sourceTypeAsSeen") ?? kind ?? "unknown", claim.Text("labelAsSeen") ?? externalId, claim.Text("targetAsSeen"), kind, claim.Text("suggestedClaimant"), Https(claim.Text("url"))));
        }

        foreach (InventoryItem item in _fileItems)
        {
            Add(item);
        }

        return byId.Values.OrderBy(i => i.ExternalId, StringComparer.Ordinal).ToList();
    }

    private static string? Https(string? url)
    {
        return url != null && url.StartsWith("https://", StringComparison.Ordinal) ? url : null;
    }

    private static string? Text(JsonObject node, string name)
    {
        return node[name] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }
}
