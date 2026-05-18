

"""Kit-side GLB exporter for per-part isolated animations.

Runs inside Omniverse Kit (USD Composer or a headless Kit session). For each
configured part, opens the part-specific animation USD from Nucleus, flattens
the composed stage, and exports a clean GLB with no materials, no lights,
no cameras, and animations baked in. Output GLBs land in a job-scoped
directory on AT21's local disk where the orchestrator picks them up.

Invocation from Kit USD Composer's Script Editor:
    exec(open(r"D:\\Users\\Abdul\\Omniverse-UnityAR-WorkerGuidance\\Omniverse-UnityAR-WorkerGuidance\\tools\\packaging\\export_glbs_from_usd.py").read())

Invocation from a headless Kit command line:
    kit.exe --no-window --exec "D:\\...\\tools\\packaging\\export_glbs_from_usd.py"

Output:
    <REPO_ROOT>/shared/samples/assets/_raw/<job_id>/<part_id>.glb
    <REPO_ROOT>/shared/samples/assets/_raw/<job_id>/_export_report.json
"""

from __future__ import annotations

import asyncio
import json
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any
import omni.client
# --- CONFIGURATION ---------------------------------------------------------

JOB_ID = "demonstrator-26-02-25"
NUCLEUS_BASE = "omniverse://141.43.76.21/Projects/DIREKT/Omniverse%20Tests/Animation%20Februar%2025"
# REPO_ROOT = Path(r"D:\Users\Abdul\Omniverse-UnityAR-WorkerGuidance\Omniverse-UnityAR-WorkerGuidance")
NUCLEUS_OUTPUT_ROOT = "omniverse://141.43.76.21/Users/shahan"

@dataclass(frozen=True)
class PartSpec:
    step_id: str           # stable step identifier
    part_id: str           # filesystem-safe part name
    display_name: str      # human-readable label
    usd_basename: str      # exact USD filename without .usd extension (spaces allowed)
    sequence_index: int    # assembly order (1-based)


PARTS: list[PartSpec] = [
    PartSpec("step-001", "plate_bottom_01",     "PLATE_BOTTOM_01_001",                              "PLATE_BOTTOM_01_001",                              1),
    PartSpec("step-002", "cores_001_002",       "CORES_001 CORES_002",                              "CORES_001 CORES_002",                              2),
    PartSpec("step-003", "left_unit_phase_03",  "LEFT_UNIT_PHASE_03_001",                           "LEFT_UNIT_PHASE_03_001",                           3),
    PartSpec("step-004", "right_unit_phase_03", "RIGHT_UNIT_PHASE_03_001",                          "RIGHT_UNIT_PHASE_03_001",                          4),
    PartSpec("step-005", "plate_top_02",        "PLATE_TOP_02_002",                                 "PLATE_TOP_02_002",                                 5),
    PartSpec("step-006", "frame_ring_03_004",   "TestFrameRing03_004",                              "TestFrameRing03_004",                              6),
    PartSpec("step-007", "frame_ring_03_multi", "TestFrameRing03_003-005-006-007-010-011",          "TestFrameRing03_003-005-006-007-010-011",          7),
]

# glTFast-friendly settings: disable materials, lights, cameras. Keep animations.
CONVERTER_SETTINGS: dict[str, Any] = {
    "ignore_materials": True,
    "ignore_animations": False,
    "ignore_camera": True,
    "ignore_light": True,
    "embed_textures": False,
    "embed_mdl_in_usd": False,
    "export_preview_surface": False,
    "export_hidden_props": False,
    "export_mdl_gltf_extension": False,
    "export_separate_gltf": False,
    "bake_mdl_material": False,
    "baking_scales": False,
    "convert_fbx_to_y_up": False,
    "convert_fbx_to_z_up": False,
    "create_world_as_default_root_prim": True,
    "disabling_instancing": False,
    "ignore_flip_rotations": False,
    "ignore_pivots": False,
    "ignore_unbound_bones": False,
    "keep_all_materials": False,
    "merge_all_meshes": False,
    "single_mesh": False,
    "smooth_normals": True,
    "support_point_instancer": False,
    "use_double_precision_to_usd_transform_op": False,
    "use_meter_as_world_unit": True,
}


# --- KIT-ONLY IMPORTS ------------------------------------------------------

import omni.usd                          # noqa: E402
import omni.kit.asset_converter as asset_converter  # noqa: E402


def _encode_url(basename: str) -> str:
    return basename.replace(" ", "%20")


def _apply_converter_settings(context: Any) -> None:
    for key, value in CONVERTER_SETTINGS.items():
        if hasattr(context, key):
            setattr(context, key, value)
        else:
            print(f"[warn] AssetConverterContext has no '{key}', skipping")


async def export_part_glb(part: PartSpec, output_base_url: str) -> tuple[bool, str]:
    part_url = f"{NUCLEUS_BASE}/{_encode_url(part.usd_basename)}.usd"

    # Construct the Nucleus URL for the output GLB
    output_glb_url = f"{output_base_url}/{part.part_id}.glb"

    print(f"[{part.step_id}] Opening {part_url}")
    ctx = omni.usd.get_context()
    opened = await ctx.open_stage_async(part_url)
    if not opened:
        return False, f"open_stage_async returned False for {part_url}"

    # Flattening still happens locally in temp space for speed/stability
    tmp_dir = Path(tempfile.mkdtemp(prefix="guidance_flat_"))
    flat_usd = tmp_dir / "flat.usd"

    flat_result = await ctx.export_as_stage_async(str(flat_usd))
    if isinstance(flat_result, tuple):
        ok, err = flat_result
    else:
        ok, err = bool(flat_result), "unknown"
    if not ok:
        return False, f"flatten failed: {err}"

    conv_ctx = asset_converter.AssetConverterContext()
    _apply_converter_settings(conv_ctx)

    # Convert directly to Nucleus URL
    converter = asset_converter.get_instance()
    task = converter.create_converter_task(
        str(flat_usd),
        output_glb_url, # Now a Nucleus URL
        None,
        conv_ctx,
    )
    success = await task.wait_until_finished()
    if not success:
        return False, f"convert failed: {task.get_error_message()} (status {task.get_status()})"

    print(f"[{part.step_id}] Wrote {output_glb_url}")
    return True, output_glb_url


async def run() -> None:
    # Build the full Nucleus job path
    output_dir_url = f"{NUCLEUS_OUTPUT_ROOT}/{JOB_ID}"

    print(f"=== Exporting {len(PARTS)} parts for job '{JOB_ID}' ===")
    print(f"=== Output Nucleus: {output_dir_url} ===\n")

    report_data: list[dict[str, Any]] = []

    for part in PARTS:
        ok, detail = await export_part_glb(part, output_dir_url)
        report_data.append({
            "step_id": part.step_id,
            "part_id": part.part_id,
            "display_name": part.display_name,
            "sequence_index": part.sequence_index,
            "glb_url": detail if ok else None,
            "ok": ok,
            "detail": detail,
        })

    # --- NEW: Write the JSON report to Nucleus ---
    report_path_url = f"{output_dir_url}/_export_report.json"

    report_content = json.dumps({
        "job_id": JOB_ID,
        "nucleus_base": NUCLEUS_BASE,
        "parts": report_data,
    }, indent=2)

    # omni.client.write_file expects bytes
    content_bytes = report_content.encode("utf-8")
    result = omni.client.write_file(report_path_url, content_bytes)

    if result == omni.client.Result.OK:
        print(f"Success: Report written to {report_path_url}")
    else:
        print(f"Error: Failed to write report to Nucleus (Result: {result})")

    print("\n=== Summary ===")
    for row in report_data:
        mark = "OK  " if row["ok"] else "FAIL"
        print(f"{mark} {row['step_id']}  {row['part_id']}")
asyncio.ensure_future(run())
