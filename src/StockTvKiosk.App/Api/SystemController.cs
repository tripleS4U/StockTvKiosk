using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockTvKiosk.App.Services;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Control;

namespace StockTvKiosk.App.Api;

/// <summary>Auskunft ueber den Rechner sowie Neustart und Herunterfahren.</summary>
[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemController(
    IPowerController power,
    KioskWindowManager windows,
    KioskOptions options,
    PlatformInfo platform) : ControllerBase
{
    /// <summary>Betriebssystem, Kiosk-Version, erkannte Bildschirme und eine etwaige Vormerkung.</summary>
    [HttpGet("info")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> Info()
    {
        var pending = power.Pending;

        return new SystemInfoResponse(
            Platform: platform.Describe(),
            Architecture: System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            KioskVersion: typeof(SystemController).Assembly.GetName().Version?.ToString() ?? "unbekannt",
            ConfigurationFile: AppConfig.GetConfigFullName(),
            Display: DisplayController.BuildState(windows),
            PendingPowerAction: pending is null ? null : PendingPowerResponse.From(pending));
    }

    /// <summary>
    /// Was auf diesem System tatsaechlich geht - beantwortet vorab, ob ein Aufruf gleich mit
    /// "nicht unterstuetzt" zurueckkommt.
    /// </summary>
    [HttpGet("capabilities")]
    [ProducesResponseType<CapabilitiesResponse>(StatusCodes.Status200OK)]
    public ActionResult<CapabilitiesResponse> Capabilities()
        => CapabilitiesResponse.From(
            CapabilityReporter.Describe(platform, options, windows.CurrentDisplayMode));

    /// <summary>Startet den Rechner neu. Antwortet sofort; ausgefuehrt wird nach der Verzoegerung.</summary>
    [HttpPost("reboot")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status501NotImplemented)]
    public IActionResult Reboot([FromBody] PowerRequest? request)
        => power.Schedule(PowerAction.Reboot, request?.DelaySeconds, request?.Force ?? false)
                .ToActionResult(StatusCodes.Status202Accepted);

    /// <summary>Faehrt den Rechner herunter. Antwortet sofort; ausgefuehrt wird nach der Verzoegerung.</summary>
    [HttpPost("shutdown")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status501NotImplemented)]
    public IActionResult Shutdown([FromBody] PowerRequest? request)
        => power.Schedule(PowerAction.Shutdown, request?.DelaySeconds, request?.Force ?? false)
                .ToActionResult(StatusCodes.Status202Accepted);

    /// <summary>
    /// Nimmt eine vorgemerkte Aktion zurueck. Wirkt nur auf Vormerkungen dieser Schnittstelle -
    /// ein anderweitig angestossenes Herunterfahren erreicht sie nicht.
    /// </summary>
    [HttpPost("cancel-shutdown")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult CancelShutdown() => power.Cancel().ToActionResult();
}
