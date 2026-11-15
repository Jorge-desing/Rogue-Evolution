"""Connect Ironclad hip actuator clevises to the rigid thigh armor."""
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import RogueModelKit as rmk


def add_biped_actuator_supports(unit_name, material):
    """Seat each eye on the evaluated thigh surface before rigging and batching."""
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    supports = []
    for side in ("L", "R"):
        eye = bpy.data.objects.get(f"{unit_name}Leg_HipActuator_{side}_ClevisEye_Tip")
        armor = bpy.data.objects.get(f"{unit_name}Leg_ThighArmor_{side}")
        if eye is None or armor is None:
            raise ValueError(f"Missing biped actuator or thigh armor: {side}")
        evaluated = armor.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            vertices = [evaluated.matrix_world @ v.co for v in mesh.vertices]
            tree = BVHTree.FromPolygons(vertices, [tuple(p.vertices) for p in mesh.polygons])
            eye_center = sum((eye.matrix_world @ v.co for v in eye.data.vertices), Vector()) / len(eye.data.vertices)
            hit = tree.find_nearest(eye_center)
            if hit is None or not 0.01 < hit[3] < 0.25:
                raise ValueError(f"Unexpected biped actuator seat: {side}, {hit}")
            surface = hit[0]
            # Embed only the attachment ends so the short stay meets both hosts.
            direction = (surface - eye_center).normalized()
            support = rmk.make_rod(f"{unit_name}Leg_HipActuator_{side}_TipStay", eye_center, surface + direction * 0.004, 0.025, material, vertices=8)
            supports.append(support)
        finally:
            evaluated.to_mesh_clear()
    return supports


def add_ironclad_actuator_supports(material):
    return add_biped_actuator_supports("IroncladWalker", material)
