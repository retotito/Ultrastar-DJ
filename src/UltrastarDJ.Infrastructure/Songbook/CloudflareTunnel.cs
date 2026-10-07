using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.Infrastructure.Songbook;

/// <summary>
/// The songbook's public link: a Cloudflare Quick Tunnel (<c>cloudflared tunnel --url http://localhost:PORT</c>), free and
/// without an account. cloudflared prints a fresh <c>https://….trycloudflare.com</c> address and forwards it to the
/// local songbook, so guests on mobile data reach it too. One process per start; <see cref="Ended"/> when it dies.
/// Chosen over bore (the prototype's): a plain https address on the usual port, Cloudflare's network, reconnects itself.
/// </summary>
public sealed partial class CloudflareTunnel(string executable, ILogger log) : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(40);
    private Process? _process;

    /// <summary>The process exited (crash, network gone for good). Raised on a worker thread.</summary>
    public event Action? Ended;

    public string? Url { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>The address in a cloudflared log line, or null.</summary>
    public static string? FindUrl(string? line) => line is null ? null : UrlPattern().Match(line) is { Success: true } m ? m.Value : null;

    // Not api.trycloudflare.com: cloudflared's own endpoint, which shows up in its error lines.
    [GeneratedRegex(@"https://(?!api\.)[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>Starts cloudflared and returns its public address once announced.</summary>
    /// <exception cref="InvalidOperationException">No address within the timeout, or cloudflared exited.</exception>
    public async Task<string> StartAsync(int port, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);
        TaskCompletionSource<string> announced = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ProcessStartInfo psi = new(executable, ["tunnel", "--no-autoupdate", "--url", $"http://localhost:{port}"])
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process p = new() { StartInfo = psi, EnableRaisingEvents = true };
        void OnLine(object? _, DataReceivedEventArgs e)
        {
            if (FindUrl(e.Data) is { } url)
            {
                announced.TrySetResult(url);
            }
            else if (e.Data is { } line && line.Contains(" ERR ", StringComparison.Ordinal))
            {
                log.LogWarning("cloudflared: {Line}", line);
            }
        }

        p.ErrorDataReceived += OnLine;
        p.OutputDataReceived += OnLine;
        p.Exited += (_, _) =>
        {
            announced.TrySetException(new InvalidOperationException("cloudflared stopped before the link was ready."));
            if (ReferenceEquals(_process, p))
            {
                log.LogWarning("Public link: cloudflared exited (code {Code})", p.ExitCode);
                Url = null;
                Ended?.Invoke();
            }
        };

        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        _process = p;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StartTimeout);
        try
        {
            Url = await announced.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Cloudflare did not hand out a link in time — is this Mac online?");
        }

        log.LogInformation("Public link: {Url}", Url);
        return Url;
    }

    public async Task StopAsync()
    {
        Process? p = _process;
        _process = null;
        Url = null;
        if (p is null)
        {
            return;
        }

        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            log.LogWarning(ex, "Public link: cloudflared did not stop cleanly");
        }
        finally
        {
            p.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
