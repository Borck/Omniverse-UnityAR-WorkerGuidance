"""Run the full GLB-and-manifest pipeline once.

Invoked by watcher.py when a Nucleus change is detected, and also directly by
trigger_now.bat for manual testing.

Two stages:
  1. Kit headless export   -> subprocess that writes GLBs + _export_report.json
                              to a Nucleus folder (NUCLEUS_OUTPUT_ROOT inside
                              export_glbs_from_usd.py).
  2. nucleus_job_service.prepare_job() -> in-process call that downloads
                              GLBs from Nucleus, hashes them, writes the
                              versioned manifest the FastAPI server serves,
                              and updates step-definitions.yaml.

Stage 2 imports omni.client and must therefore run in Kit's bundled Python.
Both trigger_now.bat and run_watcher.bat point at that Python.

Exit code 0 on success, non-zero on any failure. Logs to stdout and to a
timestamped file under <log_dir>/runs/.
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import subprocess
import sys
import time
import traceback
from pathlib import Path

import yaml


SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_CONFIG = SCRIPT_DIR / "livesync.config.yaml"


def load_config(path: Path) -> dict:
    if not path.exists():
        sys.exit(
            f"Missing config: {path}\n"
            f"Copy livesync.config.example.yaml to livesync.config.yaml and edit it."
        )
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def format_command(template: str, cfg: dict) -> str:
    return template.format(
        repo_root=cfg["repo_root"],
        kit_app_dir=cfg["kit_app_dir"],
        job_id=cfg["job_id"],
    )


def run_subprocess_step(name: str, command: str, cwd: Path, timeout: int, log_file: Path) -> int:
    banner = f"\n=== {name} ===\n$ {command}\n"
    print(banner, flush=True)
    with log_file.open("a", encoding="utf-8") as f:
        f.write(banner)
        f.flush()
        started = time.monotonic()
        try:
            proc = subprocess.run(
                command,
                cwd=cwd,
                shell=True,
                stdout=f,
                stderr=subprocess.STDOUT,
                timeout=timeout,
            )
        except subprocess.TimeoutExpired:
            f.write(f"\n[TIMEOUT after {timeout}s]\n")
            print(f"[pipeline_runner] {name} timed out after {timeout}s", flush=True)
            return 124
        elapsed = time.monotonic() - started
        f.write(f"\n[exit {proc.returncode} after {elapsed:.1f}s]\n")
    print(f"[pipeline_runner] {name} -> exit {proc.returncode} in {elapsed:.1f}s", flush=True)
    return proc.returncode


def run_prepare_job(cfg: dict, repo_root: Path, log_file: Path) -> int:
    """Stage 2: download GLBs + build manifest, in-process.

    Imports nucleus_job_service which depends on omni.client and
    app.core.config. Adds server-kit/ to sys.path so `from app.core.*` works.
    """
    name = "nucleus_job_service.prepare_job"
    banner = f"\n=== {name} ===\n"
    print(banner, flush=True)
    started = time.monotonic()
    with log_file.open("a", encoding="utf-8") as f:
        f.write(banner)
        f.flush()
        try:
            sys.path.insert(0, str(repo_root / "server-kit"))
            from app.omniverse.nucleus_job_service import prepare_job  # noqa: E402

            nucleus_export_path = f"{cfg['nucleus_export_root'].rstrip('/')}/{cfg['job_id']}"
            f.write(f"nucleus_export_path = {nucleus_export_path}\n")
            f.flush()

            result = prepare_job(
                nucleus_export_path=nucleus_export_path,
                repo_root=repo_root,
                target_id=cfg.get("target_id", ""),
                target_version=cfg.get("target_version", "v1.0.0"),
                target_file=cfg.get("target_file", "demonstrator.dat"),
            )
            elapsed = time.monotonic() - started
            f.write(f"result = {result}\n")
            f.write(f"[ok after {elapsed:.1f}s]\n")
            print(f"[pipeline_runner] {name} -> ok in {elapsed:.1f}s (steps_synced={result.get('steps_synced')})", flush=True)
            return 0
        except Exception as exc:
            elapsed = time.monotonic() - started
            tb = traceback.format_exc()
            f.write(f"\n[FAIL after {elapsed:.1f}s]\n{tb}\n")
            print(f"[pipeline_runner] {name} FAILED: {exc}", flush=True)
            print(tb, flush=True)
            return 1


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the full live-sync pipeline once.")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    args = parser.parse_args()

    cfg = load_config(args.config)
    repo_root = Path(cfg["repo_root"])

    log_dir = Path(cfg["log_dir"]) / "runs"
    log_dir.mkdir(parents=True, exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    log_file = log_dir / f"pipeline-{stamp}.log"
    print(f"[pipeline_runner] Log: {log_file}", flush=True)

    # The watcher sets LIVESYNC_CHANGED_URLS to a pipe-separated list of Nucleus
    # URLs that changed. The Kit subprocess inherits this env var and skips
    # re-exporting parts whose source USD wasn't touched. Empty/unset = full
    # rebuild (manual trigger_now.bat).
    changed_env = os.environ.get("LIVESYNC_CHANGED_URLS", "")
    if changed_env:
        n = sum(1 for u in changed_env.split("|") if u)
        print(f"[pipeline_runner] Incremental rebuild: {n} changed Nucleus URL(s)", flush=True)
    else:
        print("[pipeline_runner] Full rebuild (no LIVESYNC_CHANGED_URLS)", flush=True)

    kit_cmd = format_command(cfg["kit_export_command"], cfg)
    rc = run_subprocess_step("Kit GLB export", kit_cmd, repo_root, cfg["pipeline_timeout_sec"], log_file)
    if rc != 0:
        return rc

    return run_prepare_job(cfg, repo_root, log_file)


if __name__ == "__main__":
    sys.exit(main())
