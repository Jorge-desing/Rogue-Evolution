---
name: rogue-command-pack-validation
description: "Validate Rogue Command content manifests, AssetBundles, loader builds, and install packages before delivery."
---

# Rogue Command Pack Validation

Use this skill when validating or packaging a Rogue Command loader/content change. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` for current commands and constraints.

## Validate the source and content

- Parse `content/schema/content.schema.json` and validate each changed `content.json` against it. Run `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` for the package-local contract and registry safeguards when tests are part of the accepted task. Report a skipped symlink check as skipped, not passed.
- Schema and offline harness validation do not prove that a `blueprintPoolEntries` pool ID exists in the native stage template. A missing native pool disables the whole pack during runtime registration, so verify pool IDs with generated stage references or a live BepInEx MCP registration probe before delivery.
- Native stage templates differ in available pools: `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` supports `BP_CheaperVehicles`, `BP_CheaperRobos`, and `BP_CheaperMechs`, while the tested `StageParameters_Pools_Level3` template supports only `BP_CheaperMechs`. Treat any pool ID as invalid until a runtime registration probe or generated stage reference confirms it.
- Build the loader in Release. For Unity assets, use the repository's Unity 2021.3.45f2 authoring project and builder, then reopen the generated AssetBundle in Unity. Verify that bundles embed their own compiled shaders (`VehiclePBR_VertexWear` or `Standard`) for URP execution, since native game assets do not provide built-in shaders. UnityPy can independently list serialized objects offline, but cannot establish runtime compatibility.
- Before assigning card or visual asset names, enumerate the bundle with UnityPy (`env.container.keys()`) and confirm each name exists; validate `productionBuilding` card/visual pairs against the schema and run `tools/validate_native_visual_roots.py` for every unit visual root. Report visual-root checks as local static evidence, not rendering proof.
- In diagnostic trace and telemetry formatting, ensure bounded outputs (e.g. `MaxTraceCharacters = 4096`) place omission/loss counters before unbounded-detail collections (`offeredProducts`, candidates, pools) so tail truncation never hides lost observations.
- Rebuild the install archive with `tools/Package-Loader.ps1`. Inspect its actual entries: confirm the intended plugin, manifests, bundles, and readmes are present, and that game assemblies, BepInEx, Harmony, PDBs, and Unity editor output are absent.

## Protect the repository and runtime

- Keep generated build output under ignored `artifacts/`. Inspect `.gitignore` and `git status`; stage only task-owned source, documentation, and distributable bundle inputs. Do not stage Unity `Library/`, generated editor caches, local game assemblies, or user workspace metadata.
- Do not install the package into the game as part of offline validation. When runtime validation is explicitly part of the task, use Steam and a reversible test profile, then restore temporary deployment changes.
- For a requested commit/push, review the staged names and diff, fetch and compare `origin/main` immediately before pushing, and verify the remote SHA afterward. Do not create a pull request unless requested.

## Report what each check proves

Separate static inspection, local build/validation, and in-game evidence. Schema validity, unit tests, bundle reopening, and ZIP inspection are local evidence; they do not prove content registration or playability in Rogue Command.
