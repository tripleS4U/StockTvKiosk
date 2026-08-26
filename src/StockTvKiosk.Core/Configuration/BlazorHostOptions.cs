
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
            ["ASPNETCORE_URLS"] = "http://localhost:5129"
        };
        [Description("Environment variables")]
        public Dictionary<string, string> Environment { get => _Environment; set { if (!object.Equals(_Environment, value)) { _Environment = value; } } }

        private bool _StopExistingInstance = true;
        [Description("Stop existing instance")]
        public bool StopExistingInstance { get => _StopExistingInstance; set { if (!object.Equals(_StopExistingInstance, value)) { _StopExistingInstance = value; } } }

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



