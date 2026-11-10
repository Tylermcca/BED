"""Self-authored UV crate and 8x8 plank texture. Writes only five named outputs.
Run --check for byte-for-byte verification without writes; standard library only.
This complements generate_assets.py without modifying its cube/pillar outputs.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib

ASSETS = Path(__file__).resolve().parent / "assets"
MODEL = "c8a7e000000000000000000000000001"
TEXTURE = "c8a7e000000000000000000000000002"


def png():
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    raw = bytearray()
    for y in range(8):
        raw.append(0)
        for x in range(8):
            raw.extend((72, 40, 20, 255) if x in (0, 7) or y in (0, 7) or x == y else (176, 116, 56, 255))
    # A single stored DEFLATE block avoids compressor-version choices.
    n = len(raw)
    compressed = b"\x78\x01\x01" + struct.pack("<HH", n, n ^ 65535) + raw + struct.pack(">I", zlib.adler32(raw))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 6, 0, 0, 0)) + chunk(b"IDAT", compressed) + chunk(b"IEND", b"")


def outputs():
    vertices = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 1, -.5), (-.5, 1, -.5),
                (-.5, 0, .5), (.5, 0, .5), (.5, 1, .5), (-.5, 1, .5)]
    faces = [(1, 4, 3, 2), (5, 6, 7, 8), (1, 5, 8, 4), (2, 3, 7, 6), (1, 2, 6, 5), (4, 8, 7, 3)]
    lines = ["# Self-authored BED crate; generate_textured_crate.py", "mtllib textured-crate.mtl", "o textured-crate"]
    lines += ["v " + " ".join(format(v, ".9g") for v in p) for p in vertices]
    lines += ["vt 0 0", "vt 1 0", "vt 1 1", "vt 0 1", "usemtl wood"]
    for a, b, c, d in faces:
        lines += [f"f {a}/1 {b}/2 {c}/3", f"f {a}/1 {c}/3 {d}/4"]
    result = {"textured-crate.obj": ("\n".join(lines) + "\n").encode("ascii"),
              "textured-crate.mtl": b"# Self-authored material\nnewmtl wood\nKd 1 1 1\nmap_Kd textured-crate.png\n",
              "textured-crate.png": png()}
    for source, guid, importer in (("textured-crate.obj", MODEL, "obj"), ("textured-crate.png", TEXTURE, "png")):
        record = {"kind": "asset", "version": 1, "guid": guid, "source": source,
                  "sourceHash": hashlib.sha256(result[source]).hexdigest(), "importer": {"id": importer, "version": 1},
                  "settings": {"scale": 1, "upAxis": "Y"} if importer == "obj" else {},
                  "provenance": {"author": "BED sample contributors", "licence": "self-authored",
                                 "origin": "samples/SceneStarterProject/generate_textured_crate.py", "declared": "ours"},
                  "aiUse": "allowed"}
        result[source + ".asset.json"] = (json.dumps(record, indent=2) + "\n").encode()
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    expected = outputs()
    if args.check:
        wrong = [name for name, data in expected.items() if not (ASSETS / name).is_file() or (ASSETS / name).read_bytes() != data]
        if wrong:
            print("Crate outputs differ: " + ", ".join(wrong))
            return 1
        print("All five self-authored crate outputs match.")
    else:
        ASSETS.mkdir(exist_ok=True)
        for name, data in expected.items():
            (ASSETS / name).write_bytes(data)
        print("Wrote five self-authored crate outputs.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
