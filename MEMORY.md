# Project Memory — Rogue Command Expansion

This is a compact, durable orientation for agents. For the changing checkout state and detailed evidence, read [`docs/research/AGENT-HANDOFF.md`](docs/research/AGENT-HANDOFF.md) first; follow [`AGENTS.md`](AGENTS.md) and the content contract in [`content/schema/README.md`](content/schema/README.md).

## Project baseline

- The loader is version `1.19.0`. The goal is playable additive Rogue Command content; BepInEx 5 Mono x64 and Harmony are supporting infrastructure.
- Historical compatibility baseline: Rogue Command `1.0.9`, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Recheck the full tuple together before relying on reflected game members or metadata. Do not assume this baseline is still current.
- Keep generated outputs under ignored `artifacts/`. Preserve the Unity authoring project and unrelated staged, unstaged, and untracked work.

## Verified implementation constraints

- Unit-definition changes follow the complete contract: JSON schema, DTO, explicit null-safe deep clone, package validation, then an exact runtime mapping only when the native consumer is verified. Schema acceptance or a DTO name alone does not prove gameplay behavior.
- `customBalance` maps 25 verified per-unit `EntityBalancingStore.ChangeableValue` keys to `EntityBalancingParameters` row fields; global-only `ManaRechargePerSecond` is excluded. Apply overrides to the incoming row before registration, never seed final-value caches. Precedence: `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`.
- The balance override enum is `EntityBalancingStore.ChangeableValue`. Preserve native cache invalidation so getters rebuild from the row and compose card changes.
- `StageParameterBindingContract` supports only `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`; `cardReward` is a value type, not `OpenReward`. Do not infer `cardReward.count/weight` bindings.
- Structured armor fields remain descriptive: native `MaxArmor` and `ArmorProtection` have distinct meanings. Require finite nonnegative armor/shield values and finite `damageReduction` in `[0,1]`.
- Do not add mappings for other structured unit fields without a verified native or compiled-runtime consumer. The 2026-10-21 opportunity audit found no new safe mapping (No-Go).
- For `sample:patrol-and-intercept`, `AttackMove` remains the default. Native patrol must fail closed unless the current command is `DoNothing` and the exact private queue is verified empty; it must preserve queued orders. Local tests do not establish in-game pathing or command resumption.

## Evidence boundaries and known gaps

- Steam/BepInEx MCP evidence from 2026-10-21 confirmed the row-based `sample-native:scout-tank` provider row and native getters returned `MoveSpeed=15.75`, `MaxHealth=137`, `Damage1=7.25`, and `Attack1Cooldown=1.15`, including after cache invalidation. This proves row/getter behavior in the game process only—not custom-unit instantiation or gameplay consumption. See the artifact and evidence notes in the handoff.
- A 2026-10-22 MainMenu boundary smoke loaded valid `MaxHealth=16,777,216` and rejected `16,777,217`; the probe-invoked loader hook read row/native getters across cache invalidations. It did not spawn a unit or prove gameplay stat consumption. See the handoff for exact values and restoration evidence.
- An earlier 2026-10-22 non-incremental Debug/Release harness record reported `207/208` with one Windows symlink-privilege skip. It is superseded by the latest checkout validation below; local build/test evidence is not gameplay acceptance.
- Still unverified in a natural Steam run: eligible modded reward through selection and factory placement, custom-unit production/spawn, rendering and stat consumption, reactive AI response, and Native patrol route behavior. Treat offer, placement, production, rendering, stage generation, and AI action as separate acceptance steps.
- Label findings as static inspection, local validation, or live Steam runtime. Never promote one evidence tier into another.

## Checkout snapshot (2026-10-23)

- **Repository identity:** `C:/Users/Alex/Documents/Mod Rogue Command`, branch `codex/rogue-unit-balance-20261018`, loader `1.19.0`. The code-bearing base at the start of this refresh was `04571a900f281608fee51295163c2894271c2db9`; at the latest verification HEAD/upstream/fresh remote matched `14b9abb30273b295e1b46fb0538f9fec1238e4ff` (`docs(memory): clarify snapshot verification`). Memory-only commits `820327cf330b4c09b0719b9edcfb58d53d920a9a` and `14b9abb30273b295e1b46fb0538f9fec1238e4ff` were pushed; `0715b105249814114aa0c8dca678a37357241e0d` is the preceding implementation/test snapshot.
- Root tracked files were clean and unstaged at inspection; preserve modified nested worktrees `.worktrees/cargador` and `.worktrees/task-graph-validation/`. This sync updates project `MEMORY.md` and Codex/Antigravity memories only. Commit `04571a900f281608fee51295163c2894271c2db9` corrected the in-process bridge endpoint and added the root `/.hermes/` ignore; latest checked HEAD/upstream/remote is memory-only commit `14b9abb30273b295e1b46fb0538f9fec1238e4ff`.
- **Committed loader updates:** `units[].variants` is schema-backed and deep-cloned but metadata-only; no runtime selection/application or AP/HE effect. The implementation history also includes iterative upgrade/synergy graph validation (cycle/depth limit 5), production-queue feasibility warnings, factory/pool/localization diagnostics, provider-row override synchronization after successful registration, hardened registry/simulator validation, and a regression for custom units without `nativeTemplateId`.
- **Validation:** `python -B -m unittest discover -s tests -v` passed **45/45** on the code-bearing base `04571a900f281608fee51295163c2894271c2db9`, and was rerun successfully after the memory-only commits `820327cf330b4c09b0719b9edcfb58d53d920a9a` and `14b9abb30273b295e1b46fb0538f9fec1238e4ff`. These later commits change memory documentation only. The preceding loader commit `b0ef57b` has historical Release plugin/harness 263/264 (one Windows symlink-privilege skip) and Python 41/41; no C# harness or gameplay test was performed in this sync.
- **Content and balance constraints:** sample pack has 9 product/factory pairs and 3 stages; native `StageParameters_Pools_Level3` lacks `BP_CheaperVehicles`. Keep the 25 verified per-unit `EntityBalancingStore.ChangeableValue` → row mappings, exclude global `ManaRechargePerSecond`, mutate copied rows and commit only after successful registration, never seed final-value caches, and preserve precedence `customBalance` > raw overrides > positive finite movement. Integer/float limits remain `16,777,216`/`Single.MaxValue`; unverified structured fields remain descriptive. `startCountRange` is an ordered invariant-culture integer pair 1–64 bounded by final `maxWidth`; `cardReward` is a value type, not `OpenReward`.
- **Runtime boundary:** a single 2026-10-22 Profile2 continuation saw the Scout factory missing from two assigned pools (8/11 candidates) but present in an unassigned `BP_CheaperTanks` clone; this proves neither global exclusion nor loader failure. Natural offer→placement→production/spawn→render/stat use, reactive AI response, and Native patrol route remain unverified. Historical profile audit found Profile2 has a save, Profile1 was not authorized for QA, Profile5 is not natively selectable, and no empty authorized QA profile is established; protect saves and preserve Steam Cloud. The documented in-process game bridge is `http://127.0.0.1:8080/mcp/`, separate from companion MCP; recheck process and endpoint before runtime work. This sync did not contact the bridge or launch the game.


## Working rules

- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
