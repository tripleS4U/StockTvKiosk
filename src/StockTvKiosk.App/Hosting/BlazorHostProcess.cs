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

        var executable = ResolveExecutable();
        if (executable is null)
        {
            logger.LogError(
                "StockTV-Anwendung wurde nicht gefunden (gesucht: {Name}). " +
                "Bitte KioskOptions > BlazorHost > ExecutablePath setzen. Der Kiosk wartet weiter auf die URL.",
                options.ExecutableName);
            return;
        }

        if (!MayStart(executable))
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

        foreach (var (key, value) in options.Environment)
            startInfo.Environment[key] = value;

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

    /// <summary>
    /// Sieht nach, ob StockTV schon laeuft oder einer seiner Ports belegt ist, und
    /// entscheidet danach, ob ueberhaupt gestartet wird.
    /// </summary>
    /// <remarks>
    /// StockTV belegt NetMQ 4747/4748 und den HTTP-Port exklusiv; ein zweiter Start scheitert
    /// daran zwangslaeufig. Blind zu starten hiesse ausserdem, eine laufende Wertung
    /// abzuraeumen - nur weil der Kiosk neu gestartet wurde.
    /// </remarks>
    private bool MayStart(FileInfo executable)
    {
        string processName = Path.GetFileNameWithoutExtension(executable.Name);
        var ports = BlazorHostGuard.PortsToCheck(options);

        var running = FindRunningInstances(processName);

        try
        {
            var verdict = BlazorHostGuard.Decide(
                running.Count, BlazorHostGuard.FindListeningPorts(ports), options.StopExistingInstance);

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
                    StopExistingInstances(running);
                    return PortsAreFreeAfterStopping(ports);
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
    private bool PortsAreFreeAfterStopping(IReadOnlyList<int> ports)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (true)
        {
            var busy = BlazorHostGuard.FindListeningPorts(ports);
            if (busy.Count == 0)
                return true;

            if (DateTime.UtcNow >= deadline)
            {
                logger.LogError(
                    "{Ports} auch nach dem Abraeumen noch belegt - offenbar von etwas anderem als " +
                    "StockTV. Der Kiosk startet nichts und wartet auf die URL.",
                    busy.Count == 1 ? $"Port {busy[0]} ist" : $"Die Ports {string.Join(", ", busy)} sind");
                return false;
            }

            Thread.Sleep(250);
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
    /// Beendet die gefundenen Instanzen. Nur erreichbar, wenn
    /// <see cref="BlazorHostOptions.StopExistingInstance"/> das ausdruecklich erlaubt - der
    /// Aufrufer entsorgt die uebergebenen Prozessobjekte.
    /// </summary>
    private void StopExistingInstances(List<Process> running)
    {
        foreach (var process in running)
        {
            try
            {
                if (process.HasExited)
                    continue;

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
        // Erst die Warteschleife stoppen, sonst meldet sie beim Herunterfahren noch einen
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
