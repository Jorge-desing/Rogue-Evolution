# Requirements and feedback register

Public edition note (2026-09-17): dated entries below are historical snapshots, not current task instructions. Private logs and original media are not bundled. Their identifiers are provenance only, not reproducible public evidence. Later dated entries supersede earlier pending states without rewriting history.

Use this index at the start of work and when revising requirements. Worklogs hold detailed actions and evidence. Source status does not mean previous models have been repaired. English is authoritative; user statements below are faithful summaries, not verbatim quotations.

## Explicit user requirements — 2026-09-10

| ID | Requirement | Status/location |
| --- | --- | --- |
| R01 | Arrange Shader Editor nodes clearly for editing; no crowded overlap. | Main skill/checklist; awaiting asset trial. |
| R02 | At least BaseColor, Normal, Roughness, and Metalness maps; add emission masks, alpha, displacement, etc. as needed. | Main skill/checklist; awaiting asset trial. |
| R03 | Every model uses only triangles/quads and clean unwrapped UVs; create UDIM UVs/maps when requested. | Main skill/checklist; awaiting asset trial. |
| R04 | Confirm reference analysis and production details before modeling. Ask about unknowns and occluded structures, including omissions. | Main skill/brief template. |
| R05 | Log requests, actions, tests, rationale, and user feedback after every work session. | Main skill/worklog template. |
| R06 | Do not disable selection or Extras in delivered files. | Added in 0.1.1; overrides the earlier suggestion that a mode toggle alone sufficed. |
| R07 | Write authoritative skill documents and worklogs in English; a Traditional Chinese skill reading copy is optional. | Added in 0.1.1; reading copy provided and version-linked. |
| R08 | Product photos may be supplemented by online product research and the findings confirmed with the user. | Added in 0.1.1; research is allowed, consequential assumptions still follow R04. |

## Earlier video feedback — unresolved asset issues

Source: private user video (not distributed). The full transcript and key frames were previously inspected; these are summaries.

| Segment | Feedback | Future effect |
| --- | --- | --- |
| 7:35–9:50, building | Floating lines instead of recesses, fragmented faces, difficult shader graphs. | Check actual depth and joins; useful part structure; R01/R03. |
| 11:12–11:47, scene | Repeated foliage overhead; flag parts and pivots difficult to manipulate. | Select geometry/cards/instances by use; useful origins. This is not a blanket ban on leaf geometry. |
| 13:02–14:24, keyboard | Inaccurate cap-top proportions, legend attachment, and icon. | Confirm dimensions under R04; match lettering to surface and motion needs. |
| 15:03–16:12, cable | Excessive braid geometry, connector join issues, overstretching. | Choose surface representation by shot needs; test endpoints and length. |
| 16:38–19:10, editing | Locked selection, crossed-strip switch stem, cluttered nodes, hard-to-find lights. | Coherent editable construction; R01 and the explicit R06 default handoff state. |
| 19:15–19:49, materials | Restrained studio lighting and missing matte keycap grain. | Neutral-light calibration before art lighting; do not impose a keyboard-specific aesthetic on all assets. |
| 21:35–22:16, monster | Repairing the result may take longer than sculpting again. | Primary shape, transitions, and sculpt usability first; polygon/manifold counts do not establish artistic quality. |

## Implementation details awaiting trial calibration

- Confirm density, padding, geometry/map budgets by use. The user has not supplied universal numeric thresholds.
- Neutral-light checks, shader screenshots, and reopening are evidence methods; report applicability honestly.
- Shared material sets, justified constant maps, and separate generative sources are implementation interpretations. Record and follow a different organization if the user requests it.
- The 3D04 bottle is the first trial. Do not automatically remake the earlier building, keyboard, or monster.

## Adding feedback

Record an ID, date, asset/version, user statement or labeled translated summary, evidence, reproducibility, response, affected rule, test outcome, and resolution status. Mark assistant ideas as proposals. Without user feedback, write pending, not accepted or satisfied. Preserve past results and cross-link the relevant worklog.


## 2026-09-10 — Bottle Stage 1 feedback, incorporated in 0.1.2

Faithful English summary of the user's Chinese feedback: overall relatively satisfied. Accept the current bottle as a high-detail asset and retain the current UV layout/resolutions for now. This is positive Stage 1 review with requested improvements, not unconditional acceptance of every detail.

| ID | Explicit request | Scope / status |
| --- | --- | --- |
| R01 update | Far-left Ctrl+T-style Texture Coordinate and Mapping connected to every image | General rule added; bottle implementation deferred |
| R03 update | Reduce unnecessary faces on nearly flush stickers | General topology principle added; simplify this bottle's label meshes next |
| R09 | Organize Windows texture folders by object category, keeping channels together | General rule added; bottle files/paths deferred |
| R04 update | Ask target count or low/medium/high tier; assess tiers per asset | General rule added; current bottle's high-detail body accepted |
| R03/R04 update | Ask UV sheet count and resolution; prefer square maps | General rule added; current bottle's non-square maps/layout accepted unchanged for now |
| R02 update | Understand the actual material; strengthen this bottle's paper texture | General material principle added; paper-grain strength is this asset's requested revision |

The user instructed this turn to update the skill and record future work only. Implement the bottle improvements together with Rigging in the next resumed production session. See worklog 005 (private record: `Codex_worklog_20260910_005_bottle-feedback-skill-012.md`; not distributed) and next-session plan (private record: `3D04_next-session_EN.md`; not distributed). No Blender/model/texture modification occurs in this specification session. New rule execution remains untested until the next production pass.


## Implementation update — 2026-09-11 / Stage 2 v02

The user resumed production. Mapping chains, reduced panels, category folders, paper grain and the native cap/All Ctrl rig have been implemented and tested in the new Stage 2 package. The glass geometry/UV and all map resolutions are preserved. Current status: self-checks passed; Stage 2 user review pending. R04 polygon-tier and square-sheet questioning awaits the next new-asset trial. See worklog 006 (private record: `Codex_worklog_20260911_006_3D04-refinements-and-rig.md`; not distributed) for results, corrections and limitations. No rule change beyond 0.1.2.

## 2026-09-11 — Stage 2 user review and R10 (skill 0.1.3)

Faithful English summary: the user accepts the materials, but rejects the Object Mode control workflow. Under this user's Blender convention, controls must be Armature pose bones animated in Pose Mode. Ask about rig capability in detail and summarize the requirements for user confirmation before production. Added R10, updated the brief/checklist, and synchronized the Chinese copy.

Bottle-specific requirements: an All control drives the bottle assembly, with its cap control as a child. Initially the cap can translate only along Z; that translation drives Z rotation. After sufficient upward travel clears the bottle, XYZ translation becomes available. Scale is locked. Detached rotation, exact clearance threshold and reattachment workflow remain to be confirmed; do not silently retain v02's Open_Turns or separate free-offset workflow.

Stage 2 v02's previous self-checks remain historical results, not evidence of satisfying this new interaction requirement. Materials accepted; rig needs revision. No model changes in this feedback session. See worklog 007 (private record: `Codex_worklog_20260911_007_pose-rig-feedback.md`; not distributed) and rig brief (private record: `3D04_armature-rig-brief_EN.md`; not distributed). R10 implementation and native interaction tests are pending.

## 2026-09-11 — R10 implementation trial / v03

The user confirmed the rig brief and requested execution. Armature pose controls now replace v02's Object Mode workflow, with native pose interaction, scale, clearance, keyframe and reopen checks. Accepted mesh/UV/material data is preserved. Detached XYZ travel includes downward movement; return above the release plane before aligning and lowering to close. Production checks passed; user review remains pending. See worklog 008 (private record: `Codex_worklog_20260911_008_armature-pose-rig.md`; not distributed). Skill version stays 0.1.3; no new rule introduced.

## 2026-09-11 — Positive v03 review and low-detail derivative request / 0.1.4

Faithful English summary: the user considers this result good and asks to retain the lessons. They identify the existing bottle as high-detail and request the same bottle as a low-detail version, preferably 1,000–3,000 total polygons. They suggest single-face alpha cards for side labels and baking high-detail surface features, including the bottle-mouth screw appearance, onto the low-detail mesh. Preserve the rig and unwrap into one 2048 × 2048 UV atlas. They explicitly require a complete requirement recap before future execution and ask to record it in the skill.

R04 now requires a consolidated pre-execution recap for each new or materially changed scope, followed by user confirmation; no repeated confirmation is required once that scope is approved. v03's high-detail result and pose workflow received positive review. This does not approve a not-yet-built low-detail derivative. The numeric budget and single 2K atlas are local to this bottle request, not defaults for every future model.

The low-detail summary proposes a conservative final-triangle counting convention and distinguishes one UV atlas from its multiple PBR channel images. These interpretations still await confirmation. See worklog 009 (private record: `Codex_worklog_20260911_009_highpoly-acceptance-lowpoly-brief.md`; not distributed) and low-detail brief (private record: `3D04_lowpoly-brief_EN.md`; not distributed). No model or texture changes in this specification session.


## 2026-09-11 — Low-detail confirmation and implementation / LowPoly v01

Faithful English summary: the user approved the recap with all-quad delivery and separate channels sharing one UV, then requested continuation. Implemented 1,375 product quads (2,750 triangle-equivalent), one shared 2048-square atlas with four maps and BaseColor alpha, high-to-low baking and the unchanged accepted Armature rig. Tests and UI/render inspection passed with documented macro silhouette/refraction limitations. The quad amendment and budget are asset-specific. No new universal rule; skill remains 0.1.4. **User review of LowPoly v01 is pending.** See worklog 010 (private record: `Codex_worklog_20260911_010_lowpoly-quad-atlas-production.md`; not distributed). This dated update supersedes the earlier awaiting-confirmation status without rewriting its history.


## 2026-09-11 — Positive LowPoly v01 review and request template / 0.1.5

Faithful English summary: the user says the low-poly result is very good and asks for a reusable description template so users can supply the needed information and make communication/execution smoother. This is positive review of the delivered LowPoly v01 result, not independent certification of every technical property or future asset.

Added a short user-facing intake form, optional rig/pipeline blocks and a revision-feedback form, in authoritative English and a Traditional Chinese reading/fill-in copy. Unknowns can be answered with a request for recommendations. Reuse known answers and convert the intake to the existing R04 recap; blank fields do not authorize guessing or add an extra approval step. No model changes. Template effectiveness awaits a new asset trial. See worklog 011 (private record: `Codex_worklog_20260911_011_request-template-and-lowpoly-feedback.md`; not distributed).


## 2026-09-11 — User-refined template / 0.1.6

Faithful English summary: the user revised the core goals/reference, geometry/material, rig and delivery fields. If information is not supplied, the assistant must proactively ask in the language the user uses. They explicitly include all-quads, triangles, mixed or other topology; atlas count/resolution and UDIM choice; default BaseColor/Normal/Roughness/Metallic with extra-channel questions; detailed rig actions, attachments, locked axes and lifecycle; full recap/tradeoff confirmation before production, preserved sources/new versions and English worklogs.

Implemented in template 1.1 and skill 0.1.6, synchronized both language copies, assistant brief and checklist. The user's "Metalic" spelling was normalized to the standard channel name "Metallic" without changing meaning. Known answers are reused; irrelevant conditional fields are N/A with a reason. "Other" topology is clarified as an asset-specific choice rather than silently banned or treated as general permission for n-gons. See worklog 012 (private record: `Codex_worklog_20260911_012_proactive-intake-and-language.md`; not distributed). New-asset behavior trial pending. No model changes requested or made.


## 2026-09-15 — 3D06 v02 feedback and opening topology / 0.1.7

**F017-3D06-OPENINGS.** Faithful English summary: the user still finds the central hinge and screen effect unsatisfactory, but asks to leave them for now. They explicitly request recording the hole-topology method in the skill for future attention. They want a low version with at most 3,000 faces, preserving current functions; actual hole geometry may be omitted and represented as dark baked detail from the high source. Questions are invited before execution.

Evidence: v01 long side-panel triangle fans in feedback screenshots 174403/174413; v02 local quad-ring rebuild and native wireframe/render evidence in worklog 024. R03 now records local opening patches, bevel/wall continuity, conditional baked-detail substitution and visual verification; the production checklist and Chinese reading copy match. This records a demonstrated method and explicit user request, not blanket approval of v02 or a universal requirement to model/bake every hole.

Hinge and screen-effect quality remain unresolved and intentionally deferred, not accepted as satisfactory. Proposed low version reuses v02 mechanics and its render-only compositor effect. Whether 3,000 means evaluated triangles or Blender polygon faces remains a question; previous 4K/shared-atlas and mixed triangle/quad requirements are reused in the consolidated brief. No production edits in this session. See worklog 025 (private record: `Codex_worklog_20260915_025_openings-skill-lowpoly-brief.md`; not distributed). Next relevant trial: verify the agreed low budget, baked static holes and preserved controls; this trial is pending confirmation.


## 2026-09-15 — Low v01 panel fans and pre-bake smoothing / 0.1.8

**F018-3D06-PANELS-NORMALS.** Faithful English summary: the user likes the polygon-budget control, but rejects the long triangle-fan topology visible in screenshots. Use quads or reasonable local diagonals wherever practical, with more even edge distribution. Establish Auto Smooth before baking; compare Shade Auto Smooth and ordinary Shade Smooth by surface, with angle-controlled smoothing preferred for this opening/recess case.

Three supplied screenshots show rounded back/outer-screen panels, camera capsule/caps and side-opening shading. Source inspection confirms boundary-vertex fan construction in `plate()` and circular caps, plus `use_smooth` selected by triangle/quad type. Weighted Normal was applied selectively; there was no deliberate angle-smoothing comparison/pre-bake gate. Screenshots alone do not prove all side shading defects have one cause.

R03 now covers balanced panel/cap topology beyond holes. R02 and the checklist require smoothing/normal-state selection before bake, consistent evaluated tangent basis and rechecking/rebaking after relevant changes. English/Chinese versions are synchronized. Mixed topology and the 3,000 evaluated-triangle budget remain unchanged; no universal all-quad, fixed-angle or equal-density mandate is introduced.

Low v01 budget received positive review, not whole-asset acceptance. Proposed low v02 topology/normal revision awaits consolidated confirmation; no blend changes this session. Hinge and screen-effect appearance remain deferred. See worklog 028 (private record: `Codex_worklog_20260915_028_panel-topology-smoothing-feedback.md`; not distributed), including preserved screenshot evidence. Next trial must show improved panel/cap wireframes and pre/post-bake shading within the existing budget.

## 2026-09-15 — Positive low v02 review and bake-proxy lessons / 0.1.9

**F019-3D06-LOW-V02.** Faithful English summary: the user says the result is very good, asks to record the shortcomings in the skill, and supplies 3D07 as the next trial. This is positive review of the delivered low v02 correction; it does not retroactively erase low v01's faults or explicitly resolve the deferred hinge/screen appearance concerns.

Worklog 029 documents 1,457 quads + 16 triangles, 2,930 evaluated triangles, redesigned panel/cap fills, per-surface smoothing, four rebaked 4K channels and preserved native rig behavior. Actual wireframe/neutral/baked views, coverage, UVs, pose interaction and destination reopening were checked. This supersedes F018's awaiting-production status through a dated update; its historical record remains intact.

The additional reproducible failure was normal drift during joined/spatially isolated bake-proxy preparation, even after target normals were selected. Using evaluated meshes, compact offsets, restored corner normals and correspondence checks corrected it within declared tolerances. The source's prior tangent-normal map was disabled on temporary high copies to avoid stale-basis reprojection; fine normal detail was not independently restored, a disclosed case-specific tradeoff. Do not convert that choice into a universal normal-map removal rule.

R02 and the production checklist now make these operational checks explicit. R03's existing balanced topology rule remains in force; no mandatory all-quad topology, uniform density, fixed smoothing angle or global polygon budget was added. English original and Traditional Chinese copy are 0.1.9. See production worklog 029 (private record: `Codex_worklog_20260915_029_low-v02-topology-normals.md`; not distributed) and feedback/intake worklog 030 (private record: `Codex_worklog_20260915_030_skill019-3D07-intake.md`; not distributed).

3D07 is a new building trial. The earlier architectural issues remain relevant: floating strips instead of recesses, fragmented surfaces and unreadable shader graphs. Its own dimensions, hidden-side interpretation, props, viewing distance and acceptance points must be confirmed; the phone's settings do not become building defaults.


## 2026-09-16 — Accepted 3D07 Stage1 material workflow / 0.1.10

**F020-3D07-MATERIAL-WORKFLOW.** Faithful English summary: the user finds the model acceptable, says this validates earlier skill requirements and resolves many texture issues, and asks to preserve the successful workflow before reading 3D08.

This accepts the delivered Stage1 material model; it does not retroactively rewrite worklog 032's then-pending feedback, certify photographic identity, resolve the stated close-view density limits, or complete the unproduced <=3,000-triangle building derivative. No earlier Blender asset was changed.

Promoted the demonstrated material-source/atlas split, material-specific scale and response, completed-source dependency, approved-tile UV validation and matched-view comparison into [material-authoring-and-bake-workflow.md](material-authoring-and-bake-workflow.md). Existing normals, readable nodes, four-map, folder and reopen rules remain authoritative. No universal smoothing angle, noise setting, tree method or polygon budget was introduced. English and Traditional Chinese copies are 0.1.10.

See production worklog 032 (private record: `Codex_worklog_20260916_032_3D07-materials-trees.md`; not distributed) and feedback/intake worklog 033 (private record: `Codex_worklog_20260916_033_skill010-3D08-intake.md`; not distributed). Next trial: 3D08 laptop materials, fine keyboard emission masks and verified hinge/press poses, subject to the user's consolidated confirmation.

## 2026-09-17 — Public packaging / 0.2.0

The user requested a shareable package named blendsmith, an original-skill ZIP backup, and selected GPL-3.0. The public package uses GPL-3.0-only. Existing R01–R10 IDs and substantive production requirements remain. Machine-specific logging was replaced with a portable project-workspace fallback that preserves configured central logs. Private record links became explicit provenance identifiers; public contribution templates require new shareable evidence.

Current trial boundary: 3D08's first high exterior/material package has been delivered for review (private worklog 034); user acceptance, rig and low derivative remain pending. This supersedes the earlier intake-pending wording, without certifying geometry or motion quality. 3D07's final low derivative is unproduced; earlier 3D06 hinge/screen appearance concerns remain deferred. Packaging checks do not count as a new Blender trial.

## 2026-09-18 — Script-only handoff failure / 2026.09.011

**F021-ACTUAL-BLEND-DELIVERY.** Faithful English summary: in a trial on another computer, the delivered folder lacked a Blender file and the user had to run PowerShell to generate it. The user expects the assistant to execute generation and deliver the actual model.

The report establishes the user's delivery requirement; the other computer's output and execution environment were not independently inspected. Do not infer a particular tool failure or missing installation. R11 makes execution, actual saved output, fresh reopen and an accessible handoff explicit, with a script-only exception only when requested. The checklist and brief/worklog templates record the evidence. English and Traditional Chinese copies are synchronized. No previous asset was modified or newly accepted. Document checks do not prove future assistant compliance; the next relevant model trial must verify the delivered folder contains a usable `.blend` without user-run generation.
