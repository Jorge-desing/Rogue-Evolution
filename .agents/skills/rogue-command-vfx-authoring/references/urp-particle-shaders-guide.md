# URP Particle Shaders & Material Pipeline in Rogue Command

This reference explains shader selection, material configuration, and render pipeline compatibility for visual effects in Rogue Command (Unity 2021.3.45f2 / Universal Render Pipeline).

---

## 1. Verified Native URP Shaders

Static inspection of `Rogue Command_Data/*.assets` confirms the presence and runtime support of the following particle shaders:

| Shader Name | Performance Cost | Primary Use Case | Supported Blend Modes |
|:---|:---:|:---|:---|
| `Universal Render Pipeline/Particles/Unlit` | Low (Recommended) | Muzzle flashes, sparks, lasers, plasma arcs, stylized smoke. | Additive, Alpha Blended, Premultiply |
| `Universal Render Pipeline/Particles/Simple Lit` | Medium | Realistic smoke clouds, dust plumes receiving scene directional light. | Alpha Blended |
| `Universal Render Pipeline/Particles/Lit` | High (Use sparingly) | Heavy debris meshes, large fluid splashes with specular highlights. | Opaque, Alpha Blended |
| `Legacy Shaders/Particles/Additive (Soft)` | Low (Fallback) | Legacy glow quads, soft fireballs. | Additive |
| `Mobile/Particles/Alpha Blended` | Very Low (Fallback) | Lightweight background smoke puffs. | Alpha Blended |

> [!IMPORTANT]
> Standard built-in pipeline shaders (such as `Particles/Standard Surface` or `Standard`) **will render magenta/pink** in Rogue Command's URP environment. Always use `Universal Render Pipeline/Particles/*` or mobile particle shaders.

---

## 2. Material Configuration Guidelines

When authoring materials for particle emitters in the Unity authoring project (`unity/RogueCommandAssetAuthoring`):

### Surface and Blending Modes
- **Additive Blending (`_BlendMode = 1` / Additive)**:
  - Best for: Muzzle flashes, laser bolts, electrical sparks, fire cores.
  - Transparent regions do not occlude geometry behind them; colors add directly to the frame buffer.
  - Does not cast or receive shadows.
- **Alpha Blending (`_BlendMode = 0` / Alpha)**:
  - Best for: Engine smoke, tyre dust, explosions after flash fade.
  - Smooth opacity transitions using alpha channel of particle texture and vertex color.
- **Premultiply Blending**:
  - Best for: Explosions that transition smoothly from glowing incandescent core (additive) to thick black smoke (alpha).

### Depth and Soft Particle Settings
To prevent harsh clipping lines where particles intersect the ground terrain or vehicle hulls:
- **Enable Soft Particles**: Set `_SoftParticlesEnabled = 1.0f`.
- **Near Fade Distance**: `0.0f` to `0.2f`.
- **Far Fade Distance**: `0.8f` to `1.5f`.
- **Camera Fading**: Enable `_CameraFadingEnabled = 1.0f` with `_CameraFarFadeDistance = 3.0f` so zooming in the tactical camera does not clip massive particle billboards directly across the screen.

---

## 3. Performance & AssetBundle Self-Containment

### GPU Instancing on Particle Systems
In dense RTS skirmishes with dozens of firing units, draw calls must be controlled:
1. On `ParticleSystemRenderer`:
   - Set `Render Mode = Billboard` (or `Mesh` for debris).
   - Check **Enable GPU Instancing** (`enableGPUInstancing = true`).
2. On Particle Materials:
   - Check **Enable GPU Instancing** on the material inspector.
   - URP's SRP batcher will batch identical particle materials across multiple vehicle instances into a single instanced draw call.

### Texture Atlasing
- Pack common particle sprites (flash, round smoke puff, spark streak, shockwave ring) into a single $512\times 512$ or $1024\times 1024$ texture atlas.
- Configure the `Texture Sheet Animation` module on the `ParticleSystem` (e.g. $2\times 2$ or $4\times 4$ grid) to select tiles by lifetime or random index.
- Sharing one atlas material across muzzle flashes, smoke, and debris enables full draw call batching across the entire vehicle formation.

### Particle Count Safeguards
- Muzzle Flashes: $\le 15$ particles per shot, lifetime $\le 0.12\text{s}$.
- Damage Smoke: $\le 40$ particles max, emission rate $\le 15/\text{s}$.
- Damage Fire: $\le 30$ particles max, emission rate $\le 20/\text{s}$.
- Explosions: $\le 60$ particles per burst.
- Never exceed 250 max particles per individual vehicle.
