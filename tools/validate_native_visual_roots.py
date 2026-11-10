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
    def __init__(self, serialized_asset: Path):
        self.environment = UnityPy.load(str(serialized_asset))
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

    def resolve_template_root(self, template: str) -> int:
        candidates = self.candidates.get(template, [])
        if len(candidates) != 1:
            raise ValueError(f"native template {template!r} has {len(candidates)} scripted candidates; cannot select uniquely")
        return candidates[0]

    def resolve_path(self, root: int, path: str, label: str) -> int:
        if not isinstance(path, str) or not path or path.strip() != path:
            raise ValueError(f"{label}: Transform path must be a non-empty, trimmed string")
        current = root
        segments = path.split("/")
        if any(segment in ("", ".", "..") for segment in segments) or "\\" in path:
            raise ValueError(f"{label}: invalid relative Transform path {path!r}")
        for segment in segments:
            matches = [gid for gid in self.children(current) if self.data(gid).get("m_Name") == segment]
            if len(matches) != 1:
                raise ValueError(f"{label}: segment {segment!r} has {len(matches)} direct-child matches")
            current = matches[0]
        return current

    def check(self, template: str, path: str) -> tuple[int, int, int]:
        root = self.resolve_template_root(template)
        current = self.resolve_path(root, path, f"{template}/{path}")
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


class BundleHierarchy(NativeHierarchy):
    """Transform lookup rooted at an exact GameObject entry in an AssetBundle."""

    def __init__(self, bundle_path: Path):
        super().__init__(bundle_path)
        self.container_roots: dict[str, list[int]] = {}
        for serialized_file in self.environment.files.values():
            for name, object_reader in getattr(serialized_file, "container", {}).items():
                if getattr(getattr(object_reader, "type", None), "name", None) != "GameObject":
                    continue
                self.container_roots.setdefault(str(name).replace("\\", "/"), []).append(
                    int(object_reader.path_id)
                )

    def resolve_prefab_root(self, asset_name: str) -> int:
        roots = self.container_roots.get(asset_name, [])
        if len(roots) != 1:
            raise ValueError(f"bundle prefab {asset_name!r} has {len(roots)} exact GameObject container entries")
        root = roots[0]
        if root not in self.transforms:
            raise ValueError(f"bundle prefab {asset_name!r} has no unique Transform component")
        return root


def safe_pack_file(pack_root: Path, relative_path: str) -> Path:
    if not isinstance(relative_path, str) or not relative_path or "\\" in relative_path or ":" in relative_path:
        raise ValueError(f"invalid pack-relative file path: {relative_path!r}")
    parts = relative_path.split("/")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"pack-relative path cannot traverse directories: {relative_path!r}")
    candidate = pack_root.joinpath(*parts)
    resolved = candidate.resolve(strict=True)
    if pack_root not in resolved.parents or not resolved.is_file():
        raise ValueError(f"pack-relative path escapes the pack or is not a file: {relative_path!r}")
    current = candidate
    while current != pack_root.parent:
        if current.is_symlink():
            raise ValueError(f"pack-relative path cannot contain symlinks: {relative_path!r}")
        current = current.parent
    return resolved


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pack", type=Path, help="Directory containing content.json")
    parser.add_argument("--game-root", type=Path, default=Path(r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command"))
    args = parser.parse_args()
    try:
        pack_root = args.pack.resolve(strict=True)
        pack = json.loads((pack_root / "content.json").read_text(encoding="utf-8-sig"))
        resources = verify_identity(args.game_root)
        hierarchy = NativeHierarchy(resources)
        bundle_entries = {
            entry.get("id"): entry
            for entry in pack.get("assetBundles") or []
            if isinstance(entry, dict) and isinstance(entry.get("id"), str)
        }
        bundle_hierarchies: dict[Path, BundleHierarchy] = {}
        failures = checked_roots = checked_mappings = 0
        for unit in pack.get("units") or []:
            retarget = unit.get("nativeRigRetarget")
            if not unit.get("visualRootPath"):
                if retarget is None:
                    continue
                failures += 1
                print(
                    f"ERROR {unit.get('id', '<missing-id>')}: nativeRigRetarget requires visualRootPath",
                    file=sys.stderr,
                )
                continue
            checked_roots += 1
            try:
                root, target, renderers = hierarchy.check(
                    unit.get("nativeTemplateId", ""), unit["visualRootPath"]
                )
                print(
                    f"OK {unit['id']}: {unit['nativeTemplateId']}/{unit['visualRootPath']} "
                    f"(root={root}, target={target}, renderers={renderers})"
                )
            except (KeyError, ValueError) as exc:
                failures += 1
                print(f"ERROR {unit.get('id', '<missing-id>')}: {exc}", file=sys.stderr)

            if retarget is None:
                continue
            if not isinstance(retarget, dict) or not isinstance(retarget.get("mappings"), list):
                failures += 1
                print(f"ERROR {unit.get('id', '<missing-id>')}: nativeRigRetarget.mappings must be an array", file=sys.stderr)
                continue

            mappings = retarget["mappings"]
            checked_mappings += len(mappings)
            try:
                if not 1 <= len(mappings) <= 64:
                    raise ValueError(f"nativeRigRetarget has {len(mappings)} mappings; expected 1..64")
                template = unit.get("nativeTemplateId", "")
                native_root = hierarchy.resolve_template_root(template)
                visual_root_path = unit.get("visualRootPath", "")
                visual_root = hierarchy.resolve_path(
                    native_root, visual_root_path, f"{unit.get('id', '<missing-id>')} visualRootPath"
                )
                bundle_id = unit.get("assetBundleId")
                bundle_entry = bundle_entries.get(bundle_id)
                if not isinstance(bundle_entry, dict) or not isinstance(bundle_entry.get("path"), str):
                    raise ValueError(f"assetBundleId {bundle_id!r} does not resolve to a declared bundle")
                if not isinstance(unit.get("visualAssetName"), str) or not unit["visualAssetName"]:
                    raise ValueError("nativeRigRetarget requires visualAssetName")
                bundle_path = safe_pack_file(pack_root, bundle_entry["path"])
                bundle_hierarchy = bundle_hierarchies.get(bundle_path)
                if bundle_hierarchy is None:
                    bundle_hierarchy = BundleHierarchy(bundle_path)
                    bundle_hierarchies[bundle_path] = bundle_hierarchy
                visual_prefab_root = bundle_hierarchy.resolve_prefab_root(unit["visualAssetName"])
            except (KeyError, OSError, TypeError, ValueError) as exc:
                failures += 1
                print(f"ERROR {unit.get('id', '<missing-id>')} retarget setup: {exc}", file=sys.stderr)
                continue

            seen_targets: set[str] = set()
            for index, mapping in enumerate(mappings):
                label = f"{unit.get('id', '<missing-id>')} retarget[{index}]"
                try:
                    if not isinstance(mapping, dict):
                        raise ValueError("mapping entry must be an object")
                    source_path = mapping.get("sourcePath")
                    target_path = mapping.get("targetPath")
                    if not isinstance(source_path, str) or not isinstance(target_path, str):
                        raise ValueError("sourcePath and targetPath must be strings")
                    if target_path in seen_targets:
                        raise ValueError(f"duplicate targetPath {target_path!r}")
                    seen_targets.add(target_path)
                    if source_path == unit["visualRootPath"] or unit["visualRootPath"].startswith(source_path + "/"):
                        raise ValueError("sourcePath cannot equal or contain visualRootPath")
                    source = hierarchy.resolve_path(native_root, source_path, label + " sourcePath")
                    target = bundle_hierarchy.resolve_path(
                        visual_prefab_root, target_path, label + " targetPath"
                    )
                    print(
                        f"OK {label}: {source_path} -> {target_path} "
                        f"(source={source}, target={target}, visualRoot={visual_root})"
                    )
                except (KeyError, ValueError) as exc:
                    failures += 1
                    print(f"ERROR {label}: {exc}", file=sys.stderr)

        print(
            f"Static native visual preflight: {checked_roots} visual root path(s), "
            f"{checked_mappings} retarget mapping(s), {failures} error(s). "
            "Runtime resource selection and rendering remain separate checks."
        )
        return 1 if failures else 0
    except Exception as exc:
        print(f"Cannot inspect native visual paths: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
