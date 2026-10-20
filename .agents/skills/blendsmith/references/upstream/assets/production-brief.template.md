# Pre-production brief — {{asset name}}

Write the record in English. Discuss it with the user in their preferred language.

- Date / skill version:
- Current scope / output location:
- Input files and references: inspected / not yet accessible:
- Blender and receiving-application versions:
- Purpose: game / VFX / product visual / other; nearest view and deformation needs:

## Intake completeness and communication

- User's current/requested language for questions and recap:
- Core request fields: provided / previously confirmed / missing and asked / explicitly delegated / not applicable with reason:
- Related omissions asked together; unresolved replies and affected scope:
- Explicit topology choice: all quads / all triangles / mixed / other clarified in asset brief:
- Atlas count/resolution and UDIM choice; default four channels and extra-map request:

Use [the user request template](user-request.template_EN.md) as the checklist even for free-form requests. Ask missing applicable items in the user's language; do not infer that blank means no rig, no render, permission to guess, or approval. Reuse known answers and record explicit delegation. Keep this stored brief in English.

## Reference analysis and questions

| Part/item | Visible evidence and source | Confirmed dimensions/proportions | Hypothesis or unknown | Question for the user | User decision/source |
| --- | --- | --- | --- | --- | --- |

An estimate does not belong in the confirmed column. Label perspective effects, occlusions, and conflicts. Batch consequential questions, provide understandable choices or annotated images, and reuse existing answers.

## Supplementary product research — when useful

| Source URL / access date | Exact product/model/variant | Image/spec inspected | Finding and limits | Agreement/conflict with user's reference | User confirmation if consequential |
| --- | --- | --- | --- | --- | --- |

Prefer exact-model primary sources. Disclose related-but-different variants. Research is allowed without a separate permission question; uncertain production choices still require confirmation. If no research was useful or accessible, record why.

## Occluded components and internals

| Component | Why it may be needed: shot/teardown/rig | Available reference / missing information | Full/simplified/omitted proposal and impact | User choice |
| --- | --- | --- | --- | --- |

Ask whether another necessary structure is missing. Invisible does not automatically mean unnecessary, and absent references do not authorize inventing genuine product internals.

## Production specification

- Units, dimensions, origin, orientation:
- Parts, names, Collections, generative-source/delivery-mesh separation:
- User target polygon count or low/medium/high tier; asset-specific rationale:
- Confirmed/delegated base and evaluated budgets; faces vs triangles; subdivision/LODs, deformation:
- Lightweight label/decal topology plan and attachment checks:
- Number of UV sheets/atlases and square resolution per sheet (not PBR channel count):
- UV map, 0–1 or UDIM, allowed reuse, texel density, padding; accepted non-square exceptions:
- Material sets, four required maps, extra channels, resolutions/bit depths/color interpretation:
- Texture Coordinate → Mapping → every Image Texture; shared coordinate groups:
- Observed material finish and characteristic detail scale (e.g. paper grain):
- Object-category texture folder hierarchy and shared-map consumers:
- UDIM tile and channel-file plan, or N/A:
- Controls, motion ranges, cable stretch allowance:
- Handoff state: object/Collection selection enabled; overlays and Extras enabled in all saved 3D Views:
- Lighting direction, atmosphere, neutral inspection setup:
- `.blend`, editable maps, additional exports and receiving-app tests:
- R11 execution readiness: Blender/tool availability, writable output folder, expected versioned filename and user-accessible delivery method:
- Agreed user-review milestones and work allowed to continue without another review:

## Rig specification and user confirmation — R10, when controls are requested

- Required animation actions, range and level of control:
- Armature / Pose Mode controls; hierarchy and attached components:
- Each control's location/rotation/scale channels, coordinate spaces and locks:
- User input and driven response, units, direction and limits:
- Engaged, transition and detached states; reattachment/alignment workflow:
- Keyframing, playback, scrubbing and export needs:
- Confirmed requirements versus assistant proposals and consequential unknowns:
- User-facing summary, user answer/date, and status awaiting confirmation / confirmed:
- Planned native Pose Mode interaction and transition tests:

## Complete user-facing requirement recap — R04

- Intended result and source/version preserved:
- Explicit requests, reused confirmations and proposed choices:
- Geometry/part changes, budget/count convention and UV/material/bake plan:
- Rig behavior to retain/change and deliverables:
- Consequential quality tradeoffs and remaining questions:
- Summary presented to the user / date:
- User confirmation or requested correction / date:

## Basis for starting production

- Confirmed items and message/date:
- Explicitly authorized estimation ranges and limits, or none:
- Open questions and affected parts:
- Status: awaiting confirmation / confirmed to start.

Before confirmation, reference research and documentation may continue; do not generate the model. Filling in this template does not itself constitute user approval.
