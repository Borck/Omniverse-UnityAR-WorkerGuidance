from pathlib import Path
from concurrent import futures
import socket
import sys

import grpc

sys.path.append(str(Path(__file__).resolve().parents[1]))
sys.path.append(str(Path(__file__).resolve().parents[1] / "app" / "generated"))

from app.generated import guidance_pb2
from app.generated import guidance_pb2_grpc
from app.grpc_session_service import GuidanceSessionService
from app.guidance_server import SessionManager
from app.logging_config import configure_logging


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
