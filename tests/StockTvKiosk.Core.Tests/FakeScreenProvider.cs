using StockTvKiosk.Core.Display;

namespace StockTvKiosk.Core.Tests;

/// <summary>
/// Ersetzt die Avalonia-Screens-API in den Tests. Genau dafuer ist die Bildschirmerkennung
/// hinter <see cref="IScreenProvider"/> gekapselt.
/// </summary>
internal sealed class FakeScreenProvider(params ScreenInfo[] screens) : IScreenProvider
{
    public IReadOnlyList<ScreenInfo> GetScreens() => screens;
}

internal sealed class FakeDisplayModeDetector(DisplayMode mode, int monitorCount = 0) : IDisplayModeDetector
{
    public DisplayModeInfo Detect() => new()
    {
        Mode = mode,
        MonitorCount = monitorCount,
        Source = "fake"
    };
}

internal static class Screens
{
    /// <summary>Bildschirm mit Bounds ab (x,0) und der angegebenen Groesse.</summary>
    public static ScreenInfo Make(
        int index, int x, int y, int width, int height,
        bool isPrimary = false, double scaling = 1.0, int taskbarHeight = 0)
    {
        var bounds = new ScreenRect(x, y, width, height);
        var working = taskbarHeight == 0
            ? bounds
            : new ScreenRect(x, y, width, height - taskbarHeight);

        return new ScreenInfo(index, bounds, working, isPrimary, scaling, $"Screen{index}");
    }

    /// <summary>Ein einzelner 1920x1080-Primaerbildschirm bei (0,0).</summary>
    public static ScreenInfo[] Single() => [Make(0, 0, 0, 1920, 1080, isPrimary: true)];

    /// <summary>Zwei nebeneinanderliegende 1920x1080-Bildschirme, der linke ist primaer.</summary>
    public static ScreenInfo[] Dual() =>
    [
        Make(0, 0, 0, 1920, 1080, isPrimary: true),
        Make(1, 1920, 0, 1920, 1080)
    ];
}
