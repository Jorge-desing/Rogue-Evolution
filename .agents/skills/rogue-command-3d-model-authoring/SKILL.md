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

## 3. Stylized Realism & Form Design

Target **grounded, stylized military sci-fi realism**: believable mass, connected mechanisms, distinctive silhouettes, and materials that respond coherently, while preserving Rogue Command faction identity and RTS readability. Do not pursue photorealism, and do not carry a primitive blockout into the final asset unchanged.

### 3.1 Shape first, detail second
- Define the role, mobility, weapon/system, and two or three silhouette cues before modeling. Gather references for proportions, construction, and motion; use multiple views to infer how major assemblies connect and move. Use them to inform an original design rather than copying one vehicle or imposing one real-world archetype on every unit.
- Establish the primary shell and stance first. Use tapered profiles, lofted or superellipse sections, controlled curvature, sculpted transitions, and plate facets where the material or fabrication calls for them. Keep a box or cylinder when it represents a real plate, axle, barrel, tank, joint, or hidden support; replace repeated primitive stacks when they define the visible body.
- Assign each large form a construction role before detailing it: formed or cast shells can use controlled radii; fabricated armor can use separate plates, brackets, and credible joints; machined mechanisms can use precise cylindrical or prismatic sections. These are design cues, not mandatory real-world standards. Avoid giving every assembly the same edge language.
- After booleans or dense panel cuts, inspect the surface under a moving grazing highlight from several views. Fix pinching, stepped curvature, and unintended flat spots by reshaping or retopologizing before UVs and atlas work; bevels and weighted normals cannot repair those defects.
- Build secondary forms around visible function: load-bearing mounts, mobility, articulation, recoil clearance, cooling, sensing, or production flow. Add tertiary fasteners, seams, warning marks, hoses, and optics only when they clarify scale or construction at the target camera distance.
- Give each archetype a deliberate rhythm of broad masses, intermediate panels or mechanisms, and sparse small accents. Avoid repeating the same module, bevel width, seam spacing, or bolt pattern across every surface; variation should follow assembly, access, protection, or faction identity.
- Use the large/medium/small hierarchy as a composition aid. The 70/20/10 and 70/30 splits are optional visual heuristics, not measurable acceptance thresholds or universal quotas.
- Treat procedural output as a functional blockout. A generated shell is not production-ready until its cross-sections, transitions, intersections, material breaks, and silhouette have been reviewed and deliberately refined.

### 3.2 Mechanical plausibility by archetype
Choose only the engineering cues needed by the unit's role. A tracked tank may need a readable track loop and suspension; a bike needs a coherent wheelbase, fork, frame, and payload attachment; a walker needs supported limbs, joints, and foot contact; a hovercraft needs lift/propulsion cues; a factory needs a legible structural and material-flow path. Do not require every combat unit to have ballistic armor, a V-hull, ERA, APS, crew hatches, or human-scale service hardware.

For each prominent mechanism, ask what supports it, what moves or carries energy through it, what clearance it needs, and how it attaches to the larger body. Add visible cables, cooling, maintenance panels, or reinforcement where they answer those questions; leave them out where they would be decorative noise. Real-world engineering standards are references for applicable machinery, not blanket certification claims for fictional assets.

### 3.3 Originality and RTS readability
- Differentiate units with outline, stance, proportion, negative space, weapon placement, and one or two role-specific shapes. Preserve the current unit's identity while improving its construction; new detail must not make adjacent unit classes harder to distinguish.
- Run a roster-divergence pass before detailed modeling. Compare the current unit with its closest native/mod counterpart and record the inherited body-plan pattern and the role cues that must remain readable. If the blockout still follows a generic or stock pattern, sketch at least two structurally different silhouette blockouts derived from its mobility, weapon, energy/cooling, or production needs. Choose a design whose visible supports and clearances agree with its function; state the thesis as role constraint → shape choice → functional evidence. This is a local design method, not a universal industry quota.
- Create novelty through primary massing, stance, negative space, articulation, and support layout. Surface noise, emissive outlines, spikes, or piles of add-on parts do not count as a new body plan.
- Review a black silhouette and a neutral clay model from the elevated camera and front/side/top. Test at actual on-screen sizes. Enlarge only the features that disappear, and remove detail that turns into noise.
- Use believable asymmetry, layered armor, exposed mechanisms, and distinct material transitions when they support a design story. Do not force one shared tank-like silhouette across bikes, walkers, hover units, aircraft, and facilities.

### 3.4 Surface and detail discipline
Keep broad forms calm enough to read. Assign visible outline, section curvature, gaps, articulation, and silhouette-critical edges to geometry. Use the existing maps for material identity and markings, and reserve normal-map relief for small details that remain legible at RTS scale. Any high-to-low bake idea is conditional on the mandatory Material Maker texture workflow and the current four-map import contract; it must not become an alternate final-map pipeline. Use selective bevels and custom normals to describe edge construction; neither a bevel nor weighted normals can repair a weak silhouette. Let color, metallic response, smoothness, and wear describe the material and its use; avoid uniform grunge or high-frequency noise used to disguise generic geometry. Give intact paint, exposed metal, rubber, glass, and emissive hardware distinct responses: painted or non-metal regions should not inherit the same high metallic value as bare metal, and smoothness should follow the material finish. Keep MetallicGloss.R as metallic and A as smoothness under the current shader/import contract. Place wear where contact, exposed edges, exhaust, or maintenance plausibly produce it, and preserve the shader's existing vertex-wear channels. Reserve emission for actual lights, displays, or energy features; do not outline every armor plate with glow. Judge the textured asset under neutral grazing light and at RTS scale, not only in a close-up beauty render.

### 3.5 Realism review gates
- Review a black silhouette from the elevated RTS camera and front/side/top before adding fine detail. Compare against adjacent unit classes at their actual on-screen sizes.
- Show a side-by-side black-silhouette comparison with the nearest existing counterpart. Confirm the design changes its body-plan language while keeping the role cues that let players identify it quickly; return to massing if it reads as a stock copy or loses its gameplay role.
- Do a rough articulation or motion-clearance pass while the model is still a blockout. Once the model and team-color preview are available, compare them against several representative terrain values, including a background close to the unit's main hue; adjust value grouping or color placement if the unit disappears. This is an authoring-preview check, not proof of in-game faction tinting.
- Review neutral clay under grazing light. Check that armor facets, cast curves, load-bearing joints, weapons, and mobility systems remain distinct and physically connected; remove both primitive stacks and oversized smooth blobs.
- Compare clay and textured passes: the clay pass must carry the form by itself, while PBR variation should clarify fabrication, material, and use without changing the silhouette or masking weak transitions.
- Keep technical texture validation separate from visual acceptance. Inspect the exact candidate maps on the target UV model under neutral light and at RTS scale; reject debug overlays, obvious tiling, harsh high-frequency noise, or palette shifts before promotion even if the file and channel checks pass.
- Check that material boundaries follow actual parts or coatings, and that metallic, smoothness, and emission variation describe those materials instead of tracing every procedural edge or covering the whole vehicle.
- Check a representative pose or motion envelope for moving weapons, hatches, legs, wheels, or suspension. Correct clipping and unsupported parts before atlas work.
- Run a physical-contact pass from front, side, and top views. Exposed plate faces, barrels, wheels, and housings must not visibly pass through neighboring surfaces; allow overlap only at concealed mounts, sealed joints, or articulated interfaces. Confirm that the contact remains plausible at the RTS camera scale.
- Preserve visual quality while reducing faces: remove exact duplicates and hidden internals first, then collapse redundant bevel subdivisions or geometry that is sub-pixel at the gameplay camera. Never remove visible armor or functional parts solely to meet the cap; redesign or reposition parts that intersect. Protect silhouette, section changes, weapon and locomotion function, recognizable hardware, UVs, vertex-wear RGBA, and authored shading. Recheck neutral clay, silhouette, grazing light, and textured views after each reduction; do not accept count-only decimation.
- If LODs are authored, inspect silhouette, role cues, and transition stability at the actual screen-size thresholds. Remove sub-pixel details before they create noise or flicker.
- Keep before/after previews and record Blender faces, Unity-exported triangles, bounds, and socket/rig changes separately. A preview is local authoring evidence, not proof of runtime appearance.
- See [Design Guides Adapted](references/design-guides-adapted.md) for the cited process and acceptance checklist.

### 3.6 Canonical Two-Level Visual Variant Boundary
- **Level 1 — Primary Chassis:** The Blender-authored mesh, silhouette, functional assemblies, rig, pivots, canonical sockets, and baked PBR maps remain the geometry authority for the unit.
- **Level 2 — Tactical Variants:** `visualVariants` apply their preset label, tint, and optional whole-root scale to the Level 1 chassis. They may also mount declared visual-only hardpoint modules through the runtime module registry. Modules add localized equipment geometry while the primary chassis, gameplay controller, rig, and unit identity remain authoritative.
- A Level 2 module is explicit only when the variant contains a `modules` entry with a supported `slotId` and resolvable `assetName`. Supported slots are `ERA`, `APS`, `Sponsons`, and `FuelDrums`; modules load from the owning unit's `assetBundleId` and mount at the existing canonical hardpoint group. A preset, tint, or scale alone never implies a new assembly.
- Keep module prefabs script-free and do not use them to replace the primary chassis or its functional hierarchy. Treat `visualAssetName` and `visualRootPath` as declarations unless their separate overlay path is explicitly authored and validated. Diagnostics report module application separately from tint-and-scale effects.
- A requested primary silhouette, rig, socket, collision, LOD, or functional-assembly change belongs in the authored Level 1 source and needs its full model/FBX/PBR/bundle validation. A Level 2 equipment change belongs in a separately authored module and must preserve the same chassis and controller.

---

## 4. Selective Edge Treatment & Normals

Bevels, smoothing, and weighted normals are shading tools that support the authored forms. They do not replace tapered shells, changing cross-sections, planned intersections, or a readable silhouette. Use flat shading on deliberate armor facets, smooth shading on formed or cast surfaces, and split/weighted normals when they preserve authored planar faces.

### 4.1 Bevel only where the part needs it
- Scale the bevel to the component and its visible edge. Small hardware may need a narrow chamfer; a cast housing may need a broader radius; an already-shaped loft may need no extra bevel.
- Use one segment for a crisp plate break. Add two to four segments only where a rounded radius is visible in silhouette or highlight. Avoid uniform bevel widths on adjacent parts made by different processes.
- ANGLE and WEIGHT limit methods can help select edges; check for overlaps on acute corners and inspect the evaluated mesh.
- Review the neutral clay model under grazing light. If the body still reads as boxes, redesign its sections and transitions instead of increasing bevel segments.

### 4.2 Normals and smoothing in Blender 4.5.14
- Choose flat or smooth shading per surface. Smooth curved barrels and formed shells; preserve designed breaks between armor plates.
- Use the repository's angle-based smoothing and weighted-normal helpers where they improve broad-face shading. Weighted normals should stabilize an authored shape, not hide a lumpy or faceted silhouette.
- The project helper apply_weighted_normals can be used directly or during material batching. Inspect the evaluated mesh after batching because joined material meshes can alter shading boundaries.

### 4.3 Unity FBX normals and tangents
- Keep the project's FBX export contract: mesh_smooth_type="FACE", use_mesh_modifiers=True, and bake_space_transform=False.
- Preserve authored normals through the Unity Model Importer with Normals set to Import, and use the configured MikkTSpace tangent path for normal-mapped assets. Verify effective importer settings after Unity reimports the FBX; do not assume a generic preset matches the project.
## 5. Project-Adapted FOSS & Advanced Toolchain

To ensure zero vendor lock-in, zero cost, and seamless integration with Rogue Command's engine pipeline:

### 5.1 Procedural PBR Texturing: Material Maker 1.7 (Mandatory FOSS)
- Node-based procedural PBR authoring built on Godot engine. Replaces proprietary Substance Designer.
- Automated pipeline runner via `tools/Run-MaterialMakerBake.ps1 -Workflow urp`.
- Outputs strictly 4 PBR texture maps:
  - `<Name>_Albedo.png` (sRGB, base color + macro dirt)
  - `<Name>_MetallicGloss.png` (Linear, RGB = Metallic, Alpha = Smoothness $1 - \text{roughness}$)
  - `<Name>_Normal.png` (Normal map, tangent-space MikkTSpace)
  - `<Name>_Emission.png` (sRGB, HDR optic and energy glow)
- *Strict Invariant:* Material Maker outputs PNG textures only; never import its meshes or `.mat` files into Unity.

### 5.2 UV layout and optional Blender add-ons
- Use the repository's Blender 4.5.14 scripts and verified UV/bake workflow as the reproducible path. Do not make DreamUV, BagaPie, JMesh, or another third-party add-on a project dependency unless its license, Blender 4.5.14 compatibility, installation path, and reproducible output have been checked for the task.
- For repeating structural elements, prefer the current kit's procedural helpers or Blender built-ins. UV trim sheets are optional and must not replace the unit atlas, alter the canonical hierarchy, or introduce unsupported material channels.

### 5.3 Specialized texture skills
- Material Maker 1.7 is the required image-authoring tool for mod PBR texture workflows; follow [the local Material Maker skill](../rogue-command-material-maker-texture-authoring/SKILL.md), then validate against [the Rogue Command PBR texture contract](../rogue-command-pbr-texture-authoring/SKILL.md).
- ArmorPaint is an optional, texture-only art pass. blendsmith and the adapted Blender 4.5 PBR skill are references for workflow ideas; local Rogue Command contracts take precedence. Keep the production Blender FBX, UVs, hierarchy, sockets, LODs, and Color RGBA vertex wear authoritative.

### 5.4 Unity URP Rendering & Optimization
- Do not assume a Decal Renderer Feature, D-Buffer configuration, or generic URP Lit detail inputs are enabled in this Unity project. Confirm the renderer asset and shader before relying on projectors or secondary maps.
- The project's RogueCommand/VehiclePBR_VertexWear shader has its own micro-detail normal inputs. Verify the shader properties and actual material wiring before authoring to them; do not add texture maps or channels outside the established four-map contract.
- Treat 10,000 Blender source faces as the strict project maximum for LOD0, not a quota or a Unity limit. Record source face counts and exported Unity triangles separately.
- Build LODs around screen-space silhouette and function. Remove hidden and sub-pixel detail first; preserve role-defining equipment, joints, and hard/soft shading boundaries.
- `RogueVehicleBuilder.build_and_export` and `RogueStructureBuilder.build_and_export` apply `RogueModelKit.reduce_subpixel_details_to_budget` before final quality and LOD0 budget checks. Preset refinements that simplify bevel detail use the same authored-data guards. Direct source builders call the shared gate before skinning and vertex-color baking; common `batch_and_join_by_material` and `merge_meshes_by_material` paths repeat the strict check before export. The pass dissolves redundant splits only on small fittings, uses UV discontinuities as limits, and skips meshes with authored Color data, vertex groups/armature modifiers, custom normals, material splits, or explicit sharp/seam edges. It prioritizes fasteners, welds, lugs, shackles, and hinges, and protects primary hulls, weapons, sensors, optics, wheels/tracks, articulation, and other role-defining systems. Every proposed dissolve is checked for symmetric sampled surface displacement, accumulated across passes, and rejected above 0.02 m. A bounded second stage reaches eligible details up to 1.2 m; if the model remains over 10,000, export fails for model-specific redesign. Any exporter that bypasses these shared paths must call the same helper and enforce the cap before export.
---

## 6. Palette and material response

Use faction color, value grouping, and material response to separate armor, machinery, rubber, optics, and warning accents. Tune each asset to its role, atlas, and gameplay lighting; there is no fixed five-tone count, coverage percentage, or universal metallic/smoothness value. Consult the current generator and PBR preset registry for available names instead of relying on a copied list.

| Material role | Typical visual cue |
|---|---|
| Armor | Faction base color; broad, readable panels with role-specific wear |
| Machinery | Dark alloy or exposed metal on joints, axles, actuators, and weapon housings |
| Rubber / flexible parts | Low-metal response on tires, pads, seals, and gaiters |
| Warning accents | Localized safety or unit-identification marks |
| Optics / energy | Small emissive regions with controlled intensity |

Camouflage and procedural variation must support panel layout and unit recognition, not obscure them.

---

## 7. 4-Channel Vertex Shading Contract (`VehiclePBR_VertexWear`)

Stored in `Mesh.colors32` (RGBA) for consumption by `RogueCommand/VehiclePBR_VertexWear` shader:

| Channel | Effect | Value Range | Behavior & Shader Decoding |
|---|---|---|---|
| **R (Red)** | Cavity AO / Dirt | 0.25 – 1.0 | $1.0$ on open/zenith surfaces; down to $0.25 - 0.40$ in wheel wells & undersides |
| **G (Green)** | Curvature / Edge Wear | 0.0 – 1.0 | Strictly $0.0$ on flat/concave faces; $0.7 - 1.0$ on sharp convex edges ($\ge 35^\circ$). Triggers two-tier chip (oxidized primer `#2c2724` at $0.12-0.42$, bright steel `#c7ccd6` at $>0.42$) |
| **B (Blue)** | Thermal Soot | 0.0 – 1.0 | $0.0$ baseline; scales to $1.0$ at muzzles/exhausts. Suppresses specular smoothness (`× (1 - soot*0.65)`) and metallic (`- soot*0.40`) |
| **A (Alpha)** | Team Tint Mask | 0.0 or 1.0 | $1.0$ on primary/light armor plates for `_TeamColor`; $0.0$ on mechanical gunmetal and treads |

---

## 8. Generator Architecture (generate_unit_model.py and RogueModelKit)

- Treat the procedural CLI and RogueModelKit as a fast, repeatable blockout and construction toolkit. Refine each asset primary form and functional connections before export; a preset name or successful generator run is not a visual acceptance check.
- Read python tools/generate_unit_model.py --help and the relevant builder source for current archetypes, flags, and outputs. Treat the code and tools/UnitPbrPresetRegistry.py as the live source of truth; do not copy evolving counts or preset lists into this skill.
- For primary shells, use purpose-built profile stations, including make_lofted_superellipse_shell where appropriate, selective edge treatment, and per-surface shading choices. Preserve explicit moving components and pivots instead of smoothing across mechanical boundaries.
- Keep DEFAULT_FACE_BUDGET = 10000 as the strict project LOD0 source-face maximum. Review Blender faces and exported Unity triangles separately; do not add geometry merely to fill the budget. Vehicle, structure, direct-source, and rigged export paths use `RogueModelKit.reduce_subpixel_details_to_budget` before batching and reject any model that remains over the cap. Run it before assigning skin weights or baking `Color` RGBA; already-authored data-bearing meshes are protected. The safe dissolve gate limits accumulated sampled surface displacement to 0.02 m while preserving primary silhouettes, weapons, sensors, movement systems, sockets, pivots, UV seams, authored shading, and mesh Color RGBA. Keep the face-reduction diagnostics and pass status in the report JSON. Redesign a model-specific overage when the safe pass cannot meet the cap; never force a count-only decimation.
- Before export, validate scale, ground contact, bounds, materials, UVs, Color RGBA, rigs, sockets, and LODs against the relevant local skills and builders.

---
## 9. Procedural Animations & Procedural Action Clips

- **Action Clips Contract:** `Action_Idle`, `Action_Walk`/`Action_Drive`, `Action_FireRecoil`, and `Action_Death`.
  - `Action_Idle`: Peaks at f15 with f8/f22 easing; never keyframes `Bone_Root`.
  - `Action_Death`: 12% settle at f8, 50% collapse at f16, full collapse at f28, holding to completion.
- **Armature Hierarchy:** `Bone_Root` $\rightarrow$ `Bone_Hull` $\rightarrow$ `Bone_TurretYaw` $\rightarrow$ `Bone_TurretPitch` $\rightarrow$ `Bone_BarrelRecoil`.
- **Rigid Weighting:** 1.0 per armor plate on unbatched source meshes to prevent rubbery mesh deformation.

---

## 10. Socket-to-Gameplay Runtime Binding Map (`RuntimeVisualDirector`)

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

## 11. Template Dimensions & Native Hierarchy Alignment

| Native Template | Sample Unit | `visualRootPath` | Collider Dimensions (W × H × L) |
|---|---|---|---|
| `PCXBaseTank` | Sandlance Scout | `"Vehicle"` | ~2.4m × 1.6m × 2.8m |
| `BombBike` | Skirmish Strike Bike | `"GameObject/BombBike"` | ~1.2m × 1.4m × 2.2m |
| `ArtilleryTank` | Colossus Siege Platform | `"Vehicle"` | ~2.8m × 1.9m × 3.6m |
| `GatlingWalker` | Ironclad Walker | `"Vehicle"` | ~2.2m × 2.7m × 2.0m |
| `LightningTank` | Surge Arc Tank | `"Vehicle"` | ~2.3m × 1.6m × 2.8m |

---

## 12. Material Maker Texture Runner & Card Sprite Rules

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

## 13. Rigged Armature & Facility Realism Lessons Learned

### 13.1 Rigid Weight Rebinding on Authored Armatures
When authoring or updating articulated walker/quad-mech sources with pre-existing armatures (e.g. `BastionSiegeMech` or `AtlasCraneMech`), adding detail geometry or wave passes (lifting lugs, coamings, or ERA brackets) introduces new mesh objects lacking vertex group bindings.
- **Invariant:** Always call `rmk.assign_rigid_mesh_weights(meshes, prefix=UNIT_NAME, armature_obj=armature, leg_type="quad"|"biped")` explicitly before running `validate_rigid_weights(meshes, armature)`.
- If new detail passes (`apply_wave2_details`, `apply_wave34_details`) are appended, re-run `assign_rigid_mesh_weights` so every appended object receives its rigid 1.0 weight on the appropriate bone (`Bone_Hull`, `Bone_Pelvis`, `Bone_TurretYaw`, etc.) before batching and export.

### 13.2 Industrial Facility Construction Cues
When generating or refining production facilities, use engineering assemblies only when they support a visible process or service route:
- `add_process_piping_manifold`: flanged piping, elbows, and gate valves where they support visible process flow.
- `add_pressure_vessel`: storage vessel with formed ends and stiffeners where its scale and use make them legible.
- `add_gantry_crane_trolley`: overhead gantry with a readable hoist, cable, hook, and end stops.
- `add_caged_ladder`: access ladder, cage, and landing when the service route reads at gameplay scale.
- Treat these as visual construction cues for fictional assets; do not claim code or safety-standard compliance unless separately verified.

---

## 14. References & Detailed Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — Proportions, silhouette tests, and faction palettes.
- [Design Guides Adapted](references/design-guides-adapted.md) — Research-backed blockout, form-refinement, motion, and RTS review gates.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — In-depth `bpy` recipes for tracks, bevels, and hulls.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — URP shaders, keyword toggles, and prefab builders.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy trees for native templates.
- [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) & [Validation](../rogue-command-pbr-texture-validation/SKILL.md).

## 15. Canonical Two-Level Visual System

- Preserve the Level 1 primary chassis as the complete engineering form: major structure, armor hierarchy, articulated or serviceable assemblies, and authored functional details stay legible as one coherent unit silhouette. Level 2 keeps that chassis and may add separately declared equipment modules; do not flatten the modules into replacement chassis meshes or clone the gameplay unit.
- Author module roots to mount at the canonical hardpoint origin and use the catalogued slots `ERA`, `APS`, `Sponsons`, or `FuelDrums`. Keep module geometry visually self-contained, with scale/offset inside its hierarchy; the existing hardpoint socket and attachment group own placement. Search for and reuse canonical sockets before creating transforms.
- `content/source-art/canonical-visual-modules.blend` and its JSON manifest are the generated source set for the current 35 modules. Use `tools/Build-CanonicalVisualModules.py` with the pinned Blender 4.5.14 toolchain to regenerate; preserve the existing primary unit source blends and the RGBA vertex-wear attribute.
- `content/source-art/canonical-visual-module-roles.json` is the authoring-only source for each generated module's `visualEngineeringRole`, keyed by variant and hardpoint. Keep visible mechanical role separate from the PBR preset; the preset changes surface treatment and never selects assembly geometry. Every role names a form with a plausible mount, service path, and load path, and the build rejects a role assigned to an incompatible slot.
- The Unity canonical builder cross-checks each pack module against the generated manifest and resolves the owning Level 1 prefab. Every hardpoint used by a Level 2 variant must resolve to exactly one matching `Socket_Hardpoint_<Type>` in that primary hierarchy; duplicate and noncanonical hardpoints fail the build.
- For a package-ready rebuild, run `tools/Build-CanonicalVisualModules.ps1`; it regenerates Level 2 module FBXs and shared PBR maps, refreshes the four Level 1 prefabs that currently back modules, validates their canonical hardpoints, then builds the Unity bundle outputs. It does not rebuild primary mesh geometry. If changing a primary socket, update `tools/CanonicalUnitHardpoints.py`, regenerate the primary FBX from its model builder, then refresh the Unity prefab; keep each hardpoint on a plausible armor/deck/side mounting surface.
- Inspect the labeled contact sheet for separation of silhouette, clearances, and attachment fit. A contact sheet is authoring review, not a live render check.
