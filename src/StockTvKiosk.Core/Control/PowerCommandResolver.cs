using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.Core.Control;

/// <summary>
/// Bestimmt, welches Programm den Rechner neu startet bzw. herunterfaehrt. Reine Logik ohne
/// Seiteneffekte, damit beide Plattformwege unabhaengig vom Testrechner geprueft werden koennen.
/// </summary>
public static class PowerCommandResolver
{
    /// <summary>
    /// Windows kennt eine Verzoegerung (<c>/t</c>), systemd nicht. Beide bekommen deshalb 0 -
    /// gewartet wird in der Anwendung, siehe <see cref="PowerController"/>.
    /// </summary>
    public static CommandPlan Resolve(PowerAction action, PlatformKind platform, PowerOptions options, bool force)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Mode == ControlMode.Disabled)
        {
            return CommandPlan.Disabled(
                "Neustart und Herunterfahren sind abgeschaltet (KioskOptions > Power > Mode = Disabled).");
        }

        string? configured = action == PowerAction.Reboot ? options.RebootCommand : options.ShutdownCommand;
        string? configuredArguments = action == PowerAction.Reboot ? options.RebootArguments : options.ShutdownArguments;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return CommandPlan.For(configured.Trim(), (configuredArguments ?? string.Empty).Trim(),
                $"{Name(action)} ueber den konfigurierten Befehl.");
        }

        if (options.Mode == ControlMode.CommandLine)
        {
            // Ausdruecklich nur konfigurierte Befehle - dann muss auch einer dastehen. Still
            // auf die Betriebssystem-Vorgabe auszuweichen wuerde die Einstellung aushebeln.
            return CommandPlan.Invalid(
                $"KioskOptions > Power > Mode ist CommandLine, aber {CommandKey(action)} ist nicht gesetzt.");
        }

        return platform switch
        {
            // /f schliesst laufende Anwendungen ohne Rueckfrage. Ohne /f kann eine blockierende
            // Anwendung den Neustart verhindern - deshalb ist es anforderbar, aber nicht Vorgabe.
            PlatformKind.Windows => CommandPlan.For(
                "shutdown.exe",
                action == PowerAction.Reboot ? Windows("/r", force) : Windows("/s", force),
                $"{Name(action)} ueber shutdown.exe."),

            // systemctl geht ueber logind; fuer die aktive lokale Sitzung erlaubt polkit das
            // ueblicherweise ohne Passwort. Reicht es nicht, hilft ein eigener Befehl mit sudo.
            PlatformKind.Linux => CommandPlan.For(
                "systemctl",
                action == PowerAction.Reboot ? "reboot" : "poweroff",
                $"{Name(action)} ueber systemctl."),

            _ => CommandPlan.NotSupported(
                $"{Name(action)} ist auf dieser Plattform nicht vorgesehen. " +
                $"Mit KioskOptions > Power > Mode = CommandLine laesst sich ein eigener Befehl hinterlegen.")
        };
    }

    private static string Windows(string switchName, bool force)
        => force ? $"{switchName} /t 0 /f" : $"{switchName} /t 0";

    private static string Name(PowerAction action)
        => action == PowerAction.Reboot ? "Neustart" : "Herunterfahren";

    private static string CommandKey(PowerAction action)
        => action == PowerAction.Reboot ? "RebootCommand" : "ShutdownCommand";
}
