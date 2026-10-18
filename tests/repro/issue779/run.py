#!/usr/bin/env python3
"""Run #779 only: 1 = reproduced assertion, 2 = harness/build error."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]


def main():
    command = [
        "dotnet", "run", "--project", "tests/repro/issue779/Issue779.Repro.csproj",
        "-c", "Release", "-p:WarningLevel=0",
    ]
    env = os.environ.copy()
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    print("Running: " + " ".join(command), flush=True)
    try:
        completed = subprocess.run(
            command, cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, timeout=300, check=False,
        )
        code, output = completed.returncode, completed.stdout
    except (OSError, subprocess.TimeoutExpired) as error:
        code, output = 2, str(error)
    print(output, end="" if output.endswith("\n") else "\n", flush=True)
    result = {"command": command, "exit_code": code}
    match = re.search(r"^RESULT passed=(\d+) failed=(\d+)$", output, re.MULTILINE)
    if match:
        result.update(passed=int(match[1]), failed=int(match[2]))
    if not match or re.search(r"^FAIL (?!REPRO )", output, re.MULTILINE):
        result["harness_error"] = "Build, execution or control scenario failed"
    artifacts = ROOT / ".ci-logs" / "issue779"
    artifacts.mkdir(parents=True, exist_ok=True)
    (artifacts / "repro.log").write_text(output, encoding="utf-8")
    (artifacts / "results.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(f"Results: {artifacts}", flush=True)
    if "harness_error" in result or code not in (0, 1):
        return 2
    return 1 if code != 0 or result["failed"] != 0 else 0


if __name__ == "__main__":
    sys.exit(main())
