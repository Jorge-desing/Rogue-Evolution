---
name: rogue-command-pack-validation
description: "Validate Rogue Command content manifests, AssetBundles, loader builds, and install packages before delivery."
---

# Rogue Command Pack Validation

Use this skill when validating or packaging a Rogue Command loader/content change. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` for current commands and constraints.

## Validate the source and content

- Parse `content/schema/content.schema.json` and validate each changed `content.json` against it. Run `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` for the package-local contract and registry safeguards when tests are part of the accepted task. Report a skipped symlink check as skipped, not passed.
- Build the loader in Release. For Unity assets, use the repository's Unity 2021.3.45f2 authoring project and builder, then reopen the generated AssetBundle in Unity. UnityPy can independently list serialized objects offline, but cannot establish runtime compatibility.
- Rebuild the install archive with `tools/Package-Loader.ps1`. Inspect its actual entries: confirm the intended plugin, manifests, bundles, and readmes are present, and that game assemblies, BepInEx, Harmony, PDBs, and Unity editor output are absent.

## Protect the repository and runtime

- Keep generated build output under ignored `artifacts/`. Inspect `.gitignore` and `git status`; stage only task-owned source, documentation, and distributable bundle inputs. Do not stage Unity `Library/`, generated editor caches, local game assemblies, or user workspace metadata.
- Do not install the package into the game as part of offline validation. When runtime validation is explicitly part of the task, use Steam and a reversible test profile, then restore temporary deployment changes.
- For a requested commit/push, review the staged names and diff, fetch and compare `origin/main` immediately before pushing, and verify the remote SHA afterward. Do not create a pull request unless requested.

## Report what each check proves

Separate static inspection, local build/validation, and in-game evidence. Schema validity, unit tests, bundle reopening, and ZIP inspection are local evidence; they do not prove content registration or playability in Rogue Command.
