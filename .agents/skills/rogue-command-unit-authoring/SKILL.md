---
name: rogue-command-unit-authoring
description: "Author or review Rogue Command units, including balance variants, model overlays, card sprites, and localized names."
version: 1.2.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, unit authoring, balance, variants, factories, blueprints, cards, sprites]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-pack-validation, rogue-command-3d-model-authoring, rogue-command-balance-simulation]
---

# Rogue Command Unit Authoring

Authoritative guide for creating and reviewing modded units, balance variants, factory pairings, and reward pool bindings.

---

## 1. Unit Construction Routes

| Route | Definition Mechanism | Asset & Code Contract | Validation & Constraints |
|---|---|---|---|
| **Native Template** | Copies `nativeTemplateId` balance row and native prefab | Uses `balanceOverrides` / `customBalance`; keeps native controller & logic | Run `tools/validate_native_templates.py` to verify ID |
| **Visual Overlay** | Declares `visualAssetName` + `visualRootPath` in bundle | Overlays custom mesh onto native renderer subtree; retains native logic | Run `tools/validate_native_visual_roots.py`. Root must resolve to valid renderer subtree |
| **Custom Prefab** | Full bundled balance asset, prefab, and card sprite | Script-free prefab; requires complete Unity scaffolding | Fail-closed if game-required components cannot be resolved |

- **Factory Invariant:** Every added unit requires its own namespaced `productionBuilding` linking `factoryForEntityId` to the unit. Reward pools offer the factory's blueprint; AI rosters and stage manifests reference the produced unit.
- **Fail-Closed Factory Registration:** If factory registration throws, remove the registered unit product row before rethrowing to prevent partial provider state.

---

## 2. Balance Precedence, Bounds & Enums

### Precedence Order
$$\text{customBalance} > \text{raw balanceOverrides} > \text{positive finite movement.moveSpeed}$$

### Balance Overrides Contract
- **25 Verified `ChangeableValue` Keys:** Maps directly to `EntityBalancingParameters` row fields (`MaxHealth`, `MoveSpeed`, `Damage1`, `Attack1Cooldown`, `Damage2`, `Attack2Cooldown`, `MaxArmor`, `ArmorProtection`, `Cost`, `BuildTime`, `SightRadius`, `AttackRange`, etc.). Excludes global-only `ManaRechargePerSecond`.
- **Numeric Caps:** Integer fields capped at `16,777,216` (due to native `CalculateResultingIntValue` widening `Int32` to `Single`). Float fields capped at `Single.MaxValue`. Must be finite and nonnegative.
- **Armor Contract:** `MaxArmor` is purely an inert repair pool / visual bar; it does NOT mitigate damage. Only `ArmorProtection` (flat damage subtraction, floor 0.1) reduces damage.
- **Scout Range Floor:** Modded scouts must have `weaponRange >= 5.5` to prevent `INFINITE_KITING_ENVELOPE` against slower enemies.
- **Supported Enums:**
  - `rarity`: `Common`, `Rare`, `UltraRare`.
  - `attackType`: `None`, `Single`, `AoE`, `Multi`, `MultiAoE`, `Beam`, `Artillery`, `MultiArtillery`.
  - `skillType`: `None`, `Toggle`, `Location`, `LocationAoE`, `LocationMultiTargets`, `Self`, `SelfAoE`, `Target`, `TargetMultiTarget`, `TargetAoE`, `SelfMultiTarget`.

---

## 3. Declarative Loadout Variants (`units[].variants`)

- **Capacity & Bounds:** Max 8 variants per unit; max 64 `balanceOverrides` per variant.
- **Identifier Rules:** Variant IDs must be valid identifiers and strictly reject line terminators (`\r`, `\n`, CRLF, U+2028, U+2029).
- **Runtime Application:** `diagnostics_order_apply_unit_loadout` applies variants on live units. Converts absolute instance stats (`damage1`, `attackCooldown`, `moveSpeed`, `weaponRange`, `maxHealth`, `maxArmor`, `armorProtection`, `maxShield`, `sightRadius`) to native relative modifiers. `Standard` resets to baseline.

---

## 4. Structured Fields & Runtime Mechanics

When adding or altering runtime mechanics, update all 4 layers simultaneously: Schema $\rightarrow$ DTO/Clone $\rightarrow$ Validation $\rightarrow$ Runtime Loader.

| Mechanic Field | Sub-Properties & Types | Range & Constraints | Behavior & ChangeableValue Mapping |
|---|---|---|---|
| `shieldRegeneration` | `delay` (float), `rate` (float) | Nonnegative floats | Regenerates shield charges out of combat |
| `vampiricLeech` | `siphonRatio` (float), `maxSiphonPerHit` (float) | $0.0 < \text{ratio} \le 1.0$ (exclusive 0). Use `null` if disabled | Heals on hit $\rightarrow$ maps to `Damage1` |
| `tauntPulse` | `radius` (float), `duration` (float) | Positive floats | Draws enemy aggro |
| `shieldOverloadNova`| `radius` (float), `damage` (float) | Positive floats | AoE explosion when shield breaks |
| `damageReflect` | `reflectRatio` (float), `maxReflectPerHit` (float) | $0.0 < \text{ratio} \le 1.0$ (exclusive 0); max $\ge 0$ (`0` = uncapped) | Thorns: reflects fraction of damage taken to hostile attacker via native `TakeDamage`; thread-static depth guard prevents mutual-reflect loops |
| `deathNova` | `novaRadius` (float), `damage` (float), `stunDuration` (float), `slowDuration` (float), `maxTargets` (int) | Radius 1–50; damage 0–500; stun/slow 0–15; targets 0–32 | Deathrattle AoE on `Die` postfix only (`OnEntityKilled`); pooled reuse via `OnEntityDied` never triggers |
| `experienceReward` | `experienceAmount` (int) | Positive integer (floats reject!) | Bounty XP grant $\rightarrow$ maps to `Damage1` |
| `combatBoosts[]` | `statName` (string), `multiplier` (float), `duration` (float) | Finite positive | Timed buff + persistent card modifier; role-aware |

- **Tiered Role Scaling:** Scale mechanics by cost tier (T0 $\le 80$, T1 $\le 260$, T2 $> 260$) and role. Melee/support gain sustain; disposable units get zero leech. Cap flat bounty damage (×0.6 for `MassDestruction`).
- **Director Cleanup Invariant:** When entities are destroyed or recycled, `RuntimeUnitMechanicsDirector` must call `RemoveStatusEffect` for baseline effects and `RemoveCaptain` for command links. Combat XP is cleared only on final destruction.

---

## 5. Drafting, Blueprint Pools & Card Sprites

- **Drafting & Roster Flags:**
  - `isAllowedAsStartingBlueprint`: `true` to offer during initial Stage 1 deck draft.
  - `isAllowedForAi`: `true` for combat units, `false` for factories/HQs.
  - `isAllowedForDemo`: `true` for demo run eligibility.
  - `isForSpecialists`: `true` for specialist archetype pools.
- **Card Initialization Order:** Always register unit rows with `EntityBalancingStore` BEFORE injecting card prefabs into reward pools. If `CardNew.Init` queries an unregistered entity, it falls back to an error panel.
- **Card Sprites:** $128 \times 128$ PNG; `FilterMode.Point`, `mipmapEnabled = false`, `spriteImportMode = Single`. Generated via `imagegen` against dark tactical gradient (`#0a1216` to `#162a34`).

---

## 6. References

- [Pack Validation Skill](../rogue-command-pack-validation/SKILL.md) — Schema and registry validation.
- [3D Model Authoring Skill](../rogue-command-3d-model-authoring/SKILL.md) — Visual geometry, sockets, and materials.
- [Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — In-game inspection and command director.
- [Balance Simulation Skill](../rogue-command-balance-simulation/SKILL.md) — Lanchester combat calibration.
