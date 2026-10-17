# Rogue Command 3D Art Style & Color Palette Guide

This guide establishes the visual rules and aesthetic principles for authoring 3D vehicles and unit models that fit seamlessly into Rogue Command's stylized sci-fi RTS world.

---

## 1. Aesthetic Lineage & Core Philosophy

Rogue Command (developed by **feneq**) is defined by a clean, chunky, hard-surface low-poly aesthetic. Its visual DNA combines:

1. **Kenney-Inspired Modular Primitives**: Geometric vocabulary rooted in modular vehicle kits (faceted hulls, sloped chamfers, cylindrical exhausts, piston brackets, and segmented track skirts).
2. **High-Readability RTS Stance**: Bold silhouettes optimized for an elevated, angled camera (~45°–60° isometric/orthographic tilt) where units appear between 30 and 120 pixels tall on screen.
3. **Tactile Machined Surfaces**: Crisp, single-segment chamfers (bevels) on mechanical edges that catch specular sun glints, eliminating the sterile look of pure CAD sharp edges without requiring heavy textures.
4. **Functional Color Blocking**: A disciplined 5-to-6-tone palette where colors communicate function, team allegiance, and structural hierarchy.

---

## 2. Silhouette Readability & The 3-Tier Hierarchy

Every unit must pass the **Squint Test**: when zoomed out or viewed as a solid black silhouette against a neutral gray background, its gameplay role and forward orientation must be immediately identifiable in less than a quarter-second.

```
+-------------------------------------------------------------+
| TIER 1: Macro Silhouette (70% of visual read)              |
| - Overall volume, footprint, aspect ratio, mass balance    |
| - Identifies archetype: Scout vs Siege vs Mech vs Bike     |
+-------------------------------------------------------------+
                              |
                              v
+-------------------------------------------------------------+
| TIER 2: Meso Mechanical Geometry (20% of visual read)       |
| - Turret shape, track sponsons, engine deck, intake grilles|
| - Identifies unit function, weapon class, and propulsion   |
+-------------------------------------------------------------+
                              |
                              v
+-------------------------------------------------------------+
| TIER 3: Micro High-Contrast Accents (10% of visual read)    |
| - Emissive lenses, hazard stripes, muzzle brakes, antenna  |
| - Directs the eye to focal points (sensors, weapons, core) |
+-------------------------------------------------------------+
```

### Archetype Silhouette Guidelines

| Archetype | Native Template | Silhouette Features | Proportions (X × Y × Z) | Visual Mass Center |
|---|---|---|---|---|
| **Fast Scout** | `PCXBaseTank` | Low profile, sloped frontal glacis, wide stance, elevated sensor mast | ~2.1m × 2.6m × 1.5m | Low and central |
| **Strike Bike** | `BombBike` | Ultra-narrow two-wheel footprint, aggressive forward rake, outrigger charges | ~1.1m × 2.0m × 1.3m | Forward-leaning, compact |
| **Heavy Artillery** | `ArtilleryTank` | Broad tracked chassis, rear recoil spade/outriggers, massive elongated cannon barrel | ~2.8m × 3.6m × 1.9m | Rear-biased, heavy ground contact |
| **Assault Walker** | `GatlingWalker` | Vertical bipedal stance, inverted knee (digitigrade), twin underslung rotary guns | ~2.2m × 2.0m × 2.7m | Upper torso, dynamic vertical |
| **Surge / Arc Tank** | `LightningTank` | Segmented tracked hull, central toroidal reactor/induction coil, forward emitter prongs | ~2.3m × 2.8m × 1.6m | Central reactor core |

---

## 3. The 5-Tone Color Palette Rule

Do not use random color assortments or muddy textures. Rogue Command units use crisp, solid-color PBR materials structured into functional roles:

```
+-------------------------------------------------------------------------+
| [Primary Armor: 40-45%]        [Secondary Light Armor: 20-25%]          |
| Base faction chassis           Upper deck, turret cheeks, sponson caps  |
|                                                                         |
| [Mechanical Gunmetal: 15-20%]  [Functional Tread/Rubber: 10-15%]        |
| Barrels, frame, exhausts       Treads, road wheels, dark weather seals  |
|                                                                         |
| [High-Contrast Warning: 3-5%]  [Emissive Tech / Optics: 1-3%]           |
| Hazard stripes, battery rails  Sensors, headlights, arc coils (GLOWING) |
+-------------------------------------------------------------------------+
```

### Recommended Faction / Palette Sets

#### Palette A: Standard Vanguard (Sample-Native Expansion Default)
- **Primary Armor**: Slate Teal (`#244552` / `RGB(36, 69, 82)`)
  - *Metallic*: 0.42 | *Smoothness*: 0.63
- **Secondary Light Armor**: Seafoam Slate (`#435f62` / `RGB(67, 95, 98)`)
  - *Metallic*: 0.32 | *Smoothness*: 0.60
- **Mechanical Gunmetal**: Deep Charcoal Steel (`#1b262b` / `RGB(27, 38, 43)`)
  - *Metallic*: 0.74 | *Smoothness*: 0.70
- **Tread / Rubber**: Carbon Black (`#161b1e` / `RGB(22, 27, 30)`)
  - *Metallic*: 0.18 | *Smoothness*: 0.34
- **Warning Accent**: Signal Amber (`#e86c20` / `RGB(232, 108, 32)`)
  - *Metallic*: 0.18 | *Smoothness*: 0.64
- **Emissive Optics**: Arc Cyan (`#1fcdbf` / `RGB(31, 205, 191)`)
  - *Metallic*: 0.20 | *Smoothness*: 0.76 | *Emissive*: Enabled (HDR ×1.8)

#### Palette B: Heavy Siege / Industrial Outpost
- **Primary Armor**: Rust Ochre (`#684126` / `RGB(104, 65, 38)`)
  - *Metallic*: 0.35 | *Smoothness*: 0.50
- **Secondary Light Armor**: Desert Sand (`#8c6d48` / `RGB(140, 109, 72)`)
  - *Metallic*: 0.25 | *Smoothness*: 0.55
- **Mechanical Gunmetal**: Cast Iron (`#1e1f22` / `RGB(30, 31, 34)`)
  - *Metallic*: 0.80 | *Smoothness*: 0.65
- **Warning Accent**: Industrial Hazard Yellow (`#f2a91d` / `RGB(242, 169, 29)`)
  - *Metallic*: 0.15 | *Smoothness*: 0.60
- **Emissive Optics**: Molten Orange (`#ff5511` / `RGB(255, 85, 17)`)
  - *Metallic*: 0.10 | *Smoothness*: 0.80 | *Emissive*: Enabled (HDR ×2.2)

#### Palette C: High-Tech Electrodynamic / Syndicate
- **Primary Armor**: Deep Cobalt (`#14243b` / `RGB(20, 36, 59)`)
  - *Metallic*: 0.55 | *Smoothness*: 0.68
- **Secondary Light Armor**: Titanium Grey (`#526070` / `RGB(82, 96, 112)`)
  - *Metallic*: 0.60 | *Smoothness*: 0.72
- **Mechanical Gunmetal**: Anodized Black (`#12161c` / `RGB(18, 22, 28)`)
  - *Metallic*: 0.78 | *Smoothness*: 0.75
- **Warning Accent**: Electric Magenta (`#d41876` / `RGB(212, 24, 118)`)
  - *Metallic*: 0.20 | *Smoothness*: 0.70
- **Emissive Optics**: Overload Plasma Cyan (`#00f0ff` / `RGB(0, 240, 255)`)
  - *Metallic*: 0.15 | *Smoothness*: 0.88 | *Emissive*: Enabled (HDR ×2.5)

---

## 4. Edge Beveling & The Specular Highlight Rule

In real-time low-poly rendering, flat surfaces with razor-sharp 90° edges suffer from **specular extinction**: the geometric edge has zero surface area, so the directional sun light jumps abruptly from 100% lit face to 0% unlit face without any highlight.

```
Razor Edge (Flat, Digital, Lifeless):
    |
    |  <--- 90° corner has 0 thickness, no highlight
    +--------

Chamfered Edge (Machined, Tactile, Catches Specular Glints):
    |
    \  <--- 45° chamfer reflects sun light directly into RTS camera!
     +-------
```

### Rules for Beveling in Rogue Command Models:
1. **1-Segment Chamfers**: Always use a single segment bevel (`segments = 1`). Never use multi-segment curved bevels on low-poly models—they waste polygon budget without adding readability at RTS distance.
2. **Width Proportional to Part Scale**:
   - Main hull / large armor blocks: `width = 0.04m - 0.08m`
   - Medium parts (turrets, weapon cowlings, sponsons): `width = 0.025m - 0.04m`
   - Small details (sensor pods, brackets, muzzle rings): `width = 0.010m - 0.018m`
3. **Flat Shading Preserved**: Keep `polygon.use_smooth = False` on chamfered faces. The crisp transition between the flat hull face and the angled chamfer face is what produces the distinctive stylized low-poly glint.

---

## 5. Visual Enhancement Details (Greebles with Purpose)

Do not add random visual noise. Every mechanical detail on a Rogue Command unit should imply a clear functional purpose:

1. **Continuous Track Loops**:
   - Rather than floating square boxes on a flat hull, shape tracks with sloped front approach angles (~25°–35° climb) and rounded return idlers.
   - Model individual tread shoes (`0.15m–0.22m` spacing) along the top and bottom runs to create rhythmic surface breakups under dynamic shadows.
2. **Road Wheel Rims & Hub Caps**:
   - Stepped cylinders: outer rubber tire (matte), recessed wheel rim (medium tone), and protruding central axle cap with hex bolts (gunmetal).
3. **Engine Grilles & Louvers**:
   - Slatted intake vents on the rear engine deck (2–4 angled chamfered slats) to ground the power plant.
4. **Sensor Masts & Optical Arrays**:
   - Elevate sensor clusters on braced pylons. Recess the glowing lens within an armored visor or hood so the emissive face contrasts sharply against the dark shadow of the housing.
5. **Weapon Sleeves & Muzzle Collars**:
   - Add stepped muzzle brakes or heat-dissipation collars at the barrel tip to give weapons visual punch.
6. **Battery Rails & Conduit Tubes**:
   - For energy or explosive units, route visible conduit pipes or insulated charge rails along the flanks to telegraph volatility.
7. **Explosive Reactive Armor (ERA) & Applique Tiles**:
   - Spaced armor tiles on frontal glacis or turret cheeks angled at 20°–35° to reflect incoming fire and break up broad, flat expanses.
8. **Hexagonal Bolt Seams**:
   - 6-sided bolt heads placed along structural seams or suspension knuckles to convey industrial rigidity without mesh complexity.

---

## 6. Weapon Hardpoints & Mobility Kinematics Standards

### A. Weapon Silhouette & Caliber Ratios
- **Heavy Ballistic Cannons**: Barrel length should be $1.4\times$ to $2.2\times$ the turret length with a prominent breech collar and double-baffle muzzle brake. Recoil dampers should sit parallel to the barrel vector.
- **Rotary Gatling Clusters**: 6-barrel circular bundles with clamp collars at 45% and 95% of barrel length. Always include a central drive axle and an offset ammo feed chute to sell mechanical operation.
- **Missile / Rocket Pods**: Multi-cell honeycomb arrays ($2\times 3$, $2\times 2$, or $3\times 3$) recessed inside an armored box tilted upward by $10^\circ–20^\circ$. Bright conical warheads peeking out provide high-contrast focal points.
- **Electromagnetic Arc Weapons**: Twin or triple toroidal induction coils surrounding a central glowing plasma cathode, ending in convergent prongs angled inward toward a focal arc convergence zone.

### B. Bipedal Walker Kinematics
- **Digitigrade Stance**: Upper thigh angled backwards ($-15^\circ$ to $-25^\circ$), lower shin angled forwards ($+20^\circ$ to $+35^\circ$), with heavy hydraulic pistons bridging the joints.
- **Ground Contact Footprint**: Multi-toed stabilizer foot with a wide center toe, an angled lateral outrigger, and a rear heel claw. The lowest vertex must rest precisely on the ground ($Z \ge 0.0\text{m}$) to eliminate floating or clipping into terrain.

---

## 7. Draw-Call Batching & Performance Guidelines

To maintain 60+ FPS in dense RTS battles with 50+ units on screen:
- **Modular Kitbashing during Authoring**: Author with clean geometric primitives, separate objects, and explicit materials.
- **Material-Based Mesh Unification for Export**: Group and join static submeshes by their PBR material slot (`batch_and_join_by_material`).
- **Target Submesh Count**: A completed vehicle should contain **4 to 6 unified mesh objects** (one per material, plus any independent animated pivots like `TurretBase` or `MainBarrel`).

