---
name: live-diagnostics-bridge
topic: agent-readable runtime diagnostics on the loader
---

# Live agent-readable diagnostics bridge

Build an agent-readable bridge that exposes loader runtime state, telemetry censuses,
gameplay commands, and clean-room utilities over the BepInEx MCP bridge without visual captures,
and the pure policy extraction that makes those utilities unit-testable. Applies whenever the
loader's `LoaderDiagnosticsComponent` (or an equivalent bridge) is extended with
new getters, censuses, command director methods, or MCP tools.

## Bridge component placement

- Attach the diagnostics `MonoBehaviour` to a regular scene GameObject, NOT a
  DontDestroyOnLoad object. The MCP bridge filters out objects whose `scene.name`
  is null, so a persistent object is invisible to `scene:find_objects_with_component`.
  Recreate the component on scene load (`SceneManager.sceneLoaded`) so it survives scene transitions.

## Keep contracts pure and dependency-free

- Extract color / selection / contract / census logic into pure `static` policies (no
  MonoBehaviour, no Unity instantiation) so they are unit-testable in the
  dependency-free harness AND live-readable without starting the display.
- The JSON formatter (`AgentDiagnosticsFormatter`) must write JSON manually with primitive DTOs decoupled from
  Unity types, so it compiles in the test harness (which provides only minimal
  `UnityEngine.*` stubs). No Newtonsoft, no heavy Unity types in the formatter.
- Give each JSON object a short `description`/`kind` field so an agent reading raw
  JSON knows what it represents.
- Place omission / loss counters first in bounded diagnostic reports before unbounded collections.

## Tool suites and aggregators

- **Aggregators**: Expose composite aggregators (`diagnostics_runtime_snapshot` for composite state and
  `diagnostics_runtime_scalars` for rapid polling) to minimize round-trip latency.
- **Censuses**: Expose decoupled censuses covering AI director, camera viewport, harvesters, audio mixer,
  tech catalog, selection, fog, battlefield, player/AI rosters, grid metrics, and run progression.
- **Commands**: Route tactical commands and balance tests through `RuntimeGameplayCommandDirector`
  (unit move/attack/stop/hold, active skills, vitality adjustments, status effects, selection, control groups,
  fog, timescale, spawner, and data-driven card changes).
- **Batching & Profiles**: Use `diagnostics_batch_read` and diagnostic profiles for grouped analysis.

## Expose, then verify live

- Add public property getters for config/state and JSON methods for structured
  reads; the agent reads them via `component/get_member` and `component/call_method`.
- Verify live: build Release, record the deployed DLL hash, back it up, deploy,
  run the MCP verify script, and read the JSON back. A passing build is local
  build/validation, not in-game runtime evidence — label accordingly.

## C# pitfalls

- A public member cannot expose a parameter or return type that is LESS accessible
  than the member (CS0051). Before widening a type's visibility to satisfy one
  accessor, check whether a JSON method already exposes that data — if so, delete
  the dead accessor instead of making the type public.
- A pure policy file that returns `UnityEngine.Color` still needs
  `using UnityEngine;`. The test harness resolves the type from a Color stub, but
  the file must import the namespace or the build fails with CS0246.
- Do not add a public accessor that merely duplicates what a JSON method already
  returns; it is dead code that only widens the accessibility surface.
- Static properties with private setters (e.g. `AiBehaviourManager.CurrentAttackWaveNumber`)
  must be set via reflection passing `null` target and `BindingFlags.Static | BindingFlags.NonPublic`.
- Private instance methods (e.g. `Harvest.ClaimClosestCrystalAndMoveToIt`) must be invoked via cached reflection.
- Game assembly helper methods (e.g. `AudioMixerHelper.Set*Volume`) must have their parameter order verified
  against decompiled signatures (`AudioMixer` first, `float` second).

## Clean-room color authoring

- When authoring per-kind colors for a clean-room recreation, record the authored
  RGBA values and confirm they are distinct from the reference palette. Clean-room
  means independent authoring, not reproducing the original's exact numbers.
