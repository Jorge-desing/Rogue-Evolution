---
name: rogue-command-assetbundle-authoring
description: "Create or validate Rogue Command Unity AssetBundles for unit models, visual overlays, and card sprites. Use for Unity asset work, not balance or registry changes."
---

# Rogue Command AssetBundle Authoring

Use this skill when preparing a unit model, native visual overlay, card Sprite, material, texture, or other non-code Unity asset for a Rogue Command content pack. For manifest or balance changes, also use `rogue-command-unit-authoring` and read the pack contract.

## Work in the authoring project

- Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, the AssetBundle section of `README.md`, and `unity/RogueCommandAssetAuthoring/README.md` before building. Read `content/schema/README.md` when editing pack declarations.
- Use `unity/RogueCommandAssetAuthoring/`; it is distinct from the user's `Rogue Evolution` project. Preserve its `ProjectSettings`, package manifests, Plastic/Unity Version Control metadata, Unity Cloud settings, and other authored files. Confirm `ProjectSettings/ProjectVersion.txt` before opening or building; this repository currently targets Unity 2021.3.45f2.
- Reuse the existing Editor builder and visual sample scaffolder. Keep intermediates and inspection output under ignored `artifacts/`; copy only intentional runtime assets into the pack's declared `bundles/` path.

## Choose the asset shape that the loader can use

- A native-template unit can replace one uniquely resolved renderer subtree with a script-free visual prefab while retaining the native controller and gameplay hierarchy. Keep model offsets and scale in the prefab's child hierarchy. This path does not retarget native animation rigs or prove that model-specific weapons and movement visuals match.
- Render pipeline and shader self-containment: Rogue Command executes on Universal Render Pipeline (URP). Shipped native vehicles use untextured geometry rendered via `Shader Graphs/EntityColorShader`. Modded AssetBundles must embed their own compiled shaders (`RogueCommand/VehiclePBR_VertexWear` or `Standard`); never assume built-in shaders are provided by the base game assets. Setting `enableInstancing = true` on materials opts eligible renderers into instancing, but is not a guarantee of batching or reduced draw calls.
- Sockets and transforms: Search recursively for existing sockets before instantiating new GameObjects; never spawn duplicate socket transforms under the visual root when the imported FBX hierarchy already contains them, as multiple transforms matching the same role trigger `UniqueSocketRoleBinding` ambiguity and disable runtime behavior.
- A fully custom unit prefab needs the serialized game components and references required by the game. The Editor cannot author new components from the precompiled `Assembly-CSharp.dll` here; do not invent missing references or treat an incomplete prefab as installable. AssetBundles carry Unity data, not new executable `MonoBehaviour` code.
- Build for the game's Windows target with the pinned project editor. Reopen the produced bundle in Unity and verify each required asset name and type, the bundle manifest, renderer/material references, and absence of missing or `MonoBehaviour` components where a visual-only prefab is expected. Use `tools/inspect_unity_objects.py` or UnityPy as an independent serialized-object inventory when useful.

## Report evidence accurately

An Editor build and successful AssetBundle import are local authoring evidence. They do not prove that Rogue Command resolves the asset, registers the unit, or renders it in a match. For those claims, use `rogue-command-steam-runtime-validation` and record the in-game observation separately. Do not replace a failed or missing asset with a placeholder bundle and call the pack complete.
