#!/usr/bin/env python3
"""Create reproducible space-military Material Maker 1.7 graph templates.

Configures the Material Maker 1.7 modular panel generator for hard-surface
military-spacecraft and tactical armor plating:
- Laser-cut expansion seams (`mortar = 0.012`), CNC 45-degree chamfers
  (`bevel = 0.035`), and corner miters (`corner = 0.02`).
- Minimal edge warp (`amount = 0.0025`) so panel lines stay crisp and machined
  instead of looking like wavy masonry bricks.
- Anisotropic cold-rolled brushed alloy grain (`scale_x = 32, scale_y = 8`) with
  controlled normal relief (`blend_2.amount = 0.08`, `normal_map_2.param1 = 0.45`).
- A second high-frequency micrograin layer drives both the tangent normal and
  roughness, preserving panel readability while preventing flat 2048/4096 maps.
- Bimodal procedural `colorize_metallic` node wired from the panel height graph.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import cast

EXAMPLE = Path(
    ".tools/MaterialMaker-1.7/material_maker_1_7_windows/examples/bricks.ptex"
)

PRESETS: dict[str, dict[str, object]] = {
    "olive_armor": {
        "label": "Exo-Atmospheric Tactical Olive Armor",
        "name": "tactical_olive_armor_hull",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.009, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.05, 0.07, 0.04)), (1.0, (0.16, 0.20, 0.12))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.18, 0.22, 0.15)),
            (0.20, (0.28, 0.33, 0.22)),
            (0.40, (0.15, 0.19, 0.13)),
            (0.60, (0.34, 0.39, 0.26)),
            (0.80, (0.21, 0.25, 0.17)),
            (1.0, (0.30, 0.35, 0.24)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.26, 0.26, 0.26)),
            (1.0, (0.36, 0.36, 0.36)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.030,
        "normal_strength": 0.38,
        "micro_perlin_scale": 120,
        "micro_blend_amount": 0.11,
        "micro_roughness_amount": 0.09,
    },
    "gunmetal_chassis": {
        "label": "Orbital Dreadnought Gunmetal Hull",
        "name": "tactical_gunmetal_chassis",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.008, "bevel": 0.024, "corner": 0.015},
        "seam_tint": ((0.0, (0.05, 0.06, 0.08)), (1.0, (0.15, 0.18, 0.23))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.16, 0.19, 0.24)),
            (0.25, (0.27, 0.31, 0.38)),
            (0.50, (0.14, 0.17, 0.22)),
            (0.75, (0.33, 0.38, 0.46)),
            (1.0, (0.22, 0.26, 0.32)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.66, 0.66, 0.66)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.24, 0.24, 0.24)),
            (1.0, (0.32, 0.32, 0.32)),
        ),
        "warp_amount": 0.0016,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.028,
        "normal_strength": 0.40,
        "micro_perlin_scale": 124,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "desert_camo": {
        "label": "Lunar/Martian Thermal Regolith Plating",
        "name": "tactical_desert_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 6, "columns": 3, "mortar": 0.009, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.12, 0.09, 0.06)), (1.0, (0.28, 0.22, 0.15))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.34, 0.28, 0.19)),
            (0.25, (0.52, 0.43, 0.30)),
            (0.50, (0.28, 0.23, 0.16)),
            (0.75, (0.58, 0.48, 0.34)),
            (1.0, (0.42, 0.35, 0.24)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.94, 0.94, 0.94)),
            (1.0, (0.90, 0.90, 0.90)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.16, 0.16, 0.16)),
            (0.25, (0.30, 0.30, 0.30)),
            (1.0, (0.40, 0.40, 0.40)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.032,
        "normal_strength": 0.40,
        "micro_perlin_scale": 116,
        "micro_blend_amount": 0.12,
        "micro_roughness_amount": 0.10,
    },
    "hazard_industrial": {
        "label": "Orbital Docking Bay EVA Hazard Plating",
        "name": "hazard_industrial_plating",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 4, "columns": 2, "mortar": 0.009, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.07, 0.07, 0.08)), (1.0, (0.24, 0.20, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.17, 0.18, 0.20)),
            (0.25, (0.58, 0.43, 0.12)),
            (0.50, (0.15, 0.16, 0.18)),
            (0.75, (0.64, 0.48, 0.14)),
            (1.0, (0.24, 0.25, 0.27)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.70, 0.70, 0.70)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.26, 0.26, 0.26)),
            (1.0, (0.36, 0.36, 0.36)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.030,
        "normal_strength": 0.38,
        "micro_perlin_scale": 120,
        "micro_blend_amount": 0.11,
        "micro_roughness_amount": 0.09,
    },
    "orbital_titanium": {
        "label": "Cryo-Titanium Spacecraft Armor",
        "name": "orbital_titanium_armor",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.008, "bevel": 0.022, "corner": 0.014},
        "seam_tint": ((0.0, (0.08, 0.10, 0.13)), (1.0, (0.26, 0.32, 0.38))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.38, 0.43, 0.50)),
            (0.25, (0.54, 0.60, 0.68)),
            (0.50, (0.32, 0.37, 0.44)),
            (0.75, (0.62, 0.68, 0.75)),
            (1.0, (0.46, 0.52, 0.60)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.08, 0.08, 0.08)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.95, 0.95, 0.95)),
        ),
        "roughness_curve": (
            (0.0, (0.62, 0.62, 0.62)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.20, 0.20, 0.20)),
            (1.0, (0.28, 0.28, 0.28)),
        ),
        "warp_amount": 0.0015,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.024,
        "normal_strength": 0.36,
        "micro_perlin_scale": 128,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
    "stealth_void_composite": {
        "label": "Void-Stealth Graphene Composite Plating",
        "name": "stealth_void_composite_hull",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 5, "columns": 3, "mortar": 0.007, "bevel": 0.020, "corner": 0.012},
        "seam_tint": ((0.0, (0.02, 0.02, 0.03)), (1.0, (0.05, 0.06, 0.07))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.12, 0.13, 0.15)),
            (0.25, (0.18, 0.19, 0.22)),
            (0.50, (0.10, 0.11, 0.13)),
            (0.75, (0.22, 0.23, 0.26)),
            (1.0, (0.15, 0.16, 0.18)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.05, 0.05, 0.05)),
            (0.07, (0.95, 0.95, 0.95)),
            (0.12, (0.92, 0.92, 0.92)),
            (0.16, (0.06, 0.06, 0.06)),
            (1.0, (0.04, 0.04, 0.04)),
        ),
        "roughness_curve": (
            (0.0, (0.75, 0.75, 0.75)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.35, 0.35, 0.35)),
            (1.0, (0.46, 0.46, 0.46)),
        ),
        "warp_amount": 0.0015,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.022,
        "normal_strength": 0.35,
        "micro_perlin_scale": 136,
        "micro_blend_amount": 0.08,
        "micro_roughness_amount": 0.07,
    },
    "solar_gold_radiator": {
        "label": "Orbital Solar Gold Radiator Shielding",
        "name": "orbital_solar_radiator_plating",
        "bricks": {"pattern": 3, "repeat": 2, "rows": 4, "columns": 4, "mortar": 0.008, "bevel": 0.020, "corner": 0.012},
        "seam_tint": ((0.0, (0.06, 0.06, 0.07)), (1.0, (0.18, 0.17, 0.16))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.86, 0.66, 0.22)),
            (0.25, (0.96, 0.78, 0.32)),
            (0.50, (0.82, 0.62, 0.18)),
            (0.75, (0.98, 0.82, 0.36)),
            (1.0, (0.90, 0.70, 0.25)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.08, 0.08, 0.08)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.96, 0.96, 0.96)),
        ),
        "roughness_curve": (
            (0.0, (0.62, 0.62, 0.62)),
            (0.08, (0.08, 0.08, 0.08)),
            (0.25, (0.14, 0.14, 0.14)),
            (1.0, (0.20, 0.20, 0.20)),
        ),
        "warp_amount": 0.0014,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.020,
        "normal_strength": 0.32,
        "micro_perlin_scale": 136,
        "micro_blend_amount": 0.07,
        "micro_roughness_amount": 0.05,
    },
    "battleship_tungsten": {
        "label": "Heavy Dreadnought Ballistic Tungsten Armor",
        "name": "dreadnought_tungsten_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 5, "columns": 2, "mortar": 0.010, "bevel": 0.028, "corner": 0.018},
        "seam_tint": ((0.0, (0.04, 0.05, 0.06)), (1.0, (0.12, 0.14, 0.17))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.24, 0.26, 0.30)),
            (0.25, (0.34, 0.37, 0.42)),
            (0.50, (0.20, 0.22, 0.25)),
            (0.75, (0.40, 0.44, 0.48)),
            (1.0, (0.28, 0.31, 0.36)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.26, 0.26, 0.26)),
            (1.0, (0.36, 0.36, 0.36)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.034,
        "normal_strength": 0.40,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.12,
        "micro_roughness_amount": 0.10,
    },
    "cerberus_white_ceramic": {
        "label": "Fleet Interceptor Arctic Ceramic Plating",
        "name": "interceptor_ceramic_white",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 6, "columns": 3, "mortar": 0.008, "bevel": 0.020, "corner": 0.012},
        "seam_tint": ((0.0, (0.04, 0.04, 0.05)), (1.0, (0.12, 0.13, 0.14))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.78, 0.80, 0.82)),
            (0.25, (0.86, 0.88, 0.90)),
            (0.50, (0.72, 0.74, 0.76)),
            (0.75, (0.90, 0.92, 0.94)),
            (1.0, (0.80, 0.82, 0.84)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.94, 0.94, 0.94)),
            (0.12, (0.90, 0.90, 0.90)),
            (0.16, (0.06, 0.06, 0.06)),
            (1.0, (0.04, 0.04, 0.04)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.28, 0.28, 0.28)),
        ),
        "warp_amount": 0.0014,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.022,
        "normal_strength": 0.34,
        "micro_perlin_scale": 128,
        "micro_blend_amount": 0.08,
        "micro_roughness_amount": 0.07,
    },
    "plasma_cinder_ablative": {
        "label": "Re-Entry Sintered Carbon-Phenolic Heatshield",
        "name": "reentry_ablative_cinder",
        "bricks": {"pattern": 3, "repeat": 2, "rows": 5, "columns": 5, "mortar": 0.010, "bevel": 0.024, "corner": 0.016},
        "seam_tint": ((0.0, (0.08, 0.04, 0.03)), (1.0, (0.24, 0.12, 0.07))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.16, 0.14, 0.13)),
            (0.25, (0.26, 0.20, 0.17)),
            (0.50, (0.13, 0.11, 0.10)),
            (0.75, (0.32, 0.23, 0.18)),
            (1.0, (0.20, 0.16, 0.14)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.95, 0.95, 0.95)),
            (1.0, (0.90, 0.90, 0.90)),
        ),
        "roughness_curve": (
            (0.0, (0.75, 0.75, 0.75)),
            (0.08, (0.16, 0.16, 0.16)),
            (0.25, (0.32, 0.32, 0.32)),
            (1.0, (0.40, 0.40, 0.40)),
        ),
        "warp_amount": 0.0020,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.032,
        "normal_strength": 0.40,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.13,
        "micro_roughness_amount": 0.11,
    },
    "cyber_nanoweave": {
        "label": "Electronic Warfare High-Density Nano-Lattice",
        "name": "ew_nanoweave_lattice",
        "bricks": {"pattern": 4, "repeat": 4, "rows": 6, "columns": 4, "mortar": 0.006, "bevel": 0.018, "corner": 0.010},
        "seam_tint": ((0.0, (0.03, 0.05, 0.07)), (1.0, (0.09, 0.14, 0.19))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.14, 0.20, 0.24)),
            (0.25, (0.22, 0.32, 0.38)),
            (0.50, (0.10, 0.15, 0.19)),
            (0.75, (0.28, 0.40, 0.46)),
            (1.0, (0.18, 0.26, 0.32)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.18, 0.18, 0.18)),
            (1.0, (0.26, 0.26, 0.26)),
        ),
        "warp_amount": 0.0013,
        "perlin_scale_x": 22,
        "perlin_scale_y": 22,
        "micro_relief_amount": 0.030,
        "normal_strength": 0.36,
        "micro_perlin_scale": 148,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
    "anodized_cobalt": {
        "label": "Anodized Cobalt-Titanium Stealth Hull",
        "name": "stealth_cobalt_titanium",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.008, "bevel": 0.022, "corner": 0.014},
        "seam_tint": ((0.0, (0.04, 0.05, 0.08)), (1.0, (0.12, 0.15, 0.24))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.15, 0.19, 0.28)),
            (0.25, (0.24, 0.30, 0.44)),
            (0.50, (0.12, 0.15, 0.22)),
            (0.75, (0.30, 0.38, 0.52)),
            (1.0, (0.20, 0.25, 0.36)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.64, 0.64, 0.64)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.30, 0.30, 0.30)),
        ),
        "warp_amount": 0.0015,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.026,
        "normal_strength": 0.36,
        "micro_perlin_scale": 124,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "quantum_hyperdrive_alloy": {
        "label": "Quantum Resonance Super-Alloy Plating",
        "name": "hyperdrive_quantum_alloy",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 5, "columns": 3, "mortar": 0.007, "bevel": 0.020, "corner": 0.012},
        "seam_tint": ((0.0, (0.07, 0.04, 0.08)), (1.0, (0.22, 0.12, 0.26))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.42, 0.44, 0.50)),
            (0.25, (0.58, 0.60, 0.68)),
            (0.50, (0.36, 0.38, 0.44)),
            (0.75, (0.65, 0.68, 0.74)),
            (1.0, (0.50, 0.52, 0.58)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.95, 0.95, 0.95)),
        ),
        "roughness_curve": (
            (0.0, (0.62, 0.62, 0.62)),
            (0.08, (0.08, 0.08, 0.08)),
            (0.25, (0.16, 0.16, 0.16)),
            (1.0, (0.24, 0.24, 0.24)),
        ),
        "warp_amount": 0.0014,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.022,
        "normal_strength": 0.36,
        "micro_perlin_scale": 132,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
    "beryllium_mirror_hull": {
        "label": "Exo-Atmospheric Lightweight Beryllium Optical Shielding",
        "name": "beryllium_mirror_plating",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 6, "columns": 2, "mortar": 0.007, "bevel": 0.018, "corner": 0.010},
        "seam_tint": ((0.0, (0.06, 0.08, 0.10)), (1.0, (0.18, 0.22, 0.28))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.62, 0.66, 0.72)),
            (0.25, (0.76, 0.80, 0.86)),
            (0.50, (0.54, 0.58, 0.64)),
            (0.75, (0.84, 0.88, 0.92)),
            (1.0, (0.68, 0.72, 0.78)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.08, 0.08, 0.08)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.96, 0.96, 0.96)),
        ),
        "roughness_curve": (
            (0.0, (0.60, 0.60, 0.60)),
            (0.08, (0.06, 0.06, 0.06)),
            (0.25, (0.12, 0.12, 0.12)),
            (1.0, (0.20, 0.20, 0.20)),
        ),
        "warp_amount": 0.0012,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.018,
        "normal_strength": 0.30,
        "micro_perlin_scale": 136,
        "micro_blend_amount": 0.08,
        "micro_roughness_amount": 0.06,
    },
    "obsidian_strike_interceptor": {
        "label": "Deep-Space Interceptor Carbon-Graphite Hull",
        "name": "obsidian_strike_interceptor_hull",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 5, "columns": 3, "mortar": 0.008, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.02, 0.02, 0.03)), (1.0, (0.06, 0.07, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.10, 0.11, 0.13)),
            (0.25, (0.16, 0.17, 0.20)),
            (0.50, (0.08, 0.09, 0.11)),
            (0.75, (0.20, 0.22, 0.25)),
            (1.0, (0.13, 0.14, 0.16)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.95, 0.95, 0.95)),
            (0.12, (0.92, 0.92, 0.92)),
            (0.16, (0.10, 0.10, 0.10)),
            (1.0, (0.08, 0.08, 0.08)),
        ),
        "roughness_curve": (
            (0.0, (0.75, 0.75, 0.75)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.28, 0.28, 0.28)),
            (1.0, (0.35, 0.35, 0.35)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 14,
        "perlin_scale_y": 14,
        "micro_relief_amount": 0.026,
        "normal_strength": 0.34,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "fusion_reactor_core_plating": {
        "label": "High-Energy Magnetic Confinement Armor",
        "name": "fusion_tokamak_confinement_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 4, "columns": 2, "mortar": 0.009, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.07, 0.04, 0.03)), (1.0, (0.22, 0.13, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.48, 0.32, 0.18)),
            (0.25, (0.62, 0.44, 0.26)),
            (0.50, (0.42, 0.26, 0.14)),
            (0.75, (0.68, 0.50, 0.30)),
            (1.0, (0.55, 0.38, 0.22)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.66, 0.66, 0.66)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.32, 0.32, 0.32)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.028,
        "normal_strength": 0.38,
        "micro_perlin_scale": 116,
        "micro_blend_amount": 0.11,
        "micro_roughness_amount": 0.09,
    },
    "martian_iron_rust": {
        "label": "Colonial Expedition Heavy Ferrous Plating",
        "name": "colonial_martian_ferrous_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 6, "columns": 3, "mortar": 0.009, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.12, 0.06, 0.04)), (1.0, (0.28, 0.14, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.44, 0.22, 0.14)),
            (0.25, (0.58, 0.32, 0.20)),
            (0.50, (0.36, 0.18, 0.10)),
            (0.75, (0.64, 0.38, 0.24)),
            (1.0, (0.50, 0.26, 0.16)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.95, 0.95, 0.95)),
            (1.0, (0.90, 0.90, 0.90)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.28, 0.28, 0.28)),
            (1.0, (0.38, 0.38, 0.38)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.034,
        "normal_strength": 0.40,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.12,
        "micro_roughness_amount": 0.10,
    },
    "neosteel_command_citadel": {
        "label": "Flagship Reinforced Heavy Neosteel Armor",
        "name": "flagship_neosteel_citadel_armor",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.008, "bevel": 0.024, "corner": 0.015},
        "seam_tint": ((0.0, (0.06, 0.08, 0.10)), (1.0, (0.18, 0.22, 0.26))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.42, 0.46, 0.52)),
            (0.25, (0.58, 0.62, 0.68)),
            (0.50, (0.36, 0.40, 0.46)),
            (0.75, (0.64, 0.68, 0.74)),
            (1.0, (0.50, 0.54, 0.60)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.64, 0.64, 0.64)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.20, 0.20, 0.20)),
            (1.0, (0.26, 0.26, 0.26)),
        ),
        "warp_amount": 0.0015,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.024,
        "normal_strength": 0.38,
        "micro_perlin_scale": 124,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "antimatter_containment_lattice": {
        "label": "Void Vacuum Antimatter Superconducting Tiles",
        "name": "antimatter_superconducting_lattice",
        "bricks": {"pattern": 3, "repeat": 3, "rows": 5, "columns": 5, "mortar": 0.009, "bevel": 0.025, "corner": 0.016},
        "seam_tint": ((0.0, (0.05, 0.03, 0.08)), (1.0, (0.16, 0.10, 0.24))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.26, 0.24, 0.34)),
            (0.25, (0.38, 0.34, 0.48)),
            (0.50, (0.20, 0.18, 0.28)),
            (0.75, (0.44, 0.40, 0.54)),
            (1.0, (0.32, 0.28, 0.40)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.20, 0.20, 0.20)),
            (1.0, (0.28, 0.28, 0.28)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.026,
        "normal_strength": 0.34,
        "micro_perlin_scale": 120,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
    "cryo_glacier_camo": {
        "label": "Europa Sub-Zero Cryogenic Camouflage Armor",
        "name": "cryo_glacier_camo_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 5, "columns": 3, "mortar": 0.008, "bevel": 0.024, "corner": 0.015},
        "seam_tint": ((0.0, (0.06, 0.09, 0.12)), (1.0, (0.18, 0.24, 0.30))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.68, 0.78, 0.84)),
            (0.25, (0.82, 0.90, 0.94)),
            (0.50, (0.60, 0.72, 0.78)),
            (0.75, (0.88, 0.94, 0.98)),
            (1.0, (0.74, 0.84, 0.88)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.70, 0.70, 0.70)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.28, 0.28, 0.28)),
        ),
        "warp_amount": 0.0016,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.026,
        "normal_strength": 0.36,
        "micro_perlin_scale": 120,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "hypervelocity_railgun_cermet": {
        "label": "Hypervelocity Railgun Cermet Armor",
        "name": "hypervelocity_railgun_cermet",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 6, "columns": 2, "mortar": 0.009, "bevel": 0.024, "corner": 0.015},
        "seam_tint": ((0.0, (0.05, 0.04, 0.03)), (1.0, (0.18, 0.12, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.16, 0.17, 0.19)),
            (0.25, (0.26, 0.28, 0.30)),
            (0.50, (0.12, 0.13, 0.15)),
            (0.75, (0.34, 0.36, 0.40)),
            (1.0, (0.20, 0.22, 0.24)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.32, 0.32, 0.32)),
        ),
        "warp_amount": 0.0016,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.030,
        "normal_strength": 0.38,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "stealth_radar_absorbent_ferrite": {
        "label": "Stealth Microwave Absorbent Ferrite Tiles",
        "name": "stealth_ferrite_ram_tiles",
        "bricks": {"pattern": 3, "repeat": 3, "rows": 5, "columns": 5, "mortar": 0.010, "bevel": 0.028, "corner": 0.018},
        "seam_tint": ((0.0, (0.02, 0.02, 0.03)), (1.0, (0.07, 0.07, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.11, 0.12, 0.13)),
            (0.25, (0.18, 0.19, 0.20)),
            (0.50, (0.09, 0.09, 0.10)),
            (0.75, (0.22, 0.23, 0.25)),
            (1.0, (0.14, 0.15, 0.16)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.05, 0.05, 0.05)),
            (0.07, (0.92, 0.92, 0.92)),
            (0.15, (0.88, 0.88, 0.88)),
            (0.20, (0.05, 0.05, 0.05)),
            (1.0, (0.04, 0.04, 0.04)),
        ),
        "roughness_curve": (
            (0.0, (0.76, 0.76, 0.76)),
            (0.08, (0.22, 0.22, 0.22)),
            (0.25, (0.42, 0.42, 0.42)),
            (1.0, (0.52, 0.52, 0.52)),
        ),
        "warp_amount": 0.0020,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.036,
        "normal_strength": 0.42,
        "micro_perlin_scale": 128,
        "micro_blend_amount": 0.14,
        "micro_roughness_amount": 0.12,
    },
    "solar_flare_radiation_shield": {
        "label": "Multi-Layer Coronal Radiation Shielding",
        "name": "coronal_radiation_shield",
        "bricks": {"pattern": 2, "repeat": 3, "rows": 4, "columns": 4, "mortar": 0.008, "bevel": 0.022, "corner": 0.014},
        "seam_tint": ((0.0, (0.08, 0.05, 0.02)), (1.0, (0.25, 0.16, 0.06))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.75, 0.58, 0.20)),
            (0.25, (0.90, 0.74, 0.32)),
            (0.50, (0.65, 0.48, 0.15)),
            (0.75, (0.96, 0.82, 0.40)),
            (1.0, (0.80, 0.64, 0.24)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.08, 0.08, 0.08)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.96, 0.96, 0.96)),
        ),
        "roughness_curve": (
            (0.0, (0.60, 0.60, 0.60)),
            (0.08, (0.08, 0.08, 0.08)),
            (0.25, (0.16, 0.16, 0.16)),
            (1.0, (0.22, 0.22, 0.22)),
        ),
        "warp_amount": 0.0028,
        "perlin_scale_x": 20,
        "perlin_scale_y": 20,
        "micro_relief_amount": 0.042,
        "normal_strength": 0.40,
        "micro_perlin_scale": 140,
        "micro_blend_amount": 0.12,
        "micro_roughness_amount": 0.10,
    },
    "ion_thruster_titanium_grate": {
        "label": "Anodized Ion Thruster Titanium Baffles",
        "name": "ion_thruster_titanium_grate",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 8, "columns": 2, "mortar": 0.014, "bevel": 0.030, "corner": 0.020},
        "seam_tint": ((0.0, (0.04, 0.04, 0.05)), (1.0, (0.10, 0.10, 0.12))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.34, 0.36, 0.45)),
            (0.25, (0.45, 0.42, 0.56)),
            (0.50, (0.28, 0.30, 0.38)),
            (0.75, (0.52, 0.48, 0.62)),
            (1.0, (0.38, 0.40, 0.50)))
        ,
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.66, 0.66, 0.66)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.26, 0.26, 0.26)),
            (1.0, (0.35, 0.35, 0.35)),
        ),
        "warp_amount": 0.0020,
        "perlin_scale_x": 14,
        "perlin_scale_y": 14,
        "micro_relief_amount": 0.032,
        "normal_strength": 0.38,
        "micro_perlin_scale": 104,
        "micro_blend_amount": 0.11,
        "micro_roughness_amount": 0.09,
    },
    "nanite_repair_chitin": {
        "label": "Biomechanical Nanite Swarm Chitin",
        "name": "nanite_repair_chitin",
        "bricks": {"pattern": 3, "repeat": 4, "rows": 6, "columns": 6, "mortar": 0.008, "bevel": 0.022, "corner": 0.014},
        "seam_tint": ((0.0, (0.03, 0.06, 0.04)), (1.0, (0.08, 0.16, 0.11))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.16, 0.22, 0.18)),
            (0.25, (0.24, 0.32, 0.26)),
            (0.50, (0.12, 0.17, 0.14)),
            (0.75, (0.30, 0.40, 0.34)),
            (1.0, (0.20, 0.27, 0.22)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.94, 0.94, 0.94)),
            (1.0, (0.88, 0.88, 0.88)),
        ),
        "roughness_curve": (
            (0.0, (0.70, 0.70, 0.70)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.32, 0.32, 0.32)),
        ),
        "warp_amount": 0.0024,
        "perlin_scale_x": 22,
        "perlin_scale_y": 22,
        "micro_relief_amount": 0.038,
        "normal_strength": 0.36,
        "micro_perlin_scale": 136,
        "micro_blend_amount": 0.12,
        "micro_roughness_amount": 0.10,
    },
    "tungsten_heavy_flak_plating": {
        "label": "Sintered Tungsten Anti-Fragmentation Flak Armor",
        "name": "tungsten_flak_armor",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 4, "columns": 3, "mortar": 0.013, "bevel": 0.034, "corner": 0.022},
        "seam_tint": ((0.0, (0.06, 0.06, 0.06)), (1.0, (0.18, 0.18, 0.18))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.26, 0.27, 0.29)),
            (0.25, (0.36, 0.37, 0.40)),
            (0.50, (0.20, 0.21, 0.23)),
            (0.75, (0.44, 0.46, 0.49)),
            (1.0, (0.30, 0.31, 0.34)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.93, 0.93, 0.93)),
        ),
        "roughness_curve": (
            (0.0, (0.74, 0.74, 0.74)),
            (0.08, (0.18, 0.18, 0.18)),
            (0.25, (0.34, 0.34, 0.34)),
            (1.0, (0.44, 0.44, 0.44)),
        ),
        "warp_amount": 0.0026,
        "perlin_scale_x": 12,
        "perlin_scale_y": 12,
        "micro_relief_amount": 0.044,
        "normal_strength": 0.44,
        "micro_perlin_scale": 80,
        "micro_blend_amount": 0.15,
        "micro_roughness_amount": 0.13,
    },
    "zero_g_docking_alloy": {
        "label": "Orbital Shipyard Zero-G Docking Alloy",
        "name": "docking_clamp_alloy",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 4, "mortar": 0.010, "bevel": 0.026, "corner": 0.018},
        "seam_tint": ((0.0, (0.05, 0.06, 0.07)), (1.0, (0.14, 0.16, 0.19))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.52, 0.55, 0.58)),
            (0.25, (0.64, 0.67, 0.70)),
            (0.50, (0.44, 0.47, 0.50)),
            (0.75, (0.72, 0.75, 0.78)),
            (1.0, (0.58, 0.61, 0.64)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.64, 0.64, 0.64)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.22, 0.22, 0.22)),
            (1.0, (0.30, 0.30, 0.30)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.030,
        "normal_strength": 0.36,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "plasma_arc_containment": {
        "label": "High-Voltage Dielectric Plasma Arc Barrier",
        "name": "plasma_arc_dielectric_barrier",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 7, "columns": 3, "mortar": 0.012, "bevel": 0.028, "corner": 0.018},
        "seam_tint": ((0.0, (0.06, 0.03, 0.07)), (1.0, (0.18, 0.10, 0.20))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.65, 0.63, 0.72)),
            (0.25, (0.78, 0.76, 0.84)),
            (0.50, (0.58, 0.55, 0.64)),
            (0.75, (0.85, 0.82, 0.90)),
            (1.0, (0.70, 0.68, 0.76)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.05, 0.05, 0.05)),
            (0.07, (0.95, 0.95, 0.95)),
            (0.14, (0.92, 0.92, 0.92)),
            (0.18, (0.05, 0.05, 0.05)),
            (1.0, (0.03, 0.03, 0.03)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.18, 0.18, 0.18)),
            (1.0, (0.26, 0.26, 0.26)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 14,
        "perlin_scale_y": 14,
        "micro_relief_amount": 0.028,
        "normal_strength": 0.35,
        "micro_perlin_scale": 120,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
    "tachyon_warp_radiator": {
        "label": "Tachyon Warp Drive Thermal Radiator Fins",
        "name": "tachyon_warp_radiator_hull",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 8, "columns": 2, "mortar": 0.009, "bevel": 0.024, "corner": 0.015},
        "seam_tint": ((0.0, (0.04, 0.03, 0.07)), (1.0, (0.12, 0.10, 0.22))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.18, 0.20, 0.28)),
            (0.25, (0.28, 0.30, 0.40)),
            (0.50, (0.14, 0.16, 0.24)),
            (0.75, (0.35, 0.38, 0.48)),
            (1.0, (0.22, 0.24, 0.32)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.64, 0.64, 0.64)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.18, 0.18, 0.18)),
            (1.0, (0.28, 0.28, 0.28)),
        ),
        "warp_amount": 0.0016,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.028,
        "normal_strength": 0.36,
        "micro_perlin_scale": 120,
        "micro_blend_amount": 0.10,
        "micro_roughness_amount": 0.08,
    },
    "graphene_ablative_lattice": {
        "label": "Hexagonal Graphene Matrix Ablative Tiles",
        "name": "graphene_ablative_lattice",
        "bricks": {"pattern": 3, "repeat": 4, "rows": 6, "columns": 6, "mortar": 0.007, "bevel": 0.020, "corner": 0.012},
        "seam_tint": ((0.0, (0.02, 0.03, 0.02)), (1.0, (0.06, 0.08, 0.06))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.08, 0.10, 0.09)),
            (0.25, (0.14, 0.17, 0.15)),
            (0.50, (0.06, 0.08, 0.07)),
            (0.75, (0.18, 0.22, 0.20)),
            (1.0, (0.11, 0.13, 0.12)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.05, 0.05, 0.05)),
            (0.07, (0.92, 0.92, 0.92)),
            (0.15, (0.86, 0.86, 0.86)),
            (0.20, (0.05, 0.05, 0.05)),
            (1.0, (0.04, 0.04, 0.04)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.12, 0.12, 0.12)),
            (0.25, (0.24, 0.24, 0.24)),
            (1.0, (0.34, 0.34, 0.34)),
        ),
        "warp_amount": 0.0015,
        "perlin_scale_x": 20,
        "perlin_scale_y": 20,
        "micro_relief_amount": 0.026,
        "normal_strength": 0.34,
        "micro_perlin_scale": 128,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
    "dreadnought_adamantine_hull": {
        "label": "Superheavy Dreadnought Adamantine Citadel Plating",
        "name": "adamantine_citadel_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 4, "columns": 2, "mortar": 0.014, "bevel": 0.038, "corner": 0.024},
        "seam_tint": ((0.0, (0.05, 0.05, 0.06)), (1.0, (0.16, 0.16, 0.18))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.22, 0.24, 0.27)),
            (0.25, (0.32, 0.35, 0.39)),
            (0.50, (0.17, 0.19, 0.22)),
            (0.75, (0.40, 0.43, 0.48)),
            (1.0, (0.27, 0.29, 0.33)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.75, 0.75, 0.75)),
            (0.08, (0.18, 0.18, 0.18)),
            (0.25, (0.32, 0.32, 0.32)),
            (1.0, (0.42, 0.42, 0.42)),
        ),
        "warp_amount": 0.0024,
        "perlin_scale_x": 14,
        "perlin_scale_y": 14,
        "micro_relief_amount": 0.040,
        "normal_strength": 0.42,
        "micro_perlin_scale": 96,
        "micro_blend_amount": 0.13,
        "micro_roughness_amount": 0.11,
    },
    "void_stalker_metamaterial": {
        "label": "Optical-Cloaking Metamaterial Phase Shingle",
        "name": "metamaterial_phase_shingle",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 5, "columns": 4, "mortar": 0.007, "bevel": 0.020, "corner": 0.012},
        "seam_tint": ((0.0, (0.03, 0.03, 0.05)), (1.0, (0.09, 0.10, 0.15))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.12, 0.15, 0.22)),
            (0.25, (0.20, 0.24, 0.32)),
            (0.50, (0.09, 0.11, 0.17)),
            (0.75, (0.26, 0.30, 0.40)),
            (1.0, (0.16, 0.19, 0.27)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.95, 0.95, 0.95)),
            (0.16, (0.90, 0.90, 0.90)),
            (0.20, (0.08, 0.08, 0.08)),
            (1.0, (0.06, 0.06, 0.06)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.08, (0.08, 0.08, 0.08)),
            (0.25, (0.18, 0.18, 0.18)),
            (1.0, (0.26, 0.26, 0.26)),
        ),
        "warp_amount": 0.0015,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.022,
        "normal_strength": 0.32,
        "micro_perlin_scale": 132,
        "micro_blend_amount": 0.08,
        "micro_roughness_amount": 0.06,
    },
    "orbital_drop_pod_cermet": {
        "label": "Atmospheric Re-entry Kinetic Cermet Tiles",
        "name": "drop_pod_reentry_cermet",
        "bricks": {"pattern": 2, "repeat": 3, "rows": 5, "columns": 5, "mortar": 0.011, "bevel": 0.030, "corner": 0.018},
        "seam_tint": ((0.0, (0.08, 0.05, 0.03)), (1.0, (0.24, 0.15, 0.09))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.50, 0.38, 0.28)),
            (0.25, (0.64, 0.50, 0.38)),
            (0.50, (0.42, 0.30, 0.22)),
            (0.75, (0.72, 0.58, 0.44)),
            (1.0, (0.56, 0.44, 0.33)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.94, 0.94, 0.94)),
            (0.15, (0.88, 0.88, 0.88)),
            (0.20, (0.06, 0.06, 0.06)),
            (1.0, (0.05, 0.05, 0.05)),
        ),
        "roughness_curve": (
            (0.0, (0.78, 0.78, 0.78)),
            (0.08, (0.20, 0.20, 0.20)),
            (0.25, (0.36, 0.36, 0.36)),
            (1.0, (0.48, 0.48, 0.48)),
        ),
        "warp_amount": 0.0022,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.038,
        "normal_strength": 0.40,
        "micro_perlin_scale": 108,
        "micro_blend_amount": 0.12,
        "micro_roughness_amount": 0.10,
    },
    "pulsar_magnetic_shield": {
        "label": "Pulsar Magnetosphere Induction Armor",
        "name": "pulsar_magnetosphere_armor",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 6, "columns": 3, "mortar": 0.010, "bevel": 0.026, "corner": 0.016},
        "seam_tint": ((0.0, (0.08, 0.04, 0.06)), (1.0, (0.24, 0.12, 0.18))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.58, 0.42, 0.32)),
            (0.25, (0.72, 0.54, 0.42)),
            (0.50, (0.48, 0.34, 0.25)),
            (0.75, (0.80, 0.62, 0.48)),
            (1.0, (0.64, 0.48, 0.36)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.08, 0.08, 0.08)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.95, 0.95, 0.95)),
        ),
        "roughness_curve": (
            (0.0, (0.62, 0.62, 0.62)),
            (0.08, (0.10, 0.10, 0.10)),
            (0.25, (0.20, 0.20, 0.20)),
            (1.0, (0.30, 0.30, 0.30)),
        ),
        "warp_amount": 0.0018,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.030,
        "normal_strength": 0.38,
        "micro_perlin_scale": 116,
        "micro_blend_amount": 0.11,
        "micro_roughness_amount": 0.09,
    },
    "exoskeleton_reactive_scale": {
        "label": "Articulated Combat Walker Reactive Scales",
        "name": "walker_reactive_scale_armor",
        "bricks": {"pattern": 3, "repeat": 3, "rows": 6, "columns": 4, "mortar": 0.010, "bevel": 0.028, "corner": 0.018},
        "seam_tint": ((0.0, (0.04, 0.05, 0.03)), (1.0, (0.12, 0.15, 0.10))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.22, 0.26, 0.18)),
            (0.25, (0.32, 0.38, 0.26)),
            (0.50, (0.16, 0.20, 0.13)),
            (0.75, (0.40, 0.46, 0.33)),
            (1.0, (0.26, 0.31, 0.22)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.95, 0.95, 0.95)),
            (1.0, (0.90, 0.90, 0.90)),
        ),
        "roughness_curve": (
            (0.0, (0.70, 0.70, 0.70)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.28, 0.28, 0.28)),
            (1.0, (0.38, 0.38, 0.38)),
        ),
        "warp_amount": 0.0022,
        "perlin_scale_x": 18,
        "perlin_scale_y": 18,
        "micro_relief_amount": 0.034,
        "normal_strength": 0.38,
        "micro_perlin_scale": 112,
        "micro_blend_amount": 0.11,
        "micro_roughness_amount": 0.09,
    },
    "cryogenic_fuel_shroud": {
        "label": "Cryogenic Liquid Hydrogen Tank Vacuum Shroud",
        "name": "cryo_fuel_tank_shroud",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 4, "mortar": 0.009, "bevel": 0.022, "corner": 0.014},
        "seam_tint": ((0.0, (0.04, 0.05, 0.06)), (1.0, (0.12, 0.15, 0.18))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.68, 0.72, 0.78)),
            (0.25, (0.82, 0.85, 0.90)),
            (0.50, (0.58, 0.62, 0.68)),
            (0.75, (0.90, 0.92, 0.96)),
            (1.0, (0.74, 0.78, 0.84)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.98, 0.98, 0.98)),
            (1.0, (0.96, 0.96, 0.96)),
        ),
        "roughness_curve": (
            (0.0, (0.60, 0.60, 0.60)),
            (0.08, (0.06, 0.06, 0.06)),
            (0.25, (0.14, 0.14, 0.14)),
            (1.0, (0.22, 0.22, 0.22)),
        ),
        "warp_amount": 0.0016,
        "perlin_scale_x": 16,
        "perlin_scale_y": 16,
        "micro_relief_amount": 0.024,
        "normal_strength": 0.32,
        "micro_perlin_scale": 124,
        "micro_blend_amount": 0.09,
        "micro_roughness_amount": 0.07,
    },
}


# Edge-only emission profiles keep the atlas mostly unlit while giving each
# tactical material a restrained sensor/thermal accent at machined bevels.
# Values are linear RGB, bevel threshold, and Material Maker emission energy.
EMISSION_PROFILES: dict[str, tuple[tuple[float, float, float], float, float]] = {
    "olive_armor": ((0.08, 0.40, 0.14), 0.45, 2.0),
    "gunmetal_chassis": ((0.05, 0.35, 0.95), 0.45, 2.0),
    "desert_camo": ((0.95, 0.28, 0.04), 0.42, 2.0),
    "hazard_industrial": ((1.0, 0.50, 0.04), 0.42, 2.2),
    "orbital_titanium": ((0.10, 0.55, 1.0), 0.42, 2.1),
    "stealth_void_composite": ((0.04, 0.30, 0.90), 0.48, 1.9),
    "solar_gold_radiator": ((1.0, 0.35, 0.03), 0.42, 2.3),
    "battleship_tungsten": ((0.40, 0.65, 1.0), 0.42, 2.0),
    "cerberus_white_ceramic": ((0.30, 0.85, 1.0), 0.42, 1.8),
    "plasma_cinder_ablative": ((1.0, 0.22, 0.02), 0.40, 2.5),
    "cyber_nanoweave": ((0.02, 0.90, 1.0), 0.42, 2.2),
    "anodized_cobalt": ((0.25, 0.35, 1.0), 0.42, 2.1),
    "quantum_hyperdrive_alloy": ((0.80, 0.16, 1.0), 0.40, 2.4),
    "beryllium_mirror_hull": ((0.18, 0.90, 1.0), 0.42, 2.0),
    "obsidian_strike_interceptor": ((0.05, 0.90, 1.0), 0.45, 2.0),
    "fusion_reactor_core_plating": ((1.0, 0.48, 0.06), 0.40, 2.3),
    "martian_iron_rust": ((1.0, 0.22, 0.03), 0.42, 2.0),
    "neosteel_command_citadel": ((0.15, 0.55, 1.0), 0.42, 2.1),
    "antimatter_containment_lattice": ((0.85, 0.08, 0.95), 0.40, 2.4),
    "cryo_glacier_camo": ((0.12, 0.95, 0.90), 0.42, 2.0),
    "hypervelocity_railgun_cermet": ((0.08, 0.65, 1.0), 0.45, 2.2),
    "stealth_radar_absorbent_ferrite": ((0.85, 0.08, 0.04), 0.55, 1.5),
    "solar_flare_radiation_shield": ((1.0, 0.55, 0.02), 0.45, 2.3),
    "ion_thruster_titanium_grate": ((0.15, 0.50, 1.0), 0.40, 2.5),
    "nanite_repair_chitin": ((0.05, 1.0, 0.35), 0.45, 2.0),
    "tungsten_heavy_flak_plating": ((1.0, 0.85, 0.05), 0.45, 1.9),
    "zero_g_docking_alloy": ((1.0, 0.60, 0.08), 0.45, 2.1),
    "plasma_arc_containment": ((0.75, 0.15, 1.0), 0.40, 2.6),
    "tachyon_warp_radiator": ((0.20, 0.40, 1.0), 0.42, 2.2),
    "graphene_ablative_lattice": ((0.05, 0.95, 0.40), 0.45, 2.0),
    "dreadnought_adamantine_hull": ((1.0, 0.50, 0.04), 0.45, 1.9),
    "void_stalker_metamaterial": ((0.55, 0.10, 1.0), 0.45, 2.1),
    "orbital_drop_pod_cermet": ((1.0, 0.22, 0.02), 0.42, 2.3),
    "pulsar_magnetic_shield": ((0.95, 0.15, 0.85), 0.40, 2.4),
    "exoskeleton_reactive_scale": ((1.0, 0.75, 0.05), 0.45, 1.8),
    "cryogenic_fuel_shroud": ((0.10, 0.85, 1.0), 0.42, 2.0),
}


def color(r: float, g: float, b: float) -> dict[str, object]:
    return {"a": 1.0, "b": round(float(b), 4), "g": round(float(g), 4), "r": round(float(r), 4), "type": "Color"}


def gradient(*points: tuple[float, tuple[float, float, float]]) -> dict[str, object]:
    return {
        "interpolation": 1,
        "points": [
            {**color(*rgb), "pos": round(float(position), 4)} for position, rgb in points
        ],
        "type": "Gradient",
    }


_TEMPLATE_CACHE: dict[str, dict[str, object]] = {}


def _load_example_template(example_path: Path) -> dict[str, object]:
    import copy

    resolved = str(example_path.resolve())
    if resolved not in _TEMPLATE_CACHE:
        _TEMPLATE_CACHE[resolved] = json.loads(example_path.read_text(encoding="utf-8"))
    return copy.deepcopy(_TEMPLATE_CACHE[resolved])


def _configure_subgraph_bricks(subgraph_node: dict[str, object], bricks_cfg: dict[str, object]) -> None:
    for child in subgraph_node.get("nodes", []):
        cname = child.get("name")
        cparams = child.setdefault("parameters", {})
        if cname == "Bricks":
            cparams["pattern"] = bricks_cfg["pattern"]
            cparams["repeat"] = bricks_cfg["repeat"]
            cparams["rows"] = bricks_cfg["rows"]
            cparams["columns"] = bricks_cfg["columns"]
            cparams["mortar"] = bricks_cfg["mortar"]
            cparams["bevel"] = bricks_cfg["bevel"]
            cparams["corner"] = bricks_cfg["corner"]
            cparams["round"] = 0.0
        elif cname == "switch_0":
            cparams["source"] = 0


def _ensure_node(
    nodes: list[dict[str, object]],
    name: str,
    node_type: str,
    position: tuple[float, float],
    parameters: dict[str, object],
) -> dict[str, object]:
    """Create or refresh an idempotent Material Maker detail node."""
    node = next((entry for entry in nodes if entry.get("name") == name), None)
    if node is None:
        node = {
            "name": name,
            "node_position": {"x": position[0], "y": position[1]},
            "parameters": {},
            "type": node_type,
        }
        nodes.append(node)
    node["type"] = node_type
    node["parameters"] = dict(parameters)
    return node


def build_template_project(
    preset_name: str = "olive_armor",
    size_power: int = 11,
    example_path: Path = EXAMPLE,
) -> dict[str, object]:
    if preset_name not in PRESETS:
        raise ValueError(f"Unknown Material Maker preset '{preset_name}'. Choices: {sorted(PRESETS)}")
    if not example_path.is_file():
        raise FileNotFoundError(f"Material Maker example is missing: {example_path}")

    preset = PRESETS[preset_name]
    emission_rgb, emission_threshold, emission_energy = EMISSION_PROFILES[preset_name]
    project = _load_example_template(example_path)
    project["label"] = preset["label"]
    project["name"] = preset["name"]
    project["rogueCommandQuality"] = {
        "profile": "tactical-hd-v2",
        "authoringTool": "Material Maker 1.7",
        "shader": "RogueCommand/VehiclePBR_VertexWear",
        "microDetail": "multi-scale-normal-and-roughness",
        "mapContract": ["Albedo", "MetallicGloss", "Normal", "Emission"],
    }

    normal_strength = float(cast(float, preset.get("normal_strength", 0.38)))
    micro_scale = int(cast(int, preset.get("micro_perlin_scale", 96)))
    micro_blend_amount = float(cast(float, preset.get("micro_blend_amount", 0.12)))
    micro_roughness_amount = float(cast(float, preset.get("micro_roughness_amount", 0.10)))

    nodes = project.get("nodes", [])
    for node in nodes:
        name = node.get("name")
        parameters = node.setdefault("parameters", {})
        if name == "Material":
            parameters["size"] = int(size_power)
            parameters["metallic"] = 1
            parameters["roughness"] = 1
            parameters["normal"] = 1
            parameters["emission_energy"] = float(emission_energy)
        elif name == "graph":
            _configure_subgraph_bricks(node, preset["bricks"])
        elif name == "Warp":
            # Crisp CNC-machined spacecraft seams with micro edge wear only.
            parameters["amount"] = float(preset.get("warp_amount", 0.0025))
            parameters["eps"] = 0.015
        elif name == "Perlin":
            # Balanced isotropic aerospace micro-grain (smooth CNC finish without streaky banding).
            parameters["scale_x"] = int(preset.get("perlin_scale_x", 12))
            parameters["scale_y"] = int(preset.get("perlin_scale_y", 12))
            parameters["iterations"] = 4
            parameters["persistence"] = 0.50
        elif name == "blend_2":
            # Crisp panel bevel geometry blended with subtle satin metal micro-relief.
            parameters["amount"] = float(preset.get("micro_relief_amount", 0.035))
        elif name == "normal_map_2":
            parameters["param0"] = int(size_power)
            parameters["param1"] = normal_strength
        elif name == "uniform_0":
            parameters["color"] = color(0.90, 0.90, 0.90)
        elif name == "colorize_0":
            parameters["gradient"] = gradient(*preset["seam_tint"])
        elif name == "colorize_2":
            parameters["gradient"] = gradient(*preset["bevel_mask"])
        elif name == "colorize_1":
            parameters["gradient"] = gradient(*preset["plate_palette"])
        elif name == "colorize_4":
            parameters["gradient"] = gradient(*preset["roughness_curve"])

    has_metal_node = any(n.get("name") == "colorize_metallic" for n in nodes)
    if not has_metal_node:
        nodes.append(
            {
                "name": "colorize_metallic",
                "node_position": {"x": 760.0, "y": 140.0},
                "parameters": {
                    "gradient": gradient(*preset["metallic_curve"]),
                },
                "type": "colorize",
            }
        )
    else:
        for n in nodes:
            if n.get("name") == "colorize_metallic":
                n.setdefault("parameters", {})["gradient"] = gradient(*preset["metallic_curve"])

    _ensure_node(
        nodes,
        "micro_perlin",
        "perlin",
        (480.0, 520.0),
        {
            "iterations": 3,
            "persistence": 0.34,
            "scale_x": micro_scale,
            "scale_y": micro_scale,
        },
    )
    _ensure_node(
        nodes,
        "micro_detail_blend",
        "blend",
        (700.0, 520.0),
        {"amount": micro_blend_amount, "blend_type": 0},
    )
    _ensure_node(
        nodes,
        "micro_roughness_blend",
        "blend",
        (920.0, 240.0),
        {"amount": micro_roughness_amount, "blend_type": 0},
    )
    project["rogueCommandQuality"].update(
        {
            "microPerlinScale": micro_scale,
            "microNormalStrength": round(normal_strength * 0.34, 4),
            "microNormalBlend": micro_blend_amount,
            "microRoughnessBlend": micro_roughness_amount,
        }
    )

    emission_gradient = gradient(
        (0.0, (0.0, 0.0, 0.0)),
        (float(emission_threshold), (0.0, 0.0, 0.0)),
        (1.0, emission_rgb),
    )
    emission_node = next((n for n in nodes if n.get("name") == "colorize_emission"), None)
    if emission_node is None:
        nodes.append(
            {
                "name": "colorize_emission",
                "node_position": {"x": 760.0, "y": 360.0},
                "parameters": {"gradient": emission_gradient},
                "type": "colorize",
            }
        )
    else:
        emission_node.setdefault("parameters", {})["gradient"] = emission_gradient

    edge_node = next((n for n in nodes if n.get("name") == "edge_detect_emission"), None)
    edge_parameters = {"size": int(size_power), "threshold": 0.06, "width": 1}
    if edge_node is None:
        nodes.append(
            {
                "name": "edge_detect_emission",
                "node_position": {"x": 560.0, "y": 360.0},
                "parameters": edge_parameters,
                "type": "edge_detect",
            }
        )
    else:
        edge_node["parameters"] = edge_parameters

    connections = [
        c
        for c in project.get("connections", [])
        if not (
            (c.get("to") == "Material" and c.get("to_port") in (1, 2, 3))
            or c.get("to") in (
                "colorize_emission",
                "edge_detect_emission",
                "micro_detail_blend",
                "micro_roughness_blend",
                "normal_map_2",
            )
        )
    ]
    connections.append(
        {
            "from": "Warp",
            "from_port": 0,
            "to": "colorize_metallic",
            "to_port": 0,
        }
    )
    connections.append(
        {
            "from": "colorize_metallic",
            "from_port": 0,
            "to": "Material",
            "to_port": 1,
        }
    )
    connections.append(
        {
            "from": "graph",
            "from_port": 0,
            "to": "edge_detect_emission",
            "to_port": 0,
        }
    )
    connections.append(
        {
            "from": "edge_detect_emission",
            "from_port": 0,
            "to": "colorize_emission",
            "to_port": 0,
        }
    )
    connections.append(
        {
            "from": "colorize_emission",
            "from_port": 0,
            "to": "Material",
            "to_port": 3,
        }
    )
    connections.extend(
        [
            {
                "from": "blend_2",
                "from_port": 0,
                "to": "micro_detail_blend",
                "to_port": 0,
            },
            {
                "from": "micro_perlin",
                "from_port": 0,
                "to": "micro_detail_blend",
                "to_port": 1,
            },
            {
                "from": "micro_detail_blend",
                "from_port": 0,
                "to": "normal_map_2",
                "to_port": 0,
            },
            {
                "from": "colorize_4",
                "from_port": 0,
                "to": "micro_roughness_blend",
                "to_port": 0,
            },
            {
                "from": "micro_perlin",
                "from_port": 0,
                "to": "micro_roughness_blend",
                "to_port": 1,
            },
            {
                "from": "micro_roughness_blend",
                "from_port": 0,
                "to": "Material",
                "to_port": 2,
            },
        ]
    )
    project["connections"] = connections
    return project


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--preset",
        choices=sorted(PRESETS),
        default="olive_armor",
        help="Space-military surface preset to author into the .ptex graph.",
    )
    parser.add_argument(
        "--size",
        type=int,
        default=11,
        choices=[9, 10, 11, 12],
        help="Material Maker power-of-two resolution exponent (10=1024, 11=2048).",
    )
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    if args.output.exists() and not args.force:
        print(f"Keeping existing Material Maker template: {args.output}")
        return 0

    project = build_template_project(preset_name=args.preset, size_power=args.size)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(project, indent="\t"), encoding="utf-8")
    print(f"Prepared Material Maker template ({args.preset}, 2^{args.size}): {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
