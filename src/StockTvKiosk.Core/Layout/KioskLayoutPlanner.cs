using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.Core.Layout;

/// <summary>
/// Entscheidet, welche Fenster auf welchem Bildschirm geoeffnet werden. Bewusst frei von
/// Avalonia-Abhaengigkeiten, damit die gesamte Entscheidungslogik ohne UI testbar ist.
/// </summary>
public static class KioskLayoutPlanner
{
    /// <summary>Notgroesse, falls das System ueberhaupt keinen Bildschirm meldet.</summary>
    private static readonly ScreenRect FallbackBounds = new(0, 0, 1280, 720);

    public static KioskLayout Plan(
        IReadOnlyList<ScreenInfo> screens,
        DisplayModeInfo displayMode,
        StockTvKiosk.Core.Configuration.KioskOptions options)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(displayMode);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var display = options.Display;

        string primaryUrl = BuildUrl(options.Urls.PrimaryUrl, options.Urls.Demo);
        string secondaryUrl = BuildUrl(options.Urls.SecondaryUrl, options.Urls.Demo);

        if (screens.Count == 0)
        {
            warnings.Add("Das System meldet keinen einzigen Bildschirm - verwende Notgroesse 1280x720 bei (0,0).");
            return new KioskLayout(
                [new WindowPlan(WindowRole.Primary, primaryUrl, 0, FallbackBounds, 1.0)],
                warnings, displayMode.Mode, 0);
        }

        int mainIndex = ResolveMainIndex(screens, display, warnings);

        // Bei "Duplizieren" zeigen beide Ausgaenge dasselbe Bild - ein zweites Vollbildfenster
        // auf "Bildschirm 2" waere unsichtbar bzw. wuerde das Bedienfenster verdecken.
        bool duplicatedCollapse = displayMode.Mode == DisplayMode.Duplicated
                                  && display.TreatDuplicatedAsSingleScreen;
        if (duplicatedCollapse)
        {
            warnings.Add(
                "Anzeigemodus 'Duplizieren' erkannt: beide Ausgaenge zeigen dasselbe Bild. " +
                "Der zweite Bildschirm wird ignoriert (TreatDuplicatedAsSingleScreen=true).");
        }

        if (!display.Dual)
        {
            var single = Region(screens[mainIndex], display.UseWorkingArea);
            return new KioskLayout(
                [new WindowPlan(WindowRole.Primary, primaryUrl, mainIndex, single, screens[mainIndex].Scaling)],
                warnings, displayMode.Mode, screens.Count);
        }

        int? mirrorIndex = duplicatedCollapse
            ? null
            : ResolveMirrorIndex(screens, display, mainIndex, warnings);

        if (mirrorIndex is { } mirror && mirror != mainIndex)
        {
            // Der Normalfall: je ein randloses Vollbildfenster pro Bildschirm.
            var mainBounds = Region(screens[mainIndex], display.UseWorkingArea);
            var mirrorBounds = Region(screens[mirror], display.UseWorkingArea);

            return new KioskLayout(
                [
                    // Spiegel zuerst, Bedienfenster zuletzt -> Fokus bleibt beim Bedienfenster.
                    new WindowPlan(WindowRole.Secondary, secondaryUrl, mirror, mirrorBounds, screens[mirror].Scaling),
                    new WindowPlan(WindowRole.Primary, primaryUrl, mainIndex, mainBounds, screens[mainIndex].Scaling)
                ],
                warnings, displayMode.Mode, screens.Count);
        }

        // Ab hier: kein zweiter nutzbarer Bildschirm.
        if (!duplicatedCollapse && display.UseSecondScreen)
            warnings.Add("Kein zweiter Bildschirm erkannt.");

        if (display.SingleScreenFallback == SingleScreenFallback.PrimaryOnly)
        {
            warnings.Add("Fallback 'PrimaryOnly': es wird nur das Bedienfenster geoeffnet.");
            var only = Region(screens[mainIndex], display.UseWorkingArea);
            return new KioskLayout(
                [new WindowPlan(WindowRole.Primary, primaryUrl, mainIndex, only, screens[mainIndex].Scaling)],
                warnings, displayMode.Mode, screens.Count);
        }

        warnings.Add("Fallback 'SideBySide': beide Fenster liegen nebeneinander auf dem Hauptbildschirm.");
        var (left, right) = SplitHorizontally(Region(screens[mainIndex], display.UseWorkingArea));
        double scaling = screens[mainIndex].Scaling;

        return new KioskLayout(
            [
                new WindowPlan(WindowRole.Secondary, secondaryUrl, mainIndex, right, scaling),
                new WindowPlan(WindowRole.Primary, primaryUrl, mainIndex, left, scaling)
            ],
            warnings, displayMode.Mode, screens.Count);
    }

    private static int ResolveMainIndex(
        IReadOnlyList<ScreenInfo> screens, DisplayOptions display, List<string> warnings)
    {
        if (display.MainDisplayIndex >= 0)
        {
            if (display.MainDisplayIndex < screens.Count)
                return display.MainDisplayIndex;

            warnings.Add(
                $"MainDisplayIndex {display.MainDisplayIndex} ist ungueltig " +
                $"(verfuegbar: 0..{screens.Count - 1}) - verwende stattdessen den System-Primaerbildschirm.");
        }

        int primary = IndexOfPrimary(screens);
        return primary >= 0 ? primary : 0;
    }

    private static int? ResolveMirrorIndex(
        IReadOnlyList<ScreenInfo> screens, DisplayOptions display, int mainIndex, List<string> warnings)
    {
        if (display.MirrorDisplayIndex >= 0)
        {
            if (display.MirrorDisplayIndex >= screens.Count)
            {
                warnings.Add(
                    $"MirrorDisplayIndex {display.MirrorDisplayIndex} ist ungueltig " +
                    $"(verfuegbar: 0..{screens.Count - 1}).");
                return null;
            }

            if (display.MirrorDisplayIndex == mainIndex)
            {
                warnings.Add(
                    $"MirrorDisplayIndex zeigt auf denselben Bildschirm [{mainIndex}] wie das Bedienfenster.");
                return null;
            }

            return display.MirrorDisplayIndex;
        }

        if (!display.UseSecondScreen)
            return null;

        for (int i = 0; i < screens.Count; i++)
        {
            if (i != mainIndex)
                return i;
        }

        return null;
    }

    private static int IndexOfPrimary(IReadOnlyList<ScreenInfo> screens)
    {
        for (int i = 0; i < screens.Count; i++)
        {
            if (screens[i].IsPrimary)
                return i;
        }

        return -1;
    }

    private static ScreenRect Region(ScreenInfo screen, bool useWorkingArea)
        => useWorkingArea ? screen.WorkingArea : screen.Bounds;

    /// <summary>
    /// Teilt einen Bildschirm in linke und rechte Haelfte - wie start-compiled.ps1 es tut,
    /// wenn kein zweiter Bildschirm vorhanden ist. Die rechte Haelfte bekommt bei ungerader
    /// Breite das uebrige Pixel, damit die Flaeche luecken- und ueberlappungsfrei bleibt.
    /// </summary>
    internal static (ScreenRect Left, ScreenRect Right) SplitHorizontally(ScreenRect bounds)
    {
        int half = bounds.Width / 2;
        var left = new ScreenRect(bounds.X, bounds.Y, half, bounds.Height);
        var right = new ScreenRect(bounds.X + half, bounds.Y, bounds.Width - half, bounds.Height);
        return (left, right);
    }

    /// <summary>Haengt bei Bedarf "demo=true" an, ohne eine bestehende Query zu zerstoeren.</summary>
    internal static string BuildUrl(string url, bool demo)
    {
        if (!demo || string.IsNullOrWhiteSpace(url))
            return url;

        if (url.Contains("demo=", StringComparison.OrdinalIgnoreCase))
            return url;

        char separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}demo=true";
    }
}
