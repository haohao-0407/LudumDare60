"""Integral instrument wall: no tabletop, rail across the pilot, or desk legs."""
import importlib.util
from pathlib import Path
from mathutils import Vector, Matrix

EYE=Vector((0,-.48,1.44))


def aim(p):
    n=(EYE-Vector(p)).normalized()
    u=Vector((0,0,1)).cross(n).normalized()
    v=n.cross(u).normalized()
    return Matrix((u,v,n)).transposed()


def build(cc):
    b=cc.b;g='07_ConsoleBody';F=cc.FRONT
    sp=importlib.util.spec_from_file_location('wall_transition',str(Path(__file__).with_name('wall_transition.py')))
    wt=importlib.util.module_from_spec(sp);sp.loader.exec_module(wt)
    for side,name,tex in [(-1,'RADAR','radar.png'),(1,'ATTITUDE','attitude.png')]:
        p=Vector((side*.40,.77,1.625));R=aim(p);n=R.col[2]
        wt.embed(cc,'UpperWall_'+name,p,R,.742,.635)
        cc.panel('UpperInstrumentPlate_'+name,g,'Ivory',p-n*.047,(.742,.635,.105),.05,R)
        cc.screen(name,p,.585,.465,R,tex)
        xx=side*.354
        points=[p+R@Vector((xx,v,z)) for v,z in [(-.249,.02),(-.224,.067),(.224,.067),(.249,.02)]]
        cc.pipe(name+'_GrabRail','10_Fittings',points,.010)
        for vv in [-.249,.249]:
            b.lathe('10_Fittings','Metal',p+R@Vector((xx,vv,.018)),[(-.003,.017),(.007,.017)],24,n)
        q=Vector((side*.83,.52,1.60));Q=aim(q)
        wt.embed(cc,'OuterBank_'+name,q,Q,.115,.59)
        cc.panel('OuterControlStrip_'+name,g,'Ivory',q,(.108,.575,.045),.014,Q)
        for k in range(3):cc.button('BANK_STATUS_%s_%s'%(side,k),q+Q@Vector((0,.025+k*.073,.027)),Q,w=.032,h=.036)
        cc.knob('BANK_SELECTOR_'+name,q+Q@Vector((0,-.153,.025)),Q,r=.022)

    # The central spine and its trim are seated in the same pressure-wall bank.
    spine=Vector((0,.934,1.60))
    wt.embed(cc,'MainPowerSpine',spine,F,.130,.70)
    cc.panel('MainPowerSpine',g,'Ivory',spine,(.108,.67,.07),.017,F)
    cc.panel(None,g,'Dark',(0,.892,1.625),(.043,.40,.013),.005,F)
    for k in range(15):cc.panel(None,'09_Controls','Amber' if k<10 else 'Rubber',(0,.881,1.444+k*.025),(.024,.019,.008),.002,F)
    cc.button('MASTER_CAUTION',(0,.88,1.88),F,w=.057,h=.028)
    cc.label(g,'PWR',(0,.885,1.368),.012,F)

    # The former desktop chart is a steep, eye-targeted wall recess.
    nav=Vector((0,.64,.995));D=aim(nav)
    wt.embed(cc,'NavigationWall',nav,D,.950,.600)
    cc.panel('NavigationMountPlate',g,'Ivory',nav-D.col[2]*.046,(.940,.590,.075),.040,D)
    cc.screen('NAVIGATION',nav,.80,.46,D,'navigation.png')
    for side,name,tex in [(-1,'COMMUNICATIONS','comms.png'),(1,'SYSTEMS','systems.png')]:
        p=Vector((side*.69,.52,1.09));R=aim(p)
        wt.embed(cc,'LowerWall_'+name,p,R,.470,.545)
        cc.panel('LowerInstrumentPlate_'+name,g,'Ivory',p-R.col[2]*.035,(.466,.541,.070),.035,R)
        cc.screen(name,p,.258,.412,R,tex)
        for k in range(3):cc.knob('AUX_SELECTOR_%s_%s'%(side,k),p+R@Vector((-side*.190,.12-k*.12,.015)),R,.016)

    # Inputs become slanted in-wall panels, with sealed returns to the cabin.
    for side in [-1,1]:
        p=Vector((side*.69,.56,.61));R=aim(p)
        wt.embed(cc,'InputWall_'+str(side),p,R,.385,.302)
        cc.panel('WallInputPlate_'+str(side),g,'Metal',p,(.365,.284,.025),.016,R)
        cc.panel(None,g,'Ivory',p+R.col[2]*.016,(.352,.270,.019),.013,R)
        for x in [-.157,.157]:
            for y in [-.116,.116]:cc.bolt(g,p+R@Vector((x,y,.029)),R,.0032)
        if side<0:
            for j in range(3):
                for i in range(3):
                    q=p+R@Vector(((i-1)*.089,(j-1)*.068,.032))
                    if j==2 or i==2:cc.knob('WALL_KNOB_%d_%d'%(i,j),q,R,.018)
                    else:cc.button('WALL_ACTION_%d_%d'%(i,j),q,R,.034,.028,'Amber')
        else:
            for j,row in enumerate(['12345678','QWERTYUI','ASDFGHJK','ZXCVBN<>']):
                for i,ch in enumerate(row):cc.button('KEY_%d_%d'%(j,i),p+R@Vector(((i-3.5)*.0325,(1.5-j)*.041,.034)),R,.026,.029,'Ivory' if i<7 else 'Dark',ch)

    # Shallow centre service apron links the instrument bank with the lower wall.
    apron=Vector((0,.76,.555))
    wt.embed(cc,'CentralServiceApron',apron,F,.930,.270)
    cc.panel('CentralServiceApron_Cover',g,'Ivory',apron,(.922,.262,.025),.025,F)
    cc.panel('CentralServiceApron_ID',g,'Orange',(0,.743,.652),(.75,.008,.004),.002,F)
    for side in [-1,1]:
        for k in range(7):
            cc.panel(None,g,'Dark',(side*.345,.744,.488+k*.016),(.095,.006,.003),.002,F)
        for x in [side*.42,side*.12]:
            for z in [.448,.657]:cc.bolt(g,(x,.743,z),F,.0035)
    cc.label(g,'FLIGHT SYSTEMS / SERVICE ACCESS',(0,.742,.560),.011,F)
    for i in range(5):cc.button('ANNUNCIATOR_%02d'%i,((i-2)*.049,.742,.500),F,w=.036,h=.027,mat='Amber' if i<2 else 'White')

    c=Vector((0,.79,1.275))
    wt.embed(cc,'AuxSwitchWall',c,F,.70,.110)
    cc.panel('AuxiliarySwitchStrip',g,'Metal',c,(.68,.10,.028),.013,F)
    cc.panel(None,g,'Ivory',c+Vector((0,-.020,0)),(.66,.086,.014),.010,F)
    for i in range(6):
        p=c+Vector(((i-2.5)*.095,-.032,0));b.lathe('09_Controls','Metal',p,[(-.003,.011),(.006,.011)],20,(0,-1,0))
        b.cylinder('09_Controls','Dark',p,p+Vector((0,-.028,.011)),.0034,12)
        cc.panel('TOGGLE_%02d'%i,'09_Controls','White',p+Vector((0,-.031,.012)),(.010,.018,.011),.004,F)
        cc.label('09_Controls',['RCS','DOCK','COM','NAV','AUX','ARM'][i],p+Vector((0,-.004,.025)),.007,F)
    wt.build_seams(cc)
    cc.root['pilot_eye_metres']=list(EYE)
    cc.root['layout']='Integral front instrument wall; five inward-facing screens, inclined wall controls, hollow rounded fairings to cylindrical shell. No tabletop or desk legs.'
    print('Built wall-integrated console: five eye-targeted screens; no desk geometry')
