from pathlib import Path
from concurrent import futures
import socket
import sys

import grpc
import pytest

sys.path.append(str(Path(__file__).resolve().parents[1]))
sys.path.append(str(Path(__file__).resolve().parents[1] / "app" / "generated"))

from app.generated import guidance_pb2
from app.generated import guidance_pb2_grpc
from app.grpc_session_service import GuidanceSessionService
from app.guidance_server import SessionManager
from app.logging_config import configure_logging
from app.step_definition_repository import StepDefinitionRepository


def _get_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def test_grpc_connect_stream_emits_hello_step_and_ping() -> None:
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    logger = configure_logging("INFO")
    service = GuidanceSessionService(session_manager=SessionManager(), logger=logger)
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(service, server)
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    def request_stream() -> guidance_pb2.ClientMessage:
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )
        yield guidance_pb2.ClientMessage(
            heartbeat=guidance_pb2.Heartbeat(
                session_id="session-1",
                client_time_unix_ms=123,
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        responses = stub.Connect(request_stream())

        first = next(responses)
        second = next(responses)
        third = next(responses)

        assert first.HasField("hello_response")
        assert second.HasField("step_activated")
        assert third.HasField("ping")
        assert third.ping.nonce == "hb-123"

    server.stop(grace=0)


def test_grpc_connect_stream_ignores_duplicate_hello() -> None:
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    logger = configure_logging("INFO")
    service = GuidanceSessionService(session_manager=SessionManager(), logger=logger)
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(service, server)
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    def request_stream() -> guidance_pb2.ClientMessage:
        hello = guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )
        yield hello
        yield hello
        yield guidance_pb2.ClientMessage(
            heartbeat=guidance_pb2.Heartbeat(
                session_id="session-1",
                client_time_unix_ms=124,
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        responses = list(stub.Connect(request_stream()))

        assert len(responses) == 3
        assert responses[0].HasField("hello_response")
        assert responses[0].hello_response.session_id == "session-1"
        assert responses[1].HasField("step_activated")
        assert responses[2].HasField("ping")
        assert responses[2].ping.nonce == "hb-124"

    server.stop(grace=0)


def test_grpc_reconnect_reuses_session_for_same_device() -> None:
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    logger = configure_logging("INFO")
    service = GuidanceSessionService(session_manager=SessionManager(), logger=logger)
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(service, server)
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    def hello_stream() -> guidance_pb2.ClientMessage:
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-reconnect-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        first_connect_responses = list(stub.Connect(hello_stream()))
        second_connect_responses = list(stub.Connect(hello_stream()))

        assert first_connect_responses[0].HasField("hello_response")
        assert second_connect_responses[0].HasField("hello_response")
        assert (
            first_connect_responses[0].hello_response.session_id
            == second_connect_responses[0].hello_response.session_id
        )

    server.stop(grace=0)


def test_grpc_restart_preserves_session_resume(tmp_path: Path) -> None:
    store_file = tmp_path / "runtime" / "sessions.json"

    first_port = _get_free_port()
    first_server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))
    logger = configure_logging("INFO")
    first_service = GuidanceSessionService(
        session_manager=SessionManager(store_file=store_file),
        logger=logger,
    )
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(first_service, first_server)
    first_server.add_insecure_port(f"127.0.0.1:{first_port}")
    first_server.start()

    def hello_stream() -> guidance_pb2.ClientMessage:
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-restart-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{first_port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        first_session_id = list(stub.Connect(hello_stream()))[0].hello_response.session_id

    first_server.stop(grace=0)

    second_port = _get_free_port()
    second_server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))
    second_service = GuidanceSessionService(
        session_manager=SessionManager(store_file=store_file),
        logger=logger,
    )
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(second_service, second_server)
    second_server.add_insecure_port(f"127.0.0.1:{second_port}")
    second_server.start()

    with grpc.insecure_channel(f"127.0.0.1:{second_port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        resumed_session_id = list(stub.Connect(hello_stream()))[0].hello_response.session_id

    second_server.stop(grace=0)

    assert resumed_session_id == first_session_id


def test_grpc_fault_log_contains_correlation_id(capfd: pytest.CaptureFixture[str]) -> None:
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    logger = configure_logging("INFO")
    service = GuidanceSessionService(session_manager=SessionManager(), logger=logger)
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(service, server)
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    def request_stream() -> guidance_pb2.ClientMessage:
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-fault-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )
        yield guidance_pb2.ClientMessage(
            fault=guidance_pb2.Fault(
                code="CLIENT_TEST",
                message="fault for correlation test",
                correlation_id="corr-123",
                recoverable=True,
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        responses = list(stub.Connect(request_stream()))
        assert responses[0].HasField("hello_response")
        assert responses[1].HasField("step_activated")

    server.stop(grace=0)

    stderr = capfd.readouterr().err
    assert '"event": "grpc.client.fault"' in stderr
    assert '"correlation_id": "corr-123"' in stderr


def test_grpc_step_completed_emits_next_step_from_repository() -> None:
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    logger = configure_logging("INFO")
    step_repository = StepDefinitionRepository(
        step_definition_file=Path("shared/samples/step-definitions.yaml")
    )
    service = GuidanceSessionService(
        session_manager=SessionManager(),
        logger=logger,
        step_repository=step_repository,
        default_job_id="job-layer-example-001",
    )
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(service, server)
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    def request_stream() -> guidance_pb2.ClientMessage:
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-next-step-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )
        yield guidance_pb2.ClientMessage(
            step_completed=guidance_pb2.StepCompleted(
                job_id="job-layer-example-001",
                step_id="10",
                completed_at_unix_ms=1000,
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        responses = list(stub.Connect(request_stream()))

    server.stop(grace=0)

    assert len(responses) == 3
    assert responses[0].HasField("hello_response")
    assert responses[1].HasField("step_activated")
    assert responses[1].step_activated.step_id == "10"
    assert responses[1].step_activated.part_id == "PART_A"
    assert responses[2].HasField("step_activated")
    assert responses[2].step_activated.step_id == "20"
    assert responses[2].step_activated.part_id == "PART_B"


def test_grpc_step_progression_stops_after_last_sequence_step() -> None:
    port = _get_free_port()
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    logger = configure_logging("INFO")
    step_repository = StepDefinitionRepository(
        step_definition_file=Path("shared/samples/step-definitions.yaml")
    )
    service = GuidanceSessionService(
        session_manager=SessionManager(),
        logger=logger,
        step_repository=step_repository,
        default_job_id="job-layer-example-001",
    )
    guidance_pb2_grpc.add_GuidanceSessionServiceServicer_to_server(service, server)
    server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()

    def request_stream() -> guidance_pb2.ClientMessage:
        yield guidance_pb2.ClientMessage(
            hello=guidance_pb2.HelloRequest(
                device_id="device-sequence-end-1",
                app_version="0.1.0",
                capabilities="mock",
            )
        )
        yield guidance_pb2.ClientMessage(
            step_completed=guidance_pb2.StepCompleted(
                job_id="job-layer-example-001",
                step_id="10",
                completed_at_unix_ms=1000,
            )
        )
        yield guidance_pb2.ClientMessage(
            step_completed=guidance_pb2.StepCompleted(
                job_id="job-layer-example-001",
                step_id="20",
                completed_at_unix_ms=2000,
            )
        )
        yield guidance_pb2.ClientMessage(
            step_completed=guidance_pb2.StepCompleted(
                job_id="job-layer-example-001",
                step_id="30",
                completed_at_unix_ms=3000,
            )
        )
        yield guidance_pb2.ClientMessage(
            step_completed=guidance_pb2.StepCompleted(
                job_id="job-layer-example-001",
                step_id="40",
                completed_at_unix_ms=4000,
            )
        )
        # No step should be emitted after the final sequence item is completed.
        yield guidance_pb2.ClientMessage(
            step_completed=guidance_pb2.StepCompleted(
                job_id="job-layer-example-001",
                step_id="50",
                completed_at_unix_ms=5000,
            )
        )

    with grpc.insecure_channel(f"127.0.0.1:{port}") as channel:
        stub = guidance_pb2_grpc.GuidanceSessionServiceStub(channel)
        responses = list(stub.Connect(request_stream()))

    server.stop(grace=0)

    assert responses[0].HasField("hello_response")
    activated_steps = [
        message.step_activated.step_id
        for message in responses
        if message.HasField("step_activated")
    ]
    assert activated_steps == ["10", "20", "30", "40", "50"]
