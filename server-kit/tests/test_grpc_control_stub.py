"""Tests for the external GuidanceControlService (GOTO step control)."""

from pathlib import Path
from concurrent import futures
import queue
import socket
import sys

import grpc
import pytest

sys.path.append(str(Path(__file__).resolve().parents[1]))
sys.path.append(str(Path(__file__).resolve().parents[1] / "app" / "generated"))

from app.generated import guidance_pb2
from app.generated import guidance_pb2_grpc
from app.grpc_control_service import GuidanceControlService
from app.grpc_session_service import GuidanceSessionService
from app.guidance_server import SessionManager
from app.logging_config import configure_logging
from app.session_channels import SessionChannels
from app.step_definition_repository import StepDefinitionRepository

JOB_ID = "test-control-job"

# Minimal step-definitions YAML with the fields StepDefinitionRepository requires.
_STEPS_YAML = """
jobs:
  - jobId: test-control-job
    timelineProfile:
      startStep: 0
      endStep: 300
      fps: 30
    steps:
      - stepId: step-001
        partId: PART_A
        displayName: First
        sourcePrimPath: /World
        animationName: anim_a
        anchorType: model-target
        targetId: tgt
        targetVersion: v1
        assetVersion: sha256_aaa
        instructionsShort: Do A
        sequenceIndex: 1
      - stepId: step-002
        partId: PART_B
        displayName: Second
        sourcePrimPath: /World
        animationName: anim_b
        anchorType: model-target
        targetId: tgt
        targetVersion: v1
        assetVersion: sha256_bbb
        instructionsShort: Do B
        sequenceIndex: 2
      - stepId: step-003
        partId: PART_C
        displayName: Third
        sourcePrimPath: /World
        animationName: anim_c
        anchorType: model-target
        targetId: tgt
        targetVersion: v1
        assetVersion: sha256_ccc
        instructionsShort: Do C
        sequenceIndex: 3
"""


def _get_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def _repo(tmp_path: Path) -> StepDefinitionRepository:
    yaml_path = tmp_path / "step-definitions.yaml"
    yaml_path.write_text(_STEPS_YAML, encoding="utf-8")
    return StepDefinitionRepository(step_definition_file=yaml_path)


def _service(tmp_path: Path, channels: SessionChannels) -> GuidanceControlService:
    return GuidanceControlService(
        session_channels=channels,
        step_repository=_repo(tmp_path),
        logger=configure_logging("INFO"),
    )


# ── Direct servicer tests (no network) ──────────────────────────────────────

def test_goto_resolves_step_and_pushes_to_attached_session(tmp_path: Path) -> None:
    channels = SessionChannels()
    outbound = channels.attach("sess-1", JOB_ID)  # simulate a connected device
    service = _service(tmp_path, channels)

    resp = service.ControlStep(
        guidance_pb2.ControlStepRequest(
            job_id=JOB_ID,
            action=guidance_pb2.CONTROL_ACTION_GOTO,
            step_id="step-002",
        ),
        context=None,
    )

    assert resp.ok is True
    assert resp.activated_step_id == "step-002"
    assert resp.sessions_notified == 1

    # The device's queue actually received the StepActivated push.
    pushed = outbound.get_nowait()
    assert pushed.HasField("step_activated")
    assert pushed.step_activated.step_id == "step-002"
    assert pushed.step_activated.part_id == "PART_B"
    assert pushed.step_activated.job_id == JOB_ID


def test_goto_unknown_step_returns_not_ok(tmp_path: Path) -> None:
    channels = SessionChannels()
    channels.attach("sess-1", JOB_ID)
    service = _service(tmp_path, channels)

    resp = service.ControlStep(
        guidance_pb2.ControlStepRequest(
            job_id=JOB_ID,
            action=guidance_pb2.CONTROL_ACTION_GOTO,
            step_id="step-999",
        ),
        context=None,
    )

    assert resp.ok is False
    assert "not found" in resp.message
    assert resp.sessions_notified == 0


def test_goto_with_no_sessions_is_ok_but_notifies_zero(tmp_path: Path) -> None:
    channels = SessionChannels()  # nobody attached
    service = _service(tmp_path, channels)

    resp = service.ControlStep(
        guidance_pb2.ControlStepRequest(
            job_id=JOB_ID,
            action=guidance_pb2.CONTROL_ACTION_GOTO,
            step_id="step-001",
        ),
        context=None,
    )

    assert resp.ok is True
    assert resp.sessions_notified == 0


def test_next_action_is_rejected_in_mvp(tmp_path: Path) -> None:
    service = _service(tmp_path, SessionChannels())

    resp = service.ControlStep(
        guidance_pb2.ControlStepRequest(
            job_id=JOB_ID,
            action=guidance_pb2.CONTROL_ACTION_NEXT,
        ),
        context=None,
    )

    assert resp.ok is False
    assert "GOTO" in resp.message


# ── End-to-end test: external control drives a live device stream ────────────

def test_control_goto_reaches_live_session_stream(tmp_path: Path) -> None:
    """A device opens Connect(); an external control client GOTOs a step; the
    device receives the StepActivated over its existing stream."""
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=8))
    logger = configure_logging("INFO")

    # Shared push fabric — both services use the SAME instance (as in production).
    channels = SessionChannels()
    repo = _repo(tmp_path)

    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(
        GuidanceSessionService(
            session_manager=SessionManager(),
            logger=logger,
            step_repository=repo,
            default_job_id=JOB_ID,
            session_channels=channels,
        ),
        server,
    )
    guidance_pb2_grpc.add_GuidanceControlServiceServicer_to_server(
        GuidanceControlService(session_channels=channels, step_repository=repo, logger=logger),
        server,
    )
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    # Device inbound messages are fed through a queue so we can interleave the
    # control call between the hello and a later heartbeat (which flushes pushes).
    inbound: queue.Queue = queue.Queue()

    def device_messages():
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(device_id="vuzix-1", app_version="1.0", capabilities="")
        )
        while True:
            item = inbound.get()
            if item is None:
                return
            yield item

    try:
        with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
            session_stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
            control_stub = guidance_pb2_grpc.GuidanceControlServiceStub(channel)

            responses = session_stub.Connect(device_messages(), timeout=15)
            assert next(responses).HasField("hello_response")
            first_step = next(responses)
            assert first_step.HasField("step_activated")
            assert first_step.step_activated.step_id == "step-001"  # auto first step

            # External system jumps the device to step-003.
            ctrl = control_stub.ControlStep(
                guidance_pb2.ControlStepRequest(
                    job_id=JOB_ID,
                    action=guidance_pb2.CONTROL_ACTION_GOTO,
                    step_id="step-003",
                ),
                timeout=15,
            )
            assert ctrl.ok is True
            assert ctrl.sessions_notified == 1

            # Flush: any client message makes Connect() drain the push queue.
            inbound.put(
                guidance_pb2.ClientMessage(
                    heartbeat=guidance_pb2.Heartbeat(session_id="vuzix-1", client_time_unix_ms=1)
                )
            )
            assert next(responses).HasField("ping")              # heartbeat reply
            pushed = next(responses)                              # drained control push
            assert pushed.HasField("step_activated")
            assert pushed.step_activated.step_id == "step-003"
            assert pushed.step_activated.part_id == "PART_C"

            inbound.put(None)  # end the device stream cleanly
    finally:
        server.stop(grace=0)
