#!/usr/bin/env bash
#
# Entfernt die StockTV-Kiosk-User-Services wieder.
#
set -euo pipefail

UNIT_DIR="${HOME}/.config/systemd/user"

for unit in stocktv-kiosk.service stocktv.service; do
    if [ -f "${UNIT_DIR}/${unit}" ]; then
        systemctl --user stop "$unit" 2>/dev/null || true
        systemctl --user disable "$unit" 2>/dev/null || true
        rm -f "${UNIT_DIR}/${unit}"
        echo "Entfernt: ${unit}"
    fi
done

systemctl --user daemon-reload
echo "Fertig."
