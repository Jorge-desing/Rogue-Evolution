# Editable material sources and dependable atlas delivery

Use when procedural or mixed material authoring must become an editable image-texture delivery. This workflow was exercised on 3D07 Stage1 and accepted by the user on 2026-09-16. It complements R01–R03/R09; it does not replace asset-specific requirements or certify unfinished stages.

## Author from observed surfaces

Make a material/feature inventory from inspected reference crops. Distinguish substrate, coating, reflection, grain, seams, printed artwork and geometric relief. Record approximate real-world feature scale and unknowns. Reference photos include lighting; do not indiscriminately paste photographic shading into BaseColor. Supplemental artwork must match the observed variant, with provenance and any approximation disclosed.

Tune BaseColor, Roughness and Normal together at the agreed viewing distance. In 3D07, material-specific brick courses, mortar and stone grain were useful; reducing excessively broad metal colour noise corrected an unintended stone-like finish. This is a decision principle, not a universal noise range. Inspect neutral and grazing light before presentation lighting.

## Preserve source and derivative roles

Retain a clearly named editable material-source file and a versioned baked derivative when the source contains useful procedural controls. Include the UV/artwork inputs needed to rebake. Explain which file edits parameters and which edits baked pixels. Source edits require an explicit rebake; the two files are not automatically synchronized. Separate geometry tier from material representation in filenames and notes.

Record whether a bake transfers materials on the same geometry or projects a high source onto a different low mesh. The first does not satisfy a requested high-to-low bake. Report evaluated counts for each agreed asset group; review-stage acceptance does not imply the final budget has been met.

## Stabilize UVs and shading before baking

Use R02's final smoothing and bake-proxy correspondence checks. Resolve tiny/degenerate conversion patches and transforms before final UV packing and normal evaluation; avoid replacing large panel fans with other uneditable fills. Do not copy numerical cleanup thresholds from a metre-scale building to a small product.

For a single atlas, explicitly constrain packing to the approved tile and verify every island remains within it, especially after increasing priority-island scale. A pack operation completing successfully does not prove single-tile compliance. Inspect overlap, padding, stretch, coverage and texel density. Prefer density where users read labels or inspect close features, while retaining approved reuse rules.

Wait for source rebuild/save completion before launching dependent baking. Verify the actual source version loaded. A stale source or failed save invalidates the derivative even if all image files exist. Recheck and rebake affected channels after material, UV or tangent-basis changes.

## Compare and hand off

Compare the authored source and baked derivative at matching views, lighting and exposure. Include the closest agreed view and an oblique/grazing view. Judge channel registration, relief direction, seams, lettering, roughness and metallic response; do not judge only the attractive wide shot. Quantify atlas density when close detail is soft. More geometry cannot restore missing texels. Propose repacking, another atlas or approved tiled detail when necessary; do not silently exceed the texture contract.

Connect all delivered channels through the readable shared coordinate/mapping chain, with appropriate colour interpretation and Normal Map UV. Follow R09's category folders, relative external files, packing and fresh destination reopen. Inspect the actual Shader Editor and report screenshot coverage. Record evidence and limitations against the delivered version.

## Scope of the evidence

3D07 Stage1 demonstrated this workflow with four-channel atlases, retained procedural controls, proxy-normal checks, actual Shader Editor inspection and destination reopening. The user found it acceptable and said it resolved many texture problems. Whole-building 4K close-view softness and approximate lettering remained disclosed. The 13,014-triangle source building was not the promised final <=3,000-triangle derivative. Three simple trees and their separate atlas/budget are local choices, not future defaults.
