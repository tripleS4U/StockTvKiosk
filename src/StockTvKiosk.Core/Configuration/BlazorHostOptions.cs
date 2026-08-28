
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

        private Dictionary<string, string> _Environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = "http://localhost:5129",
            // "Development" laesst Blazor die echte Exception zeigen statt nur
            // "An unhandled error has occurred" - entspricht -DetailedErrors in start-compiled.ps1.
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        };
        [Description("Environment variables")]
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



