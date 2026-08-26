#!/usr/bin/env bash
#
# Richtet den StockTV-Kiosk als systemd-User-Services ein.
#
# Bewusst User-Units statt System-Units: der Kiosk braucht die grafische Sitzung des
# angemeldeten Benutzers, und ohne sudo bleibt die Installation nachvollziehbar.
# Auf einem Pi mit Autologin ist das genau das richtige Modell.
#
#   ./install-linux.sh --kiosk-dir /opt/stocktv-kiosk
#   ./install-linux.sh --kiosk-dir /opt/stocktv-kiosk --stocktv-dir /opt/stocktv
#   ./install-linux.sh --kiosk-dir /opt/stocktv-kiosk --display :0 --port 5129
#
set -euo pipefail

KIOSK_DIR=""
STOCKTV_DIR=""
DISPLAY_NAME=":0"
PORT="5129"
DRY_RUN="false"

usage() {
    sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
    cat <<'EOF'

Optionen:
  --kiosk-dir <Pfad>    Verzeichnis mit StockTvKiosk (Pflicht)
  --stocktv-dir <Pfad>  Verzeichnis mit StockTvBlazor; weggelassen = nur Kiosk einrichten
  --display <:N>        X-Display fuer den Kiosk (Standard :0)
  --port <N>            Port der Blazor-Anwendung (Standard 5129)
  --dry-run             Units nur erzeugen und anzeigen, nichts installieren
  -h, --help            Diese Hilfe
EOF
}

while [ $# -gt 0 ]; do
    case "$1" in
        --kiosk-dir)   KIOSK_DIR="${2:?--kiosk-dir braucht einen Pfad}"; shift 2 ;;
        --stocktv-dir) STOCKTV_DIR="${2:?--stocktv-dir braucht einen Pfad}"; shift 2 ;;
        --display)     DISPLAY_NAME="${2:?--display braucht einen Wert}"; shift 2 ;;
        --port)        PORT="${2:?--port braucht einen Wert}"; shift 2 ;;
        --dry-run)     DRY_RUN="true"; shift ;;
        -h|--help)     usage; exit 0 ;;
        *) echo "Unbekannte Option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [ -z "$KIOSK_DIR" ]; then
    echo "Fehler: --kiosk-dir ist erforderlich." >&2
    usage >&2
    exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
UNIT_DIR="${HOME}/.config/systemd/user"

KIOSK_DIR="$(cd "$KIOSK_DIR" 2>/dev/null && pwd || echo "$KIOSK_DIR")"
if [ ! -x "${KIOSK_DIR}/StockTvKiosk" ]; then
    echo "Warnung: ${KIOSK_DIR}/StockTvKiosk ist nicht ausfuehrbar." >&2
    echo "         Nachholen mit: chmod +x ${KIOSK_DIR}/StockTvKiosk" >&2
fi

mkdir -p "$UNIT_DIR"

render() {
    # $1 = Vorlage, $2 = Ziel
    sed -e "s|__KIOSK_DIR__|${KIOSK_DIR}|g" \
        -e "s|__STOCKTV_DIR__|${STOCKTV_DIR}|g" \
        -e "s|__DISPLAY__|${DISPLAY_NAME}|g" \
        -e "s|__PORT__|${PORT}|g" \
        "$1" > "$2"
}

UNITS="stocktv-kiosk.service"
[ -n "$STOCKTV_DIR" ] && UNITS="stocktv.service ${UNITS}"

if [ "$DRY_RUN" = "true" ]; then
    for unit in $UNITS; do
        echo "===== ${unit} ====="
        render "${SCRIPT_DIR}/${unit}" /dev/stdout
        echo
    done
    exit 0
fi

for unit in $UNITS; do
    render "${SCRIPT_DIR}/${unit}" "${UNIT_DIR}/${unit}"
    echo "Geschrieben: ${UNIT_DIR}/${unit}"
done

systemctl --user daemon-reload

for unit in $UNITS; do
    systemctl --user enable "$unit"
done

# Ohne Linger laufen User-Units erst ab der Anmeldung. Auf einem Kiosk-Pi mit Autologin
# reicht das; ohne Autologin sorgt Linger dafuer, dass StockTV schon beim Booten laeuft.
if [ -n "$STOCKTV_DIR" ]; then
    # $USER ist unter "set -u" nicht verlaesslich - in einer Nicht-Login-Shell ist es
    # schlicht nicht gesetzt und das Skript braeche hier ab. id -un liefert immer.
    CURRENT_USER="${USER:-$(id -un)}"
    echo
    echo "Hinweis: Soll StockTV bereits vor der Anmeldung laufen:"
    echo "  sudo loginctl enable-linger ${CURRENT_USER}"
fi

cat <<EOF

Fertig. Naechste Schritte:
  systemctl --user start stocktv-kiosk.service
  systemctl --user status stocktv-kiosk.service
  journalctl --user -u stocktv-kiosk.service -f

Deinstallieren: ./uninstall-linux.sh
EOF
