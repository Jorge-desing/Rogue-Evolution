# Bipedal & Quadrupedal Mech Rigging Guide

This reference defines the bone hierarchy, mechanical joint axes, and vertex weighting conventions for rigging articulated walkers and mechs in Rogue Command.

---

## 1. The Two Rigging Paradigms in Rogue Command

Reverse engineering of Rogue Command 1.0.9 (e.g., native `GatlingWalker` and `FlameWalker` in `resources.assets`) reveals two distinct rigging architectures:

```
+-----------------------------------------------------------------------------------+
|                            RIGGING ARCHITECTURES                                  |
+-----------------------------------------------------------------------------------+
|  1. HIERARCHICAL TRANSFORM RIG (Native)   |  2. SKELETAL ARMATURE RIG (Mecanim)  |
|  - No bones, no Animator component        |  - Blender Armature with Bones       |
|  - Separate static meshes per limb segment|  - SkinnedMeshRenderer or MeshBinding|
|  - Named pivots: RotHor, ROTVERT, LEGS    |  - Generic Mecanim Avatar            |
|  - Zero-code procedural aiming & stepping |  - Driven by RuntimeVisualDirector   |
|  - Ideal for modular hard-surface walkers |  - Ideal for complex organic/IK mechs|
+-----------------------------------------------------------------------------------+
```

---

## 2. Canonical Bone Naming Hierarchy

When authoring a skeletal armature in Blender for export to Unity, use canonical prefixes and lowercase/PascalCase conventions:

### A. Bipedal Walker (Digitigrade / Chicken-Walker & Plantigrade)

```
Bone_Root (Ground origin at Z = 0.0, world orientation)
└── Bone_Pelvis (Hip hub / core pelvis, Z ~ 0.85m - 1.40m)
    ├── Bone_Thigh_L (Hip to knee joint)
    │   └── Bone_Shin_L (Knee to ankle joint)
    │       └── Bone_Foot_L (Ankle to ground toe contact)
    ├── Bone_Thigh_R (Hip to knee joint)
    │   └── Bone_Shin_R (Knee to ankle joint)
    │       └── Bone_Foot_R (Ankle to ground toe contact)
    └── Bone_TorsoYaw / Bone_TurretYaw (Horizontal azimuth traverse)
        ├── Bone_WeaponPitch_L / Bone_TurretPitch (Vertical elevation)
        │   └── Bone_BarrelRecoil_L (Linear backward recoil slide)
        └── Bone_WeaponPitch_R
            └── Bone_BarrelRecoil_R
```

### B. Quadrupedal Heavy Mech (4-Legged Crawler)

```
Bone_Root (Ground origin at Z = 0.0)
└── Bone_Hull / Bone_Pelvis (Chassis body)
    ├── Bone_Thigh_FL (Front-Left hip)
    │   └── Bone_Shin_FL
    │       └── Bone_Foot_FL
    ├── Bone_Thigh_FR (Front-Right hip)
    │   └── Bone_Shin_FR
    │       └── Bone_Foot_FR
    ├── Bone_Thigh_RL (Rear-Left hip)
    │   └── Bone_Shin_RL
    │       └── Bone_Foot_RL
    ├── Bone_Thigh_RR (Rear-Right hip)
    │   └── Bone_Shin_RR
    │       └── Bone_Foot_RR
    └── Bone_TurretYaw
        └── Bone_TurretPitch
            └── Bone_BarrelRecoil
```

---

## 3. Rigid Vertex Weighting (Hard-Surface Invariant)

Unlike organic characters (humans, creatures), **mechanical mechs must never exhibit rubbery bending or deformation on solid armor plates, hydraulic pistons, or heavy chassis castings**.

### The $1.0$ Weight Invariant:
- Every vertex of a rigid mech component (e.g. `ThighArmor_L`, `PistonShaft_R`, `FootToe_Center`) must have a weight of **strictly $1.0$** assigned to its single parent bone.
- Avoid automatic heat weights ("With Automatic Weights") as they spread fractions ($0.3$ to bone A, $0.7$ to bone B) across joint intersections, causing metallic plates to stretch gelatinously.
- In Blender:
  ```python
  # Assign 100% rigid weight to target bone
  vg = mesh_obj.vertex_groups.get(target_bone) or mesh_obj.vertex_groups.new(name=target_bone)
  all_v_indices = list(range(len(mesh_obj.data.vertices)))
  vg.add(all_v_indices, 1.0, "REPLACE")
  ```

---

## 4. Canonical Gameplay Sockets Integration

Gameplay sockets must be empty Transforms parented to the appropriate bone or visual node:

| Socket Name | Parent Bone | Purpose |
|---|---|---|
| `Socket_TurretPivot` | `Bone_Pelvis` / `Bone_Hull` | Azimuth horizontal rotation (base 360°/s) |
| `Socket_BarrelPitch` | `Bone_TurretYaw` | Vertical gun elevation (-10° to +45°) |
| `Socket_MuzzlePrimary` | `Bone_BarrelRecoil` | Projectile spawn and `ShootProjectile` muzzle flash |
| `Socket_DamageSmoke` | `Bone_Hull` / `Bone_Pelvis` | Smoke particles at $\le 50\%$ health |
| `Socket_DamageFire` | `Bone_TurretYaw` / `Bone_Hull` | Fire particles and jammed tilt at $\le 25\%$ health |
| `Socket_FootContact_L` | `Bone_Foot_L` | Left foot ground impact dust & step SFX |
| `Socket_FootContact_R` | `Bone_Foot_R` | Right foot ground impact dust & step SFX |

---

## 5. Blender Armature Generation Snippet

```python
import bpy
from mathutils import Vector

def create_walker_armature(prefix: str, hip_height: float = 1.2, hip_width: float = 0.5) -> bpy.types.Object:
    arm_data = bpy.data.armatures.new(f"{prefix}_ArmatureData")
    arm_obj = bpy.data.objects.new(f"{prefix}_Armature", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj

    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    # 1. Root
    b_root = eb.new("Bone_Root")
    b_root.head = (0.0, 0.0, 0.0)
    b_root.tail = (0.0, 0.0, 0.2)

    # 2. Pelvis
    b_pelvis = eb.new("Bone_Pelvis")
    b_pelvis.head = (0.0, 0.0, 0.2)
    b_pelvis.tail = (0.0, 0.0, hip_height)
    b_pelvis.parent = b_root

    # 3. Legs (Left and Right)
    for side, side_str in ((-1, "L"), (1, "R")):
        hip_pos = Vector((side * hip_width, 0.0, hip_height))
        knee_pos = Vector((side * hip_width * 1.1, -0.25, hip_height * 0.55))
        ankle_pos = Vector((side * hip_width, 0.15, hip_height * 0.20))
        foot_pos = Vector((side * hip_width, 0.25, 0.0))

        b_thigh = eb.new(f"Bone_Thigh_{side_str}")
        b_thigh.head = hip_pos
        b_thigh.tail = knee_pos
        b_thigh.parent = b_pelvis

        b_shin = eb.new(f"Bone_Shin_{side_str}")
        b_shin.head = knee_pos
        b_shin.tail = ankle_pos
        b_shin.parent = b_thigh

        b_foot = eb.new(f"Bone_Foot_{side_str}")
        b_foot.head = ankle_pos
        b_foot.tail = foot_pos
        b_foot.parent = b_shin

    # 4. Turret / Torso
    b_torso = eb.new("Bone_TurretYaw")
    b_torso.head = (0.0, 0.0, hip_height)
    b_torso.tail = (0.0, 0.0, hip_height + 0.4)
    b_torso.parent = b_pelvis

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj
```
