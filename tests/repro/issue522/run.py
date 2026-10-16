#!/usr/bin/env python3
"""Run only issue #522 and retain the original output and per-case failures."""
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]


def main():
    artifacts = Path(__file__).resolve().parent / "artifacts" / datetime.datetime.now(
        datetime.timezone.utc
    ).strftime("%Y%m%dT%H%M%S%fZ")
    artifacts.mkdir(parents=True, exist_ok=False)
    command = [
        "dotnet", "run", "--project", "tests/repro/issue522/Issue522.Repro.csproj",
        "-p:WarningLevel=0",
    ]
    env = os.environ.copy()
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    print("Running: " + " ".join(command), flush=True)
    try:
        completed = subprocess.run(
            command, cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, timeout=600, check=False,
        )
        code, output = completed.returncode, completed.stdout
    except (OSError, subprocess.TimeoutExpired) as error:
        code, output = 2, str(error)
    (artifacts / "repro.log").write_text(output, encoding="utf-8")
    result = {"command": command, "exit_code": code}
    try:
        start = output.index('\n{\n') + 1
        result.update(json.loads(output[start:]))
    except (ValueError, json.JSONDecodeError):
        result["harness_error"] = "No case results: compilation or execution did not finish"
        print(output, flush=True)
    else:
        print(output[:start].rstrip(), flush=True)
        print(f"total={result['total']} passed={result['passed']} failed={result['failed']} exit_code={code}", flush=True)
    (artifacts / "results.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(f"Results: {artifacts}", flush=True)
    if "harness_error" in result:
        return 2
    return 1 if code != 0 or result["failed"] != 0 else 0


if __name__ == "__main__":
    sys.exit(main())
