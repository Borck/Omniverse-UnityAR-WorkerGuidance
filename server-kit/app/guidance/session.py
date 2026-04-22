"""Shared server-side session primitives for HTTP and gRPC entry points."""

from dataclasses import dataclass
from enum import Enum
import json
from pathlib import Path


@dataclass(frozen=True)
class ServerConfig:
    grpc_host: str = "0.0.0.0"
    grpc_port: int = 50051
    http_host: str = "0.0.0.0"
    http_port: int = 8080


class SessionState(str, Enum):
    CONNECTED = "Connected"
    IDLE = "Idle"
    STEP_READY = "StepReady"


@dataclass(frozen=True)
class SessionContext:
    session_id: str
    device_id: str
    state: SessionState


class SessionManager:
    """Manages session registration, resume, state transitions, and persistence."""

    def __init__(self, store_file: Path | None = None) -> None:
        self._store_file = store_file
        self._session_count = 0
        self._sessions: dict[str, SessionContext] = {}
        self._device_index: dict[str, str] = {}
        self._load_from_store()

    def _load_from_store(self) -> None:
        if self._store_file is None or not self._store_file.exists():
            return

        payload = json.loads(self._store_file.read_text(encoding="utf-8"))
        self._session_count = int(payload.get("sessionCount", 0))
        self._device_index = {
            str(device_id): str(session_id)
            for device_id, session_id in payload.get("deviceIndex", {}).items()
        }
        self._sessions = {
            session_id: SessionContext(
                session_id=session_id,
                device_id=str(item["deviceId"]),
                state=SessionState(item["state"]),
            )
            for session_id, item in payload.get("sessions", {}).items()
        }

    def _persist(self) -> None:
        if self._store_file is None:
            return

        self._store_file.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "sessionCount": self._session_count,
            "deviceIndex": self._device_index,
            "sessions": {
                session_id: {
                    "deviceId": context.device_id,
                    "state": context.state.value,
                }
                for session_id, context in self._sessions.items()
            },
        }
        self._store_file.write_text(
            json.dumps(payload, indent=2, ensure_ascii=True),
            encoding="utf-8",
        )

    def register_or_resume_session(self, device_id: str) -> tuple[str, bool]:
        """Returns existing session for device or creates a new one."""
        known_session_id = self._device_index.get(device_id)
        if known_session_id is not None and known_session_id in self._sessions:
            return known_session_id, True

        self._session_count += 1
        session_id = f"session-{self._session_count}"
        self._sessions[session_id] = SessionContext(
            session_id=session_id,
            device_id=device_id,
            state=SessionState.CONNECTED,
        )
        self._device_index[device_id] = session_id
        self._persist()
        return session_id, False

    def register_session(self) -> str:
        session_id, _ = self.register_or_resume_session(f"anonymous-{self._session_count + 1}")
        return session_id

    def set_state(self, session_id: str, state: SessionState) -> None:
        """Updates persisted state for the given session identifier."""
        previous = self._sessions[session_id]
        self._sessions[session_id] = SessionContext(
            session_id=session_id,
            device_id=previous.device_id,
            state=state,
        )
        self._persist()

    def get(self, session_id: str) -> SessionContext | None:
        return self._sessions.get(session_id)
