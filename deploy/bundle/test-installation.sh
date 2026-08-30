#!/usr/bin/env bash
#
# Prueft eine StockTV-Installation auf einem Linux-Geraet und meldet, was nicht stimmt.
# Veraendert nichts. Der Exitcode ist die Zahl der Fehler.
#
#   ./test-installation.sh
#   ./test-installation.sh --api-key "..." --install-dir /opt/stocktvapps
#
# Der haeufigste stille Fehler ist die Portabweichung: StockTV lauscht auf 8080, der Kiosk
# laedt von 5129, und zu sehen ist nur die Statusseite. Genau darauf zielt die Pruefung
# "Kiosk-Adresse passt zu StockTV".
#
set -uo pipefail

INSTALL_DIR="/opt/stocktvapps"
# Vorbelegt wie in der Installation, siehe --api-key dort.
API_KEY="Secret4StockTV?"
TIMEOUT="10"

while [ $# -gt 0 ]; do
    case "$1" in
        --install-dir) INSTALL_DIR="${2:?}"; shift 2 ;;
        --api-key)     API_KEY="${2:?}"; shift 2 ;;
        --timeout)     TIMEOUT="${2:?}"; shift 2 ;;
        -h|--help)     sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unbekannte Option: $1" >&2; exit 2 ;;
    esac
done

STOCKTV_HOME="${INSTALL_DIR}/StockTV"
KIOSK_HOME="${INSTALL_DIR}/StockTvKiosk"

FAILED=0
HINTS=0
CHECKS=0

result() {
    # $1 = Bereich, $2 = Pruefung, $3 = OK|FEHLER|HINWEIS, $4 = Details
    CHECKS=$((CHECKS + 1))
    case "$3" in
        FEHLER)  FAILED=$((FAILED + 1)) ;;
        HINWEIS) HINTS=$((HINTS + 1)) ;;
    esac
    printf '%-14s %-32s %-8s %s\n' "$1" "$2" "$3" "$4"
}

<<'NOTE'
Die Kiosk-Konfiguration verpackt jeden Abschnitt der obersten Ebene in
{ description, value }. Ohne jq wird deshalb nicht geparst, sondern gezielt gelesen -
das reicht fuer die paar Werte, um die es hier geht, und spart eine Abhaengigkeit.
NOTE
read_json_string() {
    # $1 = Datei, $2 = Schluessel -> erster Stringwert dieses Schluessels
    sed -n "s/.*\"$2\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$1" | head -n1
}
read_json_bool() {
    sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\(true\|false\).*/\1/p" "$2" | head -n1
}
port_of() {
    printf '%s' "$1" | sed -n 's|.*://[^:/]*:\([0-9]\+\).*|\1|p'
}

printf '==========================================\n'
printf '  StockTV-Installation pruefen\n'
printf '  Verzeichnis: %s\n' "$INSTALL_DIR"
printf '==========================================\n\n'
printf '%-14s %-32s %-8s %s\n' "Bereich" "Pruefung" "Status" "Details"
printf '%-14s %-32s %-8s %s\n' "-------" "--------" "------" "-------"

# ---------------------------------------------------------------------------------------
# Dateien
# ---------------------------------------------------------------------------------------

for entry in "StockTV:${STOCKTV_HOME}/StockTvBlazor" "StockTV-Kiosk:${KIOSK_HOME}/StockTvKiosk"; do
    label="${entry%%:*}"
    path="${entry#*:}"

    if [ ! -e "$path" ]; then
        result "Dateien" "$label" "FEHLER" "nicht gefunden: $path"
    elif [ ! -x "$path" ]; then
        result "Dateien" "$label" "FEHLER" "nicht ausfuehrbar - chmod +x $path"
    else
        result "Dateien" "$label" "OK" "$(date -r "$path" '+%Y-%m-%d %H:%M')"
    fi
done

# Das Gegenstueck zur WebView2-Runtime unter Windows. Fehlt WebKitGTK, startet der Kiosk
# und zeigt eine leere Flaeche - ohne Absturz, ohne Fehlermeldung.
MISSING=""
for pkg in libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0; do
    dpkg-query -W -f='${Status}' "$pkg" 2>/dev/null | grep -q "ok installed" || MISSING="${MISSING} ${pkg}"
done
if [ -z "$MISSING" ]; then
    result "Dateien" "WebKitGTK" "OK" "vollstaendig"
else
    result "Dateien" "WebKitGTK" "FEHLER" "fehlt:${MISSING} - der Kiosk zeigt ohne sie nichts an"
fi

# ---------------------------------------------------------------------------------------
# Konfiguration
# ---------------------------------------------------------------------------------------

STOCKTV_CONFIG="${STOCKTV_HOME}/_config/stocktv.config.json"
KIOSK_CONFIG="${KIOSK_HOME}/_config/AppConfig.json"

if [ -f "$STOCKTV_CONFIG" ]; then
    BAHN="$(sed -n 's/.*"BahnNummer"[[:space:]]*:[[:space:]]*\([0-9]\+\).*/\1/p' "$STOCKTV_CONFIG" | head -n1)"
    result "Konfiguration" "stocktv.config.json" "OK" "lesbar, Bahn ${BAHN:-?}"
else
    result "Konfiguration" "stocktv.config.json" "HINWEIS" \
        "fehlt - StockTV legt sie beim ersten Start mit Standardwerten an"
fi

KIOSK_PORT=""
HOST_PORT=""
BLAZOR_HOST_ENABLED=""

if [ -f "$KIOSK_CONFIG" ]; then
    PRIMARY_URL="$(read_json_string "$KIOSK_CONFIG" PrimaryUrl)"
    KIOSK_PORT="$(port_of "$PRIMARY_URL")"
    KIOSK_API_PORT="$(sed -n '/"RestApi"/,/}/ s/.*"Port"[[:space:]]*:[[:space:]]*\([0-9]\+\).*/\1/p' "$KIOSK_CONFIG" | head -n1)"
    BLAZOR_HOST_ENABLED="$(sed -n '/"BlazorHost"/,/}/ s/.*"Enabled"[[:space:]]*:[[:space:]]*\(true\|false\).*/\1/p' "$KIOSK_CONFIG" | head -n1)"
    API_KEY_SET="$(read_json_string "$KIOSK_CONFIG" ApiKey)"
    BIND_ADDRESS="$(read_json_string "$KIOSK_CONFIG" BindAddress)"
    HOST_URLS="$(read_json_string "$KIOSK_CONFIG" ASPNETCORE_URLS)"
    [ -n "$HOST_URLS" ] && HOST_PORT="$(port_of "$HOST_URLS")"

    result "Konfiguration" "AppConfig.json" "OK" "lesbar, PrimaryUrl ${PRIMARY_URL}"

    if [ "$BLAZOR_HOST_ENABLED" = "true" ]; then
        result "Konfiguration" "Betriebsart" "OK" "managed - der Kiosk startet StockTV selbst"
    else
        result "Konfiguration" "Betriebsart" "OK" "service - der Kiosk wartet nur auf die Adresse"
    fi

    # Ohne Schluessel startet die Schnittstelle bei nicht-lokaler Bindung gar nicht.
    case "$BIND_ADDRESS" in
        127.0.0.1|localhost|::1) LOOPBACK="true" ;;
        *) LOOPBACK="false" ;;
    esac

    if [ -z "$API_KEY_SET" ] && [ "$LOOPBACK" = "false" ]; then
        result "Konfiguration" "API-Schluessel" "FEHLER" \
            "leer bei BindAddress ${BIND_ADDRESS} - die Fernsteuerung startet so nicht"
    elif [ -z "$API_KEY_SET" ]; then
        result "Konfiguration" "API-Schluessel" "HINWEIS" "leer, aber nur lokal gebunden"
    else
        case "$API_KEY_SET" in
            enc:*) result "Konfiguration" "API-Schluessel" "OK" "gesetzt und verschluesselt" ;;
            *)     result "Konfiguration" "API-Schluessel" "OK" "gesetzt, wird beim naechsten Start verschluesselt" ;;
        esac
    fi
else
    result "Konfiguration" "AppConfig.json" "HINWEIS" \
        "fehlt - der Kiosk legt sie beim ersten Start mit Standardwerten an (Port 5129!)"
fi

# ---------------------------------------------------------------------------------------
# Units
# ---------------------------------------------------------------------------------------

UNIT_PORT=""
for unit in stocktv.service stocktv-kiosk.service; do
    UNIT_FILE="${HOME}/.config/systemd/user/${unit}"

    if [ ! -f "$UNIT_FILE" ]; then
        if [ "$unit" = "stocktv.service" ] && [ "$BLAZOR_HOST_ENABLED" = "true" ]; then
            result "Autostart" "$unit" "OK" "nicht vorhanden - richtig fuer die Betriebsart managed"
        else
            result "Autostart" "$unit" "HINWEIS" "nicht eingerichtet"
        fi
        continue
    fi

    STATE="$(systemctl --user is-active "$unit" 2>/dev/null || true)"
    ENABLED="$(systemctl --user is-enabled "$unit" 2>/dev/null || true)"

    [ "$unit" = "stocktv.service" ] && \
        UNIT_PORT="$(port_of "$(sed -n 's/.*ASPNETCORE_URLS=\(.*\)/\1/p' "$UNIT_FILE" | head -n1)")"

    if [ "$STATE" = "active" ]; then
        result "Autostart" "$unit" "OK" "${STATE}, ${ENABLED}"
    else
        result "Autostart" "$unit" "FEHLER" "${STATE:-unbekannt}, ${ENABLED:-unbekannt}"
    fi
done

# Der stille Klassiker: beide laufen, reden aber ueber verschiedene Ports.
EXPECTED_PORT="${UNIT_PORT:-$HOST_PORT}"
if [ -n "$KIOSK_PORT" ] && [ -n "$EXPECTED_PORT" ]; then
    if [ "$KIOSK_PORT" = "$EXPECTED_PORT" ]; then
        result "Konfiguration" "Kiosk-Adresse passt zu StockTV" "OK" "beide Port ${KIOSK_PORT}"
    else
        result "Konfiguration" "Kiosk-Adresse passt zu StockTV" "FEHLER" \
            "Kiosk laedt von ${KIOSK_PORT}, StockTV hoert auf ${EXPECTED_PORT}"
    fi
fi

# ---------------------------------------------------------------------------------------
# Firewall
# ---------------------------------------------------------------------------------------

# Raspberry Pi OS bringt keine aktive Firewall mit. Ist ufw eingeschaltet, muessen die
# Ports aber durch - und von hier aus faellt das sonst nicht auf: die HTTP-Pruefungen
# weiter unten laufen ueber localhost und merken von einer Sperre nichts.
if ! command -v ufw >/dev/null 2>&1; then
    result "Firewall" "ufw" "OK" "nicht vorhanden - nichts zu sperren"
elif ! ufw status 2>/dev/null | head -n1 | grep -q "active"; then
    result "Firewall" "ufw" "OK" "nicht aktiv - nichts zu sperren"
else
    UFW_RULES="$(ufw status 2>/dev/null || true)"
    BLOCKED=""
    for spec in "${KIOSK_PORT:-8080}/tcp" "4747/tcp" "4748/tcp" "${KIOSK_API_PORT:-8099}/tcp" "5353/udp"; do
        printf '%s' "$UFW_RULES" | grep -q "${spec%%/*}" || BLOCKED="${BLOCKED} ${spec}"
    done

    if [ -z "$BLOCKED" ]; then
        result "Firewall" "ufw" "OK" "alle Ports freigegeben"
    else
        result "Firewall" "ufw" "FEHLER" "aktiv, aber nicht freigegeben:${BLOCKED}"
    fi
fi

# ---------------------------------------------------------------------------------------
# Netz
# ---------------------------------------------------------------------------------------

# curl gibt bei einem Verbindungsfehler selbst schon "000" aus und beendet sich dabei mit
# einem Fehlercode. Ein zusaetzliches "|| printf 000" haengte eine zweite Null-Gruppe an -
# heraus kaeme "000000".
probe() {
    local code
    code="$(curl --silent --output /dev/null --write-out '%{http_code}' \
        --max-time "$TIMEOUT" "$@" 2>/dev/null)"
    printf '%s' "${code:-000}"
}

if [ -n "$KIOSK_PORT" ]; then
    CODE="$(probe "http://localhost:${KIOSK_PORT}/")"
    if [ "$CODE" != "000" ]; then
        # Jede HTTP-Antwort zaehlt: der Server steht dann, und das ist die Frage.
        result "Netz" "StockTV antwortet" "OK" "http://localhost:${KIOSK_PORT}/ -> HTTP ${CODE}"
    elif [ "$BLAZOR_HOST_ENABLED" = "true" ] && ! pgrep -x StockTvKiosk >/dev/null 2>&1; then
        result "Netz" "StockTV antwortet" "HINWEIS" \
            "keine Antwort - der Kiosk laeuft nicht, und in dieser Betriebsart startet er StockTV"
    else
        result "Netz" "StockTV antwortet" "FEHLER" "keine Antwort auf Port ${KIOSK_PORT}"
    fi
fi

if [ -n "${KIOSK_API_PORT:-}" ]; then
    if [ -n "$API_KEY" ]; then
        CODE="$(probe -H "X-Api-Key: ${API_KEY}" "http://localhost:${KIOSK_API_PORT}/api/system/capabilities")"
    else
        CODE="$(probe "http://localhost:${KIOSK_API_PORT}/api/system/capabilities")"
    fi

    case "$CODE" in
        000) result "Netz" "Kiosk-Fernsteuerung" "HINWEIS" "keine Antwort auf Port ${KIOSK_API_PORT} - laeuft der Kiosk?" ;;
        200) result "Netz" "Kiosk-Fernsteuerung" "OK" "antwortet${API_KEY:+ mit Schluessel}" ;;
        401) if [ -n "$API_KEY" ]; then
                 result "Netz" "Kiosk-Fernsteuerung" "FEHLER" "Schluessel abgelehnt"
             else
                 result "Netz" "Kiosk-Fernsteuerung" "OK" "steht, Schluessel nicht geprueft (--api-key angeben)"
             fi ;;
        *)   result "Netz" "Kiosk-Fernsteuerung" "HINWEIS" "HTTP ${CODE}" ;;
    esac
fi

# ---------------------------------------------------------------------------------------

printf '\n==========================================\n'
if [ "$FAILED" -eq 0 ]; then
    printf 'Keine Fehler. %s Pruefungen, %s Hinweise.\n' "$CHECKS" "$HINTS"
else
    printf '%s von %s Pruefungen fehlgeschlagen.\n' "$FAILED" "$CHECKS"
fi
printf '==========================================\n'

exit "$FAILED"
