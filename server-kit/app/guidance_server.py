from dataclasses import dataclass


@dataclass(frozen=True)
class ServerConfig:
    grpc_host: str = "0.0.0.0"
    grpc_port: int = 50051
    http_host: str = "0.0.0.0"
    http_port: int = 8080


class SessionManager:
    def __init__(self) -> None:
        self._session_count = 0

    def register_session(self) -> str:
        self._session_count += 1
        return f"session-{self._session_count}"
