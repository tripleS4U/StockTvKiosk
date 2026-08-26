using System;
using System.Collections.Generic;
using System.Text;

namespace StockTvKiosk.Core.Configuration
{
    // Beschreibungs-Attribut (nur erforderlich, falls nicht separat definiert).
    // ACHTUNG: Kollidiert im Namen mit System.ComponentModel.DescriptionAttribute - falls die
    // Zieldatei "using System.ComponentModel;" verwendet, muss dort ggf. voll qualifiziert
    // oder per Alias (using Description = ...;) disambiguiert werden.
    /// <summary>
    /// Attribut fuer Properties, deren Beschreibung beim Speichern mit in die JSON- bzw.
    /// XML-Konfigurationsdatei geschrieben wird (siehe JsonSerialization/XmlSerialization).
    /// Beschraenkung: wird aktuell nur auf der obersten Objektebene ausgewertet (siehe
    /// Kopfkommentar der Datei bzw. EnrichJsonWithDescriptions/EnrichXmlWithDescriptions).
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
    public class DescriptionAttribute : System.Attribute
    {
        /// <summary>
        /// Die Beschreibung des Properties.
        /// </summary>
        public string Description { get; }

        public DescriptionAttribute(string description)
        {
            Description = description;
        }
    }
}
