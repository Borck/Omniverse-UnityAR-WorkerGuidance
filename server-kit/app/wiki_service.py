"""Builds a graph + search index over the repo's markdown docs.

No LLM involved: markdown links are already exact, machine-readable syntax
([text](path.md)), so a regex parses them correctly every time. An LLM would
be strictly worse here -- slower, costs money, and non-deterministic -- for
something regex already gets exactly right.

Scans docs/ and openwiki/ (the latter doesn't exist until OpenWiki is set up
to auto-generate docs -- listed here so it's picked up automatically once it
does, no code change needed) plus a few root-level docs.
"""

from __future__ import annotations

import re
from pathlib import Path

_LINK_RE = re.compile(r"\[([^\]]*)\]\(([^)]+\.md)(?:#[^)]*)?\)")
_TITLE_RE = re.compile(r"^#\s+(.+)$", re.MULTILINE)

_DOC_ROOTS = ["docs", "openwiki"]
_ROOT_FILES = ["README.md", "Plan.md", "ToDo.md"]


def build_wiki_index(repo_root: Path) -> dict:
    """Returns {"nodes": [{id, title, content}], "edges": [{source, target}]}."""
    files: dict[str, dict] = {}

    candidates: list[Path] = []
    for root_name in _DOC_ROOTS:
        root_dir = repo_root / root_name
        if root_dir.is_dir():
            candidates.extend(root_dir.rglob("*.md"))
    for name in _ROOT_FILES:
        p = repo_root / name
        if p.is_file():
            candidates.append(p)

    for path in candidates:
        rel = path.relative_to(repo_root).as_posix()
        try:
            text = path.read_text(encoding="utf-8")
        except OSError:
            continue
        title_match = _TITLE_RE.search(text)
        title = title_match.group(1).strip() if title_match else path.stem
        files[rel] = {"title": title, "content": text}

    nodes = [
        {"id": rel, "title": data["title"], "content": data["content"]}
        for rel, data in files.items()
    ]

    edges = []
    seen_edges: set[tuple[str, str]] = set()
    for rel, data in files.items():
        source_dir = (repo_root / rel).parent
        for _link_text, link_path in _LINK_RE.findall(data["content"]):
            if link_path.startswith(("http://", "https://")):
                continue
            resolved = (source_dir / link_path).resolve()
            try:
                target_rel = resolved.relative_to(repo_root.resolve()).as_posix()
            except ValueError:
                continue
            if target_rel not in files or target_rel == rel:
                continue
            edge_key = (rel, target_rel)
            if edge_key in seen_edges:
                continue
            seen_edges.add(edge_key)
            edges.append({"source": rel, "target": target_rel})

    return {"nodes": nodes, "edges": edges}


if __name__ == "__main__":
    # ponytail: smallest useful self-check -- run this file directly to sanity
    # check the parser against the real repo without booting the whole server.
    repo_root = Path(__file__).resolve().parents[2]
    index = build_wiki_index(repo_root)
    print(f"nodes: {len(index['nodes'])}  edges: {len(index['edges'])}")
    assert len(index["nodes"]) > 0, "expected at least one doc file"
    assert len(index["edges"]) > 0, "expected at least one resolved link"
    for edge in index["edges"][:5]:
        print(f"  {edge['source']} -> {edge['target']}")
