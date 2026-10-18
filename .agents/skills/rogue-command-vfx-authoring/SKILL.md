---
name: rogue-command-vfx-authoring
description: "Author, configure, and validate ParticleSystems, muzzle flashes, damage smoke/fire, and URP shaders for Rogue Command."
---

# Rogue Command VFX Authoring

Use this skill when creating, modifying, or reviewing Visual Effects (VFX), particle systems, weapon muzzle flashes, damage degradation effects, and URP shaders for Rogue Command vehicles and units.

Read `AGENTS.md`, `references/native-vfx-integration-points.md`, `references/urp-particle-shaders-guide.md`, and `references/vfx-prefab-recipe-book.md` before authoring particle assets in Unity.

---

## 1. When to Use This Skill

- Authoring weapon firing effects (cannon flash, autocannon bursts, laser arcs, rocket exhaust).
- Setting up tiered physical vehicle damage (`Socket_DamageSmoke` at $\le 50\%$ HP, `Socket_DamageFire` at $\le 25\%$ HP).
- Ensuring particle shaders compile cleanly under Universal Render Pipeline (URP) in Unity 2021.3.45f2.
- Auditing visual prefabs and AssetBundles with `validate_vfx_assets.py` to prevent missing sockets or pink shader fallbacks.
- Tuning particle performance bounds (max particle caps, emission rates, GPU instancing) to preserve frame rates in dense RTS battles.

---

## 2. Core Technical Rules & Native Hooks

1. **Automatic Muzzle Flash Hook (`ShootProjectile`)**:
   - The native engine iterates `firePointsTransform` children at startup.
   - If a child has sub-children, it automatically caches `child.GetChild(0).GetComponent<ParticleSystem>()` and calls `.Play()` upon firing.
   - **Rule**: Place your muzzle flash `ParticleSystem` as **child index 0** under each individual firepoint transform. Set `playOnAwake = false` and `looping = false`. No custom C# code is needed.

2. **Damage Smoke & Fire Anchors (`RuntimeVisualDirector`)**:
   - The loader visual director attaches smoke when `HealthRatio <= 0.50f` and fire when `HealthRatio <= 0.25f`.
   - Provide explicit transform sockets named `Socket_DamageSmoke` (e.g. over the engine exhaust deck) and `Socket_DamageFire` (near fuel tanks/turret ring).
   - If sockets are missing, the director falls back to emitting particles from the vehicle's ground center.

3. **URP Shader Compatibility**:
   - Rogue Command runs on the Universal Render Pipeline.
   - Use `Universal Render Pipeline/Particles/Unlit` (or `Simple Lit`). Built-in pipeline shaders (`Particles/Standard Surface`, `Standard`) will render pink/broken.
   - Always check **Enable GPU Instancing** on particle materials and `ParticleSystemRenderer`.

4. **Performance Safeguards**:
   - Cap max particles at $\le 250$ per vehicle to avoid micro-hitches during 100+ unit engagements.
   - Enable **Soft Particles** (`_SoftParticlesEnabled = 1.0f`) to prevent harsh intersection lines with terrain.
   - Use `Simulation Space = World` for smoke and dust trails so particles trail naturally as vehicles drive.

---

## 3. VFX Validation Tool

Use `tools/validate_vfx_assets.py` (mirrored in `scripts/validate_vfx_assets.py` within this skill) to audit visual assets:

```powershell
# Audit all visual prefabs in the Unity authoring project
.venv\Scripts\python.exe tools/validate_vfx_assets.py

# Audit a compiled content pack bundle
.venv\Scripts\python.exe tools/validate_vfx_assets.py --pack content/packs/sample-native-expansion

# Audit a specific model gameplay metadata file
.venv\Scripts\python.exe tools/validate_vfx_assets.py --metadata artifacts/CombatTank.gameplay.json

# Audit a specific prefab with JSON output
.venv\Scripts\python.exe tools/validate_vfx_assets.py --prefab unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/ScoutTankVisual.prefab --json
```

---

## 4. Authoring Workflow

1. **Select Recipe**: Choose a verified parameter template from `references/vfx-prefab-recipe-book.md`.
2. **Setup Hierarchy**: Place the emitter under the canonical socket (`Socket_MuzzlePrimary`, `Socket_DamageSmoke`, `Socket_DamageFire`) in Blender or Unity.
3. **Assign Material**: Use `Universal Render Pipeline/Particles/Unlit` with Additive (for flashes/arcs) or Alpha Blended (for smoke/dust).
4. **Run Validator**: Execute `validate_vfx_assets.py` to confirm socket names, particle limits, and URP shader compliance.
5. **Rebuild Bundle**: Rebuild the AssetBundle using the project's Unity batchmode exporter and run `validate_content_bundle_references.py`.
