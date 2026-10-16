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

The test projects serialize their own tests (shared game state:
DisableTestParallelization), so the projects are built serially and their tests
run as parallel ``dotnet test`` processes (at most one per CPU core). The Core
suite is additionally split into class-filtered processes: each of the five
heavyweight classes anchors one process and the remaining classes are
distributed over the same processes by test-method count. With ``--slow`` (or
the ``SODRPG_SLOW=1`` environment variable, which the release scripts and CI
set instead of the flag), every Speed=Slow test method anchors a process of
its own (SODRPG_SLOW=1), so the exhaustive tests of one class overlap too
instead of serializing behind the slowest class; the class-group filters
subtract exactly those methods. The two extended equivalence methods further
partition complete hero/seed sequences over 16 processes each, retaining all
steps and assertions. Timing-threshold classes run in one lone process after
every parallel batch has finished, so overlapping processes cannot push their
measured loops over the limit. Together the processes cover every scenario of
a single unfiltered run (the two sharded methods report one result per shard).

If nothing test-relevant changed, a message is printed and the exit code is 0.
Otherwise the exit code is the first non-zero ``dotnet test`` exit code.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import contextvars
import fnmatch
import functools
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
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

# The Core project is split into parallel per-class-filter processes (#404):
# each test assembly serializes its own tests (shared Loc/StarClusters state),
# so the wall-clock win comes from separate processes. The five classes below
# take 62% of the suite's serial run time and #404 keeps them in separate
# processes; five is also the smallest process count that allows that, so the
# split runs Core over exactly five processes.
CORE_TESTS_PROJECT = "tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj"
HEAVY_CORE_CLASSES = frozenset({
    "SodRpg.Core.Tests.AllocationEquivalenceV131Tests",
    "SodRpg.Core.Tests.GeneratedHeroAcceptanceTests",
    "SodRpg.Core.Tests.StarMapReachabilityTests",
    "SodRpg.Core.Tests.PressureRewardMultiplierTests",
    "SodRpg.Core.Tests.StarMapOptimizeCommandTests",
})
CORE_PROCESS_COUNT = 5

# These methods contain independent hero/seed sequences. Keep every sequence,
# but avoid one serialized test becoming the critical path of the release gate.
EQUIVALENCE_SHARDS = {
    "SodRpg.Core.Tests.AllocationEquivalenceV131Tests."
    "Extended_random_equivalence_over_the_generated_v131_hero_trees": 16,
    "SodRpg.Core.Tests.AllocationEquivalenceV131Tests."
    "Extended_random_equivalence_over_every_real_hero_the_synthetic_tree_and_the_authored_probe": 16,
}
_EQUIVALENCE_SHARD = contextvars.ContextVar("equivalence_shard", default=None)

# Threshold-sensitive timing tests (ms per kill / ms per build upper bounds).
# Their measurements are only reliable without CPU contention, so these classes
# run in one lone process each after every parallel batch has finished; the
# union of the runs still selects exactly the tests one unfiltered run would.
ISOLATED_CORE_CLASSES = frozenset({
    "SodRpg.Core.Tests.PerformanceTests",
})

TYPE_DECL_RE = re.compile(
    r"\b(?:class|struct|enum|interface|record)(?:\s+(?:class|struct))?\s+([A-Za-z_]\w*)"
)
CLASS_DECL_RE = re.compile(r"\bclass\s+([A-Za-z_]\w*)")
# A class declaration line: leading whitespace, optional modifiers, ``class``.
# Namespace-level classes are the declarations at the smallest indentation of
# their namespace segment (nested helper classes sit strictly deeper), so the
# scan follows whatever indentation a file uses instead of assuming one.
CLASS_LINE_RE = re.compile(
    r"^([ \t]*)(?:(?:public|internal|protected|private|sealed|static|abstract|partial)\s+)*"
    r"class\s+([A-Za-z_]\w*)",
    re.M,
)
SLOW_TEST_RE = re.compile(r'\[\s*SlowFact\b|Trait\(\s*"Speed"\s*,\s*"Slow"\s*\)')
# A Slow test's method signature: the first test-method name after its marker.
SLOW_METHOD_RE = re.compile(r'\b(?:void|Task|ValueTask)\s+([A-Za-z_]\w*)\s*\(')
TEST_METHOD_RE = re.compile(r"\[\s*(?:SlowFact|Fact|Theory)\b")
COMPILE_INCLUDE_RE = re.compile(r'<Compile\s+Include="([^"]+)"', re.IGNORECASE)
COMPILE_REMOVE_RE = re.compile(r'<Compile\s+Remove="([^"]+)"', re.IGNORECASE)
NO_DEFAULT_COMPILE_RE = re.compile(
    r"<EnableDefaultCompileItems\s+>\s*false\s*</", re.IGNORECASE)
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


def top_level_test_classes(content: str) -> list[str]:
    """Fully-qualified names of the namespace-level classes of a test .cs file.

    Unlike ``declared_test_classes`` this excludes nested helper classes, whose
    real fully-qualified names (``Outer+Nested``) never match a filter anyway.
    A file may declare several namespaces (stub api + the real test classes);
    each declaration owns the segment up to the next one, so every class keeps
    the namespace it is declared in. Within a segment the namespace-level
    classes are the ones at the smallest indentation, whatever that indentation
    is. Files without any class declaration line fall back to the inclusive
    scan.
    """
    stripped = strip_comments(content)
    declarations = list(NAMESPACE_RE.finditer(stripped))
    segments = ([(None, stripped)] if not declarations else
                [(match.group(1), stripped[match.end(): declarations[i + 1].start()
                                               if i + 1 < len(declarations)
                                               else len(stripped)])
                 for i, match in enumerate(declarations)])
    classes: list[str] = []
    for namespace, segment in segments:
        found = CLASS_LINE_RE.findall(segment)
        if not found:
            continue
        top = min(len(indent.replace("\t", "    ")) for indent, _ in found)
        for indent, name in found:
            if len(indent.replace("\t", "    ")) != top:
                continue
            fqn = f"{namespace}.{name}" if namespace else name
            if fqn not in classes:
                classes.append(fqn)
    if not classes:
        return declared_test_classes(content)
    return classes


def slow_test_classes(files: dict[str, str]) -> set[str]:
    """Classes holding Speed=Slow tests ([SlowFact] / Trait("Speed","Slow"))).

    Used by ``--check-ci-matrix`` to compare against the ci.yml slow-tests
    matrix, which runs whole slow classes.
    """
    classes: set[str] = set()
    for path, content in files.items():
        if path.startswith("tests/") and path.endswith(".cs") \
                and SLOW_TEST_RE.search(strip_comments(content)):
            classes.update(top_level_test_classes(content))
    return classes


def slow_test_methods(files: dict[str, str]) -> dict[str, set[str]]:
    """Speed=Slow test methods per declaring class, as ``{class: {class.method}}``.

    Each method anchors one ``dotnet test`` process when slow tests are enabled,
    so the exhaustive variants of one class overlap instead of serializing
    behind the slowest of them. Only namespace-level classes are resolved; a
    slow marker inside a nested class attributes to the outer class, whose
    ``FullyQualifiedName=Outer.Method`` process then selects nothing while the
    class-group term (``Outer+``) still runs the test exactly once, so the
    union of the processes never loses a test.
    """
    methods: dict[str, set[str]] = {}
    for path, content in files.items():
        if not (path.startswith("tests/") and path.endswith(".cs")):
            continue
        stripped = strip_comments(content)
        markers = list(SLOW_TEST_RE.finditer(stripped))
        if not markers:
            continue
        # Namespace segments own the text from their declaration to the next
        # one (the block body of ``namespace N { ... }`` and the whole rest of
        # a file-scoped ``namespace N;`` alike).
        declarations = list(NAMESPACE_RE.finditer(stripped))
        segments = [((declarations[i].end() if i < len(declarations) else 0),
                     declarations[i + 1].start() if i + 1 < len(declarations) else len(stripped),
                     declarations[i].group(1) if i < len(declarations) else "")
                    for i in range(len(declarations) + 1)]
        classes = [(match.start(), len(match.group(1).replace("\t", "    ")), match.group(2))
                   for match in CLASS_LINE_RE.finditer(stripped)]
        for marker in markers:
            segment = next(((start, end, namespace) for start, end, namespace in segments
                            if start <= marker.start() < end), None)
            if segment is None:
                continue
            start, _, namespace = segment
            scope = [c for c in classes if start <= c[0] < marker.start()]
            if not scope:
                continue
            top = min(indent for _, indent, _ in scope)
            owners = [c for c in scope if c[1] == top]
            fqn = f"{namespace}.{owners[-1][2]}" if namespace else owners[-1][2]
            signature = SLOW_METHOD_RE.search(stripped, marker.end())
            if signature is None:
                continue
            methods.setdefault(fqn, set()).add(f"{fqn}.{signature.group(1)}")
    return methods


def class_weights(files: dict[str, str]) -> dict[str, float]:
    """Test-method count per class (shared evenly when a file declares several).

    A rough proxy for run time, used only to spread the remaining classes
    evenly over the Core processes.
    """
    weights: dict[str, float] = {}
    for path, content in files.items():
        if not (path.startswith("tests/") and path.endswith(".cs")):
            continue
        classes = top_level_test_classes(content)
        if not classes:
            continue
        share = len(TEST_METHOD_RE.findall(strip_comments(content))) / len(classes)
        for name in classes:
            weights[name] = weights.get(name, 0.0) + share
    return weights


def core_run_groups(core_classes: set[str], weights: dict[str, float]) -> list[list[str]]:
    """Split the Core suite's classes into one list per parallel process.

    Each heavyweight class anchors a process of its own; classes with detected
    test methods are attached to the currently least-loaded process, keeping
    the count at ``CORE_PROCESS_COUNT`` unless the anchored classes alone
    exceed it. The anchors are all known long runners, so every anchor starts
    with the same load instead of its (unrepresentative) method count and the
    remaining classes spread evenly on top; classes without test methods carry
    no load and are spread by count to keep the filters even. Every class
    stays in exactly one group, so the union of the groups runs exactly the
    tests a single unfiltered process would. Slow test methods are not
    classes' business here: they anchor their own per-method processes (see
    ``full_project_runs``) and are subtracted from these class filters.
    """
    anchors = sorted(HEAVY_CORE_CLASSES & core_classes)
    remainder = core_classes - set(anchors)
    groups = [[name] for name in anchors]
    while len(groups) < CORE_PROCESS_COUNT and remainder:
        groups.append([])
    anchor_load = max((weights.get(name, 0.0) for name in anchors), default=0.0)
    loads = [anchor_load] * len(groups)
    weighted = sorted((name for name in remainder if weights.get(name, 0.0) > 0.0),
                      key=lambda c: (-weights.get(c, 0.0), c))
    for name in weighted:
        index = min(range(len(groups)), key=lambda i: (loads[i], i))
        groups[index].append(name)
        loads[index] += weights.get(name, 0.0)
    # Classes without any detected test method (stub api types) carry no load,
    # so spreading them by class count keeps the filter sizes even instead of
    # letting them pile into whichever process happens to be lightest.
    for name in sorted(remainder - set(weighted)):
        index = min(range(len(groups)), key=lambda i: (len(groups[i]), i))
        groups[index].append(name)
    return [group for group in groups if group]


def _csproj_path_pattern(base: str, rel: str) -> str:
    """Normalize a csproj-relative Include/Remove pattern to repo-relative.

    ``..`` segments pop, empty and ``.`` segments drop; wildcard characters
    pass through for the later glob -> regex translation. Unresolvable ``..``
    segments (path escaping the repo root) are kept, producing a pattern that
    matches no repo-relative path.
    """
    parts: list[str] = []
    for segment in f"{base}/{rel}".replace("\\", "/").split("/"):
        if segment == "..":
            if parts:
                parts.pop()
        elif segment not in ("", "."):
            parts.append(segment)
    return "/".join(parts)


def _glob_regex(pattern: str) -> re.Pattern:
    """MSBuild glob -> regex: ``*``/``?`` stay inside one path segment and
    ``**`` matches zero or more whole segments (fnmatch would let ``*`` cross
    ``/`` and require ``**/`` to consume one)."""
    segments = pattern.split("/")
    regex = ""
    for index, segment in enumerate(segments):
        if segment == "**":
            regex += "(?:[^/]+/)*" if index < len(segments) - 1 else ".*"
            continue
        part = "".join("[^/]*" if ch == "*" else "[^/]" if ch == "?"
                       else re.escape(ch) for ch in segment)
        regex += part + ("/" if index < len(segments) - 1 else "")
    return re.compile(regex + r"\Z")


def classes_by_project(root: Path, files: dict[str, str],
                       projects: list[str]) -> dict[str, set[str]]:
    """Declared test classes per project.

    A class belongs to every project whose assembly compiles its file: an
    explicit ``<Compile Include>`` link, or the project's default folder glob
    (``EnableDefaultCompileItems`` unset/false honored, ``<Compile Remove>``
    patterns applied). A file can be owned by several projects at once — e.g.
    shared test data linked from one project while still sitting inside
    another project's folder — and then its classes run in each owning
    project's processes, matching what the assemblies really compile. Files
    owned by no project count for every project, preserving the historical
    run-everywhere filter.
    """
    includes: dict[str, set[str]] = {}
    removes: dict[str, set[re.Pattern]] = {}
    default_globs: dict[str, bool] = {}
    folders: dict[str, str] = {}
    for project in projects:
        text = read_file(root, project) or ""
        base_dir = project.rsplit("/", 1)[0]
        # Include/Remove attributes are semicolon-separated MSBuild item lists.
        includes[project] = {_csproj_path_pattern(base_dir, rel)
                             for value in COMPILE_INCLUDE_RE.findall(text)
                             for rel in value.split(";") if rel.strip()}
        removes[project] = {_glob_regex(_csproj_path_pattern(base_dir, rel))
                            for value in COMPILE_REMOVE_RE.findall(text)
                            for rel in value.split(";") if rel.strip()}
        default_globs[project] = not NO_DEFAULT_COMPILE_RE.search(text)
        folders[project] = base_dir
    mapping = {project: set() for project in projects}
    for path, content in sorted(files.items()):
        if not (path.startswith("tests/") and path.endswith(".cs")):
            continue
        owners = [p for p in projects if path in includes[p]]
        for p in projects:
            if p in owners or not default_globs[p]:
                continue
            if not path.startswith(folders[p] + "/"):
                continue
            if any(pattern.fullmatch(path) for pattern in removes[p]):
                continue
            owners.append(p)  # the default glob compiles it too
        classes = top_level_test_classes(content)
        for owner in owners:
            mapping[owner].update(classes)
        if not owners:
            for project in projects:
                mapping[project].update(classes)
    return mapping


def mentions_any(content: str, names: list[str]) -> bool:
    """True if any name occurs in content as a whole word."""
    for name in names:
        if re.search(rf"\b{re.escape(name)}\b", content):
            return True
    return False


CI_WORKFLOW_PATH = ".github/workflows/ci.yml"


def ci_slow_matrix_classes(text: str) -> set[str]:
    """Class entries listed in the slow-tests matrix of a ci.yml text.

    The ``- <class>`` lines directly under the matrix ``class:`` key of the
    ``slow-tests`` job. A missing job or key yields an empty set, which the
    consistency check then reports as classes that would never run in CI.
    """
    classes: set[str] = set()
    lines = text.splitlines()
    job = next((i for i, line in enumerate(lines)
                if re.match(r"^  slow-tests:\s*$", line)), len(lines))
    if job == len(lines):
        return classes
    end = job + 1
    while end < len(lines) and not re.match(r"^  \S", lines[end]):
        end += 1
    block = lines[job:end]
    key = next((i for i, line in enumerate(block)
                if re.match(r"^\s+class:\s*$", line)), len(block))
    for line in block[key + 1:]:
        entry = re.match(r"^\s+-\s+(\S+)\s*$", line)
        if not entry:
            break
        classes.add(entry.group(1))
    return classes


def check_ci_matrix(root: Path) -> int:
    """Fail when the ci.yml slow-tests matrix and the detected classes differ.

    The matrix classes are maintained by hand; this guard (run in CI and
    usable locally) makes a forgotten entry a visible failure instead of a
    slow test class that silently stops running in CI.
    """
    detected = slow_test_classes(scan_test_files(root))
    listed = ci_slow_matrix_classes(read_file(root, CI_WORKFLOW_PATH) or "")
    if detected == listed:
        print(f"ci.yml slow-tests matrix matches the {len(detected)} detected slow class(es)")
        return 0
    print("ci.yml slow-tests matrix does not match the Speed=Slow test classes:", file=sys.stderr)
    for name in sorted(detected - listed):
        print(f"  missing from the matrix (would never run in CI): {name}", file=sys.stderr)
    for name in sorted(listed - detected):
        print(f"  in the matrix but no Speed=Slow tests detected: {name}", file=sys.stderr)
    print("update the slow-tests matrix in .github/workflows/ci.yml", file=sys.stderr)
    return 1


def is_generated_cs(path: str) -> bool:
    """True for generated .cs files (e.g. Foo.Generated.cs, GeneratedBar.cs)."""
    name = path.rsplit("/", 1)[-1]
    return name.startswith("Generated") or fnmatch.fnmatch(name, "*.Generated.cs")


def is_build_file(path: str) -> bool:
    """True for *.csproj and Directory.Build.* files."""
    name = path.rsplit("/", 1)[-1]
    return path.endswith(".csproj") or name.startswith("Directory.Build.")


_LINKED_INPUT_RE = re.compile(
    r'''<(?:None|Content)\b[^>]*?\b(?:Include|Update)\s*=\s*["']([^"']+)["']'''
)


@functools.lru_cache(maxsize=None)
def linked_test_inputs(root: Path = REPO_ROOT) -> set[str]:
    """Repo-relative test inputs declared by None/Content Include or Update.

    Tests read them under another name (``ReviewedV129.md``), so the path
    never appears in the test source and rule 3 cannot find the readers.
    This scans literal paths in tests/*/*.csproj, not MSBuild imports,
    properties or globs. Declarations are included even without copy metadata
    so conditional output copies conservatively select their readers.
    """
    linked: set[str] = set()
    for csproj in sorted((root / "tests").glob("*/*.csproj")):
        try:
            text = csproj.read_text(encoding="utf-8")
        except OSError:
            continue
        for include in _LINKED_INPUT_RE.findall(text):
            target = (csproj.parent / include.replace("\\", "/")).resolve()
            try:
                linked.add(target.relative_to(root.resolve()).as_posix())
            except ValueError:
                continue
    return linked


def project_elements(project: Path, root: Path):
    """Read project XML, nearest Directory.Build files and literal local imports.

    This is a conservative dependency scan, not an MSBuild evaluator: conditional
    items are retained, while property-expanded imports are left to MSBuild.
    XML parsing ignores comments and accepts namespaces and any attribute order.
    """
    root = root.resolve()
    pending = [project.resolve()]
    for name in ("Directory.Build.props", "Directory.Build.targets"):
        directory = project.resolve().parent
        while directory.is_relative_to(root):
            candidate = directory / name
            if candidate.is_file():
                pending.append(candidate)
                break
            directory = directory.parent
    seen = set()
    while pending:
        path = pending.pop()
        if path in seen or not path.is_relative_to(root):
            continue
        seen.add(path)
        try:
            document = ET.parse(path).getroot()
        except (OSError, ET.ParseError):
            continue
        for element in document.iter():
            tag = element.tag.rsplit("}", 1)[-1]
            yield tag, element, path
            if tag == "Import":
                include = element.get("Project", "").replace("\\", "/")
                if include and not any(char in include for char in "$*?;"):
                    pending.append((path.parent / include).resolve())


@functools.lru_cache(maxsize=None)
def referenced_tool_dirs(root: Path = REPO_ROOT) -> tuple[str, ...]:
    """Repo-relative tools/ project directories referenced by test projects."""
    dirs: set[str] = set()
    for csproj in sorted((root / "tests").rglob("*.csproj")):
        if any(part in ("bin", "obj") for part in csproj.relative_to(root).parts):
            continue
        for tag, element, _ in project_elements(csproj, root):
            if tag != "ProjectReference":
                continue
            include = element.get("Include", "").replace("\\", "/")
            if not include or any(char in include for char in "$*?;"):
                continue
            target = (csproj.parent / include).resolve().parent
            try:
                rel = target.relative_to(root.resolve()).as_posix()
            except ValueError:
                continue
            if rel.startswith("tools/"):
                dirs.add(rel)
    return tuple(sorted(dirs))


def is_production_cs(path: str) -> bool:
    """True for .cs under src/ or a tools/ project referenced by the tests."""
    return path.endswith(".cs") and (path.startswith("src/") or any(
        path.startswith(f"{directory}/") for directory in referenced_tool_dirs()))


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
      1. a changed test .cs under tests/ selects its own classes and the tests
         mentioning its declared types (helpers, fakes); other test inputs select
         path readers, or everything if unknown (unreferenced docs are ignored);
      2. a changed production .cs under src/ or a referenced tools/ project
         selects, for each declared type,
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
    # Index whole words once rather than scanning every test for every token in
    # large generated diffs. Keep punctuation-containing literals on the exact
    # original boundary matcher, with a cheap substring check before the regex.
    word_mentions: dict[str, set[str]] = {}
    for tf in test_files:
        for word in set(re.findall(r"\w+", files[tf])):
            word_mentions.setdefault(word, set()).add(tf)
    literal_mentions: dict[str, set[str]] = {}

    def tests_mentioning(name: str) -> set[str]:
        if re.fullmatch(r"\w+", name):
            return word_mentions.get(name, set())
        if name not in literal_mentions:
            words = re.findall(r"\w+", name)
            possible = word_mentions.get(max(words, key=len), set()) if words else test_files
            matches = {tf for tf in possible if name in files[tf]}
            if matches:
                pattern = re.compile(rf"\b{re.escape(name)}\b")
                matches = {tf for tf in matches if pattern.search(files[tf])}
            literal_mentions[name] = matches
        return literal_mentions[name]

    for path in sorted(changed):
        content = files.get(path, "")
        if is_build_file(path):
            selection.run_all = True
            selection.reasons.append(f"build file changed: {path}")
            continue
        if path.startswith("tools/balance/") and path.endswith((".json", ".py")):
            selection.run_all = True
            selection.reasons.append(f"balance input changed: {path}")
            continue
        if path in ("src/SodRpg.Core/Game/Balance/Forge.Generated.cs",
                    "src/SodRpg.Core/Game/Balance/Powers.Generated.cs",
                    "src/SodRpg.Core/Game/Balance/Stars.Generated.cs"):
            selection.run_all = True
            selection.reasons.append(f"balance input changed: {path}")
            continue
        if path == "tools/balance/boss_values.py" or path.startswith("tools/balance/bosses/") \
                or re.fullmatch(r"src/SodRpg\.Core/Game/Balance/Boss[A-Za-z]+(Values)?\.Generated\.cs", path):
            selection.run_all = True
            selection.reasons.append(f"balance input changed: {path}")
            continue
        if path in linked_test_inputs():
            selection.run_all = True
            selection.reasons.append(f"file copied into a test project changed: {path}")
            continue
        if path.startswith("tests/") and path.endswith(".cs"):
            # Helpers and fakes also select the tests using their declared types.
            selection.classes.update(top_level_test_classes(content))
            for name in declared_types(content):
                for tf in tests_mentioning(name):
                    selection.classes.update(top_level_test_classes(files[tf]))
            continue
        if path.startswith("tests/") and not any(
                part in ("bin", "obj") for part in path.split("/")):
            readers = [tf for tf in test_files if references_path(files[tf], path)]
            if not readers:
                # Build files were handled above. Documentation without readers
                # does not affect executable tests; unknown fixtures still do.
                if path.lower().endswith((".md", ".rst")):
                    continue
                selection.run_all = True
                selection.reasons.append(f"test fixture changed: {path}")
                continue
            for tf in readers:
                selection.classes.update(top_level_test_classes(files[tf]))
            continue
        if is_production_cs(path) and content:
            # Rule 2 (+ the wide-type part of rule 4).
            types = declared_types(content)
            if not path.startswith("src/"):
                # Top-level programs and tool internals are reached via the
                # namespace or DLL name named by their integration tests.
                types = types + [path.split("/")[1]]
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
                mentions = {c: tests_mentioning(c) for c in candidates}
                wide = {c for c, hits in mentions.items()
                        if test_files and len(hits) > WIDE_TYPE_THRESHOLD * len(test_files)}
                if types and wide & set(types):
                    # The change touches a declaration of a type most tests use.
                    selection.run_all = True
                    selection.reasons.append(
                        f"{path}: types {', '.join(sorted(wide & set(types))[:3])} used in "
                        f"most test files"
                    )
                    continue
                hits = set().union(*(mentions[c] for c in candidates - wide))
                for tf in sorted(hits):
                    selection.classes.update(top_level_test_classes(files[tf]))
                continue
            if types:
                hits = set().union(*(tests_mentioning(name) for name in types))
                if test_files and len(hits) > WIDE_TYPE_THRESHOLD * len(test_files):
                    selection.run_all = True
                    selection.reasons.append(
                        f"{path}: types {', '.join(types[:3])}... used in "
                        f"{len(hits)}/{len(test_files)} test files"
                    )
                else:
                    for tf in hits:
                        selection.classes.update(top_level_test_classes(files[tf]))
        if (is_data_file(path) or is_generated_cs(path)) and content is not None:
            # Rule 3: test files that reference the changed data/generated file.
            for tf in test_files:
                if references_path(files[tf], path):
                    selection.classes.update(top_level_test_classes(files[tf]))

    if selection.run_all:
        every = set()
        for tf in test_files:
            every.update(top_level_test_classes(files[tf]))
        selection.classes = every
    return selection


def build_filters(classes, max_chars: int = MAX_FILTER_CHARS,
                  exclude=()) -> list[str]:
    """Chunk ``FullyQualifiedName~X.`` / ``FullyQualifiedName~X+`` terms.

    The trailing separator keeps the contains-match scoped to class X: without
    it a class whose fully-qualified name contains another's (X and XMore)
    would have its tests selected by both terms. The ``+`` twin of each term
    keeps nested classes covered: vstest writes their fully-qualified names as
    ``Ns.X+Nested.Method``, which a dot-only term would silently miss.

    ``exclude`` lists fully-qualified test-method names subtracted from every
    chunk with ``&FullyQualifiedName!=M`` terms: methods that run in their own
    per-method processes (the slow split) must not be selected a second time
    here, or they would run twice and report twice. The chunk's OR terms are
    parenthesized (``&`` binds tighter than ``|`` in the vstest grammar) and
    the suffix is counted against ``max_chars`` so a chunk plus its exclusions
    still fits.
    """
    suffix = "".join(f"&FullyQualifiedName!={method}" for method in sorted(exclude)) \
        if exclude else ""
    terms: list[str] = []
    for c in sorted(classes):
        terms.append(f"FullyQualifiedName~{c}.")
        terms.append(f"FullyQualifiedName~{c}+")
    chunks: list[str] = []
    current: list[str] = []

    def close(chunk: list[str]) -> str:
        # Only the subtracting form needs the parentheses; a bare OR selects
        # exactly the same tests and keeps the plain chunks unchanged.
        return f"({'|'.join(chunk)}){suffix}" if suffix else "|".join(chunk)

    for term in terms:
        joined = "|".join(current + [term]) + suffix
        if current and len(joined) > max_chars:
            chunks.append(close(current))
            current = [term]
        else:
            current.append(term)
    if current:
        chunks.append(close(current))
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
        if path.startswith("tools/balance/") and path.endswith((".json", ".py")):
            changed.add(path)
        if path.endswith(".cs") or is_data_file(path):
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
    """Find executable test projects, excluding helper-only csproj files.

    Inspect actual XML items and properties, including Directory.Build files
    and literal local imports, instead of matching text inside XML comments.
    """
    projects = []
    for path in (root / "tests").rglob("*.csproj"):
        if any(part in ("bin", "obj") for part in path.relative_to(root).parts):
            continue
        is_test = False
        for tag, element, _ in project_elements(path, root):
            if tag == "PackageReference" and element.get("Include", "").casefold() == "microsoft.net.test.sdk":
                is_test = True
            if tag == "IsTestProject" and (element.text or "").strip().casefold() == "true":
                is_test = True
        if is_test:
            projects.append(path.relative_to(root).as_posix())
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
    env = dict(os.environ)
    env["DOTNET_ROOT"] = dotnet_root
    # Keep the console summary machine-parseable regardless of the OS locale.
    env.setdefault("DOTNET_CLI_UI_LANGUAGE", "en")
    # Never inherit a partial selection from a caller's shell.
    env.pop("SODRPG_EQUIVALENCE_SHARD", None)
    shard = _EQUIVALENCE_SHARD.get()
    if shard is not None:
        env["SODRPG_EQUIVALENCE_SHARD"] = shard
    if slow:
        env["SODRPG_SLOW"] = "1"
    if no_build:
        # A separate remote phase may not share the earlier build's outputs.
        # Ask MSBuild for the path so custom output directories work as well.
        target = subprocess.run(
            [dotnet, "msbuild", project, "-nologo", "-property:Configuration=Release",
             "-getProperty:TargetPath"], cwd=str(root), env=env,
            stdin=subprocess.DEVNULL, capture_output=True, text=True,
            encoding="utf-8", errors="replace",
        )
        if target.returncode != 0:
            return target.returncode, (target.stdout or "") + (target.stderr or "")
        target_path = target.stdout.strip()
        if not target_path:
            return 1, f"MSBuild returned no TargetPath for {project}\n"
        if (root / target_path).is_file():
            cmd.append("--no-build")
        else:
            print(f"build output missing for {project}; building before test",
                  file=sys.stderr)
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


def full_project_runs(project: str, all_classes: set[str],
                      slow_methods: dict[str, set[str]],
                      weights: dict[str, float]) -> tuple[list[tuple[str | None, str]], list[tuple[str, str]]]:
    """((filter, label) parallel runs, (filter, label) deferred runs) for a project.

    Only the Core suite is split: each Speed=Slow test method anchors a process
    of its own (an exact ``FullyQualifiedName=`` match, so one class's slow
    variants overlap instead of serializing behind the slowest) and the classes
    are grouped by ``core_run_groups`` so the heavyweight ones each anchor a
    parallel process. The slow methods are subtracted from the class filters
    (``build_filters``' ``exclude``), so every test is selected by exactly one
    run and the union of the runs selects exactly the tests one unfiltered
    process would. Timing-threshold classes (``ISOLATED_CORE_CLASSES``) are
    excluded from the parallel groups and returned as deferred runs: they run
    alone after the parallel pool drains, so overlapping processes cannot push
    their measurements over the limit.
    """
    if project != CORE_TESTS_PROJECT or not all_classes:
        return [(None, "")], []
    labeled: list[tuple[str, str]] = []
    for cls in sorted(slow_methods):
        for method in sorted(slow_methods[cls]):
            labeled.append((f"FullyQualifiedName={method}",
                            method.rsplit(".", 1)[-1]))
    isolated = sorted(all_classes & ISOLATED_CORE_CLASSES)
    for group in core_run_groups(all_classes - set(isolated), weights):
        anchor = group[0].rsplit(".", 1)[-1]
        name = anchor if len(group) == 1 else f"{anchor}+{len(group) - 1}"
        excluded = set().union(*(slow_methods[c] for c in group if c in slow_methods)) \
            if any(c in slow_methods for c in group) else set()
        for expr in build_filters(group, exclude=excluded):
            labeled.append((expr, name))
    parallel = [(expr, f" ({index}/{len(labeled)}: {name})")
                for index, (expr, name) in enumerate(labeled, start=1)]
    deferred = [(expr, f" (isolated: {name})")
                for name in isolated for expr in build_filters([name])]
    return parallel, deferred


def selection_runs(selected: set[str],
                   slow_methods: dict[str, set[str]]) -> tuple[list[tuple[str, str]], list[tuple[str, str]]]:
    """((filter, label) parallel runs, (filter, label) deferred runs) selecting ``selected``.

    With slow tests enabled every Speed=Slow method of the selected classes
    keeps its own process so the exhaustive variants overlap; the methods are
    subtracted from the shared class filter so nothing runs twice.
    Timing-threshold classes run alone after the parallel pool instead
    (``ISOLATED_CORE_CLASSES``), same as in the full run.
    """
    runs: list[tuple[str, str]] = []
    excluded = set().union(*(slow_methods[c] for c in selected if c in slow_methods)) \
        if any(c in slow_methods for c in selected) else set()
    for method in sorted(excluded):
        runs.append((f"FullyQualifiedName={method}", f" {method.rsplit('.', 1)[-1]}"))
    parallel_classes = set(selected)
    deferred_classes = sorted(parallel_classes & ISOLATED_CORE_CLASSES)
    parallel_classes -= set(deferred_classes)
    if parallel_classes:
        parallel_excluded = set().union(*(slow_methods[c] for c in parallel_classes if c in slow_methods)) \
            if any(c in slow_methods for c in parallel_classes) else set()
        for expr in build_filters(sorted(parallel_classes), exclude=parallel_excluded):
            runs.append((expr, ""))
    deferred = [(expr, f" (isolated: {name})")
                for name in deferred_classes for expr in build_filters([name])]
    return runs, deferred


def run_test_process(root: Path, project: str, filter_expr: str | None,
                     slow: bool, shard: str | None) -> tuple[int, str]:
    # Context-local metadata preserves run_dotnet_test's public signature (the
    # release gate wraps it to add its ordinary/timing filters).
    token = _EQUIVALENCE_SHARD.set(shard)
    try:
        return run_dotnet_test(root, project, filter_expr, True, slow)
    finally:
        _EQUIVALENCE_SHARD.reset(token)


def run_selection(root: Path, classes, run_all: bool, slow: bool = False) -> int:
    """Run the selected classes (or everything); returns the exit code.

    Projects are built serially, then their tests run in parallel: every test
    assembly serializes its own tests (shared registry state), so the
    wall-clock win comes from overlapping processes. The Core suite is split
    into class-filtered processes (heavy and slow classes anchoring one each),
    and the parallelism is capped at the CPU core count so low-core machines
    queue processes instead of thrashing.
    """
    projects = find_test_projects(root)
    if not projects:
        print("no test projects found under tests/", file=sys.stderr)
        return 1
    code = build_projects(root, projects)
    if code != 0:
        return code
    files = scan_test_files(root)
    weights = class_weights(files)
    slow_methods = slow_test_methods(files) if slow else {}
    owned = classes_by_project(root, files, projects)
    unowned: set[str] = set()
    if not run_all and classes:
        known = set().union(*owned.values()) if owned else set()
        unowned = set(classes) - known
    pairs: list[tuple[str, str | None, str, str | None]] = []  # (project, filter, label, shard)
    deferred_pairs: list[tuple[str, str | None, str]] = []  # alone, after the pool drains
    for project in projects:
        if run_all:
            runs, deferred = full_project_runs(project, owned.get(project, set()), slow_methods, weights)
        else:
            selected = (owned.get(project, set()) & set(classes or ())) | unowned
            if not selected:
                continue  # no class of this project is selected; nothing to run
            runs, deferred = selection_runs(selected, slow_methods)
        for expr, label in runs:
            method = expr.removeprefix("FullyQualifiedName=") if expr else ""
            count = EQUIVALENCE_SHARDS.get(method, 1) if slow and project == CORE_TESTS_PROJECT else 1
            for index in range(count):
                shard = f"{index}/{count}" if count > 1 else None
                suffix = f" [sequence shard {index + 1}/{count}]" if shard else ""
                pairs.append((project, expr, label + suffix, shard))
        deferred_pairs.extend((project, expr, label) for expr, label in deferred)
    if not pairs and not deferred_pairs:
        print("no tests selected for any project")
        return 0
    workers = max(1, min(len(pairs), os.cpu_count() or 1)) if pairs else 0
    print(f"running {len(pairs)} dotnet test processes ({workers} in parallel)",
          file=sys.stderr)
    exit_code = 0
    total_failed = total_passed = 0
    if pairs:
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
            futures = {pool.submit(run_test_process, root, project, expr, slow, shard):
                       (project, label) for project, expr, label, shard in pairs}
            for future in concurrent.futures.as_completed(futures):
                project, label = futures[future]
                code, output = future.result()
                print(f"===== {project}{label} =====")
                print(output, end="" if output.endswith("\n") else "\n")
                if code != 0:
                    exit_code = exit_code or code
                failed, passed = parse_summary(output)
                print(f"-> failed: {failed}  passed: {passed}")
                total_failed += failed
                total_passed += passed
    if deferred_pairs:
        # Timing-threshold classes: nothing else may compete for the CPU while
        # they measure, so they wait for the pool and then run alone.
        print(f"running {len(deferred_pairs)} isolated timing-sensitive process(es) "
              f"after the parallel batches", file=sys.stderr)
        for project, expr, label in deferred_pairs:
            code, output = run_dotnet_test(root, project, expr, True, slow)
            print(f"===== {project}{label} =====")
            print(output, end="" if output.endswith("\n") else "\n")
            if code != 0:
                exit_code = exit_code or code
            failed, passed = parse_summary(output)
            print(f"-> failed: {failed}  passed: {passed}")
            total_failed += failed
            total_passed += passed
    print(f"selected classes: {'all' if run_all else len(classes)}")
    print(f"processes: {len(pairs) + len(deferred_pairs)}")
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
                        help="also run Speed=Slow exhaustive tests (SODRPG_SLOW=1); "
                             "the SODRPG_SLOW=1 environment variable is equivalent")
    parser.add_argument("--check-ci-matrix", action="store_true",
                        help="verify the ci.yml slow-tests matrix lists exactly the "
                             "classes holding Speed=Slow tests, then exit")
    args = parser.parse_args(argv)
    # The release scripts and CI export SODRPG_SLOW=1 instead of passing --slow;
    # treat both identically so the slow split applies to the real callers too.
    slow = args.slow or os.environ.get("SODRPG_SLOW") == "1"

    root = REPO_ROOT
    if args.check_ci_matrix:
        return check_ci_matrix(root)
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
                 for name in top_level_test_classes(content)}
            )
            for name in classes:
                print(name)
            print(f"selected: {len(classes)} classes (full run)")
            return 0
        return run_selection(root, None, run_all=True, slow=slow)

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
        return run_selection(root, None, run_all=True, slow=slow)

    if not selection.classes:
        print("nothing relevant changed; no tests to run")
        return 0

    if args.list:
        for name in sorted(selection.classes):
            print(name)
        print(f"selected: {len(selection.classes)} classes")
        return 0

    print(f"selected classes: {len(selection.classes)}")
    return run_selection(root, selection.classes, run_all=False, slow=slow)


if __name__ == "__main__":
    sys.exit(main())
