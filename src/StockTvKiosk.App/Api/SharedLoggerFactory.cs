using Microsoft.Extensions.Logging;

namespace StockTvKiosk.App.Api;

/// <summary>
/// Reicht die Logger-Fabrik des Kiosks an ASP.NET weiter, ohne sie aus der Hand zu geben.
/// </summary>
/// <remarks>
/// Ohne diese Huelle wuerde der Web-Host die geteilte Fabrik beim Herunterfahren entsorgen -
/// und der Kiosk verloere mitten im Betrieb sein Log, weil die Schnittstelle beendet wurde.
/// Entsorgt wird die echte Fabrik weiterhin genau an einer Stelle: in Program.Main.
/// </remarks>
internal sealed class SharedLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose()
    {
        // Absichtlich leer, siehe Klassenkommentar.
    }
}
