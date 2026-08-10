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
import re
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


def _nucleus_host(cfg: dict) -> str:
    """Nucleus host prefix (e.g. omniverse://141.43.76.21).

    From the explicit `nucleus_host` key if set, otherwise parsed from
    `nucleus_job_root`. No path is hardcoded here.
    """
    explicit = str(cfg.get("nucleus_host", "") or "").strip().rstrip("/")
    if explicit:
        return explicit
    job_root = str(cfg.get("nucleus_job_root", "") or "").strip()
    match = re.match(r"(omniverse://[^/]+)", job_root)
    if match:
        return match.group(1)
    raise KeyError(
        "Set 'nucleus_host' or 'nucleus_job_root' so the Nucleus host can be resolved"
    )


def _nucleus_output_root(cfg: dict) -> str:
    """Full omniverse:// URL where the Kit exporter drops GLBs + report."""
    host = _nucleus_host(cfg)
    export_root = str(cfg["nucleus_export_root"]).strip("/")
    return f"{host}/{export_root}"


def _model_target_dir(cfg: dict) -> str:
    """Nucleus folder holding the Vuforia model target. Explicit key wins;
    otherwise derived as <nucleus_job_root>/model_target."""
    explicit = str(cfg.get("model_target_dir", "") or "").strip()
    if explicit:
        return explicit
    job_root = str(cfg.get("nucleus_job_root", "") or "").rstrip("/")
    return f"{job_root}/model_target" if job_root else ""


def _export_env(cfg: dict) -> dict[str, str]:
    """Config handed to the Kit exporter subprocess via environment variables.
    Single source of truth = the yaml; nothing is hardcoded in the Kit script."""
    return {
        "DIREKT_JOB_ID": str(cfg["job_id"]),
        "DIREKT_NUCLEUS_OUTPUT_ROOT": _nucleus_output_root(cfg),
        "DIREKT_NUCLEUS_JOB_ROOT": str(cfg.get("nucleus_job_root", "") or "").rstrip("/"),
        "DIREKT_ASSEMBLY_DEFINITION_URL": str(cfg.get("assembly_definition_url", "") or ""),
        "DIREKT_ANIMATION_SOURCE_DIR": str(cfg.get("animation_source_dir", "") or ""),
    }


def _prune_old_logs(runs_dir: Path, keep: int) -> None:
    """Delete old pipeline-*.log files, keeping only the newest `keep`.

    The timestamped filenames (pipeline-YYYYMMDD-HHMMSS.log) sort
    chronologically, so a plain name sort puts the newest last.
    """
    if keep <= 0:
        return
    logs = sorted(runs_dir.glob("pipeline-*.log"))
    for old in logs[:-keep]:
        try:
            old.unlink()
        except OSError:
            pass


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
            model_target_dir = _model_target_dir(cfg)
            f.write(f"nucleus_export_path = {nucleus_export_path}\n")
            f.write(f"model_target_dir    = {model_target_dir}\n")
            f.flush()

            result = prepare_job(
                nucleus_export_path=nucleus_export_path,
                repo_root=repo_root,
                model_target_dir=model_target_dir,
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

    # Fail fast with a readable message (not a raw KeyError that vanishes when
    # trigger_now.bat closes) if a required key is missing or empty.
    required = [
        "repo_root", "job_id", "nucleus_export_root",
        "kit_app_dir", "kit_export_command", "pipeline_timeout_sec", "log_dir",
    ]
    missing = [k for k in required if cfg.get(k) in (None, "")]
    if not cfg.get("nucleus_job_root") and not cfg.get("nucleus_host"):
        missing.append("nucleus_job_root or nucleus_host")
    if missing:
        sys.exit(
            f"[pipeline_runner] Missing required config key(s): {', '.join(missing)}\n"
            f"  Fix them in {args.config}"
        )

    repo_root = Path(cfg["repo_root"])

    log_dir = Path(cfg["log_dir"]) / "runs"
    log_dir.mkdir(parents=True, exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    log_file = log_dir / f"pipeline-{stamp}.log"
    log_file.touch()  # create now so this run's log is counted when pruning
    _prune_old_logs(log_dir, int(cfg.get("max_pipeline_logs", 5)))
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

    # Hand the job config to the Kit exporter subprocess via env. The subprocess
    # (cmd -> repo.bat -> kit) inherits os.environ, same channel the watcher uses
    # for LIVESYNC_CHANGED_URLS. This is what replaces the old hardcoded
    # JOB_ID/NUCLEUS_BASE/PARTS constants inside export_glbs_from_usd.py.
    export_env = _export_env(cfg)
    os.environ.update(export_env)
    for key, value in export_env.items():
        print(f"[pipeline_runner]   {key}={value}", flush=True)

    kit_cmd = format_command(cfg["kit_export_command"], cfg)
    rc = run_subprocess_step("Kit GLB export", kit_cmd, repo_root, cfg["pipeline_timeout_sec"], log_file)
    if rc != 0:
        return rc

    return run_prepare_job(cfg, repo_root, log_file)


if __name__ == "__main__":
    sys.exit(main())
