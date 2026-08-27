using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using StockTvKiosk.App.Services;
using StockTvKiosk.Core.Configuration;
using StockTvKiosk.Core.Control;
using StockTvKiosk.Core.Display;

namespace StockTvKiosk.App.Api;

/// <summary>
/// Startet die REST-Schnittstelle auf Kestrel im selben Prozess wie die Kiosk-Fenster.
/// </summary>
/// <remarks>
/// Im selben Prozess, weil das Neuanordnen der Fenster nach einem Wechsel des Anzeigemodus
/// ueber den UI-Thread laufen muss - aus einem eigenen Dienst heraus ginge das nicht.
/// </remarks>
internal sealed class KioskApiHost(WebApplication app, ILogger logger) : IAsyncDisposable
{
    /// <summary>
    /// Baut und startet die Schnittstelle, sofern eingeschaltet und zulaessig konfiguriert.
    /// Gibt null zurueck, wenn sie nicht laufen soll - der Kiosk selbst laeuft dann normal weiter.
    /// </summary>
    public static async Task<KioskApiHost?> StartIfEnabledAsync(
        KioskOptions options,
        KioskWindowManager windows,
        IDisplayModeDetector detector,
        PlatformInfo platform,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("StockTvKiosk.Api");
        var rest = options.RestApi;

        if (!rest.Enabled)
        {
            logger.LogInformation(
                "Die REST-Schnittstelle ist abgeschaltet (KioskOptions > RestApi > Enabled = false).");
            return null;
        }

        if (!TryResolveAddress(rest.BindAddress, out var address))
        {
            logger.LogError(
                "KioskOptions > RestApi > BindAddress ist keine gueltige Adresse: {Address}. " +
                "Die Schnittstelle wird nicht gestartet.", rest.BindAddress);
            return null;
        }

        // Ohne Schluessel waere der Neustart-Endpunkt ein Fernausschalter fuer jeden im Netz.
        // Auf Loopback kommt ohnehin nur heran, wer schon am Rechner ist - dort darf er fehlen.
        bool loopbackOnly = IPAddress.IsLoopback(address);
        if (!loopbackOnly && string.IsNullOrEmpty(rest.ApiKey))
        {
            logger.LogError(
                "Die REST-Schnittstelle soll auf {Address} lauschen, aber KioskOptions > RestApi > ApiKey " +
                "ist leer. Ohne Schluessel koennte jeder im Netz den Rechner neu starten - die " +
                "Schnittstelle wird deshalb nicht gestartet. Entweder einen Schluessel eintragen " +
                "(er wird beim naechsten Start verschluesselt abgelegt) oder BindAddress auf 127.0.0.1 setzen.",
                rest.BindAddress);
            return null;
        }

        var app = Build(options, windows, detector, platform, loggerFactory, address);

        try
        {
            await app.StartAsync();
        }
        catch (Exception ex)
        {
            // Ein belegter Port darf nicht den ganzen Kiosk verhindern - die Anzeige ist
            // wichtiger als die Fernsteuerung.
            logger.LogError(ex,
                "Die REST-Schnittstelle konnte nicht auf {Address}:{Port} starten. Der Kiosk laeuft ohne sie weiter.",
                rest.BindAddress, rest.Port);

            await app.DisposeAsync();
            return null;
        }

        logger.LogInformation("REST-Schnittstelle laeuft auf http://{Address}:{Port}{Auth}",
            rest.BindAddress, rest.Port,
            loopbackOnly && string.IsNullOrEmpty(rest.ApiKey) ? " (ohne Schluessel, nur lokal)" : "");

        if (rest.SwaggerEnabled)
            logger.LogInformation("Swagger: http://{Address}:{Port}/{Route}", rest.BindAddress, rest.Port, rest.SwaggerRoute.Trim('/'));

        return new KioskApiHost(app, logger);
    }

    private static WebApplication Build(
        KioskOptions options,
        KioskWindowManager windows,
        IDisplayModeDetector detector,
        PlatformInfo platform,
        ILoggerFactory loggerFactory,
        IPAddress address)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });

        // Die einzige Konfigurationsquelle ist AppConfig. CreateBuilder bringt von sich aus
        // appsettings.json, Umgebungsvariablen und Kommandozeile mit - genau die drei Quellen,
        // die aus dem Kiosk bewusst entfernt wurden. Sie wuerden hier still gegen AppConfig
        // arbeiten, etwa ueber ein gesetztes ASPNETCORE_URLS.
        builder.Configuration.Sources.Clear();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(address, options.RestApi.Port);

            // Der Kiosk nennt sich nicht selbst in jeder Antwort.
            kestrel.AddServerHeader = false;
        });

        // Ein einziges Log fuer Fenster und Schnittstelle, in derselben Datei.
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ILoggerFactory>(new SharedLoggerFactory(loggerFactory));

        var runner = new ProcessCommandRunner(loggerFactory.CreateLogger<ProcessCommandRunner>());

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(options.DisplayControl);
        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton(windows);
        builder.Services.AddSingleton<ICommandRunner>(runner);
        builder.Services.AddSingleton<IPowerController>(
            new PowerController(options.Power, runner, platform.Kind, loggerFactory.CreateLogger<PowerController>()));
        builder.Services.AddSingleton<IDisplayTopologyController>(
            new DisplayTopologyController(options.DisplayControl, runner, detector, platform,
                loggerFactory.CreateLogger<DisplayTopologyController>()));
        builder.Services.AddSingleton<DisplaySwitchService>();

        builder.Services.AddControllers();

        if (options.RestApi.SwaggerEnabled)
            AddSwagger(builder, options.RestApi);

        var app = builder.Build();

        app.UseMiddleware<ApiKeyMiddleware>(options.RestApi);

        if (options.RestApi.SwaggerEnabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI(ui =>
            {
                ui.SwaggerEndpoint("/swagger/v1/swagger.json", "StockTV-Kiosk v1");
                ui.RoutePrefix = options.RestApi.SwaggerRoute.Trim('/');
            });
        }

        app.MapControllers();
        return app;
    }

    private static void AddSwagger(WebApplicationBuilder builder, RestApiOptions rest)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "StockTV-Kiosk",
                Version = "v1",
                Description = "Fernsteuerung des Kiosk-Rechners: Neustart, Herunterfahren und " +
                              "Umschalten zwischen Bildschirme duplizieren und erweitern."
            });

            // Die Beschreibungen der Endpunkte stammen aus den XML-Kommentaren im Quelltext.
            string xml = Path.Combine(AppContext.BaseDirectory,
                $"{typeof(KioskApiHost).Assembly.GetName().Name}.xml");
            if (File.Exists(xml))
                swagger.IncludeXmlComments(xml);

            if (string.IsNullOrEmpty(rest.ApiKey))
                return;

            // Damit die Oberflaeche ein Eingabefeld fuer den Schluessel anbietet - ohne das
            // liefert dort jeder Aufruf 401 und niemand sieht, warum.
            swagger.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = rest.ApiKeyHeader,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = $"Der Wert aus KioskOptions > RestApi > ApiKey, als Kopfzeile {rest.ApiKeyHeader}."
            });

            // Der Verweis wird an das erzeugte Dokument gebunden - deshalb der Umweg ueber
            // die Funktion statt eines fertigen Objekts.
            swagger.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
            });
        });
    }

    /// <summary>Uebersetzt die konfigurierte Adresse; akzeptiert auch "localhost" und "*".</summary>
    internal static bool TryResolveAddress(string? configured, out IPAddress address)
    {
        string value = (configured ?? string.Empty).Trim();

        switch (value.ToLowerInvariant())
        {
            case "":
            case "localhost":
                address = IPAddress.Loopback;
                return true;
            case "*":
            case "+":
            case "any":
                address = IPAddress.Any;
                return true;
            default:
                return IPAddress.TryParse(value, out address!);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.StopAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Die REST-Schnittstelle liess sich nicht sauber beenden.");
        }

        await app.DisposeAsync();
    }
}
