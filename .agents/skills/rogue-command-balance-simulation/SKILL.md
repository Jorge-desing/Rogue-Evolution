---
name: rogue-command-balance-simulation
description: "Simulate and calibrate tactical combat balance, Lanchester dynamics, and unit stats for Rogue Command."
---

# Rogue Command Combat Balance Simulation

Use this skill when designing, tuning, or reviewing combat balance for Rogue Command units and packs. It validates unit stats against reverse-engineered game mechanics and performs Lanchester differential combat simulations.

Read `AGENTS.md`, `references/changeable-values-spec.md`, `references/lanchester-combat-math.md`, and `references/unit-stat-calibration-matrix.md` before adjusting `balanceOverrides` or `customBalance` in `content.json`.

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
- **`MaxArmor` / `CurrentArmor` is inert for damage mitigation**: Verified in decompiled `EntityController.TakeDamage` — only `ArmorProtection` (flat subtraction, floor 0.1) and discrete shield charges (`ShieldHealth`, which absorb 100% of a hit) reduce incoming damage. `MaxArmor`/`CurrentArmor` are a repair pool + visual bar only; they never absorb damage. Consequences for balance: (a) setting `maxArmor` > 0 on a unit that never repairs only pollutes the stat row and misleads authors; set it to 0 unless the unit has a repair/maintenance mechanic; (b) raising `armorProtection` to 2+ creates an **ARMOR_LOCKOUT** against low-damage units (e.g. GatlingWalker `damage1=2` → `max(0.1, 2-2)=0.1`), effectively immunizing the unit; cap `armorProtection` at 1 for non-boss units unless the lockout is intentional and documented; (c) do not model `maxArmor` as EHP in the simulator or in CEI calculations.
- **Weapon Ranges & Speeds**: Units close distance until within `weaponRange` before opening fire. If a unit is outranged and slower, it will take damage without dealing any back.
- **Suicide Detonators**: Units with the `Bomb` tag or `is_suicide` close to contact ($\le 1.0\text{m}$), detonate their payload across `effectRadius1`, and immediately self-destruct.
- **Balance Precedence & Value Overrides**:
  - The simulator and runtime enforce identical balance precedence: `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`.
  - 25 verified per-unit `EntityBalancingStore.ChangeableValue` keys map to row fields; global-only `ManaRechargePerSecond` is excluded.
  - Numeric limits enforce integer cap `16,777,216` and float cap `Single.MaxValue`.
- **Custom Units Without Native Templates**: Custom units do not require a `nativeTemplateId`. As verified by `test_custom_unit_without_native_template_id_loads`, the simulator parses and benchmarks standalone custom units using their authored manifest stats.
- **Attack delivery model limit**: In this simulator, `UnitProfile.attack_type` is descriptive metadata only; the game uses `AttackType` to select delivery and targeting behavior. Primary attacks model `firePointCount` as one same-tick projectile hit per firepoint, including shield-charge consumption per hit; `damage1` is per projectile and cooldown is per volley. Nominal DPS/CEI also includes `damage2` and `attack2Cooldown` when supplied, but timed duel and squad events execute only primary volleys; secondary delivery is not simulated. The model does not reproduce firepoint stagger, beam ticks, exact target ordering, or actual AoE victim counts. It reports `Unknown` when the input has no verified `AttackType`; do not treat `Continuous` as a game enum or as proof that damage is continuous.
- **Squad geometry limit**: `--squad` assigns each unit an independent scalar distance, adds initial approach scatter, and moves each toward its own weapon range. It does not track pairwise positions, collision, formation, obstacles, line of sight, flanking, or retreat behavior; every firing unit can choose the lowest-HP surviving opponent regardless of that target's distance. Squad results are comparative estimates, and an infinite-kiting flag from the duel model remains a warning about range/speed matchups.
- **Tactical Combat Mechanics (`RuntimeUnitMechanicsDirector`)**:
  - *Autonomous Shield Regeneration*: Units with defined shield capacity regenerate charges over time outside combat hits (`shieldRegenPerSecond`, `shieldCapacity`).
  - *Vampiric Life Leech*: Restores health based on dealt combat damage (`vampiricLifeLeechRatio`); strictly safeguarded against boosting from friendly fire.
  - *Taunt Pulse*: Forces nearby enemies within `tauntRadius` to focus fire on the taunting unit for `tauntDurationSeconds`.
  - *Shield Overload EMP*: Emits an area EMP blast (`shieldOverloadEmpRadius`, `shieldOverloadEmpDamage`) upon shield depletion.
- **Precision Critical Strike Specialization**:
  - Attribute `criticalHitChance` ($0.0 \le p \le 1.0$) mapped through `EntityController.AddCriticalHitChanceBonus` for high-variance spike damage.
- **Artillery Minimum Attack Range Dead-Zone**:
  - Attribute `minAttackRange` ($0 \le r \le 200\text{m}$) enforced via `EntityAttack.IsTargetInRange` postfix hook to prevent siege artillery from firing at point-blank enemies.
- **Dynamic On-Hit Status Effects**:
  - Support for `onHitStatusEffects[]` declaring status applications (Stun, SlowedDown, OnFire, Electrified, CombatBoost) with bounded duration and trigger chance.
- **Runtime augmentations are NOT modeled by this simulator**: `healing`, `healing.repairAmount`, `combatBoosts`, `experience` (rank-up), `experienceReward` (bounty), and `vampiricLeech` are persistent card-change augmentations applied by `RuntimeUnitMechanicsDirector` via `RuntimeCardChangeApplier`, mapped onto `EntityBalancingStore` ChangeableValues (healAmount→MaxHealth, repairAmount→MaxArmor, combatBoost/levelUpBonus→MaxHealth or Damage1, vampiricLeech.siphonRatio→Damage1, experienceReward→Damage1). The simulator's CEI/TTK/DPS reflect base stats only; never expect it to reflect augmentation impact. When authoring these, enforce schema bounds (`siphonRatio` ∈ (0,1], `experienceAmount` integer, `levelUpBonus` ≤ ~0.1) and validate the pack.
  - **`combatBoosts` apply twice**: a timed buff AND a persistent `CardChangeScriptableObject` modifier, so the `multiplier` is effectively permanent damage scaling, not a transient. Budget it as a permanent stat.
  - **Calibration budgets (cost tier × role, applied to the 9 sample units; local-only heuristic, not in-game proven)**: base by cost — T0 (≤80): cb 1.03 / dur 15s / lvlUp 0.04 / maxLevel 3 / sip 0.04 / heal 2 / rep 1 / bounty 2; T1 (≤260): cb 1.05 / dur 20s / lvlUp 0.05 / maxLevel 4 / sip 0.05 / heal 3 / rep 1.5 / bounty 4; T2 (>260): cb 1.06 / dur 30s / lvlUp 0.06 / maxLevel 5 / sip 0.06 / heal 4 / rep 2 / bounty 5. Role multipliers on top: suicide (`Bomb`) gets no sustain (heal/rep/sip/lvlUp=0, maxLevel 2, leech null); support (`Supporter`/`Economy`/`Heal`) caps cb ≤1.04 with heal/rep ×1.5, sip ×0.5, lvlUp ×0.8, bounty ×0.7, longer buffs (dur ×1.3); sniper (`Sniper`/`Artillery`) caps cb ≤1.04, sip ×0.5, heal ×0.5, bounty ×0.6, shorter buffs (dur ×0.7); bruiser (`MassDestruction`/`FrontalAttacker`) gets sip ×1.3, heal ×1.2, lvlUp ×1.1, longer buffs (dur ×1.1), bounty ×0.6.
  - **Snowball control**: cap total rank-up HP at `levelUpBonus × maxLevel` (≈30% for T2); flat-damage `experienceReward` per kill compounds hardest on high-kill bruisers and is proportionally huge on low-damage units, so scale bounty down for them; scale `vampiricLeech` by melee-vs-ranged involvement; make `combatBoost` duration role-aware (melee/support longer, sniper shorter).

---

## 3. Using the Simulation Tool

The simulation engine is available at `tools/simulate_combat_balance.py` (with a byte-identical copy at `scripts/simulate_combat_balance.py` inside this skill). Both copies must maintain 0-byte binary parity.

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
4. **Validate Pack**: Always run `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` and `python -m tools.run_python_tests --output artifacts/python-tests/report.json` to ensure schema constraints, simulator regressions, and game rules remain completely satisfied.
5. **Balance pass with pinned calibration tests (TDD)**: when `tests/python/balance/test_*_calibration.py` pins exact `damage1`/`max_shield`/`weapon_range`/`dps`/`cost_efficiency_index`, update the *test expectations first* and observe RED, then apply the surgical manifest edits (see the content-loader-tooling CRLF/byte-exact rule) and re-run the simulator (`--all-mod-units`, `--matchup`, `--unit <id> --json`) to read the *exact* new `dps`/`cost_efficiency_index` from simulator output before writing them into the expectations — do not hand-compute CEI, it folds shield charges and cooldowns. Everything else (schema, contract, localization, Release build, DLL deploy) must stay GREEN after the edit.
6. **Losing to a cheaper native in the duel benchmark is not automatically a bug**: supports (aura/heal/synergy) lose to a basic tank because those effects are unmodeled — judge supports by the DPS band floor and the shield safeguard, not the duel. Likewise, `INFINITE_KITING_ENVELOPE` for a ranged/fast harass archetype against a short-range native tank (e.g. range 3.8, speed 11) is inherent to the archetype bands; document it rather than break the band to chase the flag. When a native has many firepoints (e.g. GatlingWalker's 6), the duel race is firepoint-driven — raise `damage1` inside the band instead of adding firepoints.
7. **Native reference stats come from the simulator, not memory**: use `--unit PCXBaseTank --json` / `--unit GatlingWalker --json` to read actual range/speed/HP/DPS before tuning against them.
