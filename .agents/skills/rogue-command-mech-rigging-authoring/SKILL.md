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
