import os
from pathlib import Path
import bpy
from mathutils import Vector

def render_unit(fbx_path: str, texture_path: str, card_path: str, preview_path: str, ortho_scale: float = 5.4):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    
    # Import FBX
    print(f"Importing {fbx_path}...")
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    
    # Load texture
    print(f"Loading texture {texture_path}...")
    img = bpy.data.images.load(str(texture_path))
    
    # Create material with image texture
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
    
    # Assign material to all mesh objects
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            obj.data.materials.clear()
            obj.data.materials.append(mat)
            
    # Camera
    target = Vector((0.0, 0.0, 0.75))
    bpy.ops.object.camera_add(location=(3.8, -4.5, 3.2))
    card_cam = bpy.context.object
    card_cam.name = "TacticalCardCamera"
    card_cam.data.type = "ORTHO"
    card_cam.data.ortho_scale = ortho_scale
    card_cam.rotation_euler = (target - card_cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = card_cam

    # Backdrop plane
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

    # Lighting
    lights = [
        ("CardKey", (3.0, -3.5, 4.5), 900, 3.0),
        ("CardRim", (-3.5, 3.5, 4.0), 1200, 2.5),
        ("CardFill", (-2.5, -2.0, 2.0), 400, 3.0),
    ]
    for name, pos, energy, size in lights:
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

    # Render Preview (512x512)
    os.makedirs(os.path.dirname(os.path.abspath(preview_path)), exist_ok=True)
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.filepath = str(preview_path)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered preview: {preview_path}")

    # Render Card (128x128)
    os.makedirs(os.path.dirname(os.path.abspath(card_path)), exist_ok=True)
    scene.render.resolution_x = 128
    scene.render.resolution_y = 128
    scene.render.filepath = str(card_path)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered card: {card_path}")

def main():
    repo_root = Path(__file__).resolve().parent.parent

    # 1. Salvage Hauler
    salvage_fbx = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/SalvageHauler.fbx"
    salvage_tex = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/Textures/SalvageHauler_Albedo.png"
    salvage_card = repo_root / "unity/RogueCommandAssetAuthoring/Assets/TacticalOperations/SalvageHaulerCard.png"
    salvage_card_sample = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/SalvageHaulerCard.png"
    salvage_preview = repo_root / "artifacts/previews/SalvageHauler_Updated_Preview.png"

    render_unit(str(salvage_fbx), str(salvage_tex), str(salvage_card), str(salvage_preview), ortho_scale=5.4)
    # Also copy to sample folder if needed
    if salvage_card.exists():
        import shutil
        shutil.copy2(str(salvage_card), str(salvage_card_sample))

    # 2. Skyguard SAM
    skyguard_fbx = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/SkyguardSAM.fbx"
    skyguard_tex = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/Textures/SkyguardSAM_Albedo.png"
    skyguard_card = repo_root / "unity/RogueCommandAssetAuthoring/Assets/TacticalOperations/SkyguardSAMCard.png"
    skyguard_card_sample = repo_root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/SkyguardSAMCard.png"
    skyguard_preview = repo_root / "artifacts/previews/SkyguardSAM_Updated_Preview.png"

    render_unit(str(skyguard_fbx), str(skyguard_tex), str(skyguard_card), str(skyguard_preview), ortho_scale=5.4)
    if skyguard_card.exists():
        import shutil
        shutil.copy2(str(skyguard_card), str(skyguard_card_sample))

if __name__ == "__main__":
    main()
