# Status Effects & Battlefield Environmental Hazards

This reference guides the creation of map hazards, area-of-effect (AoE) status fields, and temporary combat conditions in Rogue Command using native engine components.

---

## 1. Canonical StatusEffect Enumeration (30 Values)

Rogue Command 1.0.9 defines 30 status effects in `StatusEffect` (token `0x020000bd` / `EntityController.StatusEffect`):

| Enum Identifier | Int Value | Category | Gameplay Effect & Tactical Role |
| :--- | :---: | :--- | :--- |
| `None` | 0 | - | Baseline empty state. |
| `CombatBoost` | 1 | Buff | Temporary increase to attack power and cadence. |
| `Defender` | 2 | Buff | Damage mitigation when fortified in position. |
| `SquadEffect` | 3 | Buff | Proximity aura granted by squad formation cohesion. |
| `Turbo` | 4 | Buff | Significant temporary boost to movement velocity. |
| `SlowedDown` | 5 | Debuff | Reduced movement speed and turning rate. |
| `Stun` | 6 | Debuff | Complete suppression of movement and weapon firing. |
| `OnFire` | 7 | Hazard | Ongoing damage over time (DoT); spreads to nearby grass. |
| `Spikes` | 8 | Utility | Thorns damage reflected onto melee attackers. |
| `CriticallyHitting`| 9 | Buff | Guaranteed critical strike bonus on next salvo. |
| `Battered` | 10 | Debuff | Increased damage received from all incoming attacks. |
| `Reaper` | 11 | Buff | Life steal or gold bounty awarded upon unit kill. |
| `Regen` | 12 | Buff | Continuous restoration of hull integrity over time. |
| `Rage` | 13 | Buff | Attack speed ramps up as unit health drops. |
| `Attacker` | 14 | Buff | Bonus damage when engaging entrenched defensive positions. |
| `Invincible` | 15 | Special | Complete immunity to all forms of damage. |
| `Leaking` | 16 | Debuff | Continuous loss of energy or shield capacity. |
| `Vampirism` | 17 | Buff | Fractional damage converted to immediate unit repair. |
| `Robust` | 18 | Buff | Immunity to knockback, stun, and forced movement. |
| `Marked` | 19 | Debuff | Defense stripped; priority targeting by AI snipers. |
| `Rush` | 20 | Buff | Burst acceleration upon spawning or stage transition. |
| `Solo` | 21 | Buff | Stat amplification when operating isolated from friendly units. |
| `Stealth` | 22 | Utility | Invisibility to enemy targeting outside point-blank range. |
| `Taunt` | 23 | Utility | Forces nearby enemy units to prioritize this target. |
| `Armored` | 24 | Buff | Flat damage reduction applied before hull calculations. |
| `AttackDeactivated`| 25 | Debuff | Weapons jammed; unit can move but cannot attack. |
| `Panic` | 26 | Debuff | Unit loses formation control and retreats erratically. |
| `Overclocked` | 27 | Buff | High fire rate accompanied by minor continuous heat buildup. |
| `Buff` | 28 | Meta | Generic filter identifier for all positive conditions. |
| `Debuff` | 29 | Meta | Generic filter identifier for all negative conditions. |
| `All` | 30 | Meta | Wildcard filter matching any active status effect. |

---

## 2. Prefab Architecture for Battlefield Hazards

Environmental hazards exist in stage scenes or are spawned dynamically by tactical airstrikes, artillery shells, or destroyed reactors.

### Hierarchy & Component Stack

```
[Hazard_AcidPool_Prefab]
├── Transform (Position, Rotation, Scale)
├── SphereCollider / BoxCollider (IsTrigger = true)
├── HazardZoneTrigger (Script handling OnTriggerEnter / OnTriggerStay / OnTriggerExit)
│   ├── TargetSide: Both / Player / Enemy
│   ├── TickIntervalSeconds: 0.5f
│   ├── AppliedStatusEffect: OnFire / SlowedDown / Leaking
│   └── DamagePerTick: 15.0f
├── AudioSource (Spatial 3D loop: SFX_Hazard_AcidSizzle)
└── VisualRoot
    ├── MeshRenderer (Decal / ground puddle plane)
    └── ParticleSystem (URP smoke, bubbling acid, caustic vapors)
```

### Critical Setup Rules

1. **Trigger Collider Physics:**
   - Always set `Collider.isTrigger = true`.
   - Never place non-trigger colliders on ground hazards, as they disrupt unit pathfinding (`EntityFlockingMovement`) and cause severe traffic jams.
2. **Amortized Tick Rate:**
   - Avoid executing damage or status application every frame in `OnTriggerStay`.
   - Implement an accumulated timer buffer:
     ```csharp
     private float _tickTimer;
     void OnTriggerStay(Collider other)
     {
         _tickTimer += Time.deltaTime;
         if (_tickTimer >= 0.5f)
         {
             _tickTimer = 0f;
             ApplyHazardEffect(other);
         }
     }
     ```
3. **Audio Calibration:**
   - Environmental loops must set `spatialBlend = 1.0f` (full 3D).
   - Configure gentle roll-off curves (`minDistance = 2.0f`, `maxDistance = 18.0f`) to prevent ambient audio fatigue when viewing large battlefields.

---

## 3. Native Integration with StatusEffectsAndParticles

When a hazard applies a status effect to an `EntityController`, the native component `StatusEffectsAndParticles` displays canonical visual markers:
- `StatusEffect.OnFire`: Spawns orange flame and black soot particles on unit sockets.
- `StatusEffect.Stun`: Emits revolving electrical discharge arcs around the unit hull.
- `StatusEffect.SlowedDown`: Displays cryo frost condensation and frozen mist.
- `StatusEffect.CombatBoost`: Emits golden upwelling particle rays.

Custom units authored with the `rogue-command-vfx-authoring` and `rogue-command-3d-model-authoring` skills will automatically bind these particle systems if standard sockets (`Socket_DamageSmoke`, `Socket_MuzzlePrimary`) exist.
