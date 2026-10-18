# Native Hierarchy and Pivots

This addendum records the canonical socket and runtime visual-director target contract supplied for Rogue Command's Unity authoring project. Static source inspection now enforces the socket and hostility rules below; runtime animation and targeting still need a Steam run.

## 4. Canonical Sockets & Runtime Visual Director Integration

- Treat the catalogued `Socket_*` names as exact identifiers. The visual director resolves names through `RogueCommandSocketCatalog`; it accepts exact canonical names and legacy `<model>_Socket_<canonical-name>` suffixes only. It does not discover turret, radar, deploy, or hardpoint sockets by arbitrary substring matching, or substitute a similarly named mesh or display label.
- If a prefab supplies multiple distinct transforms for one behavior role, that role is disabled instead of selecting whichever transform appears first in hierarchy traversal. This applies across aliases such as `Socket_TurretPivot`/`Socket_TurretYaw`, radar mast/dish, deployment aliases, hardpoints, and attachment groups; unrelated roles remain available. An ambiguous detached attachment group never falls back to its physical socket, while a missing detached group may use that socket for legacy prefabs.
- `Socket_Antenna` is the pivot parent for visible antenna meshes. Thruster vector sockets use the authored left/right identifiers `Socket_ThrusterVector_L` and `Socket_ThrusterVector_R`; preserve their authored local rotations.
- A radar mast calibrated to 30 RPM must rotate at 180 degrees per second (`RPM * 6`).
- Before turret tracking commits an angle, verify the target is active in the hierarchy, not destroyed, and hostile by calling the exact `EntityController.IsEnemy(EntityController)` signature. Failure to resolve or invoke that check must fail closed. Static source now checks `gameObject.activeInHierarchy`, destruction/liveness, and `IsEnemy`.
- Unity import, prefab construction, and a local plugin build do not establish in-game animation or targeting behavior; record Steam-run evidence separately.
