# Native Hierarchy and Pivots

This addendum records the canonical socket and runtime visual-director target contract supplied for Rogue Command's Unity authoring project. The runtime implementation has known gaps listed below; the target contract must not be reported as fully implemented until they are closed and checked in a Steam run.

## 4. Canonical Sockets & Runtime Visual Director Integration

- Treat the catalogued `Socket_*` names as exact identifiers. The visual director should resolve those names directly and must not substitute a similarly named mesh or infer a socket from a display label. Current source still uses substring matching for legacy turret, radar, deploy, and hardpoint discovery; these paths do not yet meet this rule.
- `Socket_Antenna` is the pivot parent for visible antenna meshes. Thruster vector sockets use the authored left/right identifiers `Socket_ThrusterVector_L` and `Socket_ThrusterVector_R`; preserve their authored local rotations.
- A radar mast calibrated to 30 RPM must rotate at 180 degrees per second (`RPM * 6`).
- Before turret tracking commits an angle, verify the target is active in the hierarchy, not destroyed, and hostile by calling `EntityController.IsEnemy(candidate)`. Failure to resolve or invoke that check must fail closed. Current source checks destruction/liveness and `IsEnemy`, but does not yet check `gameObject.activeInHierarchy`.
- Unity import, prefab construction, and a local plugin build do not establish in-game animation or targeting behavior; record Steam-run evidence separately.
