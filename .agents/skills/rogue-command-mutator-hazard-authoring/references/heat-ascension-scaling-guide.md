# Heat & Ascension Difficulty Scaling Guide

This guide establishes mathematical formulas, design principles, and calibration budgets for progressive difficulty modifiers in Rogue Command.

---

## 1. Progression Architecture & Scaling Flags

Rogue Command provides two separate high-tier progression systems:
1. **Ascension Levels (Discrete Tiers):** Step-function modifiers unlocked sequentially. Each Ascension level activates specific `ManageStartCardChanges` rules where `startingOnAscensionLevel <= currentAscension`.
2. **Heat Modifiers (Continuous Scalers):** Granular difficulty toggles chosen by the player before starting a run. When a `CardChangeScriptableObject` sets `multiplyValueWithCurrentHeatLevel = true`, the modifier scales proportionally with the active Heat value.

---

## 2. Mathematical Scaling Models

When calibrating mutators across Heat levels, authors must account for whether operations are additive or multiplicative.

### Multiplicative Scaling Formula

For a mutator authored with `Operation.Multiply` and `multiplyValueWithCurrentHeatLevel = true`:

$$\text{EffectiveMultiplier} = 1.0 + \text{HeatLevel} \times (\text{BaseValue} - 1.0)$$

*Example:* A baseline 5% damage buff authored as `value = 1.05`:
- At Heat 1: $1.0 + 1 \times 0.05 = 1.05$ (+5%)
- At Heat 5: $1.0 + 5 \times 0.05 = 1.25$ (+25%)
- At Heat 10: $1.0 + 10 \times 0.05 = 1.50$ (+50%)

### Additive Scaling Formula

For a mutator authored with `Operation.Add` and `multiplyValueWithCurrentHeatLevel = true`:

$$\text{EffectiveDelta} = \text{BaseValue} \times \text{HeatLevel}$$

*Example:* A range bonus authored as `value = 0.5f`:
- At Heat 1: $+0.5\text{m}$
- At Heat 4: $+2.0\text{m}$

---

## 3. Lanchester Combat Dynamics & Difficulty Runaway

In real-time strategy skirmishes governed by Lanchester's Square Law ($P \propto N^2 \times D$), increasing combat attributes produces quadratic or exponential difficulty spikes:

$$\text{Tactical Combat Power} = \text{UnitCount}^2 \times \text{DamagePerSecond} \times \text{EffectiveHealth}$$

### Lanchester Multiplier Table

| Heat Level | Stat Delta ($\Delta \text{Stat}$) | Linear Power Ratio | Effective Lanchester Combat Power | Risk Level |
| :---: | :---: | :---: | :---: | :--- |
| **0** | $+0\%$ | $1.00\times$ | $1.00\times$ | Baseline |
| **3** | $+9\%$ ($+3\%$/lvl) | $1.09\times$ | $1.19\times$ | Balanced |
| **6** | $+18\%$ ($+3\%$/lvl) | $1.18\times$ | $1.39\times$ | Challenging |
| **10** | $+30\%$ ($+3\%$/lvl) | $1.30\times$ | $1.69\times$ | Expert |
| **15** | $+45\%$ ($+3\%$/lvl) | $1.45\times$ | $2.10\times$ | Punishing |
| **20** | $+60\%$ ($+3\%$/lvl) | $1.60\times$ | $2.56\times$ | Extreme (Requires Min-Maxing) |

### Authoring Budgets & Anti-Runaway Rules

1. **Cap Attack Range Increases:**
   - **Never** scale `AttackRange` on AI snipers or turrets beyond $+15\%$ maximum across all Heat levels. Excessive range breaks AI siege mechanics, causing enemies to destroy player factories from beyond fog of war or defensive response boundaries.
2. **Offset Lethality with Economic Relief or Telegraphed Cadence:**
   - When boosting enemy `AttackDamage`, avoid decreasing enemy `TimeBetweenAttacks` simultaneously. Dual scaling squares DPS and leads to instant squad vaporization.
3. **Prefer Health & Armor over Raw Alpha Damage:**
   - Increasing enemy `Health` or `Armor` prolongs engagement duration without causing frustrating player one-shots.
4. **Targeted Role Filters:**
   - Always restrict drastic mutators using `cardMustHaveOneOfTheseRoles` or `cardMustNotHaveOneOfTheseRoles`. For instance, give health buffs to `Tank` or `Building` units while exempting cheap swarm units (`Rock`, `Scout`).

---

## 4. Ascension Level Progression Blueprint

Recommended progression roadmap for a 10-level Ascension sequence:

| Tier | Category | Target Side | Mutable Value | Operation | Magnitude | Design Intent |
| :---: | :--- | :--- | :--- | :---: | :---: | :--- |
| **A1** | Attrition | Enemy | `Health` | `Multiply` | $1.10\times$ | Mild HP bump across all enemy combatants. |
| **A2** | Economy | Player | `ProductionCostMoney` | `Multiply` | $1.08\times$ | Stricter early-game credit management. |
| **A3** | Speed | Enemy | `MovementSpeed` | `Multiply` | $1.12\times$ | Faster flankers and scout raids. |
| **A4** | Production | Enemy | `ProductionTime` | `Multiply` | $0.90\times$ | Slightly faster enemy reinforcement waves. |
| **A5** | Defenses | Enemy | `Armor` | `Add` | $+2.0f$ | Requires high-DPS or anti-armor specialists. |
| **A6** | Logistics | Player | `CoinsReceivedOnDestroy` | `Multiply` | $0.85\times$ | Less salvage bounty from defeated enemies. |
| **A7** | Precision | Enemy | `Accuracy` | `Multiply` | $1.20\times$ | Enemy artillery and snipers miss less frequently. |
| **A8** | Siege | Enemy | `BonusDamageAgainstShields`| `Multiply` | $1.25\times$ | Player shields strip faster under bombardment. |
| **A9** | Swarm | Enemy | `UnitCount` | `Add` | $+1$ | Low-tier swarm squads spawn +1 extra unit. |
| **A10**| Apex | Enemy | `DamagePerSecond` | `Multiply` | $1.15\times$ | Final comprehensive lethality tuning. |
