using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.Core.Configuration
{

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
}



