"""Five original instrument modules arranged around one pilot. +Y forward."""
import math
from mathutils import Vector,Matrix

EYE=Vector((0,-.48,1.44))

def aim(p):
    n=(EYE-Vector(p)).normalized();u=Vector((0,0,1)).cross(n).normalized();v=n.cross(u).normalized()
    return Matrix((u,v,n)).transposed()

def build(cc):
    b=cc.b;g='07_ConsoleBody';F=cc.FRONT
    # Each main instrument pod is rigid and looks directly at the pilot eye point.
    for side,name,tex in [(-1,'RADAR','radar.png'),(1,'ATTITUDE','attitude.png')]:
        p=Vector((side*.40,.77,1.625));R=aim(p);n=R.col[2]
        cc.panel('UpperPod_'+name,g,'Ivory',p-n*.047,(.742,.635,.105),.05,R)
        # Real wall brackets keep the suspended upper pods physically attached.
        for xx in [-.23,.23]:
            back=p+R@Vector((xx,0,-.10));wall=Vector((back.x,math.sqrt(1.105**2-back.x**2),back.z))
            b.cylinder(g,'Dark',back,wall,.026,20)
        cc.screen(name,p,.585,.465,R,tex)
        # Handle follows the outer edge of its own pod, rather than the room wall.
        xx=side*.354
        points=[p+R@Vector((xx,v,z)) for v,z in [(-.249,.02),(-.224,.067),(.224,.067),(.249,.02)]]
        cc.pipe(name+'_GrabRail','10_Fittings',points,.010)
        for vv in [-.249,.249]:
            b.lathe('10_Fittings','Metal',p+R@Vector((xx,vv,.018)),[(-.003,.017),(.007,.017)],24,n)
        q=Vector((side*.83,.52,1.60));Q=aim(q)
        cc.panel('OuterControlStrip_'+name,g,'Ivory',q,(.079,.53,.045),.014,Q)
        for k in range(3):cc.button('BANK_STATUS_%s_%s'%(side,k),q+Q@Vector((0,.025+k*.073,.027)),Q,w=.032,h=.036)
        cc.knob('BANK_SELECTOR_'+name,q+Q@Vector((0,-.153,.025)),Q,r=.022)
    # Narrow central spine follows the front arc.
    cc.panel('MainPowerSpine',g,'Ivory',(0,.934,1.60),(.108,.67,.07),.017,F)
    cc.panel(None,g,'Dark',(0,.892,1.625),(.043,.40,.013),.005,F)
    for k in range(15):cc.panel(None,'09_Controls','Amber' if k<10 else 'Rubber',(0,.881,1.444+k*.025),(.024,.019,.008),.002,F)
    cc.button('MASTER_CAUTION',(0,.88,1.88),F,w=.057,h=.028)
    cc.label(g,'PWR',(0,.885,1.368),.012,F)
    for i in range(5):cc.button('ANNUNCIATOR_%02d'%i,((i-2)*.049,.865,1.318),F,w=.036,h=.027,mat='Amber' if i<2 else 'White')
    # Continuous curved desk: circular outer boundary, shallow concave pilot edge.
    N=80;cy=-.43;inner=.48;ang=math.radians(58)
    def radius(t):return -cy*math.cos(t)+math.sqrt(1.035**2-cy*cy*math.sin(t)**2)
    vs=[]
    for layer in range(4):
        for i in range(N+1):
            t=-ang+2*ang*i/N;r=inner if layer in (0,2) else radius(t)
            z=.65 if layer<2 else .794+.105*(r-inner)
            vs.append((r*math.sin(t),cy+r*math.cos(t),z))
    n=N+1;faces=[]
    for i in range(N):
        faces.extend([(i+1,i,n+i,n+i+1),(2*n+i,2*n+i+1,3*n+i+1,3*n+i),
                      (i,i+1,2*n+i+1,2*n+i),(n+i+1,n+i,3*n+i,3*n+i+1)])
    faces.extend([(0,2*n,3*n,n),(N,n+N,3*n+N,2*n+N)])
    b.begin('Curved_FlightDesk',g);b.mesh(g,'Ivory',vs,faces);b.end()
    # Front desk edge and handrail describe a wrap around the single user.
    pts=[]
    for i in range(21):
        t=-ang+2*ang*i/20;pts.append((inner*math.sin(t),cy+inner*math.cos(t),.729))
    cc.pipe('Desk_OrangeEdge',g,pts,.012,'Orange')
    pts=[]
    for i in range(21):
        t=-ang+2*ang*i/20;r=.451;pts.append((r*math.sin(t),cy+r*math.cos(t),.831))
    cc.pipe('Desk_GrabRail','10_Fittings',pts,.013)
    for t in [-ang,ang]:
        p=(.451*math.sin(t),cy+.451*math.cos(t),.831)
        b.cylinder('10_Fittings','Metal',Vector(p)+Vector((0,0,-.07)),p,.014,20)
    for side in [-1,1]:
        p=Vector((side*.72,.48,.335));R=Matrix.Rotation(-side*.43,3,'Z')
        cc.panel('DeskPedestal_'+str(side),g,'Dark',p,(.19,.37,.67),.022,R)
        p2=Vector((side*.72,.281,.34));R2=Matrix.Rotation(-side*.25,3,'Z')@F
        cc.panel(None,g,'Ivory',p2,(.19,.55,.018),.014,R2)
        for z in [-.17,-.09,-.01,.07,.15]:cc.panel(None,g,'Dark',p2+R2@Vector((0,z,.012)),(.12,.014,.004),.003,R2)
    # Central chart has a steeper 50-degree deck, while side screens target EYE exactly.
    D=Matrix.Rotation(math.radians(50),3,'X');nav=Vector((0,.27,1.00))
    cc.panel('Navigation_TiltCradle',g,'Dark',nav-D.col[2]*.045,(.95,.58,.10),.026,D)
    cc.screen('NAVIGATION',nav,.80,.46,D,'navigation.png')
    for side,name,tex in [(-1,'COMMUNICATIONS','comms.png'),(1,'SYSTEMS','systems.png')]:
        p=Vector((side*.68,.41,1.08));R=aim(p)
        cc.screen(name,p,.258,.412,R,tex)
        # Adjustment pots sit on the inner flange and share its orientation.
        for k in range(3):cc.knob('AUX_SELECTOR_%s_%s'%(side,k),p+R@Vector((-side*.184,.12-k*.12,.015)),R,.016)
    # Tangential left/right decks turn the keys and knobs toward the pilot too.
    for side in [-1,1]:
        p=Vector((side*.65,.105,.876));R=Matrix.Rotation(math.radians(-side*43),3,'Z')@Matrix.Rotation(math.radians(16),3,'X')
        cc.panel('LowerControlDeck_'+str(side),g,'Metal',p,(.335,.265,.025),.016,R)
        cc.panel(None,g,'Ivory',p+R.col[2]*.016,(.322,.251,.019),.013,R)
        for x in [-.146,.146]:
            for y in [-.107,.107]:cc.bolt(g,p+R@Vector((x,y,.029)),R,.0032)
        if side<0:
            for j in range(3):
                for i in range(3):
                    q=p+R@Vector(((i-1)*.089,(j-1)*.068,.032))
                    if j==2 or i==2:cc.knob('DESK_KNOB_%d_%d'%(i,j),q,R,.018)
                    else:cc.button('DESK_ACTION_%d_%d'%(i,j),q,R,.034,.028,'Amber')
        else:
            for j,row in enumerate(['12345678','QWERTYUI','ASDFGHJK','ZXCVBN<>']):
                for i,ch in enumerate(row):cc.button('KEY_%d_%d'%(j,i),p+R@Vector(((i-3.5)*.0325,(1.5-j)*.041,.034)),R,.026,.029,'Ivory' if i<7 else 'Dark',ch)
    # Six centre toggles; no equipment added to the empty rear half of the room.
    c=Vector((0,.755,1.248))
    cc.panel('AuxiliarySwitchStrip',g,'Metal',c,(.66,.09,.028),.013,F)
    cc.panel(None,g,'Ivory',c+Vector((0,-.020,0)),(.64,.076,.014),.010,F)
    for i in range(6):
        p=c+Vector(((i-2.5)*.095,-.032,0));b.lathe('09_Controls','Metal',p,[(-.003,.011),(.006,.011)],20,(0,-1,0))
        b.cylinder('09_Controls','Dark',p,p+Vector((0,-.028,.011)),.0034,12)
        cc.panel('TOGGLE_%02d'%i,'09_Controls','White',p+Vector((0,-.031,.012)),(.010,.018,.011),.004,F)
        cc.label('09_Controls',['RCS','DOCK','COM','NAV','AUX','ARM'][i],p+Vector((0,-.004,.025)),.007,F)
    # Short service lines behind the outer pods follow the circular shell.
    for side in [-1,1]:
        cc.pipe('Pod_ServiceConduit','10_Fittings',[(side*.88,.56,.55),(side*.90,.55,.89),(side*.82,.64,1.52),(side*.71,.74,1.92)],.011)
    cc.root['pilot_eye_metres']=list(EYE)
    cc.root['layout']='Circular inner diameter2.20m; inward-facing rigid display pods; curved desk and rotated input decks'
    print('Built curved console with four eye-targeted screens and 50-degree navigation deck')
