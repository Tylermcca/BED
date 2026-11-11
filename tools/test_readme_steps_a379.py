"""Static source recipe guard, never a build or command executor.

New command grammar fails closed for review. SDK --project is recognized grammar;
application options must occur in parser branches, not merely usage prose.
"""
from pathlib import Path
import os
import re
import shlex
import tempfile
import unittest
from urllib.parse import urlsplit


def need(ok, message):
    if not ok:
        raise AssertionError(message)


def path(root, value, line):
    need(not re.match(r"^[A-Za-z]:|^[/\\]|^[a-z]+://", value),
         f"line {line}: expected repository-relative path: {value}")
    result = (root / value).resolve()
    need(result.is_relative_to(root.resolve()), f"line {line}: path escapes repository")
    need(result.exists(), f"line {line}: missing path: {value}")
    return result


def validate(root, text):
    root = Path(root).resolve()
    selected = False
    fenced = False
    sections, commands = set(), []
    for line, raw in enumerate(text.splitlines(), 1):
        if not fenced and raw.startswith("## "):
            title = raw[3:].strip()
            selected = title == "Build from source" or title == "Try it" or title.startswith("Try it (")
            if selected:
                sections.add("Try it" if title.startswith("Try it") else title)
        if raw.strip().startswith(chr(96)*3):
            fenced = not fenced
            continue
        if selected and fenced and raw.strip() and not raw.lstrip().startswith("#"):
            commands.append((line, raw.strip()))
    need(not fenced, "README: unclosed fence")
    need(sections == {"Build from source", "Try it"}, "README: missing required section")
    clone = None
    projects = 0
    for line, command in commands:
        need(not any(x in command for x in (";", "&&", "|", chr(96))),
             f"line {line}: unsupported shell composition")
        words = shlex.split(command)
        if words[:2] == ["git", "clone"]:
            need(len(words) in (3, 4), f"line {line}: unsupported clone syntax")
            url = urlsplit(words[2])
            need(url.scheme == "https" and url.netloc and not url.username,
                 f"line {line}: expected public HTTPS clone URL")
            clone = words[3] if len(words) == 4 else Path(url.path).name.removesuffix(".git")
            need(bool(re.fullmatch(r"[\w.-]+", clone)), f"line {line}: invalid clone destination")
        elif words[:1] == ["cd"]:
            need(len(words) == 2, f"line {line}: unsupported cd syntax")
            if words[1] == clone:
                clone = None  # A freshly cloned root, not a nested repo path.
            else:
                raise AssertionError(f"line {line}: unsupported cd target; review working-directory semantics")
        elif words[:2] == ["dotnet", "run"]:
            head, separator, tail = command.partition(" -- ")
            driver = shlex.split(head)
            need(len(driver) == 4 and driver[2] == "--project",
                 f"line {line}: unsupported run syntax; expected --project PATH")
            target = path(root, driver[3], line)
            candidates = [target] if target.suffix == ".csproj" else list(target.glob("*.csproj"))
            need(len(candidates) == 1 and candidates[0].is_file(), f"line {line}: expected one csproj")
            projects += 1
            if separator:
                parser = candidates[0].parent / "Program.cs"
                need(parser.is_file(), f"line {line}: no known Program.cs parser")
                source = parser.read_text(encoding="utf-8-sig")
                # Explicit branch syntax only. This intentionally does not infer delegates.
                branches = " ".join(re.findall(r"case\s+(.+?):", source))
                options = set(re.findall(r'"(--?[\w-]+|/\?)"', branches))
                options.update(re.findall(r'args\.Contains\("(--[\w-]+)"', source))
                args = shlex.split(tail)
                i = 0
                while i < len(args):
                    arg = args[i]
                    if arg.startswith("-") or arg == "/?":
                        need(arg in options, f"line {line}: option not parsed: {arg}")
                        if arg not in {"--help", "-h", "/?", "--version"}:
                            need(i+1 < len(args) and not args[i+1].startswith("-"),
                                 f"line {line}: missing option value: {arg}")
                            i += 1
                            path(root, args[i], line)
                    else:
                        need(path(root, arg, line).is_dir(), f"line {line}: expected sample folder")
                    i += 1
        else:
            raise AssertionError(f"line {line}: unsupported documented command: {command}")
    need(projects > 0, "README: no project command validated")
    refs = dict(re.findall(r"^\s*\[([^]]+)\]:\s*(\S+)", text, re.M))
    images = re.findall(r"!\[[^]]*\]\(([^)]+)\)", text)
    for alt, key in re.findall(r"!\[([^]]*)\]\[([^]]*)\]", text):
        key = key or alt
        need(key in refs, f"README: missing image reference {key}")
        images.append(refs[key])
    for image in images:
        value = image.split()[0].strip("<>")
        target = path(root, value, "image")
        need(target.is_file() and target.is_relative_to((root/"docs/images").resolve()),
             f"README: image must be a file under docs/images: {value}")
    return len(commands), len(images)


def repository():
    for root in Path(__file__).resolve().parents:
        if (root/"Bed.slnx").is_file():
            return root
    raise AssertionError("cannot locate repository by Bed.slnx")


class ReadmeStepsTests(unittest.TestCase):
    def test_repository_readme(self):
        root = repository()
        validate(root, (root/"README.md").read_text(encoding="utf-8-sig"))

    def test_level_walkthrough_names_existing_controls_and_separates_download_from_development(self):
        root = repository()
        text = (root/"README.md").read_text(encoding="utf-8-sig")
        # The step-by-step level walkthrough lives in the levels guide; the README links to it.
        guide = (root/"docs/guide/levels.md").read_text(encoding="utf-8-sig")
        for word in ("SceneStarterProject", "level.json", "Add selected object", "Position X", "**Play**", "**Stop**"):
            self.assertIn(word, guide)
        for page in ("levels", "playing", "exporting"):
            self.assertIn(f"docs/guide/{page}.md", text)
            self.assertTrue((root/f"docs/guide/{page}.md").is_file())
        for page in ("assets", "playing", "exporting"):
            self.assertIn(f"({page}.md)", guide)
        placement = (root/"src/Bed.Editor.ImGui/DataWorkspace.SceneAssets.cs").read_text(encoding="utf-8-sig")
        self.assertIn("Add selected object", placement)
        launcher = (root/"src/Bed.Launcher/Bed.Launcher.csproj").read_text(encoding="utf-8-sig")
        # The build must supply the adapter; the reader need not learn its assembly name.
        self.assertIn("SceneStarterAdapter/SceneStarterAdapter.csproj", launcher)
        self.assertIn("samples/SceneStarterProject", launcher)
        self.assertIn("0.3.0-rc.3", text)
        self.assertIn("The download includes a level sample", text)
        self.assertIn("samples\scene-starter", text)
        self.assertIn("#build-from-source", text)
        self.assertNotIn("next preview download", text)
        self.assertNotIn("Export build** is the next integration step", text)

    def test_export_steps_name_landed_menu_controls_and_keep_copy_gate_and_empty_folder_rule(self):
        root = repository()
        text = (root/"docs/guide/exporting.md").read_text(encoding="utf-8-sig")
        readme = (root/"docs/guide/levels.md").read_text(encoding="utf-8-sig")
        window = (root/"src/Bed.Editor.ImGui/EditorWindow.cs").read_text(encoding="utf-8-sig")
        dialog = (root/"src/Bed.Editor.ImGui/ExportBuildDialog.cs").read_text(encoding="utf-8-sig")
        self.assertIn('Choice("Export build..."', window)
        for label in ("Build saved files", "Destination folder", "Copy build to folder..."):
            self.assertIn(label, text)
            self.assertIn(label, readme)
            self.assertIn(f'"{label}"u8', dialog)
        self.assertIn("Cancel export", text)
        self.assertIn("only after verification succeeds", text)
        self.assertIn("new or empty", text)
        self.assertIn("refuses a destination that already contains files", text)
        self.assertIn("Export build is in BED **0.2.1-rc.1** and later", text)
        self.assertNotIn("development build", text)

    def test_reader_guides_omit_old_protocol_and_implementation_jargon(self):
        root = repository()
        text = "\n".join((root/file).read_text(encoding="utf-8-sig") for file in (
            "README.md", "docs/guide/levels.md", "docs/guide/assets.md", "docs/guide/playing.md", "docs/guide/building.md"))
        for old in ("runtime peer", "catalog-backed", "parent/local transforms", "linked readiness exchange", "bounded sample protocol"):
            self.assertNotIn(old, text)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get("A379_FIXTURE_ROOT"))
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in ("src/SampleApp", "samples/demo", "docs/images"):
            (self.root/name).mkdir(parents=True)
        (self.root/"src/SampleApp/SampleApp.csproj").write_text("<Project />")
        (self.root/"src/SampleApp/Program.cs").write_text('switch(args[i]) { case "--data": break; } // usage: --imaginary')
        (self.root/"docs/images/demo.png").write_bytes(b"synthetic existence placeholder")
        fence = chr(96)*3
        self.readme = "\n".join(["## Try it", fence, "dotnet run --project src/SampleApp -- --data samples/demo", fence,
            "## Build from source", fence, "git clone https://example.invalid/Example.git", "cd Example",
            "dotnet run --project src/SampleApp", fence, "![Example](docs/images/demo.png)"])

    def test_valid_recipe(self):
        self.assertEqual((4, 1), validate(self.root, self.readme))

    def broken(self, old, new, reason):
        with self.assertRaisesRegex(AssertionError, reason):
            validate(self.root, self.readme.replace(old, new))

    def test_missing_project(self):
        self.broken("src/SampleApp", "src/Missing", "missing path")

    def test_missing_sample(self):
        self.broken("samples/demo", "samples/absent", "missing path")

    def test_unknown_option_is_not_usage_prose(self):
        self.broken("--data samples/demo", "--imaginary samples/demo", "option not parsed")

    def test_missing_image(self):
        self.broken("docs/images/demo.png", "docs/images/absent.png", "missing path")

    def test_image_outside_images(self):
        self.broken("docs/images/demo.png", "src/SampleApp/Program.cs", "under docs/images")

    def test_unsupported_command(self):
        self.broken("dotnet run", "dotnet build", "unsupported documented command")

    def test_changed_directory_requires_review(self):
        self.broken("cd Example", "cd samples/demo", "unsupported cd target")

    def test_missing_value(self):
        self.broken("--data samples/demo", "--data", "missing option value")

    def test_path_escape(self):
        self.broken("samples/demo", "../private", "escapes repository")


if __name__ == "__main__":
    unittest.main()
