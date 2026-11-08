"""UDP discovery beacon — lets the Unity client auto-find this server on the LAN.

The beacon periodically broadcasts a small JSON payload on UDP/<discovery_port>.
Unity listens for the payload at startup and uses it to populate the gRPC target
and HTTP bridge URL, removing the need to rebuild the APK when the server IP
changes (e.g. moving between sites or switching to a hotspot).
"""
from __future__ import annotations

import json
import logging
import socket
import threading
import time
from dataclasses import dataclass


PROTOCOL_VERSION = 1
SERVICE_NAME = "direkt-guidance"


@dataclass(frozen=True)
class BeaconConfig:
    grpc_port: int
    http_port: int
    discovery_port: int
    interval_seconds: float
    service_tag: str


def _detect_lan_ip() -> str:
    """Best-effort detection of the primary LAN IP for this host.

    Uses a UDP socket trick: connecting (no packets sent) to a public IP lets
    the OS pick the outbound interface, whose address is what other LAN
    devices would route to. Falls back to hostname resolution, then 127.0.0.1.
    """
    candidates: list[str] = []
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        candidates.append(sock.getsockname()[0])
    except OSError:
        pass
    finally:
        sock.close()

    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            candidates.append(info[4][0])
    except socket.gaierror:
        pass

    for ip in candidates:
        if ip and not ip.startswith("127."):
            return ip
    return "127.0.0.1"


class DiscoveryBeacon:
    """Background broadcaster. Start once; runs as a daemon thread until process exit."""

    def __init__(self, config: BeaconConfig, logger: logging.Logger | None = None) -> None:
        self._config = config
        self._logger = logger or logging.getLogger(__name__)
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self._thread is not None and self._thread.is_alive():
            return
        self._thread = threading.Thread(
            target=self._run, name="discovery-beacon", daemon=True
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)

    def _build_payload(self) -> bytes:
        payload = {
            "service": SERVICE_NAME,
            "version": PROTOCOL_VERSION,
            "tag": self._config.service_tag,
            "host": _detect_lan_ip(),
            "grpc": self._config.grpc_port,
            "http": self._config.http_port,
        }
        return json.dumps(payload, separators=(",", ":")).encode("utf-8")

    def _run(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        try:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        except OSError:
            pass
        target = ("255.255.255.255", self._config.discovery_port)
        interval = max(0.5, self._config.interval_seconds)
        self._logger.info(
            "discovery beacon started (port=%d tag=%s interval=%.1fs)",
            self._config.discovery_port,
            self._config.service_tag,
            interval,
        )
        try:
            while not self._stop_event.is_set():
                try:
                    sock.sendto(self._build_payload(), target)
                except OSError as exc:
                    self._logger.warning("discovery beacon send failed: %s", exc)
                # Use wait() so stop() can break us out of the sleep immediately.
                if self._stop_event.wait(interval):
                    break
        finally:
            sock.close()
            self._logger.info("discovery beacon stopped")


def start_beacon_from_config(config, logger: logging.Logger | None = None) -> DiscoveryBeacon | None:
    """Helper for the server entrypoints. Returns None if discovery is disabled."""
    if not getattr(config, "discovery_enabled", False):
        return None
    beacon_config = BeaconConfig(
        grpc_port=config.grpc_port,
        http_port=config.http_port,
        discovery_port=config.discovery_port,
        interval_seconds=config.discovery_interval_seconds,
        service_tag=config.service_tag or socket.gethostname(),
    )
    beacon = DiscoveryBeacon(beacon_config, logger=logger)
    beacon.start()
    return beacon


def _time_now() -> float:
    return time.monotonic()
