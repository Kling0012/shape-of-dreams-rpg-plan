#!/usr/bin/env python3
"""Emit real format-7 trade saves using the unmodified pre-format-8 Core."""
import io
from pathlib import Path
import subprocess
import tarfile

ROOT = Path(__file__).resolve().parents[3]
REVISION = "4e69ed52b5e5231c83b2f467be70a18fed13802a"
BUILD = ROOT / ".ci-logs" / "issue1028-v7-generator"
OUTPUT = ROOT / "tests" / "SodRpg.Core.Tests" / "Fixtures" / "CoopTradeV7"


def main():
    if (OUTPUT / "v7-journal.json").exists():
        raise SystemExit("Fixtures already exist; refusing to overwrite them.")
    BUILD.mkdir(parents=True, exist_ok=True)
    archive = subprocess.check_output(
        ["git", "archive", REVISION, "src/SodRpg.Core"], cwd=ROOT
    )
    with tarfile.open(fileobj=io.BytesIO(archive)) as source:
        for member in source.getmembers():
            if member.isfile() and member.name.endswith(".cs"):
                path = BUILD / member.name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(source.extractfile(member).read())
    (BUILD / "FixtureGenerator.cs").write_bytes(
        (Path(__file__).parent / "FixtureGenerator.cs").read_bytes()
    )
    project = BUILD / "FixtureGenerator.csproj"
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
</Project>
''')
    subprocess.run(
        ["dotnet", "run", "--project", str(project), "-c", "Release", "--", str(OUTPUT)],
        cwd=ROOT, check=True,
    )


if __name__ == "__main__":
    main()
