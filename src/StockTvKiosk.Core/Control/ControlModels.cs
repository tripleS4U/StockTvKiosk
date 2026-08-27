using System.Runtime.InteropServices;

namespace StockTvKiosk.Core.Control;

/// <summary>
/// Betriebssystem, auf das eine Aktion abgebildet wird. Bewusst ein eigener Wert statt
/// direkter <see cref="OperatingSystem"/>-Abfragen: nur so lassen sich die Linux-Pfade
/// unter Windows testen und umgekehrt.
/// </summary>
public enum PlatformKind
{
    Windows,
    Linux,
    Other
}

/// <summary>Was zum Ausschalten angefordert wurde.</summary>
public enum PowerAction
{
    Reboot,
    Shutdown
}

/// <summary>Wie die Bildschirme zueinander stehen sollen.</summary>
public enum DisplayTopology
{
    /// <summary>Alle Ausgaenge zeigen dasselbe Bild.</summary>
    Duplicate,

    /// <summary>Die Ausgaenge bilden eine zusammenhaengende Desktopflaeche.</summary>
    Extend
}

/// <summary>
/// Ausgang einer Aktion. Die Statuswerte sind so geschnitten, dass die Web-Schicht sie
/// ohne Fallunterscheidung auf HTTP-Codes abbilden kann.
/// </summary>
public enum ControlStatus
{
    Ok,

    /// <summary>Per Konfiguration abgeschaltet (Mode = Disabled).</summary>
    Disabled,

    /// <summary>Auf diesem System nicht durchfuehrbar, z. B. Umschalten unter Wayland.</summary>
    NotSupported,

    /// <summary>Die Anfrage selbst war unbrauchbar, z. B. negative Verzoegerung.</summary>
    InvalidRequest,

    /// <summary>Es laeuft bereits etwas, das dem entgegensteht.</summary>
    Conflict,

    /// <summary>Versucht, aber fehlgeschlagen.</summary>
    Failed
}

/// <summary>Ergebnis einer Aktion, fertig zum Zurueckgeben an den Aufrufer.</summary>
/// <param name="Status">Grobe Einordnung, siehe <see cref="ControlStatus"/>.</param>
/// <param name="Message">Ein Satz fuer den Menschen, der die Antwort liest.</param>
/// <param name="Detail">Rohausgabe des Befehls o. Ae., falls vorhanden.</param>
public sealed record ControlResult(ControlStatus Status, string Message, string? Detail = null)
{
    public bool Success => Status == ControlStatus.Ok;

    public static ControlResult Ok(string message, string? detail = null)
        => new(ControlStatus.Ok, message, detail);

    public static ControlResult Disabled(string message)
        => new(ControlStatus.Disabled, message);

    public static ControlResult NotSupported(string message)
        => new(ControlStatus.NotSupported, message);

    public static ControlResult InvalidRequest(string message)
        => new(ControlStatus.InvalidRequest, message);

    public static ControlResult Conflict(string message)
        => new(ControlStatus.Conflict, message);

    public static ControlResult Failed(string message, string? detail = null)
        => new(ControlStatus.Failed, message, detail);
}

/// <summary>Ein auszufuehrendes Programm samt Argumenten.</summary>
public sealed record CommandSpec(string FileName, string Arguments)
{
    public override string ToString()
        => string.IsNullOrEmpty(Arguments) ? FileName : $"{FileName} {Arguments}";
}

/// <summary>
/// Ergebnis eines Befehlsaufrufs. <see cref="TimedOut"/> ist bewusst getrennt vom Exitcode:
/// ein abgebrochener Befehl hat gar keinen, und die Ursache waere sonst nicht zu erkennen.
/// </summary>
public sealed record CommandOutcome(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    public bool Success => !TimedOut && ExitCode == 0;

    /// <summary>Kurzfassung fuer Log und Antwort - stderr zuerst, da dort die Ursache steht.</summary>
    public string Describe()
    {
        if (TimedOut)
            return "Zeitueberschreitung - der Befehl hat nicht zurueckgemeldet.";

        string text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
        text = text.Trim();
        return string.IsNullOrEmpty(text) ? $"Exitcode {ExitCode}." : $"Exitcode {ExitCode}: {text}";
    }
}

/// <summary>Fuehrt ein externes Programm aus. Abstrahiert, damit die Aufrufer testbar bleiben.</summary>
public interface ICommandRunner
{
    Task<CommandOutcome> RunAsync(CommandSpec command, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ergebnis der Befehlsermittlung. Entweder steht ein <see cref="Command"/> bereit, oder
/// <see cref="Status"/> und <see cref="Message"/> sagen, warum nicht - dieselbe Sprache,
/// die auch <see cref="ControlResult"/> spricht.
/// </summary>
public sealed record CommandPlan(CommandSpec? Command, ControlStatus Status, string Message)
{
    public bool Success => Status == ControlStatus.Ok && Command is not null;

    public static CommandPlan For(string fileName, string arguments, string message)
        => new(new CommandSpec(fileName, arguments), ControlStatus.Ok, message);

    public static CommandPlan Disabled(string message) => new(null, ControlStatus.Disabled, message);
    public static CommandPlan NotSupported(string message) => new(null, ControlStatus.NotSupported, message);
    public static CommandPlan Invalid(string message) => new(null, ControlStatus.InvalidRequest, message);

    /// <summary>Uebersetzt einen misslungenen Plan in das Ergebnis, das der Aufrufer zurueckgibt.</summary>
    public ControlResult ToFailure() => new(Status, Message);
}

/// <summary>Ob eine Aktionsgruppe auf diesem System zur Verfuegung steht - und warum nicht.</summary>
public sealed record CapabilityInfo(bool Supported, string Reason);

/// <summary>Was die Schnittstelle auf dem laufenden System tatsaechlich leisten kann.</summary>
public sealed record SystemCapabilities(
    string Platform,
    string Architecture,
    PlatformKind Kind,
    bool IsWayland,
    CapabilityInfo Power,
    CapabilityInfo Display);

/// <summary>Das erkannte Betriebssystem und die dafuer wesentlichen Eigenheiten.</summary>
/// <param name="Kind">Grobe Familie, auf die die Befehle abgebildet werden.</param>
/// <param name="IsWayland">
/// Nur unter Linux von Belang: der Compositor bestimmt die Bildschirmanordnung selbst,
/// deshalb greift das Umschalten dort nicht ueber xrandr.
/// </param>
public sealed record PlatformInfo(PlatformKind Kind, bool IsWayland)
{
    public static PlatformInfo Detect()
    {
        if (OperatingSystem.IsWindows())
            return new PlatformInfo(PlatformKind.Windows, false);

        if (OperatingSystem.IsLinux())
        {
            bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            return new PlatformInfo(PlatformKind.Linux, wayland);
        }

        return new PlatformInfo(PlatformKind.Other, false);
    }

    public string Describe() => RuntimeInformation.OSDescription;
}
