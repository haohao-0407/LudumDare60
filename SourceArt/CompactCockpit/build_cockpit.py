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
    scene=bpy.data.scenes.new('CompactCockpit');bpy.context.window.scene=scene;b.scene=scene
    scene.unit_settings.system=b.enum(scene.unit_settings,'system','METRIC');scene.unit_settings.scale_length=1
    root=bpy.data.objects.new('COCKPIT_OnePilot_1.9x2.2x2.1m',None);scene.collection.objects.link(root)
    root['interior_metres']=[1.9,2.2,2.1];root['scope']='Complete enclosure and front flight console. Remaining interior intentionally empty.'
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
    # All six sides are real geometry. Side upper chamfers establish a pressure cabin.
    panel('Floor_Continuous','01_Floor','Dark',(0,0,-.055),(2.04,2.34,.11),.04)
    for x in [-.467,.467]:
        for y in [-.72,0,.72]:
            panel(None,'01_Floor','Floor',(x,y,.008),(.916,.70,.016),.015)
            for xx in [-.416,.416]:
                for yy in [-.30,.30]:bolt('01_Floor',(x+xx,y+yy,.018),r=.004)
    # Plain side wall panels and a thin charcoal frame, no furnishing.
    for side,g in [(-1,'02_LeftWall'),(1,'03_RightWall')]:
        R=Matrix(((0,0,-side),(-side,0,0),(0,1,0)))
        panel('Wall_'+str(side)+'_Continuous',g,'Dark',(side*1.005,0,.96),(2.34,1.92,.11),.025,R)
        for y in [-.735,0,.735]:
            panel(None,g,'Wall',(side*.947,y,1.055),(.716,1.55,.025),.026,R)
            panel(None,g,'Ivory',(side*.945,y,.185),(.716,.145,.029),.014,R)
            for zz in [.335,1.775]:
                for yy in [-.317,.317]:bolt(g,(side*.929,y+yy,zz),R,.0045)
        panel(None,g,'Orange',(side*.925,0,.305),(2.16,.025,.012),.004,R)
        # The continuous roof chamfer meets the flat ceiling.
        a=Vector((side*.95,0,1.84));c=Vector((side*.69,0,2.10));v=(c-a).normalized();u=Vector((0,1,0));n=u.cross(v)
        if side>0:u=-u;n=u.cross(v)
        RR=Matrix((u,v,n)).transposed()
        panel('RoofChamfer_'+str(side),'06_Ceiling','Dark',(a+c)/2,(2.31,(c-a).length+.045,.08),.016,RR)
        for y in [-.735,0,.735]:panel(None,'06_Ceiling','Wall',Vector((side*.82,y,1.97))+n*.049,(.713,.34,.02),.015,RR)
        # One unobtrusive warm linear luminaire along each shoulder.
        panel(None,'11_Lighting','Dark',Vector((side*.82,-.17,1.97))+n*.068,(1.70,.055,.04),.016,RR)
        panel(None,'11_Lighting','Warm',Vector((side*.82,-.17,1.97))+n*.095,(1.57,.026,.016),.009,RR)
    panel('Roof_Continuous','06_Ceiling','Dark',(0,0,2.145),(1.47,2.34,.09),.02)
    for y in [-.74,0,.74]:panel(None,'06_Ceiling','Wall',(0,y,2.094),(1.35,.71,.022),.018)
    # Front and rear cross-sections overlap seams so the enclosure is light-tight.
    cross=[(-.99,-.04),(.99,-.04),(.99,1.87),(.71,2.15),(-.71,2.15),(-.99,1.87)]
    for y,g in [(1.155,'04_FrontWall'),(-1.155,'05_RearWall')]:
        vs=[(x,yy,z) for yy in [y-.055,y+.055] for x,z in cross];n=len(cross)
        b.begin('Bulkhead_Front' if y>0 else 'Bulkhead_Rear',g)
        b.mesh(g,'Wall',vs,[tuple(range(n)),tuple(reversed(range(n,2*n)))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]);b.end()
    # A restrained flush closed access hatch in the rear wall.
    rear=Matrix.Rotation(math.pi,3,'Z')@FRONT
    panel('RearHatch_Seal','05_RearWall','Dark',(0,-1.091,1.015),(.81,1.88,.022),.075,rear)
    panel('RearHatch_Leaf','05_RearWall','Wall',(0,-1.073,1.015),(.77,1.84,.025),.067,rear)
    panel(None,'05_RearWall','Orange',(0,-1.057,1.85),(.62,.027,.006),.004,rear)
    label('05_RearWall','01 / ACCESS',(0,-1.055,1.73),.031,rear)
    panel(None,'05_RearWall','Dark',(.285,-1.054,1.0),(.047,.17,.009),.012,rear)
    pipe('RearHatch_Handle','05_RearWall',[(.285,-1.046,.95),(.285,-1.015,.97),(.285,-1.015,1.06),(.285,-1.046,1.08)],.009)
    # Restrained upper console-area tubing is deliberately concentrated at the front.
    for x in [-.87,.87]:
        pipe('Console_ServiceConduit','10_Fittings',[(x,.94,.30),(x,1.01,.5),(x,1.015,1.80),(x*.85,1.015,1.99)],.014)
        for z in [.42,.92,1.38,1.79]:
            panel(None,'10_Fittings','Ivory',(x,1.0,z),(.061,.035,.028),.006,FRONT)
    label('04_FrontWall','KESTREL   /   SINGLE PILOT FLIGHT MODULE',(0,1.092,2.018),.023,FRONT)
    print('Complete 1.90 x 2.20 x 2.10m enclosure built')

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
    g='07_ConsoleBody'
    # Shared vertical instrument bank and a shallow wedge-shaped lower desk.
    panel('UpperInstrumentBank',g,'Ivory',(0,1.037,1.595),(1.78,.75,.12),.07,FRONT)
    for side in [-1,1]:
        panel(None,g,'Dark',(side*.84,.985,1.60),(.09,.59,.025),.012,FRONT)
        panel(None,g,'Ivory',(side*.84,.964,1.60),(.077,.575,.026),.011,FRONT)
        for i in range(3):button('BANK_STATUS_%s_%s'%(side,i),(side*.84,.944,1.54+i*.073),w=.033,h=.038)
        knob('BANK_SELECTOR_'+str(side),(side*.84,.946,1.397),r=.022)
    screen('RADAR',(-.421,.941,1.608),.585,.465,FRONT,'radar.png')
    screen('ATTITUDE',(.421,.941,1.608),.585,.465,FRONT,'attitude.png')
    # Central segmented power readout and small annunciator strip.
    panel('MainPowerSpine',g,'Ivory',(0,.899,1.60),(.105,.665,.075),.018,FRONT)
    panel(None,g,'Dark',(0,.855,1.63),(.043,.40,.014),.006,FRONT)
    for i in range(15):panel(None,'09_Controls','Amber' if i<10 else 'Rubber',(0,.844,1.444+i*.025),(.024,.019,.008),.002,FRONT)
    button('MASTER_CAUTION',(0,.847,1.881),w=.058,h=.028)
    label(g,'PWR',(0,.852,1.366),.013,FRONT)
    for i in range(5):button('ANNUNCIATOR_%02d'%i,((i-2)*.049,.85,1.267),w=.037,h=.029,mat='Amber' if i<2 else 'White')
    # Physical grip rails outside the two upper screens.
    for x in [-.776,.776]:
        pipe('UpperBank_GrabHandle','10_Fittings',[(x,.89,1.365),(x,.849,1.39),(x,.849,1.806),(x,.89,1.83)],.010)
        for z in [1.365,1.83]:b.lathe('10_Fittings','Metal',(x,.89,z),[(-.004,.018),(.007,.018)],24,(0,-1,0))
    # Desk chassis extruded across X. Broad enough for elbows, narrow footwell below.
    cross=[(-.19,.67),(.93,.67),(.99,.73),(.99,.94),(.19,.84),(-.19,.78)]
    vs=[(x,y,z) for x in [-.895,.895] for y,z in cross];n=len(cross)
    b.begin('Desk_WedgeChassis',g)
    b.mesh(g,'Ivory',vs,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]);b.end()
    # Console footing against front wall; empty rear floor remains useful.
    for x in [-.76,.76]:
        panel('Console_Foot_'+str(x),g,'Dark',(x,.66,.34),(.23,.71,.68),.025)
        panel(None,g,'Ivory',(x,.292,.35),(.20,.56,.024),.018,FRONT)
        for z in [.15,.23,.31,.39,.47]:panel(None,g,'Dark',(x,.276,z),(.12,.013,.006),.004,FRONT)
    panel(None,g,'Orange',(0,-.192,.744),(1.73,.047,.015),.008,FRONT)
    label(g,'KESTREL  /  FLIGHT CONTROL UNIT   01',(0,-.202,.704),.018,FRONT)
    # Central inclined display, with its own closed sloped mounting bed.
    cross=[(-.025,.80),(.60,.88),(.60,1.13),(-.025,.835)]
    vs=[(x,y,z) for x in [-.505,.505] for y,z in cross];n=len(cross)
    b.begin('Navigation_WedgeMount',g)
    b.mesh(g,'Ivory',vs,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]);b.end()
    panel('Navigation_Deck',g,'Metal',(0,.297,.979),(1.04,.69,.052),.035,DECK)
    screen('NAVIGATION',(0,.291,.995),.843,.516,DECK,'navigation.png')
    for side,tex,name in [(-1,'comms.png','COMMUNICATIONS'),(1,'systems.png','SYSTEMS')]:
        screen(name,(side*.692,.61,1.073),.258,.412,SIDE,tex)
        # Narrow knob rails next to secondary displays, towards central deck.
        for k in range(3):knob('AUX_SELECTOR_%s_%s'%(side,k),(side*.503,.61+(.11-k*.11)*math.cos(math.radians(63)),1.073+(.11-k*.11)*math.sin(math.radians(63))),SIDE,.016)
    # Left tactile bank on a lightly sloping desk.
    keyR=Matrix.Rotation(math.radians(12),3,'X');baseL=Vector((-.689,.035,.845));baseR=Vector((.689,.035,.845))
    for c in [baseL,baseR]:
        panel(None,g,'Metal',c,(.36,.29,.023),.019,keyR)
        panel(None,g,'Ivory',c+keyR.col[2]*.015,(.344,.276,.018),.016,keyR)
        for xx in [-.157,.157]:
            for yy in [-.117,.117]:bolt(g,c+keyR@Vector((xx,yy,.027)),keyR,.0032)
    for j in range(3):
        for i in range(3):
            p=baseL+keyR@Vector(((i-1)*.098,(j-1)*.074,.03))
            if j==2 or i==2:knob('DESK_KNOB_%d_%d'%(i,j),p,keyR,.019)
            else:button('DESK_ACTION_%d_%d'%(i,j),p,keyR,.037,.030,'Amber')
    # Compact four-row keyboard; each key remains a distinct mesh for later wiring.
    chars=['12345678','QWERTYUI','ASDFGHJK','ZXCVBN<>']
    for j,row in enumerate(chars):
        for i,ch in enumerate(row):
            p=baseR+keyR@Vector(((i-3.5)*.036,(1.5-j)*.045,.032))
            button('KEY_%d_%d'%(j,i),p,keyR,.028,.032,'Ivory' if i<7 else 'Dark',ch)
    # Front guard rail frames the navigation deck.
    pipe('Navigation_FrontGrabRail','10_Fittings',[(-.49,-.125,.82),(-.47,-.207,.878),(-.37,-.217,.899),(.37,-.217,.899),(.47,-.207,.878),(.49,-.125,.82)],.014)
    for side in [-1,1]:
        # Vent insert and screw-fastened service panel beneath the displays.
        c=Vector((side*.45,.88,1.257))
        panel(None,g,'Dark',c,(.60,.068,.015),.01,FRONT)
        panel(None,g,'Ivory',c+Vector((0,-.009,0)),(.58,.057,.015),.008,FRONT)
        for i in range(18):panel(None,g,'Dark',c+Vector(((i-8.5)*.023,-.018,0)),(.010,.035,.004),.002,FRONT)
        for xx in [-.276,.276]:bolt(g,c+Vector((xx,-.019,0)),FRONT,.0035)
    # Compact centre switch strip in the gap above the chart display.
    center=Vector((0,.795,1.189))
    panel('Auxiliary_SwitchStrip',g,'Metal',center,(.74,.095,.035),.015,FRONT)
    panel(None,g,'Ivory',center+Vector((0,-.021,0)),(.72,.081,.014),.010,FRONT)
    for i in range(6):
        p=center+Vector(((i-2.5)*.105,-.032,0))
        b.lathe('09_Controls','Metal',p,[(-.003,.012),(.004,.012),(.007,.008)],16,(0,-1,0))
        b.cylinder('09_Controls','Dark',p+Vector((0,-.007,0)),p+Vector((0,-.036,.012)),.0035,12)
        panel('TOGGLE_%02d'%i,'09_Controls','White',p+Vector((0,-.038,.013)),(.010,.021,.012),.004,FRONT)
        label('09_Controls',['RCS','DOCK','COM','NAV','AUX','ARM'][i],p+Vector((0,-.004,.028)),.0075,FRONT)
    # Four rectangular warm task lights, tucked into the front upper corners.
    for side in [-1,1]:
        for j in range(2):
            c=(side*(.54+j*.22),.81,1.996);R=Matrix.Rotation(math.radians(145),3,'X')
            panel(None,'11_Lighting','Dark',c,(.19,.072,.045),.014,R)
            panel(None,'11_Lighting','Warm',Vector(c)+R.col[2]*.025,(.158,.045,.012),.009,R)
    print('Five-screen flight control console built')

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
        g=o.parent.name.removesuffix('_ROOT')
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
    camera('CAM_Pilot',(0,-.98,1.49),(0,.72,1.21),23)
    camera('CAM_ConsoleDetail',(0,-.63,1.48),(0,.70,1.29),26)
    camera('CAM_InteriorWide',(-.13,-1.035,1.69),(.04,.64,.99),17)
    camera('CAM_RearWall',(0,.37,1.53),(0,-1.1,1.02),18)
    camera('CAM_Cutaway',(3.4,-4.3,3.3),(0,.18,.93),47)
    scene.camera=bpy.data.objects['CAM_Pilot']
    area('Ceiling_Softbox',(0,-.15,2.04),(0,.3,.55),26,(1,.84,.63),1.20)
    area('Front_WarmBounce',(0,.75,1.99),(0,.1,.82),12,(1,.59,.28),.8)
    area('Rear_CoolFill',(0,-1.02,1.60),(0,.8,1.22),15,(.66,.81,1),1.15)
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
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'CompactCockpit.blend'))
    print(json.dumps({'saved':str(HERE/'CompactCockpit.blend'),'objects':len(scene.objects),'screens':screen_specs},ensure_ascii=False))

def render(name='Cockpit_Interior',cam='CAM_Pilot',percent=100):
    scene.camera=bpy.data.objects[cam];scene.render.resolution_percentage=percent
    scene.render.filepath=str(HERE/'Previews'/(name+'.png'));bpy.ops.render.render(write_still=True)

if __name__=='__main__':
    setup();shell();console();finish()
