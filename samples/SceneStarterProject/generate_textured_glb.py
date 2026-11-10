"""Self-authored textured glTF quad; standard library only.
Writes four named files to an explicit --out directory, or verifies them with --check.
This fixture is optional and never changes the shipped level or existing sample assets.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib

MODEL = "a5140000000000000000000000000001"
TEXTURE = "a5140000000000000000000000000002"


def image():
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    colors = [(240, 24, 32, 255), (24, 224, 40, 255), (24, 40, 232, 255), (240, 224, 24, 255)]
    raw = b"".join(b"\0" + b"".join(bytes(colors[(y // 4) * 2 + x // 4]) for x in range(8)) for y in range(8))
    n = len(raw)
    compressed = b"\x78\x01\x01" + struct.pack("<HH", n, n ^ 65535) + raw + struct.pack(">I", zlib.adler32(raw))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 6, 0, 0, 0)) + chunk(b"IDAT", compressed) + chunk(b"IEND", b"")


def outputs():
    positions = [(-1, 0, 0), (1, 0, 0), (1, 2, 0), (-1, 2, 0)]
    normals = [(0, 0, 1)] * 4
    uv = [(0, 1), (1, 1), (1, 0), (0, 0)]
    binary = bytearray()
    views = []
    def add(fmt, values, target):
        data = b"".join(struct.pack(fmt, *v) for v in values)
        views.append({"buffer": 0, "byteOffset": len(binary), "byteLength": len(data), "target": target})
        binary.extend(data)
    add("<3f", positions, 34962)
    add("<3f", normals, 34962)
    add("<2f", uv, 34962)
    add("<H", [(i,) for i in (0, 1, 2, 0, 2, 3)], 34963)
    document = {"asset": {"version": "2.0", "generator": "BED self-authored textured quad"},
                "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"name": "painted-quad", "mesh": 0}],
                "buffers": [{"byteLength": len(binary)}], "bufferViews": views,
                "accessors": [{"bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3", "min": [-1, 0, 0], "max": [1, 2, 0]},
                              {"bufferView": 1, "componentType": 5126, "count": 4, "type": "VEC3"},
                              {"bufferView": 2, "componentType": 5126, "count": 4, "type": "VEC2"},
                              {"bufferView": 3, "componentType": 5123, "count": 6, "type": "SCALAR"}],
                "images": [{"uri": "textured-quad.png"}], "samplers": [{"magFilter": 9729, "minFilter": 9729, "wrapS": 10497, "wrapT": 10497}],
                "textures": [{"sampler": 0, "source": 0}], "materials": [{"name": "four-colors", "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "baseColorTexture": {"index": 0}, "metallicFactor": 0, "roughnessFactor": 1}}],
                "meshes": [{"name": "textured-quad", "primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": 0}]}]}
    encoded = json.dumps(document, separators=(",", ":"), ensure_ascii=True).encode("ascii")
    encoded += b" " * (-len(encoded) % 4)
    binary.extend(b"\0" * (-len(binary) % 4))
    glb = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(encoded) + 8 + len(binary))
    glb += struct.pack("<II", len(encoded), 0x4E4F534A) + encoded + struct.pack("<II", len(binary), 0x004E4942) + binary
    result = {"textured-quad.glb": glb, "textured-quad.png": image()}
    for source, guid, importer in (("textured-quad.glb", MODEL, "gltf"), ("textured-quad.png", TEXTURE, "png")):
        record = {"kind": "asset", "version": 1, "guid": guid, "source": source,
                  "sourceHash": hashlib.sha256(result[source]).hexdigest(), "importer": {"id": importer, "version": 1},
                  "settings": {"scale": 1, "upAxis": "Y"} if importer == "gltf" else {},
                  "provenance": {"author": "BED sample contributors", "licence": "self-authored", "origin": "samples/SceneStarterProject/generate_textured_glb.py", "declared": "ours"}, "aiUse": "allowed"}
        result[source + ".asset.json"] = (json.dumps(record, indent=2) + "\n").encode("utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    expected = outputs()
    if args.check:
        wrong = [name for name, data in expected.items() if not (args.out / name).is_file() or (args.out / name).read_bytes() != data]
        if wrong:
            print("GLB outputs differ: " + ", ".join(wrong))
            return 1
        print("All four self-authored textured GLB outputs match.")
    else:
        args.out.mkdir(parents=True, exist_ok=True)
        for name, data in expected.items():
            (args.out / name).write_bytes(data)
        print("Wrote four self-authored textured GLB outputs.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
