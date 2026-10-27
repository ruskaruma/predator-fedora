#!/usr/bin/env bash
# Re-draw PredatorCore's artwork: the mark, the three paper-cut illustrations, the app icon and its sizes.
# Drawn in code with the New Horizon print kit (~/.claude/skills/new-horizon-style): flat inks, hatching,
# stipple, paper grain and an off-register vermilion plate. Needs node with playwright-core and Chromium,
# plus ImageMagick. Every ink, edge and grain is seeded, so the output is the same each time.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP="$HERE/../../src/PredatorCore"
KIT="${KIT:-$HOME/.claude/skills/new-horizon-style/scripts}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
cd "$HERE"

node mark.mjs mark.svg '#4fc3dc'
for spot in fan chip dial; do
    node "$KIT/render.mjs" "spot-$spot.js,plants.js" "$TMP/spot-$spot.png" 1600 1600
    magick "$TMP/spot-$spot.png" -resize 480x480 -strip "$APP/Assets/Art/spot-$spot.png"
done

# App icon: the fan and its disc only (icon-flag.js drops the leaves and sparks)
node "$KIT/render.mjs" spot-fan.js,plants.js,icon-flag.js "$TMP/icon.png" 1024 1024
magick "$TMP/icon.png" -resize 256x256 "$APP/icon.png"
for s in 16 24 32 48 64 128 256 512; do
    magick "$TMP/icon.png" -resize "${s}x${s}" "$HERE/../hicolor/${s}x${s}/apps/predatorcore.png"
done
# Header mark: white blades and a coolant core on a transparent ground
magick -background none -density 600 mark.svg -resize 256x256 "$APP/iconTransparent.png"
echo "==> Artwork redrawn"
