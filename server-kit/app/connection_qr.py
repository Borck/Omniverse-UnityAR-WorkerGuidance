"""Connection QR codes for the Unity AR client.

The client's Server Configuration screen scans two independent QR codes, each
identified by its URL scheme so they can be scanned in any order and can even
point at different machines:

    gRPC     ->  grpc://HOST:PORT
    FastAPI  ->  http://HOST:PORT

This module builds those two payloads from the running server's config and
renders them as:

  * an ASCII QR printed to the console on startup (scannable straight off the
    terminal), and
  * SVG (served by the FastAPI /api/connection page — no Pillow needed).

`qrcode` is an optional dependency: if it isn't installed the server still boots
and just prints the URLs (which can be typed in or turned into QR codes with any
generator).
"""
from __future__ import annotations

import io
import os
import socket
import sys
from dataclasses import dataclass

try:
    from .discovery_beacon import _detect_lan_ip
except ImportError:  # when run as a top-level module (sys.path = app/)
    from discovery_beacon import _detect_lan_ip

try:
    import qrcode
except ImportError:  # optional dependency
    qrcode = None  # type: ignore[assignment]


@dataclass(frozen=True)
class ConnectionEndpoints:
    host: str
    grpc_url: str
    http_url: str


# Resolved once per process so QR builds (console + web requests) stay consistent
# and the interactive picker only ever runs once at startup.
_resolved_host: str | None = None


def list_lan_interfaces() -> list[tuple[str, str]]:
    """Return (interface_name, IPv4) for every non-loopback IPv4 on this host.

    The primary outbound interface (the one that would reach the internet) is
    placed first so it's the natural default. Uses psutil for friendly interface
    names when available; falls back to a stdlib IP-only enumeration otherwise.
    """
    primary = _detect_lan_ip()
    results: list[tuple[str, str]] = []
    seen: set[str] = set()

    try:
        import psutil  # optional: gives real interface names (Wi-Fi, Ethernet, ...)

        for name, addrs in psutil.net_if_addrs().items():
            for a in addrs:
                if a.family == socket.AF_INET and a.address and not a.address.startswith("127."):
                    if a.address not in seen:
                        seen.add(a.address)
                        results.append((name, a.address))
    except Exception:
        # stdlib fallback — IPs only, interface name unknown.
        try:
            for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
                ip = info[4][0]
                if ip and not ip.startswith("127.") and ip not in seen:
                    seen.add(ip)
                    results.append(("?", ip))
        except socket.gaierror:
            pass

    # Make sure the primary interface exists and sorts first.
    if primary and not primary.startswith("127."):
        if primary not in seen:
            results.insert(0, ("primary", primary))
        results.sort(key=lambda t: t[1] != primary)

    return results


def _configured_or_override_host(config) -> str | None:
    """A host chosen without any detection: explicit override, then concrete config."""
    override = os.getenv("GUIDANCE_PUBLIC_HOST", "").strip()
    if override:
        return override
    for candidate in (getattr(config, "grpc_host", ""), getattr(config, "http_host", "")):
        candidate = (candidate or "").strip()
        if candidate and candidate not in ("0.0.0.0", "::", ""):
            return candidate
    return None


def resolve_host(config) -> str:
    """The host the glasses should dial — NON-interactive (never prompts).

    Priority: GUIDANCE_PUBLIC_HOST → concrete configured host → a host already
    chosen this process (interactive picker or env index) → GUIDANCE_NETWORK_INDEX
    → best-effort auto-detected LAN IP. Safe to call from web requests.
    """
    forced = _configured_or_override_host(config)
    if forced:
        return forced
    if _resolved_host:
        return _resolved_host

    interfaces = list_lan_interfaces()
    if not interfaces:
        return _detect_lan_ip()

    idx_env = os.getenv("GUIDANCE_NETWORK_INDEX", "").strip()
    if idx_env.isdigit() and 0 <= int(idx_env) < len(interfaces):
        return interfaces[int(idx_env)][1]

    return interfaces[0][1]  # primary interface


def prompt_and_resolve_host(config) -> str:
    """Resolve the host and, if MULTIPLE interfaces are found, let the operator
    pick one at the console. Call once at startup. The choice is cached for the
    rest of the process (so web requests reuse it and never prompt).

    Never blocks a headless server: an explicit override / config host / env index
    skips the prompt, and if stdin isn't a TTY (or GUIDANCE_NETWORK_SELECT=off)
    it falls back to the auto choice.
    """
    global _resolved_host

    forced = _configured_or_override_host(config)
    if forced:
        _resolved_host = forced
        return forced
    if _resolved_host:
        return _resolved_host

    interfaces = list_lan_interfaces()
    if not interfaces:
        _resolved_host = _detect_lan_ip()
        return _resolved_host

    idx_env = os.getenv("GUIDANCE_NETWORK_INDEX", "").strip()
    if idx_env.isdigit() and 0 <= int(idx_env) < len(interfaces):
        _resolved_host = interfaces[int(idx_env)][1]
        return _resolved_host

    if len(interfaces) == 1:
        _resolved_host = interfaces[0][1]
        return _resolved_host

    select_mode = os.getenv("GUIDANCE_NETWORK_SELECT", "auto").strip().lower()
    interactive = bool(getattr(sys, "stdin", None)) and sys.stdin.isatty()
    if select_mode != "off" and interactive:
        _resolved_host = _prompt_choice(interfaces)
    else:
        _resolved_host = interfaces[0][1]
        if len(interfaces) > 1:
            _print_safe(
                f"[connection] {len(interfaces)} network interfaces found; using {_resolved_host}. "
                "Set GUIDANCE_PUBLIC_HOST or GUIDANCE_NETWORK_INDEX to choose another."
            )
    return _resolved_host


def _prompt_choice(interfaces: list[tuple[str, str]]) -> str:
    default_ip = interfaces[0][1]
    _print_safe("\nMultiple network interfaces detected. Which one should the glasses connect to?")
    for i, (name, ip) in enumerate(interfaces):
        marker = "  (default)" if i == 0 else ""
        _print_safe(f"  [{i}] {ip:<15}  {name}{marker}")
    try:
        raw = input(f"Select network [0-{len(interfaces) - 1}, Enter={default_ip}]: ").strip()
    except (EOFError, KeyboardInterrupt):
        return default_ip
    if raw == "":
        return default_ip
    if raw.isdigit() and 0 <= int(raw) < len(interfaces):
        return interfaces[int(raw)][1]
    _print_safe(f"Invalid choice; using default {default_ip}.")
    return default_ip


def build_endpoints(config) -> ConnectionEndpoints:
    """Builds the gRPC + FastAPI QR payloads from server config."""
    host = resolve_host(config)
    grpc_url = f"grpc://{host}:{config.grpc_port}"
    http_url = f"http://{host}:{config.http_port}"
    return ConnectionEndpoints(host=host, grpc_url=grpc_url, http_url=http_url)


def qrcode_available() -> bool:
    return qrcode is not None


def ascii_qr(data: str) -> str | None:
    """Scannable ASCII QR, or None when qrcode isn't installed."""
    if qrcode is None:
        return None
    qr = qrcode.QRCode(border=2)
    qr.add_data(data)
    qr.make(fit=True)
    buf = io.StringIO()
    qr.print_ascii(out=buf, invert=True)
    return buf.getvalue()


def svg_qr(data: str) -> bytes | None:
    """QR as an SVG document (bytes), or None when qrcode isn't installed.

    SVG needs no Pillow — good for serving from FastAPI on a headless host.
    """
    if qrcode is None:
        return None
    import qrcode.image.svg as svg  # local import: only when rendering

    img = qrcode.make(data, image_factory=svg.SvgPathImage, border=2)
    buf = io.BytesIO()
    img.save(buf)
    return buf.getvalue()


def _print_safe(text: str) -> None:
    """Print block-glyph QR art even on consoles whose codepage can't encode it."""
    try:
        print(text)
    except UnicodeEncodeError:
        try:
            sys.stdout.buffer.write((text + "\n").encode("utf-8"))
            sys.stdout.buffer.flush()
        except Exception:
            print(text.encode("ascii", "replace").decode("ascii"))


def log_connection_qr(config, logger=None) -> ConnectionEndpoints:
    """Log the endpoints (structured) and print scannable ASCII QR to the console.

    Called from both server entrypoints on startup. Returns the endpoints so the
    caller can reuse them if needed. If the machine has multiple network
    interfaces this prompts (once) for which one to advertise.
    """
    prompt_and_resolve_host(config)  # interactive network pick (startup only)
    endpoints = build_endpoints(config)

    if logger is not None:
        logger.info(
            "connection endpoints ready",
            session_id="-",
            step_id="-",
            event="connection.qr",
            correlation_id=endpoints.host,
        )

    _print_safe("")
    _print_safe("=" * 62)
    _print_safe(" Scan these on the client's Server Configuration screen")
    _print_safe(" (Scan QR codes -> Camera). Both codes = auto-connect.")
    _print_safe("=" * 62)

    for label, url in (("gRPC", endpoints.grpc_url), ("FastAPI", endpoints.http_url)):
        _print_safe(f"\n{label}: {url}")
        art = ascii_qr(url)
        if art is not None:
            _print_safe(art)
        else:
            _print_safe("  (install 'qrcode' to render a scannable code, or type the URL)")

    if qrcode is not None:
        _print_safe(
            f"\nOr open  http://{endpoints.host}:{config.http_port}/api/connection  "
            "to scan from a browser page.\n"
        )

    return endpoints


def connection_page_html(config) -> str:
    """Self-contained HTML page showing both QR codes (inline SVG) + URLs."""
    endpoints = build_endpoints(config)

    def _panel(label: str, url: str) -> str:
        svg_bytes = svg_qr(url)
        if svg_bytes is not None:
            art = svg_bytes.decode("utf-8")
        else:
            art = (
                "<p style='color:#b00'>Install the <code>qrcode</code> package on the "
                "server to render this code, or encode the URL below with any QR tool.</p>"
            )
        return (
            "<div style='display:inline-block;margin:24px;text-align:center;"
            "vertical-align:top;max-width:340px'>"
            f"<h2 style='font-family:sans-serif'>{label}</h2>"
            f"<div style='width:300px;height:300px;margin:0 auto'>{art}</div>"
            f"<p style='font-family:monospace;font-size:16px;word-break:break-all'>{url}</p>"
            "</div>"
        )

    return (
        "<!doctype html><html><head><meta charset='utf-8'>"
        "<title>Connect the AR client</title>"
        "<meta name='viewport' content='width=device-width, initial-scale=1'>"
        "</head><body style='text-align:center;background:#fff'>"
        "<h1 style='font-family:sans-serif'>Connect the AR client</h1>"
        "<p style='font-family:sans-serif'>On the glasses: Server Configuration &rarr; "
        "<b>Scan QR codes</b>. Scanning both auto-connects.</p>"
        f"{_panel('gRPC', endpoints.grpc_url)}{_panel('FastAPI', endpoints.http_url)}"
        "</body></html>"
    )
