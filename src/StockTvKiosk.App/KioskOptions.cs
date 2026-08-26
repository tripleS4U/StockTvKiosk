namespace StockTvKiosk.App
{
    public partial class AppConfig
    {
        public class KioskOptions
        {
            private UrlsOptions _Urls = new UrlsOptions();
            [Description("URLs")]
            public UrlsOptions Urls { get => _Urls; set { if (!object.Equals(_Urls, value)) { _Urls = value; } } }

            public class UrlsOptions
            {
                private string _PrimaryUrl = "http://localhost:5129/turnier";
                [Description("Primary URL")]
                public string PrimaryUrl { get => _PrimaryUrl; set { if (!object.Equals(_PrimaryUrl, value)) { _PrimaryUrl = value; } } }

                private string _SecondaryUrl = "http://localhost:5129/display2";
                [Description("Secondary URL")]
                public string SecondaryUrl { get => _SecondaryUrl; set { if (!object.Equals(_SecondaryUrl, value)) { _SecondaryUrl = value; } } }

                private bool _Demo = false;
                [Description("Demo mode")]
                public bool Demo { get => _Demo; set { if (!object.Equals(_Demo, value)) { _Demo = value; } } }
            }

            private DisplayOptions _Display = new DisplayOptions();
            [Description("Display options")]
            public DisplayOptions Display { get => _Display; set { if (!object.Equals(_Display, value)) { _Display = value; } } }

            public class DisplayOptions
            {
                private bool _Dual = true;
                [Description("Dual display")]
                public bool Dual { get => _Dual; set { if (!object.Equals(_Dual, value)) { _Dual = value; } } }

                private bool _UseSecondScreen = true;
                [Description("Use second screen")]
                public bool UseSecondScreen { get => _UseSecondScreen; set { if (!object.Equals(_UseSecondScreen, value)) { _UseSecondScreen = value; } } }

                private int _MainDisplayIndex = -1;
                [Description("Main display index")]
                public int MainDisplayIndex { get => _MainDisplayIndex; set { if (!object.Equals(_MainDisplayIndex, value)) { _MainDisplayIndex = value; } } }

                private int _MirrorDisplayIndex = -1;
                [Description("Mirror display index")]
                public int MirrorDisplayIndex { get => _MirrorDisplayIndex; set { if (!object.Equals(_MirrorDisplayIndex, value)) { _MirrorDisplayIndex = value; } } }

                private string _SingleScreenFallback = "SideBySide";
                [Description("Single screen fallback")]
                public string SingleScreenFallback { get => _SingleScreenFallback; set { if (!object.Equals(_SingleScreenFallback, value)) { _SingleScreenFallback = value; } } }

                private string _WindowMode = "Borderless";
                [Description("Window mode")]
                public string WindowMode { get => _WindowMode; set { if (!object.Equals(_WindowMode, value)) { _WindowMode = value; } } }

                private bool _UseWorkingArea = false;
                [Description("Use working area")]
                public bool UseWorkingArea { get => _UseWorkingArea; set { if (!object.Equals(_UseWorkingArea, value)) { _UseWorkingArea = value; } } }

                private bool _TreatDuplicatedAsSingleScreen = true;
                [Description("Treat duplicated as single screen")]
                public bool TreatDuplicatedAsSingleScreen { get => _TreatDuplicatedAsSingleScreen; set { if (!object.Equals(_TreatDuplicatedAsSingleScreen, value)) { _TreatDuplicatedAsSingleScreen = value; } } }

                private bool _HideCursor = true;
                [Description("Hide cursor")]
                public bool HideCursor { get => _HideCursor; set { if (!object.Equals(_HideCursor, value)) { _HideCursor = value; } } }

                private bool _AllowExitShortcut = true;
                [Description("Allow exit shortcut")]
                public bool AllowExitShortcut { get => _AllowExitShortcut; set { if (!object.Equals(_AllowExitShortcut, value)) { _AllowExitShortcut = value; } } }
            }

            private RetryOptions _Retry = new RetryOptions();
            [Description("Retry options")]
            public RetryOptions Retry { get => _Retry; set { if (!object.Equals(_Retry, value)) { _Retry = value; } } }

            public class RetryOptions
            {
                private bool _Enabled = true;
                [Description("Retry enabled")]
                public bool Enabled { get => _Enabled; set { if (!object.Equals(_Enabled, value)) { _Enabled = value; } } }

                private int _IntervalSeconds = 5;
                [Description("Retry interval seconds")]
                public int IntervalSeconds { get => _IntervalSeconds; set { if (!object.Equals(_IntervalSeconds, value)) { _IntervalSeconds = value; } } }

                private bool _ShowErrorPage = true;
                [Description("Show error page")]
                public bool ShowErrorPage { get => _ShowErrorPage; set { if (!object.Equals(_ShowErrorPage, value)) { _ShowErrorPage = value; } } }

                private int _ProbeTimeoutSeconds = 5;
                [Description("Probe timeout seconds")]
                public int ProbeTimeoutSeconds { get => _ProbeTimeoutSeconds; set { if (!object.Equals(_ProbeTimeoutSeconds, value)) { _ProbeTimeoutSeconds = value; } } }
            }

            private BlazorHostOptions _BlazorHost = new BlazorHostOptions();
            [Description("Blazor host options")]
            public BlazorHostOptions BlazorHost { get => _BlazorHost; set { if (!object.Equals(_BlazorHost, value)) { _BlazorHost = value; } } }

            public class BlazorHostOptions
            {
                private bool _Enabled = false;
                [Description("Enable Blazor host")]
                public bool Enabled { get => _Enabled; set { if (!object.Equals(_Enabled, value)) { _Enabled = value; } } }

                private string _ExecutablePath = null;
                [Description("Executable path")]
                public string ExecutablePath { get => _ExecutablePath; set { if (!object.Equals(_ExecutablePath, value)) { _ExecutablePath = value; } } }

                private string _ExecutableName = "StockTvBlazor";
                [Description("Executable name")]
                public string ExecutableName { get => _ExecutableName; set { if (!object.Equals(_ExecutableName, value)) { _ExecutableName = value; } } }

                private string _SearchRoot = null;
                [Description("Search root")]
                public string SearchRoot { get => _SearchRoot; set { if (!object.Equals(_SearchRoot, value)) { _SearchRoot = value; } } }

                private string _Arguments = null;
                [Description("Arguments")]
                public string Arguments { get => _Arguments; set { if (!object.Equals(_Arguments, value)) { _Arguments = value; } } }

                private string _WorkingDirectory = null;
                [Description("Working directory")]
                public string WorkingDirectory { get => _WorkingDirectory; set { if (!object.Equals(_WorkingDirectory, value)) { _WorkingDirectory = value; } } }

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

       
    }
}