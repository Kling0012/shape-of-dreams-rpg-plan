#!/usr/bin/env python3
"""Numeric public CI plan; project and class identities stay in this repository."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/ci.yml"


def affects_ci(path):
    # Ordered paths from the original push/pull_request workflow.
    if path == "docs/specs/v1.29-uniques-table.md":
        return True
    return not (path.startswith("docs/") or path.endswith(".md")
                or path.startswith("src/SodRpg.Mod/about/"))


def choose_mode(paths, labels=(), main=False):
    """None means the old workflow's path filter would not start CI."""
    if not any(affects_ci(path) for path in paths):
        return None
    return "full" if main or "ci-full" in labels else "quick"


def shards(mode):
    text = WORKFLOW.read_text()
    projects = re.search(r"^        project: \[([^\n]+)\]$", text, re.M)
    if projects is None:
        raise ValueError("missing matrix")
    mods = [value.strip() for value in projects[1].split(",")]
    slow_block = text.split("  slow-tests:", 1)[1]
    classes = re.search(r"^        class:\n((?:          - \S+\n)+)", slow_block, re.M)
    if classes is None:
        raise ValueError("missing matrix")
    result = [{"project": "tests/SodRpg.Core.Tests", "core": True}]
    result.extend({"project": project} for project in mods)
    if mode == "full":
        result.extend({"project": "tests/SodRpg.Core.Tests", "class": name}
                      for name in re.findall(r"^          - (\S+)$", classes[1], re.M))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("quick", "full"), required=True)
    args = parser.parse_args()
    print(json.dumps({"shard": list(range(1, len(shards(args.mode)) + 1))}, separators=(",", ":")))


if __name__ == "__main__":
    main()
