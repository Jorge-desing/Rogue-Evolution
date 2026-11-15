# Bounded agent automation

Read this guide before sequences, waits or recovery. Discover the tools for the current area, inspect their schemas, observe current state, perform the authorized action, verify its postcondition and decide what comes next. Keep native UI, placement, route classification, saves and Harmony identity rules authoritative.

## Choose the smallest operation

| Need | Operation |
| --- | --- |
| One current fact or one native action | Dedicated diagnostics tool |
| Related UI state on one Unity-thread read | `diagnostics_get_runtime_control_surface` |
| Broad best-effort overview | Snapshot; sections may come from separate reads |
| Independent operations with no data dependency | Existing batch tool; inspect each result |
| Ordered operations with typed dependencies and gates | `automation_run_sequence` |
| A condition that becomes true asynchronously | `automation_wait_for_state`; for unique interactable labels use `diagnostics_wait_for_ui_text` |

Snapshots and waits do not lock state or keep instance IDs current. Native actions must still reject stale, ambiguous, inactive or non-interactable targets. Refresh census paths/IDs before an action. Avoid splitting one mutation into blind repeated attempts.

## State waits

`automation_wait_for_state` accepts `tool`, optional `arguments`, and 1–8 `expect` checks at JSON Pointers. Allowed tools: `diagnostics_get_runtime_control_surface`, `diagnostics_get_ui_accessibility_census`, `diagnostics_get_intermission_native_panel_state`, `diagnostics_get_intermission_census`, `diagnostics_get_navigator_state`, `diagnostics_get_game_time_scale`, `diagnostics_get_intermission_reward_state`, `diagnostics_get_battlefield_unit_state`. Discovery still exposes every other tool; this list restricts automatic polling only.

Optional `stableSamples` (1–8, default 1) requires consecutive matching reads; a missing field or mismatch resets the count. Results report `stableSamples` and `consecutiveMatches`. Use 2 or more when a transient UI state must not advance the agent; this observes stability across samples, not between them. The original timeout and sequence budget still apply.

Each wait condition takes one of `equals`, `atMost`, or `atLeast`. Numeric bounds are inclusive and match only JSON numbers. Sequence step expectations continue to use `equals`.

Defaults: 10 seconds and 250 ms between reads. Limits: 0.1–30 seconds, 100–2000 ms. Missing fields or mismatches remain pending. Invalid replies, transport failures and errors in the queried section stop the wait. An error in an unrelated section does not invalidate a healthy field. Results contain `reason`, `attempts`, `elapsedSeconds`, `checks`, and only the `lastObservation`. No mutation is retried.

Example: wait for an already authorized native transition to reach intermission; this does not load a node or advance a route.

```json
{"tool":"automation_wait_for_state","arguments":{"tool":"diagnostics_get_runtime_control_surface","expect":[{"path":"/isIntermissionActive","equals":true}],"timeoutSeconds":10,"pollIntervalMs":250}}
```

## Sequences and typed references

Sequences allow 1–16 diagnostics/wait steps with a shared 1–120 second budget (default 60). Waits use the smaller of their own timeout and the remaining sequence budget. Keep `tool` and expectation `path` literals; references can supply query arguments or expected values. Exact reference shape: `{"$ref":{"step":0,"path":"/field"}}`, zero-based, earlier successful step only. References preserve JSON types. Use `expect` on steps to gate subsequent operations.

The following example requires an authorized UI change and a freshly resolved, unique slider. Replace the illustrative path with the current exact scene-qualified census path. The getter gate prevents indexing a missing or ambiguous control; the native action validates the ID/path again. The example sets an authorized slider value of 0.5, then waits for its observed value. Confirm that value is within the native slider range before adapting the recipe.

```json
{"tool":"automation_run_sequence","arguments":{"steps":[{"tool":"diagnostics_get_ui_accessibility_census","arguments":{"query":"MainMenu::/Canvas/Options/Slider","maxResults":2},"expect":[{"path":"/candidates/0/hierarchyPath","equals":"MainMenu::/Canvas/Options/Slider"},{"path":"/totalCandidateCount","equals":1}]},{"tool":"diagnostics_ui_set_value","arguments":{"hierarchyPath":{"$ref":{"step":0,"path":"/candidates/0/hierarchyPath"}},"gameObjectId":{"$ref":{"step":0,"path":"/candidates/0/gameObjectId"}},"kind":"slider","value":0.5},"expect":[{"path":"/effectObserved","equals":true}]},{"tool":"automation_wait_for_state","arguments":{"tool":"diagnostics_get_ui_accessibility_census","arguments":{"query":"MainMenu::/Canvas/Options/Slider","maxResults":2},"expect":[{"path":"/totalCandidateCount","equals":1},{"path":"/candidates/0/hierarchyPath","equals":"MainMenu::/Canvas/Options/Slider"},{"path":"/candidates/0/value","equals":0.5}]}}]}}
```

The census exposes `value` as a number for sliders. Inspect `controlType`, `minValue`, `maxValue`, visibility and interactability before choosing the recipe target.

## Battlefield orders

### Mixed plans from an external agent

Use `diagnostics_order_battlefield_plan` when one tactical turn assigns different orders to different units. First read `diagnostics_get_battlefield_census` or `diagnostics_get_ai_roster` and use the current `runtimeEntityKeyId` values. Every order has `commandType`, an `entityKeyIds` array, and optional `arguments` containing command-specific fields; give single-unit commands exactly one key. A plan contains 1–16 orders and at most 32 distinct player units. A unit may receive only one order in a plan, which avoids conflicting commands being inserted into the native queue.

Supported order shapes:

| `commandType` | Required `arguments` fields | Optional `arguments` fields |
| --- | --- | --- |
| `Move` | `targetX`, `targetZ` | `replaceCurrent` |
| `Attack` | `targetEntityKeyId` | — |
| `AttackMove` | `targetX`, `targetZ` | — |
| `Stop`, `HoldPosition`, `GroupStop`, `GroupHoldPosition` | — | — |
| `GroupMove`, `GroupAttackMove` | `targetX`, `targetZ` | — |
| `FormationMove` | `formationType`, `targetX`, `targetZ` | `spacing` (default 4 m) |
| `ActiveSkill` | — | `targetEntityKeyId` **or** paired `targetX`/`targetZ`; omit all targets for native self skills |
| `CoordinatedAssault` | `targetEntityKeyId` | `maxAttackers` (1–16, default 16) |
| `Retreat` | `threatX`, `threatZ`, `distance` | — |
| `Patrol` | parallel `waypointX` and `waypointZ` arrays (1–8 points) | — |

The dispatcher requires the active `Game` scene, resolves every actor uniquely, and rejects the plan before dispatch if an actor is missing, ambiguous, destroyed, not a player-controlled unit, or reused by another order. `Attack` and `CoordinatedAssault` also require a live hostile target. Native dispatch follows the plan order in one Unity-thread callback. The response includes an item for each command and separates `accepted`, `complete`, and `partial`; if a native command fails after dispatch begins, later independent orders still run and earlier orders cannot be rolled back. Inspect `dispatchResult` and the plan counts before deciding what to replan.

Entity-key resolution requires a complete read of the live controller registries. If a registry cannot be enumerated, resolution returns no match instead of treating a partial census as proof of uniqueness. Refresh the state and diagnose the registry before issuing another order.

This is a one-shot command surface, not a game-side autopilot. The outside agent owns the observe → decide → dispatch → observe loop. Use fresh battlefield state after each plan; use `automation_run_sequence` when later commands depend on arrival, health, mana, or another changing condition. Do not put two orders for the same unit in one plan to simulate a queue; use the native order sequence and state waits instead.

Example mixed order call (replace every key and coordinate with current live values):

```json
{"tool":"diagnostics_order_battlefield_plan","arguments":{"orders":[{"commandType":"FormationMove","entityKeyIds":["101","102"],"arguments":{"formationType":"Wedge","targetX":20,"targetZ":35}},{"commandType":"CoordinatedAssault","entityKeyIds":["103","104"],"arguments":{"targetEntityKeyId":"201","maxAttackers":2}},{"commandType":"ActiveSkill","entityKeyIds":["105"],"arguments":{"targetX":22,"targetZ":33}}]}}
```

Movement orders accept optional `replaceCurrent: true` to revise a unit's current destination using native `ExecuteAndCancelCurrentCommand`. Pending native orders stay in the chain. Omit the option to retain the interrupted current command through the existing prepend behavior. Use replacement when replanning an agent-owned approach, then observe arrival at the new goal.

Native `Self` skills always dispatch through `ActiveSkillWithoutTarget`, including requests containing target arguments. This runs the native self-activation conditions and reports `Self` with the caster's coordinates.

An active-skill order with `targetEntityKeyId` resolves that entity's current position for native `TargetOrigin.ChosenLocation` skills and dispatches `ActiveSkillAtPosition`. The result reports `Position` mode and the chosen coordinates. Entity-targeted skills retain their native target command. The location is sampled at dispatch, so subsequent target motion can move it outside the effect.

For active skills, the unit observation provides `hasActiveSkill`, `currentMana`, `skillManaCost`, `skillManaAvailable`, and `skillActivationQueued`. Wait for an existing skill with sufficient mana and no queued activation, then issue `diagnostics_order_active_skill` with its native target arguments. Retain the unit and scene expectations in the wait. Native target geometry and command execution determine whether the skill takes effect. Read the resulting mana, queued activation and target state before planning another cast.

Resolve enemy unit and building instances from `diagnostics_get_ai_roster` using each row's `runtimeEntityKeyId`. Pass that key as `targetEntityKeyId` to `diagnostics_order_unit_attack`. The native order checks hostility at dispatch. Use the same key to compare `attackTargetKeyId` in the attacker's subsequent observations.

Find living player units with `diagnostics_get_battlefield_census` and use the row's string `runtimeEntityKeyId`. Refresh keys on scene changes. Confirm the match has started and choose a reachable destination before dispatch. The existing Move, Attack, AttackMove, HoldPosition and Stop tools issue native orders. A successful dispatch records the call. Observe the unit to establish its subsequent state.

To withdraw a player unit when its remaining health reaches 35%, wait on the same unit and scene with the condition `{"path":"/healthRatio","atMost":0.35}`, then dispatch a Move to the chosen withdrawal point. The sample includes `currentHealth` in hit points and `healthRatio` as a fraction. A missing or nonnumeric health ratio cannot trigger the order.

`diagnostics_get_battlefield_unit_state` reads one live entity in `Game`. It reports the scene handle, player ownership, native command, current attack target key and range state, and live X/Z position. An optional `goal` object with `x`, `z`, and `tolerance` (default 1 world unit) adds horizontal distance and `withinGoal`. Use this getter in `automation_wait_for_state` for arrival or wait for both `attackTargetKeyId` and `attackTargetInRange` for engagement. Range and target telemetry establish engagement state. Damage requires a separate observation.

For movement, put `diagnostics_order_unit_move` in a sequence with an expectation of `/success == true`, then wait for `/withinGoal == true` using the same unit key and coordinates. Include `/sceneHandle` with its value from a fresh observation when following up across calls. A lost unit or scene exit stops the wait. On timeout, inspect the last sample and replan from current state.

Replace the illustrative unit key `42` and coordinates `(10,20)` below with the live unit and authorized destination. The initial read supplies the scene handle and confirms player ownership.

```json
{"tool":"automation_run_sequence","arguments":{"steps":[{"tool":"diagnostics_get_battlefield_unit_state","arguments":{"entityKeyId":"42"},"expect":[{"path":"/isControlledByPlayer","equals":true}]},{"tool":"diagnostics_order_unit_move","arguments":{"entityKeyId":{"$ref":{"step":0,"path":"/entityKeyId"}},"destinationX":10,"destinationZ":20,"replaceCurrent":true},"expect":[{"path":"/success","equals":true}]},{"tool":"automation_wait_for_state","arguments":{"tool":"diagnostics_get_battlefield_unit_state","arguments":{"entityKeyId":{"$ref":{"step":0,"path":"/entityKeyId"}},"goal":{"x":10,"z":20}},"expect":[{"path":"/withinGoal","equals":true},{"path":"/sceneHandle","equals":{"$ref":{"step":0,"path":"/sceneHandle"}}}],"stableSamples":2,"timeoutSeconds":20}}]}}
```

## Verify and recover

`stopReason` is null for a successful sequence, otherwise `{code, step}` identifies the first failure (or exhausted budget). A failed wait, an incomplete battlefield plan whose dispatch started, or a partial group/formation/stance/assault action stops subsequent actions even with `stopOnFailure=false`; inspect the affected units before replanning. Direct group calls can report `success=true` for partial dispatch, so compare counts with the requested units. An uncertain native effect also stops unconditionally. `stopOnFailure=false` never authorizes automatic recovery or replay.

Example: after an uncertain native UI call, inspect this read-only result in a new request and decide whether the intended value is already present. Do not replay the stopped sequence.

```json
{"tool":"diagnostics_get_runtime_control_surface","arguments":{"query":"MainMenu::/Canvas/Options/Slider","maxUiResults":2}}
```

For changes observed asynchronously, wait on the exact current value field from the fresh census, then inspect `lastObservation`. Paths, labels, instance IDs and offered items expire across scenes; route/shop mutations require their native dedicated census and validation.

Agent sessions, heartbeats and resource locks are advisory coordination for cooperating agents. They do not make a snapshot atomic with a later action, prevent other callers from mutating the game, or restore an expired session automatically. Release owned locks and temporary observers using their exact returned identifiers when the task finishes.
