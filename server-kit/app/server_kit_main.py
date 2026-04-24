"""FastAPI entrypoint for guidance runtime HTTP and bridge endpoints."""

from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi import BackgroundTasks
from fastapi import HTTPException
from pydantic import BaseModel
from fastapi.responses import FileResponse
from fastapi.responses import JSONResponse
from fastapi import status
from pathlib import Path
from app.omniverse.router import router as omniverse_router
from app.unity.router import router as unity_router
from app.unity.router import connected_clients
import json as _json


try:
  from .config import AppConfig
  from .draco_codec import DracoCodec
  from .draco_codec import DracoCodecConfig
  from .export_job_service import ExportJobService
  from .export_pipeline import StepPackageExporter
  from .logging_config import configure_logging
  from .stage_open_service import StageOpenService
  from .layer_stack_resolver import LayerStackResolver
except ImportError:
  from config import AppConfig
  from draco_codec import DracoCodec
  from draco_codec import DracoCodecConfig
  from export_job_service import ExportJobService
  from export_pipeline import StepPackageExporter
  from logging_config import configure_logging
  from stage_open_service import StageOpenService
  from layer_stack_resolver import LayerStackResolver

try:
  from .manifest_service import ManifestService
  from .step_definition_repository import StepDefinitionRepository
  from .guidance_server import SessionManager, SessionState
except ImportError:
  from manifest_service import ManifestService
  from step_definition_repository import StepDefinitionRepository
  from guidance_server import SessionManager, SessionState


class HelloRequestPayload(BaseModel):
  device_id: str
  app_version: str
  capabilities: str


class HeartbeatPayload(BaseModel):
  session_id: str
  client_time_unix_ms: int


class ConnectEnvelope(BaseModel):
  hello: HelloRequestPayload


class HeartbeatEnvelope(BaseModel):
  heartbeat: HeartbeatPayload


class StepCompletedPayload(BaseModel):
  session_id: str
  job_id: str
  step_id: str
  completed_at_unix_ms: int


class StepCompletedEnvelope(BaseModel):
  step_completed: StepCompletedPayload


class LayerResolvePayload(BaseModel):
  sublayer_paths_bottom_to_top: list[str]


def create_app(config: AppConfig | None = None) -> FastAPI:
  """Creates the configured FastAPI application and wires runtime services."""
  resolved_config = config or AppConfig.from_env()
  if resolved_config.export_job_processing_mode not in {"inline", "enqueue-only"}:
    raise ValueError(
      "GUIDANCE_EXPORT_JOB_PROCESSING_MODE must be 'inline' or 'enqueue-only'"
    )
  repo_root = Path(__file__).resolve().parents[2]
  manifests_root = repo_root / resolved_config.manifests_root
  asset_root = repo_root / resolved_config.asset_root
  target_root = repo_root / resolved_config.target_root
  export_manifest_root = repo_root / resolved_config.export_manifest_root
  export_asset_root = repo_root / resolved_config.export_asset_root
  export_job_store_file = repo_root / resolved_config.export_job_store_file
  session_store_file = repo_root / resolved_config.session_store_file
  step_definition_file = repo_root / resolved_config.step_definition_file

  manifest_service = ManifestService(manifests_root=manifests_root)
  step_repo = StepDefinitionRepository(step_definition_file=step_definition_file)
  layer_stack_resolver = LayerStackResolver(step_repository=step_repo)
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
  session_manager = SessionManager(store_file=session_store_file)
  logger = configure_logging(resolved_config.log_level)
  stage_open_service = StageOpenService(stage_uri=resolved_config.stage_uri)
  processed_step_completions: dict[str, set[tuple[str, str, int]]] = {}
  default_job_id = "demonstrator-26-02-25"

  def _sorted_steps_for_job(job_id: str):
    steps = step_repo.get_steps(job_id)
    return sorted(
      steps,
      key=lambda s: (
        s.sequence_index,
        int(s.step_id) if s.step_id.isdigit() else 0,
        s.step_id,
      ),
    )

  def _next_step(job_id: str, completed_step_id: str):
    steps = _sorted_steps_for_job(job_id)
    if not steps:
      return None

    for index, step in enumerate(steps):
      if step.step_id == completed_step_id:
        if index + 1 < len(steps):
          return steps[index + 1]
        return None

    return None

  def _first_step(job_id: str):
    steps = _sorted_steps_for_job(job_id)
    return steps[0] if steps else None

  def _to_step_payload(step, job_id: str):
    return {
      "job_id": job_id,
      "step_id": step.step_id,
      "part_id": step.part_id,
      "display_name": step.display_name,
      "asset_version": step.asset_version,
      "target_id": step.target_id,
      "target_version": step.target_version,
    }

  def _set_session_state_with_log(session_id: str, next_state: SessionState, reason: str, step_id: str = "-") -> None:
    previous = session_manager.get(session_id)
    previous_state = previous.state.value if previous is not None else "Unknown"
    session_manager.set_state(session_id, next_state)
    logger.info(
      f"session state transition {previous_state} -> {next_state.value} ({reason})",
      session_id=session_id,
      step_id=step_id,
      event="http.session.state.transition",
      correlation_id=reason,
    )

  @asynccontextmanager
  async def lifespan(_: FastAPI):
    logger.info(
      "http service started",
      session_id="-",
      step_id="-",
      event="server.start",
    )
    import omni.client
    omni.client.initialize()
    yield
    omni.client.shutdown()

  app = FastAPI(title="Guidance Server", version="0.2.0", lifespan=lifespan)
  app.state.config = resolved_config
  app.state.logger = logger
  app.include_router(omniverse_router, prefix="/omni", tags=["Omniverse Connection"])
  app.include_router(unity_router, prefix="/unity", tags=["Unity Connection"])

  @app.get("/health")
  def health() -> dict[str, str]:
    logger.info("health check", session_id="-", step_id="-", event="http.health")
    return {"status": "ok"}

  @app.post("/api/stage:open-smoke")
  def stage_open_smoke() -> JSONResponse:
    result = stage_open_service.smoke_open()
    status_code = status.HTTP_200_OK
    if result.code == "STAGE_URI_NOT_CONFIGURED":
      status_code = status.HTTP_409_CONFLICT
    elif result.code == "STAGE_URI_INVALID":
      status_code = status.HTTP_400_BAD_REQUEST

    return JSONResponse(
      status_code=status_code,
      content={
        "success": result.success,
        "code": result.code,
        "message": result.message,
        "stageUri": result.stage_uri,
      },
    )

  @app.post("/session/connect")
  def session_connect(payload: ConnectEnvelope) -> JSONResponse:
    session_id, resumed = session_manager.register_or_resume_session(payload.hello.device_id)
    _set_session_state_with_log(
      session_id=session_id,
      next_state=SessionState.STEP_READY,
      reason="connect",
    )

    logger.info(
      f"session connected over http bridge ({'resumed' if resumed else 'new'})",
      session_id=session_id,
      step_id="-",
      event="http.session.connect",
    )

    first_step = _first_step(default_job_id)
    if first_step is not None:
      step_payload = _to_step_payload(first_step, default_job_id)
    else:
      step_payload = {
        "job_id": "job-mock-001",
        "step_id": "17",
        "part_id": "Bracket_12",
        "display_name": "Install bracket",
      }

    return JSONResponse(
      content={
        "hello_response": {
          "session_id": session_id,
          "protocol_version": "v1",
          "server_time_unix_ms": 0,
        },
        "step_activated": step_payload,
      }
    )

  @app.post("/session/heartbeat")
  def session_heartbeat(payload: HeartbeatEnvelope) -> JSONResponse:
    session = session_manager.get(payload.heartbeat.session_id)
    if session is None:
      return JSONResponse(
        status_code=status.HTTP_404_NOT_FOUND,
        content={
          "fault": {
            "code": "SESSION_NOT_FOUND",
            "message": "Session not found",
            "correlation_id": payload.heartbeat.session_id,
            "recoverable": True,
          }
        },
      )

    logger.info(
      "heartbeat over http bridge",
      session_id=payload.heartbeat.session_id,
      step_id="-",
      event="http.session.heartbeat",
    )
    return JSONResponse(content={"ping": {"nonce": f"hb-{payload.heartbeat.client_time_unix_ms}"}})

  @app.post("/session/step-completed")
  def session_step_completed(payload: StepCompletedEnvelope) -> JSONResponse:
    completion = payload.step_completed
    session = session_manager.get(completion.session_id)
    if session is None:
      return JSONResponse(
        status_code=status.HTTP_404_NOT_FOUND,
        content={
          "fault": {
            "code": "SESSION_NOT_FOUND",
            "message": "Session not found",
            "correlation_id": completion.session_id,
            "recoverable": True,
          }
        },
      )

    completion_key = (completion.job_id, completion.step_id, completion.completed_at_unix_ms)
    session_completions = processed_step_completions.setdefault(completion.session_id, set())
    if completion_key in session_completions:
      logger.info(
        "duplicate step completion ignored over http bridge",
        session_id=completion.session_id,
        step_id=completion.step_id,
        event="http.session.step_completed.duplicate",
      )
      return JSONResponse(content={"ack": {"duplicate": True}})

    session_completions.add(completion_key)
    _set_session_state_with_log(
      session_id=completion.session_id,
      next_state=SessionState.IDLE,
      reason="step-completed",
      step_id=completion.step_id,
    )
    logger.info(
      "step completed over http bridge",
      session_id=completion.session_id,
      step_id=completion.step_id,
      event="http.session.step_completed",
    )

    next_step = _next_step(completion.job_id, completion.step_id)
    if next_step is None:
      return JSONResponse(content={"ack": {"duplicate": False}})
    if next_step is not None:
        # Push via WebSocket to any connected AxisAlign/WS clients simultaneously
        step_msg = _json.dumps({"action": "load_step", "step_id": next_step.step_id})
        for ws_client in list(connected_clients):
            try:
                import asyncio
                asyncio.create_task(ws_client.send_text(step_msg))
            except Exception:
                connected_clients.remove(ws_client)
    _set_session_state_with_log(
      session_id=completion.session_id,
      next_state=SessionState.STEP_READY,
      reason="next-step-activated",
      step_id=next_step.step_id,
    )
    return JSONResponse(
      content={
        "step_activated": _to_step_payload(next_step, completion.job_id)
      }
    )

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
          "targetUrl": f"/api/targets/{step.target_version}/{step.target_file}" if step.target_version and step.target_file else "",
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
    if resolved_config.export_job_processing_mode == "inline":
      background_tasks.add_task(export_job_service.process, queued.run_id)

    logger.info(
      f"package export queued (mode={resolved_config.export_job_processing_mode})",
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
        "processingMode": resolved_config.export_job_processing_mode,
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

  @app.delete("/api/package-jobs/{run_id}")
  def cancel_package_job(run_id: str) -> JSONResponse:
    record = export_job_service.cancel(run_id)
    if record is None:
      raise HTTPException(status_code=404, detail="Package job not found")
    if record.state != "canceled":
      raise HTTPException(status_code=409, detail=f"Package job cannot be canceled in state '{record.state}'")

    logger.info(
      "package export canceled",
      session_id="-",
      step_id="-",
      event="http.packages.cancel",
      correlation_id=run_id,
    )
    return JSONResponse(
      content={
        "runId": record.run_id,
        "jobId": record.job_id,
        "state": record.state,
      }
    )

  @app.post("/api/package-jobs:cleanup")
  def cleanup_package_jobs(ttl_seconds: int | None = None) -> JSONResponse:
    retention = ttl_seconds if ttl_seconds is not None else resolved_config.export_job_retention_seconds
    removed = export_job_service.cleanup(retention)
    return JSONResponse(content={"removed": removed, "ttlSeconds": retention})

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

  @app.post("/api/jobs/{job_id}/layers:resolve")
  def resolve_layers(job_id: str, payload: LayerResolvePayload) -> JSONResponse:
    resolved_steps = layer_stack_resolver.resolve_steps(
      job_id=job_id,
      sublayer_paths_bottom_to_top=payload.sublayer_paths_bottom_to_top,
    )
    response = {
      "jobId": job_id,
      "resolvedSteps": [
        {
          "stepId": item.step_id,
          "sequenceIndex": item.sequence_index,
          "partId": item.part_id,
          "displayName": item.display_name,
          "sourcePrimPath": item.source_prim_path,
          "activePrimPath": item.active_prim_path,
          "animationName": item.animation_name,
          "animationLayerRole": item.animation_layer_role,
          "targetLayerRole": item.target_layer_role,
          "animationLayerId": item.animation_layer_id,
          "targetLayerId": item.target_layer_id,
          "animationLayerIndex": item.animation_layer_index,
          "targetLayerIndex": item.target_layer_index,
          "timelineStartStep": item.timeline_start_step,
          "timelineEndStep": item.timeline_end_step,
          "timelineFps": item.timeline_fps,
          "animationStartStep": item.animation_start_step,
          "animationEndStep": item.animation_end_step,
          "keepVisibleUntilStep": item.keep_visible_until_step,
          "handoverTargetLayerId": item.handover_target_layer_id,
          "handoverNextAnimationLayerId": item.handover_next_animation_layer_id,
          "visibleLayerIds": list(item.visible_layer_ids),
          "mutedLayerIds": list(item.muted_layer_ids),
          "cacheKey": item.cache_key,
        }
        for item in resolved_steps
      ],
    }
    logger.info("layers resolved", session_id="-", step_id="-", event="http.layers.resolve")
    return JSONResponse(content=response)

  return app


app = create_app()
