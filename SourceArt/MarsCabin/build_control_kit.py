"""Detailed moving throttle / flight-grip kit for the ARES cabin.

All four top-level meshes have their mechanical pivot at the origin. These are
moving parts only; the matching fixed pods are generated with Meshy T2.5.
Master parts are stored in a hidden collection in the cabin source scene.
"""
import bpy, math, os, json, importlib.util
from mathutils import Vector, Matrix

ROOT=r'D:\unity-projects\LudumDare60'
SOURCE=os.path.join(ROOT,'SourceArt','MarsCabin')
EXPORT=os.path.join(ROOT,'Assets','MarsCabin','Models')
TAU=math.pi*2


def build():
    spec=importlib.util.spec_from_file_location('cabin_control_geometry',os.path.join(SOURCE,'build_cabin.py'))
    b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
    room=bpy.data.scenes.get('MarsCabin_Interior') or bpy.context.scene
    bpy.context.window.scene=room
    b.scene=room
    collection=bpy.data.collections.new('ControlKit_Masters_Hidden')
    room.collection.children.link(collection)
    parent=bpy.data.objects.new('Cabin_ControlKit',None);collection.objects.link(parent)
    b.groups={'ControlKit':parent}
    for name in ['MC_Ivory','MC_Orange','MC_Metal','MC_Dark','MC_Rubber','MC_Screen','MC_GreenGlow','MC_WarmGlow','MC_RedGlow','MC_White','MC_Brass']:
        mat=bpy.data.materials.get(name)
        if mat is None:raise RuntimeError('Cabin material unavailable: '+name)
        b.materials[name]=mat

    def loft(mat,stations,n=48,power=2.8):
        # Contoured cross sections with superellipse shoulders instead of a capsule.
        vs=[]
        for z,cx,cy,rx,ry in stations:
            for i in range(n):
                a=TAU*i/n;c=math.cos(a);s=math.sin(a)
                vs.append((cx+rx*math.copysign(abs(c)**(2/power),c),cy+ry*math.copysign(abs(s)**(2/power),s),z))
        fs=[(k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i) for k in range(len(stations)-1) for i in range(n)]
        fs += [tuple(reversed(range(n))),tuple((len(stations)-1)*n+i for i in range(n))]
        b.mesh('ControlKit',mat,vs,fs,True)

    def grip_x(mat,c):
        # Purpose-shaped palm bar: thicker at the outer ends, concave centre.
        vs=[];n=40
        profile=[(-.064,.005,.006),(-.061,.017,.018),(-.054,.024,.022),(-.043,.025,.021),(-.020,.021,.019),(0,.020,.018),(.020,.021,.019),(.043,.025,.021),(.054,.024,.022),(.061,.017,.018),(.064,.005,.006)]
        for x,ry,rz in profile:
            for j in range(n):
                a=TAU*j/n;vs.append((c[0]+x,c[1]+ry*math.cos(a),c[2]+rz*math.sin(a)))
        fs=[(k*n+j,k*n+(j+1)%n,(k+1)*n+(j+1)%n,(k+1)*n+j) for k in range(len(profile)-1) for j in range(n)]
        fs += [tuple(reversed(range(n))),tuple((len(profile)-1)*n+j for j in range(n))]
        b.mesh('ControlKit',mat,vs,fs,True)

    def small_bolt(p,axis,r=.0035):
        p=Vector(p);axis=Vector(axis).normalized()
        b.cylinder('ControlKit','MC_Metal',p-axis*.0008,p+axis*.0012,r,6)
        tangent=axis.cross(Vector((.31,.73,.42))).normalized()*r*.58
        b.cylinder('ControlKit','MC_Dark',p+axis*.0013-tangent,p+axis*.0013+tangent,.00045,6)

    roots=[]
    for idx,name in enumerate(['ThrottleLever_A','ThrottleLever_B','ThrottleLever_C']):
        b.begin(name,'ControlKit',(0,0,0))
        # Hinge is the moving root. The axis is local X, exactly through origin.
        b.cylinder('ControlKit','MC_Metal',(-.022,0,0),(.022,0,0),.023,40)
        for side in [-1,1]:
            b.torus('ControlKit','MC_Brass',(side*.023,0,0),.017,.003,(1,0,0),32,8)
            b.cylinder('ControlKit','MC_Dark',(side*.023,0,0),(side*.025,0,0),.009,12)
            small_bolt((side*.026,0,0),(side,0,0),.006)
        loft('MC_Metal',[(.003,0,0,.009,.007),(.028,0,-.002,.010,.007),(.08,0,-.010,.009,.006),(.15,0,-.026,.008,.006),(.205,0,-.034,.009,.007)],24,4)
        # Two black recessed side strips and individual captive end fasteners.
        for side in [-1,1]:
            b.rounded('ControlKit','MC_Dark',(side*.0095,-.016,.106),(.0018,.009,.095),.001)
            for z,y in [(.05,-.006),(.16,-.027)]:small_bolt((side*.010,y,z),(side,0,0),.0031)
        b.cylinder('ControlKit','MC_Brass',(-.011,-.034,.204),(.011,-.034,.204),.013,24)
        grip_x(['MC_Ivory','MC_Dark','MC_Orange'][idx],(0,-.034,.232))
        # Endcaps, separation seams and finely spaced anti-slip rings.
        for side in [-1,1]:
            b.cylinder('ControlKit','MC_Orange',(side*.056,-.034,.232),(side*.059,-.034,.232),.018,32)
            b.cylinder('ControlKit','MC_Metal',(side*.059,-.034,.232),(side*.062,-.034,.232),.011,24)
            small_bolt((side*.063,-.034,.232),(side,0,0),.004)
        for xx in [-.043,-.034,-.025,.025,.034,.043]:
            # Partial ribs on the lower/front gripping face.
            for j in range(20):
                a=math.pi*.68+j*math.pi*.038;c=math.pi*.68+(j+1)*math.pi*.038
                b.cylinder('ControlKit','MC_Rubber',(xx,-.034+.022*math.cos(a),.232+.020*math.sin(a)),(xx,-.034+.022*math.cos(c),.232+.020*math.sin(c)),.0013,6)
        # Raised ivory index line and distinct one/two/three tactile dots.
        b.rounded('ControlKit','MC_White',(0,-.034,.251),(.014,.022,.002),.001)
        for dot in range(idx+1):
            x=(dot-idx/2)*.009
            b.cylinder('ControlKit','MC_Dark',(x,-.032,.252),(x,-.032,.254),.0025,10)
        b.end();roots.append(name)

    # Flight grip: steel attachment, orange locking collar, lower palm ledge and
    # asymmetrical swept rubber grip, with shaped finger scallops and thumb crown.
    b.begin('JoystickGrip','ControlKit',(0,0,0))
    b.lathe('ControlKit','MC_Metal',(0,0,0),[(-.016,.012),(0,.014),(.042,.014),(.048,.021)],48)
    b.torus('ControlKit','MC_Orange',(0,0,.038),.018,.004,n=40,sides=10)
    for j in range(12):
        a=TAU*j/12
        b.cylinder('ControlKit','MC_Dark',(.0142*math.cos(a),.0142*math.sin(a),.004),(.0142*math.cos(a),.0142*math.sin(a),.028),.0014,6)
    loft('MC_Dark',[(.041,0,0,.020,.023),(.052,0,.001,.040,.039),(.063,0,.003,.043,.042),(.075,0,.006,.034,.032),(.095,0,.011,.032,.029),(.116,0,.016,.034,.031),(.14,-.002,.023,.036,.033),(.17,-.003,.031,.036,.034),(.198,-.003,.037,.039,.037),(.218,-.004,.043,.045,.038),(.235,-.006,.047,.044,.035),(.248,-.007,.047,.035,.027),(.255,-.007,.047,.018,.015)],64,2.6)
    # Ivory side cheeks conform to the swept back rather than float on a cylinder.
    for side in [-1,1]:
        zs=[.080,.105,.135,.167,.193,.217]
        vs=[]
        for z in zs:
            y=.005+(z-.075)*.24
            x=side*(.0335+max(0,z-.13)*.055)
            vs += [(x,y-.014,z),(x+side*.002,y+.017,z)]
        b.mesh('ControlKit','MC_Ivory',vs,[(i*2,i*2+1,i*2+3,i*2+2) if side>0 else (i*2+2,i*2+3,i*2+1,i*2) for i in range(len(zs)-1)])
        for z in [.09,.20]:
            y=.005+(z-.075)*.24
            small_bolt((side*(.035+max(0,z-.13)*.055),y,z),(side,0,0),.0036)
        # Fine mould split lines run beside each cheek.
        for p,q in zip(zs,zs[1:]):
            b.cylinder('ControlKit','MC_Rubber',(side*.035,.005+(p-.075)*.24-.018,p),(side*.036,.005+(q-.075)*.24-.018,q),.0016,6)
    # Four scalloped forward finger pads, separated by narrow rubber channels.
    for z in [.092,.119,.148,.176]:
        cy=.008+(z-.08)*.24
        for j in range(28):
            a=math.pi*1.08+j*math.pi*.030;bend=math.pi*1.08+(j+1)*math.pi*.030
            b.cylinder('ControlKit','MC_Rubber',(.032*math.cos(a),cy+.032*math.sin(a),z),(.032*math.cos(bend),cy+.032*math.sin(bend),z),.0023,8)
    # Palm ledge orange seam and small guarded attachment screws.
    b.torus('ControlKit','MC_Orange',(0,.003,.061),.036,.0025,n=64,sides=8)
    for side in [-1,1]:small_bolt((side*.034,.006,.064),(0,0,1),.003)
    # Thumb hat mounting boss and top-button bezel are stationary on the grip.
    b.lathe('ControlKit','MC_Metal',(-.027,.055,.241),[(0,.012),(.006,.012),(.008,.010)],32)
    b.lathe('ControlKit','MC_Dark',(-.027,.055,.249),[(0,.011),(.009,.014),(.013,.011)],16)
    for a in [0,math.pi/2,math.pi,math.pi*1.5]:
        b.box('ControlKit','MC_White',(-.027+.007*math.cos(a),.055+.007*math.sin(a),.263),(.002,.002,.001))
    b.lathe('ControlKit','MC_Metal',(.015,.042,.245),[(0,.012),(.006,.012)],32)
    # Trigger guard is a shaped U frame in front of the index finger.
    last=None
    for j in range(28):
        a=math.radians(-70+j*230/27)
        p=(0,.008-.034*math.cos(a),.211+.034*math.sin(a))
        if last:b.cylinder('ControlKit','MC_Metal',last,p,.0035,10)
        last=p
    b.end();roots.append('JoystickGrip')

    trigger_pivot=Vector((0,-.004,.228))
    b.begin('INT_Joystick_Trigger','ControlKit',trigger_pivot)
    # Distinct curved trigger blade, not fused to the grip or guard.
    profile=[(-.006,.231),(-.020,.223),(-.029,.211),(-.029,.199),(-.019,.192),(-.013,.196),(-.020,.206),(-.017,.216),(-.005,.224)]
    vs=[(x,y,z) for x in [-.009,.009] for y,z in profile];n=len(profile)
    fs=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]+[(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    b.mesh('ControlKit','MC_Orange',vs,fs)
    b.cylinder('ControlKit','MC_Metal',(-.014,-.004,.228),(.014,-.004,.228),.004,20)
    for j in range(4):b.cylinder('ControlKit','MC_Rubber',(-.008,-.028,.201+j*.003),(.008,-.028,.201+j*.003),.0008,6)
    b.end()
    button_pivot=Vector((.015,.042,.252))
    b.begin('INT_Joystick_TopButton','ControlKit',button_pivot)
    b.lathe('ControlKit','MC_RedGlow',button_pivot,[(0,.009),(.004,.009),(.007,.007)],32)
    b.end()
    b.flush()
    for obj in b.objects:
        for col in list(obj.users_collection):col.objects.unlink(obj)
        collection.objects.link(obj)
    stick=next(o for o in b.objects if o.name=='JoystickGrip')
    for name in ['INT_Joystick_Trigger','INT_Joystick_TopButton']:
        obj=next(o for o in b.objects if o.name==name)
        matrix=obj.matrix_world.copy();obj.parent=stick;obj.matrix_world=matrix
    for obj in b.objects:
        if obj.name.startswith('ThrottleLever_'):obj['interaction_axis_blender']=[1,0,0]
    # Export root parts at zero, with identity transforms. Individual parts may be
    # extracted by name, placed and articulated without origin correction.
    for o in room.objects:o.select_set(False)
    parent.select_set(True)
    for o in b.objects:o.select_set(True)
    bpy.context.view_layer.objects.active=parent
    op=bpy.ops.export_scene.fbx
    path=os.path.join(EXPORT,'Cabin_ControlKit.fbx')
    op(filepath=path,use_selection=True,global_scale=1,apply_unit_scale=True,bake_space_transform=False,axis_forward=b.op_enum(op,'axis_forward','-Z'),axis_up=b.op_enum(op,'axis_up','Y'),object_types={b.op_enum(op,'object_types','MESH'),b.op_enum(op,'object_types','EMPTY')},add_leaf_bones=False,bake_anim=False,path_mode=b.op_enum(op,'path_mode','AUTO'),mesh_smooth_type=b.op_enum(op,'mesh_smooth_type','FACE'))
    tri=0
    for o in b.objects:o.data.calc_loop_triangles();tri+=len(o.data.loop_triangles)
    manifest={'file':path,'units':'metres','coordinateSpace':'Blender local X,Y,Z; pivot at lower hinge/socket','expectedUnityMapping':'(-x,z,-y), verify prior imported architecture sentinels','masterCollection':'ControlKit_Masters_Hidden','geometryTriangles':tri,'parts':[
        {'name':n,'kind':'Lever','pivot':[0,0,0],'axis':[1,0,0],'rangeDegrees':70,'gripCenter':[0,-.034,.232],'height':.256,'gripWidth':.128} for n in roots[:3]
    ]+[{'name':'JoystickGrip','kind':'Joystick','pivot':[0,0,0],'axes':[[1,0,0],[0,1,0]],'height':.263,'socketRadius':.014},
        {'name':'INT_Joystick_Trigger','parent':'JoystickGrip','kind':'Trigger','pivot':list(trigger_pivot),'axis':[1,0,0],'rangeDegrees':16},
        {'name':'INT_Joystick_TopButton','parent':'JoystickGrip','kind':'Button','pivot':list(button_pivot),'axis':[0,0,-1],'travel':.004}]}
    with open(os.path.join(SOURCE,'control_kit_manifest.json'),'w',encoding='utf-8') as f:json.dump(manifest,f,indent=2)
    collection.hide_render=True;collection.hide_viewport=True
    for o in room.objects:o.select_set(False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'MarsCabin_Interior.blend'))
    bpy.app.driver_namespace['mars_control_kit']={'builder':b,'collection':collection,'parent':parent,'parts':b.objects,'room':room,'manifest':manifest}
    print('CONTROL_KIT_COMPLETE',json.dumps(manifest))


if __name__=='__main__':build()
