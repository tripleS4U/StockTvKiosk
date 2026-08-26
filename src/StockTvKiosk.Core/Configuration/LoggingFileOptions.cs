namespace StockTvKiosk.Core.Configuration
{
    public class LoggingFileOptions
    {
        private bool _Enabled = true;
        [Description("Enable file logging")]
        public bool Enabled { get => _Enabled; set { if (!object.Equals(_Enabled, value)) { _Enabled = value; } } }

        private string _Directory = "_logs";
        [Description("Log directory")]
        public string Directory { get => _Directory; set { if (!object.Equals(_Directory, value)) { _Directory = value; } } }

        private string _FileName = "kiosk.log";
        [Description("Log file name")]
        public string FileName { get => _FileName; set { if (!object.Equals(_FileName, value)) { _FileName = value; } } }

        private int _MaxFileSizeBytes = 5242880;
        [Description("Maximum log file size in bytes")]
        public int MaxFileSizeBytes { get => _MaxFileSizeBytes; set { if (!object.Equals(_MaxFileSizeBytes, value)) { _MaxFileSizeBytes = value; } } }

        private int _RetainedFileCount = 3;
        [Description("Number of retained log files")]
        public int RetainedFileCount { get => _RetainedFileCount; set { if (!object.Equals(_RetainedFileCount, value)) { _RetainedFileCount = value; } } }
    }


}
