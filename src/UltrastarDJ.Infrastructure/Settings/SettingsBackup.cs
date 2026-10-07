using System.IO.Compression;
using System.Text.Json;

namespace UltrastarDJ.Infrastructure.Settings;

/// <summary>
/// Backup of what cannot be rebuilt: the settings documents (players and mics, outputs and latency, sources, songbook,
/// app settings, song marks) as one zip, saved wherever the DJ likes. The song index, USDB catalog, cache and logs
/// are left out — a scan and a USDB sync rebuild them. So is <c>usdb.json</c>: it holds the USDB password in plain
/// text, and a backup on a stick must not. A restore is staged and applied at the next start, before anything loads
/// its settings: the running app would otherwise save over the restored files on its way out.
/// </summary>
public static class SettingsBackup
{
    public const string ManifestName = "ultrastar-dj-backup.json";
    private const string PendingFolder = "restore-pending";
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "usdb.json" };

    /// <summary>Writes the backup zip. Returns how many settings documents it holds.</summary>
    public static int Create(string settingsDir, string zipPath)
    {
        string temp = zipPath + ".tmp";
        int count = 0;
        using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.EnumerateFiles(settingsDir, "*.json").Order())
            {
                if (!Excluded.Contains(Path.GetFileName(file)))
                {
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
                    count++;
                }
            }

            using StreamWriter manifest = new(zip.CreateEntry(ManifestName).Open());
            manifest.Write(JsonSerializer.Serialize(new { app = "Ultrastar DJ", createdUtc = DateTime.UtcNow, documents = count }));
        }

        File.Move(temp, zipPath, overwrite: true);
        return count;
    }

    /// <summary>Checks the zip and stages its documents for the next start. Throws <see cref="InvalidDataException"/> if it is not a backup.</summary>
    public static void Stage(string zipPath, string settingsDir)
    {
        string pending = Path.Combine(settingsDir, PendingFolder);
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            if (zip.GetEntry(ManifestName) is null)
            {
                throw new InvalidDataException("This zip is not an Ultrastar DJ backup.");
            }

            List<ZipArchiveEntry> documents = [.. zip.Entries.Where(e => e.FullName != ManifestName)];
            // Plain file names only: nothing may land outside the settings folder.
            if (documents.Any(e => e.FullName != Path.GetFileName(e.FullName) || !e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("This backup holds unexpected files.");
            }

            if (Directory.Exists(pending))
            {
                Directory.Delete(pending, recursive: true);
            }

            Directory.CreateDirectory(pending);
            foreach (ZipArchiveEntry e in documents)
            {
                e.ExtractToFile(Path.Combine(pending, e.FullName));
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"The backup could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>At startup, before the settings are read: moves a staged restore into place. Returns how many documents.</summary>
    public static int ApplyPending(string settingsDir)
    {
        string pending = Path.Combine(settingsDir, PendingFolder);
        if (!Directory.Exists(pending))
        {
            return 0;
        }

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(pending, "*.json"))
        {
            File.Copy(file, Path.Combine(settingsDir, Path.GetFileName(file)), overwrite: true);
            count++;
        }

        Directory.Delete(pending, recursive: true);
        return count;
    }
}
