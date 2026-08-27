using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Layout;

namespace StockTvKiosk.App.Services;

/// <summary>
/// Haelt ein einzelnes Kiosk-Fenster an seiner URL: prueft die Erreichbarkeit, zeigt bei
/// Ausfall die lokale Statusseite und laedt automatisch neu, sobald die Blazor-App wieder
/// antwortet. Ohne diese Schleife bliebe im unbeaufsichtigten Betrieb die Fehlerseite des
/// Browsers stehen, bis jemand vor Ort eingreift.
/// </summary>
internal sealed class WebViewConnectionController : IAsyncDisposable
{
    private static readonly Uri ErrorPageBaseUri = new("about:blank");

    private readonly NativeWebView _webView;
    private readonly WindowPlan _plan;
    private readonly RetryOptions _retry;
    private readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly Uri _target;

    private CancellationTokenSource _cts = new();
    private Task _loop = Task.CompletedTask;

    /// <summary>
    /// True, solange die lokale Statusseite angezeigt wird. Fuer sie feuert
    /// <c>NavigationCompleted</c> genauso wie fuer die echte Seite - ohne dieses Flag wuerde
    /// die Fehlerbehandlung sich selbst erneut ausloesen.
    /// </summary>
    private bool _showingStatusPage;

    private int _attempt;
    private bool _connected;

    public WebViewConnectionController(
        NativeWebView webView, WindowPlan plan, RetryOptions retry, ILogger logger)
    {
        _webView = webView;
        _plan = plan;
        _retry = retry;
        _logger = logger;
        _target = new Uri(plan.Url, UriKind.Absolute);

        // Kein Proxy: das Ziel ist ein lokaler Dienst. Ein im System hinterlegter
        // Unternehmensproxy wuerde die Erreichbarkeitspruefung sonst ins Leere laufen lassen.
        var handler = new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, retry.ProbeTimeoutSeconds))
        };

        _webView.NavigationCompleted += OnNavigationCompleted;
    }

    public void Start() => Restart();

    /// <summary>
    /// Laedt die Seite neu. Geht bewusst durch dieselbe Schleife wie der Start: haengt die
    /// Blazor-App gerade, landet das Fenster auf der Statusseite und kommt von allein zurueck,
    /// statt eine Browser-Fehlerseite stehen zu lassen.
    /// </summary>
    public void Reload()
    {
        _logger.LogInformation("{Role}: Neuladen angefordert.", _plan.Role);
        _connected = false;
        Restart();
    }

    private void Restart()
    {
        var previous = _cts;
        _cts = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();

        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _attempt = 0;
        var interval = TimeSpan.FromSeconds(Math.Max(1, _retry.IntervalSeconds));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _attempt++;

                if (_attempt == 1 && _retry.ShowErrorPage)
                    await ShowStatusPageAsync(ErrorPage.Connecting(_plan, _attempt, _retry.IntervalSeconds));

                string? failure = await ProbeAsync(cancellationToken);
                if (failure is null)
                {
                    _logger.LogInformation(
                        "{Role}: {Url} ist erreichbar - Seite wird geladen (Versuch {Attempt}).",
                        _plan.Role, _plan.Url, _attempt);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _showingStatusPage = false;
                        _webView.Navigate(_target);
                    });

                    return; // Erneut gestartet wird die Schleife nur bei einem Navigationsfehler.
                }

                _logger.LogWarning(
                    "{Role}: {Url} nicht erreichbar (Versuch {Attempt}): {Reason}",
                    _plan.Role, _plan.Url, _attempt, failure);

                if (_retry.ShowErrorPage)
                    await ShowStatusPageAsync(ErrorPage.Failed(_plan, _attempt, _retry.IntervalSeconds, failure));

                if (!_retry.Enabled)
                {
                    _logger.LogWarning(
                        "{Role}: Auto-Retry ist deaktiviert - die Statusseite bleibt stehen.", _plan.Role);
                    return;
                }

                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Fenster wird geschlossen oder die Schleife neu gestartet.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Role}: Verbindungsueberwachung abgebrochen.", _plan.Role);
        }
    }

    /// <summary>Gibt null zurueck, wenn der Server geantwortet hat, sonst den Grund.</summary>
    private async Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Jede HTTP-Antwort zaehlt als erreichbar - auch 404 oder 500. Der Server laeuft dann,
            // und seine echte Antwort ist fuer die Fehlersuche aussagekraeftiger als unsere Seite.
            using var response = await _http.GetAsync(
                _target, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            _logger.LogDebug("{Role}: Erreichbarkeitspruefung auf {Url} lieferte HTTP {Status}.",
                _plan.Role, _plan.Url, (int)response.StatusCode);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return $"Keine Antwort innerhalb von {_retry.ProbeTimeoutSeconds} s.";
        }
        catch (HttpRequestException ex)
        {
            return ex.InnerException?.Message ?? ex.Message;
        }
    }

    private async Task ShowStatusPageAsync(string html)
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _showingStatusPage = true;
            _webView.NavigateToString(html, ErrorPageBaseUri);
        });

    private void OnNavigationCompleted(object? sender, EventArgs args)
    {
        // Die Statusseite navigiert selbst - ihr Ergebnis darf die Schleife nicht neu ausloesen.
        if (_showingStatusPage)
            return;

        bool success = TryReadSuccess(args) ?? true;
        if (success)
        {
            if (!_connected)
                _logger.LogInformation("{Role}: {Url} geladen.", _plan.Role, _plan.Url);

            _connected = true;
            return;
        }

        _connected = false;
        _logger.LogWarning("{Role}: Laden von {Url} fehlgeschlagen - Verbindungsueberwachung startet neu.",
            _plan.Role, _plan.Url);

        if (_retry.Enabled)
            Restart();
    }

    /// <summary>
    /// Liest <c>IsSuccess</c> aus den Ereignisdaten. Der Typ der EventArgs ist zwischen
    /// WebView-Versionen nicht stabil, deshalb reflektiv und mit Fallback.
    /// </summary>
    private static bool? TryReadSuccess(EventArgs args)
        => args.GetType().GetProperty("IsSuccess")?.GetValue(args) as bool?;

    public async ValueTask DisposeAsync()
    {
        _webView.NavigationCompleted -= OnNavigationCompleted;

        await _cts.CancelAsync();
        try { await _loop; } catch { /* beim Herunterfahren egal */ }

        _cts.Dispose();
        _http.Dispose();
    }
}
