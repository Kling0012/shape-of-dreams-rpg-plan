#!/usr/bin/env python3
"""Generate compile-time balance content; --check never writes files.

--check consults a content-hash cache (tools/balance/.gen_cs-cache.json,
git-ignored): when neither the generator inputs (everything under
tools/star-manifest/ and tools/balance/ plus the hand-written src files the
generators read) nor the generated files themselves changed since the last
verified run, the expensive rendering is skipped entirely.
"""

import argparse
import hashlib
import importlib.util
import json
import os
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

# Hand-written src files the generators read (tools/balance/star_values.py,
# set_values.py, equipment_items_values.py, tools/star-manifest/gen_cs.py and
# validate.py; StarClusters/*.cs is globbed by validate.legacy_data and
# BossProfiles.*.cs by equipment_items_values). Generated outputs are hashed
# separately and must not appear here. Extend this list when a generator
# starts reading a new src file.
GENERATOR_SRC_INPUTS = (
    "src/SodRpg.Core/Game/Content.cs",
    "src/SodRpg.Core/Game/Gimmicks.cs",
    "src/SodRpg.Core/Game/HeroSigils.cs",
    "src/SodRpg.Core/Game/HeroStarRoutes.cs",
    "src/SodRpg.Core/Game/Ids.cs",
    "src/SodRpg.Core/Game/PairCombos.cs",
    "src/SodRpg.Core/Game/StarClusters.cs",
    "src/SodRpg.Core/Game/Mechanisms/AuthoredKeystoneCompiler.cs",
    "src/SodRpg.Core/Game/Mechanisms/AuthoredStarContract.cs",
    "src/SodRpg.Core/Game/Mechanisms/SacrificeShield.cs",
    "src/SodRpg.Core/Game/Mechanisms/StunSourceFilter.cs",
)


def _hash_file(path: Path) -> str | None:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1 << 20), b""):
                digest.update(chunk)
    except OSError:
        return None
    return digest.hexdigest()


def _generator_inputs() -> list[str]:
    """Repo-relative input paths whose content decides the generated output."""
    paths = list(GENERATOR_SRC_INPUTS)
    for folder in ("tools/star-manifest", "tools/balance"):
        for path in sorted((ROOT / folder).rglob("*")):
            if path.is_file() and "__pycache__" not in path.parts and path != CACHE_PATH:
                paths.append(path.relative_to(ROOT).as_posix())
    game = ROOT / "src/SodRpg.Core/Game"
    for pattern in ("StarClusters/*.cs", "BossProfiles.*.cs"):
        for path in sorted(game.glob(pattern)):
            if path.name.startswith("Generated") or path.name.endswith(".Generated.cs"):
                continue  # generated outputs are hashed separately
            paths.append(path.relative_to(ROOT).as_posix())
    return paths


def _inputs_digest() -> str:
    digest = hashlib.sha256()
    for rel in sorted(set(_generator_inputs())):
        digest.update(f"{rel}:{_hash_file(ROOT / rel)}\n".encode())
    return digest.hexdigest()


def _cache_is_fresh(inputs: str) -> bool:
    """True when the recorded inputs and every recorded generated file match.

    A changed or missing generated file invalidates the cache, so a stale
    output is never waved through.
    """
    try:
        record = json.loads(CACHE_PATH.read_text(encoding="utf-8"))
        outputs = record.get("outputs")
        if record.get("inputs") != inputs or not isinstance(outputs, dict) or not outputs:
            return False
    except (OSError, ValueError):
        return False
    return all(_hash_file(ROOT / rel) == file_hash for rel, file_hash in outputs.items())


def _write_cache(inputs: str, outputs) -> None:
    record = {
        "inputs": inputs,
        "outputs": {path.relative_to(ROOT).as_posix():
                    hashlib.sha256(content.encode("utf-8")).hexdigest()
                    for path, content in outputs.items()},
    }
    temp = CACHE_PATH.with_name(CACHE_PATH.name + ".tmp")
    try:
        temp.write_text(json.dumps(record, indent=1), encoding="utf-8")
        os.replace(temp, CACHE_PATH)
    except OSError:
        pass  # the cache only saves time; never fail a successful run for it


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
