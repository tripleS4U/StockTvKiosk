using System.Runtime.InteropServices;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.Core.Control;

/// <summary>
/// Beantwortet vorab, was auf dem laufenden System ueberhaupt geht - damit niemand raten muss,
/// ob ein Endpunkt gleich mit "nicht unterstuetzt" antwortet.
/// </summary>
public static class CapabilityReporter
{
    public static SystemCapabilities Describe(
        PlatformInfo platform, KioskOptions options, DisplayModeInfo detected)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(detected);

        return new SystemCapabilities(
            Platform: platform.Describe(),
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            Kind: platform.Kind,
            IsWayland: platform.IsWayland,
            Power: DescribePower(platform, options.Power),
            Display: DescribeDisplay(platform, options.DisplayControl, detected));
    }

    private static CapabilityInfo DescribePower(PlatformInfo platform, PowerOptions options)
    {
        // Stellvertretend fuer beide Aktionen geprueft: Neustart und Herunterfahren gehen
        // ueber denselben Weg und scheitern deshalb auch gemeinsam.
        var plan = PowerCommandResolver.Resolve(PowerAction.Reboot, platform.Kind, options, force: false);

        return plan.Success
            ? new CapabilityInfo(true, plan.Message)
            : new CapabilityInfo(false, plan.Message);
    }

    private static CapabilityInfo DescribeDisplay(
        PlatformInfo platform, DisplayControlOptions options, DisplayModeInfo detected)
    {
        if (options.Mode == ControlMode.Disabled)
        {
            return new CapabilityInfo(false,
                "Abgeschaltet (KioskOptions > DisplayControl > Mode = Disabled).");
        }

        // Unter Windows im Auto-Modus laeuft das Umschalten ueber die CCD-API, nicht ueber einen
        // Befehl - der Resolver taugt dort also nicht als Auskunft.
        if (platform.Kind == PlatformKind.Windows && options.Mode == ControlMode.Auto)
            return new CapabilityInfo(true, "Ueber die CCD-API von Windows.");

        var plan = DisplayCommandResolver.Resolve(DisplayTopology.Extend, platform, options, detected);
        return new CapabilityInfo(plan.Success, plan.Message);
    }
}
