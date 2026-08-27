using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Services;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Layout;

namespace StockTvKiosk.App.Views;

/// <summary>
/// Randloses Vollbildfenster mit genau einer Aufgabe: eine URL der StockTV-Blazor-App
/// auf einem bestimmten Bildschirm anzeigen.
/// </summary>
public partial class KioskWindow : Window
{
    private WebViewConnectionController? _connection;
    private ILogger? _logger;
    private WindowPlan? _plan;

    public KioskWindow()
    {
        InitializeComponent();
    }

    /// <summary>Rolle des Fensters, sobald es konfiguriert wurde.</summary>
    public WindowRole? Role => _plan?.Role;

    /// <summary>
    /// Setzt Position, Groesse und Zieladresse. Muss vor <see cref="Window.Show()"/> aufgerufen
    /// werden, damit das Fenster direkt auf dem richtigen Bildschirm erscheint und nicht
    /// sichtbar umherspringt.
    /// </summary>
    public void Configure(WindowPlan plan, DisplayOptions display, RetryOptions retry, ILogger logger)
    {
        _plan = plan;
        _logger = logger;

        Title = plan.Role == WindowRole.Primary ? "StockTV" : "StockTV Anzeige";
        ApplyBounds(plan, display);

        if (display.HideCursor)
            Cursor = new Cursor(StandardCursorType.None);

        if (display.AllowExitShortcut)
            KeyDown += OnKeyDown;

        _connection = new WebViewConnectionController(WebView, plan, retry, logger);
    }

    /// <summary>
    /// Legt das Fenster auf einen anderen Bildschirm bzw. auf eine neue Groesse - nach einem
    /// Wechsel zwischen "Duplizieren" und "Erweitern" stimmt die alte Anordnung nicht mehr.
    /// Anders als <see cref="Configure"/> darf das am offenen Fenster passieren.
    /// </summary>
    public void Reposition(WindowPlan plan, DisplayOptions display)
    {
        _plan = plan;
        ApplyBounds(plan, display);

        _logger?.LogInformation("{Role}: neu angeordnet auf Bildschirm [{Screen}] bei {Bounds}.",
            plan.Role, plan.ScreenIndex, plan.Bounds);
    }

    /// <summary>Laedt die Seite neu - inklusive Erreichbarkeitspruefung und Statusseite.</summary>
    public void Reload() => _connection?.Reload();

    private void ApplyBounds(WindowPlan plan, DisplayOptions display)
    {
        // Position ist in physischen Pixeln, Width/Height in geraeteunabhaengigen Einheiten.
        // Ohne diese Umrechnung waere das Fenster auf einem skalierten Bildschirm zu gross.
        double scaling = plan.Scaling <= 0 ? 1.0 : plan.Scaling;

        // Ein Vollbildfenster nimmt keine neue Position an - es haengt an seinem Bildschirm.
        // Fuer die Neuanordnung muss es deshalb erst wieder ein normales Fenster werden.
        if (WindowState == WindowState.FullScreen)
            WindowState = WindowState.Normal;

        Position = new PixelPoint(plan.Bounds.X, plan.Bounds.Y);
        Width = plan.Bounds.Width / scaling;
        Height = plan.Bounds.Height / scaling;

        if (display.WindowMode == WindowMode.FullScreen)
        {
            // Der Compositor entscheidet dann selbst ueber den Zielbildschirm - meist den,
            // auf dem das Fenster gerade liegt. Deshalb wird die Position vorher gesetzt.
            WindowState = WindowState.FullScreen;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_plan is not null)
        {
            _logger?.LogInformation("{Role}: Fenster geoeffnet auf Bildschirm [{Screen}] bei {Bounds}.",
                _plan.Role, _plan.ScreenIndex, _plan.Bounds);
        }

        _connection?.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Notausstieg fuer Test und Wartung. Im Feldbetrieb ueber AllowExitShortcut abschaltbar.
        if (e.Key == Key.Escape && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _logger?.LogInformation("Shift+Esc erkannt - Kiosk wird beendet.");
            e.Handled = true;
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        var connection = _connection;
        _connection = null;
        if (connection is not null)
            _ = connection.DisposeAsync().AsTask();
    }

    /// <summary>Holt den Tastaturfokus in den WebView - Voraussetzung fuer die Numpad-Eingabe.</summary>
    public void FocusWebContent()
    {
        Activate();
        WebView.Focus();
    }
}
