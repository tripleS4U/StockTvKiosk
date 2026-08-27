namespace StockTvKiosk.Core.Configuration
{
    /// <summary>
    /// Neustart und Herunterfahren des Rechners.
    /// </summary>
    /// <remarks>
    /// Die Verzoegerung wird bewusst im Kiosk selbst abgewartet und nicht an das
    /// Betriebssystem durchgereicht: <c>shutdown /t</c> kennt sie, <c>systemctl poweroff</c>
    /// nicht. In der Anwendung zu warten verhaelt sich auf beiden Plattformen gleich und
    /// laesst sich ueber <c>POST /api/system/cancel-shutdown</c> zurueckholen.
    /// </remarks>
    public class PowerOptions
    {
        private ControlMode _Mode = ControlMode.Auto;
        [Description("Auto = detect per operating system, CommandLine = use the commands below, Disabled = refuse")]
        public ControlMode Mode { get => _Mode; set { if (!object.Equals(_Mode, value)) { _Mode = value; } } }

        private string? _RebootCommand = null;
        [Description("Reboot command; empty = per operating system (shutdown.exe / systemctl)")]
        public string? RebootCommand { get => _RebootCommand; set { if (!object.Equals(_RebootCommand, value)) { _RebootCommand = value; } } }

        private string? _RebootArguments = null;
        [Description("Arguments for the reboot command")]
        public string? RebootArguments { get => _RebootArguments; set { if (!object.Equals(_RebootArguments, value)) { _RebootArguments = value; } } }

        private string? _ShutdownCommand = null;
        [Description("Shutdown command; empty = per operating system (shutdown.exe / systemctl)")]
        public string? ShutdownCommand { get => _ShutdownCommand; set { if (!object.Equals(_ShutdownCommand, value)) { _ShutdownCommand = value; } } }

        private string? _ShutdownArguments = null;
        [Description("Arguments for the shutdown command")]
        public string? ShutdownArguments { get => _ShutdownArguments; set { if (!object.Equals(_ShutdownArguments, value)) { _ShutdownArguments = value; } } }

        private int _DefaultDelaySeconds = 0;
        [Description("Delay used when the request does not specify one")]
        public int DefaultDelaySeconds { get => _DefaultDelaySeconds; set { if (!object.Equals(_DefaultDelaySeconds, value)) { _DefaultDelaySeconds = value; } } }

        private int _MaxDelaySeconds = 3600;
        [Description("Upper bound for the delay a request may ask for")]
        public int MaxDelaySeconds { get => _MaxDelaySeconds; set { if (!object.Equals(_MaxDelaySeconds, value)) { _MaxDelaySeconds = value; } } }

        private int _CommandTimeoutSeconds = 15;
        [Description("How long to wait for the command to return")]
        public int CommandTimeoutSeconds { get => _CommandTimeoutSeconds; set { if (!object.Equals(_CommandTimeoutSeconds, value)) { _CommandTimeoutSeconds = value; } } }
    }
}
