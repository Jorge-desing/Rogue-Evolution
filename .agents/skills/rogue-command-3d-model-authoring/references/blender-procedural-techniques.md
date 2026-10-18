# Blender Procedural Modeling Techniques for Rogue Command

**Updated:** 2026-10-18  
**Applies to:** `tools/RogueModelKit.py`, `tools/generate_unit_model.py`, and twin skill scripts.  
**Baseline:** Blender 4.5.14 LTS (headless scripting via `bpy`).

---

## 1. Coordinate Systems & FBX Export Configuration

| Axis / Property | Blender World | Unity World (Imported Asset) |
|---|---|---|
| **Forward** | `+Y` (Front of vehicle faces `+Y`) | `+Z` (Front of vehicle faces `+Z`) |
| **Up** | `+Z` (Roof of vehicle faces `+Z`) | `+Y` (Roof of vehicle faces `+Y`) |
| **Right / Starboard** | `+X` (Right side faces `+X`) | `+X` (Right side faces `+X`) |
| **Ground Plane** | `Z = 0.0` (Treads/wheels rest on `Z = 0.0`) | `Y = 0.0` (Treads/wheels rest on `Y = 0.0`) |
| **Local Origin** | `(0.0, 0.0, 0.0)` at ground center | Local origin `(0.0, 0.0, 0.0)` at identity |

### Authoritative FBX Export Parameters
```python
bpy.ops.export_scene.fbx(
    filepath=str(fbx_path),
    use_selection=True,
    object_types={"MESH", "ARMATURE", "EMPTY"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    use_mesh_modifiers=True,
    mesh_smooth_type="OFF",          # Preserves hard-surface flat chamfers
    axis_forward="-Z",               # Maps Blender +Y to Unity +Z
    axis_up="Y",                     # Maps Blender +Z to Unity +Y
    bake_space_transform=False,
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False
)
```

---

## 2. Orthonormal Vector Framing & Directional Alignment

When aligning barrels, thrusters, or outriggers along arbitrary 3D vectors $\vec{d}$, construct an orthonormal basis $(\vec{u}, \vec{v}, \vec{d})$:

$$\vec{u} = \frac{\vec{d} \times \vec{k}}{\|\vec{d} \times \vec{k}\|}, \quad \vec{v} = \frac{\vec{d} \times \vec{u}}{\|\vec{d} \times \vec{u}\|}$$

```python
def get_orthonormal_basis(direction: Vector) -> tuple[Vector, Vector, Vector]:
    dir_vec = direction.normalized()
    k_ref = Vector((0.0, 0.0, 1.0)) if abs(dir_vec.z) < 0.92 else Vector((1.0, 0.0, 0.0))
    u_vec = dir_vec.cross(k_ref).normalized()
    v_vec = dir_vec.cross(u_vec).normalized()
    return u_vec, v_vec, dir_vec

# Directional cylinder alignment
rot_euler = dir_vec.to_track_quat("Z", "Y").to_euler()
```

---

## 3. Procedural Assemblies Catalog (`RogueModelKit`)

### A. Chassis & Hull Assemblies
- `make_sloped_glacis_hull(name, length_front, length_rear, width_front, width_rear, height_lower, height_upper, material, bevel)`: Sloped tank chassis with belly undercut.
- `make_faceted_turret_housing(prefix, location, size, armor_mat, roof_mat, bevel)`: Octagonal turret shell with mantlet cradle and rear counterweight bustle.
- `make_troop_compartment(prefix, location, size, armor_mat, door_mat, detail_mat)`: Armored rear cabin with clamshell doors and vision slits for APCs.
- `make_tubular_rollcage_assembly(prefix, size, location, bar_radius, tube_mat, roof_mat, visor_mat)`: 3D welded exo-skeleton frame for buggies.

### B. Mobility & Propulsion Assemblies
- `make_continuous_track_assembly(prefix, x_pos, length, height, belt_width, roadwheel_count, ...)`: Track loop, drive sprocket, idler, bogie wheels, shoes, mudguard, side skirts.
- `make_quad_track_pod_assembly(prefix, location, length, height, belt_width, ...)`: Independent corner track pods for super-heavies.
- `make_all_terrain_wheel_assembly(prefix, location, radius, width, tire_mat, rim_mat, hub_mat, ...)`: Off-road chevron tires with planetary gear hubs.
- `make_bipedal_digitigrade_leg(prefix, hip_location, side, ...)`: Walker leg with hip actuator, hydraulic thigh piston, knee armor, and 3-toed foot.
- `make_quad_crawler_leg(prefix, hip_location, side_x, side_y, reach_x, reach_y, ...)`: Heavy 4-legged articulated spider limb with ground pad.
- `make_hover_skimmer_assembly(prefix, hull_length, hull_width, float_height, ...)`: Repulsor skirt, ventral plasma thruster bells, vectoring ducts.
- `make_ducted_tiltrotor_assembly(prefix, location, radius, depth, tilt_deg, ...)`: Aerodynamic VTOL fan pod with spinner hub and swivel bearing.

### C. Heavy Weapon Assemblies
- `make_coaxial_cannon_assembly(prefix, gun_pivot, direction, caliber_radius, barrel_length, ...)`: Rifled barrel, thermal shroud, bore evacuator, muzzle brake.
- `make_dual_cannon_assembly(prefix, gun_pivot, direction, caliber_radius, barrel_length, barrel_spacing, ...)`: Twin synchronized heavy cannons on unified mantlet.
- `make_heavy_howitzer_assembly(prefix, gun_pivot, direction, caliber_radius, barrel_length, ...)`: Large-bore siege mortar with recoil recuperators and quadrant gear.
- `make_rotary_gatling_cannon(prefix, pivot, direction, barrel_count, barrel_length, ...)`: Circular 6-barrel cluster with clamp rings and drive axle.
- `make_missile_launcher_pod(prefix, location, size, rows, cols, elevation_deg, ...)`: Armored honeycomb box pod with backblast vents.
- `make_arc_energy_emitter(prefix, base_location, forward_dir, core_radius, prong_count, ...)`: High-voltage cathode core, induction rings, discharge prongs.
- `make_railgun_assembly(prefix, gun_pivot, direction, rail_length, rail_spacing, ...)`: Parallel kinetic rails, capacitor bank, containment clamps, central core.
- `make_quad_flak_autocannon_assembly(prefix, gun_pivot, direction, caliber_radius, barrel_length, ...)`: 2x2 autocannon cluster with flash hiders and ammo feeds.
- `make_plasma_mortar_assembly(prefix, pivot, direction, caliber, ...)`: Siege mortar with 3 magnetic toroidal accelerator rings.

### D. Tactical Structures & Base Buildings (`RogueStructureBuilder`)
- `build_factory`: Slab base, industrial hall, roller door, gantry crane, roof stacks, `Socket_Deploy_Unit`.
- `build_defense_turret`: Stepped concrete pillbox, 360° traverse collar, weapon mount.
- `build_radar_station`: Comms bunker, 4-leg lattice mast, parabolic dish (30 RPM).
- `build_energy_silo`: Reactor vessel, foundation buttresses, containment rings, plasma conduits.
- `build_orbital_silo`: Heavy launch pit, blast berm, hydraulic blast doors, umbilical gantry tower.

---

## 4. Optimization, Weighting & Shading

### A. Material Batching & Smart Angle Smoothing
- `batch_and_join_by_material(objects, prefix, preserve_names, smooth_by_angle=True, smooth_angle_deg=38.0)`:
  - Joins meshes sharing materials into 4–6 unified FBX objects while preserving critical animated pivots.
  - Executes `bpy.ops.object.shade_smooth_by_angle(angle=math.radians(38.0))` to smooth cylinders and hubs while keeping 45°/90° chamfers sharp.

### B. Rigid Vertex Weighting & Armature Integration
- `assign_rigid_mesh_weights(objects, armature_obj, archetype)`:
  - Assigns discrete weight $1.0$ per armor plate on unbatched source meshes *before* join operations.
  - Prevents rubbery mesh deformation across joints when exported to Unity.
- `generate_procedural_armature(model_name, objects, archetype)`:
  - Generates 8-bone biped or 10-bone quad armature.
- `generate_procedural_action_clips(armature, builder)`:
  - Bakes 5 standard actions (`Action_Idle`, `Action_Walk` with $\Delta\text{pos} = (0, 0, 0)$ on `Bone_Root`, `Action_Drive`, `Action_FireRecoil`, `Action_Death`).

### C. Topological BMesh RGBA Shading (`bake_advanced_vertex_shading`)
- Detects dihedral aristas ($\ge 35^\circ$) and evaluates convexity $\Delta = (v_{other} - v_{edge}) \cdot \vec{n}_{f1}$.
- Encodes Cavity AO ($R$), Edge Wear ($G$), Thermal Soot ($B$), and Team Mask ($A$) into `Mesh.colors32`.

### D. Multi-Level LOD Chain & Colliders
- `generate_lod_chain(batched_objects, prefix, lod_levels)`: Produces LOD0 (100%), LOD1 (50%), and LOD2 (22%) meshes preserving planar boundaries.
- `generate_collision_hulls(model_objects, prefix)`: Produces flat-shaded `<Name>_COLLIDER_*` boxes.
- `export_gameplay_metadata(...)`: Writes `<Name>.gameplay.json` sidecar for Unity prefab construction.
