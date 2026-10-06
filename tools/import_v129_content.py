"""Audit historical v1.29 identities against canonical equipment sources; never import historical numbers."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
import sys
sys.path.insert(0, str(ROOT / "tools/balance"))
from equipment_items_values import load_uniques

def audit():
    source = ROOT / "src/SodRpg.Core/Game/Content.cs"
    text = source.read_text(encoding="utf-8")
    table = (ROOT / "docs/specs/v1.29-uniques-table.md").read_text(encoding="utf-8")
    canonical_uniques = load_uniques()
    rows = [[part.strip() for part in line.strip().strip("|").split("|")]
            for line in table.splitlines() if line.startswith("|")]
    uniques = [r for r in rows if len(r) == 13 and r[0].startswith("unique.")]
    sets = [r for r in rows if len(r) == 6 and r[0].startswith("set.")]
    pieces = [r for r in rows if len(r) == 5 and r[0].startswith("set.")]
    assert (len(uniques), len(sets), len(pieces)) == (559, 24, 72)
    assert len({r[0] for r in uniques + pieces}) == 631
    missing_uniques = {row[0] for row in uniques + pieces} - set(canonical_uniques)
    if missing_uniques:
        raise ValueError(f"reviewed uniques missing from canonical table: {sorted(missing_uniques)}")
    missing_authored = [row[0] for row in uniques + pieces if json.dumps(row[0]) not in text]
    if missing_authored:
        raise ValueError(f"reviewed identities missing from authored definitions: {missing_authored}")
    # Ordinary set numeric definitions are generated exclusively from equipment/sets.json.
    from set_values import load_sets
    canonical_set_ids = {row["id"] for row in load_sets()["sets"]}
    missing_sets = {row[0] for row in sets} - canonical_set_ids
    if missing_sets:
        raise ValueError(f"reviewed sets missing from canonical table: {sorted(missing_sets)}")
    result = {"reviewed_uniques": len(uniques), "reviewed_set_pieces": len(pieces),
              "reviewed_sets": len(sets)}
    print(json.dumps(result, ensure_ascii=True, indent=2))
    return result

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args()
    audit()
