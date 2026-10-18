# Lanchester Combat Mathematics in Rogue Command

This reference explains the mathematical models and differential combat dynamics utilized by the Rogue Command combat balance simulator.

---

## 1. Classical Lanchester Laws in RTS Combat

In real-time strategy games like Rogue Command, combat outcomes are governed by two distinct Lanchester dynamics depending on weapon range, unit density, and focus fire capabilities.

### Lanchester's Linear Law (Unfocused / Area Attrition)
When units fire blindly into an engagement area without concentrated targeting, or when combat consists of isolated 1v1 duels spread across a front line:

$$\frac{dA}{dt} = -\beta B(t), \quad \frac{dB}{dt} = -\alpha A(t)$$

When target acquisition is distributed equally, combat strength scales linearly with numbers:

$$\text{Power} = \alpha \times N$$

### Lanchester's Square Law (Modern Concentrated Focus Fire)
When every unit within range can simultaneously acquire and focus fire upon the same enemy target until destroyed:

$$\frac{dA}{dt} = -\beta B(t), \quad \frac{dB}{dt} = -\alpha A(t)$$

Under ideal focus fire, the combat power of a squad scales with the square of its headcount:

$$\text{Power} = \alpha \times N^2$$

This fundamental law explains why:
1. Two groups of equal total mineral cost do not fight evenly if one group has superior local concentration.
2. A swarm of light units ($N = 5$) can overwhelm heavy units ($M = 2$) even if individual stats favor the heavy unit, provided the swarm can maintain firing cohesion without getting obliterated by AoE splash.

---

## 2. Rogue Command Mitigation Formulas

Because Rogue Command implements **flat armor subtraction** and **discrete shield charges** rather than percentage mitigations, standard RTS formulas must be adjusted.

### Effective DPS Against Target Armor
Given primary weapon damage $D_1$, attack cooldown $T_1$, secondary damage $D_2$, secondary cooldown $T_2$, and defender armor $A$:

$$\text{DPS}_{\text{eff}}(A) = \frac{\max(0.1, D_1 - A)}{\max(0.05, T_1)} + \frac{\max(0.1, D_2 - A)}{\max(0.05, T_2)}$$

### Effective Health Pool (EHP)
For a defender with base health $H$, discrete shield charges $S$, and armor $A$, defending against an attacker with incoming raw damage per hit $D_{\text{in}}$:

$$EHP(D_{\text{in}}) = S \cdot D_{\text{in}} + H \cdot \left(\frac{D_{\text{in}}}{\max(0.1, D_{\text{in}} - A)}\right)$$

Key insights:
- **Shield scaling**: Against heavy siege shells ($D_{\text{in}} = 55$), each shield charge is worth $55\text{ EHP}$. Against rapid light fire ($D_{\text{in}} = 2$), each charge is worth only $2\text{ EHP}$.
- **Armor scaling**: When $D_{\text{in}} \to A$, the denominator approaches $0.1$, causing $EHP$ to skyrocket by up to $10\times$ to $50\times$, rendering low-damage weapons virtually useless.

---

## 3. Cost-Efficiency Index (CEI)

To balance units across different tiers, supply weights, and factory build times, the simulation engine calculates a normalized Cost-Efficiency Index:

$$CEI = \frac{\sqrt{\text{DPS}_{\text{eff}}(1.0) \times EHP(15.0)}}{\text{Cost} + 0.3 \times \text{ProductionDuration}} \times 100$$

### Interpretation Scale
- **$CEI < 8.0$**: Undertuned or niche specialist (e.g. ultra-long range artillery whose value comes purely from range advantage, not raw stats).
- **$CEI \in [10.0, 25.0]$**: Balanced core combat unit (Main Battle Tanks, Walkers, standard line units).
- **$CEI \in [25.0, 45.0]$**: High-efficiency light combat or skirmisher (Scouts, fast raiders, offset by low survivability per unit).
- **$CEI > 70.0$**: One-shot consumable / suicide unit (e.g. BombBike whose high index reflects immediate non-renewable payload).

---

## 4. Kiting and Speed Differential Envelope

A unit $A$ can achieve an **infinite kiting envelope** against unit $B$ if and only if:

$$\text{WeaponRange}_A > \text{WeaponRange}_B \quad \text{AND} \quad \text{MoveSpeed}_A > \text{MoveSpeed}_B$$

In 1v1 duels, this condition results in Unit $A$ dealing damage while Unit $B$ can never close distance, producing a 100% win rate for Unit $A$ with zero damage taken.

In swarm engagements, however, spatial crowding and surrounding dynamics restrict kiting space:
- Turn rates and obstacle navigation slow retreat vectors.
- Swarm units can pinch and enclose kiting units from flanks.
- The simulator models this in `--squad` mode by applying initial approach scatter and collision boundaries.
