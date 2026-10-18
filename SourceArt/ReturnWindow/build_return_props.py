"""Editable retro mission props. Run with Blender --background --python this_file.

Metres; Blender +Z up, -Y display front; glTFast Unity mapping (-X, Z, -Y).
The exported models contain static artwork only. Unity owns digits, crosses and state.
"""
import bpy, math, os, json, shutil, random
from datetime import date, timedelta
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'SourceArt', 'ReturnWindow')
EXPORT = os.path.join(ROOT, 'Exports', 'ReturnWindow')
UNITY = os.path.join(ROOT, 'Assets', 'ReturnWindow', 'Art', 'Models')
random.seed(19810913)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials): bpy.data.materials.remove(m)
scene = bpy.context.scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.render.engine='CYCLES'; scene.cycles.samples=48
scene.render.resolution_x=1600; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.world.color=(.12,.12,.12)
scene.view_settings.view_transform='AgX'

def mat(name, color, metal=0, rough=.5, emission=0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=(*color,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    p.inputs['Emission Color'].default_value=(*color,1); p.inputs['Emission Strength'].default_value=emission
    return m

ivory=mat('RW_Ivory_Enamel',(.67,.64,.49),.45,.38)
paper=mat('RW_Aged_Paper',(.82,.74,.52),0,.91)
orange=mat('RW_Safety_Orange',(.68,.19,.045),.35,.42)
dark=mat('RW_Black_Rubber',(.018,.023,.022),.05,.66)
metal=mat('RW_Brushed_Metal',(.30,.32,.31),.88,.3)
ink=mat('RW_Printed_Ink',(.055,.073,.067),0,.8)
red=mat('RW_Red_Ink',(.52,.048,.022),0,.7)
glass=mat('RW_Smoked_Glass',(.012,.023,.022),.32,.2)
amber=mat('RW_Amber_LED',(.95,.24,.025),0,.35,2)
amberOff=mat('RW_Amber_Unlit',(.065,.029,.013),0,.7)
groups={}; current=None

def group(name):
    global current
    current=bpy.data.objects.new(name,None); scene.collection.objects.link(current); groups[name]=current
    return current

def register(o, name, material, print_layer=False):
    o.name=name; o.data.materials.append(material); o.parent=current
    if print_layer: o['plan_print']=True
    return o

def box(name,x,h,d,w,height,depth,material,bevel=0,print_layer=False):
    bpy.ops.mesh.primitive_cube_add(size=1,location=(x,-d,h)); o=bpy.context.object
    o.dimensions=(w,depth,height); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    register(o,name,material,print_layer)
    if bevel:
        mod=o.modifiers.new('Manufactured edge radius','BEVEL'); mod.width=bevel; mod.segments=3
        o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return o

def text(name,body,x,h,d,size,material=ink,align='CENTER',print_layer=False):
    c=bpy.data.curves.new(name,'FONT'); c.body=body; c.size=size; c.align_x=align; c.align_y='CENTER'; c.extrude=.00004; c.resolution_u=3
    o=bpy.data.objects.new(name,c); scene.collection.objects.link(o); o.location=(x,-d,h); o.rotation_euler=(math.pi/2,0,0)
    register(o,name,material,print_layer); return o

def cylinder(name,x,h,d,r,depth,material,vertices=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=depth,location=(x,-d,h),rotation=(math.pi/2,0,0))
    return register(bpy.context.object,name,material)

def screw(x,h,d):
    cylinder('Slotted fastening screw',x,h,d,.0032,.0015,metal)
    box('Machined screw slot',x,h,d+.0009,.0037,.00065,.0002,dark)

def line(name,points,material=ink,width=.00045,print_layer=False):
    c=bpy.data.curves.new(name,'CURVE'); c.dimensions='3D'; c.bevel_depth=width; c.bevel_resolution=1
    s=c.splines.new('POLY'); s.points.add(len(points)-1)
    for p,pt in zip(s.points,points): p.co=(pt[0],-pt[2],pt[1],1)
    o=bpy.data.objects.new(name,c); scene.collection.objects.link(o); register(o,name,material,print_layer); return o

calendar=group('ReturnCalendar')
box('Pressed metal backing',0,.18,0,.30,.36,.012,ivory,.004)
box('Orange rear bracket',0,.18,-.007,.275,.32,.007,orange,.004)
box('Rubber rear pad',0,.18,-.011,.255,.29,.002,dark,.002)
for i in range(4): box('Paper stack leaf %02d'%i,0,.177,.007+i*.00045,.274-i*.0007,.327-i*.0005,.00035,paper)
box('Upper clip',0,.342,.010,.074,.023,.006,metal,.002)
box('Clip dark grip',0,.343,.0135,.058,.006,.001,dark,.001)
screw(-.13,.346,.007); screw(.13,.346,.007)
text('Calendar title','MISSION RECORD',0,.310,.0105,.016)
text('Calendar subtitle','EARTH STANDARD / MM/DD',0,.290,.0105,.0065)
line('Header divider',[(-.119,.276,.0106),(.119,.276,.0106)],width=.00065)
for day in range(20):
    col=day%4; row=day//4; x=-.093+col*.062; h=.236-row*.033
    for xx,yy,ww,hh in [(x,h+.0145,.057,.0012),(x,h-.0145,.057,.0012),(x-.0285,h,.0012,.029),(x+.0285,h,.0012,.029)]:
        box('Date grid line',xx,yy,.0106,ww,hh,.0001,ink,print_layer=True)
text('Checklist title','BEFORE DEPARTURE',-.119,.078,.0107,.006,align='LEFT',print_layer=True)
for i,label in enumerate(['RECOVER COMPONENTS','REPAIR / ASSEMBLE','SYSTEM CHECK','CONFIRM DEPARTURE']):
    h=.066-i*.009
    for xx,yy,ww,hh in [(-.113,h+.002,.004,.0004),(-.113,h-.002,.004,.0004),(-.115,h,.0004,.004),(-.111,h,.0004,.004)]:
        box('Checklist checkbox',xx,yy,.0107,ww,hh,.0001,ink,print_layer=True)
    text('Checklist '+label,label,-.104,h,.0107,.0052,align='LEFT',print_layer=True)
text('Calendar footer','MARS / EARTH-STANDARD DATES',0,.017,.0107,.0052)

# Editable reference printing is included in BLEND/GLB; Unity replaces this mesh.
for day in range(20):
    x=-.093+(day%4)*.062; h=.236-(day//4)*.033
    o=text('Calendar date %02d'%(day+1),(date(1981,8,25)+timedelta(days=day)).strftime('%m/%d'),x,h,.011,.011)
    o['date_print']=True
    if day==19:
        pts=[(x+.025*math.cos(i*2*math.pi/48),h+.013*math.sin(i*2*math.pi/48),.0112) for i in range(49)]
        line('Deadline ring - 13 September 1981',pts,red,.0008)['date_print']=True
text('Current civil date','25 AUG 1981',0,.263,.011,.007,red)['date_print']=True

timer=group('ReturnCountdown')
box('Enamel instrument housing',0,.08,.009,.44,.16,.098,ivory,.008)
box('Rubber face gasket',0,.08,.058,.425,.146,.003,dark,.006)
box('Orange instrument bezel',0,.08,.0605,.419,.14,.003,orange,.005)
box('Recessed black display surround',0,.078,.0626,.378,.087,.0015,dark,.003)
box('Smoked display glass',0,.079,.0638,.357,.074,.0008,glass,.002)
text('Countdown title','LATEST DEPARTURE',0,.136,.063,.012,ink)
text('Countdown footer','FLIGHT DYNAMICS / 81-B',-.185,.019,.063,.0049,ink,'LEFT')
text('Countdown serial','SER. 20-913',.185,.019,.063,.0049,ink,'RIGHT')
for x in [-.195,.195]:
    for h in [.02,.14]: screw(x,h,.063)
for x in [-.184,.184]:
    for i in range(6): box('Cooling vent',x,.052+i*.009,.064,.012,.003,.0015,dark,.0006)
for x,label in [(-.105,'DAYS'),(0,'HOURS'),(.105,'MINUTES')]:
    text('Units '+label,label,x,.048,.0647,.0067,ivory)
cylinder('Warning lamp bezel',.171,.136,.063,.005,.002,metal)
cylinder('Warning lamp off lens',.171,.136,.0645,.0038,.0015,amberOff)
text('Caution legend','CAUTION',.144,.136,.063,.0046)
# Feet remain within the defined 44 x 16 cm frontal envelope.
for x in [-.14,.14]: box('Mounting foot',x,.007,-.018,.045,.014,.064,dark,.003)

def convert_and_join():
    for root in groups.values():
        # Keep the printed calendar grid separate from the metal housing.
        for layer in ['body','plan','dates']:
            subset=[o for o in root.children_recursive if o.type in {'MESH','FONT','CURVE'} and ('dates' if o.get('date_print',False) else 'plan' if o.get('plan_print',False) else 'body')==layer]
            if not subset: continue
            bpy.ops.object.select_all(action='DESELECT')
            for o in subset:o.select_set(True)
            bpy.context.view_layer.objects.active=subset[0]; bpy.ops.object.convert(target='MESH'); bpy.ops.object.join()
            joined=bpy.context.object; joined.name='CalendarDatePrint' if layer=='dates' else 'CalendarPlanPrint' if layer=='plan' else root.name+'_StaticGeometry'
            bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)

# Save the editable source before converting curves / bevels for portable GLB export.
scene['units']='metres'; scene['unity_front']='+Z'; scene['dynamic_artwork']='Added by Unity ReturnPropBuilder; never baked into static models.'
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'ReturnWindowProps.blend'))
convert_and_join()
for name,root in groups.items():
    bpy.ops.object.select_all(action='DESELECT'); root.select_set(True)
    for o in root.children_recursive:o.select_set(True)
    dest=os.path.join(EXPORT,name+'.glb')
    bpy.ops.export_scene.gltf(filepath=dest,export_format='GLB',use_selection=True,export_apply=True,export_yup=True,export_extras=True)
    shutil.copy2(dest,os.path.join(UNITY,name+'.glb'))

# Staged product preview. Dynamic display examples exist only in the preview.
calendar.location=(-.24,0,0); timer.location=(.18,0,.075)
current=timer
for x,body in [(-.105,'20'),(0,'00'),(.105,'00')]: text('PREVIEW digits',body,x,.088,.066,.034,amber)

def area(name,location,power,size,color):
    d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.shape='DISK'; d.size=size; d.color=color
    o=bpy.data.objects.new(name,d); scene.collection.objects.link(o); o.location=location; o.rotation_euler=(Vector((0,0,.15))-o.location).to_track_quat('-Z','Y').to_euler()
area('Large warm key',(-1,-1.5,2),120,2,(1,.88,.68))
area('Cool edge',(.7,.5,1),90,1.5,(.64,.77,1))
area('Front fill',(.7,-1,.6),35,1,(1,.95,.88))
floor=mat('Preview_Background',(.025,.038,.041),.1,.65)
current=None
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.017)); bpy.context.object.data.materials.append(floor)
cam=bpy.data.cameras.new('Review camera'); co=bpy.data.objects.new('Review camera',cam); scene.collection.objects.link(co)
co.location=(.45,-1.7,.75); target=Vector((0,0,.18)); co.rotation_euler=(target-co.location).to_track_quat('-Z','Y').to_euler()
cam.type='ORTHO'; cam.ortho_scale=.98; scene.camera=co
scene.render.filepath=os.path.join(OUT,'Previews','ReturnWindowProps.png'); bpy.ops.render.render(write_still=True)
# Frontal review catches mirrored text and print density.
co.location=(0,-2,.18); co.rotation_euler=(target-co.location).to_track_quat('-Z','Y').to_euler(); cam.ortho_scale=.94
scene.render.filepath=os.path.join(OUT,'Previews','ReturnWindowProps_Front.png'); bpy.ops.render.render(write_still=True)
report={'units':'metres','unity_front':'+Z','unity_up':'+Y','root':'bottom centre','calendar_front':[.30,.36], 'countdown_front':[.44,.16], 'source':'ReturnWindowProps.blend','exported':[n+'.glb' for n in groups], 'calendar_dates':'1981-08-25 through 1981-09-13; MM/DD; editable reference print replaced in Unity', 'dynamic_layers':'Unity-generated seven-segment digits, ink crosses, day marker, sowing epilogue, status and warning light'}
with open(os.path.join(OUT,'model_manifest.json'),'w',encoding='utf8') as f:json.dump(report,f,indent=2)
print('RETURN_WINDOW_PROPS_COMPLETE')
