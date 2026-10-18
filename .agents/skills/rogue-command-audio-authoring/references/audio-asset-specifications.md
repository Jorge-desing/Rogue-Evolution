# Audio Asset Specifications & Importer Presets for Rogue Command

This reference defines file standards, audio processing rules, and Unity AudioImporter configuration presets for Rogue Command AssetBundles.

---

## 1. Master Audio File Standards

| Asset Category | Target Format | Channels | Sample Rate | Target Duration | Headroom / Peak |
|:---|:---:|:---:|:---:|:---:|:---:|
| **Weapon Shot (Cannon, Shell)** | WAV / OGG | **1 (Mono)** | 44.1 kHz / 48 kHz | $0.8\text{s} – 1.8\text{s}$ | -1.0 dBFS Peak |
| **Rapid Fire (Gatling, Laser)** | WAV / OGG | **1 (Mono)** | 44.1 kHz / 48 kHz | $0.15\text{s} – 0.35\text{s}$ | -1.5 dBFS Peak |
| **Engine Movement Loop** | WAV / OGG | **1 (Mono)** | 44.1 kHz / 48 kHz | $2.5\text{s} – 5.0\text{s}$ (Seamless) | -3.0 dBFS RMS |
| **Explosion / Detonation** | WAV / OGG | **1 (Mono)** | 44.1 kHz / 48 kHz | $1.2\text{s} – 2.5\text{s}$ | -0.5 dBFS Peak |
| **Impact / Ricochet** | WAV / OGG | **1 (Mono)** | 44.1 kHz / 48 kHz | $0.2\text{s} – 0.5\text{s}$ | -2.0 dBFS Peak |
| **Unit Selection Voice / Chirp** | WAV / OGG | **1 (Mono)** | 44.1 kHz / 48 kHz | $0.4\text{s} – 1.2\text{s}$ | -1.0 dBFS Peak |
| **2D UI / Menu Notification** | WAV / OGG | 2 (Stereo) | 48 kHz | $0.2\text{s} – 0.8\text{s}$ | -2.0 dBFS Peak |

---

## 2. Unity AudioImporter Presets (Unity 2021.3.45f2)

When importing audio assets into the Unity authoring project (`unity/RogueCommandAssetAuthoring`):

### Preset A: Rapid Weapon SFX (Gatling, Cannons, Lasers)
- **Force To Mono**: `true` (downmixes stereo automatically if not authored mono).
- **Load Type**: `Decompress On Load` (avoids runtime decompression latency during rapid firing).
- **Compression Format**: `ADPCM` (lightweight fixed decompression with zero CPU overhead).
- **Preload Audio Data**: `true`.

### Preset B: Engine Loops & Ambient Mechanical Hum
- **Force To Mono**: `true`.
- **Load Type**: `Compressed In Memory`.
- **Compression Format**: `Vorbis` (Quality: 70%).
- **Preload Audio Data**: `true`.

### Preset C: Unit Voice Lines & Long Detonations ($> 6\text{s}$)
- **Force To Mono**: `true`.
- **Load Type**: `Compressed In Memory` or `Streaming`.
- **Compression Format**: `Vorbis` (Quality: 75%).
- **Preload Audio Data**: `false` (streams from disk when triggered).

---

## 3. Acoustic Processing & Mixing Rules

1. **High-Pass Filtering (Low Cut)**:
   - Apply a high-pass filter at **45 Hz – 55 Hz** (18 dB/octave) to all weapon shots and engine loops.
   - Sub-bass rumble below 40 Hz creates acoustic mud when 30+ vehicles fire simultaneously.
2. **Frequency Slotting**:
   - Heavy Artillery: Meat in the low-mid punch ($80\text{ Hz} – 180\text{ Hz}$), rolled off above $8\text{ kHz}$.
   - Autocannons & Machine Guns: Crisp mechanical transient ($1.5\text{ kHz} – 4.5\text{ kHz}$).
   - Energy / Electric Arcs: High-frequency sizzle ($4.0\text{ kHz} – 12.0\text{ kHz}$) with fast decaying resonance.
3. **Loop Point Smoothing**:
   - Movement loops for `EntityFlockingMovement._movementSoundAudioSource` must have matched zero-crossings at start and end frames with a 10 ms crossfade to prevent audible click artifacts during indefinite movement cycles.
