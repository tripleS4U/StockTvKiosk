using System;
using System.Collections.Generic;
using System.Text;

namespace StockTvKiosk.Core.Configuration
{
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

        //private string _SingleScreenFallback = "SideBySide";
        [Description("Single screen fallback")]
        public SingleScreenFallback SingleScreenFallback { get; set; } = SingleScreenFallback.SideBySide;

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

}
