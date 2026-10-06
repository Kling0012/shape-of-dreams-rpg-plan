"""Compile daily-dream numeric definitions without publishing generated files."""
from decimal import Decimal
import json
import math
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/daily-dream.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/DailyDream.Generated.cs"
# Frozen compatibility reference, never an editable source or runtime fallback.
LEGACY = json.loads(r'''{
  "schemaVersion": 1,
  "powerBoostPct": 50,
  "definitions": {
    "4": {
      "shardMult": 1.5
    },
    "5": {
      "dropBonus": 0.25
    },
    "6": {
      "nightmareMult": 2.0
    },
    "7": {
      "xpMult": 1.3
    },
    "8": {
      "bountyMult": 2.0
    },
    "13": {
      "xpMult": 1.5
    },
    "14": {
      "bountyMult": 1.5,
      "shardMult": 1.25
    },
    "21": {
      "dropBonus": 0.2,
      "shardMult": 1.2
    },
    "22": {
      "nightmareMult": 1.5,
      "xpMult": 1.2
    },
    "23": {
      "dropBonus": 0.35
    },
    "24": {
      "xpMult": 1.3,
      "bountyMult": 1.5
    },
    "25": {
      "shardMult": 1.6
    },
    "26": {
      "bountyMult": 1.5,
      "dropBonus": 0.1
    },
    "30": {
      "nightmareMult": 1.5,
      "dropBonus": 0.25
    },
    "46": {
      "shardMult": 1.4,
      "dropBonus": 0.15
    },
    "47": {
      "xpMult": 1.4,
      "nightmareMult": 1.25
    },
    "48": {
      "bountyMult": 1.75,
      "xpMult": 1.15
    },
    "49": {
      "dropBonus": 0.3,
      "xpMult": 1.2
    },
    "50": {
      "nightmareMult": 2.0,
      "dropBonus": 0.2
    },
    "51": {
      "shardMult": 1.5,
      "bountyMult": 1.25
    },
    "52": {
      "dropBonus": 0.15
    },
    "53": {
      "xpMult": 1.25
    },
    "54": {
      "nightmareMult": 1.5
    },
    "57": {
      "shardMult": 1.45,
      "dropBonus": 0.1
    },
    "60": {
      "xpMult": 1.3,
      "shardMult": 1.3
    }
  }
}''', parse_float=Decimal)
# Closed numeric schema: names and enum/definition relationships stay in Core.
FIELDS = {
    'powerBoostPct': ('PowerBoostPct', 'int', 'percent'),
    'definitions.4.shardMult': ('Day4ShardMult', 'double', 'multiplier'),
    'definitions.5.dropBonus': ('Day5DropBonus', 'double', 'ratio'),
    'definitions.6.nightmareMult': ('Day6NightmareMult', 'double', 'multiplier'),
    'definitions.7.xpMult': ('Day7XpMult', 'double', 'multiplier'),
    'definitions.8.bountyMult': ('Day8BountyMult', 'double', 'multiplier'),
    'definitions.13.xpMult': ('Day13XpMult', 'double', 'multiplier'),
    'definitions.14.bountyMult': ('Day14BountyMult', 'double', 'multiplier'),
    'definitions.14.shardMult': ('Day14ShardMult', 'double', 'multiplier'),
    'definitions.21.dropBonus': ('Day21DropBonus', 'double', 'ratio'),
    'definitions.21.shardMult': ('Day21ShardMult', 'double', 'multiplier'),
    'definitions.22.nightmareMult': ('Day22NightmareMult', 'double', 'multiplier'),
    'definitions.22.xpMult': ('Day22XpMult', 'double', 'multiplier'),
    'definitions.23.dropBonus': ('Day23DropBonus', 'double', 'ratio'),
    'definitions.24.xpMult': ('Day24XpMult', 'double', 'multiplier'),
    'definitions.24.bountyMult': ('Day24BountyMult', 'double', 'multiplier'),
    'definitions.25.shardMult': ('Day25ShardMult', 'double', 'multiplier'),
    'definitions.26.bountyMult': ('Day26BountyMult', 'double', 'multiplier'),
    'definitions.26.dropBonus': ('Day26DropBonus', 'double', 'ratio'),
    'definitions.30.nightmareMult': ('Day30NightmareMult', 'double', 'multiplier'),
    'definitions.30.dropBonus': ('Day30DropBonus', 'double', 'ratio'),
    'definitions.46.shardMult': ('Day46ShardMult', 'double', 'multiplier'),
    'definitions.46.dropBonus': ('Day46DropBonus', 'double', 'ratio'),
    'definitions.47.xpMult': ('Day47XpMult', 'double', 'multiplier'),
    'definitions.47.nightmareMult': ('Day47NightmareMult', 'double', 'multiplier'),
    'definitions.48.bountyMult': ('Day48BountyMult', 'double', 'multiplier'),
    'definitions.48.xpMult': ('Day48XpMult', 'double', 'multiplier'),
    'definitions.49.dropBonus': ('Day49DropBonus', 'double', 'ratio'),
    'definitions.49.xpMult': ('Day49XpMult', 'double', 'multiplier'),
    'definitions.50.nightmareMult': ('Day50NightmareMult', 'double', 'multiplier'),
    'definitions.50.dropBonus': ('Day50DropBonus', 'double', 'ratio'),
    'definitions.51.shardMult': ('Day51ShardMult', 'double', 'multiplier'),
    'definitions.51.bountyMult': ('Day51BountyMult', 'double', 'multiplier'),
    'definitions.52.dropBonus': ('Day52DropBonus', 'double', 'ratio'),
    'definitions.53.xpMult': ('Day53XpMult', 'double', 'multiplier'),
    'definitions.54.nightmareMult': ('Day54NightmareMult', 'double', 'multiplier'),
    'definitions.57.shardMult': ('Day57ShardMult', 'double', 'multiplier'),
    'definitions.57.dropBonus': ('Day57DropBonus', 'double', 'ratio'),
    'definitions.60.xpMult': ('Day60XpMult', 'double', 'multiplier'),
    'definitions.60.shardMult': ('Day60ShardMult', 'double', 'multiplier'),
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="daily-dream"):
    if isinstance(reference, dict):
        if type(data) is not dict or set(data) != set(reference):
            raise ValueError(f"{path}: expected exactly {', '.join(reference)}")
        for key in reference:
            _shape(data[key], reference[key], f"{path}.{key}")
    elif isinstance(data, bool) or not isinstance(data, (int, Decimal)) or not Decimal(data).is_finite():
        raise ValueError(f"{path}: expected a finite number (not boolean)")


def validate(data):
    _shape(data, LEGACY)
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1:
        raise ValueError("daily-dream.schemaVersion: expected integer 1")
    for path, (_, kind, _) in FIELDS.items():
        value = _get(data, path)
        if kind == "int":
            if type(value) is not int or not 0 <= value <= 2147483647:
                raise ValueError(f"daily-dream.{path}: expected nonnegative Int32 (not boolean)")
        else:
            try:
                adopted = float(value)
            except OverflowError as exc:
                raise ValueError(f"daily-dream.{path}: exceeds double range") from exc
            if value < 0 or not math.isfinite(adopted) or (value != 0 and adopted == 0):
                raise ValueError(f"daily-dream.{path}: expected a nonnegative finite representable double")
            if Decimal(repr(adopted)) != Decimal(value):
                raise ValueError(f"daily-dream.{path}: double precision would change the authored value")

    return data


def load(path=PATH):
    return validate(read_json(path))


def _literal(value):
    return format(Decimal(value), "f").rstrip("0").rstrip(".") if Decimal(value) != Decimal(value).to_integral_value() else str(int(value))


def fingerprint_record(data):
    validate(data)
    records = []
    for path, (_, kind, unit) in FIELDS.items():
        value = _get(data, path)
        if value != _get(LEGACY, path):
            literal = _literal(value)
            if kind == "double":
                # The fingerprint describes the adopted IEEE double, not JSON spelling.
                literal = repr(float(value))
                if float(value) == float(_get(LEGACY, path)):
                    continue
            records.append(f"{path.replace('.', '/')}:{kind}:{unit}:{literal}")
    return "balance:daily-dream:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/daily-dream.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class DailyDreamBalance", "    {"]
    for path, (name, kind, _) in FIELDS.items():
        literal = _literal(_get(data, path))
        if kind == "double":
            literal += "d"
        lines.append(f"        internal const {kind} {name} = {literal};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}
