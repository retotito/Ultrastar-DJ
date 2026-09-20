using System.Runtime.InteropServices;

namespace UltrastarDJ.Media.Native;

/// <summary>
/// Raw libmpv C API (client.h + render.h, API 2.x). Only the functions the engine needs.
/// Everything here is unsafe plumbing; <see cref="MpvPlayer"/> is the only consumer.
/// </summary>
internal static unsafe partial class LibMpv
{
    public const string LibraryName = "mpv";

    static LibMpv()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibMpv).Assembly, NativeLibraryLoader.Resolve);
    }

    // ── Client ────────────────────────────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "mpv_client_api_version")]
    public static partial ulong ClientApiVersion();

    [LibraryImport(LibraryName, EntryPoint = "mpv_create")]
    public static partial nint Create();

    [LibraryImport(LibraryName, EntryPoint = "mpv_initialize")]
    public static partial int Initialize(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "mpv_terminate_destroy")]
    public static partial void TerminateDestroy(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetOptionString(nint handle, string name, string value);

    [LibraryImport(LibraryName, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetPropertyString(nint handle, string name, string value);

    [LibraryImport(LibraryName, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetPropertyString(nint handle, string name);

    [LibraryImport(LibraryName, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GetProperty(nint handle, string name, MpvFormat format, void* data);

    [LibraryImport(LibraryName, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetProperty(nint handle, string name, MpvFormat format, void* data);

    [LibraryImport(LibraryName, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ObserveProperty(nint handle, ulong replyUserdata, string name, MpvFormat format);

    [LibraryImport(LibraryName, EntryPoint = "mpv_command")]
    public static partial int Command(nint handle, byte** args);

    [LibraryImport(LibraryName, EntryPoint = "mpv_command_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int CommandString(nint handle, string args);

    [LibraryImport(LibraryName, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int RequestLogMessages(nint handle, string minLevel);

    [LibraryImport(LibraryName, EntryPoint = "mpv_wait_event")]
    public static partial MpvEvent* WaitEvent(nint handle, double timeoutSec);

    [LibraryImport(LibraryName, EntryPoint = "mpv_wakeup")]
    public static partial void Wakeup(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "mpv_set_wakeup_callback")]
    public static partial void SetWakeupCallback(nint handle, delegate* unmanaged[Cdecl]<nint, void> callback, nint userData);

    [LibraryImport(LibraryName, EntryPoint = "mpv_free")]
    public static partial void Free(nint data);

    [LibraryImport(LibraryName, EntryPoint = "mpv_error_string")]
    public static partial nint ErrorString(int error);

    // ── Render (software) ─────────────────────────────────────────────────

    [LibraryImport(LibraryName, EntryPoint = "mpv_render_context_create")]
    public static partial int RenderContextCreate(out nint renderContext, nint handle, MpvRenderParam* renderParams);

    [LibraryImport(LibraryName, EntryPoint = "mpv_render_context_set_update_callback")]
    public static partial void RenderContextSetUpdateCallback(nint renderContext, delegate* unmanaged[Cdecl]<nint, void> callback, nint userData);

    [LibraryImport(LibraryName, EntryPoint = "mpv_render_context_update")]
    public static partial ulong RenderContextUpdate(nint renderContext);

    [LibraryImport(LibraryName, EntryPoint = "mpv_render_context_render")]
    public static partial int RenderContextRender(nint renderContext, MpvRenderParam* renderParams);

    [LibraryImport(LibraryName, EntryPoint = "mpv_render_context_free")]
    public static partial void RenderContextFree(nint renderContext);

    // ── Helpers ───────────────────────────────────────────────────────────

    public static string ErrorMessage(int error)
        => Marshal.PtrToStringUTF8(ErrorString(error)) ?? $"mpv error {error}";

    /// <summary>Reads and frees a string returned by mpv (or null).</summary>
    public static string? TakeString(nint ptr)
    {
        if (ptr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            Free(ptr);
        }
    }

    // ── Enums & structs ───────────────────────────────────────────────────

    public enum MpvFormat
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
    }

    public enum MpvEventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        Idle = 11,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
    }

    public enum MpvEndFileReason
    {
        Eof = 0,
        Stop = 2,
        Quit = 3,
        Error = 4,
        Redirect = 5,
    }

    public enum MpvRenderParamType
    {
        Invalid = 0,
        ApiType = 1,
        SwSize = 17,
        SwFormat = 18,
        SwStride = 19,
        SwPointer = 20,
    }

    public const ulong RenderUpdateFrame = 1UL << 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public MpvEventId EventId;
        public int Error;
        public ulong ReplyUserdata;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public byte* Name;
        public MpvFormat Format;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventEndFile
    {
        public MpvEndFileReason Reason;
        public int Error;
        public long PlaylistEntryId;
        public int PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventLogMessage
    {
        public byte* Prefix;
        public byte* Level;
        public byte* Text;
        public int LogLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvRenderParam
    {
        public MpvRenderParamType Type;
        public void* Data;
    }
}
