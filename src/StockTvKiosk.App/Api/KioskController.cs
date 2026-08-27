using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockTvKiosk.App.Services;

namespace StockTvKiosk.App.Api;

/// <summary>Eingriffe in die Anzeige selbst, ohne den Rechner anzufassen.</summary>
[ApiController]
[Route("api/kiosk")]
[Produces("application/json")]
public sealed class KioskController(KioskWindowManager windows) : ControllerBase
{
    /// <summary>
    /// Laedt die Blazor-Seiten in allen Fenstern neu - der Fernersatz fuer Shift+Esc, wenn
    /// eine Seite haengt und niemand vor Ort ist.
    /// </summary>
    [HttpPost("reload")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResponse> Reload()
    {
        await windows.ReloadAsync();
        return new ActionResponse("Ok", "Die Fenster laden ihre Seite neu.", null);
    }

    /// <summary>
    /// Erkennt die Bildschirme neu und ordnet die Fenster entsprechend an - ohne den
    /// Anzeigemodus des Rechners zu aendern. Nuetzlich, wenn ein Bildschirm nachtraeglich
    /// angeschlossen oder ueber das Betriebssystem umgestellt wurde.
    /// </summary>
    [HttpPost("relayout")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResponse> Relayout()
    {
        var layout = await windows.ApplyLayoutAsync();

        return new ActionResponse(
            "Ok",
            $"{layout.Windows.Count} Fenster neu angeordnet.",
            string.Join("; ", layout.Windows.Select(w => w.ToString())));
    }
}
