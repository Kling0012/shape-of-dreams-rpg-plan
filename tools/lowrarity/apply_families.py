#!/usr/bin/env python3
"""v1.32 low-rarity volume: apply base families / append new bases in Content.cs.

Modes
  families   rewrite existing `new BaseDef(...)` lines so they pass the family
             (ids, names and stats are kept; Plain stays as the 6-argument form).
             Input: base-families.json = {"<base id>": "<Family>", ...}
                    or [{"id": "...", "family": "..."}, ...]
  new-bases  append new BaseDef lines at the end of Content.Bases.
             Input: new-bases.json metadata with canonical valueRef (no numeric value).
             `slot`, `line`, `stat` and `family` are enum member names (e.g. "Weapon", "Offense", "Haste", "Frost").
             Ids are only ever added; an id that already exists aborts the run.

Usage
  python tools/lowrarity/apply_families.py families  [--json PATH] [--content PATH] [--dry-run]
  python tools/lowrarity/apply_families.py new-bases [--json PATH] [--content PATH] [--dry-run]

Line endings of Content.cs are preserved. Nothing is written when validation fails.
"""
import argparse
import json
import os
import re
import sys
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_CONTENT = os.path.join(ROOT, "src", "SodRpg.Core", "Game", "Content.cs")
sys.path.insert(0, str(Path(ROOT) / "tools/balance"))
from equipment_items_values import constant_name, load_bases

FAMILIES = {"Plain", "Frost", "Flame", "Light", "Dark", "Guard", "Gale", "Mend", "Summon", "Memory"}
SLOTS = {"Weapon", "Armor", "Charm", "Head", "Hands", "Feet"}
LINES = {"Offense", "Guard", "Resonance"}

# new BaseDef("id", Slot.X, Line.Y, new Txt("ja", "en"), Stat.Z, 5[, Family.F])[,]
BASE_RE = re.compile(
    r'^(?P<indent>\s*)new BaseDef\("(?P<id>[^"]+)", (?P<head>Slot\.\w+, Line\.\w+, new Txt\("[^"]*", "[^"]*"\), Stat\.\w+, EquipmentItemsBalanceValues\.\w+)'
    r'(?:, Family\.(?P<fam>\w+))?\)(?P<comma>,?)(?P<tail>\s*)$'
)


def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def read_text(path):
    with open(path, encoding="utf-8", newline="") as f:
        return f.read()


def write_text(path, text):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    os.replace(tmp, path)


def normalize_families(data):
    if isinstance(data, dict):
        pairs = list(data.items())
    else:
        pairs = [(e["id"], e["family"]) for e in data]
    result = {}
    for base_id, family in pairs:
        if family not in FAMILIES:
            raise ValueError("unknown family %r for %s" % (family, base_id))
        if base_id in result and result[base_id] != family:
            raise ValueError("conflicting families for %s" % base_id)
        result[base_id] = family
    return result


def apply_families(text, families):
    eol = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(eol)
    seen = set()
    changed = 0
    for i, line in enumerate(lines):
        m = BASE_RE.match(line)
        if not m:
            continue
        base_id = m.group("id")
        if base_id not in families:
            continue
        seen.add(base_id)
        family = families[base_id]
        current = m.group("fam") or "Plain"
        if current == family:
            continue
        family_arg = "" if family == "Plain" else ", Family.%s" % family
        lines[i] = "%snew BaseDef(\"%s\", %s%s)%s%s" % (
            m.group("indent"), base_id, m.group("head"), family_arg, m.group("comma"), m.group("tail"))
        changed += 1
    missing = sorted(set(families) - seen)
    if missing:
        raise ValueError("ids not found in Content.cs: %s" % ", ".join(missing[:10]) + (" ..." if len(missing) > 10 else ""))
    return eol.join(lines), changed


def cs_string(value):
    return '"%s"' % value.replace("\\", "\\\\").replace('"', '\\"')


def append_new_bases(text, entries):
    eol = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(eol)
    existing = set()
    last = None
    start = None
    for i, line in enumerate(lines):
        if "IReadOnlyList<BaseDef> Bases" in line:
            start = i
        m = BASE_RE.match(line)
        if m:
            existing.add(m.group("id"))
            last = i
    if start is None or last is None:
        raise ValueError("Content.Bases not found")
    if not lines[last + 1].strip() == "};":
        raise ValueError("unexpected end of Content.Bases at line %d" % (last + 2))
    new_lines = []
    batch_ids = set()
    values = load_bases()
    for e in entries:
        base_id = e["id"]
        if base_id in existing or base_id in batch_ids:
            raise ValueError("duplicate base id %s" % base_id)
        batch_ids.add(base_id)
        for key, allowed in (("slot", SLOTS), ("line", LINES), ("family", FAMILIES)):
            if e[key] not in allowed:
                raise ValueError("%s: bad %s %r" % (base_id, key, e[key]))
        if not re.fullmatch(r"\w+", e["stat"]):
            raise ValueError("%s: bad stat %r" % (base_id, e["stat"]))
        family_arg = "" if e["family"] == "Plain" else ", Family.%s" % e["family"]
        expected_ref = "equipment/bases.json#%s/implicitValue" % base_id
        if "value" in e or e.get("valueRef") != expected_ref or base_id not in values:
            raise ValueError("%s: expected canonical valueRef" % base_id)
        if values[base_id]["stat"] != e["stat"]:
            raise ValueError("%s: stat must match canonical relation" % base_id)
        new_lines.append("            new BaseDef(%s, Slot.%s, Line.%s, new Txt(%s, %s), Stat.%s, EquipmentItemsBalanceValues.%s%s)," % (
            cs_string(base_id), e["slot"], e["line"], cs_string(e.get("ja", e.get("nameJa"))), cs_string(e.get("en", e.get("nameEn"))),
            e["stat"], constant_name("Base", base_id), family_arg))
    lines[last + 1:last + 1] = new_lines
    return eol.join(lines), len(new_lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["families", "new-bases"])
    ap.add_argument("--json", help="input JSON (default: tools/lowrarity/base-families.json or new-bases.json)")
    ap.add_argument("--content", default=DEFAULT_CONTENT, help="Content.cs path")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)

    json_path = args.json or os.path.join(HERE, "base-families.json" if args.mode == "families" else "new-bases.json")
    if not os.path.exists(json_path):
        print("input not found: %s" % json_path, file=sys.stderr)
        return 2
    text = read_text(args.content)
    data = load_json(json_path)
    try:
        if args.mode == "families":
            new_text, n = apply_families(text, normalize_families(data))
        else:
            new_text, n = append_new_bases(text, data)
    except (ValueError, KeyError) as ex:
        print("error: %s" % ex, file=sys.stderr)
        return 1
    print("%s: %d line(s) %s" % (args.mode, n, "would change" if args.dry_run else "changed"))
    if not args.dry_run and new_text != text:
        write_text(args.content, new_text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
