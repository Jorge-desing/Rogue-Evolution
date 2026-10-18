# Native Hierarchy, Visual Roots & Pivot Calibration

This reference documents the internal transform hierarchy of Rogue Command's native units, how `visualRootPath` works, and how to calibrate scale, pivots, and weapon firepoints.

---

## 1. How the Loader Replaces Visuals

In Rogue Command, gameplay logic (`EntityController`, weapons, pathfinding, health, balance, audio) is tightly coupled to native Unity prefabs. The Expansion Loader uses **non-destructive visual replacement**:

1. When a pack registers a unit with `nativeTemplateId` and `visualAssetName`, the loader clones the native template prefab.
2. It navigates to the transform specified by `visualRootPath` using `FindUniqueTransformPath()`.
3. It disables all native `Renderer` components under that subtree (`renderer.enabled = false`).
4. It instantiates the mod's visual prefab at `target`, setting:
   - `localPosition = Vector3.zero`
   - `localRotation = Quaternion.identity`
   - `localScale = Vector3.one`
5. All native controller scripts, colliders, audio sources, and minimap shapes remain intact on the cloned root.

```
Native Prefab (Cloned at Runtime):
[EntityController / BoxCollider / Rigidbody]  <-- Untouched by mod
  |-- MinimapShape                             <-- Untouched (displays on minimap)
  +-- [visualRootPath Node] (e.g., "Vehicle")
        |-- Native MeshRenderers               <-- Disabled (enabled = false)
        +-- RCL_Visual_<mod_unit_id>           <-- Your custom visual prefab instantiated here!
```

---

## 2. Verified Native Template Hierarchies

### A. `PCXBaseTank` (Template for Scout, Light & Medium Tanks)

Static asset inspection (`resources.assets`, PathID `95696`) confirmed:

```
PCXBaseTank
  |-- Transform (Root)
  |-- Rigidbody
  |-- BoxCollider (Size: ~2.4m wide x 1.6m high x 2.8m long)
  |-- AudioSource x2
  |-- EntityController (LocalId: 582674877)
  |-- MinimapShape
  +-- Vehicle (Depth 1, Transform PathId: 199580)  <-- visualRootPath: "Vehicle"
        +-- doubleCannonTireTank (Depth 2)
              |-- ROTHOR (Depth 3)                 <-- Native turret yaw rotation
              |     +-- rotHor (Depth 4)
              |           |-- body_topBackVan (Renderer)
              |           |-- chimney_medium (Renderer)
              |           +-- Firepoints (Muzzle flash emitters)
              |                 |-- first (Firepoint Left: X=-0.14, Y=1.01, Z=0.78)
              |                 +-- first (1) (Firepoint Right: X=+0.14, Y=1.01, Z=0.78)
              +-- PCXBaseTank (Depth 3)            <-- Native hull renderers
                    |-- body_frontTank
                    |-- tank_skirt
                    +-- track_wideSmoothSide
```

#### Calibration Rules for `PCXBaseTank` Overlays:
- **`visualRootPath`**: Use `"Vehicle"`.
- **Center of Hull**: Position the chassis center at `(0, 0, 0.45)`.
- **Ground Clearance**: Treads must contact `Y = 0.0`.
- **Twin Cannons Alignment**: If building a dual-barrel tank, place the barrels at `X = ±0.14` and height `Y ≈ 1.0` to match native firepoint trajectories.

---

### B. `BombBike` (Template for Fast Strike Bikes & Scout Cycles)

Static asset inspection (`resources.assets`) confirmed:

```
BombBike
  |-- Transform (Root)
  |-- BoxCollider (Size: ~1.2m wide x 1.4m high x 2.2m long)
  |-- EntityController
  |-- MinimapShape
  +-- GameObject (Depth 1)
        +-- BombBike (Depth 2)                     <-- visualRootPath: "GameObject/BombBike"
              |-- Chassis (Renderers)
              |-- Wheels (Renderers)
              +-- ExplosivePayload (Renderers)
```

#### Calibration Rules for `BombBike` Overlays:
- **`visualRootPath`**: Use `"GameObject/BombBike"`.
- **Wheelbase**: Rear axle at `Z ≈ -0.78`, front axle at `Z ≈ +0.75`.
- **Wheel Radius**: `R ≈ 0.38m` (tire diameter ~0.76m).
- **Ground Clearance**: Tire bottoms must touch `Y = 0.0` (`Axle center Y = 0.39m`).
- **Narrow Profile**: Overall vehicle width should not exceed `1.2m` to prevent clipping through tight pathfinding corridors.

---

### C. `ArtilleryTank` (Template for Heavy Siege Platforms)

- **`visualRootPath`**: `"Vehicle"`.
- **Footprint**: Large broad tracked base (`width ≈ 2.8m`, `length ≈ 3.6m`).
- **Cannon Elevation**: Long-bore barrel elevated at ~15°–25° to telegraph ballistic siege trajectory.
- **Rear Recoil Stance**: Visual stabilizers or spade legs planted at the rear.

---

### D. `GatlingWalker` (Template for Bipedal Mechs)

- **`visualRootPath`**: `"Vehicle"` or `"WalkerRoot"`.
- **Height**: ~2.5m–2.8m tall.
- **Weapon Hardpoints**: Twin underslung rotary barrels mounted at waist/arm level (`X ≈ ±0.8m`, `Y ≈ 1.2m`).

---

## 3. Scale & Bounding Box Checklist

Before finalizing any model, verify dimensions against the template bounds:

```
        +---------------+  ^
        |     HULL      |  | Height (Z in Blender, Y in Unity)
  +-----+---------------+-----+  |
  | TREAD               TREAD |  v
  +---------------------------+  Ground Plane (0.0)
  <--------------------------->
     Width (X in both engines)
```

1. **Ground Alignment**: Does the lowest vertex of the wheels/treads sit exactly on `Z = 0.0` in Blender?
2. **Width Bounds**: Does the vehicle width fit within the template's `BoxCollider` width?
3. **Length Bounds**: Does the vehicle front/rear stay within the template's collision extents?
4. **Camera Scale Test**: Zoom the Blender viewport to orthographic view (`Numpad 5` + `Numpad 7`). Does the model look proportional alongside neighboring units?

---

## 4. Canonical Sockets & Runtime Visual Director Integration

Models authored for Rogue Command hook into autonomous gameplay visuals via canonical empty transforms (`Socket_*`) discovered at runtime by `RuntimeVisualDirector`:

### A. Canonical Socket Catalog

| Canonical Socket Name | Intended Transform Location | Runtime Visual Director Behavior |
| :--- | :--- | :--- |
| `Socket_TurretPivot` / `Socket_TurretYaw` | Turret ring rotation center | Procedural azimuth tracking toward hostile target (`IsEnemy == true`) at 120°/s; jams with $+4.5^\circ$ tilt at $\le 25\%$ HP |
| `Socket_TurretPitch` | Weapon trunnion elevation pivot | Pitch elevation toward target elevation |
| `Socket_RecoilPrimary` | Base of main gun barrel | Rapid 0.22m backward recoil kick on shot, smoothed with spring-damper recovery |
| `Socket_MuzzlePrimary` | Tip of main gun barrel | 0.07s emissive muzzle star-spike flash on shot |
| `Socket_DamageSmoke` | Engine deck / exhaust grilles | Volumetric smoke particle emission at $\le 50\%$ HP |
| `Socket_DamageFire` | Engine deck / damaged hull | Flame particle emission + dynamic point light at $\le 25\%$ HP |
| `Socket_RadarMast` / `Socket_RadarDish` | Command mast / radar collar | Continuous rotation at true **30 RPM** ($180^\circ/\text{s}$) during match uptime |
| `Socket_Deploy_Unit` / `Socket_DeployRamp` | Factory rollout portal / bay door | Vertical sliding door opening during active unit production (`InProductionCount > 0`) |
| `Socket_Hardpoint_ERA` | Glacis / turret cheek mounts | Mounts modular ERA armor tiles; detonates on heavy impact ($\le 75\%$ HP) |
| `Socket_Hardpoint_APS` | Turret roof countermeasure pods | Mounts active protection mortars; burns out at $\le 25\%$ HP |
| `Socket_Hardpoint_Sponsons` | Flank sponson weapon mounts | Mounts auxiliary weapon sponsons |
| `Socket_Hardpoint_FuelDrums` | Rear chassis hull brackets | Mounts auxiliary fuel drums; jettisons at $\le 50\%$ HP |

### B. Targeting Hostility Invariant

Turret tracking in `RuntimeVisualDirector` explicitly queries `EntityController.IsEnemy(EntityController)` (token `0x06000844`). Sockets will never orient toward friendly allies, neutral terrain, or friendly waypoints.

