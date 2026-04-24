"""Pulls a prepared job from Nucleus and builds the local manifest + YAML."""
from __future__ import annotations

import hashlib
import json
import shutil
import tempfile
from pathlib import Path
from typing import Any

import omni.client
from app.core.config import SERVER
from app.core.logging import configure_logging

logger = configure_logging("INFO")

WORKFLOW_VERSION = "1.0.0"
TIMELINE_FPS = 30


def _omni_copy_to_local(nucleus_path: str, local_path: Path) -> None:
    """Download one file from Nucleus to local disk."""
    local_path.parent.mkdir(parents=True, exist_ok=True)
    dst_url = "file:///" + str(local_path).replace("\\", "/")
    result = omni.client.copy(
        f"{SERVER}{nucleus_path}",
        dst_url,
        behavior=omni.client.CopyBehavior.OVERWRITE,
    )
    if result != omni.client.Result.OK:
        raise RuntimeError(f"omni.client.copy failed for {nucleus_path}: {result}")


def _read_nucleus_json(nucleus_path: str) -> dict:
    """Read and parse a JSON file directly from Nucleus."""
    result, version, content = omni.client.read_file(f"{SERVER}{nucleus_path}")
    if result != omni.client.Result.OK:
        raise RuntimeError(f"Cannot read {nucleus_path}: {result}")
    return json.loads(bytes(content).decode("utf-8"))


def _hash_glb(glb_path: Path) -> str:
    h = hashlib.sha256(glb_path.read_bytes()).hexdigest()
    return h


def prepare_job(
    nucleus_export_path: str,   # e.g. /Projects/DIREKT/.../Exports/demonstrator-26-02-25
    repo_root: Path,
    target_id: str = "demonstrator_model_target",
    target_version: str = "v1.0.0",
    target_file: str = "demonstrator.dat",
) -> dict[str, Any]:
    """
    1. Reads _export_report.json from Nucleus
    2. Downloads each GLB to shared/samples/assets/_raw/{job_id}/
    3. Hashes GLBs → versioned asset directories
    4. Writes step-definitions.yaml + manifest.json
    Returns: { "job_id": ..., "steps_synced": N }
    """
    nucleus_export_path = nucleus_export_path.rstrip("/")

    # ── 1. Read the export report from Nucleus ──────────────────────────────
    report = _read_nucleus_json(f"{nucleus_export_path}/_export_report.json")
    job_id = report["job_id"]
    parts = [p for p in report["parts"] if p["ok"]]
    logger.info(f"[NucleusJobService] Preparing job '{job_id}' — {len(parts)} parts")
    if parts:
        logger.info(f"[NucleusJobService] Report keys: {list(parts[0].keys())}")

    raw_dir = repo_root / "shared" / "samples" / "assets" / "_raw" / job_id
    asset_root = repo_root / "shared" / "samples" / "assets"
    manifests_dir = repo_root / "shared" / "samples" / "manifests"
    manifests_dir.mkdir(parents=True, exist_ok=True)
    raw_dir.mkdir(parents=True, exist_ok=True)

    # ── 2. Download each GLB from Nucleus ───────────────────────────────────
    for part in parts:
        glb_url = part.get("glb_url") or part.get("glb_file") or part.get("file") or ""
        # glb_url may be a full Nucleus URL or just a filename — extract the filename
        glb_filename = glb_url.split("/")[-1] if "/" in glb_url else glb_url
        if not glb_filename:
            glb_filename = f"{part['part_id']}.glb"
        part["_resolved_glb"] = glb_filename
        glb_nucleus = f"{nucleus_export_path}/{glb_filename}"
        glb_local = raw_dir / glb_filename
        if not glb_local.exists():
            logger.info(f"[NucleusJobService] Downloading {glb_filename}")
            _omni_copy_to_local(glb_nucleus, glb_local)
        else:
            logger.info(f"[NucleusJobService] Cached: {glb_filename}")


    # ── 3. Hash GLBs → versioned asset directories ──────────────────────────
    manifest_steps = []
    yaml_steps = []

    for part in parts:
        glb_local = raw_dir / part["_resolved_glb"]
        glb_bytes = glb_local.read_bytes()
        digest = hashlib.sha256(glb_bytes).hexdigest()
        digest_short = digest[:16]
        asset_version = f"sha256_{digest_short}"
        glb_out_name = f"part_{part['part_id']}_{digest[:8]}.glb"
        step_json_name = f"{part['step_id']}.json"

        versioned_dir = asset_root / asset_version
        versioned_dir.mkdir(parents=True, exist_ok=True)

        # Write GLB
        out_glb = versioned_dir / glb_out_name
        if not out_glb.exists():
            shutil.copyfile(glb_local, out_glb)

        # Write step JSON
        step_json = {
            "stepId": part["step_id"],
            "partId": part["part_id"],
            "displayName": part["display_name"],
            "sequenceIndex": part["sequence_index"],
            "assetVersion": asset_version,
            "glbFile": glb_out_name,
            "instructionsShort": f"Install {part['display_name']}",
            "safetyNotes": [],
            "expectedDurationSec": 30,
        }
        (versioned_dir / step_json_name).write_text(
            json.dumps(step_json, indent=2), encoding="utf-8"
        )

        # manifest_steps.append({
        #     "stepId": part["step_id"],
        #     "partId": part["part_id"],
        #     "assetVersion": asset_version,
        #     "glbFile": glb_out_name,
        #     "stepJsonFile": step_json_name,
        #     "targetVersion": target_version,
        #     "targetFile": target_file,
        #     "compression": "NONE",
        # })
        manifest_steps.append({
            "stepId": part["step_id"],
            "partId": part["part_id"],
            "assetVersion": asset_version,
            "glbFile": glb_out_name,
            "stepJsonFile": step_json_name,
            "targetVersion": "",    # ← empty: no Vuforia target needed
            "targetFile": "",       # ← empty: skip target download
            "compression": "NONE",
            })

        yaml_steps.append({
            "step_id": part["step_id"],
            "part_id": part["part_id"],
            "display_name": part["display_name"],
            "sequence_index": part["sequence_index"],
            "asset_version": asset_version,
        })

    # ── 4. Write manifest.json ───────────────────────────────────────────────
    manifest_path = manifests_dir / f"{job_id}.manifest.json"
    manifest_path.write_text(json.dumps({
        "jobId": job_id,
        "workflowVersion": WORKFLOW_VERSION,
        "steps": manifest_steps,
    }, indent=2), encoding="utf-8")
    logger.info(f"[NucleusJobService] Wrote manifest: {manifest_path}")

    # ── 5. Write step-definitions.yaml (append or replace this job) ─────────
    _write_step_definitions_yaml(repo_root, job_id, yaml_steps, target_id, target_version)

    return {"job_id": job_id, "steps_synced": len(parts)}


def _write_step_definitions_yaml(
    repo_root: Path,
    job_id: str,
    steps: list[dict],
    target_id: str,
    target_version: str,
) -> None:
    yaml_path = repo_root / "shared" / "samples" / "step-definitions.yaml"
    end_step = max(s["sequence_index"] for s in steps) * 100

    lines = [f"  - jobId: {job_id}",
             f"    workflowVersion: \"{WORKFLOW_VERSION}\"",
             "    timelineProfile:",
             "      startStep: 0",
             f"      endStep: {end_step}",
             f"      fps: {TIMELINE_FPS}",
             "    steps:"]

    for s in steps:
        idx = s["sequence_index"]
        lines += [
            f"      - stepId: \"{s['step_id']}\"",
            f"        partId: {s['part_id']}",
            f"        displayName: \"{s['display_name']}\"",
            f"        sourcePrimPath: /World",
            f"        activePrimPath: /World",
            f"        animationName: {s['part_id']}_anim",
            f"        anchorType: model-target",
            f"        targetId: {target_id}",
            f"        targetVersion: \"{target_version}\"",
            f"        assetVersion: {s['asset_version']}",
            f"        instructionsShort: \"Install {s['display_name']}\"",
            "        safetyNotes: []",
            "        expectedDurationSec: 30",
            f"        sequenceIndex: {idx}",
            f"        animationStartStep: {(idx-1)*100}",
            f"        animationEndStep: {idx*100}",
            f"        keepVisibleUntilStep: {idx*100}",
            "        animationLayerRole: animation",
            "        targetLayerRole: target-position",
            "        startOffset: [0.0, 0.0, 0.0]",
            "        targetPosition: [0.0, 0.0, 0.0]",
        ]

    # Load existing YAML, replace or append this job
    existing = ""
    if yaml_path.exists():
        existing = yaml_path.read_text(encoding="utf-8")

    # Remove existing entry for this job_id if present
    import re
    existing = re.sub(
        rf"  - jobId: {re.escape(job_id)}.+?(?=  - jobId:|\Z)",
        "",
        existing,
        flags=re.DOTALL,
    )
    existing = existing.rstrip()
    if "jobs:" not in existing:
        existing = "jobs:\n"

    yaml_path.write_text(
        existing.rstrip() + "\n" + "\n".join(lines) + "\n",
        encoding="utf-8"
    )
    logger.info(f"[NucleusJobService] Wrote step-definitions.yaml")
