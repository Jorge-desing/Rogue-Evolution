---
name: rogue-command-reverse-engineering
description: "Reverse engineer Rogue Command's Mono backend (Assembly-CSharp.dll) using REA managed inspection, ILSpy, and Unity asset tools. Use when analyzing game internals, finding hooks, decompiling methods, or understanding data formats."
version: 1.1.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, reverse engineering, Mono, BepInEx, Unity, ILSpy, REA]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-harmony-policy, rogue-command-game-research]
---

# Rogue Command Reverse Engineering

Operational guide for reverse engineering Rogue Command's Unity Mono backend (`Assembly-CSharp.dll`) using REA managed tools, ILSpy, and asset inspectors.

## Compatibility Identity (Verify Before Session)

| Property | Pinned Value |
|---|---|
| **Game Version** | Rogue Command `1.0.9` |
| **Engine** | Unity `2021.3.45f2` (Mono 64-bit, IL-only PE32) |
| **Assembly-CSharp SHA-256** | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` |
| **Assembly-CSharp MVID** | `db0e9462-128d-4296-9900-de18b4bd0c00` |
| **Target Path** | `C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data\Managed\Assembly-CSharp.dll` |

---

## Tool Selection & Inspection Protocol

1. **REA Managed Inspection (Primary / MCP):**
   - `mcp__rea__open_binary` $\rightarrow$ Target `Assembly-CSharp.dll`.
   - `mcp__rea__inspect_managed_artifact` $\rightarrow$ Validate MVID and SHA-256.
   - `mcp__rea__inspect_managed_members` with `type_names: ["TypeName"]` $\rightarrow$ Discover fields, methods, properties.
   - *Spillover Handling:* Large results save to `C:\Users\Alex\AppData\Local\hermes\cache\spillover\`; parse via regex in Python.
   - `mcp__rea__close_binary` $\rightarrow$ Release file lock on exit.
2. **ILSpy / CLI (Fallback):** Use pinned script `tools/Decompile-ManagedType.ps1` for offline decompilation.
3. **Asset Tools:** UABEA / AssetRipper for serialized bundle data; UnityDataTool 2.4.0 for asset catalogs.
4. **Safety & Rule Zero:** Always inspect the compiled binary directly. Store decompiled output outside the repository (`~/rogue-command-decomp/`). Never publish decompiled code.

---

## REA-Validated Member Invariants & Metadata Tokens

Authoritative reflection contracts verified against pinned `Assembly-CSharp.dll`:

| Declaring Type | Member Name | Token | Signature / Return Type | Reflection & Invariant Rules |
|---|---|---|---|---|
| `EntityController` | `InProductionCount` | `0x060007df` | `Nullable<int>` (`int?`) | Value type nullable; unbox as `int?`, NOT raw `int` |
| `EntityController` | `EntityId` | `0x060007c6` | `string` (PascalCase) | PascalCase on controller; compare with camelCase below |
| `EntityInAiDeck` | `entityId` | `0x0400005b` | `string` (camelCase) | Field in AI deck configuration struct |
| `ProductionInfo` | `entityId` | `0x04000603` | `string` (camelCase) | Field in factory production queue struct |
| `AiBehaviourManager`| `CurrentAttackWaveNumber` | `0x06000084` (get)<br>`0x06000085` (set) | `int` | Setter is `private static`; invoke via `BindingFlags.Static \| BindingFlags.NonPublic` with `null` instance |
| `Harvest` | `ClaimClosestCrystalAndMoveToIt` | `0x060004ef` | `void ()` | `private instance`; invoke via `BindingFlags.Instance \| BindingFlags.NonPublic` |
| `Harvest` | `MoveToClosestRefinery` | `0x060004f0` | `void ()` | `private instance`; invoke via `BindingFlags.Instance \| BindingFlags.NonPublic` |
| `AudioMixerHelper` | `SetMasterVolume` etc. | - | `(AudioMixer mixer, float sliderValue)` | Exact parameter order: `AudioMixer` first, `float` second |
| `EntityCommand` | Struct definition | `0x020000a2` | Value type struct | Fields: `tag` (`0x040005d5`), `command` (`0x020000a3`), `destination`, `target` |
| `EntityController` | `ExecuteCommand` | `0x06000755` | `(EntityCommand cmd, CommandProcessingType type)` | Dispatches orders; postfix populates `GetLastUnitCommandTag()` |
| `EntityController` | `ExecuteNextCommand`| `0x06000756` | `void ()` | Steps private `LinkedList<EntityCommand> _commandChain` |
| `EntityController` | `ResetCurrentCommand`| `0x06000757` | `void ()` | Resets active command to `DoNothing` (0); invalidates AI lease |
| `EntityController` | `DeleteCommandChain`| `0x06000758` | `void ()` | Clears all queued unit commands; releases AI lease |
| `EntityController` | `ResetAndInitForReuse`| `0x06000760`| `void ()` | Pool recycle preparation; resets effects and combat XP |
| `EntityController` | `OnHasBeenInstantiatedForReuse` | `0x06000761` | `void ()` | Pool re-activation callback |
| `EntityController` | `Produce` | `0x06000770` | `(bool instant, bool forFree, bool doNotTriggerEvent)` | Factory production cycle completion |
| `ExistingControllers` | `Init` | `0x06000180` | `void ()` | Scene start; resets active battlefield registry |
| `ExistingControllers` | `OnEntityCreated` | `0x06000181` | `(EntityController controller, bool isRevive)` | Spawns unit into active player/AI rosters |
| `ExistingControllers` | `OnEntityWillBeDestroyed` | `0x06000182` | `(EntityController controller, EntityController killer)` | Destruction hook; releases aura followers and AI leases |
| `BuildingPlacement` | `CreateBuildingPlacementGhost` | `0x06000320` | `(string entityId, GameObject ghostPrefab)` | Preview hologram instantiation |

---

## Evidence Boundaries

| Tier | Source | Capability |
|---|---|---|
| **Static Inspection** | REA / ILSpy / Unity assets | Confirms tokens, type signatures, and data shapes. Never proves runtime behavior. |
| **Local Validation** | Unit tests / CLI harness | Validates reflection resolvers and DTO structures offline. |
| **Runtime Evidence** | Live Steam session (AppID `1461910`) | Observes actual method execution, pool recycling, and order loop timing. |

---

## References

- [Harmony Policy Skill](../rogue-command-harmony-policy/SKILL.md) — Patching rules for the verified tokens.
- [Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — Telemetry queries and live MCP bridge.
- `tools/Decompile-ManagedType.ps1` — Pinned ILSpy script.
- `docs/research/BASELINE.md` & `AGENT-HANDOFF.md` — Verified environment and token registers.

## Destroy terminal-call compatibility anchor (2026-11-10)

- Recheck the full compatibility tuple before using metadata: Rogue Command `1.0.9`, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`.
- In this exact assembly, `EntityController.Destroy(bool, EntityController)` (`0x06000869`) has one terminal call to `UnityEngine.Object.Destroy(UnityEngine.Object)`. Harmony may redirect only that unique terminal call operand to a same-signature callback while preserving the instruction's opcode and metadata. Treat call count/signature drift as an installation failure. This static anchor does not prove runtime animation; see the separate BepInEx trace at `artifacts/runtime-smoke/retarget-death-visual-cloud-20261110170654/death-hold-runtime-result.json`.
