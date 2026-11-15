"""Adapt the authored Atlas biped rig and cut damper seats in its rest pose."""
import bpy
import RogueModelKit as rmk
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from ironclad_damper_clearance_recipe import cut_biped_damper_clearance
from ironclad_actuator_support_recipe import add_biped_actuator_supports


def prepare_atlas_damper_clearance(armature):
    if armature.type != 'ARMATURE':
        raise ValueError('Atlas clearance requires its existing armature')
    original_pose = armature.data.pose_position
    armature.data.pose_position = 'REST'
    try:
        bpy.context.view_layer.update()
        cut_biped_damper_clearance('AtlasCraneMech')
        for side in ('L', 'R'):
            bone = f'Bone_Shin_{side}'
            parts = [f'{p}_{side}' for p in ('ShinArmor', 'CalfArmor', 'ShinBone')]
            parts += [f'AnkleDamper_{side}_{tag}' for tag in ('L', 'R')]
            for part in parts:
                obj = bpy.data.objects[f'AtlasCraneMechLeg_{part}']
                for group in list(obj.vertex_groups):
                    obj.vertex_groups.remove(group)
                obj.vertex_groups.new(name=bone).add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
            clevis = bpy.data.objects[f'AtlasCraneMechLeg_AnkleClevis_{side}']
            for group in list(clevis.vertex_groups):
                clevis.vertex_groups.remove(group)
            clevis.vertex_groups.new(name=f'Bone_Foot_{side}').add(list(range(len(clevis.data.vertices))), 1.0, 'REPLACE')
            support_name = f'AtlasCraneMechLeg_FootAnkleSupport_{side}'
            if bpy.data.objects.get(support_name) is None:
                foot = bpy.data.objects[f'AtlasCraneMechLeg_FootBase_{side}']
                def bounds(obj):
                    coords = [obj.matrix_world @ v.co for v in obj.data.vertices]
                    return (min(v.z for v in coords), max(v.z for v in coords))
                clevis_min, _ = bounds(clevis)
                _, foot_max = bounds(foot)
                start = Vector((foot.matrix_world.translation.x, foot.matrix_world.translation.y, foot_max - 0.01))
                end = Vector((clevis.matrix_world.translation.x, clevis.matrix_world.translation.y, clevis_min + 0.01))
                support = rmk.make_rod(support_name, start, end, 0.045, clevis.active_material, vertices=8)
                support.vertex_groups.new(name=f'Bone_Foot_{side}').add(list(range(len(support.data.vertices))), 1.0, 'REPLACE')
                support.modifiers.new('Armature', 'ARMATURE').object = armature
            for obj in bpy.data.objects:
                if obj.type != 'MESH' or not obj.name.startswith(f'AtlasCraneMechLeg_HipActuator_{side}_'):
                    continue
                for group in list(obj.vertex_groups):
                    obj.vertex_groups.remove(group)
                obj.vertex_groups.new(name=f'Bone_Thigh_{side}').add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
            for obj in bpy.data.objects:
                if obj.type != 'MESH' or not obj.name.startswith(f'AtlasCraneMechPiston_Leg_{side}_'):
                    continue
                for group in list(obj.vertex_groups):
                    obj.vertex_groups.remove(group)
                obj.vertex_groups.new(name=f'Bone_Thigh_{side}').add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
            shin = armature.pose.bones[bone]
            for constraint in shin.constraints:
                if constraint.type == 'IK':
                    constraint.pole_angle = 1.5707963267948966
        # Existing four roof lugs and pads have no nearby supporting shell.
        # Remove the unsupported clusters until a host-fitted mount is designed.
        for obj in list(bpy.data.objects):
            if obj.name.startswith(('AtlasCraneMechArmor_LiftingLug_', 'AtlasCraneMechArmor_LugWeldPad_')):
                bpy.data.objects.remove(obj, do_unlink=True)
        # The original gussets have no adjacent load-bearing host and follow
        # a different bone from the shoulder mechanism; omit those clusters.
        for obj in list(bpy.data.objects):
            if obj.name.startswith('AtlasCraneMechArmor_Gusset_'):
                bpy.data.objects.remove(obj, do_unlink=True)
        hatch_names = ['CoamingRim', 'HatchPlate', 'HingeBar', 'DogLatch']
        hatch = [bpy.data.objects[f'AtlasCraneMechArmor_AccessHatch_{name}'] for name in hatch_names]
        if not hatch[0].get('RCL_AtlasRoofSeatV1'):
            # Shorten the coaming at its front while preserving the hinge end.
            for obj in hatch:
                for vertex in obj.data.vertices:
                    vertex.co.y = -0.35 + (vertex.co.y + obj.location.y + 0.35) * 0.75 - obj.location.y
            bpy.context.view_layer.update()
            host = bpy.data.objects['AtlasCraneMechTurret_BaseShell'].evaluated_get(bpy.context.evaluated_depsgraph_get())
            mesh = host.to_mesh()
            try:
                tree = BVHTree.FromPolygons([host.matrix_world @ v.co for v in mesh.vertices], [tuple(p.vertices) for p in mesh.polygons])
                coords = [hatch[0].matrix_world @ v.co for v in hatch[0].data.vertices]
                low = min(v.z for v in coords)
                x0, x1 = min(v.x for v in coords), max(v.x for v in coords)
                y0, y1 = min(v.y for v in coords), max(v.y for v in coords)
                heights = []
                for x in (x0 + .03, (x0 + x1) / 2, x1 - .03):
                    for y in (y0 + .03, (y0 + y1) / 2, y1 - .03):
                        hit, normal, _, _ = tree.ray_cast(Vector((x, y, low + 0.1)), Vector((0, 0, -1)), 1.0)
                        if hit is None or normal.z < .95:
                            raise ValueError('Atlas hatch requires a flat supported roof')
                        heights.append(hit.z)
                if max(heights) - min(heights) > .001:
                    raise ValueError('Atlas hatch roof is not sufficiently flat')
                shift = sum(heights) / len(heights) - low
                for obj in hatch:
                    obj.location.z += shift
                hatch[0]['RCL_AtlasRoofSeatV1'] = 1
            finally:
                host.to_mesh_clear()
        support_names = [f'AtlasCraneMechLeg_HipActuator_{side}_TipStay' for side in ('L', 'R')]
        existing = [bpy.data.objects.get(name) for name in support_names]
        if any(existing) and not all(existing):
            raise ValueError('Partial Atlas actuator support state')
        if not any(existing):
            mat = bpy.data.objects['AtlasCraneMechLeg_HipActuator_L_OuterBarrel'].active_material
            for obj in add_biped_actuator_supports('AtlasCraneMech', mat):
                side = 'L' if '_L_' in obj.name else 'R'
                obj.vertex_groups.new(name=f'Bone_Thigh_{side}').add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
                modifier = obj.modifiers.new('Armature', 'ARMATURE')
                modifier.object = armature
    finally:
        armature.data.pose_position = original_pose
        bpy.context.view_layer.update()
