# Design Guides Adapted for Rogue Command

This reference translates public modeling and game-art guidance into checks for Rogue Command's grounded, stylized military units. It complements the project art direction; it does not replace the pinned Blender, Unity, mesh, texture, socket, or AssetBundle contracts.

The studio interviews below describe other games and their constraints. Blender and Unity manuals describe tool behavior. The workflow and review thresholds that follow are **project-specific adaptations**, not universal art rules.

## Source findings and project decisions

| Finding from the source | Rogue Command adaptation |
|---|---|
| PlaySide's RTS art direction starts with silhouette and reduces texture micro-detail that adds noise or obscures gameplay units. | Make the unit recognizable from the elevated gameplay camera as a black silhouette before spending time on texture, wear, or emissives. Use faction color and materials as secondary cues. |
| Blizzard's StarCraft team rendered units at tiny sizes and selectively widened or thickened features that failed to translate. | Exaggerate only features that disappear at Rogue Command's camera scale, such as a role-defining barrel, sensor, limb, or skirt. Do not inflate every component. |
| Blender Studio's modeling guide uses references and a staged modeling/refinement process. | Establish role, proportions, and mechanical layout before adding paneling and greebles. Keep the procedural result as a blockout when its primary forms still look like assembled primitives. |
| Blender's Bevel modifier adds geometry according to its width, segment count, and edge limit. Weighted normals affect shading; they do not alter the outline. | Spend bevel segments on a visible rolled radius or a silhouette transition. Use a narrow one-segment chamfer for a cut plate edge. Keep flat armor faces broad, and do not use weighted normals as a substitute for a shaped cross-section. |
| Unity 2021.3 supports importing authored mesh normals and tangents; its single Smoothing Angle can smooth some edges incorrectly. Its LOD system reduces rendered triangles as camera distance increases. | Author hard and smooth edges in Blender and inspect the FBX import. Keep tangents available for the normal-mapped shader. Treat 7,500 faces as the current Rogue Command profiling target for LOD0, not a Unity limit or a target to fill; track exported triangles separately and simplify by LOD/screen size. |
| Blender baking can transfer high-detail normal information to a lower-detail mesh with a UV margin. | If close-up surface relief is needed, bake it into the existing Normal atlas after the shape and UVs are stable. Do not add a new AO map or move the mesh's RGBA vertex-wear channels into a texture. |
| Vitaly Bulgarov / Neil Blevins form hierarchy establishes a 70/20/10 distribution with 70/30 rest balance. | Allocate 70% of visual mass to primary silhouette, 20% to functional mechanisms (turrets, engines, sponsons), 10% to scale-defining greebles. Preserve 70% clean resting surfaces and concentrate 30% mechanical complexity into functional bays. Avoid uniform random greebling. |
| Real-world military & industrial engineering requires all hardware to be physically operable. | Apply the 4 Engineering Questions: verify power/fluid conveyance (hydraulic catenary slack), thermal dissipation (radiators/louvers), human maintenance (hoist shackles/D-rings/latches), and structural stress load transfer (gusset plates/trusses). |

## Adapted authoring sequence

1. **Write a role brief.** State the unit's tactical role, mobility type, primary weapon/system, and two or three silhouette cues. Gather front, side, and top references or proportion sketches. Define how the visible subsystems connect and move.
2. **Block out primary masses.** Use RogueModelKit assemblies to establish dimensions, stance, ground contact, weapon clearance, and mechanical pivots. Shape the visible hull/cabin/turret as a coherent shell; reserve boxes and cylinders for parts whose function supports those forms or for hidden structure.
3. **Pass the silhouette gate.** Render unlit black silhouettes from the in-game elevated camera and front/side/top views. Check the archetype, heading, and role-defining equipment at the project's 30–120 px review range, especially around 50 px. The pixel size and the existing 250 ms glance target are local review heuristics, not thresholds prescribed by the cited studios.
4. **Refine fabrication and intersections.** Add tapered profiles, changing cross-sections, rounded cast shoulders, plate thickness, recesses, attachment lips, shadow gaps, and visible joints where the construction needs them. Keep welded plate facets planar. Resolve tangencies and floating/intersecting pieces before adding small details.
5. **Run a shape and shading pass separately.** Inspect neutral clay under grazing light. Smooth curved/revolved surfaces and preserve hard edges on plates. Use a selective bevel to catch useful highlights; use weighted normals only to improve broad-face shading. If the silhouette still looks boxy in black, change the geometry.
6. **Add surface detail after UVs stabilize.** Use the existing Albedo, MetallicGloss, Normal, and Emission atlas outputs. For a detail bake, transfer high-resolution normal information onto the budget mesh with adequate UV padding. Preserve the shader contract: Metallic in `MetallicGloss.R`, Smoothness in `.A` as `1 - roughness`, linear import (`sRGBTexture = false`), alpha from input, and mesh `Color` RGBA vertex wear unchanged.
7. **Review by camera and budget.** Compare a close clay render, the black silhouette, and the textured unit in the elevated gameplay view. Keep the current 7,500-face authoring budget as a ceiling for LOD0 profiling rather than a quota; the generator reports the budget but does not automatically clamp geometry. Reduce hidden or high-frequency detail first, then inspect LODs and exported Unity triangle counts.
8. **Verify the delivered asset.** Use Blender 4.5.14 and the pinned Unity 2021.3.45f2 project. Check FBX normals/tangents, UVs, map imports, Color RGBA, LODGroup, canonical sockets, hierarchy, transforms, and ground contact. A render or successful import is not evidence of appearance in a live match.

## Review checklist

- Does the unit read from silhouette alone at the gameplay camera and remain distinct from nearby archetypes?
- Does the model observe the 70/30 rest rule with 70% clean planar armor/casting and 30% concentrated functional mechanics?
- Does every added detail answer one of the 4 Engineering Questions (power/fluids, heat dissipation, maintenance/lifting, or structural load transfer)?
- Are the primary forms shaped as coherent shells, with visible parts attached and mechanical joints where needed?
- Do soft and hard transitions follow the material and fabrication logic instead of sharing one bevel or smoothing treatment?
- Does every small feature still read at gameplay scale, or should it be removed or transferred to the existing texture atlas?
- Is the 7,500-face profiling target checked without treating it as an automatically enforced cap or face-count goal? Are Unity triangles and LODs inspected separately?
- Were normal-map details baked only after UVs stabilized, with the project's atlas, import, vertex-color, socket, and hierarchy contracts intact?

## Sources

- [Blender Studio Artist Guide: Modeling](https://studio.blender.org/tools/artist-guide/asset-creation/modeling) — reference-led modeling, blockout, refinement, and delivery review.
- [Vitaly Bulgarov / Neil Blevins: Functional Concept Design](https://www.neilblevins.com/art_assets/art_assets.htm) — form hierarchy, the 70/30 rule of visual balance, and functional hard-surface engineering.
- [PlaySide interview: *Age of Darkness* art direction](https://www.unrealengine.com/developer-interviews/age-of-darkness-is-an-rts-that-throws-thousands-of-undead-monsters-at-you) — RTS camera, silhouette-first unit readability, and reduction of noisy micro-detail.
- [Blizzard: *StarCraft* development retrospective](https://news.blizzard.com/en-us/article/20719767/rock-and-roll-days-of-starcraft-a-development-retrospective) — evaluating tiny in-game renders and selectively thickening features that failed to translate.
- [Blender 4.5 Bevel Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/generate/bevel.html), [Weighted Normal Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/normals/weighted_normal.html), [Mesh Structure and Normals](https://docs.blender.org/manual/en/4.5/modeling/meshes/structure.html), and [Cycles Baking](https://docs.blender.org/manual/en/4.5/render/cycles/baking.html) — geometry, shading, and bake behavior.
- [Unity 2021.3 Model Importer](https://docs.unity3d.com/2021.3/Documentation/Manual/FBXImporter-Model.html) and [Level of Detail](https://docs.unity3d.com/2021.3/Documentation/Manual/LevelOfDetail.html) — normals/tangents import and distance-based triangle reduction.
- [Unity URP 12.1 Lit shader](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@12.1/manual/lit-shader.html) — normal-map surface detail. Rogue Command's custom shader and builder remain authoritative for map channels and importer settings.
