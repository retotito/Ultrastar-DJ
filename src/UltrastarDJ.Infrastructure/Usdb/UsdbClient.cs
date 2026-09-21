using System.Net;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Usdb;

/// <summary>
/// HTTP client for usdb.animux.de. Form posts against the PHP site; the session cookie lives in the handler's
/// cookie container. Paging: 100 rows per request until a short page arrives.
/// </summary>
public sealed class UsdbClient : IUsdbClient, IDisposable
{
    private const int PageSize = 100;
    private static readonly Uri ListUri = new($"{UsdbHtml.BaseUrl}/index.php?link=list");

    private readonly HttpClient _http;
    private readonly ILogger<UsdbClient> _log;

    public UsdbClient(ILogger<UsdbClient> log)
        : this(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AutomaticDecompression = DecompressionMethods.All }, log)
    {
    }

    public UsdbClient(HttpMessageHandler handler, ILogger<UsdbClient> log)
    {
        _log = log;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("UltrastarDJ/1.0");
    }

    public bool IsLoggedIn { get; private set; }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        string body = await PostFormAsync(new Uri($"{UsdbHtml.BaseUrl}/"), new Dictionary<string, string>
        {
            ["user"] = username,
            ["pass"] = password,
            ["login"] = "Login",
        }, ct).ConfigureAwait(false);

        IsLoggedIn = !UsdbHtml.IsLoginRejected(body);
        _log.LogInformation("USDB login {Result}", IsLoggedIn ? "ok" : "rejected");
        return IsLoggedIn;
    }

    public void Logout() => IsLoggedIn = false;

    public async IAsyncEnumerable<IReadOnlyList<UsdbCatalogEntry>> FetchAllPagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int start = 0; ; start += PageSize)
        {
            IReadOnlyList<UsdbCatalogEntry> page = await FetchPageAsync("id", "asc", start, ct).ConfigureAwait(false);
            yield return page;
            if (page.Count < PageSize)
            {
                break;
            }
        }
    }

    public async Task<IReadOnlyList<UsdbCatalogEntry>> FetchUpdatedAsync(long lastMtime, IReadOnlySet<int> idsAtLastMtime, IProgress<UsdbSyncProgress>? progress = null, CancellationToken ct = default)
    {
        List<UsdbCatalogEntry> updated = [];
        for (int start = 0; ; start += PageSize)
        {
            IReadOnlyList<UsdbCatalogEntry> page = await FetchPageAsync("lastchange", "desc", start, ct).ConfigureAwait(false);
            bool reachedWatermark = false;
            foreach (UsdbCatalogEntry e in page)
            {
                if (e.UsdbMtime > lastMtime || (e.UsdbMtime == lastMtime && !idsAtLastMtime.Contains(e.SongId)))
                {
                    updated.Add(e);
                }
                else
                {
                    reachedWatermark = true;
                }
            }

            progress?.Report(new UsdbSyncProgress(updated.Count));
            if (reachedWatermark || page.Count < PageSize)
            {
                break;
            }
        }

        return updated;
    }

    public async Task<string> GetSongTxtAsync(int songId, CancellationToken ct = default)
    {
        string body = await PostFormAsync(new Uri($"{UsdbHtml.BaseUrl}/index.php?link=gettxt&id={songId}"), new Dictionary<string, string> { ["wd"] = "1" }, ct).ConfigureAwait(false);
        return UsdbHtml.ExtractSongTxt(body) ?? throw new UsdbException($"USDB returned no song text for #{songId} (deleted, or not logged in)");
    }

    private async Task<IReadOnlyList<UsdbCatalogEntry>> FetchPageAsync(string order, string direction, int start, CancellationToken ct)
    {
        string body = await PostFormAsync(ListUri, new Dictionary<string, string>
        {
            ["order"] = order,
            ["ud"] = direction,
            ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["start"] = start.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["details"] = "1",
        }, ct).ConfigureAwait(false);
        return UsdbHtml.ParseSongList(body);
    }

    private async Task<string> PostFormAsync(Uri uri, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using FormUrlEncodedContent content = new(form);
            using HttpResponseMessage response = await _http.PostAsync(uri, content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UsdbException($"USDB request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UsdbException("USDB request timed out", ex);
        }
    }

    public void Dispose() => _http.Dispose();
}
