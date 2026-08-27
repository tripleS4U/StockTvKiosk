using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Hosting;
using StockTvKiosk.App.Services;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.App;

public partial class App : Application
{
    private BlazorHostProcess? _blazorHost;
    private KioskWindowManager? _windows;
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
        if (!Program.DryRun)
        {
            _blazorHost = new BlazorHostProcess(
                options.BlazorHost, Program.LoggerFactory.CreateLogger<BlazorHostProcess>());
            _blazorHost.StartIfEnabled(ReadinessProbe(options.Urls.PrimaryUrl));
        }

        WarnAboutWayland();

        var detector = new MonitorDetector(Program.LoggerFactory.CreateLogger<MonitorDetector>());
        _windows = new KioskWindowManager(options, detector, Program.LoggerFactory);

        if (Program.DryRun)
        {
            _windows.Plan();
            _logger.LogInformation("DryRun aktiv - es wird kein Fenster geoeffnet, der Kiosk beendet sich.");

            // Beides ist noetig, sonst faellt der Start mit "Cannot perform requested operation
            // because the Dispatcher shut down" um: OnExplicitShutdown verhindert, dass das
            // Schliessen des Probe-Fensters sofort herunterfaehrt, und der Post sorgt dafuer,
            // dass der eigentliche Shutdown erst aus der laufenden Hauptschleife heraus passiert.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _windows.CloseAll();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        _windows.Start(desktop);

        desktop.ShutdownRequested += OnShutdownRequested;
        desktop.Exit += OnExit;
    }

    /// <summary>
    /// Wurzeladresse der Blazor-Anwendung, gegen die auf Bereitschaft geprueft wird - also nur
    /// Schema, Host und Port der Primaer-URL. Der Seitenpfad taugt dafuer nicht: /turnier koennte
    /// 404 liefern, obwohl der Server laeuft.
    /// </summary>
    private Uri? ReadinessProbe(string primaryUrl)
    {
        if (Uri.TryCreate(primaryUrl, UriKind.Absolute, out var uri))
            return new Uri(uri.GetLeftPart(UriPartial.Authority));

        _logger.LogWarning("PrimaryUrl ist keine gueltige Adresse: {Url}", primaryUrl);
        return null;
    }

    private void WarnAboutWayland()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return;
        }

        _logger.LogWarning(
            "Wayland-Sitzung erkannt: der Compositor kann die Fensterposition ueberstimmen. " +
            "Fuer verlaessliche Zuordnung auf zwei Bildschirme X11 verwenden " +
            "(Raspberry Pi OS: raspi-config -> Advanced Options -> Wayland -> X11).");
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        => _logger.LogInformation("Kiosk wird heruntergefahren.");

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _blazorHost?.Dispose();
        _blazorHost = null;
    }
}
