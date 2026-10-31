# Clean-room utility recreation and live agent-readable diagnostics

Use this reference when recreating an RCM utility as clean-room C# in the loader, or when exposing loader state to an external agent through the BepInEx MCP bridge without visual captures.

## Clean-room extraction rules

- Do not reuse RCM code, assets, palette values, or constants. Use only public standards (FNV-1a) and independently authored values.
- No new dependencies: the loader must not take Newtonsoft.Json or any other JSON library. Serialize manually.
- The test harness compiles against a minimal `UnityEngine` stub (no real Unity types). Therefore any formatter or DTO that must be tested must be decoupled from Unity types: primitive DTOs (`string`, `int`, `bool`, `float`, `double`) only, with no `UnityEngine.Color`, `Vector3`, etc.
- Keep the pure logic in a dependency-free class (`AgentDiagnosticsFormatter`, `IdentifierColorPolicy`) and the MonoBehaviour as a thin bridge that calls it.

## Clean-room FNV-1a color policy (pattern)

When a utility needs a deterministic color index from a string identifier:

1. Implement FNV-1a 32-bit (offset basis `0x811C9DC5`, prime `0x01000193`) over the UTF-16 char sequence.
2. Modulo the hash by the palette length (power of two, e.g. 16) to get an index.
3. Use an independently authored 16-entry palette; never copy RCM's palette.
4. Verify against known vectors: `"" → 0x811C9DC5`, `"a" → 0xE40C292C`, `"foobar" → 0xBF9CF968`.
5. Ensure determinism: same input → same index → same color across calls and sessions.

## Live agent-readable diagnostics bridge

Goal: let an external agent (BepInEx MCP bridge) read loader state in real time without visual captures or screenshots.

### Architecture

- Create a MonoBehaviour (`LoaderDiagnosticsComponent`) with JSON-returning methods (`GetLiveSnapshotJson`, `GetCleanRoomUtilitiesJson`, `GetVisualUtilitiesJson`, `GetPackAccentColorsJson`, `GetRangeDisplayStatusJson`, `GetInspectorStatusJson`, `GetDiagnosticsJson`, `GetUtilitiesJson`, `ResolveIdentifierColor`).
- Split the pure formatting into a separate dependency-free class (`AgentDiagnosticsFormatter`) with manual JSON writer and primitive DTOs (`OverlayStateView`, `RangeStateView`). The MonoBehaviour delegates to it.
- Expose read-only accessors on the visual utilities (`RuntimeOverlayUi.IsOpen`, `ToggleShortcutText`; `RangeDisplayRuntime.IsEnabled`, `ToggleKey`, `TrackedEntityCount`) so the formatter can read them without Unity coupling.

### MCP bridge discovery (critical)

- The BepInEx MCP bridge's `find_objects_with_component` internally calls `Resources.FindObjectsOfTypeAll` (which DOES find DontDestroyOnLoad components) but then filters with `gameObject.scene.name != null`. DontDestroyOnLoad objects have a null scene name, so they are silently excluded and the HTTP response is an empty array even though the server log says "Found N objects".
- **Rule**: attach the diagnostics component to a GameObject in a normal scene (not DontDestroyOnLoad). Recreate it on each scene load (`SceneManager.sceneLoaded`). The component can still reference persistent objects (provider, overlay, range display) held on the plugin's DontDestroyOnLoad GameObject.
- Use `using UnityEngine;` in addition to `using UnityEngine.SceneManagement;` when calling `new GameObject(...)`.
- After deploying, verify live readability: `scene:find_objects_with_component?componentName=LoaderDiagnosticsComponent` must return the object; `component/get_details` and `component/call_method` must return real values.

### Extending the bridge with a new utility

When adding a new clean-room or visual utility to the live bridge, follow this three-part pattern so it stays testable and agent-readable:

1. **Pure formatter serializer** in `AgentDiagnosticsFormatter` (dependency-free, primitive DTOs only): add a `SerializeX(...)` method returning JSON, plus a `BuildX()` helper if it feeds `SerializeUtilities`. Verify it compiles in the test harness (no Unity types).
2. **Bridge surface** in `LoaderDiagnosticsComponent`: expose a read-only getter (for `component/get_details`) and/or a `GetXJson(...)` method (for `component/call_method`) that delegates to the formatter. Keep the MonoBehaviour thin — no logic beyond null-safe delegation.
3. **Test** in `RuntimeAndDiagnosticsValidationTests.cs`: assert the serializer output contains the expected keys/values (e.g. `"valid":true`, `"pointCount":33`).

Rules:
- Every new utility must be readable by an agent without a visual capture: either a getter or a JSON method (prefer both).
- Primitive DTOs only (`string`, `int`, `bool`, `float`); `UnityEngine.Color`/`Vector3` never cross the formatter boundary.
- Place omission/loss counters before unbounded-detail collections in bounded reports so tail truncation cannot hide them.
- Rebuild plugin + test project and run the harness after each batch; the patch tool's fuzzy matching has repeatedly broken adjacent declarations.

- No Newtonsoft.Json. Write a minimal serializer that emits `{"key":value}` with proper escaping for strings, numbers, booleans, and null.
- Keep DTOs as primitive-only structs/classes so they compile in both the plugin (Unity) and the test harness (stub).
- Place omission/loss counters near the start of bounded diagnostic reports, before unbounded-detail collections, so tail truncation never suppresses loss diagnostics.

## Deployment verification workflow

When deploying a new DLL for live verification:

1. Build plugin Release (0 warnings, 0 errors) and test project Release.
2. Run the C# harness; expect N/N (or N-1 Windows symlink skip).
3. Back up the current installed DLL to `artifacts/runtime-backups/<date>-<label>/`.
4. Copy the new DLL to `BepInEx/plugins/`; verify types present (e.g. `grep LoaderDiagnosticsComponent`).
5. Launch the game via a background process (not `cmd /c start` which may not persist); use a Python `subprocess.Popen` or equivalent.
6. Wait for MCP bridge to respond (`system/ping` or `system/capabilities`).
7. Verify: `scene:find_objects_with_component` finds the component; `component/get_details` returns real values; `component/call_method` returns JSON.
8. After verification, restore the original DLL if the deployment was temporary; verify restored hash matches backup.
9. Commit and push only after verification passes; verify remote HEAD matches local.

## Evidence boundary

- A successful MCP response alone is not evidence that registration occurred; record the BepInEx log lines showing pack loading and any injected probe.
- Label results as static inspection, local build/validation, or in-game runtime evidence. Only the last establishes observed live behavior.