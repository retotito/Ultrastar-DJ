using System.Reflection;

namespace UltrastarDJ.App.Localization;

/// <summary>About this build: its version, and whether it is a developer build (translation editor, Pseudo language).</summary>
public static class BuildInfo
{
#if DEBUG
    public const bool IsDeveloper = true;
#else
    public const bool IsDeveloper = false;
#endif

    /// <summary>The release version as built ("0.2.0-alpha.1"), without the commit the SDK appends ("+2fdf39a…").</summary>
    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "dev";
}
