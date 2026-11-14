---
name: rogue-command-vfx-authoring
description: "Author and validate Rogue Command particle VFX in URP."
metadata:
  hermes:
    tags: [Rogue Command, Unity, VFX, ParticleSystem, URP]
    related_skills: [rogue-command-3d-model-authoring, rogue-command-assetbundle-authoring, rogue-command-animation-authoring]
---

# Rogue Command VFX Authoring

Use this skill to author, integrate, or review particle effects and visual feedback for Rogue Command units. The Unity authoring project targets Unity 2021.3.45f2 and URP. AssetBundles contain serialized Unity assets, not new gameplay code; static inspection and editor builds do not prove effects appear in a Steam-launched match.

For Unity particle authoring, read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, `unity/RogueCommandAssetAuthoring/README.md`, and `references/native-vfx-integration-points.md`, `references/urp-particle-shaders-guide.md`, and `references/vfx-prefab-recipe-book.md`.

---

## 1. When to Use This Skill

- Authoring weapon firing effects (cannon flash, autocannon bursts, laser arcs, rocket exhaust).
- Setting up tiered physical vehicle damage (`Socket_DamageSmoke` at $\le 50\%$ HP, `Socket_DamageFire` at $\le 25\%$ HP).
- Ensuring particle shaders compile cleanly under Universal Render Pipeline (URP) in Unity 2021.3.45f2.
- Auditing visual prefabs and AssetBundles with `validate_vfx_assets.py` to prevent missing sockets or pink shader fallbacks.
- Tuning particle performance bounds (max particle caps, emission rates, GPU instancing) to preserve frame rates in dense RTS battles.

---

## 2. Core Technical Rules & Native Hooks

Damage suppresses ERA, APS and FuelDrums independently of requested attachment visibility. Repair restores the requested state for that slot. Pool reuse restores the original attachment visibility and clears the previous visual stance.

Temporary turret and deployment door overrides apply to one entity lifetime. Pool reuse clears both overrides and restores the authored closed door and recoil rest positions. Native production drives the door again on the next visual update.

Stun suspends turret tracking while active. Critical damage independently locks and tilts the turret. Repair restores the pre-damage pivot rotation, and reuse clears the stun presentation.

The runtime director shares one fire source across critical damage and OnFire. It uses the authored fire emitter when present and clears the effect after both causes end or the entity is reused. The fallback is created only when the authored emitter is absent.

1. **Automatic Muzzle Flash Hook & BFS Safeguard (`ShootProjectile`)**:
   - The native engine iterates `firePointsTransform` children at startup.
   - If a child has sub-children, it automatically caches `child.GetChild(0).GetComponent<ParticleSystem>()` and calls `.Play()` upon firing.
   - **Rule**: Place your muzzle flash `ParticleSystem` as **child index 0** (`FirePoint_0`) under `Socket_MuzzlePrimary`. Set `playOnAwake = false` and `looping = false`.
   - **Loader Safeguard (`EnsureMuzzleFlashHierarchy`)**: If an imported model lacks child particle emitters under a declared firepoint, `ContentPackRuntimeLoader` executes a recursive BFS to discover or synthesize a lightweight particle emitter on `FirePoint_0`, preventing unhandled `NullReferenceException` crashes during native `ShootProjectile.Init()`.

2. **Damage Smoke & Fire Anchors (`RuntimeVisualDirector`)**:
   - The loader visual director attaches smoke when `HealthRatio <= 0.50f` and fire when `HealthRatio <= 0.25f`.
   - Provide explicit transform sockets named `Socket_DamageSmoke` (e.g. over the engine exhaust deck) and `Socket_DamageFire` (near fuel tanks/turret ring).
   - A missing socket may make the fallback mount at the entity root. It only applies if `RuntimeVisualDirector` attaches to that entity; do not imply every native/custom prefab receives the modded effect. Authored emitters bind only when the socket role is unique and exactly one descendant `ParticleSystem` exists.

3. **URP Shader Compatibility & Runtime Fallback**:
   - Rogue Command runs on the Universal Render Pipeline.
   - Use `Universal Render Pipeline/Particles/Unlit` (or `Simple Lit`). Built-in pipeline shaders (`Particles/Standard Surface`, `Standard`) will render pink/broken.
   - Enable GPU instancing only when the chosen shader and renderer support it. The flag makes a renderer eligible; it does not prove batching or fewer draw calls.
   - A distributable bundle must include its required shader/material dependencies; do not assume the game's built-in shader assets satisfy a modded bundle.
   - **Runtime Fallback (`ApplyFallbackVisualMaterial`)**: In-game fallback handler automatically inspects imported materials and replaces null or unsupported shaders with native `Shader Graphs/EntityColorShader` or `Universal Render Pipeline/Lit` to prevent pink material artifacts.

4. **Performance Safeguards**:
   - Treat 250 max particles per vehicle as a local authoring ceiling, not an engine-enforced limit; the reference recipes suggest roughly 15 per muzzle flash, 40 damage smoke, and 30 damage fire.
   - Bound bursts, emission, and particle lifetimes; consider sub-emitters, simultaneous effects, and how many vehicles can emit at once. The current validator does not measure total formation cost comprehensively.
   - Choose soft particles and simulation space per effect. World space preserves trails as the vehicle moves; local space keeps effects attached. Do not force the same setting on every emitter.

---

## 3. VFX Validation Tool

Use `tools/validate_vfx_assets.py` as the CLI entry point. A copy exists at `scripts/validate_vfx_assets.py` in this skill; compare it with the root tool before relying on a behavior, because copies can drift. Run from the repository root through the project environment (explicit `PYTHONPATH` avoids globally visible Hermes package shadowing):

```text
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_vfx_assets.py --prefab unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/ScoutTankVisual.prefab --json")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_vfx_assets.py --pack content/packs/sample-native-expansion --json")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_content_bundle_references.py content/packs/sample-native-expansion")
```

Use `--help` for current options. The VFX validator's prefab checks are name-based; its bundle checks inspect root visual prefab names/sockets, but do not inspect each serialized `ParticleSystem`, material shader, or native firepoint binding. It applies weapon/damage socket expectations generically, so non-combat factory visual prefabs may receive irrelevant missing-socket flags. A non-critical exit code/score is not proof of correct wiring, rendering, quality, or performance.

---

## 4. Authoring Workflow

1. **Trace the consumer**: For native muzzle discovery or reflected bindings, verify the complete current game identity tuple (game version, Unity version, `Assembly-CSharp.dll` SHA-256, MVID); inspect the builder/runtime hierarchy rather than inferring from a socket label.
2. **Select a recipe**: Choose a suitable template from `references/vfx-prefab-recipe-book.md`, then tune it for effect scale and role.
3. **Preserve hierarchy**: Reuse existing canonical sockets; keep the muzzle `ParticleSystem` at child index 0 under each native firepoint. Avoid duplicate socket roles; damage sockets should contain exactly one intended emitter if relying on the authored binding.
4. **Configure rendering and cost**: Choose URP shader/blend mode and local/world simulation space per effect. Bound emission, lifetime, and max particles; stage one focal effect and keep support effects subordinate at RTS zoom.
5. **Audit and rebuild**: Run the VFX and bundle-reference validators, interpret their documented limits, and rebuild through the pinned Unity project builder when the bundle is in scope.
6. **Report evidence**: Distinguish static inspection, local Unity build/validation, and Steam-launched in-game observation; do not claim runtime rendering from a validator or bundle build.
