#!/bin/bash
# ============================================================================
# R10 Bridge — one-time setup for a fresh 64-bit Raspberry Pi OS.
#
#   ./setup.sh
#
# Installs the .NET 8 SDK, makes sure BlueZ is running and configured, builds
# the bridge, and puts the "R10 Bridge" icon on the desktop.
#
# When it finishes:
#   1. Turn the R10 on and put it in PAIRING MODE (solid blue), sitting flat.
#   2. Double-click the "R10 Bridge" desktop icon (or run: ./run.sh --reset).
# ============================================================================
set -uo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-$0}")" && pwd)"
cd "$SCRIPT_DIR"

c() { printf '\033[%sm%s\033[0m\n' "$1" "$2"; }
info() { c '1;36' ">> $1"; }
ok()   { c '1;32' "OK $1"; }
warn() { c '1;33' "!! $1"; }
err()  { c '1;31' "XX $1"; }

# --- 0. architecture sanity ---------------------------------------------------
if [[ "$(uname -m)" != "aarch64" ]]; then
  warn "Expected 64-bit (aarch64) but 'uname -m' says '$(uname -m)'."
  warn "If this is a 32-bit OS, reflash 64-bit Raspberry Pi OS for best results."
fi

# --- 1. BlueZ -----------------------------------------------------------------
info "Ensuring BlueZ is installed and running..."
if ! command -v bluetoothctl >/dev/null 2>&1; then
  sudo apt-get update && sudo apt-get install -y bluez
fi
sudo systemctl enable --now bluetooth || warn "Could not enable the bluetooth service."

# --- 2. .NET 8 SDK ------------------------------------------------------------
if command -v dotnet >/dev/null 2>&1; then
  ok ".NET already on PATH ($(dotnet --version 2>/dev/null))."
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export PATH="$HOME/.dotnet:$PATH"
  ok ".NET found in ~/.dotnet ($(dotnet --version 2>/dev/null))."
else
  info "Installing the .NET 8 SDK to ~/.dotnet (a couple of minutes)..."
  if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; then
    err "Could not download the .NET install script. Check the Pi's internet connection."
    exit 1
  fi
  bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet" || { err ".NET install failed."; exit 1; }
  export PATH="$HOME/.dotnet:$PATH"
  grep -q 'HOME/.dotnet' "$HOME/.bashrc" 2>/dev/null || echo 'export PATH="$HOME/.dotnet:$PATH"' >> "$HOME/.bashrc"
  ok ".NET 8 installed ($(dotnet --version 2>/dev/null))."
fi

# --- 3. build the bridge (so the first run is fast) ---------------------------
info "Building the bridge..."
if ! dotnet build -c Release; then
  err "Build failed."
  exit 1
fi
ok "Build succeeded."

# --- 4. BlueZ GATT cache config + desktop icon (reuse run.sh) -----------------
info "Configuring BlueZ (disable GATT caching so the R10 handshake is reliable)..."
./run.sh --fix-bluez || warn "fix-bluez step had a problem; continuing."

info "Installing the desktop icon..."
./run.sh --install-icon || warn "icon install had a problem; continuing."

echo
ok "Setup complete."
echo
info "To run the bridge:"
echo "   1. Turn the R10 on and put it in PAIRING MODE (solid blue), sitting flat."
echo "   2. Double-click the 'R10 Bridge' icon on the desktop"
echo "      (or from a terminal:  ./run.sh --reset )"
