
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
    }

}

 