#!/usr/bin/env python3
"""Read-only preflight of native visual paths on the pinned Rogue Command build."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import uuid
from functools import lru_cache
from pathlib import Path

import dnfile
import UnityPy


EXPECTED_VERSION = "1.0.9"
EXPECTED_ASSEMBLY_SHA256 = "d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b"
EXPECTED_MVID = "db0e9462-128d-4296-9900-de18b4bd0c00"
EXPECTED_RESOURCES_SHA256 = "c751bdeb3c1190b8f9fe80d58dd3b99e5a28130e37df41b93f9d8698a49c6490"


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_identity(game_root: Path) -> Path:
    version_text = (game_root / "version.txt").read_text(encoding="utf-8-sig")
    match = re.search(r"\bVersion\s+([0-9]+\.[0-9]+\.[0-9]+)\b", version_text)
    version = match.group(1) if match else None
    assembly = game_root / "Rogue Command_Data/Managed/Assembly-CSharp.dll"
    assembly_hash = sha256(assembly)
    pe = dnfile.dnPE(str(assembly))
    try:
        mvid = str(uuid.UUID(bytes_le=pe.net.mdtables.Module.rows[0].Mvid.value))
    finally:
        pe.close()
    actual = (version, assembly_hash, mvid)
    expected = (EXPECTED_VERSION, EXPECTED_ASSEMBLY_SHA256, EXPECTED_MVID)
    if actual != expected:
        raise ValueError(f"unsupported compatibility identity: version={version}, SHA256={assembly_hash}, MVID={mvid}")
    resources = game_root / "Rogue Command_Data/resources.assets"
    resource_hash = sha256(resources)
    if resource_hash != EXPECTED_RESOURCES_SHA256:
        raise ValueError(f"resources.assets changed: SHA256={resource_hash}; re-inspect native prefab locations")
    print(f"Identity OK: Rogue Command {version}, Assembly SHA256={assembly_hash}, MVID={mvid}")
    return resources


class NativeHierarchy:
    def __init__(self, resources: Path):
        self.environment = UnityPy.load(str(resources))
        self.objects = {obj.path_id: obj for obj in self.environment.objects}
        self.transforms: dict[int, int] = {}
        self.candidates: dict[str, list[int]] = {}
        for obj in self.objects.values():
            if self.kind(obj.path_id) != "GameObject":
                continue
            data = self.data(obj.path_id)
            components = self.components(obj.path_id)
            transforms = [cid for cid in components if self.kind(cid) in ("Transform", "RectTransform")]
            if len(transforms) != 1:
                continue
            self.transforms[obj.path_id] = transforms[0]
            # Render-only nested model objects can share the gameplay prefab name.
            if any(self.kind(cid) == "MonoBehaviour" for cid in components):
                self.candidates.setdefault(data.get("m_Name", ""), []).append(obj.path_id)

    def kind(self, path_id: int) -> str:
        return str(self.objects[path_id].type.name)

    @lru_cache(maxsize=None)
    def data(self, path_id: int) -> dict:
        return self.objects[path_id].parse_as_dict()

    def components(self, game_object_id: int) -> list[int]:
        return [int(item["component"]["m_PathID"]) for item in self.data(game_object_id).get("m_Component", [])]

    def children(self, game_object_id: int) -> list[int]:
        transform = self.data(self.transforms[game_object_id])
        children = []
        for reference in transform.get("m_Children", []):
            if int(reference.get("m_FileID", 0)) != 0:
                raise ValueError("external Transform reference requires runtime verification")
            child = self.data(int(reference["m_PathID"]))
            children.append(int(child["m_GameObject"]["m_PathID"]))
        return children

    def check(self, template: str, path: str) -> tuple[int, int, int]:
        candidates = self.candidates.get(template, [])
        if len(candidates) != 1:
            raise ValueError(f"native template {template!r} has {len(candidates)} scripted candidates; cannot select uniquely")
        root = candidates[0]
        current = root
        segments = path.split("/")
        if any(segment in ("", ".", "..") for segment in segments) or "\\" in path:
            raise ValueError(f"invalid relative Transform path {path!r}")
        for segment in segments:
            matches = [gid for gid in self.children(current) if self.data(gid).get("m_Name") == segment]
            if len(matches) != 1:
                raise ValueError(f"{template}/{path}: segment {segment!r} has {len(matches)} direct-child matches")
            current = matches[0]
        renderer_count = 0
        pending = [current]
        seen: set[int] = set()
        while pending:
            gid = pending.pop()
            if gid in seen:
                raise ValueError(f"cycle or shared Transform in {template}/{path}")
            seen.add(gid)
            renderer_count += sum(self.kind(cid).endswith("Renderer") for cid in self.components(gid))
            pending.extend(self.children(gid))
        if renderer_count == 0:
            raise ValueError(f"{template}/{path}: target subtree contains no Renderer")
        return root, current, renderer_count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pack", type=Path, help="Directory containing content.json")
    parser.add_argument("--game-root", type=Path, default=Path(r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command"))
    args = parser.parse_args()
    try:
        pack = json.loads((args.pack / "content.json").read_text(encoding="utf-8-sig"))
        resources = verify_identity(args.game_root)
        hierarchy = NativeHierarchy(resources)
        failures = checked = 0
        for unit in pack.get("units") or []:
            if not unit.get("visualRootPath"):
                continue
            checked += 1
            try:
                root, target, renderers = hierarchy.check(unit.get("nativeTemplateId", ""), unit["visualRootPath"])
                print(f"OK {unit['id']}: {unit['nativeTemplateId']}/{unit['visualRootPath']} (root={root}, target={target}, renderers={renderers})")
            except (KeyError, ValueError) as exc:
                failures += 1
                print(f"ERROR {unit.get('id', '<missing-id>')}: {exc}", file=sys.stderr)
        print(f"Static native visual preflight: {checked} path(s), {failures} error(s). Runtime resource selection and rendering remain separate checks.")
        return 1 if failures else 0
    except Exception as exc:
        print(f"Cannot inspect native visual paths: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
