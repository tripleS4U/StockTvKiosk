using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace StockTvKiosk.Core.Display;

/// <summary>
/// Erkennt, ob die angeschlossenen Monitore im Modus "Erweitert" oder "Duplizieren"
/// betrieben werden. Avalonias <c>Screens</c>-API kann das nicht unterscheiden, fuer den
/// Kiosk-Betrieb ist es aber entscheidend: bei "Duplizieren" waere ein zweites Fenster auf
/// "Bildschirm 2" sinnlos, weil beide Ausgaenge dasselbe Bild zeigen.
///
/// Plattformen:
///  - Windows: CCD-API (QueryDisplayConfig) -> zuverlaessig, da im Extend-Modus jeder Monitor
///    eine eigene Grafikquelle ("Source") hat, im Clone-Modus mehrere Pfade sich eine teilen.
///  - Linux Desktop (X11): "xrandr --query" -> gleiche Position im Layout = Duplicate.
///  - Linux headless / Wayland ohne xrandr: /sys/class/drm/*/status -> nur "angeschlossen";
///    ein Betriebssystem-Konzept "erweiterter Desktop" existiert ohne Compositor nicht.
/// </summary>
public sealed class MonitorDetector : IDisplayModeDetector
{
    private readonly ILogger<MonitorDetector> _logger;

    public MonitorDetector(ILogger<MonitorDetector>? logger = null)
        => _logger = logger ?? NullLogger<MonitorDetector>.Instance;

    public DisplayModeInfo Detect()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return DetectWindows();

            if (OperatingSystem.IsLinux())
                return DetectLinux();

            return new DisplayModeInfo
            {
                Mode = DisplayMode.Unknown,
                Source = RuntimeInformation.OSDescription,
                Details = ["Anzeigemodus-Erkennung ist nur fuer Windows und Linux implementiert."]
            };
        }
        catch (Exception ex)
        {
            // Der Kiosk darf an einer fehlgeschlagenen Modus-Erkennung nicht scheitern:
            // die Fenster werden dann rein anhand der Avalonia-Screens platziert.
            _logger.LogWarning(ex, "Anzeigemodus konnte nicht ermittelt werden.");
            return new DisplayModeInfo
            {
                Mode = DisplayMode.Unknown,
                Source = "fehlgeschlagen",
                Details = [ex.Message]
            };
        }
    }

    // ==================== WINDOWS ====================

    [SupportedOSPlatform("windows")]
    private static DisplayModeInfo DetectWindows()
    {
        uint err = GetDisplayConfigBufferSizes(QDC.OnlyActivePaths, out int pathCount, out int modeCount);
        if (err != ErrorSuccess)
        {
            return new DisplayModeInfo
            {
                Mode = DisplayMode.Unknown,
                Source = "Windows CCD",
                Details = [$"GetDisplayConfigBufferSizes fehlgeschlagen (Code {err})."]
            };
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        err = QueryDisplayConfig(QDC.OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != ErrorSuccess)
        {
            return new DisplayModeInfo
            {
                Mode = DisplayMode.Unknown,
                Source = "Windows CCD",
                Details = [$"QueryDisplayConfig fehlgeschlagen (Code {err})."]
            };
        }

        var activePaths = paths.Take(pathCount)
                               .Where(p => (p.flags & DisplayConfigPathActive) != 0)
                               .ToList();

        // Im Extend-Modus bekommt jeder Monitor eine eigene Grafikquelle,
        // im Duplicate-Modus teilen sich mehrere Pfade dieselbe.
        int distinctSources = activePaths
            .Select(p => (p.sourceInfo.adapterId.LowPart, p.sourceInfo.adapterId.HighPart, p.sourceInfo.id))
            .Distinct()
            .Count();

        var mode = activePaths.Count <= 1 ? DisplayMode.SingleMonitor
                 : distinctSources == activePaths.Count ? DisplayMode.Extended
                 : DisplayMode.Duplicated;

        return new DisplayModeInfo
        {
            MonitorCount = activePaths.Count,
            Mode = mode,
            Source = "Windows CCD",
            Details = [$"Aktive Anzeigepfade: {activePaths.Count}, unterschiedliche Grafikquellen: {distinctSources}"]
        };
    }

    private const uint ErrorSuccess = 0;
    private const uint DisplayConfigPathActive = 0x00000001;

    private enum QDC : uint
    {
        AllPaths = 0x1,
        OnlyActivePaths = 0x2,
        DatabaseCurrent = 0x4
    }

    [DllImport("user32.dll")]
    private static extern uint GetDisplayConfigBufferSizes(
        QDC flags, out int numPathArrayElements, out int numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern uint QueryDisplayConfig(
        QDC flags,
        ref int numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref int numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // Dokumentierte Groesse: 64 Byte (infoType 4 + id 4 + adapterId 8 + Union 48).
    // Der Union-Inhalt wird hier nicht gebraucht, nur die korrekte Array-Groesse fuer den
    // API-Aufruf. Die Groessen werden von den Unit-Tests abgesichert.
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
#pragma warning disable CS0169 // Nie verwendet - dient allein der korrekten Struct-Groesse.
        private readonly long _union1, _union2, _union3, _union4, _union5, _union6;
#pragma warning restore CS0169
    }

    /// <summary>Nur fuer Tests: tatsaechliche Groessen der P/Invoke-Structs in Byte.</summary>
    internal static (int PathInfo, int ModeInfo) NativeStructSizes()
        => (Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>(), Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>());

    // ==================== LINUX ====================

    private DisplayModeInfo DetectLinux()
    {
        bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        if (TryRunXrandr(out string xrandrOutput))
            return ParseXrandr(xrandrOutput, wayland);

        if (wayland)
            _logger.LogInformation("Wayland-Sitzung ohne nutzbares xrandr erkannt - falle auf DRM-sysfs zurueck.");

        // Kein X-Server bzw. kein XWayland-xrandr -> nur die physische Verbindung ist feststellbar.
        return DetectLinuxDrmSysfs(wayland);
    }

    private static bool TryRunXrandr(out string output)
    {
        output = string.Empty;
        try
        {
            var psi = new ProcessStartInfo("xrandr", "--query")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* bestmoeglich */ }
                return false;
            }

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            // xrandr nicht installiert (z. B. Raspberry Pi OS Lite) oder kein DISPLAY gesetzt.
            return false;
        }
    }

    // "HDMI-1 connected primary 1920x1080+0+0 (normal ...) 598mm x 336mm"
    // "HDMI-2 connected (normal ...)"  <- angeschlossen, aber NICHT aktiv (keine Geometrie)
    private static readonly Regex ConnectedLine =
        new(@"^(?<name>\S+)\s+connected\b", RegexOptions.Compiled);

    private static readonly Regex Geometry =
        new(@"(?<w>\d+)x(?<h>\d+)\+(?<x>-?\d+)\+(?<y>-?\d+)", RegexOptions.Compiled);

    internal static DisplayModeInfo ParseXrandr(string output, bool wayland = false)
    {
        var details = new List<string>();
        var active = new List<(string Name, int X, int Y)>();
        int connected = 0;

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            var match = ConnectedLine.Match(line);
            if (!match.Success)
                continue;

            connected++;
            string name = match.Groups["name"].Value;
            var geo = Geometry.Match(line);

            if (geo.Success)
            {
                active.Add((name,
                            int.Parse(geo.Groups["x"].Value),
                            int.Parse(geo.Groups["y"].Value)));
                details.Add(line.Trim());
            }
            else
            {
                // Angeschlossen, aber kein Modus gesetzt. Solche Ausgaenge duerfen NICHT in den
                // Positionsvergleich einflieszen - sonst gelten zwei inaktive Ausgaenge (beide
                // ohne Geometrie) faelschlich als "gleiche Position" und damit als Duplicate.
                details.Add($"{name}: angeschlossen, aber nicht aktiv (kein Modus gesetzt)");
            }
        }

        details.Insert(0, $"Angeschlossen: {connected}, davon aktiv: {active.Count}");
        if (wayland)
            details.Insert(1, "Hinweis: Wayland-Sitzung - xrandr zeigt ggf. nur die XWayland-Sicht.");

        DisplayMode mode;
        if (active.Count <= 1)
        {
            mode = DisplayMode.SingleMonitor;
        }
        else
        {
            // Gleiche Position im virtuellen Layout = beide zeigen denselben Ausschnitt.
            int distinctPositions = active.Select(a => (a.X, a.Y)).Distinct().Count();
            mode = distinctPositions < active.Count ? DisplayMode.Duplicated : DisplayMode.Extended;
        }

        return new DisplayModeInfo
        {
            MonitorCount = active.Count,
            Mode = mode,
            Source = "xrandr",
            Details = details
        };
    }

    private static DisplayModeInfo DetectLinuxDrmSysfs(bool wayland)
    {
        const string drmPath = "/sys/class/drm";
        var details = new List<string>();

        if (!Directory.Exists(drmPath))
        {
            return new DisplayModeInfo
            {
                Mode = DisplayMode.Unknown,
                Source = "DRM sysfs",
                Details = ["Kein /sys/class/drm gefunden - KMS/DRM nicht verfuegbar."]
            };
        }

        int connected = 0;
        foreach (string dir in Directory.GetDirectories(drmPath).Order())
        {
            string name = Path.GetFileName(dir);
            if (!name.Contains('-')) // z. B. "card1-HDMI-A-1"; "card1" selbst ueberspringen
                continue;

            string statusFile = Path.Combine(dir, "status");
            if (!File.Exists(statusFile))
                continue;

            string status;
            try { status = File.ReadAllText(statusFile).Trim(); }
            catch (IOException) { continue; }

            details.Add($"{name}: {status}");
            if (status == "connected")
                connected++;
        }

        // Ohne laufenden Compositor gibt es softwareseitig keinen "erweiterten Desktop" -
        // nur die physische Verbindung ist feststellbar.
        var mode = connected <= 1 ? DisplayMode.SingleMonitor : DisplayMode.Unknown;
        if (connected > 1)
        {
            details.Add(wayland
                ? "Hinweis: Wayland ohne xrandr -> 'Erweitert/Duplizieren' nicht ermittelbar."
                : "Hinweis: Kein X11/Wayland aktiv -> 'Erweitert' ist ohne Compositor nicht definiert.");
        }

        return new DisplayModeInfo
        {
            MonitorCount = connected,
            Mode = mode,
            Source = "DRM sysfs",
            Details = details
        };
    }
}
