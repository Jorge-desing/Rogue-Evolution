# Unit Stat Calibration Matrix for Rogue Command

This reference provides established benchmark bands for authoring and tuning units across the 6 primary tactical archetypes in Rogue Command.

---

## 1. Tactical Archetype Benchmark Bands

| Archetype | Typical Cost | Base HP | Shields | Armor | Speed | Range | DPS | Target CEI | Archetype Role & Design Invariants |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---|
| **Light Recon / Scout** | 50 – 100 | 90 – 140 | 0 | 0 – 1 | 14.0 – 16.0 | 5.0 – 7.0 | 7 – 10 | 30.0 – 45.0 | Fast harassment, vision reveal (`sightRadius` $\ge 8$), high mobility, vulnerable to sustained fire. |
| **Main Battle Tank** | 180 – 240 | 150 – 220 | 0 – 3 | 1 – 2 | 10.0 – 12.0 | 3.5 – 5.0 | 4 – 8 | 12.0 – 20.0 | Durable frontline combatant, anchors formations, withstands moderate enemy fire. |
| **Heavy Assault Mech** | 350 – 550 | 380 – 500 | 0 | 1 – 3 | 11.0 – 14.0 | 5.0 – 6.5 | 8 – 14 | 10.0 – 18.0 | Towering combat platform with high health pool, multi-barrel rotary cannons, slow turn. |
| **Siege / Artillery** | 280 – 360 | 80 – 160 | 0 | 0 – 2 | 8.0 – 13.0 | 14.0 – 19.0 | 6 – 14 | 6.0 – 16.0 | Long-range ballistic fire (`effectRadius1` $\ge 1.0$), vulnerable in close quarters, high alpha damage. |
| **Suicide Detonator / Bike** | 40 – 60 | 50 – 80 | 0 | 0 | 20.0 – 26.0 | 0.5 – 1.0 | 30 – 40* | 75.0 – 95.0 | High-speed interceptor (`is_suicide = true`), single explosive detonation, zero sustain. |
| **Support / Shield Truck**| 220 – 280 | 80 – 110 | 3 – 6 | 1 | 12.0 – 14.0 | 4.0 – 5.0 | 6 – 9 | 14.0 – 22.0 | Defensive aura/shields, moderate combat punch, high factory build duration ($> 80\text{s}$). |

*\*Note: For suicide detonators, DPS represents the immediate one-time explosive blast payload.*

---

## 2. Reverse-Engineered Balance Safeguards

When authoring `balanceOverrides` in `content.json`, test with `simulate_combat_balance.py` against these invariants:

### Safeguard A: Avoid Armor Lockouts on Standard Combatants
- In Rogue Command, incoming damage is mitigated via $\max(0.1, \text{Damage} - \text{Armor})$.
- If you author a unit with $\text{Armor} \ge 2$, any enemy unit with $\text{Damage} \le 2$ (such as native `GatlingWalker` with 2.0 damage per shot) will have its damage slashed to **0.1 per shot** (a 95% reduction).
- **Rule**: If an archetype is intended to fight infantry or Gatling units, do not raise its armor above 1 unless its cost or vulnerability to other weapons is intentionally elevated.

### Safeguard B: Preserve Counterplay on Long-Range Artillery
- High-damage artillery (`damage1` $\ge 40$, `weaponRange` $\ge 14.0$) must maintain low movement speed ($\le 10.0\text{ m/s}$) and prolonged attack cooldowns ($\ge 4.0\text{s}$).
- Giving an artillery platform both extreme range ($> 15.0$) and high speed ($> 13.0$) allows it to kite and eliminate all mid-range tanks without engagement risk.

### Safeguard C: Discrete Shield Charge Pacing
- Remember that each shield charge absorbs 100% of an incoming attack.
- Granting `maxShield` $\ge 6$ to a unit renders it virtually invulnerable to low-rate-of-fire weapons (e.g. 6 shots from a 4-second cooldown tank requires 24 seconds just to strip shields).
- Keep shield charges between 2 and 5 for combat vehicles, reserving higher values for dedicated defensive structures.
