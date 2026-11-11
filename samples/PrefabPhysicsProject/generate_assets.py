"""Reproduce the prefab-physics sample: a small self-authored cartoon diorama (a grassy island, wooden crates,
a striped paddle, a picket fence, round trees and a beach ball), its textures and sidecars, the crate prefab,
the physics level and the project's scene settings.

Only the named outputs below are written. Run --check to compare without writes.
No importer, application, subprocess, network or external data is used; standard library only.

How the look is made. Every model is bevelled, so edges catch the light. BED's 3D view shades faces by one fixed
light when it draws on the CPU, and draws unlit colour times texture on the GPU. So each material carries its colour
as Kd and a texture whose tiles hold the same light level baked in, one tile per face direction that uses it. Both
views then show the same shading, and the GPU view adds the painted detail: planks, grass, pebbles.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ORIGIN = "samples/PrefabPhysicsProject/generate_assets.py"

# Stable identities: 32 lowercase hexadecimal characters each.
CRATE_MODEL = "ba5e0000000000000000000000000c01"
FLOOR_MODEL = "ba5e0000000000000000000000000f01"
PADDLE_MODEL = "ba5e0000000000000000000000000d01"
TREE_MODEL = "ba5e0000000000000000000000000e01"
FENCE_MODEL = "ba5e0000000000000000000000000e02"
BALL_MODEL = "ba5e0000000000000000000000000e03"
TEXTURES = {"planks": "ba5e0000000000000000000000000a01", "frame": "ba5e0000000000000000000000000a02",
            "grass": "ba5e0000000000000000000000000a03", "soil": "ba5e0000000000000000000000000a04",
            "paint": "ba5e0000000000000000000000000a05"}
CRATE_PREFAB = "ba5e0000000000000000000000000ab1"
CRATE_ROOT = "ba5e00000000000000000000000000b0"
INSTANCE_KEYS = ("ba5e000000000000000000000000001a", "ba5e000000000000000000000000001b", "ba5e000000000000000000000000001c")

LIGHT = (-0.4, 1.0, -0.6)   # The direction BED's CPU view lights faces from; baked into the textures for the GPU view.
TILE = 64                   # Texture pixels per tile (one metre on the crate and the grass).


def srgb(r: int, g: int, b: int) -> tuple:
    """A display colour as the linear Kd BED expects."""
    def lin(c):
        c /= 255
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return tuple(round(lin(c), 4) for c in (r, g, b))


# material: (Kd, texture). Each texture holds a pattern; "paint" is plain, for flat-coloured parts.
MATERIALS = {
    "crate_planks": (srgb(236, 176, 102), "planks"), "crate_frame": (srgb(156, 92, 46), "frame"),
    "grass_light": (srgb(132, 206, 84), "grass"), "grass_dark": (srgb(112, 188, 70), "grass"),
    "grass_rim": (srgb(98, 170, 60), "paint"), "soil": (srgb(160, 108, 66), "soil"),
    "paddle_red": (srgb(232, 66, 62), "paint"), "paddle_white": (srgb(250, 246, 236), "paint"),
    "trunk": (srgb(136, 88, 54), "frame"), "leaves": (srgb(70, 160, 82), "paint"),
    "leaves_light": (srgb(108, 190, 90), "paint"), "fence_paint": (srgb(246, 238, 222), "paint"),
    "ball_red": (srgb(236, 78, 70), "paint"), "ball_yellow": (srgb(252, 206, 72), "paint"),
    "ball_blue": (srgb(72, 142, 232), "paint"), "ball_white": (srgb(250, 248, 242), "paint"),
}


def number(value):
    """Integral values as integers, like the editor's writer; others as shortest decimal."""
    return int(value) if float(value).is_integer() else value


# --- Geometry -------------------------------------------------------------------------------------------------------
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def unit(a):
    n = math.sqrt(dot(a, a))
    return (a[0] / n, a[1] / n, a[2] / n)


def normal(poly):
    """Newell's normal, outward for counter-clockwise polygons seen from outside."""
    n = [0.0, 0.0, 0.0]
    for i, a in enumerate(poly):
        b = poly[(i + 1) % len(poly)]
        n[0] += (a[1] - b[1]) * (a[2] + b[2])
        n[1] += (a[2] - b[2]) * (a[0] + b[0])
        n[2] += (a[0] - b[0]) * (a[1] + b[1])
    return unit(n)


def outward(poly, centre):
    """The polygon wound counter-clockwise as seen from outside a convex part around centre."""
    middle = tuple(sum(p[i] for p in poly) / len(poly) for i in range(3))
    # Reversed from the same first corner, so a quad keeps its diagonal (BED fans faces from the first corner).
    return poly if dot(normal(poly), sub(middle, centre)) >= 0 else [poly[0]] + poly[:0:-1]


def level_of(n) -> float:
    """BED's CPU light for a face normal: 0.35 ambient plus 0.65 of the fixed light."""
    return round(round((0.35 + 0.65 * max(0.0, dot(n, unit(LIGHT)))) * 40) / 40, 3)   # steps of 1/40 keep textures small


def bevel_box(centre, half, bevel, material, edge, splits=None):
    """A box with bevelled edges and corners. Each main face can be cut into a grid; material(axis, sign, i, j) paints
    each cell and edge paints the bevels. splits(axis) gives the cut positions along the face's two other axes."""
    faces = []
    inner = [h - bevel for h in half]
    def point(axis_values):
        return tuple(centre[i] + axis_values[i] for i in range(3))
    for axis in range(3):
        a, b = [i for i in range(3) if i != axis]
        cuts = splits(axis) if splits else ([], [])
        ua = [-inner[a]] + [c for c in cuts[0]] + [inner[a]]
        ub = [-inner[b]] + [c for c in cuts[1]] + [inner[b]]
        for sign in (-1, 1):
            for i in range(len(ua) - 1):
                for j in range(len(ub) - 1):
                    quad = []
                    for (x, y) in ((ua[i], ub[j]), (ua[i + 1], ub[j]), (ua[i + 1], ub[j + 1]), (ua[i], ub[j + 1])):
                        v = [0.0, 0.0, 0.0]
                        v[axis], v[a], v[b] = sign * half[axis], x, y
                        quad.append(point(v))
                    faces.append((outward(quad, centre), material(axis, sign, i, j)))
    # Bevel strips along each edge and a triangle at each corner.
    def corner_point(signs, pulled):
        v = [signs[i] * (half[i] if i == pulled else inner[i]) for i in range(3)]
        return point(v)
    for axis in range(3):          # strips running along this axis
        p, q = [i for i in range(3) if i != axis]
        pieces = max(1, math.ceil(2 * inner[axis] - 0.01))
        for sp in (-1, 1):
            for sq in (-1, 1):
                ends = []
                for s in (-1, 1):
                    signs = [0, 0, 0]
                    signs[axis], signs[p], signs[q] = s, sp, sq
                    ends.append((corner_point(signs, p), corner_point(signs, q)))
                def along(t, k):
                    return tuple(ends[0][k][i] + (ends[1][k][i] - ends[0][k][i]) * t for i in range(3))
                for piece in range(pieces):
                    t0, t1 = piece / pieces, (piece + 1) / pieces
                    quad = [along(t0, 0), along(t0, 1), along(t1, 1), along(t1, 0)]
                    faces.append((outward(quad, centre), edge))
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                tri = [corner_point((sx, sy, sz), k) for k in range(3)]
                faces.append((outward(tri, centre), edge))
    return faces


def sphere(centre, radius, segments, rings, material, squash=1.0):
    """A faceted sphere; material(segment, ring) paints each facet."""
    def point(seg, ring):
        theta = math.pi * ring / rings
        phi = 2 * math.pi * seg / segments
        return (centre[0] + radius * math.sin(theta) * math.cos(phi), centre[1] + radius * squash * math.cos(theta),
                centre[2] + radius * math.sin(theta) * math.sin(phi))
    faces = []
    for ring in range(rings):
        for seg in range(segments):
            a, b = point(seg, ring), point(seg + 1, ring)
            c, d = point(seg + 1, ring + 1), point(seg, ring + 1)
            poly = [a, d, c] if ring == 0 else [a, b, c] if ring == rings - 1 else [a, b, c, d]
            faces.append((outward(poly, centre), material(seg, ring)))
    return faces


def prism(centre, radius, bottom, top, sides, material, cap):
    """An upright prism (trunk); the top and bottom caps use cap."""
    ring = [(centre[0] + radius * math.cos(2 * math.pi * k / sides), centre[2] + radius * math.sin(2 * math.pi * k / sides)) for k in range(sides)]
    middle = (centre[0], (bottom + top) / 2, centre[2])
    faces = []
    for k in range(sides):
        (x0, z0), (x1, z1) = ring[k], ring[(k + 1) % sides]
        faces.append((outward([(x0, bottom, z0), (x1, bottom, z1), (x1, top, z1), (x0, top, z0)], middle), material))
    faces.append((outward([(x, top, z) for x, z in ring], middle), cap))
    faces.append((outward([(x, bottom, z) for x, z in ring], middle), cap))
    return faces


def picket(x, width, height, depth, z):
    """One fence picket with a pointed top."""
    w, d = width / 2, depth / 2
    outline = [(x - w, 0), (x + w, 0), (x + w, height - w), (x, height), (x - w, height - w)]
    centre = (x, height / 2, z)
    front = [(px, py, z - d) for px, py in outline]
    back = [(px, py, z + d) for px, py in outline]
    faces = [(outward(front, centre), "fence_paint"), (outward(back, centre), "fence_paint")]
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        faces.append((outward([front[i], front[j], back[j], back[i]], centre), "fence_paint"))
    return faces


def crate_faces():
    # One painted panel per side (frame, planks and a brace in the texture) and bevels in darker frame wood.
    # The model is 1.08 m, a little larger than the crate's 1 m body: BED draws each prefab copy's placement as a
    # 1 m grey cube under its parts, so the panels sit just outside that cube, split along the same diagonal.
    return bevel_box((0, 0, 0), (0.54, 0.54, 0.54), 0.05, lambda *_: "crate_planks", "crate_frame")


def floor_faces():
    # An 8 m island: a checked lawn on top, a grass rim, then soil. The top is at +0.25 (the body's top face).
    half, bevel = (4, 0.6, 4), 0.12
    centre = (0, 0.25 - 0.6, 0)
    inner = 4 - bevel
    metres = [float(k) for k in range(-3, 4)]
    def splits(axis):
        if axis == 1:
            return metres, metres
        rim = 0.6 - bevel - 0.18
        return (metres, [rim]) if axis == 2 else ([rim], metres)
    def paint(axis, sign, i, j):
        if axis == 1:
            return ("grass_light" if (i + j) % 2 == 0 else "grass_dark") if sign > 0 else "soil"
        band = j if axis == 2 else i
        return "grass_rim" if band == 1 else "soil"
    faces = bevel_box(centre, half, bevel, paint, "soil", splits)
    # Bevels on the top edges are grass; the rest soil.
    top = 0.25 - 0.001
    return [(poly, "grass_rim" if mat == "soil" and min(p[1] for p in poly) > top - bevel - 0.01 and len(poly) >= 3
             and max(p[1] for p in poly) > top else mat) for poly, mat in faces]


def paddle_faces():
    # A red bumper with two white stripes and white bevels.
    stripes = [-0.62, -0.46, 0.46, 0.62]
    def paint(axis, sign, i, j):
        if axis == 0:
            return "paddle_red"
        return "paddle_white" if i in (1, 3) else "paddle_red"
    return bevel_box((0, 0, 0), (1, 0.125, 0.25), 0.05, paint, "paddle_white",
                     lambda axis: ((stripes, []) if axis != 0 else ([], [])))


def tree_faces():
    # A round cartoon tree: a short trunk and a faceted crown with a lighter cap.
    faces = prism((0, 0, 0), 0.13, 0, 0.75, 7, "trunk", "trunk")
    faces += sphere((0, 1.25, 0), 0.62, 9, 6, lambda seg, ring: "leaves_light" if ring < 2 else "leaves", squash=0.95)
    return faces


def fence_faces():
    # A white picket fence 7.2 m long, along X.
    faces = []
    for k in range(13):
        faces += picket(-3.6 + 0.6 * k, 0.14, 0.62, 0.05, 0)
    for y in (0.16, 0.42):
        faces += bevel_box((0, y, 0.06), (3.65, 0.04, 0.025), 0.012, lambda *_: "fence_paint", "fence_paint")
    return faces


def ball_faces():
    colours = ("ball_red", "ball_white", "ball_yellow", "ball_white", "ball_blue", "ball_white")
    return sphere((0, 0, 0), 0.3, 12, 8, lambda seg, ring: "ball_white" if ring in (0, 7) else colours[(seg // 2) % 6])


MODELS = {   # name: (guid, faces)
    "crate": (CRATE_MODEL, crate_faces), "floor": (FLOOR_MODEL, floor_faces), "paddle": (PADDLE_MODEL, paddle_faces),
    "tree": (TREE_MODEL, tree_faces), "fence": (FENCE_MODEL, fence_faces), "ball": (BALL_MODEL, ball_faces),
}


# --- Textures -------------------------------------------------------------------------------------------------------
def noise(x: int, y: int, seed: int) -> float:
    """A repeatable value in [0, 1) from integer coordinates."""
    h = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65536


def pattern(kind: str, x: int, y: int) -> float:
    """Brightness of the painted detail at pixel (x, y) of a tile, y down; 1 is the plain material colour."""
    t = TILE
    if kind == "planks":
        plank = t // 3
        row = y % plank
        if row == 0 and y > 0:
            return 0.5
        value = 0.94 + 0.06 * math.sin(x * 0.35 + (y // plank) * 2.1 + math.sin(y * 0.9) * 0.6) - 0.05 * noise(x // 3, y, 1)
        if row == 1:
            value = 1.0
        if row == plank - 1:
            value *= 0.86
        if min(x, y, t - 1 - x, t - 1 - y) < 9:   # the frame around the panel
            edge = min(x, y, t - 1 - x, t - 1 - y)
            return (0.42 if edge == 0 else 0.7 if edge < 8 else 0.5) - 0.05 * noise(x // 3, y // 3, 7)
        d = abs((x - (t - 1 - y)))          # the diagonal brace, lower left to upper right
        if d <= 6:
            value = 0.92 + 0.05 * math.sin(x * 0.5) if d < 6 else 0.6
        for nx, ny in ((4, 4), (t - 5, 4), (4, t - 5), (t - 5, t - 5)):
            if abs(x - nx) <= 1 and abs(y - ny) <= 1:
                value = 0.45
        return value
    if kind == "frame":
        return 0.9 + 0.08 * math.sin(y * 0.8 + math.sin(x * 0.2) * 1.5) - 0.06 * noise(x // 4, y, 2)
    if kind == "grass":
        value = 0.93 + 0.06 * noise(x // 2, y // 2, 3)
        if noise(x, y, 4) > 0.965 and y > 1:
            value = 1.0
        if noise(x, y - 1, 4) > 0.965:
            value = 0.8
        return value
    if kind == "soil":
        value = 0.9 + 0.07 * noise(x // 2, y // 2, 5)
        for k in range(7):
            cx, cy = int(noise(k, 0, 6) * t), int(noise(k, 1, 6) * t)
            r = 2.5 + 2.5 * noise(k, 2, 6)
            d = math.hypot(x - cx, (y - cy) * 1.4)
            if d < r:
                value = 1.0 if d < r - 1 else 0.7
        return value
    return 1.0


def decode(c: float) -> float:
    """BED multiplies Kd by the texture in linear light, so a display brightness is stored as its linear value."""
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def png(width: int, height: int, pixel) -> bytes:
    """An RGBA PNG written with stored DEFLATE blocks, so the bytes never depend on a compressor version."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        for x in range(width):
            v = max(0, min(255, round(255 * pixel(x, y))))
            raw.extend((v, v, v, 255))
    blocks = bytearray(b"\x78\x01")
    for start in range(0, len(raw), 65535):
        part = raw[start:start + 65535]
        final = 1 if start + 65535 >= len(raw) else 0
        blocks += bytes([final]) + struct.pack("<HH", len(part), len(part) ^ 0xFFFF) + part
    blocks += struct.pack(">I", zlib.adler32(bytes(raw)))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", bytes(blocks)) + chunk(b"IEND", b""))


def texture(kind: str, levels: list[float]) -> bytes:
    """One tile per light level, side by side; plain paint uses small tiles."""
    size = TILE if kind != "paint" else 4
    return png(size * len(levels), size, lambda x, y: decode(levels[x // size] * pattern(kind, x % size, y)))


# --- Model files ----------------------------------------------------------------------------------------------------
def tangents(n):
    """Two axes across a face: across and up for walls, X and Z for floors and roofs."""
    if abs(n[1]) > 0.7:
        return (1.0, 0.0, 0.0), (0.0, 0.0, -1.0 if n[1] > 0 else 1.0)
    across = unit((n[2], 0.0, -n[0]))
    return across, unit((-n[0] * n[1], 1.0 - n[1] * n[1], -n[2] * n[1]))   # world up, flattened onto the face


def all_levels() -> dict[str, list[float]]:
    """The light levels each texture needs, from every face of every model."""
    used: dict[str, set] = {}
    for _, (_, faces) in MODELS.items():
        for poly, mat in faces():
            used.setdefault(MATERIALS[mat][1], set()).add(level_of(normal(poly)))
    return {kind: sorted(levels) for kind, levels in used.items()}


def model(name: str, faces, levels: dict[str, list[float]]) -> bytes:
    fmt = lambda v: format(round(v, 5) + 0.0, ".6g")
    positions: dict[tuple, int] = {}
    coords: dict[tuple, int] = {}
    groups: dict[str, list[str]] = {}
    for poly, mat in faces:
        kind = MATERIALS[mat][1]
        n = normal(poly)
        tiles = levels[kind]
        k = tiles.index(level_of(n))
        size = TILE if kind != "paint" else 4
        inset = 1.5 / size
        if kind == "paint":
            uvs = [(0.5, 0.5)] * len(poly)
        else:
            across, up = tangents(n)
            us = [dot(p, across) for p in poly]
            vs = [dot(p, up) for p in poly]
            u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
            uvs = [((u - u0) / max(u1 - u0, 1e-6), (v - v0) / max(v1 - v0, 1e-6)) for u, v in zip(us, vs)]
        refs = []
        for p, (u, v) in zip(poly, uvs):
            key = tuple(fmt(c) for c in p)
            positions.setdefault(key, len(positions) + 1)
            uv = (fmt((k + inset + u * (1 - 2 * inset)) / len(tiles)), fmt(inset + v * (1 - 2 * inset)))
            coords.setdefault(uv, len(coords) + 1)
            refs.append(f"{positions[key]}/{coords[uv]}")
        groups.setdefault(mat, []).append("f " + " ".join(refs))
    lines = ["# Self-authored BED sample; generated by generate_assets.py", "mtllib " + name + ".mtl", "o " + name]
    lines += ["v " + " ".join(key) for key in positions]
    lines += ["vt " + " ".join(key) for key in coords]
    for mat in sorted(groups):
        lines += ["usemtl " + mat] + groups[mat]
    return ("\n".join(lines) + "\n").encode("ascii")


def material_library(names) -> bytes:
    lines = ["# Self-authored materials"]
    for mat in sorted(names):
        colour, kind = MATERIALS[mat]
        lines += ["newmtl " + mat, "Kd " + " ".join(format(c, ".9g") for c in colour), "map_Kd " + kind + ".png"]
    return ("\n".join(lines) + "\n").encode("ascii")


# --- Documents ------------------------------------------------------------------------------------------------------
def text(value) -> bytes:
    """Indented JSON with LF endings, matching the editor's saved layout."""
    return (json.dumps(value, indent=2, separators=(",", ": "), ensure_ascii=False) + "\n").encode("utf-8")


def canonical(value) -> bytes:
    """RFC 8785 JSON canonical form for the simple values used here (ASCII keys, integers and short decimals)."""
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def sort_keys(value):
    if isinstance(value, dict):
        return {key: sort_keys(value[key]) for key in sorted(value)}
    if isinstance(value, list):
        return [sort_keys(item) for item in value]
    return value


def body(motion: str, half: tuple, friction: float, mass: float | None = None) -> dict:
    result = {"motion": motion, "shape": {"kind": "box", "halfExtents": [number(v) for v in half], "center": [0, 0, 0]},
              "friction": number(friction)}
    if mass is not None:
        result["massKg"] = number(mass)
    return result


def crate_definition() -> dict:
    node = {"key": CRATE_ROOT, "label": "Crate", "asset": CRATE_MODEL,
            "properties": {}, "references": {},
            "body": body("dynamic", (0.5, 0.5, 0.5), 0.6, 1)}
    return sort_keys({"kind": "prefab-definition", "version": 1, "guid": CRATE_PREFAB, "space": "3d",
                      "root": CRATE_ROOT, "nodes": [node], "slots": []})


def transform(object_id: str, position: tuple, turn: float = 0, scale: float = 1) -> dict:
    """turn is a rotation about Y in degrees."""
    half = math.radians(turn) / 2
    rotation = [0, number(round(math.sin(half), 6)), 0, number(round(math.cos(half), 6))]
    return {"id": object_id, "position": [number(v) for v in position], "rotation": rotation, "scale": [number(scale)] * 3}


def scenery(object_id: str, asset: str, position: tuple, turn: float = 0, scale: float = 1) -> dict:
    """A decorative object: a model with no body, so the physics game ignores it."""
    return transform(object_id, position, turn, scale) | {"asset": asset, "properties": {}, "references": {}}


def level() -> dict:
    definition = crate_definition()
    revision = "sha256:" + hashlib.sha256(canonical(definition)).hexdigest()
    floor = transform("floor", (0, -0.25, 0)) | {"asset": FLOOR_MODEL, "properties": {}, "references": {},
                                                 "body": body("static", (4, 0.25, 4), 0.8)}
    paddle = transform("paddle", (2.5, 0.75, 0)) | {"asset": PADDLE_MODEL, "properties": {}, "references": {},
                                                    "body": body("kinematic", (1, 0.125, 0.25), 0.5)}
    decorations = [scenery("fence", FENCE_MODEL, (0, 0, 3.55)),
                   scenery("tree", TREE_MODEL, (-2.7, 0, 2.4), 20),
                   scenery("tree_small", TREE_MODEL, (3.0, 0, -2.7), 70, 0.7),
                   scenery("ball", BALL_MODEL, (-2.6, 0.3, -1.1), 30)]
    # A small stack, each crate dropped a little above the one below. The top crate is heavier.
    stack = [("crate_1", (0, 0.6, 0), []),
             ("crate_2", (0.15, 1.8, 0), []),
             ("crate_3", (0.3, 3, 0), [{"node": CRATE_ROOT, "field": "/body/massKg", "op": "set", "value": 5}])]
    instances = [{"key": key, "source": CRATE_PREFAB, "base": "crate_v1", "placement": transform(name, position),
                  "nodeIds": {CRATE_ROOT: name + "_box"}, "bindings": {}, "overrides": overrides}
                 for key, (name, position, overrides) in zip(INSTANCE_KEYS, stack)]
    return {"kind": "scene", "version": 2, "space": "3d", "objects": [floor, paddle] + decorations,
            "physics": {"gravity": [0, -9.8, 0], "fixedStepSeconds": 0.01},
            "bases": [{"id": "crate_v1", "revision": revision, "definition": definition}],
            "instances": instances}


def project_settings() -> dict:
    # The same document Scene2BuildSettings.WriteProject("{}", ...) produces with SupportsPhysics = true.
    return {"sceneBuild": {"version": 1, "sceneVersion": 2, "source": "data/level.json",
                           "generated": ".bed/generated/scenes/level.json", "spaces": ["3d"], "physics": True}}


def record(guid: str, source: str, data: bytes, importer: str) -> dict:
    return {"kind": "asset", "version": 1, "guid": guid, "source": source,
            "sourceHash": hashlib.sha256(data).hexdigest(), "importer": {"id": importer, "version": 1},
            "settings": {"scale": 1, "upAxis": "Y"} if importer == "obj" else {},
            "provenance": {"author": "BED sample contributors", "licence": "self-authored",
                           "origin": ORIGIN, "declared": "ours"},
            "aiUse": "allowed"}


def outputs() -> dict[str, bytes]:
    result: dict[str, bytes] = {}
    levels = all_levels()
    for name, (guid, faces) in MODELS.items():
        source = name + ".obj"
        built = faces()
        data = model(name, built, levels)
        result["assets/" + source] = data
        result["assets/" + name + ".mtl"] = material_library({mat for _, mat in built})
        result["assets/" + source + ".asset.json"] = text(record(guid, source, data, "obj"))
    for kind, guid in TEXTURES.items():
        data = texture(kind, levels[kind])
        result["assets/" + kind + ".png"] = data
        result["assets/" + kind + ".png.asset.json"] = text(record(guid, kind + ".png", data, "png"))
    result["data/crate.json"] = text(crate_definition())
    result["data/level.json"] = text(level())
    result["schemas/crate.schema.json"] = text({"type": "object", "required": ["kind", "version", "guid", "root", "nodes", "slots"],
                                                "properties": {"kind": {"const": "prefab-definition"}, "version": {"const": 1}}})
    result["reference_map.json"] = text({"game": "SceneStarter", "labels": [], "references": []})
    result[".bed/project.json"] = text(project_settings())
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="compare committed outputs without writing")
    args = parser.parse_args()
    expected = outputs()
    if args.check:
        wrong = [name for name, content in expected.items() if not (ROOT / name).is_file() or (ROOT / name).read_bytes() != content]
        if wrong:
            print("Generated sample files differ: " + ", ".join(wrong))
            return 1
        print(f"All {len(expected)} generated sample files match.")
        return 0
    for name, content in expected.items():
        (ROOT / name).parent.mkdir(parents=True, exist_ok=True)
        (ROOT / name).write_bytes(content)
    print(f"Wrote {len(expected)} sample files: six models, five textures, the crate prefab, the level and the project settings.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
