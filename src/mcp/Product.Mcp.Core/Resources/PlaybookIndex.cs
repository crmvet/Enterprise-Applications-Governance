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

    // Reads the chapter files of a folder such as content/playbook: "<nn>-<slug>.md", title from the first "# " line.
    // Only the number, the title and the slug are kept.
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

            string? heading = File.ReadLines(path).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
            string title = heading == null ? file.Substring(dash + 1).Replace('-', ' ') : heading.Substring(2).Trim();
            chapters.Add(new PlaybookChapter(number, title, file));
        }

        return new PlaybookIndex(chapters);
    }
}

public sealed class PlaybookChapter
{
    public PlaybookChapter(int number, string title, string slug)
    {
        if (number < 1 || number > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Chapter numbers run from 1 to 999.");
        }

        Number = number;
        Title = string.IsNullOrWhiteSpace(title) ? "Chapter " + number.ToString(CultureInfo.InvariantCulture) : title;
        Slug = slug ?? string.Empty;
    }

    public int Number { get; }

    public string Title { get; }

    // The page name under the hub, such as "02-parties-roles-and-decision-rights".
    public string Slug { get; }
}
