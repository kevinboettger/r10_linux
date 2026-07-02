## R10 Bridge — README & Developer Guide

R10 Bridge is a local service that connects to a Garmin Approach R10 over Bluetooth LE and exposes the device’s shot data through two simple local interfaces: a JSON HTTP API and a newline-delimited JSON TCP stream. Use it to integrate the R10 with simulators, analytics tools, or custom apps on the same machine.

It runs on **Linux (including the Raspberry Pi, ARM64)** using BlueZ over D-Bus for Bluetooth. It is built as a cross-platform .NET 8 application; the primary/target build is `linux-arm64`.

### What this service does

- Connects to a paired Garmin Approach R10 over Bluetooth LE.
- Subscribes to shot events and metrics via a proprietary BLE protocol and a protobuf-based service exposed by the device.
- Normalizes shot metrics into simple models and:
  - Serves status and last-shot over a local HTTP API.
  - Broadcasts each shot as newline-delimited JSON over a local TCP socket.

All network services bind to localhost (127.0.0.1) by default for safety.

---

## Architecture Overview

High-level components and their roles:

- ConnectionManager
  - Orchestrates subsystem startup/shutdown based on `settings.json`.
  - Holds the latest shot (ball and club) and timestamp.
  - Pushes shots to the TCP broadcast server; exposes device operations to the HTTP API.

- BluetoothConnection
  - Finds the paired device and manages automatic reconnect.
  - Creates and configures `LaunchMonitorDevice`.
  - Translates raw `Metrics` from the device into API models and forwards them to `ConnectionManager`.

- LaunchMonitorDevice (BLE device wrapper)
  - Implements the GATT handshake, notification subscriptions, and protobuf request/response flow.
  - Provides device actions: wake, tilt calibration, status, and environment/shot configuration.

- HTTP API Server
  - Local-only HTTP server exposing status and control endpoints.
  - Optional built-in dashboard for quick inspection/testing.

- TCP Shot Broadcast Server
  - Local-only TCP server that accepts clients and broadcasts one JSON line per shot to all connected clients.

Data flow:
1) R10 emits shot -> BLE notifications -> `LaunchMonitorDevice` produces `Metrics`.
2) `BluetoothConnection` converts device metrics to API `BallData`/`ClubData` (mph, degrees, derived spins).
3) `ConnectionManager.SendShot` stores as last-shot and broadcasts JSON on TCP; HTTP API exposes the latest snapshot.

---

## Settings and Runtime Behavior

Configuration lives in `settings.json` and is loaded at startup. Defaults are safe and local-only.

```json
{
  "bluetooth": {
    "enabled": true,
    "bluetoothDeviceName": "Approach R10",
    "reconnectInterval": 10,
    "autoWake": true,
    "calibrateTiltOnConnect": true,
    "debugLogging": false,
    "altitude": 0,
    "humidity": 0.5,
    "temperature": 60,
    "airDensity": 1.225,
    "teeDistanceInFeet": 7
  },
  "httpApi": {
    "enabled": true,
    "port": 5001,
    "enableDashboard": true
  },
  "tcpBroadcast": {
    "enabled": true,
    "port": 5510
  }
}
```

Notes:
- Bluetooth
  - The device must be paired with the OS first (via BlueZ / `bluetoothctl` — see "Pairing the R10" below). `bluetoothDeviceName` must match the paired device name reported by BlueZ.
  - `autoWake`: if the device is in standby, the service attempts to wake it automatically.
  - `calibrateTiltOnConnect`: optional recalibration at session start.
  - Environment parameters (`temperature`, `humidity`, `altitude`, `airDensity`, `teeDistanceInFeet`) are sent to the device to improve measurement accuracy.
- HTTP API
  - Binds to `127.0.0.1:<port>` only.
  - Simple HTML dashboard can be enabled for local inspection.
- TCP Broadcast
  - Binds to `127.0.0.1:<port>` only. Broadcasts a single JSON line per shot to all connected clients.

---

## HTTP API

Base URL: `http://127.0.0.1:<port>` (default 5001)

- GET `/`
  - If the dashboard is enabled, serves a small HTML page showing status and last shot, with action buttons for wake and tilt calibration.
  - Otherwise returns 200 OK.

- GET `/api/status`
  - Returns current device status snapshot. Example response:
  ```json
  {
    "connected": true,
    "ready": true,
    "battery": 87,
    "firmware": "x.y.z",
    "model": "Approach R10",
    "serial": "XXXXXXXXXX",
    "tilt": { "roll": 0.7, "pitch": -1.2 }
  }
  ```

- GET `/api/last-shot`
  - Returns the latest shot captured by the service, if any.
  - Example response:
  ```json
  {
    "ballData": {
      "Speed": 143.2,
      "SpinAxis": -2.8,
      "TotalSpin": 2875,
      "BackSpin": 2874.6,
      "SideSpin": 140.5,
      "HLA": 1.2,
      "VLA": 14.8,
      "CarryDistance": 0
    },
    "clubData": {
      "Speed": 99.3,
      "AngleOfAttack": -2.3,
      "FaceToTarget": 0.8,
      "Path": -1.5,
      "SpeedAtImpact": 99.3,
      "VerticalFaceImpact": 0,
      "HorizontalFaceImpact": 0,
      "ClosureRate": 0
    },
    "atUtc": "2025-01-01T12:34:56.789Z"
  }
  ```
  - Field notes:
    - Units are mph for speeds; angles are degrees; spins are rpm.
    - `BackSpin` and `SideSpin` are derived from `TotalSpin` and `SpinAxis` using trigonometry.
    - Some fields may be `0` when not provided by the device.

- POST `/api/wake`
  - Sends a wake command to the device.
  - Returns `{ "ok": true|false }` based on whether a status response was received.

- POST `/api/tilt/calibrate/start`
  - Starts a tilt calibration routine on the device.
  - Returns `{ "ok": true|false }`.

- POST `/api/tilt/calibrate/reset`
  - Resets tilt calibration on the device.
  - Returns `{ "ok": true|false }`.

Security & scope:
- No authentication; service is intended for local use only (loopback binding).
- If you need remote access, front this with your own gateway that enforces auth and TLS.

---

## TCP Shot Broadcast

Endpoint: `tcp://127.0.0.1:<port>` (default 5510)

Behavior:
- When a shot is captured, the service broadcasts a single UTF-8 JSON line to all connected clients, then appends a newline (`\n`).
- Incoming data from clients is ignored; this is a broadcast-only server.

Message format (identical to `/api/last-shot` payload, per-shot):
```json
{ "ballData": { ... }, "clubData": { ... }, "atUtc": "ISO-8601 UTC" }
```

Example payload:
```json
{
  "ballData": {
    "Speed": 144.9,
    "SpinAxis": -3.1,
    "TotalSpin": 2950,
    "BackSpin": 2948.6,
    "SideSpin": 159.4,
    "HLA": 1.0,
    "VLA": 15.2,
    "CarryDistance": 0
  },
  "clubData": {
    "Speed": 101.2,
    "AngleOfAttack": -2.1,
    "FaceToTarget": 0.6,
    "Path": -1.3,
    "SpeedAtImpact": 101.2,
    "VerticalFaceImpact": 0,
    "HorizontalFaceImpact": 0,
    "ClosureRate": 0
  },
  "atUtc": "2025-01-01T12:34:56.789Z"
}
```

Example client (PowerShell):
```powershell
$client = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 5510)
$stream = $client.GetStream()
$reader = New-Object System.IO.StreamReader($stream)
while ($true) { $line = $reader.ReadLine(); if ($line) { $line } }
```

Example client (Python):
```python
import socket

s = socket.create_connection(("127.0.0.1", 5510))
f = s.makefile('r')
for line in f:
    print(line.strip())
```

Notes:
- Messages are newline-delimited to simplify streaming parsers.
- The server logs client connects/disconnects; failure to consume data fast enough on client side will not block other clients.
- Multiple clients can connect concurrently; each receives the same stream.
- There are no heartbeats/keepalives; reconnect on EOF if needed.

### Consuming the TCP stream (parsing by newline)

The stream is newline-delimited JSON (NDJSON). 
To parse in any language:
1) Open a TCP connection to `127.0.0.1:<port>`.
2) Read bytes until `\n`.
3) Parse the accumulated bytes as a JSON object.
4) Repeat.

Pseudocode:
```text
connect -> stream
buffer = ""
while stream open:
  chunk = read()
  buffer += chunk
  while buffer contains "\n":
    line, buffer = split once at first "\n"
    obj = json.parse(line)
    handle(obj)
```

### Why JSON (not Protobuf) for the TCP stream

- Shot frequency is low (human swing cadence), so JSON size/CPU overhead is negligible.
- JSON is human-readable and easy to debug with basic tools (telnet/netcat, logs).
- Trivial to parse in Unreal/Unity/Node/Python/C# without extra dependencies or codegen.
- NDJSON framing (newline) avoids custom length-prefix framing and simplifies client code.
- If you later need high-rate telemetry or strict contracts, add a second port that uses length-prefixed Protobuf and publish the .proto schema.

---

## Data Model and Conversions

API models (exposed via HTTP/TCP):
- BallData
  - Speed: ball speed at launch (mph).
  - SpinAxis: tilt of the spin axis; negative = draw/left, positive = fade/right (deg).
  - TotalSpin: resultant spin magnitude (rpm).
  - BackSpin / SideSpin: components derived from TotalSpin and SpinAxis (rpm).
  - HLA: horizontal launch angle, positive right (deg).
  - VLA: vertical launch angle (deg).
  - CarryDistance: distance carried in the air; may be 0 if not computed here (yards).
- ClubData
  - Speed: clubhead speed prior to impact (mph).
  - SpeedAtImpact: clubhead speed at impact (mph) — mirrors Speed when distinct not available.
  - AngleOfAttack: positive = up, negative = down (deg).
  - FaceToTarget: clubface angle relative to target line (deg).
  - Path: club path direction relative to target line (deg).
  - VerticalFaceImpact / HorizontalFaceImpact / ClosureRate: included for forward compatibility; may be 0 if not reported.

Conversions from device `Metrics`:
- Ball speed: meters/second × 2.2369 → mph.
- Spin axis: inverted sign to align with conventional left/right.
- Back/Side spin: derived from TotalSpin and SpinAxis via cosine/sine (SpinAxis in radians).

### Using shot data in a game (basic 3D flight)

Simplest way to visualize a shot as a projectile (no drag/spin):
- Read `ballData.Speed` (mph), `ballData.HLA` (deg, right +), `ballData.VLA` (deg, up +).
- Convert to SI and compute an initial velocity vector in a right-handed system (x=forward, y=right, z=up).

Example (Python-like):
```python
import math

gravity_mps2 = 9.81

ball_speed_mps = ballData["Speed"] * 0.44704
horizontal_angle_rad = math.radians(ballData["HLA"])   # right +
vertical_angle_rad   = math.radians(ballData["VLA"])   # up +

velocity_forward_x = ball_speed_mps * math.cos(vertical_angle_rad) * math.cos(horizontal_angle_rad)
velocity_right_y   = ball_speed_mps * math.cos(vertical_angle_rad) * math.sin(horizontal_angle_rad)
velocity_up_z      = ball_speed_mps * math.sin(vertical_angle_rad)

time_step_s = 0.01
position_x = position_y = position_z = 0.0

while position_z >= 0.0:
    position_x += velocity_forward_x * time_step_s
    position_y += velocity_right_y   * time_step_s
    velocity_up_z -= gravity_mps2 * time_step_s
    position_z += velocity_up_z * time_step_s
    # draw/update ball at (position_x, position_y, position_z)
```

Note: To model ball flight more accurately, consider adding the effects of lift from `BackSpin` and lateral curvature from `SideSpin`. This can be done by applying small upward and rightward accelerations in your simulation, each proportional to the respective spin values.

Protobuf and BLE framing overview:
- The device communicates via a custom Bluetooth Low Energy (BLE) service implemented through the Generic Attribute Profile (GATT). Requests and responses are encoded as Protocol Buffer (protobuf) messages, and then wrapped in a custom protocol:
  - Each message frame contains a length prefix and a CRC16 checksum for integrity, followed by Consistent Overhead Byte Stuffing (COBS) encoding to ensure robust BLE transmission. Data is then split into BLE MTU-sized chunks.
  - Communication begins with a simple handshake to establish a session. Subsequent messages include a request counter to match commands with responses.

---

## Build, Run, and Packaging (Raspberry Pi / Linux)

### Requirements

**Target (the Pi that runs the bridge):**
- A 64-bit OS (Raspberry Pi OS 64-bit or Ubuntu for ARM). The build is `linux-arm64`.
- BlueZ and its D-Bus service (`bluez`, shipped by default on Raspberry Pi OS). Verify with `bluetoothctl --version`.
- A Bluetooth LE controller (the Pi 3/4/5 built-in radio works).
- The Garmin Approach R10, paired and trusted beforehand (see below).
- If you build a **self-contained** package, the Pi needs **no .NET install**. If you build framework-dependent, install the **.NET 8 runtime** on the Pi.

**Build machine (can be any x64/arm64 Linux, macOS, or Windows):**
- .NET 8 SDK. `dotnet publish` cross-compiles to `linux-arm64` from any host — you do not need to build on the Pi.

### Build

Self-contained, single-file `linux-arm64` (recommended — bundles the runtime):
```bash
dotnet publish -c Release -r linux-arm64 --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
# Output: bin/Release/net8.0/linux-arm64/publish/  (r10-bridge + settings.json)
```

Or use the helper script, which also produces zipped artifacts under `publish/`:
```bash
./build/publish-linux-arm64.sh
```

Framework-dependent (smaller; requires the .NET 8 runtime on the Pi):
```bash
dotnet publish -c Release -r linux-arm64 --self-contained false
```

Local quick run on your dev machine (x64):
```bash
dotnet run          # HTTP/TCP start immediately; Bluetooth needs BlueZ + an R10
```

### Quickstart: one command (`run.sh`)

The publish zip includes a launcher that does everything below in one shot. On the Pi, after unzipping:

```bash
chmod +x run.sh r10-bridge
./run.sh
```

It starts the bluetooth service, auto-pairs + trusts the R10 if it isn't already paired (turn the R10 on first — it uses the name from `settings.json`), then launches the bridge. Re-run it any time; if the R10 is already paired it skips straight to launch. The manual steps below are the fallback if you'd rather do it by hand or auto-pairing fails.

### Pairing the R10 (one-time, on the Pi)

Bluetooth pairing is an OS responsibility; the bridge only connects to an already-paired device.

```bash
sudo systemctl enable --now bluetooth      # ensure BlueZ is running
bluetoothctl
# In the bluetoothctl prompt:
power on
agent on
default-agent
scan on                 # wake the R10, then wait for "Approach R10" to appear
scan off
pair    <MAC>           # e.g. pair AA:BB:CC:DD:EE:FF
trust   <MAC>           # trust so it reconnects automatically
quit
```

Confirm the device name BlueZ reports (`Name`/`Alias`) matches `bluetoothDeviceName` in `settings.json` (default `Approach R10`). The bridge itself does the GATT connect/reconnect — you do **not** need to `connect` in `bluetoothctl`.

### Run

1) Copy the publish folder to the Pi and ensure `settings.json` sits next to the `r10-bridge` executable.
2) Make it executable and start it:
   ```bash
   chmod +x r10-bridge
   ./r10-bridge
   ```
   You should see logs for HTTP/TCP startup and Bluetooth connection attempts. If BlueZ can’t be reached, the bridge logs a clear message and keeps the HTTP/TCP servers running.
3) Visit `http://127.0.0.1:5001/` (or the configured port) for the dashboard.
4) Connect a TCP client to `127.0.0.1:5510` to receive live shot JSON.

Note: the R10 exposes standard BLE GATT services, so no special permissions are usually required. If GATT access is denied, ensure your user can talk to BlueZ over the D-Bus system bus (typically membership in the `bluetooth` group), or run under a user that can.

### Run as a service (optional, systemd)

Easiest: let `run.sh` do it. From the source checkout on the Pi (after pairing once with a plain `./run.sh`):
```bash
./run.sh --install-service
```
This builds a self-contained binary into `/opt/r10-bridge`, installs and enables a `r10-bridge` systemd unit (auto-start on boot, restart on failure, runs as your user), and prints the management commands. It won't overwrite an existing `/opt/r10-bridge/settings.json`, so your config survives reinstalls.

**Where do the logs go?** Two places, always:
- A dated **log file** next to the app: `logs/r10-bridge-YYYY-MM-DD.log` (under the working directory — `/opt/r10-bridge/logs/` for the service). The exact path is printed at startup (`Writing log to …`). Tail it with `tail -f logs/r10-bridge-*.log`.
- The **console** — your terminal in the foreground, or the systemd **journal** for the service:
  ```bash
  journalctl -u r10-bridge -f            # follow live
  journalctl -u r10-bridge --since today # today's logs
  ```

To do it by hand instead, create `/etc/systemd/system/r10-bridge.service`:
```ini
[Unit]
Description=R10 Bridge
After=bluetooth.target
Wants=bluetooth.target

[Service]
WorkingDirectory=/opt/r10-bridge
ExecStart=/opt/r10-bridge/r10-bridge
Restart=on-failure
# The default settings.json binds HTTP/TCP to localhost only.

[Install]
WantedBy=multi-user.target
```
Then:
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now r10-bridge
journalctl -u r10-bridge -f     # follow logs
```
Note: the app reads `settings.json` from its working directory, so `WorkingDirectory` must point at the install folder. Running as a service is non-interactive; the "press enter to close" prompt is only for foreground runs.

### Packaging

- Preferred: self-contained publish (above) to distribute a single-folder app that includes the .NET runtime — nothing to install on the Pi.
- Script: `build/publish-linux-arm64.sh` bundles the runtime and emits two zips under `publish/` (Bluetooth-enabled and a Bluetooth-disabled variant for HTTP/TCP-only testing).
- Always include `settings.json` alongside the executable in your package.

---

## Design Choices & Rationale

- HTTP: simple frontend + API
  - The HTTP service doubles as a lightweight UI and JSON API. It’s intentionally minimal so web apps can embed or call it directly.
  - Future-friendly: adding WebSockets for push updates is straightforward, but not required for this use case.

- TCP: designed for real-time clients
  - The broadcast socket emits newline-delimited JSON per shot. Designed to be simple to consume in Unreal, Unity, or any software.
  - Intended to run on the same machine as the game engine. It binds to localhost; do not expose it to the internet. While it’s read-only, you still don’t want random clients connecting.

- Local-only services by default
  - Minimizes security exposure; avoids cross-network attack surface for a tool meant to run on a player’s local machine.

- Decoupling via ConnectionManager
  - Keeps BLE acquisition, HTTP control surface, and TCP streaming loosely coupled; simplifies testing and future expansion.

- Newline-delimited JSON for streaming
  - Simple, resilient, and friendly to many downstream consumers and log/indexing tools.

- Verbose logging with channels
  - Separate channels for R10 BLE, HTTP, and TCP aid troubleshooting without overwhelming consumers.

---

## Extending the System

- Add more HTTP endpoints (e.g., expose live readiness, battery events, or historical shots).
- Add remote-bind support behind a reverse proxy with TLS/auth if needed.
- Support additional broadcast formats (e.g., UDP, WebSocket) while keeping the core JSON payload consistent.

---

## Caveats and Known Limitations

- Some fields from the device are not currently exposed or are set to 0 when unavailable.
- The BLE protocol and protobuf service are based on reverse engineering; behavior may change across device firmware versions.
- The service assumes a single local R10; multiple concurrent devices are not currently supported.

---

## Quick Reference (Ports & Endpoints)

- HTTP (default): `127.0.0.1:5001`
  - `GET /` (dashboard/OK)
  - `GET /api/status`
  - `GET /api/last-shot`
  - `POST /api/wake`
  - `POST /api/tilt/calibrate/start`
  - `POST /api/tilt/calibrate/reset`

- TCP broadcast (default): `127.0.0.1:5510`
  - Broadcast-only, newline-delimited JSON: `{ ballData, clubData, atUtc }`