"""Regression tests for geometry builds without calibration limits."""
from __future__ import annotations
import ast
import json
import subprocess
import tempfile
from pathlib import Path
import unittest

from tests.python.helpers import REPO_ROOT as ROOT, parse_ast_cached

BLENDER = ROOT / '.tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe'

class ModelBuildWithoutCalibrationTests(unittest.TestCase):
    def test_asset_build_settings_do_not_depend_on_calibration(self):
        from tools.model_build_policy import authoring_settings, STATUS
        settings = authoring_settings('BulwarkSiegeTank')
        self.assertEqual(settings['geometryBudgetStatus'], STATUS)
        self.assertFalse(settings['calibrated'])
        self.assertFalse(settings['geometryLimitsApplied'])
        self.assertEqual([x['retentionRatio'] for x in settings['lods']], [1.0, .5, .25])
        self.assertTrue(all('targetTrianglesPerInstance' not in x for x in settings['lods']))
        with self.assertRaises(ValueError): authoring_settings('')

    @unittest.skipUnless(BLENDER.exists(), 'Blender 4.5.14 is required')
    def test_fbx_and_lods_build_with_missing_profile_without_claiming_calibration(self):
        with tempfile.TemporaryDirectory(prefix='rcl-unlimited-') as tmp:
            tmpdir=Path(tmp); script=tmpdir/'probe.py'; fbx=tmpdir/'candidate.fbx'
            script.write_text('''\nimport json, sys\nfrom pathlib import Path\nimport bpy\nsys.path.insert(0,str(Path.cwd()/"tools"))\nimport RogueModelKit as kit\nbpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)\nbpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32)\nobj=bpy.context.object\nmissing=Path(sys.argv[sys.argv.index("--")+1])\noutput=Path(sys.argv[sys.argv.index("--")+2])\nsettings=kit.resolve_geometry_budget("Fixture", profile_path=missing)\nassert settings["calibrated"] is False and settings["geometryLimitsApplied"] is False\nbefore=len(obj.data.polygons); counts=kit.enforce_face_budget_before_export([obj],"Fixture",profile_path=missing)\nassert counts["faces_removed"] == 0 and len(obj.data.polygons)==before\nkit.export_unity_fbx(output,[obj],asset_id="Fixture",profile_path=missing,generate_profiled_lods=True)\nassert output.exists() and output.with_name("candidate_LODs.fbx").exists()\nassert kit.summarize_lod_budget(settings,{"LOD0":[obj]})["verdict"] == "UNCALIBRATED_USER_AUTHORIZED"\ntry: kit._validate_export_budget_token(kit.resolve_geometry_budget("Different"),"Fixture")\nexcept ValueError: pass\nelse: raise AssertionError("cross-asset settings accepted")\nprint("UNLIMITED_BUILD_TEST_OK")\n''',encoding='utf-8')
            proc=subprocess.run([str(BLENDER),'--background','--factory-startup','--python-exit-code','1','--python',str(script),'--',str(tmpdir/'missing-profile.json'),str(fbx)],cwd=ROOT,capture_output=True,text=True,timeout=180)
            self.assertEqual(proc.returncode,0,proc.stdout[-3000:]+proc.stderr[-3000:])
            self.assertIn('UNLIMITED_BUILD_TEST_OK',proc.stdout)

    @unittest.skipUnless(BLENDER.exists(), 'Blender 4.5.14 is required')
    def test_vehicle_and_structure_export_without_profile_or_old_budget_limit(self):
        with tempfile.TemporaryDirectory(prefix='rcl-generic-build-') as tmp:
            out=Path(tmp); script=out/'generic.py'
            script.write_text('''\nimport bpy,sys\nfrom pathlib import Path\nsys.path.insert(0,str(Path.cwd()/"tools"));import RogueModelKit as m\nout=Path(sys.argv[sys.argv.index("--")+1])\nm.clear_scene();v=m.RogueVehicleBuilder("NoCalibrationVehicle")\nv.objects.append(m.make_box("NoCalibrationVehicleHull",(0,0,.5),(2,3,1),v.palette.primary_armor,bevel=0))\nr=v.build_and_export(out/"vehicle.fbx",out/"vehicle.blend",include_locators=False,generate_lods=True,wear_level="none",bake_vertex_dirt=False,weighted_normals=False,target_budget=1,budget_profile_path=out/"missing.json")\nassert r["geometryLimitsApplied"] is False and r["geometry_budget"]["calibrated"] is False\nm.clear_scene();s=m.RogueStructureBuilder("NoCalibrationStructure");s.build_factory(width=3,length=4,height=2)\nr=s.build_and_export(out/"structure.fbx",out/"structure.blend",generate_lods=True,generate_colliders=False,bake_vertex_shading=False,weighted_normals=False,target_budget=1,budget_profile_path=out/"missing.json")\nassert r["geometryLimitsApplied"] is False and r["geometry_budget"]["calibrated"] is False\nassert (out/"vehicle_LODs.fbx").is_file() and (out/"structure_LODs.fbx").is_file()\nprint("GENERIC_BUILDERS_NO_CALIBRATION_OK")\n''',encoding='utf-8')
            proc=subprocess.run([str(BLENDER),'--background','--factory-startup','--python-exit-code','1','--python',str(script),'--',str(out)],cwd=ROOT,capture_output=True,text=True,timeout=180)
            self.assertEqual(proc.returncode,0,proc.stdout[-3000:]+proc.stderr[-3000:])
            self.assertIn('GENERIC_BUILDERS_NO_CALIBRATION_OK',proc.stdout)

    def test_recipe_builders_do_not_save_over_the_source_blend(self):
        for name in ('Build-ArcLanceWalkerModel.py','Build-BulwarkSiegeTankModel.py','Build-FieldMedicRoverModel.py','Build-SalvageHaulerModel.py','Build-SkyguardSAMModel.py','Build-TidebreakerHovercraftModel.py'):
            with self.subTest(builder=name):
                tree=parse_ast_cached(ROOT/'tools'/name)
                for node in ast.walk(tree):
                    if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr=='save_as_mainfile':
                        self.assertFalse(any(isinstance(n,ast.Name) and n.id=='SOURCE_BLEND' for n in ast.walk(node)))

if __name__ == '__main__': unittest.main()
