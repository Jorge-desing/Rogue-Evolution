# Animation review and Manim preview

This reference combines the target-medium boundary from `/12-principles-of-animation` with the narrative, visual hierarchy, and render discipline from `/manim-video`. It is an optional communication layer for Rogue Command animation work; Blender, Unity, AssetBundle inspection, and a Steam run remain the implementation evidence.

## 1. Start with a motion question

Write the question before authoring or previewing. Good questions are concrete:

- Can an RTS player distinguish brace, impact, and recovery in `Action_FireRecoil`?
- Do the body and weapon curves agree on the direction of force?
- Does `Action_Walk` show contact, passing, lift, and weight transfer while `Bone_Root` stays in place?
- Does an imported controller leave the rig in a canonical state after a fire trigger?

Do not begin with “make it more animated.” Record the unit role, camera distance, mass impression, focal event, and the transform owner for each moving part.

## 2. Storyboard the action

Use an ignored directory such as `artifacts/animation-previews/<slug>/` and create `plan.md` before `script.py`:

```markdown
# <unit> — <action> preview

## Motion question
<one falsifiable question>

## Narrative arc
1. Neutral pose and silhouette
2. Anticipation / brace
3. Contact or impact
4. Follow-through / overlap
5. Recovery and invariant

## Abstract objects
- body: hull or pelvis
- weapon: turret, mount, barrel
- invariant: Bone_Root does not translate
- guides: ground, weapon axis, phase timeline

## Phase map
| Phase | Frames or seconds | Body | Weapon | Focal cue |
|---|---:|---|---|---|
| neutral | ... | ... | ... | ... |
| anticipation | ... | ... | ... | ... |
| impact | ... | ... | ... | ... |
| follow-through | ... | ... | ... | ... |
| recovery | ... | ... | ... | ... |

## Evidence boundary
- static preview: what it communicates
- local validation: which files/checks prove curves or state graph
- Steam runtime: observed state/time/transforms, or explicitly pending
```

The phase map is not a replacement for the actual F-curves. It is a compact hypothesis that makes missing phases visible before rendering.

## 3. Abstract Manim scene contract

A preview uses simple shapes instead of game assets:

- a rounded rectangle or polygon for `Bone_Hull`/`Bone_Pelvis`;
- a smaller rectangle or arc for `Bone_TurretPitch`/the mount;
- a line or rectangle for `Bone_BarrelRecoil`;
- a fixed ground line and a root marker;
- a low-opacity axis and timeline;
- one phase label at a time.

Keep the body and weapon as separate objects so the preview can show their phase relationship. Highlight only the current focal event. Use full opacity for the primary motion, roughly `0.4` for annotations, and roughly `0.15` for guides. Add a pause after each phase and a longer pause after the recovery/invariant reveal.

A minimal scene can be written with standard Manim primitives:

```python
from manim import *

PRIMARY = "#58C4DD"
WEAPON = "#FFCC66"
GUIDE = "#FFFFFF"


class FireRecoilPreview(Scene):
    def construct(self):
        self.camera.background_color = "#1C1C1C"
        body = RoundedRectangle(width=3.2, height=1.3, corner_radius=0.18, color=PRIMARY)
        turret = Rectangle(width=1.0, height=0.55, color=WEAPON).next_to(body, UP, buff=0)
        barrel = Line(turret.get_right(), turret.get_right() + RIGHT * 1.6, color=WEAPON)
        root = Dot(body.get_center(), color=GUIDE)
        ground = Line(LEFT * 5 + DOWN * 1.2, RIGHT * 5 + DOWN * 1.2, color=GUIDE, stroke_opacity=0.15)
        phase = Text("neutral", font="Menlo", font_size=24, color=GUIDE).to_edge(UP, buff=0.5)
        rig = VGroup(body, turret, barrel, root)

        self.add(ground)
        self.play(FadeIn(rig), Write(phase), run_time=1.0)
        self.wait(0.8)

        self.play(phase.animate.become(Text("anticipation", font="Menlo", font_size=24, color=GUIDE).to_edge(UP, buff=0.5)),
                  body.animate.shift(UP * 0.08), barrel.animate.shift(LEFT * 0.04), run_time=0.25)
        self.wait(0.35)

        self.play(phase.animate.become(Text("impact", font="Menlo", font_size=24, color=GUIDE).to_edge(UP, buff=0.5)),
                  body.animate.shift(DOWN * 0.18), barrel.animate.shift(LEFT * 0.26), run_time=0.12)
        self.wait(0.25)

        self.play(phase.animate.become(Text("follow-through", font="Menlo", font_size=24, color=GUIDE).to_edge(UP, buff=0.5)),
                  barrel.animate.shift(RIGHT * 0.08), run_time=0.20)
        self.wait(0.35)

        self.play(phase.animate.become(Text("recovery / Bone_Root fixed", font="Menlo", font_size=24, color=GUIDE).to_edge(UP, buff=0.5)),
                  body.animate.shift(UP * 0.10), barrel.animate.shift(RIGHT * 0.22), run_time=0.30)
        self.wait(1.5)
```

This code communicates phase ordering only. It does not reproduce Unity coordinate axes, FBX interpolation, rigid weights, Animator transitions, muzzle VFX, or gameplay movement. Keep those claims in the corresponding local/runtime evidence records.

## 4. Rogue Command project contract

A project preview should expose the contracts that a generic Manim scene would hide. Keep the numeric values in a side panel and use frame labels rather than inventing seconds when the source clip's imported FPS has not been measured.

### `Action_FireRecoil`

Use the source-authored key map from `tools/RogueModelKit.py`:

| Frame | Phase | Body Y (m) | Barrel Y (m) | Body pitch (deg) | Turret pitch (deg) |
|---:|---|---:|---:|---:|---:|
| 1 | Neutral | 0.000 | 0.000 | 0.00 | 0.00 |
| 2 | Brace | +0.008 | +0.035 | +0.25 | +0.35 |
| 3 | Impact | -0.024 | -0.220 | -1.40 | -0.90 |
| 5 | Follow-through | -0.010 | -0.080 | +0.45 | +0.25 |
| 9 | Settle | -0.002 | -0.018 | +0.12 | +0.08 |
| 12 | Recovery | 0.000 | 0.000 | 0.00 | 0.00 |
| 15 | Neutral | 0.000 | 0.000 | 0.00 | 0.00 |

The body binding is `Bone_Hull` for vehicles or `Bone_Pelvis` for bipedal units; the weapon chain is `Bone_BarrelRecoil` plus `Bone_TurretPitch` when present. A preview may exaggerate displacement for readability, but must label the display scale and preserve the source-local numbers in the panel. Never move `Bone_Root`.

### `Action_Walk` and Animator state graph

Use frame labels `1, 10, 20, 30, 40` for contact/down/passing/push-off/contact. Show the canonical walker tokens `FL`, `ML`, `RL`, `FR`, `MR`, `RR` when previewing hexapod coverage, and state explicitly that `EntityFlockingMovement` owns world translation. Show the Unity parameters and routes exactly: `RCL_IsMoving` → `Action_Walk`/`Action_Drive`, `RCL_Fire` → `Action_FireRecoil`, and `RCL_Death` → `Action_Death`; FireRecoil returns to movement before its idle fallback. Include the Generic Avatar, exactly-one-Animator, and `applyRootMotion=false` constraints in a contract scene or annotation.

Do not claim that the preview is a serialized Unity curve audit. Pair it with actual FBX/Unity inspection and, for runtime claims, an observed Animator state, normalized time, and transform series in a Steam run.

## 5. Render and review

Check Manim CE before rendering. A preview is optional; do not install dependencies automatically for a documentation or authoring task.

```text
terminal(command="manim --version", timeout=60)
terminal(command="manim -ql artifacts/animation-previews/<slug>/script.py FireRecoilPreview", timeout=300)
terminal(command="manim -ql --format=png -s artifacts/animation-previews/<slug>/script.py FireRecoilPreview", timeout=300)
```

Review the still before producing a video:

1. Is the body/weapon phase relationship readable without the labels?
2. Is there one focal event rather than simultaneous competing motion?
3. Does the root marker remain fixed while body compression is local?
4. Are guides subordinate and is there enough time to absorb each reveal?
5. Does the preview communicate a question that the real curves or runtime probe can answer?

Use `-qh` only for a requested final explanatory video. If several scenes are rendered, stitch only the verified clips with an ignored `concat.txt` and `ffmpeg`; never commit generated media by default.

## 5. Twelve-principle audit rubric

Record a finding when a principle is not met; do not force a pass merely because a preview renders.

| Rule | Question for Rogue Command |
|---|---|
| Squash/stretch | Is deformation limited to compliant mechanics rather than rigid armor? |
| Anticipation | Is there a visible brace/load before impact? |
| Staging | Is the main combat cue dominant at RTS scale? |
| Pose-to-pose / continuous | Is each transform written by exactly one appropriate owner? |
| Follow-through / overlap | Do subordinate parts settle after the primary impulse? |
| Slow in / slow out | Are acceleration and recovery shaped without falsifying constant RPM motion? |
| Arcs | Do pivots and axes produce the intended barrel/turret path? |
| Secondary action | Does antenna, dust, audio, or debris support rather than obscure intent? |
| Timing | Do role, mass, and phase durations agree? |
| Exaggeration | Is the displacement large enough to read but small enough to preserve mass and bounds? |
| Solid form | Are weights, sockets, colliders, bounds, contacts, and Generic Avatar intact? |
| Appeal / clarity | Can the viewer identify unit role, heading, weapon intent, and state? |

For a real acceptance result, pair this table with:

- actual Blender/FBX F-curves and root-motion checks;
- Unity prefab/controller and serialized bundle audits;
- a Steam observation of the intended Animator state, normalized time, and changing transforms.

## 6. Evidence and cleanup

Classify the output explicitly:

- **Static inspection:** storyboard, Manim frames, source F-curves, controller text, or bundle serialization.
- **Local build/validation:** Blender export, Unity batch build, pack validation, or test output.
- **In-game Steam runtime:** the game launched through Steam with the matching deployed build, observed state/time, and transform series.

A Manim preview can make a missing anticipation or competing focal point obvious, but it cannot prove that Unity imported the curves, that `RuntimeVisualDirector` selected the state, or that a live unit moved. Remove temporary probes and keep generated files under `artifacts/animation-previews/`; do not leave scripts in `Assets/Editor/`.
