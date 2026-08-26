using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Logging;
using StockTvKiosk.Core.Configuration;
using static StockTvKiosk.App.AppConfig;

namespace StockTvKiosk.App;

internal static class Program
{
    /// <summary>
    /// Kommandozeilen-Kurzformen. Jeder Wert aus appsettings.json laesst sich zusaetzlich
    /// direkt ueber seinen Pfad ueberschreiben, z. B. --Kiosk:Retry:IntervalSeconds=10.
    /// </summary>
    private static readonly Dictionary<string, string> SwitchMappings = new()
    {
        ["--primary-url"] = "Kiosk:Urls:PrimaryUrl",
        ["--secondary-url"] = "Kiosk:Urls:SecondaryUrl",
        ["--demo"] = "Kiosk:Urls:Demo",
        ["--dual"] = "Kiosk:Display:Dual",
        ["--second-screen"] = "Kiosk:Display:UseSecondScreen",
        ["--main-display"] = "Kiosk:Display:MainDisplayIndex",
        ["--mirror-display"] = "Kiosk:Display:MirrorDisplayIndex",
        ["--window-mode"] = "Kiosk:Display:WindowMode",
        ["--dry-run"] = "Kiosk:DryRun",
        ["--manage-host"] = "Kiosk:BlazorHost:Enabled",
        ["--host-exe"] = "Kiosk:BlazorHost:ExecutablePath"
    };

    public static IConfiguration Configuration { get; private set; } = null!;
    public static ILoggerFactory LoggerFactory { get; private set; } = null!;
    public static KioskOptions Options { get; private set; } = null!;

    /// <summary>
    /// Nur erkennen und protokollieren, kein Fenster oeffnen (--dry-run). Bewusst NICHT Teil
    /// von <see cref="KioskOptions"/>: der Schalter gilt fuer genau einen Programmlauf und
    /// haette in der persistierten Einstellungsdatei nichts verloren.
    /// </summary>
    public static bool DryRun { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Damit Log-Ausgaben sichtbar sind, wenn der Kiosk aus einer Konsole gestartet wurde.
        // Beim Doppelklick passiert nichts - es oeffnet sich also kein stoerendes Fenster.
        AttachParentConsole();

        Configuration = BuildConfiguration(args);
        Options = AppConfig.Instance.KioskOptions;
        DryRun = Configuration.GetValue<bool>("Kiosk:DryRun");
        LoggerFactory = BuildLoggerFactory(Configuration);

        var logger = LoggerFactory.CreateLogger("StockTvKiosk");
        logger.LogInformation("StockTV-Kiosk startet auf {Os} ({Arch}).",
            RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Der Kiosk wurde durch einen unbehandelten Fehler beendet.");
            return 1;
        }
        finally
        {
            LoggerFactory.Dispose();
        }
    }

    // Wird auch vom Avalonia-Designer und von den Previewern aufgerufen.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .LogToTrace();

    private static IConfiguration BuildConfiguration(string[] args)
    {
        string baseDirectory = AppContext.BaseDirectory;

        return new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("STOCKTV_")
            .AddCommandLine(args, SwitchMappings)
            .Build();
    }

    private static ILoggerFactory BuildLoggerFactory(IConfiguration configuration)
        => Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });

            var fileOptions = configuration.GetSection("Logging:File").Get<FileLoggerOptions>();
            if (fileOptions is { Enabled: true })
                builder.AddProvider(new FileLoggerProvider(fileOptions, AppContext.BaseDirectory));
        });

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private static void AttachParentConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // Nur eingreifen, wenn ueberhaupt kein stdout existiert. Ist es bereits gesetzt -
            // weil der Kiosk aus einem Skript heraus mit umgeleiteter Ausgabe laeuft -, wuerde
            // AttachConsole die Umleitung kaputtmachen und die Logzeilen verschwinden lassen.
            if (GetStdHandle(StdOutputHandle) != IntPtr.Zero)
                return;

            AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException) { /* ohne Konsole laeuft der Kiosk trotzdem */ }
        catch (EntryPointNotFoundException) { /* dito */ }
    }
}
