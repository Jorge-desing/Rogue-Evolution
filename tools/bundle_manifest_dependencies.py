"""Read a Unity AssetBundleManifest and resolve its transitive bundle dependencies."""
from __future__ import annotations

from pathlib import Path
import copy


def resolve_bundle_dependencies(manifest_path: str | Path | bytes, bundle_names: list[str], *, require_exact: bool = False) -> list[str]:
    """Return the selected bundles and dependencies, failing on incomplete metadata."""
    import UnityPy

    # Loading from a path can keep UnityPy's mmap open on Windows and block
    # renaming or cleaning the staged directory. Read and close first.
    data = manifest_path if isinstance(manifest_path, bytes) else Path(manifest_path).read_bytes()
    environment = UnityPy.load(data)
    manifests = [obj for obj in environment.objects if obj.type.name == "AssetBundleManifest"]
    if len(manifests) != 1:
        raise ValueError("El bundle debe contener exactamente un AssetBundleManifest")
    tree = manifests[0].read_typetree()
    names = dict(tree["AssetBundleNames"])
    infos = dict(tree["AssetBundleInfos"])
    if len(names) != len(tree["AssetBundleNames"]) or len(set(names.values())) != len(names):
        raise ValueError("Nombres o índices de bundle duplicados en el manifest")
    if set(names) != set(infos):
        raise ValueError("El manifest contiene índices de bundle incompletos")
    name_to_index = {name: index for index, name in names.items()}
    missing = sorted(set(bundle_names) - set(name_to_index))
    if missing:
        raise ValueError("Bundles ausentes del manifest: " + ", ".join(missing))
    if require_exact and (len(bundle_names) != len(set(bundle_names)) or set(bundle_names) != set(name_to_index)):
        raise ValueError("Las declaraciones no coinciden exactamente con el manifest de Unity")
    visiting: set[int] = set()
    visited: set[int] = set()
    ordered: list[str] = []

    def visit(index: int) -> None:
        if index in visiting:
            raise ValueError("Dependencias de AssetBundles cíclicas")
        if index in visited:
            return
        if index not in infos:
            raise ValueError(f"Índice de dependencia desconocido: {index}")
        visiting.add(index)
        for dependency in infos[index]["AssetBundleDependencies"]:
            visit(dependency)
        visiting.remove(index)
        visited.add(index)
        ordered.append(names[index])

    for name in bundle_names:
        visit(name_to_index[name])
    return ordered


def merge_bundle_manifests(manifest_paths: list[str | Path | bytes]) -> bytes:
    """Combine disjoint Unity manifest tables, preserving hashes and dependency edges."""
    import UnityPy

    if not manifest_paths:
        raise ValueError("Se requiere al menos un manifest")
    environments = [UnityPy.load(path if isinstance(path, bytes) else Path(path).read_bytes()) for path in manifest_paths]
    objects = [[obj for obj in env.objects if obj.type.name == "AssetBundleManifest"] for env in environments]
    if any(len(group) != 1 for group in objects):
        raise ValueError("Cada bundle debe contener exactamente un AssetBundleManifest")
    trees = [group[0].read_typetree() for group in objects]
    combined = copy.deepcopy(trees[0])
    combined["AssetBundleNames"] = []
    combined["AssetBundleInfos"] = []
    combined["AssetBundlesWithVariant"] = []
    known_names: set[str] = set()
    next_index = 0
    for tree in trees:
        names = dict(tree["AssetBundleNames"])
        infos = dict(tree["AssetBundleInfos"])
        if len(names) != len(tree["AssetBundleNames"]) or len(infos) != len(tree["AssetBundleInfos"]) or set(names) != set(infos):
            raise ValueError("Índices de bundle duplicados o incompletos")
        active: set[int] = set()
        done: set[int] = set()

        def check(index: int) -> None:
            if index in active:
                raise ValueError("Dependencias de AssetBundles cíclicas")
            if index in done:
                return
            if index not in infos:
                raise ValueError(f"Índice de dependencia desconocido: {index}")
            active.add(index)
            for dependency in infos[index]["AssetBundleDependencies"]:
                check(dependency)
            active.remove(index)
            done.add(index)

        for index in names:
            check(index)
        mapping = {}
        for old_index, name in tree["AssetBundleNames"]:
            if not isinstance(name, str) or not name or name in known_names:
                raise ValueError(f"Nombre de bundle duplicado o inválido: {name!r}")
            known_names.add(name)
            mapping[old_index] = next_index
            combined["AssetBundleNames"].append((next_index, name))
            next_index += 1
        for old_index, value in tree["AssetBundleInfos"]:
            info = copy.deepcopy(value)
            try:
                info["AssetBundleDependencies"] = [mapping[dependency] for dependency in info["AssetBundleDependencies"]]
            except KeyError as exc:
                raise ValueError(f"Dependencia de bundle desconocida: {exc}") from exc
            combined["AssetBundleInfos"].append((mapping[old_index], info))
        try:
            combined["AssetBundlesWithVariant"].extend(mapping[index] for index in tree["AssetBundlesWithVariant"])
        except KeyError as exc:
            raise ValueError(f"Variante de bundle desconocida: {exc}") from exc
    objects[0][0].save_typetree(combined)
    return environments[0].file.save()
