"""Writes a plain-English summary of BED's test results to the GitHub run page.

Reads the .trx files `dotnet test --logger trx` wrote and appends Markdown to the file
named by GITHUB_STEP_SUMMARY (or prints it when that isn't set). Test names like
`SpriteSampleTests.Stop_closes_the_window` become "Sprite sample: stop closes the window".
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def words(name: str) -> str:
    """A test's full name as a short plain sentence."""
    name = re.sub(r"\(.*\)$", "", name)          # drop theory arguments
    parts = name.split(".")
    method = parts[-1]
    owner = parts[-2] if len(parts) > 1 else ""
    owner = re.sub(r"Tests?$", "", owner)
    owner = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", owner).strip()
    method = method.replace("_", " ").strip()
    method = method[:1].upper() + method[1:]
    return f"{owner}: {method}" if owner else method


def first_line(text: str) -> str:
    for line in (text or "").splitlines():
        line = line.strip()
        if line:
            return line[:300]
    return ""


def main(folder: str) -> int:
    passed, skipped, failed = 0, [], []
    for path in glob.glob(os.path.join(folder, "**", "*.trx"), recursive=True):
        root = ET.parse(path).getroot()
        for r in root.iterfind(".//t:UnitTestResult", NS):
            outcome = r.get("outcome")
            name = words(r.get("testName", ""))
            message = first_line("".join(r.find(".//t:Message", NS).itertext()) if r.find(".//t:Message", NS) is not None else "")
            if outcome == "Passed":
                passed += 1
            elif outcome == "Failed":
                failed.append((name, message))
            else:
                skipped.append((name, message or "no reason given"))

    out = []
    if failed:
        out.append(f"## {len(failed)} of BED's checks failed")
    elif passed:
        out.append("## All of BED's checks passed")
    else:
        out.append("## No test results were found")
        out.append("The tests didn't run, most likely because BED didn't build. The build step above says why.")
    out.append(f"{passed:,} passed, {len(failed)} failed, {len(skipped)} skipped.")
    if failed:
        out.append("\n### What failed")
        out += [f"- **{n}**  \n  {m}" for n, m in failed]
    if skipped:
        reasons = {}
        for n, m in skipped:
            reasons.setdefault(m, []).append(n)
        out.append("\n### Skipped, and why")
        out.append("These checks need something this machine doesn't have, like a graphics card, or are slow checks run only by hand.")
        for reason, names in sorted(reasons.items(), key=lambda kv: -len(kv[1])):
            out.append(f"- {reason} ({len(names)})")
    text = "\n".join(out) + "\n"
    target = os.environ.get("GITHUB_STEP_SUMMARY")
    if target:
        with open(target, "a", encoding="utf-8") as f:
            f.write(text)
    else:
        sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "TestResults"))
