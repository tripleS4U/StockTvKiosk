using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StockTvKiosk.Core.Control;

/// <summary>
/// Schaltet unter Windows ueber die CCD-API zwischen "Duplizieren" und "Erweitern" um -
/// dieselbe Schnittstelle, mit der <see cref="Display.MonitorDetector"/> den Modus ausliest.
/// </summary>
/// <remarks>
/// <c>SetDisplayConfig</c> mit einem SDC_TOPOLOGY_*-Flag laesst Windows selbst eine passende
/// Konfiguration waehlen; Pfade und Modi muessen dafuer nicht uebergeben werden. Das ist genau
/// das, was auch die Tastenkombination Win+P ausloest, und braucht keine erhoehten Rechte.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsDisplayTopology
{
    /// <returns>Windows-Fehlercode; 0 = erledigt.</returns>
    public static int Apply(DisplayTopology topology)
    {
        uint flags = SdcApply | (topology == DisplayTopology.Duplicate ? SdcTopologyClone : SdcTopologyExtend);
        return SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
    }

    /// <summary>Klartext zu den Fehlercodes, die hier ueberhaupt vorkommen koennen.</summary>
    public static string Describe(int errorCode) => errorCode switch
    {
        ErrorSuccess => "erledigt",
        ErrorInvalidParameter => "ungueltiger Parameter (Code 87)",
        ErrorNotSupported => "vom Grafiktreiber nicht unterstuetzt (Code 50)",
        ErrorAccessDenied => "Zugriff verweigert (Code 5)",
        ErrorGenFailure => "allgemeiner Geraetefehler (Code 31)",
        ErrorBadConfiguration => "die angeforderte Anordnung ist mit den angeschlossenen Bildschirmen nicht moeglich (Code 1610)",
        _ => $"Fehlercode {errorCode}"
    };

    private const int ErrorSuccess = 0;
    private const int ErrorAccessDenied = 5;
    private const int ErrorGenFailure = 31;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorBadConfiguration = 1610;

    private const uint SdcTopologyClone = 0x00000002;
    private const uint SdcTopologyExtend = 0x00000004;
    private const uint SdcApply = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements,
        IntPtr pathArray,
        uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);
}
