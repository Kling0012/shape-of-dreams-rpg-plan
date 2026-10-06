"""Compile Infinity supply and pressure values without publishing outputs."""
import json
from decimal import Decimal
from pathlib import Path
from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/infinity.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Infinity.Generated.cs"
# Frozen compatibility comparator only; never a runtime or generation fallback.
LEGACY = {
    "schemaVersion": 1,
    "rates": dict(referenceSeconds=2100, relicsPerHour=24, guaranteesPerHour=Decimal(".25"), shardsPerHour=180,
                  tuningPerHour=6, xpPerHour=1200, starXpPerHour=780, awakeningPerHour=780,
                  dustConversionsPerHour=6, merchantsPerHour=6),
    "killMix": dict(lesser=200, normal=160, miniBoss=5, boss=4),
    "bursts": dict(lesserTime=10, normalTime=8, miniBossTime=1, bossTime=1, highRare=7, relics=24,
                   legendary=7, guaranteeOpportunities=1, guaranteedRelics=2, shards=30, tuning=3,
                   xp=50, starXp=20, awakening=20, dustConversions=1, merchants=1),
    "rooms": dict(lesser=dict(cap=10, increment=5), normal=dict(cap=8, increment=4),
                  miniBoss=dict(cap=1, increment=Decimal(".125")), boss=dict(cap=1, increment=Decimal(".1"))),
    "run": dict(defaultInterval=10, shortInterval=10, middleInterval=15, longInterval=20, maximumPressureStage=100),
}

# Separate from the frozen legacy comparator: new tuning has no historical fallback.
SCALING_SHAPE = {
    key: dict(pressureOffset=0, enemyCountBonus=0, relicMultiplier=1, ordinaryBudgetMultiplier=1)
    for key in ("short", "middle", "long")
}


def _leaves(data, prefix=""):
    for key, value in data.items():
        path = f"{prefix}.{key}" if prefix else key
        if isinstance(value, dict):
            yield from _leaves(value, path)
        elif path != "schemaVersion":
            yield path, value


def _name(path):
    group, key, *tail = path.split(".")
    name = key[0].upper() + key[1:]
    if group == "killMix": return "KillMix" + name
    if group == "bursts": return name + "Burst"
    if group == "rooms": return name + "Room" + tail[0].capitalize()
    if group == "intervalScaling": return name + "".join(part[0].upper() + part[1:] for part in tail)
    return name


def _literal(value):
    return format(Decimal(value), "f").rstrip("0").rstrip(".") if Decimal(value) % 1 else str(int(value))


def validate(data):
    def shape(value, reference, path):
        if isinstance(reference, dict):
            if type(value) is not dict or set(value) != set(reference):
                raise ValueError(f"{path}: expected exactly {', '.join(reference)}")
            for key in reference: shape(value[key], reference[key], f"{path}.{key}")
        elif path == "infinity.schemaVersion":
            if type(value) is not int or value != 1: raise ValueError(f"{path}: expected integer 1")
        else:
            integer = ".run." in path or ".killMix." in path or path.endswith(".pressureOffset")
            if type(value) not in ((int,) if integer else (int, Decimal)):
                raise ValueError(f"{path}: expected {'integer' if integer else 'number'} (not boolean)")
            number = Decimal(value)
            if not number.is_finite() or not 0 <= number <= 2147483647:
                raise ValueError(f"{path}: expected finite nonnegative value <= Int32.MaxValue")
            if number * 1000000 != (number * 1000000).to_integral_value():
                raise ValueError(f"{path}: maximum precision is six decimal places")
    shape(data, {**LEGACY, "intervalScaling": SCALING_SHAPE}, "infinity")
    if data["rates"]["referenceSeconds"] <= 0: raise ValueError("infinity.rates.referenceSeconds: must be positive")
    run = data["run"]
    if not 1 <= run["shortInterval"] < run["middleInterval"] < run["longInterval"] <= 4096:
        raise ValueError("infinity.run: require ordered positive intervals <= graph safety capacity 4096")
    if not 0 <= run["maximumPressureStage"] <= 100:
        raise ValueError("infinity.run.maximumPressureStage: expected 0..100 (existing stage safety capacity)")
    if run["defaultInterval"] not in (run["shortInterval"], run["middleInterval"], run["longInterval"]):
        raise ValueError("infinity.run.defaultInterval: must be a valid interval")
    for tier, row in data["rooms"].items():
        if row["increment"] > row["cap"]: raise ValueError(f"infinity.rooms.{tier}: increment must not exceed cap")
    for interval, row in data["intervalScaling"].items():
        if row["pressureOffset"] > 100 or row["enemyCountBonus"] > 4 or not 1 <= row["relicMultiplier"] <= 2:
            raise ValueError(f"infinity.intervalScaling.{interval}: require offset <=100, bonus <=4, multiplier 1..2")
        if row["ordinaryBudgetMultiplier"] != row["relicMultiplier"]:
            raise ValueError(f"infinity.intervalScaling.{interval}: ordinary budget multiplier must match relic multiplier")
    return data


def load(path=PATH):
    return validate(read_json(path))


def fingerprint_record(data):
    validate(data)
    previous = dict(_leaves(LEGACY))
    records = []
    for path, _ in _leaves({**LEGACY, "intervalScaling": SCALING_SHAPE}):
        value = data
        for key in path.split("."):
            value = value[key]
        if path not in previous or value != previous[path]:
            kind = "int" if path.startswith(("run.", "killMix.")) or path.endswith(".pressureOffset") else "double"
            unit = ("seconds" if path == "rates.referenceSeconds"
                    else "credits/hour" if path.startswith("rates.")
                    else "stages" if path == "run.maximumPressureStage" or path.endswith(".pressureOffset")
                    else "rooms" if path.startswith("run.")
                    else "multiplier" if path.startswith("intervalScaling.")
                    else "kills/reference" if path.startswith("killMix.")
                    else "credits/room" if path.startswith("rooms.") and path.endswith(".increment")
                    else "credits")
            records.append(f"{path.replace('.', '/')}:{kind}:{unit}:{_literal(value)}")
    return "balance:infinity:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />", "// Source: tools/balance/infinity.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class InfinityBalance", "    {"]
    for path, _ in _leaves({**LEGACY, "intervalScaling": SCALING_SHAPE}):
        value = data
        for key in path.split("."):
            value = value[key]
        kind = "int" if path.startswith(("run.", "killMix.")) or path.endswith(".pressureOffset") else "double"
        lines.append(f"        internal const {kind} {_name(path)} = {_literal(value)};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    return {OUTPUT: "\n".join(lines + ["    }", "}", ""])}
