"""Blender presentation setup; no terrain and no change to the architecture export."""
import bpy
import math
from pathlib import Path
from mathutils import Vector

source = Path(r'D:\unity-projects\LudumDare60\SourceArt\MarsBase')
scene = bpy.data.scenes['MarsBase_RetroFuture']
bpy.context.window.scene = scene
collection = bpy.data.collections.get('_Presentation_Surface')
if collection is None:
    collection = bpy.data.collections.new('_Presentation_Surface')
    scene.collection.children.link(collection)
    mesh = bpy.data.meshes.new('Flat presentation surface')
    mesh.from_pydata([(-400,-400,-.08),(400,-400,-.08),(400,400,-.08),(-400,400,-.08)], [], [(0,1,2,3)])
    floor = bpy.data.objects.new('Presentation floor - no terrain',mesh)
    collection.objects.link(floor)
    mat = bpy.data.materials.new('Mars_PresentationSand')
    mat.use_nodes = True
    nodes=mat.node_tree.nodes
    p=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=(.23,.085,.035,1)
    p.inputs['Roughness'].default_value=.93
    noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=85
    coord=nodes.new('ShaderNodeTexCoord')
    bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.22;bump.inputs['Distance'].default_value=.05
    mat.node_tree.links.new(coord.outputs['Object'],noise.inputs['Vector'])
    mat.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height'])
    mat.node_tree.links.new(bump.outputs['Normal'],p.inputs['Normal'])
    mesh.materials.append(mat)
sun=bpy.data.objects.get('Mars_Presentation_Sun')
if sun is None:
    assert 'SUN' in [v.identifier for v in bpy.types.Light.bl_rna.properties['type'].enum_items]
    light=bpy.data.lights.new('Mars_Presentation_Sun','SUN')
    light.energy=2.6;light.angle=math.radians(3);light.color=(1,.73,.48)
    sun=bpy.data.objects.new('Mars_Presentation_Sun',light);collection.objects.link(sun)
sun.rotation_euler=(math.radians(62),math.radians(-15),math.radians(-28))
scene.camera.location=(50,-80,28)
scene.camera.rotation_euler=(Vector((0,1,7))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera.data.lens=43
scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
scene.render.filepath=str(source/'MarsBase_Blender_Preview.png')
for o in scene.objects:o.select_set(False)
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        space=area.spaces.active
        allowed=[e.identifier for e in space.region_3d.bl_rna.properties['view_perspective'].enum_items]
        if 'CAMERA' in allowed:space.region_3d.view_perspective='CAMERA'
        space.region_3d.view_camera_zoom=8
        space.overlay.show_overlays=False
bpy.ops.wm.save_as_mainfile(filepath=str(source/'MarsBase_RetroFuture.blend'))
print('Presentation saved; render path:',scene.render.filepath)
