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
"""

from __future__ import annotations

import queue
import threading


class SessionChannels:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._queues: dict[str, queue.Queue] = {}    # session_id -> outbound queue
        self._jobs: dict[str, str] = {}              # session_id -> active job_id

    def attach(self, session_id: str, job_id: str) -> queue.Queue:
        """Register a session's outbound channel. Returns the queue to drain."""
        with self._lock:
            q: queue.Queue = queue.Queue()
            self._queues[session_id] = q
            self._jobs[session_id] = job_id
        return q

    def update_job(self, session_id: str, job_id: str) -> None:
        """The session switched to a different job. Update routing."""
        with self._lock:
            if session_id in self._jobs:
                self._jobs[session_id] = job_id

    def detach(self, session_id: str) -> None:
        """Stream closed. Drop the session's channel."""
        with self._lock:
            self._queues.pop(session_id, None)
            self._jobs.pop(session_id, None)

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
