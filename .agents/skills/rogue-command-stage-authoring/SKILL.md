---
name: rogue-command-stage-authoring
description: "Design or review Rogue Command stage packs, progression parameters, blueprint pools, worlds, and map references."
---

# Rogue Command Stage Authoring

Use this skill for stage declarations, stage progression, world/map assets, and unit-pool eligibility. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing a pack.

## Decide whether the work is a variant or a new stage experience

- `nativeTemplateId` clones a native `StageParametersScriptableObject` and applies bounded numeric overrides. It reuses the template's world and blueprint-pool references; describe it as a progression variant, not a new map.
- A custom stage requires a complete bundled stage asset with valid world, generator/map, and blueprint-pool references. Do not substitute a placeholder bundle when any serialized reference is missing.
- A stage can list `unitIds` and `aiProfileIds`; validate those references before any registry mutation. Blueprint eligibility is a global entity-row flag, and shared pools may make a unit selectable outside the stage that declared it.
- `blueprintPoolEntries` scopes mod products to explicitly named native pools. A listed pool receives only its listed products; unlisted pools retain the broad `stage.unitIds` set. Product IDs in an entry need not be repeated in `stage.unitIds`, but `stage.unitIds` still governs broader stage eligibility. Native per-pool filters (`blueprintFilter.IsBlueprintValid`) are preserved. Verify pool IDs against generated research or native objects before editing.

## Check game-specific generation constraints

- Use `StageGenerationCompatibility` and the offline validation harness. Its preflight follows the pinned game's AI map restrictions, selected World restrictions, progression map lists, active/demo-allowed Worlds, and the distinct default, Heat, and FirstRun profiles.
- Stage-scoped native AI aliases must point to contiguous same-pack stage indices. Do not hardcode indices based only on a sample's current position; earlier packs or native progression rows affect them.
- Keep bundle names, dependency declarations, and paths consistent with the schema and Unity builder. Keep generated bundles under the pack's declared `bundles/` directory.

## Evidence boundary

Static preflight and a valid Unity AssetBundle do not prove that a map is generated or playable. Label static inspection and local validation separately from a Steam-launched match. Do not change user saves or Cloud state for a match check; use an isolated, reversible profile and restore temporary installation changes.
