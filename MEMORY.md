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

## Mem0 Memory

- Rogue Command snapshot (2026-10-24): branch `codex/rogue-unit-balance-20261018`, loader `1.19.0`. Verified HEAD `d6b89ae138ee8421bade52f8d16d7b6c205e5eea` (`d6b89ae`); local HEAD/upstream/origin all match. Latest commits: `d6b89ae` fix(loader) muzzle-flash socket resolution and warm pre-spawn pool, `1c3a8d6` feat(validation) cross-pack unit-upgrade/synergy graph checks, `231fb2f` docs(memory) sync project state across agents. Preserve unrelated dirty `.agents/skills/rogue-command-unit-authoring/SKILL.md`, `.worktrees/cargador`, and `.worktrees/task-graph-validation`.
- The loader includes first-class `ContentBuildingDefinition` and `ContentPackDefinition.buildings[]` for standalone buildings, with DTO/deep-clone/schema/validation, bounded structured balance and asset checks, optional visuals/card Sprite/localization, and runtime registration through `ContentPackRuntimeLoader.CreateStandaloneBuilding`. Commit `d6b89ae` also improves muzzle-flash socket resolution and warms the pre-spawn pool against cold hitches.
- Current validation on HEAD `d6b89ae` (rerun this sync): Release build 0 warnings/errors; C# harness `313/314` passed (1 Windows symlink-privilege skip, 0 failures); Python unittest suite blocked by missing `rpds.rpds` environment dependency; registry validation 3 manifests/0 issues via `tools/validate_loader_registry.py`. No fresh Unity rebuild or gameplay test is recorded.
- Current uncommitted graph-validation changes touch `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs`, `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs`, validation `Program.cs`, `tools/validate_loader_registry.py`, and `tests/test_validate_loader_registry.py`; they add cross-pack unit-upgrade/synergy cycle/depth checks and runtime pack disabling. They are unbuilt and untested; preserve separately from unrelated dirty paths.
- Natural eligible offer→selection→placement→production/render/stat use and tactical AI behavior in a match remain unverified. Builds, harnesses, stubs, and static inspection do not prove gameplay. Profile2 is dedicated QA without a tutorial: do not ask, inspect, infer, gate, bypass, or switch profiles for tutorial; preserve saves and never overwrite without a fresh safety check/authorization.
- Before reflection, recheck together Rogue Command version, Unity `2021.3.45f2`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, and MVID `db0e9462-128d-4296-9900-de18b4bd0c00`.
## Working rules

- Rogue Command QA uses Profile2 as the user's designated dedicated profile; its tutorial state is established as no tutorial. Do not ask, infer, inspect, or gate QA on tutorial status there, and never propose a tutorial bypass. Check the authoritative selector and current save/profile safety, proceed on Profile2 when safe, do not switch to Profile3, and never overwrite saves without a fresh safety check/authorization. Preserve all profiles and backups.
- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
