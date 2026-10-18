# Unity Avatar and Animator Contracts

This reference documents the Unity Model Importer settings, Avatar configuration, and `AnimatorController` parameter contracts used by Rogue Command's `RuntimeVisualDirector`.

---

## 1. Unity Model Importer Configuration

When importing a rigged walker FBX into Unity 2021.3.45f2:

### Rig Tab Settings:
- **Animation Type:** `Generic` (Do **NOT** use `Humanoid`).
  > [!IMPORTANT]
  > Mechs feature reverse-knee digitigrade joints, 3-toed feet, rotating turrets, and mechanical hardpoints that the Unity Humanoid retargeter distorts, clamps, or strips entirely. Generic retains the exact bone hierarchy and orientation.
- **Avatar Definition:** `Create From This Model`.
- **Root Node:** `Bone_Root` (or `None`).

### Animation Tab Settings:
For each imported clip (`Action_Idle`, `Action_Walk`, `Action_FireRecoil`, `Action_Death`):
- **Action_Idle:**
  - `Loop Time`: **Checked**
  - `Loop Pose`: **Checked**
- **Action_Walk (or Action_Drive):**
  - `Loop Time`: **Checked**
  - `Loop Pose`: **Checked**
  - `Root Transform Rotation` -> `Bake Into Pose`: **Checked**
  - `Root Transform Position (Y)` -> `Bake Into Pose`: **Checked**
  - `Root Transform Position (XZ)` -> `Bake Into Pose`: **Checked**
- **Action_FireRecoil:**
  - `Loop Time`: **Unchecked**
- **Action_Death:**
  - `Loop Time`: **Unchecked**
  - `Clamp Forever`: **Checked**

---

## 2. Animator Controller State Machine

The Unity `AnimatorController` must be structured with the following states and transitions:

```
               +-------------------+
               |     Any State     |
               +-------------------+
                 /               \
       RCL_Fire /                 \ RCL_Death
               v                   v
      +-----------------+   +---------------+
      |Action_FireRecoil|   | Action_Death  | (Terminal State)
      +-----------------+   +---------------+
              |
     (Has Exit Time)
              v
       +-------------+    RCL_IsMoving = true     +-------------+
       | Action_Idle | ------------------------>  | Action_Walk |
       |  (Default)  | <------------------------  |             |
       +-------------+    RCL_IsMoving = false    +-------------+
```

---

## 3. Parameter Contract with RuntimeVisualDirector

The expansion loader's `RuntimeVisualDirector` autonomously discovers and drives these three parameters:

| Parameter Name | Type | Set Condition in RuntimeVisualDirector |
|---|---|---|
| `RCL_IsMoving` | **Bool** | `true` when entity displacement $(\Delta\text{pos}/\Delta t)^2 > (0.15\text{m/s})^2$; `false` when stationary. |
| `RCL_Fire` | **Trigger** | Fired automatically when `ShootProjectile` executes a shot or `TriggerWeaponFeedback()` is invoked. |
| `RCL_Death` | **Trigger** | Fired once when `EntityController.IsDestroyed` becomes `true`. Cancels `RCL_IsMoving`. |

---

## 4. Automated Builder Integration (`VehiclePrefabBuilder.cs`)

The project's pinned Unity prefab builder (`Assets/Editor/VehiclePrefabBuilder.cs`) automatically configures the Animator and AnimatorController:
1. Detects `Bone_*` or `Armature` objects in the imported FBX.
2. Creates `<UnitName>_Controller.controller` in `Assets/RogueCommandExpansionSample/Animations/`.
3. Populates states with imported clips matching `Action_Idle`, `Action_Walk` / `Action_Drive`, `Action_FireRecoil`, and `Action_Death`.
4. Hooks parameter transitions with standard blend times ($0.15\text{s}$ walk blend, $0.20\text{s}$ stop blend).
