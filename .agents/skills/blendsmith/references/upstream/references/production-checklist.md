# Production and handoff checks

Consult relevant sections during production and evaluate applicability before delivery. R01–R10 come from the user's requirements; the detailed checks below implement them and still need calibration through asset trials. There is no universal polygon count, texture resolution, texel density, or padding value.

## 1. Brief, references, and shape

- Before a new or materially changed production scope, restate all requirements together and record the user's confirmation. A low-detail derivative is a new scope even when the source and rig are approved. Reuse approved details within the summary; do not repeatedly seek the same confirmation after production is authorized.

- Record the exact Blender version, units, physical dimensions, game/VFX purpose, receiving application/engine, nearest camera distance, and whether the asset deforms.
- Confirm budgets for editable base geometry, evaluated triangles, objects/materials, texture sizes/counts, LODs, and subdivision. Do not report a VFX source cage and game export triangles as if they were the same metric.
- When product research helps, search exact-model manufacturer sources first and supplement with useful retailer/review angles. Record URL, access date, model/variant and applicability. Confirm consequential findings with the user; flag contradictory measurements, regional layouts, and product revisions.
- Compare the model and references at appropriate viewpoints. Disclose perspective distortion, unknown lenses, and inferred dimensions. Do not distort confirmed physical measurements merely to match one perspective image.
- Inspect silhouettes, proportions, joins, gaps, and thickness from multiple views. Recessed seams need appropriate depth; floating strips are not a substitute for recesses.
- Reconcile every occluded structure with the brief: modeled, simplified, or omitted, and on whose confirmation. Do not invent internals and represent them as the actual product construction.

- Ask the target count or low/medium/high tier for each new asset; translate it into asset-specific base/evaluated face or triangle budgets with the user's confirmation or delegated judgment. Reuse accepted budgets.
- Confirm square UV sheet/atlas count and per-sheet resolution separately from channel count. Prefer square textures for new work; retain explicitly accepted existing exceptions.

## 2. Topology and editable structure — R03

For each model mesh record its name, vertex/face counts, triangles/quads/n-gons, and UV map names. Also inspect evaluated modifier/instance geometry, documenting counting methods to avoid omissions or double counting.

- Base and evaluated n-gon counts must both be zero. Do not conceal poor base topology with final triangulation.
- Inspect zero-area faces, duplicate faces, loose vertices, unintended normal directions, interpenetration, self-intersection, and accidental holes. Supplement unreliable automated checks with sections and multiple views.
- Explain non-manifold elements and open boundaries individually: sheets, garment openings, and closed solids have different needs. Do not automatically accept or reject them based on one count.
- Check edge flow in deformation regions and pinching/silhouettes at agreed subdivision levels. All-quad topology can still be poor topology.
- Inspect rounded panels, screens, capsules and circular caps for boundary-vertex fans and long diagonals. Use useful quad strips/grids or local triangles; inspect pole placement and spacing at the same evaluated-triangle budget. Flat shading or coplanarity does not exempt poor editing topology.
- Name and separate objects by function; place origins for actual support, pivoting, or assembly. Shared mesh data and UV relationships should be understandable.
- Organize model, rig, lights, cameras, and references into clear Collections. Agree on source/delivery separation in the brief. Hiding noncompliant objects does not exempt them.

- For nearly flush labels, compare before/after face counts and inspect silhouette, arched edges, UV registration and contact under subdivision. Dense tessellation needs a shape/deformation reason.

- For modeled openings, inspect local perimeter/support loops, bevel-to-wall continuity and their transition into the panel. Reject avoidable long fan/sliver connections to distant corners; review wireframe plus neutral/grazing-light close-ups at relevant poses. Record actual evidence, not only manifold or n-gon counts.
- For approved baked openings, record what geometry was omitted and the lost depth/parallax. Test matched high/low part projection, normal direction, cage/ray misses, dark-region coverage, seam bleed and absence of adjacent moving-part contamination. Do not freeze studio reflections into BaseColor to imitate a hole. Preserve independent controls, moving caps and needed deformation segments; report total evaluated counts at motion extremes under the agreed budget convention.

## 3. UVs, UDIMs, and texture coverage — R03

- Every delivered mesh has nonempty, nondegenerate UVs with finite coordinates for all face loops; merely creating a UV layer is insufficient.
- Default to a non-overlapping 0–1 layout. Mirrored, stacked, or tiled reuse requires asset-specific confirmation and documentation. Check unintentional overlap across objects sharing an atlas as well as within a mesh.
- Use a checker to inspect seams, visible distortion, orientation, and texel density. Record the density target, purposeful exceptions, and observed deviations; automatic unwrap does not prove quality.
- Set island separation and edge padding from texture resolution, baking, and intended mip usage. Inspect seam bleed at relevant reduced resolutions. Specify numbers in the brief, not as universal constants.
- Record the main UV map and use matching UVs for the texture and tangent-space Normal Map node. [Blender 4.4 Normal Map documentation](https://docs.blender.org/manual/ja/4.4/render/shader_nodes/vector/normal_map.html)
- For UDIMs, confirm the tile list, purposes/resolutions, and image files for every required channel. Load the actual tiles; inspect missing or wrong tiles, unintended cross-tile islands, seams, and receiving-pipeline support. Moving UVs outside 0–1 is not a completed UDIM deliverable. [Blender 4.4 UDIM documentation](https://docs.blender.org/manual/fi/4.4/modeling/meshes/uv/workflows/udims.html)
- If overlap, stretch, or padding cannot be exhaustively checked, report the tools, manual inspection scope, and remaining gaps. Do not report UV-layer existence as a clean-UV pass.

## 4. BSDF materials and maps — R02

- Before final baking, compare angle-controlled Auto Smooth, ordinary Smooth and intentional Flat by surface role. For hard-surface openings, establish and inspect Smooth by Angle/sharp edges first. Record selected mode, angle, source/target normal state and modifier order; face side count must not choose the shading mode.
- Inspect neutral shading without a normal map before projection. Verify that joined/isolated bake proxies preserve custom/split normals, UVs and evaluation diagonals. The delivered mesh must use the bake's tangent basis; recheck/rebake affected channels after topology, UV or normal-state changes.
- Compare the baked material under neutral/grazing light, with the normal map on/off where needed, and through relevant rig poses. Distinguish topology/smoothing defects from cage misses, projection bleed or texture sampling; record limitations instead of treating Auto Smooth as a universal repair.


Each material set needs at least four useful, loadable maps and an object/material/image mapping. Multi-object atlases are allowed. Example names: `asset_material_BaseColor.png` or `asset_material_BaseColor.1001.png` for UDIM tiles.

| Channel | Connection and verification |
| --- | --- |
| BaseColor | Represent material color, without baked studio highlights, cast shadows, or reflections. Interpret ordinary color images as sRGB when appropriate to their source; document other color pipelines. |
| Normal | Confirm map space and axis convention. For tangent maps, use Non-Color interpretation and a Normal Map node with matching UVs. A grayscale height map is not an RGB tangent normal map. |
| Roughness | Interpret as Non-Color data. Match roughness and microstructure scale under neutral and grazing light, rather than using arbitrarily large noise to suggest detail. |
| Metallic/Metalness | Interpret as Non-Color data and distinguish exposed metals, coatings, and dielectrics. A uniform black dielectric map is legitimate when it is an actual, used channel. |

Tangent-normal image interpretation and matching UVs are described in the [Blender 4.4 Normal Map documentation](https://docs.blender.org/manual/ja/4.4/render/shader_nodes/vector/normal_map.html).

Additional channels depend on the asset:

- An independent mask or image alpha channel may control emission color/strength. Connect surface Alpha only where transparency is intended; emission and transparency have distinct functions. [Blender 4.4 Principled BSDF inputs](https://docs.blender.org/manual/vi/4.4/render/shader_nodes/shader/principled.html)
- Document displacement scale, midpoint, bit depth, and required subdivision/render settings. Choose between small shading bumps and silhouette-changing displacement according to the use.
- If the receiving application needs packed channels such as ORM, also retain the four editable source maps unless the user explicitly changes this delivery requirement.
- Procedural nodes may remain as editable sources, but delivered maps must match final UVs. Generated images are not automatically valid material channels: inspect lighting contamination, seams, and alignment.
- Record asset/texture sources and delivery rights. Online product reference photos are not automatically redistributable texture assets.

- Inspect material-specific texture features at the agreed scale: paper fibers/pores/coating, for example, rather than generic noise. Compare strengthened paper detail under neutral/grazing light without losing print readability.
- R09: verify category folders contain all corresponding channels, shared maps have explicit consumers, and the manifest plus relative/packed Blender paths agree after reopening.

### Bake-proxy consistency

- If geometry or normals depend on modifiers, derive the bake target from the intended evaluated rest state, not blindly from `object.data`. Preserve the agreed editable cage in the deliverable.
- After joining, transforming or spatially isolating matched parts, compare corresponding triangle corners, UV orientation and world-space split/custom normals to the delivery target. Account for updated dependency-graph transforms and reordered polygons; positional or UV correspondence is safer than assuming join order. Choose tolerances for the asset scale and normal encoding, and report them.
- Small parts translated far from the origin can lose precision. Keep matching groups separated enough to prevent cross-projection without unnecessarily large offsets; preserve or restore evaluated normals if a join recalculates them. Confirm the correction with neutral/grazing views as well as numeric checks.
- When changing high-source smoothing, inspect its existing tangent Normal chain. Retain/reproject it only with a valid basis; otherwise rebake from geometry or authored detail and explicitly account for any microdetail lost. Do not globally disable every high-source normal map as a standard recipe.
- Angle splits/sharp edges, flat faces and optional Weighted Normal serve different purposes. Record what is stored in native mesh data and what remains a live modifier. Recheck after any post-bake change. Do not copy the 3D06 40/55-degree settings or its tolerance to unrelated assets without evaluation.
- Compare before/after panel and cap wireframes at the same counting convention; record editable triangles/quads and evaluated triangles separately. Keep useful curved boundaries/deformation loops rather than funding improved caps by silently destroying silhouettes. A passing quad percentage, count or normal test is not user acceptance.

## 5. Shader Editor — R01

- Inspect every used material and nested group for node/label overlap and traceable connections.
- Arrange maps by channel and processing from left to right. Use labeled Frames, useful Reroutes, and whitespace.
- Expose meaningful editing parameters, with names describing purpose or units. Basic color/roughness edits must not require dismantling a giant opaque group.
- Remove purposeless nodes; label and separate retained alternatives from the active shading chain.
- Save overview and readable detail screenshots as evidence. Disclose incomplete screenshot coverage or uninspected groups.

- Confirm Texture Coordinate and Mapping nodes are at the far left and every image's Vector input receives the intended Mapping chain. Aligned channels share transforms. Verify identity mapping preserves the baseline, and inspect normal-map behavior after any mapping changes.

## 6. Rig, selection, Extras, and lighting

- R10: confirm a rig behavior summary with the user before implementing revised controls. Record unresolved axes, scale policy, hierarchy, transitions and reattachment requirements.
- Animator-facing controls must be Armature pose bones used in Pose Mode. Test selection and ordinary transforms/keyframes there; custom-shape objects or Object Mode empties are not substitutes.
- Test input-driven motion, channel restrictions, parent motion, attached details, disengagement boundaries in both directions, and the agreed reattachment sequence. Include playback/scrubbing and a fresh reopen. Do not mistake transform locks for selection restrictions or a numerical driver test for actual usability.

- Use understandable controller names and agreed motion ranges. Caps, legends, and decorations stay together across full travel; inspect long keys and adjacent parts.
- Inspect cable endpoints, length, and cross-section under deformation. Clearly distinguish optional artistic stretching from fixed-length behavior. Do not claim untested physical solving.
- **R06 delivery requirement:** all object and Collection `hide_select` flags are false, including parent Collection restrictions. Confirm practical selection of representative meshes, lights, and cameras after reopening; do not require an unlocking script.
- In every saved 3D View, enable overlays and `show_extras`. Check the saved workspaces/screens, not only the currently active editor. In the intended scene view, lights/cameras should be discoverable and selectable; check visibility restrictions if they obstruct this.
- Never hand off a controls-only locked default state. If an animation-only mode is explicitly requested, make it reversible and save the deliverable with selection and Extras enabled. A toggle's existence does not exempt the default state.
- Keep pivots, parents, constraints, and modifiers inspectable. Explain any non-obvious operating conventions.
- Validate materials under neutral light, then apply the agreed studio/cinematic direction. Record exposure and color management. RGB lighting must not conceal material or proportion errors.

## 7. Save, reopen, and deliver

- R11: confirm Blender execution and output-folder access early; execute the authorized build and wait for save completion. A script-only package is incomplete unless explicitly requested.
- Verify the exact delivered `.blend` exists, has nonzero size and contains this build's expected asset/version. Record filename, size and save result; reject stale output after a failed build.
- Provide a working local file link or supported downloadable attachment to the checked artifact. Confirm the user can access the output location; report unresolved attachment/access limits. Do not claim a Files-panel entry solely because a script names an output path.
- If Blender cannot run, report the blocker and mark generation/reopen NOT RUN or FAIL as appropriate. Label any script-only output as partial, not a finished model.

- Deliver the `.blend` and editable maps. Verify relative paths, packing strategy, dependencies, and versions; a beauty image is not the asset.
- Reopen the actual deliverable in a fresh Blender process and inspect images, fonts, groups, drivers, modifiers, scenes, materials, selection, and Extras. Mark this NOT RUN if not performed.
- If a game or other DCC export is requested, import it into the receiving application. Blender-only inspection does not establish downstream compatibility.
- Tie tests to the exact file/version. Report geometry statistics, UV/material evidence, usability checks, and performance measurements separately.
- Distinguish production finished, self-checks passed, and user accepted. Missing hard requirements mean WIP or needs correction, with named objects and gaps; do not call the asset fully compliant.


## Intake revision — R04/R07, skill 0.1.6

- Check the core user-request fields even for free-form input; proactively ask missing applicable items in the user's current/requested language. Reuse supplied/confirmed answers and group related questions.
- Distinguish missing, unknown with proposed options, explicit delegation, confirmed and N/A. N/A needs a scope reason; silence is not an answer. Rig details depend on rig need, and render settings depend on image delivery.
- Ask topology as all quads / all triangles / mixed / other, clarifying the latter's editable and output representation in the asset brief. Honor explicit user overrides without silently discarding the current default convention.
- Explicitly resolve UV atlas count, resolution and UDIM usage. Use the default BaseColor/Normal/Roughness/Metallic set and ask for extras.
- Summarize requirements, hidden structures and tradeoffs for confirmation before production. Keep English records while adapting conversation language.


## Source-to-atlas delivery — skill 0.1.10

- When retaining procedural sources, follow [material-authoring-and-bake-workflow.md](material-authoring-and-bake-workflow.md): label source/derivative roles and same-geometry versus high-to-low baking.
- Verify completed source version, approved UV tile, and source/baked appearance at matching near and grazing views; disclose atlas-density limits.
- Record stage acceptance separately from final geometry-budget compliance.
