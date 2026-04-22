import os
from typing import Optional

# Must happen BEFORE importing omni.client so Windows can find the DLLs
# os.add_dll_directory(r"C:\Users\shahan\VsCodeProjects\omniverse_connection")

# Set credentials before initializing
os.environ["OMNI_USER"] = "shahan"
os.environ["OMNI_PASS"] = "12345678"

import omni.client
from fastapi import FastAPI, HTTPException, APIRouter
from contextlib import asynccontextmanager
from app.omniverse.service import _list, _recursive_list
from app.core.config import SERVER


router = APIRouter(tags=["Omniverse Connection"])



# ─── Endpoints ──────────────────────────────────────────────────────────────

@router.get("/files", summary="List files and folders at a path")
def list_files(path: str = "/"):
    """List immediate children (files + folders) at the given path."""
    print(f"This is the file path I'm fetching: {path}")
    return {"path": path, "items": _list(path)}


@router.get("/folders", summary="List only folders at a path")
def list_folders(path: str = "/"):
    """List only sub-folders at the given path."""
    items = _list(path)
    folders = [i for i in items if i["type"] == "folder"]
    return {"path": path, "folders": folders}


@router.get("/tree", summary="Recursively list everything under a path")
def list_tree(path: str = "/"):
    """
    Recursively walk the entire tree under the given path.
    Warning: can be slow on large directories.
    """
    items = []
    _recursive_list(path, items)
    return {"path": path, "total": len(items), "items": items}


@router.get("/usd", summary="Find all USD files under a path")
def list_usd_files(path: str = "/"):
    """Recursively find all .usd / .usda / .usdc / .usdz files."""
    items = []
    _recursive_list(path, items)
    usd_exts = (".usd", ".usda", ".usdc", ".usdz")
    usd_files = [
        i for i in items
        if i["type"] == "file" and i["name"].lower().endswith(usd_exts)
    ]
    return {"path": path, "total": len(usd_files), "files": usd_files}


@router.get("/search", summary="Search for files by name keyword")
def search(path: str = "/", keyword: str = "", ext: str = ""):
    """
    Recursively search under `path` for items whose name contains `keyword`.
    Optionally filter by file extension e.g. ext=.usd
    """
    if not keyword and not ext:
        raise HTTPException(status_code=400, detail="Provide at least a keyword or ext")
    items = []
    _recursive_list(path, items)
    results = items
    if keyword:
        results = [i for i in results if keyword.lower() in i["name"].lower()]
    if ext:
        ext = ext if ext.startswith(".") else f".{ext}"
        results = [i for i in results if i["type"] == "file" and i["name"].lower().endswith(ext)]
    return {"path": path, "keyword": keyword, "ext": ext, "total": len(results), "results": results}


@router.get("/stat", summary="Get metadata for a single file or folder")
def stat_file(path: str):
    """Return metadata (size, modified time) for a specific path."""
    result, entry = omni.client.stat(f"{SERVER}{path}")
    if result != omni.client.Result.OK:
        raise HTTPException(status_code=404, detail=str(result))
    return {
        "path": path,
        "size": entry.size,
        "modified_time": str(entry.modified_time),
    }


@router.get("/download", summary="Copy a file from Nucleus to local disk")
def download(remote_path: str, local_path: str):
    """
    Download a file from Nucleus to the local machine.
    local_path can be a directory (filename is taken from remote_path) or a full file path.
    Example: remote_path=/Projects/scene.usd  local_path=C:/Users/shahan/Downloads/
    """
    # If local_path is a directory, append the filename from remote_path
    filename = remote_path.rstrip("/").split("/")[-1]
    if local_path.endswith("/") or local_path.endswith("\\"):
        local_path = local_path.rstrip("/\\") + "/" + filename

    # Normalize backslashes and build file:/// URL
    dst_url = "file:///" + local_path.replace("\\", "/")

    result = omni.client.copy(
        f"{SERVER}{remote_path}",
        dst_url,
        behavior=omni.client.CopyBehavior.OVERWRITE,
    )
    if result != omni.client.Result.OK:
        raise HTTPException(status_code=500, detail=str(result))
    return {"status": "ok", "downloaded_to": local_path}
