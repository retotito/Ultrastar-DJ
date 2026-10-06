using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using UltrastarDJ.Core.Songbook;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Infrastructure.Songbook;

/// <summary>
/// In-process Kestrel host for the guest songbook: one HTML page plus a tiny JSON API. Optional 4-digit PIN,
/// checked on every API call (<c>?pin=</c> or <c>X-Pin</c> header). Start/stop many times per process.
/// Phones download the catalog once (<c>/api/library</c>, gzip, cacheable per library version) and search and
/// filter it themselves — no request per keystroke while the party runs.
/// </summary>
public sealed class SongbookServer : IAsyncDisposable
{
    private static readonly string Page = LoadPage();
    private readonly Lock _catalogLock = new();
    private (int Version, byte[] Gzip)? _catalog;

    private readonly ISongbookBackend _backend;
    private readonly ILogger<SongbookServer> _log;
    private WebApplication? _app;
    private string? _pin;

    public SongbookServer(ISongbookBackend backend, ILogger<SongbookServer> log)
    {
        _backend = backend;
        _log = log;
    }

    public bool IsRunning => _app is not null;
    public int Port { get; private set; }

    /// <summary>PIN guests must enter; null = open party. Can change while running.</summary>
    public string? Pin
    {
        get => _pin;
        set => _pin = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        if (_app is not null)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

        WebApplication app = builder.Build();
        app.MapGet("/", () => Results.Content(Page, "text/html; charset=utf-8"));
        app.MapGet("/api/verify-pin", (HttpRequest req) => PinOk(req) ? Results.Ok() : Results.Unauthorized());
        app.MapGet("/api/library", (HttpRequest req, HttpResponse res) =>
        {
            if (!PinOk(req))
            {
                return Results.Unauthorized();
            }

            // The URL carries the version (?v=), so the phone's cache may keep it forever: a new library = a new URL.
            res.Headers.ContentEncoding = "gzip";
            res.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Bytes(CatalogGzip(), "application/json");
        });
        app.MapGet("/api/youtube", async (HttpRequest req, string? id, CancellationToken ct) =>
        {
            if (!PinOk(req))
            {
                return Results.Unauthorized();
            }

            return string.IsNullOrEmpty(id) || await _backend.YouTubeIdAsync(id, ct).ConfigureAwait(false) is not { } yt
                ? Results.NotFound()
                : Results.Json(new { id = yt });
        });
        app.MapGet("/api/state", async (HttpRequest req, string? client) =>
            PinOk(req) ? Results.Json(await _backend.StateAsync(Client(client)).ConfigureAwait(false)) : Results.Unauthorized());
        app.MapPost("/api/request", async (HttpRequest req, RequestBody body) =>
        {
            if (!PinOk(req))
            {
                return Results.Unauthorized();
            }

            string guest = (body.Guest ?? "").Trim();
            if (string.IsNullOrEmpty(body.SongId) || guest.Length is 0 or > 40 || Client(body.Client) is not { } client)
            {
                return Results.BadRequest();
            }

            SongbookRequestResult r = await _backend.RequestAsync(body.SongId, guest, client).ConfigureAwait(false);
            return r.Ok ? Results.Ok() : r.UnknownSong ? Results.NotFound() : Results.Json(new { message = r.Message }, statusCode: StatusCodes.Status409Conflict);
        });
        app.MapPost("/api/cancel", async (HttpRequest req, CancelBody body) =>
        {
            if (!PinOk(req))
            {
                return Results.Unauthorized();
            }

            return string.IsNullOrEmpty(body.Id) || Client(body.Client) is not { } client
                ? Results.BadRequest()
                : await _backend.CancelAsync(body.Id, client).ConfigureAwait(false) ? Results.Ok() : Results.Conflict();
        });

        await app.StartAsync(ct).ConfigureAwait(false);
        _app = app;
        Port = port;
        _log.LogInformation("Songbook listening on port {Port} (PIN {Pin})", port, _pin is null ? "off" : "on");
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        WebApplication app = _app;
        _app = null;
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        _log.LogInformation("Songbook stopped");
    }

    /// <summary>The catalog as compact JSON, gzipped — built once per library version (30 000 songs ≈ 0.5 MB).</summary>
    private byte[] CatalogGzip()
    {
        int version = _backend.LibraryVersion;
        lock (_catalogLock)
        {
            if (_catalog is { } c && c.Version == version)
            {
                return c.Gzip;
            }
        }

        byte[] gzip = Compress(_backend.Catalog(), version);
        lock (_catalogLock)
        {
            _catalog = (version, gzip);
        }

        _log.LogInformation("Songbook catalog v{Version}: {Kb} KB compressed", version, gzip.Length / 1024);
        return gzip;
    }

    // Songs as arrays, not objects: [id, title, artist, year, [languages], [genres], stars, usdb 0/1, youtubeId|null].
    private static byte[] Compress(SongbookCatalog catalog, int version)
    {
        using MemoryStream buffer = new();
        using (GZipStream gz = new(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (Utf8JsonWriter w = new(gz))
        {
            w.WriteStartObject();
            w.WriteNumber("v", version);
            w.WriteStartArray("languages");
            foreach (string l in catalog.Languages)
            {
                w.WriteStringValue(l);
            }

            w.WriteEndArray();
            w.WriteStartArray("genres");
            foreach (string g in catalog.Genres)
            {
                w.WriteStringValue(g);
            }

            w.WriteEndArray();
            w.WriteStartArray("songs");
            foreach (SongbookEntry e in catalog.Songs)
            {
                w.WriteStartArray();
                w.WriteStringValue(e.Id);
                w.WriteStringValue(e.Title);
                w.WriteStringValue(e.Artist);
                w.WriteNumberValue(e.Year);
                WriteInts(w, e.Languages);
                WriteInts(w, e.Genres);
                w.WriteNumberValue(e.Stars);
                w.WriteNumberValue(e.Usdb ? 1 : 0);
                if (e.YouTubeId is { } yt)
                {
                    w.WriteStringValue(yt);
                }
                else
                {
                    w.WriteNullValue();
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteInts(Utf8JsonWriter w, int[] values)
    {
        w.WriteStartArray();
        foreach (int v in values)
        {
            w.WriteNumberValue(v);
        }

        w.WriteEndArray();
    }

    private bool PinOk(HttpRequest req)
    {
        if (_pin is null)
        {
            return true;
        }

        string? given = req.Query["pin"].FirstOrDefault() ?? req.Headers["X-Pin"].FirstOrDefault();
        return given is not null && string.Equals(given.Trim(), _pin, StringComparison.Ordinal);
    }

    private static string LoadPage()
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("songbook.html");
        if (s is null)
        {
            return "<html><body>songbook.html missing from build</body></html>";
        }

        using StreamReader r = new(s);
        return r.ReadToEnd();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    // The phone's random id: letters and digits, bounded — it is only a key, never shown.
    private static string? Client(string? id) => id is { Length: >= 8 and <= 64 } && id.All(char.IsAsciiLetterOrDigit) ? id : null;

    private sealed record RequestBody(string? SongId, string? Guest, string? Client);
    private sealed record CancelBody(string? Id, string? Client);
}
