#!/usr/bin/env python3
"""Attempt issue #10 reproduction using the existing startup harness on HDD."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
TEST = "tests/SodRpg.Mod.Startup.Tests/Issue10LimboTravelReproTests.cs"
PROJECT = "tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj"
FILTER = "|".join(
    "FullyQualifiedName~" + name
    for name in (
        "Issue10SoloLimboTravelThroughCombatCycle",
        "LobbySceneFirstMapUsesCurrentTransportCompatibility",
        "HostRevealsOneRoomPerArrivalWithoutChangingFiniteGraphOrAllowingHiddenTravel",
        "ConfirmedTravelWaitsForRewardsWithoutAnotherClickOrVote",
        "DelveChoosesAcrossNativeZonesWithoutRepeatingAndReplaysSavedRunIdentity",
    )
)


def main():
    scratch = os.environ.get("TMPDIR")
    if not scratch or not Path(scratch).is_dir():
        print("TMPDIR must point to an existing HDD scratch directory.", file=sys.stderr)
        return 2
    if not shutil.which("dotnet"):
        print("The .NET SDK is required.", file=sys.stderr)
        return 2
    work = Path(tempfile.mkdtemp(prefix="issue10-", dir=scratch))
    source = work / "source"
    # Preserve project-relative extraction paths. The existing MSBuild extraction
    # target prepends the project directory even to an absolute IntermediateOutputPath.
    tracked = subprocess.check_output(
        ["git", "ls-files", "-z"], cwd=ROOT
    ).decode().split("\0")
    selected = {
        name
        for name in tracked
        if name.startswith("src/")
        or name.startswith("tests/SodRpg.Mod.Startup.Tests/")
    }
    selected.update((TEST, "tools/balance/essence-caps.json"))
    for name in sorted(selected):
        destination = source / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / name, destination)

    env = dict(os.environ)
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    env["DOTNET_CLI_HOME"] = str(work / "dotnet-home")
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    command = [
        "dotnet", "test", str(source / PROJECT),
        "--results-directory", str(work / "results"),
        "--logger", "trx;LogFileName=issue10.trx",
        "--filter", FILTER, "--nologo", "-v", "minimal",
        "-p:PythonCommand=" + sys.executable, "-p:WarningLevel=0",
    ]
    print("Work directory:", work, flush=True)
    completed = subprocess.run(
        command, cwd=source, env=env, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, text=True,
    )
    (work / "test.log").write_text(completed.stdout, encoding="utf-8")
    print(completed.stdout, end="", flush=True)
    result = {"exit_code": completed.returncode, "command": command}
    trx = work / "results/issue10.trx"
    if trx.exists():
        counters = ET.parse(trx).getroot().find(".//{*}Counters")
        if counters is not None:
            result["counters"] = dict(counters.attrib)
    (work / "results.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print("Recorded result:", work / "results.json")
    # A passing harness does not establish that native Limbo travel works in game.
    return completed.returncode


if __name__ == "__main__":
    sys.exit(main())
