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

## Checkout snapshot (2026-10-22; HEAD and remote rechecked after balance calibration)

- **Repo/branch/plugin:** `C:/Users/Alex/Documents/Mod Rogue Command`, branch `codex/rogue-unit-balance-20261018`, loader `1.19.0`. Local HEAD, upstream tracking ref, and fresh `git ls-remote` match at `90e4dac2131fef527e78defb8da8a9e21c36f4ea` (`fix(balance): bring siege artillery into calibration band`). This is repository identity, not gameplay acceptance.
- **Preserve the dirty worktree:** five project skills, nested `.worktrees/cargador`, sample pack/schema, handoff/opportunity notes, pool-scope/overlay sources, and simulator/registry tools are modified. Stage-authoring references, eight `.hermes/plans/2026-10-22_*.md` plans, and Python tests are present; preserve all unrelated work. This memory refresh changes only this file and does not validate those edits.
- **Content/configuration:** the sample has 9 product/factory pairs and 3 stages. `training-variant` explicitly maps `BP_CheaperVehicles` → Scout/Bike and `BP_CheaperRobos`/`BP_CheaperMechs` → Arachnid/Overlord; fortified/core omit explicit pool entries. Scoped AI roster counts are 3/3, 3/3, and 4/4. These are manifest facts, not natural-offer proof. `StageParameters_Pools_Level3` lacks `BP_CheaperVehicles`; do not declare that entry on Level3 pools.
- **Balance tooling:** supported-field precedence is `customBalance` > raw `balanceOverrides` > structured movement fallback. The handoff's latest offline `--all-mod-units` simulation reports Sandlance Scout CEI 36.9 and heavy artillery CEI 16.0; these are model outputs, not measured/live balance evidence. The timed duel/squad model executes primary `Damage1` volleys and does not reproduce full delivery or target selection.
- **Latest recorded local validation (from the handoff; not rerun by this memory sync):** Python unit tests 11/11; Draft 2020-12 accepted all 3 manifests; registry tool found 0 issues; Debug and Release harnesses each reported 227/228 with one Windows symlink-privilege skip; non-incremental Release plugin build reported 0 warnings/errors. Treat as snapshot-specific local evidence, never as gameplay proof.
- **Runtime boundary:** the latest natural-pool audit was a continuation of saved QA Profile2, not a clean no-save profile. On one `training-variant` map, queried vehicle/robo pools had 8/11 native candidates and neither contained the Scout factory; it appeared in an unassigned pool clone. No natural offer, selection, placement, production/spawn, rendering, or configured-stat use was observed. This does not prove global exclusion or loader failure.
- **Profile safety:** deployment/logs and selector `4` were restored; preserve the Profile2 QA save and its recorded metadata state. Profile5 remains unauthorized despite being empty; keep Steam Cloud unchanged. No empty QA profile is authorized for a FirstRun-bypass acceptance run.
- **Durable implementation constraints:** do not add structured unit fields or stage overrides without a verified consumer; prefer validated `EntityBalancingStore` enum-backed routes and TDD for `MoveSpeed`/`MaxHealth`. Preserve the 25 verified per-unit balance-row mappings and exclude global-only `ManaRechargePerSecond`. Stage bindings remain limited to `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`; armor fields remain descriptive. Native patrol defaults to `AttackMove` and must fail closed without `DoNothing` plus an exactly verified empty queue. Natural offer→production/render/stat use, reactive AI, and Native patrol acceptance remain pending; consult the handoff for evolving evidence.

## Working rules

- Before repository work, inspect `git status`, branch, remotes, and relevant diffs; preserve unrelated work and stage only exact task-owned paths. Follow `AGENTS.md` for commit/push and verify the upstream/remote SHA.
- Before live-game work, read `docs/research/STEAM-RUNTIME-CONTROL.md`; protect user profiles and saves, use an isolated QA profile when needed, keep Steam Cloud unchanged, and verify rollback.
- For code changes, use TDD and the documented validation harness in `README.md`. Recheck the compatibility tuple before reflected calls. Builds and stubs are not proof of gameplay.
