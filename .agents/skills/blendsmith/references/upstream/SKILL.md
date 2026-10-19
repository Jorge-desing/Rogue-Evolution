---
name: blendsmith
license: GPL-3.0-only
description: Create, edit, or review editable Blender assets under the user's production requirements for reference confirmation, triangle/quad topology, UVs and UDIMs, PBR textures, readable shaders, rig usability, and delivery checks. Also use for revising these requirements from asset trials and user feedback and maintaining English worklogs.
metadata:
  version: "2026.09.011"
  status: "Public-preview package; production rules inherited from 0.1.10. Historical trial evidence has limited scope."
---

# blendsmith — Editable Blender Asset Production

## Purpose and authority

Produce assets the user can directly edit, texture, rig, and export. Attractive renders do not replace accurate shapes, useful topology, clean UVs, manageable performance, or a usable handoff.

These are blendsmith project conventions developed through asset trials, not a universal game/VFX standard or certification. Explicit user instructions take precedence; record agreed departures in the production brief. Confirm each asset's actual pipeline and budgets. Retain the existing Blender 4.4 requirement unless the user changes it; record the exact version and consider compatibility before switching.

**R07 — Language:** English is authoritative for this skill, its supporting documents, and all worklogs. Keep the [Traditional Chinese reading copy](SKILL.zh-TW.md) aligned to the same version. If wording diverges, the English original governs, subject to the user's latest instructions. User-facing questions, explanations and recaps follow the language the user is using or explicitly requests; do not impose English or Traditional Chinese on another-language user. Label translated user feedback as a translation or faithful summary, not a verbatim quote.

Stay within the current task. Specification-only or review-only requests do not authorize model edits. For a local correction, report unrelated defects without silently turning it into a complete asset rebuild.

## Start and record the work

1. Read relevant recent worklogs and applicable entries in [feedback-register.md](references/feedback-register.md).
2. Identify inputs, output directories, and files authorized for modification. Preserve original references and user source files; use new versioned filenames for model revisions.
3. Reuse a user-configured central worklog directory. Otherwise use `worklog/` within the active asset/project workspace and record its location in the brief. Do not write production logs into a globally installed skill folder or silently relocate an existing log. Private worklogs are not automatically public contribution records.
4. **R05 — Worklogs:** Use [worklog.template.md](assets/worklog.template.md) after every work session, including analysis, specification updates, and waiting for answers. Write in English, name entries by date and sequence, and preserve historical results.

## User-facing request template

Offer the [English request template](assets/user-request.template_EN.md) or its [Traditional Chinese reading/fill-in copy](assets/user-request.template.zh-TW.md) when useful. Apply its core information checklist even to free-form requests. Actively ask about any missing applicable field in the core request template, even when the user writes a free-form message rather than filling in the form. Ask questions, explain choices and recap requirements in the language the user is using (or their explicitly requested language); technical channel names may remain unchanged. English remains authoritative for stored skill documents, briefs and worklogs. Reuse already supplied/confirmed answers. A blank field is missing information, not "not needed" or permission to guess. If the user does not know, offer concrete options and tradeoffs and include the proposed choice in the confirmation recap. Explicit delegation can resolve a field; silence cannot. Group related omissions into manageable questions, retain unanswered items, and continue reference analysis while awaiting answers. Ask rig-needed/preserve/new first; detailed rig questions are N/A when no rig is needed. Likewise, no-render delivery makes lighting/image settings N/A. Do not require irrelevant advanced pipeline fields or ask answered questions again.

Convert the intake into the existing production brief and R04 recap; do not add another approval gate or replace already confirmed scope. Ask about atlas count/resolution and UDIM explicitly. The default channel set is BaseColor, Normal, Roughness and Metallic; ask about extras. Topology choices include all quads, all triangles, mixed, or other. For "other", clarify the intended representation and any asset-specific departure from R03 in the consolidated brief; explicit user instructions take precedence. Bottle-specific numbers remain examples, not defaults.

## R01 — Readable Shader Editor

Arrange every delivered material and its nested groups for editing. Use a left-to-right flow: coordinates/textures, adjustments, BSDF, Output. Use Frames for purposes and meaningful names for images, groups, and important parameters. Nodes and labels must not obscure each other; connections must be traceable. Excessive collapsing or opaque groups must not conceal a confusing graph. Verify the actual Shader Editor view, not just node coordinates in a script.

Use the Ctrl+T-style **Texture Coordinate → Mapping → Image Texture** setup at the far left of each material. Use UV coordinates for UV-authored maps and connect the Mapping output to **every image texture**, including Normal, Roughness, Metallic and optional masks. Programmatically creating the equivalent nodes is valid; Node Wrangler is not a delivery dependency. Share one Mapping transform across aligned channels of a texture set, starting with identity transforms so the approved appearance is preserved. Separate coordinate chains only when the surface requires them, label their purpose, and verify channel registration and tangent-normal behavior after mapping changes.

## R02 — BSDF-based materials and at least four texture maps

Each surface material set used by the final asset must have actual, editable, delivered and correctly connected **BaseColor, Normal, Roughness, and Metallic/Metalness** texture maps. Materials, atlases, and images may be shared across objects; duplicate image files per object are not required. Every used material must resolve to its texture set.

Procedural authoring is allowed, but four numeric sockets or empty placeholder images do not satisfy this requirement. Bake/export the authored appearance to the confirmed UV layout. A physically justified constant map is valid for a uniform channel, such as Metallic=0 for a dielectric; document its purpose rather than inventing detail to fill a channel.

Consider emission masks, alpha, displacement, transmission, subsurface, and coatings where the material needs them. Four maps alone do not prove faithful material reproduction. An image alpha channel may carry an emission mask, but **do not automatically connect that mask to surface transparency**. See [production-checklist.md](references/production-checklist.md) for mapping, color interpretation, and delivery checks.

Before authoring texture detail, identify the observed material, coating, finish, and feature scale. Build the characteristic surface response across BaseColor, Roughness and Normal rather than applying generic noise. For paper, consider fibers, pores, coating and print; judge their strength at the intended viewing distance under neutral and grazing light. Increase visible paper grain when the user requests it, while preserving print legibility and avoiding an unrelated fabric or stone appearance.


**Finalize smoothing before baking:** Make a deliberate per-surface comparison of Shade Auto Smooth / Smooth by Angle, Shade Smooth and intentional flat shading before generating the final bake. For the user's hard-surface opening/recess workflow, establish angle-controlled smoothing and required sharp edges on working bake meshes first; inspect both the high source normals and the low target normals. Use ordinary Shade Smooth for continuously smooth surfaces when the comparison supports it, and record the choice. Do not choose flat/smooth solely from triangle-versus-quad face type. Weighted Normal is an optional normal-weighting operation, not a substitute for deciding hard edges and angle smoothing.

Finalize topology, UVs, sharp edges/smoothing angle, custom normals and relevant modifier order before the tangent-space bake. Bake with the same evaluated normals and triangulation that will shade the delivered model; an isolated or joined bake proxy must preserve them. Keep an editable quad cage where requested, while maintaining consistent evaluation diagonals. Changes to these inputs after baking require rechecking and rebaking affected maps; adding Auto Smooth only after a normal bake is not the complete fix. Compare a neutral material with the normal map disabled, then the baked result under neutral/grazing light and relevant rig poses. Record actual angles and comparison results rather than imposing a universal angle or claiming smoother normals will fix bad topology or bake projection errors. Blender 4.4 implementation context: [Smooth By Angle](https://docs.blender.org/manual/en/4.4/modeling/modifiers/normals/smooth_by_angle.html).

**Verify the bake proxy, not only the source object:** Copying unevaluated mesh data can omit modifier-generated normals; joining or spatially separating small parts can change automatic normals and numerical precision. Use the intended evaluated target state and verify corresponding triangle corners, UVs and split/custom normals after proxy preparation, within recorded scale-appropriate tolerances. Preserve or restore normals when necessary; do not assume a successful object join preserves the shading basis. Inspect any inherited tangent-normal input on a re-smoothed high source: decide whether to retain, reproject, replace or separately recover it, avoiding double-encoded shading or silently discarded microdetail. See the [bake-proxy checks](references/production-checklist.md#bake-proxy-consistency) before matched-part baking. Store angle smoothing as native sharp-edge/normal data or an appropriate modifier and document which; the 3D06 angles, tolerances and grid dimensions are examples, not global defaults.

**Reusable source-to-atlas workflow:** When authoring procedural materials for baked delivery, preserve an editable source alongside the versioned atlas derivative, explain their roles and rebake relationship, and distinguish same-geometry material baking from actual high-to-low projection. Read [material-authoring-and-bake-workflow.md](references/material-authoring-and-bake-workflow.md) for the 3D07-tested sequence, surface-scale checks, single-tile packing and delivery evidence. User acceptance of a review stage does not certify an unfinished low derivative.

## R03 — Triangle/quad topology and clean UVs

Every delivered model mesh must contain only triangles and quads: no n-gons. Every mesh needs valid, actually unwrapped UVs, including modeled hidden or occluded components. Cameras, lights, armatures, and controllers without model surfaces are not surface meshes.

Check both the editable base mesh and the final evaluated geometry. A last-step Triangulate modifier must not hide n-gons in the editable source, and hiding a noncompliant component does not exempt it. Curves, text, and Geometry Nodes may aid authoring, but delivered surfaces need compliant editable meshes and UVs. Agree beforehand on the separation between retained generative source files and delivery meshes.

Zero n-gons does not prove useful topology. Arrange edges for the shape, subdivision, and deformation needs; avoid arbitrary fan cuts, sliver triangles, accidental holes, and duplicate surfaces. Do not force legitimate thin sheets into closed solids.

**Balanced panel and cap topology:** When the brief prefers quads, use reasonably distributed quad patches/strips, grid fills or local diagonals wherever they fit the shape. Do not fill a rounded panel, screen, capsule or circular cap by connecting every boundary vertex to one boundary point by default; a flat surface is not an automatic exception to editable edge-flow quality. Keep necessary triangles local and reasonably proportioned instead of spanning the whole panel. Judge circular-cap grids and pole placement by shading/editability, not quad percentage alone. Evenness means appropriate distribution for curvature and deformation, not equally dense subdivision of every flat region. Retain the agreed evaluated-triangle budget: pairing two triangles into a quad improves editing but does not reduce its two-triangle rendering cost. Do not increase the budget or reinterpret mixed topology as mandatory all-quads without user confirmation.


**Openings and recesses:** Plan each opening's perimeter and its connection to the surrounding surface before cutting. For a real hole, prefer a local ring/O-grid or equivalent controlled triangle/quad patch with consistent loops through the bevel and wall thickness. Match the patch boundary to the surrounding panel; avoid long thin fan triangles connecting a hole to distant corners or unrelated openings. Boolean tools may assist authoring, but clean their result; automatic triangulation, zero n-gons, or smooth normals alone do not establish acceptable edge flow. Add segments for visible curvature and shading needs, not a fixed count copied from another asset. Inspect the editable wireframe and neutral/grazing-light close-ups for pinching, waviness, uneven bevels, silhouette damage and deformation across relevant poses. Read the opening checks in [production-checklist.md](references/production-checklist.md) for verification.

For a budget-limited derivative, classify openings by visible silhouette, depth/parallax, deformation and interaction. With the user's agreement, a small static recess may become baked dark color, tangent normal and roughness detail on a continuous low mesh. This does not create a true opening or correct close-up parallax. Do not flatten movable button caps, required clearances or silhouette-critical openings merely to save faces. Keep corresponding bake parts isolated so nearby moving parts do not project onto the wrong surface; check registered channels and the result at the intended distance and at motion extremes. The 3D06 user's permission to omit hole geometry is asset-specific, not a blanket rule for all products.


Check UV overlaps, degeneracy, stretch, seams, padding, texel density, and texture coverage. Default to non-overlapping UVs. Explain and confirm mirrored, stacked, or tiled reuse per asset. When the user requests UDIMs, confirm tiles and receiving-pipeline support, then deliver the UV layout and corresponding maps for every required channel, not merely UVs in multiple tiles.

Allocate topology by silhouette, curvature, attachment and deformation needs. Nearly flat stickers or labels should use the minimum useful triangle/quad layout that preserves their outline and clean surface contact; do not copy a dense bottle grid onto them by default. Inspect curved borders and penetration after reducing geometry, and retain the approved artwork/UV alignment.

## R04 — Confirm references, unknowns, and occluded structures before modeling

**Restate the complete request before execution.** For each new asset task or materially changed scope (including a new detail tier, retopology/baking, UV/material changes, or a rig revision), give the user a clear consolidated description in their language before modifying production assets. Include the intended result, source/version to preserve, appearance and geometry changes, included/omitted parts, polygon counting convention and budget, UV atlas count/resolution, material channels and baking approach, preserved/changed rig behavior, deliverables, and consequential quality tradeoffs. Distinguish explicit requests, reused confirmations and assistant proposals. Ask the user to confirm the summary and resolve the remaining consequential ambiguities. A complete summary is required even when most individual details are already known. After the user confirms that scope, execute it without asking again for the same requirements; pause only newly affected work when a new consequential change arises. Documentation and reference analysis may continue before confirmation.

Before generating a model, complete [production-brief.template.md](assets/production-brief.template.md) from references actually inspected. Distinguish observations, confirmed facts, hypotheses, uncertain dimensions/proportions, contradictory sources, and invisible or optional structures.

Batch questions that affect shape, construction, materials, or workload: for example, keycap top dimensions, undersides, switches, shell thickness, and connector internals. For occluded parts, explain full, simplified, or omitted construction and its consequences. Ask whether the list misses another required structure. **Do not begin generating the model until the production details are confirmed.** Reference research, known-dimension analysis, and documentation can continue.

Reuse existing confirmed specifications and explicitly authorized estimation ranges. New consequential unknowns require an answer before making the affected part; unaffected, confirmed work can proceed. Silence is not approval. Do not present projected image measurements or inferred dimensions as measured product data.

For each new asset, ask the user for a target polygon count or **low / medium / high** detail tier before modeling. Interpret a tier for that asset's complexity, shot distance and purpose; propose concrete base and evaluated budgets, state whether counts mean faces or triangles, and record the user's confirmation or explicit delegation. Do not invent universal tier thresholds. Ask how many **square UV texture sheets/atlases** they want and the resolution of each (for example 1024 × 1024 or 4096 × 4096), plus UDIM needs. Sheet count means UV layouts/atlases, not the number of PBR channel files. Prefer square maps. Reuse existing answers, and preserve an accepted asset's non-square layouts or higher geometry budget until the user requests a change.

## R08 — Supplementary product reference research

When user-supplied photos depict a product, proactively search for useful additional product images or specifications when they can resolve identity, proportions, materials, or hidden construction. This research does not require a separate permission question. Prefer exact-model manufacturer documentation and product photos; use reliable retailers or reviews for additional angles where useful.

Record source URLs, access dates, product/model/variant, the relevant image or specification, and what it actually establishes. Verify layout, generation, finish, region, and revision as relevant; do not substitute a visually similar variant without disclosure. Separate promotional images, measured specifications, and inference. A found page that has not been inspected is not visual evidence.

Present consequential supplementary findings alongside the user's references and ask the user to confirm their applicability in the R04 brief. Explain contradictions; never silently replace the user's reference. Searching is authorized, but adopting uncertain dimensions or adding unseen internals still follows R04. If sources are inaccessible or inconclusive, state the gap and ask for the missing information. Do not present a public product image as a licensed texture asset merely because it is online.

## R06 — Delivery keeps selection and Extras enabled

At handoff, enable selection for all objects and Collections in the delivered file. Do not leave object or Collection `hide_select` locks enabled, including locks inherited from parent Collections. Enable viewport overlays and `show_extras` in every delivered 3D View so lights, cameras, and other extras remain accessible in the intended view. Check both saved settings and practical selection after reopening the file.

Do not deliver a controls-only locked scene, require the user to run an unlocking script, or globally hide Extras for presentation. If a separate animation-only mode is explicitly requested later, make it reversible and restore selection and Extras before saving the default handoff state. This supersedes earlier suggestions that a mode toggle alone was sufficient while selection remained locked at delivery.

## R09 — Texture folders by object category

Organize external texture files into meaningful object-category folders, keeping each category's complete material channels together; for example `textures/glass/`, `textures/cap/`, `textures/labels/front/`, and `textures/labels/back/`. Multiple material sets in a category may use clearly named subfolders. Put shared maps in one clearly named shared/category folder and record their consumers rather than duplicating them. Match Blender image paths and the texture manifest to the hierarchy. After reorganizing, verify both external and packed images, relative paths, and a fresh reopen. Packing does not replace the requested on-disk organization.

## R10 — Armature controls and a confirmed rig brief

Under the blendsmith Blender workflow, a request for Ctrl/controllers requires an **Armature with animator-facing pose bones operated and keyframed in Pose Mode**. Object Mode empties or standalone curve controls do not satisfy it. Optional custom shapes only display the pose-bone controls; the shape objects must not become the animator's transform targets. This is a blendsmith project convention, not a claim that all Blender rigs must work this way.

Before building or materially revising a rig, ask what the user needs to animate and to what extent. Describe and summarize the hierarchy, attachment behavior, transform spaces and axes, input-to-motion relationships, limits, scale policy, disengagement/reattachment behavior, and keyframing workflow. Separate confirmed requirements from proposed defaults, then have the user confirm that summary before implementing affected controls. Reuse answered questions; do not add repeated approvals after confirmation. Use the rig section in the production brief and keep asset-specific mechanics there.

Validate the actual Pose Mode workflow: ordinary transform tools and keyframes, permitted and blocked channels, parent motion, attached details, transition boundaries, reverse motion, save/reopen, and playback/scrubbing. Transform-channel locks are distinct from object/bone selection locks: retain R06 selection and Extras while enforcing agreed motion limits. Helper bones, constraints and drivers may support the rig, but keep the animator-facing controls clear. A passing mathematical driver test alone does not establish a usable animator workflow.

## R11 — Deliver the actual Blender file

When the agreed deliverable is a Blender asset, execute the authorized build yourself and save a versioned `.blend` in the agreed delivery folder. Generation scripts (`.py`, `.ps1`), commands, previews and instructions are supplementary sources; they do not replace the requested model. Do not leave the user to run PowerShell or Python to obtain the first usable `.blend`, unless the user explicitly requested script-only delivery.

Early in production, verify that the available tools can operate Blender and write to the output location. If execution is unavailable or fails, state the concrete blocker and that the model has not been generated; label any supplied script as unexecuted/partial work. Do not describe a script-only package as a completed asset or silently install software beyond the authorized scope.

Before claiming delivery complete, wait for generation/save to finish, verify the exact file exists and has nonzero size, and reopen that saved file in a fresh Blender process. Inspect the expected scene and required dependencies using the [delivery checklist](references/production-checklist.md#7-save-reopen-and-deliver). An old file left by a failed build, a successful script exit, or file size alone does not prove the current deliverable is valid. Record the actual filename/version, size, save and reopen results; unresolved required checks remain incomplete.

Provide a clickable link to the actual `.blend` in the user's accessible output folder, or attach the file through the host's supported download mechanism. Verify the exposed artifact matches the checked file. A path that exists only in an inaccessible assistant environment is not a completed handoff. Report any access/attachment limitation explicitly; do not claim a file appeared in a Files panel without evidence. Apply this rule to each agreed asset-delivery milestone. Analysis-only, review-only and explicitly script-only tasks do not require a new model file.

## Production and verification

1. **Confirm the brief:** purpose, camera distance, dimensions, internals, materials/maps, UV/UDIM plan, rig, budgets, and output formats. Do not invent universal industry numbers. Agree on any user review milestones rather than adding repeated approvals later.
2. **Establish primary shapes:** compare silhouettes, proportions, joints, and multiple clay views before adding fine detail. Use any review milestones already agreed with the user.
3. **Build editable structure:** choose functional parts, origins, names, Collections, materials, and modifiers. Use instances for repetition where useful and allocate small surface detail to textures or geometry according to the shot. More polygons are not a substitute for sound construction.
4. **Finish topology, UVs, shading, and controls:** follow R01–R03 and R10 for rigs. Before applying transforms/modifiers, consider dimensions, UVs, normals, parenting, and rigs. Moving parts, legends, and attached details must move together.
5. **Check actual results:** apply the relevant [production checklist](references/production-checklist.md). Record PASS, FAIL, NOT RUN, or N/A with evidence. A beauty render, manifold count, or face count cannot substitute for production validation. Include R06 handoff checks.
6. **Deliver and log:** apply R11, then provide versioned `.blend` files, editable texture maps and necessary dependencies, brief operating notes, actual test results, and limitations. Write the worklog and provide inspection entry points.

## Improve through asset trials

Wait for the user to identify the next model; creating this skill does not authorize choosing one and starting automatically. Each round records request, actions, tests/observations, concise decision rationale, delivery, user feedback, and any rule revision. Record evidence-based rationale and tradeoffs, not private step-by-step deliberation.

Keep feedback tied to its date, source, asset, and version. Append dated feedback to the original log, or cross-link a new feedback log. Never rewrite previous test outcomes to make an earlier delivery appear accepted. An authorized language translation may change wording but must preserve historical facts and identify itself as a translation.

Promote explicit user requirements and reproducible workflow failures into appropriate rules. Keep one-asset preferences local, and mark assistant proposals as unconfirmed. Increment the skill version when requirements change, explain differences and compatibility implications in the worklog, synchronize the Chinese copy, and validate formatting and links. Mark rules awaiting actual asset trials; test whether the next relevant trial reduces rework.
