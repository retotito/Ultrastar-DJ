using System.IO.Compression;
using UltrastarDJ.Infrastructure.Settings;

namespace UltrastarDJ.Infrastructure.Tests;

public sealed class SettingsBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "usdj-tests", Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(_root, "settings");
    private string Zip => Path.Combine(_root, "backup.zip");

    public SettingsBackupTests()
    {
        Directory.CreateDirectory(Settings);
        File.WriteAllText(Path.Combine(Settings, "players.json"), "{\"players\":[]}");
        File.WriteAllText(Path.Combine(Settings, "marks.json"), "{\"marks\":[]}");
        File.WriteAllText(Path.Combine(Settings, "usdb.json"), "{\"username\":\"dj\",\"password\":\"secret\"}");
        File.WriteAllText(Path.Combine(Settings, "app.json.tmp"), "half written");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Create_ZipsTheSettings_WithoutTheUsdbPassword()
    {
        int files = SettingsBackup.Create(Settings, Zip);

        using ZipArchive zip = ZipFile.OpenRead(Zip);
        List<string> names = [.. zip.Entries.Select(e => e.FullName).Order()];
        Assert.Equal(["marks.json", "players.json", SettingsBackup.ManifestName], names);
        Assert.Equal(2, files);
    }

    [Fact]
    public void Stage_ThenApply_RestoresTheFiles()
    {
        SettingsBackup.Create(Settings, Zip);
        File.WriteAllText(Path.Combine(Settings, "players.json"), "{\"players\":[\"changed later\"]}");

        SettingsBackup.Stage(Zip, Settings);
        // Staged only: what runs now may still save over players.json until the app quits.
        Assert.Contains("changed later", File.ReadAllText(Path.Combine(Settings, "players.json")));

        int applied = SettingsBackup.ApplyPending(Settings);
        Assert.Equal(2, applied);
        Assert.Equal("{\"players\":[]}", File.ReadAllText(Path.Combine(Settings, "players.json")));
        Assert.Equal(0, SettingsBackup.ApplyPending(Settings));   // applied once
    }

    [Fact]
    public void Apply_KeepsTheUsdbLogin()
    {
        SettingsBackup.Create(Settings, Zip);
        SettingsBackup.Stage(Zip, Settings);
        SettingsBackup.ApplyPending(Settings);

        Assert.Contains("secret", File.ReadAllText(Path.Combine(Settings, "usdb.json")));
    }

    [Fact]
    public void Stage_NotABackup_Throws_AndStagesNothing()
    {
        using (ZipArchive zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
        {
            zip.CreateEntry("players.json");
        }

        Assert.Throws<InvalidDataException>(() => SettingsBackup.Stage(Zip, Settings));
        Assert.Equal(0, SettingsBackup.ApplyPending(Settings));
    }

    [Fact]
    public void Stage_EntryWithAPath_Throws()
    {
        // "../" or folders in a zip must never write outside the settings folder.
        using (ZipArchive zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
        {
            zip.CreateEntry(SettingsBackup.ManifestName);
            zip.CreateEntry("../evil.json");
        }

        Assert.Throws<InvalidDataException>(() => SettingsBackup.Stage(Zip, Settings));
    }

    [Fact]
    public void Stage_NotAZip_Throws()
    {
        File.WriteAllText(Zip, "hello");
        Assert.Throws<InvalidDataException>(() => SettingsBackup.Stage(Zip, Settings));
    }
}
