using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockTvKiosk.App.Services;
using StockTvKiosk.Core.Control;

namespace StockTvKiosk.App.Api;

/// <summary>Anzeigemodus abfragen und zwischen Duplizieren und Erweitern umschalten.</summary>
[ApiController]
[Route("api/display")]
[Produces("application/json")]
public sealed class DisplayController(
    DisplaySwitchService switching,
    KioskWindowManager windows) : ControllerBase
{
    /// <summary>Aktueller Anzeigemodus, erkannte Bildschirme und die offenen Kiosk-Fenster.</summary>
    [HttpGet("mode")]
    [ProducesResponseType<DisplayStateResponse>(StatusCodes.Status200OK)]
    public ActionResult<DisplayStateResponse> Mode() => BuildState(windows);

    /// <summary>
    /// Schaltet auf "Bildschirme duplizieren". Wirkt systemweit, nicht nur auf den Kiosk.
    /// Der Spiegel wird danach geschlossen, sofern TreatDuplicatedAsSingleScreen gesetzt ist -
    /// beide Ausgaenge zeigen ja dasselbe Bild.
    /// </summary>
    [HttpPost("duplicate")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status501NotImplemented)]
    public async Task<IActionResult> Duplicate(CancellationToken cancellationToken)
        => (await switching.SwitchAsync(DisplayTopology.Duplicate, cancellationToken)).ToActionResult();

    /// <summary>
    /// Schaltet auf "Bildschirme erweitern" und ordnet die Fenster auf die beiden
    /// Bildschirme auf.
    /// </summary>
    [HttpPost("extend")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status501NotImplemented)]
    public async Task<IActionResult> Extend(CancellationToken cancellationToken)
        => (await switching.SwitchAsync(DisplayTopology.Extend, cancellationToken)).ToActionResult();

    /// <summary>
    /// Baut die Antwort zum aktuellen Anzeigezustand. Liegt hier, weil auch
    /// <see cref="SystemController.Info"/> sie braucht.
    /// </summary>
    internal static DisplayStateResponse BuildState(KioskWindowManager windows)
    {
        var mode = windows.CurrentDisplayMode;

        return new DisplayStateResponse(
            Mode: mode.Mode.ToString(),
            MonitorCount: mode.MonitorCount,
            Source: mode.Source,
            Details: mode.Details,
            ConnectedOutputs: mode.ConnectedOutputs,
            PrimaryOutput: mode.PrimaryOutput,
            Screens: [.. windows.CurrentScreens.Select(ScreenResponse.From)],
            Windows: [.. (windows.CurrentLayout?.Windows ?? []).Select(WindowResponse.From)]);
    }
}
