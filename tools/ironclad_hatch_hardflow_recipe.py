"""Replay the reviewed Ironclad hatch fit using Blender-native operations."""

import bpy
from mathutils import Matrix


def apply_ironclad_hatch_fit():
    prefix = "IroncladWalkerArmor_AccessHatch_"
    names = ("CoamingRim", "HatchPlate", "HingeBar", "DogLatch")
    objects = [bpy.data.objects.get(prefix + name) for name in names]
    if any(obj is None or obj.type != "MESH" for obj in objects):
        raise ValueError("Ironclad hatch fit requires the complete hatch assembly")
    plate = objects[1]
    if plate.get("RCL_IroncladHatchFit") == 1:
        return
    transform = (Matrix.Translation((0, 0, -0.06)) @
                 Matrix.Diagonal((1, 0.78, 1, 1)) @
                 Matrix.Translation((0, 0.22, 0)))
    for obj in objects:
        obj.matrix_world = transform @ obj.matrix_world
    bpy.context.view_layer.update()

    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0.143, 2.457))
    cutter = bpy.context.object
    cutter.name = "RCL_IroncladHatchLatchCutter"
    cutter.dimensions = (0.135, 0.06, 0.014)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    try:
        modifier = plate.modifiers.new("RCL_LatchRecess", "BOOLEAN")
        modifier.operation = "DIFFERENCE"
        modifier.solver = "EXACT"
        modifier.object = cutter
        plate.modifiers.move(len(plate.modifiers) - 1, 0)
        bpy.ops.object.select_all(action="DESELECT")
        plate.select_set(True)
        bpy.context.view_layer.objects.active = plate
        with bpy.context.temp_override(object=plate, active_object=plate,
                                       selected_objects=[plate], selected_editable_objects=[plate]):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
    finally:
        bpy.data.objects.remove(cutter, do_unlink=True)
    plate["RCL_IroncladHatchFit"] = 1
