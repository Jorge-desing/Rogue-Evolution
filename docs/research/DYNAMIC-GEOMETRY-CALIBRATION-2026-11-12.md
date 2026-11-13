# Dynamic Geometry Calibration Attempt — 2026-11-12

## Latest retry: calibration-retry-02

The latest live run produced one matched low/high pair that passed the per-report gates. Both samples used the same Level 4 Game scene, Main Camera21 at 1920×1080 and pose (2, 147.800003, 30), Unity 2021.3.45f2, 120 frames, and the same boundary census: 29 battlefield entities (13 player units including 12 held QA models, 6 AI units, 10 AI buildings).

| Report | Frustum triangles | p95 interval | Census | Probe |
|---|---:|---:|---|---|
| low-load-05.json | 142,654 | 12.1333 ms | 29 before and after | Eligible |
| high-load-02.json | 428,837 | 12.1443 ms | 29 before and after | Eligible |

The pair gives a measured incremental range of 286,183 triangles. Applying the existing 25% reserve yields a candidate incremental allowance of 214,637 triangles, or 357,291 total when added to the measured low-load baseline. This is a candidate only: the profile fitter requires at least three valid samples at two geometry loads. Compiling the two-report candidate returned calibrationStatus=uncalibrated with “calibration needs at least three samples at two or more triangle loads.” The checked-in profile was not changed.

Other reports from this retry were retained but excluded. Low-load captures 02 and 03 and high-load captures 01 and 03 changed their renderer/LOD signatures; low-load 04 changed from 29 to 30 entities; high-load 01 changed from 30 to 29 entities and measured p95 18.1737 ms; high-load 03 changed from 29 to 26 entities. These transitions show that the Level 4 AI population is not controlled long enough to provide a third matching sample. Do not combine these reports with the valid pair or raise production budgets from the candidate calculation.

The hashed evidence bundle, audit, candidate profile input/output, and raw reports are under artifacts/runtime-smoke/geometry-calibration-qa-20261112T201515Z/calibration-retry-02/. No source profile, FBX, prefab, bundle, or generated Level 2 manifest was changed. The source declarations still cover 57 Level 2 modules and 47 presets, while the generated output remains the prior 35-module build.

Cleanup completed: the temporary probe and camera controller were removed; the original camera pose and renderer flags were restored; Rogue Command exited normally; the Profile3 QA run was snapshotted; and the full profile checkpoint verified exact equality with selector 2. Steam Cloud settings stayed account-enabled/app-disabled. The installed loader already matched the verified Release artifact at SHA-256 81573ca10d6b78c4b8e4ed9985f3987f17fcf1ebc9d6a19475b6c7547ec545b7, so no copy was needed.

## Prior calibration batch

The live measurements were **not integrated into** `content/source-art/model-budget-profile.json`. Two low-load schema-v3 captures passed the probe's technical checks, but three higher-load captures failed the renderer/geometry stability gate. The high reports cannot be reinterpreted as eligible samples, and the profile remains `uncalibrated` with no authored scene demands. No FBX, prefab, bundle, or loader output was rebuilt.

The temporary v3 probe source now records `geometryStabilityTrace` at the initial census and every periodic geometry check, including triangle/renderer deltas, exact signatures, camera state, and time-scale state. It compiled against the installed Unity 2021.3.45f2 assemblies as a temporary `netstandard2.1` project with 0 warnings and errors. The exact stability gate is unchanged. The new trace has not yet been exercised in-game and provides no new calibration evidence by itself.

## Captures

All five reports use the same `Game` scene, `Main Camera21`, Unity `2021.3.45f2`, 1920×1080 display, and camera pose/FOV. The camera was at `(-226.2, 147.8, -39.8)`, rotation `(48.000004, 135, 0)`, FOV `26.231289`.

| Report | Frames | Initial frustum triangles | Candidate / counted renderers | p95 unscaled interval | Probe eligible |
|---|---:|---:|---:|---:|---|
| `level4-load-low-sample-01.json` | 240 | 120,110 | 748 / 705 | 12.123564 ms | Yes |
| `level4-load-low-sample-02.json` | 240 | 120,354 | 750 / 707 | 12.125100 ms | Yes |
| `level4-load-high-unstable-sample-01.json` | 240 | 330,460 | 883 / 840 | 12.128664 ms | No |
| `level4-load-high-unstable-sample-02.json` | 240 | 328,924 | 866 / 823 | 12.148700 ms | No |
| `level4-load-high-unstable-sample-03.json` | 120 | 329,434 | 872 / 829 | 12.140265 ms | No |

The high load was a controlled in-game test using project Bulwark and Gravitas units. Its initial triangle estimate was about 2.74× the low load, but every high report set `geometryLoadStable=false` and `calibrationReady=false`. The high p95 values also stayed close to the low values; because those samples are ineligible, that comparison is diagnostic only and is not evidence of a geometry-to-frame-time fit.

V3 records one initial geometry census, then compares exact triangle totals, renderer counts, and a renderer/mesh signature every 30 rendered frames. It does not store the values from each recheck. Therefore these reports cannot establish whether aggregate geometry stayed within a small tolerance while individual renderers or LODs changed. Keep the v3 gate intact; do not flip report flags or ingest the high files. A future probe schema can revisit this only after it records every periodic count and signature, exposes aggregate variation, and has explicit renderer-count and low/high separation gates.

## Runtime cleanup

- The temporary `RuntimeModelBudgetProbeV3` was removed and a component census returned no remaining probe.
- Rogue Command exited normally. Steam Cloud was enabled for the account and disabled for the app; no Cloud setting changed.
- The selector remained `2`. The run changed only `Profile2/metaSavegame.dat`; that file was restored from the verified checkpoint. The full checkpoint then reported `profileFilesExactMatch=true`, `selectorMatch=true`, and `safe_to_finalize=true` across all 21 profile files/selector entries.
- The latest Release loader was redeployed with the game closed. Artifact, pre-deploy backup, and installed DLL all hash to `81573ca10d6b78c4b8e4ed9985f3987f17fcf1ebc9d6a19475b6c7547ec545b7`; the manifest is `artifacts/deployment-backups/dynamic-model-budget-calibration-20261112T0232Z/deployment-manifest.json`.

## Builder write-order guard

The audit found five recipe builders that could save wave details over their canonical `.blend` before budget rejection: Arc Lance Walker, Field Medic Rover, Salvage Hauler, Skyguard SAM, and Tidebreaker Hovercraft. They now replay those recipes in memory. Thirteen other direct builders had output-directory creation before the gate; those calls, the Bulwark's output paths, and Ironclad's paths are now deferred. `RogueVehicleBuilder` and `RogueStructureBuilder` also defer directory creation until calibrated demand and measured LOD0 geometry pass. The static regression `tests/python/test_model_builder_budget_gate_order.py` checks the direct builders, shared methods, and source preservation.

## Evidence files

Raw reports and profile checkpoints are under `artifacts/runtime-smoke/dynamic-model-budget-calibration-20261112T015657Z/`. They are local ignored artifacts. The canonical profile remains unchanged and all export builders must continue to fail closed until a valid calibration and per-scene asset demands exist.
