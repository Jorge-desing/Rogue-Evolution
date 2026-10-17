---
name: rogue-command-unit-authoring
description: "Author or review Rogue Command units, including balance variants, model overlays, card sprites, and localized names."
---

# Rogue Command Unit Authoring

Use this skill when adding or reviewing a unit in this repository. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing a pack. The handoff is the current source of truth for research, commands, and evidence limits.

## Choose the unit construction path

- A `nativeTemplateId` unit copies a registered game balance row and clones the native prefab. Use `balanceOverrides` only for allowlisted numeric fields. It keeps the native controller and gameplay components.
- A native visual overlay can set `visualAssetName` and `visualRootPath` with an AssetBundle. The root path must resolve to one native renderer subtree. The loader replaces those renderers and retains the native gameplay hierarchy; it does not retarget rigs or animations.
- A custom-prefab unit needs a complete bundled balance asset, prefab, and card Sprite with the game-required serialized components and references. The Unity Editor's inability to attach the precompiled game scripts is an authoring constraint; fail closed when a complete prefab cannot be produced.
- AssetBundles hold serialized Unity assets, not new executable `MonoBehaviour` code. Put new code in the compiled loader only when the task requires it.

## Identity, text, and eligibility

- Use a namespaced unit ID and provide an `en-US` display name for custom-prefab units. Pack translations use the exact language IDs in `content/schema/README.md`; runtime resolution tries the active language, then English, then native-template text, and finally a safe ID-derived name.
- Use `cardImageAssetName` for a bundle Sprite when the unit needs its own card image. The current Sandlance sample demonstrates a model overlay, icon, balance overrides, and localization; it still inherits `PCXBaseTank` gameplay.
- A stage's `unitIds` makes a unit blueprint-eligible by changing a game-wide entity-row flag. Check how shared native pools filter blueprints; this is not guaranteed to be isolated to one stage.
- Before relying on reflected game methods, recheck the compatibility identity as a whole: Rogue Command version, `Assembly-CSharp.dll` SHA-256, and MVID. Unity/bundle authoring must use the pinned Editor version documented in the handoff.

## Verify honestly

Run the repository's offline validator and Release build when implementation or acceptance criteria call for them. Build the Unity bundle with the repository scaffolder and reopen it in Unity; offline bundle inspection can confirm object names and types. Label these as local validation. Only a Steam-launched match can confirm registration, pool selection, instantiation, and rendering. Use a reversible profile and restore temporary deployment changes after an authorized runtime check.
