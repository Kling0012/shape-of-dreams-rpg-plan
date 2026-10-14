#!/usr/bin/env python3
"""Generate compile-time balance content; --check writes no source files.

--check consults a content-hash cache (tools/balance/.gen_cs-cache.json,
git-ignored): when neither the generator inputs (everything under
tools/star-manifest/ and tools/balance/ plus every hand-written .cs under
src/SodRpg.Core/Game) nor the generated files themselves changed since the
last verified run, the expensive rendering is skipped entirely. The cache
record is validated (safe paths, sha256-shaped hashes, a key set equal to the
disk-derived expected outputs, a digest over the whole outputs mapping)
before it is trusted, and anything unreadable, malformed, truncated or
tampered with just counts as stale, falling back to the normal generation.
--check still refreshes that cache file; it never touches the generated
sources.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import re
import sys
from pathlib import Path
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import gear_values
import set_values
import forge_values
import star_progression_values
import boss_values
import star_values
import pressure_values
import monster_values
import infinity_values
import infinity_boss_values
import stage6_values
import equipment_items_values
import powers_values
import equipment_pools_values
import room_omen_values

ROOT = Path(__file__).resolve().parents[2]
FORGE_PATH = ROOT / "tools" / "balance" / "forge.json"
OUTPUT_PATH = ROOT / "src" / "SodRpg.Core" / "Game" / "Balance" / "Forge.Generated.cs"
# Keep these entry points for balance tools and the stage0 generator contract.
load_forge = forge_values.load
render_forge = forge_values.render

CACHE_PATH = ROOT / "tools" / "balance" / ".gen_cs-cache.json"
# OUTPUT_PATH as declared: tests patch this module attribute, and the cache is
# only valid for the default output layout.
DEFAULT_OUTPUT_PATH = OUTPUT_PATH

# Hand-written src under this folder is hashed wholesale as generator input:
# the generators (tools/balance/*_values.py, tools/star-manifest/*) read a
# subset of it today, and hashing all of it means a generator that starts
# reading a new file invalidates the cache automatically instead of relying on
# a hand-maintained list. Generated outputs (*.Generated.cs, Generated*.cs,
# *.g.cs) are excluded: they are hashed separately as cache outputs. If a
# future output breaks that naming rule it only keeps the cache from ever
# hitting (the full generation then runs, which is still correct).
GENERATOR_SRC_ROOT = "src/SodRpg.Core/Game"

# Generated outputs whose names carry no generated marker or that live
# outside src/ (everything else matches _is_generated_output under src/).
# Listed by hand so the cache's output set is checked against a disk-derived
# expectation; a future output of that kind only keeps the cache from ever
# hitting (the full generation then runs, which is still correct).
EXTRA_OUTPUT_PATHS = ("src/SodRpg.Core/Game/NamedItems.Data.cs",
                      "tools/lowrarity/new-bases.json")
# Generated-marker files under src/ that this generator does NOT own; leaving
# them in the expectation would permanently invalidate the cache.
FOREIGN_MARKER_PATHS = ("src/SodRpg.Core/Game/StarMapPlacements.Generated.cs",)


def _is_generated_output(path: Path) -> bool:
    name = path.name
    return (name.startswith("Generated") or name.endswith(".Generated.cs")
            or name.endswith(".g.cs"))


def _hash_file(path: Path) -> str | None:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1 << 20), b""):
                digest.update(chunk)
    except (OSError, ValueError):  # ValueError: e.g. NUL byte in the path
        return None
    return digest.hexdigest()


HEX64_RE = re.compile(r"[0-9a-f]{64}")


def _is_cacheable_path(rel) -> bool:
    """True for a plain repo-relative posix path (no escape, no NUL byte)."""
    return (isinstance(rel, str) and bool(rel) and "\\" not in rel and "\x00" not in rel
            and not rel.startswith("/") and not re.match(r"[A-Za-z]:", rel)
            and ".." not in rel.split("/"))


def _outputs_digest(outputs: dict) -> str:
    """Digest over the whole recorded outputs mapping (path -> content hash).

    Stored next to ``outputs`` and recomputed on read, so a cache whose output
    list was truncated or edited after writing (dropping generated files from
    the check) no longer validates.
    """
    lines = sorted(f"{rel}:{digest}" for rel, digest in outputs.items())
    return hashlib.sha256("\n".join(lines).encode("utf-8")).hexdigest()


def _generator_inputs() -> list[str]:
    """Repo-relative input paths whose content decides the generated output."""
    paths: list[str] = []
    for folder in ("tools/star-manifest", "tools/balance"):
        for path in sorted((ROOT / folder).rglob("*")):
            if path.is_file() and "__pycache__" not in path.parts and path != CACHE_PATH:
                paths.append(path.relative_to(ROOT).as_posix())
    for path in sorted((ROOT / GENERATOR_SRC_ROOT).rglob("*.cs")):
        if not _is_generated_output(path):
            paths.append(path.relative_to(ROOT).as_posix())
    return paths


def _inputs_digest() -> str:
    digest = hashlib.sha256()
    for rel in sorted(set(_generator_inputs())):
        digest.update(f"{rel}:{_hash_file(ROOT / rel)}\n".encode())
    return digest.hexdigest()


def _expected_output_paths() -> set[str] | None:
    """Output paths derived from the working tree, not from the cache record.

    Every generated-marker .cs file under src/ minus the marker files this
    generator does not own, plus the hand-listed marker-less / out-of-src
    outputs. Compared against the cache's recorded keys, so a cache whose
    output list was truncated and self-consistently re-digested (dropping a
    generated file from the check) no longer validates even though every
    recorded hash still matches. None when the scan itself fails.
    """
    try:
        paths = set(EXTRA_OUTPUT_PATHS)
        for path in (ROOT / "src").rglob("*.cs"):
            rel = path.relative_to(ROOT).as_posix()
            if _is_generated_output(path) and rel not in FOREIGN_MARKER_PATHS:
                paths.add(rel)
        return paths
    except OSError:
        return None


def _cache_is_fresh(inputs: str) -> bool:
    """True when the recorded inputs and every recorded generated file match.

    A changed or missing generated file invalidates the cache, so a stale
    output is never waved through. The record is validated before use: the
    inputs digest must match, ``outputs`` must be a non-empty mapping of safe
    repo-relative paths to sha256-shaped hashes whose key set equals the
    disk-derived expected outputs (a partial, truncated or hand-edited output
    list cannot smuggle unrecorded generated files past the check), and the
    digest stored next to it must still describe that whole mapping. Any
    unreadable or malformed cache (missing file, invalid JSON, non-object JSON)
    just counts as stale; nothing here raises.
    """
    try:
        record = json.loads(CACHE_PATH.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return False
    if not isinstance(record, dict):
        return False
    outputs = record.get("outputs")
    if record.get("inputs") != inputs or not isinstance(outputs, dict) or not outputs:
        return False
    if not all(_is_cacheable_path(rel) and isinstance(digest, str)
               and HEX64_RE.fullmatch(digest) for rel, digest in outputs.items()):
        return False
    expected = _expected_output_paths()
    if expected is None or set(outputs) != expected:
        return False
    if record.get("outputs_digest") != _outputs_digest(outputs):
        return False
    try:
        return all(_hash_file(ROOT / rel) == digest for rel, digest in outputs.items())
    except (OSError, ValueError):
        return False


def _write_cache(inputs: str, outputs) -> None:
    try:
        recorded = {path.relative_to(ROOT).as_posix():
                    hashlib.sha256(content.encode("utf-8")).hexdigest()
                    for path, content in outputs.items()}
        record = {"inputs": inputs, "outputs": recorded,
                  "outputs_digest": _outputs_digest(recorded)}
        text = json.dumps(record, indent=1)
    except (OSError, ValueError):  # ValueError also covers UnicodeEncodeError
        return  # the cache only saves time; never fail a successful run for it
    temp = CACHE_PATH.with_name(CACHE_PATH.name + ".tmp")
    try:
        temp.write_text(text, encoding="utf-8")
        os.replace(temp, CACHE_PATH)
    except OSError:
        pass


def _star_generator():
    directory = ROOT / "tools" / "star-manifest"
    sys.path.insert(0, str(directory))
    spec = importlib.util.spec_from_file_location("balance_star_generator", directory / "gen_cs.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def generate(check=False):
    # The cache is only valid for the real output layout (tests patch OUTPUT_PATH).
    use_cache = OUTPUT_PATH == DEFAULT_OUTPUT_PATH
    inputs = _inputs_digest() if use_cache else None
    if check and use_cache and _cache_is_fresh(inputs):
        print("gen_cs --check: inputs and generated files unchanged (cache); skipped generation")
        return True
    # Resolve and render every domain before publishing any file.
    stage6 = stage6_values.render_outputs()
    forge = render_forge(load_forge())
    gear = gear_values.render_outputs()
    sets = set_values.render_outputs()
    progression = star_progression_values.render_outputs()
    pressure = pressure_values.render_outputs()
    monsters = monster_values.render_outputs()
    infinity = infinity_values.render_outputs()
    stars = _star_generator()
    outputs = stars.render_outputs()
    outputs[OUTPUT_PATH] = forge
    outputs.update(gear)
    outputs.update(sets)
    outputs.update(boss_values.render_outputs())
    outputs.update(progression)
    outputs.update(pressure)
    outputs.update(monsters)
    outputs.update(infinity)
    outputs.update(infinity_boss_values.render_outputs())
    outputs.update(stage6)
    outputs.update(equipment_items_values.render_outputs())
    outputs.update(powers_values.render_outputs())
    outputs.update(equipment_pools_values.render_outputs())
    outputs.update(room_omen_values.render_outputs())
    ok = stars.publish_outputs(outputs, check=check)
    if ok and use_cache:
        _write_cache(inputs, outputs)
    return ok


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if generated source is stale, without writing")
    args = parser.parse_args()
    try:
        return 0 if generate(check=args.check) else 1
    except (OSError, ValueError) as error:
        print(f"balance generation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
