namespace StockTvKiosk.App;

/// <summary>
/// Zugriff auf die Konfigurationsdatei als Datei - Herunterladen, Ersetzen, Zuruecksetzen.
/// </summary>
/// <remarks>
/// Bewusst als weiterer Teil von <see cref="AppConfig"/> und nicht als eigene Klasse: nur so
/// sind der Serialisierer und <c>WriteToFile</c> erreichbar, ohne die Vorlage in AppConfig.cs
/// anzufassen. Dort steht nur, was aus der Vorlage stammt; hier steht, was dieses Projekt
/// zusaetzlich braucht.
/// </remarks>
public partial class AppConfig
{
    /// <summary>Groesste Datei, die noch angenommen wird. Die echte liegt bei ~4 KB.</summary>
    public const int MaxFileSizeBytes = 1024 * 1024;

    /// <summary>Liest die Datei so, wie sie auf der Platte liegt - mit verschluesselten Geheimnissen.</summary>
    public static string ReadFile() => System.IO.File.ReadAllText(GetConfigFullName());

    public static bool FileExists() => System.IO.File.Exists(GetConfigFullName());

    /// <summary>
    /// Ersetzt die Datei, aber erst nachdem der neue Inhalt vollstaendig gelesen werden konnte.
    /// </summary>
    /// <remarks>
    /// Ungeprueft zu schreiben waere aus der Ferne fahrlaessig: eine unlesbare Datei legt
    /// AppConfig beim naechsten Start beiseite und faengt mit Standardwerten an - dann ist
    /// auch der API-Schluessel weg und niemand kommt mehr auf das Geraet. Die bisherige Datei
    /// bleibt deshalb bis zum letzten Moment unangetastet und wandert dann nach .bak.
    /// </remarks>
    /// <param name="content">Inhalt der neuen Datei.</param>
    /// <param name="error">Grund, falls der Inhalt nicht angenommen wurde.</param>
    /// <param name="backupPath">Wohin die bisherige Datei gesichert wurde.</param>
    public static bool TryReplaceFile(string content, out string? error, out string? backupPath)
    {
        error = null;
        backupPath = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Der Inhalt ist leer.";
            return false;
        }

        string target = GetConfigFullName();
        string candidate = target + ".upload";

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            System.IO.File.WriteAllText(candidate, content, new System.Text.UTF8Encoding(false));

            // Der eigentliche Test: laesst sich daraus eine Konfiguration lesen? Ein
            // Syntaxcheck allein wuerde eine gueltige, aber voellig andere JSON-Datei
            // durchlassen.
            AppConfig? parsed = configFormat == ConfigFormat.Xml
                ? XmlSerialization.FromFile(candidate)
                : JsonSerialization.FromFile(candidate);

            if (parsed is null)
            {
                error = "Der Inhalt liess sich nicht als Konfiguration lesen.";
                return false;
            }

            if (FileExists())
            {
                backupPath = target + ".bak";
                System.IO.File.Copy(target, backupPath, overwrite: true);
            }

            System.IO.File.Copy(candidate, target, overwrite: true);
            return true;
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { System.IO.File.Delete(candidate); } catch { /* Aufraeumen ist Beiwerk */ }
        }
    }

    /// <summary>
    /// Schreibt die Datei mit Standardwerten neu.
    /// </summary>
    /// <param name="keepRestApi">
    /// Die Einstellungen der REST-Schnittstelle aus der laufenden Konfiguration uebernehmen.
    /// Ohne das waere der Aufruf ein Selbstausschluss: mit leerem ApiKey startet die
    /// Schnittstelle bei einer nicht-lokalen Bindung gar nicht mehr, und das Geraet ist aus
    /// der Ferne nicht mehr erreichbar.
    /// </param>
    /// <returns>Pfad der gesicherten bisherigen Datei, oder null.</returns>
    public static string? ResetToDefaults(bool keepRestApi)
    {
        string target = GetConfigFullName();
        string? backupPath = null;

        if (System.IO.File.Exists(target))
        {
            backupPath = target + ".bak";
            System.IO.File.Copy(target, backupPath, overwrite: true);
        }

        var fresh = new AppConfig();

        if (keepRestApi)
            fresh.KioskOptions.RestApi = Instance.KioskOptions.RestApi;

        fresh.WriteToFile();
        return backupPath;
    }
}
