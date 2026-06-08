"""Run the full GLB-and-manifest pipeline once.

Invoked by watcher.py when a Nucleus change is detected, and also directly by
trigger_now.bat for manual testing. Owns the two subprocess calls:

  1. Kit headless export  -> writes GLBs + _export_report.json
  2. automate_job.py      -> writes manifest, runs build_runtime_packages.py

Exit code 0 on success, non-zero on any failure. Logs to stdout (captured by
the watcher) and to a timestamped file under <log_dir>/runs/.
"""

from __future__ import annotations

import argparse
import datetime as dt
import subprocess
import sys
import time
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


def run_step(name: str, command: str, cwd: Path, timeout: int, log_file: Path) -> int:
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


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the full live-sync pipeline once.")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    args = parser.parse_args()

    cfg = load_config(args.config)
    repo_root = Path(cfg["repo_root"])
    job_id = cfg["job_id"]

    log_dir = Path(cfg["log_dir"]) / "runs"
    log_dir.mkdir(parents=True, exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    log_file = log_dir / f"pipeline-{stamp}.log"
    print(f"[pipeline_runner] Log: {log_file}", flush=True)

    kit_cmd = format_command(cfg["kit_export_command"], cfg)
    rc = run_step("Kit GLB export", kit_cmd, repo_root, cfg["pipeline_timeout_sec"], log_file)
    if rc != 0:
        return rc

    automate_cmd = (
        f"\"{cfg['venv_python']}\" tools/packaging/automate_job.py --job-id {job_id}"
    )
    rc = run_step("automate_job", automate_cmd, repo_root, cfg["pipeline_timeout_sec"], log_file)
    return rc


if __name__ == "__main__":
    sys.exit(main())
