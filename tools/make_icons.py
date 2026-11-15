import math, sys
from PIL import Image, ImageDraw
OUT = sys.argv[1]
S = 320  # draw 4x, downsample to 80
BG = (18, 18, 20, 255)
FG = (235, 235, 235, 255)
RED = (230, 57, 70, 255); GREEN = (46, 196, 112, 255); AMBER = (245, 166, 35, 255)
OFF_FG = (90, 90, 95, 255)

def tri(d, cx, cy, r, c, left=False):
    pts = [(cx - r*0.8, cy - r), (cx - r*0.8, cy + r), (cx + r, cy)]
    if left: pts = [(2*cx - x, y) for x, y in pts]
    d.polygon(pts, fill=c)

def magnifier(d, c, sign):
    d.ellipse((70, 60, 210, 200), outline=c, width=22)
    d.line((195, 185, 260, 250), fill=c, width=30)
    if sign in "+-": d.rectangle((105, 121, 175, 139), fill=c)
    if sign == "+": d.rectangle((131, 95, 149, 165), fill=c)
    if sign == "f":
        d.rectangle((105, 108, 175, 152), outline=c, width=12)

def draw(name, c, accent):
    d = ImageDraw.Draw(img := Image.new("RGBA", (S, S), BG))
    a = accent
    if name == "record": d.ellipse((90, 90, 230, 230), fill=a or RED)
    elif name == "record-new":
        d.ellipse((70, 100, 190, 220), fill=a or RED)
        d.rectangle((215, 80, 235, 160), fill=c); d.rectangle((185, 110, 265, 130), fill=c)
    elif name == "play":
        tri(d, 125, 160, 65, a or c); d.rectangle((200, 128, 255, 183), fill=a or c)
    elif name == "stop": d.rectangle((100, 100, 220, 220), fill=c)
    elif name == "pause": d.rectangle((100, 95, 145, 225), fill=a or c); d.rectangle((175, 95, 220, 225), fill=a or c)
    elif name == "skip-start": d.rectangle((85, 100, 110, 220), fill=c); tri(d, 175, 160, 60, c, left=True)
    elif name == "skip-end": tri(d, 145, 160, 60, c); d.rectangle((210, 100, 235, 220), fill=c)
    elif name == "label":
        d.polygon([(80,110),(200,110),(250,160),(200,210),(80,210)], fill=c)
        d.ellipse((100, 148, 124, 172), fill=BG)
    elif name == "undo":
        d.arc((90, 100, 250, 250), 180, 360+90, fill=c, width=24)
        d.polygon([(60,175),(120,175),(90,125)], fill=c)
    elif name == "save":
        d.rounded_rectangle((85, 85, 235, 235), 14, fill=c)
        d.rectangle((115, 85, 205, 140), fill=BG); d.rectangle((175, 95, 195, 130), fill=c)
        d.rectangle((110, 170, 210, 235), fill=BG)
    elif name == "zoom-in": magnifier(d, c, "+")
    elif name == "zoom-out": magnifier(d, c, "-")
    elif name == "zoom-fit": magnifier(d, c, "f")
    return img.resize((80, 80), Image.LANCZOS)

active = {"record": RED, "record-new": RED, "play": GREEN, "pause": AMBER}
for n in ["record","record-new","play","stop","pause","skip-start","skip-end","label","undo","save","zoom-in","zoom-out","zoom-fit"]:
    draw(n, FG, None).save(f"{OUT}/Images/{n}.png")
    draw(n, OFF_FG, OFF_FG).save(f"{OUT}/Images/{n}-off.png")
    if n in active:
        img = draw(n, FG, active[n])
        ring = ImageDraw.Draw(img); ring.rounded_rectangle((1, 1, 78, 78), 10, outline=active[n], width=4)
        img.save(f"{OUT}/Images/{n}-active.png")

# 256x256 plugin icon: waveform + record dot
big = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
d.rounded_rectangle((0, 0, 1023, 1023), 200, fill=(28, 36, 64, 255))
for i, x in enumerate(range(150, 700, 50)):
    h = 60 + 250 * abs(math.sin(i * 0.9)) * (0.4 + 0.6 * math.sin(i / 11 * math.pi))
    d.rounded_rectangle((x, 512 - h, x + 28, 512 + h), 14, fill=(120, 170, 255, 255))
d.ellipse((700, 400, 900, 600), fill=RED)
big.resize((256, 256), Image.LANCZOS).save(f"{OUT}/package/metadata/Icon256x256.png")
