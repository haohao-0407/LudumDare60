"""Independent geometry helpers copied from the project-authored MarsCabin toolkit. No scene is created at import time."""
import bpy, json, math, os, random
from mathutils import Vector, Matrix
from collections import OrderedDict

TAU=math.pi*2
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




