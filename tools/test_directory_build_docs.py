"""Library docs are invariant under Windows import casing and project visitation order."""
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

import pytest

ROOT = Path(__file__).resolve().parent.parent


@pytest.mark.skipif(os.name != "nt", reason="Different-case aliases of the same Windows project/import")
@pytest.mark.parametrize("first_upper", [False, True])
def test_mixed_case_references_keep_library_docs_and_leave_tools_unchanged(tmp_path, first_upper):
    probe = tmp_path / "docs.targets"
    probe.write_text('<Project><Target Name="ProbeDocs"><Message Importance="high" '
                     'Text="DOCS|$(MSBuildProjectName)|$(GenerateDocumentationFile)|$(DocumentationFile)" />'
                     '</Target></Project>', encoding="utf-8")
    project = ET.Element("Project")
    target = ET.SubElement(project, "Target", Name="Probe")
    for index, relative in enumerate(("src/Bed.Data/Bed.Data.csproj", "src/Bed.Runtime/Bed.Runtime.csproj",
                                      "tools/SceneShaderCompiler/SceneShaderCompiler.csproj")):
        base = str(ROOT).upper() if (index == 0) == first_upper else str(ROOT).lower()
        ET.SubElement(target, "MSBuild", Projects=str(Path(base) / relative), Targets="ProbeDocs",
                      Properties=f"Configuration=Release;CustomAfterMicrosoftCommonTargets={probe}")
    path = tmp_path / "probe.proj"
    ET.ElementTree(project).write(path, encoding="utf-8", xml_declaration=True)
    dotnet = shutil.which("dotnet") or str(Path(os.environ["ProgramFiles"]) / "dotnet/dotnet.exe")
    result = subprocess.run([dotnet, "msbuild", str(path), "-t:Probe", "-m:1", "-v:minimal"],
                            cwd=ROOT, capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
    rows = [line.strip().split("|") for line in result.stdout.splitlines() if line.strip().startswith("DOCS|")]
    assert len(rows) == 3, result.stdout
    for _, name, enabled, documentation in rows:
        if name == "SceneShaderCompiler":
            assert enabled == "false" and documentation == "", result.stdout
        else:
            assert name in ("Bed.Data", "Bed.Runtime"), result.stdout
            assert enabled == "true" and documentation.endswith(name + ".xml"), result.stdout