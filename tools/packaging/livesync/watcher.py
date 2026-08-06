"""Poll Nucleus for changes to the master USD (and its parts) and trigger a
rebuild when something changes.

Runs forever. Designed to be launched once at boot via run_watcher.bat and left
alone. Uses omni.client.stat() to ask Nucleus for the modified-time of each
watched path; this requires Kit's bundled Python (run_watcher.bat handles that).

State (last-seen mtimes) is persisted to disk so a restart doesn't trigger a
spurious rebuild for files that haven't actually changed.
"""

from __future__ import annotations

import json
import logging
import os
import subprocess
import sys
import time
from datetime import datetime
from logging.handlers import RotatingFileHandler
from pathlib import Path

import yaml


SCRIPT_DIR = Path(__file__).resolve().parent
CONFIG_PATH = SCRIPT_DIR / "livesync.config.yaml"
PIPELINE_RUNNER = SCRIPT_DIR / "pipeline_runner.py"


def load_config() -> dict:
    if not CONFIG_PATH.exists():
        sys.exit(
            f"Missing config: {CONFIG_PATH}\n"
            f"Copy livesync.config.example.yaml to livesync.config.yaml and edit it."
        )
    return yaml.safe_load(CONFIG_PATH.read_text(encoding="utf-8"))


def setup_logging(log_dir: Path) -> logging.Logger:
    log_dir.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger("livesync")
    logger.setLevel(logging.INFO)
    fmt = logging.Formatter("%(asctime)s %(levelname)s %(message)s", "%Y-%m-%d %H:%M:%S")

    fh = RotatingFileHandler(log_dir / "watcher.log", maxBytes=2_000_000, backupCount=5, encoding="utf-8")
    fh.setFormatter(fmt)
    logger.addHandler(fh)

    sh = logging.StreamHandler()
    sh.setFormatter(fmt)
    logger.addHandler(sh)
    return logger


def _enc(url: str) -> str:
    return url.replace(" ", "%20")


def _usd_name(step_id: str) -> str:
    # Files on Nucleus are "step_id_<major>_<minor>.usd" (no "_Animation" suffix).
    return f"step_id_{step_id.replace('.', '_')}.usd"


def resolve_watch_paths(cfg: dict, logger: logging.Logger) -> list[str]:
    """Return the Nucleus URLs to watch.

    If `watch_paths` is set in the config it wins (backward compatible).
    Otherwise the list is derived from assembly_definition.json:
      - the JSON itself (edits re-trigger a rebuild),
      - the model_target .dat/.xml (target updates re-trigger),
      - every `is_animation` step's step_id_<M>_<N>.usd.
    All resolved from nucleus_job_root by convention, matching the exporter.
    """
    explicit = cfg.get("watch_paths") or []
    if explicit:
        return list(explicit)

    import omni.client  # Kit Python only; the watcher already runs under it.

    job_root = str(cfg.get("nucleus_job_root", "") or "").rstrip("/")
    # The animation USDs live directly in the job root (the Animation Export folder).
    anim_dir = str(cfg.get("animation_source_dir", "") or "").rstrip("/") or job_root
    assembly_url = str(cfg.get("assembly_definition_url", "") or "").strip()
    mt_dir = str(cfg.get("model_target_dir", "") or "").strip() or (
        f"{job_root}/model_target" if job_root else ""
    )

    if not assembly_url:
        if not job_root:
            logger.error("No watch_paths and no nucleus_job_root/assembly_definition_url to derive from")
            return []
        json_dir = f"{job_root}/JSON"
        result, entries = omni.client.list(_enc(json_dir))
        if result != omni.client.Result.OK:
            logger.error("Cannot list %s: %s", json_dir, result)
            return []
        jsons = [e.relative_path for e in entries if e.relative_path.lower().endswith(".json")]
        if len(jsons) != 1:
            logger.error("Expected exactly one .json in %s, found %s", json_dir, jsons)
            return []
        assembly_url = f"{json_dir}/{jsons[0]}"

    result, _version, content = omni.client.read_file(_enc(assembly_url))
    if result != omni.client.Result.OK:
        logger.error("Cannot read %s: %s", assembly_url, result)
        return []
    definition = json.loads(bytes(content).decode("utf-8"))

    paths: list[str] = [_enc(assembly_url)]

    if mt_dir:
        mt_result, mt_entries = omni.client.list(_enc(mt_dir))
        if mt_result == omni.client.Result.OK:
            for e in mt_entries:
                if e.relative_path.lower().endswith((".dat", ".xml")):
                    paths.append(_enc(f"{mt_dir}/{e.relative_path}"))
        else:
            logger.warning("Cannot list model_target %s: %s", mt_dir, mt_result)

    for operation in definition.get("operations", []):
        for step in operation.get("steps", []):
            if step.get("is_animation"):
                paths.append(_enc(f"{anim_dir}/{_usd_name(step['step_id'])}"))

    logger.info("Derived %d watch path(s) from %s", len(paths), assembly_url)
    return paths


def auto_derive_enabled(cfg: dict) -> bool:
    """True when no explicit watch_paths is configured, so the list is derived."""
    return not (cfg.get("watch_paths") or [])


def refresh_watch_paths(
    cfg: dict,
    logger: logging.Logger,
    current: list[str],
    last_seen: dict[str, float],
    state_file: Path,
) -> list[str]:
    """Re-derive the watch list after a pipeline run and report what changed.

    A rebuild may have consumed a NEW assembly_definition.json (steps added,
    removed, or re-flagged is_animation), which changes the set of USDs we should
    be watching. Re-deriving here means a new animation step starts being watched
    without restarting the watcher.

    Fails safe: on any error, or if the re-derived list comes back empty (Nucleus
    hiccup, JSON temporarily unreadable), the previous list is kept so the watcher
    never ends up watching nothing.
    """
    try:
        updated = resolve_watch_paths(cfg, logger)
    except Exception as exc:  # noqa: BLE001 -- never let a refresh kill the watcher
        logger.exception("Could not re-derive watch paths (keeping current list): %s", exc)
        return current

    if not updated:
        logger.warning(
            "Re-derived watch list came back empty; keeping the previous %d path(s)",
            len(current),
        )
        return current

    before, after = set(current), set(updated)
    added, removed = after - before, before - after
    if not added and not removed:
        return updated

    for p in sorted(added):
        # New paths have no last_seen entry, so the next poll sees them as changed
        # and rebuilds them -- which is what we want for a newly added step.
        logger.info("Now watching (new): %s", p)
    for p in sorted(removed):
        logger.info("No longer watching: %s", p)
        last_seen.pop(p, None)  # prune state so it can't grow unbounded
    if removed:
        save_state(state_file, last_seen)

    logger.info(
        "Watch list updated: %d path(s) (+%d, -%d)", len(updated), len(added), len(removed)
    )
    return updated


def load_state(state_file: Path) -> dict[str, float]:
    if not state_file.exists():
        return {}
    try:
        return json.loads(state_file.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}


def save_state(state_file: Path, state: dict[str, float]) -> None:
    state_file.parent.mkdir(parents=True, exist_ok=True)
    state_file.write_text(json.dumps(state, indent=2), encoding="utf-8")


def stat_nucleus_mtime(path: str, logger: logging.Logger) -> float | None:
    """Return the modified-time of a Nucleus path as Unix seconds, or None on failure.

    omni.client.stat() is synchronous and returns (Result, ListEntry).
    The mtime attribute name and type vary by Kit version:
      - older: entry.modified_time_ns (int, nanoseconds)
      - newer: entry.modified_time    (datetime, or float seconds)
    """
    import omni.client  # imported lazily so the help text works without Kit Python

    result, entry = omni.client.stat(path)
    if result != omni.client.Result.OK or entry is None:
        logger.warning("stat failed for %s: %s", path, result)
        return None

    ns = getattr(entry, "modified_time_ns", None)
    if ns is not None:
        return float(ns) / 1e9

    mt = getattr(entry, "modified_time", None)
    if mt is None:
        logger.warning("ListEntry has neither modified_time_ns nor modified_time for %s", path)
        return None
    if hasattr(mt, "timestamp"):
        return mt.timestamp()
    return float(mt)


def detect_changes(
    watch_paths: list[str],
    last_seen: dict[str, float],
    logger: logging.Logger,
) -> list[str]:
    changed: list[str] = []
    for p in watch_paths:
        mtime = stat_nucleus_mtime(p, logger)
        if mtime is None:
            continue
        prev = last_seen.get(p)
        if prev is None or mtime > prev + 0.001:
            changed.append(p)
            last_seen[p] = mtime
    return changed


def run_pipeline(
    cfg: dict,
    logger: logging.Logger,
    changed_urls: list[str],
) -> int:
    # Use the SAME Python that runs the watcher (Kit's bundled Python). The
    # pipeline_runner imports omni.client via nucleus_job_service, which is
    # only available in Kit's Python, not the project venv.
    cmd = [sys.executable, str(PIPELINE_RUNNER), "--config", str(CONFIG_PATH)]

    # Pass the list of changed Nucleus URLs through env var. The Kit subprocess
    # will read this same var (it's inherited) and skip re-exporting parts whose
    # source USD wasn't touched. Empty / unset = full rebuild (manual trigger_now).
    env = os.environ.copy()
    env["LIVESYNC_CHANGED_URLS"] = "|".join(changed_urls)

    logger.info(
        "Triggering pipeline (incremental: %d changed path(s)): %s",
        len(changed_urls),
        " ".join(cmd),
    )
    try:
        proc = subprocess.run(cmd, timeout=cfg["pipeline_timeout_sec"], env=env)
    except subprocess.TimeoutExpired:
        logger.error("Pipeline exceeded %ss timeout", cfg["pipeline_timeout_sec"])
        return 124
    logger.info("Pipeline finished with exit code %s", proc.returncode)
    return proc.returncode


def main() -> int:
    cfg = load_config()
    log_dir = Path(cfg["log_dir"])
    logger = setup_logging(log_dir)
    logger.info("Live-sync watcher starting")
    watch_paths = resolve_watch_paths(cfg, logger)
    # When watch_paths is empty in the config the list is DERIVED from the
    # assembly definition, so it can be refreshed after each rebuild. An explicit
    # list in the config stays authoritative and is never overwritten.
    auto_derive = auto_derive_enabled(cfg)
    logger.info(
        "Watching %d path(s) on Nucleus (%s)",
        len(watch_paths),
        "auto-derived; refreshed after each rebuild" if auto_derive
        else "explicit watch_paths from config; NOT auto-refreshed",
    )

    state_file = Path(cfg["state_file"])
    last_seen = load_state(state_file)

    poll = int(cfg["poll_interval_sec"])
    debounce = int(cfg["debounce_sec"])
    last_change_at: float | None = None
    # URLs that changed since the last successful pipeline trigger. Accumulates
    # across multiple polls within a debounce window so an artist saving two
    # files in quick succession produces ONE pipeline run that rebuilds both.
    pending_changes: set[str] = set()

    while True:
        try:
            changed = detect_changes(watch_paths, last_seen, logger)
        except Exception as exc:
            logger.exception("Error while polling Nucleus: %s", exc)
            time.sleep(poll)
            continue

        if changed:
            for p in changed:
                logger.info("Change detected: %s", p)
                pending_changes.add(p)
            save_state(state_file, last_seen)
            last_change_at = time.monotonic()

        if last_change_at is not None and time.monotonic() - last_change_at >= debounce:
            logger.info(
                "Debounce window elapsed (%ss quiet); running pipeline for %d changed path(s)",
                debounce,
                len(pending_changes),
            )
            urls_to_rebuild = sorted(pending_changes)
            pending_changes.clear()
            last_change_at = None
            rc = run_pipeline(cfg, logger, urls_to_rebuild)
            if rc != 0:
                logger.error("Pipeline reported failure (exit %s); will retry on next change", rc)

            # The rebuild may have picked up a new assembly_definition.json, which
            # changes which USDs exist / matter. Re-derive so added steps start
            # being watched (and removed ones stop) without a watcher restart.
            if auto_derive:
                watch_paths = refresh_watch_paths(
                    cfg, logger, watch_paths, last_seen, state_file
                )

        time.sleep(poll)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\n[watcher] Stopped by user")
        sys.exit(0)
