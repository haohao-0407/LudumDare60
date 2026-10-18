"""ARES OUTPOST / procedural architecture source. Run inside Blender 5.x.

Creates a separate scene without modifying objects in the user's original scene.
Metres, Blender Z-up. All architecture is authored geometry; generated assets are
added separately by the Unity assembly workflow. No terrain is generated here.
"""
import bpy
import json
import math
import os
from collections import defaultdict
from mathutils import Vector, Matrix

ROOT = r"D:\unity-projects\LudumDare60"
SOURCE = os.path.join(ROOT, "SourceArt", "MarsBase")
EXPORT = os.path.join(ROOT, "Assets", "MarsBase", "Models")
os.makedirs(SOURCE, exist_ok=True)
os.makedirs(EXPORT, exist_ok=True)
TAU = math.pi * 2


def enum_choice(owner, property_name, requested):
    values = [item.identifier for item in owner.bl_rna.properties[property_name].enum_items]
    if requested not in values:
        raise RuntimeError("Unsupported %s=%s; available %s" % (property_name, requested, values))
    return requested


def op_enum(operator, property_name, requested):
    values = [item.identifier for item in operator.get_rna_type().properties[property_name].enum_items]
    if requested not in values:
        raise RuntimeError("Unsupported operator %s=%s; available %s" % (property_name, requested, values))
    return requested


scene = bpy.data.scenes.new("MarsBase_RetroFuture")
if bpy.context.window:
    bpy.context.window.scene = scene
scene.unit_settings.system = enum_choice(scene.unit_settings, "system", "METRIC")
scene.unit_settings.scale_length = 1.0
scene["project"] = "ARES / retrofuturist Mars outpost"
scene["units"] = "metres; Z up; front -Y"
scene["provenance"] = "Architecture hand-authored with procedural Blender mesh construction"
materials = {}


def material(name, color, metallic=0, roughness=.5, emission=0, alpha=1):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, alpha)
    node = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value = (*color, alpha)
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    node.inputs["Alpha"].default_value = alpha
    if emission:
        node.inputs["Emission Color"].default_value = (*color, 1)
        node.inputs["Emission Strength"].default_value = emission
    if alpha < 1:
        if "Transmission Weight" in node.inputs:
            node.inputs["Transmission Weight"].default_value = .22
        if hasattr(mat, "surface_render_method"):
            mat.surface_render_method = enum_choice(mat, "surface_render_method", "DITHERED")
    materials[name] = mat
    return mat


material("MB_Ceramic", (.73, .69, .55), .15, .42)
material("MB_Orange", (.83, .19, .035), .2, .38)
material("MB_Metal", (.27, .29, .27), .75, .37)
material("MB_Dark", (.045, .065, .062), .45, .5)
material("MB_Glass", (.13, .28, .25), .22, .16, alpha=.36)
material("MB_WindowGlow", (1.0, .56, .12), .05, .25, emission=2.1)
material("MB_Green", (.14, .33, .055), .0, .65)
material("MB_GreenLight", (.35, .50, .08), .0, .7)
material("MB_RedSoil", (.21, .095, .045), .0, .92)
material("MB_Sign", (.79, .83, .76), .1, .5)
material("MB_Blue", (.035, .10, .17), .55, .24)
material("MB_RedLight", (1.0, .055, .015), .1, .2, emission=3.0)

collections = {}
batches = {}
text_objects = []


def module(name):
    col = bpy.data.collections.new(name)
    scene.collection.children.link(col)
    collections[name] = col
    return name


class Batch:
    def __init__(self, group, mat):
        self.group, self.mat = group, mat
        self.verts, self.faces, self.smooth = [], [], []

    def add(self, verts, faces, smooth=False):
        start = len(self.verts)
        self.verts.extend(tuple(v) for v in verts)
        self.faces.extend(tuple(start + i for i in f) for f in faces)
        self.smooth.extend([smooth] * len(faces))


def mesh(group, mat, verts, faces, smooth=False):
    key = (group, mat)
    if key not in batches:
        batches[key] = Batch(group, mat)
    batches[key].add(verts, faces, smooth)


def basis_z(axis):
    return Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix()


def transform(vertices, center, rotation=None):
    c = Vector(center)
    if rotation is None:
        return [Vector(v) + c for v in vertices]
    return [rotation @ Vector(v) + c for v in vertices]


def box(g, mat, center, size, rotation=0):
    x, y, z = (v / 2 for v in size)
    vs = [(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    rot = Matrix.Rotation(rotation, 3, 'Z') if isinstance(rotation, (int, float)) else rotation
    mesh(g, mat, transform(vs, center, rot), [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])


def lathe(g, mat, center, profile, segments=64, axis=(0,0,1), caps=True):
    vs = [(r * math.cos(TAU*j/segments), r * math.sin(TAU*j/segments), z)
          for z, r in profile for j in range(segments)]
    faces = []
    for k in range(len(profile)-1):
        for j in range(segments):
            q = (j+1) % segments
            faces.append((k*segments+j,k*segments+q,(k+1)*segments+q,(k+1)*segments+j))
    if caps:
        faces.append(tuple(reversed(range(segments))))
        faces.append(tuple((len(profile)-1)*segments+j for j in range(segments)))
    mesh(g, mat, transform(vs, center, basis_z(axis)), faces, True)


def cylinder(g, mat, p1, p2, radius, segments=16):
    a, b = Vector(p1), Vector(p2)
    d = b-a
    if d.length < .0001:
        return
    lathe(g, mat, (a+b)/2, [(-d.length/2,radius),(d.length/2,radius)], segments, d)


def torus(g, mat, center, radius, tube, axis=(0,0,1), segments=64, sides=8):
    vs=[]
    for i in range(segments):
        a=TAU*i/segments
        for j in range(sides):
            b=TAU*j/sides
            rr=radius+tube*math.cos(b)
            vs.append((rr*math.cos(a),rr*math.sin(a),tube*math.sin(b)))
    fs=[]
    for i in range(segments):
        for j in range(sides):
            fs.append((i*sides+j,((i+1)%segments)*sides+j,((i+1)%segments)*sides+(j+1)%sides,i*sides+(j+1)%sides))
    mesh(g,mat,transform(vs,center,basis_z(axis)),fs,True)


def rounded_box(g, mat, center, size, bevel=.15, rotation=0):
    # Extruded rounded rectangle, radius in the XY plane, with bevelled top/bottom.
    sx,sy,sz=size
    r=min(bevel,sx*.45,sy*.45,sz*.45)
    outline=[]
    for cx,cy,start in [(sx/2-r,sy/2-r,0),(-sx/2+r,sy/2-r,90),(-sx/2+r,-sy/2+r,180),(sx/2-r,-sy/2+r,270)]:
        for step in range(5):
            a=math.radians(start+step*22.5)
            outline.append((cx+r*math.cos(a),cy+r*math.sin(a)))
    levels=[(-sz/2,.96),(-sz/2+r,1),(sz/2-r,1),(sz/2,.96)]
    vs=[(x*s,y*s,z) for z,s in levels for x,y in outline]
    n=len(outline)
    fs=[tuple(reversed(range(n))),tuple(3*n+i for i in range(n))]
    fs += [(k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i) for k in range(3) for i in range(n)]
    rot=Matrix.Rotation(rotation,3,'Z') if isinstance(rotation,(int,float)) else rotation
    mesh(g,mat,transform(vs,center,rot),fs,False)


def label(g, content, center, size=.5, angle=0, mat="MB_Sign"):
    # Font curve converted to mesh at the end; local text XY plane faces -Y.
    curve=bpy.data.curves.new("Label_"+content.replace('\n','_'), type='FONT')
    curve.body=content
    curve.size=size
    curve.align_x=enum_choice(curve,"align_x","CENTER")
    curve.align_y=enum_choice(curve,"align_y","CENTER")
    curve.extrude=.003
    curve.resolution_u=3
    obj=bpy.data.objects.new("Sign_"+content.replace('\n','_'),curve)
    collections[g].objects.link(obj)
    obj.location=center
    obj.rotation_euler=(math.pi/2,0,angle)
    obj.data.materials.append(materials[mat])
    text_objects.append(obj)


def radial(center,r,a,z):
    return (center[0]+r*math.cos(a),center[1]+r*math.sin(a),z)


def radial_window(g, center, radius, angle, z, width, height=.64):
    p=radial(center,radius+.065,angle,z)
    rot=angle+math.pi/2
    rounded_box(g,"MB_Dark",p,(width+.18,.14,height+.22),.08,rot)
    p2=radial(center,radius+.145,angle,z)
    box(g,"MB_WindowGlow",p2,(width,.045,height),rot)
    # Warm interior strips and thick dark mullions make glazing feel inhabited.
    for s in [-.32,.32]:
        x=p2[0]-math.sin(angle)*width*s
        y=p2[1]+math.cos(angle)*width*s
        box(g,"MB_Metal",(x,y,z),(.045,.09,height+.08),rot)
    box(g,"MB_Metal",(p2[0],p2[1],z-.06),(width+.1,.08,.045),rot)


def support_leg(g,center,r,a,base,top):
    upper=radial(center,r*.94,a,top)
    lower=radial(center,r+1.05,a,base+.24)
    cylinder(g,"MB_Ceramic",upper,lower,.18,12)
    cylinder(g,"MB_Metal",upper,(lower[0],lower[1],base+.7),.075,10)
    rounded_box(g,"MB_Metal",(lower[0],lower[1],base+.12),(.85,.70,.24),.10,a)
    torus(g,"MB_Orange",upper,.23,.065,axis=Vector(lower)-Vector(upper),segments=12,sides=6)


def rail_line(g,a,b,height=1.02):
    av,bv=Vector(a),Vector(b)
    length=(bv-av).length
    count=max(1,int(length/1.6))
    for i in range(count+1):
        p=av.lerp(bv,i/count)
        cylinder(g,"MB_Metal",p,p+Vector((0,0,height)),.045,8)
    for z in [height*.52,height]:
        cylinder(g,"MB_Orange",av+Vector((0,0,z)),bv+Vector((0,0,z)),.042,8)


def stairs(g,center,angle,landing_z=1.7,width=1.9):
    # Angle is outward radial direction. Descend away from habitat.
    d=Vector((math.cos(angle),math.sin(angle),0))
    t=Vector((-d.y,d.x,0))
    c=Vector(center)
    n=max(5,int(landing_z/.22))
    run=n*.32
    rot=angle+math.pi/2
    for i in range(n):
        p=c+d*(.32*i)
        z=landing_z*(1-i/n)
        box(g,"MB_Metal",(p.x,p.y,z),(width,.34,.12),rot)
        cylinder(g,"MB_Sign",(p-t*width/2+Vector((0,0,z+.07))),(p+t*width/2+Vector((0,0,z+.07))),.024,6)
    for side in [-1,1]:
        a=c+t*(width/2*side)+Vector((0,0,landing_z-.15))
        b=c+d*run+t*(width/2*side)+Vector((0,0,.05))
        cylinder(g,"MB_Metal",a,b,.12,8)
        rail_line(g,a+Vector((0,0,.15)),b+Vector((0,0,.15)),.9)
    rounded_box(g,"MB_Metal",(c.x,c.y,landing_z), (width,1,.14),.06,rot)


def door(g,center,r,angle,base,height=2.55):
    rot=angle+math.pi/2
    p=radial(center,r+.12,angle,base+height/2)
    rounded_box(g,"MB_Orange",p,(1.9,.40,height+.5),.3,rot)
    p2=radial(center,r+.34,angle,base+height/2)
    rounded_box(g,"MB_Dark",p2,(1.38,.14,height),.25,rot)
    p3=radial(center,r+.43,angle,base+height/2)
    rounded_box(g,"MB_Ceramic",p3,(1.12,.055,height-.21),.22,rot)
    win=radial(center,r+.49,angle,base+height*.70)
    rounded_box(g,"MB_Dark",win,(.6,.06,.72),.12,rot)
    win=radial(center,r+.53,angle,base+height*.70)
    box(g,"MB_WindowGlow",win,(.44,.03,.5),rot)
    control=radial(center,r+.52,angle,base+height*.38)
    box(g,"MB_Orange",control,(.45,.08,.12),rot)
    # Latching rods across each side of the airlock.
    for side in [-1,1]:
        v=Vector(p3)+Vector((-math.sin(angle),math.cos(angle),0))*(side*.68)
        cylinder(g,"MB_Metal",v+Vector((0,0,-height*.33)),v+Vector((0,0,height*.33)),.045,8)
    c=Vector(radial(center,r+1,angle,0))
    stairs(g,c,angle,base,width=1.85)


def roof_vent(g,x,y,z,r=.56,height=.9):
    lathe(g,"MB_Ceramic",(x,y,z),[(0,r),(.15,r*1.1),(height*.72,r*1.1),(height,r*.85)],24)
    for dz in [.22,.39,.56]:
        torus(g,"MB_Dark",(x,y,z+dz),r*1.11,.045,segments=24,sides=6)
    torus(g,"MB_Orange",(x,y,z+.12),r*1.1,.065,segments=24,sides=6)


def habitat(name,center,r=5.2,height=5.5,base=1.5,command=False):
    g=module(name)
    x,y=center
    profile=[(base,r*.86),(base+.16,r*.93),(base+.45,r*.99),(base+.85,r),
             (base+height-.85,r),(base+height-.45,r*.99),(base+height-.16,r*.93),(base+height,r*.86)]
    lathe(g,"MB_Ceramic",(x,y,0),profile,96 if command else 72)
    lathe(g,"MB_Dark",(x,y,0),[(base-.17,r*.79),(base+.10,r*.87)],72)
    for z in [base+.73,base+height-.70]:
        lathe(g,"MB_Orange",(x,y,0),[(z-.15,r*1.003),(z+.15,r*1.003)],72)
    for z in [base+.42,base+height-.42]:
        torus(g,"MB_Metal",(x,y,z),r*.982,.032,segments=72,sides=6)
    # Panel lines wrap around the rounded cap and continue down the straight shell.
    panels=24 if command else 18
    for i in range(panels):
        a=TAU*i/panels
        for (za,ra),(zb,rb) in zip(profile,profile[1:]):
            cylinder(g,"MB_Metal",radial(center,ra+.014,a,za),radial(center,rb+.014,a,zb),.012,5)
        for z in [base+.9,base+height-.9]:
            for da in [-.019,.019]:
                p=radial(center,r+.023,a+da,z)
                cylinder(g,"MB_Metal",p,radial(center,r+.055,a+da,z),.031,6)
    # Ring of selective slit windows, each glazed with three panels.
    for i in range(12 if command else 10):
        a=TAU*i/(12 if command else 10)+.06
        if abs(math.atan2(math.sin(a+math.pi/2),math.cos(a+math.pi/2))) < .2:
            continue
        radial_window(g,center,r,a,base+height*.67,2.0 if command else 1.5,.67)
    if command:
        for a in [math.radians(v) for v in [203,229,255,281,307,333,359,25]]:
            radial_window(g,center,r,a,base+height*.31,1.5,.54)
        label(g,"ARES",(x,y-r-.1,base+height*.84),1.4)
        label(g,"01  /  COMMAND",(x,y-r-.13,base+height*.77),.27)
        # External vertical pressure pipes, fixed with individual brackets.
        for a in [math.radians(v) for v in [30,150,205,325]]:
            p=radial(center,r+.24,a,0)
            cylinder(g,"MB_Metal",(p[0],p[1],base+.35),(p[0],p[1],base+height-.55),.18,14)
            for z in [base+1,base+4,base+7,base+10]:
                torus(g,"MB_Orange",(p[0],p[1],z),.20,.06,segments=16,sides=6)
        torus(g,"MB_Metal",(x,y,base+height+.18),r*.73,.09,segments=72,sides=8)
        for i in range(20):
            a=TAU*i/20
            cylinder(g,"MB_Metal",radial(center,r*.73,a,base+height),radial(center,r*.73,a,base+height+1.05),.04,8)
        torus(g,"MB_Orange",(x,y,base+height+1.05),r*.73,.045,segments=72,sides=6)
    else:
        label(g,name.split('_')[0],(x,y-r-.1,base+height*.85),.55)
        label(g,"ARES  /  PRESSURIZED",(x,y-r-.11,base+height*.75),.15)
    for i in range(10 if command else 8):
        support_leg(g,center,r,TAU*i/(10 if command else 8),0,base+.3)
    door(g,center,r,-math.pi/2,base)
    for dx,dy,vr in [(-1.7,1.2,.55),(1.3,1.1,.7),(1.3,-1.1,.4)]:
        roof_vent(g,x+dx,y+dy,base+height-.04,vr)
    # White roof service hatch, central signal beacon.
    lathe(g,"MB_Metal",(x,y,base+height),[(0,1.2),(.2,1.2),(.25,1.0)],36)
    cylinder(g,"MB_Metal",(x,y,base+height+.2),(x,y,base+height+1.35),.055,10)
    lathe(g,"MB_RedLight",(x,y,base+height+1.35),[(0,.13),(.26,.13),(.34,.06)],12)
    return g


habitat("HAB-02_West",(-20,-4),5.4,5.2)
habitat("HAB-03_Foreground",(-5,-13),5.3,5.5)
habitat("SCI-04_East",(22,7),5.0,5.2)
habitat("LAB-05_Rear",(-10,19),4.6,5.0)
habitat("ARES-01_Command",(4,8),6.8,12.5,1.65,True)


def corridor(name,a,b,ra,rb,radius=1.65,z=3.35):
    g=module(name)
    av,bv=Vector((*a,z)),Vector((*b,z))
    d=(bv-av).normalized()
    start=av+d*(ra-.18)
    end=bv-d*(rb-.18)
    length=(end-start).length
    side=Vector((-d.y,d.x,0))
    mid=(start+end)/2
    lathe(g,"MB_Ceramic",mid,[(-length/2,radius*.94),(-length/2+.23,radius),
          (length/2-.23,radius),(length/2,radius*.94)],48,d)
    angle=math.atan2(d.y,d.x)
    count=max(3,int(length/1.15))
    for i in range(count+1):
        p=start.lerp(end,i/count)
        torus(g,"MB_Metal",p,radius+.025,.06,axis=d,segments=40,sides=6)
        if i < count:
            w=length/count-.18
            q=start.lerp(end,(i+.5)/count)
            for s in [-1,1]:
                frame=q+side*(s*(radius*.962))
                box(g,"MB_Dark",frame,(w,.12,.73),angle)
                glass=frame+side*s*.072
                box(g,"MB_WindowGlow",glass,(w-.12,.035,.49),angle)
                box(g,"MB_Metal",glass,(.04,.06,.59),angle)
    for i in [0,1]:
        p=start if i==0 else end
        cylinder(g,"MB_Dark",p-d*.22,p+d*.22,radius+.13,48)
        for off in [-.29,0,.29]:
            torus(g,"MB_Orange",p+d*off,radius+.15,.075,axis=d,segments=48,sides=6)
        for j in range(12):
            ang=TAU*j/12
            q=p+side*(math.cos(ang)*(radius+.18))+Vector((0,0,math.sin(ang)*(radius+.18)))
            cylinder(g,"MB_Metal",q-d*.12,q+d*.12,.06,8)
    for f in [.20,.5,.80]:
        p=start.lerp(end,f)
        for s in [-1,1]:
            q=p+side*s*radius*.7
            cylinder(g,"MB_Metal",(q.x,q.y,.25),(q.x,q.y,z-radius*.58),.12,10)
            rounded_box(g,"MB_Ceramic",(q.x,q.y,.13),(.65,.65,.26),.08)
        cylinder(g,"MB_Metal",p-side*radius*.8-Vector((0,0,radius*.62)),p+side*radius*.8-Vector((0,0,radius*.62)),.12,10)
    cylinder(g,"MB_Orange",start+Vector((0,0,radius+.10)),end+Vector((0,0,radius+.10)),.07,10)


corridor("LINK-01_West_Front",(-20,-4),(-5,-13),5.4,5.3)
corridor("LINK-02_Front_Command",(-5,-13),(4,8),5.3,6.8)
corridor("LINK-03_Command_East",(4,8),(22,7),6.8,5)
corridor("LINK-04_West_Rear",(-20,-4),(-10,19),5.4,4.6)
corridor("LINK-05_Rear_Command",(-10,19),(4,8),4.6,6.8)


def plant(g,x,y,z,scale=1):
    cylinder(g,"MB_Green",(x,y,z),(x,y,z+scale*.8),.025*scale,6)
    for i in range(5):
        a=i*2.4
        h=z+scale*(.17+i*.12)
        spread=scale*(.5-i*.035)
        tip=Vector((x+math.cos(a)*spread,y+math.sin(a)*spread,h+scale*.25))
        root=Vector((x,y,h))
        mid=root.lerp(tip,.55)+Vector((0,0,scale*.13))
        side=Vector((-math.sin(a),math.cos(a),0))*scale*.14
        mesh(g,"MB_GreenLight" if i%2 else "MB_Green",[root,mid+side,tip,mid-side],[(0,1,2,3)],False)


def greenhouse(name,center,r,height):
    g=module(name)
    x,y=center
    base=.62
    lathe(g,"MB_Ceramic",(x,y,0),[(.12,r*.99),(.25,r*1.035),(.68,r*1.035),(.82,r*.99)],72)
    torus(g,"MB_Orange",(x,y,.68),r*1.02,.09,segments=72,sides=8)
    # A tessellated hemispherical greenhouse, individual triangular glazed panels.
    rings=6
    n=24
    vertices=[]
    for k in range(rings+1):
        phi=(math.pi/2-.025)*k/rings
        for j in range(n):
            a=TAU*(j+(k%2)*.5)/n
            vertices.append((x+r*math.cos(phi)*math.cos(a),y+r*math.cos(phi)*math.sin(a),base+height*math.sin(phi)))
    fs=[]
    edges=set()
    for k in range(rings):
        for j in range(n):
            a=k*n+j;b=k*n+(j+1)%n;c=(k+1)*n+j;d=(k+1)*n+(j+1)%n
            fs.extend([(a,b,c),(b,d,c)])
            for e in [(a,b),(a,c),(b,c),(b,d),(c,d)]:
                edges.add(tuple(sorted(e)))
    mesh(g,"MB_Glass",vertices,fs,False)
    for a,b in sorted(edges):
        cylinder(g,"MB_Ceramic",vertices[a],vertices[b],.047 if r>7 else .04,7)
    for j in range(n):
        a=TAU*j/n
        p=radial(center,r,a,.7)
        cylinder(g,"MB_Metal",(p[0],p[1],.20),(p[0],p[1],.87),.07,8)
    roof_vent(g,x,y,base+height,.42,.55)
    # Visible raised hydroponic beds and a clear circulation aisle.
    for ix in [-2,-1,1,2]:
        xx=x+ix*r*.26
        max_y=math.sqrt(max(0,(r*.8)**2-(xx-x)**2))
        length=max_y*1.6
        rounded_box(g,"MB_Metal",(xx,y,.91),(r*.19,length,.42),.14)
        box(g,"MB_RedSoil",(xx,y,1.13),(r*.17,length-.15,.05))
        for j in range(max(3,int(length/.6))):
            yy=y-length*.44+j*(length*.88/max(1,int(length/.6)-1))
            for dx in [-r*.045,r*.045]:
                plant(g,xx+dx,yy,1.15,.55+(j%3)*.14)
        cylinder(g,"MB_WindowGlow",(xx-r*.095,y-length*.46,.98),(xx-r*.095,y+length*.46,.98),.025,6)
    box(g,"MB_Metal",(x,y,.8),(.95,r*1.65,.12))
    # Greenhouse airlock at front and a service water line along the perimeter.
    rounded_box(g,"MB_Ceramic",(x,y-r-.24,1.4),(2.2,1.5,2.3),.35)
    rounded_box(g,"MB_Orange",(x,y-r-1.02,1.4),(1.65,.12,2.05),.25)
    rounded_box(g,"MB_Dark",(x,y-r-1.1,1.4),(1.32,.08,1.75),.2)
    box(g,"MB_Glass",(x,y-r-1.16,1.65),(1.05,.04,1.0))
    label(g,"BIO / "+name.split('-')[1].split('_')[0],(x,y-r-1.19,2.12),.22)
    torus(g,"MB_Metal",(x,y,.43),r+0.45,.13,segments=72,sides=8)
    for j in range(12):
        a=TAU*j/12
        p=radial(center,r+.45,a,.43)
        torus(g,"MB_Orange",p,.15,.045,axis=(-math.sin(a),math.cos(a),0),segments=12,sides=6)


greenhouse("BIO-06_Greenhouse",(24,-12),7.5,5.6)
greenhouse("BIO-07_Greenhouse",(-24,14),6.0,4.6)


def tower(name,center,height,base_z=0):
    g=module(name)
    x,y=center
    legs=3
    levels=7
    for k in range(levels):
        z1=base_z+height*k/levels
        z2=base_z+height*(k+1)/levels
        r1=1.05*(1-k/levels)+.22
        r2=1.05*(1-(k+1)/levels)+.22
        for j in range(legs):
            a=TAU*j/legs
            b=TAU*(j+1)/legs
            p1=radial(center,r1,a,z1);p2=radial(center,r2,a,z2)
            q1=radial(center,r1,b,z1);q2=radial(center,r2,b,z2)
            cylinder(g,"MB_Metal",p1,p2,.08,10)
            cylinder(g,"MB_Metal",p1,q2,.035,8)
            cylinder(g,"MB_Metal",q1,p2,.035,8)
            cylinder(g,"MB_Orange",p2,q2,.05,8)
    for j in range(3):
        a=TAU*j/3
        p=radial(center,1.27,a,base_z+.12)
        rounded_box(g,"MB_Ceramic",p,(.8,.8,.24),.08)
    cylinder(g,"MB_Metal",(x,y,base_z+height),(x,y,base_z+height+3.4),.045,8)
    for off in [-.4,.4]:
        cylinder(g,"MB_Metal",(x+off,y,base_z+height-.5),(x+off,y,base_z+height+1.8),.025,8)
    for z in [.75,.87]:
        cylinder(g,"MB_Metal",(x-1.1,y,base_z+height*z),(x+1.1,y,base_z+height*z),.055,8)
    lathe(g,"MB_RedLight",(x,y,base_z+height+.1),[(0,.12),(.25,.12)],12)


tower("COM-08_West_Mast",(-20,-3.5),11,6.7)
tower("COM-09_Command_Mast",(2.3,9.4),7.7,14.2)
tower("COM-10_East_Mast",(29,11),12.8,.0)
tower("COM-11_Back_Mast",(-11,24),13.5,.0)


def dish(name,center,radius=2.2,aim=(0,-.7,.7)):
    g=module(name)
    c=Vector(center)
    axis=Vector(aim).normalized()
    rotation=basis_z(axis)
    profile=[(.0,.06)]
    for i in range(1,9):
        r=radius*i/8
        profile.append((r*r/(radius*3),r))
    lathe(g,"MB_Ceramic",c,profile,48,axis,False)
    rim=c+axis*profile[-1][0]
    torus(g,"MB_Metal",rim,radius,.055,axis,48,8)
    feed=c+axis*(radius*.93)
    for a in [0,TAU/3,2*TAU/3]:
        p=c+rotation@Vector((radius*.85*math.cos(a),radius*.85*math.sin(a),radius*.85**2/3))
        cylinder(g,"MB_Orange",p,feed,.035,8)
    cylinder(g,"MB_Dark",feed-axis*.12,feed+axis*.24,.12,12)
    cylinder(g,"MB_Metal",c-Vector((0,0,1.1)),c,.16,12)
    rounded_box(g,"MB_Metal",c-Vector((0,0,1.2)),(1,1,.25),.1)
    # Radial dish ribs visible on the reverse side.
    for i in range(12):
        a=TAU*i/12
        prev=None
        for z,r in profile:
            p=c+rotation@Vector((r*math.cos(a),r*math.sin(a),z-.04))
            if prev is not None:
                cylinder(g,"MB_Metal",prev,p,.025,6)
            prev=p


dish("SAT-12_Command_Dish",(6.6,8.0,16.1),2.25,(.1,-.7,.71))
dish("SAT-13_West_Dish",(-22,14,7.0),1.35,(-.35,-.7,.65))
dish("SAT-14_East_Dish",(21,8,8.0),1.45,(.4,-.65,.65))


def service_yard():
    g=module("UTIL-15_Power_Water")
    # Solar array behind east lab, pitched racks, visible individual cells.
    for ix in range(3):
        for iy in range(2):
            x=16+ix*4.7;y=19+iy*3.9
            tilt=Matrix.Rotation(math.radians(22),3,'X')
            center=Vector((x,y,2.15))
            box(g,"MB_Metal",center,(4.15,3.25,.17),tilt)
            for cx in range(6):
                for cy in range(4):
                    p=center+tilt@Vector(((cx-2.5)*.64,(cy-1.5)*.74,.11))
                    box(g,"MB_Blue",p,(.59,.69,.035),tilt)
                    # Silver busbars distinguish photovoltaics from plain blue slabs.
                    for bx in [-.14,.14]:
                        b=center+tilt@Vector(((cx-2.5)*.64+bx,(cy-1.5)*.74,.135))
                        box(g,"MB_Metal",b,(.01,.69,.01),tilt)
            for dx in [-1.4,1.4]:
                cylinder(g,"MB_Metal",(x+dx,y,.18),(x+dx,y,2.1),.09,10)
                rounded_box(g,"MB_Ceramic",(x+dx,y,.12),(.6,.8,.24),.08)
    # Four upright oxygen/water tanks.
    for i in range(4):
        x=8+i*1.3;y=-7.0
        lathe(g,"MB_Ceramic",(x,y,0),[(.1,.45),(.25,.56),(3.4,.56),(3.65,.38),(3.74,.1)],32)
        for z in [.6,2.95]:
            torus(g,"MB_Orange",(x,y,z),.57,.065,segments=32,sides=6)
        cylinder(g,"MB_Metal",(x,y,3.7),(x,y,3.97),.065,10)
        cylinder(g,"MB_Metal",(x,y,3.97),(x,y+.75,3.97),.065,10)
        label(g,"O2" if i%2==0 else "H2O",(x,y-.58,2.08),.25)
    # Service cabinets with louvres, switches and safety labels.
    for i in range(3):
        x=8.4+i*1.65;y=-3.8
        rounded_box(g,"MB_Ceramic",(x,y,1.45),(1.35,1.15,2.5),.14)
        box(g,"MB_Dark",(x,y-.59,1.45),(1.1,.03,1.95))
        for k in range(9):
            box(g,"MB_Metal",(x,y-.64,.8+k*.14),(.85,.07,.06))
        box(g,"MB_Orange",(x,y-.65,2.35),(1.1,.06,.24))
        label(g,"PWR "+str(i+1),(x,y-.69,2.35),.17)
    # External bundled utility pipes, with regular expansion collars and stools.
    for idx,offset in enumerate([0,.42,.84]):
        start=Vector((6,-16-offset,.55+idx*.05));end=Vector((19,-16-offset,.55+idx*.05))
        cylinder(g,"MB_Metal",start,end,.13,14)
        for j in range(9):
            p=start.lerp(end,j/8)
            torus(g,"MB_Orange",p,.15,.045,axis=(1,0,0),segments=14,sides=6)
        for j in [0,3,6,8]:
            p=start.lerp(end,j/8)
            rounded_box(g,"MB_Ceramic",(p.x,p.y,.18),(.45,.55,.36),.07)
    # Battered cargo crates only as modest architectural set dressing.
    for x,y,z,s in [(-10,-8,.45,.85),(-10.9,-8.1,.4,.75),(13,-1,.5,1),(13,1,.5,1)]:
        rounded_box(g,"MB_Ceramic",(x,y,z),(s,s,z*2),.10)
        for off in [-.31,.31]:
            box(g,"MB_Orange",(x+off*s,y,z),(.08,s+.015,z*2+.02))
        box(g,"MB_Dark",(x,y-s*.51,z),(s*.45,.03,.13))


service_yard()

# Two pedestrian paths leave foreground staging open for the generated rover.
g=module("SITE-16_Grated_Walkways")
for a,b,width in [((-4,-20),(6,-20),1.5),((6,-20),(6,-8),1.5),((-18,-12),(-11,-16),1.2)]:
    av,bv=Vector((*a,.15)),Vector((*b,.15))
    d=(bv-av);length=d.length;angle=math.atan2(d.y,d.x)
    box(g,"MB_Metal",(av+bv)/2,(length,width,.18),angle)
    count=int(length/.23)
    for i in range(count+1):
        p=av.lerp(bv,i/count)+Vector((0,0,.10))
        box(g,"MB_Dark",p,(.065,width*.91,.012),angle)
    side=Vector((-d.y,d.x,0)).normalized()
    for s in [-1,1]:
        cylinder(g,"MB_Orange",av+side*width*s*.48,bv+side*width*s*.48,.035,8)


# Build one mesh per module and material. This preserves meaningful Unity children
# while keeping draw calls and object counts much lower than primitive-by-primitive.
architecture=[]
for (group,matname),batch in batches.items():
    datablock=bpy.data.meshes.new(group+"_"+matname)
    datablock.from_pydata(batch.verts,[],batch.faces)
    datablock.update()
    datablock.materials.append(materials[matname])
    for poly,smooth in zip(datablock.polygons,batch.smooth):
        poly.use_smooth=smooth
    # UV0 in metre-scaled box projection: useful for tileable Unity weathering.
    uv=datablock.uv_layers.new(name="UVMap")
    for poly in datablock.polygons:
        n=poly.normal
        axis=max(range(3),key=lambda k: abs(n[k]))
        axes=[k for k in range(3) if k!=axis]
        for li in poly.loop_indices:
            v=datablock.vertices[datablock.loops[li].vertex_index].co
            uv.data[li].uv=(v[axes[0]]*.25,v[axes[1]]*.25)
    obj=bpy.data.objects.new(group+"__"+matname,datablock)
    collections[group].objects.link(obj)
    obj["module"]=group
    architecture.append(obj)

# Convert all readable labels without using context-sensitive operators.
depsgraph=bpy.context.evaluated_depsgraph_get()
for obj in text_objects:
    evaluated=obj.evaluated_get(depsgraph)
    datablock=bpy.data.meshes.new_from_object(evaluated,depsgraph=depsgraph)
    replacement=bpy.data.objects.new(obj.name,datablock)
    replacement.matrix_world=obj.matrix_world.copy()
    for col in list(obj.users_collection):
        col.objects.link(replacement)
    if not replacement.data.materials:
        replacement.data.materials.append(materials["MB_Sign"])
    bpy.data.objects.remove(obj,do_unlink=True)
    architecture.append(replacement)

# Render and viewport apparatus are excluded from the export.
preview=module("_Preview_Camera_Lights")
camera_data=bpy.data.cameras.new("ARES_Overview_Camera")
camera=bpy.data.objects.new("ARES_Overview_Camera",camera_data)
collections[preview].objects.link(camera)
camera.location=(65,-85,48)
target=Vector((0,2,6))
camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type=enum_choice(camera_data,"type","PERSP")
camera_data.lens=46
camera_data.clip_end=1000
scene.camera=camera
for name,kind,location,energy,color,size in [
    ("Mars_Golden_Key","AREA",(22,-35,48),2800,(1,.65,.37),25),
    ("Mars_Cool_Fill","AREA",(-30,-10,32),1800,(.49,.65,1),30),
    ("Mars_Rim","AREA",(8,28,35),2500,(1,.38,.16),18)]:
    # Light type identifiers are checked through RNA before data-block creation.
    allowed=[i.identifier for i in bpy.types.Light.bl_rna.properties["type"].enum_items]
    if kind not in allowed:
        raise RuntimeError("Unsupported light type "+kind)
    data=bpy.data.lights.new(name,kind)
    data.energy=energy;data.color=color
    if hasattr(data,"shape"):
        data.shape=enum_choice(data,"shape","DISK")
    if hasattr(data,"size"):
        data.size=size
    obj=bpy.data.objects.new(name,data)
    collections[preview].objects.link(obj)
    obj.location=location
    obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
world=bpy.data.worlds.new("Mars_Preview_World")
world.use_nodes=True
background=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
background.inputs["Color"].default_value=(.10,.045,.025,1)
background.inputs["Strength"].default_value=.45
scene.world=world
scene.render.resolution_x=1600
scene.render.resolution_y=1000
scene.render.resolution_percentage=100
try:
    scene.render.engine='BLENDER_EEVEE'
except TypeError:
    pass
scene.render.image_settings.file_format=enum_choice(scene.render.image_settings,"file_format","PNG")
scene.render.filepath=os.path.join(SOURCE,"MarsBase_Architecture_Preview.png")

# Select only the authored architecture and export in Unity-compatible axes.
for obj in scene.objects:
    obj.select_set(False)
for obj in architecture:
    obj.select_set(True)
bpy.context.view_layer.objects.active=architecture[0]
fbx_op=bpy.ops.export_scene.fbx
fbx_args=dict(filepath=os.path.join(EXPORT,"MarsBase_Architecture.fbx"),use_selection=True,
              global_scale=1.0,apply_unit_scale=True,bake_space_transform=False,
              axis_forward=op_enum(fbx_op,"axis_forward","-Z"),
              axis_up=op_enum(fbx_op,"axis_up","Y"),
              object_types={op_enum(fbx_op,"object_types","MESH")},
              use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,
              path_mode=op_enum(fbx_op,"path_mode","AUTO"),
              mesh_smooth_type=op_enum(fbx_op,"mesh_smooth_type","FACE"))
fbx_op(**fbx_args)

points=[obj.matrix_world@Vector(corner) for obj in architecture for corner in obj.bound_box]
minimum=[min(p[k] for p in points) for k in range(3)]
maximum=[max(p[k] for p in points) for k in range(3)]
triangles=0
for obj in architecture:
    obj.data.calc_loop_triangles()
    triangles+=len(obj.data.loop_triangles)
report={
    "scene":scene.name,"blender_version":bpy.app.version_string,"units":"metres",
    "coordinates":"Blender Z-up; front -Y; FBX -Z forward +Y up",
    "architecture_objects":len(architecture),"triangles":triangles,
    "bounds_min":minimum,"bounds_max":maximum,
    "dimensions":[maximum[k]-minimum[k] for k in range(3)],
    "materials":list(materials),"modules":[n for n in collections if not n.startswith('_')],
    "rover_staging":{"center":[-18,-20,0],"clearance_radius":4.5},
    "generator_staging":{"center":[10,-21,0],"clearance_radius":3.0},
    "terrain":False,
    "art_direction":"Cream porcelain capsule pressure habitats, safety-orange bands, warm slit windows, triangulated horticulture domes, analog communications and exposed utility piping",
    "authored_geometry":"Central command hub, four habitat/lab pods, five ribbed corridors, two glazed greenhouses and crops, four lattice masts, three dishes, solar utility yard, tanks, cabinets, pipes, stairs, walkways",
    "generated_assets":"Reserved separately for Meshy T2.5 rover and generator only",
    "export_fbx":fbx_args["filepath"],
    "source_blend":os.path.join(SOURCE,"MarsBase_RetroFuture.blend")
}
with open(os.path.join(SOURCE,"design_manifest.json"),'w',encoding='utf-8') as f:
    json.dump(report,f,ensure_ascii=False,indent=2)
bpy.ops.wm.save_as_mainfile(filepath=report["source_blend"])
print("MARS_BASE_BUILD_COMPLETE "+json.dumps(report,ensure_ascii=False))
