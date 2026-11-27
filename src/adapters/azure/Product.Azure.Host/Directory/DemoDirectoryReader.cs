using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

// The directory of a demo install and of a local run (product review, story step 11): the fictional personas and the
// fictional groups they are in, as the persona switch (D-39) and the development identity already trust them. Roles and
// access lists the people in each bound group from it, the binding check finds each bound group that a persona is in, and
// the Teams page lists each team's members from it: the role groups a persona signs in with, and the teams it is in that
// bind no role (a persona's "teams", such as a support team; document 24, B24-20). It reads no Entra and holds no token.
// Choice (D-41, recorded in docs/design/step2/26-security-model.md): a bound group no persona is in counts as not found, and the
// demo has no app role assignments, so a workload grant is reported as having none. It serves only in the Demo
// environment (demo personas) and in Development with the development identity on.
// Wave 4b QA (D-41, recorded in doc 26 and the Azure STATUS): a user's account state comes from the demo's fictional
// directory, which is the estate itself: the directory state and leave date the store's Principal Reference records (the
// state the demo estate sets, the last owner check kept, or a leave a person recorded). A person of the persona file
// with no reference is enabled, and anyone else unknown. So "Check the owners" can be performed in a demo: it finds the
// disabled account and the leavers the estate holds, and records the run as read from the demo's fictional directory,
// as the gap check reads the demo's fictional inventory (DemoInventoryReader). Before, it answered that the directory
// was not connected, so the web app offered an action no demo could perform.
// Fix round 1 of wave 4 (product review, D-41): the persona file's "members" are the estate's other fictional people (the
// Change board's Ines Varga, Marcus Oyelaran and Felix Arendt, the data owners Greta Lund and Wren Abernathy, and so on),
// whom nobody signs in as. They join their groups and teams here, in both environments, so a committee shows its
// members and the owners of the estate show in the teams they work in. They never give a request a role: roles come
// from a request's own groups.
public sealed class DemoDirectoryReader : IDirectoryReader, IDemoDirectory
{
    // What an owner check run records as the admin role used (O22 Admin Role Used), as DemoInventoryReader does.
    public const string AdminRoleUsedMarker = "demo: the demo's fictional directory";

    private readonly IReadOnlyList<Person> _people;
    private readonly IRecordReader? _rows;

    private DemoDirectoryReader(IReadOnlyList<Person> people, IRecordReader? rows)
    {
        _people = people;
        _rows = rows;
    }

    // The reader for this host, or null when the host is neither a demo install with personas nor a local run with the
    // development identity on.
    public static DemoDirectoryReader? From(IServiceProvider services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
        ProductWebAppOptions web = services.GetRequiredService<IOptions<ProductWebAppOptions>>().Value;
        var people = new List<Person>();
        if (ProductDemo.IsDemo(environment))
        {
            foreach (ProductDemoPersona persona in web.AllDemoPersonas.Where(p => ProductDemo.IsFictionalId(p.ObjectId)))
            {
                people.Add(new Person(persona.ObjectId.Trim(), persona.Label, persona.Groups.Concat(persona.Teams).Where(ProductDemo.IsFictionalId).Select(g => g.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            }
        }
        else if (environment.IsDevelopment())
        {
            IConfiguration configuration = services.GetRequiredService<IConfiguration>();
            if (!configuration.GetValue<bool>("Product:DevIdentity:Enabled"))
            {
                return null;
            }

            foreach (IConfigurationSection persona in configuration.GetSection("Product:DevIdentity:Personas").GetChildren())
            {
                string? objectId = persona["ObjectId"];
                string kind = persona["PrincipalKind"] ?? "user";
                if (!ProductDemo.IsFictionalId(objectId) || !string.Equals(kind, "user", StringComparison.Ordinal))
                {
                    continue;
                }

                // A harness login (a11y.mjs, screenshots.mjs) is not one of the story's people, so its groups never make
                // a persona a member of a team it is not in (mateo-web carries the reader group).
                if (web.DevelopmentPersonas.Any(p => p.Harness && string.Equals(p.Name, persona.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string label = web.DevelopmentPersonas.FirstOrDefault(p => string.Equals(p.Name, persona.Key, StringComparison.OrdinalIgnoreCase))?.Label
                    ?? web.DevelopmentPersonas.FirstOrDefault(p => string.Equals(p.Name, persona.Key + "-web", StringComparison.OrdinalIgnoreCase))?.Label
                    ?? persona.Key;

                // The groups it signs in with, and the teams it is in that bind no role (members only, never a role).
                string[] groups = persona.GetSection("Groups").GetChildren().Concat(persona.GetSection("Teams").GetChildren()).Select(g => g.Value).Where(ProductDemo.IsFictionalId).Select(g => g!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                people.Add(new Person(objectId!.Trim(), label, groups));
            }
        }

        foreach (ProductDemoPersona member in Members(services, environment, web))
        {
            if (ProductDemo.IsFictionalId(member.ObjectId) && !string.IsNullOrWhiteSpace(member.Label))
            {
                people.Add(new Person(member.ObjectId.Trim(), member.Label.Trim(), member.Groups.Concat(member.Teams).Where(ProductDemo.IsFictionalId).Select(g => g.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) { IsMember = true });
            }
        }

        // One entry per person: a persona listed twice (nadia and nadia-web) keeps the label with a role in it, and every
        // login of that person adds its groups and teams. Only the story's named personas count ("Yusuf Brennan,
        // configuration admin"): a person none of whose logins has a name of its own, such as "A configuration admin", is
        // a harness login, not one of the people the demo story tells (wave 3 verification: it showed as a third
        // configuration admin where the story has two). A login with no name of its own beside a named one, such as
        // demo-priya beside priya-web, is the same person, so its teams count (wave 4 demo kit: on a local run Field
        // Apps Support and Platform Team showed no members, because demo-priya and demo-owen carry the teams).
        List<Person> distinct = people
            .GroupBy(p => p.ObjectId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(p => p.IsNamed))
            .Select(g => g.OrderByDescending(p => p.Label.Contains(',')).ThenByDescending(p => p.IsMember).First() with { Groups = g.SelectMany(p => p.Groups).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() })
            .ToList();
        return distinct.Count == 0 ? null : new DemoDirectoryReader(distinct, services.GetService<IRecordStore>());
    }

    // The estate's other people: the "members" of the persona file. The file is the one Product:WebApp:DemoPersonasFile
    // names, else Product:Directory:DemoMembersFile, else seed/demo-personas.json next to the content root (a local run
    // from the server's project folder, or the sandbox kit's local server from src/adapters/azure). None when no file is
    // found or it cannot be read: the personas alone are the directory then.
    private static IEnumerable<ProductDemoPersona> Members(IServiceProvider services, IHostEnvironment environment, ProductWebAppOptions web)
    {
        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        var candidates = new List<string>();
        foreach (string? named in new[] { web.DemoPersonasFile, configuration["Product:Directory:DemoMembersFile"] })
        {
            if (!string.IsNullOrWhiteSpace(named))
            {
                candidates.Add(named!.Trim());
            }
        }

        candidates.Add(Path.Combine("seed", "demo-personas.json"));
        candidates.Add(Path.Combine("..", "seed", "demo-personas.json"));
        string? file = candidates.Select(c => ProductDemo.ResolveFile(c, environment)).FirstOrDefault(File.Exists);
        if (file == null)
        {
            return Array.Empty<ProductDemoPersona>();
        }

        try
        {
            return new ConfigurationBuilder().AddJsonFile(file, optional: false, reloadOnChange: false).Build().GetSection("members").Get<List<ProductDemoPersona>>() ?? new List<ProductDemoPersona>();
        }
        catch (Exception exception) when (exception is FormatException || exception is IOException || exception is InvalidDataException || exception is InvalidOperationException)
        {
            return Array.Empty<ProductDemoPersona>();
        }
    }

    public DirectoryAnswer<IReadOnlyList<DirectoryObject>> GroupMembers(BackendCaller asPerson, string groupObjectId)
    {
        IReadOnlyList<DirectoryObject> members = _people
            .Where(p => p.Groups.Contains(groupObjectId, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new DirectoryObject(p.ObjectId, p.Name))
            .ToList();
        return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(members);
    }

    public DirectoryAnswer<DirectoryObject?> Group(BackendCaller asPerson, string groupObjectId)
    {
        Person? member = _people.FirstOrDefault(p => p.Groups.Contains(groupObjectId, StringComparer.OrdinalIgnoreCase));
        if (member == null)
        {
            return DirectoryAnswer<DirectoryObject?>.Of(null);
        }

        // A fictional group has no name of its own. The tools name it for the role its binding carries ("Reader group
        // (demo)"); this neutral label is what any other reader sees, never one member's role (wave 3 verification: the
        // reader group read "PMO group (demo)" after its first member).
        return DirectoryAnswer<DirectoryObject?>.Of(new DirectoryObject(groupObjectId, "Demo group", "group"));
    }

    // The account state and leave date of a person in the demo's fictional directory: as the store's Principal Reference
    // records them, or enabled for a person of the persona file with no reference, or unknown.
    public DirectoryAnswer<DirectoryUserState> UserState(BackendCaller asPerson, string objectId)
    {
        StoredRow? reference = _rows?.Query(RowQuery.For("principal-reference"))
            .FirstOrDefault(r => string.Equals(r.Text("entraObjectId"), objectId, StringComparison.OrdinalIgnoreCase));
        if (reference != null)
        {
            DateTime? leavesOn = DateTime.TryParseExact(reference.Text("leavesOn"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime day) ? (DateTime?)day : null;
            return DirectoryAnswer<DirectoryUserState>.Of(new DirectoryUserState(reference.Text("directoryState") ?? "enabled", leavesOn, reference.Text("displayLabel")));
        }

        Person? person = _people.FirstOrDefault(p => string.Equals(p.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
        return DirectoryAnswer<DirectoryUserState>.Of(new DirectoryUserState(person == null ? "unknown" : "enabled", null, person?.Name));
    }

    public DirectoryAnswer<IReadOnlyList<DirectAssignment>> DirectAssignments(BackendCaller asPerson)
    {
        return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Of(Array.Empty<DirectAssignment>());
    }

    private sealed record Person(string ObjectId, string Label, string[] Groups)
    {
        // One of the estate's other people (the persona file's "members"), not a login.
        public bool IsMember { get; init; }

        // A story person: a persona with a role in its label, or an estate member.
        public bool IsNamed => IsMember || Label.Contains(',');

        // "Yusuf Brennan" from "Yusuf Brennan, configuration admin".
        public string Name => Label.Split(',')[0].Trim();
    }
}
