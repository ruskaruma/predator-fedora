#!/usr/bin/env bash
# Build a release tarball: dist/predatorcore-<version>-linux-x64.tar.gz (+ .sha256)
# Contains a self-contained binary (no .NET needed), the daemon, icons and an installer.
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(grep -oP '<Version>\K[^<]+' "$REPO_DIR/src/PredatorCore/PredatorCore.csproj")"
NAME="predatorcore-${VERSION}-linux-x64"
STAGE="$REPO_DIR/dist/$NAME"

rm -rf "$STAGE" "$REPO_DIR/dist/$NAME.tar.gz" "$REPO_DIR/dist/$NAME.tar.gz.sha256"
mkdir -p "$STAGE"

echo "==> Building PredatorCore $VERSION"
dotnet publish "$REPO_DIR/src/PredatorCore/PredatorCore.csproj" -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -o "$STAGE/bin"

echo "==> Staging"
cp -r "$REPO_DIR/daemon" "$STAGE/daemon"
rm -rf "$STAGE/daemon/__pycache__"
cp -r "$REPO_DIR/assets/hicolor" "$STAGE/hicolor"
cp "$REPO_DIR/src/PredatorCore/icon.png" "$STAGE/icon.png"
cp "$REPO_DIR/LICENSE" "$REPO_DIR/NOTICE" "$REPO_DIR/README.md" "$STAGE/"
cp "$REPO_DIR/scripts/install-daemon.sh" "$STAGE/install-daemon.sh"
cp "$REPO_DIR/scripts/package-install.sh" "$STAGE/install.sh"
chmod +x "$STAGE/install.sh" "$STAGE/install-daemon.sh" "$STAGE/bin/PredatorCore"

# install-daemon.sh expects the repo layout (../daemon); inside the package the daemon sits next to it
sed -i 's|REPO_DIR="$(cd "$(dirname "${BASH_SOURCE\[0\]}")/.." \&\& pwd)"|REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" \&\& pwd)"|' \
    "$STAGE/install-daemon.sh"

echo "==> Archiving"
tar -C "$REPO_DIR/dist" -czf "$REPO_DIR/dist/$NAME.tar.gz" "$NAME"
(cd "$REPO_DIR/dist" && sha256sum "$NAME.tar.gz" > "$NAME.tar.gz.sha256")
echo "==> dist/$NAME.tar.gz ($(du -h "$REPO_DIR/dist/$NAME.tar.gz" | cut -f1))"
