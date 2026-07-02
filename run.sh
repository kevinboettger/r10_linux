#!/bin/bash
# One-shot launcher for the R10 Bridge on a Raspberry Pi / Linux.
#
#   ./run.sh                    Pair the R10 if needed, then run in the foreground.
#   ./run.sh --install-service  Build a standalone binary and install a systemd
#                               service so the bridge auto-starts on boot.
#
# Plain run will:
#   1. Make sure the BlueZ bluetooth service is running.
#   2. Pair + trust the R10 automatically if it isn't already paired
#      (using the device name from settings.json). Turn the R10 on first.
#   3. Launch the bridge (HTTP API + TCP shot stream).
#
# Re-run it any time. If the R10 is already paired it skips straight to launch.

set -uo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-$0}")" && pwd)"
cd "$SCRIPT_DIR"

MODE="run"
case "${1:-}" in
  "")                 MODE="run" ;;
  --install-service)  MODE="install" ;;
  --install-icon)     MODE="icon" ;;
  *)
    echo "Unknown option: $1"
    echo "Usage: ./run.sh [--install-service] [--install-icon]"
    exit 2
    ;;
esac

# --- pretty logging -----------------------------------------------------------
c() { printf '\033[%sm%s\033[0m\n' "$1" "$2"; }
info()  { c '1;36' ">> $1"; }
ok()    { c '1;32' "OK $1"; }
warn()  { c '1;33' "!! $1"; }
err()   { c '1;31' "XX $1"; }

# --- systemd installer --------------------------------------------------------
install_service() {
  local INSTALL_DIR="/opt/r10-bridge"
  local UNIT="/etc/systemd/system/r10-bridge.service"
  local RUN_USER="${SUDO_USER:-$USER}"

  if ! ls "$SCRIPT_DIR"/*.csproj >/dev/null 2>&1; then
    err "--install-service must be run from the source checkout (no .csproj found here)."
    exit 1
  fi
  if ! command -v dotnet >/dev/null 2>&1; then
    err "dotnet SDK not found; needed to build the standalone binary."
    exit 1
  fi

  info "Building self-contained linux-arm64 binary..."
  dotnet publish "$SCRIPT_DIR"/*.csproj -c Release -r linux-arm64 --self-contained true \
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
    -o "$SCRIPT_DIR/.publish" || { err "Build failed."; exit 1; }

  info "Installing to $INSTALL_DIR ..."
  sudo mkdir -p "$INSTALL_DIR"
  sudo cp "$SCRIPT_DIR/.publish/r10-bridge" "$INSTALL_DIR/"
  sudo chmod +x "$INSTALL_DIR/r10-bridge"
  # Don't overwrite an existing settings.json — preserve the user's edits on reinstall.
  if [[ -f "$INSTALL_DIR/settings.json" ]]; then
    warn "Keeping existing $INSTALL_DIR/settings.json (not overwritten)."
  else
    sudo cp "$SCRIPT_DIR/.publish/settings.json" "$INSTALL_DIR/"
  fi
  # Let the service user own the dir so it can write logs to $INSTALL_DIR/logs.
  sudo chown -R "$RUN_USER" "$INSTALL_DIR"

  info "Writing systemd unit $UNIT ..."
  sudo tee "$UNIT" >/dev/null <<UNITEOF
[Unit]
Description=R10 Bridge
After=bluetooth.target network.target
Wants=bluetooth.target

[Service]
Type=simple
User=$RUN_USER
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/r10-bridge
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
UNITEOF

  info "Enabling and starting the service..."
  sudo systemctl daemon-reload
  sudo systemctl enable --now r10-bridge

  ok "Installed. The bridge now runs on boot as user '$RUN_USER'."
  echo
  info "Handy commands:"
  echo "   sudo systemctl status r10-bridge      # is it running?"
  echo "   journalctl -u r10-bridge -f           # follow live logs"
  echo "   sudo systemctl restart r10-bridge     # restart"
  echo "   sudo systemctl disable --now r10-bridge  # stop + don't start on boot"
  echo
  warn "The R10 must already be paired (run ./run.sh once first to pair it)."
  warn "Edit settings at $INSTALL_DIR/settings.json, then: sudo systemctl restart r10-bridge"
}

if [[ "$MODE" == "install" ]]; then
  install_service
  exit 0
fi

# --- desktop icon installer ---------------------------------------------------
install_icon() {
  local desktop_dir icon_file wrapper icon_img
  desktop_dir="$(xdg-user-dir DESKTOP 2>/dev/null || echo "$HOME/Desktop")"
  mkdir -p "$desktop_dir"
  icon_file="$desktop_dir/R10-Bridge.desktop"
  icon_img="$SCRIPT_DIR/assets/r10-bridge.svg"
  # Remove the older-named launcher if it exists.
  rm -f "$desktop_dir/Connect-R10.desktop"

  # A wrapper keeps the terminal window OPEN after the run ends, so output and
  # any errors stay visible instead of flashing closed.
  wrapper="$HOME/.local/bin/connect-r10.sh"
  mkdir -p "$(dirname "$wrapper")"
  cat > "$wrapper" <<WRAPEOF
#!/bin/bash
cd "$SCRIPT_DIR" || exit 1
./run.sh
echo
read -n1 -s -r -p "Press any key to close this window..."
WRAPEOF
  chmod +x "$wrapper"

  cat > "$icon_file" <<ICONEOF
[Desktop Entry]
Type=Application
Version=1.0
Name=R10 Bridge
Comment=Reconnect the Garmin Approach R10 and start the bridge
Icon=$icon_img
Exec=$wrapper
Path=$SCRIPT_DIR
Terminal=true
Categories=Utility;
ICONEOF

  chmod +x "$icon_file"
  # Mark it trusted so double-clicking runs it without the "untrusted launcher" prompt.
  gio set "$icon_file" metadata::trusted true 2>/dev/null || true

  ok "Desktop icon created: $icon_file"
  info "Double-click 'R10 Bridge' on your desktop any time to reconnect + start the bridge."
  info "If the desktop asks the first time, choose 'Execute' / 'Trust'."
}

if [[ "$MODE" == "icon" ]]; then
  install_icon
  exit 0
fi

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

# --- 2. make sure the R10 is set up -------------------------------------------
find_mac() {
  # MAC of a known device matching DEVICE_NAME (whether or not it's bonded).
  bluetoothctl devices 2>/dev/null | grep -F " $DEVICE_NAME" | head -n1 | awk '{print $2}'
}
is_ready() {
  # The R10 works once it's paired OR trusted; it often connects without bonding.
  [[ -n "$1" ]] && bluetoothctl info "$1" 2>/dev/null | grep -qE "Paired: yes|Trusted: yes"
}

MAC="$(find_mac)"
if is_ready "$MAC"; then
  ok "'$DEVICE_NAME' already set up ($MAC)."
else
  warn "'$DEVICE_NAME' is not set up yet."
  info "Make sure the R10 is powered on and awake (and NOT connected to the Garmin"
  info "phone app), then press Enter to scan..."
  read -r _

  info "Scanning for ~20s..."
  bluetoothctl --timeout 20 scan on >/dev/null 2>&1 || true

  MAC="$(find_mac)"
  if [[ -z "$MAC" ]]; then
    err "Did not find '$DEVICE_NAME' nearby."
    err "Check the R10 is on/awake, not held by the Garmin phone app, and that its"
    err "name matches bluetoothDeviceName in settings.json."
    exit 1
  fi

  info "Found $MAC. Pairing, trusting, connecting..."
  # Do it all inside ONE bluetoothctl session with an agent registered. The R10
  # uses "Just Works" pairing (no PIN), which needs an agent; separate one-shot
  # `bluetoothctl pair` calls have none and fail silently.
  {
    echo "power on";      sleep 1
    echo "agent on";      sleep 1
    echo "default-agent"; sleep 1
    echo "pair $MAC";     sleep 8
    echo "trust $MAC";    sleep 2
    echo "connect $MAC";  sleep 3
    echo "quit"
  } | bluetoothctl >/dev/null 2>&1 || true

  if is_ready "$MAC"; then
    ok "'$DEVICE_NAME' set up ($MAC)."
  else
    # Not fatal: the R10 frequently connects without a full bond. Trust it and
    # let the bridge connect anyway.
    bluetoothctl trust "$MAC" >/dev/null 2>&1 || true
    warn "Could not confirm bonding, but the R10 usually connects without it."
    warn "Trusted $MAC and continuing; the bridge will try to connect."
  fi
fi

# --- 3. launch ----------------------------------------------------------------
# If the background service is running it already holds the R10 + the ports, so a
# foreground run would clash. Stop it so this terminal can take over cleanly.
if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet r10-bridge 2>/dev/null; then
  warn "Background service 'r10-bridge' is running; stopping it so this run can use the R10."
  warn "(It will start again on next boot. To keep it off:  sudo systemctl disable r10-bridge)"
  sudo systemctl stop r10-bridge
fi

info "Starting R10 Bridge (Ctrl-C or Enter to stop). Logs also saved under ./logs/"
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
