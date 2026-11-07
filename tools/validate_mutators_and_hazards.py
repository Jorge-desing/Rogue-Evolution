#!/usr/bin/env python3
"""
validate_mutators_and_hazards.py — Audits and validates roguelike mutators,
relic modifiers, Heat/Ascension difficulty scaling, and environmental hazard
declarations for Rogue Command modding.

Enforces engine invariants discovered via REA in Rogue Command 1.0.9:
1. Canonical Mutable Stats (ChangeableValue):
   Exactly 26 stats mutators can affect:
   Cost, ProductionDuration, MaxCapacity, MaxHealth, MaxShield, MaxMana, MaxRank,
   MoveSpeed, SightRadius, Damage1, Damage2, WeaponRange, Attack1Cooldown, Attack2Cooldown,
   EffectRadius1, EffectRadius2, SkillRange, HealAmount1, HealAmount2, GainCreditsAmount,
   SkillManaCost, Duration1, SpawnTtl, ManaRechargePerSecond, MaxArmor, ArmorProtection.
2. Operations: Add, Multiply.
3. Target Sides: Player (False), Enemy (True), Irrelevant (Global).
4. Unit Roles (34 canonical enum roles):
   Spawn, Harvester, FrontalAttacker, MassDestruction, Fire, Sniper, Supporter,
   Rock, Robo, Spawner, PCXCard, Building, Factory, Turret, Refinery, Unit, Heal,
   Specialist, Skill, Engineer, Bomb, Scout, Chest, Tank, Vehicle, Mech, Tree,
   Melee, Research, Crystal, Builder, Drop, All, None.
5. Status Effects (30 canonical enum values):
   CombatBoost, Defender, SquadEffect, Turbo, SlowedDown, Stun, OnFire, Spikes,
   CriticallyHitting, Battered, Reaper, Regen, Rage, Attacker, Invincible, Leaking,
   Vampirism, Robust, Marked, Rush, Solo, Stealth, Taunt, Armored, AttackDeactivated,
   Panic, Overclocked, Buff, Debuff, All.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, field, asdict
import json
from pathlib import Path
import sys
from typing import List, Dict, Any

CANONICAL_CHANGEABLE_VALUES = {
    "cost",
    "productionduration",
    "maxcapacity",
    "maxhealth",
    "maxshield",
    "maxmana",
    "maxrank",
    "movespeed",
    "sightradius",
    "damage1",
    "damage2",
    "weaponrange",
    "attack1cooldown",
    "attack2cooldown",
    "effectradius1",
    "effectradius2",
    "skillrange",
    "healamount1",
    "healamount2",
    "gaincreditsamount",
    "skillmanacost",
    "duration1",
    "spawnttl",
    "manarechargepersecond",
    "maxarmor",
    "armorprotection",
}

CANONICAL_OPERATIONS = {"add", "multiply"}

CANONICAL_SIDES = {"player", "enemy", "irrelevant", "false", "true"}

CANONICAL_ROLES = {
    "none", "spawn", "harvester", "frontalattacker", "massdestruction", "fire",
    "sniper", "supporter", "rock", "robo", "spawner", "pcxcard", "building",
    "factory", "turret", "refinery", "unit", "heal", "specialist", "skill",
    "engineer", "bomb", "scout", "chest", "tank", "vehicle", "mech", "tree",
    "melee", "research", "crystal", "builder", "drop", "all"
}

CANONICAL_STATUS_EFFECTS = {
    "none", "combatboost", "defender", "squadeffect", "turbo", "sloweddown",
    "stun", "onfire", "spikes", "criticallyhitting", "battered", "reaper",
    "regen", "rage", "attacker", "invincible", "leaking", "vampirism", "robust",
    "marked", "rush", "solo", "stealth", "taunt", "armored", "attackdeactivated",
    "panic", "overclocked", "buff", "debuff", "all"
}


@dataclass
class MutatorAudit:
    mutator_id: str
    target_stat: str
    operation: str
    value: float
    side: str
    filter_roles: List[str] = field(default_factory=list)
    status_effects: List[str] = field(default_factory=list)
    scales_with_heat: bool = False
    score: int = 100
    flags: List[str] = field(default_factory=list)


class MutatorValidator:
    """Validates mutators, card changes, and environmental hazard configurations."""

    def __init__(self) -> None:
        self.audits: List[MutatorAudit] = []

    def validate_mutator_entry(self, entry: Dict[str, Any], identifier: str) -> MutatorAudit:
        stat_raw = str(entry.get("valueToChange") or entry.get("stat") or entry.get("name") or "").strip().lower()
        op_raw = str(entry.get("operation") or "Add").strip().lower()
        side_raw = str(entry.get("side") or "Player").strip().lower()
        scales_heat = bool(entry.get("multiplyValueWithCurrentHeatLevel", False))

        try:
            val = float(entry.get("value", 1.0))
        except (ValueError, TypeError):
            val = 0.0

        audit = MutatorAudit(
            mutator_id=identifier,
            target_stat=stat_raw or "unknown",
            operation=op_raw or "unknown",
            value=val,
            side=side_raw or "unknown",
            scales_with_heat=scales_heat,
        )

        # 1. Validate Target Stat
        if stat_raw not in CANONICAL_CHANGEABLE_VALUES:
            audit.score -= 30
            audit.flags.append(
                f"UNKNOWN_TARGET_STAT: '{stat_raw}' is not one of the 26 canonical ChangeableValue stats."
            )

        if scales_heat:
            audit.score -= 20
            audit.flags.append(
                "UNVERIFIED_HEAT_SCALING: Confirm the native scaling formula and per-level budget before enabling this modifier."
            )

        # 2. Validate Operation
        if op_raw not in CANONICAL_OPERATIONS:
            audit.score -= 20
            audit.flags.append(f"UNKNOWN_OPERATION: '{op_raw}' must be 'Add' or 'Multiply'.")

        # 3. Validate Side
        if side_raw not in CANONICAL_SIDES:
            audit.score -= 10
            audit.flags.append(f"UNKNOWN_SIDE: '{side_raw}' must be 'Player', 'Enemy', or 'Irrelevant'.")

        # 4. Validate Roles Filter
        roles = entry.get("cardMustHaveOneOfTheseRoles") or entry.get("roles") or []
        if isinstance(roles, str):
            roles = [roles]
        for r in roles:
            r_str = str(r).strip().lower()
            audit.filter_roles.append(r_str)
            if r_str not in CANONICAL_ROLES:
                audit.score -= 10
                audit.flags.append(f"UNKNOWN_UNIT_ROLE: Filter role '{r_str}' is not a valid UnitRole.")

        # 5. Validate Status Effects
        effects = entry.get("statusEffects") or entry.get("statusEffect") or []
        if isinstance(effects, str):
            effects = [effects]
        for e in effects:
            e_str = str(e).strip().lower()
            audit.status_effects.append(e_str)
            if e_str not in CANONICAL_STATUS_EFFECTS:
                audit.score -= 10
                audit.flags.append(f"UNKNOWN_STATUS_EFFECT: '{e_str}' is not one of the 30 StatusEffect enums.")

        # 6. Bounded Values & Extreme Balance Hazards
        if op_raw == "multiply" and val <= 0.0:
            audit.score -= 25
            audit.flags.append("NON_POSITIVE_MULTIPLIER: Multiplier <= 0.0 can cause division by zero or permanent disable.")
        elif op_raw == "multiply" and val > 10.0:
            audit.flags.append(f"EXTREME_MULTIPLIER: Multiplier {val:.1f}x may cause severe balance destabilization.")

        if stat_raw == "movespeed" and op_raw == "multiply" and val < 0.2:
            audit.flags.append("IMMOBILITY_HAZARD: MoveSpeed multiplier < 0.2 may stall units completely.")

        if stat_raw == "movespeed" and op_raw == "add" and val < -0.8:
            audit.flags.append("IMMOBILITY_HAZARD: MoveSpeed additive reduction < -0.8 may cause negative/zero speed.")

        audit.score = max(0, audit.score)
        return audit

    def validate_content_json(self, content_path: Path) -> List[MutatorAudit]:
        audits: List[MutatorAudit] = []
        if not content_path.exists():
            return audits

        try:
            data = json.loads(content_path.read_text(encoding="utf-8"))
        except Exception as e:
            sys.stderr.write(f"Error parsing JSON in {content_path}: {e}\n")
            return audits

        # Check for mutators / relics / cardChanges in pack
        # 1. Inspect mutators list if present
        for i, m in enumerate(data.get("mutators", [])):
            mid = m.get("id", f"mutator_{i}")
            audits.append(self.validate_mutator_entry(m, mid))

        # 2. Inspect native card changes declared by content-pack relics.
        for i, relic in enumerate(data.get("relics", [])):
            relic_id = relic.get("id", f"relic_{i}")
            for j, change in enumerate(relic.get("cardChanges", [])):
                entry = dict(change)
                # The pack's player-facing Health alias maps to native MaxHealth.
                if str(entry.get("valueToChange", "")).strip().lower() == "health":
                    entry["valueToChange"] = "MaxHealth"
                audits.append(self.validate_mutator_entry(
                    entry, f"{relic_id}_card_change_{j}"
                ))

        # 3. Inspect stage mutator overrides if present
        for stage in data.get("stages", []):
            s_id = stage.get("id", "stage")
            for j, mo in enumerate(stage.get("mutators", [])):
                audits.append(self.validate_mutator_entry(mo, f"{s_id}_mutator_{j}"))

        # 4. Inspect unit balance overrides as baseline modifiers
        for unit in data.get("units", []):
            u_id = unit.get("id", "unit")
            for bo in unit.get("balanceOverrides", []):
                name = bo.get("name", "")
                if name.lower() in CANONICAL_CHANGEABLE_VALUES:
                    # Synthetic check for sanity
                    audits.append(self.validate_mutator_entry(
                        {"valueToChange": name, "operation": "Add", "value": bo.get("value", 0)},
                        f"{u_id}_{name}"
                    ))

        return audits


def print_audit_table(audits: List[MutatorAudit]) -> None:
    header = f"{'Mutator Identifier':<30} | {'Target Stat':<16} | {'Op':<8} | {'Value':<8} | {'Side':<10} | {'Score':<5}"
    print("-" * len(header))
    print(header)
    print("-" * len(header))
    for a in audits:
        v_str = f"{a.value:.2f}" if isinstance(a.value, float) else str(a.value)
        print(f"{a.mutator_id:<30} | {a.target_stat:<16} | {a.operation:<8} | {v_str:<8} | {a.side:<10} | {a.score:<5}")
    print("-" * len(header))

    flagged = [a for a in audits if a.flags]
    if flagged:
        print("\n[!] MUTATOR & HAZARD AUDIT DIAGNOSTICS:")
        for a in flagged:
            print(f"  * {a.mutator_id} (Score: {a.score}/100):")
            for f in a.flags:
                print(f"      - {f}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Rogue Command Roguelike Mutators, Relics & Environmental Hazard Validator."
    )
    parser.add_argument(
        "--pack",
        type=Path,
        help="Path to content pack directory containing content.json.",
    )
    parser.add_argument(
        "--file",
        type=Path,
        help="Path to a pack-shaped JSON file containing mutators, relics, or units.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results in JSON format.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    validator = MutatorValidator()
    all_audits: List[MutatorAudit] = []

    # 1. If pack specified
    if args.pack:
        pack_json = args.pack / "content.json" if args.pack.is_dir() else args.pack
        all_audits.extend(validator.validate_content_json(pack_json))

    # 2. If specific file specified
    if args.file:
        all_audits.extend(validator.validate_content_json(args.file))

    # 3. Default: scan content/packs/
    if not (args.pack or args.file):
        packs_dir = Path("content/packs")
        if packs_dir.exists():
            for p in packs_dir.glob("*/content.json"):
                all_audits.extend(validator.validate_content_json(p))

    if not all_audits:
        sys.stderr.write("No mutator or balance override specifications found to audit.\n")
        return 1

    if args.json:
        data = [asdict(a) for a in all_audits]
        print(json.dumps(data, indent=2))
    else:
        print(f"\nAudited {len(all_audits)} mutator / balance modifier(s).")
        print_audit_table(all_audits)

    has_critical_failure = any(a.score < 40 for a in all_audits)
    return 2 if has_critical_failure else 0


if __name__ == "__main__":
    sys.exit(main())
