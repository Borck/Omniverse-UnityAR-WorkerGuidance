from pathlib import Path
import sys

sys.path.append(str(Path(__file__).resolve().parents[1]))

from app.export_job_service import ExportJobService


class FakeExportResult:
    def __init__(self, generated_steps: int, manifest_path: Path) -> None:
        self.generated_steps = generated_steps
        self.manifest_path = manifest_path


class FakeExporter:
    def __init__(self, manifest_path: Path) -> None:
        self._manifest_path = manifest_path

    def build_job_packages(self, job_id: str) -> FakeExportResult:
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
