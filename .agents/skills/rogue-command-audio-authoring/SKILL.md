---
name: rogue-command-audio-authoring
description: "Author, calibrate, and validate 3D spatial SFX, weapon audio, engine loops, and AudioSources for Rogue Command."
---

# Rogue Command Audio Authoring

Use this skill when designing, authoring, importing, or reviewing audio sound effects (SFX), weapon sounds, engine loops, particle impacts, and `AudioSource` components for Rogue Command vehicles and units.

Read `AGENTS.md`, `references/native-audio-integration-points.md`, `references/unity-3d-audio-spatialization-guide.md`, and `references/audio-asset-specifications.md` before importing audio into Unity or AssetBundles.

---

## 1. When to Use This Skill

- Authoring weapon firing sounds (cannons, rapid autocannons, laser beams, missile launches).
- Adding vehicle engine and tread movement audio triggered by `EntityFlockingMovement`.
- Attaching randomized impact, explosion, or debris sound effects via `ParticleSoundPlayer`.
- Auditing raw `.wav` files or compiled AssetBundles with `validate_audio_assets.py` to catch stereo-in-3D errors or improper distance rolloffs.
- Configuring unit selection and acknowledgement dialogue clips via `SelectionSoundPlayer`.

---

## 2. Core Audio Invariants (Rogue Command 1.0.9)

1. **Mono (1-Channel) Requirement for 3D Positional Audio**:
   - Unity's 3D spatial audio engine **only works on Mono audio clips**.
   - If an `AudioClip` has 2 channels (Stereo), Unity disables 3D spatial attenuation and panning, causing distant sounds to play loud and flat across the player's headphones.
   - **Rule**: All weapon, engine, explosion, and world sound effects **must be Mono**. Reserve Stereo exclusively for 2D UI clicks and non-diegetic music.

2. **Native Weapon Audio Hook (`ShootProjectile`)**:
   - Assign the weapon `AudioSource` to `ShootProjectile.shotSoundAudioSource`.
   - The native engine automatically triggers `.Play()` on every shot with procedural pitch modulation ($\pm 5\%$) to eliminate machine-gun fatigue.

3. **Native Engine Locomotion Sound (`EntityFlockingMovement`)**:
   - Assign a looping engine/track sound to `EntityFlockingMovement._movementSoundAudioSource`.
   - The game automatically plays the sound with randomized pitch ($\pm 4\%$) when moving, and calls `.Stop()` when halting.

4. **Calibrated 3D AudioSource Parameters**:
   - `spatialBlend = 1.0` (100% 3D spatialization).
   - `rolloffMode = Logarithmic`.
   - `minDistance = 3.0m`, `maxDistance = 60.0m – 80.0m` (matches the tactical camera height and screen radius).
   - `dopplerLevel = 0.0` (critical in RTS: non-zero doppler warbles when the player rapidly pans the camera).

---

## 3. Audio Validation Tool

Use `tools/validate_audio_assets.py` (mirrored in `scripts/validate_audio_assets.py` within this skill) to audit audio assets:

```powershell
# Audit all audio clips and AudioSources in a content pack
.venv\Scripts\python.exe tools/validate_audio_assets.py --pack content/packs/sample-native-expansion

# Audit a folder of raw uncompressed WAV sound effects
.venv\Scripts\python.exe tools/validate_audio_assets.py --audio-dir path/to/raw_sfx/

# Audit a compiled AssetBundle
.venv\Scripts\python.exe tools/validate_audio_assets.py --bundle content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle

# Audit a Unity prefab YAML file
.venv\Scripts\python.exe tools/validate_audio_assets.py --prefab unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/ScoutTankVisual.prefab
```

---

## 4. Authoring Workflow

1. **Design & Master SFX**: Export uncompressed WAV files at 44.1 kHz / 16-bit **Mono**. Apply high-pass filter at 45 Hz – 55 Hz to prevent sub-bass clutter.
2. **Import to Unity**: Apply importer presets from `references/audio-asset-specifications.md` (`Decompress On Load` + `ADPCM` for fast weapons; `Compressed In Memory` + `Vorbis` for engine loops).
3. **Configure AudioSource**: Place `AudioSource` on the model socket (`Socket_MuzzlePrimary` or `Socket_GroundCenter`) with `spatialBlend = 1.0`, `minDistance = 3.0`, `maxDistance = 60.0`, `dopplerLevel = 0.0`.
4. **Hook to Native Components**: Wire `shotSoundAudioSource` on `ShootProjectile`, `_movementSoundAudioSource` on `EntityFlockingMovement`, or use `ParticleSoundPlayer` for particle impacts.
5. **Run Validator**: Execute `validate_audio_assets.py` before building AssetBundles.
