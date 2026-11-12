# Rogue Command ChangeableValues Specification

This reference details the 26 canonical attributes defined in `EntityBalancingStore.ChangeableValue` within `Assembly-CSharp.dll` (Rogue Command 1.0.9). These attributes govern unit and structure attributes throughout combat and macro-economy.

## 1. The 26 Canonical Enum Attributes

Static inspection of the pinned Rogue Command 1.0.9 `EntityBalancingStore.GetOriginalIntValue` and `GetOriginalValueAsFloat` shows that these members do not all share the same storage type or source field. `customBalance` keys use the exact `ChangeableValue` name; the row-source column is the actual field read by the native getter.

| Enum Index | `ChangeableValue` / `customBalance` key | Native base source | Storage | Notes |
|:---:|:---|:---|:---:|:---|
| 0 | `Cost` | `EntityBalancingParameters.cost` | `Int32` | Credits. |
| 1 | `ProductionDuration` | `EntityBalancingParameters.productionDuration` | `Single` | Seconds. |
| 2 | `MaxCapacity` | `EntityBalancingParameters.maxCapacity` | `Int32` | Native capacity value. |
| 3 | `MaxHealth` | `EntityBalancingParameters.maxHealth` | `Int32` | Base health. |
| 4 | `MaxShield` | `EntityBalancingParameters.maxShield` | `Int32` | Discrete shield charges. |
| 5 | `MaxMana` | `EntityBalancingParameters.maxMana` | `Int32` | Base mana capacity. |
| 6 | `MaxRank` | `EntityBalancingParameters.maxRank` | `Int32` | Maximum rank. |
| 7 | `MoveSpeed` | `EntityBalancingParameters.moveSpeed` | `Single` | Maximum translational speed. |
| 8 | `SightRadius` | `EntityBalancingParameters.sightRadius` | `Int32` | Native sight-radius value. |
| 9 | `Damage1` | `EntityBalancingParameters.damage1` | `Single` | Primary damage value. |
| 10 | `Damage2` | `EntityBalancingParameters.damage2` | `Single` | Secondary damage value. |
| 11 | `WeaponRange` | `EntityBalancingParameters.weaponRange` | `Single` | Weapon range. |
| 12 | `Attack1Cooldown` | `EntityBalancingParameters.attackCooldown` | `Single` | Primary cooldown; field name is not `attack1Cooldown`. |
| 13 | `Attack2Cooldown` | `EntityBalancingParameters.attack2Cooldown` | `Single` | Secondary cooldown. |
| 14 | `EffectRadius1` | `EntityBalancingParameters.effectRadius1` | `Single` | Primary effect radius. |
| 15 | `EffectRadius2` | `EntityBalancingParameters.effectRadius2` | `Single` | Secondary effect radius. |
| 16 | `SkillRange` | `EntityBalancingParameters.skillRange` | `Int32` | Native skill-range value. |
| 17 | `HealAmount1` | `EntityBalancingParameters.healAmount1` | `Single` | Primary healing amount. |
| 18 | `HealAmount2` | `EntityBalancingParameters.healAmount2` | `Single` | Secondary healing amount. |
| 19 | `GainCreditsAmount` | `EntityBalancingParameters.gainCreditsAmount` | `Single` | Credit yield. |
| 20 | `SkillManaCost` | `EntityBalancingParameters.skillManaCost` | `Single` | Skill cost. |
| 21 | `Duration1` | `EntityBalancingParameters.duration1` | `Single` | Primary duration. |
| 22 | `SpawnTtl` | `EntityBalancingParameters.duration2` | `Single` | Native mapping uses `duration2`, not `spawnTtl`. |
| 23 | `ManaRechargePerSecond` | `GameBalancingStore.ManaRechargePerSecond` | `Single` | Global source; not a per-unit row field. |
| 24 | `MaxArmor` | `EntityBalancingParameters.maxArmor` | `Single` | Maximum replenishable armor. |
| 25 | `ArmorProtection` | `EntityBalancingParameters.armorProtection` | `Single` | Flat damage subtraction. |

The per-unit `customBalance` contract supports only the 25 row-backed entries above; it excludes global `ManaRechargePerSecond`. `Attack1Cooldown` and `SpawnTtl` require explicit field mappings and must not be derived by simple casing rules.

## 2. Per-unit row overrides and native caches

The loader applies `customBalance` and structured `movement.moveSpeed` values to the incoming `EntityBalancingParameters` row before it is registered. It does not seed `ChangeableIntValueCache` or `ChangeableFloatValueCache` with final stat values. In the pinned game, `EntityBalancingStore.InvalidateCache` clears those caches; subsequent getters recalculate from the row and then apply relic, upgrade, in-game card, and meta-progression `CardChangeScriptableObject` deltas. Directly inserting a final value into the cache is therefore transient and bypasses those native modifiers. This describes static inspection plus local validation, not in-game consumption evidence.

Integer-backed `ChangeableValue` entries have an additional native precision bound. Static inspection of `CalculateResultingIntValue` shows it widens the row's `Int32` to `Single`, applies card deltas in `Single`, then calls `MathHelper.RoundToInt`. Therefore the loader caps integer-backed custom and raw balance overrides at `2^24` (`16,777,216`), the largest contiguous integer range exactly representable by `Single`; `16,777,217` becomes `16,777,216` at the native conversion. The DTO still uses `double` so JSON parsing does not silently round the value before validation. This is static inspection corroborated by local regression tests, not proof of an in-game unit consuming an override.

---

## 3. Reverse-Engineered Damage Mitigation Rules

Direct inspection of `EntityController.TakeDamage(float amount, EntityController attacker, Vector3 damageSourcePosition, bool bypassShield = false)` in `Assembly-CSharp.dll` confirms:

### Discrete Shield Charges
```csharp
if (amount > 0f && this.HasShield && this.CurrentShield > 0f && !bypassShield)
{
    this.ChargeShield(-1f);
    // 100% damage absorbed, health is untouched!
    return;
}
```
- Shields **do not possess hit points**.
- Any hit ($D > 0$) consumes exactly **1 discrete charge**, whether it is 2 damage from a Gatling gun or 55 damage from a siege shell.
- Units with high rate-of-fire (`attackCooldown` $\le 0.4\text{s}$) counter shielded targets rapidly, whereas heavy single-shot artillery units are hard-countered by shield charges.

### Flat Armor Subtraction and Floor
```csharp
float damageAfterArmor = Mathf.Max(0.1f, amount - this.ArmorProtection);
this.CurrentHealth -= damageAfterArmor;
```
- Armor provides **pure flat subtraction**, not percentage resistance.
- There is an immutable floor of **0.1 damage**. Even if a unit has 10 armor and is hit for 1 damage, it will still take 0.1 damage per hit.
- Attacking a unit whose `ArmorProtection >= Damage1` triggers an **Armor Lockout**, severely degrading attacker DPS to trivial levels ($\frac{0.1}{\text{cooldown}}$).

---

## 4. Weapon & Attack Types

`attackType` in Rogue Command defines targeting delivery:
- `None`: Unarmed or passive support.
- `Single`: Direct line-of-fire shot against one entity.
- `AoE`: Explosive payload affecting all units in `effectRadius1`.
- `Multi`: Multiple simultaneous projectiles per volley.
- `MultiAoE`: Multiple explosive rounds per volley.
- `Beam`: Sustained energy beam.
- `Artillery`: Ballistic high-arc projectile requiring clear pitch traversal.
- `MultiArtillery`: Ballistic multi-shell barrage.

> [!NOTE]
> Suicide detonation units (e.g., `BombBike`) are distinguished by the tag `Bomb` in `offeredSystemTags` or `attackCooldown == 0`, **not** by setting `attackType = "Single"`. Marking a combat walker with `attackType = "Single"` does not make it self-destruct; it simply specifies single-target targeting.
