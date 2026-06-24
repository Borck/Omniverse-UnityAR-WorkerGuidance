"""gRPC control service: lets an external system drive which step a connected
device is on, without being the device's session client.

It does NOT open a Connect() stream. Instead it resolves the target step from the
step repository and pushes a StepActivated onto the per-session outbound queues
via SessionChannels -- the same queues the device's Connect() loop already drains.
So the device reacts exactly as if the step had advanced normally.

MVP scope: only GOTO (jump to an explicit step_id) is implemented. NEXT/PREVIOUS
need server-side current-step tracking and are intentionally rejected for now so
the contract is honest rather than silently wrong.
"""

from collections.abc import Iterable
from pathlib import Path
import sys

import grpc

sys.path.append(str(Path(__file__).resolve().parent / "generated"))

try:
    from .generated import guidance_pb2
    from .generated import guidance_pb2_grpc
    from .grpc_session_service import GuidanceSessionService
    from .logging_config import ContextAdapter
    from .session_channels import SessionChannels
    from .step_definition_repository import StepDefinition, StepDefinitionRepository
except ImportError:
    from generated import guidance_pb2
    from generated import guidance_pb2_grpc
    from grpc_session_service import GuidanceSessionService
    from logging_config import ContextAdapter
    from session_channels import SessionChannels
    from step_definition_repository import StepDefinition, StepDefinitionRepository


class GuidanceControlService(guidance_pb2_grpc.GuidanceControlServiceServicer):
    """Server implementation for external step control."""

    def __init__(
        self,
        session_channels: SessionChannels,
        step_repository: StepDefinitionRepository,
        logger: ContextAdapter,
    ) -> None:
        self._session_channels = session_channels
        self._step_repository = step_repository
        self._logger = logger

    def ControlStep(
        self,
        request: guidance_pb2.ControlStepRequest,
        context: grpc.ServicerContext,
    ) -> guidance_pb2.ControlStepResponse:
        job_id = request.job_id
        action = request.action

        self._logger.info(
            f"control step request action={action} job={job_id or '-'} step={request.step_id or '-'}",
            session_id="-",
            step_id=request.step_id or "-",
            event="grpc.control.request",
        )

        if action != guidance_pb2.CONTROL_ACTION_GOTO:
            # NEXT/PREVIOUS need per-session current-step tracking (not in MVP).
            msg = "only CONTROL_ACTION_GOTO is implemented; NEXT/PREVIOUS need current-step tracking"
            self._logger.warning(msg, session_id="-", step_id="-", event="grpc.control.unsupported")
            return guidance_pb2.ControlStepResponse(ok=False, message=msg)

        if not job_id or not request.step_id:
            return guidance_pb2.ControlStepResponse(
                ok=False, message="job_id and step_id are required for GOTO"
            )

        target = self._get_step(job_id, request.step_id)
        if target is None:
            msg = f"step '{request.step_id}' not found in job '{job_id}'"
            self._logger.warning(msg, session_id="-", step_id=request.step_id, event="grpc.control.not_found")
            return guidance_pb2.ControlStepResponse(ok=False, message=msg)

        # Reuse the session service's mapping so the pushed StepActivated is
        # identical to one produced by the normal client-driven flow.
        server_message = guidance_pb2.ServerMessage(
            step_activated=GuidanceSessionService._to_step_activated(target, job_id)
        )
        notified = self._session_channels.broadcast_to_job(job_id, server_message)

        self._logger.info(
            f"control step pushed step={target.step_id} sessions={notified}",
            session_id="-",
            step_id=target.step_id,
            event="grpc.control.pushed",
        )
        return guidance_pb2.ControlStepResponse(
            ok=True,
            activated_step_id=target.step_id,
            sessions_notified=notified,
            message=("pushed" if notified else "no active sessions on this job"),
        )

    def _get_step(self, job_id: str, step_id: str) -> StepDefinition | None:
        steps: Iterable[StepDefinition] = self._step_repository.get_steps(job_id)
        for step in steps:
            if step.step_id == step_id:
                return step
        return None
