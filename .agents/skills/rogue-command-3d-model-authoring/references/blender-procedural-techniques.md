# Blender Procedural Modeling Techniques for Rogue Command

This reference provides tested Python (`bpy`) recipes and geometric algorithms to generate high-quality, production-ready low-poly vehicle models deterministically in Blender 4.5.14 LTS.

---

## 1. Coordinate Systems & Export Invariants

Blender and Unity use different coordinate handedness. To avoid flipped meshes, upside-down turrets, or inverted steering:

| Property | Blender World | Unity World (Imported Asset) |
|---|---|---|
| **Forward Direction** | `+Y` (Front of vehicle faces `+Y`) | `+Z` (Front of vehicle faces `+Z`) |
| **Up Direction** | `+Z` (Roof of vehicle faces `+Z`) | `+Y` (Roof of vehicle faces `+Y`) |
| **Right Direction** | `+X` (Starboard / Right side faces `+X`) | `+X` (Right side faces `+X`) |
| **Ground Plane** | `Z = 0.0` (Treads/wheels rest on `Z = 0.0`) | `Y = 0.0` (Treads/wheels rest on `Y = 0.0`) |
| **Vehicle Center** | `(0.0, 0.0, 0.0)` at ground center | Local origin `(0.0, 0.0, 0.0)` at identity |

### FBX Export Configuration

Always invoke `bpy.ops.export_scene.fbx` with these exact parameters:

```python
bpy.ops.export_scene.fbx(
    filepath=str(fbx_path),
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    use_mesh_modifiers=True,
    mesh_smooth_type="OFF",          # Flat shading preserved
    path_mode="AUTO",
    axis_forward="-Z",               # Maps Blender +Y to Unity +Z
    axis_up="Y",                     # Maps Blender +Z to Unity +Y
    bake_space_transform=False,
    add_leaf_bones=False,
    use_custom_props=False
)
```

---

## 2. Core Geometric Helper Functions

### A. Chamfered Box (Machined Hard-Surface Block)

```python
import bpy
import math
from mathutils import Vector

def finish_mesh(obj, material, bevel=0.0):
    """Assigns material, enforces flat shading, and applies 1-segment bevel."""
    if material:
        obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    if bevel > 0.0:
        modifier = obj.modifiers.new("machined chamfers", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = math.radians(35)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        for polygon in obj.data.polygons:
            polygon.use_smooth = False
    obj.select_set(False)
    return obj

def make_box(name, size, location, material, bevel=0.025, rotation=(0.0, 0.0, 0.0)):
    """Creates a dimensionally accurate box with baked scale and optional bevel."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish_mesh(obj, material, bevel)
```

### B. Stepped Cylinder (Barrels, Piston Arms, Axles)

```python
def make_cylinder(name, radius, depth, location, material, rotation=(0.0, 0.0, 0.0), vertices=12, bevel=None):
    """Creates a low-poly cylinder with proportional chamfers on rims."""
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel is None:
        bevel = min(radius * 0.16, depth * 0.14)
    return finish_mesh(obj, material, bevel)
```

### C. Sloped Armored Glacis (Wedge Chassis)

```python
def make_sloped_hull(name, length_front, length_rear, width_front, width_rear,
                     height_lower, height_upper, material, bevel=0.045):
    """Creates a faceted, sloped glacis tank chassis with angled lower hull."""
    hw_f = width_front * 0.5
    hw_r = width_rear * 0.5
    vertices = [
        # Lower hull (ground clearance z = height_lower)
        (-hw_r * 0.85, -length_rear, height_lower),
        ( hw_r * 0.85, -length_rear, height_lower),
        ( hw_f * 0.85,  length_front * 0.7, height_lower),
        (-hw_f * 0.85,  length_front * 0.7, height_lower),
        # Upper hull deck (z = height_upper)
        (-hw_r, -length_rear * 0.9, height_upper),
        ( hw_r, -length_rear * 0.9, height_upper),
        ( hw_f,  length_front, height_upper),
        (-hw_f,  length_front, height_upper),
    ]
    faces = [
        (3, 2, 1, 0),  # Bottom
        (4, 5, 6, 7),  # Top Deck
        (0, 4, 7, 3),  # Left Side
        (1, 2, 6, 5),  # Right Side
        (0, 1, 5, 4),  # Rear
        (3, 7, 6, 2),  # Front Glacis Plate
    ]
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return finish_mesh(obj, material, bevel)
```

---

## 3. High-Fidelity Propulsion Recipes

### A. Continuous Track Loop Assembly

A flat rectangular box for tank treads looks cheap and unconvincing. Use an angled front climb and rear sprocket contour:

```python
def make_continuous_track_belt(name, side, length, height, width, tread_mat):
    """Creates an articulated continuous track belt with sloped front approach."""
    # side: -1 for Left, +1 for Right
    x = side * (0.91)
    hw = width * 0.5
    # Angled front approach gives the tank aggressive forward movement telegraphed visually
    vertices = [
        # Left side vertices (in local belt space)
        (-hw, -length * 0.5, 0.15),         # 0: Rear bottom
        ( hw, -length * 0.5, 0.15),         # 1
        ( hw,  length * 0.35, 0.15),        # 2: Front approach start
        (-hw,  length * 0.35, 0.15),        # 3
        ( hw,  length * 0.52, 0.35),        # 4: Front idler apex
        (-hw,  length * 0.52, 0.35),        # 5
        ( hw,  length * 0.45, height),      # 6: Front top return
        (-hw,  length * 0.45, height),      # 7
        ( hw, -length * 0.48, height),      # 8: Rear top return
        (-hw, -length * 0.48, height),      # 9
        ( hw, -length * 0.52, 0.35),        # 10: Rear sprocket apex
        (-hw, -length * 0.52, 0.35),        # 11
    ]
    # Connect into loop faces
    faces = [
        (3, 2, 1, 0),     # Ground run
        (5, 4, 2, 3),     # Front climb
        (7, 6, 4, 5),     # Front over-curve
        (9, 8, 6, 7),     # Top return run
        (11, 10, 8, 9),   # Rear over-curve
        (0, 1, 10, 11),   # Rear under-curve
        # Side covers
        (0, 11, 9, 7, 5, 3), # Outer/Inner side
        (1, 2, 4, 6, 8, 10), # Inner/Outer side
    ]
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = (x, 0.0, 0.0)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return finish_mesh(obj, tread_mat, 0.02)
```

### B. Multi-Component Road Wheels with Hub Step

```python
def make_stepped_roadwheel(prefix, location, radius, thickness, rim_mat, hub_mat, gunmetal_mat):
    """Creates a 3-tier road wheel: outer rim, recessed dish, and center hex hub."""
    objects = []
    # 1. Main outer rim
    objects.append(make_cylinder(
        f"{prefix}_Rim", radius, thickness, location, rim_mat,
        (0.0, math.radians(90), 0.0), vertices=12, bevel=radius * 0.12
    ))
    # 2. Recessed inner dish (both sides)
    sign = 1 if location[0] > 0 else -1
    dish_offset = sign * (thickness * 0.5 + 0.005)
    objects.append(make_cylinder(
        f"{prefix}_Dish", radius * 0.72, 0.015,
        (location[0] + dish_offset, location[1], location[2]), hub_mat,
        (0.0, math.radians(90), 0.0), vertices=10, bevel=0.008
    ))
    # 3. Center axle cap
    cap_offset = sign * (thickness * 0.5 + 0.02)
    objects.append(make_cylinder(
        f"{prefix}_AxleCap", radius * 0.32, 0.03,
        (location[0] + cap_offset, location[1], location[2]), gunmetal_mat,
        (0.0, math.radians(90), 0.0), vertices=8, bevel=0.006
    ))
    return objects
```

---

## 4. Mechanical Detailing & Focal Point Recipes

### A. Slatted Engine Intake Deck

```python
def make_engine_deck_grille(prefix, location, size, slat_count, slat_mat, frame_mat):
    """Creates an armored engine ventilation deck with angled slats."""
    objects = []
    # Recessed frame
    objects.append(make_box(f"{prefix}_Frame", size, location, frame_mat, bevel=0.02))

    # Angled louvers / slats
    slat_spacing = (size[1] * 0.8) / slat_count
    slat_width = size[0] * 0.85
    slat_thickness = size[2] * 0.6
    start_y = location[1] - (size[1] * 0.4) + (slat_spacing * 0.5)

    for i in range(slat_count):
        y = start_y + i * slat_spacing
        objects.append(make_box(
            f"{prefix}_Slat_{i+1}",
            (slat_width, slat_spacing * 0.65, slat_thickness),
            (location[0], y, location[2] + size[2] * 0.35),
            slat_mat, bevel=0.008,
            rotation=(math.radians(-25), 0.0, 0.0)
        ))
    return objects
```

### B. Recessed Optic Sensor Cluster (Glowing Visor)

```python
def make_recessed_sensor_visor(prefix, location, size, armor_mat, sensor_mat, gunmetal_mat):
    """Creates a hooded sensor cluster with deeply recessed glowing lenses."""
    objects = []
    # 1. Armored protective hood / eyebrow
    objects.append(make_box(
        f"{prefix}_Housing", size, location, armor_mat, bevel=0.025
    ))
    # 2. Dark recessed optic cavity
    recess_size = (size[0] * 0.8, size[1] * 0.4, size[2] * 0.6)
    cavity_loc = (location[0], location[1] + size[1] * 0.32, location[2])
    objects.append(make_box(
        f"{prefix}_Cavity", recess_size, cavity_loc, gunmetal_mat, bevel=0.01
    ))
    # 3. Glowing optical lenses (twin or triple slit)
    lens_width = recess_size[0] * 0.35
    for idx, side in enumerate((-1, 1), start=1):
        lens_x = location[0] + side * (lens_width * 0.65)
        objects.append(make_box(
            f"{prefix}_Lens_{idx}",
            (lens_width, 0.02, recess_size[2] * 0.7),
            (lens_x, location[1] + size[1] * 0.45, location[2]),
            sensor_mat, bevel=0.005
        ))
    return objects
```

---

## 5. Preview Studio Setup & Multi-Angle Validation

To visually verify models before exporting FBX:

```python
def setup_preview_studio(model_objects, preview_path):
    """Creates standard 3-point studio lighting and isometric RTS camera."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = str(preview_path)
    scene.world.color = (0.035, 0.045, 0.065)

    # Floor shadow catcher
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0.0, 0.0, -0.015))
    floor = bpy.context.object
    floor.name = "PreviewFloor"
    floor_mat = bpy.data.materials.new("PreviewFloorMat")
    floor_mat.diffuse_color = (0.035, 0.052, 0.065, 1.0)
    floor.data.materials.append(finish_mesh(floor, floor_mat))

    # Camera: Standard RTS orthographic angle (45° azimuth, ~35° elevation)
    bpy.ops.object.camera_add(location=(4.8, 5.8, 3.6))
    camera = bpy.context.object
    camera.name = "RTS_IsometricCamera"
    target = Vector((0.0, 0.0, 0.7))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 5.8
    scene.camera = camera

    # 3-Point Studio Lights
    lights = [
        ("Key_Light", (3.5, 4.5, 6.0), 850, 4.0),
        ("Fill_Light", (-4.2, 1.0, 3.5), 520, 3.5),
        ("Rim_Light", (-1.5, -4.2, 4.5), 980, 3.0),
    ]
    for name, pos, energy, size in lights:
        bpy.ops.object.light_add(type="AREA", location=pos)
        l = bpy.context.object
        l.name = name
        l.data.energy = energy
        l.data.shape = "DISK"
        l.data.size = size
        l.rotation_euler = (Vector((0.0, 0.0, 0.6)) - l.location).to_track_quat("-Z", "Y").to_euler()
```
