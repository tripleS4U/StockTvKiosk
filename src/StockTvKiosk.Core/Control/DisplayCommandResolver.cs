using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.Core.Control;

/// <summary>
/// Baut den Befehl, der zwischen "Duplizieren" und "Erweitern" umschaltet. Reine Logik ohne
/// Seiteneffekte - der Windows-Weg ueber die CCD-API steht bewusst nicht hier, sondern in
/// <see cref="WindowsDisplayTopology"/>; hier landet nur, was sich als Kommandozeile
/// ausdruecken laesst.
/// </summary>
public static class DisplayCommandResolver
{
    /// <param name="detected">
    /// Ergebnis der Anzeigeerkennung. Liefert unter Linux die xrandr-Ausgangsnamen; ohne sie
    /// laesst sich kein Befehl bauen, weil xrandr die Ausgaenge namentlich anspricht.
    /// </param>
    public static CommandPlan Resolve(
        DisplayTopology topology,
        PlatformInfo platform,
        DisplayControlOptions options,
        DisplayModeInfo detected)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(detected);

        if (options.Mode == ControlMode.Disabled)
        {
            return CommandPlan.Disabled(
                "Das Umschalten der Bildschirme ist abgeschaltet (KioskOptions > DisplayControl > Mode = Disabled).");
        }

        string? configured = topology == DisplayTopology.Duplicate
            ? options.DuplicateCommand
            : options.ExtendCommand;
        string? configuredArguments = topology == DisplayTopology.Duplicate
            ? options.DuplicateArguments
            : options.ExtendArguments;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var outputs = ResolveOutputs(options, detected);
            return CommandPlan.For(
                configured.Trim(),
                Substitute(configuredArguments ?? string.Empty, outputs, options.ExtendDirection).Trim(),
                $"{Name(topology)} ueber den konfigurierten Befehl.");
        }

        if (options.Mode == ControlMode.CommandLine)
        {
            return CommandPlan.Invalid(
                $"KioskOptions > DisplayControl > Mode ist CommandLine, aber {CommandKey(topology)} ist nicht gesetzt.");
        }

        return platform.Kind switch
        {
            // Der eigentliche Windows-Weg ist die CCD-API; hierher kommt nur der Rueckfall.
            PlatformKind.Windows => ResolveWindowsFallback(topology, options),
            PlatformKind.Linux => ResolveLinux(topology, platform, options, detected),
            _ => CommandPlan.NotSupported(
                "Das Umschalten der Bildschirme ist auf dieser Plattform nicht vorgesehen. " +
                "Mit KioskOptions > DisplayControl > Mode = CommandLine laesst sich ein eigener Befehl hinterlegen.")
        };
    }

    private static CommandPlan ResolveWindowsFallback(DisplayTopology topology, DisplayControlOptions options)
    {
        if (!options.UseDisplaySwitchFallback)
        {
            return CommandPlan.NotSupported(
                "Die CCD-API hat das Umschalten abgelehnt und der DisplaySwitch-Rueckfall ist ausgeschaltet " +
                "(KioskOptions > DisplayControl > UseDisplaySwitchFallback = false).");
        }

        // DisplaySwitch.exe gehoert zu Windows und braucht keine erhoehten Rechte.
        return CommandPlan.For(
            "DisplaySwitch.exe",
            topology == DisplayTopology.Duplicate ? "/clone" : "/extend",
            $"{Name(topology)} ueber DisplaySwitch.exe.");
    }

    private static CommandPlan ResolveLinux(
        DisplayTopology topology,
        PlatformInfo platform,
        DisplayControlOptions options,
        DisplayModeInfo detected)
    {
        if (platform.IsWayland)
        {
            // Unter Wayland bestimmt der Compositor die Anordnung; xrandr sieht bestenfalls
            // die XWayland-Sicht und aendert am echten Ausgang nichts.
            return CommandPlan.NotSupported(
                "Wayland-Sitzung: die Bildschirmanordnung bestimmt der Compositor, xrandr greift dort nicht. " +
                "Entweder auf X11 umstellen (Raspberry Pi OS: raspi-config -> Advanced Options -> Wayland -> X11) " +
                "oder unter KioskOptions > DisplayControl einen eigenen Befehl hinterlegen (z. B. wlr-randr).");
        }

        var (primary, secondary) = ResolveOutputs(options, detected);

        if (primary is null || secondary is null)
        {
            return CommandPlan.Invalid(
                "Es konnten keine zwei Ausgaenge ermittelt werden (gefunden: " +
                (detected.ConnectedOutputs.Count == 0 ? "keine" : string.Join(", ", detected.ConnectedOutputs)) +
                "). Namen unter KioskOptions > DisplayControl > PrimaryOutput/SecondaryOutput eintragen.");
        }

        // --auto schaltet einen bisher abgeschalteten Ausgang mit seinem bevorzugten Modus ein -
        // noetig, weil beim Wechsel auf "Erweitern" der zweite Ausgang oft noch aus ist.
        string placement = topology == DisplayTopology.Duplicate
            ? $"--same-as {primary}"
            : $"{DirectionSwitch(options.ExtendDirection)} {primary}";

        return CommandPlan.For(
            "xrandr",
            $"--output {primary} --auto --primary --output {secondary} --auto {placement}",
            $"{Name(topology)} ueber xrandr ({primary}, {secondary}).");
    }

    /// <summary>
    /// Ausgangsnamen bestimmen: was konfiguriert ist, gewinnt. Sonst der von xrandr als
    /// primaer gemeldete Ausgang und der erste andere angeschlossene.
    /// </summary>
    internal static (string? Primary, string? Secondary) ResolveOutputs(
        DisplayControlOptions options, DisplayModeInfo detected)
    {
        string? primary = Trimmed(options.PrimaryOutput)
                          ?? Trimmed(detected.PrimaryOutput)
                          ?? detected.ConnectedOutputs.FirstOrDefault();

        string? secondary = Trimmed(options.SecondaryOutput)
                            ?? detected.ConnectedOutputs.FirstOrDefault(
                                o => !string.Equals(o, primary, StringComparison.Ordinal));

        return (primary, secondary);
    }

    private static string Substitute(
        string arguments, (string? Primary, string? Secondary) outputs, ExtendDirection direction)
        => arguments
            .Replace("{primary}", outputs.Primary ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{secondary}", outputs.Secondary ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{direction}", DirectionSwitch(direction), StringComparison.OrdinalIgnoreCase);

    private static string DirectionSwitch(ExtendDirection direction) => direction switch
    {
        ExtendDirection.LeftOf => "--left-of",
        ExtendDirection.Above => "--above",
        ExtendDirection.Below => "--below",
        _ => "--right-of"
    };

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Name(DisplayTopology topology)
        => topology == DisplayTopology.Duplicate ? "Duplizieren" : "Erweitern";

    private static string CommandKey(DisplayTopology topology)
        => topology == DisplayTopology.Duplicate ? "DuplicateCommand" : "ExtendCommand";
}
