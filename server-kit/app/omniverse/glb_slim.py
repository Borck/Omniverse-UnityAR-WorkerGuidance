"""Strip unused vertex attributes from exported GLBs before they are published.

Why this exists
---------------
The Omniverse glTF converter exports every mesh *primvar* it finds. For this
project that means each GLB carries TEXCOORD_0 (UVs, from `primvars:st`) and
COLOR_0 (from `primvars:displayColor`) -- even though the exporter runs with
`ignore_materials: True` and the Unity client's HologramApplier replaces every
material with the hologram shader. Neither attribute is ever read.

These are NOT textures: the GLBs contain zero images/textures/materials.
`ignore_materials` correctly suppressed those. UVs and vertex colours are mesh
vertex attributes, not material data, so the converter exports them as geometry.

On the large assemblies that dead weight is ~36% of the file (~36 MB of a 98 MB
GLB), so removing it is a large, entirely lossless win for this pipeline.

How it works
------------
Deleting an attribute from a primitive is not enough -- the vertex data would
stay stranded in the binary chunk and the file would not shrink. So we remove
the attribute references, then garbage-collect every accessor and bufferView
that became unreferenced and rebuild the binary chunk from only what survives.

Deliberately dependency-free (stdlib only): this runs inside Kit's bundled
Python during prepare_job, where installing packages is not an option.

Safety
------
Shrinking an asset must never be able to break the pipeline. Every failure path
returns the ORIGINAL bytes untouched with `stats["error"]` set, so a malformed
or unexpected GLB is published as-is rather than corrupted or dropped.
"""

from __future__ import annotations

import json
import struct
from typing import Any, Iterable

_GLB_MAGIC = 0x46546C67   # 'glTF'
_CHUNK_JSON = 0x4E4F534A  # 'JSON'
_CHUNK_BIN = 0x004E4942   # 'BIN\0'

# Attributes this project never reads. See the module docstring.
DEFAULT_DROP: tuple[str, ...] = ("TEXCOORD_0", "COLOR_0")


def _pad4(n: int) -> int:
    """Bytes needed to reach the next 4-byte boundary."""
    return (4 - (n % 4)) % 4


def _parse_glb(data: bytes) -> tuple[dict, bytes]:
    """Split a GLB into its parsed JSON chunk and raw BIN chunk."""
    if len(data) < 12:
        raise ValueError("too short to be a GLB")
    magic, version, _total = struct.unpack("<III", data[:12])
    if magic != _GLB_MAGIC:
        raise ValueError("not a GLB (bad magic)")
    if version != 2:
        raise ValueError(f"unsupported glTF binary version {version}")

    off = 12
    gltf: dict | None = None
    binary = b""
    # Per spec chunkLength already includes the chunk's own 4-byte padding.
    while off + 8 <= len(data):
        clen, ctype = struct.unpack("<II", data[off:off + 8])
        off += 8
        chunk = data[off:off + clen]
        off += clen
        if ctype == _CHUNK_JSON:
            gltf = json.loads(chunk.decode("utf-8"))
        elif ctype == _CHUNK_BIN:
            binary = chunk

    if gltf is None:
        raise ValueError("GLB has no JSON chunk")
    return gltf, binary


def _referenced_accessors(gltf: dict) -> set[int]:
    """Every accessor index still reachable after attribute removal."""
    used: set[int] = set()
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            used.update(prim.get("attributes", {}).values())
            if prim.get("indices") is not None:
                used.add(prim["indices"])
            for target in prim.get("targets") or []:
                used.update(target.values())
    for anim in gltf.get("animations", []):
        for sampler in anim.get("samplers", []):
            used.add(sampler["input"])
            used.add(sampler["output"])
    for skin in gltf.get("skins", []):
        if skin.get("inverseBindMatrices") is not None:
            used.add(skin["inverseBindMatrices"])
    return used


def strip_vertex_attributes(
    glb_bytes: bytes,
    drop: Iterable[str] = DEFAULT_DROP,
) -> tuple[bytes, dict[str, Any]]:
    """Remove the named vertex attributes and repack the GLB.

    Returns (new_bytes, stats). `stats` carries bytes_before / bytes_after /
    removed / changed / error. On ANY problem the input bytes are returned
    unchanged with `changed=False` and `error` describing why.
    """
    stats: dict[str, Any] = {
        "changed": False,
        "bytes_before": len(glb_bytes),
        "bytes_after": len(glb_bytes),
        "removed": [],
        "error": "",
    }
    drop_set = {d.upper() for d in drop}

    try:
        gltf, binary = _parse_glb(glb_bytes)

        # Buffers stored outside the GLB aren't handled; leave such files alone.
        for buf in gltf.get("buffers", []):
            if buf.get("uri"):
                stats["error"] = "external buffer uri; left unchanged"
                return glb_bytes, stats

        # --- 1. drop the attribute references ----------------------------
        removed: set[str] = set()
        for mesh in gltf.get("meshes", []):
            for prim in mesh.get("primitives", []):
                for name in list(prim.get("attributes", {})):
                    if name.upper() in drop_set:
                        del prim["attributes"][name]
                        removed.add(name)
                for target in prim.get("targets") or []:
                    for name in list(target):
                        if name.upper() in drop_set:
                            del target[name]
                            removed.add(name)

        if not removed:
            return glb_bytes, stats  # nothing to do; not an error

        # --- 2. garbage-collect accessors ---------------------------------
        old_accessors = gltf.get("accessors", [])
        keep_acc = sorted(
            i for i in _referenced_accessors(gltf) if 0 <= i < len(old_accessors)
        )
        acc_map = {old: new for new, old in enumerate(keep_acc)}

        # --- 3. garbage-collect bufferViews -------------------------------
        old_views = gltf.get("bufferViews", [])
        used_views: set[int] = set()
        for i in keep_acc:
            bv = old_accessors[i].get("bufferView")
            if bv is not None:
                used_views.add(bv)
        for img in gltf.get("images", []):
            if img.get("bufferView") is not None:
                used_views.add(img["bufferView"])
        keep_views = sorted(v for v in used_views if 0 <= v < len(old_views))
        view_map = {old: new for new, old in enumerate(keep_views)}

        # --- 4. rebuild the binary chunk from surviving views only ---------
        # Copy each kept bufferView verbatim so accessor.byteOffset (which is
        # relative to its view) stays valid, and any byteStride is preserved.
        new_bin = bytearray()
        new_views: list[dict] = []
        for old_idx in keep_views:
            v = old_views[old_idx]
            start = v.get("byteOffset", 0)
            length = v["byteLength"]
            new_bin.extend(b"\x00" * _pad4(len(new_bin)))
            nv: dict[str, Any] = {
                "buffer": 0,
                "byteOffset": len(new_bin),
                "byteLength": length,
            }
            new_bin.extend(binary[start:start + length])
            for key in ("byteStride", "target", "name"):
                if key in v:
                    nv[key] = v[key]
            new_views.append(nv)

        # --- 5. remap every index that survived ---------------------------
        new_accessors: list[dict] = []
        for old_idx in keep_acc:
            a = dict(old_accessors[old_idx])
            if a.get("bufferView") is not None:
                a["bufferView"] = view_map[a["bufferView"]]
            new_accessors.append(a)

        for mesh in gltf.get("meshes", []):
            for prim in mesh.get("primitives", []):
                prim["attributes"] = {
                    k: acc_map[v] for k, v in prim.get("attributes", {}).items()
                }
                if prim.get("indices") is not None:
                    prim["indices"] = acc_map[prim["indices"]]
                if prim.get("targets"):
                    prim["targets"] = [
                        {k: acc_map[v] for k, v in t.items()} for t in prim["targets"]
                    ]
        for anim in gltf.get("animations", []):
            for sampler in anim.get("samplers", []):
                sampler["input"] = acc_map[sampler["input"]]
                sampler["output"] = acc_map[sampler["output"]]
        for skin in gltf.get("skins", []):
            if skin.get("inverseBindMatrices") is not None:
                skin["inverseBindMatrices"] = acc_map[skin["inverseBindMatrices"]]
        for img in gltf.get("images", []):
            if img.get("bufferView") is not None:
                img["bufferView"] = view_map[img["bufferView"]]

        gltf["accessors"] = new_accessors
        gltf["bufferViews"] = new_views
        gltf["buffers"] = [{"byteLength": len(new_bin)}] if new_bin else []

        # --- 6. re-serialize ----------------------------------------------
        json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
        json_bytes += b" " * _pad4(len(json_bytes))   # JSON pads with spaces
        bin_bytes = bytes(new_bin)
        bin_bytes += b"\x00" * _pad4(len(bin_bytes))  # BIN pads with zeros

        total = 12 + 8 + len(json_bytes) + ((8 + len(bin_bytes)) if bin_bytes else 0)
        out = bytearray()
        out.extend(struct.pack("<III", _GLB_MAGIC, 2, total))
        out.extend(struct.pack("<II", len(json_bytes), _CHUNK_JSON))
        out.extend(json_bytes)
        if bin_bytes:
            out.extend(struct.pack("<II", len(bin_bytes), _CHUNK_BIN))
            out.extend(bin_bytes)

        result = bytes(out)
        # Never publish a "shrink" that grew the file.
        if len(result) >= len(glb_bytes):
            stats["error"] = "repack did not reduce size; left unchanged"
            return glb_bytes, stats

        stats["changed"] = True
        stats["bytes_after"] = len(result)
        stats["removed"] = sorted(removed)
        return result, stats

    except Exception as exc:  # noqa: BLE001 -- must never break the pipeline
        stats["error"] = f"{type(exc).__name__}: {exc}"
        return glb_bytes, stats
