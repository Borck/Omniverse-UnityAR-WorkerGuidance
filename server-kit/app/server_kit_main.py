from fastapi import FastAPI
from fastapi import BackgroundTasks
from fastapi import HTTPException
from fastapi.responses import FileResponse
from fastapi.responses import JSONResponse
from fastapi import status
from pathlib import Path

try:
  from .config import AppConfig
  from .draco_codec import DracoCodec
  from .draco_codec import DracoCodecConfig
  from .export_job_service import ExportJobService
  from .export_pipeline import StepPackageExporter
  from .logging_config import configure_logging
except ImportError:
  from config import AppConfig
  from draco_codec import DracoCodec
  from draco_codec import DracoCodecConfig
  from export_job_service import ExportJobService
  from export_pipeline import StepPackageExporter
  from logging_config import configure_logging

try:
  from .manifest_service import ManifestService
  from .step_definition_repository import StepDefinitionRepository
except ImportError:
  from manifest_service import ManifestService
  from step_definition_repository import StepDefinitionRepository


def create_app(config: AppConfig | None = None) -> FastAPI:
  resolved_config = config or AppConfig.from_env()
  repo_root = Path(__file__).resolve().parents[2]
  manifests_root = repo_root / resolved_config.manifests_root
  asset_root = repo_root / resolved_config.asset_root
  target_root = repo_root / resolved_config.target_root
  export_manifest_root = repo_root / resolved_config.export_manifest_root
  export_asset_root = repo_root / resolved_config.export_asset_root
  export_job_store_file = repo_root / resolved_config.export_job_store_file
  step_definition_file = repo_root / resolved_config.step_definition_file

  manifest_service = ManifestService(manifests_root=manifests_root)
  step_repo = StepDefinitionRepository(step_definition_file=step_definition_file)
  draco_codec = DracoCodec(
    DracoCodecConfig(
      enabled=resolved_config.draco_enabled,
      encoder_command_template=resolved_config.draco_encoder_command_template,
      toolchain=resolved_config.draco_toolchain,
    )
  )
  package_exporter = StepPackageExporter(
    manifest_service=manifest_service,
    source_asset_root=asset_root,
    output_asset_root=export_asset_root,
    output_manifest_root=export_manifest_root,
    draco_codec=draco_codec,
  )
  export_job_service = ExportJobService(package_exporter, store_file=export_job_store_file)
  logger = configure_logging(resolved_config.log_level)

  app = FastAPI(title="Guidance Server", version="0.2.0")
  app.state.config = resolved_config
  app.state.logger = logger

  @app.on_event("startup")
  async def startup_event() -> None:
    logger.info(
      "http service started",
      session_id="-",
      step_id="-",
      event="server.start",
    )

  @app.get("/health")
  def health() -> dict[str, str]:
    logger.info("health check", session_id="-", step_id="-", event="http.health")
    return {"status": "ok"}

  @app.get("/api/jobs/{job_id}/manifest")
  def get_manifest(job_id: str) -> JSONResponse:
    try:
      manifest = manifest_service.get_manifest(job_id)
    except FileNotFoundError as exc:
      raise HTTPException(status_code=404, detail="Manifest not found") from exc

    payload = {
      "jobId": manifest.job_id,
      "workflowVersion": manifest.workflow_version,
      "steps": [
        {
          "stepId": step.step_id,
          "partId": step.part_id,
          "assetVersion": step.asset_version,
          "glbUrl": f"/api/assets/{step.asset_version}/{step.glb_file}",
          "stepJsonUrl": f"/api/assets/{step.asset_version}/{step.step_json_file}",
          "targetVersion": step.target_version,
          "targetUrl": f"/api/targets/{step.target_version}/{step.target_file}",
          "compression": step.compression,
        }
        for step in manifest.steps
      ],
    }
    headers = {
      "Cache-Control": "public, max-age=60",
      "ETag": f'"manifest-{job_id}"',
    }
    logger.info("manifest served", session_id="-", step_id="-", event="http.manifest")
    return JSONResponse(content=payload, headers=headers)

  @app.post("/api/jobs/{job_id}/packages:build", status_code=status.HTTP_202_ACCEPTED)
  def build_runtime_packages(job_id: str, background_tasks: BackgroundTasks) -> JSONResponse:
    try:
      manifest_service.get_manifest(job_id)
    except FileNotFoundError as exc:
      raise HTTPException(status_code=404, detail="Manifest not found") from exc

    queued = export_job_service.enqueue(job_id)
    background_tasks.add_task(export_job_service.process, queued.run_id)

    logger.info(
      "package export queued",
      session_id="-",
      step_id="-",
      event="http.packages.build",
      correlation_id=queued.run_id,
    )

    return JSONResponse(
      status_code=status.HTTP_202_ACCEPTED,
      content={
        "jobId": job_id,
        "runId": queued.run_id,
        "state": queued.state,
        "statusUrl": f"/api/package-jobs/{queued.run_id}",
      },
    )

  @app.get("/api/package-jobs/{run_id}")
  def get_package_job(run_id: str) -> JSONResponse:
    record = export_job_service.get(run_id)
    if record is None:
      raise HTTPException(status_code=404, detail="Package job not found")

    payload = {
      "runId": record.run_id,
      "jobId": record.job_id,
      "state": record.state,
      "createdAt": record.created_at,
      "updatedAt": record.updated_at,
      "generatedSteps": record.generated_steps,
      "manifestPath": record.manifest_path,
      "error": record.error,
    }
    return JSONResponse(content=payload)

  @app.get("/api/assets/{asset_version}/{file_name}")
  def get_asset(asset_version: str, file_name: str) -> FileResponse:
    file_path = asset_root / asset_version / file_name
    if not file_path.exists():
      raise HTTPException(status_code=404, detail="Asset not found")
    headers = {
      "Cache-Control": "public, immutable, max-age=31536000",
      "ETag": f'"asset-{asset_version}-{file_name}"',
    }
    logger.info(
      "asset served",
      session_id="-",
      step_id="-",
      event="http.asset",
      correlation_id=asset_version,
    )
    return FileResponse(file_path, headers=headers)

  @app.get("/api/targets/{target_version}/{file_name}")
  def get_target(target_version: str, file_name: str) -> FileResponse:
    file_path = target_root / target_version / file_name
    if not file_path.exists():
      raise HTTPException(status_code=404, detail="Target not found")
    headers = {
      "Cache-Control": "public, immutable, max-age=31536000",
      "ETag": f'"target-{target_version}-{file_name}"',
    }
    logger.info(
      "target served",
      session_id="-",
      step_id="-",
      event="http.target",
      correlation_id=target_version,
    )
    return FileResponse(file_path, headers=headers)

  @app.get("/api/jobs/{job_id}/steps")
  def get_steps(job_id: str) -> JSONResponse:
    steps = step_repo.get_steps(job_id)
    payload = {
      "jobId": job_id,
      "steps": [step.__dict__ for step in steps],
    }
    logger.info("steps served", session_id="-", step_id="-", event="http.steps")
    return JSONResponse(content=payload)

  return app


app = create_app()
