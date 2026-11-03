"""Generate the tileable RCL_SteelMicroDetail_Normal.png used by VehiclePBR_VertexWear _DetailBumpMap.

Deterministic (seeded), seamlessly tileable (FFT-periodic) rolled-steel / cast-grain micro relief.
Output is a tangent-space OpenGL-convention normal map; Unity imports it as NormalMap.
"""

from pathlib import Path

import numpy as np
from PIL import Image

SIZE = 256
SEED = 20261103
OUT = (
    Path(__file__).resolve().parents[1]
    / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "RogueCommandExpansionSample"
    / "Textures" / "Detail" / "RCL_SteelMicroDetail_Normal.png"
)


def periodic_noise(rng: np.random.Generator, power: float, aniso: float) -> np.ndarray:
    """Band-limited periodic noise; aniso > 1 stretches grain along X (rolled-steel streaks)."""
    fx = np.fft.fftfreq(SIZE)[None, :] * aniso
    fy = np.fft.fftfreq(SIZE)[:, None]
    radius = np.sqrt(fx * fx + fy * fy)
    radius[0, 0] = 1.0
    spectrum = (rng.normal(size=(SIZE, SIZE)) + 1j * rng.normal(size=(SIZE, SIZE))) / radius ** power
    spectrum[0, 0] = 0.0
    field = np.real(np.fft.ifft2(spectrum))
    field -= field.min()
    return field / max(field.max(), 1e-8)


def main() -> None:
    rng = np.random.default_rng(SEED)
    height = 0.55 * periodic_noise(rng, 1.6, 1.0) + 0.45 * periodic_noise(rng, 1.1, 6.0)
    # Sparse pitting from cast/rolled surface
    pits = periodic_noise(rng, 0.4, 1.0)
    height -= 0.25 * np.clip((pits - 0.82) / 0.18, 0.0, 1.0)

    strength = 2.2
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength * SIZE / 32.0
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength * SIZE / 32.0
    nx, ny, nz = -dx, dy, np.ones_like(dx)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5
    OUT.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray((rgb * 255.0 + 0.5).astype(np.uint8), "RGB").save(OUT)
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()
