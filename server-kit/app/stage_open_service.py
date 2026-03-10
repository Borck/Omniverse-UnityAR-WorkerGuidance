"""Mock stage-open contract validator used by smoke endpoint checks."""

from dataclasses import dataclass
from urllib.parse import urlparse


@dataclass(frozen=True)
class StageOpenResult:
    success: bool
    code: str
    message: str
    stage_uri: str


class StageOpenService:
    """Validates configured stage URI shape without requiring Kit runtime."""

    def __init__(self, stage_uri: str) -> None:
        self._stage_uri = (stage_uri or "").strip()

    def smoke_open(self) -> StageOpenResult:
        if not self._stage_uri:
            return StageOpenResult(
                success=False,
                code="STAGE_URI_NOT_CONFIGURED",
                message="GUIDANCE_STAGE_URI is empty",
                stage_uri="",
            )

        parsed = urlparse(self._stage_uri)
        if parsed.scheme not in {"omniverse", "file", "http", "https"}:
            return StageOpenResult(
                success=False,
                code="STAGE_URI_INVALID",
                message=f"Unsupported stage URI scheme: {parsed.scheme or '-'}",
                stage_uri=self._stage_uri,
            )

        return StageOpenResult(
            success=True,
            code="STAGE_OPEN_SMOKE_OK",
            message="Stage-open smoke contract passed (mocked, no Kit runtime)",
            stage_uri=self._stage_uri,
        )
