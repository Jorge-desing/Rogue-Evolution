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
  - `Action_Idle` (30 frames loop): neutral at f1, slow-in/out shoulders at f8/f22 (`-0.004` m non-hover / `-0.005` m hover and `0.20°`), shallow peak at f15 (`-0.010` m non-hover / `-0.014` m hover and `0.55°`), close at f30. Keep `Bone_Root` untouched.
  - `Action_Walk` (40 frames loop, in-place, pelvis dip and foot clearance)
  - `Action_FireRecoil` (role-specific end frames: light/medium `15`, sam/walker `18`, heavy `21`; monotone phases at f1 neutral, f3 anticipation, f5 impact, f7 follow-through, f11 recovery, f15 neutral/hold)
  - `Action_Death` (default `35` frames; Bastion verified override `45`): early settle at f8 (`12%` collapse), half collapse at f16, full collapse by f28 and hold to the end. Vehicle collapse `-0.22` m; biped `-0.55` m; quad `-0.28` m; turret pitch droop `-34°` at full collapse.
- `Action_FireRecoil` must key both the weapon chain (`Bone_BarrelRecoil`, plus `Bone_TurretPitch` when present) and the mass-bearing body (`Bone_Hull` or `Bone_Pelvis`). Use a small local forward brace before impact, a backward body impulse at impact, a short settling overshoot, and neutral keys at the end. Never key `Bone_Root` translation.
- For every pack, audit the attack clip's actual f-curves: a clip name, Animator state, or barrel curve alone is insufficient evidence that the body moves.
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
- **Animator state graph must be canonical after every rebuild.** Clear stale states/transitions, bind `Action_Idle` as default, prefer `Action_Walk`/`Action_Drive` for `RCL_IsMoving`, route `RCL_Fire` to `Action_FireRecoil`, and return to locomotion when `RCL_IsMoving` remains true before the idle fallback. Do not leave duplicate short-name and armature-qualified states in the generated controller.
- When a rigged FBX is the source, point the scaffolder at the rigged FBX, not the static atlas FBX, and configure its importer as `Generic` with `Create From This Model` before prefab generation.
- A rigged import can carry its own `Animator`. Remove every imported Animator from the model instance before calling `SetupAnimatorIfAvailable`; otherwise the prefab/bundle retains two Animators and the rigging contract fails. The final visual root must contain exactly one configured Animator with a valid Generic avatar and runtime controller.
- **VisualOnly/Prefabs can refresh an existing rigged visual with `RefreshRiggedPbrPrefabAnimator`** to automate Animator integration without replacing authoring geometry, sockets, colliders, or materials. The helper instantiates the existing prefab, calls `SetupAnimatorIfAvailable` against the rigged FBX, disables root motion, validates one Generic Animator/avatar/controller, and saves the prefab. It is used for ArachnidHexapod and OverlordCommandHQ, whose visual prefabs live in `VisualOnly/Prefabs/` but whose FBX uses the batch material system. The refresh does not add card sprites or rebuild the mesh.
- **ModelImporter must be configured before prefab generation.** Call `ValidateWalkerModelSource` (or equivalent) for each rigged FBX to set: `animationType = Generic`, `avatarSetup = CreateFromThisModel`, `importAnimation = true`, `optimizeGameObjects = false`. Without `CreateFromThisModel`, the avatar is null and `SetupAnimatorIfAvailable` fails.
- **Material resolution for PBR units.** The batch material system strips unit-name prefixes (`ArachnidHexapod_`, `OverlordCommandHQ_`) from material keys. Add these to `intermediatePrefixes` in `TryResolveBatchMaterial` and ensure aliases cover all material types used (PrimaryArmor→armor, Gunmetal→gunmetal, Tread→tread, WheelRim→wheelRim, Warning→warning, Emissive→emissive, LightArmor→lightArmor).
- **Model yaw correction uses only the horizontal forward axis (Z), never vertical height (Y).** `CalculateVisualYawCorrection` must compare muzzle vs. rear socket positions along Z (Unity forward). Using Y (vertical height) or picking the dominant axis between Y and Z caused false 180° flips on walkers whose muzzle/rear height difference exceeded their horizontal separation. Use `muzzleLocal.z` and `rearLocal.z` exclusively.
- **Runtime movement detection must use fixed squared thresholds plus a release grace.** `RuntimeVisualDirector.UpdateLocomotionAnimation` treats `positionDeltaSquared > 1e-8f` or sampled `velocitySquared > 1e-6f` as positive movement evidence. `LocomotionAnimationPolicy` keeps `RCL_IsMoving` true for 0.12 scaled seconds after the last positive sample, bridging sparse native movement ticks that would otherwise flash Idle. A zero/invalid delta time clears the hold immediately, so pause returns to Idle; stun, death, and reuse also clear it. This same moving-state flag delays turret aiming through the grace interval.
- **Native-pathfinding retarget evidence (2026-11-10):** A diagnostic-spawned Ironclad Walker followed a native Move order to its destination in a bounded 1.203-second sample. The direct Game-camera renderer probe recorded 24 visible/in-frustum frames, 8/8 mappings, `RCL_IsMoving=true` in 18/24 frames, and Walk/Idle states; a subsequent HoldPosition probe recorded the moving flag false in all 24 frames and ended in Idle. This verifies native pathfinding telemetry through the retarget controller, not production spawning, AI behavior, pixel visibility, or gait quality. Traces are under `artifacts/runtime-smoke/retarget-native-pathfinding-20261110/`.
- **Turret aiming must pause while the unit is moving.** If `UpdateTurretAiming` runs during locomotion, the turret tracks targets while walking and produces a "shooting while moving" visual artifact. Guard aim updates with the same moving-state flag used for `RCL_IsMoving` so the turret only tracks when the unit is stationary.
- Keep socket creation idempotent: search recursively for existing canonical sockets before creating any new transform; duplicate same-role sockets make `UniqueSocketRoleBinding` ambiguous.

### Vehicle / Tank Rigging (leg_type = None)

For non-walker vehicles (tanks, rovers, hovercraft) that have a turret but no articulated legs, use `leg_type=None` in `generate_procedural_armature`. This creates a hull-based armature (`Bone_Hull` → `Bone_Pelvis` → `Bone_TurretYaw` → `Bone_TurretPitch` → `Bone_BarrelRecoil`) without leg chains or IK.

- **In-place invariant still applies**: `Bone_Root` must have zero translation. For vehicles, the `Walk` action animates `Bone_Hull`, not `Bone_Root`. Root-motion validation must check that any `Bone_Root` location curves present are all zero (not require exactly 3 curves).
- **Action clips**: `generate_procedural_action_clips` with `leg_type=None` produces `Action_Idle`, `Action_Drive` (and an `Action_Walk` alias to the same action), `Action_FireRecoil`, and `Action_Death`. The drive/walk clip animates `Bone_Hull` location/rotation for a 40-frame in-place bob and pitch/roll read at RTS distance; idle and death use the same f8/f22 and f8/f16 easing contracts as walkers.
- **Rigid weighting**: The same rigid 1.0-per-mesh rule applies. Meshes containing "Barrel", "Muzzle", "Bore", "Recoil", "Nozzle" bind to `Bone_BarrelRecoil`; "Mantlet", "Cradle", "Howitzer", "TurretPitch" bind to `Bone_TurretPitch`; "Turret", "Bustle", "Visor", "Radar" bind to `Bone_TurretYaw`; everything else binds to `Bone_Hull`.
- **Socket preservation**: Vehicle sources already have canonical sockets (`Socket_MuzzlePrimary`, `Socket_TurretPivot`, etc.) as empties. Reuse them; do not create duplicates.
- **Unity scaffolder changes needed**:
  1. Check which model path each unit currently uses — some units in a pack may already point to `*.fbx` (static, pre-rig) while others use `*_Atlas.fbx`. Change all to the rigged `*.fbx`.
  2. Add `ConfigureRiggedWalkerModel(modelPath)` call (reuses the same Generic-avatar importer config).
  3. In the visual creation method: destroy imported Animators, call `SetupAnimatorIfAvailable`, and disable root motion on the final Animator.
- **No foot contact sockets** for wheeled/tracked vehicles unless they have articulated legs. The `EnsureCanonicalWalkerSockets` call is walker-specific and should be skipped for vehicles.

### Animation scope discipline

- When the user says "remove animations from the hexapod" or "legs only", they mean ONLY that specific unit's leg animations — do not remove or alter animations on other units or modify shared animation generation functions.
- The user's frustration signal "Estamos hablando de las patas" means: scope is legs only, don't touch other animations.
- When asked to "remove the leg animations and start over", actually delete the Walk/Drive actions, strip hull keyframes, and rebuild — do not just modify existing clips or remove unrelated animations (Idle, FireRecoil, Death).
- Label the result as static inspection vs. in-game runtime evidence. A successful build does not prove the animation plays correctly in-game.

### Debugging hexapod deformation (lessons from repeated failed fixes)
- **Edit-mode vs object-mode bone coordinates differ.** `bone.tail.z` in object mode (via `arm.data.bones[name].tail`) is NOT the same as `edit_bone.tail.z` in edit mode. Always verify bone orientation in edit mode; object-mode tail values can be misleading and cause a "fix" that is actually a no-op.
- **Middle leg bones (ML/MR) are authored in the source blend, not generated by `RogueModelKit.py`.** There is no code that creates `Bone_*_ML/MR`; they exist only in `content/source-art/arachnid-hexapod.blend`. A normalizer function in `RogueModelKit.py` that flips tail Z is a no-op if the bones are already correctly oriented in edit mode. To change their geometry, edit the blend directly or add a function that creates them from scratch with the correct rest matrices.
- **Do not claim a rig fix is resolved without in-game visual validation.** Static tests, Blender contract tests, Unity batchmode builds, and bundle imports all pass while the model remains deformed in-game. Only a screenshot or video of the running game confirms geometry and animation transitions. If runtime smoke crashes or is unavailable, report the limitation explicitly instead of asserting the fix works.
- **Blender FBX export must use `bake_space_transform=False`.** With `bake_space_transform=True`, Blender bakes the axis conversion (Z-up → Y-up) into vertex/bone transforms, corrupting bind-pose matrices for skinned meshes — the SkinnedMeshRenderer reports zero `localBounds` (Extents 0,0,0) and the mesh collapses in-game. Set `bake_space_transform=False` in `bpy.ops.export_scene.fbx()` for any rigged FBX destined for Unity; Unity handles axis conversion at import time, preserving correct bind poses. This was verified via runtime inspection: `True` → degenerate bounds; `False` → correct bounds. This is especially critical for hexapods and other non-standard rigs where the root bone has a non-identity rotation.
- **`bake_space_transform=False` alone may not resolve zero-extent SMR bounds.** If the SkinnedMeshRenderer `localBounds` remain zero after this fix, the root cause is likely deeper: the middle leg bones (ML/MR) authored in the source blend may have degenerate rest poses, or the bind-pose matrices stored in the FBX do not match the armature's edit-mode bone matrices. Next steps: (1) inspect `SkinnedMeshRenderer.sharedMesh.bindposes` vs `bone.localToWorldMatrix` in Unity at runtime; (2) verify that every mesh vertex group maps to a bone that exists in the armature; (3) re-author the middle leg bones directly in the blend with correct rest matrices rather than relying on a normalizer. Do not assert the fix is resolved without in-game visual confirmation.
- **`Animator.isInitialized` is always `False` in this game even for working native units.** Do not use it as a health check. `SkinnedMeshRenderer.isVisible` and `localBounds` are better indicators of whether the mesh is actually rendering. A zero-extent `localBounds` means the mesh is collapsed regardless of Animator state.
- **Unity FBX meta settings for rigged models must be `animationType: 3` (Generic) and `avatarSetup: 2` (CreateFromThisModel).** The build validator calls `ValidateWalkerModelSource` which sets these via `ModelImporter`, but the `.meta` file on disk may still show `animationType: 2` and `avatarSetup: 1`. When a rigged model does not render at all (invisible, no mesh), check the `.meta` file and manually set `animationType: 3` and `avatarSetup: 2`. Without `CreateFromThisModel`, the avatar is null and `SetupAnimatorIfAvailable` fails silently — the prefab has no valid avatar and the SMR renders nothing.
- **Build validation must be adjusted for rigged models.** The `ValidateVisualBundleAssets` check counts `MeshFilter` components; rigged models use `SkinnedMeshRenderer` and report 0 MeshFilters. Set `minimumMeshFilters` to 0 for rigged units. Similarly, `ValidatePbrVisualBundlePrefab` checks for `RogueCommand/VehiclePBR_VertexWear` shader; units that no longer use PBR textures (because they are now rigged) will fail this check. Skip PBR validation for rigged units by commenting out the `ValidatePbrVisualBundlePrefab` calls.
- **FBX export without mesh batching can preserve vertex weights.** If `batch_and_join_by_material` corrupts vertex groups during join (meshes lose their rigid 1.0 weight binding), export the original unbatched meshes directly: `rmk.export_unity_fbx(FBX_PATH, list(meshes) + [armature], ...)`. This preserves per-mesh vertex groups and Armature modifiers. The trade-off is more draw calls, but correctness takes priority over batching for rigged units.
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

## Native Overlay Retarget Contract

- For a native-template visual overlay, declare `nativeRigRetarget.mappings[]` only when a replacement rig needs selected native transform motion. Map exact paths from the native prefab root to exact paths from the visual prefab root; use the serialized Transform hierarchy as the source of truth.
- Run `tools/validate_native_visual_roots.py <pack>` to resolve every mapping source in the pinned native hierarchy and every target in the exact bundle prefab before adjusting the rig; this is static structure evidence, not motion evidence.
- Keep the map sparse and explicit. At runtime the loader applies source bind-relative local position/rotation deltas to target bind poses after Animator evaluation, scaling positions for the overlay's uniform scale. It never writes scale or root motion, and unmapped bones remain Animator-owned.
- The installed `EntityController.Destroy` CIL transpiler suspends the bridge at method entry; it validates `MethodBase __originalMethod` against the exact game `EntityController` type and requires the static, non-generic `RuntimeHarmonyPatches` callback `void(object, bool, object)`. Target/hook drift or empty IL aborts Harmony installation. The visual director's dead-state check remains a fallback. The existing pool-reuse transpiler route restores source/target bind poses. This is a transform-pose bridge, not a Humanoid retargeter or proof that walk, turret, firing, death, and reuse all look correct; confirm those transitions in a game run.
- The local C# validation harness covers unique mapping, ambiguous path rejection, bind-relative pose transfer, suspension through the destroy-entry hook, and pooled reset. Keep visual deformation and animation-quality claims pending until observed in a Steam match.
- The destroy-entry hook now issues `RCL_Death` through `RuntimeVisualDirector` before suspending mapped-pose copying when native actions are enabled. A BepInEx event trace observed this order on Profile2 after direct lethal diagnostic damage; it does not establish natural-combat or rendered death animation. `EntityController.Destroy` continues immediately after the hook, so confirm any intended death hold/render timing separately: `artifacts/runtime-smoke/retarget-animation-restart-profile2-20261110T1605Z/destroy-hook-method-events.json`.
- `diagnostics_inspect_entity_visual` reports presence/bind/suspension state and up to 16 mapping rows with current source/target local poses plus the computed target pose. `omittedMappingCount` precedes `mappings`; the computed target is a math reference, not proof of rendered deformation. During a live unsuspended walk/fire sample, check that source and target poses change together and target matches the computed pose; verify death suspension and pool reuse as separate transitions.
- The 2026-11-10 saved-Profile2 sample observed 8/8 Ironclad mappings bound and matching computed poses for two leg roots during `Action_Walk`. A later saved-Profile1 BepInEx MCP sample captured `Action_FireRecoil` and matching computed yaw/pitch target poses, supporting aim/fire pose transfer through telemetry. A following saved-Profile2 pass captured `IsSuspended=true` after the destroy callback while the profile remained bound 8/8 and before the native object disappeared. The probe used direct diagnostic damage; it does not prove natural-combat death or rendered death appearance. Static CIL shows `EntityController.Destroy` decrements the pre-spawn count and destroys its GameObject without pushing it back to `EntityFactory.PrespawnedEntities`; treat stack-pop reset as a separate reuse route. No image inspection was made. Runtime trace: `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`; static trace: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.

### Runtime renderer sampling

- For a live rig check, use `diagnostics_wait_for_renderer_frame_evidence` to wait for distinct direct Game-camera samples with the required movement, deformation, retarget, or visibility signals. The probe samples at most 128 local vertices with `BakeMesh` and attempts cleanup in `finally` after receiving a valid token; inspect cleanup status. Compare multiple frames and keep root-motion/in-place checks separate. This remains runtime telemetry, not pixel appearance or gait-quality evidence.
- To run spawn → native move → evidence wait in one request, use `automation_run_sequence` and pass the prior spawn's `entityKeyId` as `{"$ref":{"step":0,"path":"/entityKeyId"}}`. References are typed JSON Pointer lookups from earlier successful results; a rejected reference must not trigger the dependent game action.
- A 2026-11-10 paused Profile2 sample observed Ironclad `Action_Idle`, 8/8 mappings bound, and small positive local vertex deltas. It validates the probe and an idle deformation signal only; it does not establish walk-cycle quality, aiming, firing, death appearance, or pixels.
- A factory-produced Profile2 Ironclad Walker (native `Produce`, unique key 254) retained 8/8 retarget bindings and was sampled during native Move: all 8 frames on each of four skinned renderers reported `Action_Walk`, `RCL_IsMoving=true`, visible direct Game-camera bounds, and positive `BakeMesh` deltas. HoldPosition returned the Animator to Idle. This is runtime deformation telemetry, not gait-quality or image evidence; trace: `artifacts/runtime-smoke/retarget-factory-production-20261110/factory-retarget-runtime-summary.json`.
- Start the frame probe after movement begins. It samples on unscaled render callbacks, so paused rendering can fill the bounded sample quota with Idle frames even when `Time.timeScale=0`.
- Use the MCP 1.4.6 clip/movement sampling filters when a native Move order has not reached Walk yet. Unity skips nonmatching render frames before `BakeMesh` and quota use; keep positive deformation and 8/8 retarget binding as separate post-sample requirements. The 2026-11-11 live Profile3 run captured 48 Walk/moving/visible/retarget-bound frames across four skinned renderers and positive local-vertex delta in 44/48; this does not establish gait quality or pixel appearance.
- Exact final Release verification (SHA-256 `fe08d495c3236a174c31e89bfae236c79f6af42541404770845a1043934f1856`): native Move plus Walk/moving filters returned 32 samples (8 each across four Ironclad Walker skinned batches); sampled records retained 8/8 bound, unsuspended mappings, with positive post-baseline `BakeMesh` deltas observed per batch. Probe stop plus a live component census confirmed cleanup. This is skeletal deformation telemetry only, not gait-quality or pixel evidence; report: `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/final-release-retarget-summary.json`.
- Current Release SHA-256 `2E740D387F600CAF0023ED7254F9F41ECF9E69E34E548328BFF147D250BC3DFB` retained the valid SkeletalArmature avatar, disabled root motion, and 8/8 bound mappings. The walker had five skinned renderers while the bounded probe sampled four; each sampled renderer had 7/8 positive local BakeMesh deltas during native Move/Walk and during diagnostic Death. Death rows were stationary with retirement pending and the retarget profile suspended. These measurements establish sampled skeletal deformation and lifecycle state, not gait or death-pose quality. See `docs/research/RETARGET-CURRENT-RELEASE-RUNTIME-2026-11-11.md`.
- d576 Release Profile3 run: the retarget map transferred the native RoboMarine leg chains and `RotHor`/`ROTVERT` yaw-pitch sources into the Ironclad Walker armature with 8/8 mappings bound. Native Move traversed 20 world units, the Animator played Action_Walk while moving, and sampled baked geometry changed on all four observed renderer batches; it returned to Idle after arrival. Five skinned batches existed, so one was outside the probe cap. This does not rate contact, balance, or gait readability. Details: `docs/research/RETARGET-D576-RUNTIME-2026-11-11.md`.
