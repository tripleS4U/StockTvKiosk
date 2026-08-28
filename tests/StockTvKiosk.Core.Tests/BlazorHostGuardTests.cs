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
        var verdict = BlazorHostGuard.Decide(urlIsServing: false, runningProcesses: 0, busyPorts: [], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Start, verdict.Decision);
    }

    [Fact]
    public void LaufenderProzess_OhneErlaubnisNichtStarten()
    {
        var verdict = BlazorHostGuard.Decide(urlIsServing: false, runningProcesses: 1, busyPorts: [], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Skip, verdict.Decision);
        Assert.Contains("laeuft bereits", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BelegterPortOhneProzess_ReichtSchonZumNichtstarten()
    {
        // StockTV laeuft nicht, aber etwas anderes haelt den Port. Ein Start scheitert daran
        // ohnehin - er wuerde nur eine irrefuehrende Fehlermeldung erzeugen.
        var verdict = BlazorHostGuard.Decide(urlIsServing: false, runningProcesses: 0, busyPorts: [4747], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Skip, verdict.Decision);
        Assert.Contains("Port 4747", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MitErlaubnis_ErstAbraeumenDannStarten()
    {
        var verdict = BlazorHostGuard.Decide(urlIsServing: false, runningProcesses: 2, busyPorts: [4747, 5129], mayStopExisting: true);

        Assert.Equal(HostStartDecision.StopThenStart, verdict.Decision);
        Assert.Contains("2 StockTV-Instanzen", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("4747, 5129", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AntwortendeAdresse_KeinEigenerStart()
    {
        var verdict = BlazorHostGuard.Decide(
            urlIsServing: true, runningProcesses: 0, busyPorts: [], mayStopExisting: false);

        Assert.Equal(HostStartDecision.Skip, verdict.Decision);
        Assert.Contains("antwortet bereits", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AntwortendeAdresse_SchlaegtAuchStopExistingInstance()
    {
        // Der Fall "StockTV als Dienst installiert": die Ports sind belegt, ein Prozess
        // laeuft, und die Adresse liefert. Abraeumen wuerde hier nur den Dienstverwalter zum
        // Neustart bringen - und noetig ist es nicht, es funktioniert ja alles.
        var verdict = BlazorHostGuard.Decide(
            urlIsServing: true, runningProcesses: 1, busyPorts: [4747, 4748, 5129], mayStopExisting: true);

        Assert.Equal(HostStartDecision.Skip, verdict.Decision);
    }

    private static readonly Uri PrimaryAddress = new("http://localhost:5129");

    [Fact]
    public void DerHttpPortKommtAusDerPrimaerUrlDazu()
    {
        var ports = BlazorHostGuard.PortsToCheck(new BlazorHostOptions(), PrimaryAddress);

        // 4747/4748 aus der Vorgabe, 5129 aus der Primaer-URL - auf genau den wartet der
        // Kiosk spaeter, er gehoert also in die Pruefung.
        Assert.Equal([4747, 4748, 5129], ports);
    }

    [Fact]
    public void EinGesetztesAspnetcoreUrlsSchlaegtDiePrimaerUrl()
    {
        // Wer den Schluessel ausdruecklich eintraegt, laesst StockTV woanders lauschen - dann
        // ist dieser Port der richtige, nicht der aus der Primaer-URL.
        var options = new BlazorHostOptions();
        options.Environment["ASPNETCORE_URLS"] = "http://localhost:5200";

        Assert.Equal([4747, 4748, 5200], BlazorHostGuard.PortsToCheck(options, PrimaryAddress));
    }

    [Fact]
    public void KeineDoppeltenPorts()
    {
        var options = new BlazorHostOptions { Ports = [5129, 5129, 4747] };

        Assert.Equal([5129, 4747], BlazorHostGuard.PortsToCheck(options, PrimaryAddress));
    }

    [Fact]
    public void UnsinnigePortnummernFliegenRaus()
    {
        var options = new BlazorHostOptions { Ports = [0, -1, 70000, 4747] };

        Assert.Equal([4747], BlazorHostGuard.PortsToCheck(options, primaryAddress: null));
    }

    [Fact]
    public void DerselbePfadIstDieEigeneAnwendung()
    {
        string path = Path.Combine(Path.GetTempPath(), "StockTV", "StockTvBlazor.exe");

        Assert.True(BlazorHostGuard.IsSameExecutable(path, path));
    }

    [Fact]
    public void UmwegeImPfadAendernNichts()
    {
        string direct = Path.Combine(Path.GetTempPath(), "StockTV", "StockTvBlazor.exe");
        string detour = Path.Combine(Path.GetTempPath(), "StockTV", "unterordner", "..", "StockTvBlazor.exe");

        Assert.True(BlazorHostGuard.IsSameExecutable(detour, direct));
    }

    [Fact]
    public void EineZweiteInstallationIstFremd()
    {
        string ours = Path.Combine(Path.GetTempPath(), "StockTV", "StockTvBlazor.exe");
        string theirs = Path.Combine(Path.GetTempPath(), "StockTV-alt", "StockTvBlazor.exe");

        Assert.False(BlazorHostGuard.IsSameExecutable(theirs, ours));
    }

    [Fact]
    public void OhneErmittelbarenPfadGiltDerProzessAlsFremd()
    {
        // Genau der Dienst-Fall: MainModule ist bei einem Prozess unter einem anderen
        // Benutzer nicht lesbar. Im Zweifel nicht anfassen.
        string ours = Path.Combine(Path.GetTempPath(), "StockTV", "StockTvBlazor.exe");

        Assert.False(BlazorHostGuard.IsSameExecutable(null, ours));
        Assert.False(BlazorHostGuard.IsSameExecutable("   ", ours));
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
    public void PortsAusEinerUrlAngabe(string urls, int[] expected)
        => Assert.Equal(expected, BlazorHostGuard.ExtractPorts(urls));

    [Fact]
    public void OhnePortsWirdNichtsAbgefragt()
        => Assert.Empty(BlazorHostGuard.FindListeningPorts([]));
}
