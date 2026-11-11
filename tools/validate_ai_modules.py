#!/usr/bin/env python3
"""Valida perfiles AI de un pack contra el registro de módulos del loader.

Lee los IDs de ``AiModuleRegistry.CreateDefault()`` y sincroniza las listas
de parámetros/límites usadas por los parsers C# de
``src/RogueCommandExpansionLoader/Modules/``.
La función pura :func:`validate_ai_profiles` devuelve diagnósticos
localizados; el CLI audita un pack o todo ``content/packs``.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from tools.ai_module_contracts import registered_ai_module_ids
from tools.merge_content_packs import _as_dict, _as_list, _entity_key

KNOWN_MODULES = registered_ai_module_ids()

MAX_MODULES_PER_PROFILE = 8

# Nombres de parámetros aceptados por módulo (de *Parameters.cs).
MODULE_PARAMS: dict[str, set[str]] = {
    "sample:trace-default": {"logTag"},
    "sample:scheduled-base-build": {"baseIndex", "entityId", "intervalSeconds",
                                   "maxSuccessfulBuilds", "maxAttempts"},
    "sample:reactive-base-defense": {"unitId", "baseIndex", "cooldownSeconds",
                                     "defenseRadiusCells", "maxResponders",
                                     "protectedTargetType"},
    "sample:harvester-raider": {"baseIndex", "scanIntervalSeconds",
                                "cooldownSeconds", "raidSquadSize",
                                "minGameTimeSeconds"},
    "sample:siege-assault": {"baseIndex", "cooldownSeconds", "minSquadSize",
                             "targetPriority", "minGameTimeSeconds"},
    "sample:retreat-and-regroup": {"baseIndex", "cooldownSeconds",
                                   "healthThresholdPercent", "regroupRadiusCells"},
    "sample:harvester-escort": {"baseIndex", "escortRadiusCells",
                                "scanIntervalSeconds", "maxEscorts", "engageAttackers"},
    "sample:patrol-and-intercept": {"baseIndex", "patrolRadiusCells",
                                    "interceptRadiusCells", "scanIntervalSeconds",
                                    "maxPatrolUnits", "patrolMode"},
    "sample:factory-reinforcement": {"baseIndex", "checkIntervalSeconds",
                                     "maxQueuePerFactory", "maxStandingArmy",
                                     "minGameTimeSeconds", "requireEconomy"},
    "sample:flanking-strike": {"baseIndex", "cooldownSeconds", "flankAngleDegrees",
                               "flankDistance", "minGameTimeSeconds", "squadSize",
                               "targetPreference"},
    "sample:focus-fire": {"baseIndex", "checkIntervalSeconds", "squadRadius",
                          "minUnitsInSquad", "priorityMode", "targetRole"},
    "sample:construction-expansion": {"baseIndex", "checkIntervalSeconds",
                                      "buildingId", "desiredCount",
                                      "queuePriority", "minSurplusCrystals"},
    "sample:tactical-regroup": {"baseIndex", "checkIntervalSeconds",
                                "healthRetreatThreshold", "regroupMinSquadSize",
                                "regroupDistanceToBase"},
    "sample:squad-formation": {"anchorRole", "maxFollowersPerCaptain",
                               "formationRadiusCells", "cooldownSeconds", "baseIndex"},
    "sample:tactical-skill-activation": {"enemyTriggerRadiusCells", "minManaRatio",
                                         "cooldownSeconds", "baseIndex"},
    "sample:counter-battery": {"baseIndex", "cooldownSeconds", "maxInterceptors",
                               "minInterceptorSpeed", "threatTriggerDistance"},
    "sample:skirmish-kiting": {"cooldownSeconds", "fleeDistance", "maxKiteTargets",
                               "minKiteSpeed", "preferredEngagementRatio",
                               "retreatDistanceRatio"},
    "sample:support-repair": {"cooldownSeconds", "healthThresholdRatio",
                              "maxAssignedRepairersPerTarget",
                              "prioritizeHarvestersAndBuildings", "repairScanRadius"},
    "sample:wave-escalation": {"baseIndex", "maxWaves", "waveIntervalSeconds"},
    "sample:threat-prioritization": {"baseIndex", "checkIntervalSeconds",
                                     "engagementRadius", "maxRetargetsPerTick",
                                     "priorityMode", "retargetThreshold"},
    "sample:adaptive-commander": {"baseIndex", "scanIntervalSeconds", "commandRadius",
                                  "damagedStanceThreshold", "healthyStanceThreshold",
                                  "preferredLoadout", "commanderAffix",
                                  "adaptiveDoctrine", "auraType", "xpGrantPerPulse"},
    "sample:tactical-director": {"baseIndex", "evalIntervalSeconds",
                                 "engagementRadius", "defenseRadius",
                                 "retreatHealthRatio", "maxProposalsPerUnit",
                                 "enableSurvivalKiting", "enableBaseDefense",
                                 "enableFocusTargeting", "enableFlanking",
                                 "minFlankSpeed"},
    "sample:squad-operations-director": {"baseIndex", "evalIntervalSeconds",
                                         "supportRadius",
                                         "counterBatteryMinRangeThreshold",
                                         "enableSupportLogistics",
                                         "enableCounterBattery",
                                         "enableTacticalSkills"},
}

# Límites numéricos conocidos (name -> (min, max, entero)).
PARAM_BOUNDS: dict[str, tuple[float, float, bool]] = {
    "baseIndex": (0, 31, True),
    "intervalSeconds": (1, 600, False),
    "maxSuccessfulBuilds": (1, 32, True),
    "maxAttempts": (1, 128, True),
    "cooldownSeconds": (0.5, 300, False),
    "defenseRadiusCells": (1, 64, False),
    "maxResponders": (1, 4, True),
    "scanIntervalSeconds": (0.5, 120, False),
    "raidSquadSize": (1, 8, True),
    "minSquadSize": (1, 12, True),
    "minGameTimeSeconds": (0, 600, False),
    "maxEscorts": (1, 16, True),
    "escortRadiusCells": (1, 64, False),
    "patrolRadiusCells": (5, 32, False),
    "interceptRadiusCells": (5, 32, False),
    "maxPatrolUnits": (1, 8, True),
    "squadRadius": (5, 50, False),
    "minUnitsInSquad": (2, 12, True),
    "desiredCount": (1, 10, True),
    "queuePriority": (1, 100, True),
    "minSurplusCrystals": (0, 5000, False),
    "healthRetreatThreshold": (0.10, 0.60, False),
    "regroupMinSquadSize": (2, 10, True),
    "regroupDistanceToBase": (5, 50, False),
    "maxFollowersPerCaptain": (1, 16, True),
    "formationRadiusCells": (1, 64, False),
    "minManaRatio": (0.0, 1.0, False),
    "enemyTriggerRadiusCells": (1, 64, False),
}

# Overrides where a module has a narrower or differently signed bound than
# the shared name-level fallback above.
MODULE_PARAM_BOUNDS: dict[str, dict[str, tuple[float, float, bool]]] = {
    "sample:scheduled-base-build": {
        "baseIndex": (0, 31, True),
        "intervalSeconds": (1, 600, False),
        "maxSuccessfulBuilds": (1, 32, True),
        "maxAttempts": (1, 128, True),
    },
    "sample:tactical-director": {
        "baseIndex": (-1, 31, True),
        "evalIntervalSeconds": (0.1, 10.0, False),
        "engagementRadius": (5.0, 120.0, False),
        "defenseRadius": (5.0, 60.0, False),
        "retreatHealthRatio": (0.05, 0.80, False),
        "maxProposalsPerUnit": (1, 4, True),
        "minFlankSpeed": (3.0, 25.0, False),
    },
    "sample:squad-operations-director": {
        "baseIndex": (-1, 31, True),
        "evalIntervalSeconds": (0.20, 10.0, False),
        "supportRadius": (5.0, 80.0, False),
        "counterBatteryMinRangeThreshold": (8.0, 50.0, False),
    },
}


def validate_ai_profiles(pack: dict,
                         known_unit_ids: set[str] | None = None) -> list[str]:
    """Audita los perfiles AI de ``pack`` y devuelve diagnósticos."""
    pack = _as_dict(pack)
    findings: list[str] = []
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else "<unknown>"
    prefix = f"[{pack_id}]"

    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        pid = _entity_key(profile) or "<unknown>"
        pfx = f"{prefix} aiProfile '{pid}'"

        modules = _as_list(profile.get("modules"))
        if len(modules) > MAX_MODULES_PER_PROFILE:
            findings.append(
                f"{pfx}: {len(modules)} modules excede el máximo "
                f"{MAX_MODULES_PER_PROFILE}"
            )

        seen: set[str] = set()
        for module in modules:
            if not isinstance(module, dict):
                findings.append(f"{pfx}: module no es un objeto")
                continue
            mid = module.get("moduleId")
            if not isinstance(mid, str) or not mid.strip():
                findings.append(f"{pfx}: module sin moduleId")
                continue
            if mid in seen:
                findings.append(f"{pfx}: moduleId duplicado '{mid}'")
            seen.add(mid)
            if mid not in KNOWN_MODULES:
                findings.append(f"{pfx}: moduleId desconocido '{mid}'")
                continue

            allowed = MODULE_PARAMS.get(mid, set())
            for param in _as_list(module.get("parameters")):
                if not isinstance(param, dict):
                    continue
                name = param.get("name")
                value = param.get("value")
                if not isinstance(name, str) or not name.strip():
                    findings.append(f"{pfx}: parámetro sin nombre en '{mid}'")
                    continue
                if name not in allowed:
                    findings.append(
                        f"{pfx}: parámetro desconocido '{name}' para '{mid}'"
                    )
                    continue
                bounds = MODULE_PARAM_BOUNDS.get(mid, {}).get(name, PARAM_BOUNDS.get(name))
                if bounds is None or value is None:
                    continue
                lo, hi, is_int = bounds
                try:
                    num = float(value)
                except (TypeError, ValueError):
                    findings.append(
                        f"{pfx}: parámetro '{name}' de '{mid}' no es numérico: '{value}'"
                    )
                    continue
                if is_int and int(num) != num:
                    findings.append(
                        f"{pfx}: parámetro '{name}' de '{mid}' debe ser entero: '{value}'"
                    )
                    continue
                if num < lo or num > hi:
                    findings.append(
                        f"{pfx}: parámetro '{name}'={value} fuera de "
                        f"[{lo}, {hi}] para '{mid}'"
                    )

        # Perfiles con plantilla nativa deben aislarse por alcance.
        if isinstance(profile.get("nativeTemplateId"), str) and profile["nativeTemplateId"].strip():
            if profile.get("scopeToReferencedStages") is not True:
                findings.append(
                    f"{pfx}: scopeToReferencedStages debe ser true "
                    "para perfiles con nativeTemplateId"
                )

        if known_unit_ids is not None:
            for field in ("unitEntries", "productionBuildingEntries"):
                for entry in _as_list(profile.get(field)):
                    if not isinstance(entry, dict):
                        continue
                    uid = entry.get("unitId")
                    if isinstance(uid, str) and uid and uid not in known_unit_ids:
                        findings.append(
                            f"{pfx}: {field} referencia unidad no declarada '{uid}'"
                        )

    return findings


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Valida perfiles AI contra el registro de módulos del loader"
    )
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    parser.add_argument("--all", action="store_true",
                        help="Auditar todo content/packs")
    args = parser.parse_args(argv)

    targets: list[Path] = []
    if args.all:
        root = Path("content/packs")
        if root.is_dir():
            targets = sorted(
                p / "content.json" for p in root.iterdir() if p.is_dir()
            )
    else:
        targets = [_resolve_pack(Path(args.target))]

    # IDs conocidos globales para referencias cruzadas.
    known: set[str] = set()
    packs: list[dict] = []
    for t in targets:
        try:
            data = json.loads(t.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        packs.append(data)
        for unit in _as_list(data.get("units")):
            uid = _entity_key(unit)
            if uid:
                known.add(uid)

    if not args.all and not packs:
        if args.json:
            print(json.dumps({"ok": False, "issues": ["pack no encontrado"]},
                             ensure_ascii=False, indent=2))
        else:
            print("pack no encontrado")
        return 1

    all_issues: list[str] = []
    for pack in packs:
        all_issues.extend(validate_ai_profiles(pack, known_unit_ids=known or None))

    if args.json:
        print(json.dumps({"ok": not all_issues, "issues": all_issues},
                         ensure_ascii=False, indent=2))
    else:
        if all_issues:
            for issue in all_issues:
                print(issue)
        else:
            print("OK: perfiles AI válidos")
    return 0 if not all_issues else 1


if __name__ == "__main__":
    sys.exit(main())
