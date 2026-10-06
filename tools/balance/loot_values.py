"""Validate and stage typed drop and material balance; never publish here."""
from decimal import Decimal
import json
import math
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/loot.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Loot.Generated.cs"
INT_MAX = (1 << 31) - 1
# Frozen migration reference, not an editable source or runtime fallback.
LEGACY = json.loads('''{
  "schemaVersion": 1,
  "rarityWeights": [3000, 1350, 500, 30, 2],
  "dropChance": {"lesser": 0.008, "normal": 0.027, "miniBoss": 0.45, "boss": 1.0},
  "tierLuck": {"miniBoss": 0.6, "boss": 1.2},
  "heat": {"dropBonus": 0.35, "luck": 0.3},
  "rarityLuckCoefficient": 0.6,
  "bossExtraRelicChance": 0.6,
  "selection": {"focusWeight": 2, "setPieceWeight": 4, "setCompletionWeight": 60},
  "materials": {
    "lesser": {"shardChance": 0.05, "shards": 1},
    "normal": {"shardChance": 0.12, "shardMin": 1, "shardMax": 2},
    "miniBoss": {"shardMin": 5, "shardMax": 10, "tuningChance": 0.2, "tuning": 1},
    "boss": {"shardMin": 20, "shardMax": 30, "tuning": 1}
  },
  "variantAdditiveShards": 10
}''', parse_float=Decimal)
FIELDS = {
    "rarityWeights": ("RarityWeights", "weight"),
    "dropChance.lesser": ("LesserDropChance", "probability"),
    "dropChance.normal": ("NormalDropChance", "probability"),
    "dropChance.miniBoss": ("MiniBossDropChance", "probability"),
    "dropChance.boss": ("BossDropChance", "probability"),
    "tierLuck.miniBoss": ("MiniBossLuck", "luck"),
    "tierLuck.boss": ("BossLuck", "luck"),
    "heat.dropBonus": ("HeatDropBonus", "multiplier/heat"),
    "heat.luck": ("HeatLuck", "luck/heat"),
    "rarityLuckCoefficient": ("RarityLuckCoefficient", "multiplier/luck"),
    "bossExtraRelicChance": ("BossExtraRelicChance", "probability"),
    "selection.focusWeight": ("FocusWeight", "multiplier"),
    "selection.setPieceWeight": ("SetPieceWeight", "multiplier"),
    "selection.setCompletionWeight": ("SetCompletionWeight", "multiplier"),
    "materials.lesser.shardChance": ("LesserShardChance", "probability"),
    "materials.lesser.shards": ("LesserShards", "shards"),
    "materials.normal.shardChance": ("NormalShardChance", "probability"),
    "materials.normal.shardMin": ("NormalShardMin", "shards"),
    "materials.normal.shardMax": ("NormalShardMax", "shards"),
    "materials.miniBoss.shardMin": ("MiniBossShardMin", "shards"),
    "materials.miniBoss.shardMax": ("MiniBossShardMax", "shards"),
    "materials.miniBoss.tuningChance": ("MiniBossTuningChance", "probability"),
    "materials.miniBoss.tuning": ("MiniBossTuning", "tuning"),
    "materials.boss.shardMin": ("BossShardMin", "shards"),
    "materials.boss.shardMax": ("BossShardMax", "shards"),
    "materials.boss.tuning": ("BossTuning", "tuning"),
    "variantAdditiveShards": ("VariantAdditiveShards", "shards"),
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, old, path="loot"):
    if isinstance(old, dict):
        if type(data) is not dict or set(data) != set(old):
            raise ValueError(f"{path}: expected exactly {', '.join(old)}")
        for key in old:
            _shape(data[key], old[key], f"{path}.{key}")
    elif isinstance(old, list):
        if type(data) is not list or len(data) != len(old):
            raise ValueError(f"{path}: expected exactly five rarity weights")
        for i, value in enumerate(data):
            _shape(value, old[i], f"{path}[{i}]")
    elif isinstance(old, Decimal):
        if type(data) not in (int, Decimal) or not Decimal(data).is_finite() or not 0 <= data <= 1000000:
            raise ValueError(f"{path}: expected finite nonnegative decimal <= 1000000")
        if not math.isfinite(float(data)) or (data != 0 and float(data) == 0):
            raise ValueError(f"{path}: must be representable as finite nonzero Double")
        if Decimal(repr(float(data))) != Decimal(data):
            raise ValueError(f"{path}: precision cannot round-trip through a Double literal")
    elif type(data) is not int or not 0 <= data <= INT_MAX:
        raise ValueError(f"{path}: expected nonnegative Int32, not boolean")


def validate(data):
    _shape(data, LEGACY)
    if data["schemaVersion"] != 1:
        raise ValueError("loot.schemaVersion: expected integer 1")
    for path, (_, unit) in FIELDS.items():
        if unit == "probability" and _get(data, path) > 1:
            raise ValueError(f"loot.{path}: probability must be in 0..1")
    if sum(data["rarityWeights"][1:4]) == 0:
        raise ValueError("loot.rarityWeights: Uncommon..Epic must have positive total weight (boss floor and non-legendary draws)")
    for tier in ("normal", "miniBoss", "boss"):
        material = data["materials"][tier]
        if material["shardMin"] > material["shardMax"]:
            raise ValueError(f"loot.materials.{tier}: shardMin must not exceed shardMax")
    selection = data["selection"]
    if any(value < 1 for value in selection.values()):
        raise ValueError("loot.selection: all selection multipliers must be positive")
    if math.prod(selection.values()) * 1418 > INT_MAX:
        raise ValueError("loot.selection: current unique candidate total would exceed Int32")
    if selection["focusWeight"] * (600 + 360 * 12) > INT_MAX:
        raise ValueError("loot.selection.focusWeight: current base/named candidate total would exceed Int32")
    return data


def load(path=PATH):
    return validate(read_json(path))


def _literal(value, old):
    return repr(float(value)) if isinstance(old, Decimal) else str(value)


def fingerprint_record(data):
    validate(data)
    records = []
    for path, (_, unit) in FIELDS.items():
        value, old = _get(data, path), _get(LEGACY, path)
        if isinstance(old, list):
            if value != old:
                records.append(f"{path}:int[]:{unit}:[{','.join(map(str, value))}]")
        elif _literal(value, old) != _literal(old, old):
            kind = "double" if isinstance(old, Decimal) else "int"
            records.append(f"{path.replace('.', '/')}:{kind}:{unit}:{_literal(value, old)}")
    return "balance:loot:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />", "// Source: tools/balance/loot.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class LootBalance", "    {"]
    for path, (name, _) in FIELDS.items():
        value, old = _get(data, path), _get(LEGACY, path)
        if isinstance(old, list):
            lines.append(f"        internal static readonly int[] {name} = {{ " + ", ".join(map(str, value)) + " };")
        else:
            kind = "double" if isinstance(old, Decimal) else "int"
            lines.append(f"        internal const {kind} {name} = {_literal(value, old)};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}
