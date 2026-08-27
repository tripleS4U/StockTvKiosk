namespace StockTvKiosk.Core.Configuration
{
    /// <summary>
    /// Umschalten zwischen "Bildschirme duplizieren" und "Bildschirme erweitern".
    /// </summary>
    /// <remarks>
    /// Die Umschaltung wirkt systemweit, nicht nur auf den Kiosk. Nach dem Wechsel auf
    /// "Duplizieren" faellt der Fensterplan wie gehabt auf ein einziges Fenster zurueck,
    /// sofern <c>Display.TreatDuplicatedAsSingleScreen</c> gesetzt ist - das zweite Fenster
    /// ist dann nicht abgestuerzt, sondern absichtlich weg.
    /// </remarks>
    public class DisplayControlOptions
    {
        private ControlMode _Mode = ControlMode.Auto;
        [Description("Auto = detect per operating system, CommandLine = use the commands below, Disabled = refuse")]
        public ControlMode Mode { get => _Mode; set { if (!object.Equals(_Mode, value)) { _Mode = value; } } }

        private string? _DuplicateCommand = null;
        [Description("Command switching to duplicated displays; empty = per operating system")]
        public string? DuplicateCommand { get => _DuplicateCommand; set { if (!object.Equals(_DuplicateCommand, value)) { _DuplicateCommand = value; } } }

        /// <summary>
        /// Platzhalter: <c>{primary}</c>, <c>{secondary}</c>, <c>{direction}</c> - werden vor
        /// dem Aufruf durch die Ausgangsnamen bzw. die xrandr-Richtung ersetzt.
        /// </summary>
        private string? _DuplicateArguments = null;
        [Description("Arguments; placeholders {primary}, {secondary}, {direction}")]
        public string? DuplicateArguments { get => _DuplicateArguments; set { if (!object.Equals(_DuplicateArguments, value)) { _DuplicateArguments = value; } } }

        private string? _ExtendCommand = null;
        [Description("Command switching to extended displays; empty = per operating system")]
        public string? ExtendCommand { get => _ExtendCommand; set { if (!object.Equals(_ExtendCommand, value)) { _ExtendCommand = value; } } }

        private string? _ExtendArguments = null;
        [Description("Arguments; placeholders {primary}, {secondary}, {direction}")]
        public string? ExtendArguments { get => _ExtendArguments; set { if (!object.Equals(_ExtendArguments, value)) { _ExtendArguments = value; } } }

        /// <summary>Nur Linux/xrandr: leer lassen, dann werden die Ausgaenge aus <c>xrandr --query</c> genommen.</summary>
        private string? _PrimaryOutput = null;
        [Description("Linux only: xrandr output of the main screen; empty = detect")]
        public string? PrimaryOutput { get => _PrimaryOutput; set { if (!object.Equals(_PrimaryOutput, value)) { _PrimaryOutput = value; } } }

        private string? _SecondaryOutput = null;
        [Description("Linux only: xrandr output of the second screen; empty = detect")]
        public string? SecondaryOutput { get => _SecondaryOutput; set { if (!object.Equals(_SecondaryOutput, value)) { _SecondaryOutput = value; } } }

        private ExtendDirection _ExtendDirection = ExtendDirection.RightOf;
        [Description("Linux only: where the second screen sits relative to the main screen")]
        public ExtendDirection ExtendDirection { get => _ExtendDirection; set { if (!object.Equals(_ExtendDirection, value)) { _ExtendDirection = value; } } }

        /// <summary>
        /// Windows: scheitert die CCD-API, wird <c>DisplaySwitch.exe /clone|/extend</c> versucht.
        /// Beides gehoert zu Windows, es muss nichts nachinstalliert werden.
        /// </summary>
        private bool _UseDisplaySwitchFallback = true;
        [Description("Windows only: fall back to DisplaySwitch.exe if the CCD API fails")]
        public bool UseDisplaySwitchFallback { get => _UseDisplaySwitchFallback; set { if (!object.Equals(_UseDisplaySwitchFallback, value)) { _UseDisplaySwitchFallback = value; } } }

        private bool _ReapplyLayoutAfterSwitch = true;
        [Description("Recalculate and reapply the kiosk window layout after switching")]
        public bool ReapplyLayoutAfterSwitch { get => _ReapplyLayoutAfterSwitch; set { if (!object.Equals(_ReapplyLayoutAfterSwitch, value)) { _ReapplyLayoutAfterSwitch = value; } } }

        /// <summary>
        /// Wartezeit, bis das Betriebssystem die neue Topologie meldet. Ohne sie liest der
        /// Kiosk direkt nach dem Umschalten noch die alte Bildschirmliste und ordnet die
        /// Fenster nach veralteten Daten an.
        /// </summary>
        private int _SettleDelayMilliseconds = 1500;
        [Description("Wait after switching before reading the new screen layout")]
        public int SettleDelayMilliseconds { get => _SettleDelayMilliseconds; set { if (!object.Equals(_SettleDelayMilliseconds, value)) { _SettleDelayMilliseconds = value; } } }

        private int _CommandTimeoutSeconds = 20;
        [Description("How long to wait for the command to return")]
        public int CommandTimeoutSeconds { get => _CommandTimeoutSeconds; set { if (!object.Equals(_CommandTimeoutSeconds, value)) { _CommandTimeoutSeconds = value; } } }
    }
}
