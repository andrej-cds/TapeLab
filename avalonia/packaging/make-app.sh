#!/usr/bin/env bash
# Izdela "CDS TapeLab.app" in .dmg za macOS. Zaženi na Macu (ali v GitHub Actions na macos-latest).
#   uporaba: packaging/make-app.sh <rid> <verzija> <oznaka>
#   primer:  packaging/make-app.sh osx-arm64 0.7.0 apple-silicon     (ali osx-x64 0.7.0 intel)
# Potrebuje: .NET 8 SDK. Podpis je "ad-hoc" (brez Apple Developer računa).
set -euo pipefail

RID="${1:?rid (osx-arm64 | osx-x64)}"
VER="${2:?verzija}"
TAG="${3:-$RID}"

cd "$(dirname "$0")/.."
PUB="publish/$RID"
DIST="dist/$RID"
APP="$DIST/CDS TapeLab.app"
DMG="dist/CDSTapeLab-$VER-macOS-$TAG.dmg"

rm -rf "$PUB" "$DIST" "$DMG"
mkdir -p dist

echo "== dotnet publish ($RID) =="
dotnet publish CDSTapeLab.csproj -c Release -r "$RID" --self-contained true \
  -p:Version="$VER" -p:DebugType=none -p:DebugSymbols=false -o "$PUB"

echo "== .app =="
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/CDSTapeLab"
sed "s/__VERSION__/$VER/g" packaging/Info.plist > "$APP/Contents/Info.plist"

echo "== ikona =="
ISET="$(mktemp -d)/CDSTapeLab.iconset"
mkdir -p "$ISET"
for s in 16 32 128 256 512; do
  sips -z "$s" "$s" Assets/icon.png --out "$ISET/icon_${s}x${s}.png" >/dev/null
  sips -z "$((s * 2))" "$((s * 2))" Assets/icon.png --out "$ISET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ISET" -o "$APP/Contents/Resources/CDSTapeLab.icns"

echo "== podpis (ad-hoc) =="
codesign --force --deep --sign - "$APP"
codesign --verify --deep --verbose=2 "$APP" || true

echo "== dmg =="
cp packaging/PREBERI-ME.txt "$DIST/READ ME FIRST - PREBERI ME.txt"
ln -s /Applications "$DIST/Applications"
hdiutil create -volname "CDS TapeLab $VER" -srcfolder "$DIST" -ov -format UDZO "$DMG"
echo "Narejeno: $DMG"
