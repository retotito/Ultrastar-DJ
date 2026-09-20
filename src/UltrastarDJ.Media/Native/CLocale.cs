using System.Runtime.InteropServices;

namespace UltrastarDJ.Media.Native;

/// <summary>
/// libmpv refuses to start unless the C locale's LC_NUMERIC is "C" (it parses floats with the C
/// library). .NET does not touch the C locale, but a host UI toolkit may — so we force it once.
/// </summary>
internal static partial class CLocale
{
    // LC_NUMERIC: 4 on macOS/BSD and Windows CRT, 1 on glibc.
    private static int LcNumeric => OperatingSystem.IsLinux() ? 1 : 4;

    private static bool _done;

    public static void EnsureCNumeric()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetLocaleWin(LcNumeric, "C");
            }
            else
            {
                SetLocaleUnix(LcNumeric, "C");
            }
        }
        catch (DllNotFoundException)
        {
            // No libc found — nothing we can do; mpv_create will report it.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [LibraryImport("libc", EntryPoint = "setlocale", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SetLocaleUnix(int category, string locale);

    [LibraryImport("ucrtbase", EntryPoint = "setlocale", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SetLocaleWin(int category, string locale);
}
