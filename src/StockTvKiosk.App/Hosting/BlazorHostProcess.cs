using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Hosting;

namespace StockTvKiosk.App.Hosting;

/// <summary>
/// Startet und beendet optional den StockTV-Blazor-Prozess, damit der Kiosk ein einziges
/// Startobjekt sein kann (Autostart / systemd). Ist <see cref="BlazorHostOptions.Enabled"/>
/// false, tut diese Klasse nichts - die App wird dann extern gestartet.
/// </summary>
internal sealed class BlazorHostProcess(BlazorHostOptions options, ILogger logger) : IDisposable
{
    private Process? _process;
    private bool _startedByUs;
    private CancellationTokenSource? _readinessCts;
    private Task _readiness = Task.CompletedTask;
    private string? _stdoutLogPath;
    private string? _stderrLogPath;

    /// <summary>Laeuft im Hintergrund; siehe <see cref="StartIfEnabled"/>.</summary>
    private Task _startup = Task.CompletedTask;

    /// <summary>Bricht die Vorpruefungen ab, wenn der Kiosk beendet wird, waehrend sie laufen.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    public bool IsManaged => options.Enabled;

    /// <param name="readinessProbe">
    /// Adresse, unter der die Blazor-Anwendung antworten muss. StockTV ist eine Web-App: der
    /// gestartete Prozess sagt noch nichts darueber aus, ob der HTTP-Server steht. Ohne diese
    /// Pruefung bliebe ein Startfehler unsichtbar - der Kiosk zeigte nur endlos seine
    /// Statusseite. Wird nicht abgewartet, damit die Fenster sofort erscheinen.
    /// </param>
    public void StartIfEnabled(Uri? readinessProbe = null)
    {
        if (!options.Enabled)
        {
            logger.LogInformation(
                "Prozess-Management ist deaktiviert - StockTV muss extern gestartet werden.");
            return;
        }

        // Ab hier wird nicht mehr im Aufrufer gearbeitet: die Vorpruefungen kosten Zeit -
        // allein die Adressabfrage rund 2 s, wenn nichts antwortet, weil localhost zuerst auf
        // ::1 aufloest. Die Fenster sollen deswegen nicht spaeter erscheinen; sie zeigen
        // ohnehin ihre Statusseite, bis StockTV liefert.
        _startup = Task.Run(() => StartAsync(readinessProbe, _lifetime.Token));
    }

    private async Task StartAsync(Uri? readinessProbe, CancellationToken cancellationToken)
    {
        bool urlIsServing = readinessProbe is not null
                            && await IsAlreadyServingAsync(readinessProbe, cancellationToken);

        // Die Adressabfrage steht vor dem Suchen der Anwendung: laeuft StockTV als Dienst,
        // liegt die Datei womoeglich gar nicht dort, wo der Kiosk sie erwartet - eine
        // Fehlermeldung darueber waere dann irrefuehrend, es fehlt ja nichts.
        if (urlIsServing)
        {
            logger.LogInformation(
                "StockTV antwortet bereits unter {Url} - der Kiosk startet keine eigene Instanz.",
                readinessProbe);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        var executable = ResolveExecutable();
        if (executable is null)
        {
            logger.LogError(
                "StockTV-Anwendung wurde nicht gefunden (gesucht: {Name}). " +
                "Bitte KioskOptions > BlazorHost > ExecutablePath setzen. Der Kiosk wartet weiter auf die URL.",
                options.ExecutableName);
            return;
        }

        if (!MayStart(executable, readinessProbe, cancellationToken))
            return;

        var startInfo = new ProcessStartInfo(executable.FullName)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                ? executable.DirectoryName!
                : options.WorkingDirectory,
            UseShellExecute = false,

            // Ohne das blitzt auf einem Kiosk-Bildschirm ein Konsolenfenster ueber der
            // Anzeige auf und bleibt stehen, solange StockTV laeuft.
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(options.Arguments))
            startInfo.Arguments = options.Arguments;

        ApplyEnvironment(startInfo, readinessProbe);

        string? logDirectory = ResolveLogDirectory(executable);
        if (logDirectory is not null)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }

        try
        {
            _process = Process.Start(startInfo);
            if (_process is null)
            {
                logger.LogError("StockTV konnte nicht gestartet werden: {Path}", executable.FullName);
                return;
            }

            _startedByUs = true;
            logger.LogInformation("StockTV gestartet: {Path} (PID {Pid})", executable.FullName, _process.Id);

            if (logDirectory is not null)
                PipeOutputToFiles(_process, logDirectory);

            if (readinessProbe is not null && options.ReadinessTimeoutSeconds > 0)
            {
                _readinessCts = new CancellationTokenSource();
                _readiness = WaitUntilReadyAsync(readinessProbe, _process, _readinessCts.Token);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Start von StockTV fehlgeschlagen: {Path}", executable.FullName);
        }
    }

    private const string AspNetCoreUrls = "ASPNETCORE_URLS";

    /// <summary>
    /// Setzt die Umgebungsvariablen und leitet <c>ASPNETCORE_URLS</c> aus der Primaer-URL ab.
    /// </summary>
    /// <remarks>
    /// Damit steht die Adresse nur an einer Stelle in der Konfiguration. Zweimal gepflegt
    /// laufen die beiden Werte irgendwann auseinander, und dann laedt der Kiosk von einem
    /// Port, auf dem StockTV nicht lauscht - mit einer Statusseite als einzigem Hinweis. Ein
    /// ausdruecklich eingetragener Wert gewinnt weiterhin.
    /// </remarks>
    private void ApplyEnvironment(ProcessStartInfo startInfo, Uri? primaryAddress)
    {
        foreach (var (key, value) in options.Environment)
            startInfo.Environment[key] = value;

        if (primaryAddress is null || options.Environment.ContainsKey(AspNetCoreUrls))
            return;

        string urls = primaryAddress.GetLeftPart(UriPartial.Authority);
        startInfo.Environment[AspNetCoreUrls] = urls;

        logger.LogInformation("{Key} aus der Primaer-URL abgeleitet: {Urls}", AspNetCoreUrls, urls);
    }

    /// <summary>
    /// Sieht nach, ob StockTV schon laeuft oder einer seiner Ports belegt ist, und
    /// entscheidet danach, ob ueberhaupt gestartet wird.
    /// </summary>
    /// <remarks>
    /// StockTV belegt NetMQ 4747/4748 und den HTTP-Port exklusiv; ein zweiter Start scheitert
    /// daran zwangslaeufig. Blind zu starten hiesse ausserdem, eine laufende Wertung
    /// abzuraeumen - nur weil der Kiosk neu gestartet wurde.
    /// </remarks>
    private bool MayStart(FileInfo executable, Uri? primaryAddress, CancellationToken cancellationToken)
    {
        string processName = Path.GetFileNameWithoutExtension(executable.Name);
        var ports = BlazorHostGuard.PortsToCheck(options, primaryAddress);

        var running = FindRunningInstances(processName);

        try
        {
            var verdict = BlazorHostGuard.Decide(
                urlIsServing: false, running.Count, BlazorHostGuard.FindListeningPorts(ports),
                options.StopExistingInstance);

            switch (verdict.Decision)
            {
                case HostStartDecision.Start:
                    return true;

                case HostStartDecision.Skip:
                    logger.LogInformation(
                        "StockTV wird nicht gestartet: {Reason} Der Kiosk laesst den Bestand in Ruhe und " +
                        "wartet auf die URL. Zum Uebernehmen KioskOptions > BlazorHost > StopExistingInstance " +
                        "auf true setzen.", verdict.Reason);
                    return false;

                default:
                    logger.LogWarning(
                        "{Reason} StopExistingInstance ist true - der Bestand wird abgeraeumt.", verdict.Reason);
                    StopOwnInstances(running, executable);
                    return PortsAreFreeAfterStopping(ports, cancellationToken);
            }
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
        }
    }

    /// <summary>
    /// Wartet, bis die abgeraeumten Prozesse ihre Ports wieder freigegeben haben. Bleibt einer
    /// belegt, haelt ihn etwas anderes als StockTV - dann ist ein Start aussichtslos, und den
    /// fremden Halter abzuschiessen steht dem Kiosk nicht zu.
    /// </summary>
    private bool PortsAreFreeAfterStopping(IReadOnlyList<int> ports, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!cancellationToken.IsCancellationRequested)
        {
            var busy = BlazorHostGuard.FindListeningPorts(ports);
            if (busy.Count == 0)
                return true;

            if (DateTime.UtcNow >= deadline)
            {
                logger.LogError(
                    "{Ports} auch nach dem Abraeumen noch belegt - von einem Prozess, den der Kiosk " +
                    "nicht beenden darf. Er startet nichts und wartet auf die URL.",
                    busy.Count == 1 ? $"Port {busy[0]} ist" : $"Die Ports {string.Join(", ", busy)} sind");
                return false;
            }

            Thread.Sleep(250);
        }

        return false;
    }

    /// <summary>
    /// Fragt einmal ab, ob unter der Adresse schon jemand liefert.
    /// </summary>
    /// <remarks>
    /// Jede HTTP-Antwort zaehlt, auch 404 oder 500 - dann steht der Server, und ihn ein
    /// zweites Mal zu starten kann nur schiefgehen. Das ist dieselbe Regel wie bei der
    /// Verbindungsueberwachung der Fenster.
    /// </remarks>
    private async Task<bool> IsAlreadyServingAsync(Uri probe, CancellationToken cancellationToken)
    {
        if (options.UrlProbeTimeoutSeconds <= 0)
            return false;

        // Kein Proxy: das Ziel ist ein lokaler Dienst. Ein im System hinterlegter
        // Unternehmensproxy wuerde die Pruefung sonst ins Leere laufen lassen.
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(options.UrlProbeTimeoutSeconds)
        };

        try
        {
            using var response = await http.GetAsync(
                probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            logger.LogDebug("Vorabpruefung: {Url} antwortet mit HTTP {Status}.",
                probe, (int)response.StatusCode);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Vorabpruefung: {Url} antwortet nicht ({Reason}).", probe, ex.Message);
            return false;
        }
    }

    private List<Process> FindRunningInstances(string processName)
    {
        try
        {
            return [.. Process.GetProcessesByName(processName)];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Laufende StockTV-Instanzen konnten nicht ermittelt werden.");
            return [];
        }
    }

    /// <summary>
    /// Wartet, bis die Blazor-Anwendung auf HTTP antwortet - wie die Warteschleife in
    /// start-compiled.ps1. Bricht sofort ab, wenn sich der Prozess vorher beendet: das ist der
    /// haeufige Startfehler (belegter Port, fehlende Runtime) und ohne diese Meldung waere er
    /// nur an einer endlosen Statusseite erkennbar.
    /// </summary>
    private async Task WaitUntilReadyAsync(Uri probe, Process process, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var deadline = started.AddSeconds(options.ReadinessTimeoutSeconds);

        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        logger.LogInformation("Warte auf StockTV unter {Url} (bis zu {Timeout} s).",
            probe, options.ReadinessTimeoutSeconds);

        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                logger.LogError(
                    "StockTV hat sich beim Start wieder beendet (Exitcode {Code}). {Hint}",
                    process.ExitCode, LogHint());
                return;
            }

            try
            {
                using var response = await http.GetAsync(
                    probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                logger.LogInformation(
                    "StockTV antwortet nach {Seconds:0.0} s auf {Url} (HTTP {Status}).",
                    (DateTimeOffset.UtcNow - started).TotalSeconds, probe, (int)response.StatusCode);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Noch nicht oben - das ist waehrend des Hochfahrens der Normalfall.
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken); }
            catch (OperationCanceledException) { return; }
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        // Anders als das PowerShell-Skript wird der Prozess hier NICHT beendet: er laeuft
        // moeglicherweise nur langsam an, und ein Abschuss wuerde garantieren, dass die URL
        // nie erreichbar wird. Die Fenster warten ohnehin selbsttaetig weiter.
        logger.LogError(
            "StockTV antwortet nach {Timeout} s noch nicht auf {Url}. Der Kiosk zeigt weiter seine " +
            "Statusseite und laedt automatisch nach, sobald die Anwendung erreichbar ist. {Hint}",
            options.ReadinessTimeoutSeconds, probe, LogHint());
    }

    private string LogHint()
        => _stderrLogPath is null
            ? "Konsolenausgabe wird nicht mitgeschrieben - LogDirectory setzen, um die Ursache zu sehen."
            : $"Ursache siehe {_stderrLogPath} bzw. {_stdoutLogPath}.";

    /// <summary>
    /// Legt das Logverzeichnis relativ zur StockTV-Anwendung an, nicht relativ zum Kiosk:
    /// die Konsolenausgabe gehoert zu StockTV und wird dort gesucht (wie in start-compiled.ps1).
    /// </summary>
    private string? ResolveLogDirectory(FileInfo executable)
    {
        if (string.IsNullOrWhiteSpace(options.LogDirectory))
            return null;

        try
        {
            string directory = Path.IsPathRooted(options.LogDirectory)
                ? options.LogDirectory
                : Path.Combine(executable.DirectoryName!, options.LogDirectory);

            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Logverzeichnis fuer StockTV konnte nicht angelegt werden.");
            return null;
        }
    }

    private void PipeOutputToFiles(Process process, string logDirectory)
    {
        _stdoutLogPath = Path.Combine(logDirectory, "stocktv-console.log");
        _stderrLogPath = Path.Combine(logDirectory, "stocktv-console-error.log");
        logger.LogInformation("Konsolen-Log von StockTV: {Path}", _stdoutLogPath);

        var stdout = new StreamWriter(_stdoutLogPath, append: true) { AutoFlush = true };
        var stderr = new StreamWriter(_stderrLogPath, append: true) { AutoFlush = true };

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.WriteLine(e.Data); };
        process.Exited += (_, _) => { stdout.Dispose(); stderr.Dispose(); };
        process.EnableRaisingEvents = true;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// Beendet die Instanzen, die aus der konfigurierten Datei gestartet wurden - und nur die.
    /// </summary>
    /// <remarks>
    /// Nur erreichbar, wenn <see cref="BlazorHostOptions.StopExistingInstance"/> das
    /// ausdruecklich erlaubt. Die Erlaubnis gilt aber der eigenen Anwendung, nicht jedem
    /// Prozess, der zufaellig so heisst: eine zweite Installation oder ein Dienst kann
    /// denselben Namen tragen und geht den Kiosk nichts an. Der Aufrufer entsorgt die
    /// uebergebenen Prozessobjekte.
    /// </remarks>
    private void StopOwnInstances(List<Process> running, FileInfo executable)
    {
        foreach (var process in running)
        {
            try
            {
                if (process.HasExited)
                    continue;

                string? path = TryGetExecutablePath(process);
                if (!BlazorHostGuard.IsSameExecutable(path, executable.FullName))
                {
                    logger.LogInformation(
                        "PID {Pid} heisst zwar {Name}, wurde aber nicht aus {Configured} gestartet " +
                        "({Actual}) - bleibt unangetastet.",
                        process.Id, process.ProcessName, executable.FullName,
                        path ?? "Pfad nicht ermittelbar");
                    continue;
                }

                logger.LogWarning(
                    "Beende bereits laufende StockTV-Instanz (PID {Pid}) wegen der belegten Ports.",
                    process.Id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Instanz PID {Pid} konnte nicht beendet werden.", process.Id);
            }
        }
    }

    /// <summary>
    /// Pfad der Programmdatei eines laufenden Prozesses, oder null. Fehlschlagen ist der
    /// Normalfall bei fremden Prozessen - etwa einem Dienst unter einem anderen Benutzer -
    /// und genau deshalb gelten die dann als fremd.
    /// </summary>
    private static string? TryGetExecutablePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Exception) { return null; }
    }

    private FileInfo? ResolveExecutable()
    {
        string fileName = OperatingSystem.IsWindows()
            ? options.ExecutableName + ".exe"
            : options.ExecutableName;

        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            var file = new FileInfo(options.ExecutablePath);
            if (file.Exists)
                return file;

            // Ein Verzeichnis statt der Datei ist ein naheliegender Eintrag - dann dort nach
            // ExecutableName suchen, statt die Konfiguration wegen einer Formalie abzulehnen.
            if (Directory.Exists(options.ExecutablePath))
            {
                var inDirectory = new FileInfo(Path.Combine(options.ExecutablePath, fileName));
                if (inDirectory.Exists)
                {
                    logger.LogInformation(
                        "ExecutablePath zeigt auf ein Verzeichnis - verwende {Path}.", inDirectory.FullName);
                    return inDirectory;
                }

                logger.LogError(
                    "KioskOptions > BlazorHost > ExecutablePath ist ein Verzeichnis, aber {Name} liegt nicht darin: {Path}",
                    fileName, options.ExecutablePath);
                return null;
            }

            logger.LogError(
                "KioskOptions > BlazorHost > ExecutablePath existiert nicht: {Path}", file.FullName);
            return null;
        }

        string root = string.IsNullOrWhiteSpace(options.SearchRoot)
            ? AppContext.BaseDirectory
            : options.SearchRoot;

        // Vom Startverzeichnis aus nach oben suchen: deckt die uebliche Ablage
        // (Repo/StockTvBlazor/bin/Release/net10.0) ab, ohne das ganze Laufwerk zu scannen.
        var directory = new DirectoryInfo(root);
        for (int level = 0; level < 4 && directory is not null; level++, directory = directory.Parent)
        {
            var hit = FindNewest(directory, fileName);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    private static FileInfo? FindNewest(DirectoryInfo directory, string fileName)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 6,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            return directory.EnumerateFiles(fileName, enumerationOptions)
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        // Zuerst den Hintergrundstart abwarten. Ohne das koennte er gerade dabei sein, einen
        // Prozess zu starten, den anschliessend niemand mehr beendet - _process waere dann
        // schon gelesen und die Instanz bliebe nach dem Kiosk zurueck.
        _lifetime.Cancel();
        try { _startup.Wait(TimeSpan.FromSeconds(5)); } catch { /* beim Beenden egal */ }
        _lifetime.Dispose();

        // Dann die Warteschleife stoppen, sonst meldet sie beim Herunterfahren noch einen
        // "hat sich beendet"-Fehler, den wir selbst ausgeloest haben.
        if (_readinessCts is not null)
        {
            _readinessCts.Cancel();
            try { _readiness.Wait(TimeSpan.FromSeconds(3)); } catch { /* beim Beenden egal */ }
            _readinessCts.Dispose();
            _readinessCts = null;
        }

        var process = _process;
        _process = null;

        if (process is null)
            return;

        using (process)
        {
            if (!_startedByUs || !options.ShutdownWithKiosk)
                return;

            try
            {
                if (process.HasExited)
                    return;

                logger.LogInformation("Beende StockTV (PID {Pid}) zusammen mit dem Kiosk.", process.Id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "StockTV konnte beim Herunterfahren nicht beendet werden.");
            }
        }
    }
}
