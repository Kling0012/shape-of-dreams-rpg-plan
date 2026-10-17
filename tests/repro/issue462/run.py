#!/usr/bin/env python3
"""Run only the issue #462 reproductions; record all results before returning failure."""

import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[3]
ARTIFACTS = Path(__file__).resolve().parent / "artifacts" / datetime.datetime.now(
    datetime.timezone.utc
).strftime("%Y%m%dT%H%M%S%fZ")
CASES = (
    ("core", "tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj", "FullyQualifiedName~Issue462ReproductionTests"),
    ("omens", "tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj", "FullyQualifiedName~RoomOmensHostTests"),
)


def main():
    ARTIFACTS.mkdir(parents=True, exist_ok=False)
    env = os.environ.copy()
    # This host has .NET 10 only; allow net8 testhost to use the installed runtime.
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    results = []
    for name, project, test_filter in CASES:
        command = [
            "dotnet", "test", project, "--filter", test_filter,
            "--logger", "console;verbosity=normal",
            "--results-directory", str(ARTIFACTS),
            "--logger", f"trx;LogFileName={name}.trx",
            "-p:PythonCommand=python3", "-p:WarningLevel=0",
        ]
        print(f"Running {name}: {' '.join(command)}", flush=True)
        try:
            completed = subprocess.run(
                command, cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT, timeout=600, check=False,
            )
            code, output = completed.returncode, completed.stdout
        except (OSError, subprocess.TimeoutExpired) as error:
            code, output = 2, str(error)
        (ARTIFACTS / f"{name}.log").write_text(output, encoding="utf-8")
        result = {"name": name, "command": command, "exit_code": code}
        trx = ARTIFACTS / f"{name}.trx"
        if trx.exists():
            counters = ET.parse(trx).find(".//{*}Counters")
            if counters is not None:
                result["counts"] = dict(counters.attrib)
        results.append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    (ARTIFACTS / "results.json").write_text(
        json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(f"Results: {ARTIFACTS}", flush=True)
    return 1 if any(result["exit_code"] != 0 for result in results) else 0


if __name__ == "__main__":
    sys.exit(main())
