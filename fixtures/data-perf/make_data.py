"""INVENTED, self-authored synthetic data. No game or private data is read."""
import argparse
import hashlib
import json
import random
from pathlib import Path

SEED = 330031


def generate(folder, rows, seed=SEED):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    rng = random.Random(seed)
    integer = {"type": "integer", "minimum": -100, "maximum": 100000}
    props = {
        "id": {"type": "string"},
        "name": {"type": "string"},
        "category": {"type": "string", "enum": ["alpha", "beta", "gamma"]},
        "active": {"type": "boolean"},
        "score": {"type": "number", "minimum": -100, "maximum": 1000},
    }
    props.update({f"n{i:02}": {"$ref": "#/$defs/integer"} for i in range(15)})
    props["tags"] = {"type": "array", "items": {"$ref": "#/$defs/tag"}, "minItems": 2, "maxItems": 2}
    props["metadata"] = {"type": "object", "properties": {"note": {"type": "string"}}, "required": ["note"], "additionalProperties": False}
    schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object", "properties": {"rows": {"type": "array", "items": {"$ref": "#/$defs/row"}}},
        "required": ["rows"], "additionalProperties": False,
        "$defs": {
            "integer": integer,
            "tag": {"type": "object", "properties": {"label": {"type": "string"}, "weight": integer}, "required": ["label", "weight"], "additionalProperties": False},
            "row": {"type": "object", "properties": props, "required": list(props), "additionalProperties": False},
        },
    }
    items = []
    for i in range(rows):
        item = {"id": f"{i:06}", "name": f"{'needle' if i % 100 == 0 else 'sample'} {i:06}",
                "category": ["alpha", "beta", "gamma"][i % 3], "active": i % 2 == 0,
                "score": rng.randrange(10000) / 10}
        item.update({f"n{j:02}": rows - i if j == 0 else rng.randrange(100000) for j in range(15)})
        item["tags"] = [{"label": "red", "weight": i % 7}, {"label": "blue", "weight": i % 11}]
        item["metadata"] = {"note": f"group {i % 17}"}
        items.append(item)
    paths = []
    for name, obj in [("schema.json", schema), (f"rows-{rows}.json", {"rows": items})]:
        path = folder / name
        path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        paths.append(path)
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--rows", type=int, choices=[1000, 10000], required=True)
    parser.add_argument("--seed", type=int, default=SEED)
    args = parser.parse_args()
    print(json.dumps(generate(args.folder, args.rows, args.seed), sort_keys=True))
