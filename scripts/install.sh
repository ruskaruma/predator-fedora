#!/usr/bin/env bash
# Build PredatorCore and install it system-wide.
# Installs only the GUI. The Linuwu-Sense driver and the control daemon must already be present.
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFIX="/opt/predatorcore"
PUBLISH_DIR="$REPO_DIR/build/publish"

command -v dotnet >/dev/null || { echo "dotnet SDK 9 is required (Fedora: sudo dnf install dotnet-sdk-9.0)"; exit 1; }

echo "==> Building"
dotnet publish "$REPO_DIR/src/PredatorCore/PredatorCore.csproj" -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$PUBLISH_DIR"

echo "==> Installing to $PREFIX (needs sudo)"
sudo install -d "$PREFIX"
sudo install -m 755 "$PUBLISH_DIR/PredatorCore" "$PREFIX/PredatorCore"
sudo install -m 644 "$REPO_DIR/src/PredatorCore/icon.png" "$PREFIX/icon.png"
for dir in "$REPO_DIR"/assets/hicolor/*/; do
    size="$(basename "$dir")"
    sudo install -D -m 644 "$dir/apps/predatorcore.png" "/usr/share/icons/hicolor/$size/apps/predatorcore.png"
done
sudo ln -sf "$PREFIX/PredatorCore" /usr/local/bin/predatorcore

sudo tee /usr/share/applications/predatorcore.desktop >/dev/null <<EOF
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
    sudo tee /usr/local/bin/DAMX >/dev/null <<EOF
#!/bin/bash
exec $PREFIX/PredatorCore "\$@"
EOF
    sudo chmod +x /usr/local/bin/DAMX
    sudo sed -i "s#pgrep -f \"/opt/damx/gui/DivAcerManagerMax\"#pgrep -f \"$PREFIX/PredatorCore\"#" \
        /usr/local/bin/nitro-key-detection.sh
    sudo systemctl restart nitro-key-detection.service 2>/dev/null || true
fi

# Optional local artwork: icons in ~/.local/share/predatorcore/icons/hicolor override the defaults for
# this user only (the app also picks up icon.png / iconTransparent.png from that folder at runtime).
LOCAL_ICONS="${XDG_DATA_HOME:-$HOME/.local/share}/predatorcore/icons/hicolor"
if [ -d "$LOCAL_ICONS" ]; then
    echo "==> Using local icon override from $LOCAL_ICONS"
    for dir in "$LOCAL_ICONS"/*/; do
        size="$(basename "$dir")"
        install -D -m 644 "$dir/apps/predatorcore.png" \
            "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/$size/apps/predatorcore.png"
    done
fi

sudo gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || true
gtk-update-icon-cache -q "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" 2>/dev/null || true
sudo update-desktop-database -q /usr/share/applications 2>/dev/null || true
echo "==> Done. Launch 'PredatorCore' from your app menu or run: predatorcore"
