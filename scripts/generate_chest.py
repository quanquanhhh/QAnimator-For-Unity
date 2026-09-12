"""Deterministic original treasure chest animation. Requires Pillow and FFmpeg.
python scripts/generate_chest.py --ffmpeg path/to/ffmpeg.exe
"""
import argparse
import math
import random
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SIZE, FPS, FRAMES, AA = 512, 30, 120, 2


def render(frame):
    t = frame / FPS
    u = min(1, max(0, (t - .65) / 1.15))
    opening = 1 - (1-u)**3
    angle = opening * 1.65
    canvas = Image.new('RGBA', (SIZE*AA, SIZE*AA))
    draw = ImageDraw.Draw(canvas)

    def project(p):
        x, y, z = p
        return ((256 + x*.90 + z*.48)*AA, (340 - y - z*.32 + x*.12)*AA)

    def poly(points, color, outline='#4d2b21', width=2):
        pts = [project(p) for p in points]
        draw.polygon(pts, fill=color)
        if outline:
            draw.line(pts+[pts[0]], fill=outline, width=width*AA, joint='curve')

    def lid(x, z, height=0):
        # Rear hinge at z=110, y=108.
        distance = 110-z
        return (x, 108 + distance*math.sin(angle) + height*math.cos(angle),
                110-distance*math.cos(angle) + height*math.sin(angle))

    # Contact shadow; semi-transparent edges also exercise alpha storage.
    shadow = Image.new('RGBA', canvas.size)
    ImageDraw.Draw(shadow).ellipse((100*AA, 327*AA, 426*AA, 385*AA), fill=(0, 0, 0, 95))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(12*AA)))
    draw = ImageDraw.Draw(canvas)
    # Box shell, visible interior and right side.
    poly([(-125,0,0),(125,0,0),(125,108,0),(-125,108,0)], '#965025')
    poly([(125,0,0),(125,0,110),(125,108,110),(125,108,0)], '#653821')
    poly([(-125,108,0),(125,108,0),(125,108,110),(-125,108,110)], '#39231f')
    poly([(-112,108,12),(112,108,12),(112,108,97),(-112,108,97)], '#261b20', None)
    for y in (30, 61, 90):
        poly([(-124,y,0),(124,y,0),(124,y+2,0),(-124,y+2,0)], '#643b24', None)
        poly([(125,y,1),(125,y,109),(125,y+2,109),(125,y+2,1)], '#422b21', None)
    # Wood grain, fixed between frames.
    rng = random.Random(121)
    for _ in range(48):
        x, y = rng.uniform(-120,100), rng.uniform(6,103)
        draw.line([project((x,y,0)), project((min(120,x+rng.uniform(5,25)),y+1,0))], fill='#ac6431', width=AA)
    # Metal straps, corners, rivets.
    for left in (-111, 77):
        poly([(left,0,-1),(left+28,0,-1),(left+28,109,-1),(left,109,-1)], '#deb14b')
        for y in (12, 48, 95):
            px, py = project((left+14,y,-2))
            draw.ellipse((px-3*AA,py-3*AA,px+3*AA,py+3*AA),fill='#fff0a0',outline='#865a26',width=AA)
    for y in (0, 99):
        poly([(-125,y,-2),(125,y,-2),(125,y+9,-2),(-125,y+9,-2)], '#efc45f')
        poly([(126,y,0),(126,y,110),(126,y+9,110),(126,y+9,0)], '#b58636')

    # Treasure is occluded by the closed lid naturally.
    rng = random.Random(37)
    for _ in range(65):
        px, py = project((rng.uniform(-98,98),111,rng.uniform(15,92)))
        draw.ellipse((px-9*AA,py-3*AA,px+9*AA,py+3*AA),fill='#f3c64d',outline='#a26c21',width=AA)
    # Hinged lid and raised planks.
    poly([lid(-125,0),lid(125,0),lid(125,110),lid(-125,110)], '#b26b33')
    for z in (0, 28, 56, 84):
        poly([lid(-125,z),lid(125,z),lid(125,z+26),lid(-125,z+26)], '#a96130')
    for x in (-111,77):
        poly([lid(x,0,2),lid(x+28,0,2),lid(x+28,110,2),lid(x,110,2)], '#e9bc54')
    for z in (0,102):
        poly([lid(-125,z,3),lid(125,z,3),lid(125,z+8,3),lid(-125,z+8,3)], '#ffd77b')
    # Lid front thickness, lock and cyan gem.
    poly([lid(-125,0),lid(125,0),lid(125,0,16),lid(-125,0,16)], '#e9b74d')
    poly([(-18,65,-4),(18,65,-4),(18,104,-4),(-18,104,-4)], '#efca67')
    poly([(0,72,-5),(11,84,-5),(0,98,-5),(-11,84,-5)], '#60e6ed')

    if opening > .12:
        glow = Image.new('RGBA',canvas.size)
        gd = ImageDraw.Draw(glow)
        strength = int(70*opening)
        gd.ellipse((155*AA,170*AA,390*AA,292*AA), fill=(255,199,64,strength))
        canvas.alpha_composite(glow.filter(ImageFilter.GaussianBlur(23*AA)))
        draw = ImageDraw.Draw(canvas)
        rng = random.Random(7)
        for i in range(24):
            seed = rng.random()
            phase = ((t-.8)*(.4+seed*.3)+seed) % 1
            x = 270+rng.uniform(-105,105)*(phase+.3)
            y = 250-phase*150
            r = (2+seed*3)*math.sin(math.pi*phase)*opening
            alpha = int(255*math.sin(math.pi*phase)*opening)
            pts=[((x-r)*AA,y*AA),(x*AA,(y-r*2)*AA),((x+r)*AA,y*AA),(x*AA,(y+r*2)*AA)]
            draw.polygon(pts,fill=(255,224,126,alpha))
    return canvas.resize((SIZE,SIZE),Image.Resampling.LANCZOS)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--ffmpeg',default='ffmpeg')
    args=parser.parse_args()
    common=[args.ffmpeg,'-y','-v','error','-f','rawvideo','-pixel_format','rgba','-video_size',f'{SIZE}x{SIZE}','-framerate',str(FPS),'-i','pipe:0','-an']
    mp4=subprocess.Popen(common+['-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(ROOT/'treasure_chest_open.mp4')],stdin=subprocess.PIPE)
    webm=subprocess.Popen(common+['-c:v','libvpx-vp9','-lossless','1','-pix_fmt','yuva420p','-auto-alt-ref','0',str(ROOT/'treasure_chest_open_alpha.webm')],stdin=subprocess.PIPE)
    background=Image.new('RGBA',(SIZE,SIZE))
    d=ImageDraw.Draw(background)
    for y in range(SIZE):
        d.line((0,y,SIZE,y),fill=(19+int(y/70),24+int(y/55),42+int(y/35),255))
    d.ellipse((75,315,450,404),outline='#35435c',width=2)
    previews=[]
    try:
        for frame in range(FRAMES):
            transparent=render(frame)
            opaque=Image.alpha_composite(background,transparent)
            mp4.stdin.write(opaque.tobytes())
            webm.stdin.write(transparent.tobytes())
            if frame in (0,30,60,100): previews.append(opaque.convert('RGB'))
    finally:
        mp4.stdin.close(); webm.stdin.close()
    if mp4.wait() or webm.wait(): raise RuntimeError('FFmpeg generation failed')
    sheet=Image.new('RGB',(SIZE*4,SIZE))
    for i,picture in enumerate(previews): sheet.paste(picture,(i*SIZE,0))
    sheet.save(ROOT/'treasure_chest_contact_sheet.jpg',quality=92)
    print(f'Generated {FRAMES} frames at {FPS} FPS, {SIZE}x{SIZE}: MP4 + alpha WebM')


if __name__=='__main__': main()
