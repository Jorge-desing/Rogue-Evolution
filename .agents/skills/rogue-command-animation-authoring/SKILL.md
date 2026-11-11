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
- `RuntimeVisualDirector` sets `RCL_IsMoving` from observed entity position changes and sampled velocity; it does not drive a speed blend tree. `LocomotionAnimationPolicy` holds the moving state for 0.12 scaled seconds after a positive sample so intermittent native movement ticks do not flash Idle between updates. A zero/invalid scaled delta (including pause), reuse, death, and stun clear the hold. The same moving state gates turret aim. The director triggers `RCL_Fire` on detected weapon shots and triggers `RCL_Death` once when the death state is recognized, when those parameters/clips are available.
- Current builder transitions use 0.12 s for movement-state changes and 0.05 s for Any State fire/death triggers. Fire returns after its exit time; death is not an ordinary looping locomotion transition. Treat these as implementation values, not universal artistic limits.
- The director's current mechanical feedback is separate from the Animator: radar sweep is configured at 30 RPM (180°/s), turret traverse at 120°/s, recoil is set to 0.22 m on shot with a `Mathf.Lerp` recovery, and the procedural muzzle burst defaults to 0.07 s. Re-read source before relying on these values after a change.
- `tools/RogueModelKit.py::generate_procedural_action_clips` is authoritative for keyframe shape; do not rely on older prose summaries alone. Current contract:
  - `Action_Idle`: neutral at f1, slow-in/out shoulders at f8/f22 (`-0.004` m non-hover / `-0.005` m hover, `0.20°`), shallow peak at f15 (`-0.010` m non-hover / `-0.014` m hover, `0.55°`), close at f30. Never key `Bone_Root`.
  - `Action_FireRecoil`: role-specific ends (light/medium f15, sam/walker f18, heavy f21); monotone phase map at f1 neutral, f3 anticipation, f5 impact, f7 follow-through, f11 recovery, f15 neutral/hold. Never key `Bone_Root`.
  - `Action_Death`: default end f35, verified override f45 for Bastion; early settle at f8 (`12%` collapse), half collapse at f16, full collapse by f28 and hold to end. Current vehicle collapse is `-0.22` m, biped `-0.55` m, quad `-0.28` m, with turret pitch droop `-34°` at full collapse.
  - `Action_Walk` and `Action_Drive` remain `40` frames and in-place; wheeled/tracked vehicles intentionally use `Action_Drive`.

## Attack clip contract: body, weapon, and state graph

A `FireRecoil` clip must never animate only `Bone_BarrelRecoil` while the chassis/pelvis remains static. Author all four layers as one readable action: (1) anticipation/brace on `Bone_Hull` or `Bone_Pelvis`, (2) ballistic barrel recoil and body compression at impact, (3) turret-pitch or mount follow-through, and (4) damped recovery to the neutral pose. Keep the body translation local and small; never key `Bone_Root` or introduce gameplay root motion. Rigid armor remains rigid; use only mechanically plausible suspension/recoil travel.

The generated controller must contain one canonical state per imported action, prefer armature-qualified `Action_*` clips over legacy short names, default to `Action_Idle`, route `RCL_IsMoving` to `Action_Walk`/`Action_Drive`, and return `FireRecoil` to locomotion when `RCL_IsMoving` is still true before falling back to idle. Rebuild the state machine instead of accumulating stale `Idle`/`Walk`/`Drive`/`Death` duplicates. Remove imported FBX `Animator` components from the model instance so the visual root owns exactly one non-root-motion Animator.

When auditing every pack, inspect actual FireRecoil curves for body and weapon channels, not merely clip names or controller references. A valid AssetBundle is local authoring evidence; only a Steam run showing the Animator state and changing body/weapon transforms proves effective in-game motion.

Relevant implementations:

- `tools/RogueModelKit.py`
- `tests/python/rigging/test_tactical_animation_hold_and_easing.py`
- `tests/python/rigging/test_tactical_animation_improvements.py`
- `tests/python/rigging/test_attack_clip_authoring_contract.py`
- `src/RogueCommandExpansionLoader/Runtime/Visual/RuntimeVisualDirector.cs`
- `unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs`
- `unity/RogueCommandAssetAuthoring/Assets/Editor/RogueCommandVisualSampleScaffolder.cs`

## Motion review contract: 12 principles for mechanical RTS units

This section is the Rogue Command adaptation of `/12-principles-of-animation`. Use the principles as review lenses and annotate the actual clip/state, not as web-UI pass/fail formulas. The source skill's 300 ms, 50 ms stagger, and generic `ease-out` rules do not apply to game actions; derive timing from mass, role, frame rate, gameplay hooks, and RTS camera scale.

| Principle | Mechanical audit | Evidence to record |
|---|---|---|
| **Squash/stretch** | Deform only compliant parts (suspension, hoses, recoil dampers); rigid armor keeps its volume and rigid weights. | Animated transforms and representative close/RTS frames. |
| **Anticipation** | Brace the hull/pelvis, load the legs, or align the turret before impact; anticipation must precede the weapon impulse. | Frame numbers and body/weapon curve values. |
| **Staging** | Keep one focal event—muzzle, gait contact, damage, or death—dominant; do not let radar, antenna, and recoil compete. | Silhouette/readability check at both camera scales. |
| **Pose-to-pose / continuous motion** | Key deterministic actions in Blender/FBX; reserve runtime updates for tracking, sweeps, and native movement. One owner writes each transform. | Binding ownership table: Animator vs. `RuntimeVisualDirector` vs. native gameplay. |
| **Follow-through / overlap** | Let mount, barrel, sensor, cable, or antenna settle after the primary body impulse, with plausible hierarchy order. | Phase order and secondary curve offsets. |
| **Slow in / slow out** | Use shaped curves for starting, braking, brace, and recovery; preserve constant angular velocity for radar/turret sweeps driven by RPM. | F-curve tangents or runtime angular-velocity calculation. |
| **Arcs** | Rotate around authored pivots, pitch around the barrel joint, and recoil along the weapon axis; reject pivot drift, axis inversion, and clipping. | Pivot coordinates, socket alignment, and frame review. |
| **Secondary action** | Keep antenna sway, radiator flutter, shell ejection, dust, and audio cues subordinate to the main intent. | Visibility/contrast review and attachment ownership. |
| **Timing** | Separate anticipation, impact, hold, follow-through, and recovery; heavy cannons, autocannons, gaits, radar, and death need role-specific durations. | Clip frame rate, phase frame map, and transition durations. |
| **Exaggeration** | Enlarge only the smallest local displacement/rotation needed to read at game zoom; never fake unsupported gameplay or deform the silhouette. | Maximum local displacement/angle and bounds comparison. |
| **Solid form** | Preserve mass, scale, rigid plate weights, contact with ground, colliders, bounds, sockets, and Generic Avatar integrity. | Rig/weight report, bounds, foot-contact and collider checks. |
| **Appeal / clarity** | Make faction, role, heading, weapon intent, and damage state legible without visual clutter or ambiguous motion. | Static thumbnail strip plus runtime observation when available. |

### Required review pass

For each authored action, mark `neutral → anticipation → contact/impact → hold → follow-through → recovery`. Check the body chain and the weapon chain separately, then check their phase ordering together. A clip name, controller state, barrel curve, or successful AssetBundle import is not sufficient evidence. For `Action_Walk`, inspect contact, passing, lift, and weight-transfer poses; for `Action_FireRecoil`, require body plus weapon motion; for `Action_Death`, verify a readable loss of support without root-motion drift.

The review result must classify each finding as **static inspection**, **local build/validation**, or **in-game Steam runtime**. Do not promote a Manim preview, Unity import, or bundle audit into proof that the target game deforms or transitions correctly.

## Manim preview and communication workflow

Use `/manim-video` as an optional explanatory-preview method, not as a replacement for Blender, FBX, Unity, or Steam validation. A preview should teach the motion phases with abstract geometry: hull/pelvis, turret/mount, barrel, ground contact, phase labels, and a timeline. Never copy proprietary game meshes, extracted assets, credentials, or installation data into a preview.

- **Narrative arc:** begin with the neutral pose, reveal anticipation, isolate impact/recoil, show follow-through, then hold on recovery and the invariant (`Bone_Root` remains fixed). Geometry comes before labels; labels explain a visible relationship rather than substitute for it.
- **Visual hierarchy:** primary body/weapon motion at full opacity, timing guides and axes around `0.15`, annotations around `0.4`, and only one highlighted focal event at a time. Leave a deliberate `wait` after each phase and a longer pause after the key reveal.
- **Artifact hygiene:** keep `plan.md`, `script.py`, rendered frames, and video only under ignored `artifacts/animation-previews/<slug>/`. The source `.blend`, FBX, prefab, controller, and bundle remain authoritative; a Manim script must not write into `Assets/` or replace them.
- **Draft first:** if Manim CE is available, render with `-ql` first and inspect a still; render `-qh` only when a final explanatory video is requested. If it is unavailable, keep the plan and abstract storyboard as static evidence—do not install dependencies or fabricate a render.
- **Rogue Command scene set:** for a substantial review use `FireRecoilCurveAuditPreview`, `FireRecoilPrinciplesComparisonPreview`, `WalkContactMatrixPreview`, `AnimatorContractPreview`, and `EvidenceTierPreview`; bind them to the source frame map, canonical bones, Animator parameters, and evidence boundary documented in `references/animation-review-and-manim.md` rather than producing a generic motion demo.

Canonical optional commands, run through `terminal` from the repository root:

```text
terminal(command="manim -ql artifacts/animation-previews/<slug>/script.py FireRecoilPreview", timeout=300)
terminal(command="manim -ql --format=png -s artifacts/animation-previews/<slug>/script.py FireRecoilPreview", timeout=300)
terminal(command="ffmpeg -y -f concat -safe 0 -i artifacts/animation-previews/<slug>/concat.txt -c copy artifacts/animation-previews/<slug>/final.mp4", timeout=300)
```

Use the [animation review and Manim reference](references/animation-review-and-manim.md) for the storyboard template, abstract mechanical scene contract, and post-render checklist. The preview is communication evidence only; use the Unity/bundle commands below and the Steam runtime skill for implementation evidence.

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
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe -B -m unittest tests.python.rigging.test_tactical_animation_hold_and_easing tests.python.rigging.test_tactical_animation_improvements tests.python.rigging.test_attack_clip_authoring_contract tests.python.bundles.test_mech_rigging_bundle tests.python.rigging.test_tactical_walker_rigging_contract")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_content_bundle_references.py content/packs/tactical-operations")
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

- **Verify before editing.** Animation improvement requests often describe work that is already present in the current HEAD; run `test_tactical_animation_hold_and_easing`, `test_tactical_animation_improvements`, `test_attack_clip_authoring_contract`, `test_mech_rigging_bundle`, and `test_tactical_walker_rigging_contract`, and inspect recent animation commits before changing clips, controllers, or builders. Do not duplicate, restore, or strip animations that already satisfy the verified contract.
- **Do not flatten Idle or Death shaping.** Generated `Action_Idle` needs f8/f22 shoulder keys before the f15 peak, and generated `Action_Death` needs the f8 `12%` early settle before the f16 half collapse. A three-key Idle or a Death that jumps directly to half/full collapse loses the slow in/out envelope and should be treated as an animation regression, not as a simpler equivalent.
- **Mixed generic and rigged states in an AnimatorController.** A controller can contain both generic states (`Idle`, `Walk`, `Drive`) and rigged states (`<Armature>|..._Action_*`). If a rigged Idle transition points at the generic `Walk`/`Drive` state instead of the rigged action state, the unit enters a state with no deforming clip and appears frozen/non-animated. When editing or auditing a controller, verify every rigged Idle→movement transition targets the rigged action state, and that the rigged movement state has a conditioned return to rigged Idle on `!RCL_IsMoving`. Otherwise the unit enters movement once and never leaves it.
- **Animator discovery must prefer the modded visual clone.** `GetComponentInChildren<Animator>` on the entity root can resolve a *native* Animator when the native template has one, instead of the modded clone's Animator. In `RuntimeVisualDirector.CacheAnimatorParameters`, search for a child whose name starts with `RCL_Visual_` and use its `GetComponentInChildren<Animator>` first, falling back to the root search only when no modded clone Animator exists.
- **Generated sockets cannot prove orientation.** When empties are not exported, `VehiclePrefabBuilder` creates sockets from renderer bounds, so muzzle lands at max-Z and rear at min-Z by construction; the yaw-correction heuristic can never detect a reversed model from those sockets. Validate orientation against authored source (Blender front axis, FBX `FrontAxis`, prefab transform) and in-game observation; never treat generated sockets as evidence of correct facing.
- **Movement-detection threshold must use a small fixed squared value.** `RuntimeVisualDirector.UpdateLocomotionAnimation` computes `RCL_IsMoving` from position delta. A threshold like `(0.15 * deltaTime)^2` scales with frame time and can miss small or slow displacements, leaving the unit stuck in Idle even though the gameplay mover is active. `0.0001f` (squared distance 0.01) is still too coarse for slow units. Use `1e-6f` so any measurable displacement activates locomotion.
- **Clip-name ambiguity on rigged imports.** An FBX can import both `Action_Walk` (armature-prefixed, e.g. `IroncladWalker_Armature|IroncladWalker_Action_Walk`) and a plain `Walk` clip. `FindAnimationClip(clips, "Walk")` returns the first name containing "Walk", which may be the wrong (non-deforming or legacy) clip. Prefer `Action_*` names first (`Action_Walk`, `Action_Idle`, `Action_FireRecoil`, `Action_Death`) before falling back to plain names; otherwise Idle→Walk can end up targeting a Death-named or idle clip and the unit appears frozen.
- **FireRecoil must return to locomotion.** `FireRecoil` exit transitions must include a conditioned return to the movement state (`Action_Walk`/`Action_Drive`) when `RCL_IsMoving` is still true, with `hasExitTime = true`, in addition to the unconditional exit to `Idle`. Otherwise a shot interrupts walking and leaves the unit stuck in a recoil/idle pose.
- **Turret aiming must pause while moving.** If the runtime director updates turret yaw/pitch during locomotion, the weapon tracks targets while the unit walks, producing a "shooting while moving" visual artifact. Guard `UpdateTurretAiming` with the same moving-state flag used for `RCL_IsMoving`; only track when the unit is stationary.
- **Duplicate Animator from imported model.** A rigged FBX may carry its own `Animator`. Destroy every imported `Animator` on the model instance before `SetupAnimatorIfAvailable`; otherwise the prefab/bundle keeps two Animators and the rigging contract (exactly one configured Animator on the visual root) fails.
- Native visual overlays retain native gameplay hierarchies and do not retarget native animation rigs automatically. A native-template unit may opt into `nativeRigRetarget.mappings[]`: `sourcePath` is relative to the native prefab root and `targetPath` to the visual prefab root. Resolve every segment as an exact, unique direct-child path in the installed game's serialized hierarchy and the actual bundle; filenames or similar bone names are not evidence of a match.
- Runtime retargeting copies only bind-relative local position and rotation deltas after Animator evaluation. It preserves the target bind pose, compensates mapped position deltas for the overlay's uniform scale, never copies scale or root motion, and leaves unmapped bones under Animator control. The installed `EntityController.Destroy` CIL transpiler suspends copying at method entry; it requires the exact game `EntityController` type, instance `void Destroy(bool, EntityController)` signature, and static non-generic `RuntimeHarmonyPatches` callback `void(object, bool, object)`. Target/hook drift or empty IL must fail Harmony installation instead of silently leaving lifecycle suspension inactive. `RuntimeVisualDirector.Update` remains a dead-state fallback. Reset on both native reuse routes: `EntityFactory.InstantiateEntity` stack-pool hits call the regular `OnHasBeenInstantiated` path, so its existing post-factory `OnEntityInstantiatedTranspiler` callback must call `RuntimeVisualDirector.OnEntityReused`; the separate `OnHasBeenInstantiatedForReuse` callback uses the same reset method. A 2026-11-10 Profile2 BepInEx run reused the same native stack-pool GameObject after its retarget profile had been suspended and its mapped target pose displaced; it returned bound 8/8, unsuspended, with zero target-pose delta and `RCL_IsMoving=false`. This proves lifecycle reset telemetry for the pre-spawn stack-pop route, not rendered appearance. Static CIL shows `PrespawnEntity` pushes pre-spawn objects, `InstantiateEntity` pops them, and `EntityController.Destroy` decrements the prespawn count then calls `UnityEngine.Object.Destroy` without pushing the dead GameObject back. Do not expect a natural death-to-this-stack return; keep destruction and pre-spawn reset as separate routes. Trace: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.
- `RuntimeAndDiagnosticsValidationTests` also exercises unique-path binding, ambiguous-target fail-closed behavior, pose copying, suspension through the `EntityController.Destroy` transpiler entry hook, and pooled reuse. These C# harness checks cover lifecycle logic only; they do not prove rendered animation in a Steam run.
- `diagnostics_inspect_entity_visual` reports `animation.nativeRigRetarget` state plus up to 16 mapped rows: `sourcePath`, `targetPath`, availability flags, current `sourceLocalPose`, current `targetLocalPose`, and computed `expectedTargetLocalPose`. `omittedMappingCount` appears before `mappings`; the computed pose applies the bind-relative transfer math but proves nothing by itself. In a live unsuspended unit, compare source movement, actual target movement, and computed target pose during locomotion/fire; verify suspension at `Destroy` entry and pooled reuse separately.
- A serialized Animator/Avatar/controller reference does not prove the model deforms or transitions correctly in the target player.
- Root-motion-disabled Editor setup does not by itself prove an in-place clip; inspect the clip's root curves and test the animated model.
- The project validators check limited structural contracts. A passing result does not establish visual quality, stable frame-rate behavior, combat synchronization, or runtime rendering.
- When a runtime visual complaint includes suspected backward movement, separate the animation fix from the orientation check: animation setup can be validated offline, but direction of travel requires a live observation.
- Keep reflected-signature claims behind the complete game identity check: Rogue Command version, Unity version, `Assembly-CSharp.dll` SHA-256, and MVID.
- Label results as **static inspection**, **local build/validation**, or **in-game runtime evidence**; never promote one tier into another.

## Runtime follow-up: cloned retarget profiles and delayed Animators

- Unity can clone a `NativeRigRetargetProfile` after its non-serialized `_configuredMappingCount` was assigned. Derive the public configured count and omission diagnostics from serialized `_sourcePaths` as well, so the clone reports configured mappings before binding.
- A visual clone's `Animator` or controller parameters may become available after `RuntimeVisualDirector.Initialize`. Retry parameter discovery on `Time.unscaledTime` while the controller is missing or an expected `RCL_IsMoving`, `RCL_Fire`, or `RCL_Death` parameter is absent, and refresh immediately when the controller reference changes.
- For live locomotion evidence, sample `Animator.GetBool("RCL_IsMoving")`, the current clip, and source/target mapped poses in one BepInEx bridge batch while the unit is moving. At `Time.timeScale = 0`, `Time.deltaTime` is zero and the movement policy returns false; independent bridge calls can observe different frames.
- The 2026-11-10 Profile2 movement check observed 8/8 bound mappings and matching target/expected leg poses during `Action_Walk`. A later saved-Profile1 BepInEx MCP pass captured `Action_FireRecoil`, `RCL_Fire=triggered`, positive recoil, and matching computed target poses for `RotHor` and `ROTVERT`, supporting live aim/fire pose transfer through telemetry. In a subsequent saved-Profile2 pass, a temporary BepInEx Harmony postfix observed `IsSuspended=true` with the profile still bound 8/8 after `SuspendForDeath` returned and before original `EntityController.Destroy` continued. This used direct diagnostic damage, not natural combat; natural-combat death and death appearance remain unverified. Static inspection shows the destroyed object is sent to `UnityEngine.Object.Destroy`, not pushed back into the pre-spawn stack. Runtime trace: `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`; static factory trace: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.

### Destruction-triggered death animation

- The `EntityController.Destroy` entry hook calls `RuntimeVisualDirector.TriggerDeathAnimationForDestruction()` before retarget suspension only when `withoutTriggeringActions == false`. The director clears `RCL_IsMoving`, issues `RCL_Death`, and records success only after `Animator.SetTrigger` returns. A Profile2 BepInEx event trace observed the trigger method before `SuspendForDeath`, with the death-trigger flag and 8/8-bound suspended profile present at the hook postfix. The actor was killed by direct diagnostic damage; this is not natural-combat or rendered-death evidence, and native destruction follows immediately. Trace: `artifacts/runtime-smoke/retarget-animation-restart-profile2-20261110T1605Z/destroy-hook-method-events.json`.

### Timed death-visual retirement

- Keep the death Animator and `RuntimeVisualDirector` enabled while the Harmony terminal-destroy callback holds the visual. Disable gameplay behaviours and colliders and freeze rigidbodies so the death pose cannot continue affecting combat. Use the valid `Action_Death` clip duration divided by effective Animator speed plus the configured grace interval, with bounded/fail-closed timing; honor scaled versus unscaled Animator update mode.
- The 2026-11-10 saved-Profile2 BepInEx probe observed `IroncladWalker_Action_Death` (1.416667 s), a 1.566667 s hold, normalized time above 1, then GameObject removal while scaled `Time.time` stayed fixed. `UnscaledTime` was set only on the disposable probe clone to keep the saved match paused. This is direct-diagnostic runtime telemetry, not natural-combat or rendered-appearance evidence: `artifacts/runtime-smoke/retarget-death-visual-cloud-20261110170654/death-hold-runtime-result.json`.

### Screenshot-free runtime automation

- Use `diagnostics_get_ui_accessibility_census`, `diagnostics_wait_for_ui_text`, and `diagnostics_ui_click_text` for native profile/menu flows. They read Unity `Selectable` labels and hierarchy state and deliver clicks through EventSystem; they do not use Computer Use, screenshots, or screen-image inference. `automation_run_sequence` chains bounded MCP diagnostics calls.
- `Application.runInBackground` is enabled by the loader. Use `system_launch_game` only through Steam and preserve the selected saved profile. Confirm readiness with a live Unity scene query before claiming runtime evidence.
- Runtime animation telemetry (Animator state, clip time, mapped poses, lifecycle counters) remains distinct from rendered appearance. If MCP cannot observe a needed state, extend the bridge or report `BLOCKED`/`UNKNOWN`; a label census or computed rectangle is not pixel/render evidence.
- For bounded frame evidence without manual timing, call `diagnostics_wait_for_renderer_frame_evidence` with a clip substring and the required signals. It waits for distinct direct Game-camera samples and attempts to remove its probe in `finally` after receiving a cleanup token; check the returned cleanup status. A timeout is an unmet telemetry condition, not evidence that an animation failed to render.
- `automation_run_sequence` can chain diagnostic spawn, native movement, and the frame-evidence wait. Pass the spawned `entityKeyId` using `{"$ref":{"step":0,"path":"/entityKeyId"}}`; only earlier successful JSON results can be referenced. Check each step result and preserve the evidence boundary between Animator/deformation telemetry and pixels.

## Completion report

List edited source, clips, controllers, prefabs, and bundles; state tool/engine versions and commands; report observed diagnostics; and separate static, local Unity, and live-game evidence. Identify unverified state transitions or motion behavior explicitly.

### Screenshot-free retarget frame telemetry

- The renderer-frame probe reports active Animator clip/time, movement and death flags, native retarget bind/suspension counts, camera/frustum/layer state, and bounded `BakeMesh` vertex deltas. Focus the game camera on the disposable test entity before sampling; an offscreen renderer receives camera callbacks but no renderer samples.
- A 2026-11-10 native Move order on a diagnostic-spawned Ironclad Walker reached its destination during a 1.203-second unpaused window. The screenshot-free probe recorded visible direct Game-camera frames, 8/8 mappings, `RCL_IsMoving=true` in 18/24 frames, and Walk/Idle transitions. HoldPosition then produced 24/24 false movement samples and Idle. This is native pathfinding/Animator telemetry on a disposable clone; it does not establish production spawning, AI behavior, gait quality, or rendered pixels. Evidence: `artifacts/runtime-smoke/retarget-native-pathfinding-20261110/native-pathfinding-frame-result.json` and `native-pathfinding-held-idle-result.json`.
- When the match must stay paused, set `Animator.updateMode=UnscaledTime` only on the disposable clone. The 2026-11-10 Profile2 Ironclad retest used a temporary self-removing transform-motion driver: 24/24 unique samples had `RCL_IsMoving=true` and `Action_Walk`, with no parameter transitions, 8/8 retarget mappings bound, and a maximum sampled local-vertex delta of `0.182215`; after the driver ended, the parameter returned to false and the clip to `Action_Idle`. This validates the Animator/retarget path under synthetic transform motion only; it does not prove native pathfinding or rendered pixels. Evidence: `artifacts/runtime-smoke/retarget-walk-grace-live-20261110/renderer-frame-probe-post-fix-summary.json`.
- A later saved-Profile2 factory test used `SpawnEntityJson` for a disposable test factory and native `OrderFactoryProduceJson` for its walker. Unique product ID 254 retained 8/8 bound retarget mappings; four skinned renderers each recorded 8/8 visible Game-camera samples in `Action_Walk` with `RCL_IsMoving=true` and nonzero sampled local vertex deltas. HoldPosition returned it to `Action_Idle`. This closes the factory-output locomotion telemetry gap, but does not establish ordinary blueprint placement, AI scheduling, gait quality, or pixel appearance. See `artifacts/runtime-smoke/retarget-factory-production-20261110/factory-retarget-runtime-summary.json`.
- Start the renderer-frame probe only after the native Move order is active and the unit is moving. Unity continues rendering while `Time.timeScale=0`, and the probe's unscaled callback/sample cap can fill with paused Idle frames before a later unpause.
