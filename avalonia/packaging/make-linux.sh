#!/usr/bin/env bash
# Izdela CDSTapeLab-<verzija>-linux-<arhitektura>.tar.gz (samostojen paket, .NET ni potreben).
#   uporaba: packaging/make-linux.sh <rid> <verzija> <oznaka>
#   primer:  packaging/make-linux.sh linux-x64 0.6.0 x64      (ali linux-arm64 0.6.0 arm64)
set -euo pipefail

RID="${1:?rid (linux-x64 | linux-arm64)}"
VER="${2:?verzija}"
TAG="${3:-$RID}"

cd "$(dirname "$0")/.."
PUB="publish/$RID"
NAME="CDSTapeLab-$VER-linux-$TAG"
STAGE="dist/$NAME"
OUT="dist/$NAME.tar.gz"

rm -rf "$PUB" "$STAGE" "$OUT"
mkdir -p dist

echo "== dotnet publish ($RID) =="
dotnet publish CDSTapeLab.csproj -c Release -r "$RID" --self-contained true \
  -p:Version="$VER" -p:DebugType=none -p:DebugSymbols=false -o "$PUB"

echo "== PortAudio knjiznica =="
find "$PUB" -iname 'libportaudio*' -print || true
if ! find "$PUB" -iname 'libportaudio*' | grep -q .; then
  echo "OPOZORILO: libportaudio ni v paketu; uporabnik bo moral namestiti paket libportaudio2."
fi

echo "== paket =="
mkdir -p "$STAGE/app"
cp -R "$PUB/." "$STAGE/app/"
chmod +x "$STAGE/app/CDSTapeLab"
cp Assets/icon.png "$STAGE/cds-tapelab.png"
cp packaging/linux/install.sh packaging/linux/uninstall.sh "$STAGE/"
cp packaging/linux/cds-tapelab.desktop "$STAGE/"
cp packaging/linux/PREBERI-ME.txt "$STAGE/READ ME FIRST - PREBERI ME.txt"
chmod +x "$STAGE/install.sh" "$STAGE/uninstall.sh"

tar -C dist -czf "$OUT" "$NAME"
echo "Narejeno: $OUT"
