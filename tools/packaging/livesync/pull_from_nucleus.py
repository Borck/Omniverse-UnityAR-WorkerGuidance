"""Pull already-exported GLBs from Nucleus into shared/samples — no Kit.

This is the Stage-2-only path: it runs nucleus_job_service.prepare_job()
directly, which:
  1. Reads <job>/_export_report.json from the ACTIVE Nucleus (per .env),
  2. Downloads each GLB into shared/samples/assets/_raw/<job_id>/,
  3. Hashes them into shared/samples/assets/sha256_*/ versioned folders,
  4. Writes shared/samples/manifests/<job_id>.manifest.json,
  5. Appends/updates the job block in shared/samples/step-definitions.yaml.

Use this when the export already exists on Nucleus and you just need it on this
PC — no USD Composer, no Kit headless export, no FastAPI endpoint call.

Run with the venv that has omni.client (.venv310). The pull_now.bat next to this
file does that for you. Manual equivalent:

    .venv310\\Scripts\\python.exe tools\\packaging\\livesync\\pull_from_nucleus.py --job-id demonstrator-26-02-25
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

# tools/packaging/livesync/pull_from_nucleus.py -> repo root is parents[3].
REPO_ROOT = Path(__file__).resolve().parents[3]
CONFIG_PATH = Path(__file__).resolve().parent / "livesync.config.yaml"


def _config_defaults() -> dict:
    """Defaults for this pull, taken from livesync.config.yaml when present so the
    config is the single source of truth (same file the watcher/trigger read).
    Hardcoded fallbacks apply only when no config exists."""
    defaults = {
        "job_id": "Segment_Assembly",
        "nucleus_export_root": "/Users/abdul",
        "target_version": "2026-03-10.1",
        "target_file": "Fixture_detectors_1.dat",
    }
    if CONFIG_PATH.exists():
        try:
            import yaml
            cfg = yaml.safe_load(CONFIG_PATH.read_text(encoding="utf-8")) or {}
            for key in defaults:
                if cfg.get(key):
                    defaults[key] = cfg[key]
        except Exception:
            pass
    return defaults


def main() -> int:
    d = _config_defaults()
    parser = argparse.ArgumentParser(
        description="Pull exported GLBs from Nucleus (no Kit). Reads "
                    "livesync.config.yaml for defaults; flags override."
    )
    parser.add_argument("--job-id", default=d["job_id"],
                        help="Job id / Nucleus export folder name.")
    parser.add_argument("--nucleus-path", default=None,
                        help="Nucleus folder holding _export_report.json. "
                             "Default: <nucleus_export_root>/<job-id>")
    parser.add_argument("--target-version", default=d["target_version"])
    parser.add_argument("--target-file", default=d["target_file"])
    args = parser.parse_args()

    # The exporter writes GLBs to <nucleus_export_root>/<job-id>, so the report
    # lives at <nucleus_export_root>/<job-id>/_export_report.json.
    nucleus_path = args.nucleus_path or f"{d['nucleus_export_root'].rstrip('/')}/{args.job_id}"

    # server-kit/ on sys.path so `from app.*` imports resolve.
    sys.path.insert(0, str(REPO_ROOT / "server-kit"))
    from app.omniverse.nucleus_manager import get_manager
    from app.omniverse.nucleus_job_service import prepare_job

    server = get_manager().active_server()
    print(f"[pull] Active Nucleus : {server}")
    print(f"[pull] Export report  : {server}{nucleus_path}/_export_report.json")
    print(f"[pull] Job id         : {args.job_id}")
    print("[pull] Downloading...", flush=True)

    try:
        result = prepare_job(
            nucleus_export_path=nucleus_path,
            repo_root=REPO_ROOT,
            target_version=args.target_version,
            target_file=args.target_file,
        )
    except Exception as exc:
        print(f"\n[pull] FAILED: {exc}")
        print("[pull] Common causes: the export doesn't exist at that Nucleus path, "
              "the wrong Nucleus is active (check OMNI_ACTIVE_NUCLEUS in .env), "
              "or the credentials in .env are wrong.")
        return 1

    print(f"\n[pull] Done: {result}")
    print(f"[pull] GLBs are now under {REPO_ROOT / 'shared' / 'samples' / 'assets'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
