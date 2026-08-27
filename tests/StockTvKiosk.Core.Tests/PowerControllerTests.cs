using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Control;
using Xunit;

namespace StockTvKiosk.Core.Tests;

public class PowerControllerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static PowerController Create(FakeCommandRunner runner, PowerOptions? options = null)
        => new(options ?? new PowerOptions(), runner, PlatformKind.Linux);

    [Fact]
    public async Task OhneVerzoegerung_WirdDerBefehlTrotzdemErstNachDerAntwortAusgefuehrt()
    {
        var runner = new FakeCommandRunner();
        var controller = Create(runner);

        var result = controller.Schedule(PowerAction.Reboot, delaySeconds: 0, force: false);

        // Der Aufruf kehrt sofort zurueck - sonst kaeme die Antwort nie beim Aufrufer an,
        // weil der Rechner mitten im Schreiben weggeht.
        Assert.True(result.Success);
        Assert.Empty(runner.Executed);

        Assert.True(await runner.WaitForExecutionAsync(Patience));
        Assert.Equal(new CommandSpec("systemctl", "reboot"), runner.Executed.Single());
    }

    [Fact]
    public async Task Zuruecknehmen_VerhindertDieAusfuehrung()
    {
        var runner = new FakeCommandRunner();
        var controller = Create(runner);

        controller.Schedule(PowerAction.Shutdown, delaySeconds: 30, force: false);
        var cancelled = controller.Cancel();

        Assert.True(cancelled.Success);
        Assert.Null(controller.Pending);
        Assert.False(await runner.WaitForExecutionAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ZuruecknehmenOhneVormerkung_SagtDas()
    {
        var controller = Create(new FakeCommandRunner());

        var result = controller.Cancel();

        Assert.Equal(ControlStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public void ZweiteVormerkung_WirdAbgelehnt()
    {
        var runner = new FakeCommandRunner();
        var controller = Create(runner);

        controller.Schedule(PowerAction.Reboot, delaySeconds: 60, force: false);
        var second = controller.Schedule(PowerAction.Shutdown, delaySeconds: 60, force: false);

        Assert.Equal(ControlStatus.Conflict, second.Status);
        Assert.Equal(PowerAction.Reboot, controller.Pending!.Action);
    }

    [Fact]
    public void NegativeVerzoegerung_WirdAbgelehnt()
    {
        var result = Create(new FakeCommandRunner())
            .Schedule(PowerAction.Reboot, delaySeconds: -1, force: false);

        Assert.Equal(ControlStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public void VerzoegerungUeberDemMaximum_WirdAbgelehnt()
    {
        var options = new PowerOptions { MaxDelaySeconds = 60 };

        var result = Create(new FakeCommandRunner(), options)
            .Schedule(PowerAction.Reboot, delaySeconds: 61, force: false);

        Assert.Equal(ControlStatus.InvalidRequest, result.Status);
        Assert.Contains("MaxDelaySeconds", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OhneAngabe_GiltDieVorgabeAusDerKonfiguration()
    {
        var options = new PowerOptions { DefaultDelaySeconds = 45 };

        var result = Create(new FakeCommandRunner(), options)
            .Schedule(PowerAction.Reboot, delaySeconds: null, force: false);

        Assert.True(result.Success);
        Assert.Contains("45 s", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abgeschaltet_MerktNichtsVorUndFuehrtNichtsAus()
    {
        var runner = new FakeCommandRunner();
        var options = new PowerOptions { Mode = ControlMode.Disabled };

        var result = Create(runner, options).Schedule(PowerAction.Reboot, delaySeconds: 0, force: false);

        Assert.Equal(ControlStatus.Disabled, result.Status);
        Assert.False(await runner.WaitForExecutionAsync(TimeSpan.FromSeconds(2)));
    }
}
