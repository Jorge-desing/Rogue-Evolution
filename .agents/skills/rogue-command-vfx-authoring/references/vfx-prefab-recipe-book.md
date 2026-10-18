# Rogue Command VFX Prefab Recipe Book

This reference contains concrete, verified parameter configurations for creating particle effects in Unity 2021.3.45f2 for Rogue Command units and weapons.

---

## Recipe 1: Heavy Cannon Muzzle Flash

Designed for primary tank cannons and heavy artillery (`ColossusSiege`, `PCXBaseTank`). Placed as child index 0 of `Socket_MuzzlePrimary` or individual firepoints.

### Emitter Structure
```
MuzzleFlash_HeavyCannon (Empty GameObject, Child Index 0)
├── Flash_Core (ParticleSystem - Additive Glow Quad)
└── Sparks_Cone (ParticleSystem - High-speed directional sparks)
```

### Module Parameters
| Module | Parameter | Flash_Core Value | Sparks_Cone Value |
|:---|:---|:---|:---|
| **Main** | Duration | `0.1s` | `0.1s` |
| | Looping | `false` | `false` |
| | Prewarm | `false` | `false` |
| | Start Lifetime | `0.06s` | `0.15s – 0.25s` |
| | Start Speed | `0.0` | `15.0 – 25.0` |
| | Start Size | `1.8 – 2.4` | `0.15 – 0.35` |
| | Start Color | `RGBA(255, 220, 140, 255)` | `RGBA(255, 180, 80, 255)` |
| | Gravity Modifier | `0.0` | `0.8` |
| | Simulation Space | `World` | `World` |
| | Max Particles | `2` | `16` |
| **Emission** | Bursts | 1 burst at Time 0: `1 particle` | 1 burst at Time 0: `10 – 14 particles` |
| | Rate over Time | `0` | `0` |
| **Shape** | Shape | `Sphere` (Radius: 0.05) | `Cone` (Angle: 12°, Radius: 0.1) |
| **Color over Lifetime** | Gradient | Fade alpha: $255 \to 0$ in 0.06s | Fade color: Yellow $\to$ Orange $\to$ Grey $\to$ Alpha 0 |
| **Renderer** | Render Mode | `Billboard` | `Stretched Billboard` (Length Scale: 2.5) |
| | Material | `Universal Render Pipeline/Particles/Unlit` (Additive) | `Universal Render Pipeline/Particles/Unlit` (Additive) |

---

## Recipe 2: Rapid Gatling / Rotary Autocannon Flash

Designed for high rate-of-fire walkers and light vehicles (`IroncladWalker`, `GatlingWalker`). Must clean up rapidly to avoid particle accumulation during continuous firing.

### Module Parameters
- **Duration**: `0.05s`, **Looping**: `false`.
- **Start Lifetime**: `0.04s` (ultra-fast turnover).
- **Start Speed**: `2.0 – 4.0`.
- **Start Size**: `0.8 – 1.2`.
- **Emission**: 1 Burst of `2 – 3` particles. Rate over Time: `0`.
- **Shape**: `Cone` (Angle: 8°, Radius: 0.05).
- **Size over Lifetime**: Shrinks from $1.0 \to 0.2$.
- **Max Particles**: `6`.

---

## Recipe 3: Moderate Damage Smoke (`Socket_DamageSmoke`)

Activated automatically by `RuntimeVisualDirector` when vehicle drops to $\le 50\%$ HP. Positioned on the engine deck or exhaust.

### Module Parameters
- **Duration**: `1.0s`, **Looping**: `true`.
- **Start Lifetime**: `1.2s – 1.8s`.
- **Start Speed**: `1.5 – 2.5` (slow upward drift).
- **Start Size**: `0.6 – 0.9` expanding over lifetime to `2.2 – 3.0`.
- **Start Color**: Dark charcoal grey `RGBA(45, 45, 50, 220)`.
- **Gravity Modifier**: `-0.15` (slight buoyant updraft).
- **Simulation Space**: `World` (smoke puffs trail realistically behind moving vehicle).
- **Emission**: Rate over Time: `12` particles/second.
- **Shape**: `Hemisphere` (Radius: 0.15).
- **Size over Lifetime**: Curve scaling from $0.4 \to 1.0 \to 1.5$.
- **Color over Lifetime**: Alpha fades in to 220 in first 10%, then fades to 0 over final 40%.
- **Renderer**:
  - Render Mode: `Billboard`.
  - Material: `Universal Render Pipeline/Particles/Unlit` (Alpha Blended).
  - Enable GPU Instancing: `true`.
- **Max Particles**: `35`.

---

## Recipe 4: Critical Damage Engine Fire (`Socket_DamageFire`)

Activated automatically by `RuntimeVisualDirector` when vehicle drops to $\le 25\%$ HP.

### Module Parameters
- **Duration**: `0.8s`, **Looping**: `true`.
- **Start Lifetime**: `0.3s – 0.5s` (flickering flame turnover).
- **Start Speed**: `2.5 – 4.0` (violent jetting flame).
- **Start Size**: `0.5 – 0.8`.
- **Start Color**: Bright incandescent yellow `RGBA(255, 230, 110, 255)`.
- **Gravity Modifier**: `-0.3` (fast vertical draft).
- **Simulation Space**: `Local` or `World`.
- **Emission**: Rate over Time: `18` particles/second.
- **Color over Lifetime**: Rapid transition from Yellow ($t=0$) $\to$ Bright Orange ($t=0.3$) $\to$ Dark Red ($t=0.7$) $\to$ Black Soot with 0 Alpha ($t=1.0$).
- **Renderer**:
  - Render Mode: `Billboard`.
  - Material: `Universal Render Pipeline/Particles/Unlit` (Additive or Premultiply).
  - Enable GPU Instancing: `true`.
- **Max Particles**: `25`.

---

## Recipe 5: Surge Arc / Electrical Discharge

Designed for arc weapons and EMP disablers (`SurgeArcTank`, `LightningTank`).

### Module Parameters
- **Duration**: `0.2s`, **Looping**: `false`.
- **Start Lifetime**: `0.1s – 0.18s`.
- **Start Speed**: `0.5`.
- **Start Size**: `1.2 – 1.8`.
- **Start Color**: Cyan-electric `RGBA(120, 240, 255, 255)`.
- **Emission**: 1 burst of `6 – 8` particles.
- **Noise Module**:
  - Enabled: `true`.
  - Strength: `1.5`, Frequency: `0.8`, Scroll Speed: `2.0` (creates erratic jagged electrical jitter).
- **Renderer**:
  - Material: `Universal Render Pipeline/Particles/Unlit` (Additive).
  - Texture: Lightning branch / electric arc strip.

---

## Recipe 6: Ground Dust & Tread Kickup (`Socket_GroundContact`)

Designed for vehicle wheels and tank tracks on unpaved terrain.

### Module Parameters
- **Duration**: `1.0s`, **Looping**: `true`.
- **Start Lifetime**: `0.6s – 1.0s`.
- **Start Speed**: `1.0 – 2.0` backward along vehicle forward vector.
- **Start Size**: `0.8 – 1.4`.
- **Start Color**: Sandy tan `RGBA(180, 160, 130, 140)`.
- **Simulation Space**: `World`.
- **Emission**: Rate over Distance: `2.5` particles/meter (emits only while vehicle is moving!). Rate over Time: `0`.
- **Color over Lifetime**: Alpha fades out smoothly over lifetime.
- **Renderer**:
  - Material: `Universal Render Pipeline/Particles/Unlit` (Alpha Blended).
  - Soft Particles: Enabled (`_SoftParticlesNearFadeDistance = 0.1`, `_SoftParticlesFarFadeDistance = 1.0`).
- **Max Particles**: `20`.
