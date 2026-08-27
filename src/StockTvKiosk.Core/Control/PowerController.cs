using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.Core.Control;

/// <summary>Ein vorgemerkter, noch nicht ausgefuehrter Neustart bzw. Shutdown.</summary>
public sealed record PowerPending(PowerAction Action, DateTimeOffset DueUtc);

public interface IPowerController
{
    /// <summary>Merkt die Aktion vor und kehrt sofort zurueck - siehe <see cref="PowerController"/>.</summary>
    ControlResult Schedule(PowerAction action, int? delaySeconds, bool force);

    /// <summary>Nimmt eine vorgemerkte Aktion zurueck, solange sie noch nicht ausgefuehrt wurde.</summary>
    ControlResult Cancel();

    PowerPending? Pending { get; }
}

/// <summary>
/// Startet den Rechner neu bzw. faehrt ihn herunter.
/// </summary>
/// <remarks>
/// Die Aktion wird immer im Hintergrund ausgefuehrt, auch ohne Verzoegerung: der Aufrufer
/// bekommt sonst keine Antwort mehr, weil der Rechner mitten im Schreiben der Antwort
/// weggeht. Gewartet wird ausserdem in der Anwendung statt ueber <c>shutdown /t</c> - systemd
/// kennt keine Verzoegerung, und so verhaelt sich beides gleich und laesst sich zuruecknehmen.
/// </remarks>
public sealed class PowerController : IPowerController
{
    /// <summary>
    /// Vorlauf, damit die HTTP-Antwort den Aufrufer noch erreicht, bevor der Rechner
    /// tatsaechlich weggeht.
    /// </summary>
    private static readonly TimeSpan ResponseGrace = TimeSpan.FromMilliseconds(750);

    private readonly PowerOptions _options;
    private readonly ICommandRunner _runner;
    private readonly PlatformKind _platform;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _pendingSource;
    private PowerPending? _pending;

    public PowerController(
        PowerOptions options,
        ICommandRunner runner,
        PlatformKind platform,
        ILogger<PowerController>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _platform = platform;
        _logger = logger ?? NullLogger<PowerController>.Instance;
    }

    public PowerPending? Pending
    {
        get { lock (_gate) return _pending; }
    }

    public ControlResult Schedule(PowerAction action, int? delaySeconds, bool force)
    {
        int delay = delaySeconds ?? _options.DefaultDelaySeconds;

        if (delay < 0)
            return ControlResult.InvalidRequest("Die Verzoegerung darf nicht negativ sein.");

        if (delay > _options.MaxDelaySeconds)
        {
            return ControlResult.InvalidRequest(
                $"Die Verzoegerung ueberschreitet das erlaubte Maximum von {_options.MaxDelaySeconds} s " +
                "(KioskOptions > Power > MaxDelaySeconds).");
        }

        var plan = PowerCommandResolver.Resolve(action, _platform, _options, force);
        if (!plan.Success)
        {
            _logger.LogWarning("{Action} abgelehnt: {Reason}", action, plan.Message);
            return plan.ToFailure();
        }

        lock (_gate)
        {
            if (_pending is { } existing)
            {
                return ControlResult.Conflict(
                    $"Es ist bereits {Describe(existing.Action)} fuer {existing.DueUtc:HH:mm:ss} UTC vorgemerkt. " +
                    "Erst mit cancel-shutdown zuruecknehmen.");
            }

            var source = new CancellationTokenSource();
            _pendingSource = source;
            _pending = new PowerPending(action, DateTimeOffset.UtcNow.AddSeconds(delay).Add(ResponseGrace));

            _ = ExecuteAfterDelayAsync(action, plan.Command!, TimeSpan.FromSeconds(delay), source.Token);
        }

        _logger.LogWarning("{Action} vorgemerkt in {Delay} s ueber {Command}.", action, delay, plan.Command);

        return ControlResult.Ok(
            delay == 0
                ? $"{Describe(action)} wird jetzt ausgefuehrt."
                : $"{Describe(action)} wird in {delay} s ausgefuehrt.",
            plan.Message);
    }

    public ControlResult Cancel()
    {
        CancellationTokenSource? source;
        PowerPending? pending;

        lock (_gate)
        {
            source = _pendingSource;
            pending = _pending;
            _pendingSource = null;
            _pending = null;
        }

        if (source is null || pending is null)
            return ControlResult.InvalidRequest("Es ist nichts vorgemerkt, das sich zuruecknehmen liesse.");

        source.Cancel();
        source.Dispose();

        _logger.LogInformation("{Action} zurueckgenommen.", pending.Action);
        return ControlResult.Ok($"{Describe(pending.Action)} wurde zurueckgenommen.");
    }

    private async Task ExecuteAfterDelayAsync(
        PowerAction action, CommandSpec command, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay + ResponseGrace, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var outcome = await _runner.RunAsync(
                command, TimeSpan.FromSeconds(_options.CommandTimeoutSeconds), cancellationToken);

            if (outcome.Success)
            {
                _logger.LogWarning("{Action} ausgeloest.", action);
                return;
            }

            // Ab hier gibt es niemanden mehr, der eine Antwort bekaeme - der Aufrufer hat seine
            // laengst. Bleibt nur das Log, und dort muss der Grund vollstaendig stehen.
            _logger.LogError("{Action} fehlgeschlagen: {Outcome}", action, outcome.Describe());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} konnte nicht ausgefuehrt werden ({Command}).", action, command);
        }
        finally
        {
            lock (_gate)
            {
                _pendingSource = null;
                _pending = null;
            }
        }
    }

    private static string Describe(PowerAction action)
        => action == PowerAction.Reboot ? "Der Neustart" : "Das Herunterfahren";
}
