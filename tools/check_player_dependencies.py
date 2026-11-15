"""Reject direct and transitive editor references in the players bundled with BED."""
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PLAYERS = (
    "samples/Checkers.Game/Checkers.Game.csproj",
    "samples/SceneStarter/SceneStarter.csproj",
    "samples/PhysicsStarter/PhysicsStarter.csproj",
    "samples/StarterGame/StarterGame.csproj",
)

def check(root: Path = ROOT, players=PLAYERS) -> list[str]:
    problems = []
    for player in players:
        pending = [(root / player, [player])]
        seen = set()
        while pending:
            project, chain = pending.pop()
            project = project.resolve()
            if project in seen:
                continue
            seen.add(project)
            if project.stem.startswith("Bed.Editor"):
                problems.append(" -> ".join(chain))
                continue
            document = ET.parse(project)
            for reference in document.findall(".//PackageReference"):
                name = reference.get("Include", reference.get("Update", ""))
                if name.startswith("Bed.Editor"):
                    problems.append(" -> ".join([*chain, name]))
            # An editor reference hidden behind a build condition is still an editor dependency.
            for reference in document.findall(".//ProjectReference"):
                name = reference.get("Include", "")
                if not name or "$" in name or "*" in name:
                    problems.append(" -> ".join([*chain, "unresolved project reference: " + name]))
                    continue
                dependency = project.parent / name.replace("\\", "/")
                pending.append((dependency, [*chain, dependency.name]))
    return problems

if __name__ == "__main__":
    failures = check()
    print("\n".join(failures) if failures else "Bundled players have no editor project or package references.")
    raise SystemExit(bool(failures))
