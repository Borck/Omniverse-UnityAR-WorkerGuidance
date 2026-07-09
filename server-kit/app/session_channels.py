"""Per-session outbound push channels for the gRPC session stream.

The Connect() bidi stream is fundamentally a request-driven generator: it yields
responses in reaction to client messages. To inject server-driven events
(notably ManifestUpdated from the live-sync pipeline), each session gets a
thread-safe outbound queue. External producers call broadcast_to_job() to fan
a ServerMessage out to every session active on that job; Connect() drains its
queue at the end of each iteration and yields the items to the client.

Latency: pushes are delivered the next time the client sends ANY message
(heartbeats fire every ~5s), so worst-case delivery is on the order of one
heartbeat interval. For a research-grade live-sync demo this is acceptable;
sub-second delivery would require an inbound-thread refactor of Connect().

Also tracks each session's grpc.ServicerContext (so an admin action can force
a disconnect via context.cancel()) and last-heartbeat timestamp (so a
dashboard can tell "attached" apart from "actually alive").
"""

from __future__ import annotations

import queue
import threading
import time


class SessionChannels:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._queues: dict[str, queue.Queue] = {}       # session_id -> outbound queue
        self._jobs: dict[str, str] = {}                 # session_id -> active job_id
        self._contexts: dict[str, object] = {}           # session_id -> grpc.ServicerContext
        self._last_heartbeat_ms: dict[str, int] = {}     # session_id -> unix ms
        self._current_step: dict[str, str] = {}          # session_id -> step_id

    def attach(self, session_id: str, job_id: str, context: object | None = None) -> queue.Queue:
        """Register a session's outbound channel. Returns the queue to drain."""
        with self._lock:
            q: queue.Queue = queue.Queue()
            self._queues[session_id] = q
            self._jobs[session_id] = job_id
            if context is not None:
                self._contexts[session_id] = context
        return q

    def update_job(self, session_id: str, job_id: str) -> None:
        """The session switched to a different job. Update routing."""
        with self._lock:
            if session_id in self._jobs:
                self._jobs[session_id] = job_id

    def touch_heartbeat(self, session_id: str) -> None:
        with self._lock:
            if session_id in self._queues:
                self._last_heartbeat_ms[session_id] = int(time.time() * 1000)

    def get_context(self, session_id: str) -> object | None:
        with self._lock:
            return self._contexts.get(session_id)

    def set_current_step(self, session_id: str, step_id: str) -> None:
        """Record the step_id of the last StepActivated sent to this session."""
        with self._lock:
            if session_id in self._queues:
                self._current_step[session_id] = step_id

    def session_ids_for_job(self, job_id: str) -> list[str]:
        with self._lock:
            return [sid for sid, sj in self._jobs.items() if sj == job_id]

    def detach(self, session_id: str) -> None:
        """Stream closed. Drop the session's channel."""
        with self._lock:
            self._queues.pop(session_id, None)
            self._jobs.pop(session_id, None)
            self._contexts.pop(session_id, None)
            self._last_heartbeat_ms.pop(session_id, None)
            self._current_step.pop(session_id, None)

    def broadcast_to_job(self, job_id: str, message) -> int:
        """Push message to every session currently active on this job.

        Returns the number of sessions targeted. Failures from put_nowait are
        swallowed (an unbounded queue should not raise, but be defensive).
        """
        with self._lock:
            target_sessions = [sid for sid, sj in self._jobs.items() if sj == job_id]
            queues_to_push = [self._queues[sid] for sid in target_sessions if sid in self._queues]
        for q in queues_to_push:
            try:
                q.put_nowait(message)
            except queue.Full:
                pass
        return len(queues_to_push)

    def session_count(self) -> int:
        with self._lock:
            return len(self._queues)

    def snapshot(self) -> list[tuple[str, str, int, str]]:
        """(session_id, job_id, last_heartbeat_unix_ms, current_step_id) per attached session.

        last_heartbeat_unix_ms is 0 if no heartbeat has arrived since attach.
        current_step_id is "" until the first StepActivated is sent.
        """
        with self._lock:
            return [
                (sid, job_id, self._last_heartbeat_ms.get(sid, 0), self._current_step.get(sid, ""))
                for sid, job_id in self._jobs.items()
            ]
