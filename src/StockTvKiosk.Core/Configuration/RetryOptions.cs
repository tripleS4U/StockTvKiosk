

namespace StockTvKiosk.Core.Configuration
{
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
}



