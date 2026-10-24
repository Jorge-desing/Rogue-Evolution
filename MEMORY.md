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

## Checkout snapshot (2026-10-24)

- Canonical project-memory snapshot: keep Codex and Antigravity notes aligned with verified facts here; check `AGENT-HANDOFF.md` and live checkout before acting. Recorded validation remains historical unless freshly rerun.


- **Checkout snapshot at the start of this memory-only synchronization (2026-10-24):** loader `1.19.0`, branch `codex/rogue-unit-balance-20261018`; local HEAD, upstream, and `origin` matched `04431f5ab33061562e68d48432ed2e0a7bbe7dfd` (`docs(memory): synchronize agent memory snapshots`). The latest code-bearing commit remains `eb8e699b43db5b8346ebe39e8f70717c105aa9a3`. The static audit `docs/research/LEGACY-UNITS-LOADER-1.19-AUDIT.md` records checks at `0bfa7ccb283960b745aed06d77f05b5b3ee48620`; these are historical, not rerun in this synchronization. Preserve modified nested worktrees `.worktrees/cargador` and `.worktrees/task-graph-validation/`. This synchronization changes only memory snapshots; no tests, builds, or gameplay were run.
- **Latest implementation constraints:** nine native product/factory pairs and three stages. `units[].variants` remains metadata-only (no selection/application or AP/HE effect); IDs reject LF, CR, CRLF, U+2028, U+2029, and each variant allows at most 64 raw overrides. Preserve 25 verified per-unit `EntityBalancingStore.ChangeableValue` row mappings, excluding global `ManaRechargePerSecond`; mutate copied rows and commit only after successful registration, never seed final-value caches, and retain precedence `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`. Bounds are `16,777,216` for integer rows and `Single.MaxValue` for floats. Unverified structured fields remain descriptive. `startCountRange` is an ordered invariant-culture pair 1–64 bounded by final `maxWidth`; `cardReward` is a value type, not `OpenReward`.
- **Static legacy-unit audit** (`docs/research/LEGACY-UNITS-LOADER-1.19-AUDIT.md`; inventory at `bfcacea6`, final recorded checks at `0bfa7cc`): nine native units use 23–24 raw row-backed balance fields each; migration to `customBalance` is only a candidate because simulator parity covers a subset. Require per-key C# row/registration regressions before migrating; keep direct enum/flag overrides on their existing route. `sample-expansion` lacks both declared bundle files and is not distributable. The audit notes stale Ironclad visual/card-count documentation. Recorded checks: Python 47/47, schema 3 manifests/0 errors, registry 3/0 issues, native bundle refs 16/0 errors, visual roots 9/0 errors, Release build 0 warnings/errors, harness 263/264 (one Windows symlink-privilege skip). These are static/local results recorded at `0bfa7cc`; they were not rerun during this memory-only sync, and no game was launched.
- **Runtime/evidence boundary:** natural eligible offer→selection→placement→production/render/stat use, reactive AI response, and Native patrol remain unverified. Static inspection, local build/tests, and live Steam evidence are separate tiers; builds, harnesses, simulations, or one map observation do not prove gameplay. Recheck the current process, selector, profile, and deployment before runtime work. Profile2 is the dedicated QA profile with no tutorial: do not ask, inspect, infer, gate QA on tutorial status, propose a bypass, or switch to Profile3. Preserve saves/backups and never overwrite a save without a fresh safety check/authorization.
- This memory-only synchronization did not rerun builds/tests or gameplay. The prior Profile2→Profile3 run is historical; its selector/deployment state must not be assumed current.

## Working rules

- Rogue Command QA uses Profile2 as the user's designated dedicated profile; its tutorial state is established as no tutorial. Do not ask, infer, inspect, or gate QA on tutorial status there, and never propose a tutorial bypass. Check the authoritative selector and current save/profile safety, proceed on Profile2 when safe, do not switch to Profile3, and never overwrite saves without a fresh safety check/authorization. Preserve all profiles and backups.
- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
