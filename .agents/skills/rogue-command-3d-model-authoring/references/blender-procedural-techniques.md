# Blender Procedural Modeling Techniques for Rogue Command

This reference provides tested Python (`bpy`) recipes, mathematical foundations, and geometric algorithms to generate production-ready low-poly vehicle models deterministically in Blender 4.5.14 LTS.

All recipes are implemented in `tools/RogueModelKit.py` and `.agents/skills/rogue-command-3d-model-authoring/scripts/RogueModelKit.py`.

---

## 1. Coordinate Systems & Export Invariants

Blender and Unity use different coordinate handedness. To avoid flipped meshes, upside-down turrets, or inverted steering:

| Property | Blender World | Unity World (Imported Asset) |
|---|---|---|
| **Forward Direction** | `+Y` (Front of vehicle faces `+Y`) | `+Z` (Front of vehicle faces `+Z`) |
| **Up Direction** | `+Z` (Roof of vehicle faces `+Z`) | `+Y` (Roof of vehicle faces `+Y`) |
| **Right Direction** | `+X` (Starboard / Right side faces `+X`) | `+X` (Right side faces `+X`) |
| **Ground Plane** | `Z = 0.0` (Treads/wheels rest on `Z = 0.0`) | `Y = 0.0` (Treads/wheels rest on `Y = 0.0`) |
| **Vehicle Center** | `(0.0, 0.0, 0.0)` at ground center | Local origin `(0.0, 0.0, 0.0)` at identity |

### FBX Export Configuration

Always invoke `bpy.ops.export_scene.fbx` with these exact parameters:

```python
bpy.ops.export_scene.fbx(
    filepath=str(fbx_path),
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",         # Preserves custom weighted split normals & flat chamfers
    path_mode="AUTO",
    axis_forward="-Z",               # Maps Blender +Y to Unity +Z
    axis_up="Y",                     # Maps Blender +Z to Unity +Y
    bake_space_transform=False,
    add_leaf_bones=False,
    use_custom_props=False
)
```

---

## 2. Orthonormal Vector Framing & Directional Alignment

When aligning cylinders, barrels, or pods along arbitrary 3D vectors $\vec{d}$ (e.g., cannon elevation, rotary axles, angled outriggers), naive Euler rotations suffer from gimbal lock or sign inversion.

Always construct a right-handed orthonormal basis $(\vec{u}, \vec{v}, \vec{d})$:

$$\vec{u} = \frac{\vec{d} \times \vec{k}}{\|\vec{d} \times \vec{k}\|}, \quad \vec{v} = \frac{\vec{d} \times \vec{u}}{\|\vec{d} \times \vec{u}\|}$$

```python
def get_orthonormal_basis(direction: Vector) -> tuple[Vector, Vector, Vector]:
    """Constructs a right-handed orthonormal basis (u_vec, v_vec, dir_vec) around direction."""
    dir_vec = direction.normalized()
    if abs(dir_vec.z) < 0.92:
        u_vec = dir_vec.cross(Vector((0.0, 0.0, 1.0))).normalized()
    else:
        u_vec = dir_vec.cross(Vector((1.0, 0.0, 0.0))).normalized()
    v_vec = dir_vec.cross(u_vec).normalized()
    return u_vec, v_vec, dir_vec
```

For Blender cylinders (which naturally align along local Z), compute rotation via:
```python
rot_euler = dir_vec.to_track_quat("Z", "Y").to_euler()
```

---

## 3. Macro Hull & Chassis Generators

| Generator Function | Primary Parameters | Geometric Features & Chamfers |
|---|---|---|
| `make_sloped_glacis_hull` | `length_front`, `length_rear`, `width_front`, `width_rear`, `height_lower`, `height_upper`, `bevel=0.045` | Armored wedge chassis, sloped frontal glacis, undercut belly clearance, tapered sponson flanks |
| `make_faceted_turret_housing` | `location`, `size=(x,y,z)`, `armor_mat`, `roof_mat`, `bevel=0.035` | Octagonal faceted turret shell, sloped gun mantlet cradle, rear bustle counterweight |
| `make_troop_compartment` | `location`, `size`, `armor_mat`, `door_mat`, `detail_mat` | Armored personnel cabin, rear clamshell access doors with grab handles, roof commander cupola |
| `make_tubular_rollcage_assembly` | `size`, `location`, `bar_radius=0.034`, `tube_mat`, `roof_mat`, `visor_mat` | Welded tubular roll cage, A/B pillars, cross-header bar, diagonal C-stays, armored sun visor plate |

```python
# Chassis and superstructure construction pattern
hull = rmk.make_sloped_glacis_hull(
    name="Vehicle_Hull", length_front=1.45, length_rear=1.40,
    width_front=1.35, width_rear=1.42, height_lower=0.24, height_upper=0.82,
    material=armor_primary, bevel=0.045
)
turret = rmk.make_faceted_turret_housing(
    prefix="Vehicle_Turret", location=(0.0, 0.10, 1.15), size=(1.45, 1.65, 0.42),
    armor_mat=armor_primary, roof_mat=armor_light, bevel=0.035
)
cabin = rmk.make_troop_compartment(
    prefix="APC_TroopBay", location=(0.0, -0.95, 0.88), size=(1.42, 1.65, 0.74),
    armor_mat=armor_primary, door_mat=armor_light, detail_mat=gunmetal
)
```

---

## 4. High-Fidelity Mobility & Suspension Systems

| System Generator | Mobility Type & Ground Invariant | Key Subsystem Primitives |
|---|---|---|
| `make_continuous_track_assembly` | Continuous Track ($Z_{\min} = 0.0\text{m}$) | Articulated belt (~30° climb), 5–6 stepped road wheels, drive sprocket, return idler, cleats, skirts, catenary sag (`track_sag`), suspension arms (`with_suspension_arms`), spaced skirts (`with_spaced_skirts`) |
| `make_quad_track_pod_assembly` | Independent 4-Track Pod ($Z_{\min} = 0.0\text{m}$) | Independent continuous track pod, catenary sag (`track_sag`), swivel mounting sponson arm, top mudguard fender |
| `make_swing_arm_suspension_linkage` | Trailing Swing-Arm Linkage | Trailing swing arms, inboard torsion bar bosses on hull, upper coilover/hydraulic dampers |
| `make_spaced_armor_side_skirts` | Standoff Composite Skirts | Multi-panel armor tiles, top mounting rail, standoff bracket struts, rear rubber mudflaps |
| `make_all_terrain_wheel_assembly` | Tactical Wheeled ($Z_{\min} = 0.0\text{m}$) | Directional chevron tire lugs, deep-dish rim, planetary hub with 6 bolts, coilover shock (`add_all_terrain_wheels(..., middle_axle=True)` for 6x6 chassis) |
| `make_bipedal_digitigrade_leg` | Digitigrade Walker ($Z_{\min} = 0.0\text{m}$) | Rotary hip actuator, hydraulic thigh ram, knee armor cap, shin strut, 3-toed stabilizer foot |
| `make_quad_crawler_leg` | Quad Spider Crawler ($Z_{\min} = 0.0\text{m}$) | Rotary hip knuckle, upper femur boom, knee knuckle, lower tibia strut, circular rubber foot pad |
| `make_hover_skimmer_assembly` | Repulsor Hover ($Z_{\min} \approx 0.24–0.52\text{m}$) | Ventral repulsor cushion skirt, 4 plasma thruster bells, lateral stabilizer fins, vectoring ducts |
| `make_ducted_tiltrotor_assembly` | VTOL Tilt-Rotor (Aerial) | Shroud cowl with beveled lip, spinner hub, angled pitch blades, swivel pylon, plasma exhaust |

```python
# Bilateral track and walker leg instantiation patterns
for side in (-1, 1):
    # Continuous tracks: side * 1.10 spacing with suspension linkage & spaced skirts
    tracks = rmk.make_continuous_track_assembly(
        prefix="Vehicle_Treads", x_pos=side * 1.10, length=2.85, height=0.62, belt_width=0.34,
        roadwheel_count=5, tread_mat=tread_mat, rim_mat=wheel_rim, hub_mat=wheel_hub,
        axle_mat=gunmetal, fender_mat=armor_light, skirt_mat=armor_primary, accent_mat=warning_amber,
        with_suspension_arms=True, with_spaced_skirts=True
    )
    model_objects.extend(tracks)

    # Bipedal walker legs: hip at (side * 0.42, -0.05, 1.45)
    leg = rmk.make_bipedal_digitigrade_leg(
        prefix="Walker_Leg", hip_location=(side * 0.42, -0.05, 1.45), side=side,
        armor_mat=armor_primary, joint_mat=gunmetal, piston_mat=piston_chrome, accent_mat=warning_amber
    )
    model_objects.extend(leg)
```

---

## 5. Heavy Weapon System Generators

| Weapon Generator | Weapon Class & Pivot Semantics | Key Mechanical Primitives |
|---|---|---|
| `make_coaxial_cannon_assembly` | Coaxial Cannon (`gun_pivot`, `direction`) | Breech collar, thermal sleeve, bore evacuator, rifled barrel, double-baffle muzzle brake, recoil rams, accordion dust boot (`has_dust_boot`) |
| `make_mantlet_dust_boot` | Open Accordion Bellows | Hollow torus pleat rings with alternating radii ($1.14\times$ / $0.90\times$) and an open retention clamp, preserving the weapon bore |
| `make_dual_cannon_assembly` | Twin Cannon (transverse offset $\vec{u}$) | Unified armored mantlet, dual parallel rifled barrels, dual bore evacuators, muzzle brakes |
| `make_heavy_howitzer_assembly` | Ballistic Howitzer (`elevation_deg \approx 26^\circ`) | Massive breech collar, short large-bore barrel, reinforced blast ring, sector elevation gear |
| `make_rotary_gatling_cannon` | Gatling Cluster (angle $\theta_i = \frac{2\pi i}{N}$) | Rotor housing, central drive axle, 6 rifled barrels with clamp retaining collars, ammo feed chute |
| `make_missile_launcher_pod` | Rocket Pod ($R \times C$ array) | Armored pod shell ($10^\circ–20^\circ$ elevation), recessed launch tube grid, warheads, backblast louvers |
| `make_arc_energy_emitter` | Arc Plasma Emitter (`forward_dir`) | Generator base pedestal, glowing plasma cathode, dual toroidal induction coils, 3 convergent prongs |
| `make_collimated_beam_laser_assembly` | Directed Energy Laser (`laser_pivot`, `direction`) | Heavy emitter shroud, radiator cooling fins, focal aperture ring, high-voltage conduit cables |
| `make_vls_vertical_missile_array` | Vertical Launch Silo ($R \times C$ array) | Armored rectangular silo coaming, recessed missile cells with chamfered lips, exhaust duct trench |
| `make_flamethrower_assembly` | Heavy Flame Projector (`pivot`, `direction`) | Pressurized fuel manifold block, dual side feed tanks, perforated heat shroud, ventral igniter pilot assembly (`has_dust_boot`) |
| `make_emp_emitter_assembly` | Electromagnetic Pulse Array (`pivot`, `direction`) | Octagonal dielectric pedestal, 3 stepped concentric pulse rings, central waveguide mast, 4 dipole fins, glowing HDR pulse globe |

```python
# Coaxial cannon and rotary gatling alignment patterns
cannon = rmk.make_coaxial_cannon_assembly(
    prefix="Colossus_Gun", gun_pivot=(0.0, 0.52, 1.20),
    direction=(0.0, math.cos(math.radians(12)), math.sin(math.radians(12))),
    caliber_radius=0.095, barrel_length=1.65,
    armor_mat=armor_light, barrel_mat=gunmetal, accent_mat=warning_amber,
    has_thermal_sleeve=True, has_bore_evacuator=True, has_muzzle_brake=True
)

gatling = rmk.make_rotary_gatling_cannon(
    prefix="Ironclad_Gatling_R", pivot=(0.80, 0.45, 1.45), direction=(0.0, 1.0, 0.0),
    barrel_count=6, barrel_length=1.15, barrel_radius=0.032, cluster_radius=0.10,
    gunmetal_mat=gunmetal, housing_mat=armor_light, accent_mat=warning_amber
)
```

---

## 6. Micro-Greebles & Optical Sensors

| Greeble Generator | Functional Role | Mounting & Transform Invariant |
|---|---|---|
| `make_recessed_sensor_visor` | Primary FLIR / targeting visor | Armored brow hood with deep shadow cavity and glowing HDR slit lenses |
| `make_gimbal_sensor_turret` | 2-axis targeting pod | Azimuth pedestal, U-shaped elevation yoke, spherical sensor ball, thermal lens |
| `make_radar_dish_or_rotodome`| Recon radar antenna | Parabolic dish with feed horn or elliptical rotodome mounted on turntable mast |
| `make_armor_applique_array` | Explosive Reactive Armor (ERA) | Array of beveled armor tiles with corner standoff bolts along glacis seams |
| `make_hex_bolt_array` | Industrial structural rivets | Low-poly 6-sided bolt heads placed along armor seams and joint flanges |
| `make_smoke_launcher_rack` | Defensive smoke discharger | Multi-tube angled grenade rack with recessed launch bores on mounting bracket |
| `make_side_sponson_assembly` | External ammo / fuel drum | Armored flank panniers or cylindrical drums with retention straps |
| `make_dozer_blade` | Obstacle clearing ram | Heavy angled shield plate, bottom cutting edge, twin hydraulic push rams |
| `make_catenary_cable_assembly` | Catenary hydraulic hose / power conduit | Hyperbolic cosine (`cosh`) tube sweep in BMesh with corrugated rib modulation, hex bulkhead couplers, and retaining collar clamps |
| `make_bmesh_hull_greebles` | Recessed deck hatches, vents & lugs | Selective BMesh face inset (`bmesh.ops.inset_individual`) maintenance hatches with grab handles, micro-louver vents, and 4 forged lifting lugs |
| `make_tactical_decal_plates` | BVHTree-raycasted floating markings | `mathutils.bvhtree.BVHTree` normal-snapped chevron stripe bars and zenith identification plates (+0.012m normal offset) |
| `create_unity_locator_sockets`| Standardized FBX empties | Emits `Socket_MuzzlePrimary`, `Socket_TurretPivot`, `Socket_Exhaust_L/R`, `Socket_SensorEye` |

```python
# Sensor, defensive smoke, and socket locator patterns
sensor = rmk.make_gimbal_sensor_turret(
    prefix="Scout_FLIR", location=(0.42, 0.35, 1.35),
    radius=0.18, look_dir=(0.0, 1.0, 0.15),
    armor_mat=armor_light, lens_mat=emissive_cyan, gunmetal_mat=gunmetal
)
smoke = rmk.make_smoke_launcher_rack(
    prefix="Turret_Smoke", location=(0.65, 0.25, 1.25),
    tube_count=3, direction=(0.7, 0.7, 0.4),
    armor_mat=armor_primary, tube_mat=gunmetal, cap_mat=warning_amber
)
locators = rmk.create_unity_locator_sockets(
    name_prefix="SiegeTank",
    muzzle_loc=(0.0, 2.45, 1.25), muzzle_dir=(0.0, 1.0, 0.1),
    turret_loc=(0.0, 0.05, 1.15),
    exhaust_left=(-0.52, -1.35, 0.98), exhaust_right=(0.52, -1.35, 0.98)
)
```

---

## 7. Draw-Call Batching, Smart Smoothing, Weighted Normals & Cavity Baking

Procedural kitbashing produces 50–120 individual objects. If left unbatched, imported geometry creates redundant draw calls and material switches.

### A. Material Batching with Smart Angle Smoothing & Weighted Normals
`batch_and_join_by_material` unifies meshes sharing the same PBR material into 4–6 batched submeshes while preserving designated animated/gameplay pivots:

```python
optimized_objects = rmk.batch_and_join_by_material(
    model_objects,
    prefix="Colossus",
    preserve_names=["TurretBase", "MainBarrel"],
    smooth_by_angle=True,
    smooth_angle_deg=38.0,
    weighted_normals=True,
    normal_weight=50,
)
```

- **Smart Angle-Weighted Smoothing**: Uses Blender 4.5's `bpy.ops.object.shade_smooth_by_angle` to produce silky smooth round barrels, road wheels, hubs, and exhaust pipes, while preserving razor-sharp 45°/90° chamfers on flat armor plates.
- **Face-Area Weighted Normals (`apply_weighted_normals`)**: Applies Blender's `WEIGHTED_NORMAL` modifier (`mode="FACE_AREA"`, `weight=50`, `keep_sharp=True`) so large armor plates dominate vertex normals and 1-segment chamfers absorb the transition without shading pinch. Exported via `mesh_smooth_type="FACE"`.
- **Batching Estimate**: The authoring report estimates fewer mesh/material groups after batching; confirm actual draw calls in Unity with the selected prefab source and rendering settings.
- **Clean Normals**: Re-evaluates outward normals consistently and strips non-manifold seams.

### B. Multi-Channel Procedural Vertex Shading (`bake_advanced_vertex_shading`)
Calculates multi-channel crevice darkening, edge curvature scratching, thermal soot, and team color masking directly into `Color` (`Mesh.colors32`):

```python
rmk.bake_advanced_vertex_shading(
    model_objects,
    attribute_name="Color",
    wear_level="battle_hardened",  # pristine, field_tested, battle_hardened
    exhaust_locs=[Vector((-0.55, -1.35, 0.95)), Vector((0.55, -1.35, 0.95))],
    muzzle_loc=Vector((0.0, 2.50, 1.35))
)
```

- **R (Red)**: Ambient occlusion cavity dirt ($0.25–0.40$ on undersides and crevices; $1.0$ on zenith faces).
- **G (Green)**: Edge wear glints ($0.7–1.0$ on sharp convex edges with dihedral angle $\ge 35^\circ$; $0.0$ on flat planes).
- **B (Blue)**: Thermal soot ($0.0$ base metal; scales to $1.0$ at muzzles and exhaust tips with quadratic dropoff $(1 - d/R)^2$).
- **A (Alpha)**: Team color tint mask ($1.0$ on primary/light armor; $0.0$ on mechanical gunmetal and rubber treads).

### C. Multi-Level LOD Chain Generation (`generate_lod_chain`)
Creates decimated LOD mesh hierarchies with planar normal boundary preservation:

```python
lod_dict = rmk.generate_lod_chain(
    batched_objects,
    prefix="Colossus",
    lod_levels=(("LOD0", 1.0), ("LOD1", 0.50), ("LOD2", 0.22)),
    smooth_by_angle=True,
    smooth_angle_deg=38.0
)
# Returns {"LOD0": [...], "LOD1": [...], "LOD2": [...]}
```

### D. Physics Collision Hulls & Companion Metadata
```python
colliders = rmk.generate_collision_hulls(
    model_objects, prefix="Colossus",
    include_turret_collider=True, ground_offset_z=0.04
)

meta = rmk.export_gameplay_metadata(
    output_path=Path("Colossus.gameplay.json"), unit_name="Colossus",
    archetype="super_heavy", bounds=diagnostics["bounds"], sockets=locators,
    turret_center=Vector((0.0, 0.15, 1.25)), gun_pivot=Vector((0.0, 2.45, 1.45)),
    recoil_distance=0.25, elevation_range=(-8.0, 32.0), yaw_speed=35.0,
    visual_root_path="Vehicle"
)

wreckage_objects = rmk.make_wreckage_variant(
    source_objects=model_objects, prefix="Colossus",
    output_fbx_path=Path("Colossus_Wreckage.fbx"),
    output_blend_path=Path("colossus-wreckage.blend"),
    archetype="super_heavy",
    severity="catastrophic"  # light, medium, catastrophic
)
```

---

## 8. Multi-Perspective Studio & Quality Verification

### A. Multi-Perspective RTS Studio (`setup_multi_view_studio`)
Sets up AgX color management, 3-point area lighting, shadow-catcher ground plane, and 4 calibrated orthographic cameras:
- `ISOMETRIC`: Standard $45^\circ$ RTS combat view.
- `TOP_DOWN`: Direct vertical plan view (minimap/drafting evaluation).
- `FRONT`: Head-on silhouette readability.
- `SIDE`: Profile and suspension balance.

```python
cams = rmk.setup_multi_view_studio(
    model_objects,
    preview_dir=REPO_ROOT / "artifacts" / "previews",
    base_name="colossus-siege",
    ortho_scale=6.0,
    render_all=True  # Renders all 4 views automatically
)
```

### B. Automated Geometric Quality Diagnostics & Subsystem Profiling
Inspects model boundaries, ground contact, vertex counts, and material coverage before export:

```python
diagnostics = rmk.validate_model_mesh_quality(model_objects)
# Verifies:
# - Ground contact: -0.02m <= Z_min <= 0.08m
# - Polycount: typically 1,500 to 5,000 polygons
# - Material slots: zero unassigned faces or missing materials

profile = rmk.profile_model_budget_and_drawcalls(
    model_objects,
    prefix="Colossus",
    target_budget=4500
)
# Outputs detailed subsystem triangle distribution & budget health
```
