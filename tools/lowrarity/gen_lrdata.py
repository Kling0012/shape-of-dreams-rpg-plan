# -*- coding: utf-8 -*-
"""Generate low-rarity base metadata from authored definitions and canonical numbers.

The 360 original family identities are reviewed in base-families.json. Numerical
values live exclusively in tools/balance/equipment/bases.json; generated metadata
contains references, never a second editable numeric source.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/balance"))
from equipment_items_values import load_bases


def render_outputs():
    values = load_bases()
    text = (ROOT / "src/SodRpg.Core/Game/Content.cs").read_text(encoding="utf-8")
    families_path = ROOT / "tools/lowrarity/base-families.json"
    families = json.loads(families_path.read_text(encoding="utf-8"))
    pattern = re.compile(r'new BaseDef\("([^"\n]+)", Slot\.(\w+), Line\.(\w+), new Txt\("([^"\n]*)", "([^"\n]*)"\), Stat\.(\w+), EquipmentItemsBalanceValues\.\w+(?:, Family\.(\w+))?\)')
    definitions = list(pattern.finditer(text))
    if len(definitions) != len(values) or set(families) - set(values):
        raise ValueError("authored base coverage does not match canonical source")
    entries = []
    for match in definitions:
        item_id, slot, line, ja, en, stat, family = match.groups()
        family = family or "Plain"
        if item_id in families:
            if families[item_id] != family:
                raise ValueError(f"{item_id}: reviewed family differs from authored family")
            continue
        entries.append(dict(id=item_id, slot=slot, line=line, family=family,
                            nameJa=ja, nameEn=en, stat=stat,
                            valueRef=f"equipment/bases.json#{item_id}/implicitValue"))
    return {ROOT / "tools/lowrarity/new-bases.json": json.dumps(entries, ensure_ascii=False, indent=1) + "\n"}


def main():
    for path, text in render_outputs().items():
        if path.read_text(encoding="utf-8") != text:
            path.write_text(text, encoding="utf-8", newline="\n")
    print("Generated base metadata from canonical equipment values.")


if __name__ == "__main__":
    main()
