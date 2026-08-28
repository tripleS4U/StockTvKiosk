
namespace StockTvKiosk.Core.Configuration
{

    public class BlazorHostOptions
    {
        private bool _Enabled = false;
        [Description("Enable Blazor host")]
        public bool Enabled { get => _Enabled; set { if (!object.Equals(_Enabled, value)) { _Enabled = value; } } }

        private string? _ExecutablePath = null;
        [Description("Executable path")]
        public string? ExecutablePath { get => _ExecutablePath; set { if (!object.Equals(_ExecutablePath, value)) { _ExecutablePath = value; } } }

        private string _ExecutableName = "StockTvBlazor";
        [Description("Executable name")]
        public string ExecutableName { get => _ExecutableName; set { if (!object.Equals(_ExecutableName, value)) { _ExecutableName = value; } } }

        private string? _SearchRoot = null;
        [Description("Search root")]
        public string? SearchRoot { get => _SearchRoot; set { if (!object.Equals(_SearchRoot, value)) { _SearchRoot = value; } } }

        private string? _Arguments = null;
        [Description("Arguments")]
        public string? Arguments { get => _Arguments; set { if (!object.Equals(_Arguments, value)) { _Arguments = value; } } }

        private string? _WorkingDirectory = null;
        [Description("Working directory")]
        public string? WorkingDirectory { get => _WorkingDirectory; set { if (!object.Equals(_WorkingDirectory, value)) { _WorkingDirectory = value; } } }

        /// <summary>
        /// Zusaetzliche Umgebungsvariablen fuer den StockTV-Prozess. Standardmaessig leer.
        /// </summary>
        /// <remarks>
        /// <c>ASPNETCORE_URLS</c> steht hier bewusst nicht drin: der Wert wird aus
        /// <c>KioskOptions &gt; Urls &gt; PrimaryUrl</c> abgeleitet. Zweimal dieselbe Adresse
        /// zu pflegen geht irgendwann schief, und dann laedt der Kiosk von einem Port, auf dem
        /// StockTV gar nicht lauscht. Wer doch etwas anderes braucht - mehrere Adressen, eine
        /// andere Bindung -, traegt den Schluessel hier ein und gewinnt damit.
        ///
        /// <c>ASPNETCORE_ENVIRONMENT</c> fehlt aus demselben Grund: "Production" ist ohnehin
        /// die Vorgabe von ASP.NET. Fuer die echten Fehlermeldungen statt "An unhandled error
        /// has occurred" hier "Development" eintragen.
        /// </remarks>
        private Dictionary<string, string> _Environment = new Dictionary<string, string>(StringComparer.Ordinal);
        [Description("Extra environment variables; ASPNETCORE_URLS is derived from Urls > PrimaryUrl unless set here")]
        public Dictionary<string, string> Environment { get => _Environment; set { if (!object.Equals(_Environment, value)) { _Environment = value; } } }

        /// <summary>
        /// Was geschieht, wenn beim Start bereits eine StockTV-Instanz laeuft oder einer der
        /// Ports belegt ist: <c>false</c> (Vorgabe) laesst den Bestand in Ruhe und startet
        /// nichts - der Kiosk wartet dann einfach auf die URL. <c>true</c> raeumt die
        /// gefundenen Prozesse ab und startet eine eigene Instanz.
        /// </summary>
        /// <remarks>
        /// Die Vorgabe ist bewusst <c>false</c>: waehrend eines Turniers eine laufende Wertung
        /// abzuschiessen, weil der Kiosk neu gestartet wurde, waere der teurere Fehler.
        /// </remarks>
        private bool _StopExistingInstance = false;
        [Description("Stop an already running instance instead of leaving it alone")]
        public bool StopExistingInstance { get => _StopExistingInstance; set { if (!object.Equals(_StopExistingInstance, value)) { _StopExistingInstance = value; } } }

        /// <summary>
        /// Wie lange vor dem Start geprueft wird, ob die Primaer-URL bereits antwortet;
        /// <c>0</c> schaltet die Pruefung ab.
        /// </summary>
        /// <remarks>
        /// Diese Pruefung ist die aussagekraeftigste: antwortet die Adresse, laeuft StockTV -
        /// gleich ob von Hand gestartet, per Autostart oder als Dienst. Ports und Prozessnamen
        /// sind daneben nur Indizien. Nicht unter 5 s setzen: <c>localhost</c> loest zuerst auf
        /// <c>::1</c> auf, und dieser Versuch braucht rund 2 s, bevor auf <c>127.0.0.1</c>
        /// zurueckgefallen wird.
        /// </remarks>
        private int _UrlProbeTimeoutSeconds = 5;
        [Description("Seconds to check whether the primary URL already answers before starting; 0 = skip")]
        public int UrlProbeTimeoutSeconds { get => _UrlProbeTimeoutSeconds; set { if (!object.Equals(_UrlProbeTimeoutSeconds, value)) { _UrlProbeTimeoutSeconds = value; } } }

        /// <summary>
        /// Ports, die vor dem Start frei sein muessen. Vorgabe sind die NetMQ-Ports, die
        /// StockTV exklusiv belegt; der Port aus <c>ASPNETCORE_URLS</c> kommt automatisch dazu.
        /// </summary>
        private int[] _Ports = [4747, 4748];
        [Description("TCP ports that must be free before starting; the port from ASPNETCORE_URLS is added automatically")]
        public int[] Ports { get => _Ports; set { if (!object.Equals(_Ports, value)) { _Ports = value; } } }

        private int _ReadinessTimeoutSeconds = 60;
        [Description("Readiness timeout seconds")]
        public int ReadinessTimeoutSeconds { get => _ReadinessTimeoutSeconds; set { if (!object.Equals(_ReadinessTimeoutSeconds, value)) { _ReadinessTimeoutSeconds = value; } } }

        private bool _ShutdownWithKiosk = true;
        [Description("Shutdown with kiosk")]
        public bool ShutdownWithKiosk { get => _ShutdownWithKiosk; set { if (!object.Equals(_ShutdownWithKiosk, value)) { _ShutdownWithKiosk = value; } } }

        private string _LogDirectory = "_logs";
        [Description("Log directory for Blazor host")]
        public string LogDirectory { get => _LogDirectory; set { if (!object.Equals(_LogDirectory, value)) { _LogDirectory = value; } } }
    }
}



