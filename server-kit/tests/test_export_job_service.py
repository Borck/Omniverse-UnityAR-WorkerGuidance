from pathlib import Path
from datetime import datetime, timedelta, timezone
import json
import sys

sys.path.append(str(Path(__file__).resolve().parents[1]))

from app.export_job_service import ExportJobService
from app.export_job_service import ExportJobState


class FakeExportResult:
    def __init__(self, generated_steps: int, manifest_path: Path) -> None:
        self.generated_steps = generated_steps
        self.manifest_path = manifest_path


class FakeExporter:
    def __init__(self, manifest_path: Path) -> None:
        self._manifest_path = manifest_path
        self.calls: list[str] = []

    def build_job_packages(self, job_id: str) -> FakeExportResult:
        self.calls.append(job_id)
        return FakeExportResult(generated_steps=1, manifest_path=self._manifest_path / f"{job_id}.manifest.json")


def test_export_job_service_persists_and_recovers_records(tmp_path: Path) -> None:
    store_file = tmp_path / "jobs.json"
    exporter = FakeExporter(tmp_path)

    service = ExportJobService(exporter=exporter, store_file=store_file)
    queued = service.enqueue("job-persist-001")
    service.process(queued.run_id)

    restarted_service = ExportJobService(exporter=exporter, store_file=store_file)
    recovered = restarted_service.get(queued.run_id)

    assert recovered is not None
    assert recovered.job_id == "job-persist-001"
    assert recovered.state == "succeeded"
    assert recovered.generated_steps == 1
    assert recovered.manifest_path is not None


def test_export_job_service_cancel_prevents_processing(tmp_path: Path) -> None:
    store_file = tmp_path / "jobs.json"
    exporter = FakeExporter(tmp_path)

    service = ExportJobService(exporter=exporter, store_file=store_file)
    queued = service.enqueue("job-cancel-001")
    canceled = service.cancel(queued.run_id)
    assert canceled is not None
    assert canceled.state == "canceled"

    service.process(queued.run_id)
    after = service.get(queued.run_id)
    assert after is not None
    assert after.state == "canceled"


def test_export_job_service_cleanup_removes_expired_terminal_jobs(tmp_path: Path) -> None:
    store_file = tmp_path / "jobs.json"
    now = datetime.now(timezone.utc)
    old = (now - timedelta(days=2)).isoformat()

    payload = {
        "jobs": [
            {
                "runId": "old-succeeded",
                "jobId": "job-old",
                "state": "succeeded",
                "createdAt": old,
                "updatedAt": old,
                "generatedSteps": 1,
                "manifestPath": "old.json",
                "error": None,
            },
            {
                "runId": "active-running",
                "jobId": "job-active",
                "state": "running",
                "createdAt": now.isoformat(),
                "updatedAt": now.isoformat(),
                "generatedSteps": None,
                "manifestPath": None,
                "error": None,
            },
        ]
    }
    store_file.write_text(json.dumps(payload), encoding="utf-8")

    service = ExportJobService(exporter=FakeExporter(tmp_path), store_file=store_file)
    removed = service.cleanup(retention_seconds=3600)

    assert removed == 1
    assert service.get("old-succeeded") is None
    running = service.get("active-running")
    assert running is not None
    assert running.state == ExportJobState.RUNNING


def test_export_job_service_process_next_queued_runs_single_oldest_entry(tmp_path: Path) -> None:
    store_file = tmp_path / "jobs.json"
    exporter = FakeExporter(tmp_path)
    service = ExportJobService(exporter=exporter, store_file=store_file)

    first = service.enqueue("job-1")
    second = service.enqueue("job-2")

    processed = service.process_next_queued()
    assert processed == first.run_id
    assert exporter.calls == ["job-1"]

    first_record = service.get(first.run_id)
    second_record = service.get(second.run_id)
    assert first_record is not None and first_record.state == ExportJobState.SUCCEEDED
    assert second_record is not None and second_record.state == ExportJobState.QUEUED


def test_export_job_service_reload_from_store_observes_external_changes(tmp_path: Path) -> None:
    store_file = tmp_path / "jobs.json"
    service = ExportJobService(exporter=FakeExporter(tmp_path), store_file=store_file)

    external_payload = {
        "jobs": [
            {
                "runId": "external-1",
                "jobId": "job-ext",
                "state": "queued",
                "createdAt": datetime.now(timezone.utc).isoformat(),
                "updatedAt": datetime.now(timezone.utc).isoformat(),
                "generatedSteps": None,
                "manifestPath": None,
                "error": None,
            }
        ]
    }
    store_file.write_text(json.dumps(external_payload), encoding="utf-8")

    service.reload_from_store()
    loaded = service.get("external-1")
    assert loaded is not None
    assert loaded.state == ExportJobState.QUEUED
