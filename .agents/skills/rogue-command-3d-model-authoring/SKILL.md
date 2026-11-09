---
name: rogue-command-3d-model-authoring
description: "Design, create, or refine original 3D vehicle and unit visuals for Rogue Command with Blender and the pinned Unity authoring project. Use alongside unit and AssetBundle skills when the task changes a model or its in-game appearance."
---

# Rogue Command 3D Model Authoring

Authoritative guide for modeling, texturing, rigging, and exporting 3D vehicle and structure assets for Rogue Command.

---

## 1. Verified Architecture & Directory Invariants

- **Authoring Tools:** Blender 4.5.14 LTS (`.tools/Blender-4.5.14/.../blender.exe`) + Unity 2021.3.45f2 (Mono x64). Material Maker 1.7 for PBR images. Zero remote dependencies.
- **Authoring Project:** `unity/RogueCommandAssetAuthoring/`. Preserve all `ProjectSettings`, package manifests, and version control metadata.
- **Directory Layout:**
  - Blender Sources: `content/source-art/*.blend`
  - Python Scripts: `tools/Build-*.py`, `tools/generate_unit_model.py`, `tools/RogueModelKit.py`
  - Unity FBX: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/*.fbx`
  - Prefabs & Materials: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/`
  - AssetBundles: `content/packs/<pack-id>/bundles/*.bundle`
  - Previews & Logs: `artifacts/previews/` (ignored)
- **Script Parity Contract:** The copy in `.agents/skills/rogue-command-3d-model-authoring/scripts/RogueModelKit.py` must remain byte-identical to `tools/RogueModelKit.py` (verified by `test_model_kit_skill_copy.py`).

---

## 2. Coordinate System & Export Invariants

| Dimension | Blender Axis | Unity Axis | Coordinate Constraint |
|---|---|---|---|
| **Forward** | `+Y` | `+Z` | Vehicle front points toward `+Y` / `+Z` |
| **Up** | `+Z` | `+Y` | Vertical elevation |
| **Right / Starboard**| `+X` | `+X` | Lateral axis |
| **Ground Contact**| $Z \ge 0.0$ | $Y \ge 0.0$ | Lowest vertex rests at $0.0\text{m} \le Z_{\min} \le 0.08\text{m}$ ($Z_{\min} \le 0.55\text{m}$ hover/vtol) |
| **Origin Pivot** | `(0, 0, 0)` | `(0, 0, 0)` | Centered at ground plane midpoint |

### FBX Export Call & Rigging Invariant
- **CRITICAL:** `bake_space_transform=False` is MANDATORY for rigged/skinned meshes. Setting it to `True` bakes axis conversions into bone matrices, destroying bind-poses and causing zero-extent bounds (`localBounds 0,0,0`) in Unity.
```python
bpy.ops.export_scene.fbx(
    filepath=str(FBX_PATH), use_selection=True, object_types={"MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE", use_mesh_modifiers=True,
    mesh_smooth_type="FACE", path_mode="AUTO", axis_forward="-Z", axis_up="Y",
    bake_space_transform=False, add_leaf_bones=False, use_custom_props=False
)
```

---

## 3. Real Functional Engineering & Form Hierarchy Standard

Rogue Command visuals must depart completely from primitive geometric boxes and decorative, random "greeble noise." Every vehicle, combat unit, and industrial factory structure must be grounded in real-world mechanical and structural engineering principles.

### 3.1 Three-Tier Form Hierarchy & The 70/30 Rest Rule (Vitaly Bulgarov / Neil Blevins)
- **Primary Macro Forms (70% Visual Mass):** Establishes the silhouette, tactical silhouette stance, and center of gravity.
  - *Vehicles & Mobile Units:* Low-profile, aggressive ground clearance, sloped ballistic hulls, and functional wheel/track wheelbase proportions.
  - *Factories & Strategic Facilities:* Broad structural bases, pyramidical or stepped mass distribution, anchored pylons, and defined vertical/horizontal logistics flow.
- **Secondary Meso Forms (20% Visual Mass):** Articulated mechanical assemblies that explain how the machine operates.
  - Sponsons, engine decks, armored turrets, suspension swing arms, exhaust stacks, hoppers, pressure vessels, and conveyor housings.
- **Tertiary Micro Forms (10% Visual Mass):** Scale-defining engineering details.
  - Fasteners, flanges, latches, lifting shackles, warning placards, sensor lenses, and hydraulic fittings.
- **The 70/30 Rest Rule:** 70% of visible surfaces must remain clean, continuous planar plates or smooth cast hulls where the viewer's eye can rest. Concentrate the remaining 30% of high-density mechanical complexity into purposeful functional clusters (engine bays, suspension linkages, process manifold manifolds, and turret rings). *Never scatter random geometric cuts across all surfaces.*

### 3.2 Combat Unit Functional Engineering Anatomy
Every combat unit must incorporate authentic military defense and mobility subsystems:
1. **Ballistic & Blast Deflection Geometry:**
   - Sloped armor glacis plates angled at $55^\circ$ to $70^\circ$ from vertical.
   - V-hull shaped chassis bottoms engineered for mine and IED blast deflection.
   - Compound chamfers (e.g. $45^\circ$ and $22.5^\circ$ double facets) along vulnerable hull shoulders.
2. **Layered Composite Armor & ERA:**
   - Visual stratification: structural inner chassis $\rightarrow$ composite ceramic matrix plates $\rightarrow$ modular stand-off spaced armor skirts or reactive armor (ERA) tile cassettes with welded brackets.
   - Distinct shutlines and panel gaps ($0.003\text{m} - 0.008\text{m}$) indicating field-replaceable modular armor blocks.
3. **Authentic Running Gear & Suspension:**
   - Tracked chassis must feature: toothed drive sprockets, idler wheels with visible mechanical/hydraulic tensioning screws, dual road wheels with solid rubber tires and heavy 6/8-bolt planetary hubs, return rollers, and track mud scrapers.
   - Articulated trailing swing arms with torsion bar housings or hydropneumatic struts featuring polished chrome cylinder rods.
   - Track loops with individual shoes, rubber road pads, connector pins, and center guide horns.
4. **Active Protection Systems (APS) & Sensor Suites:**
   - Millimeter-wave radar panels (Trophy/Iron Fist style) flush-mounted on turret corners with armored cable conduits.
   - Directional soft/hard-kill countermeasure launchers mounted on articulated gimbals.
   - $66\text{mm} / 76\text{mm}$ smoke grenade discharger banks with wired ignition manifolds.
   - Independent panoramic commander sights (CITV) featuring anti-reflective germanium optical elements (deep violet/amber tint) and integrated wiper blades.
5. **Ergonomics & Field Maintenance Provisions:**
   - Forged steel recovery shackles and D-rings welded to structural hull hardpoints for crane recovery.
   - Folding crew steps, grab handles, and high-traction textured anti-slip coatings on crew transit surfaces.
   - Rear bustle stowage racks for expeditionary gear, tool pioneer tools (axes, shovels, tow cables), and thermal exhaust louver diffusers.

### 3.3 Factory & Industrial Facility Engineering Standards
Strategic industrial buildings must feel like functional, high-throughput chemical/mechanical plants rather than hollow sci-fi buildings:
1. **Modular Structural Metric Grid:**
   - Strict metric spacing based on powers of two or standard metric intervals ($0.5\text{m}, 1\text{m}, 2\text{m}, 4\text{m}, 8\text{m}$).
   - Structural column bays at $6\text{m} \times 6\text{m}$ or $8\text{m} \times 8\text{m}$; standard story heights of $4.0\text{m} - 4.5\text{m}$.
   - Heavy structural steel framing: standard I-beams (IPE/HEB), C-channels (UPN), and angle irons connected via triangular gusset plates and heavy hexagonal structural bolt assemblies (A325/A490).
2. **Process Piping Standards (ASME B31.3 / ISO):**
   - Standard nominal pipe diameters: DN50 (2"), DN100 (4"), DN200 (8"), DN300 (12").
   - Long-radius ($1.5D$) and short-radius ($1.0D$) $90^\circ$ and $45^\circ$ flanged/welded elbows.
   - Concentric and eccentric reducers to prevent sediment pooling in liquid lines.
   - Weld-neck flanges with visible bolt circles and sealing gaskets.
   - Handwheel gate valves, pneumatic actuator butterfly valves, and spring-loaded pressure relief valves with vertical discharge horns.
   - Overhead pipe racks with U-bolt clamping, spring hangers for thermal/vibrational absorption, and U-shaped thermal expansion loops on long lines.
   - **ASME A13.1 Fluid Color Coding:**
     - *Yellow:* Flammable gases and liquids (fuel, natural gas).
     - *Green:* Potable, industrial, or cooling water.
     - *Blue:* Compressed air systems.
     - *Red:* Fire suppression manifolds.
     - *Orange:* Corrosive acids and caustic chemicals.
     - *Silver / Grey:* High-pressure superheated steam.
3. **Pressure Vessels & Storage Silos:**
   - Dished heads (toriespherical / ASME $2:1$ ellipsoidal code heads) instead of flat cylinder caps.
   - External circumferential stiffener rings, manways with swinging davit arms, armored liquid level sight glasses, and flared skirt bases anchored to reinforced concrete footings with double-nut anchor bolts.
4. **Heavy Logistics & Material Handling:**
   - Overhead gantry cranes: runway I-beams with rail clips, polyurethane end buffers, wire-rope hoist drums with double-sheave forged crane hooks, and C-rail festoon cable loops.
   - Industrial scaffolding & walkways: $48.3\text{mm}$ standard pipe tubing with swiveling couplers, threaded leveling screw jacks, $150\text{mm}$ safety kick plates / toe boards, two-tier safety handrails ($0.5\text{m}$ and $1.1\text{m}$), and caged safety ladders.
   - Industrial ventilation: Conical cyclone dust separators, pulse-jet baghouse collector banks, and spiral-wound ductwork with crimped standing seams.

### 3.4 The 4 Functional Engineering Questions Checklist
Before approving any modeled component, verify it answers all four engineering criteria:
- **Power & Fluid Conveyance:** Does every moving or energized component show hydraulic hoses with realistic catenary slack, armored conduits, or rigid hard-lines with bulkhead fittings?
- **Heat Dissipation:** Are engines, reactors, and energy capacitors paired with cooling louvers, core radiators, or thermal exhaust diffusers?
- **Human Maintenance Access:** Can human technicians service the unit via visible access latches, hoist points, drain plugs, and inspection hatches?
- **Structural Load Distribution:** Do cantilevered arms, masts, and weapon mounts feature gussets, corner reinforcement fillets, and heavy pin hinges designed to transfer mechanical loads?

### 3.5 Canonical Two-Level Visual Variant Boundary
- **Level 1 — Primary Chassis:** The Blender-authored mesh, silhouette, functional assemblies, rig, pivots, canonical sockets, and baked PBR maps remain the geometry authority for the unit.
- **Level 2 — Tactical Variants:** Current `visualVariants` apply a preset label, color tint, and optional whole-root scale to that same authored chassis. They do not replace the functional form hierarchy or switch to another mesh at runtime.
- Treat `visualAssetName` and `visualRootPath` on a variant as declarations only unless the runtime has explicit, verified mesh/root switching support. Diagnostics report these declarations separately from the current tint-and-scale effect.
- A requested silhouette or mechanical-assembly change needs a separately authored visual asset and explicit runtime integration that preserves sockets, rig, collision, LOD, and PBR contracts. Do not imply a new engine, suspension, armor module, or weapon assembly from a palette tint.

---

## 4. Native Bevel Modifier + Weighted Normal Modifier Pipeline

This is the project's primary modeling and shading standard for achieving photorealistic, SubD-quality specular highlights on hard-surface assets without high-poly polygon bloat.

### 4.1 The Shading Challenge & The Solution
Traditional low-poly hard-surface models suffer from an inherent flaw:
- Flat shading produces harsh, razor-sharp edges that look like paper-thin computer graphics.
- Naive smooth shading averages face normals across right angles, causing broad armor plates to bend and distort light (unsightly "shading gradient" or "pinch").
- SubD modeling requires dense support loops ($50\text{k} - 200\text{k}$ polygons per asset), destroying real-time RTS performance.

**The Solution:** The non-destructive pairing of a scale-aware **Bevel Modifier** followed by a **Weighted Normal Modifier**. Broad planar faces receive overwhelming mathematical normal priority over tiny edge chamfers. Consequently, large armor plates reflect light as perfectly flat surfaces, while the beveled edges catch crisp, physical specular highlights.

### 4.2 Modifier Stack Configuration in Blender 4.5.14
Apply the following stack in order on all hard-surface components:

1. **Bevel Modifier (Machined Chamfers):**
   - **Width:** Scale-aware based on component role:
     - *Small hardware & latches:* $0.003\text{m} - 0.006\text{m}$
     - *Armor plate edges & chassis rails:* $0.010\text{m} - 0.025\text{m}$
     - *Cast housings, road wheels, and silo shoulders:* $0.030\text{m} - 0.060\text{m}$
   - **Segments:**
     - `1` for crisp machined chamfers (LOD0 & LOD1 standard).
     - `2` or `3` for rounded cast hulls, cylinders, and piping.
   - **Limit Method:** `ANGLE` with Angle Limit $= 35^\circ$ ($0.61\text{ rad}$) or `WEIGHT` for hand-tuned edge sharpness.
   - **Profile:** `0.50` (creates a planar chamfer at 1 segment, true circular arc at $\ge 2$ segments).
   - **Clamp Overlap:** `True` (prevents self-intersection on acute corners).
   - **Harden Normals:** `False` (let the dedicated `WEIGHTED_NORMAL` modifier govern).

2. **Angle-Based Auto-Smooth:**
   - Execute in Blender 4.5+:
     ```python
     bpy.ops.object.shade_smooth_by_angle(angle=math.radians(38.0))
     ```
   - Smooths cylindrical barrels and curved pipes while preserving sharp boundaries between planar faces.

3. **Weighted Normal Modifier (`RCL_WeightedNormal`):**
   - **Mode:** `FACE_AREA` (weights vertex normals proportionally to the surface area of connected faces).
   - **Weight:** `50` to `60` (ensures massive hull plates dominate the vertex normal vector).
   - **Keep Sharp:** `True` (preserves tagged sharp edges and angle splits).
   - **Face Influence:** `False`.

### 4.3 Automated Python Integration (`RogueModelKit.py`)
In project generator scripts, invoke the verified native helper:
```python
import RogueModelKit as rmk

# Apply directly to an authored object
rmk.apply_weighted_normals(
    obj,
    weight=50,
    mode="FACE_AREA",
    keep_sharp=True,
    apply_modifier=True  # Bakes custom split normals into mesh loops
)

# Or automatically during material batching:
batched = rmk.batch_and_join_by_material(
    model_objects,
    prefix="VanguardTank",
    smooth_by_angle=True,
    smooth_angle_deg=38.0,
    weighted_normals=True,
    normal_weight=50
)
```

### 4.4 Unity URP FBX Export & Import Contract
- **Blender FBX Export Call:**
  - `mesh_smooth_type="FACE"` (MANDATORY: exports custom split normals directly to FBX mesh attributes).
  - `use_mesh_modifiers=True`
  - `bake_space_transform=False`
- **Unity Model Importer Configuration:**
  - **Normals:** `Import` (do NOT set to `Calculate`, which discards Blender's custom weighted normals).
  - **Normal Generation Method:** `MikkTSpace`
  - **Tangents:** `Calculate MikkTSpace` (ensures baked normal maps and micro-detail textures align perfectly without tangent distortion).

---

## 5. Project-Adapted FOSS & Advanced Toolchain

To ensure zero vendor lock-in, zero cost, and seamless integration with Rogue Command's engine pipeline:

### 5.1 Procedural PBR Texturing: Material Maker 1.7 (Mandatory FOSS)
- Node-based procedural PBR authoring built on Godot engine. Replaces proprietary Substance Designer.
- Automated pipeline runner via `tools/Run-MaterialMakerBake.ps1 -Workflow urp`.
- Outputs strictly 4 PBR texture maps:
  - `<Name>_Albedo.png` (sRGB, base color + macro dirt)
  - `<Name>_MetallicGloss.png` (Linear, RGB = Metallic, Alpha = Smoothness $1 - \text{roughness}$)
  - `<Name>_Normal.png` (Normal map, tangent-space MikkTSpace)
  - `<Name>_Emission.png` (sRGB, HDR optic and energy glow)
- *Strict Invariant:* Material Maker outputs PNG textures only; never import its meshes or `.mat` files into Unity.

### 5.2 Trim Sheets & Rapid UV Layout: DreamUV (FOSS Blender Addon)
- Integrates into Blender 3D Viewport (`Shift + D`).
- Enables 1-click snapping, cycling, and scaling of low-poly geometry UV islands directly to industrial trim sheet strips (hazard stripes, panel seams, pipe collars, catwalk grates).
- Eliminates manual UV unwrapping for modular factory kits and structural beams.

### 5.3 Parametric Structural Modeling: BagaPie Free & JMesh Tools (FOSS)
- **BagaPie Free:** Geometry Nodes-based parametric generation of catenary cables, industrial piping networks with elbow joints, railings, and structural stairs.
- **JMesh Tools:** Rapid non-destructive booleans, surface extraction, and symmetrical hard-surface slicing.

### 5.4 Unity URP Rendering & Optimization
- **URP Decal Projectors (D-Buffer Mode):**
  - Project stencils, hazard warnings, weld beads, and grease streaks without adding geometry.
  - D-Buffer mode writes directly to G-Buffer / Depth before lighting, ensuring decals receive physically accurate shadows and specular highlights.
- **Detail Normal Maps:**
  - In `URP Lit` materials, enable *Detail Inputs* with high-tiling ($20\times20$) micro-normals (rolled steel mill-scale or cast iron stipple). Guarantees photorealistic micro-tactility even when zoomed to centimeters.
- **Automated LOD Chains with UnityMeshSimplifier:**
  - Open-source mesh reduction integrated into Unity asset pipelines:
    - *LOD0 (100%):* Full fidelity ($1,500 - 5,000$ tris), complete chamfers and mechanical linkages.
    - *LOD1 (50%):* Collapses minor hardware while preserving silhouette and weighted normal boundaries.
    - *LOD2 (20%):* Coarse mass for distant tactical overview.

---

## 6. 5-Tone PBR Structural Palette Standard

| Material Role | Proportion | Surface Description | Metallic | Smoothness | Emission |
|---|---|---|---|---|---|
| **Primary Armor** | 40–45% | Main hull plates, sponsons, chassis rails | 0.35–0.45 | 0.60–0.65 | None |
| **Light Armor** | 20–25% | Turret cheeks, upper deck, access hatches | 0.30–0.35 | 0.58–0.62 | None |
| **Mechanical Gunmetal** | 15–20% | Cannon barrels, suspension linkages, axles | 0.74–0.80 | 0.68–0.75 | None |
| **Tread / Rubber** | 10–15% | Track shoes, road wheels, tire chevrons | 0.08–0.18 | 0.28–0.34 | None |
| **Warning Accent** | 3–5% | Identification bands, danger stripes | 0.16–0.20 | 0.60–0.65 | None |
| **Emissive Optics** | 1–3% | Sensor visors, headlamps, plasma conduits | 0.15–0.25 | 0.75–0.85 | **HDR ×1.5–2.5** |

- **Faction Colors:** `vanguard` (slate teal), `siege` (rust ochre), `syndicate` (deep cobalt), `arctic` (tundra navy).
- **Camouflage Patterns:** `digital`, `splinter`, `tigerstripe` (Object-coordinate Voronoi/wave); `desert`, `urban`, `arctic`, `woodland` (noise-based).

---

## 7. 4-Channel Vertex Shading Contract (`VehiclePBR_VertexWear`)

Stored in `Mesh.colors32` (RGBA) for consumption by `RogueCommand/VehiclePBR_VertexWear` shader:

| Channel | Effect | Value Range | Behavior & Shader Decoding |
|---|---|---|---|
| **R (Red)** | Cavity AO / Dirt | 0.25 – 1.0 | $1.0$ on open/zenith surfaces; down to $0.25 - 0.40$ in wheel wells & undersides |
| **G (Green)** | Curvature / Edge Wear | 0.0 – 1.0 | Strictly $0.0$ on flat/concave faces; $0.7 - 1.0$ on sharp convex edges ($\ge 35^\circ$). Triggers two-tier chip (oxidized primer `#2c2724` at $0.12-0.42$, bright steel `#c7ccd6` at $>0.42$) |
| **B (Blue)** | Thermal Soot | 0.0 – 1.0 | $0.0$ baseline; scales to $1.0$ at muzzles/exhausts. Suppresses specular smoothness (`× (1 - soot*0.65)`) and metallic (`- soot*0.40`) |
| **A (Alpha)** | Team Tint Mask | 0.0 or 1.0 | $1.0$ on primary/light armor plates for `_TeamColor`; $0.0$ on mechanical gunmetal and treads |

---

## 8. Generator Architecture (`generate_unit_model.py` & `RogueModelKit`)

### CLI Scope & Archetypes
- **42 Mobile Archetypes:** `tank`, `walker`, `bike`, `artillery`, `arc_tank`, `hover`, `spider`, `vtol`, `halftrack`, `super_heavy`, `buggy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `quad_mech`, `combat_harvester`, `colossus_titan`, `hover_cruiser`, `heavy_dropship`, `shield_truck`, `demolition_breacher`, `artillery_strider`, `thermobaric_tank`, `ew_jammer`, `mobile_sam_launcher`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `hexapod_assault`, `assault_hovercraft`, `bridge_layer`, `command_hq_rig`, `flying_wing_bomber`, `mortar_carrier`, `siege_drill_mole`, `heavy_crane_mech`, `nanite_support_rover`, `gravity_siege_breaker`, `tactical_relay_carrier`, `amphibious_assault_tank`, `plasma_mortar_strider`, `stealth_recon_skimmer`.
- **10 Strategic Structures:** `factory`, `defense_turret`, `radar_station`, `energy_silo`, `orbital_silo`, `shield_dome`, `emp_obelisk`, `bunker_pillbox`, `anti_air_battery`, `comms_relay_tower`.
- **15 Weapon Mounts:** `cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon`, `railgun`, `quad_flak`, `plasma_mortar`, `laser_beam`, `vls_missiles`, `flamethrower`, `emp_emitter`, `nanite_projector`, `gravity_cannon`.
- **Wear Levels:** `pristine` (factory clean), `field_tested` (moderate wear), `battle_hardened` (heavy combat gouges and soot).
- **Key CLI Flags:**
  - Geometry: `--lods` (LOD0-2 chain), `--colliders`, `--gameplay-json`, `--track-sag`, `--armor-plating`, `--ik-rig`, `--weighted-normals` (default on; disable via `--no-weighted-normals`).
  - Greebles: `--suspension-arms`, `--mantlet-boot`, `--spaced-skirts`, `--hydraulic-hoses`, `--hull-greebles`, `--tactical-markings`, `--hydraulic-pistons`, `--ammo-chutes`, `--aps-suite`, `--stowage-kit`, `--slat-armor`, `--era-bricks`, `--camo-netting`, `--thermal-radiators`, `--nanite-dispenser`, `--grav-dampener`, `--comms-mast`, `--ram-prow`.
  - Attachments & Damage: `--attachments era,aps,sponson,fuel_drums`, `--export-detached-attachments`, `--damaged-state`, `--render-icons`.
  - Atlas Baking: `--bake-atlas`, `--bake-normals`, `--single-material` (`<Name>_Atlas.fbx`).

### Presets (56 Total)
Key presets include: `siege_breaker` (super_heavy dual_cannon), `recon_interceptor` (buggy gatling), `gunship_alpha` (vtol missiles), `colossus_titan` (titan dual_cannon), `sea_lance` (hover_cruiser railgun), `sky_sweeper` (flak_anti_air), `dreadnought_crawler` (siege_crawler railgun), `hive_carrier` (drone_carrier), `titan_quad` (quad_mech), `resource_behemoth` (combat_harvester), `aegis_bastion` (shield_truck), `longbow_strider` (artillery_strider), `inferno_purger` (thermobaric_tank), `spectre_jammer` (ew_jammer), `skyguard_sam` (mobile_sam), `salvage_hauler` (recovery_crawler), `arachnid_hexapod` (hexapod_assault), `scissor_bridgelayer` (bridge_layer), `overlord_command_hq` (command_hq_rig), `subterranean_mole` (siege_drill_mole), `atlas_crane_mech` (heavy_crane_mech), and faction remixes (`crimson_praetorian`, `cobalt_widowmaker`, `golden_aegis_command`).

---

## 9. Procedural Animations & Procedural Action Clips

- **Action Clips Contract:** `Action_Idle`, `Action_Walk`/`Action_Drive`, `Action_FireRecoil`, and `Action_Death`.
  - `Action_Idle`: Peaks at f15 with f8/f22 easing; never keyframes `Bone_Root`.
  - `Action_Death`: 12% settle at f8, 50% collapse at f16, full collapse at f28, holding to completion.
- **Armature Hierarchy:** `Bone_Root` $\rightarrow$ `Bone_Hull` $\rightarrow$ `Bone_TurretYaw` $\rightarrow$ `Bone_TurretPitch` $\rightarrow$ `Bone_BarrelRecoil`.
- **Rigid Weighting:** 1.0 per armor plate on unbatched source meshes to prevent rubbery mesh deformation.

---

## 10. Socket-to-Gameplay Runtime Binding Map (`RuntimeVisualDirector`)

Visuals bind dynamically to native units via canonical unprefixed sockets:

| Authored Socket | Runtime Director Behavior |
|---|---|
| `Socket_TurretPivot` | Traverses azimuth at 120°/s toward hostile `CurrentAttackTarget` (`IsEnemy==true`). Tilts 5° at $\le 25\%$ HP |
| `Socket_RadarMast` | Rotates continuously at 30 RPM around local up axis |
| `Socket_DamageSmoke` | Spawns volumetric smoke when `HealthRatio <= 0.50f`; switches to `<Name>_Damaged` mesh and jettisons fuel drums |
| `Socket_DamageFire` | Spawns flame particles and light when `HealthRatio <= 0.25f`; burns out APS and tilts turret pivot |
| `Socket_Deploy_Unit` | Slides bay door open 1.4 m when factory queue `InProductionCount > 0` |
| `Socket_RecoilPrimary` | Linear slide backward (0.22 m) upon shot fired (`_timeOfLastShot`); spring-damper recovery |
| `Socket_MuzzlePrimary` | Synchronous muzzle flash pulse for 0.07 s; contains nested `FirePoint_0` for native child discovery |
| `Socket_Hardpoint_<Type>`| Mounts detached accessories; detonates ERA tiles at $\le 75\%$ HP |
| `Socket_FootContact_*` | Ground impact anchors ($Z = 0.0$) for articulated walkers/mechs |
| `Socket_EngineAudio` | Placed over engine deck for `EntityFlockingMovement._movementSoundAudioSource` |
| `Socket_WeaponAudio` | Placed at weapon muzzle for `ShootProjectile.shotSoundAudioSource` |

---

## 11. Template Dimensions & Native Hierarchy Alignment

| Native Template | Sample Unit | `visualRootPath` | Collider Dimensions (W × H × L) |
|---|---|---|---|
| `PCXBaseTank` | Sandlance Scout | `"Vehicle"` | ~2.4m × 1.6m × 2.8m |
| `BombBike` | Skirmish Strike Bike | `"GameObject/BombBike"` | ~1.2m × 1.4m × 2.2m |
| `ArtilleryTank` | Colossus Siege Platform | `"Vehicle"` | ~2.8m × 1.9m × 3.6m |
| `GatlingWalker` | Ironclad Walker | `"Vehicle"` | ~2.2m × 2.7m × 2.0m |
| `LightningTank` | Surge Arc Tank | `"Vehicle"` | ~2.3m × 1.6m × 2.8m |

---

## 12. Material Maker Texture Runner & Card Sprite Rules

### Texture Generation
- Blender 4.5.14 bakes geometry/UVs/wear; Material Maker 1.7 generates final textures:
  ```powershell
  powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow urp
  powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow blender
  ```
- Material Maker outputs PNGs only. Never import its mesh or `.mat` exports to Unity.

### Card Sprite Contract
- **Resolution & Format:** $128 \times 128$ PNG; `FilterMode.Point`, `mipmapEnabled = false`, `spriteImportMode = Single`.
- **Generation:** Use built-in `imagegen` skill referencing the final Blender preview silhouette and palette. Never generate procedurally via Pillow/SVG.
- **Background:** Deep dark tactical gradient (`#0a1216` to `#162a34`) with a 2–3px emissive optic glint.

---

## 13. Rigged Armature & Facility Realism Lessons Learned

### 13.1 Rigid Weight Rebinding on Authored Armatures
When authoring or updating articulated walker/quad-mech sources with pre-existing armatures (e.g. `BastionSiegeMech` or `AtlasCraneMech`), adding detail geometry or wave passes (lifting lugs, coamings, or ERA brackets) introduces new mesh objects lacking vertex group bindings.
- **Invariant:** Always call `rmk.assign_rigid_mesh_weights(meshes, prefix=UNIT_NAME, armature_obj=armature, leg_type="quad"|"biped")` explicitly before running `validate_rigid_weights(meshes, armature)`.
- If new detail passes (`apply_wave2_details`, `apply_wave34_details`) are appended, re-run `assign_rigid_mesh_weights` so every appended object receives its rigid 1.0 weight on the appropriate bone (`Bone_Hull`, `Bone_Pelvis`, `Bone_TurretYaw`, etc.) before batching and export.

### 13.2 Industrial Facility Realism Assemblies
When generating or refining production facilities (e.g. `ArachnidHexapodFactory`, `OverlordCommandHQFactory`), leverage the high-realism functional engineering assemblies in `RogueModelKit`:
- `add_process_piping_manifold`: ASME B31.3 flanged piping with long-radius elbows and gate valves.
- `add_pressure_vessel`: Chemical storage pressure vessel with ASME 2:1 ellipsoidal dished heads and stiffener rings.
- `add_gantry_crane_trolley`: Heavy overhead gantry crane with hoist drum, cable, forged hook, and end-stops.
- `add_caged_ladder`: OSHA-compliant industrial caged safety ladder with safety hoops and platform landing.

---

## 14. References & Detailed Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — Proportions, silhouette tests, and faction palettes.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — In-depth `bpy` recipes for tracks, bevels, and hulls.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — URP shaders, keyword toggles, and prefab builders.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy trees for native templates.
- [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) & [Validation](../rogue-command-pbr-texture-validation/SKILL.md).
