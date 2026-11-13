"""Geometry authoring policy: no calibration-dependent build limits.

Calibration data remains available for measurement and is never synthesized
into a passing performance claim. LOD ratios are authoring settings, not a
measured capacity or triangle ceiling.
"""
from __future__ import annotations

STATUS = "UNCALIBRATED_USER_AUTHORIZED"
LOD_RATIOS = (1.0, 0.5, 0.25)
LOD_TRANSITIONS = (0.6, 0.3, 0.1)


def authoring_settings(asset_id: str, scene_id: str | None = None) -> dict:
    if not isinstance(asset_id, str) or not asset_id.strip():
        raise ValueError("asset_id must identify the authored asset")
    return {
        "assetId": asset_id,
        "sceneId": scene_id,
        "geometryBudgetStatus": STATUS,
        "calibrated": False,
        "geometryLimitsApplied": False,
        "lods": [
            {"lod": f"LOD{i}", "retentionRatio": ratio,
             "screenRelativeTransitionHeight": transition}
            for i, (ratio, transition) in enumerate(zip(LOD_RATIOS, LOD_TRANSITIONS))
        ],
    }
