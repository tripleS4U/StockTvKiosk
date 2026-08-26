using Avalonia.Controls;
using Avalonia.Platform;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.App.Services;

/// <summary>
/// Bruecke von Avalonias <see cref="Avalonia.Platform.Screens"/> auf das UI-freie
/// <see cref="IScreenProvider"/> des Core-Projekts.
/// </summary>
/// <remarks>
/// Avalonia stellt die Bildschirmliste erst ueber ein <see cref="TopLevel"/> bereit. Deshalb
/// wird hier ein bereits erzeugtes Fenster durchgereicht - es muss dafuer nicht sichtbar sein.
/// </remarks>
public sealed class AvaloniaScreenProvider(TopLevel topLevel) : IScreenProvider
{
    public IReadOnlyList<ScreenInfo> GetScreens()
    {
        var screens = topLevel.Screens;
        if (screens is null || screens.ScreenCount == 0)
            return [];

        var all = screens.All;
        var result = new List<ScreenInfo>(all.Count);

        for (int i = 0; i < all.Count; i++)
        {
            var screen = all[i];
            result.Add(new ScreenInfo(
                Index: i,
                Bounds: ToRect(screen.Bounds),
                WorkingArea: ToRect(screen.WorkingArea),
                IsPrimary: screen.IsPrimary,
                Scaling: screen.Scaling,
                Name: screen.DisplayName));
        }

        return result;
    }

    private static ScreenRect ToRect(Avalonia.PixelRect rect)
        => new(rect.X, rect.Y, rect.Width, rect.Height);
}
