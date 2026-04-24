"""End-to-end job orchestrator.

Runs AFTER `export_glbs_from_usd.py` has produced per-part GLBs. This script:
  1. Reads the export report (_export_report.json)
  2. Stages each GLB and a per-step JSON into the source asset layout
  3. Writes a source manifest JSON (pre-hashing)
  4. Writes step-definitions.yaml with all required fields
  5. Invokes build_runtime_packages.py to hash, version, and write the final
     manifest the FastAPI server serves to Unity
  6. Verifies Vuforia Model Target files are in place under shared/samples/targets

Run from the repo root with venv active:
    python tools\\packaging\\automate_job.py --job-id demonstrator-26-02-25
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]


@dataclass(frozen=True)
class TargetSpec:
    target_id: str
    target_version: str
    xml_filename: str
    dat_filename: str


# Vuforia Model Target used by every step. Files live under
#   shared/samples/targets/<target_version>/<xml_filename>
#   shared/samples/targets/<target_version>/<dat_filename>
# The Unity client loads both before starting the session.
DEFAULT_TARGET = TargetSpec(
    target_id="demonstrator_model_target",
    target_version="v1.0.0",
    xml_filename="demonstrator.xml",
    dat_filename="demonstrator.dat",
)

WORKFLOW_VERSION = "1.0.0"
TIMELINE_FPS = 30


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Automate job packaging for Unity AR client.")
    p.add_argument("--job-id", required=True, help="e.g. demonstrator-26-02-25")
    p.add_argument("--target-version", default=DEFAULT_TARGET.target_version)
    p.add_argument("--target-xml", default=DEFAULT_TARGET.xml_filename)
    p.add_argument("--target-dat", default=DEFAULT_TARGET.dat_filename)
    return p.parse_args()


def load_export_report(job_id: str) -> dict[str, Any]:
    report_path = REPO_ROOT / "shared" / "samples" / "assets" / "_raw" / job_id / "_export_report.json"
    if not report_path.exists():
        sys.exit(
            f"Export report missing: {report_path}\n"
            f"Run the Kit exporter first (tools/packaging/export_glbs_from_usd.py)."
        )
    return json.loads(report_path.read_text(encoding="utf-8"))


def verify_target_files(target: TargetSpec) -> None:
    target_dir = REPO_ROOT / "shared" / "samples" / "targets" / target.target_version
    xml = target_dir / target.xml_filename
    dat = target_dir / target.dat_filename
    missing = [p for p in (xml, dat) if not p.exists()]
    if missing:
        sys.exit(
            "Missing Vuforia Model Target files:\n  "
            + "\n  ".join(str(m) for m in missing)
            + f"\n\nPlace your .xml and .dat in: {target_dir}"
        )
    print(f"[target] Found {xml.name} and {dat.name} under {target.target_version}")


def stage_source_assets(job_id: str, report: dict[str, Any], target: TargetSpec) -> tuple[list[dict[str, Any]], Path]:
    """Copies each raw GLB into a pre-hash staging area and writes a per-step JSON."""
    raw_dir = REPO_ROOT / "shared" / "samples" / "assets" / "_raw" / job_id
    stage_root = REPO_ROOT / "shared" / "samples" / "assets" / "_staging" / job_id
    if stage_root.exists():
        shutil.rmtree(stage_root)
    stage_root.mkdir(parents=True, exist_ok=True)

    steps: list[dict[str, Any]] = []
    for part in report["parts"]:
        if not part["ok"]:
            sys.exit(f"Part {part['part_id']} failed to export; fix that first.")

        source_glb = raw_dir / part["glb_file"]
        step_dir = stage_root / part["part_id"]
        step_dir.mkdir(parents=True, exist_ok=True)

        staged_glb_name = f"{part['part_id']}.glb"
        staged_glb = step_dir / staged_glb_name
        shutil.copyfile(source_glb, staged_glb)

        step_json_name = f"{part['step_id']}.json"
        step_json_payload = {
            "stepId": part["step_id"],
            "partId": part["part_id"],
            "displayName": part["display_name"],
            "sequenceIndex": part["sequence_index"],
            "instructionsShort": f"Place Part_{part['display_name']}",
            "safetyNotes": [],
            "expectedDurationSec": 30,
            "anchor": {
                "type": "model-target",
                "targetId": target.target_id,
                "targetVersion": target.target_version,
            },
        }
        (step_dir / step_json_name).write_text(
            json.dumps(step_json_payload, indent=2), encoding="utf-8"
        )

        steps.append({
            "step_id": part["step_id"],
            "part_id": part["part_id"],
            "display_name": part["display_name"],
            "sequence_index": part["sequence_index"],
            "asset_version": part["part_id"],   # placeholder, replaced by hasher
            "glb_file": staged_glb_name,
            "step_json_file": step_json_name,
        })

    return steps, stage_root


def write_source_manifest(job_id: str, steps: list[dict[str, Any]], target: TargetSpec) -> Path:
    """Writes the pre-hash manifest build_runtime_packages reads."""
    manifests_dir = REPO_ROOT / "shared" / "samples" / "manifests"
    manifests_dir.mkdir(parents=True, exist_ok=True)
    path = manifests_dir / f"{job_id}.manifest.json"

    payload = {
        "jobId": job_id,
        "workflowVersion": WORKFLOW_VERSION,
        "steps": [
            {
                "stepId": s["step_id"],
                "partId": s["part_id"],
                "assetVersion": s["asset_version"],
                "glbFile": s["glb_file"],
                "stepJsonFile": s["step_json_file"],
                "targetVersion": target.target_version,
                "targetFile": target.xml_filename,
                "compression": "NONE",
            }
            for s in steps
        ],
    }
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"[manifest] Wrote source manifest: {path}")
    return path


def write_step_definitions_yaml(job_id: str, steps: list[dict[str, Any]], target: TargetSpec) -> Path:
    """Writes the YAML that StepDefinitionRepository reads."""
    path = REPO_ROOT / "shared" / "samples" / "step-definitions.yaml"
    end_step = max(s["sequence_index"] for s in steps) * 100

    lines: list[str] = []
    lines.append("jobs:")
    lines.append(f"  - jobId: {job_id}")
    lines.append(f"    workflowVersion: \"{WORKFLOW_VERSION}\"")
    lines.append("    timelineProfile:")
    lines.append("      startStep: 0")
    lines.append(f"      endStep: {end_step}")
    lines.append(f"      fps: {TIMELINE_FPS}")
    lines.append("    steps:")

    for s in steps:
        idx = s["sequence_index"]
        anim_start = (idx - 1) * 100
        anim_end = idx * 100
        lines.extend([
            f"      - stepId: {s['step_id']}",
            f"        partId: {s['part_id']}",
            f"        displayName: \"{s['display_name']}\"",
            f"        sourcePrimPath: /World",
            f"        animationName: {s['part_id']}_anim",
            "        anchorType: model-target",
            f"        targetId: {target.target_id}",
            f"        targetVersion: \"{target.target_version}\"",
            f"        assetVersion: pending",
            f"        instructionsShort: \"Place Part_{s['display_name']}\"",
            "        safetyNotes: []",
            "        expectedDurationSec: 30",
            f"        sequenceIndex: {idx}",
            f"        animationStartStep: {anim_start}",
            f"        animationEndStep: {anim_end}",
            f"        keepVisibleUntilStep: {anim_end}",
            f"        activePrimPath: /World",
            "        animationLayerRole: animation",
            "        targetLayerRole: target-position",
            "        startOffset: [0.0, 0.0, 0.0]",
            "        targetPosition: [0.0, 0.0, 0.0]",
        ])

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"[yaml] Wrote step-definitions: {path}")
    return path


def move_staged_to_asset_root(job_id: str, steps: list[dict[str, Any]]) -> None:
    """Moves staged GLB+JSON into shared/samples/assets/<asset_version> folders
    using the placeholder asset_version (part_id) used by the source manifest.
    build_runtime_packages.py then re-hashes and produces the final versioned
    layout + final manifest.
    """
    stage_root = REPO_ROOT / "shared" / "samples" / "assets" / "_staging" / job_id
    asset_root = REPO_ROOT / "shared" / "samples" / "assets"

    for s in steps:
        src = stage_root / s["part_id"]
        dst = asset_root / s["asset_version"]
        if dst.exists():
            shutil.rmtree(dst)
        shutil.copytree(src, dst)
    print(f"[stage] Copied staged steps into {asset_root}")


def run_build_runtime_packages(job_id: str) -> None:
    script = REPO_ROOT / "tools" / "packaging" / "build_runtime_packages.py"
    cmd = [sys.executable, str(script), "--job-id", job_id]
    print(f"[build] Running {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=REPO_ROOT)
    if result.returncode != 0:
        sys.exit(f"build_runtime_packages.py exited with code {result.returncode}")


def main() -> None:
    args = parse_args()
    target = TargetSpec(
        target_id=DEFAULT_TARGET.target_id,
        target_version=args.target_version,
        xml_filename=args.target_xml,
        dat_filename=args.target_dat,
    )

    print(f"=== Automating job: {args.job_id} ===")
    verify_target_files(target)

    report = load_export_report(args.job_id)
    if report["job_id"] != args.job_id:
        sys.exit(f"Report job_id ({report['job_id']}) != --job-id ({args.job_id})")

    steps, _ = stage_source_assets(args.job_id, report, target)
    write_source_manifest(args.job_id, steps, target)
    write_step_definitions_yaml(args.job_id, steps, target)
    move_staged_to_asset_root(args.job_id, steps)
    run_build_runtime_packages(args.job_id)

    print("\n=== Pipeline complete ===")
    print(f"Unity can now request job: {args.job_id}")


if __name__ == "__main__":
    main()
