"""Read-only Markdown/source drift checks. No fenced command is ever executed.

INVENTED: static citation/symbol/command audit. Uses declarations and declared
walkthrough files, not arbitrary source-text occurrences. Ambiguous citations or
dynamic command syntax are reported for review, never silently certified.
"""
from __future__ import annotations
import argparse
from collections import defaultdict
from dataclasses import dataclass
import json
from pathlib import Path
import re
import shlex
import shutil
import xml.etree.ElementTree as ET
from urllib.parse import unquote

PAGES = ("README.md", "CONTRIBUTING.md", "docs/api/extension_points.md", "docs/quick_start.md")
FENCE = re.compile(r"(?ms)^ {0,3}```([^\n`]*)\n(.*?)^ {0,3}```[ \t]*$")
CODE = re.compile(r"(?<!`)`([^`\n]+)`(?!`)")
LINK = re.compile(r"(?<!!)\[[^\]\n]*\]\(([^)\n]+)\)")
PATH = re.compile(r"(?<![\w/])(?:[A-Za-z_.-][A-Za-z0-9_.-]*/)+[^\s`|,;)<>]+")
EXT = re.compile(r"\.(?:cs|csproj|slnx|props|targets|py|ps1|json|md|txt|dll|exe|png|xlsx|csv|tsv|yaml|yml|toml|config|nuspec)(?::\d+(?:-\d+)?)?$", re.I)
CITE = re.compile(r"^(.*?):(\d+)(?:-(\d+))?$")
SHORT = re.compile(r"^:(\d+)(?:-(\d+))?$")
TYPE = re.compile(r"\b(class|interface|struct|enum|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)")
DECL = re.compile(r"\b(?:public|private|protected|internal)\s+[^;{}\n]*?\b([A-Za-z_]\w*)\s*(?=\(|=>|\{|=|;)")
CONTROL = {"if", "for", "foreach", "while", "switch", "catch", "return", "new", "get", "set", "init", "where"}
FRAMEWORK = {"Action", "Func", "Task", "TimeSpan", "CancellationToken", "JsonElement", "JsonDocument", "JsonObject", "JsonArray", "IEnumerable", "IReadOnlyList", "IList", "List", "Dictionary", "IProgress", "StringReader", "StringWriter", "TextReader", "TextWriter", "Exception"}


@dataclass(frozen=True)
class InlineSpan:
    value: str
    left: int
    right: int

    def __getitem__(self, index):
        if index != 1:
            raise IndexError(index)
        return self.value

    def start(self):
        return self.left

    def end(self):
        return self.right


def clean_cs(text):
    """Mask comments/strings, keeping line numbers and braces in actual code."""
    token = re.compile(r'(?s)/\*.*?\*/|//[^\n]*|\$*"{3,}.*?"{3,}|@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'')
    return token.sub(lambda m: "".join("\n" if c == "\n" else " " for c in m[0]), text)


class Symbols:
    def __init__(self):
        self.types = set()
        self.members = defaultdict(set)
        self.namespaces = set()
        self.declarations = defaultdict(set)
        self.locations = defaultdict(set)

    def add(self, text, key):
        code = clean_cs(text)
        self.namespaces.update(re.findall(r"\bnamespace\s+([\w.]+)", code))
        braces = []
        pairs = {}
        for i, c in enumerate(code):
            if c == "{":
                braces.append(i)
            elif c == "}" and braces:
                pairs[braces.pop()] = i
        scopes = []
        for match in TYPE.finditer(code):
            name = match[2]
            self.types.add(name)
            self.locations[name].add((key, code.count("\n", 0, match.start()) + 1))
            self.declarations[(key, code.count("\n", 0, match.start()) + 1)].add(name)
            tail = code[match.end():]
            opening = code.find("{", match.end())
            semi = code.find(";", match.end())
            if opening >= 0 and (semi < 0 or opening < semi):
                scopes.append((opening, pairs.get(opening, len(code)), name, match[1]))
            if match[1].startswith("record") and tail.lstrip().startswith("("):
                end = tail.find(")")
                for parameter in tail[tail.find("(") + 1:end].split(","):
                    names = re.findall(r"\b([A-Za-z_]\w*)\b", parameter.split("=", 1)[0])
                    if names:
                        self.members[name].add(names[-1])
                        number = code.count("\n", 0, code.find(parameter, match.end())) + 1
                        self.locations[name + "." + names[-1]].add((key, number))
                        self.locations[names[-1]].add((key, number))
        for match in re.finditer(r"\bdelegate\s+[^;{}()\n]+?\b([A-Za-z_]\w*)\s*\(", code):
            name = match[1]
            number = code.count("\n", 0, match.start()) + 1
            self.types.add(name)
            self.locations[name].add((key, number))
            self.declarations[(key, number)].add(name)
        def owner(at):
            candidates = [s for s in scopes if s[0] < at < s[1]]
            return max(candidates, default=None, key=lambda s: s[0])
        for match in DECL.finditer(code):
            name = match[1]
            scope = owner(match.start())
            if scope and name not in CONTROL and name not in self.types:
                self.members[scope[2]].add(name)
                self.locations[scope[2] + "." + name].add((key, code.count("\n", 0, match.start()) + 1))
                self.locations[name].add((key, code.count("\n", 0, match.start()) + 1))
                self.declarations[(key, code.count("\n", 0, match.start()) + 1)].add(name)
        # Interface members have implicit public visibility; enum values are declarations too.
        for left, right, name, kind in scopes:
            body = code[left + 1:right]
            if kind == "interface":
                for m in re.finditer(r"\b([A-Za-z_]\w*)\s*(?=\(|\{|=>)", body):
                    if m[1] not in CONTROL:
                        self.members[name].add(m[1])
                        number = code.count("\n", 0, left + 1 + m.start()) + 1
                        self.locations[name + "." + m[1]].add((key, number))
                        self.locations[m[1]].add((key, number))
                        self.declarations[(key, code.count("\n", 0, left + 1 + m.start()) + 1)].add(m[1])
            if kind == "enum":
                for m in re.finditer(r"(?:^|,)\s*([A-Za-z_]\w*)", body):
                    self.members[name].add(m[1])
                    number = code.count("\n", 0, left + 1 + m.start()) + 1
                    self.locations[name + "." + m[1]].add((key, number))
                    self.locations[m[1]].add((key, number))
                    self.declarations[(key, code.count("\n", 0, left + 1 + m.start()) + 1)].add(m[1])

    def known(self, token):
        if token in self.types or token in self.namespaces or "." in token and token.rsplit(".", 1)[0] in self.namespaces and token.rsplit(".", 1)[1] in self.types:
            return True
        if "." in token:
            owner, member = token.rsplit(".", 1)
            if "." in owner and owner.rsplit(".", 1)[0] not in self.namespaces:
                return False
            owner = owner.rsplit(".", 1)[-1]
            if owner in self.types:
                return member in self.members[owner]
        return any(token.rsplit(".", 1)[-1] in names for names in self.members.values())


def api_names(span):
    """Backticks are mixed prose: paths/flags/literals are handled separately."""
    if re.match(r"^(?:[A-Za-z]:[\\/]|(?:src|docs|tools|samples|tests|fixtures|build)/)", span) or span.startswith(("--", "-h", ":")) or EXT.search(span) or "/" in span and "." not in span:
        return []
    parts = re.findall(r"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*", span)
    result = []
    for token in parts:
        if token in {"true", "false", "null"}:
            continue
        if token[0].isupper() or "." in token and token.rsplit(".", 1)[1][0].isupper():
            result.append(token)
    return result


def audit(root, pages=PAGES, near=2, find_program=shutil.which):
    root = Path(root).resolve()
    if near < 0:
        raise ValueError("near must be nonnegative")
    findings, notes, checks = [], [], []
    seen = set()
    def report(page, line, kind, message, status="drift"):
        key = (str(page), line, kind, message)
        if key not in seen:
            seen.add(key)
            findings.append(dict(page=str(page), line=line, kind=kind, status=status, message=message))
    source = {}
    symbols = Symbols()
    for folder in ("src", "samples"):
        for p in sorted((root / folder).rglob("*.cs")):
            if any(x in {"bin", "obj", "codex", ".git", "third_party"} for x in p.relative_to(root).parts):
                continue
            text = p.read_text(encoding="utf-8-sig")
            key = p.relative_to(root).as_posix()
            source[key] = text
            symbols.add(text, key)
    basename = defaultdict(list)
    for folder in ("src", "docs", "tools", "samples", "tests", "build", "fixtures"):
        for p in (root / folder).rglob("*"):
            if p.is_file() and not any(x in {"bin", "obj", "codex", ".git", "__pycache__"} for x in p.relative_to(root).parts):
                basename[p.name].append(p)
    texts = {}
    for page in pages:
        path = Path(page)
        path = path if path.is_absolute() else root / path
        if not path.is_file():
            report(page, 1, "missing-page", "Page does not exist: " + str(path))
            continue
        texts[str(page)] = (path, path.read_text(encoding="utf-8-sig"))
    # Example declarations are source too, but only actual declaration syntax counts.
    for page, (_, text) in texts.items():
        for i, block in enumerate(FENCE.finditer(text)):
            if block[1].strip().lower() in {"cs", "csharp"}:
                symbols.add(block[2], page + "#example" + str(i))

    for page, (page_path, text) in texts.items():
        virtual = {}
        dirs = set()
        inline_commands = []
        blocks = list(FENCE.finditer(text))
        for block in blocks:
            prefix = text[:block.start()]
            declaration = re.search(r"<!-- file:([^>]+) -->\s*$", prefix)
            if declaration:
                name = declaration[1].strip().replace("\\", "/")
                virtual[name] = block[2]
                dirs.update(str(p).replace("\\", "/") for p in Path(name).parents if str(p) != ".")
        def exists(name):
            name = name.replace("\\", "/").rstrip("/")
            return name in virtual or name in dirs or (root / name).exists()
        def resolve(name):
            name = unquote(name).strip("<>").split("#", 1)[0].replace("\\", "/")
            if name in virtual or name in dirs:
                return None, "declared"
            virtual_names = [x for x in virtual if Path(x).name == name]
            if "/" not in name and len(virtual_names) == 1:
                return None, "declared"
            options = [page_path.parent / name, root / name]
            for p in options:
                if p.exists():
                    return p, "exists"
            if "/" not in name and len(basename.get(name, [])) == 1:
                return basename[name][0], "exists"
            if "/" not in name and len(basename.get(name, [])) > 1:
                return None, "ambiguous"
            return None, "missing"
        # Strip fenced blocks before scanning narrative references.
        narrative = list(text)
        for block in blocks:
            for i in range(block.start(), block.end()):
                if narrative[i] != "\n":
                    narrative[i] = " "
        narrative = "".join(narrative)
        section_path = None
        processed_plain = set()
        for paragraph in re.finditer(r"(?s)(?:[^\n]|\n(?!\s*\n))+", narrative):
            if re.search(r"(?m)^\s*#{1,6}\s", paragraph[0]):
                section_path = None
            last_path = section_path
            anchors = []
            original_spans = list(CODE.finditer(paragraph[0]))
            spans = [InlineSpan(m[1], m.start(), m.end()) for m in original_spans]
            links = list(LINK.finditer(paragraph[0]))
            for match in PATH.finditer(paragraph[0]):
                if any(m.start() <= match.start() < m.end() for m in original_spans + links):
                    continue
                value = match[0].rstrip(".:")
                source_line = paragraph[0].splitlines()[paragraph[0].count("\n", 0, match.start())]
                if CITE.match(value) or EXT.search(value) or source_line.lstrip().startswith("|"):
                    spans.append(InlineSpan(value, match.start(), match.start() + len(value)))
                    processed_plain.add(paragraph.start() + match.start())
            spans.sort(key=lambda m: m.start())
            used_after = set()
            for token_index, token in enumerate(spans):
                value = token[1].strip()
                line = text.count("\n", 0, paragraph.start() + token.start()) + 1
                if re.match(r"^(?:dotnet|python|python3|powershell|pwsh)\s+", value):
                    inline_commands.append((line, value))
                    continue
                names = api_names(value)
                if names:
                    for name in names:
                        short = name.split(".")[0]
                        if short in FRAMEWORK:
                            if "." in name:
                                report(page, line, "framework-member", name + " is outside the repo declaration index", "review")
                            notes.append(dict(page=page, line=line, kind="framework-symbol", value=name))
                            continue
                        if not symbols.known(name):
                            report(page, line, "missing-symbol", "No declaration for " + name)
                        else:
                            checks.append(dict(page=page, line=line, kind="symbol", value=name))
                            if token_index not in used_after:
                                if anchors:
                                    gap = paragraph[0][anchors[-1][1]:token.start()]
                                    if not re.fullmatch(r"[\s/,]*(?:(?:and|or)[\s/,]*)?", gap):
                                        anchors = []
                                anchors.append((name, token.end()))
                    continue
                citation = CITE.match(value)
                shorthand = SHORT.match(value)
                if shorthand:
                    if not last_path:
                        report(page, line, "unresolved-shorthand", value + " has no preceding path in its paragraph", "review")
                        continue
                    name, first, end = last_path, int(shorthand[1]), int(shorthand[2] or shorthand[1])
                elif citation:
                    name, first, end = citation[1], int(citation[2]), int(citation[3] or citation[2])
                    last_path = name
                else:
                    name, first, end = value, None, None
                if not (name in dirs or name in virtual or first is not None or EXT.search(name) or name.endswith("/") or re.match(r"^[A-Za-z]:[\\/]", name) or name.startswith(("src/", "docs/", "tools/", "samples/", "tests/", "fixtures/", "build/"))):
                    continue
                p, status = resolve(name)
                if status == "declared":
                    if first is not None:
                        contents = virtual.get(name)
                        if contents is None:
                            report(page, line, "declared-line", name + " has no declared text to check", "review")
                        elif first < 1 or end < first or end > len(contents.splitlines()):
                            report(page, line, "stale-line", f"{name}:{first}-{end} outside declared text")
                        else:
                            report(page, line, "declared-anchor", name + ": line exists in its declared example; declaration anchor needs review", "review")
                    checks.append(dict(page=page, line=line, kind="declared-file", value=name))
                    continue
                if status == "ambiguous":
                    report(page, line, "ambiguous-path", name + " needs a repo-relative path", "review")
                    continue
                if status == "missing":
                    if name.lower().endswith((".dll", ".exe")) or name == "adapters/":
                        report(page, line, "runtime-artifact", name + " is a runtime/example artifact, not verified in source", "review")
                    else:
                        report(page, line, "missing-path", "No file or directory for " + name)
                    continue
                checks.append(dict(page=page, line=line, kind="path", value=name))
                if first is None:
                    continue
                if not p.resolve().is_relative_to(root) or not p.is_file() or p.suffix.lower() in {".png", ".dll", ".exe", ".xlsx"}:
                    report(page, line, "unreadable-citation", name + ": line content is outside root, a directory or binary; not read", "review")
                    continue
                lines = p.read_text(encoding="utf-8-sig").splitlines()
                if first < 1 or end < first or end > len(lines):
                    report(page, line, "stale-line", f"{name}:{first}-{end} outside 1..{len(lines)}")
                    continue
                if p.suffix.lower() != ".cs":
                    continue
                anchors = [(name, at) for name, at in anchors if token.start() - at <= 200]
                expected = anchors.pop(0)[0] if anchors else None
                if expected is None and token_index + 1 < len(spans):
                    following = spans[token_index + 1]
                    after_names = api_names(following[1])
                    if re.fullmatch(r"\s*(?:defines|declares|implements|contains|\()\s*", paragraph[0][token.end():following.start()]) and len(after_names) == 1 and symbols.known(after_names[0]):
                        expected = after_names[0]
                        used_after.add(token_index + 1)
                window = "\n".join(lines[max(0, first - 1 - near):min(len(lines), end + near)])
                if expected:
                    member = expected.rsplit(".", 1)[-1]
                    key = p.relative_to(root).as_posix() if p.is_relative_to(root) else str(p)
                    owner_name = expected.rsplit(".", 1)[0].rsplit(".", 1)[-1] + "." + member if "." in expected else expected
                    location_name = expected if expected in symbols.locations else owner_name if owner_name in symbols.locations else member
                    located = symbols.locations.get(location_name, set())
                    declared_here = any(path == key and max(1, first - near) <= number <= end + near for path, number in located)
                    named_here = re.search(r"\b" + re.escape(member) + r"\b", clean_cs(window)) is not None
                    nearby_prose = paragraph[0][max(0, token.start() - 100):token.end() + 100]
                    defines = re.search(r"\b(?:defined|declared|definitions?|declarations?|defines|declares)\b", nearby_prose, re.I) is not None
                    if not named_here or defines and not declared_here:
                        role = "declaration of " if defines else "named symbol "
                        report(page, line, "stale-anchor", f"{name}:{first}: expected {role}{expected} within +/-{near} lines")
                    else:
                        checks.append(dict(page=page, line=line, kind="anchor", value=f"{name}:{first} {expected}"))
                else:
                    report(page, line, "unanchored-citation", f"{name}:{first}: add a named type/member beside this citation", "review")
            section_path = last_path
        for link in LINK.finditer(narrative):
            target = link[1].strip().split(' "', 1)[0]
            line = text.count("\n", 0, link.start()) + 1
            if re.match(r"[A-Za-z][A-Za-z0-9+.-]*:", target) or target.startswith("#"):
                notes.append(dict(page=page, line=line, kind="external-or-fragment-link", value=target))
                continue
            relative = unquote(target).strip("<>").split("#", 1)[0].replace("\\", "/")
            p = root / relative.lstrip("/") if relative.startswith("/") else page_path.parent / relative
            declared = p.is_relative_to(root) and (p.relative_to(root).as_posix() in virtual or p.relative_to(root).as_posix() in dirs)
            status = "exists" if p.exists() else "declared" if declared else "missing"
            if status not in {"exists", "declared"}:
                report(page, line, "missing-link", target + " does not resolve relative to page or repo")
            else:
                checks.append(dict(page=page, line=line, kind="link", value=target))
        # Plain repo paths in tables/prose are citations as well as code spans.
        for match in PATH.finditer(narrative):
            if match.start() in processed_plain or any(m.start() <= match.start() < m.end() for m in LINK.finditer(narrative)):
                continue
            name = match[0].rstrip(".:")
            if CITE.match(name):
                continue  # handled as a line citation above
            if any(span.start() <= match.start() < span.end() for span in CODE.finditer(narrative)):
                continue
            line = text.count("\n", 0, match.start()) + 1
            _, status = resolve(name)
            if status not in {"exists", "declared"}:
                source_line = narrative.splitlines()[line - 1]
                if not EXT.search(name) and not source_line.lstrip().startswith("|"):
                    notes.append(dict(page=page, line=line, kind="unquoted-prose-pathlike", value=name))
                    continue
                report(page, line, "missing-path", "No file or directory for " + name)
            else:
                checks.append(dict(page=page, line=line, kind="path", value=name))
        # A bare filename in prose is still a citation (for example REPORT.md).
        excluded = [(m.start(), m.end()) for m in CODE.finditer(narrative)] + [(m.start(), m.end()) for m in LINK.finditer(narrative)]
        bare = re.compile(r"(?<![\w/\\.])([A-Za-z_][A-Za-z0-9_.-]*\.(?:md|cs|json|py|csproj|slnx|props|targets|ps1|txt|png|csv|tsv|yaml|yml|toml|config|nuspec))(?!\w|\.[A-Za-z0-9_])")
        for match in bare.finditer(narrative):
            if any(start <= match.start() < end for start, end in excluded):
                continue
            name = match[1]
            line = text.count("\n", 0, match.start()) + 1
            _, status = resolve(name)
            if status in {"exists", "declared"}:
                checks.append(dict(page=page, line=line, kind="prose-file", value=name))
            elif status == "ambiguous":
                report(page, line, "ambiguous-path", name + " needs a repo-relative path", "review")
            elif name.lower().endswith(".png"):
                report(page, line, "runtime-artifact", name + " is an output image, not verified in source", "review")
            else:
                report(page, line, "missing-path", "No file or directory for " + name)
        # Commands are parsed, never launched. Step outputs become virtual inputs.
        command_lines = inline_commands[:]
        for block in blocks:
            if block[1].strip().lower() in {"powershell", "sh", "bash", "cmd", "console"}:
                command_lines.extend((text.count("\n", 0, block.start()) + 2 + offset, command)
                                     for offset, command in enumerate(block[2].splitlines()))
        for line, command in command_lines:
            command = command.strip()
            if not command or command.startswith("#"):
                continue
            try:
                args = shlex.split(command, posix=True)
            except ValueError as exc:
                report(page, line, "command-syntax", str(exc))
                continue
            if any(x in args for x in ("|", ";", "&&", ">", ">>")) or command.startswith("$"):
                report(page, line, "dynamic-command", "Not statically verified: " + command, "review")
                continue
            program = args[0]
            if program.lower() == "new-item":
                if "-ItemType" not in args or args[args.index("-ItemType") + 1:][:1] != ["Directory"]:
                    report(page, line, "unsupported-new-item", "Only literal directory creation is statically modeled", "review")
                    continue
                names = " ".join(args[1:])
                names = re.sub(r"-(?:ItemType)\s+Directory|-Force", "", names, flags=re.I)
                for name in names.split(","):
                    if name.strip():
                        dirs.add(name.strip().replace("\\", "/"))
                checks.append(dict(page=page, line=line, kind="command", value=command, executed=False))
                continue
            if program not in {"python", "python3", "dotnet", "powershell", "pwsh"}:
                report(page, line, "unsupported-command", "Not statically verified: " + program, "review")
                continue
            if not find_program(program):
                report(page, line, "missing-program", "Program not found on PATH: " + program)
            inputs = []
            if program.startswith("python") and len(args) > 1 and not args[1].startswith("-"):
                inputs.append(args[1])
            if program == "dotnet" and len(args) > 1:
                verb = args[1]
                if verb in {"build", "test", "restore"}:
                    inputs.extend(x for x in args[2:] if x.endswith((".csproj", ".slnx", ".sln")))
                if "--project" in args:
                    at = args.index("--project")
                    if at + 1 < len(args):
                        inputs.append(args[at + 1])
                    else:
                        report(page, line, "missing-option-value", "--project needs an input path")
                for flag in ("--data", "--schemas", "--map", "--adapter"):
                    if flag in args:
                        at = args.index(flag)
                        if at + 1 < len(args):
                            inputs.append(args[at + 1])
                        else:
                            report(page, line, "missing-option-value", flag + " needs an input path")
                if verb == "build":
                    for name in inputs[:1]:
                        if name.endswith(".csproj"):
                            p = root / name
                            xml = virtual.get(name) or (p.read_text(encoding="utf-8-sig") if p.is_file() else None)
                            if xml:
                                try:
                                    element = ET.fromstring(xml)
                                    tfm = element.findtext(".//TargetFramework")
                                    if not tfm and (root / "Directory.Build.props").is_file():
                                        tfm = ET.fromstring((root / "Directory.Build.props").read_text(encoding="utf-8-sig")).findtext(".//TargetFramework")
                                    if not tfm or "$" in tfm:
                                        report(page, line, "unknown-build-output", "No literal target framework for " + name, "review")
                                        continue
                                    assembly = element.findtext(".//AssemblyName") or Path(name).stem
                                    virtual[(Path(name).parent / "bin" / "Debug" / tfm / (assembly + ".dll")).as_posix()] = "<built output>"
                                except ET.ParseError:
                                    report(page, line, "project-xml", "Cannot read project XML: " + name)
            for name in inputs:
                if not exists(name):
                    report(page, line, "missing-command-input", "Command input is absent and not declared/generated earlier: " + name)
            checks.append(dict(page=page, line=line, kind="command", value=command, executed=False))
    return dict(root=str(root),pages=list(pages),findings=findings,checks=checks,notes=notes,commands_executed=0,
                counts=dict(drift=sum(x['status']=="drift" for x in findings),review=sum(x['status']=="review" for x in findings),checks=len(checks),notes=len(notes)))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pages", nargs="*", default=list(PAGES))
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--near", type=int, default=2)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    if args.near < 0:
        parser.error("--near must be nonnegative")
    try:
        result = audit(args.root, args.pages, args.near)
    except (OSError, UnicodeError, ValueError) as exc:
        parser.error(str(exc))
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        for row in result['findings']:
            print(f"{row['status'].upper()} {row['page']}:{row['line']} {row['kind']}: {row['message']}")
        print(f"{result['counts']['drift']} drift; {result['counts']['review']} review; {result['counts']['checks']} checks; commands executed: 0")
    return 1 if result['findings'] else 0

if __name__ == "__main__":
    raise SystemExit(main())
