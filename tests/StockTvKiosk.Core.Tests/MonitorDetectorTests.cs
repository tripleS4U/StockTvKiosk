using System.Runtime.InteropServices;
using StockTvKiosk.Core.Display;
using Xunit;

namespace StockTvKiosk.Core.Tests;

public class MonitorDetectorXrandrTests
{
    private const string Extended = """
        Screen 0: minimum 320 x 200, current 3840 x 1080, maximum 16384 x 16384
        HDMI-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 598mm x 336mm
           1920x1080     60.00*+  50.00
        HDMI-2 connected 1920x1080+1920+0 (normal left inverted right x axis y axis) 598mm x 336mm
           1920x1080     60.00*+
        """;

    private const string Duplicated = """
        Screen 0: minimum 320 x 200, current 1920 x 1080, maximum 16384 x 16384
        HDMI-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 598mm x 336mm
           1920x1080     60.00*+
        HDMI-2 connected 1920x1080+0+0 (normal left inverted right x axis y axis) 598mm x 336mm
           1920x1080     60.00*+
        """;

    [Fact]
    public void Xrandr_ErkenntErweitertAnUnterschiedlichenPositionen()
    {
        var info = MonitorDetector.ParseXrandr(Extended);

        Assert.Equal(DisplayMode.Extended, info.Mode);
        Assert.Equal(2, info.MonitorCount);
    }

    [Fact]
    public void Xrandr_ErkenntDuplizierenAnGleicherPosition()
    {
        var info = MonitorDetector.ParseXrandr(Duplicated);

        Assert.Equal(DisplayMode.Duplicated, info.Mode);
        Assert.Equal(2, info.MonitorCount);
    }

    [Fact]
    public void Xrandr_AngeschlosseneAberInaktiveAusgaengeGeltenNichtAlsDupliziert()
    {
        // Regression: zwei angeschlossene, aber nicht aktivierte Ausgaenge haben beide keine
        // Geometrie. Werden sie in den Positionsvergleich einbezogen, sehen sie wie "gleiche
        // Position" aus und wuerden faelschlich als Duplicate gemeldet.
        const string output = """
            HDMI-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 598mm x 336mm
            HDMI-2 connected (normal left inverted right x axis y axis)
            DP-1 connected (normal left inverted right x axis y axis)
            """;

        var info = MonitorDetector.ParseXrandr(output);

        Assert.Equal(DisplayMode.SingleMonitor, info.Mode);
        Assert.Equal(1, info.MonitorCount);
        Assert.Contains(info.Details, d => d.Contains("Angeschlossen: 3, davon aktiv: 1"));
    }

    [Fact]
    public void Xrandr_ZaehltDisconnectedNichtMit()
    {
        const string output = """
            HDMI-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 598mm x 336mm
            HDMI-2 disconnected (normal left inverted right x axis y axis)
            """;

        var info = MonitorDetector.ParseXrandr(output);

        Assert.Equal(DisplayMode.SingleMonitor, info.Mode);
        Assert.Equal(1, info.MonitorCount);
        Assert.Contains(info.Details, d => d.Contains("Angeschlossen: 1"));
    }

    [Fact]
    public void Xrandr_KommtMitNegativenOffsetsZurecht()
    {
        const string output = """
            HDMI-1 connected primary 1920x1080+0+0 (normal) 598mm x 336mm
            HDMI-2 connected 1920x1080+-1920+0 (normal) 598mm x 336mm
            """;

        var info = MonitorDetector.ParseXrandr(output);

        Assert.Equal(DisplayMode.Extended, info.Mode);
        Assert.Equal(2, info.MonitorCount);
    }

    [Fact]
    public void Xrandr_LeereAusgabeIstEinBildschirm()
    {
        var info = MonitorDetector.ParseXrandr(string.Empty);

        Assert.Equal(DisplayMode.SingleMonitor, info.Mode);
        Assert.Equal(0, info.MonitorCount);
    }

    [Fact]
    public void Xrandr_WaylandHinweisWirdProtokolliert()
    {
        var info = MonitorDetector.ParseXrandr(Extended, wayland: true);

        Assert.Contains(info.Details, d => d.Contains("Wayland"));
    }
}

public class MonitorDetectorNativeTests
{
    [Fact]
    public void PInvokeStructs_HabenDieVonDerWindowsDokuVorgegebeneGroesse()
    {
        // Die CCD-Structs waren nie auf echter Hardware verifiziert. Stimmen die Groessen nicht,
        // liefert QueryDisplayConfig Muell oder beschaedigt den Speicher.
        var (pathInfo, modeInfo) = MonitorDetector.NativeStructSizes();

        Assert.Equal(72, pathInfo);  // sourceInfo 20 + targetInfo 48 + flags 4
        Assert.Equal(64, modeInfo);  // infoType 4 + id 4 + adapterId 8 + Union 48
    }

    [Fact]
    public void Detect_LiefertAufJederPlattformEinErgebnisStattEinerException()
    {
        var info = new MonitorDetector().Detect();

        Assert.NotNull(info);
        Assert.NotEmpty(info.Source);

        if (OperatingSystem.IsWindows())
        {
            // Auf dem Buildrechner laeuft mindestens ein Bildschirm bzw. eine Sitzung.
            Assert.Equal("Windows CCD", info.Source);
        }
        else if (!OperatingSystem.IsLinux())
        {
            Assert.Equal(DisplayMode.Unknown, info.Mode);
            Assert.Contains(RuntimeInformation.OSDescription, info.Source);
        }
    }
}
