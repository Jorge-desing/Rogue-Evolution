---
name: rogue-command-3d-model-authoring
description: "Design, create, or refine original 3D vehicle and unit visuals for Rogue Command with Blender and the pinned Unity authoring project. Use alongside unit and AssetBundle skills when the task changes a model or its in-game appearance."
---

# Rogue Command 3D Model Authoring

Authoritative guide for modeling, texturing, rigging, and exporting 3D vehicle and structure assets for Rogue Command.

---

## 1. Verified Architecture & Directory Invariants

- **Authoring Tools:** Blender 4.5.14 LTS (`.tools/Blender-4.5.14/.../blender.exe`) + Unity 2021.3.45f2 (Mono x64). Material Maker 1.7 for PBR images. Zero remote dependencies.
- **Authoring Project:** `unity/RogueCommandAssetAuthoring/`. Preserve all `ProjectSettings`, package manifests, and version control metadata.
- **Directory Layout:**
  - Blender Sources: `content/source-art/*.blend`
  - Python Scripts: `tools/Build-*.py`, `tools/generate_unit_model.py`, `tools/RogueModelKit.py`
  - Unity FBX: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/*.fbx`
  - Prefabs & Materials: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/`
  - AssetBundles: `content/packs/<pack-id>/bundles/*.bundle`
  - Previews & Logs: `artifacts/previews/` (ignored)
- **Script Parity Contract:** The copy in `.agents/skills/rogue-command-3d-model-authoring/scripts/RogueModelKit.py` must remain byte-identical to `tools/RogueModelKit.py` (verified by `test_model_kit_skill_copy.py`).

---

## 2. Coordinate System & Export Invariants

| Dimension | Blender Axis | Unity Axis | Coordinate Constraint |
|---|---|---|---|
| **Forward** | `+Y` | `+Z` | Vehicle front points toward `+Y` / `+Z` |
| **Up** | `+Z` | `+Y` | Vertical elevation |
| **Right / Starboard**| `+X` | `+X` | Lateral axis |
| **Ground Contact**| $Z \ge 0.0$ | $Y \ge 0.0$ | Lowest vertex rests at $0.0\text{m} \le Z_{\min} \le 0.08\text{m}$ ($Z_{\min} \le 0.55\text{m}$ hover/vtol) |
| **Origin Pivot** | `(0, 0, 0)` | `(0, 0, 0)` | Centered at ground plane midpoint |

### FBX Export Call & Rigging Invariant
- **CRITICAL:** `bake_space_transform=False` is MANDATORY for rigged/skinned meshes. Setting it to `True` bakes axis conversions into bone matrices, destroying bind-poses and causing zero-extent bounds (`localBounds 0,0,0`) in Unity.
```python
bpy.ops.export_scene.fbx(
    filepath=str(FBX_PATH), use_selection=True, object_types={"MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE", use_mesh_modifiers=True,
    mesh_smooth_type="FACE", path_mode="AUTO", axis_forward="-Z", axis_up="Y",
    bake_space_transform=False, add_leaf_bones=False, use_custom_props=False
)
```

---

## 3. 5-Tone PBR Structural Palette Standard

| Material Role | Proportion | Surface Description | Metallic | Smoothness | Emission |
|---|---|---|---|---|---|
| **Primary Armor** | 40–45% | Main hull plates, sponsons, chassis rails | 0.35–0.45 | 0.60–0.65 | None |
| **Light Armor** | 20–25% | Turret cheeks, upper deck, access hatches | 0.30–0.35 | 0.58–0.62 | None |
| **Mechanical Gunmetal** | 15–20% | Cannon barrels, suspension linkages, axles | 0.74–0.80 | 0.68–0.75 | None |
| **Tread / Rubber** | 10–15% | Track shoes, road wheels, tire chevrons | 0.08–0.18 | 0.28–0.34 | None |
| **Warning Accent** | 3–5% | Identification bands, danger stripes | 0.16–0.20 | 0.60–0.65 | None |
| **Emissive Optics** | 1–3% | Sensor visors, headlamps, plasma conduits | 0.15–0.25 | 0.75–0.85 | **HDR ×1.5–2.5** |

- **Faction Colors:** `vanguard` (slate teal), `siege` (rust ochre), `syndicate` (deep cobalt), `arctic` (tundra navy).
- **Camouflage Patterns:** `digital`, `splinter`, `tigerstripe` (Object-coordinate Voronoi/wave); `desert`, `urban`, `arctic`, `woodland` (noise-based).

---

## 4. 4-Channel Vertex Shading Contract (`VehiclePBR_VertexWear`)

Stored in `Mesh.colors32` (RGBA) for consumption by `RogueCommand/VehiclePBR_VertexWear` shader:

| Channel | Effect | Value Range | Behavior & Shader Decoding |
|---|---|---|---|
| **R (Red)** | Cavity AO / Dirt | 0.25 – 1.0 | $1.0$ on open/zenith surfaces; down to $0.25 - 0.40$ in wheel wells & undersides |
| **G (Green)** | Curvature / Edge Wear | 0.0 – 1.0 | Strictly $0.0$ on flat/concave faces; $0.7 - 1.0$ on sharp convex edges ($\ge 35^\circ$). Triggers two-tier chip (oxidized primer `#2c2724` at $0.12-0.42$, bright steel `#c7ccd6` at $>0.42$) |
| **B (Blue)** | Thermal Soot | 0.0 – 1.0 | $0.0$ baseline; scales to $1.0$ at muzzles/exhausts. Suppresses specular smoothness (`× (1 - soot*0.65)`) and metallic (`- soot*0.40`) |
| **A (Alpha)** | Team Tint Mask | 0.0 or 1.0 | $1.0$ on primary/light armor plates for `_TeamColor`; $0.0$ on mechanical gunmetal and treads |

---

## 5. Generator Architecture (`generate_unit_model.py` & `RogueModelKit`)

### CLI Scope & Archetypes
- **42 Mobile Archetypes:** `tank`, `walker`, `bike`, `artillery`, `arc_tank`, `hover`, `spider`, `vtol`, `halftrack`, `super_heavy`, `buggy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `quad_mech`, `combat_harvester`, `colossus_titan`, `hover_cruiser`, `heavy_dropship`, `shield_truck`, `demolition_breacher`, `artillery_strider`, `thermobaric_tank`, `ew_jammer`, `mobile_sam_launcher`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `hexapod_assault`, `assault_hovercraft`, `bridge_layer`, `command_hq_rig`, `flying_wing_bomber`, `mortar_carrier`, `siege_drill_mole`, `heavy_crane_mech`, `nanite_support_rover`, `gravity_siege_breaker`, `tactical_relay_carrier`, `amphibious_assault_tank`, `plasma_mortar_strider`, `stealth_recon_skimmer`.
- **10 Strategic Structures:** `factory`, `defense_turret`, `radar_station`, `energy_silo`, `orbital_silo`, `shield_dome`, `emp_obelisk`, `bunker_pillbox`, `anti_air_battery`, `comms_relay_tower`.
- **15 Weapon Mounts:** `cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon`, `railgun`, `quad_flak`, `plasma_mortar`, `laser_beam`, `vls_missiles`, `flamethrower`, `emp_emitter`, `nanite_projector`, `gravity_cannon`.
- **Wear Levels:** `pristine` (factory clean), `field_tested` (moderate wear), `battle_hardened` (heavy combat gouges and soot).
- **Key CLI Flags:**
  - Geometry: `--lods` (LOD0-2 chain), `--colliders`, `--gameplay-json`, `--track-sag`, `--armor-plating`, `--ik-rig`, `--weighted-normals` (default on; disable via `--no-weighted-normals`).
  - Greebles: `--suspension-arms`, `--mantlet-boot`, `--spaced-skirts`, `--hydraulic-hoses`, `--hull-greebles`, `--tactical-markings`, `--hydraulic-pistons`, `--ammo-chutes`, `--aps-suite`, `--stowage-kit`, `--slat-armor`, `--era-bricks`, `--camo-netting`, `--thermal-radiators`, `--nanite-dispenser`, `--grav-dampener`, `--comms-mast`, `--ram-prow`.
  - Attachments & Damage: `--attachments era,aps,sponson,fuel_drums`, `--export-detached-attachments`, `--damaged-state`, `--render-icons`.
  - Atlas Baking: `--bake-atlas`, `--bake-normals`, `--single-material` (`<Name>_Atlas.fbx`).

### Presets (56 Total)
Key presets include: `siege_breaker` (super_heavy dual_cannon), `recon_interceptor` (buggy gatling), `gunship_alpha` (vtol missiles), `colossus_titan` (titan dual_cannon), `sea_lance` (hover_cruiser railgun), `sky_sweeper` (flak_anti_air), `dreadnought_crawler` (siege_crawler railgun), `hive_carrier` (drone_carrier), `titan_quad` (quad_mech), `resource_behemoth` (combat_harvester), `aegis_bastion` (shield_truck), `longbow_strider` (artillery_strider), `inferno_purger` (thermobaric_tank), `spectre_jammer` (ew_jammer), `skyguard_sam` (mobile_sam), `salvage_hauler` (recovery_crawler), `arachnid_hexapod` (hexapod_assault), `scissor_bridgelayer` (bridge_layer), `overlord_command_hq` (command_hq_rig), `subterranean_mole` (siege_drill_mole), `atlas_crane_mech` (heavy_crane_mech), and faction remixes (`crimson_praetorian`, `cobalt_widowmaker`, `golden_aegis_command`).

---

## 6. Procedural Animations & Procedural Action Clips

- **Action Clips Contract:** `Action_Idle`, `Action_Walk`/`Action_Drive`, `Action_FireRecoil`, and `Action_Death`.
  - `Action_Idle`: Peaks at f15 with f8/f22 easing; never keyframes `Bone_Root`.
  - `Action_Death`: 12% settle at f8, 50% collapse at f16, full collapse at f28, holding to completion.
- **Armature Hierarchy:** `Bone_Root` $\rightarrow$ `Bone_Hull` $\rightarrow$ `Bone_TurretYaw` $\rightarrow$ `Bone_TurretPitch` $\rightarrow$ `Bone_BarrelRecoil`.
- **Rigid Weighting:** 1.0 per armor plate on unbatched source meshes to prevent rubbery mesh deformation.

---

## 7. Socket-to-Gameplay Runtime Binding Map (`RuntimeVisualDirector`)

Visuals bind dynamically to native units via canonical unprefixed sockets:

| Authored Socket | Runtime Director Behavior |
|---|---|
| `Socket_TurretPivot` | Traverses azimuth at 120°/s toward hostile `CurrentAttackTarget` (`IsEnemy==true`). Tilts 5° at $\le 25\%$ HP |
| `Socket_RadarMast` | Rotates continuously at 30 RPM around local up axis |
| `Socket_DamageSmoke` | Spawns volumetric smoke when `HealthRatio <= 0.50f`; switches to `<Name>_Damaged` mesh and jettisons fuel drums |
| `Socket_DamageFire` | Spawns flame particles and light when `HealthRatio <= 0.25f`; burns out APS and tilts turret pivot |
| `Socket_Deploy_Unit` | Slides bay door open 1.4 m when factory queue `InProductionCount > 0` |
| `Socket_RecoilPrimary` | Linear slide backward (0.22 m) upon shot fired (`_timeOfLastShot`); spring-damper recovery |
| `Socket_MuzzlePrimary` | Synchronous muzzle flash pulse for 0.07 s; contains nested `FirePoint_0` for native child discovery |
| `Socket_Hardpoint_<Type>`| Mounts detached accessories; detonates ERA tiles at $\le 75\%$ HP |
| `Socket_FootContact_*` | Ground impact anchors ($Z = 0.0$) for articulated walkers/mechs |
| `Socket_EngineAudio` | Placed over engine deck for `EntityFlockingMovement._movementSoundAudioSource` |
| `Socket_WeaponAudio` | Placed at weapon muzzle for `ShootProjectile.shotSoundAudioSource` |

---

## 8. Template Dimensions & Native Hierarchy Alignment

| Native Template | Sample Unit | `visualRootPath` | Collider Dimensions (W × H × L) |
|---|---|---|---|
| `PCXBaseTank` | Sandlance Scout | `"Vehicle"` | ~2.4m × 1.6m × 2.8m |
| `BombBike` | Skirmish Strike Bike | `"GameObject/BombBike"` | ~1.2m × 1.4m × 2.2m |
| `ArtilleryTank` | Colossus Siege Platform | `"Vehicle"` | ~2.8m × 1.9m × 3.6m |
| `GatlingWalker` | Ironclad Walker | `"Vehicle"` | ~2.2m × 2.7m × 2.0m |
| `LightningTank` | Surge Arc Tank | `"Vehicle"` | ~2.3m × 1.6m × 2.8m |

---

## 9. Material Maker Texture Runner & Card Sprite Rules

### Texture Generation
- Blender 4.5.14 bakes geometry/UVs/wear; Material Maker 1.7 generates final textures:
  ```powershell
  powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow urp
  powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow blender
  ```
- Material Maker outputs PNGs only. Never import its mesh or `.mat` exports to Unity.

### Card Sprite Contract
- **Resolution & Format:** $128 \times 128$ PNG; `FilterMode.Point`, `mipmapEnabled = false`, `spriteImportMode = Single`.
- **Generation:** Use built-in `imagegen` skill referencing the final Blender preview silhouette and palette. Never generate procedurally via Pillow/SVG.
- **Background:** Deep dark tactical gradient (`#0a1216` to `#162a34`) with a 2–3px emissive optic glint.

---

## 10. References & Detailed Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — Proportions, silhouette tests, and faction palettes.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — In-depth `bpy` recipes for tracks, bevels, and hulls.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — URP shaders, keyword toggles, and prefab builders.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy trees for native templates.
- [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) & [Validation](../rogue-command-pbr-texture-validation/SKILL.md).
