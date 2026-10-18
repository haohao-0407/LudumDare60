"""Precision authored interactive cockpit equipment. No generated geometry.

Final world metres, identity root; architecture's 0.8 scale is NOT inherited.
build() creates the reviewable source. export_hero() is called after render QA.
"""
import bpy, math, os, json, importlib.util
from mathutils import Vector, Matrix

ROOT=r'D:\unity-projects\LudumDare60'
SOURCE=os.path.join(ROOT,'SourceArt','MarsCabin')
EXPORT=os.path.join(ROOT,'Assets','MarsCabin','Models')
TAU=math.pi*2


def build():
    spec=importlib.util.spec_from_file_location('hero_geometry',os.path.join(SOURCE,'build_cabin.py'))
    b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
    room=bpy.data.scenes.get('MarsCabin_Interior') or bpy.context.scene;bpy.context.window.scene=room;b.scene=room
    col=bpy.data.collections.new('HeroConsole_Authored');room.collection.children.link(col)
    root=bpy.data.objects.new('Cabin_HeroConsole',None);col.objects.link(root)
    root['construction']='Precision authored Blender meshes, no Meshy geometry';root['units']='final world metres; scale1'
    b.groups={}
    for name in ['Hero_Main','Hero_Throttle','Hero_Joystick','Hero_Metadata']:
        o=bpy.data.objects.new(name,None);col.objects.link(o);o.parent=root;b.groups[name]=o
    for name in ['MC_Ivory','MC_Orange','MC_Metal','MC_Dark','MC_Rubber','MC_Screen','MC_GreenGlow','MC_WarmGlow','MC_RedGlow','MC_White','MC_Brass']:
        b.materials[name]=bpy.data.materials[name]
    controls=[];screens=[];textowners=[];bevels={}
    I=Matrix.Identity(3)
    main=Vector((0,-1.94,0))
    # U points to the pilot's right, V rises away, N faces the pilot and upward.
    screenR=Matrix(((-1,0,0),(0,-.60,.80),(0,.80,.60))).transposed()
    panelR=Matrix(((-1,0,0),(0,-.988,.154),(0,.154,.988))).transposed()

    def tinybolt(g,p,n=(0,0,1),r=.004):
        p=Vector(p);n=Vector(n).normalized()
        b.cylinder(g,'MC_Metal',p-n*.001,p+n*.0014,r,8)
        tangent=n.cross(Vector((.31,.79,.45))).normalized()*r*.61
        b.cylinder(g,'MC_Dark',p+n*.0016-tangent,p+n*.0016+tangent,.0006,6)

    def text(g,t,p,size=.025,R=I,mat='MC_Dark',owner=None):
        b.label(g,t,p,size,R,mat);textowners.append(owner)

    def part(name,g,p=(0,0,0),bevel=0):
        b.begin(name,g,p)
        if bevel:bevels[name]=bevel

    def register(name,kind,p,axis=(0,0,1),**extra):
        e={'name':name,'kind':kind,'pivot':list(p),'worldAxis':list(axis)};e.update(extra);controls.append(e)

    def button(g,name,p,R=I,size=(.042,.038),color='MC_Ivory',caption=''):
        p=Vector(p);n=R@Vector((0,0,1));w,h=size
        b.rounded(g,'MC_Dark',p,(w+.011,h+.011,.015),.007,R)
        part(name,g,p)
        b.rounded(g,color,p+n*.010,(w,h,.015),.006,R)
        b.end();register(name,'Button',p,-n,travel=.004)
        if caption:text(g,caption,p+n*.0185,min(w,h)*.36,R,'MC_Dark',name)

    def knob(g,name,p,R=I,r=.023,caption=''):
        p=Vector(p);n=R@Vector((0,0,1))
        b.cylinder(g,'MC_Metal',p-n*.003,p+n*.004,r*1.32,40)
        # Readable radial tick scale engraved around a physically separate knob.
        for j in range(21):
            a=math.radians(-135+j*270/20)
            u=R@Vector((math.sin(a),math.cos(a),0));v1=p+u*r*1.48+n*.005;v2=p+u*r*(1.72 if j%5==0 else 1.61)+n*.005
            b.cylinder(g,'MC_Dark',v1,v2,.0008,5)
        part(name,g,p)
        b.lathe(g,'MC_Dark',p,[(0,r*.95),(.007,r),(.029,r),(.035,r*.82)],40,n)
        for j in range(32):
            a=TAU*j/32;v=R@Vector((r*math.cos(a),r*math.sin(a),0))
            b.cylinder(g,'MC_Metal',p+v+n*.009,p+v+n*.025,.0011,6)
        b.rounded(g,'MC_White',p+R@Vector((0,r*.43,.0358)),(.0035,r*.75,.0015),.0006,R)
        b.end();register(name,'Knob',p,n,rangeDegrees=270,minimumAngle=-135,maximumAngle=135)
        if caption:text(g,caption,p+R@Vector((0,-r*2.03,.003)),.013,R)

    def toggle(g,name,p,R=I,caption='',guard=False):
        p=Vector(p);n=R@Vector((0,0,1));axis=R@Vector((1,0,0))
        b.rounded(g,'MC_Metal',p,(.034,.058,.009),.005,R)
        b.torus(g,'MC_Dark',p+n*.006,.010,.002,n,24,6)
        part(name,g,p+n*.008)
        b.cylinder(g,'MC_Metal',p+n*.008,p+R@Vector((0,-.008,.041)),.004,16)
        b.rounded(g,'MC_Ivory',p+R@Vector((0,-.009,.044)),(.011,.015,.016),.004,R)
        b.end();register(name,'Toggle',p+n*.008,axis,rangeDegrees=42,minimumAngle=-21,maximumAngle=21)
        for yy in [-.022,.022]:tinybolt(g,p+R@Vector((0,yy,.005)),n,.0026)
        if guard:
            for xx in [-.021,.021]:
                a=p+R@Vector((xx,-.023,.006));q=p+R@Vector((xx,-.023,.052));z=p+R@Vector((xx,.025,.052))
                b.cylinder(g,'MC_Orange',a,q,.003,10);b.cylinder(g,'MC_Orange',q,z,.003,10)
            b.cylinder(g,'MC_Orange',p+R@Vector((-.021,.025,.052)),p+R@Vector((.021,.025,.052)),.003,10)
        if caption:text(g,caption,p+R@Vector((0,.042,.002)),.011,R)

    def crt(g,name,p,w,h,R,texture):
        p=Vector(p);n=R@Vector((0,0,1))
        # Independent moulded case, stepped ceramic bezel, orange rim, gasket.
        for suffix,mat,offset,sz,rad in [('Case','MC_Dark',-.032,(w+.105,h+.105,.105),.032),('Bezel','MC_Ivory',.018,(w+.092,h+.092,.038),.027),('OrangeTrim','MC_Orange',.040,(w+.044,h+.044,.018),.019),('Gasket','MC_Rubber',.052,(w+.013,h+.013,.012),.016)]:
            part(name+'_'+suffix,g,p)
            b.rounded(g,mat,p+n*offset,sz,rad,R);b.end()
        # Curved active glass: gentle 5mm crown, continuous UV0 exactly 0..1.
        pp=p+n*.060;part('SCREEN_'+name,g,pp)
        cols,rows=32,16;vs=[];uv=[]
        for j in range(rows+1):
            for i in range(cols+1):
                u=i/cols;v=j/rows;x=(u-.5)*w;y=(v-.5)*h
                crown=.005*(1-(2*u-1)**2)*(1-(2*v-1)**2)
                vs.append(pp+R@Vector((x,y,crown)));uv.append((u,v))
        fs=[(j*(cols+1)+i,j*(cols+1)+i+1,(j+1)*(cols+1)+i+1,(j+1)*(cols+1)+i) for j in range(rows) for i in range(cols)]
        b.mesh(g,'MC_Screen',vs,fs,True);b.batches[b.override].uv=uv;b.end()
        screens.append({'name':'SCREEN_'+name,'center':list(pp),'worldNormal':list(n),'size':[w,h],'texture':texture,'curvatureMetres':.005})
        for x in [-w/2-.032,w/2+.032]:
            for y in [-h/2-.032,h/2+.032]:tinybolt(g,p+R@Vector((x,y,.040)),n,.0043)
        text(g,{'HERO_Navigation':'ARES  |  NAVIGATION','HERO_Comms':'COMMUNICATIONS','HERO_Systems':'SYSTEM STATUS'}[name],p+R@Vector((0,h/2+.029,.040)),.013 if w<.5 else .019,R)
        # Side-adjustment pot and tiny indicator are real separate controls.
        knob(g,'INT_KNOB_'+name+'_Brightness',p+R@Vector((w/2+.033,-h/2+.015,.042)),R,.009)

    # MAIN CONSOLE: a chamfered structural case and two real floor pedestals.
    g='Hero_Main';part('Hero_Main_Chassis',g,main,bevel=.012)
    cross=[(-.36,.54),(.35,.54),(.39,.60),(.39,.84),(.31,.89),(-.27,1.035),(-.36,1.015)]
    vs=[(main.x+x,main.y+y,z) for x in [-.89,.89] for y,z in cross];n=len(cross)
    b.mesh(g,'MC_Ivory',vs,[tuple(reversed(range(n))),tuple(n+i for i in range(n))]+[(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)])
    b.end()
    for side in [-1,1]:
        x=side*.55
        part('Hero_Main_Pedestal_'+str(side),g,(x,main.y,0),bevel=.009)
        b.box(g,'MC_Dark',(x,main.y,.065),(.39,.58,.13))
        b.box(g,'MC_Metal',(x,main.y,.32),(.20,.36,.44))
        b.box(g,'MC_Ivory',(x,main.y,.47),(.34,.44,.17));b.end()
        b.rounded(g,'MC_Orange',(x,main.y,.545),(.38,.49,.035),.014)
        for xx in [-.14,.14]:
            for yy in [-.22,.22]:tinybolt(g,(x+xx,main.y+yy,.133),r=.006)
        for z in [.20,.28,.36]:
            b.rounded(g,'MC_Dark',(x,main.y+.185,z),(.12,.012,.027),.004)
    b.rounded(g,'MC_Metal',(0,main.y,.45),(.85,.28,.12),.025)
    # Front service fascia is subdivided into two screw-fastened panels.
    frontR=Matrix(((-1,0,0),(0,0,1),(0,1,0))).transposed()
    for side in [-1,1]:
        c=Vector((side*.45,main.y+.393,.70))
        b.rounded(g,'MC_Dark',c,(.77,.205,.012),.016,frontR)
        b.rounded(g,'MC_Ivory',c+Vector((0,.008,0)),(.75,.19,.010),.014,frontR)
        for j in range(12):b.rounded(g,'MC_Dark',c+Vector(((j-5.5)*.046,.016,-.012)),(.018,.105,.009),.005,frontR)
        for xx in [-.34,.34]:
            for zz in [-.068,.068]:tinybolt(g,c+Vector((xx,.021,zz)),(0,1,0),.004)
    b.rounded(g,'MC_Orange',(0,main.y+.402,.835),(1.72,.018,.037),.012)
    text(g,'ARES  /  FLIGHT CONTROL  /  FCU-01',(0,main.y+.415,.815),.022,frontR,'MC_Dark')
    # Separate upper deck panels with metal perimeter lines and screws.
    for side in [-1,1]:
        c=Vector((side*.55,main.y+.205,.902))
        b.rounded(g,'MC_Metal',c,(.44,.255,.018),.020,panelR)
        b.rounded(g,'MC_Ivory',c+panelR@Vector((0,0,.012)),(.425,.240,.012),.016,panelR)
        for xx in [-.192,.192]:
            for yy in [-.10,.10]:tinybolt(g,c+panelR@Vector((xx,yy,.021)),panelR@Vector((0,0,1)),.0035)
    # Three matched CRTs: centre 2:1, flanking square instruments.
    crt(g,'HERO_Navigation',(0,main.y-.16,1.055),.74,.37,screenR,'HeroCRT_Navigation.png')
    for side,name,tex in [(-1,'HERO_Comms','HeroCRT_Comms.png'),(1,'HERO_Systems','HeroCRT_Systems.png')]:
        R=Matrix.Rotation(math.radians(side*11),3,'Z')@screenR
        crt(g,name,(side*.62,main.y-.12,1.045),.31,.31,R,tex)
    # True sculpted keycaps with independent press pivots and embossed legends.
    for row in range(3):
        for c in range(4):
            p=Vector((-.55+(c-1.5)*.080,main.y+.205+(row-1)*.064,.919))
            button(g,'INT_BUTTON_Main_Key_%02d'%(row*4+c),p,panelR,(.057,.046),'MC_Ivory',['1','2','3','CLR','4','5','6','ENT','7','8','9','0'][row*4+c])
    for row in range(2):
        for c in range(4):
            p=Vector((.55+(c-1.5)*.080,main.y+.220+(row-.5)*.080,.919))
            button(g,'INT_BUTTON_Main_Mode_%02d'%(row*4+c),p,panelR,(.057,.053),'MC_WarmGlow' if row==1 else 'MC_Ivory',['NAV','MAP','COM','SYS','ARM','HOLD','AUTO','EXEC'][row*4+c])
    # Centre command strip and tactile selectors are spatially distinct.
    c=Vector((0,main.y+.285,.925))
    b.rounded(g,'MC_Dark',c,(.54,.110,.017),.015,panelR)
    for i in range(6):button(g,'INT_BUTTON_Main_Command_%02d'%i,c+panelR@Vector(((i-2.5)*.080,0,.012)),panelR,(.056,.052),'MC_WarmGlow' if i<4 else 'MC_Orange',['PWR','RCS','DOCK','SCAN','ACK','RST'][i])
    for side in [-1,1]:knob(g,'INT_KNOB_Main_Trim_'+str(side),(side*.17,main.y+.115,.955),panelR,.024,'TRIM' if side<0 else 'RANGE')
    for side in [-1,1]:
        # Protective rails are actual bent tubes with collars and bolted feet.
        x=side*.876
        for y in [main.y-.20,main.y+.28]:
            b.cylinder(g,'MC_Metal',(x,y,.91),(x,y,1.02),.010,16)
            b.rounded(g,'MC_Metal',(x,y,.91),(.045,.055,.015),.009)
            for yy in [-.017,.017]:tinybolt(g,(x,y+yy,.920),r=.003)
        b.cylinder(g,'MC_Metal',(x,main.y-.20,1.02),(x,main.y+.28,1.02),.010,16)
    # Rear vent slots and side access fasteners make every visible view useful.
    for side in [-1,1]:
        sideR=Matrix.Rotation(math.pi/2,3,'Y')
        for j in range(9):b.rounded(g,'MC_Dark',(side*.895,main.y-.02+(j-4)*.052,.73),(.15,.020,.012),.004,sideR)
        for y in [-.25,.25]:
            for z in [.61,.88]:tinybolt(g,(side*.904,main.y+y,z),(side,0,0),.004)

    # Both side pods have authored structural supports all the way to the floor.
    def support(g,c):
        c=Vector(c)
        for name,mat,z,sz,bev in [('Foot','MC_Dark',.055,(.38,.46,.11),.020),('FootTrim','MC_Metal',.117,(.35,.43,.016),.009),('Column','MC_Metal',.46,(.10,.14,.68),.012),('ColumnCover','MC_Ivory',.47,(.15,.12,.48),.014),('Mount','MC_Metal',.849,(.34,.38,.055),.014)]:
            part(g+'_'+name,g,c,bevel=.003 if name=='Column' else 0)
            b.rounded(g,mat,c+Vector((0,0,z)),sz,bev);b.end()
        for xx in [-.13,.13]:
            for yy in [-.16,.16]:tinybolt(g,c+Vector((xx,yy,.131)),r=.005)
        for z in [.31,.59]:b.torus(g,'MC_Orange',c+Vector((0,0,z)),.083,.006,n=40,sides=8)
        # Flexible service conduit secured beneath the arm-height body.
        for j in range(18):b.torus(g,'MC_Rubber',c+Vector((.07,.08,.20+j*.029)),.025,.005,n=20,sides=6)
    throttle=Vector((-.8,-.45,0));joystick=Vector((.8,-.45,0))
    support('Hero_Throttle',throttle);support('Hero_Joystick',joystick)
    g='Hero_Throttle'
    part('Hero_Throttle_LowerCase',g,throttle,bevel=.006)
    b.rounded(g,'MC_Ivory',throttle+Vector((0,0,.93)),(.49,.37,.15),.027);b.end()
    b.rounded(g,'MC_Orange',throttle+Vector((0,.184,.93)),(.43,.012,.075),.010)
    text(g,'THRUST / TRIPLE CHANNEL',throttle+Vector((0,.193,.935)),.016,frontR)
    # Each slot has its own raised arc cover, exposed metal rails and readable scale.
    for channel in range(3):
        x=(channel-1)*.134
        b.rounded(g,'MC_Dark',throttle+Vector((x,0,1.010)),(.055,.305,.025),.013)
        for side in [-1,1]:
            pts=[]
            for j in range(29):
                y=-.15+j*.30/28;z=1.025+.025*(1-(y/.15)**2);pts.append(throttle+Vector((x+side*.032,y,z)))
            for a,q in zip(pts,pts[1:]):b.cylinder(g,'MC_Metal',a,q,.005,12)
            # Ivory strip next to the slot carries the individual scale graduations.
            vs=[]
            for j in range(29):
                y=-.15+j*.30/28;z=1.023+.025*(1-(y/.15)**2)
                for xx in [x+side*.040,x+side*.060]:vs.append(throttle+Vector((xx,y,z)))
            b.mesh(g,'MC_Ivory',vs,[(j*2,j*2+1,j*2+3,j*2+2) if side>0 else (j*2+2,j*2+3,j*2+1,j*2) for j in range(28)])
            for j in range(13):
                y=-.13+j*.26/12;z=1.026+.025*(1-(y/.15)**2)
                b.cylinder(g,'MC_Dark',throttle+Vector((x+side*.041,y,z)),throttle+Vector((x+side*(.057 if j%3==0 else .050),y,z)),.0007,5)
        for y in [-.155,.155]:
            b.rounded(g,'MC_Metal',throttle+Vector((x,y,1.027)),(.076,.027,.014),.006)
            for xx in [-.025,.025]:tinybolt(g,throttle+Vector((x+xx,y,1.037)),r=.0035)
        text(g,['A','B','C'][channel],throttle+Vector((x,.162,1.041)),.016)
    for side in [-1,1]:
        for j in range(3):
            pp=throttle+Vector((side*.222,-.11+j*.10,1.012))
            button(g,'INT_BUTTON_Throttle_%s_%d'%('L' if side<0 else 'R',j),pp,I,(.029,.037),'MC_WarmGlow','')
    # Side grip rails, jack socket and ventilated front service face.
    for side in [-1,1]:
        for y in [-.14,.14]:b.cylinder(g,'MC_Metal',throttle+Vector((side*.265,y,.97)),throttle+Vector((side*.265,y,1.025)),.007,12)
        b.cylinder(g,'MC_Metal',throttle+Vector((side*.265,-.14,1.025)),throttle+Vector((side*.265,.14,1.025)),.007,12)
    for j in range(11):b.rounded(g,'MC_Dark',throttle+Vector(((j-5)*.026,.192,.905)),(.012,.048,.009),.004,frontR)
    # Joystick pod: machined bolted plate, flexible four-ridge gaiter and lock collar.
    g='Hero_Joystick';part('Hero_Joystick_LowerCase',g,joystick,bevel=.005)
    b.rounded(g,'MC_Ivory',joystick+Vector((0,0,.923)),(.36,.34,.135),.025);b.end()
    b.rounded(g,'MC_Orange',joystick+Vector((0,.168,.925)),(.30,.012,.073),.010)
    text(g,'ATTITUDE / MANUAL',joystick+Vector((0,.178,.928)),.016,frontR)
    b.rounded(g,'MC_Metal',joystick+Vector((0,-.025,.996)),(.19,.19,.018),.018)
    for x in [-.073,.073]:
        for y in [-.073,.073]:tinybolt(g,joystick+Vector((x,y-.025,1.007)),r=.006)
    for z,r in [(1.005,.074),(1.018,.065),(1.031,.055),(1.044,.044)]:
        b.torus(g,'MC_Rubber',joystick+Vector((0,-.025,z)),r,.009,n=64,sides=12)
    b.lathe(g,'MC_Metal',joystick+Vector((0,-.025,1.047)),[(0,.031),(.012,.031),(.015,.025)],48)
    for i in range(16):
        a=TAU*i/16;b.cylinder(g,'MC_Dark',joystick+Vector((.031*math.cos(a),-.025+.031*math.sin(a),1.050)),joystick+Vector((.031*math.cos(a),-.025+.031*math.sin(a),1.058)),.0012,6)
    for i in range(3):button(g,'INT_BUTTON_Joystick_Status_%d'%i,joystick+Vector((-.132,-.105+i*.095,1.001)),I,(.036,.048),'MC_WarmGlow')
    toggle(g,'INT_TOGGLE_Joystick_Safe',joystick+Vector((.124,.065,1.001)),I,'ARM',True)
    knob(g,'INT_KNOB_Joystick_Gain',joystick+Vector((.120,-.075,1.001)),I,.018,'GAIN')
    for side in [-1,1]:
        for y in [-.13,.13]:b.cylinder(g,'MC_Metal',joystick+Vector((side*.20,y,.98)),joystick+Vector((side*.20,y,1.027)),.006,12)
        b.cylinder(g,'MC_Metal',joystick+Vector((side*.20,-.13,1.027)),joystick+Vector((side*.20,.13,1.027)),.006,12)
    for i in range(8):b.rounded(g,'MC_Dark',joystick+Vector(((i-3.5)*.026,.178,.900)),(.013,.035,.008),.004,frontR)
    # Bake mesh/text objects, then attach moving legends to their real controls.
    text_count=len(textowners);b.flush()
    if text_count:
        for obj,owner in zip(b.objects[-text_count:],textowners):
            if owner:
                target=next(o for o in b.objects if o.name==owner);world=obj.matrix_world.copy();obj.parent=target;obj.matrix_world=world
    # Real edge bevels on structural castings complement the rounded panel corners.
    for obj in b.objects:
        if obj.name in bevels:
            allowed=[i.identifier for i in bpy.types.Modifier.bl_rna.properties['type'].enum_items]
            if 'BEVEL' not in allowed:raise RuntimeError(allowed)
            mod=obj.modifiers.new('Manufactured edge radius','BEVEL');mod.width=bevels[obj.name];mod.segments=3
            mod.limit_method=b.enum(mod,'limit_method','ANGLE')
            dep=bpy.context.evaluated_depsgraph_get();new=bpy.data.meshes.new_from_object(obj.evaluated_get(dep),depsgraph=dep)
            obj.modifiers.clear();obj.data=new
    # Reuse the already authored precision moving parts, never generated handles.
    masters=bpy.app.driver_namespace.get('mars_control_kit')
    if not masters:raise RuntimeError('Build authored control kit first')
    sourceparts={o.name:o for o in masters['parts']};cloned={}
    for i,source_name in enumerate(['ThrottleLever_A','ThrottleLever_B','ThrottleLever_C']):
        src=sourceparts[source_name];obj=src.copy();obj.data=src.data.copy();obj.name='INT_LEVER_Throttle_'+chr(65+i);col.objects.link(obj);obj.parent=b.groups['Hero_Throttle'];obj.location=throttle+Vector(((i-1)*.134,0,1.0));obj.rotation_euler=(math.radians([-12,0,12][i]),0,0);b.objects.append(obj)
        register(obj.name,'Lever',obj.location,(1,0,0),rangeDegrees=64,minimumAngle=-32,maximumAngle=32)
    src=sourceparts['JoystickGrip'];stick=src.copy();stick.data=src.data.copy();stick.name='INT_JOYSTICK_Attitude';col.objects.link(stick);stick.parent=b.groups['Hero_Joystick'];stick.location=joystick+Vector((0,-.025,1.052));b.objects.append(stick)
    register(stick.name,'Joystick',stick.location,(1,0,0),secondaryWorldAxis=[0,1,0],rangeDegrees=24,minimumAngle=-12,maximumAngle=12)
    for source_name in ['INT_Joystick_Trigger','INT_Joystick_TopButton']:
        src=sourceparts[source_name];obj=src.copy();obj.data=src.data.copy();obj.name='INT_BUTTON_Hero_Trigger' if 'Trigger' in source_name else 'INT_BUTTON_Hero_Top';col.objects.link(obj);obj.parent=stick;obj.location=src.location;obj.rotation_euler=src.rotation_euler;b.objects.append(obj)
        if 'Trigger' in source_name:register(obj.name,'Button',stick.location+src.location,(1,0,0),minimumAngle=0,maximumAngle=-16,rangeDegrees=16,buttonUsesRotation=True)
        else:register(obj.name,'Button',stick.location+src.location,(0,0,-1),travel=.004)
    # Relink helper outputs into the isolated hero collection, retaining hierarchy.
    for obj in b.objects:
        for oldcol in list(obj.users_collection):
            if oldcol!=col:oldcol.objects.unlink(obj)
        if obj.name not in col.objects:col.objects.link(obj)
    for name,p in [('ORIENTATION_HERO_X_POS',(1,0,0)),('ORIENTATION_HERO_Y_POS',(0,1,0)),('ORIENTATION_HERO_Z_POS',(0,0,1))]:
        o=bpy.data.objects.new(name,None);col.objects.link(o);o.parent=b.groups['Hero_Metadata'];o.location=p
    result={'builder':b,'root':root,'collection':col,'controls':controls,'screens':screens,'room':room}
    bpy.app.driver_namespace['mars_hero_console']=result
    update_textures()
    write_manifest()
    print('HERO_CONSOLE_REVIEW_READY',len(b.objects),'meshes',len(controls),'controls',len(screens),'CRTs')
    return result


def update_textures():
    hero=bpy.app.driver_namespace['mars_hero_console']
    for s in hero['screens']:
        name=s['name'];obj=next(o for o in hero['builder'].objects if o.name==name)
        material=bpy.data.materials.get(name) or bpy.data.materials.new(name);material.use_nodes=True
        p=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        p.inputs['Base Color'].default_value=(.002,.012,.004,1);p.inputs['Roughness'].default_value=.26;p.inputs['Metallic'].default_value=0
        if 'Specular IOR Level' in p.inputs:p.inputs['Specular IOR Level'].default_value=.08
        p.inputs['Emission Color'].default_value=(.015,.3,.055,1);p.inputs['Emission Strength'].default_value=1.0
        path=os.path.join(ROOT,'Assets','MarsCabin','Textures',s['texture'])
        if os.path.exists(path):
            image=bpy.data.images.load(path,check_existing=True);image.pack()
            node=next((n for n in material.node_tree.nodes if n.type=='TEX_IMAGE'),None) or material.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
            for link in list(p.inputs['Base Color'].links):material.node_tree.links.remove(link)
            p.inputs['Base Color'].default_value=(.001,.002,.001,1)
            material.node_tree.links.new(node.outputs['Color'],p.inputs['Emission Color'])
            # CRT phosphor remains black between pixels even under strong cabin
            # illumination; the gently curved active surface is a real mesh.
            emit=next((n for n in material.node_tree.nodes if n.type=='EMISSION'),None) or material.node_tree.nodes.new('ShaderNodeEmission')
            emit.inputs['Strength'].default_value=1.15
            material.node_tree.links.new(node.outputs['Color'],emit.inputs['Color'])
            out=next(n for n in material.node_tree.nodes if n.type=='OUTPUT_MATERIAL')
            material.node_tree.links.new(emit.outputs[0],out.inputs['Surface'])
        obj.data.materials.clear();obj.data.materials.append(material)


def write_manifest():
    hero=bpy.app.driver_namespace['mars_hero_console'];b=hero['builder'];bpy.context.view_layer.update();tris=0
    for e in hero['controls']:
        obj=next(o for o in b.objects if o.name==e['name']);e['pivot']=list(obj.matrix_world.translation)
    for e in hero['screens']:
        obj=next(o for o in b.objects if o.name==e['name']);e['center']=list(obj.matrix_world.translation)
    for o in b.objects:o.data.calc_loop_triangles();tris+=len(o.data.loop_triangles)
    pts=[o.matrix_world@Vector(v) for o in b.objects for v in o.bound_box]
    report={'coordinateSpace':'Blender final world metres, Z up, identity root, no architecture0.8 scale','authorship':'All key equipment and moving controls are Blender geometry. No Meshy geometry.','root':'Cabin_HeroConsole','scale':1,'meshObjects':len(b.objects),'triangles':tris,'boundsMin':[min(p[k] for p in pts) for k in range(3)],'boundsMax':[max(p[k] for p in pts) for k in range(3)],'controls':hero['controls'],'screens':hero['screens'],'mainConsoleCenter':[0,-1.94,0],'podCenters':[[-.8,-.45,0],[.8,-.45,0]],'supports':'Integrated authored feet and columns to floor0; remove old Unity mounts','fbxAxes':{'forward':'-Z','up':'Y'}}
    with open(os.path.join(SOURCE,'hero_interaction_manifest.json'),'w',encoding='utf-8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
    return report


def export_hero():
    hero=bpy.app.driver_namespace['mars_hero_console'];b=hero['builder'];bpy.context.window.scene=hero['room'];update_textures();report=write_manifest()
    for o in hero['room'].objects:o.select_set(False)
    for o in [hero['root']]+list(b.groups.values())+b.objects+list(b.groups['Hero_Metadata'].children):o.select_set(True)
    bpy.context.view_layer.objects.active=hero['root'];op=bpy.ops.export_scene.fbx
    op(filepath=os.path.join(EXPORT,'Cabin_HeroConsole.fbx'),use_selection=True,global_scale=1,apply_unit_scale=True,bake_space_transform=False,axis_forward=b.op_enum(op,'axis_forward','-Z'),axis_up=b.op_enum(op,'axis_up','Y'),object_types={b.op_enum(op,'object_types','MESH'),b.op_enum(op,'object_types','EMPTY')},add_leaf_bones=False,bake_anim=False,path_mode=b.op_enum(op,'path_mode','AUTO'),mesh_smooth_type=b.op_enum(op,'mesh_smooth_type','FACE'))
    for o in hero['room'].objects:o.select_set(False)
    previous_versions=bpy.context.preferences.filepaths.save_version
    try:
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'MarsCabin_Interior.blend'))
    finally:bpy.context.preferences.filepaths.save_version=previous_versions
    print('HERO_CONSOLE_EXPORTED',json.dumps({k:v for k,v in report.items() if k not in ['controls','screens']}))


if __name__=='__main__':build()
