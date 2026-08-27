
namespace StockTvKiosk.Core.Configuration
{

    public class KioskOptions
    {
        private UrlsOptions _Urls = new UrlsOptions();
        [Description("URLs")]
        public UrlsOptions Urls { get => _Urls; set { if (!object.Equals(_Urls, value)) { _Urls = value; } } }

        private DisplayOptions _Display = new DisplayOptions();
        [Description("Display options")]
        public DisplayOptions Display { get => _Display; set { if (!object.Equals(_Display, value)) { _Display = value; } } }

  
        private RetryOptions _Retry = new RetryOptions();
        [Description("Retry options")]
        public RetryOptions Retry { get => _Retry; set { if (!object.Equals(_Retry, value)) { _Retry = value; } } }

        private BlazorHostOptions _BlazorHost = new BlazorHostOptions();
        [Description("Blazor host options")]
        public BlazorHostOptions BlazorHost { get => _BlazorHost; set { if (!object.Equals(_BlazorHost, value)) { _BlazorHost = value; } } }

        private RestApiOptions _RestApi = new RestApiOptions();
        [Description("REST API options")]
        public RestApiOptions RestApi { get => _RestApi; set { if (!object.Equals(_RestApi, value)) { _RestApi = value; } } }

        private PowerOptions _Power = new PowerOptions();
        [Description("Power options (reboot, shutdown)")]
        public PowerOptions Power { get => _Power; set { if (!object.Equals(_Power, value)) { _Power = value; } } }

        private DisplayControlOptions _DisplayControl = new DisplayControlOptions();
        [Description("Display control options (duplicate, extend)")]
        public DisplayControlOptions DisplayControl { get => _DisplayControl; set { if (!object.Equals(_DisplayControl, value)) { _DisplayControl = value; } } }
    }

}

 