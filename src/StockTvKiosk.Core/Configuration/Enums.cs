using System;
using System.Collections.Generic;
using System.Text;

namespace StockTvKiosk.Core.Configuration
{

    public enum SingleScreenFallback
    {
        /// <summary>Beide Fenster nebeneinander auf den Hauptbildschirm legen (wie -SecondScreen:$false).</summary>
        SideBySide,

        /// <summary>Nur das Hauptfenster oeffnen, kein Spiegel.</summary>
        PrimaryOnly
    }

    /// <summary>Wie ein Fenster den Bildschirm einnimmt.</summary>
    public enum WindowMode
    {
        /// <summary>Randlos auf exakte Position und Groesse gesetzt. Zuverlaessig unter Windows und X11.</summary>
        Borderless,

        /// <summary>Zusaetzlich WindowState=FullScreen. Auf manchen Compositors robuster, dafuer
        /// laesst sich der Zielbildschirm dort nicht mehr erzwingen.</summary>
        FullScreen
    }


}
