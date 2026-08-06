import omni.client
from typing import Optional
from fastapi import HTTPException
from app.omniverse.nucleus_manager import get_manager
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FuturesTimeoutError

# ponytail: timeout for /folders and /files endpoints only.
# 50s because the Nucleus runs in Docker -- a cold connect can take 30-40s,
# and it occasionally drops and re-establishes. 15s was firing before it even
# finished connecting.
_NUCLEUS_TIMEOUT_SECONDS = 50
# A timed-out request leaves its thread blocked on the uncancellable
# omni.client.list call, so a small pool gets permanently exhausted after a
# few slow requests (the "fails after N refreshes" bug). 16 gives generous
# headroom for a single-admin dashboard; threads free as calls finally return.
# ponytail: bump higher only if a genuinely concurrent multi-user need appears.
_executor = ThreadPoolExecutor(max_workers=16)

# ─── Services ────────────────────────────────────────────────────────────────
def entry_to_dict(path: str, e) -> dict:
    """Convert a Nucleus list entry to a plain dict."""
    is_folder = bool(e.flags & omni.client.ItemFlags.CAN_HAVE_CHILDREN)
    return {
        "name": e.relative_path,
        "path": f"{path}/{e.relative_path}".replace("//", "/"),
        "type": "folder" if is_folder else "file",
        "size": e.size,
        "modified_time": str(e.modified_time),
    }


def _list(path: str) -> list:
    """List a single path, raise on failure."""
    url = f"{get_manager().active_server()}{path}"
    try:
        future = _executor.submit(omni.client.list, url)
        result, entries = future.result(timeout=_NUCLEUS_TIMEOUT_SECONDS)
    except FuturesTimeoutError:
        raise HTTPException(status_code=504, detail=f"Nucleus request timed out after {_NUCLEUS_TIMEOUT_SECONDS}s")

    if result != omni.client.Result.OK:
        raise HTTPException(status_code=500, detail=f"Cannot list {path}: {str(result)}")

    return [entry_to_dict(path, e) for e in entries]


def _recursive_list(path: str, items: list, ext_filter: Optional[str] = None):
    """Walk the tree recursively and collect all items."""
    result, entries = omni.client.list(f"{get_manager().active_server()}{path}")
    if result != omni.client.Result.OK:
        return
    for e in entries:
        is_folder = bool(e.flags & omni.client.ItemFlags.CAN_HAVE_CHILDREN)
        full_path = f"{path}/{e.relative_path}".replace("//", "/")
        item = {
            "name": e.relative_path,
            "path": full_path,
            "type": "folder" if is_folder else "file",
            "size": e.size,
            "modified_time": str(e.modified_time),
        }
        if ext_filter:
            if not is_folder and not e.relative_path.endswith(ext_filter):
                continue
        items.append(item)
        if is_folder:
            _recursive_list(full_path, items, ext_filter)
