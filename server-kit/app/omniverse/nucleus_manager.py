"""Runtime registry + active selection for the configured Nucleus servers.

The active Nucleus is global to the server process. Switching it (from the
Unity Server Configuration screen via the /omni/nucleus endpoints) updates the
in-process active key and re-applies that server's credentials to the
OMNI_USER / OMNI_PASS broker variables that omni.client reads when it connects.

omni.client caches a connection/auth per host, so swapping the broker creds is
safe here because each host is always paired with one credential set (the two
servers use different logins).
"""
from __future__ import annotations

import os
import threading

from app.core.config import (
    NucleusEndpoint,
    default_active_key,
    load_nucleus_endpoints,
)


class NucleusManager:
    """Holds the configured Nucleus endpoints and the globally active one."""

    def __init__(self, endpoints: dict[str, NucleusEndpoint], active_key: str):
        if not endpoints:
            raise ValueError("NucleusManager requires at least one endpoint")
        self._endpoints = endpoints
        self._active_key = active_key
        self._lock = threading.Lock()
        self._apply_credentials(endpoints[active_key])

    @classmethod
    def from_env(cls) -> "NucleusManager":
        endpoints = load_nucleus_endpoints()
        return cls(endpoints, default_active_key(endpoints))

    # ── Reads ────────────────────────────────────────────────────────────
    def active(self) -> NucleusEndpoint:
        return self._endpoints[self._active_key]

    def active_server(self) -> str:
        """Base URL (e.g. omniverse://host) to prefix Nucleus paths with."""
        return self.active().server

    def list_endpoints(self) -> list[dict]:
        """Public view of the endpoints — never includes passwords."""
        return [
            {
                "key": ep.key,
                "name": ep.name,
                "server": ep.server,
                "active": ep.key == self._active_key,
            }
            for ep in self._endpoints.values()
        ]

    # ── Writes ───────────────────────────────────────────────────────────
    def set_active(self, key: str) -> NucleusEndpoint:
        """Switch the globally active Nucleus and apply its credentials.

        Raises KeyError if the key is not a configured endpoint.
        """
        key = (key or "").strip().lower()
        with self._lock:
            if key not in self._endpoints:
                raise KeyError(key)
            self._active_key = key
            endpoint = self._endpoints[key]
            self._apply_credentials(endpoint)
            self._reset_auth(endpoint.server)
            return endpoint

    @staticmethod
    def _apply_credentials(endpoint: NucleusEndpoint) -> None:
        os.environ["OMNI_USER"] = endpoint.user
        os.environ["OMNI_PASS"] = endpoint.password

    @staticmethod
    def _reset_auth(server: str) -> None:
        """Drop any cached auth for the target host so the next connection
        re-brokers with the just-applied OMNI_USER/OMNI_PASS.

        Defensive: omni.client caches a connection/auth per host, so a host
        contacted earlier won't otherwise pick up changed credentials. Imported
        lazily and fully guarded — on hosts without the Omniverse SDK (or if
        sign_out misbehaves) this is a no-op rather than a failure.
        """
        try:
            import omni.client  # type: ignore[import-not-found]
            omni.client.sign_out(server)
        except Exception:
            pass


# ── Process-wide singleton ──────────────────────────────────────────────
# Services that aren't request-scoped (service.py, nucleus_job_service.py)
# reach the active server through this accessor.
_manager: NucleusManager | None = None
_manager_lock = threading.Lock()


def get_manager() -> NucleusManager:
    global _manager
    if _manager is None:
        with _manager_lock:
            if _manager is None:
                _manager = NucleusManager.from_env()
    return _manager
