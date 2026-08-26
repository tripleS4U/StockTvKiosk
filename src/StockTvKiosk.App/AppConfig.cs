using StockTvKiosk.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace StockTvKiosk.App
{

    // -------------------------------------------------------------------------------------------
    // AppConfig - Anwendungskonfiguration
    //
    // Zielframeworks : .NET Framework 4.7.2 / 4.8 und .NET 8 (identischer Quellcode, C# 7.3)
    //                   Laeuft unter Windows, Linux und macOS.
    // Format         : ConfigFormat.Json  (Umschalten in der Zeile "configFormat", weiter unten)
    // Ablageort      : %LOCALAPPDATA%\<Firma>\<Anwendung>\_config\AppConfig.json bzw. .xml
    //                   (unter Linux/macOS: entsprechender Pfad aus SpecialFolder.LocalApplicationData)
    // Standardwerte  : stehen direkt am Feld  ->  private string _Password = "";
    //
    // Verschluesselung: Properties mit [Secret] werden vor dem Speichern per AES-256-CBC
    //                    VERSCHLEIERT (siehe Hinweis in der Verschluesselung-Region weiter
    //                    unten - das ist KEIN Schutz vor einem Angreifer mit Datei- und
    //                    Programmzugriff, nur gegen versehentliches Klarlesen). Steht in der
    //                    Datei (noch) ein Klartextwert ohne "enc:"-Praefix - z.B. weil er von
    //                    Hand eingetragen wurde - wird er beim naechsten Programmstart
    //                    eingelesen und die Datei sofort wieder verschluesselt zurueckgeschrieben.
    //                    [Secret] wird auch in verschachtelten, selbst definierten
    //                    Konfigurations-Objekten erkannt (rekursiv, siehe WalkSecrets).
    //
    // Beschreibungen : Properties mit [Description] werden dokumentiert in der Datei abgelegt:
    //                  - JSON: { "PropertyName": { "description": "...", "value": ... } }
    //                  - XML:  <PropertyName description="...">value</PropertyName>
    //                  Beim Laden wird der Wrapper transparent wieder entfernt (RemoveDescription-
    //                  Fields bzw. das eingelesene description-Attribut wird ignoriert), sodass
    //                  JsonSerializer/XmlSerializer ganz normal in AppConfig deserialisieren.
    //                  EINSCHRAENKUNG: DescriptionHelper.GetDescriptions() sammelt Beschreibungen
    //                  nur auf der jeweils obersten Objektebene (flaches Dictionary). Beim
    //                  rekursiven Schreiben wird dieses Dictionary unveraendert auch auf
    //                  verschachtelte Konfigurations-Objekte angewendet - hat ein verschachteltes
    //                  Objekt eine Property mit demselben Namen wie eine Top-Level-Property mit
    //                  [Description], wird ihr faelschlich dieselbe Beschreibung zugeordnet. Fuer
    //                  korrekt verschachtelte Beschreibungen muesste GetDescriptions() pro Ebene
    //                  anhand des jeweiligen Objekttyps neu aufgerufen werden.
    //                  Alte Dateien mit "*_Description"-Feldern (Legacy-Format) werden beim Lesen
    //                  ebenfalls ignoriert/uebersprungen.
    //
    // Verwendung     : AppConfig.Instance.Password        lesen/schreiben
    //                  AppConfig.Save()                         speichern
    //                  AppConfig.Reload()                       nicht gespeicherte Aenderungen verwerfen
    //
    // Voraussetzungen:
    //   .NET Framework 4.7.2/4.8 : PackageReference auf System.Text.Json
    //   .NET 8                   : keine zusaetzlichen Pakete noetig (Aes/SHA256 sind Teil des BCL)
    // -------------------------------------------------------------------------------------------



    // -------------------------------------------------------------------------------------------
    // AppConfig - Anwendungskonfiguration
    // -------------------------------------------------------------------------------------------
#if NET5_0_OR_GREATER
#nullable disable // Quellcode ist C#-7.3-kompatibel und traegt daher keine Nullable-Annotationen.
#endif
    /// <summary>
    /// Konfiguration
    /// </summary>
    public partial class AppConfig : System.ComponentModel.INotifyPropertyChanged
    {
        private static string ConfigFolderName { get { return "_config"; } } //private static string ConfigFolderName { get { return "_" + ApplicationName; } }
        private static string MeineFirma { get { return "JusIT"; } }
        // static readonly (nicht const): verhindert Warnungen wegen unerreichbarem Code.
        private static readonly ConfigFormat configFormat = ConfigFormat.Json;

        private static string ApplicationName
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly();
                return System.IO.Path.GetFileNameWithoutExtension(assembly != null ? assembly.Location : null) ?? "UnknownApplication";
            }
        }
        private static string GetConfigFolder()
        {
            // LocalApplicationData ist immer beschreibbar - anders als das Verzeichnis der EXE,
            // das unter "Program Files", MSIX oder SingleFile-Publish ausfaellt.
            // Alternativen:
            //   System.Environment.SpecialFolder.CommonApplicationData  -> gemeinsam fuer alle Benutzer
            //   System.AppContext.BaseDirectory                         -> neben der EXE
            string root = System.AppContext.BaseDirectory;
            return System.IO.Path.Combine(root, ConfigFolderName);
        }
    }
    /// <summary>
    /// Properties
    /// </summary>
    public partial class AppConfig
    {


        private LoggingOptions _Logging = new LoggingOptions();
        [Description("Logging configuration")]
        public LoggingOptions Logging
        {
            get => _Logging;
            set { if (!object.Equals(_Logging, value)) { _Logging = value; RaisePropertyChanged(); } }
        }

        private KioskOptions _Kiosk = new KioskOptions();
        [Description("Kiosk configuration")]
        public KioskOptions KioskOptions
        {
            get => _Kiosk;
            set { if (!object.Equals(_Kiosk, value)) { _Kiosk = value; RaisePropertyChanged(); } }
        }


    }

    /// <summary>
    /// DON'T TOUCH ME
    /// </summary>
    public partial class AppConfig
    {
        private void EncryptSecrets()
        {
            WalkSecrets(this, Secrets.ProtectIfRequired);
        }
        /// <summary>
        /// Entschluesselt alle [Secret]-Properties, auch in verschachtelten eigenen
        /// Konfigurations-Objekten. Gibt true zurueck, wenn dabei mindestens ein Wert im
        /// Klartext (ohne "enc:"-Praefix) vorgefunden wurde, z.B. weil er von Hand in die
        /// Datei eingetragen wurde. Der Aufrufer kann die Datei dann sofort wieder
        /// verschluesselt zurueckschreiben.
        /// </summary>
        private bool DecryptSecrets()
        {
            bool foundPlainTextSecret = false;
            WalkSecrets(this, value =>
            {
                if (!Secrets.IsEncrypted(value))
                    foundPlainTextSecret = true;
                return Secrets.UnprotectIfRequired(value);
            });
            return foundPlainTextSecret;
        }
        /// <summary>
        /// Durchsucht ein Objekt und - rekursiv - alle darin referenzierten, selbst definierten
        /// Konfigurations-Objekte (z.B. eine verschachtelte "GoPhishProperties"-Klasse) nach
        /// string-Properties mit [Secret] und wendet "transform" auf deren Wert an. BCL-Typen
        /// (string selbst, int, DateTime, List&lt;&gt;, ...) werden dabei nicht als Konfigurations-
        /// Objekt behandelt, sondern uebersprungen.
        /// </summary>
        private static void WalkSecrets(object instance, System.Func<string, string> transform)
        {
            WalkSecrets(instance, transform, new System.Collections.Generic.HashSet<object>());
        }
        private static void WalkSecrets(object instance, System.Func<string, string> transform,
            System.Collections.Generic.HashSet<object> visited)
        {
            if (instance == null || !visited.Add(instance))
                return;

            var properties = instance.GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;

                if (property.PropertyType == typeof(string))
                {
                    if (!property.IsDefined(typeof(SecretAttribute), true))
                        continue;
                    string value = property.GetValue(instance) as string;
                    if (value == null)
                        continue;
                    property.SetValue(instance, transform(value));
                }
                else if (IsOwnConfigType(property.PropertyType) && property.CanRead)
                {
                    WalkSecrets(property.GetValue(instance), transform, visited);
                }
            }
        }
        /// <summary>
        /// True fuer selbst definierte Klassen (gleiche Assembly wie AppConfig), die daher
        /// beim Verschluesseln/Klonen rekursiv betrachtet werden. BCL-Typen wie string, int,
        /// DateTime oder List&lt;&gt; liefern hier bewusst false.
        /// </summary>
        private static bool IsOwnConfigType(System.Type type)
        {
            return type.IsClass && type != typeof(string) && type.Assembly == typeof(AppConfig).Assembly;
        }
        /// <summary>
        /// Tiefe Kopie inkl. aller verschachtelten, selbst definierten Konfigurations-Objekte.
        /// Im Gegensatz zu einem einfachen MemberwiseClone() zeigen Kopie und Original bei
        /// Referenztyp-Properties (z.B. einer verschachtelten Unter-Konfigurationsklasse) NICHT
        /// mehr auf dasselbe Objekt - Aenderungen an der Kopie (z.B. durch EncryptSecrets oder
        /// einen Optionsdialog) wirken sich damit nicht mehr versehentlich auf die laufende
        /// Instanz aus.
        /// </summary>
        private static object DeepClone(object source)
        {
            if (source == null)
                return null;

            var cloneMethod = source.GetType().GetMethod("MemberwiseClone",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object clone = cloneMethod.Invoke(source, null);

            var properties = source.GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!IsOwnConfigType(property.PropertyType) || !property.CanRead || !property.CanWrite)
                    continue;

                object nestedClone = DeepClone(property.GetValue(source));
                property.SetValue(clone, nestedClone);
            }
            return clone;
        }

        #region Ablage
        private enum ConfigFormat
        {
            Json,
            Xml
        }
        public static string GetConfigFullName()
        {
            string fileName = "AppConfig" + (configFormat == ConfigFormat.Xml ? ".xml" : ".json");
            return System.IO.Path.Combine(GetConfigFolder(), fileName);
        }
        #endregion
        #region Instanz, Laden und Speichern
        // Lazy<T> ist von Haus aus threadsicher und wird beim Deklarieren initialisiert
        // (kein CS8618 in Projekten mit <Nullable>enable</Nullable>).
        private static System.Lazy<AppConfig> _instance = CreateLazy();
        private static System.Lazy<AppConfig> CreateLazy()
        {
            return new System.Lazy<AppConfig>(Load);
        }
        public static AppConfig Instance
        {
            get { return _instance.Value; }
        }
        /// <summary>
        /// Verwirft alle nicht gespeicherten Aenderungen. Beim naechsten Zugriff auf Instance
        /// wird die Datei erneut gelesen.
        /// </summary>
        public static void Reload()
        {
            _instance = CreateLazy();
        }
        /// <summary>
        /// Schreibt die aktuelle Instanz. Wirft bei Schreibfehlern, z.B. fehlenden Rechten.
        /// </summary>
        public static void Save()
        {
            Instance.WriteToFile();
        }
        private static AppConfig Load()
        {
            string filePath = GetConfigFullName();
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    AppConfig loaded = (configFormat == ConfigFormat.Xml)
                        ? XmlSerialization.FromFile(filePath)
                        : JsonSerialization.FromFile(filePath);
                    if (loaded != null)
                    {
                        bool hasPlainTextSecrets = loaded.DecryptSecrets();
                        if (hasPlainTextSecrets)
                        {
                            // Von Hand im Klartext eingetragene Werte sofort verschluesselt
                            // zurueckschreiben, damit sie nicht dauerhaft im Klartext auf der
                            // Festplatte liegen.
                            try
                            {
                                loaded.WriteToFile();
                            }
                            catch (System.Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine(ex.ToString());
                            }
                        }
                        return loaded;
                    }
                }
                catch (System.Exception ex)
                {
                    // Defekte Datei beiseitelegen, damit die Anwendung startfaehig bleibt.
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    MoveToBackup(filePath);
                }
            }
            // Neu angelegte Konfiguration: die Standardwerte stammen aus den Feldinitialisierern.
            AppConfig result = new AppConfig();
            try
            {
                result.WriteToFile();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
            return result;
        }
        private void WriteToFile()
        {
            // Tiefe Kopie (inkl. verschachtelter Konfigurations-Objekte): EncryptSecrets darf
            // weder die laufende Instanz noch von ihr referenzierte Objekte veraendern.
            AppConfig copy = (AppConfig)DeepClone(this);
            copy.EncryptSecrets();
            string filePath = GetConfigFullName();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath));
            // Erst vollstaendig in eine temporaere Datei schreiben, dann ersetzen. Scheitert die
            // Serialisierung, bleibt die bisherige Konfiguration unbeschaedigt.
            string tempPath = filePath + ".tmp";
            if (configFormat == ConfigFormat.Xml)
            {
                XmlSerialization.ToFile(copy, tempPath);
            }
            else
            {
                JsonSerialization.ToFile(copy, tempPath);
            }
            System.IO.File.Copy(tempPath, filePath, true);
            System.IO.File.Delete(tempPath);
        }
        private static void MoveToBackup(string filePath)
        {
            try
            {
                string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                System.IO.File.Move(filePath, filePath + ".bad_" + stamp);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
        }
        /// <summary>
        /// Tiefe Kopie, z.B. fuer einen Optionsdialog mit Abbrechen-Moeglichkeit. Verschachtelte
        /// Konfigurations-Objekte werden mitgeklont, damit Aenderungen in der Kopie nicht
        /// versehentlich auch die laufende Instanz veraendern.
        /// </summary>
        public AppConfig Copy()
        {
            return (AppConfig)DeepClone(this);
        }
        #endregion
        #region Json
        private static class JsonSerialization
        {
            private static readonly System.Text.Json.JsonSerializerOptions options = CreateOptions();
            private static System.Text.Json.JsonSerializerOptions CreateOptions()
            {
                System.Text.Json.JsonSerializerOptions result = new System.Text.Json.JsonSerializerOptions();
                result.WriteIndented = true;
                result.AllowTrailingCommas = true;
                result.ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip;
                // Umlaute bleiben lesbar statt als \uXXXX. Unbedenklich, weil die Datei nie in
                // einen HTML- oder Skript-Kontext eingebettet wird.
                result.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                return result;
            }
            public static AppConfig FromFile(string filePath)
            {
                // ReadAllText entfernt eine eventuell vorhandene BOM - Utf8JsonReader kaeme damit nicht klar.
                string text = System.IO.File.ReadAllText(filePath);

                // Beschreibungs-Wrapper ({description,value}) bzw. altes "*_Description"-Format
                // vor der Deserialisierung entfernen, damit JsonSerializer die Datei normal lesen kann.
                text = RemoveDescriptionFields(text);

                return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(text, options);
            }

            private static string RemoveDescriptionFields(string jsonText)
            {
                try
                {
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonText))
                    {
                        using (var stream = new System.IO.MemoryStream())
                        {
                            using (var writer = new System.Text.Json.Utf8JsonWriter(stream,
                                new System.Text.Json.JsonWriterOptions { Indented = true }))
                            {
                                RemoveDescriptionFieldsRecursive(writer, doc.RootElement);
                            }
                            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Fehler beim Entfernen von Beschreibungsfeldern: " + ex.Message);
                    // Bei Fehler: Original zurueckgeben, Deserialisierung wirft dann ggf. eine
                    // aussagekraeftige JSON-Exception statt hier stillschweigend etwas Falsches zu liefern.
                    return jsonText;
                }
            }

            private static void RemoveDescriptionFieldsRecursive(System.Text.Json.Utf8JsonWriter writer,
                System.Text.Json.JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Object:
                        writer.WriteStartObject();
                        foreach (var property in element.EnumerateObject())
                        {
                            // Legacy-Format ignorieren (altes "*_Description"-Feld neben dem eigentlichen Wert).
                            if (property.Name.EndsWith("_Description", System.StringComparison.Ordinal))
                                continue;

                            // Aktuelles Format: Ein Objekt, das nur "description" und "value" enthaelt,
                            // wird durch seinen "value"-Inhalt ersetzt.
                            if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                var obj = property.Value;
                                bool hasDescription = false;
                                bool hasValue = false;
                                System.Text.Json.JsonElement valueElement = default;

                                foreach (var prop in obj.EnumerateObject())
                                {
                                    if (prop.Name == "description")
                                        hasDescription = true;
                                    if (prop.Name == "value")
                                    {
                                        hasValue = true;
                                        valueElement = prop.Value;
                                    }
                                }

                                if (hasDescription && hasValue)
                                {
                                    writer.WritePropertyName(property.Name);
                                    RemoveDescriptionFieldsRecursive(writer, valueElement);
                                    continue;
                                }
                            }

                            // Standard-Verarbeitung (keine Beschreibung an dieser Property).
                            writer.WritePropertyName(property.Name);
                            RemoveDescriptionFieldsRecursive(writer, property.Value);
                        }
                        writer.WriteEndObject();
                        break;

                    case System.Text.Json.JsonValueKind.Array:
                        writer.WriteStartArray();
                        foreach (var item in element.EnumerateArray())
                        {
                            RemoveDescriptionFieldsRecursive(writer, item);
                        }
                        writer.WriteEndArray();
                        break;

                    case System.Text.Json.JsonValueKind.String:
                        writer.WriteStringValue(element.GetString());
                        break;

                    case System.Text.Json.JsonValueKind.Number:
                        if (element.TryGetInt32(out int intVal))
                            writer.WriteNumberValue(intVal);
                        else if (element.TryGetInt64(out long longVal))
                            writer.WriteNumberValue(longVal);
                        else if (element.TryGetDouble(out double doubleVal))
                            writer.WriteNumberValue(doubleVal);
                        else
                            writer.WriteRawValue(element.GetRawText());
                        break;

                    case System.Text.Json.JsonValueKind.True:
                        writer.WriteBooleanValue(true);
                        break;

                    case System.Text.Json.JsonValueKind.False:
                        writer.WriteBooleanValue(false);
                        break;

                    case System.Text.Json.JsonValueKind.Null:
                        writer.WriteNullValue();
                        break;
                }
            }
            public static void ToFile(AppConfig value, string filePath)
            {
                string text = System.Text.Json.JsonSerializer.Serialize<AppConfig>(value, options);

                // Beschreibungen aus [Description]-Attributen auslesen und als {description,value}-
                // Wrapper in die JSON-Struktur einfuegen.
                string textWithDescriptions = EnrichJsonWithDescriptions(text, value);

                System.IO.File.WriteAllText(filePath, textWithDescriptions, new System.Text.UTF8Encoding(false));
            }

            private static string EnrichJsonWithDescriptions(string jsonText, AppConfig config)
            {
                try
                {
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonText))
                    {
                        // HINWEIS: liefert nur Beschreibungen der obersten Ebene von "config" (siehe
                        // Einschraenkung im Kopfkommentar - wird unveraendert auch auf verschachtelte
                        // Objekte angewendet, dort also ggf. mit falschen/fehlenden Beschreibungen).
                        var descriptions = DescriptionHelper.GetDescriptions(config);
                        if (descriptions.Count == 0)
                            return jsonText;

                        using (var stream = new System.IO.MemoryStream())
                        {
                            using (var writer = new System.Text.Json.Utf8JsonWriter(stream,
                                new System.Text.Json.JsonWriterOptions { Indented = true }))
                            {
                                WriteJsonWithDescriptionsNewFormat(writer, doc.RootElement, descriptions);
                            }
                            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Fehler beim Anreichern von Beschreibungen: " + ex.Message);
                    // Bei Fehler: Original ohne Beschreibungen zurueckgeben, Datei bleibt gueltiges JSON.
                    return jsonText;
                }
            }

            private static void WriteJsonWithDescriptionsNewFormat(System.Text.Json.Utf8JsonWriter writer,
                System.Text.Json.JsonElement element, System.Collections.Generic.Dictionary<string, string> descriptions)
            {
                switch (element.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Object:
                        writer.WriteStartObject();
                        foreach (var property in element.EnumerateObject())
                        {
                            if (descriptions.TryGetValue(property.Name, out var desc) && !string.IsNullOrEmpty(desc))
                            {
                                // { "PropertyName": { "description": "...", "value": ... } }
                                writer.WritePropertyName(property.Name);
                                writer.WriteStartObject();

                                writer.WritePropertyName("description");
                                writer.WriteStringValue(desc);

                                writer.WritePropertyName("value");
                                WriteJsonWithDescriptionsNewFormat(writer, property.Value, descriptions);

                                writer.WriteEndObject();
                            }
                            else
                            {
                                writer.WritePropertyName(property.Name);
                                WriteJsonWithDescriptionsNewFormat(writer, property.Value, descriptions);
                            }
                        }
                        writer.WriteEndObject();
                        break;

                    case System.Text.Json.JsonValueKind.Array:
                        writer.WriteStartArray();
                        foreach (var item in element.EnumerateArray())
                        {
                            WriteJsonWithDescriptionsNewFormat(writer, item, descriptions);
                        }
                        writer.WriteEndArray();
                        break;

                    case System.Text.Json.JsonValueKind.String:
                        writer.WriteStringValue(element.GetString());
                        break;

                    case System.Text.Json.JsonValueKind.Number:
                        if (element.TryGetInt32(out int intVal))
                            writer.WriteNumberValue(intVal);
                        else if (element.TryGetInt64(out long longVal))
                            writer.WriteNumberValue(longVal);
                        else if (element.TryGetDouble(out double doubleVal))
                            writer.WriteNumberValue(doubleVal);
                        else
                            writer.WriteRawValue(element.GetRawText());
                        break;

                    case System.Text.Json.JsonValueKind.True:
                        writer.WriteBooleanValue(true);
                        break;

                    case System.Text.Json.JsonValueKind.False:
                        writer.WriteBooleanValue(false);
                        break;

                    case System.Text.Json.JsonValueKind.Null:
                        writer.WriteNullValue();
                        break;
                }
            }
        }
        #endregion
        #region Xml
        private static class XmlSerialization
        {
            public static AppConfig FromFile(string filePath)
            {
                System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(AppConfig));
                using (System.Xml.XmlReader reader = System.Xml.XmlReader.Create(filePath))
                {
                    return (AppConfig)serializer.Deserialize(reader);
                }
            }
            public static void ToFile(AppConfig value, string filePath)
            {
                // Erste Phase: normale Serialisierung ueber XmlSerializer.
                System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(AppConfig));
                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                settings.Indent = true;
                settings.Encoding = new System.Text.UTF8Encoding(false);

                string xmlText;
                using (var stream = new System.IO.MemoryStream())
                {
                    using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(stream, settings))
                    {
                        serializer.Serialize(writer, value);
                    }
                    xmlText = settings.Encoding.GetString(stream.ToArray());
                }

                // Zweite Phase: description-Attribute aus [Description] einfuegen.
                xmlText = EnrichXmlWithDescriptions(xmlText, value);

                System.IO.File.WriteAllText(filePath, xmlText, settings.Encoding);
            }

            private static string EnrichXmlWithDescriptions(string xmlText, AppConfig config)
            {
                try
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlText);

                    // Gleiche Einschraenkung wie in EnrichJsonWithDescriptions: nur Top-Level-Properties.
                    var descriptions = DescriptionHelper.GetDescriptions(config);
                    if (descriptions.Count == 0)
                        return xmlText;

                    var rootNode = doc.DocumentElement;
                    if (rootNode == null)
                        return xmlText;

                    foreach (var kvp in descriptions)
                    {
                        string propertyName = kvp.Key;
                        string description = kvp.Value;

                        // SelectSingleNode mit einfachem Namen entspricht XPath "child::Name",
                        // findet also nur direkte Kind-Elemente von rootNode - das reicht fuer
                        // Top-Level-Properties, matcht aber keine verschachtelten Objekte.
                        var propertyNode = rootNode.SelectSingleNode(propertyName);
                        if (propertyNode != null)
                        {
                            var descAttr = doc.CreateAttribute("description");
                            descAttr.Value = description;
                            propertyNode.Attributes.Append(descAttr);
                        }
                    }

                    using (var stringWriter = new System.IO.StringWriter())
                    {
                        var writerSettings = new System.Xml.XmlWriterSettings();
                        writerSettings.Indent = true;
                        writerSettings.IndentChars = "  ";
                        writerSettings.Encoding = System.Text.Encoding.UTF8;
                        writerSettings.OmitXmlDeclaration = false;

                        using (var writer = System.Xml.XmlWriter.Create(stringWriter, writerSettings))
                        {
                            doc.WriteTo(writer);
                        }
                        return stringWriter.ToString();
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Fehler beim Anreichern von Beschreibungen in XML: " + ex.Message);
                    return xmlText;
                }
            }
        }
        #endregion
        #region Verschluesselung
        /// <summary>
        /// "Verschluesselt" Werte mit AES-256 (CBC, PKCS7, zufaellige IV pro Aufruf). Der
        /// Schluessel wird deterministisch aus dem Anwendungsnamen abgeleitet (SHA-256) - das ist
        /// KEIN echtes Geheimnis und KEIN Schutz vor einem Angreifer, der sowohl die
        /// Konfigurationsdatei als auch das Programm (bzw. dessen Namen) kennt. Es verhindert nur,
        /// dass Werte im Klartext in der Datei stehen - etwa gegen versehentliches Mitlesen bei
        /// einem Screenshot, in einem Log oder einer Support-Anfrage.
        ///
        /// Fuer echten, an den Windows-Benutzer/-Rechner gebundenen Schutz siehe
        /// System.Security.Cryptography.ProtectedData (DPAPI, Windows-only) oder eine externe
        /// Schluesselablage (OS-Keychain, Azure Key Vault, HSM). Da hier keine Authentifizierung
        /// (MAC) der Ciphertexte stattfindet, ist diese Klasse nicht fuer Szenarien geeignet, in
        /// denen ein Angreifer wiederholt Entschluesselungsversuche mit manipulierten Werten
        /// beobachten kann (Padding-Oracle) - fuer eine lokale, einmalig beim Programmstart
        /// gelesene Konfigurationsdatei ist das kein praktisches Risiko.
        ///
        /// "enc:"-Praefix kennzeichnet bereits verschluesselte Werte. Fehlt er, gilt der Wert als
        /// Klartext und wird beim naechsten Speichern automatisch verschluesselt - so kann ein
        /// Wert auch von Hand im Klartext in die Konfigurationsdatei eingetragen werden.
        ///
        /// Diese Region kann ersatzlos geloescht werden, wenn keine Geheimnisse gespeichert
        /// werden. Keine zusaetzlichen NuGet-Pakete noetig; funktioniert identisch unter .NET
        /// Framework 4.7.2/4.8 und .NET 8 sowie unter Windows, Linux und macOS.
        /// </summary>
        private static class Secrets
        {
            private const string EncryptedPrefix = "enc:";
            private const int IvSizeBytes = 16; // AES-Blockgroesse
            public static bool IsEncrypted(string value)
            {
                return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix, System.StringComparison.Ordinal);
            }
            public static string ProtectIfRequired(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return value;
                if (IsEncrypted(value))
                    return value;
                return EncryptedPrefix + Protect(value);
            }
            public static string UnprotectIfRequired(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return value;
                if (!IsEncrypted(value))
                    return value;
                string encrypted = value.Substring(EncryptedPrefix.Length);
                try
                {
                    return Unprotect(encrypted);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    // z.B. Wert stammt von einer anderen Anwendung oder ist beschaedigt.
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    return value;
                }
                catch (System.FormatException ex)
                {
                    // Kein gueltiges Base64 -> Wert ist defekt, unveraendert zurueckgeben.
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    return value;
                }
            }
            public static string Protect(string plainText)
            {
                if (string.IsNullOrEmpty(plainText))
                    return plainText;
                byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = DerivedKey;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                    aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                    aes.GenerateIV();
                    byte[] iv = aes.IV;
                    byte[] cipherBytes;
                    using (System.Security.Cryptography.ICryptoTransform encryptor = aes.CreateEncryptor())
                    {
                        cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                    }
                    // iv + cipherText
                    byte[] result = new byte[iv.Length + cipherBytes.Length];
                    System.Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                    System.Buffer.BlockCopy(cipherBytes, 0, result, iv.Length, cipherBytes.Length);
                    return System.Convert.ToBase64String(result);
                }
            }
            public static string Unprotect(string encryptedText)
            {
                if (string.IsNullOrEmpty(encryptedText))
                    return encryptedText;
                byte[] data = System.Convert.FromBase64String(encryptedText);
                if (data.Length < IvSizeBytes)
                    throw new System.Security.Cryptography.CryptographicException("Verschluesselter Wert ist zu kurz.");
                byte[] iv = new byte[IvSizeBytes];
                byte[] cipherBytes = new byte[data.Length - IvSizeBytes];
                System.Buffer.BlockCopy(data, 0, iv, 0, IvSizeBytes);
                System.Buffer.BlockCopy(data, IvSizeBytes, cipherBytes, 0, cipherBytes.Length);
                using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = DerivedKey;
                    aes.IV = iv;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                    aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                    using (System.Security.Cryptography.ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                        return System.Text.Encoding.UTF8.GetString(plainBytes);
                    }
                }
            }
            // SHA-256 liefert 32 Byte, also genau die Schluessellaenge fuer AES-256.
            private static byte[] DerivedKey
            {
                get { return System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(ApplicationName)); }
            }
        }
        #endregion
        #region INotifyPropertyChanged
        // Initialisierung mit einem leeren Delegaten: kein Null-Check noetig und keine
        // CS8618-Warnung in Projekten mit <Nullable>enable</Nullable>.
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged = delegate { };
        protected void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            this.PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public sealed class SecretAttribute : System.Attribute
    {
    }

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

    /// <summary>
    /// Hilfsklasse zur Extraktion von [Description]-Attributen aus Objekten - per Reflection,
    /// z.B. fuer Tooltips in einem Optionsdialog. Wird auch von JsonSerialization/XmlSerialization
    /// verwendet, um Beschreibungen beim Speichern in die Konfigurationsdatei einzufuegen.
    /// Liefert nur die Beschreibungen der uebergebenen Objektebene (nicht rekursiv) - siehe
    /// Einschraenkung im Kopfkommentar der Datei.
    /// </summary>
    public static class DescriptionHelper
    {
        /// <summary>
        /// Liest alle Properties eines Objekts und ihre [Description]-Attribute aus.
        /// Gibt ein Dictionary zurueck: PropertyName -> Description (Properties ohne
        /// Attribut werden ausgelassen).
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, string> GetDescriptions(object instance)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

            if (instance == null)
                return result;

            var properties = instance.GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var property in properties)
            {
                // Ignoriere Read-Only Properties.
                if (!property.CanRead || !property.CanWrite)
                    continue;

                // Suche nach [Description]-Attribut. GetCustomAttribute<T>() ist eine Extension-
                // Method aus System.Reflection.CustomAttributeExtensions - da dieser Code ohne
                // "using System.Reflection;" auskommt, wird sie hier statisch aufgerufen.
                var descAttr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DescriptionAttribute>(property, true);
                if (descAttr != null)
                {
                    result[property.Name] = descAttr.Description;
                }
            }

            return result;
        }

        /// <summary>
        /// Sucht nach dem [Description]-Attribut auf einem bestimmten Property.
        /// </summary>
        public static string GetDescription(System.Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);

            if (property == null)
                return null;

            var descAttr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DescriptionAttribute>(property, true);
            return descAttr?.Description;
        }
    }
#if NET5_0_OR_GREATER
#nullable restore
#endif

}


