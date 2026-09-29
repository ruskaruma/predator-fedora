#!/usr/bin/env bash
# Run the PredatorCore daemon (from daemon/) in place of an existing DAMX daemon install.
#
# Uses a systemd drop-in on damx-daemon.service, so the original unit stays untouched.
# Undo with:  sudo rm /etc/systemd/system/damx-daemon.service.d/predatorcore.conf && sudo systemctl daemon-reload && sudo systemctl restart damx-daemon
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="/opt/predatorcore/daemon"
UNIT="damx-daemon.service"
DROPIN_DIR="/etc/systemd/system/${UNIT}.d"

[ "$(id -u)" -eq 0 ] || { echo "Run as root (sudo $0)"; exit 1; }
systemctl cat "$UNIT" >/dev/null 2>&1 || { echo "$UNIT not found. Install the DAMX suite (driver + daemon) first."; exit 1; }
command -v python3 >/dev/null || { echo "python3 is required"; exit 1; }

echo "==> Installing daemon to $DEST"
install -d "$DEST"
install -m 644 "$REPO_DIR"/daemon/*.py "$DEST/"
python3 -m py_compile "$DEST"/*.py

echo "==> Pointing $UNIT at the PredatorCore daemon"
install -d "$DROPIN_DIR"
cat > "$DROPIN_DIR/predatorcore.conf" <<EOF
[Service]
ExecStart=
ExecStart=/usr/bin/python3 $DEST/predatorcore_daemon.py
WorkingDirectory=$DEST
EOF

systemctl daemon-reload
systemctl restart "$UNIT"
sleep 2
systemctl is-active --quiet "$UNIT" && echo "==> $UNIT is running the PredatorCore daemon" || {
    echo "!! $UNIT failed to start; recent log:"; journalctl -u "$UNIT" -n 20 --no-pager; exit 1; }
