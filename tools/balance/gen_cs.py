#!/usr/bin/env python3
"""Generate compile-time balance content; --check never writes files."""

import argparse
import importlib.util
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

ROOT = Path(__file__).resolve().parents[2]
FORGE_PATH = ROOT / "tools" / "balance" / "forge.json"
OUTPUT_PATH = ROOT / "src" / "SodRpg.Core" / "Game" / "Balance" / "Forge.Generated.cs"
# Keep these entry points for balance tools and the stage0 generator contract.
load_forge = forge_values.load
render_forge = forge_values.render


def _star_generator():
    directory = ROOT / "tools" / "star-manifest"
    sys.path.insert(0, str(directory))
    spec = importlib.util.spec_from_file_location("balance_star_generator", directory / "gen_cs.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def generate(check=False):
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
    return stars.publish_outputs(outputs, check=check)


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
