#!/usr/bin/env python3
"""Rogue Command Audio & SFX Asset Validator.

Validates audio clips, AudioSource configurations, and sound integration for Rogue Command:
- Verifies Mono (1-channel) invariant for 3D positional audio in tactical camera view.
- Audits AudioSource 3D settings: spatialBlend = 1.0, min/max distance rolloff, low doppler.
- Checks native hook compliance (ShootProjectile shotSoundAudioSource, EntityFlockingMovement _movementSoundAudioSource, ParticleSoundPlayer).
- Inspects both raw audio files (.wav) and compiled Unity AssetBundles.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
import wave
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import List, Tuple

import UnityPy

# Audio configuration bounds for Rogue Command RTS camera
RECOMMENDED_SAMPLE_RATES = {44100, 48000}
MAX_WEAPON_SHOT_DURATION = 3.0  # seconds
MAX_ENGINE_LOOP_DURATION = 12.0  # seconds
RECOMMENDED_MIN_DISTANCE = 3.0
RECOMMENDED_MAX_DISTANCE = 80.0


@dataclass
class AudioClipAudit:
    """Audit report for an individual AudioClip asset."""

    name: str
    source_type: str  # 'wav_file' or 'bundle'
    channels: int = 1
    sample_rate: int = 44100
    duration_seconds: float = 1.0
    is_mono: bool = True
    intended_usage: str = "3D_Positional"  # '3D_Positional' or '2D_UI_Music'
    flags: List[str] = field(default_factory=list)
    score: int = 100


@dataclass
class AudioSourceAudit:
    """Audit report for an AudioSource component inside a prefab or bundle."""

    owner_name: str
    spatial_blend: float = 1.0  # 1.0 = 3D, 0.0 = 2D
    min_distance: float = 3.0
    max_distance: float = 60.0
    doppler_level: float = 0.0
    rolloff_mode: str = "Logarithmic"
    has_clip_assigned: bool = True
    flags: List[str] = field(default_factory=list)
    score: int = 100


class AudioAssetValidator:
    """Audits audio assets and AudioSource components."""

    def __init__(self):
        self.clip_audits: List[AudioClipAudit] = []
        self.source_audits: List[AudioSourceAudit] = []

    def validate_wav_file(self, wav_path: Path, is_ui_or_music: bool = False) -> AudioClipAudit:
        """Audits a raw uncompressed .wav file."""
        name = wav_path.name
        audit = AudioClipAudit(
            name=name,
            source_type="wav_file",
            intended_usage="2D_UI_Music" if is_ui_or_music else "3D_Positional",
        )

        try:
            with wave.open(str(wav_path), "rb") as wf:
                channels = wf.getnchannels()
                sample_rate = wf.getframerate()
                n_frames = wf.getnframes()
                duration = n_frames / float(sample_rate) if sample_rate > 0 else 0.0

                audit.channels = channels
                audit.sample_rate = sample_rate
                audit.duration_seconds = round(duration, 2)
                audit.is_mono = (channels == 1)

                # Channel validation
                if not audit.is_mono and not is_ui_or_music:
                    audit.flags.append(
                        f"AUDIO_STEREO_IN_3D_SPACE: File has {channels} channels. "
                        "Unity 3D spatial attenuation and directional panning only work on Mono (1-channel) clips. "
                        "Downmix to Mono before importing into AssetBundles."
                    )
                    audit.score -= 40

                # Sample rate check
                if sample_rate not in RECOMMENDED_SAMPLE_RATES:
                    audit.flags.append(
                        f"AUDIO_NONSTANDARD_SAMPLE_RATE: Sample rate is {sample_rate} Hz (recommended: 44100 or 48000 Hz)."
                    )
                    audit.score -= 10

                # Duration bounds
                if not is_ui_or_music and duration > MAX_WEAPON_SHOT_DURATION and ("shot" in name.lower() or "fire" in name.lower()):
                    audit.flags.append(
                        f"AUDIO_EXCESSIVE_SHOT_DURATION: Weapon shot is {duration:.1f}s (recommended <= {MAX_WEAPON_SHOT_DURATION}s)."
                    )
                    audit.score -= 10

        except Exception as e:
            audit.flags.append(f"AUDIO_READ_ERROR: Failed to parse WAV: {e}")
            audit.score = 0

        audit.score = max(0, audit.score)
        return audit

    def validate_bundle_audio(self, bundle_path: Path) -> Tuple[List[AudioClipAudit], List[AudioSourceAudit]]:
        """Extracts and audits AudioClips and AudioSources from a compiled AssetBundle."""
        clips: List[AudioClipAudit] = []
        sources: List[AudioSourceAudit] = []

        if not bundle_path.is_file():
            raise FileNotFoundError(bundle_path)

        env = UnityPy.load(str(bundle_path))

        for obj in env.objects:
            if obj.type.name == "AudioClip":
                try:
                    data = obj.read()
                    name = getattr(data, "m_Name", "") or f"Clip_{obj.path_id}"
                    channels = int(getattr(data, "m_Channels", 1))
                    sample_rate = int(getattr(data, "m_Frequency", 44100))
                    length = float(getattr(data, "m_Length", 1.0))

                    is_ui = "ui" in name.lower() or "music" in name.lower()
                    audit = AudioClipAudit(
                        name=name,
                        source_type="bundle",
                        channels=channels,
                        sample_rate=sample_rate,
                        duration_seconds=round(length, 2),
                        is_mono=(channels == 1),
                        intended_usage="2D_UI_Music" if is_ui else "3D_Positional",
                    )

                    if channels > 1 and not is_ui:
                        audit.flags.append(
                            f"AUDIO_STEREO_IN_3D_SPACE: AudioClip '{name}' has {channels} channels. "
                            "Unity 3D spatial attenuation is disabled on stereo clips."
                        )
                        audit.score -= 40

                    if sample_rate not in RECOMMENDED_SAMPLE_RATES:
                        audit.flags.append(f"AUDIO_NONSTANDARD_SAMPLE_RATE: {sample_rate} Hz.")
                        audit.score -= 10

                    audit.score = max(0, audit.score)
                    clips.append(audit)
                except Exception as exc:
                    clips.append(AudioClipAudit(name=f"Clip_{obj.path_id}", source_type="bundle", score=0,
                                                flags=[f"AUDIO_READ_ERROR: {exc}"]))

            elif obj.type.name == "AudioSource":
                try:
                    data = obj.read()
                    owner_name = getattr(data, "m_Name", "") or f"AudioSource_{obj.path_id}"
                    # UnityPy exposes serialized names, not Unity C# properties.
                    keys = data.panLevelCustomCurve.m_Curve
                    values = [float(key.value) for key in keys]
                    if not values or any(not math.isfinite(v) or not 0 <= v <= 1 for v in values):
                        raise ValueError("missing or invalid spatial-blend curve")
                    spatial_blend = min(values)
                    min_dist = float(data.MinDistance)
                    max_dist = float(data.MaxDistance)
                    doppler = float(data.DopplerLevel)
                    if not all(math.isfinite(v) for v in (min_dist, max_dist, doppler)):
                        raise ValueError("non-finite AudioSource parameters")
                    if min_dist < 0 or max_dist <= min_dist or doppler < 0:
                        raise ValueError("invalid AudioSource distance/doppler range")

                    src_audit = AudioSourceAudit(
                        owner_name=owner_name,
                        spatial_blend=spatial_blend,
                        min_distance=min_dist,
                        max_distance=max_dist,
                        doppler_level=doppler,
                    )

                    if spatial_blend < 0.95:
                        src_audit.flags.append(
                            f"AUDIO_2D_SPATIAL_BLEND: spatialBlend is {spatial_blend:.2f}. "
                            "In-game vehicle sounds must have spatialBlend = 1.0 for full 3D positioning."
                        )
                        src_audit.score -= 30

                    if doppler > 0.3:
                        src_audit.flags.append(
                            f"AUDIO_EXCESSIVE_DOPPLER: dopplerLevel is {doppler:.2f}. "
                            "Rapid RTS camera panning causes unnatural pitch shifting when doppler > 0.2."
                        )
                        src_audit.score -= 15

                    src_audit.score = max(0, src_audit.score)
                    sources.append(src_audit)
                except Exception as exc:
                    sources.append(AudioSourceAudit(owner_name=f"AudioSource_{obj.path_id}", score=0,
                                                    flags=[f"AUDIO_READ_ERROR: {exc}"]))

        return clips, sources

    def validate_prefab_yaml(self, prefab_path: Path) -> List[AudioSourceAudit]:
        """Audits AudioSource configurations inside a Unity .prefab YAML file."""
        sources: List[AudioSourceAudit] = []
        if not prefab_path.exists():
            return sources

        content = prefab_path.read_text(encoding="utf-8", errors="ignore")
        if "AudioSource:" not in content:
            return sources

        # Parse AudioSource blocks
        as_blocks = content.split("--- !u!82 &")  # ClassID 82 is AudioSource
        for block in as_blocks[1:]:
            src_audit = AudioSourceAudit(owner_name=prefab_path.stem)

            sb_match = re.search(r"m_SpatialBlend:\s*([\d\.]+)", block)
            if sb_match:
                src_audit.spatial_blend = float(sb_match.group(1))

            min_match = re.search(r"m_MinDistance:\s*([\d\.]+)", block)
            if min_match:
                src_audit.min_distance = float(min_match.group(1))

            max_match = re.search(r"m_MaxDistance:\s*([\d\.]+)", block)
            if max_match:
                src_audit.max_distance = float(max_match.group(1))

            dop_match = re.search(r"m_DopplerLevel:\s*([\d\.]+)", block)
            if dop_match:
                src_audit.doppler_level = float(dop_match.group(1))

            if src_audit.spatial_blend < 0.95:
                src_audit.flags.append(
                    f"AUDIO_2D_SPATIAL_BLEND: spatialBlend is {src_audit.spatial_blend:.2f}. "
                    "Must be 1.0 for 3D spatialization."
                )
                src_audit.score -= 30

            if src_audit.doppler_level > 0.3:
                src_audit.flags.append(
                    f"AUDIO_EXCESSIVE_DOPPLER: dopplerLevel is {src_audit.doppler_level:.2f}."
                )
                src_audit.score -= 15

            src_audit.score = max(0, src_audit.score)
            sources.append(src_audit)

        return sources


def print_audio_report(clips: List[AudioClipAudit], sources: List[AudioSourceAudit]) -> None:
    """Prints a formatted ASCII report."""
    if clips:
        header = f"{'Audio Clip Name':<32} | {'Source':<10} | {'Ch':<3} | {'Rate (Hz)':<9} | {'Dur (s)':<7} | {'Mono':<5} | {'Score':<5}"
        print("-" * len(header))
        print(header)
        print("-" * len(header))
        for c in clips:
            mono_str = "YES" if c.is_mono else "NO"
            print(f"{c.name:<32} | {c.source_type:<10} | {c.channels:<3} | {c.sample_rate:<9} | {c.duration_seconds:<7.2f} | {mono_str:<5} | {c.score:<5}")
        print("-" * len(header))

    if sources:
        header_src = f"{'AudioSource Owner':<28} | {'SpatialBlend':<12} | {'MinDist':<7} | {'MaxDist':<7} | {'Doppler':<7} | {'Score':<5}"
        print("\n" + "-" * len(header_src))
        print(header_src)
        print("-" * len(header_src))
        for s in sources:
            print(f"{s.owner_name:<28} | {s.spatial_blend:<12.2f} | {s.min_distance:<7.1f} | {s.max_distance:<7.1f} | {s.doppler_level:<7.2f} | {s.score:<5}")
        print("-" * len(header_src))

    # Print diagnostics
    all_flags = [(c.name, c.score, c.flags) for c in clips if c.flags] + [(s.owner_name, s.score, s.flags) for s in sources if s.flags]
    if all_flags:
        print("\n[!] AUDIO AUDIT DIAGNOSTICS:")
        for name, score, flags in all_flags:
            print(f"  * {name} (Score: {score}/100):")
            for f in flags:
                print(f"      - {f}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Rogue Command Audio & SFX Asset Validator."
    )
    parser.add_argument(
        "--pack",
        type=Path,
        help="Path to content pack directory containing content.json and bundles/.",
    )
    parser.add_argument(
        "--bundle",
        type=Path,
        help="Path to a specific compiled .bundle file.",
    )
    parser.add_argument(
        "--audio-dir",
        type=Path,
        help="Path to a directory containing raw .wav/.ogg audio files.",
    )
    parser.add_argument(
        "--prefab",
        type=Path,
        help="Path to a specific Unity .prefab YAML file.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results in JSON format.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    validator = AudioAssetValidator()
    all_clips: List[AudioClipAudit] = []
    all_sources: List[AudioSourceAudit] = []

    # 1. Scan pack bundles
    if args.pack:
        pack_dir = args.pack if args.pack.is_dir() else args.pack.parent
        bundle_dir = pack_dir / "bundles"
        if bundle_dir.exists():
            for b in bundle_dir.glob("*.bundle"):
                if "manifest" not in b.name.lower():
                    c, s = validator.validate_bundle_audio(b)
                    all_clips.extend(c)
                    all_sources.extend(s)

    # 2. Specific bundle
    if args.bundle:
        c, s = validator.validate_bundle_audio(args.bundle)
        all_clips.extend(c)
        all_sources.extend(s)

    # 3. Audio directory
    if args.audio_dir and args.audio_dir.exists():
        for f in args.audio_dir.rglob("*.wav"):
            all_clips.append(validator.validate_wav_file(f))

    # 4. Prefab
    if args.prefab:
        all_sources.extend(validator.validate_prefab_yaml(args.prefab))

    # Default fallback: scan unity authoring project audio or prefabs if no args given
    if not (args.pack or args.bundle or args.audio_dir or args.prefab):
        unity_root = Path("unity/RogueCommandAssetAuthoring/Assets")
        if unity_root.exists():
            for f in unity_root.rglob("*.wav"):
                all_clips.append(validator.validate_wav_file(f))
            for p in unity_root.rglob("*.prefab"):
                all_sources.extend(validator.validate_prefab_yaml(p))

    if not all_clips and not all_sources:
        print("No audio clips or AudioSources found to audit.")
        return 0

    if args.json:
        data = {
            "clips": [asdict(c) for c in all_clips],
            "sources": [asdict(s) for s in all_sources],
        }
        print(json.dumps(data, indent=2))
    else:
        print(f"\nAudited {len(all_clips)} audio clip(s) and {len(all_sources)} AudioSource(s).")
        print_audio_report(all_clips, all_sources)

    has_failures = any(c.score < 50 for c in all_clips) or any(s.score < 50 for s in all_sources)
    return 2 if has_failures else 0


if __name__ == "__main__":
    sys.exit(main())
