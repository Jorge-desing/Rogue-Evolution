# Rogue Command 3D Art Style & Color Palette Guide

This guide establishes the visual rules and aesthetic principles for authoring grounded, mechanically plausible 3D vehicles and unit models that remain legible in Rogue Command's stylized sci-fi RTS world.

---

## 1. Aesthetic Lineage & Core Philosophy

Use a grounded, stylized military sci-fi language: readable from the elevated RTS camera, with believable mass, connected machinery, and surfaces that respond like armor, cast housings, rubber, and machined metal. The desired finish is not photorealism; it is a more convincing, less primitive-looking version of the project's clear faction and gameplay language.
The checks below adapt public RTS art-direction and Blender/Unity modeling guidance to this project; see [Design Guides Adapted](design-guides-adapted.md) for sources and the full workflow.
1. **Designed Silhouettes**: Distinct proportions and a coherent outer shell replace a stack of uniform boxes. Use faceted planes where they describe armor and controlled curvature where it describes cast or formed parts.
2. **High-Readability RTS Stance**: Keep the archetype and facing legible from the elevated, angled camera (~45°–60° isometric/orthographic tilt), including when the unit is roughly 30–120 pixels tall.
3. **Believable Fabrication**: Distinguish welded plates, cast armor, access panels, suspension, and attached equipment through thickness, overlap, recesses, joints, and scale-aware edge radii.
4. **Functional Color and Material Blocking**: Keep a disciplined 5-to-6-tone faction palette, then use restrained value and roughness variation to distinguish structural roles without adding visual noise.

---

## 2. Silhouette Readability & The 3-Tier Hierarchy

Every unit must pass the **Silhouette Test**: from the elevated gameplay camera and the main orthographic views, the black silhouette should identify its role and forward orientation before color or material is considered. The project's roughly 50-pixel review size and 250ms glance target are internal heuristics, not external standards. The camera-first review is adapted from RTS art direction that prioritizes form and removes noisy micro-detail.

- **Tier 1 (Macro Silhouette, 65%)**: Proportion, stance, ground clearance, and largest hull/turret masses. Identifies archetype (Scout vs Siege vs Mech vs Bike).
- **Tier 2 (Meso Mechanical Construction, 25%)**: Hull transitions, turret seating, track or suspension depth, weapon mounts, engine deck, and access systems. Explains how the unit is built and operated.
- **Tier 3 (Micro Accents, 10%)**: Optics, fasteners, seams, markings, and localized wear. Directs attention but never carries the silhouette on its own.

Treat 65/25/10 as relative visual priorities for this project, not measured percentages or an external standard.

### Form Construction Checklist
- Establish the primary hull/cabin/turret as a coherent shell from tapered profiles and intersecting volumes with intentional transitions; do not leave obvious box-to-box joins on the visible silhouette.
- Vary dimensions and cross-sections according to each component's job. Taper barrels and booms, soften cast shoulders, recess wheel wells and optics, and give panels enough thickness to read at gameplay scale.
- Make mechanical connections visible where they matter: turret ring under turret, suspension arms into wheel hubs, gun breech into mantlet, hose ends on fittings, and exhausts rooted in the engine deck.
- Keep intentional asymmetry purposeful. Repeated details should follow a real layout (bolt rows, cooling banks, track shoes) and should not become all-over surface decoration.
- Compare a neutral clay render under grazing light against the textured model. Geometry must carry the form without relying on camouflage, AO, vertex wear, or emissive accents.

### Archetype Silhouette Matrix

| Archetype | Native Template | Silhouette Features | Dimensions (W × L × H) | Mass Distribution |
|---|---|---|---|---|
| **Fast Scout** | `PCXBaseTank` | Low profile, sloped glacis, wide stance, elevated sensor mast | ~2.1m × 2.6m × 1.5m | Low and central |
| **Strike Bike** | `BombBike` | Ultra-narrow 2-wheel footprint, aggressive forward rake, roll cage | ~1.1m × 2.0m × 1.3m | Forward-leaning |
| **Heavy Artillery** | `ArtilleryTank` | Broad tracked chassis, rear outriggers, massive elongated cannon barrel | ~2.8m × 3.6m × 1.9m | Rear-biased |
| **Assault Walker** | `GatlingWalker` | Vertical bipedal stance, digitigrade inverted knee, twin rotary guns | ~2.2m × 2.0m × 2.7m | Dynamic vertical |
| **Surge Arc Tank** | `LightningTank` | Segmented tracked hull, central toroidal reactor coil, emitter prongs | ~2.3m × 2.8m × 1.6m | Central reactor core |
| **Hover Skimmer** | `PCXBaseTank` | Anti-grav cushion, ventral thruster bells, lateral stabilizer fins | ~2.5m × 2.9m × 1.6m | Floating (Z ~0.24–0.45m) |
| **Siege Spider** | `GatlingWalker` | 4 wide outrigger crawler legs, central turret, hydraulic knee knuckles | ~4.6m × 4.8m × 1.8m | Low 4-point footprint |
| **VTOL Gunship** | `PCXBaseTank` | Sleek lifting body, swept wings, dual ducted tilt-rotors, V-tail | ~3.8m × 4.7m × 1.5m | Wing-biased |
| **Half-Track APC** | `PCXBaseTank` | Front wheels, rear continuous tracks, armored troop cabin | ~2.2m × 3.8m × 2.2m | Mid-chassis |
| **Super-Heavy** | `ArtilleryTank` | 4 independent track pods, massive superstructure, dual heavy cannons | ~3.3m × 4.6m × 2.4m | Broad 4-pod base |
| **Recon Buggy** | `BombBike` | Wide stance all-terrain tires, tubular roll cage, exposed rear engine | ~2.3m × 3.2m × 2.2m | Low off-road stance |

---

## 3. The 5-Tone Color Palette Rule

Every vehicle follows the disciplined 5-to-6-tone structural PBR material standard:

| Material Role | Proportion | Vanguard (`vanguard`) | Heavy Siege (`siege`) | Syndicate (`syndicate`) | PBR Properties |
|---|---|---|---|---|---|
| **Primary Armor** | 40–45% | Slate Teal (`#244552`) | Rust Ochre (`#684126`) | Deep Cobalt (`#14243b`) | Met: $0.35–0.55$, Smooth: $0.50–0.68$ |
| **Secondary Armor** | 20–25% | Seafoam Slate (`#435f62`) | Desert Sand (`#8c6d48`) | Titanium Grey (`#526070`) | Met: $0.25–0.60$, Smooth: $0.55–0.72$ |
| **Mechanical Gunmetal** | 15–20% | Charcoal Steel (`#1b262b`)| Cast Iron (`#1e1f22`) | Anodized Black (`#12161c`)| Met: $0.74–0.80$, Smooth: $0.65–0.75$ |
| **Tread / Rubber** | 10–15% | Carbon Black (`#161b1e`) | Carbon Black (`#161b1e`) | Dark Tungsten (`#12161c`) | Met: $0.08–0.18$, Smooth: $0.28–0.34$ |
| **Warning Accent** | 3–5% | Signal Amber (`#e86c20`) | Hazard Yellow (`#f2a91d`) | Electric Magenta (`#d41876`)| Met: $0.15–0.20$, Smooth: $0.60–0.70$ |
| **Emissive Optics** | 1–3% | Arc Cyan (`#1fcdbf`, 1.8×) | Molten Orange (`#ff5511`, 2.2×)| Plasma Cyan (`#00f0ff`, 2.5×) | Met: $0.10–0.20$, Smooth: $0.76–0.88$ |

### Camouflage Presets (`--camo`)
- **`desert`**: Coyote tan (`#6b522e`), desert khaki secondary (`#8c7042`), matte finish with high-visibility signal markings.
- **`urban`**: Concrete slate (`#383d42`), asphalt grey secondary (`#5c636b`), dark tungsten mechanics, cool cyan optics.
- **`arctic`**: Glacial tundra white (`#c2d1e0`), frost grey secondary (`#8494a8`), dark carbon rubber, crimson hazard accents.
- **`woodland`**: Olive drab (`#2e4024`), forest green secondary (`#475c38`), dark gunmetal, high-visibility yellow bands.

---

## 4. Scale-Aware Edge Radii & Surface Shading

Use edge treatment to describe fabrication and catch light, not to make every object look like the same beveled primitive.
- **Treat edges selectively**: Keep broad armor planes clean. Chamfer an edge when it is a cut plate edge, protective corner, or deliberate facet; use a rounded transition for cast housings, formed covers, wheel shoulders, and other surfaces that should not read as a sharp folded plate.
- **Scale the treatment to the part**: As a starting guide, use about `0.003m–0.010m` on small machined details, `0.006m–0.025m` on exposed armor plate edges, and `0.02m–0.06m` on large cast or molded housings. Prefer a fraction of local part thickness over a universal bevel width. Reduce these values when they make a panel look inflated.
- **Choose segments by shape**: A 1-segment bevel is appropriate for a crisp chamfer. Use 2–4 segments for visible rolled edges and rounded transitions; use enough radial sides on wheels, pipes, and barrels that their gameplay-scale silhouette does not look square. Keep hidden geometry economical.
- **Preserve intentional planes**: Keep true armor facets flat-shaded, but shade curved surfaces smoothly by angle. Inspect normal transitions and highlights in a neutral clay render; do not use weighted normals to disguise a bad cross-section or intersecting shells.
- **Avoid uniform beveling**: Never apply the same wide bevel to every edge. Large, soft corners on all panels create a toy-like look and erase structural differences.

Keep shape and shading checks separate: bevels change mesh geometry and highlights; smooth shading and weighted normals change how existing faces shade. If a black silhouette still looks boxy, revise the mesh cross-section instead of increasing normal weights.

---

## 5. Functional Construction Details

Every added detail should explain protection, access, cooling, sensing, propulsion, ammunition, or field repair. Avoid decorative repetition and scattered “greebles” without a visible attachment point.

1. **Continuous Track Loops**: Sloped front approach angles (~25°–35° climb), rounded return idlers, and individual tread shoes (`0.15m–0.22m` spacing).
2. **Road Wheel Rims & Hubs**: Stepped cylinders: outer rubber tire (matte), recessed rim, and central 6-bolt planetary axle cap (gunmetal).
3. **Engine Grilles**: 2–4 angled chamfered cooling louvers on the rear engine deck.
4. **Sensor Masts**: Armored hood visor with recessed glowing optical lens.
5. **Weapon Sleeves**: Stepped muzzle brakes or heat-dissipation collars at the barrel tip.
6. **Conduit Rails**: Insulated charge rails along flanks for energy weapons.
7. **Applique Armor (ERA)**: Spaced armor tiles on frontal glacis angled at 20°–35°.
8. **Fasteners & Seams**: Place small fasteners along real plate flanges, access covers, and mounting rails; keep scale and spacing consistent with the panel they secure.

---

## 6. Weapon Hardpoints & Kinematics Standards

- **Heavy Ballistic Cannons**: Barrel length $1.4	imes$ to $2.2	imes$ turret length with breech collar, double-baffle muzzle brake, and parallel recoil dampers.
- **Rotary Gatling Clusters**: 6-barrel bundles with clamp collars at 45% and 95% of length, central drive axle, and offset ammo chute.
- **Missile Pods**: Multi-cell honeycomb arrays ($2	imes 3$, $2	imes 2$, or $3	imes 3$) recessed inside an armored box tilted $10^\circ–20^\circ$.
- **Arc Weapons**: Twin/triple toroidal induction coils around a plasma cathode with convergent discharge prongs.
- **Biped Walkers**: Digitigrade stance (thigh $-15^\circ$ to $-25^\circ$, shin $+20^\circ$ to $+35^\circ$) with hydraulic rams and 3-toed stabilizer foot resting at $Z \ge 0.0\text{m}$.

---

## 7. Draw-Call Optimization via Material Batching

- **Blockout and Refinement**: Start with clean primitives and explicit materials, then replace visible silhouette masses with refined shells, transitions, or shaped components before the production export.
- **Material-Based Unification for Export**: Group and join static submeshes by PBR material via `batch_and_join_by_material`.
- **Target Output**: 4 to 6 unified mesh objects per vehicle (one per material, plus independent animated pivots like `TurretBase` or `MainBarrel`).

---

## 8. Multi-Channel Procedural Vertex Shading (`Mesh.colors32`)

Tactile shading is encoded directly into vertex colors to avoid heavy texture lookups:

| Channel | Shader Role | Technical Mapping & Invariant |
|---|---|---|
| **R (Red)** | Ambient Occlusion & Cavity Dirt | $0.25–0.40$ on undersides, wheel recesses, and crevices; $1.0$ on zenith faces. |
| **G (Green)** | Curvature & Edge Wear | $0.70–1.0$ on sharp convex edges (dihedral angle $\ge 35^\circ$); $0.0$ on flat planes. |
| **B (Blue)** | Thermal Soot & Weathering | $0.0$ default clean base metal; scales to $1.0$ at muzzles and exhaust tips with quadratic dropoff $(1 - d/R)^2$. Never invert. |
| **A (Alpha)** | Team Tint Mask | $1.0$ on primary and light armor plates for runtime tinting; $0.0$ on gunmetal and rubber treads. |

---

## 9. Multi-Level LOD Hierarchy & Collision Geometry

- **LOD0 (Combat View, 100%)**: Full fidelity ($1,500$–$5,000$ triangles), complete chamfers, road wheel lugs, and sensor antennas.
- **LOD1 (Mid-Range View, 50%)**: Preserved planar boundaries, decimated fine bevels ($800$–$2,000$ triangles).
- **LOD2 (Far Tactical View, 22%)**: Coarse blockout silhouette ($300$–$700$ triangles) for extreme RTS camera zoom.
- **Collision Hulls**: Flat-shaded `<Prefix>_COLLIDER_Hull` and `<Prefix>_COLLIDER_Turret` matching vehicle bounding boxes (`is_collider = True`).

---

## 10. Standardized Gameplay Sockets

Models emit standardized empty locators in the FBX hierarchy (`Socket_MuzzlePrimary`, `Socket_MuzzleSecondary`, `Socket_TurretPivot`, `Socket_RecoilPrimary`, `Socket_EngineAudio`, `Socket_WeaponAudio`, `Socket_DamageSmoke`, `Socket_DamageFire`, `Socket_GroundContact`). Sockets must never repeat vehicle prefixes.
*Detailed transform hierarchy and runtime binding: [Native Hierarchy & Pivots](native-hierarchy-and-pivots.md) and [AGENT-HANDOFF.md](../../../../docs/research/AGENT-HANDOFF.md).*
