"""Resolve star numeric references before canonical validation or C# rendering."""

from copy import deepcopy
from decimal import Decimal, DecimalException, ROUND_HALF_EVEN, localcontext
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
TABLE_PATH = ROOT / "tools/balance/stars.json"
MANIFEST_DIR = ROOT / "tools/star-manifest"
HEROES = ("aurena", "bismuth", "cetus", "husk", "lacerta", "mist", "nachia", "vesper", "yubar")
HERO_KEYS = {"Hero_" + name.title() for name in HEROES}
INT_MIN, INT_MAX = -(1 << 31), (1 << 31) - 1
# FNV-1a identity of the original adopted key/milli content, not a file hash.
# Keeping one identity avoids a second numeric original beside stars.json.
INITIAL_CONTENT_IDENTITY = 14722834479925839058


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"), parse_float=Decimal,
                          object_pairs_hook=unique_object)
    except (ValueError, OSError) as error:
        raise ValueError(f"{path}: {error}") from error


def number(value, path):
    if isinstance(value, bool) or not isinstance(value, (int, Decimal)):
        raise ValueError(f"{path}: expected a finite decimal number (not a boolean)")
    result = Decimal(value)
    if not result.is_finite():
        raise ValueError(f"{path}: expected a finite decimal number")
    return result


def milli(value, path):
    value = number(value, path)
    # Adjusting the exponent is exact, unlike arithmetic in a rounded context.
    scaled = Decimal((value.as_tuple().sign, value.as_tuple().digits, value.as_tuple().exponent + 3))
    if scaled != scaled.to_integral_value():
        raise ValueError(f"{path}: MemoryDamage must be an exact 0.001 Link value; rounding is forbidden")
    if scaled < INT_MIN or scaled > INT_MAX:
        raise ValueError(f"{path}: MemoryDamage ValueMilli must fit Int32 ({INT_MIN}..{INT_MAX})")
    return int(scaled)


def field_name(key):
    return "Effect_" + re.sub(r"[^A-Za-z0-9]", "_", key)


def load_table(path=TABLE_PATH):
    return validate_table(read_json(path), path)


def validate_table(table, path=TABLE_PATH):
    if not isinstance(table, dict) or set(table) != {"schemaVersion", "multipliers", "effects", "runGrowth", "rankScaling"}:
        raise ValueError(f"{path}: expected exactly schemaVersion, multipliers, effects, runGrowth, rankScaling")
    if type(table["schemaVersion"]) is not int or table["schemaVersion"] != 1:
        raise ValueError(f"{path}/schemaVersion: expected integer 1")
    multipliers = table["multipliers"]
    if not isinstance(multipliers, dict) or set(multipliers) - {"byKind", "byHero"}:
        raise ValueError(f"{path}/multipliers: only byKind and byHero are supported")
    by_kind, by_hero = multipliers.get("byKind", {}), multipliers.get("byHero", {})
    if not isinstance(by_kind, dict) or set(by_kind) - {"MemoryDamage"}:
        raise ValueError(f"{path}/multipliers/byKind: only MemoryDamage is supported")
    if not isinstance(by_hero, dict):
        raise ValueError(f"{path}/multipliers/byHero: expected an object")
    for hero, kinds in by_hero.items():
        if hero not in HERO_KEYS:
            raise ValueError(f"{path}/multipliers/byHero/{hero}: unknown Hero_* owner")
        if not isinstance(kinds, dict) or set(kinds) - {"MemoryDamage"}:
            raise ValueError(f"{path}/multipliers/byHero/{hero}: only MemoryDamage is supported")
    for owner, kinds in [("byKind", by_kind)] + [("byHero/" + h, k) for h, k in by_hero.items()]:
        for kind, value in kinds.items():
            if number(value, f"{path}/multipliers/{owner}/{kind}") < 0:
                raise ValueError(f"{path}/multipliers/{owner}/{kind}: multiplier must be nonnegative")
    effects = table["effects"]
    if not isinstance(effects, dict):
        raise ValueError(f"{path}/effects: expected an object")
    fields = {}
    for key, value in effects.items():
        if not isinstance(key, str) or not key:
            raise ValueError(f"{path}/effects: keys must be nonempty strings")
        number(value, f"{path}/effects/{key}")
        if key.endswith("/link/value"):
            field = field_name(key)
            if field in fields:
                raise ValueError(f"{path}/effects/{key}: generated identifier collides with {fields[field]}")
            fields[field] = key
    growth = table["runGrowth"]
    if not isinstance(growth, dict):
        raise ValueError(f"{path}/runGrowth: expected an object")
    for key, value in growth.items():
        if not isinstance(key, str) or not key:
            raise ValueError(f"{path}/runGrowth: keys must be nonempty strings")
        milli(value, f"{path}/runGrowth/{key}")
    rank = table["rankScaling"]
    if not isinstance(rank, dict) or set(rank) != {"damageGain", "pointDenominator", "maxPoints"}:
        raise ValueError(f"{path}/rankScaling: expected damageGain, pointDenominator, maxPoints")
    if number(rank["damageGain"], f"{path}/rankScaling/damageGain") < 0:
        raise ValueError(f"{path}/rankScaling/damageGain: expected nonnegative gain")
    for key in ("pointDenominator", "maxPoints"):
        if type(rank[key]) is not int or rank[key] <= 0:
            raise ValueError(f"{path}/rankScaling/{key}: expected positive integer")
    return table


def load_manifests(directory=MANIFEST_DIR):
    return {name: read_json(Path(directory) / (name + ".json")) for name in (*HEROES, "outer")}


def legacy_components(root=ROOT):
    """Read route shape/ownership, never recover adopted numeric values from C#."""
    text = (Path(root) / "src/SodRpg.Core/Game/HeroStarRoutes.cs").read_text(encoding="utf-8")
    result = {}
    current, order = None, 0
    for line in text.splitlines():
        route = re.search(r'new Route\(nodes,\s*"(\w+)",\s*"([^"]+)",\s*"([^"]+)"', line)
        if route:
            current, order = route.groups(), 0
            continue
        call = re.search(r'\br\.(S|P|L|G|CapG)\((.*)\);', line)
        if current is None or call is None:
            continue
        order += 1
        kind, args = call.groups()
        if kind == "CapG" or kind == "L" and re.search(r'LinkKind\.MemoryDamage\s*,', args):
            key = f"h.{current[0].lower()}.route.{current[1]}.{order}/link/value"
            result[key] = "Hero_" + current[0]
    return result


def effective_value(table, key, owner, path):
    if key not in table["effects"]:
        raise ValueError(f"{path}: missing reference tools/balance/stars.json/effects/{key}")
    base = number(table["effects"][key], f"tools/balance/stars.json/effects/{key}")
    multipliers = table["multipliers"]
    kind = number(multipliers.get("byKind", {}).get("MemoryDamage", 1), "multipliers/byKind/MemoryDamage")
    hero = number(multipliers.get("byHero", {}).get(owner, {}).get("MemoryDamage", 1),
                  f"multipliers/byHero/{owner}/MemoryDamage") if owner != "shared" else Decimal(1)
    # Enough coefficient digits for all three operands means no implicit rounding.
    try:
        with localcontext() as context:
            operands = (base, kind, hero)
            context.prec = max(28, sum(len(v.as_tuple().digits) for v in operands))
            context.Emax = max(context.Emax, sum(max(0, v.adjusted()) for v in operands) + 1)
            context.Emin = min(context.Emin, sum(min(0, v.as_tuple().exponent) for v in operands))
            effective = base * kind * hero
            if kind * hero != 1:
                effective = effective.quantize(Decimal("0.001"), rounding=ROUND_HALF_EVEN)
    except (DecimalException, ValueError, OverflowError) as error:
        raise ValueError(f"{path}: MemoryDamage decimal product is not representable") from error
    if effective <= 0:
        raise ValueError(f"{path}: MemoryDamage amount must be positive")
    milli(effective, path)
    return effective


def resolve_manifest(name, raw, table):
    """Return a fresh numeric view; raw valueRef objects remain untouched."""
    data = deepcopy(raw)
    owner = "shared" if name == "outer" else raw.get("hero")
    references = {}
    for index, star in enumerate(data.get("stars", [])):
        sid = star.get("id")
        objects = [("value", star, f"tools/star-manifest/{name}.json/stars/{index}")]
        options = star.get("options")
        if isinstance(options, list):
            objects.extend((f"options/{i}/value", option, f"tools/star-manifest/{name}.json/stars/{index}/options/{i}")
                           for i, option in enumerate(options) if isinstance(option, dict))
        for effect_path, obj, path in objects:
            if obj.get("kind") not in ("RunGrowth", "RunGrowthMod"):
                continue
            growth = obj.get("growth", {})
            fields = [(field, growth, field) for field in ("threshold", "cap", "capBonus") if field in growth]
            fields.extend((f"effects/{i}/amount", effect, "amount")
                          for i, effect in enumerate(growth.get("effects", [])))
            for field_path, container, field in fields:
                key = f"{sid}/{effect_path.removesuffix('value')}growth/{field_path}"
                reference = container[field]
                if not isinstance(reference, dict) or reference != {"valueRef": key}:
                    raise ValueError(f"{path}/growth/{field_path}: expected canonical valueRef {key}")
                if key not in table["runGrowth"]:
                    raise ValueError(f"{path}/growth/{field_path}: missing runGrowth reference {key}")
                value = number(table["runGrowth"][key], key)
                milli(value, key)
                if field != "amount":
                    if value != value.to_integral_value():
                        raise ValueError(f"{key}: expected an integer")
                    value = int(value)
                container[field] = value
                references[key] = value
        for effect_path, obj, path in objects:
            if obj.get("kind") != "MemoryDamage":
                if "valueRef" in obj:
                    raise ValueError(f"{path}/valueRef: only MemoryDamage may reference the balance table")
                continue
            if "value" in obj:
                raise ValueError(f"{path}/value: MemoryDamage requires valueRef only; literal/mixed values are forbidden")
            key = f"{sid}/{effect_path}"
            if "valueRef" not in obj:
                raise ValueError(f"{path}/valueRef: missing MemoryDamage reference {key}")
            if obj["valueRef"] != key:
                raise ValueError(f"{path}/valueRef: expected canonical reference {key}, found {obj['valueRef']!r}")
            if key in references:
                raise ValueError(f"{path}/valueRef: duplicate reference {key}")
            value = effective_value(table, key, owner, f"{path}/valueRef ({key})")
            references[key] = value
            # Retain canonical ordered schema for all downstream graph/payload checks.
            replacement = {("value" if k == "valueRef" else k): (value if k == "valueRef" else v)
                           for k, v in obj.items()}
            obj.clear()
            obj.update(replacement)
    return data, references


def resolve_all(table=None, manifests=None, root=ROOT):
    table = load_table() if table is None else validate_table(table)
    manifests = load_manifests() if manifests is None else manifests
    resolved, effective = {}, {}
    for name, raw in manifests.items():
        data, references = resolve_manifest(name, raw, table)
        duplicates = effective.keys() & references.keys()
        if duplicates:
            raise ValueError(f"tools/star-manifest/{name}.json: duplicate reference {sorted(duplicates)[0]}")
        resolved[name] = data
        effective.update(references)
    legacy = legacy_components(root)
    for key, owner in legacy.items():
        effective[key] = effective_value(table, key, owner, f"tools/balance/stars.json/effects/{key}")
    extra = (table["effects"].keys() | table["runGrowth"].keys()) - effective.keys()
    if extra:
        raise ValueError(f"tools/balance/stars.json/effects/{sorted(extra)[0]}: extra unreferenced effect")
    return resolved, effective


def fingerprint_record(manifests, effective):
    replaced = {s["id"] for data in manifests.values() for s in data["stars"]
                if s.get("region") == "migration" and s.get("kind") is not None}
    adopted = {key: value for key, value in effective.items()
               if "/growth/" not in key and (not key.endswith("/link/value") or key[:-len("/link/value")] not in replaced)}
    record = "balance:stars:MemoryDamage:v1|" + "|".join(
        f"{key}:milli:{milli(value, key)}" for key, value in sorted(adopted.items()))
    identity = 14695981039346656037
    for byte in record.encode("utf-8"):
        identity = ((identity ^ byte) * 1099511628211) & ((1 << 64) - 1)
    return None if identity == INITIAL_CONTENT_IDENTITY else record


def decimal_literal(value):
    value = number(value, "generated MemoryDamage")
    return format(value.normalize(), "f") + "m"


def render_balance(manifests, effective):
    record = fingerprint_record(manifests, effective)
    lines = ["// <auto-generated />", "// Source: tools/balance/stars.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class MemoryDamageBalance", "    {"]
    # Manifest amounts are emitted by the star compiler; only legacy routes need fields.
    lines.extend(f"        internal static readonly decimal {field_name(key)} = {decimal_literal(value)};"
                 for key, value in sorted(effective.items()) if key.endswith("/link/value"))
    lines.extend(["        // Original adopted effective content adds no record, preserving existing fingerprints.",
                  "        internal static readonly string ContentFingerprintRecord = " +
                  ("null" if record is None else json.dumps(record)) + ";", "    }", "}", ""])
    return "\n".join(lines)


def render_rank_balance(table=None):
    table = load_table() if table is None else validate_table(table)
    rank = table["rankScaling"]
    gain = number(rank["damageGain"], "rankScaling/damageGain") / rank["pointDenominator"]
    maximum = 1 + gain * rank["maxPoints"]
    record = ("balance:stars:rank:v1|gain:" + format(number(rank["damageGain"], "rankScaling/damageGain").normalize(), "f")
              + f"|denominator:{rank['pointDenominator']}|maxPoints:{rank['maxPoints']}|growth:"
              + "|".join(f"{key}:milli:{milli(value, key)}" for key, value in sorted(table["runGrowth"].items())))
    return "\n".join([
        "// <auto-generated />",
        "// Source: tools/balance/stars.json; regenerate with python tools/balance/gen_cs.py.",
        "namespace SodRpg.Core.Game", "{", "    internal static class StarRankBalance", "    {",
        f"        internal const decimal DamagePerPoint = {decimal_literal(gain)};",
        f"        internal const decimal MaxMultiplier = {decimal_literal(maximum)};",
        f"        internal const int MaxPoints = {rank['maxPoints']};",
        "        internal static readonly string ContentFingerprintRecord = " + json.dumps(record) + ";",
        "    }", "}", ""])
