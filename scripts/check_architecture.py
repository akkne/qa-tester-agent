#!/usr/bin/env python3
"""Check project boundaries and repository templates without extra packages."""

from __future__ import annotations

import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECTS = {
    "src/QaAgent.Domain/QaAgent.Domain.csproj": set(),
    "src/QaAgent.Application/QaAgent.Application.csproj": {
        "src/QaAgent.Domain/QaAgent.Domain.csproj"
    },
    "src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj": {
        "src/QaAgent.Application/QaAgent.Application.csproj",
        "src/QaAgent.Domain/QaAgent.Domain.csproj",
    },
    "src/QaAgent.Host/QaAgent.Host.csproj": {
        "src/QaAgent.Application/QaAgent.Application.csproj",
        "src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj",
        "src/QaAgent.Domain/QaAgent.Domain.csproj",
    },
    "tests/QaAgent.UnitTests/QaAgent.UnitTests.csproj": {
        "src/QaAgent.Application/QaAgent.Application.csproj",
        "src/QaAgent.Domain/QaAgent.Domain.csproj",
    },
    "tests/QaAgent.IntegrationTests/QaAgent.IntegrationTests.csproj": {
        "src/QaAgent.Host/QaAgent.Host.csproj",
        "src/QaAgent.Infrastructure/QaAgent.Infrastructure.csproj",
    },
    "demo/Demo.Web/Demo.Web.csproj": set(),
    "demo/Demo.Tests/Demo.Tests.csproj": {"demo/Demo.Web/Demo.Web.csproj"},
}


def check_projects() -> None:
    solution = ET.parse(ROOT / "QaAgent.slnx").getroot()
    declared = {element.attrib["Path"] for element in solution.iter("Project")}
    assert declared == PROJECTS.keys(), (declared, PROJECTS.keys())

    for path, allowed in PROJECTS.items():
        project = ROOT / path
        tree = ET.parse(project)
        references = {
            (project.parent / element.attrib["Include"]).resolve().relative_to(ROOT).as_posix()
            for element in tree.iter("ProjectReference")
        }
        assert references == allowed, (path, references, allowed)
        for package in tree.iter("PackageReference"):
            assert "Version" not in package.attrib, (path, package.attrib)


def check_docs_and_config() -> None:
    for path in (ROOT / "src").rglob("appsettings*.json"):
        json.loads(path.read_text())
    json.loads((ROOT / "global.json").read_text())

    for source in (ROOT / "docs").rglob("*.md"):
        for target in re.findall(r"\[[^]]+\]\(([^)]+)\)", source.read_text()):
            if not target.startswith(("https://", "http://", "#")):
                assert (source.parent / target.split("#", 1)[0]).exists(), (source, target)

    for environment in ("development", "demo", "production"):
        actual = ROOT / f".env.{environment}"
        example = ROOT / f".env.{environment}.example"
        assert example.exists()
        if actual.exists():
            result = subprocess.run(
                ["git", "check-ignore", "-q", actual.name], cwd=ROOT, check=False
            )
            assert result.returncode == 0, actual


def check_source_format() -> None:
    for source in ROOT.rglob("*.cs"):
        if any(part in {"bin", "obj"} for part in source.parts):
            continue
        content = source.read_text()
        assert content.endswith("\n") and "\r" not in content, source
        assert all(line == line.rstrip() and "\t" not in line for line in content.splitlines()), source
        usings = re.findall(r"^using ([^;]+);$", content, re.MULTILINE)
        assert usings == sorted(usings, key=lambda name: (not name.startswith("System"), name)), source


if __name__ == "__main__":
    check_projects()
    check_docs_and_config()
    check_source_format()
    print("Architecture, project references, source style, JSON, docs links and env ignores: OK")
