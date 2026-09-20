namespace UltrastarDJ.Infrastructure;

/// <summary>Well-known per-user folders. Created on first access.</summary>
public sealed class AppPaths
{
    public const string AppFolderName = "UltrastarDJ";

    public AppPaths()
    {
        string baseDir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Data = Path.Combine(baseDir, AppFolderName);
        Settings = Path.Combine(Data, "settings");
        Logs = Path.Combine(Data, "logs");
        Cache = Path.Combine(Data, "cache");

        foreach (string dir in new[] { Data, Settings, Logs, Cache })
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string Data { get; }
    public string Settings { get; }
    public string Logs { get; }
    public string Cache { get; }
}
