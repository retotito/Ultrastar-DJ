namespace UltrastarDJ.App.Services;

/// <summary>Command-line options. Parsed once at startup and injected as a singleton.</summary>
public sealed record AppOptions(bool BeamerDebug)
{
    /// <summary>
    /// <c>--beamer-debug</c>: open beamer windows as normal resizable windows on the current screen
    /// instead of fullscreen on a second monitor. For development on a single-screen machine.
    /// </summary>
    public static AppOptions Parse(IReadOnlyList<string> args)
        => new(BeamerDebug: args.Contains("--beamer-debug", StringComparer.OrdinalIgnoreCase));
}
