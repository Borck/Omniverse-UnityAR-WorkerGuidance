"""Structured JSON logging adapter used by HTTP and gRPC services.""" # No change needed here, this file is moved.

import logging
import json
from typing import Any


class ContextAdapter(logging.LoggerAdapter):
    """Injects session/step/correlation context fields into log records."""

    def process(self, msg: str, kwargs: dict[str, Any]) -> tuple[str, dict[str, Any]]:
        session_id = kwargs.pop("session_id", "-")
        step_id = kwargs.pop("step_id", "-")
        event = kwargs.pop("event", "log")
        correlation_id = kwargs.pop("correlation_id", "-")
        kwargs.setdefault(
            "extra",
            {
                "session_id": session_id,
                "step_id": step_id,
                "event": event,
                "correlation_id": correlation_id,
            },
        )
        return msg, kwargs


class JsonFormatter(logging.Formatter):
    """Formats log records into stable JSON payloads."""

    def format(self, record: logging.LogRecord) -> str:
        payload = {
            "timestamp": self.formatTime(record, "%Y-%m-%dT%H:%M:%S"),
            "level": record.levelname,
            "logger": record.name,
            "event": getattr(record, "event", "log"),
            "message": record.getMessage(),
            "session_id": getattr(record, "session_id", "-"),
            "step_id": getattr(record, "step_id", "-"),
            "correlation_id": getattr(record, "correlation_id", "-"),
        }
        return json.dumps(payload, ensure_ascii=True)


def configure_logging(level: str) -> ContextAdapter:
    logger = logging.getLogger("Guidance-Logger") # name it
    logger.setLevel(getattr(logging, level, logging.INFO)) # set a level based on env var
    logger.handlers.clear() # clear any prev so no duplicates
    handler = logging.StreamHandler()
    handler.setFormatter(JsonFormatter())
    logger.addHandler(handler)
    logger.propagate = False
    return ContextAdapter(logger, extra={})
