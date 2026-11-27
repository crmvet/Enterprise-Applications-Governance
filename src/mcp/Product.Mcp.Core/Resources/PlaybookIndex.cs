using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Product.Mcp.Core.Resources;

// The playbook's chapter list for the playbook://chapter/<n> resource (document 24, section 6.12): number, title and a
// link only. The playbook itself lives as wiki or web pages outside the product (D-17); this index never holds its text.
public sealed class PlaybookIndex
{
    private readonly Dictionary<int, PlaybookChapter> _chapters;

    public PlaybookIndex(IEnumerable<PlaybookChapter> chapters)
    {
        _chapters = (chapters ?? Enumerable.Empty<PlaybookChapter>()).ToDictionary(c => c.Number);
    }

    public static PlaybookIndex Empty { get; } = new(Array.Empty<PlaybookChapter>());

    public IEnumerable<PlaybookChapter> Chapters => _chapters.Values.OrderBy(c => c.Number);

    public bool TryGet(int number, out PlaybookChapter chapter)
    {
        if (_chapters.TryGetValue(number, out PlaybookChapter? found))
        {
            chapter = found;
            return true;
        }

        chapter = null!;
        return false;
    }

    // Reads the chapter files of a folder such as content/playbook: "<nn>-<slug>.md", title from the first "# " line, and
    // from the header list before the first "## " heading the track ("- **Track:** IT") and the guideline numbers the
    // chapter applies ("- **Guidelines:** [G1](...), [G4](...)"). Only that metadata is kept, never the text.
    public static PlaybookIndex FromFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Empty;
        }

        var chapters = new List<PlaybookChapter>();
        foreach (string path in Directory.GetFiles(folder, "*.md").OrderBy(p => p, StringComparer.Ordinal))
        {
            string file = Path.GetFileNameWithoutExtension(path);
            int dash = file.IndexOf('-');
            if (dash < 1 || !int.TryParse(file.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > 999)
            {
                continue;
            }

            var header = File.ReadLines(path).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)).ToList();
            string? heading = header.FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
            string title = heading == null ? file.Substring(dash + 1).Replace('-', ' ') : heading.Substring(2).Trim();
            string? track = header.Where(l => l.StartsWith("- **Track:**", StringComparison.Ordinal)).Select(l => l.Substring("- **Track:**".Length).Trim()).FirstOrDefault(t => t.Length > 0);
            string guidelineLine = header.FirstOrDefault(l => l.StartsWith("- **Guidelines:**", StringComparison.Ordinal)) ?? string.Empty;
            var guidelines = System.Text.RegularExpressions.Regex.Matches(guidelineLine, @"\[(G[1-9][0-9]{0,2})\]").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
            chapters.Add(new PlaybookChapter(number, title, file, track, guidelines));
        }

        return new PlaybookIndex(chapters);
    }
}

public sealed class PlaybookChapter
{
    public PlaybookChapter(int number, string title, string slug, string? track = null, IEnumerable<string>? guidelineNumbers = null)
    {
        if (number < 1 || number > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Chapter numbers run from 1 to 999.");
        }

        Number = number;
        Title = string.IsNullOrWhiteSpace(title) ? "Chapter " + number.ToString(CultureInfo.InvariantCulture) : title;
        Slug = slug ?? string.Empty;
        Track = string.IsNullOrWhiteSpace(track) ? null : track!.Trim();
        GuidelineNumbers = new ReadOnlyCollection<string>((guidelineNumbers ?? Enumerable.Empty<string>()).ToList());
    }

    public int Number { get; }

    public string Title { get; }

    // The page name under the hub, such as "02-parties-roles-and-decision-rights".
    public string Slug { get; }

    // The chapter's track, such as Start here, Organization, IT or Shared; null when the header names none.
    public string? Track { get; }

    // The guideline numbers the chapter's header lists, such as G1 and G4.
    public IReadOnlyList<string> GuidelineNumbers { get; }
}
