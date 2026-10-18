# Rogue Command ChangeableValues Specification

This reference details the 26 canonical attributes defined in `EntityBalancingStore.ChangeableValue` within `Assembly-CSharp.dll` (Rogue Command 1.0.9). These attributes govern unit and structure attributes throughout combat and macro-economy.

## 1. The 26 Canonical Enum Attributes

| Enum Index | Enum Identifier | TSV Header | `content.json` Override | Type | Unit | Description |
|:---|:---|:---|:---|:---|:---|:---|
| 0 | `Cost` | `cost` | `cost` | `float` | Credits | Mineral/credit cost to produce or construct. |
| 1 | `ProductionDuration` | `productionDuration` | `productionDuration` | `float` | Seconds | Time required by a factory to produce one instance. |
| 2 | `MaxCapacity` | `maxCapacity` | `maxCapacity` | `int` | Supply | Army supply cost consumed while the unit is alive. |
| 3 | `MaxHealth` | `maxHealth` | `maxHealth` | `float` | Hit Points | Base structural durability before destruction. |
| 4 | `MaxShield` | `maxShield` | `maxShield` | `float` | Charges | Discrete hit-absorbing shield charges (not a second HP bar). |
| 5 | `MaxMana` | `maxMana` | `maxMana` | `float` | Energy | Capacity for active skills and tactical abilities. |
| 6 | `MaxRank` | `maxRank` | `maxRank` | `int` | Level | Maximum veterancy rank reachable through combat. |
| 7 | `MoveSpeed` | `moveSpeed` | `moveSpeed` | `float` | m/s | Maximum translational movement velocity on terrain. |
| 8 | `SightRadius` | `sightRadius` | `sightRadius` | `float` | Meters | Fog-of-war vision reveal radius. |
| 9 | `Damage1` | `damage1` | `damage1` | `float` | HP / hit | Primary weapon unmitigated payload per projectile. |
| 10 | `Attack1Cooldown` | `attackCooldown` | `attackCooldown` | `float` | Seconds | Delay between consecutive primary weapon discharges. |
| 11 | `Damage2` | `damage2` | `damage2` | `float` | HP / hit | Secondary weapon unmitigated payload per projectile. |
| 12 | `Attack2Cooldown` | `attack2Cooldown` | `attack2Cooldown` | `float` | Seconds | Delay between consecutive secondary weapon discharges. |
| 13 | `WeaponRange` | `weaponRange` | `weaponRange` | `float` | Meters | Maximum engagement distance to target center. |
| 14 | `EffectRadius1` | `effectRadius1` | `effectRadius1` | `float` | Meters | Primary blast splash radius for area-of-effect damage. |
| 15 | `EffectRadius2` | `effectRadius2` | `effectRadius2` | `float` | Meters | Secondary blast splash radius. |
| 16 | `SkillRange` | `skillRange` | `skillRange` | `float` | Meters | Maximum cast range for active abilities. |
| 17 | `HealAmount1` | `healAmount1` | `healAmount1` | `float` | HP | Primary repair/healing output per cycle. |
| 18 | `HealAmount2` | `healAmount2` | `healAmount2` | `float` | HP | Secondary repair/healing output. |
| 19 | `GainCreditsAmount` | `gainCreditsAmount` | `gainCreditsAmount` | `float` | Credits | Bounties or economic yield generated. |
| 20 | `SkillManaCost` | `skillManaCost` | `skillManaCost` | `float` | Energy | Energy deducted upon activating special skill. |
| 21 | `Duration1` | `duration1` | `duration1` | `float` | Seconds | Primary buff/effect duration. |
| 22 | `SpawnTtl` | `spawnTtl` | `spawnTtl` | `float` | Seconds | Time-to-live before automatic expiration (summons/drones). |
| 23 | `ManaRechargePerSecond`| `manaRechargePerSecond`| `manaRechargePerSecond`| `float` | Energy/s | Passive energy regeneration rate. |
| 24 | `MaxArmor` | `maxArmor` | `maxArmor` | `float` | Points | Maximum replenishable armor rating cap. |
| 25 | `ArmorProtection` | `armorProtection` | `armorProtection` | `float` | Flat reduction | Direct subtraction from incoming unmitigated damage. |

---

## 2. Reverse-Engineered Damage Mitigation Rules

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

## 3. Weapon & Attack Types

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
