"""Single-pilot pressure cabin, authored in Blender in metres. Pilot faces +Y.
Run setup(), shell(), console(), finish() in order. No existing scenes are removed.
"""
import bpy, math, json, os, importlib.util
from pathlib import Path
from mathutils import Vector, Matrix

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('cockpit_geometry',str(HERE/'geometry.py'))
b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
I=Matrix.Identity(3)
FRONT=Matrix.Rotation(math.pi/2,3,'X')
DECK=Matrix.Rotation(math.radians(28),3,'X')
SIDE=Matrix.Rotation(math.radians(63),3,'X')
TAU=math.tau
screen_specs=[]

def panel(name,g,mat,c,sz,r=.02,R=I):
    """Rounded rectangle with an independent planar corner radius and edge bevel."""
    w,h,d=sz;r=min(r,w*.45,h*.45);edge=min(.004,d*.24)
    outline=[]
    for x,y,a in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
        for j in range(7):
            t=math.radians(a+j*15);outline.append((x+r*math.cos(t),y+r*math.sin(t)))
    vs=[]
    for z,shrink in [(-d/2,edge),(-d/2+edge,0),(d/2-edge,0),(d/2,edge)]:
        for x,y in outline:vs.append((x*(1-2*shrink/w),y*(1-2*shrink/h),z))
    n=len(outline);fs=[tuple(reversed(range(n))),tuple(3*n+i for i in range(n))]
    fs += [(k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i) for k in range(3) for i in range(n)]
    if name:b.begin(name,g,c)
    b.mesh(g,mat,b.xf(vs,c,R),fs)
    if name:b.end()

def bolt(g,p,R=I,r=.0045):
    p=Vector(p);n=R.col[2];u=R.col[0]
    b.lathe(g,'Metal',p,[(-.001,r*.8),(0,r),(.0025,r),(.0035,r*.7)],12,n)
    b.cylinder(g,'Dark',p+n*.0037-u*r*.6,p+n*.0037+u*r*.6,.00055,5)

def label(g,t,p,size=.012,R=FRONT,mat='Ink'):
    b.label(g,t,p,size,R,mat)

def pipe(name,g,points,r=.012,mat='Metal'):
    # Interpolated tubular runs with gently rounded joins.
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions=b.enum(curve,'dimensions','3D')
    curve.resolution_u=12;curve.bevel_depth=r;curve.bevel_resolution=3
    s=curve.splines.new('BEZIER');s.bezier_points.add(len(points)-1)
    for pt,co in zip(s.bezier_points,points):
        pt.co=co;pt.handle_left_type=b.enum(pt,'handle_left_type','AUTO');pt.handle_right_type=b.enum(pt,'handle_right_type','AUTO')
    o=bpy.data.objects.new(name,curve);collections[g].objects.link(o);o.parent=b.groups[g]
    curve.materials.append(b.materials[mat]);return o

def setup():
    global scene,collections,root
    scene=bpy.data.scenes.new('CompactCockpit_WallIntegrated');bpy.context.window.scene=scene;b.scene=scene
    scene.unit_settings.system=b.enum(scene.unit_settings,'system','METRIC');scene.unit_settings.scale_length=1
    root=bpy.data.objects.new('COCKPIT_WallIntegrated_D2.2_H2.1m',None);scene.collection.objects.link(root)
    root['interior_metres']=[2.2,2.2,2.1];root['scope']='Complete enclosure and front flight console. Remaining interior intentionally empty.'
    collections={}
    for g in ['01_Floor','02_LeftWall','03_RightWall','04_FrontWall','05_RearWall','06_Ceiling','07_ConsoleBody','08_Displays','09_Controls','10_Fittings','11_Lighting','12_Cameras']:
        co=bpy.data.collections.new(g);scene.collection.children.link(co);collections[g]=co
        o=bpy.data.objects.new(g+'_ROOT',None);co.objects.link(o);o.parent=root;b.groups[g]=o
    for name,color,metal,rough,emit in [
        ('Ivory',(.66,.59,.46),.17,.36,0),('Wall',(.71,.69,.61),.12,.46,0),
        ('Orange',(.56,.072,.009),.24,.33,0),('Metal',(.32,.35,.34),.82,.25,0),
        ('Dark',(.035,.045,.045),.40,.4,0),('Rubber',(.009,.014,.014),.04,.72,0),
        ('Floor',(.15,.18,.18),.42,.5,0),('Ink',(.025,.035,.033),.1,.6,0),
        ('White',(.82,.80,.68),.1,.33,0),('Amber',(1,.32,.025),.05,.3,2.7),
        ('Warm',(1,.65,.29),.02,.35,3.2),('Green',(.05,.75,.24),.05,.3,1.1),
        ('Red',(.55,.025,.008),.1,.4,0)]:
        b.material(name,color,metal,rough,emit)
    # Fine manufacturing texture rather than large noisy grunge.
    for name in ['Ivory','Wall','Orange','Floor']:
        m=b.materials[name];ns=m.node_tree.nodes;ls=m.node_tree.links
        bs=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
        no=ns.new('ShaderNodeTexNoise');no.noise_dimensions=b.enum(no,'noise_dimensions','3D');no.inputs['Scale'].default_value=165;no.inputs['Detail'].default_value=2
        bump=ns.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.15;bump.inputs['Distance'].default_value=.00028
        ls.new(no.outputs['Fac'],bump.inputs['Height']);ls.new(bump.outputs['Normal'],bs.inputs['Normal'])
    print('Created isolated CompactCockpit scene and materials')

def shell():
    sp=importlib.util.spec_from_file_location('round_shell',str(HERE/'circular_shell.py'));module=importlib.util.module_from_spec(sp);sp.loader.exec_module(module)
    import sys
    module.build(sys.modules[__name__])

def button(name,p,R=FRONT,w=.032,h=.028,mat='Amber',caption=''):
    g='09_Controls';p=Vector(p);n=R.col[2]
    panel(None,g,'Metal',p,(w+.009,h+.009,.009),.005,R)
    panel(None,g,'Rubber',p+n*.005,(w+.004,h+.004,.009),.003,R)
    panel(name,g,mat,p+n*.012,(w,h,.013),.004,R)
    if caption:label(g,caption,p+n*.0195,min(w,h)*.29,R)

def knob(name,p,R=FRONT,r=.019,caption=''):
    g='09_Controls';p=Vector(p);n=R.col[2]
    b.lathe(g,'Metal',p,[(-.003,r*1.27),(.002,r*1.27),(.005,r*1.12)],32,n)
    for j in range(15):
        a=math.radians(-135+j*270/14);v=R@Vector((math.sin(a),math.cos(a),0))
        b.cylinder(g,'Ink',p+v*r*1.4+n*.003,p+v*r*(1.64 if j%7==0 else 1.53)+n*.003,.00065,5)
    b.begin(name,g,p)
    b.lathe(g,'Dark',p,[(.003,r*.96),(.006,r),(.027,r),(.033,r*.86)],40,n)
    for j in range(32):
        a=TAU*j/32;v=R@Vector((r*math.cos(a),r*math.sin(a),0))
        b.cylinder(g,'Rubber',p+v+n*.009,p+v+n*.024,.0012,6)
    b.rounded(g,'White',p+R@Vector((0,r*.48,.034)),(.0028,r*.72,.0015),.0004,R)
    b.end()
    if caption:label(g,caption,p+R@Vector((0,-r*2.05,.003)),.008,R)

def screen(name,p,w,h,R,texture):
    g='08_Displays';p=Vector(p);n=R.col[2]
    for suffix,mat,offset,sz,r in [
        ('Body','Dark',-.026,(w+.106,h+.106,.10),.045),
        ('Enamel','Ivory',.022,(w+.10,h+.10,.035),.038),
        ('SafetyOrange','Orange',.043,(w+.062,h+.062,.017),.033),
        ('RubberSeal','Rubber',.055,(w+.019,h+.019,.013),.024)]:
        panel(name+'_'+suffix,g,mat,p+n*offset,sz,r,R)
    pp=p+n*.063;nu,nv=44,32;vs=[];uv=[]
    # Rounded corners mapped into the glass outline, slight CRT crown.
    radius=min(.018,h*.08)
    for j in range(nv+1):
        v=j/nv;y=(v-.5)*h
        edge=max(abs(y)-(h/2-radius),0);half=w/2-radius+math.sqrt(max(0,radius**2-edge**2))
        for i in range(nu+1):
            u=i/nu;x=(u-.5)*2*half;crown=.004*(1-(2*u-1)**2)*(1-(2*v-1)**2)
            vs.append(pp+R@Vector((x,y,crown)));uv.append((u,v))
    fs=[(j*(nu+1)+i,j*(nu+1)+i+1,(j+1)*(nu+1)+i+1,(j+1)*(nu+1)+i) for j in range(nv) for i in range(nu)]
    mn='CRT_'+name;b.material(mn,(.003,.018,.009),.02,.32,1)
    b.begin('SCREEN_'+name,g,pp);b.mesh(g,mn,vs,fs,True);b.batches[b.override].uv=uv;b.end()
    screen_specs.append({'name':name,'material':mn,'texture':texture,'size':[w,h],'center':list(pp),'normal':list(n)})
    for xx in [-w/2-.038,w/2+.038]:
        for yy in [-h/2-.038,h/2+.038]:bolt(g,p+R@Vector((xx,yy,.042)),R,.0042)
    label(g,name.replace('_',' / ').upper(),p+R@Vector((0,h/2+.037,.042)),.0105,R)

def console():
    sp=importlib.util.spec_from_file_location('round_console',str(HERE/'circular_console.py'));module=importlib.util.module_from_spec(sp);sp.loader.exec_module(module)
    import sys
    module.build(sys.modules[__name__])

def materialize():
    for key,bt in b.batches.items():
        me=bpy.data.meshes.new(bt.name);me.from_pydata(bt.v,[],bt.f);me.update()
        for m in bt.m:me.materials.append(b.materials[m])
        for poly,mi,smooth in zip(me.polygons,bt.fm,bt.s):poly.material_index=mi;poly.use_smooth=smooth
        if bt.uv:
            layer=me.uv_layers.new(name='UVMap')
            for poly in me.polygons:
                for li in poly.loop_indices:layer.data[li].uv=bt.uv[me.loops[li].vertex_index]
        o=bpy.data.objects.new(bt.name,me);collections[bt.group].objects.link(o);o.parent=b.groups[bt.group];o.location=bt.origin
    for o in b.text_objects:
        g=next(k for k,v in b.groups.items() if v==o.parent)
        for col in list(o.users_collection):col.objects.unlink(o)
        collections[g].objects.link(o)
    for sp in screen_specs:
        m=b.materials[sp['material']];n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(HERE/'Textures'/sp['texture']),check_existing=True);tex.image.pack()
        m.node_tree.links.new(tex.outputs['Color'],n.inputs['Base Color']);m.node_tree.links.new(tex.outputs['Color'],n.inputs['Emission Color'])
        n.inputs['Emission Strength'].default_value=.85;n.inputs['Roughness'].default_value=.29;n.inputs['Specular IOR Level'].default_value=.06

def camera(name,loc,target,lens):
    d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);collections['12_Cameras'].objects.link(o)
    o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();d.lens=lens;d.clip_start=.015;d.clip_end=100
    return o

def area(name,loc,target,power,color,size):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape=b.enum(d,'shape','DISK');d.size=size
    o=bpy.data.objects.new(name,d);collections['11_Lighting'].objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()

def finish():
    materialize()
    # The portholes cut the fully materialized shell before it is saved/exported.
    sp=importlib.util.spec_from_file_location('side_windows',str(HERE/'side_windows.py'))
    windows=importlib.util.module_from_spec(sp);sp.loader.exec_module(windows)
    import sys
    windows.apply(sys.modules[__name__])
    camera('WALL_CAM_Pilot',(0,-.98,1.49),(0,.72,1.21),23)
    camera('WALL_CAM_ConsoleDetail',(0,-.63,1.48),(0,.70,1.29),26)
    camera('WALL_CAM_InteriorWide',(-.13,-1.035,1.69),(.04,.64,.99),17)
    camera('WALL_CAM_RearWall',(0,.37,1.53),(0,-1.1,1.02),18)
    camera('WALL_CAM_Cutaway',(3.4,-4.3,3.3),(0,.18,.93),47)
    camera('WALL_CAM_TwinPorts',(0,-1.03,1.49),(0,.4,1.28),11)
    camera('WALL_CAM_PortLeft',(-.10,-.74,1.47),(-1.04,-.16,1.43),24)
    camera('WALL_CAM_PortRight',(.10,-.74,1.47),(1.04,-.16,1.43),24)
    scene.camera=bpy.data.objects['WALL_CAM_Pilot']
    area('Ceiling_Softbox',(0,-.15,2.04),(0,.3,.55),26,(1,.84,.63),1.20)
    area('Front_WarmBounce',(0,.75,1.99),(0,.1,.82),12,(1,.59,.28),.8)
    area('Rear_CoolFill',(0,-.72,1.65),(0,.8,1.22),12,(.66,.81,1),.75)
    area('CRT_Spill',(0,.61,1.50),(0,-.7,.9),2.5,(.11,1,.44),.85)
    scene.world=bpy.data.worlds.new('Cockpit_StudioWorld');scene.world.use_nodes=True
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.11,.14,.18,1);bg.inputs['Strength'].default_value=.15
    try:scene.render.engine='CYCLES'
    except TypeError:scene.render.engine='BLENDER_EEVEE'
    if scene.render.engine=='CYCLES':
        scene.cycles.samples=64;scene.cycles.use_denoising=True
        cp=bpy.context.preferences.addons.get('cycles')
        if cp and cp.preferences.compute_device_type!='NONE':scene.cycles.device=b.enum(scene.cycles,'device','GPU')
    scene.render.resolution_x=1600;scene.render.resolution_y=1200;scene.render.resolution_percentage=65
    scene.render.image_settings.file_format=b.enum(scene.render.image_settings,'file_format','PNG')
    scene.render.film_transparent=False
    scene.view_settings.exposure=-.35
    # Keep existing Blender scenes in the source file, with this one active.
    for a in bpy.context.screen.areas:
        if a.type=='VIEW_3D':
            a.spaces.active.region_3d.view_perspective='CAMERA';a.spaces.active.overlay.show_overlays=False
            a.spaces.active.shading.type='MATERIAL'
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'CompactCockpit_WallIntegrated.blend'))
    print(json.dumps({'saved':str(HERE/'CompactCockpit_WallIntegrated.blend'),'objects':len(scene.objects),'screens':screen_specs},ensure_ascii=False))

def render(name='Cockpit_Interior',cam='WALL_CAM_Pilot',percent=100):
    scene.camera=bpy.data.objects[cam];scene.render.resolution_percentage=percent
    scene.render.filepath=str(HERE/'Previews'/(name+'.png'));bpy.ops.render.render(write_still=True)

if __name__=='__main__':
    setup();shell();console();finish()


