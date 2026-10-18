#!/usr/bin/env python3
"""Run one numeric shard, retaining diagnostics only in private log files."""
import contextlib
import json
import os
import re
import subprocess
import sys
import traceback
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from plan import ROOT, shards


def run(number, directory):
    selected = shards("full")[number - 1]
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    env.pop("SODRPG_SLOW", None)
    if "class" in selected:
        env["SODRPG_SLOW"] = "1"
    passed = failed = 0
    okay = True
    with (directory / "output.log").open("w") as output:
        def command(args):
            nonlocal okay
            output.write("$ " + " ".join(args) + "\n")
            output.flush()
            result = subprocess.run(args, cwd=ROOT, env=env, stdout=output, stderr=output)
            okay = okay and result.returncode == 0
            return result.returncode == 0

        if selected.get("core"):
            command([sys.executable, "tools/balance/gen_cs.py", "--check"])
            command([sys.executable, "tools/test_changed.py", "--check-ci-matrix"])
            suite = unittest.defaultTestLoader.discover(str(ROOT / "tools/tests"), pattern="test_*.py")
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                result = unittest.TextTestRunner(stream=output, verbosity=1).run(suite)
            failed += len(result.failures) + len(result.errors)
            passed += result.testsRun - len(result.skipped) - len(result.failures) - len(result.errors)
            okay = okay and result.wasSuccessful()
        project = selected["project"]
        if command(["dotnet", "build", "-c", "Release", project]):
            args = ["dotnet", "test", "-c", "Release", project, "--no-build",
                    "--logger", "trx;LogFileName=results.trx", "--results-directory", str(directory)]
            if "class" in selected:
                name = selected["class"]
                args += ["--filter", f"Speed=Slow&(FullyQualifiedName~{name}.|FullyQualifiedName~{name}+)"]
            command(args)
            counters = ET.parse(directory / "results.trx").find(".//{*}Counters")
            if counters is None or int(counters.get("executed", "0")) == 0:
                okay = False
            else:
                passed += int(counters.get("passed", "0"))
                failed += int(counters.get("failed", "0"))
                okay = okay and failed == 0
        if selected.get("core"):
            command(["dotnet", "build", "-c", "Release", "tools/WikiGen"])
    return okay, passed, failed


def main():
    os.umask(0o077)
    number = int(sys.argv[1]) if len(sys.argv) == 2 and re.fullmatch(r"[1-9][0-9]*", sys.argv[1]) else 0
    okay, passed, failed = False, 0, 0
    directory = Path(os.environ.get("CI_LOG_DIR", str(ROOT / ".ci-logs"))).resolve() / f"shard-{number}"
    try:
        directory.mkdir(parents=True, exist_ok=True)
        # Capture import/build/helper exceptions too. No source diagnostics reach the console.
        with (directory / "runner.log").open("w") as diagnostic:
            with contextlib.redirect_stdout(diagnostic), contextlib.redirect_stderr(diagnostic):
                try:
                    if number < 1 or number > len(shards("full")):
                        raise ValueError("invalid shard")
                    okay, passed, failed = run(number, directory)
                except Exception:
                    traceback.print_exc()
        (directory / "result.json").write_text(json.dumps({
            "shard": number, "ok": okay, "passed": passed, "failed": failed}) + "\n")
    except Exception:
        pass
    print(f"shard {number}: {'ok' if okay else 'fail'}, passed {passed}, failed {failed}")
    return 0 if okay else 1


if __name__ == "__main__":
    sys.exit(main())
