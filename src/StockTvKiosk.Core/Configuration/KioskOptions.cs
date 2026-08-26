namespace StockTvKiosk.Core.Configuration;

/// <summary>Wurzelknoten von appsettings.json (Abschnitt "Kiosk").</summary>
public sealed class KioskOptions
{
    public const string SectionName = "Kiosk";

    public UrlOptions Urls { get; set; } = new();
    public DisplayOptions Display { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public BlazorHostOptions BlazorHost { get; set; } = new();
}

/// <summary>Die beiden Adressen der StockTV-Blazor-App.</summary>
public sealed class UrlOptions
{
    /// <summary>Bedienfenster - hier wird per Numpad getippt.</summary>
    public string PrimaryUrl { get; set; } = "http://localhost:5129/turnier";

    /// <summary>Gespiegelte Anzeige auf dem zweiten Bildschirm.</summary>
    public string SecondaryUrl { get; set; } = "http://localhost:5129/display2";

    /// <summary>Haengt an beide URLs "demo=true" an (entspricht -Demo in start-compiled.ps1).</summary>
    public bool Demo { get; set; }
}

/// <summary>Verhalten, wenn nur ein einziger nutzbarer Bildschirm zur Verfuegung steht.</summary>
public enum SingleScreenFallback
{
    /// <summary>Beide Fenster nebeneinander auf den Hauptbildschirm legen (wie -SecondScreen:$false).</summary>
    SideBySide,

    /// <summary>Nur das Hauptfenster oeffnen, kein Spiegel.</summary>
    PrimaryOnly
}

/// <summary>Wie ein Fenster den Bildschirm einnimmt.</summary>
public enum WindowMode
{
    /// <summary>Randlos auf exakte Position und Groesse gesetzt. Zuverlaessig unter Windows und X11.</summary>
    Borderless,

    /// <summary>Zusaetzlich WindowState=FullScreen. Auf manchen Compositors robuster, dafuer
    /// laesst sich der Zielbildschirm dort nicht mehr erzwingen.</summary>
    FullScreen
}

public sealed class DisplayOptions
{
    /// <summary>Zweites Fenster (/display2) oeffnen. Entspricht -Dual.</summary>
    public bool Dual { get; set; } = true;

    /// <summary>Das Spiegel-Fenster auf den zweiten Bildschirm legen. Entspricht -SecondScreen.</summary>
    public bool UseSecondScreen { get; set; } = true;

    /// <summary>0-basierter Bildschirmindex fuers Hauptfenster; -1 = automatisch (System-Primary).</summary>
    public int MainDisplayIndex { get; set; } = -1;

    /// <summary>0-basierter Bildschirmindex fuers Spiegel-Fenster; -1 = automatisch (erster anderer).</summary>
    public int MirrorDisplayIndex { get; set; } = -1;

    public SingleScreenFallback SingleScreenFallback { get; set; } = SingleScreenFallback.SideBySide;

    public WindowMode WindowMode { get; set; } = WindowMode.Borderless;

    /// <summary>
    /// WorkingArea (ohne Taskleiste/Panel) statt der vollen Bounds verwenden.
    /// Fuer echten Kiosk-Betrieb normalerweise false.
    /// </summary>
    public bool UseWorkingArea { get; set; }

    /// <summary>
    /// Bei erkanntem Modus "Duplizieren" so behandeln, als gaebe es nur einen Bildschirm.
    /// Andernfalls wird trotzdem versucht, auf den gemeldeten zweiten Bildschirm zu positionieren.
    /// </summary>
    public bool TreatDuplicatedAsSingleScreen { get; set; } = true;

    /// <summary>Cursor in den Kiosk-Fenstern ausblenden.</summary>
    public bool HideCursor { get; set; } = true;

    /// <summary>Kiosk mit Alt+F4 / Esc beendbar halten (fuer Tests). Im Feldbetrieb auf false.</summary>
    public bool AllowExitShortcut { get; set; } = true;
}

public sealed class RetryOptions
{
    /// <summary>Automatisch neu laden, solange die URL nicht erreichbar ist.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Wartezeit zwischen zwei Versuchen.</summary>
    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Lokale Fehlerseite anzeigen, statt die Browser-Standardfehlerseite stehen zu lassen.</summary>
    public bool ShowErrorPage { get; set; } = true;

    /// <summary>Timeout des HTTP-Erreichbarkeitschecks.</summary>
    public int ProbeTimeoutSeconds { get; set; } = 3;
}

/// <summary>
/// Optionales Prozess-Management fuer StockTvBlazor. Ist <see cref="Enabled"/> false, muss die
/// Blazor-App extern gestartet werden (Skript, systemd, Autostart) - der Kiosk wartet dann nur,
/// bis die URL antwortet.
/// </summary>
public sealed class BlazorHostOptions
{
    /// <summary>StockTvBlazor selbst starten und beim Beenden mit herunterfahren.</summary>
    public bool Enabled { get; set; }

    /// <summary>Vollstaendiger Pfad zur ausfuehrbaren Datei. Leer = im SearchRoot suchen.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Dateiname ohne Endung, nach dem gesucht wird.</summary>
    public string ExecutableName { get; set; } = "StockTvBlazor";

    /// <summary>Wurzelverzeichnis fuer die Suche. Leer = Verzeichnis der Kiosk-Anwendung.</summary>
    public string? SearchRoot { get; set; }

    public string? Arguments { get; set; }

    /// <summary>Arbeitsverzeichnis. Leer = Verzeichnis der gefundenen exe.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Zusaetzliche Umgebungsvariablen, z. B. ASPNETCORE_URLS / ASPNETCORE_ENVIRONMENT.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>
    /// Eine bereits laufende Instanz vorher beenden. StockTV bindet NetMQ auf 4747/4748,
    /// diese Ports duerfen nur einmal belegt sein.
    /// </summary>
    public bool StopExistingInstance { get; set; } = true;

    /// <summary>Maximale Wartezeit, bis die App auf der Primaer-URL antwortet.</summary>
    public int ReadinessTimeoutSeconds { get; set; } = 60;

    /// <summary>Beim Schliessen des Kiosks auch den gestarteten Blazor-Prozess beenden.</summary>
    public bool ShutdownWithKiosk { get; set; } = true;

    /// <summary>Verzeichnis fuer stdout/stderr des Blazor-Prozesses. Leer = keine Umleitung.</summary>
    public string? LogDirectory { get; set; }
}
