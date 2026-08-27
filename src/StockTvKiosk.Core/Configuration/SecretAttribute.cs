namespace StockTvKiosk.Core.Configuration
{
    /// <summary>
    /// Markiert ein string-Property, das in der Konfigurationsdatei nicht im Klartext stehen
    /// soll (z. B. der API-Schluessel). AppConfig verschluesselt solche Werte beim Speichern.
    /// </summary>
    /// <remarks>
    /// Bewusst ein eigenes Attribut neben dem gleichnamigen in AppConfig.cs - genau wie bei
    /// <see cref="DescriptionAttribute"/>. Dieses Projekt darf nicht auf die Anwendung
    /// verweisen (die Abhaengigkeit laeuft andersherum), deshalb kann es das dortige Attribut
    /// nicht verwenden. AppConfig erkennt beide am Namen.
    ///
    /// Wie dort gilt: das ist Verschleierung gegen versehentliches Mitlesen, kein Schutz vor
    /// jemandem, der Datei UND Programm hat.
    /// </remarks>
    [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
    public sealed class SecretAttribute : System.Attribute
    {
    }
}
