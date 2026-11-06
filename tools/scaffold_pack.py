#!/usr/bin/env python3
"""Genera un esqueleto mínimo de content pack válido para Rogue Command.

La función pura :func:`generate_scaffold` devuelve el dict listo para
``content.json``. El CLI escribe ese dict en un directorio de pack y,
opcionalmente, lo valida con el esquema del repositorio.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# Generación del esqueleto
# ---------------------------------------------------------------------------

_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]*$")


def _validate_token(value: str, label: str) -> str:
    if not isinstance(value, str) or not _ID_PATTERN.match(value):
        raise ValueError(f"{label} inválido: {value!r}")
    return value


def _validate_pack_id(pack_id: str) -> str:
    if not isinstance(pack_id, str) or not pack_id:
        raise ValueError(f"packId inválido: {pack_id!r}")
    namespace, sep, slug = pack_id.partition(":")
    if not sep or not namespace or not slug:
        raise ValueError("packId debe tener forma 'namespace:slug'")
    _validate_token(namespace, "namespace")
    _validate_token(slug, "slug de pack")
    if ":" in slug:
        raise ValueError("packId debe tener un único ':'")
    return pack_id


def _localizations(name: str, description: str, es_name: str, es_description: str) -> list[dict]:
    return [
        {"language": "en-US", "name": name, "description": description},
        {"language": "es-XL", "name": es_name, "description": es_description},
    ]


def _balance(name: str, value: str) -> dict:
    return {"name": name, "value": str(value)}


def generate_scaffold(
    pack_id: str,
    unit_slug: str,
    stage_slug: str = "training-stage",
    ai_slug: str = "base-defense",
    unit_display_name: str = "Scaffold Scout",
    unit_template: str = "PCXBaseTank",
    factory_template: str = "PCXBaseTankFactory",
    stage_template: str = "StageParameters_Pools_Level1",
    ai_template: str = "_01_Enemy - Melee Poker PCXPoker Variant",
) -> dict:
    """Devuelve un ``content.json`` mínimo y válido.

    El pack declara una unidad con su fábrica 1:1, una etapa nativa que ofrece
    esa fábrica y un perfil AI aislado con un módulo de construcción. Los IDs
    resultantes están namespaced con el namespace de ``pack_id``.
    """
    _validate_pack_id(pack_id)
    for slug, label in (
        (unit_slug, "unit_slug"),
        (stage_slug, "stage_slug"),
        (ai_slug, "ai_slug"),
    ):
        _validate_token(slug, label)
    namespace = pack_id.split(":", 1)[0]
    unit_id = f"{namespace}:{unit_slug}"
    factory_id = f"{namespace}:{unit_slug}-factory"
    stage_id = f"{namespace}:{stage_slug}"
    ai_id = f"{namespace}:{ai_slug}"

    unit = {
        "id": unit_id,
        "nativeTemplateId": unit_template,
        "isAllowedAsStartingBlueprint": True,
        "isAllowedForAi": True,
        "isAllowedForDemo": True,
        "isForSpecialists": False,
        "localizations": _localizations(
            unit_display_name,
            "A scaffold unit generated for a new content pack.",
            f"Explorador {unit_display_name}",
            "Unidad de esqueleto generada para un nuevo pack de contenido.",
        ),
        "balanceOverrides": [
            _balance("rarity", "Common"),
            _balance("neededExperienceLevel", "0"),
        ],
        "productionBuilding": {
            "id": factory_id,
            "nativeTemplateId": factory_template,
            "balanceOverrides": [
                _balance("maxCapacity", "3"),
                _balance("neededExperienceLevel", "0"),
                _balance("rarity", "Common"),
            ],
            "localizations": _localizations(
                f"{unit_display_name} Assembly Plant",
                f"Produces {unit_display_name} units.",
                f"Planta de ensamblaje {unit_display_name}",
                f"Produce unidades {unit_display_name}.",
            ),
        },
    }

    stage = {
        "id": stage_id,
        "nativeTemplateId": stage_template,
        "parameterOverrides": [
            _balance("levelCount", "8"),
            _balance("maxWidth", "6"),
            _balance("coinReward", "160"),
            _balance("startCrystalReward", "80"),
        ],
        "mapSizeMin": 35,
        "mapSizeMax": 80,
        "isPartOfDemo": False,
        "unitIds": [unit_id],
        "aiProfileIds": [ai_id],
    }

    ai_profile = {
        "id": ai_id,
        "nativeTemplateId": ai_template,
        "scopeToReferencedStages": True,
        "doctrine": "Standard",
        "aggressionMultiplier": 1.0,
        "defaultUnitStance": "Aggressive",
        "defaultUnitLoadout": "Standard",
        "startingUnitAffixes": ["Swift"],
        "startingUnitVeterancyRank": 1,
        "unitEntries": [
            {
                "unitId": unit_id,
                "baseIndex": 0,
                "startingInAscensionLevel": 0,
                "countAtStart": 1,
                "maxLiving": 1,
            }
        ],
        "productionBuildingEntries": [
            {
                "unitId": unit_id,
                "baseIndex": 0,
                "startingInAscensionLevel": 0,
                "maxLiving": 1,
            }
        ],
        "modules": [
            {
                "moduleId": "sample:scheduled-base-build",
                "parameters": [
                    _balance("entityId", factory_id),
                    _balance("baseIndex", "0"),
                    _balance("intervalSeconds", "8"),
                    _balance("maxSuccessfulBuilds", "1"),
                    _balance("maxAttempts", "3"),
                ],
            }
        ],
    }

    return {
        "schemaVersion": 1,
        "packId": pack_id,
        "assetBundles": [],
        "units": [unit],
        "stages": [stage],
        "aiProfiles": [ai_profile],
    }


def write_scaffold(scaffold: dict, out_dir: str | Path) -> Path:
    out_path = Path(out_dir)
    out_path.mkdir(parents=True, exist_ok=True)
    target = out_path / "content.json"
    target.write_text(json.dumps(scaffold, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return target


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Genera un content pack esqueleto válido para Rogue Command",
    )
    parser.add_argument(
        "--pack-id",
        default="scaffold:expansion",
        help="ID namespaced del pack (defecto: scaffold:expansion)",
    )
    parser.add_argument("--unit-slug", default="scaffold-scout", help="Slug de la unidad")
    parser.add_argument("--unit-name", default="Scaffold Scout", help="Nombre visible de la unidad")
    parser.add_argument("--stage-slug", default="training-stage", help="Slug de la etapa")
    parser.add_argument("--ai-slug", default="base-defense", help="Slug del perfil AI")
    parser.add_argument("--out-dir", required=True, help="Directorio destino del pack")
    parser.add_argument(
        "--validate",
        action="store_true",
        help="Valida el pack escrito contra content/schema/content.schema.json",
    )
    args = parser.parse_args(argv)

    try:
        scaffold = generate_scaffold(
            pack_id=args.pack_id,
            unit_slug=args.unit_slug,
            stage_slug=args.stage_slug,
            ai_slug=args.ai_slug,
            unit_display_name=args.unit_name,
        )
    except ValueError as exc:
        print(f"Invalid scaffold arguments: {exc}", file=sys.stderr)
        return 2

    written = write_scaffold(scaffold, args.out_dir)
    print(f"Scaffold escrito en {written}")

    if args.validate:
        repo_root = Path(__file__).resolve().parent.parent
        schema_path = repo_root / "content" / "schema" / "content.schema.json"
        import jsonschema  # type: ignore

        schema = json.loads(schema_path.read_text(encoding="utf-8"))
        try:
            jsonschema.validate(instance=scaffold, schema=schema)
        except jsonschema.ValidationError as exc:
            print(f"Validación JSON Schema falló: {exc.message}", file=sys.stderr)
            return 1
        print("Validación JSON Schema: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
