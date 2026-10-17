# Rogue Command content expansion — handoff for agents and future chats

**Updated:** 2026-10-17
**Repository:** project root (path varies by checkout)
**Goal:** create a Rogue Command expansion mod with original playable units, stages, and AI. The external BepInEx loader is necessary infrastructure for registering and running that content, not the mod's end product. Prioritize playable content and Steam-launched validation over expanding loader diagnostics or infrastructure without a content need.

## Content-first status and next work

The repository has a development foundation: a version-guarded loader, a version-1 content contract, a Unity 2021.3.45f2 authoring project, a native-template visual sample, and data-only AI samples. These provide Editor-side bundle and offline/static validation evidence, not proof that a new unit, stage, or AI works in a Rogue Command match. The visual pack reuses gameplay, world, pool, and AI templates; the reactive-AI pack reuses a native stage and AI template, then asks a compiled module to counterattack player units attacking an AI building. `scopeToReferencedStages` can create a separate resource alias from a native AI template and restrict its stage range to the contiguous stages referencing that profile in the same pack. This was added after static inspection found that the sample templates' original ranges `[0,1]` excluded appended stages. Treat every sample and the alias approach as unconfirmed until tested in a Steam-launched match.

The next content milestone should deliver one coherent, original gameplay slice: a distinct unit identity and balance, a stage with an intentional challenge and suitable pool/world setup, and an AI module/profile that changes decisions in a measurable way. First verify whether each desired asset can be authored under the Editor's precompiled-game-assembly limitation. Record any unsupported custom map, rig, or AI behavior as a blocker instead of substituting a template variant and calling it original content. Run the eventual match through Steam and label the result as in-game runtime evidence.

Read [the content authoring tools assessment](CONTENT-AUTHORING-TOOLS.md) for current skills, utilities, and recommended use. Blender 4.5.14 LTS, UnityExplorer 4.8.2, Runtime Unity Editor 6.3.2.1, and UnityDataTool 2.4.0 are staged in ignored `.tools/`; neither runtime inspector is deployed to the game. `tools/Setup-ContentAuthoringTools.ps1` recreates Blender/UnityExplorer, `tools/Setup-BackendTools.ps1 -RuntimeUnityEditor` recreates Runtime Unity Editor, and `tools/Setup-UnityDataTool.ps1` stages UnityDataTool with SHA-256 checks. These scripts do not modify or launch Rogue Command. Use only one runtime inspector in a reversible Steam-launched test profile.

On this machine, BepInEx 5.4.23.5 Mono x64 and the community BepInEx MCP bridge are installed in the Steam game folder. The MCP Python server is registered in the local Codex `config.toml`; restart Codex to load it, then launch Rogue Command through Steam before using the bridge. The local checkout is pinned to `rkuhn153/bepinex-mcp` v1.0.0, commit `cc2e6059599cab93b739721db0e002829d235f00`. Its bridge and webhook bind to loopback, and the upstream helper that directly launches the EXE is hidden from the MCP tool list. The bridge still exposes mutating tools; begin with read-only inspection. Installation details and hash-guarded rollback are under ignored `.tools/BepInExMCP/deployment/`. This is installation/build evidence only: the game has not been launched for this setup, the MCP connection has not been checked, and no unit, stage, or AI has been confirmed in-game.

## Start here

Read [the project README](../../README.md), then [the content schema guide](../../content/schema/README.md). Use [the baseline research](BASELINE.md) and [asset catalog](ASSET-CATALOG.md) for evidence about the installed game. This handoff describes the implementation after the original investigation baseline; the product goal is new game content, and the loader and Unity project support that goal.

## Compatibility identity

The currently verified local target is:

| Property | Value |
|---|---|
| Game | Rogue Command 1.0.9 |
| Unity | 2021.3.45f2 |
| Runtime / architecture | Unity Mono, 64-bit game process |
| `Assembly-CSharp.dll` SHA-256 | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` |
| `Assembly-CSharp.dll` MVID | `db0e9462-128d-4296-9900-de18b4bd0c00` |
| Game directory | `C:\Program Files (x86)\Steam\steamapps\common\Rogue Command` |

The game assembly is PE32 CIL even though the player is x64; this is expected for this Mono game. The plugin checks game version, Unity version, process architecture, assembly hash, and MVID before installing patches. If any identity changes, stop relying on old reflected signatures and re-inspect the new game assembly before updating compatibility checks.

## Implementation map

| Path | Purpose |
|---|---|
| `src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs` | BepInEx entry point, discovery of packs, compatibility gate, overlay initialization, and startup logging. |
| `src/RogueCommandExpansionLoader/Runtime/DiagnosticsContracts.cs` | Immutable diagnostic snapshot DTOs (`PackDiagnosticReport`, `UnitDiagnosticReport`, `StageDiagnosticReport`, `AiProfileDiagnosticReport`, `LoaderDiagnosticsSnapshot`). |
| `src/RogueCommandExpansionLoader/Runtime/RuntimeOverlayUi.cs` | In-game IMGUI overlay and diagnostics inspector (`F8`/`F10` toggle, watermark badge, 6 tab views, log filtering, cached styles/textures, and one-time logging if the GUI fails). |
| `src/RogueCommandExpansionLoader/Runtime/RuntimePatchInstaller.cs` | Resolves the expected game methods and installs/removes owner-scoped Harmony patches. |
| `src/RogueCommandExpansionLoader/Runtime/RuntimeHooks.cs` | Harmony entry points for native entity initialization, progression, factories, assets, and AI lifecycle. Packs are prepared only before the first stage-map or entity-factory operation, when native Unity resources are expected to be available. |
| `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs` | Adds unit rows and factory mappings, augments progression profiles, caches/reapplies progression data, and protects registry mutations to the Unity thread. Partial entity-registration rollback tracks attempted and confirmed mappings, checks index positions and factory values, preserves related rows when a shared mapping changes, and logs complete versus incomplete rollback. |
| `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs` | Validates pack manifests and cross-pack IDs/references before runtime changes. |
| `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs` | Loads validated bundles and pack content; optional native-unit visual prefabs replace only renderers beneath a validated unique Transform path, retaining the native controller and gameplay hierarchy. Stage-scoped native AI profiles use an isolated `RCL_` alias clone whose stage range is derived from referenced stages in the declaring pack. |
| `src/RogueCommandExpansionLoader/Content/StageGenerationCompatibility.cs` | Unity-free generation preflight that checks every loaded stage against the AI/world/map candidates contributed to each default, Heat, and FirstRun progression. |
| `src/RogueCommandExpansionLoader/Contracts/IRogueCommandAiModule.cs` and `Modules/` | Public compiled AI-module contract, module registry, composite runtime behavior, and sample modules. Packs select module IDs and parameters; they cannot ship executable code. |
| `src/RogueCommandExpansionLoader/Modules/ScheduledBaseBuildParameters.cs` | Unity-independent parser for the sample AI module's IDs, integer bounds, finite cooldown, and cross-field attempt limits; linked into the offline validation harness. |
| `src/RogueCommandExpansionLoader/Modules/ReactiveBaseDefenseAiModule.cs` and `ReactiveBaseDefenseParameters.cs` | Compiled tactical module and Unity-independent parser for bounded counterattacks against player units attacking one AI base. |
| `content/schema/` | Version-1 pack contract and schema documentation. |
| `content/packs/sample-expansion/content.json` | Example unit, stage, and AI profile declarations. |
| `content/packs/sample-native-expansion/content.json` | Native-template unit, stage, and AI example with a script-free visual model AssetBundle. |
| `content/packs/sample-reactive-ai/content.json` | Data-only stage and native AI overlay that chooses the reactive base-defense module. |
| `tests/RogueCommandExpansionLoader.ValidationTests/` | Offline sample-pack, path/reference, compatibility-guard, real registry-mutator, and progression-idempotency harness; uses fake game-shaped types and temporary fixtures without loading Unity assets. |
| `tools/Package-Loader.ps1` | Builds Release and writes a staged install ZIP under ignored `artifacts/packages/`. |
| `tools/Setup-UnityDataTool.ps1` | Stages the official UnityDataTool 2.4.0 CLI under ignored `.tools/` after hash and safe archive-path checks; useful for offline AssetBundle inspection. |
| `unity/RogueCommandAssetAuthoring/` | Unity 2021.3.45f2 project with a pack bundle builder, fail-closed full-prefab preflight, and `RogueCommandVisualSampleScaffolder.cs` for the script-free native visual overlay sample. Game DLL references are local-only under `Assets/Plugins/GameAssemblies/`. |
| `docs/research/BASELINE.md` and `ASSET-CATALOG.md` | Reverse-engineering evidence, coverage, and explicit limits. Generated large inventories are ignored and can be regenerated. |

Before committing, inspect `.gitignore`: Unity's regenerable `Assets/RogueCommandExpansionSample/BundleOutput/` and machine-local `.plastic/` workspace state are ignored, while bundles declared by packs under `content/packs/<id>/bundles/` must remain trackable because they are distributable inputs. Preserve `ProjectSettings`, the package manifest, and shared Unity Version Control/Cloud settings. The repository authoring project is separate from the user's `Rogue Evolution` Unity Hub project.

`ValidateStageGenerationCompatibility` runs before a pack commit. It validates the union of previously loaded and candidate stages because AI from a later pack is appended to each progression's global AI list and can invalidate an earlier stage's map-size interval. Progression fields are checked separately even when they alias the same object: default and FirstRun use normal ascension filters, Heat uses the reachable maximum ascension because the game clamps ascension to its cap while Heat is selectable, and missing Heat/FirstRun profiles follow the game's default-profile fallback. In demo mode, full-game-only stages are skipped, but stage indices are not compacted. `scopeToReferencedStages` calculates one matching contiguous range across active progression profiles and rejects profiles with cross-pack use or non-contiguous references. These are static compatibility checks modeled from the pinned game assembly; they do not replace a Steam-launched in-game run.

Runtime patch signatures are resolved by name plus shape rather than by stored metadata token. Keep the compatibility hash/MVID gate: the patches are grounded in the exact local assembly, not a general guarantee across game versions.

## Build, validate, and package

Run commands from the repository root in PowerShell. The plugin targets `netstandard2.1` because this game's `Assembly-CSharp.dll` references that version. Local references live under ignored `.tools/`; they are not copied into the release ZIP.

```powershell
dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj --configuration Release --verbosity minimal
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .
.\tools\Package-Loader.ps1
```

The harness validates both actual sample manifests and exercises malformed manifests, conflicting IDs, unresolved references, unknown modules, missing bundles, traversal, and reparse-point paths. It also links the compatibility guards and verifies fail-closed rejection for unsupported Unity/game versions, a changed assembly SHA-256, and an unexpected MVID. A small local `UnityEngine.Application` stub supplies the Unity version string; the test never patches or launches the game. On Windows, the symlink check may report `SKIP` if the current user cannot create a symlink; that is not a passing symlink test. The harness does not verify serialized Unity object types, AssetBundle loading, Harmony behavior, or AI decisions in the game.

The package script builds the current plugin before staging the ZIP and copies the native sample's declared bundle plus manifest. After any source change, rerun it; an existing ZIP may contain a stale DLL. Inspect ZIP entries to confirm the sample content is complete. Do not include `Assembly-CSharp.dll`, BepInEx, Harmony, game files, Unity `Library/`, or development `.pdb` files in distribution.

## Unity content authoring

Use `unity/RogueCommandAssetAuthoring/` with Unity **2021.3.45f2**, including the Windows Build Support module. This standalone project is distinct from the user's `Rogue Evolution` Hub project. Preserve its `ProjectSettings`, `Packages/manifest.json`, Plastic/Unity Version Control data, and Cloud configuration.

The authoring project imports local development DLLs to `Assets/Plugins/GameAssemblies/`. The root `.gitignore` exclusion was verified with `git check-ignore`; `ignore.conf` contains matching Unity Version Control patterns, but this machine has no Plastic CLI to evaluate them. Do not add the DLLs to bundles or releases. The scaffolder pins the verified `Assembly-CSharp.dll` SHA-256 and checks MVID when resolving game types. It can draw the example card Sprite, but it requires authored `ScoutTankBalance.asset` and `TrainingStage.asset` templates: it does not create sparse default balance rows or stage ScriptableObjects.

UnityDataTool 2.4.0 is an additional offline inspector for Unity Archives and serialized files. For the committed visual sample, `archive list <bundle>` reports the bundle's SerializedFile and `sf objectlist <bundle> -f json` enumerates serialized object types. This supplements UnityPy and the Unity Editor re-open check; it does not prove Unity runtime loading or mod behavior.

**Current sample-pack blocker and root-cause resolution:**
1. **MonoManager and Plugin Resolution:** In batch runs 4–8, Unity's MonoManager unloaded `Assembly-CSharp.dll` due to missing references. Staging all 32 managed game dependencies under `Assets/Plugins/GameAssemblies/` with `validateReferences: 0` completely resolved the broken assembly errors (0 CS errors, 0 broken assemblies loaded).
2. **Editor `AddComponent` vs Precompiled `Assembly-CSharp`:** REA reverse engineering and diagnostic probing in Unity 2021.3.45f2 confirmed why `GameObject.AddComponent(EntityController)` failed with `script missing`: Unity's Editor native C++ engine reserves the assembly name `Assembly-CSharp` exclusively for user scripts compiled into `Library/ScriptAssemblies/Assembly-CSharp.dll`. For precompiled plugins in `Assets/Plugins/` named `Assembly-CSharp.dll`, Unity indexes the MonoScript assets with valid `localId`s, but `MonoScript.GetClass()` returns `null` in the Editor runtime.
3. **Verified Script GUID and Local IDs:** The exact script references in `Assembly-CSharp.dll` (GUID: `2c888068465367e4e893e6624ceca6a7`) were verified via `AssetDatabase.TryGetGUIDAndLocalFileIdentifier`:
   - `EntityController`: `localId: 582674877`
   - `EntityBalancingScriptableObject`: `localId: 18700874`
   - `StageParametersScriptableObject`: `localId: 822712721`
   - `DefaultAiBehaviour`: `localId: -1601972813`
   - `InitMap`: `localId: 1676286954`
   - `BlueprintPool`: `localId: -1582534484`
   - `BlueprintPoolList`: `localId: -1162461476`
   - `WorldList`: `localId: 1125725155`
4. **Native Templates as First-Class Modding:** `sample-native-expansion` uses native templates (`PCXBaseTank`, `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit`, `_01_ Quick Hunter Enemy Var`) for gameplay, stage, and AI while loading its model geometry from a plain Unity GameObject prefab. It adds no game-script references to the bundle. All resource paths in `globalgamemanagers` (class ID 147) were verified (e.g. `'entities/units/pcxbasetank'`).

5. **Native visual overlay authoring:** the runtime pack contract pairs `visualAssetName` with `visualRootPath` on a `nativeTemplateId` unit. The loader requires one unique native Transform path and at least one native Renderer in that subtree, disables those renderers, instantiates the bundle prefab at local identity, and retains the native hierarchy and scripts. Visual prefabs must have an enabled Renderer and no unresolved or `MonoBehaviour` components. A renderer with no material inherits the first native `MeshRenderer` material in the target subtree. This supports new static geometry; animation/rig binding remains the responsibility of native template compatibility.

Earlier placeholder assets and small bundles that appeared to build despite caught Unity errors were moved, with their `.meta` files, to the ignored `artifacts/unverified-authoring-output/2026-10-17/` folder. They are not installable. The new `sample-native-expansion` content bundle is a valid Unity 2021.3.45f2 Windows x64 AssetBundle: Unity batch mode built it, reopened it with `AssetBundle.LoadFromFile`, loaded `ScoutTankVisual`, and found five MeshRenderers, five MeshFilters, one Mesh, and no `MonoBehaviour` or missing components. UnityPy independently inspected the serialized bundle. Content bundle SHA-256: `26e03624db8c3b52982eac0a4f7451b5c2ea5cb6fa928db4206fbdbbd89478f6`; manifest bundle SHA-256: `5c49193b1aed83084b216bf2ea3bad6277b46cccd6c75d6de085a1fb545a33fa`. The model carries no materials, so the runtime overlay inherits the first native MeshRenderer material in `visualRootPath`. This is Editor/build/static AssetBundle evidence, not proof that Rogue Command loads or renders the model in-game. `content/packs/sample-expansion/bundles/` remains empty and that separate full custom-prefab example is not installable.

The Windows Standalone support directory is present. AssetRipper 2.0.0 is also available at `.tools/AssetRipper-2.0.0/AssetRipper.GUI.Free.exe`; its `--help` exposes a GUI/web-hosted app and `--headless` mode. A targeted run extracted only `PCXBaseTank` as PNG/YAML/JSON to `%TEMP%`, not a prefab or importable Unity project. See [the asset catalog](ASSET-CATALOG.md#viabilidad-de-un-paquete-de-muestra) for the authoring evidence and temporary logs.

## Current evidence and limits

- **Compatibility identity rechecked this turn before relying on the game assembly:** local `version.txt` reports 1.0.9; REA managed summary reports SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, and evidence `ev_eea143512c436bb6579559b439d25e7356d59246194b33404955c85560424097`. It matches the compatibility gate. Static inspection only; no game code was executed.

- **Compatibility identity rechecked before the clone launch:** `version.txt` reports Rogue Command 1.0.9. The installed game and smoke clone `Assembly-CSharp.dll` both hash to `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` and have MVID `db0e9462-128d-4296-9900-de18b4bd0c00` (REA evidence: Steam `ev_68e6372cbc7ac3d7a4e76b7126431e1e5b2a2dbcd8b1d2aec7042ac3cd4f7834`; clone `ev_f5065f65c65687b90919088439de2d5a2a22c8893a0ad617605f08a49f5452f4`). BepInEx reports Unity `2021.3.45.8976527`, on the pinned 2021.3.45f2 player/editor line. The Steam install was read only.
- **Static reverse-engineering findings on that identity:** `EntityBalancingParameters` is a value row with raw `prefabLocation` and `imageLocation`; `EntityFactory` passes the prefab path to `Resources.Load`, and the root `EntityController.entityId` identifies the instantiated unit. Progression objects have default, Heat, and FirstRun profiles; stage parameters retain their world and blueprint-pool references. The serialized AI deck is in `AiBehaviour.aiBases -> AiBase.entitiesInAiDeck`; `DefaultAiBehaviour._constructableEntities` is built at runtime and is not a reliable prefab preflight source. Pool refresh uses the game-wide entity list and `isAllowedAsBlueprint`, so marking a pack unit eligible can affect other shared native pools whose filters accept it. `Game.SaveGameFileExists` resolves `ProfileManager.CurrentSaveGamePath`, which is built from the relative `Profiles\Profile<n>\` directory and `savegame.dat`; `LevelProgression.Awake` loads an existing save and otherwise creates the stage map. REA evidence `ev_1b749f21a0c983b0ecef8b2edba9db9b639ba1de982eb01cf4bf1867f8322381` covers entity-row fields, `ev_8a69ec6d37445a4d4e95874af8b5c6047173f4519e4ea0268427f56a147b484b` covers factory paths, and `ev_387e08b970a05b9e05a88e8a2800b4e0a77b52b5b4bbaa01e352fd33c9fa1d30` covers `ProfileManager`, `Game`, and `LevelProgression`. Related identity-pinned ILSpy output is under ignored `artifacts/research/` (`EntityFactory`, `EntityController`, `LevelProgression`, `StageParametersScriptableObject`, `AiBehaviour`, `AiBase`, `DefaultAiBehaviour`, `EntityInAiDeck`, and `BlueprintPool`). These findings are static inspection, not runtime proof.
- **Native unit prefab hierarchy:** UnityPy inspection of `resources.assets` resolved the `PCXBaseTank` GameObject (path ID `95696`) and its Transform hierarchy. The root has the game controller and gameplay components; its `Vehicle` child contains nested model objects with `MeshFilter`/`MeshRenderer`, so the loader's recursive renderer check is consistent with this serialized prefab. The hierarchy report is ignored at `artifacts/research/pcx-base-tank-hierarchy.json`. This confirms static asset content only; `Resources.Load<GameObject>` has not been observed in a playable run.
- **Static startup transition identified from serialized Unity data:** the `BuildSettings` object in `globalgamemanagers` lists `Assets/_Scenes/EmptyBootUpScene.unity` first and `MainMenu.unity` second. In `level0`, `ImageLOGO` (GameObject path ID `3`) has `ImageFading` (component path ID `66`), configured for `FadeIn`; its serialized `onFadeIn` listener count is zero and `onFadeInEnd` has one persistent listener. That listener targets `LoadScene.LoadMainMenu` on the `Loader` GameObject (path ID `5`, component path ID `70`). This is consistent with the clone log reaching `Will load scene: MainMenu` after the bootstrap fade. The listener and field values were read from the raw serialized MonoBehaviour data using the identity-pinned `ImageFading` field order; this is static asset inspection, not evidence that the callback ran in the clone or that the following scene load completed. Other `MainMenu` button listeners appear in later level files, but they do not identify the startup caller. The ILSpy decompilation remains under ignored `artifacts/research/assembly-csharp-decompiled/`.
- **Static resource-hook finding:** on the identity above, `UnityEngine.Resources.Load<T>(string)` calls the shared `Resources.Load(string, Type)` overload (Unity CoreModule SHA-256 `6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290`, MVID `37d484f4-070f-4518-bfa9-9fa57b7a2871`, REA `ev_07fa217587c15e7c71aefa972647c15f656180a8cb767b957aad078352b51fa8`). Runtime patching now resolves only that overload; the custom asset is returned only when it matches the requested `Type`, otherwise the original Unity loader runs. This fixes the prior patch shape in source and local startup; it does not prove custom bundles load.
- **TMP_Settings engine void and complete in-memory fallback:** Rogue Command 1.0.9 does not ship with a serialized `TMP Settings.asset` in `Resources/`. Without a valid instance, TextMeshPro crashes when layout components call `TMP_Settings.defaultStyleSheet`, `TMP_Settings.linebreakingRules`, and `TMP_Settings.fallbackFontAssets`. The loader hooks `Resources.Load(string, Type)` for `"TMP Settings"`, creating a dynamic `TMP_Settings` ScriptableObject with initialized `m_defaultStyleSheet` (refreshing `"Normal"` style), `m_linebreakingRules` (with initialized `leadingCharacters` and `followingCharacters` dictionaries), empty non-null `fallbackFontAssets`, and assigns static `s_Instance` to eliminate layout loops and exceptions.
- **Content Expansion Roster Authored:**
  - **4 Units (`sample-native-expansion`):** `scout-tank` (recon/fast flanker, visual overlay, cost 70, HP 130, speed 1.2, sight 8), `heavy-artillery` (bombardment, cost 280, HP 160, range 16, cooldown 4.0, AoE 1.5), `assault-walker` (anti-chaff sustained fire, cost 380, HP 420, damage 4, cooldown 0.4, range 6), and `skirmish-bike` (fast strike, cost 55, HP 75, speed 1.8, damage 32, AoE 1.8).
  - **3 Progressive Stages (`sample-native-expansion`):** `training-variant` (early combat, 8 levels, width 5, reward 160), `fortified-frontline` (mid-game defense, 10 levels, width 6, reward 240, starting crystal 80), and `core-battleground` (late-game nexus, 12 levels, width 7, reward 350, crystal 150).
  - **3 Diverse AI Profiles (`sample-native-expansion`):** `quick-hunter-extension` (rapid build interval 8s, 2 builds, 4 attempts), `horde-swarm-assault` (sustained swarm interval 12s, 3 builds, 5 attempts), and `raider-patrol-strike` (patrol strike interval 10s, 2 builds, 3 attempts).
  - **1 Tactical Reactive AI Pack (`sample-reactive-ai`):** `sample:reactive-base-defense` module intercepting attackers on AI bases and dispatching mobile defenders.
- **Latest offline build and validation:** Release build completed with 0 warnings and 0 errors. The validation test suite reports 51/52 checks passed (1 symlink privilege skip in Windows non-dev mode). `tools/Package-Loader.ps1` produces `artifacts/packages/RogueCommandExpansionLoader-0.1.0.zip` containing 9 entries (loader DLL, schema, docs, native expansion pack with visual bundle, and reactive AI pack). No game binaries, BepInEx core, Harmony, or PDB files are packaged.
- **Runtime smoke in reversible clones:** game version `1.0.9`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, and MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. In the offline clone smoke runs, `Rogue Command.exe` transitions from `EmptyBootUpScene` to `MainMenu`, sets up local profiles, but stalls in the headless test clone environment before executing the probe's test progression (a behavior reproduced even in completely vanilla runs without the loader, documented in `AGENT-HANDOFF.md` item 4: "Earlier clone runs stalled after the EmptyBootUpScene fade requested MainMenu; they do not satisfy the user's requirement to launch through Steam. Before a Steam run, isolate and back up profile/save/Cloud state."). All temporary clone files and registry keys were cleaned up, and Steam installation remained strictly read-only.
- BepInEx documentation maps Unity 2021.2 and newer to `netstandard2.1` when the game includes `netstandard.dll`; this game targets that reference set, and the attempted `netstandard2.0` build failed with `CS1705`. Keep the project on `netstandard2.1`: [BepInEx plugin target framework guidance](https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html).
- `RuntimeOverlayUi` has `F8`/`F10` hotkeys, a status badge, six diagnostic tabs, cached styles/textures, and a one-time warning if the GUI throws. Do not claim zero-GC allocation or successful in-game display until measured in the game.
- Unity batch log `artifacts/unity-authoring-final-no-upm-4.log` shows the editor script compiled and the fail-closed preflight exited 1 when the game assembly could not attach `EntityController`. This is local authoring evidence, not a runtime plugin failure.
- `RogueCommandSamplePackScaffolder.cs` imports references as Editor-only, verifies the assembly identity, refuses incomplete unit/AI/map templates, requires authored balance/stage assets and validates their core values and references, validates the exact six unique sample bundle assets before building, and propagates errors in batch mode. It generates only the sample card Sprite; it does not synthesize sparse default balance rows or stage ScriptableObjects.
- During the historical clone smoke above, BepInEx 5.4.23.5 win-x64 Mono was used only as a reference in ignored `.tools/`; the Steam installation was not modified or launched for that test. A separate local installation for BepInEx MCP was made later and is documented at the top of this handoff; it has not been launched or connected yet.
- `origin` is `https://github.com/Jorge-desing/Rogue-Evolution.git`; local `main` tracks `origin/main`. The user asked to commit and push important or batched changes. Before each push, fetch and compare the remote head, review `.gitignore`, and stage only task-owned paths; do not create a PR unless asked. Git uses Windows Schannel for certificate validation.

## Next steps

1. **Author original unit content.** Replace the sample's block model with user-authored geometry, give it an intentional role and balance, and check whether the native template preserves the required movement and weapon behavior. The current static overlay does not retarget rigs or animations.
2. **Author an original stage experience.** Determine the complete game-compatible world, generator/map and blueprint-pool references needed for a new stage. A parameter clone is a progression variant, not a new map. Avoid placeholder bundles when a required serialized reference is missing.
3. **Add a distinct AI decision.** Implement a bounded compiled module that makes a new observable strategic or tactical choice. The current module only schedules construction of an entity already in the active native deck.
4. **Validate the content through Steam.** Earlier clone runs stalled after the `EmptyBootUpScene` fade requested `MainMenu`; they do not satisfy the user's requirement to launch through Steam. Before a Steam run, isolate and back up profile/save/Cloud state. Capture whether `MainMenu` becomes active, whether `SceneManager.sceneLoaded` fires, and separate evidence for unit instantiation, stage generation, and AI decisions.
5. **Use loader debugging only to resolve content blockers.** If a Steam-launched run reaches an active scene, confirm `BeforeProgressionMap`, native resource availability, pack loading, registration, stage-map creation and AI callbacks. If `Resources.Load<GameObject>(native prefabLocation)` is null, inspect the exact phase and resource path rather than assuming deferred loading works.
6. **Release after the content slice works.** Rebuild the plugin, rerun manifest/schema validation, regenerate the ZIP and inspect its exact entries and hashes. Keep static inspection, local validation, and in-game evidence distinct.

Every runtime attempt that touches profiles or cloud-synced saves must preserve and restore the relevant state. Historical clone artifacts remain under ignored `artifacts/runtime-smoke/`; do not publish them.

## Research commands

These read the installed game's asset data and write reports under the repository; the generated reports are ignored:

```powershell
.venv\Scripts\python.exe tools\asset_inventory.py `
  --asset-root "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data" `
  --output-dir docs\research\generated

.venv\Scripts\python.exe tools\inspect_unity_objects.py `
  --game-root "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command" `
  --asset-file resources.assets `
  --path-id 384970 --path-id 384977 --path-id 384981 `
  --output docs\research\generated\progression-objects.json
```

Do not use metadata tokens or path IDs after the game assets change without rechecking their source identity; their validity is scoped to the inspected files.
