"""Replay the user-authorized Bulwark Foundry source and LOD FBX in Blender 4.5."""
import bpy, math, json, pathlib
from mathutils import Vector
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
POLICY = ROOT / 'content/source-art/bulwark-foundry-build-policy.json'
sys.path.insert(0, str(ROOT / 'tools'))
from model_build_policy import authoring_settings
settings = authoring_settings('BulwarkFoundry')
OUT = ROOT / 'artifacts/bulwark-foundry-build/candidate'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def material(name, color, metallic=.0, roughness=.65):
    mat = bpy.data.materials.new(name); mat.diffuse_color = (*color, 1)
    mat.use_nodes=True
    bsdf=mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=(*color,1)
    bsdf.inputs['Metallic'].default_value=metallic
    bsdf.inputs['Roughness'].default_value=roughness
    return mat

armor=material('Tungsten armor',(.19,.22,.25),.7)
dark=material('Gunmetal structure',(.065,.08,.095),.65)
steel=material('Machined hardware',(.31,.33,.34),.8,.42)
amber=material('Amber warning',(.83,.33,.035),.25,.4)
floor=material('Workshop floor',(.105,.115,.12),.35)
parts=[]

def box(name, center, dims, mat, bevel=.04):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    o=bpy.context.object; o.name=name; o.dimensions=dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        m=o.modifiers.new('edge chamfer','BEVEL'); m.width=bevel; m.segments=1
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=m.name)
    o.data.materials.append(mat); parts.append(o); return o

def loft(name, stations, mat):
    # Each transverse station is (y, x outer, height, inner x), mirrored.
    verts=[]
    for y,outer,height,inner in stations:
        verts.extend([(-outer,y,.2),(-outer,y,height-.35),(-outer+.45,y,height),
                      (-inner,y,height),(-inner,y,.2),
                      (inner,y,.2),(inner,y,height),(outer-.45,y,height),
                      (outer,y,height-.35),(outer,y,.2)])
    faces=[]
    for i in range(len(stations)-1):
        a=i*10; b=(i+1)*10
        for j in range(10):
            k=(j+1)%10
            if j==4: continue  # central bay remains open
            faces.append((a+j,b+j,b+k,a+k))
    # End caps for each left/right wing.
    for base,reverse in ((0,True),((len(stations)-1)*10,False)):
        for ids in ((0,1,2,3,4),(5,6,7,8,9)):
            seq=[base+i for i in ids]; faces.append(tuple(reversed(seq)) if reverse else tuple(seq))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(o); o.data.materials.append(mat); parts.append(o)
    return o

# Broad planted floor and two faceted, load-bearing side wings.
box('Foundry foundation',(0,0,.12),(12,12,.24),dark,.08)
box('Central assembly floor',(0,0,.255),(6.25,11.5,.05),floor,.01)
loft('Tapered armor wings',[(-5.5,5.55,3.0,3.12),(-3.7,5.85,3.35,3.12),(3.8,5.85,3.35,3.12),(5.45,5.4,2.9,3.12)],armor)
# Main roof bridge and supported ribs retain a readable drive-through central bay.
for y in (-3.35,-1.1,1.1,3.35):
    box('Portal beam', (0,y,4.30), (7.9,.42,.48),dark)
    for side in (-1,1):
        box('Portal bearing', (side*3.65,y,2.13), (.36,.43,3.86),dark)
for side in (-1,1):
    box('Crane runway girder',(side*3.25,0,3.475),(.26,7.4,.25),steel)
    box('Crane runway track',(side*3.25,0,3.625),(.09,7.4,.05),dark,.01)
box('Crane bridge',(0,.8,3.81),(6.65,.35,.32),steel)
box('Crane trolley',(.65,.8,3.53),(.65,.58,.24),dark)
box('Hoist cable',(.65,.8,3.05),(.045,.045,.75),steel,.005)
box('Hoist block',(.65,.8,2.64),(.32,.26,.18),amber,.025)
# Roof armor segmented at the edges; central opening exposes the crane.
for side in (-1,1):
    box('Upper wing cap',(side*4.25,0,3.35),(2.12,7.4,.16),armor)
    box('Exterior sill',(side*5.8,0,.45),(.18,9.5,.4),dark)
    for y in (-2.775,-.925,.925,2.775):
        box('Side armor',(side*5.84,y,1.75),(.16,1.65,1.68),armor)
        box('Side armor foot',(side*5.96,y,.92),(.17,1.6,.12),steel,.02)
    # Surface-mounted service door and frame with positive wall contact.
    box('Service door frame',(side*5.98,-2.775,1.35),(.16,1.36,1.85),dark)
    box('Service door',(side*6.075,-2.775,1.35),(.05,1.12,1.62),steel,.015)
    box('Door handle',(side*6.12,-2.435,1.28),(.07,.12,.08),amber,.01)
    # A compact screened ventilation plenum on a wing cap.
    box('Vent plenum',(side*4.2,1.0,3.54),(1.5,1.55,.23),dark)
    for i in range(5):
        box('Vent louver',(side*4.2,.43+i*.28,3.68),(1.22,.12,.07),steel,.01)
    # Floor guide rails and low contrast amber safety beacons.
    box('Track guide',(side*1.65,0,.31),(.10,10.6,.06),steel,.01)
    for y in (-3.35,3.35): box('Warning lamp',(side*3.65,y,4.59),(.16,.2,.1),amber,.015)

# Fit checks use exact design bounds. Structural rail supports stay within the
# level roof section, while the moving bridge clears the fixed portal beams and
# posts. These checks are not a substitute for the in-game footprint census.
assert 7.4 / 2 <= 3.7
assert 4.30 - .48 / 2 > 3.81 + .32 / 2
assert 3.65 - .36 / 2 > 6.65 / 2
assert 3.65 - .36 / 2 > 3.25 + .26 / 2
assert 2.775 + 1.65 / 2 < 3.7  # side panels stay on the flat wing section
assert 2.775 + 1.36 / 2 < 3.7  # service door frame stays on its armor panel

import hashlib
import RogueModelKit as rmk

# Material Maker's emission mask is confined to the middle of the texture.
# Ordinary parts use the lower-left atlas quadrant; warning lenses alone use
# the measured amber mask. Keep UV triangles non-degenerate for tangent import.
for obj in parts:
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.015)
    bpy.ops.object.mode_set(mode='OBJECT')
    is_lamp = obj.name.startswith('Warning lamp')
    for uv in obj.data.uv_layers.active.data:
        if is_lamp:
            uv.uv.x = .552 + uv.uv.x * .002
            uv.uv.y = .494 + uv.uv.y * .002
        else:
            uv.uv.x *= .40; uv.uv.y *= .40
rmk.bake_advanced_vertex_shading(parts, team_mask_materials=[armor], soot_intensity=0.0)

OUT.mkdir(parents=True, exist_ok=True)
source = OUT / 'BulwarkFoundry.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(source))
lods = {}
for level in range(3):
    selection = []
    for obj in parts:
        if level >= 1 and obj.name.startswith(('Door handle','Warning lamp','Vent louver')):
            continue
        if level >= 2 and obj.name.startswith(('Side armor foot','Track guide','Crane runway track','Service door')):
            continue
        duplicate=obj.copy(); duplicate.data=obj.data.copy()
        duplicate.name = f'LOD{level}_' + obj.name
        bpy.context.collection.objects.link(duplicate); selection.append(duplicate)
    lods[f'LOD{level}'] = selection

combined=[obj for name in ('LOD0','LOD1','LOD2') for obj in lods[name]]
fbx=OUT / 'BulwarkFoundry_LODs.fbx'
rmk.export_unity_fbx(fbx, combined, include_empties=False, asset_id='BulwarkFoundry', allow_uncalibrated_export=True)
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
report={
    'schemaVersion':1, 'assetId':'BulwarkFoundry',
    'geometryBudgetStatus':'UNCALIBRATED_USER_AUTHORIZED',
    'policySha256':digest(POLICY) if POLICY.is_file() else None, 'sourceSha256':digest(source), 'fbxSha256':digest(fbx),
    'lodScreenRelativeTransitionHeights':[lod['screenRelativeTransitionHeight'] for lod in settings['lods']],
    'lods': {name:{'objects':len(objects),'blenderFaces':sum(len(o.data.polygons) for o in objects),
                   'triangles':rmk.count_mesh_triangles(objects)} for name,objects in lods.items()},
    'limitations':['Native bounds and runtime appearance unverified.','Not performance-calibrated.']
}
(OUT / 'geometry-report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('FOUNDRY_GEOMETRY_BUILT', json.dumps(report))
