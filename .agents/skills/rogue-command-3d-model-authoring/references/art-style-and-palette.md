# Rogue Command 3D Art Style & Color Palette Guide

**Updated:** 2026-10-18  
**Scope:** Aesthetic rules, 3-tier visual hierarchy, 5-tone PBR palette, and bevel physics for Rogue Command.

---

## 1. Aesthetic Lineage & Core Philosophy

Rogue Command (developed by **feneq**) features a clean, chunky, hard-surface low-poly aesthetic combining Kenney-inspired modularity with high-clarity RTS gameplay:
1. **High-Readability RTS Stance**: Bold silhouettes optimized for an elevated camera (~45°–60° tilt) where units appear 30–120 pixels tall.
2. **Single-Segment Chamfers (The Specular Glint Rule)**: Crisp 1-segment bevels (`segments = 1`, `width = 0.02m–0.06m`) with flat shading (`use_smooth = False`) on mechanical edges that catch bright specular sun glints.
3. **Disciplined 5-Tone Palette**: Solid-color PBR materials communicating function, faction, and structural hierarchy.
4. **The Squint Test**: Zooming out the viewport until the model is 50px tall must communicate archetype, role, and heading in < 250ms.

---

## 2. 3-Tier Visual Read & Archetype Proportions

- **Tier 1: Macro Silhouette (70%)**: Overall volume, stance, ground clearance, and mass balance.
- **Tier 2: Meso Mechanical Geometry (20%)**: Continuous track contours, articulated legs, tilt-rotors, engine louvers.
- **Tier 3: Micro High-Contrast Accents (10%)**: Recessed emissive optics (HDR ×1.5–2.5), hazard stripes, muzzle collars.

| Archetype | Native Template | Proportions (X × Y × Z) | Silhouette Signature & Mass Center |
|---|---|---|---|
| **Fast Scout** | `PCXBaseTank` | ~2.1m × 2.6m × 1.5m | Low sloped glacis, wide stance, elevated sensor mast. Low mass center. |
| **Strike Bike** | `BombBike` | ~1.1m × 2.0m × 1.3m | Narrow two-wheel footprint, aggressive forward rake. Forward-leaning. |
| **Heavy Artillery** | `ArtilleryTank` | ~2.8m × 3.6m × 1.9m | Broad tracks, rear recoil spade, elongated cannon barrel. Rear-biased. |
| **Assault Walker** | `GatlingWalker` | ~2.2m × 2.0m × 2.7m | Digitigrade bipedal legs, twin underslung rotary guns. Dynamic vertical. |
| **Surge Arc Tank** | `LightningTank` | ~2.3m × 2.8m × 1.6m | Segmented hull, central toroidal induction coil, forward discharge prongs. |
| **Hover Skimmer** | `PCXBaseTank` | ~2.5m × 2.9m × 1.6m | Repulsor skirt, ventral thrusters, lateral strakes. Floating 0.35m–0.55m. |
| **Siege Spider** | `GatlingWalker` | ~4.6m × 4.8m × 1.8m | 4 outrigger crawler legs, central turret, ground footpads. Stable 4-point stance. |
| **VTOL Gunship** | `PCXBaseTank` | ~3.8m × 4.7m × 1.5m | Swept wings, dual swiveling ducted tilt-rotors, sensor chin pod. |
| **Half-Track APC** | `PCXBaseTank` | ~2.2m × 3.8m × 2.2m | Front steerable wheels, rear tracks, armored troop bay with clamshell doors. |
| **Super-Heavy** | `ArtilleryTank` | ~3.3m × 4.6m × 2.4m | 4 independent track pods, massive superstructure, command radar. |
| **Colossus Titan** | `GatlingWalker` | ~3.0m × 2.8m × 3.8m | Massive bipedal legs ($2.3\text{m}$ hip height), citadel torso, twin cannons. |

---

## 3. The 5-Tone Color Palette Standard

| Material Role | Proportion | Function & Description | Metallic | Smoothness | Emission |
|---|---|---|---|---|---|
| **Primary Armor** | 40–45% | Main chassis, lower hull, armor sponsons | 0.35–0.45 | 0.60–0.65 | None |
| **Light Armor** | 20–25% | Turret cheeks, upper deck, hatch covers | 0.30–0.35 | 0.58–0.62 | None |
| **Mechanical Gunmetal** | 15–20% | Cannon barrels, roll bars, suspension, axles | 0.74–0.80 | 0.68–0.75 | None |
| **Tread / Rubber** | 10–15% | Track shoes, road wheels, tire rubber | 0.08–0.18 | 0.28–0.34 | None |
| **Warning Accent** | 3–5% | High-contrast identification hazard stripes | 0.16–0.20 | 0.60–0.65 | None |
| **Emissive Tech/Optics** | 1–3% | Sensor lenses, headlamps, plasma coils | 0.15–0.25 | 0.75–0.85 | **HDR ×1.5–2.5** |

### Faction Presets
- **Vanguard (Slate Teal)**: Primary `#244552` (0.12, 0.19, 0.24), Light `#435f62`, Accent Amber `#e86c20`, Emissive Cyan `#1fcdbf`.
- **Siege (Rust Ochre)**: Primary `#684126` (0.26, 0.14, 0.075), Light `#8c6d48`, Accent Yellow `#f2a91d`, Emissive Orange `#ff5511`.
- **Syndicate (Deep Cobalt)**: Primary `#14243b` (0.055, 0.11, 0.20), Light `#526070`, Accent Magenta `#d41876`, Emissive Cyan `#00f0ff`.
- **Arctic (Tundra Navy)**: Primary `#c2d1e0` (0.72, 0.78, 0.85), Light `#8494a8`, Accent Red `#e61e26`, Emissive Glacial Blue `#26d9ff`.

---

## 4. Shading & Edge Beveling Guidelines

### Beveling Physics
- **1-Segment Chamfers Only**: Always single-segment (`segments = 1`). Never multi-segment rounded bevels.
- **Scale-Proportional Widths**:
  - Large hull / armor blocks: `0.04m - 0.08m`
  - Medium turrets / sponsons: `0.025m - 0.04m`
  - Small brackets / collars: `0.010m - 0.018m`
- **Flat Shading**: Keep `use_smooth = False` on chamfers to produce the distinctive RTS specular glint.

### Topological BMesh RGBA Vertex Shading
- **Channel R (Cavity AO)**: $1.0$ on zenith/open faces; $0.25 - 0.40$ on undersides and recesses ($\Delta > 10^{-4}$).
- **Channel G (Edge Wear)**: $0.7 - 1.0$ on sharp convex edges ($\ge 35^\circ$, $\Delta < -10^{-4}$); strictly $0.0$ on flat/concave seams.
- **Channel B (Thermal Soot)**: $0.0$ clean metal; scales to $1.0$ at weapon muzzles ($R = 0.60\text{m}$) and exhausts ($R = 0.45\text{m}$) with $(1 - d/R)^2$ dropoff.
- **Channel A (Team Mask)**: $1.0$ on armor plates for runtime tinting; $0.0$ on mechanical parts.
