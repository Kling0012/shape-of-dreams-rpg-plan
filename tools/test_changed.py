#!/usr/bin/env python3
"""Run only the tests affected by the current changes.

Usage:
    python tools/test_changed.py [--base <git ref>] [--list] [--all]

Changed files are collected from ``git diff --name-only <base>...HEAD`` plus
staged/unstaged working-tree changes and untracked .cs files.  They are mapped
to test classes (see ``select_tests``), and the selected classes are run with
``dotnet test <project> --filter FullyQualifiedName~...``.

If nothing test-relevant changed, a message is printed and the exit code is 0.
Otherwise the exit code is the exit code of the last ``dotnet test`` run
(first non-zero exit code if the filter had to be split into several runs).
"""

from __future__ import annotations

import argparse
import fnmatch
import os
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

# A type mentioned in strictly more than this fraction of test files counts as
# "very widely used" and triggers a full run.
WIDE_TYPE_THRESHOLD = 0.6

# Keep each ``dotnet test --filter`` comfortably below the ~32k Windows
# command-line limit; longer selections are chunked into several runs.
MAX_FILTER_CHARS = 15000

# Non-code directories whose files are read by tests at run time.
DATA_DIRS = ("tools/star-manifest", "tools/lowrarity")

TYPE_DECL_RE = re.compile(
    r"\b(?:class|struct|enum|interface|record)(?:\s+(?:class|struct))?\s+([A-Za-z_]\w*)"
)
CLASS_DECL_RE = re.compile(r"\bclass\s+([A-Za-z_]\w*)")
SUMMARY_RE = re.compile(r"Failed:\s*(\d+)[,，]?\s*Passed:\s*(\d+)")
NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_][\w.]*)")
COMMENT_RE = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)

# Identifiers that can follow a type keyword position but are never type names.
NON_TYPE_NAMES = {"class", "struct", "enum", "interface", "record", "namespace"}


def strip_comments(text: str) -> str:
    """Remove // line and /* */ block comments (best effort, strings ignored)."""
    return COMMENT_RE.sub("", text)


def declared_types(content: str) -> list[str]:
    """Type names (class/struct/enum/interface/record) declared in C# source.

    Partial declarations yield the name once.
    """
    names = []
    for match in TYPE_DECL_RE.finditer(strip_comments(content)):
        name = match.group(1)
        if name not in NON_TYPE_NAMES and name not in names:
            names.append(name)
    return names


def declared_test_classes(content: str) -> list[str]:
    """Fully-qualified names of the classes declared in a test .cs file."""
    stripped = strip_comments(content)
    namespace_match = NAMESPACE_RE.search(stripped)
    namespace = namespace_match.group(1) if namespace_match else ""
    classes = []
    for match in CLASS_DECL_RE.finditer(stripped):
        name = match.group(1)
        if name in NON_TYPE_NAMES:
            continue
        fqn = f"{namespace}.{name}" if namespace else name
        if fqn not in classes:
            classes.append(fqn)
    return classes


def mentions_any(content: str, names: list[str]) -> bool:
    """True if any name occurs in content as a whole word."""
    for name in names:
        if re.search(rf"\b{re.escape(name)}\b", content):
            return True
    return False


def is_generated_cs(path: str) -> bool:
    """True for generated .cs files (e.g. Foo.Generated.cs, GeneratedBar.cs)."""
    name = path.rsplit("/", 1)[-1]
    return name.startswith("Generated") or fnmatch.fnmatch(name, "*.Generated.cs")


def is_build_file(path: str) -> bool:
    """True for *.csproj and Directory.Build.* files."""
    name = path.rsplit("/", 1)[-1]
    return path.endswith(".csproj") or name.startswith("Directory.Build.")


def is_data_file(path: str) -> bool:
    """True for the JSON data files tests read at run time."""
    return any(path.startswith(f"{d}/") for d in DATA_DIRS) and path.endswith(".json")


def references_path(content: str, path: str) -> bool:
    """True if a test file references the changed data file.

    Matches either the whole relative path (either separator) or both the
    containing directory name and the file name, so ``Path.Combine``-style
    references (``Path.Combine(root, "tools", "star-manifest", "outer.json")``)
    are found too.
    """
    parts = [p for p in path.split("/") if p]
    if len(parts) < 2:
        return False
    directory, base = parts[-2], parts[-1]
    if path in content or path.replace("/", "\\") in content:
        return True
    return directory in content and base in content


class Selection:
    """Result of mapping changed files to test classes."""

    def __init__(self) -> None:
        self.run_all = False
        self.reasons: list[str] = []
        self.classes: set[str] = set()


def select_tests(changed, files) -> Selection:
    """Map changed repo-relative paths to test classes.

    ``files`` maps repo-relative posix paths to file contents; it must include
    every .cs file under tests/ (the corpus) and the changed .cs files.

    Rules:
      1. a changed test .cs under tests/ selects the classes it declares;
      2. a changed production .cs under src/ selects, for each declared type,
         every test file mentioning the type as a whole word -> its classes;
      3. changed data files (tools/star-manifest/*.json, tools/lowrarity/*.json,
         generated .cs) select test files that reference their path or types;
      4. build files (*.csproj, Directory.Build.*) or types used by more than
         60% of the test files select everything.
    """
    selection = Selection()
    test_files = sorted(p for p in files if p.startswith("tests/") and p.endswith(".cs"))

    for path in sorted(changed):
        content = files.get(path, "")
        if is_build_file(path):
            selection.run_all = True
            selection.reasons.append(f"build file changed: {path}")
            continue
        if path.startswith("tests/") and path.endswith(".cs"):
            # Rule 1: the test file itself changed.
            selection.classes.update(declared_test_classes(content))
        if path.startswith("src/") and path.endswith(".cs") and content:
            # Rule 2 (+ the wide-type part of rule 4).
            types = declared_types(content)
            if types:
                hits = [tf for tf in test_files if mentions_any(files[tf], types)]
                if test_files and len(hits) > WIDE_TYPE_THRESHOLD * len(test_files):
                    selection.run_all = True
                    selection.reasons.append(
                        f"{path}: types {', '.join(types[:3])}... used in "
                        f"{len(hits)}/{len(test_files)} test files"
                    )
                else:
                    for tf in hits:
                        selection.classes.update(declared_test_classes(files[tf]))
        if (is_data_file(path) or is_generated_cs(path)) and content is not None:
            # Rule 3: test files that reference the changed data/generated file.
            for tf in test_files:
                if references_path(files[tf], path):
                    selection.classes.update(declared_test_classes(files[tf]))

    if selection.run_all:
        every = set()
        for tf in test_files:
            every.update(declared_test_classes(files[tf]))
        selection.classes = every
    return selection


def build_filters(classes, max_chars: int = MAX_FILTER_CHARS) -> list[str]:
    """Chunk ``FullyQualifiedName~X`` terms into filter expressions."""
    terms = [f"FullyQualifiedName~{c}" for c in sorted(classes)]
    chunks: list[str] = []
    current: list[str] = []
    for term in terms:
        joined = "|".join(current + [term])
        if current and len(joined) > max_chars:
            chunks.append("|".join(current))
            current = [term]
        else:
            current.append(term)
    if current:
        chunks.append("|".join(current))
    return chunks


# ---------------------------------------------------------------- git helpers


def run_git(root: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", "-c", "core.quotepath=false", *args],
        cwd=str(root), capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"git {' '.join(args)} failed ({result.returncode}): {result.stderr.strip()}"
        )
    return result.stdout


def default_base(root: Path) -> str:
    """Merge-base with origin/main; fall back to HEAD (uncommitted work only)."""
    try:
        base = run_git(root, "merge-base", "HEAD", "origin/main").strip()
        if base:
            return base
    except RuntimeError:
        pass
    return run_git(root, "rev-parse", "HEAD").strip()


def collect_changed(root: Path, base: str) -> list[str]:
    """Committed changes since base, plus staged/unstaged/untracked ones."""
    changed = set()
    for line in run_git(root, "diff", "--name-only", f"{base}...HEAD").splitlines():
        changed.add(line.strip().replace("\\", "/"))
    for line in run_git(root, "diff", "--name-only", "HEAD").splitlines():
        changed.add(line.strip().replace("\\", "/"))
    for line in run_git(root, "ls-files", "--others", "--exclude-standard").splitlines():
        path = line.strip().replace("\\", "/")
        if path.endswith(".cs"):
            changed.add(path)
    return sorted(p for p in changed if p and not p.startswith(".ref/"))


def read_file(root: Path, relpath: str) -> str | None:
    try:
        return (root / relpath).read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return None


def scan_test_files(root: Path) -> dict[str, str]:
    """Contents of every .cs file under tests/ (excluding bin/obj)."""
    files = {}
    tests_dir = root / "tests"
    for path in tests_dir.rglob("*.cs"):
        rel = path.relative_to(root).as_posix()
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        files[rel] = read_file(root, rel) or ""
    return files


def collect_files(root: Path, changed) -> dict[str, str]:
    """Test corpus plus the contents of the changed .cs files."""
    files = dict(scan_test_files(root))
    for path in changed:
        if path.endswith(".cs") and path not in files:
            content = read_file(root, path)
            if content is not None:
                files[path] = content
    return files


def find_test_projects(root: Path) -> list[str]:
    projects = [
        path.relative_to(root).as_posix()
        for path in (root / "tests").rglob("*.csproj")
        if not any(part in ("bin", "obj") for part in path.parts)
    ]
    return sorted(projects)


# ------------------------------------------------------------------- running


def resolve_dotnet() -> tuple[str, str]:
    """(dotnet executable, DOTNET_ROOT). DOTNET env var overrides the default."""
    override = os.environ.get("DOTNET")
    if override:
        return override, os.path.dirname(os.path.abspath(override))
    sdk = Path(os.environ.get("LOCALAPPDATA", "")) / "dotnet-sdk"
    return str(sdk / "dotnet.exe"), str(sdk)


def parse_summary(output: str) -> tuple[int, int]:
    """(failed, passed) from the dotnet test console summary, if present."""
    matches = SUMMARY_RE.findall(output)
    if not matches:
        return (0, 0)
    failed, passed = matches[-1]
    return int(failed), int(passed)


def run_dotnet_test(root: Path, project: str, filter_expr: str | None) -> tuple[int, str]:
    dotnet, dotnet_root = resolve_dotnet()
    if not Path(dotnet).exists():
        print(f"dotnet not found at {dotnet} (set DOTNET to override)", file=sys.stderr)
        return 1, ""
    cmd = [dotnet, "test", project]
    if filter_expr:
        cmd += ["--filter", filter_expr]
    env = dict(os.environ)
    env["DOTNET_ROOT"] = dotnet_root
    # Keep the console summary machine-parseable regardless of the OS locale.
    env.setdefault("DOTNET_CLI_UI_LANGUAGE", "en")
    result = subprocess.run(
        cmd, cwd=str(root), env=env, stdin=subprocess.DEVNULL,
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    output = (result.stdout or "") + (result.stderr or "")
    print(output, end="" if output.endswith("\n") else "\n")
    return result.returncode, output


def run_selection(root: Path, classes, run_all: bool) -> int:
    """Run the selected classes (or everything); returns the exit code."""
    projects = find_test_projects(root)
    if not projects:
        print("no test projects found under tests/", file=sys.stderr)
        return 1
    filters = [None] if run_all else build_filters(classes)
    exit_code = 0
    total_failed = total_passed = 0
    for project in projects:
        for index, filter_expr in enumerate(filters, start=1):
            if len(filters) > 1:
                print(f"running {project} ({index}/{len(filters)})", file=sys.stderr)
            code, output = run_dotnet_test(root, project, filter_expr)
            if code != 0:
                exit_code = exit_code or code
            failed, passed = parse_summary(output)
            total_failed += failed
            total_passed += passed
    print(f"selected classes: {'all' if run_all else len(classes)}")
    print(f"passed: {total_passed}  failed: {total_failed}")
    return exit_code


# ---------------------------------------------------------------------- main


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(
        description="Run only the tests affected by the current changes."
    )
    parser.add_argument("--base", metavar="REF",
                        help="git ref to diff against (default: merge-base with origin/main)")
    parser.add_argument("--list", action="store_true",
                        help="only print the selected test classes")
    parser.add_argument("--all", action="store_true",
                        help="run the full test suite")
    args = parser.parse_args(argv)

    root = REPO_ROOT
    try:
        if args.base:
            run_git(root, "rev-parse", "--verify", f"{args.base}^{{commit}}")
            base = args.base
        else:
            base = default_base(root)
        changed = collect_changed(root, base)
    except RuntimeError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(f"base: {base}  changed files: {len(changed)}", file=sys.stderr)

    if args.all:
        if args.list:
            classes = sorted(
                {name for content in scan_test_files(root).values()
                 for name in declared_test_classes(content)}
            )
            for name in classes:
                print(name)
            print(f"selected: {len(classes)} classes (full run)")
            return 0
        return run_selection(root, None, run_all=True)

    files = collect_files(root, changed)
    selection = select_tests(changed, files)
    for reason in selection.reasons:
        print(f"full run: {reason}", file=sys.stderr)

    if selection.run_all:
        print(f"selected: {len(selection.classes)} classes (full run)")
        if args.list:
            for name in sorted(selection.classes):
                print(name)
            return 0
        return run_selection(root, None, run_all=True)

    if not selection.classes:
        print("nothing relevant changed; no tests to run")
        return 0

    if args.list:
        for name in sorted(selection.classes):
            print(name)
        print(f"selected: {len(selection.classes)} classes")
        return 0

    print(f"selected classes: {len(selection.classes)}")
    return run_selection(root, selection.classes, run_all=False)


if __name__ == "__main__":
    sys.exit(main())
