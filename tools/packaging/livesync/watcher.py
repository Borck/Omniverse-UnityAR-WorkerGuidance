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


def run_pipeline(cfg: dict, logger: logging.Logger) -> int:
    cmd = [cfg["venv_python"], str(PIPELINE_RUNNER), "--config", str(CONFIG_PATH)]
    logger.info("Triggering pipeline: %s", " ".join(cmd))
    try:
        proc = subprocess.run(cmd, timeout=cfg["pipeline_timeout_sec"])
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
    logger.info("Watching %d path(s) on Nucleus", len(cfg["watch_paths"]))

    state_file = Path(cfg["state_file"])
    last_seen = load_state(state_file)

    poll = int(cfg["poll_interval_sec"])
    debounce = int(cfg["debounce_sec"])
    last_change_at: float | None = None

    while True:
        try:
            changed = detect_changes(cfg["watch_paths"], last_seen, logger)
        except Exception as exc:
            logger.exception("Error while polling Nucleus: %s", exc)
            time.sleep(poll)
            continue

        if changed:
            for p in changed:
                logger.info("Change detected: %s", p)
            save_state(state_file, last_seen)
            last_change_at = time.monotonic()

        if last_change_at is not None and time.monotonic() - last_change_at >= debounce:
            logger.info("Debounce window elapsed (%ss quiet); running pipeline", debounce)
            last_change_at = None
            rc = run_pipeline(cfg, logger)
            if rc != 0:
                logger.error("Pipeline reported failure (exit %s); will retry on next change", rc)

        time.sleep(poll)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\n[watcher] Stopped by user")
        sys.exit(0)
