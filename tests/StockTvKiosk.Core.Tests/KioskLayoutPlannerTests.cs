using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Display;
using StockTvKiosk.Core.Layout;
using Xunit;

namespace StockTvKiosk.Core.Tests;

public class KioskLayoutPlannerTests
{
    private const string Primary = "http://localhost:5129/turnier";
    private const string Secondary = "http://localhost:5129/display2";

    private static KioskOptions Options(Action<DisplayOptions>? configure = null)
    {
        var options = new KioskOptions
        {
            Urls = new UrlOptions { PrimaryUrl = Primary, SecondaryUrl = Secondary }
        };
        configure?.Invoke(options.Display);
        return options;
    }

    private static DisplayModeInfo Mode(DisplayMode mode, int count = 0)
        => new() { Mode = mode, MonitorCount = count, Source = "test" };

    // ---------- Zwei Bildschirme: der Normalfall ----------

    [Fact]
    public void ZweiBildschirme_ErzeugenZweiVollbildfenster()
    {
        var layout = KioskLayoutPlanner.Plan(
            new FakeScreenProvider(Screens.Dual()).GetScreens(),
            Mode(DisplayMode.Extended, 2),
            Options());

        Assert.Equal(2, layout.Windows.Count);

        var main = Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary);
        Assert.Equal(0, main.ScreenIndex);
        Assert.Equal(new ScreenRect(0, 0, 1920, 1080), main.Bounds);
        Assert.Equal(Primary, main.Url);

        var mirror = Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary);
        Assert.Equal(1, mirror.ScreenIndex);
        Assert.Equal(new ScreenRect(1920, 0, 1920, 1080), mirror.Bounds);
        Assert.Equal(Secondary, mirror.Url);
    }

    [Fact]
    public void Bedienfenster_WirdZuletztGeoeffnet_DamitDerNumpadFokusStimmt()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2), Options());

        // start-compiled.ps1 oeffnet bewusst erst den Spiegel, dann das Bedienfenster.
        Assert.Equal(WindowRole.Secondary, layout.Windows[0].Role);
        Assert.Equal(WindowRole.Primary, layout.Windows[^1].Role);
        Assert.Same(layout.Windows[^1], layout.FocusWindow);
    }

    [Fact]
    public void NichtPrimaererBildschirmZuerst_BedienfensterLandetTrotzdemAufDemPrimaeren()
    {
        ScreenInfo[] screens =
        [
            Screens.Make(0, 0, 0, 1280, 1024),
            Screens.Make(1, 1280, 0, 1920, 1080, isPrimary: true)
        ];

        var layout = KioskLayoutPlanner.Plan(screens, Mode(DisplayMode.Extended, 2), Options());

        Assert.Equal(1, Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary).ScreenIndex);
        Assert.Equal(0, Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).ScreenIndex);
    }

    [Fact]
    public void ZweiterBildschirmLinks_MitNegativemOffset_WirdKorrektBelegt()
    {
        // Reale Testkonfiguration: der zweite Monitor liegt links vom primaeren,
        // seine X-Koordinate ist deshalb negativ.
        ScreenInfo[] screens =
        [
            Screens.Make(0, -1920, 0, 1920, 1200),
            Screens.Make(1, 0, 0, 2560, 1440, isPrimary: true)
        ];

        var layout = KioskLayoutPlanner.Plan(screens, Mode(DisplayMode.Extended, 2), Options());

        var main = Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary);
        var mirror = Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary);

        Assert.Equal(new ScreenRect(0, 0, 2560, 1440), main.Bounds);
        Assert.Equal(new ScreenRect(-1920, 0, 1920, 1200), mirror.Bounds);
        Assert.Empty(layout.Warnings);
    }

    // ---------- Nur ein Bildschirm ----------

    [Fact]
    public void EinBildschirm_SideBySide_TeiltDenBildschirmLueckenlos()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Single(), Mode(DisplayMode.SingleMonitor, 1), Options());

        Assert.Equal(2, layout.Windows.Count);

        var main = Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary);
        var mirror = Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary);

        Assert.Equal(new ScreenRect(0, 0, 960, 1080), main.Bounds);
        Assert.Equal(new ScreenRect(960, 0, 960, 1080), mirror.Bounds);
        Assert.Equal(main.Bounds.Right, mirror.Bounds.X);
        Assert.Contains(layout.Warnings, w => w.Contains("Kein zweiter Bildschirm"));
    }

    [Fact]
    public void UngeradeBreite_WirdOhneLueckeUndOhneUeberlappungGeteilt()
    {
        var (left, right) = KioskLayoutPlanner.SplitHorizontally(new ScreenRect(0, 0, 1921, 1080));

        Assert.Equal(960, left.Width);
        Assert.Equal(961, right.Width);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(1921, left.Width + right.Width);
    }

    [Fact]
    public void EinBildschirm_PrimaryOnly_OeffnetNurDasBedienfenster()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Single(), Mode(DisplayMode.SingleMonitor, 1),
            Options(d => d.SingleScreenFallback = SingleScreenFallback.PrimaryOnly));

        var window = Assert.Single(layout.Windows);
        Assert.Equal(WindowRole.Primary, window.Role);
        Assert.Equal(new ScreenRect(0, 0, 1920, 1080), window.Bounds);
    }

    [Fact]
    public void DualAus_OeffnetNurEinVollbildfenster()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2), Options(d => d.Dual = false));

        var window = Assert.Single(layout.Windows);
        Assert.Equal(WindowRole.Primary, window.Role);
        Assert.Equal(new ScreenRect(0, 0, 1920, 1080), window.Bounds);
    }

    [Fact]
    public void UseSecondScreenAus_LegtBeideFensterAufDenHauptbildschirm()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2), Options(d => d.UseSecondScreen = false));

        Assert.Equal(2, layout.Windows.Count);
        Assert.All(layout.Windows, w => Assert.Equal(0, w.ScreenIndex));
        Assert.Equal(new ScreenRect(960, 0, 960, 1080),
            Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).Bounds);
    }

    // ---------- Duplizieren ----------

    [Fact]
    public void Duplizieren_IgnoriertDenZweitenBildschirmUndWarnt()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Duplicated, 2), Options());

        // Beide Ausgaenge zeigen dasselbe Bild - ein Vollbild-Spiegel wuerde das Bedienfenster verdecken.
        Assert.All(layout.Windows, w => Assert.Equal(0, w.ScreenIndex));
        Assert.Contains(layout.Warnings, w => w.Contains("Duplizieren"));
    }

    [Fact]
    public void Duplizieren_KannPerOptionUeberstimmtWerden()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Duplicated, 2),
            Options(d => d.TreatDuplicatedAsSingleScreen = false));

        Assert.Equal(1, Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).ScreenIndex);
    }

    [Fact]
    public void UnbekannterModus_VerhaeltSichWieErweitert()
    {
        // Linux ohne Compositor meldet Unknown - dann entscheiden allein die Avalonia-Screens.
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Unknown, 2), Options());

        Assert.Equal(1, Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).ScreenIndex);
    }

    // ---------- Explizite Indizes ----------

    [Fact]
    public void ExpliziteIndizes_UeberstimmenDieSystemPrimaryAutomatik()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2),
            Options(d => { d.MainDisplayIndex = 1; d.MirrorDisplayIndex = 0; }));

        Assert.Equal(1, Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary).ScreenIndex);
        Assert.Equal(0, Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).ScreenIndex);
    }

    [Fact]
    public void UngueltigerMainIndex_FaelltAufDenPrimaerbildschirmZurueckUndWarnt()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2), Options(d => d.MainDisplayIndex = 7));

        Assert.Equal(0, Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary).ScreenIndex);
        Assert.Contains(layout.Warnings, w => w.Contains("MainDisplayIndex 7"));
    }

    [Fact]
    public void UngueltigerMirrorIndex_FaelltAufSideBySideZurueckUndWarnt()
    {
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2), Options(d => d.MirrorDisplayIndex = 7));

        Assert.Equal(2, layout.Windows.Count);
        Assert.All(layout.Windows, w => Assert.Equal(0, w.ScreenIndex));
        Assert.Contains(layout.Warnings, w => w.Contains("MirrorDisplayIndex 7"));
    }

    [Fact]
    public void GleicherIndexFuerBeideFenster_TeiltDenBildschirm()
    {
        // Entspricht start-compiled.ps1: -MainDisplayIndex ohne -MirrorDisplayIndex
        // legt den Spiegel auf die rechte Haelfte des Hauptbildschirms.
        var layout = KioskLayoutPlanner.Plan(
            Screens.Dual(), Mode(DisplayMode.Extended, 2),
            Options(d => { d.MainDisplayIndex = 1; d.MirrorDisplayIndex = 1; }));

        Assert.Equal(new ScreenRect(1920, 0, 960, 1080),
            Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary).Bounds);
        Assert.Equal(new ScreenRect(2880, 0, 960, 1080),
            Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).Bounds);
    }

    // ---------- Sonderfaelle ----------

    [Fact]
    public void KeinBildschirmGemeldet_LiefertEinNotfenster()
    {
        var layout = KioskLayoutPlanner.Plan([], Mode(DisplayMode.Unknown), Options());

        var window = Assert.Single(layout.Windows);
        Assert.Equal(new ScreenRect(0, 0, 1280, 720), window.Bounds);
        Assert.Contains(layout.Warnings, w => w.Contains("keinen einzigen Bildschirm"));
    }

    [Fact]
    public void UseWorkingArea_BeruecksichtigtDieTaskleiste()
    {
        ScreenInfo[] screens = [Screens.Make(0, 0, 0, 1920, 1080, isPrimary: true, taskbarHeight: 40)];

        var layout = KioskLayoutPlanner.Plan(
            screens, Mode(DisplayMode.SingleMonitor, 1),
            Options(d => { d.UseWorkingArea = true; d.SingleScreenFallback = SingleScreenFallback.PrimaryOnly; }));

        Assert.Equal(new ScreenRect(0, 0, 1920, 1040), Assert.Single(layout.Windows).Bounds);
    }

    [Fact]
    public void Skalierung_WirdAnDasFensterDurchgereicht()
    {
        ScreenInfo[] screens =
        [
            Screens.Make(0, 0, 0, 3840, 2160, isPrimary: true, scaling: 2.0),
            Screens.Make(1, 3840, 0, 1920, 1080, scaling: 1.0)
        ];

        var layout = KioskLayoutPlanner.Plan(screens, Mode(DisplayMode.Extended, 2), Options());

        Assert.Equal(2.0, Assert.Single(layout.Windows, w => w.Role == WindowRole.Primary).Scaling);
        Assert.Equal(1.0, Assert.Single(layout.Windows, w => w.Role == WindowRole.Secondary).Scaling);
    }

    // ---------- Demo-Schalter ----------

    [Theory]
    [InlineData("http://localhost:5129/turnier", true, "http://localhost:5129/turnier?demo=true")]
    [InlineData("http://localhost:5129/turnier", false, "http://localhost:5129/turnier")]
    [InlineData("http://localhost:5129/turnier?x=1", true, "http://localhost:5129/turnier?x=1&demo=true")]
    [InlineData("http://localhost:5129/turnier?demo=false", true, "http://localhost:5129/turnier?demo=false")]
    public void DemoSchalter_HaengtQueryKorrektAn(string url, bool demo, string expected)
        => Assert.Equal(expected, KioskLayoutPlanner.BuildUrl(url, demo));

    [Fact]
    public void DemoSchalter_GiltFuerBeideFenster()
    {
        var options = Options();
        options.Urls.Demo = true;

        var layout = KioskLayoutPlanner.Plan(Screens.Dual(), Mode(DisplayMode.Extended, 2), options);

        Assert.All(layout.Windows, w => Assert.EndsWith("demo=true", w.Url));
    }
}
