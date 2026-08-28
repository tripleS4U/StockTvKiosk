using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Hosting;
using Xunit;

namespace StockTvKiosk.Core.Tests;

/// <summary>
/// Prueft, ob StockTV gestartet werden darf. Der teure Fehler ist hier nicht ein
/// ausgebliebener Start, sondern ein blinder: der raeumt eine laufende Wertung ab.
/// </summary>
public class BlazorHostGuardTests
{
    [Fact]
    public void NichtsLaeuft_AlsoStarten()
    {
        var verdict = BlazorHostGuard.Decide(runningProcesses: 0, busyPorts: [], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Start, verdict.Decision);
    }

    [Fact]
    public void LaufenderProzess_OhneErlaubnisNichtStarten()
    {
        var verdict = BlazorHostGuard.Decide(runningProcesses: 1, busyPorts: [], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Skip, verdict.Decision);
        Assert.Contains("laeuft bereits", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BelegterPortOhneProzess_ReichtSchonZumNichtstarten()
    {
        // StockTV laeuft nicht, aber etwas anderes haelt den Port. Ein Start scheitert daran
        // ohnehin - er wuerde nur eine irrefuehrende Fehlermeldung erzeugen.
        var verdict = BlazorHostGuard.Decide(runningProcesses: 0, busyPorts: [4747], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Skip, verdict.Decision);
        Assert.Contains("Port 4747", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MitErlaubnis_ErstAbraeumenDannStarten()
    {
        var verdict = BlazorHostGuard.Decide(runningProcesses: 2, busyPorts: [4747, 5129], mayStopExisting: true);

        Assert.Equal(HostStartDecision.StopThenStart, verdict.Decision);
        Assert.Contains("2 StockTV-Instanzen", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("4747, 5129", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DerHttpPortKommtAusAspnetcoreUrlsDazu()
    {
        var options = new BlazorHostOptions();

        var ports = BlazorHostGuard.PortsToCheck(options);

        // 4747/4748 aus der Vorgabe, 5129 aus ASPNETCORE_URLS - auf genau den wartet der
        // Kiosk spaeter, er gehoert also in die Pruefung.
        Assert.Equal([4747, 4748, 5129], ports);
    }

    [Fact]
    public void KeineDoppeltenPorts()
    {
        var options = new BlazorHostOptions { Ports = [5129, 5129, 4747] };

        Assert.Equal([5129, 4747], BlazorHostGuard.PortsToCheck(options));
    }

    [Fact]
    public void UnsinnigePortnummernFliegenRaus()
    {
        var options = new BlazorHostOptions { Ports = [0, -1, 70000, 4747] };
        options.Environment.Clear();

        Assert.Equal([4747], BlazorHostGuard.PortsToCheck(options));
    }

    [Theory]
    [InlineData("http://localhost:5129", new[] { 5129 })]
    [InlineData("http://localhost:5129;https://localhost:5130", new[] { 5129, 5130 })]
    // "+" und "*" stehen fuer "alle Adressen" und sind fuer Uri kein gueltiger Host.
    [InlineData("http://+:5129", new[] { 5129 })]
    [InlineData("http://*:5129", new[] { 5129 })]
    // Ohne Portangabe gilt der Standardport des Schemas.
    [InlineData("http://localhost", new[] { 80 })]
    [InlineData("", new int[0])]
    [InlineData("keine-url", new int[0])]
    public void PortsAusAspnetcoreUrls(string urls, int[] expected)
        => Assert.Equal(expected, BlazorHostGuard.ExtractPorts(urls));

    [Fact]
    public void OhnePortsWirdNichtsAbgefragt()
        => Assert.Empty(BlazorHostGuard.FindListeningPorts([]));
}
