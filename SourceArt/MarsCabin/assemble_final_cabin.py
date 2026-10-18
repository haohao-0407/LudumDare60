"""Complete the authored cabin with THREE decorative Meshy PBR assets only.

Critical controls, displays and supports are from build_hero_console.py. This
assembly intentionally excludes generated navigation/throttle/joystick devices.
Exact verified Unity decorative geometry/UVs are used to remove axis ambiguity.
"""
import bpy, os, json
from mathutils import Vector

ROOT=r'D:\unity-projects\LudumDare60'
SOURCE=os.path.join(ROOT,'SourceArt','MarsCabin')
DECORATIVE=('PilotSeat_T25','CommsRack_T25','ReelRecorder_T25')


def assemble_decorations():
    room=bpy.data.scenes['MarsCabin_Interior'];bpy.context.window.scene=room
    manifest=json.load(open(os.path.join(SOURCE,'unity_assembly_geometry.json'),encoding='utf-8'))
    staged=bpy.data.collections.new('Meshy_Decorative_Originals_Hidden');room.collection.children.link(staged)
    final=bpy.data.collections.new('Meshy_Decorative_Only');room.collection.children.link(final)
    reports=[]
    for name in DECORATIVE:
        path=os.path.join(ROOT,'Assets','MarsCabin','Models','Meshy',name+'.glb')
        before=set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        imported=[o for o in bpy.data.objects if o not in before]
        imported_materials=[]
        for obj in imported:
            if obj.type=='MESH':
                for mat in obj.data.materials:
                    if mat and mat not in imported_materials:imported_materials.append(mat)
            for col in list(obj.users_collection):col.objects.unlink(obj)
            staged.objects.link(obj);obj.name='Source_'+name+'_'+obj.name
        if not imported_materials:raise RuntimeError('Missing imported PBR for '+name)
        entries=[m for m in manifest['meshes'] if m['root']==name and m['name']=='Meshy_T25_OriginalPBR']
        if len(entries)!=1:raise RuntimeError('Expected exactly one verified decorative mesh for '+name)
        entry=entries[0];faces=[];slots=[]
        for sub in entry['submeshes']:
            tri=sub['triangles'];faces.extend(tuple(tri[i:i+3]) for i in range(0,len(tri),3));slots += [sub['material']]*(len(tri)//3)
        data=bpy.data.meshes.new('Decor_'+name+'_PBR');data.from_pydata(entry['vertices'],[],faces);data.update()
        for mat in imported_materials:data.materials.append(mat)
        for p,slot in zip(data.polygons,slots):p.material_index=min(slot,len(imported_materials)-1);p.use_smooth=True
        uv=data.uv_layers.new(name='UVMap')
        for loop in data.loops:uv.data[loop.index].uv=entry['uv'][loop.vertex_index]
        setter=getattr(data,'normals_split_custom_set_from_vertices',None)
        if setter and len(entry['normals'])==len(data.vertices):setter(entry['normals'])
        obj=bpy.data.objects.new('Decor_'+name,data);final.objects.link(obj)
        obj['role']='Background / decorative Meshy T2.5 asset; no operational controls'
        obj['source_glb']=path;obj['placement']='Exact verified Unity world geometry converted to Blender world metres'
        pts=[Vector(v) for v in entry['vertices']]
        reports.append({'name':obj.name,'source':path,'triangles':len(faces),'materialCount':len(imported_materials),'boundsMin':[min(v[k] for v in pts) for k in range(3)],'boundsMax':[max(v[k] for v in pts) for k in range(3)]})
    staged.hide_render=True;staged.hide_viewport=True
    for obj in room.objects:obj.select_set(False)
    # Pack every image referenced by retained Blender materials into the source.
    packed=0
    for image in bpy.data.images:
        if image.source in {'FILE','GENERATED'} and image.size[0]>0:
            try:image.pack();packed+=1
            except RuntimeError:pass
    result={'decorativeOnly':list(DECORATIVE),'generatedOperationalDevicesPresent':False,'decorations':reports,'packedImages':packed,'criticalEquipment':'Cabin_HeroConsole: fully authored Blender geometry with separate controls and CRT glass'}
    with open(os.path.join(SOURCE,'final_blender_assembly_manifest.json'),'w',encoding='utf-8') as f:json.dump(result,f,ensure_ascii=False,indent=2)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'MarsCabin_Interior.blend'))
    print('DECORATIVE_ASSEMBLY_COMPLETE',json.dumps(result))


if __name__=='__main__':assemble_decorations()
