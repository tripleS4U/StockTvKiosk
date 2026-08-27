using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Logging;
using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.App;

internal static class Program
{
    /// <summary>
    /// Die gesamte Konfiguration kommt aus <see cref="AppConfig"/> (Datei
    /// <c>_config/AppConfig.json</c> neben der Anwendung). Es gibt bewusst keine zweite
    /// Quelle mehr: appsettings.json, Umgebungsvariablen und Kommandozeilen-Overrides
    /// wuerden sonst still gegeneinander arbeiten.
    /// </summary>
    public static KioskOptions Options { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    /// <summary>
    /// Nur erkennen und protokollieren, kein Fenster oeffnen (<c>--dry-run</c>).
    /// Der einzige Schalter, den es noch auf der Kommandozeile gibt - und bewusst NICHT in
    /// AppConfig: er gilt fuer genau einen Programmlauf. In der Datei abgelegt wuerde ein
    /// vergessenes true dazu fuehren, dass der Kiosk gar nichts mehr anzeigt.
    /// </summary>
    public static bool DryRun { get; private set; }

    private const string DryRunSwitch = "--dry-run";

    [STAThread]
    public static int Main(string[] args)
    {
        // Damit Log-Ausgaben sichtbar sind, wenn der Kiosk aus einer Konsole gestartet wurde.
        // Beim Doppelklick passiert nichts - es oeffnet sich also kein stoerendes Fenster.
        AttachParentConsole();

        Options = AppConfig.Instance.KioskOptions;
        DryRun = ReadSwitch(args, DryRunSwitch);
        LoggerFactory = BuildLoggerFactory(AppConfig.Instance.Logging);

        var logger = LoggerFactory.CreateLogger("StockTvKiosk");
        logger.LogInformation("StockTV-Kiosk startet auf {Os} ({Arch}).",
            RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture);
        logger.LogInformation("Konfiguration: {Path}", AppConfig.GetConfigFullName());
        WriteBackConfig(logger);

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

    /// <summary>
    /// Schreibt die eingelesene Konfiguration einmal zurueck. Damit stehen nach einem Update
    /// auch neu hinzugekommene Einstellungen samt Standardwert in der Datei - sonst muesste
    /// man wissen, dass es sie gibt, um sie eintragen zu koennen. Schlaegt das Schreiben fehl
    /// (schreibgeschuetztes Verzeichnis), laeuft der Kiosk unveraendert mit den Werten weiter.
    /// </summary>
    private static void WriteBackConfig(ILogger logger)
    {
        try
        {
            AppConfig.Save();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Konfiguration konnte nicht zurueckgeschrieben werden - neue Einstellungen fehlen deshalb in der Datei.");
        }
    }

    /// <summary>
    /// Liest einen booleschen Schalter aus den Argumenten. Akzeptiert <c>--x</c>,
    /// <c>--x true</c> und <c>--x=true</c>; ein ausdrueckliches <c>--x false</c> gewinnt.
    /// </summary>
    private static bool ReadSwitch(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];

            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return !bool.TryParse(argument[(name.Length + 1)..], out bool inline) || inline;

            if (!argument.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < args.Length && bool.TryParse(args[i + 1], out bool following))
                return following;

            return true;
        }

        return false;
    }

    private static ILoggerFactory BuildLoggerFactory(LoggingOptions logging)
        => Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            ApplyLogLevels(builder, logging.LogLevel);

            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });

            if (logging.File.Enabled)
                builder.AddProvider(new FileLoggerProvider(logging.File, AppContext.BaseDirectory));
        });

    /// <summary>
    /// Uebertraegt den LogLevel-Abschnitt aus AppConfig auf den Logger. Entspricht dem, was
    /// frueher AddConfiguration aus appsettings.json gelesen hat: "Default" setzt die
    /// Mindeststufe, jeder andere Eintrag ist ein Filter auf das Kategoriepraefix.
    /// </summary>
    private static void ApplyLogLevels(ILoggingBuilder builder, Dictionary<string, string> levels)
    {
        foreach (var (category, name) in levels)
        {
            if (!Enum.TryParse(name, ignoreCase: true, out LogLevel level))
                continue;

            if (category.Equals("Default", StringComparison.OrdinalIgnoreCase))
                builder.SetMinimumLevel(level);
            else
                builder.AddFilter(category, level);
        }
    }

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
