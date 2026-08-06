#!/usr/bin/env python3
"""Generate the two connection QR codes for the Unity AR client.

The client (ServerConfigPanel) scans two independent QR codes, identified by
their URL scheme so they can be scanned in any order:

    gRPC     ->  grpc://HOST:PORT     (grpcs:// for TLS)
    FastAPI  ->  http://HOST:PORT     (https:// for TLS)

gRPC and FastAPI may be on different machines, so each code carries its own host.
When only one host is given it is used for both.

Usage
-----
    # same machine for both services
    python tools/generate_connection_qr.py --host 192.168.1.50

    # different machines / ports
    python tools/generate_connection_qr.py \
        --grpc-host 192.168.1.50 --grpc-port 50051 \
        --http-host 192.168.1.51 --http-port 8080

    # TLS on the FastAPI side
    python tools/generate_connection_qr.py --host example.local --http-tls

Output
------
* Always prints a scannable ASCII QR for each endpoint to the terminal.
* Also writes PNGs (connection_grpc.png / connection_fastapi.png) when Pillow
  is installed.

Requires the `qrcode` package (pure-python; ASCII works with no other deps):
    pip install qrcode            # ASCII only
    pip install "qrcode[pil]"     # ASCII + PNG
"""

import argparse
import sys

# print_ascii() emits Unicode block glyphs (█); the default Windows console
# codepage (cp1252) can't encode them, so force UTF-8 on stdout.
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

try:
    import qrcode
except ImportError:
    sys.exit(
        "The 'qrcode' package is required.\n"
        "  pip install qrcode          # ASCII QR in the terminal\n"
        '  pip install "qrcode[pil]"   # also save PNG files'
    )


def build_payloads(args):
    grpc_host = args.grpc_host or args.host
    http_host = args.http_host or args.host
    if not grpc_host or not http_host:
        sys.exit("Provide --host (for both) or --grpc-host and --http-host.")

    grpc_scheme = "grpcs" if args.grpc_tls else "grpc"
    http_scheme = "https" if args.http_tls else "http"

    grpc = f"{grpc_scheme}://{grpc_host}:{args.grpc_port}"
    http = f"{http_scheme}://{http_host}:{args.http_port}"
    return grpc, http


def emit(label, payload, png_path):
    print()
    print("=" * 60)
    print(f"{label}: {payload}")
    print("=" * 60)

    qr = qrcode.QRCode(border=2)
    qr.add_data(payload)
    qr.make(fit=True)
    # print_ascii() renders a scannable code straight to the terminal.
    qr.print_ascii(invert=True)

    try:
        img = qr.make_image()
        img.save(png_path)
        print(f"[saved] {png_path}")
    except Exception as exc:  # Pillow missing or write failure — ASCII still works.
        print(f"[png skipped] {png_path} ({exc})")


def main():
    p = argparse.ArgumentParser(description="Generate gRPC + FastAPI connection QR codes.")
    p.add_argument("--host", help="Host/IP used for both services unless overridden.")
    p.add_argument("--grpc-host", help="gRPC host/IP (overrides --host).")
    p.add_argument("--http-host", help="FastAPI host/IP (overrides --host).")
    p.add_argument("--grpc-port", type=int, default=50051)
    p.add_argument("--http-port", type=int, default=8080)
    p.add_argument("--grpc-tls", action="store_true", help="Use grpcs:// (TLS).")
    p.add_argument("--http-tls", action="store_true", help="Use https:// (TLS).")
    args = p.parse_args()

    grpc_payload, http_payload = build_payloads(args)
    emit("gRPC", grpc_payload, "connection_grpc.png")
    emit("FastAPI", http_payload, "connection_fastapi.png")

    print("\nScan both codes on the client's Server Configuration screen "
          "(Scan QR codes → Camera).")


if __name__ == "__main__":
    main()
