---
name: rogue-command-mech-rigging-authoring
description: Author, rig, and animate bipedal and quadrupedal mechs and walkers for Rogue Command with Blender armatures, in-place locomotion, and Unity Generic Animators.
---

# Rogue Command Mech Rigging & Armature Authoring

This skill guides the design, skeletal rigging, mechanical vertex weighting, gait cycle animation, and Unity Mecanim setup for custom bipedal and quadrupedal walker units in Rogue Command.

---

## 1. Engine Realities & Rigging Paradigms

Reverse engineering of Rogue Command 1.0.9 (e.g. `GatlingWalker` in `resources.assets`) confirms two supported approaches:

1. **Native Hierarchical Transform Rig:**
   - Visual nodes are individual GameObjects with `MeshFilter` and `MeshRenderer` attached to parent/child rotation pivots:
     - `RotHor`: Horizontal azimuth traverse
     - `ROTVERT`: Vertical weapon elevation
     - `LEGS` -> `RightLeg` / `LeftLeg` -> `RLow` / `LLow` -> `RFoot` / `LFoot`
   - Zero-code procedural aiming and step dust; requires no `Animator` component or bone armature.
2. **Skeletal Armature Rig (Mecanim Generic):**
   - Single Armature with standardized bone names (`Bone_Root`, `Bone_Pelvis`, `Bone_Thigh_*`, `Bone_Shin_*`, `Bone_Foot_*`).
   - Rigid vertex weighting ($1.0$ weight per mechanical armor plate) preventing rubbery mesh deformation.
   - Four baked in-place NLA Actions (`Action_Idle`, `Action_Walk`, `Action_FireRecoil`, `Action_Death`).
   - Driven autonomously at runtime by [`RuntimeVisualDirector`](../../../src/RogueCommandExpansionLoader/Runtime/Visual/RuntimeVisualDirector.cs) via parameters `RCL_IsMoving`, `RCL_Fire`, and `RCL_Death`.

---

## 2. Step-by-Step Mech Authoring Workflow

```
[1. 3D Modeling] -> [2. Armature Rigging] -> [3. Rigid Weighting] -> [4. Gait Animation] -> [5. Unity Prefab] -> [6. Validation]
```

### Step 1: Model Articulated Limb Segments
- Build separate mechanical pieces for hip actuator, thigh bone & armor, knee hinge, shin bone & armor, and multi-toed foot.
- Ensure feet rest precisely on the ground plane at $Z \ge 0.0$ in Blender ($Y \ge 0.0$ in Unity).

### Step 2: Construct the Bone Armature
- Create `Bone_Root` at $(0, 0, 0)$.
- Parent `Bone_Pelvis` (or `Bone_Hull`) to `Bone_Root`.
- Extrude leg chains: `Bone_Thigh_L/R` -> `Bone_Shin_L/R` -> `Bone_Foot_L/R`.
- Extrude aiming bones: `Bone_TurretYaw` -> `Bone_TurretPitch` -> `Bone_BarrelRecoil`.
- Refer to [biped-quadruped-rigging-guide.md](references/biped-quadruped-rigging-guide.md) for quad mechs and joint limit details.

### Step 3: Apply Rigid Vertex Weighting
- In hard-surface mechs, **never use automatic smooth heat weights**.
- Assign 100% weight ($1.0$) of each armor plate or hydraulic cylinder to exactly one parent bone.
- Add an `Armature` modifier pointing to the armature object on all skinned meshes.

### Multi-leg (hexapod) rigs
- Model six leg chains as `FL`/`FR`/`RL`/`RR` plus `ML`/`MR`. Add `Bone_Thigh_ML/MR`, `Bone_Shin_*`, and `Bone_Foot_*`, and bind middle meshes to those bones; do not leave mid armor on `Bone_Hull`.
- Animate the middle legs in `Action_Walk` as part of a tripod gait (for example, `ML` with `FR`/`RL` and `MR` with `FL`/`RR`). Otherwise the middle legs remain static while the outer legs move.
- Derive each leg tag from the authored token between `CrawlerLeg_` and the next underscore (`L_F`, `R_R`, etc.). Do not use substring checks for `FL`/`FR`/`RL`/`RR`: names such as `..._FemurBoom` contain `R_F` and misclassify legs.

### Rigid rebinding hygiene
- Before reassigning weights, remove all existing vertex groups and Armature modifiers from each mesh. Otherwise stale groups persist and the mesh is not rigidly bound to exactly one bone.
- Call `assign_rigid_mesh_weights(..., leg_type=...)` in the rigged build script before `batch_and_join_by_material` and FBX export. Generating animation clips alone does not repair stale source weights.

### Step 4: Animate In-Place Locomotion & Actions
- **Strict In-Place Invariant:** Root bone `Bone_Root` must have zero position delta across the entire clip ($\Delta X = 0, \Delta Y = 0, \Delta Z = 0$). `EntityFlockingMovement` translates the unit in the game grid.
- Keyframe the 4 standard actions:
  - `Action_Idle` (30 frames loop)
  - `Action_Walk` (40 frames loop, in-place, pelvis dip and foot clearance)
  - `Action_FireRecoil` (15 frames impulse and damped recovery)
  - `Action_Death` (45 frames knee buckle and collapse)
- Refer to [gait-cycle-and-in-place-locomotion.md](references/gait-cycle-and-in-place-locomotion.md) for keyframe tables.

### Step 5: Mount Canonical Gameplay Sockets
Mount empty GameObjects oriented along Unity forward (+Z) and up (+Y):
- `Socket_TurretPivot`: Torso / Turret azimuth pivot
- `Socket_BarrelPitch`: Gun elevation pivot
- `Socket_MuzzlePrimary`: Firepoint spawn origin
- `Socket_DamageSmoke`: Mount for $\le 50\%$ health smoke particles
- `Socket_DamageFire`: Mount for $\le 25\%$ health fire particles
- `Socket_FootContact_L` / `Socket_FootContact_R`: Foot impact points

### Step 6: Unity Import and Prefab Generation
- In Unity Model Importer:
  - **Animation Type:** `Generic` (Do NOT use Humanoid).
  - **Avatar Definition:** `Create From This Model`.
  - Bake Root Transform Position (Y, XZ) and Rotation into Pose.
- Run `Tools/Rogue Command/Build AssetBundles` or inspect via `VehiclePrefabBuilder.cs`.
- Refer to [unity-avatar-and-animator-contracts.md](references/unity-avatar-and-animator-contracts.md) for AnimatorController transition states.
- When a rigged FBX is the source, point the scaffolder at the rigged FBX, not the static atlas FBX, and configure its importer as `Generic` with `Create From This Model` before prefab generation.
- A rigged import can carry its own `Animator`. Remove every imported Animator from the model instance before calling `SetupAnimatorIfAvailable`; otherwise the prefab/bundle retains two Animators and the rigging contract fails. The final visual root must contain exactly one configured Animator with a valid Generic avatar and runtime controller.
- **VisualOnly/Prefabs can now use `CreateRiggedPbrVisual`** to automate rigging integration. This method instantiates the rigged FBX, adds canonical VFX and walker sockets, calls `SetupAnimatorIfAvailable`, disables root motion, and saves as prefab. It is used for PBR-textured units (ArachnidHexapod, OverlordCommandHQ) whose visual prefabs live in `VisualOnly/Prefabs/` but whose FBX uses the batch material system (not PBR textures). The method validates that the resulting prefab has a valid Generic Avatar and runtime controller. Units using this method do not need card sprites; pass `null, null` for card suffix/name in `ValidateRiggedWalkerBundleAssets`.
- **ModelImporter must be configured before prefab generation.** Call `ValidateWalkerModelSource` (or equivalent) for each rigged FBX to set: `animationType = Generic`, `avatarSetup = CreateFromThisModel`, `importAnimation = true`, `optimizeGameObjects = false`. Without `CreateFromThisModel`, the avatar is null and `SetupAnimatorIfAvailable` fails.
- **Material resolution for PBR units.** The batch material system strips unit-name prefixes (`ArachnidHexapod_`, `OverlordCommandHQ_`) from material keys. Add these to `intermediatePrefixes` in `TryResolveBatchMaterial` and ensure aliases cover all material types used (PrimaryArmor→armor, Gunmetal→gunmetal, Tread→tread, WheelRim→wheelRim, Warning→warning, Emissive→emissive, LightArmor→lightArmor).
- **Model yaw correction uses only the horizontal forward axis (Z), never vertical height (Y).** `CalculateVisualYawCorrection` must compare muzzle vs. rear socket positions along Z (Unity forward). Using Y (vertical height) or picking the dominant axis between Y and Z caused false 180° flips on walkers whose muzzle/rear height difference exceeded their horizontal separation. Use `muzzleLocal.z` and `rearLocal.z` exclusively.
- **Runtime movement detection must use a small fixed squared threshold.** `RuntimeVisualDirector.UpdateLocomotionAnimation` derives `RCL_IsMoving` from entity position delta. A threshold like `(0.15 * deltaTime)^2` scales with frame time and can miss small or slow displacements; `0.0001f` (squared distance 0.01) is still too coarse for slow units. Use `1e-6f` so any measurable displacement activates locomotion. As a fallback, also check sampled velocity (`_lastVelocity.sqrMagnitude > 1e-6f`) so units whose transform reports quantized or tiny per-frame deltas still drive their walk cycle.
- **Turret aiming must pause while the unit is moving.** If `UpdateTurretAiming` runs during locomotion, the turret tracks targets while walking and produces a "shooting while moving" visual artifact. Guard aim updates with the same moving-state flag used for `RCL_IsMoving` so the turret only tracks when the unit is stationary.
- Keep socket creation idempotent: search recursively for existing canonical sockets before creating any new transform; duplicate same-role sockets make `UniqueSocketRoleBinding` ambiguous.

### Vehicle / Tank Rigging (leg_type = None)

For non-walker vehicles (tanks, rovers, hovercraft) that have a turret but no articulated legs, use `leg_type=None` in `generate_procedural_armature`. This creates a hull-based armature (`Bone_Hull` → `Bone_Pelvis` → `Bone_TurretYaw` → `Bone_TurretPitch` → `Bone_BarrelRecoil`) without leg chains or IK.

- **In-place invariant still applies**: `Bone_Root` must have zero translation. For vehicles, the `Walk` action animates `Bone_Hull`, not `Bone_Root`. Root-motion validation must check that any `Bone_Root` location curves present are all zero (not require exactly 3 curves).
- **Action clips**: `generate_procedural_action_clips` with `leg_type=None` produces Idle, Walk (hull bob), Drive, FireRecoil, and Death clips. The `Walk` clip animates `Bone_Hull` location/rotation.
- **Rigid weighting**: The same rigid 1.0-per-mesh rule applies. Meshes containing "Barrel", "Muzzle", "Bore", "Recoil", "Nozzle" bind to `Bone_BarrelRecoil`; "Mantlet", "Cradle", "Howitzer", "TurretPitch" bind to `Bone_TurretPitch`; "Turret", "Bustle", "Visor", "Radar" bind to `Bone_TurretYaw`; everything else binds to `Bone_Hull`.
- **Socket preservation**: Vehicle sources already have canonical sockets (`Socket_MuzzlePrimary`, `Socket_TurretPivot`, etc.) as empties. Reuse them; do not create duplicates.
- **Unity scaffolder changes needed**:
  1. Check which model path each unit currently uses — some units in a pack may already point to `*.fbx` (static, pre-rig) while others use `*_Atlas.fbx`. Change all to the rigged `*.fbx`.
  2. Add `ConfigureRiggedWalkerModel(modelPath)` call (reuses the same Generic-avatar importer config).
  3. In the visual creation method: destroy imported Animators, call `SetupAnimatorIfAvailable`, and disable root motion on the final Animator.
- **No foot contact sockets** for wheeled/tracked vehicles unless they have articulated legs. The `EnsureCanonicalWalkerSockets` call is walker-specific and should be skipped for vehicles.

### Debugging hexapod deformation (lessons from repeated failed fixes)
- **Edit-mode vs object-mode bone coordinates differ.** `bone.tail.z` in object mode (via `arm.data.bones[name].tail`) is NOT the same as `edit_bone.tail.z` in edit mode. Always verify bone orientation in edit mode; object-mode tail values can be misleading and cause a "fix" that is actually a no-op.
- **Middle leg bones (ML/MR) are authored in the source blend, not generated by `RogueModelKit.py`.** There is no code that creates `Bone_*_ML/MR`; they exist only in `content/source-art/arachnid-hexapod.blend`. A normalizer function in `RogueModelKit.py` that flips tail Z is a no-op if the bones are already correctly oriented in edit mode. To change their geometry, edit the blend directly or add a function that creates them from scratch with the correct rest matrices.
- **Do not claim a rig fix is resolved without in-game visual validation.** Static tests, Blender contract tests, Unity batchmode builds, and bundle imports all pass while the model remains deformed in-game. Only a screenshot or video of the running game confirms geometry and animation transitions. If runtime smoke crashes or is unavailable, report the limitation explicitly instead of asserting the fix works.
- **Patch safety for `assign_rigid_mesh_weights`.** When patching `RogueModelKit.py`, verify the function signature remains intact (`objects, prefix, armature_obj, leg_type=None`). A misplaced patch can silently drop parameters and break the build with `TypeError: got multiple values for argument`.

When a blend file already has an armature (e.g., from a previous authoring pass) but lacks animation clips, open the existing blend, retrieve the armature by name, and call `generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type=...)` to generate the four standard clips. Validate root motion and rigid weights, then export with `include_armature=True, bake_anim=True`. Create a separate build script with a `RiggedModel` suffix (e.g., `Build-ArachnidHexapodRiggedModel.py`) to avoid overwriting the original geometry-creation script. The exported FBX replaces the static one in the Unity project.

---

## 3. Auditing & Validation Tool

Run the dedicated validator CLI to audit models, prefabs, and compiled AssetBundles:

```bash
# Audit a specific Unity prefab
python tools/validate_mech_rigging.py --prefab unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Prefabs/IroncladWalkerVisual.prefab

# Audit an entire content pack bundle
python tools/validate_mech_rigging.py --pack content/packs/sample-native-expansion

# Scan all prefabs in the authoring project
python tools/validate_mech_rigging.py
```

The script evaluates armature presence, bone hierarchy, canonical sockets, and AnimatorController contracts, outputting an ASCII diagnostic table and non-zero exit codes on critical omissions.
