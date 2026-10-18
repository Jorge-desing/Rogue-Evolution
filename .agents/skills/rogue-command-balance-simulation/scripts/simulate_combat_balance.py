#!/usr/bin/env python3
"""Rogue Command Combat Balance & Lanchester Simulation Tool.

Calibrates, validates, and simulates tactical combat balance between Rogue Command
native units and modded expansion units using reverse-engineered combat formulas:
- Flat armor subtraction with minimum damage floor: max(0.1, RawDamage - ArmorProtection).
- Discrete shield charges: each non-zero hit consumes exactly 1 shield charge, absorbing 100% damage.
- Lanchester N-square and linear combat dynamics for 1v1 duels, squads, and swarm engagements.
- Cost-Efficiency Index (CEI) and Time-To-Kill (TTK) diagnostics.
"""

from __future__ import annotations

import argparse
import copy
import csv
import json
import math
import random
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

# Pinned default game installation TSV path
DEFAULT_GAME_TSV_PATH = Path(
    r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Mod\exampleEntityBalancing.tsv"
)

# Canonical 26 ChangeableValues in EntityBalancingStore
CANONICAL_STATS = [
    "Cost",
    "ProductionDuration",
    "MaxCapacity",
    "MaxHealth",
    "MaxShield",
    "MaxMana",
    "MaxRank",
    "MoveSpeed",
    "SightRadius",
    "Damage1",
    "Attack1Cooldown",
    "Damage2",
    "Attack2Cooldown",
    "WeaponRange",
    "EffectRadius1",
    "EffectRadius2",
    "SkillRange",
    "HealAmount1",
    "HealAmount2",
    "GainCreditsAmount",
    "SkillManaCost",
    "Duration1",
    "SpawnTtl",
    "ManaRechargePerSecond",
    "MaxArmor",
    "ArmorProtection",
]

# Built-in fallback archetype stats from Rogue Command 1.0.9 verified dataset
BUILTIN_NATIVE_ARCHETYPES: Dict[str, Dict[str, Any]] = {
    "PCXBaseTank": {
        "cost": 200.0,
        "productionDuration": 42.0,
        "maxCapacity": 5.0,
        "maxHealth": 180.0,
        "maxShield": 0.0,
        "armorProtection": 1.0,
        "moveSpeed": 11.0,
        "sightRadius": 5.0,
        "damage1": 8.0,
        "attackCooldown": 3.0,
        "weaponRange": 3.8,
        "effectRadius1": 0.2,
        "systemTags": "None",
        "attackType": "Continuous",
    },
    "ArtilleryTank": {
        "cost": 320.0,
        "productionDuration": 50.0,
        "maxCapacity": 4.0,
        "maxHealth": 80.0,
        "maxShield": 0.0,
        "armorProtection": 0.0,
        "moveSpeed": 13.0,
        "sightRadius": 6.0,
        "damage1": 45.0,
        "attackCooldown": 7.0,
        "weaponRange": 18.0,
        "effectRadius1": 1.2,
        "systemTags": "AoE, Vehicle, Sniper",
        "attackType": "Continuous",
    },
    "GatlingWalker": {
        "cost": 500.0,
        "productionDuration": 36.0,
        "maxCapacity": 4.0,
        "maxHealth": 400.0,
        "maxShield": 0.0,
        "armorProtection": 0.0,
        "moveSpeed": 14.0,
        "sightRadius": 7.0,
        "damage1": 2.0,
        "attackCooldown": 0.6,
        "weaponRange": 5.0,
        "effectRadius1": 0.0,
        "systemTags": "Mech",
        "attackType": "Continuous",
    },
    "BombBike": {
        "cost": 45.0,
        "productionDuration": 10.0,
        "maxCapacity": 10.0,
        "maxHealth": 60.0,
        "maxShield": 0.0,
        "armorProtection": 0.0,
        "moveSpeed": 22.0,
        "sightRadius": 5.0,
        "damage1": 36.0,
        "attackCooldown": 0.0,
        "weaponRange": 0.8,
        "effectRadius1": 1.75,
        "systemTags": "AoE, Bomb, Vehicle, SpawnDuration",
        "attackType": "Single",  # Suicide detonation
    },
    "LightningTank": {
        "cost": 180.0,
        "productionDuration": 30.0,
        "maxCapacity": 5.0,
        "maxHealth": 120.0,
        "maxShield": 5.0,
        "armorProtection": 1.0,
        "moveSpeed": 14.0,
        "sightRadius": 6.0,
        "damage1": 7.0,
        "attackCooldown": 1.5,
        "weaponRange": 3.0,
        "effectRadius1": 0.0,
        "systemTags": "Shield, Armor, SkillRangeUse, Tank",
        "attackType": "Continuous",
    },
    "PCXShieldingTruck": {
        "cost": 260.0,
        "productionDuration": 100.0,
        "maxCapacity": 3.0,
        "maxHealth": 90.0,
        "maxShield": 3.0,
        "armorProtection": 1.0,
        "moveSpeed": 13.0,
        "sightRadius": 6.0,
        "damage1": 8.0,
        "attackCooldown": 0.35,
        "weaponRange": 4.5,
        "effectRadius1": 0.0,
        "systemTags": "None",
        "attackType": "Continuous",
    },
}


@dataclass
class UnitProfile:
    """Normalized combat profile for a Rogue Command unit."""

    unit_id: str
    display_name: str
    source: str  # 'native' or 'mod'
    template_id: Optional[str] = None
    cost: float = 100.0
    production_duration: float = 30.0
    max_health: float = 100.0
    max_shield: float = 0.0  # Discrete shield charges
    armor_protection: float = 0.0  # Flat reduction
    move_speed: float = 12.0
    sight_radius: float = 6.0
    damage1: float = 10.0
    attack_cooldown: float = 1.0
    damage2: float = 0.0
    attack2_cooldown: float = 0.0
    weapon_range: float = 5.0
    effect_radius1: float = 0.0
    attack_type: str = "Single"  # "Single", "AoE", "Multi", "Beam", "Artillery"
    is_suicide: bool = False
    tags: List[str] = field(default_factory=list)

    @property
    def is_suicide_unit(self) -> bool:
        """Determines if the unit detonates on impact and self-destructs."""
        return (
            self.is_suicide
            or "Bomb" in self.tags
            or ("Bomb" in (self.template_id or ""))
            or ("Bomb" in self.unit_id)
            or ("bike" in self.unit_id.lower() and self.attack_cooldown == 0.0)
        )

    @property
    def effective_range(self) -> float:
        """Effective engagement trigger distance."""
        if self.is_suicide_unit or self.weapon_range <= 0.0:
            return max(1.0, self.weapon_range)
        return self.weapon_range

    @property
    def dps(self) -> float:
        """Nominal unmitigated DPS."""
        val = 0.0
        if self.is_suicide_unit:
            return self.damage1
        if self.attack_cooldown > 0.0:
            val += self.damage1 / self.attack_cooldown
        if self.damage2 > 0.0 and self.attack2_cooldown > 0.0:
            val += self.damage2 / self.attack2_cooldown
        return val

    def effective_dps_against(self, defender_armor: float) -> float:
        """DPS after flat armor subtraction with min 0.1 floor."""
        if self.is_suicide_unit:
            return max(0.1, self.damage1 - defender_armor)
        val = 0.0
        if self.attack_cooldown > 0.0:
            dmg1 = max(0.1, self.damage1 - defender_armor)
            val += dmg1 / self.attack_cooldown
        if self.damage2 > 0.0 and self.attack2_cooldown > 0.0:
            dmg2 = max(0.1, self.damage2 - defender_armor)
            val += dmg2 / self.attack2_cooldown
        return val

    def effective_health_against(self, attacker_damage: float) -> float:
        """Effective Health Pool (EHP) accounting for discrete shield charges and flat armor."""
        shield_absorbed_damage = self.max_shield * attacker_damage
        effective_damage = max(0.1, attacker_damage - self.armor_protection)
        mitigation_ratio = attacker_damage / effective_damage if attacker_damage > 0 else 1.0
        armor_ehp = self.max_health * mitigation_ratio
        return shield_absorbed_damage + armor_ehp

    @property
    def cost_efficiency_index(self) -> float:
        """Standardized Cost-Efficiency Index (CEI): sqrt(DPS * BaselineEHP) / (Cost + 0.3 * ProdTime)."""
        baseline_ehp = self.effective_health_against(attacker_damage=15.0)
        baseline_dps = max(0.1, self.effective_dps_against(defender_armor=1.0))
        effective_cost = self.cost + (0.3 * self.production_duration)
        if effective_cost <= 0:
            return 0.0
        return (math.sqrt(baseline_dps * baseline_ehp) / effective_cost) * 100.0


@dataclass
class DuelResult:
    """Outcome of a 1v1 simulated combat between two units."""

    winner_id: str
    loser_id: str
    duration_seconds: float
    winner_remaining_hp: float
    winner_remaining_hp_percent: float
    winner_remaining_shields: float
    loser_final_hp: float
    shots_fired_winner: int
    shots_fired_loser: int
    total_damage_dealt_winner: float
    total_damage_absorbed_by_armor_winner: float
    total_damage_absorbed_by_shields_winner: float
    flags: List[str] = field(default_factory=list)


@dataclass
class SkirmishSummary:
    """Statistical summary of Monte Carlo skirmishes."""

    unit_a_id: str
    unit_b_id: str
    count_a: int
    count_b: int
    trials: int
    a_wins: int
    b_wins: int
    draws: int
    a_win_rate_pct: float
    avg_duration_seconds: float
    avg_remaining_hp_a_pct: float
    avg_remaining_hp_b_pct: float
    avg_surviving_units_a: float
    avg_surviving_units_b: float
    flags: List[str] = field(default_factory=list)


class CombatSimulator:
    """Rogue Command discrete-event combat engine."""

    def __init__(self, time_step: float = 0.05, max_simulation_time: float = 120.0):
        self.time_step = time_step
        self.max_simulation_time = max_simulation_time

    @staticmethod
    def calculate_hit_damage(raw_damage: float, defender_armor: float, defender_shields: float) -> Tuple[float, float, float]:
        """Calculates exact damage resolution according to REA-verified Rogue Command mechanics.

        Returns: (applied_hp_damage, absorbed_by_armor, absorbed_by_shields)
        """
        if defender_shields > 0:
            return 0.0, 0.0, raw_damage

        effective_damage = max(0.1, raw_damage - defender_armor)
        absorbed_armor = max(0.0, raw_damage - effective_damage)
        return effective_damage, absorbed_armor, 0.0

    def simulate_1v1_duel(
        self,
        unit_a: UnitProfile,
        unit_b: UnitProfile,
        initial_distance: Optional[float] = None,
        jitter_initiative: bool = False,
    ) -> DuelResult:
        """Simulates a deterministic or jittered 1v1 duel between two units."""
        max_range = max(unit_a.effective_range, unit_b.effective_range)
        current_distance = initial_distance if initial_distance is not None else max_range + 5.0

        hp_a = unit_a.max_health
        hp_b = unit_b.max_health
        shields_a = unit_a.max_shield
        shields_b = unit_b.max_shield

        cd_a = random.uniform(0.0, unit_a.attack_cooldown * 0.2) if (jitter_initiative and unit_a.attack_cooldown > 0) else 0.0
        cd_b = random.uniform(0.0, unit_b.attack_cooldown * 0.2) if (jitter_initiative and unit_b.attack_cooldown > 0) else 0.0

        shots_a = 0
        shots_b = 0
        dmg_dealt_a = 0.0
        dmg_dealt_b = 0.0
        armor_absorbed_a = 0.0
        armor_absorbed_b = 0.0
        shield_absorbed_a = 0.0
        shield_absorbed_b = 0.0

        elapsed_time = 0.0
        flags = []

        if unit_a.damage1 <= unit_b.armor_protection and unit_a.damage1 > 0:
            flags.append(f"ARMOR_LOCKOUT: {unit_a.unit_id} ({unit_a.damage1} dmg) <= {unit_b.unit_id} ({unit_b.armor_protection} armor). Dealing 0.1 floor.")
        if unit_b.damage1 <= unit_a.armor_protection and unit_b.damage1 > 0:
            flags.append(f"ARMOR_LOCKOUT: {unit_b.unit_id} ({unit_b.damage1} dmg) <= {unit_a.unit_id} ({unit_a.armor_protection} armor). Dealing 0.1 floor.")

        while elapsed_time < self.max_simulation_time:
            elapsed_time += self.time_step
            cd_a -= self.time_step
            cd_b -= self.time_step

            a_in_range = current_distance <= unit_a.effective_range
            b_in_range = current_distance <= unit_b.effective_range

            if not a_in_range or not b_in_range:
                closing_speed = 0.0
                if not a_in_range:
                    closing_speed += unit_a.move_speed
                if not b_in_range:
                    closing_speed += unit_b.move_speed

                # Kiting behavior
                if unit_a.effective_range > unit_b.effective_range and unit_a.move_speed > unit_b.move_speed and a_in_range and not b_in_range:
                    pass
                elif unit_b.effective_range > unit_a.effective_range and unit_b.move_speed > unit_a.move_speed and b_in_range and not a_in_range:
                    pass
                else:
                    current_distance = max(0.0, current_distance - (closing_speed * self.time_step))

            a_in_range = current_distance <= unit_a.effective_range
            b_in_range = current_distance <= unit_b.effective_range

            # Action phase: Unit A fires
            fired_a = False
            if a_in_range and cd_a <= 0.0 and hp_a > 0:
                fired_a = True
                shots_a += 1
                hp_loss, arm_abs, sh_abs = self.calculate_hit_damage(unit_a.damage1, unit_b.armor_protection, shields_b)
                if shields_b > 0:
                    shields_b = max(0.0, shields_b - 1.0)
                hp_b -= hp_loss
                dmg_dealt_a += hp_loss
                armor_absorbed_b += arm_abs
                shield_absorbed_b += sh_abs

                if unit_a.is_suicide_unit:
                    hp_a = 0.0
                else:
                    cd_a = unit_a.attack_cooldown

            # Action phase: Unit B fires
            if b_in_range and cd_b <= 0.0 and hp_b > 0:
                shots_b += 1
                hp_loss, arm_abs, sh_abs = self.calculate_hit_damage(unit_b.damage1, unit_a.armor_protection, shields_a)
                if shields_a > 0:
                    shields_a = max(0.0, shields_a - 1.0)
                hp_a -= hp_loss
                dmg_dealt_b += hp_loss
                armor_absorbed_a += arm_abs
                shield_absorbed_a += sh_abs

                if unit_b.is_suicide_unit:
                    hp_b = 0.0
                else:
                    cd_b = unit_b.attack_cooldown

            if hp_a <= 0.0 or hp_b <= 0.0:
                break

        if hp_a > 0.0 and hp_b <= 0.0:
            winner = unit_a.unit_id
            loser = unit_b.unit_id
            rem_hp = hp_a
            rem_pct = (hp_a / unit_a.max_health) * 100.0
            rem_sh = shields_a
            loser_hp = max(0.0, hp_b)
            win_shots, lose_shots = shots_a, shots_b
            win_dmg, lose_arm, win_sh = dmg_dealt_a, armor_absorbed_a, shield_absorbed_a
        elif hp_b > 0.0 and hp_a <= 0.0:
            winner = unit_b.unit_id
            loser = unit_a.unit_id
            rem_hp = hp_b
            rem_pct = (hp_b / unit_b.max_health) * 100.0
            rem_sh = shields_b
            loser_hp = max(0.0, hp_a)
            win_shots, lose_shots = shots_b, shots_a
            win_dmg, lose_arm, win_sh = dmg_dealt_b, armor_absorbed_b, shield_absorbed_b
        else:
            winner = "DRAW"
            loser = "DRAW"
            rem_hp = max(0.0, hp_a)
            rem_pct = 0.0
            rem_sh = 0.0
            loser_hp = max(0.0, hp_b)
            win_shots, lose_shots = shots_a, shots_b
            win_dmg, lose_arm, win_sh = 0.0, 0.0, 0.0
            if elapsed_time >= self.max_simulation_time:
                flags.append("TIMEOUT: Combat reached max simulation duration without resolution.")

        if elapsed_time < 1.5 and winner != "DRAW":
            flags.append("EXTREME_LOW_TTK: Target destroyed in under 1.5 seconds.")

        return DuelResult(
            winner_id=winner,
            loser_id=loser,
            duration_seconds=round(elapsed_time, 2),
            winner_remaining_hp=round(rem_hp, 1),
            winner_remaining_hp_percent=round(rem_pct, 1),
            winner_remaining_shields=round(rem_sh, 1),
            loser_final_hp=round(loser_hp, 1),
            shots_fired_winner=win_shots,
            shots_fired_loser=lose_shots,
            total_damage_dealt_winner=round(win_dmg, 1),
            total_damage_absorbed_by_armor_winner=round(lose_arm, 1),
            total_damage_absorbed_by_shields_winner=round(win_sh, 1),
            flags=flags,
        )

    def simulate_squad_skirmish(
        self,
        unit_a: UnitProfile,
        count_a: int,
        unit_b: UnitProfile,
        count_b: int,
        trials: int = 100,
    ) -> SkirmishSummary:
        """Simulates N vs M Lanchester combat over multiple Monte Carlo trials."""
        a_wins = 0
        b_wins = 0
        draws = 0
        total_duration = 0.0
        total_rem_hp_a_pct = 0.0
        total_rem_hp_b_pct = 0.0
        total_surviving_a = 0.0
        total_surviving_b = 0.0

        for _ in range(trials):
            squad_a = [
                {
                    "hp": unit_a.max_health,
                    "shields": unit_a.max_shield,
                    "cd": random.uniform(0.0, unit_a.attack_cooldown * 0.5) if unit_a.attack_cooldown > 0 else 0.0,
                    "dist": max(unit_a.effective_range, unit_b.effective_range) + random.uniform(3.0, 7.0),
                }
                for _ in range(count_a)
            ]
            squad_b = [
                {
                    "hp": unit_b.max_health,
                    "shields": unit_b.max_shield,
                    "cd": random.uniform(0.0, unit_b.attack_cooldown * 0.5) if unit_b.attack_cooldown > 0 else 0.0,
                    "dist": max(unit_a.effective_range, unit_b.effective_range) + random.uniform(3.0, 7.0),
                }
                for _ in range(count_b)
            ]

            t = 0.0
            while t < self.max_simulation_time:
                t += self.time_step
                living_a = [u for u in squad_a if u["hp"] > 0]
                living_b = [u for u in squad_b if u["hp"] > 0]

                if not living_a or not living_b:
                    break

                for ua in living_a:
                    ua["cd"] -= self.time_step
                    if ua["dist"] > unit_a.effective_range:
                        ua["dist"] = max(0.0, ua["dist"] - (unit_a.move_speed * self.time_step))

                for ub in living_b:
                    ub["cd"] -= self.time_step
                    if ub["dist"] > unit_b.effective_range:
                        ub["dist"] = max(0.0, ub["dist"] - (unit_b.move_speed * self.time_step))

                # Fire phase A -> B
                for ua in living_a:
                    if ua["dist"] <= unit_a.effective_range and ua["cd"] <= 0.0 and ua["hp"] > 0:
                        in_range_b = [ub for ub in living_b if ub["hp"] > 0]
                        if in_range_b:
                            target = min(in_range_b, key=lambda x: x["hp"])
                            hp_loss, _, _ = self.calculate_hit_damage(unit_a.damage1, unit_b.armor_protection, target["shields"])
                            if target["shields"] > 0:
                                target["shields"] = max(0.0, target["shields"] - 1.0)
                            target["hp"] -= hp_loss

                            if unit_a.effect_radius1 > 0.0 and len(in_range_b) > 1:
                                extra_targets = [ub for ub in in_range_b if ub is not target][:2]
                                for extra in extra_targets:
                                    extra_hp_loss, _, _ = self.calculate_hit_damage(
                                        unit_a.damage1 * 0.6, unit_b.armor_protection, extra["shields"]
                                    )
                                    if extra["shields"] > 0:
                                        extra["shields"] = max(0.0, extra["shields"] - 1.0)
                                    extra["hp"] -= extra_hp_loss

                            if unit_a.is_suicide_unit:
                                ua["hp"] = 0.0
                            else:
                                ua["cd"] = unit_a.attack_cooldown

                # Fire phase B -> A
                for ub in living_b:
                    if ub["dist"] <= unit_b.effective_range and ub["cd"] <= 0.0 and ub["hp"] > 0:
                        in_range_a = [ua for ua in living_a if ua["hp"] > 0]
                        if in_range_a:
                            target = min(in_range_a, key=lambda x: x["hp"])
                            hp_loss, _, _ = self.calculate_hit_damage(unit_b.damage1, unit_a.armor_protection, target["shields"])
                            if target["shields"] > 0:
                                target["shields"] = max(0.0, target["shields"] - 1.0)
                            target["hp"] -= hp_loss

                            if unit_b.effect_radius1 > 0.0 and len(in_range_a) > 1:
                                extra_targets = [ua for ua in in_range_a if ua is not target][:2]
                                for extra in extra_targets:
                                    extra_hp_loss, _, _ = self.calculate_hit_damage(
                                        unit_b.damage1 * 0.6, unit_a.armor_protection, extra["shields"]
                                    )
                                    if extra["shields"] > 0:
                                        extra["shields"] = max(0.0, extra["shields"] - 1.0)
                                    extra["hp"] -= extra_hp_loss

                            if unit_b.is_suicide_unit:
                                ub["hp"] = 0.0
                            else:
                                ub["cd"] = unit_b.attack_cooldown

            survivors_a = [u for u in squad_a if u["hp"] > 0]
            survivors_b = [u for u in squad_b if u["hp"] > 0]

            total_duration += t
            total_surviving_a += len(survivors_a)
            total_surviving_b += len(survivors_b)

            sum_hp_a = max(0.0, sum(u["hp"] for u in squad_a))
            max_hp_a = count_a * unit_a.max_health
            total_rem_hp_a_pct += (sum_hp_a / max_hp_a) * 100.0

            sum_hp_b = max(0.0, sum(u["hp"] for u in squad_b))
            max_hp_b = count_b * unit_b.max_health
            total_rem_hp_b_pct += (sum_hp_b / max_hp_b) * 100.0

            if survivors_a and not survivors_b:
                a_wins += 1
            elif survivors_b and not survivors_a:
                b_wins += 1
            else:
                draws += 1

        flags = []
        win_rate_a = (a_wins / trials) * 100.0
        if win_rate_a > 95.0 and (count_a * unit_a.cost) <= (count_b * unit_b.cost):
            flags.append(f"ASYMMETRIC_SUPERIORITY: {unit_a.unit_id} overwhelmingly defeats {unit_b.unit_id} at equal or lower investment.")
        elif win_rate_a < 5.0 and (count_a * unit_a.cost) >= (count_b * unit_b.cost):
            flags.append(f"ASYMMETRIC_DEFICIT: {unit_a.unit_id} consistently collapses against {unit_b.unit_id} despite equal or higher investment.")

        return SkirmishSummary(
            unit_a_id=unit_a.unit_id,
            unit_b_id=unit_b.unit_id,
            count_a=count_a,
            count_b=count_b,
            trials=trials,
            a_wins=a_wins,
            b_wins=b_wins,
            draws=draws,
            a_win_rate_pct=round(win_rate_a, 1),
            avg_duration_seconds=round(total_duration / trials, 2),
            avg_remaining_hp_a_pct=round(total_rem_hp_a_pct / trials, 1),
            avg_remaining_hp_b_pct=round(total_rem_hp_b_pct / trials, 1),
            avg_surviving_units_a=round(total_surviving_a / trials, 2),
            avg_surviving_units_b=round(total_surviving_b / trials, 2),
            flags=flags,
        )


class BalanceRepository:
    """Loads and harmonizes native TSV and modded content.json balance registries."""

    def __init__(self, tsv_path: Optional[Path] = None):
        self.profiles: Dict[str, UnitProfile] = {}
        self.tsv_path = tsv_path if tsv_path and tsv_path.exists() else None
        self._load_base_archetypes()

    def _load_base_archetypes(self) -> None:
        """First loads built-in verified archetypes, then enriches with TSV if available."""
        for name, data in BUILTIN_NATIVE_ARCHETYPES.items():
            self.profiles[name] = UnitProfile(
                unit_id=name,
                display_name=name,
                source="native",
                cost=float(data["cost"]),
                production_duration=float(data.get("productionDuration", 30.0)),
                max_health=float(data["maxHealth"]),
                max_shield=float(data.get("maxShield", 0.0)),
                armor_protection=float(data.get("armorProtection", 0.0)),
                move_speed=float(data.get("moveSpeed", 12.0)),
                sight_radius=float(data.get("sightRadius", 6.0)),
                damage1=float(data.get("damage1", 10.0)),
                attack_cooldown=float(data.get("attackCooldown", 1.0)),
                damage2=float(data.get("damage2", 0.0)),
                attack2_cooldown=float(data.get("attack2Cooldown", 0.0)),
                weapon_range=float(data.get("weaponRange", 5.0)),
                effect_radius1=float(data.get("effectRadius1", 0.0)),
                attack_type=str(data.get("attackType", "Continuous")),
                tags=str(data.get("systemTags", "")).split(", "),
            )

        if self.tsv_path and self.tsv_path.is_file():
            try:
                with open(self.tsv_path, mode="r", encoding="utf-8") as f:
                    reader = csv.DictReader(f, delimiter="\t")
                    for row in reader:
                        eid = row.get("entityId")
                        if not eid:
                            continue
                        self.profiles[eid] = UnitProfile(
                            unit_id=eid,
                            display_name=eid,
                            source="native",
                            cost=float(row.get("cost", 100.0) or 100.0),
                            production_duration=float(row.get("productionDuration", 30.0) or 30.0),
                            max_health=float(row.get("maxHealth", 100.0) or 100.0),
                            max_shield=float(row.get("maxShield", 0.0) or 0.0),
                            armor_protection=float(row.get("armorProtection", 0.0) or 0.0),
                            move_speed=float(row.get("moveSpeed", 12.0) or 12.0),
                            sight_radius=float(row.get("sightRadius", 6.0) or 6.0),
                            damage1=float(row.get("damage1", 10.0) or 10.0),
                            attack_cooldown=float(row.get("attackCooldown", 1.0) or 1.0),
                            damage2=float(row.get("damage2", 0.0) or 0.0),
                            attack2_cooldown=float(row.get("attack2Cooldown", 0.0) or 0.0),
                            weapon_range=float(row.get("weaponRange", 5.0) or 5.0),
                            effect_radius1=float(row.get("effectRadius1", 0.0) or 0.0),
                            attack_type="Single" if "Bomb" in str(row.get("offeredSystemTags", "")) else "Continuous",
                            tags=[t.strip() for t in str(row.get("offeredSystemTags", "")).split(",") if t.strip()],
                        )
            except Exception as e:
                sys.stderr.write(f"Warning: Failed to parse TSV at {self.tsv_path}: {e}\n")

    def load_content_pack(self, content_json_path: Path) -> List[str]:
        """Loads a mod content.json pack, cloning native templates and applying balanceOverrides."""
        loaded_units = []
        if not content_json_path.exists():
            return loaded_units

        with open(content_json_path, mode="r", encoding="utf-8") as f:
            pack_data = json.load(f)

        units = pack_data.get("units", [])
        for u in units:
            uid = u.get("id")
            if not uid:
                continue

            template_id = u.get("nativeTemplateId")
            base = self.profiles.get(template_id) if template_id else None

            display_name = uid
            for loc in u.get("localizations", []):
                if loc.get("language") == "en-US":
                    display_name = loc.get("name", uid)
                    break

            if base:
                profile = copy.deepcopy(base)
                profile.unit_id = uid
                profile.display_name = display_name
                profile.source = "mod"
                profile.template_id = template_id
            else:
                profile = UnitProfile(
                    unit_id=uid,
                    display_name=display_name,
                    source="mod",
                    template_id=template_id,
                )

            for override in u.get("balanceOverrides", []):
                name = override.get("name", "")
                raw_val = override.get("value", "")
                try:
                    val = float(raw_val)
                except ValueError:
                    val = 0.0

                key = name.lower()
                if key == "cost":
                    profile.cost = val
                elif key == "productionduration":
                    profile.production_duration = val
                elif key in ("maxhealth", "health"):
                    profile.max_health = val
                elif key in ("maxshield", "shield"):
                    profile.max_shield = val
                elif key in ("armorprotection", "armor"):
                    profile.armor_protection = val
                elif key in ("movespeed", "speed"):
                    profile.move_speed = val
                elif key in ("sightradius", "sight"):
                    profile.sight_radius = val
                elif key in ("damage1", "damage"):
                    profile.damage1 = val
                elif key in ("attackcooldown", "cooldown"):
                    profile.attack_cooldown = val
                elif key in ("weaponrange", "range"):
                    profile.weapon_range = val
                elif key in ("effectradius1", "effectradius", "aoe"):
                    profile.effect_radius1 = val
                elif key == "attacktype":
                    profile.attack_type = str(raw_val)

            self.profiles[uid] = profile
            loaded_units.append(uid)

        return loaded_units


def print_unit_profile_table(profiles: List[UnitProfile]) -> None:
    """Prints a formatted ASCII comparison table of unit stats."""
    header = (
        f"{'Unit ID':<28} | {'Cost':<5} | {'HP':<5} | {'Sh':<3} | {'Arm':<3} | "
        f"{'Spd':<4} | {'Dmg':<5} | {'CD':<4} | {'Rng':<4} | {'DPS':<6} | {'CEI':<6}"
    )
    print("-" * len(header))
    print(header)
    print("-" * len(header))
    for p in profiles:
        print(
            f"{p.unit_id:<28} | {p.cost:<5.0f} | {p.max_health:<5.0f} | {p.max_shield:<3.0f} | "
            f"{p.armor_protection:<3.0f} | {p.move_speed:<4.1f} | {p.damage1:<5.1f} | "
            f"{p.attack_cooldown:<4.1f} | {p.weapon_range:<4.1f} | {p.dps:<6.1f} | {p.cost_efficiency_index:<6.1f}"
        )
    print("-" * len(header))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Rogue Command Combat Balance & Lanchester Simulation CLI."
    )
    parser.add_argument(
        "--tsv",
        type=Path,
        default=DEFAULT_GAME_TSV_PATH,
        help="Path to exampleEntityBalancing.tsv.",
    )
    parser.add_argument(
        "--pack",
        type=Path,
        default=Path("content/packs/sample-native-expansion/content.json"),
        help="Path to mod content.json pack.",
    )
    parser.add_argument(
        "--unit",
        type=str,
        help="Print detailed combat breakdown and calibration metrics for a single unit.",
    )
    parser.add_argument(
        "--all-mod-units",
        action="store_true",
        help="Run comprehensive balance diagnostics and template comparisons for all units in pack.",
    )
    parser.add_argument(
        "--matchup",
        nargs=2,
        metavar=("UNIT_A", "UNIT_B"),
        help="Simulate a 1v1 duel between two units.",
    )
    parser.add_argument(
        "--squad",
        nargs=2,
        metavar=("UNIT_A:COUNT", "UNIT_B:COUNT"),
        help="Simulate an N vs M skirmish (e.g. sample-native:scout-tank:5 PCXBaseTank:2).",
    )
    parser.add_argument(
        "--trials",
        type=int,
        default=100,
        help="Number of Monte Carlo trials for squad skirmishes (default: 100).",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results in JSON format.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    repo = BalanceRepository(tsv_path=args.tsv)
    mod_units = repo.load_content_pack(args.pack)

    simulator = CombatSimulator()

    if args.unit:
        uid = args.unit
        if uid not in repo.profiles:
            sys.stderr.write(f"Error: Unknown unit '{uid}'.\n")
            return 1
        p = repo.profiles[uid]
        if args.json:
            data = asdict(p)
            data["dps"] = p.dps
            data["cei"] = p.cost_efficiency_index
            data["effective_range"] = p.effective_range
            data["ehp_vs_10dmg"] = p.effective_health_against(10.0)
            data["ehp_vs_50dmg"] = p.effective_health_against(50.0)
            print(json.dumps(data, indent=2))
        else:
            print(f"\n=== UNIT PROFILE: {p.display_name} ({p.unit_id}) ===")
            print(f"Source: {p.source} | Template: {p.template_id or 'None'}")
            print(f"Cost: {p.cost:.0f} | Production Duration: {p.production_duration:.1f}s")
            print(f"Health: {p.max_health:.0f} | Shields: {p.max_shield:.0f} | Armor: {p.armor_protection:.0f}")
            print(f"Move Speed: {p.move_speed:.1f} m/s | Sight Radius: {p.sight_radius:.1f} m")
            print(f"Weapon: Damage={p.damage1:.1f}, Cooldown={p.attack_cooldown:.2f}s, Range={p.weapon_range:.1f}m (Effective={p.effective_range:.1f}m)")
            print(f"Nominal DPS: {p.dps:.2f} | Cost-Efficiency Index (CEI): {p.cost_efficiency_index:.2f}")
            print(f"EHP vs Light Attack (10 dmg): {p.effective_health_against(10.0):.1f}")
            print(f"EHP vs Heavy Shell (50 dmg): {p.effective_health_against(50.0):.1f}")
            if p.tags:
                print(f"Tags: {', '.join(p.tags)}")
        return 0

    if args.matchup:
        uid_a, uid_b = args.matchup
        if uid_a not in repo.profiles:
            sys.stderr.write(f"Error: Unknown unit '{uid_a}'.\n")
            return 1
        if uid_b not in repo.profiles:
            sys.stderr.write(f"Error: Unknown unit '{uid_b}'.\n")
            return 1

        unit_a = repo.profiles[uid_a]
        unit_b = repo.profiles[uid_b]

        duel = simulator.simulate_1v1_duel(unit_a, unit_b, jitter_initiative=True)

        if args.json:
            print(json.dumps(asdict(duel), indent=2))
        else:
            print(f"\n=== 1v1 DUEL: {unit_a.display_name} vs {unit_b.display_name} ===")
            print_unit_profile_table([unit_a, unit_b])
            print(f"Winner: {duel.winner_id} in {duel.duration_seconds}s")
            print(f"Remaining HP: {duel.winner_remaining_hp} ({duel.winner_remaining_hp_percent}%)")
            print(f"Remaining Shields: {duel.winner_remaining_shields}")
            print(f"Shots Fired: Winner={duel.shots_fired_winner}, Loser={duel.shots_fired_loser}")
            if duel.flags:
                print("\n[!] BALANCE FLAGS:")
                for f in duel.flags:
                    print(f"  - {f}")
        return 0

    if args.squad:
        spec_a, spec_b = args.squad
        try:
            uid_a, cnt_a_str = spec_a.rsplit(":", 1)
            count_a = int(cnt_a_str)
            uid_b, cnt_b_str = spec_b.rsplit(":", 1)
            count_b = int(cnt_b_str)
        except ValueError:
            sys.stderr.write("Error: Squad argument format must be UNIT_ID:COUNT (e.g. UnitA:5 UnitB:2)\n")
            return 1

        if uid_a not in repo.profiles or uid_b not in repo.profiles:
            sys.stderr.write(f"Error: One or both unit IDs not found: '{uid_a}', '{uid_b}'.\n")
            return 1

        unit_a = repo.profiles[uid_a]
        unit_b = repo.profiles[uid_b]

        summary = simulator.simulate_squad_skirmish(unit_a, count_a, unit_b, count_b, trials=args.trials)

        if args.json:
            print(json.dumps(asdict(summary), indent=2))
        else:
            print(f"\n=== SQUAD SKIRMISH: {count_a}x {unit_a.display_name} vs {count_b}x {unit_b.display_name} ({args.trials} Trials) ===")
            print_unit_profile_table([unit_a, unit_b])
            total_invest_a = count_a * unit_a.cost
            total_invest_b = count_b * unit_b.cost
            print(f"Total Investment: Side A = {total_invest_a:.0f} credits | Side B = {total_invest_b:.0f} credits")
            print(f"Win Rate: Side A = {summary.a_win_rate_pct:.1f}% ({summary.a_wins} wins) | Side B = {100.0 - summary.a_win_rate_pct:.1f}% ({summary.b_wins} wins)")
            print(f"Avg Duration: {summary.avg_duration_seconds:.2f}s")
            print(f"Avg Surviving Force: Side A = {summary.avg_surviving_units_a:.1f}/{count_a} ({summary.avg_remaining_hp_a_pct:.1f}% HP) | Side B = {summary.avg_surviving_units_b:.1f}/{count_b} ({summary.avg_remaining_hp_b_pct:.1f}% HP)")
            if summary.flags:
                print("\n[!] BALANCE FLAGS:")
                for f in summary.flags:
                    print(f"  - {f}")
        return 0

    if args.all_mod_units or not (args.matchup or args.squad):
        print(f"\nLoaded {len(repo.profiles)} units ({len(mod_units)} from mod pack '{args.pack}').")
        display_profiles = [repo.profiles[uid] for uid in mod_units]
        print("\n=== MOD EXPANSION UNITS OVERVIEW ===")
        print_unit_profile_table(display_profiles)

        print("\n=== BENCHMARK MATCHUPS AGAINST NATIVE TEMPLATES ===")
        for uid in mod_units:
            unit = repo.profiles[uid]
            template_id = unit.template_id
            if not template_id or template_id not in repo.profiles:
                continue
            template = repo.profiles[template_id]
            duel = simulator.simulate_1v1_duel(unit, template, jitter_initiative=True)

            print(f"\n* Matchup: {unit.display_name} ({unit.unit_id}) vs Native {template_id}")
            print(f"  Cost: {unit.cost:.0f} vs {template.cost:.0f} | CEI: {unit.cost_efficiency_index:.1f} vs {template.cost_efficiency_index:.1f}")
            print(f"  Duel Result: Winner = {duel.winner_id} in {duel.duration_seconds}s (Winner HP: {duel.winner_remaining_hp_percent:.1f}%)")
            if duel.flags:
                for f in duel.flags:
                    print(f"    [!] {f}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
