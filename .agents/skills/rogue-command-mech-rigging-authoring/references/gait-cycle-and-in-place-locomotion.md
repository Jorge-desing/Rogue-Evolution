# Gait Cycles and In-Place Locomotion for RTS Walkers

This reference explains the mechanical gait cycle, ground contact constraints, and the strict **In-Place Locomotion Invariant** required for Rogue Command walker units.

---

## 1. The In-Place Locomotion Invariant

In real-time strategy (RTS) engines like Rogue Command, **unit movement across the map is simulated by the gameplay engine (`EntityFlockingMovement`), not by animation root motion**.

```
+-----------------------------------------------------------------------------------+
|                        THE IN-PLACE LOCOMOTION INVARIANT                          |
+-----------------------------------------------------------------------------------+
|  - Root bone (`Bone_Root`) position delta MUST BE ZERO:                           |
|      ΔX = 0.0, ΔY = 0.0, ΔZ = 0.0 across all frames of the animation clip.       |
|  - Pelvis/Chassis bones move locally relative to the stationary root.              |
|  - Feet swing forward and return to the rear relative to the center of mass.      |
|  - If Root Motion is enabled, the unit will move twice as fast (double-stepping)  |
|    or pop backwards at the end of each animation loop!                           |
+-----------------------------------------------------------------------------------+
```

---

## 2. Bipedal Mechanical Gait Cycle (Digitigrade & Plantigrade)

A standard mechanical walk cycle consists of **40 frames at 30 FPS** (1.33 seconds per full two-step cycle).

### The 4 Phases per Step (Left Leg shown, Right Leg is $180^\circ$ out of phase at Frame 21):

```
Frame 1: Contact (Heel Strike)
  - Left foot reaches maximum forward extension (+Y in Blender).
  - Right foot pushes off ground at maximum rear extension (-Y).
  - Pelvis height is at neutral baseline.

Frame 10: Down (Weight Absorption)
  - Left foot is flat on ground bearing 100% of chassis weight.
  - Left knee flexes, shock absorbers compress.
  - Pelvis dips to lowest height (-0.04m) and sways slightly over left leg (+X).

Frame 20: Passing (Mid-Stride)
  - Right leg swings forward past the supporting left leg.
  - Right foot reaches maximum clearance height above ground (+0.12m).
  - Pelvis rises to neutral height.

Frame 30: High Point (Push-Off)
  - Left leg extends fully pushing chassis upward.
  - Pelvis reaches highest point (+0.03m).
  - Right leg prepares for contact.
```

### Keyframe Schedule for 40-Frame Bipedal Walk Cycle:

| Phase | Frame (Left Leg) | Frame (Right Leg) | Foot Z (Height) | Foot Y (Stride) | Pelvis Z |
|---|---|---|---|---|---|
| **Contact** | 1 | 21 | 0.00m | +0.45m (forward) | Neutral |
| **Down** | 10 | 30 | 0.00m | +0.20m (under) | -0.04m (compression) |
| **Passing** | 20 | 40 (1) | +0.12m (lift) | 0.00m (passing) | Neutral |
| **High Point** | 30 | 10 | +0.06m (drop) | -0.35m (rear) | +0.03m (extension) |

---

## 3. Quadrupedal Gait Cycles (4-Legged Heavy Walkers)

For heavy 4-legged walkers (e.g. siege mechs, spider tanks), two gait styles are recommended:

### A. Heavy Trot Gait (Fast & Aggressive, 30-40 frames)
- Diagonal leg pairs move simultaneously:
  - Pair 1: Front-Left (`FL`) and Rear-Right (`RR`).
  - Pair 2: Front-Right (`FR`) and Rear-Left (`RL`).
- Frame 1-20: Pair 1 in swing phase, Pair 2 in stance phase.
- Frame 21-40: Pair 2 in swing phase, Pair 1 in stance phase.

### B. Stable Creep/Crawl Gait (Massive Super-Heavy Mechs, 60 frames)
- Only one leg lifts at any instant, ensuring a 3-point tripod base of support:
  1. Front-Left (`FL`) swings (Frames 1-15)
  2. Rear-Right (`RR`) swings (Frames 16-30)
  3. Front-Right (`FR`) swings (Frames 31-45)
  4. Rear-Left (`RL`) swings (Frames 46-60)
- Chassis experiences minimal roll/pitch, giving an impression of immense tonnage.

---

## 4. Ground Contact Plane & Zero-Penetration

In Blender:
- The ground contact plane is **$Z = 0.0$** (in Unity, this maps to $Y = 0.0$).
- Feet and claws must never penetrate below $Z < 0.0$ during the stance phase.
- The lowest surface of `FootBase`, `ToeCenter`, and `HeelClaw` should rest exactly at $Z = 0.02\text{m} - 0.04\text{m}$ to allow for ground clearance and prevent z-fighting with the terrain mesh.

---

## 5. Standard Baked NLA Action Set

Every modded walker model should export an FBX containing these four NLA Actions:

1. **`Action_Idle` (30 frames, Looping):**
   - Shallow chassis breathing with slow-in/out shoulders at f8/f22 and a peak at f15.
   - Legs remain firmly planted with zero foot movement.
2. **`Action_Walk` (or `Action_Drive`, 40 frames, Looping):**
   - In-place gait cycle with pelvis counter-sway and foot lift arcs.
3. **`Action_FireRecoil` (role-specific end frame: light/medium 15, sam/walker 18, heavy 21, Non-Looping):**
   - Monotone phases at f1 neutral, f3 anticipation, f5 impact, f7 follow-through, f11 recovery, and f15 neutral/hold.
   - Body pitch and recoil travel key together; a barrel-only impulse is insufficient.
4. **`Action_Death` (default 35 frames, verified Bastion override 45 frames, Non-Looping / Clamp):**
   - Slow collapse starting with a `12%` settle at f8, half collapse at f16, and full collapse by f28 with hold to the end.
