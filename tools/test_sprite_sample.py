"""Focused original sprite generation and separate release-staging contract."""
import importlib.util
from pathlib import Path
import package_release as release

ROOT = Path(__file__).resolve().parents[1]

def generator():
    spec = importlib.util.spec_from_file_location("sprite_generator", ROOT / "samples/SpriteLevelProject/generate_assets.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def test_committed_original_outputs_are_reproducible_and_have_owned_rights():
    import hashlib
    import json
    module = generator()
    for name, data in module.outputs().items():
        assert (module.ROOT / name).read_bytes() == data
        if name.endswith(".asset.json"):
            record = json.loads(data)
            assert record["provenance"]["declared"] == "ours"
            assert record["provenance"]["licence"] == "self-authored"
            assert record["sourceHash"] == hashlib.sha256((module.ROOT / "assets" / record["source"]).read_bytes()).hexdigest()

def test_sprite_staging_has_explicit_inventory_without_local_cache_or_generated_files(tmp_path):
    runtime = tmp_path / "runtime"; runtime.mkdir()
    for name in release.SCENE_EXPORT_RUNTIME:
        (runtime / name).write_bytes(b"inert fixture runtime")
    stage = tmp_path / "stage"
    release.copy_sprite_sample(stage, runtime, runtime)
    paths = {p.relative_to(stage).as_posix() for p in stage.rglob("*") if p.is_file()}
    expected = {p for p in release.REQUIRED if p.startswith("samples/sprite-2d/")}
    assert paths == expected
    assert not any("/.bed/cache/" in p or "/.bed/generated/" in p for p in paths)
