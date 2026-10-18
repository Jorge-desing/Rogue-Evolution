---
name: rogue-command-balance-simulation
description: "Simulate and calibrate tactical combat balance, Lanchester dynamics, and unit stats for Rogue Command."
---

# Rogue Command Combat Balance Simulation

Use this skill when designing, tuning, or reviewing combat balance for Rogue Command units and packs. It validates unit stats against reverse-engineered game mechanics and performs Lanchester differential combat simulations.

Read `AGENTS.md`, `references/changeable-values-spec.md`, `references/lanchester-combat-math.md`, and `references/unit-stat-calibration-matrix.md` before adjusting `balanceOverrides` in `content.json`.

---

## 1. When to Use This Skill

- Authoring new units or tweaking existing unit stats in `content.json`.
- Diagnosing why a modded unit is overperforming or getting crushed in tactical skirmishes.
- Verifying the Cost-Efficiency Index (CEI) and Time-To-Kill (TTK) across unit archetypes.
- Testing asymmetric battles (e.g. swarm vs heavy armor, artillery vs scouts, shield attrition).
- Ensuring that changes avoid unintended mechanics traps such as **Armor Lockouts** ($\text{Damage} \le \text{Armor}$, leading to 0.1 floor damage) or **Infinite Kiting Envelopes** ($\text{Range}_A > \text{Range}_B$ and $\text{Speed}_A > \text{Speed}_B$).

---

## 2. Core Game Combat Realities (Rogue Command 1.0.9)

- **Discrete Shield Charges**: Shields do **not** act as an extra health bar. Any attack of damage $D > 0$ consumes exactly 1 shield charge and deals 0 HP damage. Rapid-fire weapons strip shields quickly; slow alpha-strike cannons suffer severe damage loss against shields.
- **Flat Armor Subtraction with Floor**: Damage after armor is calculated as $\max(0.1\text{f}, \text{RawDamage} - \text{ArmorProtection})$. Armor directly subtracts from damage; it does not reduce by a percentage.
- **Weapon Ranges & Speeds**: Units close distance until within `weaponRange` before opening fire. If a unit is outranged and slower, it will take damage without dealing any back.
- **Suicide Detonators**: Units with the `Bomb` tag or `is_suicide` close to contact ($\le 1.0\text{m}$), detonate their payload across `effectRadius1`, and immediately self-destruct.

---

## 3. Using the Simulation Tool

The simulation engine is available at `tools/simulate_combat_balance.py` (with a byte-identical copy at `scripts/simulate_combat_balance.py` inside this skill).

Run it with the project's virtual environment:
```powershell
.venv\Scripts\python.exe tools/simulate_combat_balance.py [OPTIONS]
```

### Common Commands

1. **Benchmark all mod units against their native templates**:
   ```powershell
   .venv\Scripts\python.exe tools/simulate_combat_balance.py --all-mod-units
   ```

2. **Inspect a single unit's detailed profile and EHP**:
   ```powershell
   .venv\Scripts\python.exe tools/simulate_combat_balance.py --unit sample-native:scout-tank
   .venv\Scripts\python.exe tools/simulate_combat_balance.py --unit sample-native:scout-tank --json
   ```

3. **Simulate a 1v1 duel between two units**:
   ```powershell
   .venv\Scripts\python.exe tools/simulate_combat_balance.py --matchup sample-native:assault-walker GatlingWalker
   ```

4. **Simulate a squad / Lanchester skirmish (Monte Carlo)**:
   ```powershell
   # 5 scouts vs 2 siege platforms over 100 trials
   .venv\Scripts\python.exe tools/simulate_combat_balance.py --squad sample-native:scout-tank:5 sample-native:heavy-artillery:2 --trials 100
   ```

5. **Test custom content packs or TSV paths**:
   ```powershell
   .venv\Scripts\python.exe tools/simulate_combat_balance.py --pack path/to/content.json --tsv path/to/exampleEntityBalancing.tsv --all-mod-units
   ```

---

## 4. Balance Tuning Workflow

1. **Check Archetype Bands**: Compare proposed stats in `content.json` with `references/unit-stat-calibration-matrix.md`.
2. **Run 1v1 Benchmark**: Run `--matchup <mod-unit> <native-template>` to verify that the mod unit fills its intended role without breaking relative power scaling.
3. **Inspect Balance Flags**: Watch for diagnostic flags:
   - `ARMOR_LOCKOUT`: Attacker damage $\le$ defender armor.
   - `EXTREME_LOW_TTK`: Target dies in $< 1.5\text{s}$ with no counterplay window.
   - `ASYMMETRIC_SUPERIORITY`: Squad overwhelmingly dominates at equal or lower mineral investment.
4. **Validate Pack**: Always run `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` to ensure schema constraints and game rules remain completely satisfied.
