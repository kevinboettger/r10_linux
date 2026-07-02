#!/bin/bash
# One-shot launcher for the R10 Bridge on a Raspberry Pi / Linux.
#
#   ./run.sh
#
# It will:
#   1. Make sure the BlueZ bluetooth service is running.
#   2. Pair + trust the R10 automatically if it isn't already paired
#      (using the device name from settings.json). Turn the R10 on first.
#   3. Launch the bridge (HTTP API + TCP shot stream).
#
# Re-run it any time. If the R10 is already paired it skips straight to launch.

set -uo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-$0}")" && pwd)"
cd "$SCRIPT_DIR"

# --- pretty logging -----------------------------------------------------------
c() { printf '\033[%sm%s\033[0m\n' "$1" "$2"; }
info()  { c '1;36' ">> $1"; }
ok()    { c '1;32' "OK $1"; }
warn()  { c '1;33' "!! $1"; }
err()   { c '1;31' "XX $1"; }

# --- locate settings + device name -------------------------------------------
SETTINGS="$SCRIPT_DIR/settings.json"
if [[ ! -f "$SETTINGS" ]]; then
  err "settings.json not found next to this script ($SETTINGS)."
  exit 1
fi
DEVICE_NAME="$(grep -oP '"bluetoothDeviceName"\s*:\s*"\K[^"]+' "$SETTINGS" 2>/dev/null || true)"
DEVICE_NAME="${DEVICE_NAME:-Approach R10}"
info "Target device name: '$DEVICE_NAME'"

# --- 1. bluetooth service -----------------------------------------------------
if ! command -v bluetoothctl >/dev/null 2>&1; then
  err "bluetoothctl not found. Install BlueZ:  sudo apt install -y bluez"
  exit 1
fi
if command -v systemctl >/dev/null 2>&1 && ! systemctl is-active --quiet bluetooth; then
  info "Starting the bluetooth service..."
  sudo systemctl start bluetooth || warn "Could not start bluetooth service; continuing anyway."
fi
bluetoothctl power on >/dev/null 2>&1 || true

# --- 2. pair the R10 if needed ------------------------------------------------
paired_mac() {
  # Prints the MAC of a paired device matching DEVICE_NAME, or nothing.
  bluetoothctl devices Paired 2>/dev/null | grep -F " $DEVICE_NAME" | head -n1 | awk '{print $2}'
}

MAC="$(paired_mac)"
if [[ -n "$MAC" ]]; then
  ok "'$DEVICE_NAME' already paired ($MAC)."
else
  warn "'$DEVICE_NAME' is not paired yet."
  info "Make sure the R10 is powered on and awake, then press Enter to scan..."
  read -r _

  info "Scanning for ~20s..."
  bluetoothctl --timeout 20 scan on >/dev/null 2>&1 || true

  MAC="$(bluetoothctl devices 2>/dev/null | grep -F " $DEVICE_NAME" | head -n1 | awk '{print $2}')"
  if [[ -z "$MAC" ]]; then
    err "Did not find '$DEVICE_NAME' nearby."
    err "Check the R10 is on/awake, and that its name matches bluetoothDeviceName in settings.json."
    exit 1
  fi

  info "Found $MAC. Pairing + trusting..."
  bluetoothctl pair "$MAC"  >/dev/null 2>&1 || true
  bluetoothctl trust "$MAC" >/dev/null 2>&1 || true

  if [[ -n "$(paired_mac)" ]]; then
    ok "Paired and trusted '$DEVICE_NAME' ($MAC)."
  else
    err "Pairing did not complete. Try manually:  bluetoothctl -> pair $MAC / trust $MAC"
    exit 1
  fi
fi

# --- 3. launch ----------------------------------------------------------------
info "Starting R10 Bridge (Ctrl-C or Enter to stop)..."
echo
if [[ -x "$SCRIPT_DIR/r10-bridge" ]]; then
  exec "$SCRIPT_DIR/r10-bridge"
elif command -v dotnet >/dev/null 2>&1 && ls "$SCRIPT_DIR"/*.csproj >/dev/null 2>&1; then
  # dev fallback: no published binary here, run from source
  exec dotnet run -c Release
else
  err "No 'r10-bridge' binary next to this script, and no .csproj to run from source."
  err "Build first:  ./build/publish-linux-arm64.sh   (then run this from the publish folder)"
  exit 1
fi
