"""Check that the shared atlas unwrap does not stretch the Ironclad mesh."""

import math
import sys
import unittest
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
import RogueModelKit as rmk


class AtlasUvBlenderTests(unittest.TestCase):
    def test_ironclad_atlas_is_not_severely_stretched(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/IroncladWalker.fbx"
        bpy.ops.import_scene.fbx(filepath=str(path))
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        self.assertTrue(meshes)
        rmk.unwrap_atlas_uvs(meshes)
        area_total = area_stretched = 0.0
        degenerate = 0
        for obj in meshes:
            mesh = obj.data
            mesh.calc_loop_triangles()
            uv = mesh.uv_layers.active.data
            for tri in mesh.loop_triangles:
                p = [obj.matrix_world @ mesh.vertices[i].co for i in tri.vertices]
                q = [uv[i].uv.copy() for i in tri.loops]
                a, b = p[1] - p[0], p[2] - p[0]
                u, v = q[1] - q[0], q[2] - q[0]
                area = a.cross(b).length / 2
                det = u.x * v.y - u.y * v.x
                if area < 1e-10:
                    continue
                if abs(det) < 1e-12:
                    degenerate += 1
                    continue
                x, y = (a * v.y - b * u.y) / det, (-a * v.x + b * u.x) / det
                aa, bb, cc = x.dot(x), x.dot(y), y.dot(y)
                disc = math.sqrt(max(0.0, (aa - cc) ** 2 + 4 * bb * bb))
                low, high = (aa + cc - disc) / 2, (aa + cc + disc) / 2
                self.assertGreater(low, 1e-14)
                area_total += area
                if math.sqrt(high / low) > 2:
                    area_stretched += area
        self.assertEqual(degenerate, 0)
        self.assertGreater(area_total, 0)
        self.assertLess(area_stretched / area_total, 0.001)


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]])
