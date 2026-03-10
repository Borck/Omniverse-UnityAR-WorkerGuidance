from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
import json
from pathlib import Path
from threading import Lock
from typing import Protocol
import uuid


class ExportJobRunner(Protocol):
    def build_job_packages(self, job_id: str):
        ...


class ExportJobState(str, Enum):
    QUEUED = "queued"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"


@dataclass(frozen=True)
class ExportJobRecord:
    run_id: str
    job_id: str
    state: ExportJobState
    created_at: str
    updated_at: str
    generated_steps: int | None = None
    manifest_path: str | None = None
    error: str | None = None


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


class ExportJobService:
    def __init__(self, exporter: ExportJobRunner, store_file: Path | None = None) -> None:
        self._exporter = exporter
        self._records: dict[str, ExportJobRecord] = {}
        self._lock = Lock()
        self._store_file = store_file
        self._load_records()

    def _load_records(self) -> None:
        if self._store_file is None or not self._store_file.exists():
            return

        payload = json.loads(self._store_file.read_text(encoding="utf-8"))
        records: dict[str, ExportJobRecord] = {}
        for item in payload.get("jobs", []):
            state = ExportJobState(item["state"])
            record = ExportJobRecord(
                run_id=item["runId"],
                job_id=item["jobId"],
                state=state,
                created_at=item["createdAt"],
                updated_at=item["updatedAt"],
                generated_steps=item.get("generatedSteps"),
                manifest_path=item.get("manifestPath"),
                error=item.get("error"),
            )
            records[record.run_id] = record
        self._records = records

    def _persist_records(self) -> None:
        if self._store_file is None:
            return
        self._store_file.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "jobs": [
                {
                    "runId": record.run_id,
                    "jobId": record.job_id,
                    "state": record.state.value,
                    "createdAt": record.created_at,
                    "updatedAt": record.updated_at,
                    "generatedSteps": record.generated_steps,
                    "manifestPath": record.manifest_path,
                    "error": record.error,
                }
                for record in self._records.values()
            ]
        }
        self._store_file.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    def enqueue(self, job_id: str) -> ExportJobRecord:
        now = _utc_now_iso()
        record = ExportJobRecord(
            run_id=str(uuid.uuid4()),
            job_id=job_id,
            state=ExportJobState.QUEUED,
            created_at=now,
            updated_at=now,
        )
        with self._lock:
            self._records[record.run_id] = record
            self._persist_records()
        return record

    def get(self, run_id: str) -> ExportJobRecord | None:
        with self._lock:
            return self._records.get(run_id)

    def process(self, run_id: str) -> None:
        with self._lock:
            current = self._records.get(run_id)
            if current is None:
                return
            self._records[run_id] = ExportJobRecord(
                run_id=current.run_id,
                job_id=current.job_id,
                state=ExportJobState.RUNNING,
                created_at=current.created_at,
                updated_at=_utc_now_iso(),
            )
            self._persist_records()

        current = self.get(run_id)
        if current is None:
            return

        try:
            result = self._exporter.build_job_packages(current.job_id)
            finished = ExportJobRecord(
                run_id=current.run_id,
                job_id=current.job_id,
                state=ExportJobState.SUCCEEDED,
                created_at=current.created_at,
                updated_at=_utc_now_iso(),
                generated_steps=result.generated_steps,
                manifest_path=str(result.manifest_path),
            )
        except Exception as exc:  # pragma: no cover - defensive path
            finished = ExportJobRecord(
                run_id=current.run_id,
                job_id=current.job_id,
                state=ExportJobState.FAILED,
                created_at=current.created_at,
                updated_at=_utc_now_iso(),
                error=str(exc),
            )

        with self._lock:
            self._records[run_id] = finished
            self._persist_records()
