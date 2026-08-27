using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Control;
using Xunit;

namespace StockTvKiosk.Core.Tests;

/// <summary>
/// Prueft die Befehlsermittlung fuer Neustart und Herunterfahren. Beide Plattformwege werden
/// hier unabhaengig vom Testrechner geprueft - deshalb wird die Plattform uebergeben statt
/// abgefragt.
/// </summary>
public class PowerCommandResolverTests
{
    [Fact]
    public void Windows_NutztShutdownExeOhneVerzoegerung()
    {
        var plan = PowerCommandResolver.Resolve(
            PowerAction.Reboot, PlatformKind.Windows, new PowerOptions(), force: false);

        Assert.True(plan.Success);
        Assert.Equal("shutdown.exe", plan.Command!.FileName);
        // /t 0: gewartet wird in der Anwendung, damit sich der Neustart zuruecknehmen laesst.
        Assert.Equal("/r /t 0", plan.Command.Arguments);
    }

    [Fact]
    public void Windows_ForceFuegtSchalterHinzu()
    {
        var plan = PowerCommandResolver.Resolve(
            PowerAction.Shutdown, PlatformKind.Windows, new PowerOptions(), force: true);

        Assert.Equal("/s /t 0 /f", plan.Command!.Arguments);
    }

    [Fact]
    public void Linux_NutztSystemctl()
    {
        var reboot = PowerCommandResolver.Resolve(
            PowerAction.Reboot, PlatformKind.Linux, new PowerOptions(), force: false);
        var shutdown = PowerCommandResolver.Resolve(
            PowerAction.Shutdown, PlatformKind.Linux, new PowerOptions(), force: false);

        Assert.Equal(new CommandSpec("systemctl", "reboot"), reboot.Command);
        Assert.Equal(new CommandSpec("systemctl", "poweroff"), shutdown.Command);
    }

    [Fact]
    public void Linux_IgnoriertForce()
    {
        // systemctl kennt kein Gegenstueck zu /f - der Schalter darf dort nichts anhaengen.
        var plan = PowerCommandResolver.Resolve(
            PowerAction.Reboot, PlatformKind.Linux, new PowerOptions(), force: true);

        Assert.Equal("reboot", plan.Command!.Arguments);
    }

    [Fact]
    public void KonfigurierterBefehlSchlaegtDieVorgabe()
    {
        var options = new PowerOptions
        {
            RebootCommand = "sudo",
            RebootArguments = "/sbin/shutdown -r now"
        };

        var plan = PowerCommandResolver.Resolve(PowerAction.Reboot, PlatformKind.Linux, options, force: false);

        Assert.Equal(new CommandSpec("sudo", "/sbin/shutdown -r now"), plan.Command);
    }

    [Fact]
    public void KonfigurierterBefehlGiltNurFuerSeineAktion()
    {
        var options = new PowerOptions { RebootCommand = "sudo", RebootArguments = "/sbin/reboot" };

        var shutdown = PowerCommandResolver.Resolve(
            PowerAction.Shutdown, PlatformKind.Linux, options, force: false);

        Assert.Equal("systemctl", shutdown.Command!.FileName);
    }

    [Fact]
    public void CommandLineOhneBefehl_FaelltNichtStillAufDieVorgabeZurueck()
    {
        // Sonst waere die Einstellung wirkungslos: wer CommandLine waehlt, will genau diesen
        // Befehl - und keinen anderen.
        var options = new PowerOptions { Mode = ControlMode.CommandLine };

        var plan = PowerCommandResolver.Resolve(PowerAction.Reboot, PlatformKind.Windows, options, force: false);

        Assert.False(plan.Success);
        Assert.Equal(ControlStatus.InvalidRequest, plan.Status);
        Assert.Contains("RebootCommand", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_LehntAb()
    {
        var options = new PowerOptions { Mode = ControlMode.Disabled };

        var plan = PowerCommandResolver.Resolve(PowerAction.Reboot, PlatformKind.Windows, options, force: false);

        Assert.Equal(ControlStatus.Disabled, plan.Status);
    }

    [Fact]
    public void UnbekanntePlattform_MeldetNichtUnterstuetzt()
    {
        var plan = PowerCommandResolver.Resolve(
            PowerAction.Reboot, PlatformKind.Other, new PowerOptions(), force: false);

        Assert.Equal(ControlStatus.NotSupported, plan.Status);
        Assert.Contains("CommandLine", plan.Message, StringComparison.Ordinal);
    }
}
