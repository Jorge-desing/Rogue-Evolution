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


- **Checkout snapshot (2026-10-24):** loader `1.19.0`, branch `codex/rogue-unit-balance-20261018`; the read-only Git check during this memory sync verified local HEAD, upstream tracking ref, and `origin` all at `f3efa20aecba657047664477cfb5ae4f1553296a`. The latest code-bearing commit remains `603595c1bea75e70ecd80ba62bdfeaece66bcf15`. The worktree is dirty in the paths listed below; preserve modified nested worktrees `.worktrees/cargador` and `.worktrees/task-graph-validation/` and all pending edits.
- **Current uncommitted worktree:** docs/schema/tests/tool changes align visual asset claims with the manifest and bundle; `validate_content_bundle_references.py` checks factory card/prefab references using each factory's own bundle ID. Package validation now rejects duplicate structured/raw declarations and prevents upgrades/synergies from targeting a production factory as if it were a unit. Preserve all current source/docs/tests edits; memory-only delivery must stage only exact memory paths.
- **Visual/content snapshot (static/local only):** the pack declares nine product overlays, two factory overlays and seven custom card Sprites; the Ironclad bundle asset has an authored armature, articulated legs and canonical sockets. The offline validator resolved 25 product/factory references with 0 errors. This does not prove runtime selection, animation or Steam rendering.
- **Structured unit fields and AI:** alongside the 25 verified `customBalance` row mappings, `ContentUnitDefinition` exposes `combatValue` (integer 0–16,777,216), `gridSize` (1–16), `cardModelScalingFactor` (finite, >0.01 and ≤100), `coinsAmount` (0–16,777,216), and `firePointCount` (1–64), with schema/DTO/deep-clone/validation and reflective assignment to boxed `EntityBalancingParameters` fields. This is static evidence only; verify the full game compatibility tuple before relying on reflected identities. Registered modules `sample:factory-reinforcement` and `sample:flanking-strike` have bounded parser tests; live-match behavior remains unverified. Preserve row mapping/cache/precedence rules recorded above.
- **Latest local validation recorded for 2026-10-24 (not rerun during this memory sync):** Python `unittest` 52/52; C# validation harness 270/271 (one Windows symlink-privilege skip); Release plugin build 0 warnings/errors; registry validator 3 manifests valid/0 issues; AssetBundle reference validator 25 references/0 errors. No fresh Unity rebuild or in-game test was run. These are local/static checks, not gameplay evidence. `units[].variants` remains metadata-only; `cardReward` is a value type, not `OpenReward`; `sample-expansion` lacks both declared bundles and is not distributable.
- **Runtime/evidence boundary:** natural eligible offer→selection→placement→production/render/stat use, reactive AI response, and Native patrol remain unverified. Recheck process, selector, profile, and deployment before runtime work. Profile2 is the dedicated QA profile with no tutorial: do not ask, inspect, infer, gate QA on tutorial status, propose a bypass, or switch to Profile3. Preserve saves/backups and never overwrite a save without a fresh safety check/authorization.

## Working rules

- Rogue Command QA uses Profile2 as the user's designated dedicated profile; its tutorial state is established as no tutorial. Do not ask, infer, inspect, or gate QA on tutorial status there, and never propose a tutorial bypass. Check the authoritative selector and current save/profile safety, proceed on Profile2 when safe, do not switch to Profile3, and never overwrite saves without a fresh safety check/authorization. Preserve all profiles and backups.
- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
