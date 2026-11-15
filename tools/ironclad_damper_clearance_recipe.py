"""Cut shin clearances for matching biped dampers before rigid skinning."""
import bpy
import numpy as np


def cut_biped_damper_clearance(unit_name):
    hosts = [bpy.data.objects.get(f"{unit_name}Leg_{part}_{side}")
             for side in ("L", "R") for part in ("ShinArmor", "CalfArmor", "ShinBone")]
    if any(host is None or host.type != "MESH" for host in hosts):
        raise ValueError(f"Missing {unit_name} shin clearance hosts")
    marked = [bool(host.get("RCL_DamperClearanceV1")) for host in hosts]
    if all(marked):
        return
    if any(marked):
        raise ValueError(f"Partial {unit_name} damper clearance state")
    bpy.context.view_layer.update()
    for side in ("L", "R"):
        for tag in ("L", "R"):
            damper = bpy.data.objects.get(f"{unit_name}Leg_AnkleDamper_{side}_{tag}")
            if damper is None or damper.type != "MESH":
                raise ValueError(f"Missing biped damper {side}/{tag}")
            cutter = damper.copy()
            cutter.data = damper.data.copy()
            bpy.context.scene.collection.objects.link(cutter)
            try:
                for modifier in list(cutter.modifiers):
                    cutter.modifiers.remove(modifier)
                points = np.array([v.co[:] for v in cutter.data.vertices])
                center = points.mean(axis=0)
                _, axes = np.linalg.eigh(np.cov((points - center).T))
                axis = axes[:, -1]
                for vertex in cutter.data.vertices:
                    point = np.array(vertex.co[:]) - center
                    axial = axis * np.dot(point, axis)
                    vertex.co = center + 1.04 * axial + 1.30 * (point - axial)
                for part in ("ShinArmor", "CalfArmor", "ShinBone"):
                    host = bpy.data.objects.get(f"{unit_name}Leg_{part}_{side}")
                    if host is None or host.type != "MESH":
                        raise ValueError(f"Missing biped {part} {side}")
                    modifier = host.modifiers.new("RCL_DamperClearance", "BOOLEAN")
                    modifier.operation = "DIFFERENCE"
                    modifier.solver = "EXACT"
                    modifier.object = cutter
                    host.modifiers.move(len(host.modifiers) - 1, 0)
                    bpy.ops.object.select_all(action="DESELECT")
                    host.select_set(True)
                    bpy.context.view_layer.objects.active = host
                    with bpy.context.temp_override(object=host, active_object=host,
                                                   selected_objects=[host], selected_editable_objects=[host]):
                        bpy.ops.object.modifier_apply(modifier=modifier.name)
            finally:
                bpy.data.objects.remove(cutter, do_unlink=True)
    for host in hosts:
        host["RCL_DamperClearanceV1"] = 1


def cut_ironclad_damper_clearance():
    cut_biped_damper_clearance("IroncladWalker")
