# Native Patrol command: verified route and loader support

## Compatibility identity

Rechecked before relying on managed signatures:

- Rogue Command: `1.0.9` (`version.txt`)
- Unity: `2021.3.45f2` (`Player.log`)
- `Assembly-CSharp.dll` SHA-256: `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`
- MVID: `db0e9462-128d-4296-9900-de18b4bd0c00`

REA classifies the artifact as Unity Mono CIL. The assembly was inspected statically, not loaded or executed by REA.

## Static inspection

REA and pinned ILSpy decompilation established the following native contract:

| Member | Token | Signature / evidence | Observed behavior |
|---|---|---|---|
| `EntityCommand.Patrol` | `0x06000697` | Static `EntityCommand Patrol(Int32)`; managed return type is a value type. REA evidence `ev_c75143540a8ad1ddd47529e13888fed7f1347ea5edfd1182cc81575edc9c3a01`. | CIL creates the `Patrol` command (enum value `20`) and stores the integer argument in `intValue`. |
| `EntityController.ExecuteCommand` | `0x0600087d` | Instance `Void ExecuteCommand(EntityCommand, EntityController.CommandProcessingType)`; body SHA-256 `e0ad82c6088f58694dc7be147eccec6b3a9d309e307e8b4abda693772da766e5`. REA evidence `ev_f71b5082244047d160d9ca99e04edae364e00c444615ad9bc2ba9965d9241c07`. | Its command switch handles `Patrol`; when no route index is active, it inserts the controller's current `Position` at index 0 and selects index 1, then attack-moves to that route point. |
| `EntityController.ExecuteNextCommand` | `0x0600087e` | Instance method; body SHA-256 `db817987865c1911d856bd17c6bdd2958ae1d0fe909cb21805eb9c5710677dbb`. Same REA evidence `ev_f71b5082244047d160d9ca99e04edae364e00c444615ad9bc2ba9965d9241c07`. | While `Patrol` is current, advances or reverses through patrol positions and issues another `Patrol` command. It does not advance the queued command chain in that branch; a queued `Move` is therefore not resumed while the patrol remains active. |
| `EntityController._commandChain` | `0x0400056e` | Private instance `LinkedList<EntityCommand>`; REA field view `ev_51c53d711562ead194b1b0fe54f017f0b4555209d7fedcf4213332c826248aa7` reports `class:453<valuetype:916>`, resolved by pinned ILSpy as `LinkedList<EntityCommand>`. | This queue is separate from `_currentPlayerCommand`. `ExecuteBeforeCurrentCommand` leaves it intact when current command is `DoNothing`, so `CurrentPlayerCommandName == DoNothing` does not prove the queue is empty. Native patrol reads this field reflectively and fails closed if the field/type/value cannot be verified. |
| `EntityController.ClearPatrolPositions` | `0x06000888` | Instance `Void ClearPatrolPositions()`; REA evidence `ev_803fbc9f1d4e054db49533369ae2ff24bb9c0c012e6e649200a1caa662e993b9`. | Clears the route, resets the current index to `-1`, and restores forward direction. |
| `EntityController.AddPatrolPosition` | `0x06000889` | Instance `Void AddPatrolPosition(Vector3)`; REA evidence `ev_803fbc9f1d4e054db49533369ae2ff24bb9c0c012e6e649200a1caa662e993b9`. | Appends a position to the native patrol list. |
| `EntityController.ArePatrolPositionsEmpty` | `0x0600088a` | Instance `Boolean ArePatrolPositionsEmpty()`; REA evidence `ev_803fbc9f1d4e054db49533369ae2ff24bb9c0c012e6e649200a1caa662e993b9`. | Reports whether the native position list has fewer than one entry. |

Artifact identity evidence: REA `ev_b923e23cfec30be565c58056206d348d2714b1d81204fa26364cbbcce0bd6463` (linked source `ev_f048aa6628febf3c3c289096b62db3d7a2705ba2822c9c7fa5bb6e53edd3a8a5`). `EntityCommand` type summary: `ev_f5bbcfab7d68f054c65da86f8456a5320dd87d7735e00c9045a9b038b37b5d03`; `EntityController` type summary: `ev_dc497976fbe7b1ce703dfb12c7d8f7473b5d1a327efc10f2ddc8121def7f1905`.

The decompiled C# used for cross-checking was written outside the repository under Hermes scratch. No decompiled output is part of the mod or its distributable.

## Loader behavior

The existing startup gates protect this private-field binding: `RogueCommandExpansionPlugin.Awake` calls `GameCompatibility.TryValidate` for the game version, Unity version, and assembly SHA-256 before constructing the loader; `RuntimePatchInstaller.Install` then calls `RuntimeBuildGuard.TryValidate`, which also checks the `Assembly-CSharp` MVID, before installing hooks. The native runtime behavior is created only during later AI profile activation. Thus the reflective field contract is used only for the pinned game identity; the harness fixture does not prove reflection against the shipped assembly.

`sample:patrol-and-intercept` accepts optional `patrolMode`:

- `AttackMove` (default): preserves the existing tagged attack-move waypoint behavior for eligible idle or moving defenders.
- `Native`: for a defender whose current command is `DoNothing` and whose verified native command queue is empty, selects up to eight distinct AI-building cells inside `patrolRadiusCells`, ordered nearest-first; clears and seeds the route with their `Position` values; then inserts `EntityCommand.Patrol(0)` with the module tag. The private `_commandChain` binding is pinned to the verified game identity and checked for the exact `LinkedList<EntityCommand>` type; an absent/incompatible field or read failure suppresses native patrol (fail-closed). The native command adds the current position as its initial route endpoint. Existing player-threat interceptions remain attack-move orders and still precede waypoint dispatch under the shared `maxPatrolUnits` cap.

Native mode checks both the current command and the queued command chain: it skips `Move` as the current command and skips any defender with queued commands, including a `Move` behind a current `DoNothing`. This avoids leaving queued orders dormant while native Patrol continually traverses its route. It does not overwrite an active native Patrol command. Route selection and order count are bounded; no profile or pack schema changes are required because module parameters are validated by the registered parser.

## Evidence boundaries

- **Static inspection:** pinned assembly signatures and native route/command-chain semantics above.
- **Local build/validation:** the validation harness uses a value-type `EntityCommand` test fixture with a private `LinkedList<EntityCommand>` command queue. Tests cover both accepted modes, default preservation, invalid/duplicate options, nearest distinct route selection, output/route cap, idle-only patrol assignment, queued-Move preservation behind `DoNothing`, shared dispatch cap, and attack-move interception preservation. The new queue regression failed before the fix and passed afterward. Release plugin and harness builds passed with 0 warnings/errors; the exact Debug and Release test DLLs each passed `189/190`, with one Windows symlink test skipped because the environment lacks the required privilege.
- **In-game runtime evidence:** none for the new mode. Route movement, pathing, target selection after the first point, and interruption/resumption have not been observed in a Steam-launched match. Do not report the harness or build as in-game proof.
