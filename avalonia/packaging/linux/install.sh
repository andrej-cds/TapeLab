#!/usr/bin/env bash
# Namesti CDS TapeLab za trenutnega uporabnika (brez sudo): ~/.local/share/cds-tapelab
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/.local/share/cds-tapelab"
APPS="$HOME/.local/share/applications"

rm -rf "$DEST"; mkdir -p "$DEST" "$APPS"
cp -R "$HERE/app" "$HERE/cds-tapelab.png" "$DEST/"
sed "s|@APPDIR@|$DEST|g" "$HERE/cds-tapelab.desktop" > "$APPS/cds-tapelab.desktop"
chmod +x "$DEST/app/CDSTapeLab" "$APPS/cds-tapelab.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$APPS" || true
echo "CDS TapeLab je nameščen. Najdeš ga v meniju aplikacij (Zvok / Audio) ali: $DEST/app/CDSTapeLab"
