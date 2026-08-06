# QR-code connection setup

The Unity client can be pointed at its gRPC and FastAPI servers by scanning **two
QR codes** on the *Server Configuration* screen — in addition to typing the IP
manually or using UDP auto-discovery.

Two independent codes (identified by URL scheme) mean gRPC and FastAPI can live on
different machines, and they can be scanned in any order:

| Service | QR payload            | Default port |
|---------|-----------------------|--------------|
| gRPC    | `grpc://HOST:PORT`    | 50051        |
| FastAPI | `http://HOST:PORT`    | 8080         |

TLS variants: `grpcs://HOST:PORT` and `https://HOST:PORT`.

When both codes are scanned the client **auto-connects** and goes straight to the
job selector. The endpoints are saved (PlayerPrefs) and survive APK reinstalls.

## Scanning behaviour — one at a time or both together?

Either works. On the *Server Configuration* screen, tap **Scan QR codes** and then:

* **Both at once** — hold both codes in the camera's view together (e.g. printed
  side by side, or shown together on the `/api/connection` page). They are captured
  within a frame or two of each other. *(For this, enable **Detect Multiple
  Barcodes** on the Barcode object — see setup below.)*
* **One after the other** — point at the gRPC code, then move to the FastAPI code
  (or vice-versa). Order does not matter.

The panel shows live progress — `gRPC ✓  FastAPI –` → `gRPC ✓  FastAPI ✓` — and the
moment both show ✓ it connects automatically. You must capture **both** codes,
because each carries a different endpoint (gRPC = live session/control/asset
streaming; FastAPI = manifest + asset/target HTTP downloads); the client can't run
with only one.

How detection works under the hood: `QrEndpointScanner` enables the Vuforia
`BarcodeBehaviour`, listens to `OnTargetStatusChanged`, and also polls every
`BarcodeBehaviour`'s `InstanceData.Text` each frame while scanning (this second
path catches barcodes Vuforia surfaces on internally-cloned instances). Each
decoded string is parsed by `EndpointQrPayload`; the scheme (`grpc://` vs
`http://`) decides which slot it fills, and each service is captured only once.

Note: disabling Vuforia's **video background** (as on the optical see-through
M4000) does **not** affect scanning — the camera still feeds frames to Vuforia.

## Generating the codes

### A. The server generates them automatically (recommended)

`server-kit` prints both QR codes and serves a browser page — no separate step.

* **On startup**, both the gRPC server (`grpc_server_main.py`) and the FastAPI
  server (`server_kit_main.py`) print scannable ASCII QR codes for
  `grpc://HOST:PORT` and `http://HOST:PORT` to the console.
* **Browser page:** open `http://<server>:<http_port>/api/connection` to show
  both QR codes on screen (SVG — no Pillow needed) and scan them with the glasses.
* **Raw endpoints:**
  * `GET /api/connection/endpoints` → JSON `{host, grpc, http}`
  * `GET /api/connection/qr/grpc.svg` and `/api/connection/qr/fastapi.svg` → SVG

Ports come from `GUIDANCE_GRPC_PORT` / `GUIDANCE_HTTP_PORT` (defaults 50051 / 8080).

#### Choosing the network interface (multi-NIC machines)

The host in the QR codes is the machine's **LAN IP on the network the glasses share**
(not a public/internet address). On a machine with several interfaces
(Ethernet + Wi-Fi + VPN, etc.) the server helps you pick the right one:

* **Interactive picker (startup):** if more than one interface is found and the
  server is started in a terminal, it lists them and asks which to advertise, e.g.

  ```
  Multiple network interfaces detected. Which one should the glasses connect to?
    [0] 141.43.71.88     BTU-LAN 1Gbit  (default)
    [1] 10.20.48.246     LAN-Verbindung
    [2] 169.254.161.225  OpenVPN Connect DCO Adapter
  Select network [0-2, Enter=141.43.71.88]:
  ```

  The choice is used for the whole run (console codes *and* the `/api/connection`
  page). Friendly interface names need the optional `psutil` package; without it
  the picker still lists the IPs.

* **Non-interactive / headless:** the picker never blocks. Force the interface up
  front with either env var (both skip the prompt):
  * `GUIDANCE_PUBLIC_HOST=192.168.1.50` — exact host/IP (also accepts a hostname),
    or
  * `GUIDANCE_NETWORK_INDEX=1` — pick by the list index above.
  * `GUIDANCE_NETWORK_SELECT=off` — never prompt; just use the primary interface.

  If none are set and stdin isn't a TTY, it uses the primary (internet-bound)
  interface and logs which one it picked.

This needs the `qrcode` package (already added to `server-kit/app/requirements.txt`).
If it's missing, the server still starts and just prints the URLs.

### B. Standalone generator (no running server)

```bash
# same machine for both services
python tools/generate_connection_qr.py --host 192.168.1.50

# different machines / ports
python tools/generate_connection_qr.py \
    --grpc-host 192.168.1.50 --grpc-port 50051 \
    --http-host 192.168.1.51 --http-port 8080
```

ASCII QR (prints in the terminal, scannable) needs only `qrcode`; PNG output needs
Pillow:

```bash
pip install qrcode          # ASCII only
pip install "qrcode[pil]"   # also writes connection_grpc.png / connection_fastapi.png
```

Any third-party QR generator works too — just encode the exact strings above.

## One-time Unity scene setup (required for scanning)

Scanning uses **Vuforia's built-in Barcode Scanner** (Vuforia already owns the
camera, so there's no camera conflict — no extra library needed).

1. In the scene, add a Barcode object: **GameObject → Vuforia Engine → Barcode**.
   This adds a `BarcodeBehaviour`.
2. On that object's Barcode Behaviour:
   - Under **Observed Types**, enable **QR Code** and uncheck the rest (faster,
     more reliable than scanning all symbologies).
   - Enable **Detect Multiple Barcodes** if you want to scan both codes in one
     shot (both in view together). Leave it off to scan them one at a time.
3. Wiring (either option works):
   - Leave it to runtime: `QrEndpointScanner` finds the `BarcodeBehaviour`
     automatically (keep the Barcode GameObject **active** so it can be found), **or**
   - Assign explicitly: add a `QrEndpointScanner` component, drag the Barcode
     object into its `Barcode` field, and drag the `QrEndpointScanner` into the
     `ServerConfigPanel`'s `Qr Scanner` field.

If no Barcode object is present, the **Scan QR codes** button reports the scanner
is unavailable and manual entry / auto-discovery still work.

## Code map

- `Assets/App/Networking/EndpointQrPayload.cs` — parses the QR strings (pure, testable).
- `Assets/App/Vuforia/QrEndpointScanner.cs` — wraps Vuforia `BarcodeBehaviour` (behind `VUFORIA_ENGINE`);
  detects via `OnTargetStatusChanged` + a per-frame `InstanceData.Text` poll; verbose logging toggle for debugging.
- `Assets/App/UI/ServerConfigPanel.cs` — Scan button + capture UI, auto-connect on both.
- `Assets/App/Runtime/AppBootstrap.cs` — separate gRPC/HTTP hosts, persistence.
- `tools/generate_connection_qr.py` — standalone QR generator.
- `server-kit/app/connection_qr.py` — server-side QR builder (ASCII + SVG + HTML page).
- `server-kit/app/grpc_server_main.py` / `server_kit_main.py` — print QR on startup;
  FastAPI also serves `/api/connection`, `/api/connection/endpoints`, `/api/connection/qr/{service}.svg`.
