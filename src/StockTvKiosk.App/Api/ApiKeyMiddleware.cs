using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.App.Api;

/// <summary>
/// Laesst nur Anfragen mit dem richtigen Schluessel durch.
/// </summary>
/// <remarks>
/// Ist kein Schluessel hinterlegt, laeuft die Schnittstelle nur auf Loopback - dafuer sorgt
/// <see cref="KioskApiHost"/> beim Start. Diese Klasse muss den Fall also nicht abfangen,
/// sondern laesst dann bewusst jeden durch, der ueberhaupt bis hierher kommt.
/// </remarks>
internal sealed class ApiKeyMiddleware(RequestDelegate next, RestApiOptions options, ILogger<ApiKeyMiddleware> logger)
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(options.ApiKey ?? string.Empty);

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expected.Length == 0 || IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(options.ApiKeyHeader, out var provided) || !Matches(provided!))
        {
            logger.LogWarning(
                "Anfrage ohne gueltigen Schluessel abgewiesen: {Method} {Path} von {Remote}",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "Unauthorized",
                message = $"Kopfzeile {options.ApiKeyHeader} fehlt oder stimmt nicht."
            });
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Die Swagger-Oberflaeche bleibt offen, damit sie sich ueberhaupt oeffnen laesst - der
    /// Schluessel wird dort erst eingetragen. Sie zeigt nur die Beschreibung der Endpunkte;
    /// jeder Aufruf daraus geht wieder durch diese Pruefung.
    /// </summary>
    private static bool IsExempt(PathString path)
        => path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Zeitkonstanter Vergleich: ein normaler Stringvergleich bricht beim ersten
    /// abweichenden Zeichen ab und verraet ueber die Laufzeit, wie weit ein Versuch kam.
    /// </summary>
    private bool Matches(string provided)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expected);
}
