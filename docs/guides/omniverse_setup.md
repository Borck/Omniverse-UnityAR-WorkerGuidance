# Omniverse Setup Guide

How to get `omni.client` installed and working in this repo without the full Omniverse Connect Samples bloat.

---

## Why Not Connect Samples?

The official `connect-samples` repo pulls in a massive amount of clutter and is fragile to set up. The approach below installs only what's needed — the `omni.client` Python module and its bundled DLL — directly from NVIDIA's PyPI index.

---

## Installation

### 1. Create and activate a virtual environment

```powershell
python -m venv venv
venv\Scripts\activate
```

### 2. Install server dependencies

```powershell
pip install fastapi uvicorn
```

### 3. Install the Omniverse client

```powershell
pip install omniverseclient --extra-index-url https://pypi.nvidia.com
```

This pulls the `omni.client` module along with its native DLL, bundled inside the wheel. No separate Omniverse Launcher or Connect SDK install required.

> Browse available versions: https://pypi.nvidia.com/omniverseclient/

---

## Asset Paths

| Location | Path |
|----------|------|
| **Remote (Nucleus)** | `/Users/shahan/demonstrator-26-02-25` |
| **Local (server-kit)** | `C:/Users/shahan/VsCodeProjects/Omniverse-UnityAR-WorkerGuidance/server-kit/app/unity/assets/` |

---

## gRPC Port Setup (Port 50051)

The gRPC server runs on port **50051**. On Windows you need to both verify it's listening and open it in the firewall.

### Verify gRPC is listening on all interfaces

Run this in PowerShell on the server machine:

```powershell
netstat -an | findstr "50051"
```

Expected output:

```
TCP    0.0.0.0:50051          0.0.0.0:0              LISTENING
TCP    [::]:50051             [::]:0                 LISTENING
```

Both lines must appear. If you only see `127.0.0.1`, the server is bound to localhost only and remote clients (Vuzix, Android) won't reach it.

### Open port 50051 in Windows Firewall

Run as **Administrator**:

```powershell
New-NetFirewallRule -DisplayName "gRPC Server 50051" -Direction Inbound -Protocol TCP -LocalPort 50051 -Action Allow
```

This only needs to be done once per machine. After this, Unity/Vuzix clients connecting over the local network can reach the gRPC server.

---

## Quick Checklist

- [ ] Virtual environment created and activated
- [ ] `fastapi`, `uvicorn`, `omniverseclient` installed
- [ ] Nucleus remote path accessible (`/Users/shahan/demonstrator-26-02-25`)
- [ ] `netstat` shows port 50051 listening on `0.0.0.0`
- [ ] Firewall rule created for port 50051
