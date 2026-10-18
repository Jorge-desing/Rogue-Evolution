# Instructions for repository agents

Before changing this repository, read [the agent handoff](docs/research/AGENT-HANDOFF.md), the root [README](README.md), and the content contract in `content/schema/README.md` when the change touches packs.

- Treat the game's version, `Assembly-CSharp.dll` SHA-256, and MVID as a single compatibility identity. Recheck all three before relying on reflected game signatures or metadata tokens.
- Every added unit must declare its own namespaced `productionBuilding`. Register the new building and unit additively, link that building only to its unit, and offer the building's blueprint through the stage's random reward pools. Preserve native factories and products. AI `unitEntries` refers to the produced unit ID.
- Label findings as static inspection, local build/validation, or in-game runtime evidence. A successful build or AssetBundle import does not prove that the plugin works in a run.
- Preserve the Unity authoring project at `unity/RogueCommandAssetAuthoring/`, including its `ProjectSettings`, `Packages/manifest.json`, Plastic/Unity Version Control metadata, and Unity Cloud configuration. It is separate from the user's `Rogue Evolution` project.
- Keep BepInEx, Harmony, and game assemblies out of the distributable ZIP; only the local development build may reference them.
- Keep offline checks read-only against the game installation. When a requested implementation plan explicitly calls for runtime validation, use a reversible test setup and restore any temporary installation changes afterward. Keep generated build outputs under the ignored `artifacts/` directory.
- `origin` points to `https://github.com/Jorge-desing/Rogue-Evolution.git`; verify the remote branch before each push. The user asked for important or batched changes to be committed and pushed. Review `.gitignore`, stage only task-owned paths, and inspect the staged file list before committing; never use a bulk `git add -A` or assume a GitHub PR is requested.
- In 3D model authoring and Unity prefab generation:
  - Authoring sockets must never repeat vehicle name prefixes; use canonical unprefixed names (`Socket_*`, e.g. `Socket_DamageSmoke`, `Socket_MuzzlePrimary`, `Socket_RadarMast`, `Socket_Antenna`, `Socket_Hardpoint_*`).
  - Unity PBR texture atlas imports for `_MetallicGloss.png` must set `sRGBTexture = false` (linear non-color data) and `alphaSource = TextureImporterAlphaSource.FromInput`. Standard shaders must enable keyword `_METALLICGLOSSMAP` and retain float multipliers `_Metallic = 1.0f`, `_Glossiness = 1.0f`.
  - Continuous rotation in runtime directors must compute true angular velocity ($\omega = \text{RPM} \times 6.0^\circ/\text{s}$). Turret targeting must explicitly check candidate hostility via `EntityController.IsEnemy` before aiming.
  - Mesh hierarchy searches in prefab builders must prioritize `_LODs.fbx` over `_Atlas.fbx` to preserve `LODGroup` generation, and locate modular attachments or damaged variants across asset paths using `AssetDatabase.FindAssets`.

The handoff contains the current layout, verified commands, evidence boundaries, and next implementation steps.

