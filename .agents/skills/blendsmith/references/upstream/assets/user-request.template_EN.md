# 3D asset request template — English original

Template version: 1.1. Skill compatibility: 0.1.6. This English document is authoritative; `user-request.template.zh-TW.md` is its Traditional Chinese reading/fill-in copy.

Fill in what you know; use "Please propose options" for unknowns and "Not needed" for irrelevant sections. Plain descriptions of intended results are enough; the assistant translates them into technical specifications. Examples illustrate choices, not automatic defaults. A completed form is input to the requirement recap, not approval of unresolved proposals.

Use the short form to start. Add the optional blocks only where they affect the job. Attach or link references, and name the relevant files if a folder contains several assets. The assistant must not claim to have inspected unavailable files.

## Missing information and language

Actively ask about any missing applicable field in the core request template, even when the user writes a free-form message rather than filling in the form. Ask questions, explain choices and recap requirements in the language the user is using (or their explicitly requested language); technical channel names may remain unchanged. English remains authoritative for stored skill documents, briefs and worklogs. Reuse already supplied/confirmed answers. A blank field is missing information, not "not needed" or permission to guess. If the user does not know, offer concrete options and tradeoffs and include the proposed choice in the confirmation recap. Explicit delegation can resolve a field; silence cannot. Group related omissions into manageable questions, retain unanswered items, and continue reference analysis while awaiting answers. Ask rig-needed/preserve/new first; detailed rig questions are N/A when no rig is needed. Likewise, no-render delivery makes lighting/image settings N/A. Do not require irrelevant advanced pipeline fields or ask answered questions again.

The assistant also needs the asset identity, working/output folder, source version to preserve, and Blender/receiving-application version where relevant. Reuse this context or ask if missing; these operational details need not be repeated in the core form.

## Short form — copy and fill

```text
If I omit any applicable item below, actively ask me in the language I am using.

[Goals and references]
Use: game / film-VFX / product images / animation / other:
Style and nearest viewing distance:
Reference photos, files or product links:
Known dimensions (include units and whether measured or estimated):
Structures that must be made / may be omitted:
The three most important acceptance criteria:

[Geometry and materials]
Low / medium / high detail:
Target polygon count and counting convention (please propose if uncertain):
Topology: all quads / all triangles / mixed triangles and quads / other (describe):
Is high-to-low baking needed:
UV atlas count and resolution, and whether to use UDIM:
Additional description of each part's material, texture, age/wear, etc.:
Default channels: BaseColor, Normal, Roughness, Metallic; extra channels needed:

[Rig]
Not needed / preserve existing / new (please describe as fully as possible):
Actions I want to operate:
Parts that must move together:
Axes/channels that can move or must be locked:
How to start, disengage, reset or close:

[Delivery]
Lighting, background, camera views and image dimensions:
Required file formats:
Production stages and desired review milestones:

First analyze references and list unknowns and occluded structures.
Summarize the complete requirements and main tradeoffs, then wait for my confirmation before production.
Preserve source files, save a new version, and write an English worklog.
```

## Optional rig block — describe behavior, not just "add Ctrl"

This project's Blender controls use Armature pose bones in Pose Mode. Copy the following block once per control where useful; natural-language descriptions are welcome.

```text
Master control: what it drives; its parent/child hierarchy:
Control name or purpose:
Parts and attached details that must move with it:
User input -> intended motion (e.g. upward motion causes screw rotation):
Allowed/locked translation, rotation and scale; local/world space if known:
Range, distance, angle, pitch, direction or limits (or please propose):
Behavior at start / during transition / after separation:
How to reset, reconnect, close or reverse the action:
Keyframing, playback, loop or export needs:
For cables/soft parts: bending, endpoints and whether stretching is allowed:
Physics/collision simulation needed, or animation controls only:
```

## Optional pipeline details — only if your receiving workflow specifies them

```text
Units / up-forward axes / origin / naming / collection rules:
Base and evaluated face budgets, LODs and any export triangulation requirement:
UV texel density, island padding, seams or reuse restrictions:
UDIM tile IDs and per-tile/channel resolutions:
Normal-map convention, map bit depth, channel packing and color space:
Texture formats and object-category/shared-atlas folder needs:
Export format/version and receiving engine/DCC validation required:
Rig export constraints, bone naming and deformation requirements:
```

If topology is "other", ask what representation and editable/exported topology the user means. Clarify any departure from the current triangle/quad convention in the consolidated asset brief; an explicit user requirement takes precedence. Do not silently forbid the option or treat it as blanket permission for n-gons.

Do not infer these values from the bottle trial. In particular, its 1,375 quads, 2K atlas and screw distances are not general defaults. One UV atlas can have several matching channel images and several materials. Face count and triangle-equivalent are different: 1,000 quads become 2,000 triangles when triangulated. Baking can retain surface shading detail but not replace silhouette, wall thickness or correct refraction geometry.

## Existing project conventions — no need to repeat these in every request

Retain editable named parts, compliant topology and usable UVs; four image-based PBR channels; readable left-to-right shaders with Texture Coordinate → Mapping connected to every image; textures grouped by object category or a documented shared atlas; source preservation and versioned outputs; enabled object/Collection selection and viewport overlays/Extras; appropriate geometry/UV/material/rig checks and reopening; English worklogs recording requirements, actions, evidence, tradeoffs and feedback. The assistant must surface any conflict between these conventions and the new request. They do not replace the asset-specific brief.

## Follow-up feedback form — copy after a review

```text
File/version reviewed; part, material or control concerned:
Please preserve these successful aspects:
Current result/problem (include image, frame or reproduction steps if useful):
Desired result and how I will judge it:
Priority: must fix / improvement / optional:
Scope: record/analyze only / prepare a revision summary for confirmation / execute this already confirmed revision:
```

A sentence such as "the material is good" approves that aspect of the named version, not unrelated topology, rig behavior or future versions. The assistant records the scope of feedback and confirms materially changed requirements before affected production.
