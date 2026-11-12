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
- Establish the primary shell and stance first. Use tapered profiles, lofted or superellipse sections, controlled curvature, sculpted transitions, and plate facets where the material or fabrication calls for them. Keep cylinders, circular caps, and rings only where rotation, pressure containment, a bearing, axle, barrel, wheel, tank, exhaust, mast, or another named mechanical role explains the round section. Do not add concentric rings or repeated circular primitives as decoration; use lofted, superellipse, faceted, or chamfered sections for armor and housings that have no circular function.
- Assign each large form a construction role before detailing it: formed or cast shells can use controlled radii; fabricated armor can use separate plates, brackets, and credible joints; machined mechanisms can use precise cylindrical or prismatic sections. These are design cues, not mandatory real-world standards. Avoid giving every assembly the same edge language.
- After booleans or dense panel cuts, inspect the surface under a moving grazing highlight from several views. Fix pinching, stepped curvature, and unintended flat spots by reshaping or retopologizing before UVs and atlas work; bevels and weighted normals cannot repair those defects.
- Build secondary forms around visible function: load-bearing mounts, mobility, articulation, recoil clearance, cooling, sensing, or production flow. Add tertiary fasteners, seams, warning marks, hoses, and optics only when they clarify scale or construction at the target camera distance.
- Give each archetype a deliberate rhythm of broad masses, intermediate panels or mechanisms, and sparse small accents. Avoid repeating the same module, bevel width, seam spacing, or bolt pattern across every surface; variation should follow assembly, access, protection, or faction identity.
- Use the large/medium/small hierarchy as a composition aid. The 70/20/10 and 70/30 splits are optional visual heuristics, not measurable acceptance thresholds or universal quotas.
- Treat procedural output as a functional blockout. A generated shell is not production-ready until its cross-sections, transitions, intersections, material breaks, and silhouette have been reviewed and deliberately refined.

### 3.2 Mechanical plausibility by archetype
Choose only the engineering cues needed by the unit's role. A tracked tank may need a readable track loop and suspension; a bike needs a coherent wheelbase, fork, frame, and payload attachment; a walker needs supported limbs, joints, and foot contact; a hovercraft needs lift/propulsion cues; a factory needs a legible structural and material-flow path. Do not require every combat unit to have ballistic armor, a V-hull, ERA, APS, crew hatches, or human-scale service hardware.

For each prominent mechanism, ask four role-relevant questions: what carries its load or recoil; how power, energy, or fluid reaches moving parts; where heat is rejected when the system produces it; and how the system is inspected, serviced, or recovered when the unit's role supports human maintenance. Check its motion clearance and attachment to the larger body as well. Add visible cables, cooling, access panels, or reinforcement only where they answer those questions; leave them out where they would be decorative noise. Real-world engineering standards are references for applicable machinery, not blanket certification claims for fictional assets.

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
- Treat visible interpenetration as a pre-export failure. Run a physical-contact pass from front, side, top, and elevated RTS views, then inspect close views of every dense assembly. Exposed plate faces, barrels, wheels, housings, and their attachments must not pass through neighboring surfaces. Overlap is permitted only inside a concealed mount, sealed joint, or articulated interface; verify the full motion envelope so moving parts do not clip. Reposition, reshape, or create a real clearance gap before UVs or baking, then repeat the pass. Do not count a decorative trim ring or circular stack as a fix for a bad contact.
- For each visible round form, name its mechanical function and check that its axis and attachment support that function. Replace functionless circular armor details, concentric bands, and repeated cylinders with a shaped shell, plate, rib, or prismatic support.
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
- Retired the fixed 6,500 / 2,000 / 10,000-face rejection ceilings. `content/source-art/model-budget-profile.json` is the single policy input: it records PC/resolution/camera/scene metadata, Blender source faces, triangulated Unity geometry, visible-instance demand, screen footprint, role priority, and per-LOD transitions. Budgets are finite per asset and scene, derived from calibrated p95 unscaled frame-interval headroom. Blender faces remain separate diagnostics from exported Unity triangles. Older handoff or research sections that repeat those face values are historical and never override this profile. The checked-in local profile is uncalibrated; prefab/package builds fail closed until a real 60-FPS sample set and passing per-scene asset demands are recorded.
- Runtime calibration uses temporary `tools/RuntimeModelBudgetProbe.cs` source through BepInEx MCP, never the loader. It requires a display-backed `Game` camera, counts enabled in-frustum mesh triangles once, then captures at least 120 unscaled frame intervals. It checks time scale each frame and camera plus renderer/LOD signature every 30 frames; unstable captures are rejected. The count remains a conservative frustum estimate, not an exact GPU draw or occlusion query; frame intervals are realized pacing, not CPU/GPU timings. Bind production samples through a hashed `rogue-command/runtime-geometry-evidence/v1` bundle that identifies the exact Game scene, level, stage, mode, camera pose, resolution, and entity mix; the compiler verifies each raw v3 report's SHA-256. Do not ingest an intermission/menu capture as gameplay. A report's `calibrationReady` flag validates only the capture.
- If p95 frame intervals barely change across measured geometry loads, do not extrapolate a tiny regression slope into a huge triangle allowance. `conservative-measured-incremental-envelope-v1` may produce a candidate from 75% of the observed low-to-high triangle range with a 25% reserve and no extrapolation. Keep the profile `uncalibrated` when entities or buildings changed independently between those loads, when scene demands are missing, when demand counts exceed the authored visible-instance maximum, or when demand source faces/triangles/hash diverge from the FBX inventory. Current Level 4 follow-up captures are accepted evidence only; their 15-entity low load and 35-entity high load are not a controlled pair, so the builders remain closed.
- Builder safety: resolve the calibrated geometry budget before creating output directories, saving source blends, or writing generated assets. Replay accepted recipes in memory and write to the dedicated output path; a blocked build must not mutate the authoring source. Direct builders, `RogueVehicleBuilder`, and `RogueStructureBuilder` follow this order; `tests/python/test_model_builder_budget_gate_order.py` guards the direct builders, shared methods, and recipe-source preservation.
- FBX export safety: every `export_unity_fbx` call must either resolve its own calibrated profile with `generate_profiled_lods=True` or receive the matching resolved allocation as `prevalidated_budget` plus the expected `asset_id`. The shared exporter validates the 60 FPS verdict, passing scene/asset allocation, and three ordered LOD targets before creating the destination; atlas, damaged, and wreckage helpers validate before any output writes too. This is a fail-closed accidental-bypass guard, not a geometry-cap substitute.
- 2026-11-12 live measurement follow-up: two low-load v3 captures passed their technical gate (120,110 and 120,354 triangles, p95 12.124/12.125 ms). Three higher-load captures reached 328,924–330,460 triangles but failed `geometryLoadStable` and are not calibration samples. Do not relax or manually override the v3 signature gate; its report does not retain per-recheck triangle counts needed to justify aggregate stability. The profile remains uncalibrated; see [the calibration attempt report](../../../docs/research/DYNAMIC-GEOMETRY-CALIBRATION-2026-11-12.md).
- Build LODs around screen-space silhouette and function. Remove hidden and sub-pixel detail first; preserve role-defining equipment, joints, and hard/soft shading boundaries.
- All procedural, direct-source, structure, and canonical-module exports resolve `RogueModelKit.resolve_geometry_budget()` against a calibrated scene profile. Missing, uncalibrated, infeasible, or absent asset allocations fail closed; no fixed cap fallback exists. LOD targets use scene triangles as the runtime cost, with Blender faces reported separately. Safe sub-pixel cleanup still preserves authored Color, weights, normals, material boundaries, UV seams, sharp edges, and role-defining forms, and rejects changes above 0.02 m accumulated sampled surface deviation. LOD0 must satisfy the measured allocation after that cleanup; redesign the model when it does not. `tools/profile_model_budgets.py` compiles measured samples and scene demands into the profile; do not label estimates or synthetic fixtures as hardware calibration.
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
- No asset class has a universal face-count maximum. Require a calibrated 60 FPS scene profile for authoring exports and package builds. Use measured per-scene p95 unscaled frame-interval samples to derive scene triangle capacity; these represent realized frame pacing, not a CPU/GPU timing breakdown, then allocate by visible instances, projected screen area, and role priority. A shared asset uses the tightest per-instance allocation across every scene where it appears; Unity applies the authored screen-relative LOD thresholds and does not generate meshes during play. Keep Blender face counts, exported triangles, per-LOD targets, screen thresholds, and verdicts in the JSON report. Run topology cleanup before skin weights and vertex-wear baking; never use count-only decimation.
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
- Apply the calibrated scene allocation independently to each authored asset. Level 2 module geometry is budgeted as its own visible asset demand, separate from its chassis. Never sum all modules into a chassis cap; no universal per-module or per-chassis cap remains.
- Author module roots to mount at the canonical hardpoint origin and use the catalogued slots `ERA`, `APS`, `Sponsons`, or `FuelDrums`. Keep module geometry visually self-contained, with scale/offset inside its hierarchy; the existing hardpoint socket and attachment group own placement. Search for and reuse canonical sockets before creating transforms.
- `content/source-art/canonical-visual-modules.blend` and its JSON manifest are the generated source set for the current 35 modules. Use `tools/Build-CanonicalVisualModules.py` with the pinned Blender 4.5.14 toolchain to regenerate; preserve the existing primary unit source blends and the RGBA vertex-wear attribute.
- `content/source-art/canonical-visual-module-roles.json` is the authoring-only source for each generated module's `visualEngineeringRole`, keyed by variant and hardpoint. Keep visible mechanical role separate from the PBR preset; the preset changes surface treatment and never selects assembly geometry. Every role names a form with a plausible mount, service path, and load path, and the build rejects a role assigned to an incompatible slot.
- Bind a module's reusable geometry explicitly on its variant row when the layout depends on that geometry. The Bulwark Battleship Tungsten ERA row sets both `geometryPreset: era_brick_cassette_v1` and `geometryLayout: bulwark_side_skirt_array_v1`; the Level 1 source retains its six existing front cassettes while Level 2 generates four side cassettes. Keep this separate from the Material Maker PBR `preset`.
- Reusable mechanical details use a separate `geometryPreset` identifier from the Material Maker `preset`. The shared `tools/hard_surface_geometry_presets.py`, `tools/hard_surface_geometry_presets_v2.py`, and `tools/hard_surface_geometry_presets_functional.py` registries define baseline v1, polished v2, and specialized functional presets across contemporary and futuristic military styles:
  - **Contemporary Military (Estilo militar actual):**
    - `service_hatch_recessed_v1` (Slot: `Sponsons`, Role: `utility_sponson`): Recessed panel with coaming, hinge, dog latch, and frame fasteners.
    - `era_brick_cassette_v1` (Slot: `ERA`, Role: `armor_cassette`): Cross-seamed ERA cassette with backing plate, side keepers, and corner retainer bolts.
    - `aps_hardkill_radar_v1` / `_v2` (Slot: `APS`, Role: `aps_hardkill_radar`): v1 features AESA radar housing with quadrant array dividers, 2-axis gimbal yoke on stepped turntable with flange bolts, trunnion lock nuts, and twin countermeasure mortars with flared blast rings. **v2** upgrades to a 2x2 quad mortar cluster with individual breech lock rings and umbilical wiring bus, AESA radome with silicone environmental gasket and micro-emitter phased array sub-plates, azimuth traverse ring gear with grounding braid, functional hydraulic elevation actuator cylinder, curved blast deflector shield between launcher and radome, rear finned heatsink plate with top lifting eye, and localized Channel B thermal soot.
    - `fuel_aux_jerrycan_rack_v1` / `_v2` (Slot: `FuelDrums`, Role: `fuel_jerrycan_carrier`): v1 features perimeter tubular skid basket with lower terrain skid runners, clevis pins, dual stamped jerrycans, and ratchet straps. **v2** adds laser-cut slotted floor drainage grid, molded neoprene rubber isolation pucks on 4 mounting feet, refined dual stamped jerrycans with recessed X-stiffeners and perimeter crimp weld flanges (2-piece pressed steel joint), dual-cap system (cam-lock filler neck + vent screw cap) with stamped brass ID tags, hinged drop-down retaining gate with swing-bolt and knurled clamping star knob, and ratchet tie-downs with grounding wire clip.
    - `slat_armor_screen_v1` / `_v2` (Slot: `ERA`, Role: `slat_armor_screen`): v1 features standoff bar/slat armor screen with dihedral deflection bevels, vertical center stiffener, hinge clevises, and sway braces. **v2** upgrades to 8 horizontal knife-edge slat bars with 45° projectile deflection wedge profiles, notched interlocking center and quarter-span uprights, secondary statistical wire mesh grid between bars, articulated parallel-linkage hinge standoff arms with grease zerks, captive lynchpins on steel lanyards, heavy locking sway struts, and molded neoprene rubber hull slap bumpers.
    - `ordnance_mine_dispenser_rack_v1` / `_v2` (Slot: `FuelDrums`, Role: `ordnance_carrier`): v1 features 4 launch cassettes with 3 canisters each, A-frame struts, axle, and jettison lever. **v2** adds a sloped steel backblast deflector deck shield with drainage scuppers, quick-change toggle clamps with yellow safety pins on all 4 cassettes, 12 canisters with bore cutters and protective rubber blowout burst-caps (scored petal tear lines), graduated elevation quadrant sector arc with index notches and locking handwheel, armored junction box with status LED, and emergency mechanical jettison lever with pneumatic cylinder.
    - `thermal_armored_radiator_pack_v1` / `_v2` (Slot: `Sponsons`, Role: `thermal_radiator`): v1 features cowl shell, top/bottom deflectors, cooling matrix, 6 chevron louvers, and supply/return pipes. **v2** adds sloped ballistic deflector cowls (30°), 6 chevron armor louvers with perforated steel micro-mesh debris screens, dual auxiliary forced-air cooling fans in circular cowls recessed behind core, ANSI 4-bolt pipe flanges on supply/return pipes with braided stainless flex-hose and manual quarter-turn ball valve handles, diagnostic sensor taps, and flush maintenance hatch with perimeter seal, dual rotary cam-latches, and grease-zerk hinges.
    - `autocannon_remote_sponson_pod_v1` / `_v2` (Slot: `Sponsons`, Role: `heavy_flak_battery`): v1 features turntable boss, cradle, trunnions, barrel with bore cutter and muzzle brake, magazine, and sensor ball. **v2** upgrades to stepped azimuth turntable with 8 flange bolts and traverse gear teeth, elevating curved ballistic sponson mantlet shield sealing aperture gaps across all angles (-10° to +60°), heavy 30mm barrel with 6 longitudinal cooling flutes, slotted double-baffle muzzle brake, perforated thermal jacket sleeve, articulated linkless feed chute with interlocking stainless steel links from conformal magazine to receiver, downward-angled spent casing chute with rubber flap, and stabilized dual-sensor FLIR + daylight TV / laser rangefinder pod with protective visor.
    - `excavation_drill_carbide_borer_v1` (Slot: `Sponsons`, Role: `excavation_drill`): Heavy rotary carbide excavation borer with 6-bolt mounting base, heavy trunnion gussets, cylindrical reduction gearbox with 6 cooling ribs and brass sight glass, dual radial hydraulic drive motors with flexible braided hoses, torque reaction arm, curved debris deflector shroud, spiral cutting head with 12 welded tungsten-carbide pick teeth, and center chisel pilot bit.
    - `docking_clamp_hydraulic_arm_v1` (Slot: `Sponsons`, Role: `docking_clamp`): Heavy-duty zero-G structural docking clamp with 4-bolt base, emergency pyrotechnic release collar, twin-ear pivot clevis, cantilever arm with lightening pockets, dual opposing hydraulic cylinders with chrome-plated piston rods, articulated grapple jaws with interlocking hardened-steel serrated teeth pads, and central optronic docking guide pod with laser rangefinder.
    - `apu_turbogenerator_sponson_v1` (Slot: `Sponsons`, Role: `power_containment`): Tactical auxiliary power unit (APU) turbogenerator pod with 4 neoprene vibration-isolation pucks, sloped ballistic shell, cyclonic particulate separator cowl with 4 horizontal intake louvers and centrifugal dust ejector tube, thermal-jacketed exhaust diffuser duct, flush maintenance door with dual rotary compression latches, and armored high-voltage electrical terminal box with orange flexible conduits.
    - `smoke_grenade_salvo_discharger_v1` (Slot: `APS`, Role: `aps_sensor_suite`): Multispectral countermeasure aerosol / smoke salvo discharger bank with sloped 4-bolt pedestal, azimuth turntable index ring, 8 mortar tubes arranged in a 2-tier fan bank (2x4) with 48° azimuth spread and 38° elevation, flared protective muzzle lips, rubber weather-seal blowout caps, rear breech ignition plugs, armored electrical conduit, and sealed junction box with status LED.
    - `ciws_rotary_point_defense_sponson_v1` (Slot: `Sponsons`, Role: `heavy_flak_battery`): 20mm rotary Gatling CIWS sponson with 8-bolt azimuth turntable, U-shaped cradle cheeks with trunnion hubs, elevation quadrant sector arc, hydraulic elevation ram, 6-barrel rotary Gatling cluster with mid and muzzle clamp rings, slotted flash suppressor cage, 3-segment articulated linkless feed chute, rear pylon monopulse tracking radome sphere, and `Socket_MuzzlePrimary`/`FirePoint_0`.
    - `reinforced_fuel_drum_drop_rack_v1` (Slot: `FuelDrums`, Role: `fuel_drum_pair`): Ejectable drop rack for twin 200L stamped steel fuel drums with circular rolling hoops, threaded fill and vent bung caps, welded tubular cradle frame with lower skid runners, 4 webbed tie-down straps with cadmium tension ratchets, central quick-disconnect fuel transfer manifold with quarter-turn ball valves and braided lines, and emergency pneumatic jettison release claw.
    - `field_engineering_crane_winch_sponson_v1` (Slot: `Sponsons`, Role: `repair_dispenser`): Combat engineering recovery crane and winch sponson with 6-bolt mounting base, armored swivel turntable housing, box-section luffing boom with telescopic extension section, hydraulic luffing ram, hydraulic cable winch drum with coiled wire rope and fairlead roller guides, snatch block with forged swivel lifting hook, and 3-spool hydraulic valve bank with control levers and pressure gauge.
    - `multispectral_countermeasure_jammer_v1` (Slot: `APS`, Role: `aps_magnetic_shield` / `aps_sensor_suite`): Multispectral active EW jammer and MAWS mast with square pedestal plate on 4 elastomeric shock mounts, high-speed azimuth servo gearbox with finned aluminum cooling heatsink, conical dielectric EW jammer radome with 3 stepped annular emission rings, top hemispherical emitter cap, twin angled MAWS sensor pods with germanium IR optics, and armored electrical junction box with grounding braid.
  - **Futuristic Military (Estilo militar futurista):**
    - `aps_phase_shield_emitter_v1` / `_v2` (Slot: `APS`, Role: `aps_phase_shield`): v1 features faceted stealth nacelle, concave projector cavity, dielectric lens core, 3 electrodes, magnetic rings, cryo louvers, and conduit feed. **v2** features compound faceted wedge nacelle with continuous perimeter razor chine and recessed sawtooth RAM expansion seams, geodesic multi-faceted photonic crystal emitter core inside parabolic cavity, 3 concentric stepped superconducting magnetic collimation rings, 4 convergent tungsten-rhenium micro-arc electrodes with ceramic insulator collars, quad vacuum-jacketed liquid-helium heat pipes to upper cryo dissipation louvers, translucent spine fiber-optic diagnostic waveguide strip, and armored coaxial power terminal.
    - `plasma_containment_cell_v1` / `_v2` (Slot: `Sponsons`, Role: `plasma_containment_cell`): v1 features polyhedral vault, flux viewport with 4 probes, twin torus coils, cryo manifold, heat sink fins, and lifting lugs. **v2** upgrades to polyhedral armored vault with titanium-carbide skid plate and 4 forged lifting eyes, quad magnetic confinement coils (2 primary toroidal + 2 secondary helical poloidal rings) with forged saddle clamps and hydraulic tensioner jacks, hyper-vacuum flux viewport with dual quartz panes, hexagonal 12-bolt flange, 6 radial electromagnetic probes with luminous tip rings, triple vacuum-insulated cryo manifold, dual 20° emergency directional vapor blow-off vents with drainage scuppers, and machined base heat-sink fin array with perforated rock-guard underplate.
    - `stealth_hex_reactive_tile_v1` / `_v2` (Slot: `ERA`, Role: `stealth_hex_reactive_tile`): v1 features 7 interlocking hexagonal composite tiles, 2-tier faceting, optical status sensor diodes, and 6 valley fasteners. **v2** features 7 interlocking composite hexagonal tiles with 3-tiered ballistic structure (faceted stealth cap with 6 pyramidal micro-chines, graphene-ceramic rim, shaped reactive charge), laser-etched directional blast micro-grooves, dual sensor nodes per tile (coaxial optical IR trigger + micro-radar transceiver node in brass collar), central recessed hex-socket captive mount pin, interlocking tongue-and-groove expansion joints, and faceted backing plate with underside CNC weight-reduction pocketing and 6 standoff brackets with elastomer bushings.
    - `plasma_torpedo_magazine_rack_v1` / `_v2` (Slot: `FuelDrums`, Role: `ordnance_carrier`): v1 features faceted stealth base plate, 3 guide rails, 3 torpedoes with containment rings, flux nozzles, and power conduits. **v2** features faceted stealth base plate with 4 magnetic decoupling anchors, 3 linear induction accelerator launch rails with open triangulated truss frames and visible copper stator induction coils, arched composite ballistic canopy cover protecting bays from top-attack shrapnel, 3 Quantum Plasma Torpedoes with faceted ogive kinetic penetrator nose cones, 3 glowing containment rings, quad fold-out magnetic stabilizer fins, vectoring thrust nozzle bells, dual superconducting power feeder cables with braided vacuum sleeves, and emergency manual plasma dump purge valve.
    - `tachyonic_radiator_array_v1` / `_v2` (Slot: `Sponsons`, Role: `thermal_radiator`): v1 features diamond cowl, central heat sink rod, 6 dissipation fins, 4 deflection nodes, and cryo pipes. **v2** features aerodynamic diamond-profile armored cowl shell with front variable-geometry stealth intake louvers and ceramic thermal insulation lining, dual parallel superconducting quantum resonance core rods suspended by magnetic levitation collars, 8 forward-swept geometric dissipation fins with micro-serrated chevron trailing edges and laser-cut slots, 6-node perimeter multi-phase magnetic field deflection emitter array with ceramic insulating bezels and active field coils, twin cryo distribution pipes with quick-disconnect magnetic couplers, and digital temperature telemetry transceiver node.
    - `nanite_repair_dispenser_pod_v1` / `_v2` (Slot: `Sponsons`, Role: `repair_dispenser`): v1 features pod housing, aperture shutter doors, 2 storage cylinders with viewports, 3-nozzle manifold, and transceiver mast. **v2** features faceted composite aerodynamic sponson housing with recessed mounting flange, quad-petal motorized aperture shutter iris doors with hydraulic mini-rams and environmental dust wipers, twin filament-wound carbon-fiber nanite storage flasks with elongated transparent optical inspection viewports, convergent 4-nozzle high-velocity dispersal array with magnetic induction collimation rings and internal vortex swirling vanes, micro-drone hive auxiliary bay for deploying laser-guided micro-repair survey drones, phased-array micro-telemetry antenna panel, and 4-segment diagnostic LED status indicator bar.
    - `plasma_excavation_torch_v1` (Slot: `Sponsons`, Role: `excavation_drill`): Quantum plasma thermal excavation lance with faceted aerodynamic sponson housing, titanium-carbide skid plate, high-density magnetic confinement barrel, vortex nozzle with refractory alloy tips, 3 concentric stepped superconducting collimation tori with ceramic standoffs, dual vacuum-jacketed cryo coolant feeds, and luminous linear telemetry waveguide on the dorsal spine.
    - `magnetic_tractor_docking_clamp_v1` (Slot: `Sponsons`, Role: `docking_clamp`): Futuristic zero-G magnetic tractor docking clamp with faceted diamond-profile bracket, 4 magnetic anchor studs, central parabolic tractor projector dish with focus emitter node, 4 cross-configured superconducting magnetic flux pole shoes with ceramic bezels, twin pulse capacitor energy storage banks, and 4-point phased laser alignment guidance array.
    - `graviton_recoil_anchor_v1` (Slot: `Sponsons`, Role: `utility_sponson`): Kinetic and graviton recoil stabilization anchor pod with sloped armor housing, articulated titanium scissor linkage arms with heavy pivot pins, fluted tungsten ground penetrator spike with integrated coaxial electromagnetic coupling coil, pneumatic buffer piston, and directional energy bleed exhaust louvers.
    - `optronic_recon_sensor_mast_v1` (Slot: `APS`, Role: `aps_sensor_suite`): Retractable armored optronic surveillance / targeting mast pod with faceted mast well housing, dual motorized sliding visor doors, hexagonal segmented telescopic mast column with elevation ram, 2-axis gyro-stabilized sensor turret sphere (containing thermal FLIR lens, high-magnification TV window, and laser designator aperture), and a circular millimeter-wave phased array radar disc with gold waveguide feed horn.
    - `quad_pulse_laser_ciws_sponson_v1` (Slot: `Sponsons`, Role: `heavy_flak_battery`): Quad pulsed laser CIWS sponson with faceted composite maglev base, circular traverse ring, sloped cradle cheek armor, 2x2 convergent laser emitter barrels with ceramic collimator rings and refractory alloy muzzle shrouds, dual liquid helium/nitrogen cryo manifolds, flank planar capacitor energy banks with transverse glowing radiator fins, and gyro-stabilized multi-aperture optical targeting sphere.
    - `cryo_fusion_cell_containment_rack_v1` (Slot: `FuelDrums`, Role: `fuel_drum_pair`): Cryogenic fusion fuel cell containment rack with faceted composite bulkhead, 4 magnetic decoupling latches, twin spherical deuterium/helium-3 fuel cells with titanium equatorial clamping rings and gold MLI thermal blankets, superconducting inter-cell transfer bridge with dual rupture discs, ventral cryogenic boil-off relief exhaust nozzles with frost deflectors, and digital OLED telemetry panels.
    - `nanofabrication_repair_projector_sponson_v1` (Slot: `Sponsons`, Role: `repair_dispenser`): Articulated molecular nanofabrication and repair projector with aerodynamic root base on direct-drive magnetic bearing turntable, articulated composite boom beam with elbow pivot joint, coherent projector head cowl with concentric magnetic collimation tori, dual quick-swap solid-state feedstock cartridges with luminous level viewstrips, surface reconstruction lidar scanner with sapphire optical wedge, and rear transverse heat pipe matrix.
    - `flux_deflection_shield_projector_v1` (Slot: `APS`, Role: `aps_magnetic_shield`): Magnetohydrodynamic flux deflection shield projector with hexagonal heavy composite base plate on 6 grounding studs, stepped cylindrical magnetic pinch chamber with superconducting copper field coils, 4 articulating electromagnetic deflection guides (petals) with stepped ceramic insulator teeth, high-voltage busbar cables with silicone strain-relief boots, closed-loop helium return plenum, and dorsal pulsing shield beacon emitter dome.
- `coaxial_service_connector_recessed_v1` (Slot: `Sponsons`, Role: `power_containment`): Recessed service socket on the existing Gravitas Breaker power-cell panel, with an Exact counterbore, stepped insulator/hub/coupling, a seated retainer ring, and four panel anchor bolts kept outside the ring footprint. The variant-only canonical binding is `tactical-operations:gravitas-breaker-quantum-hyperdrive-alloy`; keep the Material Maker PBR preset independent.
- Run `powershell -ExecutionPolicy Bypass -File tools/Run-CanonicalLevel2HardflowPilot.ps1 -UseHardflow -RenderPreviews` for the isolated preset review scene. Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Run-GravitasPowerConnectorHardflowPilot.ps1` for the focused native/Hardflow connector replay, which records evaluated connector-surface deviation, local mesh health, UV/RGBA vertex wear, and sampled clearance to the coolant feeds and containment braces. Both pilots write under ignored `artifacts/` and never promote production FBX or manifests.
- Fast headless preset verification: `tests/python/test_hard_surface_geometry_presets.py` verifies all 43 Level 2 presets in headless Blender. When batch-generating presets for test evaluation, always unlink and delete created objects and orphaned mesh/curve datablocks after recording metrics (`bpy.data.objects.remove(o, do_unlink=True)`, `bpy.data.meshes.remove(m)`). This eliminates Blender's dependency-graph re-evaluation bottleneck across accumulated objects, reducing execution time from ~95-130s down to ~5-6s with zero loss of geometry quality or test assertions.

- The Unity canonical builder cross-checks each pack module against the generated manifest and resolves the owning Level 1 prefab. Every hardpoint used by a Level 2 variant must resolve to exactly one matching `Socket_Hardpoint_<Type>` in that primary hierarchy; duplicate and noncanonical hardpoints fail the build.
- For a package-ready rebuild, run `tools/Build-CanonicalVisualModules.ps1`; it regenerates Level 2 module FBXs and shared PBR maps, refreshes the four Level 1 prefabs that currently back modules, validates their canonical hardpoints, then builds the Unity bundle outputs. It does not rebuild primary mesh geometry. If changing a primary socket, update `tools/CanonicalUnitHardpoints.py`, regenerate the primary FBX from its model builder, then refresh the Unity prefab; keep each hardpoint on a plausible armor/deck/side mounting surface.
- The canonical full build remains fail-closed while `content/source-art/model-budget-profile.json` is uncalibrated. Validate new geometry presets in an isolated Blender scene and record source faces and exported triangles; do not use synthetic counts to bypass the package gate or claim FBX, prefab, or bundle outputs were refreshed.
- Inspect the labeled contact sheet for separation of silhouette, clearances, and attachment fit. At every declared hardpoint, verify that exposed module surfaces clear the Level 1 chassis; allow penetration only inside a concealed mount, sealed interface, or functional joint. Review attachment from front, side, top, and elevated RTS views. A contact sheet is authoring review, not a live render check.

## 16. Optional Hardflow Geometry Authoring

- Hardflow 1.23.0 is an optional Blender 4.5.14 extension for manual geometry authoring. Stage the official release with `tools/Setup-Hardflow.ps1` and launch the project-local preferences and extension repository with `tools/Start-BlenderWithHardflow.ps1`. The pinned ZIP SHA-256 is `5f4b2fe1eddfc3b4d0397d8052a8fbdfe8bf8f4292c0b8c6ff79bee00349b947`; installation lives under ignored `.tools/Hardflow-1.23.0/` and does not alter the normal Blender profile.
- Use its draw-cut, mirror, vent, bolt-circle, seam, pipe, and cable tools to hand-author functional vehicle, armor, turret, or weapon details. Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Run-GravitasBreakerHardflowPilot.ps1` to create an isolated `.blend`, four baseline/pilot camera views, and a mesh-health report under ignored `artifacts/model-budget-hardflow-pilot/`. The normal builder replays the shared recipe with Blender's built-in Boolean, bevel, and weighted-normal operations so batch builds do not need the extension. Review contact, calibrated per-scene triangle budgets, rig, socket, UV, vertex-wear, and FBX contracts before production promotion.
- The Gravitas Breaker follow-up in `tools/gravitas_breaker_hardflow_recipe.py` uses idempotent recipes for recessed feed connectors on `GravitasBreakerGravCannon_Gravity_BreechMount` and countersunk locks on the existing trunnion retention caps; `tools/Build-GravitasBreakerModel.py` replays both with Blender built-ins. Compare evaluated meshes because Hardflow can retain bevel modifiers that are absent from the base mesh data. Verify that fasteners land on the cap's visible face and perform a static recoil sweep against fixed hardware. Reject or redesign hoses and glands that cross exposed armor or float over an opening; the rejected Gravitas test cable/gland is intentionally absent from the accepted recipe. Keep FBX and bundle promotion blocked while the shared model-budget profile is uncalibrated.
- The Bulwark pilot is `tools/Run-BulwarkHardflowPilot.ps1`. It uses the verified source backup, keeps the six existing front cassettes byte-for-byte in the candidate object set, and replays four Level 2 side cassettes plus the skirt/hatch contact repairs with Blender-native operations. The v14 repair routes all six drop hangers outside the moving belt, checks triangle-level hanger/belt intersections, and seats the existing dog latch on the hatch panel. The Hardflow candidate is compared against the native replay across ten ERA shell surfaces; the pilot writes a separate source replay without the Level 2 module, so never copy the combined preview into the primary source. Hardflow/Blender views are local authoring evidence only; package outputs remain blocked until geometry demand is calibrated.
- Before cutting a functional seat, compare the actual world bounds of the named shell and the mechanism it serves, then inspect top and side views. In the Bulwark source, `BulwarkSiegeTankArmor_UpperDeck` ends at Z=1.08 m while `BulwarkSiegeTankEngine_RadiatorCavity` begins at Z=1.524 m; this raised hull-bound assembly intersects the turret yaw volume. The shared `tools/bulwark_radiator_vent_recipe.py` cuts the vent with Exact Booleans, built-in bevels, in-bounds louvers, retained UVMap, and RGBA wear. Its `seat_bulwark_radiator_assembly()` helper lowers the cavity and louvers to the deck and seats the existing hatch group laterally with a measured gap; it updates `rcl_bulwark_radiator_vent_bounds` so a marked source can migrate once and replay as a no-op. `tools/bulwark_contact_repair_recipe.py` calls that helper before the deck-seat assertions, then cuts a shallow coaming rebate. For the Bulwark Pioneer rack, compute the group center from aggregate world mesh bounds, not the mean of object origins; the source rack pieces have deliberately offset origins. Seat its cleats on the starboard fender with measured pads, outside the deck and turret motion envelope. Keep tall radiator rails/cages out of the yaw volume, preserve hatch hinge relationships and the full turret turn, and verify hatch opening and turret yaw/pitch/recoil clearance on isolated candidates. The candidate recipe is local Blender evidence only; do not promote its `.blend` until the four-view and contact/sweep review passes.
- The Overlord HQ factory has a separate focused recipe and builder: `tools/overlord_hq_factory_hardflow_recipe.py`, `tools/Run-OverlordCommandHQFactoryHardflowPilot.py`, and `tools/Build-OverlordCommandHQFactoryModel.py`. Its isolated pass seats the six existing side service plates, adds cam locks and maintenance-door hinges/latch, and clears the door approach by cutting back the two landing rails. The Hardflow pilot compares sampled surfaces against the native Blender replay; the normal builder fails closed until the model-budget profile supplies a calibrated asset demand. Do not copy the candidate to the Unity FBX or bundle while that gate is closed.
- `assign_rigid_mesh_weights` routes meshes by name after stripping the unit prefix: `TurretRingGuard` is an explicit `Bone_Hull` exception because the ring guard is fixed to the hull deck; `CoaxialPort` assemblies use `Bone_TurretYaw` before the generic barrel/muzzle/recoil rule; barrel/muzzle/bore/recoil/nozzle use `Bone_BarrelRecoil`; mantlet/cradle/breech/howitzer/rail/mortar/pod and related weapon terms use `Bone_TurretPitch`; turret/bustle/visor/radar/trunnion terms use `Bone_TurretYaw`; all other meshes use `Bone_Hull`. Keep pitch-specific `Mortar`/`Pod`/`Cradle`/`Breech` names in the earlier pitch branch. Hull-mounted radiator supports must avoid the `Cradle` and `Rail` tokens (for example, `RadiatorDeckStand*`); confirm the resulting bone assignment whenever a recipe adds meshes. Arachnid's `MidKnee_L/R` and `MidKneeAxle_L/R` names explicitly identify the middle legs and bind to `Bone_Shin_ML/MR`; do not infer a middle leg from `_L/_R` alone.
- Hard-surface recipes that add mesh objects must append them to the builder's `objects` collection so they receive vertex wear, rigid weights, batching, and export. A `complete` recipe marker is valid only while its named detail objects remain both in `bpy.data.objects` and in the builder collection. For trunnion-seat visibility checks, skip `_COLLIDER_` proxy hulls and check the first hit among visible render meshes instead.
- Hardflow is not a dependency of generator scripts or pack builds. Its unwrap, texture bake, and PBR features do not replace Blender's established UV/FBX workflow or the required Material Maker texture pass. Local UI or mesh checks do not prove appearance in a Rogue Command match.
