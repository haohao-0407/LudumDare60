"""ARES cabin interior: source geometry in metres, +Z up, pilot faces -Y.

Run build_all(), or setup(); build_shell(); build_ceiling(); build_equipment();
finish(). This intentionally authors the INTERIOR, without an exterior hull.
Existing Blender scenes are preserved. Meshy props are assembled separately.
"""
import bpy, json, math, os, random
from mathutils import Vector, Matrix
from collections import OrderedDict

ROOT=r'D:\unity-projects\LudumDare60'
SOURCE=os.path.join(ROOT,'SourceArt','MarsCabin')
EXPORT=os.path.join(ROOT,'Assets','MarsCabin','Models')
TAU=math.pi*2
ARCHITECTURE_SCALE=.8
random.seed(84)
batches=OrderedDict()
materials={}
groups={}
objects=[]
controls=[]
screens=[]
text_objects=[]
override=None


def enum(owner,prop,wanted):
    opts=[i.identifier for i in owner.bl_rna.properties[prop].enum_items]
    if wanted not in opts: raise RuntimeError((prop,wanted,opts))
    return wanted


def op_enum(operator,prop,wanted):
    opts=[i.identifier for i in operator.get_rna_type().properties[prop].enum_items]
    if wanted not in opts: raise RuntimeError((prop,wanted,opts))
    return wanted


def material(name,color,metal=0,rough=.5,emit=0):
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    mat.diffuse_color=(*color,1)
    n=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value=(*color,1)
    n.inputs['Metallic'].default_value=metal;n.inputs['Roughness'].default_value=rough
    if emit:
        n.inputs['Emission Color'].default_value=(*color,1)
        n.inputs['Emission Strength'].default_value=emit
    materials[name]=mat


def setup():
    global scene,root
    os.makedirs(SOURCE,exist_ok=True);os.makedirs(EXPORT,exist_ok=True)
    scene=bpy.data.scenes.new('MarsCabin_Interior')
    bpy.context.window.scene=scene
    scene.unit_settings.system=enum(scene.unit_settings,'system','METRIC')
    scene.unit_settings.scale_length=1
    col=bpy.data.collections.new('Cabin_Interior_Architecture')
    scene.collection.children.link(col)
    root=bpy.data.objects.new('Cabin_Architecture',None);col.objects.link(root)
    root['description']='Authored interior only. Ceiling is complete and visible by default.'
    for name in ['Floor','Walls','Door','Ceiling','Pipes','Lighting','Service','PerimeterBanks','Controls','Screens','Metadata']:
        obj=bpy.data.objects.new(name,None);col.objects.link(obj);obj.parent=root
        groups[name]=obj
    material('MC_Ivory',(.66,.61,.48),.16,.4)
    material('MC_Orange',(.80,.18,.025),.23,.35)
    material('MC_Metal',(.24,.25,.23),.8,.29)
    material('MC_Dark',(.038,.045,.04),.48,.48)
    material('MC_Rubber',(.018,.022,.018),.05,.8)
    material('MC_Screen',(.003,.022,.009),.05,.24,.15)
    material('MC_GreenGlow',(.035,.9,.23),.02,.25,1.7)
    material('MC_WarmGlow',(1,.43,.075),.05,.2,4)
    material('MC_RedGlow',(.9,.025,.008),.05,.2,2.3)
    material('MC_White',(.76,.78,.69),.2,.4)
    material('MC_Brass',(.37,.23,.075),.75,.35)


class Batch:
    def __init__(self,name,group,origin=(0,0,0)):
        self.name=name;self.group=group;self.origin=Vector(origin)
        self.v=[];self.f=[];self.s=[];self.fm=[];self.m=[];self.uv=None
    def add(self,mat,vs,fs,smooth):
        k=len(self.v);self.v += [tuple(Vector(v)-self.origin) for v in vs]
        self.f += [tuple(i+k for i in f) for f in fs]
        self.s += [smooth]*len(fs)
        if mat not in self.m:self.m.append(mat)
        self.fm += [self.m.index(mat)]*len(fs)


def mesh(g,m,vs,fs,smooth=False):
    key=override or g+'__'+m
    if key not in batches:batches[key]=Batch(key,g)
    batches[key].add(m,vs,fs,smooth)


def begin(name,g,p=(0,0,0)):
    global override
    override=name;batches[name]=Batch(name,g,p)


def end():
    global override
    override=None


def rotz(a):return Matrix.Rotation(a,3,'Z')
def axisrot(d):return Vector((0,0,1)).rotation_difference(Vector(d).normalized()).to_matrix()
def xf(vs,c,R=None):
    c=Vector(c)
    return [c+(R@Vector(v) if R is not None else Vector(v)) for v in vs]


def box(g,m,c,size,R=None):
    x,y,z=[v/2 for v in size]
    vs=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    mesh(g,m,xf(vs,c,R),[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])


def rounded(g,m,c,size,bevel=.08,R=None):
    x,y,z=size;r=min(bevel,x*.4,y*.4,z*.4);outline=[]
    for cx,cy,start in [(x/2-r,y/2-r,0),(-x/2+r,y/2-r,90),(-x/2+r,-y/2+r,180),(x/2-r,-y/2+r,270)]:
        for i in range(5):
            a=math.radians(start+i*22.5);outline.append((cx+r*math.cos(a),cy+r*math.sin(a)))
    vs=[(vx*s,vy*s,h) for h,s in [(-z/2,.975),(-z/2+r,1),(z/2-r,1),(z/2,.975)] for vx,vy in outline]
    n=len(outline);fs=[tuple(reversed(range(n))),tuple(3*n+i for i in range(n))]
    fs += [(k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i) for k in range(3) for i in range(n)]
    mesh(g,m,xf(vs,c,R),fs)


def lathe(g,m,c,profile,n=32,axis=(0,0,1),caps=True):
    vs=[(r*math.cos(TAU*j/n),r*math.sin(TAU*j/n),z) for z,r in profile for j in range(n)]
    fs=[(k*n+j,k*n+(j+1)%n,(k+1)*n+(j+1)%n,(k+1)*n+j) for k in range(len(profile)-1) for j in range(n)]
    if caps:fs += [tuple(reversed(range(n))),tuple((len(profile)-1)*n+j for j in range(n))]
    mesh(g,m,xf(vs,c,axisrot(axis)),fs,True)


def cylinder(g,m,a,b,r,n=12):
    a,b=Vector(a),Vector(b);d=b-a
    if d.length<1e-6:return
    lathe(g,m,(a+b)/2,[(-d.length/2,r),(d.length/2,r)],n,d)


def torus(g,m,c,r,t,axis=(0,0,1),n=64,sides=8):
    vs=[((r+t*math.cos(TAU*j/sides))*math.cos(TAU*i/n),(r+t*math.cos(TAU*j/sides))*math.sin(TAU*i/n),t*math.sin(TAU*j/sides)) for i in range(n) for j in range(sides)]
    fs=[(i*sides+j,((i+1)%n)*sides+j,((i+1)%n)*sides+(j+1)%sides,i*sides+(j+1)%sides) for i in range(n) for j in range(sides)]
    mesh(g,m,xf(vs,c,axisrot(axis)),fs,True)


def arc(g,m,r,a,b,z,t=.025,n=30):
    for i in range(n):
        u=a+(b-a)*i/n;v=a+(b-a)*(i+1)/n
        cylinder(g,m,(r*math.cos(u),r*math.sin(u),z),(r*math.cos(v),r*math.sin(v),z),t,8)


def wedge(g,m,r1,r2,a,b,z,n=8):
    vs=[]
    for r in [r1,r2]:
        for i in range(n+1):
            t=a+(b-a)*i/n;vs.append((r*math.cos(t),r*math.sin(t),z))
    mesh(g,m,vs,[(i,n+1+i,n+2+i,i+1) for i in range(n)])


def bolt(g,p,axis=(0,0,1),r=.025):
    d=Vector(axis).normalized();p=Vector(p)
    cylinder(g,'MC_Metal',p-d*.008,p+d*.014,r,6)
    # The darker slot remains geometrically distinct under close inspection.
    cylinder(g,'MC_Dark',p+d*.015-d.cross(Vector((.21,.77,.41))).normalized()*r*.55,p+d*.015+d.cross(Vector((.21,.77,.41))).normalized()*r*.55,.003,5)


def label(g,text,p,size=.10,R=None,mat='MC_Dark'):
    allowed=[i.identifier for i in bpy.data.curves.bl_rna.functions['new'].parameters['type'].enum_items]
    if 'FONT' not in allowed:raise RuntimeError(allowed)
    curve=bpy.data.curves.new('TXT_'+text,'FONT');curve.body=text;curve.size=size
    curve.align_x=enum(curve,'align_x','CENTER');curve.align_y=enum(curve,'align_y','CENTER')
    curve.resolution_u=3;curve.extrude=.0008
    obj=bpy.data.objects.new('Label_'+text,curve);scene.collection.objects.link(obj)
    obj.location=p
    if R is not None:obj.rotation_euler=R.to_euler()
    obj.parent=groups[g];curve.materials.append(materials[mat]);text_objects.append(obj)


def build_shell():
    # Circular interior floor, segmented access panels and real lattice grilles.
    lathe('Floor','MC_Dark',(0,0,-.10),[(-.06,4.19),(.06,4.19)],128)
    wedge('Floor','MC_Dark',0,1.8,0,TAU,.005,128)
    for radius in [1.84,3.65,4.10]:
        wedge('Floor','MC_Orange',radius-.045,radius+.045,0,TAU,.018,128)
    for i in range(20):
        a=TAU*i/20+.012;b=TAU*(i+1)/20-.012
        wedge('Floor','MC_Metal',1.92,3.55,a,b,.012)
        wedge('Floor','MC_Dark',1.98,3.49,a+.015,b-.015,.023)
        wedge('Floor','MC_Ivory',3.73,4.08,a,b,.025)
        # Each outer access grille has actual open spacing above a black duct bed.
        for j in range(12):
            aa=a+.028+(b-a-.056)*j/11
            cylinder('Floor','MC_Metal',(2.8*math.cos(aa),2.8*math.sin(aa),.04),(3.43*math.cos(aa),3.43*math.sin(aa),.04),.013,6)
        for rr in [2.81,2.94,3.07,3.20,3.33,3.43]:
            arc('Floor','MC_Metal',rr,a+.025,b-.025,.042,.010,8)
        for rr in [2.07,2.27,2.47,2.65]:
            for j in range(6):
                aa=a+.025+(b-a-.05)*(j+.5)/6
                box('Floor','MC_Metal',(rr*math.cos(aa),rr*math.sin(aa),.038),(.045,.021,.008),rotz(aa+math.pi/4))
        for rr in [1.96,3.52,3.84]:
            for aa in [a+.025,b-.025]:bolt('Floor',(rr*math.cos(aa),rr*math.sin(aa),.035),r=.018)
    for x in [-.43,.43]:
        rounded('Floor','MC_Metal',(x,-.2,.065),(.09,1.2,.1),.025)
        for y in [-.65,.25]:bolt('Floor',(x,y,.123),r=.027)
    label('Floor','ARES  /  PILOT STATION',(0,.9,.03),.14)
    # Inward facing curved panels, no external hull. Leave the rear pressure door bay.
    for i in range(24):
        a=TAU*i/24;b=TAU*(i+1)/24;mid=(a+b)/2
        if abs(mid-math.pi/2)<.28:continue
        for z1,z2,mat in [(.05,.60,'MC_Orange'),(.64,1.35,'MC_Ivory'),(1.39,2.55,'MC_Ivory'),(2.59,3.38,'MC_Ivory')]:
            vs=[]
            for z in [z1,z2]:
                for k in range(7):
                    q=a+.009+(b-a-.018)*k/6;vs.append((4.18*math.cos(q),4.18*math.sin(q),z))
            mesh('Walls',mat,vs,[(k,k+7,k+8,k+1) for k in range(6)])
            for q in [a+.035,b-.035]:
                for z in [z1+.06,z2-.06]:bolt('Walls',(4.16*math.cos(q),4.16*math.sin(q),z),(-math.cos(q),-math.sin(q),0),.022)
        cylinder('Walls','MC_Dark',(4.15*math.cos(a),4.15*math.sin(a),.08),(4.15*math.cos(a),4.15*math.sin(a),3.38),.027,8)
        # Curved ivory pillars rise around the closed dome ceiling.
        if i%2==0:
            cylinder('Walls','MC_Metal',(4.07*math.cos(a),4.07*math.sin(a),.15),(4.07*math.cos(a),4.07*math.sin(a),3.32),.062,12)
            for z in [.75,2.0,3.1]:
                cylinder('Walls','MC_Orange',(4.02*math.cos(a),4.02*math.sin(a),z),(4.1*math.cos(a),4.1*math.sin(a),z),.085,12)
    for z,r,t,mat in [(.16,4.04,.07,'MC_Metal'),(.62,4.13,.032,'MC_Metal'),(3.30,4.07,.075,'MC_Metal'),(3.42,4.12,.055,'MC_Orange')]:
        arc('Walls',mat,r,1.86,TAU+1.28,z,t,80)
    # Rear hatch: layered rounded pressure frame, wheel lock, hinges and porthole.
    R=Matrix.Rotation(math.pi/2,3,'X')
    for m,p,sz,bev in [('MC_Metal',(0,4.04,1.55),(2.25,3.12,.20),.20),('MC_Orange',(0,3.92,1.55),(2.05,2.94,.14),.22),('MC_Dark',(0,3.83,1.55),(1.82,2.72,.08),.18),('MC_Ivory',(0,3.77,1.55),(1.69,2.60,.075),.16)]:
        rounded('Door',m,p,sz,bev,R)
    for a in range(12):
        q=TAU*a/12
        bolt('Door',(.48*math.cos(q),3.70,2.15+.48*math.sin(q)),(0,-1,0),.029)
    cylinder('Door','MC_Dark',(0,3.70,2.15),(0,3.65,2.15),.40,64)
    cylinder('Door','MC_Screen',(0,3.645,2.15),(0,3.64,2.15),.34,64)
    torus('Door','MC_Metal',(0,3.63,2.15),.39,.045,(0,1,0),64,10)
    torus('Door','MC_Brass',(0,3.62,2.15),.34,.012,(0,1,0),64,8)
    # Porthole interior structural grille (the external landscape is not invented).
    for off in [-.14,.14]:cylinder('Door','MC_Metal',(off,3.61,1.86),(off,3.61,2.44),.018,8)
    rounded('Door','MC_Metal',(0,3.68,.93),(.52,.52,.06),.09,R)
    torus('Door','MC_Dark',(0,3.58,.93),.23,.045,(0,1,0),40,8)
    for a in [0,TAU/3,TAU*2/3]:cylinder('Door','MC_Metal',(0,3.57,.93),(.21*math.cos(a),3.57,.93+.21*math.sin(a)),.025,10)
    cylinder('Door','MC_Metal',(0,3.54,.93),(.48,3.52,.93),.036,12)
    cylinder('Door','MC_Dark',(.48,3.52,.87),(.48,3.52,1.00),.043,12)
    for x in [-.79,.79]:
        for z in [.58,1.50,2.52]:
            rounded('Door','MC_Metal',(x,3.68,z),(.19,.20,.08),.025,R)
            cylinder('Door','MC_Metal',(x+.06,3.61,z-.11),(x+.06,3.61,z+.11),.045,12)
            bolt('Door',(x-.04,3.62,z),(0,-1,0),.022)
    for x in [-1.28,1.28]:
        cylinder('Door','MC_Metal',(x,3.78,.58),(x,3.78,2.14),.035,10)
        for z in [.58,2.14]:cylinder('Door','MC_Metal',(x,3.78,z),(x,4.03,z),.035,10)
    label('Door','AIRLOCK  01',(0,3.68,2.75),.105,R)
    label('Door','CAUTION / PRESSURE SEAL',(0,3.67,.42),.07,R)
    for i in range(4):
        rounded('Door','MC_Dark',(-1.14,3.82,1.5+i*.18),(.13,.13,.055),.025,R)
        rounded('Door','MC_WarmGlow',(-1.14,3.78,1.5+i*.18),(.07,.07,.025),.012,R)
    begin('Door_Header','Door',(0,4.02,3.27))
    rounded('Door','MC_Ivory',(0,4.02,3.27),(2.28,.44,.10),.04,R)
    rounded('Door','MC_Orange',(0,3.96,3.12),(2.20,.07,.04),.015,R)
    end()
    flush()
    print('CABIN_PHASE_SHELL_COMPLETE',len(objects))


def build_ceiling():
    # Closed inward dome, clear height 3.44m at edge and 4.10m at centre.
    def height(r):return 4.1-.66*(r/4.2)**2
    rs=[0,.70,1.55,2.45,3.32,4.20]
    for i in range(24):
        a=TAU*i/24+.004;b=TAU*(i+1)/24-.004
        for r1,r2 in zip(rs,rs[1:]):
            vs=[]
            for r in [r1,r2]:
                for j in range(6):
                    t=a+(b-a)*j/5;vs.append((r*math.cos(t),r*math.sin(t),height(r)))
            mesh('Ceiling','MC_Ivory',vs,[(j,j+1,j+7,j+6) for j in range(5)])
        # Ribs follow the dome underside and carry captive bolts at each radius.
        a=TAU*i/24
        for r1,r2 in zip(rs[1:],rs[2:]):
            cylinder('Ceiling','MC_Metal',(r1*math.cos(a),r1*math.sin(a),height(r1)-.035),(r2*math.cos(a),r2*math.sin(a),height(r2)-.035),.024,8)
        for r in [1.55,2.45,3.32,4.1]:bolt('Ceiling',(r*math.cos(a),r*math.sin(a),height(r)-.06),(0,0,-1),.023)
    for r in [.7,1.55,2.45,3.32,4.10]:torus('Ceiling','MC_Metal',(0,0,height(r)-.02),r,.026,n=96)
    # Central environmental-service hub, inset vent slats and orange service ring.
    lathe('Ceiling','MC_Dark',(0,0,3.99),[(-.035,.68),(.035,.68)],64)
    torus('Ceiling','MC_Orange',(0,0,3.94),.61,.055,n=64)
    for y in [i*.09 for i in range(-5,6)]:
        half=math.sqrt(max(0,.50*.50-y*y));box('Ceiling','MC_Metal',(0,y,3.94),(half*2,.035,.035))
    # Ring of recessed luminaires in their own Ceiling hierarchy, closed above.
    for i in range(12):
        a=TAU*i/12;r=2.86;z=height(r)-.045
        rounded('Ceiling','MC_Dark',(r*math.cos(a),r*math.sin(a),z),(.24,.72,.08),.04,rotz(a))
        rounded('Ceiling','MC_WarmGlow',(r*math.cos(a),r*math.sin(a),z-.05),(.13,.60,.025),.025,rotz(a))
        for t in [-.25,.25]:
            p=Vector((r*math.cos(a),r*math.sin(a),z-.072))+rotz(a)@Vector((0,t,0))
            box('Ceiling','MC_Metal',p,(.16,.025,.03),rotz(a))
    for j,r in enumerate([3.72,3.83,3.94]):
        torus('Ceiling','MC_Dark' if j==1 else 'MC_Metal',(0,0,height(r)-.12),r,.034,n=112)
        for i in range(24):
            a=TAU*i/24;p=(r*math.cos(a),r*math.sin(a),height(r)-.12)
            torus('Ceiling','MC_Brass',p,.043,.012,(-math.sin(a),math.cos(a),0),n=12,sides=6)
    # Perimeter upper pipes and inward wall light fittings.
    for i in range(12):
        a=TAU*i/12
        if abs(a-math.pi/2)<.25:continue
        u=Vector((math.sin(a),-math.cos(a),0));v=Vector((0,0,1));n=Vector((-math.cos(a),-math.sin(a),0));R=Matrix((u,v,n)).transposed()
        c=Vector((4.04*math.cos(a),4.04*math.sin(a),2.95))
        rounded('Lighting','MC_Metal',c,(.91,.19,.12),.04,R)
        rounded('Lighting','MC_WarmGlow',c+n*.07,(.72,.095,.025),.02,R)
        for z in [2.61,3.20]:
            p=(4.04*math.cos(a),4.04*math.sin(a),z)
            arc('Pipes','MC_Metal',4.04,a-.23,a+.23,z,.043,12)
    flush()
    print('CABIN_PHASE_CEILING_COMPLETE',len(objects))


def screen(name,center,width,height,R,variant=0):
    c=Vector(center);n=R@Vector((0,0,1))
    rounded('Screens','MC_Metal',c,(width+.21,height+.21,.11),.06,R)
    rounded('Screens','MC_Orange',c+n*.065,(width+.13,height+.13,.045),.04,R)
    rounded('Screens','MC_Rubber',c+n*.094,(width+.025,height+.025,.025),.04,R)
    for x in [-width/2-.07,width/2+.07]:
        for y in [-height/2-.07,height/2+.07]:bolt('Screens',c+R@Vector((x,y,.095)),n,.018)
    p=c+n*.111
    begin('SCREEN_'+name,'Screens',p)
    mesh('Screens','MC_Screen',xf([(-width/2,-height/2,0),(width/2,-height/2,0),(width/2,height/2,0),(-width/2,height/2,0)],p,R),[(0,1,2,3)])
    batches[override].uv=[(0,0),(1,0),(1,1),(0,1)];end()
    screens.append({'name':'SCREEN_'+name,'center':list(p),'worldNormal':list(n),'size':[width,height]})
    # Vector-drawn phosphor technical graphics stay individually removable.
    begin('SCREEN_GRAPHICS_'+name,'Screens',p)
    def line(a,b,r=.002):cylinder('Screens','MC_GreenGlow',p+R@Vector((a[0],a[1],.003)),p+R@Vector((b[0],b[1],.003)),r,5)
    w=width*.46;h=height*.43
    for x in [-w,w]:line((x,-h),(x,h),.0028)
    for y in [-h,h]:line((-w,y),(w,y),.0028)
    for j in range(1,6):
        y=-h+2*h*j/6;line((-w,y),(w,y),.0008)
    for j in range(1,8):
        x=-w+2*w*j/8;line((x,-h),(x,h),.0008)
    if variant%3==0:
        for rr in [.15,.28,.40]:
            rr*=min(width,height)
            for j in range(48):
                a=TAU*j/48;b=TAU*(j+1)/48;line((rr*math.cos(a),rr*math.sin(a)),(rr*math.cos(b),rr*math.sin(b)),.0018)
        line((0,0),(w*.65,h*.7),.003)
        for j in range(8):
            x=math.sin(j*2.7)*w*.8;y=math.cos(j*1.8)*h*.8;line((x-.012,y),(x+.012,y),.0025);line((x,y-.012),(x,y+.012),.0025)
    elif variant%3==1:
        # Navigation landscape wireframe with multiple parallax scan lines.
        for row in range(5):
            prev=None
            for j in range(45):
                x=-w+w*2*j/44;y=-h*.8+row*h*.23+abs(math.sin(j*.18+row*.13))*h*.5+math.exp(-((j-21)/5)**2)*h*.55
                if prev:line(prev,(x,y),.0016)
                prev=(x,y)
    else:
        for j in range(24):
            x=-w*.9+j*w*1.8/23;v=(.12+abs(math.sin(j*1.78))*.65)*h
            line((x,-h*.72),(x,-h*.72+v),.003)
        prev=None
        for j in range(60):
            x=-w+w*2*j/59;y=h*.32+math.sin(j*.5)*h*.08+(h*.28 if j%15==0 else 0)
            if prev:line(prev,(x,y),.0018)
            prev=(x,y)
    end()
    label('Screens',['RANGE / RADAR','ARES / NAVIGATION','LIFE SUPPORT / TELEMETRY'][variant%3],p+R@Vector((0,height*.36,.008)),height*.055,R,'MC_GreenGlow')


def build_wall_screens():
    for idx,a in enumerate([.18,.75,2.39,2.96,3.60,5.82]):
        u=Vector((math.sin(a),-math.cos(a),0));v=Vector((0,0,1));n=Vector((-math.cos(a),-math.sin(a),0))
        R=Matrix((u,v,n)).transposed()@Matrix.Rotation(math.radians(-18),3,'X')
        c=(3.81*math.cos(a),3.81*math.sin(a),1.77)
        screen('WALL_%02d'%idx,c,.77,.62,R,idx)


def build_perimeter_banks():
    """Banked modular consoles fill the wall base; rear egress stays clear."""
    g='PerimeterBanks'
    # Every module is an inward-facing radial cabinet with a sloped upper deck.
    for i in range(18):
        a=TAU*i/18+.012;b=TAU*(i+1)/18-.012;mid=(a+b)/2
        # Rear hatch has a continuous ~2.25m-wide clear approach before scaling.
        if math.radians(68)<mid<math.radians(112):continue
        def curvepanel(r1,z1,r2,z2,mat):
            vs=[]
            for r,z in [(r1,z1),(r2,z2)]:
                for j in range(9):
                    t=a+(b-a)*j/8;vs.append((r*math.cos(t),r*math.sin(t),z))
            mesh(g,mat,vs,[(j,j+9,j+10,j+1) for j in range(8)])
        curvepanel(3.29,.12,3.29,.93,'MC_Ivory')
        curvepanel(3.30,.14,3.30,.28,'MC_Orange')
        curvepanel(3.29,.94,3.29,1.15,'MC_Orange')
        curvepanel(3.28,1.15,3.99,1.54,'MC_Ivory')
        wedge(g,'MC_Dark',3.3,4.02,a,b,.12)
        # End-cap structural panels and dark reveal around every deck segment.
        for t in [a,b]:
            vs=[(3.29*math.cos(t),3.29*math.sin(t),.12),(4.02*math.cos(t),4.02*math.sin(t),.12),(3.99*math.cos(t),3.99*math.sin(t),1.54),(3.28*math.cos(t),3.28*math.sin(t),1.15)]
            mesh(g,'MC_Metal',vs,[(0,1,2,3)])
            cylinder(g,'MC_Metal',vs[3],vs[2],.014,8)
        arc(g,'MC_Metal',3.275,a,b,1.15,.019,16)
        arc(g,'MC_Metal',3.99,a,b,1.54,.017,16)
        # Inward cabinet service door, ventilation slots, latch and serial marking.
        u=Vector((math.sin(mid),-math.cos(mid),0));v=Vector((0,0,1));n=Vector((-math.cos(mid),-math.sin(mid),0));R=Matrix((u,v,n)).transposed()
        c=Vector((3.275*math.cos(mid),3.275*math.sin(mid),.63))
        rounded(g,'MC_Dark',c,(.76,.60,.035),.035,R)
        rounded(g,'MC_Ivory',c+n*.023,(.70,.54,.025),.030,R)
        for row in range(6):box(g,'MC_Dark',c+R@Vector((-.07,-.14+row*.052,.043)),(.40,.018,.01),R)
        rounded(g,'MC_Metal',c+R@Vector((.255,.03,.05)),(.06,.18,.03),.012,R)
        for x in [-.30,.30]:
            for z in [-.21,.21]:bolt(g,c+R@Vector((x,z,.052)),n,.017)
        label(g,'SYS %02d / ACCESS'%(i+1),c+R@Vector((0,.20,.057)),.050,R)
        # Deck control face follows the radial slope and carries grouped lamps.
        deckR=R@Matrix.Rotation(math.radians(-61),3,'X')
        dc=Vector((3.61*math.cos(mid),3.61*math.sin(mid),1.35))
        rounded(g,'MC_Metal',dc,(.83,.45,.034),.027,deckR)
        rounded(g,'MC_Ivory',dc+deckR@Vector((0,0,.025)),(.78,.40,.018),.020,deckR)
        for xx in [-.34,.34]:
            for yy in [-.15,.15]:bolt(g,dc+deckR@Vector((xx,yy,.041)),deckR@Vector((0,0,1)),.013)
        for j in range(5):
            pp=dc+deckR@Vector((-.27+j*.115,.095,.043))
            rounded(g,'MC_Dark',pp,(.085,.072,.019),.010,deckR)
            rounded(g,'MC_WarmGlow' if j%3 else 'MC_RedGlow',pp+deckR@Vector((0,0,.013)),(.060,.048,.010),.007,deckR)
        for j in range(4):
            pp=dc+deckR@Vector((-.25+j*.16,-.075,.046));axis=deckR@Vector((0,0,1))
            cylinder(g,'MC_Metal',pp,pp+axis*.017,.040,24)
            cylinder(g,'MC_Dark',pp+axis*.017,pp+axis*.052,.031,24)
            for k in range(12):
                aa=TAU*k/12;rad=deckR@Vector((.031*math.cos(aa),.031*math.sin(aa),0))
                cylinder(g,'MC_Metal',pp+rad+axis*.020,pp+rad+axis*.045,.002,5)
            box(g,'MC_White',pp+deckR@Vector((0,-.014,.054)),(.005,.017,.002),deckR)
        # Short edge handrail with returns between adjoining modules.
        railA=Vector((3.19*math.cos(a+.045),3.19*math.sin(a+.045),1.19))
        railB=Vector((3.19*math.cos(b-.045),3.19*math.sin(b-.045),1.19))
        cylinder(g,'MC_Metal',railA,railB,.018,10)
        for pp in [railA,railB]:cylinder(g,'MC_Metal',pp,pp+Vector((math.cos(mid)*.095,math.sin(mid)*.095,-.045)),.018,10)
    flush()
    print('CABIN_PERIMETER_BANKS_COMPLETE')


def build_equipment():
    # Service cabinets occupy the wall perimeter and leave Meshy console bays free.
    for idx,a in enumerate([.57,2.57,3.72,5.70]):
        n=Vector((-math.cos(a),-math.sin(a),0));u=Vector((math.sin(a),-math.cos(a),0));v=Vector((0,0,1));R=Matrix((u,v,n)).transposed()
        c=Vector((3.91*math.cos(a),3.91*math.sin(a),.81))
        rounded('Service','MC_Ivory',c,(.90,1.35,.30),.08,R)
        rounded('Service','MC_Dark',c+n*.17,(.74,.98,.02),.03,R)
        rounded('Service','MC_Ivory',c+n*.19,(.69,.93,.02),.03,R)
        for z in [-.34,.34]:
            for x in [-.25,.25]:bolt('Service',c+R@Vector((x,z,.225)),n,.022)
        label('Service',['OXYGEN','HYDRAULICS','AVIONICS','COOLANT'][idx],c+R@Vector((0,.48,.23)),.074,R)
        for j in range(7):box('Service','MC_Dark',c+R@Vector((0,-.24+j*.065,.23)),(.44,.022,.012),R)
    # Cylindrical reserve bottles, band clamps, valves, hoses and warning tags.
    for side in [-1,1]:
        x=side*1.68;y=3.62
        lathe('Service','MC_Metal',(x,y,.3),[(0,.14),(.08,.21),(.88,.21),(.98,.14),(1.04,.06)],32)
        cylinder('Service','MC_Metal',(x,y,1.34),(x,y,1.54),.035,12)
        torus('Service','MC_Orange',(x,y,1.52),.085,.014,n=20,sides=6)
        for z in [.47,1.05]:torus('Service','MC_Dark',(x,y,z),.217,.027,n=32,sides=6)
        rounded('Service','MC_Orange',(x,y-.205,.84),(.19,.025,.33),.025)
        label('Service','O2',(x,y-.23,.84),.10,Matrix.Rotation(math.pi/2,3,'X'),'MC_White')
        # Riser tubes curve through distinct elbow segments.
        for off in [-.28,.28]:
            px=x+off
            cylinder('Pipes','MC_Metal',(px,y,.15),(px,y,2.8),.033,10)
            for z in [.23,.7,1.5,2.6]:torus('Pipes','MC_Brass',(px,y,z),.041,.012,n=12,sides=6)
    build_wall_screens()
    # Free-standing control wings, each control keeps its own mesh and usable pivot.
    for side in [-1,1]:
        x=side*1.06;y=-1.45
        rounded('Service','MC_Ivory',(x,y,.91),(.65,.88,.22),.07)
        rounded('Service','MC_Orange',(x,y,1.035),(.57,.78,.035),.025)
        rounded('Service','MC_Metal',(x,y,1.06),(.53,.74,.025),.02)
        for xx in [-.23,.23]:
            for yy in [-.32,.32]:bolt('Service',(x+xx,y+yy,1.08),r=.016)
        for xx in [-.22,.22]:
            cylinder('Service','MC_Metal',(x+xx,y-.22,.06),(x+xx,y-.22,.79),.035,10)
            cylinder('Service','MC_Metal',(x+xx,y-.22,.79),(x+xx,y+.22,.79),.035,10)
        label('Service','SYSTEM A' if side<0 else 'SYSTEM B',(x,y+.31,1.084),.055)
        for row in range(3):
            for col in range(3):
                p=Vector((x+(col-1)*.135,y-.12+row*.115,1.09))
                rounded('Controls','MC_Dark',p,(.112,.092,.025),.017)
                name='INT_BUTTON_%s_%02d'%('L' if side<0 else 'R',row*3+col)
                begin(name,'Controls',p)
                rounded('Controls','MC_WarmGlow' if row==0 else 'MC_Ivory',p+Vector((0,0,.021)),(.080,.064,.032),.012)
                end();controls.append({'name':name,'kind':'Button','pivot':list(p),'worldAxis':[0,0,-1],'travel':.012})
                label('Controls',str(row*3+col+1),p+Vector((0,0,.039)),.030)
        for col in range(3):
            p=Vector((x+(col-1)*.16,y-.30,1.09));torus('Controls','MC_Ivory',p,.052,.008,n=24,sides=6)
            name='INT_KNOB_%s_%02d'%('L' if side<0 else 'R',col)
            begin(name,'Controls',p)
            lathe('Controls','MC_Dark',p,[(0,.043),(.055,.043),(.068,.035)],24)
            for k in range(16):
                a=TAU*k/16;cylinder('Controls','MC_Metal',p+Vector((.044*math.cos(a),.044*math.sin(a),.01)),p+Vector((.044*math.cos(a),.044*math.sin(a),.05)),.0045,5)
            box('Controls','MC_White',p+Vector((0,-.018,.070)),(.008,.025,.003))
            end();controls.append({'name':name,'kind':'Knob','pivot':list(p),'worldAxis':[0,0,1],'rangeDegrees':270})
        for k in range(2):
            p=Vector((x+side*.34,y-.16+k*.27,1.04))
            rounded('Service','MC_Ivory',p,(.13,.19,.06),.025)
            name='INT_TOGGLE_%s_%02d'%('L' if side<0 else 'R',k)
            begin(name,'Controls',p)
            cylinder('Controls','MC_Metal',p,p+Vector((0,.024,.10)),.012,10)
            lathe('Controls','MC_Dark',p+Vector((0,.024,.10)),[(-.015,.022),(.025,.022),(.035,.014)],16)
            end();controls.append({'name':name,'kind':'Toggle','pivot':list(p),'worldAxis':[1,0,0],'rangeDegrees':45})
        # A slim grip rail around the wing can be grabbed independently of controls.
        for yy in [-.43,.43]:
            cylinder('Service','MC_Metal',(x-.27,y+yy,1.13),(x+.27,y+yy,1.13),.024,10)
            for xx in [-.27,.27]:cylinder('Service','MC_Metal',(x+xx,y+yy,1.04),(x+xx,y+yy,1.13),.024,10)
    # Labelled analogue gauge pods and equipment warning plates.
    for side in [-1,1]:
        a=.98 if side>0 else 2.16
        n=Vector((-math.cos(a),-math.sin(a),0));u=Vector((math.sin(a),-math.cos(a),0));v=Vector((0,0,1));R=Matrix((u,v,n)).transposed()
        c=Vector((4.01*math.cos(a),4.01*math.sin(a),2.0))
        rounded('Service','MC_Ivory',c,(.42,.72,.14),.04,R)
        for off in [-.17,.17]:
            cc=c+R@Vector((0,off,.09));cylinder('Service','MC_Dark',cc,cc+n*.03,.135,40)
            torus('Service','MC_Metal',cc+n*.04,.13,.015,n,40,8)
            for k in range(11):
                aa=math.radians(30+300*k/10)
                p=cc+R@Vector((.09*math.cos(aa),.09*math.sin(aa),.05));q=cc+R@Vector((.115*math.cos(aa),.115*math.sin(aa),.05))
                cylinder('Service','MC_White',p,q,.0025,5)
            cylinder('Service','MC_WarmGlow',cc+n*.055,cc+R@Vector((.07,.05,.055)),.004,6)
    flush()
    print('CABIN_PHASE_EQUIPMENT_COMPLETE',len(objects),'controls',len(controls),'screens',len(screens))


def flush():
    for key,b in list(batches.items()):
        if not b.v:continue
        data=bpy.data.meshes.new(b.name);data.from_pydata(b.v,[],b.f);data.update()
        for m in b.m:data.materials.append(materials[m])
        for p,s,m in zip(data.polygons,b.s,b.fm):p.use_smooth=s;p.material_index=m
        uv=data.uv_layers.new(name='UVMap')
        for poly in data.polygons:
            normal=poly.normal;axis=max(range(3),key=lambda j:abs(normal[j]));axes=[j for j in range(3) if j!=axis]
            for li in poly.loop_indices:
                vi=data.loops[li].vertex_index;v=data.vertices[vi].co
                uv.data[li].uv=b.uv[vi] if b.uv else (v[axes[0]],v[axes[1]])
        obj=bpy.data.objects.new(b.name,data);scene.collection.objects.link(obj)
        obj.location=b.origin;obj.parent=groups[b.group];objects.append(obj)
    batches.clear()
    depsgraph=bpy.context.evaluated_depsgraph_get()
    for obj in list(text_objects):
        data=bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph),depsgraph=depsgraph)
        new=bpy.data.objects.new(obj.name,data);scene.collection.objects.link(new)
        new.matrix_world=obj.matrix_world.copy();new.parent=obj.parent
        objects.append(new);bpy.data.objects.remove(obj,do_unlink=True)
    text_objects.clear()


def finish():
    # Orientation empties make handedness observable in the Unity asset importer.
    for name,p in [('ORIENTATION_X_POS',(1,0,0)),('ORIENTATION_Y_POS',(0,1,0)),('ORIENTATION_Z_POS',(0,0,1))]:
        o=bpy.data.objects.new(name,None);scene.collection.objects.link(o);o.parent=groups['Metadata'];o.location=p
    for entry in controls:
        obj=next(o for o in objects if o.name==entry['name'])
        obj['interaction_kind']=entry['kind'];obj['world_axis_blender']=entry['worldAxis']
    root.scale=(ARCHITECTURE_SCALE,)*3
    bpy.context.view_layer.update()
    # Interior-friendly preview lights and two cameras, excluded from the FBX.
    world=bpy.data.worlds.new('Cabin_Dark_World');world.use_nodes=True
    n=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');n.inputs['Color'].default_value=(.018,.022,.028,1);n.inputs['Strength'].default_value=.3
    scene.world=world
    for name,p,energy,color,size in [('Cabin_Ceiling_Key',(0,0,3.35),500,(1,.73,.46),3.5),('Cabin_Door_Fill',(0,2.5,2.9),130,(.64,.78,1),2),('Cabin_Forward_Fill',(0,-2.5,2.7),180,(1,.55,.26),2),('Cabin_Ceiling_Bounce',(0,0,1.25),140,(.65,.76,1),3.5)]:
        allowed=[i.identifier for i in bpy.types.Light.bl_rna.properties['type'].enum_items]
        if 'AREA' not in allowed:raise RuntimeError(allowed)
        data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.color=color;data.size=size
        o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=Vector(p)*ARCHITECTURE_SCALE
        if name=='Cabin_Ceiling_Bounce':o.rotation_euler=(math.pi,0,0)
    for name,p,target,lens in [('Cabin_Cutaway_Camera',(7,-9,9),(0,0,1.2),47),('Cabin_Interior_Camera',(0,2.4,1.95),(0,-2.1,1.35),20)]:
        data=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=Vector(p)*ARCHITECTURE_SCALE;o.rotation_euler=(Vector(target)*ARCHITECTURE_SCALE-o.location).to_track_quat('-Z','Y').to_euler();data.lens=lens;data.clip_start=.03
        if name=='Cabin_Interior_Camera':scene.camera=o
    scene.render.resolution_x=1600;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
    try:scene.render.engine='BLENDER_EEVEE'
    except TypeError:pass
    scene.render.image_settings.file_format=enum(scene.render.image_settings,'file_format','PNG')
    # Material colours are retained in Blender and intentionally remapped in Unity.
    for o in scene.objects:o.select_set(False)
    root.select_set(True)
    for o in list(groups.values())+objects:o.select_set(True)
    for o in groups['Metadata'].children:o.select_set(True)
    bpy.context.view_layer.objects.active=root
    fbx=bpy.ops.export_scene.fbx
    fbx(filepath=os.path.join(EXPORT,'Cabin_Architecture.fbx'),use_selection=True,global_scale=1,apply_unit_scale=True,bake_space_transform=False,
        axis_forward=op_enum(fbx,'axis_forward','-Z'),axis_up=op_enum(fbx,'axis_up','Y'),object_types={op_enum(fbx,'object_types','MESH'),op_enum(fbx,'object_types','EMPTY')},
        add_leaf_bones=False,bake_anim=False,path_mode=op_enum(fbx,'path_mode','AUTO'),mesh_smooth_type=op_enum(fbx,'mesh_smooth_type','FACE'))
    tris=0
    for o in objects:o.data.calc_loop_triangles();tris+=len(o.data.loop_triangles)
    bounds=[o.matrix_world@Vector(v) for o in objects for v in o.bound_box]
    report={'scene':scene.name,'units':'metres','architectureScale':ARCHITECTURE_SCALE,'roomRadius':4.2*ARCHITECTURE_SCALE,'ceilingVisible':True,'ceilingRange':[3.44*ARCHITECTURE_SCALE,4.1*ARCHITECTURE_SCALE],'exteriorHull':False,'meshObjects':len(objects),'triangles':tris,'boundsMin':[min(p[k] for p in bounds) for k in range(3)],'boundsMax':[max(p[k] for p in bounds) for k in range(3)],'materials':list(materials),'groups':list(groups),'fbxAxes':{'forward':'-Z','up':'Y'},'expectedUnityMapping':'(-BlenderX, BlenderZ, -BlenderY); orientation sentinels now measure scale0.8','reservedPropsAuthoredCoordinates':{'PilotSeat':[0,-.2,0],'MainNavConsole':[0,-2.1,0],'LeftConsole':[-2.8,0,0],'RightConsole':[2.8,0,0]},'controlCount':len(controls),'screenCount':len(screens)}
    with open(os.path.join(SOURCE,'design_manifest.json'),'w',encoding='utf-8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
    final_controls=[dict(e,pivot=[v*ARCHITECTURE_SCALE for v in e['pivot']],**({'travel':e['travel']*ARCHITECTURE_SCALE} if 'travel' in e else {})) for e in controls]
    final_screens=[dict(e,center=[v*ARCHITECTURE_SCALE for v in e['center']],size=[v*ARCHITECTURE_SCALE for v in e['size']]) for e in screens]
    with open(os.path.join(SOURCE,'interaction_manifest.json'),'w',encoding='utf-8') as f:json.dump({'coordinateSpace':'Blender world after root scale, metres, Z up','architectureScale':ARCHITECTURE_SCALE,'controls':final_controls,'screens':final_screens},f,ensure_ascii=False,indent=2)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'MarsCabin_Interior.blend'))
    print('CABIN_BUILD_COMPLETE',json.dumps(report))


def build_all():
    setup();build_shell();build_ceiling();build_equipment();build_perimeter_banks();finish()


if __name__=='__main__':build_all()
