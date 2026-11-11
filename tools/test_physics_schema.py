"""The accepted scene2 physics vocabulary; semantic world checks belong to the core resolver."""
import copy
import json
from pathlib import Path
import pytest
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = json.loads((ROOT / "schemas/scene-v2.schema.json").read_text(encoding="utf-8-sig"))
FIXTURES = ROOT / "tests/Bed.Tests/Fixtures/Physics"

@pytest.mark.parametrize("name", ["physics-3d.example.json", "physics-2d.example.json"])
def test_accepted_physics_examples(name):
    Draft202012Validator.check_schema(SCHEMA)
    Draft202012Validator(SCHEMA).validate(json.loads((FIXTURES / name).read_text(encoding="utf-8-sig")))

@pytest.mark.parametrize("change", ["missing_mass", "static_mass", "zero_radius", "friction", "unknown_world", "unknown_shape", "missing_step", "unknown_body"])
def test_invalid_owned_physics_fields_refuse(change):
    scene = json.loads((FIXTURES / "physics-3d.example.json").read_text(encoding="utf-8-sig"))
    body = scene["objects"][1]["body"]
    if change == "missing_mass": del body["massKg"]
    if change == "static_mass": body["motion"] = "static"
    if change == "zero_radius": body["shape"] = {"kind": "sphere", "radius": 0}
    if change == "friction": body["friction"] = -0.1
    if change == "unknown_world": scene["physics"]["solver"] = 1
    if change == "unknown_shape": body["shape"] = {"kind": "triangle"}
    if change == "missing_step": del scene["physics"]["fixedStepSeconds"]
    if change == "unknown_body": body["restitution"] = 0.5
    assert list(Draft202012Validator(SCHEMA).iter_errors(scene))

def test_definition_schema_shares_the_same_closed_body_and_override_vocabulary():
    prefab = json.loads((ROOT / "schemas/prefab-definition-v1.schema.json").read_text(encoding="utf-8-sig"))
    Draft202012Validator.check_schema(prefab)
    assert prefab["$defs"] == SCHEMA["$defs"]
    original = json.loads((ROOT / "tests/Bed.Tests/Fixtures/Scene2/prefab-definition.example.json").read_text(encoding="utf-8-sig"))
    body = {"motion": "dynamic", "massKg": 2, "friction": 0.5, "shape": {"kind": "sphere", "radius": 1}}
    original["nodes"][0]["body"] = copy.deepcopy(body)
    Draft202012Validator(prefab).validate(original)
    original["nodes"][0]["body"]["massKg"] = 0
    assert list(Draft202012Validator(prefab).iter_errors(original))
