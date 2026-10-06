"""Measure stage 6 in isolated Core processes, using the shared baseline transaction."""
import json
from decimal import Decimal

MODES = ("loot-economy", "pact-daily-waypoints", "events")


def measure(dotnet, dll, staging, execute, env):
    snapshots = []
    for mode in MODES:
        path = staging / f"{mode}.json"
        execute([dotnet, str(dll), "--mode", mode,
                 "--out", str(staging / f"{mode}.md"), "--metrics-json", str(path)], env)
        snapshots.append(json.loads(path.read_text(encoding="utf-8"), parse_float=Decimal))
    return snapshots
