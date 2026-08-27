using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Views;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Display;
using StockTvKiosk.Core.Layout;

namespace StockTvKiosk.App.Services;

/// <summary>
/// Besitzt die Kiosk-Fenster und haelt sie an der aktuellen Bildschirmanordnung.
/// </summary>
/// <remarks>
/// Ausgelagert aus <see cref="App"/>, weil die Anordnung nicht mehr nur einmal beim Start
/// gebraucht wird: nach einem Wechsel zwischen "Duplizieren" und "Erweitern" stimmt sie nicht
/// mehr und muss neu gerechnet werden, angestossen von der REST-Schnittstelle.
///
/// Die Fenster werden dabei ueber ihre Rolle wiederverwendet statt geschlossen und neu
/// geoeffnet. Das ist kein Geschmacksurteil: das Bedienfenster ist das MainWindow, und die
/// Anwendung beendet sich, sobald es geschlossen wird - eine Neuanordnung wuerde den Kiosk
/// also mitnehmen. Nebenbei bleibt so die geladene Blazor-Seite samt Eingabezustand stehen.
/// </remarks>
internal sealed class KioskWindowManager(
    KioskOptions options,
    IDisplayModeDetector detector,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<KioskWindowManager>();

    private KioskWindow? _primary;
    private KioskWindow? _secondary;

    /// <summary>Zuletzt angewendeter Fensterplan, oder null vor dem ersten Aufbau.</summary>
    public KioskLayout? CurrentLayout { get; private set; }

    /// <summary>Bildschirme, wie sie beim letzten Aufbau gemeldet wurden.</summary>
    public IReadOnlyList<ScreenInfo> CurrentScreens { get; private set; } = [];

    /// <summary>Anzeigemodus, wie er beim letzten Aufbau erkannt wurde.</summary>
    public DisplayModeInfo CurrentDisplayMode { get; private set; } =
        new() { Mode = DisplayMode.Unknown, Source = "noch nicht ermittelt" };

    /// <summary>
    /// Erkennt Bildschirme und Anzeigemodus, rechnet den Fensterplan und protokolliert ihn -
    /// ohne etwas anzuzeigen (<c>--dry-run</c>). Erzeugt dabei bereits
    /// das Bedienfenster: Avalonia gibt die Bildschirmliste nur ueber ein TopLevel heraus.
    /// </summary>
    public KioskLayout Plan()
    {
        _primary ??= new KioskWindow();

        var screens = new AvaloniaScreenProvider(_primary).GetScreens();
        var displayMode = Detect();
        var layout = KioskLayoutPlanner.Plan(screens, displayMode, options);

        CurrentScreens = screens;
        CurrentDisplayMode = displayMode;
        CurrentLayout = layout;

        LogLayout(layout);
        return layout;
    }

    /// <summary>Baut die Fenster auf und meldet den verwendeten Plan.</summary>
    public KioskLayout Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var layout = Plan();
        Apply(layout);

        // MainWindow bestimmt, wann die Anwendung beendet wird: das Bedienfenster schliessen
        // beendet den Kiosk, das Schliessen des Spiegels allein nicht.
        desktop.MainWindow = _primary;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

        _primary!.FocusWebContent();
        return layout;
    }

    /// <summary>
    /// Erkennt die Bildschirme neu und ordnet die Fenster entsprechend an. Wird von der
    /// REST-Schnittstelle nach dem Umschalten aufgerufen und laeuft deshalb ueber den
    /// UI-Thread - Fensterpositionen duerfen nur dort angefasst werden.
    /// </summary>
    public async Task<KioskLayout> ApplyLayoutAsync()
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var layout = Plan();
            Apply(layout);

            // Der Tastaturfokus gehoert zurueck aufs Bedienfenster, sonst laeuft die
            // Numpad-Eingabe nach dem Umschalten ins Leere.
            _primary!.FocusWebContent();
            return layout;
        });

    /// <summary>Laedt in allen offenen Fenstern die Seite neu.</summary>
    public async Task ReloadAsync()
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _primary?.Reload();
            _secondary?.Reload();
        });

    /// <summary>Schliesst alle Fenster - nur fuer den geordneten Abbruch bei <c>--dry-run</c>.</summary>
    public void CloseAll()
    {
        _secondary?.Close();
        _secondary = null;
        _primary?.Close();
        _primary = null;
    }

    private void Apply(KioskLayout layout)
    {
        var primaryPlan = layout.Windows.LastOrDefault(w => w.Role == WindowRole.Primary);
        var secondaryPlan = layout.Windows.FirstOrDefault(w => w.Role == WindowRole.Secondary);

        // Reihenfolge ist Absicht: der Spiegel zuerst, das Bedienfenster zuletzt - so bleibt
        // der Tastaturfokus am Ende dort, wo per Numpad getippt wird.
        if (secondaryPlan is not null)
            _secondary = Ensure(_secondary, secondaryPlan);
        else if (_secondary is not null)
        {
            _logger.LogInformation(
                "Der Spiegel wird geschlossen - im aktuellen Plan gibt es kein zweites Fenster mehr.");
            _secondary.Close();
            _secondary = null;
        }

        if (primaryPlan is not null)
            _primary = Ensure(_primary, primaryPlan);
    }

    private KioskWindow Ensure(KioskWindow? window, WindowPlan plan)
    {
        var target = window ?? new KioskWindow();

        // Role bleibt null, solange das Fenster nicht eingerichtet ist. Das trifft auch auf das
        // Bedienfenster zu, das in Plan() schon als Bildschirmquelle erzeugt wurde.
        if (target.Role is null)
        {
            target.Configure(plan, options.Display, options.Retry, loggerFactory.CreateLogger($"Kiosk.{plan.Role}"));
            target.Show();
        }
        else
        {
            target.Reposition(plan, options.Display);
        }

        return target;
    }

    private DisplayModeInfo Detect()
    {
        try
        {
            return detector.Detect();
        }
        catch (Exception ex)
        {
            // Ohne Modus laesst sich immer noch anhand der Bildschirmliste planen.
            _logger.LogWarning(ex, "Anzeigemodus konnte nicht ermittelt werden.");
            return new DisplayModeInfo { Mode = DisplayMode.Unknown, Source = "fehlgeschlagen" };
        }
    }

    private void LogLayout(KioskLayout layout)
    {
        _logger.LogInformation("Anzeigemodus: {Mode}", CurrentDisplayMode);

        if (CurrentScreens.Count == 0)
        {
            _logger.LogWarning("Avalonia meldet keine Bildschirme.");
        }
        else
        {
            _logger.LogInformation("Erkannte Bildschirme ({Count}):", CurrentScreens.Count);
            foreach (var screen in CurrentScreens)
                _logger.LogInformation("  {Screen}", screen);

            if (CurrentScreens.Count < 2)
                _logger.LogInformation("Kein sekundaerer Monitor gefunden.");
        }

        foreach (string warning in layout.Warnings)
            _logger.LogWarning("{Warning}", warning);

        _logger.LogInformation("Fensterplan: {Count} Fenster.", layout.Windows.Count);
        foreach (var plan in layout.Windows)
            _logger.LogInformation("  {Plan}", plan);
    }
}
