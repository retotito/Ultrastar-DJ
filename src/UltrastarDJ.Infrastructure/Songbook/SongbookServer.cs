using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Infrastructure.Songbook;

/// <summary>
/// In-process Kestrel host for the guest songbook: one HTML page plus a tiny JSON API. Optional 4-digit PIN,
/// checked on every API call (<c>?pin=</c> or <c>X-Pin</c> header). Start/stop many times per process.
/// </summary>
public sealed class SongbookServer : IAsyncDisposable
{
    private const int MaxResults = 200;
    private static readonly string Page = LoadPage();

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
        app.MapGet("/api/songs", (HttpRequest req, string? q) =>
            PinOk(req) ? Results.Json(_backend.Search(q ?? "", MaxResults)) : Results.Unauthorized());
        app.MapGet("/api/state", (HttpRequest req) => PinOk(req) ? Results.Json(_backend.State()) : Results.Unauthorized());
        app.MapPost("/api/request", (HttpRequest req, RequestBody body) =>
        {
            if (!PinOk(req))
            {
                return Results.Unauthorized();
            }

            string guest = (body.Guest ?? "").Trim();
            if (string.IsNullOrEmpty(body.SongId) || guest.Length is 0 or > 40)
            {
                return Results.BadRequest();
            }

            return _backend.Request(body.SongId, guest) ? Results.Ok() : Results.NotFound();
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

    private sealed record RequestBody(string? SongId, string? Guest);
}
