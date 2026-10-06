using System.Diagnostics;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Hands files and folders to the operating system: reveal a file selected in Finder / Explorer, open a folder, or
/// open a file in its viewer. Avalonia's launcher can open a folder but not select a file in it.
/// </summary>
public static class FileReveal
{
    /// <summary>Opens the file's folder with the file selected.</summary>
    public static void Reveal(string file)
    {
        if (OperatingSystem.IsMacOS())
        {
            Run("open", ["-R", file]);
        }
        else if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false });
        }
        else
        {
            OpenFolder(Path.GetDirectoryName(file) ?? file);
        }
    }

    public static void OpenFolder(string folder)
    {
        if (OperatingSystem.IsMacOS())
        {
            Run("open", [folder]);
        }
        else if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        }
        else
        {
            Run("xdg-open", [folder]);
        }
    }

    /// <summary>A log in a viewer that searches and follows new lines: Console on macOS, the default app elsewhere.</summary>
    public static void OpenLog(string file)
    {
        if (OperatingSystem.IsMacOS())
        {
            Run("open", ["-a", "Console", file]);
        }
        else
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
    }

    private static void Run(string command, string[] args) => Process.Start(new ProcessStartInfo(command, args) { UseShellExecute = false });
}
