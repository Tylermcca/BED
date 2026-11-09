"""Summarize one xUnit/VSTest TRX receipt; case-duration sums are never wall elapsed time."""
from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass
from datetime import datetime
from decimal import Decimal
from pathlib import Path
import re
import xml.etree.ElementTree as ET


@dataclass(frozen=True)
class ClassTiming:
    name: str
    cases: int
    seconds: Decimal
    maximum: Decimal


@dataclass(frozen=True)
class Profile:
    classes: tuple[ClassTiming, ...]
    results: int
    missing_durations: int
    unmapped: int
    outcomes: dict[str, int]
    wall_seconds: Decimal | None
    declared_total: int | None

    @property
    def case_seconds(self) -> Decimal:
        return sum((row.seconds for row in self.classes), Decimal(0))


def duration_seconds(value: str) -> Decimal:
    match = re.fullmatch(r"(?:(\d+)\.)?(\d+):(\d{2}):(\d{2}(?:\.\d+)?)", value)
    if not match:
        raise ValueError(f"Unsupported TRX duration: {value!r}")
    days, hours, minutes, seconds = match.groups()
    if int(minutes) >= 60 or Decimal(seconds) >= 60:
        raise ValueError(f"Invalid TRX duration: {value!r}")
    return Decimal(days or 0) * 86400 + Decimal(hours) * 3600 + Decimal(minutes) * 60 + Decimal(seconds)


def parse_receipt(content: bytes) -> Profile:
    root = ET.fromstring(content)
    namespace = root.tag.partition("}")[0] + "}" if root.tag.startswith("{") else ""
    def path(*parts: str) -> str:
        return "/".join(namespace + part for part in parts)
    definitions = {}
    for test in root.findall(path("TestDefinitions", "UnitTest")):
        method = test.find(path("TestMethod"))
        if method is not None and method.get("className"):
            definitions[test.get("id")] = method.get("className")
    rows: dict[str, list[Decimal | int]] = {}
    outcomes: Counter[str] = Counter()
    missing = unmapped = 0
    results = root.findall(path("Results", "UnitTestResult"))
    for result in results:
        name = definitions.get(result.get("testId"), "<unmapped>")
        if name == "<unmapped>":
            unmapped += 1
        outcomes[result.get("outcome", "<missing>")] += 1
        row = rows.setdefault(name, [0, Decimal(0), Decimal(0)])
        row[0] += 1
        duration = result.get("duration")
        if duration is None:
            missing += 1
            continue
        seconds = duration_seconds(duration)
        row[1] += seconds
        row[2] = max(row[2], seconds)
    wall = None
    times = root.find(path("Times"))
    if times is not None and times.get("start") and times.get("finish"):
        start = datetime.fromisoformat(times.get("start").replace("Z", "+00:00"))
        finish = datetime.fromisoformat(times.get("finish").replace("Z", "+00:00"))
        elapsed = finish - start
        wall = Decimal(elapsed.days * 86400 + elapsed.seconds) + Decimal(elapsed.microseconds) / 1000000
        if wall < 0:
            raise ValueError("Receipt finish precedes start")
    counters = root.find(path("ResultSummary", "Counters"))
    total = int(counters.get("total")) if counters is not None and counters.get("total") else None
    classes = tuple(sorted((ClassTiming(name, int(row[0]), row[1], row[2]) for name, row in rows.items()),
                           key=lambda row: (-row.seconds, row.name)))
    return Profile(classes, len(results), missing, unmapped, dict(outcomes), wall, total)


def markdown(profile: Profile, source: str, top: int = 20) -> str:
    if not 1 <= top <= 200:
        raise ValueError("top must be between 1 and 200")
    wall = "unavailable" if profile.wall_seconds is None else f"{profile.wall_seconds:.3f} s"
    lines = [f"Source label: {source}", "",
             f"Receipt wall elapsed: {wall}. Sum of individual case durations: {profile.case_seconds:.3f} s.",
             "Case sums can overlap under parallel execution; they are not elapsed time or measured savings.",
             "Class setup/teardown can sit outside case durations; use full receipt wall time for isolated class runs.",
             f"Results: {profile.results}; declared total: {profile.declared_total}; outcomes: {profile.outcomes}.",
             f"Missing durations: {profile.missing_durations}; unmapped results: {profile.unmapped}.", "",
             "| Class | Cases | Sum case seconds | Slowest case seconds |", "|---|---:|---:|---:|"]
    for row in profile.classes[:top]:
        lines.append(f"| {row.name.replace('|', r'\|')} | {row.cases} | {row.seconds:.3f} | {row.maximum:.3f} |")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("receipt", type=Path)
    parser.add_argument("--source", required=True, help="Explicit source epoch; this tool never runs Git or tests")
    parser.add_argument("--top", type=int, default=20)
    args = parser.parse_args()
    if args.receipt.stat().st_size > 64 * 1024 * 1024:
        parser.error("TRX receipt exceeds the 64 MiB report budget")
    print(markdown(parse_receipt(args.receipt.read_bytes()), args.source, args.top), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
