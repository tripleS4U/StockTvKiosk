namespace StockTvKiosk.Core.Configuration
{
    /// <summary>
    /// Die REST-Schnittstelle, ueber die sich der Rechner neu starten, herunterfahren und
    /// zwischen "Duplizieren" und "Erweitern" umschalten laesst.
    /// </summary>
    /// <remarks>
    /// Was die Schnittstelle tatsaechlich darf, steht NICHT hier, sondern in
    /// <see cref="PowerOptions.Mode"/> bzw. <see cref="DisplayControlOptions.Mode"/>:
    /// <c>Disabled</c> ist dort der Notausschalter fuer die jeweilige Gruppe. Ein zweiter
    /// Schalter an dieser Stelle wuerde nur die Frage aufwerfen, welcher von beiden gewinnt.
    /// </remarks>
    public class RestApiOptions
    {
        private bool _Enabled = true;
        [Description("Enable REST API")]
        public bool Enabled { get => _Enabled; set { if (!object.Equals(_Enabled, value)) { _Enabled = value; } } }

        private string _BindAddress = "0.0.0.0";
        [Description("Bind address: 0.0.0.0 = all interfaces, 127.0.0.1 = local only")]
        public string BindAddress { get => _BindAddress; set { if (!object.Equals(_BindAddress, value)) { _BindAddress = value; } } }

        private int _Port = 8099;
        [Description("TCP port")]
        public int Port { get => _Port; set { if (!object.Equals(_Port, value)) { _Port = value; } } }

        /// <summary>
        /// Pflicht, sobald die Schnittstelle ueber Loopback hinaus erreichbar ist - sonst waere
        /// der Neustart-Endpunkt ein Fernausschalter fuer jeden im Netz. Ist der Schluessel dann
        /// leer, startet die Schnittstelle gar nicht erst und schreibt den Grund ins Log.
        /// Wird bei jedem Speichern verschluesselt abgelegt (siehe [Secret]).
        /// </summary>
        private string _ApiKey = "";
        [Secret]
        [Description("API key, required unless bound to loopback only")]
        public string ApiKey { get => _ApiKey; set { if (!object.Equals(_ApiKey, value)) { _ApiKey = value; } } }

        private string _ApiKeyHeader = "X-Api-Key";
        [Description("HTTP header carrying the API key")]
        public string ApiKeyHeader { get => _ApiKeyHeader; set { if (!object.Equals(_ApiKeyHeader, value)) { _ApiKeyHeader = value; } } }

        private bool _SwaggerEnabled = true;
        [Description("Serve the Swagger UI")]
        public bool SwaggerEnabled { get => _SwaggerEnabled; set { if (!object.Equals(_SwaggerEnabled, value)) { _SwaggerEnabled = value; } } }

        private string _SwaggerRoute = "swagger";
        [Description("Route prefix of the Swagger UI")]
        public string SwaggerRoute { get => _SwaggerRoute; set { if (!object.Equals(_SwaggerRoute, value)) { _SwaggerRoute = value; } } }
    }
}
