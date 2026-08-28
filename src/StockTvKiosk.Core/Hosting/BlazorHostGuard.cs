using System.Net.NetworkInformation;
using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.Core.Hosting;

/// <summary>Was mit einer bereits vorhandenen StockTV-Instanz geschehen soll.</summary>
public enum HostStartDecision
{
    /// <summary>Nichts steht im Weg - starten.</summary>
    Start,

    /// <summary>Es laeuft schon etwas und es darf nicht abgeraeumt werden - nicht starten.</summary>
    Skip,

    /// <summary>Es laeuft schon etwas, es darf aber abgeraeumt werden.</summary>
    StopThenStart
}

/// <param name="Decision">Was zu tun ist.</param>
/// <param name="Reason">Ein Satz fuer das Log, der sagt, was gefunden wurde.</param>
public sealed record HostStartVerdict(HostStartDecision Decision, string Reason);

/// <summary>
/// Entscheidet, ob StockTV ueberhaupt gestartet werden darf.
/// </summary>
/// <remarks>
/// StockTV belegt seine Ports exklusiv (NetMQ 4747/4748, dazu der HTTP-Port). Ein zweiter
/// Start scheitert daran zwangslaeufig - schlimmer noch, bis dahin hat der Kiosk unter
/// Umstaenden die laufende Wertung schon abgeraeumt. Deshalb wird vorher nachgesehen statt
/// blind zu starten.
///
/// Reine Entscheidungslogik ohne Seiteneffekte, damit sie ohne laufende Prozesse pruefbar ist.
/// </remarks>
public static class BlazorHostGuard
{
    /// <summary>
    /// Ports, die frei sein muessen: die konfigurierten (Vorgabe 4747/4748) plus die aus
    /// <c>ASPNETCORE_URLS</c>. Letztere gehoeren dazu, weil genau auf diesen Port der Kiosk
    /// spaeter wartet - ist er von etwas anderem belegt, kommt StockTV dort nie hoch.
    /// </summary>
    public static IReadOnlyList<int> PortsToCheck(BlazorHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ports = new List<int>();

        foreach (int port in options.Ports)
        {
            if (port is > 0 and <= 65535 && !ports.Contains(port))
                ports.Add(port);
        }

        foreach (int port in ExtractPorts(options.Environment.GetValueOrDefault("ASPNETCORE_URLS")))
        {
            if (!ports.Contains(port))
                ports.Add(port);
        }

        return ports;
    }

    /// <summary>
    /// Liest die Ports aus einer ASPNETCORE_URLS-Angabe, z. B.
    /// <c>http://localhost:5129;http://+:5130</c>. Ohne Portangabe gilt der Standardport
    /// des Schemas.
    /// </summary>
    internal static IReadOnlyList<int> ExtractPorts(string? urls)
    {
        var ports = new List<int>();
        if (string.IsNullOrWhiteSpace(urls))
            return ports;

        foreach (string part in urls.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = part.Trim();
            if (candidate.Length == 0)
                continue;

            // "http://+:5129" und "http://*:5129" sind fuer Uri keine gueltigen Hosts, meinen
            // aber "alle Adressen" - fuer die Portsuche ist der Host ohnehin einerlei.
            candidate = candidate.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0");

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                continue;

            if (uri.Port > 0 && !ports.Contains(uri.Port))
                ports.Add(uri.Port);
        }

        return ports;
    }

    /// <summary>Fragt das Betriebssystem, welche der Ports gerade jemand abhoert.</summary>
    public static IReadOnlyList<int> FindListeningPorts(IReadOnlyList<int> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        if (ports.Count == 0)
            return [];

        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return [.. ports.Where(port => listeners.Any(l => l.Port == port))];
        }
        catch (Exception)
        {
            // Die Abfrage ist auf manchen Systemen gesperrt. Dann lieber ohne diese
            // Information weitermachen als den Start deswegen zu verweigern - die
            // Prozesspruefung greift ja weiterhin.
            return [];
        }
    }

    /// <param name="runningProcesses">Anzahl bereits laufender StockTV-Prozesse.</param>
    /// <param name="busyPorts">Belegte Ports aus <see cref="FindListeningPorts"/>.</param>
    /// <param name="mayStopExisting">KioskOptions &gt; BlazorHost &gt; StopExistingInstance.</param>
    public static HostStartVerdict Decide(
        int runningProcesses, IReadOnlyList<int> busyPorts, bool mayStopExisting)
    {
        ArgumentNullException.ThrowIfNull(busyPorts);

        if (runningProcesses <= 0 && busyPorts.Count == 0)
            return new HostStartVerdict(HostStartDecision.Start, "Es laeuft keine StockTV-Instanz.");

        string reason = Describe(runningProcesses, busyPorts);

        return mayStopExisting
            ? new HostStartVerdict(HostStartDecision.StopThenStart, reason)
            : new HostStartVerdict(HostStartDecision.Skip, reason);
    }

    private static string Describe(int runningProcesses, IReadOnlyList<int> busyPorts)
    {
        var parts = new List<string>(2);

        if (runningProcesses == 1)
            parts.Add("eine StockTV-Instanz laeuft bereits");
        else if (runningProcesses > 1)
            parts.Add($"{runningProcesses} StockTV-Instanzen laufen bereits");

        if (busyPorts.Count == 1)
            parts.Add($"Port {busyPorts[0]} ist belegt");
        else if (busyPorts.Count > 1)
            parts.Add($"die Ports {string.Join(", ", busyPorts)} sind belegt");

        return string.Join(" und ", parts) + ".";
    }
}
