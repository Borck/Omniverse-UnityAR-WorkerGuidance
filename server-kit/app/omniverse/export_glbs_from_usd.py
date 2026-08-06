"""Kit-side GLB exporter driven by assembly_definition.json (no hardcoded parts).

Runs inside the minimal headless `direkt_export.kit`. Reads the job's
assembly_definition.json from Nucleus and, for every step flagged
`is_animation: true`, opens that step's animation USD
(`step_id_<major>_<minor>.usd`) from the Animation Export folder,
flattens the composed stage, and exports a clean GLB (no materials/lights/
cameras, animations baked) to the job's Nucleus output folder.

Instruction-only steps (`is_animation: false`) are still recorded in the
export report -- with no GLB -- so the downstream manifest lists every step
and the client never fails a step lookup.

ALL configuration comes from environment variables set by pipeline_runner
(which reads livesync.config.yaml). Nothing is hardcoded here:

    DIREKT_JOB_ID                   job identifier (also the Nucleus output subfolder)
    DIREKT_NUCLEUS_OUTPUT_ROOT      full omniverse:// URL; GLBs + report land in <root>/<job_id>/
    DIREKT_NUCLEUS_JOB_ROOT         full omniverse:// URL of the job folder (the Animation
                                    Export folder). When set, these are derived from it:
                                        <job_root>/JSON/*.json  -> assembly definition
                                        <job_root>/             -> the step_id_<M>_<N>.usd files (in the root)
    DIREKT_ASSEMBLY_DEFINITION_URL  optional explicit URL (overrides the JSON/ discovery)
    DIREKT_ANIMATION_SOURCE_DIR     optional explicit dir (overrides the job-root default)

Output:
    <DIREKT_NUCLEUS_OUTPUT_ROOT>/<job_id>/<part_id>.glb
    <DIREKT_NUCLEUS_OUTPUT_ROOT>/<job_id>/_export_report.json

------------------------------------------------------------------------------
RUN HARNESS -- why this file looks the way it does (read before editing the tail):

The minimal `direkt_export.kit` quits the instant this `--exec` script *returns*,
BEFORE any coroutine scheduled with `asyncio.ensure_future` gets to run. If this
file ever ends with a bare `asyncio.ensure_future(run())` (no pump loop), the run
prints NOTHING and exits 0 in ~3 s -- that is the classic "silent exit" symptom.
The pump loop at the bottom is mandatory: it drives `app.update()` until the
coroutine signals completion, then force-exits.

Rules baked in here (do not relearn them):
  * Pass LITERAL spaces to omni.usd.open_stage_async (OmniUsdResolver cannot
    download a %20-encoded Nucleus path). Use _enc() ONLY for omni.client calls.
  * Do NOT `await next_update_async()` inside run() -- it stalls under this loop.
  * stdout is forced to line-buffered below; when pipeline_runner captures this
    process, a block-buffered pipe would otherwise swallow prints on os._exit.
------------------------------------------------------------------------------
"""

from __future__ import annotations

import asyncio
import json
import os
import re
import sys
import tempfile
import time
from pathlib import Path
from typing import Any

# --- OUTPUT HARDENING ------------------------------------------------------
# os._exit() (used in the pump tail) skips Python's normal buffer flushing, and
# when this process's stdout is a captured pipe (pipeline_runner) rather than a
# console, Python block-buffers it by default -- so prints can vanish on exit.
# Force line buffering so every print reaches the parent immediately. Belt-and-
# suspenders: we also flush explicitly before os._exit.
try:
    sys.stdout.reconfigure(line_buffering=True)  # type: ignore[attr-defined]
    sys.stderr.reconfigure(line_buffering=True)  # type: ignore[attr-defined]
except Exception:  # noqa: BLE001 -- non-TextIOWrapper stdout; flush-before-exit still covers us
    pass


def log(msg: str = "") -> None:
    """Print + flush. Every diagnostic goes through here so nothing is buffered away."""
    print(msg, flush=True)


# A load banner printed the instant Kit execs this file. If you SEE this line in
# the log, the current file reached the interpreter; if you do NOT, AT21 is
# running a stale copy or the --exec path is wrong.
_BUILD_TAG = "pump-loop+hardened-logging 2026-07-16"
log(f"=== export_glbs_from_usd.py loaded [{_BUILD_TAG}] ===")


import omni.client  # noqa: E402

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


# --- HELPERS ---------------------------------------------------------------

def _enc(url: str) -> str:
    """Percent-encode literal spaces so omni.client URL parsing stays happy.

    Nucleus folders here contain spaces ('Project Assembly', 'Animation
    Export'). Encoding an already-encoded URL is a no-op (no spaces left).
    NOTE: never hand a _enc()'d URL to omni.usd.open_stage_async -- that resolver
    needs literal spaces. _enc() is for omni.client (read/list/write) only.
    """
    return url.replace(" ", "%20")


def usd_name(step_id: str) -> str:
    """Canonical animation USD filename for a step. Single source of truth.

    The files on Nucleus are named "step_id_<major>_<minor>.usd" with NO
    "_Animation" suffix (verified against the Animation Export folder):
        "1.2"  -> "step_id_1_2.usd"
        "15.3" -> "step_id_15_3.usd"
    """
    return f"step_id_{step_id.replace('.', '_')}.usd"


def _slug(value: str) -> str:
    slug = re.sub(r"[^0-9A-Za-z]+", "_", str(value)).strip("_").lower()
    return slug or "part"


def _read_nucleus_json(url: str) -> dict:
    result, _version, content = omni.client.read_file(_enc(url))
    if result != omni.client.Result.OK:
        raise RuntimeError(f"read_file failed for {url}: {result}")
    return json.loads(bytes(content).decode("utf-8"))


def _list_nucleus(url: str) -> list[str]:
    result, entries = omni.client.list(_enc(url))
    if result != omni.client.Result.OK:
        raise RuntimeError(f"list failed for {url}: {result}")
    return [e.relative_path for e in entries]


def _resolve_assembly_url(explicit: str, job_root: str) -> str:
    """Explicit URL wins; otherwise discover the single *.json in <job_root>/JSON."""
    if explicit:
        return explicit
    if not job_root:
        raise RuntimeError("Set DIREKT_ASSEMBLY_DEFINITION_URL or DIREKT_NUCLEUS_JOB_ROOT")
    json_dir = f"{job_root}/JSON"
    names = [n for n in _list_nucleus(json_dir) if n.lower().endswith(".json")]
    if len(names) != 1:
        raise RuntimeError(f"Expected exactly one .json in {json_dir}, found {names}")
    return f"{json_dir}/{names[0]}"


def _label_for(step: dict) -> str:
    """A human-ish, non-step_id part label used only for the GLB filename."""
    if step.get("component_type"):
        return step["component_type"]
    if step.get("step_type") == "screwing":
        return "screw"
    return step.get("step_type", "part")


def _iter_steps(defn: dict):
    """Yield (sequence_index, step_dict) in authoring order across all operations."""
    seq = 0
    for operation in defn.get("operations", []):
        for step in operation.get("steps", []):
            seq += 1
            yield seq, step


def _load_changed_urls() -> set[str]:
    """Watcher's pipe-separated changed-URL set. Empty = full rebuild."""
    raw = os.environ.get("LIVESYNC_CHANGED_URLS", "")
    if not raw:
        return set()
    return {u for u in raw.split("|") if u}


def _apply_converter_settings(context: Any) -> None:
    for key, value in CONVERTER_SETTINGS.items():
        if hasattr(context, key):
            setattr(context, key, value)
        else:
            log(f"[warn] AssetConverterContext has no '{key}', skipping")


async def _wait_for_stage_ready(ctx: Any, timeout: float = 15.0) -> bool:
    """Poll until the USD context reports OPENED, or bail on CLOSED/timeout.

    open_stage_async can return truthy while the background download/parse fails
    on a secondary thread (that is exactly the 'Could not download local file'
    then 'Stage busy' cascade). Gating flatten on the real stage state avoids
    flattening a half-open stage. Uses asyncio.sleep (fine under the pump loop);
    NOT next_update_async (that stalls -- see the header notes).
    """
    interval = 0.1
    elapsed = 0.0
    while elapsed < timeout:
        state = ctx.get_stage_state()
        if state == omni.usd.StageState.OPENED:
            return True
        if state == omni.usd.StageState.CLOSED and elapsed > 0.0:
            return False  # open didn't stick -> definitive failure, bail fast
        await asyncio.sleep(interval)
        elapsed += interval
    return False


async def export_glb(usd_url: str, output_glb_url: str, debug_name: str = "") -> tuple[bool, str]:
    """Open one animation USD, flatten locally, convert to a GLB on Nucleus."""
    ctx = omni.usd.get_context()

    # 1. Probe via omni.client BEFORE touching the USD resolver. omni.client is
    #    the channel that demonstrably reaches this Nucleus tree (it reads the
    #    JSON), so a stat here cleanly separates the two failure modes:
    #      * stat != OK          -> the file is missing / Nucleus unreachable
    #      * stat OK but open fails -> resolver cache / auth / resolver-only issue
    #    This turns "Could not download local file" from a guess into a fact.
    stat_result, entry = omni.client.stat(_enc(usd_url))
    if stat_result != omni.client.Result.OK:
        return False, (f"omni.client.stat={stat_result} -- file MISSING or unreachable "
                       f"on Nucleus (resolver never tried): {usd_url}")
    size = getattr(entry, "size", "?")
    print(f"    [probe] stat OK ({size} bytes) -> file exists on Nucleus", flush=True)

    # 2. Close any previously-open stage so a prior step's failure cannot leave
    #    the context 'Stage busy' and cascade into this open.
    if ctx.get_stage_state() != omni.usd.StageState.CLOSED:
        await ctx.close_stage_async()

    # 3. Open with LITERAL spaces. omni.client tolerates %20, but omni.usd's
    #    OmniUsdResolver cannot download a %20-encoded Nucleus path. Do NOT _enc()
    #    this URL.
    opened = await ctx.open_stage_async(usd_url)
    if not opened:
        await ctx.close_stage_async()
        return False, f"open_stage_async returned False for {usd_url}"

    # 4. open_stage_async can lie (return truthy while the async load fails).
    #    Gate on the real stage state before flattening; close on failure so the
    #    NEXT step starts from a clean CLOSED context (no cascade).
    if not await _wait_for_stage_ready(ctx):
        await ctx.close_stage_async()
        return False, (f"stage never reached OPENED (download/parse failed although "
                       f"the file exists) for {usd_url} -- likely stale resolver cache "
                       f"or the USD's references can't resolve")

    # 5. Report the two stage settings that drive the GLB's orientation and size.
    #    This exporter applies NO rotation and NO scale of its own -- the glTF
    #    converter derives both from the stage metadata below:
    #      upAxis=Z -> converter inserts a -90deg X rotation on the GLB root to
    #                  satisfy glTF's mandatory Y-up. Author the stage as
    #                  upAxis=Y and NO rotation is emitted (root stays identity).
    #      metersPerUnit -> drives the exported scale; glTF is always metres.
    #    NOTE: the converter reads the METADATA, not how the geometry looks. A
    #    stage whose meshes were rotated to "look" Y-up but still declares
    #    upAxis=Z will STILL get the -90deg X rotation (and end up tipped).
    try:
        from pxr import UsdGeom  # noqa: PLC0415 -- Kit-only, keep import local
        _stage = ctx.get_stage()
        print(f"    [probe] stage upAxis={UsdGeom.GetStageUpAxis(_stage)} "
              f"metersPerUnit={UsdGeom.GetStageMetersPerUnit(_stage)}", flush=True)
    except Exception as exc:  # noqa: BLE001 -- diagnostic only, never fail the export
        print(f"    [probe] could not read stage upAxis/metersPerUnit: {exc}", flush=True)

    # Flatten in local temp space for speed/stability.
    tmp_dir = Path(tempfile.mkdtemp(prefix="guidance_flat_"))
    flat_usd = tmp_dir / "flat.usd"
    flat_result = await ctx.export_as_stage_async(str(flat_usd))
    if isinstance(flat_result, tuple):
        ok, err = flat_result
    else:
        ok, err = bool(flat_result), "unknown"
    if not ok:
        return False, f"flatten failed: {err}"

    # Debug: keep the flattened stage so animation survival can be inspected.
    # Set DIREKT_KEEP_FLAT_DIR to a local folder to enable.
    keep_dir = os.environ.get("DIREKT_KEEP_FLAT_DIR", "").strip()
    if keep_dir and debug_name:
        import shutil
        dst = Path(keep_dir) / f"flat_{debug_name}.usd"
        try:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(flat_usd, dst)
            log(f"[debug] kept flattened stage -> {dst}")
        except Exception as exc:  # noqa: BLE001
            log(f"[debug] could not keep flat for {debug_name}: {exc}")

    conv_ctx = asset_converter.AssetConverterContext()
    _apply_converter_settings(conv_ctx)
    task = asset_converter.get_instance().create_converter_task(
        str(flat_usd), _enc(output_glb_url), None, conv_ctx
    )
    success = await task.wait_until_finished()
    if not success:
        return False, f"convert failed: {task.get_error_message()} (status {task.get_status()})"
    return True, output_glb_url


async def run() -> int:
    """Run the export. Returns a process exit code (0 ok, non-zero on failure)."""
    # --- Configuration from environment (set by pipeline_runner) ------------
    # Read required vars explicitly so a missing one gives a clear message
    # instead of a bare KeyError buried in the traceback.
    missing = [k for k in ("DIREKT_JOB_ID", "DIREKT_NUCLEUS_OUTPUT_ROOT")
               if not os.environ.get(k)]
    if missing:
        raise RuntimeError(f"Missing required env var(s): {', '.join(missing)}")

    job_id = os.environ["DIREKT_JOB_ID"]
    output_root = os.environ["DIREKT_NUCLEUS_OUTPUT_ROOT"].rstrip("/")
    job_root = os.environ.get("DIREKT_NUCLEUS_JOB_ROOT", "").rstrip("/")
    anim_dir = os.environ.get("DIREKT_ANIMATION_SOURCE_DIR", "").rstrip("/")
    if not anim_dir:
        if not job_root:
            raise RuntimeError("Set DIREKT_ANIMATION_SOURCE_DIR or DIREKT_NUCLEUS_JOB_ROOT")
        # The animation USDs live directly in the job root (the Animation Export folder).
        anim_dir = job_root
    assembly_url = _resolve_assembly_url(
        os.environ.get("DIREKT_ASSEMBLY_DEFINITION_URL", "").strip(), job_root
    )

    output_dir = f"{output_root}/{job_id}"
    definition = _read_nucleus_json(assembly_url)
    changed = _load_changed_urls()

    # A change to the assembly definition itself may add or re-flag steps, so a
    # plain incremental skip could miss a newly-added animation. Force a full
    # rebuild when the JSON is among the changed paths.
    if changed and (_enc(assembly_url) in changed or assembly_url in changed):
        log("=== assembly_definition.json changed -> full rebuild ===")
        changed = set()

    log(f"=== Job '{job_id}' ===")
    log(f"    assembly = {assembly_url}")
    log(f"    anim dir = {anim_dir}")
    log(f"    output   = {output_dir}")
    log("=== Full export ===" if not changed
        else f"=== Incremental: {len(changed)} changed URL(s) ===")

    report: list[dict[str, Any]] = []
    for seq, step in _iter_steps(definition):
        step_id = step["step_id"]
        instruction = step.get("instruction", "")
        step_type = step.get("step_type", "")
        is_animation = bool(step.get("is_animation", False))

        if not is_animation:
            report.append({
                "step_id": step_id, "part_id": "", "display_name": instruction,
                "instruction": instruction, "step_type": step_type,
                "is_animation": False, "sequence_index": seq,
                "glb_url": None, "ok": True, "skipped": False, "detail": "text-only",
            })
            log(f"TEXT {step_id}  {instruction}")
            continue

        part_id = f"{_slug(_label_for(step))}_{seq}"
        usd_url = f"{anim_dir}/{usd_name(step_id)}"
        output_glb_url = f"{output_dir}/{part_id}.glb"

        # Incremental: skip if this USD isn't in the changed set (reuse the GLB
        # already on Nucleus). Tolerant of %20 vs literal space.
        if changed and _enc(usd_url) not in changed and usd_url not in changed:
            log(f"SKIP {step_id} (unchanged) -> {output_glb_url}")
            report.append({
                "step_id": step_id, "part_id": part_id, "display_name": instruction,
                "instruction": instruction, "step_type": step_type,
                "is_animation": True, "sequence_index": seq,
                "glb_url": output_glb_url, "ok": True, "skipped": True,
                "detail": "unchanged; reusing existing GLB on Nucleus",
            })
            continue

        log(f"[{step_id}] Opening {usd_url}")
        try:
            ok, detail = await export_glb(usd_url, output_glb_url, debug_name=part_id)
        except Exception as exc:  # noqa: BLE001 -- one bad step must not abort the whole job
            import traceback
            ok, detail = False, f"exception: {exc}"
            traceback.print_exc()
        report.append({
            "step_id": step_id, "part_id": part_id, "display_name": instruction,
            "instruction": instruction, "step_type": step_type,
            "is_animation": True, "sequence_index": seq,
            "glb_url": output_glb_url if ok else None, "ok": ok,
            "skipped": False, "detail": detail,
        })
        log(f"{'OK  ' if ok else 'FAIL'} {step_id}  {part_id}  :: {detail}")

    # --- Write the export report to Nucleus --------------------------------
    report_url = f"{output_dir}/_export_report.json"
    payload = json.dumps({
        "job_id": job_id,
        "assembly_definition_url": assembly_url,
        "animation_source_dir": anim_dir,
        "steps": report,
    }, indent=2).encode("utf-8")
    result = omni.client.write_file(_enc(report_url), payload)
    log(f"{'Wrote' if result == omni.client.Result.OK else 'FAILED to write'} "
        f"report {report_url} ({result})")

    total = len(report)
    animation = sum(1 for r in report if r["is_animation"])
    exported = sum(1 for r in report if r["is_animation"] and r["ok"] and not r["skipped"])
    skipped = sum(1 for r in report if r.get("skipped"))
    failed = sum(1 for r in report if not r["ok"])
    log(f"\n=== Summary: steps={total} animation={animation} "
        f"exported={exported} skipped={skipped} failed={failed} ===")

    # Total-failure guard: we had animation work to do, produced nothing, and
    # didn't legitimately skip it -> this run accomplished nothing. Signal failure
    # so pipeline_runner halts BEFORE prepare_job reads this empty report and
    # ships a 0-animation manifest as if the run had succeeded.
    if animation > 0 and exported == 0 and skipped == 0:
        log("[export] ERROR: every animation step failed -- signalling failure "
            "so the pipeline does not ship an empty manifest.")
        return _EXIT_EXPORT_FAILED
    return _EXIT_OK


# --- Kit event-loop pump tail ---------------------------------------------
# The minimal direkt_export.kit quits the instant this --exec script returns,
# BEFORE the scheduled coroutine gets to run. is_running() is also False during
# --exec, so we cannot gate on it. Instead we pump app.update() until the task
# signals completion, then force-exit. os._exit() skips Python finalizers but is
# safe: every write to Nucleus above is already awaited/synchronous, and we flush
# stdout/stderr first.
#
# Exit codes (distinct so the log/orchestrator can tell modes apart):
#   0  run() completed (check the Summary line for exported/failed counts)
#   1  run() raised -- traceback is above the exit
#   2  timeout -- run() never signalled within the deadline (Nucleus/Hub stall?)
#   3  the coroutine never even started -- event loop not pumping (harness bug)

_EXIT_OK = 0
_EXIT_ERROR = 1
_EXIT_TIMEOUT = 2
_EXIT_NEVER_STARTED = 3
_EXIT_EXPORT_FAILED = 4   # run() completed but every animation step failed

_DEADLINE_SECONDS = float(os.environ.get("DIREKT_EXPORT_DEADLINE_SECONDS", "1200"))


async def _run_and_signal(state: dict) -> None:
    state["started"] = True
    code = _EXIT_OK
    try:
        code = await run()
    except Exception as exc:  # noqa: BLE001
        import traceback
        log(f"[export] FATAL: {exc}")
        traceback.print_exc()
        code = _EXIT_ERROR
    finally:
        state["code"] = code


import omni.kit.app  # noqa: E402

_state: dict = {"code": None, "started": False}
asyncio.ensure_future(_run_and_signal(_state))

_kit_app = omni.kit.app.get_app()
_start = time.time()
_deadline = _start + _DEADLINE_SECONDS
_next_heartbeat = _start + 15.0
_started_seen = False

while _state["code"] is None and time.time() < _deadline:
    _kit_app.update()
    now = time.time()
    if not _started_seen and _state.get("started"):
        _started_seen = True
        log("[export] coroutine started, pumping event loop...")
    if now >= _next_heartbeat:
        log(f"[export] still running... {int(now - _start)}s elapsed")
        _next_heartbeat = now + 15.0

# Decide the exit code.
if _state["code"] is not None:
    _exit_code = _state["code"]
elif not _state.get("started"):
    # The while loop ended (timeout) but the coroutine never even began: the
    # event loop wasn't being pumped. This is the "silent exit" class of bug.
    log("[export] ERROR: coroutine never started -- event loop was not pumped.")
    _exit_code = _EXIT_NEVER_STARTED
else:
    log(f"[export] ERROR: timed out after {int(time.time() - _start)}s "
        f"(deadline {_DEADLINE_SECONDS:.0f}s) -- run() never finished.")
    _exit_code = _EXIT_TIMEOUT

log(f"=== export process exiting with code {_exit_code} ===")
sys.stdout.flush()
sys.stderr.flush()
os._exit(_exit_code)
