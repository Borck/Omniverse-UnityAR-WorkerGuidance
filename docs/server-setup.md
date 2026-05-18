# Server Setup Guide

This guide covers how to run both the Python server-kit (production-oriented) and the
ASP.NET test server (development / demo).

---

## Python Server-Kit

### Prerequisites

- Python 3.11 or later
- `pip` package manager

### Installation

```bash
cd server-kit
pip install -r app/requirements.txt
```

### Configuration (environment variables)

All settings have sensible defaults. Override via `.env` file or shell environment.

| Variable | Default | Description |
|----------|---------|-------------|
| `GUIDANCE_HTTP_HOST` | `0.0.0.0` | HTTP listen address |
| `GUIDANCE_HTTP_PORT` | `8080` | HTTP listen port |
| `GUIDANCE_GRPC_HOST` | `0.0.0.0` | gRPC listen address |
| `GUIDANCE_GRPC_PORT` | `50051` | gRPC listen port |
| `GUIDANCE_LOG_LEVEL` | `INFO` | Logging level (`DEBUG`, `INFO`, `WARNING`, `ERROR`) |
| `GUIDANCE_MANIFEST_ROOT` | `shared/samples/manifests` | Directory containing `*.manifest.json` files |
| `GUIDANCE_ASSET_ROOT` | `shared/samples/assets` | Directory containing versioned GLB model files |
| `GUIDANCE_TARGET_ROOT` | `shared/samples/targets` | Directory containing versioned Vuforia target files |
| `GUIDANCE_STEP_DEFINITION_FILE` | `shared/samples/step-definitions.yaml` | YAML step definition file |
| `GUIDANCE_DRACO_ENABLED` | `false` | Enable Draco mesh compression (`true`/`false`) |
| `GUIDANCE_EXPORT_JOB_PROCESSING_MODE` | `inline` | `inline` or `enqueue-only` |

A complete list is available in `server-kit/app/config.py`.

### Running Locally

**HTTP + gRPC (combined):**

```bash
# From repo root
python -m uvicorn server_kit.app.server_kit_main:app --host 0.0.0.0 --port 8080 &
python -m server_kit.app.grpc_server_main
```

Or using the convenience script if present:

```bash
bash tools/scripts/start-dev.sh
```

### Running with Docker

```bash
docker build -f server-kit/Dockerfile -t guidance-server .
docker run -p 8080:8080 -p 50051:50051 guidance-server
```

### Health Check

```bash
curl http://localhost:8080/health
# → {"status": "ok"}
```

### Sample Data

Sample manifests, assets, and step definitions are in `shared/samples/`.
The default job ID is `job-mock-001`.

```bash
# Fetch the manifest for the sample job
curl http://localhost:8080/api/jobs/job-mock-001/manifest
```

---

## ASP.NET Test Server

The test server provides a web-based admin UI for operators to submit assembly jobs
and push them to connected Unity devices. It implements the same gRPC services as the
Python server.

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (download from Microsoft)

### Running

**Double-click (Windows):**

```
test-server\start-server.bat
```

**Shell (macOS / Linux):**

```bash
bash test-server/start-server.sh
```

**Manual:**

```bash
cd test-server
dotnet run
```

The server starts on `http://0.0.0.0:5000` (HTTP/1.1 + HTTP/2 on the same port for
gRPC cleartext).

### Web Admin UI Routes

| URL | Description |
|-----|-------------|
| `http://<server-ip>:5000/` | Redirect to job list |
| `http://<server-ip>:5000/Index` | Job overview — list all submitted jobs |
| `http://<server-ip>:5000/Jobs/Submit` | Submit a new assembly job with file uploads |

### gRPC Endpoints

| Service | Description |
|---------|-------------|
| `guidance.v1.GuidanceSessionService/Connect` | Duplex stream session |
| `guidance.v1.AssetTransferService/StreamStepAsset` | Chunk-streamed asset download |

Configure the Unity client to point at the test server by setting
**gRPC Target** to `<server-ip>:5000` in the `AppBootstrap` Inspector.

### Data Storage

Uploaded files and generated manifests are stored under `test-server/data/`:

```
test-server/data/
  assets/{assetVersion}/{filename}.glb
  targets/{targetVersion}/{filename}.dat
  manifests/{jobId}.manifest.json
```

These files persist across restarts. The job store (in-memory) resets on restart;
however, the file assets remain on disk and can be re-served.

---

## Network Requirements

- The Unity AR device and the server must be able to reach each other over IP (same Wi-Fi/LAN, shared mobile hotspot, VPN, or any routable network).
- Required open ports:
  - **5000** (test server: HTTP + gRPC)
  - **8080** (Python server: HTTP)
  - **50051** (Python server: gRPC)
- No internet connection is required during operation, only mutual reachability.

### Windows firewall — first time per host

The Unity AR device cannot reach the servers without inbound rules for the gRPC and HTTP ports. Run **PowerShell as Administrator** on the host:

```powershell
New-NetFirewallRule -DisplayName "Guidance gRPC" -Direction Inbound -Protocol TCP -LocalPort 50051 -Action Allow -Profile Any
New-NetFirewallRule -DisplayName "Guidance HTTP" -Direction Inbound -Protocol TCP -LocalPort 8080  -Action Allow -Profile Any
```

`-Profile Any` is important when using a mobile hotspot — Windows classifies hotspot networks as Public, and a Private-only rule will not apply.

### Verifying reachability from the AR device

From any other machine on the same network:

```powershell
# Health check (Python server)
curl http://<server-ip>:8080/health
# expected: {"status":"ok"}

# gRPC port reachability
Test-NetConnection <server-ip> -Port 50051
# expected: TcpTestSucceeded : True
```

### Running the server on a different machine than the assets

The server process is location-independent: any PC with Python and the dependencies installed can run it. As long as the server can reach the asset/manifest store (currently a local folder; future: Nucleus URL), and the AR device can reach the server's IP, the system works.

To move the server to a new PC:
1. Install dependencies on that PC (`pip install -r server-kit/app/requirements.txt`)
2. Open the firewall ports (above)
3. Update the Unity client's `AppBootstrap.grpcTarget` / `httpBridgeBaseUrl` to the new PC's IP and rebuild the APK
