"""Resolve star numeric references before canonical validation or C# rendering."""

from copy import deepcopy
from decimal import Decimal, DecimalException, ROUND_HALF_EVEN, localcontext
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
TABLE_PATH = ROOT / "tools/balance/stars.json"
GROWTH_TABLE_PATH = ROOT / "tools/balance/run-growth.json"
MANIFEST_DIR = ROOT / "tools/star-manifest"
HEROES = ("aurena", "bismuth", "cetus", "husk", "lacerta", "mist", "nachia", "vesper", "yubar")
HERO_KEYS = {"Hero_" + name.title() for name in HEROES}
INT_MIN, INT_MAX = -(1 << 31), (1 << 31) - 1
# FNV-1a identity of the original adopted key/milli content, not a file hash.
# Keeping one identity avoids a second numeric original beside stars.json.
INITIAL_CONTENT_IDENTITY = 14722834479925839058
KINDS = frozenset(("MemoryDamage", "MemoryHaste", "GimmickBoost", "GimmickParam", "Notable", "Keystone", "Stat"))
EXAMPLE_KINDS = {
    "h.cetus.cluster.icy-veins/2/amount": "GimmickBoost",
    "h.cetus.cluster.icy-veins/3/amount": "GimmickParam",
    "h.cetus.cluster.icy-veins/4/amount": "GimmickParam",
    "h.cetus.cluster.icy-veins/5/options/2/amount": "Notable",
    "h.cetus.cluster.icy-veins/6/amount": "GimmickBoost",
    "h.cetus.cluster.frozen-recall/1/amount": "MemoryHaste",
    "h.cetus.cluster.frozen-recall/3/amount": "GimmickParam",
    "h.cetus.cluster.abyssal-shell/1/amount": "Stat",
    "h.cetus.cluster.abyssal-shell/3/amount": "GimmickParam",
    "h.cetus.cluster.abyssal-shell/4/options/1/amount": "Stat",
    "h.cetus.cluster.abyssal-shell/4/options/2/amount": "Notable",
}


def legacy_metadata(key):
    """Typed schema for generated legacy factory fields; contains no numeric originals."""
    path = key.removeprefix("legacy/")
    owner = "Hero_Cetus" if path.startswith("example/cetus/") else "shared" if path.startswith("t.") else "Hero_" + path.split(".")[1].title()
    unit = "float" if path.endswith(("/cooldown", "/windowDuration")) else "int"
    if "/native/" in path:
        field = path.rsplit("/", 1)[1]
        return "Keystone", owner, "float" if field in ("duration", "cooldown") else "hundredth" if path == "h.cetus.key2/native/value" else "decimal"
    if path.startswith("example/cetus/"):
        kind = EXAMPLE_KINDS.get(path.removeprefix("example/cetus/"), "Notable")
    elif "/stat/" in path:
        kind = "Stat"
    elif "/link/" in path:
        kind = "MemoryHaste"
    elif re.match(r"h\.\w+\.key2?/", path) or re.match(r"t\.\w+\.key/", path):
        kind = "Keystone"
    else:
        kind = "Notable"
    return kind, owner, unit


def legacy_fields(table, root=ROOT):
    # References are the schema boundary. No literal numeric value is recovered.
    paths = ("src/SodRpg.Core/Game/HeroStarRoutes.cs", "src/SodRpg.Core/Game/HeroSigils.cs",
             "src/SodRpg.Core/Game/Content.cs", "src/SodRpg.Core/Game/StarClusters.cs",
             "src/SodRpg.Core/Game/StarClusters/Cetus.Example.cs",
             "src/SodRpg.Core/Game/PairCombos.cs",
             "src/SodRpg.Core/Game/Mechanisms/SacrificeShield.cs", "src/SodRpg.Core/Game/Mechanisms/StunSourceFilter.cs",
             "src/SodRpg.Core/Game/Mechanisms/AuthoredKeystoneCompiler.cs")
    fields = {field_name(key): key for key in table["effects"] if key.startswith("legacy/")}
    result = {}
    for path in paths:
        text = (Path(root) / path).read_text(encoding="utf-8")
        for field in re.findall(r"MemoryDamageBalance\.(Effect_legacy_\w+)", text):
            if field not in fields:
                raise ValueError(f"{path}: missing balance original for {field}")
            key = fields[field]
            result[key] = legacy_metadata(key)
    return result


def migrated_ids(manifests):
    return {s["id"] for data in manifests.values() for s in data["stars"]
            if s.get("region") == "migration" and s.get("kind") is not None}


def legacy_adopted(key, manifests):
    if "/native/" in key or "/example/" in key:
        return True
    sid = key.removeprefix("legacy/").split("/", 1)[0]
    if sid not in migrated_ids(manifests):
        return True
    # These keys' typed upside is explicitly their existing baseline Power.
    return any(s["id"] == sid and s.get("kind") == "Keystone"
               and s["keystone"]["upsideSpec"] is None and not s.get("power")
               for data in manifests.values() for s in data["stars"])


def legacy_numeric_source(path, table=None):
    """Feed legacy topology parsers resolved factory arguments, never stale literals."""
    table = load_table() if table is None else table
    manifests = load_manifests()
    fields = {field_name(key): key for key in table["effects"] if key.startswith("legacy/")}
    def replacement(match):
        field = match[1]
        if field not in fields:
            raise ValueError(f"{path}: missing numeric original for {field}")
        key = fields[field]
        kind, owner, unit = legacy_metadata(key)
        value = effective_value(table, key, owner, key, kind, unit) if legacy_adopted(key, manifests) else number(table["effects"][key], key)
        return format(number(value, key), "f") + ("f" if unit == "float" else "")
    text = (ROOT / path).read_text(encoding="utf-8")
    return re.sub(r"MemoryDamageBalance\.(Effect_legacy_\w+)", replacement, text)

# Identity of the stage2 adopted semantic payloads at the initial cutover.
INITIAL_STAGE2_IDENTITY = 13790957529443898316
# Original adopted effect percentages add no new record at the numeric cutover.
INITIAL_GROWTH_EFFECT_PERCENT_IDENTITY = 13997727163470566896


def integer(value, path):
    value = number(value, path)
    if value != value.to_integral_value() or not INT_MIN <= value <= INT_MAX:
        raise ValueError(f"{path}: requires an exact Int32; fractional quantities cannot be rounded")
    return int(value)


def semantic_fields(obj):
    """Yield only authored quantities, with their existing consumer's precision."""
    kind = obj.get("kind")
    if kind in ("MemoryDamage", "MemoryHaste", "GimmickBoost", "GimmickParam"):
        unit = "milli" if kind == "MemoryDamage" else "int" if kind == "MemoryHaste" or obj.get("param") == "ExtraTargets" else "hundredth"
        yield obj, "value", "value", unit
    if kind in ("Notable", "Stat", "Keystone"):
        for field in ("power", "stat"):
            if obj.get(field):
                yield obj[field], "perRank", field + "/perRank", "int"
        if obj.get("gimmick"):
            yield from gimmick_fields(obj["gimmick"], "gimmick")
    if kind == "Keystone" and obj.get("keystone"):
        for i, spec in enumerate(obj["keystone"].get("upsideSpec") or []):
            prefix = f"keystone/upsideSpec/{i}"
            # Arg is a recipient/element enum except Ricochet's explicit count.
            structural = spec.get("field") == "Arg" and spec.get("effect") != "Ricochet"
            for field in ("pct", "from", "to", "delta", "max"):
                if field in spec and not structural:
                    unit = "hundredth" if field == "pct" else "int" if spec.get("field") in ("Arg", "ExtraTargets", "EveryN") else "decimal"
                    yield spec, field, prefix + "/" + field, unit
            if spec.get("gimmick"):
                g = spec["gimmick"]
                if g.get("effect") in ("SacrificeShield", "StunSourceFilter"):
                    sid = "h.aurena.key2" if g["effect"] == "SacrificeShield" else "h.cetus.key2"
                    yield g, "value", f"legacy/{sid}/native/value", "decimal"
                    if g["effect"] == "StunSourceFilter":
                        yield g, "cooldown", f"legacy/{sid}/native/cooldown", "float"
                else:
                    yield from gimmick_fields(g, prefix + "/gimmick")


def gimmick_fields(g, prefix):
    effect = g.get("effect")
    # Flag-only native adapters and no-amount modes carry no quantity in Value.
    flag = effect in ("SacrificeShield", "StunSourceFilter") or effect == "IdentityStrike" and g.get("strike", {}).get("mode") == "DashBonusAsMemory" or effect == "MemoryTuning" and g.get("tuning", {}).get("kind") == "StanceSwordQiAttackBasis"
    if not flag and "valuesByRank" not in g:
        ordinary = effect in ("Element", "ElementEdge", "Echo", "Burst", "Sap", "Wound", "Shield", "Rampart", "Heal", "PackMend", "Expose", "Ricochet", "Crescendo", "Daze", "Empower", "Quicken", "Weakspot", "Siphon", "Reload")
        yield g, "value", prefix + "/value", "precise" if ordinary else "hundredth"
    if g.get("cooldown") != 0 and effect not in ("IdentityStrike", "MemoryTuning", "StunSourceFilter"):
        yield g, "cooldown", prefix + "/cooldown", "float"
    if effect in ("Ricochet", "Reload"):
        yield g, "arg", prefix + "/arg", "int"
    if "everyN" in g:
        yield g, "everyN", prefix + "/everyN", "int"
    for i in range(len(g.get("valuesByRank", []))):
        yield g["valuesByRank"], i, prefix + f"/valuesByRank/{i}", "hundredth"
    strike = g.get("strike")
    if strike:
        for field in ("range", "width", "windowSeconds", "bonusSpeed", "maxTargets"):
            if field in strike:
                yield strike, field, prefix + "/strike/" + field, "int" if field == "maxTargets" else "hundredth" if field == "bonusSpeed" else "float"


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


def load_growth_table(path=GROWTH_TABLE_PATH):
    return validate_growth_table(read_json(path), path)


def validate_growth_table(table, path=GROWTH_TABLE_PATH):
    if not isinstance(table, dict) or set(table) != {"schemaVersion", "effects"}:
        raise ValueError(f"{path}: expected exactly schemaVersion and effects")
    if type(table["schemaVersion"]) is not int or table["schemaVersion"] != 1:
        raise ValueError(f"{path}/schemaVersion: expected integer 1")
    effects = table["effects"]
    if not isinstance(effects, dict):
        raise ValueError(f"{path}/effects: expected an object")
    for key, value in effects.items():
        if not isinstance(key, str) or not key:
            raise ValueError(f"{path}/effects: keys must be nonempty strings")
        context = f"{path}/effects/{key}"
        field = key.rsplit("/", 1)[-1]
        if field == "amount":
            amount = milli(value, context)
            if not 1 <= amount <= 1000000:
                raise ValueError(f"{context}: amount must be 0.001..1000 in exact thousandths")
        else:
            bounds = {"threshold": (1, 1000), "cap": (1, 500),
                      "capBonus": (0, 200), "effectPct": (0, 500)}
            if field not in bounds:
                raise ValueError(f"{context}: unknown growth numeric field")
            lo, hi = bounds[field]
            if not lo <= integer(value, context) <= hi:
                raise ValueError(f"{context}: {field} must be an integer {lo}..{hi}")
    return table


def validate_table(table, path=TABLE_PATH):
    if not isinstance(table, dict) or set(table) != {"schemaVersion", "multipliers", "effects", "rankScaling", "essenceSlots"}:
        raise ValueError(f"{path}: expected exactly schemaVersion, multipliers, effects, rankScaling, essenceSlots")
    if type(table["schemaVersion"]) is not int or table["schemaVersion"] != 1:
        raise ValueError(f"{path}/schemaVersion: expected integer 1")
    multipliers = table["multipliers"]
    if not isinstance(multipliers, dict) or set(multipliers) - {"byKind", "byHero"}:
        raise ValueError(f"{path}/multipliers: only byKind and byHero are supported")
    by_kind, by_hero = multipliers.get("byKind", {}), multipliers.get("byHero", {})
    if not isinstance(by_kind, dict) or set(by_kind) - KINDS:
        raise ValueError(f"{path}/multipliers/byKind: unknown adopted star kind")
    if not isinstance(by_hero, dict):
        raise ValueError(f"{path}/multipliers/byHero: expected an object")
    for hero, kinds in by_hero.items():
        if hero not in HERO_KEYS:
            raise ValueError(f"{path}/multipliers/byHero/{hero}: unknown Hero_* owner")
        if not isinstance(kinds, dict) or set(kinds) - KINDS:
            raise ValueError(f"{path}/multipliers/byHero/{hero}: unknown adopted star kind")
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
    rank = table["rankScaling"]
    if not isinstance(rank, dict) or set(rank) != {"damageGain", "pointDenominator", "maxPoints"}:
        raise ValueError(f"{path}/rankScaling: expected damageGain, pointDenominator, maxPoints")
    if number(rank["damageGain"], f"{path}/rankScaling/damageGain") < 0:
        raise ValueError(f"{path}/rankScaling/damageGain: expected nonnegative gain")
    for key in ("pointDenominator", "maxPoints"):
        if type(rank[key]) is not int or rank[key] <= 0:
            raise ValueError(f"{path}/rankScaling/{key}: expected positive integer")
    slots = table["essenceSlots"]
    if not isinstance(slots, dict) or set(slots) != {"maxAdded", "maxPerLocation", "starAmount"}:
        raise ValueError(f"{path}/essenceSlots: expected maxAdded, maxPerLocation and starAmount")
    for key in ("maxAdded", "maxPerLocation", "starAmount"):
        if type(slots[key]) is not int or not 0 <= slots[key] <= INT_MAX:
            raise ValueError(f"{path}/essenceSlots/{key}: expected nonnegative int32")
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


def effective_value(table, key, owner, path, kind="MemoryDamage", unit="milli"):
    if key not in table["effects"]:
        raise ValueError(f"{path}: missing reference tools/balance/stars.json/effects/{key}")
    base = number(table["effects"][key], f"tools/balance/stars.json/effects/{key}")
    multipliers = table["multipliers"]
    by_kind = number(multipliers.get("byKind", {}).get(kind, 1), f"multipliers/byKind/{kind}")
    by_hero = number(multipliers.get("byHero", {}).get(owner, {}).get(kind, 1),
                     f"multipliers/byHero/{owner}/{kind}") if owner != "shared" else Decimal(1)
    try:
        with localcontext() as context:
            operands = (base, by_kind, by_hero)
            context.prec = max(28, sum(len(v.as_tuple().digits) for v in operands))
            context.Emax = max(context.Emax, sum(max(0, v.adjusted()) for v in operands) + 1)
            context.Emin = min(context.Emin, sum(min(0, v.as_tuple().exponent) for v in operands))
            effective = base * by_kind * by_hero
            # Stage1's established MemoryDamage policy is unchanged.
            if kind == "MemoryDamage" and by_kind * by_hero != 1:
                effective = effective.quantize(Decimal("0.001"), rounding=ROUND_HALF_EVEN)
    except (DecimalException, ValueError, OverflowError) as error:
        raise ValueError(f"{path}: {kind} decimal product is not representable") from error
    if kind == "MemoryDamage":
        if effective <= 0:
            raise ValueError(f"{path}: MemoryDamage amount must be positive")
        milli(effective, path)
    elif unit == "int":
        return integer(effective, path)
    elif unit == "hundredth":
        scaled = Decimal((effective.as_tuple().sign, effective.as_tuple().digits, effective.as_tuple().exponent + 2))
        integer(scaled, path + " (hundredth units)")
    elif unit == "precise":
        scaled = Decimal((effective.as_tuple().sign, effective.as_tuple().digits, effective.as_tuple().exponent + 7))
        if scaled != scaled.to_integral_value() or not -(1 << 63) <= scaled < (1 << 63):
            raise ValueError(f"{path}: Gimmick value requires exact 0.0000001 units fitting Int64")
    elif unit == "decimal":
        digits = effective.as_tuple().digits
        exponent = effective.as_tuple().exponent
        while digits and digits[-1] == 0 and exponent < 0:
            digits, exponent = digits[:-1], exponent + 1
        coefficient = int("".join(map(str, digits)) or "0") * 10 ** max(0, exponent)
        if exponent < -28 or coefficient >= 1 << 96:
            raise ValueError(f"{path}: quantity must fit an exact System.Decimal")
    elif unit == "float":
        if abs(effective) > Decimal("3.4028234663852886e38"):
            raise ValueError(f"{path}: quantity must fit a finite Single")
    return effective


def resolve_manifest(name, raw, table, growth_table=None):
    """Return a fresh numeric view; raw valueRef objects remain untouched."""
    data = deepcopy(raw)
    owner = "shared" if name == "outer" else raw.get("hero")
    references = {}
    growth_table = load_growth_table() if growth_table is None else validate_growth_table(growth_table)
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
            growth = obj.get("growth")
            if not isinstance(growth, dict):
                raise ValueError(f"{path}/growth: expected an object")
            numeric_fields = ("threshold", "cap") if obj["kind"] == "RunGrowth" else ("capBonus", "effectPct")
            fields = [(field, growth, field) for field in numeric_fields]
            fields.extend((f"effects/{i}/amount", effect, "amount")
                          for i, effect in enumerate(growth.get("effects", [])))
            for field_path, container, field in fields:
                key = f"{sid}/{effect_path.removesuffix('value')}growth/{field_path}"
                reference = container.get(field)
                if not isinstance(reference, dict) or reference != {"valueRef": key}:
                    raise ValueError(f"{path}/growth/{field_path}: expected canonical valueRef {key}")
                if key not in growth_table["effects"]:
                    raise ValueError(f"{path}/growth/{field_path}: missing runGrowth reference {key}")
                value = number(growth_table["effects"][key], key)
                milli(value, key)
                if field != "amount":
                    if value != value.to_integral_value():
                        raise ValueError(f"{key}: expected an integer")
                    value = int(value)
                container[field] = value
                references[key] = value
        for effect_path, obj, path in objects:
            kind = obj.get("kind")
            if kind not in KINDS:
                if "valueRef" in obj:
                    raise ValueError(f"{path}/valueRef: kind does not support numeric references")
                continue
            prefix = effect_path.removesuffix("value")
            for container, field, field_path, unit in semantic_fields(obj):
                key = field_path if field_path.startswith("legacy/") else f"{sid}/{prefix}{field_path}"
                top = container is obj and field == "value"
                reference = container.get("valueRef") if top else container[field]
                if top and "value" in container:
                    raise ValueError(f"{path}/value: {kind} requires valueRef only; literal/mixed values are forbidden")
                expected = key if top else {"valueRef": key}
                if reference != expected:
                    raise ValueError(f"{path}/{field_path}: expected canonical valueRef {key}")
                if key in references:
                    raise ValueError(f"{path}/{field_path}: duplicate reference {key}")
                value = effective_value(table, key, owner, f"{path}/{field_path} ({key})", kind, unit)
                references[key] = value
                if top:
                    replacement = {("value" if k == "valueRef" else k): (value if k == "valueRef" else v)
                                   for k, v in obj.items()}
                    obj.clear()
                    obj.update(replacement)
                else:
                    container[field] = value
            gimmicks = [("gimmick", obj["gimmick"])] if obj.get("gimmick") else []
            if kind == "Keystone":
                gimmicks.extend((f"keystone/upsideSpec/{i}/gimmick", s["gimmick"])
                                for i, s in enumerate(obj.get("keystone", {}).get("upsideSpec") or []) if s.get("gimmick"))
            for gpath, gimmick in gimmicks:
                if gimmick.get("valuesByRank"):
                    key = f"{sid}/{prefix}{gpath}/valuesByRank/0"
                    if gimmick["value"] != {"valueRef": key}:
                        raise ValueError(f"{path}/{gpath}/value: rank-one payload must reference {key}")
                    gimmick["value"] = gimmick["valuesByRank"][0]
    return data, references


def resolve_all(table=None, manifests=None, root=ROOT, growth_table=None):
    table = load_table() if table is None else validate_table(table)
    manifests = load_manifests() if manifests is None else manifests
    growth_table = load_growth_table() if growth_table is None else validate_growth_table(growth_table)
    resolved, effective = {}, {}
    for name, raw in manifests.items():
        data, references = resolve_manifest(name, raw, table, growth_table)
        duplicates = effective.keys() & references.keys()
        if duplicates:
            raise ValueError(f"tools/star-manifest/{name}.json: duplicate reference {sorted(duplicates)[0]}")
        resolved[name] = data
        effective.update(references)
    legacy = legacy_components(root)
    for key, owner in legacy.items():
        effective[key] = effective_value(table, key, owner, f"tools/balance/stars.json/effects/{key}")
    for key, (kind, owner, unit) in legacy_fields(table, root).items():
        # Obsolete same-ID effects exist only in the baseline construction path.
        effective[key] = (effective_value(table, key, owner, key, kind, unit)
                          if legacy_adopted(key, resolved) else number(table["effects"][key], key))
    extra_growth = growth_table["effects"].keys() - effective.keys()
    if extra_growth:
        raise ValueError(f"tools/balance/run-growth.json/effects/{sorted(extra_growth)[0]}: extra unreferenced effect")
    extra = table["effects"].keys() - effective.keys()
    if extra:
        raise ValueError(f"tools/balance/stars.json/effects/{sorted(extra)[0]}: extra unreferenced effect")
    return resolved, effective


def effect_schema(manifests, effective):
    result = {}
    for data in manifests.values():
        for star in data["stars"]:
            for prefix, obj in [("", star)] + [(f"options/{i}/", o) for i, o in enumerate(star.get("options") or [])]:
                for _, _, path, unit in semantic_fields(obj):
                    key = path if path.startswith("legacy/") else f"{star['id']}/{prefix}{path}"
                    result[key] = (obj["kind"], unit)
    for key in effective:
        if key.startswith("legacy/"):
            kind, _, unit = legacy_metadata(key)
            result[key] = kind, unit
        elif key.endswith("/link/value"):
            result[key] = "MemoryDamage", "milli"
    return result


def content_identity(record):
    identity = 14695981039346656037
    for byte in record.encode("utf-8"):
        identity = ((identity ^ byte) * 1099511628211) & ((1 << 64) - 1)
    return identity


def canonical_number(value):
    text = format(number(value, "fingerprint"), "f")
    return (text.rstrip("0").rstrip(".") if "." in text else text) or "0"


def stage2_record(manifests, effective):
    schema = effect_schema(manifests, effective)
    return "balance:stars:semantic:v2|" + "|".join(
        f"{key}:{schema[key][0]}:{schema[key][1]}:{canonical_number(value)}"
        for key, value in sorted(effective.items())
        if key in schema and schema[key][0] != "MemoryDamage"
        and (not key.startswith("legacy/") or legacy_adopted(key, manifests)))


def fingerprint_record(manifests, effective):
    replaced = migrated_ids(manifests)
    schema = effect_schema(manifests, effective)
    adopted = {key: value for key, value in effective.items()
               if key in schema and schema[key][0] == "MemoryDamage"
               and (not key.endswith("/link/value") or key[:-len("/link/value")] not in replaced)}
    record = "balance:stars:MemoryDamage:v1|" + "|".join(
        f"{key}:milli:{milli(value, key)}" for key, value in sorted(adopted.items()))
    records = [] if content_identity(record) == INITIAL_CONTENT_IDENTITY else [record]
    semantic = stage2_record(manifests, effective)
    if content_identity(semantic) != INITIAL_STAGE2_IDENTITY:
        records.append(semantic)
    return "\n".join(records) or None


def decimal_literal(value):
    value = number(value, "generated MemoryDamage")
    return format(value.normalize(), "f") + "m"


def render_balance(manifests, effective):
    record = fingerprint_record(manifests, effective)
    lines = ["// <auto-generated />", "// Source: tools/balance/stars.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class MemoryDamageBalance", "    {"]
    # Manifest amounts are emitted by the star compiler; only legacy routes need fields.
    lines.extend(f"        internal static readonly decimal {field_name(key)} = {decimal_literal(value)};"
                 for key, value in sorted(effective.items()) if key.endswith("/link/value") and not key.startswith("legacy/"))
    for key, value in sorted(effective.items()):
        if not key.startswith("legacy/"):
            continue
        _, _, unit = legacy_metadata(key)
        cstype = "int" if unit == "int" else "float" if unit == "float" else "decimal"
        literal = str(integer(value, key)) if unit == "int" else format(number(value, key), "f") + ("f" if unit == "float" else "m")
        lines.append(f"        internal const {cstype} {field_name(key)} = {literal};")
    lines.extend(["        // Original adopted effective content adds no record, preserving existing fingerprints.",
                  "        internal static readonly string ContentFingerprintRecord = " +
                  ("null" if record is None else json.dumps(record)) + ";", "    }", "}", ""])
    return "\n".join(lines)


def rank_fingerprint_record(table, growth_table):
    rank = table["rankScaling"]
    record = ("balance:stars:rank:v1|gain:" + format(number(rank["damageGain"], "rankScaling/damageGain").normalize(), "f")
              + f"|denominator:{rank['pointDenominator']}|maxPoints:{rank['maxPoints']}|growth:"
              + "|".join(f"{key}:milli:{milli(value, key)}"
                         for key, value in sorted(growth_table["effects"].items())
                         if not key.endswith("/effectPct")))
    effect_percent = "balance:run-growth:effect-percent:v1|" + "|".join(
        f"{key}:int:{integer(value, key)}"
        for key, value in sorted(growth_table["effects"].items()) if key.endswith("/effectPct"))
    if content_identity(effect_percent) != INITIAL_GROWTH_EFFECT_PERCENT_IDENTITY:
        record += "\n" + effect_percent
    return record


def render_rank_balance(table=None, growth_table=None):
    table = load_table() if table is None else validate_table(table)
    growth_table = load_growth_table() if growth_table is None else validate_growth_table(growth_table)
    rank = table["rankScaling"]
    gain = number(rank["damageGain"], "rankScaling/damageGain") / rank["pointDenominator"]
    maximum = 1 + gain * rank["maxPoints"]
    record = rank_fingerprint_record(table, growth_table)
    return "\n".join([
        "// <auto-generated />",
        "// Sources: tools/balance/stars.json, tools/balance/run-growth.json; regenerate with python tools/balance/gen_cs.py.",
        "namespace SodRpg.Core.Game", "{", "    internal static class StarRankBalance", "    {",
        f"        internal const decimal DamagePerPoint = {decimal_literal(gain)};",
        f"        internal const decimal MaxMultiplier = {decimal_literal(maximum)};",
        f"        internal const int MaxPoints = {rank['maxPoints']};",
        f"        internal const int EssenceSlotsMaxAdded = {table['essenceSlots']['maxAdded']};",
        f"        internal const int EssenceSlotsMaxPerLocation = {table['essenceSlots']['maxPerLocation']};",
        f"        internal const int EssenceSlotStarAmount = {table['essenceSlots']['starAmount']};",
        "        internal static readonly string ContentFingerprintRecord = " + json.dumps(record) + ";",
        "    }", "}", ""])
