from dataclasses import dataclass
from enum import Enum


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
    state: SessionState


class SessionManager:
    def __init__(self) -> None:
        self._session_count = 0
        self._sessions: dict[str, SessionContext] = {}

    def register_session(self) -> str:
        self._session_count += 1
        session_id = f"session-{self._session_count}"
        self._sessions[session_id] = SessionContext(
            session_id=session_id,
            state=SessionState.CONNECTED,
        )
        return session_id

    def set_state(self, session_id: str, state: SessionState) -> None:
        self._sessions[session_id] = SessionContext(session_id=session_id, state=state)

    def get(self, session_id: str) -> SessionContext | None:
        return self._sessions.get(session_id)
