"""Render 128x128 tactical cards for Arc/Bulwark/FieldMedic (replace PLACEHOLDER)."""
from pathlib import Path
import os
import bpy
from mathutils import Vector


def render_unit(fbx_path: str, texture_path: str, card_path: str, ortho_scale: float = 5.4):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    print(f"Importing {fbx_path}...")
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    print(f"Loading texture {texture_path}...")
    img = bpy.data.images.load(str(texture_path))
    mat = bpy.data.materials.new(name="PBR_Texture_Atlas")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    tex_node = nodes.new(type="ShaderNodeTexImage")
    tex_node.image = img
    links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.5
    bsdf.inputs["Metallic"].default_value = 0.2
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            obj.data.materials.clear()
            obj.data.materials.append(mat)
    target = Vector((0.0, 0.0, 0.75))
    bpy.ops.object.camera_add(location=(3.8, -4.5, 3.2))
    card_cam = bpy.context.object
    card_cam.name = "TacticalCardCamera"
    card_cam.data.type = "ORTHO"
    card_cam.data.ortho_scale = ortho_scale
    card_cam.rotation_euler = (target - card_cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = card_cam
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0.0, 0.0, -0.05))
    card_floor = bpy.context.object
    card_floor.name = "CardBackdrop"
    card_mat = bpy.data.materials.new(name="CardBackdropMat")
    card_mat.use_nodes = True
    card_bsdf = card_mat.node_tree.nodes.get("Principled BSDF")
    if card_bsdf:
        card_bsdf.inputs["Base Color"].default_value = (0.04, 0.07, 0.09, 1.0)
        card_bsdf.inputs["Roughness"].default_value = 0.95
        card_bsdf.inputs["Metallic"].default_value = 0.0
    card_floor.data.materials.append(card_mat)
    for name, pos, energy, size in [("CardKey", (3.0, -3.5, 4.5), 900, 3.0), ("CardRim", (-3.5, 3.5, 4.0), 1200, 2.5), ("CardFill", (-2.5, -2.0, 2.0), 400, 3.0)]:
        bpy.ops.object.light_add(type="AREA", location=pos)
        l = bpy.context.object
        l.name = name
        l.data.energy = energy
        l.data.shape = "DISK"
        l.data.size = size
        l.rotation_euler = (target - l.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.color = (0.04, 0.06, 0.08)
    os.makedirs(os.path.dirname(os.path.abspath(card_path)), exist_ok=True)
    scene.render.resolution_x = 128
    scene.render.resolution_y = 128
    scene.render.filepath = str(card_path)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered card: {card_path}")


def main():
    repo_root = Path(__file__).resolve().parents[1]
    model_dir = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
    tex_dir = model_dir / "Textures"
    card_dir = repo_root / "unity/RogueCommandAssetAuthoring/Assets/TacticalOperations"
    units = [
        ("ArcLanceWalker", "arclancewalkercard.png"),
        ("BulwarkSiegeTank", "bulwarksiegetankcard.png"),
        ("FieldMedicRover", "fieldmedicrovercard.png"),
    ]
    for name, card_name in units:
        render_unit(str(model_dir / f"{name}.fbx"), str(tex_dir / f"{name}_Albedo.png"), str(card_dir / card_name), ortho_scale=5.4)


if __name__ == "__main__":
    main()
