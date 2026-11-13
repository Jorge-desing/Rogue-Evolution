# Rogue Command 3D Art Style & Palette Guide

## Direction

Use grounded, stylized military science-fiction realism. Models should carry believable mass, connected mechanisms, intentional surface transitions, and distinct unit silhouettes while remaining legible from the elevated RTS camera. The goal is a more convincing, original version of Rogue Command's visual language, not photorealism.

The project native units are a gameplay and scale reference, not a shape limit. Preserve the role and recognition cues of a unit while developing its own stance, proportions, outline, and construction details. See [Design Guides Adapted](design-guides-adapted.md) for source findings and the adapted workflow.

Before detailed modeling, compare the draft against the closest native or mod counterpart. Record which body-plan conventions it inherits and which role cues must remain. If the silhouette still follows a generic stock pattern, make two structurally different blockouts based on the unit's mobility and systems, then select the form with the clearest support, clearance, and RTS read. This local divergence pass creates originality through shape and function; surface noise and decorative add-ons do not substitute for it.

## Form design

- Start with tactical role, mobility, primary weapon/system, and two or three silhouette cues.
- Build the primary body before adding detail. Replace visible primitive stacks with tapered profiles, lofted shells, curved or faceted transitions, and purposeful plate layering.
- Retain cylinders, circular caps, and rings only when their round section supports a named mechanical function such as an axle, barrel, bearing, wheel, tank, exhaust, or mast. Avoid concentric trim, repeated cylinder stacks, and circular armor details without a mechanical purpose; use shaped, lofted, superellipse, faceted, or chamfered forms for housings and armor.
- Before UV work, inspect exposed geometry from front, side, top, and RTS views. No visible surface may pass through a neighboring part; allow overlap only inside concealed mounts, sealed joints, and articulated interfaces, and recheck the full motion envelope for moving assemblies.
- Use faceted planes for fabricated armor, controlled curvature for cast or formed parts, and asymmetric shapes when they support the unit's function or identity.
- Connect each visible mechanism to a support, load path, energy/fluid path, and required motion clearance where relevant. Do not require tank subsystems on bikes, walkers, aircraft, hover units, or facilities that do not use them.
- For each prominent mechanism, ask what carries load or recoil, how power/fluid reaches moving parts, where generated heat goes, and how the unit can be serviced or recovered when the archetype supports human access. Treat these as design prompts; do not add hoses, radiators, lugs, hatches, or reinforcement where they have no visible purpose.
- Use large, medium, and small form hierarchy as a composition aid. Fixed 65/25/10, 70/20/10, or 70/30 percentages are optional heuristics, not acceptance rules.

## RTS readability and originality

Review a black silhouette and a neutral clay render from the elevated camera and front/side/top views. Compare at actual on-screen sizes; around 50 pixels tall can be a useful internal review size, but it is not an external standard. Enlarge only the features that disappear. Remove micro-detail that merges into noise.

Unit identity should come from outline, stance, proportion, negative space, weapon placement, and material blocks. Color, emissive optics, wear, and surface texture reinforce recognition; they cannot rescue an unclear silhouette.

## Palette and material cues

Use faction colors and material response to distinguish armor, machinery, rubber, optics, and warning accents. Treat the examples below as starting points; tune color, metallic, roughness/smoothness, and accent coverage for the unit's role, lighting, atlas, and game scale. No fixed palette percentage is required.

| Material role | Example treatment | Notes |
|---|---|---|
| Primary armor | Faction base color, satin or matte response | Main silhouette surfaces and armor shells |
| Secondary armor | Lighter, darker, or warmer related tone | Selective panels, shields, or high-contrast equipment |
| Machinery | Dark gunmetal or exposed alloy | Joints, barrels, axles, actuators, and mounting hardware |
| Rubber and treads | Near-black, low metallic response | Tires, pads, seals, and flexible surfaces |
| Warning marks | Amber, yellow, red, or faction accent | Localized labels and safety cues |
| Optics and energy | Restrained cyan, orange, magenta, or faction color | Keep emissives small and role-specific |

Use camouflage or panel-value variation to support unit recognition and material separation. Do not add procedural noise that obscures the armor layout.

## Edge treatment and finish

- Keep broad armor planes clean. Chamfer a cut or exposed plate edge; use a rounded transition for cast housings, wheel shoulders, and formed covers.
- Scale edge widths to the part and its visible size. Inspect highlights and silhouette rather than applying a single bevel value everywhere.
- Flat-shade deliberate facets; smooth curved surfaces by intent. Use weighted normals to stabilize broad faces after the shape is already correct.
- Review the model in neutral clay under grazing light before relying on material color, vertex wear, or emission.

## PBR and vertex-wear contracts

Keep the project's four texture outputs:

| Map | Required content |
|---|---|
| Albedo | sRGB base color, camouflage, and authored paint |
| MetallicGloss | Linear; RGB = metallic, A = smoothness (1 - roughness); the shader reads R and A |
| Normal | Tangent-space normal map |
| Emission | sRGB emissive regions |

The current Unity import and material wiring remain authoritative. Do not put AO into MetallicGloss; this map does not consume it. Keep vertex wear on the mesh Color RGBA attribute:

| Channel | Meaning |
|---|---|
| R | Cavity AO: lower in recesses and undersides, higher on open surfaces |
| G | Edge wear: on convex exposed edges, zero on broad flat or concave faces |
| B | Thermal soot: localized at exhausts and muzzles |
| A | Team mask: armor tinting where supported by the material |

Never replace vertex wear with texture AO or an external painter's vertex-color output. Follow the project PBR authoring and validation skills when maps or imports change.

## LOD and delivery

There are no fixed face-count ceilings for chassis, modules, or structures. Resolve each asset's finite triangle and Blender-face allocations from the calibrated per-scene profile at `content/source-art/model-budget-profile.json`; measured Unity triangles are the runtime cost, while Blender faces remain a separate topology diagnostic. The allocation varies with visible instance count, projected screen area, and role priority. Preserve primary silhouettes and functional parts through authored LODs and screen-relative transitions. Shared bounded cleanup protects authored data and role-defining forms, limits accumulated sampled surface displacement to 0.02 m, and fails when the asset exceeds its measured allocation. Exporters fail closed when the profile is missing, uncalibrated, or lacks the asset entry.

Keep Blender 4.5.14, Unity 2021.3.45f2, canonical unprefixed sockets, UVs, source hierarchy, rig/pivots, mesh Color RGBA, and the existing PBR/import contract intact. Validate the final FBX and bundle locally. A Blender preview or Unity import does not establish live in-game appearance.
