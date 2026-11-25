namespace UltrastarDJ.Core.Updates;

/// <summary>
/// The "What's new" list of an update (Markdown from the release, `.github/whats-new.md`) as plain lines for the update
/// dialog: one per bullet, without Markdown marks. Headings, empty lines and the release page's links are left out.
/// </summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<string> Lines(string? markdown)
    {
        List<string> lines = [];
        foreach (string raw in (markdown ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line is "" or "-" or "*" || line.StartsWith('#') || line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                line = line[2..].Trim();
            }

            line = line.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal);
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
