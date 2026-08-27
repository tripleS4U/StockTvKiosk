using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace StockTvKiosk.Core.Control;

/// <summary>
/// Fuehrt Befehle als eigenen Prozess aus und sammelt deren Ausgaben ein. Die Ausgaben werden
/// gebraucht, nicht nur protokolliert: scheitert xrandr oder shutdown, steht der Grund in
/// stderr und gehoert in die Antwort der Schnittstelle.
/// </summary>
public sealed class ProcessCommandRunner(ILogger<ProcessCommandRunner>? logger = null) : ICommandRunner
{
    private readonly ILogger _logger = logger ?? NullLogger<ProcessCommandRunner>.Instance;

    public async Task<CommandOutcome> RunAsync(
        CommandSpec command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo(command.FileName, command.Arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Fuehre aus: {Command}", command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Prozess konnte nicht gestartet werden: {command}");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Zeitueberschreitung: der Prozess haengt. Aufraeumen, damit kein Rest liegen bleibt.
            try { process.Kill(entireProcessTree: true); } catch { /* bestmoeglich */ }

            _logger.LogWarning("Zeitueberschreitung nach {Seconds:0.#} s: {Command}", timeout.TotalSeconds, command);
            return new CommandOutcome(-1, string.Empty, string.Empty, TimedOut: true);
        }

        var outcome = new CommandOutcome(
            process.ExitCode,
            await SafeRead(stdout),
            await SafeRead(stderr),
            TimedOut: false);

        if (outcome.Success)
            _logger.LogInformation("Erledigt: {Command}", command);
        else
            _logger.LogWarning("Fehlgeschlagen: {Command} - {Outcome}", command, outcome.Describe());

        return outcome;
    }

    /// <summary>
    /// Die Ausgabe ist Beiwerk: steht sie nicht zur Verfuegung, darf das nicht den ganzen
    /// Aufruf scheitern lassen, dessen Exitcode bereits feststeht.
    /// </summary>
    private static async Task<string> SafeRead(Task<string> reader)
    {
        try { return await reader; }
        catch (Exception) { return string.Empty; }
    }
}
