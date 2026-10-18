"""Original vector-drawn phosphor avionics for the compact single-pilot cockpit."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops
import math, random, json

OUT = Path(__file__).resolve().parent / 'Textures'
OUT.mkdir(parents=True, exist_ok=True)
FONT = 'C:/Windows/Fonts/consola.ttf'
BOLD = 'C:/Windows/Fonts/consolab.ttf'
BG=(1,9,7); GREEN=(62,217,143); LIME=(151,255,192); DIM=(23,100,66); FAINT=(7,40,27)

def f(n,bold=False): return ImageFont.truetype(BOLD if bold else FONT,n)
def tx(d,xy,text,size=24,fill=GREEN,bold=False): d.text(xy,text,font=f(size,bold),fill=fill)
def center(d,xy,text,size=24,fill=GREEN): d.text(xy,text,font=f(size),fill=fill,anchor='mm')
def line(d,p,fill=GREEN,width=2): d.line(p,fill=fill,width=width)
def panel(d,b,title,size=23):
    x0,y0,x1,y1=b
    d.rectangle(b,outline=DIM,width=2)
    tx(d,(x0+14,y0+10),title,size)
    line(d,(x0+10,y0+42,x1-10,y0+42),DIM,1)

def canvas(w,h,title,code):
    im=Image.new('RGB',(w,h),BG);d=ImageDraw.Draw(im)
    d.rounded_rectangle((9,9,w-10,h-10),radius=20,outline=DIM,width=2)
    tx(d,(30,23),title,30,LIME,True)
    tw=d.textbbox((0,0),code,font=f(19))[2]
    tx(d,(w-30-tw,32),code,19)
    line(d,(26,72,w-26,72),GREEN,2)
    line(d,(26,h-51,w-26,h-51),DIM,1)
    tx(d,(30,h-41),'CS-01  /  ORBITAL UTILITY',18)
    if w>700: tx(d,(w-302,h-41),'MISSION 044 : 218 : 06',18)
    else: tx(d,(w-90,h-41),'LIVE',18,LIME)
    return im,d

def grid(d,b,s=45):
    x0,y0,x1,y1=map(int,b)
    for x in range(x0,x1+1,s): line(d,(x,y0,x,y1),FAINT,1)
    for y in range(y0,y1+1,s): line(d,(x0,y,x1,y),FAINT,1)

def segments(d,xy,w,h,value,n=18):
    x,y=xy; gap=4; sw=(w-gap*(n-1))/n
    for i in range(n):
        d.rectangle((x+i*(sw+gap),y,x+i*(sw+gap)+sw,y+h),fill=GREEN if i<n*value else FAINT,outline=DIM,width=1)

def radar(d,c,r,contacts=True):
    x,y=c
    for i in range(1,7):
        rr=r*i/6;d.ellipse((x-rr,y-rr,x+rr,y+rr),outline=GREEN if i==6 else DIM,width=2 if i==6 else 1)
    for a in range(0,360,30):
        ang=math.radians(a);line(d,(x,y,x+r*math.cos(ang),y+r*math.sin(ang)),FAINT,1)
    line(d,(x-r,y,x+r,y),DIM,1);line(d,(x,y-r,x,y+r),DIM,1)
    for a in range(0,360,5):
        ang=math.radians(a);rl=r-(12 if a%30==0 else 5)
        line(d,(x+rl*math.cos(ang),y+rl*math.sin(ang),x+r*math.cos(ang),y+r*math.sin(ang)),GREEN,2)
    for a,label in [(-90,'000'),(0,'090'),(90,'180'),(180,'270')]:
        ang=math.radians(a);center(d,(x+(r+24)*math.cos(ang),y+(r+24)*math.sin(ang)),label,18)
    a=-.62
    line(d,(x,y,x+r*math.cos(a),y+r*math.sin(a)),LIME,3)
    for spread in range(2,17,2):
        aa=a+spread*.01;line(d,(x,y,x+r*math.cos(aa),y+r*math.sin(aa)),(12,67-spread*2,42-spread),1)
    line(d,[(x,y-13),(x-11,y+10),(x+11,y+10),(x,y-13)],LIME,3)
    if contacts:
        for i,(dx,dy) in enumerate([(-.53,-.38),(-.66,.27),(.3,-.59),(.48,.38),(.08,.72),(-.21,-.68),(.68,-.3)]):
            px=x+r*dx;py=y+r*dy
            d.ellipse((px-5,py-5,px+5,py+5),fill=LIME)
            d.ellipse((px-10,py-10,px+10,py+10),outline=DIM,width=1)
            if r>200: tx(d,(px+12,py-9),f'{i+1:02}',17)

def craft(d,c,s):
    # Symmetric top view, original lifting-body shuttle and service pods.
    x,y=c
    def p(points): return [(x+px*s,y+py*s) for px,py in points]
    contour=[(0,-115),(-13,-93),(-20,-47),(-32,-22),(-80,42),(-88,84),(-61,88),(-37,46),(-26,88),(-13,101),(13,101),(26,88),(37,46),(61,88),(88,84),(80,42),(32,-22),(20,-47),(13,-93),(0,-115)]
    line(d,p(contour),GREEN,3)
    for sign in [-1,1]:
        for pts in [[(11,-96),(13,-48),(18,80),(12,96)],[(17,-53),(45,55),(63,82)],[(22,-39),(67,46),(76,76)],[(12,-49),(25,-23),(39,53)],[(0,-30),(30,-15),(74,54)],[(0,12),(38,30),(81,70)]]:
            line(d,p([(sign*a,b) for a,b in pts]),DIM,2)
        for yy,ww in [(-62,17),(-35,24),(-7,32),(17,39),(42,47),(66,53)]:
            line(d,p([(0,yy),(sign*ww,yy+5)]),GREEN,1)
        pod=[(sign*52,47),(sign*67,43),(sign*74,81),(sign*67,103),(sign*52,100),(sign*47,82),(sign*52,47)]
        line(d,p(pod),LIME,2)
        line(d,p([(sign*48,81),(sign*74,81)]),GREEN,2)
        line(d,p([(sign*51,91),(sign*70,92)]),DIM,1)
    line(d,p([(0,-121),(0,110)]),DIM,1)
    line(d,p([(-10,-86),(10,-86),(12,-66),(-12,-66),(-10,-86)]),LIME,2)
    line(d,p([(-18,88),(18,88),(14,105),(-14,105),(-18,88)]),GREEN,2)

def finish(im,name):
    # Preserve crisp strokes with only a restrained halo.
    glow=im.filter(ImageFilter.GaussianBlur(2.0))
    im=ImageChops.add(im,glow,scale=1.12)
    scan=Image.new('RGB',im.size,(255,255,255));sd=ImageDraw.Draw(scan)
    for y in range(0,im.height,4):sd.line((0,y,im.width,y),fill=(224,233,226))
    im=ImageChops.multiply(im,scan)
    im.save(OUT/name)
    return im

def make_radar():
    im,d=canvas(1200,1000,'PROXIMITY / RADAR','RNG  025 KM')
    panel(d,(25,94,836,829),'SECTOR 04  /  TRACKING ACTIVE');grid(d,(42,147,820,810),54)
    radar(d,(430,480),302)
    tx(d,(51,153),'RELATIVE FRAME',19);tx(d,(51,784),'07 CONTACTS     AUTO TRACK',22)
    panel(d,(858,94,1175,490),'TARGET REGISTER')
    for i,(label,val) in enumerate([('ID','CARGO-07'),('RANGE','08.42 KM'),('BEARING','042.8'),('REL V','-02.4 M/S'),('CLOSURE','NOMINAL'),('LINK','LOCKED')]):
        y=159+i*50;tx(d,(878,y),label,20);tx(d,(1000,y),val,20,LIME)
    panel(d,(858,510,1175,829),'SIGNAL / RETURN')
    radar(d,(1017,667),80,False)
    segments(d,(882,790),267,14,.87,19)
    panel(d,(25,849,1175,935),'ECHO INTENSITY')
    for i in range(74):
        h=8+22*abs(math.sin(i*.63))+13*math.exp(-((i-30)/6)**2)
        d.rectangle((311+i*11,924-h,317+i*11,924),fill=GREEN if i%4 else LIME)
    return finish(im,'radar.png')

def make_attitude():
    im,d=canvas(1200,1000,'FLIGHT / GUIDANCE','INERTIAL  /  01')
    panel(d,(25,94,722,755),'ATTITUDE  /  LOCAL HORIZON')
    cx,cy,r=374,442,257
    # Fill the circle with subdued blue and earth tones.
    d.pieslice((cx-r,cy-r,cx+r,cy+r),180,360,fill=(11,45,59))
    d.pieslice((cx-r,cy-r,cx+r,cy+r),0,180,fill=(65,42,20))
    d.ellipse((cx-r,cy-r,cx+r,cy+r),outline=GREEN,width=3)
    for a in range(0,360,10):
        ang=math.radians(a);ll=18 if a%30==0 else 9
        line(d,(cx+(r-ll)*math.cos(ang),cy+(r-ll)*math.sin(ang),cx+r*math.cos(ang),cy+r*math.sin(ang)),LIME,2)
    line(d,(cx-r,cy,cx+r,cy),LIME,3)
    line(d,(cx,cy-r+23,cx,cy+r-23),GREEN,1)
    for v in range(-60,61,10):
        if not v:continue
        yy=cy-v*3.1;ww=67 if v%20==0 else 40
        line(d,(cx-ww,yy,cx-12,yy),GREEN,2);line(d,(cx+12,yy,cx+ww,yy),GREEN,2)
        center(d,(cx-ww-23,yy),str(abs(v)),18);center(d,(cx+ww+23,yy),str(abs(v)),18)
    line(d,[(cx-116,cy+9),(cx-45,cy+9),(cx-31,cy+26),(cx-15,cy+9),(cx+15,cy+9),(cx+31,cy+26),(cx+45,cy+9),(cx+116,cy+9)],LIME,4)
    line(d,[(cx,cy-r-31),(cx-10,cy-r-11),(cx+10,cy-r-11),(cx,cy-r-31)],LIME,3)
    center(d,(374,721),'PITCH  +00.4     ROLL  -00.2',24,LIME)
    panel(d,(743,94,1175,619),'VEHICLE / ORIENTATION')
    grid(d,(758,145,1160,601),45);craft(d,(958,370),1.66)
    tx(d,(761,580),'YAW  042.8     STABLE',21,LIME)
    panel(d,(743,639,1175,935),'REACTION CONTROL')
    for i,(label,v) in enumerate([('PITCH',.65),('ROLL',.52),('YAW',.78),('TRIM',.87)]):
        yy=699+i*53;tx(d,(761,yy),label,21);segments(d,(850,yy+2),260,18,v,17)
    panel(d,(25,777,722,935),'FLIGHT TELEMETRY')
    for i,(a,b,c) in enumerate([('ALTITUDE','01 370','M'),('VELOCITY','003.20','M/S'),('HEADING','042.80','DEG')]):
        xx=44+i*225;tx(d,(xx,830),a,18);tx(d,(xx,860),b,33,LIME,True);tx(d,(xx,903),c,17)
    return finish(im,'attitude.png')

def make_navigation():
    im,d=canvas(1600,1000,'SURFACE / NAVIGATION','AUTO PILOT  :  STANDBY')
    panel(d,(25,94,1212,814),'LOCAL TOPOGRAPHY  /  GRID 38 - 71');grid(d,(40,145,1198,794),65)
    tx(d,(50,156),'N 18.42   E 137.65',21);tx(d,(50,190),'DTM  25M / CELL',18)
    def proj(x,z):
        peaks=.35*math.exp(-((x+.5)**2*12+(z-.35)**2*17))+.55*math.exp(-((x-.45)**2*18+(z-.60)**2*15))+.36*math.exp(-((x+.30)**2*10+(z-.87)**2*30))
        peaks+=(.021*math.sin(x*38+z*15)+.018*math.cos(x*22-z*37))*(.3+peaks*1.8)
        return 620+x*(554-220*z),760-z*433-peaks*390
    for j in reversed(range(39)):
        z=j/38
        line(d,[proj(-1+2*i/90,z) for i in range(91)],(29,130+int(60*(1-z)),90),2)
    for i in range(52):line(d,[proj(-1+2*i/51,j/80) for j in range(81)],DIM,1)
    for i in range(55):
        z0=i/55;z1=(i+.6)/55
        line(d,[proj(.02+.15*math.sin(z*5),z) for z in [z0,z1]],LIME,4)
    for j,z in enumerate([.08,.33,.60,.90]):
        xx,yy=proj(.02+.15*math.sin(z*5),z)
        d.ellipse((xx-9,yy-9,xx+9,yy+9),fill=BG,outline=LIME,width=3)
        tx(d,(xx+16,yy-24),f'WP-{j+1:02}',20,LIME)
    tx(d,(47,779),'ROUTE B-17    /    APPROACH VECTOR VERIFIED',19)
    panel(d,(1233,94,1575,407),'BEARING / RANGE')
    radar(d,(1405,252),85,False)
    tx(d,(1250,372),'042.8 DEG  /  2.41 KM',19,LIME)
    panel(d,(1233,426,1575,814),'NAV REGISTER')
    for i,(a,b) in enumerate([('TIME TO GO','04:52'),('GROUND V','003.2 M/S'),('ELEVATION','1370 M'),('SLOPE','04.2 DEG'),('CLEARANCE','048 M'),('PRECISION','0.25 M')]):
        yy=484+i*50;tx(d,(1250,yy),a,18);tx(d,(1431,yy),b,18,LIME)
    panel(d,(25,834,1575,935),'ELEVATION PROFILE  /  NEXT 2.4 KM')
    grid(d,(505,852,1544,922),65)
    pts=[(505+i*2.3,917-10-14*math.sin(i*.022)-11*math.sin(i*.067)-25*math.exp(-((i-310)/65)**2)) for i in range(451)]
    line(d,pts,GREEN,2)
    tx(d,(45,891),'MIN 1328 M   MAX 1524 M',19,LIME)
    return finish(im,'navigation.png')

def make_comms():
    im,d=canvas(600,1000,'COMMS','RF / 02')
    panel(d,(25,94,575,436),'NETWORK REGISTER')
    rows=[('01','BRIDGE','ONLINE'),('02','ENGINEERING','ONLINE'),('03','LIFE SUPPORT','ONLINE'),('04','NAVIGATION','ONLINE'),('05','SCIENCE','STANDBY'),('06','CARGO','ONLINE')]
    for i,(no,lab,status) in enumerate(rows):
        yy=155+i*44;tx(d,(43,yy),no,23);tx(d,(94,yy),lab,23);d.ellipse((394,yy+6,404,yy+16),outline=GREEN,width=2);tx(d,(424,yy),status,21,LIME if status=='ONLINE' else GREEN)
    panel(d,(25,455,575,784),'CHANNEL SELECT')
    for i,(name,status) in enumerate([('GENERAL','ACTIVE'),('FLEET','STANDBY'),('SCIENCE','OPEN'),('ENGINEERING','OPEN'),('SECURITY','STANDBY'),('EMERGENCY','OPEN')]):
        yy=515+i*42
        if i==0:d.rectangle((37,yy-2,563,yy+31),fill=FAINT,outline=DIM,width=1)
        tx(d,(44,yy),'>' if i==0 else ' ',23,LIME);tx(d,(74,yy),f'{i+1:02}',23);tx(d,(120,yy),name,23);tx(d,(428,yy),status,20,LIME)
    panel(d,(25,805,575,935),'UPLINK  /  437.250 MHZ')
    for i in range(80):
        xx=43+i*6.3;yy=906-6*math.sin(i*2.7)-31*math.exp(-((i-43)/3)**2)
        line(d,(xx,yy,xx+4,907-6*math.sin((i+1)*2.7)-31*math.exp(-((i+1-43)/3)**2)),GREEN,2)
    tx(d,(43,849),'SNR 38.2 DB    ENCRYPTED',18,LIME)
    return finish(im,'comms.png')

def make_systems():
    im,d=canvas(600,1000,'CRAFT STATUS','ECS / 01')
    panel(d,(25,94,575,617),'SYSTEMS / PRESSURIZED')
    grid(d,(42,149,557,599),43)
    craft(d,(303,374),1.55)
    for i,(label,value) in enumerate([('ENG','100'),('PWR','100'),('NAV','100'),('THR','100'),('SHLD','100')]):
        yy=169+i*36;tx(d,(45,yy),label,19);tx(d,(105,yy),value,19,LIME)
    tx(d,(483,169),'FUEL',18);tx(d,(492,197),'86%',19,LIME)
    for j in range(26):d.rectangle((500,235+j*10,535,240+j*10),fill=GREEN if j>3 else FAINT,outline=DIM,width=1)
    tx(d,(46,572),'HULL',18);segments(d,(119,574),425,15,.96,27)
    panel(d,(25,637,575,935),'SUBSYSTEMS')
    for i,(label,v) in enumerate([('PROPULSION',.93),('REACTOR',.84),('GYRO',.98),('AUXILIARY',.76),('OXYGEN',.91),('COOLANT',.72)]):
        yy=696+i*37;tx(d,(44,yy),label,21);segments(d,(267,yy+3),218,16,v,14);tx(d,(502,yy),f'{int(v*100):02}',20,LIME)
    return finish(im,'systems.png')

if __name__=='__main__':
    ims=[make_radar(),make_attitude(),make_navigation(),make_comms(),make_systems()]
    montage=Image.new('RGB',(1800,1300),(9,16,14))
    for im,xy,sz in [(ims[0],(0,0),(720,600)),(ims[1],(720,0),(720,600)),(ims[3],(1440,0),(360,600)),(ims[2],(0,600),(1120,700)),(ims[4],(1170,600),(420,700))]:
        montage.paste(im.resize(sz,Image.Resampling.LANCZOS),xy)
    montage.save(OUT/'instrument_montage.jpg',quality=94)
    (OUT/'manifest.json').write_text(json.dumps({'art':'Original code-authored instrument graphics','font':'Windows Consolas','textures':{n:im.size for n,im in zip(['radar.png','attitude.png','navigation.png','comms.png','systems.png'],ims)}},indent=2),encoding='utf-8')
    print(str(OUT))
