"""Rebuild the original Flag Garden sample: standard library only, no external art. --check writes nothing.

Every picture is drawn here from simple shapes (circles, ellipses, rounded boxes, polygons) with
anti-aliased edges computed from signed distances, so the output is smooth at any pixel and the
same bytes come out on every machine. PNGs use stored (uncompressed) deflate blocks for that reason.
"""
import argparse
import hashlib
import json
from math import sqrt
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parent
ART = ("ground", "block", "round", "flag", "sky", "hills", "coin", "flower")
IDS = {name: f"2d{i:030x}" for i, name in enumerate(ART, 1)}
DEFINITION = "2d000000000000000000000000000100"
BASE_NODE = "2d000000000000000000000000000101"
FLAG_NODE = "2d000000000000000000000000000102"

# One small palette for the whole scene; every outline uses the same deep plum.
INK = (52, 36, 58)
WHITE = (255, 255, 255)
GOLD, GOLD_DARK, GOLD_LIGHT = (252, 196, 64), (214, 140, 42), (255, 232, 140)
WOOD, WOOD_DARK, WOOD_LIGHT = (214, 142, 78), (164, 96, 54), (240, 184, 112)
GRASS, GRASS_DARK, GRASS_LIGHT = (104, 186, 76), (66, 140, 66), (166, 222, 100)
DIRT, DIRT_DARK, PEBBLE = (172, 110, 72), (132, 80, 56), (204, 160, 116)
LILAC, LILAC_DARK, LILAC_LIGHT = (176, 132, 232), (128, 92, 196), (214, 186, 250)
CORAL, CORAL_DARK, CORAL_LIGHT = (240, 92, 86), (196, 58, 68), (255, 150, 128)
PINK = (255, 130, 170)
STAR = ((0, -1), (0.2351, -0.3236), (0.9511, -0.309), (0.3804, 0.1236), (0.5878, 0.809),
        (0, 0.4), (-0.5878, 0.809), (-0.3804, 0.1236), (-0.9511, -0.309), (-0.2351, -0.3236))


def noise(x, y, seed=0):
    """Deterministic integer hash in [0, 1)."""
    n = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65536


# --- shapes: (signed distance in pixels, bounding box) ----------------------------------------
def circle(cx, cy, r):
    return (lambda x, y: sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy))-r), (cx-r, cy-r, cx+r, cy+r)

def ellipse(cx, cy, rx, ry):
    m = min(rx, ry)
    return (lambda x, y: (sqrt(((x-cx)/rx)**2+((y-cy)/ry)**2)-1)*m), (cx-rx, cy-ry, cx+rx, cy+ry)

def box(x0, y0, x1, y1, r=0):
    cx, cy, hx, hy = (x0+x1)/2, (y0+y1)/2, (x1-x0)/2-r, (y1-y0)/2-r
    def d(x, y):
        qx, qy = abs(x-cx)-hx, abs(y-cy)-hy
        return sqrt(max(qx, 0)**2+max(qy, 0)**2)+min(max(qx, qy), 0)-r
    return d, (x0, y0, x1, y1)

def poly(points):
    n = len(points)
    def d(x, y):
        best, inside = 1e18, False
        for i in range(n):
            ax, ay = points[i]; bx, by = points[i-1]
            ex, ey, wx, wy = bx-ax, by-ay, x-ax, y-ay
            t = max(0, min(1, (wx*ex+wy*ey)/(ex*ex+ey*ey)))
            dx, dy = wx-ex*t, wy-ey*t
            best = min(best, dx*dx+dy*dy)
            if (ay > y) != (by > y) and x < ax+(y-ay)*ex/ey: inside = not inside
        return -sqrt(best) if inside else sqrt(best)
    xs, ys = [p[0] for p in points], [p[1] for p in points]
    return d, (min(xs), min(ys), max(xs), max(ys))

def star(cx, cy, r):
    return poly([(cx+px*r, cy+py*r) for px, py in STAR])

def grow(shape, k):
    d, (x0, y0, x1, y1) = shape
    return (lambda x, y: d(x, y)-k), (x0-k, y0-k, x1+k, y1+k)

def union(*shapes):
    fns = [s[0] for s in shapes]
    return (lambda x, y: min(f(x, y) for f in fns)), (min(s[1][0] for s in shapes), min(s[1][1] for s in shapes), max(s[1][2] for s in shapes), max(s[1][3] for s in shapes))

def cut(shape, *clips):
    """The part of shape inside every clip; the box stays the shape's."""
    fns = [shape[0]]+[c[0] for c in clips]
    return (lambda x, y: max(f(x, y) for f in fns)), shape[1]

def outside(shape):
    d, _ = shape
    return (lambda x, y: -d(x, y)), (-1e9, -1e9, 1e9, 1e9)


class Canvas:
    """Premultiplied RGBA in floats, drawn with the 'over' operator."""
    def __init__(self, width, height):
        self.w, self.h = width, height
        self.px = [0.0]*(width*height*4)

    def fill(self, shape, paint, alpha=1.0, soft=1.0):
        d, (x0, y0, x1, y1) = shape
        w, px, solid = self.w, self.px, not callable(paint)
        m = int(soft)+2
        for y in range(max(0, int(y0)-m), min(self.h, int(y1)+m+1)):
            for x in range(max(0, int(x0)-m), min(w, int(x1)+m+1)):
                cover = 0.5-d(x+0.5, y+0.5)/soft
                if cover <= 0: continue
                a = alpha*(1 if cover >= 1 else cover)
                r, g, b = paint if solid else paint(x, y)
                i = (y*w+x)*4; k = 1-a
                px[i] = r*a+px[i]*k; px[i+1] = g*a+px[i+1]*k; px[i+2] = b*a+px[i+2]*k; px[i+3] = a+px[i+3]*k

    def pad_top(self, rows):
        self.px = [0.0]*(self.w*rows*4)+self.px; self.h += rows
        return self

    def inked(self, shape, paint, width, ink=INK):
        self.fill(grow(shape, width), ink); self.fill(shape, paint)

    def png(self):
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)
            for x in range(self.w):
                i = (y*self.w+x)*4; a = self.px[i+3]
                if a <= 1/510: raw.extend((0, 0, 0, 0)); continue
                raw.extend(min(255, int(self.px[i+c]/a+0.5)) for c in range(3)); raw.append(min(255, int(a*255+0.5)))
        return encode_png(self.w, self.h, bytes(raw))


def encode_png(width, height, raw):
    def chunk(kind, data): return struct.pack(">I", len(data))+kind+data+struct.pack(">I", zlib.crc32(kind+data))
    stored = bytearray(b"\x78\x01")
    for start in range(0, len(raw), 65535):
        part = raw[start:start+65535]
        stored += bytes([start+65535 >= len(raw)])+struct.pack("<HH", len(part), len(part) ^ 65535)+part
    stored += struct.pack(">I", zlib.adler32(raw))
    return b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))+chunk(b"IDAT", bytes(stored))+chunk(b"IEND", b"")


def mix(a, b, t):
    return tuple(a[i]+(b[i]-a[i])*t for i in range(3))


# --- the pictures ------------------------------------------------------------------------------
def draw_sky():
    c = Canvas(448, 208)  # 14 x 6.5 m at 32 px/m
    c.fill(box(0, 0, 448, 208), lambda x, y: mix((92, 170, 232), (214, 240, 246), (y/207)**1.3))
    for r, a in ((40, 0.12), (31, 0.16), (24, 0.22)):  # a soft sun glow
        c.fill(circle(408, 40, r), (255, 250, 220), a, soft=6)
    c.fill(circle(408, 40, 16), (255, 244, 186))
    for cx, cy, s in ((70, 52, 1.0), (236, 34, 0.75), (330, 104, 0.6), (150, 118, 0.45)):
        puffs = [circle(cx+dx*s, cy+dy*s, r*s) for dx, dy, r in ((-30, 4, 16), (-8, -8, 22), (18, -2, 18), (36, 6, 12))]
        body = cut(union(*puffs, box(cx-44*s, cy, cx+46*s, cy+18*s, 9*s)), box(cx-60*s, cy-40*s, cx+60*s, cy+18*s))
        c.fill(grow(body, 2), (178, 214, 238))
        c.fill(body, WHITE)
        c.fill(cut(body, box(cx-60*s, cy+9*s, cx+60*s, cy+20*s)), (222, 236, 248))
    return c

def draw_hills():
    c = Canvas(448, 96)  # 14 x 3 m of hills at 32 px/m, padded to 4.5 m so the frame sits above the view

    far = union(*(ellipse(x, 96, rx, ry) for x, rx, ry in ((40, 120, 70), (230, 110, 52), (420, 120, 78))))
    c.inked(far, lambda x, y: mix((170, 220, 176), (140, 200, 156), y/96), 1.5, (110, 170, 130))
    near = union(*(ellipse(x, 104, rx, ry) for x, rx, ry in ((120, 96, 58), (330, 90, 46))))
    c.inked(near, lambda x, y: mix((128, 204, 116), (98, 180, 98), y/96), 2, (64, 132, 78))
    c.fill(cut(near, outside(grow(near, -6))), (156, 222, 132), 0.6)
    for x, y, r in ((92, 66, 7), (106, 70, 5), (150, 60, 6), (306, 76, 6), (350, 72, 8), (366, 78, 5)):
        c.inked(circle(x, y, r), (84, 164, 88), 1.5, (54, 116, 70))
        c.fill(circle(x-r*0.3, y-r*0.35, r*0.4), (130, 200, 116))
    return c.pad_top(48)

def draw_ground():
    c = Canvas(896, 144)  # 14 x 2.25 m at 64 px/m; the grass surface is 16 px from the top
    c.fill(box(0, 17, 896, 144), lambda x, y: mix(DIRT, DIRT_DARK, max(0, (y-40)/104)))
    for row, y in enumerate(range(62, 144, 26)):  # loose dirt strata
        for x in range(0, 896, 40):
            if noise(x, row, 1) < 0.55: c.fill(box(x+6, y, x+6+18+noise(x, row, 2)*30, y+3, 1.5), DIRT_DARK, 0.5)
    for i in range(44):  # pebbles
        x, y = noise(i, 0, 3)*880+8, 54+noise(i, 1, 3)*82
        r = 2.5+noise(i, 2, 3)*3.5
        p = ellipse(x, y, r*1.4, r)
        c.inked(p, PEBBLE if i % 3 else (186, 140, 104), 1.5, (110, 66, 50))
        c.fill(ellipse(x-r*0.4, y-r*0.35, r*0.5, r*0.3), (232, 200, 160))
    grass = union(box(-8, 16, 904, 34), *(circle(x+9, 33, 10) for x in range(-9, 900, 18)))
    c.inked(grass, GRASS, 3, (46, 100, 52))
    c.fill(cut(grass, box(-8, 16, 904, 23)), GRASS_LIGHT)
    for x in range(0, 896, 18):
        c.fill(circle(x+9, 36, 3.5), GRASS_DARK, 0.55)
    for i in range(24):  # tufts of blades poking up from the surface
        x = 20+i*37+noise(i, 0, 5)*20
        blades = [poly([(x+dx-4, 18), (x+dx+lean, 18-h), (x+dx+4, 18)]) for dx, lean, h in ((-6, -4, 9), (0, 1, 13), (6, 5, 8))]
        tuft = union(*blades)
        c.fill(grow(tuft, 2.5), (46, 100, 52))
        c.fill(tuft, GRASS_LIGHT)
        c.fill(box(x-14, 15, x+14, 22), GRASS_LIGHT)
    return c

def draw_block():
    c = Canvas(128, 128)  # 1 m at 128 px/m: a wooden crate
    outer = box(5, 5, 123, 123, 12)
    c.inked(outer, WOOD, 5)
    for y in (42, 80):  # plank seams
        c.fill(box(10, y, 118, y+3), WOOD_DARK, 0.7)
    for i in range(14):  # grain
        x, y = 16+noise(i, 0, 7)*90, 12+noise(i, 1, 7)*104
        c.fill(box(x, y, x+10+noise(i, 2, 7)*18, y+2.2, 1.1), WOOD_DARK, 0.45)
    frame = cut(outer, outside(box(23, 23, 105, 105, 4)))
    c.fill(frame, WOOD_LIGHT)
    c.fill(cut(frame, box(0, 104, 128, 128)), WOOD_DARK, 0.55)
    c.fill(cut(frame, box(104, 0, 128, 128)), WOOD_DARK, 0.35)
    c.fill(cut(outside(box(23, 23, 105, 105, 4)), grow(box(23, 23, 105, 105, 4), 2.5)), INK, 0.85)
    brace = cut(poly([(23, 92), (92, 23), (105, 36), (36, 105)]), box(23, 23, 105, 105))
    c.fill(grow(brace, 2.5), INK, 0.85)
    c.fill(brace, WOOD_LIGHT)
    c.fill(cut(brace, poly([(23, 105), (105, 23), (105, 105)])), WOOD, 0.8)
    for x, y in ((15, 15), (113, 15), (15, 113), (113, 113)):  # nails
        c.inked(circle(x, y, 3.5), (226, 216, 200), 1.5)
    c.fill(cut(outer, box(10, 9, 118, 13, 2)), WHITE, 0.4)
    return c

def draw_round():
    c = Canvas(128, 128)  # 1 m at 128 px/m: the round friend, a lilac sprout creature
    c.fill(ellipse(64, 121, 40, 6), (40, 30, 50), 0.22)
    for x in (46, 80):
        c.inked(ellipse(x, 116, 13, 8), LILAC_DARK, 4)
    stem = box(61, 18, 67, 40, 3)
    c.inked(stem, GRASS, 3.5)
    left = cut(circle(64, 32, 14), circle(46, 14, 14))
    right = cut(circle(68, 12, 14), circle(86, 30, 14))
    for leaf in (left, right):
        c.inked(leaf, GRASS_LIGHT, 3.5)
    body = ellipse(64, 76, 46, 40)
    c.inked(body, LILAC, 5)
    c.fill(cut(body, outside(circle(52, 64, 50))), LILAC_DARK)
    c.fill(cut(ellipse(66, 96, 26, 16), body), LILAC_LIGHT, 0.7)
    c.fill(ellipse(40, 54, 11, 7), WHITE, 0.65)
    for x in (62, 88):
        eye = ellipse(x, 70, 10, 13)
        c.inked(eye, WHITE, 3)
        c.fill(cut(circle(x+3, 73, 7), eye), INK)
        c.fill(circle(x+1, 69, 2.6), WHITE)
        c.fill(circle(x+5.5, 77, 1.3), WHITE)
    for x in (48, 102):
        c.fill(ellipse(x, 88, 8, 5), PINK, 0.65)
    smile = cut(grow(circle(76, 84, 6), 1.8), outside(grow(circle(76, 84, 6), -1.8)), box(60, 86, 92, 100))
    c.fill(smile, INK)
    return c

def draw_flag():
    c = Canvas(192, 256)  # 1.5 x 2 m at 128 px/m; the pole stands at a quarter of the width
    pennant = poly([(52, 30), (176, 46), (146, 76), (176, 106), (52, 116)])
    c.inked(pennant, CORAL, 4.5)
    c.fill(cut(pennant, box(52, 26, 192, 52)), CORAL_LIGHT)
    c.fill(cut(pennant, poly([(52, 100), (192, 100), (192, 120), (52, 120)])), CORAL_DARK)
    c.inked(star(98, 72, 17), (255, 236, 160), 3)
    c.fill(box(52, 34, 58, 112), CORAL_DARK, 0.5)
    pole = box(42, 22, 54, 248, 4)
    c.inked(pole, (238, 222, 190), 4)
    c.fill(cut(pole, box(49, 0, 60, 256)), (196, 172, 140))
    c.inked(box(32, 240, 64, 256, 3), WOOD_DARK, 4)
    cap = circle(48, 18, 12)
    c.inked(cap, GOLD, 4)
    c.fill(cut(cap, outside(circle(44, 14, 12))), GOLD_DARK)
    c.fill(circle(44, 13, 4), WHITE, 0.8)
    return c

def draw_coin():
    c = Canvas(64, 64)  # 0.5 m at 128 px/m
    coin = ellipse(32, 32, 24, 28)
    c.inked(coin, GOLD, 3.5)
    c.fill(cut(coin, outside(circle(28, 28, 26))), GOLD_DARK)
    c.fill(cut(grow(ellipse(32, 32, 16, 20), 1.5), outside(grow(ellipse(32, 32, 16, 20), -1.5))), GOLD_DARK, 0.8)
    c.fill(star(32, 33, 10), GOLD_LIGHT)
    c.fill(ellipse(22, 18, 5, 7), WHITE, 0.75)
    return c

def draw_flower():
    c = Canvas(64, 64)  # 0.5 m at 128 px/m
    c.inked(box(30, 30, 34, 64, 2), GRASS, 2.5)
    c.inked(cut(circle(28, 54, 10), circle(42, 44, 10)), GRASS_LIGHT, 2.5)
    petals = union(*(circle(32+px*12, 24+py*12, 8.5) for px, py in STAR[::2]))
    c.inked(petals, PINK, 3)
    c.fill(cut(petals, outside(circle(32, 24, 14))), (255, 186, 210), 0.7)
    c.inked(circle(32, 24, 7), GOLD, 2.5)
    c.fill(circle(30, 22, 2.2), WHITE, 0.8)
    return c

# name: (drawing, pixels per metre, filter)
PICTURES = {"ground": (draw_ground, 64), "block": (draw_block, 128), "round": (draw_round, 128), "flag": (draw_flag, 128),
            "sky": (draw_sky, 32), "hills": (draw_hills, 32), "coin": (draw_coin, 128), "flower": (draw_flower, 128)}


def sprite(identity, art, position, size, order, pivot=(0.5, 0), **extra):
    return dict(id=identity, asset=IDS[art], position=position, sprite=dict(size=size, pivot=list(pivot), order=order, filter="linear"), **extra)

def outputs():
    result = {}
    def put(path, value): result[path] = (json.dumps(value, indent=2)+"\n").encode()
    for name, identity in IDS.items():
        draw, density = PICTURES[name]
        png = draw().png(); result[f"assets/{name}.png"] = png
        put(f"assets/{name}.png.asset.json", dict(kind="asset", version=1, guid=identity, source=name+".png", sourceHash=hashlib.sha256(png).hexdigest(), importer=dict(id="png", version=1), settings=dict(spriteDefaults=dict(version=1, pixelsPerMetre=density, pivot=[0.25 if name == "flag" else 0.5, 0], filter="linear")), provenance=dict(author="BED sample contributors", licence="self-authored", origin="samples/SpriteLevelProject/generate_assets.py", declared="ours"), aiUse="allowed"))
    block = sprite("unused", "block", [0, 0, 0], [1, 1], 0); block.pop("id"); block.update(key=BASE_NODE, label="Block")
    flag = sprite("unused", "flag", [0, 1, 0], [1.5, 2], 10, pivot=(0.25, 0)); flag.pop("id"); flag.update(key=FLAG_NODE, label="Flag", parentKey=BASE_NODE)
    definition = dict(kind="prefab-definition", version=1, guid=DEFINITION, space="2d-xy", root=BASE_NODE, nodes=[block, flag], slots=[])
    put("data/flag-post.json", definition)
    revision = "sha256:"+hashlib.sha256(json.dumps(definition, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()
    instances = []
    for n, x in ((1, -3), (2, 5)):
        instances.append(dict(key=f"2d{512+n:030x}", source=DEFINITION, base="flag_post", placement=dict(id=f"post_{n}", position=[x, -1, 0]), nodeIds={BASE_NODE: f"post_{n}_block", FLAG_NODE: f"post_{n}_flag"}, bindings={}, overrides=[] if n == 1 else [dict(node=FLAG_NODE, field="/sprite/size", op="set", value=[1.875, 2.5])]))
    ordinary = [
        sprite("sky", "sky", [0, -3.25, 0], [14, 6.5], -20), sprite("hills", "hills", [0, -1.25, 0], [14, 4.5], -10),
        sprite("ground", "ground", [0, -3, 0], [14, 2.25], 0),
        sprite("step_left", "block", [0, -1, 0], [1, 1], 0), sprite("step_mid", "block", [1, -1, 0], [1, 1], 0),
        sprite("step_right", "block", [1, 0, 0], [1, 1], 0), sprite("ledge", "block", [3, 1, 0], [1, 1], 0),
        sprite("round_friend", "round", [-5, -1, 0], [1, 1], 10),
        sprite("coin_1", "coin", [0, 0.25, 0], [0.5, 0.5], 10), sprite("coin_2", "coin", [1, 1.25, 0], [0.5, 0.5], 10),
        sprite("coin_3", "coin", [3, 2.25, 0], [0.5, 0.5], 10),
        sprite("flower_left", "flower", [-6, -1, 0], [0.5, 0.5], 10), sprite("flower_right", "flower", [-1.5, -1, 0], [0.5, 0.5], 10),
    ]
    put("data/level.json", dict(kind="scene", version=2, space="2d-xy", objects=ordinary, bases=[dict(id="flag_post", revision=revision, definition=definition)], instances=instances))
    put("reference_map.json", dict(game="SceneStarter", labels=[], references=[]))
    put(".bed/project.json", dict(sceneBuild=dict(version=1, sceneVersion=2, source="data/level.json", generated=".bed/generated/scenes/level.json", spaces=["2d-xy"])))
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("--check", action="store_true"); args = parser.parse_args()
    files = outputs()
    if args.check:
        wrong = [name for name, data in files.items() if not (ROOT/name).is_file() or (ROOT/name).read_bytes() != data]
        if wrong: print("Generated files differ: "+", ".join(wrong)); return 1
        print(f"All {len(files)} original sample files match."); return 0
    for name, data in files.items():
        path = ROOT/name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
    print(f"Wrote {len(files)} original sample files."); return 0

if __name__ == "__main__": raise SystemExit(main())
