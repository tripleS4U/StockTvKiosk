using StockTvKiosk.Core.Display;

namespace StockTvKiosk.Core.Layout;

public enum WindowRole
{
    /// <summary>Bedienfenster - muss am Ende den Tastaturfokus haben (Numpad-Eingabe).</summary>
    Primary,

    /// <summary>Gespiegelte Anzeige.</summary>
    Secondary
}

/// <summary>Ein zu oeffnendes Kiosk-Fenster.</summary>
/// <param name="Role">Rolle des Fensters.</param>
/// <param name="Url">Anzuzeigende Adresse.</param>
/// <param name="ScreenIndex">Bildschirm, auf dem das Fenster liegt.</param>
/// <param name="Bounds">Zielrechteck in physischen Pixeln.</param>
/// <param name="Scaling">DPI-Skalierung des Zielbildschirms (fuer die Umrechnung px -> DIP).</param>
public sealed record WindowPlan(
    WindowRole Role,
    string Url,
    int ScreenIndex,
    ScreenRect Bounds,
    double Scaling)
{
    public override string ToString() =>
        $"{Role} -> Bildschirm [{ScreenIndex}] {Bounds}: {Url}";
}

/// <summary>
/// Fertiger Fensterplan. <see cref="Windows"/> ist bereits in Aktivierungsreihenfolge sortiert:
/// das Spiegel-Fenster zuerst, das Bedienfenster zuletzt, damit dieses am Ende den
/// Tastaturfokus haelt (Numpad-Eingabe) - genau wie in start-compiled.ps1.
/// </summary>
public sealed record KioskLayout(
    IReadOnlyList<WindowPlan> Windows,
    IReadOnlyList<string> Warnings,
    DisplayMode DisplayMode,
    int ScreenCount)
{
    /// <summary>Das Fenster, das nach dem Oeffnen aller Fenster den Fokus bekommen muss.</summary>
    public WindowPlan FocusWindow => Windows.Last(w => w.Role == WindowRole.Primary);
}
