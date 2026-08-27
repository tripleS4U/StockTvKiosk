using Microsoft.Extensions.Logging;
using StockTvKiosk.App.Services;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Control;

namespace StockTvKiosk.App.Api;

/// <summary>
/// Schaltet die Bildschirme um und zieht die Kiosk-Fenster nach.
/// </summary>
/// <remarks>
/// Beides gehoert zusammen: eine Umschaltung ohne Neuanordnung hinterlaesst ein
/// Vollbildfenster auf einem Bildschirm, den es so nicht mehr gibt. Die Reihenfolge samt
/// Wartezeit steht hier und nicht im Controller, weil sie die eigentliche Sacharbeit ist.
/// </remarks>
public sealed class DisplaySwitchService(
    IDisplayTopologyController topology,
    KioskWindowManager windows,
    DisplayControlOptions options,
    ILogger<DisplaySwitchService> logger)
{
    public async Task<ControlResult> SwitchAsync(DisplayTopology target, CancellationToken cancellationToken)
    {
        var result = await topology.ApplyAsync(target, cancellationToken);
        if (!result.Success)
            return result;

        if (!options.ReapplyLayoutAfterSwitch)
        {
            return result with
            {
                Message = result.Message +
                          " Die Fenster bleiben, wo sie sind (ReapplyLayoutAfterSwitch = false)."
            };
        }

        // Das Betriebssystem meldet die neue Anordnung nicht sofort. Ohne diese Pause wuerde
        // der Kiosk noch die alte Bildschirmliste lesen und nach veralteten Daten anordnen.
        if (options.SettleDelayMilliseconds > 0)
            await Task.Delay(options.SettleDelayMilliseconds, cancellationToken);

        try
        {
            var layout = await windows.ApplyLayoutAsync();

            return result with
            {
                Message = result.Message + $" {layout.Windows.Count} Fenster neu angeordnet.",
                Detail = string.Join("; ", layout.Windows.Select(w => w.ToString()))
            };
        }
        catch (Exception ex)
        {
            // Umgeschaltet wurde erfolgreich - das darf die Antwort nicht unterschlagen, sonst
            // versucht es jemand ein zweites Mal, obwohl das Problem woanders liegt.
            logger.LogError(ex, "Bildschirme umgeschaltet, aber die Fenster liessen sich nicht neu anordnen.");

            return ControlResult.Failed(
                result.Message + " Die Fenster liessen sich danach aber nicht neu anordnen.",
                ex.Message);
        }
    }
}
