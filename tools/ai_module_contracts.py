"""Read the compiled AI module IDs from the loader's C# registration table."""
from __future__ import annotations

import re
from pathlib import Path


class AiModuleContractError(RuntimeError):
    """Raised when the source registry cannot be resolved without guessing."""


def registered_ai_module_ids() -> frozenset[str]:
    """Return module IDs registered by ``AiModuleRegistry.CreateDefault``.

    The Python content auditors used to keep separate, hand-maintained lists.
    Reading the small C# registration surface here prevents those lists from
    silently falling behind the loader when a compiled module is added.
    """
    repository_root = Path(__file__).resolve().parents[1]
    modules_root = repository_root / "src" / "RogueCommandExpansionLoader" / "Modules"
    registry_path = modules_root / "AiModuleRegistry.cs"
    if not registry_path.is_file():
        raise AiModuleContractError(f"AI registry source not found: {registry_path}")

    registry_text = registry_path.read_text(encoding="utf-8-sig")
    module_types = re.findall(
        r"\bmodules\.Register\s*\(\s*new\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        registry_text,
    )
    if not module_types:
        raise AiModuleContractError(
            f"No module registrations found in {registry_path}"
        )

    ids: list[str] = []
    for module_type in module_types:
        module_path = modules_root / f"{module_type}.cs"
        if not module_path.is_file():
            raise AiModuleContractError(
                f"Registered AI module source not found: {module_path}"
            )
        module_text = module_path.read_text(encoding="utf-8-sig")

        direct_id = re.search(
            r"\b(?:public|internal)\s+const\s+string\s+ModuleId\s*=\s*\"([^\"]+)\"\s*;",
            module_text,
        )
        if direct_id:
            ids.append(direct_id.group(1))
            continue

        parser_ref = re.search(
            r"\b(?:public|internal)\s+const\s+string\s+ModuleId\s*=\s*"
            r"([A-Za-z_][A-Za-z0-9_]*)\.ModuleId\s*;",
            module_text,
        )
        if not parser_ref:
            raise AiModuleContractError(
                f"Could not resolve ModuleId in registered type {module_type}"
            )

        parser_type = parser_ref.group(1)
        parser_pattern = re.compile(
            r"\bclass\s+" + re.escape(parser_type) + r"\b"
        )
        parser_sources = [
            path
            for path in modules_root.glob("*.cs")
            if parser_pattern.search(path.read_text(encoding="utf-8-sig"))
        ]
        if len(parser_sources) != 1:
            raise AiModuleContractError(
                f"Expected one source for {parser_type}; found {len(parser_sources)}"
            )

        parser_text = parser_sources[0].read_text(encoding="utf-8-sig")
        parser_id = re.search(
            r"\b(?:public|internal)\s+const\s+string\s+ModuleId\s*=\s*\"([^\"]+)\"\s*;",
            parser_text,
        )
        if not parser_id:
            raise AiModuleContractError(
                f"Could not resolve ModuleId for {parser_type} in {parser_sources[0]}"
            )
        ids.append(parser_id.group(1))

    duplicates = sorted({module_id for module_id in ids if ids.count(module_id) > 1})
    if duplicates:
        raise AiModuleContractError(
            "Duplicate module IDs in AiModuleRegistry.CreateDefault: "
            + ", ".join(duplicates)
        )
    return frozenset(ids)
