"""Original, code-authored instrument graphics for the Blender-built cabin.
No Meshy imagery is used. Pixel coordinates define crisp technical readouts.
"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from pathlib import Path
import math, random, json

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/"Assets/MarsCabin/Textures"
OUT.mkdir(parents=True,exist_ok=True)
FONT="C:/Windows/Fonts/consola.ttf"
GREEN=(88,231,139); BRIGHT=(173,255,195); DIM=(22,94,55); FAINT=(11,42,28)
def font(size): return ImageFont.truetype(FONT,size)
def canvas(w,h,title,code):
    im=Image.new("RGB",(w,h),(2,12,8)); d=ImageDraw.Draw(im)
    d.rounded_rectangle((8,8,w-9,h-9),18,outline=DIM,width=3)
    d.text((32,24),title,font=font(34 if w>1400 else 29),fill=BRIGHT)
    d.text((w-250,30),code,font=font(21),fill=GREEN)
    d.line((28,77,w-28,77),fill=GREEN,width=2)
    d.line((28,h-62,w-28,h-62),fill=DIM,width=2)
    d.text((32,h-48),"LOCAL / SIM     SYS: NOMINAL",font=font(20),fill=GREEN)
    d.text((w-238,h-48),"MISSION 044.218",font=font(18),fill=GREEN)
    return im,d

def box(d,b,title):
    x0,y0,x1,y1=b;d.rectangle(b,outline=DIM,width=2)
    d.text((x0+14,y0+10),title,font=font(22),fill=GREEN)
    d.line((x0+10,y0+43,x1-10,y0+43),fill=DIM,width=1)
def grid(d,b,step=64):
    x0,y0,x1,y1=b
    for x in range(x0,x1+1,step):d.line((x,y0,x,y1),fill=FAINT,width=1)
    for y in range(y0,y1+1,step):d.line((x0,y,x1,y),fill=FAINT,width=1)
def radar(d,c,r):
    x,y=c
    for k in range(1,5):
        rr=r*k/4;d.ellipse((x-rr,y-rr,x+rr,y+rr),outline=DIM if k<4 else GREEN,width=2)
    d.line((x-r,y,x+r,y),fill=DIM,width=1);d.line((x,y-r,x,y+r),fill=DIM,width=1)
    for deg in range(0,360,15):
        a=math.radians(deg);r0=r-10 if deg%45==0 else r-5
        d.line((x+r0*math.cos(a),y+r0*math.sin(a),x+r*math.cos(a),y+r*math.sin(a)),fill=GREEN,width=2)
    a=-.6;d.line((x,y,x+math.cos(a)*r,y+math.sin(a)*r),fill=BRIGHT,width=3)
    for dx,dy in [(.42,-.3),(-.52,.32),(.05,.64)]:
        px=x+r*dx;py=y+r*dy;d.rectangle((px-4,py-4,px+4,py+4),fill=BRIGHT)
def trace(d,b,fn,color=GREEN,width=2):
    x0,y0,x1,y1=b
    points=[(x0+t*(x1-x0),y1-(fn(t)*.5+.5)*(y1-y0)) for t in [i/450 for i in range(451)]]
    d.line(points,fill=color,width=width)
def finish(im,name):
    # A restrained phosphor halo and scanlines; all art is generated from geometry.
    halo=im.filter(ImageFilter.GaussianBlur(2.2))
    from PIL import ImageChops
    im=ImageChops.add(im,halo,scale=1.35)
    d=ImageDraw.Draw(im)
    for y in range(0,im.height,4):
        # Small contrast change, not opaque black bars.
        for x in range(im.width):
            r,g,b=im.getpixel((x,y));d.point((x,y),fill=(int(r*.82),int(g*.82),int(b*.82)))
    im.save(OUT/name)
    return name

im,d=canvas(2048,1024,"ARES  /  SURFACE NAVIGATION","NAV - 03 / ACTIVE")
box(d,(30,98,1430,740),"LOCAL TOPOGRAPHY   |   GRID 38 - 71"); grid(d,(48,150,1412,720),64)
# Perspective terrain wireframe, with a distinct valley through the center.
def project(x,z):
    elev=.34*math.exp(-((x+.35)**2*5+(z-.28)**2*10))+.28*math.exp(-((x-.55)**2*8+(z-.5)**2*12))
    elev+=.025*math.sin(16*x+5*z)*math.cos(14*z)
    return 730+x*(610-260*z), 680-z*365-elev*410
for j in range(26):
    z=j/25
    pts=[project(-1+2*i/100,z) for i in range(101)]
    d.line(pts,fill=(40,140+int(65*(1-z)),87),width=2)
for i in range(33):
    x=-1+2*i/32; d.line([project(x,j/60) for j in range(61)],fill=DIM,width=1)
route=[project(.12+.13*math.sin(z*5),z) for z in [i/40 for i in range(41)]]
d.line(route,fill=BRIGHT,width=4)
for t in [.1,.37,.7,.94]:
    x,y=project(.12+.13*math.sin(t*5),t);d.ellipse((x-7,y-7,x+7,y+7),outline=BRIGHT,width=2)
d.text((60,166),"N 18.42     E 137.65",font=font(23),fill=GREEN)
d.text((62,696),"SCALE  1 : 25000      +  TARGET  ARES-04",font=font(21),fill=GREEN)
box(d,(1450,98,2016,480),"BEARING / RANGE");radar(d,(1735,300),135)
d.text((1474,445),"HDG  042.8     RNG  2.41 KM",font=font(22),fill=BRIGHT)
box(d,(1450,498,2016,942),"FLIGHT TELEMETRY")
rows=[("ALTITUDE","1 370 M"),("VELOCITY","003.2 M/S"),("PITCH","+00.4 DEG"),("ROLL","-00.2 DEG"),("FUEL","86.4 %"),("PRESSURE","101.3 KPA"),("LINK","ESTABLISHED")]
for j,(a,b) in enumerate(rows):
    d.text((1470,558+j*48),a,font=font(21),fill=GREEN);d.text((1780,558+j*48),b,font=font(21),fill=BRIGHT)
box(d,(30,758,1430,942),"ELEVATION PROFILE / NEXT 2.4 KM");grid(d,(48,815,1412,925),80)
trace(d,(50,820,1410,924),lambda t:.45*math.sin(t*7)+.21*math.sin(t*23),GREEN,3)
files=[finish(im,"HeroCRT_Navigation.png")]

im,d=canvas(1024,1024,"COMMUNICATIONS","RF / 02")
box(d,(30,98,994,488),"SPECTRUM  437.250 MHZ");grid(d,(48,152,976,464),64)
def spectrum(t):
    return -.83 + .13*abs(math.sin(t*91))+.09*abs(math.sin(t*217))+1.55*math.exp(-((t-.48)/.017)**2)+.52*math.exp(-((t-.66)/.03)**2)
trace(d,(50,164,974,460),spectrum,GREEN,2)
for i,s in enumerate(["432.0","434.0","436.0","438.0","440.0"]):
    d.text((55+i*198,460),s,font=font(18),fill=GREEN)
box(d,(30,508,494,942),"ANTENNA TRACK");radar(d,(260,744),154)
box(d,(514,508,994,942),"CHANNEL REGISTER")
rows=[("UPLINK","LOCK"),("DOWNLINK","LOCK"),("GAIN","24.8 DB"),("SNR","38.2 DB"),("PACKET","001 284"),("CRC ERR","000 000"),("ANT AZ","042.8"),("ANT EL","+27.4")]
for j,(a,b) in enumerate(rows):
    d.text((536,565+j*43),a,font=font(20),fill=GREEN);d.text((792,565+j*43),b,font=font(20),fill=BRIGHT)
files.append(finish(im,"HeroCRT_Comms.png"))

im,d=canvas(1024,1024,"ENVIRONMENT / POWER","ECS / 01")
box(d,(30,98,994,380),"LIFE SUPPORT   /   CABIN LOOP")
values=[("OXYGEN",.209,"20.9 %"),("PRESSURE",.89,"101.3 KPA"),("BATTERY",.86,"86.4 %"),("COOLANT",.71,"71.0 %")]
for j,(label,v,value) in enumerate(values):
    y=165+j*50;d.text((52,y),label,font=font(21),fill=GREEN);d.rectangle((290,y+3,740,y+23),outline=DIM,width=2)
    for k in range(int(v*35)):d.rectangle((294+k*12,y+6,301+k*12,y+20),fill=GREEN)
    d.text((770,y),value,font=font(21),fill=BRIGHT)
box(d,(30,398,994,680),"BUS LOAD / DISTRIBUTION");grid(d,(48,454,976,655),64)
trace(d,(50,462,974,646),lambda t:.3*math.sin(t*18)+.12*math.sin(t*67),GREEN,3)
trace(d,(50,462,974,646),lambda t:-.4+.12*math.sin(t*13+2),DIM,2)
box(d,(30,698,994,942),"STATUS REGISTER")
rows=[("THERMAL","22.4 C","NOMINAL"),("AIR SCRUBBER","LOOP A","ONLINE"),("MAIN POWER","28.4 V DC","STABLE"),("RESERVE O2","92.1 %","READY")]
for j,(a,b,c) in enumerate(rows):
    y=758+j*42;d.text((52,y),a,font=font(21),fill=GREEN);d.text((424,y),b,font=font(21),fill=BRIGHT);d.text((785,y),c,font=font(21),fill=GREEN)
files.append(finish(im,"HeroCRT_Systems.png"))
(OUT/"HeroCRT_manifest.json").write_text(json.dumps({"source":"original code-authored vector instrument graphics","generator":"SourceArt/MarsCabin/build_crt_graphics.py","files":files,"meshy_content":False},indent=2),encoding="utf-8")
print(json.dumps({"textures":files,"output":str(OUT)}))

