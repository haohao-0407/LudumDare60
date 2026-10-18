"""Validation, safe snapshot exports and presentation for the finished cabin."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE=Path(__file__).resolve().parent

def validate():
    scene=bpy.data.scenes['CompactCockpit'];bpy.context.window.scene=scene
    dg=bpy.context.evaluated_depsgraph_get();verts=[];faces=[];meshes=[];tris=0
    shell_groups=('01_','02_','03_','04_','05_','06_')
    for o in scene.objects:
        if o.type not in {'MESH','CURVE','FONT'}:continue
        e=o.evaluated_get(dg);me=e.to_mesh();me.calc_loop_triangles();tris+=len(me.loop_triangles);meshes.append(o.name)
        if o.parent and o.parent.name.startswith(shell_groups):
            base=len(verts);verts.extend([o.matrix_world@v.co for v in me.vertices]);faces.extend([tuple(base+i for i in p.vertices) for p in me.polygons])
        e.to_mesh_clear()
    tree=BVHTree.FromPolygons(verts,faces);miss=[];rays=0
    for origin in [Vector((0,-.62,1.2)),Vector((-.35,-.58,.55)),Vector((.35,-.48,1.62))]:
        for i in range(1200):
            z=1-2*(i+.5)/1200;a=i*math.pi*(3-math.sqrt(5));q=math.sqrt(1-z*z);d=Vector((q*math.cos(a),q*math.sin(a),z));rays+=1
            if tree.ray_cast(origin,d,10)[0] is None:miss.append({'origin':list(origin),'direction':list(d)})
    imgs=[im for im in bpy.data.images if im.name in ['radar.png','attitude.png','navigation.png','comms.png','systems.png']]
    report={'scene':scene.name,'units':'metres','interior_nominal':[1.9,2.2,2.1],
        'rear_free_floor_nominal':[1.86,.86],'floor_to_console_underside':.67,
        'objects':len(scene.objects),'renderable_objects':len(meshes),'evaluated_triangles':tris,
        'screen_count':len([o for o in scene.objects if o.name.startswith('SCREEN_')]),
        'closed_enclosure_ray_check':{'rays':rays,'misses':len(miss),'miss_examples':miss[:8]},
        'textures_packed':{im.name:bool(im.packed_file) for im in imgs},
        'hidden_geometry':[o.name for o in scene.objects if o.type in {'MESH','CURVE','FONT'} and o.hide_render],
        'scope':'Complete walls, ceiling, floor, flush rear hatch and front cockpit console. No chair or auxiliary furnishing.',
        'source':str(HERE/'CompactCockpit.blend')}
    (HERE/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False))
    return report

def export_glb():
    original=bpy.context.window.scene
    export_scene=bpy.data.scenes.new('_CockpitExportTemporary');bpy.context.window.scene=original
    dg=bpy.context.evaluated_depsgraph_get();copies=[];data=[]
    # Bake only mesh/curve/text geometry into a separate scene; original remains editable.
    for o in original.objects:
        if o.type not in {'MESH','CURVE','FONT'}:continue
        me=bpy.data.meshes.new_from_object(o.evaluated_get(dg),preserve_all_data_layers=True,depsgraph=dg)
        co=bpy.data.objects.new(o.name,me);export_scene.collection.objects.link(co);co.matrix_world=o.matrix_world.copy();copies.append(co);data.append(me)
    bpy.context.window.scene=export_scene
    out=HERE.parents[1]/'Exports'/'CompactCockpit';out.mkdir(parents=True,exist_ok=True)
    try:
        bpy.ops.export_scene.gltf(filepath=str(out/'CompactCockpit.glb'),export_format='GLB',use_selection=False,use_active_scene=True,export_apply=True,export_yup=True,export_animations=False,export_cameras=False,export_lights=False)
    finally:
        bpy.context.window.scene=original
        for o in copies:bpy.data.objects.remove(o,do_unlink=True)
        for me in data:
            if me.users==0:bpy.data.meshes.remove(me)
        bpy.data.scenes.remove(export_scene)
    print('Exported '+str(out/'CompactCockpit.glb'))

def cutaway():
    scene=bpy.context.scene;before=scene.camera;hidden=[]
    # This is only a presentation view; all enclosure geometry is restored afterwards.
    for o in scene.objects:
        if ((o.parent and o.parent.name.startswith(('03_','05_','06_'))) or (o.type in {'MESH','CURVE','FONT'} and o.parent and o.parent.name.startswith('11_'))):
            hidden.append((o,o.hide_render));o.hide_render=True
    scene.camera=bpy.data.objects['CAM_Cutaway'];scene.render.film_transparent=True
    scene.render.resolution_x=1600;scene.render.resolution_y=1400;scene.render.resolution_percentage=100
    scene.render.filepath=str(HERE/'Previews'/'Cockpit_Cutaway.png')
    try:bpy.ops.render.render(write_still=True)
    finally:
        for o,state in hidden:o.hide_render=state
        scene.render.film_transparent=False;scene.camera=before;scene.render.resolution_y=1200

def rear():
    scene=bpy.context.scene;before=scene.camera
    scene.camera=bpy.data.objects['CAM_RearWall'];scene.render.resolution_percentage=75
    scene.render.filepath=str(HERE/'Previews'/'Cockpit_RearInterior.png')
    try:bpy.ops.render.render(write_still=True)
    finally:scene.camera=before;scene.render.resolution_percentage=100
