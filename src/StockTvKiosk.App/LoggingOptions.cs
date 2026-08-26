namespace StockTvKiosk.App
{
    public partial class AppConfig
    {
        public class LoggingOptions
        {
            private Dictionary<string, string> _LogLevel = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Default"] = "Information",
                ["Microsoft"] = "Warning"
            };
            [Description("Log levels")]
            public Dictionary<string, string> LogLevel
            {
                get => _LogLevel;
                set { if (!object.Equals(_LogLevel, value)) { _LogLevel = value; } }
            }

            private LoggingFileOptions _File = new LoggingFileOptions();
            [Description("File logger options")]
            public LoggingFileOptions File
            {
                get => _File;
                set { if (!object.Equals(_File, value)) { _File = value; } }
            }
        }

        
    }
}