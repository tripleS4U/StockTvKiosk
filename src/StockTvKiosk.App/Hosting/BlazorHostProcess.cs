using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StockTvKiosk.Core.Configuration;

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

    public bool IsManaged => options.Enabled;

    public void StartIfEnabled()
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
                "Bitte Kiosk:BlazorHost:ExecutablePath setzen. Der Kiosk wartet weiter auf die URL.",
                options.ExecutableName);
            return;
        }

        if (options.StopExistingInstance)
            StopExistingInstances(executable);

        var startInfo = new ProcessStartInfo(executable.FullName)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                ? executable.DirectoryName!
                : options.WorkingDirectory,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(options.Arguments))
            startInfo.Arguments = options.Arguments;

        foreach (var (key, value) in options.Environment)
            startInfo.Environment[key] = value;

        string? logDirectory = ResolveLogDirectory();
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Start von StockTV fehlgeschlagen: {Path}", executable.FullName);
        }
    }

    private string? ResolveLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(options.LogDirectory))
            return null;

        try
        {
            string directory = Path.IsPathRooted(options.LogDirectory)
                ? options.LogDirectory
                : Path.Combine(AppContext.BaseDirectory, options.LogDirectory);

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
        var stdout = new StreamWriter(
            Path.Combine(logDirectory, "stocktv-console.log"), append: true) { AutoFlush = true };
        var stderr = new StreamWriter(
            Path.Combine(logDirectory, "stocktv-console-error.log"), append: true) { AutoFlush = true };

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.WriteLine(e.Data); };
        process.Exited += (_, _) => { stdout.Dispose(); stderr.Dispose(); };
        process.EnableRaisingEvents = true;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// StockTV bindet NetMQ auf 4747/4748. Laeuft bereits eine Instanz, scheitert der Start
    /// der zweiten an den belegten Ports - deshalb wird vorher aufgeraeumt.
    /// </summary>
    private void StopExistingInstances(FileInfo executable)
    {
        string processName = Path.GetFileNameWithoutExtension(executable.Name);

        Process[] running;
        try { running = Process.GetProcessesByName(processName); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Laufende StockTV-Instanzen konnten nicht ermittelt werden.");
            return;
        }

        foreach (var process in running)
        {
            using (process)
            {
                try
                {
                    logger.LogWarning(
                        "Beende bereits laufende StockTV-Instanz (PID {Pid}) wegen der NetMQ-Ports 4747/4748.",
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
    }

    private FileInfo? ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            var file = new FileInfo(options.ExecutablePath);
            if (file.Exists)
                return file;

            logger.LogError("Kiosk:BlazorHost:ExecutablePath existiert nicht: {Path}", file.FullName);
            return null;
        }

        string fileName = OperatingSystem.IsWindows()
            ? options.ExecutableName + ".exe"
            : options.ExecutableName;

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
