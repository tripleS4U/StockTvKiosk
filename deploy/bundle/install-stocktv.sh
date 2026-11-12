#!/usr/bin/env bash
#
# Installiert StockTV und den StockTV-Kiosk auf einem Linux-Geraet (Raspberry Pi 4, 64-bit).
#
# Laeuft auf dem Zielgeraet, im Ordner des kopierten Verteilpakets:
#
#   ./install-stocktv.sh
#   ./install-stocktv.sh --mode managed --bahn 3
#   ./install-stocktv.sh --dry-run
#
# Zwei Betriebsarten:
#
#   service   Zwei systemd-User-Units: stocktv.service und stocktv-kiosk.service. Der Kiosk
#             startet StockTV nicht selbst, sondern wartet nur auf die Adresse - ein Neustart
#             der Anzeige laesst die laufende Wertung damit unangetastet. Vorgabe.
#
#   managed   Nur stocktv-kiosk.service. Der Kiosk startet StockTV selbst und faehrt es beim
#             Beenden mit herunter - eine einzige Einheit.
#
# Vorhandene Konfigurationsdateien werden NICHT ueberschrieben. Ein Update laesst damit
# Bahnnummer, Farben und den API-Schluessel stehen; --reset-config setzt sie zurueck.
#
set -euo pipefail

MODE="service"
INSTALL_DIR="/opt/stocktvapps"
PORT="8080"
KIOSK_API_PORT="8099"
# Vorbelegt, damit alle Geraete gleich eingerichtet sind. Der Preis: wer ihn von einem
# Geraet kennt, kommt auf alle - eine Huerde gegen versehentliche Zugriffe, kein
# Geraeteschutz. Pro Geraet aendern mit --generate-api-key, --api-key oder spaeter ueber
# POST /api/config/api-key.
API_KEY="Secret4StockTV?"
GENERATE_API_KEY="false"
BAHN=""
DISPLAY_NAME=":0"
NO_KIOSK="false"
RESET_CONFIG="false"
SKIP_DEPS="false"
SKIP_FIREWALL="false"
DRY_RUN="false"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

usage() {
    sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'
    cat <<'EOF'
Optionen:
  --mode <service|managed>  Betriebsart (Vorgabe service)
  --install-dir <Pfad>      Zielverzeichnis (Vorgabe /opt/stocktvapps)
  --port <N>                HTTP-Port von StockTV (Vorgabe 8080)
  --api-port <N>            Port der Kiosk-Fernsteuerung (Vorgabe 8099)
  --api-key <Wert>          Schluessel der Fernsteuerung (Vorgabe: fest vorbelegt)
  --generate-api-key        Statt des vorbelegten einen zufaelligen Schluessel erzeugen
  --bahn <N>                Bahnnummer in die StockTV-Konfiguration eintragen
  --display <:N>            X-Display fuer den Kiosk (Vorgabe :0)
  --no-kiosk                Dateien kopieren, aber keine Kiosk-Unit einrichten
  --reset-config            Vorhandene Konfiguration durch die Vorlagen ersetzen
  --skip-deps               Die WebKitGTK-Pakete nicht pruefen
  --skip-firewall           ufw nicht anfassen
  --dry-run                 Nur anzeigen, was geschehen wuerde
  -h, --help                Diese Hilfe
EOF
}

while [ $# -gt 0 ]; do
    case "$1" in
        --mode)        MODE="${2:?--mode braucht service oder managed}"; shift 2 ;;
        --install-dir) INSTALL_DIR="${2:?--install-dir braucht einen Pfad}"; shift 2 ;;
        --port)        PORT="${2:?--port braucht einen Wert}"; shift 2 ;;
        --api-port)    KIOSK_API_PORT="${2:?--api-port braucht einen Wert}"; shift 2 ;;
        --api-key)     API_KEY="${2:?--api-key braucht einen Wert}"; shift 2 ;;
        --generate-api-key) GENERATE_API_KEY="true"; shift ;;
        --bahn)        BAHN="${2:?--bahn braucht eine Zahl}"; shift 2 ;;
        --display)     DISPLAY_NAME="${2:?--display braucht einen Wert}"; shift 2 ;;
        --no-kiosk)    NO_KIOSK="true"; shift ;;
        --reset-config) RESET_CONFIG="true"; shift ;;
        --skip-deps)   SKIP_DEPS="true"; shift ;;
        --skip-firewall) SKIP_FIREWALL="true"; shift ;;
        --dry-run)     DRY_RUN="true"; shift ;;
        -h|--help)     usage; exit 0 ;;
        *) echo "Unbekannte Option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

case "$MODE" in
    service|managed) ;;
    *) echo "Fehler: --mode muss service oder managed sein, nicht '$MODE'." >&2; exit 2 ;;
esac

# Die Vorlagen heissen wie unter Windows AppConfig.Service/Managed.linux.json. Linux unterscheidet
# Gross- und Kleinschreibung - mit "service" eingesetzt fand das Skript die Datei nicht.
case "$MODE" in
    service) MODE_FILE="Service" ;;
    managed) MODE_FILE="Managed" ;;
esac

STOCKTV_HOME="${INSTALL_DIR}/StockTV"
KIOSK_HOME="${INSTALL_DIR}/StockTvKiosk"
STOCKTV_SOURCE="${SCRIPT_DIR}/StockTvApps/StockTV"
KIOSK_SOURCE="${SCRIPT_DIR}/StockTvApps/StockTvKiosk"
CONFIG_SOURCE="${SCRIPT_DIR}/config"
UNIT_DIR="${HOME}/.config/systemd/user"

WARNINGS=()

step() { printf '  %s\n' "$1"; }
warn() { printf '  ! %s\n' "$1" >&2; WARNINGS+=("$1"); }
title() { printf '\n%s\n' "$1"; }

# Nur dort erhoehen, wo es noetig ist: /opt gehoert root, ein Zielverzeichnis im
# Benutzerordner nicht. Blind zu sudo'n wuerde auf einem Pi ohne sudo-Recht scheitern,
# obwohl gar nichts fehlt - und bei jedem Lauf unnoetig nach dem Passwort fragen.
NEED_SUDO="false"
probe_dir="$INSTALL_DIR"
while [ ! -e "$probe_dir" ] && [ "$probe_dir" != "/" ] && [ "$probe_dir" != "." ]; do
    probe_dir="$(dirname "$probe_dir")"
done
if [ ! -w "$probe_dir" ] && [ "$(id -u)" -ne 0 ]; then
    NEED_SUDO="true"
fi

maybe_sudo() {
    if [ "$NEED_SUDO" = "true" ]; then sudo "$@"; else "$@"; fi
}

# Paketverwaltung und Firewall brauchen root, ganz gleich wohin installiert wird - dafuer
# taugt maybe_sudo nicht, das sich nach dem Zielverzeichnis richtet.
run_root() {
    if [ "$(id -u)" -eq 0 ]; then "$@"; else sudo "$@"; fi
}

printf '==========================================\n'
printf '  StockTV-Installation\n'
printf '  Betriebsart: %s\n' "$MODE"
printf '  Ziel:        %s\n' "$INSTALL_DIR"
printf '==========================================\n'

# ---------------------------------------------------------------------------------------
title "1. Verteilpaket pruefen"
# ---------------------------------------------------------------------------------------

for required in \
    "${STOCKTV_SOURCE}/StockTvBlazor" \
    "${KIOSK_SOURCE}/StockTvKiosk" \
    "${CONFIG_SOURCE}/stocktv.config.json" \
    "${CONFIG_SOURCE}/stocktv.device.json" \
    "${CONFIG_SOURCE}/AppConfig.${MODE_FILE}.linux.json"
do
    if [ ! -e "$required" ]; then
        echo "Fehlt im Paket: $required" >&2
        exit 1
    fi
done
step "Paket vollstaendig."

# Verteilpaket und Installation duerfen nicht derselbe Ordner sein - sonst kopierte die
# Installation die Anwendungen in genau das Verzeichnis, das sie enthaelt.
case "$(cd "$INSTALL_DIR" 2>/dev/null && pwd || echo "$INSTALL_DIR")" in
    "$SCRIPT_DIR"|"$SCRIPT_DIR"/*)
        echo "Das Verteilpaket liegt in ${SCRIPT_DIR}, installiert werden soll nach ${INSTALL_DIR}." >&2
        echo "Das waere dasselbe Verzeichnis (oder eines darunter) - bitte --install-dir setzen." >&2
        exit 1 ;;
esac

if [ "$DRY_RUN" = "true" ]; then
    printf '\n  --dry-run: ab hier wird nichts veraendert.\n'
fi

# ---------------------------------------------------------------------------------------
title "2. Abhaengigkeiten"
# ---------------------------------------------------------------------------------------

# Das Gegenstueck zur WebView2-Runtime unter Windows: der Kiosk nutzt die native Engine der
# Plattform. Fehlt WebKitGTK, startet er und zeigt nichts an - ohne Absturz, ohne Meldung.
if [ "$SKIP_DEPS" = "true" ]; then
    step "Uebersprungen (--skip-deps)."
else
    MISSING=""
    for pkg in libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0; do
        if ! dpkg-query -W -f='${Status}' "$pkg" 2>/dev/null | grep -q "ok installed"; then
            MISSING="${MISSING} ${pkg}"
        fi
    done

    if [ -z "$MISSING" ]; then
        step "WebKitGTK vorhanden."
    elif [ "$DRY_RUN" = "true" ]; then
        step "wuerde nachinstallieren:${MISSING}"
    else
        step "Fehlt:${MISSING} - wird nachinstalliert."
        # Ohne aktuelle Paketlisten (frisches Image, lange nicht aktualisiert) findet apt die Pakete nicht.
        run_root apt-get update -qq || warn "apt-get update ist fehlgeschlagen - es wird mit den vorhandenen Paketlisten versucht."
        if run_root apt-get install -y ${MISSING}; then
            step "Nachinstalliert."
        else
            warn "Die Pakete${MISSING} liessen sich nicht installieren. Der Kiosk zeigt ohne sie nichts an."
        fi
    fi
fi

# ---------------------------------------------------------------------------------------
title "3. Laufendes anhalten"
# ---------------------------------------------------------------------------------------

if [ "$DRY_RUN" = "true" ]; then
    step "wuerde die Units anhalten"
else
    for unit in stocktv-kiosk.service stocktv.service; do
        if systemctl --user is-active --quiet "$unit" 2>/dev/null; then
            systemctl --user stop "$unit"
            step "${unit} gestoppt."
        fi
    done
    # StockTV gibt seine NetMQ-Ports erst beim Beenden frei.
    sleep 1
fi

# ---------------------------------------------------------------------------------------
title "4. Dateien kopieren"
# ---------------------------------------------------------------------------------------

copy_app() {
    local from="$1" to="$2" label="$3"

    if [ "$DRY_RUN" = "true" ]; then
        step "wuerde ${label} nach ${to} kopieren"
        return
    fi

    maybe_sudo mkdir -p "$to" "${to}/_config" "${to}/_logs"

    # Beide Anwendungen schreiben ihre Konfiguration selbst zurueck. Gehoerte das
    # Verzeichnis root, koennten sie das nicht - und der Fehler faellt erst auf, wenn eine
    # Einstellung nicht gespeichert wird. Nur noetig, wenn ueberhaupt erhoeht wurde.
    # VOR dem Kopieren: kopiert wird als Benutzer, und in einen frisch per sudo angelegten
    # Ordner duerfte er sonst nicht schreiben (Erstinstallation nach /opt).
    if [ "$NEED_SUDO" = "true" ]; then
        maybe_sudo chown -R "$(id -un):$(id -gn)" "$to"
    fi

    # _config und _logs bleiben aussen vor: eine vorhandene Konfiguration gehoert dem Geraet
    # und darf ein Update nicht verlieren.
    ( cd "$from" && find . -maxdepth 1 -mindepth 1 \
        ! -name '_config' ! -name '_logs' -exec cp -r {} "$to/" \; )

    step "${label} nach ${to} ($(find "$from" -type f | wc -l) Dateien)."
}

copy_app "$STOCKTV_SOURCE" "$STOCKTV_HOME" "StockTV"
copy_app "$KIOSK_SOURCE"   "$KIOSK_HOME"   "StockTV-Kiosk"

if [ "$DRY_RUN" != "true" ]; then
    chmod +x "${STOCKTV_HOME}/StockTvBlazor" "${KIOSK_HOME}/StockTvKiosk"
    step "Ausfuehrbar gesetzt."
fi

# ---------------------------------------------------------------------------------------
title "5. Konfiguration"
# ---------------------------------------------------------------------------------------

GENERATED_KEY="false"
if [ "$GENERATE_API_KEY" = "true" ] || [ -z "$API_KEY" ]; then
    API_KEY="$(head -c 24 /dev/urandom | base64)"
    GENERATED_KEY="true"
fi

install_config() {
    local template="$1" target="$2" label="$3"

    if [ -e "$target" ] && [ "$RESET_CONFIG" != "true" ]; then
        step "${label} bleibt unveraendert (schon vorhanden) - mit --reset-config ersetzen."
        return 1
    fi

    if [ "$DRY_RUN" = "true" ]; then
        step "wuerde ${label} schreiben: ${target}"
        return 0
    fi

    mkdir -p "$(dirname "$target")"
    cp "$template" "$target"
    step "${label} geschrieben: ${target}"
    return 0
}

STOCKTV_CONFIG="${STOCKTV_HOME}/_config/stocktv.config.json"
KIOSK_CONFIG="${KIOSK_HOME}/_config/AppConfig.json"

if install_config "${CONFIG_SOURCE}/stocktv.config.json" "$STOCKTV_CONFIG" "StockTV-Konfiguration"; then
    if [ "$DRY_RUN" != "true" ] && [ -n "$BAHN" ]; then
        sed -i "s/\"BahnNummer\"[[:space:]]*:[[:space:]]*[0-9]\+/\"BahnNummer\": ${BAHN}/" "$STOCKTV_CONFIG"
        step "Bahnnummer ${BAHN} eingetragen."
    fi
fi

KIOSK_CONFIG_WRITTEN="false"
if install_config "${CONFIG_SOURCE}/AppConfig.${MODE_FILE}.linux.json" "$KIOSK_CONFIG" "Kiosk-Konfiguration (${MODE})"; then
    KIOSK_CONFIG_WRITTEN="true"

    if [ "$DRY_RUN" != "true" ]; then
        # Die Vorlagen sind auf 8080 und /opt/stocktvapps geschrieben; hier wird beides auf
        # die tatsaechlichen Werte gezogen. PrimaryUrl ist die einzige Stelle fuer die
        # Adresse - ASPNETCORE_URLS leitet der Kiosk davon ab, wo es nicht ausdruecklich steht.
        sed -i \
            -e "s|http://localhost:8080/|http://localhost:${PORT}/|g" \
            -e "s|http://+:8080|http://+:${PORT}|g" \
            -e "s|/opt/stocktvapps/StockTV/StockTvBlazor|${STOCKTV_HOME}/StockTvBlazor|g" \
            -e "s|\"ApiKey\": \"\"|\"ApiKey\": \"${API_KEY//\//\\/}\"|" \
            "$KIOSK_CONFIG"

        # Der Port der Fernsteuerung steht im RestApi-Abschnitt, also nach dem ApiKey-Feld.
        sed -i "/\"RestApi\"/,/}/ s/\"Port\"[[:space:]]*:[[:space:]]*[0-9]\+/\"Port\": ${KIOSK_API_PORT}/" "$KIOSK_CONFIG"

        step "Port ${PORT}, Fernsteuerung ${KIOSK_API_PORT}."
    fi
fi

# StockTV-Geraete-Datei: REST-Schnittstelle (Port 8098) mit demselben Schluessel wie der Kiosk.
# Fehlt sie, teilt StockTV beim ersten Start die Betriebs-Konfiguration auf und legt sie mit LEEREM
# Schluessel an - dann startet die REST-Schnittstelle nicht, und StockSuite findet die Anzeige nicht.
# Eine vorhandene Datei behaelt ihren Schluessel, ausser er ist leer: dann wird er nachgetragen.
STOCKTV_DEVICE="${STOCKTV_HOME}/_config/stocktv.device.json"
STOCKTV_API_PORT="8098"
STOCKTV_KEY_WRITTEN="false"
KEY_SED="s|\"ApiKey\"[[:space:]]*:[[:space:]]*\"\"|\"ApiKey\": \"${API_KEY//\//\\/}\"|"

if [ -e "$STOCKTV_DEVICE" ] && [ "$RESET_CONFIG" != "true" ]; then
    if grep -Eq '"ApiKey"[[:space:]]*:[[:space:]]*""' "$STOCKTV_DEVICE"; then
        if [ "$DRY_RUN" = "true" ]; then
            step "wuerde den leeren API-Schluessel in ${STOCKTV_DEVICE} eintragen"
        else
            sed -i "$KEY_SED" "$STOCKTV_DEVICE"
            STOCKTV_KEY_WRITTEN="true"
            warn "StockTV hatte keinen API-Schluessel - die REST-Schnittstelle war aus. Schluessel nachgetragen; wirkt nach dem naechsten Start von StockTV."
        fi
    else
        step "StockTV-Geraete-Datei bleibt unveraendert (schon vorhanden, mit Schluessel) - mit --reset-config ersetzen."
    fi
elif [ "$DRY_RUN" = "true" ]; then
    step "wuerde StockTV-Geraete-Datei schreiben: ${STOCKTV_DEVICE}"
else
    mkdir -p "$(dirname "$STOCKTV_DEVICE")"
    cp "${CONFIG_SOURCE}/stocktv.device.json" "$STOCKTV_DEVICE"
    sed -i "$KEY_SED" "$STOCKTV_DEVICE"

    # Eine alte Einzeldatei (vor der Aufteilung in drei Dateien) traegt Datei-Protokoll und Netzwerk selbst.
    # Die neue Geraete-Datei verhindert, dass StockTV sie noch aufteilt - also hier uebernehmen.
    if [ -e "$STOCKTV_CONFIG" ]; then
        if grep -Eq '"FileLoggingEnabled"[[:space:]]*:[[:space:]]*false' "$STOCKTV_CONFIG"; then
            sed -i 's/"FileLoggingEnabled"[[:space:]]*:[[:space:]]*true/"FileLoggingEnabled": false/' "$STOCKTV_DEVICE"
        fi
        if tr -d '\n\r' < "$STOCKTV_CONFIG" | grep -Eq '"Network"[[:space:]]*:[[:space:]]*\{[[:space:]]*"Enabled"[[:space:]]*:[[:space:]]*false'; then
            sed -i '/"Network"/,/}/ s/"Enabled"[[:space:]]*:[[:space:]]*true/"Enabled": false/' "$STOCKTV_DEVICE"
        fi
    fi

    STOCKTV_KEY_WRITTEN="true"
    step "StockTV-Geraete-Datei geschrieben: ${STOCKTV_DEVICE} (REST-Schnittstelle auf Port ${STOCKTV_API_PORT})"
fi

if [ "$KIOSK_CONFIG_WRITTEN" != "true" ] && [ "$STOCKTV_KEY_WRITTEN" != "true" ] && [ "$GENERATED_KEY" = "true" ]; then
    API_KEY=""
    warn "Die vorhandenen Konfigurationen behalten ihren bisherigen API-Schluessel."
elif [ "$KIOSK_CONFIG_WRITTEN" != "true" ] && [ "$STOCKTV_KEY_WRITTEN" = "true" ]; then
    warn "Der Kiosk behaelt seinen bisherigen API-Schluessel; StockTV hat den unten angezeigten."
fi

# ---------------------------------------------------------------------------------------
title "6. Autostart (systemd-User-Units)"
# ---------------------------------------------------------------------------------------

# Bewusst User-Units: der Kiosk braucht die grafische Sitzung des angemeldeten Benutzers,
# und ohne sudo bleibt die Installation nachvollziehbar.
UNITS="stocktv-kiosk.service"
[ "$MODE" = "service" ] && UNITS="stocktv.service ${UNITS}"
[ "$NO_KIOSK" = "true" ] && UNITS="$(echo "$UNITS" | sed 's/stocktv-kiosk.service//')"

render_unit() {
    sed -e "s|__KIOSK_DIR__|${KIOSK_HOME}|g" \
        -e "s|__STOCKTV_DIR__|${STOCKTV_HOME}|g" \
        -e "s|__DISPLAY__|${DISPLAY_NAME}|g" \
        -e "s|__PORT__|${PORT}|g" \
        "${SCRIPT_DIR}/units/$1"
}

if [ -z "$(echo "$UNITS" | tr -d ' ')" ]; then
    step "Keine Units einzurichten."
elif [ "$DRY_RUN" = "true" ]; then
    for unit in $UNITS; do
        printf '\n===== %s =====\n' "$unit"
        render_unit "$unit"
    done
else
    mkdir -p "$UNIT_DIR"
    for unit in $UNITS; do
        render_unit "$unit" > "${UNIT_DIR}/${unit}"
        step "Geschrieben: ${UNIT_DIR}/${unit}"
    done

    systemctl --user daemon-reload
    for unit in $UNITS; do
        systemctl --user enable "$unit" >/dev/null
        systemctl --user restart "$unit"
        step "${unit} aktiviert und gestartet."
    done
fi

# ---------------------------------------------------------------------------------------
title "7. Firewall"
# ---------------------------------------------------------------------------------------

# Raspberry Pi OS bringt keine aktive Firewall mit. Ist ufw da und eingeschaltet, muessen
# die Ports aber durch - sonst kommt aus dem Netz niemand an die Bedienoberflaeche.
if [ "$SKIP_FIREWALL" = "true" ]; then
    step "Uebersprungen (--skip-firewall)."
elif ! command -v ufw >/dev/null 2>&1; then
    step "Kein ufw vorhanden - auf diesem System ist nichts freizugeben."
elif ! ufw status 2>/dev/null | head -n1 | grep -q "active"; then
    step "ufw ist nicht aktiv - nichts freizugeben."
else
    #  8080/tcp  Bedienoberflaeche   4747,4748/tcp  NetMQ zu den Anzeigen
    #  8098/tcp  StockTV-REST (StockSuite)
    #  8099/tcp  Kiosk-Fernsteuerung 5353/udp       Geraetesuche (mDNS)
    for spec in "${PORT}/tcp" "4747/tcp" "4748/tcp" "${STOCKTV_API_PORT}/tcp" "${KIOSK_API_PORT}/tcp" "5353/udp"; do
        if [ "$DRY_RUN" = "true" ]; then
            step "wuerde freigeben: ${spec}"
        elif run_root ufw allow "$spec" >/dev/null 2>&1; then
            step "Freigegeben: ${spec}"
        else
            warn "Port ${spec} liess sich nicht freigeben - von Hand: sudo ufw allow ${spec}"
        fi
    done
fi

# ---------------------------------------------------------------------------------------
title "8. Zusammenfassung"
# ---------------------------------------------------------------------------------------

CURRENT_USER="${USER:-$(id -un)}"

cat <<EOF

  StockTV        ${STOCKTV_HOME}
  Kiosk          ${KIOSK_HOME}
  Bedienfenster  http://localhost:${PORT}/
  Im Netz        http://$(hostname):${PORT}
  Fernsteuerung  http://$(hostname):${KIOSK_API_PORT}/swagger
  StockTV-REST   http://$(hostname):${STOCKTV_API_PORT}  (fuer StockSuite, gleicher Schluessel)
EOF

if [ -n "$API_KEY" ]; then
    cat <<EOF

  API-Schluessel: ${API_KEY}
EOF

    if [ "$GENERATED_KEY" = "true" ]; then
        cat <<EOF
  Jetzt notieren - nach dem ersten Start des Kiosks steht er nur noch
  verschluesselt in der Datei und laesst sich nicht mehr ablesen.
EOF
    else
        cat <<EOF
  Das ist der vorbelegte Schluessel - auf allen so eingerichteten Geraeten derselbe.
  Pro Geraet aendern:
    curl -X POST -H "X-Api-Key: ${API_KEY}" -H "Content-Type: application/json" \\
         -d '{"newKey":"..."}' http://localhost:${KIOSK_API_PORT}/api/config/api-key
EOF
    fi
fi

if [ "$MODE" = "service" ] && [ "$DRY_RUN" != "true" ]; then
    cat <<EOF

  Soll StockTV schon vor der Anmeldung laufen:
    sudo loginctl enable-linger ${CURRENT_USER}
EOF
fi

if [ ${#WARNINGS[@]} -gt 0 ]; then
    printf '\n  Hinweise:\n'
    for w in "${WARNINGS[@]}"; do printf '   - %s\n' "$w"; done
fi

if [ "$DRY_RUN" = "true" ]; then
    printf '\nProbelauf beendet - es wurde nichts veraendert.\n\n'
else
    printf '\nFertig. Pruefen mit:  ./test-installation.sh\n\n'
fi
