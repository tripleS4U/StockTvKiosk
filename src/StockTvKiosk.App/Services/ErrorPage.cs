using System.Net;
using StockTvKiosk.Core.Layout;

namespace StockTvKiosk.App.Services;

/// <summary>
/// Lokale Statusseite, die angezeigt wird, solange die Blazor-App nicht antwortet.
/// Bewusst vollstaendig eingebettet und ohne externe Ressourcen: sie muss gerade dann
/// funktionieren, wenn kein Server erreichbar ist.
/// </summary>
internal static class ErrorPage
{
    public static string Connecting(WindowPlan plan, int attempt, int intervalSeconds)
        => Render(plan, attempt, intervalSeconds, isFirstAttempt: attempt <= 1, reason: null);

    public static string Failed(WindowPlan plan, int attempt, int intervalSeconds, string? reason)
        => Render(plan, attempt, intervalSeconds, isFirstAttempt: false, reason);

    private static string Render(
        WindowPlan plan, int attempt, int intervalSeconds, bool isFirstAttempt, string? reason)
    {
        string title = isFirstAttempt ? "Verbindung wird aufgebaut" : "StockTV ist nicht erreichbar";
        string subtitle = isFirstAttempt
            ? "Es wird auf die StockTV-Anwendung gewartet."
            : $"Erneuter Versuch alle {intervalSeconds} Sekunden.";

        string role = plan.Role == WindowRole.Primary ? "Bedienfenster" : "Spiegel-Anzeige";
        string details = reason is null ? "" :
            $"""<p class="reason">{Escape(reason)}</p>""";

        return $$"""
            <!doctype html>
            <html lang="de">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{Escape(title)}}</title>
            <style>
              :root { color-scheme: dark; }
              * { box-sizing: border-box; }
              html, body {
                height: 100%; margin: 0;
                background: #101418; color: #e6edf3;
                font-family: "Segoe UI", Roboto, "DejaVu Sans", system-ui, sans-serif;
              }
              main {
                height: 100%;
                display: flex; flex-direction: column;
                align-items: center; justify-content: center;
                gap: 1.1rem; padding: 2rem; text-align: center;
              }
              .spinner {
                width: 3.2rem; height: 3.2rem;
                border: 4px solid rgba(230,237,243,.18);
                border-top-color: #4aa3ff;
                border-radius: 50%;
                animation: spin 1s linear infinite;
              }
              @keyframes spin { to { transform: rotate(360deg); } }
              @media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 3s; } }
              h1 { margin: 0; font-size: clamp(1.4rem, 3vw, 2.2rem); font-weight: 600; }
              p  { margin: 0; font-size: clamp(.95rem, 1.6vw, 1.15rem); color: #9fb0c0; }
              .reason { color: #ffb4a2; font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: .9rem; }
              .url {
                font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
                background: rgba(255,255,255,.06); border: 1px solid rgba(255,255,255,.1);
                border-radius: .4rem; padding: .45rem .8rem; font-size: .95rem; color: #cbd6e2;
                max-width: 90vw; overflow-wrap: anywhere;
              }
              footer { position: fixed; bottom: 1.2rem; font-size: .8rem; color: #5d6b7a; }
            </style>
            </head>
            <body>
            <main>
              <div class="spinner" role="status" aria-live="polite" aria-label="{{Escape(title)}}"></div>
              <h1>{{Escape(title)}}</h1>
              <p>{{Escape(subtitle)}}</p>
              {{details}}
              <div class="url">{{Escape(plan.Url)}}</div>
            </main>
            <footer>{{Escape(role)}} &middot; Bildschirm [{{plan.ScreenIndex}}] &middot; Versuch {{attempt}}</footer>
            </body>
            </html>
            """;
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
