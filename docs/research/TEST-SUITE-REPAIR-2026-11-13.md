# Complete Python suite repair — 2026-11-13

Local validation: `python -m tools.run_python_tests --output artifacts/suite-fix/final.json` completed successfully (exit 0). Results: {'passed': 731, 'skipped': 1}. No test was removed or newly skipped.

Ironclad tests now inspect the current blend in a disposable pinned-Blender process and the current AssetBundle with UnityPy, rather than requiring ignored diagnostic JSON files. Profile assertions reflect reviewed unsupported-hardware removals for Arachnid, Sentinel and Command Relay; explicit absence checks remain. Tactical bundle inventory includes the existing foundry and containment module without weakening unit rig checks.

Arachnid's current FBX was a static export. Re-exported the existing rigged blend with armature and baked animations, preserving the source blend; backup and export log are under `artifacts/suite-fix/`. The pinned Unity sample-native builder succeeded and refreshed prefabs, controllers and the ignored bundle. Animation payload tests remain enforced. Geometry is UNCALIBRATED_USER_AUTHORIZED; this is local asset/test evidence, not in-game or performance acceptance.

Bundles remain regenerable ignored outputs. Run the sample-native Unity builder after checkout to refresh them. Existing unrelated MCP edits and memory.csv deletion are outside this pass.
