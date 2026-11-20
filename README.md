# DXDesk

An **AnyDesk-style remote support tool for internal IT**, written from scratch (no RDP).
When an admin wants to connect to a computer, the user on that computer first sees a
consent dialog (in Azerbaijani). Once the user clicks **Yes**, the admin **sees the
live screen and controls it**, and the user can watch everything that happens.

How it differs from RDP: RDP logs the user out of their session and is often disabled.
DXDesk shares the user's **real (attended) session**.

```
┌────────────────┐  RequestConnection  ┌───────────────┐  ConnectionRequest  ┌──────────────────┐
│ Server (admin) │ ──────────────────► │  Relay (hub)  │ ──────────────────► │ Client (user PC) │
│                │ ◄────────────────── │  /remotehub   │ ◄────────────────── │  consent dialog  │
└───────┬────────┘  ConnectionResponse └───────────────┘  ConnectionResponse └────────┬─────────┘
        │ (accepted + IP + port + session token)                (Yes → screen server)  │
        │                                                                              │
        │          direct TCP: screen frames + input (mouse / keyboard / clipboard)    │
        └─────────────────────────── Viewer  ◄─────────────────────────────────────────┘
```

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| **DXDesk.Relay**   | ASP.NET Core 9 SignalR hub (`/remotehub`) | Central signaling server (discovery + consent) |
| **DXDesk.Service** | Windows service (LocalSystem) | Runs on user PCs; launches the agent as SYSTEM in the active session |
| **DXDesk.Client**  | Console (net9.0-windows, silent WinExe) | The agent: consent dialog, then screen server |
| **DXDesk.Server**  | Console + WinForms (single exe) | Admin menu; opens the Viewer in-process |
| **DXDesk.Viewer**  | Library (WinForms) | Remote screen window (bundled in Server, not a separate exe) |
| **DXDesk.Core**    | Library | Screen capture, frame protocol, stream server/receiver, input, session auth |
| **DXDesk.Host**    | Console (test) | Streaming/protocol tests: `serve` / `selftest` / `authtest` / `inputtest` / `relaytest` / `tiletest` |
| **DXDesk.CaptureProbe** | Console (test) | Screen capture test |

## Architecture

- **Signaling / discovery / consent** → SignalR relay (`/remotehub`).
- **Media (screen + input)** → two paths, tried in parallel:
  1. **direct TCP** to the agent's port 7000 (fastest, works on the LAN);
  2. **through the relay** (`/stream`, WebSocket) — both the agent and the viewer connect *out* to the
     relay, so it works behind NAT, VPN, VMs and firewalls without opening any port on the PC.
  The viewer tries direct for 3 seconds, then falls back to the relay. Frame protocol:
  `[4-byte length][payload]`; input travels back over the same connection.
- When the user clicks **Yes**, the agent starts a screen server bound to that session:
  it accepts only the viewer holding the one-time session token, and stops automatically
  when the viewer disconnects or nobody connects within 30 seconds.
- The agent runs as **SYSTEM** and follows desktop switches (Default ↔ Secure/Winlogon),
  so UAC prompts are visible and can be answered remotely.

> Relay traffic is not encrypted yet (no TLS) — see [Security](#security).

---

## 1. Running the relay server

On a server every machine on the network can reach (Windows or Linux — the relay is
cross-platform):

```bash
cd src/DXDesk.Relay
dotnet run
```

Default: `http://0.0.0.0:5100` (hub: `/remotehub`). Change the port via `Urls` in
`appsettings.json`. Health checks: `GET /health`, `GET /api/clients` (requires the
`X-Admin-Token` header).

In Production the relay **will not start** without a strong `Admin:Secret` (see [Security](#security)).

---

## 2. Deploying to user PCs

Build self-contained single files (no .NET needed on the client):

```bash
dotnet publish src/DXDesk.Service/DXDesk.Service.csproj -c Release -r win-x64
dotnet publish src/DXDesk.Client/DXDesk.Client.csproj -c Release -r win-x64
```

Configure `appsettings.json` before deploying:

```json
{
  "Relay": { "Url": "http://RELAY-IP:5100/remotehub", "ClientSecret": "" },
  "Stream": { "Port": 7000, "TargetFps": 12, "TileSize": 128 },
  "Launch": { "Enabled": true, "AllowedPrograms": [] }
}
```

Put `DXDesk.Service.exe`, `DXDesk.Client.exe` and `appsettings.json` next to
`deploy/install-service.bat` and run it as administrator (or via GPO). It installs the
service into `%ProgramData%\DXDesk` and opens **port 7000 (stream)** in the firewall.

For the older client-only mode, use `deploy/install-client.bat` (startup script) or
`deploy/DXDesk-Client.xml` (scheduled task).

---

## 3. Using the admin (server) tool

The admin machine only needs **one file**: `DXDesk.Server.exe` (+ a small
`appsettings.json`). The Viewer is built in. Single-file publish:

```bash
dotnet publish src/DXDesk.Server/DXDesk.Server.csproj -c Release -r win-x64
```

`appsettings.json` → `Relay:Url` must point to the relay (e.g. `http://10.30.2.11:5100/remotehub`),
and `Admin:Secret` must match the relay's secret.

Menu (the UI is in Azerbaijani):

```
  [1] List online computers
  [2] Connect to a computer (enter hostname)
  [4] Launch a program as SYSTEM on a remote PC (UAC-free install)
  [3] Exit
```

- **[1]** — real-time list of online computers from the relay.
- **[2]** — enter a hostname → the user sees the consent dialog. On **Yes** the Viewer
  opens automatically and shows the remote screen; you control it with mouse and keyboard.
  On **No** you get "the user declined".
- **[4]** — runs a program (e.g. `msiexec.exe /i ... /qn`) as SYSTEM in the user's
  session, subject to the PC's local `Launch` policy.

---

## Test modes (DXDesk.Host)

```bash
# Direct streaming + control on one machine (no relay):
DXDesk.Host.exe serve 7000

# Automated tests:
DXDesk.Host.exe selftest    # network streaming + tile decode
DXDesk.Host.exe authtest    # session token: wrong/no token gets 0 frames, correct token gets frames
DXDesk.Host.exe inputtest   # input protocol
DXDesk.Host.exe relaytest        # relay signaling flow (relay must be running;
                                 # set DXDESK_ADMIN_SECRET / DXDESK_CLIENT_SECRET env vars)
DXDesk.Host.exe relaystreamtest  # screen stream through the relay (same env vars)
DXDesk.Host.exe capturetest      # screen capture method + brightness check
```

## Running the relay on Ubuntu with Docker

The Docker package for an Ubuntu server is in `deploy/relay/`.

```bash
# 1) Docker (if missing)
sudo apt update && sudo apt install -y docker.io docker-compose-v2
sudo systemctl enable --now docker

# 2) Firewall — port 5100
sudo ufw allow 5100/tcp

# 3) Copy the project to the server (src/DXDesk.Relay + deploy/relay), then:
cd deploy/relay
export MILLA_ADMIN_SECRET='long-random-secret'
export MILLA_CLIENT_SECRET='another-long-random-secret'   # optional but recommended
sudo -E docker compose up -d --build

# 4) Check
curl http://localhost:5100/health        # {"status":"ok",...}
sudo docker compose logs -f              # follow logs

# 5) Server IP
hostname -I
```

Then set `Relay:Url` in the **admin** and **client** `appsettings.json` files to the
Ubuntu server's IP, e.g. `http://192.168.0.50:5100/remotehub`.

> **Network note:** when the admin can't reach a PC directly (NAT, VPN, VM, firewall), the screen
> goes **through the relay**, so the relay's bandwidth matters: roughly 50–500 KB/s per active
> session. Only port **5100** on the relay has to be reachable from everyone.

## Bandwidth optimization (tile diffing)

The screen is split into 128×128 tiles; each frame sends only the **changed tiles**.
When the screen is completely static, **nothing is sent**.

- First frame (keyframe): ~280–350 KB (whole screen, once).
- After that: only changed tiles — usually **2–6 KB**, **0** on a static screen.
- Result: **~95%+ less bandwidth** than sending full frames.

Configuration (Client `appsettings.json` → `Stream`): `Port`, `TargetFps`, `TileSize`.

## Screen capture notes

- **DPI scaling:** the Client is Per-Monitor-V2 DPI-aware, so full physical resolution is
  captured on 125%/150% scaled displays (otherwise only part of the screen would show).
- **Multiple monitors:** only the **primary monitor** is captured for now.

## Troubleshooting

### Black screen in the viewer

First check the status bar at the bottom of the viewer — it shows whether the connection is
direct or via the relay, and any error. If `agent.log` says *"no viewer with a valid key connected
in time"*, the viewer never reached the PC: update the relay so the relay path is available.

The agent also handles the common capture problems automatically:

- **Wrong session** — the agent used to start in the physical console session. If the user works over
  RDP / a VM enhanced session, that session is empty and the screen was black. The service now picks
  the session where the user is actually logged in.
- **Display asleep** — while a viewer is connected the agent keeps the display on, and wakes it with a
  1-pixel mouse nudge if the picture is black.
- **GDI returns black frames** (some Windows 11 GPU drivers, hybrid graphics) — after 3 fully black
  frames the agent switches to **DXGI Desktop Duplication** automatically.

Diagnostics: `C:\ProgramData\DXDesk\agent.log` shows the session id, the capture method and any
fallback. `DXDesk.Host.exe capturetest` checks capture on a machine; set `DXDESK_CAPTURE=dxgi` to
force DXGI.

### Certificate / "Windows protected your PC" on Windows 11

Windows 11 blocks unsigned executables copied from a network share (SmartScreen, Smart App Control,
AppLocker). Sign every release:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\sign.ps1                # uses the DXDesk/SRXDesk code-signing cert
powershell -ExecutionPolicy Bypass -File deploy\sign.ps1 -NewSelfSigned # or create "CN=DXDesk Code Signing"
```

It signs all exes in `deploy/admin` and `deploy/service-package` (SHA256 + timestamp) and exports
`DXDesk-signing.cer`. With a self-signed or internal (AD CS) certificate, every PC must trust it:
deploy the `.cer` via GPO to **Trusted Root Certification Authorities** and **Trusted Publishers**,
or leave it next to `install-service.bat`, which imports it. The installer also removes the
"downloaded from the internet" mark from the copied files. A commercial or Azure Trusted Signing
certificate needs no distribution and is the only option that satisfies Smart App Control.

## Security

| What | How |
|------|-----|
| **Consent** | Every session requires the user's approval. The screen server starts only after "Yes" and stops when the viewer leaves or nobody connects within 30 s. |
| **Admin secret** | `Admin:Secret` on the relay (Docker: `MILLA_ADMIN_SECRET`) is **mandatory** — if it is empty or weak (min 16 chars), the relay refuses to start in Production. The Server's `appsettings.json` → `Admin:Secret` must match. |
| **Client secret** | Relay `Client:Secret` (Docker: `MILLA_CLIENT_SECRET`) = Service/Client `Relay:ClientSecret`. When set, only computers that know the secret can register. If a hostname is already online from a different IP, a second registration is rejected. |
| **Session token** | For each connection request the relay creates a random one-time token. The agent on port 7000 accepts only the viewer that sends this token, and the token goes only to the admin who made the request. The agent's "accepted" reply is verified with the same token. |
| **SYSTEM launch policy** | Service `appsettings.json` → `Launch:Enabled` (`false` = fully disabled) and `Launch:AllowedPrograms` (e.g. `["msiexec.exe", "C:\\Deploy\\*"]`). If the list is set, the PC only runs those programs, even if the relay is compromised. |
| **Audit log** | The relay writes every admin login, connection, rejection and SYSTEM command to `audit/audit.log` (JSON Lines). With Docker it is kept in `deploy/relay/audit/`. |
| **`/api/clients`** | Only with the `X-Admin-Token` header. |

Not done yet:

- ⏳ **Encryption (TLS):** relay traffic and the media stream are not encrypted yet — add TLS before any use over the internet.

**Upgrade rule:** the protocol has changed — update Relay, Service, Client and Server **at the same time**.

## Requirements

- Relay: .NET 9 (ASP.NET Core), Windows or Linux.
- Server / Viewer / Client / Service: net9.0-windows. Self-contained publishes need no runtime on the target.
