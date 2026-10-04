# -*- coding: utf-8 -*-
"""Generates src/SodRpg.Core/Game/NamedItems.Data.cs from the authored JSON (spec 3.2 / 4).

Reads tools/lowrarity/named.json (360 銘品) and tools/lowrarity/minisets.json (30 組) and
converts every {power, band} pair into a FIXED integer value using the production power
range (Content.PowerPools). A power's Min-Max is identical in every slot pool that
contains it (checked here), so the range is looked up across all slot pools.

Band -> value (round half up, exact integer arithmetic; clamped to [min, max]):
  low  = round(min + (max - min) * 1/6)   = (5*min +   max +  3) // 6
  mid  = round(min + (max - min) * 0.40)  = (6*min + 4*max +  5) // 10
  high = round(min + (max - min) * 0.65)  = (7*min + 13*max + 10) // 20

Rarity rules mirror Content.PowerAllowedForRarity (Content.cs:5086-5089):
droppable (in a slot pool, not None/ShadowStep/currency) and, below Epic, not a
conditional AD/AP power (NewPowersV129.IsConditionalAttribute). A mini-set's 3-piece
power must be allowed for the LOWEST rarity among its pieces (spec 3.2: conditional
AD/AP only in all-Epic sets).

Any entry that cannot be converted (unknown power/stat, power not allowed for the
rarity, power with no slot-pool range, unknown base/piece, duplicate id) is listed and
the script exits non-zero. Entries are never skipped.

Run:  python tools/lowrarity/gen_named_cs.py
"""
import collections
import json
import re
import sys

SRC = "src/SodRpg.Core/Game/Content.cs"
IDS = "src/SodRpg.Core/Game/Ids.cs"
NEWP = "src/SodRpg.Core/Game/NewPowersV129.cs"
IN_NAMED = "tools/lowrarity/named.json"
IN_SETS = "tools/lowrarity/minisets.json"
OUT = "src/SodRpg.Core/Game/NamedItems.Data.cs"

BAND_T = {"low": (1, 6), "mid": (2, 5), "high": (13, 20)}  # value = min + (max-min)*num/den


def band_value(mn, mx, band):
    """Round-half-up of mn + (mx-mn)*num/den in exact integer arithmetic, clamped to [mn, mx]."""
    num, den = BAND_T[band]
    value = (mn * (den - num) + mx * num + den // 2) // den
    return max(mn, min(mx, value))


def load_reference():
    src = open(SRC, encoding="utf-8").read()
    ids = open(IDS, encoding="utf-8").read()
    newp = open(NEWP, encoding="utf-8").read()

    def enum_names(name):
        m = re.search(r"enum %s\s*\{(.*?)\n    \}" % name, ids, re.S)
        body = re.sub(r"///.*", "", m.group(1))
        return re.findall(r"^\s*(\w+)\s*=", body, re.M)

    powers = enum_names("Power")
    stats = enum_names("Stat")
    rarities = enum_names("Rarity")
    power_set, stat_set, rarity_rank = set(powers), set(stats), {r: i for i, r in enumerate(rarities)}

    pool_m = re.search(r"PowerPools = new Dictionary<Slot, PowerRange\[\]>\s*\{(.*?)\n        \};", src, re.S)
    pools = collections.defaultdict(dict)
    for slot, chunk in re.findall(r"\[Slot\.(\w+)\] = new\[\]\s*\{(.*?)\n            \}", pool_m.group(1), re.S):
        for p, mn, mx in re.findall(r"PowerRange\(Power\.(\w+), (\d+), (\d+)\)", chunk):
            if p in pools[slot]:
                raise SystemExit("duplicate PowerRange in slot %s: %s" % (slot, p))
            pools[slot][p] = (int(mn), int(mx))
    # A power's range must be identical in every slot pool that carries it.
    ranges = {}
    for slot, ps in pools.items():
        for p, rng in ps.items():
            if p in ranges and ranges[p] != rng:
                raise SystemExit("power %s has different ranges across slot pools: %s vs %s" % (p, ranges[p], rng))
            ranges[p] = rng

    bases = {}
    base_pat = re.compile(r'new BaseDef\("([^"]+)", Slot\.(\w+), Line\.\w+, new Txt\("[^"]*", "[^"]*"\), '
                          r"Stat\.\w+, -?\d+(?:, Family\.(\w+))?\)")
    for m in base_pat.finditer(src):
        if m.group(1) in bases:
            raise SystemExit("duplicate BaseDef id: " + m.group(1))
        bases[m.group(1)] = m.group(2)

    conditional = set(re.findall(r"case Power\.(\w+):", re.search(
        r"IsConditionalAttribute\(Power p\)\s*\{\s*switch \(p\)\s*\{\s*(.*?)default:", newp, re.S).group(1)))
    # CurrencyStars.IsPower: KillGoldPct..DreamDustDelvePct (contiguous in the Power enum).
    ki, di = powers.index("KillGoldPct"), powers.index("DreamDustDelvePct")
    currency = set(powers[ki:di + 1])
    return bases, pools, ranges, power_set, stat_set, rarity_rank, conditional, currency


def droppable(power, ranges, power_set, currency):
    """Content.IsPowerDroppable + 'has a range in a slot pool'."""
    return (power in power_set and power not in ("None", "ShadowStep")
            and power not in currency and power in ranges)


def allowed_for_rarity(power, rarity, ranges, power_set, rarity_rank, conditional, currency):
    """Content.PowerAllowedForRarity (Content.cs:5088)."""
    if not droppable(power, ranges, power_set, currency):
        return False
    return rarity_rank[rarity] >= rarity_rank["Epic"] or power not in conditional


def cs(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    bases, pools, ranges, power_set, stat_set, rarity_rank, conditional, currency = load_reference()
    named_json = json.load(open(IN_NAMED, encoding="utf-8"))
    sets_json = json.load(open(IN_SETS, encoding="utf-8"))
    errs = []

    def check(cond, msg):
        if not cond:
            errs.append(msg)
        return cond

    seen_ids, named_out = set(), []
    for e in named_json:
        nid = e.get("id", "<no id>")
        if nid in seen_ids:
            errs.append("%s: duplicate named id" % nid)
        seen_ids.add(nid)
        slot, rarity, base_id = e.get("slot"), e.get("rarity"), e.get("baseId")
        if slot not in ("Weapon", "Armor", "Charm", "Head", "Hands", "Feet"):
            errs.append("%s: unknown slot %r" % (nid, slot))
        if rarity not in rarity_rank:
            errs.append("%s: unknown rarity %r" % (nid, rarity))
        if base_id not in bases:
            errs.append("%s: unknown baseId %r" % (nid, base_id))
        elif slot in ("Weapon", "Armor", "Charm", "Head", "Hands", "Feet") and bases[base_id] != slot:
            errs.append("%s: base %s is Slot.%s, entry says %s" % (nid, base_id, bases[base_id], slot))
        values = []
        for q in e.get("powers", []):
            power, band = q.get("power"), q.get("band")
            if power not in power_set:
                errs.append("%s: unknown power %r" % (nid, power))
                values.append(None)
                continue
            if power not in ranges:
                errs.append("%s: power %s has no PowerRange in any slot pool" % (nid, power))
                values.append(None)
                continue
            if rarity in rarity_rank and not allowed_for_rarity(
                    power, rarity, ranges, power_set, rarity_rank, conditional, currency):
                errs.append("%s: power %s is not allowed for rarity %s" % (nid, power, rarity))
            if band not in BAND_T:
                errs.append("%s: unknown band %r for power %s" % (nid, band, power))
                values.append(None)
                continue
            mn, mx = ranges[power]
            values.append(band_value(mn, mx, band))
        if len(values) not in (1, 2) or any(v is None for v in values):
            continue  # already reported; do not emit a broken def
        name = "new Txt(%s, %s)" % (cs(e["nameJa"]), cs(e["nameEn"]))
        lore = "new Txt(%s, %s)" % (cs(e["loreJa"]), cs(e["loreEn"]))
        mini = "null" if e.get("miniSetId") is None else cs(e["miniSetId"])
        args = [cs(nid), cs(base_id), "Rarity." + rarity, name, lore, mini]
        for power, value in zip([q["power"] for q in e["powers"]], values):
            args.extend(["Power." + power, str(value)])
        named_out.append(args)

    set_ids, sets_out = set(), []
    for s in sets_json:
        sid = s.get("id", "<no id>")
        if sid in set_ids:
            errs.append("%s: duplicate mini-set id" % sid)
        set_ids.add(sid)
        pieces = s.get("pieces", [])
        piece_rarities = []
        ok = True
        for p in pieces:
            match = next((e for e in named_json if e.get("id") == p), None)
            if match is None:
                errs.append("%s: piece %r is not a named item" % (sid, p))
                ok = False
            else:
                piece_rarities.append(match["rarity"])
        two = s.get("twoPiece") or {}
        stat, value = two.get("stat"), two.get("value")
        if stat not in stat_set:
            errs.append("%s: unknown twoPiece stat %r" % (sid, stat))
            ok = False
        three = s.get("threePiece")
        power, band = (three or {}).get("power"), (three or {}).get("band")
        three_line = None
        if three is not None:
            if power not in power_set:
                errs.append("%s: unknown threePiece power %r" % (sid, power))
                ok = False
            elif power not in ranges:
                errs.append("%s: threePiece power %s has no PowerRange in any slot pool" % (sid, power))
                ok = False
            else:
                # 設計 3.2: 条件付き攻撃力・魔力は全部位がエピックの組だけ（= 最低レア度で判定）。
                lowest = min(piece_rarities, key=lambda r: rarity_rank.get(r, 99)) if piece_rarities else None
                if lowest is not None and not allowed_for_rarity(
                        power, lowest, ranges, power_set, rarity_rank, conditional, currency):
                    errs.append("%s: threePiece power %s is not allowed for piece rarity %s" % (sid, power, lowest))
                if band != "low":
                    errs.append("%s: threePiece band must be low, got %r" % (sid, band))
                else:
                    mn, mx = ranges[power]
                    three_line = ("Power." + power, band_value(mn, mx, "low"))
        if not ok:
            continue
        piece_args = ", ".join(cs(p) for p in pieces)
        args = [cs(sid), "new Txt(%s, %s)" % (cs(s["nameJa"]), cs(s["nameEn"])),
                "new[] { %s }" % piece_args, "new StatLine(Stat.%s, %s)" % (stat, value)]
        if three_line is not None:
            args.append("new PowerLine(%s, %s)" % three_line)
        sets_out.append(args)

    if errs:
        print("ERRORS (%d):" % len(errs))
        for e in errs:
            print(" -", e)
        sys.exit(1)

    lines = []
    w = lines.append
    w("// <auto-generated>")
    w("//   このファイルは tools/lowrarity/gen_named_cs.py が生成しました。手で書き換えないこと。")
    w("//   元データ: tools/lowrarity/named.json (%d 銘品) / tools/lowrarity/minisets.json (%d 組)。"
      % (len(named_out), len(sets_out)))
    w("//   帯の固定値（四捨五入・範囲内に丸める）: low = min + (max-min)/6,")
    w("//   mid = min + (max-min)*0.40, high = min + (max-min)*0.65（設計 3.2）。")
    w("// </auto-generated>")
    w("namespace SodRpg.Core.Game")
    w("{")
    w("    /// <summary>銘品・組の本体データ（v1.32 設計 3.2・4）。NamedItems の初期化で登録される。</summary>")
    w("    internal static class NamedItemsData")
    w("    {")
    w("        internal static readonly NamedDef[] Named =")
    w("        {")
    for args in named_out:
        w("            new NamedDef(%s," % args[0])
        w("                %s, %s, %s," % (args[1], args[2], args[3]))
        w("                %s, %s," % (args[4], args[5]))
        w("                %s)," % ", ".join(args[6:]))
    w("        };")
    w("")
    w("        internal static readonly MiniSetDef[] MiniSets =")
    w("        {")
    for args in sets_out:
        w("            new MiniSetDef(%s, %s," % (args[0], args[1]))
        w("                %s," % args[2])
        w("                %s)," % ", ".join(args[3:]))
    w("        };")
    w("    }")
    w("}")
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("OK: %s (%d named, %d mini sets)" % (OUT, len(named_out), len(sets_out)))


if __name__ == "__main__":
    main()
