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
- The fresh Release plugin/harness builds had zero warnings/errors; the exact Release harness passed `200/201`, with one Windows symlink-privilege skip. This is local build/test evidence, not gameplay acceptance.
- Still unverified in a natural Steam run: eligible modded reward through selection and factory placement, custom-unit production/spawn, rendering and stat consumption, reactive AI response, and Native patrol route behavior. Treat offer, placement, production, rendering, stage generation, and AI action as separate acceptance steps.
- Label findings as static inspection, local validation, or live Steam runtime. Never promote one evidence tier into another.

## Latest repository recheck (2026-10-22; balance-row worktree and memory sync)

- **Starting snapshot:** `C:/Users/Alex/Documents/Mod Rogue Command`, branch `codex/rogue-unit-balance-20261018`, loader `1.19.0`; local HEAD and upstream were `e3825142f395f0a53938d99ed0751363d534100b`, confirmed with `git ls-remote` before this update.
- **Preservation:** at inspection, 12 tracked paths were already modified across balance mapping, schema/validation, row mutation, tests, handoff/research, plan, and skill reference; `.worktrees/` was untracked. This task leaves those edits untouched and stages only root `MEMORY.md`; consult the handoff for changing details.
- **Current local row-balance change:** the worktree maps 25 exact per-unit `EntityBalancingStore.ChangeableValue` members to verified `EntityBalancingParameters` fields and excludes global-only `ManaRechargePerSecond`. Special mappings include `Attack1Cooldown` → `attackCooldown` and `SpawnTtl` → `duration2`. Schema/validation reject unknown keys, negative/non-finite values, and fractional or out-of-Int32 integer values. Overrides go into incoming rows before registration; precedence is `customBalance` > raw `balanceOverrides` > positive structured `movement.moveSpeed`.
- **Fresh local verification (2026-10-22):** Release plugin and validation-harness builds completed with 0 warnings/errors. The exact fresh Release harness passed 200/201 checks; the one skip was the Windows symlink-privilege check. This is local evidence, not gameplay acceptance.
- **Recorded Steam evidence (2026-10-21; not rerun here):** the handoff records a Steam/BepInEx MCP probe in `Game` where the row and native getters for `sample-native:scout-tank` returned `MoveSpeed=15.75`, `MaxHealth=137`, `Damage1=7.25`, and `Attack1Cooldown=1.15` across cache invalidation. This proves row/getter behavior in that process only, not custom-unit spawn or gameplay stat consumption. The prior `MainMenu` `14.5`/`130` cache-seeding smoke is superseded. Rollback is documented in the handoff; installation/profile state and the game compatibility tuple were not rechecked during this memory sync.
- **Other constraints and gaps:** do not infer mappings from DTO names. Stage bindings remain only `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`; `cardReward` is a value type. Structured armor remains descriptive because native `MaxArmor` and `ArmorProtection` differ. Native patrol defaults to `AttackMove`; opt-in `Native` fails closed unless the command is `DoNothing` and the exact private queue is verified empty. Natural eligible offer → factory placement → custom-unit production/spawn/rendering/stat consumption, reactive AI, and native patrol runtime acceptance remain pending.

## Working rules

- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
