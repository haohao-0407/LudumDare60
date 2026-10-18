"""Validation, safe snapshot exports and presentation for the finished cabin."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE=Path(__file__).resolve().parent

def validate():
    scene=bpy.data.scenes['CompactCockpit_WallIntegrated'];bpy.context.window.scene=scene
    dg=bpy.context.evaluated_depsgraph_get();verts=[];faces=[];opaque_faces=[];meshes=[];tris=0
    shell_groups=('01_','02_','03_','04_','05_','06_')
    for o in scene.objects:
        if o.type not in {'MESH','CURVE','FONT'}:continue
        e=o.evaluated_get(dg);me=e.to_mesh();me.calc_loop_triangles();tris+=len(me.loop_triangles);meshes.append(o.name)
        if o.parent and o.parent.name.startswith(shell_groups):
            base=len(verts);verts.extend([o.matrix_world@v.co for v in me.vertices])
            object_faces=[tuple(base+i for i in p.vertices) for p in me.polygons]
            faces.extend(object_faces)
            if not (o.name.startswith('PortWindow_') and o.name.endswith('_Glass')):opaque_faces.extend(object_faces)
        e.to_mesh_clear()
    tree=BVHTree.FromPolygons(verts,faces);miss=[];rays=0
    opaque_tree=BVHTree.FromPolygons(verts,opaque_faces)
    windows=[]
    for glass in scene.objects:
        if not (glass.name.startswith('PortWindow_') and glass.name.endswith('_Glass')):continue
        center=sum((glass.matrix_world@Vector(corner) for corner in glass.bound_box),Vector())/8
        side=1 if center.x>0 else -1;direction=Vector((side,0,0))
        offsets=[(0,0)]+[(r*math.cos(i*math.tau/16),r*math.sin(i*math.tau/16)) for r in (.10,.205) for i in range(16)]
        blocked=0;glazed=0
        for dy,dz in offsets:
            origin=Vector((side*.8,center.y+dy,center.z+dz))
            blocked+=opaque_tree.ray_cast(origin,direction,1)[0] is not None
            glazed+=tree.ray_cast(origin,direction,1)[0] is not None
        windows.append({'name':glass.name,'center':list(center),'clear_diameter_metres':.46,'sample_rays':len(offsets),'opaque_obstructions':blocked,'glass_hits':glazed})
    for origin in [Vector((0,-.62,1.2)),Vector((-.35,-.58,.55)),Vector((.35,-.48,1.62))]:
        for i in range(1200):
            z=1-2*(i+.5)/1200;a=i*math.pi*(3-math.sqrt(5));q=math.sqrt(1-z*z);d=Vector((q*math.cos(a),q*math.sin(a),z));rays+=1
            if tree.ray_cast(origin,d,10)[0] is None:miss.append({'origin':list(origin),'direction':list(d)})
    imgs=set()
    for o in scene.objects:
        if o.type=='MESH' and o.name.startswith('SCREEN_'):
            for mat in o.data.materials:
                if mat and mat.use_nodes:
                    imgs.update(n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image)
    view_checks=[];eye=Vector((0,-.48,1.44))
    rc=bpy.app.driver_namespace.get('cockpit_wall')
    if rc:
        for sp in rc.screen_specs:
            direction=(eye-Vector(sp['center'])).normalized();normal=Vector(sp['normal']).normalized()
            view_checks.append({'screen':sp['name'],'off_axis_degrees':math.degrees(math.acos(max(-1,min(1,normal.dot(direction)))) )})
    # Sample the usable part of each display from the authored pilot eye.
    visibility={}
    for o in scene.objects:
        if o.type!='MESH' or not o.name.startswith('SCREEN_'):continue
        blocked=[];tested=0
        for row in [6,11,16,21,26]:
            for col in [8,15,22,29,36]:
                target=o.matrix_world@o.data.vertices[row*45+col].co
                direction=target-eye;distance=direction.length;direction.normalize()
                hit,point,normal,face,obj,mat=scene.ray_cast(dg,eye,direction,distance=distance+.01)
                tested+=1
                if not hit or obj!=o:blocked.append(obj.name if hit else 'MISS')
        visibility[o.name]={'sample_count':tested,'occluded_count':len(blocked),'occluders':sorted(set(blocked))}
    report={'scene':scene.name,'units':'metres','interior_nominal':[2.2,2.2,2.1],
        'shape':'circular','interior_diameter_metres':2.2,'pilot_eye_metres':list(eye),'display_view_checks':view_checks,'console_mounting':'Integral wall fairings; no tabletop or legs',
        'objects':len(scene.objects),'renderable_objects':len(meshes),'evaluated_triangles':tris,
        'screen_count':len([o for o in scene.objects if o.name.startswith('SCREEN_')]),
        'screen_visibility_from_pilot':visibility,
        'desk_geometry_remaining':[o.name for o in scene.objects if any(term in o.name for term in ('FlightDesk','DeskPedestal','Desk_','LowerControlDeck'))],
        'closed_enclosure_ray_check':{'rays':rays,'misses':len(miss),'miss_examples':miss[:8]},
        'side_window_apertures':windows,
        'textures_packed':{im.name:bool(im.packed_file) for im in imgs},
        'hidden_geometry':[o.name for o in scene.objects if o.type in {'MESH','CURVE','FONT'} and o.hide_render],
        'scope':'Complete walls, ceiling, floor, flush rear hatch and front cockpit console. No chair or auxiliary furnishing.',
        'source':str(HERE/'CompactCockpit_WallIntegrated.blend')}
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
    out=HERE.parents[1]/'Exports'/'CompactCockpit_WallIntegrated';out.mkdir(parents=True,exist_ok=True)
    try:
        bpy.ops.export_scene.gltf(filepath=str(out/'CompactCockpit_WallIntegrated.glb'),export_format='GLB',use_selection=False,use_active_scene=True,export_apply=True,export_yup=True,export_animations=False,export_cameras=False,export_lights=False)
    finally:
        bpy.context.window.scene=original
        for o in copies:bpy.data.objects.remove(o,do_unlink=True)
        for me in data:
            if me.users==0:bpy.data.meshes.remove(me)
        bpy.data.scenes.remove(export_scene)
    print('Exported '+str(out/'CompactCockpit_WallIntegrated.glb'))

def cutaway():
    scene=bpy.context.scene;before=scene.camera;hidden=[]
    # This is only a presentation view; all enclosure geometry is restored afterwards.
    for o in scene.objects:
        if ((o.parent and o.parent.name.startswith(('03_','05_','06_'))) or (o.type in {'MESH','CURVE','FONT'} and o.parent and o.parent.name.startswith('11_'))):
            hidden.append((o,o.hide_render));o.hide_render=True
    scene.camera=bpy.data.objects['WALL_CAM_Cutaway'];scene.render.film_transparent=True
    scene.render.resolution_x=1600;scene.render.resolution_y=1400;scene.render.resolution_percentage=100
    scene.render.filepath=str(HERE/'Previews'/'Cockpit_Cutaway.png')
    try:bpy.ops.render.render(write_still=True)
    finally:
        for o,state in hidden:o.hide_render=state
        scene.render.film_transparent=False;scene.camera=before;scene.render.resolution_y=1200

def rear():
    scene=bpy.context.scene;before=scene.camera
    scene.camera=bpy.data.objects['WALL_CAM_RearWall'];scene.render.resolution_percentage=75
    scene.render.filepath=str(HERE/'Previews'/'Cockpit_RearInterior.png')
    try:bpy.ops.render.render(write_still=True)
    finally:scene.camera=before;scene.render.resolution_percentage=100


