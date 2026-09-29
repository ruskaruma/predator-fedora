#!/usr/bin/env bash
# Installer shipped inside the release tarball (becomes install.sh there).
# Installs the prebuilt PredatorCore app. Run ./install-daemon.sh as well for power limits,
# fan curves and GPU power control (needs an existing DAMX / Linuwu-Sense setup).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="/opt/predatorcore"
SUDO=""
[ "$(id -u)" -eq 0 ] || SUDO="sudo"

echo "==> Installing PredatorCore to $PREFIX"
$SUDO install -d "$PREFIX"
$SUDO install -m 755 "$HERE/bin/PredatorCore" "$PREFIX/PredatorCore"
$SUDO install -m 644 "$HERE/icon.png" "$PREFIX/icon.png"
for dir in "$HERE"/hicolor/*/; do
    size="$(basename "$dir")"
    $SUDO install -D -m 644 "$dir/apps/predatorcore.png" "/usr/share/icons/hicolor/$size/apps/predatorcore.png"
done
$SUDO ln -sf "$PREFIX/PredatorCore" /usr/local/bin/predatorcore

$SUDO tee /usr/share/applications/predatorcore.desktop >/dev/null <<EOF
[Desktop Entry]
Name=PredatorCore
Comment=Predator control center: performance, fans, lighting, monitoring
Exec=$PREFIX/PredatorCore
Icon=predatorcore
Terminal=false
Type=Application
StartupWMClass=PredatorCore
Categories=Utility;System;Settings;
Keywords=acer;predator;fan;rgb;keyboard;monitor;
EOF

# If the DAMX PredatorSense-key service is installed, point the key at PredatorCore instead.
if [ -f /usr/local/bin/nitro-key-detection.sh ]; then
    echo "==> Pointing the PredatorSense key at PredatorCore"
    printf '#!/bin/bash\nexec %s "$@"\n' "$PREFIX/PredatorCore" | $SUDO tee /usr/local/bin/DAMX >/dev/null
    $SUDO chmod +x /usr/local/bin/DAMX
    $SUDO sed -i "s#pgrep -f \"/opt/damx/gui/DivAcerManagerMax\"#pgrep -f \"$PREFIX/PredatorCore\"#" \
        /usr/local/bin/nitro-key-detection.sh
    $SUDO systemctl restart nitro-key-detection.service 2>/dev/null || true
fi

$SUDO gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || true
$SUDO update-desktop-database -q /usr/share/applications 2>/dev/null || true

echo "==> Done. Launch PredatorCore from your app menu or run: predatorcore"
echo "    For power limits, fan curves and GPU power control also run: sudo ./install-daemon.sh"
