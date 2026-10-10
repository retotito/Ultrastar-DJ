namespace UltrastarDJ.App.Localization;

/// <summary>Developer builds show the translation editor and the Pseudo test language.</summary>
public static class BuildInfo
{
#if DEBUG
    public const bool IsDeveloper = true;
#else
    public const bool IsDeveloper = false;
#endif
}
