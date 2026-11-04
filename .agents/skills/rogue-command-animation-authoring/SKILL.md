---
name: rogue-command-animation-authoring
description: "Author in-place unit animation for Rogue Command."
metadata:
  hermes:
    tags: [Rogue Command, Unity, animation, Animator, locomotion]
    related_skills: [rogue-command-mech-rigging-authoring, rogue-command-vfx-authoring, rogue-command-3d-model-authoring]
---

# Rogue Command Animation Authoring

Use this skill for Unity Animator/AnimationClip integration and mechanical animation on Rogue Command vehicles and units. Apply animation principles to RTS camera scale, native movement, and the project's verified runtime hooks; do not copy browser/CSS timing rules literally. For walker gait and armature construction, use [Mech Rigging](../rogue-command-mech-rigging-authoring/SKILL.md). For particle effects, use [VFX Authoring](../rogue-command-vfx-authoring/SKILL.md). For mesh, sockets, and FBX work, use [3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md).

## When to use

- `Action_Idle`, `Action_Walk`/`Action_Drive`, `Action_FireRecoil`, or `Action_Death` clips.
- Unity Generic Avatar, AnimatorController, runtime parameters, or transition timing.
- Turret/radar motion, recoil, antenna wobble, factory doors, and damage-state animation.
- Reviewing animation readability, timing, and staging from RTS distance.

## Project contracts

- Rogue Command movement is driven by gameplay (`EntityFlockingMovement`), not clip root motion. Walker locomotion must remain in-place: `Bone_Root` has zero position delta through the clip, and the Unity overlay Animator must have root motion disabled.
- The project builder configures Generic animation and an Avatar from the model, then wires the AnimatorController when the expected clips exist. The standard parameters are `RCL_IsMoving` (Bool), `RCL_Fire` (Trigger), and `RCL_Death` (Trigger).
- `RuntimeVisualDirector` sets `RCL_IsMoving` from observed entity position changes; it does not drive a speed blend tree. It triggers `RCL_Fire` on detected weapon shots and triggers `RCL_Death` once when the death state is recognized, when those parameters/clips are available.
- Current builder transitions use 0.12 s for movement-state changes and 0.05 s for Any State fire/death triggers. Fire returns after its exit time; death is not an ordinary looping locomotion transition. Treat these as implementation values, not universal artistic limits.
- The director's current mechanical feedback is separate from the Animator: radar sweep is configured at 30 RPM (180°/s), turret traverse at 120°/s, recoil is set to 0.22 m on shot with a `Mathf.Lerp` recovery, and the procedural muzzle burst defaults to 0.07 s. Re-read source before relying on these values after a change.

## Attack clip contract: body, weapon, and state graph

A `FireRecoil` clip must never animate only `Bone_BarrelRecoil` while the chassis/pelvis remains static. Author all four layers as one readable action: (1) anticipation/brace on `Bone_Hull` or `Bone_Pelvis`, (2) ballistic barrel recoil and body compression at impact, (3) turret-pitch or mount follow-through, and (4) damped recovery to the neutral pose. Keep the body translation local and small; never key `Bone_Root` or introduce gameplay root motion. Rigid armor remains rigid; use only mechanically plausible suspension/recoil travel.

The generated controller must contain one canonical state per imported action, prefer armature-qualified `Action_*` clips over legacy short names, default to `Action_Idle`, route `RCL_IsMoving` to `Action_Walk`/`Action_Drive`, and return `FireRecoil` to locomotion when `RCL_IsMoving` is still true before falling back to idle. Rebuild the state machine instead of accumulating stale `Idle`/`Walk`/`Drive`/`Death` duplicates. Remove imported FBX `Animator` components from the model instance so the visual root owns exactly one non-root-motion Animator.

When auditing every pack, inspect actual FireRecoil curves for body and weapon channels, not merely clip names or controller references. A valid AssetBundle is local authoring evidence; only a Steam run showing the Animator state and changing body/weapon transforms proves effective in-game motion.

Relevant implementations:

- `src/RogueCommandExpansionLoader/Runtime/Visual/RuntimeVisualDirector.cs`
- `unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs`
- `unity/RogueCommandAssetAuthoring/Assets/Editor/RogueCommandVisualSampleScaffolder.cs`

## Animation principles adapted to RTS units

Use these as review lenses, not pass/fail formulas:

1. **Squash/stretch, used sparingly:** Reserve deformation for flexible hoses, suspension, recoil dampers, or an intentionally stylized hit; keep hard armor and rigid mechanical plates undeformed.
2. **Anticipation:** A brief brace, recoil wind-up, leg load, or turret alignment can make an action readable before its main motion.
3. **Staging:** Keep one prominent moving focal point at a time. Preserve weapon, heading, damage cue, and silhouette readability at RTS zoom.
4. **Pose-to-pose vs. continuous motion:** Prefer deterministic keyed poses for mechanical action clips and damage states; use continuous updates for truly procedural tracking/sweeps. Keep one owner per transform to avoid Animator/runtime fighting.
5. **Follow-through and overlap:** Let a barrel mount, hose, sensor, or antenna settle after the main chassis/weapon when that secondary motion is mechanically plausible.
6. **Slow in and slow out:** Shape starts/stops for mass and control. Do not ease a system that is intended to rotate at constant angular velocity.
7. **Arcs:** Turrets rotate around their authored pivot, barrels pitch about the correct joint, and recoil follows the weapon axis. Check axis inversion, pivot drift, and clipping.
8. **Secondary action:** Keep antenna sway, radiator flutter, shell ejection, and debris subordinate to the principal action; disable or reduce them if they obscure combat feedback.
9. **Timing:** Convey mass and role through acceleration, hold, impact, and recovery. A heavy cannon, autocannon, radar sweep, gait, and death sequence need different durations; browser 300 ms rules do not apply.
10. **Exaggeration, restrained:** Enlarge a short impact, recoil, or silhouette change only enough to read at game zoom; do not distort armor proportions or imply unsupported gameplay effects.
11. **Solid form:** Maintain coherent 3D mass, rigid plate weights, mechanically plausible pivots, and consistent scale; inspect both rendered frames and the actual rig hierarchy.
12. **Appeal and clarity:** Preserve faction palette, unit role, and distinctive silhouette. Animation should make intent obvious without competing motion or visual clutter.

There is no blanket 300 ms limit for game animation. A short muzzle flash, a full gait cycle, and a death sequence have different jobs and durations. Keep transitions only as long as needed for clear state changes and avoid excessive overlap that muddies combat feedback.

## Stripping non-walk animations from a rigged model

When the user requests that a unit should only have walking leg animations (no idle breathing, fire recoil, or death), the procedure is:

1. **In the Blender build script**, after loading the existing armature from the blend file, delete all actions except the Walk action. **Handle aliases**: if `Drive` is an alias of `Walk` (same action object), pop the key without removing the action, or check object identity before removal to avoid `ReferenceError: StructRNA of type Action has been removed`:
   ```python
   walk_act = actions.get("Walk")
   for name in ("Idle", "FireRecoil", "Death"):
       act = actions.pop(name, None)
       if act is not None and act is not walk_act:
           bpy.data.actions.remove(act)
   actions.pop("Drive", None)  # alias, do not remove the shared action
   ```
2. **Strip hull movement from Walk** so ONLY legs animate. Remove all fcurves whose data_path contains `Bone_Hull`:
   ```python
   if walk_act is not None:
       for fc in list(walk_act.fcurves):
           if 'Bone_Hull' in fc.data_path:
               walk_act.fcurves.remove(fc)
   ```
3. **Remove NLA tracks** for the deleted actions from the armature:
   ```python
   if armature.animation_data:
       for track in list(armature.animation_data.nla_tracks):
           if "Walk" not in (track.name or ""):
               armature.animation_data.nla_tracks.remove(track)
   ```
4. **Regenerate the blend and FBX** with `include_armature=True, bake_anim=True, bake_space_transform=False`. Unity performs the axis conversion at import; enabling Blender's space bake can corrupt bind poses and collapse the skinned mesh.
5. **Update the Unity validator** (`RogueCommandVisualSampleScaffolder.cs`): if the validator requires `Action_Idle`, `Action_FireRecoil`, or `Action_Death` clips, add a `requireIdleFireDeath` parameter (default `true`) and pass `false` for the unit being stripped. The validator's `missingActions` check must be conditional on this flag.
6. **Rebuild the Unity bundle** and deploy.
7. **Note**: The AnimatorController may still reference Idle/FireRecoil/Death states, but since those clips no longer exist in the FBX, those states will play the bind pose (static). This is acceptable when the user only wants walking animations. To fully remove those states, edit the `.controller` file manually.
8. **Scope discipline**: When the user says "remove animations from the hexapod", they mean THAT unit only. Do not remove animations from other units or alter shared animation generation functions. The user's frustration signal "Estamos hablando de las patas" means: legs only, don't touch other animations.
9. **Label the result**: static inspection (Blender/Unity build) vs. in-game runtime evidence. A successful build does not prove the animation plays correctly in-game.

## FBX import settings for rigged models

When a rigged model does not render at all (invisible mesh, zero-extent SMR bounds, or `SetupAnimatorIfAvailable` fails), check the FBX `.meta` file:

- `animationType` must be `3` (Generic), not `2` (Humanoid). A Humanoid avatar on a Generic rig causes the model to not render.
- `avatarSetup` must be `2` (CreateFromThisModel), not `1` (NoAvatar). Without a valid avatar, `SetupAnimatorIfAvailable` cannot configure the Animator.
- The build validator (`ValidateWalkerModelSource`) should set these via `ModelImporter`, but the `.meta` file on disk may retain stale values. If the model is invisible after a successful build, manually edit the `.meta` file and rebuild.

## FBX export settings for rigged models

- **`bake_space_transform` must be `False`** for rigged FBX exports. With `True`, Blender bakes the axis conversion (Z-up → Y-up) into vertex/bone transforms, corrupting bind-pose matrices — the SkinnedMeshRenderer reports zero `localBounds` and the mesh collapses in-game. Unity handles axis conversion at import time when this is `False`.
- **Mesh batching can corrupt vertex weights.** If `batch_and_join_by_material` joins meshes and loses rigid 1.0 weight bindings, export the original unbatched meshes directly: `rmk.export_unity_fbx(FBX_PATH, list(meshes) + [armature], ...)`. Correctness takes priority over draw-call optimization for rigged units.

1. **Read project context.** Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, `unity/RogueCommandAssetAuthoring/README.md`, and the relevant rigging/model references. Confirm the worktree and preserve unrelated changes.
2. **Trace the driver.** Search the builder and runtime director for parameter names, clip lookup, transition setup, thresholds, delta-time use, and root-motion settings. Do not invent an Animator parameter or assume every prefab has the director attached.
3. **Author or edit the source.** Work from the existing `.blend` and deterministic model builder where possible. Keep bone names, pivots, canonical sockets, rigid armor weights, and root transform aligned to the project contract. Put previews/diagnostics in ignored `artifacts/`.
4. **Validate clips and rig.** Confirm the Generic Avatar is valid, expected clips are present, the root-motion invariant holds, mechanical weights are rigid, looping clips close cleanly, and feet do not penetrate the ground. Use `tools/validate_mech_rigging.py` for its documented structural checks; do not treat it as a full motion-quality or live-controller test.
5. **Build the prefab/bundle.** Use the Unity authoring builder in `unity/RogueCommandAssetAuthoring/` with Unity 2021.3.45f2. Check `applyRootMotion`, controller parameters, state transitions, clip references, missing scripts, renderer/material links, and canonical socket uniqueness after the build.
6. **Review animation quality.** Inspect representative frames and loops at close and RTS scales. Check anticipation, contact, silhouette, timing, direction, recovery, and whether animation conflicts with native transform updates. Run the rigging, VFX, and bundle-reference checks relevant to the change.
7. **Prove runtime claims separately.** A valid clip, prefab, or AssetBundle is local authoring evidence. To claim that motion plays correctly, observe the intended state transition and motion in Rogue Command launched through Steam using the runtime-validation procedure.

## Validation commands

Run from the repository root through `terminal`, using the project Python environment:

```text
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_mech_rigging.py --pack content/packs/sample-native-expansion --json")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_vfx_assets.py --pack content/packs/sample-native-expansion --json")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_content_bundle_references.py content/packs/sample-native-expansion")
```

Inspect `--help` for current options. Use [Steam Runtime Validation](../rogue-command-steam-runtime-validation/SKILL.md) for any in-game acceptance run.

## Bug triage: animations vs orientation vs card image

When a user reports multiple visual symptoms at once—e.g. “walks backward / no animations / wrong photo”—triage them as separate issues before changing code:

- **Missing or frozen animation** is usually an offline fixable defect: missing clip imports, wrong clip-name priority (`Action_*` before plain `Walk`/`Drive`), disabled looping, competing `RCL_IsMoving` transitions, missing root-motion disable, duplicate imported `Animator` components, or a movement-detection threshold so high that small displacements never set `RCL_IsMoving`. These can be diagnosed and repaired in the Unity authoring project and verified with rigging/bundle tests.
- **Wrong facing direction while walking cannot be proven offline.** Static evidence—Blender front axis, FBX `FrontAxis`, prefab transform yaw, generated sockets, or controller/clip inspection—does not establish forward direction during gameplay movement. Treat this as an in-game observation requirement, not a prefab defect, until a live match shows the unit moving opposite to its authored front.
- **Wrong card image** is usually a manifest/asset mismatch: compare `cardImageAssetName` across units. If one unit is `null` while others reference bundle Sprites, the card fallback is the most likely cause.

- **Deformed model with looping idle and no visible walk can be a rig-weight/coverage defect, not only an Animator defect.** Verify every leg mesh is rigidly bound to its own bone, middle legs are keyed in `Action_Walk`, and the build script actually called `assign_rigid_mesh_weights` before export. A passing `RCL_IsMoving` transition cannot animate bones that were never keyed or were bound to `Bone_Hull`/the wrong leg.

Do not rotate a model or change yaw correction based only on static evidence. Require in-game observation or a confirmed authored-axis mismatch before applying an orientation fix.

## Pitfalls and evidence limits

- **Mixed generic and rigged states in an AnimatorController.** A controller can contain both generic states (`Idle`, `Walk`, `Drive`) and rigged states (`<Armature>|..._Action_*`). If a rigged Idle transition points at the generic `Walk`/`Drive` state instead of the rigged action state, the unit enters a state with no deforming clip and appears frozen/non-animated. When editing or auditing a controller, verify every rigged Idle→movement transition targets the rigged action state, and that the rigged movement state has a conditioned return to rigged Idle on `!RCL_IsMoving`. Otherwise the unit enters movement once and never leaves it.
- **Animator discovery must prefer the modded visual clone.** `GetComponentInChildren<Animator>` on the entity root can resolve a *native* Animator when the native template has one, instead of the modded clone's Animator. In `RuntimeVisualDirector.CacheAnimatorParameters`, search for a child whose name starts with `RCL_Visual_` and use its `GetComponentInChildren<Animator>` first, falling back to the root search only when no modded clone Animator exists.
- **Generated sockets cannot prove orientation.** When empties are not exported, `VehiclePrefabBuilder` creates sockets from renderer bounds, so muzzle lands at max-Z and rear at min-Z by construction; the yaw-correction heuristic can never detect a reversed model from those sockets. Validate orientation against authored source (Blender front axis, FBX `FrontAxis`, prefab transform) and in-game observation; never treat generated sockets as evidence of correct facing.
- **Movement-detection threshold must use a small fixed squared value.** `RuntimeVisualDirector.UpdateLocomotionAnimation` computes `RCL_IsMoving` from position delta. A threshold like `(0.15 * deltaTime)^2` scales with frame time and can miss small or slow displacements, leaving the unit stuck in Idle even though the gameplay mover is active. `0.0001f` (squared distance 0.01) is still too coarse for slow units. Use `1e-6f` so any measurable displacement activates locomotion.
- **Clip-name ambiguity on rigged imports.** An FBX can import both `Action_Walk` (armature-prefixed, e.g. `IroncladWalker_Armature|IroncladWalker_Action_Walk`) and a plain `Walk` clip. `FindAnimationClip(clips, "Walk")` returns the first name containing "Walk", which may be the wrong (non-deforming or legacy) clip. Prefer `Action_*` names first (`Action_Walk`, `Action_Idle`, `Action_FireRecoil`, `Action_Death`) before falling back to plain names; otherwise Idle→Walk can end up targeting a Death-named or idle clip and the unit appears frozen.
- **FireRecoil must return to locomotion.** `FireRecoil` exit transitions must include a conditioned return to the movement state (`Action_Walk`/`Action_Drive`) when `RCL_IsMoving` is still true, with `hasExitTime = true`, in addition to the unconditional exit to `Idle`. Otherwise a shot interrupts walking and leaves the unit stuck in a recoil/idle pose.
- **Turret aiming must pause while moving.** If the runtime director updates turret yaw/pitch during locomotion, the weapon tracks targets while the unit walks, producing a "shooting while moving" visual artifact. Guard `UpdateTurretAiming` with the same moving-state flag used for `RCL_IsMoving`; only track when the unit is stationary.
- **Duplicate Animator from imported model.** A rigged FBX may carry its own `Animator`. Destroy every imported `Animator` on the model instance before `SetupAnimatorIfAvailable`; otherwise the prefab/bundle keeps two Animators and the rigging contract (exactly one configured Animator on the visual root) fails.
- Native visual overlays retain native gameplay hierarchies but do not retarget native animation rigs to a replacement mesh automatically.
- A serialized Animator/Avatar/controller reference does not prove the model deforms or transitions correctly in the target player.
- Root-motion-disabled Editor setup does not by itself prove an in-place clip; inspect the clip's root curves and test the animated model.
- The project validators check limited structural contracts. A passing result does not establish visual quality, stable frame-rate behavior, combat synchronization, or runtime rendering.
- When a runtime visual complaint includes suspected backward movement, separate the animation fix from the orientation check: animation setup can be validated offline, but direction of travel requires a live observation.
- Keep reflected-signature claims behind the complete game identity check: Rogue Command version, Unity version, `Assembly-CSharp.dll` SHA-256, and MVID.
- Label results as **static inspection**, **local build/validation**, or **in-game runtime evidence**; never promote one tier into another.

## Completion report

List edited source, clips, controllers, prefabs, and bundles; state tool/engine versions and commands; report observed diagnostics; and separate static, local Unity, and live-game evidence. Identify unverified state transitions or motion behavior explicitly.
