---
name: rogue-command-3d-model-authoring
description: "Design, create, or refine original 3D vehicle and unit visuals for Rogue Command with Blender and the pinned Unity authoring project. Use alongside unit and AssetBundle skills when the task changes a model or its in-game appearance."
---

# Rogue Command 3D Model Authoring

Use this skill to design, model, texture, and export original, high-readability 3D vehicle models and visual overlays for Rogue Command. It guides the entire visual pipeline—from procedural low-poly blockout in Blender 4.5.14 LTS to Unity 2021.3.45f2 AssetBundle authoring, material assignment, card sprite creation, and multi-tier verification.

---

## 1. Preserve and Use the Established Authoring Path

Before making any changes, review the project rules and handoffs:
- Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `unity/RogueCommandAssetAuthoring/README.md`.
- Use `rogue-command-unit-authoring` for gameplay balance and native templates, `rogue-command-assetbundle-authoring` for Unity bundle creation and import checks, `rogue-command-pack-validation` for manifest references, and `rogue-command-game-research` for pinned identity/signature inspection. Use `rogue-command-steam-runtime-validation` when the claim requires in-game evidence; follow its reversible deployment and cleanup steps. The local BepInEx MCP skill at `.tools/BepInExMCP/skills/bepinex-mcp/SKILL.md` applies when that bridge is available.
- **Authoring Project Preservation**: Work only inside `unity/RogueCommandAssetAuthoring/`. Preserve `ProjectSettings`, package manifests (`Packages/manifest.json`), Plastic/Unity Version Control metadata, and Unity Cloud configuration. Target **Unity 2021.3.45f2** (Mono, Windows Standalone x64). The user's separate Unity Hub project is not the mod authoring project.
- **Directory Invariants**:
  - Editable Blender sources: `content/source-art/*.blend`
  - Deterministic Python build scripts: `tools/Build-*.py`
  - Unity FBX models: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/*.fbx`
  - Visual prefabs and materials: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/`
  - High-resolution render previews and temporary diagnostics: ignored `artifacts/previews/` and `artifacts/`
  - Distributable bundles: `content/packs/<pack-id>/bundles/*.bundle`
- **Zero Remote Dependencies**: The local Blender executable (`.tools/Blender-4.5.14/.../blender.exe`) and deterministic Python scripts (`bpy`) are the supported authoring tools. Do not require Blender MCP or external 3D subscription services.

---

## 2. Rogue Command Art Direction & Silhouette Design

Rogue Command (by **feneq**) features a distinctive chunky, hard-surface low-poly aesthetic combining Kenney-inspired modularity with high-clarity RTS gameplay.

```
+-----------------------------------------------------------------------+
| TIER 1: Macro Silhouette (70% of visual read)                         |
| - Bold volume, aspect ratio, stance, ground clearance                  |
| - Must pass the "Squint Test" in < 250ms at 45° RTS elevation          |
+-----------------------------------------------------------------------+
                                  |
                                  v
+-----------------------------------------------------------------------+
| TIER 2: Meso Mechanical Geometry (20% of visual read)                  |
| - Continuous track loops, turret cheeks, engine louvers, exhaust ports |
| - Conveys unit propulsion, armor grade, and weapon function            |
+-----------------------------------------------------------------------+
                                  |
                                  v
+-----------------------------------------------------------------------+
| TIER 3: Micro High-Contrast Accents (10% of visual read)               |
| - Emissive sensor optics, hazard warning stripes, muzzle collars       |
| - Focuses the player's eye on active sensors and weapon hardpoints     |
+-----------------------------------------------------------------------+
```

### The 4 Core Visual Rules:
1. **The Squint Test**: Zoom out the Blender orthographic viewport until the unit is roughly 50 pixels tall. If its archetype (Scout, Strike Bike, Siege Tank, Mech) and forward direction are not instantly recognizable, simplify the internal geometry and exaggerate the primary silhouette masses.
2. **Single-Segment Chamfers (The Specular Glint Rule)**: Razor-sharp 90° edges have zero surface area facing the sun and look digital and dead. Always apply a 1-segment bevel (`segments = 1`, `width = 0.02m–0.06m`) with flat shading (`polygon.use_smooth = False`). These angled chamfers catch bright specular glints under directional lights at RTS distance.
3. **Continuous Track Contour**: Avoid flat box treads with floating blocks. Model tracks with sloped front approach angles (~25°–35° climb), round return idlers, and individual tread shoes (`0.15m–0.22m` spacing) along the ground and return runs.
4. **Recessed Glowing Optics**: Never leave sensor glass or headlights as flat paint. Recess lenses inside an armored visor hood and activate Unity's `_EMISSION` keyword with HDR multipliers (`1.5×–2.2×`) so they glow through the game's bloom filter.

*See detailed breakdown in [art-style-and-palette.md](references/art-style-and-palette.md).*

---

## 3. The 5-Tone Material Palette Standard

Do not use random palettes or noisy textures. Every vehicle must follow the disciplined 5-tone structural palette:

| Material Role | Proportion | Surface Description | Metallic | Smoothness | Emission |
|---|---|---|---|---|---|
| **Primary Armor** | 40–45% | Main hull plates, sponsons, lower chassis | 0.35–0.45 | 0.60–0.65 | None |
| **Light Armor** | 20–25% | Turret cheeks, upper deck, hatch covers | 0.30–0.35 | 0.58–0.62 | None |
| **Mechanical Gunmetal** | 15–20% | Cannon barrels, roll bars, suspension, axles | 0.74–0.80 | 0.68–0.75 | None |
| **Tread / Rubber** | 10–15% | Track shoes, road wheels, tire rubber | 0.08–0.18 | 0.28–0.34 | None |
| **Warning Accent** | 3–5% | High-contrast identification stripes, battery rails | 0.16–0.20 | 0.60–0.65 | None |
| **Emissive Tech/Optics** | 1–3% | Sensor lenses, headlamps, arc discharge rings | 0.15–0.25 | 0.75–0.85 | **HDR ×1.5–2.5** |

*See faction color presets and hex codes in [art-style-and-palette.md](references/art-style-and-palette.md).*

---

## 4. Blender Procedural & Deterministic Modeling

All production models must be script-reproducible using Blender's Python API (`bpy`) and the authoritative toolkit in `tools/RogueModelKit.py` (and `.agents/skills/rogue-command-3d-model-authoring/scripts/RogueModelKit.py`).

### Coordinate System Invariants
- **Forward**: Blender `+Y` $\rightarrow$ Unity `+Z`
- **Up**: Blender `+Z` $\rightarrow$ Unity `+Y`
- **Right / Starboard**: Blender `+X` $\rightarrow$ Unity `+X`
- **Ground Contact**: Lowest vertex of treads/tires/feet must rest exactly on $Z \ge 0.0$ ($0.0\text{m} \le Z_{\min} \le 0.08\text{m}$).
- **Local Origin**: `(0.0, 0.0, 0.0)` at ground center.

### Core Procedural Generator Suite (`RogueModelKit`)
Use the tested, high-readability procedural assemblies rather than ad-hoc mesh primitives:

| Category | Generator Function | Visual Subsystem Produced |
|---|---|---|
| **Chassis & Turret** | `make_sloped_glacis_hull` | Armored wedge chassis with sloped frontal glacis and belly clearance |
| | `make_faceted_turret_housing` | Octagonal turret shell with sloped mantlet cradle and rear bustle counterweight |
| **Mobility & Legs** | `make_continuous_track_assembly` | Full track unit (articulated belt, road wheels, sprockets, cleats, and fenders) |
| | `make_all_terrain_wheel_assembly` | Rugged chevron-lug tactical tire, deep-dish rim, planetary hub, and coilover shock |
| | `make_bipedal_digitigrade_leg` | Kinematic walker leg: hip joint, hydraulic thigh, knee armor, ankle struts, 3-toed foot |
| **Heavy Weapons** | `make_coaxial_cannon_assembly` | Coaxially aligned tank gun with breech collar, thermal sleeve, bore evacuator, and muzzle brake |
| | `make_rotary_gatling_cannon` | 6-barrel circular Gatling cluster with drive axle, clamp collars, and ammo feed chute |
| | `make_missile_launcher_pod` | Armored pod with upward elevation, recessed tube array, warheads, and backblast vents |
| | `make_arc_energy_emitter` | Futuristic weapon with glowing plasma cathode, induction coils, and convergent discharge prongs |
| **Optics & Greebles** | `make_gimbal_sensor_turret` | 2-axis FLIR targeting pod with azimuth base, elevation yoke, and dual optical lenses |
| | `make_recessed_sensor_visor` | Armored brow hood with deep cavity and glowing HDR sensor slit lenses |
| | `make_engine_deck_grille` | Armored engine intake frame with angled cooling louvers |
| | `make_armor_applique_array` | Array of Explosive Reactive Armor (ERA) tiles with standoff mounting bolts |
| | `make_hex_bolt_array` | Low-poly 6-sided bolt heads placed along armor seams and flanges |

### Draw-Call Optimization via Material Batching
Kitbashing produces 50–120 individual objects. In Unity, each object creates a separate GameObject and draw call unless unified. Always pass models through `batch_and_join_by_material(objects, prefix, preserve_names)` before export to combine meshes by their PBR material slot into 4–6 unified submeshes while preserving essential animated pivots.

### Fluent Assembly with `RogueVehicleBuilder`
For rapid, reproducible vehicle generation without manual coordinate arithmetic, use the declarative `RogueVehicleBuilder`:

```python
import RogueModelKit as rmk

builder = (
    rmk.RogueVehicleBuilder("HeavySiegeTank", faction="siege")
    .add_sloped_hull(length=3.2, width=2.5, height=0.92)
    .add_continuous_tracks(length=3.0, height=0.64, roadwheels=6, with_skirts=True)
    .add_engine_deck(slat_count=4, with_exhausts=True)
    .add_turret(size=(1.5, 1.7, 0.48), center_y=0.10)
    .add_cannon(caliber=0.10, length=1.85, elevation_deg=14.0)
    .add_sensor_visor()
    .add_spring_antenna()
    .add_dozer_blade(width=2.6, height=0.44)
)

# Automated validation, lightmap UVs, card sprite, .blend, batched FBX
diagnostics = builder.build_and_export(
    fbx_path=FBX_PATH,
    blend_path=BLEND_PATH,
    preview_path=PREVIEW_PATH,
    card_path=CARD_PATH,
    render_multiview=True
)
```

### Scriptable CLI Tool (`generate_unit_model.py`)
Run headless vehicle generation directly from the command line using Blender and the CLI generator script:

```powershell
.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\generate_unit_model.py -- `
    --name ColossusTank `
    --archetype tank `
    --faction siege `
    --weapon cannon `
    --dozer `
    --multiview `
    --card
```

- **Archetypes**: `tank`, `walker`, `bike`, `artillery`, `arc_tank`.
- **Factions**: `vanguard` (slate teal), `siege` (rust ochre), `syndicate` (deep cobalt), `arctic` (tundra navy).
- **Weapons**: `cannon` (ballistic), `gatling` (rotary 6-barrel), `missile` (honeycomb pod), `arc` (plasma cathode & prongs).
- **Outputs**: Generates editable `.blend`, Unity-optimized `.fbx` with lightmap UVs and batched submeshes, 4-view orthographic preview sheet, and 128x128 point-filtered tactical card sprite.

### Automated Quality Diagnostics
Always invoke `validate_model_mesh_quality(model_objects)` prior to export. It verifies:
- **Ground Contact Check**: Confirms $Z_{\min} \in [-0.02\text{m}, 0.08\text{m}]$.
- **Polycount Verification**: Confirms vertex and triangle counts match the low-poly RTS budget ($1,500$–$5,000$ triangles).
- **Material Slot Hygiene**: Flags any object with missing or null PBR material assignments.

### Multi-Perspective RTS Studio
Use `setup_multi_view_studio(model_objects, preview_dir, base_name, ortho_scale, render_all=True)` to render 4 calibrated orthographic perspectives simultaneously:
- **`ISOMETRIC`**: $45^\circ$ RTS combat camera (the gameplay primary read).
- **`TOP_DOWN`**: Direct vertical plan view (minimap readability and deck balance).
- **`FRONT`**: Head-on silhouette readability and glacis slope verification.
- **`SIDE`**: Profile balance, ground clearance, and suspension proportions.

### FBX Export Parameters
In your Python builder script, export selected meshes with:
```python
bpy.ops.export_scene.fbx(
    filepath=str(FBX_PATH),
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    use_mesh_modifiers=True,
    mesh_smooth_type="OFF",
    path_mode="AUTO",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=False,
    add_leaf_bones=False,
    use_custom_props=False
)
```

*See tested geometric algorithms and recipes in [blender-procedural-techniques.md](references/blender-procedural-techniques.md).*

---

## 5. Native Hierarchy & Template Alignment

Rogue Command gameplay components (`EntityController`, weapons, combat physics) live on native prefabs. Mod models are non-destructive visual overlays loaded via `visualRootPath`.

### Template Dimension & Hierarchy Matrix

| Native Template | Mod Unit Example | `visualRootPath` | Collider Bounds (W × H × L) | Key Pivot Alignment |
|---|---|---|---|---|
| `PCXBaseTank` | Sandlance Scout | `"Vehicle"` | ~2.4m × 1.6m × 2.8m | Twin barrels at `X = ±0.14`, `Y ≈ 1.0` |
| `BombBike` | Skirmish Strike Bike | `"GameObject/BombBike"` | ~1.2m × 1.4m × 2.2m | Wheelbase: Rear `Z = -0.78`, Front `Z = +0.75` |
| `ArtilleryTank` | Colossus Siege Platform | `"Vehicle"` | ~2.8m × 1.9m × 3.6m | Long elevated barrel at ~20° elevation |
| `GatlingWalker` | Ironclad Walker | `"Vehicle"` | ~2.2m × 2.7m × 2.0m | Underslung rotary guns at waist level |
| `LightningTank` | Surge Arc Tank | `"Vehicle"` | ~2.3m × 1.6m × 2.8m | Toroidal core centered at `(0, -0.1, 1.2)` |

*See full hierarchy trees and calibration rules in [native-hierarchy-and-pivots.md](references/native-hierarchy-and-pivots.md).*

---

## 6. Unity Material Pipeline & Draw-Call Optimization

When the Unity project scaffolds the prefab and builds the AssetBundle:

1. **Standard Shader Compatibility**: Use `Shader.Find("Standard")` to ensure consistent lighting with game scenes.
2. **The Emission Invariant**: Setting `_EmissionColor` is not enough; you must explicitly call `material.EnableKeyword("_EMISSION")` and set `globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive`.
3. **GPU Instancing**: Always enable `material.enableInstancing = true` so dozens of units sharing the 5-tone materials render in batched GPU draw calls.
4. **Script-Free Prefabs**: Visual prefabs must contain **no** `MonoBehaviour` components. The loader enforces fail-closed rejection on any missing or custom scripts.

*See C# implementation details in [unity-material-pipeline.md](references/unity-material-pipeline.md).*

---

## 7. Card Sprite Authoring Standards

Each playable unit requires a matching `128 × 128` pixel card Sprite referenced by `cardImageAssetName`.

- For every new or refreshed 2D raster card, icon, or sprite, use the installed built-in `imagegen` skill. Use the final Blender preview as a visual reference and preserve the authored model's silhouette and palette. Do not draw these images procedurally in C#, Python, SVG, canvas, or Pillow.
- Keep the generated PNG in the Unity authoring project's tracked `Assets/RogueCommandExpansionSample/` tree. Unity import settings may cap the runtime texture at 128 × 128; validate that the Sprite in the built AssetBundle uses that size. Record the final ImageGen prompt with the asset change so another agent can reproduce the intent.

- **Texture Import**: `FilterMode.Point`, `mipmapEnabled = false`, `spriteImportMode = Single`.
- **Composition**: Angled profile of the vehicle rendered or drawn against a deep tactical dark gradient (`#0a1216` to `#162a34`).
- **Color Fidelity**: Use the exact color values from the 3D model's materials so the card immediately prepares the player for what appears on the field.
- **Focal Glint**: Add a 2–3 pixel bright emissive optic or weapon glint to draw the eye during card drafting.

---

## 8. Multi-Stage Validation & Evidence Boundaries

Follow this strict progression when creating or modifying 3D models:

1. **Blender Visual Preview**:
   - Run the Python builder in Blender:
     ```powershell
     .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-<UnitName>Model.py
     ```
   - Inspect `artifacts/previews/<unit-name>.png` for silhouette clarity, ground contact, bevel specular highlights, and emissive accents.
2. **Unity Scaffolding & AssetBundle Build**:
   - Run the visual scaffolder to build and reopen the bundle in Unity 2021.3.45f2.
   - Verify zero missing scripts, valid mesh renderers, and zero null material slots.
3. **Offline Reference Audit**:
   - Run `tools/validate_content_bundle_references.py content/packs/<pack-id>` to verify all manifest asset names resolve within the built AssetBundle.
   - Run `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` to ensure registry, balance, and localization invariants pass.
4. **Steam Runtime Validation**:
   - Static inspection, Blender rendering, and Unity reopening provide **local authoring evidence**.
   - Only a Rogue Command session launched through Steam can prove that the model loads, instantiates without clipping, and renders correctly during combat. Record runtime evidence in `docs/research/AGENT-HANDOFF.md` using the categories established there and follow the BepInEx MCP skill when available.

---

## References & Specialized Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — 3-tier silhouette hierarchy, 5-tone palette sets, bevel physics, and greeble standards.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — Parametric `bpy` recipes for chamfered hulls, continuous tracks, stepped road wheels, and engine grilles.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — Standard shader configuration, emission keywords, GPU instancing, and card sprite rules.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy maps for `PCXBaseTank`, `BombBike`, `ArtilleryTank`, `GatlingWalker`, and `LightningTank`.
