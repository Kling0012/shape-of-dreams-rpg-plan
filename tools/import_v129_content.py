"""Import reviewed v1.29 tables; fail on unknown effects."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def generate(write=False):
    source = ROOT / "src/SodRpg.Core/Game/Content.cs"
    text = source.read_text(encoding="utf-8")
    table = (ROOT / "docs/specs/v1.29-uniques-table.md").read_text(encoding="utf-8")
    ids = (ROOT / "src/SodRpg.Core/Game/Ids.cs").read_text(encoding="utf-8")
    powers = {"P" + str(int(number) - 47): name for name, number in
              re.findall(r"^        (\w+) = (\d+),", ids, re.M) if 48 <= int(number) <= 91}
    powers.update({ja: name for name, ja in re.findall(r'case Power\.(\w+): return Loc.T\("([^"]+)"', text)})
    powers.update(dict(zip(["蒸気", "蝕", "燃え殻", "氷晶"], ["Steam", "Eclipse", "Cinder", "FrostCrystal"])))
    rows = [[part.strip() for part in line.strip().strip("|").split("|")]
            for line in table.splitlines() if line.startswith("|")]
    uniques = [r for r in rows if len(r) == 13 and r[0].startswith("unique.")]
    sets = [r for r in rows if len(r) == 6 and r[0].startswith("set.")]
    pieces = [r for r in rows if len(r) == 5 and r[0].startswith("set.")]
    assert (len(uniques), len(sets), len(pieces)) == (559, 24, 72)
    assert len({r[0] for r in uniques + pieces}) == 631
    deferred = []
    deferred_sets = []
    quote = lambda value: json.dumps(value, ensure_ascii=False)
    new_uniques, new_sets = [], []
    for r in uniques:
        if r[0] in deferred or quote(r[0]) in text:
            continue
        item = (f"            new UniqueDef({quote(r[0])}, {quote(r[1])}, new Txt({quote(r[2])}, {quote(r[3])}),\n"
                f"                new Txt({quote(r[4])}, {quote(r[5])}),\n"
                f"                Power.{powers[r[6]]}, {int(r[7])}, Power.{powers[r[8]]}, {int(r[9])})")
        if r[10] != "-":
            requires = ", ".join(map(quote, r[10].split("+")))
            item += f" {{ Link = new LinkDef {{ Requires = new[] {{ {requires} }}, Kind = LinkKind.{r[11]}, Value = {int(r[12])} }} }}"
        new_uniques.append(item + ",")
    for r in pieces:
        if r[1] in deferred_sets or quote(r[0]) in text:
            continue
        new_uniques.append(f"            new UniqueDef({quote(r[0])}, {quote(r[2])}, new Txt({quote(r[3])}, {quote(r[4])}), {quote(r[1])}),")
    for r in sets:
        if r[0] in deferred_sets or quote(r[0]) + ", Name" in text:
            continue
        stats = [part.strip().split() for part in r[4].split(";")]
        effects = [part.strip().split() for part in r[5].split(";")]
        stat_code = ", ".join(f"new StatLine(Stat.{name}, {int(value)})" for name, value in stats)
        power_code = ", ".join(f"new PowerLine(Power.{powers[name]}, {int(value)})" for name, value in effects)
        new_sets.append(f"            new SetDef {{ Id = {quote(r[0])}, Name = new Txt({quote(r[1])}, {quote(r[2])}),\n"
                        f"                TwoPiece = new[] {{ {stat_code} }},\n"
                        f"                ThreePiece = new[] {{ {power_code} }} }},")
    if write:
        boundary = "        };\n\n        public static readonly IReadOnlyList<SetDef> Sets"
        assert text.count(boundary) == 1
        if new_uniques:
            text = text.replace(boundary, "\n".join(new_uniques) + "\n" + boundary)
        if new_sets:
            start = text.index("public static readonly IReadOnlyList<SetDef> Sets")
            end = text.index("        };", start)
            text = text[:end] + "\n".join(new_sets) + "\n" + text[end:]
        source.write_text(text, encoding="utf-8", newline="\n")
    result = {"new_uniques_and_pieces": len(new_uniques), "new_sets": len(new_sets),
              "deferred_uniques": deferred, "deferred_sets": deferred_sets}
    print(json.dumps(result, ensure_ascii=True, indent=2))
    return result

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    generate(parser.parse_args().write)
