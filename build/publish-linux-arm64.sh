#!/bin/bash
# Publish a self-contained, single-file linux-arm64 build for the Raspberry Pi.
# Produces a zip that bundles the .NET runtime, so the Pi needs no dotnet install.
set -euo pipefail

SCRIPT_PATH="${BASH_SOURCE[0]:-$0}";
SCRIPT_DIR=`dirname -- "$SCRIPT_PATH"`
BASE_DIR=`realpath "${SCRIPT_DIR}/.."`

VERSION=`grep -oP '(?<=\<VersionPrefix\>).*(?=\<\/VersionPrefix\>)' "${BASE_DIR}"/*.csproj`
PLATFORM=linux-arm64

PUBLISH_DIR="${BASE_DIR}/bin/Release/net8.0/${PLATFORM}/publish"
OUTDIR="${BASE_DIR}/publish"

rm -rf "${BASE_DIR}/bin/Release"
rm -rf "${OUTDIR}"
mkdir -p "${OUTDIR}"

dotnet publish "${BASE_DIR}/r10-bridge.csproj" \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -r ${PLATFORM} \
  -c Release \
  --self-contained true

zip -j "${OUTDIR}/r10-bridge-v${VERSION}-linux-arm64-bluetooth-enabled.zip" "${PUBLISH_DIR}"/*

# Also emit a variant with Bluetooth disabled (HTTP/TCP only) for testing without a device.
sed -i 's|true, //bluetooth enabled|false, //bluetooth enabled|' "${PUBLISH_DIR}/settings.json"

zip -j "${OUTDIR}/r10-bridge-v${VERSION}-linux-arm64.zip" "${PUBLISH_DIR}"/*

echo "Artifacts written to ${OUTDIR}:"
ls -1 "${OUTDIR}"
