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


## 5. Builder Pitfalls

When a rigged FBX imports both canonical clips (`Action_Walk`) and armature-prefixed duplicates (`<Unit>_Armature|<Unit>_Action_Walk`), `ConfigureAnimationController` creates an `AnimatorState` for **every** clip and `FindAnimationClip` returns the **first** match. Consequences and fixes:

- **Clip selection ambiguity.** `FindAnimationClip(clips, "Walk")` can resolve to a legacy/empty clip instead of the skinned `Action_Walk` clip. Always prefer the `Action_*` names first (`Action_Idle`, `Action_Walk`/`Action_Drive`, `Action_FireRecoil`, `Action_Death`) and fall back to the simple names.
- **Transitions target the wrong state.** If `Idle` is wired to a state whose clip is actually `Action_Death` (or any non-walk clip), the unit's feet never animate even though `RCL_IsMoving` toggles correctly. Verify the serialized controller's `m_DstState` fileID resolves to the intended `Action_*` state, not a duplicate.
- **FireRecoil must return to walk when still moving.** An unconditional exit from `FireRecoil` to `Idle` leaves the firing animation playing (or the unit stuck) while moving. Add a second exit with `hasExitTime = true`, `exitTime = 1`, conditioned on `RCL_IsMoving = If`, targeting `Action_Walk`, ahead of the unconditional `Idle` fallback.
- **Duplicate Animators.** A rigged FBX can carry its own `Animator`. Destroy every imported `Animator` on the model instance (`GetComponentsInChildren<Animator>(true)`) before `SetupAnimatorIfAvailable`, otherwise the prefab/bundle retains two Animators that fight over the same rig.
- **YAML empty names.** Unity serializes empty transition names as `m_Name: ` with a trailing space. Preserve that when hand-editing controller YAML.
