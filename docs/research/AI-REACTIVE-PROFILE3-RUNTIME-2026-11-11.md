# AI Reactive Profile QA Run: Profile3 (2026-11-11)

## Scope and identity

This was a BepInEx MCP runtime pass against the installed Release loader while continuing the unit-loader/AI work. No loader source changed. Game identity: Rogue Command 1.0.9, Unity 2021.3.45f2, BepInEx 5.4.23.5 Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Release 1.20.6 SHA-256: `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf`.

## Profile and run

With the game closed, Profile2 was copied into the authorized Profile3 QA slot after an all-profile checkpoint. Both tutorial-completion flags were true on the source and clone. The source remained untouched. Profile3 was selected through the native UI; the overwrite dialog was read before starting a new run. No tutorial method was called and `tutorialState.txt` was not edited. Steam Cloud was already disabled by the user and was not changed.

The run began with an offered unit card and was advanced through native route nodes to Game level 4. The active `LoadingHider` was cleared by invoking its actual `Iniciar` button through BepInEx UI diagnostics. The match was frozen with `Time.timeScale=0` for inspection. Tutorial controllers reported no active tutorial UI.

Profile evidence is under `artifacts/runtime-smoke/ai-reactive-stage13-qa-profile3-20261111T044842/`: `profile-checkpoint-before.json`, `profile3-cloned-from-profile2.json`, the source and destination backups, and `Profile3-generated-run-after-exit/manifest.json` plus generated files. After exit, all four original Profile3 files were restored and selector `2` was restored. Re-running `tools/runtime_profile_checkpoint.py verify` found no additions, removals, or changes across the 20 listed files, confirmed the selector, and reported `safe_to_finalize=true` (21 checkpoint entries including the selector). Profile2 remained unchanged.

## Active stage and AI observations

- The active Game stage resolved to native sequence index 0, `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit`; it declared no mod AI profile and had zero loader-bound runtime profile instances.
- Configured sequence row 13 resolved to `sample-reactive-ai:expansion:sample-reactive-ai:counterstrike-lane`, declaring `sample-reactive-ai:base-defense`. This row was configuration evidence only; it was not the active stage. The sequence contained 14 configured rows, 0–13 resolved, and unresolved tail rows 14–31, so the overall sequence status remained `partial`.
- The native Game-scene director census reported attack 8, defend 6, scout 4, build 10, produce 3, and other 2. Its construction queue listed `PCXSupplyTank` (2) and `CF0.5Factory` (5). The roster census reported 14 AI units and 11 AI buildings. Production census reported five AI factories, one active production, one queued unit, and zero player factories. These are native-stage baseline observations, not behavior from the target mod profile or proof of a modded unit product.
- The loader audit listed `sample-reactive-ai:base-defense` with four registered modules and `activeInstanceCount=0`.

## Harmony and evidence boundary

The runtime hook census reported `hooksInstalled=true` and 54 hooks. Observed counters included `unitCreationTranspiler=26`, `entityRegistered=172`, `unitReuseTranspiler=0`, `initEntityControllerTranspiler=146`, `entityOnInstantiated=172`, `entityReuseOnInstantiated=0`, `factoryProduceTranspiler=17`, `entityRegistryReset=4`, and `entityInit=175`; death counters were zero. These counts indicate installed hooks/callback activity only. They do not establish target-profile execution, semantic unit production, or retarget behavior.

**Result:** the configured reactive profile and its module behavior remain unverified in a live match because the active stage was native row 0. No more route nodes were selected to reach the unrelated configured row. This pass also made no new animation-retarget claim. Runtime values above were observed through BepInEx MCP; a separate raw telemetry JSON export was not saved. No screenshots or Computer Use were used.

## Deployment

No loader source changed, so no rebuild was required. Rogue Command was confirmed closed before redeployment. The Release artifact, pre-deploy backup, and installed plugin all have SHA-256 `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf`; the backup and copy readback are recorded in `artifacts/deployment-backups/ai-profile3-pass-20261111T123948Z/deployment-manifest.json`.
