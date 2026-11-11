# Rogue Command 3D Model Authoring — Technical Architecture & Enhancements

**Updated:** 2026-11-10
**Repository:** Mod Rogue Command  
**Applies to:** `.agents/skills/rogue-command-3d-model-authoring`, `tools/RogueModelKit.py`, `tools/generate_unit_model.py`  
**Tooling Baseline:** Blender 4.5.14 LTS (pinned in `.tools/Blender-4.5.14/`), Unity 2021.3.45f2 (pinned in `unity/RogueCommandAssetAuthoring/`)

---

## 1. Executive Summary & Core Invariants

The Rogue Command 3D model authoring pipeline provides a deterministic procedural geometry generator for vehicles, base structures, modular attachments, and destruction states. Its art direction is grounded, stylized military sci-fi: preserve the game's RTS readability while giving visible forms plausible mass, construction, and controlled curvature. The tools operate locally via Python (`bpy`) under Blender 4.5.14 LTS and bridge into Unity 2021.3.45f2 through automated prefab builders and runtime directors.

### Architectural Invariants
- **Zero Content Pollution**: Authoring geometry and CLI generators never add unrequested balance rows, prefab registrations, or modifications to `content.json`. All gameplay registrations require explicit authoring tasks.
- **Unity Project Integrity**: The Unity project at `unity/RogueCommandAssetAuthoring/` is strictly preserved (`ProjectSettings`, `Packages/manifest.json`, version control metadata). Target is **Unity 2021.3.45f2** (Mono x64).
- **Universal Render Pipeline (URP) Invariant**: Rogue Command executes strictly on URP (`UniversalRenderPipelineAsset`). Shipped native vehicles use untextured geometry rendered via `Shader Graphs/EntityColorShader`. Modded AssetBundles must embed self-contained compiled shaders (`RogueCommand/VehiclePBR_VertexWear` or `Standard`); never assume built-in shaders are provided by base game assets.
- **Twin Script Parity**: All authoring tools (`RogueModelKit.py`, `generate_unit_model.py`, `validate_vfx_assets.py`, `validate_mech_rigging.py`) must maintain 100% byte-for-byte binary parity between `tools/` and `.agents/skills/.../scripts/`, verified via `fc.exe /b`.
- **Coordinate Conventions**: Forward = Blender `+Y` $\to$ Unity `+Z`; Up = Blender `+Z` $\to$ Unity `+Y`; Ground Plane = $Z \ge 0.0$ ($0.0\text{m} \le Z_{\min} \le 0.08\text{m}$); Local Origin = `(0.0, 0.0, 0.0)` at ground center.

### FBX Export Invariant
Always export FBX from Blender using these exact parameter settings to ensure Unity import transforms remain identity without 90-degree axis flips:
```python
bpy.ops.export_scene.fbx(
    filepath=str(fbx_path),
    use_selection=True,
    object_types={"MESH", "ARMATURE", "EMPTY"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    path_mode="AUTO",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=False,
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False,
)
```

---

## 2. Art Direction & The 3-Tier RTS Visual Hierarchy

Target grounded, stylized military sci-fi realism: original silhouettes, believable mass, connected mechanisms, and surfaces that communicate their construction while remaining readable from the elevated RTS camera. The 30–120 pixel review range is an internal heuristic; check the actual game camera and unit size before accepting detail.

- **Tier 1 — Macro silhouette and stance:** role, mobility, proportion, weapon placement, ground clearance.
- **Tier 2 — Meso construction:** hull transitions, joints, support paths, cooling, access.
- **Tier 3 — Micro cues:** selective seams, optics, labels, wear, and service detail.

Use this hierarchy as a composition aid, not as a fixed percentage target. Generate a functional blockout first, then refine the visible primary shells. Replace primitive stacks where they define a whole body; retain boxes, cylinders, and rods when they are appropriate plates, axles, barrels, joints, tanks, or supports. Use broad facets for fabricated armor, controlled curvature for cast or formed parts, and purposeful asymmetry where it supports the role.

Allocate detail by its screen-space job: geometry defines the outline, cross-section changes, mechanical gaps, and visible articulation; the current PBR maps carry material identity, markings, and microrelief only when it survives at gameplay scale. Any high-to-low normal-bake technique must remain within the mandatory Material Maker workflow and existing four-map contract. After boolean or panel-cut operations, inspect highlight continuity and repair pinching or stepped curvature before UV and atlas work.

### The 5 Core Visual Rules

1. **Readability gate**: Compare a black silhouette from the elevated gameplay view and front/side/top. Test at actual on-screen sizes and distinguish the unit from neighboring classes before adding micro-detail. Preview its intended color grouping against light, dark, and similar-hue terrain values; this is an authoring preview, not proof of runtime team tinting.
2. **Scale-aware edge treatment**: Choose bevel width and segments for the part and its visible radius. Use crisp chamfers for plate breaks and rounded transitions only where a formed or cast part calls for them; do not bevel every edge uniformly.
3. **Curvature and shading**: After booleans or dense cuts, inspect the shell under a moving grazing highlight from multiple angles and repair pinching or lumpy transitions. Flat-shade deliberate armor facets and smooth curved surfaces by intent. Authored and weighted normals stabilize a modeled form; they do not repair a weak silhouette or justify smoothing every armor boundary.
4. **Functional detail**: Test a rough movement or articulation envelope while the model is still a blockout. Connect visible equipment to its support and clearance. Recess optics where the housing allows it, and keep emissive regions legible without relying on unverified renderer or bloom assumptions.
5. **Material hierarchy**: Give intact paint, bare metal, rubber, glazing, and powered lights distinct surface responses. Use low metallic response for non-metal coatings and rubber, higher metallic response for exposed metal, and vary smoothness by finish. Keep emission on actual lamps, displays, or energy hardware instead of using bright outlines around every panel.

These project rules adapt public hard-surface and RTS art guidance; numeric view sizes and composition ratios are local heuristics, not published industry standards.

---
## 3. Palette and PBR Material Cues

There is no fixed five-tone count, coverage ratio, or universal metallic/smoothness value. Separate armor, machinery, rubber, optics, and warning accents through role-appropriate color and material response. Tune values for the unit, atlas, and gameplay lighting; consult the live generator and PBR preset registry for current names.

| Material role | Visual cue |
|---|---|
| Armor | Faction color and broad readable panel groups |
| Machinery | Dark alloy or exposed metal at joints, axles, actuators, and weapon housings |
| Rubber / flexible parts | Low-metal response on tires, pads, seals, and gaiters |
| Warning accents | Localized safety markings or unit identification |
| Optics / energy | Small, controlled emissive regions |

Camouflage and procedural variation should reinforce the armor layout and unit identity. Do not allow noise, high-contrast markings, or emissive regions to erase the macro silhouette. Treat painted armor as a non-metal coating over a metal substrate: the intact paint region should not receive the same metallic response as exposed steel or alloy. Keep the project's exact MetallicGloss.R and MetallicGloss.A channel behavior.

---
## 4. Master Roster: The 24 Tactical Archetypes & 13 Weapons

| Archetype | Native Template | Proportions (W × L × H) | Stance / Mobility | Default Weapon | Rig Architecture | Signature Visual Features |
|---|---|---|---|---|---|---|
| `tank` | `PCXBaseTank` | 2.2m × 2.6m × 1.6m | Dual continuous tracks | `cannon` | Standard Rig (6 bones) | Sloped glacis, octagonal turret, coaxial cannon, side skirts |
| `walker` | `GatlingWalker` | 2.2m × 2.0m × 2.7m | Bipedal digitigrade legs | `gatling` | Biped Armature (8 bones) | Articulated hydraulic legs, hip rings, twin Gatling rotary guns |
| `bike` | `BombBike` | 1.2m × 2.2m × 1.4m | Two-wheeled motorcycle | `gatling` | Standard Rig (6 bones) | Rugged wheels, tubular rollcage, outrigger charge canisters |
| `artillery` | `ArtilleryTank` | 2.8m × 3.8m × 1.9m | Broad continuous tracks | `howitzer` | Standard Rig (6 bones) | Elevated heavy siege howitzer, recoil recuperators, rear spade |
| `arc_tank` | `LightningTank` | 2.3m × 2.8m × 1.6m | Continuous tracks | `arc` | Standard Rig (6 bones) | Insulated generator housing, dual capacitors, toroidal emitter |
| `hover` | `PCXBaseTank` | 2.5m × 3.0m × 1.6m | Anti-grav repulsor cushion | `cannon` | Standard Rig (6 bones) | Floating ~0.4m, ventral thruster bells, lateral strakes |
| `spider` | `GatlingWalker` | 4.6m × 4.8m × 1.8m | 4-point hydraulic legs | `missile` | Quad Armature (10 bones) | Wide stance, heavy knee knuckles, center turret housing |
| `vtol` | `PCXBaseTank` | 3.8m × 4.7m × 1.5m | Twin ducted tilt-rotors | `missile` | Standard Rig (6 bones) | Aerodynamic lifting body, swept wings, swiveling ducted fan pods |
| `halftrack` | `PCXBaseTank` | 2.2m × 3.8m × 2.2m | Front wheels + rear tracks | `gatling` | Standard Rig (6 bones) | Armored troop compartment, clamshell doors, roof vision cupola |
| `super_heavy` | `PCXBaseTank` | 3.8m × 4.8m × 2.4m | 4 independent track pods | `dual_cannon` | Standard Rig (6 bones) | Quad track assemblies, massive slab citadel, twin heavy cannons |
| `buggy` | `BombBike` | 1.8m × 2.6m × 1.5m | 4 wide tactical wheels | `gatling` | Standard Rig (6 bones) | Exposed tubular chassis, mid-engine block, rollcage, top gunner |
| `flak_anti_air`| `PCXBaseTank` | 2.4m × 3.2m × 2.0m | Dual continuous tracks | `quad_flak` | Standard Rig (6 bones) | High-elevation 2x2 flak autocannon cluster, radar dish |
| `siege_crawler`| `ArtilleryTank` | 3.6m × 4.6m × 2.2m | 4 heavy crawler legs | `railgun` | Quad Armature (10 bones) | Outrigger legs, hydraulic footpads, longitudinal kinetic rail |
| `drone_carrier`| `PCXBaseTank` | 3.2m × 5.2m × 2.1m | Heavy continuous tracks | `missile` | Standard Rig (6 bones) | Angled flight deck, landing centerline stripes, command island |
| `quad_mech` | `GatlingWalker` | 4.2m × 4.2m × 2.5m | 4 articulated walker legs | `dual_cannon` | Quad Armature (10 bones) | Heavy central chassis, 4 femur-tibia legs, dual turret mantlet |
| `combat_harvester`| `PCXBaseTank`| 3.0m × 4.8m × 2.2m | Heavy reinforced tracks | `gatling` | Standard Rig (6 bones) | Heavy excavator cutting drum, reinforced cab, collection hopper |
| `colossus_titan`| `GatlingWalker` | 3.2m × 3.0m × 3.6m | Heavy bipedal war walker | `dual_cannon` | Biped Armature (8 bones) | Digitigrade legs (2.3m hip), shoulder pauldrons, dual cannons |
| `hover_cruiser`| `PCXBaseTank` | 3.2m × 4.6m × 1.8m | Hydrodynamic skimmer | `railgun` | Standard Rig (6 bones) | Sloped bow prow (4.6m), flank repulsors, conning tower, railgun |
| `heavy_dropship`| `PCXBaseTank` | 4.8m × 5.2m × 2.2m | High-wing VTOL transport | `missile` | Standard Rig (6 bones) | Cargo fuselage (3.8m), rear ramp, ducted tilt-rotors, missile pods |
| `shield_truck` | `PCXBaseTank` | 2.4m × 4.2m × 2.5m | 6x6 tactical wheels | `arc` | Standard Rig (6 bones) | Armored 6x6 chassis, dorsal shield projector pylon, toroidal rings, capacitor banks |
| `demolition_breacher`| `BombBike` | 2.3m × 3.2m × 2.1m | Compact high-speed tracks | `plasma_mortar`| Standard Rig (6 bones) | Exposed volatile plasma core drum, containment hoops, forward detonator spikes |
| `artillery_strider`| `GatlingWalker`| 3.2m × 4.7m × 4.2m | Tall digitigrade biped legs | `railgun` | Biped Armature (8 bones) | Elevated sniper walker, spinal long-barrel railgun, counter-recoil rear spade |
| `thermobaric_tank`| `PCXBaseTank` | 2.6m × 3.4m × 1.9m | Heavy continuous tracks | `flamethrower`| Standard Rig (6 bones) | Twin dorsal pressurized fuel tanks, armored transfer pipes, flame projector turret |
| `ew_jammer` | `PCXBaseTank` | 2.4m × 3.9m × 2.8m | 6x6 tactical wheels | `emp_emitter` | Standard Rig (6 bones) | Mobile EW command shelter, dual phased-array panels, EMP pulse mast |

### The 13 Procedural Weapon Assemblies
1. `cannon` (`make_coaxial_cannon_assembly`): Tank gun with breech collar, thermal sleeve, bore evacuator, and muzzle brake.
2. `dual_cannon` (`make_dual_cannon_assembly`): Synchronized twin-cannon mantlet with dual rifled barrels.
3. `howitzer` (`make_heavy_howitzer_assembly`): Large-bore siege mortar with recoil recuperators and elevation sector gear.
4. `gatling` (`make_rotary_gatling_cannon`): 6-barrel circular Gatling cluster with drive axle and ammo feed chute.
5. `missile` (`make_missile_launcher_pod`): Armored pod with elevating tube array, recessed warheads, and backblast vents.
6. `arc` (`make_arc_energy_emitter`): Glowing plasma cathode, induction toroidal rings, and convergent discharge prongs.
7. `railgun` (`make_railgun_assembly`): Dual parallel electromagnetic kinetic rails with capacitor bank and accelerator core.
8. `quad_flak` (`make_quad_flak_autocannon_assembly`): High-cadence 2×2 autocannon cluster with flash hiders and ammo boxes.
9. `plasma_mortar` (`make_plasma_mortar_assembly`): Heavy siege mortar with 3 toroidal accelerator rings and radiator coils.
10. `laser_beam` (`make_collimated_beam_laser_assembly`): High-intensity directed-energy emitter with focal ring, collimator shroud, cooling radiator fins, and power conduits.
11. `vls_missiles` (`make_vls_vertical_missile_array`): Armored vertical launch system missile cell silo array with recessed cells, beveled lip, and exhaust duct trenching.
12. `flamethrower` (`make_flamethrower_assembly`): Heavy flame projector with pressurized fuel manifold block, dual side feed tanks, perforated heat shroud, and ventral igniter pilot assembly.
13. `emp_emitter` (`make_emp_emitter_assembly`): Electromagnetic pulse array with octagonal dielectric pedestal, 3 stepped concentric pulse rings, central waveguide mast, 4 dipole fins, and glowing HDR pulse globe.

---

## 5. Procedural Mechanical Assemblies (`RogueModelKit.py`)

The toolkit provides tested Python (`bpy`) parametric generators across 5 functional categories:

### Chassis & Turret Generators
```python
# Sloped armored hull with forward glacis and chamfered fender shelves
hull = rmk.make_sloped_glacis_hull(
    name="Vehicle_Hull", length_front=1.45, length_rear=1.40,
    width_front=1.35, width_rear=1.42, height_lower=0.24, height_upper=0.82,
    material=armor_primary, bevel=0.045
)

# Octagonal faceted turret shell with sloped gun mantlet cradle and rear bustle
turret_parts = rmk.make_faceted_turret_housing(
    prefix="Vehicle_Turret", location=(0.0, 0.10, 1.15),
    size=(1.45, 1.65, 0.42), armor_mat=armor_primary, roof_mat=armor_light, bevel=0.035
)

# Procedural BMesh faceted armor panels with individual face insets and chamfers
rmk.apply_procedural_armor_plating(hull, thickness=0.035, inset_depth=0.04, bevel_width=0.015)
```

### Mobility & Locomotion Assemblies
- `make_continuous_track_assembly`: Dual track units with drive sprockets, return idlers, road wheels, track cleats, and parametric catenary sag (`track_sag`).
- `make_quad_track_pod_assembly`: Self-contained continuous track pods with catenary sag for super-heavy 4-track crawlers.
- `make_all_terrain_wheel_assembly`: Deep-lug tactical tire, rim dish, planetary gear hub, and coilover shock absorber.
- `make_bipedal_digitigrade_leg`: Articulated 3-segment biped walker leg (hip actuator, hydraulic thigh, knee knuckle, shin strut, 3-toed ground pad).
- `make_quad_crawler_leg`: Outrigger spider leg with horizontal hip traverse, femur piston, tibia ram, and swivel footpad.
- `make_hover_skimmer_assembly`: Ventral repulsor cushion bells, lateral air-intake strakes, and vectoring exhaust nozzles.
- `make_ducted_tiltrotor_assembly`: Swiveling ducted fan pod with aerodynamic shroud cowl, multi-blade rotor, and spinner cone.

### Rigging & Kinematics Assemblies
- `generate_procedural_armature`: Skeletal armatures for bipeds, quadrupeds, and tracked vehicles with optional IK control rig (`with_ik=True`).
- `setup_walker_ik_armature`: Automated 2-bone IK solver constraints on `Bone_Shin_*` targeting non-deforming `IK_Target_Foot_*` and `IK_Pole_Knee_*` control bones.

### Micro-Detail & Accent Assemblies
- `make_whip_antenna`: Armored mounting bracket, 3-tier helical spring coils, and angled mast with high-visibility tip.
- `make_crew_cupola_hatch`: Octagonal vision cupola with 5 armored periscope slits and split circular hatch doors.
- `make_rivets_and_weld_seams`: Six-sided bolt heads and weld seams along plate boundaries for scale-appropriate specular highlights.
- `make_articulated_track_shoes`: Volumetric track shoe plates with center guide horns along ground contact runs.
- `make_engine_deck_grille`: Cooling intake frame with angled louvers and exhaust pipe manifolds.
- `make_recessed_sensor_visor`: Armored visor brow hood with deep cavity and glowing HDR sensor prisms.
- `make_catenary_cable_assembly` (`--hydraulic-hoses`): Hyperbolic cosine (`cosh`) catenary tube sweep in BMesh with corrugated rib modulation, hex bulkhead couplers, and retaining collar clamps.
- `make_bmesh_hull_greebles` (`--hull-greebles`): Selective BMesh face inset (`bmesh.ops.inset_individual`) maintenance hatches with grab handles, slotted micro-louver vents, and 4 forged corner lifting lugs.
- `make_tactical_decal_plates` (`--tactical-markings`): `mathutils.bvhtree.BVHTree` normal-snapped floating chevron stripe bars and zenith identification plates (+0.012m normal offset).
- `apply_weighted_normals` (`--weighted-normals` / `--no-weighted-normals`): Face-area weighted custom split normals (`mode="FACE_AREA"`, `weight=50`, `keep_sharp=True`) reducing shading pinch across broad armor faces and intentional edge treatments.

### Fluent Vehicle Construction with `RogueVehicleBuilder`
```python
import RogueModelKit as rmk

builder = (
    rmk.RogueVehicleBuilder("ColossusFortress", faction="siege", camo="desert")
    .set_archetype("super_heavy")
    .set_wear_level("battle_hardened")
    .add_sloped_hull(length=3.8, width=2.8, height=1.05)
    .add_quad_track_pods(pod_length=1.65, pod_height=0.62)
    .add_engine_deck(slat_count=5, with_exhausts=True)
    .add_turret(size=(1.85, 2.1, 0.58), center_y=0.15)
    .add_dual_cannon(caliber=0.082, length=1.65, spacing=0.38, elevation_deg=11.0)
    .add_sensor_visor()
    .add_radar_dish()
    .add_side_sponsons()
    .add_smoke_launchers(tube_count=3)
    .add_spring_antenna()
    .add_hydraulic_hoses()
    .add_hull_greebles()
    .add_tactical_markings()
    .add_colliders(include_turret=True)
    .add_unity_locators()
)

diagnostics = builder.build_and_export(
    fbx_path=FBX_PATH, blend_path=BLEND_PATH, preview_path=PREVIEW_PATH,
    render_multiview=True, smooth_by_angle=True, smooth_angle_deg=38.0,
    weighted_normals=True, bake_vertex_dirt=True, include_locators=True,
    generate_lods=True, generate_colliders=True, wear_level="battle_hardened",
    gameplay_meta_path=GAMEPLAY_JSON_PATH, wreckage_fbx_path=WRECKAGE_FBX_PATH,
    visual_root_path="Vehicle", profile_budget=True, target_budget=4500,
)
```

---

## 6. Procedural Geometry & Topological BMesh Vertex Shading

### Mathematical Basis for Directional Alignment
Aligning cylinders or pods along arbitrary 3D vectors $\vec{d}$ without gimbal lock uses an orthonormal basis $(\vec{u}, \vec{v}, \vec{d})$:
$$\vec{u} = \frac{\vec{d} \times \vec{k}}{\|\vec{d} \times \vec{k}\|}, \quad \vec{v} = \frac{\vec{d} \times \vec{u}}{\|\vec{d} \times \vec{u}\|}$$
For Blender cylinders (local Z alignment), rotation is computed via:
```python
rot_euler = dir_vec.to_track_quat("Z", "Y").to_euler()
```

### Topological BMesh RGBA Analysis (`bake_advanced_vertex_shading`)
Rather than geometry-blind loops, BMesh evaluates edge dihedral angles (`edge.calc_face_angle() >= 35°`) and projects neighboring face normal convexity:
$$\Delta = (v_{other} - v_{edge}) \cdot \vec{n}_{f1}$$
- $\Delta < -1\times 10^{-4}$: Sharp convex outside corner $\to$ Wear $G \in [0.7, 1.0]$.
- $\Delta > 1\times 10^{-4}$: Concave crease or recess $\to$ Intact paint $G = 0.0$ and Cavity AO $R \in [0.25, 0.40]$.

### URP Vertex Shading Channel Contracts (`VehiclePBR_VertexWear.shader`)
- **Channel R (Cavity AO)**: `half ao = saturate(lerp(1.0h, input.vertexColor.r, _AoStrength))`. $1.0$ on open/zenith surfaces; $0.25 - 0.40$ on undersides and wheel recesses.
- **Channel G (Edge Wear)**: `half wear = saturate(input.vertexColor.g * _WearStrength)`. Strictly $0.0$ on flat faces and concave seams; $0.7 - 1.0$ on convex edges (dihedral angle $\ge 35^\circ$), converting albedo to bright steel and boosting metallic/smoothness.
- **Channel B (Thermal Soot)**: `half soot = saturate(input.vertexColor.b * _SootStrength)`. $0.0$ (clean metal) default. Scales to $1.0$ exclusively at weapon muzzles ($R = 0.60\text{m}$) and exhausts ($R = 0.45\text{m}$) with quadratic dropoff $(1 - d/R)^2$.
- **Channel A (Team Tint Mask)**: `surfaceData.albedo = lerp(surfaceData.albedo, surfaceData.albedo * _TeamColor.rgb, input.vertexColor.a)`. $1.0$ on primary and light armor plates for player tinting; $0.0$ on neutral mechanical parts.

---

## 7. Multi-System Socket Hierarchy & Component Anchors

All sockets use canonical unprefixed names oriented to Unity conventions (+Z forward, +Y up):

| Canonical Socket Name | Hierarchy Placement | Runtime System / Component Driven |
|---|---|---|
| `Socket_MuzzlePrimary` | Primary weapon muzzle tip | Projectile origin, muzzle VFX, recoil animation |
| `FirePoint_0` | **Child empty nested under Muzzle** | Satisfies native `child.GetChild(0).GetComponent<ParticleSystem>()` in `ShootProjectile.Init()` |
| `Socket_MuzzleSecondary` | Secondary weapon muzzle tip | Secondary weapon discharge (`FirePoint_1` child) |
| `Socket_WeaponAudio` | Primary muzzle tip | Spatial 3D audio for `ShootProjectile.shotSoundAudioSource` |
| `Socket_EngineAudio` | Engine deck / exhaust midpoint | Spatial 3D audio for `EntityFlockingMovement._movementSoundAudioSource` |
| `Socket_FootContact_L/R` | Ground plane $Z = 0.0$ (Bipeds) | Step dust & surface impact VFX/SFX |
| `Socket_FootContact_FL/FR/RL/RR` | Ground plane $Z = 0.0$ (Quads) | 4-point crawler contact VFX/SFX |
| `Socket_TurretPivot` | Hull-turret junction center | Turret azimuth traverse & damage jamming tilt |
| `Socket_RecoilPrimary` | Gun mantlet slide axis | Recoil animation & mechanical displacement |
| `Socket_DamageSmoke` | Exhausts (or turret ring fallback) | Smoke emitter at $\le 50\%$ HP (native `HealthRatio`) |
| `Socket_DamageFire` | Reactor core / fuel cell flank | Fire emitter & point light at $\le 25\%$ HP |
| `Socket_Antenna` | Whip antenna mount root | Inertial 2D spring-damper sway (`UpdateAntennaWobble`) |
| `Socket_RadarMast` | Command collar / roof | Continuous 30 RPM ($180^\circ/\text{s}$) radar sweep |
| `Socket_ThrusterVector_L/R`| VTOL nacelle swivel axes | Velocity-based forward thrust vectoring ($0^\circ - 28^\circ$) |
| `Socket_Deploy_Unit` | Factory ramp / threshold | Unit rollout gate; door opens during active production |
| `Socket_Hardpoint_*` | Glacis, cheeks, roof, flanks | Modular attachment mounts (`ERA`, `APS`, `Sponsons`, `FuelDrums`) |

---

## 8. Skeletal Mech Armatures, Rigid Weighting & In-Place Locomotion

### Bone Hierarchies
- **Bipedal Walkers (`walker`, `colossus_titan`, `artillery_strider`)**: `Bone_Root` $\to$ `Bone_Pelvis` $\to$ `Bone_Hull` $\to$ `Bone_Thigh_L/R` $\to$ `Bone_Shin_L/R` $\to$ `Bone_Foot_L/R`, with `Bone_TurretYaw` $\to$ `Bone_TurretPitch` $\to$ `Bone_BarrelRecoil`.
- **Quadrupeds (`spider`, `quad_mech`, `siege_crawler`)**: `Bone_Root` $\to$ `Bone_Hull` $\to$ `Bone_Pelvis` $\to$ `Bone_Thigh_FL/FR/RL/RR` $\to$ `Bone_Shin_*` $\to$ `Bone_Foot_*`, plus turret bones.
- **Tracked / Wheeled**: Standard 6-bone rig (`Bone_Root`, `Bone_Hull`, `Bone_Pelvis`, `Bone_TurretYaw`, `Bone_TurretPitch`, `Bone_BarrelRecoil`).

### Rigid Weighting & In-Place Locomotion Invariant
- **Rigid Weight Assignment**: `assign_rigid_mesh_weights` assigns discrete $1.0$ weight per plate on unbatched source meshes *before* `batch_and_join_by_material`. `bpy.ops.object.join()` preserves all vertex groups across joined meshes; adding the `Armature` modifier ensures Unity imports a single skinned mesh with rigid bindings, preventing rubbery mesh distortion.
- **In-Place Invariant ($\Delta\text{pos} = (0, 0, 0)$)**: In RTS pathfinding (`EntityFlockingMovement`), translation is governed by physics grid navigation. All frames of `Action_Walk` lock `Bone_Root` translation delta to strictly $(0, 0, 0)$, eliminating double-stepping and cyclic snap-back artifacts.
- **5 Baked NLA Actions**:
  1. `Action_Idle`: 30-frame mechanical engine rumble and breathing loop.
  2. `Action_Walk`: 40-frame mechanical walk cycle strictly enforcing $\Delta\text{pos} = (0, 0, 0)$ on `Bone_Root`.
  3. `Action_Drive`: 30-frame suspension sway and road vibration oscillation.
  4. `Action_FireRecoil`: 20-frame ballistic recoil slide with progressive spring-damper recovery.
  5. `Action_Death`: 35-frame knee buckle collapse and turret tilt.
  Controlled dynamically in Mecanim via `RCL_IsMoving`, `RCL_Fire`, and `RCL_Death`.

---

## 9. Tactical Structures & Modular Attachments

### Tactical Structures (`RogueStructureBuilder`)
Supports the mod rule that *every added unit requires a dedicated production building*:
1. `factory`: Concrete slab base, ribbed hall, recessed bay door, yellow gantry crane, dual exhaust stacks, and `Socket_Deploy_Unit`.
2. `defense_turret`: Stepped octagonal bunker pillbox, 360° collar, beveled cap, and weapon mount.
3. `radar_station`: Octagonal bunker, 4-leg lattice mast, and motorized parabolic dish.
4. `energy_silo`: Reactor vessel, 4 buttress legs, magnetic containment rings, and emissive conduits.
5. `orbital_silo`: Heavy missile launch silo with book-opening blast doors and umbilical gantry tower.
6. `shield_dome`: Reinforced octagonal generator pad, 4 arched field-emitter ribs, central plasma pylon, 4 perimeter capacitor banks, and translucent faceted energy dome.
7. `emp_obelisk`: Stepped octagonal pedestal, 4 diagonal grounding buttresses, tapered dielectric pylon, 4 stacked high-voltage Tesla toroid rings, and apex discharge globe.

### Modular Attachments & Auxiliary Assets
- **Hardpoints (`--attachments`)**: `era` (Explosive Reactive Armor tiles), `aps` (Active Protection System radar/countermeasures), `sponson` (side autocannon pods), `fuel_drums` (jettisonable fuel cells). Detached FBX export (`--export-detached-attachments`) excludes meshes from primary atlas/LODs and outputs `<Name>_Attachment_<Type>.fbx`.
- **Damaged Combat State (`--damaged-state`)**: Exports `<Name>_Damaged.fbx` with dented armor plates, jammed $5^\circ$ turret tilt, displaced wheels, and scorched materials.
- **Tactical Icons (`--render-icons`)**: Headless Cycles/Eevee render of transparent alpha PNGs in Zenith (90°) and Isometric RTS (45° at `--icon-elevation 45.0`) at 64×64 and 128×128 resolutions.
- **Subsystem Budget Breakdown**: Use the counts as diagnostics, not fixed percentage quotas. Keep silhouette, mobility, weapon function, and readable construction intact; recover faces from exact duplicates, hidden internals, redundant bevel segments, or detail that disappears at the intended screen size. The LOD0 limit is a cap, not a target to fill.

---

## 10. Unity Prefab Builder & URP Material Pipeline

### Asset Resolution Order (`VehiclePrefabBuilder.cs`)
1. **Model Selection**: Prefers animated primary FBX or `_LODs.fbx` over `_Atlas.fbx` to preserve `LODGroup` (LOD0: 50%, LOD1: 20%, LOD2: 5% cull). Detached accessories mount under `RCL_AttachmentGroup_<Type>`.
2. **Linear Texture Import (`_MetallicGloss.png`)**:
   - `textureType = TextureImporterType.Default`
   - `sRGBTexture = false` (linear non-color data)
   - `alphaSource = TextureImporterAlphaSource.FromInput`
   - `alphaIsTransparency = false`
3. **URP Material Keywords**: Custom URP shader enables `_METALLICSPECGLOSSMAP`, `_GlossMapScale = 1.0f`, `_Metallic = 1.0f`, `_Glossiness = 1.0f`. Samples Metallic from Red and Smoothness from Alpha.
4. **Collider Conversion**: Flat-shaded `_COLLIDER_Hull` and `_COLLIDER_Turret` meshes convert to Unity `BoxCollider` components with `Renderer.enabled = false`.

```powershell
& "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -batchmode -quit `
  -projectPath "unity\RogueCommandAssetAuthoring" `
  -executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine `
  -gameplayJson "artifacts\CombatTank.gameplay.json"
```

---

## 11. Runtime Automation & Reactive Visual Director (`RuntimeVisualDirector.cs`)

Attached via Harmony postfix on `EntityFactory.InstantiateEntity`:
- **Progressive Health Degradation**:
  - $\le 75\%$ HP: Modular `ERA` hardpoints detonate with muzzle flash VFX.
  - $\le 50\%$ HP: Activates `<Name>_Damaged` variant or applies scorched soot via `MaterialPropertyBlock`; `FuelDrums` jettison. `Socket_DamageSmoke` activates.
  - $\le 25\%$ HP: `Socket_DamageFire` activates; $+4.5^\circ$ jammed tilt on `Socket_TurretPivot`; `APS` burns out.
  - Health Recovery: Fully reversible when repaired above thresholds.
- **Secondary Animation Mechanics**:
  - Antenna Wobble: 2D damped harmonic oscillator driven by linear acceleration $\vec{a} = (\vec{v}_t - \vec{v}_{t-1}) / \Delta t$ on `Socket_Antenna`:
    $$\ddot{\theta} = -\omega^2 \theta - 2\zeta\omega \dot{\theta} - k_{\text{accel}} (\vec{a} \cdot \hat{u})$$
  - Radar Sweep: Exact 30 RPM rotation ($\omega = 30 \times 6.0^\circ/\text{s} = 180^\circ/\text{s}$) on `Socket_RadarMast`.
  - VTOL Vectoring: Pitches `Socket_ThrusterVector_L/R` forward up to $28^\circ$ based on forward velocity.
  - Hostility Aiming: Tracks target azimuth at 120°/s; strictly filters hostility via `EntityController.IsEnemy` (fails closed if non-enemy or inactive).

---

## 12. REA Reverse-Engineering CIL Tokens Reference Table

Verified against `Assembly-CSharp.dll` (SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`):

| Member Name | Declaring Type | Token | Type / Return | Verified Runtime Purpose |
|---|---|---|---|---|
| `get_HealthRatio` | `EntityController` | `0x060007e4` | `Single` (`r4`) | Threshold checks for damage smoke (50%) and fire (25%) |
| `get_DestroyHasBeenStarted` | `EntityController` | `0x0600077d` | `Boolean` | Validates destruction before releasing emitters |
| `get_InProductionCount` | `EntityController` | `0x060007df` | `Nullable<Int32>` | Factory bay door opening during active production |
| `get_CurrentAttackTarget` | `EntityController` | `0x060007fc` | `EntityController` | Primary combat target for turret tracking |
| `IsEnemy` | `EntityController` | `0x06000844` | `Boolean` | Hostility check before committing turret traverse |
| `get_EntityId` | `EntityController` | `0x060007c6` | `String` (PascalCase)| Controller identity assignment |
| `_attackTarget` | `EntityAttack` | `0x040004a2` | `EntityController` | Authoritative attack target field |
| `_timeOfLastShot` | `EntityAttack` | `0x040004ab` | `Single` | Ballistic clock triggering weapon recoil and flash |
| `onFireParticleSystem` | `EntityController` | `0x040004e1` | `ParticleSystem` | Native fire VFX discovery for fallback cloning |
| `entityId` | `EntityInAiDeck` | `0x0400005b` | `String` (camelCase) | AI deck roster references |
| `entityId` | `ProductionInfo` | `0x04000603` | `String` (camelCase) | Production building product mapping |

---

## 13. The 23 CLI Presets Table

`tools/generate_unit_model.py` exposes 23 pre-calibrated presets:

| Preset Name | Archetype | Default Weapon | Faction | Key Active Subsystems |
|---|---|---|---|---|
| `scout_bike` | `bike` | `gatling` | `vanguard` | Rollcage, tactical tire, antenna |
| `heavy_tank` | `tank` | `cannon` | `vanguard` | Side skirts, smoke launchers, cupola |
| `siege_artillery` | `artillery` | `howitzer` | `siege` | Rear spade, outriggers, radar |
| `plasma_walker` | `walker` | `arc` | `syndicate` | Digitigrade biped legs, hip rings |
| `storm_quad` | `spider` | `missile` | `siege` | 4 crawler legs, missile pod, radar |
| `strike_vtol` | `vtol` | `missile` | `vanguard` | Ducted tilt-rotors, swept wings |
| `recon_halftrack` | `halftrack`| `gatling` | `arctic` | Troop compartment, front wheels, rear tracks |
| `behemoth_crawler`| `super_heavy`| `dual_cannon` | `siege` | 4 track pods, massive slab citadel |
| `flak_carrier` | `flak_anti_air`| `quad_flak` | `arctic` | High-elevation 2x2 flak, radar |
| `resource_behemoth`| `combat_harvester`| `gatling` | `siege` | Dozer blade, sponsons, excavator drum |
| `colossus_titan` | `colossus_titan` | `dual_cannon` | `vanguard`| Heavy biped legs, IK rig, radar, antenna, smoke |
| `sea_lance` | `hover_cruiser` | `railgun` | `syndicate`| Flank repulsors, sponsons, radar |
| `sky_dreadnought`| `heavy_dropship`| `missile` | `siege` | Heavy VTOL wings, tilt-rotors, sponsons |
| `orbital_defense`| `orbital_silo` (structure) | `railgun` | `vanguard`| Launch pit, gantry tower, blast doors |
| `beam_destroyer` | `tank` | `laser_beam` | `syndicate`| Collimated beam laser, radiator fins, sensor visor, sloped glacis |
| `vls_missile_cruiser`| `hover_cruiser`| `vls_missiles` | `siege` | Vertical launch missile array, conning tower, flank repulsors |
| `aegis_bastion` | `shield_truck` | `arc` | `vanguard` | 6x6 wheels, dorsal shield pylon, radar, smoke, energy shield dome |
| `volatile_runner` | `demolition_breacher`| `plasma_mortar`| `siege` | Volatile plasma core drum, dozer blade, rollcage, detonator spikes |
| `longbow_strider` | `artillery_strider`| `railgun` | `syndicate`| Tall digitigrade biped legs, IK rig, spinal railgun, rear spade |
| `inferno_purger` | `thermobaric_tank`| `flamethrower` | `siege` | Twin dorsal fuel tanks, flame projector, spaced skirts, sponsons |
| `spectre_jammer` | `ew_jammer` | `emp_emitter` | `arctic` | 6x6 wheels, phased-array jammer panels, EMP mast, radar, antenna |
| `bastion_shield_dome`| `shield_dome` (structure)| `arc` | `vanguard` | Octagonal generator pad, 4 arched ribs, faceted energy dome |
| `tesla_obelisk` | `emp_obelisk` (structure)| `emp_emitter` | `syndicate`| Grounding buttresses, tapered dielectric pylon, 4 Tesla toroids |

---

## 14. Structural Budget Diagnostics & Report Schema

When executing with `--report-json <path>`, `generate_unit_model.py` writes companion diagnostics:
```json
{
  "name": "SkySweeper",
  "archetype": "flak_anti_air",
  "weapon": "quad_flak",
  "total_triangles": 5636,
  "total_vertices": 5596,
  "predicted_draw_calls": 10,
  "ground_contact_valid": true,
  "subsystem_breakdown": {
    "Chassis & Hull": 1770,
    "Turret & Cabin": 102,
    "Mobility & Wheels": 1776,
    "Weapons": 1270,
    "Optics & Greebles": 718
  },
  "lod_levels": { "LOD0": 10, "LOD1": 10, "LOD2": 10 },
  "colliders": [ "SkySweeper_COLLIDER_Hull", "SkySweeper_COLLIDER_Turret" ]
}
```

---

## 15. Procedural Camouflage & PBR Atlas Dual-Pass Baking

### Procedural Camouflage Algorithms (`get_faction_palette`)
Blender procedural materials generate resolution-independent camouflage using Object-coordinate texture coordinates:
- `digital`: Dual-tone pixelated camouflage using 2-step Voronoi texture (`distance = CHEBYSHEV`, scale 18.0) mapped through a ColorRamp.
- `splinter`: Angular fracture pattern using Voronoi texture (`distance = EUCLIDEAN`, scale 12.0) mixed with coordinate vector warping.
- `tigerstripe`: Brushstroke camouflage using Wave texture (`bands`, scale 8.0, distortion 12.0) modulating primary and secondary armor albedo.
- `desert` / `woodland` / `arctic` / `urban`: Multi-octave Perlin Noise texture (scale 6.0–14.0, detail 4.0, roughness 0.6) with faction color stops.

### Contrast Midpoint Transformation (`--camo-contrast`)
To prevent camouflage from washing out base faction identification, contrast transforms pattern colors relative to the faction color midpoint:
$$C_{\text{final}} = \text{clamp}\left(C_{\text{midpoint}} + (C_{\text{pattern}} - C_{\text{midpoint}}) \times \text{contrast}, 0.0, 1.0\right)$$
Accepted CLI ranges: `--camo-scale 0.1–10.0`, `--camo-contrast 0.0–4.0`, `--normal-intensity 0.0–4.0`.

### PBR Texture Atlas Dual-Pass Baking (`bake_pbr_texture_atlas`)
Switches Blender render engine to Cycles (CPU, 1 sample, $< 1\text{s}$) to bake complete PBR texture maps:
1. **Pass 1 (Albedo, Emission, Tangent Normal)**: Direct surface bake into `Assets/.../Textures/<Name>_Albedo.png`, `_Emission.png`, and `_Normal.png`.
2. **Pass 2 (Metallic & Smoothness via Emission Rerouting)**:
   - Reroutes Principled BSDF `Metallic` to an Emission shader and bakes `type="EMIT"`.
   - Bakes `Roughness` via `type="ROUGHNESS"` and computes Smoothness:
     $$\text{Smoothness} = 1.0 - \text{Roughness}$$
   - Packs channels into linear non-color texture `<Prefix>_MetallicGloss.png`:
     $$\text{Red} = \text{Metallic}, \quad \text{Green} = \text{Metallic}, \quad \text{Blue} = \text{Metallic}, \quad \text{Alpha} = \text{Smoothness}$$

---

## 16. Headless CLI Reference & Operational Commands

Execute via Blender 4.5.14 LTS background mode:
```powershell
.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- [OPTIONS]
```

### Key CLI Options & Workflows
- **Single Unit Generation**:
  ```powershell
  .tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- `
    --archetype colossus_titan --name ColossusWarMech --weapon dual_cannon --wear field_tested `
    --soot-intensity 1.2 --rig --animate --lods --colliders --gameplay-json
  ```
- **Structure Generation**:
  ```powershell
  .tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- `
    --structure --building-type factory --name VanguardMechFactory --colliders --gameplay-json
  ```
- **Batch Planning / Dry Run**:
  ```powershell
  .tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- `
    --batch-all-archetypes --dry-run
  ```
- **Structural Budget Diagnostics**:
  ```powershell
  .tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- `
    --archetype tank --name HeavyBattleTank --target-budget 4500 --report-json artifacts/diagnostics/tank.json
  ```
- **Advanced Catenary Sag, Armor Plating & IK Rigging**:
  ```powershell
  .tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- `
    --preset beam_destroyer --name HeavyBeamDestroyer --track-sag --armor-plating --lods --colliders
  ```
- **PBR Texture Atlas Baking & Single-Material Export**:
  ```powershell
- **Mechanical Suspension, Dust Boot & Spaced Skirts Authoring**:
  ```powershell
  .tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- `
    --preset siege_breaker --name SiegeDestroyer --suspension-arms --mantlet-boot --spaced-skirts `
    --wreckage --wreckage-severity catastrophic --lods --colliders
  ```
- **Content Pack Visual Overlay Binding (`content.json`)**:
  ```json
  {
    "id": "colossus-war-mech",
    "name": "Colossus War Mech",
    "nativeTemplateId": "GatlingWalker",
    "visualPrefab": "Prefabs/ColossusWarMechVisual",
    "nativeRootPath": "GameObject/GatlingWalker",
    "cardSprite": "Sprites/ColossusWarMechCard"
  }
  ```

---

## 17. Mechanical Articulation, Suspension Linkages & Archetype Wreckage (v2026.10)

### 1. Trailing-Arm Suspension Linkage (`make_swing_arm_suspension_linkage`)
Links each roadwheel axle to hull sponsons via diagonal swing arms, torsion bar pivot bosses, and upper coilover hydraulic dampers.
- **Topology**: Torsion boss cylinder on inboard hull wall offset upward/forward ($Z + 0.60 L$, $Y + 0.65 L$). Planar trailing arm connects boss to wheel hub. Upper shock damper connects arm midpoint to sponson overhang.
- **CLI Flag**: `--suspension-arms` (auto-enabled in presets `siege_breaker`, `sky_sweeper`, `dreadnought_crawler`, `beam_destroyer`).

### 2. Tiered Concentric Mantlet Dust Boot (`make_mantlet_dust_boot`)
Adds segmented rubber/canvas accordion rings between the mantlet faceplate and each supported weapon barrel while keeping the bore open.
- **Topology**: Hollow torus pleat rings with alternating major radii ($1.14\times$ outer, $0.90\times$ inner) and an open torus clamp. Dual cannons and quad flak emit one fitted boot per barrel.
- **CLI Flag**: `--mantlet-boot` (auto-enabled in presets `siege_breaker`, `sky_sweeper`; supports cannon, dual cannon, howitzer, railgun, quad flak, and plasma mortar).

### 3. Segmented Spaced Armor Skirts (`make_spaced_armor_side_skirts`)
Protects continuous track runs with segmented standoff composite armor plates.
- **Topology**: Continuous top longitudinal mounting rail, $N$ independent armor tiles separated by vertical panel gaps, angular standoff mounting brackets, and rear flexible rubber mudflaps.
- **CLI Flag**: `--spaced-skirts` (auto-enabled in presets `siege_breaker`, `beam_destroyer`).

### 4. Archetype-Aware Wreckage Generator (`make_wreckage_variant`)
Replaces generic turret detachment with archetype-specific combat destruction physics and damage severity control:
- **Severity Levels (`--wreckage-severity <light|medium|catastrophic>`)**:
  - `light`: Sub-5° turret yaw/pitch misalignment, scorching and combat soot ($0.65$).
  - `medium`: Moderate turret tilt ($12.5^\circ$), hull list ($3.2^\circ$), soot ($0.85$).
  - `catastrophic`: Full structural destruction based on vehicle mobility type:
    - *Tracked / Wheeled Tanks & Artillery*: Decapitated turret thrown on ground at $24^\circ$ tilt; uncoiled crumpled track segment on terrain (`make_thrown_track_debris`); hull list ($5.5^\circ$).
    - *Bipedal & Quad Walkers / Mechs*: Knee-joint buckle collapse; pelvis and torso lowered to terrain ($Z \ge 0.0$); decoupled dropped weapons.
    - *Hover & VTOL*: Ventral crash-landing on terrain plane ($Z \approx 0.03$); tilted airframe.
    - *Super-Heavy Crawlers*: Ground-settled hull; fractured track pods; popped hatches.
- **Color Temperature Shading**: Scorched metal gradient, desaturated albedo ($0.45\times$), zero metallic reflectivity on burned facets, charred carbon soot ($1.0$).

---

## 18. Next-Gen Mechanical & Tactical Detail Suite (v2026.10 Wave 2)

### 1. Telescopic Hydraulic Piston Rams (`make_telescopic_piston_assembly` / `--hydraulic-pistons`)
Constructs dual-stage telescopic hydraulic actuator struts between arbitrary 3D endpoints using `get_orthonormal_basis`:
- **Topology**: Start and end clevis eye-mount knuckles with cross pins, outer hydraulic barrel housing (`gunmetal`), wiper seal gland collar (`tread`) with optional pressure ring (`warning`), and inner chrome/steel telescopic ram rod (`wheel_rim`).
- **Integration**: Automatically spans walker hip-to-knee joints on bipedal/quad walkers (`colossus_titan`, `titan_quad`, `longbow_strider`), gun elevation cradles on artillery/tanks (`sky_sweeper`, `dreadnought_crawler`, `volatile_runner`), and perimeter stabilizing struts on structures (`orbital_defense`, `tesla_obelisk`).

### 2. Articulated Metallic Ammo Belt Chutes (`make_ammo_belt_chute_assembly` / `--ammo-chutes`)
Generates flexible metallic ammunition feed chutes sweeping from ammo hoppers into rapid-fire weapon receivers:
- **Topology**: 3D curve with vertical catenary sag and outward lateral bow (`sin(pi * t)`), swept as a continuous rectangular `BMesh` ribbon with stepped expansion ribs, alternate exposed brass cartridge glimpse links (`warning`), source ammo hopper box, and breech feed throat collar.
- **Integration**: Auto-enabled on rotary gatling and quad flak presets (`recon_interceptor`, `apc_enforcer`, `dune_raider`, `sky_sweeper`, `resource_behemoth`).

### 3. Active Protection System & LWR Suite (`make_aps_hardkill_suite` / `--aps-suite`)
Equips modern armored turrets and defense bunkers with active countermeasure hardware:
- **Topology**: Bilateral swiveling hard-kill interceptor launcher pods with dual countermeasure mortar tubes, 4 corner faceted millimeter-wave AESA radar transceiver panels with dielectric faces, and a central roof hemispherical Laser Warning Receiver (LWR) optical prism (`emissive`).
- **Integration**: Auto-enabled on heavy tanks, cruisers, and airframes (`siege_breaker`, `gunship_alpha`, `dreadnought_crawler`, `hive_carrier`, `titan_quad`, `colossus_titan`, `sea_lance`, `sky_dreadnought`, `beam_destroyer`, `vls_missile_cruiser`, `aegis_bastion`, `spectre_jammer`, `bastion_shield_dome`).

### 4. Tactical Bustle Stowage & Spare Track Kit (`make_tactical_stowage_kit` / `--stowage-kit`)
Adds lived-in military field gear to vehicle hulls and turrets:
- **Topology**: Rear turret bustle tubular cargo rack frame, rolled canvas camouflage tarp bundle (`light_armor`) with dual rubber retention straps (`tread`), bilateral auxiliary Jerry fuel cans in side brackets, and forward glacis spare track-link armor blocks with a clamping bar.
- **Integration**: Auto-enabled on combat and logistics presets (`siege_breaker`, `recon_interceptor`, `apc_enforcer`, `resource_behemoth`, `aegis_bastion`, `inferno_purger`, `spectre_jammer`).

---

### Authoring Verification Commands
```powershell
# Verify twin script parity (0 byte difference required)
fc.exe /b tools\RogueModelKit.py .agents\skills\rogue-command-3d-model-authoring\scripts\RogueModelKit.py
fc.exe /b tools\generate_unit_model.py .agents\skills\rogue-command-3d-model-authoring\scripts\generate_unit_model.py

# Run complete loader validation suite (132 tests)
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .
```


## 19. Wave 3 Archetypes and Presets (local build/validation evidence)

- Six new mobile archetypes in `tools/generate_unit_model.py`: `mobile_sam_launcher`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `hexapod_assault`, `assault_hovercraft`. All are composed from existing RogueModelKit primitives and the faction palette (no new materials). Wreckage, gameplay-JSON template and rig metadata tables in `RogueModelKit.py` were extended.
- Twelve new presets (6 archetype presets, 4 remixes: `urban_brawler`, `arctic_sentinel`, `night_stalker`, `armored_hauler`; 2 structures: `early_warning_array`, `fusion_silo`). Presets may now declare `camo`, `wear` and `attachments` defaults; explicit CLI options win. `--preset` choices derive from `PRESETS` keys.
- Blender 4.5.14 headless: all 12 presets built with `Ground OK: True` (4.5k-8.5k tris before LOD); `arachnid_hexapod --rig --animate` built; `--batch-all-archetypes --dry-run` plans 30 archetypes. No in-game runtime evidence yet.
- Boundary: the generic `hexapod_assault` preset still uses a static body-bound middle pair; the active `ArachnidHexapod` rigged bridge now materializes `Bone_Thigh/Shin/Foot_ML/MR`, assigns rigid weights, and animates all six leg chains.

---

## 20. Wave 4 Tactical Archetypes, Strategic Structures, and Elite Presets (local build/validation evidence)

- **Six new mobile archetypes** (`tools/generate_unit_model.py` & `tools/RogueModelKit.py`), bringing total mobile archetypes to **36**:
  1. `bridge_layer`: Armored Vehicle-Launched Bridge (AVLB) on tracked chassis with front hydraulic launch boom (`A-frame` + twin hydraulic rams), elevated scissor hinge pin, and folded dual-lane ribbed treadway bridge spans with approach wedges.
  2. `command_hq_rig`: 8x8 heavy C4ISR mobile command truck with armored cab, expandable rear tactical operations shelter, roof satcom radome dish, telescopic comms mast with crossbars, and deployed side stabilizer outriggers.
  3. `flying_wing_bomber`: Low-observable delta flying-wing strike craft with swept stealth chines, serrated dorsal twin air intakes, recessed internal rotary bomb-bay doors, and flush exhaust slots.
  4. `mortar_carrier`: Tracked heavy siege mortar carrier with open-hatch fighting compartment, split folded roof doors, internal ribbed heavy breach-loaded mortar tube on turntable, and racked spare shells.
  5. `siege_drill_mole`: Subterranean assault bore vehicle with reinforced heavy crawler tracks, forward push-dozer shield, 4-stage stepped conical front auger drill head with helical tungsten cutter rings, rear rock-crusher exhaust chutes, and roof warning beacon.
  6. `heavy_crane_mech`: Bipedal industrial/siege walker (`rigType: BipedalWalker`) with wide-stance armored legs, roll-caged cockpit, dorsal fusion reactor core, overhead shoulder gantry crane boom, and dual heavy hydraulic pincer claws.
- **Three new strategic structure assemblies** (`RogueStructureBuilder`), bringing total structure types to **10**:
  1. `bunker_pillbox` (`make_bunker_pillbox_assembly`): Reinforced octagonal casemate with sloped armor walls, recessed panoramic firing embrasure, twin heavy automatic cannon barrels, blast-door rear entrance, and roof observation cupola.
  2. `anti_air_battery` (`make_anti_air_battery_assembly`): Rotatable azimuth turret house on heavy octagonal plinth with twin 4-cell inclined SAM box pods (8 sealed missile tubes) and central planar fire-control radar mast.
  3. `comms_relay_tower` (`make_comms_relay_tower_assembly`): Armored base transceiver shelter with climate-control vents, 4-chord tapered lattice truss mast, bilateral high-gain microwave drum dishes, and glowing omni-directional apex beacon spire.
- **Twelve new presets** (total **47 presets**):
  - 6 archetype presets (`scissor_bridgelayer`, `overlord_command_hq`, `wraith_stealth_wing`, `thunder_mortar_carrier`, `subterranean_mole`, `atlas_crane_mech`).
  - 3 structure presets (`fortified_pillbox`, `patriot_sam_battery`, `deep_space_relay`).
  - 3 elite custom-palette remix presets (`crimson_praetorian`, `cobalt_widowmaker`, `golden_aegis_command`) utilizing preset-level overrides for `primary_color`, `light_armor_color`, `emissive_color`, `vfx_flash`, and `vfx_thruster` (honored whenever the corresponding CLI flag is not explicitly passed).

---

## 21. Wave 5 Procedural BMesh Survivability, Camo Netting & Thermal Radiator Suite (local build/validation evidence)

- **Four new consolidated BMesh subsystem generators** (`tools/RogueModelKit.py` & `tools/generate_unit_model.py`), all reusing the existing `FactionPalette` materials (`primary_armor`, `light_armor`, `gunmetal`, `tread`, `emissive`) for **0 additional draw calls** after `batch_meshes_by_material()`:
  1. **Anti-HEAT Slat / Cage Armor (`make_slat_cage_armor_assembly` / `--slat-armor`)**:
     - Consolidates bilateral side-hull horizontal slat bars, vertical support ribs, angled hull standoff mounting brackets, and an optional turret bustle wrap-around cage screen into two single `BMesh` objects (`SlatCage_Grid`, `SlatCage_Struts`).
  2. **Explosive Reactive Armor Wedge Bricks (`make_era_reactive_bricks_assembly` / `--era-bricks`)**:
     - Generates staggered rows of Kontakt-5 / Relikt sloped ERA tiles across the forward glacis (with full `cos_p` / `sin_p` pitch alignment), bilateral angled turret cheek ERA wedge blocks, and recessed mounting bolts (`ERABricks_Tiles`, `ERABricks_Bolts`).
  3. **3D Noise-Displaced Camouflage Netting & Barrel Wrap (`make_camo_netting_assembly` / `--camo-netting`)**:
     - Subdivides a dorsal `bmesh.ops.create_grid` canopy displaced via `mathutils.noise.noise` with quadratic edge gravity droop ($d^2$) and solidifies it via `bmesh.ops.extrude_face_region` (`+0.028m` Z thickness) to prevent single-sided backface culling in Unity URP, accompanied by 4 corner telescopic support poles and 3 main-gun barrel ghillie wrap rings along `gun_pivot` / `gun_dir`.
  4. **Glowing Thermal Exchanger Radiator Banks (`make_thermal_radiator_assembly` / `--thermal-radiators`)**:
     - Builds bilateral recessed radiator shroud housings (`primary_armor`), glowing internal plasma/fusion heat-core beds (`emissive`), consolidated multi-slat `BMesh` cooling fin arrays (`gunmetal`), and longitudinal coolant manifold pipes (`tread`).
- **Builder & CLI Integration**:
  - Added `.add_slat_armor()`, `.add_era_bricks()`, `.add_camo_netting()`, and `.add_thermal_radiators()` to both `RogueVehicleBuilder` and `RogueStructureBuilder`.
  - Exposed `--slat-armor`, `--era-bricks`, `--camo-netting`, and `--thermal-radiators` CLI flags and auto-enabled them across matching tactical presets (`siege_breaker`, `urban_brawler`, `dreadnought_crawler`, `beam_destroyer`, `longbow_strider`, `inferno_purger`, `longshot_tankette`, `arctic_sentinel`, `thunder_mortar_carrier`, `subterranean_mole`, `atlas_crane_mech`, `fortified_pillbox`, `deep_space_relay`, `crimson_praetorian`, `cobalt_widowmaker`, `golden_aegis_command`, etc.).

---

## 22. Wave 6 Tactical Hardware & Heavy Support Suite (local build/validation evidence)

- **Four new consolidated tactical hardware generators** (`tools/RogueModelKit.py` & `tools/generate_unit_model.py`), reusing exclusively the canonical `FactionPalette` materials (`primary_armor`, `light_armor`, `gunmetal`, `tread`, `wheel_rim`, `emissive`) for **0 additional draw calls** after `batch_meshes_by_material()`:
  1. **Remote Weapon Station (`make_remote_weapon_station_assembly` / `--rws-mount`)**:
     - Independent roof-mounted traversable weapon station comprising an azimuth plinth, U-shaped traversing yoke with vertical trunnions, an armored ammunition feed box, a stabilized Electro-Optical / Infrared (EO/IR) sensor ball (dual FLIR thermal lens and laser rangefinder aperture in `emissive`), and an elevated rapid-fire autocannon barrel with a slotted flash suppressor.
  2. **Parasitic Recon / Escort Drone Docking Bay (`make_drone_docking_bay_assembly` / `--drone-dock`)**:
     - Recessed landing tray cradle base with perimeter chamfered guide curbs, magnetic charging puck, glowing runway alignment LED corner markers (`emissive`), and a docked parasitic reconnaissance quadcopter drone with central fuselage pod, ventral glowing sensor eye, and 4 outrigger ducted fan rotor cowlings.
  3. **Heavy Hydraulic Recoil Spades / Earth Anchors (`make_recoil_earth_spade_assembly` / `--recoil-spade`)**:
     - Heavy rear chassis transverse pivot hinge trunnion crossbar, mounting brackets, bilateral welded swing arms, a wedge spade anchor plate with 5 triangular ground-penetrating teeth biting firmly into the terrain plane ($Z \approx 0.02\text{m}$), and dual angled hydraulic actuator rams with chrome telescopic rods.
  4. **Perforated Thermal Barrel Shroud & Multi-Baffle Muzzle Brake (`make_perforated_barrel_shroud_assembly` / `--barrel-shroud`)**:
     - Ventilated cylindrical/octagonal thermal jacket constructed in consolidated `BMesh` with alternating radial cooling perforation slots, base and tip clamping collars, and an expanding multi-baffle muzzle brake at the barrel tip with bilateral horizontal gas-deflector baffles and stepped crown ring.
- **Builder & CLI Integration**:
  - Added `.add_rws_mount()`, `.add_drone_dock()`, `.add_recoil_spade()`, and `.add_barrel_shroud()` to both `RogueVehicleBuilder` and `RogueStructureBuilder`.
  - Exposed `--rws-mount`, `--drone-dock`, `--recoil-spade`, and `--barrel-shroud` CLI flags and auto-enabled them across matching tactical presets (`siege_breaker`, `apc_enforcer`, `sky_sweeper`, `dreadnought_crawler`, `hive_carrier`, `beam_destroyer`, `volatile_runner`, `longbow_strider`, `spectre_jammer`, `salvage_hauler`, `longshot_tankette`, `urban_brawler`, `night_stalker`, `early_warning_array`, `overlord_command_hq`, `thunder_mortar_carrier`, `fortified_pillbox`, `patriot_sam_battery`, `deep_space_relay`).

---

## 23. External Design Guidance Adapted to Rogue Command

The local art direction now uses a source-backed, camera-first review sequence. These sources inform the workflow; Rogue Command's own style, face budget, shader, and export contracts remain authoritative.

- **RTS silhouette before surface detail**: PlaySide's *Age of Darkness* art director describes prioritizing silhouettes from the gameplay camera and removing micro-detail that adds noise. Blizzard's *StarCraft* retrospective describes testing tiny renders and selectively widening/thickening features that failed to read. Adaptation: test each unit as a black silhouette at the elevated game view; exaggerate only role-defining parts that disappear at the local 30–120 px review scale. [PlaySide interview](https://www.unrealengine.com/developer-interviews/age-of-darkness-is-an-rts-that-throws-thousands-of-undead-monsters-at-you), [Blizzard retrospective](https://news.blizzard.com/en-us/article/20719767/rock-and-roll-days-of-starcraft-a-development-retrospective).
- **Reference-led hard-surface refinement**: Blender Studio's modeling guide and Blender's bevel documentation support a staged workflow that starts from references and blockout, then applies edge geometry selectively. Adaptation: treat RogueModelKit outputs as functional blockouts, shape the primary shells, and spend segments only on the silhouette or highlights that need them. Weighted normals improve shading but do not change the outline. [Blender Studio guide](https://studio.blender.org/tools/pipeline-overview/asset-creation/modeling), [Bevel Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/generate/bevel.html), [Weighted Normal Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/normals/weighted_normal.html).
- **Shading and runtime budgets**: Unity 2021.3 supports importing authored normals and tangents, warns that a single smoothing angle can smooth some edges incorrectly, and describes LOD as reducing triangles for distant objects. The current 10,000-face profile target is a project-specific LOD0 budget, not an official Unity limit or a quota. Record Blender faces and Unity-exported triangles separately. [Unity FBX Model Importer](https://docs.unity3d.com/2021.3/Documentation/Manual/FBXImporter-Model.html), [Unity LOD](https://docs.unity3d.com/2021.3/Documentation/Manual/LevelOfDetail.html).
- **Baked surface relief**: Blender's bake workflow and URP's Lit material documentation support transferring fine normal detail to a lower-detail mesh. Adaptation: bake into the existing Normal atlas only after UVs stabilize, and preserve MetallicGloss R/A, texture import settings, and mesh vertex-wear RGBA. [Blender Cycles baking](https://docs.blender.org/manual/en/4.5/render/cycles/baking.html), [URP 12.1 Lit](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@12.1/manual/lit-shader.html).

The local 50 px silhouette glance test, 30–120 px review range, and 65/25/10 visual hierarchy are authoring heuristics for this project. The cited sources support camera-first readability and staged refinement, but do not prescribe these numeric thresholds.


## 23. Unity prefab scaffolding of muzzle VFX and 3D audio

VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents adds (idempotently) FirePoint_0/FirePoint_1 under Socket_MuzzlePrimary/Socket_MuzzleSecondary with a ParticleSystem as child 0 (shared URP material Materials/VFX/VFX_MuzzleFlash_Additive.mat, max 25 particles), and 3D AudioSources (spatialBlend 1, logarithmic, 3-60 m, doppler 0) on Socket_WeaponAudio and Socket_EngineAudio (looping). Damage sockets stay clean for RuntimeVisualDirector. Evidence: static edit only; Unity compile and prefab build not yet run.
---

## 24. Wave 7 Tactical Archetypes, Procedural Weapons, and Support Hardware (local build/validation evidence)

- **Six new mobile vehicle archetypes** (`tools/generate_unit_model.py` & `tools/RogueModelKit.py`), bringing total mobile archetypes to **42**:
  1. `nanite_support_rover`: Agile 4x4 tactical medical/field repair rover with sloped glacis chassis, forward cab cowl, rear equipment bay, bilateral all-terrain wheel assemblies, and dual armored nanite repair dispenser pods.
  2. `gravity_siege_breaker`: Heavy assault citadel breacher on 6-roadwheel continuous tracks with armored upper casemate, rear power core, heavy V-prow ram, and gyroscopic grav-dampener hub.
  3. `tactical_relay_carrier`: Heavy 6x6 forward communications carrier with armored forward cab, rear electronics shelter, and high-gain telescopic comms & EW jamming mast.
  4. `amphibious_assault_tank`: Tracked assault tank with raised water-deflecting bow prow, sloped glacis, continuous tracks with roadwheels, and dual stern propulsion waterjet thrusters with vectoring steering nozzles.
  5. `plasma_mortar_strider`: Quadrupedal crawler siege strider (`rigType: QuadCrawler`) with cruciform center chassis, heavy turntable pedestal, articulated 2-bone crawler legs, and heavy rear ground recoil spade.
  6. `stealth_recon_skimmer`: Low-observable hover reconnaissance skimmer with sharp faceted radar-evasion chines, sloped glacis, and flush stealth canopy.
- **Two new procedural weapon assemblies** (`RogueModelKit.py` & `generate_unit_model.py`), bringing total weapon choices to **15**:
  1. `nanite_projector` (`make_nanite_projector_assembly` / `.add_nanite_projector()`): Articulated nanite dispersal cannon with elevation cradle, bilateral chemical canisters with glowing level-indicator sight glasses, flexible armored feeder conduits, stepped magnetic focusing collars, and flared multi-aperture dispersal bell with glowing collimating lens ring.
  2. `gravity_cannon` (`make_gravity_cannon_assembly` / `.add_gravity_cannon()`): Super-heavy gravitational singularity mass driver with sloped mantlet housing, dual heavy recoil damper cylinders, 4 stepped magnetic accelerator ring stages, glowing grav-core chamber, and longitudinal stabilization rails.
- **Four new consolidated tactical hardware subsystems** (reusing canonical `FactionPalette` for **0 additional draw calls** after `batch_meshes_by_material()`):
  1. **Armored Nanite Repair Dispensers (`make_nanite_repair_dispenser_assembly` / `--nanite-dispenser` / `.add_nanite_dispenser()`)**:
     - Bilateral armored nanite repair canisters with glowing inspection sight glasses and omni-directional atomizer spray heads.
  2. **Gyroscopic Grav-Dampener Hub (`make_grav_dampener_hub_assembly` / `--grav-dampener` / `.add_grav_dampener()`)**:
     - Cylindrical gyroscopic gravitational anchor and inertial dampener hub with counter-rotating magnetic rotor rings, 4 radial structural bracing struts, and glowing spherical singularity core.
  3. **Telescopic Comms & EW Jamming Mast (`make_comms_ew_mast_assembly` / `--comms-mast` / `.add_comms_mast()`)**:
     - 3-stage telescopic communications and EW jamming mast with planar blade antenna fins, cross-dipole jamming horns, satcom teardrop radome, and apex beacon LEDs.
  4. **Reinforced Kinetic Ram Prow (`make_reinforced_ram_prow_assembly` / `--ram-prow` / `.add_ram_prow()`)**:
     - Heavy angled V-shaped kinetic battering ram cowcatcher bumper with diagonal hazard chevron accent plates, structural gusset reinforcement ribs, and forged towing shackles.
- **Nine new presets** (bringing total presets to **56**):
  - 6 archetype presets (`sentinel_relay`, `gravitas_breaker`, `command_relay`, `coastal_dreadnought`, `plasma_strider`, `phantom_skimmer`).
  - 3 elite custom-palette remix presets (`emerald_warden`, `solar_colossus`, `void_spectre`) with custom primary/light/emissive color overrides and specialized VFX (muzzle star-spike, exhaust thruster plumes).
- **Wreckage & Gameplay Metadata Integration**:
  - `make_wreckage_variant`: Updated preservation filter to retain `"Nanite"`, `"Gravity"`, `"Prow"`, `"Dispenser"`, and `"Mast"` components across battle damage, and mapped new archetypes into respective chassis breakup categories.
  - `export_gameplay_metadata`: Added kinematic vehicle template mappings for all 6 new mobile archetypes (`PCXBaseTank` and `GatlingWalker`).
- **Parity Unit Test & Automated Validation**:
  - Twin script byte-for-byte binary parity strictly maintained between `tools/` and `.agents/skills/rogue-command-3d-model-authoring/scripts/`.
  - Added unit test suite `tests/python/authoring/test_model_kit_skill_copy.py` verifying file parity, archetype count (42), preset count (56), procedural assemblies, and builder chaining methods.
---

## 25. Hard-Surface Realism & Anti-Primitive Architectural Overhaul (local build/validation evidence)

This is a historical implementation inventory. The listed helpers do not guarantee that each generated asset is realistic or finished; Section 26 and the current authoring skill define the active silhouette, form, motion, and surface acceptance rules.

To address disconnected primitive stacks, the procedural modeling pipeline added or updated construction helpers across `RogueModelKit.py` and `generate_unit_model.py`:

- **Compound Faceted Ballistic Turret Architecture (`make_faceted_turret_housing`)**:
  - Replaces raw 3-box stacking with a 16-vertex monocoque kinetic turret shell:
    - **Armored Turret Bearing Ring / Race Collar (`Turret_TurretRing`)**: Cylindrical/octagonal bearing race (r ≈ width × 0.44, h = 0.08m) that physically anchors the turret into the hull deck, eliminating floating boxes.
    - **Kinetic Deflector Cheeks**: Forward wedge cheeks sloped at 32° for authentic projectile deflection.
    - **Inward Tumblehome Flanks & Raked Brow**: Lateral armor angles inward toward the roof with a sloped front brow.
    - **Undercut Rear Bustle**: Rear compartment underside angles upward at 28°, providing realistic clearance over the engine deck for 360° traverse without clipping.
    - **Recessed Cast Gun Mantlet Cradle (`Turret_MantletHousing`)**: Cast mantlet collar with sloped cheek bevels.
    - **Bilateral Composite Cheek Applique Plates (`Turret_CheekArmor_L/R`)**: Layered kinetic standoff armor panels flanking the mantlet.
- **Hull Turret Well Coaming & Glacis Driver Cupola (`add_sloped_hull`)**:
  - Integrated raised cylindrical turret coaming rim on the fighting compartment deck (`Armor_TurretCoaming`) to seat the turret race.
  - Forward glacis faceted driver cupola (`Armor_DriverHood`) with emissive periscope vision slit (`Armor_DriverPeriscope`), breaking flat upper glacis monotony.
- **Articulated Walker / Mech Torso & Cast Pelvis Architecture (`make_articulated_mech_torso`, `make_cast_pelvis_assembly`)**:
  - Eliminates the previous pattern of 4 vertically stacked cubes (`Pelvis`, `Torso`, `Chest`, `PowerCell`):
    - **Cast Pelvis Gimbal Assembly (`make_cast_pelvis_assembly` / `.add_cast_pelvis()`)**: Heavy central waist gimbal cylinder, bilateral forged hip clevis actuator brackets with cross-pin axle bosses, and lower groin chevron deflector plate.
    - **Faceted Monocoque Mech Torso (`make_articulated_mech_torso` / `.add_mech_torso()`)**: Tapered ballistic torso pod with sloped chest glacis plate (20° forward slope), recessed panoramic sensor visor cowl, bilateral shoulder pauldron mounting hardpoints with cylindrical joint cuffs, and dorsal power reactor bay with twin angled cooling exhaust stacks.
    - Applied across `walker`, `colossus_titan`, `heavy_crane_mech`, and `artillery_strider`.
- **Sculpted Wheel Arch Fenders (`make_sculpted_wheel_fender_assembly` / `.add_wheel_fenders()`)**:
  - Contoured 3-segment arched wheel well fenders hugging all-terrain tires, with forward mudflap deflectors, rear splash guards, and outer chamfered flare skirts.
  - Deployed on `nanite_support_rover`, `tactical_relay_carrier`, `halftrack`, and wheeled variants.


- **Transitional Mounting Coamings (`make_transitional_coaming_collar` / `.add_transitional_coaming()`)**:
  - Beveled perimeter mounting flanges bridging cabins, cowls, superstructures, hoppers, and equipment bays to chassis decks, eliminating unnatural computer-generated 90-degree knife-edge intersections.
  - Deployed across the full fleet: `super_heavy`, `siege_crawler`, `drone_carrier`, `combat_harvester`, `gravity_siege_breaker`, `mortar_carrier`, `mine_layer`, `recovery_crawler`, `bridge_layer`, `demolition_breacher`, `amphibious_assault_tank`, `ew_jammer`, `mobile_sam_launcher`, `command_hq_rig`, `buggy`, `bike`, `quad_mech`, `spider`, `hexapod_assault`, `plasma_mortar_strider`.
- **Aerodynamic Compound Fuselages & Wing Root Fillets (`make_aerodynamic_fuselage_assembly` / `.add_aerodynamic_fuselage()`)**:
  - Tapered aeronautical monocoques with multi-panel faceted cockpit canopies, upper shoulder tumblehome chamfers, bilateral jet/lift-fan intake scoops with splitter plates, ventral keel rails, and blended wing root fairings.
  - Deployed on `vtol`, `heavy_dropship`, and `flying_wing_bomber`, eliminating primitive rectangular-block aircraft bodies.
- **Segmented Plenum Skirt & Vectoring Louvers (`make_hover_skimmer_assembly`)**:
  - Flexible rubber plenum skirt pleats at bow and stern (22° deflection), lateral thrust vectoring air louvers, and perimeter welt bumper collar across all hovercraft (`hover`, `hover_cruiser`, `assault_hovercraft`, `stealth_recon_skimmer`).
- **Architectural Plinths & Buttress Pilasters (`make_factory_assembly`, `make_turret_bunker_assembly`)**:
  - Angled foundation corner buttresses and sloped armor glacis plinth rings anchoring industrial production halls and defense bunkers firmly to Z=0.0m foundation slabs.


### Wave 2 Hard-Surface Realism & Mechanical Fidelity Suite (local build/validation evidence)

To elevate mechanical fidelity beyond basic blockout geometry, Wave 2 introduces authentic heavy hardware, suspension running gear, joint deflection shields, and field rigging across `RogueModelKit.py` and `generate_unit_model.py`:

- **Heavy Forged Towing Clevis Shackles (`make_towing_shackle_assembly` / `.add_towing_shackles()`)**:
  - Welded mounting bosses, transverse retaining clevis pins, and hanging 18° angled forged D-ring shackles mounted low on front lower glacis and rear hull plates.
  - Deployed across 25 vehicles (`tank`, `artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `combat_harvester`, `demolition_breacher`, `thermobaric_tank`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `bridge_layer`, `mortar_carrier`, `siege_drill_mole`, `gravity_siege_breaker`, `amphibious_assault_tank`, `halftrack`, `shield_truck`, `ew_jammer`, `mobile_sam_launcher`, `command_hq_rig`, `tactical_relay_carrier`, `nanite_support_rover`).
- **Crane Rigging & Field Hoist Eye Lugs (`make_crane_lifting_lugs` / `.add_lifting_lugs()`)**:
  - 4-point forged circular eye lugs with reinforced weld pads mounted to upper hull, chassis deck corners, and turret roofs across 30 vehicles and mechs (`tank`, `artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `combat_harvester`, `demolition_breacher`, `thermobaric_tank`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `bridge_layer`, `mortar_carrier`, `siege_drill_mole`, `gravity_siege_breaker`, `amphibious_assault_tank`, `halftrack`, `shield_truck`, `ew_jammer`, `mobile_sam_launcher`, `command_hq_rig`, `tactical_relay_carrier`, `nanite_support_rover`, `colossus_titan`, `heavy_crane_mech`, `artillery_strider`, `walker`, `quad_mech`).
- **Trailing-Arm Suspension Linkages with Torsion Bosses & Coil Springs (`with_suspension_arms=True`)**:
  - Enabled by default on `add_continuous_tracks()`, connecting all roadwheels to the hull via mechanical swing arms, cylindrical torsion bosses, and coil shock dampers.
- **Rear Flexible Rubber Mudflaps (`FenderRearMudflap_L/R`)**:
  - Integrated into `make_continuous_track_assembly()`, angled at -32° to prevent realistic track debris throw.
- **Ballistic Chevron Knee Deflector Shields & Ankle Hydraulic Dampers (`make_quad_crawler_leg`)**:
  - Added angled chevron kneecap deflector armor (`KneeCapShield`) and parallel ankle hydraulic shock absorbers (`AnkleHydraulicShock`) to multi-leg spider, hexapod, and crawler archetypes.
- **Rocket Backblast Deflector Cowl & Umbilical Conduit Bundle (`make_missile_launcher_pod`)**:
  - Top overhanging blast deflector cowl (`BlastCowl`) and flexible umbilical firing cable conduit (`UmbilicalConduit`) routing rocket guidance controls into the launcher rack.
- **Factory Electrical Substation & Cable Raceway (`make_factory_assembly`)**:
  - Step-down electrical transformer housing (`SubstationTransformer`) and high-voltage cable raceway trunking (`CableRaceway`) running along industrial factory foundations.
- **Fleet-Wide Commander's Crew Cupolas (`.add_crew_cupola()`)**:
  - Armored commander cupolas with emissive periscope vision blocks deployed across 15 fighting vehicles.

### Waves 3 & 4 Hard-Surface Realism: Anti-Floating Anchors, Structural Bridging & Interlocking Armor Gussets (local build/validation evidence)

To resolve floating accessories and disconnected primitive joins ("figuras geométricas flotando o unidas sin anclaje"), Waves 3 & 4 deploy physical load-bearing mounts, drive axle housings, sponson bridges, and structural web gussets across `RogueModelKit.py` and `generate_unit_model.py`:

- **Structural Deck Pylons & Anti-Floating Weapon/Sensor Mounts**:
  - **Radar Turntable Pedestal Column (`Radar_DeckPylon`)**: Heavy cylindrical pedestal anchoring the radar turntable down into the turret/hull roof, eliminating floating rotodomes and dishes.
  - **Missile Pod Trunnion Cradle & Axle (`MissilePod_TrunnionCradle`, `MissilePod_TrunnionAxle`)**: Cast elevation saddle and trunnion pin mounting rocket pods securely to the deck rather than hovering in empty air.
  - **Pintle Mounting Saddles & Struts (`Gatling_PintleSaddle`, `Gatling_PintlePin`)**: Forged pintle brackets and transverse pins anchoring side-mounted rotary autocannons to vehicle hulls and mech shoulders.
  - **Arc Emitter Insulator Mounting Collar (`Arc_PedestalCollar`)**: Flanged armored pedestal collar locking the electrical arc projector core directly into the turret face plate.
  - **Cheek Smoke Rack Brackets (`Smoke_MountBracket_L/R`)**: Angled steel mounting brackets welding smoke grenade dispensers to turret armor cheeks.
  - **Cantilever Sponson Bridge Beams (`Sponson_BridgeArm_L/R_Front/Rear`)**: Dual load-bearing cantilever beams physically bridging external sponsons to the primary hull.
  - **Flanged Antenna Deck Sockets (`Sensor_AntennaDeckCollar`)**: Flared armored socket collars seating spring-loaded and whip antenna bases flush into decks.

- **Wheeled Chassis Drive Axle Beams & Vertical Shock Struts (`add_all_terrain_wheels`)**:
  - Heavy transverse drive axle beam housings (`Chassis_AxleBeam`) and center differential pumpkins (`Chassis_Differential`) spanning between left and right wheels across the chassis underside.
  - Vertical suspension shock absorber struts (`Chassis_ShockStrut_L/R`) connecting each wheel hub to the chassis frame rail, eliminating disconnected floating wheels.

- **Tracked Sponson Hull Floor Bridging (`add_continuous_tracks`)**:
  - Armored sponson floor plates (`Armor_SponsonBridge_L/R`) closing the hollow void between the inner track face and the lower hull tub.

- **Mech Hip Mounting Flanges, Calf Shields & Ankle Clevises (`make_bipedal_digitigrade_leg`)**:
  - Flanged hip rotary actuator collars (`HipFlange_L/R`) locking the leg pivot into the pelvis.
  - Rear armored calf deflector plates (`CalfArmor_L/R`) shielding the twin ankle hydraulic dampers.
  - Ankle clevis yoke brackets (`AnkleClevis_L/R`) physically boxing the ankle pivot to the stabilizer foot base.

- **Recessed Engine Deck Radiator Cavities (`add_engine_deck`)**:
  - Deep dark intake cavity bed (`Engine_RadiatorCavity`) recessed beneath the angled louvers to simulate real engine compartment depth.

- **Welded Triangular Interlocking Armor Gussets (`make_interlocking_armor_gusset` / `.add_interlocking_gussets()`)**:
  - 3D triangular web gusset brackets welded into 90° hull/superstructure joints across heavy combat vehicles (`tank`, `artillery`, `super_heavy`, `siege_crawler`, `colossus_titan`, `heavy_crane_mech`, `command_hq_rig`, `combat_harvester`, `demolition_breacher`, `gravity_siege_breaker`).

### Waves 5 & 6 Hard-Surface Realism: Ballistic Splash Protection, Bustle Stowage, CITV Sights, Recovery Winches, Fuel Drums & Thermal Deflectors (local build/validation evidence)

To eliminate primitive boxiness and inject authentic battlefield functional architecture across `RogueModelKit.py` and `generate_unit_model.py`:
- **V-Shaped Glacis Ballistic Splash Ribs (`make_glacis_splash_rib` / `.add_splash_rib()`)**:
  - Chevron-angled kinetic splash breaker ribs welded to the forward glacis plate, deflecting shell fragments, ricochets, mud, and water away from driver periscopes and turret ring raceways.
  - Deployed across 13 armored fighting vehicles (`tank`, `artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `thermobaric_tank`, `amphibious_assault_tank`, `demolition_breacher`, `gravity_siege_breaker`, `bridge_layer`, `mine_layer`, `mortar_carrier`, `drone_carrier`).
- **Welded Tubular Turret Bustle Stowage Baskets (`make_bustle_stowage_basket` / `.add_bustle_basket()`)**:
  - Dual-tier welded tubular perimeter rail basket with perforated mesh floor and hull mounting standoffs cantilevered off the turret rear overhang, breaking the planar rear silhouette and providing authentic gear/camo net stowage.
  - Deployed across 7 frontline combat turrets (`tank`, `artillery`, `super_heavy`, `arc_tank`, `flak_anti_air`, `thermobaric_tank`, `amphibious_assault_tank`).
- **Commander's Independent Thermal Viewer (CITV) Panoramic Sights (`make_panoramic_commander_sight` / `.add_commander_sight()`)**:
  - 360-degree rotating armored optic mast with lower turntable ring, dual sensor apertures (high-resolution FLIR thermal camera + laser rangefinder), and angled protective sunshade brow hood.
  - Deployed across 10 combat units (`tank`, `artillery`, `super_heavy`, `flak_anti_air`, `thermobaric_tank`, `colossus_titan`, `quad_mech`, `gravity_siege_breaker`, `combat_harvester`, `drone_carrier`).
- **Heavy Front Recovery Winch Assemblies (`make_recovery_winch_assembly` / `.add_recovery_winch()`)**:
  - Front bumper-mounted recovery winch unit featuring armored drive housing, grooved steel cable spool drum, and dual-axis (horizontal + vertical) roller fairleads for field vehicle recovery and self-extraction.
  - Deployed across 7 support, engineering, and heavy tracked/wheeled units (`recovery_crawler`, `halftrack`, `shield_truck`, `command_hq_rig`, `demolition_breacher`, `combat_harvester`, `bridge_layer`).
- **Soviet-Style Auxiliary External Fuel Drums (`make_auxiliary_fuel_drum_assembly` / `.add_fuel_drums()`)**:
  - Dual transverse cylindrical fuel/battery drums mounted to rear overhang via stamped steel cradle saddles, recessed end caps, double tension retention bands, and armored fuel transfer manifold line.
  - Deployed across heavy tracked main battle tanks (`super_heavy`, `artillery`, `arc_tank`).
- **Heavy Armored Exhaust Deflector Cowls (`make_exhaust_deflector_cowl` / `.add_exhaust_deflector()`)**:
  - Angled protective exhaust hood with directional heat diffuser louvers, directing black engine smoke and thermal signature downward and backward away from optics and cooling intakes.
  - Deployed on heavy utility and diesel engineering vehicles (`combat_harvester`, `bridge_layer`, `mine_layer`, `mortar_carrier`).
- **Articulated Mech Torso Yaw Bearing Collar & Flexible Knee Joint Dust Gaiters**:
  - `Torso_YawRace`: Heavy 14-vertex cast yaw bearing collar physically bridging the pelvis ring to the articulated upper torso in `add_mech_torso()`.
  - `KneeGaiter`: Flexible accordion rubber knee joint dust boot shielding internal hydraulic pistons from grit and shrapnel in `make_bipedal_digitigrade_leg()`.
### Wave 7 Hard-Surface Realism: Anti-Ring Weapon Overhaul, Tapered Cannon Barrels, Stator Rails & Two-Tier Shader Wear (local build/validation evidence)

To resolve the artificial "toy-like" look caused by straight constant-radius pipes and stacked concentric/toroid rings ("demasiados anillos y tubos rectos pegados entre si"), Wave 7 replaces primitive cylinder stacks across `RogueModelKit.py` and `VehiclePBR_VertexWear.shader` with authentic military ballistic and electromagnetic engineering:

- **Tapered Conical Frustum Primitive (`make_tapered_cylinder`)**:
  - Leverages Blender's `bpy.ops.mesh.primitive_cone_add(radius1, radius2, depth)` with directional quaternion tracking to generate realistic conical frustums.
  - Deployed across all cannon tubes, howitzer barrels, bore evacuators, and electrode prongs, providing authentic structural taper from thick reinforced breeches to narrower muzzles.
- **Continuous Tapered Cannon & Howitzer Assemblies (`make_coaxial_cannon_assembly`, `make_heavy_howitzer_assembly`, `make_dual_cannon_assembly`)**:
  - Heavy breech reinforce section tapering smoothly into barrel (1.25x to 0.92x caliber).
  - Double-conical aerodynamic bore evacuators with forward and aft transition tapers, completely eliminating flat rectangular steps and floating warning rings.
  - Multi-baffle muzzle brakes with real ventilated gas port cutouts.
- **Electromagnetic Stator Rail Beams (`make_plasma_mortar_assembly`)**:
  - Replaced stacked concentric floating rings/collars with four full-length longitudinal structural stator rail beams (`StatorRail_Top`, `_Bottom`, `_L`, `_R`) and recessed linear induction channels, transforming toy-like ring stacks into an authentic electromagnetic siege weapon.
- **Faceted Shroud & Heat-Sink Collimator Rails (`make_collimated_beam_laser_assembly`)**:
  - Replaced floating toroid donut rings with a faceted hexagonal jacket (`HexCollimationShroud`) and longitudinal heat-sink fin rails.
- **Dielectric Ceramic Housing & Convergent Electrode Prongs (`make_arc_energy_emitter`)**:
  - Replaced repetitive toroid coil loops with a faceted ceramic dielectric housing, heavy rear transformer insulator collar, and twin tapered convergent discharge prongs.
- **Conical Accordion Pleat Mantlet Dust Boot (`make_mantlet_dust_boot`)**:
  - Replaced disjoint floating toroid donuts with continuous conical accordion pleat pairs (`PleatConeA`, `PleatConeB`) producing an authentic flexible rubber/canvas protective bellows.
- **Two-Tier Edge Wear & Carbon Soot Specular Extinction (`VehiclePBR_VertexWear.shader`)**:
  - Two-tier wear transition curve: dark oxidized primer boundary outline (`#2c2724`, `wear 0.12-0.50`) transitioning into bare bright hardened alloy steel ridge (`#c7ccd6`, `wear > 0.42`), accurately simulating realistic paint chipping.
  - Thermal soot specular extinction: carbon soot scaling suppresses specular smoothness (`smoothness *= (1.0 - soot * 0.65)`) and reduces metallic response (`metallic -= soot * 0.40`), ensuring burned muzzles and exhausts render as velvety carbon-matte instead of plastic or glossy metal.

### Wave 8 Hard-Surface Realism: Procedural Scalloped Weld Seams, Weighted Normals, World-Space Mud & RNM Micro-Relief (local build/validation evidence)

To elevate realism beyond clean primitive blockouts and eliminate knife-edge joins and uniform shading:
- **Procedural Scalloped Weld Seam Chains (`make_weld_seam_bead` / `.add_weld_seams()`)**:
  - Simulates industrial MIG/TIG weld seams along plate boundaries (glacis-to-hull joints, cheek armor) via alternating-tilt low-poly elliptical weld puddle cones with 45° angle-weighted auto-smoothing.
  - Deployed on `tank`, `super_heavy`, and `artillery` sloped armor junctions.
- **Automatic Weighted Normal Enforcement in FBX Export (`merge_meshes_by_material`)**:
  - `merge_meshes_by_material` now systematically applies `shade_smooth_by_angle(38.0°)` and `apply_weighted_normals(weight=60, keep_sharp=True)` across all combined mesh batches.
  - Keeps broad armor planes mathematically flat (no shading pinch) while 2-segment chamfers reflect sharp specular sun glints.
- **World-Space Ground Mud & Road Dust Accumulation (`VehiclePBR_VertexWear.shader`)**:
  - Computes vertical height falloff from `positionWS.y` and `_MudHeight` (default 0.55m).
  - Procedurally blends albedo towards `_MudColor`, extinguishes metallic reflectivity (`metallic *= (1.0 - mudFactor)`), and dulls specularity to `_MudSmoothness` (0.20), grounding tracks, roadwheels, and lower hulls naturally onto terrain.
- **Reoriented Normal Mapping (RNM) & Toksvig Anti-Shimmering**:
  - Integrates `BlendNormalsRNM` for micro-armor grain (`_DetailBumpMap`) blending without flattening base normals.
  - View-distance Toksvig damping softens specular highlights at high RTS camera elevations to eliminate pixel shimmering.
- **Automated Open-Source Addon Installer (`tools/Setup-BlenderAuthoringAddons.ps1`)**:
  - Automates installation and verification of `EdgeFlow` (edge-loop curvature), `DreamUV` (trim-sheet mapping), and `Ucupaint` (layered PBR texturing) directly in Blender 4.5.14 LTS.

### Wave 9: Fleet-Wide Glacis Weld Seams & Shared Steel Micro-Detail Normal (local build/validation evidence)

- **`add_glacis_weld_seams()`**: hull-scaled scalloped weld beads along both glacis-to-side-plate junctions; applied to every archetype that already carries `add_splash_rib()` (13 total with the Wave 8 hand-placed trio).
- **`RCL_SteelMicroDetail_Normal.png`** (`tools/generate_steel_micro_detail_normal.py`): deterministic, seamlessly tileable (FFT-periodic) 256 px rolled-steel/cast-grain normal with anisotropic streaks and sparse pitting. `VehiclePrefabBuilder` binds it to `_DetailBumpMap` (scale 0.5) and the shader RNM-blends it independently of `_NORMALMAP`, fading out between 18 m and 58 m camera distance to prevent RTS shimmer.
- Runtime/Steam evidence: **none** for this wave. Builds, importer settings, and Blender previews do not prove in-game rendering; the Unity AssetBundle rebuild and a Profile2 Steam acceptance run remain pending.

### Wave 10: Deck-Edge Weld Seams on Every Sloped Hull (local build/validation evidence)

- **`add_deck_edge_weld_seams()`**: scalloped weld beads along both upper deck-to-side-plate edges, scaled to the hull; emitted right after each single-line `builder.add_sloped_hull(...)` call (23 archetype branches). Combined with Wave 9 glacis seams, sloped-hull vehicles now show welded plate construction on both the front and top edges.
- Evidence level: local Blender build only; no Unity bundle rebuild or Steam runtime run.


### Wave 11: Turret Ring Guards, Driver Periscope Hoods, Exhaust Heat Shields & Thermal Tempering (local build/validation evidence)

To banish floating boxes, disconnected primitive joins, and toy-like surfaces, Wave 11 deploys heavy load-bearing structural collars, crewed vision architecture, and metallurgical thermal tempering across `RogueModelKit.py`, `generate_unit_model.py`, and `VehiclePBR_VertexWear.shader`:

- **Armored Turret Ring Guard & Shot-Trap Deflector Collar (`make_turret_ring_guard` / `.add_turret_ring_guard()`)**:
  - Armored circular deflector ring / yaw-race coaming with upper kinetic bevel, stepped inner yaw bearing lip, and 8 radial hex bolt studs physically seating and boxing the turret into the hull deck.
  - Deployed across 12 turreted combat archetypes (`tank`, `artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `thermobaric_tank`, `combat_harvester`, `recovery_crawler`, `sniper_tankette`, `mortar_carrier`, `gravity_siege_breaker`, `amphibious_assault_tank`).
- **Cast Armored Driver Periscope Hoods (`make_driver_periscope_hood` / `.add_driver_periscope_hood()`)**:
  - Heavy cast steel hoods with sloped ballistic brows and recessed optical prisms (`Driver_PeriscopeGlass` with emissive sensor cyan) providing authentic crewed cockpit architecture.
  - Deployed across 11 armored hull archetypes (`tank`, `artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `demolition_breacher`, `gravity_siege_breaker`, `amphibious_assault_tank`, `mortar_carrier`, `mine_layer`, `bridge_layer`).
- **Perforated Exhaust Heat Shield Sleeves (`make_perforated_exhaust_heat_shield` / `.add_exhaust_heat_shield()`)**:
  - Tubular heat shield sleeves with tension clamp bands at both ends surrounding exhaust ducts to prevent plumbing pipe appearances.
  - Deployed on heavy utility and diesel engineering vehicles (`combat_harvester`, `bridge_layer`, `mine_layer`, `mortar_carrier`).
- **Thermal Tempering Fringe & Crevice Micro-Dust Softening (`VehiclePBR_VertexWear.shader`)**:
  - Metallurgical heat bluing: Simulates straw bronze (`#d4af37`) and iridescent blue/violet (`#435388`) temper oxide colors at the outer boundary of channel B (thermal soot, `soot 0.10 - 0.65`) before thick carbon soot takes over.
  - Crevice micro-dust softening: Deep recesses (channel R, Cavity AO) lose specular gloss due to trapped dry micro-dust (`smoothness *= lerp(0.70, 1.0, ao)`).
  - Zenith sun bleach: Upward-facing armor plates (`normalWS.y > 0.55`) receive subtle atmospheric sun-bleached oxidation.
- **Surface Diagnostics & Telemetry Expansion (`RuntimeVisualSurfaceCensus.cs`, `AgentDiagnosticsFormatter.cs`)**:
  - Exposes `temperFringeVertexCount` in `VisualSurfaceRow` and `visualSurfaceCensus` payloads.
- Evidence level: local build/validation evidence only; no fresh Steam runtime proof.

### Wave 12: Weapon Trunnion Brackets, Final Drive Planetary Housings & Armored Access Hatches (local build/validation evidence)

To banish primitive "stuck-on/floating" geometry and establish realistic structural load paths between weapons, drivetrain sprockets, and armored hulls, Wave 12 introduces 3 procedural mechanical assemblies across `RogueModelKit.py` and integrates them into `generate_unit_model.py`:

- **Weapon Elevation Trunnion Bracket Assemblies (`make_weapon_trunnion_bracket_assembly` / `.add_weapon_trunnions()`)**:
  - Heavy cast/machined elevation trunnion bearing cheek brackets flanking the weapon mantlet or gun pivot center.
  - Features left and right trunnion cheek housing blocks (`_CheekBlock_L/R`), heavy horizontal pivot pins (`_TrunnionPin_L/R`), flanged retention end caps / hub nuts (`_RetentionCap_L/R`), and flanged base mounting saddles (`_MountFlange_L/R`).
  - Physically anchors elevating gun tubes and ballistic barrels to the turret or casemate carriage.
  - Deployed across 10 weapon configurations and heavy artillery mounts (`railgun`, `gravity_cannon`, `laser_beam`, `quad_flak`, `plasma_mortar`, `howitzer`, `dual_cannon`, `sniper_tankette`, standard `cannon`, and `artillery` turret).
- **Planetary Final-Drive Transmission Gearbox Housings (`make_final_drive_housing_assembly` / `.add_final_drive_housings()`)**:
  - Stepped cylindrical cast planetary final-drive transmission gearbox housings connecting the lower hull sponson wall to drive sprockets.
  - Features heavy stepped main housing cylinders (`_DriveHousing_L/R`), circular 8-bolt mounting flange rings (`_FlangeRing_L/R`), and protruding center bearing lubrication hub caps (`_HubCap_L/R`).
  - Mechanically grounds the continuous track assemblies to the armored chassis, resolving the appearance of floating track belts.
  - Deployed across all 16 tracked combat and engineering archetypes (`artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `combat_harvester`, `demolition_breacher`, `thermobaric_tank`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `bridge_layer`, `mortar_carrier`, `siege_drill_mole`, `gravity_siege_breaker`, `amphibious_assault_tank`, `standard_tank`).
- **Raised Armored Crew & Maintenance Access Hatches (`make_armored_access_hatch_assembly` / `.add_access_hatches()`)**:
  - Raised armored crew ingress/egress and engine/battery inspection hatches.
  - Features raised perimeter coaming weld rims (`_HatchCoaming`), beveled protective hatch cover plates (`_HatchCover`), heavy cylindrical transverse hinge knuckles (`_HatchHinge`), and rotating dogging-clamp locking latch handles (`_HatchLatch`).
  - Supported on both vehicles (`RogueVehicleBuilder.add_access_hatches()`) and structures (`RogueStructureBuilder.add_access_hatches()`).
  - Deployed across 14 combat, command, and engineering vehicle archetypes (`artillery`, `super_heavy`, `combat_harvester`, `demolition_breacher`, `thermobaric_tank`, `recovery_crawler`, `mine_layer`, `bridge_layer`, `command_hq_rig`, `mortar_carrier`, `siege_drill_mole`, `gravity_siege_breaker`, `amphibious_assault_tank`, `standard_tank`).
- Evidence level: local build/validation evidence only; no fresh Steam runtime proof.

### Wave 13: Hydraulic Track Tensioners, Coaxial Gun Ports & Pioneer Entrenching Tool Racks (local build/validation evidence)

To banish floating track idlers, plain featureless sponsons, and flat turret faces, Wave 13 introduces 3 procedural assemblies across `RogueModelKit.py` and `generate_unit_model.py`:

- **Hydraulic Track Tensioners & Front Idler Wheel Yokes (`make_track_tensioner_idler_yoke_assembly` / `.add_track_tensioners()`)**:
  - Mechanical front idler wheel mounting linking the front sponson wall to the track loop.
  - Features longitudinal hydraulic tensioner cylinder barrels (`_TensionerCyl_L/R`), chrome-finished extension piston shafts (`_TensionerPiston_L/R`), heavy cast trailing yoke swing-arms (`_IdlerYokeArm_L/R`), idler spindle hub collars (`_IdlerSpindleHub_L/R`), and angled debris/mud scraper blades (`_TrackScraper_L/R`).
  - Completes the mechanical suspension loop alongside Wave 12 rear final-drive housings: both ends of the track assembly are now structurally engineered into the chassis.
  - Deployed across all 16 tracked combat and engineering archetypes (`artillery`, `arc_tank`, `super_heavy`, `flak_anti_air`, `combat_harvester`, `demolition_breacher`, `thermobaric_tank`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `bridge_layer`, `mortar_carrier`, `siege_drill_mole`, `gravity_siege_breaker`, `amphibious_assault_tank`, `standard_tank`).
- **Turret Coaxial Weapon Ports & Blast Deflector Cowls (`make_coaxial_gun_port_assembly` / `.add_coaxial_weapon_ports()`)**:
  - Armored secondary weapon port flanking main ordnance on the turret cheek or mantlet bezel.
  - Features a cast armored port housing block (`_PortHousing`), sloped ballistic blast deflector brow cowl (`_BlastCowl`), recessed secondary barrel sleeve (`_CoaxSleeve`), and perforated flash hider tip (`_CoaxMuzzle`).
  - Banishes empty, flat turret cheeks by establishing authentic secondary coaxial defense armament.
  - Deployed across 10 turreted combat archetypes (`standard_tank`, `super_heavy`, `artillery`, `flak_anti_air`, `thermobaric_tank`, `sniper_tankette`, `gravity_siege_breaker`, `amphibious_assault_tank`, `arc_tank`, `combat_harvester`).
- **Exterior Pioneer Entrenching Tool Racks & Sponson Stowage Cleats (`make_pioneer_tool_rack_assembly` / `.add_pioneer_tool_racks()`)**:
  - Field pioneer entrenching gear and stowage racks mounted along vehicle flanks, sponsons, and facility exterior decks.
  - Features dual stamped steel mounting C-cleats (`_MountCleat_0/1`) with dogging clamp clasps (`_ClampClasp_0/1`), an entrenching shovel (`_SpadeShaft`, `_SpadeBlade`), and an octagonal steel track-pin breaker pry bar with curved chisel claw tip (`_PryBar`, `_PryBarClaw`).
  - Supported on both vehicles (`RogueVehicleBuilder.add_pioneer_tool_racks()`) and structures (`RogueStructureBuilder.add_pioneer_tool_racks()`).
  - Deployed across 14 military combat and engineering vehicles (`standard_tank`, `super_heavy`, `combat_harvester`, `demolition_breacher`, `thermobaric_tank`, `recovery_crawler`, `mine_layer`, `bridge_layer`, `command_hq_rig`, `mortar_carrier`, `siege_drill_mole`, `gravity_siege_breaker`, `halftrack`, `shield_truck`).
- Evidence level: local build/validation evidence only; no fresh Steam runtime proof.

### Full Fleet & Active Unit Realism Rollout (Waves 11–13 Completed Integration)

Wave 11–13 procedural assembly coverage now reaches **all 42 archetypes** in `generate_unit_model.py` and **all 18 active pack units** in `ActiveUnitVisualEnhancements.py`. These counts describe helper deployment only; they do not certify realistic silhouettes, model finish, or gameplay readability:

- **Complete 42-Archetype Generator Coverage (`generate_unit_model.py` & skill twin)**:
  - **Raised Access & Maintenance Hatches (`add_access_hatches`)**: 43 deployments across tanks, tracked crawlers, walkers, mechs, wheeled vehicles, and aircraft.
  - **Turret Ring Deflector Guards (`add_turret_ring_guard`)**: 32 deployments across turreted combat vehicles and weapon stations.
  - **Driver Periscope Ballistic Hoods (`add_driver_periscope_hood`)**: 31 deployments on enclosed glacis and cab roofs.
  - **Pioneer Entrenching Tool Racks (`add_pioneer_tool_racks`)**: 29 deployments along sponsons, fenders, and side armor.
  - **Coaxial Secondary Gun Ports (`add_coaxial_weapon_ports`)**: 21 deployments beside main guns on combat turrets.
  - **Elevation Weapon Trunnions (`add_weapon_trunnions`)**: 20 deployments on elevating barrels and heavy weapon mounts.
  - **Planetary Final-Drive Housings (`add_final_drive_housings`)**: 19 deployments on tracked chassis and heavy crawlers.
  - **Hydraulic Track Tensioners & Front Idlers (`add_track_tensioners`)**: 19 deployments on front track sponsons.
  - **Exhaust Heat Shields (`add_exhaust_heat_shield`)**: 13 deployments over engine manifolds and exhaust pipes.
  - Every archetype has at least one assembly from these waves; this inventory is not a visual-quality score.
- **Full Active Pack Unit Integration (`ActiveUnitVisualEnhancements.py`)**:
  - Authoring profile map `ACTIVE_UNIT_REALISM_PROFILES` added for all 18 active units:
    - Tracked: `SandlanceScout`, `ColossusSiegePlatform`, `SurgeArcTank`, `BulwarkSiegeTank`, `GravitasBreaker` (final drive, track tensioner, access hatch, pioneer rack, turret ring guard, coaxial port, driver periscope, weapon trunnions).
    - Wheeled: `OverlordCommandHQ`, `FieldMedicRover`, `SentinelRelay`, `CommandRelay`, `SkyguardSAM`, `SalvageHauler` (access hatch, driver periscope, pioneer rack, turret ring guard).
    - Mechs / Walkers: `IroncladWalker`, `AtlasCraneMech`, `BastionSiegeMech`, `ArachnidHexapod`, `ArcLanceWalker` (access hatch, weapon trunnions, turret ring guard where turreted).
    - Hovercraft & Bike: `TidebreakerHovercraft` (access hatch, driver periscope, turret ring guard, coaxial port), `SkirmishStrikeBike` (access hatch, pioneer rack).
  - Idempotent builder integration via `apply_realism_details()`, chained into `apply_wave34_details()`.
- **Twin Script Parity & Quality Verification**:
  - Byte-for-byte binary parity between `tools/generate_unit_model.py` and `.agents/skills/rogue-command-3d-model-authoring/scripts/generate_unit_model.py` verified via `fc.exe /b`.
  - Comprehensive unit test `test_active_units_have_realism_profiles` and updated threshold assertions verified in `tests/python/authoring/test_model_kit_skill_copy.py`.
  - Full Python test suite passed cleanly (131/131 tests).
- Evidence level: local build/validation evidence only; no fresh Steam runtime proof.

## 26. Stylized Realism Research and Three-Model Pilot (local Blender evidence)

The current art direction is grounded stylized military science-fiction realism. This section and the linked authoring skill supersede any earlier implication that fixed detail ratios, a generator preset, or adding hardware by itself guarantees a realistic model.

### Research translated into project rules

- Blender Studio recommends collecting references, establishing a scaled blockout, testing movement, and refining the asset from several views; boolean-heavy refinements may need topology cleanup to restore smooth curvature. Rogue Command treats generator output as a functional blockout: refine the primary hull, torso, fairing, or facility shell before adding small parts, then inspect cut surfaces under moving highlights and retopologize pinched areas before UVs. [Blender Studio modeling guide](https://studio.blender.org/tools/pipeline-overview/asset-creation/modeling)
- Blizzard's StarCraft: Remastered team retained recognizable unit silhouettes while increasing visible detail. For Rogue Command, preserve each unit role and recognition cues, while developing original stance, proportions, negative space, and construction. [Blizzard development interview](https://news.blizzard.com/en-us/article/20726732/behind-the-scenes-of-starcraft-remastered)
- Turn that identity/readability balance into an explicit roster-divergence pass: compare each candidate against the closest native/mod counterpart, record the inherited default body plan and the role cues to retain, and sketch two structurally distinct blockouts if the first draft still follows the stock pattern. Choose a form whose visible support, clearance, movement, or thermal path explains its massing. The two-blockout exercise is a project heuristic, not a Blizzard rule. Novelty must come from form and function rather than surface noise or decorative attachments.
- Blizzard's RTS art guidance prioritizes unit readability and warns that a team color can disappear against similarly colored terrain. Add a preview check against representative light, dark, and similar-hue backgrounds; treat this as an art-direction check rather than runtime faction-color evidence. [Blizzard mapmaking art and performance guidance](https://news.blizzard.com/en-gb/article/20097658/mapmaking-best-practices-in-art-and-performance)
- Blender's bevel and baking references distinguish edge geometry and surface shading from transferred fine relief. Apply bevels by part construction, use flat shading for intentional armor facets and smooth shading for formed parts, and reserve tangent-space normal maps, cage setup, and UV padding for detail after geometry and UVs settle. [Bevel Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/generate/bevel.html), [Cycles baking](https://docs.blender.org/manual/en/4.5/render/cycles/baking.html)
- Blender's Weighted Normal modifier changes custom shading normals based on face area, corner angle, or face strength. Use it to preserve broad designed planes after the shape is authored; it cannot replace silhouette and cross-section refinement. [Weighted Normal Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/normals/weighted_normal.html)
- Unity's model importer and LOD workflow support authored normals/tangents and screen-relative simplification. Review shape at the actual camera scale, preserve authored shading, and simplify hidden or sub-pixel geometry first. The current 10,000-face LOD0 value is a project profiling ceiling, not an engine limit or target. [Unity 2021.3 Model Importer](https://docs.unity3d.com/2021.3/Documentation/Manual/FBXImporter-Model.html), [Unity 2021.3 LOD](https://docs.unity3d.com/2021.3/Documentation/Manual/LevelOfDetail.html)
- PBR guidance separates metalness from roughness: roughness controls reflection sharpness. For Rogue Command, use that principle to choose material-specific values and localized wear while preserving the project's MetallicGloss packing and vertex-wear contract. [Adobe OpenPBR overview](https://experienceleague.adobe.com/en/docs/substance-3d/general-knowledge/openpbr/openpbr-overview)
- Unity 2021.3's metallic workflow supports region-specific metallic and smoothness data: the Standard Shader reads metallic from R and smoothness from A. Use this to separate coating, bare metal, rubber, optics, and other materials rather than assigning one metallic value across the atlas; retain the actual mod shader/importer as the final contract. [Unity 2021.3 Metallic workflow](https://docs.unity3d.com/2021.3/Documentation/Manual/StandardShaderMaterialParameterMetallic.html), [Unity 2021.3 Smoothness](https://docs.unity3d.com/2021.3/Documentation/Manual/StandardShaderMaterialParameterSmoothness.html)

### Pilot results

- **Sandlance Scout:** refined the main hull, deck, side pods, turret, and gun nose with profiled shells. Local Blender builder result: 6,946 faces; bounds 2.17 × 2.70 × 1.57 m; ground contact passed.
- **Skirmish Strike Bike:** refined the front fairing and side/body shells, reduced the battery volume, and added four insulator supports beneath the induction ring. Local Blender builder result: 3,910 faces; bounds 1.20 × 2.20 × 1.31 m; ground contact passed. Its separate rig/export builder validated rigid weights and in-place clips and regenerated the atlas FBX.
- **Ironclad Walker:** refined torso, pelvis, shoulder, guard, and bracer shells. Faceted armor remains flat-shaded while formed housings and smaller joint-related covers use restrained smooth shading. Local Blender builder result: 4,396 faces; bounds 2.02 × 1.98 × 2.546 m; ground contact passed; rig, five action clips, and atlas export completed.

These three historical builds were measured against the former 7,500-face ceiling; all three results (6,946, 3,910, and 4,396 faces) sit within the current 10,000-face maximum. Their preview renders are under `artifacts/previews/`; face counts refer to Blender source geometry, not Unity triangles. Material Maker 1.7 produced strict 2048×2048 four-map preview sets under `artifacts/model-realism-pilot/material-maker/` with the matching unit presets. These generated maps remain review intermediates and were not promoted over the current unit material atlases. The local Unity authoring build and live Steam appearance were not verified in this pass.

- **Overlord Command HQ (ongoing, isolated candidate)**: a refined Blender build produced 6,696 faces and met the former 7,500-face ceiling; it also sits within the current 10,000-face maximum. The `neosteel_command_citadel` Material Maker set passed strict map/channel checks, but its broad blue edge emission and noisy normal detail failed visual review. An isolated graph with reduced microdetail and no emission passed the strict map gate, but the textured render still showed too much broad mottling. The `three_d` workflow also created and audited a UV-frozen, single-submesh paint proxy; its stock graph reproduced the same broad emission and dense surface pattern. Keep the geometry and map outputs as review candidates under `artifacts/pre-realism-fleet-step3-20261110/` and do not promote this texture set. The previews are local Blender authoring evidence, not Unity or in-game validation.
- **Gravitas Breaker (isolated LOD0 candidate)**: targeted duplicate removal found 22 exact overlapping meshes in towing/lifting and gusset details; a protected 48-degree dissolve pass on parts up to 1.2 m produced 6,271 Blender faces / 16,346 triangles with ground contact at 0.0088 m. The source and FBX candidate are under `artifacts/pre-realism-fleet-step2-20261110/candidate-48deg/`. The four-view review still reads as a slab/tank body and the splinter pattern remains visually busy, so this is a geometry-budget experiment only and is not promoted. Production source/FBX, maps, and AssetBundle remain untouched; appearance evidence is local Blender only.

### Acceptance rules for future fleet work

1. Define role, movement, primary system, and two or three silhouette cues from several references. Compare against the closest native/mod counterpart, retain role cues, and depart from its generic body-plan pattern where function supports a new silhouette.
2. If the first blockout follows the stock pattern, make two structurally distinct silhouette blockouts before detailed modeling. Record the selected design's role constraint, shape choice, and functional evidence.
3. Test a rough articulation or movement envelope while the blockout is still cheap to change. Pass side-by-side black-silhouette and neutral-clay reviews at actual RTS size before tertiary detail. Review front, side, top, elevated gameplay view, and a representative movement pose.
4. Refine large connected forms with changing profiles and purposeful transitions. After boolean or dense panel cuts, inspect curvature under grazing highlights and repair pinching or lumpy topology before UVs. Keep separate plates and joints where the mechanism requires them; reject both unmodified block stacks and oversized smooth blobs.
5. Recheck support, load path, clearance, and motion for every prominent weapon, sensor, wheel, leg, hatch, or suspension part.
6. Finalize geometry and UVs before baking. Keep the exact PBR map/channel/import contract, vertex Color RGBA, canonical sockets, rig/pivots, hierarchy, and LOD behavior.
7. Keep the material pass subordinate to the form pass: tune response per material and place grime/wear where use or exposure explains it. Do not use full-surface noise or edge highlights to compensate for weak shape design.
8. Distinguish intact coatings from exposed metal, flexible parts, glazing, and lights in the existing PBR channels. Keep emissions localized to functional elements and avoid outlining every panel.
9. Give generated maps an independent visual gate on the target UV model at RTS scale. A strict file/channel report does not establish material quality; reject debug overlays, conspicuous tiling, high-frequency noise, and color shifts that damage readability before promotion.
10. Select bevel and smoothing per part construction. Use weighted normals to preserve large authored faces, not as a blanket smoothing fix; inspect both clay and textured assets at the actual RTS view size. Preview intended color grouping against terrain colors that could compete with the faction tint.
11. Report Blender faces, exported Unity triangles, local Unity validation, and runtime appearance as separate evidence levels.

## 27. Quality-first 6,000-face reduction pass (2026-11-10, historical cap)

Vehicle and structure `build_and_export` methods run `RogueModelKit.reduce_subpixel_details_to_budget` after optional details/refinement and before their strict LOD0 gates. Preset bevel refinements use the same authored-data guards. Direct-source builders call the pass before skinning and vertex-color baking; the common `batch_and_join_by_material` and `merge_meshes_by_material` paths enforce the final cap. It prioritizes small fasteners, welds, lugs, shackles, and hinges, then dissolves redundant splits on eligible fittings while respecting UV discontinuities. Meshes with authored Color data, weights, custom normals, multiple material assignments, or explicit sharp/seam edges are skipped so those attributes remain exact. Its conservative stage is limited to 0.60 m; a bounded fallback reaches 1.20 m and protects armor/shell parts and turret bases, rings, yaw races, mantlets, and all main functional systems. Each proposed dissolve is compared against the prior surface in both directions using vertex, edge-midpoint, and face-center samples. The accumulated sampled displacement per mesh is capped at 0.02 m across both preset and budget passes. If those safe reductions are insufficient, export remains blocked for a model-specific redesign rather than count-only decimation. Reports now include the per-build face-reduction diagnostics. Exporters bypassing the shared paths need their own measured call and strict gate.

Re-auditing the earlier isolated candidates with the displacement guard shows which builds met the former 6,000-face cap. The r3 results below are historical candidates from before this check and should not be treated as current accepted outputs. The guarded r8 build of Sentinel Relay produced 5,993 Blender source faces and 12,864 Blender-profiled triangles with valid ground contact; its FBX and four neutral views are under `artifacts/face-reduction-6000-r8-20261110/SentinelRelay/`. Static Blender FBX reimport confirmed the same 5,993 faces / 12,864 triangles, UVs on all nine visible meshes, retained Color RGBA, armature and colliders, and 20 unique sockets with `FirePoint_0` parented under `Socket_MuzzlePrimary`. This is Blender export/reimport evidence, not Unity or in-game validation. Under the former 6,000-face cap, Command Relay remained at 6,480 faces, Overlord Command HQ at 6,269, and Gravitas Breaker at 8,893 after the guarded pass and therefore stopped before FBX export. No source-art blend, production FBX, atlas, Unity prefab, or bundle was promoted.

- **Command Relay (isolated candidate)**: the current builder produced 8,539 faces after its model-specific refinement; the shared cleanup reduced this to 5,981 faces / 12,940 triangles across 230 source parts batched into 8 Unity meshes, with ground contact at 0.055 m. Four neutral Blender views and the FBX are under `artifacts/face-reduction-6000-r3-20261110/CommandRelay/`. The candidate keeps the hull, missile system, turret, wheels, and mast. No PBR atlas was baked or promoted.
- **Sentinel Relay (isolated candidate)**: the current builder produced 7,195 faces after its model-specific refinement; cleanup reduced this to 5,968 faces / 12,870 triangles across 186 source parts batched into 9 Unity meshes, with ground contact at 0.0655 m. Four neutral Blender views and the FBX are under `artifacts/face-reduction-6000-r3-20261110/SentinelRelay/`. No production textures or assets were replaced.
- **Overlord Command HQ (isolated candidate)**: the current builder produced 8,588 faces after its custom wheel/fender/bevel pass; the shared cleanup produced 5,996 faces / 13,724 triangles across 231 source parts batched into 8 Unity meshes, with ground contact at 0.0505 m. Its four neutral views and FBX are under `artifacts/face-reduction-6000-r3-20261110/OverlordCommandHQ/`. No PBR atlas was baked or promoted.
- **Conservative in-memory source survey (under the former 6,000-face cap)**: after adding guards for authored colors, skinning, custom normals, material splits, and marked sharp/seam edges, six primary sources were flagged for model-specific work: Bulwark Siege Tank 9,320→7,143; Gravitas Breaker 12,400→12,400; Longshot Tankette 10,698→10,698; Minefield Sower 11,522→11,522; Salvage Hauler 7,979→6,274; Subterranean Mole 11,756→11,756. These are unsaved static Blender measurements on source `.blend` files before each unit's builder refinements. The refined Gravitas candidate at 8,428 faces was also blocked only by the former 6,000-face cap; it is numerically within the new cap but requires a fresh artifact-only build before acceptance. The three passing builds were isolated neutral candidates under the former cap only; no production `.blend`, FBX, atlas, or Unity prefab was overwritten or promoted.

## 28. LOD0 maximum raised to 10,000 faces (2026-11-11)

`RogueModelKit.DEFAULT_FACE_BUDGET` is now 10,000 Blender source faces. The generic CLI, direct model builders, procedural authoring helper, and Rogue Command 3D authoring skill all use the same ceiling. The quality gates remain unchanged: reductions are limited to eligible small-detail meshes, authored surface data and role-defining parts stay protected, and accumulated sampled surface displacement cannot exceed 0.02 m. The older 6,000-face reduction results remain historical; the previously guarded Command Relay (6,480), Overlord Command HQ (6,269), and Gravitas Breaker (8,893) counts fall under the new numeric cap, but still need a fresh artifact-only build before they can be considered current export candidates. No model, source blend, production FBX, texture, Unity prefab, or AssetBundle was changed by the cap update.
