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
except ImportError:
    from generated import guidance_pb2
    from generated import guidance_pb2_grpc
    from guidance_server import SessionManager, SessionState
    from logging_config import ContextAdapter


class GuidanceSessionService(guidance_pb2_grpc.GuidanceSessionServiceServicer):
    def __init__(self, session_manager: SessionManager, logger: ContextAdapter) -> None:
        self._session_manager = session_manager
        self._logger = logger

    def Connect(
        self,
        request_iterator: Iterator[guidance_pb2.ClientMessage],
        context: grpc.ServicerContext,
    ) -> Iterator[guidance_pb2.ServerMessage]:
        session_id = ""
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
                self._session_manager.set_state(session_id, SessionState.IDLE)
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

                # Emit one mock step for vertical-slice development.
                self._session_manager.set_state(session_id, SessionState.STEP_READY)
                yield guidance_pb2.ServerMessage(
                    step_activated=guidance_pb2.StepActivated(
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
                )

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
                self._session_manager.set_state(session_id, SessionState.IDLE)
                self._logger.info(
                    "step completed",
                    session_id=session_id,
                    step_id=message.step_completed.step_id,
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


def serve_grpc(host: str, port: int, logger: ContextAdapter) -> None:
    session_manager = SessionManager()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=8))
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(
        GuidanceSessionService(session_manager=session_manager, logger=logger),
        server,
    )
    server.add_insecure_port(f"{host}:{port}")
    server.start()
    logger.info("grpc service started", session_id="-", step_id="-")
    server.wait_for_termination()


def run_grpc_server(host: str, port: int, logger: ContextAdapter) -> None:
    serve_grpc(host, port, logger)
