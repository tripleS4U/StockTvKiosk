using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace StockTvKiosk.App.Api;

/// <summary>
/// Die Konfigurationsdatei aus der Ferne holen, ersetzen oder zuruecksetzen.
/// </summary>
/// <remarks>
/// Aenderungen wirken erst beim naechsten Start des Kiosks - die laufenden Fenster und
/// Dienste arbeiten mit den Werten, die sie beim Hochfahren gelesen haben. Nach dem
/// Hochladen also <c>POST /api/system/reboot</c>.
/// </remarks>
[ApiController]
[Route("api/config")]
public sealed class ConfigController(ILogger<ConfigController> logger) : ControllerBase
{
    private const string FileName = "AppConfig.json";

    /// <summary>
    /// Laedt die Konfigurationsdatei herunter, so wie sie auf dem Geraet liegt. Als
    /// <c>[Secret]</c> markierte Werte stehen darin verschluesselt.
    /// </summary>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status404NotFound)]
    public IActionResult Download()
    {
        if (!AppConfig.FileExists())
        {
            return NotFound(new ActionResponse(
                "NotFound",
                $"Es gibt keine Konfigurationsdatei unter {AppConfig.GetConfigFullName()}.",
                null));
        }

        logger.LogInformation("Konfigurationsdatei wird heruntergeladen.");
        return File(Encoding.UTF8.GetBytes(AppConfig.ReadFile()), "application/json", FileName);
    }

    /// <summary>
    /// Ersetzt die Konfigurationsdatei. Der Inhalt wird vorher probeweise gelesen; scheitert
    /// das, bleibt die bisherige Datei unveraendert. Sie wird zusaetzlich als
    /// <c>.bak</c> gesichert.
    /// </summary>
    /// <param name="file">Die neue Konfigurationsdatei.</param>
    [HttpPut]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return Reject("Es wurde keine Datei uebertragen.");

        if (file.Length > AppConfig.MaxFileSizeBytes)
        {
            return Reject(
                $"Die Datei ist mit {file.Length} Byte zu gross " +
                $"(erlaubt sind {AppConfig.MaxFileSizeBytes} Byte).");
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync();

        if (!AppConfig.TryReplaceFile(content, out string? error, out string? backupPath))
        {
            logger.LogWarning("Hochgeladene Konfiguration abgelehnt: {Error}", error);
            return Reject($"Die Datei wurde nicht uebernommen: {error}");
        }

        logger.LogWarning(
            "Konfigurationsdatei ersetzt ({Bytes} Byte). Bisherige Fassung: {Backup}",
            file.Length, backupPath ?? "keine vorhanden");

        return Ok(new ActionResponse(
            "Ok",
            "Die Konfiguration wurde uebernommen und wirkt beim naechsten Start des Kiosks.",
            backupPath is null ? null : $"Bisherige Fassung gesichert als {backupPath}"));
    }

    /// <summary>
    /// Legt die Konfigurationsdatei mit Standardwerten neu an.
    /// </summary>
    /// <param name="keepRestApi">
    /// Die Einstellungen der REST-Schnittstelle behalten (Vorgabe). Auf <c>false</c> gesetzt
    /// verliert das Geraet seinen API-Schluessel und ist aus der Ferne nicht mehr erreichbar.
    /// </param>
    [HttpPost("default")]
    [Produces("application/json")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status500InternalServerError)]
    public IActionResult CreateDefault([FromQuery] bool keepRestApi = true)
    {
        try
        {
            string? backupPath = AppConfig.ResetToDefaults(keepRestApi);

            logger.LogWarning(
                "Konfigurationsdatei auf Standardwerte zurueckgesetzt (RestApi behalten: {Keep}). " +
                "Bisherige Fassung: {Backup}", keepRestApi, backupPath ?? "keine vorhanden");

            return Ok(new ActionResponse(
                "Ok",
                keepRestApi
                    ? "Standardkonfiguration angelegt, die Einstellungen der REST-Schnittstelle " +
                      "wurden uebernommen. Wirkt beim naechsten Start des Kiosks."
                    : "Standardkonfiguration angelegt - ohne die bisherigen Einstellungen der " +
                      "REST-Schnittstelle. Nach dem naechsten Start ist dieses Geraet aus der " +
                      "Ferne nicht mehr erreichbar.",
                backupPath is null ? null : $"Bisherige Fassung gesichert als {backupPath}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Standardkonfiguration konnte nicht angelegt werden.");

            return StatusCode(StatusCodes.Status500InternalServerError,
                new ActionResponse("Failed", "Die Standardkonfiguration konnte nicht angelegt werden.", ex.Message));
        }
    }

    /// <summary>
    /// Wechselt den Schluessel der Fernsteuerung. Der neue gilt sofort - der bisherige ab
    /// dieser Antwort nicht mehr.
    /// </summary>
    /// <remarks>
    /// Geschrieben wird die gesamte laufende Konfiguration. Wurde seit dem Start eine Datei
    /// ueber <c>PUT /api/config</c> hochgeladen, aber noch nicht neu gestartet, gewinnt hier
    /// der Stand aus dem Arbeitsspeicher. In der Reihenfolge hochladen - neu starten -
    /// Schluessel wechseln kommt das nicht vor.
    /// </remarks>
    /// <param name="request">Der neue Schluessel.</param>
    [HttpPost("api-key")]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ActionResponse>(StatusCodes.Status500InternalServerError)]
    public IActionResult ChangeApiKey([FromBody] ApiKeyRequest? request)
    {
        string newKey = (request?.NewKey ?? string.Empty).Trim();

        if (newKey.Length == 0)
            return Reject("Es wurde kein neuer Schluessel uebergeben (Feld newKey).");

        if (newKey.Length < MinimumApiKeyLength)
        {
            return Reject(
                $"Der Schluessel ist zu kurz - mindestens {MinimumApiKeyLength} Zeichen. " +
                "Er ist das Einzige, was zwischen dem Netz und dem Neustart-Endpunkt steht.");
        }

        var restApi = AppConfig.Instance.KioskOptions.RestApi;

        if (string.Equals(newKey, restApi.ApiKey, StringComparison.Ordinal))
            return Reject("Der neue Schluessel ist der bisherige.");

        string previous = restApi.ApiKey;

        try
        {
            // Erst in den laufenden Betrieb, dann auf die Platte: die Pruefung liest den
            // Wert bei jeder Anfrage aus genau diesem Objekt.
            restApi.ApiKey = newKey;
            AppConfig.Save();
        }
        catch (Exception ex)
        {
            // Zuruecknehmen, sonst gaelte ein Schluessel, der nirgends steht - und nach dem
            // naechsten Start kaeme niemand mehr auf das Geraet.
            restApi.ApiKey = previous;

            logger.LogError(ex, "Der API-Schluessel konnte nicht gespeichert werden.");

            return StatusCode(StatusCodes.Status500InternalServerError, new ActionResponse(
                "Failed",
                "Der Schluessel konnte nicht gespeichert werden und bleibt unveraendert.",
                ex.Message));
        }

        logger.LogWarning("Der API-Schluessel wurde ueber die Schnittstelle gewechselt.");

        return Ok(new ActionResponse(
            "Ok",
            "Der Schluessel gilt ab sofort. Der bisherige ist damit ungueltig.",
            $"Verschluesselt abgelegt in {AppConfig.GetConfigFullName()}"));
    }

    /// <summary>
    /// Kurz genug zum Eintippen, lang genug, dass Durchprobieren keine Aussicht hat. Wer es
    /// genauer will, nimmt einen erzeugten Schluessel.
    /// </summary>
    private const int MinimumApiKeyLength = 8;

    private BadRequestObjectResult Reject(string message)
        => BadRequest(new ActionResponse("InvalidRequest", message, null));
}
