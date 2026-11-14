---
name: rogue-command-material-maker-texture-authoring
description: "Use Material Maker 1.7 for all procedural or painted Rogue Command texture images (required standard tool), adapting its single-submesh paint limit and Unity exports to the project's PBR contract."
---

# Rogue Command Material Maker Texture Authoring

Use this skill when Material Maker is selected for procedural PBR graphs or a supported 3D paint pass. Read [Rogue Command PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) first; it remains authoritative for the source mesh, channel packing, and Unity import.

Material Maker saves or generates material graphs as `.ptex`. The integrated Blender/Unity route exports that graph to the four PNG maps; the repository's downstream tools consume images rather than directly importing `.ptex`. Keep reproducible generated templates under `artifacts/`, and preserve a hand-authored graph required for a promoted material as task-owned source art with its inputs.

## Setup and source protection

- Material Maker 1.7 is the required standard tool and is staged under ignored `.tools/MaterialMaker-1.7/`. To prepare it in another checkout, use `tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker`; it verifies and extracts the pinned application archive, then builds the isolated CLI runtime under `.tools/MaterialMaker-1.7-patched/runtime/`. The upstream install remains unchanged.
- Keep the finalized `.blend`, production FBX, UVs, sockets, hierarchy, LODs, and RGBA vertex wear unchanged. Work from a disposable copy under `artifacts/texture-work/<Name>/MaterialMaker/`.
- Material Maker's paint project accepts OBJ/GLB/GLTF/FBX with UVs and a single submesh. If the production FBX has multiple submeshes, use procedural graphs or make a temporary texture-only copy; never change the Unity production mesh to satisfy the paint UI.

## Author and export

1. Freeze UVs and decide whether the task uses procedural graph generation or direct model painting. Confirm the texture resolution and export paths before saving anything to the authoring project.
2. Build or edit a material graph for the observed finish and feature scale. Avoid generic noise that obscures panel layout or material separation at the game's RTS camera distance. A connected edge-emission graph can outline a large fraction of a repeated procedural panel field; inspect its coverage on the target UV model and keep emission for powered features only. Generate and tune experimental graphs under `artifacts/` unless the requested change explicitly includes a shared preset.
3. Export image files as PNG to the temporary workspace. Do not use generated Unity `.mat` files, shader templates, or mesh exports as Rogue Command assets.
4. Deliver the exact set `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. Pack metallic in R/G/B and smoothness (`1 - roughness`) in A. Do not pack AO into MetallicGloss; the current shader reads only R and A.
5. Keep auxiliary AO, height, and curvature maps separate unless the in-project shader is changed and verified. Use the repository's scalar export and four-map packer for the required channel layout. If export fails, keep the Blender baseline and report the failed Material Maker pass; the baseline does not replace the required authoring pass.
6. Keep experimental/generated graphs under `artifacts/`. Preserve a hand-authored graph needed to reproduce a promoted material with its task-owned source-art inputs; it remains a Material Maker source project, while the Blender model and Unity material builder retain their roles.

## Headless export (CLI)

For the integrated pipeline, use `tools/Run-MaterialMakerBake.ps1`; the raw application CLI below explains upstream behavior and does not replace project packing/auditing.

Material Maker supports batch export without the GUI. For Rogue Command, use the repository runner; do not use the upstream executable directly for unattended exports because its 1.7 CLI shutdown does not join its rendering worker before exit.

```
material_maker --export-material --target <engine> -o <output_dir> <input.ptex> --quit
```

- Run with the working directory set to the Material Maker install dir (`.tools/MaterialMaker-1.7/material_maker_1_7_windows/`); relative output paths are resolved against that dir, so prefer absolute `-o` paths.
- `<engine>` is one of `Blender`, `Godot`, `Unity`, `Unreal`. For this project use **`Blender`** — it emits PNGs only (albedo/normal/metallic-gloss/etc.), with no engine-specific `.mat`/`.tres`/`.mm2ue` files to discard. `Unity` target also works but writes a `.mat` we do not use.
- Template strategy: copy an existing example `.ptex` (e.g. `examples/bricks.ptex`) into `artifacts/mm-templates/<Name>.ptex`, edit the graph in the GUI or by JSON, then export. Keep the edited `.ptex` under `artifacts/` as reproducible source art.
- First-time setup is fully automatable: `tools/Run-MaterialMakerBake.ps1` creates `%APPDATA%/material_maker_2/export_targets/` and temporarily writes the `material_export_roguecommand.mme` export target (Material Maker 1.7's `<material>_export_<target>.mme` naming convention). It restores any pre-existing target afterward. Do not create `export_targets` as a regular file.
- Material Maker 1.7 release issue #1506 has a startup race in command-line rendering-device initialization. The runner writes a temporary `override.cfg` beside the patched executable and autoloads `tools/MaterialMakerWaitDevice.gd`; it waits for `/root/mm_renderer`'s `RenderingDevice` before export and restores the previous override afterward.
- `tools/Build-MaterialMakerCli.ps1` downloads the pinned Material Maker source and Godot 4.7 archives by SHA-256 and builds a **CLI-only local copy**. Its source patch queues texture/shader RID cleanup on the renderer owner thread, frees the RenderingDevice there, drains queued GPU resources and joins the renderer thread before exit. It also releases the non-reference-counted shader error handler and ports the PTeX EditorImportPlugin/fixup scripts to Godot 4.7 (`FileAccess`, current importer signatures, `await`, and `DirAccess`). The official Material Maker installation and project assets are not edited. `-VerboseMaterialMaker` enables lifecycle markers in `material-maker.log`.
- A one-off hidden Windows/Vulkan editor import verified that those patched GDScripts no longer produce parse/signature errors. Godot's short-lived editor process then reported thread/resource leaks at shutdown, so the reproducible PCK build stays on Godot's headless path instead of keeping that editor mode. The headless build still logs renderer-dummy shader messages; the actual CLI texture export runs on Vulkan and is checked separately.
- The isolated build copy corrects source diagnostics verified in the pinned release: a stale logo UID, three demo OBJ files that referenced absent `.mtl` files, Godot 3 `shader_param/` keys, and an unavailable optional Steam extension. It also removes only the three known identity HDR metadata pairs (`GAMMA=1` and empty `PRIMARIES`) from the isolated copy; the patcher verifies the binary pixel payload remains byte-identical. For `SubViewportContainer` scenes with `stretch=true`, it avoids manual resize callbacks and removes conflicting fixed `SubViewport.size` assignments. The pinned Godot import/pack no longer emits HDR or viewport warnings. Headless PCK packing still logs six `Parameter "shader" is null` errors from Godot's dummy renderer (`material_storage.cpp`); keep these visible and distinguish them from the Vulkan CLI export. The latest build has no source parse errors or thread/resource leaks.
- The archive setup helper computes relative paths from the verified extraction root instead of relying on `Resolve-Path -RelativeBasePath`, which is unavailable in some PowerShell installations. The CLI builder hashes the staged Godot executables and skips a rewrite when the verified runtime binary is already identical.
- The actual strict CLI export was checked separately: no Material Maker script/compile errors, ObjectDB/resource/RID leak reports, invalid IDs, missing extension/profile messages, or orphan resources were logged after clean shutdown. On this host, `material-maker-error.log` still contains two Vulkan loader warnings (registry manifest lookup and duplicate Medal hook layer); those belong to the host driver/layer setup. Preserve and report them instead of suppressing diagnostics or changing global graphics settings.
- The runner invokes the patched Godot/PCK pair with `Start-Process -Wait`, exports scalar PNGs through the temporary `RogueCommand` target, then runs `tools/pack_material_maker_pbr.py` to produce the exact four-map contract. The packer writes metallic to RGB and `1 - roughness` to alpha in `<Name>_MetallicGloss.png`.
- If four-map packing returns nonzero after the renderer has exited, `Run-MaterialMakerBake.ps1` retries `pack_material_maker_pbr.py` once against the already-exported scalar maps, then fails if the retry also returns nonzero. This handles a transient post-export pack failure without bypassing strict quality checks; inspect the per-preset logs and reports before repeating the whole bake.
- Verify the output directory contains the expected PNGs and dimensions before copying into the Unity texture folder; a silent empty export is possible if the target path is wrong.

## Repository workflow matrix

The machine-readable contract lives at `tools/material-maker/rogue-command-material-maker.json` and is validated by `tools/validate_material_maker_config.py`. It keeps one Material Maker export profile while routing the resulting images to the correct downstream workflow:

- **`urp`**: default. Produces the four maps for `RogueCommand/VehiclePBR_VertexWear`, then Unity 2021.3.45f2 imports them with the existing preview builder. This is the game-bound path.
- **`blender`**: produces the same image contract for Blender 4.5.14 preview/material inspection. Blender `.blend`, FBX, sockets, LODs and vertex wear remain authoritative; Material Maker never replaces the production mesh.
- **`three_d`**: supports a disposable UV-frozen FBX/GLB/GLTF/OBJ paint copy. The single-submesh limitation applies only to the Material Maker paint workspace; export images only and discard any Material Maker mesh export.
- **`procedural`**: graph-only Material Maker generation with no paint import. It still uses the same four-map packer and channel contract.

Examples:

```powershell
# URP/game-bound texture set (default) with preset and strict quality gate
powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow urp -Preset gunmetal_chassis -Strict

# Blender preview-oriented output
powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow blender -Preset desert_camo -Strict

# Disposable 3D-paint workspace (auto-generates UV-frozen single-submesh proxy + .ptex)
powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow three_d -UnitName SkyguardSAM -PaintSourceModel content/source-art/skyguardsam-rigged.blend -Preset hazard_industrial -Strict

# URP/game-bound output plus a disposable Blender 4.5 material preview
powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow urp -UnitName SkyguardSAM -Preset quantum_hyperdrive_alloy -BlenderPreviewModel content/source-art/skyguardsam-rigged.blend -Strict

# Procedural graph-only generation
powershell -File tools/Run-MaterialMakerBake.ps1 -Workflow procedural -Preset olive_armor -Strict
```

`-BlenderPreviewModel` is optional for `urp`/`blender` and creates a disposable `.blend` with the four maps bound to Principled BSDF. The `three_d` workflow automatically previews the supplied source `.blend` without modifying it; all preview blends stay under `artifacts/`. Use `-SizePower 11` for the validated 2048×2048 hero path. `-SizePower 12` is allowed only when the quality report confirms a real 4096×4096 export; the runner records requested versus actual resolution and strict mode fails a renderer cap mismatch.

For `three_d`, the generated Blender proxy-export snippet runs in Blender's embedded Python process and must import `sys` in that snippet before reading `sys.argv`. Verify the resulting paint manifest (`uvFrozen`, `singleSubmeshForPaint`, `paintProxyPresent`) and source SHA-256 before treating the proxy as ready; the production mesh remains untouched.

`tools/Prepare-MaterialMakerTacticalTemplate.py` supports a suite of 44 high-fidelity space-military presets (`--preset <name>`):
- **`olive_armor`**: Exo-Atmospheric Tactical Olive Armor (standard ground/orbital assault hull).
- **`gunmetal_chassis`**: Orbital Dreadnought Gunmetal Hull (slate-gray cold-rolled aerospace steel).
- **`desert_camo`**: Lunar/Martian Thermal Regolith Plating (high-friction dust-resistant ceramic alloy).
- **`hazard_industrial`**: Orbital Docking Bay EVA Hazard Plating (high-contrast industrial caution/docking slabs).
- **`orbital_titanium`**: Cryo-Titanium Spacecraft Armor (bright specular titanium alloy with laser-cut seams).
- **`stealth_void_composite`**: Void-Stealth Graphene Composite Plating (radar-absorbent ultra-dark dielectric body, exposed precision titanium CNC chamfers, razor seams).
- **`solar_gold_radiator`**: Orbital Solar Gold Radiator Shielding (24K gold-titanium thermal dissipation foil, high specular gloss, dark beryllium frame brackets).
- **`battleship_tungsten`**: Heavy Dreadnought Ballistic Tungsten Armor (monolithic tungsten-carbide kinetic defense slabs, shot-peened finish, deep ballistic chamfers).
- **`cerberus_white_ceramic`**: Fleet Interceptor Arctic Ceramic Plating (sintered zirconium-boride thermal barrier tiles, high solar reflectance white, dark vacuum expansion joints).
- **`plasma_cinder_ablative`**: Re-Entry Sintered Carbon-Phenolic Heatshield (pyrolyzed carbon-carbon heatshield, burnt-copper/amber thermal oxide seam margins).
- **`cyber_nanoweave`**: Electronic Warfare High-Density Nano-Lattice (high-frequency nano-patterned stealth meta-material, cyan/slate dielectric plates, conductive waveguide traces).
- **`anodized_cobalt`**: Anodized Cobalt-Titanium Stealth Hull (vacuum-anodized deep indigo-navy alloy, low-IR body, specular blue-violet edge relief).
- **`quantum_hyperdrive_alloy`**: Quantum Resonance Super-Alloy Plating (tachyonic drive housing, specular nickel-chromium silver, subtle refractive violet flux margins).
- **`beryllium_mirror_hull`**: Exo-Atmospheric Lightweight Beryllium Optical Shielding (high-reflectance beryllium-palladium mirror plates, razor seams, laser alignment indicators).
- **`obsidian_strike_interceptor`**: Deep-Space Interceptor Carbon-Graphite Hull (diamond-like carbon DLC obsidian hull, precision CNC titanium bevels, tactical cyan HUD edges).
- **`fusion_reactor_core_plating`**: High-Energy Magnetic Confinement Armor (copper-tungsten and sintered bronze tokamak containment slabs with thermal relief seams).
- **`martian_iron_rust`**: Colonial Expedition Heavy Ferrous Plating (oxidized hematite iron plating, shot-peened finish, thermal hazard warning edges).
- **`neosteel_command_citadel`**: Flagship Reinforced Heavy Neosteel Armor (prestigious cold-rolled naval neosteel slabs, mirror-beveled chamfers, command bridge blue accents).
- **`antimatter_containment_lattice`**: Void Vacuum Antimatter Superconducting Tiles (superconducting cryogenic ceramic-metal matrix tiles, Cherenkov violet flux channels).
- **`cryo_glacier_camo`**: Europa Sub-Zero Cryogenic Camouflage Armor (sub-zero arctic frost titanium, pale turquoise pulse, low-dissipation thermal seams).
- **`hypervelocity_railgun_cermet`**: Hypervelocity Railgun Cermet Armor (dense ceramic-metal magnetic pressure slabs, copper induction traces, electric blue Lorentz flux seams).
- **`stealth_radar_absorbent_ferrite`**: Stealth Microwave Absorbent Ferrite Tiles (manganese-zinc ferrite RAM tiles, matte basalt finish, radar-scattering faceted chamfers).
- **`solar_flare_radiation_shield`**: Multi-Layer Coronal Radiation Shielding (lead-tantalum and polyimide radiation blankets, reflective amber-gold foil, tritium warning alerts).
- **`ion_thruster_titanium_grate`**: Anodized Ion Thruster Titanium Baffles (heat-tempered titanium exhaust baffles, iridescent violet-blue tempering, xenon glow accents).
- **`nanite_repair_chitin`**: Biomechanical Nanite Swarm Chitin (hexagonal interlocking carbon micro-scales, emerald nanite repair nodes, self-healing substrate).
- **`tungsten_heavy_flak_plating`**: Sintered Tungsten Anti-Fragmentation Flak Armor (shot-peened monolithic tungsten carbide plates, orbital debris mitigation, yellow hazard bevels).
- **`zero_g_docking_alloy`**: Orbital Shipyard Zero-G Docking Alloy (electroless nickel-phosphorus clamp plates, micro-milled machine grooves, amber docking beacon edges).
- **`plasma_arc_containment`**: High-Voltage Dielectric Plasma Arc Barrier (boron nitride and beryllium oxide ceramic insulators, high-voltage electric violet ionization seams).
- **`tachyon_warp_radiator`**: Tachyon Warp Drive Thermal Radiator Fins (deep chromatic vacuum radiator vanes, high metallic polish, cyan-violet Cherenkov flux seams).
- **`graphene_ablative_lattice`**: Hexagonal Graphene Matrix Ablative Tiles (ultra-hard multi-layer aerospace graphene honeycomb tiles, emerald nanite trace channels).
- **`dreadnought_adamantine_hull`**: Superheavy Dreadnought Adamantine Citadel Plating (monolithic naval battlecruiser armor, titanium carbide welds, amber telemetry markers).
- **`void_stalker_metamaterial`**: Optical-Cloaking Metamaterial Phase Shingle (phase-shifting radar/lidar refraction shingles, sleek dark chrome, ultraviolet spectral seams).
- **`orbital_drop_pod_cermet`**: Atmospheric Re-entry Kinetic Cermet Tiles (extreme thermal shock silica ceramic-metal tiles, scorched terracotta finish, superheat incandescence).
- **`pulsar_magnetic_shield`**: Pulsar Magnetosphere Induction Armor (superconducting flux-compression plates, bronze-beryllium alloy, high-voltage plasma magenta glow).
- **`exoskeleton_reactive_scale`**: Articulated Combat Walker Reactive Scales (overlapping dynamic chassis scales, matte olive/titanium composite, tactical laser-gold bevels).
- **`cryogenic_fuel_shroud`**: Cryogenic Liquid Hydrogen Tank Vacuum Shroud (specular multi-layer vacuum insulation panels, aerospace silver mirror finish, cryogenic cyan seams).
- **`subterranean_mole_borer`**: Planetary Crust Kinetic Tunneling Plating (tungsten-carbide facets, abrasive regolith wear, seismic amber beacon seams).
- **`longshot_sniper_camo`**: Anti-Materiel Sniper Optical Shroud Plating (anti-glare carbon-fiber shroud, optical sensor housing, ballistic olive/charcoal mitered plates).
- **`minefield_ordnance_carrier`**: Area-Denial Combat Minelayer ERA Plating (reinforced V-hull blast deflection plates, explosive reactive armor cassettes, hazard amber seams).
- **`gravitas_singularity_chassis`**: Graviton Singular Flux Distortion Armor (dense singularity gunmetal, graviton flux ring conduits, intense electric violet-amber seams).
- **`arc_discharge_conduit`**: Directed-Energy High-Voltage Arc Armor (high-conductivity copper-beryllium busbars, ceramic dielectric insulators, cyan Lorentz arc discharge seams).
- **`combat_medic_bioceramic`**: Autonomous Field Triage Bioceramic Plating (sterile white bioceramic panels, surgical titanium brackets, medical emerald diagnostic beacons).
- **`salvage_reclamation_ironclad`**: Heavy Battlefield Logistical Recovery Armor (cold-rolled salvage steel, crane counterweight brackets, high-contrast industrial warning seams).
- **`orbital_phased_array_comm`**: Deep-Space Phased Array EW Superstructure (gold-titanium radar array micro-tiles, high-frequency waveguides, quantum telemetry cyan uplink beacons).

All presets feature laser-cut seams, precision chamfers, isotropic satin grain, a bimodal `colorize_metallic` node, an edge-only `edge_detect_emission` → `colorize_emission` graph, and the `tactical-hd-v2` `micro_perlin` → normal/roughness detail stack connected to the Material Maker emission/material ports with preset-specific sensor/thermal colors. `tools/pack_material_maker_pbr.py` renormalizes tangent-space normal maps per pixel and applies gentle PBR-safe micro-contrast on albedo. `tools/prepare_material_maker_3d_paint.py` builds the disposable single-submesh UV-frozen `<Unit>_PaintProxy.obj` under `artifacts/texture-work/<Unit>/MaterialMaker/` without touching the production FBX, records the source SHA-256, and fails closed when Blender 4.5.14 cannot create a required proxy. `tools/apply_material_maker_to_blender.py` binds the four-map contract to a Principled BSDF node setup in Blender 4.5 preserving sockets and vertex wear. The runner clears stale raw/final outputs and passes the graph/workspace paths to `tools/audit_material_maker_output.py`, which verifies micro-detail, emission, UV-frozen proxy, and source-preservation metrics.

The procedural graph checks confirm required nodes, connections, and map properties; they do not establish that the material reads well on a unit. Inspect generated emission coverage and normal/albedo frequency on the actual UV model at RTS scale. A technically valid repeated panel graph can still create too much edge glow or mottling; keep that result as a candidate until it passes the visual gate.

## Unit-to-Preset Integration Across All Content Packs

All 44 presets are mapped and integrated across all 19 units across `sample-expansion`, `sample-native-expansion`, and `tactical-operations` via `tools/UnitPbrPresetRegistry.py` and batch execution tooling `tools/Apply-UnitPbrPresets.py`. Each unit declares its primary battlefield chassis preset and specialized tactical variant presets (`visualVariants`) in `content.json` conforming to `namespacedId` and `unitVisualVariant`:

- **Audit completeness**: `python tools/Apply-UnitPbrPresets.py --audit` asserts that 100% of all 44 presets are mapped to mod units with 0 unmapped presets.
- **Inspect unit matrix**: `python tools/Apply-UnitPbrPresets.py --list` formats the complete matrix of 19 units, their source art, primary presets, and variants.
- **Batch template generation**: `python tools/Apply-UnitPbrPresets.py --all --prepare-templates` prepares valid `.ptex` graphs for all units and variants.
- **Integration manifest**: `python tools/Apply-UnitPbrPresets.py --export-json <path>` emits structured integration telemetry.

All workflows write an `material-maker-workflow.json` manifest beside the output maps. The manifest records the workflow, shader, pipeline, source authority and post-process rule for agent-readable handoff.

## Pack report and quality gates

`tools/pack_material_maker_pbr.py` re-saves Albedo/Normal/Emission as opaque RGB, caps smoothness at 1 - 0.04 (roughness floor, anti-aliasing at RTS distance) and writes `<Name>_pack-report.json` with per-map stats and warnings (flat/blank export, non power-of-two, non-unit normals, implausible albedo, mid-tone metallic, over-bright emission). Pass `--strict` to fail (exit 2) on warnings. Shader changes (weld normal tilt, mud breakup, specular AA, softened cavity occlusion, aero-alloy glancing sheen) are local authoring changes and must be compiled/verified in Unity 2021.3.45f2 before being called validated.

## Compatible finish and microdetail recipes

Read [recipes 1, 2 and 6](../rogue-command-pbr-texture-authoring/references/compatible-texture-recipes.md) for existing graph controls and the scalar-to-four-map path. Use a task-owned `.ptex`, an existing `-TemplatePath` and an isolated `-OutputDirectory`. Output names follow the template stem, not `-UnitName`. Never use `-ForceTemplate` on an edited graph, and keep paint originals outside the runner's cleared output directory.

Palette/response curves and the `micro_perlin` normal/roughness stack are supported controls; model-aware material classification is not automatic. Preserve the audited graph wiring and quality metadata. The audit requires the tactical profile and positive graph emission energy; report a conflicting finish rather than adding artificial glow. Neither passing those checks nor normal-vector renormalization proves surface quality or correct normal orientation.

The `three_d` path prepares the proxy and procedural images; it does not automate strokes or validate arbitrary painted-project CLI export. Use only a separately verified manual image export for subsequent painting, and distinguish its final-image review from the baseline graph audit.

## Validation handoff

Run [Rogue Command PBR Texture Validation](../rogue-command-pbr-texture-validation/SKILL.md). Inspect image dimensions, UV coverage, seams, normal convention, and the MetallicGloss alpha channel, then verify effective Unity importer settings after `VehiclePrefabBuilder` reimports the maps. Confirm the original imported mesh still carries RGBA vertex wear and that the prefab uses the project's actual shader and keyword.

## References

- [Material Maker official site](https://materialmaker.org/)
- [Material Maker 1.7 Windows release](https://github.com/RodZill4/material-maker/releases/tag/1.7)
- [3D paint project requirements](https://rodzill4.github.io/material-maker/doc/user_interface_paint_panel.html)
- [Texture and material export documentation](https://rodzill4.github.io/material-maker/doc/export.html)

## Canonical Level 2 Module Texture Set

- `tools/Build-CanonicalVisualModuleTextures.ps1` bakes the 28 shared Material Maker 1.7 procedural presets used by the 57 Level 2 modules. Run the pinned `tools/Run-MaterialMakerBake.ps1 -Workflow procedural` path with strict quality checks; each preset produces the four named 2048x2048 maps required by the module builder.
- These are 28 shared preset material sets, not 57 independent texture families. Keep the generated module FBXs authoritative, export images only, and leave working files/reports under the fresh ignored `artifacts/canonical-level2-<UTC>/` build root until the Unity builder consumes them.
- Successful strict reports establish map-generation evidence. `CanonicalVisualModuleBuilder` must still confirm imported maps, shader/material bindings, mesh vertex colors, and AssetBundle contents in Unity.
- When promoting a generated map, compare its SHA-256 with the existing Unity texture first and leave byte-identical files untouched; this avoids unnecessary writes and preserves stable asset imports. Replace only maps whose validated bytes differ.
- `tools/Build-CanonicalVisualModules.ps1` runs Blender generation, this strict 28-preset bake, Unity import/build, and bundle validation in order. Each run records its source fingerprint and output hashes in `artifacts/canonical-level2-<UTC>/build-manifest.json`; packaging rejects artifacts after their inputs or staged bundles change. The current build was authorized with an uncalibrated status; its reports must not be presented as performance validation.

## Bulwark foundry material

`content/source-art/bulwarkfoundry-material.ptex` reproduces the four 2048px maps. Its compact amber emission mask is reserved for warning-light UVs; the ordinary surfaces use a non-emissive atlas region. The output passed technical checks, but the Blender preview is not evidence of in-game lighting.
