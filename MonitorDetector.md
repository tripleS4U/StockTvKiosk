Kurz zur Einordnung, bevor du den Code liest: „Erweitert vs. Duplizieren" ist auf jeder Plattform ein anderes Konzept, 
deshalb gibt's keine einheitliche API dafür.

Windows – Zuverlässig geht das über die CCD-API (QueryDisplayConfig aus user32.dll). 
Jeder aktive Anzeigepfad hat eine „Source" (Grafikquelle): 
Im Extend-Modus bekommt jeder Monitor eine eigene Source, im Duplicate-Modus teilen sich mehrere Pfade dieselbe. 
Daraus lässt sich der Modus zuverlässig ableiten – deutlich robuster als die simple Screen.AllScreens-Prüfung aus WinForms, die nur die Bounds vergleicht.

Linux mit Desktop (X11) – Hier gibt's keine offizielle .NET-API, aber xrandr --query liefert alles: verbundene Ausgänge und deren 
Position im virtuellen Bildschirmlayout. Gleiche Position (+0+0 bei beiden) = Duplicate, unterschiedliche Position = Extended.

Raspberry Pi OS Lite (dein Fall) – Das ist der wichtige Punkt: Lite hat standardmäßig keine grafische Oberfläche, 
also läuft weder X11 noch Wayland. Ohne Compositor gibt es softwareseitig gar kein „erweiterter Desktop"-Konzept – das ist reine Fenstersystem-Logik. 
Was du auf Lite feststellen kannst, ist nur die physische Verbindung, über /sys/class/drm/*/status (funktioniert auch headless, ohne laufenden Grafikserver). 
Wenn du auf dem Pi wirklich einen erweiterten Desktop brauchst, müsstest du zusätzlich einen minimalen Wayland-Compositor (z. B. labwc oder wayfire) 
oder X11 installieren und laufen lassen – dann greift derselbe xrandr-Zweig wie bei jedem Linux-Desktop.

Der Code in MonitorDetector.cs deckt alle drei Fälle automatisch ab (OperatingSystem.IsWindows() / IsLinux() + Prüfung ob xrandr verfügbar ist). 
Zwei Hinweise dazu:

Die Windows-Structs (DISPLAYCONFIG_*) sind per P/Invoke nachgebaut – die Größen stimmen mit der Doku überein, 
aber ich konnte das hier nicht auf echter Windows-Hardware kompilieren/testen. Einmal gegenprüfen, bevor es produktiv läuft.
Für Raspberry Pi OS Lite git es standardmäßig kein xrandr – das ist beabsichtigt, 
der Code fällt dann automatisch auf die DRM-sysfs-Variante zurück.



Für deinen Anwendungsfall (Pi 4, zwei Monitore, C#/.NET, Modus-Erkennung) empfehle ich:

Raspberry Pi OS (64-bit) mit Desktop – nicht die Lite-Version.

Warum nicht Lite:
Du müsstest, wie im letzten Schritt beschrieben, den Compositor (labwc/wayfire), Autologin, Monitor-Layout etc. alles von Hand zusammenbauen und pflegen. Das volle Desktop-Image bringt das alles schon fertig konfiguriert mit – inklusive korrekt eingerichteter Dual-HDMI-Unterstützung out of the box, ohne dass du in der config.txt HDMI-Modi von Hand setzen musst.

Ein wichtiger Kniff, den dir Arbeit spart:
Seit Bookworm (aktuelles Raspberry Pi OS) läuft der Desktop standardmäßig auf Wayland (mit labwc), nicht mehr X11. Dein bereits geschriebener C#-Code nutzt aber xrandr, was ein reines X11-Tool ist – auf Wayland würde er also in den DRM-Sysfs-Fallback rutschen und dir keinen Extend/Duplicate-Status mehr liefern.

Du kannst das einfach umgehen, indem du in raspi-config → Advanced Options → Wayland zurück auf X11 stellst. Dann läuft der Desktop klassisch mit xrandr, dein Code funktioniert unverändert, und du hast trotzdem sofort verfügbaren Dual-Monitor-Support ohne manuelle Compositor-Installation.