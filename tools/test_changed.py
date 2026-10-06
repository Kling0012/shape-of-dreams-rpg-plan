#!/usr/bin/env python3
"""Run only the tests affected by the current changes.

Usage:
    python tools/test_changed.py [--base <git ref>] [--list] [--all] [--slow]

Changed files are collected from ``git diff --name-only <base>...HEAD`` plus
staged/unstaged working-tree changes and untracked C#/data/generator files. They are mapped
to test classes (see ``select_tests``); for production files the unified diff
narrows the mapping to touched tokens (members, string constants), retaining
file-level type dependencies for object initialization changes.  The selected
classes run with ``dotnet test <project> --filter ...``.

The three test projects serialize their own tests (shared game state), so the
projects are built serially and then run in parallel for wall-clock time.
``--slow`` additionally enables the Speed=Slow exhaustive tests (SODRPG_SLOW=1).

If nothing test-relevant changed, a message is printed and the exit code is 0.
Otherwise the exit code is the first non-zero ``dotnet test`` exit code.
"""

from __future__ import annotations

import argparse
import concurrent.futures
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
DATA_DIRS = ("tools/star-manifest", "tools/lowrarity", "tools/balance")

TYPE_DECL_RE = re.compile(
    r"\b(?:class|struct|enum|interface|record)(?:\s+(?:class|struct))?\s+([A-Za-z_]\w*)"
)
CLASS_DECL_RE = re.compile(r"\bclass\s+([A-Za-z_]\w*)")
SUMMARY_RE = re.compile(r"Failed:\s*(\d+)[,，]?\s*Passed:\s*(\d+)")
NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_][\w.]*)")
# Identifiers worth mapping to tests when a diff narrows a production change:
# PascalCase tokens (types, methods, properties) and string constants (content IDs, names).
CANDIDATE_RE = re.compile(r"\b[A-Z][A-Za-z0-9_]{2,}\b")
LITERAL_RE = re.compile(r'"([^"\\]{4,})"')
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


def unified_diff_plus_minus(diff_text: str) -> tuple[list[str], list[str]]:
    """(+lines, -lines) from a unified diff; file headers are skipped."""
    plus, minus = [], []
    for line in diff_text.splitlines():
        if line.startswith(("+++", "---")):
            continue
        if line.startswith("+"):
            plus.append(line[1:])
        elif line.startswith("-"):
            minus.append(line[1:])
    return plus, minus


def diff_candidates(plus: list[str], minus: list[str]) -> set[str] | None:
    """Tokens (types, members, string constants) touched by a change.

    None when the change is comments/whitespace only, i.e. it cannot alter
    behavior and no test needs to run.
    """
    def code(lines):
        return [line for line in map(strip_comments, lines) if line.strip()]

    new, old = code(plus), code(minus)
    if not new and not old:
        return None
    candidates: set[str] = set()
    for line in new + old:
        candidates.update(CANDIDATE_RE.findall(line))
        candidates.update(LITERAL_RE.findall(line))
    return candidates


class Selection:
    """Result of mapping changed files to test classes."""

    def __init__(self) -> None:
        self.run_all = False
        self.reasons: list[str] = []
        self.classes: set[str] = set()


def select_tests(changed, files, diffs=None) -> Selection:
    """Map changed repo-relative paths to test classes.

    ``files`` maps repo-relative posix paths to file contents; it must include
    every .cs file under tests/ (the corpus) and the changed .cs files.
    ``diffs`` optionally maps changed src/ paths to unified-diff text; when
    present it narrows rule 2 to the tokens the change actually touches.

    Rules:
      1. a changed test .cs under tests/ selects the classes it declares;
      2. a changed production .cs under src/ selects, for each declared type,
         every test file mentioning the type as a whole word -> its classes;
         with a diff, use the tokens (members, string constants) touched by the
         change, retaining file types for object initialization or unrecognized
         changes; a widely used declared type still selects everything;
      3. changed data files (tools/star-manifest/*.json, tools/lowrarity/*.json,
         tools/balance/*.json, generated .cs) select tests referencing their path or types;
      4. build files (*.csproj, Directory.Build.*), balance inputs, or types
         used by more than 60% of the test files select everything.
    """
    selection = Selection()
    test_files = sorted(p for p in files if p.startswith("tests/") and p.endswith(".cs"))

    for path in sorted(changed):
        content = files.get(path, "")
        if is_build_file(path):
            selection.run_all = True
            selection.reasons.append(f"build file changed: {path}")
            continue
        if path in ("tools/balance/forge.json", "tools/balance/stars.json",
                    "tools/balance/run-growth.json", "tools/balance/star-progression.json",
                    "tools/balance/star_progression_values.py",
                    "tools/balance/gen_cs.py", "tools/balance/star_values.py",
                    "src/SodRpg.Core/Game/Balance/Forge.Generated.cs",
                    "src/SodRpg.Core/Game/Balance/Stars.Generated.cs"):
            selection.run_all = True
            selection.reasons.append(f"balance input changed: {path}")
            continue
        if path.startswith("tests/") and path.endswith(".cs"):
            # Rule 1: the test file itself changed.
            selection.classes.update(declared_test_classes(content))
        if path.startswith("src/") and path.endswith(".cs") and content:
            # Rule 2 (+ the wide-type part of rule 4).
            types = declared_types(content)
            diff = (diffs or {}).get(path)
            if diff is not None:
                plus, minus = unified_diff_plus_minus(diff)
                candidates = diff_candidates(plus, minus)
                if candidates is None:
                    selection.reasons.append(f"{path}: comment/whitespace-only change")
                    continue
                creates_objects = any(re.search(r"\bnew\b", strip_comments(line))
                                      for line in plus + minus)
                if types and (creates_objects or candidates.isdisjoint(types)):
                    # Constructor types do not identify the owning catalog.
                    # Keep its readers even when expectations come from a table;
                    # unrecognized hunks also retain file-level dependencies.
                    candidates.update(types)
                mentions = {tf: {c for c in candidates
                                 if re.search(rf"\b{re.escape(c)}\b", files[tf])}
                            for tf in test_files}
                wide = {c for c, count in
                        ((c, sum(1 for tf in test_files if c in mentions[tf])) for c in candidates)
                        if test_files and count > WIDE_TYPE_THRESHOLD * len(test_files)}
                if types and wide & set(types):
                    # The change touches a declaration of a type most tests use.
                    selection.run_all = True
                    selection.reasons.append(
                        f"{path}: types {', '.join(sorted(wide & set(types))[:3])} used in "
                        f"most test files"
                    )
                    continue
                usable = candidates - wide
                for tf in test_files:
                    if mentions[tf] & usable:
                        selection.classes.update(declared_test_classes(files[tf]))
                continue
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
        if path.endswith(".cs") or is_data_file(path) or path in ("tools/balance/gen_cs.py", "tools/balance/star_values.py",
                                                               "tools/balance/star_progression_values.py"):
            changed.add(path)
    return sorted(p for p in changed if p and not p.startswith(".ref/"))


def collect_diffs(root: Path, base: str, changed) -> dict[str, str]:
    """Unified-diff text per changed src/ .cs path ("" when the diff is empty).

    Committed and working-tree changes are concatenated; an untracked file is
    treated as fully added so its tokens can be mapped like any other change.
    """
    diffs = {}
    untracked = {line.strip().replace("\\", "/")
                 for line in run_git(root, "ls-files", "--others", "--exclude-standard").splitlines()}
    for path in changed:
        if not path.startswith("src/") or not path.endswith(".cs"):
            continue
        text = run_git(root, "diff", "--no-color", "--unified=0", f"{base}...HEAD", "--", path)
        text += run_git(root, "diff", "--no-color", "--unified=0", "HEAD", "--", path)
        if not text.strip() and path in untracked:
            content = read_file(root, path)
            if content is not None:
                text = "\n".join("+" + line for line in content.splitlines())
        diffs[path] = text
    return diffs


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


def run_dotnet_test(root: Path, project: str, filter_expr: str | None,
                    no_build: bool = False, slow: bool = False) -> tuple[int, str]:
    dotnet, dotnet_root = resolve_dotnet()
    if not Path(dotnet).exists():
        print(f"dotnet not found at {dotnet} (set DOTNET to override)", file=sys.stderr)
        return 1, ""
    cmd = [dotnet, "test", project, "-c", "Release"]
    if filter_expr:
        cmd += ["--filter", filter_expr]
    if no_build:
        cmd.append("--no-build")
    env = dict(os.environ)
    env["DOTNET_ROOT"] = dotnet_root
    # Keep the console summary machine-parseable regardless of the OS locale.
    env.setdefault("DOTNET_CLI_UI_LANGUAGE", "en")
    if slow:
        env["SODRPG_SLOW"] = "1"
    result = subprocess.run(
        cmd, cwd=str(root), env=env, stdin=subprocess.DEVNULL,
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    return result.returncode, (result.stdout or "") + (result.stderr or "")


def build_projects(root: Path, projects: list[str]) -> int:
    """Build every test project serially (shared obj/ dirs must not race)."""
    dotnet, dotnet_root = resolve_dotnet()
    if not Path(dotnet).exists():
        print(f"dotnet not found at {dotnet} (set DOTNET to override)", file=sys.stderr)
        return 1
    env = dict(os.environ)
    env["DOTNET_ROOT"] = dotnet_root
    env.setdefault("DOTNET_CLI_UI_LANGUAGE", "en")
    for project in projects:
        print(f"building {project}", file=sys.stderr)
        result = subprocess.run(
            [dotnet, "build", project, "-c", "Release"], cwd=str(root), env=env,
            stdin=subprocess.DEVNULL, capture_output=True, text=True,
            encoding="utf-8", errors="replace",
        )
        if result.returncode != 0:
            print((result.stdout or "") + (result.stderr or ""))
            return result.returncode
    return 0


def run_selection(root: Path, classes, run_all: bool, slow: bool = False) -> int:
    """Run the selected classes (or everything); returns the exit code.

    Projects are built serially, then their tests run in parallel: the three
    test projects serialize their own tests (shared registry state), so the
    wall-clock win comes from overlapping the projects.
    """
    projects = find_test_projects(root)
    if not projects:
        print("no test projects found under tests/", file=sys.stderr)
        return 1
    code = build_projects(root, projects)
    if code != 0:
        return code
    filters = [None] if run_all else build_filters(classes)
    pairs = [(project, index, expr)
             for project in projects for index, expr in enumerate(filters, start=1)]
    exit_code = 0
    total_failed = total_passed = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(projects)) as pool:
        futures = {pool.submit(run_dotnet_test, root, project, expr, True, slow):
                   (project, index) for project, index, expr in pairs}
        for future, (project, index) in sorted(futures.items(), key=lambda item: item[1][1]):
            code, output = future.result()
            header = project if len(filters) == 1 else f"{project} ({index}/{len(filters)})"
            print(f"===== {header} =====")
            print(output, end="" if output.endswith("\n") else "\n")
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
    parser.add_argument("--slow", action="store_true",
                        help="also run Speed=Slow exhaustive tests (SODRPG_SLOW=1)")
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
        return run_selection(root, None, run_all=True, slow=args.slow)

    files = collect_files(root, changed)
    try:
        diffs = collect_diffs(root, base, changed)
    except RuntimeError as error:
        print(f"warning: diff collection failed ({error}); using file-level mapping", file=sys.stderr)
        diffs = None
    selection = select_tests(changed, files, diffs)
    for reason in selection.reasons:
        print(f"full run: {reason}", file=sys.stderr)

    if selection.run_all:
        print(f"selected: {len(selection.classes)} classes (full run)")
        if args.list:
            for name in sorted(selection.classes):
                print(name)
            return 0
        return run_selection(root, None, run_all=True, slow=args.slow)

    if not selection.classes:
        print("nothing relevant changed; no tests to run")
        return 0

    if args.list:
        for name in sorted(selection.classes):
            print(name)
        print(f"selected: {len(selection.classes)} classes")
        return 0

    print(f"selected classes: {len(selection.classes)}")
    return run_selection(root, selection.classes, run_all=False, slow=args.slow)


if __name__ == "__main__":
    sys.exit(main())
