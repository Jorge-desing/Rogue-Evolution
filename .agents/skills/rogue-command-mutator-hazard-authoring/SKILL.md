---
name: rogue-command-mutator-hazard-authoring
description: Author roguelike mutators, relics, battlefield hazards, and Heat/Ascension scaling curves for Rogue Command with native ScriptableObjects and schema validation.
---

# Rogue Command Mutator & Hazard Authoring

This skill guides the design, implementation, and calibration of roguelike mutators, passive relics, environmental map hazards, and progressive difficulty curves (Heat & Ascension) for Rogue Command.

---

## 1. Core Engine Realities & Reverse-Engineered Architecture

Reverse engineering of Rogue Command 1.0.9 (`Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`) confirms:

1. **Native ScriptableObject Mutator Pipeline:**
   - Modded mutators and stat changes do not require runtime Harmony hooks or bytecode patching.
   - The engine provides [`CardChangeScriptableObject`](references/native-card-change-and-relic-schema.md), applying direct modifications across the 26 canonical attributes in `EntityBalancingStore.ChangeableValue`.
   - Mutators are loaded additively into `RelicBalancingStore` or evaluated at stage start via `ManageStartCardChanges`.
2. **Faction & Role Target Filtering:**
   - Target side is governed by the `Side` enum: `False` (Player), `True` (Enemy/AI), and `Irrelevant` (Global/Symmetric).
   - Precision targeting uses conjunction (`cardMustHaveAllOfTheseRoles`), disjunction (`cardMustHaveOneOfTheseRoles`), and exclusion lists over the 34 native `UnitRole` identifiers.
3. **Canonical 30-Status-Effect Engine:**
   - Built-in status effects (`Turbo`, `Stun`, `OnFire`, `Vampirism`, `Stealth`, `Armored`, `Overclocked`, etc.) are natively supported and visually handled by `StatusEffectsAndParticles`.
4. **Dynamic Heat & Ascension Progression:**
   - Modifiers natively scale with pre-run Heat level via `multiplyValueWithCurrentHeatLevel = true` and activate at specific thresholds (`startingOnHeat`, `startingOnAscensionLevel`).

---

## 2. Step-by-Step Mutator & Hazard Authoring Workflow

```
[1. Scope & Faction] -> [2. Stat Operations] -> [3. Hazard Prefabs] -> [4. Heat/Ascension] -> [5. Pack Manifest] -> [6. Audit]
```

### Step 1: Define Scope & Faction Targeting
- Determine if the mutator is a Player Relic (`Side.False`), an AI Escalation (`Side.True`), or an Environmental Rule (`Side.Irrelevant`).
- Restrict applicability using `UnitRole` filters to protect strategic asymmetry (e.g. apply siege buffs only to `Building` or `Turret`, avoid giving infinite range to `Rock` swarmers).

### Step 2: Configure Stat Modifiers & Operations
- Choose target attributes from the 26 canonical `ChangeableValue` entries (e.g. `Health`, `AttackDamage`, `MovementSpeed`, `ProductionTime`, `Armor`).
- Select mathematical operation:
  - `Operation.Add` for flat adjustments ($+2$ Armor, $+1$ UnitCount).
  - `Operation.Multiply` for fractional scaling ($1.20\times$ Damage, $0.85\times$ ProductionTime).
- Refer to [native-card-change-and-relic-schema.md](references/native-card-change-and-relic-schema.md) for field definitions.

### Step 3: Implement Environmental Battlefield Hazards
- Create hazard prefabs in Unity authoring project (`unity/RogueCommandAssetAuthoring/`):
  - Add trigger collider (`isTrigger = true`) to avoid breaking unit flocking pathfinding.
  - Implement amortized tick intervals ($\ge 0.5\text{s}$) rather than per-frame physics checks.
  - Bind URP ParticleSystems and 3D spatialized AudioSources (`spatialBlend = 1.0f`).
- Refer to [status-effects-and-battlefield-hazards.md](references/status-effects-and-battlefield-hazards.md) for status effect tables and visual wiring.

### Step 4: Calibrate Heat & Ascension Progression Curves
- Apply the Lanchester Square Law ($P \propto N^2 \times D$) to prevent unintended exponential difficulty spikes.
- Follow recommended authoring budgets: $\le 3\%$ major combat stat per Heat level; cap `AttackRange` increases at $+15\%$ maximum.
- Refer to [heat-ascension-scaling-guide.md](references/heat-ascension-scaling-guide.md) for scaling formulas and progression blueprints.

### Step 5: Declare Pack Manifest & Export Assets
- Register mutators in `mutators.json` within your expansion pack directory (or compile serialized ScriptableObjects into the pack AssetBundle).
- Ensure localization tokens exist for pre-run card descriptions.

### Step 6: Validate with the Dedicated Audit Tool
- Run `tools/validate_mutators_and_hazards.py` to audit schema compliance, enum validity, value bounds, and scaling curves.

---

## 3. Auditing & Validation Tool

Audit custom mutators, relics, hazard prefabs, and Heat scaling curves:

```bash
# Audit an entire content pack
python tools/validate_mutators_and_hazards.py --pack content/packs/sample-native-expansion

# Audit a standalone mutators.json manifest
python tools/validate_mutators_and_hazards.py --manifest content/packs/sample-native-expansion/mutators.json

# Simulate Lanchester scaling at Heat Level 10
python tools/validate_mutators_and_hazards.py --pack content/packs/sample-native-expansion --heat 10

# Scan workspace packs and authoring prefabs with default settings
python tools/validate_mutators_and_hazards.py
```

The tool validates enum identities, detects invalid string values, checks for dangerous out-of-bounds scaling, verifies trigger collider invariants, and outputs a formatted diagnostic summary table.
