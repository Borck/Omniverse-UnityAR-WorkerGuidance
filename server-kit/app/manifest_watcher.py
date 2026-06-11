"""Polls manifest files on disk; broadcasts ManifestUpdated on content change.

Sits between the live-sync pipeline (which writes <job_id>.manifest.json on
each rebuild) and the gRPC server (which holds open client sessions). On each
poll, diffs the on-disk manifest's stepId -> assetVersion map against the
last-seen snapshot per job. For every step whose assetVersion changed, builds
a ManifestUpdated message with changed_steps populated and pushes it through
SessionChannels to any connected client subscribed to that job.

Steps whose hash is unchanged are NOT included in changed_steps, so a no-op
rebuild (identical bytes -> identical hashes -> identical manifest) produces
no broadcast at all.
"""

from __future__ import annotations

import json
import logging
import threading
from pathlib import Path

try:
    from .generated import guidance_pb2
except ImportError:
    from generated import guidance_pb2


class ManifestWatcher:
    def __init__(
        self,
        manifests_dir: Path,
        channels,
        logger: logging.Logger,
        poll_interval_sec: float = 2.0,
    ) -> None:
        self._manifests_dir = manifests_dir
        self._channels = channels
        self._logger = logger
        self._poll = poll_interval_sec
        # job_id -> (workflow_version, {step_id -> asset_version})
        self._last_known: dict[str, tuple[str, dict[str, str]]] = {}
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        # Seed last-known from current state so the first poll doesn't fire
        # ManifestUpdated for every step that was already on disk at startup.
        seeded = 0
        for path in sorted(self._manifests_dir.glob("*.manifest.json")):
            data = self._read_manifest(path)
            if data:
                job_id, workflow_version, step_map = data
                self._last_known[job_id] = (workflow_version, step_map)
                seeded += 1
        self._logger.info(
            "manifest_watcher seeded job_count=%d poll_interval_sec=%.1f",
            seeded,
            self._poll,
        )
        self._thread = threading.Thread(
            target=self._run, daemon=True, name="ManifestWatcher"
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=5.0)

    def _run(self) -> None:
        while not self._stop.is_set():
            try:
                self._poll_once()
            except Exception:
                self._logger.exception("manifest_watcher poll failed")
            # Sleep with stop awareness so .stop() returns promptly.
            self._stop.wait(self._poll)

    def _poll_once(self) -> None:
        for path in self._manifests_dir.glob("*.manifest.json"):
            data = self._read_manifest(path)
            if not data:
                continue
            job_id, workflow_version, step_map = data
            prev_version, prev_steps = self._last_known.get(job_id, ("", {}))

            changed = {
                sid: ver
                for sid, ver in step_map.items()
                if prev_steps.get(sid) != ver
            }
            if not changed and prev_version == workflow_version:
                continue  # nothing to push

            msg = guidance_pb2.ServerMessage(
                manifest_updated=guidance_pb2.ManifestUpdated(
                    job_id=job_id,
                    new_workflow_version=workflow_version,
                    changed_steps=changed,
                )
            )
            count = self._channels.broadcast_to_job(job_id, msg)
            self._logger.info(
                "manifest_updated job=%s changed_steps=%d sessions_notified=%d",
                job_id,
                len(changed),
                count,
            )
            self._last_known[job_id] = (workflow_version, step_map)

    def _read_manifest(self, path: Path) -> tuple[str, str, dict[str, str]] | None:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            # Either mid-write or corrupt; will retry next poll.
            return None
        try:
            job_id = data["jobId"]
            workflow_version = data.get("workflowVersion", "")
            steps = {s["stepId"]: s["assetVersion"] for s in data["steps"]}
        except (KeyError, TypeError):
            return None
        return job_id, workflow_version, steps
