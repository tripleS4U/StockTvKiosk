using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Hosting;
using StockTvKiosk.App.Services;
using StockTvKiosk.App.Views;
using StockTvKiosk.Core.Display;
using StockTvKiosk.Core.Layout;

namespace StockTvKiosk.App;

public partial class App : Application
{
    private readonly List<KioskWindow> _windows = [];
    private BlazorHostProcess? _blazorHost;
    private ILogger _logger = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _logger = Program.LoggerFactory.CreateLogger<App>();

            try
            {
                StartKiosk(desktop);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Der Kiosk konnte nicht gestartet werden.");
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartKiosk(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var options = Program.Options;

        // Optional: StockTV selbst starten. Bewusst nicht blockierend - die Fenster erscheinen
        // sofort mit der Statusseite und laden nach, sobald der Server antwortet.
        if (!options.DryRun)
        {
            _blazorHost = new BlazorHostProcess(
                options.BlazorHost, Program.LoggerFactory.CreateLogger<BlazorHostProcess>());
            _blazorHost.StartIfEnabled();
        }

        // Avalonia liefert die Bildschirmliste nur ueber ein TopLevel. Das erste Fenster wird
        // deshalb erzeugt (noch nicht angezeigt) und dient zugleich als Bildschirmquelle.
        var probeWindow = new KioskWindow();
        var screenProvider = new AvaloniaScreenProvider(probeWindow);
        var screens = screenProvider.GetScreens();

        var displayMode = new MonitorDetector(
            Program.LoggerFactory.CreateLogger<MonitorDetector>()).Detect();

        LogEnvironment(screens, displayMode);

        var layout = KioskLayoutPlanner.Plan(screens, displayMode, options);
        foreach (string warning in layout.Warnings)
            _logger.LogWarning("{Warning}", warning);

        _logger.LogInformation("Fensterplan: {Count} Fenster.", layout.Windows.Count);
        foreach (var plan in layout.Windows)
            _logger.LogInformation("  {Plan}", plan);

        if (options.DryRun)
        {
            _logger.LogInformation("DryRun aktiv - es wird kein Fenster geoeffnet, der Kiosk beendet sich.");

            // Beides ist noetig, sonst faellt der Start mit "Cannot perform requested operation
            // because the Dispatcher shut down" um: OnExplicitShutdown verhindert, dass das
            // Schliessen des Probe-Fensters sofort herunterfaehrt, und der Post sorgt dafuer,
            // dass der eigentliche Shutdown erst aus der laufenden Hauptschleife heraus passiert.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            probeWindow.Close();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        // Reihenfolge ist Absicht: das Bedienfenster steht im Plan zuletzt und wird deshalb
        // zuletzt aktiviert - dort landet der Tastaturfokus fuer die Numpad-Eingabe.
        for (int i = 0; i < layout.Windows.Count; i++)
        {
            var plan = layout.Windows[i];
            var window = i == 0 ? probeWindow : new KioskWindow();

            window.Configure(
                plan, options.Display, options.Retry,
                Program.LoggerFactory.CreateLogger($"Kiosk.{plan.Role}"));

            _windows.Add(window);
            window.Show();
        }

        // MainWindow bestimmt, wann die Anwendung beendet wird: das Bedienfenster schliessen
        // beendet den Kiosk, das Schliessen des Spiegels allein nicht.
        desktop.MainWindow = _windows[^1];
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.ShutdownRequested += OnShutdownRequested;
        desktop.Exit += OnExit;

        _windows[^1].FocusWebContent();
    }

    private void LogEnvironment(IReadOnlyList<ScreenInfo> screens, DisplayModeInfo displayMode)
    {
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            _logger.LogWarning(
                "Wayland-Sitzung erkannt: der Compositor kann die Fensterposition ueberstimmen. " +
                "Fuer verlaessliche Zuordnung auf zwei Bildschirme X11 verwenden " +
                "(Raspberry Pi OS: raspi-config -> Advanced Options -> Wayland -> X11).");
        }

        _logger.LogInformation("Anzeigemodus: {Mode}", displayMode);

        if (screens.Count == 0)
        {
            _logger.LogWarning("Avalonia meldet keine Bildschirme.");
            return;
        }

        _logger.LogInformation("Erkannte Bildschirme ({Count}):", screens.Count);
        foreach (var screen in screens)
            _logger.LogInformation("  {Screen}", screen);

        if (screens.Count < 2)
            _logger.LogInformation("Kein sekundaerer Monitor gefunden.");
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        => _logger.LogInformation("Kiosk wird heruntergefahren.");

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _blazorHost?.Dispose();
        _blazorHost = null;
    }
}
