"""Source-only guide guard: parses text/JSON/AST, never runs BED, Git or packaging."""
from pathlib import Path
import ast
import json
import re
import shlex
import unittest


def validate(root, guide, packager, cli):
    root = Path(root)
    tree = ast.parse(packager)
    values = {}
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id in {"REQUIRED", "TOP_LEVEL"}:
                    values[target.id] = ast.literal_eval(node.value)
    assert "guides/using_bed_with_ai.md" in values["REQUIRED"]
    assert "guides" in values["TOP_LEVEL"]
    assert "adapters/starter/StarterAdapter.dll" in values["REQUIRED"]
    checks = (root / "src/Brutal.Authoring.Data/Checks.cs").read_text(encoding="utf-8-sig")
    assert 'new("links.all", "Check every link"' in checks
    functions = {n.name: n for n in tree.body if isinstance(n, ast.FunctionDef)}
    helper = ast.get_source_segment(packager, functions["copy_ai_guide"])
    assert '"using_bed_with_ai.md"' in helper and '"guide"' in helper and 'shutil.copy2' in helper
    build = ast.get_source_segment(packager, functions["build"])
    assert 'shutil.copytree(ROOT / "samples" / "StarterProject", stage / "samples" / "recipes")' in build
    assert "copy_ai_guide(stage)" in build
    branches = " ".join(re.findall(r"case\s+(.+?):", cli))
    options = set(re.findall(r'"(--[\w-]+)"', branches))
    calls = set(re.findall(r'"([a-z]+)"\s*=>\s*session\.', cli))
    commands = [line.strip() for line in guide.splitlines() if line.startswith("./bed-cli.exe ")]
    assert len(commands) == 5
    seen = []
    for command in commands:
        words = shlex.split(command)
        assert words.pop(0) == "./bed-cli.exe"
        settings = {}
        rest = []
        i = 0
        while i < len(words):
            word = words[i]
            if word.startswith("--"):
                assert word in options, word
                if word in {"--preview", "--save"}:
                    settings[word] = True
                else:
                    assert word in {"--data", "--schemas", "--map"}
                    i += 1
                    settings[word] = words[i]
            else:
                rest.append(word)
            i += 1
        assert settings["--data"] == "samples/recipes/data"
        assert settings["--schemas"] == "samples/recipes/schemas"
        assert settings["--map"] == "samples/recipes/reference_map.json"
        for destination in [settings["--data"], settings["--schemas"], settings["--map"]]:
            source = root / destination.replace("samples/recipes", "samples/StarterProject", 1)
            assert source.exists(), source
        assert rest[0] in calls
        if rest[0] == "describe": assert rest == ["describe"]
        elif rest[0] == "find": assert rest == ["find", "recipes.json", "/recipes", "Milling"]
        elif rest[0] == "set":
            assert rest == ["set", "recipes.json", "/recipes/0/minutes", "25"]
            assert ("--preview" in settings) != ("--save" in settings)
        elif rest[0] == "check": assert rest == ["check", "links.all"]
        else: raise AssertionError("Unexpected walkthrough call")
        seen.append(rest[0])
    assert seen == ["describe", "find", "set", "set", "check"]
    data = json.loads((root / "samples/StarterProject/data/recipes.json").read_text())
    assert data["recipes"][0]["name"] == "Milling" and data["recipes"][0]["minutes"] == 20
    schema = json.loads((root / "samples/StarterProject/schemas/recipes.schema.json").read_text())
    minutes = schema["properties"]["recipes"]["items"]["properties"]["minutes"]
    assert minutes["type"] == "integer" and minutes["minimum"] <= 25
    configs = re.findall(r"```json\n(.*?)\n```", guide, re.S)
    assert len(configs) == 1
    entry = json.loads(configs[0])["mcpServers"]["bed"]
    assert entry["command"] == "FULL_PATH_TO_BED/bed-cli.exe"
    assert entry["args"] == ["--data", "FULL_PATH_TO_BED/samples/recipes/data", "--schemas", "FULL_PATH_TO_BED/samples/recipes/schemas", "--map", "FULL_PATH_TO_BED/samples/recipes/reference_map.json", "mcp"]
    assert "Check your app's current docs" in guide
    assert "--read-only` with `--adapter` is not supported" in guide
    assert "can write immediately unless previewed" in guide
    assert "run ids last only within one session" in cli
    return len(commands)


def repository():
    for root in Path(__file__).resolve().parents:
        if (root / "Brutal.Authoring.slnx").is_file(): return root
    raise AssertionError("repository not found")


class AiGuideTests(unittest.TestCase):
    def test_documented_calls_match_source_and_packaged_sample(self):
        root = repository()
        self.assertEqual(5, validate(root,
            (root / "docs/guide/using_bed_with_ai.md").read_text(encoding="utf-8"),
            (root / "tools/package_release.py").read_text(encoding="utf-8"),
            (root / "src/Brutal.Authoring.Agent.Cli/Program.cs").read_text(encoding="utf-8")))
