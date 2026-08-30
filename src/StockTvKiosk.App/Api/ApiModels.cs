using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockTvKiosk.Core.Control;
using StockTvKiosk.Core.Display;
using StockTvKiosk.Core.Layout;

namespace StockTvKiosk.App.Api;

/// <summary>Antwort auf eine ausgeloeste Aktion.</summary>
/// <param name="Status">Einordnung des Ergebnisses, z. B. "Ok" oder "NotSupported".</param>
/// <param name="Message">Was passiert ist bzw. warum nicht.</param>
/// <param name="Detail">Zusatzangaben, etwa die Ausgabe des ausgefuehrten Befehls.</param>
public sealed record ActionResponse(string Status, string Message, string? Detail);

/// <summary>Anforderung fuer Neustart bzw. Herunterfahren.</summary>
public sealed class PowerRequest
{
    /// <summary>
    /// Verzoegerung in Sekunden. Weggelassen gilt KioskOptions &gt; Power &gt; DefaultDelaySeconds.
    /// Bis zum Ablauf laesst sich die Aktion ueber cancel-shutdown zuruecknehmen.
    /// </summary>
    public int? DelaySeconds { get; set; }

    /// <summary>
    /// Laufende Anwendungen ohne Rueckfrage schliessen (Windows <c>shutdown /f</c>). Unter
    /// Linux ohne Wirkung, weil systemctl kein Gegenstueck dazu hat.
    /// </summary>
    public bool Force { get; set; }
}

/// <summary>Anforderung, den Schluessel der Fernsteuerung zu wechseln.</summary>
public sealed class ApiKeyRequest
{
    /// <summary>
    /// Der neue Schluessel. Mindestens 8 Zeichen; Leerzeichen am Rand werden entfernt, weil
    /// sie sich beim Kopieren einschleichen und dann niemand den Fehler findet.
    /// </summary>
    public string? NewKey { get; set; }
}

/// <summary>Ein Bildschirm, wie ihn das Fenstersystem meldet.</summary>
public sealed record ScreenResponse(
    int Index, int X, int Y, int Width, int Height, bool IsPrimary, double Scaling, string? Name)
{
    public static ScreenResponse From(ScreenInfo screen) => new(
        screen.Index, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height,
        screen.IsPrimary, screen.Scaling, screen.Name);
}

/// <summary>Ein geoeffnetes Kiosk-Fenster.</summary>
public sealed record WindowResponse(string Role, string Url, int ScreenIndex, string Bounds)
{
    public static WindowResponse From(WindowPlan plan)
        => new(plan.Role.ToString(), plan.Url, plan.ScreenIndex, plan.Bounds.ToString());
}

/// <summary>Aktueller Anzeigemodus samt Bildschirmen und Fensteraufteilung.</summary>
public sealed record DisplayStateResponse(
    string Mode,
    int MonitorCount,
    string Source,
    IReadOnlyList<string> Details,
    IReadOnlyList<string> ConnectedOutputs,
    string? PrimaryOutput,
    IReadOnlyList<ScreenResponse> Screens,
    IReadOnlyList<WindowResponse> Windows);

/// <summary>Allgemeine Auskunft ueber Kiosk und Rechner.</summary>
public sealed record SystemInfoResponse(
    string Platform,
    string Architecture,
    string KioskVersion,
    string ConfigurationFile,
    DisplayStateResponse Display,
    PendingPowerResponse? PendingPowerAction);

/// <summary>Eine vorgemerkte, noch nicht ausgefuehrte Aktion.</summary>
public sealed record PendingPowerResponse(string Action, DateTimeOffset DueUtc, double RemainingSeconds)
{
    public static PendingPowerResponse From(PowerPending pending) => new(
        pending.Action.ToString(),
        pending.DueUtc,
        Math.Max(0, (pending.DueUtc - DateTimeOffset.UtcNow).TotalSeconds));
}

/// <summary>Was auf diesem System tatsaechlich geht.</summary>
public sealed record CapabilitiesResponse(
    string Platform,
    string Architecture,
    string Kind,
    bool IsWayland,
    bool CanReboot,
    string PowerReason,
    bool CanSwitchDisplays,
    string DisplayReason)
{
    public static CapabilitiesResponse From(SystemCapabilities capabilities) => new(
        capabilities.Platform,
        capabilities.Architecture,
        capabilities.Kind.ToString(),
        capabilities.IsWayland,
        capabilities.Power.Supported,
        capabilities.Power.Reason,
        capabilities.Display.Supported,
        capabilities.Display.Reason);
}

/// <summary>Uebersetzt ein Ergebnis der Steuerung in eine HTTP-Antwort.</summary>
internal static class ControlResultExtensions
{
    /// <param name="result">Ergebnis, das uebersetzt werden soll.</param>
    /// <param name="successStatusCode">
    /// 200 fuer sofort erledigte Aktionen, 202 fuer solche, die nur vorgemerkt wurden.
    /// </param>
    public static IActionResult ToActionResult(this ControlResult result, int successStatusCode = StatusCodes.Status200OK)
    {
        int statusCode = result.Status switch
        {
            ControlStatus.Ok => successStatusCode,
            ControlStatus.InvalidRequest => StatusCodes.Status400BadRequest,

            // Nicht 404: der Endpunkt gibt es, er ist per Konfiguration gesperrt.
            ControlStatus.Disabled => StatusCodes.Status403Forbidden,
            ControlStatus.Conflict => StatusCodes.Status409Conflict,
            ControlStatus.NotSupported => StatusCodes.Status501NotImplemented,
            _ => StatusCodes.Status500InternalServerError
        };

        return new ObjectResult(new ActionResponse(result.Status.ToString(), result.Message, result.Detail))
        {
            StatusCode = statusCode
        };
    }
}
