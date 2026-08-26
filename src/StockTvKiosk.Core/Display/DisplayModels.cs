namespace StockTvKiosk.Core.Display;

/// <summary>
/// Anzeigemodus des Systems. "Erweitert" vs. "Duplizieren" ist auf jeder Plattform ein
/// anderes Konzept, deshalb liefert nicht jede Plattform jeden Wert
/// (siehe <see cref="MonitorDetector"/>).
/// </summary>
public enum DisplayMode
{
    /// <summary>Konnte nicht ermittelt werden (z. B. Linux ohne laufenden Compositor).</summary>
    Unknown,

    /// <summary>Genau ein aktiver Bildschirm.</summary>
    SingleMonitor,

    /// <summary>Mehrere Bildschirme bilden eine zusammenhaengende Desktopflaeche.</summary>
    Extended,

    /// <summary>Mehrere Bildschirme zeigen dasselbe Bild.</summary>
    Duplicated
}

/// <summary>Rechteck in physischen Pixeln (identisch zu Avalonias PixelRect, aber UI-frei).</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}

/// <summary>
/// Ein einzelner Bildschirm, UI-framework-unabhaengig, damit die Layout-Logik ohne
/// Avalonia getestet werden kann.
/// </summary>
/// <param name="Index">0-basierter Index in der Bildschirmliste des Systems.</param>
/// <param name="Bounds">Gesamte Flaeche in physischen Pixeln.</param>
/// <param name="WorkingArea">Flaeche abzueglich Taskleisten/Panels, in physischen Pixeln.</param>
/// <param name="IsPrimary">True, wenn das Betriebssystem diesen Bildschirm als primaer meldet.</param>
/// <param name="Scaling">DPI-Skalierungsfaktor (1.0 = 100 %).</param>
/// <param name="Name">Anzeigename, falls die Plattform einen liefert.</param>
public sealed record ScreenInfo(
    int Index,
    ScreenRect Bounds,
    ScreenRect WorkingArea,
    bool IsPrimary,
    double Scaling = 1.0,
    string? Name = null)
{
    public override string ToString() =>
        $"[{Index}] {Bounds}{(IsPrimary ? " <- primaer" : "")}" +
        $"{(Scaling != 1.0 ? $" @{Scaling:0.##}x" : "")}" +
        $"{(string.IsNullOrEmpty(Name) ? "" : $" ({Name})")}";
}

/// <summary>Ergebnis der Anzeigemodus-Erkennung.</summary>
public sealed class DisplayModeInfo
{
    /// <summary>Anzahl aktiver (nicht nur angeschlossener) Monitore, soweit ermittelbar.</summary>
    public int MonitorCount { get; init; }

    public DisplayMode Mode { get; init; }

    /// <summary>Rohdaten der Erkennung fuer das Log.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>Wie die Erkennung durchgefuehrt wurde, z. B. "Windows CCD", "xrandr", "DRM sysfs".</summary>
    public string Source { get; init; } = "unbekannt";

    public override string ToString() =>
        $"Monitore: {MonitorCount}, Modus: {Mode} (Quelle: {Source})" +
        (Details.Count == 0 ? "" : Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", Details));
}

/// <summary>Liefert die Bildschirme des Systems. Wird vom UI-Layer implementiert (Avalonia Screens).</summary>
public interface IScreenProvider
{
    IReadOnlyList<ScreenInfo> GetScreens();
}

/// <summary>Ermittelt, ob die Bildschirme erweitert oder gespiegelt betrieben werden.</summary>
public interface IDisplayModeDetector
{
    DisplayModeInfo Detect();
}
