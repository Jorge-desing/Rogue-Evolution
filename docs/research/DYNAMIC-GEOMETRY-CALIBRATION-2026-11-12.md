# Dynamic Geometry Calibration Attempt — 2026-11-12

## Decision

The live measurements were **not integrated into** `content/source-art/model-budget-profile.json`. Two low-load schema-v3 captures passed the probe's technical checks, but three higher-load captures failed the renderer/geometry stability gate. The high reports cannot be reinterpreted as eligible samples, and the profile remains `uncalibrated` with no authored scene demands. No FBX, prefab, bundle, or loader output was rebuilt.

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
