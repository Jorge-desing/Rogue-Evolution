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
    mesh_smooth_type="OFF",          # Preserves hard-surface flat shading
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

### A. Sloped Armored Glacis Hull (`make_sloped_glacis_hull`)
Generates an armored tank chassis with sloped frontal glacis, undercut belly clearance, and tapered sides.

```python
hull = rmk.make_sloped_glacis_hull(
    name="Vehicle_Hull",
    length_front=1.45, length_rear=1.40,
    width_front=1.35, width_rear=1.42,
    height_lower=0.24, height_upper=0.82,
    material=armor_primary, bevel=0.045
)
```

### B. Faceted Turret Shell with Bustle (`make_faceted_turret_housing`)
Creates an octagonal/faceted armored turret shell with sloped gun mantlet cradle and rear counterweight bustle.

```python
turret_parts = rmk.make_faceted_turret_housing(
    prefix="Vehicle_Turret",
    location=(0.0, 0.10, 1.15),
    size=(1.45, 1.65, 0.42),
    armor_mat=armor_primary, roof_mat=armor_light, bevel=0.035
)
```

### C. Armored Troop Cabin (`make_troop_compartment`)
Generates an enclosed rear personnel compartment for APCs and halftracks:
- Sloped armored crew shell with single-segment chamfers.
- Twin rear clamshell access doors with grab handles and recessed seams.
- Roof observation hatch / commander cupola.
- Flank armored vision slits and firing ports.

```python
cabin = rmk.make_troop_compartment(
    prefix="APC_TroopBay",
    location=(0.0, -0.95, 0.88),
    size=(1.42, 1.65, 0.74),
    armor_mat=armor_primary, door_mat=armor_light, detail_mat=gunmetal
)
```

### D. Off-Road Tubular Roll Cage (`make_tubular_rollcage_assembly`)
Generates an exo-skeleton welded tubular frame for strike buggies and scout vehicles:
- A-pillars (windshield posts) with cross-header bar.
- B-pillar main roll hoop and diagonal rear C-stays.
- Armored roof sun-visor plate and brow shield.

```python
rollcage = rmk.make_tubular_rollcage_assembly(
    prefix="Buggy_RollCage",
    size=(1.15, 1.45, 0.78),
    location=(0.0, 0.02, 1.02),
    bar_radius=0.034,
    tube_mat=gunmetal, roof_mat=armor_primary, visor_mat=armor_light
)
```

---

## 4. High-Fidelity Mobility & Suspension Systems

### A. Parametric Continuous Track Assembly (`make_continuous_track_assembly`)
Creates a complete track subsystem including:
1. Articulated continuous track belt with sloped front approach angle (~25°–35° climb).
2. Stepped road wheels array (outer tread rim, recessed hub dish, center axle cap).
3. Rear drive sprocket and front idler wheel.
4. Top-run cleats for mechanical surface rhythm.
5. Optional armored upper mudguard/fender and chamfered side skirts with hazard stripes.

```python
for side in (-1, 1):
    x_pos = side * 1.10
    tracks = rmk.make_continuous_track_assembly(
        prefix="Vehicle_Treads",
        x_pos=x_pos, length=2.85, height=0.62, belt_width=0.34,
        roadwheel_count=5, tread_mat=tread_mat, rim_mat=wheel_rim,
        hub_mat=wheel_hub, axle_mat=gunmetal,
        fender_mat=armor_light, skirt_mat=armor_primary, accent_mat=warning_amber
    )
    model_objects.extend(tracks)
```

### B. Heavy Tactical All-Terrain Wheel (`make_all_terrain_wheel_assembly`)
Generates a rugged off-road tire for recon buggies, missile trucks, and strike bikes:
- Directional chevron tread lugs around tire circumference.
- Deep-dish steel rim recessed inside the rubber carcass.
- Planetary gear reduction hub with 6-bolt hexagonal pattern.
- Suspension wishbone and coilover shock strut.

```python
wheel = rmk.make_all_terrain_wheel_assembly(
    prefix="Buggy_Wheel_FL",
    location=(0.85, 0.95, 0.38),
    radius=0.38, width=0.28,
    tire_mat=tire_rubber, rim_mat=wheel_rim, hub_mat=gunmetal,
    accent_mat=warning_amber, lug_count=10
)
```

### C. Articulated Digitigrade Bipedal Leg (`make_bipedal_digitigrade_leg`)
Generates a combat walker leg with mechanical joint kinematics:
- Rotary hip actuator cylinder.
- Armored thigh plate with hydraulic compression cylinder and chrome piston rod.
- Knee hinge joint with protective armor cap.
- Sloped shin armor plate with dual ankle damper struts.
- 3-toed stabilizer foot (center toe, lateral outrigger, heel claw) resting flat on $Z \ge 0.0$.

```python
for side in (-1, 1):
    leg = rmk.make_bipedal_digitigrade_leg(
        prefix="Walker_Leg",
        hip_location=(side * 0.42, -0.05, 1.45),
        side=side,
        armor_mat=armor_primary, joint_mat=gunmetal,
        piston_mat=piston_chrome, accent_mat=warning_amber
    )
    model_objects.extend(leg)
```

### D. Articulated Quad-Crawler Spider Leg (`make_quad_crawler_leg`)
Generates heavy 4-legged walking artillery and siege spider platforms:
- Heavy rotary hip knuckle actuator.
- Reinforced upper femur boom with hydraulic support ram.
- Knee hinge joint with protective knuckle armor.
- Lower tibia strut angled towards ground contact.
- Broad circular foot pad with rubber tread base and ankle shock sleeve resting exactly at $Z \ge 0.0\text{m}$.

```python
for sx in (-1, 1):
    for sy in (-1, 1):
        tag = f"{'L' if sx < 0 else 'R'}_{'F' if sy > 0 else 'R'}"
        leg = rmk.make_quad_crawler_leg(
            prefix=f"Spider_Leg_{tag}",
            hip_location=(sx * 1.10, sy * 0.95, 0.72),
            side_x=sx, side_y=sy,
            reach_x=1.15, reach_y=0.90, knee_height=1.02,
            armor_mat=armor_primary, joint_mat=gunmetal, damper_mat=armor_light
        )
        model_objects.extend(leg)
```

### E. Anti-Grav Repulsor Skimmer Assembly (`make_hover_skimmer_assembly`)
Creates a futuristic hovering propulsion unit for skimmers and assault hovercraft:
- Ventral repulsor cushion skirt with chamfered cowlings.
- 4 downward-firing thruster bells with glowing plasma field emitter cores.
- Lateral aerodynamic stabilizer strakes/fins angled for ground-effect lift.
- Twin rear vectoring thrust ducts with glowing internal nozzles.

```python
skimmer = rmk.make_hover_skimmer_assembly(
    prefix="Skimmer_Propulsion",
    hull_length=2.9, hull_width=2.5, float_height=0.52,
    skirt_mat=armor_primary, thruster_mat=gunmetal,
    core_mat=armor_light, glow_mat=emissive_cyan
)
model_objects.extend(skimmer)
```

### F. Ducted Tilt-Rotor Pod (`make_ducted_tiltrotor_assembly`)
Creates an aerodynamic ducted propulsion fan for VTOL gunships and aerial assault drones:
- Cylindrical outer shroud cowl with beveled intake lip.
- Inner recessed duct wall with aerodynamic bullet spinner hub.
- Angled pitch rotor blades radiating from hub.
- Transverse mounting pylon with swivel pivot bearing.
- Ventral glowing plasma core exhaust.

```python
tilt_rotor = rmk.make_ducted_tiltrotor_assembly(
    prefix="Gunship_TiltRotor_L",
    location=(-1.45, 0.15, 0.75),
    radius=0.44, depth=0.26, tilt_deg=14.0,
    duct_mat=armor_primary, rotor_mat=gunmetal,
    hub_mat=armor_light, pylon_mat=gunmetal, glow_mat=emissive_cyan
)
model_objects.extend(tilt_rotor)
```

### G. Independent Quad-Track Pod (`make_quad_track_pod_assembly`)
Generates 4-corner independent continuous track assemblies for mobile fortresses and super-heavy siege platforms:
- Self-contained continuous track belt with front and rear climb approach slopes.
- Stepped road wheels array and idler/sprocket units.
- Armored top mudguard fender.
- Inboard swivel mounting sponson arm connecting directly to the vehicle superstructure.

```python
track_pod = rmk.make_quad_track_pod_assembly(
    prefix="Fortress_TrackPod_FL",
    location=(-1.25, 1.0, 0.0),
    length=1.65, height=0.62, belt_width=0.34,
    tread_mat=tread_mat, rim_mat=wheel_rim, hub_mat=wheel_hub,
    axle_mat=gunmetal, fender_mat=armor_primary, accent_mat=warning_amber,
    ground_z=0.06, inboard_side=1
)
model_objects.extend(track_pod)
```

---

## 5. Heavy Weapon System Generators

### A. Coaxial Parametric Cannon (`make_coaxial_cannon_assembly`)
Guarantees coaxial alignment along gun elevation and azimuth vectors without drift:
- Heavy breech collar.
- Thermal shroud / sleeve with chamfered profile.
- Mid-barrel bore evacuator (fume extractor) with warning stripe.
- Rifled main barrel rod.
- Double-baffle muzzle brake with bore opening.
- Twin hydraulic recoil recuperator cylinders flanking the mantlet.

```python
cannon = rmk.make_coaxial_cannon_assembly(
    prefix="Colossus_Gun",
    gun_pivot=(0.0, 0.52, 1.20),
    direction=(0.0, math.cos(math.radians(12)), math.sin(math.radians(12))),
    caliber_radius=0.095, barrel_length=1.65,
    armor_mat=armor_light, barrel_mat=gunmetal, accent_mat=warning_amber,
    has_thermal_sleeve=True, has_bore_evacuator=True, has_muzzle_brake=True
)
```

### B. Rotary Gatling Cannon (`make_rotary_gatling_cannon`)
Generates an articulated 6-barrel (or N-barrel) Gatling autocannon:
- Cylindrical rotor housing / motor breech with ammo feed chute.
- Central drive axle shaft.
- Ring of N rifled barrels mathematically spaced at $\theta_i = \frac{2\pi i}{N}$.
- Mid-barrel and muzzle clamp retaining collars with optional warning accent.

```python
gatling = rmk.make_rotary_gatling_cannon(
    prefix="Ironclad_Gatling_R",
    pivot=(0.80, 0.45, 1.45),
    direction=(0.0, 1.0, 0.0),
    barrel_count=6, barrel_length=1.15,
    barrel_radius=0.032, cluster_radius=0.10,
    gunmetal_mat=gunmetal, housing_mat=armor_light, accent_mat=warning_amber
)
```

### C. Armored Missile Launcher Pod (`make_missile_launcher_pod`)
Generates a multi-cell rocket / missile pod:
- Armored pod chassis with upward elevation angle ($10^\circ–20^\circ$).
- Recessed dark launch cell well.
- Grid array of $R \times C$ cylindrical launch tubes with conical warheads peeking out.
- Rear backblast exhaust louvers / blast vents.
- Flank identification stripes.

```python
pod = rmk.make_missile_launcher_pod(
    prefix="Strike_RocketPod",
    location=(0.0, 0.15, 1.40),
    size=(0.72, 0.95, 0.48),
    rows=2, cols=3, elevation_deg=14.0,
    armor_mat=armor_primary, cavity_mat=gunmetal,
    warhead_mat=warning_amber, accent_mat=warning_amber
)
```

### D. Electromagnetic Arc Energy Emitter (`make_arc_energy_emitter`)
Generates a futuristic high-voltage arc weapon:
- Heavy generator base pedestal with heat sink housing.
- Central glowing plasma cathode core.
- Dual toroidal induction accelerator coils with dielectric insulator rings.
- Forward convergent discharge prongs angled toward a focal arc point ahead.

```python
arc_weapon = rmk.make_arc_energy_emitter(
    prefix="Surge_ArcEmitter",
    base_location=(0.0, 0.20, 1.25),
    forward_dir=(0.0, 1.0, 0.10),
    core_radius=0.22, core_length=0.48, prong_count=3, prong_length=0.55,
    housing_mat=armor_primary, gunmetal_mat=gunmetal, arc_mat=emissive_cyan
)
```

### E. Heavy Siege Howitzer / Mortar (`make_heavy_howitzer_assembly`)
Generates a massive large-bore ballistic artillery weapon:
- Heavy cylindrical breech collar with armored reinforcements.
- Large-caliber short barrel with rifled profile.
- Massive reinforced muzzle blast ring with high-visibility warning stripe.
- Dual hydraulic recoil recuperator cylinders flanking the breech.
- Sector elevation quadrant gear plates.

```python
howitzer = rmk.make_heavy_howitzer_assembly(
    prefix="Siege_Howitzer",
    gun_pivot=(0.0, 0.40, 1.20),
    direction=(0.0, math.cos(math.radians(26)), math.sin(math.radians(26))),
    caliber_radius=0.135, barrel_length=1.15,
    armor_mat=armor_light, barrel_mat=gunmetal, accent_mat=warning_amber
)
```

### F. Synchronized Dual Coaxial Cannon (`make_dual_cannon_assembly`)
Generates twin heavy autocannons on a unified mantlet:
- Armored mantlet block spanning both barrels.
- Dual parallel rifled barrels mathematically spaced along local transverse axis $\vec{u}$.
- Individual thermal shroud sleeves, mid-barrel bore evacuators, and double-baffle muzzle brakes.

```python
dual_cannons = rmk.make_dual_cannon_assembly(
    prefix="Fortress_DualGun",
    gun_pivot=(0.0, 0.42, 1.22),
    direction=(0.0, math.cos(math.radians(11)), math.sin(math.radians(11))),
    caliber_radius=0.082, barrel_length=1.65, barrel_spacing=0.38,
    armor_mat=armor_light, barrel_mat=gunmetal, accent_mat=warning_amber
)
```

---

## 6. Micro-Greebles & Optical Sensors

### A. Recessed Sensor Visor (`make_recessed_sensor_visor`)
Protective armored visor housing with recessed cavity and glowing HDR optical slits.

### B. Gimballed FLIR Sensor Turret (`make_gimbal_sensor_turret`)
2-axis gimballed spherical targeting pod with:
- Azimuth mounting pedestal and U-shaped elevation yoke.
- Spherical sensor ball.
- Primary circular thermal lens (with bezel ring).
- Secondary optical slit / laser designator aperture.

```python
sensor_pod = rmk.make_gimbal_sensor_turret(
    prefix="Scout_FLIR",
    location=(0.42, 0.35, 1.35),
    radius=0.18, look_dir=(0.0, 1.0, 0.15),
    armor_mat=armor_light, lens_mat=emissive_cyan, gunmetal_mat=gunmetal
)
```

### C. Explosive Reactive Armor (ERA) Bricks (`make_armor_applique_array`)
Array of modular armored tiles with corner standoff mounting bolts.

```python
era_tiles = rmk.make_armor_applique_array(
    prefix="Glacis_ERA",
    start=(-0.65, 0.75, 0.85), end=(0.65, 0.75, 0.85),
    count=5, size=(0.20, 0.24, 0.05),
    armor_mat=armor_light, standoff_mat=gunmetal,
    rotation=(math.radians(-25), 0.0, 0.0)
)
```

### D. Hexagonal Bolt Seams (`make_hex_bolt_array`)
Places low-poly 6-sided bolt heads along armor seams or joint flanges without excess geometry.

### E. Smoke Discharger Racks (`make_smoke_launcher_rack`)
Multi-tube defensive grenade banks with angled bracket and recessed launch bores:
```python
smoke_rack = rmk.make_smoke_launcher_rack(
    prefix="Turret_Smoke",
    location=(0.65, 0.25, 1.25),
    tube_count=3,
    direction=(0.7, 0.7, 0.4),
    armor_mat=armor_primary, tube_mat=gunmetal, cap_mat=warning_amber
)
```

### F. Tactical Radar Dish & Rotodome (`make_radar_dish_or_rotodome`)
Turntable-mounted parabolic dish or elliptical rotodome with feed horn and transceiver:
```python
radar = rmk.make_radar_dish_or_rotodome(
    prefix="Command_Radar",
    location=(0.0, -0.45, 1.35),
    dish_radius=0.42, is_rotodome=False,
    armor_mat=armor_primary, antenna_mat=gunmetal, accent_mat=warning_amber
)
```

### G. Armored Side Sponsons (`make_side_sponson_assembly`)
External armored ammo panniers or auxiliary fuel cells with retention straps:
```python
sponson = rmk.make_side_sponson_assembly(
    prefix="Tank_FuelDrum",
    location=(1.35, -0.20, 0.65),
    side=1, size=(0.26, 0.82, 0.38),
    armor_mat=armor_primary, strap_mat=gunmetal, accent_mat=warning_amber
)
```

### H. Procedural Unity Socket Locators (`create_unity_locator_sockets`)
Generates standardized empty transforms (`Socket_MuzzlePrimary`, `Socket_TurretPivot`, `Socket_Exhaust_L`, `Socket_Exhaust_R`, `Socket_SensorEye`) exported directly into the FBX hierarchy:
```python
locators = rmk.create_unity_locator_sockets(
    name_prefix="SiegeTank",
    muzzle_loc=(0.0, 2.45, 1.25), muzzle_dir=(0.0, 1.0, 0.1),
    turret_loc=(0.0, 0.05, 1.15),
    exhaust_left=(-0.52, -1.35, 0.98), exhaust_right=(0.52, -1.35, 0.98)
)
```

### I. Heavy Frontal Dozer Ram Blade (`make_dozer_blade`)
Generates an armored obstacle-clearing ram blade:
- Angled primary shield plate with heavy bottom cutting edge.
- Twin hydraulic push rams with spherical anchor brackets.

```python
dozer = rmk.make_dozer_blade(
    prefix="Breaker_Dozer",
    location=(0.0, 1.85, 0.42),
    width=2.6, height=0.45,
    armor_mat=armor_primary, ram_mat=gunmetal
)
```

---

## 7. Draw-Call Batching, Smart Smoothing & Cavity Baking

Procedural kitbashing produces 50–120 individual objects. If left unbatched, imported geometry can create many Unity objects and renderers. Batching compatible meshes reduces object and material overhead, but does not by itself establish the runtime draw-call count.

### A. Material Batching with Smart Angle Smoothing
`batch_and_join_by_material` unifies meshes sharing the same PBR material into 4–6 batched submeshes while preserving designated animated/gameplay pivots:

```python
optimized_objects = rmk.batch_and_join_by_material(
    model_objects,
    prefix="Colossus",
    preserve_names=["TurretBase", "MainBarrel"],
    smooth_by_angle=True,
    smooth_angle_deg=38.0
)
```

- **Smart Angle-Weighted Smoothing**: Uses Blender 4.5's `bpy.ops.object.shade_smooth_by_angle` to produce silky smooth round barrels, road wheels, hubs, and exhaust pipes, while preserving razor-sharp 45°/90° chamfers on flat armor plates.
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

### D. Physics Collision Hulls (`generate_collision_hulls`)
Generates flat-shaded bounding boxes tagged for Unity physics:

```python
colliders = rmk.generate_collision_hulls(
    model_objects,
    prefix="Colossus",
    include_turret_collider=True,
    ground_offset_z=0.04
)
# Returns [Colossus_COLLIDER_Hull, Colossus_COLLIDER_Turret]
```

### E. Companion Gameplay Metadata JSON (`export_gameplay_metadata`)
Generates companion specification for Unity prefab wiring:

```python
meta = rmk.export_gameplay_metadata(
    output_path=Path("Colossus.gameplay.json"),
    unit_name="Colossus",
    archetype="super_heavy",
    bounds=diagnostics["bounds"],
    sockets=locators,
    turret_center=Vector((0.0, 0.15, 1.25)),
    gun_pivot=Vector((0.0, 2.45, 1.45)),
    recoil_distance=0.25,
    elevation_range=(-8.0, 32.0),
    yaw_speed=35.0,
    visual_root_path="Vehicle"
)
```

### F. Procedural Damaged & Burned Wreckage (`make_wreckage_variant`)
Generates battle-destroyed model with dislodged turret and charred materials:

```python
wreckage_objects = rmk.make_wreckage_variant(
    source_objects=model_objects,
    prefix="Colossus",
    output_fbx_path=Path("Colossus_Wreckage.fbx"),
    output_blend_path=Path("colossus-wreckage.blend")
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
    render_all=True  # Renders all 4 views automatically!
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
