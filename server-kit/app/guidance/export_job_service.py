"""Persistent export-job queue and processing state management."""

from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
import json
from pathlib import Path
from threading import Lock
from typing import Protocol
import uuid


class ExportJobRunner(Protocol):
    """Protocol implemented by package exporters consumed by queued jobs."""

    def build_job_packages(self, job_id: str):
        ...


class ExportJobState(str, Enum):
    """Lifecycle states for queued and processed export jobs."""

    QUEUED = "queued"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"
    CANCELED = "canceled"


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


def _parse_iso(ts: str) -> datetime:
    return datetime.fromisoformat(ts)


class ExportJobService:
    """Queues, processes, persists, and cleans up export jobs."""

    def __init__(self, exporter: ExportJobRunner, store_file: Path | None = None) -> None:
        self._exporter = exporter
        self._records: dict[str, ExportJobRecord] = {}
        self._lock = Lock()
        self._store_file = store_file
        self._load_records()

    @staticmethod
    def _record_from_store(item: dict[str, object]) -> ExportJobRecord:
        state = ExportJobState(str(item["state"]))
        return ExportJobRecord(
            run_id=str(item["runId"]),
            job_id=str(item["jobId"]),
            state=state,
            created_at=str(item["createdAt"]),
            updated_at=str(item["updatedAt"]),
            generated_steps=item.get("generatedSteps"),
            manifest_path=item.get("manifestPath"),
            error=item.get("error"),
        )

    @staticmethod
    def _record_to_store(record: ExportJobRecord) -> dict[str, object]:
        return {
            "runId": record.run_id,
            "jobId": record.job_id,
            "state": record.state.value,
            "createdAt": record.created_at,
            "updatedAt": record.updated_at,
            "generatedSteps": record.generated_steps,
            "manifestPath": record.manifest_path,
            "error": record.error,
        }

    @staticmethod
    def _select_latest(left: ExportJobRecord, right: ExportJobRecord) -> ExportJobRecord:
        left_ts = _parse_iso(left.updated_at)
        right_ts = _parse_iso(right.updated_at)
        return left if left_ts >= right_ts else right

    def _load_records(self) -> None:
        if self._store_file is None or not self._store_file.exists():
            return

        payload = json.loads(self._store_file.read_text(encoding="utf-8"))
        records: dict[str, ExportJobRecord] = {}
        for item in payload.get("jobs", []):
            record = self._record_from_store(item)
            records[record.run_id] = record
        self._records = records

    def reload_from_store(self) -> None:
        with self._lock:
            self._load_records()

    def _persist_records(self, merge_on_disk: bool = True) -> None:
        if self._store_file is None:
            return
        self._store_file.parent.mkdir(parents=True, exist_ok=True)

        merged = dict(self._records)
        if merge_on_disk and self._store_file.exists():
            on_disk = json.loads(self._store_file.read_text(encoding="utf-8"))
            for item in on_disk.get("jobs", []):
                record = self._record_from_store(item)
                existing = merged.get(record.run_id)
                if existing is None:
                    merged[record.run_id] = record
                    continue
                merged[record.run_id] = self._select_latest(existing, record)

        self._records = merged
        payload = {
            "jobs": [
                self._record_to_store(record)
                for record in merged.values()
            ]
        }
        self._store_file.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    def list_queued_run_ids(self) -> list[str]:
        with self._lock:
            queued = [
                (record.created_at, record.run_id)
                for record in self._records.values()
                if record.state == ExportJobState.QUEUED
            ]
        queued.sort(key=lambda item: (item[0], item[1]))
        return [run_id for _, run_id in queued]

    def process_next_queued(self) -> str | None:
        queued = self.list_queued_run_ids()
        if not queued:
            return None
        run_id = queued[0]
        self.process(run_id)
        return run_id

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

    def cancel(self, run_id: str) -> ExportJobRecord | None:
        with self._lock:
            current = self._records.get(run_id)
            if current is None:
                return None
            if current.state != ExportJobState.QUEUED:
                return current

            canceled = ExportJobRecord(
                run_id=current.run_id,
                job_id=current.job_id,
                state=ExportJobState.CANCELED,
                created_at=current.created_at,
                updated_at=_utc_now_iso(),
            )
            self._records[run_id] = canceled
            self._persist_records()
            return canceled

    def cleanup(self, retention_seconds: int) -> int:
        if retention_seconds < 0:
            return 0

        terminal = {
            ExportJobState.SUCCEEDED,
            ExportJobState.FAILED,
            ExportJobState.CANCELED,
        }
        now = datetime.now(timezone.utc)

        removed = 0
        with self._lock:
            for run_id, record in list(self._records.items()):
                if record.state not in terminal:
                    continue
                age = (now - _parse_iso(record.updated_at)).total_seconds()
                if age < retention_seconds:
                    continue
                del self._records[run_id]
                removed += 1

            if removed > 0:
                self._persist_records(merge_on_disk=False)

        return removed

    def process(self, run_id: str) -> None:
        with self._lock:
            current = self._records.get(run_id)
            if current is None:
                return
            if current.state != ExportJobState.QUEUED:
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
