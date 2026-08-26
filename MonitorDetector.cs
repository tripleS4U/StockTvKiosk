// MonitorDetector.cs
// Erkennt angeschlossene Monitore und (soweit möglich) ob der Anzeigemodus
// "Erweitert" (Extend) oder "Duplizieren" (Clone) ist.
//
// Plattformen:
//  - Windows: über die CCD-API (QueryDisplayConfig) -> zuverlässig Extend/Clone erkennbar
//  - Linux Desktop (X11, z.B. Raspberry Pi OS Desktop): über "xrandr --query"
//  - Linux headless / Raspberry Pi OS Lite: über /sys/class/drm/*/status
//    -> hier gibt es KEIN Betriebssystem-Konzept "erweiterter Desktop",
//       da ohne laufenden X11/Wayland-Compositor kein Fenstersystem existiert,
//       das mehrere Bildschirme zu einer Desktopfläche zusammenfügt.
//       Es kann nur "angeschlossen / nicht angeschlossen" ermittelt werden.
//
// Zielframework: net8.0 (funktioniert plattformübergreifend, kein WinForms nötig)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DisplayDetector
{
    public enum DisplayMode
    {
        Unknown,
        SingleMonitor,
        Extended,
        Duplicated
    }

    public class DisplayInfo
    {
        public int MonitorCount { get; set; }
        public DisplayMode Mode { get; set; }
        public List<string> Details { get; } = new();

        public override string ToString() =>
            $"Monitore: {MonitorCount}, Modus: {Mode}\n  " + string.Join("\n  ", Details);
    }

    public static class MonitorDetector
    {
        public static DisplayInfo Detect()
        {
            if (OperatingSystem.IsWindows())
                return DetectWindows();

            if (OperatingSystem.IsLinux())
                return DetectLinux();

            throw new PlatformNotSupportedException("Nur Windows und Linux werden unterstützt.");
        }

        // ==================== WINDOWS ====================
        private static DisplayInfo DetectWindows()
        {
            var info = new DisplayInfo();

            int pathCount, modeCount;
            uint err = GetDisplayConfigBufferSizes(QDC.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
            if (err != 0)
                throw new InvalidOperationException($"GetDisplayConfigBufferSizes fehlgeschlagen (Code {err}).");

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            err = QueryDisplayConfig(QDC.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (err != 0)
                throw new InvalidOperationException($"QueryDisplayConfig fehlgeschlagen (Code {err}).");

            var activePaths = paths.Take(pathCount)
                                    .Where(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0)
                                    .ToList();

            info.MonitorCount = activePaths.Count;

            // Im Extend-Modus bekommt jeder Monitor eine eigene "Source" (Grafikquelle).
            // Im Duplicate/Clone-Modus teilen sich mehrere Pfade dieselbe Source.
            var distinctSources = activePaths
                .Select(p => (p.sourceInfo.adapterId.LowPart, p.sourceInfo.adapterId.HighPart, p.sourceInfo.id))
                .Distinct()
                .Count();

            info.Mode = activePaths.Count <= 1 ? DisplayMode.SingleMonitor
                       : distinctSources == activePaths.Count ? DisplayMode.Extended
                       : DisplayMode.Duplicated;

            info.Details.Add($"Aktive Pfade: {activePaths.Count}, unterschiedliche Grafikquellen: {distinctSources}");
            return info;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDisplayConfigBufferSizes(QDC flags, out int numPathArrayElements, out int numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern uint QueryDisplayConfig(QDC flags, ref int numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref int numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        private enum QDC : uint
        {
            QDC_ALL_PATHS = 0x1,
            QDC_ONLY_ACTIVE_PATHS = 0x2,
            QDC_DATABASE_CURRENT = 0x4
        }

        private const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

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

        // Größe = 64 Byte (dokumentiert). Der Inhalt der Union wird hier nicht benötigt,
        // nur die korrekte Struktur-/Array-Größe für den API-Aufruf.
        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            private long dummy1, dummy2, dummy3, dummy4, dummy5, dummy6;
        }

        // ==================== LINUX ====================
        private static DisplayInfo DetectLinux()
        {
            if (IsXrandrAvailable())
                return DetectLinuxXrandr();

            // Kein X-Server aktiv -> vermutlich headless, z.B. Raspberry Pi OS Lite
            return DetectLinuxDrmSysfs();
        }

        private static bool IsXrandrAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("xrandr", "--query")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(2000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static DisplayInfo DetectLinuxXrandr()
        {
            var info = new DisplayInfo();
            var psi = new ProcessStartInfo("xrandr", "--query")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            var connectedLines = output.Split('\n')
                .Where(l => l.Contains(" connected"))
                .ToList();

            info.MonitorCount = connectedLines.Count;
            info.Details.AddRange(connectedLines);

            if (connectedLines.Count <= 1)
            {
                info.Mode = DisplayMode.SingleMonitor;
                return info;
            }

            // Position im Layout extrahieren, z. B. "1920x1080+0+0" -> Offset (0,0)
            var positions = connectedLines
                .Select(l =>
                {
                    var m = Regex.Match(l, @"\d+x\d+\+(\d+)\+(\d+)");
                    return m.Success ? (x: m.Groups[1].Value, y: m.Groups[2].Value) : ("", "");
                })
                .ToList();

            bool anySamePosition = positions.Count != positions.Distinct().Count();
            info.Mode = anySamePosition ? DisplayMode.Duplicated : DisplayMode.Extended;
            return info;
        }

        // Headless-Fall, z. B. Raspberry Pi OS Lite (kein X11/Wayland läuft)
        private static DisplayInfo DetectLinuxDrmSysfs()
        {
            var info = new DisplayInfo();
            const string drmPath = "/sys/class/drm";

            if (!Directory.Exists(drmPath))
            {
                info.Mode = DisplayMode.Unknown;
                info.Details.Add("Kein /sys/class/drm gefunden – KMS/DRM nicht verfügbar.");
                return info;
            }

            var statusFiles = Directory.GetDirectories(drmPath)
                .Where(d => Path.GetFileName(d).Contains("-")) // z.B. card1-HDMI-A-1
                .Select(d => Path.Combine(d, "status"))
                .Where(File.Exists);

            int connected = 0;
            foreach (var file in statusFiles)
            {
                string status = File.ReadAllText(file).Trim();
                string name = Path.GetFileName(Path.GetDirectoryName(file))!;
                info.Details.Add($"{name}: {status}");
                if (status == "connected")
                    connected++;
            }

            info.MonitorCount = connected;

            // Wichtig: ohne laufenden Compositor gibt es softwareseitig keinen "erweiterten
            // Desktop" – nur die physische Verbindung ist feststellbar.
            info.Mode = connected > 1 ? DisplayMode.Unknown : DisplayMode.SingleMonitor;
            if (connected > 1)
                info.Details.Add("Hinweis: Kein X11/Wayland aktiv -> 'Erweitert' ist ohne Compositor nicht definiert.");

            return info;
        }
    }

    internal class Program
    {
        static void Main()
        {
            var info = MonitorDetector.Detect();
            Console.WriteLine(info);
        }
    }
}
