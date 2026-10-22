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
- The latest fresh non-incremental Debug/Release harness builds and Release plugin build recorded in the 2026-10-22 handoff had zero warnings/errors; both exact harness DLLs passed `207/208`, with one Windows symlink-privilege skip. This is local build/test evidence, not gameplay acceptance, and was not rerun during this memory-only update.
- Still unverified in a natural Steam run: eligible modded reward through selection and factory placement, custom-unit production/spawn, rendering and stat consumption, reactive AI response, and Native patrol route behavior. Treat offer, placement, production, rendering, stage generation, and AI action as separate acceptance steps.
- Label findings as static inspection, local validation, or live Steam runtime. Never promote one evidence tier into another.

## Checkout checkpoint before this memory sync (2026-10-22)

- **Repo/branch/plugin:** `C:/Users/Alex/Documents/Mod Rogue Command`, branch `codex/rogue-unit-balance-20261018`, loader `1.19.0`. Immediately before this update, local HEAD, upstream, and fresh `git ls-remote` all matched `365eb986ecda0c1de311569b573ccb8cc2dba8be`; that prior commit changed only root `MEMORY.md`. The row-based balance implementation and its schema, validation, tests, and handoff are already committed.
- **Working tree:** the parent repository reports only `.worktrees/cargador` as modified (gitlink `11e9968e340eb8e30d9b0bdb133f10d44ecbead2`). The nested checkout is on `main` at that commit, has uncommitted `MODDING_PLAN.md` and `MODLOG.md` changes, and is 99 commits behind `origin/main`. Preserve it; do not stage, reset, clean, or update the nested checkout.
- **Committed balance contract:** `customBalance` maps 25 exact per-unit `EntityBalancingStore.ChangeableValue` keys to verified `EntityBalancingParameters` fields; global-only `ManaRechargePerSecond` is excluded. Apply values to incoming rows before registration so native getters can rebuild caches and preserve card changes. JSON values remain `double`; integer-backed values are bounded to `16,777,216`, float-backed values to `Single.MaxValue`. Special fields: `Attack1Cooldown` → `attackCooldown`, `SpawnTtl` → `duration2`. Precedence is `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`.
- **Local evidence recorded in the handoff:** clean non-incremental Debug/Release harness builds and a Release plugin build are recorded with 0 warnings/errors. The handoff's final-validation entry says `207/208`, while a later current-status note says `208/209`; each records one Windows symlink-privilege skip. Keep this discrepancy explicit; these results were not rerun during this memory sync and are not gameplay proof.
- **Runtime boundary:** the 2026-10-22 MainMenu smoke used a probe-invoked hook and read `MoveSpeed=14.5`, `MaxHealth=16,777,216`, `Damage1=8`, `Attack1Cooldown=0.9` across cache invalidations; `MaxHealth=16,777,217` was rejected. This did not prove a natural factory call, spawned unit, or gameplay stat consumption. The historical 2026-10-21 Game-scene getters (`15.75`, `137`, `7.25`, `1.15`) prove row/getter behavior only.
- **Acceptance and safety:** the latest attempt had no interpretable factory-menu opening, natural offer, verified placement, or target product. Installation files/logs were restored and selector returned to `4`; preserve Profile2's QA-created `savegame.dat` and changed `metaSavegame.dat`; `tutorialState.txt` was not edited. Profile5 is empty but unauthorized, so no empty authorized QA profile is available.
- **Constraints/gaps:** do not infer mappings from DTO names. Stage bindings remain only `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`; `cardReward` is a value type; structured armor remains descriptive. Native patrol defaults to `AttackMove`; opt-in `Native` requires `DoNothing` and exact empty-queue verification. Natural offer → placement → production/spawn/rendering/stat consumption, reactive AI response, and Native patrol runtime acceptance remain pending. No build, test, or live-game run was performed for this memory-only update; read the handoff for evolving evidence.

## Working rules

- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
