"""Pulls a prepared job from Nucleus and builds the local manifest + YAML.

Stage 2 of the live-sync pipeline. Reads the Kit exporter's
_export_report.json (which lists EVERY step -- animation and instruction-only),
downloads the animation GLBs, hashes them into versioned asset dirs, discovers
and downloads the job's Vuforia Model Target from Nucleus, and writes the
manifest + step-definitions.yaml the FastAPI/gRPC server serves.

Nothing is hardcoded: the step list comes from the report (driven by
assembly_definition.json) and the Model Target is discovered from a Nucleus
folder, not named in config.
"""
from __future__ import annotations

import hashlib
import json
import re
import shutil
from pathlib import Path
from typing import Any

import omni.client
from app.omniverse.nucleus_manager import get_manager
from app.core.logging import configure_logging
from app.omniverse.glb_slim import strip_vertex_attributes

logger = configure_logging("INFO")

WORKFLOW_VERSION = "1.0.0"
TIMELINE_FPS = 30


def _enc(url: str) -> str:
    """Percent-encode literal spaces for omni.client full-URL calls."""
    return url.replace(" ", "%20")


def _omni_copy_to_local(nucleus_path: str, local_path: Path) -> None:
    """Download one file from Nucleus (path under SERVER) to local disk."""
    local_path.parent.mkdir(parents=True, exist_ok=True)
    dst_url = "file:///" + str(local_path).replace("\\", "/")
    result = omni.client.copy(
        _enc(f"{get_manager().active_server()}{nucleus_path}"),
        dst_url,
        behavior=omni.client.CopyBehavior.OVERWRITE,
    )
    if result != omni.client.Result.OK:
        raise RuntimeError(f"omni.client.copy failed for {nucleus_path}: {result}")


def _omni_copy_url_to_local(src_url: str, local_path: Path) -> None:
    """Download one file from Nucleus given a FULL omniverse:// URL."""
    local_path.parent.mkdir(parents=True, exist_ok=True)
    dst_url = "file:///" + str(local_path).replace("\\", "/")
    result = omni.client.copy(
        _enc(src_url), dst_url, behavior=omni.client.CopyBehavior.OVERWRITE
    )
    if result != omni.client.Result.OK:
        raise RuntimeError(f"omni.client.copy failed for {src_url}: {result}")


def _list_url(url: str) -> list[str]:
    result, entries = omni.client.list(_enc(url))
    if result != omni.client.Result.OK:
        raise RuntimeError(f"omni.client.list failed for {url}: {result}")
    return [e.relative_path for e in entries]


def _read_nucleus_json(nucleus_path: str) -> dict:
    """Read and parse a JSON file directly from Nucleus."""
    result, version, content = omni.client.read_file(f"{get_manager().active_server()}{nucleus_path}")
    if result != omni.client.Result.OK:
        raise RuntimeError(f"Cannot read {nucleus_path}: {result}")
    return json.loads(bytes(content).decode("utf-8"))


def _sync_model_target(
    model_target_dir: str,
    repo_root: Path,
    job_id: str,
) -> tuple[str, str, str]:
    """Discover the Vuforia dataset in a Nucleus folder and copy it verbatim.

    The .dat/.xml are NOT hashed or renamed -- they are copied as-is into
    shared/samples/targets/<version>/, where <version> is the single subfolder
    inside model_target/ if one exists, else the job_id.

    Returns (target_version, target_file, target_id). Empty strings if no
    model_target_dir was given (the job then has no Vuforia target).
    """
    if not model_target_dir:
        logger.warning("[NucleusJobService] No model_target_dir; job has no Vuforia target")
        return "", "", ""

    base = model_target_dir.rstrip("/")
    entries = _list_url(base)
    dats = [e for e in entries if e.lower().endswith(".dat")]
    xmls = [e for e in entries if e.lower().endswith(".xml")]

    version = job_id
    src_dir = base
    if not dats:
        # Fall back to a single version subfolder, e.g. model_target/2026-03-10.1/
        subdirs = [e.rstrip("/") for e in entries if "." not in e.rstrip("/")]
        if len(subdirs) == 1:
            version = subdirs[0]
            src_dir = f"{base}/{subdirs[0]}"
            sub = _list_url(src_dir)
            dats = [e for e in sub if e.lower().endswith(".dat")]
            xmls = [e for e in sub if e.lower().endswith(".xml")]

    if len(dats) != 1:
        raise RuntimeError(
            f"Expected exactly one .dat in {src_dir}, found {dats} -- "
            f"check the model_target folder on Nucleus"
        )

    dat_file = dats[0]
    stem = Path(dat_file).stem
    dest_dir = repo_root / "shared" / "samples" / "targets" / version

    _omni_copy_url_to_local(f"{src_dir}/{dat_file}", dest_dir / dat_file)
    xml_file = f"{stem}.xml"
    if xml_file in xmls:
        _omni_copy_url_to_local(f"{src_dir}/{xml_file}", dest_dir / xml_file)
    else:
        logger.warning(
            f"[NucleusJobService] No matching {xml_file} beside {dat_file} in {src_dir}; "
            f"Vuforia needs both .dat and .xml"
        )

    target_id = f"{stem}_model_target"
    logger.info(
        f"[NucleusJobService] Model target synced -> {dest_dir} "
        f"(version={version}, file={dat_file})"
    )
    return version, dat_file, target_id


def prepare_job(
    nucleus_export_path: str,   # e.g. /Users/abdul/Segment_Assembly
    repo_root: Path,
    model_target_dir: str = "",      # full omniverse:// URL of the model_target folder
    target_version: str = "",        # optional explicit overrides (usually empty)
    target_file: str = "",
    target_id: str = "",
) -> dict[str, Any]:
    """
    1. Reads _export_report.json from Nucleus (lists every step).
    2. Downloads each animation GLB, hashes it into a versioned asset dir.
    3. Discovers + downloads the Vuforia Model Target from Nucleus.
    4. Writes manifest.json + step-definitions.yaml for ALL steps
       (animation steps carry a GLB; instruction-only steps carry empty asset
       fields so the client's manifest lookup still succeeds).
    Returns: { "job_id", "steps_synced", "animation_steps", "orphans_removed" }
    """
    nucleus_export_path = nucleus_export_path.rstrip("/")

    # ── 1. Read the export report from Nucleus ──────────────────────────────
    report = _read_nucleus_json(f"{nucleus_export_path}/_export_report.json")
    job_id = report["job_id"]
    steps = report.get("steps", report.get("parts", []))
    logger.info(f"[NucleusJobService] Preparing job '{job_id}' — {len(steps)} steps")

    raw_dir = repo_root / "shared" / "samples" / "assets" / "_raw" / job_id
    asset_root = repo_root / "shared" / "samples" / "assets"
    manifests_dir = repo_root / "shared" / "samples" / "manifests"
    manifests_dir.mkdir(parents=True, exist_ok=True)
    raw_dir.mkdir(parents=True, exist_ok=True)

    # ── 2. Discover + download the Model Target from Nucleus ────────────────
    tv, tf, tid = _sync_model_target(model_target_dir, repo_root, job_id)
    # Explicit config overrides win, if provided.
    tv = target_version or tv
    tf = target_file or tf
    tid = target_id or tid

    manifest_steps: list[dict] = []
    yaml_steps: list[dict] = []

    # ── 3. Process every step ───────────────────────────────────────────────
    for step in steps:
        step_id = step["step_id"]
        seq = int(step.get("sequence_index", 0))
        instruction = step.get("instruction") or step.get("display_name") or ""
        # New reports set is_animation explicitly. Old-format reports (keyed
        # "parts", no flag) are all animations -> fall back to "has a glb_url".
        is_animation = bool(step.get("is_animation", bool(step.get("glb_url"))))
        step_json_name = f"{step_id.replace('.', '_')}.json"
        has_glb = is_animation and step.get("ok") and step.get("glb_url")

        if has_glb:
            glb_filename = step["glb_url"].split("/")[-1]
            glb_local = raw_dir / glb_filename
            # Always download; live-sync overwrites the same Nucleus filename
            # with new content on every save.
            logger.info(f"[NucleusJobService] Downloading {glb_filename}")
            _omni_copy_to_local(f"{nucleus_export_path}/{glb_filename}", glb_local)

            glb_bytes = glb_local.read_bytes()

            # Drop vertex attributes nothing downstream reads (UVs and vertex
            # colours -- the hologram shader replaces all materials). Done BEFORE
            # hashing so the asset version reflects the bytes actually served,
            # and the ~36% saving reaches the device instead of just the disk.
            glb_bytes, slim = strip_vertex_attributes(glb_bytes)
            if slim["changed"]:
                saved = 1 - slim["bytes_after"] / slim["bytes_before"]
                logger.info(
                    f"[NucleusJobService] Slimmed {glb_filename}: "
                    f"{slim['bytes_before'] / 1e6:.1f} -> {slim['bytes_after'] / 1e6:.1f} MB "
                    f"(-{saved * 100:.0f}%, dropped {', '.join(slim['removed'])})"
                )
            elif slim["error"]:
                logger.warning(
                    f"[NucleusJobService] {glb_filename} left unslimmed: {slim['error']}"
                )

            digest = hashlib.sha256(glb_bytes).hexdigest()
            asset_version = f"sha256_{digest[:16]}"
            part_id = step.get("part_id") or Path(glb_filename).stem
            glb_out_name = f"part_{part_id}_{digest[:8]}.glb"

            versioned_dir = asset_root / asset_version
            versioned_dir.mkdir(parents=True, exist_ok=True)
            out_glb = versioned_dir / glb_out_name
            if not out_glb.exists():
                out_glb.write_bytes(glb_bytes)

            (versioned_dir / step_json_name).write_text(json.dumps({
                "stepId": step_id,
                "partId": part_id,
                "displayName": instruction,
                "sequenceIndex": seq,
                "assetVersion": asset_version,
                "glbFile": glb_out_name,
                "instructionsShort": instruction,
                "isAnimation": True,
                "safetyNotes": [],
                "expectedDurationSec": 30,
            }, indent=2), encoding="utf-8")

            manifest_steps.append({
                "stepId": step_id,
                "partId": part_id,
                "assetVersion": asset_version,
                "glbFile": glb_out_name,
                "stepJsonFile": step_json_name,
                "targetVersion": tv,
                "targetFile": tf,
                "compression": "NONE",
            })
            yaml_steps.append({
                "step_id": step_id,
                "part_id": part_id,
                "display_name": instruction,
                "sequence_index": seq,
                "asset_version": asset_version,
                "is_animation": True,
                "instruction": instruction,
            })
        else:
            # Instruction-only step: present in the manifest with empty asset
            # fields so the client's stepId lookup succeeds (it shows text only
            # when glbUrl is empty).
            manifest_steps.append({
                "stepId": step_id,
                "partId": "",
                "assetVersion": "",
                "glbFile": "",
                "stepJsonFile": "",
                "targetVersion": tv,
                "targetFile": tf,
                "compression": "NONE",
            })
            yaml_steps.append({
                "step_id": step_id,
                "part_id": "",
                "display_name": instruction,
                "sequence_index": seq,
                "asset_version": "",
                "is_animation": False,
                "instruction": instruction,
            })

    # ── 4. Write manifest.json ──────────────────────────────────────────────
    manifest_path = manifests_dir / f"{job_id}.manifest.json"
    manifest_path.write_text(json.dumps({
        "jobId": job_id,
        "workflowVersion": WORKFLOW_VERSION,
        "steps": manifest_steps,
    }, indent=2), encoding="utf-8")
    logger.info(f"[NucleusJobService] Wrote manifest: {manifest_path}")

    # ── 5. Write step-definitions.yaml (all steps) ──────────────────────────
    _write_step_definitions_yaml(repo_root, job_id, yaml_steps, tid, tv)

    # ── 6. Cleanup orphaned versioned asset folders ─────────────────────────
    removed = _cleanup_orphaned_versions(asset_root, manifest_steps, logger)

    animation_steps = sum(1 for m in manifest_steps if m["glbFile"])
    return {
        "job_id": job_id,
        "steps_synced": len(steps),
        "animation_steps": animation_steps,
        "orphans_removed": removed,
    }


def _cleanup_orphaned_versions(
    asset_root: Path,
    manifest_steps: list[dict],
    logger,
) -> int:
    """Delete every sha256_*/ folder no longer referenced by the manifest.

    Aggressive policy: keeps exactly the current set, nothing more. Safe
    because the source of truth is on Nucleus -- any deleted version can be
    regenerated by re-triggering the pipeline. Returns count of removed dirs.
    """
    live_versions = {s["assetVersion"] for s in manifest_steps if s["assetVersion"]}
    removed = 0
    for entry in asset_root.iterdir():
        if not entry.is_dir() or not entry.name.startswith("sha256_"):
            continue
        if entry.name in live_versions:
            continue
        try:
            shutil.rmtree(entry)
            logger.info(f"[NucleusJobService] Removed orphan: {entry.name}")
            removed += 1
        except OSError as exc:
            logger.warning(f"[NucleusJobService] Couldn't remove {entry.name}: {exc}")
    if removed:
        logger.info(f"[NucleusJobService] Cleaned up {removed} orphaned version(s)")
    return removed


def _yaml_escape(text: str) -> str:
    """Escape a value for a double-quoted YAML scalar."""
    return text.replace("\\", "\\\\").replace('"', '\\"')


def _write_step_definitions_yaml(
    repo_root: Path,
    job_id: str,
    steps: list[dict],
    target_id: str,
    target_version: str,
) -> None:
    yaml_path = repo_root / "shared" / "samples" / "step-definitions.yaml"
    end_step = max((s["sequence_index"] for s in steps), default=0) * 100

    lines = [f"  - jobId: {job_id}",
             f"    workflowVersion: \"{WORKFLOW_VERSION}\"",
             "    timelineProfile:",
             "      startStep: 0",
             f"      endStep: {end_step}",
             f"      fps: {TIMELINE_FPS}",
             "    steps:"]

    for s in steps:
        idx = s["sequence_index"]
        part_id = s.get("part_id", "")
        is_animation = bool(s.get("is_animation", False))
        instruction = _yaml_escape(s.get("instruction") or s.get("display_name") or "")
        display = _yaml_escape(s.get("display_name") or instruction)
        animation_name = f"{part_id}_anim" if part_id else ""
        asset_version_str = s["asset_version"] if s["asset_version"] else '""'
        lines += [
            f"      - stepId: \"{s['step_id']}\"",
            f"        partId: \"{part_id}\"",
            f"        displayName: \"{display}\"",
            f"        sourcePrimPath: /World",
            f"        activePrimPath: /World",
            f"        animationName: \"{animation_name}\"",
            f"        anchorType: model-target",
            f"        targetId: {target_id}",
            f"        targetVersion: \"{target_version}\"",
            f"        assetVersion: {asset_version_str}",
            f"        isAnimation: {str(is_animation).lower()}",
            f"        instructionsShort: \"{instruction}\"",
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

    # Load existing YAML, replace or append this job. Normalise to LF so the
    # regex works consistently on Windows (CRLF) and Linux (LF).
    existing = ""
    if yaml_path.exists():
        existing = yaml_path.read_text(encoding="utf-8").replace("\r\n", "\n")

    # Remove existing entry for this job_id if present. (?=  - jobId:|\Z)
    # ensures an exact job-id match so a prefix job id doesn't strip the wrong
    # block.
    existing = re.sub(
        rf"  - jobId: {re.escape(job_id)}\n.+?(?=  - jobId:|\Z)",
        "",
        existing,
        flags=re.DOTALL,
    )
    existing = existing.rstrip()
    if "jobs:" not in existing:
        existing = "jobs:\n"

    yaml_path.write_text(
        existing.rstrip() + "\n" + "\n".join(lines) + "\n",
        encoding="utf-8",
    )
    logger.info(f"[NucleusJobService] Wrote step-definitions.yaml")
