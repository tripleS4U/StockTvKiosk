using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.Core.Control;

public interface IDisplayTopologyController
{
    /// <summary>Schaltet die Bildschirme um. Ordnet die Kiosk-Fenster NICHT neu an - das macht die Anwendung.</summary>
    Task<ControlResult> ApplyAsync(DisplayTopology topology, CancellationToken cancellationToken = default);
}

/// <summary>
/// Schaltet zwischen "Duplizieren" und "Erweitern" um.
/// </summary>
/// <remarks>
/// Unter Windows ueber die CCD-API, sonst ueber einen Befehl (xrandr bzw. was konfiguriert
/// ist). Die CCD-API ist der bessere Weg, weil sie einen Fehlercode liefert - DisplaySwitch.exe
/// meldet auch dann Erfolg, wenn nichts passiert ist. Genau deshalb ist sie der erste Versuch
/// und DisplaySwitch nur die Rueckfallebene.
/// </remarks>
public sealed class DisplayTopologyController(
    DisplayControlOptions options,
    ICommandRunner runner,
    IDisplayModeDetector detector,
    PlatformInfo platform,
    ILogger<DisplayTopologyController>? logger = null) : IDisplayTopologyController
{
    private readonly ILogger _logger = logger ?? NullLogger<DisplayTopologyController>.Instance;

    public async Task<ControlResult> ApplyAsync(
        DisplayTopology topology, CancellationToken cancellationToken = default)
    {
        if (options.Mode == ControlMode.Disabled)
        {
            return ControlResult.Disabled(
                "Das Umschalten der Bildschirme ist abgeschaltet (KioskOptions > DisplayControl > Mode = Disabled).");
        }

        // OperatingSystem.IsWindows() steht bewusst mit dabei: nur daran erkennt der
        // Plattform-Analyzer, dass der Aufruf der Windows-API abgesichert ist.
        if (platform.Kind == PlatformKind.Windows && options.Mode == ControlMode.Auto && OperatingSystem.IsWindows())
        {
            int code = WindowsDisplayTopology.Apply(topology);
            if (code == 0)
            {
                _logger.LogInformation("Bildschirme auf {Topology} umgeschaltet (CCD-API).", topology);
                return ControlResult.Ok($"Bildschirme auf {Name(topology)} umgeschaltet.", "Windows CCD");
            }

            _logger.LogWarning(
                "CCD-API konnte nicht auf {Topology} umschalten: {Reason}",
                topology, WindowsDisplayTopology.Describe(code));

            var fallback = await RunPlanAsync(topology, cancellationToken);

            // Der Rueckfall hat es auch nicht geschafft: dann ist der CCD-Fehlercode die
            // aussagekraeftigere Meldung, denn DisplaySwitch sagt zur Ursache nichts.
            return fallback.Success
                ? fallback
                : ControlResult.Failed(
                    $"Umschalten auf {Name(topology)} fehlgeschlagen: {WindowsDisplayTopology.Describe(code)}.",
                    fallback.Detail ?? fallback.Message);
        }

        return await RunPlanAsync(topology, cancellationToken);
    }

    private async Task<ControlResult> RunPlanAsync(DisplayTopology topology, CancellationToken cancellationToken)
    {
        var detected = Detect();
        var plan = DisplayCommandResolver.Resolve(topology, platform, options, detected);

        if (!plan.Success)
        {
            _logger.LogWarning("Umschalten auf {Topology} abgelehnt: {Reason}", topology, plan.Message);
            return plan.ToFailure();
        }

        try
        {
            var outcome = await runner.RunAsync(
                plan.Command!, TimeSpan.FromSeconds(options.CommandTimeoutSeconds), cancellationToken);

            if (outcome.Success)
            {
                _logger.LogInformation("Bildschirme auf {Topology} umgeschaltet: {Command}", topology, plan.Command);
                return ControlResult.Ok($"Bildschirme auf {Name(topology)} umgeschaltet.", plan.Message);
            }

            return ControlResult.Failed(
                $"Umschalten auf {Name(topology)} fehlgeschlagen.", outcome.Describe());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Umschalten auf {Topology} fehlgeschlagen ({Command}).", topology, plan.Command);
            return ControlResult.Failed(
                $"Umschalten auf {Name(topology)} fehlgeschlagen.", ex.Message);
        }
    }

    /// <summary>
    /// Die Erkennung liefert die xrandr-Ausgangsnamen. Scheitert sie, wird trotzdem
    /// weitergemacht: sind die Namen konfiguriert, braucht es sie gar nicht.
    /// </summary>
    private DisplayModeInfo Detect()
    {
        try
        {
            return detector.Detect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bildschirme konnten vor dem Umschalten nicht erkannt werden.");
            return new DisplayModeInfo { Mode = DisplayMode.Unknown, Source = "fehlgeschlagen" };
        }
    }

    private static string Name(DisplayTopology topology)
        => topology == DisplayTopology.Duplicate ? "Duplizieren" : "Erweitern";
}
