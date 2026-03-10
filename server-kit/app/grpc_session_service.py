from collections.abc import Iterator
from concurrent import futures
from pathlib import Path
import sys

import grpc

sys.path.append(str(Path(__file__).resolve().parent / "generated"))

try:
    from .generated import guidance_pb2
    from .generated import guidance_pb2_grpc
    from .guidance_server import SessionManager, SessionState
    from .logging_config import ContextAdapter
    from .step_definition_repository import StepDefinition, StepDefinitionRepository
except ImportError:
    from generated import guidance_pb2
    from generated import guidance_pb2_grpc
    from guidance_server import SessionManager, SessionState
    from logging_config import ContextAdapter
    from step_definition_repository import StepDefinition, StepDefinitionRepository


class GuidanceSessionService(guidance_pb2_grpc.GuidanceSessionServiceServicer):
    def __init__(
        self,
        session_manager: SessionManager,
        logger: ContextAdapter,
        step_repository: StepDefinitionRepository | None = None,
        default_job_id: str = "job-mock-001",
    ) -> None:
        self._session_manager = session_manager
        self._logger = logger
        self._step_repository = step_repository
        self._default_job_id = default_job_id

    def _set_session_state_with_log(self, session_id: str, next_state: SessionState, reason: str, step_id: str = "-") -> None:
        previous = self._session_manager.get(session_id)
        previous_state = previous.state.value if previous is not None else "Unknown"
        self._session_manager.set_state(session_id, next_state)
        self._logger.info(
            f"session state transition {previous_state} -> {next_state.value} ({reason})",
            session_id=session_id,
            step_id=step_id,
            event="grpc.session.state.transition",
            correlation_id=reason,
        )

    def Connect(
        self,
        request_iterator: Iterator[guidance_pb2.ClientMessage],
        context: grpc.ServicerContext,
    ) -> Iterator[guidance_pb2.ServerMessage]:
        session_id = ""
        active_job_id = self._default_job_id
        handshake_done = False
        processed_step_completions: set[tuple[str, str, int]] = set()

        for message in request_iterator:
            payload_name = message.WhichOneof("payload")

            if payload_name == "hello":
                if handshake_done:
                    self._logger.info(
                        "duplicate hello ignored",
                        session_id=session_id or "-",
                        step_id="-",
                    )
                    continue

                device_id = message.hello.device_id or "unknown-device"
                session_id, resumed = self._session_manager.register_or_resume_session(device_id)
                self._set_session_state_with_log(session_id, SessionState.IDLE, reason="hello")
                handshake_done = True
                self._logger.info(
                    f"session connected ({'resumed' if resumed else 'new'})",
                    session_id=session_id,
                    step_id="-",
                )

                yield guidance_pb2.ServerMessage(
                    hello_response=guidance_pb2.HelloResponse(
                        session_id=session_id,
                        protocol_version="v1",
                        server_time_unix_ms=0,
                    )
                )

                first_step = self._get_first_step(active_job_id)
                if first_step is not None:
                    self._set_session_state_with_log(
                        session_id,
                        SessionState.STEP_READY,
                        reason="first-step-activated",
                        step_id=first_step.step_id,
                    )
                    yield guidance_pb2.ServerMessage(step_activated=self._to_step_activated(first_step, active_job_id))
                else:
                    # Backward-compatible fallback for tests/flows without configured step repository.
                    self._set_session_state_with_log(session_id, SessionState.STEP_READY, reason="mock-step-activated", step_id="17")
                    yield guidance_pb2.ServerMessage(step_activated=self._mock_step_activated())

            elif payload_name == "heartbeat" and handshake_done:
                nonce = f"hb-{message.heartbeat.client_time_unix_ms}"
                self._logger.info("heartbeat", session_id=session_id, step_id="-")
                yield guidance_pb2.ServerMessage(ping=guidance_pb2.Ping(nonce=nonce))

            elif payload_name == "step_completed" and handshake_done:
                completed_key = (
                    message.step_completed.job_id,
                    message.step_completed.step_id,
                    message.step_completed.completed_at_unix_ms,
                )
                if completed_key in processed_step_completions:
                    self._logger.info(
                        "duplicate step completion ignored",
                        session_id=session_id,
                        step_id=message.step_completed.step_id,
                    )
                    continue

                processed_step_completions.add(completed_key)
                self._set_session_state_with_log(
                    session_id,
                    SessionState.IDLE,
                    reason="step-completed",
                    step_id=message.step_completed.step_id,
                )
                self._logger.info(
                    "step completed",
                    session_id=session_id,
                    step_id=message.step_completed.step_id,
                )

                completed_job_id = message.step_completed.job_id or active_job_id
                next_step = self._get_next_step(completed_job_id, message.step_completed.step_id)
                if next_step is not None:
                    active_job_id = completed_job_id
                    self._set_session_state_with_log(
                        session_id,
                        SessionState.STEP_READY,
                        reason="next-step-activated",
                        step_id=next_step.step_id,
                    )
                    yield guidance_pb2.ServerMessage(
                        step_activated=self._to_step_activated(next_step, completed_job_id)
                    )

            elif payload_name == "fault":
                self._logger.warning(
                    "client fault code=%s message=%s",
                    message.fault.code,
                    message.fault.message,
                    session_id=session_id or "-",
                    step_id="-",
                    event="grpc.client.fault",
                    correlation_id=message.fault.correlation_id or "-",
                )

        self._logger.info("session stream closed", session_id=session_id or "-", step_id="-")

    def _get_steps(self, job_id: str) -> list[StepDefinition]:
        if self._step_repository is None:
            return []
        steps = self._step_repository.get_steps(job_id)
        return sorted(steps, key=lambda s: (s.sequence_index, int(s.step_id) if s.step_id.isdigit() else 0, s.step_id))

    def _get_first_step(self, job_id: str) -> StepDefinition | None:
        steps = self._get_steps(job_id)
        return steps[0] if steps else None

    def _get_next_step(self, job_id: str, completed_step_id: str) -> StepDefinition | None:
        steps = self._get_steps(job_id)
        if not steps:
            return None
        for index, step in enumerate(steps):
            if step.step_id == completed_step_id:
                if index + 1 < len(steps):
                    return steps[index + 1]
                return None
        return None

    @staticmethod
    def _to_step_activated(step: StepDefinition, job_id: str) -> guidance_pb2.StepActivated:
        return guidance_pb2.StepActivated(
            job_id=job_id,
            step_id=step.step_id,
            part_id=step.part_id,
            display_name=step.display_name,
            instructions_short=step.instructions_short,
            safety_notes=step.safety_notes,
            asset_version=step.asset_version,
            target_id=step.target_id,
            target_version=step.target_version,
            anchor_type=step.anchor_type,
            animation_name=step.animation_name,
            prefetch_next_step_id="",
        )

    @staticmethod
    def _mock_step_activated() -> guidance_pb2.StepActivated:
        return guidance_pb2.StepActivated(
            job_id="job-mock-001",
            step_id="17",
            part_id="Bracket_12",
            display_name="Install bracket",
            instructions_short="Align and insert along highlighted path.",
            safety_notes=["Check cable clearance"],
            asset_version="sha256:mock-asset",
            target_id="AssemblyMarker_A",
            target_version="2026-03-10.1",
            anchor_type="ImageTarget",
            animation_name="Insert_17",
            prefetch_next_step_id="18",
        )


def serve_grpc(
    host: str,
    port: int,
    logger: ContextAdapter,
    step_repository: StepDefinitionRepository | None = None,
    default_job_id: str = "job-mock-001",
) -> None:
    session_manager = SessionManager()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=8))
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(
        GuidanceSessionService(
            session_manager=session_manager,
            logger=logger,
            step_repository=step_repository,
            default_job_id=default_job_id,
        ),
        server,
    )
    server.add_insecure_port(f"{host}:{port}")
    server.start()
    logger.info("grpc service started", session_id="-", step_id="-")
    server.wait_for_termination()


def run_grpc_server(
    host: str,
    port: int,
    logger: ContextAdapter,
    step_repository: StepDefinitionRepository | None = None,
    default_job_id: str = "job-mock-001",
) -> None:
    serve_grpc(host, port, logger, step_repository=step_repository, default_job_id=default_job_id)
