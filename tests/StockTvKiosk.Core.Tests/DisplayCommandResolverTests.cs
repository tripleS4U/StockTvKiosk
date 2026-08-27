using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Control;
using StockTvKiosk.Core.Display;
using Xunit;

namespace StockTvKiosk.Core.Tests;

public class DisplayCommandResolverTests
{
    private static readonly PlatformInfo LinuxX11 = new(PlatformKind.Linux, IsWayland: false);
    private static readonly PlatformInfo LinuxWayland = new(PlatformKind.Linux, IsWayland: true);
    private static readonly PlatformInfo Windows = new(PlatformKind.Windows, IsWayland: false);

    /// <summary>Zwei angeschlossene Ausgaenge, HDMI-1 als primaer gemeldet.</summary>
    private static DisplayModeInfo TwoOutputs(params string[] active) => new()
    {
        Mode = DisplayMode.Extended,
        MonitorCount = active.Length,
        Source = "xrandr",
        ConnectedOutputs = ["HDMI-1", "HDMI-2"],
        ActiveOutputs = active,
        PrimaryOutput = "HDMI-1"
    };

    [Fact]
    public void LinuxErweitern_SchaltetDenZweitenAusgangEinUndSetztIhnDaneben()
    {
        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxX11, new DisplayControlOptions(), TwoOutputs("HDMI-1"));

        Assert.True(plan.Success);
        Assert.Equal("xrandr", plan.Command!.FileName);
        // --auto auf HDMI-2 ist der Punkt: beim Wechsel auf "Erweitern" ist der zweite
        // Ausgang haeufig noch abgeschaltet und muss erst einen Modus bekommen.
        Assert.Equal(
            "--output HDMI-1 --auto --primary --output HDMI-2 --auto --right-of HDMI-1",
            plan.Command.Arguments);
    }

    [Fact]
    public void LinuxDuplizieren_LegtDenZweitenAusgangAufDenErsten()
    {
        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Duplicate, LinuxX11, new DisplayControlOptions(), TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(
            "--output HDMI-1 --auto --primary --output HDMI-2 --auto --same-as HDMI-1",
            plan.Command!.Arguments);
    }

    [Theory]
    [InlineData(ExtendDirection.RightOf, "--right-of")]
    [InlineData(ExtendDirection.LeftOf, "--left-of")]
    [InlineData(ExtendDirection.Above, "--above")]
    [InlineData(ExtendDirection.Below, "--below")]
    public void LinuxErweitern_BeachtetDieRichtung(ExtendDirection direction, string expected)
    {
        var options = new DisplayControlOptions { ExtendDirection = direction };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxX11, options, TwoOutputs("HDMI-1"));

        Assert.Contains($"{expected} HDMI-1", plan.Command!.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void KonfigurierteAusgangsnamenSchlagenDieErkennung()
    {
        var options = new DisplayControlOptions { PrimaryOutput = "DP-1", SecondaryOutput = "DP-2" };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxX11, options, TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(
            "--output DP-1 --auto --primary --output DP-2 --auto --right-of DP-1",
            plan.Command!.Arguments);
    }

    [Fact]
    public void OhneZweitenAusgang_SagtWelcheGefundenWurden()
    {
        var detected = new DisplayModeInfo
        {
            Mode = DisplayMode.SingleMonitor,
            Source = "xrandr",
            ConnectedOutputs = ["HDMI-1"],
            PrimaryOutput = "HDMI-1"
        };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxX11, new DisplayControlOptions(), detected);

        Assert.False(plan.Success);
        Assert.Equal(ControlStatus.InvalidRequest, plan.Status);
        Assert.Contains("HDMI-1", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wayland_LehntMitHinweisAufX11Ab()
    {
        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxWayland, new DisplayControlOptions(), TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(ControlStatus.NotSupported, plan.Status);
        Assert.Contains("X11", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wayland_LaesstSichMitEigenemBefehlUmgehen()
    {
        // Der dokumentierte Ausweg: wer wlr-randr hat, traegt es ein und bekommt seinen Befehl.
        var options = new DisplayControlOptions
        {
            Mode = ControlMode.CommandLine,
            ExtendCommand = "wlr-randr",
            ExtendArguments = "--output {secondary} --on --pos 1920,0"
        };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxWayland, options, TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.True(plan.Success);
        Assert.Equal("--output HDMI-2 --on --pos 1920,0", plan.Command!.Arguments);
    }

    [Fact]
    public void Platzhalter_WerdenAlleErsetzt()
    {
        var options = new DisplayControlOptions
        {
            ExtendCommand = "meinskript",
            ExtendArguments = "{primary} {secondary} {direction}"
        };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxX11, options, TwoOutputs("HDMI-1"));

        Assert.Equal("HDMI-1 HDMI-2 --right-of", plan.Command!.Arguments);
    }

    [Fact]
    public void WindowsRueckfall_NutztDisplaySwitch()
    {
        var duplicate = DisplayCommandResolver.Resolve(
            DisplayTopology.Duplicate, Windows, new DisplayControlOptions(), TwoOutputs("HDMI-1", "HDMI-2"));
        var extend = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, Windows, new DisplayControlOptions(), TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(new CommandSpec("DisplaySwitch.exe", "/clone"), duplicate.Command);
        Assert.Equal(new CommandSpec("DisplaySwitch.exe", "/extend"), extend.Command);
    }

    [Fact]
    public void WindowsOhneRueckfall_MeldetNichtUnterstuetzt()
    {
        var options = new DisplayControlOptions { UseDisplaySwitchFallback = false };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, Windows, options, TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(ControlStatus.NotSupported, plan.Status);
    }

    [Fact]
    public void Disabled_LehntAb()
    {
        var options = new DisplayControlOptions { Mode = ControlMode.Disabled };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Extend, LinuxX11, options, TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(ControlStatus.Disabled, plan.Status);
    }

    [Fact]
    public void CommandLineOhneBefehl_FaelltNichtStillAufXrandrZurueck()
    {
        var options = new DisplayControlOptions { Mode = ControlMode.CommandLine };

        var plan = DisplayCommandResolver.Resolve(
            DisplayTopology.Duplicate, LinuxX11, options, TwoOutputs("HDMI-1", "HDMI-2"));

        Assert.Equal(ControlStatus.InvalidRequest, plan.Status);
        Assert.Contains("DuplicateCommand", plan.Message, StringComparison.Ordinal);
    }
}
