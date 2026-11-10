# Design Guides Adapted for Rogue Command

This reference adapts public modeling, shading, bake, and RTS art-direction material to Rogue Command. It sets an art workflow; the pinned Blender 4.5.14, Unity 2021.3.45f2, shader, importer, mesh, and AssetBundle contracts remain authoritative.

## Findings and project decisions

| Source finding | Rogue Command decision |
|---|---|
| Blender Studio's hard-surface workflow begins from measured references and simple blockout geometry, then tests movement, refines shapes, and checks the model from several views. | Use generators and primitives to establish scale and function. Refine the primary hull, torso, fairing, or facility shell before adding small parts. Review intersections and moving clearances. |
| Blizzard's StarCraft Remastered team kept familiar unit silhouettes so players could recognize units while seeing more detail. | Preserve each unit's role identity and recognition cues, while treating the native game's common shape conventions as a starting point rather than a ceiling. Add original stance, proportions, negative space, and construction language when the unit remains readable. |
| Blender's Bevel and normal tools alter edge geometry and shading; normal maps can transfer high-detail surface information to a UV-mapped mesh, with cages, projection distances, and island margins controlling bake artifacts. | Shape large masses in geometry. Apply bevels selectively, preserve plate facets, and use the existing Normal atlas for fine relief after the shape and UVs stabilize. Inspect seams and padding at target resolution. |
| Blender's Weighted Normal modifier adjusts custom vertex-corner normals using face area, corner angle, and optional face strength. | Use it only to preserve the intended shading of an already sound shape. Do not use weighted normals or smooth shading to hide poor cross-sections, lumpy topology, or an unclear silhouette. |
| Unity 2021.3 imports authored mesh normals and tangents, and uses screen-relative object size to switch LODs. | Preserve the project's authored normal/tangent path. Review each LOD at its actual screen size and keep role-defining silhouette and mechanisms readable. The 7,500-face value is a project profiling ceiling for LOD0, not a Unity limit or a quota. |
| The project shader encodes material and wear data in the four named maps plus the mesh Color RGBA attribute. | Keep the current shader and importer contract. Do not add AO maps or repack shader-unconsumed channels without a separate verified shader change. |
| PBR material references treat metalness and roughness as distinct controls for reflected light; roughness variation represents surface-scale variation, not arbitrary color noise. | Give paint, bare metal, rubber, glass, and emissive elements distinct material responses. Translate roughness to the project's existing smoothness channel (`1 - roughness`) and keep all output inside the four-map and vertex-Color RGBA contracts. The cited OpenPBR guide is a material concept reference, not a replacement shader specification. |

These choices are project adaptations, not rules claimed by the cited studios or software vendors.

## Authoring sequence

1. **Write a visual brief.** Record tactical role, mobility, primary weapon/system, scale, and two or three silhouette cues. Collect front, side, and top references for proportions, machinery, and materials. Use reference to understand construction; combine and alter it into an original design.
2. **Block out primary masses.** Match the native gameplay footprint, ground contact, weapon clearance, pivots, rig, and required sockets. Use the generated geometry as a blockout whenever the major surfaces still look like unmodified boxes or cylinders.
3. **Pass the silhouette gate.** Render black silhouettes from the elevated game camera and from front/side/top. Check that the unit reads at actual gameplay scale and is distinguishable from nearby archetypes. Exaggerate only the features that disappear at that scale.
4. **Shape the main body.** Replace primitive stacks that define visible primary masses with tapered profiles, lofted sections, superellipse shells, planned plate transitions, or sculpted forms suited to the archetype. Keep broad planar plates where they communicate armor or fabrication; use curved surfaces where they communicate cast, formed, aerodynamic, or soft structures.
5. **Resolve mechanical logic.** For each prominent mechanism, check its support, load path, energy or fluid path, clearance, articulation, and connection to adjacent parts. Apply only role-relevant cues: a bike does not need tank armor; a walker needs clear joints and supported feet; a factory needs a comprehensible process or logistics path.
6. **Refine edge and shading transitions.** Inspect a neutral clay render under grazing light. Use flat shading for deliberate facets, smooth shading for curved forms, and bevel widths/segments that describe the edge. Weighted normals can help keep broad faces stable but cannot repair a weak body form.
7. **Add surface detail after UVs stabilize.** Use the existing Albedo, MetallicGloss, Normal, and Emission maps. Bake detailed relief into the current Normal atlas only when it survives at gameplay scale. Use material-specific base color and reflectance; keep dirt and wear localized to plausible contact, exposed-edge, exhaust, or service areas instead of laying uniform noise over the asset. Preserve MetallicGloss.R as metallic, MetallicGloss.A as smoothness (1 - roughness), linear import with alpha from input, and the mesh Color RGBA vertex-wear data unchanged.
8. **Review camera, budget, and LOD.** Compare clay, silhouette, and textured renders. Keep LOD0 within the 7,500-face project profiling ceiling without treating the value as a target to fill. Track exported Unity triangles separately. Simplify hidden and sub-pixel features first; preserve primary outline and functional parts through LOD transitions.
9. **Verify the delivered source.** Use the pinned Blender and Unity versions. Inspect effective importer settings, UV coverage, map channels, vertex colors, LODs, sockets, hierarchy, transforms, ground contact, and rig bindings after rebuild. Label results as static inspection, local Unity validation, or in-game runtime evidence.

## Review checklist

- Does the unit read from silhouette alone at the elevated gameplay camera?
- Are its large forms original and coherent, rather than a stack of unrelated primitives?
- Do visible parts connect, move, and clear one another in a way that fits the archetype?
- Do material, curvature, and edge transitions communicate how the part is made?
- Does each secondary detail support function, scale, or unit identity at RTS scale?
- Are UVs and tangent-space bake padding adequate for the final atlas and mipmaps?
- Does the mesh remain within project bounds, preserve required sockets/hierarchy/Color RGBA, and stay below the 7,500-face LOD0 profiling ceiling?
- Were Blender faces, Unity triangles, and runtime appearance reported as separate evidence?

## Sources

- [Blender Studio: Modeling](https://studio.blender.org/tools/pipeline-overview/asset-creation/modeling) — reference gathering, blockout, refinement, and production review.
- [Blender 4.5: Bevel Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/generate/bevel.html) and [Weighted Normal Modifier](https://docs.blender.org/manual/en/4.5/modeling/modifiers/normals/weighted_normal.html) — edge geometry and shading behavior.
- [Blender 4.5: Cycles baking](https://docs.blender.org/manual/en/4.5/render/cycles/baking.html) — tangent-space normal bakes, selected-to-active projection, cages, and UV-island margins.
- [Blizzard: Behind the Scenes of StarCraft: Remastered](https://news.blizzard.com/en-us/article/20726732/behind-the-scenes-of-starcraft-remastered) — retaining recognizable unit silhouettes while updating visual detail.
- [Unity 2021.3: Model Importer](https://docs.unity3d.com/2021.3/Documentation/Manual/FBXImporter-Model.html) and [Level of Detail](https://docs.unity3d.com/2021.3/Documentation/Manual/LevelOfDetail.html) — authored normals/tangents and distance-based LOD.
- [Adobe Substance 3D: OpenPBR overview](https://experienceleague.adobe.com/en/docs/substance-3d/general-knowledge/openpbr/openpbr-overview) — material-response concepts for metalness and microscopic roughness; not a Rogue Command shader contract.
- [Epic: Talisman environment template](https://dev.epicgames.com/documentation/en-us/fortnite/talisman-environment-template-in-unreal-editor-for-fortnite) — a cross-engine example of mid-poly hard-surface assets and deliberate normal treatment; use only as technique reference, not as a Rogue Command pipeline requirement.
