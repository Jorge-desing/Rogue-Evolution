# Native Audio Integration Points in Rogue Command

This reference documents the reverse-engineered audio components and hooks in Rogue Command 1.0.9 (`Assembly-CSharp.dll`). Authoring visual and entity prefabs to match these native patterns enables audio playback with procedural pitch modulation **without requiring custom C# gameplay scripts**.

---

## 1. Weapon Firing Audio (`ShootProjectile`)

In `ShootProjectile.cs`, weapon discharge sound is triggered directly from a serialized `AudioSource`:

```csharp
// ShootProjectile.ShootFromFirepoint(int firePointIndex)
if ((bool)this.shotSoundAudioSource && this.shotSoundAudioSource.enabled)
{
    // Native procedural pitch jitter prevents machine-gun acoustic fatigue
    this.shotSoundAudioSource.pitch = 1f + UnityEngine.Random.Range(-0.05f, 0.05f);
    this.shotSoundAudioSource.Play();
}
```

### Hierarchy Recipe for Weapons
```
VehiclePrefab
└── Socket_TurretPivot
    └── Socket_TurretPitch
        └── Socket_MuzzlePrimary
            ├── ShootProjectile (Component)
            │   └── shotSoundAudioSource -> (References AudioSource below)
            └── WeaponAudioSource (GameObject with AudioSource Component)
```
- **Rule**: Set `playOnAwake = false` and `loop = false`.
- **Rule**: Clip must be **Mono (1 channel)** with `spatialBlend = 1.0`.

---

## 2. Vehicle Movement & Engine Loops (`EntityFlockingMovement`)

Vehicle movement audio is driven directly by the grid locomotion system in `EntityFlockingMovement.cs`:

```csharp
// EntityFlockingMovement.PlayMovementSound()
if ((bool)this._movementSoundAudioSource && !this._movementSoundAudioSource.isPlaying)
{
    this._movementSoundAudioSource.pitch = 1f + UnityEngine.Random.Range(-0.04f, 0.04f);
    this._movementSoundAudioSource.Play();
}

// EntityFlockingMovement.StopMovementSound()
if ((bool)this._movementSoundAudioSource && this._movementSoundAudioSource.isPlaying)
{
    this._movementSoundAudioSource.Stop();
}
```

### Engine Sound Configuration
- **AudioSource Settings**:
  - `loop = true`.
  - `playOnAwake = false`.
  - `spatialBlend = 1.0` (3D).
  - `minDistance = 3.0`, `maxDistance = 60.0`.
  - `rolloffMode = Logarithmic`.
- **AudioClip**: Seamless looping engine rumble or track clatter ($2.0\text{s} - 6.0\text{s}$ duration), zero-crossing loop points.

---

## 3. Particle-Triggered Audio (`ParticleSoundPlayer`)

For transient visual events (explosions, ricochets, shell casing ejections, debris impacts), Rogue Command provides `ParticleSoundPlayer`:

```csharp
public class ParticleSoundPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    public List<AudioClip> audioClips;
    public Vector2 pitchRange; // e.g. (0.92f, 1.08f)
    private ParticleSystem _particleSystem;
}
```

### Setup Recipe for Explosions & Impacts
1. Attach `ParticleSoundPlayer` to the GameObject carrying the `ParticleSystem`.
2. Add a child `AudioSource` with `spatialBlend = 1.0`.
3. Populate `audioClips` with 3 to 5 distinct sound variations (Mono WAV/OGG).
4. Set `pitchRange = (0.92, 1.08)`. The native component randomly selects a clip and modulates pitch each time the particle system emits.

---

## 4. Unit Selection & Acknowledgement (`SelectionSoundPlayer`)

Rogue Command manages unit selection dialogue and mechanical chirps via `SelectionSoundPlayer`:

```csharp
public class SelectionSoundPlayer : MonoBehaviour
{
    public AudioSource sourceA;
    public AudioSource sourceB;
    public float fadeDuration;
    private Dictionary<string, AudioClip> _clips;

    public void RegisterSelectionSound(string entityId, AudioClip clip);
    public void PlaySelectionSound(string entityId);
}
```

- When the player selects a unit in tactical combat, `PlaySelectionSound` crossfades between `sourceA` and `sourceB` to prevent clicking or sound truncation.
- Mod units can register custom unit selection clips by namespaced entity ID.

---

## 5. Entity Event Sounds (`PlaySound` / `IEntityAction`)

Rogue Command includes `PlaySound` as an executable `IEntityAction` implementing the game's event action chain:

- `AudioSource audioSource`: Target emitter to trigger.
- `string id`: Identifies the sound action in the entity state machine.
- Used in native event triggers: on taking damage, on shield depletion, on unit death, on skill activation.
