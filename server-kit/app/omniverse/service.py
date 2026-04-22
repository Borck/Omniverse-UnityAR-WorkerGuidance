import omni.client
from typing import Optional
from fastapi import HTTPException
from app.core.config import SERVER

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
    result, entries = omni.client.list(f"{SERVER}{path}")
    if result != omni.client.Result.OK:
        raise HTTPException(status_code=500, detail=f"Cannot list {path}: {str(result)}")

    return [entry_to_dict(path, e) for e in entries]


def _recursive_list(path: str, items: list, ext_filter: Optional[str] = None):
    """Walk the tree recursively and collect all items."""
    result, entries = omni.client.list(f"{SERVER}{path}")
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
